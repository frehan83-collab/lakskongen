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

        foreach (var scenario in system.GetAll())
            result.Add(ScenarioToDictionary(scenario));

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
    public bool StartScenario(string scenarioId)
    {
        if (string.IsNullOrWhiteSpace(scenarioId) || SimulationEngine == null)
            return false;

        // Not running yet (e.g. launched straight from the campaign menu):
        // validate against the roster, then initialize the engine first.
        if (!_gameRunning || SimulationEngine.Scenario == null)
        {
            var probe = SimulationEngine.Scenario ?? new ScenarioSystem(SimulationEngine.State);
            var candidate = probe.GetScenario(scenarioId);
            if (candidate == null || !candidate.IsUnlocked)
            {
                GD.Print($"GameBridge.StartScenario: '{scenarioId}' unavailable (unknown or locked)");
                return false;
            }
            StartNewGame("Player", (int)candidate.StartingCapitalNOK);
        }

        var scenario = SimulationEngine.Scenario!.GetScenario(scenarioId);
        if (scenario == null) return false;

        try
        {
            SimulationEngine.Scenario!.StartScenario(scenarioId);
            // Materialize the scenario's starting sites/populations/research into
            // the live GameState (idempotent; SimulationEngine is left untouched).
            scenario.ApplyStartingState(SimulationEngine.State);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"GameBridge.StartScenario('{scenarioId}') failed: {ex.Message}");
            return false;
        }

        EmitSignal(SignalName.ScenarioLoaded, scenario.Id, scenario.Name);
        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: scenario started — {scenario.Name} ({scenario.Id})");
        return true;
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
        if (s.SpecialRules.ContainsKey("single_site")) special.Add("1 anlegg");
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
        WinCondition.Profit => $"Nå {FormatCapital(RuleDecimal(s, "profit_target", 5_000_000m))} i overskudd",
        WinCondition.Sustainability => $"Nå bærekraftsmål {RuleDecimal(s, "sustainability_target", 95m):0}",
        WinCondition.Survival => $"Overlev {(s.TimeLimitDays ?? 730)} dager med positiv saldo",
        WinCondition.Competition => $"Nå {FormatCapital(RuleDecimal(s, "profit_target", 10_000_000m))} før konkurrentene",
        _ => s.WinCondition.ToString()
    };

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
        var site = SimulationEngine?.State?.SeaSites?.FirstOrDefault(s => s.Id == siteId);
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

        SimulationEngine.FinanceManager.Spend(cost);
        site.Barrier = parsed;
        site.BarrierDaysSinceCleaning = 0;

        SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = SimulationEngine.State.Day,
            Name = $"{Farm.BarrierTech.SpecFor(parsed).DisplayName} installert",
            Category = LakskongenSim.Events.EventCategory.Equipment,
            Description = $"{Farm.BarrierTech.SpecFor(parsed).DisplayName} ved {siteId} ({cost:N0} NOK)",
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
        SimulationEngine.FinanceManager.Spend(cost);
        site.BarrierDaysSinceCleaning = 0;

        SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = SimulationEngine.State.Day,
            Name = "Barriere rengjort",
            Category = LakskongenSim.Events.EventCategory.Equipment,
            Description = $"{Farm.BarrierTech.SpecFor(site.Barrier).DisplayName} rengjort ved {siteId}",
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
    /// Returns true on success, false if zone not found or insufficient funds.
    /// </summary>
    public bool BuildSeaSite(string zoneId, string name)
    {
        if (SimulationEngine?.State?.Zones == null || SimulationEngine?.State?.SeaSites == null)
        {
            GD.PrintErr("GameBridge: BuildSeaSite failed - engine not initialized");
            EmitSignal(SignalName.FarmActionCompleted, "build_site", zoneId, false);
            return false;
        }

        // Check if zone exists
        var zone = SimulationEngine.State.Zones.FirstOrDefault(z => z.Id == zoneId);
        if (zone == null)
        {
            GD.PrintErr($"GameBridge: BuildSeaSite failed - zone '{zoneId}' not found");
            EmitSignal(SignalName.FarmActionCompleted, "build_site", zoneId, false);
            return false;
        }

        // License cost: ~30M NOK for 780-tonne license (Norwegian market rate)
        decimal licenseCost = 30_000_000m;
        
        // Check if player can afford it
        if (SimulationEngine.FinanceManager.Balance < licenseCost)
        {
            GD.PrintErr($"GameBridge: BuildSeaSite failed - insufficient funds (need {licenseCost:N0} NOK)");
            EmitSignal(SignalName.FarmActionCompleted, "build_site", zoneId, false);
            return false;
        }

        // Create new sea site
        var siteId = string.IsNullOrWhiteSpace(name) ? $"site_{SimulationEngine.State.SeaSites.Count + 1}" : name;
        var newSite = new LakskongenSim.Farm.SeaSite(siteId, zoneId)
        {
            MABTonnes = 780.0, // Standard Norwegian license
            NumberOfPens = 8,
            PenVolume = 50000f,
            MaxAllowedDensity = 25.0f
        };

        SimulationEngine.State.SeaSites.Add(newSite);

        // Charge the player
        SimulationEngine.FinanceManager.Spend(licenseCost);

        // Record event
        SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = SimulationEngine.State.Day,
            Name = "License Acquisition",
            Category = LakskongenSim.Events.EventCategory.Regulation,
            Description = $"Built new sea site '{siteId}' in zone '{zoneId}' (780T MAB)",
            BalanceChange = -licenseCost
        });

        EmitSignal(SignalName.FarmActionCompleted, "build_site", zoneId, true);
        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: Built sea site '{siteId}' in zone '{zoneId}' for {licenseCost:N0} NOK");
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
    /// Deploy a bloom countermeasure (Phase 4.3): "bubble" (1.8M + 90k/wk,
    /// jelly ×0.23) or "diffuser" (2.4M + 120k/wk, hypoxia ×0.3, algae ×0.7).
    /// Toggling an active measure OFF is free.
    /// </summary>
    public bool DeployCountermeasure(string siteId, string kind)
    {
        var site = SimulationEngine?.State?.SeaSites?.FirstOrDefault(s => s.Id == siteId);
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
            SimulationEngine.FinanceManager.Spend(cost);
            if (isBubble) site.HasBubbleCurtain = true; else site.HasO2Diffuser = true;
            SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
            {
                Day = SimulationEngine.State.Day,
                Name = isBubble ? "Boblegardin installert" : "O2-diffuser installert",
                Category = LakskongenSim.Events.EventCategory.Equipment,
                Description = $"{(isBubble ? "Boblegardin" : "O2-diffuser")} ved {siteId}",
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

    /// <summary>October window days left, -1 when closed (Phase 5.1).</summary>
    public int VaccineWindowDaysLeft =>
        LakskongenSim.Health.VaccinationProgram.WindowDaysLeft(SimulationEngine?.State?.Day ?? 0);

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
            SimulationEngine.FinanceManager.Spend(cost);
            site.HasSludgeHubConnection = true;

            SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
            {
                Day = SimulationEngine.State.Day,
                Name = hubExists ? "Slam-hub tilkobling" : "Slam-hub byggd",
                Category = LakskongenSim.Events.EventCategory.Equipment,
                Description = $"{site.Id} koblet til sentral slam-hub ({cost:N0} NOK)",
                BalanceChange = -cost
            });
        }
        else
        {
            site.HasSludgeHubConnection = false;
            SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
            {
                Day = SimulationEngine.State.Day,
                Name = "Slam-hub frakoblet",
                Category = LakskongenSim.Events.EventCategory.Equipment,
                Description = $"{site.Id} frakoblet fra sentral slam-hub",
                BalanceChange = 0m
            });
        }

        EmitSignal(SignalName.GameStateUpdated);
        return true;
    }

    /// <summary>
    /// Manually trigger sludge valorization at a site (Phase 5.3).
    /// Returns net revenue (biogas + fertilizer - opex).
    /// </summary>
    public decimal ValorizeSludgeNow(string siteId)
    {
        var site = SimulationEngine?.State?.SeaSites?.FirstOrDefault(s => s.Id == siteId);
        if (site == null) return 0m;
        if (site.SludgeAccumulatedKgDs < 100.0) return 0m;

        var sludgeParams = Farm.SludgeValorization.Default;
        var (biogasRev, fertRev, opex) = Farm.SludgeValorization.Valorize(site.SludgeAccumulatedKgDs, sludgeParams);
        decimal netRev = biogasRev + fertRev - opex;

        if (netRev > 0)
            SimulationEngine!.FinanceManager.AddRevenue(netRev);
        else
        {
            if (!SimulationEngine!.FinanceManager.Spend(-netRev))
                SimulationEngine!.FinanceManager.ForceSpend(-netRev);
        }

        site.SludgeValorizedKgDs += site.SludgeAccumulatedKgDs;
        site.LastSludgeValorizationDay = SimulationEngine!.State.Day;

        SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = SimulationEngine.State.Day,
            Name = "Manuell slam-valorisering",
            Category = LakskongenSim.Events.EventCategory.Equipment,
            Description = $"{site.Id}: {site.SludgeAccumulatedKgDs:F0} kg DS → biogas {biogasRev:N0} + gjødsel {fertRev:N0} - OPEX {opex:N0} = {netRev:+#,0;-#,0;0} NOK",
            BalanceChange = netRev
        });
        SimulationEngine.State.TrimEventLog();

        site.SludgeAccumulatedKgDs = 0.0;
        EmitSignal(SignalName.GameStateUpdated);
        return netRev;
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
    /// Plan a cohort transfer between sites (Phase 5.3).
    /// Returns plan with cost, mortality estimate, ETA. Does not execute.
    /// Keys: "source_id", "dest_id", "fish_count", "mean_weight_kg", "reason",
    ///       "eta_days", "estimated_cost_nok", "estimated_mortality_pct", "requires_wellboat".
    /// </summary>
    public Godot.Collections.Dictionary PlanCohortTransfer(
        string sourceSiteId, string destSiteId, int fishCount, double meanWeightKg, string reasonStr)
    {
        var result = new Godot.Collections.Dictionary();
        if (!System.Enum.TryParse<Farm.CohortTransferSystem.TransferReason>(reasonStr, true, out var reason))
        {
            result["error"] = "Invalid reason";
            return result;
        }

        var state = SimulationEngine?.State;
        if (state == null) { result["error"] = "Engine not initialized"; return result; }

        bool hasVet = LakskongenSim.Core.CrewEffects.HasRole(state, "veterinaer");
        var plan = Farm.CohortTransferSystem.PlanTransfer(sourceSiteId, destSiteId, fishCount, meanWeightKg, reason, state, hasVet);

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
    /// Execute a planned cohort transfer (Phase 5.3).
    /// Called when wellboat arrives (or immediately for testing).
    /// Returns result dict: "success", "fish_transferred", "fish_lost", "actual_cost_nok", "message".
    /// </summary>
    public Godot.Collections.Dictionary ExecuteCohortTransfer(
        string sourceSiteId, string destSiteId, int fishCount, double meanWeightKg, string reasonStr)
    {
        var result = new Godot.Collections.Dictionary();
        if (!System.Enum.TryParse<Farm.CohortTransferSystem.TransferReason>(reasonStr, true, out var reason))
        {
            result["success"] = false;
            result["message"] = "Invalid reason";
            return result;
        }

        var engine = SimulationEngine;
        if (engine?.State == null) { result["success"] = false; result["message"] = "Engine not initialized"; return result; }

        bool hasVet = LakskongenSim.Core.CrewEffects.HasRole(engine.State, "veterinaer");
        var plan = Farm.CohortTransferSystem.PlanTransfer(sourceSiteId, destSiteId, fishCount, meanWeightKg, reason, engine.State, hasVet);
        var execResult = Farm.CohortTransferSystem.ExecuteTransfer(plan, engine.State, engine.FinanceManager, Random.Shared);

        result["success"] = execResult.Success;
        result["fish_transferred"] = execResult.FishTransferred;
        result["fish_lost"] = execResult.FishLost;
        result["actual_cost_nok"] = (double)execResult.ActualCostNok;
        result["message"] = execResult.Message;

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
                ["name"] = site.Id,
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

        // Sea sites
        foreach (var site in state.SeaSites)
        {
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

        // Offshore farms
        var offshoreArray = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var off in SimulationEngine!.OffshoreFarms)
        {
            if (!off.IsOperational) continue;
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
                ["name"] = off.Name,
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
                                     SimulationEngine!.OffshoreFarms.Sum(o => o.CarbonTracker.NetThisYear(state.Day)) +
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
                                SimulationEngine!.OffshoreFarms.Sum(o => o.CarbonTracker.TotalEmissions()) +
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
                             SimulationEngine!.OffshoreFarms.Sum(o => o.CarbonTracker.NetThisYear(state.Day)) +
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

        double temp = engine!.Weather?.CurrentTemperatureC ?? 10.0;
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
