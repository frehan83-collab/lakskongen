using System;

namespace LakskongenSim.Logistics
{
    /// <summary>
    /// Spot-hire wellboat job (Phase 2.4). Requested via GameBridge, stored in
    /// GameState (serializable), executed on due day. Own-vs-hire and fleet
    /// simulation land in Phase 3.
    /// </summary>
    [Serializable]
    public class WellboatJob
    {
        private string? _id;

        /// <summary>
        /// Deterministic id (roadmap D8, P0-07): booking day, site, kind and the
        /// game's booking sequence number, so the same seeded game books the
        /// same ids in every run and process (it was <c>Guid.NewGuid()</c>).
        /// A saved id (also a legacy GUID one) is kept as loaded.
        /// </summary>
        public string Id
        {
            get => _id ?? ComposeId(BookedDay, SiteId, Kind, Sequence);
            set => _id = value;
        }

        public string SiteId { get; set; } = "";
        /// <summary>"harvest" or "delouse".</summary>
        public string Kind { get; set; } = "harvest";
        /// <summary>Delousing method (for delouse jobs).</summary>
        public string Method { get; set; } = "";
        /// <summary>Game day the job was booked.</summary>
        public int BookedDay { get; set; }
        /// <summary>The game's running booking number (GameState.WellboatJobsBooked when booked).</summary>
        public int Sequence { get; set; }
        public int DueDay { get; set; }
        public decimal HireCost { get; set; }

        public WellboatJob() { }

        /// <summary>The id of the <paramref name="sequence"/>-th booking: day, site, kind, sequence.</summary>
        public static string ComposeId(int bookedDay, string siteId, string kind, int sequence)
            => $"wellboat-d{bookedDay}-{siteId}-{kind}-{sequence}";
    }
}
