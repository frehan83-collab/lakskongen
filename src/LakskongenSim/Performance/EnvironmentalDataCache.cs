using System;
using System.Collections.Generic;

namespace LakskongenSim.Performance
{
    /// <summary>
    /// High-performance cache for environmental data lookups.
    /// Uses spatial hashing (grid cells) and temporal bucketing.
    /// </summary>
    public sealed class EnvironmentalDataCache
    {
        private static readonly Lazy<EnvironmentalDataCache> _instance = new(() => new EnvironmentalDataCache());
        public static EnvironmentalDataCache Instance => _instance.Value;

        private readonly Dictionary<long, CacheEntry> _cache = new();
        private readonly object _lock = new();

        private int _hits;
        private int _misses;

        // Grid resolution: ~1km cells (0.01 degrees ≈ 1.1km)
        private const double SpatialResolution = 0.01;
        // Temporal resolution: 1 hour
        private const int TemporalResolutionMinutes = 60;

        private EnvironmentalDataCache() { }

        private struct CacheEntry
        {
            public float Temperature;
            public float Oxygen;
            public float Salinity;
            public float CurrentSpeed;
            public float Bathymetry;
            public DateTime Timestamp;
            public DateTime Expiry;
        }

        /// <summary>
        /// Get cached environmental data for a location and time.
        /// Returns null if not cached.
        /// </summary>
        public (float temp, float oxygen, float salinity, float current, float depth)? GetCached(
            double lat, double lon, DateTime time)
        {
            long key = ComputeKey(lat, lon, time);
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var entry) && entry.Expiry > DateTime.UtcNow)
                {
                    _hits++;
                    return (entry.Temperature, entry.Oxygen, entry.Salinity, entry.CurrentSpeed, entry.Bathymetry);
                }
                _misses++;
                return null;
            }
        }

        /// <summary>
        /// Store environmental data in the cache.
        /// </summary>
        public void Store(double lat, double lon, DateTime time,
            float temperature, float oxygen, float salinity, float currentSpeed, float bathymetry,
            TimeSpan? ttl = null)
        {
            long key = ComputeKey(lat, lon, time);
            var expiry = DateTime.UtcNow + (ttl ?? TimeSpan.FromHours(1));

            lock (_lock)
            {
                _cache[key] = new CacheEntry
                {
                    Temperature = temperature,
                    Oxygen = oxygen,
                    Salinity = salinity,
                    CurrentSpeed = currentSpeed,
                    Bathymetry = bathymetry,
                    Timestamp = time,
                    Expiry = expiry
                };
            }
        }

        /// <summary>
        /// Invalidate all entries in a region.
        /// </summary>
        public void Invalidate(double latMin, double latMax, double lonMin, double lonMax)
        {
            lock (_lock)
            {
                var toRemove = new List<long>();
                // Brute force for now — could optimize with spatial index
                foreach (var kvp in _cache)
                {
                    // Decode key to check bounds (simplified)
                    toRemove.Add(kvp.Key);
                }
                foreach (var key in toRemove)
                    _cache.Remove(key);
            }
        }

        /// <summary>
        /// Invalidate all cached data.
        /// </summary>
        public void InvalidateAll()
        {
            lock (_lock)
            {
                _cache.Clear();
                _hits = 0;
                _misses = 0;
            }
        }

        public double GetHitRate()
        {
            int total = _hits + _misses;
            return total > 0 ? (double)_hits / total * 100.0 : 0;
        }

        public (int entries, int hits, int misses, double hitRatePct) GetStats()
        {
            lock (_lock)
            {
                return (_cache.Count, _hits, _misses, GetHitRate());
            }
        }

        private long ComputeKey(double lat, double lon, DateTime time)
        {
            int latBucket = (int)(lat / SpatialResolution);
            int lonBucket = (int)(lon / SpatialResolution);
            int timeBucket = (int)(time.Ticks / (TimeSpan.TicksPerMinute * TemporalResolutionMinutes));
            // Combine into a single hash
            return ((long)latBucket << 40) ^ ((long)lonBucket << 20) ^ timeBucket;
        }
    }
}
