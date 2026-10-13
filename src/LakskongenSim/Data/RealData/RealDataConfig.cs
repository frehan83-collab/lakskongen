using System;

namespace Lakskongen.Data.RealData
{
    /// <summary>
    /// Configuration for the real data orchestrator.
    /// </summary>
    public class RealDataConfig
    {
        /// <summary>
        /// Directory for caching downloaded data.
        /// </summary>
        public string CacheDirectory { get; set; } = "data/cache/realdata";

        /// <summary>
        /// Default cache expiration in days.
        /// </summary>
        public int CacheExpirationDays { get; set; } = 30;

        /// <summary>
        /// Whether to use live data sources when available.
        /// </summary>
        public bool UseLiveSources { get; set; } = false;

        /// <summary>
        /// Timeout for external API calls in seconds.
        /// </summary>
        public int ApiTimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Whether to enable offline mode (only use cached/snapshot data).
        /// </summary>
        public bool OfflineMode { get; set; } = false;

        /// <summary>
        /// As-of date (UTC) of the embedded offline snapshots. Availability and provenance
        /// metadata report this fixed date instead of the wall clock, so layer output is
        /// reproducible (DeterminismBanTests forbids DateTime.UtcNow in src/).
        /// </summary>
        public DateTime SnapshotDate { get; set; } = DefaultSnapshotDate;

        /// <summary>
        /// Snapshot as-of date used when no config is supplied: the date the built-in
        /// climatology tables were authored.
        /// </summary>
        public static readonly DateTime DefaultSnapshotDate = new(2026, 10, 13, 0, 0, 0, DateTimeKind.Utc);
    }
}
