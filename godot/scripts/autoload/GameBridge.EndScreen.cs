using Godot;
using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Finance;

// U2-03 — the end screen, the liquidity ladder and the rescue loan.
// Thin Godot-facing projection: the engine owns the verdict, the report (its
// P&L story is the ledger's own bookings) and the bank's ladder; this partial
// maps them onto Godot dictionaries and forwards the two rescue verbs.
public partial class GameBridge : Node
{
    /// <summary>Event-log entries the end screen lists (newest first).</summary>
    private const int EndScreenLogEntries = 60;

    /// <summary>The verdict as it stands: "running", "won", "lost" or "bankrupt".</summary>
    public string CurrentVerdict => VerdictKey(SimulationEngine?.CurrentVerdict() ?? ScenarioVerdict.Running);

    /// <summary>
    /// Everything the end screen shows (U2-03), read from the engine's books.
    /// Keys: success, scenario_id, scenario_name, verdict, loss_reason,
    /// start_day, end_day, days_played, opening_cash, closing_cash,
    /// lowest_cash, lowest_cash_day, bankruptcy_floor, loan_principal,
    /// revenue, costs, net_result, harvested_tonnes_hog, harvest_batches,
    /// fish_died, fish_left_alive, fish_in_sea, mortality_share,
    /// liquidity_warnings, bank_calls, rescue_loans, rescue_loan_day (-1 = none),
    /// rescue_loan, overdraft_interest, fines,
    /// medal ("none" | "bronze" | "silver" | "gold"),
    /// months [{month, first_day, revenue, costs, result, cumulative, closing_cash}],
    /// turning_points [{category, income, first_day, last_day, amount, revenue,
    /// costs, bookings}] (a run's income and costs in one ledger line),
    /// lines [{category, income, amount}] (the largest ledger lines),
    /// distinctions [{id, met, value, threshold}],
    /// log [{day, id, name, category, description, balance_change}] (the
    /// game's event log since the start, newest first).
    /// </summary>
    public Godot.Collections.Dictionary GetScenarioEndReport()
    {
        var result = new Godot.Collections.Dictionary();
        var engine = SimulationEngine;
        if (engine?.State == null)
        {
            result["success"] = false;
            result["verdict"] = "running";
            return result;
        }
        var r = engine.GetScenarioEndReport();
        result["success"] = true;
        result["scenario_id"] = r.ScenarioId;
        result["scenario_name"] = r.ScenarioName;
        result["verdict"] = VerdictKey(r.Verdict);
        result["loss_reason"] = r.LossReason;
        result["start_day"] = r.StartDay;
        result["end_day"] = r.EndDay;
        result["days_played"] = r.DaysPlayed;
        result["opening_cash"] = (double)r.OpeningCashNok;
        result["closing_cash"] = (double)r.ClosingCashNok;
        result["lowest_cash"] = (double)r.LowestCashNok;
        result["lowest_cash_day"] = r.LowestCashDay;
        result["bankruptcy_floor"] = (double)r.BankruptcyFloorNok;
        result["loan_principal"] = (double)r.LoanPrincipalNok;
        result["revenue"] = (double)r.RevenueNok;
        result["costs"] = (double)r.CostsNok;
        result["net_result"] = (double)r.NetResultNok;
        result["harvested_tonnes_hog"] = r.HarvestedTonnesHog;
        result["harvest_batches"] = r.HarvestBatches;
        result["fish_died"] = r.FishDied;
        result["fish_left_alive"] = r.FishLeftAlive;
        result["fish_in_sea"] = r.FishInSea;
        result["mortality_share"] = r.MortalityShare;
        result["liquidity_warnings"] = r.LiquidityWarnings;
        result["bank_calls"] = r.BankCalls;
        result["rescue_loans"] = r.RescueLoans;
        result["rescue_loan_day"] = r.RescueLoanDay;
        result["rescue_loan"] = (double)r.RescueLoanNok;
        result["overdraft_interest"] = (double)r.OverdraftInterestNok;
        result["fines"] = (double)r.FinesNok;
        result["medal"] = ScenarioMedals.Key(r.Medal);

        var months = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var m in r.Months)
            months.Add(new Godot.Collections.Dictionary
            {
                ["month"] = m.Month,
                ["first_day"] = m.FirstDay,
                ["revenue"] = (double)m.RevenueNok,
                ["costs"] = (double)m.CostsNok,
                ["result"] = (double)m.ResultNok,
                ["cumulative"] = (double)m.CumulativeResultNok,
                ["closing_cash"] = (double)m.ClosingCashNok,
            });
        result["months"] = months;

        var points = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var t in r.TurningPoints)
            points.Add(new Godot.Collections.Dictionary
            {
                ["category"] = t.Category,
                ["income"] = t.Income,
                ["first_day"] = t.FirstDay,
                ["last_day"] = t.LastDay,
                ["amount"] = (double)t.AmountNok,
                ["revenue"] = (double)t.RevenueNok,
                ["costs"] = (double)t.CostsNok,
                ["bookings"] = t.Bookings,
            });
        result["turning_points"] = points;

        var lines = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var l in r.Lines)
            lines.Add(new Godot.Collections.Dictionary
            {
                ["category"] = l.Category,
                ["income"] = l.Income,
                ["amount"] = (double)l.AmountNok,
            });
        result["lines"] = lines;

        var marks = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var d in r.Distinctions)
            marks.Add(new Godot.Collections.Dictionary
            {
                ["id"] = d.Id,
                ["met"] = d.Met,
                ["value"] = d.Value,
                ["threshold"] = d.Threshold,
            });
        result["distinctions"] = marks;

        var log = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var evt in engine.State.EventLog
                     .Where(e => e.Day >= r.StartDay)
                     .Reverse()
                     .Take(EndScreenLogEntries))
            log.Add(new Godot.Collections.Dictionary
            {
                ["day"] = evt.Day,
                ["id"] = evt.Id ?? "",
                ["name"] = _tr_or("EVENT_" + UpperSnake(evt.Name), evt.Name),
                ["category"] = evt.Category.ToString(),
                ["description"] = evt.Description,
                ["balance_change"] = (double)evt.BalanceChange,
            });
        result["log"] = log;
        return result;
    }

    /// <summary>
    /// The bank's reading of the cash and its rescue offer (U2-03). Keys:
    /// success, cash, floor, burn_per_day, runway_days (-1 = cash not falling),
    /// projected_floor_day, history_days, declining, stage ("none" |
    /// "warning" | "bank_call"), warning_day, bank_call_day,
    /// overdraft_rate, overdraft_interest_paid, rescue_loans_taken,
    /// collateral_value, collateral_cap, offer (empty when the bank has made
    /// none: {status "open" | "accepted" | "declined" | "expired" |
    /// "withdrawn", amount, interest_rate, fee_rate, fee, term_months,
    /// offered_day, expires_day, days_left, loan_id}).
    /// </summary>
    public Godot.Collections.Dictionary GetLiquidityStatus()
    {
        var result = new Godot.Collections.Dictionary();
        var engine = SimulationEngine;
        if (engine?.State == null)
        {
            result["success"] = false;
            result["offer"] = new Godot.Collections.Dictionary();
            return result;
        }
        var ladder = engine.State.Liquidity;
        var forecast = engine.GetLiquidityForecast();
        var rules = engine.LiquidityRulesConfig;
        result["success"] = true;
        result["cash"] = (double)forecast.CashNok;
        result["floor"] = (double)forecast.FloorNok;
        result["burn_per_day"] = (double)forecast.BurnPerDayNok;
        result["runway_days"] = forecast.IsDeclining ? forecast.RunwayDays : -1.0;
        result["projected_floor_day"] = forecast.ProjectedFloorDay;
        result["history_days"] = forecast.HistoryDays;
        result["declining"] = forecast.IsDeclining;
        result["stage"] = ladder.BankCallDay >= 0 ? "bank_call" : ladder.WarningDay >= 0 ? "warning" : "none";
        result["warning_day"] = ladder.WarningDay;
        result["bank_call_day"] = ladder.BankCallDay;
        result["overdraft_rate"] = rules.OverdraftInterestRate;
        result["overdraft_interest_paid"] = (double)ladder.OverdraftInterestPaidNok;
        result["rescue_loans_taken"] = ladder.RescueLoansTaken;
        result["collateral_value"] = (double)engine.BiomassCollateralValueNok();
        result["collateral_cap"] = (double)engine.RescueCollateralCapNok();
        result["offer"] = OfferDictionary(ladder.Offer, engine.State.Day);
        return result;
    }

    /// <summary>Take the bank's open rescue offer: {success, error_key, message, amount, loan_id}.</summary>
    public Godot.Collections.Dictionary AcceptRescueLoan()
    {
        var result = new Godot.Collections.Dictionary();
        if (SimulationEngine?.State == null)
        {
            result["success"] = false;
            result["error_key"] = "engine_not_ready";
            result["message"] = "Not initialized";
            return result;
        }
        var outcome = SimulationEngine.AcceptRescueLoan();
        result["success"] = outcome.Ok;
        result["error_key"] = outcome.ErrorKey ?? "";
        result["message"] = outcome.Message;
        result["amount"] = outcome.Ok ? outcome.Value : 0.0;
        result["loan_id"] = outcome.Ok ? outcome.Id ?? "" : "";
        if (outcome.Ok)
        {
            ProjectDayNotes();
            EmitSignal(SignalName.GameStateUpdated);
        }
        return result;
    }

    /// <summary>Say no to the bank's open rescue offer: {success, error_key, message}.</summary>
    public Godot.Collections.Dictionary DeclineRescueLoan()
    {
        var result = new Godot.Collections.Dictionary();
        if (SimulationEngine?.State == null)
        {
            result["success"] = false;
            result["error_key"] = "engine_not_ready";
            result["message"] = "Not initialized";
            return result;
        }
        var outcome = SimulationEngine.DeclineRescueLoan();
        result["success"] = outcome.Ok;
        result["error_key"] = outcome.ErrorKey ?? "";
        result["message"] = outcome.Message;
        if (outcome.Ok)
            EmitSignal(SignalName.GameStateUpdated);
        return result;
    }

    private static Godot.Collections.Dictionary OfferDictionary(RescueOffer? offer, int today)
    {
        if (offer == null) return new Godot.Collections.Dictionary();
        return new Godot.Collections.Dictionary
        {
            ["status"] = offer.Status.ToString().ToLowerInvariant(),
            ["amount"] = (double)offer.AmountNok,
            ["interest_rate"] = offer.InterestRate,
            ["fee_rate"] = offer.ArrangementFee,
            ["fee"] = (double)offer.FeeNok,
            ["term_months"] = offer.TermMonths,
            ["offered_day"] = offer.OfferedDay,
            ["expires_day"] = offer.ExpiresDay,
            ["days_left"] = offer.Status == RescueOfferStatus.Open ? System.Math.Max(0, offer.ExpiresDay - today) : 0,
            ["loan_id"] = offer.LoanId,
        };
    }

    private static string VerdictKey(ScenarioVerdict verdict) => verdict switch
    {
        ScenarioVerdict.Won => "won",
        ScenarioVerdict.Lost => "lost",
        ScenarioVerdict.Bankrupt => "bankrupt",
        _ => "running"
    };
}
