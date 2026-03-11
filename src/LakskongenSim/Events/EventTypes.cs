using System;

namespace LakskongenSim.Events
{
    public enum EventCategory
    {
        Weather,
        Disease,
        Market,
        Regulation,
        Equipment,
        Community,
        Investor,
        Competitor,
        Other
    }

    public class EventRecord
    {
        public int Day { get; set; }
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public EventCategory Category { get; set; }
        public string Description { get; set; } = string.Empty;
        public decimal BalanceChange { get; set; }

        public override string ToString()
        {
            return $"Day {Day}: {Name} ({Category}) - {Description} Impact: {BalanceChange:N0} NOK";
        }
    }
}