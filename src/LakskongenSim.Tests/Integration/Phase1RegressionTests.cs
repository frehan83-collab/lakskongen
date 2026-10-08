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

    [Fact]
    public void LiceSync_Weekly_MirrorsModelAverage()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 15);
        var pop = new FishPopulation { SeaLiceLoad = 0.0 };
        engine.State.Populations.Add(pop);
        engine.SeaLice.SeedExternalInfection(1.0);
        Assert.True(engine.SeaLice.AverageLicePerFish > 0.0); // seeded, non-vacuous

        // NOTE: AdvanceOneDay ticks with Day=0..N-1, so the Day>=7 weekly
        // block first fires on the 8th tick.
        engine.Advance(8); // weekly traffic-light block syncs SeaLiceLoad

        // The population mirrors the model average exactly — whatever the
        // model dynamics did to it (a fishless test tank lets lice decline).
        Assert.Equal(engine.SeaLice.AverageLicePerFish, pop.SeaLiceLoad, precision: 10);
    }
}
