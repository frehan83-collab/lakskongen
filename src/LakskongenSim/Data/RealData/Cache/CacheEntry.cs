using System;
using System.Text.Json.Serialization;

namespace Lakskongen.Data.RealData.Cache
{
    /// <summary>
    /// Typed cache entry with metadata.
    /// </summary>
    public class CacheEntry<T>
    {
        /// <summary>
        /// The cached data.
        /// </summary>
        public T Data { get; set; } = default!;

        /// <summary>
        /// When this cache entry was created.
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// When this cache entry expires.
        /// </summary>
        public DateTime ExpiresAt { get; set; }

        /// <summary>
        /// Check if the cache entry has expired.
        /// </summary>
        [JsonIgnore]
        public bool IsExpired => DateTime.UtcNow > ExpiresAt;

        /// <summary>
        /// Cache entry version for compatibility.
        /// </summary>
        public int Version { get; set; } = 1;

        /// <summary>
        /// Original data source identifier.
        /// </summary>
        public string SourceId { get; set; } = "";

        /// <summary>
        /// Query parameters used to generate this data.
        /// </summary>
        public string QueryParameters { get; set; } = "";

        /// <summary>
        /// Number of times this entry has been accessed.
        /// </summary>
        public int AccessCount { get; set; }

        /// <summary>
        /// Last access time.
        /// </summary>
        public DateTime? LastAccessedAt { get; set; }
    }
}
