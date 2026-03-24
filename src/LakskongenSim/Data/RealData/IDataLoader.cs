using System;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Lakskongen.Data.RealData
{
    /// <summary>
    /// Base interface for all real-world data loaders.
    /// </summary>
    public interface IDataLoader
    {
        /// <summary>
        /// Unique identifier for this data source.
        /// </summary>
        string SourceId { get; }

        /// <summary>
        /// Human-readable name of the data source.
        /// </summary>
        string SourceName { get; }

        /// <summary>
        /// Whether this loader is currently available (online).
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Initialize the loader with configuration.
        /// </summary>
        Task InitializeAsync(Dictionary<string, string> config);

        /// <summary>
        /// Check if data is available for a specific geographic area.
        /// </summary>
        Task<bool> HasDataForAreaAsync(BoundingBox bbox);

        /// <summary>
        /// Get data availability for a specific time range.
        /// </summary>
        Task<DataAvailability> GetAvailabilityAsync(DateTime start, DateTime end);

        /// <summary>
        /// Health check for the data source.
        /// </summary>
        Task<HealthStatus> HealthCheckAsync();
    }

    /// <summary>
    /// Bounding box for geographic queries.
    /// </summary>
    public readonly struct BoundingBox
    {
        public float MinLat { get; }
        public float MaxLat { get; }
        public float MinLon { get; }
        public float MaxLon { get; }

        public BoundingBox(float minLat, float maxLat, float minLon, float maxLon)
        {
            MinLat = minLat;
            MaxLat = maxLat;
            MinLon = minLon;
            MaxLon = maxLon;
        }

        public bool Contains(float lat, float lon)
        {
            return lat >= MinLat && lat <= MaxLat && lon >= MinLon && lon <= MaxLon;
        }

        public override string ToString()
        {
            return $"[{MinLat:F4},{MaxLat:F4}]x[{MinLon:F4},{MaxLon:F4}]";
        }
    }

    /// <summary>
    /// Data availability information.
    /// </summary>
    public class DataAvailability
    {
        public DateTime EarliestData { get; set; }
        public DateTime LatestData { get; set; }
        public TimeSpan TemporalResolution { get; set; }
        public float SpatialResolutionKm { get; set; }
        public int TotalDataPoints { get; set; }
        public Dictionary<string, int> VariablesAvailable { get; set; } = new();
    }

    /// <summary>
    /// Health status of a data source.
    /// </summary>
    public class HealthStatus
    {
        public bool IsHealthy { get; set; }
        public string StatusMessage { get; set; } = "";
        public TimeSpan ResponseTime { get; set; }
        public DateTime LastSuccessfulQuery { get; set; }
        public int FailedRequestsLastHour { get; set; }
    }

    /// <summary>
    /// Exception thrown when data cannot be loaded.
    /// </summary>
    public class DataLoadException : Exception
    {
        public DataLoadException(string message) : base(message) { }
        public DataLoadException(string message, Exception inner) : base(message, inner) { }
    }
}
