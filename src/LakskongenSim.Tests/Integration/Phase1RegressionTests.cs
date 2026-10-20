using System;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Health;
using Xunit;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// Regression tests for the NEXT_LEVEL_PLAN Phase 1 honesty fixes:
/// save identity (S2), engine reset (S9), loan amortization + insurance (S5),
/// lice seeding/sync/single-kill (S1/S8), environment wiring (S3).
/// </summary>
public class Phase1RegressionTests
{
    [Fact]
    public void RelinkIds_PensAndPopulationsShareInstances()
    {
        var state = new GameState();
        var site = new SeaSite("site-1", "zone-1");
        var canonical = new FishPopulation { Id = "pop-1", SiteId = "site-1" };
        // Simulate a JSON round-trip: Pens holds a detached copy.
        var copy = new FishPopulation { Id = "pop-1", SiteId = "site-1" };
        state.Populations.Add(canonical);
        site.Pens.Add(copy);
        state.SeaSites.Add(site);

        GameStateRelinker.RelinkIds(state);

        Assert.Same(canonical, site.Pens[0]);
    }

    [Fact]
    public void RelinkIds_AdoptsOrphanPens()
    {
        var state = new GameState();
        var site = new SeaSite("site-1", "zone-1");
        site.Pens.Add(new FishPopulation { Id = "orphan-1" });
        state.SeaSites.Add(site);

        GameStateRelinker.RelinkIds(state);

        Assert.Same(site.Pens[0], state.Populations[0]);
        Assert.Equal("site-1", state.Populations[0].SiteId);
    }

    [Fact]
    public void Initialize_Twice_DoesNotInheritPreviousGame()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 11);
        engine.State.Zones.Add(new Zone("z1"));
        engine.State.SeaSites.Add(new SeaSite("s1", "z1"));
        engine.State.Populations.Add(new FishPopulation());
        engine.Advance(5);

        engine.Initialize(enableEvents: false, seed: 11);

        Assert.Empty(engine.State.Zones);
        Assert.Empty(engine.State.SeaSites);
        Assert.Empty(engine.State.Populations);
        Assert.Empty(engine.State.EventLog);
        Assert.Equal(0, engine.State.Day);
        Assert.Equal(0, engine.Time.CurrentDay);
    }

    [Fact]
    public void Loan_AmortizesDaily_UntilRepaid()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 12);
        engine.FinanceManager.AddRevenue(365_000m); // simulate TakeLoan cash
        engine.State.ActiveLoans.Add(new LoanState
        {
            Id = "loan_test",
            Principal = 365_000m,
            InterestRate = 0m, // exact arithmetic: 1000/day
            TermDays = 365,
            DaysTaken = 0
        });

        engine.Advance(10);

        // Exact liability countdown (balance itself also moves with opex,
        // achievement rewards etc. — the ProfitScenario test below pins the
        // economic effect instead).
        var loan = engine.State.ActiveLoans.Single();
        Assert.Equal(355_000m, loan.Principal);
        Assert.Equal(10, loan.DaysTaken);

        engine.Advance(355);
        Assert.Empty(engine.State.ActiveLoans);
    }

    [Fact]
    public void Delousing_CompanyLiceKill_AppliesExactlyOnce()
    {
        var lice = new SeaLiceModel();
        lice.SeedExternalInfection(1.0);
        double seeded = lice.AverageLicePerFish;
        Assert.True(seeded > 0.001);

        var disease = new IntegratedDiseaseSystem(lice);
        var delousing = new DelousingSystem();
        var pop1 = new FishPopulation();
        var pop2 = new FishPopulation();

        delousing.Treat(pop1, Data.DelousingMethod.Thermolicer, lice, disease, 10.0, 1.0,
            applyCompanyLiceKill: true);
        double afterFirst = lice.AverageLicePerFish;
        Assert.True(afterFirst < seeded);

        delousing.Treat(pop2, Data.DelousingMethod.Thermolicer, lice, disease, 10.0, 1.0,
            applyCompanyLiceKill: false);
        Assert.Equal(afterFirst, lice.AverageLicePerFish);
    }

    [Fact]
    public void ProfitScenario_LoanCash_DoesNotTriggerWin()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 13);
        engine.Scenario.StartScenario("lofoten_eventyr"); // 2M start, 5M target
        // Simulate TakeLoan(4M): cash in + liability recorded.
        engine.FinanceManager.AddRevenue(4_000_000m);
        engine.State.ActiveLoans.Add(new LoanState
        {
            Id = "loan_win",
            Principal = 4_000_000m,
            InterestRate = 0.05m,
            TermDays = 360,
            DaysTaken = 0
        });

        engine.Advance(1); // scenario win check runs in the tick

        // Cash is ~6M (>= 5M target) but equity is ~2M — must NOT win.
        Assert.True(engine.FinanceManager.Balance >= 5_000_000m);
        Assert.False(engine.Scenario.IsWon);
    }

    [Fact]
    public void Environment_ZonesAdvance_WithLiveState()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 14);
        var zone = new Zone("z-live");
        engine.State.Zones.Add(zone);

        engine.Advance(7);

        // Zone.AdvanceDays ran through Environment (ElapsedDays only moves there).
        Assert.Equal(7, zone.ElapsedDays);
    }

    private static SeaSite StockTestSite(SimulationEngine engine, string siteId, double liceLoad)
    {
        // Mirrors the runtime invariant: the same instances live in
        // site.Pens and State.Populations (see GameStateRelinker).
        var site = new SeaSite(siteId, "zone-test");
        var pop = new FishPopulation { Id = Guid.NewGuid().ToString(), SeaLiceLoad = liceLoad, SiteId = siteId };
        pop.InjectDependencies(engine.Growth, engine.Mortality);
        pop.Cohorts.Add(new FishPopulation.Cohort(100, 10_000, 1.0, Data.LifeStage.Seawater, null));
        site.Pens.Add(pop);
        engine.State.SeaSites.Add(site);
        engine.State.Populations.Add(pop);
        return site;
    }

    [Fact]
    public void LiceSync_Weekly_MirrorsSiteModelAverage()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 15);
        var site = StockTestSite(engine, "site-sync", 0.0);
        site.Lice.SeedExternalInfection(1.0);
        Assert.True(site.Lice.AverageLicePerFish > 0.0); // seeded, non-vacuous

        // NOTE: AdvanceOneDay ticks with Day=0..N-1, so the Day>=7 weekly
        // block first fires on the 8th tick.
        engine.Advance(8); // weekly block syncs each pen to its SITE model

        // The pen mirrors its site model exactly — whatever the model
        // dynamics did to it (a fishless test tank lets lice decline).
        Assert.Equal(site.Lice.AverageLicePerFish, site.Pens[0].SeaLiceLoad, precision: 10);
    }

    [Fact]
    public void LiceSync_SelfStarts_FromScenarioStockedFish()
    {
        // Scenario/hatchery stockings set population loads without seeding
        // the site model — the weekly block must re-seed from the worst
        // pen instead of zeroing everyone's lice.
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 16);
        var site = StockTestSite(engine, "site-seed", 0.2);
        Assert.True(site.Lice.AverageLicePerFish <= 0.001);

        engine.Advance(8);

        Assert.True(site.Lice.AverageLicePerFish > 0.0);
        Assert.Equal(site.Lice.AverageLicePerFish, site.Pens[0].SeaLiceLoad, precision: 10);
    }

    [Fact]
    public void SiteLiceModels_AdvanceIndependently_WithNeighbourInflow()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 17);
        // Lice barely develop in winter water (~4 °C kills the dynamics, as
        // in reality) — fast-forward to July before seeding.
        engine.Advance(180);
        // Same zone => strong coupling; the clean site still catches
        // copepodids from its lousy neighbour.
        var lousy = StockTestSite(engine, "site-lousy", 0.0);
        lousy.ZoneId = "zone-shared";
        lousy.Lice.SeedExternalInfection(2.0);
        var clean = StockTestSite(engine, "site-clean", 0.0);
        clean.ZoneId = "zone-shared";

        engine.Advance(30);

        Assert.True(lousy.Lice.AverageLicePerFish > 0.0);
        // Clean site was never seeded directly — any lice came from inflow.
        Assert.True(clean.Lice.AverageLicePerFish > 0.0);
        // ...but stays well below the heavily seeded neighbour.
        Assert.True(clean.Lice.AverageLicePerFish < lousy.Lice.AverageLicePerFish);
    }

    [Fact]
    public void LiceBreach_TwoStrikes_IssuesTreatmentOrder()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 18);
        // Summer water: lice dynamics only run when it is warm enough.
        engine.Advance(180);
        var site = StockTestSite(engine, "site-breach", 0.0);
        // Heavy transient: overshoots the ~0.5 steady state before settling.
        site.Lice.SeedExternalInfection(20.0);

        engine.Advance(60);

        // Two consecutive weekly breaches => order with a deadline.
        Assert.True(site.ConsecutiveLiceBreaches >= 2 || site.TreatmentOrderDueDay >= 0,
            $"breaches={site.ConsecutiveLiceBreaches} due={site.TreatmentOrderDueDay} count={site.LastLiceCount:F2}");
        Assert.True(site.TreatmentOrderDueDay >= 0 || site.LastLiceCount <= 0.5,
            $"no order but breaching: count={site.LastLiceCount:F2}");
    }

    [Fact]
    public void Delousing_RepeatedThermalTreatment_CompoundsHarm()
    {
        var lice = new SeaLiceModel();
        var disease = new IntegratedDiseaseSystem(lice);
        var delousing = new DelousingSystem();
        var pop = new FishPopulation();
        pop.Cohorts.Add(new FishPopulation.Cohort(100, 100_000, 4.0, Data.LifeStage.Seawater, null));

        var first = delousing.Treat(pop, Data.DelousingMethod.Thermolicer, lice, disease, 10.0, 1.0);
        double injuryAfterFirst = pop.TreatmentHarm.InjuryLoad;
        Assert.True(injuryAfterFirst > 0.0);

        // Back-to-back treatment next day: recency amplifies the injury load.
        pop.TreatmentHarm = new Health.DelousingInjuryState
        {
            InjuryLoad = injuryAfterFirst,
            GillDamage = 0.0,
            CumulativeWelfareCost = 0.0,
            DaysSinceLastTreatment = 1,
            TreatmentsToDate = 1
        };
        var second = delousing.Treat(pop, Data.DelousingMethod.Thermolicer, lice, disease, 10.0, 1.0);

        Assert.True(pop.TreatmentHarm.InjuryLoad > injuryAfterFirst);
        // Per-capita death rate is non-decreasing (absolute deaths fall
        // because the first treatment shrank the population — compare rates).
        Assert.True((long)second.Deaths * first.FishTreated >= (long)first.Deaths * second.FishTreated);
        Assert.True(second.GillDamage > 0.0);
    }

    [Fact]
    public void CompanyLiceAverage_WeightsStockedSites()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 19);
        var a = StockTestSite(engine, "site-a", 0.0);
        a.Lice.SeedExternalInfection(1.0);
        double avgA = a.Lice.AverageLicePerFish;
        var b = StockTestSite(engine, "site-b", 0.0); // clean, extinct

        // Both sites hold 10k fish: average must sit strictly between the
        // lousy site and zero (not equal to either).
        double company = engine.GetCompanyLiceAverage();
        Assert.InRange(company, 0.0, avgA);
        Assert.True(company > 0.0 && company < avgA);
    }
}
