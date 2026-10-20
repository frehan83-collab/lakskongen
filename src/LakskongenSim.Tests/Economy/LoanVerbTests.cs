using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Finance;
using Xunit;

namespace LakskongenSim.Tests.Economy;

/// <summary>
/// Review F943: the finance panel's loan card read and repaid through methods
/// that existed nowhere. Repayment and the monthly payment are engine verbs.
/// </summary>
public class LoanVerbTests
{
    private static SimulationEngine NewEngine(decimal cash = 20_000_000m)
    {
        var engine = new SimulationEngine();
        engine.FinanceManager = new FinanceManager(cash);
        engine.Initialize(enableEvents: false, seed: 61);
        return engine;
    }

    [Fact]
    public void RepayLoan_PaysPrincipalOldestFirst_AndClosesAPaidLoan()
    {
        var engine = NewEngine();
        Assert.True(engine.TakeLoan(2_000_000, 36, 0.07).Ok);
        engine.Advance(10);
        Assert.True(engine.TakeLoan(3_000_000, 36, 0.07).Ok);
        var oldest = engine.State.ActiveLoans.OrderByDescending(l => l.DaysTaken).First();
        decimal oldestBefore = oldest.Principal;
        decimal debtBefore = engine.TotalDebtNok;
        decimal cashBefore = engine.FinanceManager.Balance;

        var repaid = engine.RepayLoan(500_000m);

        Assert.True(repaid.Ok, repaid.Message);
        Assert.Equal(cashBefore - 500_000m, engine.FinanceManager.Balance);
        Assert.Equal(debtBefore - 500_000m, engine.TotalDebtNok);
        Assert.Equal(oldestBefore - 500_000m, oldest.Principal);

        var rest = engine.RepayLoan(oldest.Principal);
        Assert.True(rest.Ok);
        Assert.DoesNotContain(oldest, engine.State.ActiveLoans);
        Assert.Single(engine.State.ActiveLoans);
        Assert.Equal(3_000_000m, engine.State.ActiveLoans.Single().OriginalPrincipal); // the newer loan remains

        engine.Advance(1);
        Assert.Equal(0m, engine.UnbookedCashTotal);
    }

    [Fact]
    public void RepayLoan_CapsAtTheDebt_AndRefusesWithAKey()
    {
        var engine = NewEngine();
        Assert.Equal("no_loans", engine.RepayLoan(1_000m).ErrorKey);
        Assert.True(engine.TakeLoan(1_000_000, 12, 0.07).Ok);
        Assert.Equal("invalid_amount", engine.RepayLoan(0m).ErrorKey);

        var all = engine.RepayLoan(5_000_000m); // more than owed
        Assert.True(all.Ok);
        Assert.Equal(1_000_000.0, all.Value, 2);
        Assert.Empty(engine.State.ActiveLoans);

        var broke = NewEngine(cash: 100_000m);
        Assert.True(broke.TakeLoan(50_000, 12, 0.07).Ok);
        broke.FinanceManager.ForceSpend(broke.FinanceManager.Balance);
        Assert.Equal("insufficient_funds", broke.RepayLoan(50_000m).ErrorKey);
        Assert.Single(broke.State.ActiveLoans);
    }

    /// <summary>Re-review: ids counted the same-day loans, so a third loan after an
    /// early repayment took a live loan's id.</summary>
    [Fact]
    public void LoanIds_StayUnique_AfterAnEarlyRepayment()
    {
        var engine = NewEngine();
        Assert.True(engine.TakeLoan(1_000_000, 36, 0.07).Ok);
        Assert.True(engine.TakeLoan(1_000_000, 36, 0.07).Ok);
        var oldest = engine.State.ActiveLoans.First();
        Assert.True(engine.RepayLoan(oldest.Principal).Ok);
        Assert.True(engine.TakeLoan(1_000_000, 36, 0.07).Ok);

        var ids = engine.State.ActiveLoans.Select(l => l.Id).ToList();
        Assert.Equal(2, ids.Count);
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void MonthlyPayment_IsThirtyDaysOfTheDailyAmortization()
    {
        var engine = NewEngine();
        Assert.Equal(0m, engine.MonthlyLoanPaymentNok());
        Assert.True(engine.TakeLoan(3_600_000, 36, 0.10).Ok);
        decimal monthly = engine.MonthlyLoanPaymentNok();
        // 3.6M over 1080 days = 3,333/day principal + 3.6M x 10% / 365 = 986/day interest.
        Assert.InRange(monthly, 125_000m, 135_000m);

        decimal cash = engine.FinanceManager.Balance;
        engine.Advance(30);
        decimal paid = cash - engine.FinanceManager.Balance;
        Assert.True(paid >= monthly * 0.9m, $"paid {paid:N0} vs quoted {monthly:N0}");
    }
}
