using System;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using Xunit;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// Review next-step 9 (engine correctness): the crash and dead-enforcement
/// paths, driven through the real tick (<c>engine.Advance</c>) rather than by
/// calling the private refresh/regulation helpers by hand.
/// </summary>
public class EngineCorrectnessTests
{
    private static SimulationEngine NewEngine(int seed, decimal cash = 100_000_000m)
    {
        var engine = new SimulationEngine();
        engine.FinanceManager = new FinanceManager(cash);
        engine.Initialize(enableEvents: false, seed: seed);
        return engine;
    }

    private static FishPopulation AddPen(SimulationEngine engine, SeaSite site, FishPopulation.Cohort cohort)
    {
        var pop = new FishPopulation { Id = $"{site.Id}-pen{site.Pens.Count}" };
        pop.InjectDependencies(engine.Growth, engine.Mortality);
        pop.Cohorts.Add(cohort);
        site.Stock(pop);
        engine.State.Populations.Add(pop);
        return pop;
    }

    private static Action<decimal> LedgerOpex(SimulationEngine engine)
        => cost => engine.PayCost(cost, CostBooking.Opex, "cohort_transfer");

    // ── F382: transfer into an empty site ──────────────────────────────────

    [Fact]
    public void TransferIntoEmptySite_CreatesAWiredPen_ThatGrowsOnTheNextTicks()
    {
        var engine = NewEngine(seed: 31);
        var zone = new Zone("zone-f382");
        engine.State.Zones.Add(zone);
        var source = new SeaSite("src-f382", zone.Id) { MABTonnes = 780 };
        var dest = new SeaSite("dst-f382", zone.Id) { MABTonnes = 780 };
        engine.State.SeaSites.Add(source);
        engine.State.SeaSites.Add(dest);
        AddPen(engine, source, new FishPopulation.Cohort(400, 20_000, 1.5, LifeStage.Seawater));

        var plan = CohortTransferSystem.PlanTransfer(source.Id, dest.Id, 10_000, 1.5,
            CohortTransferSystem.TransferReason.MabManagement, engine.State, hasVeterinarian: false);
        var result = CohortTransferSystem.ExecuteTransfer(plan, engine.State, LedgerOpex(engine),
            new Random(1), engine.Growth, engine.Mortality);

        Assert.True(result.Success, result.Message);
        var moved = Assert.Single(dest.Pens);
        Assert.Contains(moved, engine.State.Populations);

        // The old pen had no Growth/Mortality: this tick threw a NullReferenceException.
        engine.Advance(2);

        var cohort = Assert.Single(moved.Cohorts);
        Assert.True(cohort.MeanWeightKg > 1.5, $"moved fish did not grow: {cohort.MeanWeightKg:F4} kg");
        Assert.True(cohort.Count > 0);
    }

    // ── F386: smolt quality tier and genome survive rebuild/merge paths ────

    [Fact]
    public void Delousing_KeepsTheSmoltQualityTier_OfCohortsThatLostFish()
    {
        var engine = NewEngine(seed: 32);
        var pop = new FishPopulation { Id = "pop-f386" };
        pop.InjectDependencies(engine.Growth, engine.Mortality);
        pop.Cohorts.Add(new FishPopulation.Cohort(300, 10_000, 1.0, LifeStage.Seawater)
        {
            Quality = SmoltQualityTier.SuperSmolt
        });

        var result = new DelousingSystem().Treat(pop, DelousingMethod.Thermolicer,
            engine.SeaLice, engine.IntegratedDisease, temperatureC: 12.0, applyCompanyLiceKill: false);

        Assert.True(result.Success, result.Message);
        Assert.True(result.Deaths > 0, "the handling-mortality path (where the rebuild lived) must run");
        var cohort = Assert.Single(pop.Cohorts);
        Assert.Equal(SmoltQualityTier.SuperSmolt, cohort.Quality);
        Assert.True(cohort.Count < 10_000);
    }

    [Fact]
    public void Transfer_DoesNotMergePremiumFish_IntoAStandardCohort_AndKeepsTheirGenome()
    {
        var engine = NewEngine(seed: 33);
        var zone = new Zone("zone-f386");
        engine.State.Zones.Add(zone);
        var source = new SeaSite("src-f386", zone.Id) { MABTonnes = 780 };
        var dest = new SeaSite("dst-f386", zone.Id) { MABTonnes = 780 };
        engine.State.SeaSites.Add(source);
        engine.State.SeaSites.Add(dest);

        var genome = SmoltStrainRegistry.Get("standard").BuildGenome();
        AddPen(engine, source, new FishPopulation.Cohort(400, 8_000, 2.0, LifeStage.Seawater, genome)
        {
            Quality = SmoltQualityTier.SuperSmolt
        });
        // Same stage and weight: the old merge rule folded the premium fish in here.
        AddPen(engine, dest, new FishPopulation.Cohort(400, 5_000, 2.0, LifeStage.Seawater));

        var plan = CohortTransferSystem.PlanTransfer(source.Id, dest.Id, 8_000, 2.0,
            CohortTransferSystem.TransferReason.Grading, engine.State, hasVeterinarian: false);
        var result = CohortTransferSystem.ExecuteTransfer(plan, engine.State, LedgerOpex(engine),
            new Random(1), engine.Growth, engine.Mortality);

        Assert.True(result.Success, result.Message);
        var destPen = Assert.Single(dest.Pens);
        Assert.Equal(2, destPen.Cohorts.Count);
        var standard = Assert.Single(destPen.Cohorts, c => c.Quality == SmoltQualityTier.Standard);
        Assert.Equal(5_000, standard.Count);
        var premium = Assert.Single(destPen.Cohorts, c => c.Quality == SmoltQualityTier.SuperSmolt);
        Assert.Equal(result.FishTransferred, premium.Count);
        Assert.Same(genome, premium.Genetics);
    }

    // ── F1129: MAB reads live biomass on the tick path ─────────────────────

    [Fact]
    public void SiteBiomass_FollowsLiveGrowth_ThroughEngineAdvance()
    {
        var engine = NewEngine(seed: 34);
        var zone = new Zone("zone-f1129a");
        engine.State.Zones.Add(zone);
        var site = new SeaSite("site-f1129a", zone.Id) { MABTonnes = 780, PenVolume = 50_000f };
        engine.State.SeaSites.Add(site);

        var stocked = engine.StockSmolts(site.Id, 200_000);
        Assert.True(stocked.Ok, stocked.Message);
        double stockedTonnes = site.CurrentBiomassTonnes;
        Assert.Equal(20.0, stockedTonnes, 6);

        engine.Advance(150);

        double liveTonnes = site.Pens.Sum(p => p.GetTotalBiomassKg()) / 1000.0;
        Assert.True(liveTonnes > stockedTonnes * 1.5, $"fish should have grown: {liveTonnes:F1} t");
        // The old cache stayed at the stocking-time value for the whole cycle.
        Assert.Equal(liveTonnes, site.CurrentBiomassTonnes, 9);
        Assert.Equal(liveTonnes * 1000.0 / 50_000.0, site.CurrentStockingDensity, 4);
    }

    [Fact]
    public void FishOutgrowingMab_FlipIsOverMab_AndARedZoneIssuesTheOrder_OnTheTickPath()
    {
        var engine = NewEngine(seed: 35);
        var zone = new Zone("zone-f1129b");
        engine.State.Zones.Add(zone);
        var site = new SeaSite("site-f1129b", zone.Id) { MABTonnes = 25 };
        engine.State.SeaSites.Add(site);
        engine.TrafficLightSystem.SetZoneColor(zone.Id, ZoneColor.Green);

        var stocked = engine.StockSmolts(site.Id, 200_000); // 20 t of 100 g smolts
        Assert.True(stocked.Ok, stocked.Message);
        Assert.False(site.IsOverMAB);

        int days = 0;
        while (!site.IsOverMAB && days < 365) { engine.Advance(1); days++; }
        Assert.True(site.IsOverMAB, $"fish never outgrew MAB: {site.CurrentBiomassTonnes:F1} t after {days} days");

        // Heavy lice pressure: the weekly traffic-light assessment turns the zone
        // RED and the regulator issues the over-MAB order from live biomass.
        site.Lice.SeedExternalInfection(20.0);
        int guard = 0;
        while (!site.HasActiveMattilsynetOrder && guard++ < 28) engine.Advance(1);

        Assert.Equal(ZoneColor.Red, engine.TrafficLightSystem.GetZoneColor(zone.Id));
        Assert.True(site.HasActiveMattilsynetOrder, "the over-MAB order never fired on the tick path");
        Assert.Contains(engine.State.EventLog, e => e.Name == "Mattilsynet MAB-beskjed");
    }

    [Fact]
    public void StockSmolts_RefusesStockingThatWouldExceedMab_WithoutCharging()
    {
        var engine = NewEngine(seed: 36);
        var zone = new Zone("zone-f1129c");
        engine.State.Zones.Add(zone);
        var site = new SeaSite("site-f1129c", zone.Id) { MABTonnes = 30 };
        engine.State.SeaSites.Add(site);
        decimal cashBefore = engine.FinanceManager.Balance;

        var refused = engine.StockSmolts(site.Id, 400_000); // 40 t > 30 t MAB

        Assert.False(refused.Ok);
        Assert.Equal("mab_exceeded", refused.ErrorKey);
        Assert.Empty(site.Pens);
        Assert.Equal(cashBefore, engine.FinanceManager.Balance);

        Assert.True(engine.StockSmolts(site.Id, 200_000).Ok);    // 20 t fits
        var topUp = engine.StockSmolts(site.Id, 150_000);       // +15 t does not
        Assert.Equal("mab_exceeded", topUp.ErrorKey);
        Assert.Equal("invalid_count", engine.StockSmolts(site.Id, 0).ErrorKey);
        Assert.Equal("invalid_count", engine.StockSmolts(site.Id, -5).ErrorKey);
    }

    // ── F054: zone biomass is the sum of its pens, and clears after harvest ─

    [Fact]
    public void ZoneBiomass_IsTheSumOfAllPens_AndDropsToZeroAfterHarvest()
    {
        var engine = NewEngine(seed: 37);
        var zone = new Zone("zone-f054");
        engine.State.Zones.Add(zone);
        var site = new SeaSite("site-f054", zone.Id) { MABTonnes = 780 };
        engine.State.SeaSites.Add(site);
        AddPen(engine, site, new FishPopulation.Cohort(500, 2_500, 4.0, LifeStage.Seawater));  // 10 t
        AddPen(engine, site, new FishPopulation.Cohort(500, 5_000, 4.0, LifeStage.Seawater));  // 20 t
        AddPen(engine, site, new FishPopulation.Cohort(500, 7_500, 4.0, LifeStage.Seawater));  // 30 t
        double realKg = site.Pens.Sum(p => p.GetTotalBiomassKg());

        engine.Advance(1);

        // The zone used to hold only the last pen (30 t of 60 t).
        Assert.InRange(zone.CurrentFishBiomassKg, realKg * 0.97, realKg * 1.03);

        var harvest = engine.HarvestSite(site.Id);
        Assert.True(harvest.Ok, harvest.Message);
        engine.Advance(1);

        // ...and kept that phantom load after the pens were emptied.
        Assert.Equal(0.0, zone.CurrentFishBiomassKg);
    }

    // ── F1133: a lice order closes when the player complies ────────────────

    [Fact]
    public void CompliantDelousing_ClosesTheLiceOrder_AndNoForcedTreatmentFollows()
    {
        var engine = NewEngine(seed: 18);
        engine.Advance(180); // warm season: weekly lice counting, treatment windows open

        var site = new SeaSite("site-f1133", "zone-test");
        engine.State.SeaSites.Add(site);
        for (int i = 0; i < 3; i++)
            AddPen(engine, site, new FishPopulation.Cohort(100, 10_000, 1.0, LifeStage.Seawater));
        site.Lice.SeedExternalInfection(20.0);

        int guard = 0;
        while (site.TreatmentOrderDueDay < 0 && guard++ < 60) engine.Advance(1);
        Assert.True(site.TreatmentOrderDueDay >= 0, "the lice order was never issued");
        int dueDay = site.TreatmentOrderDueDay;

        // The player delouses right away (and again if the count is still high).
        var treated = engine.DelouseSite(site.Id, "thermolicer");
        Assert.True(treated.Ok, treated.Message);
        engine.Advance(7);
        if (site.TreatmentOrderDueDay >= 0)
        {
            Assert.True(engine.DelouseSite(site.Id, "thermolicer").Ok);
        }

        engine.Advance(dueDay - engine.State.Day + 14); // well past the deadline

        Assert.Equal(-1, site.TreatmentOrderDueDay);
        Assert.Contains(engine.State.EventLog, e => e.Name == "Mattilsynet order closed");
        Assert.DoesNotContain(engine.State.EventLog, e => e.Name == "Forced delousing");
    }
}
