using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using LakskongenSim.Core;

namespace LakskongenSim.Tests.Performance
{
    /// <summary>
    /// The perf gate runs alone: xUnit runs test classes in parallel, and on a
    /// two-core CI runner the timed loop shared the CPU with the rest of the
    /// suite (GitHub measured 7.2 ms/day on a run where this machine measures
    /// 0.46 ms/day).
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class PerfGateCollection
    {
        /// <summary>The collection name.</summary>
        public const string Name = "Perf gate (runs alone)";
    }

    /// <summary>
    /// CI performance gate: a realistic multi-site session must advance within a
    /// per-day budget. The soft target is 2 ms/day; the hard limit is 5 ms/day on
    /// the reference machine (M1 Pro) and scales up, never down, with how much
    /// slower the machine running the test is, measured in the same process on a
    /// fixed CPU workload.
    /// </summary>
    [Collection(PerfGateCollection.Name)]
    public class PerfGateTests
    {
        private const double WarnMsPerDay = 2.0;
        private const double FailMsPerDay = 5.0;
        private const int MeasuredDays = 365;
        private const int WarmupDays = 15;

        /// <summary>
        /// Best-of-5 time of <see cref="CalibrationWorkloadMs"/> on the reference
        /// machine (Apple M1 Pro, .NET 10, after a JIT warm-up run; 12.55-12.68 ms
        /// in three runs at load average ~6), measured 2026-10-16.
        /// </summary>
        private const double ReferenceCalibrationMs = 12.5;
        private const int CalibrationRuns = 5;

        private readonly ITestOutputHelper _output;

        public PerfGateTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void DailyTick_MultiSiteSession_StaysUnderBudget()
        {
            double calibrationMs = BestCalibrationMs();
            double machineFactor = Math.Max(1.0, calibrationMs / ReferenceCalibrationMs);
            double hardLimit = FailMsPerDay * machineFactor;

            var engine = new SimulationEngine();
            engine.Initialize(enableEvents: true, seed: 7);
            engine.Scenario.GetScenario("ressursrente")!.ApplyStartingState(engine.State);

            for (int i = 0; i < WarmupDays; i++)
                engine.Advance(1);

            var stopwatch = Stopwatch.StartNew();
            engine.Advance(MeasuredDays);
            stopwatch.Stop();

            double msPerDay = stopwatch.Elapsed.TotalMilliseconds / MeasuredDays;
            int fish = engine.State.Populations.Sum(p => p.TotalCount);

            _output.WriteLine($"Perf gate: {engine.State.SeaSites.Count} sites, {engine.State.Populations.Count} populations, {fish:N0} fish");
            _output.WriteLine($"Perf gate: calibration {calibrationMs:F2} ms (reference {ReferenceCalibrationMs:F1} ms), machine factor {machineFactor:F2}");
            _output.WriteLine($"Perf gate: {stopwatch.Elapsed.TotalMilliseconds:F2} ms total, {msPerDay:F4} ms/day " +
                              $"(soft target {WarnMsPerDay} ms/day, hard limit {hardLimit:F2} ms/day)");

            Assert.True(msPerDay < hardLimit,
                $"Daily tick {msPerDay:F4} ms/day exceeds hard budget of {hardLimit:F2} ms/day " +
                $"({FailMsPerDay} ms/day x machine factor {machineFactor:F2}).");
        }

        /// <summary>The calibration never lowers the limit below the reference budget.</summary>
        [Fact]
        public void MachineFactor_NeverTightensBelowTheReferenceBudget()
        {
            double calibrationMs = BestCalibrationMs();
            Assert.True(calibrationMs > 0.0, "the calibration workload takes measurable time");
            Assert.Equal(1.0, Math.Max(1.0, (ReferenceCalibrationMs * 0.5) / ReferenceCalibrationMs));
            Assert.Equal(3.0, Math.Max(1.0, (ReferenceCalibrationMs * 3.0) / ReferenceCalibrationMs), 9);
        }

        private static double BestCalibrationMs()
        {
            CalibrationWorkloadMs(); // JIT warm-up
            double best = double.MaxValue;
            for (int i = 0; i < CalibrationRuns; i++)
                best = Math.Min(best, CalibrationWorkloadMs());
            return best;
        }

        /// <summary>
        /// A fixed, deterministic CPU workload shaped like the daily tick: a
        /// xorshift stream, double maths and dictionary writes.
        /// </summary>
        private static double CalibrationWorkloadMs()
        {
            var table = new Dictionary<int, double>(1024);
            ulong x = 88172645463325252UL;
            double acc = 0.0;
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < 1_000_000; i++)
            {
                x ^= x << 13;
                x ^= x >> 7;
                x ^= x << 17;
                double v = (x >> 11) * (1.0 / (1UL << 53));
                acc += Math.Sqrt(v) * Math.Exp(-v);
                table[(int)(x & 1023)] = acc;
            }
            stopwatch.Stop();
            GC.KeepAlive(table);
            if (double.IsNaN(acc))
                throw new InvalidOperationException("calibration workload produced NaN");
            return stopwatch.Elapsed.TotalMilliseconds;
        }
    }
}
