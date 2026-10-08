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
        public const string DefaultVersion = "1.0.4";

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

        /// <summary>Compliance &amp; certification: fines, ASC/organic costs and premiums.</summary>
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
        /// <summary>Harvest cost per kg (wellboat rates). Real ~3-5 NOK/kg.</summary>
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
}
