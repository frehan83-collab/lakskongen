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
    /// <param name="FrostSmoke01">Frost smoke (sea smoke, steam fog) over the open water:
    /// 0 none, 1 a dense layer (G3-05, <see cref="AtmosphereModel.FrostSmoke"/>).</param>
    public readonly record struct AtmosphereConditions(
        double VisibilityKm,
        double CloudCover01,
        double PrecipitationMmPerHour,
        bool Snow,
        double SeaFog01,
        double AirTemperatureC,
        double FrostSmoke01 = 0.0);

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
    /// Frost smoke (sea smoke) is a layer on the water a few to some tens of
    /// metres deep: it does not change the visibility at the eye heights the
    /// fjord is seen from, so it only reports how dense it is.
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
        // Frost smoke (G3-05). Saunders (1964, QJRMS 90, "Sea smoke and steam
        // fog"): water steams once the air over it is 5-10 K colder than the
        // water, and the smoke thickens and deepens as the contrast grows
        // (layers of 10 m and more past about 15 K). A fjord's frost smoke is
        // land air: on a clear, calm winter night the valleys cool by
        // radiation and the cold air drains out over the water, well below
        // the maritime estimate of AirTemperatureC. How deep that day's
        // outbreak runs comes from the day's hash; the north's valleys run
        // colder (mean January minima: Bergen about -1 C, Bodø -5, Tromsø -6).
        private const double FrostSmokeOnsetK = 10.0;
        private const double FrostSmokeDenseK = 20.0;
        private const double ColdOutbreakK60N = 10.0;
        private const double ColdOutbreakKPerDegree = 0.6;
        private const double FrostSmokeCalmMs = 2.0;
        private const double FrostSmokeMaxWindMs = 6.0;
        private const double OutbreakClearCover = 0.35;
        private const double OutbreakCloudyCover = 0.75;

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

            double cloudCover = Math.Clamp(cloud, 0.0, 1.0);
            double frostSmoke = FrostSmoke(state, windMs, seaTempC, airTemp, cloudCover, precip, seaFog, simDay, doy, latitudeDeg);
            return new AtmosphereConditions(
                KoschmiederConstant / sigma, cloudCover, precip, snow, seaFog, airTemp, frostSmoke);
        }

        /// <summary>
        /// Frost smoke over the open water (0-1): the water-air contrast against
        /// the land air of a cold-air outbreak, in calm, dry weather. The
        /// outbreak needs a clear sky (radiative cooling) and the cold season
        /// (the winter weight of <see cref="AirTemperatureC"/>); its depth on
        /// the day is <see cref="ColdOutbreakMaxK"/> times <c>Hash01(day, 6)</c>.
        /// </summary>
        public static double FrostSmoke(
            WeatherState state, double windMs, double seaTempC, double airTempC, double cloudCover01,
            double precipitationMmPerHour, double seaFog01, int simDay, int dayOfYear, double latitudeDeg)
        {
            if (state >= WeatherState.Storm || precipitationMmPerHour > 0.0 || seaFog01 > 0.0)
                return 0.0;
            double winter = WinterWeight(dayOfYear);
            double clear = 1.0 - SmoothStep(OutbreakClearCover, OutbreakCloudyCover, cloudCover01);
            double outbreak = ColdOutbreakMaxK(latitudeDeg) * Hash01(simDay, 6) * winter * clear;
            double contrast = seaTempC - (airTempC - outbreak);
            double calm = 1.0 - SmoothStep(FrostSmokeCalmMs, FrostSmokeMaxWindMs, windMs);
            return SmoothStep(FrostSmokeOnsetK, FrostSmokeDenseK, contrast) * calm;
        }

        /// <summary>Deepest cold-air outbreak (K below the maritime air estimate) at a latitude.</summary>
        public static double ColdOutbreakMaxK(double latitudeDeg)
            => ColdOutbreakK60N + ColdOutbreakKPerDegree * Math.Max(latitudeDeg - 60.0, 0.0);

        /// <summary>0 in midsummer, 1 at the coldest time of year (about 20 January).</summary>
        public static double WinterWeight(int dayOfYear)
            => 0.5 * (1.0 + Math.Cos(2.0 * Math.PI * (dayOfYear - 20) / SolarGeometry.DaysPerYear));

        private static double SmoothStep(double edge0, double edge1, double x)
        {
            double t = Math.Clamp((x - edge0) / (edge1 - edge0), 0.0, 1.0);
            return t * t * (3.0 - 2.0 * t);
        }

        /// <summary>
        /// Air temperature over the water: colder than the sea in winter (more so
        /// further north), about 2 °C warmer in summer.
        /// </summary>
        public static double AirTemperatureC(double seaTempC, int dayOfYear, double latitudeDeg)
        {
            double winter = WinterWeight(dayOfYear);
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
