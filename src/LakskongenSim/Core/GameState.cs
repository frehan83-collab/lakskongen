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

        /// <summary>Processing P&L snapshot (summary per plant). New systems can push ProcessingSnapshot objects here for dashboards.
        /// Kept as a simple serializable structure so front-end/Dashboard can display aggregate processing profit/loss.
        /// </summary>
        public List<Processing.ProcessingSnapshot> ProcessingSnapshots { get; set; } = new();

        /// <summary>Event history log for the storyteller/event system. Tracks all random events that have occurred.</summary>
        public List<Events.EventRecord> EventLog { get; set; } = new();

        /// <summary>Default constructor for serializer.</summary>
        public GameState()
        {
        }
    }
}
