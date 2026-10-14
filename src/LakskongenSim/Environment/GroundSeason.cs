using System;

namespace LakskongenSim.Environment
{
    /// <summary>
    /// The land's season on a day: where the snow lies and what the birch
    /// leaves are doing.
    /// </summary>
    /// <param name="SnowLineM">Altitude (m) above which gentle ground lies under snow; 0 means snow to the shore.</param>
    /// <param name="Foliage01">Birch leaf cover, 0 bare twigs to 1 full summer canopy.</param>
    /// <param name="Autumn01">How far the leaves have turned yellow, 0 green to 1 full autumn colour.</param>
    public readonly record struct GroundSeason(double SnowLineM, double Foliage01, double Autumn01);

    /// <summary>
    /// Seasonal ground state for the 3D fjord (G2-02): the date and latitude
    /// come from the sim calendar (D20), like the sky's.
    ///
    /// <para><b>Snow line.</b> Mid-month snow lines for the inner fjords of
    /// Vestland (60.4°N) and of Nordland/Lofoten (68°N), interpolated over
    /// the year with periodic cubic (Catmull-Rom) segments and linearly in
    /// latitude, clamped outside 60.4-68°N. The anchors are estimates, not a
    /// fit: the 0 °C level of the monthly mean air temperature at the coast
    /// (about +1 °C in January and +15 °C in July at Bergen, a 6.5 °C/km lapse
    /// rate) with a snowpack lag, so the line drops fast in autumn and climbs
    /// slowly through the melt; inner Vestland keeps snow down to the fjord in
    /// February and loses the last patches on the 1,000 m plateaus in July,
    /// Lofoten has snow to the sea from December to March.</para>
    ///
    /// <para><b>Birch.</b> Downy birch leaves out in mid-May in lowland
    /// Vestland and about three days later per degree north, turns yellow from
    /// late September (earlier in the north) and is bare by late October.</para>
    ///
    /// Pure functions of their arguments; the renderer reads them through
    /// <c>CelestialBridge.Sky</c> and the EnvironmentDirector.
    /// </summary>
    public static class GroundSeasonModel
    {
        /// <summary>Latitude of the southern anchor set (inner Vestland, Bergen).</summary>
        public const double SouthLatitudeDeg = 60.4;
        /// <summary>Latitude of the northern anchor set (Nordland, Lofoten).</summary>
        public const double NorthLatitudeDeg = 68.0;

        /// <summary>Mid-month snow line (m), January to December, inner Vestland.</summary>
        private static readonly double[] SouthSnowLineM =
            { 100, 60, 180, 450, 800, 1150, 1450, 1550, 1450, 1050, 550, 220 };
        /// <summary>Mid-month snow line (m), January to December, Nordland/Lofoten.</summary>
        private static readonly double[] NorthSnowLineM =
            { 0, 0, 0, 150, 450, 800, 1050, 1150, 950, 550, 220, 40 };

        /// <summary>Day of year of the middle of each month (non-leap year).</summary>
        private static readonly double[] MidMonthDay =
            { 16, 45.5, 75, 105.5, 136, 166.5, 197, 228, 258.5, 289, 319.5, 350 };

        /// <summary>Birch leaf-out (day of year) at the southern anchor, and its delay per degree north.</summary>
        public const double LeafOutDay = 135.0;
        public const double LeafOutDaysPerDegree = 3.0;
        /// <summary>Days from bud burst to a closed canopy.</summary>
        public const double LeafOutSpanDays = 18.0;
        /// <summary>Start of yellowing (day of year) at the southern anchor, and how much earlier per degree north.</summary>
        public const double YellowingDay = 268.0;
        public const double YellowingDaysPerDegree = 2.0;
        /// <summary>Days from the first yellow to full colour, and from full colour to bare twigs.</summary>
        public const double YellowingSpanDays = 14.0;
        public const double LeafFallSpanDays = 16.0;

        private const double DaysPerYear = SolarGeometry.DaysPerYear;

        /// <summary>The ground season at a latitude (deg N) on a (fractional) day of year (1 = 1 January).</summary>
        public static GroundSeason At(double latitudeDeg, double dayOfYear)
        {
            double foliage = Foliage(latitudeDeg, dayOfYear, out double autumn);
            return new GroundSeason(SnowLineM(latitudeDeg, dayOfYear), foliage, autumn);
        }

        /// <summary>Snow line (m) at a latitude (deg N) on a (fractional) day of year.</summary>
        public static double SnowLineM(double latitudeDeg, double dayOfYear)
        {
            double t = Math.Clamp((latitudeDeg - SouthLatitudeDeg) / (NorthLatitudeDeg - SouthLatitudeDeg), 0.0, 1.0);
            double south = Periodic(SouthSnowLineM, dayOfYear);
            double north = Periodic(NorthSnowLineM, dayOfYear);
            return Math.Max(0.0, south + (north - south) * t);
        }

        /// <summary>Birch leaf cover (0-1) and, through <paramref name="autumn"/>, its autumn colour (0-1).</summary>
        public static double Foliage(double latitudeDeg, double dayOfYear, out double autumn)
        {
            double north = Math.Max(0.0, latitudeDeg - SouthLatitudeDeg);
            double day = Wrap(dayOfYear);
            double leafOut = LeafOutDay + LeafOutDaysPerDegree * north;
            double yellow = YellowingDay - YellowingDaysPerDegree * north;
            double fullColour = yellow + YellowingSpanDays;
            double bare = fullColour + LeafFallSpanDays;
            double spring = SmoothStep(leafOut, leafOut + LeafOutSpanDays, day);
            double fall = 1.0 - SmoothStep(fullColour, bare, day);
            autumn = day < leafOut ? 0.0 : SmoothStep(yellow, fullColour, day) * (day <= bare ? 1.0 : 0.0);
            return day >= bare ? 0.0 : spring * fall;
        }

        /// <summary>Day of year wrapped into [1, 366).</summary>
        private static double Wrap(double dayOfYear) => ((dayOfYear - 1.0) % DaysPerYear + DaysPerYear) % DaysPerYear + 1.0;

        /// <summary>Periodic Catmull-Rom through the twelve mid-month anchors.</summary>
        private static double Periodic(double[] anchors, double dayOfYear)
        {
            double day = Wrap(dayOfYear);
            int count = anchors.Length;
            // The segment [k, k+1] that holds the day, wrapping December → January.
            int k = count - 1;
            for (int i = 0; i < count; i++)
            {
                if (day < MidMonthDay[i])
                {
                    k = i - 1;
                    break;
                }
            }
            double start = k >= 0 ? MidMonthDay[k] : MidMonthDay[count - 1] - DaysPerYear;
            int next = (k + 1) % count;
            double end = MidMonthDay[next] + (next == 0 && k >= 0 ? DaysPerYear : 0.0);
            double f = (day - start) / (end - start);
            int i1 = (k + count) % count;
            double p0 = anchors[(i1 - 1 + count) % count], p1 = anchors[i1];
            double p2 = anchors[(i1 + 1) % count], p3 = anchors[(i1 + 2) % count];
            double f2 = f * f, f3 = f2 * f;
            return 0.5 * (2.0 * p1 + (p2 - p0) * f + (2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3) * f2 + (3.0 * p1 - p0 - 3.0 * p2 + p3) * f3);
        }

        private static double SmoothStep(double edge0, double edge1, double x)
        {
            double t = Math.Clamp((x - edge0) / (edge1 - edge0), 0.0, 1.0);
            return t * t * (3.0 - 2.0 * t);
        }
    }
}
