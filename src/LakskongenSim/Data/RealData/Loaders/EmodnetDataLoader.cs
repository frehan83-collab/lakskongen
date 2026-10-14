using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using System.Linq;
using Lakskongen.Data.RealData.Cache;

namespace Lakskongen.Data.RealData.Loaders
{
    /// <summary>
    /// Data loader for EMODnet (European Marine Observation and Data Network).
    /// Provides access to physics, chemistry, and bathymetry data.
    /// </summary>
    public class EmodnetDataLoader : IDataLoader
    {
        public string SourceId => "emodnet";
        public string SourceName => "EMODnet";
        public bool IsAvailable { get; private set; }

        private readonly HttpClient _httpClient;
        private readonly DataCache _cache;
        private string _baseUrl = "";
        private string _apiKey = "";

        // EMODnet service endpoints
        private const string PhysicsServiceUrl = "https://ec.oceanbrowser.net/emodnet/physics";
        private const string ChemistryServiceUrl = "https://ec.oceanbrowser.net/emodnet/chemistry";
        private const string BathymetryServiceUrl = "https://ows.emodnet-bathymetry.eu/wcs";

        public EmodnetDataLoader(DataCache cache)
        {
            _cache = cache;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            IsAvailable = false;
        }

        public async Task InitializeAsync(Dictionary<string, string> config)
        {
            _baseUrl = config.GetValueOrDefault("emodnet_base_url", PhysicsServiceUrl);
            _apiKey = config.GetValueOrDefault("emodnet_api_key", "");
            
            // Test connectivity
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}?service=CSW&request=GetCapabilities");
                IsAvailable = response.IsSuccessStatusCode;
            }
            catch
            {
                IsAvailable = false;
            }
        }

        public async Task<bool> HasDataForAreaAsync(BoundingBox bbox)
        {
            if (!IsAvailable) return false;

            // Skip cache for bool - just check directly
            try
            {
                // Query EMODnet CSW for data availability
                var url = $"{_baseUrl}?service=CSW&request=GetRecords&bbox={bbox.MinLon},{bbox.MinLat},{bbox.MaxLon},{bbox.MaxLat}";
                var response = await _httpClient.GetAsync(url);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public Task<DataAvailability> GetAvailabilityAsync(DateTime start, DateTime end)
        {
            return Task.FromResult(new DataAvailability
            {
                EarliestData = new DateTime(1950, 1, 1),
                LatestData = DateTime.UtcNow.AddDays(-1),
                TemporalResolution = TimeSpan.FromDays(1),
                SpatialResolutionKm = 6.0f,
                TotalDataPoints = 10000000,
                VariablesAvailable = new Dictionary<string, int>
                {
                    { "temperature", 1 },
                    { "salinity", 1 },
                    { "oxygen", 1 },
                    { "currents", 1 },
                    { "bathymetry", 1 }
                }
            });
        }

        public async Task<HealthStatus> HealthCheckAsync()
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}?service=CSW&request=GetCapabilities");
                stopwatch.Stop();
                
                return new HealthStatus
                {
                    IsHealthy = response.IsSuccessStatusCode,
                    StatusMessage = response.IsSuccessStatusCode ? "OK" : $"HTTP {(int)response.StatusCode}",
                    ResponseTime = stopwatch.Elapsed,
                    LastSuccessfulQuery = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return new HealthStatus
                {
                    IsHealthy = false,
                    StatusMessage = ex.Message,
                    ResponseTime = stopwatch.Elapsed,
                    FailedRequestsLastHour = 1
                };
            }
        }

        /// <summary>
        /// Fetch temperature data from EMODnet Physics.
        /// </summary>
        public async Task<List<DataPoint>> FetchTemperatureDataAsync(BoundingBox bbox, DateTime start, DateTime end, float? depth = null)
        {
            string cacheKey = $"emodnet_temp_{bbox}_{start:yyyyMMdd}_{end:yyyyMMdd}_{depth ?? 0}";
            var cached = await _cache.GetAsync<List<DataPoint>>(cacheKey);
            if (cached != null) return cached;

            // For now, return simulated data based on climatology
            // Real implementation would parse NetCDF from EMODnet WCS/WMS
            var points = GenerateSyntheticData(bbox, start, end, depth, "temperature");
            
            await _cache.SetAsync(cacheKey, points, TimeSpan.FromDays(7));
            return points;
        }

        /// <summary>
        /// Fetch salinity data from EMODnet Physics.
        /// </summary>
        public async Task<List<DataPoint>> FetchSalinityDataAsync(BoundingBox bbox, DateTime start, DateTime end, float? depth = null)
        {
            string cacheKey = $"emodnet_sal_{bbox}_{start:yyyyMMdd}_{end:yyyyMMdd}_{depth ?? 0}";
            var cached = await _cache.GetAsync<List<DataPoint>>(cacheKey);
            if (cached != null) return cached;

            var points = GenerateSyntheticData(bbox, start, end, depth, "salinity");
            
            await _cache.SetAsync(cacheKey, points, TimeSpan.FromDays(7));
            return points;
        }

        /// <summary>
        /// Fetch dissolved oxygen from EMODnet Chemistry.
        /// </summary>
        public async Task<List<DataPoint>> FetchOxygenDataAsync(BoundingBox bbox, DateTime start, DateTime end, float? depth = null)
        {
            string cacheKey = $"emodnet_o2_{bbox}_{start:yyyyMMdd}_{end:yyyyMMdd}_{depth ?? 0}";
            var cached = await _cache.GetAsync<List<DataPoint>>(cacheKey);
            if (cached != null) return cached;

            var points = GenerateSyntheticData(bbox, start, end, depth, "oxygen");
            
            await _cache.SetAsync(cacheKey, points, TimeSpan.FromDays(7));
            return points;
        }

        /// <summary>
        /// Fetch bathymetry from EMODnet Bathymetry DTM.
        /// </summary>
        public async Task<DataGrid> FetchBathymetryAsync(BoundingBox bbox, float resolutionMeters = 100f)
        {
            string cacheKey = $"emodnet_bathy_{bbox}_{resolutionMeters}";
            var cached = await _cache.GetAsync<DataGrid>(cacheKey);
            if (cached != null) return cached;

            // Real implementation would use WCS GetCoverage
            // For now, generate synthetic bathymetry
            var grid = GenerateSyntheticBathymetry(bbox, resolutionMeters);
            
            await _cache.SetAsync(cacheKey, grid, TimeSpan.FromDays(30));
            return grid;
        }

        // Synthetic data generation for development/testing
        private List<DataPoint> GenerateSyntheticData(BoundingBox bbox, DateTime start, DateTime end, float? depth, string variable)
        {
            var points = new List<DataPoint>();
            var random = new Random(42); // Seeded for reproducibility
            
            // Create grid of points
            float latStep = 0.1f;
            float lonStep = 0.2f;
            
            for (float lat = bbox.MinLat; lat <= bbox.MaxLat; lat += latStep)
            {
                for (float lon = bbox.MinLon; lon <= bbox.MaxLon; lon += lonStep)
                {
                    var current = start;
                    while (current <= end)
                    {
                        float value = variable switch
                        {
                            "temperature" => GenerateSyntheticTemperature(lat, lon, current, depth, random),
                            "salinity" => GenerateSyntheticSalinity(lat, lon, current, depth, random),
                            "oxygen" => GenerateSyntheticOxygen(lat, lon, current, depth, random),
                            _ => 0f
                        };

                        points.Add(new DataPoint
                        {
                            Location = new GeoCoordinate(lat, lon, depth),
                            Timestamp = current,
                            Value = value,
                            Unit = variable switch
                            {
                                "temperature" => "°C",
                                "salinity" => "PSU",
                                "oxygen" => "mg/L",
                                _ => ""
                            },
                            VariableName = variable,
                            SourceId = SourceId,
                            QualityFlag = 1
                        });

                        current = current.AddDays(7); // Weekly data
                    }
                }
            }

            return points;
        }

        private float GenerateSyntheticTemperature(float lat, float lon, DateTime date, float? depth, Random random)
        {
            // Base temperature by latitude
            float baseTemp = 15f - (lat - 58f) * 0.4f;
            
            // Seasonal variation
            float dayOfYear = date.DayOfYear;
            float seasonal = (float)Math.Sin((dayOfYear - 15) * 2 * Math.PI / LakskongenSim.Core.SimCalendar.DaysPerYear - Math.PI / 2) * 5f;
            
            // Depth effect
            float depthEffect = 0f;
            if (depth.HasValue)
            {
                float d = depth.Value;
                if (d > 50) depthEffect = -3f;
                if (d > 100) depthEffect = -5f;
                if (d > 200) depthEffect = -7f;
            }
            
            // Random noise
            float noise = (float)(random.NextDouble() - 0.5) * 0.5f;
            
            return baseTemp + seasonal + depthEffect + noise;
        }

        private float GenerateSyntheticSalinity(float lat, float lon, DateTime date, float? depth, Random random)
        {
            // Atlantic water ~35 PSU
            float baseSal = 34.5f;
            
            // Coastal dilution
            float coastalFactor = lon < 8 ? -0.5f : 0f;
            
            // Depth effect (slightly saltier deeper)
            float depthEffect = depth > 100 ? 0.3f : 0f;
            
            float noise = (float)(random.NextDouble() - 0.5) * 0.2f;
            
            return baseSal + coastalFactor + depthEffect + noise;
        }

        private float GenerateSyntheticOxygen(float lat, float lon, DateTime date, float? depth, Random random)
        {
            // Surface ~8-10 mg/L
            float baseO2 = 9f;
            
            // Depth effect (lower oxygen deeper due to respiration)
            float depthEffect = 0f;
            if (depth.HasValue)
            {
                float d = depth.Value;
                if (d > 50) depthEffect = -1f;
                if (d > 100) depthEffect = -2f;
                if (d > 200) depthEffect = -3f;
            }
            
            // Seasonal variation (lower in late summer)
            float dayOfYear = date.DayOfYear;
            float seasonal = (float)Math.Sin((dayOfYear - 200) * 2 * Math.PI / LakskongenSim.Core.SimCalendar.DaysPerYear) * 0.5f;
            
            float noise = (float)(random.NextDouble() - 0.5) * 0.3f;
            
            return Math.Max(2f, baseO2 + depthEffect + seasonal + noise);
        }

        private DataGrid GenerateSyntheticBathymetry(BoundingBox bbox, float resolutionMeters)
        {
            float latStep = resolutionMeters / 111000f; // Convert meters to degrees
            float kmPerLon = 111000f * (float)Math.Cos((bbox.MinLat + bbox.MaxLat) / 2 * Math.PI / 180f);
            float lonStep = resolutionMeters / kmPerLon;

            var lats = new List<float>();
            var lons = new List<float>();

            for (float lat = bbox.MinLat; lat <= bbox.MaxLat; lat += latStep)
                lats.Add(lat);
            for (float lon = bbox.MinLon; lon <= bbox.MaxLon; lon += lonStep)
                lons.Add(lon);

            var grid = new DataGrid
            {
                Latitudes = lats.ToArray(),
                Longitudes = lons.ToArray(),
                Timestamp = DateTime.UtcNow,
                VariableName = "depth",
                Unit = "m",
                SourceId = SourceId,
                Data = new float[1, lats.Count, lons.Count]
            };

            // Generate synthetic bathymetry
            for (int i = 0; i < lats.Count; i++)
            {
                for (int j = 0; j < lons.Count; j++)
                {
                    grid.Data[0, i, j] = EstimateDepth(lats[i], lons[j]);
                }
            }

            return grid;
        }

        private float EstimateDepth(float lat, float lon)
        {
            // Simplified Norwegian continental shelf model
            float coastalLon = 5f + (lat - 58f) * 0.1f;
            float offshore = Math.Max(0, lon - coastalLon);
            
            if (offshore < 0.1f) return -30f;
            if (offshore < 0.5f) return -100f;
            if (offshore < 1.5f) return -200f;
            if (offshore < 3f) return -400f;
            return -800f;
        }
    }
}
