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
using LakskongenSim.Market;
using LakskongenSim.Markets;
using System.Globalization;
using System.Diagnostics;

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
        public WeatherSystem Weather { get; private set; } = null!;
        public EnvironmentModel Environment { get; private set; } = null!;
        public GrowthModel Growth { get; private set; } = null!;
        public MortalityModel Mortality { get; private set; } = null!;
        public FeedSystem Feed { get; private set; } = null!;
        public SeaLiceModel SeaLice { get; private set; } = null!;
        public DiseaseSystem Disease { get; private set; } = null!;
        public IntegratedDiseaseSystem IntegratedDisease { get; private set; } = null!;
        public Hatchery Hatchery { get; private set; } = null!;
        public Genetics Genetics { get; private set; } = null!;
        public Market.MarketSimulation MarketSimulation { get; set; } = null!;
        public ContractSystem ContractSystem { get; private set; } = null!;
        public InternationalMarketSystem InternationalMarketSystem { get; private set; } = null!;
        public Finance.FinanceManager FinanceManager { get; set; } = null!;
        public Finance.FinancialStatements FinancialStatements { get; private set; } = null!;
        public SustainabilityManager SustainabilityManager { get; private set; } = null!;
        public Storyteller Storyteller { get; private set; } = null!;
        public TutorialSystem Tutorial { get; private set; } = null!;
        public AnalyticsTracker Analytics { get; private set; } = null!;
        public AudioManager Audio { get; private set; } = null!;
        public AchievementSystem Achievements { get; private set; } = null!;
        public ResearchSystem Research { get; private set; } = null!;

        // ─── Update intervals ───
        private int _lastSustainabilityUpdate = 0;
        private const int SUSTAINABILITY_UPDATE_INTERVAL = 7; // Weekly

        private BalancingConfig _balancing = new(); // Track whether events/disease are enabled
        private bool _enableEvents = true;

        // ─── External data calibration ───
        private AquacultureDataConfig _aquacultureDataConfig = new();
        private AquacultureGameplayModifiers _aquacultureModifiers = AquacultureGameplayModifiers.Default();
        private BarentsWatchClientOptions _barentsWatchClientOptions = new();
        private BarentsWatchFishHealthPipeline? _barentsWatchPipeline;

        // ─── Events ───
        public event Action<int>? OnDayAdvanced;
        public event Action<ResearchNode>? OnResearchCompleted;
        public event Action<string>? OnEventTriggered;
        public event Action<EventRecord>? OnEventFired;
        public event Action<Achievement>? OnAchievementUnlocked;

        /// <summary>
        /// Initialize all subsystems and wire them together.
        /// </summary>
        /// <param name="enableEvents">If false, disables random storyteller events and disease mortality (useful for deterministic tests).</param>
        public void Initialize(bool enableEvents = true)
        {
            _enableEvents = enableEvents;
            
            // Create subsystems
            Weather = new WeatherSystem();
            Environment = new EnvironmentModel();
            Growth = new GrowthModel();
            Mortality = new MortalityModel();
            Feed = new FeedSystem();
            SeaLice = new SeaLiceModel();
            Disease = new DiseaseSystem(SeaLice);
            IntegratedDisease = new IntegratedDiseaseSystem(SeaLice);
            
            // Wire disease systems to MortalityModel to prevent double mortality application
            Disease.SetMortalityModel(Mortality);
            IntegratedDisease.SetMortalityModel(Mortality);
            
            Hatchery = new Hatchery();
            Genetics = new Genetics();

            // Default finance and market if not set externally
            FinanceManager ??= new Finance.FinanceManager(10_000_000m); // 10M NOK starting capital
            FinancialStatements = new Finance.FinancialStatements(FinanceManager.Balance);
            
            // Sync FinanceManager balance back to GameState
            FinanceManager.OnBalanceChanged += balance => State.Balance = balance;
            
            MarketSimulation ??= new Market.MarketSimulation();
            ContractSystem = new ContractSystem();
            InternationalMarketSystem = new InternationalMarketSystem();

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

            // Initialize Storyteller event director (only enable default events if enableEvents=true)
            Storyteller = new Storyteller(Time, State, seed: null, enableDefaultEvents: enableEvents);
            if (enableEvents)
            {
                NorwegianEvents.RegisterAll(Storyteller);
            }
            Storyteller.OnEventRaised += HandleStorytellerEvent;

            // Hatchery → population
            Hatchery.OnNewCohort += cohort =>
            {
                // Assign genetics if missing (hatchery produces cohorts with specific genomes)
                if (cohort.Genetics == null)
                {
                    cohort.Genetics = Genetics.GenerateRandomGenome();
                }

                // Create a new population for this cohort (tests expect separate populations per release)
                var newPopulation = new FishPopulation(Growth, Mortality);
                newPopulation.AddCohort(cohort);
                State.Populations.Add(newPopulation);
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

            // Research / Tech Tree — restore persisted state from GameState
            Research = new ResearchSystem();
            foreach (var id in State.UnlockedResearchIds)
                Research.ForceUnlock(id);
            if (State.ActiveResearchId != null)
                Research.RestoreProgress(State.ActiveResearchId, State.ResearchProgress);
            Research.OnResearchCompleted += node =>
            {
                OnResearchCompleted?.Invoke(node);
                OnEventTriggered?.Invoke($"🔬 Research complete: {node.Name}!");
                Audio?.TriggerAlert("achievement");
                // Sync back to GameState
                SyncResearchToState();
            };

            LoadJsonConfigs();
            LoadAquacultureDataConfig();
            InitializeBarentsWatchPipeline();
            LoadExternalAquacultureSnapshot();
            ApplyExternalAquacultureCalibration();
            Console.WriteLine("✅ SimulationEngine initialized — all subsystems wired");
        }

        private void LoadJsonConfigs()
        {
            // Try multiple possible paths for data files
            string[] candidatePaths = {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "game", "data"),
                Path.Combine("game", "data"),
                "Data"
            };

            string? dataPath = null;
            foreach (var candidate in candidatePaths)
            {
                var testPath = Path.Combine(candidate, "balancing.json");
                if (File.Exists(testPath))
                {
                    dataPath = candidate;
                    break;
                }
            }

            if (dataPath == null)
            {
                Console.WriteLine("⚠️  Data directory not found. Using defaults for all configs.");
                return;
            }

            Console.WriteLine($"📁 Loading configs from: {Path.GetFullPath(dataPath)}");

            // ─── Load balancing parameters ───
            LoadBalancingConfig(dataPath);

            // ─── Load research tree ───
            LoadResearchNodes(dataPath);

            // ─── Load tutorial steps ───
            LoadTutorialSteps(dataPath);

            // ─── Load scenarios ───
            LoadScenarios(dataPath);

            // ─── Load achievements metadata ───
            LoadAchievementsMetadata(dataPath);

            // ─── Load encyclopedia entries ───
            LoadEncyclopediaEntries(dataPath);

            Console.WriteLine("✅ JSON config loading complete");
        }

        private void LoadAquacultureDataConfig()
        {
            try
            {
                var candidatePaths = new[]
                {
                    Path.Combine("Data", "AquacultureDataConfig.json"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "AquacultureDataConfig.json"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Data", "AquacultureDataConfig.json")
                };

                var configPath = candidatePaths.FirstOrDefault(File.Exists);
                if (configPath == null)
                {
                    Console.WriteLine("⚠️  AquacultureDataConfig.json not found, using external data defaults.");
                    _aquacultureDataConfig = new AquacultureDataConfig();
                    return;
                }

                var json = File.ReadAllText(configPath);
                _aquacultureDataConfig = JsonSerializer.Deserialize<AquacultureDataConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new AquacultureDataConfig();

                Console.WriteLine($"✓ Loaded aquaculture data config from {Path.GetFullPath(configPath)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Failed to load AquacultureDataConfig.json: {ex.Message}");
                _aquacultureDataConfig = new AquacultureDataConfig();
            }
        }

        private void LoadExternalAquacultureSnapshot()
        {
            try
            {
                if (_barentsWatchPipeline != null)
                {
                    var stored = _barentsWatchPipeline.LoadLatest();
                    if (stored != null)
                    {
                        State.ExternalAquacultureSnapshot = stored.Normalized;
                        State.ExternalDataAnalytics = stored.Analytics;
                        State.ExternalHealthWarnings = stored.Warnings ?? new List<HealthTrendWarning>();
                        State.ExternalHealthCrisis = BarentsWatchHealthCrisisEngine.BuildScenario(stored.Normalized);
                        State.ExternalHealthAnalytics = BarentsWatchHealthCrisisEngine.BuildReport(stored.Normalized);
                        Console.WriteLine(string.Format(
                            CultureInfo.InvariantCulture,
                            "✓ Loaded BarentsWatch fishhealth snapshot: biomass={0:F2}, lice={1:F2}, risk={2:F2}, warnings={3}",
                            stored.Normalized.NationalBiomassUtilization,
                            stored.Normalized.IndustryLicePressure,
                            stored.Normalized.ProductionAreaRiskIndex,
                            State.ExternalHealthWarnings.Count));
                        return;
                    }
                }

                var candidatePaths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "game", "data", "aquaculture_snapshot.json"),
                    Path.Combine("game", "data", "aquaculture_snapshot.json"),
                    Path.Combine("Data", "aquaculture_snapshot.json")
                };

                var snapshotPath = candidatePaths.FirstOrDefault(File.Exists);
                if (snapshotPath == null)
                {
                    Console.WriteLine("⚠️  aquaculture_snapshot.json not found, using bundled market/sustainability defaults only.");
                    return;
                }

                var json = File.ReadAllText(snapshotPath);
                State.ExternalAquacultureSnapshot = JsonSerializer.Deserialize<ExternalAquacultureSnapshot>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (State.ExternalAquacultureSnapshot != null)
                {
                    State.ExternalDataAnalytics = AquacultureDataIntegration.BuildAnalyticsSummary(State);
                    State.ExternalHealthWarnings = BarentsWatchHealthCrisisEngine.BuildWarnings(State.ExternalAquacultureSnapshot);
                    State.ExternalHealthCrisis = BarentsWatchHealthCrisisEngine.BuildScenario(State.ExternalAquacultureSnapshot);
                    State.ExternalHealthAnalytics = BarentsWatchHealthCrisisEngine.BuildReport(State.ExternalAquacultureSnapshot);
                    Console.WriteLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "✓ Loaded aquaculture snapshot: biomass={0:F2}, lice={1:F2}, risk={2:F2}",
                        State.ExternalAquacultureSnapshot.NationalBiomassUtilization,
                        State.ExternalAquacultureSnapshot.IndustryLicePressure,
                        State.ExternalAquacultureSnapshot.ProductionAreaRiskIndex));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Failed to load aquaculture snapshot: {ex.Message}");
            }
        }

        private void ApplyExternalAquacultureCalibration()
        {
            _aquacultureModifiers = AquacultureCalibrationEngine.Calculate(State, _aquacultureDataConfig);
            State.ExternalDataAnalytics = AquacultureDataIntegration.BuildAnalyticsSummary(State);

            if (State.ExternalAquacultureSnapshot != null)
            {
                State.ExternalHealthWarnings = BarentsWatchHealthCrisisEngine.BuildWarnings(State.ExternalAquacultureSnapshot);
                State.ExternalHealthCrisis = BarentsWatchHealthCrisisEngine.BuildScenario(State.ExternalAquacultureSnapshot);
                State.ExternalHealthAnalytics = BarentsWatchHealthCrisisEngine.BuildReport(State.ExternalAquacultureSnapshot);
            }

            var crisis = State.ExternalHealthCrisis ?? new HealthCrisisScenario();

            SeaLice?.SetExternalPressureMultiplier(_aquacultureModifiers.ZoneRiskMultiplier * crisis.DelousingCostMultiplier / Math.Max(1.0, crisis.GrowthPenaltyMultiplier));
            IntegratedDisease?.SetExternalDiseasePressure(_aquacultureModifiers.DiseasePressureMultiplier * crisis.MortalityPenaltyMultiplier);

            foreach (var zone in State.Zones)
            {
                var regional = AquacultureDataIntegration.GetProfileForZone(State, zone.Id);
                zone.ExternalRiskMultiplier = Math.Clamp(_aquacultureModifiers.ZoneRiskMultiplier * regional.LicePressure * crisis.EnvironmentalStressMultiplier, 0.85, 1.70);
                zone.ExternalNutrientMultiplier = Math.Clamp(_aquacultureModifiers.NutrientLoadMultiplier * (2.0 - regional.EnvironmentalCompliance) * crisis.EnvironmentalStressMultiplier, 0.90, 1.55);
                zone.ExternalDiseasePressure = Math.Clamp(regional.DiseasePressure * crisis.MortalityPenaltyMultiplier, 0.85, 1.90);
                zone.ExternalTemperatureStress = Math.Clamp(regional.TemperatureAnomaly * crisis.EnvironmentalStressMultiplier, 0.85, 1.45);
                zone.ExternalTreatmentIntensity = Math.Clamp(regional.TreatmentIntensity * crisis.DelousingCostMultiplier, 0.85, 1.60);
            }

            State.SustainabilityScore = SustainabilityManager?.CurrentScore ?? State.SustainabilityScore;
            State.SustainabilityCertification = SustainabilityManager?.CurrentCertification ?? State.SustainabilityCertification;
        }

        private void InitializeBarentsWatchPipeline()
        {
            try
            {
                var localSnapshotCandidates = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "game", "data", "barentswatch_fishhealth_raw.json"),
                    Path.Combine("game", "data", "barentswatch_fishhealth_raw.json"),
                    Path.Combine("Data", "barentswatch_fishhealth_raw.json")
                };

                _barentsWatchClientOptions = new BarentsWatchClientOptions
                {
                    BearerToken = System.Environment.GetEnvironmentVariable("BARENTSWATCH_TOKEN"),
                    LocalSnapshotPath = localSnapshotCandidates.FirstOrDefault(File.Exists)
                };

                var snapshotDirectory = Path.Combine(AppContext.BaseDirectory, "Data", "barentswatch-snapshots");
                var client = new BarentsWatchFishHealthClient(_barentsWatchClientOptions);
                var repository = new BarentsWatchSnapshotRepository(snapshotDirectory);
                _barentsWatchPipeline = new BarentsWatchFishHealthPipeline(client, repository);

                if (_barentsWatchClientOptions.HasToken || !string.IsNullOrWhiteSpace(_barentsWatchClientOptions.LocalSnapshotPath))
                {
                    try
                    {
                        var stored = _barentsWatchPipeline.IngestLatest();
                        State.ExternalAquacultureSnapshot = stored.Normalized;
                        State.ExternalDataAnalytics = stored.Analytics;
                        State.ExternalHealthWarnings = stored.Warnings;
                        State.ExternalHealthCrisis = BarentsWatchHealthCrisisEngine.BuildScenario(stored.Normalized);
                        State.ExternalHealthAnalytics = BarentsWatchHealthCrisisEngine.BuildReport(stored.Normalized);
                        Console.WriteLine($"✓ BarentsWatch Fishhealth pipeline ingested snapshot {stored.Normalized.SnapshotYear}-W{stored.Normalized.SnapshotWeek:D2}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️  BarentsWatch ingest failed, continuing with persisted/default snapshot: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine("⚠️  BarentsWatch token/local raw snapshot missing; pipeline initialized in passive mode.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Failed to initialize BarentsWatch pipeline: {ex.Message}");
            }
        }

        private void LoadBalancingConfig(string dataPath)
        {
            var filePath = Path.Combine(dataPath, "balancing.json");
            if (!File.Exists(filePath))
            {
                Console.WriteLine("⚠️  balancing.json not found, using default balancing values.");
                return;
            }

            try
            {
                var json = File.ReadAllText(filePath);
                var config = JsonSerializer.Deserialize<BalancingConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new BalancingConfig();
                _balancing = config;
                ApplyBalancingConfig(config);
                Console.WriteLine($"✓ Loaded balancing config (starting capital: {config.StartingCapital:N0} NOK)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Failed to load balancing.json: {ex.Message}");
            }
        }

        private void LoadResearchNodes(string dataPath)
        {
            var filePath = Path.Combine(dataPath, "research_nodes.json");
            if (!File.Exists(filePath))
            {
                Console.WriteLine("⚠️  research_nodes.json not found, using built-in research tree.");
                return;
            }

            try
            {
                var json = File.ReadAllText(filePath);
                var nodes = JsonSerializer.Deserialize<List<ResearchNode>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (nodes != null && nodes.Count > 0)
                {
                    Research.PopulateFromJson(nodes);
                    Console.WriteLine($"✓ Loaded {nodes.Count} research nodes from JSON");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Failed to load research_nodes.json: {ex.Message}");
            }
        }

        private void LoadTutorialSteps(string dataPath)
        {
            var filePath = Path.Combine(dataPath, "tutorial_steps.json");
            if (!File.Exists(filePath))
            {
                Console.WriteLine("⚠️  tutorial_steps.json not found, using built-in tutorial.");
                return;
            }

            try
            {
                var json = File.ReadAllText(filePath);
                // Tutorial steps in JSON don't include CompletionCondition delegates,
                // so we log the existence but don't override the hardcoded tutorial
                Console.WriteLine($"✓ Found tutorial_steps.json (tutorial uses built-in conditions)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Failed to read tutorial_steps.json: {ex.Message}");
            }
        }

        private void LoadScenarios(string dataPath)
        {
            var filePath = Path.Combine(dataPath, "scenarios.json");
            if (!File.Exists(filePath))
            {
                Console.WriteLine("⚠️  scenarios.json not found, scenarios unavailable.");
                return;
            }

            try
            {
                var json = File.ReadAllText(filePath);
                var scenarios = JsonSerializer.Deserialize<List<ScenarioDefinition>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (scenarios != null && scenarios.Count > 0)
                {
                    Console.WriteLine($"✓ Loaded {scenarios.Count} scenario definitions");
                    // ScenarioSystem integration would go here when implemented
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Failed to load scenarios.json: {ex.Message}");
            }
        }

        private void LoadAchievementsMetadata(string dataPath)
        {
            var filePath = Path.Combine(dataPath, "achievements_config.json");
            if (!File.Exists(filePath))
            {
                Console.WriteLine("⚠️  achievements_config.json not found, using built-in achievements.");
                return;
            }

            try
            {
                var json = File.ReadAllText(filePath);
                // Achievement conditions are hardcoded in AchievementSystem.RegisterAllAchievements()
                // This JSON only provides UI metadata (icons, categories)
                Console.WriteLine($"✓ Found achievements_config.json (achievements use built-in conditions)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Failed to read achievements_config.json: {ex.Message}");
            }
        }

        private void LoadEncyclopediaEntries(string dataPath)
        {
            var filePath = Path.Combine(dataPath, "encyclopedia_entries.json");
            if (!File.Exists(filePath))
            {
                Console.WriteLine("⚠️  encyclopedia_entries.json not found.");
                return;
            }

            try
            {
                var json = File.ReadAllText(filePath);
                var entries = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (entries != null && entries.Count > 0)
                {
                    Console.WriteLine($"✓ Loaded {entries.Count} encyclopedia entries");
                    // Encyclopedia system integration would go here when implemented
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Failed to load encyclopedia_entries.json: {ex.Message}");
            }
        }

        private void ApplyBalancingConfig(BalancingConfig config)
        {
            // Apply balancing parameters to subsystems
            // Note: Some subsystems may not have setter methods yet, so we apply what we can

            // Finance starting capital - only if FinanceManager was just created
            if (State.Day == 0 && config.StartingCapital > 0)
            {
                // Starting capital is set during FinanceManager construction
                Console.WriteLine($"  → Starting capital: {config.StartingCapital:N0} NOK");
            }

            // Feed system parameters
            Console.WriteLine($"  → Feed cost per kg: {config.FeedCostPerKgNOK:N2} NOK");
            Console.WriteLine($"  → FCR targets: Optimal={config.OptimalFCR}, Poor={config.PoorFCR}");

            // Market base price
            Console.WriteLine($"  → Base salmon price: {config.BaseSalmonPricePerKg:N0} NOK/kg");

            // Growth parameters
            Console.WriteLine($"  → Smolt maturation: {config.SmoltMaturationDays} days");
            Console.WriteLine($"  → Grow-out period: {config.GrowthToHarvestDays} days");
            Console.WriteLine($"  → Target harvest weight: {config.TargetHarvestWeightKg} kg");

            // NOTE: Full subsystem integration requires adding setter methods to:
            // - Feed.SetCostPerKg(), Feed.SetFCRTargets()
            // - Market.SetBasePrice(), Market.SetVolatility()
            // - Hatchery.SetCostAndMaturation()
            // - Growth.SetTargetWeight()
            // For now, this logs the values; subsystems use their own defaults.
        }

        private class BalancingConfig
        {
            // ─── Economy ───
            public decimal StartingCapital { get; set; } = 10_000_000;
            public decimal SmoltCostPerFish { get; set; } = 15;
            public decimal FeedCostPerKgNOK { get; set; } = 12.5m;
            public decimal BaseSalmonPricePerKg { get; set; } = 70;
            public double PriceVolatilityPercent { get; set; } = 15;
            public double SeasonalPriceSwingPercent { get; set; } = 20;

            // ─── Labor ───
            public decimal WorkerMonthlySalaryNOK { get; set; } = 45_000;
            public decimal ManagerMonthlySalaryNOK { get; set; } = 65_000;
            public int WorkersPerSite { get; set; } = 8;
            public int WorkersPerHatchery { get; set; } = 12;
            public int WorkersPerProcessingPlant { get; set; } = 25;

            // ─── Infrastructure ───
            public decimal SeaSiteBuildCostNOK { get; set; } = 5_000_000;
            public decimal HatcheryBuildCostNOK { get; set; } = 15_000_000;
            public decimal ProcessingPlantCostNOK { get; set; } = 50_000_000;
            public decimal FeedMillCostNOK { get; set; } = 30_000_000;

            // ─── Production limits ───
            public int MaxBiomassPerSiteKg { get; set; } = 780_000;
            public int MaxSitesPerZone { get; set; } = 4;
            public int MABLimitKgPerLicense { get; set; } = 780_000;

            // ─── Operations ───
            public decimal HarvestCostPerKgNOK { get; set; } = 3.5m;
            public decimal ProcessingCostPerKgNOK { get; set; } = 8;
            public decimal TransportCostPerKgPerKmNOK { get; set; } = 0.02m;

            // ─── Health & Treatment ───
            public decimal DelousingCostMechanicalNOK { get; set; } = 250_000;
            public decimal DelousingCostChemicalNOK { get; set; } = 150_000;
            public decimal DelousingCostCleanerfishNOK { get; set; } = 100_000;
            public decimal VaccinationCostPerKgBiomass { get; set; } = 0.5m;
            public double MortalityRateBasePercent { get; set; } = 0.03;
            public double MortalityRateISAPercent { get; set; } = 0.25;
            public double SeaLiceThresholdPerFish { get; set; } = 0.5;
            public double SeaLiceTreatmentThreshold { get; set; } = 0.2;

            // ─── Finance ───
            public double InsurancePremiumPercent { get; set; } = 0.02;
            public double LoanInterestRate { get; set; } = 0.065;
            public double MaxLoanMultiplier { get; set; } = 3.0;
            public double CorporateTaxRate { get; set; } = 0.22;
            public double ResourceRentTaxRate { get; set; } = 0.25;

            // ─── Licensing ───
            public decimal LicenseCostNOK { get; set; } = 150_000_000;

            // ─── Biology ───
            public int SmoltMaturationDays { get; set; } = 365;
            public int GrowthToHarvestDays { get; set; } = 540;
            public double TargetHarvestWeightKg { get; set; } = 5.0;
            public double MinHarvestWeightKg { get; set; } = 3.5;
            public double MaxHarvestWeightKg { get; set; } = 7.0;
            public double OptimalFCR { get; set; } = 1.15;
            public double PoorFCR { get; set; } = 1.4;

            // ─── Environment ───
            public double OptimalTemperatureMinC { get; set; } = 8;
            public double OptimalTemperatureMaxC { get; set; } = 14;
            public double LethalTemperatureMaxC { get; set; } = 23;
            public int FallowPeriodDays { get; set; } = 60;

            // ─── Compliance & Certification ───
            public decimal EscapeFinePer100Fish { get; set; } = 500_000;
            public decimal ASCCertificationCostNOK { get; set; } = 2_000_000;
            public decimal ASCAuditCostYearlyNOK { get; set; } = 500_000;
            public double ASCPricePremiumPercent { get; set; } = 8;
            public double OrganicPricePremiumPercent { get; set; } = 15;

            // ─── Research ───
            public decimal ResearchBaseCostNOK { get; set; } = 500_000;
            public decimal ResearchMaxCostNOK { get; set; } = 5_000_000;
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
            var overallStopwatch = Stopwatch.StartNew();

            // ═══════════════════════════════════════════
            // PHASE 1: WEATHER & ENVIRONMENT
            // ═══════════════════════════════════════════
            Weather.AdvanceDays(1);
            double temperature = Weather.CurrentTemperatureC;
            Audio?.Update(State.Day);

            // Update environment model and all zones
            Environment.AdvanceDays(1);
            foreach (var zone in State.Zones)
            {
                zone.SetTemperature(temperature);
                // zone.AdvanceDays(1) is already called by Environment.AdvanceDays(1)
            }

            // ═══════════════════════════════════════════
            // PHASE 2A: HEALTH PREP (Calculate disease rates BEFORE biology)
            // ═══════════════════════════════════════════
            Mortality.ClearDiseaseMortalityRates(); // Clear previous day's disease rates
            
            SeaLice.AdvanceDays(1);

            // Only calculate disease if events are enabled (skip in deterministic tests)
            if (_enableEvents)
            {
                // Calculate disease mortality rates and inject into MortalityModel
                // This MUST happen before FishPopulation.AdvanceDays() to avoid double application
                IntegratedDisease.Update(
                    State.Zones.ToList(),
                    State.Populations.ToList(),
                    1);
            }

            // ═══════════════════════════════════════════
            // PHASE 2B: BIOLOGY (Growth & Mortality)
            // Optimized: indexed loops, manual sums, in-place updates (T56)
            // ═══════════════════════════════════════════
            var biologyStopwatch = Stopwatch.StartNew();
            for (int popIdx = 0; popIdx < State.Populations.Count; popIdx++)
            {
                var population = State.Populations[popIdx];
                var zone = State.Zones.FirstOrDefault(z => z.Id == population.ZoneId);
                double waterPenalty = zone?.WaterChemistry.CalculateGrowthPenalty() ?? 1.0;

                // Manual biomass sum — avoids LINQ delegate allocation (T56)
                double biomassKg = 0.0;
                for (int ci = 0; ci < population.Cohorts.Count; ci++)
                {
                    var c = population.Cohorts[ci];
                    biomassKg += c.Count * c.MeanWeightKg;
                }

                if (zone != null)
                {
                    zone.CurrentFishBiomassKg = biomassKg;
                }

                // Advance population: age and mortality (in-place, T56)
                population.AdvanceDays(1);

                // Manual growth adjustment — indexed loop, in-place (T56)
                for (int ci = 0; ci < population.Cohorts.Count; ci++)
                {
                    var cohort = population.Cohorts[ci];

                    // Apply genetics multiplier
                    double geneticMultiplier = cohort.Genetics?.GetGrowthMultiplier() ?? 1.0;

                    // Use SDE model for more realistic growth
                    double newCalculatedWeight = Growth.ApplySDEGrowthOneDay(
                        cohort.MeanWeightKg,
                        temperature,
                        5.5, // Target 5.5kg
                        3.0, // Genetic maximum growth rate α_max
                        0.02,
                        cohort.Stage);

                    // Apply environmental and genetic modifiers in-place
                    double delta = newCalculatedWeight - cohort.MeanWeightKg;
                    cohort.MeanWeightKg += (delta * geneticMultiplier * waterPenalty);
                    if (cohort.MeanWeightKg < 0) cohort.MeanWeightKg = 0;
                }

                // Feed the population — FCR-aware
                double stockingDensity = zone != null ? (biomassKg / 10_000.0) : 20.0;
                var feedResult = Feed.Feed(population, 1, temperature, stockingDensity, waterPenalty);

                if (zone != null && feedResult.FeedUsedKg > 0)
                {
                    zone.FeedConversionRatio = feedResult.ActualFCR;
                }
            }
            biologyStopwatch.Stop();

            // ═══════════════════════════════════════════
            // PHASE 4: EVENTS (Storyteller)
            // ═══════════════════════════════════════════
            Time.AdvanceDays(1);

            // ═══════════════════════════════════════════
            // PHASE 5: MARKET & CONTRACTS
            // ═══════════════════════════════════════════
            if (_aquacultureModifiers.ProfitabilityAdjustment != 0m)
            {
                var supplyFactor = 1.0 - (double)_aquacultureModifiers.ProfitabilityAdjustment;
                var demandFactor = 1.0 + (double)_aquacultureModifiers.ProfitabilityAdjustment;
                MarketSimulation.ApplyExternalShock(supplyFactor, demandFactor);
            }

            if (State.ExternalAquacultureSnapshot != null)
            {
                var crisis = State.ExternalHealthCrisis ?? new HealthCrisisScenario();
                MarketSimulation.ApplyIndustrySignals(
                    State.ExternalAquacultureSnapshot.NationalBiomassUtilization,
                    State.ExternalAquacultureSnapshot.DiseasePressureIndex * crisis.MortalityPenaltyMultiplier,
                    State.ExternalAquacultureSnapshot.MortalityStressIndex * crisis.EnvironmentalStressMultiplier,
                    State.ExternalAquacultureSnapshot.MarketPriceSupportIndex * crisis.MarketPriceSupportMultiplier);
            }

            MarketSimulation.AdvanceDays(1);

            // ═══════════════════════════════════════════
            // PHASE 6: FINANCE
            // ═══════════════════════════════════════════
            ProcessDailyFinance();

            // ═══════════════════════════════════════════
            // PHASE 7: SUSTAINABILITY (weekly)
            // ═══════════════════════════════════════════
            if (State.Day - _lastSustainabilityUpdate >= SUSTAINABILITY_UPDATE_INTERVAL)
            {
                SustainabilityManager?.UpdateScore(State);
                State.SustainabilityScore = SustainabilityManager?.CurrentScore ?? State.SustainabilityScore;
                State.SustainabilityCertification = SustainabilityManager?.CurrentCertification ?? State.SustainabilityCertification;
                _lastSustainabilityUpdate = State.Day;
            }

            // ═══════════════════════════════════════════
            // PHASE 8-10: TUTORIAL, ANALYTICS, ACHIEVEMENTS, RESEARCH
            // ═══════════════════════════════════════════
            Tutorial?.CheckProgress();
            Analytics?.RecordSnapshot(this);
            Achievements?.CheckAchievements();
            Research?.AdvanceDay();
            SyncResearchToState();

            overallStopwatch.Stop();
            // T56: Removed Console.WriteLine to avoid string allocation in hot path
            // Use Trace.WriteLine or conditional logging if debug output needed
        }

        private void ProcessDailyFinance()
        {
            // 1. Calculate operational costs (Wages, Maintenance)
            decimal dailyWages = 0m;
            int totalSites = State.SeaSites.Count;
            dailyWages += totalSites * _balancing.WorkersPerSite * (_balancing.WorkerMonthlySalaryNOK / 30m);
            
            if (State.OwnsHatchery)
                dailyWages += _balancing.WorkersPerHatchery * (_balancing.WorkerMonthlySalaryNOK / 30m);
            
            if (State.OwnsProcessingPlant)
                dailyWages += _balancing.WorkersPerProcessingPlant * (_balancing.WorkerMonthlySalaryNOK / 30m);

            // 2. Fixed maintenance costs (e.g., 0.01% of build cost daily)
            decimal dailyMaintenance = (totalSites * _balancing.SeaSiteBuildCostNOK * 0.0001m);
            
            // 3. Apply costs to FinanceManager and FinancialStatements
            decimal totalOpEx = dailyWages + dailyMaintenance;
            if (totalOpEx > 0)
            {
                FinanceManager.Spend(totalOpEx);
                FinancialStatements.RecordOperatingExpense(totalOpEx);
            }

            // 4. Interest on debt (placeholder - could be expanded with LoanSystem)
            // if (State.Debt > 0) ...

            // 5. Taxes (apply monthly)
            if (State.Day % 30 == 0)
            {
                decimal netIncome = FinancialStatements.ComputeNetIncome();
                if (netIncome > 0)
                {
                    decimal tax = netIncome * (decimal)_balancing.CorporateTaxRate;
                    FinanceManager.Spend(tax);
                    FinancialStatements.RecordTax(tax);
                }
            }
        }


        /// <summary>
        /// Synchronise the ResearchSystem's runtime state back into the serialisable GameState
        /// so that Save/Load correctly persists the tech tree progress.
        /// </summary>
        private void SyncResearchToState()
        {
            if (Research == null) return;
            State.UnlockedResearchIds = Research.GetUnlockedIds().ToList();
            State.ActiveResearchId    = Research.ActiveResearchId;
            State.ResearchProgress    = Research.ResearchProgress;
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