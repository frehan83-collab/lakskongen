using System;
using LakskongenSim.Core;

namespace LakskongenSim.Events
{
    public class EventDefinition
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public EventCategory Category { get; set; }
        public double Weight { get; set; } = 1.0; // selection weight
        public int CooldownDays { get; set; } = 30; // default cooldown
        public int MinDay { get; set; } = 0;
        public int MaxDay { get; set; } = int.MaxValue;

        // Condition to check whether the event can trigger given current state
        public Func<GameState, bool>? Condition { get; set; }

        // Effect to apply when the event fires. Should modify GameState and/or other systems.
        public Action<GameState, EventRecord>? Apply { get; set; }
    }
}