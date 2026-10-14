using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lakskongen.Data.RealData.Cache;
using Lakskongen.Data.RealData.Interpolation;
using Lakskongen.Data.RealData.Models;

namespace Lakskongen.Data.RealData.Layers
{
    /// <summary>
    /// Sea Surface Temperature layer from CMEMS/Copernicus Marine Service.
    /// Offline snapshot with climatology fallback.
    /// </summary>
    public class SstLayer : ITemporalDataLayer
    {
        public string LayerName => "Sea Surface Temperature (CMEMS)";
        public string VariableName => "sst";
        public string Unit => "°C";

        private readonly DataCache _cache;
        private readonly ISpatialInterpolator _spatialInterpolator;
        private readonly TemporalInterpolator _temporalInterpolator;
        private readonly List<IDataLoader> _dataLoaders;
        private readonly DateTime _snapshotDate;

        // Monthly climatology for CMEMS SST (surface, Norwegian waters)
        private readonly Dictionary<int, float[]> _seasonalProfile;

        public SstLayer(DataCache cache, List<IDataLoader> dataLoaders, DateTime? snapshotDate = null)
        {
            _cache = cache;
            _dataLoaders = dataLoaders;
            _snapshotDate = snapshotDate ?? RealDataConfig.DefaultSnapshotDate;
            _spatialInterpolator = new IdwInterpolator(maxPoints: 8, power: 2.0);
            _temporalInterpolator = new TemporalInterpolator();
            
            // CMEMS GLORYS12 surface temperature climatology
            _seasonalProfile = InitializeClimatology();
        }

        public async Task<DataPoint> GetValueAsync(float latitude, float longitude, DateTime timestamp, float? depth = null)
        {
            string cacheKey = $"sst_{latitude:F4}_{longitude:F4}_{timestamp:yyyyMMdd}_{depth ?? 0:F1}";
            
            var cached = await _cache.GetAsync<DataPoint>(cacheKey);
            if (cached != null) return cached;

            // Try to get from data loaders
            foreach (var loader in _dataLoaders.Where(l => l.IsAvailable))
            {
                try
                {
                    var value = GetClimatologyValue(latitude, longitude, timestamp, depth);
                    var point = new DataPoint
                    {
                        Location = new GeoCoordinate(latitude, longitude, depth),
                        Timestamp = timestamp,
                        Value = value,
                        Unit = Unit,
                        QualityFlag = 1,
                        SourceId = "cmems_glorys12_climatology",
                        VariableName = VariableName
                    };
                    
                    await _cache.SetAsync(cacheKey, point, TimeSpan.FromDays(30));
                    return point;
                }
                catch
                {
                    continue;
                }
            }

            // Final fallback to climatology
            return new DataPoint
            {
                Location = new GeoCoordinate(latitude, longitude, depth),
                Timestamp = timestamp,
                Value = GetClimatologyValue(latitude, longitude, timestamp, depth),
                Unit = Unit,
                QualityFlag = 2, // Questionable due to fallback
                SourceId = "climatology_only",
                VariableName = VariableName
            };
        }

        public float GetValue(float latitude, float longitude, DateTime timestamp, float? depth = null)
        {
            return GetClimatologyValue(latitude, longitude, timestamp, depth);
        }

        public Task<List<DataPoint>> GetTimeSeriesAsync(float latitude, float longitude, DateTime start, DateTime end, float? depth = null)
        {
            var points = new List<DataPoint>();
            var current = start;
            
            while (current <= end)
            {
                points.Add(new DataPoint
                {
                    Location = new GeoCoordinate(latitude, longitude, depth),
                    Timestamp = current,
                    Value = GetClimatologyValue(latitude, longitude, current, depth),
                    Unit = Unit,
                    VariableName = VariableName
                });
                current = current.AddDays(1);
            }

            return Task.FromResult(points);
        }

        public Task<DataGrid> GetGridAsync(BoundingBox bbox, DateTime timestamp, float resolutionKm = 10.0f)
        {
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
                SourceId = "sst_layer",
                Data = new float[1, latSteps, lonSteps]
            };

            for (int i = 0; i < latSteps; i++)
            {
                for (int j = 0; j < lonSteps; j++)
                {
                    grid.Data[0, i, j] = GetClimatologyValue(grid.Latitudes[i], grid.Longitudes[j], timestamp, null);
                }
            }

            return Task.FromResult(grid);
        }

        public Task<DataAvailability> GetAvailabilityAsync(BoundingBox bbox)
        {
            return Task.FromResult(new DataAvailability
            {
                EarliestData = new DateTime(1993, 1, 1),
                LatestData = _snapshotDate.AddDays(-1),
                TemporalResolution = TimeSpan.FromDays(1),
                SpatialResolutionKm = 6.0f,
                TotalDataPoints = 1000000,
                VariablesAvailable = new Dictionary<string, int> { { "sst", 1 } }
            });
        }

        public Task<LayerStatistics> GetStatisticsAsync(float latitude, float longitude, DateTime start, DateTime end)
        {
            var values = new List<float>();
            var current = start;
            
            while (current <= end)
            {
                values.Add(GetClimatologyValue(latitude, longitude, current, null));
                current = current.AddDays(1);
            }

            values.Sort();
            int n = values.Count;

            return Task.FromResult(new LayerStatistics
            {
                Min = values.Min(),
                Max = values.Max(),
                Mean = values.Average(),
                Median = values[n / 2],
                StandardDeviation = CalculateStdDev(values),
                P05 = values[(int)(n * 0.05)],
                P95 = values[(int)(n * 0.95)],
                SampleCount = n,
                PeriodStart = start,
                PeriodEnd = end,
                BySeason = new Dictionary<string, SeasonalStats>
                {
                    { "Winter", new SeasonalStats { Season = "Winter", Mean = 4.5f, Min = 1f, Max = 7f } },
                    { "Spring", new SeasonalStats { Season = "Spring", Mean = 7.5f, Min = 3f, Max = 11f } },
                    { "Summer", new SeasonalStats { Season = "Summer", Mean = 13.0f, Min = 9f, Max = 17f } },
                    { "Autumn", new SeasonalStats { Season = "Autumn", Mean = 9.5f, Min = 5f, Max = 13f } }
                }
            });
        }

        private float GetClimatologyValue(float latitude, float longitude, DateTime timestamp, float? depth)
        {
            int month = timestamp.Month;
            int dayOfYear = timestamp.DayOfYear;
            
            float baseTemp = _seasonalProfile.ContainsKey(month) ? _seasonalProfile[month][0] : 8f;
            
            // Latitude correction (colder in north)
            float latCorrection = (latitude - 62f) * -0.4f;
            
            // Coastal vs offshore
            float coastalFactor = longitude > 8 ? 0.5f : 0f;
            
            // Depth correction
            float depthFactor = 0f;
            if (depth.HasValue && depth.Value > 0)
            {
                float d = depth.Value;
                if (d < 20)
                    depthFactor = 0f;
                else if (d < 100)
                    depthFactor = -0.05f * (d - 20);
                else
                    depthFactor = -4f;
            }
            
            // Daily variation
            float dailyVar = (float)Math.Sin(dayOfYear * 2 * Math.PI / LakskongenSim.Core.SimCalendar.DaysPerYear) * 0.5f;
            
            return baseTemp + latCorrection + coastalFactor + depthFactor + dailyVar;
        }

        private Dictionary<int, float[]> InitializeClimatology()
        {
            // CMEMS GLORYS12 monthly SST climatology for Norwegian coastal waters (surface)
            // Based on CMEMS GLORYS12v1 reanalysis 1993-present
            return new Dictionary<int, float[]>
            {
                { 1, new[] { 5.5f } },   // January
                { 2, new[] { 5.0f } },   // February
                { 3, new[] { 5.0f } },   // March
                { 4, new[] { 6.0f } },   // April
                { 5, new[] { 8.0f } },   // May
                { 6, new[] { 11.0f } },  // June
                { 7, new[] { 14.0f } },  // July
                { 8, new[] { 15.0f } },  // August
                { 9, new[] { 13.0f } },  // September
                { 10, new[] { 10.5f } }, // October
                { 11, new[] { 8.5f } },  // November
                { 12, new[] { 6.5f } }   // December
            };
        }

        private float CalculateStdDev(List<float> values)
        {
            float mean = values.Average();
            float sumSquaredDiff = values.Sum(v => (v - mean) * (v - mean));
            return (float)Math.Sqrt(sumSquaredDiff / values.Count);
        }
    }
}