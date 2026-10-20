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
using LakskongenSim.Farm.Offshore;
using LakskongenSim.Finance;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// F2-03's load migration (docs/FARM_TYPES_SPEC.md §3.8 and §7.1 acceptance
/// 3-5): the frozen pre-F2 facilities save (save_v8_facilities.json) and a
/// synthetic save with more facilities than named plots load into the F2 unit
/// kinds with every fish and kilo, run on bit-identically across processes,
/// migrate once (a second run changes nothing), and leave every non-hatchery
/// population in exactly one unit with its site id and zone id.
/// </summary>
public class FacilityMigrationTests
{
    private readonly ITestOutputHelper _output;
    public FacilityMigrationTests(ITestOutputHelper output) => _output = output;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LakskongenSim.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string FixturePath() => Path.Combine(RepoRoot(), "src", "LakskongenSim.Tests", "Fixtures", "save_v8_facilities.json");

    private static string FixtureGameState()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(FixturePath()).Replace("\r\n", "\n"));
        return doc.RootElement.GetProperty("GameState").GetRawText();
    }

    private static GameState Deserialize(string json)
        => JsonSerializer.Deserialize<GameState>(json, SaveJson.Options) ?? throw new InvalidOperationException("no state");

    private static long Fish(GameState s)
        => s.Populations.Sum(p => (long)p.TotalCount) + s.PostSmoltFacilities.Sum(f => f.Batches.Sum(b => (long)b.Count));

    private static double Kg(GameState s)
        => s.Populations.Sum(p => p.GetTotalBiomassKg())
            + s.PostSmoltFacilities.Sum(f => f.Batches.Sum(b => b.Count * b.AverageWeightG / 1000.0));

    // ── Acceptance 3: the frozen fixture ────────────────────────────────────

    /// <summary>
    /// The offshore farm becomes an offshore sea site with the same id and 3
    /// zones (a semi-submersible), on off_1 (the slot an OF1-class unit is
    /// rated for), holding the farm's two stocked pens in its first two zones
    /// and the farm's live state: capacity, construction, crew, structure,
    /// oxygen boost, carbon; its 12 m "max wave" is the class's 5.5 m design
    /// Hs now, its 14-day crew change the 7-day rota.
    /// </summary>
    [Fact]
    public void TheFixture_OffshoreFarm_BecomesAnOffshoreSeaSite_WithTheSameId_AndThreeZones()
    {
        var saved = Deserialize(FixtureGameState());
        var farm = saved.OffshoreFarms.Single();
        var engine = SaveStateComparer.Resume(FixtureGameState(), events: true);

        var unit = engine.OffshoreUnits.Single();
        Assert.Equal(farm.Id, unit.Id);
        Assert.Contains(unit, engine.State.SeaSites);
        Assert.Equal((SiteKind.Offshore, "off_1", "off_1", farm.Name), (unit.Kind, unit.PlotId, unit.ZoneId, unit.Label));
        Assert.Equal(unit.Id, engine.FindPlot("off_1")!.OwnerUnitId);
        Assert.Equal(3, unit.PhysicalPens.Count);
        Assert.Equal(farm.PenCount * farm.PenVolumeM3, unit.PhysicalPens.Sum(p => p.VolumeM3), 6);
        Assert.All(unit.PhysicalPens, p => Assert.Equal(farm.PenCount * farm.PenVolumeM3 / 3, p.VolumeM3, 9));
        Assert.Equal(farm.CapacityTonnes, unit.MABTonnes);
        Assert.Equal(SimulationEngine.OffshoreWatchMode, unit.WatchMode);

        // The farm's pens, in order, are the populations of zones 1 and 2.
        Assert.Equal(farm.Pens.Select(p => p.Id), unit.Pens.Select(p => p.Id));
        Assert.Equal(new[] { 1, 2 }, unit.Pens.Select(p => unit.PenOf(p)!.Number));
        Assert.True(unit.PhysicalPens[2].IsEmpty);
        Assert.All(unit.Pens, p => Assert.Equal((unit.Id, unit.ZoneId), (p.SiteId, p.ZoneId)));
        Assert.Equal(farm.Pens.Sum(p => (long)p.TotalCount), unit.Pens.Sum(p => (long)p.TotalCount));

        var structure = unit.Offshore!;
        Assert.Equal(farm.FarmType, structure.FarmType);
        Assert.Equal(12.0, farm.MaxWaveHeightM);
        Assert.Equal(5.5, structure.DesignHsM);
        Assert.Equal(14, farm.CrewRotationDays);
        Assert.Equal(7, structure.CrewRotationDays);
        Assert.Equal((farm.EmployeeCount, farm.CAPEX, farm.ConstructionProgress, farm.ConstructionDays, farm.ConstructionStartDay),
            (structure.EmployeeCount, structure.CAPEX, structure.ConstructionProgress, structure.ConstructionDays, structure.ConstructionStartDay));
        Assert.Equal((farm.StructuralIntegrity, farm.OxygenBoost, farm.DaysSinceCrewRotation),
            (structure.StructuralIntegrity, structure.OxygenBoost, structure.DaysSinceCrewRotation));
        Assert.True(structure.IsOperational);
        Assert.Equal(farm.CarbonTracker.Emissions.Count, unit.CarbonTracker.Emissions.Count);
        Assert.NotEmpty(unit.CarbonTracker.Emissions);
        Assert.Empty(engine.OffshoreFarms);
    }

    /// <summary>
    /// The post-smolt facility becomes a post-smolt land site with the same id
    /// on pad_delta (4 modules: its 10 M smolts over 1.1 M a module, capped at
    /// the site's 4); each batch becomes a population of its count, weight and
    /// age (smolt age + days in the facility) in the site's tanks, with the
    /// batch's id and vaccination; the RAS facility goes on pad_a.
    /// </summary>
    [Fact]
    public void TheFixture_PostSmoltBatches_BecomePopulations_AndTheLandSitesGoOnTheirPads()
    {
        var saved = Deserialize(FixtureGameState());
        var legacy = saved.PostSmoltFacilities.Single();
        var engine = SaveStateComparer.Resume(FixtureGameState(), events: true);

        var site = engine.PostSmoltSites.Single();
        Assert.Equal((legacy.Id, legacy.Name, LandSiteKind.PostSmolt, "pad_delta", "pad_delta"),
            (site.Id, site.Name, site.Kind, site.PlotId, site.ZoneId));
        Assert.Equal(site.Id, engine.FindPlot("pad_delta")!.OwnerUnitId);
        Assert.Equal(10_000_000, legacy.AnnualSmoltCapacity);
        Assert.Equal(4, SimulationEngine.PostSmoltModulesOf(site));
        Assert.Equal((8, 1_600.0, 1_920.0), (site.TankCount, site.TankVolumeM3, site.CapacityTonnesPerYear));
        Assert.Equal((legacy.CAPEX, legacy.ConstructionProgress, legacy.ConstructionDays, legacy.ConstructionStartDay, legacy.EmployeeCount),
            (site.CAPEX, site.ConstructionProgress, site.ConstructionDays, site.ConstructionStartDay, site.EmployeeCount));
        Assert.Equal(legacy.WaterTemp, site.TargetWaterTemp);
        Assert.True(site.IsOperational);

        Assert.Equal(legacy.Batches.Select(b => b.Id), site.Tanks.Select(p => p.Id));
        for (int i = 0; i < legacy.Batches.Count; i++)
        {
            var batch = legacy.Batches[i];
            var population = site.Tanks[i];
            var cohort = population.Cohorts.Single();
            Assert.Equal(batch.Count, cohort.Count);
            Assert.Equal(batch.AverageWeightG / 1000.0, cohort.MeanWeightKg);
            Assert.Equal(engine.Hatchery.MaturationDays + batch.DaysInFacility, cohort.AgeDays);
            Assert.Equal(LifeStage.Smolt, cohort.Stage);
            Assert.NotNull(cohort.Genetics);
            Assert.Equal(batch.VaccinationComplete ? engine.State.Day : -1, population.VaccinatedDay);
            Assert.Equal((site.Id, site.ZoneId, 0.0), (population.SiteId, population.ZoneId, population.SeaLiceLoad));
            Assert.Contains(population, engine.State.Populations);
            Assert.Equal(HoldingKind.LandTank, engine.HoldingOf(population).Kind);
        }
        Assert.Empty(engine.PostSmoltFacilities);

        var ras = engine.RASFacilities.Single(f => f.Kind == LandSiteKind.GrowOut);
        Assert.Equal((saved.RASFacilities.Single().Id, "pad_a", "pad_a"), (ras.Id, ras.PlotId, ras.ZoneId));
        Assert.All(ras.Tanks, t => Assert.Equal((ras.Id, "pad_a"), (t.SiteId, t.ZoneId)));
        Assert.Equal(ras.Id, engine.FindPlot("pad_a")!.OwnerUnitId);
        Assert.True(engine.FindPlot("pad_b")!.IsFree);
    }

    /// <summary>Acceptance 3: fish count and biomass are equal to 1e-9, in total and unit by unit.</summary>
    [Fact]
    public void TheFixture_KeepsEveryFishAndKilo()
    {
        var saved = Deserialize(FixtureGameState());
        var engine = SaveStateComparer.Resume(FixtureGameState(), events: true);

        Assert.Equal(Fish(saved), engine.State.Populations.Sum(p => (long)p.TotalCount));
        Assert.Equal(Kg(saved), engine.State.Populations.Sum(p => p.GetTotalBiomassKg()), 9);
        var unit = engine.OffshoreUnits.Single();
        Assert.Equal(saved.OffshoreFarms.Single().Pens.Sum(p => p.GetTotalBiomassKg()), unit.Pens.Sum(p => p.GetTotalBiomassKg()), 9);
        Assert.Equal(saved.RASFacilities.Single().GetTotalBiomassKg(),
            engine.RASFacilities.Single(f => f.Kind == LandSiteKind.GrowOut).GetTotalBiomassKg(), 9);
        var batches = saved.PostSmoltFacilities.Single().Batches;
        Assert.Equal(batches.Sum(b => b.Count * b.AverageWeightG / 1000.0), engine.PostSmoltSites.Single().GetTotalBiomassKg(), 9);
        foreach (var site in saved.SeaSites)
            Assert.Equal(site.Pens.Sum(p => p.GetTotalBiomassKg()),
                engine.State.SeaSites.Single(s => s.Id == site.Id).Pens.Sum(p => p.GetTotalBiomassKg()), 9);
    }

    /// <summary>
    /// Acceptance 3: running the migration twice changes nothing, at once or
    /// across a save and a load of the migrated game; the report says so.
    /// </summary>
    [Fact]
    public void RunningTheMigrationTwice_ChangesNothing()
    {
        var engine = SaveStateComparer.Resume(FixtureGameState(), events: true);
        string once = SaveStateComparer.Normalize(SaveStateComparer.Serialize(engine));

        var again = engine.MigrateFacilities();
        Assert.True(again.IsEmpty, again.ToString());
        Assert.Equal(once, SaveStateComparer.Normalize(SaveStateComparer.Serialize(engine)));

        var reloaded = SaveStateComparer.Resume(SaveStateComparer.Serialize(engine), events: true);
        Assert.True(reloaded.MigrateFacilities().IsEmpty);
        string afterReload = SaveStateComparer.Normalize(SaveStateComparer.Serialize(reloaded));
        Assert.True(once == afterReload, "a reload of the migrated game changed it: " + SaveStateComparer.FirstDifference(once, afterReload));
    }

    /// <summary>
    /// Acceptance 3: 30 days of continuation after the migration are
    /// bit-identical across two OS processes (src/StateHashProbe, no shared
    /// hash seed) and this one.
    /// </summary>
    [Fact]
    public void ThirtyDaysOfContinuation_AreBitIdenticalAcrossTwoProcesses()
    {
        string save = File.ReadAllText(FixturePath()).Replace("\r\n", "\n");
        string inProcess = StateHashProbe.StateHash.ComputeSaveContinuation(save, 30, events: true);
        string first = RunProbe(30);
        string second = RunProbe(30);
        _output.WriteLine($"in process {inProcess}; process 1 {first}; process 2 {second}");
        Assert.Equal(inProcess, first);
        Assert.Equal(first, second);
        // The continuation is the migrated game's (it differs from no-continuation).
        Assert.NotEqual(StateHashProbe.StateHash.ComputeSaveContinuation(save, 0, events: true), inProcess);
    }

    private static string RunProbe(int days)
    {
        var (exitCode, stdout, stderr) = CrossProcessDeterminismTests.RunProcess(CrossProcessDeterminismTests.DotnetHost(),
            $"\"{CrossProcessDeterminismTests.ProbePath()}\" save-continuation \"{FixturePath()}\" {days} true", TimeSpan.FromSeconds(120));
        Assert.True(exitCode == 0, $"probe failed ({exitCode}): {stderr}");
        string hash = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).LastOrDefault() ?? "";
        Assert.Matches("^[0-9A-F]{64}$", hash);
        return hash;
    }

    // ── Acceptance 4: more facilities than named plots ───────────────────────

    /// <summary>
    /// A pre-F2 game state built by hand with 4 RAS facilities and 3 offshore
    /// farms, all stocked, saved the pre-F2 way (legacy lists, no plots).
    /// </summary>
    private static (string Json, long Fish, double Kg) SyntheticLegacySave()
    {
        var engine = new SimulationEngine { FinanceManager = new FinanceManager(10_000_000_000m) };
        engine.Initialize(enableEvents: false, seed: 11);
        Assert.True(engine.StartScenario(PlayerScriptProbe.CareerScenarioId).Ok);
        var strain = SmoltStrainRegistry.Get("standard");
        int n = 0;
        FishPopulation Stocked(string siteId, int count, double kg)
        {
            var pop = new FishPopulation { Id = $"legacy-pop-{++n}", SiteId = siteId, SeaLiceLoad = 0.1 };
            pop.InjectDependencies(engine.Growth, engine.Mortality);
            pop.Cohorts.Add(new FishPopulation.Cohort(500, count, kg, LifeStage.Seawater, strain.BuildGenome()));
            pop.Cohorts.Add(new FishPopulation.Cohort(420, count / 3, kg * 0.6, LifeStage.Seawater, strain.BuildGenome()) { Quality = SmoltQualityTier.Large });
            engine.State.Populations.Add(pop);
            return pop;
        }
        var scales = new[] { RASScale.Large, RASScale.Small, RASScale.Medium, RASScale.Small };
        for (int i = 0; i < scales.Length; i++)
        {
            var ras = new RASFacility
            {
                Id = $"ras_{i + 1}", Name = $"Land {i + 1}", Scale = scales[i], ConstructionProgress = 1.0,
                CapacityTonnesPerYear = RASFacility.DefaultCapacityTonnesPerYear(scales[i]),
            };
            ras.Tanks.Add(Stocked(ras.Id, 10_000 + i, 1.0 + i));
            engine.State.RASFacilities.Add(ras);
        }
        var types = new[] { OffshoreType.SemiSubmersible, OffshoreType.SemiSubmersible, OffshoreType.ShipShaped };
        for (int i = 0; i < types.Length; i++)
        {
            var farm = new OffshoreFarm { Id = $"offshore_{i + 1}", Name = $"Hav {i + 1}", FarmType = types[i], ConstructionProgress = 1.0 };
            for (int p = 0; p < 7; p++)
                farm.Pens.Add(Stocked(farm.Id, 20_000 + 100 * p + i, 2.0 + 0.1 * p));
            engine.State.OffshoreFarms.Add(farm);
        }
        // The pre-F2 save: no plots, the legacy lists (written as the pre-F2 engine wrote them).
        engine.PrepareForSave();
        string json = JsonSerializer.Serialize(engine.State, SaveJson.Options);
        var state = Deserialize(json);
        Assert.Empty(state.SitePlots);
        return (json, Fish(state), Kg(state));
    }

    [Fact]
    public void ASyntheticSave_WithFourRasFacilitiesAndThreeOffshoreFarms_MigratesOntoOverflowPlots_WithNoLostFish()
    {
        var (json, fish, kg) = SyntheticLegacySave();
        var engine = SaveStateComparer.Resume(json, events: false);

        Assert.Equal(fish, engine.State.Populations.Sum(p => (long)p.TotalCount));
        Assert.Equal(kg, engine.State.Populations.Sum(p => p.GetTotalBiomassKg()), 9);
        Assert.Empty(engine.OffshoreFarms);

        // RAS: Large (20,000 t = 3 lots) takes pad_a, Small pad_b (2 lots),
        // then the pads are taken: Medium and the second Small go to overflow.
        var plots = engine.RASFacilities.ToDictionary(f => f.Id, f => f.PlotId);
        Assert.Equal("pad_a", plots["ras_1"]);
        Assert.Equal("pad_b", plots["ras_2"]);
        Assert.Equal("overflow_land_1", plots["ras_3"]);
        Assert.Equal("overflow_land_2", plots["ras_4"]);
        Assert.True(engine.FindPlot("pad_delta")!.IsFree); // no grow-out lots there
        // Offshore: OF1-class takes off_1, the second OF1-class overflow, the ship off_2.
        var units = engine.OffshoreUnits.ToDictionary(u => u.Id);
        Assert.Equal("off_1", units["offshore_1"].PlotId);
        Assert.Equal("overflow_offshore_1", units["offshore_2"].PlotId);
        Assert.Equal("off_2", units["offshore_3"].PlotId);
        Assert.True(engine.FindPlot("hav_1")!.IsFree);

        // Every unit on its own plot, every overflow plot it took held in the save.
        var owners = engine.State.SitePlots.Where(p => !p.IsFree).ToDictionary(p => p.Id, p => p.OwnerUnitId);
        Assert.Equal(7, owners.Count);
        foreach (var land in engine.RASFacilities)
            Assert.Equal(land.Id, owners[land.PlotId]);
        foreach (var unit in engine.OffshoreUnits)
            Assert.Equal(unit.Id, owners[unit.PlotId]);
        Assert.Equal(new[] { "overflow_land_1", "overflow_land_2", "overflow_offshore_1" },
            engine.State.SitePlots.Where(p => p.IsOverflow).Select(p => p.Id).OrderBy(i => i, StringComparer.Ordinal));
        // Seven pens merged pairwise into a semi-submersible's 3 zones and a ship's 6, cohorts whole.
        Assert.Equal(3, units["offshore_1"].Pens.Count);
        Assert.Equal(6, units["offshore_3"].Pens.Count);
        Assert.Equal(14, units["offshore_1"].Pens.Sum(p => p.Cohorts.Count));
        Assert.Equal(14, units["offshore_3"].Pens.Sum(p => p.Cohorts.Count));
        Assert.Contains(units["offshore_1"].Pens.SelectMany(p => p.Cohorts), c => c.Quality == SmoltQualityTier.Large);
        foreach (var unit in engine.OffshoreUnits)
            Assert.All(unit.Pens, p => Assert.Same(p, unit.PopulationIn(unit.PenOf(p)!)));
        Assert.Empty(UnitResolver.Violations(engine.State));

        // The migrated game runs and a second load changes nothing.
        engine.Advance(10);
        Assert.Empty(UnitResolver.Violations(engine.State));
        Assert.True(engine.MigrateFacilities().IsEmpty);
    }

    /// <summary>
    /// Pairwise merges (spec §3.8 step 1): seven pens into three zones keep
    /// every cohort whole and in order of their pens, merge (1,2), (3,4), (5,6)
    /// and then the first pair with the next, and weight the lice load by fish.
    /// </summary>
    [Fact]
    public void SevenPens_MergePairwiseIntoThreeZones_KeepingEveryCohortWhole()
    {
        var (json, _, _) = SyntheticLegacySave();
        var saved = Deserialize(json);
        var farm = saved.OffshoreFarms.First();
        var engine = SaveStateComparer.Resume(json, events: false);
        var unit = engine.OffshoreUnits.Single(u => u.Id == farm.Id);

        var legacyIds = farm.Pens.Select(p => p.Id).ToList();
        Assert.Equal(new[] { legacyIds[0], legacyIds[4], legacyIds[6] }, unit.Pens.Select(p => p.Id));
        var counts = farm.Pens.Select(p => p.Cohorts.Select(c => c.Count).ToList()).ToList();
        Assert.Equal(counts[0].Concat(counts[1]).Concat(counts[2]).Concat(counts[3]), unit.Pens[0].Cohorts.Select(c => c.Count));
        Assert.Equal(counts[4].Concat(counts[5]), unit.Pens[1].Cohorts.Select(c => c.Count));
        Assert.Equal(counts[6], unit.Pens[2].Cohorts.Select(c => c.Count));
        Assert.All(farm.Pens.Skip(1).Where(p => !unit.Pens.Any(u => u.Id == p.Id)),
            gone => Assert.DoesNotContain(engine.State.Populations, p => p.Id == gone.Id));
        Assert.Equal(0.1, unit.Pens[0].SeaLiceLoad, 12);
    }

    // ── Acceptance 5: one unit per population ───────────────────────────────

    /// <summary>
    /// Every non-hatchery population resolves to exactly one unit with a
    /// non-empty site id and zone id, on the migrated fixture and on a game
    /// that builds every kind through the verbs, day after day; the hatchery
    /// cohort is the listed exemption.
    /// </summary>
    [Fact]
    public void EveryNonHatcheryPopulation_ResolvesToOneUnit_WithSiteAndZoneIds()
    {
        var fixture = SaveStateComparer.Resume(FixtureGameState(), events: true);
        var hatchery = fixture.State.Populations.Where(p => UnitResolver.Resolve(fixture.State, p).IsNone).ToList();
        var exempt = Assert.Single(hatchery);
        Assert.True(UnitResolver.IsHatcheryExempt(exempt));
        Assert.All(exempt.Cohorts, c => Assert.Equal(LifeStage.Egg, c.Stage));
        for (int d = 0; d < 40; d++)
        {
            Assert.Empty(UnitResolver.Violations(fixture.State));
            foreach (var population in fixture.State.Populations)
            {
                var unit = UnitResolver.Resolve(fixture.State, population);
                if (unit.IsNone) { Assert.Same(exempt, population); continue; }
                Assert.False(string.IsNullOrEmpty(population.SiteId));
                Assert.False(string.IsNullOrEmpty(population.ZoneId));
                Assert.Equal((unit.Id, unit.ZoneId), (population.SiteId, population.ZoneId));
                Assert.Equal(unit.Kind, fixture.HoldingOf(population).Kind);
            }
            fixture.Advance(1);
        }

        var built = new SimulationEngine { FinanceManager = new FinanceManager(5_000_000_000m) };
        built.Initialize(enableEvents: true, seed: 12);
        Assert.True(built.StartScenario(PlayerScriptProbe.CareerScenarioId).Ok);
        built.FinanceManager.AddRevenue(4_000_000_000m); // the scenario's cash affords no facility; this tests the units
        Assert.True(built.BuildRASFacility("", RASScale.Small).Ok);
        Assert.True(built.BuildOffshoreFarm("", OffshoreType.SemiSubmersible).Ok);
        Assert.True(built.BuildPostSmoltFacility("").Ok);
        built.RASFacilities.ForEach(f => f.ConstructionDays = 1);
        built.OffshoreUnits[0].Offshore!.ConstructionDays = 1;
        built.Advance(2);
        Assert.True(built.StockRASFacility(built.RASFacilities[0].Id, 20_000).Ok);
        Assert.True(built.StockOffshoreFarm(built.OffshoreUnits[0].Id, 20_000).Ok);
        Assert.True(built.StartPostSmoltBatch(built.PostSmoltSites[0].Id, 20_000).Ok);
        for (int d = 0; d < 30; d++)
        {
            built.Advance(1);
            Assert.Empty(UnitResolver.Violations(built.State));
        }
        Assert.Contains(built.State.Populations, p => UnitResolver.Resolve(built.State, p).Kind == HoldingKind.Offshore);
        Assert.Contains(built.State.Populations, p => UnitResolver.Resolve(built.State, p).LandSite?.Kind == LandSiteKind.PostSmolt);
    }

    /// <summary>The invariant check names every way to break it (no vacuous pass).</summary>
    [Fact]
    public void TheUnitCheck_NamesEveryBreak()
    {
        var engine = SaveStateComparer.Resume(FixtureGameState(), events: false);
        Assert.Empty(UnitResolver.Violations(engine.State));
        var unit = engine.OffshoreUnits.Single();
        var ras = engine.RASFacilities.Single(f => f.Kind == LandSiteKind.GrowOut);

        var population = unit.Pens[0];
        ras.Tanks.Add(population); // in two units
        Assert.Contains(UnitResolver.Violations(engine.State), v => v.Contains("held by 2 units"));
        ras.Tanks.Remove(population);

        population.ZoneId = "";
        Assert.Contains(UnitResolver.Violations(engine.State), v => v.Contains("no zone id"));
        population.ZoneId = "po3_nord";
        Assert.Contains(UnitResolver.Violations(engine.State), v => v.Contains("zone id 'po3_nord'"));
        population.ZoneId = unit.ZoneId;
        population.SiteId = "po3_nord";
        Assert.Contains(UnitResolver.Violations(engine.State), v => v.Contains("site id 'po3_nord'"));
        population.SiteId = unit.Id;
        Assert.Empty(UnitResolver.Violations(engine.State));

        var orphan = new FishPopulation { SiteId = "gone", ZoneId = "po3_nord" };
        orphan.Cohorts.Add(new FishPopulation.Cohort(400, 10, 1.0, LifeStage.Seawater));
        engine.State.Populations.Add(orphan);
        Assert.Contains(UnitResolver.Violations(engine.State), v => v.Contains("in no unit"));
        engine.State.Populations.Remove(orphan);

        var stray = new FishPopulation { SiteId = unit.Id, ZoneId = unit.ZoneId };
        unit.Pens.Add(stray); // in a unit but not in the game's list
        Assert.Contains(UnitResolver.Violations(engine.State), v => v.Contains("not in the game's population list"));
    }
}
