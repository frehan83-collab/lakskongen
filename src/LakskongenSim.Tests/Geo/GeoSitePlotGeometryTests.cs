#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LakskongenSim.Environment;
using LakskongenSim.Environment.Geo;
using LakskongenSim.Farm;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Geo;

/// <summary>
/// hav_1's open-sea binding (STAGE23_PLAN track A, A2; rulings OD1 and OD2):
/// for every home area the Havbruk til havs candidate nearest the area's label
/// by integer squared distance (ties: score, rank, id), at Z = -clamp(5200 ×
/// km / 83, 4600, 9000) in open sea, with the candidate's depth. Only hav_1
/// moves; every other plot and every gameplay attribute is the default
/// catalogue's. The strict join refuses an unknown or mis-kinded id.
/// </summary>
public class GeoSitePlotGeometryTests
{
    private readonly ITestOutputHelper _output;

    public GeoSitePlotGeometryTests(ITestOutputHelper output) => _output = output;

    /// <summary>The binding for each home area, measured from the shipped data (BALANCE §1ab).</summary>
    public static readonly IReadOnlyDictionary<int, string> ExpectedBinding = new Dictionary<int, string>
    {
        [1] = "HTH02-02", [2] = "HTH13-03", [3] = "HTH13-02", [4] = "HTH13-02", [5] = "HTH10-03",
        [6] = "HTH10-02", [7] = "HTH01-01", [8] = "HTH01-01", [9] = "HTH05-03", [10] = "HTH21-02",
        [11] = "HTH21-02", [12] = "HTH23-01", [13] = "HTH27-02",
    };

    private static readonly TerrainGenerator Gen42 = new(42);

    public static IEnumerable<object[]> Areas() => Enumerable.Range(1, 13).Select(po => new object[] { po });

    [Theory]
    [MemberData(nameof(Areas))]
    public void EveryArea_BindsTheNearestHthCandidate_ByIntegerSquaredDistance(int po)
    {
        var geometry = new GeoSitePlotGeometry(po);
        var label = GeoProductionAreas.Default.Get(po).Label25833;
        // Brute force: every candidate's squared distance; the minimum, then score, rank and id.
        var ranked = GeoCandidates.Default.Offshore
            .Select(c => (c, d2: (c.X - label.X) * (c.X - label.X) + (c.Y - label.Y) * (c.Y - label.Y)))
            .OrderBy(t => t.d2).ThenByDescending(t => t.c.Score).ThenBy(t => t.c.Rank).ThenBy(t => t.c.Id, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(ranked[0].c.Id, geometry.Candidate.Id);
        Assert.Equal(ExpectedBinding[po], geometry.Candidate.Id);
        Assert.True(geometry.Candidate.IsOffshore);
        Assert.Equal(Math.Sqrt(ranked[0].d2) / 1000.0, geometry.DistanceKm);
        Assert.Equal(-Math.Clamp(5200.0 * geometry.DistanceKm / 83.0, 4600.0, 9000.0), geometry.Z);
        Assert.InRange(geometry.Z, -9000.0, -4600.0);
        Assert.Equal(po, geometry.HomeProductionArea);

        var slot = Assert.Single(geometry.OffshoreSlots());
        Assert.Equal(GeoSitePlotGeometry.BoundPlotId, slot.Id);
        Assert.Equal((0f, (float)geometry.Z, 1f, (float)geometry.Candidate.DepthM), (slot.X, slot.Z, slot.Exposure, slot.WaterDepthM));
        Assert.Equal(geometry.Candidate.Area, slot.Name);
        Assert.Empty(geometry.LandPadSlots());
        Assert.Equal(0f, geometry.IntakeReachM(new LandPadSlot("pad_a", "x", 0, 0, 1, 1, 1, 1, 0, 0)));
        _output.WriteLine($"PO{po}: {geometry.Candidate.Id} ({geometry.Candidate.Area}, PO{geometry.Candidate.Po}, {geometry.Candidate.DepthM} m) " +
                          $"{geometry.DistanceKm:F1} km -> Z {geometry.Z:F0}");
    }

    [Theory]
    [MemberData(nameof(Areas))]
    public void OnlyHav1Moves_EveryOtherPlotAndAttributeIsTheDefaults(int po)
    {
        var geo = GeoPlotCatalogs.ForHomeArea(po);
        var def = SitePlotCatalog.Default;
        var geometry = GeoPlotCatalogs.GeometryFor(po)!;
        Assert.Equal(def.Version, geo.Version);
        Assert.Equal(def.Plots.Select(p => p.Id), geo.Plots.Select(p => p.Id));
        foreach (var plot in def.Plots)
        {
            var bound = geo.Find(plot.Id)!;
            if (plot.Id != GeoSitePlotGeometry.BoundPlotId)
            {
                Assert.Equal(plot, bound);
                continue;
            }
            // hav_1: the display position and depth only.
            Assert.Equal(plot with { WorldX = bound.WorldX, WorldZ = bound.WorldZ, WaterDepthM = bound.WaterDepthM }, bound);
            Assert.Equal((double)(float)geometry.Z, bound.WorldZ);
            Assert.Equal(0.0, bound.WorldX);
            Assert.Equal((double)(float)geometry.Candidate.DepthM, bound.WaterDepthM);
            Assert.Equal((plot.Hs100M, plot.PlotHsScale, plot.LiceExternalMin, plot.LiceExternalMax, plot.Exposure, plot.Name, plot.RealDistanceKm, plot.ZoneRole),
                (bound.Hs100M, bound.PlotHsScale, bound.LiceExternalMin, bound.LiceExternalMax, bound.Exposure, bound.Name, bound.RealDistanceKm, bound.ZoneRole));
        }
        foreach (var kind in new[] { PlotKind.LandPad, PlotKind.OffshoreSlot })
        {
            Assert.Equal(def.ReservedOverflow(kind), geo.ReservedOverflow(kind));
            for (int i = 1; i <= 6; i++)
                Assert.Equal(def.Overflow(kind, i), geo.Overflow(kind, i));
        }
        Assert.Same(geo, GeoPlotCatalogs.ForHomeArea(po));   // built once per process
    }

    /// <summary>
    /// FarFieldTests' open-sea rule for the procedural slots, at every bound
    /// position: beyond the 4 km world, no seabed above -40 m within 600 m.
    /// </summary>
    [Theory]
    [MemberData(nameof(Areas))]
    public void TheBoundPosition_LiesInOpenSeaBeyondTheWorld(int po)
    {
        var plot = GeoPlotCatalogs.ForHomeArea(po).Find(GeoSitePlotGeometry.BoundPlotId)!;
        float x = (float)plot.WorldX, z = (float)plot.WorldZ;
        float half = Gen42.Settings.WorldSizeM * 0.5f;
        Assert.True(Math.Abs(x) > half || Math.Abs(z) > half, $"PO{po}: hav_1 lies inside the world");
        Assert.True(Math.Abs(z) < 16_000f, $"PO{po}: hav_1 lies past the far field");
        float highest = float.MinValue;
        for (float r = 0f; r <= 600f; r += 30f)
            for (int k = 0; k < 24; k++)
            {
                double a = k * Math.PI / 12.0;
                highest = Math.Max(highest, Gen42.FarFieldHeightAt(x + r * (float)Math.Cos(a), z + r * (float)Math.Sin(a)));
            }
        Assert.True(highest < -40f, $"PO{po}: the sea round hav_1 at Z {z} rises to {highest:F0} m");
    }

    [Fact]
    public void TheConstants_AreSitePlotsJsonsHav1()
    {
        var hav = SitePlotCatalog.Default.Find(GeoSitePlotGeometry.BoundPlotId)!;
        Assert.Equal(PlotKind.OffshoreSlot, hav.Kind);
        Assert.Equal(-hav.WorldZ, GeoSitePlotGeometry.ReferenceZM);
        Assert.Equal(hav.WorldX, GeoSitePlotGeometry.ReferenceX);
        Assert.Equal(hav.RealDistanceKm, GeoSitePlotGeometry.ReferenceDistanceKm);
        Assert.Equal((4600.0, 9000.0), (GeoSitePlotGeometry.MinZM, GeoSitePlotGeometry.MaxZM));
        Assert.Equal(-5200.0, GeoSitePlotGeometry.ZFor(83.0));
        Assert.Equal(-4600.0, GeoSitePlotGeometry.ZFor(0.0));
        Assert.Equal(-9000.0, GeoSitePlotGeometry.ZFor(1000.0));
        Assert.Equal(-5200.0 * 100.0 / 83.0, GeoSitePlotGeometry.ZFor(100.0));
        // The off_2 slot (coastal, 12 km) stays seaward-short of any bound hav_1.
        Assert.True(SitePlotCatalog.Default.Find("off_2")!.WorldZ > -GeoSitePlotGeometry.MinZM);
    }

    private static GeoCandidate Synthetic(string id, long x, long y, double score, int rank) =>
        GeoCandidates.Default.Offshore[0] with { Id = id, X = x, Y = y, Score = score, Rank = rank };

    [Fact]
    public void Ties_GoToTheHigherScore_ThenTheLowerRank_ThenTheOrdinalId()
    {
        var label = GeoProductionAreas.Default.Get(3).Label25833;
        long x = label.X + 30_000, y = label.Y;
        GeoCandidate Bound(params GeoCandidate[] cs) => new GeoSitePlotGeometry(3, GeoProductionAreas.Default, cs).Candidate;
        // Equal distances (one east, one west): the higher score wins either way round.
        var east = Synthetic("HTH90-01", x, y, 0.8, 1);
        var west = Synthetic("HTH91-01", label.X - 30_000, y, 0.9, 1);
        Assert.Equal("HTH91-01", Bound(east, west).Id);
        Assert.Equal("HTH91-01", Bound(west, east).Id);
        // Equal score: the lower rank.
        var rank2 = Synthetic("HTH92-02", label.X, label.Y + 30_000, 0.9, 2);
        Assert.Equal("HTH91-01", Bound(rank2, west).Id);
        // Equal rank too: the ordinal id.
        var twin = Synthetic("HTH89-01", label.X, label.Y - 30_000, 0.9, 1);
        Assert.Equal("HTH89-01", Bound(west, twin).Id);
        // Nearer always wins over score.
        var near = Synthetic("HTH99-03", label.X + 29_999, y, 0.1, 3);
        Assert.Equal("HTH99-03", Bound(east, west, near).Id);
        Assert.Throws<ArgumentException>(() => new GeoSitePlotGeometry(3, GeoProductionAreas.Default, Array.Empty<GeoCandidate>()));
    }

    [Fact]
    public void AnAreaOutsideOneToThirteen_IsTheDefaultCatalogue_AndNoGeometry()
    {
        foreach (int po in new[] { 0, -1, 14, int.MaxValue })
        {
            Assert.Same(SitePlotCatalog.Default, GeoPlotCatalogs.ForHomeArea(po));
            Assert.Null(GeoPlotCatalogs.GeometryFor(po));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => new GeoSitePlotGeometry(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GeoSitePlotGeometry(14));
    }

    private sealed class FixedGeometry : ISitePlotGeometry
    {
        private readonly LandPadSlot[] _pads;
        private readonly OffshoreSlot[] _slots;

        public FixedGeometry(LandPadSlot[] pads, OffshoreSlot[] slots)
        {
            _pads = pads;
            _slots = slots;
        }

        public IReadOnlyList<LandPadSlot> LandPadSlots() => _pads;
        public IReadOnlyList<OffshoreSlot> OffshoreSlots() => _slots;
        public float IntakeReachM(LandPadSlot pad) => 7f;
    }

    private static LandPadSlot Pad(string id) => new(id, id, 10f, 20f, 100f, 80f, 2, 3.6f, 10f, 60f);

    private static OffshoreSlot Slot(string id) => new(id, id, 5f, -6000f, 1f, 210f);

    [Fact]
    public void TheStrictJoin_RefusesAnUnknownMisKindedOrRepeatedId_NamingIt()
    {
        var catalogue = SitePlotCatalog.Default;
        var unknownSlot = Assert.Throws<InvalidDataException>(() =>
            catalogue.WithGeometry(new FixedGeometry(Array.Empty<LandPadSlot>(), new[] { Slot("HTH13-02") }), strict: true));
        Assert.Contains("'HTH13-02'", unknownSlot.Message);
        var unknownPad = Assert.Throws<InvalidDataException>(() =>
            catalogue.WithGeometry(new FixedGeometry(new[] { Pad("pad_zeta") }, Array.Empty<OffshoreSlot>()), strict: true));
        Assert.Contains("'pad_zeta'", unknownPad.Message);
        var padAsSlot = Assert.Throws<InvalidDataException>(() =>
            catalogue.WithGeometry(new FixedGeometry(Array.Empty<LandPadSlot>(), new[] { Slot("pad_a") }), strict: true));
        Assert.Contains("'pad_a'", padAsSlot.Message);
        var slotAsPad = Assert.Throws<InvalidDataException>(() =>
            catalogue.WithGeometry(new FixedGeometry(new[] { Pad("hav_1") }, Array.Empty<OffshoreSlot>()), strict: true));
        Assert.Contains("'hav_1'", slotAsPad.Message);
        var twice = Assert.Throws<InvalidDataException>(() =>
            catalogue.WithGeometry(new FixedGeometry(Array.Empty<LandPadSlot>(), new[] { Slot("off_1"), Slot("off_1") }), strict: true));
        Assert.Contains("'off_1'", twice.Message);
        Assert.Throws<ArgumentNullException>(() => catalogue.WithGeometry(null!, strict: true));
    }

    [Fact]
    public void TheStrictJoin_OfKnownIds_EqualsTheLenientJoin_WhichStillDropsUnknownIds()
    {
        var catalogue = SitePlotCatalog.Default;
        var known = new FixedGeometry(new[] { Pad("pad_b") }, new[] { Slot("off_2") });
        Assert.Equal(catalogue.WithGeometry(known).Plots, catalogue.WithGeometry(known, strict: true).Plots);
        Assert.Equal(catalogue.WithGeometry(known).Plots, catalogue.WithGeometry(known, strict: false).Plots);
        var padB = catalogue.WithGeometry(known, strict: true).Find("pad_b")!;
        Assert.Equal((10.0, 20.0, 100.0, 80.0, 7.0), (padB.WorldX, padB.WorldZ, padB.PadWidthM, padB.PadDepthM, padB.DeepIntakeReachM));
        // The existing join is unchanged: an unknown id is silently dropped.
        var lenient = catalogue.WithGeometry(new FixedGeometry(Array.Empty<LandPadSlot>(), new[] { Slot("HTH13-02") }));
        Assert.Equal(catalogue.Plots, lenient.Plots);
        Assert.Equal(catalogue.Plots, catalogue.WithGeometry(new FixedGeometry(Array.Empty<LandPadSlot>(), new[] { Slot("HTH13-02") }), strict: false).Plots);
    }
}
