using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Calibration;
using LakskongenSim.Farm;

namespace LakskongenSim.Environment.Geo
{
    /// <summary>
    /// The geo half of the plot catalogue for one home production area
    /// (STAGE23_PLAN track A, A2; rulings OD1 and OD2): it binds the open-sea
    /// plot <see cref="BoundPlotId"/> to the Havbruk til havs (HTH) candidate
    /// nearest the home area's label point, and nothing else. hav_1 keeps its
    /// id, its Hs calibration, its lice band, its exposure and its name; only
    /// its display position (<see cref="ReferenceX"/>, a compressed Z) and its
    /// water depth (the candidate's) change. off_1 and off_2 are coastal plots
    /// (5 and 12 km, Hs100 5.0 and 6.5 m) and stay procedural, so the lens
    /// never shows a 70 km open-ocean area with a coastal Hs100.
    /// <para>
    /// The nearest rule is pure integer arithmetic: the minimal squared
    /// EPSG:25833 distance from the area's <see cref="GeoProductionArea.Label25833"/>
    /// to a candidate's x/y, ties to the higher score, then the lower rank,
    /// then the ordinal id. The distance in km uses <see cref="Math.Sqrt"/>,
    /// which IEEE 754 rounds exactly on every platform. Areas with no HTH
    /// candidate of their own (PO1, 4, 5, 7, 9, 10) bind the nearest one in a
    /// neighbouring area. No land pad is bound (<see cref="LandPadSlots"/> is
    /// empty, <see cref="IntakeReachM"/> 0), and TerrainGenerator is not
    /// chained in: the pads keep the file's positions and their intake of 0.
    /// </para>
    /// </summary>
    public sealed class GeoSitePlotGeometry : ISitePlotGeometry
    {
        /// <summary>The one plot this geometry binds: the open-sea plot.</summary>
        public const string BoundPlotId = "hav_1";

        /// <summary>
        /// hav_1's procedural Z magnitude (m seaward, site_plots.json), the
        /// distance it stands for at <see cref="ReferenceDistanceKm"/>.
        /// </summary>
        [GameBalance("site_plots.json hav_1: z -5200 m stands for its real_distance_km 83; ruling OD2 scales the bound candidate's distance by it")]
        public static double ReferenceZM { get; } = 5200.0;

        /// <summary>hav_1's real distance in site_plots.json (km; 45 nm, O:R58), the distance <see cref="ReferenceZM"/> stands for.</summary>
        [GameBalance("site_plots.json hav_1 real_distance_km (45 nm, O:R58), the scale of the compression")]
        public static double ReferenceDistanceKm { get; } = 83.0;

        /// <summary>The nearest hav_1 may come (m seaward): open sea beyond off_2 (Z -3400) in the seed-42 far field.</summary>
        [GameBalance("ruling OD2: clamp so hav_1 stays seaward of off_2 and in open sea with no seabed above -40 m within 600 m (FarFieldTests' rule)")]
        public static double MinZM { get; } = 4600.0;

        /// <summary>The farthest hav_1 may go (m seaward), well inside the 16 km far field.</summary>
        [GameBalance("ruling OD2: clamp so a 250 km binding still draws inside the far field (±16 km)")]
        public static double MaxZM { get; } = 9000.0;

        /// <summary>hav_1's X (m): the file's, on the fjord's seaward axis.</summary>
        [GameBalance("site_plots.json hav_1 x 0: the open-sea plot sits on the fjord's seaward axis")]
        public static double ReferenceX { get; } = 0.0;

        /// <summary>The home production area (1-13) this geometry was built for.</summary>
        public int HomeProductionArea { get; }

        /// <summary>The bound Havbruk til havs candidate.</summary>
        public GeoCandidate Candidate { get; }

        /// <summary>Straight-line distance (km) from the home area's label point to <see cref="Candidate"/>.</summary>
        public double DistanceKm { get; }

        /// <summary>hav_1's display Z (m; negative is seaward): -clamp(5200 × km / 83, 4600, 9000).</summary>
        public double Z { get; }

        private readonly OffshoreSlot[] _slots;

        /// <summary>The binding for a home production area (1-13).</summary>
        /// <exception cref="ArgumentOutOfRangeException">The area is not 1-13.</exception>
        public GeoSitePlotGeometry(int homeProductionArea)
            : this(homeProductionArea, GeoProductionAreas.Default, GeoCandidates.Default.Offshore)
        {
        }

        /// <summary>The binding over given areas and offshore candidates (tests).</summary>
        internal GeoSitePlotGeometry(int homeProductionArea, GeoProductionAreas areas, IReadOnlyList<GeoCandidate> offshore)
        {
            if (!ProductionAreas.IsValid(homeProductionArea))
                throw new ArgumentOutOfRangeException(nameof(homeProductionArea), homeProductionArea, "Production areas are numbered 1-13");
            if (areas == null) throw new ArgumentNullException(nameof(areas));
            if (offshore == null || offshore.Count == 0)
                throw new ArgumentException("The binding needs at least one offshore candidate", nameof(offshore));
            HomeProductionArea = homeProductionArea;
            var label = areas.Get(homeProductionArea).Label25833;
            var (candidate, squared) = Nearest(label.X, label.Y, offshore);
            Candidate = candidate;
            DistanceKm = Math.Sqrt(squared) / 1000.0;
            Z = ZFor(DistanceKm);
            _slots = new[]
            {
                new OffshoreSlot(BoundPlotId, candidate.Area ?? candidate.Id, (float)ReferenceX, (float)Z, 1f, (float)candidate.DepthM),
            };
        }

        /// <summary>hav_1's display Z for a real distance: -clamp(5200 × km / 83, 4600, 9000).</summary>
        public static double ZFor(double distanceKm)
            => -Math.Clamp(ReferenceZM * distanceKm / ReferenceDistanceKm, MinZM, MaxZM);

        /// <summary>
        /// Straight-line distance (km) from a home area's label point to a
        /// candidate, measured as the binding measures it (integer squared
        /// EPSG:25833 distance, then <see cref="Math.Sqrt"/>), so the bound
        /// candidate gets exactly <see cref="DistanceKm"/>. Null when the game
        /// has no home area (anything outside 1-13, such as
        /// <see cref="ProductionAreas.NationalReference"/>): there is no point
        /// to measure from. Display only (the bridge's offshore list).
        /// </summary>
        public static double? DistanceKmFrom(int homeProductionArea, GeoCandidate candidate)
        {
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            if (!ProductionAreas.IsValid(homeProductionArea)) return null;
            var label = GeoProductionAreas.Default.Get(homeProductionArea).Label25833;
            long dx = candidate.X - label.X, dy = candidate.Y - label.Y;
            return Math.Sqrt(dx * dx + dy * dy) / 1000.0;
        }

        /// <summary>
        /// The candidate nearest (x, y) by squared integer distance; ties to the
        /// higher score, then the lower rank, then the ordinal id.
        /// </summary>
        internal static (GeoCandidate Candidate, long SquaredM2) Nearest(long x, long y, IReadOnlyList<GeoCandidate> candidates)
        {
            GeoCandidate? best = null;
            long bestSquared = long.MaxValue;
            foreach (var c in candidates)
            {
                long dx = c.X - x, dy = c.Y - y;
                long squared = dx * dx + dy * dy;
                if (best == null || squared < bestSquared || (squared == bestSquared && Before(c, best)))
                {
                    best = c;
                    bestSquared = squared;
                }
            }
            return (best!, bestSquared);
        }

        private static bool Before(GeoCandidate a, GeoCandidate b)
        {
            if (a.Score != b.Score) return a.Score > b.Score;
            if (a.Rank != b.Rank) return a.Rank < b.Rank;
            return string.CompareOrdinal(a.Id, b.Id) < 0;
        }

        /// <summary>No land pad is bound: the pads keep site_plots.json's positions.</summary>
        public IReadOnlyList<LandPadSlot> LandPadSlots() => Array.Empty<LandPadSlot>();

        /// <summary>hav_1 only, at the compressed distance, with the candidate's depth and exposure 1.</summary>
        public IReadOnlyList<OffshoreSlot> OffshoreSlots() => _slots;

        /// <summary>No pad is bound, so no intake is computed (0, as the file's pads have).</summary>
        public float IntakeReachM(LandPadSlot pad) => 0f;
    }

    /// <summary>
    /// The per-game plot catalogues (STAGE23_PLAN A2): for each home production
    /// area 1-13, <see cref="SitePlotCatalog.Default"/> strictly joined with
    /// that area's <see cref="GeoSitePlotGeometry"/>, built once per process
    /// on first use. Any other value (0: a game with no scenario) is the
    /// default catalogue. A catalogue is a pure function of the saved home area
    /// and the embedded data, so a load rebuilds it identically and nothing is
    /// saved.
    /// </summary>
    public static class GeoPlotCatalogs
    {
        private static readonly Lazy<GeoSitePlotGeometry>[] Geometries = Enumerable.Range(1, ProductionAreas.Count)
            .Select(po => new Lazy<GeoSitePlotGeometry>(() => new GeoSitePlotGeometry(po)))
            .ToArray();

        private static readonly Lazy<SitePlotCatalog>[] Catalogues = Enumerable.Range(1, ProductionAreas.Count)
            .Select(po => new Lazy<SitePlotCatalog>(() => SitePlotCatalog.Default.WithGeometry(Geometries[po - 1].Value, strict: true)))
            .ToArray();

        /// <summary>The catalogue a game with this home area plays on (Default outside 1-13).</summary>
        public static SitePlotCatalog ForHomeArea(int po)
            => ProductionAreas.IsValid(po) ? Catalogues[po - 1].Value : SitePlotCatalog.Default;

        /// <summary>The geometry behind <see cref="ForHomeArea"/>, or null outside 1-13.</summary>
        public static GeoSitePlotGeometry? GeometryFor(int po)
            => ProductionAreas.IsValid(po) ? Geometries[po - 1].Value : null;
    }
}
