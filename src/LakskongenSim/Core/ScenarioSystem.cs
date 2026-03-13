using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Data;

namespace LakskongenSim.Core
{
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
        public decimal StartingCapitalNOK { get; set; }

        /// <summary>Primary Norwegian production region for this scenario.</summary>
        public ProductionRegion StartingRegion { get; set; }

        /// <summary>Difficulty rating from 1 (easiest) to 5 (hardest).</summary>
        public int Difficulty { get; set; }

        /// <summary>Primary victory objective type.</summary>
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
        public Dictionary<string, string> SpecialRules { get; set; } = new();

        /// <summary>Optional day limit — null means unlimited time.</summary>
        public int? TimeLimitDays { get; set; }

        /// <summary>Whether this scenario is available to play (false = locked).</summary>
        public bool IsUnlocked { get; set; }
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

        public ScenarioSystem(GameState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
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
            _state.Balance = scenario.StartingCapitalNOK;

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
            if (_state.Balance < -2_000_000m)
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
                    if (_state.Balance >= target)
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
                        if (_state.Balance > 0)
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
                    if (_state.Balance >= target)
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
            // ─── 1. FØRSTE SESONG — Tutorial (Difficulty 1) ───────────────────
            //    One site, calm weather, stable market, friendly intro.
            Register(new ScenarioDefinition
            {
                Id = "forste_sesong",
                Name = "Første Sesong",
                Description =
                    "Du har arvet en liten oppdrettslisens fra onkelen din i Hardanger. " +
                    "Lær grunnleggende drift med ett anlegg, rolig vær og stabile priser. " +
                    "Fullfør opplæringen og vis at du kan drive lønnsomt.",
                StartingCapitalNOK = 500_000m,
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

            // ─── 2. LOFOTENS EVENTYR — Storm & Premium (Difficulty 3) ─────────
            //    Nordland harsh winters, but Japanese sashimi premium prices.
            Register(new ScenarioDefinition
            {
                Id = "lofoten_eventyr",
                Name = "Lofotens Eventyr",
                Description =
                    "De dramatiske øyene i Nordland byr på de reneste havstrømmene og de råeste stormene. " +
                    "Laksen her oppnår premiumpriser i Asia og Europa — men uvær truer anleggene. " +
                    "Nå et overskudd på 5 000 000 NOK for å erobre markedet.",
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

            // ─── 3. LAKSEBARONENE — Competition (Difficulty 4) ────────────────
            //    Race against 3 AI competitors to 10M NOK profit.
            Register(new ScenarioDefinition
            {
                Id = "laksebaronene",
                Name = "Laksebaronene",
                Description =
                    "Tre aggressive konkurrenter kjemper om å bli Trøndelags største lakseaktør. " +
                    "Hvem som helst kan ta markedsandeler, kjøpe lisenser og dumpe priser. " +
                    "Vær den første til å nå 10 000 000 NOK i fortjeneste — eller bli utkonkurrert.",
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

            // ─── 4. BÆREKRAFTSMESTER — Sustainability (Difficulty 3) ──────────
            //    Win by achieving Sustainability Leader certification (score ≥ 95).
            Register(new ScenarioDefinition
            {
                Id = "barekraft_mester",
                Name = "Bærekraftsmester",
                Description =
                    "Forbrukere og politikere krever ansvarlig havbruk. " +
                    "Bygg et oppdrettsselskap med minst mulig miljøavtrykk og oppnå " +
                    "Sustainability Leader-sertifiseringen (bærekraftspoeng ≥ 95). " +
                    "Strengere reguleringer gjør veien krevende.",
                StartingCapitalNOK = 3_000_000m,
                StartingRegion = ProductionRegion.Vestland,
                Difficulty = 3,
                WinCondition = WinCondition.Sustainability,
                SpecialRules = new Dictionary<string, string>
                {
                    ["sustainability_target"] = "95",
                    ["regulation_pressure"]   = "true",
                },
                TimeLimitDays = null,
                IsUnlocked = false,
            });

            // ─── 5. KRISE MANAGER — Survival (Difficulty 5) ──────────────────
            //    Start with disease + debt + regulation pressure. Survive 2 years.
            Register(new ScenarioDefinition
            {
                Id = "krise_manager",
                Name = "Krise Manager",
                Description =
                    "Du tar over et konkursrammet anlegg i Finnmark: ILA-utbrudd pågår, " +
                    "500 000 NOK i gjeld og Mattilsynet har varslet sanksjoner. " +
                    "Overlev i 730 dager (2 år) med positiv bankkonto — da er du en ekte krisehåndterer.",
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
        }

        private void Register(ScenarioDefinition scenario) => _scenarios[scenario.Id] = scenario;
    }
}
