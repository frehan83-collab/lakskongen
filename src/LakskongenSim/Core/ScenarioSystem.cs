using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Data;
using LakskongenSim.Farm;

namespace LakskongenSim.Core
{
    /// <summary>
    /// A single starting sea site for a scenario: which zone it sits in, how many
    /// pens it has, how many smolts are stocked per pen, and the smolt strain
    /// (quality) used. Purely declarative — <see cref="ScenarioDefinition.ApplyStartingState"/>
    /// turns these into live <see cref="Farm.SeaSite"/> / <see cref="FishPopulation"/> objects.
    /// </summary>
    public class StartingSiteDefinition
    {
        /// <summary>Optional explicit site id. When blank a deterministic id is derived from the scenario.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public string Id { get; set; } = "";

        /// <summary>Optional display name (UI only; not required by the simulation).</summary>
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; } = "";

        /// <summary>Environment zone id the site belongs to (created on demand).</summary>
        [System.Text.Json.Serialization.JsonPropertyName("zone")]
        public string Zone { get; set; } = "";

        /// <summary>Number of pens at the site (1+).</summary>
        [System.Text.Json.Serialization.JsonPropertyName("pens")]
        public int Pens { get; set; } = 1;

        /// <summary>Smolts stocked per pen. 0 creates an empty site shell.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("smolt_count")]
        public int SmoltCount { get; set; }

        /// <summary>Smolt strain id (see <see cref="Farm.SmoltStrainRegistry"/>), e.g. "standard", "robust", "postsmolt".</summary>
        [System.Text.Json.Serialization.JsonPropertyName("smolt_quality")]
        public string SmoltQuality { get; set; } = "standard";
    }

    /// <summary>
    /// Defines a playable scenario with starting conditions, objectives, and special rules.
    /// </summary>
    public class ScenarioDefinition
    {
        /// <summary>Unique snake_case identifier (e.g. "forste_sesong").</summary>
        public string Id { get; set; } = "";

        /// <summary>Display name in Norwegian.</summary>
        public string Name { get; set; } = "";

        /// <summary>Flavour text describing the scenario premise.</summary>
        public string Description { get; set; } = "";

        /// <summary>Starting bank balance in NOK (may be negative for debt scenarios).</summary>
        [System.Text.Json.Serialization.JsonPropertyName("starting_balance")]
        public decimal StartingCapitalNOK { get; set; }

        /// <summary>Primary Norwegian production region for this scenario.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("starting_region")]
        public ProductionRegion StartingRegion { get; set; }

        /// <summary>
        /// Difficulty rating from 1 (easiest) to 5 (hardest).
        /// Config files use names ("Easy"/"Medium"/"Hard") or numbers —
        /// the lenient converter accepts both.
        /// </summary>
        [System.Text.Json.Serialization.JsonConverter(typeof(LenientDifficultyConverter))]
        public int Difficulty { get; set; }

        /// <summary>Primary victory objective type.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("win_condition")]
        public WinCondition WinCondition { get; set; }

        /// <summary>
        /// Scenario-specific rule overrides and numeric targets.
        /// Common keys:
        ///   "profit_target"         — decimal NOK threshold for Profit/Competition win
        ///   "sustainability_target" — int score threshold for Sustainability win
        ///   "ai_competitors"        — int count for Competition scenario
        ///   "single_site"           — "true" to restrict to one sea site
        ///   "harsh_storms"          — "true" for elevated storm frequency
        ///   "premium_prices"        — "true" for export price bonus
        ///   "start_disease"         — "true" to begin with an active disease outbreak
        ///   "start_debt"            — "true" signals negative starting capital
        ///   "regulation_pressure"   — "true" for stricter compliance thresholds
        ///   "calm_weather"          — "true" to suppress severe weather
        ///   "easy_market"           — "true" for reduced price volatility
        /// Values are always strings; parse as needed.
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyName("special_rules")]
        public Dictionary<string, string> SpecialRules { get; set; } = new();

        /// <summary>Optional day limit — null means unlimited time.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("time_limit_days")]
        public int? TimeLimitDays { get; set; }
        /// <summary>Whether this scenario is available to play (false = locked).</summary>
        [System.Text.Json.Serialization.JsonPropertyName("is_unlocked")]
        public bool IsUnlocked { get; set; }

        // ── Extended starting-state schema (additive; defaults keep old data safe) ──

        /// <summary>Starting sea sites (zone, pens, smolt count/quality).</summary>
        [System.Text.Json.Serialization.JsonPropertyName("starting_sites")]
        public List<StartingSiteDefinition> StartingSites { get; set; } = new();

        /// <summary>Research node ids the player begins with already unlocked.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("starting_research")]
        public string[] StartingResearch { get; set; } = System.Array.Empty<string>();

        /// <summary>Buildings/assets the player begins with, e.g. "hatchery", "feed_mill",
        /// "processing_plant", "ai_feeding". Unknown values are ignored.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("starting_buildings")]
        public string[] StartingBuildings { get; set; } = System.Array.Empty<string>();

        /// <summary>Long-form narrative / in-world lore shown on the scenario-select screen.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("story")]
        public string Story { get; set; } = "";

        /// <summary>Human-readable regulatory/gameplay restrictions (UI copy).</summary>
        [System.Text.Json.Serialization.JsonPropertyName("restrictions")]
        public string[] Restrictions { get; set; } = System.Array.Empty<string>();

        /// <summary>Classification tags for filtering/sorting the campaign roster.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("tags")]
        public string[] Tags { get; set; } = System.Array.Empty<string>();

        /// <summary>
        /// UI-facing starting cash. Mirrors <see cref="StartingCapitalNOK"/> for the
        /// scenario-select screen; 0 means "use <see cref="StartingCapitalNOK"/>".
        /// Cash is applied by the finance layer, not by <see cref="ApplyStartingState"/>.
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyName("starting_cash")]
        public decimal StartingCash { get; set; }

        /// <summary>
        /// Pure, engine-independent application of this scenario's starting state:
        /// creates zones, sea sites, stocked fish populations, research flags and
        /// ownership flags directly on <paramref name="state"/>. Does NOT touch the
        /// finance balance (caller applies <see cref="StartingCapitalNOK"/>).
        /// Safe to call more than once — zones, site ids and research ids are de-duplicated.
        /// </summary>
        public void ApplyStartingState(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            int siteIdx = 0;
            foreach (var siteDef in StartingSites ?? new List<StartingSiteDefinition>())
            {
                if (string.IsNullOrWhiteSpace(siteDef.Zone)) continue;

                string zoneId = siteDef.Zone;
                if (state.Zones.All(z => z.Id != zoneId))
                    state.Zones.Add(new LakskongenSim.Environment.Zone(zoneId));

                string siteId = string.IsNullOrWhiteSpace(siteDef.Id)
                    ? $"{Id}_{zoneId}_{siteIdx + 1}"
                    : siteDef.Id;
                siteIdx++;

                if (state.SeaSites.Any(s => s.Id == siteId)) continue; // idempotent

                int penCount = siteDef.Pens > 0 ? siteDef.Pens : 1;
                int smoltPerPen = siteDef.SmoltCount > 0 ? siteDef.SmoltCount : 0;
                var strain = SmoltStrainRegistry.Get(siteDef.SmoltQuality);

                var site = new SeaSite(siteId, zoneId) { NumberOfPens = penCount };
                for (int pen = 0; pen < penCount; pen++)
                {
                    var pop = new FishPopulation(new GrowthModel(), new MortalityModel())
                    {
                        ZoneId = zoneId,
                        SiteId = siteId,
                        SeaLiceLoad = strain.InitialLiceLoad
                    };
                    if (smoltPerPen > 0)
                    {
                        pop.AddCohort(new FishPopulation.Cohort(
                            ageDays: 300,
                            count: smoltPerPen,
                            meanWeightKg: strain.ReleaseWeightKg,
                            stage: LifeStage.Smolt,
                            genetics: strain.BuildGenome()));
                    }
                    site.Stock(pop);
                    state.Populations.Add(pop);
                }
                // Keep site and global biomass/zone accounting coherent from day 0.
                state.SeaSites.Add(site);
                var zone = state.Zones.First(z => z.Id == zoneId);
                zone.CurrentFishBiomassKg += site.Pens.Sum(p => p.GetTotalBiomassKg());
            }

            foreach (var research in StartingResearch ?? System.Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(research) && !state.UnlockedResearchIds.Contains(research))
                    state.UnlockedResearchIds.Add(research);
            }

            foreach (var building in StartingBuildings ?? System.Array.Empty<string>())
                ApplyStartingBuilding(state, building);
        }

        private static void ApplyStartingBuilding(GameState state, string building)
        {
            switch ((building ?? "").Trim().ToLowerInvariant())
            {
                case "hatchery": state.OwnsHatchery = true; break;
                case "feed_mill" or "feedmill": state.OwnsFeedMill = true; break;
                case "processing_plant" or "processing": state.OwnsProcessingPlant = true; break;
                case "ai_feeding": state.OwnsAIFeedingSystem = true; break;
                // Unknown building names are ignored (forward-compatible).
            }
        }
    }

    /// <summary>
    /// Campaign system: manages the scenario roster, tracks the active scenario,
    /// and checks win/loss conditions on every simulated day.
    ///
    /// Integration: call <see cref="CheckWinConditions"/> once per day inside
    /// SimulationEngine.Advance(), after all subsystem updates.
    /// Wire <see cref="OnScenarioWon"/> and <see cref="OnScenarioLost"/> in GameBridge
    /// to emit Godot signals to the UI layer.
    /// </summary>
    public class ScenarioSystem
    {
        private readonly GameState _state;
        private readonly Finance.FinanceManager? _financeManager;
        private readonly Dictionary<string, ScenarioDefinition> _scenarios = new();

        // ── Active scenario state ─────────────────────────────────────────────

        /// <summary>The currently active scenario, or null if none selected.</summary>
        public ScenarioDefinition? ActiveScenario { get; private set; }

        /// <summary>True while a scenario is running.</summary>
        public bool IsScenarioActive => ActiveScenario != null && !IsWon && !IsLost;

        /// <summary>True once the player has met the win condition.</summary>
        public bool IsWon { get; private set; }

        /// <summary>True once a loss condition has been triggered.</summary>
        public bool IsLost { get; private set; }

        /// <summary>Human-readable loss reason set when <see cref="IsLost"/> becomes true.</summary>
        public string LossReason { get; private set; } = "";

        // Tracks simulated AI competitor accumulated profit for Competition scenarios
        private readonly Dictionary<string, decimal> _aiProgress = new();
        private int _scenarioStartDay;

        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>Fired when the player meets the scenario's win condition.</summary>
        public event Action<ScenarioDefinition>? OnScenarioWon;

        /// <summary>Fired when the player fails the scenario. Second argument is the reason string.</summary>
        public event Action<ScenarioDefinition, string>? OnScenarioLost;

        // ── Constructor ───────────────────────────────────────────────────────

        public ScenarioSystem(GameState state, Finance.FinanceManager? financeManager = null)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _financeManager = financeManager;
            RegisterAllScenarios();
        }

        // ── Scenario management ───────────────────────────────────────────────

        /// <summary>
        /// Activate a scenario. Applies starting capital to <see cref="GameState.Balance"/>
        /// and seeds AI competitor state if this is a Competition scenario.
        /// </summary>
        /// <exception cref="ArgumentException">Unknown scenario id.</exception>
        /// <exception cref="InvalidOperationException">Scenario is locked.</exception>
        public void StartScenario(string scenarioId)
        {
            if (!_scenarios.TryGetValue(scenarioId, out var scenario))
                throw new ArgumentException($"Unknown scenario id: '{scenarioId}'", nameof(scenarioId));

            if (!scenario.IsUnlocked)
                throw new InvalidOperationException($"Scenario '{scenarioId}' is locked.");

            ActiveScenario = scenario;
            IsWon = false;
            IsLost = false;
            LossReason = "";
            _scenarioStartDay = _state.Day;
            
            // Set starting capital via FinanceManager if available
            if (_financeManager != null)
            {
                // Reset balance to scenario starting capital
                // Use reflection to set the private setter on Balance property
                var balanceProp = typeof(Finance.FinanceManager).GetProperty("Balance");
                if (balanceProp != null && balanceProp.CanWrite)
                {
                    balanceProp.SetValue(_financeManager, scenario.StartingCapitalNOK);
                }
            }
            else
            {
                #pragma warning disable CS0618
                _state.Balance = scenario.StartingCapitalNOK;
                #pragma warning restore CS0618
            }

            // Seed AI competitors for Competition scenarios
            _aiProgress.Clear();
            if (scenario.WinCondition == WinCondition.Competition &&
                scenario.SpecialRules.TryGetValue("ai_competitors", out var countStr) &&
                int.TryParse(countStr, out var count))
            {
                for (int i = 1; i <= count; i++)
                    _aiProgress[$"ai_{i}"] = 0m;
            }

            Console.WriteLine($"[ScenarioSystem] Started '{scenario.Name}' " +
                              $"(difficulty {scenario.Difficulty}/5, goal: {scenario.WinCondition})");
        }

        /// <summary>
        /// Evaluate win and loss conditions for the active scenario.
        /// Call once per simulated day after all subsystem updates complete.
        /// </summary>
        /// <param name="sustainabilityScore">Current 0–100 sustainability score.</param>
        /// <param name="tutorialComplete">True when TutorialSystem has finished all steps.</param>
        public void CheckWinConditions(int sustainabilityScore = 0, bool tutorialComplete = false)
        {
            if (ActiveScenario == null || IsWon || IsLost) return;

            int daysElapsed = _state.Day - _scenarioStartDay;

            // ── Universal loss: bankruptcy ────────────────────────────────────
            decimal currentBalance = _financeManager?.Balance ?? 
                #pragma warning disable CS0618
                _state.Balance;
                #pragma warning restore CS0618
            
            if (currentBalance < -2_000_000m)
            {
                TriggerLoss("Konkurs — gjelden oversteg 2 000 000 NOK.");
                return;
            }

            // ── Universal loss: time limit exceeded (except Survival — handled below) ──
            if (ActiveScenario.WinCondition != WinCondition.Survival &&
                ActiveScenario.TimeLimitDays.HasValue &&
                daysElapsed > ActiveScenario.TimeLimitDays.Value)
            {
                TriggerLoss($"Tid ute etter {ActiveScenario.TimeLimitDays.Value} dager.");
                return;
            }

            // ── Per-condition win checks ──────────────────────────────────────
            switch (ActiveScenario.WinCondition)
            {
                case WinCondition.Tutorial:
                    if (tutorialComplete)
                        TriggerWin();
                    break;

                case WinCondition.Profit:
                {
                    decimal target = GetRuleDecimal("profit_target", 5_000_000m);
                    if (currentBalance >= target)
                        TriggerWin();
                    break;
                }

                case WinCondition.Sustainability:
                {
                    int target = (int)GetRuleDecimal("sustainability_target", 95m);
                    if (sustainabilityScore >= target)
                        TriggerWin();
                    break;
                }

                case WinCondition.Survival:
                {
                    int survivalDays = ActiveScenario.TimeLimitDays ?? 730;
                    if (daysElapsed > survivalDays)
                    {
                        // Must still be solvent at the end
                        if (currentBalance > 0)
                            TriggerWin();
                        else
                            TriggerLoss($"Du overlevde ikke de {survivalDays} dagene med positiv balanse.");
                    }
                    break;
                }

                case WinCondition.Competition:
                {
                    decimal target = GetRuleDecimal("profit_target", 10_000_000m);

                    // Advance AI competitors with simple linear accumulation (varied rate per AI)
                    int aiIdx = 0;
                    foreach (var key in _aiProgress.Keys.ToList())
                    {
                        decimal dailyRate = 3_500m + (aiIdx * 1_200m); // AI_1: 3.5k, AI_2: 4.7k, AI_3: 5.9k NOK/day
                        _aiProgress[key] += dailyRate;
                        aiIdx++;
                    }

                    // If any AI reached target first, player loses
                    if (_aiProgress.Values.Any(v => v >= target))
                    {
                        TriggerLoss("En konkurrent nådde profittmålet før deg!");
                        return;
                    }

                    // Player wins by reaching target before any AI
                    if (currentBalance >= target)
                        TriggerWin();
                    break;
                }
            }
        }

        // ── Roster queries ────────────────────────────────────────────────────

        /// <summary>All registered scenarios, keyed by id.</summary>
        public IReadOnlyDictionary<string, ScenarioDefinition> All => _scenarios;

        /// <summary>Look up a scenario by id, returning null if not found.</summary>
        public ScenarioDefinition? GetScenario(string id) =>
            _scenarios.TryGetValue(id, out var s) ? s : null;

        /// <summary>All scenarios visible to the player (unlocked or locked).</summary>
        public IEnumerable<ScenarioDefinition> GetAll() => _scenarios.Values;

        /// <summary>Only unlocked scenarios.</summary>
        public IEnumerable<ScenarioDefinition> GetUnlocked() =>
            _scenarios.Values.Where(s => s.IsUnlocked);

        /// <summary>Unlock a scenario (call after completing a prerequisite).</summary>
        public void Unlock(string scenarioId)
        {
            if (_scenarios.TryGetValue(scenarioId, out var s))
                s.IsUnlocked = true;
        }

        /// <summary>
        /// AI competitor accumulated profit for Competition scenarios (read-only).
        /// Keys are "ai_1", "ai_2", etc.
        /// </summary>
        public IReadOnlyDictionary<string, decimal> AiProgress => _aiProgress;

        // ── Internal helpers ──────────────────────────────────────────────────

        private decimal GetRuleDecimal(string key, decimal defaultValue)
        {
            if (ActiveScenario?.SpecialRules.TryGetValue(key, out var raw) == true &&
                decimal.TryParse(raw, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var val))
                return val;
            return defaultValue;
        }

        private void TriggerWin()
        {
            IsWon = true;
            Console.WriteLine($"[ScenarioSystem] ★ Vunnet: '{ActiveScenario!.Name}'");
            OnScenarioWon?.Invoke(ActiveScenario!);
        }

        private void TriggerLoss(string reason)
        {
            IsLost = true;
            LossReason = reason;
            Console.WriteLine($"[ScenarioSystem] ✗ Tapt: {reason}");
            OnScenarioLost?.Invoke(ActiveScenario!, reason);
        }

        // ══════════════════════════════════════════════════════════════════════
        // SCENARIO DEFINITIONS — 5 Norwegian campaign scenarios
        // ══════════════════════════════════════════════════════════════════════

        private void RegisterAllScenarios()
        {
            // ─── 1. FAMILIEGÅRDEN — Tutorial (Difficulty 1) ───────────────────
            //    Inherit grandfather's small farm in Hardanger. One license, low funds, learn basics.
            Register(new ScenarioDefinition
            {
                Id = "forste_sesong",
                Name = "Familiegården",
                Description =
                    "Du har arvet bestefars lille oppdrett i Hardanger. Med én lisens og små midler må du lære grunnleggende oppdrett. " +
                    "Bygg opp gården fra bunnen av og vis at du kan drive lønnsomt.",
                StartingCapitalNOK = 300_000m,
                StartingRegion = ProductionRegion.Vestland,
                Difficulty = 1,
                WinCondition = WinCondition.Tutorial,
                SpecialRules = new Dictionary<string, string>
                {
                    ["single_site"]  = "true",
                    ["calm_weather"] = "true",
                    ["easy_market"]  = "true",
                },
                TimeLimitDays = null,
                IsUnlocked = true,
            });

            // ─── 2. NORDLANDS UTFORDRING — Storm & Premium (Difficulty 3) ─────
            //    Harsh Nordland coast. Storms, cold, but premium sashimi market access to Japan.
            Register(new ScenarioDefinition
            {
                Id = "lofoten_eventyr",
                Name = "Nordlands Utfordring",
                Description =
                    "De harde kystene i Nordland byr på de verste stormene og kaldeste havstrømmene, " +
                    "men laksen her når premiumpriser som sashimi til Japan. " +
                    "Takle uværet og nå et overskudd på 5 000 000 NOK.",
                StartingCapitalNOK = 2_000_000m,
                StartingRegion = ProductionRegion.Nordland,
                Difficulty = 3,
                WinCondition = WinCondition.Profit,
                SpecialRules = new Dictionary<string, string>
                {
                    ["harsh_storms"]   = "true",
                    ["premium_prices"] = "true",
                    ["profit_target"]  = "5000000",
                },
                TimeLimitDays = null,
                IsUnlocked = true,
            });

            // ─── 3. TRØNDER-KRIGEN — Competition (Difficulty 4) ───────────────
            //    Competitive Trøndelag. 3 AI competitors. Market share battle.
            Register(new ScenarioDefinition
            {
                Id = "laksebaronene",
                Name = "Trønder-krigen",
                Description =
                    "Tre AI-konkurrenter kjemper om å bli Trøndelags største lakseaktør. " +
                    "Markedskampen er brutal – hvem som helst kan ta markedsandeler, kjøpe lisenser og dumpe priser. " +
                    "Vær den første til å nå 10 000 000 NOK i fortjeneste.",
                StartingCapitalNOK = 5_000_000m,
                StartingRegion = ProductionRegion.Trøndelag,
                Difficulty = 4,
                WinCondition = WinCondition.Competition,
                SpecialRules = new Dictionary<string, string>
                {
                    ["ai_competitors"] = "3",
                    ["profit_target"]  = "10000000",
                },
                TimeLimitDays = null,
                IsUnlocked = false,
            });

            // ─── 4. FINNMARKS GJENFØDSEL — Survival (Difficulty 5) ───────────
            //    Bankrupt Finnmark site with active ILA outbreak. Turnaround challenge.
            Register(new ScenarioDefinition
            {
                Id = "krise_manager",
                Name = "Finnmarks Gjenfødsel",
                Description =
                    "Du tar over et konkursrammet anlegg i Finnmark med et aktivt ILA-utbrudd. " +
                    "500 000 NOK i gjeld og Mattilsynet har varslet sanksjoner. " +
                    "Overlev i 730 dager og snu selskapet til lønnsomhet.",
                StartingCapitalNOK = -500_000m,
                StartingRegion = ProductionRegion.Finnmark,
                Difficulty = 5,
                WinCondition = WinCondition.Survival,
                SpecialRules = new Dictionary<string, string>
                {
                    ["start_disease"]       = "true",
                    ["start_debt"]          = "true",
                    ["regulation_pressure"] = "true",
                },
                TimeLimitDays = 730,
                IsUnlocked = false,
            });

            // ─── 5. LAKSKONGEN — Freeplay (Difficulty 1) ──────────────────────
            //    Freeplay. Start anywhere, no restrictions, build your empire.
            Register(new ScenarioDefinition
            {
                Id = "barekraft_mester",
                Name = "Lakskongen",
                Description =
                    "Fri lek – start hvor som helst i Norge uten begrensninger. " +
                    "Bygg ditt lakseimperium fra bunnen av, uten tidspress eller konkurrenter. " +
                    "Målet er å skape den største og mest lønnsomme oppdrettsvirksomheten.",
                StartingCapitalNOK = 50_000_000m,
                StartingRegion = ProductionRegion.Vestland,
                Difficulty = 1,
                WinCondition = WinCondition.Profit,
                SpecialRules = new Dictionary<string, string>
                {
                    ["calm_weather"] = "true",
                    ["easy_market"]  = "true",
                    ["profit_target"] = "100000000",
                },
                TimeLimitDays = null,
                IsUnlocked = false,
            });

            // ══════════════════════════════════════════════════════════════════
            // CAMPAIGN SCENARIOS — grounded in real 2024–26 Norwegian aquaculture
            // events/policy (see per-scenario comments for sources).
            // ══════════════════════════════════════════════════════════════════

            // ─── 6. PO3 RØD SONE — Red-zone MAB cut (Difficulty 4) ───────────
            //    Production area PO3 placed under red traffic light → −6% MAB.
            //    Survival strategy = "environmental flexibility" via closed
            //    containment (ContainmentLevel >= SemiClosed) per
            //    regjeringen.no revidert forslag om miljøfleksibilitet (2024–25).
            Register(new ScenarioDefinition
            {
                Id = "po3_red_sone",
                Name = "PO3 Rød Sone",
                Story =
                    "Produksjonsområde PO3 har gått over til RØDT lys etter lakselustrykk og rømningsrisiko. " +
                    "Mattilsynet og Nærings- og fiskeridepartementet strammer inn: maksimal tillatt biomasse (MTB) " +
                    "kuttes med 6 %. Din eneste vei til 'miljøfleksibilitet' er lukket containement-teknologi " +
                    "(semi-lukket og høyere), som gir biomassekreditt selv i rød sone. " +
                    "Behold lønnsomheten og overlev reguleringsbølgen.",
                Description =
                    "PO3 rød sone: −6 % MTB, lukket containement er eneste vei til miljøfleksibilitet. Overlev i 3 år.",
                StartingCapitalNOK = 12_000_000m,
                StartingCash = 12_000_000m,
                StartingRegion = ProductionRegion.Trøndelag,
                Difficulty = 4,
                WinCondition = WinCondition.Survival,
                StartingSites = new List<StartingSiteDefinition>
                {
                    new() { Name = "PO3 Nord", Zone = "po3_nord", Pens = 6, SmoltCount = 12_000, SmoltQuality = "robust" },
                    new() { Name = "PO3 Sør", Zone = "po3_sor",  Pens = 4, SmoltCount = 10_000, SmoltQuality = "standard" },
                },
                StartingResearch = new[] { "sensor-networks", "deep-sea-farming" },
                StartingBuildings = new[] { "ai_feeding" },
                Restrictions = new[]
                {
                    "Rød sone: −6 % MTB på alle åpne anlegg",
                    "Bare lukket containement (semi-lukket eller høyere) kvalifiserer for miljøfleksibilitet",
                },
                Tags = new[] { "regulering", "rød_sone", "containement", "survival" },
                SpecialRules = new Dictionary<string, string>
                {
                    ["red_zone"] = "true",
                    ["mab_reduction"] = "0.06",
                    ["environmental_flexibility"] = "closed_containment",
                    ["regulation_pressure"] = "true",
                },
                TimeLimitDays = 1095, // 3 years
                IsUnlocked = true,
            });

            // ─── 7. ISA-UTBRUDDET — Mandatory cull zone (Difficulty 5) ────────
            //    SalMar reported ISA at a site in Q3 2026 → compulsory cull
            //    (bekjempelsessone), biosecurity firewalls and insurance cover.
            Register(new ScenarioDefinition
            {
                Id = "isa_utbrudd",
                Name = "ISA-utbruddet",
                Story =
                    "SalMar varslet om ILA/ISA ved et anlegg, og Mattilsynet har opprettet bekjempelsessone. " +
                    "Hele det infiserte anlegget må slaktes ut (tvungen nedslaktning). " +
                    "Du må bygge biosikkerhetsbranner (firewalls) mellom sonene, aktivere forsikringsdekning " +
                    "og hindre smitte til nabofelt. Hvert minutt teller — klarer du å slukke brannen?",
                Description =
                    "Aktiv ISA med tvungen nedslaktningssone. Bygg firewalls, krev inn forsikring, redd de friske anleggene.",
                StartingCapitalNOK = 18_000_000m,
                StartingCash = 18_000_000m,
                StartingRegion = ProductionRegion.Trøndelag,
                Difficulty = 5,
                WinCondition = WinCondition.Survival,
                StartingSites = new List<StartingSiteDefinition>
                {
                    new() { Name = "Infisert kjerneanlegg", Zone = "isa_kjerne", Pens = 4, SmoltCount = 8_000,  SmoltQuality = "standard" },
                    new() { Name = "Nabofelt nord",          Zone = "isa_nord",   Pens = 6, SmoltCount = 12_000, SmoltQuality = "robust" },
                    new() { Name = "Nabofelt sør",           Zone = "isa_sor",    Pens = 6, SmoltCount = 12_000, SmoltQuality = "robust" },
                },
                StartingResearch = new[] { "disease-resistance", "sensor-networks" },
                StartingBuildings = new[] { "hatchery" }, // biosecure smolt production
                Restrictions = new[]
                {
                    "Tvungen nedslaktning i bekjempelsessonen",
                    "Biosikkerhetsbranner (firewalls) mellom produksjonsområder",
                    "Forsikringsdekning kreves for utbruddsansvar",
                },
                Tags = new[] { "sykdom", "isa", "krise", "biosecurity" },
                SpecialRules = new Dictionary<string, string>
                {
                    ["start_disease"] = "true",
                    ["disease_type"] = "ISA",
                    ["mandatory_cull"] = "true",
                    ["firewalls"] = "true",
                    ["insurance"] = "true",
                    ["regulation_pressure"] = "true",
                },
                TimeLimitDays = 540, // ~18 months to contain and recover
                IsUnlocked = true,
            });

            // ─── 8. ALGEKATASTROFEN — Bloom mortality (Difficulty 5) ─────────
            //    Grieg Seafood lost ~1,000 tonnes of salmon in a single harmful
            //    algal bloom incident (2025) off Nordland. Survival hinges on
            //    early warning, mitigation (bubble curtain/O2 diffuser) and
            //    insurance against catastrophic mortality.
            Register(new ScenarioDefinition
            {
                Id = "algekatastrofe",
                Name = "Algekatastrofen",
                Story =
                    "Høsten rammet Grieg Seafood hardt: et massivt skadelig algeoppblomstring drepte rundt " +
                    "1 000 tonn laks i ett enkelt hendelsesforløp. Alger tapper oksygenet og skader gjellene. " +
                    "Tidlig varsling fra sensorer, umiddelbar mitigering (boblegardin og O2-tilførsel) og " +
                    "forsikring er forskjellen mellom overlevelse og total tap.",
                Description =
                    "1 000 t dødelighet ved én algehend. Tidlig varsling, mitigerende tiltak og forsikring avgjør.",
                StartingCapitalNOK = 25_000_000m,
                StartingCash = 25_000_000m,
                StartingRegion = ProductionRegion.Nordland,
                Difficulty = 5,
                WinCondition = WinCondition.Survival,
                StartingSites = new List<StartingSiteDefinition>
                {
                    new() { Name = "Algeutsatt sør", Zone = "alge_sor",  Pens = 8, SmoltCount = 14_000, SmoltQuality = "standard" },
                    new() { Name = "Algeutsatt midt", Zone = "alge_midt", Pens = 8, SmoltCount = 14_000, SmoltQuality = "standard" },
                    new() { Name = "Innlandsbeskyttet", Zone = "alge_indre", Pens = 5, SmoltCount = 9_000, SmoltQuality = "robust" },
                },
                StartingResearch = new[] { "sensor-networks", "predictive-analytics" },
                StartingBuildings = new[] { "ai_feeding" },
                Restrictions = new[]
                {
                    "Tidligvarslingssystem for alger påkrevd",
                    "Mitigerende tiltak (boblegardin/O2) må være operative",
                    "Katastrofeforsikring kreves",
                },
                Tags = new[] { "alger", "bloom", "katastrofe", "forsikring" },
                SpecialRules = new Dictionary<string, string>
                {
                    ["start_bloom"] = "true",
                    ["early_warning"] = "true",
                    ["mitigation"] = "true",
                    ["insurance"] = "true",
                },
                TimeLimitDays = 365, // survive one full bloom season
                IsUnlocked = true,
            });

            // ─── 9. RESSURSRENTE-OVERGANEN — 25 % cash-flow tax (Difficulty 3) ─
            //    Norwegian resource-rent ("grunnrenteskatt") transition: 25 %
            //    effective cash-flow tax with a 70 MNOK standard deduction
            //    (grunnfradrag). Reshapes profitability; scale or restructure.
            Register(new ScenarioDefinition
            {
                Id = "ressursrente",
                Name = "Ressursrente-overgangen",
                Story =
                    "Den nye grunnrentebeskatningen er vedtatt: 25 % kontantstrømskatt på havbruk, " +
                    "med et standardfradrag (grunnfradrag) på 70 millioner kroner. " +
                    "Små og mellomstore aktører kan vokse skattefritt under fradraget, men hver krone " +
                    "over 70 MNOK blir dramatisk mindre verdt. Omstrukturer, invester i verdikjeden og " +
                    "lever lønnsomt i det nye skattelandskapet.",
                Description =
                    "25 % grunnrenteskatt med 70 MNOK grunnfradrag. Restrukturer for lønnsomhet under det nye regimet.",
                StartingCapitalNOK = 60_000_000m,
                StartingCash = 60_000_000m,
                StartingRegion = ProductionRegion.Vestland,
                Difficulty = 3,
                WinCondition = WinCondition.Profit,
                StartingSites = new List<StartingSiteDefinition>
                {
                    new() { Name = "Hovedanlegg vest", Zone = "vest_1", Pens = 8,  SmoltCount = 15_000, SmoltQuality = "postsmolt" },
                    new() { Name = "Vekstanlegg nord", Zone = "vest_2", Pens = 8,  SmoltCount = 15_000, SmoltQuality = "postsmolt" },
                    new() { Name = "Utvidelse sør",    Zone = "vest_3", Pens = 6,  SmoltCount = 12_000, SmoltQuality = "robust" },
                },
                StartingResearch = new[] { "international-export", "value-added-processing" },
                StartingBuildings = new[] { "processing_plant", "feed_mill" },
                Restrictions = new[]
                {
                    "25 % grunnrenteskatt på kontantstrøm",
                    "70 MNOK standardfradrag (grunnfradrag) deles på tvers av virksomheten",
                },
                Tags = new[] { "skatt", "grunnrente", "finans", "profit" },
                SpecialRules = new Dictionary<string, string>
                {
                    ["resource_rent_tax"] = "0.25",
                    ["standard_deduction"] = "70000000",
                    ["profit_target"] = "25000000",
                },
                TimeLimitDays = null,
                IsUnlocked = true,
            });
        }

        public void RegisterScenarios(IEnumerable<ScenarioDefinition> scenarios)
        {
            if (scenarios == null) return;
            foreach (var scenario in scenarios)
            {
                _scenarios[scenario.Id] = scenario;
            }
        }

        private void Register(ScenarioDefinition scenario) => _scenarios[scenario.Id] = scenario;
    }

    /// <summary>
    /// Accepts difficulty as a 1–5 number or a name ("Easy"→1,
    /// "Medium"→3, "Hard"→5). Unknown values fall back to 3 (medium).
    /// </summary>
    public class LenientDifficultyConverter : System.Text.Json.Serialization.JsonConverter<int>
    {
        public override int Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
        {
            if (reader.TokenType == System.Text.Json.JsonTokenType.Number && reader.TryGetInt32(out var n))
                return Math.Clamp(n, 1, 5);
            if (reader.TokenType == System.Text.Json.JsonTokenType.String)
            {
                var s = (reader.GetString() ?? "").Trim().ToLowerInvariant();
                if (int.TryParse(s, out var parsed))
                    return Math.Clamp(parsed, 1, 5);
                return s switch
                {
                    "easy" => 1,
                    "medium" or "normal" => 3,
                    "hard" => 5,
                    _ => 3,
                };
            }
            return 3;
        }

        public override void Write(System.Text.Json.Utf8JsonWriter writer, int value, System.Text.Json.JsonSerializerOptions options)
            => writer.WriteNumberValue(value);
    }
}
