using Lakskongen.Data.RealData.Cache;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lakskongen.Data.RealData.Layers;
using Lakskongen.Data.RealData.Models;

namespace Lakskongen.Data.RealData
{
    public class RealDataOrchestrator : IRealDataProvider
    {
        private readonly DataCache _cache;
        private readonly List<IDataLoader> _dataLoaders;
        private readonly Dictionary<string, ITemporalDataLayer> _layers;
        private readonly RealDataConfig _config;

        public RealDataOrchestrator(RealDataConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            
            _cache = new DataCache(config.CacheDirectory, TimeSpan.FromDays(config.CacheExpirationDays));
            _dataLoaders = new List<IDataLoader>();
            _layers = new Dictionary<string, ITemporalDataLayer>();

            InitializeLayers();
        }

        private void InitializeLayers()
        {
            _layers["sst"] = new Layers.SstLayer(_cache, _dataLoaders, _config.SnapshotDate);
            _layers["chl"] = new Layers.ChlorophyllLayer(_cache, _dataLoaders, _config.SnapshotDate);
            _layers["waves"] = new Layers.WavesLayer(_cache, _dataLoaders, _config.SnapshotDate);
            _layers["wind"] = new Layers.WindLayer(_cache, _dataLoaders, _config.SnapshotDate);
            _layers["currents"] = new Layers.CurrentLayer(_cache, _dataLoaders, _config.SnapshotDate);
            
            _layers["biomass"] = new Layers.BiomassLayer(_cache, _dataLoaders, _config.SnapshotDate);
            _layers["lice"] = new Layers.SeaLiceLayer(_cache, _dataLoaders, _config.SnapshotDate);
        }

        public ITemporalDataLayer GetLayer(string layerName)
        {
            if (_layers.TryGetValue(layerName, out var layer))
                return layer;
            throw new ArgumentException($"Layer '{layerName}' not found. Available: {string.Join(", ", _layers.Keys)}");
        }

        public async Task<float> GetSstAsync(float latitude, float longitude, DateTime timestamp, float? depth = null)
        {
            if (!_layers.TryGetValue("sst", out var layer))
                return 8.0f;
            return layer.GetValue(latitude, longitude, timestamp, depth);
        }

        public async Task<float> GetChlorophyllAsync(float latitude, float longitude, DateTime timestamp, float? depth = null)
        {
            if (!_layers.TryGetValue("chl", out var layer))
                return 0.5f;
            return layer.GetValue(latitude, longitude, timestamp, depth);
        }

        public async Task<float> GetWaveHeightAsync(float latitude, float longitude, DateTime timestamp)
        {
            if (!_layers.TryGetValue("waves", out var layer))
                return 1.5f;
            return layer.GetValue(latitude, longitude, timestamp, null);
        }

        public async Task<float> GetWindSpeedAsync(float latitude, float longitude, DateTime timestamp)
        {
            if (!_layers.TryGetValue("wind", out var layer))
                return 8.0f;
            return layer.GetValue(latitude, longitude, timestamp, null);
        }

        public async Task<float> GetCurrentSpeedAsync(float latitude, float longitude, DateTime timestamp, float? depth = null)
        {
            if (!_layers.TryGetValue("currents", out var layer))
                return 0.3f;
            return layer.GetValue(latitude, longitude, timestamp, depth);
        }

        public async Task<float> GetBiomassAsync(float latitude, float longitude, DateTime timestamp)
        {
            if (!_layers.TryGetValue("biomass", out var layer))
                return 3.0f;
            return layer.GetValue(latitude, longitude, timestamp, null);
        }

        public async Task<float> GetLiceAsync(float latitude, float longitude, DateTime timestamp)
        {
            if (!_layers.TryGetValue("lice", out var layer))
                return 0.2f;
            return layer.GetValue(latitude, longitude, timestamp, null);
        }

        public async Task<EnvironmentalSnapshot> GetSnapshotAsync(float latitude, float longitude, DateTime timestamp)
        {
            return new EnvironmentalSnapshot
            {
                Latitude = latitude,
                Longitude = longitude,
                Timestamp = timestamp,
                SeaSurfaceTemperature = await GetSstAsync(latitude, longitude, timestamp),
                ChlorophyllA = await GetChlorophyllAsync(latitude, longitude, timestamp),
                WaveHeight = await GetWaveHeightAsync(latitude, longitude, timestamp),
                WindSpeed = await GetWindSpeedAsync(latitude, longitude, timestamp),
                CurrentSpeed = await GetCurrentSpeedAsync(latitude, longitude, timestamp),
                Biomass = await GetBiomassAsync(latitude, longitude, timestamp),
                LicePerFish = await GetLiceAsync(latitude, longitude, timestamp)
            };
        }

        public DataProvenance GetProvenance(string layerName)
        {
            if (!_layers.TryGetValue(layerName, out var layer))
                return new DataProvenance { LayerName = layerName, Available = false };

            return new DataProvenance
            {
                LayerName = layerName,
                VariableName = layer.VariableName,
                Unit = layer.Unit,
                Available = true,
                License = GetLayerLicense(layerName),
                SourceDescription = GetSourceDescription(layerName),
                RetrievalDate = _config.SnapshotDate,
                TemporalCoverage = GetTemporalCoverage(layerName),
                SpatialResolutionKm = GetSpatialResolution(layerName)
            };
        }

        private string GetLayerLicense(string layerName)
        {
            switch (layerName)
            {
                case "sst": return "CMEMS: Free for commercial use with DOI citation (DOI: 10.48670/moi-00021). Perpetual license until 2028.";
                case "chl": return "CMEMS: Free for commercial use with DOI citation. Attribution required.";
                case "waves": return "CMEMS WAVERYS: Free for commercial use with DOI citation (DOI: 10.48670/moi-00022).";
                case "wind": return "ERA5 / MET Norway: CC-BY 4.0 (since 2025-07-02). Attribution required.";
                case "currents": return "CMEMS / MET Norway: NLOD 2.0 + CC-BY 4.0. Attribution required.";
                case "biomass": return "Fiskeridirektoratet: NLOD 1.0. Attribution 'Kilde: Fiskeridirektoratet'.";
                case "lice": return "BarentsWatch: NLOD. Attribution 'Data delivered by BarentsWatch' + Mattilsynet credit for lice data.";
                default: return "Unknown";
            }
        }

        private string GetSourceDescription(string layerName)
        {
            switch (layerName)
            {
                case "sst": return "CMEMS GLORYS12v1 reanalysis (1993-present, 1/12° daily)";
                case "chl": return "CMEMS Ocean Colour (Sentinel-3 OLCI, MODIS, VIIRS, 1-4 km daily)";
                case "waves": return "CMEMS WAVERYS reanalysis (1980-present, 1/5° 3-hourly)";
                case "wind": return "ERA5 reanalysis (1979-present, 0.25° hourly) + MET Norway Locationforecast";
                case "currents": return "CMEMS GLOBAL_MULTIYEAR_PHY (1/12° daily) + MET Norway NorKyst";
                case "biomass": return "Fiskeridirektoratet statistics (2005-2024 monthly, NLOD) + BarentsWatch site data";
                case "lice": return "BarentsWatch FishHealth API (Mattilsynet weekly lice counts, NLOD)";
                default: return "Unknown";
            }
        }

        private string GetTemporalCoverage(string layerName)
        {
            switch (layerName)
            {
                case "sst": return "1993-present (daily)";
                case "chl": return "1997-present (daily)";
                case "waves": return "1980-present (3-hourly)";
                case "wind": return "1979-present (hourly)";
                case "currents": return "1993-present (daily)";
                case "biomass": return "2005-present (monthly)";
                case "lice": return "2012-present (weekly)";
                default: return "Unknown";
            }
        }

        private float GetSpatialResolution(string layerName)
        {
            switch (layerName)
            {
                case "sst": return 8f;
                case "chl": return 4f;
                case "waves": return 22f;
                case "wind": return 30f;
                case "currents": return 8f;
                case "biomass": return 1f;
                case "lice": return 1f;
                default: return 0f;
            }
        }
    }
}
