using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lakskongen.Data.RealData.Cache;

namespace Lakskongen.Data.RealData.Layers
{
    /// <summary>
    /// Bathymetry (seabed depth) data layer.
    /// </summary>
    public class BathymetryLayer : ITemporalDataLayer
    {
        public string LayerName => "Bathymetry";
        public string VariableName => "depth";
        public string Unit => "m";

        private readonly DataCache _cache;
        private readonly List<IDataLoader> _dataLoaders;

        // Simplified depth model for Norwegian coast
        // Real implementation would use Mareano DTM data

        public BathymetryLayer(DataCache cache, List<IDataLoader> dataLoaders)
        {
            _cache = cache;
            _dataLoaders = dataLoaders;
        }

        public Task<DataPoint> GetValueAsync(float latitude, float longitude, DateTime timestamp, float? depth = null)
        {
            var value = GetDepthAt(latitude, longitude);
            return Task.FromResult(new DataPoint
            {
                Location = new GeoCoordinate(latitude, longitude),
                Timestamp = timestamp,
                Value = value,
                Unit = Unit,
                QualityFlag = 1,
                SourceId = "model_fallback",
                VariableName = VariableName,
                Metadata = new Dictionary<string, string> 
                { 
                    { "note", "Using simplified model - replace with Mareano DTM" } 
                }
            });
        }

        public float GetValue(float latitude, float longitude, DateTime timestamp, float? depth = null)
        {
            return GetDepthAt(latitude, longitude);
        }

        /// <summary>
        /// Get depth at location (positive = below sea level).
        /// </summary>
        public float GetDepthAt(float latitude, float longitude)
        {
            // This is a simplified model - real implementation would query Mareano DTM
            // Using approximate Norwegian continental shelf characteristics
            
            // Distance from coast approximation
            // Norway's coast roughly follows longitude 4-6 in south, 5-8 in north
            float coastalLon = 5.0f + (latitude - 58) * 0.1f;
            float offshoreDist = Math.Max(0, longitude - coastalLon);
            
            // Continental shelf profile
            float depth;
            if (offshoreDist < 0.2f)
            {
                // Coastal waters
                depth = 50 + offshoreDist * 500;
            }
            else if (offshoreDist < 2.0f)
            {
                // Continental shelf
                depth = 150 + (offshoreDist - 0.2f) * 100;
            }
            else
            {
                // Deep ocean
                depth = 330 + (offshoreDist - 2.0f) * 1000;
                depth = Math.Min(depth, 3000); // Max depth cap
            }
            
            // Add some regional variation based on known features
            // Norwegian Trench
            if (latitude > 57 && latitude < 62 && longitude > 5 && longitude < 9)
            {
                depth *= 1.5f; // Deeper in the trench
            }
            
            // Barents Sea shelf (shallow)
            if (latitude > 70)
            {
                depth = Math.Min(depth, 400);
            }
            
            return -depth; // Negative = below sea level
        }

        public Task<List<DataPoint>> GetTimeSeriesAsync(float latitude, float longitude, DateTime start, DateTime end, float? depth = null)
        {
            // Bathymetry is static - return same value for all dates
            var value = GetDepthAt(latitude, longitude);
            var points = new List<DataPoint>();
            var current = start;
            
            while (current <= end)
            {
                points.Add(new DataPoint
                {
                    Location = new GeoCoordinate(latitude, longitude),
                    Timestamp = current,
                    Value = value,
                    Unit = Unit,
                    VariableName = VariableName
                });
                current = current.AddMonths(1);
            }

            return Task.FromResult(points);
        }

        public Task<DataGrid> GetGridAsync(BoundingBox bbox, DateTime timestamp, float resolutionKm = 10.0f)
        {
            // Calculate grid dimensions
            float latSpan = bbox.MaxLat - bbox.MinLat;
            float lonSpan = bbox.MaxLon - bbox.MinLon;
            
            float kmPerLat = 111.0f;
            float kmPerLon = 111.0f * (float)Math.Cos((bbox.MinLat + bbox.MaxLat) / 2.0 * Math.PI / 180.0);
            
            int latSteps = Math.Max(2, (int)(latSpan * kmPerLat / resolutionKm));
            int lonSteps = Math.Max(2, (int)(lonSpan * kmPerLon / resolutionKm));
            
            var grid = new DataGrid
            {
                Latitudes = Enumerable.Range(0, latSteps).Select(i => bbox.MinLat + i * latSpan / (latSteps - 1)).ToArray(),
                Longitudes = Enumerable.Range(0, lonSteps).Select(i => bbox.MinLon + i * lonSpan / (lonSteps - 1)).ToArray(),
                Timestamp = timestamp,
                VariableName = VariableName,
                Unit = Unit,
                SourceId = "bathymetry_layer",
                Data = new float[1, latSteps, lonSteps]
            };

            for (int i = 0; i < latSteps; i++)
            {
                for (int j = 0; j < lonSteps; j++)
                {
                    grid.Data[0, i, j] = GetDepthAt(grid.Latitudes[i], grid.Longitudes[j]);
                }
            }

            return Task.FromResult(grid);
        }

        public Task<DataAvailability> GetAvailabilityAsync(BoundingBox bbox)
        {
            return Task.FromResult(new DataAvailability
            {
                EarliestData = new DateTime(1900, 1, 1), // Bathymetry doesn't change
                LatestData = new DateTime(2025, 1, 1),
                TemporalResolution = TimeSpan.FromDays(365 * 100), // Essentially static
                SpatialResolutionKm = 50f, // Mareano has 5m, 25m, 50m
                TotalDataPoints = 10000000,
                VariablesAvailable = new Dictionary<string, int> { { "depth", 1 }, { "slope", 1 } }
            });
        }

        public Task<LayerStatistics> GetStatisticsAsync(float latitude, float longitude, DateTime start, DateTime end)
        {
            var depth = GetDepthAt(latitude, longitude);
            
            return Task.FromResult(new LayerStatistics
            {
                Min = depth,
                Max = depth,
                Mean = depth,
                Median = depth,
                StandardDeviation = 0,
                SampleCount = 1,
                PeriodStart = start,
                PeriodEnd = end
            });
        }

        /// <summary>
        /// Check if depth is suitable for aquaculture (MOM-B standard).
        /// </summary>
        public bool IsSuitableForAquaculture(float latitude, float longitude, float minDepth = 50f)
        {
            float depth = Math.Abs(GetDepthAt(latitude, longitude));
            return depth >= minDepth;
        }

        /// <summary>
        /// Calculate depth at cage position with cage depth considered.
        /// </summary>
        public float GetDepthAtCage(float latitude, float longitude, float cageDepth = 50f)
        {
            float seabed = Math.Abs(GetDepthAt(latitude, longitude));
            return Math.Max(0, seabed - cageDepth);
        }
    }
}
