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
        private string? _id;

        /// <summary>
        /// Deterministic id: start day, site and kind, so the same seeded game
        /// writes the same ids in every run and process (it was
        /// <c>Guid.NewGuid()</c>, SHIP_PLAN §10, RUN_LOG §8 cycle-1 follow-up a).
        /// Unique without a sequence: a site holds at most one bloom of a kind
        /// at a time (<c>SimulationEngine.TrySpawnBloom</c>), and the next one
        /// starts on a later day. A set id (scenario, QA or saved, a legacy
        /// GUID one included) is kept as given.
        /// </summary>
        public string Id
        {
            get => _id ?? ComposeId(StartDay, SiteId, Kind);
            set => _id = value;
        }

        public string Kind { get; set; } = "jellyfish"; // jellyfish | algae | hypoxia
        public string SiteId { get; set; } = "";
        public double Severity01 { get; set; } = 0.5;
        public int StartDay { get; set; }
        public int DurationDays { get; set; } = 14;

        public BloomEvent() { }

        public bool IsExpired(int day) => day >= StartDay + DurationDays;
        public int DaysLeft(int day) => Math.Max(0, StartDay + DurationDays - day);

        /// <summary>The id of a <paramref name="kind"/> bloom at <paramref name="siteId"/> starting on <paramref name="startDay"/>.</summary>
        public static string ComposeId(int startDay, string siteId, string kind)
            => $"bloom-d{startDay}-{siteId}-{kind}";
    }
}
