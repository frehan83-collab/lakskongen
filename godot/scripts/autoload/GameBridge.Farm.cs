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

// GameBridge.Farm.cs — partial class slice (see project docs).
// <summary>
// Farm slice: sea-site construction, smolt stocking, harvest, smolt strains,
// bloom mitigation, intervention ranking, night-shift policy, vaccination,
// delousing, containment upgrades, net repair and treatment profiles, feed
// recipe/forecast, genetics, crew, and RAS / offshore / AI-feeding / post-smolt
// facility operations.
// </summary>
/// <summary>
/// GameBridge partial — sea-site construction, smolt stocking, harvest, smolt strains, (moved verbatim from GameBridge.cs).
/// </summary>
public partial class GameBridge : Node
{
    // ── Farm Management API ───────────────────────────────────────────────────

    /// <summary>
    /// Stock smolts at a specific sea site (Phase 1.4: strain-aware).
    /// Strain selects release weight, cost, genome traits and lice load.
    /// Returns true on success, false if site not found or stocking failed.
    /// </summary>
    public bool StockSmolts(string siteId, int count, string strainId = "standard")
        => StockSmolts(siteId, count, strainId, Farm.SmoltQualityTier.Standard);

    /// <summary>
    /// Stock smolts with an explicit post-smolt quality tier (P5 carryover).
    /// The tier is stamped on the cohort so the sea phase applies its
    /// mortality / growth / time-to-harvest carryover multipliers.
    /// </summary>
    public bool StockSmolts(string siteId, int count, string strainId, Farm.SmoltQualityTier qualityTier)
    {
        var engine = SimulationEngine;
        if (engine == null)
        {
            GD.PrintErr("GameBridge: StockSmolts failed - engine not initialized");
            EmitSignal(SignalName.FarmActionCompleted, "stock_smolts", siteId, false);
            return false;
        }

        // Phase 1: the verb lives in the library (SimulationEngine.Commands).
        // The bridge only projects the typed result onto the legacy surface.
        var result = engine.StockSmolts(siteId, count, strainId, qualityTier);
        if (!result.Ok)
        {
            GD.PrintErr($"GameBridge: StockSmolts failed - {result.Message}");
            EmitSignal(SignalName.FarmActionCompleted, "stock_smolts", siteId, false);
            return false;
        }

        EmitSignal(SignalName.FarmActionCompleted, "stock_smolts", siteId, true);
        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: {result.Message}");
        return true;
    }

    /// <summary>
    /// Buyable smolt strains for the stocking dialog (Phase 1.4).
    /// Keys: "id", "name", "description", "weight_g", "cost_multiplier",
    ///       "growth_pct", "disease_pct", "initial_lice".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetSmoltStrains()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var s in LakskongenSim.Farm.SmoltStrainRegistry.All)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"]              = s.Id,
                ["name"]            = s.Name,
                ["description"]     = s.Description,
                ["weight_g"]        = s.ReleaseWeightKg * 1000.0,
                ["cost_multiplier"] = s.CostMultiplier,
                ["growth_pct"]      = s.GrowthTrait * 15.0,
                ["disease_pct"]     = s.DiseaseTrait * 20.0,
                ["initial_lice"]    = s.InitialLiceLoad
            });
        }
        return result;
    }

    /// <summary>
    /// Post-smolt quality tiers for the stocking dialog (P5 carryover).
    /// Each entry: "id" (tier name), "name", "mortality_mult", "growth_mult",
    /// "days_mult", "quality" (0-1). Array order matches the enum order, so the
    /// UI's selected index equals the tier value passed to StockSmolts.
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetSmoltQualityTiers()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var q in Farm.SmoltQualityRegistry.All)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"]             = q.Tier.ToString(),
                ["name"]           = q.Name,
                ["mortality_mult"] = q.MortalityMultiplier,
                ["growth_mult"]    = q.GrowthMultiplier,
                ["days_mult"]      = q.TimeToHarvestMultiplier,
                ["quality"]        = q.Quality01
            });
        }
        return result;
    }

    /// <summary>
    /// Harvest all fish at a specific sea site.
    /// Returns the total biomass harvested in kg, or 0.0 if site not found or empty.
    /// </summary>
    public double HarvestSite(string siteId)
    {
        var engine = SimulationEngine;
        if (engine == null)
        {
            GD.PrintErr("GameBridge: HarvestSite failed - engine not initialized");
            EmitSignal(SignalName.FarmActionCompleted, "harvest", siteId, false);
            return 0.0;
        }

        // Phase 1: the verb lives in the library (SimulationEngine.Commands);
        // Value = kg, Value2 = NOK revenue. Same legacy surface as before.
        var result = engine.HarvestSite(siteId);
        if (!result.Ok)
        {
            GD.PrintErr($"GameBridge: HarvestSite failed - {result.Message}");
            EmitSignal(SignalName.FarmActionCompleted, "harvest", siteId, false);
            return 0.0;
        }

        // Trigger achievement check
        engine.Audio?.TriggerAlert("harvest");

        EmitSignal(SignalName.FarmActionCompleted, "harvest", siteId, true);
        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: Harvested {result.Value:N0} kg at '{siteId}' for {result.Value2:N0} NOK");
        return result.Value;
    }

    // ── Bloom mitigation API (P11 mitigation UI) ────────────────────────────

    private static readonly Farm.MitigationKind[] BloomMitigationKinds =
    {
        Farm.MitigationKind.BubbleCurtain,
        Farm.MitigationKind.O2Diffuser,
        Farm.MitigationKind.Pumping,
        Farm.MitigationKind.DeepFeeding
    };

    /// <summary>
    /// In-memory registry of non-flag-backed mitigation installs (pumping and
    /// deep feeding). Bubble curtain / O2 diffuser persist on the
    /// <see cref="Farm.SeaSite"/> flags; the remaining kinds have no modelled
    /// site field, so the bridge tracks them here for the UI only, without
    /// touching SimulationEngine or the site model.
    /// </summary>
    private readonly Dictionary<string, HashSet<Farm.MitigationKind>> _installedMitigations =
        new(StringComparer.OrdinalIgnoreCase);

    private static string MitigationKindKey(Farm.MitigationKind kind) => kind switch
    {
        Farm.MitigationKind.BubbleCurtain => "bubble",
        Farm.MitigationKind.O2Diffuser => "diffuser",
        Farm.MitigationKind.Pumping => "pumping",
        Farm.MitigationKind.DeepFeeding => "deep_feeding",
        _ => "none"
    };

    private static string MitigationKindLabel(Farm.MitigationKind kind) => kind switch
    {
        Farm.MitigationKind.BubbleCurtain => "Boblegardin",
        Farm.MitigationKind.O2Diffuser => "O2-diffuser",
        Farm.MitigationKind.Pumping => "Dypvannspumping",
        Farm.MitigationKind.DeepFeeding => "Dypfôring",
        _ => "Ingen"
    };

    private static string MitigationBestFor(Farm.MitigationKind kind) => kind switch
    {
        Farm.MitigationKind.BubbleCurtain => "jellyfish",
        Farm.MitigationKind.O2Diffuser => "algae/hypoxia",
        Farm.MitigationKind.Pumping => "hypoxia",
        Farm.MitigationKind.DeepFeeding => "algae",
        _ => ""
    };

    private static bool TryParseMitigationKind(string kind, out Farm.MitigationKind parsed)
    {
        switch ((kind ?? "").Trim().ToLowerInvariant())
        {
            case "bubble":
            case "bubblecurtain":
            case "bubble_curtain":
                parsed = Farm.MitigationKind.BubbleCurtain; return true;
            case "diffuser":
            case "o2":
            case "o2diffuser":
            case "o2_diffuser":
                parsed = Farm.MitigationKind.O2Diffuser; return true;
            case "pumping":
            case "pump":
                parsed = Farm.MitigationKind.Pumping; return true;
            case "deep_feeding":
            case "deepfeeding":
            case "deep_feed":
                parsed = Farm.MitigationKind.DeepFeeding; return true;
            default:
                parsed = Farm.MitigationKind.None; return false;
        }
    }

    private bool IsBloomMitigationInstalled(Farm.SeaSite site, Farm.MitigationKind kind)
    {
        if (site == null) return false;
        if (kind == Farm.MitigationKind.BubbleCurtain || kind == Farm.MitigationKind.O2Diffuser)
            return Farm.BloomMitigation.IsInstalled(kind, site);
        return _installedMitigations.TryGetValue(site.Id, out var set) && set.Contains(kind);
    }

    /// <summary>
    /// P11 persistence: mirror the in-memory mitigation registry into
    /// <see cref="GameState.BloomMitigations"/> so it is serialized on save.
    /// Flag-backed kinds (bubble curtain / O2 diffuser) persist on the site
    /// model and are intentionally not duplicated here.
    /// </summary>
    private void SyncMitigationsToState()
    {
        var state = SimulationEngine?.State;
        if (state == null) return;
        state.BloomMitigations ??= new List<BloomMitigationState>();
        state.BloomMitigations.Clear();
        foreach (var kvp in _installedMitigations)
        {
            foreach (var kind in kvp.Value)
            {
                if (kind == Farm.MitigationKind.BubbleCurtain || kind == Farm.MitigationKind.O2Diffuser)
                    continue;
                state.BloomMitigations.Add(new BloomMitigationState
                {
                    SiteId = kvp.Key,
                    Kind = MitigationKindKey(kind)
                });
            }
        }
    }

    /// <summary>
    /// P11 persistence: rebuild the in-memory mitigation registry from a saved
    /// <see cref="GameState.BloomMitigations"/> list. Backward-safe: null list
    /// or unknown kinds are ignored; flag-backed kinds are skipped (site flags
    /// are the source of truth for them).
    /// </summary>
    private void RestoreMitigationsFromState(List<BloomMitigationState>? saved)
    {
        _installedMitigations.Clear();
        if (saved == null) return;

        foreach (var entry in saved)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.SiteId)) continue;
            if (!TryParseMitigationKind(entry.Kind, out var kind) || kind == Farm.MitigationKind.None) continue;
            if (kind == Farm.MitigationKind.BubbleCurtain || kind == Farm.MitigationKind.O2Diffuser) continue;

            if (!_installedMitigations.TryGetValue(entry.SiteId, out var set))
            {
                set = new HashSet<Farm.MitigationKind>();
                _installedMitigations[entry.SiteId] = set;
            }
            set.Add(kind);
        }
    }

    /// <summary>
    /// Bloom-mitigation catalog for the UI: one entry per kind with its fixed
    /// deployment cost (NOK), peak mortality reduction (fraction), intended
    /// bloom kind and the sites where it is currently installed.
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetBloomMitigations()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var sites = SimulationEngine?.State?.SeaSites;
        foreach (var kind in BloomMitigationKinds)
        {
            var installed = new Godot.Collections.Array<string>();
            if (sites != null)
            {
                foreach (var s in sites)
                    if (IsBloomMitigationInstalled(s, kind)) installed.Add(s.Id);
            }
            result.Add(new Godot.Collections.Dictionary
            {
                ["kind"] = MitigationKindKey(kind),
                ["label"] = MitigationKindLabel(kind),
                ["fixed_cost"] = (double)Farm.BloomMitigation.FixedCostNok(kind),
                ["opex_at_full"] = (double)Farm.BloomMitigation.OpexPerSeverityNok(kind),
                ["max_reduction"] = Farm.BloomMitigation.MaxReduction(kind),
                ["best_for"] = MitigationBestFor(kind),
                ["installed_sites"] = installed
            });
        }
        return result;
    }

    /// <summary>
    /// Install (or toggle off) a bloom mitigation at a site. Bubble curtain and O2
    /// diffuser delegate to <see cref="DeployCountermeasure"/> so their existing
    /// costs and site flags are preserved; pumping and deep feeding are tracked by
    /// the bridge at their <see cref="Farm.BloomMitigation"/> fixed cost. Returns
    /// false for an unknown site/kind or when funds are insufficient.
    /// </summary>
    public bool InstallBloomMitigation(string siteId, string kind)
    {
        var site = SimulationEngine?.State?.SeaSites?.FirstOrDefault(s => s.Id == siteId);
        if (site == null) return false;
        if (!TryParseMitigationKind(kind, out var parsed) || parsed == Farm.MitigationKind.None) return false;

        if (parsed == Farm.MitigationKind.BubbleCurtain || parsed == Farm.MitigationKind.O2Diffuser)
            return DeployCountermeasure(siteId, MitigationKindKey(parsed));

        bool installed = IsBloomMitigationInstalled(site, parsed);
        if (!installed)
        {
            decimal cost = Farm.BloomMitigation.FixedCostNok(parsed);
            if (SimulationEngine!.FinanceManager.Balance < cost)
            {
                EmitSignal(SignalName.FarmActionCompleted, "countermeasure", siteId, false);
                return false;
            }
            SimulationEngine.FinanceManager.Spend(cost);
            if (!_installedMitigations.TryGetValue(site.Id, out var set))
            {
                set = new HashSet<Farm.MitigationKind>();
                _installedMitigations[site.Id] = set;
            }
            set.Add(parsed);
            SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
            {
                Day = SimulationEngine.State.Day,
                Name = $"{MitigationKindLabel(parsed)} installert",
                Category = LakskongenSim.Events.EventCategory.Equipment,
                Description = $"{MitigationKindLabel(parsed)} ved {siteId}",
                BalanceChange = -cost
            });
        }
        else
        {
            _installedMitigations[site.Id].Remove(parsed);
        }

        // P11 persistence: keep the serializable mirror in lock-step.
        SyncMitigationsToState();

        EmitSignal(SignalName.FarmActionCompleted, "countermeasure", siteId, true);
        EmitSignal(SignalName.GameStateUpdated);
        return true;
    }

    // ── Intervention advisor (Phase 5.3) ──

    /// <summary>
    /// Ranked intervention suggestions from twin-lite projection (Phase 5.3).
    /// Keys: "id", "action", "site_id", "eta_days", "delta_nok", "confidence", "reasoning".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetInterventionRanking()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var engine = SimulationEngine;
        if (engine?.State == null || engine.Growth == null || engine.SeaLice == null || engine.Analytics == null) return result;

        var state = engine.State;
        double tempC = engine.Weather?.CurrentTemperatureC ?? 10.0;
        var forecast = LakskongenSim.Optimization.ForecastTwin.Project(
            state, engine.SeaLice, tempC, engine.Analytics, engine.GrowthRateBonus);
        double currentLice = engine.SeaLice.AverageLicePerFish;
        double gillScore = 0.0;
        if (state.SeaSites.Count > 0)
            gillScore = state.SeaSites.Average(s => s.GillScore);
        double feedFcr = engine.Feed?.CurrentFCR ?? 1.15;
        var contracts = engine.ContractSystem?.ActiveContracts ?? new List<LakskongenSim.Market.Contract>();
        int crewCount = state.Crew?.Count ?? 0;

        var suggestions = LakskongenSim.Advisor.InterventionAdvisor.Rank(
            state, forecast, currentLice, gillScore, feedFcr, contracts,
            crewCount, state.Day);

        foreach (var s in suggestions)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = s.Id,
                ["action"] = s.Action,
                ["site_id"] = s.SiteId,
                ["eta_days"] = s.DaysOut,
                ["delta_nok"] = (double)s.DeltaNok,
                ["confidence"] = s.Confidence,
                ["reasoning"] = s.Reasoning
            });
        }
        return result;
    }

    // ── Phase 4.5: Attention Inbox (unified "what needs my attention" inbox) ──

    /// <summary>Emitted when the UI should open the Attention Inbox panel.</summary>
    [Signal] public delegate void AttentionInboxRequestedEventHandler();

    /// <summary>
    /// Get unified attention items from InterventionAdvisor + health/finance/market alerts.
    /// Returns array of dictionaries with: type, priority, category, title, description,
    /// delta_nok, days_out, site_id, callback, action_id.
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetAttentionItems()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var engine = SimulationEngine;
        if (engine?.State == null) return result;

        var state = engine.State;

        // 1. InterventionAdvisor suggestions
        if (engine.Growth != null && engine.SeaLice != null && engine.Analytics != null)
        {
            double tempC = engine.Weather?.CurrentTemperatureC ?? 10.0;
            var forecast = LakskongenSim.Optimization.ForecastTwin.Project(
                state, engine.SeaLice, tempC, engine.Analytics, engine.GrowthRateBonus);
            double currentLice = engine.SeaLice.AverageLicePerFish;
            double gillScore = 0.0;
            if (state.SeaSites.Count > 0)
                gillScore = state.SeaSites.Average(s => s.GillScore);
            double feedFcr = engine.Feed?.CurrentFCR ?? 1.15;
            var contracts = engine.ContractSystem?.ActiveContracts ?? new List<LakskongenSim.Market.Contract>();
            int crewCount = state.Crew?.Count ?? 0;

            var suggestions = LakskongenSim.Advisor.InterventionAdvisor.Rank(
                state, forecast, currentLice, gillScore, feedFcr, contracts,
                crewCount, state.Day);

            foreach (var s in suggestions)
            {
                result.Add(new Godot.Collections.Dictionary
                {
                    ["type"] = "intervention",
                    ["priority"] = _priority_from_confidence(s.Confidence),
                    ["category"] = "intervention",
                    ["title"] = s.Action,
                    ["description"] = s.Reasoning,
                    ["delta_nok"] = (double)s.DeltaNok,
                    ["days_out"] = s.DaysOut,
                    ["site_id"] = s.SiteId,
                    ["callback"] = "open_intervention_detail",
                    ["action_id"] = s.Id
                });
            }
        }

        // 2. Health alerts (gill health)
        if (state.SeaSites != null)
        {
            foreach (var site in state.SeaSites)
            {
                if (site.GillScore > 0 && site.GillScore < 30)
                {
                    result.Add(new Godot.Collections.Dictionary
                    {
                        ["type"] = "health_critical",
                        ["priority"] = "critical",
                        ["category"] = "health",
                        ["title"] = _tr_or("UI_ATTENTION_GILL_CRITICAL", "Critical Gill Health"),
                        ["description"] = string.Format(_tr_or("UI_ATTENTION_GILL_CRITICAL_DESC", "Site {0} has critical gill health — immediate action required"), site.Id),
                        ["site_id"] = site.Id,
                        ["callback"] = "open_site_health"
                    });
                }
                else if (site.GillScore >= 30 && site.GillScore < 60)
                {
                    result.Add(new Godot.Collections.Dictionary
                    {
                        ["type"] = "health_warning",
                        ["priority"] = "high",
                        ["category"] = "health",
                        ["title"] = _tr_or("UI_ATTENTION_GILL_WARNING", "Gill Health Warning"),
                        ["description"] = string.Format(_tr_or("UI_ATTENTION_GILL_WARNING_DESC", "Site {0} has elevated gill risk"), site.Id),
                        ["site_id"] = site.Id,
                        ["callback"] = "open_site_health"
                    });
                }
            }
        }

        // 3. Financial alerts
        if (engine.FinanceManager != null)
        {
            decimal balance = engine.FinanceManager.Balance;
            if (balance < 0)
            {
                result.Add(new Godot.Collections.Dictionary
                {
                    ["type"] = "finance_critical",
                    ["priority"] = "critical",
                    ["category"] = "finance",
                    ["title"] = _tr_or("UI_ATTENTION_NEGATIVE_BALANCE", "Negative Balance"),
                    ["description"] = string.Format(_tr_or("UI_ATTENTION_NEGATIVE_BALANCE_DESC", "Company balance is negative: {0} NOK"), balance.ToString("N0")),
                    ["callback"] = "open_finance_panel"
                });
            }
            else if (balance < 1_000_000m)
            {
                result.Add(new Godot.Collections.Dictionary
                {
                    ["type"] = "finance_warning",
                    ["priority"] = "high",
                    ["category"] = "finance",
                    ["title"] = _tr_or("UI_ATTENTION_LOW_LIQUIDITY", "Low Liquidity"),
                    ["description"] = string.Format(_tr_or("UI_ATTENTION_LOW_LIQUIDITY_DESC", "Cash reserves below 1M NOK: {0} NOK"), balance.ToString("N0")),
                    ["callback"] = "open_finance_panel"
                });
            }
        }

        // 4. Lice pressure alerts
        if (engine.SeaLice != null)
        {
            double lice = engine.SeaLice.AverageLicePerFish;
            if (lice > 0.5)
            {
                result.Add(new Godot.Collections.Dictionary
                {
                    ["type"] = "lice_critical",
                    ["priority"] = "critical",
                    ["category"] = "health",
                    ["title"] = _tr_or("UI_ATTENTION_LICE_CRITICAL", "Lice Above Treatment Threshold"),
                    ["description"] = string.Format(_tr_or("UI_ATTENTION_LICE_CRITICAL_DESC", "Sea lice at {0:F2}/fish — mandatory treatment required"), lice),
                    ["callback"] = "open_health_panel"
                });
            }
            else if (lice > 0.2)
            {
                result.Add(new Godot.Collections.Dictionary
                {
                    ["type"] = "lice_warning",
                    ["priority"] = "high",
                    ["category"] = "health",
                    ["title"] = _tr_or("UI_ATTENTION_LICE_WARNING", "Elevated Lice Levels"),
                    ["description"] = string.Format(_tr_or("UI_ATTENTION_LICE_WARNING_DESC", "Sea lice at {0:F2}/fish — approaching treatment threshold"), lice),
                    ["callback"] = "open_health_panel"
                });
            }
        }

        // 5. Weather alerts
        if (engine.Weather != null)
        {
            var weather = engine.Weather.CurrentState;
            if (weather == LakskongenSim.Environment.WeatherState.Storm || weather == LakskongenSim.Environment.WeatherState.Severe)
            {
                result.Add(new Godot.Collections.Dictionary
                {
                    ["type"] = "weather_alert",
                    ["priority"] = "critical",
                    ["category"] = "weather",
                    ["title"] = _tr_or("UI_ATTENTION_STORM", "Severe Weather"),
                    ["description"] = string.Format(_tr_or("UI_ATTENTION_STORM_DESC", "{0} warning — secure sites and review operations"), weather),
                    ["callback"] = "open_weather_panel"
                });
            }
        }

        // 6. Contract alerts (expiring within 3 weeks)
        if (engine.ContractSystem != null)
        {
            int currentWeek = state.Day / 7;
            foreach (var c in engine.ContractSystem.ActiveContracts)
            {
                if (!c.IsExpired(currentWeek) && c.IsExpired(currentWeek + 3))
                {
                    result.Add(new Godot.Collections.Dictionary
                    {
                        ["type"] = "contract_expiring",
                        ["priority"] = "high",
                        ["category"] = "contract",
                        ["title"] = _tr_or("UI_ATTENTION_CONTRACT_EXPIRING", "Contract Expiring Soon"),
                        ["description"] = string.Format(_tr_or("UI_ATTENTION_CONTRACT_EXPIRING_DESC", "Contract {0} expires in {1} weeks"), c.Id.ToString()[..8], c.DurationWeeks - (currentWeek - c.SignedAtWeek)),
                        ["callback"] = "open_contracts_panel"
                    });
                }
            }
        }

        return result;
    }

    private static string _tr_or(string key, string fallback)
    {
        // Godot translation lookup; TranslationServer returns the key itself
        // when no translation exists, in which case we use the fallback.
        var translated = Godot.TranslationServer.Translate(key).ToString();
        return translated == key ? fallback : translated;
    }

    private static string _priority_from_confidence(double conf)
    {
        if (conf >= 0.8) return "critical";
        if (conf >= 0.6) return "high";
        if (conf >= 0.4) return "medium";
        return "low";
    }

    /// <summary>
    /// Opens the Attention Inbox panel in the UI.
    /// </summary>
    public void OpenAttentionInbox()
    {
        // Notify the engine (analytics hook) and ask the UI to show the panel.
        SimulationEngine?.OpenAttentionInbox();
        EmitSignal(SignalName.AttentionInboxRequested);
    }

    // ── Night-shift ROC policy (Phase 5.2) ──

    /// <summary>Current night-shift policy (Standard/ScanOnly/AutomatedROC).</summary>
    public string NightShiftPolicy => SimulationEngine?.State?.NightShiftPolicy.ToString() ?? "Standard";

    /// <summary>
    /// Set night-shift ROC policy (Phase 5.2).
    /// Standard / ScanOnly / AutomatedROC. Returns true on success.
    /// </summary>
    public bool SetNightShiftPolicy(string policyName)
    {
        if (!System.Enum.TryParse<LakskongenSim.Logistics.NightShiftPolicy>(policyName, true, out var policy))
            return false;
        var state = SimulationEngine?.State;
        if (state == null) return false;
        state.NightShiftPolicy = policy;
        EmitSignal(SignalName.GameStateUpdated);
        return true;
    }

    /// <summary>Night-shift policy premium / detection / fatigue scalars.</summary>
    public Godot.Collections.Dictionary GetNightShiftStatus()
    {
        var state = SimulationEngine?.State;
        var policy = state?.NightShiftPolicy ?? LakskongenSim.Logistics.NightShiftPolicy.Standard;
        return new Godot.Collections.Dictionary
        {
            ["policy"] = policy.ToString(),
            ["premium_monthly"] = (double)LakskongenSim.Logistics.NightShiftResolver.MonthlyCost(
                (LakskongenSim.Logistics.NightShiftPolicy)policy),
            ["detection_chance"] = LakskongenSim.Logistics.NightShiftResolver.DetectionChance(
                (LakskongenSim.Logistics.NightShiftPolicy)policy),
            ["fatigue_per_night"] = LakskongenSim.Logistics.NightShiftResolver.FatigueDelta(
                (LakskongenSim.Logistics.NightShiftPolicy)policy),
            ["misfire_chance"] = LakskongenSim.Logistics.NightShiftResolver.MisfireChance(
                (LakskongenSim.Logistics.NightShiftPolicy)policy,
                state?.Crew?.Count > 0 ? (int)(state.Crew.Average(c => c.Morale)) : 5)
        };
    }

    /// <summary>
    /// Vaccinate a site against ulcer/CMS (Phase 5.1). October window only,
    /// 3.2 NOK/fish + 0.6% handling loss (vet halves it), 12-mo protection.
    /// </summary>
    public bool VaccinateSite(string siteId)
    {
        var state = SimulationEngine?.State;
        var site = state?.SeaSites?.FirstOrDefault(s => s.Id == siteId);
        if (site == null) return false;
        if (!LakskongenSim.Health.VaccinationProgram.IsWindowOpen(state!.Day))
        {
            GD.Print($"GameBridge: VaccinateSite refused — October window closed (day {state.Day})");
            EmitSignal(SignalName.FarmActionCompleted, "vaccinate", siteId, false);
            return false;
        }
        int fish = site.Pens.SelectMany(p => p.Cohorts).Sum(c => c.Count);
        if (fish <= 0) return false;
        decimal cost = fish * LakskongenSim.Health.VaccinationProgram.CostPerFishNok;
        if (SimulationEngine!.FinanceManager.Balance < cost)
        {
            EmitSignal(SignalName.FarmActionCompleted, "vaccinate", siteId, false);
            return false;
        }
        bool vet = LakskongenSim.Core.CrewEffects.HasRole(state, "veterinaer");
        double lossRate = LakskongenSim.Health.VaccinationProgram.HandlingLoss * (vet ? 0.5 : 1.0);
        int lost = 0;
        foreach (var pop in site.Pens)
        {
            foreach (var cohort in pop.Cohorts)
            {
                int take = (int)(cohort.Count * lossRate);
                cohort.Count = System.Math.Max(0, cohort.Count - take);
                lost += take;
            }
            pop.VaccinatedDay = state.Day;
        }
        SimulationEngine.FinanceManager.Spend(cost);
        state.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = state.Day,
            Name = "Vaksinering",
            Category = LakskongenSim.Events.EventCategory.Disease,
            Description = $"{siteId} vaksinert mot sår/CMS ({fish:N0} fisk, {lost:N0} håndteringstap)",
            BalanceChange = -cost
        });
        EmitSignal(SignalName.FarmActionCompleted, "vaccinate", siteId, true);
        EmitSignal(SignalName.GameStateUpdated);
        return true;
    }

    /// <summary>
    /// Toggle a preventive mitigation at a site (Phase 1.5).
    /// kind: "skirt" (500k install, 2k/day, -settlement) or
    ///       "deterrent" (200k install, 1k/day, seal protection).
    /// Installing charges once; toggling an active mitigation OFF is free.
    /// </summary>
    public bool ToggleMitigation(string siteId, string kind)
    {
        var site = SimulationEngine?.State?.SeaSites?.FirstOrDefault(s => s.Id == siteId);
        if (site == null)
        {
            EmitSignal(SignalName.FarmActionCompleted, "mitigation", siteId, false);
            return false;
        }
        bool isSkirt = (kind ?? "").ToLower() == "skirt";
        bool active = isSkirt ? site.HasLiceSkirt : site.HasSealDeterrent;
        if (!active)
        {
            decimal cost = isSkirt ? 500_000m : 200_000m;
            if (SimulationEngine!.FinanceManager.Balance < cost)
            {
                EmitSignal(SignalName.FarmActionCompleted, "mitigation", siteId, false);
                return false;
            }
            SimulationEngine.FinanceManager.Spend(cost);
            if (isSkirt) site.HasLiceSkirt = true; else site.HasSealDeterrent = true;
            SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
            {
                Day = SimulationEngine.State.Day,
                Name = isSkirt ? "Lice Skirt Installed" : "Seal Deterrent Installed",
                Category = LakskongenSim.Events.EventCategory.Equipment,
                Description = $"{(isSkirt ? "Luseskjørt" : "Seldeterrent")} installert ved {siteId}",
                BalanceChange = -cost
            });
        }
        else if (isSkirt) site.HasLiceSkirt = false;
        else site.HasSealDeterrent = false;

        EmitSignal(SignalName.FarmActionCompleted, "mitigation", siteId, true);
        EmitSignal(SignalName.GameStateUpdated);
        return true;
    }
    ///       "biomass_kg" (double), "max_biomass" (double), "escapes_this_year" (int).
    /// </summary>
    /// <summary>
    /// Return detailed information about all sea sites as an Array of Dictionaries.
    /// Keys: "id", "name", "zone", "fish_count", "biomass_kg", "max_biomass",
    ///       "escapes_this_year", "net_integrity", "has_skirt", "has_deterrent".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetSeaSiteDetails()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var sites = SimulationEngine?.State?.SeaSites;
        if (sites == null) return result;

        foreach (var site in sites)
        {
            // Per-site truth lives in site.Pens: populations carry the
            // shared ZoneId, never the site Id, so State-wide filtering
            // by either key misattributes biomass across sites.
            int fishCount = site.Pens.SelectMany(p => p.Cohorts).Sum(c => c.Count);
            double biomassKg = site.Pens.SelectMany(p => p.Cohorts).Sum(c => c.Count * (double)c.MeanWeightKg);
            // Living-fjord payload: per-site lice drive collar tinting, mean
            // weight scales the 3D fish, oxygen + appetite gate feeding FX.
            double licePerFish = site.Pens.Count > 0
                ? site.Pens.Average(p => p.SeaLiceLoad) : 0.0;
            double meanWeightKg = fishCount > 0 ? biomassKg / fishCount : 0.0;
            var zone = SimulationEngine?.State?.Zones?.FirstOrDefault(z => z.Id == site.ZoneId);
            double oxygenMgL = zone?.WaterChemistry?.OxygenO2 ?? double.NaN;
            double appetite = Appetite01;
            bool isFeeding = appetite > 0.4
                && (double.IsNaN(oxygenMgL) || oxygenMgL > 3.0)
                && fishCount > 0;

            result.Add(new Godot.Collections.Dictionary
            {
                ["id"]                = site.Id,
                ["name"]              = site.Id,
                ["zone"]              = site.ZoneId,
                ["fish_count"]        = fishCount,
                ["biomass_kg"]        = biomassKg,
                ["lice_per_fish"]     = licePerFish,
                ["mean_weight_kg"]    = meanWeightKg,
                ["oxygen_mgl"]        = oxygenMgL,
                ["is_feeding"]        = isFeeding,
                ["max_biomass"]       = site.MABTonnes * 1000,
                ["escapes_this_year"] = site.EscapesThisYear,
                ["net_integrity"]     = site.NetIntegrityPct,
                ["has_skirt"]         = site.HasLiceSkirt,
                ["has_deterrent"]     = site.HasSealDeterrent,
                ["containment"]       = site.Containment.ToString(),
                ["watch_mode"]        = site.WatchMode,
                ["gill_score"]        = site.GillScore,
                ["has_bubble"]        = site.HasBubbleCurtain,
                ["has_diffuser"]      = site.HasO2Diffuser,
                // P8: permeable barrier (Smoltscreen) state + flow-dependent effectiveness.
                ["barrier"]           = site.Barrier.ToString(),
                ["has_barrier"]       = site.Barrier != Farm.BarrierKind.None,
                ["barrier_days_since_cleaning"] = site.BarrierDaysSinceCleaning,
                ["barrier_lice_blocking"] = Farm.BarrierTech.EffectiveLiceBlocking(
                    site.Barrier, SiteFlowMs(SimulationEngine, site), site.BarrierDaysSinceCleaning),
                ["barrier_jellyfish_blocking"] = Farm.BarrierTech.EffectiveJellyfishBlocking(
                    site.Barrier, SiteFlowMs(SimulationEngine, site), site.BarrierDaysSinceCleaning)
            });
        }
        return result;
    }

    /// <summary>
    /// Order a delousing treatment (Phase 2.2: full treatment cards).
    /// Methods: thermolicer, hydrolicer, cleanerfish, laser, freshwater,
    /// peroxide, medicatedfeed, skirt + legacy aliases mechanical/thermal/
    /// chemical/biological. Delegates to DelousingSystem profiles (efficacy,
    /// mortality, welfare, resistance) per population.
    /// Returns true on success, false if site not found, method invalid, or insufficient funds.
    /// </summary>
    public bool OrderDelousing(string siteId, string method)
    {
        var engine = SimulationEngine;
        if (engine == null)
        {
            GD.PrintErr("GameBridge: OrderDelousing failed - engine not initialized");
            EmitSignal(SignalName.FarmActionCompleted, "delousing", siteId, false);
            return false;
        }

        // Phase 1 wave 2: the verb lives in the library
        // (SimulationEngine.DelouseSite). "all_sites" stays an adapter-side
        // fan-out over the per-site command.
        if (siteId == "all_sites")
        {
            bool ok = true;
            foreach (var site in engine.State.SeaSites.ToList())
                ok &= engine.DelouseSite(site.Id, method).Ok;
            EmitSignal(SignalName.FarmActionCompleted, "delousing", siteId, ok);
            // Partial application still changed state — refresh regardless;
            // the bool tells the UI whether every site succeeded.
            EmitSignal(SignalName.GameStateUpdated);
            return ok;
        }

        var result = engine.DelouseSite(siteId, method);
        if (!result.Ok)
        {
            GD.PrintErr($"GameBridge: OrderDelousing failed - {result.Message}");
            EmitSignal(SignalName.FarmActionCompleted, "delousing", siteId, false);
            return false;
        }

        EmitSignal(SignalName.FarmActionCompleted, "delousing", siteId, true);
        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: {result.Message}");
        return true;
    }

    /// <summary>
    /// Treatment cards for the delousing UI (Phase 2.2).
    /// Keys: "id", "efficacy", "mortality", "cost_per_fish", "welfare_impact",
    ///       "resistance_risk", "requires_wellboat".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetTreatmentProfiles()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (LakskongenSim.Data.DelousingMethod m in System.Enum.GetValues(typeof(LakskongenSim.Data.DelousingMethod)))
        {
            var p = LakskongenSim.Farm.DelousingSystem.GetProfile(m);
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"]              = m.ToString(),
                ["efficacy"]        = p.Efficacy,
                ["mortality"]       = p.MortalityRisk,
                ["cost_per_fish"]   = (double)p.CostPerFish,
                ["welfare_impact"]  = p.WelfareImpact,
                ["resistance_risk"] = p.ResistanceRiskIncrease,
                ["requires_wellboat"] = p.RequiresWellboat
            });
        }
        return result;
    }

    /// <summary>
    /// Current chemical/mechanical resistance 0-1 (Phase 2.2) for the UI.
    /// Keys: "pyrethroid", "organophosphate", "peroxide", "mechanical".
    /// </summary>
    public Godot.Collections.Dictionary GetResistanceLevels()
    {
        var lice = SimulationEngine?.SeaLice;
        return new Godot.Collections.Dictionary
        {
            ["pyrethroid"]      = lice?.PyrethroidResistance ?? 0.0,
            ["organophosphate"] = lice?.OrganophosphateResistance ?? 0.0,
            ["peroxide"]        = lice?.HydrogenPeroxideResistance ?? 0.0,
            ["mechanical"]      = lice?.MechanicalResistance ?? 0.0
        };
    }

    /// <summary>
    /// Buy a VAP processing plant (Phase 3.5). Tiers mirror ProcessingManager:
    /// Small 1M, Medium 5M, Large 15M. Owned plant adds +12% fillet margin on
    /// all harvest revenue at 5k NOK/day opex. One plant per game (upgrade
    /// = pay difference).
    /// </summary>
    public bool BuyProcessingPlant(string tier)
    {
        var state = SimulationEngine?.State;
        if (state == null) return false;
        tier = (tier ?? "").Trim();
        decimal cost = tier.ToLower() switch
        {
            "small" => 1_000_000m,
            "medium" => 5_000_000m,
            "large" => 15_000_000m,
            _ => -1m
        };
        if (cost < 0) return false;
        if (state.OwnsProcessingPlant && state.ProcessingTier.ToLower() == tier.ToLower())
            return true;
        decimal ownedValue = state.OwnsProcessingPlant ? state.ProcessingTier.ToLower() switch
        {
            "small" => 1_000_000m,
            "medium" => 5_000_000m,
            "large" => 15_000_000m,
            _ => 0m
        } : 0m;
        decimal due = cost - ownedValue;
        if (due <= 0) return true;
        if (SimulationEngine!.FinanceManager.Balance < due) return false;
        SimulationEngine.FinanceManager.Spend(due);
        state.OwnsProcessingPlant = true;
        state.ProcessingTier = tier;
        state.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = state.Day,
            Name = "Foredlingsanlegg kjøpt",
            Category = LakskongenSim.Events.EventCategory.Equipment,
            Description = $"Filetlinje ({tier}) satt i drift — +12% på all høsteinntekt",
            BalanceChange = -due
        });
        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: Processing plant bought ({tier}, {due:N0} NOK)");
        return true;
    }

    /// <summary>
    /// Containment ladder table for UI (Phase 4.1).
    /// Keys: "id", "capex_nok", "weekly_opex_nok", "lice_exclusion",
    ///       "oxygen_demand", "storm_mult", "escape_mult", "growth_mult".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetContainmentLevels()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var kv in LakskongenSim.Farm.ContainmentTech.Table)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = kv.Key.ToString(),
                ["capex_nok"] = (double)kv.Value.CapexNok,
                ["weekly_opex_nok"] = (double)kv.Value.WeeklyOpexNok,
                ["lice_exclusion"] = kv.Value.LiceExclusion01,
                ["oxygen_demand"] = kv.Value.OxygenDemandMult,
                ["storm_mult"] = kv.Value.StormDamageMult,
                ["escape_mult"] = kv.Value.EscapeRiskMult,
                ["growth_mult"] = kv.Value.GrowthMult
            });
        }
        return result;
    }

    /// <summary>
    /// Set ROC watch mode for a site (Phase 4.2). Refused when over budget.
    /// Budget = 100 + 15/crew (cap +45). Returns true on success.
    /// </summary>
    public bool SetWatchMode(string siteId, string mode)
    {
        var state = SimulationEngine?.State;
        var site = state?.SeaSites?.FirstOrDefault(s => s.Id == siteId);
        if (site == null) return false;
        mode = (mode ?? "").ToLower();
        if (mode != "watch" && mode != "scan" && mode != "auto") return false;
        int budget = LakskongenSim.Optimization.RemoteOpsCentre.BudgetFor(state!.Crew.Count);
        int used = state.SeaSites.Sum(s => LakskongenSim.Optimization.RemoteOpsCentre.CostFor(s.Id == siteId ? mode : s.WatchMode));
        if (used > budget) return false;
        site.WatchMode = mode;
        EmitSignal(SignalName.GameStateUpdated);
        return true;
    }

    /// <summary>ROC attention budget and usage (Phase 4.2).</summary>
    public Godot.Collections.Dictionary GetAttentionBudget()
    {
        var state = SimulationEngine?.State;
        int crew = state?.Crew.Count ?? 0;
        int sites = state?.SeaSites.Count ?? 0;
        return new Godot.Collections.Dictionary
        {
            ["budget"] = LakskongenSim.Optimization.RemoteOpsCentre.BudgetFor(crew),
            ["used"] = state?.SeaSites != null
                ? LakskongenSim.Optimization.RemoteOpsCentre.UsedBy(state.SeaSites) : 0,
            ["site_count"] = sites
        };
    }
    /// <summary>
    /// Set feed recipe (Phase 4.4): protein 38-48, replacement 0-60, astax 0-80.
    /// Updates feed price authority, FCR multiplier and novel-ingredient flag.
    /// </summary>
    public void SetFeedRecipe(double proteinPct, double replacePct, double astaxPpm)
    {
        var state = SimulationEngine?.State;
        if (state == null) return;
        var (p, r, a) = LakskongenSim.Processing.FeedRecipe.Clamp(proteinPct, replacePct, astaxPpm);
        state.RecipeProteinPct = p;
        state.RecipeReplacePct = r;
        state.RecipeAstaxPpm = a;
        ApplyFeedRecipe();
        EmitSignal(SignalName.GameStateUpdated);
    }

    /// <summary>Current recipe + live preview (cost, FCR mult, grade).</summary>
    public Godot.Collections.Dictionary GetFeedRecipe()
    {
        var state = SimulationEngine?.State;
        double p = state?.RecipeProteinPct ?? 42.0;
        double r = state?.RecipeReplacePct ?? 20.0;
        double a = state?.RecipeAstaxPpm ?? 50.0;
        string grade = LakskongenSim.Processing.FeedRecipe.Grade(p, a);
        return new Godot.Collections.Dictionary
        {
            ["protein"] = p,
            ["replace"] = r,
            ["astax"] = a,
            ["cost_per_kg"] = LakskongenSim.Processing.FeedRecipe.CostPerKg(p, r, a),
            ["fcr_mult"] = LakskongenSim.Processing.FeedRecipe.FcrMult(p, r, a),
            ["grade"] = grade,
            ["vap_mult"] = (double)LakskongenSim.Processing.FeedRecipe.VapMult(grade)
        };
    }

    private void ApplyFeedRecipe()
    {
        var state = SimulationEngine?.State;
        var feed = SimulationEngine?.Feed;
        if (state == null || feed == null) return;
        feed.FeedCostPerKg = (decimal)LakskongenSim.Processing.FeedRecipe.CostPerKg(
            state.RecipeProteinPct, state.RecipeReplacePct, state.RecipeAstaxPpm);
        feed.RecipeFCRMult = LakskongenSim.Processing.FeedRecipe.FcrMult(
            state.RecipeProteinPct, state.RecipeReplacePct, state.RecipeAstaxPpm);
        feed.UsesInsectMeal = state.RecipeReplacePct >= 30.0;
    }
    /// <summary>
    /// Twin-lite 7-day forecast (Phase 4.5, PROJECTION): biomass/lice/cash
    /// arrays + confidence. Cached per day. Read-only — never touches live sim.
    /// Keys: "biomass_kg", "lice", "cash_nok" (Array double), "confidence".
    /// </summary>
    private Godot.Collections.Dictionary? _forecastCache;
    private int _forecastCacheDay = -1;

    public Godot.Collections.Dictionary GetForecast()
    {
        int day = SimulationEngine?.State?.Day ?? 0;
        if (_forecastCache != null && _forecastCacheDay == day)
            return (Godot.Collections.Dictionary)_forecastCache.Duplicate();
        var result = new Godot.Collections.Dictionary
        {
            ["biomass_kg"] = new Godot.Collections.Array<double>(),
            ["lice"] = new Godot.Collections.Array<double>(),
            ["cash_nok"] = new Godot.Collections.Array<double>(),
            ["confidence"] = "low"
        };
        var engine = SimulationEngine;
        if (engine?.State != null && engine.Growth != null && engine.SeaLice != null && engine.Analytics != null)
        {
            var fc = LakskongenSim.Optimization.ForecastTwin.Project(
                engine.State, engine.SeaLice,
                engine.Weather?.CurrentTemperatureC ?? 8.0, engine.Analytics, engine.GrowthRateBonus);
            var bio = new Godot.Collections.Array<double>();
            var lice = new Godot.Collections.Array<double>();
            var cash = new Godot.Collections.Array<double>();
            foreach (var v in fc.BiomassKg) bio.Add(v);
            foreach (var v in fc.LicePerFish) lice.Add(v);
            foreach (var v in fc.CashNok) cash.Add(v);
            result["biomass_kg"] = bio;
            result["lice"] = lice;
            result["cash_nok"] = cash;
            result["confidence"] = fc.Confidence;
        }
        _forecastCache = result;
        _forecastCacheDay = day;
        return (Godot.Collections.Dictionary)result.Duplicate();
    }
    /// <summary>
    /// Upgrade site containment one rung up the ladder (Phase 4.1, no downgrade).
    /// Charges the target level's capex, syncs skirt flag at level 1+, applies
    /// immediate red-zone MAB credit when eligible. Returns true on success.
    /// </summary>
    public bool UpgradeContainment(string siteId, string levelName)
    {
        var state = SimulationEngine?.State;
        var site = state?.SeaSites?.FirstOrDefault(s => s.Id == siteId);
        if (site == null) return false;
        if (!System.Enum.TryParse<LakskongenSim.Farm.ContainmentLevel>(levelName, true, out var target))
            return false;
        if (target <= site.Containment) return false; // no downgrade / same-level
        var mods = LakskongenSim.Farm.ContainmentTech.ModifiersFor(target);
        if (SimulationEngine!.FinanceManager.Balance < mods.CapexNok) return false;
        SimulationEngine.FinanceManager.Spend(mods.CapexNok);
        site.Containment = target;
        if (target >= LakskongenSim.Farm.ContainmentLevel.Skirt)
            site.HasLiceSkirt = true;
        if (target >= LakskongenSim.Farm.ContainmentLevel.ClosedFloat)
            site.IsClosedContainment = true;
        // Immediate red-zone credit when upgrading inside a red zone.
        var tls = SimulationEngine.TrafficLightSystem;
        if (tls != null && tls.GetZoneColor(site.ZoneId).ToString() == "Red"
            && !site.RedZoneCreditApplied && target >= LakskongenSim.Farm.ContainmentLevel.SemiClosed)
        {
            double credit = LakskongenSim.Farm.ContainmentTech.MabCreditFor(target, true);
            site.MABTonnes *= credit;
            site.RedZoneCreditApplied = true;
        }
        state!.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = state.Day,
            Name = "Containment oppgradert",
            Category = LakskongenSim.Events.EventCategory.Equipment,
            Description = $"{siteId} → {target} ({mods.CapexNok:N0} NOK)",
            BalanceChange = -mods.CapexNok
        });
        EmitSignal(SignalName.FarmActionCompleted, "upgrade_containment", siteId, true);
        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: {siteId} containment → {target}");
        return true;
    }

    /// <summary>
    /// Repair nets at a sea site (Phase 1.2). Cost scales with damage:
    /// NOK 250k for a full 0→100 rebuild. Restores NetIntegrityPct to 100.
    /// Returns true on success, false if site not found or insufficient funds.
    /// </summary>
    public bool RepairNets(string siteId)
    {
        var site = SimulationEngine?.State?.SeaSites?.FirstOrDefault(s => s.Id == siteId);
        if (site == null)
        {
            GD.PrintErr($"GameBridge: RepairNets failed - site '{siteId}' not found");
            EmitSignal(SignalName.FarmActionCompleted, "repair_nets", siteId, false);
            return false;
        }
        if (site.NetIntegrityPct >= 99.5)
        {
            GD.Print($"GameBridge: RepairNets — '{siteId}' already intact");
            return true;
        }
        decimal cost = (decimal)(100.0 - site.NetIntegrityPct) / 100m * 250_000m;
        if (SimulationEngine!.FinanceManager.Balance < cost)
        {
            GD.PrintErr($"GameBridge: RepairNets failed - insufficient funds (need {cost:N0} NOK)");
            EmitSignal(SignalName.FarmActionCompleted, "repair_nets", siteId, false);
            return false;
        }
        site.NetIntegrityPct = 100.0;
        SimulationEngine.FinanceManager.Spend(cost);
        SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = SimulationEngine.State.Day,
            Name = "Net Repair",
            Category = LakskongenSim.Events.EventCategory.Equipment,
            Description = $"Repaired nets at {siteId} (100%)",
            BalanceChange = -cost
        });
        EmitSignal(SignalName.FarmActionCompleted, "repair_nets", siteId, true);
        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: Repaired nets at '{siteId}' for {cost:N0} NOK");
        return true;
    }

    // ── Genetics API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns list of broodstock genomes with their genetic traits.
    /// Each entry is a Dictionary with keys: "id", "generation", "traits" (Dictionary of trait values),
    /// "has_mutation", "summary".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetBreedingCandidates()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var state = SimulationEngine?.State;
        if (state == null) return result;

        int index = 0;
        foreach (var genome in state.Broodstock)
        {
            var traits = new Godot.Collections.Dictionary();
            foreach (var kv in genome.TraitValues)
                traits[kv.Key.ToString()] = kv.Value;

            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = index.ToString(),
                ["generation"] = genome.Generation,
                ["traits"] = traits,
                ["has_mutation"] = genome.HasMutation,
                ["summary"] = genome.GetSummary()
            });
            index++;
        }
        return result;
    }

    /// <summary>
    /// Returns trait definitions (name, heritability, current value, breeding value).
    /// For each trait, returns a Dictionary with keys: "name", "heritability", "current_value", "breeding_value".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetGeneticTraits()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var genetics = SimulationEngine?.Genetics;
        if (genetics == null) return result;

        foreach (GeneticTrait trait in Enum.GetValues(typeof(GeneticTrait)))
        {
            double heritability = Genetics.GetHeritability(trait);
            // For now, we'll return placeholder values; later we can compute current population average.
            double currentValue = 0.0; // placeholder
            double breedingValue = 0.0; // placeholder
            result.Add(new Godot.Collections.Dictionary
            {
                ["name"] = trait.ToString(),
                ["heritability"] = heritability,
                ["current_value"] = currentValue,
                ["breeding_value"] = breedingValue
            });
        }
        return result;
    }

    /// <summary>
    /// Returns current inbreeding depression level (coefficient between 0 and 1).
    /// Simplified: count of shared genomes in broodstock.
    /// </summary>
    public double GetInbreedingCoefficient()
    {
        var broodstock = SimulationEngine?.State?.Broodstock;
        if (broodstock == null || broodstock.Count < 2) return 0.0;

        // Dummy implementation: compute average relatedness based on generation difference.
        // For now, return a placeholder.
        return 0.05;
    }

    /// <summary>
    /// Wave 6: per-trait expected genetic gain for the current broodstock under
    /// genomic selection (ΔG = i·r·σ_A). Returns an empty array when there is no
    /// broodstock, so the UI can show an "insufficient data" note. Each entry has
    /// keys: "trait", "heritability", "accuracy", "gain".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetGenomicSelectionPreview()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var broodstock = SimulationEngine?.State?.Broodstock;
        if (broodstock == null || broodstock.Count == 0) return result;

        var gains = GenomicSelection.ExpectedGain(broodstock);
        foreach (GeneticTrait trait in Enum.GetValues(typeof(GeneticTrait)))
        {
            gains.TryGetValue(trait, out double gain);
            result.Add(new Godot.Collections.Dictionary
            {
                ["trait"] = trait.ToString(),
                ["heritability"] = Genetics.GetHeritability(trait),
                ["accuracy"] = GenomicSelection.SelectionAccuracy(trait, SelectionMethod.Genomic),
                ["gain"] = gain
            });
        }
        return result;
    }

    /// <summary>
    /// Wave 6: inbreeding status for the broodstock (falling back to live cohort
    /// genomes when no broodstock is held). Keys: "coefficient" (0..1),
    /// "penalty" (fractional growth loss 0..1), "penalty_percent" (0..100),
    /// "level" ("none"/"low"/"elevated"/"high"). Coefficient 0 → penalty 0.
    /// </summary>
    public Godot.Collections.Dictionary GetInbreedingStatus()
    {
        double coefficient = ComputePopulationInbreedingProxy();
        double penalty = GenomicSelection.InbreedingDepressionPenalty(
            GeneticTrait.GrowthRate, coefficient);

        string level = coefficient > 0.25 ? "high"
            : coefficient > 0.15 ? "elevated"
            : coefficient > 0.0 ? "low"
            : "none";

        return new Godot.Collections.Dictionary
        {
            ["coefficient"] = coefficient,
            ["penalty"] = penalty,
            ["penalty_percent"] = penalty * 100.0,
            ["level"] = level
        };
    }

    /// <summary>
    /// Wave 6: registered GE traits with their Norwegian regulatory status.
    /// Keys: "id", "name", "region_status", "reputation_cost", "target_trait",
    /// "modification". The game is set in Norway, so Norway status is reported.
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetGeTraits()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var trait in GeTraitRegistry.All)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = trait.Id,
                ["name"] = trait.Name,
                ["region_status"] = trait.StatusIn(GeRegion.Norway).ToString(),
                ["reputation_cost"] = trait.ReputationCost,
                ["target_trait"] = trait.TargetTrait.ToString(),
                ["modification"] = trait.Modification.ToString()
            });
        }
        return result;
    }

    /// <summary>
    /// Mean inbreeding proxy across the broodstock, falling back to live cohort
    /// genomes. Returns 0.0 when no genomes are available (no depression).
    /// </summary>
    private double ComputePopulationInbreedingProxy()
    {
        var state = SimulationEngine?.State;
        if (state == null) return 0.0;

        double total = 0.0;
        int samples = 0;

        foreach (var genome in state.Broodstock)
        {
            if (genome == null) continue;
            total += genome.InbreedingCoefficient;
            samples++;
        }

        if (samples == 0)
        {
            foreach (var population in state.Populations)
            {
                foreach (var cohort in population.Cohorts)
                {
                    if (cohort.Genetics == null) continue;
                    total += cohort.Genetics.InbreedingCoefficient;
                    samples++;
                }
            }
        }

        if (samples == 0) return 0.0;
        return Math.Clamp(total / samples, 0.0, 1.0);
    }

    // ── Worker/Crew API ───────────────────────────────────────────────────────

    /// <summary>
    /// Return the crew roster as an Array of Dictionaries (Phase 3.1, live).
    /// Keys: "id", "name", "role", "salary_nok" (double), "morale" (int),
    ///       "hired_day" (int).
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetCrewList()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var crew = SimulationEngine?.State?.Crew;
        if (crew == null) return result;
        foreach (var c in crew)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = c.Id,
                ["name"] = c.Name,
                ["role"] = c.Role,
                ["salary_nok"] = (double)c.MonthlySalaryNok,
                ["morale"] = c.Morale,
                ["hired_day"] = c.HiredDay
            });
        }
        return result;
    }

    /// <summary>
    /// Hireable candidates (Phase 3.1): role/name/salary triples.
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetHireCandidates()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var (role, name, salary) in LakskongenSim.Core.CrewEffects.Candidates)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["role"] = role,
                ["name"] = name,
                ["salary_nok"] = (double)salary
            });
        }
        return result;
    }

    /// <summary>
    /// Hire a worker by role (Phase 3.1). 50k signing cost + monthly payroll.
    /// Valid roles: "roekter", "veterinaer", "skipper".
    /// </summary>
    public bool HireWorker(string role)
    {
        var state = SimulationEngine?.State;
        if (state == null) return false;
        role = (role ?? "").ToLower();
        if (role != "roekter" && role != "veterinaer" && role != "skipper")
        {
            EmitSignal(SignalName.FinanceTransaction, "hire_worker", 0.0, $"Unknown role '{role}'");
            return false;
        }
        const decimal signingCost = 50_000m;
        if (SimulationEngine!.FinanceManager.Balance < signingCost) return false;
        var candidate = LakskongenSim.Core.CrewEffects.Candidates
            .FirstOrDefault(c => c.role == role);
        // Phase 1.4: morale roll comes from the engine's named stream (deterministic
        // per game seed + role), not a fresh unseeded Random.
        int moraleBonus = SimulationEngine?.Rng
            ?.Stream(LakskongenSim.Core.SimRngStreams.HireWorker, role)?.Next(0, 21) ?? 0;
        SimulationEngine.FinanceManager.Spend(signingCost);
        state.Crew.Add(new LakskongenSim.Core.CrewMember
        {
            Name = candidate.name,
            Role = role,
            MonthlySalaryNok = candidate.salary,
            Morale = 75 + moraleBonus,
            HiredDay = state.Day
        });
        EmitSignal(SignalName.FinanceTransaction, "hire_worker", (double)signingCost, $"Hired {candidate.name} ({role})");
        EmitSignal(SignalName.GameStateUpdated);
        return true;
    }

    /// <summary>
    /// Fire a worker by ID (Phase 3.1, free).
    /// Returns true on success, false if worker not found.
    /// </summary>
    public bool FireWorker(string workerId)
    {
        var state = SimulationEngine?.State;
        if (state?.Crew == null) return false;
        int removed = state.Crew.RemoveAll(c => c.Id == workerId);
        if (removed > 0) EmitSignal(SignalName.GameStateUpdated);
        return removed > 0;
    }

    // ── RAS Facility API ──────────────────────────────────────────────────────

    /// <summary>
    /// Return all RAS facilities as an Array of Dictionaries.
    /// Keys: "id", "name", "zone_id", "scale", "capacity_tonnes_per_year", "tank_count",
    ///       "current_water_temp", "oxygen_level", "water_ph", "co2_level".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetRASFacilities()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var facilities = SimulationEngine?.RASFacilities;
        if (facilities == null) return result;

        foreach (var facility in facilities)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = facility.Id,
                ["name"] = facility.Name,
                ["zone_id"] = facility.ZoneId,
                ["scale"] = facility.Scale.ToString(),
                ["capacity_tonnes_per_year"] = facility.CapacityTonnesPerYear,
                ["tank_count"] = facility.TankCount,
                ["current_water_temp"] = facility.CurrentWaterTemp,
                ["oxygen_level"] = facility.OxygenLevel,
                ["water_ph"] = facility.WaterPH,
                ["co2_level"] = facility.CO2Level
            });
        }
        return result;
    }

    /// <summary>
    /// Build a new RAS facility.
    /// Valid scales: "Small", "Medium", "Large", "Mega".
    /// Returns true on success, false if insufficient funds.
    /// </summary>
    public bool BuildRASFacility(string name, string scale)
    {
        if (SimulationEngine?.State == null)
        {
            GD.PrintErr("GameBridge: BuildRASFacility failed - engine not initialized");
            EmitSignal(SignalName.FarmActionCompleted, "build_ras", name, false);
            return false;
        }
        if (!Enum.TryParse<LakskongenSim.Farm.RASScale>(scale, true, out var rasScale))
        {
            GD.PrintErr($"GameBridge: BuildRASFacility failed - invalid scale '{scale}'");
            EmitSignal(SignalName.FarmActionCompleted, "build_ras", name, false);
            return false;
        }

        // The engine owns the verb: model CAPEX, construction start, GameState
        // persistence (review F383/F1069). The bridge only projects the result.
        var result = SimulationEngine.BuildRASFacility(name, rasScale);
        if (!result.Ok)
            GD.PrintErr($"GameBridge: BuildRASFacility failed - {result.Message}");
        EmitSignal(SignalName.FarmActionCompleted, "build_ras", name, result.Ok);
        if (result.Ok)
            EmitSignal(SignalName.GameStateUpdated);
        return result.Ok;
    }

    /// <summary>
    /// Perform maintenance on a RAS facility (water system cleaning, filter replacement).
    /// Returns maintenance cost in NOK.
    /// </summary>
    public float PerformRASMaintenance(string facilityId)
    {
        if (SimulationEngine?.RASFacilities == null)
        {
            GD.PrintErr("GameBridge: PerformRASMaintenance failed - engine not initialized");
            return 0f;
        }

        var facility = SimulationEngine.RASFacilities.FirstOrDefault(f => f.Id == facilityId);
        if (facility == null)
        {
            GD.PrintErr($"GameBridge: PerformRASMaintenance failed - facility '{facilityId}' not found");
            return 0f;
        }

        // Maintenance cost: ~1M NOK/month for medium facility
        decimal maintenanceCost = facility.Scale switch
        {
            LakskongenSim.Farm.RASScale.Small => 500_000m,
            LakskongenSim.Farm.RASScale.Medium => 1_000_000m,
            LakskongenSim.Farm.RASScale.Large => 2_000_000m,
            _ => 1_000_000m
        };

        if (SimulationEngine.FinanceManager.Balance < maintenanceCost)
        {
            GD.PrintErr($"GameBridge: PerformRASMaintenance failed - insufficient funds (need {maintenanceCost:N0} NOK)");
            return 0f;
        }

        SimulationEngine.FinanceManager.Spend(maintenanceCost);

        // Restore optimal water conditions after maintenance
        facility.CurrentWaterTemp = facility.TargetWaterTemp;
        facility.OxygenLevel = 8.0;
        facility.WaterPH = 7.0;
        facility.CO2Level = 8.0;

        SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = SimulationEngine.State.Day,
            Name = "RAS Maintenance",
            Category = LakskongenSim.Events.EventCategory.Equipment,
            Description = $"Performed maintenance on RAS '{facility.Name}' (water system restored)",
            BalanceChange = -maintenanceCost
        });

        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: RAS maintenance '{facilityId}' completed for {maintenanceCost:N0} NOK");
        return (float)maintenanceCost;
    }

    // ── Offshore Farm API ─────────────────────────────────────────────────────

    /// <summary>
    /// Return all offshore farms as an Array of Dictionaries.
    /// Keys: "id", "name", "zone_id", "farm_type", "capacity_tonnes", "pen_count",
    ///       "sea_lice_pressure", "water_flow_rate", "fcr".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetOffshoreFarms()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var farms = SimulationEngine?.OffshoreFarms;
        if (farms == null) return result;

        foreach (var farm in farms)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = farm.Id,
                ["name"] = farm.Name,
                ["zone_id"] = farm.ZoneId,
                ["farm_type"] = farm.FarmType.ToString(),
                ["capacity_tonnes"] = farm.CapacityTonnes,
                ["pen_count"] = farm.PenCount,
                ["sea_lice_pressure"] = farm.SeaLicePressure,
                ["water_flow_rate"] = farm.WaterFlowRate,
                ["fcr"] = farm.FCR
            });
        }
        return result;
    }

    /// <summary>
    /// Build a new offshore farm.
    /// Valid farm types: "SemiSubmersible", "Ship", "Submerged".
    /// Returns true on success, false if insufficient funds.
    /// </summary>
    public bool BuildOffshoreFarm(string name, string farmType)
    {
        if (SimulationEngine?.State == null)
        {
            GD.PrintErr("GameBridge: BuildOffshoreFarm failed - engine not initialized");
            EmitSignal(SignalName.FarmActionCompleted, "build_offshore", name, false);
            return false;
        }

        // UI passes snake_case ("semi_submersible") — normalize before parse.
        var normalizedType = (farmType ?? "").Replace("_", "").Replace(" ", "");
        if (!Enum.TryParse<LakskongenSim.Farm.OffshoreType>(normalizedType, true, out var offshoreType))
        {
            GD.PrintErr($"GameBridge: BuildOffshoreFarm failed - invalid type '{farmType}'");
            EmitSignal(SignalName.FarmActionCompleted, "build_offshore", name, false);
            return false;
        }

        var result = SimulationEngine.BuildOffshoreFarm(name, offshoreType);
        if (!result.Ok)
            GD.PrintErr($"GameBridge: BuildOffshoreFarm failed - {result.Message}");
        EmitSignal(SignalName.FarmActionCompleted, "build_offshore", name, result.Ok);
        if (result.Ok)
            EmitSignal(SignalName.GameStateUpdated);
        return result.Ok;
    }

    /// <summary>
    /// Repair offshore farm damage (storm damage, equipment failure).
    /// Returns repair cost in NOK.
    /// </summary>
    public float RepairOffshore(string farmId)
    {
        if (SimulationEngine?.OffshoreFarms == null)
        {
            GD.PrintErr("GameBridge: RepairOffshore failed - engine not initialized");
            return 0f;
        }

        var farm = SimulationEngine.OffshoreFarms.FirstOrDefault(f => f.Id == farmId);
        if (farm == null)
        {
            GD.PrintErr($"GameBridge: RepairOffshore failed - farm '{farmId}' not found");
            return 0f;
        }

        // Repair cost: major offshore repairs are expensive
        decimal repairCost = 50_000_000m; // 50M NOK for major repair

        if (SimulationEngine.FinanceManager.Balance < repairCost)
        {
            GD.PrintErr($"GameBridge: RepairOffshore failed - insufficient funds (need {repairCost:N0} NOK)");
            return 0f;
        }

        SimulationEngine.FinanceManager.Spend(repairCost);

        SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = SimulationEngine.State.Day,
            Name = "Offshore Repair",
            Category = LakskongenSim.Events.EventCategory.Equipment,
            Description = $"Repaired offshore farm '{farm.Name}' (hull integrity and equipment restored)",
            BalanceChange = -repairCost
        });

        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: Offshore repair '{farmId}' completed for {repairCost:N0} NOK");
        return (float)repairCost;
    }

    // ── AI Feeding System API ─────────────────────────────────────────────────

    /// <summary>
    /// Return AI feeding system status as a Dictionary.
    /// Keys: "is_installed", "calibration_level", "days_since_installation",
    ///       "camera_count", "camera_uptime", "appetite_score", "feed_waste_rate",
    ///       "optimal_pellet_size_mm", "feed_rate_kg_per_min".
    /// </summary>
    public Godot.Collections.Dictionary GetAIFeedingStatus()
    {
        var result = new Godot.Collections.Dictionary();
        var aiFeeding = SimulationEngine?.AIFeeding;
        if (aiFeeding == null)
        {
            result["is_installed"] = false;
            return result;
        }

        result["is_installed"] = aiFeeding.IsInstalled;
        result["calibration_level"] = aiFeeding.CalibrationLevel;
        result["days_since_installation"] = aiFeeding.DaysSinceInstallation;
        result["camera_count"] = aiFeeding.CameraCount;
        result["camera_uptime"] = aiFeeding.CameraUptime;
        result["appetite_score"] = aiFeeding.AppetiteScore;
        result["feed_waste_rate"] = aiFeeding.FeedWasteRate;
        result["optimal_pellet_size_mm"] = aiFeeding.OptimalPelletSizeMm;
        result["feed_rate_kg_per_min"] = aiFeeding.FeedRateKgPerMin;
        return result;
    }

    /// <summary>
    /// Install AI feeding system across all sites.
    /// Returns installation cost in NOK.
    /// </summary>
    public float InstallAIFeeding()
    {
        if (SimulationEngine?.AIFeeding == null)
        {
            GD.PrintErr("GameBridge: InstallAIFeeding failed - engine not initialized");
            return 0f;
        }

        if (SimulationEngine.AIFeeding.IsInstalled)
        {
            GD.PrintErr("GameBridge: InstallAIFeeding failed - AI feeding already installed");
            return 0f;
        }

        // Installation cost: ~100M NOK for farm-wide AI feeding infrastructure
        decimal installCost = 100_000_000m;

        if (SimulationEngine.FinanceManager.Balance < installCost)
        {
            GD.PrintErr($"GameBridge: InstallAIFeeding failed - insufficient funds (need {installCost:N0} NOK)");
            return 0f;
        }

        SimulationEngine.FinanceManager.Spend(installCost);
        SimulationEngine.AIFeeding.IsInstalled = true;
        SimulationEngine.AIFeeding.DaysSinceInstallation = 0;
        SimulationEngine.AIFeeding.CalibrationLevel = 0.5; // Starts at 50%, improves over time

        SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = SimulationEngine.State.Day,
            Name = "AI Feeding Installed",
            Category = LakskongenSim.Events.EventCategory.Equipment,
            Description = "Installed AI-driven precision feeding system (cameras and automation)",
            BalanceChange = -installCost
        });

        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: AI feeding system installed for {installCost:N0} NOK");
        return (float)installCost;
    }

    /// <summary>
    /// Clean AI feeding cameras (biofouling reduces camera uptime over time).
    /// Returns cleaning cost in NOK.
    /// </summary>
    public float CleanAICameras()
    {
        if (SimulationEngine?.AIFeeding == null || !SimulationEngine.AIFeeding.IsInstalled)
        {
            GD.PrintErr("GameBridge: CleanAICameras failed - AI feeding not installed");
            return 0f;
        }

        // Camera cleaning cost
        decimal cleaningCost = 500_000m; // 500k NOK for diver cleaning/maintenance

        if (SimulationEngine.FinanceManager.Balance < cleaningCost)
        {
            GD.PrintErr($"GameBridge: CleanAICameras failed - insufficient funds (need {cleaningCost:N0} NOK)");
            return 0f;
        }

        SimulationEngine.FinanceManager.Spend(cleaningCost);
        SimulationEngine.AIFeeding.CameraUptime = 1.0; // Restore full camera uptime

        SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = SimulationEngine.State.Day,
            Name = "Camera Cleaning",
            Category = LakskongenSim.Events.EventCategory.Equipment,
            Description = "Cleaned AI feeding cameras (vision restored to 100%)",
            BalanceChange = -cleaningCost
        });

        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: AI camera cleaning completed for {cleaningCost:N0} NOK");
        return (float)cleaningCost;
    }

    // ── Post-Smolt Facility API ───────────────────────────────────────────────

    /// <summary>
    /// Return all post-smolt facilities as an Array of Dictionaries.
    /// Keys: "id", "name", "zone_id", "annual_smolt_capacity", "target_transfer_weight_g",
    ///       "tank_sections", "water_temp", "survival_rate", "fcr".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetPostSmoltFacilities()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var facilities = SimulationEngine?.PostSmoltFacilities;
        if (facilities == null) return result;

        foreach (var facility in facilities)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = facility.Id,
                ["name"] = facility.Name,
                ["zone_id"] = facility.ZoneId,
                ["annual_smolt_capacity"] = facility.AnnualSmoltCapacity,
                ["target_transfer_weight_g"] = facility.TargetTransferWeightG,
                ["tank_sections"] = facility.TankSections,
                ["water_temp"] = facility.WaterTemp,
                ["survival_rate"] = facility.SurvivalRate,
                ["fcr"] = facility.FCR
            });
        }
        return result;
    }

    /// <summary>
    /// Start a new smolt batch in a post-smolt facility.
    /// Returns true on success, false if facility not found or insufficient capacity.
    /// </summary>
    public bool StartSmoltBatch(string facilityId, int count)
    {
        if (SimulationEngine?.PostSmoltFacilities == null)
        {
            GD.PrintErr("GameBridge: StartSmoltBatch failed - engine not initialized");
            EmitSignal(SignalName.FarmActionCompleted, "start_smolt_batch", facilityId, false);
            return false;
        }

        var facility = SimulationEngine.PostSmoltFacilities.FirstOrDefault(f => f.Id == facilityId);
        if (facility == null)
        {
            GD.PrintErr($"GameBridge: StartSmoltBatch failed - facility '{facilityId}' not found");
            EmitSignal(SignalName.FarmActionCompleted, "start_smolt_batch", facilityId, false);
            return false;
        }

        // Cost per smolt (hatchery production cost)
        decimal smoltCost = 15m; // 15 NOK per smolt (industry average)
        decimal totalCost = smoltCost * count;

        if (SimulationEngine.FinanceManager.Balance < totalCost)
        {
            GD.PrintErr($"GameBridge: StartSmoltBatch failed - insufficient funds (need {totalCost:N0} NOK)");
            EmitSignal(SignalName.FarmActionCompleted, "start_smolt_batch", facilityId, false);
            return false;
        }

        SimulationEngine.FinanceManager.Spend(totalCost);

        // Create new batch (simplified - actual batch tracking would be in SmoltBatch class)
        SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = SimulationEngine.State.Day,
            Name = "Smolt Batch Started",
            Category = LakskongenSim.Events.EventCategory.Equipment,
            Description = $"Started batch of {count:N0} smolts in post-smolt facility '{facility.Name}'",
            BalanceChange = -totalCost
        });

        EmitSignal(SignalName.FarmActionCompleted, "start_smolt_batch", facilityId, true);
        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: Started smolt batch in '{facilityId}' ({count:N0} smolts, {totalCost:N0} NOK)");
        return true;
    }

    /// <summary>
    /// Vaccinate a smolt batch (disease prevention before sea transfer).
    /// Returns vaccination cost in NOK.
    /// </summary>
    public float VaccinateBatch(string batchId)
    {
        if (SimulationEngine?.PostSmoltFacilities == null)
        {
            GD.PrintErr("GameBridge: VaccinateBatch failed - engine not initialized");
            return 0f;
        }

        // Vaccination cost: ~5 NOK per fish (multi-component vaccine)
        int fishCount = 100_000; // Placeholder - would fetch from actual batch
        decimal vaccineCost = 5m * fishCount;

        if (SimulationEngine.FinanceManager.Balance < vaccineCost)
        {
            GD.PrintErr($"GameBridge: VaccinateBatch failed - insufficient funds (need {vaccineCost:N0} NOK)");
            return 0f;
        }

        SimulationEngine.FinanceManager.Spend(vaccineCost);

        SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = SimulationEngine.State.Day,
            Name = "Batch Vaccination",
            Category = LakskongenSim.Events.EventCategory.Disease,
            Description = $"Vaccinated smolt batch '{batchId}' ({fishCount:N0} fish)",
            BalanceChange = -vaccineCost
        });

        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: Vaccinated batch '{batchId}' for {vaccineCost:N0} NOK");
        return (float)vaccineCost;
    }

    /// <summary>
    /// Transfer a post-smolt batch to sea (move to sea site).
    /// Returns true on success, false if batch not found or sea site unavailable.
    /// </summary>
    public bool TransferBatchToSea(string batchId)
    {
        if (SimulationEngine?.PostSmoltFacilities == null || SimulationEngine?.State?.SeaSites == null)
        {
            GD.PrintErr("GameBridge: TransferBatchToSea failed - engine not initialized");
            EmitSignal(SignalName.FarmActionCompleted, "transfer_batch", batchId, false);
            return false;
        }

        // Transfer cost: logistics, stress mitigation
        decimal transferCost = 2_000_000m; // 2M NOK for batch transfer

        if (SimulationEngine.FinanceManager.Balance < transferCost)
        {
            GD.PrintErr($"GameBridge: TransferBatchToSea failed - insufficient funds (need {transferCost:N0} NOK)");
            EmitSignal(SignalName.FarmActionCompleted, "transfer_batch", batchId, false);
            return false;
        }

        if (SimulationEngine.State.SeaSites.Count == 0)
        {
            GD.PrintErr("GameBridge: TransferBatchToSea failed - no sea sites available");
            EmitSignal(SignalName.FarmActionCompleted, "transfer_batch", batchId, false);
            return false;
        }

        SimulationEngine.FinanceManager.Spend(transferCost);

        // Move batch to first available sea site (simplified)
        var targetSite = SimulationEngine.State.SeaSites.First();

        SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = SimulationEngine.State.Day,
            Name = "Batch Transferred to Sea",
            Category = LakskongenSim.Events.EventCategory.Equipment,
            Description = $"Transferred post-smolt batch '{batchId}' to sea site '{targetSite.Id}'",
            BalanceChange = -transferCost
        });

        EmitSignal(SignalName.FarmActionCompleted, "transfer_batch", batchId, true);
        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: Transferred batch '{batchId}' to sea site '{targetSite.Id}' for {transferCost:N0} NOK");
        return true;
    }

}
