using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Events;

namespace LakskongenSim.Core;

/// <summary>One unit's warnings and criticals in one sim month.</summary>
public sealed record NotificationMonth(string UnitId, int Month, int Warnings, int Criticals,
    IReadOnlyDictionary<string, int> ByType)
{
    /// <summary>The month's calendar year (2027 = the first).</summary>
    public int Year => SimCalendar.StartYear + Month / SimCalendar.MonthsPerYear;

    /// <summary>The month of the year, 1-12.</summary>
    public int MonthOfYear => Month % SimCalendar.MonthsPerYear + 1;
}

/// <summary>A unit over the notification budget in a month (warnings) or a year (criticals).</summary>
public sealed record NotificationBudgetBreach(string UnitId, GameEventSeverity Severity, int Period, int Count, int Budget);

/// <summary>
/// The notification meter (roadmap F2-05, docs/FARM_TYPES_SPEC.md §6.6
/// "Notification budget", measured in F2-17): how many warnings and criticals
/// the player was told about, per unit and sim month. It counts what the bus
/// delivered (a new event or an escalation), never a repeat the bus absorbed,
/// and never information. A unit is the event's <see cref="GameEvent.UnitId"/>,
/// else its site; company-wide and zone events count under "".
/// <para>
/// Presentation state like the bus it listens to: no stream, no game state,
/// not saved (it is not in <see cref="GameState"/>, so it never enters the
/// save hash) and emptied with the bus (a new or loaded game).
/// </para>
/// </summary>
public sealed class NotificationMeter
{
    /// <summary>A policy-competent player sees at most this many warnings per unit per in-game month (spec §6.6).</summary>
    public const int WarningBudgetPerUnitMonth = 2;

    /// <summary>... and at most this many criticals per unit per in-game year (spec §6.6).</summary>
    public const int CriticalBudgetPerUnitYear = 1;

    /// <summary>The unit key of company-wide and zone events.</summary>
    public const string Company = "";

    private sealed class Bucket
    {
        public int Warnings;
        public int Criticals;
        public readonly SortedDictionary<string, int> ByType = new(StringComparer.Ordinal);
    }

    private readonly SortedDictionary<(string Unit, int Month), Bucket> _buckets = new(BucketOrder.Instance);

    /// <summary>Warnings and criticals counted since the meter was last cleared.</summary>
    public int Total { get; private set; }

    /// <summary>The unit an event counts under: its unit, else its site, else <see cref="Company"/>.</summary>
    public static string UnitOf(GameEvent ev) =>
        ev.UnitId.Length > 0 ? ev.UnitId : ev.SiteId.Length > 0 ? ev.SiteId : Company;

    /// <summary>The meter's month of a sim day (months since 1 January 2027).</summary>
    public static int MonthOf(int day) => SimCalendar.MonthsSinceEpoch(day);

    /// <summary>Count an event the bus delivered (information is not counted).</summary>
    public void Observe(GameEvent ev)
    {
        ArgumentNullException.ThrowIfNull(ev);
        if (ev.Severity < GameEventSeverity.Warning) return;
        var key = (UnitOf(ev), MonthOf(ev.Day));
        if (!_buckets.TryGetValue(key, out var bucket))
            _buckets[key] = bucket = new Bucket();
        if (ev.Severity == GameEventSeverity.Critical)
            bucket.Criticals++;
        else
            bucket.Warnings++;
        string type = GameEventCatalog.Describe(ev.Type).Id;
        bucket.ByType[type] = bucket.ByType.TryGetValue(type, out int n) ? n + 1 : 1;
        Total++;
    }

    /// <summary>Warnings counted for <paramref name="unitId"/> in <paramref name="month"/>.</summary>
    public int Warnings(string unitId, int month)
        => _buckets.TryGetValue((unitId ?? Company, month), out var b) ? b.Warnings : 0;

    /// <summary>Criticals counted for <paramref name="unitId"/> in <paramref name="month"/>.</summary>
    public int Criticals(string unitId, int month)
        => _buckets.TryGetValue((unitId ?? Company, month), out var b) ? b.Criticals : 0;

    /// <summary>Criticals counted for <paramref name="unitId"/> in the sim year <paramref name="yearIndex"/> (0 = 2027).</summary>
    public int CriticalsInYear(string unitId, int yearIndex)
        => _buckets.Where(kv => kv.Key.Unit == (unitId ?? Company) && kv.Key.Month / SimCalendar.MonthsPerYear == yearIndex)
            .Sum(kv => kv.Value.Criticals);

    /// <summary>Every unit-month with a warning or a critical, by unit then month.</summary>
    public IReadOnlyList<NotificationMonth> Months =>
        _buckets.Select(kv => new NotificationMonth(kv.Key.Unit, kv.Key.Month, kv.Value.Warnings, kv.Value.Criticals,
            new SortedDictionary<string, int>(kv.Value.ByType, StringComparer.Ordinal))).ToList();

    /// <summary>
    /// The units over the budget: a month with more than
    /// <see cref="WarningBudgetPerUnitMonth"/> warnings (period = the month)
    /// or a year with more than <see cref="CriticalBudgetPerUnitYear"/>
    /// criticals (period = the year index). Company-wide events are not a
    /// unit's and are left out.
    /// </summary>
    public IReadOnlyList<NotificationBudgetBreach> OverBudget()
    {
        var breaches = new List<NotificationBudgetBreach>();
        foreach (var kv in _buckets)
        {
            if (kv.Key.Unit == Company) continue;
            if (kv.Value.Warnings > WarningBudgetPerUnitMonth)
                breaches.Add(new NotificationBudgetBreach(kv.Key.Unit, GameEventSeverity.Warning, kv.Key.Month,
                    kv.Value.Warnings, WarningBudgetPerUnitMonth));
        }
        foreach (var group in _buckets.Where(kv => kv.Key.Unit != Company)
                     .GroupBy(kv => (kv.Key.Unit, Year: kv.Key.Month / SimCalendar.MonthsPerYear))
                     .OrderBy(g => g.Key.Unit, StringComparer.Ordinal).ThenBy(g => g.Key.Year))
        {
            int criticals = group.Sum(kv => kv.Value.Criticals);
            if (criticals > CriticalBudgetPerUnitYear)
                breaches.Add(new NotificationBudgetBreach(group.Key.Unit, GameEventSeverity.Critical, group.Key.Year,
                    criticals, CriticalBudgetPerUnitYear));
        }
        return breaches;
    }

    /// <summary>Forget everything (a new or loaded game).</summary>
    public void Clear()
    {
        _buckets.Clear();
        Total = 0;
    }

    private sealed class BucketOrder : IComparer<(string Unit, int Month)>
    {
        public static readonly BucketOrder Instance = new();

        public int Compare((string Unit, int Month) a, (string Unit, int Month) b)
        {
            int byUnit = string.CompareOrdinal(a.Unit, b.Unit);
            return byUnit != 0 ? byUnit : a.Month.CompareTo(b.Month);
        }
    }
}
