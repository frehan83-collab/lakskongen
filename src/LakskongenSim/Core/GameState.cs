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

        /// <summary>Monetary balance in NOK.</summary>
        public decimal Balance { get; set; }

        /// <summary>List of zones in the environment.</summary>
        public List<Environment.Zone> Zones { get; set; } = new();

        /// <summary>List of active farms/sea sites.</summary>
        public List<Farm.SeaSite> SeaSites { get; set; } = new();

        /// <summary>Fish populations across the system.</summary>
        public List<Biology.FishPopulation> Populations { get; set; } = new();

        /// <summary>Broodstock genomes available for selective breeding.</summary>
        public List<Genome> Broodstock { get; set; } = new();

        /// <summary>Processing P&amp;L snapshot (summary per plant). New systems can push ProcessingSnapshot objects here for dashboards.
        /// Kept as a simple serializable structure so front-end/Dashboard can display aggregate processing profit/loss.
        /// </summary>
        public List<Processing.ProcessingSnapshot> ProcessingSnapshots { get; set; } = new();

        /// <summary>Event history log for the storyteller/event system. Tracks all random events that have occurred.</summary>
        public List<Events.EventRecord> EventLog { get; set; } = new();

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

        /// <summary>Default constructor for serializer.</summary>
        public GameState()
        {
        }
    }
}
