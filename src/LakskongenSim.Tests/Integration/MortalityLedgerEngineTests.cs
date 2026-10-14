using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Calibration;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// S2-05 (roadmap D4) on the engine path: every death is booked to one cause,
/// per cohort per day, exactly; the career probe prints the breakdown in the
/// NVI categories; hazards read the cohort's own environment, so a land-based
/// tank and an offshore structure take their own water.
/// </summary>
public class MortalityLedgerEngineTests
{
    private readonly ITestOutputHelper _output;

    public MortalityLedgerEngineTests(ITestOutputHelper output) => _output = output;

    /// <summary>po3_red_sone's stocked sea sites plus a land-based RAS tank and an offshore structure, events on.</summary>
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
        var farm = engine.OffshoreFarms.Single();
        ras.ConstructionDays = 1;
        farm.ConstructionDays = 1;
        engine.Advance(2);
        Assert.True(engine.StockRASFacility(ras.Id, 40_000).Ok);
        Assert.True(engine.StockOffshoreFarm(farm.Id, 40_000).Ok);
        Assert.True(engine.RequestWellboat("po3_sor", "delouse", "hydrolicer").Ok);
        return engine;
    }

    private static Dictionary<FishPopulation.Cohort, int> CountCohorts(SimulationEngine engine)
    {
        var counts = new Dictionary<FishPopulation.Cohort, int>(ReferenceEqualityComparer.Instance);
        IEnumerable<FishPopulation> pops = engine.State.Populations
            .Concat(engine.State.SeaSites.SelectMany(s => s.Pens))
            .Concat(engine.RASFacilities.SelectMany(r => r.Tanks))
            .Concat(engine.OffshoreFarms.SelectMany(f => f.Pens));
        foreach (var pop in pops)
            foreach (var cohort in pop.Cohorts)
                counts[cohort] = cohort.Count;
        return counts;
    }

    /// <summary>
    /// Acceptance 1: for every cohort on every day, the fish it lost equal its
    /// deaths booked by cause plus the fish that left it alive, exactly; and
    /// each day's deaths by cause sum to the day's deaths. Eight seeds, 450
    /// days (two winters' cold, a summer's heat, storms, blooms, delousing).
    /// </summary>
    [Fact]
    public void EveryCohortEveryDay_FishLostEqualDeathsByCausePlusRemovals_Exactly()
    {
        const int days = 450;
        var causesSeen = new HashSet<MortalityCause>();
        var removalsSeen = new HashSet<FishRemoval>();
        var holdingsWithDeaths = new HashSet<string>();
        long cohortDaysChecked = 0;

        for (int seed = 1; seed <= 8; seed++)
        {
            var engine = MixedFarm(seed);
            var ledger = engine.MortalityLedger;
            long deathsBefore = ledger.Cumulative().Total.Fish;
            long deathsSummedDaily = 0;

            for (int d = 0; d < days; d++)
            {
                var before = CountCohorts(engine);
                engine.Advance(1);
                int day = engine.State.Day;
                Assert.Equal(day, ledger.Day);

                long dayDeaths = 0;
                foreach (var (cohort, countBefore) in before)
                {
                    ledger.TodayByCohort.TryGetValue(cohort, out var rec);
                    int booked = (rec?.TotalDeaths ?? 0) + (rec?.TotalRemoved ?? 0);
                    int lost = countBefore - cohort.Count;
                    if (lost != booked)
                        Assert.Fail($"seed {seed} day {day}: a cohort lost {lost} fish but {booked} were booked " +
                            $"(deaths {string.Join(", ", MortalityCauses.All.Where(c => (rec?.DeathsByCause[(int)c] ?? 0) > 0).Select(c => $"{c}={rec.DeathsByCause[(int)c]}"))})");
                    cohortDaysChecked++;
                }
                foreach (var (cohort, rec) in ledger.TodayByCohort)
                {
                    if (!before.ContainsKey(cohort))
                        Assert.Fail($"seed {seed} day {day}: a booking for a cohort that did not exist at the start of the day");
                    dayDeaths += rec.TotalDeaths;
                    foreach (var cause in MortalityCauses.All)
                        if (rec.DeathsByCause[(int)cause] > 0)
                        {
                            causesSeen.Add(cause);
                            holdingsWithDeaths.Add(rec.Population.SiteId);
                        }
                    foreach (var removal in MortalityCauses.AllRemovals)
                        if (rec.RemovedByKind[(int)removal] > 0) removalsSeen.Add(removal);
                }

                var today = ledger.ForDays(day, day);
                if (today.Total.Fish != dayDeaths)
                    Assert.Fail($"seed {seed} day {day}: day total {today.Total.Fish} != cohort bookings {dayDeaths}");
                if (MortalityCauses.All.Sum(c => today.Deaths(c).Fish) != today.Total.Fish
                    || MortalityCauses.AllCategories.Sum(c => today.Deaths(c).Fish) != today.Total.Fish)
                    Assert.Fail($"seed {seed} day {day}: causes or categories do not sum to the day's deaths");
                deathsSummedDaily += dayDeaths;
            }

            var cumulative = ledger.Cumulative();
            Assert.Equal(cumulative.Total.Fish - deathsBefore, deathsSummedDaily);
            Assert.Equal(cumulative.Total.Fish, MortalityCauses.AllCategories.Sum(c => cumulative.Deaths(c).Fish));
            Assert.Equal(cumulative.Total.Fish, ledger.SiteIds.Sum(id => ledger.ForSite(id).Total.Fish));
        }

        _output.WriteLine($"cohort-days checked: {cohortDaysChecked:N0}; causes: {string.Join(", ", causesSeen.OrderBy(c => c))}; " +
            $"left alive: {string.Join(", ", removalsSeen.OrderBy(r => r))}; holdings with deaths: {string.Join(", ", holdingsWithDeaths.OrderBy(h => h))}");
        // The run must exercise the hazards it claims to check (no vacuous pass).
        foreach (var cause in new[] { MortalityCause.BaselineAnchor, MortalityCause.Disease, MortalityCause.Delousing,
                     MortalityCause.Temperature, MortalityCause.Storm })
            Assert.Contains(cause, causesSeen);
        Assert.True(cohortDaysChecked > 8 * days * 3, $"only {cohortDaysChecked} cohort-days checked");
    }

    /// <summary>
    /// Acceptance 2-3: the career probe accounts for every fish and prints the
    /// breakdown in the NVI categories (seed 13 is the input to S4-03, D4).
    /// The fish at the start equal the deaths by cause plus the fish that left
    /// alive plus the fish still there; the harvest removal equals the fish
    /// counted at harvest.
    /// </summary>
    [Theory]
    [InlineData(7)]
    [InlineData(13)]
    public void CareerProbe_AccountsForEveryFish_AndPrintsTheBreakdownInNviCategories(int seed)
    {
        var r = PlayerScriptProbe.RunCareer(seed);
        var m = r.Mortality;
        string table = PlayerScriptProbe.FormatMortality(seed, r);
        _output.WriteLine(table);

        long leftAlive = MortalityCauses.AllRemovals.Sum(k => m.Removed(k).Fish);
        Assert.Equal((long)r.FishBeforeGrowOut, m.Total.Fish + leftAlive + (long)r.FishLeftAtEnd);
        Assert.Equal((long)r.FishAtHarvest, m.Removed(FishRemoval.Harvest).Fish);
        // The probe's sea loss is exactly the ledger's deaths plus escapes and forced harvest.
        double ledgerLossPct = 100.0 * (m.Total.Fish + m.Removed(FishRemoval.Escape).Fish
            + m.Removed(FishRemoval.ForcedHarvest).Fish + m.Removed(FishRemoval.TransferOut).Fish) / r.FishBeforeGrowOut;
        Assert.Equal(r.SeaMortalityPct, ledgerLossPct, 9);
        foreach (var category in MortalityCauses.AllCategories)
        {
            Assert.Contains(MortalityCauses.Id(category), table);
            string code = MortalityCauses.NviCode(category);
            if (code.Length > 0) Assert.Contains($" {code} ", table);
        }
        Assert.True(m.Total.Fish > 0, "a career with no deaths booked");
    }

    /// <summary>
    /// The farm-type seam: the temperature hazard reads the water the fish sit
    /// in. A land-based tank whose chiller fails (water at 0 °C) loses fish
    /// to temperature while the sea at 10 °C spares the sea pen, and the
    /// reverse when the sea is cold and the tank holds 12 °C.
    /// </summary>
    [Fact]
    public void TemperatureStress_ReadsTheHoldingsOwnWater_SoALandTankFailureKillsAndAColdSeaDoesNot()
    {
        var engine = new SimulationEngine();
        engine.FinanceManager = new FinanceManager(5_000_000_000m);
        engine.Initialize(enableEvents: false, seed: 71);
        Assert.True(engine.BuildRASFacility("Landtank", RASScale.Small).Ok);
        var ras = engine.RASFacilities.Single();
        ras.ConstructionDays = 1;
        engine.Advance(2);
        Assert.True(engine.StockRASFacility(ras.Id, 100_000).Ok);
        var site = new SeaSite("sea-pen", "zone-sea") { MABTonnes = 780 };
        engine.State.Zones.Add(new Zone("zone-sea"));
        engine.State.SeaSites.Add(site);
        Assert.True(engine.StockSmolts(site.Id, 100_000).Ok);
        var ledger = engine.MortalityLedger;
        double rate = CalibrationRegistry.Default.Mortality.TemperatureStressDailyFractionPerDegreeC;

        Assert.Equal(FishHolding.LandBasedTank, engine.ResolveCohortEnvironment(ras.Tanks[0]).Holding);
        Assert.Equal(FishHolding.SeaPen, engine.ResolveCohortEnvironment(site.Pens[0]).Holding);

        ras.CurrentWaterTemp = 0.0; // chiller failure: 4 °C below the band
        engine.ApplySeaTemperatureStress(10.0);
        Assert.Equal((long)(100_000 * (4.0 * rate)), ledger.ForSite(ras.Id).Deaths(MortalityCause.Temperature).Fish);
        Assert.Equal(0, ledger.ForSite(site.Id).Total.Fish);

        ras.CurrentWaterTemp = 12.0;
        long tankDeaths = ledger.ForSite(ras.Id).Total.Fish;
        engine.ApplySeaTemperatureStress(0.0);
        Assert.Equal(tankDeaths, ledger.ForSite(ras.Id).Total.Fish);
        Assert.Equal((long)(100_000 * (4.0 * rate)), ledger.ForSite(site.Id).Deaths(MortalityCause.Temperature).Fish);
    }

    /// <summary>
    /// The seam for oxygen: a plankton crash collapses the fjord's oxygen and
    /// kills in the sea pens, but a land-based tank in the same zone keeps its
    /// controlled oxygen. Before S2-05 the crash killed a quarter of the tank.
    /// </summary>
    [Fact]
    public void PlanktonCrash_KillsSeaPens_ButNotALandTankInTheSameZone()
    {
        var engine = new SimulationEngine();
        engine.FinanceManager = new FinanceManager(5_000_000_000m);
        engine.Initialize(enableEvents: true, seed: 72);
        var zone = new Zone("zone-crash");
        engine.State.Zones.Add(zone);
        var site = new SeaSite("crash-pen", zone.Id) { MABTonnes = 780 };
        engine.State.SeaSites.Add(site);
        Assert.True(engine.StockSmolts(site.Id, 50_000).Ok);
        Assert.True(engine.BuildRASFacility("Landtank", RASScale.Small).Ok);
        var ras = engine.RASFacilities.Single();
        ras.ZoneId = zone.Id;
        ras.ConstructionDays = 1;
        engine.Advance(2);
        Assert.True(engine.StockRASFacility(ras.Id, 50_000).Ok);
        Assert.Equal(zone.Id, ras.Tanks[0].ZoneId);

        zone.WaterChemistry.OxygenO2 = 1.5;
        var crash = engine.Storyteller.GetEventDefinitions().Single(e => e.Id == "plankton-crash");
        int tankBefore = ras.Tanks.Sum(p => p.TotalCount);
        int penBefore = site.Pens.Sum(p => p.TotalCount);
        engine.Storyteller.Fire(crash, engine.State.Day);

        Assert.Equal(tankBefore, ras.Tanks.Sum(p => p.TotalCount));
        int penLost = penBefore - site.Pens.Sum(p => p.TotalCount);
        Assert.Equal((int)(penBefore * 0.25), penLost);
        Assert.Equal(penLost, engine.MortalityLedger.ForSite(site.Id).Deaths(MortalityCause.LowOxygen).Fish);
    }

    /// <summary>
    /// A transfer books its handling deaths (injury and treatment) and the
    /// fish that move (alive); a slice whose fish all die leaves the source
    /// pen instead of being reported lost while it stayed there.
    /// </summary>
    [Fact]
    public void Transfer_BooksHandlingDeathsAndTheMovedFish_AndAWipedSliceLeavesTheSource()
    {
        var engine = new SimulationEngine();
        engine.FinanceManager = new FinanceManager(5_000_000_000m);
        engine.Initialize(enableEvents: false, seed: 74);
        var zone = new Zone("zone-t");
        engine.State.Zones.Add(zone);
        var from = new SeaSite("from", zone.Id) { MABTonnes = 780 };
        var to = new SeaSite("to", zone.Id) { MABTonnes = 780 };
        engine.State.SeaSites.Add(from);
        engine.State.SeaSites.Add(to);
        Assert.True(engine.StockSmolts(from.Id, 100_000).Ok);
        var ledger = engine.MortalityLedger;
        double w = from.Pens[0].Cohorts[0].MeanWeightKg;

        var plan = CohortTransferSystem.PlanTransfer(from.Id, to.Id, 40_000, w,
            CohortTransferSystem.TransferReason.Grading, engine.State, hasVeterinarian: false);
        var result = CohortTransferSystem.ExecuteTransfer(plan, engine.State, _ => { }, new Random(1),
            engine.Growth, engine.Mortality);
        Assert.True(result.Success, result.Message);
        Assert.Equal(result.FishLost, ledger.ForSite(from.Id).Deaths(MortalityCause.Handling).Fish);
        Assert.Equal(result.FishTransferred, ledger.ForSite(from.Id).Removed(FishRemoval.TransferOut).Fish);
        Assert.Equal(40_000, result.FishLost + result.FishTransferred);
        Assert.Equal(60_000, from.Pens.Sum(p => p.TotalCount));

        var wiped = plan with { FishCount = 1_000, EstimatedMortalityPct = 100.0 };
        var lost = CohortTransferSystem.ExecuteTransfer(wiped, engine.State, _ => { }, new Random(1),
            engine.Growth, engine.Mortality);
        Assert.Equal(1_000, lost.FishLost);
        Assert.Equal(59_000, from.Pens.Sum(p => p.TotalCount));
        Assert.Equal(result.FishLost + 1_000, ledger.ForSite(from.Id).Deaths(MortalityCategory.InjuryAndTreatment).Fish);
    }

    /// <summary>A harvest leaves alive (not a death); an ISA cull order destroys the pen (an infectious death).</summary>
    [Fact]
    public void HarvestAndIsaCull_AreBookedAsALiveRemovalAndAnInfectiousDeath()
    {
        var engine = new SimulationEngine();
        engine.FinanceManager = new FinanceManager(5_000_000_000m);
        engine.Initialize(enableEvents: true, seed: 73);
        var zone = new Zone("zone-h");
        engine.State.Zones.Add(zone);
        var culled = new SeaSite("isa-pen", zone.Id) { MABTonnes = 780 };
        var harvested = new SeaSite("harvest-pen", zone.Id) { MABTonnes = 780 };
        engine.State.SeaSites.Add(culled);
        engine.State.SeaSites.Add(harvested);
        Assert.True(engine.StockSmolts(culled.Id, 20_000).Ok);
        Assert.True(engine.StockSmolts(harvested.Id, 30_000).Ok);
        var ledger = engine.MortalityLedger;

        var isa = engine.Storyteller.GetEventDefinitions().Single(e => e.Id == "isa-outbreak");
        engine.Storyteller.Fire(isa, engine.State.Day);
        Assert.Equal(0, culled.Pens.Sum(p => p.TotalCount));
        Assert.Equal(20_000, ledger.ForSite(culled.Id).Deaths(MortalityCause.DiseaseCull).Fish);
        Assert.Equal(20_000, ledger.ForSite(culled.Id).Deaths(MortalityCategory.Infectious).Fish);

        Assert.True(engine.HarvestSite(harvested.Id).Ok);
        Assert.Equal(30_000, ledger.ForSite(harvested.Id).Removed(FishRemoval.Harvest).Fish);
        Assert.Equal(0, ledger.ForSite(harvested.Id).Total.Fish);
    }
}
