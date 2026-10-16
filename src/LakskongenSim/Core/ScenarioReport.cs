using System;
using System.Collections.Generic;
using System.Linq;

namespace LakskongenSim.Core;

/// <summary>How a game ended (or stands): the end screen's verdict (U2-03).</summary>
public enum ScenarioVerdict
{
    /// <summary>No verdict yet (a running game).</summary>
    Running = 0,
    /// <summary>The scenario's goal was met.</summary>
    Won = 1,
    /// <summary>The scenario was lost (its time ran out, a rival got there first).</summary>
    Lost = 2,
    /// <summary>Cash fell below the bankruptcy floor.</summary>
    Bankrupt = 3
}

/// <summary>A won scenario's medal (campaign progress, D16: medals, never cash).</summary>
public enum ScenarioMedal
{
    None = 0,
    Bronze = 1,
    Silver = 2,
    Gold = 3
}

/// <summary>One of the three marks a win is judged by (the medal counts them).</summary>
public sealed record ScenarioDistinction(string Id, bool Met, double Value, double Threshold);

/// <summary>A month of the P&amp;L story with its running result.</summary>
public sealed record ScenarioMonthPoint(
    int Month, int FirstDay, decimal RevenueNok, decimal CostsNok, decimal ResultNok,
    decimal CumulativeResultNok, decimal ClosingCashNok);

/// <summary>
/// A turning point: one run of one ledger line (the income and costs of one
/// category over a few days, e.g. a harvest's sales less its harvest cost),
/// as the ledger booked it. <see cref="Income"/> is true for a net income.
/// </summary>
public sealed record ScenarioTurningPoint(
    string Category, bool Income, int FirstDay, int LastDay, decimal AmountNok, int Bookings,
    decimal RevenueNok, decimal CostsNok);

/// <summary>One ledger line over the whole game (income or cost of one category).</summary>
public sealed record ScenarioLedgerLine(string Category, bool Income, decimal AmountNok);

/// <summary>Everything the end screen shows, read from the engine's own books (U2-03).</summary>
public sealed record ScenarioEndReport
{
    public string ScenarioId { get; init; } = "";
    public string ScenarioName { get; init; } = "";
    public ScenarioVerdict Verdict { get; init; }
    /// <summary>The scenario's own loss text (Norwegian), "" unless lost.</summary>
    public string LossReason { get; init; } = "";
    public int StartDay { get; init; }
    public int EndDay { get; init; }
    public int DaysPlayed => Math.Max(0, EndDay - StartDay);
    public decimal OpeningCashNok { get; init; }
    public decimal ClosingCashNok { get; init; }
    public decimal LowestCashNok { get; init; }
    public int LowestCashDay { get; init; }
    public decimal BankruptcyFloorNok { get; init; }
    public decimal LoanPrincipalNok { get; init; }
    /// <summary>Income booked since the books opened.</summary>
    public decimal RevenueNok { get; init; }
    /// <summary>Costs booked since the books opened (positive).</summary>
    public decimal CostsNok { get; init; }
    /// <summary>Result: income less costs (the story's lifetime total).</summary>
    public decimal NetResultNok => RevenueNok - CostsNok;
    public double HarvestedTonnesHog { get; init; }
    public int HarvestBatches { get; init; }
    public long FishDied { get; init; }
    public long FishLeftAlive { get; init; }
    public long FishInSea { get; init; }
    /// <summary>Share of the fish the company had that died (0-1).</summary>
    public double MortalityShare { get; init; }
    public int LiquidityWarnings { get; init; }
    public int BankCalls { get; init; }
    public int RescueLoans { get; init; }
    public decimal OverdraftInterestNok { get; init; }
    public decimal FinesNok { get; init; }
    public IReadOnlyList<ScenarioMonthPoint> Months { get; init; } = Array.Empty<ScenarioMonthPoint>();
    public IReadOnlyList<ScenarioTurningPoint> TurningPoints { get; init; } = Array.Empty<ScenarioTurningPoint>();
    /// <summary>The largest ledger lines over the game, largest first.</summary>
    public IReadOnlyList<ScenarioLedgerLine> Lines { get; init; } = Array.Empty<ScenarioLedgerLine>();
    public IReadOnlyList<ScenarioDistinction> Distinctions { get; init; } = Array.Empty<ScenarioDistinction>();
    public ScenarioMedal Medal { get; init; }
}

/// <summary>
/// The medal rule (pure): a win earns bronze; silver with two of the three
/// distinctions, gold with all three. A game that was not won earns none.
/// </summary>
public static class ScenarioMedals
{
    /// <summary>Distinction ids in display order.</summary>
    public const string Finance = "finance";
    public const string Welfare = "welfare";
    public const string Compliance = "compliance";

    /// <summary>Ledger lines that are a regulator's sanction (the compliance mark).</summary>
    public static readonly IReadOnlyList<string> SanctionCategories = new[] { "fines", "mtb_fee", "mtb_confiscation" };

    public static ScenarioMedal Award(ScenarioVerdict verdict, IReadOnlyCollection<ScenarioDistinction> distinctions)
    {
        if (verdict != ScenarioVerdict.Won) return ScenarioMedal.None;
        int met = distinctions?.Count(d => d.Met) ?? 0;
        return met >= 3 ? ScenarioMedal.Gold : met == 2 ? ScenarioMedal.Silver : ScenarioMedal.Bronze;
    }

    /// <summary>Stable lower-case key ("none" .. "gold") for the bridge and the campaign file.</summary>
    public static string Key(ScenarioMedal medal) => medal.ToString().ToLowerInvariant();
}
