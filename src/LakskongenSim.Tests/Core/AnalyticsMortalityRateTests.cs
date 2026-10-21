using System.Linq;
using Xunit;
using LakskongenSim.Core;
using LakskongenSim.Farm;
using LakskongenSim.Finance;

namespace LakskongenSim.Tests.Core
{
    /// <summary>
    /// The analytics "mortality_rate" is fish that DIED as a share of the fish
    /// alive that day. It used to be the day's drop in fish count, so every
    /// harvest (and every transfer or sale) was reported as mortality: the
    /// first playtest of the first season (SHIP_PLAN cycle 1) saw
    /// "Dødelighet 100,0 % siste døgn" the day after each harvest, on the
    /// dashboard's alert and in the analytics chart.
    /// </summary>
    public class AnalyticsMortalityRateTests
    {
        private static (SimulationEngine engine, SeaSite site) StockedSite(int seed)
        {
            var engine = new SimulationEngine();
            engine.FinanceManager = new FinanceManager(50_000_000m);
            engine.Initialize(enableEvents: false, seed: seed);
            var site = new SeaSite("mort-site", "zone-mort") { MABTonnes = 780 };
            engine.State.SeaSites.Add(site);
            Assert.True(engine.StockSmolts(site.Id, 20_000).Ok);
            return (engine, site);
        }

        private static int FishOnSite(SimulationEngine engine)
            => engine.State.Populations.Sum(p => p.TotalCount);

        [Fact]
        public void HarvestDay_IsNotRecordedAsMortality()
        {
            var (engine, site) = StockedSite(71);
            engine.Advance(120);
            int before = FishOnSite(engine);
            Assert.True(before > 0, "setup: fish are in the sea");

            var harvest = engine.HarvestSite(site.Id);
            Assert.True(harvest.Ok, harvest.Message);
            engine.Advance(1);

            Assert.True(FishOnSite(engine) < before / 2, "setup: the harvest removed the fish");
            double rate = engine.Analytics.GetLatest(AnalyticsMetric.MortalityRate);
            Assert.True(rate < 0.01,
                $"a harvest is not mortality: the day after it recorded {rate:P1}");
        }

        [Fact]
        public void OrdinaryDays_RecordTheLedgersDeathsOverTheFishAlive()
        {
            var (engine, _) = StockedSite(72);
            engine.Advance(30);
            for (int i = 0; i < 20; i++)
            {
                engine.Advance(1);
                var ledger = engine.MortalityLedger;
                long deaths = ledger.ForDays(ledger.Day, ledger.Day).Total.Fish;
                int alive = FishOnSite(engine);
                double expected = alive + deaths > 0 ? (double)deaths / (alive + deaths) : 0.0;
                double rate = engine.Analytics.GetLatest(AnalyticsMetric.MortalityRate);
                Assert.Equal(expected, rate, 9);
            }
        }
    }
}
