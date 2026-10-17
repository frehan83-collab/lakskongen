#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Calibration;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using LakskongenSim.Harvest;
using LakskongenSim.Market;
using LakskongenSim.Tests.Integration;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Harvest
{
    /// <summary>
    /// Roadmap U2-05, the harvest planner on the engine path: the same-day
    /// quote is what an express harvest books (contracts and the processing
    /// plant's premium included); the planned harvest's projection is the
    /// expectation of what the order then realizes (acceptance 2); "wait N
    /// weeks" is twin-lite and leaves the live game and its RNG untouched
    /// (acceptance 3).
    /// </summary>
    public class HarvestPlannerTests
    {
        private static readonly HarvestCalibration Cal = CalibrationRegistry.Default.Harvest;
        private readonly ITestOutputHelper _out;

        public HarvestPlannerTests(ITestOutputHelper output) => _out = output;

        private static SimulationEngine NewEngine(int seed, bool events = false)
        {
            var engine = new SimulationEngine { FinanceManager = new FinanceManager(500_000_000m) };
            engine.Initialize(enableEvents: events, seed: seed);
            engine.State.SeaSites.Clear();
            engine.State.HomeProductionArea = 4;
            return engine;
        }

        /// <summary>A site with no zone, in PO4 water (no blooms, only the sea's temperature).</summary>
        private static SeaSite AddSite(SimulationEngine engine, string id)
        {
            var site = new SeaSite(id) { MABTonnes = 10_000 };
            engine.State.SeaSites.Add(site);
            return site;
        }

        private static FishPopulation AddPen(SimulationEngine engine, SeaSite site, int fish, double kg, double cv = 0.20)
        {
            var pen = new FishPopulation { ZoneId = site.ZoneId };
            pen.InjectDependencies(engine.Growth, engine.Mortality);
            pen.Cohorts.Add(new FishPopulation.Cohort(500, fish, kg, LifeStage.Seawater) { WeightCv = cv });
            site.Stock(pen);
            engine.State.Populations.Add(pen);
            return pen;
        }

        private static SimulationEngine Started(string scenario, int seed, bool events)
        {
            var engine = new SimulationEngine { FinanceManager = new FinanceManager(50_000_000m) };
            engine.Initialize(enableEvents: events, seed: seed);
            Assert.True(engine.StartScenario(scenario).Ok);
            engine.State.RandomSeed = seed;
            return engine;
        }

        /// <summary>What an order realized: its reports summed.</summary>
        private sealed record Realized(int Fish, double HogKg, decimal Gross, decimal Net, int FirstDay, int LastDay);

        private static Realized RunOrder(SimulationEngine engine, string holdingId, IReadOnlyCollection<string>? pens = null,
            double fraction = 1.0)
        {
            var order = engine.OrderHarvest(holdingId, pens, fraction);
            Assert.True(order.Ok, order.Message);
            for (int d = 0; d < 200 && engine.HarvestOrders.Any(o => o.Id == order.Id); d++) engine.Advance(1);
            Assert.DoesNotContain(engine.HarvestOrders, o => o.Id == order.Id);
            var reports = engine.HarvestReports.Where(r => r.OrderId == order.Id).ToList();
            Assert.NotEmpty(reports);
            return new Realized(reports.Sum(r => r.Fish), reports.Sum(r => r.HogKg), reports.Sum(r => r.GrossRevenueNok),
                reports.Sum(r => r.NetRevenueNok), reports.Min(r => r.Day), reports.Max(r => r.Day));
        }

        /// <summary>A save with its process-random entity ids replaced (two games of one seed compare equal).</summary>
        private static string Normalized(SimulationEngine engine) => SaveStateComparer.Normalize(SaveStateComparer.Serialize(engine));

        private static double Error(decimal projected, decimal realized) => (double)(projected / realized) - 1.0;

        private static double Median(IEnumerable<double> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            return sorted[sorted.Count / 2];
        }

        // ─── The same-day quote ──────────────────────────────────────────────

        /// <summary>
        /// The planner's "Ekspress i dag" figure is what an express harvest
        /// books, to the øre: the contracts take their kilos first at their
        /// prices, the rest goes at today's spot, the processing plant's premium
        /// on top (S2-04's quote priced every kilo at spot without the premium).
        /// </summary>
        [Theory]
        [InlineData(61, false)]
        [InlineData(62, true)]
        public void TheSameDayQuote_IsWhatAnExpressHarvestBooks_ContractsAndThePlantIncluded(int seed, bool plant)
        {
            var engine = NewEngine(seed);
            var site = AddSite(engine, "a");
            AddPen(engine, site, 12_000, 4.9);
            AddPen(engine, site, 9_000, 5.6);
            engine.State.OwnsProcessingPlant = plant;
            var cs = engine.ContractSystem;
            cs.AcceptOffer(cs.ProposeContract("Kjøper A", ContractType.Forward, 95m, 20_000));
            cs.AcceptOffer(cs.ProposeContract("Kjøper B", ContractType.Forward, 70m, 15_000));
            string before = SaveStateComparer.Serialize(engine);

            var quote = engine.QuoteHarvest(site.Id, express: true);

            Assert.Equal("identical", SaveStateComparer.FirstDifference(before, SaveStateComparer.Serialize(engine)));
            Assert.True(engine.HarvestNow(site.Id).Ok);
            var report = engine.HarvestReports[^1];
            Assert.Equal(35_000.0, quote.ContractHogKg);
            Assert.Equal(report.ContractHogKg, quote.ContractHogKg);
            Assert.Equal(report.GrossRevenueNok, quote.GrossRevenueNok);
            Assert.Equal(report.NetRevenueNok, quote.NetRevenueNok);
            Assert.Equal(report.VapMultiplier, quote.VapMultiplier);
            Assert.Equal(plant ? 1.12m : 1m, quote.VapMultiplier);
            Assert.Equal(quote.ContractRevenueNok * quote.VapMultiplier, report.ContractRevenueNok);
        }

        // ─── Acceptance 2: the planned harvest's projection ───────────────────

        /// <summary>One projected order against what it realized.</summary>
        private sealed record PlannedError(string Case, double Gross, double Net, double Hog, double ValueKg, int DayShift);

        /// <summary>
        /// Measured against the order it projects (wait 0 = "Bestill slakting"
        /// today; a wait = advance that many days, then order). Gross and net
        /// compare the sale; HOG the gutted kilos; ValueKg the kilos the price
        /// is paid on (HOG × size class × grade: each day's sale over its
        /// reference price), the projection's physical side without the
        /// market. Prints each case so docs/BALANCE.md can be re-measured.
        /// </summary>
        private List<PlannedError> PlannedErrors(Func<int, (SimulationEngine Engine, string Site)?> start,
            IEnumerable<int> seeds, int waitDays = 0)
        {
            var errors = new List<PlannedError>();
            foreach (int seed in seeds)
            {
                if (start(seed) is not var (engine, siteId)) continue;
                var projection = engine.ProjectHarvest(siteId, null, 1.0, waitDays);
                Assert.True(projection.Ok, projection.Message);
                if (waitDays > 0) engine.Advance(waitDays);
                int reports0 = engine.HarvestReports.Count;
                var real = RunOrder(engine, siteId);
                var batches = engine.HarvestReports.Skip(reports0).Where(r => r.HoldingId == siteId).ToList();
                double realValueKg = batches.Sum(r => (double)(r.GrossRevenueNok / r.ReferencePriceNok));
                double projectedValueKg = projection.Days.Sum(d => (double)(d.GrossRevenueNok / d.ReferencePriceNok));
                var e = new PlannedError($"seed {seed} day {projection.OrderDay}", Error(projection.GrossRevenueNok, real.Gross),
                    Error(projection.NetRevenueNok, real.Net), projection.HogKg / real.HogKg - 1.0,
                    projectedValueKg / realValueKg - 1.0, projection.LastSlaughterDay - real.LastDay);
                _out.WriteLine($"{e.Case}: gross {e.Gross:P2}, net {e.Net:P2}, HOG {e.Hog:P2}, value kg {e.ValueKg:P2}, " +
                    $"last day {e.DayShift:+0;-0;0} ({projection.Days.Count} days, {real.Fish:N0} fish)");
                errors.Add(e);
            }
            return errors;
        }

        /// <summary>
        /// The projection's physical side is the order's: with nothing but the
        /// sea's temperature random (a zone-less site, the market's noise off),
        /// the gutted kilos and the kilos the price is paid on (size classes,
        /// grades, the fast's weight loss, the expected deaths) are within 1 %
        /// for every seed, and the last slaughter day within a day; the sale
        /// within 2 % in all but the cases where the certification level moved
        /// the realized price during the harvest (the engine reprices on a
        /// certification change, which the projection does not foresee).
        /// </summary>
        [Fact]
        public void ThePlannedProjection_IsTheOrder_WhereOnlyTheSeaIsRandom()
        {
            var errors = PlannedErrors(seed =>
            {
                var engine = NewEngine(seed);
                engine.MarketSimulation.VolatilityScale = 0.0;
                var site = AddSite(engine, "a");
                for (int i = 0; i < 4; i++) AddPen(engine, site, 60_000, 4.6 + 0.3 * i);
                engine.Advance(seed * 9 % 200);
                return (engine, site.Id);
            }, Enumerable.Range(1, 12));
            Assert.Equal(12, errors.Count);
            Assert.All(errors, e => Assert.InRange(e.Hog, -0.01, 0.01));
            Assert.All(errors, e => Assert.InRange(e.ValueKg, -0.01, 0.01));
            Assert.All(errors, e => Assert.InRange(e.DayShift, -1, 1));
            Assert.True(errors.Count(e => Math.Abs(e.Gross) <= 0.02) >= 11,
                string.Join("; ", errors.Where(e => Math.Abs(e.Gross) > 0.02).Select(e => $"{e.Case} {e.Gross:P2}")));
        }

        /// <summary>
        /// Roadmap U2-05 acceptance 2 on the player path (the family farm and
        /// the PO3 red-zone farm, whose bigger sites take several slaughter
        /// days; 30 seeds; events, the market's noise and its shocks on): the
        /// same-day projection of the planned harvest is within ±5 % of the
        /// realized revenue in at least 80 % of the cases, 3 % at the median,
        /// and unbiased (mean error within ±2 %); its gutted kilos are within
        /// ±3 % in at least 80 % of them. A case can miss by more: the spot
        /// moves about 2 NOK a day between the order and the slaughter, and an
        /// event (a jellyfish bloom, an outbreak) kills fish while they fast;
        /// the projection takes the expectation of both.
        /// </summary>
        [Fact]
        public void ThePlannedProjection_IsWithinFivePercentOfTheRealizedRevenue_OnThePlayerPath()
        {
            (SimulationEngine, string)? AtHarvestWeight(string scenario, int seed)
            {
                var engine = Started(scenario, seed, events: true);
                for (int d = 0; d < 500; d++)
                {
                    foreach (var site in engine.State.SeaSites)
                    {
                        double fish = site.Pens.Sum(p => p.TotalCount);
                        if (fish > 0 && site.Pens.Sum(p => p.GetTotalBiomassKg()) / fish >= 4.5) return (engine, site.Id);
                    }
                    engine.Advance(1);
                }
                return null;
            }
            var errors = PlannedErrors(seed => AtHarvestWeight("forste_sesong", seed), Enumerable.Range(1, 20));
            errors.AddRange(PlannedErrors(seed => AtHarvestWeight("po3_red_sone", seed), Enumerable.Range(1, 10)));

            Assert.True(errors.Count >= 27, $"{errors.Count} cases");
            double within = errors.Count(e => Math.Abs(e.Gross) <= 0.05) / (double)errors.Count;
            double median = Median(errors.Select(e => Math.Abs(e.Gross)));
            _out.WriteLine($"gross within ±5 %: {within:P0} of {errors.Count}, median {median:P2}, mean {errors.Average(e => e.Gross):P2}; " +
                $"HOG within ±3 %: {errors.Count(e => Math.Abs(e.Hog) <= 0.03)}");
            Assert.True(within >= 0.80, $"{within:P0} of {errors.Count} cases within ±5 %");
            Assert.True(median <= 0.03, $"median error {median:P2}");
            Assert.InRange(errors.Average(e => e.Gross), -0.02, 0.02);
            Assert.True(errors.Count(e => Math.Abs(e.Hog) <= 0.03) >= errors.Count * 0.8,
                string.Join("; ", errors.Where(e => Math.Abs(e.Hog) > 0.03).Select(e => $"{e.Case} {e.Hog:P2}")));
        }

        /// <summary>
        /// The market's expected path is its daily step without the noise: on a
        /// bare market with its own volatility at 0, the expected price is the
        /// realized one exactly until the first weekly tick, including the
        /// pressure of the player's own sales and its fading; after it, the
        /// rivals' weekly volumes (each ±5 % at random around the season) move
        /// the supply index, and the path stays within 1 % for 60 days, 0.3 %
        /// on average.
        /// </summary>
        [Fact]
        public void TheExpectedPricePath_IsTheMarketsStepWithoutItsNoise()
        {
            var engine = NewEngine(81);
            var market = engine.MarketSimulation;
            market.VolatilityScale = 0.0;
            var sold = new double[60];
            sold[2] = 150.0;
            sold[3] = 150.0;
            sold[30] = 400.0;
            var expected = market.ExpectedPrices(60, sold);
            var ratios = new List<double>();
            for (int i = 0; i < 60; i++)
            {
                market.AdvanceDays(1);
                double ratio = (double)(expected[i] / market.PricePerKg);
                ratios.Add(ratio);
                _out.WriteLine($"day {i}: expected {expected[i]:F3}, realized {market.PricePerKg:F3}");
                if (i < 5) Assert.Equal(1.0, ratio, 9);
                if (sold[i] > 0.0) market.ApplyPlayerHarvest(sold[i]);
            }
            Assert.All(ratios, r => Assert.InRange(r, 0.99, 1.01));
            Assert.InRange(ratios.Average(), 0.997, 1.003);
            Assert.True(expected[3] < expected[2], "the second day's sale is priced under the first's pressure");
        }

        /// <summary>
        /// Spot against contract: the projection fills the contracts the order
        /// will fill, oldest first at their prices, and skips one that runs out
        /// before the slaughter starts (the weekly expiry tick removes it), so
        /// its contract kilos and contract sales equal the reports'.
        /// </summary>
        [Fact]
        public void ThePlannedProjection_FillsTheContractsTheOrderWillFill()
        {
            var engine = NewEngine(83);
            engine.MarketSimulation.VolatilityScale = 0.0;
            var site = AddSite(engine, "a");
            for (int i = 0; i < 4; i++) AddPen(engine, site, 60_000, 5.0);
            engine.Advance(10);
            var cs = engine.ContractSystem;
            cs.AcceptOffer(cs.ProposeContract("Kjøper A", ContractType.Forward, 96m, 300_000) with { DurationWeeks = 8 });
            cs.AcceptOffer(cs.ProposeContract("Kjøper B", ContractType.Forward, 99m, 50_000) with { DurationWeeks = 0 });
            cs.AcceptOffer(cs.ProposeContract("Kjøper C", ContractType.Forward, 74m, 100_000) with { DurationWeeks = 8 });

            var projection = engine.ProjectHarvest(site.Id);
            int reports0 = engine.HarvestReports.Count;
            var real = RunOrder(engine, site.Id);
            var batches = engine.HarvestReports.Skip(reports0).ToList();

            Assert.True(projection.Days.Count >= 3, $"{projection.Days.Count} slaughter days");
            Assert.Equal(batches.Sum(r => r.ContractHogKg), projection.ContractHogKg, 0);
            Assert.Equal(400_000.0, projection.ContractHogKg, 0); // A and C; B ran out first
            Assert.InRange(Error(projection.ContractRevenueNok, batches.Sum(r => r.ContractRevenueNok)), -0.01, 0.01);
            Assert.InRange(projection.SpotHogKg / (real.HogKg - batches.Sum(r => r.ContractHogKg)), 0.99, 1.01);
        }

        // ─── Acceptance 3: wait N weeks ───────────────────────────────────────

        /// <summary>
        /// Waiting is projected on the twin: two, four and eight weeks of
        /// growth ahead, the gutted kilos and the sale are within ±5 % of what
        /// waiting and then ordering realizes (a zone-less site, the market's
        /// noise off, as above), and the feed eaten while waiting is counted.
        /// </summary>
        [Theory]
        [InlineData(14)]
        [InlineData(28)]
        [InlineData(56)]
        public void WaitingNWeeks_IsProjectedOnTheTwin_AndMatchesWaitingThenOrdering(int wait)
        {
            var errors = PlannedErrors(seed =>
            {
                var engine = NewEngine(seed);
                engine.MarketSimulation.VolatilityScale = 0.0;
                var site = AddSite(engine, "a");
                for (int i = 0; i < 3; i++) AddPen(engine, site, 50_000, 3.8 + 0.3 * i);
                engine.Advance(seed * 11 % 240);
                return (engine, site.Id);
            }, Enumerable.Range(1, 10), wait);
            Assert.Equal(10, errors.Count);
            Assert.All(errors, e => Assert.InRange(e.Hog, -0.05, 0.05));
            Assert.All(errors, e => Assert.InRange(e.ValueKg, -0.05, 0.05));
            Assert.True(errors.Count(e => Math.Abs(e.Gross) <= 0.05) >= 9,
                string.Join("; ", errors.Where(e => Math.Abs(e.Gross) > 0.05).Select(e => $"{e.Case} {e.Gross:P2}")));
        }

        [Fact]
        public void WaitingGrowsTheFish_AndCostsTheFeedTheyEat()
        {
            var engine = NewEngine(71);
            var site = AddSite(engine, "a");
            AddPen(engine, site, 50_000, 4.0);
            engine.Advance(120); // early May: the water warms
            var now = engine.ProjectHarvest(site.Id);
            var later = engine.ProjectHarvest(site.Id, null, 1.0, 28);
            Assert.True(now.Ok && later.Ok);
            Assert.True(later.MeanLiveWeightKg > now.MeanLiveWeightKg + 0.2,
                $"{later.MeanLiveWeightKg:F2} kg after four weeks vs {now.MeanLiveWeightKg:F2} kg now");
            Assert.True(later.HogKg > now.HogKg);
            Assert.True(later.FeedCostNok > now.FeedCostNok * 5m && later.FeedKg > 0.0);
            Assert.Equal(later.NetRevenueNok - later.FeedCostNok, later.NetAfterFeedNok);
            Assert.Equal(engine.State.Day + 28, later.OrderDay);
            Assert.True(later.SlotDay >= later.OrderDay + Cal.WellboatLeadDays);
        }

        /// <summary>
        /// Acceptance 3, the determinism half: projecting every wait the planner
        /// offers, every day, changes nothing in the save, draws nothing from any
        /// RNG stream, and a game that projects every day stays identical to its
        /// twin that never does.
        /// </summary>
        [Fact]
        public void TheProjection_LeavesTheLiveGameAndItsRngUntouched()
        {
            SimulationEngine Game()
            {
                var engine = Started("po3_red_sone", 17, events: true);
                engine.Advance(150);
                return engine;
            }
            var probed = Game();
            var twin = Game();
            Assert.Equal("identical", SaveStateComparer.FirstDifference(Normalized(probed), Normalized(twin)));

            int projections = 0;
            for (int day = 0; day < 60; day++)
            {
                string before = SaveStateComparer.Serialize(probed);
                var streams = probed.Rng.CaptureStates();
                foreach (var site in probed.State.SeaSites)
                    foreach (int wait in new[] { 0, 14, 28, 56 })
                        if (probed.ProjectHarvest(site.Id, null, 1.0, wait).Ok) projections++;
                Assert.Equal(streams, probed.Rng.CaptureStates());
                Assert.Equal("identical", SaveStateComparer.FirstDifference(before, SaveStateComparer.Serialize(probed)));
                probed.Advance(1);
                twin.Advance(1);
            }
            Assert.True(projections > 100, $"{projections} projections");
            Assert.Equal("identical", SaveStateComparer.FirstDifference(Normalized(probed), Normalized(twin)));
        }

        [Fact]
        public void TheProjection_RefusesWhatTheOrderWouldRefuse()
        {
            var engine = NewEngine(72);
            var site = AddSite(engine, "a");
            var empty = AddSite(engine, "b");
            AddPen(engine, site, 10_000, 5.0);
            Assert.Equal("holding_not_found", engine.ProjectHarvest("nowhere").ErrorKey);
            Assert.Equal("invalid_wait", engine.ProjectHarvest(site.Id, null, 1.0, -1).ErrorKey);
            Assert.Equal("invalid_wait", engine.ProjectHarvest(site.Id, null, 1.0, SimulationEngine.MaxHarvestWaitDays + 1).ErrorKey);
            Assert.Equal("invalid_fraction", engine.ProjectHarvest(site.Id, null, 0.0).ErrorKey);
            Assert.Equal("holding_empty", engine.ProjectHarvest(empty.Id).ErrorKey);
            Assert.True(engine.OrderHarvest(site.Id).Ok);
            Assert.Equal("pen_in_order", engine.ProjectHarvest(site.Id).ErrorKey);
        }
    }
}
