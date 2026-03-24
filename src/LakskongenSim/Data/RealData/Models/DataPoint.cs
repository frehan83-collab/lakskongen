using System;

namespace Lakskongen.Data.RealData
{
    /// <summary>
    /// Represents a single data measurement point with uncertainty metadata.
    /// </summary>
    public class DataPoint
    {
        /// <summary>
        /// Geographic location of the measurement.
        /// </summary>
        public GeoCoordinate Location { get; set; }

        /// <summary>
        /// Time of measurement.
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// The measured value.
        /// </summary>
        public float Value { get; set; }

        /// <summary>
        /// Unit of measurement (e.g., "°C", "m", "mg/L", "m/s").
        /// </summary>
        public string Unit { get; set; } = "";

        /// <summary>
        /// Standard deviation or measurement uncertainty.
        /// </summary>
        public float? Uncertainty { get; set; }

        /// <summary>
        /// Quality flag: 0=excellent, 1=good, 2=questionable, 3=bad, 4=missing
        /// </summary>
        public int QualityFlag { get; set; }

        /// <summary>
        /// Data source identifier.
        /// </summary>
        public string SourceId { get; set; } = "";

        /// <summary>
        /// Original variable name from source.
        /// </summary>
        public string VariableName { get; set; } = "";

        /// <summary>
        /// Additional metadata as key-value pairs.
        /// </summary>
        public System.Collections.Generic.Dictionary<string, string> Metadata { get; set; } = new();

        public bool IsValid => QualityFlag <= 2 && !float.IsNaN(Value);

        public override string ToString()
        {
            return $"{VariableName}={Value:F3}{Unit} @ {Location}, {Timestamp:yyyy-MM-dd} (Q{QualityFlag})";
        }
    }

    /// <summary>
    /// Container for gridded spatial data.
    /// </summary>
    public class DataGrid
    {
        /// <summary>
        /// Grid dimensions: [depth][lat][lon] or [lat][lon] for 2D.
        /// </summary>
        public float[,,] Data { get; set; } = new float[0,0,0];

        /// <summary>
        /// Depth levels (null for 2D surface data).
        /// </summary>
        public float[] DepthLevels { get; set; } = Array.Empty<float>();

        /// <summary>
        /// Latitude coordinates (cell centers).
        /// </summary>
        public float[] Latitudes { get; set; } = Array.Empty<float>();

        /// <summary>
        /// Longitude coordinates (cell centers).
        /// </summary>
        public float[] Longitudes { get; set; } = Array.Empty<float>();

        /// <summary>
        /// Time of this data snapshot.
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Variable name (e.g., "temperature", "salinity").
        /// </summary>
        public string VariableName { get; set; } = "";

        /// <summary>
        /// Unit of measurement.
        /// </summary>
        public string Unit { get; set; } = "";

        /// <summary>
        /// Source identifier.
        /// </summary>
        public string SourceId { get; set; } = "";

        /// <summary>
        /// Get value at a specific coordinate using nearest neighbor.
        /// </summary>
        public float GetValueAt(float lat, float lon, float? depth = null)
        {
            int latIdx = FindNearestIndex(Latitudes, lat);
            int lonIdx = FindNearestIndex(Longitudes, lon);

            if (depth.HasValue && DepthLevels != null)
            {
                int depthIdx = FindNearestIndex(DepthLevels, depth.Value);
                return Data[depthIdx, latIdx, lonIdx];
            }

            // For 2D data stored as [1, lat, lon]
            if (Data.GetLength(0) == 1)
                return Data[0, latIdx, lonIdx];

            return Data[0, latIdx, lonIdx];
        }

        private int FindNearestIndex(float[] array, float value)
        {
            if (array == null || array.Length == 0) return 0;

            int idx = 0;
            float minDiff = Math.Abs(array[0] - value);

            for (int i = 1; i < array.Length; i++)
            {
                float diff = Math.Abs(array[i] - value);
                if (diff < minDiff)
                {
                    minDiff = diff;
                    idx = i;
                }
            }

            return idx;
        }

        public int LatCount => Latitudes?.Length ?? 0;
        public int LonCount => Longitudes?.Length ?? 0;
        public int DepthCount => DepthLevels?.Length ?? 1;
    }

    /// <summary>
    /// Metadata about a data source.
    /// </summary>
    public class DataSource
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Url { get; set; } = "";
        public string Citation { get; set; } = "";
        public DateTime LastUpdated { get; set; }
        public string License { get; set; } = "";
        public string ContactEmail { get; set; } = "";
    }
}
