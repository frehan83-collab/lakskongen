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
        public SimRng Rng { get; private set; } = new SimRng(0);

        /// <summary>Drop all run state so a new game never inherits zones,
        /// sites, fish, event log or interval clocks from the previous one
        /// (S9). Called at the top of Initialize and directly by New Game.</summary>
        public void Reset()
        {
            Storyteller?.Detach();
            Storyteller = null!;
            State = new GameState();
            Rng = new SimRng(0);
            _dayNotes.Clear();
            // Run state that lives on the engine rather than in GameState (F292).
            PenPriceCouncil = new Market.PriceCouncil(DefaultPenPriceNokPerKg);
            ResourceRentPaid = 0m;
            EnableResourceRentTax = true;   // D16: on by default
            StormEscapeEvents = 0.0;
            _lastWeatherState = LakskongenSim.Environment.WeatherState.Calm;
            _stormEpisodePeakSeverity = 0.0;
            Time.Reset();
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
        public Market.PriceCouncil PenPriceCouncil { get; private set; } = new(DefaultPenPriceNokPerKg);
        private const decimal DefaultPenPriceNokPerKg = 82m;
        /// <summary>
        /// P9: when true, the 25% resource-rent tax is charged at the year-end
        /// settlement. On by default (owner decision D16, S1-03): the 70M NOK
        /// basic deduction shields family farms, so only large operations pay.
        /// A scenario can opt out (enable_resource_rent_tax=false).
        /// </summary>
        public bool EnableResourceRentTax { get; set; } = true;
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
        public Finance.CorporateFinanceSystem CorporateFinance { get; private set; } = null!;
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
        // Facilities live in GameState (the one serialized owner) so a build
        // survives save/load (F1069); these are views, not parallel stores.
        public List<RASFacility> RASFacilities => State.RASFacilities;
        public List<OffshoreFarm> OffshoreFarms => State.OffshoreFarms;
        public AIFeedingSystem AIFeeding { get; private set; } = null!;
        public List<PostSmoltFacility> PostSmoltFacilities => State.PostSmoltFacilities;

        // ─── Update intervals ───
        private const int SUSTAINABILITY_UPDATE_INTERVAL = 7; // Weekly
        private const int TRAFFIC_LIGHT_UPDATE_INTERVAL = 7; // Weekly (Phase 1.3)
        private const int BRAND_REPUTATION_UPDATE_INTERVAL = 7; // Weekly
        private const int CRISIS_CAPITALISM_UPDATE_INTERVAL = 7; // Weekly - crises don't happen daily
        private const int EMPIRE_PROGRESSION_UPDATE_INTERVAL = 7; // Weekly
        // Reserved for future systems (T68: not yet wired)
        // private int _lastAcquisitionSystemUpdate = 0;
        // private const int ACQUISITION_SYSTEM_UPDATE_INTERVAL = 7; // Weekly - M&A is slow
        // private int _lastLicenseSystemUpdate = 0;
        // private const int LICENSE_SYSTEM_UPDATE_INTERVAL = 30; // Monthly - license applications/renewals
        // private int _lastStakeholderFactionUpdate = 0;
        // private const int STAKEHOLDER_FACTION_UPDATE_INTERVAL = 7; // Weekly - faction relationship updates
        private const int MARKET_INTELLIGENCE_UPDATE_INTERVAL = 7; // Weekly - market analysis updates
        private const int BLOOM_UPDATE_INTERVAL = 7; // Weekly (Phase 4.3)
        private const int BARRIER_CLEANING_TICK_DAYS = 7; // P8: barrier cleaning decay cadence (weekly)
        private const int NIGHT_POLICY_INTERVAL = 1; // Daily (Phase 5.2)

        private BalancingConfig _balancing = new();
        // Phase A: single source of truth for balancing values.
        private CalibrationRegistry _calibration = CalibrationRegistry.Default;

        /// <summary>The calibration this engine runs on (read-only; the UI reads
        /// targets such as the FCR goal from here instead of its own constants).</summary>
        public CalibrationRegistry Calibration => _calibration;
        private bool _enableEvents = true;
        private Finance.FinanceManager? _financeManager;

        // ─── Farm systems daily OPEX (accumulated in TickDay, consumed in ProcessDailyFinance) ───
        internal decimal _dailyFarmSystemsOpex = 0m;
        internal decimal _dailyAiFeedingLicenseCost = 0m;

        // ─── Per-day biology call tuning (values unchanged; see GrowthModel docs) ───
        internal const double SdeTargetWeightKg = 5.5; // genetic asymptote K used by the daily SDE step
        internal const double SdeGeneticGrowthRateAlphaMax = 20.0; // α_max scale for SDE-only growth
        internal const double SdeIndividualVarianceSigma = 0.002; // σ kept small vs deterministic drift
        internal const double PenVolumeProxyM3 = 10_000.0; // pen-volume proxy for density estimate
        internal const double DefaultStockingDensityKgM3 = 20.0; // fallback when no zone is known
        internal const double WarmingPerYearC = 0.02; // P10: documented warming trend applied to lice development

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
        /// <summary>
        /// Routine net maintenance on a non-storm day. Re-fit with S1-02 (0.2 -> 0.5):
        /// the 0.2 was fitted to a weather chain that never reached Severe in
        /// Oct-Mar (F456). The honest chain wears nets ~57% more per year
        /// (~74 vs ~47 integrity points), and at 0.2 maintained nets fell below
        /// the 40% risk line within two years (escapes 173% of stock/site/year).
        /// 0.5 restores the measured maintained-net outcome of BALANCE.md §1c.
        /// </summary>
        private const double NetIntegrityRepairPctPerDay = 0.5;
        /// <summary>Expected containment-failure probability on a severe-storm day.</summary>
        private const double StormFailureProbabilityPerSevereDay = 0.01;
        /// <summary>Fraction of the pen released by one containment failure at full wear.</summary>
        private const double StormLossFractionPerFailure = 0.05;
        /// <summary>Documented 40% net-integrity risk line (hard breach).</summary>
        private const double NetIntegrityEscapeRiskThresholdPct = 40.0;
        /// <summary>Regulatory escape fine (NOK per escaped fish).</summary>
        private const decimal EscapeFineNOKPerFish = 100m;
        /// <summary>Quarterly fine while certification is NonCompliant (NOK).</summary>
        private const decimal NonComplianceFineNok = 50_000m;
        /// <summary>Freshwater-bath wellboat consumables per treatment (NOK).</summary>
        private const decimal FreshwaterBathConsumablesNok = 60_000m;
        /// <summary>Automated night-shift emergency oxygenation per event (NOK).</summary>
        private const decimal NightOxygenEmergencyNok = 12_000m;

        /// <summary>
        /// Cumulative expected storm containment-failure events recorded by the
        /// engine (all sites). Expected (not Bernoulli) so escape accounting is
        /// smooth and reproducible across seeds. Exposed for SimProbe.
        /// </summary>
        public double StormEscapeEvents { get; private set; } = 0.0;

        // ─── External data calibration ───
        private AquacultureDataConfig _aquacultureDataConfig = new();
        internal AquacultureGameplayModifiers _aquacultureModifiers = AquacultureGameplayModifiers.Default();
        private BarentsWatchClientOptions _barentsWatchClientOptions = new();
        /// <summary>The bundled offline Fishhealth snapshot read at Initialize (D18); null without one.</summary>
        private BarentsWatchStoredSnapshot? _offlineFishHealthSnapshot;
        private SimRngStream _rng = null!; // Engine-level event draws ("storyteller/engine" stream, seeded in Initialize)
        /// <summary>Event gating for tick systems (Phase 1 wave 4).</summary>
        internal bool EventsEnabled => _enableEvents;
        /// <summary>Legacy seeded event RNG (seed+101) for night-policy and bloom-deck systems.</summary>
        internal SimRngStream LegacyEventRng => _rng;
        /// <summary>Tick access to the balancing feed price (BalancingConfig itself stays private).</summary>
        internal decimal TickFeedCostPerKgNOK => _balancing.FeedCostPerKgNOK;
        /// <summary>Event fire wrapper for tick systems (events are +=/-= only outside the type).</summary>
        internal void FireEvent(string message) => OnEventTriggered?.Invoke(message);

        // Storm-impact edge tracking (moved from the bridge, Phase 1 wave 2).
        private LakskongenSim.Environment.WeatherState _lastWeatherState = LakskongenSim.Environment.WeatherState.Calm;
        /// <summary>Highest storm severity reached in the running episode (0 between episodes; saved).</summary>
        private double _stormEpisodePeakSeverity;
        private const double StormEpisodeSeverity = 0.5;
        private const double SevereEpisodeSeverity = 1.0;

        /// <summary>Damage severity of a weather state: Severe 1, Storm 0.5, otherwise 0.</summary>
        internal static double StormSeverity(LakskongenSim.Environment.WeatherState state) => state switch
        {
            LakskongenSim.Environment.WeatherState.Severe => SevereEpisodeSeverity,
            LakskongenSim.Environment.WeatherState.Storm => StormEpisodeSeverity,
            _ => 0.0
        };

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
            Rng = seed.HasValue ? new SimRng(seed.Value) : SimRng.CreateUnseeded();
            // P0-07: the state carries the seed its streams derive from, so a
            // save of ANY game (also one started without a seed) resumes on
            // the same streams. Callers no longer have to remember to set it.
            State.RandomSeed = Rng.Seed;
            _rng = Rng.Stream(SimRngStreams.Storyteller, "engine");
            
            // Create subsystems
            Weather = new WeatherSystem(Rng.Stream(SimRngStreams.Weather));
            Environment = new EnvironmentModel();
            Growth = new GrowthModel(Rng.Stream(SimRngStreams.Growth));
            Mortality = new MortalityModel();
            Feed = new FeedSystem();
            SeaLice = new SeaLiceModel();
            IntegratedDisease = new IntegratedDiseaseSystem(SeaLice,
                Rng.Stream(SimRngStreams.StochasticDisease),
                Rng.Stream(SimRngStreams.StochasticDisease, "lod"));
            // ONE population-level SIR: scenario seeding, treatments, the daily
            // tick and saves all act on the same prevalence map (F478/F244).
            Disease = IntegratedDisease.BaseSystem;

            // Wire disease systems to MortalityModel to prevent double mortality application
            IntegratedDisease.SetMortalityModel(Mortality);
            
            Hatchery = new Hatchery();
            Genetics = new Genetics(Rng.Stream(SimRngStreams.Genetics));

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
            CompetitorAI = new Finance.CompetitorAISystem(Rng.Stream(SimRngStreams.CompetitorAI));
            EmpireProgression = new Finance.EmpireProgressionSystem();
            // Traffic-light regulatory state machine. Constructed HERE (not
            // with the expansion systems below) because NorwegianEvents
            // .RegisterAll, called further down, needs the live instance (S9).
            TrafficLightSystem = new Finance.TrafficLightSystem();
            
            // A distressed rival puts its assets on the market (review F499: the
            // event had no subscriber, so no crisis ever opened and the distressed-
            // asset pipeline always saw empty lists).
            CompetitorAI.OnCompetitorDistressed += HandleCompetitorDistressed;

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
            
            // Always a fresh market: a retained instance would keep the previous
            // game's price path and an RNG stream from the previous SimRng (F292).
            ScenarioPriceMultiplier = 1.0; // scenario rules re-apply on scenario start / load
            MarketSimulation = new Market.MarketSimulation(
                Rng.Stream(SimRngStreams.Market),
                Rng.Stream(SimRngStreams.Intelligence, "market_inner"));
            ContractSystem = new ContractSystem(Rng.Stream(SimRngStreams.Market, "contracts"));
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
            Storyteller = new Storyteller(Time, State, FinanceManager, seed: seed,
                enableDefaultEvents: enableEvents, rng: Rng.Stream(SimRngStreams.Storyteller));
            if (enableEvents)
            {
                NorwegianEvents.RegisterAll(Storyteller, FinanceManager, TrafficLightSystem);
                RegisterPlanktonEvents();
            }
            Storyteller.OnEventRaised += HandleStorytellerEvent;
            // Event effects move cash inside the event; book exactly what moved.
            Storyteller.OnEventCashSettled += (_, delta) => BookSettledCash(delta, "events");
            // Event costs follow farm size up to one standard site (larger farms unchanged).
            Storyteller.CostScale = () => Math.Clamp(TotalSiteScale(), 1.0 / StandardPensPerSite, 1.0);

            // Initialize PlanktonBloom system
            PlanktonBloom = new PlanktonBloom(Rng.Stream(SimRngStreams.PlanktonBloom));
            LoadPlanktonBloomConfig();
            ApplyWaterOxygenCalibration(); // wave 2 sim-a-1: the crash's oxygen demand from the registry

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

            // Feed cost → finance (D1: the cash spend also books COGS so the P&L
            // finally sees the largest cost item — feed is ~35% of OPEX).
            // Feed is paid like every other operating cost: on credit when cash
            // is short. It used to be waived (booked as COGS, never paid), which
            // made deliberate debt a free-feed strategy (review F1130).
            Feed.OnFeedCostIncurred += cost =>
                PayCost(cost, CostBooking.Cogs, "feed",
                    $"⚠️ Insufficient funds for feed: {cost:N0} NOK paid on credit. Balance: {FinanceManager.Balance:N0} NOK");

            // Tutorial onboarding system
            Tutorial = new TutorialSystem(this);

            // Analytics tracker — shares GameState's DailySnapshots list
            Analytics = new AnalyticsTracker(State.DailySnapshots);

            // Audio state tracker — pure C#, no Godot; consumed via GameBridge → SimBridge
            Audio = new AudioManager(Weather);

            // Achievement system — initialized after State so conditions have full context
            Achievements = new AchievementSystem(State);
            // D16 (S1-03): achievements award medals, never cash.
            Achievements.OnAchievementUnlocked += a => OnAchievementUnlocked?.Invoke(a);

            // Research / Tech Tree — restore persisted state from GameState
            Research = new ResearchSystem();

            // StockMarket and BrandReputation systems
            StockMarket = new Finance.StockMarketSystem(Rng.Stream(SimRngStreams.StockMarket));
            BrandReputation = new BrandReputation
            {
                BrandName = "Lakskongen",
                Strategy = BrandStrategy.Premium
            };

            // CrisisCapitalism and VerticalIntegration systems
            CrisisCapitalism = new CrisisCapitalismSystem(Rng.Stream(SimRngStreams.CrisisCapitalism));
            VerticalIntegration = new VerticalIntegrationSystem(FinanceManager);
            VerticalIntegration.OnCapexPaid += capex => BookSettledCash(-capex, "vertical_integration", CostBooking.Capex);

            // Initialize financial systems (LoanSystem, TaxSystem, InvestorSystem)
            LoanSystem = new Finance.LoanSystem();
            TaxSystem = new Finance.TaxSystem();
            InvestorSystem = new Finance.InvestorSystem();
            CorporateFinance = new Finance.CorporateFinanceSystem(
                Rng.Stream(SimRngStreams.CorporateFinance));

            // Wire LoanSystem events
            LoanSystem.OnInterestCharged += interest =>
            {
                OnEventTriggered?.Invoke($"💰 Interest charged: {interest:N0} NOK");
            };

            // Wire TaxSystem events
            TaxSystem.OnTaxPaid += (tax, period) =>
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

            // Initialize new farm systems (facility lists live in the fresh State)
            AIFeeding = new AIFeedingSystem(Rng.Stream(SimRngStreams.AiFeeding));
            // In-day tick pipeline (Phase 1 wave 4): registration order IS
            // the simulation order. Rebuilt every Initialize => fresh clocks.
            _inDayPhases = new ITickSystem[]
            {
                new WeatherAndEnvironmentTickSystem(),
                new HealthPrepTickSystem(),
                new BiologyTickSystem(),
                new FarmOpexTickSystem(),
                new CarbonLedgerTickSystem(),
                new EventsAndTimeTickSystem(),
                new MarketTickSystem(),
                new FinanceTickSystem(),
                new StockMarketTickSystem(),
                new SustainabilityTickSystem(),
                new SludgeValorizationTickSystem(),
                new TrafficLightTickSystem(),
                new BrandReputationTickSystem(),
                new CompetitorAITickSystem(),
                new EmpireProgressionTickSystem(),
                new CrisisCapitalismTickSystem(),
                new VerticalIntegrationTickSystem(),
                new CorporateFinanceTickSystem(),
                new InvestorTickSystem(),
                new BloomDeckTickSystem(),
                new MarketIntelligenceTickSystem(),
                new ContractExpiryTickSystem(),
                new ProductMixOptimizerTickSystem(),
                new TutorialAnalyticsTickSystem(),
                new NightShiftPolicyTickSystem()
            };

            // Initialize expansion, license, and politics systems
            AcquisitionSystem = new Expansion.AcquisitionSystem(Rng.Stream(SimRngStreams.Acquisition));
            LicenseSystem = new Finance.LicenseSystem();
            LicenseAuctionSystem = new Finance.LicenseAuctionSystem(
                Rng.Stream(SimRngStreams.LicenseAuction));
            // NOTE: TrafficLightSystem is constructed near the top of
            // Initialize (NorwegianEvents.RegisterAll needs it). Gate stays
            // off by default: legacy event-only MAB behavior preserved until
            // explicitly enabled.
            StakeholderFactionSystem = new Politics.StakeholderFactionSystem(Rng.Stream(SimRngStreams.Factions));

            // Initialize Market Intelligence, Fish Welfare, and Visual Milestones systems
            MarketIntelligence = new Market.MarketIntelligenceSystem(
                Rng.Stream(SimRngStreams.Intelligence));
            FishWelfare = new Health.FishWelfare();
            VisualMilestones = new VisualMilestones();
            CurrencySystem = new Market.CurrencySystem(Rng.Stream(SimRngStreams.Currency));

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
                OnEventTriggered?.Invoke($"🎯 Milepæl: {title} — {description}");
                Audio?.TriggerAlert("achievement");
            };

            // Wire LicenseSystem events to FinanceManager
            LicenseSystem.OnLicenseCharged += (siteId, amount) =>
            {
                bool affordable = FinanceManager.CanAfford(amount);
                PayCost(amount, CostBooking.Opex, "licence_fees",
                    SimText.Nb($"⚠️ Kan ikke betale lisensavgiften for {SiteLabelOf(siteId)}: {amount:N0} kr. Saldo: {FinanceManager.Balance:N0} kr"));
                if (affordable)
                {
                    OnEventTriggered?.Invoke(SimText.Nb($"💰 Lisensavgift betalt for {SiteLabelOf(siteId)}: {amount:N0} kr"));
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

            // Phase 4.6: re-apply configured mods onto the fresh systems
            // (no-op when no mods directory has been set).
            ApplyMods();

            // Ledger baseline: the starting cash is the opening balance, not a flow.
            OpenBooks();

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
                // The bundled offline Fishhealth snapshot wins (D18: offline only).
                {
                    var stored = _offlineFishHealthSnapshot;
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

                // Roadmap D18: offline snapshots only. No token, no network and
                // no cache written next to the binary (the latest file in that
                // cache used to win, so two machines started different games).
                _barentsWatchClientOptions = new BarentsWatchClientOptions
                {
                    LocalSnapshotPath = localSnapshotCandidates.FirstOrDefault(File.Exists)
                };
                _offlineFishHealthSnapshot = null;

                if (!string.IsNullOrWhiteSpace(_barentsWatchClientOptions.LocalSnapshotPath))
                {
                    try
                    {
                        var stored = BarentsWatchFishHealthPipeline.ReadOffline(
                            new BarentsWatchFishHealthClient(_barentsWatchClientOptions));
                        _offlineFishHealthSnapshot = stored;
                        State.ExternalAquacultureSnapshot = stored.Normalized;
                        State.ExternalDataAnalytics = stored.Analytics;
                        State.ExternalHealthWarnings = stored.Warnings;
                        State.ExternalHealthCrisis = BarentsWatchHealthCrisisEngine.BuildScenario(stored.Normalized);
                        State.ExternalHealthAnalytics = BarentsWatchHealthCrisisEngine.BuildReport(stored.Normalized);
                        SimLog.Info($"✓ BarentsWatch Fishhealth offline snapshot {stored.Normalized.SnapshotYear}-W{stored.Normalized.SnapshotWeek:D2}");
                    }
                    catch (Exception ex)
                    {
                        SimLog.Warn($"⚠️  BarentsWatch offline snapshot unreadable, continuing with the default data: {ex.Message}");
                    }
                }
                else
                {
                    SimLog.Info("BarentsWatch: no bundled offline snapshot; external fish-health data stays at its defaults.");
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
            Weather?.ApplySeaTemperatureCalibration(_calibration.SeaTemperature);
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
                Name = "Planteplankton om våren",
                Category = EventCategory.PlanktonBloom,
                Weight = 1.5,
                CooldownDays = 60,
                MinDay = 60,
                MaxDay = 150,
                Condition = gs => gs.Zones != null && gs.Zones.Any(z => z.CurrentFishBiomassKg > 0) && gs.Day >= 60 && gs.Day <= 150,
                Apply = (gs, rec) =>
                {
                    rec.Description = "Mer lys og næringsrikt vann gir oppblomstring av planteplankton. Fare for oksygensvikt hvis oppblomstringen kollapser.";
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
                Name = "Planteplankton om høsten",
                Category = EventCategory.PlanktonBloom,
                Weight = 1.2,
                CooldownDays = 90,
                MinDay = 240,
                MaxDay = 330,
                Condition = gs => gs.Zones != null && gs.Zones.Any(z => z.CurrentFishBiomassKg > 0) && gs.Day >= 240 && gs.Day <= 330,
                Apply = (gs, rec) =>
                {
                    rec.Description = "Næringssalter og kaldere vann gir høstoppblomstring. Kortere dager gir større fare for kollaps.";
                    rec.BalanceChange = -75000m;
                    NorwegianEvents.SettleBalanceChange(_financeManager, gs, rec);
                }
            });

            // Plankton Crash (DO Crash)
            Storyteller.RegisterEvent(new EventDefinition
            {
                Id = "plankton-crash",
                Name = "Planktonkollaps",
                Category = EventCategory.PlanktonCrash,
                Weight = 0.8,
                CooldownDays = 30,
                MinDay = 1,
                MaxDay = 365,
                Condition = gs => gs.Zones != null && gs.Zones.Any(z => 
                    z.WaterChemistry != null && z.WaterChemistry.OxygenO2 < _calibration.Mortality.AcuteLowOxygenMgL), // DO below the acute limit
                Apply = (gs, rec) =>
                {
                    rec.Description = "Planteplankton dør i stor skala og bakteriene bruker opp oksygenet. Oksygenmetningen faller under 30 % og fisk dør.";
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
            
            var affectedZones = gs.Zones.Where(z => z.WaterChemistry != null && z.WaterChemistry.OxygenO2 < _calibration.Mortality.AcuteLowOxygenMgL).ToList();
            if (!affectedZones.Any()) return;

            foreach (var zone in affectedZones)
            {
                var populationsInZone = gs.Populations.Where(p => p.ZoneId == zone.Id).ToList();
                foreach (var pop in populationsInZone)
                {
                    // Mortality rate from the oxygen the fish actually sit in
                    // (S2-05): a sea pen has the zone's water; a land-based tank
                    // keeps its controlled oxygen and an offshore pen its
                    // current-fed boost, so a fjord crash no longer kills them.
                    var environment = ResolveCohortEnvironment(pop);
                    double mortalityRate = MortalityHazards.LowOxygenFraction(environment, _calibration.Mortality);
                    if (mortalityRate <= 0.0) continue;

                    for (int ci = 0; ci < pop.Cohorts.Count; ci++)
                    {
                        var cohort = pop.Cohorts[ci];
                        pop.RemoveDead(cohort, MortalityHazards.Deaths(cohort.Count, mortalityRate), MortalityCause.LowOxygen);
                    }

                    SimLog.Info($"Plankton crash mortality in zone {zone.Id}: DO={environment.OxygenMgL:F2} mg/L, rate={mortalityRate:P0}");
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

            // Tax rates come from calibration (the tax system's own defaults
            // were a second, unsynchronized copy).
            if (TaxSystem != null)
            {
                TaxSystem.CorporateTaxRate = (decimal)config.CorporateTaxRate;
                TaxSystem.ResourceRentTaxRate = (decimal)config.ResourceRentTaxRate;
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
                BankruptcyFloorNOK = c.Finance.BankruptcyFloorNOK,
                BankruptcyFloorByScenarioNOK = new Dictionary<string, decimal>(c.Finance.BankruptcyFloorByScenarioNOK),
                SolvencyWarningFractions = c.Finance.SolvencyWarningFractions.ToArray(),

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
            /// <summary>Default bankruptcy floor (NOK); see FinanceCalibration.BankruptcyFloorNOK.</summary>
            public decimal BankruptcyFloorNOK { get; set; } = -1_000_000m;
            /// <summary>Per-scenario bankruptcy floors (D16); see FinanceCalibration.</summary>
            public Dictionary<string, decimal> BankruptcyFloorByScenarioNOK { get; set; } = new();
            /// <summary>Warning ladder as fractions of the floor (ascending).</summary>
            public double[] SolvencyWarningFractions { get; set; } = { 0.0, 0.5, 0.8 };

            // ─── Licensing ───
            public decimal LicenseCostNOK { get; set; } = 150_000_000;

            // ─── Biology ───
            public int SmoltMaturationDays { get; set; } = 365;
            public int GrowthToHarvestDays { get; set; } = 540;
            public double TargetHarvestWeightKg { get; set; } = 5.2; // ↑ from 5.0
            public double MinHarvestWeightKg { get; set; } = 3.8; // ↑ from 3.5
            public double MaxHarvestWeightKg { get; set; } = 7.5; // ↑ from 7.0
            // One FCR target in the code base: the calibration registry (U1-03).
            public double OptimalFCR { get; set; } = CalibrationRegistry.Default.Biology.OptimalFCR;
            public double PoorFCR { get; set; } = CalibrationRegistry.Default.Biology.PoorFCR;
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
                BeginMortalityDay(); // S2-05: this step's deaths carry the day it ends on
                BeginHoldingDay();   // sim-a-1: every population's holding, resolved once for the step
                try
                {
                    AdvanceOneDay();
                    State.Day++; // Single source of truth for day counter

                    // Post-tick phases (ordered): storm impacts -> wellboat
                    // execution -> balance mirror. Registration order IS the
                    // execution order (see ITickSystem). Wellboat runs AFTER
                    // State.Day++ so the booking contract
                    // (DueDay = bookedDay + delay) stays exact.
                    foreach (var phase in _postTickPhases)
                        phase.Tick(this);

                    // Any cash that moved without a booking today is booked as
                    // unclassified, so the statements always reconcile with cash.
                    ReconcileUnbookedCash();
                }
                finally
                {
                    EndHoldingDay();
                }
            }

            OnDayAdvanced?.Invoke(State.Day);
        }

        // ─── Post-tick phase pipeline (Phase 1 wave 3) ────────────────────────

        private ITickSystem[] _inDayPhases = Array.Empty<ITickSystem>();

        /// <summary>Test observability for the migrated in-day pipeline (Phase 1 wave 5).</summary>
        internal ITickSystem[] InDayPhasesForTests => _inDayPhases;

        private ITickSystem[] _postTickPhases = new ITickSystem[]
        {
            new StormImpactsTickSystem(),
            new WellboatExecutionTickSystem(),
            new LicenseAuctionTickSystem(),
            new BalanceMirrorTickSystem(),
            new SolvencyTickSystem()
        };

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
        internal void ProcessDueWellboatJobs()
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
                double tempC = SeaTemperatureAtSite(site!);
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
                PayCost(FreshwaterBathConsumablesNok, CostBooking.Opex, "delousing"); // bath consumables
                message = SimText.Nb($"Ferskvannsbåten behandlet {SiteLabelOf(job.SiteId)} ({deaths:N0} døde under håndteringen, ingen resistens)");
            }
            else
            {
                message = $"Ferskvannsbåten kunne ikke behandle {SiteLabelOf(job.SiteId)}";
            }

            State.EventLog.Add(new Events.EventRecord
            {
                Day = State.Day,
                Name = ok ? "Ferskvannsbehandling" : "Brønnbåt returnerte",
                Category = Events.EventCategory.Disease,
                Description = message,
                BalanceChange = ok ? -FreshwaterBathConsumablesNok : 0m
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
                ? $"ROV på {site!.Label}: nøtene er renset (+15 %)"
                : $"ROV-en kunne ikke inspisere {SiteLabelOf(job.SiteId)}";
            if (ok)
            {
                site!.NetIntegrityPct = Math.Min(100.0, site.NetIntegrityPct + 15.0);
                int fish = site.Pens.SelectMany(p => p.Cohorts).Sum(c => c.Count);
                message = SimText.Nb($"ROV på {site.Label}: {fish:N0} fisk, {GetCompanyLiceAverage():F2} lus per fisk, gjeller {site.GillScore:F0}/100, nøter {site.NetIntegrityPct:F0} % (+15 renset), rømte fisk siste år: {site.EscapedFishInLastYear(State.Day)}");
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
            var cmd = job.Kind == "delouse"
                ? DelouseSite(job.SiteId, string.IsNullOrEmpty(job.Method) ? "hydrolicer" : job.Method)
                : HarvestSite(job.SiteId);

            State.EventLog.Add(new Events.EventRecord
            {
                Day = State.Day,
                Name = cmd.Ok ? "Brønnbåt ankommet" : "Brønnbåt returnerte",
                Category = Events.EventCategory.Equipment,
                Description = cmd.Ok
                    ? $"Brønnbåten utførte {WellboatJobName(job.Kind)} på {SiteLabelOf(job.SiteId)}"
                    : $"Brønnbåten kunne ikke utføre {WellboatJobName(job.Kind)} på {SiteLabelOf(job.SiteId)}",
                BalanceChange = 0m
            });
            _dayNotes.Add(new DayNote
            {
                Kind = job.Kind == "delouse" ? "wellboat_delouse" : "wellboat_harvest",
                SiteId = job.SiteId,
                Success = cmd.Ok,
                Message = cmd.Ok ? job.Kind : cmd.Message
            });
        }

        /// <summary>
        /// Seasonal external sea-lice pressure multiplier (neighbouring farms and
        /// wild salmon). Peaks in late summer, troughs in late winter; centred on
        /// <see cref="SeasonalLicePressureBase"/> so a normal cycle builds to the
        /// regulatory action limit. Deterministic.
        /// </summary>
        internal static double SeasonalLicePressure(int day)
        {
            double dayOfYear = SimCalendar.DayOfYear(day);
            return SeasonalLicePressureBase
                + SeasonalLicePressureAmplitude * Math.Sin(2.0 * Math.PI * (dayOfYear - 180.0) / SimCalendar.DaysPerYear);
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
        internal void ApplyStormHazards()
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
                    // Escapes leave alive: booked as a removal, not a death (S2-05).
                    remaining -= pop.RemoveAlive(cohort, remaining, FishRemoval.Escape);
                    if (remaining <= 0) break;
                }
                pop.TriggerOnPopulationChanged();
                if (remaining <= 0) break;
            }

            site.RecordEscape(escapees, State.Day);

            decimal fine = escapees * EscapeFineNOKPerFish;
            PayCost(fine, CostBooking.Opex, "fines");

            State.EventLog.Add(new Events.EventRecord
            {
                Day = State.Day,
                Name = "Rømming",
                Category = Events.EventCategory.Equipment,
                Description = SimText.Nb($"{escapees:N0} fisk rømte fra {site.Label} (nøter {site.NetIntegrityPct:F0} %, stormstyrke {severity:F1})"),
                BalanceChange = -fine
            });
            State.TrimEventLog();

            OnEventTriggered?.Invoke(SimText.Nb($"🌊 RØMMING: {escapees:N0} fisk rømte fra {site.Label} (bot {fine:N0} kr)"));
        }

        /// <summary>
        /// Single-day tick — the core simulation loop.
        /// </summary>
        /// <summary>
        /// Single-day tick — the core simulation loop.
        /// </summary>
        private void AdvanceOneDay()
        {
            var overallStopwatch = Stopwatch.StartNew();

            // Phase 1 wave 4: ordered in-day pipeline. Registration order in
            // _inDayPhases IS the simulation order; every migrated phase owns
            // its interval state (rebuilt per Initialize — fresh clocks per
            // New Game, S9, including the formerly-leaking _quarterStartDay).
            foreach (var phase in _inDayPhases)
                phase.Tick(this);

            overallStopwatch.Stop();
            // T56: Removed SimLog.Info to avoid string allocation in hot path
            // Use Trace.WriteLine or conditional logging if debug output needed
        }

        /// <summary>
        /// Storm episode impacts + temperature stress, moved from the GameBridge
        /// (Phase 1 wave 2) and now a first-class POST-TICK phase via
        /// StormImpactsTickSystem (wave 3). Storm mortality, repair bill and
        /// insurance settlement fire once per storm EPISODE (S1-02): when an
        /// episode starts, at the severity it starts with, and again only when
        /// it escalates past its peak (Storm then Severe: the extra half). The
        /// old edge rule fired on every Storm/Severe flip inside one episode;
        /// with Severe reachable in winter (F456) that counted 81 "onsets" a
        /// year for 46 episodes. Temperature stress applies daily. Gated by
        /// _enableEvents like the other event hazards, so event-off probes keep
        /// their band models.
        /// </summary>
        internal void ApplyStormAndTemperatureImpacts()
        {
            if (!_enableEvents || Weather == null)
            {
                _lastWeatherState = Weather?.CurrentState ?? LakskongenSim.Environment.WeatherState.Calm;
                _stormEpisodePeakSeverity = StormSeverity(_lastWeatherState);
                return;
            }

            var currentWeather = Weather.CurrentState;
            double currentSeverity = StormSeverity(currentWeather);
            double severity = 0.0; // damage charged today
            string weatherDesc = "";
            if (currentSeverity > _stormEpisodePeakSeverity)
                severity = currentSeverity - _stormEpisodePeakSeverity;
            _stormEpisodePeakSeverity = currentSeverity > 0.0
                ? Math.Max(_stormEpisodePeakSeverity, currentSeverity)
                : 0.0;

            if (severity > 0.0)
            {

                // Storm stress mortality (roekter halves losses). Iterate each
                // site's own pens: populations are ZoneId-tagged, State-wide
                // filtering here would hit zone-shared populations per site.
                decimal stormSpotPrice = MarketSimulation?.PricePerKg ?? 70m;
                decimal stormFishLoss = 0m;
                double stormMult = CrewEffects.StormLossMultiplier(State);
                foreach (var site in State.SeaSites)
                {
                    foreach (var pop in site.Pens)
                    {
                        foreach (var cohort in pop.Cohorts)
                        {
                            int mortalityCount = (int)(cohort.Count * severity * CalibrationRegistry.Default.Environment.StormFishLossBaseFraction * stormMult);
                            if (mortalityCount > 0)
                            {
                                mortalityCount = pop.RemoveDead(cohort, mortalityCount, MortalityCause.Storm);
                                stormFishLoss += (decimal)(mortalityCount * cohort.MeanWeightKg) * stormSpotPrice;
                            }
                        }
                    }
                }

                // Storm repair bill (infrastructure damage; escapes and net wear
                // remain owned by ApplyStormHazards, the escape single source).
                decimal repairCost = (decimal)(TotalSiteScale() * 50000 * severity);
                if (repairCost > 0)
                {
                    // Damage must be repaired: paid on credit when cash is short
                    // (it used to be skipped for free when the player was broke).
                    PayCost(repairCost, CostBooking.Opex, "storm_repairs");
                    State.EventLog.Add(new Events.EventRecord
                    {
                        Day = State.Day,
                        Name = "Storm Damage",
                        Category = Events.EventCategory.Weather,
                        Description = currentWeather == LakskongenSim.Environment.WeatherState.Severe
                            ? "Ekstremværet skadet merdene og fortøyningene"
                            : "Stormen skadet merdene og fortøyningene",
                        BalanceChange = -repairCost
                    });
                }

                // P11: settle the Storm insurance claim against the realized
                // loss (fish mortality + infrastructure repair). One claim per
                // episode (the bridge gated on the same edge-transition).
                decimal stormLossNok = stormFishLoss + repairCost;
                PayInsurance(InsuranceKind.Storm, stormLossNok, "stormtap", Events.EventCategory.Weather);

                weatherDesc = currentWeather == LakskongenSim.Environment.WeatherState.Severe
                    ? "Ekstremvær gir store skader på anlegget og stresset fisk"
                    : "Stormen gir moderate skader og økt dødelighet";
            }

            // Temperature extremes stress fish (daily), each in its own water (S2-02).
            ApplyHoldingWaterTemperatureStress();

            if (severity > 0.0)
            {
                _dayNotes.Add(new DayNote
                {
                    Kind = "storm_impact",
                    EventType = currentWeather.ToString(),
                    Severity = severity,
                    Message = weatherDesc,
                    Success = true
                });
            }

            _lastWeatherState = currentWeather;
        }

        /// <summary>
        /// Process night-shift ROC policy (Phase 5.2): handles 00-06 events based on policy.
        /// </summary>
        internal void ProcessNightShiftPolicy()
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
                                Description = $"Driftssentralen oppdaget oppblomstring på {site.Label} kl. 02.00 — tiltak er satt i gang",
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
                                Description = $"Nattskanningen oppdaget oppblomstring på {site.Label} kl. 02.00 — venter til kl. 06.00",
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
                        PayCost(NightOxygenEmergencyNok, CostBooking.Opex, "night_shift"); // emergency O2 cost
                        State.EventLog.Add(new Events.EventRecord
                        {
                            Day = State.Day,
                            Name = "Natt-oksygen",
                            Category = Events.EventCategory.Weather,
                            Description = $"Oksygenfall på {site.Label} kl. 03.00 — driftssentralen startet diffuseren (12k)",
                            BalanceChange = -NightOxygenEmergencyNok
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
                        Name = "Feil i automatisk nattdrift",
                        Category = Events.EventCategory.Equipment,
                        Description = "Den automatiske nattdriften slo feil: feil merd eller feil tiltak — mannskapet stresses (+5 %)",
                        BalanceChange = -25_000m
                    });
                    foreach (var c in State.Crew) c.Morale = Math.Max(10, c.Morale - 5);
                }
            }

            // Premium cost
            decimal premium = SimCalendar.MonthlyToDaily(Logistics.NightShiftResolver.MonthlyCost(State.NightShiftPolicy));
            PayCost(premium, CostBooking.Opex, "night_shift");
        }

        /// <summary>Total live biomass across all populations, kg (annual-close input).</summary>
        internal decimal GetTotalBiomassKg()
            => (decimal)State.Populations.Sum(p => p.GetTotalBiomassKg());

        /// <summary>Pens in a standard sea site; calibrated crew and upkeep are per standard site.</summary>
        internal const int StandardPensPerSite = 8;

        /// <summary>A site's size relative to a standard 8-pen site.</summary>
        internal static double SiteSizeScale(Farm.SeaSite site)
            => Math.Max(1, site.NumberOfPens) / (double)StandardPensPerSite;

        /// <summary>Sum of all sites' sizes in standard sites.</summary>
        private double TotalSiteScale() => State.SeaSites.Sum(SiteSizeScale);

        /// <summary>Crew of a site: the calibrated crew scaled to its size, at least one worker.</summary>
        internal decimal SiteWorkers(Farm.SeaSite site)
            => Math.Max(1m, Math.Round(_balancing.WorkersPerSite * (decimal)SiteSizeScale(site)));

        internal void ProcessDailyFinance()
        {
            // Phase 5.5: Process Mattilsynet over-MAB orders (deadline checks + forced harvest)
            ProcessMattilsynetOverMabOrders();

            // 1. Calculate operational costs (Wages, Maintenance). Site crews and
            // upkeep scale with the site's size: the calibrated crew is for a
            // standard 8-pen site, and a one-pen family farm used to pay that full
            // crew (11k NOK/day on 5,000 fish; the tutorial went bankrupt before
            // its first harvest).
            decimal dailyWages = 0m;
            foreach (var site in State.SeaSites)
                dailyWages += SiteWorkers(site) * SimCalendar.MonthlyToDaily(_balancing.WorkerMonthlySalaryNOK);
            
            if (State.OwnsHatchery)
                dailyWages += _balancing.WorkersPerHatchery * SimCalendar.MonthlyToDaily(_balancing.WorkerMonthlySalaryNOK);
            
            if (State.OwnsProcessingPlant)
                dailyWages += _balancing.WorkersPerProcessingPlant * SimCalendar.MonthlyToDaily(_balancing.WorkerMonthlySalaryNOK);

            // Phase 3.1: named crew payroll.
            foreach (var member in State.Crew)
                dailyWages += SimCalendar.MonthlyToDaily(member.MonthlySalaryNok);

            // Phase 3.5: VAP plant opex 5k/day when owned.
            decimal vapOpex = State.OwnsProcessingPlant ? 5_000m : 0m;

            // 2. Fixed maintenance costs (e.g., 0.01% of build cost daily)
            decimal dailyMaintenance = (decimal)TotalSiteScale() * _balancing.SeaSiteBuildCostNOK * 0.0001m;
            dailyMaintenance += _dailyAiFeedingLicenseCost; // Add AI Feeding license cost here

            // 3. Apply costs through the ledger (paid on credit when cash is short)
            decimal totalOpEx = dailyWages + dailyMaintenance + _dailyFarmSystemsOpex + vapOpex;
            PayCost(totalOpEx, CostBooking.Opex, "operations",
                $"💸 BANKRUPTCY WARNING: Cannot pay {totalOpEx:N0} NOK in operating costs. Balance: {FinanceManager.Balance:N0} NOK");

            // 3B. Preventive mitigation OPEX (Phase 1.5): silent daily upkeep.
            // Skirt 2 000 NOK/site/day, deterrent 1 000 NOK/site/day.
            decimal mitigationOpex = 0m;
            foreach (var site in State.SeaSites)
            {
                if (site.HasLiceSkirt) mitigationOpex += 2_000m;
                if (site.HasSealDeterrent) mitigationOpex += 1_000m;
            }
            PayCost(mitigationOpex, CostBooking.Opex, "mitigation");

            // 4. Bond interest (LoanSystem daily accrual on the InvestorSystem's
            // bond principal) is interest expense, not OpEx (review F1138).
            if (InvestorSystem != null && InvestorSystem.OutstandingBondPrincipal > 0 && LoanSystem != null)
            {
                decimal dailyInterest = LoanSystem.ChargeInterest(InvestorSystem.OutstandingBondPrincipal, 1);
                PayCost(dailyInterest, CostBooking.Interest, "interest",
                    $"💸 Cannot pay {dailyInterest:N0} NOK in loan interest. Balance: {FinanceManager.Balance:N0} NOK");
            }

            // 4B. Loan amortization (S5): TakeLoan records a LoanState liability;
            // each day pays interest (P&L) and a principal slice (balance sheet).
            // Principal used to be booked as tax-deductible OpEx (review F1138).
            foreach (var loan in State.ActiveLoans.ToList())
            {
                if (loan.Principal <= 0) { State.ActiveLoans.Remove(loan); continue; }
                loan.DaysTaken++;
                int remainingDays = Math.Max(1, loan.TermDays - loan.DaysTaken + 1);
                decimal dailyInterest = loan.Principal * loan.InterestRate / SimCalendar.DaysPerYear;
                decimal principalSlice = loan.Principal / remainingDays;
                // Final day: clear exactly (avoid dust from rounding).
                if (loan.DaysTaken >= loan.TermDays) principalSlice = loan.Principal;
                if (!FinanceManager.CanAfford(dailyInterest + principalSlice))
                    OnEventTriggered?.Invoke($"💸 Cannot pay {dailyInterest + principalSlice:N0} NOK loan instalment. Balance: {FinanceManager.Balance:N0} NOK");
                PayCost(dailyInterest, CostBooking.Interest, "interest");
                PayCost(principalSlice, CostBooking.LoanPrincipal, "loan_principal");
                loan.Principal -= principalSlice;
                if (loan.Principal <= 0.01m || loan.DaysTaken >= loan.TermDays)
                    State.ActiveLoans.Remove(loan);
            }

            // 4C. Insurance premiums (S5): policies were free — charge the
            // annual premium pro-rata every day.
            if (Insurance != null)
            {
                decimal dailyPremium = 0m;
                foreach (var policy in Insurance.Policies)
                    dailyPremium += policy.PremiumNokPerYear / SimCalendar.DaysPerYear;
                PayCost(dailyPremium, CostBooking.Opex, "insurance");
            }

            // 5. Monthly provisional corporate tax (true-up; see SettleProvisionalTax),
            // on the last day of every calendar month (S2-02; it was every 30 days).
            if (SimCalendar.IsLastDayOfMonth(State.Day))
                SettleProvisionalTax();
        }


        /// <summary>
        /// Process sludge valorization at sea sites (Phase 5.3).
        /// Converts accumulated sludge to biogas revenue + fertilizer revenue - opex.
        /// Runs weekly as part of sustainability update.
        /// </summary>
        internal void ProcessSludgeValorization()
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
                    {
                        ReceiveIncome(netRevenue, "sludge");
                        State.TotalSalesRevenueNok += netRevenue;
                    }
                    else
                        PayCost(-netRevenue, CostBooking.Opex, "sludge");

                    site.SludgeValorizedKgDs += site.SludgeAccumulatedKgDs;
                    site.LastSludgeValorizationDay = State.Day;

                    // Phase 5.4: Add biogas credit to carbon ledger
                    double biogasNm3 = site.SludgeAccumulatedKgDs * Farm.SludgeValorization.Default.BiogasYieldNm3PerKgDs;
                    double biogasCredit = Farm.CarbonLedger.BiogasCreditKgCO2e(biogasNm3);
                    site.CarbonTracker.AddCredit("Biogas", site.Id, biogasCredit, State.Day);

                    State.EventLog.Add(new Events.EventRecord
                    {
                        Day = State.Day,
                        Name = "Slamgjenvinning",
                        Category = Events.EventCategory.Equipment,
                        Description = SimText.Nb($"{site.Label}: {site.SludgeAccumulatedKgDs:F0} kg tørrstoff → biogass {biogasRev:N0} + gjødsel {fertRev:N0} − drift {opex:N0} = {netRevenue:+#,0;-#,0;0} kr (CO₂e-kreditt: {biogasCredit:F0} kg)"),
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
        /// <summary>A rival in financial distress: bankruptcy proceedings list its assets.</summary>
        private void HandleCompetitorDistressed(Finance.CompetitorCompany competitor, int _)
            => CrisisCapitalism?.TriggerCrisis(competitor.Id, competitor.Name, Events.CrisisType.BANKRUPTCY,
                State.Day, CompetitorAI.Competitors);

        internal void ProcessCarbonLedger()
        {
            int day = State.Day;
            double feedReplacementPct = State.RecipeReplacePct;
            // Research (renewable energy, carbon capture) cuts fuel and power
            // emissions, Scope 1-2; feed and transport (Scope 3) are unaffected.
            double energyEmissionFactor = 1.0 - ResearchEffectKeys.ClampReduction(CarbonReduction);

            // ─── Sea Sites ───
            foreach (var site in State.SeaSites)
            {
                double biomassTonnes = site.Pens.Sum(p => p.GetTotalBiomassKg()) / 1000.0;
                if (biomassTonnes <= 0) continue;

                // Scope 1: Diesel fuel
                double fuelL = Farm.CarbonLedger.DailySiteFuelLiters(site, biomassTonnes);
                double fuelCO2e = fuelL * Farm.CarbonLedger.DieselKgCO2ePerLiter * energyEmissionFactor;
                site.CarbonTracker.AddEmission("Scope1_Fuel", site.Id, fuelCO2e, day);

                // Scope 2: Electricity
                double elecKwh = Farm.CarbonLedger.DailySiteElectricityKwh(site, biomassTonnes);
                double elecCO2e = elecKwh * Farm.CarbonLedger.GridElectricityKgCO2ePerKwh * energyEmissionFactor;
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
                double elecCO2e = ras.DailyEnergyKWh * Farm.CarbonLedger.GridElectricityKgCO2ePerKwh * energyEmissionFactor;
                ras.CarbonTracker.AddEmission("Scope2_Electricity", ras.Id, elecCO2e, day);
            }

            // ─── Offshore Farms ───
            foreach (var off in OffshoreFarms)
            {
                if (!off.IsOperational) continue;
                double biomassTonnes = off.Pens.Sum(p => p.GetTotalBiomassKg()) / 1000.0;

                // Scope 1: Generator fuel (higher than fjord sites)
                double fuelL = 200.0 + biomassTonnes * 1.0; // base + per tonne
                double fuelCO2e = fuelL * Farm.CarbonLedger.DieselKgCO2ePerLiter * energyEmissionFactor;
                off.CarbonTracker.AddEmission("Scope1_Fuel", off.Id, fuelCO2e, day);

                // Scope 2: Grid/shore power (assume partial)
                double elecKwh = 500.0 + biomassTonnes * 2.0;
                double elecCO2e = elecKwh * Farm.CarbonLedger.GridElectricityKgCO2ePerKwh * energyEmissionFactor;
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
        internal void SyncResearchToState()
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
                    bool affordable = FinanceManager.CanAfford(NonComplianceFineNok);
                    PayCost(NonComplianceFineNok, CostBooking.Opex, "fines",
                        "⚠️ BANKRUPTCY WARNING: Cannot pay 50,000 NOK non-compliance fine. Balance: " + FinanceManager.Balance.ToString("N0") + " NOK");
                    if (affordable)
                        OnEventTriggered?.Invoke("💸 Non-compliance fine: 50,000 NOK");
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
        internal float CalculatePlayerMarketShare()
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
        internal void AdvanceSiteLiceModels()
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
                // S2-02: the lice develop at the site's own water temperature.
                site.Lice.SetEnvironment(
                    SeaTemperatureAtSite(site),
                    35.0,
                    zone?.CurrentSpeed ?? 0.15,
                    biomassKg / volumeM3);
                site.Lice.SetExternalPressure(SeasonalLicePressure(State.Day));
                // The site's own skirt, containment and barrier block settlement on
                // the lice that are counted and regulated. They used to reach only
                // the company-wide rollup, so paid mitigation never lowered a
                // site's count, traffic light or Mattilsynet exposure.
                site.Lice.PreventiveCoverage01 = SiteSettlementBlocking(site);
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
        /// females where the site's water is warm enough to count (≥4 °C),
        /// enforce the site's legal limit (0.5; 0.2 in its production area's
        /// spring ISO weeks, 16–21 up to Nord-Trøndelag and 21–26 further north,
        /// S2-02), and escalate repeat breaches into Mattilsynet treatment orders
        /// with forced treatment past the deadline.
        /// </summary>
        internal void UpdateSiteLiceCounts()
        {
            foreach (var site in State.SeaSites)
            {
                int fish = site.Pens.SelectMany(p => p.Cohorts).Sum(c => c.Count);
                if (fish <= 0)
                    continue;
                double temperature = SeaTemperatureAtSite(site);
                double limit = LiceLimitAtSite(site);

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

                if (site.TreatmentOrderDueDay >= 0 && site.LastLiceCount <= limit)
                {
                    // Back under the limit before the deadline: the order is
                    // satisfied. Without this, a player who complied still got
                    // a forced (paid, harmful) treatment at the deadline (F1133).
                    site.TreatmentOrderDueDay = -1;
                    site.ConsecutiveLiceBreaches = 0;
                    State.EventLog.Add(new Events.EventRecord
                    {
                        Day = State.Day,
                        Name = "Mattilsynet order closed",
                        Category = Events.EventCategory.Regulation,
                        Description = SimText.Nb($"Mattilsynet: {site.Label} er under lusegrensen igjen ({site.LastLiceCount:F2} mot {limit:F1}); pålegget om behandling er lukket."),
                        BalanceChange = 0m
                    });
                    State.TrimEventLog();
                    OnEventTriggered?.Invoke(
                        SimText.Nb($"✅ Mattilsynet: {site.Label} er under lusegrensen igjen ({site.LastLiceCount:F2} mot {limit:F1}) — pålegget er lukket."));
                }
                else if (site.ConsecutiveLiceBreaches >= 2 && site.TreatmentOrderDueDay < 0)
                {
                    site.TreatmentOrderDueDay = State.Day + 14;
                    State.EventLog.Add(new Events.EventRecord
                    {
                        Day = State.Day,
                        Name = "Mattilsynet order",
                        Category = Events.EventCategory.Regulation,
                        Description = SimText.Nb($"Mattilsynet: {site.Label} er over lusegrensen ({site.LastLiceCount:F2} voksne hunnlus mot {limit:F1} tillatt). Avlus innen 14 dager."),
                        BalanceChange = 0m
                    });
                    State.TrimEventLog();
                    OnEventTriggered?.Invoke(
                        SimText.Nb($"📋 Mattilsynet: {site.Label} er over lusegrensen ({site.LastLiceCount:F2} mot {limit:F1}) — avlus innen 14 dager!"));
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
        /// <summary>Result of a full-site delousing pass (shared player/regulation kernel).</summary>
        private sealed class SiteDelousingOutcome
        {
            public bool Applied = false;
            public string CancelReason = "";
            public int TotalFish = 0;
            public int TotalDeaths = 0;
            public double GillDamage = 0.0;
            public decimal Cost = 0m;
        }

        /// <summary>
        /// Treatment-mortality multiplier for a site: crew (veterinarian) and
        /// gill health (Phase 4.3: damaged gills can't take heat/handling,
        /// up to +150%). Shared by the player verb and regulatory enforcement.
        /// </summary>
        private double ComputeSiteTreatmentMortalityMultiplier(SeaSite site)
        {
            double mortMult = CrewEffects.TreatmentMortalityMultiplier(State);
            double worstGill = site.GillScore;
            return mortMult * (1.0 + (1.0 - worstGill / 100.0) * 1.5);
        }

        /// <summary>
        /// Treat every live pen of a site with one delousing method (all-pens
        /// semantics). Lice kill and gill capture happen once per site;
        /// handling mortality and harm per pen. Payment, bookkeeping and the
        /// event log stay with the caller so the player verb (validated,
        /// spend) and regulation (force-paid) share one model.
        /// </summary>
        private SiteDelousingOutcome ApplySiteDelousing(Farm.SeaSite site,
            Data.DelousingMethod method, double tempC, double mortMult)
        {
            var outcome = new SiteDelousingOutcome();
            var delousing = new Farm.DelousingSystem();
            bool siteLiceKillDone = false;
            bool gillCaptured = false;
            foreach (var pop in site.Pens)
            {
                if (pop.TotalCount <= 0) continue;
                var result = delousing.Treat(pop, method, site.Lice, IntegratedDisease,
                    tempC, mortMult, applyCompanyLiceKill: !siteLiceKillDone);
                if (!result.Success)
                {
                    outcome.CancelReason = result.Message;
                    continue;
                }
                siteLiceKillDone = true;
                outcome.Applied = true;
                outcome.TotalFish += result.FishTreated;
                outcome.TotalDeaths += result.Deaths;
                outcome.Cost += result.Cost;
                if (!gillCaptured)
                {
                    outcome.GillDamage = result.GillDamage;
                    gillCaptured = true;
                }
            }
            return outcome;
        }

        private void EnforceLiceTreatmentOrder(SeaSite site, double temperatureC)
        {
            var target = site.Pens.FirstOrDefault(p => p.TotalCount > 0);
            if (target == null)
            {
                site.TreatmentOrderDueDay = -1;
                site.ConsecutiveLiceBreaches = 0;
                return;
            }

            // Phase 1 wave 3: regulation drives the SAME treatment kernel as
            // the player verb, so a Mattilsynet order treats the WHOLE site
            // (previously only the first pen) with identical model semantics —
            // crew/gill multipliers included (documented decision; the old
            // forced path silently used a flat 1.0 multiplier).
            var outcome = ApplySiteDelousing(site, Data.DelousingMethod.Thermolicer,
                temperatureC, ComputeSiteTreatmentMortalityMultiplier(site));

            if (outcome.Applied)
            {
                PayCost(outcome.Cost, CostBooking.Opex, "delousing");
                site.GillScore = Math.Max(0.0, site.GillScore - outcome.GillDamage * 50.0);

                State.EventLog.Add(new Events.EventRecord
                {
                    Day = State.Day,
                    Name = "Forced delousing",
                    Category = Events.EventCategory.Regulation,
                    Description = SimText.Nb($"Pålegget fra Mattilsynet om avlusing på {site.Label} ble ikke fulgt — tvangsbehandling med termisk avlusing av {outcome.TotalFish:N0} fisk kostet {outcome.Cost:N0} kr."),
                    BalanceChange = -outcome.Cost
                });
                State.TrimEventLog();
                OnEventTriggered?.Invoke(SimText.Nb($"⚖️ Tvangsavlusing på {site.Label}: {outcome.Cost:N0} kr."));
            }
            else
            {
                // Temperature window closed (e.g. Thermolicer < 6 °C) — the
                // order still closes, logged honestly instead of a silent 0-cost "success".
                State.EventLog.Add(new Events.EventRecord
                {
                    Day = State.Day,
                    Name = "Forced delousing",
                    Category = Events.EventCategory.Regulation,
                    Description = $"Pålegget fra Mattilsynet på {site.Label} kunne ikke gjennomføres: {outcome.CancelReason}",
                    BalanceChange = 0m
                });
                State.TrimEventLog();
            }

            site.TreatmentOrderDueDay = -1;
            site.ConsecutiveLiceBreaches = 0;
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

        /// <summary>The name of a site id as the player reads it.</summary>
        private string SiteLabelOf(string siteId) =>
            State.SeaSites.FirstOrDefault(s => s.Id == siteId)?.Label ?? Farm.SiteNames.Humanize(siteId);

        /// <summary>Norwegian name of a bloom kind for the event log.</summary>
        private static string BloomName(string kind) => kind switch
        {
            "jellyfish" => "Maneter",
            "algae" => "Algeoppblomstring",
            "hypoxia" => "Oksygensvikt",
            _ => "Oppblomstring"
        };

        /// <summary>The Norwegian name of a traffic-light colour ("Green" → "grønn").</summary>
        private static string TrafficLightName(ZoneColor color) => color switch
        {
            ZoneColor.Green => "grønn",
            ZoneColor.Yellow => "gul",
            ZoneColor.Red => "rød",
            _ => color.ToString()
        };

        /// <summary>A zone as the player reads it: the name of its first site
        /// ("PO3 Nord"), else a readable form of the id.</summary>
        private string ZoneLabel(string zoneId)
        {
            var site = State.SeaSites.FirstOrDefault(s => s.ZoneId == zoneId);
            return site != null && !string.IsNullOrWhiteSpace(site.DisplayName)
                ? site.DisplayName
                : Farm.SiteNames.Humanize(zoneId);
        }

        internal void UpdateTrafficLightZones()
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
                    Name = $"Trafikklys: {TrafficLightName(after)}",
                    Category = EventCategory.Regulation,
                    Description = SimText.Nb($"{ZoneLabel(zone.Id)} → {TrafficLightName(after)} (lus {lice:F2} per fisk, MTB ×{factor:F2})"),
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

                // Phase 5.5: regulatory chess — orders on RED entry, cleared on exit.
                if (after.ToString() == "Red")
                    IssueMattilsynetOverMabOrders(zone.Id);
                else
                    ClearMattilsynetOverMabOrders(zone.Id);
            }
        }

    /// <summary>
    /// Phase 5.5: Regulatory chess — felt consequence.
    /// When a zone goes RED, issue Mattilsynet over-MAB orders with deadlines.
    /// When a zone exits RED, clear any remaining over-MAB orders.
    /// </summary>
    private void IssueMattilsynetOverMabOrders(string zoneId)
    {
        foreach (var site in State.SeaSites.Where(s => s.ZoneId == zoneId))
        {
            if (site.IsOverMAB && !site.HasActiveMattilsynetOrder)
            {
                int deadline = State.Day + 14; // 14-day deadline
                site.MattilsynetDeadlineDay = deadline;
                site.MattilsynetOrderFulfilled = false;

                State.EventLog.Add(new EventRecord
                {
                    Day = State.Day,
                    Name = "Mattilsynet MAB-beskjed",
                    Category = EventCategory.Regulation,
                    Description = SimText.Nb($"{ZoneLabel(zoneId)}: {site.Label} er over MTB ({site.CurrentBiomassTonnes:F1}t > {site.MABTonnes:F1}t). Avlus eller slakt ned innen dag {deadline}, ellers tvangsavslakt til redusert pris."),
                    BalanceChange = 0m
                });
                SimLog.Info($"📋 Mattilsynet MAB-beskjed: {site.Id} → deadline day {deadline}");
            }
        }
    }

    /// <summary>
    /// Clear Mattilsynet over-MAB orders when zone exits RED status.
    /// </summary>
    private void ClearMattilsynetOverMabOrders(string zoneId)
    {
        foreach (var site in State.SeaSites.Where(s => s.ZoneId == zoneId))
        {
            if (site.HasActiveMattilsynetOrder)
            {
                site.MattilsynetDeadlineDay = -1;
                site.MattilsynetOrderFulfilled = false;

                State.EventLog.Add(new EventRecord
                {
                    Day = State.Day,
                    Name = "Mattilsynet besked trukket",
                    Category = EventCategory.Regulation,
                    Description = $"{ZoneLabel(zoneId)}: {site.Label} er ikke lenger i rød sone — MTB-pålegget er trukket.",
                    BalanceChange = 0m
                });
                SimLog.Info($"📋 Mattilsynet MAB-beskjed trukket for {site.Id} (sone {zoneId} ikke lenger rød)");
            }
        }
    }

    /// <summary>
    /// Process Mattilsynet over-MAB orders daily: check deadlines,
    /// execute forced harvest at penalty price if missed.
    /// </summary>
    internal void ProcessMattilsynetOverMabOrders()
    {
        foreach (var site in State.SeaSites.Where(s => s.HasActiveMattilsynetOrder && !s.MattilsynetOrderFulfilled))
        {
            if (State.Day >= site.MattilsynetDeadlineDay)
            {
                // Deadline missed → forced harvest at 70% of market price
                decimal penaltyPrice = MarketSimulation?.PricePerKg ?? 70m;
                penaltyPrice = Math.Round(penaltyPrice * 0.7m, 2);

                double biomassT = site.CurrentBiomassTonnes;
                double harvestT = Math.Min(biomassT, site.CurrentBiomassTonnes - site.MABTonnes);
                if (harvestT < 0.1) harvestT = biomassT; // harvest all if barely over

                decimal revenue = (decimal)(harvestT * 1000 * (double)penaltyPrice); // NOK
                ReceiveIncome(revenue, "forced_harvest");
                State.TotalSalesRevenueNok += revenue;
                State.TotalHarvestRevenueNok += revenue;
                RecordHarvestForResourceRent(harvestT * 1000.0);

                // Record event
                State.EventLog.Add(new EventRecord
                {
                    Day = State.Day,
                    Name = "Tvangsavslakt (Mattilsynet)",
                    Category = EventCategory.Regulation,
                    Description = SimText.Nb($"{site.Label}: Fristen er ute. {harvestT:F1}t tvangsavslattet til {penaltyPrice:F2} kr/kg (30 % straff)."),
                    BalanceChange = revenue
                });
                SimLog.Info($"⚠️ Tvangsavslakt: {site.Id} {harvestT:F1}t til {penaltyPrice:F2} NOK/kg");

                // Remove the over-MAB biomass from populations (simplified: reduce cohort weights proportionally)
                ReduceSiteBiomass(site, harvestT);

                site.MattilsynetOrderFulfilled = true;
                site.MattilsynetDeadlineDay = -1;
            }
            else if (!site.IsOverMAB)
            {
                // Player harvested enough to get under MAB before deadline
                site.MattilsynetOrderFulfilled = true;
                site.MattilsynetDeadlineDay = -1;

                State.EventLog.Add(new EventRecord
                {
                    Day = State.Day,
                    Name = "Mattilsynet MAB-oppgjør",
                    Category = EventCategory.Regulation,
                    Description = $"{site.Label}: Biomassen er under MTB. Pålegget er oppfylt — ingen tvangsslakting.",
                    BalanceChange = 0m
                });
                SimLog.Info($"✅ Mattilsynet MAB-oppgjør: {site.Id} under MAB før deadline");
            }
        }
    }

    /// <summary>
    /// Reduce site biomass proportionally across cohorts to simulate forced harvest.
    /// </summary>
    private void ReduceSiteBiomass(Farm.SeaSite site, double tonnesToRemove)
    {
        double kgToRemove = tonnesToRemove * 1000.0;
        foreach (var pop in site.Pens)
        {
            foreach (var cohort in pop.Cohorts)
            {
                double cohortBiomassKg = cohort.Count * cohort.MeanWeightKg;
                if (cohortBiomassKg <= 0) continue;

                double removeFraction = Math.Min(1.0, kgToRemove / cohortBiomassKg);
                int fishToRemove = (int)(cohort.Count * removeFraction);
                fishToRemove = pop.RemoveAlive(cohort, fishToRemove, FishRemoval.ForcedHarvest);
                kgToRemove -= fishToRemove * cohort.MeanWeightKg;
                if (kgToRemove <= 0) break;
            }
            pop.TriggerOnPopulationChanged();
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
        internal void UpdatePreventiveCoverage()
        {
            if (SeaLice == null) return;
            double total = 0, covered = 0;
            foreach (var site in State.SeaSites)
            {
                int fish = site.Pens.SelectMany(p => p.Cohorts).Sum(c => c.Count);
                total += fish;
                covered += fish * SiteSettlementBlocking(site);

                if (site.Barrier != Farm.BarrierKind.None)
                {
                    // Periodic cleaning requirement: uncleaned barriers lose
                    // efficiency (SiteSettlementBlocking), and cleaning costs standing opex.
                    site.BarrierDaysSinceCleaning += BARRIER_CLEANING_TICK_DAYS;
                    decimal barrierOpex = Farm.BarrierTech.SpecFor(site.Barrier).WeeklyOpexNok;
                    PayCost(barrierOpex, CostBooking.Opex, "barriers");
                }

                // Phase 4.1: standing energy opex per containment level.
                decimal opex = Farm.ContainmentTech.ModifiersFor(site.Containment).WeeklyOpexNok;
                PayCost(opex, CostBooking.Opex, "containment");
            }
            SeaLice.PreventiveCoverage01 = total > 0 ? covered / total : 0.0;
        }

        /// <summary>Settlement blocked by a lice skirt (Phase 1.5).</summary>
        private const double LiceSkirtSettlementBlocking = 0.70;

        /// <summary>
        /// Fraction of new lice settlement blocked at one site: lice skirt,
        /// containment exclusion (Phase 4.1), a permeable barrier (P8, flow- and
        /// cleaning-dependent) and research lice reduction combine multiplicatively.
        /// </summary>
        private double SiteSettlementBlocking(Farm.SeaSite site)
        {
            double skirt = site.HasLiceSkirt ? LiceSkirtSettlementBlocking : 0.0;
            double exclusion = Farm.ContainmentTech.ModifiersFor(site.Containment).LiceExclusion01;
            double barrier = site.Barrier != Farm.BarrierKind.None
                ? Farm.BarrierTech.EffectiveLiceBlocking(site.Barrier, SiteFlowMs(site), site.BarrierDaysSinceCleaning)
                : 0.0;
            double research = ResearchEffectKeys.ClampReduction(LiceReduction);
            return 1.0 - (1.0 - skirt) * (1.0 - exclusion) * (1.0 - barrier) * (1.0 - research);
        }

        /// <summary>
        /// Weekly bloom roll + damage (Phase 4.3). Spawns seasonal events per
        /// zone, ticks gill damage + mortality on afflicted sites, heals clean
        /// sites, expires old blooms, charges countermeasure opex.
        /// </summary>
        internal void RollBloomWeek()
        {
            int dayOfYear = SimCalendar.DayOfYear(State.Day);
            bool calm = Weather == null || Weather.CurrentState.ToString() == "Calm"
                || Weather.CurrentState.ToString() == "Moderate";
            bool vet = Core.CrewEffects.HasRole(State, "veterinaer");

            // Spawn per zone, each on its own water temperature (S2-02).
            foreach (var zone in State.Zones)
            {
                var sites = State.SeaSites
                    .Where(s => s.ZoneId == zone.Id && s.Pens.Any(p => p.TotalCount > 0))
                    .ToList();
                if (sites.Count == 0) continue;
                var site = sites[_rng.Next(sites.Count)];
                double temp = SeaTemperatureOfZone(zone.Id);

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

                // Wave 2 sim-a-1: the hypoxia card is an in-cage oxygen sag that
                // lowers the pens' water (HoldingWaterOf); it kills only the fish
                // whose own oxygen is below the acute limit (per pen, below), not
                // a flat share of the site whatever the water held.
                bool oxygenSag = bloom.Kind == "hypoxia";
                double mortRate = oxygenSag
                    ? (vet ? 0.8 : 1.0)
                    : bloom.Kind switch
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
                    PayCost(outcome.CostNok, CostBooking.Opex, "bloom_mitigation");
                }

                int killed = 0;
                double lostKg = 0.0;
                var bloomCause = MortalityCauses.ForBloomKind(bloom.Kind);
                foreach (var pop in site.Pens)
                {
                    double popRate = oxygenSag ? mortRate * LowOxygenDeathFraction(pop) : mortRate;
                    foreach (var cohort in pop.Cohorts)
                    {
                        int take = pop.RemoveDead(cohort, (int)(cohort.Count * popRate), bloomCause);
                        killed += take;
                        lostKg += take * cohort.MeanWeightKg;
                    }
                }
                if (killed > 0)
                {
                    SimLog.Info($"🪼 Bloom {bloom.Kind} killed {killed:N0} at {site.Id} (gill {site.GillScore:F0})");

                    // P11: catastrophe insurance payout on the realized bloom loss.
                    decimal bloomPrice = MarketSimulation?.PricePerKg ?? _balancing.BaseSalmonPricePerKg;
                    PayInsurance(
                        InsuranceKind.Bloom,
                        (decimal)lostKg * bloomPrice,
                        $"{BloomName(bloom.Kind)} på {site.Label}",
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
                PayCost(opex, CostBooking.Opex, "bloom_countermeasures");
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
                Description = $"{BloomName(bloom.Kind)} på {site.Label} — sjekk gjellene og vurder tiltak",
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
        internal void PayInsurance(InsuranceKind kind, decimal lossNok, string cause, EventCategory category)
        {
            if (Insurance == null || lossNok <= 0m) return;
            decimal payout = Insurance.ComputePayout(kind, lossNok);
            if (payout <= 0m) return;

            // Booked as insurance income, not harvest revenue (review F1132).
            ReceiveIncome(payout, "insurance");

            State.EventLog.Add(new Events.EventRecord
            {
                Day = State.Day,
                Id = $"insurance-{kind.ToString().ToLowerInvariant()}",
                Name = "Forsikringsoppgjør",
                Category = category,
                Description = SimText.Nb($"Forsikringen dekket {payout:N0} av {lossNok:N0} kr i tap — {cause}"),
                BalanceChange = payout
            });
            State.TrimEventLog();
            OnEventTriggered?.Invoke($"🛡️ Forsikringsutbetaling ({kind}): {payout:N0} NOK");
        }

        /// <summary>
        /// Build EmpireState snapshot for EmpireProgressionTickSystem.
        /// </summary>
        internal Finance.EmpireState BuildEmpireState()
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
            ReconcileUnbookedCash();
            #pragma warning disable CS0618 // Type or member is obsolete
            State.Balance = FinanceManager.Balance;
            #pragma warning restore CS0618

            // T74: Sync all empire systems to GameState for serialization
            SyncEmpireSystemsToState();

            // P9/P11: Sync insurance, resource-rent tax, pen-price council and
            // bloom mitigations so top-tier depth state survives saves.
            SyncDepthSystemsToState();

            // Phase 1 wave 5: snapshot the runtime cadence (interval clocks,
            // storm edge, event gating) so a loaded game CONTINUES instead of
            // resetting weekly/quarterly beats and edge detectors.
            var runtime = new EngineRuntimeSnapshot
            {
                WeatherEdgeState = _lastWeatherState.ToString(),
                StormEpisodePeakSeverity = _stormEpisodePeakSeverity,
                EventsEnabled = _enableEvents
            };
            var clocks = new List<int>(_inDayPhases.Length);
            var clocksByName = new List<KeyValuePair<string, int>>();
            foreach (var phase in _inDayPhases)
            {
                if (phase is ITickClock clock)
                {
                    clocks.Add(clock.ClockValue);
                    clocksByName.Add(new(phase.GetType().Name, clock.ClockValue));
                }
                else
                {
                    clocks.Add(0);
                }
            }
            runtime.InDayClocks = clocks.ToArray();
            runtime.ClocksBySystem = new Dictionary<string, int>(clocksByName);

            // Phase 1 wave 6: RNG stream positions + the stateful sub-models
            // so a loaded game CONTINUES exactly (enables the save-hash test).
            // Every subsystem RNG is a named SimRng stream owned by this
            // engine's Rng service, so positions round-trip through
            // SimStreamPositions (including per-identity streams).
            runtime.SimStreamPositions = Rng.CaptureStates();

            var pcState = PenPriceCouncil?.ExportState();
            runtime.PriceCouncilSpots = pcState?.Spots;
            runtime.PriceCouncilMonthly = pcState?.Monthly ?? 0m;
            runtime.StorytellerLastEventDay = Storyteller?.LastEventDayForSnapshot ?? -9999;
            runtime.StorytellerCooldowns = Storyteller?.CooldownsForSnapshot;
            runtime.CurrencyStatePresent = true;
            runtime.TrafficLightColors = TrafficLightSystem?.ExportColors();
            var feedState = Feed.ExportState();
            runtime.FeedFcr = feedState.Fcr;
            runtime.FeedUsedKg = feedState.FeedUsedKg;
            runtime.FeedGrowthKg = feedState.GrowthKg;
            runtime.FeedLastAppetite = feedState.LastAppetite;
            runtime.FeedSustainabilityRating = feedState.SustainRating;
            var cur = CurrencySystem?.ExportCurrencyState() ?? (0, 0m, 0m, 0m, 0m, 2.5m);
            runtime.CurrencyBankRate = cur.BankRate;
            runtime.CurrencyDay = cur.Day;
            runtime.CurrencyEur = cur.Eur;
            runtime.CurrencyUsd = cur.Usd;
            runtime.CurrencyJpy = cur.Jpy;
            runtime.CurrencyGbp = cur.Gbp;

            // Phase 1 wave 6: weather live state (season day, temperature,
            // wind, wave, Markov state) — without this the season restarts
            // at January and every downstream system diverges.
            var wth = Weather;
            runtime.WeatherStatePresent = wth != null;
            if (wth != null)
            {
                runtime.WeatherDay = wth.SimulationDay;
                runtime.WeatherTempC = wth.CurrentTemperatureC;
                runtime.WeatherWindMs = wth.CurrentWindSpeedMs;
                runtime.WeatherWaveM = wth.CurrentWaveHeight;
                runtime.WeatherStateName = wth.CurrentState.ToString();
                runtime.WeatherSeaAnomalyC = wth.SeaAnomalyC;
                runtime.WeatherEventAnomalyC = wth.EventAnomalyC;
                runtime.WeatherDailyFluctuationC = wth.DailyFluctuationC;
                runtime.WeatherStormCoolingC = wth.StormCoolingC;
            }

            // Phase 1 wave 6: live statements + market trajectory. These were
            // silently reset on every load before (fresh instances), which
            // broke quarterly taxes/earnings and the whole price path.
            var finState = FinancialStatements!.ExportState();
            runtime.FinRevenue = finState.Revenue;
            runtime.FinCogs = finState.Cogs;
            runtime.FinOpex = finState.Opex;
            runtime.FinInterest = finState.Interest;
            runtime.FinTaxes = finState.Taxes;
            runtime.FinCash = finState.Cash;
            runtime.FinAssets = finState.Assets;
            runtime.FinLiabilities = finState.Liabilities;
            runtime.FinBiomassInventoryValue = finState.BiomassInventoryValue;
            runtime.FinCurrentPeriod = finState.CurrentPeriod;
            runtime.FinEvents = finState.Events;
            runtime.FinCostBreakdown = FinancialStatements.ExportCostBreakdown();
            runtime.FinRevenueBreakdown = FinancialStatements.ExportRevenueBreakdown();
            runtime.FinLifetime = FinancialStatements.ExportLifetime();
            var mktState = MarketSimulation!.ExportState();
            runtime.MktBase = mktState.Base;
            runtime.MktPrice = mktState.Price;
            runtime.MktWeeklyAvg = mktState.WeeklyAvg;
            runtime.MktLongMean = mktState.LongMean;
            runtime.MktMeanRev = mktState.MeanRev;
            runtime.MktVol = mktState.Vol;
            runtime.MktSupplyIdx = mktState.SupplyIdx;
            runtime.MktDemandIdx = mktState.DemandIdx;
            runtime.MktCertMult = mktState.CertMult;
            runtime.MktLastR2 = mktState.LastR2;
            runtime.MktLastObs = mktState.LastObs;
            runtime.MktSimDay = mktState.SimDay;
            runtime.MktHistory = mktState.History;
            runtime.MktPlayerPressure = MarketSimulation!.PlayerSupply.Pressure;
            var shockState = MarketSimulation!.ExportShockState();
            runtime.MktShockDays = shockState.ShockDays;
            runtime.MktSupplyOffset = shockState.SupplyOffset;
            var intelState = MarketSimulation!.Intelligence.ExportState();
            runtime.IntelWeek = intelState.Week;
            runtime.IntelActive = intelState.Active;
            runtime.IntelHistory = intelState.History;
            runtime.IntelCompetitors = intelState.Competitors;
            runtime.IntelPriceHistory = intelState.PriceHistory;
            runtime.IntelBrandScore = intelState.BrandScore;
            runtime.IntelBrandGood = intelState.BrandGood;
            runtime.IntelBrandTotal = intelState.BrandTotal;
            runtime.PlanktonBloomStates = PlanktonBloom?.GetAllBloomStates();
            var aiState = AIFeeding.ExportState();
            runtime.AiFeedInstalled = aiState.Installed;
            runtime.AiFeedCalibration = aiState.Calibration;
            runtime.AiFeedDays = aiState.Days;
            runtime.AiFeedCameraUptime = aiState.CameraUptime;
            runtime.AiFeedAppetite = aiState.Appetite;
            runtime.AiFeedWaste = aiState.Waste;
            runtime.AiFeedSavings = aiState.Savings;
            runtime.AiFeedFcrMult = aiState.FcrMult;
            runtime.AiFeedHistory = aiState.History;

            State.Runtime = runtime;
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
            // 1. CompetitorAI — full snapshot (F1071) plus the legacy DTO list
            // that older builds read.
            State.CompetitorAI = CompetitorAI?.ExportState();
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

            // 5. CrisisCapitalism — active crises and the listings they put on the
            // market (expired ones drop naturally). Listings were never saved, so
            // a load emptied the market. The listings are filtered by the
            // calendar, exactly like RestoreAsset and the bridge: the weekly
            // countdown lags, so a listing that closed today (ExpiresOnDay ==
            // Day) used to be saved, dropped on load, and the resumed career
            // diverged from the uninterrupted one (S1-01).
            if (CrisisCapitalism != null)
            {
                State.ActiveCrises = CrisisCapitalism.GetActiveCrises(State.Day)
                    .Select(c => new CrisisEventState
                    {
                        Id = c.Id,
                        Type = c.Type.ToString(),
                        AffectedCompetitorId = c.AffectedCompetitorId,
                        CompetitorName = c.CompetitorName,
                        SeverityScore = c.SeverityScore,
                        DiscountMultiplier = c.DiscountMultiplier,
                        DurationMonths = c.DurationMonths,
                        StartDay = c.StartDay
                    }).ToList();
                State.DistressedAssets = CrisisCapitalism.GetAllAvailableDistressedAssets(State.Day)
                    .Select(a => new DistressedAssetState
                    {
                        Id = a.Id,
                        AssetType = a.AssetType.ToString(),
                        Name = a.Name,
                        Description = a.Description,
                        Location = a.Location,
                        OriginalValueNOK = a.OriginalValueNOK,
                        DiscountedPriceNOK = a.DiscountedPriceNOK,
                        TimeRemainingMonths = a.TimeRemainingMonths,
                        ExpiresOnDay = a.ExpiresOnDay,
                        RiskScore = a.RiskScore,
                        CrisisEventId = a.CrisisEventId,
                        OwningCompetitor = a.OwningCompetitor,
                        SourceCompetitorId = a.SourceCompetitorId,
                        SiteId = a.SiteId,
                        LicenseCount = a.LicenseCount,
                        MABTonnes = a.MABTonnes
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
            State.CorporateFinance = CorporateFinance?.ExportState();

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

            // 12. Live systems persisted for the save/load matrix: contracts
            // (F409), scenario progress (F243), disease prevalence + the
            // company lice model it reads (F478), tax carry-forward (F1137).
            State.Contracts = ContractSystem?.ExportState();
            SyncScenarioToState();
            if (Disease != null)
                State.DiseasePrevalence = Disease.ExportPrevalence(LivePopulationIds());
            State.CompanyLice = SeaLice?.ExportState();
            if (TaxSystem != null)
            {
                var tax = TaxSystem.ExportState();
                State.Tax = new TaxSystemState { LossCarryForward = tax.LossCarryForward, Period = tax.Period };
            }

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
            // Reset IN PLACE: subsystems captured this instance at
            // construction, and replacing it would orphan their references.
            #pragma warning disable CS0618 // Type or member is obsolete
            if (FinanceManager == null)
                FinanceManager = new Finance.FinanceManager(loadedState.Balance);
            else
                FinanceManager.SetBalanceForLoad(loadedState.Balance);
            // Restore the daily cash mirror too: storyteller event conditions
            // read GameState.Balance, and until the next end-of-day sync it
            // would read 0 and silently change the event pool on the first
            // day after a load (found by SaveLoadMatrixTests, seed 23 day 400).
            State.Balance = FinanceManager.Balance;
            #pragma warning restore CS0618
            ResetLedgerBaseline();

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

            // Save/load matrix: contracts, scenario, disease, lice, tax.
            RestoreLiveSystemsFromState(loadedState);

            // P9/P11: Restore insurance, resource-rent tax and pen-price state
            RestoreDepthSystemsFromState(loadedState);

            // Phase 1 wave 5: restore the runtime cadence. Interval clocks
            // and the storm edge come from the snapshot so a loaded game
            // CONTINUES (no double-firing weekly/quarterly beats, no
            // re-settled mid-episode insurance). Legacy saves without a
            // snapshot align the storm edge with the current weather.
            var runtime = loadedState.Runtime;
            if (runtime != null)
            {
                // Weather live state first: the storm-edge fallback below
                // reads Weather.CurrentState, which must already be the
                // saved one (season day, temperature, wind, wave, Markov
                // state) — otherwise the whole downstream chain diverges.
                if (Weather != null && runtime.WeatherStatePresent &&
                    Enum.TryParse(runtime.WeatherStateName, ignoreCase: true,
                        out LakskongenSim.Environment.WeatherState wstate) &&
                    Enum.IsDefined(wstate))
                {
                    Weather.ImportState(runtime.WeatherDay, runtime.WeatherTempC,
                        runtime.WeatherWindMs, runtime.WeatherWaveM, wstate);
                    Weather.ImportSeaTemperatureState(runtime.WeatherSeaAnomalyC, runtime.WeatherEventAnomalyC,
                        runtime.WeatherDailyFluctuationC, runtime.WeatherStormCoolingC);
                }

                for (int i = 0; i < _inDayPhases.Length; i++)
                {
                    if (_inDayPhases[i] is not ITickClock clockSystem) continue;

                    // Identity-keyed restore first (inserting/reordering
                    // phases can never mis-map a clock); positional array is
                    // the legacy fallback.
                    if (runtime.ClocksBySystem != null &&
                        runtime.ClocksBySystem.TryGetValue(_inDayPhases[i].GetType().Name, out var savedByKey))
                    {
                        clockSystem.SetClock(savedByKey);
                    }
                    else if (runtime.InDayClocks != null &&
                             runtime.InDayClocks.Length == _inDayPhases.Length)
                    {
                        clockSystem.SetClock(runtime.InDayClocks[i]);
                    }
                }

                // Storm edge restore with a hard fallback: an unparsable or
                // undefined edge value must resolve to "aligned with current
                // weather" — a parse failure must never leave Initialize's
                // Calm behind, which could re-fire a mid-episode storm.
                bool edgeOk = Enum.TryParse(runtime.WeatherEdgeState, ignoreCase: true,
                                  out LakskongenSim.Environment.WeatherState edge)
                              && Enum.IsDefined(edge);
                _lastWeatherState = edgeOk
                    ? edge
                    : Weather?.CurrentState ?? LakskongenSim.Environment.WeatherState.Calm;
                // Saves from before S1-02 carry no episode peak: a running
                // episode is taken to have peaked at its last state.
                _stormEpisodePeakSeverity = runtime.StormEpisodePeakSeverity ?? StormSeverity(_lastWeatherState);

                // Independent of the clock restore: length mismatches must
                // never silently drop the events gating either.
                _enableEvents = runtime.EventsEnabled;

                // Phase 1 wave 6: resume exact RNG stream positions (and the
                // stateful sub-models). Absent keys keep the freshly-seeded
                // stream — legacy saves simply replay from day 0 as before.
                Rng.ImportStates(runtime.SimStreamPositions ?? new());
                if (runtime.PriceCouncilSpots != null)
                    PenPriceCouncil?.ImportState((runtime.PriceCouncilSpots, runtime.PriceCouncilMonthly));
                if (Storyteller != null && runtime.StorytellerLastEventDay != -9999)
                    Storyteller.LastEventDayForSnapshot = runtime.StorytellerLastEventDay;
                if (Storyteller != null && runtime.StorytellerCooldowns != null)
                    Storyteller.CooldownsForSnapshot = runtime.StorytellerCooldowns;
                Feed.ImportState((runtime.FeedFcr, runtime.FeedUsedKg, runtime.FeedGrowthKg,
                    runtime.FeedLastAppetite, runtime.FeedSustainabilityRating));
                if (runtime.TrafficLightColors != null)
                    TrafficLightSystem?.ImportColors(runtime.TrafficLightColors);
                SustainabilityManager?.ImportState(State.SustainabilityScore, State.SustainabilityCertification);
                if (runtime.CurrencyStatePresent)
                    CurrencySystem?.ImportCurrencyState((
                        runtime.CurrencyDay, runtime.CurrencyEur, runtime.CurrencyUsd,
                        runtime.CurrencyJpy, runtime.CurrencyGbp, runtime.CurrencyBankRate));
                FinancialStatements?.ImportState((runtime.FinRevenue, runtime.FinCogs,
                    runtime.FinOpex, runtime.FinInterest, runtime.FinTaxes,
                    runtime.FinCash, runtime.FinAssets, runtime.FinLiabilities,
                    runtime.FinBiomassInventoryValue, runtime.FinCurrentPeriod,
                    runtime.FinEvents ?? new System.Collections.Generic.List<string>()));
                FinancialStatements?.ImportBreakdowns(
                    runtime.FinCostBreakdown, runtime.FinRevenueBreakdown);
                if (runtime.FinLifetime != null)
                    FinancialStatements?.ImportLifetime(runtime.FinLifetime);
                if (runtime.MktHistory != null)
                    MarketSimulation?.ImportState((runtime.MktBase, runtime.MktPrice,
                        runtime.MktWeeklyAvg, runtime.MktLongMean, runtime.MktMeanRev,
                        runtime.MktVol, runtime.MktSupplyIdx, runtime.MktDemandIdx,
                        runtime.MktCertMult, runtime.MktLastR2, runtime.MktLastObs,
                        runtime.MktSimDay, runtime.MktHistory));
                MarketSimulation?.PlayerSupply.ImportPressure(runtime.MktPlayerPressure);
                MarketSimulation?.ImportShockState((runtime.MktShockDays, runtime.MktSupplyOffset));
                if (runtime.PlanktonBloomStates != null)
                    PlanktonBloom?.RestoreBloomStates(runtime.PlanktonBloomStates);
                MarketSimulation?.Intelligence.ImportState((runtime.IntelWeek,
                    runtime.IntelActive ?? new System.Collections.Generic.List<LakskongenSim.Market.MarketNewsEvent>(),
                    runtime.IntelHistory ?? new System.Collections.Generic.List<LakskongenSim.Market.MarketNewsEvent>(),
                    runtime.IntelCompetitors ?? new System.Collections.Generic.List<LakskongenSim.Market.CompetitorProducer>(),
                    runtime.IntelPriceHistory ?? new System.Collections.Generic.List<decimal>(),
                    runtime.IntelBrandScore, runtime.IntelBrandGood, runtime.IntelBrandTotal));
                AIFeeding.ImportState((runtime.AiFeedInstalled, runtime.AiFeedCalibration,
                    runtime.AiFeedDays, runtime.AiFeedCameraUptime, runtime.AiFeedAppetite,
                    runtime.AiFeedWaste, runtime.AiFeedSavings, runtime.AiFeedFcrMult,
                    runtime.AiFeedHistory ?? new System.Collections.Generic.List<double>()));
            }
            else
            {
                _lastWeatherState = Weather?.CurrentState
                    ?? LakskongenSim.Environment.WeatherState.Calm;
                _stormEpisodePeakSeverity = StormSeverity(_lastWeatherState);
            }

            SimLog.Info($"✅ Restored from save: Balance={FinanceManager.Balance:N0} NOK, {loadedState.Populations.Count} populations, Day {loadedState.Day}");
        }

        /// <summary>Ids of every population the engine simulates (sea pens and facility tanks).</summary>
        private IEnumerable<string> LivePopulationIds()
            => State.Populations.Select(p => p.Id)
                .Concat(State.RASFacilities.SelectMany(f => f.Tanks).Select(p => p.Id))
                .Concat(State.OffshoreFarms.SelectMany(f => f.Pens).Select(p => p.Id));

        /// <summary>
        /// Restore the live systems whose state used to be dropped on load.
        /// Each is optional: a save from before the field existed keeps the
        /// freshly initialized system (previous behavior).
        /// </summary>
        private void RestoreLiveSystemsFromState(GameState loadedState)
        {
            if (loadedState.Contracts != null)
                ContractSystem?.ImportState(loadedState.Contracts);
            RestoreScenarioFromState(loadedState);
            if (loadedState.DiseasePrevalence != null)
                Disease?.ImportPrevalence(loadedState.DiseasePrevalence);
            if (loadedState.CompanyLice != null)
                SeaLice?.ImportState(loadedState.CompanyLice);
            if (loadedState.Tax != null)
                TaxSystem?.ImportState((loadedState.Tax.LossCarryForward, loadedState.Tax.Period));
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
            if (loadedState.CompetitorAI != null && CompetitorAI != null)
            {
                CompetitorAI.ImportState(loadedState.CompetitorAI);
            }
            else if (loadedState.CompetitorCompanies?.Count > 0)
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
                        CompetitorName = c.CompetitorName ?? "",
                        SeverityScore = c.SeverityScore,
                        DiscountMultiplier = c.DiscountMultiplier,
                        DurationMonths = c.DurationMonths,
                        StartDay = c.StartDay
                    }, State.Day);
                }
            }
            if (loadedState.DistressedAssets?.Count > 0 && CrisisCapitalism != null)
            {
                foreach (var a in loadedState.DistressedAssets)
                {
                    if (!Enum.TryParse<Events.AssetType>(a.AssetType, out var assetType)) continue;
                    CrisisCapitalism.RestoreAsset(new Events.DistressedAsset
                    {
                        Id = a.Id,
                        AssetType = assetType,
                        Name = a.Name,
                        Description = a.Description,
                        Location = a.Location,
                        OriginalValueNOK = a.OriginalValueNOK,
                        DiscountedPriceNOK = a.DiscountedPriceNOK,
                        TimeRemainingMonths = a.TimeRemainingMonths,
                        ExpiresOnDay = a.ExpiresOnDay,
                        RiskScore = a.RiskScore,
                        CrisisEventId = a.CrisisEventId,
                        OwningCompetitor = a.OwningCompetitor,
                        SourceCompetitorId = a.SourceCompetitorId,
                        SiteId = a.SiteId,
                        LicenseCount = a.LicenseCount,
                        MABTonnes = a.MABTonnes
                    }, State.Day);
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

            // CorporateFinanceSystem — covenants, IPO, tax state (Phase 4.3)
            if (loadedState.CorporateFinance != null && CorporateFinance != null)
                CorporateFinance.ImportState(loadedState.CorporateFinance);

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