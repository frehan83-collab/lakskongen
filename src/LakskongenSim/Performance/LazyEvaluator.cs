using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace LakskongenSim.Performance
{
    /// <summary>
    /// Deferred computation for non-critical calculations.
    /// Spreads work across frames to maintain target FPS.
    /// </summary>
    public sealed class LazyEvaluator
    {
        private static readonly Lazy<LazyEvaluator> _instance = new(() => new LazyEvaluator());
        public static LazyEvaluator Instance => _instance.Value;

        private readonly SortedList<int, Queue<Action>> _queues = new();
        private readonly object _lock = new();
        private readonly Stopwatch _stopwatch = new();

        private LazyEvaluator() { }

        /// <summary>
        /// Schedule an action for deferred execution.
        /// Lower priority number = higher priority (processed first).
        /// </summary>
        public void Schedule(Action action, int priority = 5)
        {
            if (action == null) return;
            lock (_lock)
            {
                if (!_queues.ContainsKey(priority))
                    _queues[priority] = new Queue<Action>();
                _queues[priority].Enqueue(action);
            }
        }

        /// <summary>
        /// Process queued actions within a time budget.
        /// Call this once per frame.
        /// </summary>
        /// <param name="maxMs">Maximum milliseconds to spend processing.</param>
        /// <returns>Number of actions processed.</returns>
        public int ProcessBudget(double maxMs = 2.0)
        {
            int processed = 0;
            _stopwatch.Restart();

            lock (_lock)
            {
                foreach (var kvp in _queues)
                {
                    var queue = kvp.Value;
                    while (queue.Count > 0 && _stopwatch.Elapsed.TotalMilliseconds < maxMs)
                    {
                        var action = queue.Dequeue();
                        try
                        {
                            action();
                        }
                        catch
                        {
                            // Swallow errors in deferred work to not crash the frame
                        }
                        processed++;
                    }

                    if (_stopwatch.Elapsed.TotalMilliseconds >= maxMs) break;
                }
            }

            _stopwatch.Stop();
            return processed;
        }

        /// <summary>
        /// Get the total number of pending actions across all priorities.
        /// </summary>
        public int GetPendingCount()
        {
            lock (_lock)
            {
                return _queues.Values.Sum(q => q.Count);
            }
        }

        /// <summary>
        /// Clear all pending actions.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _queues.Clear();
            }
        }
    }
}
