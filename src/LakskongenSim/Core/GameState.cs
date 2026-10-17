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

        /// <summary>RNG seed chosen at New Game (S10). Persisted so a game is
        /// reproducible from day 0 and bug reports carry their seed. 0 means
        /// "pre-seed save / unset" — treated as unseeded on load.</summary>
        public int RandomSeed { get; set; }

        /// <summary>
        /// Monetary balance in NOK (deprecated - kept for backward save compatibility).
        /// DO NOT USE - Use FinanceManager.Balance as single source of truth.
        /// This field is only updated during save/load to maintain backward compatibility.
        /// </summary>
        [Obsolete("Use FinanceManager.Balance instead. This field is only for save/load compatibility.")]
        public decimal Balance { get; set; }

        /// <summary>List of zones in the environment.</summary>
        public List<Environment.Zone> Zones { get; set; } = new();

        /// <summary>
        /// Everything in the water (F2-03): fjord localities and offshore units
        /// (<see cref="Farm.SeaSite.Kind"/>).
        /// </summary>
        public List<Farm.SeaSite> SeaSites { get; set; } = new();

        /// <summary>
        /// Everything on land (F2-03): the land sites, grow-out and post-smolt
        /// (<see cref="Farm.RASFacility.Kind"/>; the name is kept for save,
        /// bridge and test compatibility, players read "Landanlegg").
        /// </summary>
        public List<Farm.RASFacility> RASFacilities { get; set; } = new();

        /// <summary>
        /// Load-only (F2-03): a pre-F2 save's offshore farms. The load migrates
        /// each to an offshore <see cref="Farm.SeaSite"/> and empties the list
        /// (<c>SimulationEngine.FacilityMigration.cs</c>).
        /// </summary>
        public List<Farm.OffshoreFarm> OffshoreFarms { get; set; } = new();

        /// <summary>
        /// Load-only (F2-03): a pre-F2 save's post-smolt facilities. The load
        /// migrates each to a post-smolt land site and empties the list.
        /// </summary>
        public List<Farm.PostSmoltFacility> PostSmoltFacilities { get; set; } = new();

        /// <summary>
        /// The game's farm-type plots (F2-03): the state of each land pad and
        /// offshore slot a unit stands on or could stand on (owner, zoning,
        /// grid feeds); the attributes come from the catalogue
        /// (<see cref="Farm.SitePlotCatalog"/>) and are not saved. Empty until the
        /// game first builds or migrates a land site or an offshore unit.
        /// </summary>
        public List<Farm.SitePlot> SitePlots { get; set; } = new();

        /// <summary>
        /// F2-04: the F2 units' fixed-asset register (docs/FARM_TYPES_SPEC.md
        /// §3.6), depreciated straight-line as a non-cash P&amp;L line. Empty in a
        /// game that never built a land site or an offshore unit.
        /// </summary>
        public List<Finance.FacilityAsset> FacilityAssets { get; set; } = new();

        /// <summary>F2-04: facility construction projects (spec §3.2), finished ones included.</summary>
        public List<ConstructionProject> ConstructionProjects { get; set; } = new();

        /// <summary>F2-04: facility permit cases (spec §3.2), decided ones included.</summary>
        public List<Finance.PermitCase> PermitCases { get; set; } = new();

        /// <summary>F2-04: number of the next construction project ("cp_N"); ids are never reused.</summary>
        public int NextConstructionProjectNumber { get; set; } = 1;

        /// <summary>F2-04: number of the next permit case ("pc_N"); ids are never reused.</summary>
        public int NextPermitCaseNumber { get; set; } = 1;

        /// <summary>
        /// F2-04: this fiscal year's costs of units that are not liable to
        /// resource-rent tax (every land site; an offshore unit on licences the
        /// rent does not reach), booked in the P&amp;L like any cost and left out
        /// of the resource-rent deductions (the rent base is ring-fenced,
        /// spec §3.6). Reset at the year-end tax settlement.
        /// </summary>
        public decimal RentRingFencedCostsThisTaxYear { get; set; }

        /// <summary>
        /// F2-05: the Driftsplan, the company's standing orders (one per unit
        /// and policy, sorted by unit and policy; docs/FARM_TYPES_SPEC.md §3.1
        /// decision 7). Empty in a game where the player set none.
        /// </summary>
        public List<DriftsplanEntry> Driftsplan { get; set; } = new();

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

        /// <summary>
        /// The player's home production area (1-13, S2-02): the area of the
        /// scenario's first starting site. The HUD sea temperature, land-based
        /// facilities' seawater intake and zones that name no area use it. 0 =
        /// not set (no scenario, legacy save): the engine falls back to the first
        /// site's area, else <see cref="Environment.ProductionAreas.NationalReference"/>.
        /// </summary>
        public int HomeProductionArea { get; set; }

        /// <summary>Whether the active scenario has been won.</summary>
        public bool ScenarioIsWon { get; set; }

        /// <summary>Whether the active scenario has been lost.</summary>
        public bool ScenarioIsLost { get; set; }

        /// <summary>Reason for scenario loss, if any.</summary>
        public string ScenarioLossReason { get; set; } = "";

        /// <summary>Day the scenario started (day index).</summary>
        public int ScenarioStartDay { get; set; }

        /// <summary>
        /// Highest bankruptcy-warning rung announced since cash was last at
        /// zero or above (0-3, S1-03): each rung warns once on the way down.
        /// </summary>
        public int SolvencyWarningPeak { get; set; }

        /// <summary>Day cash fell below the bankruptcy floor (null while solvent, S1-03).</summary>
        public int? BankruptDay { get; set; }

        /// <summary>
        /// The liquidity ladder (U2-03): closing-cash history, the bank's
        /// warning and call, the rescue offer, overdraft interest paid.
        /// </summary>
        public Finance.LiquidityState Liquidity { get; set; } = new();

        /// <summary>The P&amp;L story (U2-03): months, turning points and category totals.</summary>
        public Finance.PnlStoryState PnlStory { get; set; } = new();

        /// <summary>Current tutorial step index (0-based).</summary>
        public int TutorialCurrentStepIndex { get; set; }

        /// <summary>IDs of completed tutorial steps.</summary>
        public HashSet<string> CompletedTutorialStepIds { get; set; } = new();

        /// <summary>
        /// U2-02: the player skipped the tutorial. Every step then counts as
        /// done for the panels, but the Tutorial win condition stays false
        /// (owner decision D19: skipping the tutorial is not a win). Save v11.
        /// </summary>
        public bool TutorialSkipped { get; set; }

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
        public List<DistressedAssetState> DistressedAssets { get; set; } = new();

        /// <summary>VerticalIntegration: Owned assets and integration score.</summary>
        public VerticalIntegrationState? VerticalIntegration { get; set; }

        /// <summary>LoanSystem: Active loans with principal and interest rates.</summary>
        public List<LoanState> ActiveLoans { get; set; } = new();

        /// <summary>TaxSystem: Tax payment history.</summary>
        public List<TaxPaymentRecord> TaxHistory { get; set; } = new();

        /// <summary>InvestorSystem: Investors and equity dilution.</summary>
        public InvestorSystemState? InvestorSystem { get; set; }

        /// <summary>CorporateFinanceSystem: Covenants, IPO, tax state (Phase 4.3).</summary>
        public CorporateFinanceSystemState? CorporateFinance { get; set; }

        /// <summary>AcquisitionSystem: Owned acquisitions.</summary>
        public List<AcquisitionRecord> Acquisitions { get; set; } = new();

        /// <summary>LicenseSystem: Active licenses with expiry dates.</summary>
        public List<LicenseRecord> Licenses { get; set; } = new();

        /// <summary>Phase 4.1: green-zone licence auctions (open + settled).</summary>
        public List<Finance.LicenseAuctionState> LicenseAuctions { get; set; } = new();

        // ─── Capacity v1 (S2-01a): licences and MTB ────────────────────────────

        /// <summary>
        /// The capacity rules this game runs under. A new engine starts
        /// <see cref="Finance.CapacityRegime.Unregulated"/>; a scenario start opens
        /// the licence register (<see cref="Finance.CapacityRegime.LicencesV1"/>).
        /// Null only in a save written before S2-01: the load migrates it (each
        /// site's old MAB becomes one licence in its production area).
        /// </summary>
        public Finance.CapacityRegime? CapacityRules { get; set; }

        /// <summary>The company's sea licences (S2-01a); their MTB pools per production area.</summary>
        public List<Finance.LicenceHolding> LicenceHoldings { get; set; } = new();

        /// <summary>Licences granted this game: the sequence in each licence's deterministic id.</summary>
        public int LicencesGranted { get; set; }

        /// <summary>Biomass-over-MTB episodes (S2-01a): every open one and the latest closed ones.</summary>
        public List<Finance.CapacityBreach> CapacityBreaches { get; set; } = new();

        /// <summary>StakeholderFactionSystem: Faction relations.</summary>
        public Dictionary<string, decimal> FactionRelations { get; set; } = new();

        /// <summary>MarketIntelligence: Recent market reports (last 4 weeks, regenerate older).</summary>
        public List<MarketReportSnapshot> RecentMarketReports { get; set; } = new();

        /// <summary>FishWelfare: Current welfare condition score.</summary>
        public decimal FishWelfareCondition { get; set; } = 1.0m;

        /// <summary>Spot-hire wellboat jobs awaiting execution (Phase 2.4).</summary>
        public List<Logistics.WellboatJob> WellboatJobs { get; set; } = new();

        /// <summary>Wellboat bookings made this game: the sequence number in each job's deterministic id (P0-07).</summary>
        public int WellboatJobsBooked { get; set; }

        /// <summary>
        /// Harvest orders (S2-04): fish fasting toward a wellboat slot or being
        /// slaughtered batch by batch within the slaughterhouse's daily
        /// capacity. Finished and cancelled orders are dropped. Empty in saves
        /// from before S2-04.
        /// </summary>
        public List<Harvest.HarvestOrder> HarvestOrders { get; set; } = new();

        /// <summary>Harvest orders placed this game: the sequence number in each order's deterministic id (S2-04).</summary>
        public int HarvestOrdersPlaced { get; set; }

        /// <summary>
        /// The latest harvest reports (S2-04), newest last, one per slaughtered
        /// batch: fish, live and gutted kilos, size classes, grades, prices and
        /// the harvest cost exactly as booked in the ledger.
        /// </summary>
        public List<Harvest.HarvestReport> HarvestReports { get; set; } = new();

        /// <summary>Slaughtered batches sold this game: the sequence number in each harvest report's id (S2-04).</summary>
        public int HarvestBatchesSold { get; set; }

        /// <summary>Gutted (HOG) kilos sold since the game started (S2-04).</summary>
        public double TotalHarvestedHogKg { get; set; }

        /// <summary>Live kilos slaughtered since the game started (S2-04).</summary>
        public double TotalHarvestedLiveKg { get; set; }

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

        /// <summary>
        /// P9: whether the 25% resource-rent tax is charged. On by default
        /// (owner decision D16, S1-03); a scenario may opt out with
        /// enable_resource_rent_tax=false. A save keeps the value it was
        /// written with.
        /// </summary>
        public bool EnableResourceRentTax { get; set; } = true;

        /// <summary>P9: cumulative resource-rent tax paid (NOK).</summary>
        public decimal ResourceRentPaid { get; set; }

        /// <summary>Cumulative sales revenue (harvests, forced harvests, sludge) since the game started.</summary>
        public decimal TotalSalesRevenueNok { get; set; }

        /// <summary>
        /// Cumulative revenue from selling fish (harvests and forced harvests,
        /// sea or facility) since the game started; excludes sludge. The
        /// tutorial's harvest-and-sale steps read this, not the sales total.
        /// </summary>
        public decimal TotalHarvestRevenueNok { get; set; }

        /// <summary>Cash at the start of the game or scenario (profit/net-worth achievements).</summary>
        public decimal OpeningBalanceNok { get; set; }

        /// <summary>Harvested kg in the current fiscal year (resource-rent base).</summary>
        public double HarvestedKgThisTaxYear { get; set; }

        /// <summary>This fiscal year's harvest valued at the norm price (resource-rent base).</summary>
        public decimal ResourceRentHarvestValueThisTaxYear { get; set; }

        /// <summary>
        /// This fiscal year's harvest cost (wellboat, slaughter, packing; S2-04):
        /// booked as cost of goods sold but not a sea-phase cost, so it is left
        /// out of the resource-rent deductions (skatteloven § 19-5/19-6).
        /// </summary>
        public decimal HarvestCostThisTaxYear { get; set; }

        /// <summary>P9: last binding monthly pen price set by the Price Council (NOK/kg).</summary>
        public decimal PenPriceCouncilLastValue { get; set; }

        /// <summary>
        /// Engine runtime snapshot (interval clocks, storm edge, event gating).
        /// Null for saves written before Phase 1 wave 5 — those falls back to
        /// the align-with-current-weather restore.
        /// </summary>
        public EngineRuntimeSnapshot? Runtime { get; set; }

        // ── Live-system state persisted since the save/load matrix (review F409,
        //    F1071, F478, F1137). Null in older saves: the load path keeps the
        //    freshly initialized system (or the legacy DTO) in that case. ──

        /// <summary>Contract book: active/completed contracts, week, id counter, today's offers.</summary>
        public Market.ContractSystemState? Contracts { get; set; }

        /// <summary>Full competitor roster and quarter clock (supersedes <see cref="CompetitorCompanies"/>).</summary>
        public Finance.CompetitorAISnapshot? CompetitorAI { get; set; }

        /// <summary>Disease prevalence per FishPopulation.Id (population-level SIR).</summary>
        public Dictionary<string, double>? DiseasePrevalence { get; set; }

        /// <summary>Company-wide sea-lice model (drives disease transmission).</summary>
        public Health.SeaLiceModelState? CompanyLice { get; set; }

        /// <summary>Tax loss carry-forward and period counter.</summary>
        public TaxSystemState? Tax { get; set; }

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
        public string CompetitorName { get; set; } = string.Empty;
        public float SeverityScore { get; set; }
        public float DiscountMultiplier { get; set; }
        public int DurationMonths { get; set; }
        public int StartDay { get; set; }
    }

    /// <summary>Serializable snapshot of a DistressedAsset.</summary>
    public class DistressedAssetState
    {
        public string Id { get; set; } = string.Empty;
        public string AssetType { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public decimal OriginalValueNOK { get; set; }
        public decimal DiscountedPriceNOK { get; set; }
        public int TimeRemainingMonths { get; set; }
        public int ExpiresOnDay { get; set; }
        public float RiskScore { get; set; }
        public string CrisisEventId { get; set; } = string.Empty;
        public string OwningCompetitor { get; set; } = string.Empty;
        public string? SourceCompetitorId { get; set; }
        public string? SiteId { get; set; }
        public int? LicenseCount { get; set; }
        public float? MABTonnes { get; set; }
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
        /// <summary>Principal borrowed (0 in saves from before it was recorded).</summary>
        public decimal OriginalPrincipal { get; set; }
        public decimal InterestRate { get; set; }
        public int TermDays { get; set; }
        public int DaysTaken { get; set; }
    }

    /// <summary>Serializable snapshot of a tax payment.</summary>
    /// <summary>Serializable <see cref="Finance.TaxSystem"/> state.</summary>
    public class TaxSystemState
    {
        public decimal LossCarryForward { get; set; }
        public int Period { get; set; }
    }

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

    /// <summary>Serializable snapshot of CovenantSystem (Phase 4.3).</summary>
    public class CovenantSystemState
    {
        public List<CovenantBreachState> Breaches { get; set; } = new();
    }

    /// <summary>Serializable covenant breach record (Phase 4.3).</summary>
    public class CovenantBreachState
    {
        public string CovenantId { get; set; } = string.Empty;
        public string CovenantName { get; set; } = string.Empty;
        public int BreachDay { get; set; } = 0;
        public decimal RatioValue { get; set; } = 0m;
        public decimal ThresholdValue { get; set; } = 0m;
        public decimal BreachValue { get; set; } = 0m;
        public bool IsResolved { get; set; } = false;
        public int? ResolvedDay { get; set; }
    }

    /// <summary>Serializable snapshot of IPOSession (Phase 4.3).</summary>
    public class IPOSessionState
    {
        public string Stage { get; set; } = "Private";
        public decimal OfferPricePerShare { get; set; } = 0m;
        public int SharesOutstanding { get; set; } = 0;
        public decimal SharePriceNOK { get; set; } = 0m;
        public int QuartersSinceIPO { get; set; } = 0;
        public decimal MarketCap { get; set; } = 0m;
        public List<decimal> GuidanceHistory { get; set; } = new();
        public List<decimal> SharePriceHistory { get; set; } = new();
        public List<GuidanceRecordState> GuidanceRecords { get; set; } = new();
    }

    /// <summary>Serializable guidance record (Phase 4.3).</summary>
    public class GuidanceRecordState
    {
        public int Quarter { get; set; } = 0;
        public int Day { get; set; } = 0;
        public decimal GuidedRevenue { get; set; } = 0m;
        public decimal GuidedEBITDA { get; set; } = 0m;
        public decimal ActualRevenue { get; set; } = 0m;
        public decimal ActualEBITDA { get; set; } = 0m;
        public decimal RevenueMissPct { get; set; } = 0m;
        public decimal EBITDAMissPct { get; set; } = 0m;
    }

    /// <summary>Serializable snapshot of CorporateFinanceSystem (Phase 4.3).</summary>
    public class CorporateFinanceSystemState
    {
        public decimal LossCarryForward { get; set; } = 0m;
        public int CurrentPeriod { get; set; } = 0;
        public int CurrentYear { get; set; } = 1;
        public decimal TotalTaxPaid { get; set; } = 0m;
        public decimal ResourceRentTaxPaid { get; set; } = 0m;
        public List<string> Events { get; set; } = new();
        public List<CovenantBreachState> Covenants { get; set; } = new();
        public IPOSessionState IPO { get; set; } = new();
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

    // NOTE: LicenseAuctionState / LicenseBid / LicenseBidRecord live in
    // Finance/LicenseAuctionSystem.cs — that class owns the auction model.

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
