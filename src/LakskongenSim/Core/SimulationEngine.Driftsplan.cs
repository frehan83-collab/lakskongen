using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Events;
using LakskongenSim.Farm;

namespace LakskongenSim.Core;

/// <summary>
/// The Driftsplan (roadmap F2-05, docs/FARM_TYPES_SPEC.md §1 pillar 5, §3.1
/// decision 7, §3.4): the company's standing orders. Each entry runs one
/// policy on one unit; at the start of every simulated day, before the world
/// moves, each entry plans its next job and issues it when it is due, through
/// the same public verb the player calls between days. A plan therefore plays
/// exactly as a player who issues those commands at the start of those days
/// (<c>DriftsplanTests</c> holds the two games equal, day by day). Planning
/// never draws: every evaluation is checked against every stream position
/// (<see cref="DriftsplanPurityException"/>). A game without entries does
/// nothing here.
/// </summary>
public partial class SimulationEngine
{
    /// <summary>Commands kept in <see cref="DriftsplanLog"/> (oldest dropped first).</summary>
    public const int DriftsplanLogCapacity = 200;

    private readonly List<DriftsplanIssue> _driftsplanLog = new();
    private bool _driftsplanLogHooked;

    /// <summary>
    /// The commands the standing orders issued, oldest first (at most
    /// <see cref="DriftsplanLogCapacity"/>). Presentation state like the event
    /// bus: not saved, emptied with the bus (a new or loaded game); each
    /// entry keeps its own last day and outcome in the save.
    /// </summary>
    public IReadOnlyList<DriftsplanIssue> DriftsplanLog
    {
        get
        {
            HookDriftsplanLog();
            return _driftsplanLog;
        }
    }

    // ─── Commands ──────────────────────────────────────────────────────────

    /// <summary>
    /// Set (or change) the standing order <paramref name="policyKind"/> on
    /// <paramref name="unitId"/>. Settings not given keep the order's current
    /// value, or take the policy's default for a new order; a changed order
    /// keeps its history (the cadence runs on from its last job). Fails before
    /// any change: engine_not_ready, unknown_policy, not_found (no such unit),
    /// not_applicable (the policy is not for this kind of unit), out_of_range
    /// (an unknown setting, a number outside its range or not whole where it
    /// must be, an option not in the list).
    /// </summary>
    public CommandResult SetDriftsplan(string unitId, string policyKind,
        IReadOnlyDictionary<string, double>? numbers = null, IReadOnlyDictionary<string, string>? choices = null)
    {
        if (State == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        var policy = DriftsplanPolicies.Find(policyKind);
        if (policy == null)
            return CommandResult.Fail("unknown_policy", $"No Driftsplan policy '{policyKind}'");
        var kind = DriftsplanUnitKindOf(unitId);
        if (kind == null)
            return CommandResult.Fail("not_found", $"No unit '{unitId}'");
        if (!policy.AppliesTo.Contains(kind.Value))
            return CommandResult.Fail("not_applicable", $"'{policyKind}' does not apply to a {kind.Value}");

        string? problem = SettingsProblem(policy, numbers, choices);
        if (problem != null)
            return CommandResult.Fail("out_of_range", problem);

        var existing = FindDriftsplanEntry(unitId, policy.Kind);
        var entry = existing ?? new DriftsplanEntry { UnitId = unitId, Policy = policy.Kind, SinceDay = State.Day };
        var newNumbers = new Dictionary<string, double>(StringComparer.Ordinal);
        var newChoices = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var setting in policy.Settings)
        {
            if (setting.Kind == DriftsplanSettingKind.Number)
            {
                double value = numbers != null && numbers.TryGetValue(setting.Key, out double given) ? given
                    : existing != null && existing.Numbers.TryGetValue(setting.Key, out double kept) ? kept
                    : setting.Default;
                newNumbers[setting.Key] = value;
            }
            else
            {
                string value = choices != null && choices.TryGetValue(setting.Key, out var given) ? given
                    : existing != null && existing.Choices.TryGetValue(setting.Key, out var kept) ? kept
                    : setting.DefaultChoice;
                newChoices[setting.Key] = value;
            }
        }
        entry.Numbers = newNumbers;
        entry.Choices = newChoices;
        if (existing == null)
            InsertDriftsplanEntry(entry);
        return CommandResult.Success(
            existing == null ? $"Driftsplan: {policy.Kind} set on {unitId}" : $"Driftsplan: {policy.Kind} on {unitId} changed",
            id: $"{unitId}/{policy.Kind}");
    }

    /// <summary>End the standing order <paramref name="policyKind"/> on <paramref name="unitId"/>. Fails: engine_not_ready, not_found.</summary>
    public CommandResult ClearDriftsplan(string unitId, string policyKind)
    {
        if (State == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        var entry = FindDriftsplanEntry(unitId, policyKind);
        if (entry == null)
            return CommandResult.Fail("not_found", $"No Driftsplan order '{policyKind}' on '{unitId}'");
        State.Driftsplan.Remove(entry);
        return CommandResult.Success($"Driftsplan: {policyKind} on {unitId} ended", id: $"{unitId}/{policyKind}");
    }

    /// <summary>
    /// Give <paramref name="unitId"/> every policy that is a default for its
    /// kind and not set yet (a lane calls it when it builds a unit: "every
    /// periodic task has a Driftsplan default", spec §1 pillar 5). Returns how
    /// many orders it set.
    /// </summary>
    public int ApplyDriftsplanDefaults(string unitId)
    {
        var kind = DriftsplanUnitKindOf(unitId);
        if (kind == null) return 0;
        int set = 0;
        foreach (var policy in DriftsplanPolicies.All)
        {
            if (!policy.AppliesTo.Contains(kind.Value) || !policy.IsDefaultFor(kind.Value)) continue;
            if (FindDriftsplanEntry(unitId, policy.Kind) != null) continue;
            if (SetDriftsplan(unitId, policy.Kind).Ok) set++;
        }
        return set;
    }

    // ─── Reads (pure: never draw, never change the game) ───────────────────

    /// <summary>The standing orders on <paramref name="unitId"/>, sorted by policy.</summary>
    public IReadOnlyList<DriftsplanEntry> GetDriftsplan(string unitId)
        => State?.Driftsplan == null
            ? Array.Empty<DriftsplanEntry>()
            : State.Driftsplan.Where(e => e.UnitId == unitId).ToList();

    /// <summary>
    /// Every standing order's next job on <paramref name="unitId"/> (one per
    /// order that has something to plan), soonest first: dated jobs by day,
    /// then the ones that wait for a condition.
    /// </summary>
    public IReadOnlyList<DriftsplanJob> GetPlannedJobs(string unitId)
    {
        var jobs = new List<DriftsplanJob>();
        foreach (var entry in GetDriftsplan(unitId))
        {
            var policy = DriftsplanPolicies.Find(entry.Policy);
            if (policy == null) continue;
            var job = EvaluateDriftsplanEntry(policy, entry);
            if (job != null) jobs.Add(job);
        }
        return jobs
            .OrderBy(j => j.Day < 0 ? 1 : 0)
            .ThenBy(j => j.Day)
            .ThenBy(j => j.Policy, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// "Neste planlagt" (spec §6.5: every board leads with it): the soonest
    /// job of the unit's standing orders, a waiting one when none is dated,
    /// or null when the unit has no order with anything to plan.
    /// </summary>
    public DriftsplanJob? GetNextPlanned(string unitId) => GetPlannedJobs(unitId).FirstOrDefault();

    /// <summary>The kind of unit <paramref name="unitId"/> is, or null when the company has no such unit.</summary>
    public DriftsplanUnitKind? DriftsplanUnitKindOf(string? unitId)
    {
        if (string.IsNullOrEmpty(unitId) || State == null) return null;
        var site = State.SeaSites?.FirstOrDefault(s => s.Id == unitId);
        if (site != null)
            return site.Kind == SiteKind.Offshore ? DriftsplanUnitKind.OffshoreUnit : DriftsplanUnitKind.FjordSite;
        var land = State.RASFacilities?.FirstOrDefault(f => f.Id == unitId);
        if (land != null)
            return land.Kind == LandSiteKind.PostSmolt ? DriftsplanUnitKind.PostSmoltSite : DriftsplanUnitKind.LandSite;
        return null;
    }

    /// <summary>The name the player reads for a unit (a site's label, a land site's name), "" for none.</summary>
    public string UnitLabel(string? unitId)
    {
        if (string.IsNullOrEmpty(unitId) || State == null) return "";
        var site = State.SeaSites?.FirstOrDefault(s => s.Id == unitId);
        if (site != null) return site.Label;
        var land = State.RASFacilities?.FirstOrDefault(f => f.Id == unitId);
        if (land != null && !string.IsNullOrWhiteSpace(land.Name)) return land.Name;
        return SiteNames.Humanize(unitId);
    }

    // ─── The phase ─────────────────────────────────────────────────────────

    /// <summary>
    /// The Driftsplan phase: called by <see cref="Advance"/> at the start of
    /// each simulated day, before the step resolves its holdings and before
    /// the weather moves, which is where a player's commands between days
    /// land. Each order (in unit and policy order) plans; a job due today is
    /// issued through its verb, and its outcome is kept on the order. A failed
    /// command is retried on the following days (the job slips) and the
    /// player is told once (<see cref="GameEventType.DriftsplanCommandFailed"/>).
    /// </summary>
    internal void RunDriftsplan()
    {
        if (State?.Driftsplan == null || State.Driftsplan.Count == 0) return;
        bool issued = false;
        foreach (var entry in State.Driftsplan.ToList())
        {
            var policy = DriftsplanPolicies.Find(entry.Policy);
            if (policy == null) continue;
            var job = EvaluateDriftsplanEntry(policy, entry);
            if (job == null || !job.IsDueOn(State.Day)) continue;

            var result = job.Issue!(this);
            issued = true;
            entry.LastAttemptDay = State.Day;
            entry.LastOutcome = result.Ok ? "ok" : result.ErrorKey.Length > 0 ? result.ErrorKey : "failed";
            if (result.Ok)
            {
                entry.LastIssuedDay = State.Day;
                entry.IssuedCount++;
            }
            AppendDriftsplanLog(new DriftsplanIssue(State.Day, entry.UnitId, entry.Policy, job.Command, result.Ok, result.ErrorKey));
            if (!result.Ok)
                PublishDriftsplanFailure(policy, entry, result);
        }
        // A command's notes happened today, as a player's between days do.
        if (issued)
            StampDayNotes();
    }

    /// <summary>
    /// Plan one order, checked: if planning moved any stream position (or
    /// opened a stream), the policy is broken and the game must not go on
    /// as if it were the player's (spec §3.7: policies never draw).
    /// </summary>
    internal DriftsplanJob? EvaluateDriftsplanEntry(IDriftsplanPolicy policy, DriftsplanEntry entry)
    {
        var before = Rng.CaptureStates();
        var job = policy.Plan(new DriftsplanContext(this, entry, State.Day));
        var after = Rng.CaptureStates();
        string? moved = FirstMovedStream(before, after);
        if (moved != null)
            throw new DriftsplanPurityException(policy.Kind, moved);
        return job;
    }

    private static string? FirstMovedStream(Dictionary<string, ulong> before, Dictionary<string, ulong> after)
    {
        foreach (var key in after.Keys.OrderBy(k => k, StringComparer.Ordinal))
            if (!before.TryGetValue(key, out ulong old) || old != after[key])
                return key.Replace('\u0001', '/');
        return before.Count != after.Count ? "(a stream disappeared)" : null;
    }

    // ─── Save and load ─────────────────────────────────────────────────────

    /// <summary>Adopt a loaded save's standing orders (a save from before F2-05 has none).</summary>
    internal void AdoptDriftsplanState(GameState snapshot)
    {
        State.Driftsplan.Clear();
        if (snapshot.Driftsplan == null) return;
        foreach (var entry in snapshot.Driftsplan)
        {
            if (entry == null || string.IsNullOrEmpty(entry.UnitId) || string.IsNullOrEmpty(entry.Policy)) continue;
            if (FindDriftsplanEntry(entry.UnitId, entry.Policy) != null) continue;
            entry.Numbers ??= new Dictionary<string, double>(StringComparer.Ordinal);
            entry.Choices ??= new Dictionary<string, string>(StringComparer.Ordinal);
            entry.LastOutcome ??= "";
            InsertDriftsplanEntry(entry);
        }
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    private DriftsplanEntry? FindDriftsplanEntry(string unitId, string policyKind)
        => State?.Driftsplan?.FirstOrDefault(e => e.UnitId == unitId && e.Policy == policyKind);

    /// <summary>Insert keeping the list sorted by unit, then policy (ordinal).</summary>
    private void InsertDriftsplanEntry(DriftsplanEntry entry)
    {
        var list = State.Driftsplan;
        int index = 0;
        while (index < list.Count && Compare(list[index], entry) < 0)
            index++;
        list.Insert(index, entry);

        static int Compare(DriftsplanEntry a, DriftsplanEntry b)
        {
            int byUnit = string.CompareOrdinal(a.UnitId, b.UnitId);
            return byUnit != 0 ? byUnit : string.CompareOrdinal(a.Policy, b.Policy);
        }
    }

    private static string? SettingsProblem(IDriftsplanPolicy policy,
        IReadOnlyDictionary<string, double>? numbers, IReadOnlyDictionary<string, string>? choices)
    {
        var declared = policy.Settings.ToDictionary(s => s.Key, StringComparer.Ordinal);
        foreach (var kv in numbers ?? new Dictionary<string, double>())
        {
            if (!declared.TryGetValue(kv.Key, out var setting))
                return $"'{policy.Kind}' has no setting '{kv.Key}'";
            string? problem = setting.NumberProblem(kv.Value);
            if (problem != null) return problem;
        }
        foreach (var kv in choices ?? new Dictionary<string, string>())
        {
            if (!declared.TryGetValue(kv.Key, out var setting))
                return $"'{policy.Kind}' has no setting '{kv.Key}'";
            string? problem = setting.ChoiceProblem(kv.Value);
            if (problem != null) return problem;
        }
        return null;
    }

    private void HookDriftsplanLog()
    {
        if (_driftsplanLogHooked) return;
        _driftsplanLogHooked = true;
        GameEvents.Cleared += _driftsplanLog.Clear;
    }

    private void AppendDriftsplanLog(DriftsplanIssue issue)
    {
        HookDriftsplanLog();
        _driftsplanLog.Add(issue);
        if (_driftsplanLog.Count > DriftsplanLogCapacity)
            _driftsplanLog.RemoveRange(0, _driftsplanLog.Count - DriftsplanLogCapacity);
    }

    /// <summary>A standing order could not do its job today: tell the player once (the bus keeps a retrying order one notification).</summary>
    private void PublishDriftsplanFailure(IDriftsplanPolicy policy, DriftsplanEntry entry, CommandResult result)
    {
        bool seaSite = State.SeaSites.Any(s => s.Id == entry.UnitId);
        PublishGameEvent(new GameEventRequest(GameEventType.DriftsplanCommandFailed, State.Day)
        {
            SiteId = seaSite ? entry.UnitId : "",
            UnitId = entry.UnitId,
            Variant = entry.Policy,
            Message = SimText.Nb($"Driftsplanen for {UnitLabel(entry.UnitId)} fikk ikke utført {policy.Title}: {DriftsplanFailureReason(result.ErrorKey)}. Den prøver igjen hver dag."),
        });
    }

    private static string DriftsplanFailureReason(string errorKey) => errorKey switch
    {
        "insufficient_funds" => "for lite penger på konto",
        "already_booked" => "en brønnbåt er allerede bestilt",
        "site_empty" => "det er ingen fisk i merdene",
        "cancelled" => "behandlingen ble avlyst",
        "unknown_method" => "metoden finnes ikke",
        _ => "kommandoen ble avvist",
    };
}
