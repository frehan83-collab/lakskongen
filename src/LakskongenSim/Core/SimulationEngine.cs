using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LakskongenSim.Biology;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Health;
using LakskongenSim.Sustainability;

namespace LakskongenSim.Core
{
    /// <summary>
    /// Central orchestrator connecting all subsystems in correct update order.
    /// Update order: Weather → Environment/Water → Biology/Growth → Health/Disease → Market → Finance
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

        // ─── Update intervals ───
        private int _lastSustainabilityUpdate = 0;
        private const int SUSTAINABILITY_UPDATE_INTERVAL = 7; // Weekly

        // ─── Events ───
        public event Action<int>? OnDayAdvanced;
        public event Action<string>? OnEventTriggered;

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
            }

            State.Day += days;
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
            // PHASE 4: MARKET & FINANCE
            // ═══════════════════════════════════════════
            MarketSimulation.AdvanceDays(1);

            // ═══════════════════════════════════════════
            // PHASE 5: SUSTAINABILITY (weekly)
            // ═══════════════════════════════════════════
            if (State.Day - _lastSustainabilityUpdate >= SUSTAINABILITY_UPDATE_INTERVAL)
            {
                SustainabilityManager?.UpdateScore(State);
                _lastSustainabilityUpdate = State.Day;
            }
        }

        // ─── Sustainability event handlers ───

        private void OnSustainabilityScoreChanged(int score, CertificationLevel level)
        {
            FinanceManager?.UpdateSustainabilityBonuses(score);

            if (score >= 95 && level == CertificationLevel.SustainabilityLeader)
            {
                OnEventTriggered?.Invoke("🏆 Achievement: Sustainability Leader!");
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

        /// <summary>
        /// Register a system for generic management. Specific subsystems should be
        /// set via properties instead (Weather, Disease, etc.).
        /// </summary>
        public void RegisterSystem(object system) { /* reserved for future plugin/mod systems */ }
    }
}