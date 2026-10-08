using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using LakskongenSim.Biology;
using LakskongenSim.Calibration;
using LakskongenSim.Data;
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
    public partial class SimulationEngine
    {
        public TimeManager Time { get; } = new();
        public GameState State { get; private set; } = new();

        /// <summary>Named deterministic RNG streams (Phase 1.4). Re-seeded in
        /// Initialize from the game seed; replaced on Reset.</summary>
        public SimRng Rng { get; private set; } = SimRng.CreateUnseeded();

        /// <summary>Drop all run state so a new game never inherits zones,
        /// sites, fish, event log or interval clocks from the previous one
        /// (S9). Called at the top of Initialize and directly by New Game.</summary>
        public void Reset()
        {
            Storyteller?.Detach();
            Storyteller = null!;
            State = new GameState();
            Rng = SimRng.CreateUnseeded();
            _dayNotes.Clear();
            Time.Reset();
            _lastSustainabilityUpdate = 0;
            _lastTrafficLightUpdate = 0;
            _lastBrandReputationUpdate = 0;
            _lastCrisisCapitalismUpdate = 0;
            _lastVerticalIntegrationUpdate = 0;
            _lastCompetitorAIUpdate = 0;
            _lastEmpireProgressionUpdate = 0;
            _lastTaxUpdate = 0;
            _lastInvestorUpdate = 0;
            _lastMarketIntelligenceUpdate = 0;
            _lastContractUpdate = 0;
            _lastBloomUpdate = 0;
            _lastNightPolicyUpdate = 0;
            _lastProductMixOptimizerUpdate = 0;
        }

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

        // ─── Phase 3 depth systems (P5/P6/P7/P9/P10) ───
        /// <summary>P9: binding monthly pen price set by the Price Council.</summary>
        public Market.PriceCouncil PenPriceCouncil { get; private set; } = new(82m);
        /// <summary>P9: when true, the 25% resource-rent tax is charged (opt-in per scenario).</summary>
        public bool EnableResourceRentTax { get; set; } = false;
        /// <summary>P9: cumulative resource-rent tax paid. Settable so save/load
        /// restore can rehydrate the running total exactly (additive).</summary>
        public decimal ResourceRentPaid { get; set; } = 0m;
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
        public ScenarioSystem Scenario { get; private set; } = null!;
        public ResearchSystem Research { get; private set; } = null!;
        public Finance.StockMarketSystem StockMarket { get; private set; } = null!;
        public BrandReputation BrandReputation { get; private set; } = null!;
        public CrisisCapitalismSystem CrisisCapitalism { get; private set; } = null!;
        public VerticalIntegrationSystem VerticalIntegration { get; private set; } = null!;
        public Finance.LoanSystem LoanSystem { get; private set; } = null!;
        public Finance.TaxSystem TaxSystem { get; private set; } = null!;
        public Finance.InvestorSystem InvestorSystem { get; private set; } = null!;
        /// <summary>P11: catastrophe insurance policies (Mortality/Bloom/Storm/ISA).</summary>
        public Finance.InsuranceSystem Insurance { get; private set; } = new();
        public Expansion.AcquisitionSystem AcquisitionSystem { get; private set; } = null!;
        public Finance.LicenseSystem LicenseSystem { get; private set; } = null!;
        public Finance.TrafficLightSystem TrafficLightSystem { get; private set; } = null!;
        public Politics.StakeholderFactionSystem StakeholderFactionSystem { get; private set; } = null!;
        public Market.MarketIntelligenceSystem MarketIntelligence { get; private set; } = null!;
        public Health.FishWelfare FishWelfare { get; private set; } = null!;
        public VisualMilestones VisualMilestones { get; private set; } = null!;
        public Market.CurrencySystem CurrencySystem { get; private set; } = null!;
        public PlanktonBloom? PlanktonBloom { get; private set; }

        // ─── Farm Subsystems ───
        public List<RASFacility> RASFacilities { get; private set; } = new();
        public List<OffshoreFarm> OffshoreFarms { get; private set; } = new();
        public AIFeedingSystem AIFeeding { get; private set; } = null!;
        public List<PostSmoltFacility> PostSmoltFacilities { get; private set; } = new();

        // ─── Update intervals ───
        private int _lastSustainabilityUpdate = 0;
        private const int SUSTAINABILITY_UPDATE_INTERVAL = 7; // Weekly
        private int _lastTrafficLightUpdate = 0;
        private const int TRAFFIC_LIGHT_UPDATE_INTERVAL = 7; // Weekly (Phase 1.3)
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
        private int _lastContractUpdate = 0; // Phase 2.3 (same weekly cadence, own tracker)
        private int _lastBloomUpdate = 0;
        private const int BLOOM_UPDATE_INTERVAL = 7; // Weekly (Phase 4.3)
        private const int BARRIER_CLEANING_TICK_DAYS = 7; // P8: barrier cleaning decay cadence (weekly)
        private int _lastNightPolicyUpdate = 0;
        private const int NIGHT_POLICY_INTERVAL = 1; // Daily (Phase 5.2)
        private int _lastProductMixOptimizerUpdate = 0;
        private const int PRODUCT_MIX_OPTIMIZER_UPDATE_INTERVAL = 30; // Monthly - production planning is periodic

        private BalancingConfig _balancing = new();
        // Phase A: single source of truth for balancing values.
        private CalibrationRegistry _calibration = CalibrationRegistry.Default;
        private bool _enableEvents = true;
        private Finance.FinanceManager? _financeManager;

        // ─── Farm systems daily OPEX (accumulated in TickDay, consumed in ProcessDailyFinance) ───
        private decimal _dailyFarmSystemsOpex = 0m;
        private decimal _dailyAiFeedingLicenseCost = 0m;

        // ─── Per-day biology call tuning (values unchanged; see GrowthModel docs) ───
        private const double SdeTargetWeightKg = 5.5; // genetic asymptote K used by the daily SDE step
        private const double SdeGeneticGrowthRateAlphaMax = 20.0; // α_max scale for SDE-only growth
        private const double SdeIndividualVarianceSigma = 0.002; // σ kept small vs deterministic drift
        private const double PenVolumeProxyM3 = 10_000.0; // pen-volume proxy for density estimate
        private const double DefaultStockingDensityKgM3 = 20.0; // fallback when no zone is known
        private const double WarmingPerYearC = 0.02; // P10: documented warming trend applied to lice development

        // ─── Sea-lice seeding (realistic external copepodid pressure) ───
        // At sea stocking each cycle is seeded with external lice pressure; the
        // daily external multiplier is seasonally modulated so a clean grow-out
        // reaches ~0.5 adult females/fish (the Mattilsynet action limit) without
        // any treatment. Deterministic (no RNG).
        private const double StockingLiceSeedIntensity = 2.0;
        private const double SeasonalLicePressureBase = 1.0;
        private const double SeasonalLicePressureAmplitude = 0.35;

        // ─── Sea-site storm containment hazards (engine-owned, deterministic) ───
        // Net integrity wears on storm days and repairs in calm weather; a severe
        // storm can tear a weakened net and release fish. This is the single
        // source of truth for storm escapes (GameBridge.CheckWeatherImpacts no
        // longer mutates NetIntegrityPct / records escapes). Deterministic for a
        // given seed because storm timing comes from the seeded WeatherSystem.
        // Real mapping: Norwegian sea-site escapes are rare and far below 1% of
        // stock per site per year; see docs/BALANCE.md §1c and the 0.5% guard.
        private const double StormIntegrityLossPctPerDay = 0.3;
        private const double SevereIntegrityLossPctPerDay = 1.0;
        private const double NetIntegrityRepairPctPerDay = 0.2;
        /// <summary>Expected containment-failure probability on a severe-storm day.</summary>
        private const double StormFailureProbabilityPerSevereDay = 0.01;
        /// <summary>Fraction of the pen released by one containment failure at full wear.</summary>
        private const double StormLossFractionPerFailure = 0.05;
        /// <summary>Documented 40% net-integrity risk line (hard breach).</summary>
        private const double NetIntegrityEscapeRiskThresholdPct = 40.0;
        /// <summary>Regulatory escape fine (NOK per escaped fish).</summary>
        private const decimal EscapeFineNOKPerFish = 100m;

        /// <summary>
        /// Cumulative expected storm containment-failure events recorded by the
        /// engine (all sites). Expected (not Bernoulli) so escape accounting is
        /// smooth and reproducible across seeds. Exposed for SimProbe.
        /// </summary>
        public double StormEscapeEvents { get; private set; } = 0.0;

        // ─── External data calibration ───
        private AquacultureDataConfig _aquacultureDataConfig = new();
        private AquacultureGameplayModifiers _aquacultureModifiers = AquacultureGameplayModifiers.Default();
        private BarentsWatchClientOptions _barentsWatchClientOptions = new();
        private BarentsWatchFishHealthPipeline? _barentsWatchPipeline;
        private Random _rng = new(); // For event registration randomization (seeded in Initialize)

        // ─── Events ───
        public event Action<int>? OnDayAdvanced;
        public event Action<ResearchNode>? OnResearchCompleted;
        public event Action<string>? OnEventTriggered;
        public event Action<EventRecord>? OnEventFired;
        public event Action<Achievement>? OnAchievementUnlocked;

        /// <summary>Fired when the active scenario's win condition is met.</summary>
        public event Action<ScenarioDefinition>? OnScenarioWon;

        /// <summary>Fired when the active scenario is lost. Second argument is the reason.</summary>
        public event Action<ScenarioDefinition, string>? OnScenarioLost;

        /// <summary>
        /// Initialize all subsystems and wire them together.
        /// </summary>
        /// <param name="enableEvents">If false, disables random storyteller events and disease mortality (useful for deterministic tests).</param>
        /// <param name="seed">Optional RNG seed. When set, growth/market/event randomness is reproducible (tests).</param>
        public void Initialize(bool enableEvents = true, int? seed = null)
        {
            // Fresh run state first: State/Storyteller/Time outlive the
            // engine instance, so without this a second New Game inherits
            // the previous game (S9).
            Reset();
            _enableEvents = enableEvents;
            _rng = seed.HasValue ? new Random(seed.Value + 101) : new Random();
            Rng = seed.HasValue ? new SimRng(seed.Value) : SimRng.CreateUnseeded();
            
            // Create subsystems
            Weather = seed.HasValue ? new WeatherSystem(seed.Value) : new WeatherSystem();
            Environment = new EnvironmentModel();
            Growth = seed.HasValue ? new GrowthModel(seed.Value) : new GrowthModel();
            Mortality = new MortalityModel();
            Feed = new FeedSystem();
            SeaLice = new SeaLiceModel();
            Disease = new DiseaseSystem(SeaLice);
            IntegratedDisease = new IntegratedDiseaseSystem(SeaLice);
            
            // Wire disease systems to MortalityModel to prevent double mortality application
            Disease.SetMortalityModel(Mortality);
            IntegratedDisease.SetMortalityModel(Mortality);
            
            Hatchery = new Hatchery();
            Genetics = seed.HasValue ? new Genetics(seed.Value) : new Genetics();

            // Default finance and market if not set externally
            FinanceManager ??= new Finance.FinanceManager(10_000_000m); // 10M NOK starting capital
            FinancialStatements = new Finance.FinancialStatements();

            // Feed PRICE authority: FeedSystem.FeedCostPerKg is charged daily via
            // OnFeedCostIncurred. BalancingConfig mirrors it for estimates
            // (AI savings); ApplyBalancingConfig pushes file values back in.
            _balancing.FeedCostPerKgNOK = Feed.FeedCostPerKg;

// NOTE: GameState.Balance is a read-only MIRROR, synced from
            // FinanceManager.Balance at the end of each simulated day (Phase 6).
            // FinanceManager.Balance is the SINGLE SOURCE OF TRUTH for cash.
            _financeManager = FinanceManager;
             
            // Initialize CompetitorAI and EmpireProgression systems
            // (S10: every seeded subsystem gets the seed — previously most
            // ran on unseeded System.Random even in seeded games/tests.)
            CompetitorAI = new Finance.CompetitorAISystem(seed);
            EmpireProgression = new Finance.EmpireProgressionSystem();
            // Traffic-light regulatory state machine. Constructed HERE (not
            // with the expansion systems below) because NorwegianEvents
            // .RegisterAll, called further down, needs the live instance (S9).
            TrafficLightSystem = new Finance.TrafficLightSystem();
            
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
            
            MarketSimulation ??= new Market.MarketSimulation(seed);
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
            Storyteller = new Storyteller(Time, State, FinanceManager, seed: seed, enableDefaultEvents: enableEvents);
            if (enableEvents)
            {
                NorwegianEvents.RegisterAll(Storyteller, FinanceManager, TrafficLightSystem);
                RegisterPlanktonEvents();
            }
            Storyteller.OnEventRaised += HandleStorytellerEvent;

            // Initialize PlanktonBloom system
            PlanktonBloom = seed.HasValue ? new PlanktonBloom(seed.Value) : new PlanktonBloom();
            LoadPlanktonBloomConfig();

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

                // Sea-phase stocking: seed realistic external lice pressure so a
                // clean (no-treatment) grow-out reaches the regulatory action
                // limit (~0.5 adult females/fish) rather than staying at zero.
                // Each cycle starts from a fallow low point, like a real pen.
                if (cohort.Stage == LifeStage.Smolt)
                {
                    SeaLice.Reset(0.0);
                    SeaLice.SetExternalPressure(SeasonalLicePressure(State.Day));
                    SeaLice.SeedExternalInfection(StockingLiceSeedIntensity);
                }
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
            Achievements = new AchievementSystem(State, FinanceManager);
            Achievements.OnAchievementUnlocked += a => OnAchievementUnlocked?.Invoke(a);

            // Research / Tech Tree — restore persisted state from GameState
            Research = new ResearchSystem();

            // StockMarket and BrandReputation systems
            StockMarket = new Finance.StockMarketSystem(seed);
            BrandReputation = new BrandReputation
            {
                BrandName = "Lakskongen",
                Strategy = BrandStrategy.Premium
            };

            // CrisisCapitalism and VerticalIntegration systems
            CrisisCapitalism = new CrisisCapitalismSystem(seed);
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

            // P11: catastrophe insurance (additive; policies are player-subscribed
            // via GameBridge.SetInsurancePolicy). Always present so payouts work.
            Insurance = new Finance.InsuranceSystem();

            // Initialize new farm systems
            RASFacilities = new();
            OffshoreFarms = new();
            AIFeeding = new AIFeedingSystem(Rng.Stream(SimRngStreams.AiFeeding));
            PostSmoltFacilities = new();

            // Initialize expansion, license, and politics systems
            AcquisitionSystem = new Expansion.AcquisitionSystem(seed);
            LicenseSystem = new Finance.LicenseSystem();
            // NOTE: TrafficLightSystem is constructed near the top of
            // Initialize (NorwegianEvents.RegisterAll needs it). Gate stays
            // off by default: legacy event-only MAB behavior preserved until
            // explicitly enabled.
            StakeholderFactionSystem = new Politics.StakeholderFactionSystem(seed);

            // Initialize Market Intelligence, Fish Welfare, and Visual Milestones systems
            MarketIntelligence = seed.HasValue
                ? new Market.MarketIntelligenceSystem(seed.Value)
                : new Market.MarketIntelligenceSystem();
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

            // Scenario / campaign system — built-in Norwegian roster plus any
            // additional definitions loaded from scenarios.json (see LoadScenarios).
            Scenario = new ScenarioSystem(State, FinanceManager);
            Scenario.OnScenarioWon += scenario => OnScenarioWon?.Invoke(scenario);
            Scenario.OnScenarioLost += (scenario, reason) => OnScenarioLost?.Invoke(scenario, reason);

            LoadJsonConfigs();
            LoadAquacultureDataConfig();
            InitializeBarentsWatchPipeline();
            LoadExternalAquacultureSnapshot();
            ApplyExternalAquacultureCalibration();
            SimLog.Info("✅ SimulationEngine initialized — all subsystems wired");
        }

        private void LoadJsonConfigs()
        {
            string? dataPath = ResolveDataDirectory("calibration.json");
            if (dataPath == null)
            {
                SimLog.Warn("⚠️  Data directory not found. Using defaults for all configs.");
                // Calibration still loads from the embedded resource.
                LoadBalancingConfig(null);
                return;
            }

            SimLog.Info($"📁 Loading configs from: {Path.GetFullPath(dataPath)}");

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

            SimLog.Info("✅ JSON config loading complete");
        }

        /// <summary>
        /// Resolve the canonical live data dir (godot/data) by probing for a
        /// marker file. Shared by TutorialSystem and AchievementSystem, which
        /// load independently of LoadJsonConfigs. Legacy game/data last
        /// (removed 2026-10-05, see archive/README.md).
        /// </summary>
        internal static string? ResolveDataDirectory(string probeFile)
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates = {
                Path.Combine("godot", "data"),
                "data",
                Path.Combine(baseDir, "..", "..", "..", "data"),
                Path.Combine(baseDir, "..", "..", "..", "..", "..", "godot", "data"),
                // Exported macOS bundle: <app>/Contents/Resources/data (copied post-export)
                Path.Combine(baseDir, "..", "Resources", "data"),
                Path.Combine(baseDir, "..", "..", "..", "..", "..", "game", "data"),
                Path.Combine("game", "data"),
                "Data"
            };
            return candidates.FirstOrDefault(c => File.Exists(Path.Combine(c, probeFile)));
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
                    SimLog.Warn("⚠️  AquacultureDataConfig.json not found, using external data defaults.");
                    _aquacultureDataConfig = new AquacultureDataConfig();
                    return;
                }

                var json = File.ReadAllText(configPath);
                _aquacultureDataConfig = JsonSerializer.Deserialize<AquacultureDataConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new AquacultureDataConfig();

                SimLog.Info($"✓ Loaded aquaculture data config from {Path.GetFullPath(configPath)}");
            }
            catch (Exception ex)
            {
                SimLog.Warn($"⚠️  Failed to load AquacultureDataConfig.json: {ex.Message}");
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
                        SimLog.Info(string.Format(
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
                    SimLog.Warn("⚠️  aquaculture_snapshot.json not found, using bundled market/sustainability defaults only.");
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
                    SimLog.Info(string.Format(
                        CultureInfo.InvariantCulture,
                        "✓ Loaded aquaculture snapshot: biomass={0:F2}, lice={1:F2}, risk={2:F2}",
                        State.ExternalAquacultureSnapshot.NationalBiomassUtilization,
                        State.ExternalAquacultureSnapshot.IndustryLicePressure,
                        State.ExternalAquacultureSnapshot.ProductionAreaRiskIndex));
                }
            }
            catch (Exception ex)
            {
                SimLog.Warn($"⚠️  Failed to load aquaculture snapshot: {ex.Message}");
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

            SeaLice?.SetExternalPressure(_aquacultureModifiers.ZoneRiskMultiplier * crisis.DelousingCostMultiplier / Math.Max(1.0, crisis.GrowthPenaltyMultiplier));
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
                        SimLog.Info($"✓ BarentsWatch Fishhealth pipeline ingested snapshot {stored.Normalized.SnapshotYear}-W{stored.Normalized.SnapshotWeek:D2}");
                    }
                    catch (Exception ex)
                    {
                        SimLog.Warn($"⚠️  BarentsWatch ingest failed, continuing with persisted/default snapshot: {ex.Message}");
                    }
                }
                else
                {
                    SimLog.Warn("⚠️  BarentsWatch token/local raw snapshot missing; pipeline initialized in passive mode.");
                }
            }
            catch (Exception ex)
            {
                SimLog.Warn($"⚠️  Failed to initialize BarentsWatch pipeline: {ex.Message}");
            }
        }

        private void LoadBalancingConfig(string? dataPath)
        {
            // Phase A — Calibration Spine.
            // CalibrationRegistry is the single source of truth. The legacy
            // balancing.json is superseded on purpose so calibrated values
            // (feed cost, base FCR, spot price, MAB, resource rent) cannot drift
            // away from docs/REALISM_CHECKLIST.md. BalancingConfig is retained
            // (private, same property names) purely as the engine's local view.
            _calibration = CalibrationRegistry.Load(dataPath);
            var config = BalancingConfig.FromRegistry(_calibration);
            _balancing = config;
            ApplyBalancingConfig(config);
            SimLog.Info(
                $"✓ Loaded calibration v{_calibration.Version} " +
                $"(feed cost ≈ {_calibration.FeedCostPerKgProducedNOK:N2} NOK/kg produced)");
        }

        private void LoadPlanktonBloomConfig()
        {
            var candidatePaths = new[]
            {
                Path.Combine("Data", "EnvironmentalEvents.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "EnvironmentalEvents.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Data", "EnvironmentalEvents.json")
            };

            var configPath = candidatePaths.FirstOrDefault(File.Exists);
            if (configPath == null)
            {
                SimLog.Warn("⚠️  EnvironmentalEvents.json not found, using default plankton bloom config.");
                PlanktonBloom?.LoadConfig(new PlanktonBloom.PlanktonBloomConfig());
                return;
            }

            try
            {
                var json = File.ReadAllText(configPath);
                var envConfig = JsonSerializer.Deserialize<EnvironmentalEventsConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (envConfig?.PlanktonBloom != null)
                {
                    PlanktonBloom?.LoadConfig(envConfig.PlanktonBloom);
                    SimLog.Info($"✓ Loaded plankton bloom config from {Path.GetFullPath(configPath)}");
                }
                else
                {
                    PlanktonBloom?.LoadConfig(new PlanktonBloom.PlanktonBloomConfig());
                }
            }
            catch (Exception ex)
            {
                SimLog.Warn($"⚠️  Failed to load EnvironmentalEvents.json: {ex.Message}");
                PlanktonBloom?.LoadConfig(new PlanktonBloom.PlanktonBloomConfig());
            }
        }

        private void RegisterPlanktonEvents()
        {
            // Spring Plankton Bloom
            Storyteller.RegisterEvent(new EventDefinition
            {
                Id = "plankton-bloom-spring",
                Name = "Spring Plankton Bloom",
                Category = EventCategory.PlanktonBloom,
                Weight = 1.5,
                CooldownDays = 60,
                MinDay = 60,
                MaxDay = 150,
                Condition = gs => gs.Zones != null && gs.Zones.Any(z => z.CurrentFishBiomassKg > 0) && gs.Day >= 60 && gs.Day <= 150,
                Apply = (gs, rec) =>
                {
                    rec.Description = "Spring phytoplankton bloom fueled by increasing light and nutrient upwelling. Risk of oxygen depletion if bloom crashes.";
                    rec.BalanceChange = -50000m;
                    NorwegianEvents.SettleBalanceChange(_financeManager, gs, rec);
                    
                    // Trigger bloom in a random zone with fish
                    var zonesWithFish = gs.Zones.Where(z => z.CurrentFishBiomassKg > 0).ToList();
                    if (zonesWithFish.Any() && PlanktonBloom != null)
                    {
                        var zone = zonesWithFish[_rng.Next(zonesWithFish.Count)];
                        // Force bloom initiation by boosting intensity
                        var bloomState = PlanktonBloom.GetAllBloomStates().GetValueOrDefault(zone.Id, new PlanktonBloom.BloomState());
                        bloomState.Intensity = 0.4; // Above bloom threshold
                        bloomState.IsActive = true;
                        bloomState.StartDay = gs.Day;
                        // We can't directly set internal state, but the daily tick will pick up the conditions
                    }
                }
            });

            // Autumn Plankton Bloom
            Storyteller.RegisterEvent(new EventDefinition
            {
                Id = "plankton-bloom-autumn",
                Name = "Autumn Plankton Bloom",
                Category = EventCategory.PlanktonBloom,
                Weight = 1.2,
                CooldownDays = 90,
                MinDay = 240,
                MaxDay = 330,
                Condition = gs => gs.Zones != null && gs.Zones.Any(z => z.CurrentFishBiomassKg > 0) && gs.Day >= 240 && gs.Day <= 330,
                Apply = (gs, rec) =>
                {
                    rec.Description = "Autumn bloom driven by nutrient remineralization and cooling waters. Higher crash risk due to shorter days reducing photosynthesis.";
                    rec.BalanceChange = -75000m;
                    NorwegianEvents.SettleBalanceChange(_financeManager, gs, rec);
                }
            });

            // Plankton Crash (DO Crash)
            Storyteller.RegisterEvent(new EventDefinition
            {
                Id = "plankton-crash",
                Name = "Plankton Bloom Crash",
                Category = EventCategory.PlanktonCrash,
                Weight = 0.8,
                CooldownDays = 30,
                MinDay = 1,
                MaxDay = 365,
                Condition = gs => gs.Zones != null && gs.Zones.Any(z => 
                    z.WaterChemistry != null && z.WaterChemistry.OxygenO2 < 3.0), // DO below critical
                Apply = (gs, rec) =>
                {
                    rec.Description = "Massive phytoplankton die-off triggers bacterial respiration spike. Dissolved oxygen crashes below 30% saturation causing acute fish mortality.";
                    rec.BalanceChange = -200000m;
                    NorwegianEvents.SettleBalanceChange(_financeManager, gs, rec);
                    
                    // Apply acute mortality to populations in affected zones
                    ApplyPlanktonCrashMortality(gs);
                }
            });
        }

        private void ApplyPlanktonCrashMortality(GameState gs)
        {
            if (gs.Populations == null || gs.Zones == null) return;
            
            var affectedZones = gs.Zones.Where(z => z.WaterChemistry != null && z.WaterChemistry.OxygenO2 < 3.0).ToList();
            if (!affectedZones.Any()) return;

            foreach (var zone in affectedZones)
            {
                var populationsInZone = gs.Populations.Where(p => p.ZoneId == zone.Id).ToList();
                foreach (var pop in populationsInZone)
                {
                    // Mortality rate based on DO severity
                    double doLevel = zone.WaterChemistry.OxygenO2;
                    double mortalityRate = doLevel < 1.0 ? 0.5 : (doLevel < 2.0 ? 0.25 : 0.1);
                    
                    for (int ci = 0; ci < pop.Cohorts.Count; ci++)
                    {
                        var cohort = pop.Cohorts[ci];
                        int deaths = (int)(cohort.Count * mortalityRate);
                        cohort.Count -= deaths;
                        if (cohort.Count < 0) cohort.Count = 0;
                    }
                    
                    SimLog.Info($"Plankton crash mortality in zone {zone.Id}: DO={doLevel:F2} mg/L, rate={mortalityRate:P0}");
                }
            }
        }

        private void LoadResearchNodes(string dataPath)
        {
            var filePath = Path.Combine(dataPath, "research_nodes.json");
            if (!File.Exists(filePath))
            {
                SimLog.Warn("⚠️  research_nodes.json not found, using built-in research tree.");
                return;
            }

            try
            {
                var json = File.ReadAllText(filePath);
                var nodes = JsonSerializer.Deserialize<List<ResearchNode>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    Converters = { new JsonStringEnumConverter() }
                });

                if (nodes != null && nodes.Count > 0)
                {
                    Research.PopulateFromJson(nodes);
                    SimLog.Info($"✓ Loaded {nodes.Count} research nodes from JSON");
                }
            }
            catch (Exception ex)
            {
                SimLog.Warn($"⚠️  Failed to load research_nodes.json: {ex.Message}");
            }
        }

        private void LoadTutorialSteps(string dataPath)
        {
            var filePath = Path.Combine(dataPath, "tutorial_steps.json");
            if (!File.Exists(filePath))
            {
                SimLog.Warn("⚠️  tutorial_steps.json not found, using built-in tutorial.");
                return;
            }

            try
            {
                var json = File.ReadAllText(filePath);
                // Tutorial steps in JSON don't include CompletionCondition delegates,
                // so we log the existence but don't override the hardcoded tutorial
                SimLog.Info($"✓ Found tutorial_steps.json (tutorial uses built-in conditions)");
            }
            catch (Exception ex)
            {
                SimLog.Warn($"⚠️  Failed to read tutorial_steps.json: {ex.Message}");
            }
        }

        private void LoadScenarios(string dataPath)
        {
            var filePath = Path.Combine(dataPath, "scenarios.json");
            if (!File.Exists(filePath))
            {
                SimLog.Warn("⚠️  scenarios.json not found, scenarios unavailable.");
                return;
            }

            try
            {
                var json = File.ReadAllText(filePath);
                var scenarios = JsonSerializer.Deserialize<List<ScenarioDefinition>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    Converters = { new JsonStringEnumConverter() }
                });

                if (scenarios != null && scenarios.Count > 0)
                {
                    SimLog.Info($"✓ Loaded {scenarios.Count} scenario definitions");
                    // Merge into the ScenarioSystem additively: the built-in roster is
                    // authoritative, file entries only add ids that are not already
                    // registered (mod/community scenarios), never overwrite built-ins.
                    if (Scenario != null)
                    {
                        var existingIds = new HashSet<string>(Scenario.All.Keys);
                        var extras = scenarios.Where(s => !existingIds.Contains(s.Id)).ToList();
                        if (extras.Count > 0)
                        {
                            Scenario.RegisterScenarios(extras);
                            SimLog.Info($"✓ Registered {extras.Count} additional scenario(s) from JSON");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SimLog.Warn($"⚠️  Failed to load scenarios.json: {ex.Message}");
            }
        }

        private void LoadAchievementsMetadata(string dataPath)
        {
            var filePath = Path.Combine(dataPath, "achievements_config.json");
            if (!File.Exists(filePath))
            {
                SimLog.Warn("⚠️  achievements_config.json not found, using built-in achievements.");
                return;
            }

            try
            {
                var json = File.ReadAllText(filePath);
                // Achievement conditions are hardcoded in AchievementSystem.RegisterAllAchievements()
                // This JSON only provides UI metadata (icons, categories)
                SimLog.Info($"✓ Found achievements_config.json (achievements use built-in conditions)");
            }
            catch (Exception ex)
            {
                SimLog.Warn($"⚠️  Failed to read achievements_config.json: {ex.Message}");
            }
        }

        private void LoadEncyclopediaEntries(string dataPath)
        {
            var filePath = Path.Combine(dataPath, "encyclopedia_entries.json");
            if (!File.Exists(filePath))
            {
                SimLog.Warn("⚠️  encyclopedia_entries.json not found.");
                return;
            }

            try
            {
                var json = File.ReadAllText(filePath);
                var entries = JsonSerializer.Deserialize<List<EncyclopediaEntry>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (entries != null && entries.Count > 0)
                {
                    SimLog.Info($"✓ Loaded {entries.Count} encyclopedia entries");
                    // Encyclopedia system integration would go here when implemented
                }
            }
            catch (Exception ex)
            {
                SimLog.Warn($"⚠️  Failed to load encyclopedia_entries.json: {ex.Message}");
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
                SimLog.Info($"  → Starting capital: {config.StartingCapital:N0} NOK");
            }

            // Feed system parameters — applied, not just logged:
            // calibration registry is the override authority.
            if (Feed != null)
            {
                Feed.FeedCostPerKg = config.FeedCostPerKgNOK;
                Feed.SetBaseFCR(config.BaseFCR);
            }
            SimLog.Info($"  → Feed cost per kg feed: {config.FeedCostPerKgNOK:N2} NOK");
            SimLog.Info($"  → Base FCR: {config.BaseFCR:F2} (optimal={config.OptimalFCR}, poor={config.PoorFCR})");

            // Sea mortality anchor — the whole-cycle registry value is mapped to a
            // daily hazard over the calibrated grow-out duration so the realized
            // sea-phase mortality tracks the NVI 14-16% band (v1.0.2). v1.0.3 splits
            // the anchor into a husbandry baseline plus an explicit disease slice
            // when events are enabled; with events off the full anchor stays in the
            // baseline (deterministic no-disease runs).
            if (Mortality != null)
            {
                Mortality.ConfigureSeaMortality(
                    config.SeaMortalityPerCycle,
                    config.GrowthToHarvestDays,
                    config.SeaMortalityDiseaseShare,
                    _enableEvents);
                SimLog.Info(
                    $"  → Sea mortality: {config.SeaMortalityPerCycle:P1}/cycle → " +
                    $"{Mortality.SeaDailyMortalityRate:P4}/day over {config.GrowthToHarvestDays}d " +
                    $"(disease share {config.SeaMortalityDiseaseShare:P0}, events={_enableEvents})");
            }

            // Disease slice — size the SIR so its endemic daily hazard integrates
            // to SeaMortalityPerCycle × SeaMortalityDiseaseShare over the grow-out.
            // Only when events are enabled; the deterministic path never injects.
            if (_enableEvents && IntegratedDisease != null)
            {
                double diseasePerCycle = config.SeaMortalityPerCycle * config.SeaMortalityDiseaseShare;
                IntegratedDisease.ConfigureDiseaseShare(diseasePerCycle, config.GrowthToHarvestDays);
                SimLog.Info($"  → Disease slice: {diseasePerCycle:P1}/cycle (explicit SIR injection)");
            }

            // Market base price
            SimLog.Info($"  → Base salmon price: {config.BaseSalmonPricePerKg:N0} NOK/kg");

            // Growth parameters
            SimLog.Info($"  → Smolt maturation: {config.SmoltMaturationDays} days");
            SimLog.Info($"  → Grow-out period: {config.GrowthToHarvestDays} days");
            SimLog.Info($"  → Target harvest weight: {config.TargetHarvestWeightKg} kg");

            // Water Quality parameters (DO & Stocking Density)
            SimLog.Info($"  → DO saturation threshold: {config.CriticalDOSaturationThreshold:P0}");
            SimLog.Info($"  → DO saturation min multiplier: {config.DOSaturationMinMultiplier:F1}");
            SimLog.Info($"  → Stocking density threshold: {config.StockingDensityThresholdKgM3} kg/m³");
            SimLog.Info($"  → Stocking density min multiplier: {config.StockingDensityMinMultiplier:F1}");

            // Apply water quality configuration to the static WaterQuality class
            var wqConfig = new LakskongenSim.Biology.BalancingConfig
            {
                CriticalDOSaturationThreshold = config.CriticalDOSaturationThreshold,
                DOSaturationDecayCoefficient = config.DOSaturationDecayCoefficient,
                DOSaturationMinMultiplier = config.DOSaturationMinMultiplier,
                DOSaturationMaxMultiplier = config.DOSaturationMaxMultiplier,
                StockingDensityThresholdKgM3 = config.StockingDensityThresholdKgM3,
                StockingDensityReductionDenominator = config.StockingDensityReductionDenominator,
                StockingDensityMinMultiplier = config.StockingDensityMinMultiplier,
                OxygenConsumptionPerKgFishPerDay = config.OxygenConsumptionPerKgFishPerDay,
                OxygenConsumptionPerKgFeed = config.OxygenConsumptionPerKgFeed,
                WaterExchangeCoefficient = config.WaterExchangeCoefficient
            };
            LakskongenSim.Biology.WaterQuality.ApplyConfig(wqConfig);

            // NOTE: Full subsystem integration requires adding setter methods to:
            // - Feed.SetCostPerKg(), Feed.SetFCRTargets()
            // - Market.SetBasePrice(), Market.SetVolatility()
            // - Hatchery.SetCostAndMaturation()
            // - Growth.SetTargetWeight()
            // For now, this logs the values; subsystems use their own defaults.
        }

private class BalancingConfig
        {
            /// <summary>
            /// Project the immutable <see cref="CalibrationRegistry"/> into the engine's
            /// local balancing view. This is the ONLY place engine defaults are sourced,
            /// keeping code from drifting from the calibrated single source of truth.
            /// </summary>
            public static BalancingConfig FromRegistry(CalibrationRegistry c) => new()
            {
                // Economy
                StartingCapital = c.Economy.StartingCapitalNOK,
                SmoltCostPerFish = c.Economy.SmoltCostPerFishNOK,
                FeedCostPerKgNOK = c.Feed.FeedPricePerKgFeedNOK,
                BaseSalmonPricePerKg = c.Economy.BaseSalmonPricePerKgNOK,
                PriceVolatilityPercent = c.Economy.PriceVolatilityPercent,
                SeasonalPriceSwingPercent = c.Economy.SeasonalPriceSwingPercent,

                // Labor
                WorkerMonthlySalaryNOK = c.Labor.WorkerMonthlySalaryNOK,
                ManagerMonthlySalaryNOK = c.Labor.ManagerMonthlySalaryNOK,
                WorkersPerSite = c.Labor.WorkersPerSite,
                WorkersPerHatchery = c.Labor.WorkersPerHatchery,
                WorkersPerProcessingPlant = c.Labor.WorkersPerProcessingPlant,

                // Infrastructure
                SeaSiteBuildCostNOK = c.Infrastructure.SeaSiteBuildCostNOK,
                HatcheryBuildCostNOK = c.Infrastructure.HatcheryBuildCostNOK,
                ProcessingPlantCostNOK = c.Infrastructure.ProcessingPlantCostNOK,
                FeedMillCostNOK = c.Infrastructure.FeedMillCostNOK,
                MaxBiomassPerSiteKg = c.Infrastructure.MaxBiomassPerSiteKg,
                MaxSitesPerZone = c.Infrastructure.MaxSitesPerZone,
                MABLimitKgPerLicense = c.Licensing.MABLimitKgPerLicense,

                // Operations
                HarvestCostPerKgNOK = c.Operations.HarvestCostPerKgNOK,
                ProcessingCostPerKgNOK = c.Operations.ProcessingCostPerKgNOK,
                TransportCostPerKgPerKmNOK = c.Operations.TransportCostPerKgPerKmNOK,

                // Health & treatment
                DelousingCostMechanicalNOK = c.Health.DelousingCostMechanicalNOK,
                DelousingCostChemicalNOK = c.Health.DelousingCostChemicalNOK,
                DelousingCostCleanerfishNOK = c.Health.DelousingCostCleanerfishNOK,
                VaccinationCostPerKgBiomass = c.Health.VaccinationCostPerKgBiomassNOK,
                MortalityRateBasePercent = c.Health.MortalityRateBasePercent,
                MortalityRateISAPercent = c.Health.MortalityRateISAPercent,
                SeaMortalityPerCycle = c.Health.SeaMortalityPerCycle,
                SeaMortalityDiseaseShare = c.Health.SeaMortalityDiseaseShare,
                SeaLiceThresholdPerFish = c.Health.SeaLiceThresholdPerFish,
                SeaLiceTreatmentThreshold = c.Health.SeaLiceTreatmentThreshold,

                // Finance
                InsurancePremiumPercent = c.Finance.InsurancePremiumPercent,
                LoanInterestRate = c.Finance.LoanInterestRate,
                MaxLoanMultiplier = c.Finance.MaxLoanMultiplier,
                CorporateTaxRate = c.Finance.CorporateTaxRate,
                ResourceRentTaxRate = c.Finance.ResourceRentTaxRate,

                // Licensing
                LicenseCostNOK = c.Licensing.LicenseCostNOK,

                // Biology
                SmoltMaturationDays = c.Biology.SmoltMaturationDays,
                GrowthToHarvestDays = c.Biology.GrowthToHarvestDays,
                TargetHarvestWeightKg = c.Biology.TargetHarvestWeightKg,
                MinHarvestWeightKg = c.Biology.MinHarvestWeightKg,
                MaxHarvestWeightKg = c.Biology.MaxHarvestWeightKg,
                OptimalFCR = c.Biology.OptimalFCR,
                PoorFCR = c.Biology.PoorFCR,
                BaseFCR = c.Feed.BaseFCR,

                // Environment
                OptimalTemperatureMinC = c.Environment.OptimalTemperatureMinC,
                OptimalTemperatureMaxC = c.Environment.OptimalTemperatureMaxC,
                LethalTemperatureMaxC = c.Environment.LethalTemperatureMaxC,
                FallowPeriodDays = c.Environment.FallowPeriodDays,

                // Water quality
                CriticalDOSaturationThreshold = c.WaterQuality.CriticalDOSaturationThreshold,
                DOSaturationDecayCoefficient = c.WaterQuality.DOSaturationDecayCoefficient,
                DOSaturationMinMultiplier = c.WaterQuality.DOSaturationMinMultiplier,
                DOSaturationMaxMultiplier = c.WaterQuality.DOSaturationMaxMultiplier,
                StockingDensityThresholdKgM3 = c.WaterQuality.StockingDensityThresholdKgM3,
                StockingDensityReductionDenominator = c.WaterQuality.StockingDensityReductionDenominator,
                StockingDensityMinMultiplier = c.WaterQuality.StockingDensityMinMultiplier,
                OxygenConsumptionPerKgFishPerDay = c.WaterQuality.OxygenConsumptionPerKgFishPerDay,
                OxygenConsumptionPerKgFeed = c.WaterQuality.OxygenConsumptionPerKgFeed,
                WaterExchangeCoefficient = c.WaterQuality.WaterExchangeCoefficient,

                // Compliance & certification
                EscapeFinePer100Fish = c.Compliance.EscapeFinePer100FishNOK,
                ASCCertificationCostNOK = c.Compliance.ASCCertificationCostNOK,
                ASCAuditCostYearlyNOK = c.Compliance.ASCAuditCostYearlyNOK,
                ASCPricePremiumPercent = c.Compliance.ASCPricePremiumPercent,
                OrganicPricePremiumPercent = c.Compliance.OrganicPricePremiumPercent,

                // Research
                ResearchBaseCostNOK = c.Research.ResearchBaseCostNOK,
                ResearchMaxCostNOK = c.Research.ResearchMaxCostNOK,
            };

            // ─── Economy ───
            public decimal StartingCapital { get; set; } = 10_000_000;
            public decimal SmoltCostPerFish { get; set; } = 12; // ↓ from 15 (competitive smolt price)
            public decimal FeedCostPerKgNOK { get; set; } = 22.3m; // mirrors CalibrationRegistry feed price (SSOT); overwritten by FromRegistry()
            public decimal BaseSalmonPricePerKg { get; set; } = 75; // ↑ from 70 (strong market)
            public double PriceVolatilityPercent { get; set; } = 12; // ↓ from 15 (calmer market)
            public double SeasonalPriceSwingPercent { get; set; } = 18; // ↓ from 20

            // ─── Labor ───
            public decimal WorkerMonthlySalaryNOK { get; set; } = 48_000; // ↑ from 45k (2024 wages)
            public decimal ManagerMonthlySalaryNOK { get; set; } = 70_000; // ↑ from 65k
            public int WorkersPerSite { get; set; } = 7; // ↓ from 8 (automation)
            public int WorkersPerHatchery { get; set; } = 10; // ↓ from 12
            public int WorkersPerProcessingPlant { get; set; } = 20; // ↓ from 25

            // ─── Infrastructure ───
            public decimal SeaSiteBuildCostNOK { get; set; } = 6_000_000; // ↑ from 5M (inflation)
            public decimal HatcheryBuildCostNOK { get; set; } = 18_000_000; // ↑ from 15M
            public decimal ProcessingPlantCostNOK { get; set; } = 55_000_000; // ↑ from 50M
            public decimal FeedMillCostNOK { get; set; } = 35_000_000; // ↑ from 30M

            // ─── Production limits ───
            public int MaxBiomassPerSiteKg { get; set; } = 780_000;
            public int MaxSitesPerZone { get; set; } = 4;
            public int MABLimitKgPerLicense { get; set; } = 780_000;

            // ─── Operations ───
            public decimal HarvestCostPerKgNOK { get; set; } = 4.0m; // ↑ from 3.5 (wellboat rates)
            public decimal ProcessingCostPerKgNOK { get; set; } = 9; // ↑ from 8
            public decimal TransportCostPerKgPerKmNOK { get; set; } = 0.025m; // ↑ from 0.02

            // ─── Health & Treatment ───
            public decimal DelousingCostMechanicalNOK { get; set; } = 280_000; // ↑ from 250k
            public decimal DelousingCostChemicalNOK { get; set; } = 180_000; // ↑ from 150k
            public decimal DelousingCostCleanerfishNOK { get; set; } = 120_000; // ↑ from 100k
            public decimal VaccinationCostPerKgBiomass { get; set; } = 0.5m;
            public double MortalityRateBasePercent { get; set; } = 0.025; // ↓ from 0.03 (better husbandry)
            public double MortalityRateISAPercent { get; set; } = 0.22; // ↓ from 0.25
            /// <summary>Whole-cycle sea mortality anchor (fraction); mapped to a daily hazard by MortalityModel.</summary>
            public double SeaMortalityPerCycle { get; set; } = 0.15;
            /// <summary>Disease-attributable share of the sea anchor (0-1); drives the SIR slice when events are on.</summary>
            public double SeaMortalityDiseaseShare { get; set; } = 0.30;
            public double SeaLiceThresholdPerFish { get; set; } = 0.5;
            public double SeaLiceTreatmentThreshold { get; set; } = 0.15; // ↓ from 0.2 (proactive)

            // ─── Finance ───
            public double InsurancePremiumPercent { get; set; } = 0.018; // ↓ from 0.02
            public double LoanInterestRate { get; set; } = 0.055; // ↓ from 0.065 (lower rates)
            public double MaxLoanMultiplier { get; set; } = 3.5; // ↑ from 3.0
            public double CorporateTaxRate { get; set; } = 0.22;
            public double ResourceRentTaxRate { get; set; } = 0.25;

            // ─── Licensing ───
            public decimal LicenseCostNOK { get; set; } = 150_000_000;

            // ─── Biology ───
            public int SmoltMaturationDays { get; set; } = 365;
            public int GrowthToHarvestDays { get; set; } = 540;
            public double TargetHarvestWeightKg { get; set; } = 5.2; // ↑ from 5.0
            public double MinHarvestWeightKg { get; set; } = 3.8; // ↑ from 3.5
            public double MaxHarvestWeightKg { get; set; } = 7.5; // ↑ from 7.0
            public double OptimalFCR { get; set; } = 1.15; // ↓ from 1.15 (industry best)
            public double PoorFCR { get; set; } = 1.35; // ↓ from 1.4
            /// <summary>Base FCR used by FeedSystem. Reconciled to 1.15 (checklist).</summary>
            public double BaseFCR { get; set; } = 1.15;

            // ─── Environment ───
            public double OptimalTemperatureMinC { get; set; } = 8;
            public double OptimalTemperatureMaxC { get; set; } = 14;
            public double LethalTemperatureMaxC { get; set; } = 22; // ↓ from 23
            public int FallowPeriodDays { get; set; } = 60;

            // ─── Water Quality (DO & Stocking Density) ───
            public double CriticalDOSaturationThreshold { get; set; } = 0.60;
            public double DOSaturationDecayCoefficient { get; set; } = 0.3;
            public double DOSaturationMinMultiplier { get; set; } = 0.2;
            public double DOSaturationMaxMultiplier { get; set; } = 1.0;
            public double StockingDensityThresholdKgM3 { get; set; } = 25.0;
            public double StockingDensityReductionDenominator { get; set; } = 75.0;
            public double StockingDensityMinMultiplier { get; set; } = 0.5;
            public double OxygenConsumptionPerKgFishPerDay { get; set; } = 95.0; // ↓ from 100
            public double OxygenConsumptionPerKgFeed { get; set; } = 480.0; // ↓ from 500
            public double WaterExchangeCoefficient { get; set; } = 0.05;

            // ─── Compliance & Certification ───
            public decimal EscapeFinePer100Fish { get; set; } = 500_000;
            public decimal ASCCertificationCostNOK { get; set; } = 2_000_000;
            public decimal ASCAuditCostYearlyNOK { get; set; } = 500_000;
            public double ASCPricePremiumPercent { get; set; } = 10; // ↑ from 8
            public double OrganicPricePremiumPercent { get; set; } = 18; // ↑ from 15

            // ─── Research ───
            public decimal ResearchBaseCostNOK { get; set; } = 450_000; // ↓ from 500k
            public decimal ResearchMaxCostNOK { get; set; } = 4_500_000; // ↓ from 5M
        }

        private class EnvironmentalEventsConfig
        {
            public PlanktonBloom.PlanktonBloomConfig? PlanktonBloom { get; set; }
            public List<EnvironmentalEventDefinition>? EnvironmentalEvents { get; set; }
        }

        private class EnvironmentalEventDefinition
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Category { get; set; } = string.Empty;
            public double Weight { get; set; }
            public int CooldownDays { get; set; }
            public int MinDay { get; set; }
            public int MaxDay { get; set; }
            public string Description { get; set; } = string.Empty;
            public decimal BalanceChange { get; set; }
            public string Severity { get; set; } = string.Empty;
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

                // Post-tick phase: due wellboat jobs execute AFTER State.Day++
                // so the booking contract (DueDay = bookedDay + delay) stays
                // exact; placing this inside AdvanceOneDay would deliver every
                // boat one day early (off-by-one, see NEXT_LEVEL_PLAN).
                ProcessDueWellboatJobs();
            }

            OnDayAdvanced?.Invoke(State.Day);
        }

        // ─── Per-advance note ledger ─────────────────────────────────────────
        // The bridge drains these notes once per Advance/AdvanceDays call and
        // projects them onto its legacy signals. Mutating code logs to
        // State.EventLog itself; the notes only mirror WHAT happened so the
        // presentation layer can react without owning simulation logic.

        private readonly List<DayNote> _dayNotes = new();

        /// <summary>Drain and clear the per-advance note ledger.</summary>
        public List<DayNote> DrainDayNotes()
        {
            var notes = new List<DayNote>(_dayNotes);
            _dayNotes.Clear();
            return notes;
        }

        /// <summary>
        /// Execute due wellboat jobs (Phase 2.4/3.4). Engine-owned mutations
        /// and event-log entry; caller (the game) only mirrors the notes.
        /// </summary>
        private void ProcessDueWellboatJobs()
        {
            if (State?.WellboatJobs is null || State.WellboatJobs.Count == 0) return;

            foreach (var job in State.WellboatJobs.Where(j => j.DueDay <= State.Day).ToList())
            {
                if (job.Kind == "freshwater")
                    ExecuteFreshwaterBath(job);
                else if (job.Kind == "rov")
                    ExecuteRovInspection(job);
                else
                    ExecuteDelouseOrHarvestWellboat(job);

                State.WellboatJobs.Remove(job);
            }
        }

        private void ExecuteFreshwaterBath(Logistics.WellboatJob job)
        {
            var site = State.SeaSites.FirstOrDefault(s => s.Id == job.SiteId);
            bool ok = site != null && site.Pens.Any(p => p.TotalCount > 0);
            int deaths = 0;
            string message;
            if (ok)
            {
                // Freshwater bath: treat all pens, zero resistance. The site's
                // lice kill applies once per site (first live pen), matching
                // DelouseSite semantics (the old bridge multiplied the kill
                // per pen via the company model).
                var delousing = new Farm.DelousingSystem();
                double tempC = Weather?.CurrentTemperatureC ?? 10.0;
                double mortMult = CrewEffects.TreatmentMortalityMultiplier(State);
                bool killDone = false;
                foreach (var pop in site!.Pens)
                {
                    if (pop.TotalCount <= 0) continue;
                    deaths += delousing.Treat(pop, Data.DelousingMethod.FreshwaterBath,
                        site.Lice, IntegratedDisease, tempC, mortMult,
                        applyCompanyLiceKill: !killDone).Deaths;
                    killDone = true;
                }
                FinanceManager?.Spend(60_000m); // bath consumables
                message = $"Ferskvannsbåt behandlet {job.SiteId} ({deaths:N0} håndteringsdødsfall, null resistens)";
            }
            else
            {
                message = $"Ferskvannsbåt kunne ikke behandle {job.SiteId}";
            }

            State.EventLog.Add(new Events.EventRecord
            {
                Day = State.Day,
                Name = ok ? "Ferskvannsbehandling" : "Brønnbåt returnerte",
                Category = Events.EventCategory.Disease,
                Description = message,
                BalanceChange = ok ? -60_000m : 0m
            });
            _dayNotes.Add(new DayNote
            {
                Kind = "wellboat_freshwater",
                SiteId = job.SiteId,
                Success = ok,
                Message = message
            });
        }

        private void ExecuteRovInspection(Logistics.WellboatJob job)
        {
            var site = State.SeaSites.FirstOrDefault(s => s.Id == job.SiteId);
            bool ok = site != null;
            string message = ok
                ? $"ROV ved {site!.Id}: nøter renset (+15%)"
                : $"ROV kunne ikke inspisere {job.SiteId}";
            if (ok)
            {
                site!.NetIntegrityPct = Math.Min(100.0, site.NetIntegrityPct + 15.0);
                int fish = site.Pens.SelectMany(p => p.Cohorts).Sum(c => c.Count);
                message = $"ROV ved {site.Id}: {fish:N0} fisk, lus {GetCompanyLiceAverage():F2}/fisk, " +
                          $"gjeller {site.GillScore:F0}/100, nøter {site.NetIntegrityPct:F0}% (+15 renset), " +
                          $"rømminger i år: {site.EscapesThisYear}";
                State.EventLog.Add(new Events.EventRecord
                {
                    Day = State.Day,
                    Name = "ROV-inspeksjon",
                    Category = Events.EventCategory.Equipment,
                    Description = message,
                    BalanceChange = 0m
                });
            }
            _dayNotes.Add(new DayNote
            {
                Kind = "wellboat_rov",
                SiteId = job.SiteId,
                Success = ok,
                Message = message
            });
        }

        private void ExecuteDelouseOrHarvestWellboat(Logistics.WellboatJob job)
        {
            bool done = job.Kind == "delouse"
                ? DelouseSite(job.SiteId, string.IsNullOrEmpty(job.Method) ? "hydrolicer" : job.Method).Ok
                : HarvestSite(job.SiteId).Ok;

            State.EventLog.Add(new Events.EventRecord
            {
                Day = State.Day,
                Name = done ? "Brønnbåt ankommet" : "Brønnbåt returnerte",
                Category = Events.EventCategory.Equipment,
                Description = done
                    ? $"Brønnbåt utførte {job.Kind} ved {job.SiteId}"
                    : $"Brønnbåt kunne ikke utføre {job.Kind} ved {job.SiteId} (tom merd/manglende midler)",
                BalanceChange = 0m
            });
            _dayNotes.Add(new DayNote
            {
                Kind = job.Kind == "delouse" ? "wellboat_delouse" : "wellboat_harvest",
                SiteId = job.SiteId,
                Success = done,
                Message = job.Kind
            });
        }

        /// <summary>
        /// Seasonal external sea-lice pressure multiplier (neighbouring farms and
        /// wild salmon). Peaks in late summer, troughs in late winter; centred on
        /// <see cref="SeasonalLicePressureBase"/> so a normal cycle builds to the
        /// regulatory action limit. Deterministic.
        /// </summary>
        private static double SeasonalLicePressure(int day)
        {
            double dayOfYear = ((day % 365) + 365) % 365;
            return SeasonalLicePressureBase
                + SeasonalLicePressureAmplitude * Math.Sin(2.0 * Math.PI * (dayOfYear - 180.0) / 365.0);
        }

        /// <summary>
        /// Engine-owned storm containment hazards. On storm days net integrity
        /// decays (containment/crew multipliers); in calm weather it repairs. A
        /// severe storm can cause a containment failure that releases fish, and
        /// any site below the documented 40% risk line leaks on a storm day. All
        /// escapes flow through <see cref="Farm.SeaSite.RecordEscape"/> and charge
        /// the regulatory fine, keeping the sustainability and finance paths
        /// consistent. Deterministic for a given seed (storm timing is seeded).
        /// </summary>
        private void ApplyStormHazards()
        {
            if (!_enableEvents) return;
            if (State.SeaSites == null || State.SeaSites.Count == 0) return;

            var state = Weather.CurrentState;
            bool stormy = state is WeatherState.Storm or WeatherState.Severe;
            if (!stormy && State.SeaSites.All(s => s.NetIntegrityPct >= 100.0)) return;

            double severity = state == WeatherState.Severe ? 1.0 : state == WeatherState.Storm ? 0.5 : 0.0;
            double crewStormMult = Core.CrewEffects.StormLossMultiplier(State);
            double crewNetDecayMult = Core.CrewEffects.NetDecayMultiplier(State);

            foreach (var site in State.SeaSites)
            {
                var cmods = Farm.ContainmentTech.ModifiersFor(site.Containment);

                if (stormy)
                {
                    double wear = (state == WeatherState.Severe
                            ? SevereIntegrityLossPctPerDay
                            : StormIntegrityLossPctPerDay)
                        * cmods.StormDamageMult * crewNetDecayMult;
                    site.NetIntegrityPct = Math.Max(0.0, site.NetIntegrityPct - wear);
                }
                else
                {
                    site.NetIntegrityPct = Math.Min(100.0, site.NetIntegrityPct + NetIntegrityRepairPctPerDay);
                }

                int siteFish = 0;
                foreach (var pop in site.Pens)
                    foreach (var cohort in pop.Cohorts)
                        siteFish += cohort.Count;
                if (siteFish <= 0) continue;

                double exposure = Math.Max(0.0, 1.0 - site.NetIntegrityPct / 100.0);
                double riskMult = cmods.EscapeRiskMult
                    * Optimization.RemoteOpsCentre.EscapeResponseMult(site.WatchMode)
                    * crewStormMult;

                // (a) Rare containment failure on a severe-storm day, scaled by wear.
                if (state == WeatherState.Severe)
                {
                    double expected = siteFish * StormLossFractionPerFailure
                        * StormFailureProbabilityPerSevereDay * exposure * riskMult;
                    StormEscapeEvents += StormFailureProbabilityPerSevereDay;
                    int escapees = (int)Math.Round(expected);
                    if (escapees > 0)
                        RecordStormEscape(site, escapees, severity);
                }

                // (b) Hard breach below the documented 40% risk line.
                if (stormy && site.NetIntegrityPct < NetIntegrityEscapeRiskThresholdPct)
                {
                    int breach = (int)Math.Round(siteFish * (0.02 + severity * 0.06) * exposure * riskMult);
                    if (breach > 0)
                        RecordStormEscape(site, breach, severity);
                }
            }
        }

        /// <summary>
        /// Remove escaped fish from a site's pens, record the escape, charge the
        /// regulatory fine and write the event log entry.
        /// </summary>
        private void RecordStormEscape(SeaSite site, int escapees, double severity)
        {
            if (escapees <= 0) return;

            int remaining = escapees;
            foreach (var pop in site.Pens)
            {
                foreach (var cohort in pop.Cohorts)
                {
                    int take = Math.Min(cohort.Count, remaining);
                    cohort.Count -= take;
                    remaining -= take;
                    if (remaining <= 0) break;
                }
                pop.TriggerOnPopulationChanged();
                if (remaining <= 0) break;
            }

            site.RecordEscape(escapees);

            decimal fine = escapees * EscapeFineNOKPerFish;
            if (!FinanceManager.Spend(fine))
                FinanceManager.ForceSpend(fine);

            State.EventLog.Add(new Events.EventRecord
            {
                Day = State.Day,
                Name = "Escape",
                Category = Events.EventCategory.Equipment,
                Description = $"RØMMING: {escapees:N0} fish escaped at {site.Id} " +
                              $"(nets {site.NetIntegrityPct:F0}%, storm severity {severity:F1})",
                BalanceChange = -fine
            });
            State.TrimEventLog();

            OnEventTriggered?.Invoke($"🌊 RØMMING: {escapees:N0} fisk rømte ved {site.Id} (bot {fine:N0} NOK)");
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

            // Update environment model and all zones. S3: EnvironmentModel
            // keeps its own zone list, so mirror the live zones first —
            // otherwise water chemistry never advances.
            Environment.SyncZones(State.Zones);
            Environment.AdvanceDays(1);
            // NOTE: Do NOT override zone temperatures here — zones maintain their own temperatures
            // zone.AdvanceDays(1) is already called by Environment.AdvanceDays(1)

            // Plankton bloom dynamics (depends on updated zone conditions)
            PlanktonBloom?.AdvanceDay(State, Weather);

            // Storm containment hazards (engine-owned): net-integrity wear +
            // deterministic escape accounting. Only when events are enabled, so
            // enableEvents:false stays deterministic and unaffected.
            ApplyStormHazards();

            // ═══════════════════════════════════════════
            // PHASE 2A: HEALTH PREP (Calculate disease rates BEFORE biology)
            // ═══════════════════════════════════════════
            Mortality.ClearDiseaseMortalityRates(); // Clear previous day's disease rates
            
            // SeaLice.AdvanceDays(1);
            // Phase 3 (P10): climate coupling — warming shortens lice development time.
            Health.ThermalLiceCoupling.SyncSeaLiceTemperature(SeaLice, temperature, WarmingPerYearC, State.Day);
            // Seasonal external copepodid pressure (neighbouring farms / wild
            // salmon): spring/summer peak, winter trough. Keeps lice pressure
            // realistic over the year without constant artificial seeding.
            SeaLice.SetExternalPressure(SeasonalLicePressure(State.Day));
            SeaLice.AdvanceDay();
            // Phase 2.1: per-site lice models are the authority for farm
            // truth (company model retained as a fallback/compat rollup).
            AdvanceSiteLiceModels(temperature);

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

                // Calculate combined environmental multiplier (DO saturation × stocking density)
                double stockingDensity = zone != null ? (biomassKg / PenVolumeProxyM3) : DefaultStockingDensityKgM3;
                // Phase 4.1: containment — closed rungs behave denser for O2
                // purposes (recirculation load) via o2DemandMult.
                var homeSite = State.SeaSites.FirstOrDefault(s => s.Id == population.SiteId);
                var containmentMods = Farm.ContainmentTech.ModifiersFor(
                    homeSite?.Containment ?? Farm.ContainmentLevel.OpenNet);
                double densityForO2 = stockingDensity * containmentMods.OxygenDemandMult;
                double environmentalMultiplier = 1.0;
                if (zone != null)
                {
                    environmentalMultiplier = Growth.CalculateCombinedEnvironmentalMultiplier(
                        zone.WaterChemistry,
                        zone.WaterChemistry.Temperature,
                        densityForO2,
                        35.0);
                }

                // Advance population: age and mortality only — growth handled by SDE below
                population.AdvanceDays(1, skipGrowth: true);

                // Manual growth adjustment — indexed loop, in-place (T56)
                for (int ci = 0; ci < population.Cohorts.Count; ci++)
                {
                    var cohort = population.Cohorts[ci];

                    // Apply genetics multiplier
                    double geneticMultiplier = cohort.Genetics?.GetGrowthMultiplier() ?? 1.0;

                    // Wave 6: inbreeding depression — additive and opt-in. A genome
                    // carrying a cumulative inbreeding proxy > 0 loses a small fraction
                    // of its growth; a null genome or zero inbreeding leaves the factor
                    // at 1.0, so existing behaviour is unchanged.
                    double inbreedingGrowthFactor = 1.0;
                    if (cohort.Genetics != null && cohort.Genetics.InbreedingCoefficient > 0.0)
                    {
                        inbreedingGrowthFactor = 1.0 - GenomicSelection.InbreedingDepressionPenalty(
                            GeneticTrait.GrowthRate, cohort.Genetics.InbreedingCoefficient);
                    }

                    // Use SDE model for more realistic growth with environmental modifiers
                    double newCalculatedWeight = Growth.ApplySDEGrowthOneDay(
                        cohort.MeanWeightKg,
                        temperature,
                        SdeTargetWeightKg,
                        SdeGeneticGrowthRateAlphaMax, // calibrating: SDE-only growth
                        SdeIndividualVarianceSigma, // kept small vs deterministic growth
                        cohort.Stage,
                        environmentalMultiplier);

                    // Apply genetic modifier only (environmental already applied in SDE)
                    // Phase 4.1: containment density-stress growth effect.
                    // P5: post-smolt quality carryover (SuperSmolt grows faster).
                    double qualityGrowth = Farm.SmoltQualityRegistry.Get(cohort.Quality).GrowthMultiplier;
                    double delta = newCalculatedWeight - cohort.MeanWeightKg;
                    cohort.MeanWeightKg += (delta * geneticMultiplier * inbreedingGrowthFactor * containmentMods.GrowthMult * qualityGrowth);
                    if (cohort.MeanWeightKg < 0) cohort.MeanWeightKg = 0;
                }

                // Feed the population — FCR-aware.
                // Phase 2.1: real DO-saturation penalty (was hardcoded 1.0, which
                // kept FCR rosy while growth stalled in low oxygen).
                // Phase 4.1: contained sites feed at effective (denser) density.
                // Phase 4.3: damaged gills throttle appetite (0.6-1.0).
                double waterPenalty = 1.0;
                if (zone != null)
                {
                    double doSat = Biology.WaterQuality.CalculateDOSaturation(
                        zone.WaterChemistry.OxygenO2, temperature);
                    waterPenalty = Biology.WaterQuality.CalculateDOSaturationModifier(doSat);
                }
                if (homeSite != null)
                    waterPenalty *= 0.6 + 0.4 * (homeSite.GillScore / 100.0);
                // P6: precision feeding — feed-spread quality by site watch mode,
                // improved by AI feeding. Poor spread raises FCR, waste, size variance.
                double spreadQuality = homeSite?.WatchMode switch
                {
                    "watch" => 0.95,
                    "scan" => 0.85,
                    "auto" => 0.65,
                    _ => 0.80
                };
                if (AIFeeding != null && AIFeeding.IsInstalled)
                    spreadQuality = Math.Min(1.0, spreadQuality + 0.1);
                var feedResult = Feed.Feed(population, 1, temperature, densityForO2, waterPenalty, spreadQuality);

                if (zone != null && feedResult.FeedUsedKg > 0)
                {
                    zone.FeedConversionRatio = feedResult.ActualFCR;
                }

                // Phase 4.2: autopilot spill — unfed-eyes overfeed +8%.
                double spill = Optimization.RemoteOpsCentre.FeedSpillFraction(homeSite?.WatchMode);
                if (spill > 0.0 && feedResult.CostNOK > 0)
                {
                    decimal spillCost = feedResult.CostNOK * (decimal)spill;
                    if (!FinanceManager.Spend(spillCost))
                        FinanceManager.ForceSpend(spillCost);
                }

                // Phase 5.3: Sludge accumulation from feed used.
                if (homeSite != null && feedResult.FeedUsedKg > 0)
                {
                    double sludgeKgDs = Farm.SludgeValorization.DailySludgeKgDs(homeSite, feedResult.FeedUsedKg);
                    homeSite.SludgeAccumulatedKgDs += sludgeKgDs;
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
            if (AIFeeding?.IsInstalled == true)
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
            // PHASE 3B: CARBON LEDGER (daily emissions tracking, Phase 5.4)
            // ═══════════════════════════════════════════
            ProcessCarbonLedger();

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
            // P9: Price Council binds the monthly pen price from the rolling spot series.
            PenPriceCouncil.Update(MarketSimulation.PricePerKg);

            // ═══════════════════════════════════════════
            // PHASE 6: FINANCE
            // ═══════════════════════════════════════════
            ProcessDailyFinance();

            // GameState.Balance is a read-only MIRROR of FinanceManager.Balance.
            // Writers must use FinanceManager (AddRevenue/Spend/ForceSpend);
            // this keeps event Conditions and UI readers correct all day.
            #pragma warning disable CS0618 // Balance mirror: save-compat field by design
            State.Balance = FinanceManager.Balance;
            #pragma warning restore CS0618

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
            // PHASE 7A0b: SLUDGE VALORIZATION (weekly, Phase 5.3)
            // Process accumulated sludge at sites with hub connection or manually triggered.
            // ═══════════════════════════════════════════
            if (State.Day - _lastSustainabilityUpdate >= SUSTAINABILITY_UPDATE_INTERVAL)
            {
                ProcessSludgeValorization();
            }

            // ═══════════════════════════════════════════
            // PHASE 7A0: TRAFFIC LIGHT ZONES (weekly, Phase 1.3)
            // Assesses each zone from live lice loads; MAB consequence
            // applies ONCE per color change (never compounds daily).
            // ═══════════════════════════════════════════
            if (State.Day - _lastTrafficLightUpdate >= TRAFFIC_LIGHT_UPDATE_INTERVAL)
            {
                // Phase 2.1: per-site sync + weekly counting + Mattilsynet
                // orders, before the traffic-light assessment reads them.
                UpdateSiteLiceCounts();
                UpdateTrafficLightZones();
                UpdatePreventiveCoverage();
                _lastTrafficLightUpdate = State.Day;
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
            // PHASE 7F2: RESOURCE-RENT TAX (quarterly, P9, opt-in)
            // 25% cash-flow tax on the sea phase with a 70M NOK standard deduction.
            // Opt-in so default/legacy economies and tests are unaffected.
            // ═══════════════════════════════════════════
            if (EnableResourceRentTax && State.Day - _lastTaxUpdate == 0)
            {
                decimal seaProfit = FinancialStatements?.ComputeNetIncome() ?? 0m;
                decimal rr = LakskongenSim.Finance.ResourceRentTax.Compute(seaProfit);
                if (rr > 0)
                {
                    if (!FinanceManager.Spend(rr))
                    {
                        OnEventTriggered?.Invoke($"💸 Cannot pay {rr:N0} NOK resource-rent tax. Balance: {FinanceManager.Balance:N0} NOK");
                        FinanceManager.ForceSpend(rr);
                    }
                    ResourceRentPaid += rr;
                    FinancialStatements?.RecordTax(rr);
                    State.EventLog.Add(new Events.EventRecord
                    {
                        Day = State.Day,
                        Name = "Ressursrente",
                        Category = Events.EventCategory.Regulation,
                        Description = $"Ressursrenteskatt (25%, 70M-fradrag): {rr:N0} NOK",
                        BalanceChange = -rr
                    });
                    State.TrimEventLog();
                }
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
            // PHASE 7K3: BLOOM DECK (weekly, Phase 4.3)
            // ═══════════════════════════════════════════
            if (State.Day - _lastBloomUpdate >= BLOOM_UPDATE_INTERVAL)
            {
                RollBloomWeek();
                _lastBloomUpdate = State.Day;
            }

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
            // PHASE 7K2: CONTRACT EXPIRY (weekly, Phase 2.3)
            // Unfulfilled quantities incur the agreed penalty at expiry.
            // ═══════════════════════════════════════════
            if (State.Day - _lastContractUpdate >= MARKET_INTELLIGENCE_UPDATE_INTERVAL
                && ContractSystem != null)
            {
                _lastContractUpdate = State.Day;
                foreach (var (contract, penalty) in ContractSystem.AdvanceWeek())
                {
                    if (penalty > 0)
                    {
                        if (!FinanceManager.Spend(penalty))
                            FinanceManager.ForceSpend(penalty);
                        State.EventLog.Add(new Events.EventRecord
                        {
                            Day = State.Day,
                            Name = "Kontraktsbrudd",
                            Category = Events.EventCategory.Market,
                            Description = $"Kontrakt med {contract.Buyer} utløpt ({contract.FulfilledKg:N0}/{contract.QuantityKg:N0} kg) — bot {penalty:N0} NOK",
                            BalanceChange = -penalty
                        });
                        State.TrimEventLog();
                    }
                }
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

            // ─── Scenario win/loss evaluation (runs after all subsystems) ───
            Scenario?.CheckWinConditions(State.SustainabilityScore, Tutorial?.IsComplete ?? false);

            // ═══════════════════════════════════════════
            // PHASE 9: NIGHT-SHIFT ROC POLICY (daily, Phase 5.2)
            // ═══════════════════════════════════════════
            // PHASE 9: NIGHT-SHIFT ROC POLICY (daily, Phase 5.2)
            // Process night events (00-06) based on ROC policy.
            // ═══════════════════════════════════════════
            if (State.Day - _lastNightPolicyUpdate >= NIGHT_POLICY_INTERVAL)
            {
                ProcessNightShiftPolicy();
                _lastNightPolicyUpdate = State.Day;
            }

            overallStopwatch.Stop();
            // T56: Removed SimLog.Info to avoid string allocation in hot path
            // Use Trace.WriteLine or conditional logging if debug output needed
        }

        /// <summary>
        /// Process night-shift ROC policy (Phase 5.2): handles 00-06 events based on policy.
        /// </summary>
        private void ProcessNightShiftPolicy()
        {
            var policy = State.NightShiftPolicy;
            if (policy == Logistics.NightShiftPolicy.Standard) return; // nothing special

            var crewCount = State.Crew.Count;

            // Night event rolls: bloom, hypoxia, storm, equipment failure
            // Base chance per night per site, modified by policy
            var sites = State.SeaSites.Where(s => s.Pens.Any(p => p.TotalCount > 0)).ToList();
            var rng = _rng;

            foreach (var site in sites)
            {
                // 1. Bloom/hypoxia early detection (ScanOnly = queued, Auto = instant)
                double detectChance = Logistics.NightShiftResolver.DetectionChance(State.NightShiftPolicy);
                if (rng.NextDouble() < detectChance * 0.1) // 10% base chance per night
                {
                    var activeBlooms = State.ActiveBlooms.Where(b => b.SiteId == site.Id && !b.IsExpired(State.Day)).ToList();
                    if (activeBlooms.Any())
                    {
                        var severity = activeBlooms.Max(b => b.Severity01);
                        if (State.NightShiftPolicy == Logistics.NightShiftPolicy.AutomatedROC)
                        {
                            // Auto-mitigate: O2 diffuser or freshwater if available
                            // For now just log the detection
                            State.EventLog.Add(new Events.EventRecord
                            {
                                Day = State.Day,
                                Name = "Natt-deteksjon",
                                Category = Events.EventCategory.Weather,
                                Description = $"[AUTO-ROC] Oppblomstring oppdaget ved {site.Id} kl 02:00 — mottiltak initiert",
                                BalanceChange = 0m
                            });
                        }
                        else if (State.NightShiftPolicy == Logistics.NightShiftPolicy.ScanOnly)
                        {
                            State.EventLog.Add(new Events.EventRecord
                            {
                                Day = State.Day,
                                Name = "Natt-skanning",
                                Category = Events.EventCategory.Weather,
                                Description = $"[SCAN-ONLY] Oppblomstring oppdaget ved {site.Id} kl 02:00 — køet til 06:00",
                                BalanceChange = 0m
                            });
                        }
                    }
                }

                // 2. Hypoxia / O2 crash auto-response
                var zone = State.Zones.FirstOrDefault(z => z.Id == site.ZoneId);
                if (zone != null && zone.WaterChemistry.OxygenO2 < 5.5)
                {
                    if (State.NightShiftPolicy == Logistics.NightShiftPolicy.AutomatedROC)
                    {
                        // Auto O2 diffuser or aeration
                        FinanceManager.Spend(12_000m); // emergency O2 cost
                        State.EventLog.Add(new Events.EventRecord
                        {
                            Day = State.Day,
                            Name = "Natt-oksygen",
                            Category = Events.EventCategory.Weather,
                            Description = $"[AUTO-ROC] O2-krasj {site.Id} kl 03:00 — diffuser startet (12k)",
                            BalanceChange = -12_000m
                        });
                    }
                }
            }

            // Crew fatigue from AutomatedROC
            if (State.NightShiftPolicy == Logistics.NightShiftPolicy.AutomatedROC)
            {
                foreach (var crew in State.Crew)
                {
                    crew.Morale = System.Math.Max(10, crew.Morale - Logistics.NightShiftResolver.FatiguePerNightAuto);
                }
            }

            // Apply night misfire chance
            if (State.NightShiftPolicy == Logistics.NightShiftPolicy.AutomatedROC)
            {
                var avgSkill = State.Crew.Count > 0 ? State.Crew.Average(c => c.Morale) / 10.0 : 0.5;
                if (rng.NextDouble() < Logistics.NightShiftResolver.MisfireChance(State.NightShiftPolicy, (int)(avgSkill * 10)))
                {
                    State.EventLog.Add(new Events.EventRecord
                    {
                        Day = State.Day,
                        Name = "AUTO-ROC feilaktivering",
                        Category = Events.EventCategory.Equipment,
                        Description = "Automatisk natt-ROV feil: feil merd / feil tiltak — crew stress +5%",
                        BalanceChange = -25_000m
                    });
                    foreach (var c in State.Crew) c.Morale = Math.Max(10, c.Morale - 5);
                }
            }

            // Premium cost
            decimal premium = Logistics.NightShiftResolver.MonthlyCost(State.NightShiftPolicy) / 30m;
            if (premium > 0)
            {
                FinanceManager.Spend(premium);
                FinancialStatements?.RecordOperatingExpense(premium);
            }
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

            // Phase 3.1: named crew payroll.
            foreach (var member in State.Crew)
                dailyWages += member.MonthlySalaryNok / 30m;

            // Phase 3.5: VAP plant opex 5k/day when owned.
            decimal vapOpex = State.OwnsProcessingPlant ? 5_000m : 0m;

            // 2. Fixed maintenance costs (e.g., 0.01% of build cost daily)
            decimal dailyMaintenance = (totalSites * _balancing.SeaSiteBuildCostNOK * 0.0001m);
            dailyMaintenance += _dailyAiFeedingLicenseCost; // Add AI Feeding license cost here

            // 3. Apply costs to FinanceManager and FinancialStatements
            decimal totalOpEx = dailyWages + dailyMaintenance + _dailyFarmSystemsOpex + vapOpex;
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

            // 3B. Preventive mitigation OPEX (Phase 1.5): silent daily upkeep.
            // Skirt 2 000 NOK/site/day, deterrent 1 000 NOK/site/day.
            decimal mitigationOpex = 0m;
            foreach (var site in State.SeaSites)
            {
                if (site.HasLiceSkirt) mitigationOpex += 2_000m;
                if (site.HasSealDeterrent) mitigationOpex += 1_000m;
            }
            if (mitigationOpex > 0)
            {
                if (!FinanceManager.Spend(mitigationOpex))
                    FinanceManager.ForceSpend(mitigationOpex);
                FinancialStatements?.RecordOperatingExpense(mitigationOpex);
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

            // 4B. Loan amortization (S5): GameBridge.TakeLoan records a
            // LoanState liability; here principal + interest accrue daily.
            // Previously loans were free money (principal booked as revenue
            // with no repayment), winnable on day 1.
            foreach (var loan in State.ActiveLoans.ToList())
            {
                if (loan.Principal <= 0) { State.ActiveLoans.Remove(loan); continue; }
                loan.DaysTaken++;
                int remainingDays = Math.Max(1, loan.TermDays - loan.DaysTaken + 1);
                decimal dailyInterest = loan.Principal * loan.InterestRate / 365m;
                decimal dailySlice = loan.Principal / remainingDays + dailyInterest;
                // Final day: clear exactly (avoid dust from rounding).
                if (loan.DaysTaken >= loan.TermDays) dailySlice = loan.Principal + dailyInterest;
                if (!FinanceManager.Spend(dailySlice))
                {
                    OnEventTriggered?.Invoke($"💸 Cannot pay {dailySlice:N0} NOK loan instalment. Balance: {FinanceManager.Balance:N0} NOK");
                    FinanceManager.ForceSpend(dailySlice);
                }
                FinancialStatements?.RecordOperatingExpense(dailySlice);
                loan.Principal -= (dailySlice - dailyInterest);
                if (loan.Principal <= 0.01m || loan.DaysTaken >= loan.TermDays)
                    State.ActiveLoans.Remove(loan);
            }

            // 4C. Insurance premiums (S5): policies were free — charge the
            // annual premium pro-rata every day.
            if (Insurance != null)
            {
                decimal dailyPremium = 0m;
                foreach (var policy in Insurance.Policies)
                    dailyPremium += policy.PremiumNokPerYear / 365m;
                if (dailyPremium > 0)
                {
                    if (!FinanceManager.Spend(dailyPremium))
                        FinanceManager.ForceSpend(dailyPremium);
                    FinancialStatements?.RecordOperatingExpense(dailyPremium);
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
        /// Process sludge valorization at sea sites (Phase 5.3).
        /// Converts accumulated sludge to biogas revenue + fertilizer revenue - opex.
        /// Runs weekly as part of sustainability update.
        /// </summary>
        private void ProcessSludgeValorization()
        {
            var sludgeParams = Farm.SludgeValorization.Default;
            foreach (var site in State.SeaSites)
            {
                if (site.SludgeAccumulatedKgDs < 500.0) continue; // Minimum batch size for valorization

                // Valorize sludge
                var (biogasRev, fertRev, opex) = Farm.SludgeValorization.Valorize(site.SludgeAccumulatedKgDs, sludgeParams);
                decimal netRevenue = biogasRev + fertRev - opex;

                if (netRevenue != 0m)
                {
                    if (netRevenue > 0)
                        FinanceManager.AddRevenue(netRevenue);
                    else
                    {
                        if (!FinanceManager.Spend(-netRevenue))
                            FinanceManager.ForceSpend(-netRevenue);
                    }

                    site.SludgeValorizedKgDs += site.SludgeAccumulatedKgDs;
                    site.LastSludgeValorizationDay = State.Day;

                    // Phase 5.4: Add biogas credit to carbon ledger
                    double biogasNm3 = site.SludgeAccumulatedKgDs * Farm.SludgeValorization.Default.BiogasYieldNm3PerKgDs;
                    double biogasCredit = Farm.CarbonLedger.BiogasCreditKgCO2e(biogasNm3);
                    site.CarbonTracker.AddCredit("Biogas", site.Id, biogasCredit, State.Day);

                    State.EventLog.Add(new Events.EventRecord
                    {
                        Day = State.Day,
                        Name = "Slam-valorisering",
                        Category = Events.EventCategory.Equipment,
                        Description = $"{site.Id}: {site.SludgeAccumulatedKgDs:F0} kg DS → biogas {biogasRev:N0} + gjødsel {fertRev:N0} - OPEX {opex:N0} = {netRevenue:+#,0;-#,0;0} NOK (CO2e credit: {biogasCredit:F0} kg)",
                        BalanceChange = netRevenue
                    });
                    State.TrimEventLog();
                }

                site.SludgeAccumulatedKgDs = 0.0;
            }
        }


        /// <summary>
        /// Process daily carbon emissions for all facilities (Phase 5.4).
        /// Tracks Scope 1 (fuel), Scope 2 (electricity), Scope 3 (feed, transport)
        /// and credits from biogas, kelp, avoided soy.
        /// </summary>
        private void ProcessCarbonLedger()
        {
            int day = State.Day;
            double feedReplacementPct = State.RecipeReplacePct;

            // ─── Sea Sites ───
            foreach (var site in State.SeaSites)
            {
                double biomassTonnes = site.Pens.Sum(p => p.GetTotalBiomassKg()) / 1000.0;
                if (biomassTonnes <= 0) continue;

                // Scope 1: Diesel fuel
                double fuelL = Farm.CarbonLedger.DailySiteFuelLiters(site, biomassTonnes);
                double fuelCO2e = fuelL * Farm.CarbonLedger.DieselKgCO2ePerLiter;
                site.CarbonTracker.AddEmission("Scope1_Fuel", site.Id, fuelCO2e, day);

                // Scope 2: Electricity
                double elecKwh = Farm.CarbonLedger.DailySiteElectricityKwh(site, biomassTonnes);
                double elecCO2e = elecKwh * Farm.CarbonLedger.GridElectricityKgCO2ePerKwh;
                site.CarbonTracker.AddEmission("Scope2_Electricity", site.Id, elecCO2e, day);
            }

            // ─── Feed emissions (Scope 3) - aggregate across all populations ───
            double totalFeedKg = 0.0;
            foreach (var pop in State.Populations)
            {
                // Approximate from biomass - rough estimate 1.5% body weight/day
                totalFeedKg += pop.GetTotalBiomassKg() * 0.015;
            }
            if (totalFeedKg > 0)
            {
                double feedCO2e = Farm.CarbonLedger.FeedEmissionsKgCO2e(totalFeedKg, feedReplacementPct);
                // Attribute to first site for simplicity
                var firstSite = State.SeaSites.FirstOrDefault();
                if (firstSite != null)
                    firstSite.CarbonTracker.AddEmission("Scope3_Feed", "feed_total", feedCO2e, day);
            }

            // ─── RAS Facilities ───
            foreach (var ras in RASFacilities)
            {
                if (!ras.IsOperational) continue;
                double elecCO2e = ras.DailyEnergyKWh * Farm.CarbonLedger.GridElectricityKgCO2ePerKwh;
                ras.CarbonTracker.AddEmission("Scope2_Electricity", ras.Id, elecCO2e, day);
            }

            // ─── Offshore Farms ───
            foreach (var off in OffshoreFarms)
            {
                if (!off.IsOperational) continue;
                double biomassTonnes = off.Pens.Sum(p => p.GetTotalBiomassKg()) / 1000.0;

                // Scope 1: Generator fuel (higher than fjord sites)
                double fuelL = 200.0 + biomassTonnes * 1.0; // base + per tonne
                double fuelCO2e = fuelL * Farm.CarbonLedger.DieselKgCO2ePerLiter;
                off.CarbonTracker.AddEmission("Scope1_Fuel", off.Id, fuelCO2e, day);

                // Scope 2: Grid/shore power (assume partial)
                double elecKwh = 500.0 + biomassTonnes * 2.0;
                double elecCO2e = elecKwh * Farm.CarbonLedger.GridElectricityKgCO2ePerKwh;
                off.CarbonTracker.AddEmission("Scope2_Electricity", off.Id, elecCO2e, day);

                // Scope 3: Crew rotation (helicopter) - only on rotation days
                if (off.DaysSinceCrewRotation == 0 && off.EmployeeCount > 0)
                {
                    double heliCO2e = Farm.CarbonLedger.OffshoreCrewRotationEmissionsKgCO2e(off.EmployeeCount);
                    off.CarbonTracker.AddEmission("Scope3_Transport", off.Id + "_crew", heliCO2e, day);
                }

                // Scope 3: Supply vessel (daily average)
                double vesselCO2e = Farm.CarbonLedger.OffshoreSupplyVesselEmissionsKgCO2e(3); // 3 trips/week
                off.CarbonTracker.AddEmission("Scope3_Transport", off.Id + "_vessel", vesselCO2e, day);
            }

            // ─── Processing Plants ───
            if (State.OwnsProcessingPlant && Enum.TryParse<Processing.ProcessingTier>(State.ProcessingTier, out var tier))
            {
                // Estimate daily throughput from recent harvests
                double dailyThroughputKg = State.Populations
                    .SelectMany(p => p.Cohorts)
                    .Where(c => c.Stage == LakskongenSim.Data.LifeStage.Harvest)
                    .Sum(c => c.Count * c.MeanWeightKg) * 0.02; // ~2% harvest rate
                
                double procCO2e = Farm.CarbonLedger.ProcessingPlantEmissionsKgCO2e((int)dailyThroughputKg, tier);
                // Attribute to a virtual processing tracker on first site or create one
                var firstSite = State.SeaSites.FirstOrDefault();
                if (firstSite != null)
                    firstSite.CarbonTracker.AddEmission("Scope2_Electricity", "processing_plant", procCO2e, day);
            }

            // ─── Biogas credits from sludge valorization ───
            // (When sludge is valorized, add credit - handled in ProcessSludgeValorization)
            // Here we just ensure the credit is tracked when valorization happens
            // The actual credit entry is added in ProcessSludgeValorization
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

            SimLog.Info($"[Day {record.Day}] EVENT: {record.Name} — {record.Description} ({record.BalanceChange:+#,0;-#,0;0} NOK)");

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

            // P11: catastrophe insurance payout for the realized disease loss.
            // ISA detection mandates a cull → ISA cover; other outbreaks map to
            // routine mortality cover.
            if (record != null && record.BalanceChange < 0m)
            {
                var kind = string.Equals(record.Id, "isa-outbreak", StringComparison.OrdinalIgnoreCase)
                    ? InsuranceKind.ISA
                    : InsuranceKind.Mortality;
                PayInsurance(kind, -record.BalanceChange, record.Name, EventCategory.Disease);
            }
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
        /// Weekly traffic-light assessment (Phase 1.3): each zone is assessed
        /// from the live lice load of its populations. MAB consequence applies
        /// exactly once per color change; steady colors never compound.
        /// </summary>
        /// <summary>
        /// Phase 2.1 daily per-site lice: advance each stocked site's model
        /// under local conditions, decay resistance, then spread copepodids
        /// between sites (same zone couples strongly, across zones weakly).
        /// Also decays each population's delousing-injury load.
        /// </summary>
        private void AdvanceSiteLiceModels(double temperature)
        {
            if (State.SeaSites.Count == 0)
                return;

            foreach (var site in State.SeaSites)
            {
                double biomassKg = 0.0;
                int fish = 0;
                foreach (var pen in site.Pens)
                {
                    foreach (var c in pen.Cohorts)
                    {
                        biomassKg += c.Count * c.MeanWeightKg;
                        fish += c.Count;
                    }
                    // P4: injury loads heal with time (handled here so the
                    // compounding only bites on repeat treatments).
                    pen.TreatmentHarm = Health.DelousingHarmModel.AdvanceDay(pen.TreatmentHarm);
                }
                if (fish <= 0)
                    continue;

                var zone = State.Zones.FirstOrDefault(z => z.Id == site.ZoneId);
                double volumeM3 = Math.Max(1.0, (double)site.PenVolume * Math.Max(1, site.NumberOfPens));
                site.Lice.SetEnvironment(
                    temperature,
                    35.0,
                    zone?.CurrentSpeed ?? 0.15,
                    biomassKg / volumeM3);
                site.Lice.SetExternalPressure(SeasonalLicePressure(State.Day));
                site.Lice.AdvanceDay();
                site.Lice.AdvanceResistanceDecay(1);
            }

            // Farm-to-farm infection: adult females shed copepodids onto
            // neighbours, attenuated by zone separation.
            foreach (var site in State.SeaSites)
            {
                double inflow = 0.0;
                foreach (var other in State.SeaSites)
                {
                    if (ReferenceEquals(other, site))
                        continue;
                    double coupling = other.ZoneId == site.ZoneId ? 0.4 : 0.08;
                    inflow += other.Lice.AdultFemalePerFish * coupling;
                }
                if (inflow > 0.0001)
                    site.Lice.SeedExternalInfection(Math.Min(inflow * 0.02, 0.05));
            }
        }

        /// <summary>
        /// Phase 2.1 weekly lice census: mirror each site model onto its pens
        /// (traffic lights, limits and UI read pen loads), count adult
        /// females where the water is warm enough to count (≥4 °C), enforce
        /// the legal limit (0.5, 0.2 in spring weeks 16–21), and escalate
        /// repeat breaches into Mattilsynet treatment orders with forced
        /// treatment past the deadline.
        /// </summary>
        private void UpdateSiteLiceCounts()
        {
            double temperature = Weather?.CurrentTemperatureC ?? 10.0;
            int weekOfYear = (State.Day % 365) / 7;
            double limit = (weekOfYear >= 16 && weekOfYear <= 21) ? 0.2 : 0.5;

            foreach (var site in State.SeaSites)
            {
                int fish = site.Pens.SelectMany(p => p.Cohorts).Sum(c => c.Count);
                if (fish <= 0)
                    continue;

                // Self-starting: scenario/hatchery stockings set population
                // loads without seeding the site model — re-seed from the
                // worst pen instead of zeroing everyone's lice.
                if (site.Lice.AverageLicePerFish <= 0.001)
                {
                    double maxLoad = site.Pens.Max(p => p.SeaLiceLoad);
                    if (maxLoad > 0.01)
                        site.Lice.SeedExternalInfection(Math.Min(maxLoad, 0.5));
                }

                double avgLice = site.Lice.AverageLicePerFish;
                foreach (var pen in site.Pens)
                    pen.SeaLiceLoad = avgLice;

                // No counting obligation below 4 °C.
                if (temperature < 4.0)
                    continue;

                site.LastLiceCount = site.Lice.AdultFemalePerFish;
                site.LastLiceCountDay = State.Day;

                if (site.LastLiceCount > limit)
                    site.ConsecutiveLiceBreaches++;
                else
                    site.ConsecutiveLiceBreaches = 0;

                if (site.ConsecutiveLiceBreaches >= 2 && site.TreatmentOrderDueDay < 0)
                {
                    site.TreatmentOrderDueDay = State.Day + 14;
                    State.EventLog.Add(new Events.EventRecord
                    {
                        Day = State.Day,
                        Name = "Mattilsynet order",
                        Category = Events.EventCategory.Regulation,
                        Description = $"Mattilsynet: {site.Id} exceeded the lice limit " +
                            $"({site.LastLiceCount:F2} adult females vs {limit:F1} allowed). " +
                            $"Delouse within 14 days.",
                        BalanceChange = 0m
                    });
                    State.TrimEventLog();
                    OnEventTriggered?.Invoke(
                        $"📋 Mattilsynet: {site.Id} over lusenivå ({site.LastLiceCount:F2} vs {limit:F1}) — avlus innen 14 dager!");
                }
                else if (site.TreatmentOrderDueDay >= 0 && State.Day > site.TreatmentOrderDueDay)
                {
                    EnforceLiceTreatmentOrder(site, temperature);
                }
            }
        }

        /// <summary>
        /// Mattilsynet deadline passed: forced delousing at the player's
        /// expense, breaches reset. The state does not negotiate.
        /// </summary>
        private void EnforceLiceTreatmentOrder(SeaSite site, double temperatureC)
        {
            var target = site.Pens.FirstOrDefault(p => p.TotalCount > 0);
            if (target == null)
            {
                site.TreatmentOrderDueDay = -1;
                site.ConsecutiveLiceBreaches = 0;
                return;
            }

            var delousing = new Farm.DelousingSystem();
            var result = delousing.Treat(target, Data.DelousingMethod.Thermolicer,
                site.Lice, IntegratedDisease, temperatureC, 1.0, applyCompanyLiceKill: true);
            if (!FinanceManager.Spend(result.Cost))
                FinanceManager.ForceSpend(result.Cost);
            FinancialStatements?.RecordOperatingExpense(result.Cost);
            site.GillScore = Math.Max(0.0, site.GillScore - result.GillDamage * 50.0);
            site.TreatmentOrderDueDay = -1;
            site.ConsecutiveLiceBreaches = 0;

            State.EventLog.Add(new Events.EventRecord
            {
                Day = State.Day,
                Name = "Forced delousing",
                Category = Events.EventCategory.Regulation,
                Description = $"Mattilsynet ordered delousing at {site.Id} was ignored — " +
                    $"forced Thermolicer treatment cost {result.Cost:N0} NOK.",
                BalanceChange = -result.Cost
            });
            State.TrimEventLog();
            OnEventTriggered?.Invoke($"⚖️ Tvangsavlusning ved {site.Id}: {result.Cost:N0} NOK.");
        }

        /// <summary>
        /// Phase 2.1 company rollup: biomass-weighted mean lice across stocked
        /// sites. Falls back to the legacy company model when nothing is stocked.
        /// </summary>
        public double GetCompanyLiceAverage()
        {
            double liceFish = 0.0;
            long fish = 0;
            foreach (var site in State.SeaSites)
            {
                foreach (var pen in site.Pens)
                {
                    foreach (var c in pen.Cohorts)
                    {
                        liceFish += c.Count * site.Lice.AverageLicePerFish;
                        fish += c.Count;
                    }
                }
            }
            if (fish > 0)
                return liceFish / fish;
            return SeaLice?.AverageLicePerFish ?? 0.0;
        }

        private void UpdateTrafficLightZones()
        {
            if (TrafficLightSystem == null || State.Zones.Count == 0) return;

            foreach (var zone in State.Zones)
            {
                var pops = State.Populations.Where(p => p.ZoneId == zone.Id).ToList();
                int fish = pops.SelectMany(p => p.Cohorts).Sum(c => c.Count);
                double lice = fish > 0
                    ? pops.Sum(p => p.Cohorts.Sum(c => c.Count) * p.SeaLiceLoad) / fish
                    : 0.0;

                var before = TrafficLightSystem.GetZoneColor(zone.Id);
                var after = TrafficLightSystem.AssessZone(zone.Id, lice);
                if (after == before) continue;

                double factor = TrafficLightSystem.ApplyMABConsequence(zone);
                State.EventLog.Add(new EventRecord
                {
                    Day = State.Day,
                    Name = $"Trafikklys: {after}",
                    Category = EventCategory.Regulation,
                    Description = $"Sone {zone.Id} → {after} (lus {lice:F2}/fisk, MAB ×{factor:F2})",
                    BalanceChange = 0m
                });
                State.TrimEventLog();
                SimLog.Info($"🚦 Zone {zone.Id} → {after} (lice {lice:F2}, MAB ×{factor:F2})");

                // Phase 4.1: red-zone MAB credit for contained sites (exact
                // revert on exit — re-entries never compound).
                foreach (var site in State.SeaSites.Where(s => s.ZoneId == zone.Id))
                {
                    if (after.ToString() == "Red" && !site.RedZoneCreditApplied
                        && site.Containment >= Farm.ContainmentLevel.SemiClosed)
                    {
                        double credit = Farm.ContainmentTech.MabCreditFor(site.Containment, true);
                        site.MABTonnes *= credit;
                        site.RedZoneCreditApplied = true;
                        SimLog.Info($"🛡️ Site {site.Id} red-zone credit ×{credit:F2} ({site.Containment})");
                    }
                    else if (after.ToString() != "Red" && site.RedZoneCreditApplied)
                    {
                        double credit = Farm.ContainmentTech.MabCreditFor(site.Containment, true);
                        site.MABTonnes /= credit;
                        site.RedZoneCreditApplied = false;
                    }
                }
            }
        }

        /// <summary>
        /// P8: approximate tidal flow (m/s) at a site from its zone's current
        /// speed, falling back to a sensible default when no zone is found.
        /// </summary>
        private double SiteFlowMs(Farm.SeaSite site) =>
            State.Zones.FirstOrDefault(z => z.Id == site.ZoneId)?.CurrentSpeed
                ?? Farm.BarrierTech.DefaultFlowMs;

        /// <summary>
        /// Weekly preventive coverage (Phase 1.5 + 4.1): fish-weighted effective
        /// lice exclusion combining skirt toggles and containment levels.
        /// Per-site: 1-(1-skirt*0.7)*(1-exclusion). Drives settlement reduction.
        /// P8 adds a flow-dependent permeable-barrier factor (no-op when none).
        /// </summary>
        private void UpdatePreventiveCoverage()
        {
            if (SeaLice == null) return;
            double total = 0, covered = 0;
            foreach (var site in State.SeaSites)
            {
                int fish = site.Pens.SelectMany(p => p.Cohorts).Sum(c => c.Count);
                total += fish;
                double skirt = site.HasLiceSkirt ? 0.70 : 0.0;
                double excl = Farm.ContainmentTech.ModifiersFor(site.Containment).LiceExclusion01;

                // P8: a permeable barrier blocks a flow-dependent share of new
                // chalimus settlement. Guarded: no barrier installed leaves the
                // existing skirt/containment formula exactly as before.
                double barrier = 0.0;
                if (site.Barrier != Farm.BarrierKind.None)
                {
                    barrier = Farm.BarrierTech.EffectiveLiceBlocking(
                        site.Barrier, SiteFlowMs(site), site.BarrierDaysSinceCleaning);

                    // Periodic cleaning requirement: uncleaned barriers lose
                    // efficiency (handled above), and cleaning costs standing opex.
                    site.BarrierDaysSinceCleaning += BARRIER_CLEANING_TICK_DAYS;
                    decimal barrierOpex = Farm.BarrierTech.SpecFor(site.Barrier).WeeklyOpexNok;
                    if (barrierOpex > 0)
                    {
                        if (!FinanceManager.Spend(barrierOpex))
                            FinanceManager.ForceSpend(barrierOpex);
                        FinancialStatements?.RecordOperatingExpense(barrierOpex);
                    }
                }

                covered += fish * (1.0 - (1.0 - skirt) * (1.0 - excl) * (1.0 - barrier));

                // Phase 4.1: standing energy opex per containment level.
                decimal opex = Farm.ContainmentTech.ModifiersFor(site.Containment).WeeklyOpexNok;
                if (opex > 0)
                {
                    if (!FinanceManager.Spend(opex))
                        FinanceManager.ForceSpend(opex);
                    FinancialStatements?.RecordOperatingExpense(opex);
                }
            }
            SeaLice.PreventiveCoverage01 = total > 0 ? covered / total : 0.0;
        }

        /// <summary>
        /// Weekly bloom roll + damage (Phase 4.3). Spawns seasonal events per
        /// zone, ticks gill damage + mortality on afflicted sites, heals clean
        /// sites, expires old blooms, charges countermeasure opex.
        /// </summary>
        private void RollBloomWeek()
        {
            int dayOfYear = State.Day % 365;
            double temp = Weather?.CurrentTemperatureC ?? 8.0;
            bool calm = Weather == null || Weather.CurrentState.ToString() == "Calm"
                || Weather.CurrentState.ToString() == "Moderate";
            bool vet = Core.CrewEffects.HasRole(State, "veterinaer");

            // Spawn per zone.
            foreach (var zone in State.Zones)
            {
                var sites = State.SeaSites
                    .Where(s => s.ZoneId == zone.Id && s.Pens.Any(p => p.TotalCount > 0))
                    .ToList();
                if (sites.Count == 0) continue;
                var site = sites[_rng.Next(sites.Count)];

                TrySpawnBloom(site, "jellyfish", dayOfYear,
                    LakskongenSim.Environment.BloomEventDeck.InJellySeason(dayOfYear) ? 0.10 : 0.0);
                TrySpawnBloom(site, "algae", dayOfYear,
                    LakskongenSim.Environment.BloomEventDeck.InAlgaeSeason(dayOfYear) && temp > 10.0 ? 0.12 : 0.0);
                TrySpawnBloom(site, "hypoxia", dayOfYear,
                    temp > 14.0 && calm ? 0.08 : 0.0);
            }

            // Tick damage + recovery + expiry.
            foreach (var bloom in State.ActiveBlooms.ToList())
            {
                var site = State.SeaSites.FirstOrDefault(s => s.Id == bloom.SiteId);
                if (site == null || bloom.IsExpired(State.Day))
                {
                    State.ActiveBlooms.Remove(bloom);
                    continue;
                }
                double cm = LakskongenSim.Environment.BloomEventDeck.CounterMult(site, bloom.Kind)
                    * LakskongenSim.Environment.BloomEventDeck.ContainmentMult(site, bloom.Kind)
                    * (vet ? 0.8 : 1.0);

                // P8: a permeable barrier also excludes jellyfish, reducing the
                // bloom's counter-measure multiplier by the flow-dependent
                // jellyfish blocking. Guarded: no barrier leaves cm unchanged.
                if (bloom.Kind == "jellyfish" && site.Barrier != Farm.BarrierKind.None)
                {
                    double jellyBlock = Farm.BarrierTech.EffectiveJellyfishBlocking(
                        site.Barrier, SiteFlowMs(site), site.BarrierDaysSinceCleaning);
                    cm *= 1.0 - Math.Clamp(jellyBlock, 0.0, 1.0);
                }
                if (cm <= 0.0) continue; // fully dodged (contained)
                double gillDmg = bloom.Kind switch
                {
                    "jellyfish" => LakskongenSim.Environment.BloomEventDeck.JellyGillPerWeek,
                    "algae" => LakskongenSim.Environment.BloomEventDeck.AlgaeGillPerWeek,
                    _ => LakskongenSim.Environment.BloomEventDeck.HypoxiaGillPerWeek
                } * bloom.Severity01 * cm;
                site.GillScore = Math.Max(0.0, site.GillScore - gillDmg);

                double mortRate = bloom.Kind switch
                {
                    "jellyfish" => 0.05, "algae" => 0.07, _ => 0.03
                } * bloom.Severity01 * cm;

                // P11: an installed bloom mitigation blunts mortality and incurs
                // an incident deployment/operating cost. No installed kit keeps
                // the baseline behaviour unchanged.
                var mitigation = ChooseBloomMitigation(site, bloom.Kind);
                if (mitigation != MitigationKind.None)
                {
                    var outcome = BloomMitigation.MitigationEffectiveness(
                        mitigation, bloom.Severity01, bloom.Kind);
                    mortRate *= 1.0 - Math.Clamp(outcome.MortalityReductionFraction, 0.0, 1.0);
                    if (outcome.CostNok > 0m)
                    {
                        if (!FinanceManager.Spend(outcome.CostNok))
                            FinanceManager.ForceSpend(outcome.CostNok);
                        FinancialStatements?.RecordOperatingExpense(outcome.CostNok);
                    }
                }

                int killed = 0;
                double lostKg = 0.0;
                foreach (var pop in site.Pens)
                    foreach (var cohort in pop.Cohorts)
                    {
                        int take = (int)(cohort.Count * mortRate);
                        cohort.Count = Math.Max(0, cohort.Count - take);
                        killed += take;
                        lostKg += take * cohort.MeanWeightKg;
                    }
                if (killed > 0)
                {
                    SimLog.Info($"🪼 Bloom {bloom.Kind} killed {killed:N0} at {site.Id} (gill {site.GillScore:F0})");

                    // P11: catastrophe insurance payout on the realized bloom loss.
                    decimal bloomPrice = MarketSimulation?.PricePerKg ?? _balancing.BaseSalmonPricePerKg;
                    PayInsurance(
                        InsuranceKind.Bloom,
                        (decimal)lostKg * bloomPrice,
                        $"Oppblomstring ({bloom.Kind}) ved {site.Id}",
                        EventCategory.PlanktonBloom);
                }
            }

            // Recovery on clean sites.
            foreach (var site in State.SeaSites)
            {
                if (!State.ActiveBlooms.Any(b => b.SiteId == site.Id))
                    site.GillScore = Math.Min(100.0, site.GillScore + LakskongenSim.Environment.BloomEventDeck.GillRecoveryPerWeek);
                // Countermeasure standing opex.
                decimal opex = (site.HasBubbleCurtain ? 90_000m : 0m) + (site.HasO2Diffuser ? 120_000m : 0m);
                if (opex > 0)
                {
                    if (!FinanceManager.Spend(opex))
                        FinanceManager.ForceSpend(opex);
                    FinancialStatements?.RecordOperatingExpense(opex);
                }
            }
        }

        private void TrySpawnBloom(Farm.SeaSite site, string kind, int dayOfYear, double p)
        {
            if (p <= 0.0 || _rng.NextDouble() >= p) return;
            if (State.ActiveBlooms.Any(b => b.SiteId == site.Id && b.Kind == kind)) return;
            var bloom = new LakskongenSim.Environment.BloomEvent
            {
                Kind = kind, SiteId = site.Id,
                Severity01 = 0.4 + _rng.NextDouble() * 0.6,
                StartDay = State.Day, DurationDays = 7 + _rng.Next(15)
            };
            State.ActiveBlooms.Add(bloom);
            State.EventLog.Add(new Events.EventRecord
            {
                Day = State.Day,
                Name = kind == "jellyfish" ? "Manetoppblomstring" : kind == "algae" ? "Algeoppblomstring" : "Oksygenfall",
                Category = Events.EventCategory.Weather,
                Description = $"Oppblomstring ({bloom.Kind}) ved {site.Id} — sjekk gjeller, vurder mottiltak",
                BalanceChange = 0m
            });
            State.TrimEventLog();
            SimLog.Info($"🪼 Bloom {kind} @ {site.Id} (sev {bloom.Severity01:F2})");
        }

        /// <summary>
        /// P11: pick the mitigation actually installed at a site that best
        /// addresses <paramref name="bloomKind"/>. Prefers the specialist
        /// (<see cref="BloomMitigation.BestFor"/>), then falls back to whichever
        /// flag-backed intervention is installed (target-aware effectiveness
        /// discounts a mismatched tool). Returns <see cref="MitigationKind.None"/>
        /// when nothing is installed.
        /// </summary>
        private static MitigationKind ChooseBloomMitigation(Farm.SeaSite? site, string bloomKind)
        {
            if (site == null) return MitigationKind.None;
            var best = BloomMitigation.BestFor(bloomKind);
            if (best != MitigationKind.None && BloomMitigation.IsInstalled(best, site))
                return best;
            if (BloomMitigation.IsInstalled(MitigationKind.O2Diffuser, site))
                return MitigationKind.O2Diffuser;
            if (BloomMitigation.IsInstalled(MitigationKind.BubbleCurtain, site))
                return MitigationKind.BubbleCurtain;
            return MitigationKind.None;
        }

        /// <summary>
        /// P11: settle a catastrophe insurance claim. Computes the payout for
        /// <paramref name="kind"/> against the realized <paramref name="lossNok"/>,
        /// books it as revenue and logs an event. No-op when there is no policy
        /// or the loss is fully retained (below deductible).
        /// </summary>
        private void PayInsurance(InsuranceKind kind, decimal lossNok, string cause, EventCategory category)
        {
            if (Insurance == null || lossNok <= 0m) return;
            decimal payout = Insurance.ComputePayout(kind, lossNok);
            if (payout <= 0m) return;

            FinanceManager?.AddRevenue(payout);
            FinancialStatements?.RecordRevenue(payout);

            State.EventLog.Add(new Events.EventRecord
            {
                Day = State.Day,
                Id = $"insurance-{kind.ToString().ToLowerInvariant()}",
                Name = "Forsikringsoppgjør",
                Category = category,
                Description = $"Forsikring ({kind}) dekket {payout:N0} av {lossNok:N0} NOK tap — {cause}",
                BalanceChange = payout
            });
            State.TrimEventLog();
            OnEventTriggered?.Invoke($"🛡️ Forsikringsutbetaling ({kind}): {payout:N0} NOK");
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

            // P9/P11: Sync insurance, resource-rent tax, pen-price council and
            // bloom mitigations so top-tier depth state survives saves.
            SyncDepthSystemsToState();
        }

        /// <summary>
        /// P9/P11: Sync the depth systems into GameState for serialization.
        /// Insurance policies, resource-rent opt-in/paid total, the last Price
        /// Council pen price, and (GameBridge-managed) bloom mitigations.
        /// </summary>
        public void SyncDepthSystemsToState()
        {
            SyncInsuranceToState();
            State.EnableResourceRentTax = EnableResourceRentTax;
            State.ResourceRentPaid = ResourceRentPaid;
            State.PenPriceCouncilLastValue = PenPriceCouncil?.MonthlyPenPrice ?? 0m;
        }

        /// <summary>P11: mirror live insurance policies into GameState.</summary>
        public void SyncInsuranceToState()
        {
            State.InsurancePolicies = Insurance?.Policies
                .Select(p => new InsurancePolicyState
                {
                    Kind = p.Kind.ToString(),
                    Premium = (double)p.PremiumNokPerYear,
                    Coverage = p.CoverageFraction,
                    Deductible = p.DeductibleNok
                }).ToList() ?? new List<InsurancePolicyState>();
        }

        /// <summary>
        /// P11: rebuild live insurance policies from GameState. Backward-safe:
        /// null/absent list clears policies (matches pre-persistence behavior);
        /// unknown kinds and non-finite numbers are skipped/sanitized.
        /// </summary>
        public void RestoreInsuranceFromState(GameState loadedState)
        {
            Insurance ??= new Finance.InsuranceSystem();
            Insurance.ClearPolicies();
            if (loadedState?.InsurancePolicies == null) return;

            foreach (var p in loadedState.InsurancePolicies)
            {
                if (p == null || string.IsNullOrWhiteSpace(p.Kind)) continue;
                if (!Enum.TryParse<InsuranceKind>(p.Kind, ignoreCase: true, out var kind)) continue;

                double coverage = double.IsNaN(p.Coverage) || double.IsInfinity(p.Coverage)
                    ? 0.0
                    : p.Coverage;
                double rawPremium = double.IsNaN(p.Premium) || double.IsInfinity(p.Premium)
                    ? 0.0
                    : p.Premium;
                decimal premium = rawPremium > 0.0 ? (decimal)rawPremium : 0m;
                decimal deductible = p.Deductible > 0m ? p.Deductible : 0m;

                Insurance.Subscribe(new InsurancePolicy(kind, premium, coverage, deductible));
            }
        }

        /// <summary>
        /// P9/P11: restore insurance, resource-rent tax, Price Council price and
        /// the bloom-mitigation mirror from a loaded GameState.
        /// </summary>
        private void RestoreDepthSystemsFromState(GameState loadedState)
        {
            RestoreInsuranceFromState(loadedState);

            EnableResourceRentTax = loadedState.EnableResourceRentTax;
            ResourceRentPaid = loadedState.ResourceRentPaid > 0m ? loadedState.ResourceRentPaid : 0m;
            State.EnableResourceRentTax = EnableResourceRentTax;
            State.ResourceRentPaid = ResourceRentPaid;

            if (PenPriceCouncil != null && loadedState.PenPriceCouncilLastValue > 0m)
            {
                PenPriceCouncil.Reset();
                PenPriceCouncil.Update(loadedState.PenPriceCouncilLastValue);
            }
            State.PenPriceCouncilLastValue = PenPriceCouncil?.MonthlyPenPrice ?? 0m;

            // Bloom mitigations are mirrored here; GameBridge repopulates its
            // live registry from State.BloomMitigations on load.
            State.BloomMitigations = loadedState.BloomMitigations != null
                ? new List<BloomMitigationState>(loadedState.BloomMitigations)
                : new List<BloomMitigationState>();
            State.InsurancePolicies = loadedState.InsurancePolicies != null
                ? new List<InsurancePolicyState>(loadedState.InsurancePolicies)
                : new List<InsurancePolicyState>();
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
                        EBITDA = c.EBITDA,
                        MarketCap = c.MarketCap,
                        DebtLevel = c.DebtLevel,
                        CashReserves = c.CashReserves,
                        LicenseCount = c.LicenseCount,
                        TotalMABTonnes = c.TotalMABTonnes,
                        SiteCount = c.SiteCount,
                        MarketShareNorway = c.MarketShareNorway,
                        MarketShareGlobal = c.MarketShareGlobal,
                        WeeklyHarvestTonnes = c.WeeklyHarvestTonnes,
                        IsDistressed = c.IsDistressed,
                        IsAcquisitionTarget = c.IsAcquisitionTarget,
                        AcquisitionPrice = c.AcquisitionPrice
                    }).ToList();
            }

            // 2. EmpireProgression - phase, features, milestone/victory unlocks
            if (EmpireProgression != null)
            {
                State.EmpireProgression = new EmpireProgressionState
                {
                    CurrentPhase = EmpireProgression.CurrentPhase.ToString(),
                    UnlockedFeatures = EmpireProgression.UnlockedFeatures.ToList(),
                    UnlockedMilestoneIds = EmpireProgression.Milestones
                        .Where(m => m.IsUnlocked).Select(m => m.Id).ToList(),
                    AchievedVictoryIds = EmpireProgression.VictoryConditions
                        .Where(v => v.IsAchieved).Select(v => v.Id).ToList()
                };
            }

            // 3. StockMarket — live cap-table state (private setters; see RestoreState)
            if (StockMarket != null)
            {
                State.StockMarket = new StockMarketState
                {
                    Status = StockMarket.Status.ToString(),
                    SharePrice = StockMarket.SharePrice,
                    PriceHistory = StockMarket.PriceHistory.Select(e => e.NewPrice).ToList(),
                    OutstandingShares = (int)StockMarket.TotalShares
                };
            }

            // 4. BrandReputation — public setters, direct map
            if (BrandReputation != null)
            {
                State.BrandReputation = new BrandReputationState
                {
                    BrandName = BrandReputation.BrandName,
                    Strategy = BrandReputation.Strategy.ToString(),
                    QualityReputation = BrandReputation.QualityReputation,
                    SustainabilityReputation = BrandReputation.SustainabilityReputation,
                    Certifications = new HashSet<string>(BrandReputation.Certifications)
                };
            }

            // 5. CrisisCapitalism — active crises only (expired ones drop naturally)
            if (CrisisCapitalism != null)
            {
                State.ActiveCrises = CrisisCapitalism.GetActiveCrises()
                    .Select(c => new CrisisEventState
                    {
                        Id = c.Id,
                        Type = c.Type.ToString(),
                        AffectedCompetitorId = c.AffectedCompetitorId,
                        SeverityScore = c.SeverityScore,
                        DurationMonths = c.DurationMonths,
                        StartDate = c.StartDate
                    }).ToList();
            }

            // 6. VerticalIntegration — owned tiers + score
            if (VerticalIntegration != null)
            {
                var owned = VerticalIntegration.GetOwnedTiers();
                State.VerticalIntegration = new VerticalIntegrationState
                {
                    OwnsFeedMill = owned.Contains(Expansion.IntegrationTier.FEED_MILL),
                    OwnsHatchery = owned.Contains(Expansion.IntegrationTier.HATCHERY),
                    OwnsProcessing = owned.Contains(Expansion.IntegrationTier.PROCESSING),
                    OwnsColdChain = owned.Contains(Expansion.IntegrationTier.COLD_CHAIN),
                    OwnsDistribution = owned.Contains(Expansion.IntegrationTier.DISTRIBUTION),
                    IntegrationScore = VerticalIntegration.GetIntegrationScore()
                };
            }

            // 7. InvestorSystem — aggregates (no per-investor records exist yet;
            // DTO keeps the list slot for a future cap-table implementation)
            if (InvestorSystem != null)
            {
                State.InvestorSystem = new InvestorSystemState
                {
                    OutstandingShares = (int)InvestorSystem.OutstandingShares,
                    OutstandingBondPrincipal = InvestorSystem.OutstandingBondPrincipal,
                    Investors = new List<InvestorRecord>()
                };
            }

            // 8. StakeholderFactionSystem — RAW relationship scores by faction
            // (not weighted: Influence is a constant per faction, re-applied live)
            if (StakeholderFactionSystem != null)
            {
                State.FactionRelations = StakeholderFactionSystem.GetAllFactions()
                    .ToDictionary(kvp => kvp.Key.ToString(), kvp => (decimal)kvp.Value.RelationshipScore);
            }

            // 9. FishWelfare — single condition score
            if (FishWelfare != null)
            {
                State.FishWelfareCondition = (decimal)FishWelfare.ConditionScore;
            }

            // 10. VisualMilestones — achieved type names (dates not persisted)
            if (VisualMilestones != null)
            {
                State.AchievedVisualMilestones = VisualMilestones.GetAchievedMilestones()
                    .Select(m => m.Type.ToString()).ToHashSet();
            }

            // 11. TD-04: Growth/Mortality model snapshots so loaded games
            // resume disease + chronic stress pressure exactly.
            if (Growth != null)
                State.GrowthState = Growth.GetSaveSnapshot();
            if (Mortality != null)
                State.MortalityState = Mortality.GetSaveSnapshot();

            // PRESERVED (never cleared): ActiveLoans, TaxHistory, Acquisitions,
            // Licenses, RecentMarketReports. These DTO slots have no live runtime
            // counterpart yet (LoanSystem/TaxSystem/LicenseSystem are stateless
            // calculators; AcquisitionSystem tracks targets, not owned deals;
            // no market-report producer exists). Loaded values round-trip
            // untouched until the systems grow live state — then wire them here.
        }

        /// <summary>
        /// Restore state after loading by syncing GameState.Balance to FinanceManager.Balance
        /// and re-injecting dependencies into deserialized objects.
        /// Call this AFTER SaveLoadManager.LoadGame() with the loaded state.
        /// </summary>
        public void RestoreFromLoad(GameState loadedState)
        {
            // 0. S2: the Godot save path deserializes GameState directly
            // (not via the library SaveLoadManager), so re-link here too:
            // SeaSite.Pens and State.Populations must share instances.
            GameStateRelinker.RelinkIds(loadedState);

            // 1. Restore balance from saved state (always assign — a bankrupt
            // or zero save must restore exactly that, not keep live cash).
            #pragma warning disable CS0618 // Type or member is obsolete
            FinanceManager = new Finance.FinanceManager(loadedState.Balance);
            #pragma warning restore CS0618

            // 2. Re-inject GrowthModel and MortalityModel into all FishPopulations
            foreach (var pop in loadedState.Populations)
            {
                pop.InjectDependencies(Growth, Mortality);
            }

            // TD-04: resume model internals (disease rates, chronic stress).
            // Null-safe for saves written before snapshots existed.
            Growth?.RestoreFromSave(loadedState.GrowthState);
            Mortality?.RestoreFromSave(loadedState.MortalityState);

            // Phase 1.2: NetIntegrityPct defaults to 100 for new sites; saves
            // written before the field existed deserialize as 0 — migrate.
            // Phase 4.1: containment migration (skirt-owners → 1, closed → 3).
            foreach (var site in loadedState.SeaSites)
            {
                if (site.NetIntegrityPct <= 0.0)
                    site.NetIntegrityPct = 100.0;
                if (site.Containment == Farm.ContainmentLevel.OpenNet)
                {
                    if (site.IsClosedContainment)
                        site.Containment = Farm.ContainmentLevel.ClosedFloat;
                    else if (site.HasLiceSkirt)
                        site.Containment = Farm.ContainmentLevel.Skirt;
                }
            }

            // T74: Restore all empire systems from loaded state
            RestoreEmpireSystemsFromState(loadedState);

            // P9/P11: Restore insurance, resource-rent tax and pen-price state
            RestoreDepthSystemsFromState(loadedState);

            SimLog.Info($"✅ Restored from save: Balance={FinanceManager.Balance:N0} NOK, {loadedState.Populations.Count} populations, Day {loadedState.Day}");
        }

        /// <summary>
        /// Restore all empire systems from loaded GameState (T74).
        /// Backward-compatible: gracefully handles old saves without new fields.
        /// Live-system rehydration is exact and side-effect-free (no cash
        /// movement, no events). Competitors match by stable parody ID onto the
        /// procedurally spawned roster; unknown IDs are ignored.
        /// </summary>
        private void RestoreEmpireSystemsFromState(GameState loadedState)
        {
            if (loadedState.CompetitorCompanies?.Count > 0)
            {
                SimLog.Info($"✓ Loaded {loadedState.CompetitorCompanies.Count} competitor companies from save");
                if (CompetitorAI != null)
                {
                    var roster = CompetitorAI.Competitors.ToDictionary(c => c.Id);
                    foreach (var saved in loadedState.CompetitorCompanies)
                    {
                        if (!roster.TryGetValue(saved.Id, out var live)) continue;
                        live.Revenue = saved.Revenue;
                        live.EBITDA = saved.EBITDA;
                        live.MarketCap = saved.MarketCap;
                        live.DebtLevel = saved.DebtLevel;
                        live.CashReserves = saved.CashReserves;
                        live.LicenseCount = saved.LicenseCount;
                        live.TotalMABTonnes = saved.TotalMABTonnes;
                        live.SiteCount = saved.SiteCount;
                        live.MarketShareNorway = saved.MarketShareNorway;
                        live.MarketShareGlobal = saved.MarketShareGlobal;
                        live.WeeklyHarvestTonnes = saved.WeeklyHarvestTonnes;
                        live.IsDistressed = saved.IsDistressed;
                        live.IsAcquisitionTarget = saved.IsAcquisitionTarget;
                        live.AcquisitionPrice = saved.AcquisitionPrice;
                    }
                }
            }

            if (loadedState.EmpireProgression != null)
            {
                SimLog.Info($"✓ Loaded empire progression: {loadedState.EmpireProgression.CurrentPhase}");
                if (EmpireProgression != null &&
                    Enum.TryParse<Finance.EmpirePhase>(loadedState.EmpireProgression.CurrentPhase, out var phase))
                {
                    EmpireProgression.RestoreState(phase, loadedState.EmpireProgression.UnlockedFeatures,
                        loadedState.EmpireProgression.UnlockedMilestoneIds,
                        loadedState.EmpireProgression.AchievedVictoryIds);
                }
            }

            // StockMarket — exact restore via system API
            if (loadedState.StockMarket != null && StockMarket != null)
            {
                if (Enum.TryParse<Finance.OwnershipStatus>(loadedState.StockMarket.Status, out var status))
                    StockMarket.RestoreState(status, loadedState.StockMarket.SharePrice,
                        loadedState.StockMarket.OutstandingShares, loadedState.StockMarket.PriceHistory);
            }

            // BrandReputation — public setters, direct restore
            if (loadedState.BrandReputation != null && BrandReputation != null)
            {
                BrandReputation.BrandName = loadedState.BrandReputation.BrandName;
                if (Enum.TryParse<Processing.BrandStrategy>(loadedState.BrandReputation.Strategy, out var strategy))
                    BrandReputation.Strategy = strategy;
                BrandReputation.QualityReputation = loadedState.BrandReputation.QualityReputation;
                BrandReputation.SustainabilityReputation = loadedState.BrandReputation.SustainabilityReputation;
                BrandReputation.Certifications = new HashSet<string>(loadedState.BrandReputation.Certifications);
            }

            // Active crises — re-registered silently (expired ones skipped)
            if (loadedState.ActiveCrises?.Count > 0 && CrisisCapitalism != null)
            {
                foreach (var c in loadedState.ActiveCrises)
                {
                    if (!Enum.TryParse<Events.CrisisType>(c.Type, out var type)) continue;
                    CrisisCapitalism.RestoreCrisis(new Events.CrisisEvent
                    {
                        Id = c.Id,
                        Type = type,
                        AffectedCompetitorId = c.AffectedCompetitorId,
                        SeverityScore = c.SeverityScore,
                        DurationMonths = c.DurationMonths,
                        StartDate = c.StartDate
                    });
                }
            }

            // VerticalIntegration — re-mark owned tiers (no CAPEX, no events)
            if (loadedState.VerticalIntegration != null && VerticalIntegration != null)
            {
                var vi = loadedState.VerticalIntegration;
                if (vi.OwnsFeedMill) VerticalIntegration.RestoreOwnedTier(Expansion.IntegrationTier.FEED_MILL);
                if (vi.OwnsHatchery) VerticalIntegration.RestoreOwnedTier(Expansion.IntegrationTier.HATCHERY);
                if (vi.OwnsProcessing) VerticalIntegration.RestoreOwnedTier(Expansion.IntegrationTier.PROCESSING);
                if (vi.OwnsColdChain) VerticalIntegration.RestoreOwnedTier(Expansion.IntegrationTier.COLD_CHAIN);
                if (vi.OwnsDistribution) VerticalIntegration.RestoreOwnedTier(Expansion.IntegrationTier.DISTRIBUTION);
            }

            // InvestorSystem — exact aggregates via system API
            if (loadedState.InvestorSystem != null && InvestorSystem != null)
            {
                InvestorSystem.RestoreState(loadedState.InvestorSystem.OutstandingShares,
                    loadedState.InvestorSystem.OutstandingBondPrincipal);
            }

            // Faction relations — direct score restore (no delta replay)
            if (loadedState.FactionRelations?.Count > 0 && StakeholderFactionSystem != null)
            {
                foreach (var kvp in loadedState.FactionRelations)
                {
                    if (!Enum.TryParse<Politics.FactionType>(kvp.Key, out var faction)) continue;
                    var f = StakeholderFactionSystem.GetFaction(faction);
                    if (f != null) f.RelationshipScore = (float)kvp.Value;
                }
            }

            // FishWelfare — delta restore (fires normal condition event for UI)
            if (FishWelfare != null)
            {
                FishWelfare.ModifyCondition((double)loadedState.FishWelfareCondition - FishWelfare.ConditionScore);
            }

            // VisualMilestones — silent re-mark (no events, no cinematics)
            if (loadedState.AchievedVisualMilestones?.Count > 0 && VisualMilestones != null)
            {
                VisualMilestones.RestoreAchieved(loadedState.AchievedVisualMilestones);
            }

            // Old saves without these fields load fine with default values
        }
    }
}