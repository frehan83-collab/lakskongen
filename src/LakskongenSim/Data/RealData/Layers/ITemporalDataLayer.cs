using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Lakskongen.Data.RealData.Layers
{
    /// <summary>
    /// Interface for time-varying spatial data layers.
    /// </summary>
    public interface ITemporalDataLayer
    {
        /// <summary>
        /// Name of the layer.
        /// </summary>
        string LayerName { get; }

        /// <summary>
        /// Variable name (e.g., "temperature", "salinity").
        /// </summary>
        string VariableName { get; }

        /// <summary>
        /// Unit of measurement.
        /// </summary>
        string Unit { get; }

        /// <summary>
        /// Get value at a specific location and time.
        /// </summary>
        Task<DataPoint> GetValueAsync(float latitude, float longitude, DateTime timestamp, float? depth = null);

        /// <summary>
        /// Get value synchronously (uses cached data if available).
        /// </summary>
        float GetValue(float latitude, float longitude, DateTime timestamp, float? depth = null);

        /// <summary>
        /// Get a time series for a specific location.
        /// </summary>
        Task<List<DataPoint>> GetTimeSeriesAsync(float latitude, float longitude, DateTime start, DateTime end, float? depth = null);

        /// <summary>
        /// Get a spatial grid for a specific time.
        /// </summary>
        Task<DataGrid> GetGridAsync(BoundingBox bbox, DateTime timestamp, float resolutionKm = 10.0f);

        /// <summary>
        /// Get data availability for a region.
        /// </summary>
        Task<DataAvailability> GetAvailabilityAsync(BoundingBox bbox);

        /// <summary>
        /// Get historical statistics for a location.
        /// </summary>
        Task<LayerStatistics> GetStatisticsAsync(float latitude, float longitude, DateTime start, DateTime end);
    }

    /// <summary>
    /// Statistical summary of a data layer.
    /// </summary>
    public class LayerStatistics
    {
        public float Min { get; set; }
        public float Max { get; set; }
        public float Mean { get; set; }
        public float Median { get; set; }
        public float StandardDeviation { get; set; }
        public float P05 { get; set; }
        public float P95 { get; set; }
        public int SampleCount { get; set; }
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }

        /// <summary>
        /// Seasonal statistics if available.
        /// </summary>
        public Dictionary<string, SeasonalStats> BySeason { get; set; } = new();
    }

    public class SeasonalStats
    {
        public string Season { get; set; } = "";
        public float Mean { get; set; }
        public float Min { get; set; }
        public float Max { get; set; }
        public int SampleCount { get; set; }
    }
}
