using System;

namespace LakskongenSim.Environment.Geo
{
    /// <summary>
    /// The forward ETRS89 / UTM zone 33N projection (EPSG:25833), Norway's
    /// national map projection: GRS80, scale 0.9996 on the 15°E central
    /// meridian, false easting 500 000 m, Krüger series to n⁴ (Karney 2011).
    /// A double-precision port of <c>tests/test_geodata.py::utm33</c>, checked
    /// against the six pyproj points in <c>manifest.json</c>
    /// <c>projection_check_25833</c> to 1 cm (docs/geo/GEODATA.md §3).
    /// <para>
    /// <b>Display and validation only.</b> It calls the platform's libm
    /// (sinh, atanh, atan2), whose last bits can differ between machines, so a
    /// result never feeds game state: the sim's own geodata carries integer
    /// UTM coordinates from the pipeline.
    /// </para>
    /// </summary>
    public static class Utm33
    {
        private const double SemiMajorAxisM = 6378137.0;
        private const double Flattening = 1 / 298.257222101;
        private const double ScaleFactor = 0.9996;
        private const double CentralMeridianDeg = 15.0;
        private const double FalseEastingM = 500000.0;

        private static readonly double N = Flattening / (2 - Flattening);
        private static readonly double RectifyingRadius =
            SemiMajorAxisM / (1 + N) * (1 + N * N / 4 + Math.Pow(N, 4) / 64);
        private static readonly double Eccentricity = 2 * Math.Sqrt(N) / (1 + N);
        private static readonly double[] Alpha =
        {
            N / 2 - 2 * N * N / 3 + 5 * Math.Pow(N, 3) / 16 + 41 * Math.Pow(N, 4) / 180,
            13 * N * N / 48 - 3 * Math.Pow(N, 3) / 5 + 557 * Math.Pow(N, 4) / 1440,
            61 * Math.Pow(N, 3) / 240 - 103 * Math.Pow(N, 4) / 140,
            49561 * Math.Pow(N, 4) / 161280,
        };

        /// <summary>
        /// EPSG:25833 easting and northing (metres) of a longitude and latitude
        /// (degrees, ETRS89). Display and validation only; never game state.
        /// </summary>
        public static (double X, double Y) Forward(double lonDeg, double latDeg)
        {
            double phi = latDeg * Math.PI / 180.0;
            double lambda = (lonDeg - CentralMeridianDeg) * Math.PI / 180.0;
            double sinPhi = Math.Sin(phi);
            double t = Math.Sinh(Math.Atanh(sinPhi) - Eccentricity * Math.Atanh(Eccentricity * sinPhi));
            double xi = Math.Atan2(t, Math.Cos(lambda));
            double eta = Math.Atanh(Math.Sin(lambda) / Math.Sqrt(1 + t * t));
            double x = eta, y = xi;
            for (int j = 1; j <= Alpha.Length; j++)
            {
                x += Alpha[j - 1] * Math.Cos(2 * j * xi) * Math.Sinh(2 * j * eta);
                y += Alpha[j - 1] * Math.Sin(2 * j * xi) * Math.Cosh(2 * j * eta);
            }
            return (FalseEastingM + ScaleFactor * RectifyingRadius * x, ScaleFactor * RectifyingRadius * y);
        }
    }
}
