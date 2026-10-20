using Godot;
using LakskongenSim.Core;
using LakskongenSim.Finance;
using LakskongenSim.Biology;
using LakskongenSim.Data;
using LakskongenSim.Integration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
// Namespace aliases for the Phase 5/6 features (sludge, carbon, cohort transfers,
// scenarios) so qualified references like Farm.SludgeValorization resolve from
// this global-namespace file.
using Farm = LakskongenSim.Farm;
using Events = LakskongenSim.Events;
using Processing = LakskongenSim.Processing;
using Logistics = LakskongenSim.Logistics;

// GameBridge.Depth.cs — partial class slice (see project docs).
// <summary>
// Depth slice: scenario system and mod APIs, sustainability breakdown,
// insurance policies, permeable barriers (Smoltscreen), sludge valorization,
// cohort transfers, the carbon ledger, and depth-exposure helpers.
// </summary>
/// <summary>
/// GameBridge partial — scenario system and mod APIs, sustainability breakdown, (moved verbatim from GameBridge.cs).
/// </summary>
public partial class GameBridge : Node
{
    // ── Scenario System API for GDScript ─────────────────────────────────────

    /// <summary>
    /// Return all available scenarios (built-in roster + mod scenarios) as an
    /// Array of Dictionaries. Keys consumed by the UI:
    ///   "id", "name", "description", "difficulty" (int 1-5), "region",
    ///   "starting_capital" (double), "starting_capital_neg" (bool),
    ///   "starting_cash" (double), "story" (string), "tags" (Array[String]),
    ///   "restrictions" (Array[String]),
    ///   "win_condition", "win" (human text), "time_limit_days",
    ///   "is_unlocked"/"unlocked" (bool), "special_rules" (Dictionary),
    ///   "special" (Array[String]), "subtitle", "capital" (formatted).
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetAvailableScenarios()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        if (SimulationEngine == null) return result;

        ScenarioSystem? system = SimulationEngine.Scenario;
        system ??= new ScenarioSystem(SimulationEngine.State);

        // A mod scenario the engine has registered (after a game started) is
        // still a mod's: the picker tags it and shows its text as the mod's.
        var builtIn = new ScenarioSystem(new GameState()).All.Keys.ToHashSet();
        foreach (var scenario in system.GetAll())
        {
            var entry = ScenarioToDictionary(scenario);
            if (!builtIn.Contains(scenario.Id))
            {
                entry["is_mod"] = true;
                entry["subtitle"] = "Mod";
            }
            result.Add(entry);
        }

        // Merge mod scenarios that are not already in the engine roster.
        if (_modLoader != null)
        {
            foreach (var mod in _modLoader.GetAllScenarios())
            {
                if (system.GetScenario(mod.Id) != null) continue;
                result.Add(ModScenarioToDictionary(mod));
            }
        }
        return result;
    }

    /// <summary>
    /// Load and start a scenario by ID. Initializes the engine if needed,
    /// applies the scenario's starting capital, and emits <c>ScenarioLoaded</c>.
    /// Returns true on success, false if unknown/locked or on error.
    /// </summary>
    public bool StartScenario(string scenarioId) => StartScenarioCore(scenarioId, seed: null);

    /// <summary>
    /// QA-only deterministic scenario start: fixed RNG seed so screenshot and
    /// headless QA runs are reproducible. Not reachable from player UI.
    /// </summary>
    public bool StartQaScenario(string scenarioId, int seed) => StartScenarioCore(scenarioId, seed);

    private bool StartScenarioCore(string scenarioId, int? seed)
    {
        if (string.IsNullOrWhiteSpace(scenarioId) || SimulationEngine == null)
            return false;

        // A scenario defines the starting state, so it always starts a fresh
        // game: validate against the roster, then initialize the engine. Only
        // a first start used to do this; a start during a running game layered
        // the scenario's sites onto the old game and ignored the QA seed.
        var probe = SimulationEngine.Scenario ?? new ScenarioSystem(SimulationEngine.State);
        // F692: on a fresh launch the engine has no roster yet, so the probe
        // holds the built-in scenarios only — the mod scenarios the picker
        // lists (GetAvailableScenarios merges them) must be valid here too.
        if (probe.GetScenario(scenarioId) == null && _modLoader != null)
            probe.RegisterModScenarios(_modLoader.GetAllScenarios());
        var candidate = probe.GetScenario(scenarioId);
        if (candidate == null || !candidate.IsUnlocked)
        {
            GD.Print($"GameBridge.StartScenario: '{scenarioId}' unavailable (unknown or locked)");
            return false;
        }
        // A player's start (no QA seed) opens paused on its briefing (U2-07).
        StartNewGameInternal("Player", (int)candidate.StartingCapitalNOK, seed, briefing: seed == null);

        var scenario = SimulationEngine.Scenario!.GetScenario(scenarioId);
        if (scenario == null) return false;

        // Engine verb: activates the scenario and applies its starting state
        // WITH the engine, so insurance, the resource-rent flag and the starting
        // disease reach the live systems (review F244).
        var started = SimulationEngine.StartScenario(scenarioId);
        if (!started.Ok)
        {
            GD.PrintErr($"GameBridge.StartScenario('{scenarioId}') failed: {started.Message}");
            return false;
        }

        EmitSignal(SignalName.ScenarioLoaded, scenario.Id, scenario.Name);
        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: scenario started — {scenario.Name} ({scenario.Id})");
        return true;
    }

    /// <summary>
    /// Progress toward the active scenario's goal for the goal HUD, measured as
    /// the engine's win check judges it (review F949): scenario_id, objective,
    /// condition, progress (0-1), won, lost, loss_reason, days_left (-1 when the
    /// scenario has no time limit). Empty when no scenario is active.
    /// </summary>
    public Godot.Collections.Dictionary GetScenarioGoal()
    {
        var goal = SimulationEngine?.GetScenarioGoalStatus();
        if (goal == null) return new Godot.Collections.Dictionary();
        return new Godot.Collections.Dictionary
        {
            ["scenario_id"] = goal.ScenarioId,
            ["objective"] = goal.Objective,
            ["condition"] = goal.Condition.ToString(),
            ["progress"] = goal.Progress01,
            ["won"] = goal.Won,
            ["lost"] = goal.Lost,
            ["loss_reason"] = goal.LossReason,
            ["days_left"] = goal.DaysLeft ?? -1,
        };
    }

    /// <summary>
    /// Return the ID of the currently active scenario (empty when none).
    /// </summary>
    public string GetActiveScenarioId()
        => SimulationEngine?.Scenario?.ActiveScenario?.Id ?? "";

    /// <summary>Whether the active scenario has been won.</summary>
    public bool IsScenarioWon => SimulationEngine?.Scenario?.IsWon ?? false;

    /// <summary>Whether the active scenario has been lost.</summary>
    public bool IsScenarioLost => SimulationEngine?.Scenario?.IsLost ?? false;

    /// <summary>Human-readable loss reason for the active scenario ("" if none).</summary>
    public string ScenarioLossReason => SimulationEngine?.Scenario?.LossReason ?? "";

    /// <summary>
    /// Apply a scenario supplied as a JSON string (used by ScenarioManager.gd).
    /// Accepts either the C# schema (win_condition/starting_balance) or the legacy
    /// Godot schema (goal/starting_balance). Unknown ids are registered first.
    /// Returns true when the scenario was started.
    /// </summary>
    public bool ApplyScenario(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || SimulationEngine == null) return false;

        ScenarioJsonDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ScenarioJsonDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
        }
        catch (Exception ex)
        {
            GD.PrintErr($"GameBridge.ApplyScenario: parse error: {ex.Message}");
            return false;
        }

        if (dto == null || string.IsNullOrWhiteSpace(dto.Id)) return false;

        decimal capital = dto.StartingBalance
                          ?? dto.StartingCapitalNok
                          ?? dto.InitialBalanceNok
                          ?? 10_000_000m;

        if (!_gameRunning || SimulationEngine.Scenario == null)
            StartNewGame("Player", (int)capital);

        if (SimulationEngine.Scenario!.GetScenario(dto.Id) == null)
        {
            SimulationEngine.Scenario.RegisterScenarios(new[]
            {
                new ScenarioDefinition
                {
                    Id = dto.Id,
                    Name = string.IsNullOrWhiteSpace(dto.Name) ? dto.Id : dto.Name,
                    Description = dto.Description,
                    StartingCapitalNOK = dto.StartingCapitalNok ?? capital,
                    StartingCash = dto.StartingCash ?? (dto.StartingCapitalNok ?? capital),
                    Story = string.IsNullOrWhiteSpace(dto.Story) ? dto.Description : dto.Story!,
                    StartingRegion = ParseRegion(dto.StartingRegion),
                    Difficulty = ParseDifficultyElement(dto.Difficulty),
                    WinCondition = ParseWinCondition(dto.WinCondition),
                    SpecialRules = new Dictionary<string, string>(),
                    StartingSites = dto.StartingSites ?? new List<StartingSiteDefinition>(),
                    StartingResearch = dto.StartingResearch ?? System.Array.Empty<string>(),
                    StartingBuildings = dto.StartingBuildings ?? System.Array.Empty<string>(),
                    Restrictions = dto.Restrictions ?? System.Array.Empty<string>(),
                    Tags = dto.Tags ?? System.Array.Empty<string>(),
                    TimeLimitDays = dto.TimeLimitDays,
                    IsUnlocked = dto.IsUnlocked ?? true
                }
            });
        }

        return StartScenario(dto.Id);
    }

    // ── Scenario dictionary projection ───────────────────────────────────────

    private static Godot.Collections.Dictionary ScenarioToDictionary(ScenarioDefinition s)
    {
        var rules = new Godot.Collections.Dictionary();
        foreach (var kv in s.SpecialRules)
            rules[kv.Key] = kv.Value;

        var special = new Godot.Collections.Array<string>();
        if (s.SpecialRules.ContainsKey("harsh_storms")) special.Add("Harde stormer");
        if (s.SpecialRules.ContainsKey("premium_prices")) special.Add("Premiumpriser");
        if (s.SpecialRules.ContainsKey("start_disease")) special.Add("Aktiv sykdom");
        if (s.SpecialRules.ContainsKey("start_debt")) special.Add("Startgjeld");
        if (s.SpecialRules.ContainsKey("regulation_pressure")) special.Add("Reguleringstrykk");
        if (s.SpecialRules.ContainsKey("calm_weather")) special.Add("Rolig vær");
        if (s.SpecialRules.ContainsKey("easy_market")) special.Add("Stabile priser");
        if (s.SpecialRules.TryGetValue("ai_competitors", out var aiCount)) special.Add($"{aiCount} AI-rivaler");

        return new Godot.Collections.Dictionary
        {
            ["id"] = s.Id,
            ["name"] = s.Name,
            ["description"] = s.Description,
            ["difficulty"] = s.Difficulty,
            ["region"] = s.StartingRegion.ToString(),
            ["starting_capital"] = (double)s.StartingCapitalNOK,
            ["starting_capital_neg"] = s.StartingCapitalNOK < 0m,
            // Extended campaign metadata (backward-compatible additive keys).
            ["starting_cash"] = (double)(s.StartingCash != 0m ? s.StartingCash : s.StartingCapitalNOK),
            ["story"] = string.IsNullOrEmpty(s.Story) ? s.Description : s.Story,
            ["tags"] = ToGdArray(s.Tags),
            ["restrictions"] = ToGdArray(s.Restrictions),
            ["win_condition"] = s.WinCondition.ToString(),
            ["win"] = DescribeWinCondition(s),
            ["time_limit_days"] = s.TimeLimitDays ?? 0,
            ["is_unlocked"] = s.IsUnlocked,
            ["unlocked"] = s.IsUnlocked,
            ["special_rules"] = rules,
            ["special"] = special,
            ["subtitle"] = DifficultyLabel(s.Difficulty),
            ["capital"] = FormatCapital(s.StartingCapitalNOK)
        };
    }

    /// <summary>Project a C# string[] to a Godot Array[String] (empty when null).</summary>
    private static Godot.Collections.Array<string> ToGdArray(string[]? source)
    {
        var arr = new Godot.Collections.Array<string>();
        if (source != null)
            foreach (var value in source)
                if (!string.IsNullOrEmpty(value)) arr.Add(value);
        return arr;
    }

    private static Godot.Collections.Dictionary ModScenarioToDictionary(ModScenario m)
    {
        var rules = new Godot.Collections.Dictionary();
        return new Godot.Collections.Dictionary
        {
            ["id"] = m.Id,
            ["name"] = m.Name,
            ["description"] = m.Description,
            ["difficulty"] = Math.Clamp(m.Difficulty, 1, 5),
            ["region"] = m.StartingRegion,
            ["starting_capital"] = (double)m.StartingCapitalNOK,
            ["starting_capital_neg"] = m.StartingCapitalNOK < 0m,
            ["starting_cash"] = (double)m.StartingCapitalNOK,
            ["story"] = m.Description,
            ["tags"] = new Godot.Collections.Array<string>(),
            ["restrictions"] = new Godot.Collections.Array<string>(),
            ["win_condition"] = m.WinCondition,
            ["win"] = m.WinCondition,
            ["time_limit_days"] = 0,
            ["is_unlocked"] = true,
            ["unlocked"] = true,
            ["special_rules"] = rules,
            ["special"] = new Godot.Collections.Array<string>(),
            ["subtitle"] = "Mod",
            ["capital"] = FormatCapital(m.StartingCapitalNOK),
            ["is_mod"] = true
        };
    }

    private static string DifficultyLabel(int difficulty) => difficulty switch
    {
        1 => "Enkel",
        2 => "Lett",
        3 => "Middels",
        4 => "Vanskelig",
        _ => "Ekspert"
    };

    private static string DescribeWinCondition(ScenarioDefinition s) => s.WinCondition switch
    {
        WinCondition.Tutorial => "Fullfør opplæringen",
        // The win check compares equity with profit_target (ScenarioSystem),
        // so the goal names equity: "overskudd" read as profit on top of the
        // start (ressursrente showed 85 MNOK "overskudd" for a 25 MNOK goal).
        WinCondition.Profit => $"Bygg egenkapitalen til {FormatMillions(RuleDecimal(s, "profit_target", 5_000_000m))}",
        WinCondition.Sustainability => $"Nå bærekraftsmål {RuleDecimal(s, "sustainability_target", 95m):0}",
        WinCondition.Survival => $"Overlev {(s.TimeLimitDays ?? 730)} dager med positiv saldo",
        WinCondition.Competition => $"Nå {FormatMillions(RuleDecimal(s, "profit_target", 10_000_000m))} i egenkapital før konkurrentene",
        _ => s.WinCondition.ToString()
    };

    /// <summary>"85 MNOK", "2,5 MNOK" — nb-NO millions for goal text.</summary>
    private static string FormatMillions(decimal amount)
        => (amount / 1_000_000m).ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("nb-NO")) + " MNOK";

    private static decimal RuleDecimal(ScenarioDefinition s, string key, decimal fallback)
        => s.SpecialRules.TryGetValue(key, out var raw) &&
           decimal.TryParse(raw, System.Globalization.NumberStyles.Any,
               System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value : fallback;

    private static string FormatCapital(decimal amount)
        => $"{amount:N0} NOK".Replace(",", " ").Replace("\u00A0", " ");

    private static int ParseDifficultyElement(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var number))
            return Math.Clamp(number, 1, 5);
        if (el.ValueKind == JsonValueKind.String)
        {
            var raw = (el.GetString() ?? "").Trim().ToLowerInvariant();
            if (int.TryParse(raw, out var parsed)) return Math.Clamp(parsed, 1, 5);
            return raw switch { "easy" => 1, "medium" or "normal" => 3, "hard" => 5, _ => 3 };
        }
        return 3;
    }

    /// <summary>Parsed scenario JSON — union of C# and legacy Godot schemas.</summary>
    private sealed class ScenarioJsonDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")] public string Id { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("name")] public string Name { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("description")] public string Description { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("starting_balance")] public decimal? StartingBalance { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("starting_capital_nok")] public decimal? StartingCapitalNok { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("initial_balance_nok")] public decimal? InitialBalanceNok { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("starting_region")] public string StartingRegion { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("difficulty")] public JsonElement Difficulty { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("win_condition")] public string WinCondition { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("time_limit_days")] public int? TimeLimitDays { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("is_unlocked")] public bool? IsUnlocked { get; set; }
        // Extended campaign schema (additive; defaults are safe for legacy entries).
        [System.Text.Json.Serialization.JsonPropertyName("starting_cash")] public decimal? StartingCash { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("story")] public string? Story { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("tags")] public string[]? Tags { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("restrictions")] public string[]? Restrictions { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("starting_research")] public string[]? StartingResearch { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("starting_buildings")] public string[]? StartingBuildings { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("starting_sites")] public List<StartingSiteDefinition>? StartingSites { get; set; }
    }

    // ── Mod System API for GDScript ──────────────────────────────────────────

    /// <summary>Directory the ModLoader scans (user://mods preferred).</summary>
    public string ModsDirectory => _modsDirectory;

    /// <summary>
    /// Return all discovered mods as an Array of Dictionaries. Keys match
    /// ModManagerPanel.gd: "id", "name", "author", "version", "description",
    /// "enabled", "event_count", "research_count", "species_count",
    /// "scenario_count", "errors" (Array[String]).
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetModList()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        if (_modLoader == null) return result;

        foreach (var manifest in _modLoader.DiscoveredManifests)
        {
            var loaded = _modLoader.LoadedMods.FirstOrDefault(m => m.Manifest.Id == manifest.Id);

            var errors = new Godot.Collections.Array<string>();
            if (loaded != null)
                foreach (var error in loaded.Errors)
                    errors.Add(error);

            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = manifest.Id,
                ["name"] = manifest.Name,
                ["author"] = manifest.Author,
                ["version"] = manifest.Version,
                ["description"] = manifest.Description,
                ["enabled"] = manifest.Enabled,
                ["event_count"] = loaded?.Events.Count ?? 0,
                ["research_count"] = loaded?.ResearchNodes.Count ?? 0,
                ["species_count"] = loaded?.FishSpecies.Count ?? 0,
                ["scenario_count"] = loaded?.Scenarios.Count ?? 0,
                ["errors"] = errors
            });
        }
        return result;
    }

    /// <summary>Re-scan the mods directory and reload all enabled mods.</summary>
    public void ReloadMods()
    {
        _modLoader?.DiscoverAndLoad();
        EmitSignal(SignalName.GameStateUpdated);
    }

    /// <summary>
    /// Persist enable/disable state pushed from ModManagerPanel.gd. Accepts a
    /// Godot Array of {id, enabled} dictionaries (or a single dictionary),
    /// updates the mod config, and reloads.
    /// </summary>
    public bool ApplyModSettings(Godot.Variant settings)
    {
        if (_modLoader == null) return false;

        var entries = new List<(string Id, bool Enabled)>();

        if (settings.VariantType == Variant.Type.Array)
        {
            foreach (var item in settings.AsGodotArray())
            {
                if (item.VariantType != Variant.Type.Dictionary) continue;
                var dict = item.AsGodotDictionary();
                if (dict.ContainsKey("id"))
                    entries.Add((dict["id"].AsString(), dict.ContainsKey("enabled") && dict["enabled"].AsBool()));
            }
        }
        else if (settings.VariantType == Variant.Type.Dictionary)
        {
            var dict = settings.AsGodotDictionary();
            if (dict.ContainsKey("id"))
                entries.Add((dict["id"].AsString(), dict.ContainsKey("enabled") && dict["enabled"].AsBool()));
        }

        foreach (var (id, enabled) in entries)
            _modLoader.SetModEnabled(id, enabled);

        _modLoader.DiscoverAndLoad();
        EmitSignal(SignalName.GameStateUpdated);
        return true;
    }

    // ── Sustainability API (expanded) ────────────────────────────────────────

    /// <summary>
    /// Return detailed sustainability breakdown as a Dictionary.
    /// Keys: "sea_lice" (double), "escape" (double), "feed" (double),
    ///       "carbon" (double), "welfare" (double), "ecosystem" (double).
    /// </summary>
    public Godot.Collections.Dictionary GetSustainabilityBreakdown()
    {
        var result = new Godot.Collections.Dictionary();
        var manager = SimulationEngine?.SustainabilityManager;
        if (manager == null)
        {
            result["sea_lice"] = 0.0;
            result["escape"] = 0.0;
            result["feed"] = 0.0;
            result["carbon"] = 0.0;
            result["welfare"] = 0.0;
            result["ecosystem"] = 0.0;
            return result;
        }

        var breakdown = manager.GetComponentScores(SimulationEngine.State);
        result["sea_lice"] = breakdown["SeaLiceManagement"];
        result["escape"] = breakdown["EscapePrevention"];
        result["feed"] = breakdown["FeedSustainability"];
        result["carbon"] = breakdown["CarbonFootprint"];
        result["welfare"] = breakdown["WelfareStandards"];
        result["ecosystem"] = breakdown["EcosystemImpact"];
        return result;
    }

    /// <summary>
    /// Return the current certification level as a string.
    /// Possible values: "NonCompliant", "Compliant", "ASCCertified", "OrganicCertified".
    /// </summary>
    public string GetCertificationLevel()
        => SimulationEngine?.SustainabilityManager?.CurrentCertification.ToString() ?? "NonCompliant";

    /// <summary>
    /// Return the current price multiplier from certification (e.g. 1.0 = no bonus, 1.15 = +15%).
    /// </summary>
    public double GetPriceMultiplier()
        => (double)(SimulationEngine?.SustainabilityManager?.GetPriceMultiplier(SimulationEngine.SustainabilityManager.CurrentCertification) ?? 1.0m);

    // ── Insurance API (P11) ──────────────────────────────────────────────────

    private static readonly InsuranceKind[] InsuranceKinds =
    {
        InsuranceKind.Mortality, InsuranceKind.Bloom, InsuranceKind.Storm, InsuranceKind.ISA
    };

    private static string InsuranceKindKey(InsuranceKind kind) => kind.ToString().ToLowerInvariant();

    /// <summary>
    /// Per-kind insurance status for the UI:
    /// <c>{ "mortality"|"bloom"|"storm"|"isa": { enabled, premium, coverage, deductible }, "total_premium" }</c>.
    /// Premiums are summed across stacked policies of a kind; coverage/deductible
    /// report the primary (first) policy of that kind.
    /// </summary>
    public Godot.Collections.Dictionary GetInsuranceStatus()
    {
        var result = new Godot.Collections.Dictionary();
        decimal totalPremium = 0m;

        var insurance = SimulationEngine?.Insurance;
        if (insurance == null)
        {
            result["total_premium"] = 0.0;
            return result;
        }

        foreach (var kind in InsuranceKinds)
        {
            var policies = insurance.Policies.Where(p => p.Kind == kind).ToList();
            var primary = policies.Count > 0 ? policies[0] : null;
            decimal premium = policies.Sum(p => p.PremiumNokPerYear);
            totalPremium += premium;

            result[InsuranceKindKey(kind)] = new Godot.Collections.Dictionary
            {
                ["enabled"] = policies.Count > 0,
                ["premium"] = (double)premium,
                ["coverage"] = primary?.CoverageFraction ?? 0.0,
                ["deductible"] = primary != null ? (double)primary.DeductibleNok : 0.0
            };
        }

        result["total_premium"] = (double)totalPremium;
        return result;
    }

    /// <summary>
    /// Enable/disable a catastrophe policy. <paramref name="kind"/> is one of
    /// "mortality" | "bloom" | "storm" | "isa" (case-insensitive). Replaces any
    /// existing policy of that kind while preserving all other kinds. Returns
    /// false for an unknown kind or an uninitialized engine.
    /// </summary>
    public bool SetInsurancePolicy(string kind, bool enabled, double coverage, double deductible)
    {
        var engine = SimulationEngine;
        if (engine == null) return false;

        // Phase 1: the verb lives in the library (SimulationEngine.Commands);
        // the bridge keeps the bool + GameStateUpdated surface.
        var result = engine.SetInsurancePolicy(kind, enabled, coverage, deductible);
        if (result.Ok)
            EmitSignal(SignalName.GameStateUpdated);
        return result.Ok;
    }

    // ── P8 permeable-barrier API (Smoltscreen) ──────────────────────────────

    private static string BarrierKindKey(Farm.BarrierKind kind) => kind switch
    {
        Farm.BarrierKind.Smoltscreen => "smoltscreen",
        _ => "none"
    };

    private static bool TryParseBarrierKind(string kind, out Farm.BarrierKind parsed)
    {
        switch ((kind ?? "").Trim().ToLowerInvariant())
        {
            case "smoltscreen":
            case "smolt_screen":
            case "smolt-screen":
                parsed = Farm.BarrierKind.Smoltscreen; return true;
            case "none":
            case "remove":
            case "":
                parsed = Farm.BarrierKind.None; return true;
            default:
                parsed = Farm.BarrierKind.None; return false;
        }
    }

    /// <summary>P8: approximate tidal flow (m/s) at a site from its zone.</summary>
    private static double SiteFlowMs(SimulationEngine engine, Farm.SeaSite site)
    {
        var zone = engine?.State?.Zones?.FirstOrDefault(z => z.Id == site.ZoneId);
        return zone?.CurrentSpeed ?? Farm.BarrierTech.DefaultFlowMs;
    }

    /// <summary>
    /// Install (or toggle off) a permeable barrier at a site. On install the
    /// barrier's capex is charged; installing when the same kind is already
    /// present removes it (no refund), mirroring the mitigation toggle. Returns
    /// false for an unknown site/kind or when funds are insufficient.
    /// </summary>
    public bool InstallBarrier(string siteId, string kind)
    {
        // F2-03: a permeable barrier is fjord-pen hardware; an offshore unit has none.
        var site = SimulationEngine?.State?.SeaSites?.FirstOrDefault(s => s.Id == siteId && s.Kind == Farm.SiteKind.Fjord);
        if (site == null) return false;
        if (!TryParseBarrierKind(kind, out var parsed)) return false;

        // Toggle off when the same barrier is already installed.
        if (parsed == site.Barrier)
        {
            site.Barrier = Farm.BarrierKind.None;
            site.BarrierDaysSinceCleaning = 0;
            EmitSignal(SignalName.FarmActionCompleted, "install_barrier", siteId, true);
            EmitSignal(SignalName.GameStateUpdated);
            return true;
        }

        // Explicit "none"/"remove" with no barrier installed is a no-op success.
        if (parsed == Farm.BarrierKind.None)
        {
            site.Barrier = Farm.BarrierKind.None;
            site.BarrierDaysSinceCleaning = 0;
            EmitSignal(SignalName.FarmActionCompleted, "install_barrier", siteId, true);
            EmitSignal(SignalName.GameStateUpdated);
            return true;
        }

        decimal cost = Farm.BarrierTech.SpecFor(parsed).CapexNok;
        if (SimulationEngine!.FinanceManager.Balance < cost)
        {
            EmitSignal(SignalName.FarmActionCompleted, "install_barrier", siteId, false);
            return false;
        }

        SimulationEngine.PayCost(cost, LakskongenSim.Core.CostBooking.Capex, "barriers");
        site.Barrier = parsed;
        site.BarrierDaysSinceCleaning = 0;

        SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = SimulationEngine.State.Day,
            Name = $"{EnumText("ENUM_BARRIER", parsed)} installert",
            Category = LakskongenSim.Events.EventCategory.Equipment,
            Description = LakskongenSim.Core.SimText.Nb($"{EnumText("ENUM_BARRIER", parsed)} på {SiteLabel(siteId)} ({cost:N0} kr)"),
            BalanceChange = -cost
        });

        EmitSignal(SignalName.FarmActionCompleted, "install_barrier", siteId, true);
        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: {siteId} barrier → {parsed} ({cost:N0} NOK)");
        return true;
    }

    /// <summary>
    /// P8: clean an installed barrier, resetting biofouling decay. Charges the
    /// barrier's standing (weekly) opex as a one-off cleaning service. Returns
    /// false when no barrier is installed or funds are insufficient.
    /// </summary>
    public bool CleanBarrier(string siteId)
    {
        var site = SimulationEngine?.State?.SeaSites?.FirstOrDefault(s => s.Id == siteId);
        if (site == null || site.Barrier == Farm.BarrierKind.None) return false;

        decimal cost = Farm.BarrierTech.SpecFor(site.Barrier).WeeklyOpexNok;
        if (SimulationEngine!.FinanceManager.Balance < cost) return false;
        SimulationEngine.PayCost(cost, LakskongenSim.Core.CostBooking.Opex, "barriers");
        site.BarrierDaysSinceCleaning = 0;

        SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = SimulationEngine.State.Day,
            Name = "Barriere rengjort",
            Category = LakskongenSim.Events.EventCategory.Equipment,
            Description = $"{EnumText("ENUM_BARRIER", site.Barrier)} rengjort på {site.Label}",
            BalanceChange = -cost
        });

        EmitSignal(SignalName.FarmActionCompleted, "clean_barrier", siteId, true);
        EmitSignal(SignalName.GameStateUpdated);
        return true;
    }

    /// <summary>
    /// P8: per-site permeable-barrier status. Keys: "site_id", "kind",
    /// "installed", "flow_ms", "lice_blocking", "jellyfish_blocking",
    /// "days_since_cleaning", "cleaning_due_in_days", "capex_nok",
    /// "weekly_opex_nok", "eligible_environmental_flexibility", "rationale".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetBarrierStatus()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var sites = SimulationEngine?.State?.SeaSites;
        if (sites == null) return result;

        foreach (var site in sites)
        {
            if (site.Kind != Farm.SiteKind.Fjord) continue; // F2-03: barriers are fjord-pen hardware
            var spec = Farm.BarrierTech.SpecFor(site.Barrier);
            double flow = SiteFlowMs(SimulationEngine, site);
            int days = site.BarrierDaysSinceCleaning;
            int dueIn = site.Barrier != Farm.BarrierKind.None
                ? spec.CleaningIntervalDays - days
                : 0;
            result.Add(new Godot.Collections.Dictionary
            {
                ["site_id"] = site.Id,
                ["kind"] = BarrierKindKey(site.Barrier),
                ["installed"] = site.Barrier != Farm.BarrierKind.None,
                ["flow_ms"] = flow,
                ["lice_blocking"] = Farm.BarrierTech.EffectiveLiceBlocking(site.Barrier, flow, days),
                ["jellyfish_blocking"] = Farm.BarrierTech.EffectiveJellyfishBlocking(site.Barrier, flow, days),
                ["days_since_cleaning"] = days,
                ["cleaning_due_in_days"] = dueIn,
                ["capex_nok"] = (double)spec.CapexNok,
                ["weekly_opex_nok"] = (double)spec.WeeklyOpexNok,
                ["eligible_environmental_flexibility"] = Farm.BarrierTech.IsEligibleForEnvironmentalFlexibility(site.Barrier),
                ["rationale"] = Farm.BarrierTech.EnvironmentalFlexibilityRationale(site.Barrier)
            });
        }
        return result;
    }

    /// <summary>
    /// Build a new sea site in the specified zone.
    /// Phase 4.1: the whole gate + charge + licence consumption lives in the
    /// engine (<c>SimulationEngine.BuildSeaSite</c>) — this method is the
    /// Godot-facing wrapper keeping the legacy bool + signal contract.
    /// Returns true on success, false if the gate failed (zone not found,
    /// growth licence required, insufficient funds).
    /// </summary>
    public bool BuildSeaSite(string zoneId, string name)
    {
        if (SimulationEngine?.State?.Zones == null || SimulationEngine?.State?.SeaSites == null)
        {
            GD.PrintErr("GameBridge: BuildSeaSite failed - engine not initialized");
            EmitSignal(SignalName.FarmActionCompleted, "build_site", zoneId, false);
            return false;
        }

        var outcome = SimulationEngine.BuildSeaSite(zoneId, name);
        if (!outcome.Ok)
        {
            GD.PrintErr($"GameBridge: BuildSeaSite failed - {outcome.ErrorKey}: {outcome.Message}");
            EmitSignal(SignalName.FarmActionCompleted, "build_site", zoneId, false);
            return false;
        }

        EmitSignal(SignalName.FarmActionCompleted, "build_site", zoneId, true);
        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: {outcome.Message}");
        return true;
    }

    /// <summary>
    /// Active blooms for UI (Phase 4.3).
    /// Keys: "id", "kind", "site_id", "severity", "days_left".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetActiveBlooms()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var blooms = SimulationEngine?.State?.ActiveBlooms;
        if (blooms == null) return result;
        int today = SimulationEngine?.State?.Day ?? 0;
        foreach (var b in blooms)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = b.Id,
                ["kind"] = b.Kind,
                ["site_id"] = b.SiteId,
                ["severity"] = b.Severity01,
                ["days_left"] = b.DaysLeft(today)
            });
        }
        return result;
    }

    /// <summary>
    /// Maximum bloom severity (0-1) across all active blooms.
    /// Used to drive the global algae_density water shader parameter.
    /// </summary>
    public double GetMaxBloomSeverity()
    {
        var blooms = SimulationEngine?.State?.ActiveBlooms;
        if (blooms == null || blooms.Count == 0) return 0.0;
        double max = 0.0;
        foreach (var b in blooms)
        {
            if (b.Severity01 > max) max = b.Severity01;
        }
        return Math.Min(max, 1.0);
    }

    /// <summary>
    /// QA-only: inject an active algal bloom at a site (the same state the
    /// scenario starting-bloom path creates). Deterministic id from site+day,
    /// no RNG. Used by the screenshot harness to force the bloom visual state;
    /// not reachable from player UI.
    /// </summary>
    public Godot.Collections.Dictionary SpawnQaBloom(string siteId, double severity01, int durationDays = 21)
    {
        var result = new Godot.Collections.Dictionary();
        var state = SimulationEngine?.State;
        var site = state?.SeaSites?.FirstOrDefault(s => s.Id == siteId);
        if (state == null || site == null)
        {
            result["success"] = false;
            result["message"] = state == null ? "engine_not_ready" : "site_not_found";
            return result;
        }

        state.ActiveBlooms ??= new List<LakskongenSim.Environment.BloomEvent>();
        string bloomId = $"qa_bloom_{siteId}_{state.Day}";
        bool existed = state.ActiveBlooms.Any(b => b.Id == bloomId);
        if (!existed)
        {
            state.ActiveBlooms.Add(new LakskongenSim.Environment.BloomEvent
            {
                Id = bloomId,
                Kind = "algae",
                SiteId = siteId,
                Severity01 = Math.Clamp(severity01, 0.0, 1.0),
                StartDay = state.Day,
                DurationDays = Math.Max(1, durationDays),
            });
        }
        EmitSignal(SignalName.GameStateUpdated);
        result["success"] = true;
        result["message"] = existed ? "already_active" : "bloom_spawned";
        result["id"] = bloomId;
        result["severity"] = Math.Clamp(severity01, 0.0, 1.0);
        return result;
    }

    /// <summary>
    /// QA-only: set the sea-lice load on every pen of a site (the value the
    /// per-site lice model evolves and the collar tint reads). Deterministic,
    /// used by the screenshot harness to force the red-zone readout when the
    /// scenario's semi-closed pens suppress natural growth. Not player-reachable.
    /// </summary>
    public Godot.Collections.Dictionary SetQaPenLice(string siteId, double licePerFish)
    {
        var result = new Godot.Collections.Dictionary();
        var state = SimulationEngine?.State;
        var site = state?.SeaSites?.FirstOrDefault(s => s.Id == siteId);
        if (state == null || site == null)
        {
            result["success"] = false;
            result["message"] = state == null ? "engine_not_ready" : "site_not_found";
            return result;
        }

        double clamped = Math.Clamp(licePerFish, 0.0, 10.0);
        foreach (var pen in site.Pens)
            pen.SeaLiceLoad = clamped;
        // The site model is what evolves daily and what the company KPI and its
        // alerts read; split like SeaLiceModel.Reset.
        site.Lice.ChalimusPerFish = clamped * 0.5;
        site.Lice.PreAdultPerFish = clamped * 0.3;
        site.Lice.AdultFemalePerFish = clamped * 0.1;
        site.Lice.AdultMalePerFish = clamped * 0.1;

        EmitSignal(SignalName.GameStateUpdated);
        result["success"] = true;
        result["message"] = "lice_set";
        result["site"] = siteId;
        result["lice_per_fish"] = clamped;
        return result;
    }

    /// <summary>
    /// QA-only: clear ALL active blooms (the paired control for the bloom
    /// readout screenshot — clear water vs tinted water). Not player-reachable.
    /// </summary>
    public Godot.Collections.Dictionary ClearQaBlooms()
    {
        var result = new Godot.Collections.Dictionary();
        var state = SimulationEngine?.State;
        if (state == null)
        {
            result["success"] = false;
            result["message"] = "engine_not_ready";
            return result;
        }

        int removed = state.ActiveBlooms?.Count ?? 0;
        state.ActiveBlooms?.Clear();
        EmitSignal(SignalName.GameStateUpdated);
        result["success"] = true;
        result["message"] = "blooms_cleared";
        result["removed"] = removed;
        return result;
    }

    /// <summary>Largest grant <see cref="QaGrantCash"/> books at once (NOK): a Havfarm-class build with room to spare.</summary>
    private const double QaGrantCashMaxNok = 5_000_000_000.0;

    /// <summary>
    /// QA-only (F2-03): book <paramref name="nok"/> as other income so a seeded
    /// scenario (a few million in the bank) can afford the facility build a UI
    /// flow drives, e.g. an offshore unit next to the fjord's pens. Refuses a
    /// non-finite, non-positive or larger-than-5-billion amount. Not reachable
    /// from player UI (UiFlowTests calls it on the autoload).
    /// </summary>
    public bool QaGrantCash(double nok)
    {
        var engine = SimulationEngine;
        if (engine?.State == null || engine.FinanceManager == null) return false;
        if (!double.IsFinite(nok) || nok <= 0.0 || nok > QaGrantCashMaxNok) return false;
        engine.ReceiveIncome((decimal)nok, "qa_grant");
        EmitSignal(SignalName.GameStateUpdated);
        return true;
    }

    /// <summary>
    /// AI competitor companies for grey farm visuals (Phase 3).
    /// Keys: "id", "name", "market_share", "position_x", "position_z", "strategy",
    /// "market_cap", "home_po_sites" (geo track A: the company's producing sea
    /// grow-out localities in the home production area). Ordered by
    /// home_po_sites, most first, then roster order (a stable sort read from
    /// the cached rival layout), so the 3D fjord shows the rivals that farm
    /// the player's own waters.
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetCompetitorCompanies()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var competitors = SimulationEngine?.CompetitorAI?.Competitors;
        if (competitors == null || competitors.Count == 0) return result;
        var ordered = SimulationEngine.CompetitorsByHomePresence();

        // Assign fixed visual positions around the fjord for each competitor
        // These are synthetic positions for visual representation only
        var positions = new List<Vector3>
        {
            new Vector3(-800, 0, -400),   // SW
            new Vector3(800, 0, -300),    // SE
            new Vector3(-600, 0, 200),    // NW
            new Vector3(600, 0, 300),     // NE
            new Vector3(0, 0, -600),      // South
            new Vector3(-1000, 0, 500),   // Far NW
            new Vector3(1000, 0, 600),    // Far NE
            new Vector3(-400, 0, 700),    // North
        };
        
        int posIndex = 0;
        foreach (var presence in ordered)
        {
            var c = presence.Company;
            var pos = positions[posIndex % positions.Count];
            posIndex++;

            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = c.Id,
                ["name"] = c.Name,
                ["market_share"] = (double)c.MarketShareNorway,
                ["position_x"] = pos.X,
                ["position_z"] = pos.Z,
                ["strategy"] = c.Strategy.ToString(),
                ["market_cap"] = (double)c.MarketCap,
                ["home_po_sites"] = presence.HomePoSites,
            });
        }
        return result;
    }

    /// <summary>
    /// Deploy a bloom countermeasure (Phase 4.3): "bubble" (1.8M + 90k/wk,
    /// jelly ×0.23) or "diffuser" (2.4M + 120k/wk, hypoxia ×0.3, algae ×0.7).
    /// Toggling an active measure OFF is free.
    /// </summary>
    public bool DeployCountermeasure(string siteId, string kind)
    {
        // F2-03: the bloom deck spawns in the fjord's zones; an offshore unit meets none.
        var site = SimulationEngine?.State?.SeaSites?.FirstOrDefault(s => s.Id == siteId && s.Kind == Farm.SiteKind.Fjord);
        if (site == null) return false;
        bool isBubble = (kind ?? "").ToLower() == "bubble";
        if (!isBubble && (kind ?? "").ToLower() != "diffuser") return false;
        bool active = isBubble ? site.HasBubbleCurtain : site.HasO2Diffuser;
        if (!active)
        {
            decimal cost = isBubble ? 1_800_000m : 2_400_000m;
            if (SimulationEngine!.FinanceManager.Balance < cost)
            {
                EmitSignal(SignalName.FarmActionCompleted, "countermeasure", siteId, false);
                return false;
            }
            SimulationEngine.PayCost(cost, LakskongenSim.Core.CostBooking.Capex, "bloom_countermeasures");
            if (isBubble) site.HasBubbleCurtain = true; else site.HasO2Diffuser = true;
            SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
            {
                Day = SimulationEngine.State.Day,
                Name = isBubble ? "Boblegardin installert" : "Oksygendiffusor installert",
                Category = LakskongenSim.Events.EventCategory.Equipment,
                Description = $"{(isBubble ? "Boblegardin" : "Oksygendiffusor")} på {site.Label}",
                BalanceChange = -cost
            });
        }
        else if (isBubble) site.HasBubbleCurtain = false;
        else site.HasO2Diffuser = false;

        EmitSignal(SignalName.FarmActionCompleted, "countermeasure", siteId, true);
        EmitSignal(SignalName.GameStateUpdated);
        return true;
    }
    /// <summary>Vaccinated share of live fish 0-1 (Phase 5.1).</summary>
    public double VaccineCoverage
    {
        get
        {
            var state = SimulationEngine?.State;
            return state != null ? LakskongenSim.Health.VaccinationProgram.Coverage01(state) : 0.0;
        }
    }

    /// <summary>
    /// October window days left, -1 when closed (Phase 5.1). WindowDaysLeft
    /// counts from the window's start before it opens (30 from January to
    /// September), so the "vaccination window open" alert showed all year
    /// (review F763).
    /// </summary>
    public int VaccineWindowDaysLeft
    {
        get
        {
            int day = SimulationEngine?.State?.Day ?? 0;
            return LakskongenSim.Health.VaccinationProgram.IsWindowOpen(day)
                ? LakskongenSim.Health.VaccinationProgram.WindowDaysLeft(day)
                : -1;
        }
    }

    // ── Phase 5.3: Sludge Valorization ───────────────────────────────────────────

    /// <summary>
    /// Get sludge status for all sea sites.
    /// Keys per site: "id", "accumulated_kg_ds", "valorized_kg_ds", "last_valorization_day",
    ///                 "has_hub_connection", "projected_daily_kg_ds".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetSludgeStatus()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var sites = SimulationEngine?.State?.SeaSites;
        if (sites == null) return result;

        foreach (var site in sites)
        {
            // Project daily sludge based on recent feed usage
            double dailyFeedKg = 0.0;
            var pops = site.Pens;
            foreach (var pop in pops)
            {
                // Approximate from biomass - rough estimate 1.5% body weight/day
                dailyFeedKg += pop.GetTotalBiomassKg() * 0.015;
            }
            var containmentMods = Farm.SludgeValorization.ContainmentCollectionMult.TryGetValue(site.Containment, out var m) ? m : Farm.SludgeValorization.Default.CollectionEfficiency;
            double projectedDaily = dailyFeedKg / 1000.0 * Farm.SludgeValorization.Default.KgDrySolidsPerTonneFeed * containmentMods;

            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = site.Id,
                ["accumulated_kg_ds"] = site.SludgeAccumulatedKgDs,
                ["valorized_kg_ds"] = site.SludgeValorizedKgDs,
                ["last_valorization_day"] = site.LastSludgeValorizationDay,
                ["has_hub_connection"] = site.HasSludgeHubConnection,
                ["projected_daily_kg_ds"] = projectedDaily
            });
        }
        return result;
    }

    /// <summary>
    /// Toggle centralized sludge hub connection for a site (Phase 5.3).
    /// CAPEX 40M NOK (shared hub), enables weekly auto-valorization at better efficiency.
    /// Returns true on success.
    /// </summary>
    public bool ToggleSludgeHub(string siteId)
    {
        var site = SimulationEngine?.State?.SeaSites?.FirstOrDefault(s => s.Id == siteId);
        if (site == null) return false;

        if (!site.HasSludgeHubConnection)
        {
            // Check if hub already built elsewhere or build new
            bool hubExists = SimulationEngine.State.SeaSites.Any(s => s.HasSludgeHubConnection);
            decimal cost = hubExists ? 5_000_000m : 40_000_000m; // Connection vs new hub
            if (SimulationEngine!.FinanceManager.Balance < cost) return false;
            SimulationEngine.PayCost(cost, LakskongenSim.Core.CostBooking.Capex, "sludge");
            site.HasSludgeHubConnection = true;

            SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
            {
                Day = SimulationEngine.State.Day,
                Name = hubExists ? "Koblet til slamanlegget" : "Slamanlegg bygget",
                Category = LakskongenSim.Events.EventCategory.Equipment,
                Description = LakskongenSim.Core.SimText.Nb($"{site.Label} er koblet til det felles slamanlegget ({cost:N0} kr)"),
                BalanceChange = -cost
            });
        }
        else
        {
            site.HasSludgeHubConnection = false;
            SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
            {
                Day = SimulationEngine.State.Day,
                Name = "Koblet fra slamanlegget",
                Category = LakskongenSim.Events.EventCategory.Equipment,
                Description = $"{site.Label} er koblet fra det felles slamanlegget",
                BalanceChange = 0m
            });
        }

        EmitSignal(SignalName.GameStateUpdated);
        return true;
    }

    /// <summary>
    /// Manually trigger sludge valorization at a site (Phase 5.3).
    /// Returns net revenue (biogas + fertilizer - opex) in NOK. Returns double:
    /// Godot cannot marshal decimal, so with a decimal return the method was
    /// never registered and the panel's "Valorisér nå" button did nothing (F947).
    /// </summary>
    public double ValorizeSludgeNow(string siteId)
    {
        var site = SimulationEngine?.State?.SeaSites?.FirstOrDefault(s => s.Id == siteId);
        if (site == null) return 0.0;
        if (site.SludgeAccumulatedKgDs < 100.0) return 0.0;

        var sludgeParams = Farm.SludgeValorization.Default;
        var (biogasRev, fertRev, opex) = Farm.SludgeValorization.Valorize(site.SludgeAccumulatedKgDs, sludgeParams);
        decimal netRev = biogasRev + fertRev - opex;

        if (netRev > 0)
        {
            SimulationEngine!.ReceiveIncome(netRev, "sludge");
            SimulationEngine.State.TotalSalesRevenueNok += netRev; // as the weekly valorization books it
        }
        else
            SimulationEngine!.PayCost(-netRev, LakskongenSim.Core.CostBooking.Opex, "sludge");

        site.SludgeValorizedKgDs += site.SludgeAccumulatedKgDs;
        site.LastSludgeValorizationDay = SimulationEngine!.State.Day;

        SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = SimulationEngine.State.Day,
            Name = "Slamgjenvinning",
            Category = LakskongenSim.Events.EventCategory.Equipment,
            Description = LakskongenSim.Core.SimText.Nb($"{site.Label}: {site.SludgeAccumulatedKgDs:F0} kg tørrstoff → biogass {biogasRev:N0} + gjødsel {fertRev:N0} − drift {opex:N0} = {netRev:+#,0;-#,0;0} kr"),
            BalanceChange = netRev
        });
        SimulationEngine.State.TrimEventLog();

        site.SludgeAccumulatedKgDs = 0.0;
        EmitSignal(SignalName.GameStateUpdated);
        return (double)netRev;
    }

    // ── Phase 5.3: Cohort Transfers ──────────────────────────────────────────────

    /// <summary>
    /// Transfer reason enum for UI.
    /// </summary>
    public enum TransferReason
    {
        Grading,
        Fallowing,
        MabManagement,
        DiseaseIsolation,
        OffshoreStocking,
        ContainmentUpgrade
    }

    /// <summary>
    /// Plan a cohort transfer between sites (Phase 5.3; F2-13: a thin call into
    /// SimulationEngine.PlanCohortTransfer). Returns the plan with cost,
    /// mortality estimate and ETA; does not execute.
    /// Keys: "source_id", "dest_id", "fish_count", "mean_weight_kg", "reason",
    ///       "eta_days", "estimated_cost_nok", "estimated_mortality_pct", "requires_wellboat".
    /// </summary>
    public Godot.Collections.Dictionary PlanCohortTransfer(
        string sourceSiteId, string destSiteId, int fishCount, double meanWeightKg, string reasonStr)
    {
        var result = new Godot.Collections.Dictionary();
        if (SimulationEngine?.State == null) { result["error"] = "Engine not initialized"; return result; }
        var plan = SimulationEngine.PlanCohortTransfer(sourceSiteId, destSiteId, fishCount, meanWeightKg, reasonStr);
        if (plan == null) { result["error"] = "Invalid reason"; return result; }
        result["source_id"] = plan.SourceSiteId;
        result["dest_id"] = plan.DestSiteId;
        result["fish_count"] = plan.FishCount;
        result["mean_weight_kg"] = plan.MeanWeightKg;
        result["reason"] = plan.Reason.ToString();
        result["eta_days"] = plan.EtaDays;
        result["estimated_cost_nok"] = (double)plan.EstimatedCostNok;
        result["estimated_mortality_pct"] = plan.EstimatedMortalityPct;
        result["requires_wellboat"] = plan.RequiresWellboat;
        return result;
    }

    /// <summary>
    /// Execute a planned cohort transfer (Phase 5.3; F2-13: a thin call into
    /// SimulationEngine.ExecuteCohortTransfer, which moves the fish and books
    /// the cost: no money, fish or random source in the bridge).
    /// Returns: "success", "fish_transferred", "fish_lost", "actual_cost_nok", "message".
    /// </summary>
    public Godot.Collections.Dictionary ExecuteCohortTransfer(
        string sourceSiteId, string destSiteId, int fishCount, double meanWeightKg, string reasonStr)
    {
        var result = new Godot.Collections.Dictionary();
        if (SimulationEngine?.State == null) { result["success"] = false; result["message"] = "Engine not initialized"; return result; }
        var outcome = SimulationEngine.ExecuteCohortTransfer(sourceSiteId, destSiteId, fishCount, meanWeightKg, reasonStr);
        result["success"] = outcome.Ok;
        result["fish_transferred"] = outcome.Ok ? (int)outcome.Value : 0;
        result["fish_lost"] = outcome.Ok ? (int)outcome.Value2 : 0;
        result["actual_cost_nok"] = outcome.Ok
            ? (double)(SimulationEngine.PlanCohortTransfer(sourceSiteId, destSiteId, fishCount, meanWeightKg, reasonStr)?.EstimatedCostNok ?? 0m)
            : outcome.Value;
        result["message"] = outcome.Message;
        if (outcome.ErrorKey == "invalid_reason") result["message"] = "Invalid reason";
        EmitSignal(SignalName.GameStateUpdated);
        return result;
    }

    /// <summary>
    /// Get valid destination sites for a transfer from source (Phase 5.3).
    /// Keys per site: "id", "name", "zone", "current_biomass_kg", "mab_tonnes", "available_mab_kg".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetTransferDestinations(string sourceSiteId, double incomingBiomassKg)
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var state = SimulationEngine?.State;
        if (state == null) return result;

        var dests = Farm.CohortTransferSystem.GetValidDestinations(sourceSiteId, state, incomingBiomassKg);
        foreach (var site in dests)
        {
            double currentBio = site.Pens.Sum(p => p.GetTotalBiomassKg());
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = site.Id,
                ["name"] = site.Label,
                ["zone"] = site.ZoneId,
                ["current_biomass_kg"] = currentBio,
                ["mab_tonnes"] = site.MABTonnes,
                ["available_mab_kg"] = site.MABTonnes * 1000.0 - currentBio
            });
        }
        return result;
    }

    // ── Phase 5.4: Carbon Ledger ─────────────────────────────────────────────────

    /// <summary>
    /// Get aggregated carbon ledger for all sites and facilities (Phase 5.4).
    /// Returns: "sites" (array), "offshore" (array), "ras" (array), "processing" (dict),
    ///          "total_emissions_kg", "total_credits_kg", "net_emissions_kg",
    ///          "scope1_kg", "scope2_kg", "scope3_kg", "this_year_net_kg".
    /// </summary>
    public Godot.Collections.Dictionary GetCarbonLedger()
    {
        var result = new Godot.Collections.Dictionary();
        var state = SimulationEngine?.State;
        if (state == null) return result;

        var sitesArray = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        double totalEmissions = 0, totalCredits = 0;
        double scope1 = 0, scope2 = 0, scope3 = 0;

        // Sea sites (F2-03: the fjord localities; an offshore unit's carbon is in "offshore")
        foreach (var site in state.SeaSites)
        {
            if (site.Kind != Farm.SiteKind.Fjord) continue;
            var tracker = site.CarbonTracker;
            double em = tracker.TotalEmissions();
            double cr = tracker.TotalCredits();
            totalEmissions += em;
            totalCredits += cr;
            var breakdown = tracker.ScopeBreakdown(0, state.Day);
            scope1 += breakdown["Scope1"];
            scope2 += breakdown["Scope2"];
            scope3 += breakdown["Scope3"];

            var topSources = new Godot.Collections.Array();
            foreach (var t in tracker.TopSources(0, state.Day, 3))
                topSources.Add(new Godot.Collections.Dictionary { ["source"] = t.Source, ["kg"] = t.KgCO2e });
            sitesArray.Add(new Godot.Collections.Dictionary
            {
                ["id"] = site.Id,
                ["emissions_kg"] = em,
                ["credits_kg"] = cr,
                ["net_kg"] = em - cr,
                ["scope1_kg"] = breakdown["Scope1"],
                ["scope2_kg"] = breakdown["Scope2"],
                ["scope3_kg"] = breakdown["Scope3"],
                ["this_year_net_kg"] = tracker.NetThisYear(state.Day),
                ["top_sources"] = topSources
            });
        }

        // Offshore units (F2-03: offshore sea sites; the carbon is on the site's tracker)
        var offshoreArray = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var off in SimulationEngine!.OffshoreUnits)
        {
            if (off.Offshore?.IsOperational != true) continue;
            var tracker = off.CarbonTracker;
            double em = tracker.TotalEmissions();
            double cr = tracker.TotalCredits();
            totalEmissions += em;
            totalCredits += cr;
            var breakdown = tracker.ScopeBreakdown(0, state.Day);
            scope1 += breakdown["Scope1"];
            scope2 += breakdown["Scope2"];
            scope3 += breakdown["Scope3"];

            offshoreArray.Add(new Godot.Collections.Dictionary
            {
                ["id"] = off.Id,
                ["name"] = off.Label,
                ["emissions_kg"] = em,
                ["credits_kg"] = cr,
                ["net_kg"] = em - cr,
                ["scope1_kg"] = breakdown["Scope1"],
                ["scope2_kg"] = breakdown["Scope2"],
                ["scope3_kg"] = breakdown["Scope3"],
                ["this_year_net_kg"] = tracker.NetThisYear(state.Day)
            });
        }

        // RAS facilities
        var rasArray = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var ras in SimulationEngine!.RASFacilities)
        {
            if (!ras.IsOperational) continue;
            var tracker = ras.CarbonTracker;
            double em = tracker.TotalEmissions();
            double cr = tracker.TotalCredits();
            totalEmissions += em;
            totalCredits += cr;
            var breakdown = tracker.ScopeBreakdown(0, state.Day);
            scope1 += breakdown["Scope1"];
            scope2 += breakdown["Scope2"];
            scope3 += breakdown["Scope3"];

            rasArray.Add(new Godot.Collections.Dictionary
            {
                ["id"] = ras.Id,
                ["name"] = ras.Name,
                ["emissions_kg"] = em,
                ["credits_kg"] = cr,
                ["net_kg"] = em - cr,
                ["scope1_kg"] = breakdown["Scope1"],
                ["scope2_kg"] = breakdown["Scope2"],
                ["scope3_kg"] = breakdown["Scope3"],
                ["this_year_net_kg"] = tracker.NetThisYear(state.Day)
            });
        }

        // Processing plant (virtual)
        var procDict = new Godot.Collections.Dictionary();
        if (state.OwnsProcessingPlant && Enum.TryParse<Processing.ProcessingTier>(state.ProcessingTier, out var tier))
        {
            var firstSite = state.SeaSites.FirstOrDefault();
            if (firstSite != null)
            {
                var tracker = firstSite.CarbonTracker;
                double em = tracker.Emissions.Where(e => e.Source == "processing_plant").Sum(e => e.KgCO2e);
                double cr = tracker.Credits.Where(c => c.Source == "processing_plant").Sum(c => c.KgCO2e);
                procDict["emissions_kg"] = em;
                procDict["credits_kg"] = cr;
                procDict["net_kg"] = em - cr;
                procDict["tier"] = state.ProcessingTier;
            }
        }

        result["sites"] = sitesArray;
        result["offshore"] = offshoreArray;
        result["ras"] = rasArray;
        result["processing"] = procDict;
        result["total_emissions_kg"] = totalEmissions;
        result["total_credits_kg"] = totalCredits;
        result["net_emissions_kg"] = totalEmissions - totalCredits;
        result["scope1_kg"] = scope1;
        result["scope2_kg"] = scope2;
        result["scope3_kg"] = scope3;
        result["this_year_net_kg"] = state.SeaSites.Sum(s => s.CarbonTracker.NetThisYear(state.Day)) +
                                     SimulationEngine!.RASFacilities.Sum(r => r.CarbonTracker.NetThisYear(state.Day));

        return result;
    }

    /// <summary>
    /// Get carbon intensity per kg harvested (kg CO2e/kg HOG).
    /// </summary>
    public double GetCarbonIntensity()
    {
        var state = SimulationEngine?.State;
        if (state == null) return 0.0;

        double totalHarvestedKg = state.EventLog
            .Where(e => e.Category == Events.EventCategory.Market && e.Name == "Harvest")
            .Sum(e => Math.Abs((double)e.BalanceChange) / 80.0); // Rough: revenue / 80 NOK/kg
        
        if (totalHarvestedKg <= 0) return 0.0;

        double totalEmissions = state.SeaSites.Sum(s => s.CarbonTracker.TotalEmissions()) +
                                SimulationEngine!.RASFacilities.Sum(r => r.CarbonTracker.TotalEmissions());
        
        return totalEmissions / totalHarvestedKg;
    }

    /// <summary>
    /// Check if carbon-neutral certification threshold met (net <= 0 for year).
    /// </summary>
    public bool IsCarbonNeutralYear()
    {
        var state = SimulationEngine?.State;
        if (state == null) return false;
        
        double netThisYear = state.SeaSites.Sum(s => s.CarbonTracker.NetThisYear(state.Day)) +
                             SimulationEngine!.RASFacilities.Sum(r => r.CarbonTracker.NetThisYear(state.Day));
        
        return netThisYear <= 0;
    }

    // ── Phase 3 depth exposure (P7/P9/P10) ──────────────────────────────────────

    /// <summary>P9: binding monthly pen price (NOK/kg) set by the Price Council.</summary>
    public double PenPrice => (double)(SimulationEngine?.PenPriceCouncil?.MonthlyPenPrice ?? 0m);

    /// <summary>P9: whether the 25% resource-rent tax is enabled for the active scenario.</summary>
    public bool ResourceRentEnabled => SimulationEngine?.EnableResourceRentTax ?? false;

    /// <summary>P9: cumulative resource-rent tax paid (NOK).</summary>
    public double ResourceRentPaid => (double)(SimulationEngine?.ResourceRentPaid ?? 0m);

    /// <summary>
    /// P10: gill-health early-warning risk (0-1) for a site.
    /// Keys: "site_id", "risk", "warning" (bool), "critical" (bool), "reason".
    /// </summary>
    public Godot.Collections.Dictionary GetGillRisk(string siteId)
    {
        var res = new Godot.Collections.Dictionary
        {
            ["site_id"] = siteId, ["risk"] = 0.0, ["warning"] = false,
            ["critical"] = false, ["reason"] = "no data"
        };
        var engine = SimulationEngine;
        var site = engine?.State?.SeaSites?.FirstOrDefault(s => s.Id == siteId);
        if (site == null) return res;

        double temp = engine!.SeaTemperatureAtSite(site);   // the site's own water (S2-02)
        double gill = site.GillScore;
        // Derive inputs we can trust; gill score is a direct reverse proxy for risk.
        double risk = System.Math.Clamp(1.0 - gill / 100.0, 0.0, 1.0);
        var r = LakskongenSim.Health.GillHealthWarning.Evaluate(temp, 8.0, 5.0, 0.2, site.GillScore < 60 ? 0.5 : 0.0);
        // Blend the model risk with the live gill score so both matter.
        risk = System.Math.Clamp((risk + r.Risk01) * 0.5, 0.0, 1.0);
        res["risk"] = risk;
        res["warning"] = risk >= 0.5;
        res["critical"] = risk >= 0.75;
        res["reason"] = r.Reason;
        return res;
    }

    /// <summary>P7: does a site's containment qualify for red-zone capacity recovery?</summary>
    public bool QualifiesForEnvironmentalFlexibility(string siteId)
    {
        var engine = SimulationEngine;
        var site = engine?.State?.SeaSites?.FirstOrDefault(s => s.Id == siteId);
        if (site == null || engine?.TrafficLightSystem == null) return false;
        var color = engine.TrafficLightSystem.GetZoneColor(site.ZoneId);
        return LakskongenSim.Farm.EnvironmentalFlexibility.QualifiesForRedZoneRecovery(site.Containment, color);
    }

}
