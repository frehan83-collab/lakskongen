using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LakskongenSim.Biology;
using LakskongenSim.Environment;
using LakskongenSim.Events;
using LakskongenSim.Expansion;
using LakskongenSim.Farm;
using LakskongenSim.Health;
using LakskongenSim.Sustainability;
using LakskongenSim.Market;
using LakskongenSim.Markets;
using LakskongenSim.Finance;
using LakskongenSim.Processing;
using LakskongenSim.Politics;
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
        public Finance.CompetitorAISystem CompetitorAI { get; private set; } = null!;
        public Finance.EmpireProgressionSystem EmpireProgression { get; private set; } = null!;
        public SustainabilityManager SustainabilityManager { get; private set; } = null!;
        public Storyteller Storyteller { get; private set; } = null!;
        public TutorialSystem Tutorial { get; private set; } = null!;
        public AnalyticsTracker Analytics { get; private set; } = null!;
        public AudioManager Audio { get; private set; } = null!;
        public AchievementSystem Achievements { get; private set; } = null!;
        public ResearchSystem Research { get; private set; } = null!;
        public Finance.StockMarketSystem StockMarket { get; private set; } = null!;
        public BrandReputation BrandReputation { get; private set; } = null!;
        public CrisisCapitalismSystem CrisisCapitalism { get; private set; } = null!;
        public VerticalIntegrationSystem VerticalIntegration { get; private set; } = null!;
        public Finance.LoanSystem LoanSystem { get; private set; } = null!;
        public Finance.TaxSystem TaxSystem { get; private set; } = null!;
        public Finance.InvestorSystem InvestorSystem { get; private set; } = null!;
        public Expansion.AcquisitionSystem AcquisitionSystem { get; private set; } = null!;
        public Finance.LicenseSystem LicenseSystem { get; private set; } = null!;
        public Politics.StakeholderFactionSystem StakeholderFactionSystem { get; private set; } = null!;
        public Market.MarketIntelligenceSystem MarketIntelligence { get; private set; } = null!;
        public Health.FishWelfare FishWelfare { get; private set; } = null!;
        public VisualMilestones VisualMilestones { get; private set; } = null!;
        public Market.CurrencySystem CurrencySystem { get; private set; } = null!;

        // ─── Farm Subsystems ───
        public List<RASFacility> RASFacilities { get; private set; } = new();
        public List<OffshoreFarm> OffshoreFarms { get; private set; } = new();
        public AIFeedingSystem AIFeeding { get; private set; } = null!;
        public List<PostSmoltFacility> PostSmoltFacilities { get; private set; } = new();

        // ─── Update intervals ───
        private int _lastSustainabilityUpdate = 0;
        private const int SUSTAINABILITY_UPDATE_INTERVAL = 7; // Weekly
        private int _lastBrandReputationUpdate = 0;
        private const int BRAND_REPUTATION_UPDATE_INTERVAL = 7; // Weekly
        private int _lastCrisisCapitalismUpdate = 0;
        private const int CRISIS_CAPITALISM_UPDATE_INTERVAL = 7; // Weekly - crises don't happen daily
        private int _lastVerticalIntegrationUpdate = 0;
        private const int VERTICAL_INTEGRATION_UPDATE_INTERVAL = 30; // Monthly - construction/integration is slow
        private int _quarterStartDay = 0;
        private int _lastCompetitorAIUpdate = 0;
        private const int COMPETITOR_AI_UPDATE_INTERVAL = 90; // Quarterly
        private int _lastEmpireProgressionUpdate = 0;
        private const int EMPIRE_PROGRESSION_UPDATE_INTERVAL = 7; // Weekly
        // Reserved for future systems (T68: not yet wired)
        // private int _lastAcquisitionSystemUpdate = 0;
        // private const int ACQUISITION_SYSTEM_UPDATE_INTERVAL = 7; // Weekly - M&A is slow
        // private int _lastLicenseSystemUpdate = 0;
        // private const int LICENSE_SYSTEM_UPDATE_INTERVAL = 30; // Monthly - license applications/renewals
        // private int _lastStakeholderFactionUpdate = 0;
        // private const int STAKEHOLDER_FACTION_UPDATE_INTERVAL = 7; // Weekly - faction relationship updates
        private int _lastTaxUpdate = 0;
        private const int TAX_UPDATE_INTERVAL = 90; // Quarterly
        private int _lastInvestorUpdate = 0;
        private const int INVESTOR_UPDATE_INTERVAL = 30; // Monthly
        private int _lastMarketIntelligenceUpdate = 0;
        private const int MARKET_INTELLIGENCE_UPDATE_INTERVAL = 7; // Weekly - market analysis updates
        private int _lastProductMixOptimizerUpdate = 0;
        private const int PRODUCT_MIX_OPTIMIZER_UPDATE_INTERVAL = 30; // Monthly - production planning is periodic

        private BalancingConfig _balancing = new(); // Track whether events/disease are enabled
        private bool _enableEvents = true;

        // ─── Farm systems daily OPEX (accumulated in TickDay, consumed in ProcessDailyFinance) ───
        private decimal _dailyFarmSystemsOpex = 0m;
        private decimal _dailyAiFeedingLicenseCost = 0m;

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
            FinancialStatements = new Finance.FinancialStatements();
            
            // NOTE: GameState.Balance is NO LONGER synced automatically.
            // It is only updated during save/load for backward compatibility.
            // FinanceManager.Balance is the SINGLE SOURCE OF TRUTH for cash.
            
            // Initialize CompetitorAI and EmpireProgression systems
            CompetitorAI = new Finance.CompetitorAISystem();
            EmpireProgression = new Finance.EmpireProgressionSystem();
            
            // Wire CompetitorAI events
            CompetitorAI.OnCompetitorAction += action =>
            {
                OnEventTriggered?.Invoke($"🏢 {action.NewsHeadline}");
            };
            CompetitorAI.OnQuarterlyReport += report =>
            {
                OnEventTriggered?.Invoke($"📊 Q{report.Quarter} {report.Year} Industry Report: {report.MarketOutlook} outlook, avg price {report.AverageSpotPrice:F0} NOK/kg");
            };
            
            // Wire EmpireProgression events
            EmpireProgression.OnPhaseTransition += transition =>
            {
                OnEventTriggered?.Invoke(transition.Headline);
                Audio?.TriggerAlert("achievement");
            };
            EmpireProgression.OnMilestoneUnlocked += milestone =>
            {
                OnEventTriggered?.Invoke($"🎯 Milestone unlocked: {milestone.Name} — {milestone.Description}");
                Audio?.TriggerAlert("achievement");
            };
            EmpireProgression.OnVictoryAchieved += victory =>
            {
                OnEventTriggered?.Invoke($"🏆 VICTORY: {victory.Name} — {victory.Description}");
                Audio?.TriggerAlert("achievement");
            };
            
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
            // Pass FinanceManager so events can modify balance correctly
            Storyteller = new Storyteller(Time, State, FinanceManager, seed: null, enableDefaultEvents: enableEvents);
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
                if (!FinanceManager.Spend(cost))
                {
                    OnEventTriggered?.Invoke($"⚠️ Insufficient funds for feed: {cost:N0} NOK. Balance: {FinanceManager.Balance:N0} NOK");
                    // Could trigger bankruptcy event here
                }
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

            // StockMarket and BrandReputation systems
            StockMarket = new Finance.StockMarketSystem();
            BrandReputation = new BrandReputation
            {
                BrandName = "Lakskongen",
                Strategy = BrandStrategy.Premium
            };

            // CrisisCapitalism and VerticalIntegration systems
            CrisisCapitalism = new CrisisCapitalismSystem();
            VerticalIntegration = new VerticalIntegrationSystem(FinanceManager);

            // Initialize financial systems (LoanSystem, TaxSystem, InvestorSystem)
            LoanSystem = new Finance.LoanSystem();
            TaxSystem = new Finance.TaxSystem();
            InvestorSystem = new Finance.InvestorSystem();

            // Wire LoanSystem events
            LoanSystem.OnInterestCharged += interest =>
            {
                OnEventTriggered?.Invoke($"💰 Interest charged: {interest:N0} NOK");
            };

            // Wire TaxSystem events
            TaxSystem.OnTaxPaid += tax =>
            {
                OnEventTriggered?.Invoke($"💸 Tax paid: {tax:N0} NOK");
            };

            // Wire InvestorSystem events
            InvestorSystem.OnIssued += (amount, type) =>
            {
                OnEventTriggered?.Invoke($"💼 Issued {type}: {amount:N0} NOK");
            };

            // Initialize new farm systems
            RASFacilities = new();
            OffshoreFarms = new();
            AIFeeding = new AIFeedingSystem();
            PostSmoltFacilities = new();

            // Initialize expansion, license, and politics systems
            AcquisitionSystem = new Expansion.AcquisitionSystem();
            LicenseSystem = new Finance.LicenseSystem();
            StakeholderFactionSystem = new Politics.StakeholderFactionSystem();

            // Initialize Market Intelligence, Fish Welfare, and Visual Milestones systems
            MarketIntelligence = new Market.MarketIntelligenceSystem();
            FishWelfare = new Health.FishWelfare();
            VisualMilestones = new VisualMilestones();
            CurrencySystem = new Market.CurrencySystem();

            // Wire MarketIntelligence events
            MarketIntelligence.OnNewsEvent += evt =>
            {
                OnEventTriggered?.Invoke($"📰 {evt.Headline}");
            };

            // Wire CurrencySystem events
            CurrencySystem.OnCurrencyEvent += evt =>
            {
                OnEventTriggered?.Invoke(evt);
            };

            // Wire FishWelfare events
            FishWelfare.OnConditionChanged += condition =>
            {
                if (condition < 0.5)
                    OnEventTriggered?.Invoke($"⚠️ Fish welfare degrading: {condition:F2}");
            };

            // Wire VisualMilestones events
            VisualMilestones.MilestoneAchieved += (title, description) =>
            {
                OnEventTriggered?.Invoke($"🎯 Milestone: {title} — {description}");
                Audio?.TriggerAlert("achievement");
            };

            // Wire LicenseSystem events to FinanceManager
            LicenseSystem.OnLicenseCharged += (siteId, amount) =>
            {
                if (!FinanceManager.Spend(amount))
                {
                    OnEventTriggered?.Invoke($"⚠️ Cannot pay license fee for {siteId}: {amount:N0} NOK. Balance: {FinanceManager.Balance:N0} NOK");
                    FinanceManager.ForceSpend(amount);
                }
                else
                {
                    OnEventTriggered?.Invoke($"💰 License fee paid for {siteId}: {amount:N0} NOK");
                }
            };
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
            // NOTE: Do NOT override zone temperatures here — zones maintain their own temperatures
            // zone.AdvanceDays(1) is already called by Environment.AdvanceDays(1)

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
            // PHASE 3: FARM SYSTEMS & DAILY OPEX COLLECTION
            // ═══════════════════════════════════════════
            _dailyFarmSystemsOpex = 0m;
            _dailyAiFeedingLicenseCost = 0m;
            double totalBiomassForAI = State.Populations.Sum(p => p.GetTotalBiomassKg());
            decimal currentFeedPricePerKg = _balancing.FeedCostPerKgNOK; // Using balancing config for now

            // AIFeedingSystem TickDay (applies globally for all farms)
            if (AIFeeding.IsInstalled)
            {
                var aiFeedResult = AIFeeding.TickDay(totalBiomassForAI, currentFeedPricePerKg);
                _dailyAiFeedingLicenseCost = aiFeedResult.DailyLicenseCost;
            }

            foreach (var rasFacility in RASFacilities)
            {
                var rasResult = rasFacility.TickDay(State.Day, Weather.CurrentTemperatureC);
                _dailyFarmSystemsOpex += rasResult.TotalDailyOPEX;
            }

            foreach (var offshoreFarm in OffshoreFarms)
            {
                var offshoreResult = offshoreFarm.TickDay(State.Day, Weather.CurrentWindSpeedMs, Weather.CurrentWaveHeight, Weather.CurrentState == WeatherState.Storm);
                _dailyFarmSystemsOpex += offshoreResult.TotalDailyOPEX;
            }

            foreach (var postSmoltFacility in PostSmoltFacilities)
            {
                var postSmoltResult = postSmoltFacility.TickDay(State.Day);
                _dailyFarmSystemsOpex += postSmoltResult.TotalDailyOPEX;
            }


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
            CurrencySystem.AdvanceDays(1);

            // ═══════════════════════════════════════════
            // PHASE 6: FINANCE
            // ═══════════════════════════════════════════
            ProcessDailyFinance();

            // ═══════════════════════════════════════════
            // PHASE 6B: STOCK MARKET (daily price + quarterly earnings)
            // ═══════════════════════════════════════════
            // Daily price movement (only if listed)
            if (StockMarket != null && StockMarket.Status != Finance.OwnershipStatus.Private)
            {
                // Check for quarterly earnings report (every ~90 days)
                if ((State.Day - _quarterStartDay) >= 90)
                {
                    // Process quarterly earnings
                    decimal quarterlyRevenue = FinancialStatements?.Revenue ?? 0m;
                    decimal quarterlyEBITDA = (FinancialStatements?.Revenue ?? 0m) - (FinancialStatements?.COGS ?? 0m) - (FinancialStatements?.OperatingExpenses ?? 0m);
                    decimal quarterlyNetIncome = FinancialStatements?.ComputeNetIncome() ?? 0m;
                    decimal totalAssets = FinancialStatements?.Assets ?? (State.SeaSites.Count * 5_000_000m);
                    decimal totalDebt = FinancialStatements?.Liabilities ?? 0m;

                    var earningsReport = StockMarket.AdvanceQuarter(
                        quarterlyRevenue,
                        quarterlyEBITDA,
                        quarterlyNetIncome,
                        totalAssets,
                        totalDebt);

                    if (earningsReport != null)
                    {
                        OnEventTriggered?.Invoke(
                            $"📊 Q{earningsReport.Quarter} Earnings: EPS {earningsReport.EPS:F2} NOK " +
                            $"({(earningsReport.Beat ? "BEAT" : "MISS")} consensus {earningsReport.ConsensusEPS:F2})");
                    }

                    _quarterStartDay = State.Day;
                }
            }

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
            // PHASE 7A: BRAND REPUTATION (weekly)
            // ═══════════════════════════════════════════
            if (BrandReputation != null && State.Day - _lastBrandReputationUpdate >= BRAND_REPUTATION_UPDATE_INTERVAL)
            {
                // Natural reputation decay over time (prevents stagnation)
                BrandReputation.ApplyNaturalDecay(0.005m);

                // Reward consistent quality performance
                double avgBiomass = State.Populations.Any() ? State.Populations.Average(p => p.GetTotalBiomassKg()) : 0;
                if (avgBiomass > 100_000 && State.SustainabilityScore > 60)
                {
                    BrandReputation.RecordEvent(
                        ReputationEvent.ConsistentQuality,
                        "Maintained quality standards and sustainability",
                        severity: 1);
                }

                _lastBrandReputationUpdate = State.Day;
            }

            // ═══════════════════════════════════════════
            // PHASE 7B: COMPETITOR AI (quarterly)
            // ═══════════════════════════════════════════
            if (State.Day - _lastCompetitorAIUpdate >= COMPETITOR_AI_UPDATE_INTERVAL)
            {
                // Calculate player metrics for competitor context
                float playerMarketShare = CalculatePlayerMarketShare();
                decimal playerRevenue = FinancialStatements?.Revenue ?? 0m;
                float spotPrice = (float)(MarketSimulation?.PricePerKg ?? 70m);
                int playerLicenseCount = State.SeaSites.Count; // Approximate licenses by site count

                CompetitorAI?.AdvanceQuarter(playerMarketShare, playerRevenue, spotPrice, playerLicenseCount);
                _lastCompetitorAIUpdate = State.Day;
            }

            // ═══════════════════════════════════════════
            // PHASE 7C: EMPIRE PROGRESSION (weekly)
            // ═══════════════════════════════════════════
            if (State.Day - _lastEmpireProgressionUpdate >= EMPIRE_PROGRESSION_UPDATE_INTERVAL)
            {
                var empireState = BuildEmpireState();
                EmpireProgression?.Update(empireState);
                _lastEmpireProgressionUpdate = State.Day;
            }

            // ═══════════════════════════════════════════
            // PHASE 7D: CRISIS CAPITALISM (weekly)
            // ═══════════════════════════════════════════
            if (State.Day - _lastCrisisCapitalismUpdate >= CRISIS_CAPITALISM_UPDATE_INTERVAL)
            {
                CrisisCapitalism?.Update();
                _lastCrisisCapitalismUpdate = State.Day;
            }

            // ═══════════════════════════════════════════
            // PHASE 7E: VERTICAL INTEGRATION (monthly)
            // ═══════════════════════════════════════════
            if (State.Day - _lastVerticalIntegrationUpdate >= VERTICAL_INTEGRATION_UPDATE_INTERVAL)
            {
                // VerticalIntegrationSystem doesn't have an explicit Update() method
                // but we track the interval for future use (e.g., construction completion checks)
                _lastVerticalIntegrationUpdate = State.Day;
            }

            // ═══════════════════════════════════════════
            // PHASE 7F: TAX SYSTEM (quarterly)
            // ═══════════════════════════════════════════
            if (State.Day - _lastTaxUpdate >= TAX_UPDATE_INTERVAL)
            {
                decimal netIncome = FinancialStatements?.ComputeNetIncome() ?? 0m;
                if (netIncome > 0 && TaxSystem != null)
                {
                    decimal quarterlyTax = TaxSystem.ComputeTax(netIncome);
                    if (quarterlyTax > 0)
                    {
                        if (!FinanceManager.Spend(quarterlyTax))
                        {
                            OnEventTriggered?.Invoke($"💸 Cannot pay {quarterlyTax:N0} NOK in quarterly taxes. Balance: {FinanceManager.Balance:N0} NOK");
                            FinanceManager.ForceSpend(quarterlyTax);
                        }
                        else
                        {
                            FinancialStatements?.RecordTax(quarterlyTax);
                        }
                    }
                }
                _lastTaxUpdate = State.Day;
            }

            // ═══════════════════════════════════════════
            // PHASE 7G: INVESTOR SYSTEM (monthly reporting)
            // ═══════════════════════════════════════════
            if (State.Day - _lastInvestorUpdate >= INVESTOR_UPDATE_INTERVAL)
            {
                // Monthly investor update - report dilution and bond status
                if (InvestorSystem != null)
                {
                    decimal dilution = InvestorSystem.DilutionFraction();
                    if (dilution > 0.01m)
                    {
                        OnEventTriggered?.Invoke($"📊 Equity dilution: {dilution * 100:F1}% — {InvestorSystem.OutstandingShares:N0} shares outstanding");
                    }
                    if (InvestorSystem.OutstandingBondPrincipal > 0)
                    {
                        OnEventTriggered?.Invoke($"💼 Outstanding bonds: {InvestorSystem.OutstandingBondPrincipal:N0} NOK");
                    }
                }
                _lastInvestorUpdate = State.Day;
            }

            // ═══════════════════════════════════════════
            // PHASE 7H-7J: RESERVED FOR FUTURE SYSTEMS (T68: not yet wired)
            // ═══════════════════════════════════════════
            // ACQUISITION SYSTEM, LICENSE SYSTEM, STAKEHOLDER FACTION SYSTEM
            // These will be wired in future tasks
            /*
            if (State.Day - _lastAcquisitionSystemUpdate >= ACQUISITION_SYSTEM_UPDATE_INTERVAL)
            {
                _lastAcquisitionSystemUpdate = State.Day;
            }

            if (State.Day - _lastLicenseSystemUpdate >= LICENSE_SYSTEM_UPDATE_INTERVAL)
            {
                foreach (var site in State.SeaSites)
                {
                    LicenseSystem.ChargeAnnual(site.Id);
                }
                _lastLicenseSystemUpdate = State.Day;
            }

            if (State.Day - _lastStakeholderFactionUpdate >= STAKEHOLDER_FACTION_UPDATE_INTERVAL)
            {
                StakeholderFactionSystem.ProcessExpiredDemands();
                _lastStakeholderFactionUpdate = State.Day;
            }
            */

            // ═══════════════════════════════════════════
            // PHASE 7K: MARKET INTELLIGENCE (weekly)
            // ═══════════════════════════════════════════
            if (State.Day - _lastMarketIntelligenceUpdate >= MARKET_INTELLIGENCE_UPDATE_INTERVAL)
            {
                // Market intelligence updates weekly with current spot price
                decimal currentSpotPrice = MarketSimulation?.PricePerKg ?? 70m;
                MarketIntelligence?.AdvanceWeek(currentSpotPrice);
                _lastMarketIntelligenceUpdate = State.Day;
            }

            // ═══════════════════════════════════════════
            // PHASE 7L: FISH WELFARE (daily)
            // ═══════════════════════════════════════════
            // Fish welfare is monitored daily through environmental and health conditions
            // Condition score is updated via FishWelfare.ModifyCondition() called by other systems
            // (e.g., disease systems, environment stress, stocking density)

            // ═══════════════════════════════════════════
            // PHASE 7M: PRODUCT MIX OPTIMIZER (monthly)
            // ═══════════════════════════════════════════
            // ProductMixOptimizer is called on-demand when player opens processing UI
            // No periodic update needed — it's a decision support tool, not a simulation system
            // This interval marker is for future features (e.g., auto-optimization recommendations)
            if (State.Day - _lastProductMixOptimizerUpdate >= PRODUCT_MIX_OPTIMIZER_UPDATE_INTERVAL)
            {
                _lastProductMixOptimizerUpdate = State.Day;
            }

            // ═══════════════════════════════════════════
            // PHASE 7N: VISUAL MILESTONES (daily)
            // ═══════════════════════════════════════════
            // Visual milestones are checked daily for achievement conditions
            // Milestone checks are triggered by specific events (harvest, revenue, etc.)
            // implemented in the relevant systems that call VisualMilestones.CheckMilestone()

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
            dailyMaintenance += _dailyAiFeedingLicenseCost; // Add AI Feeding license cost here

            // 3. Apply costs to FinanceManager and FinancialStatements
            decimal totalOpEx = dailyWages + dailyMaintenance + _dailyFarmSystemsOpex;
            if (totalOpEx > 0)
            {
                if (!FinanceManager.Spend(totalOpEx))
                {
                    // Bankruptcy warning - insufficient funds for operations
                    OnEventTriggered?.Invoke($"💸 BANKRUPTCY WARNING: Cannot pay {totalOpEx:N0} NOK in operating costs. Balance: {FinanceManager.Balance:N0} NOK");
                    
                    // Force spend to create negative balance (debt)
                    FinanceManager.ForceSpend(totalOpEx);
                }
                FinancialStatements?.RecordOperatingExpense(totalOpEx);
            }

            // 4. Interest on debt (LoanSystem - daily accrual)
            // Use InvestorSystem's bond principal as debt source
            if (InvestorSystem != null && InvestorSystem.OutstandingBondPrincipal > 0 && LoanSystem != null)
            {
                decimal dailyInterest = LoanSystem.ChargeInterest(InvestorSystem.OutstandingBondPrincipal, 1);
                if (dailyInterest > 0)
                {
                    if (!FinanceManager.Spend(dailyInterest))
                    {
                        OnEventTriggered?.Invoke($"💸 Cannot pay {dailyInterest:N0} NOK in loan interest. Balance: {FinanceManager.Balance:N0} NOK");
                        FinanceManager.ForceSpend(dailyInterest);
                    }
                    FinancialStatements?.RecordOperatingExpense(dailyInterest);
                }
            }

            // 5. Taxes (apply monthly)
            if (State.Day % 30 == 0)
            {
                decimal netIncome = FinancialStatements?.ComputeNetIncome() ?? 0m;
                if (netIncome > 0)
                {
                    decimal tax = netIncome * (decimal)_balancing.CorporateTaxRate;
                    if (!FinanceManager.Spend(tax))
                    {
                        OnEventTriggered?.Invoke($"⚠️ Cannot pay {tax:N0} NOK in taxes. Balance: {FinanceManager.Balance:N0} NOK");
                        FinanceManager.ForceSpend(tax);
                    }
                    FinancialStatements?.RecordTax(tax);
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
                if (FinanceManager != null)
                {
                    if (!FinanceManager.Spend(50_000m))
                    {
                        OnEventTriggered?.Invoke("⚠️ BANKRUPTCY WARNING: Cannot pay 50,000 NOK non-compliance fine. Balance: " + FinanceManager.Balance.ToString("N0") + " NOK");
                        FinanceManager.ForceSpend(50_000m);
                    }
                    else
                    {
                        OnEventTriggered?.Invoke("💸 Non-compliance fine: 50,000 NOK");
                    }
                }
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

        /// <summary>
        /// Calculate player's market share in Norwegian salmon production.
        /// </summary>
        private float CalculatePlayerMarketShare()
        {
            double totalBiomass = State.Populations.Sum(p => p.GetTotalBiomassKg());
            float playerWeeklyHarvest = (float)(totalBiomass / 1000.0); // Convert to tonnes
            float totalNorwegianWeeklyHarvest = 1_250_000f / 52f; // ~24,000 tonnes/week
            return Math.Min(1.0f, playerWeeklyHarvest / totalNorwegianWeeklyHarvest);
        }

        /// <summary>
        /// Build EmpireState snapshot for EmpireProgressionSystem.
        /// </summary>
        private Finance.EmpireState BuildEmpireState()
        {
            decimal annualRevenue = FinancialStatements?.Revenue ?? 0m;
            decimal annualEBITDA = FinancialStatements?.ComputeNetIncome() ?? 0m;
            double totalBiomass = State.Populations.Sum(p => p.GetTotalBiomassKg());

            return new Finance.EmpireState
            {
                LicenseCount = State.SeaSites.Count, // Approximate licenses by site count
                SiteCount = State.SeaSites.Count,
                ZoneCount = State.Zones.Count,
                MarketShareNorway = CalculatePlayerMarketShare(),
                MarketShareGlobal = 0f, // TODO: Calculate global share when international ops are implemented
                AnnualRevenue = annualRevenue,
                AnnualEBITDA = annualEBITDA,
                Valuation = FinanceManager?.Balance ?? 0m, // Simplified: use balance as proxy for valuation
                TotalAssets = FinanceManager?.Balance ?? 0m,
                AcquisitionCount = State.AcquisitionCount,
                HasProcessing = State.OwnsProcessingPlant,
                HasHatchery = State.OwnsHatchery,
                HasFeedMill = State.OwnsFeedMill,
                HasLogistics = VerticalIntegration?.GetIntegrationScore() >= 0.45f, // COLD_CHAIN + DISTRIBUTION = logistics
                HasRetailBrand = false, // TODO: Track retail brand
                IsPubliclyListed = StockMarket?.Status != Finance.OwnershipStatus.Private,
                HasInternationalOps = State.ExportMarkets.Count > 0,
                InternationalRegions = State.ExportMarkets.ToList(),
                SustainabilityScore = State.SustainabilityScore,
                CompetitorsAcquired = State.AcquisitionCount,
                CurrentStrategy = Finance.GrowthStrategy.Organic,
                CurrentProductStrategy = Finance.ProductStrategy.Commodity,
                IntegrationScore = VerticalIntegration?.GetIntegrationScore() ?? 0f,
                EmployeeCount = State.SeaSites.Count * 8 + (State.OwnsHatchery ? 12 : 0) + (State.OwnsProcessingPlant ? 25 : 0),
                WeeklyHarvestTonnes = (float)(totalBiomass / 1000.0)
            };
        }

        // ─── Save/Load Sync ───

        /// <summary>
        /// Prepare GameState for saving by syncing FinanceManager.Balance to GameState.Balance.
        /// Call this BEFORE SaveLoadManager.SaveGame().
        /// </summary>
        public void PrepareForSave()
        {
            #pragma warning disable CS0618 // Type or member is obsolete
            State.Balance = FinanceManager.Balance;
            #pragma warning restore CS0618

            // T74: Sync all empire systems to GameState for serialization
            SyncEmpireSystemsToState();
        }

        /// <summary>
        /// Sync all empire systems' runtime state into GameState for serialization (T74).
        /// Uses public properties directly to avoid adding dozens of getter methods.
        /// </summary>
        private void SyncEmpireSystemsToState()
        {
            // 1. CompetitorAI - use public Competitors property
            if (CompetitorAI != null)
            {
                State.CompetitorCompanies = CompetitorAI.Competitors
                    .Select(c => new CompetitorCompanyState
                    {
                        Id = c.Id,
                        Name = c.Name,
                        Revenue = c.Revenue,
                        MarketCap = c.MarketCap,
                        LicenseCount = c.LicenseCount,
                        MarketShareNorway = c.MarketShareNorway,
                        IsDistressed = c.IsDistressed
                    }).ToList();
            }

            // 2. EmpireProgression - store current phase as string (will be restored)
            if (EmpireProgression != null)
            {
                State.EmpireProgression = new EmpireProgressionState
                {
                    CurrentPhase = EmpireProgression.CurrentPhase.ToString(),
                    UnlockedMilestoneIds = new List<string>(), // Will be populated by EmpireProgression if needed
                    AchievedVictoryIds = new List<string>()
                };
            }

            // 3-15: Other systems - Keep empty for now to avoid build errors
            // These will be implemented incrementally as needed
            State.StockMarket = null;
            State.BrandReputation = null;
            State.ActiveCrises.Clear();
            State.VerticalIntegration = null;
            State.ActiveLoans.Clear();
            State.TaxHistory.Clear();
            State.InvestorSystem = null;
            State.Acquisitions.Clear();
            State.Licenses.Clear();
            State.FactionRelations.Clear();
            State.RecentMarketReports.Clear();
            State.FishWelfareCondition = 1.0m;
            State.AchievedVisualMilestones.Clear();
        }

        /// <summary>
        /// Restore state after loading by syncing GameState.Balance to FinanceManager.Balance
        /// and re-injecting dependencies into deserialized objects.
        /// Call this AFTER SaveLoadManager.LoadGame() with the loaded state.
        /// </summary>
        public void RestoreFromLoad(GameState loadedState)
        {
            // 1. Restore balance from saved state
            #pragma warning disable CS0618 // Type or member is obsolete
            if (loadedState.Balance > 0)
            {
                FinanceManager = new Finance.FinanceManager(loadedState.Balance);
            }
            #pragma warning restore CS0618

            // 2. Re-inject GrowthModel and MortalityModel into all FishPopulations
            foreach (var pop in loadedState.Populations)
            {
                pop.InjectDependencies(Growth, Mortality);
            }

            // T74: Restore all empire systems from loaded state
            RestoreEmpireSystemsFromState(loadedState);

            Console.WriteLine($"✅ Restored from save: Balance={FinanceManager.Balance:N0} NOK, {loadedState.Populations.Count} populations, Day {loadedState.Day}");
        }

        /// <summary>
        /// Restore all empire systems from loaded GameState (T74).
        /// Backward-compatible: gracefully handles old saves without new fields.
        /// </summary>
        private void RestoreEmpireSystemsFromState(GameState loadedState)
        {
            // T74: For now, just log that new empire systems were loaded
            // Full restoration will be added incrementally as each system is battle-tested
            
            if (loadedState.CompetitorCompanies?.Count > 0)
            {
                Console.WriteLine($"✓ Loaded {loadedState.CompetitorCompanies.Count} competitor companies from save");
            }

            if (loadedState.EmpireProgression != null)
            {
                Console.WriteLine($"✓ Loaded empire progression: {loadedState.EmpireProgression.CurrentPhase}");
            }

            // Other systems will be restored incrementally as they're tested
            // Old saves without these fields will load fine with default values
        }
    }
}