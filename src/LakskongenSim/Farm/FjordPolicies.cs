using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Core;

namespace LakskongenSim.Farm;

/// <summary>
/// sim·core's Driftsplan policies for fjord localities (roadmap F2-05,
/// docs/FARM_TYPES_SPEC.md §1 pillar 5 "policy first, exceptions second"):
/// the periodic net care and a standing delousing order on the weekly count.
/// Neither is on by default (a fjord game plays as before until the player
/// sets one); the farm-type lanes add their units' defaults from their own
/// files (<see cref="DriftsplanPolicies"/>).
/// </summary>
public static class FjordPolicies
{
    /// <summary>Kind of <see cref="NetCleaningPolicy"/>.</summary>
    public const string NetCleaning = "net_cleaning";

    /// <summary>Kind of <see cref="DelouseOnCountPolicy"/>.</summary>
    public const string DelouseOnCount = "delouse_on_count";

    /// <summary>Trigger of a job that waits for the site to be stocked.</summary>
    public const string TriggerSiteStocked = "site_stocked";

    /// <summary>Trigger of a job that waits for a lice count above the order's level.</summary>
    public const string TriggerLiceCountAbove = "lice_count_above";

    internal static void AddTo(List<IDriftsplanPolicy> policies)
    {
        policies.Add(new NetCleaningPolicy());
        policies.Add(new DelouseOnCountPolicy());
    }

    /// <summary>The fjord locality with <paramref name="siteId"/>, or null.</summary>
    internal static SeaSite? FjordSite(SimulationEngine engine, string siteId)
        => engine.State?.SeaSites?.FirstOrDefault(s => s.Id == siteId && s.Kind == SiteKind.Fjord);

    /// <summary>Fish at the site now.</summary>
    internal static int FishAt(SeaSite site) => site.Pens.Sum(p => p.TotalCount);

    internal static DriftsplanJob WaitingForStock(string policy, string unitId, string verb, string command) => new()
    {
        Policy = policy,
        UnitId = unitId,
        Day = -1,
        Verb = verb,
        Command = command,
        Trigger = TriggerSiteStocked,
    };
}

/// <summary>
/// "Notpleie": book the ROV wellboat (in-situ net cleaning and inspection, the
/// player's <see cref="SimulationEngine.RequestWellboat"/> "rov" job) every
/// <c>interval_days</c> while the site holds fish. The first job is due on the
/// day the order is set; a job already booked for the site (by the player or
/// the order) defers the next one until it has run.
/// </summary>
public sealed class NetCleaningPolicy : IDriftsplanPolicy
{
    /// <summary>
    /// Days between two net cleanings: 14 (7-60). Elsheshtawy et al. 2023
    /// (Frontiers in Aquaculture 2:1125595, "Net cleaning impacts Atlantic
    /// salmon gill health through microbiome dysbiosis"): the commercial farms
    /// in western Norway they followed cleaned their nets every second week
    /// from May to November 2019, and at high-fouling sites cleaning "may be
    /// performed every two weeks or more" (Bloecher and Floerl 2021).
    /// </summary>
    public static readonly DriftsplanSetting IntervalDays = new()
    {
        Key = "interval_days",
        Min = 7,
        Max = 60,
        Default = 14,
        WholeNumber = true,
        Unit = "days",
        Source = "Elsheshtawy et al. 2023, Frontiers in Aquaculture 2:1125595: western Norwegian farms cleaned nets every second week, May-November 2019",
    };

    private static readonly IReadOnlyList<DriftsplanSetting> AllSettings = new[] { IntervalDays };
    private static readonly IReadOnlyList<DriftsplanUnitKind> Kinds = new[] { DriftsplanUnitKind.FjordSite };

    public string Kind => FjordPolicies.NetCleaning;
    public string Title => "notpleie (ROV og notvask)";
    public IReadOnlyList<DriftsplanUnitKind> AppliesTo => Kinds;
    public IReadOnlyList<DriftsplanSetting> Settings => AllSettings;
    public bool IsDefaultFor(DriftsplanUnitKind kind) => false;

    public DriftsplanJob? Plan(DriftsplanContext context)
    {
        var site = FjordPolicies.FjordSite(context.Engine, context.UnitId);
        if (site == null) return null;
        const string verb = "request_wellboat";
        string command = $"{verb} {site.Id} rov";
        if (FjordPolicies.FishAt(site) <= 0)
            return FjordPolicies.WaitingForStock(Kind, site.Id, verb, command);

        int interval = (int)Math.Round(context.Number(IntervalDays.Key));
        var entry = context.Entry;
        int due = entry.LastIssuedDay < 0 ? entry.SinceDay : entry.LastIssuedDay + interval;
        var booked = context.Engine.State.WellboatJobs.FirstOrDefault(j => j.SiteId == site.Id && j.Kind == "rov");
        if (booked != null)
            due = Math.Max(due, Math.Max(booked.DueDay, context.Today + 1));
        string siteId = site.Id;
        return new DriftsplanJob
        {
            Policy = Kind,
            UnitId = siteId,
            Day = due,
            Verb = verb,
            Command = command,
            Issue = engine => engine.RequestWellboat(siteId, "rov"),
        };
    }
}

/// <summary>
/// "Avlus ved telling": when the site's weekly lice count is above
/// <c>share_of_limit</c> of today's legal limit (0.5 adult females per fish,
/// 0.2 in the area's spring weeks; lakselusforskriften § 8), delouse the site
/// with <c>method</c> (the player's <see cref="SimulationEngine.DelouseSite"/>),
/// at most once per count and at most every <c>min_interval_days</c>. The order
/// reads the count the player reads (the last § 6 count), never the model's
/// hidden lice.
/// </summary>
public sealed class DelouseOnCountPolicy : IDriftsplanPolicy
{
    /// <summary>
    /// Share of the legal limit that triggers a treatment: 0.8 (0.5-1.0). GB:
    /// acting on the first count over 0.8 of the limit treats a week before a
    /// second count over the limit would bring Mattilsynet's order (two counts
    /// in a row, the game's rule); 0.8 x 0.5 = 0.4 is the golden player's
    /// level (F2-01). Counts are weekly at 4 °C and above (lakselusforskriften § 6).
    /// </summary>
    public static readonly DriftsplanSetting ShareOfLimit = new()
    {
        Key = "share_of_limit",
        Min = 0.5,
        Max = 1.0,
        Default = 0.8,
        Unit = "share",
        Source = "GB: treat on the first weekly count over 0.8 of the lakselusforskriften § 8 limit, a week before a second count over it brings Mattilsynet's order; 0.8 x 0.5 = the golden player's 0.4 (F2-01)",
    };

    /// <summary>
    /// Fewest days between two treatments: 14 (7-60). GB: the golden player's
    /// interval (F2-01); a weak treatment is not repeated before the next two
    /// counts, and every handling stresses the fish.
    /// </summary>
    public static readonly DriftsplanSetting MinIntervalDays = new()
    {
        Key = "min_interval_days",
        Min = 7,
        Max = 60,
        Default = 14,
        WholeNumber = true,
        Unit = "days",
        Source = "GB: the golden player's delousing interval (F2-01); every handling stresses the fish",
    };

    /// <summary>The treatment: mechanical (hydrolicer, the golden player's) by default; it works in any water.</summary>
    public static readonly DriftsplanSetting Method = new()
    {
        Key = "method",
        Kind = DriftsplanSettingKind.Choice,
        Choices = new[] { "hydrolicer", "thermolicer", "freshwater", "peroxide" },
        DefaultChoice = "hydrolicer",
        Source = "GB: the golden player's method (F2-01); mechanical delousing works at any sea temperature",
    };

    private static readonly IReadOnlyList<DriftsplanSetting> AllSettings = new[] { ShareOfLimit, MinIntervalDays, Method };
    private static readonly IReadOnlyList<DriftsplanUnitKind> Kinds = new[] { DriftsplanUnitKind.FjordSite };

    public string Kind => FjordPolicies.DelouseOnCount;
    public string Title => "avlusing ved telling";
    public IReadOnlyList<DriftsplanUnitKind> AppliesTo => Kinds;
    public IReadOnlyList<DriftsplanSetting> Settings => AllSettings;
    public bool IsDefaultFor(DriftsplanUnitKind kind) => false;

    public DriftsplanJob? Plan(DriftsplanContext context)
    {
        var site = FjordPolicies.FjordSite(context.Engine, context.UnitId);
        if (site == null) return null;
        const string verb = "delouse_site";
        string method = context.Choice(Method.Key);
        string command = $"{verb} {site.Id} {method}";
        if (FjordPolicies.FishAt(site) <= 0)
            return FjordPolicies.WaitingForStock(Kind, site.Id, verb, command);

        double level = context.Number(ShareOfLimit.Key) * context.Engine.LiceLimitAtSite(site);
        var entry = context.Entry;
        bool newCountAbove = site.LastLiceCountDay >= 0
            && site.LastLiceCountDay > entry.LastIssuedDay
            && site.LastLiceCount > level;
        if (!newCountAbove)
        {
            return new DriftsplanJob
            {
                Policy = Kind,
                UnitId = site.Id,
                Day = -1,
                Verb = verb,
                Command = command,
                Trigger = FjordPolicies.TriggerLiceCountAbove,
                TriggerValue = level,
            };
        }

        int interval = (int)Math.Round(context.Number(MinIntervalDays.Key));
        int earliest = entry.LastIssuedDay < 0 ? context.Today : entry.LastIssuedDay + interval;
        string siteId = site.Id;
        return new DriftsplanJob
        {
            Policy = Kind,
            UnitId = siteId,
            Day = Math.Max(context.Today, earliest),
            Verb = verb,
            Command = command,
            Trigger = FjordPolicies.TriggerLiceCountAbove,
            TriggerValue = level,
            Issue = engine => engine.DelouseSite(siteId, method),
        };
    }
}
