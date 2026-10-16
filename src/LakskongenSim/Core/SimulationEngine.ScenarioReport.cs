using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Finance;

namespace LakskongenSim.Core;

/// <summary>
/// The end screen's report (roadmap U2-03): the verdict, the key figures, the
/// P&amp;L story's months and three turning points, and the distinctions a
/// win's medal counts. A pure read of the saved state: the P&amp;L figures are
/// the ledger's (<see cref="PnlStory"/>), the fish the mortality ledger's.
/// </summary>
public partial class SimulationEngine
{
    /// <summary>Turning points the end screen shows.</summary>
    public const int EndScreenTurningPoints = 3;

    /// <summary>The verdict as it stands: bankrupt, won, lost or running.</summary>
    public ScenarioVerdict CurrentVerdict()
    {
        if (State?.BankruptDay.HasValue == true) return ScenarioVerdict.Bankrupt;
        if (Scenario?.IsWon == true) return ScenarioVerdict.Won;
        if (Scenario?.IsLost == true) return ScenarioVerdict.Lost;
        return ScenarioVerdict.Running;
    }

    /// <summary>The report of the game so far (the end screen reads it when a verdict lands).</summary>
    public ScenarioEndReport GetScenarioEndReport()
    {
        if (State == null)
            return new ScenarioEndReport();
        var story = State.PnlStory;
        var verdict = CurrentVerdict();
        int start = story.OpenedDay >= 0 ? story.OpenedDay : Scenario?.ScenarioStartDay ?? 0;
        int end = State.BankruptDay ?? State.Day;

        decimal revenue = story.Totals.Where(kv => kv.Key.StartsWith('+')).Sum(kv => kv.Value);
        decimal costs = -story.Totals.Where(kv => kv.Key.StartsWith('-')).Sum(kv => kv.Value);
        decimal fines = -ScenarioMedals.SanctionCategories.Sum(c => PnlStory.Total(story, c, income: false));

        var mortality = MortalityLedger.Cumulative();
        long died = mortality.Total.Fish;
        long leftAlive = MortalityCauses.AllRemovals
            .Where(r => r != FishRemoval.TransferOut)
            .Sum(r => mortality.Removed(r).Fish);
        long inSea = State.Populations.Sum(p => (long)p.TotalCount);
        long had = died + leftAlive + inSea;
        double mortalityShare = had > 0 ? (double)died / had : 0.0;

        var ladder = State.Liquidity;
        double welfareLimit = _balancing.SeaMortalityPerCycle;
        var distinctions = new List<ScenarioDistinction>
        {
            new(ScenarioMedals.Finance, ladder.BankCallsIssued == 0 && ladder.RescueLoansTaken == 0, ladder.BankCallsIssued, 0),
            new(ScenarioMedals.Welfare, had > 0 && mortalityShare <= welfareLimit, mortalityShare, welfareLimit),
            new(ScenarioMedals.Compliance, fines <= 0m, (double)fines, 0),
        };

        return new ScenarioEndReport
        {
            ScenarioId = Scenario?.ActiveScenario?.Id ?? "",
            ScenarioName = Scenario?.ActiveScenario?.Name ?? "",
            Verdict = verdict,
            LossReason = verdict == ScenarioVerdict.Lost || verdict == ScenarioVerdict.Bankrupt ? Scenario?.LossReason ?? "" : "",
            StartDay = start,
            EndDay = end,
            OpeningCashNok = story.OpenedDay >= 0 ? story.OpeningCashNok : State.OpeningBalanceNok,
            ClosingCashNok = FinanceManager?.Balance ?? 0m,
            LowestCashNok = story.LowestCashDay >= 0 ? story.LowestCashNok : FinanceManager?.Balance ?? 0m,
            LowestCashDay = story.LowestCashDay,
            BankruptcyFloorNok = BankruptcyFloorNok,
            LoanPrincipalNok = State.ActiveLoans.Sum(l => Math.Max(0m, l.Principal)),
            RevenueNok = revenue,
            CostsNok = costs,
            HarvestedTonnesHog = State.TotalHarvestedHogKg / 1000.0,
            HarvestBatches = State.HarvestBatchesSold,
            FishDied = died,
            FishLeftAlive = leftAlive,
            FishInSea = inSea,
            MortalityShare = mortalityShare,
            LiquidityWarnings = ladder.WarningsIssued,
            BankCalls = ladder.BankCallsIssued,
            RescueLoans = ladder.RescueLoansTaken,
            OverdraftInterestNok = ladder.OverdraftInterestPaidNok,
            FinesNok = fines,
            Months = StoryMonths(story),
            TurningPoints = PnlStory.TurningPoints(story, EndScreenTurningPoints)
                .Select(r => new ScenarioTurningPoint(r.Category, r.Income, r.FirstDay, r.LastDay, r.Amount, r.Bookings))
                .ToList(),
            Distinctions = distinctions,
            Medal = ScenarioMedals.Award(verdict, distinctions),
        };
    }

    private static IReadOnlyList<ScenarioMonthPoint> StoryMonths(PnlStoryState story)
    {
        var months = new List<ScenarioMonthPoint>(story.Months.Count);
        decimal running = 0m;
        foreach (var m in story.Months)
        {
            decimal result = m.Revenue - m.Costs;
            running += result;
            months.Add(new ScenarioMonthPoint(m.Month, m.FirstDay, m.Revenue, m.Costs, result, running, m.ClosingCash));
        }
        return months;
    }
}
