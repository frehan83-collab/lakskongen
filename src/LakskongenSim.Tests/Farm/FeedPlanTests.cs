using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using LakskongenSim.Tests.Integration;
using Xunit;

namespace LakskongenSim.Tests.Farm
{
    /// <summary>
    /// Roadmap U2-04, the feeding plan on the engine path: the verb sets every
    /// pen's ration (S2-03's RationPolicy) and the site's recipe and window;
    /// a restricted ration grows less, converts better and costs less a day
    /// (acceptance 1); the 30-day projection is the expectation of what the
    /// game then does (acceptance 2); "Fôr alle" reaches every site
    /// (acceptance 3, the engine half); plans survive a save (acceptance 4);
    /// and a game that never sets a plan is unchanged.
    /// </summary>
    public class FeedPlanTests
    {
        private const string FamilyFarm = "forste_sesong";

        private static SimulationEngine Started(string scenario, int seed, bool events = false)
        {
            var engine = new SimulationEngine { FinanceManager = new FinanceManager(50_000_000m) };
            engine.Initialize(enableEvents: events, seed: seed);
            Assert.True(engine.StartScenario(scenario).Ok);
            engine.State.RandomSeed = seed;
            return engine;
        }

        private static SeaSite StockedSite(SimulationEngine engine)
            => engine.State.SeaSites.First(s => s.Pens.Sum(p => p.TotalCount) > 0);

        /// <summary>What a stretch of days did: growth, feed delivered and eaten, feed cost.</summary>
        private sealed record Realized(double GrowthKg, double FeedKg, double EatenKg, double CostNok, int Days)
        {
            public double Fcr => FeedKg / GrowthKg;
            public double BiologicalFcr => EatenKg / GrowthKg;
            public double CostPerDay => CostNok / Days;
        }

        /// <summary>Run <paramref name="days"/> days and measure the feeding (one stocked site in the game).</summary>
        private static Realized Run(SimulationEngine engine, SeaSite site, int days)
        {
            double cost = 0.0;
            Action<decimal> onCost = c => cost += (double)c;
            engine.Feed.OnFeedCostIncurred += onCost;
            double g0 = engine.Feed.TotalGrowthKg, f0 = engine.Feed.TotalFeedUsedKg;
            engine.Advance(days);
            engine.Feed.OnFeedCostIncurred -= onCost;
            double feed = engine.Feed.TotalFeedUsedKg - f0;
            // Delivered feed is what was eaten plus what poor spreading wastes
            // (FeedSystem.FeedRation): the site's spreading gives the ratio.
            double deliveredPerEaten = 1.0 + (FeedSystem.PrecisionFCRMultiplier(engine.SpreadQualityOf(site)) - 1.0)
                * (1.0 - ResearchEffectKeys.ClampReduction(engine.FeedWasteReduction));
            return new Realized(engine.Feed.TotalGrowthKg - g0, feed, feed / deliveredPerEaten, cost, days);
        }

        // ─── Acceptance 1: 120 days at 70 % against full appetite ─────────────

        [Theory]
        [InlineData(1, false)]
        [InlineData(7, false)]
        [InlineData(13, true)]
        public void SeventyPercent_For120Days_GrowsLess_ConvertsBetter_AndCostsLessPerDay(int seed, bool events)
        {
            Realized Measure(double? rationPct)
            {
                var engine = Started(FamilyFarm, seed, events);
                engine.Advance(30);
                var site = StockedSite(engine);
                if (rationPct is double pct)
                    Assert.True(engine.SetFeedPlan(site.Id, pct, FeedRecipeKind.Standard, 0).Ok);
                return Run(engine, site, 120);
            }

            var full = Measure(null);
            var restricted = Measure(70.0);
            Assert.True(restricted.GrowthKg < full.GrowthKg * 0.85,
                $"growth {restricted.GrowthKg:N0} kg at 70 % vs {full.GrowthKg:N0} kg at full appetite");
            Assert.True(restricted.BiologicalFcr < full.BiologicalFcr,
                $"biological FCR {restricted.BiologicalFcr:F3} at 70 % vs {full.BiologicalFcr:F3}");
            Assert.True(restricted.CostPerDay < full.CostPerDay * 0.8,
                $"feed cost {restricted.CostPerDay:N0} NOK/day at 70 % vs {full.CostPerDay:N0}");
        }

        // ─── Acceptance 2: the 30-day projection ───────────────────────────────

        private sealed record ProjectionError(double Growth, double Cost, double Fcr, string Case);

        private static List<ProjectionError> ProjectionErrors(Func<int, int, (SimulationEngine, SeaSite)?> start,
            IEnumerable<int> seeds, int[] startDays, double rationPct, FeedRecipeKind recipe)
        {
            var errors = new List<ProjectionError>();
            foreach (int seed in seeds)
                foreach (int day in startDays)
                {
                    if (start(seed, day) is not var (engine, site)) continue;
                    var projection = engine.ProjectFeedPlan(site.Id, rationPct, recipe, 30)!;
                    Assert.True(engine.SetFeedPlan(site.Id, rationPct, recipe, 0).Ok);
                    var real = Run(engine, site, 30);
                    if (real.GrowthKg <= 0.0) continue;
                    errors.Add(new ProjectionError(projection.GrowthKg / real.GrowthKg - 1.0,
                        projection.CostNok / real.CostNok - 1.0, projection.Fcr / real.Fcr - 1.0, $"seed {seed} day {day}"));
                }
            return errors;
        }

        private static double Median(IEnumerable<double> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            return sorted[sorted.Count / 2];
        }

        /// <summary>
        /// The projection is the expectation of the game's own next 30 days:
        /// on the player path (the family farm, 20 seeds x 6 start days over a
        /// year, 70 % on the standard feed) the growth, FCR and feed cost per day
        /// it shows are within ±10 % of what the game then does in at least 80 %
        /// of cases, 5 % at the median, and unbiased (mean error within ±3 %).
        /// Single cases can miss by more: the sea's warm and cold spells
        /// (the AR(1) anomaly) and the bloom deck are random, and the
        /// projection takes their expectation.
        /// </summary>
        [Fact]
        public void TheThirtyDayProjection_IsWithinTenPercentOfTheRealizedResult_OnThePlayerPath()
        {
            var errors = ProjectionErrors((seed, day) =>
            {
                var engine = Started(FamilyFarm, seed);
                engine.Advance(day);
                var site = engine.State.SeaSites.First();
                return site.Pens.Sum(p => p.TotalCount) > 0 ? (engine, site) : null;
            }, Enumerable.Range(1, 20), new[] { 0, 60, 120, 180, 240, 300 }, 70.0, FeedRecipeKind.Standard);

            Assert.True(errors.Count >= 100, $"{errors.Count} cases");
            foreach (var (name, pick) in new (string, Func<ProjectionError, double>)[]
                     { ("growth", e => e.Growth), ("cost per day", e => e.Cost), ("FCR", e => e.Fcr) })
            {
                var values = errors.Select(pick).ToList();
                double within = values.Count(v => Math.Abs(v) <= 0.10) / (double)values.Count;
                Assert.True(within >= 0.80, $"{name}: {within:P0} of {values.Count} cases within ±10 %");
                Assert.True(Median(values.Select(Math.Abs)) <= 0.05, $"{name}: median error {Median(values.Select(Math.Abs)):P1}");
                Assert.InRange(values.Average(), -0.03, 0.03);
            }
            // The FCR holds case by case (blooms move growth and feed together).
            Assert.True(errors.Count(e => Math.Abs(e.Fcr) <= 0.10) >= errors.Count * 0.95,
                string.Join("; ", errors.Where(e => Math.Abs(e.Fcr) > 0.10).Select(e => $"{e.Case} {e.Fcr:P1}")));
        }

        /// <summary>
        /// Where nothing but the sea's temperature is random (a site with no
        /// zone: no blooms, no zone chemistry), the projection's FCR matches the
        /// game's to 1 % in every case and its growth and cost are within ±10 %
        /// in at least 90 % of them.
        /// </summary>
        [Fact]
        public void TheProjection_TracksTheGame_WhereOnlyTheWeatherIsRandom()
        {
            var errors = ProjectionErrors((seed, day) =>
            {
                var engine = new SimulationEngine { FinanceManager = new FinanceManager(5_000_000_000m) };
                engine.Initialize(enableEvents: false, seed: seed);
                engine.State.HomeProductionArea = 4;
                var site = new SeaSite("open") { MABTonnes = 5_000 };
                engine.State.SeaSites.Add(site);
                Assert.True(engine.StockSmolts(site.Id, 10_000, "standard").Ok);
                engine.Advance(day);
                return (engine, site);
            }, Enumerable.Range(1, 20), new[] { 30, 90, 150, 210, 270, 330 }, 70.0, FeedRecipeKind.Standard);

            Assert.Equal(120, errors.Count);
            Assert.All(errors, e => Assert.InRange(e.Fcr, -0.01, 0.01));
            Assert.True(errors.Count(e => Math.Abs(e.Growth) <= 0.10) >= 108,
                $"growth within ±10 % in {errors.Count(e => Math.Abs(e.Growth) <= 0.10)} of 120");
            Assert.True(errors.Count(e => Math.Abs(e.Cost) <= 0.10) >= 108,
                $"cost within ±10 % in {errors.Count(e => Math.Abs(e.Cost) <= 0.10)} of 120");
        }

        /// <summary>With nothing random in the window (the first month of the family farm, seed 1), the twin is the game.</summary>
        [Fact]
        public void TheProjection_IsTheGame_WhenTheWindowHoldsNoSurprise()
        {
            var engine = Started(FamilyFarm, 1);
            var site = StockedSite(engine);
            var projection = engine.ProjectFeedPlan(site.Id, 85.0, FeedRecipeKind.HighEnergy, 30)!;
            Assert.True(engine.SetFeedPlan(site.Id, 85.0, FeedRecipeKind.HighEnergy, 0).Ok);
            var real = Run(engine, site, 30);
            Assert.InRange(projection.GrowthKg / real.GrowthKg, 0.99, 1.01);
            Assert.InRange(projection.FeedKg / real.FeedKg, 0.99, 1.01);
            Assert.InRange(projection.CostNok / real.CostNok, 0.99, 1.01);
        }

        [Fact]
        public void TheProjection_ChangesNothing_AndDrawsNoRandomNumbers()
        {
            var engine = Started(FamilyFarm, 5);
            engine.Advance(40);
            string before = SaveStateComparer.Serialize(engine);
            var site = StockedSite(engine);
            for (int pct = 50; pct <= 100; pct += 10)
                foreach (var recipe in Enum.GetValues<FeedRecipeKind>())
                    Assert.NotNull(engine.ProjectFeedPlan(site.Id, pct, recipe, 30));
            Assert.Equal("identical", SaveStateComparer.FirstDifference(before, SaveStateComparer.Serialize(engine)));
        }

        // ─── The verb ──────────────────────────────────────────────────────────

        [Fact]
        public void SetFeedPlan_SetsEveryPensRation_AndTheSitesRecipeAndWindow()
        {
            var engine = Started("po3_red_sone", 3);
            var site = StockedSite(engine);
            var result = engine.SetFeedPlan(site.Id, 70.0, FeedRecipeKind.Economy, 45);
            Assert.True(result.Ok, result.Message);
            Assert.All(site.Pens, p => Assert.Equal(0.70, p.Ration.FractionOfAppetite, 12));
            var plan = engine.FeedPlanOf(site.Id)!;
            Assert.Equal(FeedRecipeKind.Economy, plan.Recipe);
            Assert.Equal(engine.State.Day + 45, plan.EndDay);
            // The other site keeps the standard plan.
            var other = engine.State.SeaSites.First(s => s.Id != site.Id);
            Assert.Null(engine.FeedPlanOf(other.Id));
            Assert.All(other.Pens, p => Assert.Equal(1.0, p.Ration.FractionOfAppetite));
        }

        [Theory]
        [InlineData("nope", 80.0, 0, "site_not_found")]
        [InlineData(null, 49.0, 0, "ration_out_of_range")]
        [InlineData(null, 101.0, 0, "ration_out_of_range")]
        [InlineData(null, double.NaN, 0, "ration_out_of_range")]
        [InlineData(null, 80.0, -1, "window_out_of_range")]
        [InlineData(null, 80.0, 366, "window_out_of_range")]
        public void SetFeedPlan_RefusesWhatItCannotDo(string? siteId, double pct, int window, string error)
        {
            var engine = Started(FamilyFarm, 2);
            var site = StockedSite(engine);
            var result = engine.SetFeedPlan(siteId ?? site.Id, pct, FeedRecipeKind.Standard, window);
            Assert.False(result.Ok);
            Assert.Equal(error, result.ErrorKey);
            Assert.Null(site.FeedPlan);
            Assert.All(site.Pens, p => Assert.Equal(1.0, p.Ration.FractionOfAppetite));
        }

        [Fact]
        public void FeedAll_GivesEverySiteThePlan()
        {
            var engine = Started("po3_red_sone", 4);
            Assert.True(engine.State.SeaSites.Count >= 2);
            var result = engine.SetFeedPlanForAllSites(85.0, FeedRecipeKind.HighEnergy, 0);
            Assert.True(result.Ok);
            Assert.Equal(engine.State.SeaSites.Count, (int)result.Value);
            foreach (var site in engine.State.SeaSites)
            {
                Assert.Equal(FeedRecipeKind.HighEnergy, engine.FeedPlanOf(site.Id)!.Recipe);
                Assert.All(site.Pens, p => Assert.Equal(0.85, p.Ration.FractionOfAppetite, 12));
            }
        }

        [Fact]
        public void APlansWindow_RunsOut_AndTheSiteGoesBackToTheStandardPlan()
        {
            var engine = Started(FamilyFarm, 6);
            var site = StockedSite(engine);
            Assert.True(engine.SetFeedPlan(site.Id, 70.0, FeedRecipeKind.Economy, 10).Ok);
            engine.Advance(9);
            Assert.NotNull(engine.FeedPlanOf(site.Id));
            Assert.All(site.Pens, p => Assert.Equal(0.70, p.Ration.FractionOfAppetite, 12));
            engine.Advance(1);
            Assert.Null(engine.FeedPlanOf(site.Id));
            engine.Advance(1);
            Assert.Null(site.FeedPlan);
            Assert.All(site.Pens, p => Assert.Equal(1.0, p.Ration.FractionOfAppetite));
        }

        [Fact]
        public void APenStockedUnderAPlan_IsFedThePlanFromItsFirstDay()
        {
            var engine = Started("po3_red_sone", 8);
            var site = engine.State.SeaSites.First(s => s.PhysicalPens.Any(s.IsPenFree));
            Assert.True(engine.SetFeedPlan(site.Id, 75.0, FeedRecipeKind.Standard, 0).Ok);
            var free = site.PhysicalPens.First(site.IsPenFree);
            var stocked = engine.StockSmoltsInPen(site.Id, free.Number.ToString(), 20_000, "standard", SmoltQualityTier.Standard);
            Assert.True(stocked.Ok, stocked.Message);
            var population = site.PopulationIn(free)!;
            engine.Advance(1);
            Assert.Equal(0.75, population.Ration.FractionOfAppetite, 12);
        }

        [Fact]
        public void TheStandardPlan_ClearsAPlan_AndPutsEveryPenBackOnFullAppetite()
        {
            var engine = Started(FamilyFarm, 9);
            var site = StockedSite(engine);
            Assert.True(engine.SetFeedPlan(site.Id, 60.0, FeedRecipeKind.HighEnergy, 0).Ok);
            Assert.True(engine.SetFeedPlan(site.Id, 100.0, FeedRecipeKind.Standard, 0).Ok);
            Assert.Null(site.FeedPlan);
            Assert.All(site.Pens, p => Assert.Same(RationPolicy.FullAppetite, p.Ration));
        }

        // ─── Recipes ───────────────────────────────────────────────────────────

        /// <summary>
        /// A recipe is priced on its energy: high energy takes less feed and
        /// economy more for the same growth, and the feed cost per kg of growth
        /// stays within 1 % of the standard feed's; the economy feed's fillets
        /// are graded pale.
        /// </summary>
        [Fact]
        public void Recipes_TradeFeedTonnageForPrice_AtAboutTheSameCostPerKgOfGrowth()
        {
            Realized Measure(FeedRecipeKind recipe)
            {
                var engine = Started(FamilyFarm, 11);
                engine.Advance(60);
                var site = StockedSite(engine);
                Assert.True(engine.SetFeedPlan(site.Id, 100.0, recipe, 0).Ok);
                return Run(engine, site, 60);
            }
            var standard = Measure(FeedRecipeKind.Standard);
            var high = Measure(FeedRecipeKind.HighEnergy);
            var economy = Measure(FeedRecipeKind.Economy);
            Assert.Equal(standard.GrowthKg, high.GrowthKg, 6);
            Assert.Equal(standard.GrowthKg, economy.GrowthKg, 6);
            Assert.InRange(high.FeedKg / standard.FeedKg, 0.945, 0.955);
            Assert.InRange(economy.FeedKg / standard.FeedKg, 1.055, 1.065);
            Assert.InRange(high.CostNok / standard.CostNok, 0.99, 1.01);
            Assert.InRange(economy.CostNok / standard.CostNok, 0.99, 1.01);

            var engine = Started(FamilyFarm, 11);
            var site = StockedSite(engine);
            Assert.Equal("A", engine.FilletGradeOf(site));
            Assert.True(engine.SetFeedPlan(site.Id, 100.0, FeedRecipeKind.Economy, 0).Ok);
            Assert.Equal("C", engine.FilletGradeOf(site));
        }

        // ─── Acceptance 4: saves ───────────────────────────────────────────────

        [Fact]
        public void APlan_SurvivesASave_AndTheGameContinuesExactly_ThroughItsEnd()
        {
            var engine = Started("po3_red_sone", 12);
            engine.Advance(20);
            var site = StockedSite(engine);
            Assert.True(engine.SetFeedPlan(site.Id, 72.0, FeedRecipeKind.Economy, 40).Ok);
            engine.Advance(10);

            var resumed = SaveStateComparer.Resume(SaveStateComparer.Serialize(engine), events: false);
            var plan = resumed.FeedPlanOf(site.Id)!;
            Assert.Equal(0.72, plan.RationFraction, 12);
            Assert.Equal(FeedRecipeKind.Economy, plan.Recipe);
            Assert.Equal(engine.FeedPlanOf(site.Id)!.EndDay, plan.EndDay);
            Assert.All(resumed.State.SeaSites.Single(s => s.Id == site.Id).Pens,
                p => Assert.Equal(0.72, p.Ration.FractionOfAppetite, 12));

            engine.Advance(60);
            resumed.Advance(60);
            Assert.Null(resumed.FeedPlanOf(site.Id));
            Assert.Equal("identical", SaveStateComparer.FirstDifference(
                SaveStateComparer.Normalize(SaveStateComparer.Serialize(engine)),
                SaveStateComparer.Normalize(SaveStateComparer.Serialize(resumed))));
        }

        /// <summary>A game that sets only standard plans plays exactly like one that never touches the verb.</summary>
        [Fact]
        public void StandardPlans_LeaveTheGameUnchanged()
        {
            var untouched = Started("po3_red_sone", 17, events: true);
            var planned = Started("po3_red_sone", 17, events: true);
            Assert.True(planned.SetFeedPlanForAllSites(100.0, FeedRecipeKind.Standard, 0).Ok);
            untouched.Advance(200);
            planned.Advance(200);
            Assert.Equal("identical", SaveStateComparer.FirstDifference(
                SaveStateComparer.Normalize(SaveStateComparer.Serialize(untouched)),
                SaveStateComparer.Normalize(SaveStateComparer.Serialize(planned))));
        }
    }
}
