using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.Json;

namespace Lakskongen.Data.RealData.Cache
{
    /// <summary>
    /// Generic file-based cache for real-world data with expiration support.
    /// </summary>
    public class DataCache
    {
        private readonly string _cacheDirectory;
        private readonly TimeSpan _defaultExpiration;
        private readonly Dictionary<string, CacheEntry> _memoryCache = new();
        private readonly object _lock = new();

        public DataCache(string cacheDirectory, TimeSpan? defaultExpiration = null)
        {
            _cacheDirectory = cacheDirectory;
            _defaultExpiration = defaultExpiration ?? TimeSpan.FromDays(7);
            
            Directory.CreateDirectory(_cacheDirectory);
        }

        /// <summary>
        /// Get cached data or return null if not found/expired.
        /// </summary>
        public async Task<T?> GetAsync<T>(string key) where T : class
        {
            // Check memory cache first
            lock (_lock)
            {
                if (_memoryCache.TryGetValue(key, out var memEntry) && !memEntry.IsExpired)
                {
                    return memEntry.Data as T;
                }
            }

            // Check file cache
            string filePath = GetCacheFilePath(key);
            if (!File.Exists(filePath))
                return null;

            try
            {
                string json = await File.ReadAllTextAsync(filePath);
                var entry = JsonSerializer.Deserialize<CacheEntry<T>>(json);

                if (entry == null || entry.IsExpired)
                {
                    File.Delete(filePath);
                    return null;
                }

                // Update memory cache
                lock (_lock)
                {
                    _memoryCache[key] = new CacheEntry 
                    { 
                        Data = entry.Data, 
                        ExpiresAt = entry.ExpiresAt 
                    };
                }

                return entry.Data;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Store data in cache.
        /// </summary>
        public async Task SetAsync<T>(string key, T data, TimeSpan? expiration = null) where T : class
        {
            var expiresAt = DateTime.UtcNow.Add(expiration ?? _defaultExpiration);
            var entry = new CacheEntry<T>
            {
                Data = data,
                ExpiresAt = expiresAt,
                CreatedAt = DateTime.UtcNow
            };

            // Update memory cache
            lock (_lock)
            {
                _memoryCache[key] = new CacheEntry 
                { 
                    Data = data, 
                    ExpiresAt = expiresAt 
                };
            }

            // Write to file
            string filePath = GetCacheFilePath(key);
            string json = JsonSerializer.Serialize(entry, new JsonSerializerOptions 
            { 
                WriteIndented = false 
            });
            
            await File.WriteAllTextAsync(filePath, json);
        }

        /// <summary>
        /// Check if data exists in cache and is not expired.
        /// </summary>
        public bool HasValidCache(string key)
        {
            lock (_lock)
            {
                if (_memoryCache.TryGetValue(key, out var memEntry))
                    return !memEntry.IsExpired;
            }

            string filePath = GetCacheFilePath(key);
            if (!File.Exists(filePath))
                return false;

            try
            {
                string json = File.ReadAllText(filePath);
                var entry = JsonSerializer.Deserialize<CacheEntry<object>>(json);
                return entry != null && !entry.IsExpired;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Invalidate a specific cache entry.
        /// </summary>
        public void Invalidate(string key)
        {
            lock (_lock)
            {
                _memoryCache.Remove(key);
            }

            string filePath = GetCacheFilePath(key);
            if (File.Exists(filePath))
                File.Delete(filePath);
        }

        /// <summary>
        /// Clear all cached data.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _memoryCache.Clear();
            }

            foreach (var file in Directory.GetFiles(_cacheDirectory, "*.cache"))
            {
                try { File.Delete(file); } catch { }
            }
        }

        /// <summary>
        /// Clean up expired cache entries.
        /// </summary>
        public async Task CleanupAsync()
        {
            int cleaned = 0;
            
            foreach (var file in Directory.GetFiles(_cacheDirectory, "*.cache"))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(file);
                    var entry = JsonSerializer.Deserialize<CacheEntry<object>>(json);
                    
                    if (entry == null || entry.IsExpired)
                    {
                        File.Delete(file);
                        cleaned++;
                    }
                }
                catch
                {
                    try { File.Delete(file); } catch { }
                    cleaned++;
                }
            }
        }

        private string GetCacheFilePath(string key)
        {
            // Sanitize key for filename
            string safeKey = key.Replace("/", "_").Replace("\\", "_").Replace(":", "_");
            return Path.Combine(_cacheDirectory, $"{safeKey}.cache");
        }
    }

    /// <summary>
    /// Non-generic cache entry for memory cache.
    /// </summary>
    internal class CacheEntry
    {
        public object Data { get; set; } = null!;
        public DateTime ExpiresAt { get; set; }
        public bool IsExpired => DateTime.UtcNow > ExpiresAt;
    }
}
