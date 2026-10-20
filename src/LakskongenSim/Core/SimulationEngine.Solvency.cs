using System;
using System.Collections.Generic;
using System.Globalization;
using LakskongenSim.Events;

namespace LakskongenSim.Core;

/// <summary>
/// Bankruptcy floor and the warning ladder before it (owner decision D16,
/// S1-03). The floor used to live in two places that disagreed: the Godot
/// bridge ended every game at -1M NOK cash, the scenario check at -2M, and
/// neither warned first. A harvest-only player on po3_red_sone, isa_utbrudd
/// or algekatastrofe crossed -1M about 85 days before the first harvest, so
/// the shipped scenarios could not be played to a sale. Now one engine rule
/// owns the verdict: each scenario has its floor in BalancingConfig (the
/// calibration registry), cash below a rung of the ladder warns once, and
/// cash below the floor declares bankruptcy, which the scenario records as
/// a loss and the bridge turns into the end of the game.
/// </summary>
public partial class SimulationEngine
{
    /// <summary>Warning-ladder keys, rung 1..3 (event-log ids and UI keys).</summary>
    public static readonly IReadOnlyList<string> SolvencyWarningKeys = new[]
    {
        "solvency_overdraft", "solvency_bank_warning", "solvency_final_warning"
    };

    /// <summary>Event-log id and game-over reason key of a bankruptcy.</summary>
    public const string BankruptcyKey = "solvency_bankrupt";

    /// <summary>Translation key the bridge ends the game with on bankruptcy.</summary>
    public const string BankruptcyReasonKey = "GAME_OVER_BANKRUPTCY";

    /// <summary>The bankruptcy floor (NOK, negative) of the active scenario.</summary>
    public decimal BankruptcyFloorNok => BankruptcyFloorFor(Scenario?.ActiveScenario?.Id);

    /// <summary>The bankruptcy floor a scenario runs with (the default when it has none).</summary>
    public decimal BankruptcyFloorFor(string? scenarioId)
        => scenarioId != null && _balancing.BankruptcyFloorByScenarioNOK.TryGetValue(scenarioId, out var floor)
            ? floor
            : _balancing.BankruptcyFloorNOK;

    /// <summary>Cash (NOK) below which warning rung <paramref name="level"/> (1..3) applies.</summary>
    public decimal SolvencyWarningThresholdNok(int level)
    {
        if (level < 1 || level > _balancing.SolvencyWarningFractions.Length)
            throw new ArgumentOutOfRangeException(nameof(level), level, "warning rungs are 1..3");
        return BankruptcyFloorNok * (decimal)_balancing.SolvencyWarningFractions[level - 1];
    }

    /// <summary>The warning rung today's cash sits on: 0 (solvent) to 3; bankruptcy is not a rung.</summary>
    public int SolvencyWarningLevel => FinanceManager == null ? 0 : WarningLevelFor(FinanceManager.Balance);

    /// <summary>True once cash fell below the floor (final: the game is over).</summary>
    public bool IsBankrupt => State.BankruptDay.HasValue;

    private int WarningLevelFor(decimal cash)
    {
        int level = 0;
        for (int i = 1; i <= _balancing.SolvencyWarningFractions.Length; i++)
            if (cash < SolvencyWarningThresholdNok(i))
                level = i;
        return level;
    }

    /// <summary>
    /// End-of-day solvency check (post-tick, after every cash flow of the
    /// day). Each rung warns once on the way down; the ladder re-arms when
    /// cash is back at zero or above. Below the floor the company is
    /// bankrupt: logged, noted for the bridge and lost for the scenario.
    /// </summary>
    internal void CheckSolvency()
    {
        if (FinanceManager == null || State.BankruptDay.HasValue) return;
        decimal cash = FinanceManager.Balance;
        decimal floor = BankruptcyFloorNok;

        if (cash < floor)
        {
            DeclareBankruptcy(cash, floor);
            return;
        }
        if (cash >= 0m)
        {
            State.SolvencyWarningPeak = 0;
            return;
        }

        int level = WarningLevelFor(cash);
        if (level <= State.SolvencyWarningPeak) return;
        State.SolvencyWarningPeak = level;

        string key = SolvencyWarningKeys[level - 1];
        string description = level switch
        {
            1 => $"Kontoen er overtrukket: {Nok(cash)} NOK. Banken stenger ved {Nok(floor)} NOK.",
            2 => $"Banken varsler: kontantbeholdningen er {Nok(cash)} NOK, halvveis til konkursgrensen på {Nok(floor)} NOK. Skaff kapital eller kutt kostnader.",
            _ => $"Siste varsel fra banken: {Nok(cash)} NOK. Under {Nok(floor)} NOK begjæres selskapet konkurs."
        };
        State.EventLog.Add(new EventRecord
        {
            Day = State.Day,
            Id = key,
            Name = level switch { 1 => "Overtrukket konto", 2 => "Bankvarsel", _ => "Siste varsel" },
            Category = EventCategory.Investor,
            Description = description,
            BalanceChange = 0m
        });
        _dayNotes.Add(new DayNote { Kind = "solvency_warning", Message = key, Severity = level, Value = (double)cash });
        OnEventTriggered?.Invoke(description);
    }

    private void DeclareBankruptcy(decimal cash, decimal floor)
    {
        State.BankruptDay = State.Day;
        string description = $"Konkurs: kontantbeholdningen ({Nok(cash)} NOK) passerte konkursgrensen på {Nok(floor)} NOK.";
        State.EventLog.Add(new EventRecord
        {
            Day = State.Day,
            Id = BankruptcyKey,
            Name = "Konkurs",
            Category = EventCategory.Investor,
            Description = description,
            BalanceChange = 0m
        });
        _dayNotes.Add(new DayNote { Kind = "bankruptcy", Message = BankruptcyKey, Severity = 1.0, Value = (double)cash });
        Scenario?.DeclareBankruptcy(description);
        SyncScenarioToState();
        OnEventTriggered?.Invoke(description);
    }

    /// <summary>NOK with Norwegian digit grouping (deterministic, culture-fixed).</summary>
    private static string Nok(decimal amount)
        => amount.ToString("#,0", NorwegianNumbers);

    private static readonly NumberFormatInfo NorwegianNumbers = new()
    {
        NumberGroupSeparator = " ",
        NegativeSign = "-",
    };
}
