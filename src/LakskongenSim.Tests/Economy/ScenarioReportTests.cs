using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Finance;
using LakskongenSim.Tests.Integration;
using Xunit;

namespace LakskongenSim.Tests.Economy;

/// <summary>
/// Roadmap U2-03: the end screen's report. Its P&amp;L story is the ledger's
/// own: the turning points reconcile to the bookings (acceptance 5), the
/// months add up to the result, the verdicts follow the engine's rules and a
/// win's medal counts its distinctions.
/// </summary>
public class ScenarioReportTests
{
    private static SimulationEngine Started(string scenarioId, int seed, bool events = true)
    {
        var probe = new SimulationEngine();
        probe.Initialize(enableEvents: false, seed: seed);
        decimal capital = probe.Scenario.GetScenario(scenarioId)!.StartingCapitalNOK;

        var engine = new SimulationEngine();
        engine.FinanceManager = new FinanceManager(capital);
        engine.Initialize(enableEvents: events, seed: seed);
        engine.Scenario.Unlock(scenarioId);
        Assert.True(engine.StartScenario(scenarioId).Ok);
        return engine;
    }

    private sealed record Booking(int Day, string Category, decimal Amount, bool Income);

    /// <summary>The minimal harvest player: sells a site's fish at 4.5 kg.</summary>
    private static void HarvestAtFourAndAHalf(SimulationEngine engine)
    {
        foreach (var site in engine.State.SeaSites.ToList())
        {
            int fish = site.Pens.Sum(p => p.TotalCount);
            if (fish > 0 && site.Pens.Sum(p => p.GetTotalBiomassKg()) / fish >= 4.5)
                engine.HarvestSite(site.Id);
        }
    }

    // ── Acceptance 5: turning points reconcile to the ledger ────────────────

    [Theory]
    [InlineData("forste_sesong", 2, 420)]
    [InlineData("po3_red_sone", 3, 500)]
    [InlineData("algekatastrofe", 1, 366)]
    public void TurningPoints_ReconcileToTheLedger(string scenarioId, int seed, int days)
    {
        var engine = Started(scenarioId, seed);
        var ledger = new List<Booking>();
        engine.PnlBooked += (day, category, amount, income) => ledger.Add(new Booking(day, category, amount, income));
        decimal lifetimeBefore = Lifetime(engine);

        for (int d = 0; d < days && !engine.IsBankrupt; d++)
        {
            engine.Advance(1);
            HarvestAtFourAndAHalf(engine);
        }
        var report = engine.GetScenarioEndReport();

        // The raw stream is the statements' P&L (summed in another order, so
        // to a millionth of a krone: decimal keeps 28 digits of each sum).
        AssertNok(Lifetime(engine) - lifetimeBefore, ledger.Sum(b => b.Amount));
        // The report's result is the stream's and the months add up to it.
        AssertNok(ledger.Sum(b => b.Amount), report.NetResultNok);
        AssertNok(ledger.Where(b => b.Income).Sum(b => b.Amount), report.RevenueNok);
        AssertNok(report.NetResultNok, report.Months.Sum(m => m.ResultNok));
        AssertNok(report.NetResultNok, report.Months[^1].CumulativeResultNok);

        Assert.Equal(SimulationEngine.EndScreenTurningPoints, report.TurningPoints.Count);
        Assert.Contains(report.TurningPoints, t => t.Category == "harvest" && t.Income);
        foreach (var tp in report.TurningPoints)
        {
            var run = ledger.Where(b => b.Category == tp.Category && b.Income == tp.Income).ToList();
            var inside = run.Where(b => b.Day >= tp.FirstDay && b.Day <= tp.LastDay).ToList();
            Assert.Equal(inside.Sum(b => b.Amount), tp.AmountNok);
            Assert.Equal(inside.Count, tp.Bookings);
            // A run is whole: nothing of its line within the merge gap either side.
            Assert.DoesNotContain(run, b => b.Day < tp.FirstDay && b.Day >= tp.FirstDay - PnlStory.MergeGapDays);
            Assert.DoesNotContain(run, b => b.Day > tp.LastDay && b.Day <= tp.LastDay + PnlStory.MergeGapDays);
            Assert.True(tp.LastDay - tp.FirstDay + 1 <= PnlStory.MaxEpisodeDays, $"{tp.Category} ran {tp.FirstDay}-{tp.LastDay}");
        }
        // The three are the largest one-off runs: no closed run outside them is larger.
        decimal smallest = report.TurningPoints.Min(t => Math.Abs(t.AmountNok));
        Assert.All(engine.State.PnlStory.Closed.Where(r => !report.TurningPoints.Any(t => t.Category == r.Category
                && t.Income == r.Income && t.FirstDay == r.FirstDay)),
            r => Assert.True(Math.Abs(r.Amount) <= smallest, $"{r.Category} {r.Amount:N0} outranks a shown point"));
        // In the order they happened.
        Assert.Equal(report.TurningPoints.OrderBy(t => t.FirstDay).Select(t => t.FirstDay), report.TurningPoints.Select(t => t.FirstDay));
    }

    private static void AssertNok(decimal expected, decimal actual)
        => Assert.True(Math.Abs(expected - actual) < 0.000001m, $"expected {expected} NOK, got {actual} NOK");

    private static decimal Lifetime(SimulationEngine e)
    {
        var fs = e.FinancialStatements;
        return fs.LifetimeRevenue - fs.LifetimeCOGS - fs.LifetimeOperatingExpenses - fs.LifetimeInterest - fs.LifetimeTaxes;
    }

    [Fact]
    public void RunningCosts_AreNeverTurningPoints()
    {
        var engine = Started("forste_sesong", 2);
        engine.Advance(200);
        var story = engine.State.PnlStory;
        Assert.Contains(story.Open, r => r.Category == "feed" && r.Running);
        var report = engine.GetScenarioEndReport();
        Assert.DoesNotContain(report.TurningPoints, t => t.Category is "feed" or "interest");
        Assert.All(report.TurningPoints, t => Assert.True(t.LastDay - t.FirstDay + 1 <= PnlStory.MaxEpisodeDays));
    }

    [Fact]
    public void TheStory_SurvivesASaveAndLoad()
    {
        var engine = Started("po3_red_sone", 5);
        for (int d = 0; d < 300; d++) { engine.Advance(1); HarvestAtFourAndAHalf(engine); }
        var resumed = SaveStateComparer.Resume(SaveStateComparer.Serialize(engine), events: true);
        for (int d = 0; d < 60; d++)
        {
            engine.Advance(1); HarvestAtFourAndAHalf(engine);
            resumed.Advance(1); HarvestAtFourAndAHalf(resumed);
        }
        var a = engine.GetScenarioEndReport();
        var b = resumed.GetScenarioEndReport();
        Assert.Equal(a.NetResultNok, b.NetResultNok);
        Assert.Equal(a.Months, b.Months);
        Assert.Equal(a.TurningPoints, b.TurningPoints);
        Assert.Equal(a.LowestCashNok, b.LowestCashNok);
    }

    // ── Verdicts, figures and medals ─────────────────────────────────────────

    [Fact]
    public void ABankruptCompany_GetsTheBankruptcyVerdict_AndNoMedal()
    {
        var engine = Started("po3_red_sone", 1);
        while (!engine.IsBankrupt && engine.State.Day < 900) engine.Advance(1);
        var report = engine.GetScenarioEndReport();
        Assert.Equal(ScenarioVerdict.Bankrupt, report.Verdict);
        Assert.Equal(engine.State.BankruptDay, report.EndDay);
        Assert.Equal(ScenarioMedal.None, report.Medal);
        Assert.Equal("po3_red_sone", report.ScenarioId);
        Assert.True(report.ClosingCashNok < report.BankruptcyFloorNok);
        Assert.Equal(report.ClosingCashNok, report.LowestCashNok);
        Assert.True(report.LiquidityWarnings >= 1 && report.BankCalls >= 1);
        Assert.True(report.OverdraftInterestNok > 0m);
        Assert.StartsWith("Konkurs", report.LossReason);
        Assert.False(report.Distinctions.Single(d => d.Id == ScenarioMedals.Finance).Met);
    }

    [Fact]
    public void ASurvivedAlgaeSeason_IsWon_WithAMedal()
    {
        var engine = Started("algekatastrofe", 2);
        while (!engine.Scenario.IsWon && !engine.Scenario.IsLost && !engine.IsBankrupt && engine.State.Day < 400)
        {
            engine.Advance(1);
            HarvestAtFourAndAHalf(engine);
        }
        var report = engine.GetScenarioEndReport();
        Assert.Equal(ScenarioVerdict.Won, report.Verdict);
        Assert.NotEqual(ScenarioMedal.None, report.Medal);
        Assert.Equal(ScenarioMedals.Award(ScenarioVerdict.Won, report.Distinctions.ToList()), report.Medal);
        Assert.Equal(new[] { ScenarioMedals.Finance, ScenarioMedals.Welfare, ScenarioMedals.Compliance },
            report.Distinctions.Select(d => d.Id));
        Assert.True(report.HarvestedTonnesHog > 0 && report.HarvestBatches > 0);
        Assert.True(report.FishDied > 0 && report.FishLeftAlive > 0);
        Assert.InRange(report.MortalityShare, 0.0, 1.0);
        Assert.Equal((double)report.FishDied / (report.FishDied + report.FishLeftAlive + report.FishInSea), report.MortalityShare, 12);
        Assert.Equal("", report.LossReason);
    }

    [Fact]
    public void AFailedSurvival_IsLost_WithTheScenariosReason()
    {
        var engine = Started("krise_manager", 2);
        while (!engine.Scenario.IsWon && !engine.Scenario.IsLost && !engine.IsBankrupt && engine.State.Day < 800)
        {
            engine.Advance(1);
            HarvestAtFourAndAHalf(engine);
        }
        var report = engine.GetScenarioEndReport();
        Assert.Equal(ScenarioVerdict.Lost, report.Verdict);
        Assert.False(engine.IsBankrupt);
        Assert.Equal(engine.Scenario.LossReason, report.LossReason);
        Assert.Contains("730", report.LossReason);
        Assert.Equal(ScenarioMedal.None, report.Medal);
    }

    [Fact]
    public void TheMedal_CountsTheDistinctionsOfAWin()
    {
        ScenarioDistinction D(string id, bool met) => new(id, met, 0, 0);
        var none = new[] { D("finance", false), D("welfare", false), D("compliance", false) };
        var two = new[] { D("finance", true), D("welfare", false), D("compliance", true) };
        var all = new[] { D("finance", true), D("welfare", true), D("compliance", true) };
        Assert.Equal(ScenarioMedal.Bronze, ScenarioMedals.Award(ScenarioVerdict.Won, none));
        Assert.Equal(ScenarioMedal.Bronze, ScenarioMedals.Award(ScenarioVerdict.Won, new[] { D("finance", true) }));
        Assert.Equal(ScenarioMedal.Silver, ScenarioMedals.Award(ScenarioVerdict.Won, two));
        Assert.Equal(ScenarioMedal.Gold, ScenarioMedals.Award(ScenarioVerdict.Won, all));
        foreach (var verdict in new[] { ScenarioVerdict.Lost, ScenarioVerdict.Bankrupt, ScenarioVerdict.Running })
            Assert.Equal(ScenarioMedal.None, ScenarioMedals.Award(verdict, all));
        Assert.Equal("gold", ScenarioMedals.Key(ScenarioMedal.Gold));
    }

    [Fact]
    public void ARegulatorsFine_CostsTheComplianceDistinction()
    {
        var engine = Started("forste_sesong", 1, events: false);
        Assert.True(engine.GetScenarioEndReport().Distinctions.Single(d => d.Id == ScenarioMedals.Compliance).Met);
        engine.PayCost(250_000m, CostBooking.Opex, "fines");
        var report = engine.GetScenarioEndReport();
        var compliance = report.Distinctions.Single(d => d.Id == ScenarioMedals.Compliance);
        Assert.False(compliance.Met);
        Assert.Equal(250_000m, report.FinesNok);
    }
}
