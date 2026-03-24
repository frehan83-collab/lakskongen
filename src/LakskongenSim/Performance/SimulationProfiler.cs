using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace LakskongenSim.Performance
{
    public struct SystemPerformanceReport
    {
        public string SystemName;
        public double AverageTimeMs;
        public double MaxTimeMs;
        public double MinTimeMs;
        public int TotalCalls;
    }

    public struct PerformanceReport
    {
        public double CurrentFPS;
        public double AverageFrameTimeMs;
        public double WorstFrameTimeMs;
        public long GcCollectionCount;
        public double EstimatedMemoryUsageMB;
        public List<SystemPerformanceReport> SystemReports;

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Performance Report ===");
            sb.AppendLine($"FPS: {CurrentFPS:F1}");
            sb.AppendLine($"Avg Frame Time: {AverageFrameTimeMs:F2}ms");
            sb.AppendLine($"Worst Frame: {WorstFrameTimeMs:F2}ms");
            sb.AppendLine($"GC Collections: {GcCollectionCount}");
            sb.AppendLine($"Memory: {EstimatedMemoryUsageMB:F1}MB");
            if (SystemReports != null)
            {
                foreach (var sys in SystemReports)
                    sb.AppendLine($"  {sys.SystemName}: avg={sys.AverageTimeMs:F2}ms max={sys.MaxTimeMs:F2}ms calls={sys.TotalCalls}");
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Lightweight profiler for tracking frame time, GC pressure, and per-system execution time.
    /// </summary>
    public sealed class SimulationProfiler
    {
        private static readonly Lazy<SimulationProfiler> _instance = new(() => new SimulationProfiler());
        public static SimulationProfiler Instance => _instance.Value;

        private readonly Stopwatch _frameStopwatch = new();
        private readonly Queue<double> _frameTimes = new();
        private readonly Dictionary<string, List<double>> _systemTimings = new();
        private readonly Stopwatch _systemStopwatch = new();
        private string? _currentSystem;

        private const int MaxSamples = 300; // ~5 seconds at 60fps
        private long _lastGcCount;

        private SimulationProfiler()
        {
            _lastGcCount = GC.CollectionCount(0);
        }

        public void BeginFrame()
        {
            _frameStopwatch.Restart();
        }

        public void EndFrame()
        {
            _frameStopwatch.Stop();
            double ms = _frameStopwatch.Elapsed.TotalMilliseconds;
            _frameTimes.Enqueue(ms);
            while (_frameTimes.Count > MaxSamples) _frameTimes.Dequeue();
        }

        public void BeginSystem(string name)
        {
            _currentSystem = name;
            _systemStopwatch.Restart();
        }

        public void EndSystem()
        {
            if (_currentSystem == null) return;
            _systemStopwatch.Stop();
            double ms = _systemStopwatch.Elapsed.TotalMilliseconds;

            if (!_systemTimings.ContainsKey(_currentSystem))
                _systemTimings[_currentSystem] = new List<double>();

            var list = _systemTimings[_currentSystem];
            list.Add(ms);
            if (list.Count > MaxSamples) list.RemoveAt(0);

            _currentSystem = null;
        }

        public void TrackSystem(string name, double elapsedMs)
        {
            if (!_systemTimings.ContainsKey(name))
                _systemTimings[name] = new List<double>();

            var list = _systemTimings[name];
            list.Add(elapsedMs);
            if (list.Count > MaxSamples) list.RemoveAt(0);
        }

        public double CurrentFPS => _frameTimes.Count > 0 ? 1000.0 / _frameTimes.Average() : 0;
        public double AverageFrameTimeMs => _frameTimes.Count > 0 ? _frameTimes.Average() : 0;

        public PerformanceReport GetReport()
        {
            long gcNow = GC.CollectionCount(0);
            long gcDelta = gcNow - _lastGcCount;
            _lastGcCount = gcNow;

            var systemReports = _systemTimings.Select(kvp =>
            {
                var samples = kvp.Value;
                return new SystemPerformanceReport
                {
                    SystemName = kvp.Key,
                    AverageTimeMs = samples.Average(),
                    MaxTimeMs = samples.Max(),
                    MinTimeMs = samples.Min(),
                    TotalCalls = samples.Count
                };
            }).ToList();

            return new PerformanceReport
            {
                CurrentFPS = CurrentFPS,
                AverageFrameTimeMs = AverageFrameTimeMs,
                WorstFrameTimeMs = _frameTimes.Count > 0 ? _frameTimes.Max() : 0,
                GcCollectionCount = gcDelta,
                EstimatedMemoryUsageMB = GC.GetTotalMemory(false) / (1024.0 * 1024.0),
                SystemReports = systemReports
            };
        }

        public void Reset()
        {
            _frameTimes.Clear();
            _systemTimings.Clear();
            _lastGcCount = GC.CollectionCount(0);
        }
    }
}
