using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Lakskongen.Data.RealData.Cache;
using Lakskongen.Data.RealData.Layers;
using Lakskongen.Data.RealData.Loaders;

namespace Lakskongen.Data.RealData
{
    /// <summary>
    /// Main orchestrator for real-world data integration.
    /// Singleton that manages all data loaders and provides unified access.
    /// </summary>
    public class RealDataOrchestrator
    {
        // Singleton instance
        private static RealDataOrchestrator? _instance;
        public static RealDataOrchestrator Instance
        {
            get
            {
                if (_instance == null)
                {
                    throw new InvalidOperationException("RealDataOrchestrator not initialized. Call Initialize() first.");
                }
                return _instance;
            }
        }

        // Configuration
        private RealDataConfig _config = null!;
        private DataCache _cache = null!;
        
        // Data loaders (in priority order)
        private List<IDataLoader> _dataLoaders = null!;
        
        // Data layers
        public TemperatureLayer TemperatureLayer { get; private set; } = null!;
        public BathymetryLayer BathymetryLayer { get; private set; } = null!;
        public LakskongenSim.Data.RealData.Layers.SalinityLayer SalinityLayer { get; private set; } = null!;
        public LakskongenSim.Data.RealData.Layers.OxygenLayer OxygenLayer { get; private set; } = null!;
        public LakskongenSim.Data.RealData.Layers.CurrentLayer CurrentLayer { get; private set; } = null!;

        // Status
        public bool IsInitialized { get; private set; }
        public Dictionary<string, DataSourceStatus> SourceStatuses { get; private set; } = new();

        /// <summary>
        /// Initialize the orchestrator with configuration.
        /// </summary>
        public static async Task<RealDataOrchestrator> InitializeAsync(string configPath = "")
        {
            if (_instance != null) return _instance;
            
            _instance = new RealDataOrchestrator();
            await _instance.InitializeInternalAsync(configPath);
            return _instance;
        }

        private async Task InitializeInternalAsync(string configPath)
        {
            if (IsInitialized) return;

            try
            {
                // Load configuration
                _config = await LoadConfigAsync(configPath);
                
                // Initialize cache
                string cacheDir = _config.CacheDirectory ?? Path.Combine(Path.GetTempPath(), "Lakskongen", "RealDataCache");
                Directory.CreateDirectory(cacheDir);
                _cache = new DataCache(cacheDir, TimeSpan.FromDays(_config.CacheExpirationDays));
                
                // Initialize data loaders
                _dataLoaders = new List<IDataLoader>();
                
                // EMODnet (primary source for most data)
                var emodnet = new EmodnetDataLoader(_cache);
                await emodnet.InitializeAsync(_config.ApiEndpoints);
                _dataLoaders.Add(emodnet);
                
                // TODO: Add Mareano loader for high-res bathymetry
                // TODO: Add NMDC loader for historical CTD data
                // TODO: Add Copernicus loader for satellite/reanalysis data
                
                // Initialize data layers
                TemperatureLayer = new TemperatureLayer(_cache, _dataLoaders);
                BathymetryLayer = new BathymetryLayer(_cache, _dataLoaders);
                SalinityLayer = new LakskongenSim.Data.RealData.Layers.SalinityLayer();
                OxygenLayer = new LakskongenSim.Data.RealData.Layers.OxygenLayer();
                CurrentLayer = new LakskongenSim.Data.RealData.Layers.CurrentLayer();
                
                // Run health checks
                await UpdateSourceStatusesAsync();
                
                IsInitialized = true;
                Console.WriteLine($"RealDataOrchestrator initialized. Cache: {cacheDir}");
                Console.WriteLine($"Data sources: {string.Join(", ", _dataLoaders.Select(l => $"{l.SourceId}({(l.IsAvailable ? "up" : "down")})"))}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize RealDataOrchestrator: {ex.Message}");
                // Initialize with fallback mode
                InitializeFallback();
            }
        }

        /// <summary>
        /// Initialize in fallback mode with no external data sources.
        /// </summary>
        private void InitializeFallback()
        {
            _config = new RealDataConfig
            {
                CacheDirectory = Path.Combine(Path.GetTempPath(), "Lakskongen", "RealDataCache"),
                CacheExpirationDays = 7,
                UseRealData = false,
                FallbackToSynthetic = true
            };
            
            _cache = new DataCache(_config.CacheDirectory);
            _dataLoaders = new List<IDataLoader>();
            
            TemperatureLayer = new TemperatureLayer(_cache, _dataLoaders);
            BathymetryLayer = new BathymetryLayer(_cache, _dataLoaders);
            SalinityLayer = new LakskongenSim.Data.RealData.Layers.SalinityLayer();
            OxygenLayer = new LakskongenSim.Data.RealData.Layers.OxygenLayer();
            
            IsInitialized = true;
            Console.WriteLine("RealDataOrchestrator initialized in FALLBACK mode (no external sources)");
        }

        /// <summary>
        /// Get sea temperature at a location.
        /// </summary>
        public async Task<float> GetTemperatureAsync(float latitude, float longitude, DateTime? date = null, float? depth = null)
        {
            EnsureInitialized();
            
            var timestamp = date ?? DateTime.UtcNow;
            var point = await TemperatureLayer.GetValueAsync(latitude, longitude, timestamp, depth);
            return point?.Value ?? GetFallbackTemperature(latitude, timestamp, depth);
        }

        /// <summary>
        /// Get sea temperature (synchronous, uses cached data).
        /// </summary>
        public float GetTemperature(float latitude, float longitude, DateTime? date = null, float? depth = null)
        {
            EnsureInitialized();
            
            var timestamp = date ?? DateTime.UtcNow;
            return TemperatureLayer.GetValue(latitude, longitude, timestamp, depth);
        }

        /// <summary>
        /// Get bathymetry (seabed depth) at a location.
        /// </summary>
        public float GetBathymetry(float latitude, float longitude)
        {
            EnsureInitialized();
            return BathymetryLayer.GetDepthAt(latitude, longitude);
        }

        /// <summary>
        /// Check if a location is suitable for aquaculture.
        /// </summary>
        public bool IsSuitableForAquaculture(float latitude, float longitude, float minDepth = 50f)
        {
            EnsureInitialized();
            return BathymetryLayer.IsSuitableForAquaculture(latitude, longitude, minDepth);
        }

        /// <summary>
        /// Get data availability for a region.
        /// </summary>
        public async Task<DataAvailability> GetAvailabilityAsync(BoundingBox bbox)
        {
            EnsureInitialized();
            return await TemperatureLayer.GetAvailabilityAsync(bbox);
        }

        /// <summary>
        /// Get a temperature grid for visualization.
        /// </summary>
        public async Task<DataGrid> GetTemperatureGridAsync(BoundingBox bbox, DateTime? date = null, float resolutionKm = 10f)
        {
            EnsureInitialized();
            return await TemperatureLayer.GetGridAsync(bbox, date ?? DateTime.UtcNow, resolutionKm);
        }

        /// <summary>
        /// Get a bathymetry grid for visualization.
        /// </summary>
        public async Task<DataGrid> GetBathymetryGridAsync(BoundingBox bbox, float resolutionKm = 10f)
        {
            EnsureInitialized();
            return await BathymetryLayer.GetGridAsync(bbox, DateTime.UtcNow, resolutionKm);
        }

        /// <summary>
        /// Run health checks on all data sources.
        /// </summary>
        public async Task UpdateSourceStatusesAsync()
        {
            SourceStatuses.Clear();
            
            foreach (var loader in _dataLoaders)
            {
                try
                {
                    var health = await loader.HealthCheckAsync();
                    SourceStatuses[loader.SourceId] = new DataSourceStatus
                    {
                        SourceId = loader.SourceId,
                        SourceName = loader.SourceName,
                        IsAvailable = loader.IsAvailable,
                        IsHealthy = health.IsHealthy,
                        LastCheck = DateTime.UtcNow,
                        ResponseTime = health.ResponseTime,
                        StatusMessage = health.StatusMessage
                    };
                }
                catch (Exception ex)
                {
                    SourceStatuses[loader.SourceId] = new DataSourceStatus
                    {
                        SourceId = loader.SourceId,
                        SourceName = loader.SourceName,
                        IsAvailable = false,
                        IsHealthy = false,
                        LastCheck = DateTime.UtcNow,
                        StatusMessage = ex.Message
                    };
                }
            }
        }

        /// <summary>
        /// Clear all cached data.
        /// </summary>
        public void ClearCache()
        {
            _cache?.Clear();
            Console.WriteLine("RealDataOrchestrator: Cache cleared");
        }

        /// <summary>
        /// Preload data for a region (background warming).
        /// </summary>
        public async Task PreloadRegionAsync(BoundingBox bbox, DateTime start, DateTime end)
        {
            Console.WriteLine($"Preloading data for {bbox}...");
            
            // Preload temperature
            _ = TemperatureLayer.GetGridAsync(bbox, start, 20f);
            _ = BathymetryLayer.GetGridAsync(bbox, start, 10f);
            
            await Task.CompletedTask;
        }

        private async Task<RealDataConfig> LoadConfigAsync(string configPath)
        {
            var defaultConfig = new RealDataConfig
            {
                CacheDirectory = Path.Combine(Path.GetTempPath(), "Lakskongen", "RealDataCache"),
                CacheExpirationDays = 7,
                UseRealData = true,
                FallbackToSynthetic = true,
                ApiEndpoints = new Dictionary<string, string>
                {
                    { "emodnet_base_url", "https://ec.oceanbrowser.net/emodnet/physics" },
                    { "geonorge_api_url", "https://kartkatalog.geonorge.no/api" },
                    { "mareano_wms_url", "https://maps.imr.no/geoserver/mareano/wms" }
                }
            };

            if (string.IsNullOrEmpty(configPath))
            {
                // Try default location
                configPath = Path.Combine(AppContext.BaseDirectory, "Data", "Config", "RealDataConfig.json");
            }

            if (File.Exists(configPath))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(configPath);
                    var config = JsonSerializer.Deserialize<RealDataConfig>(json);
                    if (config != null) return config;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to load config from {configPath}: {ex.Message}");
                }
            }

            return defaultConfig;
        }

        /// <summary>
        /// Get salinity at a zone.
        /// </summary>
        public double GetSalinity(string zoneId, DateTime? date = null, double depthM = 10.0)
        {
            EnsureInitialized();
            return SalinityLayer.GetSalinity(zoneId, date ?? DateTime.UtcNow, depthM);
        }

        /// <summary>
        /// Get dissolved oxygen at a zone.
        /// </summary>
        public double GetOxygen(string zoneId, DateTime? date = null, double depthM = 10.0)
        {
            EnsureInitialized();
            return OxygenLayer.GetOxygen(zoneId, date ?? DateTime.UtcNow, depthM);
        }

        private void EnsureInitialized()
        {
            if (!IsInitialized)
            {
                throw new InvalidOperationException("RealDataOrchestrator not initialized. Call InitializeAsync() first.");
            }
        }

        private float GetFallbackTemperature(float latitude, DateTime date, float? depth)
        {
            // Simplified fallback if everything else fails
            float baseTemp = 8f;
            float seasonal = (float)Math.Sin((date.DayOfYear - 15) * 2 * Math.PI / 365) * 4f;
            return baseTemp + seasonal;
        }
    }

    /// <summary>
    /// Configuration for real data integration.
    /// </summary>
    public class RealDataConfig
    {
        public string CacheDirectory { get; set; } = "";
        public int CacheExpirationDays { get; set; } = 7;
        public bool UseRealData { get; set; } = true;
        public bool FallbackToSynthetic { get; set; } = true;
        public Dictionary<string, string> ApiEndpoints { get; set; } = new();
        public Dictionary<string, string> ApiKeys { get; set; } = new();
    }

    /// <summary>
    /// Status of a data source.
    /// </summary>
    public class DataSourceStatus
    {
        public string SourceId { get; set; } = "";
        public string SourceName { get; set; } = "";
        public bool IsAvailable { get; set; }
        public bool IsHealthy { get; set; }
        public DateTime LastCheck { get; set; }
        public TimeSpan ResponseTime { get; set; }
        public string StatusMessage { get; set; } = "";
    }
}
