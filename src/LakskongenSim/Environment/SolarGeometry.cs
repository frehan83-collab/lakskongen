using System;

namespace LakskongenSim.Environment
{
    /// <summary>
    /// A body's place in the local sky: elevation above the horizon and
    /// azimuth from true north, clockwise (east = 90°, south = 180°).
    /// </summary>
    public readonly record struct HorizontalPosition(double ElevationDeg, double AzimuthDeg)
    {
        /// <summary>Unit vector toward the body in a local frame: north, up, east.</summary>
        public (double North, double Up, double East) Direction()
        {
            double e = SolarGeometry.Rad(ElevationDeg);
            double a = SolarGeometry.Rad(AzimuthDeg);
            return (Math.Cos(e) * Math.Cos(a), Math.Sin(e), Math.Cos(e) * Math.Sin(a));
        }
    }

    /// <summary>The moon's place, the lit fraction of its disc and whether it is waxing.</summary>
    public readonly record struct MoonState(HorizontalPosition Position, double IlluminatedFraction, bool Waxing);

    /// <summary>
    /// Sun and moon positions for the 3D sky (G1-04, D20): latitude and the
    /// date come from the sim calendar, the hour from the presentation clock.
    ///
    /// The hour is local apparent solar time (12:00 = the sun due south), so
    /// no longitude, time zone or equation of time is needed. Formulas:
    /// <list type="bullet">
    /// <item>declination: Spencer (1971) Fourier series, error under 0.04°;</item>
    /// <item>elevation and azimuth: the spherical-astronomy relations
    /// sin e = sin φ sin δ + cos φ cos δ cos h;</item>
    /// <item>moon: the low-precision lunar series of the Astronomical Almanac
    /// (about 0.3°), placed relative to the sun by right ascension so the
    /// hour stays solar time; sim day 0 is 1 January 2026.</item>
    /// </list>
    /// Elevations are geometric (no refraction), like the sky the shader draws.
    /// Pure functions of their arguments: no state, no randomness.
    /// </summary>
    public static class SolarGeometry
    {
        /// <summary>Days in the sim's calendar year (sim day 0 = 1 January).</summary>
        public const int DaysPerYear = 365;

        /// <summary>
        /// Days from J2000.0 (2000-01-01 12:00) to sim day 0 (2026-01-01 00:00):
        /// 26 years with 7 leap days, less half a day.
        /// </summary>
        public const double SimEpochDaysSinceJ2000 = 9496.5;

        private const double DegPerHour = 15.0;

        internal static double Rad(double deg) => deg * Math.PI / 180.0;
        internal static double Deg(double rad) => rad * 180.0 / Math.PI;

        /// <summary>Degrees in one full turn.</summary>
        internal const double FullTurnDegrees = 360.0;

        /// <summary>An angle in degrees wrapped into [0, 360) (geometry, not calendar arithmetic).</summary>
        internal static double WrapDegrees(double degrees)
            => degrees - FullTurnDegrees * Math.Floor(degrees / FullTurnDegrees);

        /// <summary>Day of year (1 = 1 January) for a sim day (0 = 1 January).</summary>
        public static int DayOfYear(int simDay) => ((simDay % DaysPerYear) + DaysPerYear) % DaysPerYear + 1;

        /// <summary>Solar declination in degrees for a (fractional) day of year.</summary>
        public static double DeclinationDeg(double dayOfYear)
        {
            double g = 2.0 * Math.PI * (dayOfYear - 1.0) / DaysPerYear;
            double d = 0.006918
                - 0.399912 * Math.Cos(g) + 0.070257 * Math.Sin(g)
                - 0.006758 * Math.Cos(2 * g) + 0.000907 * Math.Sin(2 * g)
                - 0.002697 * Math.Cos(3 * g) + 0.001480 * Math.Sin(3 * g);
            return Deg(d);
        }

        /// <summary>
        /// The sun at a latitude (degrees north), day of year and local solar
        /// hour (0-24). The day advances with the hour: 18:00 is day + 0.75.
        /// </summary>
        public static HorizontalPosition Sun(double latitudeDeg, double dayOfYear, double solarHour)
        {
            double declination = DeclinationDeg(dayOfYear + solarHour / 24.0);
            return ToHorizontal(latitudeDeg, declination, DegPerHour * (solarHour - 12.0));
        }

        /// <summary>The sun's elevation at solar noon (its daily maximum).</summary>
        public static double NoonElevationDeg(double latitudeDeg, double dayOfYear)
            => Sun(latitudeDeg, dayOfYear, 12.0).ElevationDeg;

        /// <summary>The sun's elevation at solar midnight (its daily minimum).</summary>
        public static double MidnightElevationDeg(double latitudeDeg, double dayOfYear)
            => Sun(latitudeDeg, dayOfYear, 0.0).ElevationDeg;

        /// <summary>
        /// Elevation and azimuth for a declination and hour angle (degrees,
        /// 0 at the meridian, positive in the afternoon).
        /// </summary>
        public static HorizontalPosition ToHorizontal(double latitudeDeg, double declinationDeg, double hourAngleDeg)
        {
            double phi = Rad(latitudeDeg), dec = Rad(declinationDeg), h = Rad(hourAngleDeg);
            double sinE = Math.Sin(phi) * Math.Sin(dec) + Math.Cos(phi) * Math.Cos(dec) * Math.Cos(h);
            double elevation = Deg(Math.Asin(Math.Clamp(sinE, -1.0, 1.0)));
            // Azimuth from the south, westward positive, then turned to north-based.
            double fromSouth = Math.Atan2(Math.Sin(h), Math.Cos(h) * Math.Sin(phi) - Math.Tan(dec) * Math.Cos(phi));
            double azimuth = WrapDegrees(Deg(fromSouth) + 180.0);
            if (azimuth < 0) azimuth += 360.0;
            return new HorizontalPosition(elevation, azimuth);
        }

        /// <summary>
        /// The moon for a fractional sim day (0 = 1 January 2026 00:00) at a
        /// latitude and local solar hour. Only its relation to the sun is
        /// used, so the hour stays solar time.
        /// </summary>
        public static MoonState Moon(double latitudeDeg, double simDay, double solarHour)
        {
            double d = SimEpochDaysSinceJ2000 + simDay + solarHour / 24.0;
            double t = d / 36525.0;
            double obliquity = Rad(23.439 - 0.0000004 * d);

            // Low-precision sun (ecliptic longitude) on the same instant.
            double meanLong = 280.460 + 0.9856474 * d;
            double anomaly = Rad(357.528 + 0.9856003 * d);
            double sunLong = meanLong + 1.915 * Math.Sin(anomaly) + 0.020 * Math.Sin(2 * anomaly);

            double moonLong = 218.32 + 481267.881 * t
                + 6.29 * SinDeg(135.0 + 477198.87 * t)
                - 1.27 * SinDeg(259.3 - 413335.36 * t)
                + 0.66 * SinDeg(235.7 + 890534.22 * t)
                + 0.21 * SinDeg(269.9 + 954397.74 * t)
                - 0.19 * SinDeg(357.5 + 35999.05 * t)
                - 0.11 * SinDeg(186.5 + 966404.03 * t);
            double moonLat = 5.13 * SinDeg(93.3 + 483202.02 * t)
                + 0.28 * SinDeg(228.2 + 960400.89 * t)
                - 0.28 * SinDeg(318.3 + 6003.15 * t)
                - 0.17 * SinDeg(217.6 - 407332.21 * t);

            var (sunRa, _) = EclipticToEquatorial(Rad(sunLong), 0.0, obliquity);
            var (moonRa, moonDec) = EclipticToEquatorial(Rad(moonLong), Rad(moonLat), obliquity);

            // Hour angle of the moon = the sun's hour angle + (RA sun - RA moon).
            double sunHourAngle = DegPerHour * (solarHour - 12.0);
            double hourAngle = sunHourAngle + Deg(sunRa - moonRa);
            var position = ToHorizontal(latitudeDeg, Deg(moonDec), hourAngle);

            double elongationCos = Math.Cos(Rad(moonLat)) * Math.Cos(Rad(moonLong - sunLong));
            double illuminated = Math.Clamp((1.0 - elongationCos) * 0.5, 0.0, 1.0);
            double ahead = WrapDegrees(moonLong - sunLong);
            return new MoonState(position, illuminated, ahead < 180.0);
        }

        private static double SinDeg(double deg) => Math.Sin(Rad(deg));

        private static (double Ra, double Dec) EclipticToEquatorial(double longitude, double latitude, double obliquity)
        {
            double x = Math.Cos(latitude) * Math.Cos(longitude);
            double y = Math.Cos(latitude) * Math.Sin(longitude);
            double z = Math.Sin(latitude);
            double ye = y * Math.Cos(obliquity) - z * Math.Sin(obliquity);
            double ze = y * Math.Sin(obliquity) + z * Math.Cos(obliquity);
            return (Math.Atan2(ye, x), Math.Asin(Math.Clamp(ze, -1.0, 1.0)));
        }
    }
}
