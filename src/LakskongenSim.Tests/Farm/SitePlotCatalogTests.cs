using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Tests.Integration;
using Xunit;

namespace LakskongenSim.Tests.Farm;

/// <summary>
/// The farm-type plot catalogue (roadmap F2-03, docs/FARM_TYPES_SPEC.md §3.2
/// and §4): <c>godot/data/site_plots.json</c> holds the gameplay attributes of
/// the land pads and offshore slots, the engine embeds the same file, the
/// geometry seam (<see cref="ISitePlotGeometry"/>) supplies positions and the
/// computed intake, and a game's plot state is saved without the attributes.
/// </summary>
public class SitePlotCatalogTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LakskongenSim.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string DataFileText() => File.ReadAllText(Path.Combine(RepoRoot(), "godot", "data", SitePlotCatalog.FileName));

    private static SitePlotCatalog Catalog => SitePlotCatalog.Default;

    [Fact]
    public void TheEmbeddedCatalogue_IsTheGamesDataFile()
    {
        var fromFile = SitePlotCatalog.Parse(DataFileText());
        Assert.Equal(fromFile.Version, Catalog.Version);
        Assert.Equal(fromFile.Plots, Catalog.Plots);
        foreach (var kind in new[] { PlotKind.LandPad, PlotKind.OffshoreSlot })
            for (int i = 1; i <= 6; i++)
                Assert.Equal(fromFile.Overflow(kind, i), Catalog.Overflow(kind, i));
    }

    /// <summary>The spec §4 plots with their attributes (pad_a at the F2-18 read gate's centre).</summary>
    [Fact]
    public void ThePlots_AreTheSpecsTable()
    {
        Assert.Equal(new[] { "pad_a", "pad_b", "pad_delta", "off_1", "off_2", "hav_1" }, Catalog.Plots.Select(p => p.Id));
        Assert.All(Catalog.Plots, p => Assert.False(p.IsOverflow));

        var padA = Catalog.Find("pad_a")!;
        Assert.Equal(("Storholmen", PlotKind.LandPad, -450.0, -1720.0, 640.0, 420.0, 4),
            (padA.Name, padA.Kind, padA.WorldX, padA.WorldZ, padA.PadWidthM, padA.PadDepthM, padA.PhaseLots));
        Assert.Equal((40.0, 1, 0.2, false), (padA.GridCapacityMW, padA.GridFeeds, padA.NatureSensitivity, padA.PreZoned));
        var padB = Catalog.Find("pad_b")!;
        Assert.Equal(("Skjærvika", 750.0, -1640.0, 480.0, 360.0, 2, 25.0, 0.6),
            (padB.Name, padB.WorldX, padB.WorldZ, padB.PadWidthM, padB.PadDepthM, padB.PhaseLots, padB.GridCapacityMW, padB.NatureSensitivity));
        var delta = Catalog.Find("pad_delta")!;
        Assert.Equal(("Elveøyra", 0.0, 1450.0, 260.0, 180.0, 0, 4, true, true),
            (delta.Name, delta.WorldX, delta.WorldZ, delta.PadWidthM, delta.PadDepthM, delta.PhaseLots, delta.PostSmoltModules,
                delta.HasRiverIntake, delta.PreZoned));
        Assert.All(Catalog.LandPads, p => Assert.Equal(13.0, p.StaticLiftM));
        Assert.All(Catalog.LandPads, p => Assert.Equal(0.0, p.DeepIntakeReachM)); // computed by a geometry, never authored

        var off1 = Catalog.Find("off_1")!;
        Assert.Equal(("Ytre kyst", ExposureClass.OuterCoast, -450.0, -2750.0, 5.0, 5.0, 0.12, 0.12, 150.0, 1.0),
            (off1.Name, off1.Exposure, off1.WorldX, off1.WorldZ, off1.RealDistanceKm, off1.Hs100M, off1.LiceExternalMin,
                off1.LiceExternalMax, off1.WaterDepthM, off1.PlotHsScale));
        var off2 = Catalog.Find("off_2")!;
        Assert.Equal(("Havkanten", ExposureClass.OuterCoastExposed, 850.0, -3400.0, 12.0, 6.5, 0.10, 220.0, 1.3),
            (off2.Name, off2.Exposure, off2.WorldX, off2.WorldZ, off2.RealDistanceKm, off2.Hs100M, off2.LiceExternalMin,
                off2.WaterDepthM, off2.PlotHsScale));
        var hav = Catalog.Find("hav_1")!;
        Assert.Equal(("Havområde", ExposureClass.OpenOcean, SitePlot.HavRole, 83.0, 13.0, 0.05, 0.30, 300.0, 2.4),
            (hav.Name, hav.Exposure, hav.ZoneRole, hav.RealDistanceKm, hav.Hs100M, hav.LiceExternalMin, hav.LiceExternalMax,
                hav.WaterDepthM, hav.PlotHsScale));
        Assert.All(Catalog.Plots.Where(p => p.Id != "hav_1"), p => Assert.Equal(SitePlot.RegionPoRole, p.ZoneRole));
    }

    /// <summary>Spec §4 "Design rating": OF1-class (5.5 m) fits off_1 only; Havfarm-class (8.0 m) off_1 and off_2; 15 m hav_1.</summary>
    [Fact]
    public void TheDesignRating_DecidesTheSlotsAClassFits()
    {
        string[] Fits(double designHs) => Catalog.OffshoreSlots.Select(SitePlot.NewGame)
            .Where(p => p.AcceptsDesignHs(designHs)).Select(p => p.Id).ToArray();
        Assert.Equal(new[] { "off_1" }, Fits(5.5));
        Assert.Equal(new[] { "off_1", "off_2" }, Fits(5.5 + 1.0)); // with the +1.0 m reinforcement
        Assert.Equal(new[] { "off_1", "off_2" }, Fits(8.0));
        Assert.Equal(new[] { "off_1", "off_2", "hav_1" }, Fits(15.0));
        Assert.Empty(Fits(4.9));
        Assert.All(Catalog.LandPads.Select(SitePlot.NewGame), p => Assert.False(p.AcceptsDesignHs(100.0)));
    }

    /// <summary>
    /// Overflow plots (spec §3.8 step 4): reserved positions with neutral
    /// attributes that fit every legacy unit, numbered from 1, generated past
    /// the reserved ones so a migrated save always finds one; found by id.
    /// </summary>
    [Fact]
    public void OverflowPlots_AreNeutral_ReservedThenGenerated_AndFoundById()
    {
        Assert.Equal(4, Catalog.ReservedOverflow(PlotKind.LandPad));
        Assert.Equal(4, Catalog.ReservedOverflow(PlotKind.OffshoreSlot));
        var land = Enumerable.Range(1, 7).Select(i => Catalog.Overflow(PlotKind.LandPad, i)).ToList();
        var sea = Enumerable.Range(1, 7).Select(i => Catalog.Overflow(PlotKind.OffshoreSlot, i)).ToList();
        Assert.Equal(Enumerable.Range(1, 7).Select(i => $"overflow_land_{i}"), land.Select(p => p.Id));
        Assert.Equal(Enumerable.Range(1, 7).Select(i => $"overflow_offshore_{i}"), sea.Select(p => p.Id));
        Assert.All(land.Concat(sea), p => Assert.True(p.IsOverflow));
        Assert.Equal(land.Count, land.Select(p => (p.WorldX, p.WorldZ)).Distinct().Count());
        Assert.Equal(sea.Count, sea.Select(p => (p.WorldX, p.WorldZ)).Distinct().Count());
        // Neutral: every legacy land unit fits (4 lots, 4 modules), every legacy offshore class is rated for it.
        Assert.All(land, p => Assert.Equal((PlotKind.LandPad, 4, 4, true), (p.Kind, p.PhaseLots, p.PostSmoltModules, p.PreZoned)));
        Assert.All(sea, p => Assert.True(SitePlot.NewGame(p).AcceptsDesignHs(5.5)));
        Assert.All(land, p => Assert.Equal(Math.Round(Math.Sqrt(p.WorldX * p.WorldX + p.WorldZ * p.WorldZ) / 1000.0, 1), p.RealDistanceKm));
        foreach (var p in land.Concat(sea))
            Assert.Equal(p, Catalog.Find(p.Id));
        Assert.Null(Catalog.Find("overflow_land_0"));
        Assert.Null(Catalog.Find("overflow_land_01"));
        Assert.Null(Catalog.Find("pad_z"));
        Assert.Null(Catalog.Find(""));
    }

    /// <summary>
    /// The geometry seam: a terrain or a bake's meta.json gives the positions,
    /// pad sizes, water depths and the computed intake, joined by id; plots it
    /// does not know keep the file's fallback. The procedural terrain agrees
    /// with the file for every plot within 1 m, pad_a included since F2-19
    /// moved its terrain slot to the F2-18 read gate's centre (the stronger
    /// replacement of the pending-move check, RUN_LOG §7 2026-10-17).
    /// </summary>
    [Fact]
    public void TheGeometrySeam_JoinsThePlotsById_AndTheTerrainAgreesWithTheFile()
    {
        var terrain = new TerrainGenerator(42);
        var joined = Catalog.WithGeometry(terrain);
        foreach (var pad in terrain.LandPadSlots())
        {
            var plot = joined.Find(pad.Id)!;
            Assert.Equal((pad.CentreX, pad.CentreZ, pad.WidthM, pad.DepthM), ((float)plot.WorldX, (float)plot.WorldZ, (float)plot.PadWidthM, (float)plot.PadDepthM));
            Assert.Equal(terrain.IntakeReachM(pad), plot.DeepIntakeReachM, 3);
            Assert.True(plot.DeepIntakeReachM > 0.0);
            var file = Catalog.Find(pad.Id)!;
            Assert.Equal(file.PhaseLots, pad.Id == "pad_delta" ? 0 : pad.Lots); // pad_delta's 4 are post-smolt modules
            if (pad.Id == "pad_a")
                Assert.Equal((-450f, -1720f), (pad.CentreX, pad.CentreZ)); // the F2-18 read gate's centre (F2-19)
            Assert.True(Math.Abs(file.WorldX - pad.CentreX) <= 1.0 && Math.Abs(file.WorldZ - pad.CentreZ) <= 1.0, pad.Id);
            Assert.Equal((file.PadWidthM, file.PadDepthM), ((double)pad.WidthM, (double)pad.DepthM));
        }
        foreach (var slot in terrain.OffshoreSlots())
        {
            var plot = joined.Find(slot.Id)!;
            Assert.Equal((slot.X, slot.Z, slot.WaterDepthM), ((float)plot.WorldX, (float)plot.WorldZ, (float)plot.WaterDepthM));
            var file = Catalog.Find(slot.Id)!;
            Assert.True(Math.Abs(file.WorldX - slot.X) <= 1.0 && Math.Abs(file.WorldZ - slot.Z) <= 1.0, slot.Id);
            Assert.InRange(slot.WaterDepthM, file.WaterDepthM * 0.85, file.WaterDepthM * 1.15);
            Assert.Equal(file.Hs100M, plot.Hs100M); // the gameplay attributes stay the file's
        }
        Assert.Equal(Catalog.Plots.Select(p => p.Id), joined.Plots.Select(p => p.Id));
    }

    /// <summary>The loader names the plot and the field of every defect, and refuses an authored intake.</summary>
    [Fact]
    public void TheLoader_RefusesADefectiveFile_NamingThePlotAndTheField()
    {
        string Mutate(Action<JsonObject> change)
        {
            var root = JsonNode.Parse(DataFileText())!.AsObject();
            change(root);
            return root.ToJsonString();
        }
        JsonObject Plot(JsonObject root, string list, int index) => root[list]!.AsArray()[index]!.AsObject();

        var cases = new (string Expect, Action<JsonObject> Change)[]
        {
            ("'pad_a' appears twice", r => Plot(r, "land_pads", 1)["id"] = "pad_a"),
            ("deep_intake_reach_m", r => Plot(r, "land_pads", 0)["deep_intake_reach_m"] = 300),
            ("lice_external_min", r => Plot(r, "offshore_slots", 2)["lice_external_min"] = 0.5),
            ("unknown exposure", r => Plot(r, "offshore_slots", 0)["exposure"] = "Fjordarm"),
            ("hs100_m", r => Plot(r, "offshore_slots", 1)["hs100_m"] = 0),
            ("width_m", r => Plot(r, "land_pads", 2).Remove("width_m")),
            ("zone_role", r => Plot(r, "land_pads", 0)["zone_role"] = "po3"),
            ("neither grow-out lots nor post-smolt modules", r => { Plot(r, "land_pads", 1)["lots"] = 0; }),
            ("reserves no positions", r => r["overflow_land"]!["positions"] = new JsonArray()),
            ("extra_step", r => r["overflow_offshore"]!["extra_step"] = new JsonArray(0, 0)),
            ("version", r => r.Remove("version")),
        };
        foreach (var (expect, change) in cases)
        {
            var ex = Assert.Throws<InvalidDataException>(() => SitePlotCatalog.Parse(Mutate(change)));
            Assert.Contains(expect, ex.Message);
        }
    }

    /// <summary>
    /// The data file keeps its keys sorted (roadmap convention) and its every
    /// attribute that is not derived names a source (a key or "GB").
    /// </summary>
    [Fact]
    public void TheDataFile_KeepsItsKeysSorted()
    {
        void Check(JsonNode node, string path)
        {
            if (node is JsonObject obj)
            {
                var keys = obj.Select(kv => kv.Key).ToList();
                Assert.True(keys.SequenceEqual(keys.OrderBy(k => k, StringComparer.Ordinal)), $"{path}: keys not sorted: {string.Join(", ", keys)}");
                foreach (var kv in obj) Check(kv.Value, $"{path}.{kv.Key}");
            }
            else if (node is JsonArray arr)
                for (int i = 0; i < arr.Count; i++) Check(arr[i], $"{path}[{i}]");
        }
        Check(JsonNode.Parse(DataFileText()), "site_plots");
        using var doc = JsonDocument.Parse(DataFileText());
        foreach (var list in new[] { "land_pads", "offshore_slots" })
            foreach (var plot in doc.RootElement.GetProperty(list).EnumerateArray())
                Assert.True(plot.TryGetProperty("sources", out var sources) && sources.EnumerateObject().Any(),
                    $"{plot.GetProperty("id").GetString()} names no sources");
    }

    /// <summary>
    /// A game's plot state: a fjord-only game holds none (its save is
    /// unchanged), the engine's view still lists every named plot free; a
    /// build takes the named plots into the save; the save keeps the state
    /// and no attribute, and a load joins the catalogue's attributes again.
    /// </summary>
    [Fact]
    public void AGamesPlotState_IsSavedWithoutItsAttributes_AndJoinsTheCatalogueOnLoad()
    {
        var engine = new SimulationEngine { FinanceManager = new LakskongenSim.Finance.FinanceManager(5_000_000_000m) };
        engine.Initialize(enableEvents: false, seed: 4);
        Assert.Empty(engine.State.SitePlots);
        Assert.Equal(Catalog.Plots.Select(p => p.Id), engine.SitePlots.Select(p => p.Id));
        Assert.All(engine.SitePlots, p => Assert.True(p.IsFree));
        Assert.Empty(engine.State.SitePlots); // reading the view adds nothing

        Assert.True(engine.BuildOffshoreFarm("", OffshoreType.SemiSubmersible).Ok);
        Assert.Equal(Catalog.Plots.Select(p => p.Id), engine.State.SitePlots.Select(p => p.Id));
        Assert.Equal("offshore_1", engine.FindPlot("off_1")!.OwnerUnitId);

        string json = SaveStateComparer.Serialize(engine);
        using (var doc = JsonDocument.Parse(json))
        {
            var saved = doc.RootElement.GetProperty("SitePlots").EnumerateArray().First(p => p.GetProperty("Id").GetString() == "off_1");
            var names = saved.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Assert.Equal(new[] { "GridFeeds", "Id", "LiceExternalRevealed", "OwnerUnitId", "PreZoned", "WithdrawnFromDay", "ZoneId" }, names);
        }
        var loaded = SaveStateComparer.Resume(json, events: false);
        var plot = loaded.FindPlot("off_1")!;
        Assert.Equal(("offshore_1", 5.0, "Ytre kyst", "off_1"), (plot.OwnerUnitId, plot.Hs100M, plot.Name, plot.ZoneId));
        Assert.Equal(Catalog.Find("off_1"), plot.Template);
        Assert.True(double.IsNaN(plot.LiceExternalRevealed));
        Assert.Equal(0.12, plot.LiceExternalMultiplier);
    }
}
