using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using LakskongenSim.Sustainability;
using LakskongenSim.Biology;

namespace LakskongenSim.Core
{
    /// <summary>
    /// Serializable snapshot of the game's state. Designed to be JSON-friendly.
    /// </summary>
    public class GameState
    {
        /// <summary>Current simulation date (day index).</summary>
        public int Day { get; set; }

        /// <summary>
        /// Monetary balance in NOK (deprecated - kept for backward save compatibility).
        /// DO NOT USE - Use FinanceManager.Balance as single source of truth.
        /// This field is only updated during save/load to maintain backward compatibility.
        /// </summary>
        [Obsolete("Use FinanceManager.Balance instead. This field is only for save/load compatibility.")]
        public decimal Balance { get; set; }

        /// <summary>List of zones in the environment.</summary>
        public List<Environment.Zone> Zones { get; set; } = new();

        /// <summary>List of active farms/sea sites.</summary>
        public List<Farm.SeaSite> SeaSites { get; set; } = new();

        /// <summary>List of active RAS facilities.</summary>
        public List<Farm.RASFacility> RASFacilities { get; set; } = new();

        /// <summary>List of active Offshore farms.</summary>
        public List<Farm.OffshoreFarm> OffshoreFarms { get; set; } = new();

        /// <summary>List of active Post-Smolt facilities.</summary>
        public List<Farm.PostSmoltFacility> PostSmoltFacilities { get; set; } = new();

        /// <summary>Whether the player has an AI Feeding System installed.</summary>
        public bool OwnsAIFeedingSystem { get; set; } = false;

        /// <summary>AI Feeding System calibration level (0.0-1.0). Improves over time with data.</summary>
        public double AIFeedingCalibration { get; set; } = 0.5;

        /// <summary>Days since AI Feeding System was installed. Used to track learning curve.</summary>
        public int AIFeedingDaysSinceInstall { get; set; } = 0;

        /// <summary>Fish populations across the system.</summary>
        public List<Biology.FishPopulation> Populations { get; set; } = new();

        /// <summary>
        /// Growth model snapshot (TD-04). Stateless marker today; null for
        /// saves written before snapshots existed.
        /// </summary>
        public Biology.GrowthModelSnapshot? GrowthState { get; set; }

        /// <summary>
        /// Mortality model snapshot (TD-04): live disease rates + accumulated
        /// environmental stress per population. Null for older saves (models
        /// then resume cleared, matching legacy post-load behavior).
        /// </summary>
        public Biology.MortalityModelSnapshot? MortalityState { get; set; }

        /// <summary>Broodstock genomes available for selective breeding.</summary>
        public List<Genome> Broodstock { get; set; } = new();

        /// <summary>Processing P&amp;L snapshot (summary per plant). New systems can push ProcessingSnapshot objects here for dashboards.
        /// Kept as a simple serializable structure so front-end/Dashboard can display aggregate processing profit/loss.
        /// </summary>
        public List<Processing.ProcessingSnapshot> ProcessingSnapshots { get; set; } = new();

        /// <summary>Event history log for the storyteller/event system. Tracks all random events that have occurred.</summary>
        public List<Events.EventRecord> EventLog { get; set; } = new();

        /// <summary>
        /// Memory bound for <see cref="EventLog"/> (TD-10/11): at most this many
        /// event records are retained in memory. Older records are evicted
        /// oldest-first. This is a compile-time constant plus a helper method —
        /// no serialized members are added, so the save format is unchanged.
        /// </summary>
        public const int MaxEventLogEntries = 1000;

        /// <summary>
        /// Evict oldest event-log entries beyond <see cref="MaxEventLogEntries"/>.
        /// Call after appending to <see cref="EventLog"/>.
        /// </summary>
        public void TrimEventLog()
        {
            if (EventLog.Count > MaxEventLogEntries)
                EventLog.RemoveRange(0, EventLog.Count - MaxEventLogEntries);
        }

        /// <summary>
        /// Daily analytics snapshots recorded by AnalyticsTracker.
        /// One entry per simulated day; used by charting UI and trend analysis.
        /// </summary>
        public List<DailySnapshot> DailySnapshots { get; set; } = new();

        // ─── Research / Tech Tree ──────────────────────────────────────────────

        /// <summary>IDs of all research nodes the player has completed.</summary>
        public List<string> UnlockedResearchIds { get; set; } = new();

        /// <summary>ID of the research node currently being studied (null if idle).</summary>
        public string? ActiveResearchId { get; set; }

        /// <summary>Days of progress accumulated on the active research.</summary>
        public int ResearchProgress { get; set; }

        // ─── Achievement Tracking ──────────────────────────────────────────────

        /// <summary>Export markets the player has sold to (e.g., "EU", "USA", "China", "Japan", "UK", "MiddleEast").</summary>
        public HashSet<string> ExportMarkets { get; set; } = new();

        /// <summary>Number of competitor farms acquired.</summary>
        public int AcquisitionCount { get; set; }

        /// <summary>Whether the player has an active long-term contract.</summary>
        public bool ContractActive { get; set; }

        /// <summary>Whether the player owns a feed mill.</summary>
        public bool OwnsFeedMill { get; set; }

        /// <summary>Whether the player owns a hatchery.</summary>
        public bool OwnsHatchery { get; set; }

        /// <summary>Whether the player owns a processing plant.</summary>
        public bool OwnsProcessingPlant { get; set; }

        /// <summary>VAP plant tier (Phase 3.5): "", "Small", "Medium", "Large".</summary>
        public string ProcessingTier { get; set; } = "";

        /// <summary>Feed recipe sliders (Phase 4.4): protein 38-48, replacement
        /// 0-60, astaxanthin 0-80 ppm.</summary>
        public double RecipeProteinPct { get; set; } = 42.0;
        public double RecipeReplacePct { get; set; } = 20.0;
        public double RecipeAstaxPpm { get; set; } = 50.0;

        /// <summary>Night-shift ROC policy (Phase 5.2).</summary>
        public LakskongenSim.Logistics.NightShiftPolicy NightShiftPolicy { get; set; } =
            LakskongenSim.Logistics.NightShiftPolicy.Standard;

        /// <summary>Whether the player has achieved ASC certification.</summary>
        public bool HasASCCertification { get; set; }

        /// <summary>Best (lowest) FCR achieved by the player.</summary>
        public double BestFCR { get; set; } = double.MaxValue;

        /// <summary>Highest breeding value achieved for growth trait.</summary>
        public double BestGrowthBreedingValue { get; set; }

        /// <summary>Highest breeding value achieved for disease resistance trait.</summary>
        public double BestDiseaseResistanceBreedingValue { get; set; }

        /// <summary>Historical minimum balance (to detect bankruptcy recovery).</summary>
        public decimal MinimumBalanceEver { get; set; } = decimal.MaxValue;

        /// <summary>Feed sustainability rating (0.0-1.0) based on sustainable feed purchases. 1.0 = 100% sustainable sources.</summary>
        public double FeedSustainabilityRating { get; set; } = 0.7;

        /// <summary>Sustainability score (0-100) computed by SustainabilityManager.</summary>
        public int SustainabilityScore { get; set; }

        /// <summary>Current certification level based on sustainability score.</summary>
        public CertificationLevel SustainabilityCertification { get; set; }

        /// <summary>Compact normalized external aquaculture data snapshot used for gameplay calibration.</summary>
        public ExternalAquacultureSnapshot? ExternalAquacultureSnapshot { get; set; }

        /// <summary>External-data analytics summary for UI/reporting surfaces.</summary>
        public ExternalDataAnalyticsSummary ExternalDataAnalytics { get; set; } = new();

        /// <summary>Latest fish-health warnings derived from BarentsWatch/industry snapshot data.</summary>
        public List<HealthTrendWarning> ExternalHealthWarnings { get; set; } = new();

        /// <summary>Current dynamic crisis scenario derived from external fish-health pressure.</summary>
        public HealthCrisisScenario ExternalHealthCrisis { get; set; } = new();

        /// <summary>Compact report for gameplay/UI messaging around external health pressure.</summary>
        public ExternalHealthAnalyticsReport ExternalHealthAnalytics { get; set; } = new();

        /// <summary>IDs of unlocked achievements.</summary>
        public HashSet<string> UnlockedAchievementIds { get; set; } = new();

        /// <summary>Active scenario ID, if any.</summary>
        public string? ActiveScenarioId { get; set; }

        /// <summary>Whether the active scenario has been won.</summary>
        public bool ScenarioIsWon { get; set; }

        /// <summary>Whether the active scenario has been lost.</summary>
        public bool ScenarioIsLost { get; set; }

        /// <summary>Reason for scenario loss, if any.</summary>
        public string ScenarioLossReason { get; set; } = "";

        /// <summary>Day the scenario started (day index).</summary>
        public int ScenarioStartDay { get; set; }

        /// <summary>Current tutorial step index (0-based).</summary>
        public int TutorialCurrentStepIndex { get; set; }

        /// <summary>IDs of completed tutorial steps.</summary>
        public HashSet<string> CompletedTutorialStepIds { get; set; } = new();

        /// <summary>Logistics manager for vehicle fleet and carbon footprint tracking.</summary>
        [JsonIgnore]
        public Logistics.LogisticsManager? LogisticsManager { get; set; }

        // ─── Empire Systems State (T74: Save/Load serialization) ───────────────

        /// <summary>CompetitorAI: Serialized competitor company states.</summary>
        public List<CompetitorCompanyState> CompetitorCompanies { get; set; } = new();

        /// <summary>EmpireProgression: Current empire phase and unlocked milestones.</summary>
        public EmpireProgressionState? EmpireProgression { get; set; }

        /// <summary>StockMarket: Ownership status, share price, price history.</summary>
        public StockMarketState? StockMarket { get; set; }

        /// <summary>BrandReputation: Brand scores, certifications, strategy.</summary>
        public BrandReputationState? BrandReputation { get; set; }

        /// <summary>CrisisCapitalism: Active crisis events.</summary>
        public List<CrisisEventState> ActiveCrises { get; set; } = new();

        /// <summary>VerticalIntegration: Owned assets and integration score.</summary>
        public VerticalIntegrationState? VerticalIntegration { get; set; }

        /// <summary>LoanSystem: Active loans with principal and interest rates.</summary>
        public List<LoanState> ActiveLoans { get; set; } = new();

        /// <summary>TaxSystem: Tax payment history.</summary>
        public List<TaxPaymentRecord> TaxHistory { get; set; } = new();

        /// <summary>InvestorSystem: Investors and equity dilution.</summary>
        public InvestorSystemState? InvestorSystem { get; set; }

        /// <summary>AcquisitionSystem: Owned acquisitions.</summary>
        public List<AcquisitionRecord> Acquisitions { get; set; } = new();

        /// <summary>LicenseSystem: Active licenses with expiry dates.</summary>
        public List<LicenseRecord> Licenses { get; set; } = new();

        /// <summary>StakeholderFactionSystem: Faction relations.</summary>
        public Dictionary<string, decimal> FactionRelations { get; set; } = new();

        /// <summary>MarketIntelligence: Recent market reports (last 4 weeks, regenerate older).</summary>
        public List<MarketReportSnapshot> RecentMarketReports { get; set; } = new();

        /// <summary>FishWelfare: Current welfare condition score.</summary>
        public decimal FishWelfareCondition { get; set; } = 1.0m;

        /// <summary>Spot-hire wellboat jobs awaiting execution (Phase 2.4).</summary>
        public List<Logistics.WellboatJob> WellboatJobs { get; set; } = new();

        /// <summary>Staff roster (Phase 3.1).</summary>
        public List<CrewMember> Crew { get; set; } = new();

        /// <summary>Active bloom/hypoxia events (Phase 4.3).</summary>
        public List<Environment.BloomEvent> ActiveBlooms { get; set; } = new();

        /// <summary>VisualMilestones: Achieved milestone IDs.</summary>
        public HashSet<string> AchievedVisualMilestones { get; set; } = new();

        // ─── Depth Systems State (P9/P11: Save/Load serialization) ─────────────

        /// <summary>P11 InsuranceSystem: subscribed catastrophe policies
        /// (kind, premium, coverage, deductible). Additive; empty for old saves.</summary>
        public List<InsurancePolicyState> InsurancePolicies { get; set; } = new();

        /// <summary>P11 bloom mitigation registry: installed non-flag-backed
        /// mitigations as (siteId, kind key). Additive; empty for old saves.</summary>
        public List<BloomMitigationState> BloomMitigations { get; set; } = new();

        /// <summary>P9: whether the 25% resource-rent tax is charged (opt-in per scenario).</summary>
        public bool EnableResourceRentTax { get; set; }

        /// <summary>P9: cumulative resource-rent tax paid (NOK).</summary>
        public decimal ResourceRentPaid { get; set; }

        /// <summary>P9: last binding monthly pen price set by the Price Council (NOK/kg).</summary>
        public decimal PenPriceCouncilLastValue { get; set; }

        /// <summary>Default constructor for serializer.</summary>
        public GameState()
        {
        }
    }

    // ─── Empire Systems Serialization DTOs (T74) ────────────────────────────

    /// <summary>Serializable snapshot of a CompetitorCompany.</summary>
    public class CompetitorCompanyState
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public decimal Revenue { get; set; }
        public decimal EBITDA { get; set; }
        public decimal MarketCap { get; set; }
        public decimal DebtLevel { get; set; }
        public decimal CashReserves { get; set; }
        public int LicenseCount { get; set; }
        public float TotalMABTonnes { get; set; }
        public int SiteCount { get; set; }
        public float MarketShareNorway { get; set; }
        public float MarketShareGlobal { get; set; }
        public float WeeklyHarvestTonnes { get; set; }
        public bool IsDistressed { get; set; }
        public bool IsAcquisitionTarget { get; set; }
        public decimal AcquisitionPrice { get; set; }
    }

    /// <summary>Serializable snapshot of EmpireProgressionSystem.</summary>
    public class EmpireProgressionState
    {
        public string CurrentPhase { get; set; } = "SmallFarmer";
        public List<string> UnlockedFeatures { get; set; } = new();
        public List<string> UnlockedMilestoneIds { get; set; } = new();
        public List<string> AchievedVictoryIds { get; set; } = new();
    }

    /// <summary>Serializable snapshot of StockMarketSystem.</summary>
    public class StockMarketState
    {
        public string Status { get; set; } = "Private";
        public decimal SharePrice { get; set; }
        public List<decimal> PriceHistory { get; set; } = new();
        public int OutstandingShares { get; set; }
    }

    /// <summary>Serializable snapshot of BrandReputation.</summary>
    public class BrandReputationState
    {
        public string BrandName { get; set; } = string.Empty;
        public string Strategy { get; set; } = "Commodity";
        public decimal QualityReputation { get; set; }
        public decimal SustainabilityReputation { get; set; }
        public HashSet<string> Certifications { get; set; } = new();
    }

    /// <summary>Serializable snapshot of a CrisisEvent.</summary>
    public class CrisisEventState
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string AffectedCompetitorId { get; set; } = string.Empty;
        public float SeverityScore { get; set; }
        public int DurationMonths { get; set; }
        public DateTime StartDate { get; set; }
    }

    /// <summary>Serializable snapshot of VerticalIntegrationSystem.</summary>
    public class VerticalIntegrationState
    {
        public bool OwnsFeedMill { get; set; }
        public bool OwnsHatchery { get; set; }
        public bool OwnsProcessing { get; set; }
        public bool OwnsColdChain { get; set; }
        public bool OwnsDistribution { get; set; }
        public float IntegrationScore { get; set; }
    }

    /// <summary>Serializable snapshot of a Loan.</summary>
    public class LoanState
    {
        public string Id { get; set; } = string.Empty;
        public decimal Principal { get; set; }
        public decimal InterestRate { get; set; }
        public int TermDays { get; set; }
        public int DaysTaken { get; set; }
    }

    /// <summary>Serializable snapshot of a tax payment.</summary>
    public class TaxPaymentRecord
    {
        public int Day { get; set; }
        public decimal Amount { get; set; }
        public string TaxType { get; set; } = string.Empty;
    }

    /// <summary>Serializable snapshot of InvestorSystem.</summary>
    public class InvestorSystemState
    {
        public int OutstandingShares { get; set; }
        public decimal OutstandingBondPrincipal { get; set; }
        public List<InvestorRecord> Investors { get; set; } = new();
    }

    /// <summary>Serializable investor record.</summary>
    public class InvestorRecord
    {
        public string Name { get; set; } = string.Empty;
        public int SharesOwned { get; set; }
        public decimal InvestmentAmount { get; set; }
    }

    /// <summary>Serializable acquisition record.</summary>
    public class AcquisitionRecord
    {
        public string TargetId { get; set; } = string.Empty;
        public string TargetName { get; set; } = string.Empty;
        public decimal PurchasePrice { get; set; }
        public int DayAcquired { get; set; }
    }

    /// <summary>Serializable license record.</summary>
    public class LicenseRecord
    {
        public string SiteId { get; set; } = string.Empty;
        public int ExpiryDay { get; set; }
        public decimal AnnualFee { get; set; }
    }

    /// <summary>Serializable market intelligence report snapshot.</summary>
    public class MarketReportSnapshot
    {
        public int Day { get; set; }
        public decimal SpotPrice { get; set; }
        public string Sentiment { get; set; } = string.Empty;
    }

    // ─── Depth Systems Serialization DTOs (P9/P11) ──────────────────────────

    /// <summary>Serializable snapshot of an <c>InsuranceSystem</c> policy.
    /// <see cref="Kind"/> is the <c>InsuranceKind</c> name (Mortality/Bloom/Storm/ISA).</summary>
    public class InsurancePolicyState
    {
        public string Kind { get; set; } = string.Empty;
        public double Premium { get; set; }
        public double Coverage { get; set; }
        public decimal Deductible { get; set; }
    }

    /// <summary>Serializable snapshot of an installed bloom mitigation.
    /// <see cref="Kind"/> is the bridge key (bubble/diffuser/pumping/deep_feeding).</summary>
    public class BloomMitigationState
    {
        public string SiteId { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
    }
}
