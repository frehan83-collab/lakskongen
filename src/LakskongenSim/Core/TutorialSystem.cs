using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LakskongenSim.Core
{
    /// <summary>Data for one tutorial step.</summary>
    public class TutorialStep
    {
        public string Id { get; init; } = "";
        public int StepNumber { get; init; }
        public string Title { get; init; } = "";
        public string Description { get; init; } = "";
        public string Hint { get; init; } = "";
        
        [JsonPropertyName("triggerCondition")]
        public string TriggerConditionString { get; init; } = "";
        
        [JsonPropertyName("completionCondition")]
        public string CompletionConditionString { get; init; } = "";
        
        [JsonPropertyName("highlightTarget")]
        public string HighlightTarget { get; init; } = "";
        
        public decimal RewardNok { get; init; }
        
        [JsonIgnore]
        public Func<TutorialSystem, bool>? TriggerCondition { get; set; }
        
        [JsonIgnore]
        public Func<TutorialSystem, bool>? CompletionCondition { get; set; }

        /// <summary>Day this step was completed, or null if not yet completed.</summary>
        [JsonIgnore]
        public int? CompletedOnDay { get; internal set; }
    }

    /// <summary>
    /// Step-based tutorial engine for Lakskongen — 12-step onboarding.
    /// Tracks player progress, checks completion conditions each day,
    /// and fires OnStepCompleted with optional NOK rewards.
    ///
    /// Registered in SimulationEngine and updated via CheckProgress() each day.
    /// NOTE: Uses GameState.Balance for step conditions (to be migrated to FinanceManager)
    /// </summary>
    #pragma warning disable CS0618 // Type or member is obsolete
    public class TutorialSystem
    {
        private readonly SimulationEngine _engine;
        private readonly List<TutorialStep> _steps;
        private int _currentStepIndex = 0;

        /// <summary>Fired when the player completes a tutorial step.</summary>
        public event Action<TutorialStep>? OnStepCompleted;

        /// <summary>The active tutorial step, or null when the tutorial is finished.</summary>
        public TutorialStep? CurrentStep =>
            _currentStepIndex < _steps.Count ? _steps[_currentStepIndex] : null;

        /// <summary>True after all 12 steps are completed.</summary>
        public bool IsComplete => _currentStepIndex >= _steps.Count;

        /// <summary>1-based number of the current step (returns 12 after completion).</summary>
        public int CurrentStepNumber =>
            _currentStepIndex < _steps.Count ? _steps[_currentStepIndex].StepNumber : 12;

        public int TotalSteps => _steps.Count;

        /// <summary>How many steps have been completed so far.</summary>
        public int CompletedCount => _steps.Count(s => s.CompletedOnDay.HasValue);

        public TutorialSystem(SimulationEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _steps = LoadStepsFromJson();
            RestoreTutorialState();
        }
        
        /// <summary>Restore tutorial progress from GameState (used when loading saved games).</summary>
        private void RestoreTutorialState()
        {
            _currentStepIndex = _engine.State.TutorialCurrentStepIndex;
            
            // Mark steps as completed based on saved IDs
            foreach (var step in _steps)
            {
                if (_engine.State.CompletedTutorialStepIds.Contains(step.Id))
                {
                    step.CompletedOnDay = _engine.State.Day; // Set to current day (actual day unknown)
                }
            }
            
            if (_currentStepIndex > 0)
            {
                Console.WriteLine($"📖 Tutorial restored: step {_currentStepIndex}/{TotalSteps}, " +
                                  $"{CompletedCount} steps completed");
            }
        }
        
        /// <summary>Load tutorial steps from JSON file and parse condition strings.</summary>
        private List<TutorialStep> LoadStepsFromJson()
        {
            string jsonPath = Path.Combine("game", "data", "tutorial_steps.json");
            
            // Fallback to hardcoded steps if JSON doesn't exist
            if (!File.Exists(jsonPath))
            {
                Console.WriteLine("⚠️ tutorial_steps.json not found, using legacy hardcoded steps");
                return BuildSteps();
            }
            
            try
            {
                string jsonContent = File.ReadAllText(jsonPath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var steps = JsonSerializer.Deserialize<List<TutorialStep>>(jsonContent, options);
                
                if (steps == null || steps.Count == 0)
                {
                    Console.WriteLine("⚠️ Failed to parse tutorial_steps.json, using legacy steps");
                    return BuildSteps();
                }
                
                // Parse condition strings into Func<TutorialSystem, bool>
                foreach (var step in steps)
                {
                    step.TriggerCondition = ParseCondition(step.TriggerConditionString);
                    step.CompletionCondition = ParseCondition(step.CompletionConditionString);
                }
                
                Console.WriteLine($"✅ Loaded {steps.Count} tutorial steps from JSON");
                return steps;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Error loading tutorial_steps.json: {ex.Message}");
                return BuildSteps();
            }
        }
        
        /// <summary>Parse condition string (e.g., "day >= 7") into executable function.</summary>
        private Func<TutorialSystem, bool> ParseCondition(string conditionStr)
        {
            if (string.IsNullOrWhiteSpace(conditionStr))
                return _ => true; // Always true for empty conditions
            
            string condition = conditionStr.Trim().ToLowerInvariant();
            
            // Simple condition parser — extend as needed
            return condition switch
            {
                // Basic progression
                "game_started" => t => t.State.Day >= 0,
                "day >= 1" => t => t.State.Day >= 1,
                "day >= 2" => t => t.State.Day >= 2,
                "day >= 7" => t => t.State.Day >= 7,
                "day >= 10" => t => t.State.Day >= 10,
                "day >= 15" => t => t.State.Day >= 15,
                "day >= 30" => t => t.State.Day >= 30,
                "day >= 60" => t => t.State.Day >= 60,
                "day >= 90" => t => t.State.Day >= 90,
                "day >= 120" => t => t.State.Day >= 120,
                
                // Fish and production
                "smolts_purchased >= 50000" => t => t.TotalFishCount >= 50000,
                "smolts_purchased >= 75000" => t => t.TotalFishCount >= 75000,
                "smolts_purchased >= 100000" => t => t.TotalFishCount >= 100000,
                "average_weight >= 4.0" => t => t.AverageFishWeightKg >= 4.0,
                
                // Health and treatment
                "lice_detection_acknowledged" => t => t.SeaLicePerFish >= 0.8, // Trigger lice event at 0.8
                "delousing_completed" => t => t.SeaLicePerFish < 0.5, // Completed when under threshold
                "biosecurity_enabled" => t => t.State.Day >= 35, // Placeholder: needs BiosecurityManager
                "cleaner_fish_deployed" => t => t.State.Day >= 40, // Placeholder: needs CleanerFish system
                
                // Harvest and sales
                "first_harvest_initiated" => t => t.State.ProcessingSnapshots.Count >= 1,
                "first_sale_completed" => t => t.State.ProcessingSnapshots.Count >= 1 && t.State.Balance > 10_000_000m,
                "pl_report_viewed" => t => t.State.ProcessingSnapshots.Count >= 1 && t.State.Day >= 1,
                
                // Expansion and systems
                "feed_strategy_adjusted" => t => t.State.Day >= 1, // Placeholder: needs FeedStrategy tracking
                "hatchery_built" => t => t.State.Day >= 50, // Placeholder: needs Hatchery.IsBuilt check
                "research_project_started" => t => false, // TODO: Implement Research system
                "genetics_viewed" => t => t.State.Day >= 61, // Placeholder: needs UI tracking
                "sustainability_checked" => t => t.State.Day >= 62, // Placeholder: needs UI tracking
                "second_site_built" => t => t.State.SeaSites.Count >= 2,
                "contract_signed" => t => t.State.Day >= 1, // Placeholder: needs ContractsManager
                "processing_plant_built" => t => t.State.Day >= 1, // Placeholder: needs ProcessingPlant system
                "export_market_opened" => t => t.State.Day >= 1, // Placeholder: needs ExportManager
                "tutorial_complete" => t => true, // Final step auto-completes
                
                // Legacy conditions
                "cinematic_watched" => t => t.State.Day >= 1,
                "license_inspected" => t => t.State.SeaSites.Count >= 0,
                "pens_inspected" => t => t.State.Zones.Count >= 1,
                "pen_1_stocked" => t => t.State.Populations.Any(),
                "health_panel_opened" => t => t.State.Day >= 1,
                "feed_panel_opened" => t => t.State.Day >= 1,
                "temperature_checked" => t => t.State.Day >= 1,
                "zone_inspected" => t => t.State.Zones.Count >= 1,
                "smolts_stocked" => t => t.State.Populations.Any(),
                "fish_fed_7_days" => t => t.State.Day >= 7,
                "health_checked" => t => t.State.Day >= 7,
                "delousing_performed" => t => t.SeaLicePerFish < 0.5,
                "day_90_reached" => t => t.State.Day >= 90,
                "disease_event_handled" => t => t.State.Day >= 30,
                "first_harvest" => t => t.State.ProcessingSnapshots.Count >= 1,
                "first_sale" => t => t.State.ProcessingSnapshots.Count >= 1,
                "production_chain_viewed" => t => t.State.Day >= 1,
                "weather_storm_survived" => t => t.State.Day >= 30,
                "crew_hired" => t => false, // TODO: Implement Crew system
                "revenue_10_million" => t => t.State.Balance >= 10_000_000m,
                
                // Default: parse numeric comparisons
                _ when condition.Contains("day >=") => ParseDayCondition(condition),
                _ when condition.Contains(">=") || condition.Contains("<=") || condition.Contains(">") || condition.Contains("<") 
                    => ParseNumericCondition(condition),
                    
                // Unknown condition — log and return false
                _ => t => { Console.WriteLine($"⚠️ Unknown tutorial condition: {conditionStr}"); return false; }
            };
        }
        
        /// <summary>Parse "day >= N" conditions.</summary>
        private Func<TutorialSystem, bool> ParseDayCondition(string condition)
        {
            try
            {
                var parts = condition.Replace("day", "").Trim().Split(new[] { ">=", "<=", ">", "<", "==" }, StringSplitOptions.None);
                if (parts.Length != 2) return _ => false;
                
                int targetDay = int.Parse(parts[1].Trim());
                
                if (condition.Contains(">=")) return t => t.State.Day >= targetDay;
                if (condition.Contains("<=")) return t => t.State.Day <= targetDay;
                if (condition.Contains(">")) return t => t.State.Day > targetDay;
                if (condition.Contains("<")) return t => t.State.Day < targetDay;
                if (condition.Contains("==")) return t => t.State.Day == targetDay;
            }
            catch { }
            
            return _ => false;
        }
        
        /// <summary>Parse generic numeric conditions (extensible).</summary>
        private Func<TutorialSystem, bool> ParseNumericCondition(string condition)
        {
            // Placeholder for future complex conditions
            return _ => false;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Called each simulated day by SimulationEngine.
        /// Evaluates the current step's trigger and completion conditions and advances when met.
        /// Chains through multiple steps if conditions are immediately satisfied.
        /// </summary>
        public void CheckProgress()
        {
            bool advanced;
            do
            {
                advanced = false;
                if (IsComplete) break;

                var step = CurrentStep!;
                
                // Check trigger condition first (if defined)
                bool triggered = step.TriggerCondition == null || step.TriggerCondition(this);
                if (!triggered) break; // Wait for trigger
                
                // Check completion condition
                if (step.CompletionCondition == null || step.CompletionCondition(this))
                {
                    CompleteStep(step);
                    advanced = true;
                }
            }
            while (advanced);
        }

        // ── Internal helpers used by conditions ───────────────────────────────

        internal GameState State => _engine.State;

        internal int TotalFishCount =>
            _engine.State.Populations.SelectMany(p => p.Cohorts).Sum(c => c.Count);

        internal double AverageFishWeightKg
        {
            get
            {
                var cohorts = _engine.State.Populations.SelectMany(p => p.Cohorts).ToList();
                int total = cohorts.Sum(c => c.Count);
                if (total == 0) return 0.0;
                return cohorts.Sum(c => c.Count * c.MeanWeightKg) / total;
            }
        }

        internal double SeaLicePerFish => _engine.SeaLice?.AverageLicePerFish ?? 0.0;

        /// <summary>Returns the day step N was completed, or null if not yet completed.</summary>
        internal int? GetStepCompletedDay(int stepNumber)
        {
            var step = _steps.FirstOrDefault(s => s.StepNumber == stepNumber);
            return step?.CompletedOnDay;
        }

        // ── Private ───────────────────────────────────────────────────────────

        private void CompleteStep(TutorialStep step)
        {
            step.CompletedOnDay = _engine.State.Day;
            _currentStepIndex++;

            // Persist completion to GameState
            _engine.State.TutorialCurrentStepIndex = _currentStepIndex;
            _engine.State.CompletedTutorialStepIds.Add(step.Id);

            if (step.RewardNok > 0)
            {
                _engine.State.Balance += step.RewardNok;
                Console.WriteLine(
                    $"🎓 Tutorial Step {step.StepNumber}/{TotalSteps} complete: {step.Title} " +
                    $"(+{step.RewardNok:N0} NOK)");
            }
            else
            {
                Console.WriteLine(
                    $"🎓 Tutorial Step {step.StepNumber}/{TotalSteps} complete: {step.Title}");
            }

            OnStepCompleted?.Invoke(step);
        }

        // ══════════════════════════════════════════════════════════════════════
        // TUTORIAL STEP DEFINITIONS — 12 steps of Norwegian salmon onboarding
        // ══════════════════════════════════════════════════════════════════════

        private List<TutorialStep> BuildSteps() => new()
        {
            // ── Step 1: Welcome ───────────────────────────────────────────────
            new TutorialStep
            {
                Id              = "welcome",
                StepNumber      = 1,
                Title           = "Velkommen til Lakskongen!",
                Description     = "Welcome to Lakskongen — Norway's premier salmon farming simulator. " +
                                  "You are about to build a world-class aquaculture operation on the " +
                                  "Norwegian coast. Watch the dashboard to track your balance, " +
                                  "fish health, and sustainability score.",
                Hint            = "Look around the dashboard to get familiar with the key indicators: " +
                                  "Balance (NOK), Fish Count, Sea Lice/fish, and Sustainability.",
                RewardNok       = 0m,
                // Completes on day 1 — the player simply sees the welcome screen
                CompletionCondition = t => t.State.Day >= 1
            },

            // ── Step 2: Buy Aquaculture License ──────────────────────────────
            new TutorialStep
            {
                Id              = "buy-license",
                StepNumber      = 2,
                Title           = "Buy Your First Aquaculture License",
                Description     = "Before farming salmon, you need a government-issued aquaculture " +
                                  "license (akvakulturtillatelse). Each license grants the right to " +
                                  "operate one sea site up to a maximum allowed biomass (MTB). " +
                                  "Licenses are your most valuable asset — Norwegian licenses trade " +
                                  "for hundreds of millions of NOK.",
                Hint            = "Navigate to Expansion → Purchase License to acquire your first license. " +
                                  "Starting capital covers the cost.",
                RewardNok       = 50_000m,
                // Complete once a sea site exists, or after day 2 (guided onboarding)
                CompletionCondition = t => t.State.SeaSites.Count >= 1 || t.State.Day >= 2
            },

            // ── Step 3: Set Up Your First Sea Site ───────────────────────────
            new TutorialStep
            {
                Id              = "setup-sea-site",
                StepNumber      = 3,
                Title           = "Set Up Your First Sea Site",
                Description     = "Your sea site (lokalitet) contains the net pens (merder) where " +
                                  "salmon grow at sea. Choose a fjord with good water flow, " +
                                  "optimal temperature (8–14°C), and low disease pressure. " +
                                  "Add a zone to activate water chemistry monitoring.",
                Hint            = "Go to Farm → New Sea Site and select a fjord location. " +
                                  "Add at least one zone to enable production.",
                RewardNok       = 100_000m,
                CompletionCondition = t => t.State.Zones.Count >= 1 || t.State.SeaSites.Count >= 1
            },

            // ── Step 4: Stock Smolts from Hatchery ───────────────────────────
            new TutorialStep
            {
                Id              = "stock-smolts",
                StepNumber      = 4,
                Title           = "Stock Smolts from Your Hatchery",
                Description     = "Smolts are young salmon (80–100 g) that have undergone " +
                                  "smoltification — the physiological transformation allowing " +
                                  "them to survive in seawater. Transfer at least 10,000 smolts " +
                                  "from your hatchery to begin your first sea-phase production cycle.",
                Hint            = "Open Hatchery → Transfer Smolts and select your sea site pen. " +
                                  "Stocking density should be < 25 kg/m³ at harvest weight.",
                RewardNok       = 50_000m,
                CompletionCondition = t => t.TotalFishCount >= 10_000
            },

            // ── Step 5: Manage Feed (FCR) ─────────────────────────────────────
            new TutorialStep
            {
                Id              = "manage-feed",
                StepNumber      = 5,
                Title           = "Manage Feed and Understand FCR",
                Description     = "Feed Conversion Ratio (FCR) measures how efficiently fish convert " +
                                  "feed into body weight. A good FCR is 1.1–1.2 (1.1 kg feed → 1 kg fish). " +
                                  "Feed accounts for ~50% of production costs. Temperature drives appetite: " +
                                  "optimal feeding is at 12–14°C. Watch your fish feed for one week.",
                Hint            = "Check the FCR indicator on the Production panel. " +
                                  "Reduce feeding if temperature rises above 16°C to avoid waste.",
                RewardNok       = 25_000m,
                CompletionCondition = t =>
                {
                    int? day4 = t.GetStepCompletedDay(4);
                    return day4.HasValue && t.State.Day >= day4.Value + 7;
                }
            },

            // ── Step 6: Monitor Fish Health ───────────────────────────────────
            new TutorialStep
            {
                Id              = "monitor-health",
                StepNumber      = 6,
                Title           = "Monitor Fish Health",
                Description     = "Sea lice (Lepeophtheirus salmonis) are the primary parasite threat " +
                                  "in Norwegian salmon farming. The regulatory limit is 0.5 adult " +
                                  "female lice per fish. Above 2.0 you risk fines; above 5.0 is critical. " +
                                  "Also watch for IPN, ISA, and Amoebic Gill Disease (AGD). " +
                                  "Monitor the health panel for one week.",
                Hint            = "Check the Health panel daily. Red indicators mean immediate action needed. " +
                                  "Sea lice levels typically rise slowly — catch them early.",
                RewardNok       = 25_000m,
                CompletionCondition = t =>
                {
                    int? day5 = t.GetStepCompletedDay(5);
                    return day5.HasValue && t.State.Day >= day5.Value + 7;
                }
            },

            // ── Step 7: Apply Delousing Treatment ────────────────────────────
            new TutorialStep
            {
                Id              = "delousing-treatment",
                StepNumber      = 7,
                Title           = "Apply Delousing Treatment",
                Description     = "When sea lice exceed the threshold, you must act. Treatment options: " +
                                  "bath treatments (hydrogen peroxide, azamethiphos), " +
                                  "biological control (leppefisk / cleaner fish), or " +
                                  "mechanical removal (Hydrolicer, Thermolicer). " +
                                  "Each has different costs, efficacy rates, and fish stress levels. " +
                                  "Get lice below 0.5 per fish.",
                Hint            = "Go to Health → Treatments. Bath treatments are fast but stressful. " +
                                  "Cleaner fish provide continuous biological control.",
                RewardNok       = 50_000m,
                CompletionCondition = t =>
                {
                    int? day6 = t.GetStepCompletedDay(6);
                    if (!day6.HasValue) return false;
                    // Pass when lice are controlled OR 7 days of management have elapsed
                    return t.SeaLicePerFish <= 0.5 || t.State.Day >= day6.Value + 7;
                }
            },

            // ── Step 8: Wait for Harvest Weight ──────────────────────────────
            new TutorialStep
            {
                Id              = "harvest-weight",
                StepNumber      = 8,
                Title           = "Wait for Harvest Weight",
                Description     = "Atlantic salmon require 14–22 months at sea to reach harvest weight. " +
                                  "Target: 4–6 kg gutted weight (5–7 kg live weight). " +
                                  "Growth rate depends on temperature, feed quality, genetics, and health. " +
                                  "A healthy fish in 12°C water gains ~10 g/day. " +
                                  "Wait until average fish weight reaches 4.0 kg.",
                Hint            = "The Production panel shows average fish weight. " +
                                  "Keep health optimal and FCR low for fastest growth.",
                RewardNok       = 100_000m,
                CompletionCondition = t => t.AverageFishWeightKg >= 4.0 && t.TotalFishCount > 0
            },

            // ── Step 9: Harvest and Sell to Market ───────────────────────────
            new TutorialStep
            {
                Id              = "harvest-and-sell",
                StepNumber      = 9,
                Title           = "Harvest and Sell to Market",
                Description     = "When fish reach target weight, initiate harvest. Salmon are " +
                                  "transferred to a well boat, slaughtered at the processing plant " +
                                  "(slakteri), graded by size and quality, and sold fresh. " +
                                  "Norwegian salmon prices range NOK 60–90/kg HOG (head-on gutted) " +
                                  "depending on market conditions.",
                Hint            = "Go to Harvest → Initiate Harvest for your ready population. " +
                                  "Sell on the spot market or negotiate a contract price.",
                RewardNok       = 0m,
                CompletionCondition = t => t.State.ProcessingSnapshots.Count >= 1
            },

            // ── Step 10: Check Your P&L ──────────────────────────────────────
            new TutorialStep
            {
                Id              = "check-pl",
                StepNumber      = 10,
                Title           = "Check Your Profit & Loss",
                Description     = "After your first harvest, review financial performance. " +
                                  "Key metrics: Revenue (price × HOG volume), " +
                                  "Feed Cost (~50% of OpEx), " +
                                  "EBIT/kg (industry benchmark: NOK 20–35/kg), " +
                                  "and Return on Invested Capital (ROIC). " +
                                  "Norwegian leaders like Mowi and SalMar achieve EBIT > 30 NOK/kg.",
                Hint            = "Open Finance → P&L Summary to see your full cost-per-kg breakdown. " +
                                  "Compare your FCR and mortality % against industry benchmarks.",
                RewardNok       = 25_000m,
                CompletionCondition = t =>
                {
                    int? day9 = t.GetStepCompletedDay(9);
                    return day9.HasValue && t.State.Day >= day9.Value + 1;
                }
            },

            // ── Step 11: Explore Genetics & Breeding ─────────────────────────
            new TutorialStep
            {
                Id              = "explore-genetics",
                StepNumber      = 11,
                Title           = "Explore Genetics & Breeding",
                Description     = "Norwegian salmon breeding programs (AquaGen, SalmoBreed) have " +
                                  "doubled growth rates over 40 years of selective breeding. " +
                                  "In Lakskongen, you can select broodstock for traits: " +
                                  "growth rate, disease resistance, FCR efficiency, and flesh quality. " +
                                  "Better genetics = higher production at lower cost. " +
                                  "Spend a week exploring the genetics panel.",
                Hint            = "Open Genetics → Breeding Programme. Select high-growth, " +
                                  "disease-resistant parents for your next smolt cohort.",
                RewardNok       = 75_000m,
                CompletionCondition = t =>
                {
                    int? day10 = t.GetStepCompletedDay(10);
                    return day10.HasValue && t.State.Day >= day10.Value + 7;
                }
            },

            // ── Step 12: Tutorial Complete ────────────────────────────────────
            new TutorialStep
            {
                Id              = "tutorial-complete",
                StepNumber      = 12,
                Title           = "Tutorial Complete — Free Play!",
                Description     = "Congratulations, oppdrettskonge! You have completed the " +
                                  "Lakskongen tutorial. You now understand the full salmon cycle:\n" +
                                  "License → Setup → Stock → Feed → Monitor → Treat → Harvest → Sell.\n\n" +
                                  "The full game awaits: expand your license portfolio, " +
                                  "earn ASC and organic certifications, navigate regulatory politics, " +
                                  "build integrated processing plants, and become Norway's leading " +
                                  "salmon producer. Lykke til — good luck!",
                Hint            = "Your goal: reach 10,000 tonnes annual production with " +
                                  "Sustainability Leader certification. Explore all panels freely.",
                RewardNok       = 500_000m,
                // Auto-completes when reached — reward is the graduation bonus
                CompletionCondition = _ => true
            },
        };
    }
    #pragma warning restore CS0618
}
