using System.Collections.Generic;
using Godot;
using LakskongenSim.Core;

// GameBridge.Driftsplan.cs — partial class slice (roadmap F2-05; sim·core's).
// <summary>
// The Driftsplan (docs/FARM_TYPES_SPEC.md §1 pillar 5, §3.4, §6.5): the
// policies the game knows with their settings (range, default, unit and the
// default's source), a unit's standing orders, "Neste planlagt", the verbs that
// set and end an order, and the notification meter (spec §6.6). Thin calls
// into SimulationEngine; every number the UI shows comes from here.
// </summary>
/// <summary>
/// GameBridge partial — F2-05 Driftsplan and notification meter (sim·core's slice).
/// </summary>
public partial class GameBridge : Node
{
    /// <summary>
    /// The policies: Array of {"kind", "name_key" ("DRIFTSPLAN_POLICY_&lt;KIND&gt;"),
    /// "applies_to" (Array of "fjord_site" | "offshore_unit" | "land_site" |
    /// "post_smolt_site"), "settings" (Array of {"key", "name_key", "kind"
    /// ("number" | "choice"), "min", "max", "default", "whole", "unit",
    /// "choices", "default_choice", "source"})}.
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetDriftsplanCatalog()
    {
        var list = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var policy in DriftsplanPolicies.All)
        {
            var kinds = new Godot.Collections.Array<string>();
            foreach (var kind in policy.AppliesTo)
                kinds.Add(DriftsplanUnitKindId(kind));
            var settings = new Godot.Collections.Array<Godot.Collections.Dictionary>();
            foreach (var setting in policy.Settings)
            {
                var choices = new Godot.Collections.Array<string>();
                foreach (var choice in setting.Choices)
                    choices.Add(choice);
                settings.Add(new Godot.Collections.Dictionary
                {
                    ["key"] = setting.Key,
                    ["name_key"] = "DRIFTSPLAN_SETTING_" + setting.Key.ToUpperInvariant(),
                    ["kind"] = setting.Kind == DriftsplanSettingKind.Choice ? "choice" : "number",
                    ["min"] = setting.Min,
                    ["max"] = setting.Max,
                    ["default"] = setting.Default,
                    ["whole"] = setting.WholeNumber,
                    ["unit"] = setting.Unit,
                    ["choices"] = choices,
                    ["default_choice"] = setting.DefaultChoice,
                    ["source"] = setting.Source,
                });
            }
            list.Add(new Godot.Collections.Dictionary
            {
                ["kind"] = policy.Kind,
                ["name_key"] = "DRIFTSPLAN_POLICY_" + policy.Kind.ToUpperInvariant(),
                ["applies_to"] = kinds,
                ["settings"] = settings,
            });
        }
        return list;
    }

    /// <summary>
    /// The standing orders on <paramref name="unitId"/>: Array of {"policy",
    /// "numbers" (Dictionary key → float), "choices" (Dictionary key → String),
    /// "since_day", "last_issued_day" (-1 never), "issued_count",
    /// "last_attempt_day", "last_outcome" ("" | "ok" | an error key), "next"
    /// (the order's next job as <see cref="GetNextPlanned"/> gives it, {} when none)}.
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetDriftsplan(string unitId)
    {
        var list = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        if (SimulationEngine?.State == null) return list;
        var jobs = new Dictionary<string, DriftsplanJob>();
        foreach (var job in SimulationEngine.GetPlannedJobs(unitId))
            jobs[job.Policy] = job;
        foreach (var entry in SimulationEngine.GetDriftsplan(unitId))
        {
            var numbers = new Godot.Collections.Dictionary();
            foreach (var kv in entry.Numbers)
                numbers[kv.Key] = kv.Value;
            var choices = new Godot.Collections.Dictionary();
            foreach (var kv in entry.Choices)
                choices[kv.Key] = kv.Value;
            list.Add(new Godot.Collections.Dictionary
            {
                ["policy"] = entry.Policy,
                ["numbers"] = numbers,
                ["choices"] = choices,
                ["since_day"] = entry.SinceDay,
                ["last_issued_day"] = entry.LastIssuedDay,
                ["issued_count"] = entry.IssuedCount,
                ["last_attempt_day"] = entry.LastAttemptDay,
                ["last_outcome"] = entry.LastOutcome,
                ["next"] = jobs.TryGetValue(entry.Policy, out var next) ? DriftsplanJobDictionary(next) : new Godot.Collections.Dictionary(),
            });
        }
        return list;
    }

    /// <summary>
    /// Set or change a standing order. <paramref name="settings"/> maps a setting
    /// key to a number (int or float) or an option (String); keys not given keep
    /// the order's value or take the default. Returns {"success", "error"
    /// ("" | engine_not_ready | unknown_policy | not_found | not_applicable |
    /// out_of_range), "message"}.
    /// </summary>
    public Godot.Collections.Dictionary SetDriftsplan(string unitId, string policyKind, Godot.Collections.Dictionary settings)
    {
        if (SimulationEngine?.State == null)
            return DriftsplanResult(CommandResult.Fail("engine_not_ready", "Not initialized"));
        var numbers = new Dictionary<string, double>();
        var choices = new Dictionary<string, string>();
        if (settings != null)
        {
            foreach (var key in settings.Keys)
            {
                var value = settings[key];
                switch (value.VariantType)
                {
                    case Variant.Type.Int: numbers[key.AsString()] = value.AsInt64(); break;
                    case Variant.Type.Float: numbers[key.AsString()] = value.AsDouble(); break;
                    case Variant.Type.String:
                    case Variant.Type.StringName: choices[key.AsString()] = value.AsString(); break;
                    default:
                        return DriftsplanResult(CommandResult.Fail("out_of_range", $"Setting '{key.AsString()}' is neither a number nor an option"));
                }
            }
        }
        var result = SimulationEngine.SetDriftsplan(unitId, policyKind, numbers, choices);
        if (result.Ok) EmitSignal(SignalName.GameStateUpdated);
        return DriftsplanResult(result);
    }

    /// <summary>End a standing order. Returns {"success", "error" ("" | engine_not_ready | not_found), "message"}.</summary>
    public Godot.Collections.Dictionary ClearDriftsplan(string unitId, string policyKind)
    {
        if (SimulationEngine?.State == null)
            return DriftsplanResult(CommandResult.Fail("engine_not_ready", "Not initialized"));
        var result = SimulationEngine.ClearDriftsplan(unitId, policyKind);
        if (result.Ok) EmitSignal(SignalName.GameStateUpdated);
        return DriftsplanResult(result);
    }

    /// <summary>
    /// "Neste planlagt" for a unit: {"policy", "unit_id", "day" (-1 = waits for
    /// "trigger"), "days_left" (-1 when waiting), "verb", "command", "trigger"
    /// ("" | "site_stocked" | "lice_count_above" | a lane's key),
    /// "trigger_value"}; {} when the unit has no order with anything to plan.
    /// </summary>
    public Godot.Collections.Dictionary GetNextPlanned(string unitId)
    {
        var job = SimulationEngine?.State == null ? null : SimulationEngine.GetNextPlanned(unitId);
        return job == null ? new Godot.Collections.Dictionary() : DriftsplanJobDictionary(job);
    }

    /// <summary>
    /// The notification meter (spec §6.6): Array of {"unit_id" ("" = the
    /// company), "month" (months since January 2027), "year", "month_of_year",
    /// "warnings", "criticals"}, by unit then month; and the budget it is
    /// measured against in {"warning_budget_per_unit_month",
    /// "critical_budget_per_unit_year"} of <see cref="GetNotificationBudget"/>.
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetNotificationMeter()
    {
        var list = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        if (SimulationEngine == null) return list;
        foreach (var month in SimulationEngine.Notifications.Months)
        {
            list.Add(new Godot.Collections.Dictionary
            {
                ["unit_id"] = month.UnitId,
                ["month"] = month.Month,
                ["year"] = month.Year,
                ["month_of_year"] = month.MonthOfYear,
                ["warnings"] = month.Warnings,
                ["criticals"] = month.Criticals,
            });
        }
        return list;
    }

    /// <summary>The notification budget (spec §6.6): {"warning_budget_per_unit_month", "critical_budget_per_unit_year"}.</summary>
    public Godot.Collections.Dictionary GetNotificationBudget() => new()
    {
        ["warning_budget_per_unit_month"] = NotificationMeter.WarningBudgetPerUnitMonth,
        ["critical_budget_per_unit_year"] = NotificationMeter.CriticalBudgetPerUnitYear,
    };

    private Godot.Collections.Dictionary DriftsplanJobDictionary(DriftsplanJob job)
    {
        int today = SimulationEngine?.State?.Day ?? 0;
        return new Godot.Collections.Dictionary
        {
            ["policy"] = job.Policy,
            ["unit_id"] = job.UnitId,
            ["day"] = job.Day,
            ["days_left"] = job.Day < 0 ? -1 : System.Math.Max(0, job.Day - today),
            ["verb"] = job.Verb,
            ["command"] = job.Command,
            ["trigger"] = job.Trigger,
            ["trigger_value"] = job.TriggerValue,
        };
    }

    private static Godot.Collections.Dictionary DriftsplanResult(CommandResult result) => new()
    {
        ["success"] = result.Ok,
        ["error"] = result.ErrorKey,
        ["message"] = result.Message,
    };

    private static string DriftsplanUnitKindId(DriftsplanUnitKind kind) => kind switch
    {
        DriftsplanUnitKind.OffshoreUnit => "offshore_unit",
        DriftsplanUnitKind.LandSite => "land_site",
        DriftsplanUnitKind.PostSmoltSite => "post_smolt_site",
        _ => "fjord_site",
    };
}
