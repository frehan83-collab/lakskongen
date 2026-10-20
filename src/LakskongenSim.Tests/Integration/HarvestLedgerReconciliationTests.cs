using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Farm;
using LakskongenSim.Data;
using LakskongenSim.Finance;
using LakskongenSim.Harvest;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// Wave-2 integration of S2-04 (harvest pipeline) with S2-05 (mortality
/// ledger): every fish a harvest takes leaves its cohort through
/// <see cref="FishPopulation.RemoveAlive"/>, so on every day and for every
/// command a cohort's lost fish equal its deaths by cause plus its live
/// removals, and the fish a harvest report sold are exactly the fish the
/// ledger shows leaving as that harvest. Covers the express verbs (sea site,
/// partial, RAS hall, offshore), a planned order over several slaughter days,
/// a hired harvest wellboat and the Mattilsynet forced harvest.
/// </summary>
public class HarvestLedgerReconciliationTests
{
    private readonly ITestOutputHelper _output;

    public HarvestLedgerReconciliationTests(ITestOutputHelper output) => _output = output;

    /// <summary>po3_red_sone's stocked sea sites plus a land-based RAS facility and an offshore farm, events on.</summary>
    private static SimulationEngine MixedFarm(int seed)
    {
        var engine = new SimulationEngine();
        engine.FinanceManager = new FinanceManager(5_000_000_000m);
        engine.Initialize(enableEvents: true, seed: seed);
        var scenario = engine.Scenario.GetScenario(PlayerScriptProbe.CareerScenarioId);
        Assert.NotNull(scenario);
        scenario.ApplyStartingState(engine.State, engine);
        Assert.True(engine.StockSmolts("po3_nord", 12_000, "robust").Ok);
        Assert.True(engine.StockSmolts("po3_sor", 10_000, "standard").Ok);
        Assert.True(engine.BuildRASFacility("Landtank", RASScale.Small).Ok);
        Assert.True(engine.BuildOffshoreFarm("Havfarm", OffshoreType.SemiSubmersible).Ok);
        var ras = engine.RASFacilities.Single();
        var unit = engine.OffshoreUnits.Single(); // F2-03: the offshore farm is an offshore sea site
        ras.ConstructionDays = 1;
        unit.Offshore!.ConstructionDays = 1;
        engine.Advance(2);
        Assert.True(engine.StockRASFacility(ras.Id, 40_000).Ok);
        Assert.True(engine.StockOffshoreFarm(unit.Id, 40_000).Ok);
        return engine;
    }

    private static IEnumerable<FishPopulation> AllPopulations(SimulationEngine engine)
        => engine.State.Populations
            .Concat(engine.State.SeaSites.SelectMany(s => s.Pens))
            .Concat(engine.RASFacilities.SelectMany(r => r.Tanks))
            .Concat(engine.OffshoreFarms.SelectMany(f => f.Pens))
            .Distinct();

    private static long FishAlive(SimulationEngine engine) => AllPopulations(engine).Sum(p => (long)p.TotalCount);

    /// <summary>
    /// Every cohort's count and what the open ledger day already holds for it
    /// (0 before an <c>Advance</c>: the step opens a new ledger day).
    /// </summary>
    private static Dictionary<FishPopulation.Cohort, (int Count, int Booked)> Snapshot(SimulationEngine engine,
        bool beforeANewDay = false)
    {
        var ledger = engine.MortalityLedger;
        var snapshot = new Dictionary<FishPopulation.Cohort, (int, int)>(ReferenceEqualityComparer.Instance);
        foreach (var pop in AllPopulations(engine))
            foreach (var cohort in pop.Cohorts)
            {
                ledger.TodayByCohort.TryGetValue(cohort, out var rec);
                snapshot[cohort] = (cohort.Count, beforeANewDay ? 0 : (rec?.TotalDeaths ?? 0) + (rec?.TotalRemoved ?? 0));
            }
        return snapshot;
    }

    /// <summary>Each cohort lost exactly the fish booked for it since <paramref name="before"/>.</summary>
    private static void AssertEveryCohortReconciles(SimulationEngine engine,
        Dictionary<FishPopulation.Cohort, (int Count, int Booked)> before, string what)
    {
        var ledger = engine.MortalityLedger;
        foreach (var (cohort, (countBefore, bookedBefore)) in before)
        {
            ledger.TodayByCohort.TryGetValue(cohort, out var rec);
            int booked = (rec?.TotalDeaths ?? 0) + (rec?.TotalRemoved ?? 0) - bookedBefore;
            int lost = countBefore - cohort.Count;
            if (lost != booked)
                Assert.Fail($"{what}: a cohort lost {lost} fish but {booked} were booked on the ledger");
        }
        foreach (var cohort in ledger.TodayByCohort.Keys)
            if (!before.ContainsKey(cohort))
                Assert.Fail($"{what}: a booking for a cohort that did not exist before");
    }

    /// <summary>Run a command and check the cohorts and the removal tally it moved.</summary>
    private static HarvestReport? Command(SimulationEngine engine, string what, FishRemoval removal,
        Func<CommandResult> command)
    {
        var before = Snapshot(engine);
        long removedBefore = engine.MortalityLedger.Cumulative().Removed(removal).Fish;
        long deathsBefore = engine.MortalityLedger.Cumulative().Total.Fish;
        long aliveBefore = FishAlive(engine);
        int reportsBefore = engine.State.HarvestBatchesSold;

        var result = command();
        Assert.True(result.Ok, $"{what}: {result.ErrorKey} {result.Message}");

        AssertEveryCohortReconciles(engine, before, what);
        long removed = engine.MortalityLedger.Cumulative().Removed(removal).Fish - removedBefore;
        Assert.Equal(deathsBefore, engine.MortalityLedger.Cumulative().Total.Fish); // a harvest kills nothing
        Assert.Equal(aliveBefore - removed, FishAlive(engine));
        if (engine.State.HarvestBatchesSold == reportsBefore) return null;
        var report = engine.HarvestReports[^1];
        Assert.Equal(report.Fish, removed);
        return report;
    }

    /// <summary>
    /// Every harvest path on a mixed farm over three seeds: per cohort per day
    /// (and per command) lost = deaths + removals; per harvest the report's
    /// fish = the ledger's removals of that kind; over the run the fish alive
    /// at the start = deaths + removals + fish left.
    /// </summary>
    [Fact]
    public void EveryHarvestPath_BooksTheFishItSells_AsLiveRemovals_PerCohortPerDay()
    {
        const int days = 220;
        long cohortDaysChecked = 0;
        var modesSeen = new HashSet<HarvestMode>();
        for (int seed = 1; seed <= 3; seed++)
        {
            var engine = MixedFarm(seed);
            var ledger = engine.MortalityLedger;
            var start = ledger.Cumulative();
            long aliveAtStart = FishAlive(engine);
            var ras = engine.RASFacilities.Single();
            var farm = engine.OffshoreUnits.Single();
            string orderId = "";

            for (int d = 0; d < days; d++)
            {
                var before = Snapshot(engine, beforeANewDay: true);
                engine.Advance(1);
                int day = engine.State.Day;
                AssertEveryCohortReconciles(engine, before, $"day {day} (seed {seed})");
                cohortDaysChecked += before.Count;

                switch (d)
                {
                    case 100:
                        // A partial express harvest (a quarter of each pen).
                        modesSeen.Add(Command(engine, $"partial express (seed {seed})", FishRemoval.Harvest,
                            () => engine.HarvestNow("po3_nord", null, 0.25))!.Mode);
                        break;
                    case 110:
                        // A planned order: the fish fast and go in batches on later days.
                        var order = engine.OrderHarvest("po3_sor");
                        Assert.True(order.Ok, order.ErrorKey);
                        orderId = order.Id;
                        break;
                    case 120:
                        // A hired harvest wellboat takes the RAS hall's neighbour: the offshore farm, later.
                        modesSeen.Add(Command(engine, $"RAS hall (seed {seed})", FishRemoval.Harvest,
                            () => engine.HarvestRASFacility(ras.Id))!.Mode);
                        break;
                    case 130:
                        // Mattilsynet over-MAB order with a deadline today: the forced harvest
                        // must remove the very fish it sells.
                        var site = engine.State.SeaSites.Single(s => s.Id == "po3_nord");
                        site.MABTonnes = Math.Max(0.5, site.CurrentBiomassTonnes * 0.6);
                        site.MattilsynetDeadlineDay = engine.State.Day;
                        site.MattilsynetOrderFulfilled = false;
                        var forced = Command(engine, $"forced harvest (seed {seed})", FishRemoval.ForcedHarvest,
                            () => { engine.ProcessMattilsynetOverMabOrders(); return CommandResult.Success("forced"); });
                        Assert.NotNull(forced);
                        Assert.Equal(HarvestMode.Forced, forced!.Mode);
                        modesSeen.Add(forced.Mode);
                        break;
                    case 140:
                        modesSeen.Add(Command(engine, $"offshore (seed {seed})", FishRemoval.Harvest,
                            () => engine.HarvestOffshoreFarm(farm.Id))!.Mode);
                        break;
                    case 150:
                        Assert.True(engine.RequestWellboat("po3_nord", "harvest").Ok, "hired harvest wellboat");
                        break;
                }
            }

            // The planned order sold every one of its fish through the ledger.
            Assert.False(string.IsNullOrEmpty(orderId));
            var orderReports = engine.HarvestReports.Where(r => r.OrderId == orderId).ToList();
            Assert.NotEmpty(orderReports);
            orderReports.ForEach(r => modesSeen.Add(r.Mode));
            engine.HarvestReports.Where(r => r.Mode == HarvestMode.HiredWellboat).ToList().ForEach(r => modesSeen.Add(r.Mode));

            var end = ledger.Cumulative();
            long harvested = end.Removed(FishRemoval.Harvest).Fish - start.Removed(FishRemoval.Harvest).Fish;
            long forcedOut = end.Removed(FishRemoval.ForcedHarvest).Fish - start.Removed(FishRemoval.ForcedHarvest).Fish;
            long sold = engine.HarvestReports.Where(r => r.Mode != HarvestMode.Forced).Sum(r => (long)r.Fish);
            long soldForced = engine.HarvestReports.Where(r => r.Mode == HarvestMode.Forced).Sum(r => (long)r.Fish);
            Assert.True(engine.State.HarvestBatchesSold <= engine.HarvestCalibration.ReportsKept,
                "the run must keep every report it checks");
            Assert.Equal(sold, harvested);
            Assert.Equal(soldForced, forcedOut);

            long deaths = end.Total.Fish - start.Total.Fish;
            long otherRemovals = MortalityCauses.AllRemovals
                .Where(r => r is not FishRemoval.Harvest and not FishRemoval.ForcedHarvest)
                .Sum(r => end.Removed(r).Fish - start.Removed(r).Fish);
            Assert.Equal(aliveAtStart, deaths + harvested + forcedOut + otherRemovals + FishAlive(engine));
            _output.WriteLine($"seed {seed}: alive {aliveAtStart:N0} = deaths {deaths:N0} + harvested {harvested:N0} " +
                $"+ forced {forcedOut:N0} + other {otherRemovals:N0} + left {FishAlive(engine):N0}; reports {engine.State.HarvestBatchesSold}");
        }

        _output.WriteLine($"cohort-days checked: {cohortDaysChecked:N0}; modes: {string.Join(", ", modesSeen.OrderBy(m => m))}");
        foreach (var mode in Enum.GetValues<HarvestMode>())
            Assert.Contains(mode, modesSeen);
    }

    /// <summary>
    /// S2-01a's deadline harvest (biomass over the licence MTB, order missed)
    /// goes through the S2-04 pipeline and the S2-05 ledger: one forced report
    /// per site, sold as gutted kilos at 70 % of the price with the express
    /// harvest cost booked as cost of goods sold; the reports' fish are
    /// exactly the fish the ledger shows leaving as forced harvest, the
    /// breach's revenue is their gross sale and its kilos their live weight.
    /// </summary>
    [Fact]
    public void TheCapacityDeadlineHarvest_SellsThroughThePipeline_AndBooksItsFishOnTheLedger()
    {
        for (int seed = 31; seed <= 33; seed++)
        {
            var engine = new SimulationEngine { FinanceManager = new FinanceManager(12_000_000m) };
            engine.Initialize(enableEvents: false, seed: seed);
            Assert.True(engine.StartScenario(PlayerScriptProbe.CareerScenarioId).Ok);
            Assert.True(engine.BuildSeaSite("po3_nord", "utvidelse").Ok);
            var built = engine.State.SeaSites.Single(s => s.Id == "utvidelse");
            const int area = 3;
            int fish = (int)Math.Ceiling((engine.AreaLicenceMtbTonnes(area) - engine.AreaStandingBiomassTonnes(area)) * 1000.0 / 4.0) + 10_000;
            var pop = new FishPopulation(engine.Growth, engine.Mortality);
            pop.AddCohort(new FishPopulation.Cohort(500, fish, 4.0, LifeStage.Seawater) { WeightCv = 0.2 });
            built.Stock(pop);
            engine.State.Populations.Add(pop);

            var before = engine.MortalityLedger.Cumulative();
            int reportsBefore = engine.State.HarvestBatchesSold;
            decimal harvestCostBefore = engine.FinancialStatements.CostBreakdown.TryGetValue("harvest", out var hc) ? hc : 0m;
            engine.Advance(1);
            var breach = Assert.Single(engine.State.CapacityBreaches);
            while (breach.IsOpen) engine.Advance(1);
            Assert.Equal(CapacityBreachStatus.ForcedHarvest, breach.Status);

            var forced = engine.HarvestReports.Skip(engine.HarvestReports.Count - (engine.State.HarvestBatchesSold - reportsBefore))
                .Where(r => r.Mode == HarvestMode.Forced).ToList();
            Assert.NotEmpty(forced);
            var after = engine.MortalityLedger.Cumulative();
            long ledgerForced = after.Removed(FishRemoval.ForcedHarvest).Fish - before.Removed(FishRemoval.ForcedHarvest).Fish;
            Assert.Equal(forced.Sum(r => (long)r.Fish), ledgerForced);
            Assert.Equal(breach.ForcedHarvestKg, forced.Sum(r => r.LiveKg), 6);
            Assert.Equal(breach.ForcedHarvestRevenueNok, forced.Sum(r => r.GrossRevenueNok));
            Assert.Equal(engine.FinancialStatements.RevenueBreakdown["forced_harvest"], forced.Sum(r => r.GrossRevenueNok));
            var cal = engine.HarvestCalibration;
            foreach (var r in forced)
            {
                Assert.Equal(r.LiveKg * cal.HogYieldFraction, r.HogKg, 6);
                Assert.Equal((double)engine.Calibration.Capacity.ForcedHarvestPriceFactor, (double)r.PriceMultiplier, 9);
                Assert.True(r.SurchargeNok > 0m, "a forced harvest pays the express wellboat and waiting pen");
            }
            decimal harvestCost = engine.FinancialStatements.CostBreakdown["harvest"] - harvestCostBefore;
            Assert.Equal(forced.Sum(r => r.HarvestCostNok), harvestCost);
            _output.WriteLine($"seed {seed}: forced {forced.Sum(r => r.Fish):N0} fish, {breach.ForcedHarvestKg / 1000:F1} t live, " +
                $"{forced.Sum(r => r.HogKg) / 1000:F1} t HOG, gross {breach.ForcedHarvestRevenueNok:N0}, cost {harvestCost:N0} NOK");
        }
    }
}
