using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LakskongenSim.Calibration
{
    /// <summary>
    /// Phase A — Calibration Spine.
    ///
    /// <see cref="CalibrationRegistry"/> is the single source of truth for game
    /// balancing. All economy/labor/infra/ops/health/finance/licensing/biology/
    /// environment/water-quality/compliance/research/market/feed constants live
    /// here, are versioned, and are enforced by <c>CalibrationGuardTests</c>.
    ///
    /// The registry is immutable after construction (init-only records), so it is
    /// safe to share across threads and across subsystems — no caller can mutate
    /// balancing values at runtime and silently drift from the real references in
    /// <c>docs/REALISM_CHECKLIST.md</c>.
    ///
    /// Load order: an external <c>calibration.json</c> file (hot-fix / modding),
    /// falling back to the build-time embedded copy, falling back to code defaults.
    /// </summary>
    public sealed record CalibrationRegistry
    {
        /// <summary>File name probed for on disk.</summary>
        public const string FileName = "calibration.json";

        /// <summary>
        /// Logical name of the embedded fallback. Wired in LakskongenSim.csproj as an
        /// <c>EmbeddedResource</c> pointing at <c>godot/data/calibration.json</c>.
        /// EmbeddedResource was chosen over copy-to-output so that unit tests,
        /// headless tools and the exported game can all load calibration with zero
        /// working-directory assumptions.
        /// </summary>
        public const string EmbeddedResourceName = "LakskongenSim.Calibration.calibration.json";

        /// <summary>Current schema/balancing version. Bump when reconciled values change.</summary>
        public const string DefaultVersion = "1.0.8";

        /// <summary>Calibration schema/balancing version.</summary>
        [JsonPropertyName("version")]
        public string Version { get; init; } = DefaultVersion;

        /// <summary>Economy: capital, smolt, salmon spot price, volatility.</summary>
        [JsonPropertyName("economy")]
        public EconomyCalibration Economy { get; init; } = new();

        /// <summary>Labor: salaries and crew headcount.</summary>
        [JsonPropertyName("labor")]
        public LaborCalibration Labor { get; init; } = new();

        /// <summary>Infrastructure: build capex and production limits.</summary>
        [JsonPropertyName("infrastructure")]
        public InfrastructureCalibration Infrastructure { get; init; } = new();

        /// <summary>Operations: harvest, processing and transport OPEX.</summary>
        [JsonPropertyName("operations")]
        public OperationsCalibration Operations { get; init; } = new();

        /// <summary>Health &amp; treatment: delousing, vaccination, mortality, lice.</summary>
        [JsonPropertyName("health")]
        public HealthCalibration Health { get; init; } = new();

        /// <summary>Finance: insurance, loans, corporate and resource-rent tax.</summary>
        [JsonPropertyName("finance")]
        public FinanceCalibration Finance { get; init; } = new();

        /// <summary>Licensing: MAB and licence market value.</summary>
        [JsonPropertyName("licensing")]
        public LicensingCalibration Licensing { get; init; } = new();

        /// <summary>Biology: smolt/harvest timing and weights, FCR bands.</summary>
        [JsonPropertyName("biology")]
        public BiologyCalibration Biology { get; init; } = new();

        /// <summary>Environment: temperature envelope and fallow period.</summary>
        [JsonPropertyName("environment")]
        public EnvironmentCalibration Environment { get; init; } = new();

/// <summary>Water quality: dissolved oxygen and stocking density.</summary>
        [JsonPropertyName("waterQuality")]
        public WaterQualityCalibration WaterQuality { get; init; } = new();

        /// <summary>Episodic storm model: NVI-attributed mortality with magnitude band validation.</summary>
        [JsonPropertyName("stormEpisode")]
        public StormEpisodeCalibration StormEpisode { get; init; } = new();

        /// <summary>Compliance and certification: fines, ASC/organic costs and premiums.</summary>
        [JsonPropertyName("compliance")]
        public ComplianceCalibration Compliance { get; init; } = new();

        /// <summary>Research: R&amp;D cost envelope.</summary>
        [JsonPropertyName("research")]
        public ResearchCalibration Research { get; init; } = new();

        /// <summary>Market: calibrated Ornstein-Uhlenbeck parameters and seasonality.</summary>
        [JsonPropertyName("market")]
        public MarketCalibration Market { get; init; } = new();

        /// <summary>Feed: feed price per kg feed, base FCR and temperature relationship.</summary>
        [JsonPropertyName("feed")]
        public FeedCalibration Feed { get; init; } = new();

        /// <summary>
        /// Sea temperature (S2-02): the shared anomaly every production area's
        /// climatology moves with (the climatologies themselves are offline data
        /// in <c>Environment/ProductionAreas.cs</c>).
        /// </summary>
        [JsonPropertyName("seaTemperature")]
        public SeaTemperatureCalibration SeaTemperature { get; init; } = new();

        /// <summary>Mortality hazards by cause (S2-05): the temperature-stress band and rate.</summary>
        [JsonPropertyName("mortality")]
        public MortalityCalibration Mortality { get; init; } = new();

        /// <summary>
        /// Sea-phase growth (S2-03): thermal growth coefficient, thermal response,
        /// size tail, growth-ration curve, FCR size factor and cohort weight CV.
        /// </summary>
        [JsonPropertyName("growth")]
        public GrowthCalibration Growth { get; init; } = new();

        /// <summary>
        /// Harvest pipeline (S2-04): live-to-gutted yield, fasting, wellboat and
        /// slaughter capacity and cost, the size-class price ladder and the
        /// quality grades.
        /// </summary>
        [JsonPropertyName("harvest")]
        public HarvestCalibration Harvest { get; init; } = new();

        /// <summary>
        /// Derived feed cost per kg of fish produced = feed price × base FCR.
        /// This is the reconciled "~25.7 NOK/kg produced" figure in
        /// docs/REALISM_CHECKLIST.md; the old model used a flat 12.5 NOK/kg fish,
        /// which double-counted nothing but under-stated real feed cost.
        /// </summary>
        [JsonIgnore]
        public decimal FeedCostPerKgProducedNOK => Feed.FeedPricePerKgFeedNOK * (decimal)Feed.BaseFCR;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private static readonly Lazy<CalibrationRegistry> LazyDefault = new(() => LoadCore());

        /// <summary>Process-wide default registry (embedded/file). Cached after first access.</summary>
        public static CalibrationRegistry Default => LazyDefault.Value;

        /// <summary>
        /// Load the registry. Probes <paramref name="dataDirectory"/> first (when
        /// supplied), then common repo/export locations, then the embedded copy.
        /// Never throws; a malformed external file silently falls back.
        /// </summary>
        public static CalibrationRegistry Load(string? dataDirectory = null) => LoadCore(dataDirectory);
        private static CalibrationRegistry LoadCore(string? dataDirectory = null)
        {
            foreach (var candidate in CandidatePaths(dataDirectory))
            {
                try
                {
                    if (File.Exists(candidate))
                        return Parse(File.ReadAllText(candidate));
                }
                catch
                {
                    // Fall through to the embedded copy — file is optional.
                }
            }

            try
            {
                using var stream = typeof(CalibrationRegistry).Assembly
                    .GetManifestResourceStream(EmbeddedResourceName);
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    return Parse(reader.ReadToEnd());
                }
            }
            catch
            {
                // Fall through to code defaults.
            }

            return new CalibrationRegistry();
        }

        /// <summary>Deserialize a registry from raw JSON (used by tests and Load).</summary>
        public static CalibrationRegistry Parse(string json)
        {
            var registry = JsonSerializer.Deserialize<CalibrationRegistry>(json, JsonOptions)
                           ?? new CalibrationRegistry();
            return string.IsNullOrWhiteSpace(registry.Version)
                ? registry with { Version = DefaultVersion }
                : registry;
        }

        private static IEnumerable<string> CandidatePaths(string? dataDirectory)
        {
            if (!string.IsNullOrWhiteSpace(dataDirectory))
                yield return Path.Combine(dataDirectory!, FileName);

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            yield return Path.Combine("godot", "data", FileName);
            yield return Path.Combine(baseDir, "godot", "data", FileName);
            yield return Path.Combine(baseDir, "..", "..", "..", "..", "..", "godot", "data", FileName);
            yield return Path.Combine(baseDir, "..", "..", "..", "data", FileName);
            yield return Path.Combine("data", FileName);
            yield return Path.Combine(baseDir, "data", FileName);
            yield return Path.Combine(baseDir, "..", "Resources", "data", FileName);
        }
    }

    /// <summary>Economy domain constants (NOK unless stated).</summary>
    public sealed record EconomyCalibration
    {
        /// <summary>Starting cash. Game default 10M.</summary>
        public decimal StartingCapitalNOK { get; init; } = 10_000_000m;
        /// <summary>Smolt purchase price per fish. Real ~12 NOK.</summary>
        public decimal SmoltCostPerFishNOK { get; init; } = 12m;
        /// <summary>Base salmon spot price NOK/kg. Real Oct-2026 ~65-73.</summary>
        public decimal BaseSalmonPricePerKgNOK { get; init; } = 70m;
        /// <summary>Annualised spot volatility, percent.</summary>
        public double PriceVolatilityPercent { get; init; } = 12;
        /// <summary>Peak-to-trough seasonal price swing, percent.</summary>
        public double SeasonalPriceSwingPercent { get; init; } = 18;
    }

    /// <summary>Labor domain constants.</summary>
    public sealed record LaborCalibration
    {
        /// <summary>Worker monthly salary. Real 2024 ~48k NOK.</summary>
        public decimal WorkerMonthlySalaryNOK { get; init; } = 48_000m;
        /// <summary>Site manager monthly salary.</summary>
        public decimal ManagerMonthlySalaryNOK { get; init; } = 70_000m;
        /// <summary>Workers required per sea site.</summary>
        public int WorkersPerSite { get; init; } = 7;
        /// <summary>Workers required per hatchery.</summary>
        public int WorkersPerHatchery { get; init; } = 10;
        /// <summary>Workers required per processing plant.</summary>
        public int WorkersPerProcessingPlant { get; init; } = 20;
    }

    /// <summary>Infrastructure capex and production limits.</summary>
    public sealed record InfrastructureCalibration
    {
        /// <summary>Sea site build cost.</summary>
        public decimal SeaSiteBuildCostNOK { get; init; } = 6_000_000m;
        /// <summary>Hatchery build cost.</summary>
        public decimal HatcheryBuildCostNOK { get; init; } = 18_000_000m;
        /// <summary>Processing plant build cost.</summary>
        public decimal ProcessingPlantCostNOK { get; init; } = 55_000_000m;
        /// <summary>Feed mill build cost.</summary>
        public decimal FeedMillCostNOK { get; init; } = 35_000_000m;
        /// <summary>Max biomass per site (kg).</summary>
        public int MaxBiomassPerSiteKg { get; init; } = 780_000;
        /// <summary>Max sites per zone.</summary>
        public int MaxSitesPerZone { get; init; } = 4;
    }

    /// <summary>Operations OPEX constants.</summary>
    public sealed record OperationsCalibration
    {
        /// <summary>Harvest cost per kg live (wellboat rates). Real ~3-5 NOK/kg. Since S2-04 the engine prices the harvest from <see cref="HarvestCalibration"/>; only SimProbe's synthetic P&amp;L reads this.</summary>
        public decimal HarvestCostPerKgNOK { get; init; } = 4.0m;
        /// <summary>Processing cost per kg.</summary>
        public decimal ProcessingCostPerKgNOK { get; init; } = 9m;
        /// <summary>Transport cost per kg per km.</summary>
        public decimal TransportCostPerKgPerKmNOK { get; init; } = 0.025m;
    }

    /// <summary>Health, treatment and mortality constants.</summary>
    public sealed record HealthCalibration
    {
        /// <summary>Mechanical delousing cost per operation.</summary>
        public decimal DelousingCostMechanicalNOK { get; init; } = 280_000m;
        /// <summary>Chemical delousing cost per operation.</summary>
        public decimal DelousingCostChemicalNOK { get; init; } = 180_000m;
        /// <summary>Cleaner-fish delousing cost per operation.</summary>
        public decimal DelousingCostCleanerfishNOK { get; init; } = 120_000m;
        /// <summary>Vaccination cost per kg biomass.</summary>
        public decimal VaccinationCostPerKgBiomassNOK { get; init; } = 0.5m;
        /// <summary>Baseline daily mortality rate (fraction).</summary>
        public double MortalityRateBasePercent { get; init; } = 0.025;
        /// <summary>Additional daily mortality under ISA outbreak (fraction).</summary>
        public double MortalityRateISAPercent { get; init; } = 0.22;
        /// <summary>Sea lice action limit, adult females per fish. Real 0.5.</summary>
        public double SeaLiceThresholdPerFish { get; init; } = 0.5;
        /// <summary>Proactive treatment trigger, adult females per fish.</summary>
        public double SeaLiceTreatmentThreshold { get; init; } = 0.15;
        /// <summary>
        /// Whole-cycle sea mortality (fraction). Real 2024 ~15.4% (14-16% band),
        /// Vet Institute Fish Health Report.
        /// </summary>
        public double SeaMortalityPerCycle { get; init; } = 0.15;
        /// <summary>
        /// Disease-attributable share (0-1) of <see cref="SeaMortalityPerCycle"/>.
        /// The SIR disease system contributes this slice explicitly when events
        /// are enabled; the remaining (1 - share) is the husbandry baseline. With
        /// events off the whole anchor stays in the baseline so a deterministic
        /// no-disease run still reproduces the 14-16% band. Real mapping: NVI
        /// attributes a large part of the 15.4% all-cause sea loss to infectious
        /// disease; 0.30 keeps disease a visible, tunable slice.
        /// </summary>
        public double SeaMortalityDiseaseShare { get; init; } = 0.30;
    }

    /// <summary>Finance constants.</summary>
    public sealed record FinanceCalibration
    {
        /// <summary>Annual insurance premium as fraction of insured value.</summary>
        public double InsurancePremiumPercent { get; init; } = 0.018;
        /// <summary>Loan interest rate (annual fraction).</summary>
        public double LoanInterestRate { get; init; } = 0.055;
        /// <summary>Max debt-to-collateral multiplier.</summary>
        public double MaxLoanMultiplier { get; init; } = 3.5;
        /// <summary>Norwegian corporate tax rate. Real 22%.</summary>
        public double CorporateTaxRate { get; init; } = 0.22;
        /// <summary>
        /// Resource rent tax effective rate. Real 25% since 1 Jan 2023
        /// (Skatteetaten / Prop.1 LS), with 70M NOK basic deduction.
        /// </summary>
        public double ResourceRentTaxRate { get; init; } = 0.25;
        /// <summary>Basic deduction before resource rent applies (NOK).</summary>
        public decimal ResourceRentBasicDeductionNOK { get; init; } = 70_000_000m;

        /// <summary>
        /// Bankruptcy floor (NOK, negative): the game ends when cash falls below
        /// it (owner decision D16, S1-03). The default is the floor the game
        /// has always enforced (-1M); a scenario can carry its own below.
        /// </summary>
        public decimal BankruptcyFloorNOK { get; init; } = -1_000_000m;

        /// <summary>
        /// Per-scenario bankruptcy floors (scenario id → NOK): the scenario's
        /// working-capital facility with its bank, like a real farmer's
        /// biomass-backed revolving credit. Each is 1.25 × the deepest cash
        /// trough of a harvest-only player on the scenario's starting stock
        /// (ten seeds, events on), rounded out to the next 0.5M NOK
        /// (docs/BALANCE.md §1e). A player who keeps the starting fish alive
        /// reaches the first harvest; one who loses them, or stocks more than
        /// the facility carries without borrowing, does not. Scenarios whose
        /// trough stays above the default floor are not listed.
        /// </summary>
        public IReadOnlyDictionary<string, decimal> BankruptcyFloorByScenarioNOK { get; init; }
            = new Dictionary<string, decimal>
            {
                ["algekatastrofe"] = -26_500_000m,
                ["isa_utbrudd"] = -24_000_000m,
                ["krise_manager"] = -4_500_000m,
                ["lofoten_eventyr"] = -2_000_000m,
                ["po3_red_sone"] = -15_000_000m,
            };

        /// <summary>
        /// Warning ladder before bankruptcy, as fractions of the floor: cash
        /// below floor × f[i] raises warning level i + 1 (overdraft at 0,
        /// bank warning at 0.5, final warning at 0.8). Ascending, in [0, 1).
        /// </summary>
        public IReadOnlyList<double> SolvencyWarningFractions { get; init; } = new[] { 0.0, 0.5, 0.8 };
    }

    /// <summary>Licensing constants.</summary>
    public sealed record LicensingCalibration
    {
        /// <summary>Market value of a standard licence. Real ~150-200M NOK.</summary>
        public decimal LicenseCostNOK { get; init; } = 150_000_000m;
        /// <summary>
        /// Maximum allowable biomass per licence (kg). Standard 780 t
        /// (945 t far north), Fiskeridir.
        /// </summary>
        public int MABLimitKgPerLicense { get; init; } = 780_000;
    }

    /// <summary>Biology timing, weights and FCR bands.</summary>
    public sealed record BiologyCalibration
    {
        /// <summary>Smolt maturation duration (days).</summary>
        public int SmoltMaturationDays { get; init; } = 365;
        /// <summary>Grow-out to harvest duration (days).</summary>
        public int GrowthToHarvestDays { get; init; } = 540;
        /// <summary>
        /// Target harvest live weight (kg). Real band 4.5-5.5 kg
        /// (Mowi Handbook / SalMar).
        /// </summary>
        public double TargetHarvestWeightKg { get; init; } = 5.2;
        /// <summary>Minimum economic harvest weight (kg).</summary>
        public double MinHarvestWeightKg { get; init; } = 3.8;
        /// <summary>Maximum harvest weight before downgrade (kg).</summary>
        public double MaxHarvestWeightKg { get; init; } = 7.5;
        /// <summary>
        /// Optimal (best-practice) FCR. Real biological FCR 1.14-1.15;
        /// economic FCR runs higher.
        /// </summary>
        public double OptimalFCR { get; init; } = 1.15;
        /// <summary>Poor FCR ceiling.</summary>
        public double PoorFCR { get; init; } = 1.35;
    }

    /// <summary>Environment temperature envelope and fallow period.</summary>
    public sealed record EnvironmentCalibration
    {
        /// <summary>Optimal temperature lower bound (°C).</summary>
        public double OptimalTemperatureMinC { get; init; } = 8;
        /// <summary>Optimal temperature upper bound (°C).</summary>
        public double OptimalTemperatureMaxC { get; init; } = 14;
        /// <summary>Lethal temperature ceiling (°C).</summary>
        public double LethalTemperatureMaxC { get; init; } = 22;
        /// <summary>Fallow period between cycles (days).</summary>
        public int FallowPeriodDays { get; init; } = 60;

        /// <summary>
        /// Storm-episode fish-loss fraction at severity 1.0 (a Storm episode
        /// applies half). PROVISIONAL single-variable calibration reconciled
        /// 2026-10-08 against the sea-mortality reference band (14-16%/cycle,
        /// NVI 2024 ≈ 15.4%, see docs/BALANCE.md §1b): the previous in-code
        /// factor 0.02 had never run in a measured probe and pushed cycle
        /// mortality to ~67%. 0.0003 contributes ≈0.5-2% cycle-wide across
        /// ~35 episodes; it MUST graduate into an episodic storm model with
        /// net-integrity exposure and a magnitude band test
        /// (docs/NEXT_LEVEL_PLAN.md follow-ups).
        /// </summary>
        public double StormFishLossBaseFraction { get; init; } = 0.0003;
    }

    /// <summary>
    /// Episodic storm model calibration (NVI-attributed). Replaces the
    /// provisional StormFishLossBaseFraction with a proper episode-based
    /// model that attributes mortality to causes (mechanical damage,
    /// stress, hypoxia, handling) and enforces a magnitude band
    /// (0.5–3% per episode per site). New in Phase 1 wave 6 follow-up.
    /// </summary>
    public sealed record StormEpisodeCalibration
    {
        /// <summary>Base mortality fraction per episode at severity 1.0 (Severe).
        /// Storm (severity 0.5) applies half. This replaces StormFishLossBaseFraction.
        /// </summary>
        public double BaseMortalityFraction { get; init; } = 0.0003;

        /// <summary>Mechanical damage share of episode mortality (net tears, collisions).</summary>
        public double MechanicalDamageShare { get; init; } = 0.35;

        /// <summary>Stress mortality share (cortisol-mediated immune suppression).</summary>
        public double StressMortalityShare { get; init; } = 0.30;

        /// <summary>Hypoxia share (crowding + reduced water exchange during storm).</summary>
        public double HypoxiaMortalityShare { get; init; } = 0.25;

        /// <summary>Handling/secondary share (post-storm handling, transport).</summary>
        public double HandlingMortalityShare { get; init; } = 0.10;

        /// <summary>Net-integrity exposure exponent. 1.0 = linear, >1.0 = super-linear.</summary>
        public double NetIntegrityExposureExponent { get; init; } = 1.2;

        /// <summary>Containment technology mitigation factor per rung (0–1).
        /// Applied multiplicatively to mechanical damage share.
        /// </summary>
        public double ContainmentMitigationPerRung { get; init; } = 0.15;

        /// <summary>Minimum episode mortality magnitude (% of site biomass).
        /// Used for validation/band test. 0.5% = 0.005.
        /// </summary>
        public double MinEpisodeMortalityPct { get; init; } = 0.005;

        /// <summary>Maximum episode mortality magnitude (% of site biomass).
        /// Used for validation/band test. 3% = 0.03.
        /// </summary>
        public double MaxEpisodeMortalityPct { get; init; } = 0.03;

        /// <summary>Roekter (storm crew) mortality reduction multiplier.</summary>
        public double StormCrewMortalityReduction { get; init; } = 0.5;

        /// <summary>Temperature stress multiplier during storm episodes.
        /// Cold water exacerbates stress mortality.
        /// </summary>
        public double ColdWaterStressMultiplier { get; init; } = 1.5;
    }

    /// <summary>Water quality configuration (DO &amp; stocking density).</summary>
    public sealed record WaterQualityCalibration
    {
        /// <summary>DO saturation threshold for stress.</summary>
        public double CriticalDOSaturationThreshold { get; init; } = 0.60;
        /// <summary>DO saturation decay coefficient.</summary>
        public double DOSaturationDecayCoefficient { get; init; } = 0.3;
        /// <summary>DO saturation minimum multiplier.</summary>
        public double DOSaturationMinMultiplier { get; init; } = 0.2;
        /// <summary>DO saturation maximum multiplier.</summary>
        public double DOSaturationMaxMultiplier { get; init; } = 1.0;
        /// <summary>Stocking density threshold (kg/m³). Norwegian limit ~25.</summary>
        public double StockingDensityThresholdKgM3 { get; init; } = 25.0;
        /// <summary>Stocking density reduction denominator.</summary>
        public double StockingDensityReductionDenominator { get; init; } = 75.0;
        /// <summary>Stocking density minimum multiplier.</summary>
        public double StockingDensityMinMultiplier { get; init; } = 0.5;
        /// <summary>Oxygen consumption per kg fish per day (g O₂/kg/day).</summary>
        public double OxygenConsumptionPerKgFishPerDay { get; init; } = 95.0;
        /// <summary>Oxygen consumption per kg feed (g O₂/kg feed).</summary>
        public double OxygenConsumptionPerKgFeed { get; init; } = 480.0;
        /// <summary>Water exchange coefficient.</summary>
        public double WaterExchangeCoefficient { get; init; } = 0.05;
    }

    /// <summary>Compliance and certification constants.</summary>
    public sealed record ComplianceCalibration
    {
        /// <summary>Escape fine per 100 fish.</summary>
        public decimal EscapeFinePer100FishNOK { get; init; } = 500_000m;
        /// <summary>ASC certification cost.</summary>
        public decimal ASCCertificationCostNOK { get; init; } = 2_000_000m;
        /// <summary>Yearly ASC audit cost.</summary>
        public decimal ASCAuditCostYearlyNOK { get; init; } = 500_000m;
        /// <summary>ASC price premium, percent.</summary>
        public double ASCPricePremiumPercent { get; init; } = 10;
        /// <summary>Organic price premium, percent.</summary>
        public double OrganicPricePremiumPercent { get; init; } = 18;
    }

    /// <summary>Research &amp; development cost envelope.</summary>
    public sealed record ResearchCalibration
    {
        /// <summary>Base research node cost.</summary>
        public decimal ResearchBaseCostNOK { get; init; } = 450_000m;
        /// <summary>Maximum research node cost.</summary>
        public decimal ResearchMaxCostNOK { get; init; } = 4_500_000m;
    }

    /// <summary>Calibrated Ornstein-Uhlenbeck market parameters and seasonality.</summary>
    public sealed record MarketCalibration
    {
        /// <summary>Initial spot price (NOK/kg).</summary>
        public decimal InitialPriceNOKPerKg { get; init; } = 88m;
        /// <summary>Price floor (NOK/kg).</summary>
        public decimal PriceFloorNOKPerKg { get; init; } = 42m;
        /// <summary>Price ceiling (NOK/kg).</summary>
        public decimal PriceCeilingNOKPerKg { get; init; } = 150m;
        /// <summary>O-U long-term mean (NOK/kg). 2020-2025 average ~82.</summary>
        public decimal LongTermMeanNOKPerKg { get; init; } = 82m;
        /// <summary>O-U mean-reversion speed (weekly).</summary>
        public decimal MeanReversionSpeedWeekly { get; init; } = 0.012m;
        /// <summary>O-U weekly volatility (NOK/kg).</summary>
        public decimal VolatilityWeekly { get; init; } = 5.0m;
        /// <summary>Mean-reversion speed lower/upper band (weekly).</summary>
        public decimal MeanReversionSpeedMin { get; init; } = 0.009m;
        /// <summary>Mean-reversion speed upper band (weekly).</summary>
        public decimal MeanReversionSpeedMax { get; init; } = 0.016m;
        /// <summary>Weekly volatility lower band (NOK/kg).</summary>
        public decimal VolatilityMin { get; init; } = 5.5m;
        /// <summary>Weekly volatility upper band (NOK/kg).</summary>
        public decimal VolatilityMax { get; init; } = 12.5m;
        /// <summary>Days per discrete market week.</summary>
        public int DaysPerWeek { get; init; } = 7;
        /// <summary>Monthly seasonal price offsets, Jan..Dec (NOK/kg).</summary>
        public decimal[] MonthlySeasonalOffsets { get; init; } =
        {
            3.2m, 4.1m, -1.8m, -3.5m, -2.1m, 0.8m,
            1.2m, -0.5m, -2.8m, -1.9m, 0.3m, 1.7m
        };
    }

    /// <summary>Feed price and conversion constants.</summary>
    public sealed record FeedCalibration
    {
        /// <summary>
        /// Feed price per kg of feed (NOK/kg feed). Cost per kg of fish produced is
        /// this × <see cref="BaseFCR"/>. Reconciled so that price × FCR ≈ 25.7.
        /// </summary>
        public decimal FeedPricePerKgFeedNOK { get; init; } = 22.3m;
        /// <summary>
        /// Base feed conversion ratio. Real biological FCR 1.14-1.15
        /// (SalMar sustainability reports).
        /// </summary>
        public double BaseFCR { get; init; } = 1.15;
        /// <summary>Temperature at which FCR is optimal (°C).</summary>
        public double OptimalFCRTempC { get; init; } = 12.0;
        /// <summary>
        /// FCR change per °C away from optimal. Balance tune (v1.0.1): reduced
        /// from 0.018 to 0.008 — the measured economic FCR median (SimProbe,
        /// seeds 1-20) was ~1.24 at 0.018 versus the bFCR 1.15 reference.
        /// Runtime authority is the matching constant in
        /// <c>FeedSystem</c>; this field is the reconciled record.
        /// </summary>
        public double FCRTempSensitivity { get; init; } = 0.008;
    }

    #region S2-02 sea temperature by production area

    /// <summary>
    /// Shared sea-temperature anomaly (roadmap S2-02). Each production area's
    /// temperature on a day is its climatology (<c>ProductionAreas</c>) plus one
    /// anomaly every area shares: a slow seeded AR(1) process (warm and cold
    /// years and seasons), a short-lived storyteller anomaly (heatwave, cold
    /// snap), the day's short-term fluctuation and the summer storm cooling.
    /// </summary>
    public sealed record SeaTemperatureCalibration
    {
        /// <summary>
        /// Standard deviation of the slow anomaly (°C). 1.0. Source: Sandvik et
        /// al. 2021, ICES J. Mar. Sci. 78(5):1849-1859 (doi:10.1093/icesjms/fsab069):
        /// at 3 m in PO3 the winter minimum and the summer maximum both vary with
        /// SD 1.2 °C between 2012 and 2020 (farm reports); HI 2025-69 states the
        /// normal as the 2012-2024 mean ± 1 SD. 1.0 for the slow part leaves room
        /// for the daily fluctuation below in that 1.2.
        /// </summary>
        public double AnomalyStdDevC { get; init; } = 1.0;

        /// <summary>
        /// E-folding time of the slow anomaly (days). 90. Source: Albretsen &amp;
        /// Asplin 2017 (HI note to the lice expert group, "Fysisk oseanografiske
        /// forhold i produksjonsområdene for akvakultur - oppdatering august 2017"),
        /// monthly 0-5 m anomalies per area 2000-2017: same-signed runs of several
        /// months, i.e. a month-to-month autocorrelation near 0.7 (exp(-30/90) = 0.72).
        /// </summary>
        public double AnomalyEFoldingDays { get; init; } = 90.0;

        /// <summary>
        /// Standard deviation of the day's short-term fluctuation around the slow
        /// anomaly (°C), on calm and moderate days; storm days scale it (2x Storm,
        /// 3x Severe, as before S2-02). 0.3. Game estimate (wind mixing and
        /// upwelling at 3 m), not sourced; the old daily white noise was 1.0.
        /// </summary>
        public double DailyFluctuationStdDevC { get; init; } = 0.3;

        /// <summary>
        /// E-folding time of a storyteller temperature anomaly (heatwave, cold
        /// snap), days. 21. Source: HI 2025-69 section 4: the July 2025 marine
        /// heatwave (3-6 °C above normal) covered most of July, about three weeks.
        /// </summary>
        public double EventAnomalyEFoldingDays { get; init; } = 21.0;
    }

    #endregion

    #region S2-05 mortality by cause

    /// <summary>
    /// Mortality hazards booked by cause (S2-05, roadmap D4). Each hazard
    /// reads the cohort's own environment
    /// (<see cref="Biology.CohortEnvironment"/>), so a land-based tank or an
    /// offshore structure applies the same rule to the water it holds.
    /// </summary>
    public sealed record MortalityCalibration
    {
        /// <summary>
        /// Lower edge of the stress-free water-temperature band: 4 °C (unit °C).
        /// Source: Calado, Mota, Madeira &amp; Leal (2021), "Summer Is Coming!
        /// Tackling Ocean Warming in Atlantic Salmon Cage Farming", Animals
        /// 11(6):1800, doi:10.3390/ani11061800,
        /// https://pmc.ncbi.nlm.nih.gov/articles/PMC8234874/ (growth range
        /// from 4-6 °C). Year 2021.
        /// </summary>
        public double TemperatureStressLowerC { get; init; } = 4.0;

        /// <summary>
        /// Upper edge of the stress-free water-temperature band: 16 °C (unit °C).
        /// Source: Calado et al. (2021), as above: growth optimum 13-16 °C,
        /// feed intake falls from 18-19 °C, ITmax about 22 °C. Year 2021.
        /// </summary>
        public double TemperatureStressUpperC { get; init; } = 16.0;

        /// <summary>
        /// Fraction of a cohort killed per °C outside the band per day:
        /// 0.00005 = 0.005 % of the fish per °C per day (unit 1/(°C·day)).
        /// A game estimate (2026): no published dose-response for sea cages
        /// exists. It is held against Calado et al. (2021), Animals 11(6):1800,
        /// https://pmc.ncbi.nlm.nih.gov/articles/PMC8234874/: a few weeks above
        /// 18 °C have minimal effects and the Tasmanian 2015/16 summer (117 days
        /// above 18 °C, peak about 23 °C) cost appetite and growth, not stock;
        /// at this rate such a summer kills about 1-3 % directly, and a northern
        /// winter at 2 °C for 90 days about 0.9 %. The documentation of
        /// <c>SimulationEngine.ApplySeaTemperatureStress</c> used to say
        /// 0.5 % per °C per day, a hundred times the rate the engine applied;
        /// the engine's rate is the realistic one and the documentation now
        /// matches it (a test reads both).
        /// </summary>
        public double TemperatureStressDailyFractionPerDegreeC { get; init; } = 0.00005;
    }

    #endregion

    #region S2-03 growth (TGC), appetite and ration

    /// <summary>
    /// The sea-phase growth law (roadmap S2-03): growth by the thermal growth
    /// coefficient, a thermal response with an optimum and an upper limit, a
    /// size factor for large fish, appetite (satiation intake) by temperature and
    /// size, the growth response to the ration fed, and the cohort weight CV.
    /// The law takes the water a cohort lives in as an input
    /// (<c>Environment.HoldingWater</c>), so sea pens, offshore hulls and land
    /// tanks at a controlled temperature share it.
    /// </summary>
    public sealed record GrowthCalibration
    {
        /// <summary>
        /// Thermal growth coefficient at full appetite, sea phase:
        /// TGC = (W1^(1/3) − W0^(1/3)) / Σ(°C·day) × 1000, W in grams. 3.0.
        /// Sources: Reid et al. 2013a, commercial Atlantic salmon, average 3.0
        /// (remedial 2.5, elite 3.5), as used by Dempsey et al. 2023, Front. Mar.
        /// Sci. 10:1094247 (doi:10.3389/fmars.2023.1094247); Karlsen et al. 2018,
        /// Sci. Rep. 8:9510 (doi:10.1038/s41598-018-27818-y): 2.80 in an open net
        /// pen and 3.04 in a semi-closed pen over the first 4 months at sea;
        /// Benchmark Genetics 2024 (Arctic Fish, 6.5 kg after 16 months): 3.38.
        /// Roadmap band 2.8-3.2.
        /// </summary>
        public double ThermalGrowthCoefficient { get; init; } = 3.0;

        /// <summary>
        /// Temperature with the highest growth (°C). 13. Source: Kleiven,
        /// Abdel-Fattah &amp; Bøhn 2026, Front. Aquac. 5 (doi:10.3389/faquc.2026.1835152):
        /// "optimal growth is commonly reported around 13 to 14 °C" for post-smolts
        /// and adults in seawater; Calado et al. 2021, Animals 11(6):1800
        /// (doi:10.3390/ani11061800): optimum 13-16 °C. Thyholdt 2014 (Aquac.
        /// Econ. Manag. 18:189-204): warm periods speed growth in the north and
        /// slow it in the south. Below the optimum the law counts degrees (TGC).
        /// </summary>
        public double OptimalTemperatureC { get; init; } = 13.0;

        /// <summary>
        /// Temperature at which growth stops (°C). 22. Source: Calado et al. 2021:
        /// incremental thermal maximum ~22 °C (CTmax ~26 °C); 2 kg salmon held at
        /// 19 °C ate and grew at least 50% less than at 14 °C over 56 days. With
        /// a linear fall of the degree weight from 13 to 22 °C, growth at 19 °C is
        /// 51% of growth at 14 °C.
        /// </summary>
        public double UpperGrowthLimitC { get; init; } = 22.0;

        /// <summary>
        /// Growth per 12 hours of day length above 12 h (photoperiod slope): the
        /// satiation gain is multiplied by 1 + slope × (D − 12) / 12, D the hours of
        /// daylight the holding gets. 0.2 (+20% under continuous light, −20% in
        /// polar night). Game estimate in the range of sea-cage light trials
        /// (Endal et al. 2000, Aquaculture 191:337-349: continuous additional light
        /// raised growth). Source of the effect: Aunsmo et al. 2014, Aquaculture
        /// 428-429:249-257 (doi:10.1016/j.aquaculture.2014.03.007): on 827
        /// Norwegian fish groups the TGC was strongly associated with mean day
        /// length, so a temperature-only TGC is biased along the coast; the Ewos
        /// growth index that fitted best accounts for light. Below the optimum the
        /// law counts degree-days (TGC; valid at 4-14 °C, Alanärä et al. 2001):
        /// growth at 4 °C is 4/13 = 0.31 of the optimum, at 5 °C 0.38.
        /// </summary>
        public double PhotoperiodGrowthSlope { get; init; } = 0.2;

        /// <summary>
        /// Weight (kg) above which the TGC law no longer holds and growth slows
        /// toward <see cref="AsymptoticWeightKg"/>. 3.0. Source: Alanärä et al.
        /// 2001: TGC valid for 100-3000 g; Dumas et al. 2007 (cited by Kleiven et
        /// al. 2026): the cube-root TGC fits intermediate-sized fish better than
        /// very small or large fish.
        /// </summary>
        public double TgcValidToKg { get; init; } = 3.0;

        /// <summary>
        /// Weight (kg) at which growth stops: above <see cref="TgcValidToKg"/> the
        /// cube-root increment falls linearly in cube-root weight to zero here
        /// (a von Bertalanffy tail). 20. Game estimate: large farmed broodstock;
        /// gives ~0.79 of the TGC rate at 5 kg and lets fish grown on pass 6 kg.
        /// </summary>
        public double AsymptoticWeightKg { get; init; } = 20.0;

        /// <summary>
        /// Maintenance ration as a fraction of satiation: the ration at which a
        /// fish neither gains nor loses weight. 0.10. Game estimate on the shape
        /// of Brett 1979 (Fish Physiology VIII, growth-ration curves of salmonids):
        /// maintenance is a small fraction of the maximum ration.
        /// </summary>
        public double MaintenanceRationFraction { get; init; } = 0.10;

        /// <summary>
        /// Curvature of the growth-ration curve
        /// g(r) = (1 − e^(−a(r − m))) / (1 − e^(−a(1 − m))). 0.5 puts the optimum
        /// ration (the best conversion) near 70% of satiation (Brett 1979: the
        /// optimum ration lies below the maximum). Feeding 70% of appetite then
        /// grows the fish 72% as fast at a 2% lower biological FCR; starving
        /// (r = 0) loses weight at 14% of the satiation growth rate.
        /// </summary>
        public double RationCurvature { get; init; } = 0.5;

        /// <summary>
        /// Biological FCR scales with fish size as (W / reference)^exponent:
        /// small fish convert better than large ones. 0.07 (0.81 at 100 g, 1.07 at
        /// 5 kg). Game estimate fitted to the 1.14-1.15 biological FCR of a full
        /// cycle (SalMar sustainability reports, <see cref="FeedCalibration.BaseFCR"/>).
        /// </summary>
        public double FcrSizeExponent { get; init; } = 0.07;

        /// <summary>Weight (kg) at which the size factor of the FCR is 1. 2.0.</summary>
        public double FcrReferenceWeightKg { get; init; } = 2.0;

        /// <summary>
        /// Weight CV of a smolt cohort at sea transfer. 0.18. Source: Karlsen et
        /// al. 2018, Table 1: 101 g smolts, one month after transfer weight SD
        /// 19.6 g (net pen, CV 17%) and 23.8 g (semi-closed, CV 21%).
        /// </summary>
        public double SmoltWeightCv { get; init; } = 0.18;

        /// <summary>
        /// Weight CV a cohort settles at under perfect feed spreading and full
        /// ration. 0.20. Game estimate: harvest classes of a 5 kg Norwegian
        /// harvest spread over 3-7 kg; poor spreading adds up to
        /// <c>FeedSystem.MaxExtraSizeVarianceCv</c>.
        /// </summary>
        public double BaseWeightCv { get; init; } = 0.20;

        /// <summary>
        /// Extra weight CV at zero ration: restricted feeding sharpens competition
        /// and widens the size spread (scaled by 1 − ration). 0.10. Game estimate.
        /// </summary>
        public double RestrictedRationExtraCv { get; init; } = 0.10;

        /// <summary>
        /// How fast the cohort CV moves to its settled value per unit of relative
        /// growth in cube-root weight (dCV = (CV* − CV) × k × ΔL/L). 2.0. Game
        /// estimate: a cohort grown from 100 g to 5 kg covers ~93% of the way.
        /// </summary>
        public double WeightCvRelaxation { get; init; } = 2.0;
    }

    #endregion

    #region S2-04 harvest pipeline

    /// <summary>
    /// The harvest pipeline (roadmap S2-04): what a live kilo becomes at the
    /// slaughterhouse and what it earns. Fish fast before slaughter, a wellboat
    /// carries them to a slaughterhouse with a daily capacity (or a land-based
    /// farm pipes them to its own hall), they are bled and gutted (HOG), sorted
    /// into size classes on the gutted weight and into the industry's quality
    /// grades, and sold at the reference price for superior HOG fish times the
    /// size-class and grade factors, less the harvest cost.
    /// </summary>
    public sealed record HarvestCalibration
    {
        // ── Yield ──────────────────────────────────────────────────────────

        /// <summary>
        /// Gutted (head-on gutted, HOG) weight per kg of live fish. 0.84.
        /// Source: Mowi, Salmon Farming Industry Handbook 2025, appendix "weight
        /// conversion ratios" (https://mowi.com/wp-content/uploads/2025/06/2025-Salmon-Farming-Industry-Handbook.pdf,
        /// p. 114): live fish 100%, loss of blood/starving 6%, offal 10%, gutted
        /// fish (HOG) approx. 84%; the 2018 edition gives 84% too, and the capital
        /// model on p. 75 uses "conversion rate from live weight to GWT: 0.84".
        /// </summary>
        public double HogYieldFraction { get; init; } = 0.84;

        // ── Fasting ────────────────────────────────────────────────────────

        /// <summary>
        /// Degree-days (°C × days) fish fast before slaughter. 30. Regulatory
        /// basis: kvalitetsforskriften (FOR-2013-06-28-844) § 16, farmed fish must
        /// have been without feed long enough that no feed residue can be found
        /// in the stomach or gut; akvakulturdriftsforskriften (FOR-2008-06-17-822)
        /// § 27, the period without feed shall be as short as possible. The
        /// duration is a game estimate, in degree-days because the gut empties
        /// more slowly in cold water: 3 days at 10 °C, 6 at 5 °C, 2 at 15 °C.
        /// </summary>
        public double FastingDegreeDays { get; init; } = 30.0;

        /// <summary>Fewest days of fasting before slaughter, whatever the temperature. 2. Game estimate.</summary>
        public int MinFastingDays { get; init; } = 2;

        // ── Wellboat and slaughterhouse ─────────────────────────────────────

        /// <summary>
        /// Days from ordering a harvest to the first wellboat slot. 5. Game
        /// estimate: the lead time the game's harvest wellboat booking already
        /// used (<c>RequestWellboat("harvest")</c>).
        /// </summary>
        public int WellboatLeadDays { get; init; } = 5;

        /// <summary>
        /// Live fish one harvest wellboat carries to the slaughterhouse a day
        /// (tonnes). 300. Game estimate: one trip a day with a 3,000 m³ wellboat
        /// (Sølvtrans' Ronja Queen class, docs/run/evidence/a3_01_vessels.md) at
        /// about 100 kg/m³; the transport regulation (FOR-2008-06-17-820 § 14)
        /// sets no number, only that density suit the fish's welfare.
        /// </summary>
        public double WellboatCapacityTonnesLivePerDay { get; init; } = 300.0;

        /// <summary>
        /// Gutted fish the player's slaughterhouse contract takes a day (tonnes
        /// HOG). 150. Game estimate: Norway slaughtered 1,408,650 t of gutted
        /// salmon in 2024 (Veterinærinstituttet, Fiskehelserapporten 2024, table
        /// 5.8.1) at about 60 land-based slaughterhouses and 40 bleeding vessels
        /// (ibid. §5.8): about 56 t per facility per working day on average,
        /// large plants several hundred.
        /// </summary>
        public double SlaughterCapacityTonnesHogPerDay { get; init; } = 150.0;

        /// <summary>
        /// Wellboat transport per kg HOG (NOK). 1.55. With
        /// <see cref="SlaughterCostPerKgHogNok"/> the harvest costs 5.0 NOK/kg HOG:
        /// Fiskeridirektoratet's profitability survey 2023, "slakting og transport"
        /// 4.45 NOK per kg round weight (Veterinærinstituttet, Fiskehelserapporten
        /// 2024, table 3.2.1, https://www.vetinst.no/rapporter-og-publikasjoner/rapporter/2025/fiskehelserapporten-2024)
        /// with the report's 12.5% gutting loss (4.45 × 1.125 = 5.0); Mowi Handbook
        /// 2025, p. 75: harvest and processing incl. well boat EUR 0.40 per kg GWT
        /// (≈ 4.7 NOK). Split 31/69 as Marine Harvest Norway 2017 (Handbook 2018,
        /// p. 48: well boat EUR 0.15, primary processing 0.33 per kg GWE).
        /// </summary>
        public decimal WellboatCostPerKgHogNok { get; init; } = 1.55m;

        /// <summary>Slaughter, gutting and packing per kg HOG (NOK). 3.45. Sources: see <see cref="WellboatCostPerKgHogNok"/>.</summary>
        public decimal SlaughterCostPerKgHogNok { get; init; } = 3.45m;

        /// <summary>
        /// Extra cost per kg HOG of a harvest taken today without a plan (NOK):
        /// a spot wellboat and the slaughterhouse's waiting pen, where the fish
        /// fast before slaughter. 1.0. Game estimate.
        /// </summary>
        public decimal ExpressSurchargePerKgHogNok { get; init; } = 1.0m;

        /// <summary>
        /// Wellboat cost of an exposed offshore farm relative to a coastal site.
        /// 1.5. Game estimate (the farm-types spec's offshore wellboat premium).
        /// </summary>
        public decimal OffshoreWellboatCostMultiplier { get; init; } = 1.5m;

        // ── Size-class price ladder ─────────────────────────────────────────
        // Factors on the reference price (superior HOG, the 4-5 kg class), by
        // the GUTTED weight of the fish (the industry's size classes are HOG).
        // Source: Kontali relative prices in Norway, weekly 2015-2024, in Mowi's
        // Salmon Farming Industry Handbook 2025, §6.7 "Different sizes - different
        // prices" (p. 49): 2-3 kg traded at about 86% of 4-5 kg on average
        // (70-97%), 6-7 kg at about 108% (95-150%, peaks in summer); the
        // Handbook 2018 (§5.7, 2008-2016) shows the same pattern. 3-4 and 5-6 kg
        // are interpolated, under 2 kg and over 7 kg extrapolated (game
        // estimates). The reference spot (Nasdaq/SISALMON) is the 3-6 kg basket,
        // within 1% of the 4-5 kg class on these factors.

        /// <summary>Price factor of fish under 1 kg HOG. 0.60. Game estimate (extrapolated).</summary>
        public double PriceFactorUnder1Kg { get; init; } = 0.60;
        /// <summary>Price factor of the 1-2 kg HOG class. 0.72. Game estimate (extrapolated with the 2-3 to 4-5 kg step).</summary>
        public double PriceFactor1To2Kg { get; init; } = 0.72;
        /// <summary>Price factor of the 2-3 kg HOG class. 0.86 (Kontali, 2015-2024 mean).</summary>
        public double PriceFactor2To3Kg { get; init; } = 0.86;
        /// <summary>Price factor of the 3-4 kg HOG class. 0.93 (interpolated).</summary>
        public double PriceFactor3To4Kg { get; init; } = 0.93;
        /// <summary>Price factor of the 4-5 kg HOG class: the reference. 1.00.</summary>
        public double PriceFactor4To5Kg { get; init; } = 1.00;
        /// <summary>Price factor of the 5-6 kg HOG class. 1.04 (interpolated).</summary>
        public double PriceFactor5To6Kg { get; init; } = 1.04;
        /// <summary>Price factor of the 6-7 kg HOG class. 1.08 (Kontali, 2015-2024 mean).</summary>
        public double PriceFactor6To7Kg { get; init; } = 1.08;
        /// <summary>Price factor of fish of 7 kg HOG and more. 1.10. Game estimate (extrapolated).</summary>
        public double PriceFactorOver7Kg { get; init; } = 1.10;

        // ── Quality grades ──────────────────────────────────────────────────
        // After slaughter fish are sorted into superior, ordinary and production
        // (Veterinærinstituttet, Fiskehelserapporten 2024 §5.8). Norway 2024 by
        // weight: superior 80.6%, ordinary 1.5%, production 17.1% (table 5.8.1);
        // superior about 65% in the first half year and 90% in the second (figure
        // 5.8.6); the commonest cause of downgrading is wounds/injuries
        // ("sår/skade"), then maturation, defects and clinical disease.

        /// <summary>Price of ordinary-grade fish relative to superior. 0.90. Game estimate.</summary>
        public double OrdinaryPriceFactor { get; init; } = 0.90;

        /// <summary>
        /// Price of production-grade fish relative to superior. 0.70. Source:
        /// Mowi Handbook 2025, p. 116: "reduced price of downgraded fish (~30%)".
        /// </summary>
        public double ProductionPriceFactor { get; init; } = 0.70;

        /// <summary>
        /// Share (by weight) of a healthy, unwounded batch graded production
        /// (deformities and other defects). 0.05. Game estimate: with the
        /// ordinary share a healthy late-summer batch grades ~92% superior
        /// (Norway, second half of 2024: ~90%).
        /// </summary>
        public double BaseProductionShare { get; init; } = 0.05;

        /// <summary>Share (by weight) graded ordinary. 0.015 (Norway 2024: 20,976 of 1,408,650 t, table 5.8.1).</summary>
        public double OrdinaryShare { get; init; } = 0.015;

        /// <summary>Water colder than this (°C) exposes the fish to winter wounds (winter ulcers). 8. Game estimate.</summary>
        public double WinterWoundTemperatureC { get; init; } = 8.0;

        /// <summary>E-folding time (days) of a population's cold-water exposure memory. 60. Game estimate.</summary>
        public double ColdExposureEFoldingDays { get; init; } = 60.0;

        /// <summary>
        /// Extra production share at full cold-water exposure (a harvest right
        /// after a winter at sea). 0.30. Game estimate, tuned so a spring harvest
        /// grades ~65% superior and a late-summer one ~90% (Norway 2024, H1/H2).
        /// </summary>
        public double WinterWoundProductionShare { get; init; } = 0.30;

        /// <summary>
        /// Extra production share per unit of delousing injury load
        /// (<c>Health.DelousingInjuryState.InjuryLoad</c>; a fresh thermal
        /// treatment adds 0.35). 0.25. Game estimate (injuries after delousing
        /// are among the most important health problems at sea, Fiskehelserapporten 2024).
        /// </summary>
        public double TreatmentWoundProductionSharePerInjury { get; init; } = 0.25;

        /// <summary>Most production share delousing injuries add. 0.5. Game estimate.</summary>
        public double MaxTreatmentWoundProductionShare { get; init; } = 0.5;

        /// <summary>Extra production share per adult female louse per fish (lice wounds). 0.05. Game estimate.</summary>
        public double LiceWoundProductionSharePerLouse { get; init; } = 0.05;

        /// <summary>Most production share lice wounds add. 0.2. Game estimate.</summary>
        public double MaxLiceWoundProductionShare { get; init; } = 0.2;

        /// <summary>
        /// Mean live weight (kg) above which sexual maturation starts to
        /// downgrade a batch. 6.0. Game estimate (maturation is the second
        /// commonest cause of downgrading; fish grown on mature more).
        /// </summary>
        public double MaturationStartWeightKg { get; init; } = 6.0;

        /// <summary>Extra production share per kg of mean live weight above <see cref="MaturationStartWeightKg"/>. 0.08. Game estimate.</summary>
        public double MaturationProductionSharePerKg { get; init; } = 0.08;

        /// <summary>Most production share maturation adds. 0.4. Game estimate.</summary>
        public double MaxMaturationProductionShare { get; init; } = 0.4;

        /// <summary>Harvest reports the game keeps (newest last). 40.</summary>
        public int ReportsKept { get; init; } = 40;
    }

    #endregion
}
