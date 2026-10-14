using System;

namespace LakskongenSim.Environment
{
    /// <summary>
    /// What the sky looks like on a sim day: horizontal visibility (the
    /// meteorological optical range), cloud cover, precipitation and sea fog.
    /// </summary>
    /// <param name="VisibilityKm">Meteorological optical range at sea level (km).</param>
    /// <param name="CloudCover01">Fraction of the sky covered by cloud.</param>
    /// <param name="PrecipitationMmPerHour">Rain or snow (water equivalent) rate.</param>
    /// <param name="Snow">True when the precipitation falls as snow.</param>
    /// <param name="SeaFog01">1 on an advection sea-fog day (a low layer over the water).</param>
    /// <param name="AirTemperatureC">Estimated 2 m air temperature over the water.</param>
    public readonly record struct AtmosphereConditions(
        double VisibilityKm,
        double CloudCover01,
        double PrecipitationMmPerHour,
        bool Snow,
        double SeaFog01,
        double AirTemperatureC);

    /// <summary>
    /// Derives the visible atmosphere (G1-02) from the sim's weather regime,
    /// wind, sea temperature, date and latitude. Pure: no saved state and no
    /// RNG stream, so it changes no save hash and no other system's draws;
    /// the day-to-day variety inside a regime comes from a hash of the sim day.
    ///
    /// Visibility is Koschmieder's: V = 3.912 / σ (2 % contrast threshold),
    /// with σ the sum of the extinction coefficients (km⁻¹) of
    /// <list type="bullet">
    /// <item>clean maritime air: 60 km in calm weather, 25 km moderate, 18 km in storms;</item>
    /// <item>rain: σ = a·R^0.67 (R in mm/h). a = 0.30 for showers, inside the
    /// 0.25-0.32 Atlas (1953) / Miers (1983) range for widespread rain, and
    /// 0.60 for storms, between that range and their orographic 1.25-2.6,
    /// because fjord walls force storm rain orographically;</item>
    /// <item>snow: σ = 2.0·S^0.7, about 1.5 km at 1 mm/h water equivalent;</item>
    /// <item>sea spray above 14 m/s: σ = 0.01·(U − 14)²;</item>
    /// <item>advection sea fog: 250-750 m visibility.</item>
    /// </list>
    /// </summary>
    public static class AtmosphereModel
    {
        /// <summary>ln(1/0.02): extinction × visibility at the 2 % contrast threshold.</summary>
        public const double KoschmiederConstant = 3.912;

        private const double CalmAirVisibilityKm = 60.0;
        private const double ModerateAirVisibilityKm = 25.0;
        private const double StormAirVisibilityKm = 18.0;
        private const double ShowerRainCoefficient = 0.30;
        private const double StormRainCoefficient = 0.60;
        private const double RainExponent = 0.67;
        private const double SnowCoefficient = 2.0;
        private const double SnowExponent = 0.7;
        private const double SprayThresholdMs = 14.0;
        private const double SprayCoefficient = 0.01;
        private const double SnowBelowAirC = 1.0;
        private const double SeaFogPeakChance = 0.18;

        /// <summary>Extinction coefficient (km⁻¹) for a visibility (km).</summary>
        public static double ExtinctionPerKm(double visibilityKm)
            => KoschmiederConstant / Math.Max(visibilityKm, 1e-3);

        /// <summary>The atmosphere for a weather regime on a sim day.</summary>
        public static AtmosphereConditions Derive(
            WeatherState state, double windMs, double seaTempC, int simDay, double latitudeDeg)
        {
            int doy = SolarGeometry.DayOfYear(simDay);
            double airTemp = AirTemperatureC(seaTempC, doy, latitudeDeg);
            double showers = Hash01(simDay, 1);
            double rate = Hash01(simDay, 2);
            double cover = Hash01(simDay, 3);

            double precip = state switch
            {
                WeatherState.Calm => 0.0,
                WeatherState.Moderate => showers < 0.45 ? 0.3 + 1.2 * rate : 0.0,
                WeatherState.Storm => 4.0 + 6.0 * rate,
                _ => 12.0 + 8.0 * rate,
            };
            bool snow = precip > 0.0 && airTemp < SnowBelowAirC;
            if (snow && state == WeatherState.Moderate)
                precip *= 0.4; // snow showers: the water-equivalent rate is lower

            double cloud = state switch
            {
                WeatherState.Calm => 0.05 + 0.35 * cover,
                WeatherState.Moderate => precip > 0.0 ? 0.88 + 0.1 * cover : 0.45 + 0.4 * cover,
                WeatherState.Storm => 0.97,
                _ => 1.0,
            };

            double sigma = KoschmiederConstant / (state switch
            {
                WeatherState.Calm => CalmAirVisibilityKm,
                WeatherState.Moderate => ModerateAirVisibilityKm,
                _ => StormAirVisibilityKm,
            });
            if (precip > 0.0)
            {
                sigma += snow
                    ? SnowCoefficient * Math.Pow(precip, SnowExponent)
                    : (state >= WeatherState.Storm ? StormRainCoefficient : ShowerRainCoefficient) * Math.Pow(precip, RainExponent);
            }
            double spray = Math.Max(0.0, windMs - SprayThresholdMs);
            sigma += SprayCoefficient * spray * spray;

            double seaFog = 0.0;
            if (precip <= 0.0 && Hash01(simDay, 4) < SeaFogChance(state, windMs, doy))
            {
                seaFog = 1.0;
                sigma += KoschmiederConstant / (0.25 + 0.5 * Hash01(simDay, 5));
            }

            return new AtmosphereConditions(
                KoschmiederConstant / sigma, Math.Clamp(cloud, 0.0, 1.0), precip, snow, seaFog, airTemp);
        }

        /// <summary>
        /// Air temperature over the water: colder than the sea in winter (more so
        /// further north), about 2 °C warmer in summer.
        /// </summary>
        public static double AirTemperatureC(double seaTempC, int dayOfYear, double latitudeDeg)
        {
            double winter = 0.5 * (1.0 + Math.Cos(2.0 * Math.PI * (dayOfYear - 20) / SolarGeometry.DaysPerYear));
            double winterOffset = 3.5 + 0.12 * (latitudeDeg - 60.0);
            return seaTempC - winterOffset * winter + 2.0 * (1.0 - winter);
        }

        /// <summary>
        /// Daily chance of advection sea fog: warm air over cold water in late
        /// spring and summer, light winds, no front.
        /// </summary>
        public static double SeaFogChance(WeatherState state, double windMs, int dayOfYear)
        {
            if (state >= WeatherState.Storm || windMs > 8.0)
                return 0.0;
            // Peaks mid-June (day 165), nothing from October to March.
            double season = Math.Max(0.0, Math.Cos(2.0 * Math.PI * (dayOfYear - 165) / SolarGeometry.DaysPerYear));
            season = season * season;
            double regime = state == WeatherState.Calm ? 1.0 : 0.5;
            return SeaFogPeakChance * season * regime * (1.0 - windMs / 8.0 * 0.5);
        }

        /// <summary>Deterministic value in [0, 1) for a day and a salt (SplitMix64 finaliser).</summary>
        public static double Hash01(int simDay, int salt)
        {
            unchecked
            {
                ulong z = (ulong)(uint)simDay * 0x9E3779B97F4A7C15UL + (ulong)(uint)salt * 0xD1B54A32D192ED03UL + 0x632BE59BD9B4E019UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                z ^= z >> 31;
                return (z >> 11) * (1.0 / 9007199254740992.0);
            }
        }
    }
}
