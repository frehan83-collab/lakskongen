using System;
using System.Globalization;
using System.Linq;
using LakskongenSim.Calibration;
using LakskongenSim.Events;
using LakskongenSim.Finance;

namespace LakskongenSim.Core;

/// <summary>
/// The liquidity ladder (roadmap U2-03), on top of S1-03's solvency ladder.
///
/// S1-03 warns by depth (overdraft, half-way, final) and declares bankruptcy
/// below the scenario's floor. That ladder has no sense of time: a steady
/// decline could cross all three rungs in a week. The bank reads the cash the
/// way a lender does: its running burn (the median daily change of the last
/// 30 days) and the runway to the floor at that burn. At 60 days it sends a
/// liquidity warning; at 21 days it calls the company in and offers a rescue
/// loan against the fish in the sea, which the company accepts or declines
/// before the offer's deadline (the clock slows and stops before it, U2-07).
///
/// Negative cash also costs interest now (it was free, RUN_LOG §8): the
/// statutory late-payment rate, above the term loan's rate, so borrowing
/// beats riding the overdraft. No RNG; every number is in the saved state.
/// </summary>
public partial class SimulationEngine
{
    /// <summary>Event-log id of the liquidity warning.</summary>
    public const string LiquidityWarningKey = "liquidity_warning";
    /// <summary>Event-log id of the bank call.</summary>
    public const string BankCallKey = "liquidity_bank_call";
    /// <summary>Event-log id of a rescue loan taken.</summary>
    public const string RescueLoanTakenKey = "liquidity_rescue_loan";
    /// <summary>Event-log id of a declined rescue offer.</summary>
    public const string RescueDeclinedKey = "liquidity_rescue_declined";
    /// <summary>Event-log id of a rescue offer nobody answered.</summary>
    public const string RescueExpiredKey = "liquidity_rescue_expired";
    /// <summary>Ledger category of overdraft interest (booked as interest).</summary>
    public const string OverdraftInterestCategory = "overdraft_interest";
    /// <summary>Ledger category of the rescue loan's up-front fee (booked as interest).</summary>
    public const string RescueLoanFeeCategory = "rescue_loan_fee";
    /// <summary>Loan id prefix of a rescue loan.</summary>
    public const string RescueLoanIdPrefix = "rescue_";

    /// <summary>The ladder's rules (calibration section <c>liquidity</c>).</summary>
    public LiquidityCalibration LiquidityRulesConfig => _calibration.Liquidity;

    /// <summary>Today's reading of the cash: burn, runway and the day the floor is reached.</summary>
    public LiquidityForecast GetLiquidityForecast()
    {
        var rules = LiquidityRulesConfig;
        var history = State?.Liquidity?.CashHistory;
        if (history == null || history.Count == 0)
            return LiquidityRules.Forecast(new[] { FinanceManager?.Balance ?? 0m }, BankruptcyFloorNok, State?.Day ?? 0,
                rules.MinBurnHistoryDays, rules.BurnStepDays);
        return LiquidityRules.Forecast(history, BankruptcyFloorNok, State!.Day, rules.MinBurnHistoryDays, rules.BurnStepDays);
    }

    /// <summary>The standing biomass at the norm price (NOK), the bank's collateral.</summary>
    public decimal BiomassCollateralValueNok()
    {
        if (State == null) return 0m;
        decimal normPrice = PenPriceCouncil?.MonthlyPenPrice > 0m
            ? PenPriceCouncil.MonthlyPenPrice
            : MarketSimulation?.PricePerKg ?? 0m;
        return (decimal)State.Populations.Sum(p => p.GetTotalBiomassKg()) * normPrice;
    }

    /// <summary>The most the bank lends against the fish today (NOK).</summary>
    public decimal RescueCollateralCapNok()
        => LiquidityRules.CollateralCap(BiomassCollateralValueNok(), FinanceManager?.Balance ?? 0m,
            State?.ActiveLoans.Sum(l => Math.Max(0m, l.Principal)) ?? 0m, LiquidityRulesConfig.RescueLoanAdvanceRate);

    // ── Post-tick: overdraft interest (before the balance mirror) ─────────────

    /// <summary>
    /// Interest on the day's negative closing cash at the overdraft rate
    /// (actual/365, rounded to øre), booked as interest expense.
    /// </summary>
    internal void ChargeOverdraftInterest()
    {
        if (FinanceManager == null || State == null || State.BankruptDay.HasValue) return;
        decimal cash = FinanceManager.Balance;
        if (cash >= 0m) return;
        decimal interest = decimal.Round(-cash * (decimal)LiquidityRulesConfig.OverdraftInterestRate / SimCalendar.DaysPerYear, 2);
        if (interest <= 0m) return;
        PayCost(interest, CostBooking.Interest, OverdraftInterestCategory);
        State.Liquidity.OverdraftInterestPaidNok += interest;
    }

    // ── Post-tick: the ladder (after the solvency check) ─────────────────────

    /// <summary>
    /// End-of-day liquidity check: record the closing cash, expire an
    /// unanswered offer, end an episode the cash recovered from, and raise the
    /// liquidity warning and the bank call (with its offer) on the way down.
    /// </summary>
    internal void CheckLiquidity()
    {
        if (FinanceManager == null || State == null) return;
        CloseStoryDay();
        if (State.BankruptDay.HasValue) return;

        var rules = LiquidityRulesConfig;
        var ladder = State.Liquidity;
        ladder.CashHistory.Add(FinanceManager.Balance);
        int keep = Math.Max(2, rules.BurnWindowDays + 1);
        if (ladder.CashHistory.Count > keep)
            ladder.CashHistory.RemoveRange(0, ladder.CashHistory.Count - keep);

        if (ladder.Offer is { Status: RescueOfferStatus.Open } open && State.Day > open.ExpiresDay)
            ResolveOffer(open, RescueOfferStatus.Expired, RescueExpiredKey, "Redningslånet utløp",
                SimText.Nb($"Banken trakk tilbudet om redningslån på {Nok(open.AmountNok)} NOK: det ble ikke besvart innen dag {open.ExpiresDay}."));

        var forecast = GetLiquidityForecast();
        if (ladder.EpisodeStartDay >= 0 && (!forecast.IsDeclining || forecast.RunwayDays > rules.ReArmRunwayDays))
        {
            EndLiquidityEpisode();
            return;
        }
        if (!forecast.IsDeclining) return;

        if (ladder.WarningDay < 0 && forecast.RunwayDays <= rules.WarningRunwayDays)
            RaiseLiquidityWarning(forecast);
        if (ladder.BankCallDay < 0 && forecast.RunwayDays <= rules.BankCallRunwayDays)
            RaiseBankCall(forecast);
    }

    private void RaiseLiquidityWarning(LiquidityForecast forecast)
    {
        var ladder = State.Liquidity;
        if (ladder.EpisodeStartDay < 0) ladder.EpisodeStartDay = State.Day;
        ladder.WarningDay = State.Day;
        ladder.WarningsIssued++;
        int days = (int)Math.Ceiling(forecast.RunwayDays);
        string description =
            SimText.Nb($"Likviditetsvarsel fra banken: kontantene faller med {Nok(forecast.BurnPerDayNok)} NOK om dagen. ") +
            SimText.Nb($"På dette forbruket når dere kredittrammen på {Nok(forecast.FloorNok)} NOK om {days} dager (dag {forecast.ProjectedFloorDay}). ") +
            SimText.Nb($"Fisken i sjøen er verdt {Nok(BiomassCollateralValueNok())} NOK: slakt, lån eller kutt kostnader.");
        LogLiquidity(LiquidityWarningKey, "Likviditetsvarsel", description);
        _dayNotes.Add(new DayNote
        {
            Kind = "liquidity_warning", EventType = LiquidityEpisodeVariant(), Message = description,
            Severity = forecast.ProjectedFloorDay, Value = forecast.RunwayDays
        });
        OnEventTriggered?.Invoke(description);
    }

    private void RaiseBankCall(LiquidityForecast forecast)
    {
        var rules = LiquidityRulesConfig;
        var ladder = State.Liquidity;
        if (ladder.EpisodeStartDay < 0) ladder.EpisodeStartDay = State.Day;
        if (ladder.WarningDay < 0) ladder.WarningDay = State.Day;   // a sudden fall: the call is the warning
        ladder.BankCallDay = State.Day;
        ladder.BankCallsIssued++;
        int days = (int)Math.Ceiling(forecast.RunwayDays);

        string offerText;
        decimal amount = 0m;
        int deadline = forecast.ProjectedFloorDay;
        if (ladder.RescueLoansTaken >= rules.MaxRescueLoans)
        {
            offerText = "Banken har allerede gitt dere et redningslån og gir ingen ny kreditt.";
        }
        else
        {
            amount = LiquidityRules.RescueAmount(forecast.BurnPerDayNok, RescueCollateralCapNok(), rules);
            if (amount <= 0m)
            {
                offerText = "Fisken i sjøen gir ikke sikkerhet nok for nye lån: banken tilbyr ingen redningskreditt.";
            }
            else
            {
                deadline = Math.Min(State.Day + rules.RescueOfferValidDays, Math.Max(State.Day, forecast.ProjectedFloorDay - 1));
                ladder.Offer = new RescueOffer
                {
                    OfferedDay = State.Day,
                    ExpiresDay = deadline,
                    AmountNok = amount,
                    InterestRate = rules.RescueLoanInterestRate,
                    ArrangementFee = rules.RescueLoanArrangementFee,
                    TermMonths = rules.RescueLoanTermMonths,
                    Status = RescueOfferStatus.Open,
                };
                offerText =
                    SimText.Nb($"Banken tilbyr et redningslån på {Nok(amount)} NOK til {rules.RescueLoanInterestRate * 100:0.#} % rente ") +
                    SimText.Nb($"og {rules.RescueLoanArrangementFee * 100:0.#} % gebyr, nedbetalt over {rules.RescueLoanTermMonths} måneder. Svar innen dag {deadline}.");
            }
        }
        string description =
            SimText.Nb($"Banken kaller inn til møte: på dagens forbruk ({Nok(forecast.BurnPerDayNok)} NOK om dagen) passerer dere kredittrammen på ") +
            SimText.Nb($"{Nok(forecast.FloorNok)} NOK om {days} dager. ") + offerText;
        LogLiquidity(BankCallKey, "Bankmøte", description);
        _dayNotes.Add(new DayNote
        {
            Kind = "bank_call", EventType = LiquidityEpisodeVariant(), Message = description,
            Severity = deadline, Value = (double)amount
        });
        OnEventTriggered?.Invoke(description);
    }

    /// <summary>The cash recovered (a harvest, a loan, a cut): both rungs re-arm and an open offer is withdrawn.</summary>
    private void EndLiquidityEpisode()
    {
        var ladder = State.Liquidity;
        if (ladder.Offer is { Status: RescueOfferStatus.Open } open)
            ResolveOffer(open, RescueOfferStatus.Withdrawn, "", "", "");
        Pacing.Resolve(LiquidityEventKey());
        ladder.EpisodeStartDay = -1;
        ladder.WarningDay = -1;
        ladder.BankCallDay = -1;
    }

    // ── Player verbs ──────────────────────────────────────────────────────────

    /// <summary>
    /// Take the bank's open rescue offer: the principal lands as liquidity and
    /// a loan (repaid daily over its term at its rate, outside the ordinary
    /// credit limit); the up-front fee is booked as interest. Refuses
    /// <c>no_rescue_offer</c>, <c>rescue_offer_expired</c> or <c>bankrupt</c>.
    /// Value = principal, Id = the loan id.
    /// </summary>
    public CommandResult AcceptRescueLoan()
    {
        if (State == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        if (State.BankruptDay.HasValue)
            return CommandResult.Fail("bankrupt", "The company is bankrupt");
        var offer = State.Liquidity.Offer;
        if (offer == null || offer.Status != RescueOfferStatus.Open)
            return CommandResult.Fail("no_rescue_offer", "The bank has no open rescue offer");
        if (State.Day > offer.ExpiresDay)
        {
            ResolveOffer(offer, RescueOfferStatus.Expired, RescueExpiredKey, "Redningslånet utløp",
                SimText.Nb($"Banken trakk tilbudet om redningslån på {Nok(offer.AmountNok)} NOK: det ble ikke besvart innen dag {offer.ExpiresDay}."));
            return CommandResult.Fail("rescue_offer_expired", $"The offer expired on day {offer.ExpiresDay}");
        }

        ReceiveLoan(offer.AmountNok);
        string prefix = $"{RescueLoanIdPrefix}{State.Day}_";
        int next = 1 + State.ActiveLoans
            .Select(l => l.Id.StartsWith(prefix, StringComparison.Ordinal)
                         && int.TryParse(l.Id.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : 0)
            .DefaultIfEmpty(0).Max();
        string loanId = prefix + next.ToString(CultureInfo.InvariantCulture);
        State.ActiveLoans.Add(new LoanState
        {
            Id = loanId,
            Principal = offer.AmountNok,
            OriginalPrincipal = offer.AmountNok,
            InterestRate = (decimal)offer.InterestRate,
            TermDays = SimCalendar.DaysInMonthsFrom(State.Day, Math.Max(1, offer.TermMonths)),
            DaysTaken = 0
        });
        PayCost(offer.FeeNok, CostBooking.Interest, RescueLoanFeeCategory);
        offer.LoanId = loanId;
        State.Liquidity.RescueLoansTaken++;
        string description =
            SimText.Nb($"Redningslån fra banken: {Nok(offer.AmountNok)} NOK til {offer.InterestRate * 100:0.#} % rente over {offer.TermMonths} måneder ") +
            SimText.Nb($"(gebyr {Nok(offer.FeeNok)} NOK).");
        ResolveOffer(offer, RescueOfferStatus.Accepted, RescueLoanTakenKey, "Redningslån", description);
        _dayNotes.Add(new DayNote { Kind = "rescue_loan", Message = description, Value = (double)offer.AmountNok, Success = true });
        // The loan ends the episode: the rungs re-arm for a later decline.
        State.Liquidity.EpisodeStartDay = -1;
        State.Liquidity.WarningDay = -1;
        State.Liquidity.BankCallDay = -1;
        return CommandResult.Success($"Rescue loan of {offer.AmountNok:N0} NOK taken", (double)offer.AmountNok, id: loanId);
    }

    /// <summary>Say no to the bank's open rescue offer (the episode goes on; no new offer in it).</summary>
    public CommandResult DeclineRescueLoan()
    {
        if (State == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        var offer = State.Liquidity.Offer;
        if (offer == null || offer.Status != RescueOfferStatus.Open)
            return CommandResult.Fail("no_rescue_offer", "The bank has no open rescue offer");
        ResolveOffer(offer, RescueOfferStatus.Declined, RescueDeclinedKey, "Redningslån avslått",
            SimText.Nb($"Dere takket nei til bankens redningslån på {Nok(offer.AmountNok)} NOK."));
        return CommandResult.Success("Rescue offer declined");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ResolveOffer(RescueOffer offer, RescueOfferStatus status, string logKey, string logName, string description)
    {
        offer.Status = status;
        offer.ResolvedDay = State.Day;
        Pacing.Resolve(LiquidityEventKey());
        if (logKey.Length > 0)
            LogLiquidity(logKey, logName, description);
    }

    private void LogLiquidity(string key, string name, string description)
    {
        State.EventLog.Add(new EventRecord
        {
            Day = State.Day,
            Id = key,
            Name = name,
            Category = EventCategory.Investor,
            Description = description,
            BalanceChange = 0m
        });
    }

    /// <summary>The episode's variant on the bus ("e" + its first day): each decline is its own news.</summary>
    private string LiquidityEpisodeVariant()
        => "e" + Math.Max(0, State.Liquidity.EpisodeStartDay).ToString(CultureInfo.InvariantCulture);

    /// <summary>The bus key (and pacing key) of the current episode's warning and bank call.</summary>
    internal string LiquidityEventKey()
        => GameEventBus.KeyOf(GameEventCatalog.Describe(GameEventType.BankCall).Category, "", LiquidityEpisodeVariant());

    /// <summary>The ladder's day notes as typed events (called from the note drain).</summary>
    private void PublishLiquidityEvent(DayNote note)
    {
        // A deadline is watched only while its episode still runs: a multi-day
        // advance can see the cash recover (and the episode resolve) before
        // its notes are drained, and a resolved deadline must not come back.
        bool live = State.Liquidity.EpisodeStartDay >= 0 && note.EventType == LiquidityEpisodeVariant();
        switch (note.Kind)
        {
            case "liquidity_warning":
                PublishGameEvent(new GameEventRequest(GameEventType.LiquidityWarning, note.Day)
                {
                    Variant = note.EventType,
                    DeadlineDay = live ? (int)note.Severity : -1,
                    Message = note.Message,
                    Value = note.Value
                });
                break;
            case "bank_call":
                bool offerOpen = State.Liquidity.Offer is { Status: RescueOfferStatus.Open };
                PublishGameEvent(new GameEventRequest(GameEventType.BankCall, note.Day)
                {
                    Variant = note.EventType,
                    DeadlineDay = live && (offerOpen || note.Value <= 0.0) ? (int)note.Severity : -1,
                    Message = note.Message,
                    Value = note.Value
                });
                break;
            case "rescue_loan":
                PublishGameEvent(new GameEventRequest(GameEventType.RescueLoanTaken, note.Day)
                {
                    Message = note.Message,
                    Value = note.Value
                });
                break;
        }
    }
}
