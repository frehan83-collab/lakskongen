using System;

namespace LakskongenSim.Events
{
    /// <summary>
    /// Categories of events that can occur in the game
    /// </summary>
    public enum EventCategory
    {
        /// <summary>Weather-related events</summary>
        Weather,
        /// <summary>Disease-related events</summary>
        Disease,
        /// <summary>Market-related events</summary>
        Market,
        /// <summary>Regulatory events</summary>
        Regulation,
        /// <summary>Equipment-related events</summary>
        Equipment,
        /// <summary>Community-related events</summary>
        Community,
        /// <summary>Investor-related events</summary>
        Investor,
        /// <summary>Competitor-related events</summary>
        Competitor,
        /// <summary>Plankton bloom events</summary>
        PlanktonBloom,
        /// <summary>Plankton crash (DO crash) events</summary>
        PlanktonCrash,
        /// <summary>Other events</summary>
        Other
    }

    /// <summary>
    /// Record of an event that occurred during gameplay
    /// </summary>
    public class EventRecord
    {
        /// <summary>Game day when the event occurred</summary>
        public int Day { get; set; }
        /// <summary>Unique identifier for the event</summary>
        public string Id { get; set; } = string.Empty;
        /// <summary>Name of the event</summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>Category of the event</summary>
        public EventCategory Category { get; set; }
        /// <summary>Description of the event</summary>
        public string Description { get; set; } = string.Empty;
        /// <summary>Financial impact of the event in NOK</summary>
        public decimal BalanceChange { get; set; }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"Day {Day}: {Name} ({Category}) - {Description} Impact: {BalanceChange:N0} NOK";
        }
    }
}