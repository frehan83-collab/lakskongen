using System;
using System.Collections.Generic;

namespace LakskongenSim.Performance
{
    /// <summary>
    /// LRU cache for expensive economic calculations.
    /// </summary>
    public sealed class EconomicCalculationCache
    {
        private static readonly Lazy<EconomicCalculationCache> _instance = new(() => new EconomicCalculationCache());
        public static EconomicCalculationCache Instance => _instance.Value;

        private readonly Dictionary<string, CacheEntry> _cache = new();
        private readonly LinkedList<string> _lruOrder = new();
        private readonly object _lock = new();

        private int _maxEntries = 1000;
        private TimeSpan _defaultTtl = TimeSpan.FromMinutes(5);
        private int _hits;
        private int _misses;

        private struct CacheEntry
        {
            public object Value;
            public DateTime Expiry;
            public LinkedListNode<string> LruNode;
        }

        private EconomicCalculationCache() { }

        /// <summary>
        /// Get or compute a cached value.
        /// </summary>
        public T GetOrCompute<T>(string key, Func<T> factory, TimeSpan? ttl = null)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var entry) && entry.Expiry > DateTime.UtcNow)
                {
                    _hits++;
                    // Move to front of LRU
                    _lruOrder.Remove(entry.LruNode);
                    _lruOrder.AddFirst(entry.LruNode);
                    return (T)entry.Value;
                }
            }

            _misses++;
            T value = factory();

            lock (_lock)
            {
                // Evict if at capacity
                while (_cache.Count >= _maxEntries && _lruOrder.Last != null)
                {
                    var evictKey = _lruOrder.Last!.Value;
                    _lruOrder.RemoveLast();
                    _cache.Remove(evictKey);
                }

                var node = _lruOrder.AddFirst(key);
                _cache[key] = new CacheEntry
                {
                    Value = value!,
                    Expiry = DateTime.UtcNow + (ttl ?? _defaultTtl),
                    LruNode = node
                };
            }

            return value;
        }

        /// <summary>
        /// Invalidate all cached entries.
        /// </summary>
        public void InvalidateAll()
        {
            lock (_lock)
            {
                _cache.Clear();
                _lruOrder.Clear();
                _hits = 0;
                _misses = 0;
            }
        }

        /// <summary>
        /// Invalidate a specific key.
        /// </summary>
        public void Invalidate(string key)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var entry))
                {
                    _lruOrder.Remove(entry.LruNode);
                    _cache.Remove(key);
                }
            }
        }

        public (int entries, int hits, int misses, double hitRatePct) GetStats()
        {
            int total = _hits + _misses;
            double rate = total > 0 ? (double)_hits / total * 100.0 : 0;
            return (_cache.Count, _hits, _misses, rate);
        }
    }
}
