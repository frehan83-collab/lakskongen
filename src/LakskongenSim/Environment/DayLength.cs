using System;

namespace LakskongenSim.Environment
{
    /// <summary>
    /// Astronomical day length (sunrise to sunset) at a latitude on a day of the
    /// year: the light a sea pen gets (roadmap S2-03, the photoperiod input of the
    /// growth law). Solar declination after Cooper (1969),
    /// δ = 23.44° × sin(2π (284 + n) / 365); sunrise and sunset at a solar
    /// altitude of −0.833° (refraction and the sun's radius, NOAA). Pure.
    /// </summary>
    public static class DayLength
    {
        /// <summary>Earth's axial tilt (°).</summary>
        private const double AxialTiltDeg = 23.44;
        /// <summary>Solar altitude at sunrise and sunset: refraction plus the solar radius (°).</summary>
        private const double HorizonAltitudeDeg = -0.833;
        private const double HoursPerDay = 24.0;
        private const double DegreesPerHour = 15.0;
        private const int DaysPerYear = 365;

        /// <summary>
        /// Hours from sunrise to sunset at <paramref name="latitudeDeg"/> (°N) on
        /// day <paramref name="dayOfYear"/> (0 = 1 January): 0 in polar night,
        /// 24 under the midnight sun.
        /// </summary>
        public static double Hours(double latitudeDeg, int dayOfYear)
        {
            int n = ((dayOfYear % DaysPerYear) + DaysPerYear) % DaysPerYear + 1; // 1..365
            double declination = AxialTiltDeg * Math.Sin(2.0 * Math.PI * (284 + n) / DaysPerYear);
            double phi = latitudeDeg * Math.PI / 180.0;
            double delta = declination * Math.PI / 180.0;
            double h0 = HorizonAltitudeDeg * Math.PI / 180.0;
            double cosHourAngle = (Math.Sin(h0) - Math.Sin(phi) * Math.Sin(delta)) / (Math.Cos(phi) * Math.Cos(delta));
            if (cosHourAngle <= -1.0) return HoursPerDay;
            if (cosHourAngle >= 1.0) return 0.0;
            double hourAngleDeg = Math.Acos(cosHourAngle) * 180.0 / Math.PI;
            return 2.0 * hourAngleDeg / DegreesPerHour;
        }
    }
}
