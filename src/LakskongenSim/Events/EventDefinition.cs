using System;
using LakskongenSim.Core;

namespace LakskongenSim.Events
{
    /// <summary>
    /// Defines a game event with conditions and effects
    /// </summary>
    public class EventDefinition
    {
        /// <summary>Unique identifier for this event</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();
        /// <summary>Display name of the event</summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>Category of the event</summary>
        public EventCategory Category { get; set; }
        /// <summary>Selection weight for random event selection</summary>
        public double Weight { get; set; } = 1.0;
        /// <summary>Cooldown period in days before this event can trigger again</summary>
        public int CooldownDays { get; set; } = 30;
        /// <summary>Minimum game day when this event can trigger</summary>
        public int MinDay { get; set; } = 0;
        /// <summary>Maximum game day when this event can trigger</summary>
        public int MaxDay { get; set; } = int.MaxValue;

        /// <summary>Condition function to check whether the event can trigger given current state</summary>
        public Func<GameState, bool>? Condition { get; set; }

        /// <summary>Action to apply when the event fires (modifies GameState and/or other systems)</summary>
        public Action<GameState, EventRecord>? Apply { get; set; }
    }
}