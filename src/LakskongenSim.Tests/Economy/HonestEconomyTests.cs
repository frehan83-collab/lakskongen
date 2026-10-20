using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Calibration;
using LakskongenSim.Core;
using LakskongenSim.Finance;
using LakskongenSim.Tests.Integration;
using Xunit;

namespace LakskongenSim.Tests.Economy;

/// <summary>
/// S1-03, owner decision D16 (honest economy): achievements and tutorial steps
/// pay no cash; each scenario has its own bankruptcy floor in BalancingConfig
/// with a warning ladder before it; the resource-rent tax is on by default and
/// its 70M NOK deduction shields family farms.
/// </summary>
public class HonestEconomyTests
{
    private static SimulationEngine Started(string scenarioId, int seed, bool events = true)
    {
        var probe = new SimulationEngine();
        probe.Initialize(enableEvents: false, seed: seed);
        decimal capital = probe.Scenario.GetScenario(scenarioId)!.StartingCapitalNOK;

        var engine = new SimulationEngine();
        engine.FinanceManager = new FinanceManager(capital);
        engine.Initialize(enableEvents: events, seed: seed);
        engine.Scenario.Unlock(scenarioId);   // krise_manager is locked on the menu
        Assert.True(engine.StartScenario(scenarioId).Ok);
        return engine;
    }

    private static int Fish(LakskongenSim.Farm.SeaSite site) => site.Pens.Sum(p => p.TotalCount);

    private static double MeanWeightKg(LakskongenSim.Farm.SeaSite site)
    {
        int fish = Fish(site);
        return fish > 0 ? site.Pens.Sum(p => p.GetTotalBiomassKg()) / fish : 0.0;
    }

    // ── 1. Rewards are medals, not cash ─────────────────────────────────────

    private static readonly string[] RewardCategories = { "achievements", "tutorial_rewards" };

    [Fact]
    public void UnlockingAnAchievement_AwardsAMedal_AndMovesNoCash()
    {
        var engine = Started("po3_red_sone", 3, events: false);
        decimal cash = engine.FinanceManager.Balance;
        var unlocked = new List<Achievement>();
        engine.OnAchievementUnlocked += unlocked.Add;

        engine.Achievements.CheckAchievements();   // po3 starts stocked: "first-smolt"

        Assert.Contains(unlocked, a => a.Id == "first-smolt");
        Assert.Equal(AchievementMedal.Bronze, engine.Achievements.MedalOf("first-smolt"));
        Assert.Equal(cash, engine.FinanceManager.Balance);
        Assert.Equal(0m, engine.FinancialStatements.LifetimeRevenue);
    }

    /// <summary>
    /// U2-02 (stronger than "a day-0 step completes", RUN_LOG §7: no step
    /// completes by itself any more): the five day-0 steps are done by their
    /// own verbs and UI marks, and every completion moves no cash and books no
    /// revenue; the cash before and after each one is the same.
    /// </summary>
    [Fact]
    public void CompletingTutorialSteps_MovesNoCash()
    {
        var engine = Started("forste_sesong", 3, events: false);
        decimal cash = engine.FinanceManager.Balance;
        var cashAtCompletion = new List<decimal>();
        engine.Tutorial.OnStepCompleted += _ => cashAtCompletion.Add(engine.FinanceManager.Balance);

        engine.Tutorial.CheckProgress();
        Assert.Equal(0, engine.Tutorial.CompletedCount);   // nothing completes by itself
        new Tutorial.TutorialPlayer().DoToday(engine);

        Assert.Equal(5, engine.Tutorial.CompletedCount);
        Assert.All(cashAtCompletion, c => Assert.Equal(cash, c));
        Assert.Equal(cash, engine.FinanceManager.Balance);
        Assert.Equal(0m, engine.FinancialStatements.LifetimeRevenue);
    }

    [Fact]
    public void EveryAchievement_AwardsAMedal()
    {
        var engine = Started("po3_red_sone", 3, events: false);
        var all = engine.Achievements.GetLocked().Concat(engine.Achievements.GetUnlocked()).ToList();
        Assert.True(all.Count >= 30, $"only {all.Count} achievements loaded");
        Assert.Contains(all, a => a.Medal == AchievementMedal.Platinum);
        Assert.All(all, a => Assert.True(Enum.IsDefined(a.Medal), $"{a.Id}: no medal"));
    }

    /// <summary>
    /// The tutorial played to the end (18 steps, the forste_sesong win) and a
    /// two-year po3 career unlock achievements and complete steps; on no day
    /// does a reward line appear in the revenue statement, and the ledger
    /// still reconciles (D16 acceptance 1). U2-02 / D19 (RUN_LOG §7): the
    /// first season's steps are played by their verbs (every one of the 18
    /// completes, stronger than "6 or more"); po3_red_sone runs no tutorial
    /// (none completes, where it used to complete day-count steps).
    /// </summary>
    [Theory]
    [InlineData("forste_sesong", 7)]
    [InlineData("po3_red_sone", 13)]
    public void Career_BooksNoAchievementOrTutorialRevenue(string scenarioId, int seed)
    {
        var engine = Started(scenarioId, seed);
        var site = engine.State.SeaSites[0];
        var tutorialPlayer = new Tutorial.TutorialPlayer();
        bool harvested = false;
        for (int day = 0; day < 730; day++)
        {
            tutorialPlayer.DoToday(engine);
            engine.Advance(1);
            foreach (string category in RewardCategories)
                Assert.False(engine.FinancialStatements.RevenueBreakdown.ContainsKey(category),
                    $"{scenarioId} day {engine.State.Day}: '{category}' booked as revenue");
            foreach (var s in engine.State.SeaSites.ToList())
            {
                if (Fish(s) > 0 && MeanWeightKg(s) >= 4.5)
                    harvested |= engine.HarvestSite(s.Id).Ok;
                else if (Fish(s) == 0 && harvested && engine.FinanceManager.Balance > 1_000_000m)
                    engine.StockSmolts(s.Id, 20_000);
            }
        }

        Assert.True(harvested, "the career never sold fish");
        Assert.True(engine.State.UnlockedAchievementIds.Count >= 3,
            $"only {engine.State.UnlockedAchievementIds.Count} achievements unlocked");
        int expectedSteps = scenarioId == "forste_sesong" ? engine.Tutorial.TotalSteps : 0;
        Assert.Equal(expectedSteps, engine.State.CompletedTutorialStepIds.Count);
        if (scenarioId == "forste_sesong")
            Assert.True(engine.Scenario.IsWon, "the tutorial played to the end wins Første sesong");
        Assert.Equal(0m, engine.UnbookedCashTotal);
        decimal gap = engine.FinanceManager.Balance - engine.State.OpeningBalanceNok
                      - engine.FinancialStatements.LifetimeNetCashFlow;
        Assert.True(Math.Abs(gap) < 0.000001m, $"cash and booked flows differ by {gap} NOK");
    }

    // ── 2. Bankruptcy floor per scenario, warning ladder ────────────────────

    [Fact]
    public void BankruptcyFloor_IsReadFromCalibration_PerScenario()
    {
        var finance = CalibrationRegistry.Load().Finance;
        var engine = Started("forste_sesong", 1, events: false);

        Assert.Equal(finance.BankruptcyFloorNOK, engine.BankruptcyFloorNok);   // no entry: the default
        foreach (var (scenarioId, floor) in finance.BankruptcyFloorByScenarioNOK)
        {
            Assert.True(floor < finance.BankruptcyFloorNOK, $"{scenarioId}: {floor:N0} is not below the default");
            Assert.Equal(floor, engine.BankruptcyFloorFor(scenarioId));
        }
        Assert.True(finance.BankruptcyFloorByScenarioNOK.ContainsKey("krise_manager"));
        Assert.Equal(finance.BankruptcyFloorByScenarioNOK["krise_manager"],
            Started("krise_manager", 1, events: false).BankruptcyFloorNok);
    }

    /// <summary>D16 acceptance 2: krise_manager (500k NOK in debt on day 0) survives a passive first month.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    public void KriseManager_SurvivesThirtyDaysOfPassivePlay(int seed)
    {
        var engine = Started("krise_manager", seed);
        Assert.True(engine.FinanceManager.Balance < 0m, "krise_manager starts in debt");
        for (int day = 1; day <= 30; day++)
        {
            engine.Advance(1);
            Assert.False(engine.IsBankrupt, $"seed {seed}: bankrupt on day {day} at {engine.FinanceManager.Balance:N0} NOK");
            Assert.False(engine.Scenario.IsLost, $"seed {seed}: lost on day {day}: {engine.Scenario.LossReason}");
        }
        // The overdraft rung warned once, on the first day.
        Assert.Single(engine.State.EventLog, e => e.Id == SimulationEngine.SolvencyWarningKeys[0]);
    }

    /// <summary>
    /// The per-scenario floor exists so the shipped scenarios can be played to
    /// a sale: a player who only harvests the starting stock (at 4.5 kg)
    /// reaches the first harvest solvent. Under the old flat -1M floor these
    /// players went bankrupt around day 190 (po3) and day 100 (krise_manager),
    /// about 85 and 175 days before their first sale.
    /// </summary>
    [Theory]
    [InlineData("po3_red_sone", 4)]
    [InlineData("po3_red_sone", 9)]
    [InlineData("isa_utbrudd", 4)]
    [InlineData("algekatastrofe", 9)]
    [InlineData("krise_manager", 4)]
    [InlineData("krise_manager", 9)]
    public void HarvestOnlyPlayer_ReachesTheFirstHarvest_Solvent(string scenarioId, int seed)
    {
        var engine = Started(scenarioId, seed);
        int firstSale = -1;
        decimal trough = engine.FinanceManager.Balance;
        while (firstSale < 0 && engine.State.Day < 400)
        {
            engine.Advance(1);
            trough = Math.Min(trough, engine.FinanceManager.Balance);
            Assert.False(engine.IsBankrupt,
                $"{scenarioId} seed {seed}: bankrupt on day {engine.State.Day} at {engine.FinanceManager.Balance:N0} " +
                $"(floor {engine.BankruptcyFloorNok:N0})");
            foreach (var site in engine.State.SeaSites.ToList())
                if (Fish(site) > 0 && MeanWeightKg(site) >= 4.5 && engine.HarvestSite(site.Id).Ok && firstSale < 0)
                    firstSale = engine.State.Day;
        }
        Assert.True(firstSale > 0, $"{scenarioId} seed {seed}: no harvest by day 400");
        Assert.True(trough < -1_000_000m, $"{scenarioId} seed {seed}: trough {trough:N0} never needed the facility");
    }

    /// <summary>
    /// A steady decline: each rung warns exactly once and in order, then the
    /// floor declares bankruptcy once, logs it and loses the scenario.
    /// </summary>
    [Fact]
    public void Decline_WarnsOnEachRungOnce_ThenDeclaresBankruptcy()
    {
        var engine = Started("po3_red_sone", 2, events: false);
        decimal floor = engine.BankruptcyFloorNok;
        decimal step = -floor / 40m;
        int guard = 0;
        while (!engine.IsBankrupt && guard++ < 200)
        {
            engine.PayCost(step, CostBooking.Opex, "test_decline");
            engine.CheckSolvency();
            engine.CheckSolvency();   // a second check the same day never repeats a warning
        }

        var ladder = engine.State.EventLog
            .Where(e => e.Id.StartsWith("solvency_", StringComparison.Ordinal))
            .Select(e => e.Id).ToList();
        Assert.Equal(new[]
        {
            "solvency_overdraft", "solvency_bank_warning", "solvency_final_warning", SimulationEngine.BankruptcyKey
        }, ladder);
        Assert.True(engine.IsBankrupt);
        Assert.True(engine.FinanceManager.Balance < floor);
        Assert.True(engine.Scenario.IsLost);
        Assert.StartsWith("Konkurs", engine.Scenario.LossReason);
        Assert.True(engine.State.ScenarioIsLost);
        Assert.Contains(engine.DrainDayNotes(), n => n.Kind == "bankruptcy");

        // Cash below the bank warning rung but above the floor: rung 2 of 3.
        var other = Started("po3_red_sone", 2, events: false);
        other.PayCost(other.FinanceManager.Balance - other.SolvencyWarningThresholdNok(2) + 1m, CostBooking.Opex, "test");
        Assert.Equal(2, other.SolvencyWarningLevel);
    }

    [Fact]
    public void Ladder_ReArmsOnceCashIsBack_AndSurvivesASaveAndLoad()
    {
        var engine = Started("po3_red_sone", 6, events: false);
        engine.PayCost(engine.FinanceManager.Balance + 1_000m, CostBooking.Opex, "test");
        engine.CheckSolvency();
        Assert.Equal(1, engine.State.SolvencyWarningPeak);

        // A load must not repeat a warning the player already had.
        var resumed = SaveStateComparer.Resume(SaveStateComparer.Serialize(engine), events: false);
        Assert.Equal(1, resumed.State.SolvencyWarningPeak);
        int logged = resumed.State.EventLog.Count(e => e.Id == "solvency_overdraft");
        resumed.CheckSolvency();
        Assert.Equal(logged, resumed.State.EventLog.Count(e => e.Id == "solvency_overdraft"));

        // Back in the black, then overdrawn again: the first rung warns again.
        resumed.ReceiveIncome(10_000m, "test");
        resumed.CheckSolvency();
        Assert.Equal(0, resumed.State.SolvencyWarningPeak);
        resumed.PayCost(20_000m, CostBooking.Opex, "test");
        resumed.CheckSolvency();
        Assert.Equal(logged + 1, resumed.State.EventLog.Count(e => e.Id == "solvency_overdraft"));
    }

    // ── 3. Resource-rent tax: on by default, the deduction shields small farms ──

    /// <summary>D16 acceptance 3a: three years of the family farm, harvests sold, no rent tax.</summary>
    [Fact]
    public void FamilyFarm_PaysNoResourceRentTax()
    {
        var engine = Started("forste_sesong", 7);
        Assert.True(engine.EnableResourceRentTax);
        decimal harvestValueInAYear = 0m;
        while (engine.State.Day < 3 * 365)
        {
            harvestValueInAYear = Math.Max(harvestValueInAYear, engine.State.ResourceRentHarvestValueThisTaxYear);
            engine.Advance(1);
            var site = engine.State.SeaSites[0];
            if (Fish(site) > 0 && MeanWeightKg(site) >= 4.5)
                engine.HarvestSite(site.Id);
            else if (Fish(site) == 0 && engine.FinanceManager.Balance > 1_000_000m)
                engine.StockSmolts(site.Id, 20_000);
        }
        Assert.True(harvestValueInAYear > 0m, "the family farm never sold fish into a tax year");
        Assert.Equal(0m, engine.ResourceRentPaid);
    }

    /// <summary>
    /// D16 acceptance 3b: a six-site career in the sandbox scenario (which sets
    /// no tax rule, so the default applies) stocks each site to 90% of its MAB,
    /// borrows against its fish when cash runs low and pays resource-rent tax
    /// on the years its rent base clears the 70M NOK deduction.
    /// </summary>
    [Theory]
    [InlineData(7)]
    [InlineData(13)]
    [InlineData(21)]
    public void MultiSiteCareer_PaysResourceRentTax(int seed)
    {
        var engine = Started("barekraft_mester", seed);
        Assert.Null(engine.Scenario.ActiveScenario!.EnableResourceRentTax);   // no opt-in: the D16 default
        string zone = engine.State.SeaSites[0].ZoneId;
        for (int i = 1; i <= 5; i++)
            Assert.True(engine.BuildSeaSite(zone, $"anlegg_{i}").Ok);

        while (engine.State.Day < 3 * 365)
        {
            foreach (var site in engine.State.SeaSites.ToList())
            {
                if (Fish(site) > 0 && MeanWeightKg(site) >= 4.5)
                    engine.HarvestSite(site.Id);
                else if (Fish(site) == 0)
                    engine.StockSmolts(site.Id, (int)(0.9 * site.MABTonnes * 1000.0 / 5.0));
            }
            if (engine.FinanceManager.Balance < 5_000_000m
                && engine.LoanCapacityNok() - engine.TotalDebtNok >= 20_000_000m)
                Assert.True(engine.TakeLoan(20_000_000, 36, 0.06).Ok);
            engine.Advance(1);
            Assert.False(engine.IsBankrupt, $"seed {seed}: bankrupt on day {engine.State.Day}");
        }

        Assert.Equal(6, engine.State.SeaSites.Count);
        Assert.True(engine.ResourceRentPaid > 0m, $"seed {seed}: a six-site career paid no resource-rent tax");
    }
}
