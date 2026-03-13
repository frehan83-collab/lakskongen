using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

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

        /// <summary>Default constructor for serializer.</summary>
        public GameState()
        {
        }
    }
}
