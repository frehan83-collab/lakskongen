using Xunit;
using LakskongenSim.Core;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using LakskongenSim.Tests.Integration;

namespace LakskongenSim.Tests
{
    public class TutorialSystemTests
    {
        [Fact]
        public void Skip_CompletesSilentlyWithoutRewards()
        {
            var engine = new SimulationEngine();
            engine.Initialize(enableEvents: false);
            decimal before = engine.FinanceManager.Balance;

            engine.Tutorial.Skip();

            Assert.True(engine.Tutorial.IsComplete);
            Assert.Equal(engine.Tutorial.TotalSteps, engine.Tutorial.CompletedCount);
            Assert.Equal(before, engine.FinanceManager.Balance); // no skip rewards
        }

        /// <summary>
        /// The harvest-and-sale steps read "has sold" from the sales total, which
        /// also counts sludge income: the weekly valorization booked it for any
        /// site with 500 kg of sludge and completed the steps before a harvest.
        /// </summary>
        [Fact]
        public void SludgeIncome_IsNotAFishSale_AHarvestIs()
        {
            var engine = new SimulationEngine();
            engine.FinanceManager = new FinanceManager(50_000_000m);
            engine.Initialize(enableEvents: false, seed: 71);
            var site = new SeaSite("sale-site", "zone-sale") { MABTonnes = 780 };
            engine.State.SeaSites.Add(site);
            Assert.True(engine.StockSmolts(site.Id, 20_000).Ok);

            site.SludgeAccumulatedKgDs = 2_000;
            decimal sales = engine.State.TotalSalesRevenueNok;
            engine.ProcessSludgeValorization();
            Assert.True(engine.State.TotalSalesRevenueNok > sales, "setup: the sludge was sold");
            Assert.Equal(0m, engine.State.TotalHarvestRevenueNok);
            Assert.False(TutorialSystem.HasSold(engine.Tutorial));

            engine.Advance(120);
            var harvest = engine.HarvestSite(site.Id);
            Assert.True(harvest.Ok, harvest.Message);
            Assert.True(engine.State.TotalHarvestRevenueNok > 0m);
            Assert.True(TutorialSystem.HasSold(engine.Tutorial));

            var resumed = SaveStateComparer.Resume(SaveStateComparer.Serialize(engine), events: false);
            Assert.Equal(engine.State.TotalHarvestRevenueNok, resumed.State.TotalHarvestRevenueNok);
        }
    }
}
