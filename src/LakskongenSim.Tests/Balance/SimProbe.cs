using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Core;

namespace LakskongenSim.Tests.Balance
{
    /// <summary>
    /// Scenario presets for the balance probe. Each is a fixed, seeded farm
    /// setup so the measured distributions are reproducible run-to-run.
    /// </summary>
    public enum SimScenario
    {
        /// <summary>One commercial sea site, 50k smolt, storyteller events off.
        /// The primary realism-proof scenario: fully deterministic when seeded.</summary>
        Baseline,

        /// <summary>Same site at double stocking (100k smolt) to show the
        /// sensitivity of the distributions to a larger farm. Also deterministic.</summary>
        HighStocking
    }

    /// <summary>
    /// A tiny order-statistics summary of one metric across the seed sweep.
    /// Primitives-only so the balance report is easy to assert on.
    /// </summary>
    public readonly record struct Distribution(double Min, double Median, double P90, double Mean)
    {
        /// <summary>Compute min / median / p90 / mean (nearest-rank percentiles) from a sample.</summary>
        public static Distribution From(IReadOnlyList<double> values)
        {
            if (values.Count == 0) return new Distribution(double.NaN, double.NaN, double.NaN, double.NaN);
            var sorted = values.OrderBy(v => v).ToArray();
            return new Distribution(
                sorted[0],
                Percentile(sorted, 0.50),
                Percentile(sorted, 0.90),
                values.Average());
        }

        private static double Percentile(double[] sorted, double q)
        {
            int idx = (int)Math.Round(q * (sorted.Length - 1), MidpointRounding.AwayFromZero);
            return sorted[Math.Clamp(idx, 0, sorted.Length - 1)];
        }

        /// <summary>Human-readable "min / median / p90" triple.</summary>
        public string Triple() => $"{Min,9:F2} / {Median,9:F2} / {P90,9:F2}";
    }

    /// <summary>One seeded 3-year run's pooled per-cycle metrics.</summary>
    public sealed record SimMetrics(
        double MeanFcr,
        double SeaMortalityPercentPerCycle,
        double MeanHarvestWeightKg,
        double FeedCostPerKgProducedNok,
        double RealizedPriceNokPerKg,
        double PeakLicePerFish,
        double PeakAdultFemaleLicePerFish,
        double EscapesPerYear);

    /// <summary>Per-scenario distribution across all seeds in the sweep.</summary>
    public sealed record ScenarioDistribution(
        SimScenario Scenario,
        int SeedFrom,
        int SeedTo,
        Distribution MeanFcr,
        Distribution SeaMortalityPercentPerCycle,
        Distribution MeanHarvestWeightKg,
        Distribution FeedCostPerKgProducedNok,
        Distribution RealizedPriceNokPerKg,
        Distribution PeakLicePerFish,
        Distribution EscapesPerYear)
    {
        /// <summary>Number of seeded runs backing this distribution.</summary>
        public int Runs => SeedTo - SeedFrom + 1;
    }

    /// <summary>
    /// Deterministic balance harness. Runs N seeded three-year (1095-day) sims
    /// across scenario presets and reduces each metric to a min/median/p90/mean
    /// distribution. Pure in-process (no Godot), reusable from tests and tools.
    ///
    /// A run models the real grow-out loop: release smolt, grow until the stock
    /// reaches the calibrated target harvest weight, record the per-cycle
    /// metrics, then harvest and re-stock. Each seed therefore contributes one
    /// or more completed cycles to the distribution, so "sea mortality %/cycle"
    /// is genuinely per cycle rather than a 3-year accumulation, and feed cost
    /// per kg produced is measured per harvested cycle.
    ///
    /// Setup note: scenarios stock a single <c>SeaSite</c> but no <c>Zone</c>.
    /// The zone-level eutrophication model (Environment.Zone) collapses
    /// dissolved oxygen to 0 once biomass is realistic, which multiplies the
    /// feed-system FCR by ~1.5x and hides the calibrated feed path. Leaving the
    /// zone list empty keeps the biology deterministic and isolates the
    /// feed/biology/market behavior the balance bands are about. See
    /// docs/BALANCE.md "Deferred / needs engine work".
    /// </summary>
    public static class SimProbe
    {
        /// <summary>Full production horizon: 3 simulated years.</summary>
        public const int HorizonDays = 1095;

        /// <summary>Nominal sea grow-out window (calibrated smolt→harvest, 540 days).
        /// Reference only; cycles end when the stock reaches target weight.</summary>
        public const int CycleLengthDays = 540;

        /// <summary>Default smolt release count per sea site (one commercial pen).</summary>
        public const int DefaultSmoltCount = 50_000;

        /// <summary>Release count for the high-stocking sensitivity scenario.</summary>
        public const int HighStockingSmoltCount = 100_000;

        /// <summary>Smolt count used by a scenario preset.</summary>
        public static int SmoltCountFor(SimScenario scenario) => scenario switch
        {
            SimScenario.HighStocking => HighStockingSmoltCount,
            _ => DefaultSmoltCount
        };

        /// <summary>
        /// Run one seeded simulation to the end of a 3-year horizon, harvesting
        /// and re-stocking each time the stock reaches the calibrated target
        /// harvest weight, and return the pooled completed-cycle metrics.
        /// Deterministic for a given (seed, scenario).
        /// </summary>
        public static SimMetrics Run(int seed, SimScenario scenario,
            int days = HorizonDays, int? smoltCount = null)
        {
            int count = smoltCount ?? SmoltCountFor(scenario);
            double targetWeight = LakskongenSim.Calibration.CalibrationRegistry.Default
                .Biology.TargetHarvestWeightKg;

            var engine = new SimulationEngine();
            engine.Initialize(enableEvents: false, seed: seed);

            // One commercial sea site. No zone on purpose (see class remarks).
            var site = new LakskongenSim.Farm.SeaSite($"{scenario}-site");
            engine.State.SeaSites.Clear();
            engine.State.SeaSites.Add(site);

            double totalInitialCount = 0;
            double totalFinalCount = 0;
            double totalInitialBiomass = 0;
            double totalFinalBiomass = 0;
            double totalFeedUsed = 0;
            double totalGrowth = 0;
            double peakLice = 0;
            double peakAdultFemale = 0;
            var harvestPrices = new List<double>();
            int completedCycles = 0;

            int day = 0;
            while (day < days)
            {
                engine.Hatchery.ReleaseSmolt(count);
                var population = engine.State.Populations.Last();
                site.Stock(population);

                double initialCount = population.Cohorts.Sum(c => c.Count);
                double initialBiomass = population.Cohorts.Sum(c => c.Count * c.MeanWeightKg);
                double feedUsed0 = engine.Feed.TotalFeedUsedKg;
                double growth0 = engine.Feed.TotalGrowthKg;

                bool completed = false;
                while (day < days)
                {
                    engine.Advance(1);
                    day++;

                    peakLice = Math.Max(peakLice, engine.SeaLice.AverageLicePerFish);
                    peakAdultFemale = Math.Max(peakAdultFemale, engine.SeaLice.AdultFemalePerFish);

                    double meanWeight = population.Cohorts.Count > 0
                        ? population.GetTotalBiomassKg() / Math.Max(population.TotalCount, 1)
                        : 0.0;
                    if (meanWeight >= targetWeight)
                    {
                        completed = true;
                        break;
                    }
                }

                if (!completed) break; // horizon cut the last cycle short

                double finalCount = population.Cohorts.Sum(c => c.Count);
                double finalBiomass = population.Cohorts.Sum(c => c.Count * c.MeanWeightKg);

                totalInitialCount += initialCount;
                totalFinalCount += finalCount;
                totalInitialBiomass += initialBiomass;
                totalFinalBiomass += finalBiomass;
                totalFeedUsed += engine.Feed.TotalFeedUsedKg - feedUsed0;
                totalGrowth += engine.Feed.TotalGrowthKg - growth0;
                completedCycles++;

                if (engine.MarketSimulation != null)
                    harvestPrices.Add((double)engine.MarketSimulation.PricePerKg);

                // Harvest and remove this cycle's population before re-stocking.
                site.Pens.Remove(population);
                engine.State.Populations.Remove(population);
            }

            double seaMortalityPct = totalInitialCount > 0
                ? 100.0 * (totalInitialCount - totalFinalCount) / totalInitialCount
                : 0.0;

            double meanHarvestWeightKg = totalFinalCount > 0 ? totalFinalBiomass / totalFinalCount : 0.0;

            // Registry-consistent definition: feed cost per kg produced =
            // price per kg feed x economic FCR (docs/BALANCE.md §1).
            double meanFcr = totalGrowth > 0 ? totalFeedUsed / totalGrowth : engine.Feed.LifetimeFCR;
            double feedCostPerKgProduced = (double)engine.Feed.FeedCostPerKg * meanFcr;

            double realizedPrice = harvestPrices.Count > 0
                ? Distribution.From(harvestPrices).Median
                : 0.0;

            double escapesThisYear = engine.State.SeaSites.Sum(s => s.EscapesThisYear);
            double escapesPerYear = escapesThisYear / (days / 365.0);

            return new SimMetrics(
                MeanFcr: meanFcr,
                SeaMortalityPercentPerCycle: seaMortalityPct,
                MeanHarvestWeightKg: meanHarvestWeightKg,
                FeedCostPerKgProducedNok: feedCostPerKgProduced,
                RealizedPriceNokPerKg: realizedPrice,
                PeakLicePerFish: peakLice,
                PeakAdultFemaleLicePerFish: peakAdultFemale,
                EscapesPerYear: escapesPerYear);
        }

        /// <summary>
        /// Run the seed sweep for one scenario (inclusive <paramref name="seedFrom"/>..<paramref name="seedTo"/>)
        /// and reduce every metric to a min/median/p90/mean distribution.
        /// </summary>
        public static ScenarioDistribution Measure(SimScenario scenario,
            int seedFrom = 1, int seedTo = 20,
            int days = HorizonDays, int? smoltCount = null)
        {
            var fcr = new List<double>();
            var mortality = new List<double>();
            var weight = new List<double>();
            var feedCost = new List<double>();
            var price = new List<double>();
            var lice = new List<double>();
            var escapes = new List<double>();

            for (int seed = seedFrom; seed <= seedTo; seed++)
            {
                var m = Run(seed, scenario, days, smoltCount);
                fcr.Add(m.MeanFcr);
                mortality.Add(m.SeaMortalityPercentPerCycle);
                weight.Add(m.MeanHarvestWeightKg);
                feedCost.Add(m.FeedCostPerKgProducedNok);
                price.Add(m.RealizedPriceNokPerKg);
                lice.Add(m.PeakAdultFemaleLicePerFish);
                escapes.Add(m.EscapesPerYear);
            }

            return new ScenarioDistribution(
                scenario, seedFrom, seedTo,
                Distribution.From(fcr),
                Distribution.From(mortality),
                Distribution.From(weight),
                Distribution.From(feedCost),
                Distribution.From(price),
                Distribution.From(lice),
                Distribution.From(escapes));
        }

        /// <summary>Compact multi-line report of one scenario's distributions.</summary>
        public static string Format(ScenarioDistribution d)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Scenario: {d.Scenario} (seeds {d.SeedFrom}-{d.SeedTo}, n={d.Runs}, {HorizonDays}-day horizon)");
            sb.AppendLine($"  {"metric",-36} {"min",9} / {"median",9} / {"p90",9}");
            sb.AppendLine($"  {"mean FCR",-36} {d.MeanFcr.Triple()}");
            sb.AppendLine($"  {"sea mortality %/cycle",-36} {d.SeaMortalityPercentPerCycle.Triple()}");
            sb.AppendLine($"  {"mean harvest weight kg",-36} {d.MeanHarvestWeightKg.Triple()}");
            sb.AppendLine($"  {"feed cost NOK/kg produced",-36} {d.FeedCostPerKgProducedNok.Triple()}");
            sb.AppendLine($"  {"realized price NOK/kg",-36} {d.RealizedPriceNokPerKg.Triple()}");
            sb.AppendLine($"  {"peak adult-female lice/fish",-36} {d.PeakLicePerFish.Triple()}");
            sb.AppendLine($"  {"escapes/year",-36} {d.EscapesPerYear.Triple()}");
            return sb.ToString();
        }
    }
}
