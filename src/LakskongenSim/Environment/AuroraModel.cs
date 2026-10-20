using System;

namespace LakskongenSim.Environment
{
    /// <summary>
    /// How the northern lights stand over a latitude on a night.
    /// </summary>
    /// <param name="Kp">Planetary K index of the night (0-9).</param>
    /// <param name="Brightness01">Display brightness if the sky were dark and clear.</param>
    /// <param name="OvalOffsetKm">Ground distance from the observer to the brightest curtains, positive to the north.</param>
    public readonly record struct AuroraState(double Kp, double Brightness01, double OvalOffsetKm);

    /// <summary>
    /// The aurora for the 3D sky (G1-04). Geomagnetic activity is a
    /// deterministic, slowly varying Kp per sim day (value noise with knots
    /// every <see cref="KnotDays"/> days, exponentially distributed with mean
    /// 1.8, so Kp ≥ 5 on about 6 % of nights). The auroral oval's equatorward
    /// edge sits near 66.5° − 2.05·Kp geomagnetic latitude (the NOAA SWPC
    /// view-line relation), the bright arc about 2.5° poleward of it, and
    /// Norwegian geomagnetic latitudes run about 3° below the geographic ones
    /// (Tromsø 69.6°N geographic, about 66.6° geomagnetic). Darkness and a
    /// clear sky are the renderer's job: this only says how strong and where.
    /// </summary>
    public static class AuroraModel
    {
        /// <summary>Days between independent activity knots.</summary>
        public const int KnotDays = 3;

        private const double MeanKp = 1.8;
        private const double GeomagneticOffsetDeg = 3.0;
        private const double QuietBoundaryDeg = 66.5;
        private const double BoundaryPerKp = 2.05;
        private const double ArcPolewardDeg = 2.5;
        private const double KmPerDegree = 111.2;
        private const double OvalWidthDeg = 3.0;
        private const double OvalWidthPerKp = 0.7;
        private const double InsideOvalSpread = 0.35;
        /// <summary>An arc further away than this sits on the horizon, out of sight.</summary>
        public const double VisibleRangeKm = 900.0;

        /// <summary>Kp for a fractional sim day: smooth between the knots.</summary>
        public static double Kp(double simDay)
        {
            double k = simDay / KnotDays;
            int i = (int)Math.Floor(k);
            double f = k - i;
            double s = f * f * (3.0 - 2.0 * f);
            return KnotKp(i) * (1.0 - s) + KnotKp(i + 1) * s;
        }

        private static double KnotKp(int knot)
        {
            double u = AtmosphereModel.Hash01(knot, 7919);
            return Math.Min(9.0, -MeanKp * Math.Log(1.0 - 0.9999 * u));
        }

        /// <summary>Geomagnetic latitude (approximate, for Norway) of a geographic latitude.</summary>
        public static double GeomagneticLatitudeDeg(double latitudeDeg) => latitudeDeg - GeomagneticOffsetDeg;

        /// <summary>The display seen from a latitude on a fractional sim day.</summary>
        public static AuroraState At(double latitudeDeg, double simDay)
        {
            double kp = Kp(simDay);
            double arcLat = QuietBoundaryDeg - BoundaryPerKp * kp + ArcPolewardDeg;
            double offsetKm = (arcLat - GeomagneticLatitudeDeg(latitudeDeg)) * KmPerDegree;
            // The oval reaches this far poleward of its bright arc, wider in storms.
            double widthKm = (OvalWidthDeg + OvalWidthPerKp * kp) * KmPerDegree;
            // Brighter with activity; a quiet night's arc is dimmer but plain to see.
            double strength = 0.45 + 0.55 * Smooth(0.5, 4.5, kp);
            double reach;
            double seenOffsetKm;
            if (offsetKm >= 0.0)
            {
                // The arc lies to the north: lower in the sky the further away.
                reach = 1.0 - Smooth(VisibleRangeKm * 0.45, VisibleRangeKm, offsetKm);
                seenOffsetKm = offsetKm;
            }
            else if (-offsetKm <= widthKm)
            {
                // Under the oval: curtains around the zenith.
                reach = 1.0;
                seenOffsetKm = offsetKm * InsideOvalSpread;
            }
            else
            {
                // Poleward of the oval (a quiet night seen from the far north):
                // its poleward edge glows low in the south.
                double beyond = -offsetKm - widthKm;
                reach = 1.0 - Smooth(VisibleRangeKm * 0.3, VisibleRangeKm * 0.8, beyond);
                seenOffsetKm = -beyond;
            }
            return new AuroraState(kp, Math.Clamp(strength * reach, 0.0, 1.0), seenOffsetKm);
        }

        private static double Smooth(double edge0, double edge1, double x)
        {
            double t = Math.Clamp((x - edge0) / (edge1 - edge0), 0.0, 1.0);
            return t * t * (3.0 - 2.0 * t);
        }
    }
}
