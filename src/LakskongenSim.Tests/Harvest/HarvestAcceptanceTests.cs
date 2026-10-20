using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Calibration;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using LakskongenSim.Tests.Integration;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Harvest
{
    /// <summary>
    /// Roadmap S2-04 acceptance on the player path, measured over seeds. The
    /// numbers are recorded in docs/BALANCE.md §1h.
    /// </summary>
    public class HarvestAcceptanceTests
    {
        private static readonly HarvestCalibration Cal = CalibrationRegistry.Default.Harvest;
        private readonly ITestOutputHelper _out;
        public HarvestAcceptanceTests(ITestOutputHelper output) => _out = output;

        /// <summary>EBIT/kg cyclical band, NOK per kg gutted weight (SalMar farming Norway: 23.4 FY24, 13.6 FY25).</summary>
        private const double EbitPerKgMin = 8.0, EbitPerKgMax = 25.0;

        private const int Seeds = 10;

        /// <summary>
        /// Acceptance 4: the EBIT/kg band test on the PlayerScriptProbe path. A
        /// two-site commercial farm played through the verbs (stock, delouse, order
        /// the harvest), with EBIT from the engine's ledger per kilo of gutted fish
        /// sold. The median of 10 seeds lies in the band; every seed is finite.
        /// </summary>
        [Fact]
        public void AFarmCycle_EarnsAnEbitPerKiloInTheCyclicalBand_OnThePlayerPath()
        {
            var ebit = new List<double>();
            foreach (int seed in Enumerable.Range(1, Seeds))
            {
                var r = PlayerScriptFarmCycle.Run(seed);
                _out.WriteLine($"seed {seed}: {r.DaysToLastSlaughter} d, {r.HogKg / 1000:N0} t HOG, revenue {r.RevenuePerKgHog:F2}, " +
                    $"cost {r.CostPerKgHog:F2}, EBIT {r.EbitPerKgHog:F2} NOK/kg HOG, price achievement {r.PriceAchievement:F3}, " +
                    $"superior {r.SuperiorShare:P1}, mortality {r.MortalityPct:F1}%, treatments {r.Treatments}");
                Assert.True(double.IsFinite(r.EbitPerKgHog));
                Assert.True(r.HogKg > 0.0);
                Assert.Equal(Sites(r), r.Orders);
                // Every harvest cost is in the ledger, never more than the sales.
                Assert.InRange((double)r.HarvestCostNok / r.HogKg, 4.9, 5.1);
                Assert.InRange(r.PriceAchievement, 0.85, 1.0);
                ebit.Add(r.EbitPerKgHog);
            }
            ebit.Sort();
            double median = (ebit[Seeds / 2 - 1] + ebit[Seeds / 2]) / 2.0;
            _out.WriteLine($"EBIT/kg HOG min {ebit[0]:F2} median {median:F2} max {ebit[^1]:F2}");
            Assert.InRange(median, EbitPerKgMin, EbitPerKgMax);
        }

        private static int Sites(PlayerScriptFarmCycle.FarmCycleResult r) => PlayerScriptFarmCycle.Sites;

        /// <summary>
        /// Acceptance 2 on the engine path: two pens harvested the same day, one
        /// of 2 kg fish and one of 5 kg fish (a near-uniform pen, CV 1%): the 2 kg
        /// fish earn at least 25% less per live kilo, gross and net of the
        /// harvest cost. With a real farm's spread (CV 20%) some of the small
        /// fish reach the 2-3 kg class and some large ones fall to 3-4 kg; the
        /// pens then differ by ~24% (measured, docs/BALANCE.md §1h).
        /// </summary>
        [Fact]
        public void TwoKiloFish_EarnAtLeast25PercentLessPerKilo_ThanFiveKiloFish_OnTheEnginePath()
        {
            var engine = new SimulationEngine { FinanceManager = new FinanceManager(100_000_000m) };
            engine.Initialize(enableEvents: false, seed: 61);
            engine.State.SeaSites.Clear();
            engine.State.HomeProductionArea = 4;
            (double gross, double net) PerLiveKg(string id, double kg, double cv)
            {
                var site = new SeaSite(id) { MABTonnes = 10_000 };
                engine.State.SeaSites.Add(site);
                var pen = new FishPopulation();
                pen.InjectDependencies(engine.Growth, engine.Mortality);
                pen.Cohorts.Add(new FishPopulation.Cohort(400, 10_000, kg, LifeStage.Seawater) { WeightCv = cv });
                site.Stock(pen);
                engine.State.Populations.Add(pen);
                var harvest = engine.HarvestSite(site.Id);
                Assert.True(harvest.Ok, harvest.Message);
                return (harvest.Value2 / harvest.Value, (double)engine.HarvestReports[^1].NetRevenueNok / harvest.Value);
            }
            var two = PerLiveKg("small", 2.0, 0.01);
            var five = PerLiveKg("large", 5.0, 0.01);
            var twoSpread = PerLiveKg("small-spread", 2.0, 0.20);
            var fiveSpread = PerLiveKg("large-spread", 5.0, 0.20);
            _out.WriteLine($"gross per live kg: 2 kg {two.gross:F2}, 5 kg {five.gross:F2} NOK ({two.gross / five.gross:P1}); " +
                $"net {two.net / five.net:P1}; with CV 20% {twoSpread.gross / fiveSpread.gross:P1}");
            Assert.True(two.gross <= 0.75 * five.gross, $"2 kg fish earn {two.gross / five.gross:P1} of 5 kg fish per kilo");
            Assert.True(two.net <= 0.75 * five.net, $"net: {two.net / five.net:P1}");
            Assert.True(twoSpread.gross < 0.80 * fiveSpread.gross, $"with spread: {twoSpread.gross / fiveSpread.gross:P1}");
        }

        /// <summary>
        /// Grades follow the water the fish lived in: sea fish harvested after a
        /// winter carry winter wounds (Norway 2024: ~65% superior in the first
        /// half year, ~90% in the second); fish in a land tank at a controlled
        /// 12 °C do not, whatever the season.
        /// </summary>
        [Fact]
        public void ASpringHarvest_GradesWorseThanALateSummerOne_AtSea_ButNotInALandTank()
        {
            (double sea, double land) SuperiorAfter(int days)
            {
                var engine = new SimulationEngine { FinanceManager = new FinanceManager(100_000_000m) };
                engine.Initialize(enableEvents: false, seed: 62);
                engine.State.SeaSites.Clear();
                engine.State.HomeProductionArea = 4;
                var site = new SeaSite("sea") { MABTonnes = 10_000 };
                engine.State.SeaSites.Add(site);
                var seaPen = new FishPopulation();
                seaPen.InjectDependencies(engine.Growth, engine.Mortality);
                seaPen.Cohorts.Add(new FishPopulation.Cohort(400, 10_000, 4.0, LifeStage.Seawater) { WeightCv = 0.20 });
                site.Stock(seaPen);
                engine.State.Populations.Add(seaPen);
                engine.State.RASFacilities.Add(new RASFacility { Id = "land", Name = "Land", ConstructionProgress = 1.0 });
                var tank = new FishPopulation { SiteId = "land" };
                tank.InjectDependencies(engine.Growth, engine.Mortality);
                tank.Cohorts.Add(new FishPopulation.Cohort(400, 10_000, 4.0, LifeStage.Seawater) { WeightCv = 0.20 });
                engine.RASFacilities.Single().Tanks.Add(tank);
                engine.State.Populations.Add(tank);
                engine.Advance(days);
                // Same size at slaughter (the land tank grows faster): only the
                // water the fish lived in differs, not maturation or size.
                foreach (var pen in new[] { seaPen, tank })
                    foreach (var cohort in pen.Cohorts)
                        cohort.MeanWeightKg = 5.0;
                Assert.True(engine.HarvestSite("sea").Ok);
                double sea = engine.HarvestReports[^1].SuperiorShare;
                Assert.True(engine.HarvestRASFacility("land").Ok);
                return (sea, engine.HarvestReports[^1].SuperiorShare);
            }
            var april = SuperiorAfter(100);     // after January-April in PO4 water below 8 °C
            var september = SuperiorAfter(250); // after a summer
            _out.WriteLine($"superior share: April sea {april.sea:P1} land {april.land:P1}; September sea {september.sea:P1} land {september.land:P1}");
            Assert.InRange(april.sea, 0.62, 0.80);
            Assert.InRange(september.sea, 0.86, 0.95);
            Assert.True(september.sea > april.sea + 0.10, "a spring harvest graded no worse than a late-summer one");
            Assert.True(april.land > april.sea + 0.10, "a land tank carried winter wounds");
            Assert.Equal(april.land, september.land, 6);
        }

        /// <summary>
        /// The po3 career (events on, mid-run save) sells gutted fish through the
        /// pipeline: the D2 rule still realizes the spot of the slaughter days per
        /// reference kilo, and the size and grade mix gives a price achievement
        /// below the superior 4-5 kg reference, as farmers report.
        /// </summary>
        [Theory]
        [InlineData(7)]
        [InlineData(13)]
        public void ThePo3Career_SellsGuttedFish_AtAPriceAchievementBelowTheReference(int seed)
        {
            var r = PlayerScriptProbe.RunCareer(seed);
            _out.WriteLine($"seed {seed}: {r.HarvestedKg:N0} kg live, {r.HarvestedHogKg:N0} kg HOG, realized {r.RealizedPricePerKg:F2} " +
                $"per reference kg, spot {r.MeanSpotOnHarvestDaysNok:F2}, achievement {r.PriceAchievement:F3}, superior {r.SuperiorShare:P1}, " +
                $"harvest cost {r.HarvestCostNok:N0}, {r.HarvestOrders} orders over {r.SlaughterDays} slaughter days");
            Assert.Equal(r.HarvestedKg * Cal.HogYieldFraction, r.HarvestedHogKg, 3);
            Assert.InRange(r.PriceAchievement, 0.85, 1.0);
            Assert.InRange(r.SuperiorShare, 0.75, 0.97);
            Assert.InRange(r.HarvestCostNok / r.HarvestedHogKg, 4.9, 5.1);
            Assert.Equal(2, r.HarvestOrders);
        }
    }
}
