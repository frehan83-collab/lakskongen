using System;

namespace LakskongenSim.Environment
{
    /// <summary>
    /// Active bloom/hypoxia event (Phase 4.3). Serializable, string kind for
    /// version tolerance. One entry per site+kind max.
    /// </summary>
    [Serializable]
    public class BloomEvent
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Kind { get; set; } = "jellyfish"; // jellyfish | algae | hypoxia
        public string SiteId { get; set; } = "";
        public double Severity01 { get; set; } = 0.5;
        public int StartDay { get; set; }
        public int DurationDays { get; set; } = 14;

        public BloomEvent() { }

        public bool IsExpired(int day) => day >= StartDay + DurationDays;
        public int DaysLeft(int day) => Math.Max(0, StartDay + DurationDays - day);
    }
}
