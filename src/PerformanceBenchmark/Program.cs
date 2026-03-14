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
            Console.WriteLine("=== Lakskongen Performance Benchmark ===");
            Console.WriteLine($"=== T56: Biology Loop Optimization ===\n");
            Console.WriteLine($"Runtime: .NET {Environment.Version}");
            Console.WriteLine($"OS: {Environment.OSVersion}");
            Console.WriteLine($"Processors: {Environment.ProcessorCount}\n");

            // ─── Scenario 1: Single cohort, 100k fish ───
            RunBenchmark("Scenario 1: Single cohort (100k fish)", 1, 100000);

            // ─── Scenario 2: Multiple cohorts simulating realistic farm ───
            RunBenchmark("Scenario 2: 100 cohorts (1k fish each = 100k total)", 100, 1000);

            // ─── Scenario 3: Many small cohorts (stress test) ───
            RunBenchmark("Scenario 3: 500 cohorts (200 fish each = 100k total)", 500, 200);
        }

        static void RunBenchmark(string scenarioName, int numCohorts, int fishPerCohort)
        {
            Console.WriteLine($"\n{"".PadRight(50, '=')}");
            Console.WriteLine($"  {scenarioName}");
            Console.WriteLine($"{"".PadRight(50, '=')}");

            // Initialize simulation
            var engine = new SimulationEngine();
            engine.Initialize(enableEvents: false);

            // Create population with multiple cohorts
            var population = new FishPopulation(engine.Growth, engine.Mortality);
            for (int i = 0; i < numCohorts; i++)
            {
                // Vary age to simulate realistic multi-cohort farm
                int startAge = 300 + (i * 2); // Ages from 300-300+2*numCohorts days
                var cohort = new FishPopulation.Cohort(
                    ageDays: startAge,
                    count: fishPerCohort,
                    meanWeightKg: 0.1 + (startAge * 0.005),
                    stage: LifeStage.Smolt
                );
                population.AddCohort(cohort);
            }
            engine.State.Populations.Add(population);

            int totalFish = population.TotalCount;
            Console.WriteLine($"Setup: {numCohorts} cohorts, {totalFish:N0} total fish");

            // Warm-up (JIT)
            for (int i = 0; i < 5; i++)
                engine.Advance(1);

            // Capture GC baseline
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            int gc0Before = GC.CollectionCount(0);
            int gc1Before = GC.CollectionCount(1);
            int gc2Before = GC.CollectionCount(2);
            long memBefore = GC.GetTotalMemory(false);

            // Benchmark 365 days
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

            // Results
            double totalMs = sw.Elapsed.TotalMilliseconds;
            double avgMs = dayTimings.Average();
            Array.Sort(dayTimings);
            double medianMs = dayTimings[182];
            double p95Ms = dayTimings[(int)(365 * 0.95)];
            double p99Ms = dayTimings[(int)(365 * 0.99)];
            double fps60Target = 1000.0 / 60.0;

            int gc0After = GC.CollectionCount(0);
            int gc1After = GC.CollectionCount(1);
            int gc2After = GC.CollectionCount(2);
            long memAfter = GC.GetTotalMemory(false);

            Console.WriteLine($"\n  Total:    {totalMs:F2} ms ({totalMs / 1000.0:F3} s)");
            Console.WriteLine($"  Avg/day:  {avgMs:F3} ms");
            Console.WriteLine($"  Median:   {medianMs:F3} ms");
            Console.WriteLine($"  P95:      {p95Ms:F3} ms");
            Console.WriteLine($"  P99:      {p99Ms:F3} ms");
            Console.WriteLine($"  Min:      {dayTimings[0]:F3} ms");
            Console.WriteLine($"  Max:      {dayTimings[364]:F3} ms");
            Console.WriteLine($"  Days/sec: {1000.0 / avgMs:F0}");
            Console.WriteLine($"  60 FPS:   {(avgMs <= fps60Target ? "✅ PASS" : "❌ FAIL")} ({avgMs / fps60Target:F4}x budget)");
            Console.WriteLine($"  GC:       Gen0={gc0After - gc0Before}, Gen1={gc1After - gc1Before}, Gen2={gc2After - gc2Before}");
            Console.WriteLine($"  Memory:   {(memAfter - memBefore) / 1024.0:+#,0;-#,0;0} KB delta");

            // Final state
            var finalPop = engine.State.Populations[0];
            Console.WriteLine($"  Fish:     {finalPop.TotalCount:N0} remaining ({finalPop.Cohorts.Count} cohorts)");
        }
    }
}
