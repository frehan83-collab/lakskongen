using System;
using LakskongenSim.Finance;

namespace LakskongenSim.Core;

/// <summary>
/// The P&amp;L story (roadmap U2-03): the ledger hands every P&amp;L booking to
/// <see cref="PnlStory"/> under the statements' own category, so the end
/// screen's months, turning points and totals are the ledger's numbers. A pure
/// observer: no RNG, no cash, no decision reads it.
/// </summary>
public partial class SimulationEngine
{
    /// <summary>
    /// Raised for every P&amp;L booking (day, category, signed amount: income
    /// positive, costs negative, as the statements book them; and whether it
    /// is an income line). Tests reconcile the story against this raw stream;
    /// nothing in the game listens.
    /// </summary>
    public event Action<int, string, decimal, bool>? PnlBooked;

    /// <summary>Start a fresh story at today's cash (a new game or a scenario start).</summary>
    internal void OpenStory()
    {
        if (State == null) return;
        PnlStory.Open(State.PnlStory, State.Day, FinanceManager?.Balance ?? 0m);
    }

    private void RecordPnl(string category, decimal signedAmount, bool income)
    {
        if (State == null || signedAmount == 0m) return;
        if (string.IsNullOrEmpty(category)) category = "other";
        PnlStory.Record(State.PnlStory, State.Day, SimCalendar.MonthsSinceEpoch(State.Day), category, signedAmount, income);
        PnlBooked?.Invoke(State.Day, category, signedAmount, income);
    }

    /// <summary>End of a simulated day (post-tick): the month's closing cash and quiet runs closing.</summary>
    internal void CloseStoryDay()
    {
        if (State == null || FinanceManager == null) return;
        PnlStory.EndOfDay(State.PnlStory, State.Day, SimCalendar.MonthsSinceEpoch(State.Day), FinanceManager.Balance);
    }
}
