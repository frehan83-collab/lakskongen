using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Calibration;
using LakskongenSim.Core;
using LakskongenSim.Events;
using LakskongenSim.Finance;
using LakskongenSim.Tests.Integration;
using Xunit;

namespace LakskongenSim.Tests.Economy;

/// <summary>
/// Roadmap U2-03: the liquidity ladder on top of S1-03's solvency ladder.
/// Negative cash pays interest above the term-loan rate; the bank reads the
/// running burn and warns at least 30 days and calls the company in at least
/// 10 days before bankruptcy, with a rescue loan against the fish that the
/// company takes or declines before the offer's deadline.
/// </summary>
public class LiquidityLadderTests
{
    private static readonly LiquidityCalibration Rules = CalibrationRegistry.Load().Liquidity;

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

    private sealed record Decline(int BankruptDay, int WarningDay, int BankCallDay, decimal Offer, SimulationEngine Engine);

    /// <summary>
    /// Plays a passive company (no harvest, no answer to the bank) until it
    /// goes bankrupt and records the rungs of the episode that ended in it.
    /// </summary>
    private static Decline PlayPassiveToBankruptcy(SimulationEngine engine, int maxDays = 1200)
    {
        int warning = -1, call = -1;
        decimal offer = 0m;
        for (int d = 0; d < maxDays && !engine.IsBankrupt; d++)
        {
            engine.Advance(1);
            var ladder = engine.State.Liquidity;
            if (ladder.WarningDay >= 0) warning = ladder.WarningDay;
            if (ladder.BankCallDay >= 0 && call != ladder.BankCallDay)
            {
                call = ladder.BankCallDay;
                offer = ladder.Offer is { Status: RescueOfferStatus.Open, OfferedDay: var day } o && day == call ? o.AmountNok : 0m;
            }
            if (ladder.EpisodeStartDay < 0) { warning = -1; call = -1; offer = 0m; }
        }
        Assert.True(engine.IsBankrupt, $"no bankruptcy within {maxDays} days (cash {engine.FinanceManager.Balance:N0})");
        return new Decline(engine.State.BankruptDay!.Value, warning, call, offer, engine);
    }

    // ── 1. Overdraft interest ───────────────────────────────────────────────

    [Fact]
    public void Calibration_OverdraftCostsMoreThanATermLoan_AndTheRescueLoanIsDearer()
    {
        var c = CalibrationRegistry.Load();
        // Forsinkelsesrente H2 2025: Norges Bank's policy rate 4.25 % + 8 pp (Finanstilsynet).
        Assert.Equal(0.1225, c.Liquidity.OverdraftInterestRate, 6);
        Assert.True(c.Liquidity.OverdraftInterestRate > c.Finance.LoanInterestRate,
            "riding the overdraft must cost more than borrowing (RUN_LOG §8 'overdraft is free')");
        // NIBOR ~4.5 % + 950 bps, 98 % issue price, three years (June 2025 fish-farm bond).
        Assert.Equal(0.14, c.Liquidity.RescueLoanInterestRate, 6);
        Assert.Equal(0.02, c.Liquidity.RescueLoanArrangementFee, 6);
        Assert.Equal(36, c.Liquidity.RescueLoanTermMonths);
        Assert.True(c.Liquidity.RescueLoanInterestRate > c.Finance.LoanInterestRate);
        // The ladder's rungs keep the roadmap's lead times with room for an accelerating burn.
        Assert.True(c.Liquidity.WarningRunwayDays >= 2 * 30);
        Assert.True(c.Liquidity.BankCallRunwayDays >= 2 * 10);
        Assert.True(c.Liquidity.ReArmRunwayDays > c.Liquidity.WarningRunwayDays);
        Assert.True(c.Liquidity.RescueOfferValidDays < c.Liquidity.BankCallRunwayDays,
            "the offer must expire before the floor at the burn that triggered the call");
        // The file and the code defaults agree.
        Assert.Equal(new LiquidityCalibration(), c.Liquidity);
    }

    [Fact]
    public void NegativeCash_PaysDailyInterestAtTheOverdraftRate_BookedAsInterest()
    {
        var engine = Started("po3_red_sone", 3, events: false);
        engine.PayCost(engine.FinanceManager.Balance + 10_000_000m, CostBooking.Opex, "test");
        Assert.Equal(-10_000_000m, engine.FinanceManager.Balance);
        decimal interestBefore = engine.FinancialStatements.LifetimeInterest;

        engine.ChargeOverdraftInterest();

        decimal expected = decimal.Round(10_000_000m * 0.1225m / 365m, 2);
        Assert.Equal(-10_000_000m - expected, engine.FinanceManager.Balance);
        Assert.Equal(interestBefore + expected, engine.FinancialStatements.LifetimeInterest);
        Assert.Equal(expected, engine.State.Liquidity.OverdraftInterestPaidNok);

        // Cash at or above zero pays nothing.
        var black = Started("po3_red_sone", 3, events: false);
        decimal cash = black.FinanceManager.Balance;
        black.ChargeOverdraftInterest();
        Assert.Equal(cash, black.FinanceManager.Balance);
        Assert.Equal(0m, black.State.Liquidity.OverdraftInterestPaidNok);
    }

    /// <summary>
    /// Player path: two identical companies deep in the overdraft; the one
    /// that borrows the shortfall at the term-loan rate ends with more equity
    /// than the one that rides the overdraft (before U2-03 they ended equal:
    /// the overdraft was free and the loan paid interest).
    /// </summary>
    [Fact]
    public void BorrowingBeatsRidingTheOverdraft()
    {
        SimulationEngine Run(bool borrow)
        {
            var engine = Started("po3_red_sone", 5, events: false);
            while (engine.FinanceManager.Balance > -3_000_000m && engine.State.Day < 500)
                engine.Advance(1);
            Assert.True(engine.FinanceManager.Balance < -3_000_000m, $"day {engine.State.Day} cash {engine.FinanceManager.Balance:N0}");
            if (borrow)
                Assert.True(engine.TakeLoan(5_000_000, 12, 0.0).Ok);
            engine.Advance(60);
            return engine;
        }
        static decimal Equity(SimulationEngine e) => e.FinanceManager.Balance - e.State.ActiveLoans.Sum(l => l.Principal);

        var rider = Run(borrow: false);
        var borrower = Run(borrow: true);
        Assert.True(Equity(borrower) > Equity(rider),
            $"borrower {Equity(borrower):N0} vs overdraft rider {Equity(rider):N0}");
        Assert.True(rider.State.Liquidity.OverdraftInterestPaidNok > borrower.State.Liquidity.OverdraftInterestPaidNok);
    }

    // ── 2. The bank's reading of the cash (pure) ─────────────────────────────

    [Fact]
    public void Burn_IsTheMedianWeek_SoAOneOffNeitherHidesNorFakesIt()
    {
        // 35 days falling 10 000 a day.
        var steady = Enumerable.Range(0, 36).Select(i => 1_000_000m - 10_000m * i).ToList();
        Assert.Equal(10_000m, LiquidityRules.BurnPerDay(steady, 7));

        // A 2 MNOK harvest payment on one day does not hide the burn...
        var harvest = steady.Select((c, i) => i >= 20 ? c + 2_000_000m : c).ToList();
        Assert.Equal(10_000m, LiquidityRules.BurnPerDay(harvest, 7));
        // ...and a 3 MNOK site bought on one day does not fake one.
        var purchase = steady.Select((c, i) => i >= 20 ? c - 3_000_000m : c).ToList();
        Assert.Equal(10_000m, LiquidityRules.BurnPerDay(purchase, 7));

        // A lump every week (a storm repair) is part of the running burn,
        // which a median of single days would miss.
        var lumpy = new List<decimal> { 1_000_000m };
        for (int i = 1; i <= 35; i++)
            lumpy.Add(lumpy[^1] - 10_000m - (i % 7 == 0 ? 140_000m : 0m));
        Assert.Equal(30_000m, LiquidityRules.BurnPerDay(lumpy, 7));
        Assert.True(LiquidityRules.BurnPerDay(lumpy, 1) < 15_000m, "a single-day median misses the weekly lump");
    }

    [Fact]
    public void Forecast_GivesTheRunwayToTheFloor_AndNothingWithoutHistoryOrDecline()
    {
        var steady = Enumerable.Range(0, 36).Select(i => 1_000_000m - 10_000m * i).ToList();   // ends at 650 000
        var f = LiquidityRules.Forecast(steady, -1_000_000m, 400, 14, 7);
        Assert.Equal(10_000m, f.BurnPerDayNok);
        Assert.Equal(165.0, f.RunwayDays, 6);
        Assert.Equal(565, f.ProjectedFloorDay);

        var tooShort = steady.Take(10).ToList();
        Assert.False(LiquidityRules.Forecast(tooShort, -1_000_000m, 9, 14, 7).IsDeclining);
        var rising = steady.AsEnumerable().Reverse().ToList();
        var up = LiquidityRules.Forecast(rising, -1_000_000m, 400, 14, 7);
        Assert.False(up.IsDeclining);
        Assert.True(double.IsPositiveInfinity(up.RunwayDays));
        Assert.Equal(-1, up.ProjectedFloorDay);
    }

    [Fact]
    public void RescueAmount_CoversTheBurnRoundedUp_WithinTheCollateral()
    {
        // 120 days of 43 210 NOK = 5 185 200, rounded up to 5 500 000.
        Assert.Equal(5_500_000m, LiquidityRules.RescueAmount(43_210m, 50_000_000m, Rules));
        // The cap binds (rounded down to a whole step).
        Assert.Equal(3_000_000m, LiquidityRules.RescueAmount(43_210m, 3_400_000m, Rules));
        // Less collateral than 30 days of burn: no offer.
        Assert.Equal(0m, LiquidityRules.RescueAmount(43_210m, 1_200_000m, Rules));
        Assert.Equal(0m, LiquidityRules.RescueAmount(0m, 50_000_000m, Rules));
        // The cap: half the fish, less the drawn facility and the loans.
        Assert.Equal(6_000_000m, LiquidityRules.CollateralCap(20_000_000m, -3_000_000m, 1_000_000m, 0.5));
        Assert.Equal(0m, LiquidityRules.CollateralCap(4_000_000m, -3_000_000m, 0m, 0.5));
    }

    // ── 3. Acceptance 3: a scripted decline is warned and called in time ─────

    /// <summary>
    /// U2-03 acceptance 3. Passive companies (no harvest, the bank never
    /// answered) decline into bankruptcy in every shipped scenario that has
    /// one: the liquidity warning of the final episode comes at least 30 sim
    /// days and the bank call at least 10 days before the bankruptcy, in that
    /// order, each once, logged and on the typed event bus with a deadline.
    /// Measured leads over 54 runs: docs/BALANCE.md §1n.
    /// </summary>
    [Theory]
    [InlineData("forste_sesong", 1)]
    [InlineData("forste_sesong", 2)]
    [InlineData("forste_sesong", 5)]
    [InlineData("po3_red_sone", 1)]
    [InlineData("po3_red_sone", 2)]
    [InlineData("po3_red_sone", 3)]
    [InlineData("krise_manager", 3)]
    [InlineData("krise_manager", 6)]
    [InlineData("algekatastrofe", 3)]
    [InlineData("isa_utbrudd", 3)]
    [InlineData("lofoten_eventyr", 2)]
    [InlineData("laksebaronene", 3)]
    [InlineData("ressursrente", 4)]
    public void ScriptedDecline_IsWarned30DaysAndCalled10DaysBeforeBankruptcy(string scenarioId, int seed)
    {
        var engine = Started(scenarioId, seed);
        var events = new List<GameEvent>();
        engine.GameEvents.Delivered += events.Add;
        int warnings = 0, calls = 0;
        int lastWarning = -1, lastCall = -1;
        for (int d = 0; d < 1200 && !engine.IsBankrupt; d++)
        {
            engine.Advance(1);
            engine.DrainDayNotes();
            var ladder = engine.State.Liquidity;
            if (ladder.WarningDay >= 0 && ladder.WarningDay != lastWarning) { lastWarning = ladder.WarningDay; warnings++; }
            if (ladder.BankCallDay >= 0 && ladder.BankCallDay != lastCall) { lastCall = ladder.BankCallDay; calls++; }
        }
        Assert.True(engine.IsBankrupt, $"{scenarioId} s{seed}: no bankruptcy");
        int bankrupt = engine.State.BankruptDay!.Value;
        var ladderAtEnd = engine.State.Liquidity;
        Assert.True(ladderAtEnd.WarningDay >= 0 && ladderAtEnd.BankCallDay >= 0,
            $"{scenarioId} s{seed}: the final episode had no warning ({ladderAtEnd.WarningDay}) or call ({ladderAtEnd.BankCallDay})");
        Assert.True(bankrupt - ladderAtEnd.WarningDay >= 30,
            $"{scenarioId} s{seed}: liquidity warning day {ladderAtEnd.WarningDay}, bankrupt day {bankrupt} ({bankrupt - ladderAtEnd.WarningDay} days)");
        Assert.True(bankrupt - ladderAtEnd.BankCallDay >= 10,
            $"{scenarioId} s{seed}: bank call day {ladderAtEnd.BankCallDay}, bankrupt day {bankrupt} ({bankrupt - ladderAtEnd.BankCallDay} days)");
        Assert.True(ladderAtEnd.WarningDay <= ladderAtEnd.BankCallDay);

        // Logged in order, the final warning and call once each.
        var log = engine.State.EventLog
            .Where(e => e.Id is SimulationEngine.LiquidityWarningKey or SimulationEngine.BankCallKey or SimulationEngine.BankruptcyKey)
            .Where(e => e.Day >= ladderAtEnd.WarningDay)
            .Select(e => e.Id).ToList();
        Assert.Equal(new[] { SimulationEngine.LiquidityWarningKey, SimulationEngine.BankCallKey, SimulationEngine.BankruptcyKey }, log);

        // On the bus: a warning and a critical call, both answered by raising
        // capital and both with a deadline on or before the projected floor day.
        var warning = Assert.Single(events, e => e.Type == GameEventType.LiquidityWarning && e.Day == ladderAtEnd.WarningDay);
        Assert.Equal(GameEventSeverity.Warning, warning.Severity);
        Assert.Equal(GameEventCatalog.VerbRaiseCapital, warning.CounterVerb);
        Assert.True(warning.DeadlineDay > warning.Day);
        var call = Assert.Single(events, e => e.Type == GameEventType.BankCall && e.Day == ladderAtEnd.BankCallDay);
        Assert.Equal(GameEventSeverity.Critical, call.Severity);
        Assert.Equal(GameEventCatalog.VerbRaiseCapital, call.CounterVerb);
        Assert.InRange(call.DeadlineDay, call.Day, bankrupt + Rules.RescueOfferValidDays);
    }

    /// <summary>A plain linear decline (a fixed cost every day) on a bare game: warned and called at the thresholds.</summary>
    [Fact]
    public void LinearDecline_IsWarnedAtTheRunwayThresholds()
    {
        var engine = new SimulationEngine();
        engine.FinanceManager = new FinanceManager(500_000m);
        engine.Initialize(enableEvents: false, seed: 11);
        int guard = 0;
        while (!engine.IsBankrupt && guard++ < 400)
        {
            engine.PayCost(20_000m, CostBooking.Opex, "test_decline");
            engine.CheckSolvency();
            engine.CheckLiquidity();
            engine.State.Day++;
        }
        Assert.True(engine.IsBankrupt);
        int bankrupt = engine.State.BankruptDay!.Value;
        var ladder = engine.State.Liquidity;
        Assert.InRange(bankrupt - ladder.WarningDay, Rules.WarningRunwayDays - 1, Rules.WarningRunwayDays + 1);
        Assert.InRange(bankrupt - ladder.BankCallDay, Rules.BankCallRunwayDays - 1, Rules.BankCallRunwayDays + 1);
    }

    // ── 4. The rescue loan ───────────────────────────────────────────────────

    [Fact]
    public void BankCall_OffersARescueLoan_SizedByTheBurn_DueBeforeTheFloor()
    {
        var engine = Started("po3_red_sone", 2);
        while (engine.State.Liquidity.BankCallDay < 0 && !engine.IsBankrupt && engine.State.Day < 800)
            engine.Advance(1);
        var ladder = engine.State.Liquidity;
        var offer = Assert.IsType<RescueOffer>(ladder.Offer);
        var forecast = engine.GetLiquidityForecast();
        Assert.Equal(RescueOfferStatus.Open, offer.Status);
        Assert.Equal(engine.State.Day, offer.OfferedDay);
        Assert.Equal(LiquidityRules.RescueAmount(forecast.BurnPerDayNok, engine.RescueCollateralCapNok(), Rules), offer.AmountNok);
        Assert.True(offer.AmountNok >= forecast.BurnPerDayNok * Rules.RescueLoanMinCoverDays);
        Assert.Equal(0m, offer.AmountNok % Rules.RescueLoanRoundingNok);
        Assert.InRange(offer.ExpiresDay, offer.OfferedDay, offer.OfferedDay + Rules.RescueOfferValidDays);
        Assert.True(offer.ExpiresDay < forecast.ProjectedFloorDay);
        Assert.Equal(0.14, offer.InterestRate, 6);
        var call = engine.State.EventLog.Last(e => e.Id == SimulationEngine.BankCallKey);
        Assert.Contains("redningslån", call.Description);
    }

    /// <summary>
    /// Player path (outcome change): the same company with the same seed goes
    /// bankrupt when it declines the rescue offer and is still trading two
    /// months later when it takes it. The loan is liquidity and a liability,
    /// never revenue; its fee is interest.
    /// </summary>
    [Fact]
    public void TakingTheRescueLoan_KeepsTheCompanyAlive_DecliningDoesNot()
    {
        SimulationEngine ToCall()
        {
            var e = Started("po3_red_sone", 2);
            while (e.State.Liquidity.Offer is not { Status: RescueOfferStatus.Open } && !e.IsBankrupt && e.State.Day < 800)
                e.Advance(1);
            Assert.False(e.IsBankrupt);
            return e;
        }

        var decliner = ToCall();
        Assert.True(decliner.DeclineRescueLoan().Ok);
        Assert.Equal(RescueOfferStatus.Declined, decliner.State.Liquidity.Offer!.Status);
        Assert.Equal("no_rescue_offer", decliner.AcceptRescueLoan().ErrorKey);
        var declined = PlayPassiveToBankruptcy(decliner);

        var taker = ToCall();
        var offer = taker.State.Liquidity.Offer!;
        decimal cash = taker.FinanceManager.Balance;
        decimal revenue = taker.FinancialStatements.LifetimeRevenue;
        decimal interest = taker.FinancialStatements.LifetimeInterest;
        var result = taker.AcceptRescueLoan();
        Assert.True(result.Ok, result.Message);
        Assert.Equal(cash + offer.AmountNok - offer.FeeNok, taker.FinanceManager.Balance);
        Assert.Equal(revenue, taker.FinancialStatements.LifetimeRevenue);
        Assert.Equal(interest + offer.FeeNok, taker.FinancialStatements.LifetimeInterest);
        var loan = Assert.Single(taker.State.ActiveLoans, l => l.Id == result.Id);
        Assert.StartsWith(SimulationEngine.RescueLoanIdPrefix, loan.Id);
        Assert.Equal(offer.AmountNok, loan.Principal);
        Assert.Equal(0.14m, loan.InterestRate);
        Assert.Equal(SimCalendar.DaysInMonthsFrom(taker.State.Day, 36), loan.TermDays);
        Assert.Equal(1, taker.State.Liquidity.RescueLoansTaken);
        var report = taker.GetScenarioEndReport();
        Assert.Equal(taker.State.Day, report.RescueLoanDay);
        Assert.Equal(offer.AmountNok, report.RescueLoanNok);
        Assert.Equal(-1, decliner.GetScenarioEndReport().RescueLoanDay);
        Assert.Equal(-1, taker.State.Liquidity.EpisodeStartDay);
        Assert.Contains(taker.State.EventLog, e => e.Id == SimulationEngine.RescueLoanTakenKey);
        Assert.Equal("no_rescue_offer", taker.AcceptRescueLoan().ErrorKey);

        taker.Advance(declined.BankruptDay - taker.State.Day + 60);
        Assert.False(taker.IsBankrupt,
            $"the rescued company went bankrupt on day {taker.State.BankruptDay}; the decliner on day {declined.BankruptDay}");
    }

    [Fact]
    public void AnUnansweredOffer_Expires_AndCannotBeTakenAfterwards()
    {
        var engine = Started("po3_red_sone", 2);
        while (engine.State.Liquidity.Offer is not { Status: RescueOfferStatus.Open } && engine.State.Day < 800)
            engine.Advance(1);
        int expires = engine.State.Liquidity.Offer!.ExpiresDay;
        engine.Advance(expires - engine.State.Day + 1);
        Assert.False(engine.IsBankrupt);
        Assert.Equal(RescueOfferStatus.Expired, engine.State.Liquidity.Offer!.Status);
        Assert.Contains(engine.State.EventLog, e => e.Id == SimulationEngine.RescueExpiredKey);
        Assert.Equal("no_rescue_offer", engine.AcceptRescueLoan().ErrorKey);
    }

    [Fact]
    public void ACompanyWithoutFishToPledge_IsCalledIn_ButOfferedNothing()
    {
        // krise_manager's small stock cannot carry its drawn facility.
        var engine = Started("krise_manager", 3);
        while (engine.State.Liquidity.BankCallDay < 0 && !engine.IsBankrupt && engine.State.Day < 900)
            engine.Advance(1);
        Assert.True(engine.State.Liquidity.BankCallDay >= 0);
        Assert.Null(engine.State.Liquidity.Offer);
        Assert.True(engine.RescueCollateralCapNok() < engine.GetLiquidityForecast().BurnPerDayNok * Rules.RescueLoanMinCoverDays,
            $"cap {engine.RescueCollateralCapNok():N0} carries {Rules.RescueLoanMinCoverDays} days of burn");
        Assert.Contains("ingen redningskreditt", engine.State.EventLog.Last(e => e.Id == SimulationEngine.BankCallKey).Description);
        Assert.Equal("no_rescue_offer", engine.AcceptRescueLoan().ErrorKey);
    }

    [Fact]
    public void ASecondBankCall_BringsNoSecondRescueLoan()
    {
        var engine = Started("po3_red_sone", 1);
        while (engine.State.Liquidity.Offer is not { Status: RescueOfferStatus.Open } && engine.State.Day < 800)
            engine.Advance(1);
        Assert.True(engine.AcceptRescueLoan().Ok);
        var first = engine.State.Liquidity.Offer!;
        while (engine.State.Liquidity.BankCallsIssued < 2 && !engine.IsBankrupt && engine.State.Day < 1500)
            engine.Advance(1);
        Assert.Equal(2, engine.State.Liquidity.BankCallsIssued);
        Assert.Same(first, engine.State.Liquidity.Offer);   // no new offer
        Assert.Contains("allerede gitt", engine.State.EventLog.Last(e => e.Id == SimulationEngine.BankCallKey).Description);
    }

    [Fact]
    public void TheLadderReArms_WhenTheCashRecovers()
    {
        var engine = Started("po3_red_sone", 3);
        while (engine.State.Liquidity.WarningDay < 0 && engine.State.Day < 800)
            engine.Advance(1);
        Assert.Equal(1, engine.State.Liquidity.WarningsIssued);
        int warned = engine.State.Liquidity.WarningDay;

        engine.ReceiveIncome(40_000_000m, "test_rescue");
        engine.Advance(Rules.BurnStepDays + 1);
        Assert.Equal(-1, engine.State.Liquidity.EpisodeStartDay);
        Assert.Equal(-1, engine.State.Liquidity.WarningDay);
        Assert.False(engine.Pacing.IsWatching(engine.LiquidityEventKey()));

        var again = PlayPassiveToBankruptcy(engine);
        Assert.True(again.WarningDay > warned);
        Assert.True(engine.State.Liquidity.WarningsIssued >= 2);
    }

    /// <summary>A save in the middle of an episode continues exactly like the game that was never saved.</summary>
    [Fact]
    public void TheLadder_SurvivesASaveAndLoad()
    {
        var engine = Started("po3_red_sone", 4);
        while (engine.State.Liquidity.WarningDay < 0 && engine.State.Day < 800)
            engine.Advance(1);
        engine.Advance(5);
        var resumed = SaveStateComparer.Resume(SaveStateComparer.Serialize(engine), events: true);
        Assert.Equal(engine.State.Liquidity.CashHistory, resumed.State.Liquidity.CashHistory);

        var a = PlayPassiveToBankruptcy(engine);
        var b = PlayPassiveToBankruptcy(resumed);
        Assert.Equal(a.BankruptDay, b.BankruptDay);
        Assert.Equal(a.WarningDay, b.WarningDay);
        Assert.Equal(a.BankCallDay, b.BankCallDay);
        Assert.Equal(a.Offer, b.Offer);
        Assert.Equal(engine.State.Liquidity.OverdraftInterestPaidNok, resumed.State.Liquidity.OverdraftInterestPaidNok);
    }
}
