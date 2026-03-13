using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LakskongenSim.Biology;
using LakskongenSim.Environment;
using LakskongenSim.Events;
using LakskongenSim.Farm;
using LakskongenSim.Health;
using LakskongenSim.Sustainability;

namespace LakskongenSim.Core
{
    /// <summary>
    /// Central orchestrator connecting all subsystems in correct update order.
    /// Update order: Weather → Environment → Biology → Health → Events → Market → Finance → Tutorial → Analytics
    /// </summary>
    public class SimulationEngine
    {
        public TimeManager Time { get; } = new();
        public GameState State { get; } = new();

        // ─── Core subsystems ───
        public WeatherSystem Weather { get; private set; }
        public EnvironmentModel Environment { get; private set; }
        public GrowthModel Growth { get; private set; }
        public MortalityModel Mortality { get; private set; }
        public FeedSystem Feed { get; private set; }
        public SeaLiceModel SeaLice { get; private set; }
        public DiseaseSystem Disease { get; private set; }
        public IntegratedDiseaseSystem IntegratedDisease { get; private set; }
        public Hatchery Hatchery { get; private set; }
        public Market.MarketSimulation MarketSimulation { get; set; }
        public Finance.FinanceManager FinanceManager { get; set; }
        public SustainabilityManager SustainabilityManager { get; private set; }
        public Storyteller Storyteller { get; private set; }
        public TutorialSystem Tutorial { get; private set; }
        public AnalyticsTracker Analytics { get; private set; }
        public AudioManager Audio { get; private set; }
        public AchievementSystem Achievements { get; private set; }

        // ─── Update intervals ───
        private int _lastSustainabilityUpdate = 0;
        private const int SUSTAINABILITY_UPDATE_INTERVAL = 7; // Weekly

        // ─── Events ───
        public event Action<int>? OnDayAdvanced;
        public event Action<string>? OnEventTriggered;
        public event Action<EventRecord>? OnEventFired;
        public event Action<Achievement>? OnAchievementUnlocked;

        /// <summary>
        /// Initialize all subsystems and wire them together.
        /// </summary>
        public void Initialize()
        {
            // Create subsystems
            Weather = new WeatherSystem();
            Environment = new EnvironmentModel();
            Growth = new GrowthModel();
            Mortality = new MortalityModel();
            Feed = new FeedSystem();
            SeaLice = new SeaLiceModel();
            Disease = new DiseaseSystem(SeaLice);
            IntegratedDisease = new IntegratedDiseaseSystem(SeaLice);
            Hatchery = new Hatchery();

            // Default finance and market if not set externally
            FinanceManager ??= new Finance.FinanceManager(10_000_000m); // 10M NOK starting capital
            MarketSimulation ??= new Market.MarketSimulation();

            // Load sustainability configuration
            SustainabilityConfig sustainConfig;
            var configPath = Path.Combine("Data", "SustainabilityConfig.json");
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                sustainConfig = JsonSerializer.Deserialize<SustainabilityConfig>(json) ?? new SustainabilityConfig();
            }
            else
            {
                sustainConfig = new SustainabilityConfig();
            }
            SustainabilityManager = new SustainabilityManager(sustainConfig);

            // Wire events
            SustainabilityManager.OnScoreChanged += OnSustainabilityScoreChanged;
            SustainabilityManager.OnCertificationChanged += OnCertificationChanged;

            // Initialize Storyteller event director
            Storyteller = new Storyteller(Time, State);
            NorwegianEvents.RegisterAll(Storyteller);
            Storyteller.OnEventRaised += HandleStorytellerEvent;

            // Hatchery → population
            Hatchery.OnNewCohort += cohort =>
            {
                if (State.Populations.Count == 0)
                {
                    State.Populations.Add(new FishPopulation(Growth, Mortality));
                }
                State.Populations[0].AddCohort(cohort);
            };

            // Feed cost → finance
            Feed.OnFeedCostIncurred += cost =>
            {
                FinanceManager.Spend(cost);
            };

            // Tutorial onboarding system
            Tutorial = new TutorialSystem(this);

            // Analytics tracker — shares GameState's DailySnapshots list
            Analytics = new AnalyticsTracker(State.DailySnapshots);

            // Audio state tracker — pure C#, no Godot; consumed via GameBridge → SimBridge
            Audio = new AudioManager(Weather);

            // Achievement system — initialized after State so conditions have full context
            Achievements = new AchievementSystem(State);
            Achievements.OnAchievementUnlocked += a => OnAchievementUnlocked?.Invoke(a);

            Console.WriteLine("✅ SimulationEngine initialized — all subsystems wired");
        }

        /// <summary>
        /// Advance the simulation by the given number of days.
        /// Executes all subsystems in correct dependency order.
        /// </summary>
        public void Advance(int days)
        {
            if (days <= 0) return;

            for (int day = 0; day < days; day++)
            {
                AdvanceOneDay();
                State.Day++; // Single source of truth for day counter
            }

            OnDayAdvanced?.Invoke(State.Day);
        }

        /// <summary>
        /// Single-day tick — the core simulation loop.
        /// </summary>
        private void AdvanceOneDay()
        {
            // ═══════════════════════════════════════════
            // PHASE 1: WEATHER & ENVIRONMENT
            // ═══════════════════════════════════════════
            Weather.AdvanceDays(1);
            double temperature = Weather.CurrentTemperatureC;
            Audio?.Update(State.Day);

            // Update zone temperatures and water chemistry
            foreach (var zone in State.Zones)
            {
                zone.SetTemperature(temperature);
                zone.AdvanceDays(1); // This updates WaterChemistry internally
            }

            Environment.AdvanceDays(1);

            // ═══════════════════════════════════════════
            // PHASE 2: BIOLOGY (Growth & Mortality)
            // ═══════════════════════════════════════════
            foreach (var population in State.Populations)
            {
                // Get water chemistry penalty from the zone this population is in
                double waterPenalty = 1.0;
                double stockingDensity = 20.0; // default
                var zone = State.Zones.FirstOrDefault(z => z.Id == population.ZoneId);
                if (zone != null)
                {
                    waterPenalty = zone.WaterChemistry.CalculateGrowthPenalty();

                    // Update zone biomass for water chemistry calculations
                    double biomassKg = population.Cohorts.Sum(c => c.Count * c.MeanWeightKg);
                    zone.CurrentFishBiomassKg = biomassKg;

                    // Estimate stocking density (kg/m³) — assume 10,000 m³ per zone as default
                    stockingDensity = biomassKg / 10_000.0;
                }

                // Apply growth with environmental modifiers
                population.AdvanceDays(1);

                // Feed the population — FCR-aware, generates waste for water chemistry
                var feedResult = Feed.Feed(population, 1, temperature, stockingDensity, waterPenalty);

                // Feed waste → water chemistry (nitrogen and phosphorus loading)
                if (zone != null && feedResult.FeedUsedKg > 0)
                {
                    // The waste is already accounted for in WaterChemistry.UpdateOneDay via biomass
                    // but we can use feedResult for more precise tracking in future
                    zone.FeedConversionRatio = feedResult.ActualFCR;
                }
            }

            // ═══════════════════════════════════════════
            // PHASE 3: HEALTH (Disease & Sea Lice)
            // ═══════════════════════════════════════════
            SeaLice.AdvanceDays(1);

            // Use integrated disease system with LOD for performance
            IntegratedDisease.Update(
                State.Zones.ToList(),
                State.Populations.ToList(),
                1);

            // ═══════════════════════════════════════════
            // PHASE 4: EVENTS (Storyteller)
            // Run before Market so event-driven supply/demand shocks
            // (e.g. oversupply-crash, chile-collapse) are applied before
            // the market price advances for this day.
            // Storyteller reads State.Day (pre-increment) as the current day.
            // ═══════════════════════════════════════════
            Time.AdvanceDays(1);

            // ═══════════════════════════════════════════
            // PHASE 5: MARKET
            // Price simulation runs after events so any shocks registered
            // this day are reflected in today's closing price.
            // ═══════════════════════════════════════════
            MarketSimulation.AdvanceDays(1);

            // ═══════════════════════════════════════════
            // PHASE 6: SUSTAINABILITY (weekly)
            // Finance bonuses / penalties derived from the sustainability
            // score are updated here; FinanceManager reacts via events.
            // ═══════════════════════════════════════════
            if (State.Day - _lastSustainabilityUpdate >= SUSTAINABILITY_UPDATE_INTERVAL)
            {
                SustainabilityManager?.UpdateScore(State);
                _lastSustainabilityUpdate = State.Day;
            }

            // ═══════════════════════════════════════════
            // PHASE 7: TUTORIAL PROGRESS CHECK
            // ═══════════════════════════════════════════
            Tutorial?.CheckProgress();

            // ═══════════════════════════════════════════
            // PHASE 8: ANALYTICS SNAPSHOT
            // Captured last so all subsystem updates are reflected.
            // ═══════════════════════════════════════════
            Analytics?.RecordSnapshot(this);

            // ═══════════════════════════════════════════
            // PHASE 9: ACHIEVEMENTS
            // Checked after analytics so all state is current.
            // ═══════════════════════════════════════════
            Achievements?.CheckAchievements();
        }

        // ─── Sustainability event handlers ───

        private void OnSustainabilityScoreChanged(int score, CertificationLevel level)
        {
            FinanceManager?.UpdateSustainabilityBonuses(score);

            if (score >= 95 && level == CertificationLevel.SustainabilityLeader)
            {
                OnEventTriggered?.Invoke("🏆 Achievement: Sustainability Leader!");
                Audio?.TriggerAlert("achievement");
            }
        }

        private void OnCertificationChanged(CertificationLevel newLevel)
        {
            if (MarketSimulation != null && SustainabilityManager != null)
            {
                var multiplier = SustainabilityManager.GetPriceMultiplier(newLevel);
                MarketSimulation.UpdateCertificationLevel(newLevel, multiplier);
            }

            // Non-compliance fines
            if (newLevel == CertificationLevel.NonCompliant && State.Day % 90 == 0)
            {
                FinanceManager?.Spend(50_000m);
                OnEventTriggered?.Invoke("💸 Non-compliance fine: 50,000 NOK");
            }
        }

        // ─── Storyteller event handler ───

        private void HandleStorytellerEvent(EventRecord record)
        {
            // Notify UI
            OnEventFired?.Invoke(record);
            OnEventTriggered?.Invoke($"📣 [{record.Category}] {record.Name}: {record.Description}");

            Console.WriteLine($"[Day {record.Day}] EVENT: {record.Name} — {record.Description} ({record.BalanceChange:+#,0;-#,0;0} NOK)");

            // Audio: map event category to alert sounds
            switch (record.Category)
            {
                case EventCategory.Disease:
                    Audio?.TriggerAlert("disease_outbreak");
                    break;
                case EventCategory.Market when record.BalanceChange < 0:
                    Audio?.TriggerAlert("market_crash");
                    break;
            }

            // Consequence chains: route each category to the appropriate subsystem
            switch (record.Category)
            {
                case EventCategory.Disease:
                    ApplyDiseaseConsequence(record);
                    break;

                case EventCategory.Weather:
                    ApplyWeatherConsequence(record);
                    break;

                case EventCategory.Market:
                    ApplyMarketConsequence(record);
                    break;

                case EventCategory.Regulation:
                    ApplyRegulatoryConsequence(record);
                    break;
            }
        }

        private void ApplyDiseaseConsequence(EventRecord record)
        {
            // Run extra disease update days to reflect outbreak escalation
            if (State.Populations.Count > 0)
                IntegratedDisease?.Update(State.Zones.ToList(), State.Populations.ToList(), 3);
        }

        private void ApplyWeatherConsequence(EventRecord record)
        {
            switch (record.Id)
            {
                case "storm-damage":
                    Weather?.ForceWeatherState(WeatherState.Storm);
                    break;
                case "heatwave":
                    Weather?.ApplyTemperatureAnomaly(+4.0);
                    break;
                case "cold-snap":
                    Weather?.ApplyTemperatureAnomaly(-3.5);
                    break;
                case "algal-bloom-toxic":
                case "weather-algal-bloom":
                case "oxygen-depletion":
                    Weather?.ForceWeatherState(WeatherState.Severe);
                    break;
                default:
                    Weather?.ForceWeatherState(WeatherState.Storm);
                    break;
            }
        }

        private void ApplyMarketConsequence(EventRecord record)
        {
            switch (record.Id)
            {
                case "oversupply-crash":
                case "market-crash":
                    MarketSimulation?.ApplyExternalShock(supplyFactor: 1.15, demandFactor: 0.90);
                    break;
                case "price-spike-china":
                case "us-salmon-trend":
                    MarketSimulation?.ApplyExternalShock(supplyFactor: 1.0, demandFactor: 1.10);
                    break;
                case "chile-collapse":
                    MarketSimulation?.ApplyExternalShock(supplyFactor: 0.85, demandFactor: 1.0);
                    break;
                case "brexit-tariffs":
                    MarketSimulation?.ApplyExternalShock(supplyFactor: 1.0, demandFactor: 0.92);
                    break;
                case "nok-weakens":
                    MarketSimulation?.ApplyExternalShock(supplyFactor: 1.0, demandFactor: 1.05);
                    break;
                case "asc-premium":
                case "contract-secured":
                    MarketSimulation?.ApplyExternalShock(supplyFactor: 1.0, demandFactor: 1.06);
                    break;
                default:
                    // Unknown market event — mild demand nudge
                    MarketSimulation?.ApplyExternalShock(supplyFactor: 1.0, demandFactor: 0.97);
                    break;
            }
        }

        private void ApplyRegulatoryConsequence(EventRecord record)
        {
            switch (record.Id)
            {
                case "traffic-light-red":
                case "escape-penalty":
                case "resource-rent-tax":
                case "welfare-inspection":
                    SustainabilityManager?.ApplyRegulatoryPenalty(10);
                    break;
                case "traffic-light-green":
                case "development-license":
                    // Positive regulatory events — force a score re-evaluation
                    SustainabilityManager?.UpdateScore(State);
                    break;
                default:
                    SustainabilityManager?.ApplyRegulatoryPenalty(5);
                    break;
            }
        }

        /// <summary>
        /// Register a system for generic management. Specific subsystems should be
        /// set via properties instead (Weather, Disease, etc.).
        /// </summary>
        public void RegisterSystem(object system) { /* reserved for future plugin/mod systems */ }
    }
}