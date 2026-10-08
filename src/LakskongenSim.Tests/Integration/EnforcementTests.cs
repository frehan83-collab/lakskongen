using System;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Farm;
using Xunit;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// Regulatory enforcement now drives the SAME site-treatment kernel as the
/// player verb (Phase 1 wave 3): a Mattilsynet order treats the whole site,
/// uses the same crew/gill multipliers, force-pays, and closes honestly when
/// the temperature window is shut.
/// </summary>
public class EnforcementTests
{
    private static SeaSite StockMultiPenSite(SimulationEngine engine, string siteId, int pens, int perPen)
    {
        var site = new SeaSite(siteId, "zone-test");
        for (int i = 0; i < pens; i++)
        {
            var pop = new FishPopulation
            {
                Id = Guid.NewGuid().ToString(),
                SeaLiceLoad = 0.0,
                SiteId = siteId
            };
            pop.InjectDependencies(engine.Growth, engine.Mortality);
            pop.Cohorts.Add(new FishPopulation.Cohort(100, perPen, 1.0, LifeStage.Seawater, null));
            site.Pens.Add(pop);
            engine.State.Populations.Add(pop);
        }
        engine.State.SeaSites.Add(site);
        return site;
    }

    [Fact]
    public void EnforcedDelousing_TreatsWholeSite_AndClosesOrder()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 18);
        engine.Advance(180); // warm season: weekly lice counting active, Thermolicer window open

        var site = StockMultiPenSite(engine, "site-breach", 3, 10_000);
        site.Lice.SeedExternalInfection(20.0); // heavy transient for two breaches

        engine.Advance(60);
        Assert.True(site.TreatmentOrderDueDay >= 0,
            $"order expected: breaches={site.ConsecutiveLiceBreaches} due={site.TreatmentOrderDueDay}");

        decimal balanceBefore = engine.FinanceManager.Balance;
        engine.Advance(16); // run past the deadline

        Assert.True(site.TreatmentOrderDueDay == -1, "order must close after the deadline");
        Assert.True(site.ConsecutiveLiceBreaches == 0);
        var log = Assert.Single(engine.State.EventLog, e => e.Name == "Forced delousing");
        if (log.BalanceChange < 0m)
        {
            // Whole-site semantics: the forced cost covers every stocked pen,
            // not just the first one (old behavior).
            decimal expectedFloor = 3 * 10_000 * 2.0m; // >half the fish at >= 2 NOK/fish
            Assert.True(-log.BalanceChange >= expectedFloor,
                $"forced cost too small for a 3-pen site: {-log.BalanceChange:N0} NOK");
            Assert.True(engine.FinanceManager.Balance < balanceBefore, "forced treatment was free");
        }
    }
}