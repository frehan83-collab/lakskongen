using System;
using LakskongenSim.Finance;

namespace LakskongenSim.Core;

/// <summary>Where a cash outflow is booked in the statements.</summary>
public enum CostBooking
{
    /// <summary>Cost of goods sold (feed, smolts).</summary>
    Cogs,
    /// <summary>Operating expense (wages, treatments, fees, fines, repairs, premiums).</summary>
    Opex,
    /// <summary>Interest on debt.</summary>
    Interest,
    /// <summary>Corporate or resource-rent tax.</summary>
    Tax,
    /// <summary>Capital expenditure: becomes a balance-sheet asset, not a P&amp;L cost.</summary>
    Capex,
    /// <summary>Repayment of loan principal: reduces a liability, not a P&amp;L cost.</summary>
    LoanPrincipal
}

/// <summary>
/// The engine's single cash ledger (review F1130/F1132/F1138). Every cash
/// movement goes through one of these helpers so it is booked exactly once:
/// operating flows in the P&amp;L, CAPEX and loan principal on the balance sheet.
/// <see cref="FinancialStatements.LifetimeNetCashFlow"/> therefore equals the
/// change in cash since the game started, which the economy tests assert.
/// </summary>
public partial class SimulationEngine
{
    /// <summary>Category for cash that moved outside the ledger (should never appear).</summary>
    public const string UnclassifiedCashCategory = "unclassified";

    /// <summary>
    /// The balance implied by every booked flow. Cash that moves without a
    /// booking (a system mutating FinanceManager directly) shows up as a gap
    /// and is booked under <see cref="UnclassifiedCashCategory"/> by
    /// <see cref="ReconcileUnbookedCash"/>, so the statements always add up.
    /// </summary>
    private decimal _bookedCash;

    /// <summary>
    /// Total cash (absolute) that moved without a booking this session. Zero in
    /// a correct economy; the reconciliation tests assert it stays zero.
    /// </summary>
    public decimal UnbookedCashTotal { get; private set; }

    /// <summary>
    /// Pay a cost that cannot be refused (operations, regulation, debt service,
    /// tax). When cash is short the balance goes negative — the documented debt
    /// mechanic — and <paramref name="shortfallWarning"/> is raised if given.
    /// </summary>
    public void PayCost(decimal amount, CostBooking booking, string category, string? shortfallWarning = null)
    {
        if (amount <= 0m || FinanceManager == null) return;
        if (!FinanceManager.Spend(amount))
        {
            if (shortfallWarning != null)
                OnEventTriggered?.Invoke(shortfallWarning);
            FinanceManager.ForceSpend(amount);
        }
        _bookedCash -= amount;
        Book(amount, booking, category);
    }

    /// <summary>
    /// Pay for a player purchase. Refused (returns false, nothing moves or is
    /// booked) when cash is short; booked once when paid.
    /// </summary>
    public bool TryPayCost(decimal amount, CostBooking booking, string category)
    {
        if (amount <= 0m) return true;
        if (FinanceManager == null || !FinanceManager.Spend(amount)) return false;
        _bookedCash -= amount;
        Book(amount, booking, category);
        return true;
    }

    /// <summary>Receive operating or other income (sales, payouts, grants, rewards).</summary>
    public void ReceiveIncome(decimal amount, string category)
    {
        if (amount <= 0m || FinanceManager == null) return;
        FinanceManager.AddRevenue(amount);
        _bookedCash += amount;
        FinancialStatements?.RecordRevenue(amount, category);
        RecordPnl(category, amount, income: true);
    }

    /// <summary>Receive borrowed money: cash in, liability up (never revenue).</summary>
    internal void ReceiveLoan(decimal amount)
    {
        if (amount <= 0m || FinanceManager == null) return;
        FinanceManager.AddLoanPrincipal(amount);
        _bookedCash += amount;
        FinancialStatements?.AddLiability(amount);
    }

    /// <summary>Receive a tax refund: cash in, booked tax down.</summary>
    internal void ReceiveTaxRefund(decimal amount)
    {
        if (amount <= 0m || FinanceManager == null) return;
        FinanceManager.AddRevenue(amount);
        _bookedCash += amount;
        FinancialStatements?.RecordTaxRefund(amount);
        RecordPnl("tax", amount, income: false);
    }

    /// <summary>
    /// Book cash a subsystem already moved on FinanceManager (event effects,
    /// achievement rewards, construction paid by an expansion system):
    /// positive = income, negative = cost under <paramref name="costBooking"/>.
    /// </summary>
    internal void BookSettledCash(decimal delta, string category, CostBooking costBooking = CostBooking.Opex)
    {
        if (delta == 0m) return;
        _bookedCash += delta;
        if (delta > 0m)
        {
            FinancialStatements?.RecordRevenue(delta, category);
            RecordPnl(category, delta, income: true);
        }
        else
            Book(-delta, costBooking, category);
    }

    /// <summary>
    /// Book any gap between cash and the ledger as unclassified (runs at the
    /// end of every simulated day and before a save). A non-zero gap means a
    /// code path moved money without a booking — the economy tests require
    /// that this never happens.
    /// </summary>
    internal void ReconcileUnbookedCash()
    {
        if (FinanceManager == null) return;
        decimal gap = FinanceManager.Balance - _bookedCash;
        if (gap == 0m) return;
        SimLog.Warn($"Ledger: {gap:N2} NOK moved without a booking (booked as '{UnclassifiedCashCategory}')");
        UnbookedCashTotal += Math.Abs(gap);
        BookSettledCash(gap, UnclassifiedCashCategory);
    }

    /// <summary>Accept the current cash as the ledger baseline (new game / load).</summary>
    internal void ResetLedgerBaseline() => _bookedCash = FinanceManager?.Balance ?? 0m;

    /// <summary>Start of a new game or scenario: the current cash is the opening balance.</summary>
    internal void OpenBooks()
    {
        ResetLedgerBaseline();
        State.OpeningBalanceNok = FinanceManager?.Balance ?? 0m;
        OpenStory();
    }

    /// <summary>
    /// Book a deliberate balance reset (scenario starting capital) as a capital
    /// adjustment so the lifetime totals keep reconciling with cash.
    /// </summary>
    internal void BookCapitalAdjustment(decimal balanceBefore)
    {
        if (FinanceManager == null) return;
        decimal delta = FinanceManager.Balance - balanceBefore;
        if (delta == 0m) return;
        _bookedCash += delta;
        FinancialStatements?.RecordEquityRaised(delta);
    }

    private void Book(decimal amount, CostBooking booking, string category)
    {
        var fs = FinancialStatements;
        if (fs == null) return;
        switch (booking)
        {
            case CostBooking.Cogs: fs.RecordCOGS(amount, category); break;
            case CostBooking.Opex: fs.RecordOperatingExpense(amount, category); break;
            case CostBooking.Interest: fs.RecordInterest(amount); break;
            case CostBooking.Tax: fs.RecordTax(amount); break;
            case CostBooking.Capex: fs.AddAsset(amount); break;
            case CostBooking.LoanPrincipal: fs.RepayLiability(amount); break;
        }
        // The P&L story (U2-03) sees every P&L line under the statements' own
        // category: interest and tax carry theirs whatever the caller named.
        switch (booking)
        {
            case CostBooking.Cogs:
            case CostBooking.Opex:
                RecordPnl(category, -amount, income: false);
                break;
            case CostBooking.Interest:
                RecordPnl("interest", -amount, income: false);
                break;
            case CostBooking.Tax:
                RecordPnl("tax", -amount, income: false);
                break;
        }
    }
}
