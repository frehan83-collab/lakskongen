using System;
using System.Diagnostics;
using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Biology;
using LakskongenSim.Data;
using LakskongenSim.Farm;

namespace PerformanceBenchmark
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("╔══════════════════════════════════════════════════╗");
            Console.WriteLine("║  Lakskongen Performance Benchmark — T61         ║");
            Console.WriteLine("║  Validating T56 Optimization (37% target)       ║");
            Console.WriteLine("╚══════════════════════════════════════════════════╝");
            Console.WriteLine($"Runtime: .NET {Environment.Version}");
            Console.WriteLine($"OS: {Environment.OSVersion}");
            Console.WriteLine($"Processors: {Environment.ProcessorCount}");
            Console.WriteLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");

            // ─── Scenario 1: Single cohort (baseline) — 100k fish, 365 days ───
            RunBenchmark("Scenario 1: Single Cohort (Baseline)", 1, 100000, enableEvents: false, runs: 3);

            // ─── Scenario 2: Multi-cohort (realistic) — 10 cohorts, 100k total ───
            RunBenchmark("Scenario 2: Multi-Cohort (Realistic)", 10, 10000, enableEvents: false, runs: 3);

            // ─── Scenario 3: Large scale (stress) — 500 cohorts, 500k total ───
            RunBenchmark("Scenario 3: Large Scale (Stress)", 500, 1000, enableEvents: false, runs: 3);

            // ─── Scenario 4: Full simulation — all systems enabled ───
            RunBenchmark("Scenario 4: Full Simulation (All Systems)", 10, 10000, enableEvents: true, runs: 3);

            Console.WriteLine("\n╔══════════════════════════════════════════════════╗");
            Console.WriteLine("║  Benchmark Complete                             ║");
            Console.WriteLine("╚══════════════════════════════════════════════════╝");
        }

        static void RunBenchmark(string scenarioName, int numCohorts, int fishPerCohort, bool enableEvents, int runs)
        {
            Console.WriteLine($"\n{"".PadRight(60, '═')}");
            Console.WriteLine($"  {scenarioName}");
            Console.WriteLine($"  Cohorts: {numCohorts}, Fish/cohort: {fishPerCohort:N0}, Events: {enableEvents}");
            Console.WriteLine($"{"".PadRight(60, '═')}");

            double bestTotalMs = double.MaxValue;
            double[] bestDayTimings = null!;
            int bestGc0 = 0, bestGc1 = 0, bestGc2 = 0;
            long bestMemDelta = 0;
            int bestFinalFish = 0;
            int bestFinalCohorts = 0;

            for (int run = 0; run < runs; run++)
            {
                // Initialize fresh simulation each run
                var engine = new SimulationEngine();
                engine.Initialize(enableEvents: enableEvents);

                // Create population with specified cohorts
                var population = new FishPopulation(engine.Growth, engine.Mortality);
                for (int i = 0; i < numCohorts; i++)
                {
                    int startAge = 300 + (i * 3);
                    var cohort = new FishPopulation.Cohort(
                        ageDays: startAge,
                        count: fishPerCohort,
                        meanWeightKg: 0.1 + (startAge * 0.005),
                        stage: LifeStage.Smolt
                    );
                    population.AddCohort(cohort);
                }
                engine.State.Populations.Add(population);

                // For full simulation, add a zone for disease system
                if (enableEvents && engine.State.Zones.Count == 0)
                {
                    try
                    {
                        var zone = new LakskongenSim.Environment.Zone("PO-3", 65.0);
                        engine.State.Zones.Add(zone);
                    }
                    catch { /* Zone may not be needed */ }
                }

                int totalFish = population.TotalCount;

                // Warm-up (JIT compilation)
                for (int i = 0; i < 10; i++)
                    engine.Advance(1);

                // Force GC and capture baseline
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, true, true);

                int gc0Before = GC.CollectionCount(0);
                int gc1Before = GC.CollectionCount(1);
                int gc2Before = GC.CollectionCount(2);
                long memBefore = GC.GetTotalMemory(true);

                // Benchmark: simulate 365 days
                var dayTimings = new double[365];
                var sw = Stopwatch.StartNew();

                for (int day = 0; day < 365; day++)
                {
                    var daySw = Stopwatch.StartNew();
                    engine.Advance(1);
                    daySw.Stop();
                    dayTimings[day] = daySw.Elapsed.TotalMilliseconds;
                }

                sw.Stop();

                double totalMs = sw.Elapsed.TotalMilliseconds;
                int gc0 = GC.CollectionCount(0) - gc0Before;
                int gc1 = GC.CollectionCount(1) - gc1Before;
                int gc2 = GC.CollectionCount(2) - gc2Before;
                long memAfter = GC.GetTotalMemory(false);
                long memDelta = memAfter - memBefore;

                var finalPop = engine.State.Populations[0];

                Console.Write($"  Run {run + 1}/{runs}: {totalMs:F2} ms total, ");
                Console.Write($"GC({gc0}/{gc1}/{gc2}), ");
                Console.WriteLine($"Fish: {finalPop.TotalCount:N0}");

                if (totalMs < bestTotalMs)
                {
                    bestTotalMs = totalMs;
                    bestDayTimings = (double[])dayTimings.Clone();
                    bestGc0 = gc0;
                    bestGc1 = gc1;
                    bestGc2 = gc2;
                    bestMemDelta = memDelta;
                    bestFinalFish = finalPop.TotalCount;
                    bestFinalCohorts = finalPop.Cohorts.Count;
                }
            }

            // Compute stats from best run
            Array.Sort(bestDayTimings);
            double avgMs = bestDayTimings.Average();
            double medianMs = bestDayTimings[182];
            double p95Ms = bestDayTimings[(int)(365 * 0.95)];
            double p99Ms = bestDayTimings[(int)(365 * 0.99)];
            double minMs = bestDayTimings[0];
            double maxMs = bestDayTimings[364];
            double fps60Budget = 1000.0 / 60.0; // 16.67ms

            Console.WriteLine($"\n  ┌─ Best Run Results ──────────────────────────");
            Console.WriteLine($"  │ Total (365d): {bestTotalMs:F2} ms ({bestTotalMs / 1000.0:F3} s)");
            Console.WriteLine($"  │ Avg/day:      {avgMs:F3} ms");
            Console.WriteLine($"  │ Median/day:   {medianMs:F3} ms");
            Console.WriteLine($"  │ P95/day:      {p95Ms:F3} ms");
            Console.WriteLine($"  │ P99/day:      {p99Ms:F3} ms");
            Console.WriteLine($"  │ Min/day:      {minMs:F3} ms");
            Console.WriteLine($"  │ Max/day:      {maxMs:F3} ms");
            Console.WriteLine($"  │ Days/sec:     {1000.0 / avgMs:F0}");
            Console.WriteLine($"  │ FPS equiv:    {1000.0 / avgMs:F0} (target: 60)");
            Console.WriteLine($"  │ 60 FPS:       {(avgMs <= fps60Budget ? "✅ PASS" : "❌ FAIL")} ({avgMs / fps60Budget:F4}x of budget)");
            Console.WriteLine($"  │ GC:           Gen0={bestGc0}, Gen1={bestGc1}, Gen2={bestGc2}");
            Console.WriteLine($"  │ Memory delta: {bestMemDelta / 1024.0:+#,0;-#,0;0} KB");
            Console.WriteLine($"  │ Fish remain:  {bestFinalFish:N0} ({bestFinalCohorts} cohorts)");
            Console.WriteLine($"  └────────────────────────────────────────────────");
        }
    }
}
