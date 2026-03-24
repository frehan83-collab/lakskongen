using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace LakskongenSim.Performance
{
    /// <summary>
    /// Interface for poolable objects.
    /// </summary>
    public interface IPoolable
    {
        void Reset();
    }

    /// <summary>
    /// Generic object pool for frequently allocated objects.
    /// </summary>
    public sealed class ObjectPoolManager
    {
        private static readonly Lazy<ObjectPoolManager> _instance = new(() => new ObjectPoolManager());
        public static ObjectPoolManager Instance => _instance.Value;

        private readonly Dictionary<Type, object> _pools = new();
        private readonly object _lock = new();

        private ObjectPoolManager() { }

        /// <summary>
        /// Get or create a pool for type T.
        /// </summary>
        public T Get<T>() where T : class, IPoolable, new()
        {
            var pool = GetPool<T>();
            if (pool.TryDequeue(out var item))
            {
                item.Reset();
                return item;
            }
            return new T();
        }

        /// <summary>
        /// Return an object to its pool.
        /// </summary>
        public void Return<T>(T obj) where T : class, IPoolable, new()
        {
            if (obj == null) return;
            var pool = GetPool<T>();
            pool.Enqueue(obj);
        }

        /// <summary>
        /// Pre-warm a pool with a number of instances.
        /// </summary>
        public void PreWarm<T>(int count) where T : class, IPoolable, new()
        {
            var pool = GetPool<T>();
            for (int i = 0; i < count; i++)
                pool.Enqueue(new T());
        }

        /// <summary>
        /// Get pool statistics.
        /// </summary>
        public (int poolCount, int totalObjects) GetPoolStats()
        {
            lock (_lock)
            {
                int total = 0;
                foreach (var kvp in _pools)
                {
                    if (kvp.Value is ConcurrentQueue<object> q)
                        total += q.Count;
                }
                return (_pools.Count, total);
            }
        }

        private ConcurrentQueue<T> GetPool<T>() where T : class, IPoolable, new()
        {
            var type = typeof(T);
            lock (_lock)
            {
                if (!_pools.ContainsKey(type))
                    _pools[type] = new ConcurrentQueue<T>();
                return (ConcurrentQueue<T>)_pools[type];
            }
        }
    }
}
