using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using LakskongenSim.Core;
using LakskongenSim.Farm;
using LakskongenSim.Farm.Land;
using LakskongenSim.Farm.Offshore;
using LakskongenSim.Finance;
using LakskongenSim.Tests.FacilityFinance;
using LakskongenSim.Tests.Farm;
using LakskongenSim.Tests.Integration;
using Xunit;

namespace LakskongenSim.Tests.Core;

/// <summary>
/// The facility presenter's read (roadmap F2-20, docs/FARM_TYPES_SPEC.md §5,
/// §6.3): <see cref="SimulationEngine.FacilityVisualViews"/> reports the
/// engine's own units (their plots, lots and tanks or zones, their fish) and
/// their builds as the engine's construction projects stand (the land lane's
/// F2-14 phases, the sea lane's F2-07 phases, tanks handed over as the phases
/// end), a legacy unit's from its own progress, and it is a pure read.
/// </summary>
public class FacilityVisualViewsTests
{
    private static string Digest(SimulationEngine engine)
    {
        engine.PrepareForSave();
        var text = new StringBuilder(JsonSerializer.Serialize(engine.State, SaveJson.Options));
        foreach (var kv in engine.Rng.CaptureStates().OrderBy(k => k.Key, StringComparer.Ordinal))
            text.Append('\n').Append(kv.Key).Append('=').Append(kv.Value);
        return text.ToString();
    }

    private static FacilityVisualView ViewOf(SimulationEngine engine, string unitId)
        => Assert.Single(engine.FacilityVisualViews(), v => v.UnitId == unitId);

    /// <summary>A game ready for land with every permit decided approved on its day.</summary>
    private static SimulationEngine PermittedEngine(int seed, LandTemplate template, string plot = "pad_a")
    {
        var engine = LandPermitTests.LandReadyEngine(seed);
        LandPermitTests.GrantEverything(engine);
        Assert.True(engine.ApplyLandPermits(plot, template).Ok);
        engine.Advance(engine.PermitCases.Max(c => c.DecisionDay) - engine.State.Day);
        Assert.All(engine.PermitCases, c => Assert.Equal(PermitStatus.Approved, c.Status));
        return engine;
    }

    [Fact]
    public void AGameWithoutFarmTypeUnits_HasNoViews()
    {
        var engine = OffshoreTestUnits.RichEngine(3);
        Assert.Empty(engine.FacilityVisualViews());
        Assert.Empty(new SimulationEngine().FacilityVisualViews());
    }

    [Fact]
    public void CommissionedLandSites_AreTheirLotsAndTanks_OnTheirPlots_Operational()
    {
        var engine = LandPermitTests.LandReadyEngine(11); // a post-smolt module on pad_delta
        var built = engine.CommissionLandSite("pad_a", LandTemplate.HfsLot, 2);
        Assert.True(built.Ok, built.Message);
        var views = engine.FacilityVisualViews();
        Assert.Equal(engine.LandSites.Select(s => s.Id), views.Select(v => v.UnitId));

        var hfs = ViewOf(engine, built.Id!);
        var site = engine.FindLandSite(built.Id!)!;
        var plot = engine.FindPlot("pad_a")!;
        Assert.Equal((FarmTypeIds.Land, FarmTypeIds.Hfs, "pad_a"), (hfs.Family, hfs.Variant, hfs.PlotId));
        Assert.Equal((plot.WorldX, plot.WorldZ), (hfs.PlotX, hfs.PlotZ));
        Assert.Equal(site.Lots.Select(l => l.Id), hfs.Lots.Select(l => l.LotId));
        Assert.Equal(site.AllTanks().Count(), hfs.TankCount);
        Assert.Equal(28, hfs.TankCount);
        foreach (var lot in hfs.Lots)
        {
            Assert.Equal("hfs_lot", lot.Template);
            Assert.Equal(Enumerable.Repeat("grow_out", 12).Concat(Enumerable.Repeat("pre_grow_out", 2)), lot.Tanks.Select(t => t.Role));
            Assert.All(lot.Tanks.Take(12), t => Assert.Equal(28.0, t.DiameterM));
            Assert.All(lot.Tanks.Skip(12), t => Assert.Equal(16.0, t.DiameterM));
            Assert.All(lot.Tanks, t => Assert.True(t.Ready && !t.Stocked && t.FishCount == 0));
            Assert.Equal(FacilityBuildStatus.None, lot.Build.Status);
            Assert.True(lot.Build.IsBuilt);
            Assert.Null(lot.Expansion);
        }
        Assert.Equal(site.AllTanks().Select(t => t.Pen.Id), hfs.Lots.SelectMany(l => l.Tanks).Select(t => t.Id));
        Assert.True(hfs.Operational && !hfs.Feeding && !hfs.Legacy);
        Assert.Equal(FacilityBuildStatus.Operational, hfs.Build.PhaseKey);

        var smolt = Assert.Single(views, v => v.Variant == FarmTypeIds.PostSmolt);
        Assert.Equal(("pad_delta", true), (smolt.PlotId, smolt.HasRiverIntake));
        var module = Assert.Single(smolt.Lots);
        Assert.Equal("post_smolt_module", module.Template);
        Assert.Equal(new[] { "post_smolt", "post_smolt" }, module.Tanks.Select(t => t.Role));
        Assert.All(module.Tanks, t => Assert.Equal(16.0, t.DiameterM));
        Assert.False(hfs.HasRiverIntake);
    }

    [Fact]
    public void AStockedTank_IsTheTankHoldingFish_AndTheSiteFeeds()
    {
        var engine = LandPermitTests.LandReadyEngine(12);
        var built = engine.CommissionLandSite("pad_a", LandTemplate.HfsLot);
        Assert.True(built.Ok, built.Message);
        var site = engine.FindLandSite(built.Id!)!;
        string tankId = site.AllTanks().First().Pen.Number.ToString("00");
        var stocked = engine.StockLandTank(site.Id, "t" + tankId, 50_000);
        Assert.True(stocked.Ok, stocked.Message);
        var view = ViewOf(engine, site.Id);
        var tanks = view.Lots.SelectMany(l => l.Tanks).ToList();
        Assert.True(tanks[0].Stocked);
        Assert.Equal(site.PopulationIn(site.AllTanks().First())!.TotalCount, tanks[0].FishCount);
        Assert.All(tanks.Skip(1), t => Assert.False(t.Stocked));
        Assert.Equal(tanks[0].FishCount, view.FishCount);
        Assert.True(view.Feeding);
        Assert.Equal(site.GetTotalBiomassKg() / 1000.0, view.BiomassTonnes);
    }

    /// <summary>
    /// A land build through the player's verbs, day by day: the lot's build
    /// walks its project's phases in the project's order (F2-14's keys),
    /// progress never falls inside a phase, the tanks are handed over as the
    /// first-tanks and completion phases end (half the grow-out tanks, then
    /// all 14), and the site is operational from its first ready tank.
    /// </summary>
    [Fact]
    public void ALandBuild_WalksItsProjectsPhasesInOrder_AndHandsOverTheTanksAsThePhasesEnd()
    {
        var engine = PermittedEngine(21, LandTemplate.HfsLot);
        var built = engine.BuildLandSite("pad_a", LandTemplate.HfsLot);
        Assert.True(built.Ok, built.Message);
        var site = engine.FindLandSite(built.Id!)!;
        var lot = Assert.Single(site.Lots);
        var project = engine.ConstructionProjects.Single(p => p.Id == lot.ConstructionProjectId);
        Assert.Equal(engine.LandPhaseKeys(LandTemplate.HfsLot), project.Phases.Select(p => p.Key));

        var keys = new List<string>();
        int firstIndex = project.Phases.FindIndex(p => p.Key == LandConstructionPhases.FirstTanks);
        var last = ViewOf(engine, site.Id);
        Assert.Equal((FacilityBuildStatus.Building, LandConstructionPhases.SiteWorks, 0), (last.Build.Status, last.Build.PhaseKey, last.Build.PhaseIndex));
        Assert.False(last.Operational);
        for (int guard = 0; guard < 1200; guard++)
        {
            var view = ViewOf(engine, site.Id);
            var build = view.Lots[0].Build;
            Assert.Equal(build, view.Build); // the site's own build is its first lot's
            if (keys.Count == 0 || keys[^1] != build.PhaseKey) keys.Add(build.PhaseKey);
            int ready = view.Lots[0].Tanks.Count(t => t.Ready);
            if (build.IsBuilt)
            {
                Assert.Equal(14, ready);
                Assert.True(view.Operational);
                break;
            }
            Assert.Equal(project.CurrentPhase, build.PhaseIndex);
            Assert.Equal(project.Phases.Count, build.PhaseCount);
            Assert.Equal(engine.LandConstructionViews().Single().PhaseProgress, build.PhaseProgress);
            if (build.PhaseKey == last.Build.PhaseKey)
                Assert.True(build.PhaseProgress >= last.Build.PhaseProgress - 1e-12, $"day {engine.State.Day}: progress fell");
            Assert.Equal(build.PhaseIndex > firstIndex ? 6 : 0, ready); // half the grow-out tanks at the first commissioning
            Assert.Equal(ready > 0, view.Operational);
            last = view;
            engine.Advance(1);
        }
        Assert.Equal(project.Phases.Select(p => p.Key).Append(FacilityBuildStatus.Operational), keys);
    }

    /// <summary>
    /// A land phase that cannot start says why (wave4b review, spec §6.3): the
    /// sea works reached in May wait for the open season ("closed_season",
    /// not suspended: they never started) and start in July; a phase whose
    /// start payment the cash cannot meet waits for the money ("funds") and
    /// starts once the money is there. A phase under way waits for nothing.
    /// </summary>
    [Fact]
    public void AWaitingLandPhase_SaysWhy_TheClosedSeasonOrTheMoney()
    {
        var engine = PermittedEngine(22, LandTemplate.HfsLot);
        var built = engine.BuildLandSite("pad_a", LandTemplate.HfsLot);
        Assert.True(built.Ok, built.Message);
        var site = engine.FindLandSite(built.Id!)!;
        var project = engine.ConstructionProjects.Single(p => p.Id == site.Lots[0].ConstructionProjectId);
        var building = ViewOf(engine, site.Id).Build;
        Assert.Equal((FacilityBuildStatus.Building, ""), (building.Status, building.WaitingReason));

        // The phase before the sea works ends on a day in May: the sea works cannot start.
        int sea = project.Phases.FindIndex(p => p.Key == LandConstructionPhases.SeaWorks);
        Assert.True(sea > 0);
        Assert.Contains(5, project.Phases[sea].ClosedMonths);
        int guard = 0;
        while (SimCalendar.Month(engine.State.Day) != 5 && guard++ < SimCalendar.DaysPerYear) engine.Advance(1);
        EndCurrentPhaseTomorrow(engine, project, sea - 1);
        engine.Advance(1);
        Assert.Equal(5, SimCalendar.Month(engine.State.Day));
        var closed = ViewOf(engine, site.Id).Build;
        Assert.Equal((FacilityBuildStatus.Waiting, LandConstructionPhases.SeaWorks, sea), (closed.Status, closed.PhaseKey, closed.PhaseIndex));
        Assert.Equal(FacilityBuildStatus.ClosedSeason, closed.WaitingReason);
        Assert.False(closed.Suspended);
        while (SimCalendar.Month(engine.State.Day) != 7 && guard++ < 2 * SimCalendar.DaysPerYear) engine.Advance(1);
        var open = ViewOf(engine, site.Id).Build;
        Assert.Equal((FacilityBuildStatus.Building, LandConstructionPhases.SeaWorks, ""), (open.Status, open.PhaseKey, open.WaitingReason));

        // The sea works end with no cash left: the next phase waits for the money.
        engine.FinanceManager.Spend(engine.FinanceManager.Balance);
        EndCurrentPhaseTomorrow(engine, project, sea);
        engine.Advance(1);
        var broke = ViewOf(engine, site.Id).Build;
        Assert.Equal((FacilityBuildStatus.Waiting, sea + 1), (broke.Status, broke.PhaseIndex));
        Assert.Equal(FacilityBuildStatus.Funds, broke.WaitingReason);
        engine.ReceiveLoan(project.BudgetNok);
        engine.Advance(1);
        var paid = ViewOf(engine, site.Id).Build;
        Assert.Equal((FacilityBuildStatus.Building, sea + 1, ""), (paid.Status, paid.PhaseIndex, paid.WaitingReason));
    }

    /// <summary>Stand <paramref name="project"/> in phase <paramref name="index"/>, under way, its last day today.</summary>
    private static void EndCurrentPhaseTomorrow(SimulationEngine engine, ConstructionProject project, int index)
    {
        project.PhaseCompletedDays.Clear();
        for (int k = 0; k < index; k++) project.PhaseCompletedDays.Add(Math.Max(0, engine.State.Day - (index - k)));
        project.CurrentPhase = index;
        project.Status = ConstructionStatus.Building;
        project.PhaseStartDay = engine.State.Day;
        project.PhaseDays = 1;
        project.SuspendedDays = 0;
    }

    /// <summary>
    /// An OF1-class unit through the sea lane's verbs: its build walks yard
    /// contract, hull, voyage, the tow (waiting for the player's "Start slep
    /// nå", then towing), mooring and commissioning, and the unit is
    /// operational once the project completes.
    /// </summary>
    [Fact]
    public void AnOffshoreBuild_WalksItsProjectsPhasesInOrder_WaitsForTheTow_ThenIsOperational()
    {
        var engine = OffshoreTestUnits.UnlockedForOf1(31);
        var built = engine.BuildOffshoreUnit("off_1", OffshoreType.SemiSubmersible, ContractForm.Managed);
        Assert.True(built.Ok, built.Message);
        var site = engine.FindOffshoreUnit(built.Id!)!;
        OffshoreTestUnits.ShortenProject(engine, site);
        engine.SeaOverrideForTests = (_, _) => 0.5; // calm: the tow holds
        var project = engine.OffshoreProjectOf(site)!;
        var view = ViewOf(engine, site.Id);
        Assert.Equal((FarmTypeIds.Hav, FarmTypeIds.Of1, "off_1"), (view.Family, view.Variant, view.PlotId));
        Assert.Equal((site.PhysicalPens.Count, 0, false), (view.Zones, view.StockedZones, view.Operational));
        Assert.Empty(view.Lots);
        Assert.False(view.Legacy);

        var keys = new List<string>();
        bool waitedForTow = false;
        for (int guard = 0; guard < 400 && !view.Operational; guard++)
        {
            view = ViewOf(engine, site.Id);
            if (keys.Count == 0 || keys[^1] != view.Build.PhaseKey) keys.Add(view.Build.PhaseKey);
            if (view.Build.Status == FacilityBuildStatus.Waiting && view.Build.WaitingReason == "tow")
            {
                waitedForTow = true;
                Assert.Equal(OffshoreProfiles.TowPhase, view.Build.PhaseKey);
                Assert.True(engine.StartTow(site.Id).Ok);
            }
            if (!view.Operational)
            {
                Assert.Equal(project.Phases.Count, view.Build.PhaseCount);
                Assert.InRange(view.Build.Progress, 0.0, 1.0);
            }
            engine.Advance(1);
        }
        Assert.True(view.Operational);
        Assert.True(waitedForTow);
        Assert.Equal(project.Phases.Select(p => p.Key).Append(FacilityBuildStatus.Operational), keys);
        Assert.Equal((FacilityBuildStatus.Complete, project.Phases.Count), (view.Build.Status, view.Build.PhaseIndex));
    }

    /// <summary>The views change nothing: the save and every stream are the same before and after.</summary>
    [Fact]
    public void TheViews_AreAPureRead()
    {
        var engine = OffshoreTestUnits.UnlockedForOf1(41);
        Assert.True(engine.BuildOffshoreUnit("off_1", OffshoreType.SemiSubmersible, ContractForm.Managed).Ok);
        engine.Research.ForceUnlock("land-based-ras");
        Assert.True(engine.CommissionLandSite("pad_delta", LandTemplate.PostSmoltModule, 2).Ok);
        LandPermitTests.GrantEverything(engine);
        Assert.True(engine.ApplyLandPermits("pad_a", LandTemplate.HfsLot).Ok);
        engine.Advance(engine.PermitCases.Max(c => c.DecisionDay) - engine.State.Day);
        Assert.True(engine.BuildLandSite("pad_a", LandTemplate.HfsLot).Ok);
        engine.Advance(30);
        string before = Digest(engine);
        var views = engine.FacilityVisualViews();
        Assert.Equal(3, views.Count);
        Assert.Equal(before, Digest(engine));
    }

    /// <summary>
    /// A legacy land site (no lots: the legacy build, a pre-F2 load) gets the
    /// lots F2-03's migration counts from its size, and its build is its own
    /// 0-1 progress laid over the template's phase plan.
    /// </summary>
    [Fact]
    public void ALegacyLandSite_GetsLotsFromItsSize_AndItsProgressOverTheTemplatesPhases()
    {
        var engine = OffshoreTestUnits.RichEngine(51);
        engine.Research.ForceUnlock("land-based-ras");
        var built = engine.BuildRASFacility("Legacy", RASScale.Large);
        Assert.True(built.Ok, built.Message);
        var site = engine.FindLandSite(built.Id!)!;
        Assert.False(site.HasLots);
        var view = ViewOf(engine, site.Id);
        Assert.True(view.Legacy);
        int lots = Math.Clamp((int)Math.Ceiling(site.CapacityTonnesPerYear / 7_900.0), 1, 4);
        Assert.Equal(lots, view.Lots.Count);
        Assert.All(view.Lots, l => Assert.Equal(14, l.Tanks.Count));
        var keys = engine.LandPhaseKeys(LandTemplate.HfsLot);
        site.ConstructionProgress = 0.0;
        Assert.Equal((keys[0], 0), (ViewOf(engine, site.Id).Build.PhaseKey, ViewOf(engine, site.Id).Build.PhaseIndex));
        site.ConstructionProgress = 0.999;
        Assert.Equal(keys[^1], ViewOf(engine, site.Id).Build.PhaseKey);
        site.ConstructionProgress = 1.0;
        Assert.True(ViewOf(engine, site.Id).Build.IsBuilt);
        Assert.All(ViewOf(engine, site.Id).Lots.SelectMany(l => l.Tanks), t => Assert.True(t.Ready));
    }

    [Fact]
    public void LegacyBuild_LaysProgressOverThePlansDays()
    {
        var plan = new[] { ("a", 100), ("b", 300), ("c", 100) };
        Assert.Equal(("a", 0, 0.0), At(0.0));
        Assert.Equal(("a", 0, 0.5), At(0.1));
        Assert.Equal(("b", 1, 0.0), At(0.2));
        Assert.Equal(("b", 1, 0.5), At(0.5));
        Assert.Equal(("c", 2, 0.5), At(0.9));
        var built = SimulationEngine.LegacyBuild(1.0, plan);
        Assert.True(built.IsBuilt);
        Assert.Equal(FacilityBuildStatus.Operational, built.PhaseKey);
        (string, int, double) At(double p)
        {
            var b = SimulationEngine.LegacyBuild(p, plan);
            return (b.PhaseKey, b.PhaseIndex, Math.Round(b.PhaseProgress, 9));
        }
    }

    /// <summary>The pre-F2 facilities save: its migrated land sites and offshore unit draw as legacy units with their fish.</summary>
    [Fact]
    public void ThePreF2FacilitiesSave_DrawsItsMigratedUnits_WithTheirFish()
    {
        var engine = FarmTypeMoveTests.Resume("as-saved/events");
        var views = engine.FacilityVisualViews();
        Assert.Equal(engine.LandSites.Count + engine.OffshoreUnits.Count, views.Count);
        foreach (var site in engine.LandSites)
        {
            var view = ViewOf(engine, site.Id);
            Assert.Equal(site.Tanks.Sum(p => (long)p.TotalCount), view.FishCount);
            Assert.Equal(site.Tanks.Count(p => p.TotalCount > 0), view.Lots.SelectMany(l => l.Tanks).Count(t => t.Stocked));
        }
        var unit = Assert.Single(engine.OffshoreUnits);
        var offshore = ViewOf(engine, unit.Id);
        Assert.Equal(FarmTypeIds.Of1, offshore.Variant);
        Assert.True(offshore.Legacy);
        Assert.Equal(unit.PhysicalPens.Count(p => unit.PopulationIn(p)?.TotalCount > 0), offshore.StockedZones);
    }
}
