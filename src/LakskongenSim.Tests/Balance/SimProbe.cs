using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Calibration;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Health;

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
        HighStocking,

        /// <summary>Storyteller events on (disease subsystem live), shocks
        /// suppressed. Measures the additive disease-attributable mortality term
        /// over a grow-out cycle: the engine splits the sea anchor into a
        /// husbandry baseline plus an explicit SIR disease slice (v1.0.3).</summary>
        DiseaseStress,

        /// <summary>Seeded storms; the engine itself applies the net-integrity
        /// wear and records escapes through <see cref="SeaSite.RecordEscape"/>
        /// (v1.0.3). The probe only reads the live engine counters.</summary>
        Storm,

        /// <summary>Three commercial sea sites operated together for the full
        /// horizon; pooled multi-site P&amp;L (revenue, OPEX, EBIT/kg).</summary>
        ThreeSite,

        /// <summary>Proactive delousing policy (treat at the registry trigger);
        /// counts treatments per cycle and the resistance built within a cycle.</summary>
        LiceControl
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

    /// <summary>One seeded run's disease-cycle figures (pooled completed cycles).</summary>
    public sealed record DiseaseMetrics(
        double DiseaseMortalityPercentPerCycle,
        double TotalMortalityPercentPerCycle,
        int CompletedCycles);

    /// <summary>Disease distribution across seeds.</summary>
    public sealed record DiseaseDistribution(
        int SeedFrom, int SeedTo,
        Distribution DiseaseMortalityPercentPerCycle,
        Distribution TotalMortalityPercentPerCycle)
    {
        /// <summary>Number of seeded runs backing this distribution.</summary>
        public int Runs => SeedTo - SeedFrom + 1;
    }

    /// <summary>One seeded run's storm/escape figures, normalised per site per year.</summary>
    public sealed record StormMetrics(
        double EscapesPerSitePerYear,
        double EscapeEventsPerSitePerYear,
        double EscapesPercentOfStockPerYear,
        double StormDaysPerYear,
        double MinIntegrityPct);

    /// <summary>Storm/escape distribution across seeds.</summary>
    public sealed record StormDistribution(
        int SeedFrom, int SeedTo,
        Distribution EscapesPerSitePerYear,
        Distribution EscapeEventsPerSitePerYear,
        Distribution EscapesPercentOfStockPerYear,
        Distribution StormDaysPerYear,
        Distribution MinIntegrityPct)
    {
        /// <summary>Number of seeded runs backing this distribution.</summary>
        public int Runs => SeedTo - SeedFrom + 1;
    }

    /// <summary>One seeded 3-site session's pooled economics.</summary>
    public sealed record MultiSiteMetrics(
        double GrossRevenueNok,
        double TotalOpexNok,
        double EbitPerKgNok,
        double CostPerKgProducedNok,
        double ProfitPerSiteNok,
        double HarvestedKgPerSite,
        double RealizedPriceNokPerKg,
        int CompletedCycles);

    /// <summary>Multi-site economics distribution across seeds.</summary>
    public sealed record MultiSiteDistribution(
        int SeedFrom, int SeedTo,
        Distribution GrossRevenueNok,
        Distribution TotalOpexNok,
        Distribution EbitPerKgNok,
        Distribution CostPerKgProducedNok,
        Distribution ProfitPerSiteNok,
        Distribution HarvestedKgPerSite)
    {
        /// <summary>Number of seeded runs backing this distribution.</summary>
        public int Runs => SeedTo - SeedFrom + 1;
    }

    /// <summary>One seeded run's proactive delousing figures.</summary>
    public sealed record LiceControlMetrics(
        double TreatmentsPerCycle,
        double MaxResistancePercent,
        double PeakAdultFemaleLicePerFish);

    /// <summary>Lice-control distribution across seeds.</summary>
    public sealed record LiceControlDistribution(
        int SeedFrom, int SeedTo,
        Distribution TreatmentsPerCycle,
        Distribution MaxResistancePercent,
        Distribution PeakAdultFemaleLicePerFish)
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

        /// <summary>Number of sites in the multi-site economics scenario.</summary>
        public const int MultiSiteCount = 3;

        // ── Engine-driven storm escapes (v1.0.3) ────────────────────────────
        // Storm net-integrity wear and escape accounting now live in
        // SimulationEngine.ApplyStormHazards (the single source of truth); the
        // probe no longer applies a test-side model. It simply reads the engine's
        // NetIntegrityPct and escape counters. See docs/BALANCE.md §1c.

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
            double targetWeight = CalibrationRegistry.Default
                .Biology.TargetHarvestWeightKg;

            var engine = new SimulationEngine();
            engine.Initialize(enableEvents: false, seed: seed);

            // One commercial sea site. No zone on purpose (see class remarks).
            var site = new SeaSite($"{scenario}-site");
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

        // ─────────────────────────────────────────────────────────────────
        // Disease-cycle mortality
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Run one seeded disease-stress grow-out. Storyteller random shocks are
        /// suppressed so the only extra mortality versus the baseline is the
        /// disease subsystem itself; the disease LOD is forced high so the full
        /// stochastic path executes. Disease-attributable deaths are measured
        /// exactly from the injected per-population disease rate
        /// (<see cref="MortalityModel.GetSaveSnapshot"/>): for a one-day step
        /// survivors(with) - survivors(without) = count x diseaseRate.
        /// </summary>
        public static DiseaseMetrics RunDisease(int seed, int days = HorizonDays,
            int? smoltCount = null)
        {
            int count = smoltCount ?? DefaultSmoltCount;
            double targetWeight = CalibrationRegistry.Default.Biology.TargetHarvestWeightKg;

            var engine = new SimulationEngine();
            engine.Initialize(enableEvents: true, seed: seed);
            // Keep the disease subsystem live (enableEvents:true) but silence the
            // random storyteller shocks so the measured term is attributable to
            // disease rather than to storms/blooms/crashes.
            engine.Storyteller.BaseDailyChance = 0.0;
            engine.Storyteller.MaxDailyChance = 0.0;
            engine.IntegratedDisease.SetLODMode(LODMode.High);

            var site = new SeaSite("DiseaseStress-site");
            engine.State.SeaSites.Clear();
            engine.State.SeaSites.Add(site);

            double totalInitial = 0, totalFinal = 0, totalDiseaseDeaths = 0;
            int completedCycles = 0;
            int day = 0;

            while (day < days)
            {
                engine.Hatchery.ReleaseSmolt(count);
                var population = engine.State.Populations.Last();
                site.Stock(population);

                double initialCount = population.Cohorts.Sum(c => c.Count);
                bool completed = false;
                while (day < days)
                {
                    int before = population.Cohorts.Sum(c => c.Count);
                    engine.Advance(1);
                    day++;

                    var snapshot = engine.Mortality.GetSaveSnapshot();
                    if (snapshot.DiseaseRates.TryGetValue(population.Id, out double rate))
                        totalDiseaseDeaths += before * rate;

                    double meanWeight = population.Cohorts.Count > 0
                        ? population.GetTotalBiomassKg() / Math.Max(population.TotalCount, 1)
                        : 0.0;
                    if (meanWeight >= targetWeight) { completed = true; break; }
                }

                if (!completed) break;

                totalInitial += initialCount;
                totalFinal += population.Cohorts.Sum(c => c.Count);
                completedCycles++;

                site.Pens.Remove(population);
                engine.State.Populations.Remove(population);
            }

            double diseasePct = totalInitial > 0 ? 100.0 * totalDiseaseDeaths / totalInitial : 0.0;
            double totalPct = totalInitial > 0 ? 100.0 * (totalInitial - totalFinal) / totalInitial : 0.0;
            return new DiseaseMetrics(diseasePct, totalPct, completedCycles);
        }

        /// <summary>Sweep seeds and reduce the disease-cycle metrics.</summary>
        public static DiseaseDistribution MeasureDisease(int seedFrom = 1, int seedTo = 20,
            int days = HorizonDays)
        {
            var disease = new List<double>();
            var total = new List<double>();
            for (int seed = seedFrom; seed <= seedTo; seed++)
            {
                var m = RunDisease(seed, days);
                disease.Add(m.DiseaseMortalityPercentPerCycle);
                total.Add(m.TotalMortalityPercentPerCycle);
            }
            return new DiseaseDistribution(seedFrom, seedTo,
                Distribution.From(disease), Distribution.From(total));
        }

        // ─────────────────────────────────────────────────────────────────
        // Storm escapes
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Run one seeded storm exposure. Storms come from the seeded weather
        /// system; the engine itself applies the net-integrity wear and records
        /// escapes through <see cref="SeaSite.RecordEscape"/> (v1.0.3). The probe
        /// only reads the engine's live <see cref="SeaSite.NetIntegrityPct"/>,
        /// the accumulated site escapes and the engine's storm-event counter, so
        /// there is a single source of truth for storm escapes.
        /// </summary>
        public static StormMetrics RunStorm(int seed, int days = HorizonDays)
        {
            double targetWeight = CalibrationRegistry.Default.Biology.TargetHarvestWeightKg;
            var engine = new SimulationEngine();
            // enableEvents:true turns on the engine's storm hazards (gated with the
            // disease/event subsystem). Storyteller random shocks are silenced so
            // the measured escapes are attributable to storms, not events.
            engine.Initialize(enableEvents: true, seed: seed);
            engine.Storyteller.BaseDailyChance = 0.0;
            engine.Storyteller.MaxDailyChance = 0.0;

            var site = new SeaSite("Storm-site");
            engine.State.SeaSites.Clear();
            engine.State.SeaSites.Add(site);

            double minIntegrity = 100.0;
            int stormDays = 0;
            int day = 0;
            int cycle = 0;

            while (day < days)
            {
                cycle++;
                engine.Hatchery.ReleaseSmolt(DefaultSmoltCount);
                var population = engine.State.Populations.Last();
                site.Stock(population);

                bool completed = false;
                while (day < days)
                {
                    engine.Advance(1);
                    day++;

                    if (engine.Weather.CurrentState >= WeatherState.Storm)
                        stormDays++;
                    minIntegrity = Math.Min(minIntegrity, site.NetIntegrityPct);

                    double meanWeight = population.Cohorts.Count > 0
                        ? population.GetTotalBiomassKg() / Math.Max(population.TotalCount, 1)
                        : 0.0;
                    if (meanWeight >= targetWeight) { completed = true; break; }
                }

                if (!completed) break;
                site.Pens.Remove(population);
                engine.State.Populations.Remove(population);
            }

            double years = days / 365.0;
            double stockedPerYear = (double)DefaultSmoltCount * cycle / years;
            double escapes = site.EscapesThisYear; // cumulative over the horizon
            return new StormMetrics(
                EscapesPerSitePerYear: escapes / years,
                EscapeEventsPerSitePerYear: engine.StormEscapeEvents / years,
                EscapesPercentOfStockPerYear: stockedPerYear > 0 ? 100.0 * escapes / stockedPerYear : 0.0,
                StormDaysPerYear: stormDays / years,
                MinIntegrityPct: minIntegrity);
        }

        /// <summary>Sweep seeds and reduce the storm/escape metrics.</summary>
        public static StormDistribution MeasureStorm(int seedFrom = 1, int seedTo = 20,
            int days = HorizonDays)
        {
            var escapes = new List<double>();
            var events = new List<double>();
            var pct = new List<double>();
            var storms = new List<double>();
            var integrity = new List<double>();
            for (int seed = seedFrom; seed <= seedTo; seed++)
            {
                var m = RunStorm(seed, days);
                escapes.Add(m.EscapesPerSitePerYear);
                events.Add(m.EscapeEventsPerSitePerYear);
                pct.Add(m.EscapesPercentOfStockPerYear);
                storms.Add(m.StormDaysPerYear);
                integrity.Add(m.MinIntegrityPct);
            }
            return new StormDistribution(seedFrom, seedTo,
                Distribution.From(escapes), Distribution.From(events),
                Distribution.From(pct), Distribution.From(storms),
                Distribution.From(integrity));
        }

        // ─────────────────────────────────────────────────────────────────
        // Multi-site economics
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Run one seeded 3-site session for the whole horizon and return the
        /// pooled P&amp;L. OPEX uses the registry-consistent feed-cost definition
        /// (price x economic FCR, docs/BALANCE.md §1) plus the calibrated smolt,
        /// labour, maintenance, harvest and processing costs, so the reported
        /// EBIT/kg is comparable to the SalMar/Mowi cyclical 8-25 NOK/kg band.
        /// </summary>
        public static MultiSiteMetrics RunThreeSite(int seed, int siteCount = MultiSiteCount,
            int days = HorizonDays)
        {
            var C = CalibrationRegistry.Default;
            double targetWeight = C.Biology.TargetHarvestWeightKg;

            var engine = new SimulationEngine();
            engine.Initialize(enableEvents: false, seed: seed);
            engine.State.SeaSites.Clear();

            var sites = new List<SeaSite>();
            var pops = new FishPopulation?[siteCount];
            int totalReleased = 0;
            for (int i = 0; i < siteCount; i++)
            {
                var s = new SeaSite($"ThreeSite-{i}");
                sites.Add(s);
                engine.State.SeaSites.Add(s);
                engine.Hatchery.ReleaseSmolt(DefaultSmoltCount);
                var p = engine.State.Populations.Last();
                s.Stock(p);
                pops[i] = p;
                totalReleased += DefaultSmoltCount;
            }

            var prices = new List<double>();
            double harvestedKg = 0, revenue = 0;
            int completedCycles = 0;
            int day = 0;

            while (day < days)
            {
                engine.Advance(1);
                day++;

                for (int i = 0; i < siteCount; i++)
                {
                    var population = pops[i];
                    if (population == null) continue;

                    double meanWeight = population.Cohorts.Count > 0
                        ? population.GetTotalBiomassKg() / Math.Max(population.TotalCount, 1)
                        : 0.0;
                    if (meanWeight < targetWeight) continue;

                    double kg = population.GetTotalBiomassKg();
                    double price = (double)engine.MarketSimulation.PricePerKg;
                    harvestedKg += kg;
                    revenue += kg * price;
                    prices.Add(price);
                    completedCycles++;

                    sites[i].Pens.Remove(population);
                    engine.State.Populations.Remove(population);

                    engine.Hatchery.ReleaseSmolt(DefaultSmoltCount);
                    var next = engine.State.Populations.Last();
                    sites[i].Stock(next);
                    pops[i] = next;
                    totalReleased += DefaultSmoltCount;
                }
            }

            // Registry-consistent feed cost (price x economic FCR applied to
            // harvested biomass; docs/BALANCE.md §5 explains why the raw cash
            // feed ledger is not used for the EBIT/kg comparison). This is a
            // farm-gate P&L: harvest is included but downstream processing is
            // not, matching how SalMar/Mowi report farming-segment EBIT/kg.
            double feedCost = harvestedKg * (double)C.FeedCostPerKgProducedNOK;
            double smoltCost = totalReleased * (double)C.Economy.SmoltCostPerFishNOK;
            double laborCost = siteCount * C.Labor.WorkersPerSite
                * (double)C.Labor.WorkerMonthlySalaryNOK * (days / 30.0);
            double maintenanceCost = siteCount * (double)C.Infrastructure.SeaSiteBuildCostNOK * 0.0001 * days;
            double harvestCost = harvestedKg * (double)C.Operations.HarvestCostPerKgNOK;

            double totalOpex = feedCost + smoltCost + laborCost + maintenanceCost + harvestCost;
            double ebit = revenue - totalOpex;
            double realizedPrice = prices.Count > 0 ? Distribution.From(prices).Median : 0.0;

            return new MultiSiteMetrics(
                GrossRevenueNok: revenue,
                TotalOpexNok: totalOpex,
                EbitPerKgNok: harvestedKg > 0 ? ebit / harvestedKg : 0.0,
                CostPerKgProducedNok: harvestedKg > 0 ? totalOpex / harvestedKg : 0.0,
                ProfitPerSiteNok: ebit / siteCount,
                HarvestedKgPerSite: harvestedKg / siteCount,
                RealizedPriceNokPerKg: realizedPrice,
                CompletedCycles: completedCycles);
        }

        /// <summary>Sweep seeds and reduce the multi-site economics.</summary>
        public static MultiSiteDistribution MeasureThreeSite(int seedFrom = 1, int seedTo = 20,
            int days = HorizonDays)
        {
            var revenue = new List<double>();
            var opex = new List<double>();
            var ebitPerKg = new List<double>();
            var costPerKg = new List<double>();
            var profitPerSite = new List<double>();
            var harvestPerSite = new List<double>();
            for (int seed = seedFrom; seed <= seedTo; seed++)
            {
                var m = RunThreeSite(seed, days: days);
                revenue.Add(m.GrossRevenueNok);
                opex.Add(m.TotalOpexNok);
                ebitPerKg.Add(m.EbitPerKgNok);
                costPerKg.Add(m.CostPerKgProducedNok);
                profitPerSite.Add(m.ProfitPerSiteNok);
                harvestPerSite.Add(m.HarvestedKgPerSite);
            }
            return new MultiSiteDistribution(seedFrom, seedTo,
                Distribution.From(revenue), Distribution.From(opex),
                Distribution.From(ebitPerKg), Distribution.From(costPerKg),
                Distribution.From(profitPerSite), Distribution.From(harvestPerSite));
        }

        // ─────────────────────────────────────────────────────────────────
        // Lice control (treatments + resistance)
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Run one seeded grow-out with a proactive delousing policy: treat with
        /// a pyrethroid + a mechanical pass whenever adult-female lice reach the
        /// calibrated trigger (<see cref="HealthCalibration.SeaLiceTreatmentThreshold"/>).
        /// Counts treatments per completed cycle and the resistance reached
        /// within a cycle (the engine resets the lice model at each sea stocking,
        /// so resistance is a per-cycle quantity).
        /// </summary>
        public static LiceControlMetrics RunLiceControl(int seed, int days = HorizonDays)
        {
            var C = CalibrationRegistry.Default;
            double targetWeight = C.Biology.TargetHarvestWeightKg;
            double trigger = C.Health.SeaLiceTreatmentThreshold;

            var engine = new SimulationEngine();
            engine.Initialize(enableEvents: false, seed: seed);

            var site = new SeaSite("LiceControl-site");
            engine.State.SeaSites.Clear();
            engine.State.SeaSites.Add(site);

            int treatments = 0;
            int completedCycles = 0;
            double maxResistance = 0;
            double peakAdultFemale = 0;
            int day = 0;

            while (day < days)
            {
                engine.Hatchery.ReleaseSmolt(DefaultSmoltCount);
                var population = engine.State.Populations.Last();
                site.Stock(population);

                bool completed = false;
                while (day < days)
                {
                    if (engine.SeaLice.AdultFemalePerFish >= trigger)
                    {
                        engine.SeaLice.ApplyTreatment("pyrethroid");
                        engine.SeaLice.ApplyTreatment("mechanical");
                        treatments++;
                    }

                    engine.Advance(1);
                    day++;

                    maxResistance = Math.Max(maxResistance,
                        Math.Max(engine.SeaLice.PyrethroidResistance, engine.SeaLice.MechanicalResistance));
                    peakAdultFemale = Math.Max(peakAdultFemale, engine.SeaLice.AdultFemalePerFish);

                    double meanWeight = population.Cohorts.Count > 0
                        ? population.GetTotalBiomassKg() / Math.Max(population.TotalCount, 1)
                        : 0.0;
                    if (meanWeight >= targetWeight) { completed = true; break; }
                }

                if (!completed) break;
                completedCycles++;
                site.Pens.Remove(population);
                engine.State.Populations.Remove(population);
            }

            return new LiceControlMetrics(
                TreatmentsPerCycle: completedCycles > 0 ? (double)treatments / completedCycles : 0.0,
                MaxResistancePercent: maxResistance * 100.0,
                PeakAdultFemaleLicePerFish: peakAdultFemale);
        }

        /// <summary>Sweep seeds and reduce the lice-control metrics.</summary>
        public static LiceControlDistribution MeasureLiceControl(int seedFrom = 1, int seedTo = 20,
            int days = HorizonDays)
        {
            var treatments = new List<double>();
            var resistance = new List<double>();
            var peak = new List<double>();
            for (int seed = seedFrom; seed <= seedTo; seed++)
            {
                var m = RunLiceControl(seed, days);
                treatments.Add(m.TreatmentsPerCycle);
                resistance.Add(m.MaxResistancePercent);
                peak.Add(m.PeakAdultFemaleLicePerFish);
            }
            return new LiceControlDistribution(seedFrom, seedTo,
                Distribution.From(treatments), Distribution.From(resistance), Distribution.From(peak));
        }

        // ─────────────────────────────────────────────────────────────────
        // Formatting
        // ─────────────────────────────────────────────────────────────────

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

        /// <summary>Compact report of the disease-cycle distribution.</summary>
        public static string Format(DiseaseDistribution d)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Scenario: DiseaseStress (seeds {d.SeedFrom}-{d.SeedTo}, n={d.Runs}, {HorizonDays}-day horizon)");
            sb.AppendLine($"  {"metric",-36} {"min",9} / {"median",9} / {"p90",9}");
            sb.AppendLine($"  {"disease mortality %/cycle",-36} {d.DiseaseMortalityPercentPerCycle.Triple()}");
            sb.AppendLine($"  {"total sea mortality %/cycle",-36} {d.TotalMortalityPercentPerCycle.Triple()}");
            return sb.ToString();
        }

        /// <summary>Compact report of the storm/escape distribution.</summary>
        public static string Format(StormDistribution d)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Scenario: Storm (seeds {d.SeedFrom}-{d.SeedTo}, n={d.Runs}, {HorizonDays}-day horizon)");
            sb.AppendLine($"  {"metric",-36} {"min",9} / {"median",9} / {"p90",9}");
            sb.AppendLine($"  {"escapes/site/year",-36} {d.EscapesPerSitePerYear.Triple()}");
            sb.AppendLine($"  {"escape events/site/year",-36} {d.EscapeEventsPerSitePerYear.Triple()}");
            sb.AppendLine($"  {"escapes % of stock/year",-36} {d.EscapesPercentOfStockPerYear.Triple()}");
            sb.AppendLine($"  {"storm days/year",-36} {d.StormDaysPerYear.Triple()}");
            sb.AppendLine($"  {"min net integrity %",-36} {d.MinIntegrityPct.Triple()}");
            return sb.ToString();
        }

        /// <summary>Compact report of the multi-site economics distribution.</summary>
        public static string Format(MultiSiteDistribution d)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Scenario: ThreeSite (seeds {d.SeedFrom}-{d.SeedTo}, n={d.Runs}, {HorizonDays}-day horizon)");
            sb.AppendLine($"  {"metric",-36} {"min",9} / {"median",9} / {"p90",9}");
            sb.AppendLine($"  {"gross revenue NOK",-36} {d.GrossRevenueNok.Triple()}");
            sb.AppendLine($"  {"total OPEX NOK",-36} {d.TotalOpexNok.Triple()}");
            sb.AppendLine($"  {"EBIT NOK/kg",-36} {d.EbitPerKgNok.Triple()}");
            sb.AppendLine($"  {"cost NOK/kg produced",-36} {d.CostPerKgProducedNok.Triple()}");
            sb.AppendLine($"  {"profit NOK/site",-36} {d.ProfitPerSiteNok.Triple()}");
            sb.AppendLine($"  {"harvested kg/site",-36} {d.HarvestedKgPerSite.Triple()}");
            return sb.ToString();
        }

        /// <summary>Compact report of the lice-control distribution.</summary>
        public static string Format(LiceControlDistribution d)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Scenario: LiceControl (seeds {d.SeedFrom}-{d.SeedTo}, n={d.Runs}, {HorizonDays}-day horizon)");
            sb.AppendLine($"  {"metric",-36} {"min",9} / {"median",9} / {"p90",9}");
            sb.AppendLine($"  {"treatments/cycle",-36} {d.TreatmentsPerCycle.Triple()}");
            sb.AppendLine($"  {"max resistance % (per cycle)",-36} {d.MaxResistancePercent.Triple()}");
            sb.AppendLine($"  {"peak adult-female lice/fish",-36} {d.PeakAdultFemaleLicePerFish.Triple()}");
            return sb.ToString();
        }
    }
}
