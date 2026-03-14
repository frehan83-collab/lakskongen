using System;
using System.Diagnostics;
using LakskongenSim.Core;
using LakskongenSim.Biology;
using LakskongenSim.Farm;

namespace PerformanceBenchmark
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== Lakskongen Performance Benchmark ===\n");

            // Initialize simulation
            var engine = new SimulationEngine();
            engine.Initialize(enableEvents: false);

            // Add test population with 100k fish
            Console.WriteLine("Creating test population (100,000 fish)...");
            var hatchery = new Hatchery();
            hatchery.OnNewCohort += cohort =>
            {
                var population = new FishPopulation(engine.Growth, engine.Mortality);
                population.AddCohort(cohort);
                engine.State.Populations.Add(population);
            };

            hatchery.ReleaseSmolt(100000, "test-zone-1");
            Console.WriteLine($"Population created: {engine.State.Populations[0].Cohorts[0].Count} fish\n");

            // Warm-up
            Console.WriteLine("Warming up (10 ticks)...");
            for (int i = 0; i < 10; i++)
            {
                engine.Advance(1);
            }

            // Benchmark 100 days
            Console.WriteLine("\nBenchmarking 100 simulation days...");
            var stopwatch = Stopwatch.StartNew();
            
            for (int day = 0; day < 100; day++)
            {
                engine.Advance(1);
            }

            stopwatch.Stop();
            
            double totalMs = stopwatch.Elapsed.TotalMilliseconds;
            double msPerDay = totalMs / 100.0;
            double daysPerSecond = 1000.0 / msPerDay;
            double fps60Target = 1000.0 / 60.0; // 16.67ms budget at 60 FPS
            
            Console.WriteLine("\n=== Results ===");
            Console.WriteLine($"Total time: {totalMs:F2} ms");
            Console.WriteLine($"Average per day: {msPerDay:F2} ms");
            Console.WriteLine($"Days per second: {daysPerSecond:F2}");
            Console.WriteLine($"\n60 FPS target: {fps60Target:F2} ms per tick");
            Console.WriteLine($"Current performance: {(msPerDay <= fps60Target ? "✅ PASS" : "❌ FAIL")}");
            Console.WriteLine($"Overhead factor: {(msPerDay / fps60Target):F2}x");

            Console.WriteLine("\n=== Final State ===");
            var pop = engine.State.Populations[0];
            var cohort = pop.Cohorts[0];
            Console.WriteLine($"Fish count: {cohort.Count}");
            Console.WriteLine($"Average weight: {cohort.MeanWeightKg:F3} kg");
            Console.WriteLine($"Age: {cohort.AgeInDays} days");
        }
    }
}
