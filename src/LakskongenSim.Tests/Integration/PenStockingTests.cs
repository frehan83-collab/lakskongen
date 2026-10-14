using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// S2-01b acceptance (roadmap S2-01 item 4): sites have 6-12 real pens from
/// their locality's layout; a stocking picks a pen and a count and is refused
/// above 200,000 fish in a pen (akvakulturdriftsforskriften § 47a) or above a
/// projected peak of 25 kg/m³ (§ 25), the projection grown on the S2-03 growth
/// law; the site verb spreads its smolts over the fewest empty pens that hold
/// them. Populations map to pens: the biology tick reads each population's
/// density in its own pen, a harvest takes a pen by its id and frees it, a
/// delousing can treat chosen pens.
/// </summary>
public class PenStockingTests
{
    private readonly ITestOutputHelper _out;

    public PenStockingTests(ITestOutputHelper output) => _out = output;

    private static SimulationEngine Started(string scenarioId, int seed, bool events = false)
    {
        var engine = new SimulationEngine { FinanceManager = new FinanceManager(1_000_000m) };
        engine.Initialize(enableEvents: events, seed: seed);
        Assert.True(engine.StartScenario(scenarioId).Ok);
        return engine;
    }

    private static SeaSite Site(SimulationEngine engine, string id) => engine.State.SeaSites.Single(s => s.Id == id);

    private static int FishIn(SeaSite site, string pen) => site.PopulationIn(site.FindPen(pen)!)?.TotalCount ?? 0;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LakskongenSim.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    // ─── Layout and mapping ────────────────────────────────────────────────

    [Fact]
    public void EveryScenarioSite_StartsWithItsLocalitysPens_AndEachPopulationInItsOwnPen()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "godot", "data", "scenarios.json")));
        int sites = 0;
        foreach (var scenario in json.RootElement.EnumerateArray())
        {
            string scenarioId = scenario.GetProperty("id").GetString()!;
            var engine = Started(scenarioId, seed: 3);
            foreach (var data in scenario.GetProperty("starting_sites").EnumerateArray())
            {
                sites++;
                var site = Site(engine, data.GetProperty("id").GetString()!);
                var layout = data.GetProperty("pen_layout");
                int count = layout.GetProperty("count").GetInt32();
                var net = new PenGeometry(layout.GetProperty("circumference_m").GetDouble(),
                    layout.GetProperty("net_wall_depth_m").GetDouble(), layout.GetProperty("cone_depth_m").GetDouble());
                int stocked = data.GetProperty("pens").GetInt32();

                Assert.InRange(site.PhysicalPens.Count, 6, 12);
                Assert.Equal(count, site.PhysicalPens.Count);
                Assert.Equal(Enumerable.Range(1, count), site.PhysicalPens.Select(p => p.Number));
                Assert.All(site.PhysicalPens, p => Assert.Equal(net.VolumeM3, p.VolumeM3, 6));
                // The stocked pens are the first pens, one population each.
                Assert.Equal(stocked, site.Pens.Count);
                for (int i = 0; i < site.Pens.Count; i++)
                    Assert.Equal(i + 1, site.PenOf(site.Pens[i])!.Number);
                Assert.Equal(count - stocked, site.PhysicalPens.Count(p => site.IsPenFree(p)));
                // The engine's own roster agrees with the data file.
                var roster = engine.Scenario.GetScenario(scenarioId)!.StartingSites.Single(s => s.Id == site.Id);
                Assert.Equal(new PenLayout(count, net), roster.ResolvePenLayout(engine.Calibration.Pens));
            }
        }
        Assert.Equal(16, sites);
    }

    [Fact]
    public void ABuiltSite_GetsTheCalibratedStandardLayout()
    {
        var engine = Started("po3_red_sone", seed: 5);

        Assert.Equal(8, engine.PreviewBuildSeaSite("po3_nord").Pens);
        Assert.True(engine.BuildSeaSite("po3_nord", "utvidelse").Ok);

        var built = Site(engine, "utvidelse");
        Assert.Equal(8, built.PhysicalPens.Count);
        Assert.All(built.PhysicalPens, p => Assert.Equal(160.0, p.CircumferenceM));
        Assert.All(built.PhysicalPens, p => Assert.Equal(61_115.498, p.VolumeM3, 2));
    }

    // ─── Acceptance 4: the per-pen limits ──────────────────────────────────

    /// <summary>§ 47a: one pen takes at most 200,000 fish; 200,001 is refused before any money moves.</summary>
    [Fact]
    public void StockingRejectsMoreThanTwoHundredThousandFishInOnePen()
    {
        var engine = Started("barekraft_mester", seed: 7);
        var site = Site(engine, "lakskongen_hovedanlegg");
        decimal cash = engine.FinanceManager.Balance;
        int populations = engine.State.Populations.Count;

        var refused = engine.StockSmoltsInPen(site.Id, "5", 200_001);

        Assert.False(refused.Ok);
        Assert.Equal(PenReasons.FishLimit, refused.ErrorKey);
        Assert.Equal(cash, engine.FinanceManager.Balance);
        Assert.Equal(populations, engine.State.Populations.Count);
        Assert.True(site.IsPenFree(site.FindPen("5")!));

        var preview = engine.PreviewPenStocking(site.Id, "5", 200_000);
        _out.WriteLine($"200,000 smolts in a 160 m pen: plan peak {preview.ProjectedPeakDensityKgPerM3:F2} kg/m³ on day {preview.ProjectedPeakDay}, room {preview.MaxCount:N0}");
        Assert.True(preview.Allowed, preview.Message);
        Assert.Equal(200_000, preview.MaxCount);
        var stocked = engine.StockSmoltsInPen(site.Id, "5", 200_000);
        Assert.True(stocked.Ok, stocked.Message);
        Assert.Equal(200_000, FishIn(site, "5"));
    }

    /// <summary>
    /// § 25 on the plan's projected peak: a small pen's room is the count whose
    /// peak on the S2-03 growth law stays at 25 kg/m³; one smolt more is
    /// refused although today the smolts stand at under 1 kg/m³.
    /// </summary>
    [Fact]
    public void StockingRejectsAProjectedPeakDensityAboveTwentyFiveKilosPerCubicMetre()
    {
        var engine = Started("barekraft_mester", seed: 8);
        var site = Site(engine, "lakskongen_hovedanlegg");
        // Pen 6 is an old 90 m ring with a 15 m wall and a 10 m cone (11,817 m³).
        var small = site.FindPen("6")!;
        var net = new PenGeometry(90.0, 15.0, 10.0);
        small.CircumferenceM = net.CircumferenceM;
        small.NetWallDepthM = net.NetWallDepthM;
        small.ConeDepthM = net.ConeDepthM;
        small.VolumeM3 = net.VolumeM3;

        int room = engine.PreviewPenStocking(site.Id, "6", 0).MaxCount;
        var fits = engine.PreviewPenStocking(site.Id, "6", room);
        var over = engine.PreviewPenStocking(site.Id, "6", room + 1);
        _out.WriteLine($"90 m pen ({small.VolumeM3:N0} m³): room {room:N0} smolts, peak {fits.ProjectedPeakDensityKgPerM3:F3} kg/m³ " +
                       $"at {fits.ProjectedPeakWeightKg:F2} kg on day {fits.ProjectedPeakDay}; +1: {over.ProjectedPeakDensityKgPerM3:F4}");

        Assert.InRange(room, 50_000, 80_000);
        Assert.True(fits.Allowed, fits.Message);
        Assert.True(fits.ProjectedPeakDensityKgPerM3 <= 25.0);
        Assert.False(over.Allowed);
        Assert.Equal(PenReasons.Density, over.ReasonKey);
        Assert.True(over.ProjectedPeakDensityKgPerM3 > 25.0);
        // The plan grows the fish to the harvest weight: today they stand at under 1 kg/m³.
        Assert.True((room + 1) * 0.1 / small.VolumeM3 < 1.0);
        Assert.InRange(over.ProjectedPeakWeightKg, 5.2, 5.3);

        decimal cash = engine.FinanceManager.Balance;
        var refused = engine.StockSmoltsInPen(site.Id, "6", room + 1);
        Assert.Equal(PenReasons.Density, refused.ErrorKey);
        Assert.Equal(cash, engine.FinanceManager.Balance);
        Assert.True(engine.StockSmoltsInPen(site.Id, "6", room).Ok);
        Assert.Equal(room, FishIn(site, "6"));
    }

    [Fact]
    public void AnOccupiedOrUnknownPen_RefusesTheStocking()
    {
        var engine = Started("po3_red_sone", seed: 9);

        Assert.Equal(PenReasons.Occupied, engine.StockSmoltsInPen("po3_nord", "3", 1_000).ErrorKey);
        Assert.Equal(PenReasons.NotFound, engine.StockSmoltsInPen("po3_nord", "9", 1_000).ErrorKey);
        Assert.Equal(PenReasons.NotFound, engine.StockSmoltsInPen("po3_nord", "po3_sor/p07", 1_000).ErrorKey);
        Assert.Equal(0, engine.PreviewPenStocking("po3_nord", "3", 1_000).MaxCount);
        Assert.True(engine.StockSmoltsInPen("po3_nord", "po3_nord/p07", 1_000).Ok);
    }

    /// <summary>The site verb spreads its smolts over the fewest empty pens that hold them, never over a pen's limits.</summary>
    [Fact]
    public void TheSiteVerb_SpreadsTheSmoltsOverTheFewestEmptyPens_WithinThePerPenLimits()
    {
        var engine = Started("barekraft_mester", seed: 10);
        var site = Site(engine, "lakskongen_hovedanlegg");

        var stocked = engine.StockSmolts(site.Id, 450_000);

        Assert.True(stocked.Ok, stocked.Message);
        var placed = site.PhysicalPens.Where(p => p.Number > 4 && !site.IsPenFree(p)).ToList();
        Assert.Equal(new[] { 5, 6, 7 }, placed.Select(p => p.Number));
        Assert.Equal(450_000, placed.Sum(p => site.PopulationIn(p)!.TotalCount));
        Assert.All(placed, p => Assert.Equal(150_000, site.PopulationIn(p)!.TotalCount));

        // With pens 8 and 9 taken, pen 10 alone binds before the locality's
        // MTB: its 200,000 fish are the room, and one fish more is refused.
        Assert.True(engine.StockSmoltsInPen(site.Id, "8", 1_000).Ok);
        Assert.True(engine.StockSmoltsInPen(site.Id, "9", 1_000).Ok);
        var mtbOnly = engine.PreviewPenStocking(site.Id, "10", 0);
        Assert.Equal(200_000, mtbOnly.MaxCount);
        Assert.Equal(200_000, engine.PreviewStocking(site.Id, 0).MaxCount);
        decimal cash = engine.FinanceManager.Balance;
        var refused = engine.StockSmolts(site.Id, 200_001);
        Assert.Equal(PenReasons.PensFull, refused.ErrorKey);
        Assert.Equal(cash, engine.FinanceManager.Balance);
        Assert.Equal(PenReasons.PensFull, engine.PreviewStocking(site.Id, 200_001).ReasonKey);
        Assert.True(engine.PreviewStocking(site.Id, 200_000).Allowed);
    }

    /// <summary>
    /// The § 47a fish limit is capacity v1's: an unregulated game (a bare
    /// engine, the strategy probe's "rules before S2-01") holds a pen only to
    /// the density its fish will reach.
    /// </summary>
    [Fact]
    public void AnUnregulatedGame_HoldsAPenToItsDensityOnly()
    {
        var engine = new SimulationEngine { FinanceManager = new FinanceManager(50_000_000m) };
        engine.Initialize(enableEvents: false, seed: 11);
        var zone = new Zone("pen-zone") { ProductionArea = 4 };
        engine.State.Zones.Add(zone);
        var site = new SeaSite("pen-site", zone.Id) { MABTonnes = 50_000 };
        engine.State.SeaSites.Add(site);
        Assert.False(engine.CapacityRulesActive);

        int room = engine.PreviewPenStocking(site.Id, "1", 0).MaxCount;
        Assert.InRange(room, 250_000, 400_000);
        Assert.Equal(PenReasons.Density, engine.StockSmoltsInPen(site.Id, "1", room + 1).ErrorKey);
        Assert.True(engine.StockSmoltsInPen(site.Id, "1", 250_000).Ok);
    }

    // ─── Populations map to pens ───────────────────────────────────────────

    /// <summary>
    /// The biology tick reads each population's density in its own pen: the
    /// same fish packed into a 6,000 m³ pen (50 kg/m³) grow slower than in a
    /// 61,116 m³ pen (5 kg/m³). The wave-2 proxy volume (20,000 m³ for 100,000
    /// fish) would grow both alike.
    /// </summary>
    [Fact]
    public void TheBiologyTick_ReadsDensityInThePopulationsOwnPen()
    {
        double Grown(bool cramped)
        {
            var engine = Started("barekraft_mester", seed: 12);
            var site = Site(engine, "lakskongen_hovedanlegg");
            var pen = site.FindPen("8")!;
            if (cramped) pen.VolumeM3 = 6_000.0;
            var population = new FishPopulation(engine.Growth, engine.Mortality);
            population.AddCohort(new FishPopulation.Cohort(500, 100_000, 3.0, LifeStage.Seawater));
            site.Stock(population, pen);
            engine.State.Populations.Add(population);
            Assert.Same(pen, engine.HoldingOf(population).Pen);
            double start = population.GetTotalBiomassKg() / population.TotalCount;
            engine.Advance(30);
            return population.GetTotalBiomassKg() / population.TotalCount - start;
        }

        double open = Grown(cramped: false), cramped = Grown(cramped: true);
        _out.WriteLine($"30 days at 3 kg: +{open * 1000:F1} g a fish at 5 kg/m³, +{cramped * 1000:F1} g at 50 kg/m³");
        Assert.True(open > 0.0);
        Assert.True(cramped < 0.8 * open, $"the cramped pen grew {cramped / open:P1} of the open pen");
    }

    [Fact]
    public void HarvestingAPenByItsId_TakesOnlyItsFish_AndFreesItForTheNextStocking()
    {
        var engine = Started("po3_red_sone", seed: 13);
        var site = Site(engine, "po3_nord");
        engine.Advance(20);
        var others = site.PhysicalPens.Where(p => p.Number != 2 && !site.IsPenFree(p))
            .ToDictionary(p => p.Number, p => site.PopulationIn(p)!.TotalCount);
        int inPen2 = FishIn(site, "2");

        var harvested = engine.HarvestNow(site.Id, new[] { "po3_nord/p02" });

        Assert.True(harvested.Ok, harvested.Message);
        Assert.True(site.FindPen("2")!.IsEmpty);
        Assert.Equal(5, site.Pens.Count);
        foreach (var (number, fish) in others)
            Assert.Equal(fish, FishIn(site, number.ToString()));
        Assert.Equal(inPen2, engine.State.HarvestReports.Last().Fish);

        Assert.True(engine.StockSmoltsInPen(site.Id, "2", 10_000).Ok);
        Assert.Equal(10_000, FishIn(site, "2"));

        // A planned harvest names its pens too, and the order keeps each pen's id.
        var ordered = engine.OrderHarvest(site.Id, new[] { "3" });
        Assert.True(ordered.Ok, ordered.Message);
        var line = Assert.Single(engine.State.HarvestOrders.Single(o => o.Id == ordered.Id).Pens);
        Assert.Equal("po3_nord/p03", line.PenId);
        Assert.Equal(site.PopulationIn(site.FindPen("3")!)!.Id, line.PopulationId);
        Assert.Equal("pen_not_found", engine.HarvestNow(site.Id, new[] { "po3_nord/p11" }).ErrorKey);
    }

    /// <summary>
    /// A delousing of chosen pens handles only their fish (deaths and cost on
    /// pen 1 alone) and kills the site's lice on the treated share of its
    /// fish: the drop is that share of the whole-site treatment's drop.
    /// </summary>
    [Fact]
    public void DelousingChosenPens_HandlesOnlyTheirFish_AndKillsTheTreatedShareOfTheLice()
    {
        (SimulationEngine Engine, SeaSite Site, double Before) Lousy()
        {
            var engine = Started("po3_red_sone", seed: 14);
            var site = Site(engine, "po3_nord");
            site.Lice.AdultFemalePerFish = 1.0;
            return (engine, site, site.Lice.AdultFemalePerFish);
        }

        var (pens, penSite, before) = Lousy();
        var counts = penSite.Pens.ToDictionary(p => p.Id, p => p.TotalCount);
        int siteFish = counts.Values.Sum();
        var pen1 = penSite.PopulationIn(penSite.FindPen("1")!)!;
        double share = (double)pen1.TotalCount / siteFish;
        decimal cash = pens.FinanceManager.Balance;

        var treated = pens.DelousePens(penSite.Id, "Hydrolicer", new[] { "1" });

        Assert.True(treated.Ok, treated.Message);
        Assert.Equal(counts[pen1.Id], (int)treated.Value);
        Assert.True(pen1.TotalCount < counts[pen1.Id], "pen 1 should lose handling deaths");
        foreach (var other in penSite.Pens.Where(p => p != pen1))
            Assert.Equal(counts[other.Id], other.TotalCount);
        Assert.Equal(counts[pen1.Id] * 2.5m, cash - pens.FinanceManager.Balance);

        var (whole, wholeSite, _) = Lousy();
        Assert.True(whole.DelouseSite(wholeSite.Id, "Hydrolicer").Ok);
        double partialDrop = 1.0 - penSite.Lice.AdultFemalePerFish / before;
        double wholeDrop = 1.0 - wholeSite.Lice.AdultFemalePerFish / before;
        _out.WriteLine($"pen 1 holds {share:P1} of the fish: lice drop {partialDrop:P2} vs {wholeDrop:P2} for the site");
        Assert.Equal(share * wholeDrop, partialDrop, 6);
    }

    /// <summary>
    /// The storyteller's ILA event culls "the first sea pen": with real pens
    /// that is pen 1 of the first site, the rest of the locality keeps its
    /// fish, and the emptied pen takes the next stocking. (Mattilsynet culls
    /// the whole locality; roadmap D5/S4-02 replaces the event with the
    /// notifiable-disease state machine, RUN_LOG §8.)
    /// </summary>
    [Fact]
    public void TheIlaEvent_CullsOnePen_WhichTakesTheNextStocking()
    {
        var engine = new SimulationEngine { FinanceManager = new FinanceManager(1_000_000m) };
        engine.Initialize(enableEvents: true, seed: 18);
        Assert.True(engine.StartScenario("po3_red_sone").Ok);
        var nord = Site(engine, "po3_nord");
        int pen1 = FishIn(nord, "1");
        int others = nord.Pens.Sum(p => p.TotalCount) - pen1;

        var ila = engine.Storyteller.GetEventDefinitions().Single(e => e.Id == "isa-outbreak");
        engine.Storyteller.Fire(ila, engine.State.Day);

        Assert.Equal(0, FishIn(nord, "1"));
        Assert.Equal(pen1, engine.MortalityLedger.ForSite(nord.Id).Deaths(MortalityCause.DiseaseCull).Fish);
        Assert.Equal(others, nord.Pens.Sum(p => p.TotalCount));
        Assert.True(nord.IsPenFree(nord.FindPen("1")!));
        Assert.True(engine.StockSmoltsInPen(nord.Id, "1", 5_000).Ok);
        Assert.Equal(5_000, FishIn(nord, "1"));
    }

    // ─── Saves ─────────────────────────────────────────────────────────────

    [Fact]
    public void AGameWithPensStockedHarvestedAndRestocked_SavesAndContinuesExactly_AndTwinsAgree()
    {
        SimulationEngine Play()
        {
            var engine = Started("po3_red_sone", seed: 15, events: true);
            Assert.True(engine.StockSmoltsInPen("po3_nord", "7", 20_000).Ok);
            engine.Advance(40);
            Assert.True(engine.HarvestNow("po3_sor", new[] { "po3_sor/p02" }).Ok);
            Assert.True(engine.StockSmoltsInPen("po3_sor", "2", 15_000, "robust").Ok);
            engine.Advance(20);
            return engine;
        }

        var straight = Play();
        var saved = Play();
        var twin = SaveStateComparer.Normalize(SaveStateComparer.Serialize(saved));
        var resumed = SaveStateComparer.Resume(SaveStateComparer.Serialize(saved), events: true);
        Assert.Equal(twin, SaveStateComparer.Normalize(SaveStateComparer.Serialize(straight)));

        straight.Advance(60);
        resumed.Advance(60);
        string a = SaveStateComparer.Normalize(SaveStateComparer.Serialize(straight));
        string b = SaveStateComparer.Normalize(SaveStateComparer.Serialize(resumed));
        Assert.True(a == b, "the resumed game diverged at " + SaveStateComparer.FirstDifference(a, b));
        Assert.Contains("\"PhysicalPens\"", a);
        var restocked = Site(resumed, "po3_sor").PopulationIn(Site(resumed, "po3_sor").FindPen("2")!);
        Assert.NotNull(restocked);
        Assert.InRange(restocked!.TotalCount, 13_000, 15_000);
    }

    /// <summary>
    /// A save from before real pens: each site gets its scenario locality's
    /// layout and each population its own pen, in list order; the game goes on.
    /// </summary>
    [Fact]
    public void ASaveFromBeforeRealPens_GetsOnePenPerPopulation_InItsLocalitysLayout()
    {
        var engine = Started("po3_red_sone", seed: 16);
        engine.Advance(10);
        var json = System.Text.Json.Nodes.JsonNode.Parse(SaveStateComparer.Serialize(engine))!.AsObject();
        foreach (var site in json["SeaSites"]!.AsArray())
            Assert.True(site!.AsObject().Remove("PhysicalPens"));

        var resumed = SaveStateComparer.Resume(json.ToJsonString(SaveJson.Options), events: false);

        foreach (var (id, layout) in new[] { ("po3_nord", 8), ("po3_sor", 6) })
        {
            var site = Site(resumed, id);
            Assert.Equal(layout, site.PhysicalPens.Count);
            for (int i = 0; i < site.Pens.Count; i++)
                Assert.Equal(i + 1, site.PenOf(site.Pens[i])!.Number);
            Assert.All(site.PhysicalPens, p => Assert.Equal(61_115.498, p.VolumeM3, 2));
        }
        resumed.Advance(5);
        Assert.True(resumed.StockSmoltsInPen("po3_nord", "8", 5_000).Ok);
    }

    /// <summary>
    /// A legacy population too large for one pen (S2-01a let a site stock its
    /// plan room in one go) keeps the water it was simulated in: its pen grows
    /// to the wave-2 proxy volume, a unit stocked before the per-pen limits
    /// (§ 70 exempts such units from § 47a). A site with no scenario layout
    /// gets the standard one.
    /// </summary>
    [Fact]
    public void ALegacyPopulationTooLargeForAPen_KeepsTheVolumeItWasSimulatedIn()
    {
        var engine = new SimulationEngine { FinanceManager = new FinanceManager(5_000_000m) };
        engine.Initialize(enableEvents: false, seed: 17);
        var zone = new Zone("legacy-zone") { ProductionArea = 4 };
        engine.State.Zones.Add(zone);
        var site = new SeaSite("legacy-site", zone.Id) { MABTonnes = 10_000 };
        engine.State.SeaSites.Add(site);
        var big = new FishPopulation(engine.Growth, engine.Mortality);
        big.AddCohort(new FishPopulation.Cohort(500, 561_600, 0.4, LifeStage.Seawater));
        site.Stock(big);
        engine.State.Populations.Add(big);
        double proxy = engine.StockingVolumeM3(big);

        var json = System.Text.Json.Nodes.JsonNode.Parse(SaveStateComparer.Serialize(engine))!.AsObject();
        Assert.True(json["SeaSites"]![0]!.AsObject().Remove("PhysicalPens"));
        var resumed = SaveStateComparer.Resume(json.ToJsonString(SaveJson.Options), events: false);

        var loaded = Site(resumed, "legacy-site");
        Assert.Equal(8, loaded.PhysicalPens.Count);
        var pen = loaded.PenOf(loaded.Pens.Single())!;
        Assert.Equal(1, pen.Number);
        Assert.Equal(112_320.0, proxy, 6);
        Assert.Equal(proxy, pen.VolumeM3, 6);
        Assert.Equal(proxy, PenGeometry.VolumeOf(pen.CircumferenceM, pen.NetWallDepthM, pen.ConeDepthM), 6);
        Assert.All(loaded.PhysicalPens.Skip(1), p => Assert.Equal(61_115.498, p.VolumeM3, 2));
        Assert.Equal(proxy, resumed.DensityVolumeM3(resumed.HoldingOf(loaded.Pens.Single()), loaded.Pens.Single()), 6);
    }
}
