using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LakskongenSim.Core;

/// <summary>
/// What a Driftsplan entry is set on (docs/FARM_TYPES_SPEC.md §1 pillar 5,
/// §3.1 decision 7): a fjord locality, an offshore unit, a grow-out land site
/// or a post-smolt land site. A policy declares the kinds it applies to.
/// </summary>
public enum DriftsplanUnitKind
{
    FjordSite = 0,
    OffshoreUnit = 1,
    LandSite = 2,
    PostSmoltSite = 3,
}

/// <summary>A setting's type: a number in a range, or one option of a fixed list.</summary>
public enum DriftsplanSettingKind
{
    Number = 0,
    Choice = 1,
}

/// <summary>
/// One setting a policy declares: its key, range or options, default, unit
/// and where the default comes from. The UI draws a setting from this record
/// and holds no number of its own; <see cref="Source"/> is shown with it, so a
/// default is never a number without a reason (spec §3.1 decision 8).
/// </summary>
public sealed record DriftsplanSetting
{
    /// <summary>Stable key ("interval_days"); the bridge and saves use it.</summary>
    public required string Key { get; init; }

    public DriftsplanSettingKind Kind { get; init; } = DriftsplanSettingKind.Number;

    /// <summary>Smallest value a number setting takes.</summary>
    public double Min { get; init; }

    /// <summary>Largest value a number setting takes.</summary>
    public double Max { get; init; }

    /// <summary>The number a new plan starts with.</summary>
    public double Default { get; init; }

    /// <summary>True when a number setting takes whole values only (days).</summary>
    public bool WholeNumber { get; init; }

    /// <summary>The unit a number is in: "days", "share" (0-1) or "" (none).</summary>
    public string Unit { get; init; } = "";

    /// <summary>The options of a choice setting (stable ids).</summary>
    public IReadOnlyList<string> Choices { get; init; } = Array.Empty<string>();

    /// <summary>The option a new plan starts with.</summary>
    public string DefaultChoice { get; init; } = "";

    /// <summary>Where the default comes from (a source with its year, or "GB: why").</summary>
    public required string Source { get; init; }

    /// <summary>Why a number is outside the setting, or null when it fits.</summary>
    public string? NumberProblem(double value)
    {
        if (Kind != DriftsplanSettingKind.Number) return $"'{Key}' takes one of {string.Join(", ", Choices)}";
        if (!double.IsFinite(value)) return $"'{Key}' must be a finite number";
        if (value < Min || value > Max)
            return string.Create(CultureInfo.InvariantCulture, $"'{Key}' must lie in [{Min}, {Max}], not {value}");
        if (WholeNumber && Math.Abs(value - Math.Round(value)) > 1e-9)
            return string.Create(CultureInfo.InvariantCulture, $"'{Key}' takes whole {Unit}, not {value}");
        return null;
    }

    /// <summary>Why an option is not one of the setting's, or null when it is.</summary>
    public string? ChoiceProblem(string value)
    {
        if (Kind != DriftsplanSettingKind.Choice) return $"'{Key}' takes a number";
        return Choices.Contains(value ?? "", StringComparer.Ordinal)
            ? null
            : $"'{Key}' takes one of {string.Join(", ", Choices)}, not '{value}'";
    }
}

/// <summary>
/// One standing order of the Driftsplan (saved): a policy on a unit with its
/// settings, and what it last did. The engine keeps
/// <see cref="GameState.Driftsplan"/> sorted by unit and policy, so the order
/// the orders act in never depends on the order the player set them in.
/// </summary>
public sealed class DriftsplanEntry
{
    /// <summary>The unit (a sea site id or a land site id).</summary>
    public string UnitId { get; set; } = "";

    /// <summary>The policy's kind (<see cref="IDriftsplanPolicy.Kind"/>).</summary>
    public string Policy { get; set; } = "";

    /// <summary>The number settings, in the policy's declared order.</summary>
    public Dictionary<string, double> Numbers { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The choice settings, in the policy's declared order.</summary>
    public Dictionary<string, string> Choices { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The day the order was first set.</summary>
    public int SinceDay { get; set; }

    /// <summary>The last day the order issued a command that went through (-1 never).</summary>
    public int LastIssuedDay { get; set; } = -1;

    /// <summary>Commands the order issued that went through.</summary>
    public int IssuedCount { get; set; }

    /// <summary>The last day the order issued a command, through or not (-1 never).</summary>
    public int LastAttemptDay { get; set; } = -1;

    /// <summary>"" before the first command, "ok", or the last failed command's error key.</summary>
    public string LastOutcome { get; set; } = "";

    /// <summary>A number setting (NaN when the entry has none of that key).</summary>
    public double Number(string key) => Numbers.TryGetValue(key, out double v) ? v : double.NaN;

    /// <summary>A choice setting ("" when the entry has none of that key).</summary>
    public string Choice(string key) => Choices.TryGetValue(key, out var v) ? v ?? "" : "";
}

/// <summary>
/// What a policy reads when it plans: the engine (read only), its entry and
/// today. A policy never changes the engine and never draws from a stream:
/// the engine checks every evaluation (<see cref="DriftsplanPurityException"/>).
/// </summary>
public readonly record struct DriftsplanContext(SimulationEngine Engine, DriftsplanEntry Entry, int Today)
{
    public string UnitId => Entry.UnitId;
    public double Number(string key) => Entry.Number(key);
    public string Choice(string key) => Entry.Choice(key);
}

/// <summary>
/// The job a standing order plans next. A dated job (<see cref="Day"/> ≥ 0) is
/// issued at the start of that day, exactly as the player would issue
/// <see cref="Command"/> before the day runs; a job with <see cref="Day"/> = -1
/// waits for a condition (<see cref="Trigger"/>, e.g. a lice count above the
/// order's level) and is shown as "ved behov" until it holds.
/// </summary>
public sealed record DriftsplanJob
{
    public required string Policy { get; init; }
    public required string UnitId { get; init; }

    /// <summary>The day the job is due (issued on that day's start), or -1 while it waits for <see cref="Trigger"/>.</summary>
    public int Day { get; init; } = -1;

    /// <summary>The engine verb it issues ("request_wellboat", "delouse_site").</summary>
    public required string Verb { get; init; }

    /// <summary>The command as the player would type it ("request_wellboat site_a rov").</summary>
    public required string Command { get; init; }

    /// <summary>What a waiting job waits for ("" for a dated job): a stable key the UI words.</summary>
    public string Trigger { get; init; } = "";

    /// <summary>The level the trigger is measured against (a lice count, a silo's days), 0 when none.</summary>
    public double TriggerValue { get; init; }

    /// <summary>Issues the command (the same public engine verb a player calls). Null for a waiting job.</summary>
    public Func<SimulationEngine, CommandResult>? Issue { get; init; }

    /// <summary>True when the job is issued on <paramref name="today"/>'s start.</summary>
    public bool IsDueOn(int today) => Day >= 0 && Day <= today && Issue != null;
}

/// <summary>
/// A Driftsplan policy (spec §3.1 decision 7): a periodic task or a standing
/// order that issues the same engine commands a player would, evaluated at the
/// start of each simulated day. Policies are stateless: what they remember
/// (the last day they acted) is their <see cref="DriftsplanEntry"/>.
/// </summary>
public interface IDriftsplanPolicy
{
    /// <summary>Stable id ("net_cleaning"); saves and the bridge use it.</summary>
    string Kind { get; }

    /// <summary>The Norwegian name the sim writes into its texts ("notpleie"); the UI words it from <c>DRIFTSPLAN_POLICY_&lt;KIND&gt;</c>.</summary>
    string Title { get; }

    /// <summary>The unit kinds the policy can be set on.</summary>
    IReadOnlyList<DriftsplanUnitKind> AppliesTo { get; }

    /// <summary>The settings, in the order the UI shows them.</summary>
    IReadOnlyList<DriftsplanSetting> Settings { get; }

    /// <summary>True when a new unit of <paramref name="kind"/> gets this policy by default (the lanes' periodic tasks).</summary>
    bool IsDefaultFor(DriftsplanUnitKind kind);

    /// <summary>
    /// The next job of <paramref name="context"/>'s entry from today on, or
    /// null when the policy has nothing to plan on the unit. Pure: reads the
    /// engine, never changes it and never draws from a stream.
    /// </summary>
    DriftsplanJob? Plan(DriftsplanContext context);
}

/// <summary>
/// A policy moved a stream position while it planned: a lane's bug. Policy
/// evaluation must never draw (spec §3.7 "forecasts, previews, lenses, policies
/// and display noise never draw"), or a game with a plan would diverge from
/// the same game played by hand.
/// </summary>
public sealed class DriftsplanPurityException : InvalidOperationException
{
    public DriftsplanPurityException(string policy, string stream)
        : base($"Driftsplan policy '{policy}' moved the stream '{stream}' while it planned; a policy must never draw")
    {
        Policy = policy;
        Stream = stream;
    }

    public string Policy { get; }
    public string Stream { get; }
}

/// <summary>A command a standing order issued (presentation log, not saved).</summary>
public sealed record DriftsplanIssue(int Day, string UnitId, string Policy, string Command, bool Ok, string ErrorKey);

/// <summary>
/// The policies the game knows, sorted by kind. sim·core's fjord policies are
/// listed here; each farm-type lane adds its own from its own file by
/// implementing its partial method (<c>Farm/Offshore/OffshorePolicies.cs</c>,
/// <c>Farm/Land/LandPolicies.cs</c>), so no lane edits this file.
/// </summary>
public static partial class DriftsplanPolicies
{
    private static readonly IReadOnlyList<IDriftsplanPolicy> Registered = Build();

    private static readonly Dictionary<string, IDriftsplanPolicy> ByKind =
        Registered.ToDictionary(p => p.Kind, StringComparer.Ordinal);

    /// <summary>Every policy, sorted by kind (ordinal).</summary>
    public static IReadOnlyList<IDriftsplanPolicy> All => Registered;

    /// <summary>The policy of <paramref name="kind"/>, or null.</summary>
    public static IDriftsplanPolicy? Find(string? kind)
        => kind != null && ByKind.TryGetValue(kind, out var policy) ? policy : null;

    private static IReadOnlyList<IDriftsplanPolicy> Build()
    {
        var policies = new List<IDriftsplanPolicy>();
        Farm.FjordPolicies.AddTo(policies);
        AddOffshorePolicies(policies);
        AddLandPolicies(policies);
        var duplicate = policies.GroupBy(p => p.Kind, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
            throw new InvalidOperationException($"Two Driftsplan policies share the kind '{duplicate.Key}'");
        return policies.OrderBy(p => p.Kind, StringComparer.Ordinal).ToList();
    }

    /// <summary>The sea lane's policies (F2-07: resupply, crew rota, inspection cadence, reports).</summary>
    static partial void AddOffshorePolicies(List<IDriftsplanPolicy> policies);

    /// <summary>The land lane's policies (F2-12 … F2-16: generator tests, grading, purges, reports).</summary>
    static partial void AddLandPolicies(List<IDriftsplanPolicy> policies);
}
