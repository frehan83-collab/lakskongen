using System;
using System.Linq;
using System.Text.Json.Nodes;
using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using Xunit;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// S2-01a acceptance 2 and 5: the regulator reads every production area's
/// standing biomass against its licence MTB and every site against its
/// locality MTB on the day it happens; a breach opens a reduction order and
/// books the infringement fee and the confiscation of the gain on its kilos;
/// a missed deadline ends in a forced harvest; the whole episode survives a
/// save/load and replays exactly.
/// </summary>
public class CapacityEnforcementTests
{
    private const int Po3 = 3;

    private static SimulationEngine StartedPo3(int seed = 31)
    {
        var engine = new SimulationEngine { FinanceManager = new FinanceManager(12_000_000m) };
        engine.Initialize(enableEvents: false, seed: seed);
        Assert.True(engine.StartScenario("po3_red_sone").Ok);
        return engine;
    }

    /// <summary>Put grown fish straight into a site (biomass the plan never saw: growth, a transfer gone wrong).</summary>
    private static FishPopulation AddFish(SimulationEngine engine, SeaSite site, int count, double weightKg)
    {
        var pop = new FishPopulation(engine.Growth, engine.Mortality);
        pop.AddCohort(new FishPopulation.Cohort(500, count, weightKg, LifeStage.Seawater));
        site.Stock(pop);
        engine.State.Populations.Add(pop);
        return pop;
    }

    private static SeaSite Site(SimulationEngine engine, string id) => engine.State.SeaSites.Single(s => s.Id == id);

    /// <summary>po3 with a third, built site in po3_nord's zone: room to stand fish under its 3,120 t locality.</summary>
    private static (SimulationEngine Engine, SeaSite Built) Po3WithABuiltSite(int seed = 31)
    {
        var engine = StartedPo3(seed);
        Assert.True(engine.BuildSeaSite("po3_nord", "utvidelse").Ok);
        return (engine, Site(engine, "utvidelse"));
    }

    private static decimal Cost(SimulationEngine engine, string category)
        => engine.FinancialStatements.CostBreakdown.TryGetValue(category, out var v) ? v : 0m;

    /// <summary>
    /// FOR-2013-12-20-1675 § 6, written out: spot x 0.83 (live to gutted) - feed
    /// price x 1.30 (economic feed factor) - 4.93 NOK/kg (insurance + slaughter).
    /// </summary>
    private static decimal NetGainPerKg(SimulationEngine engine)
        => Math.Max(0m, engine.MarketSimulation.PricePerKg * 0.83m
            - engine.Calibration.Feed.FeedPricePerKgFeedNOK * 1.30m - 4.93m);

    /// <summary>The fee's measure: the illegal kilo at its sales value (spot x 0.83).</summary>
    private static decimal SalesValuePerKg(SimulationEngine engine)
        => engine.MarketSimulation.PricePerKg * 0.83m;

    // ── Acceptance 2: daily enforcement ───────────────────────────────────

    [Fact]
    public void BiomassOverTheLicences_OpensAnOrder_AndBooksFeeAndConfiscation_OnTheSameDay()
    {
        var (engine, built) = Po3WithABuiltSite();
        // 4 kg fish that put the area 15 t over its 1,560 t, whatever the
        // scenario's starting stock weighs (wave-2 integration: S2-03's grown
        // start stands 61 t on day 0, not the 11 t of 1 January smolts the
        // literal 391,000 fish = 1,564 t was written against).
        double overKg = 15_000.0;
        int fish = (int)Math.Ceiling((engine.AreaLicenceMtbTonnes(Po3) * 1000.0
            - engine.AreaStandingBiomassTonnes(Po3) * 1000.0 + overKg) / 4.0);
        AddFish(engine, built, fish, 4.0);

        engine.Advance(1);

        var breach = Assert.Single(engine.State.CapacityBreaches);
        Assert.True(breach.IsOpen);
        Assert.Equal(Po3, breach.Area);
        Assert.Equal(engine.State.Day, breach.StartDay);
        Assert.Equal(engine.State.Day + engine.Calibration.Capacity.OrderDeadlineDays, breach.DeadlineDay);
        Assert.True(breach.AreaCapExceeded);
        Assert.Empty(breach.SitesOverLocality);          // 1,564 t stands on a 3,120 t locality
        double illegal = engine.AreaStandingBiomassTonnes(Po3) * 1000.0 - engine.AreaLicenceMtbTonnes(Po3) * 1000.0;
        Assert.Equal(illegal, breach.PeakIllegalKg, 6);
        Assert.InRange(breach.PeakIllegalKg, 1_000.0, 40_000.0);

        decimal fee = Math.Round(Math.Min(engine.Calibration.Capacity.InfringementFeeCeilingNok,
            (decimal)breach.PeakIllegalKg * SalesValuePerKg(engine)), 2);
        decimal confiscation = Math.Round((decimal)breach.PeakIllegalKg * NetGainPerKg(engine), 2);
        Assert.True(confiscation > 0m);
        Assert.Equal(fee, breach.FineNok);
        Assert.Equal(confiscation, breach.ConfiscatedNok);
        Assert.Equal(fee, Cost(engine, "mtb_fee"));
        Assert.Equal(confiscation, Cost(engine, "mtb_confiscation"));
        Assert.Contains(engine.State.EventLog, e => e.Name == "MTB-overskridelse" && e.Day == engine.State.Day);
        Assert.Contains(engine.DrainDayNotes(), n => n.Kind == "capacity_breach" && !n.Success);
    }

    [Fact]
    public void AGrowingExcess_IsChargedOnlyOnItsNewKilos_AndTheFeeStopsAtFifteenG()
    {
        var (engine, built) = Po3WithABuiltSite();
        var big = AddFish(engine, built, 391_000, 4.0);
        engine.Advance(1);
        var breach = engine.State.CapacityBreaches.Single();
        double firstPeak = breach.PeakIllegalKg;
        decimal fee = breach.FineNok, confiscation = breach.ConfiscatedNok;

        AddFish(engine, built, 2_500, 4.0);               // +10 t
        engine.Advance(1);
        double added = breach.PeakIllegalKg - firstPeak;
        Assert.InRange(added, 10_000.0, 20_000.0);        // 10 t + a day of growth
        Assert.Equal(fee + Math.Round(Math.Min(engine.Calibration.Capacity.InfringementFeeCeilingNok - fee,
            (decimal)added * SalesValuePerKg(engine)), 2), breach.FineNok);
        Assert.Equal(confiscation + Math.Round((decimal)added * NetGainPerKg(engine), 2), breach.ConfiscatedNok);
        Assert.Equal(1, engine.State.EventLog.Count(e => e.Name == "MTB-overskridelse"));   // logged once

        // 200 t more: the fee reaches 15 G and stops there; the confiscation does not.
        AddFish(engine, built, 50_000, 4.0);
        engine.Advance(1);
        Assert.Equal(engine.Calibration.Capacity.InfringementFeeCeilingNok, breach.FineNok);
        Assert.Equal(2_048_235m, breach.FineNok);
        Assert.True(breach.ConfiscatedNok > confiscation + 180_000m * NetGainPerKg(engine),
            $"confiscated {breach.ConfiscatedNok:N0} NOK after 210 t more");

        // A day under the peak books nothing new: 80 t leave, the order runs on.
        decimal costs = Cost(engine, "mtb_fee") + Cost(engine, "mtb_confiscation");
        Assert.Equal(20_000, big.RemoveAlive(big.Cohorts[0], 20_000, FishRemoval.Harvest));
        engine.Advance(1);
        Assert.Equal(costs, Cost(engine, "mtb_fee") + Cost(engine, "mtb_confiscation"));
        Assert.True(breach.IsOpen);
    }

    [Fact]
    public void AMissedDeadline_ForcesTheHarvest_HeaviestFishFirst_DownTo95PercentOfTheCap()
    {
        var (engine, built) = Po3WithABuiltSite();
        // 4 kg fish that, with the 20 t of lighter fish below, stand the area 35 t
        // over its cap as 391,000 fish did on the 11 t start of 1 January smolts
        // (wave-2 integration: S2-03's grown start stands 61 t on day 0).
        int bigFish = (int)Math.Ceiling((engine.AreaLicenceMtbTonnes(Po3) * 1000.0
            - engine.AreaStandingBiomassTonnes(Po3) * 1000.0 - 20_000.0 + 35_000.0) / 4.0);
        var big = AddFish(engine, built, bigFish, 4.0);
        var small = AddFish(engine, built, 40_000, 0.5);   // lighter fish in the same locality
        int smoltsBefore = engine.State.SeaSites.Where(s => s.Id != built.Id).Sum(s => s.Pens.Sum(p => p.TotalCount));
        engine.Advance(1);
        int smallBefore = small.TotalCount;
        var breach = engine.State.CapacityBreaches.Single();

        while (breach.IsOpen && engine.State.Day < breach.DeadlineDay + 1) engine.Advance(1);

        Assert.Equal(CapacityBreachStatus.ForcedHarvest, breach.Status);
        Assert.Equal(breach.DeadlineDay, breach.ClosedDay);
        double cap = engine.AreaLicenceMtbTonnes(Po3);
        double keep = 1.0 - engine.Calibration.Capacity.ForcedHarvestHeadroomFraction;
        // Down to 95 % of the cap on the deadline (one 4 kg fish of rounding),
        // then a day of growth.
        Assert.InRange(engine.AreaStandingBiomassTonnes(Po3), cap * keep * 0.98, cap * keep * 1.01);
        Assert.InRange(breach.ForcedHarvestKg, 80_000.0, 200_000.0);
        // The 4 kg fish went; the scenario's smolts were left alone.
        int smoltsAfter = engine.State.SeaSites.Where(s => s.Id != built.Id).Sum(s => s.Pens.Sum(p => p.TotalCount));
        Assert.True(smoltsAfter >= smoltsBefore - smoltsBefore / 20, $"smolts {smoltsBefore} -> {smoltsAfter}");
        Assert.True(big.TotalCount < bigFish - 15_000);
        Assert.True(small.TotalCount >= smallBefore - smallBefore / 50, $"0.5 kg fish {smallBefore} -> {small.TotalCount}");
        // Sold at 70 % of spot, booked as forced-harvest revenue, logged.
        Assert.True(breach.ForcedHarvestRevenueNok > 0m);
        Assert.Equal(breach.ForcedHarvestRevenueNok, engine.FinancialStatements.RevenueBreakdown["forced_harvest"]);
        Assert.Contains(engine.State.EventLog, e => e.Name == "Tvangsslakting (MTB)");
        // Every fish taken is booked on the mortality ledger as a forced harvest.
        var forced = engine.MortalityLedger.Cumulative().RemovalRows().Single(r => r.Id == "forced_harvest");
        Assert.InRange(forced.Kg, breach.ForcedHarvestKg * 0.97, breach.ForcedHarvestKg * 1.03);
        // The order is closed; the fish left still outgrow the plan, so the
        // plan keeps new smolts out until the player sells.
        Assert.Null(engine.OpenCapacityBreach(Po3));
        Assert.Equal(CapacityReasons.AreaProjectedPeak, engine.PreviewStocking("po3_sor", 1).ReasonKey);
    }

    [Fact]
    public void ComplyingBeforeTheDeadline_ClosesTheOrder_WithoutAForcedHarvest_AndTheChargesStand()
    {
        var (engine, built) = Po3WithABuiltSite();
        AddFish(engine, built, 391_000, 4.0);
        engine.Advance(1);
        var breach = engine.State.CapacityBreaches.Single();
        decimal fee = breach.FineNok, confiscation = breach.ConfiscatedNok;
        decimal cash = engine.FinanceManager.Balance;
        var refused = engine.StockSmolts("po3_sor", 1_000);
        Assert.Equal(CapacityReasons.OrderOpen, refused.ErrorKey);
        Assert.Equal(cash, engine.FinanceManager.Balance);

        Assert.True(engine.HarvestSite(built.Id).Ok);
        engine.Advance(1);

        Assert.Equal(CapacityBreachStatus.Complied, breach.Status);
        Assert.Equal(engine.State.Day, breach.ClosedDay);
        Assert.Equal(0.0, breach.ForcedHarvestKg);
        Assert.Equal(fee, breach.FineNok);
        Assert.Equal(confiscation, breach.ConfiscatedNok);
        Assert.False(engine.FinancialStatements.RevenueBreakdown.ContainsKey("forced_harvest"));
        Assert.Contains(engine.State.EventLog, e => e.Name == "MTB-pålegg oppfylt");
        Assert.True(engine.StockSmolts("po3_sor", 1_000).Ok);
    }

    [Fact]
    public void ASiteOverItsLocality_IsABreach_UnderTheAreaCap_TooAndIsHarvestedBackUnderIt()
    {
        var engine = StartedPo3();
        var sor = Site(engine, "po3_sor");
        AddFish(engine, sor, 196_000, 4.0);               // 784 t + smolts on a 780 t locality; the area holds ~800 of 1,560 t

        engine.Advance(1);

        var breach = Assert.Single(engine.State.CapacityBreaches);
        Assert.False(breach.AreaCapExceeded);
        Assert.Equal(new[] { "po3_sor" }, breach.SitesOverLocality);
        Assert.Equal(sor.CurrentBiomassTonnes * 1000.0 - sor.MABTonnes * 1000.0, breach.PeakIllegalKg, 6);

        while (breach.IsOpen) engine.Advance(1);
        Assert.Equal(CapacityBreachStatus.ForcedHarvest, breach.Status);
        Assert.True(sor.CurrentBiomassTonnes <= sor.MABTonnes, $"{sor.CurrentBiomassTonnes:F1} t on a {sor.MABTonnes:F0} t locality");
    }

    [Fact]
    public void UnderEveryCap_NoOrder_AndAnEngineWithoutARegister_NeverEnforces()
    {
        var engine = StartedPo3();
        engine.Advance(60);
        Assert.Empty(engine.State.CapacityBreaches);
        Assert.Equal(0m, Cost(engine, "mtb_fee"));

        // A bare engine (no scenario, no register) keeps the per-site rule only.
        var bare = new SimulationEngine { FinanceManager = new FinanceManager(5_000_000m) };
        bare.Initialize(enableEvents: false, seed: 4);
        Assert.Equal(CapacityRegime.Unregulated, bare.State.CapacityRules);
        var zone = new LakskongenSim.Environment.Zone("hand");
        bare.State.Zones.Add(zone);
        var site = new SeaSite("hand-site", zone.Id) { MABTonnes = 1.0 };
        bare.State.SeaSites.Add(site);
        AddFish(bare, site, 5_000, 1.0);
        bare.Advance(20);
        Assert.Empty(bare.State.CapacityBreaches);
        Assert.Equal(0m, Cost(bare, "mtb_fee"));
    }

    // ── Acceptance 5: save/load and twin determinism with an order running ─

    [Fact]
    public void AnOpenOrder_SurvivesASave_AndTheResumedGameEndsInTheSameForcedHarvest()
    {
        var (engine, built) = Po3WithABuiltSite(seed: 47);
        AddFish(engine, built, 391_000, 4.0);
        engine.Advance(3);
        Assert.True(engine.OpenCapacityBreach(Po3) != null);

        var resumed = SaveStateComparer.Resume(SaveStateComparer.Serialize(engine), events: false);
        Assert.Equal(CapacityRegime.LicencesV1, resumed.State.CapacityRules);
        Assert.Equal(engine.State.LicenceHoldings.Select(l => l.Id), resumed.State.LicenceHoldings.Select(l => l.Id));
        Assert.NotNull(resumed.OpenCapacityBreach(Po3));

        engine.Advance(20);
        resumed.Advance(20);

        Assert.Equal(CapacityBreachStatus.ForcedHarvest, engine.State.CapacityBreaches.First().Status);
        string a = SaveStateComparer.Normalize(SaveStateComparer.Serialize(engine));
        string b = SaveStateComparer.Normalize(SaveStateComparer.Serialize(resumed));
        Assert.True(a == b, "resumed game diverged at " + SaveStateComparer.FirstDifference(a, b));
    }

    [Fact]
    public void TwoEnginesOnOneSeed_RunTheSameBreach_ToTheSameState()
    {
        string Run()
        {
            var (engine, built) = Po3WithABuiltSite(seed: 53);
            AddFish(engine, built, 395_000, 4.0);
            engine.Advance(30);
            return SaveStateComparer.Normalize(SaveStateComparer.Serialize(engine));
        }
        string first = Run(), second = Run();
        Assert.True(first == second, "twin runs diverged at " + SaveStateComparer.FirstDifference(first, second));
        Assert.Contains("\"Status\": \"forced_harvest\"", first);
    }

    [Fact]
    public void ASaveFromBeforeTheRegister_GetsOneLicencePerSite_InItsArea()
    {
        var engine = StartedPo3(seed: 9);
        engine.Advance(10);
        var json = JsonNode.Parse(SaveStateComparer.Serialize(engine))!.AsObject();
        foreach (var key in new[] { "CapacityRules", "LicenceHoldings", "LicencesGranted", "CapacityBreaches" })
            Assert.True(json.Remove(key), key);

        var resumed = SaveStateComparer.Resume(json.ToJsonString(SaveJson.Options), events: false);

        Assert.Equal(CapacityRegime.LicencesV1, resumed.State.CapacityRules);
        Assert.Equal(2, resumed.State.LicenceHoldings.Count);
        Assert.All(resumed.State.LicenceHoldings, l =>
        {
            Assert.Equal(Po3, l.PooledArea);
            Assert.Equal(LicenceOrigin.LegacySave, l.Origin);
            Assert.Equal(780.0, l.MtbTonnes);
        });
        Assert.Equal(new[] { "po3_nord", "po3_sor" }, resumed.State.LicenceHoldings.Select(l => l.RegisteredSiteId));
        Assert.Equal(engine.AreaLicenceMtbTonnes(Po3), resumed.AreaLicenceMtbTonnes(Po3));
        resumed.Advance(5);
        Assert.Empty(resumed.State.CapacityBreaches);
    }
}
