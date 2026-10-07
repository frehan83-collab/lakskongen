using System;
using System.Diagnostics;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using LakskongenSim.Core;

namespace LakskongenSim.Tests.Performance
{
    /// <summary>
    /// CI performance gate: a realistic multi-site session must advance within a
    /// per-day budget. The soft target is 2 ms/day; the suite only fails the gate
    /// at 5 ms/day so shared CI runners cannot produce false failures.
    /// </summary>
    public class PerfGateTests
    {
        private const double WarnMsPerDay = 2.0;
        private const double FailMsPerDay = 5.0;
        private const int MeasuredDays = 365;
        private const int WarmupDays = 15;

        private readonly ITestOutputHelper _output;

        public PerfGateTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void DailyTick_MultiSiteSession_StaysUnderBudget()
        {
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
            _output.WriteLine($"Perf gate: {stopwatch.Elapsed.TotalMilliseconds:F2} ms total, {msPerDay:F4} ms/day " +
                              $"(soft target {WarnMsPerDay} ms/day, hard limit {FailMsPerDay} ms/day)");

            Assert.True(msPerDay < FailMsPerDay,
                $"Daily tick {msPerDay:F4} ms/day exceeds hard budget of {FailMsPerDay} ms/day.");
        }
    }
}
