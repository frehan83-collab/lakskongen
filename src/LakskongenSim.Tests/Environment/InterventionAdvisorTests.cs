using System.Collections.Generic;
using System.Linq;
using Xunit;
using LakskongenSim.Core;
using LakskongenSim.Biology;
using LakskongenSim.Data;
using LakskongenSim.Farm;
using LakskongenSim.Health;
using LakskongenSim.Optimization;
using LakskongenSim.Advisor;

namespace LakskongenSim.Tests.StormPhase12
{
    /// <summary>Phase 5.3: intervention advisor ranked suggestions.</summary>
    public class InterventionAdvisorTests
    {
        private static FishPopulation StockedPen(int count, double weightKg) =>
            new FishPopulation { Cohorts = { new FishPopulation.Cohort(365, count, weightKg, LifeStage.Seawater) } };

        [Fact]
        public void PreemptiveDelousing_SuggestedWhenLiceBreachImminent()
        {
            // Well-formed farm: one stocked site so the forecast has biomass and
            // the lice model starts above the 0.5 action limit deterministically.
            var state = new GameState();
            var site = new SeaSite("siteA", "PO1");
            var pop = StockedPen(100_000, 5.0);
            site.Stock(pop);
            state.SeaSites.Add(site);
            state.Populations.Add(pop);

            var lice = new SeaLiceModel();
            lice.Reset(0.9); // above the 0.5 threshold → breach on day 0
            var analytics = new AnalyticsTracker(state.DailySnapshots);

            var fc = InterventionAdvisor.Rank(state,
                ForecastTwin.Project(state, lice, 10.0, analytics),
                0.9, 80.0, 1.15, new List<LakskongenSim.Market.Contract>(), 0, 10);

            Assert.Contains(fc, s => s.Action == "Preemptivt avlus");
            Assert.True(fc.First(s => s.Action == "Preemptivt avlus").DeltaNok > 0);
        }

        [Fact]
        public void HarvestSuggested_WhenBiomassReady()
        {
            var state = new GameState();
            var site = new SeaSite("siteA", "PO1");
            var pop = StockedPen(100_000, 5.5); // 550,000 kg > 500,000 threshold
            site.Stock(pop);
            state.SeaSites.Add(site);
            state.Populations.Add(pop);

            var lice = new SeaLiceModel();
            lice.Reset(0.2);
            var analytics = new AnalyticsTracker(state.DailySnapshots);

            var fc = InterventionAdvisor.Rank(state,
                ForecastTwin.Project(state, lice, 10.0, analytics),
                0.2, 80.0, 1.15, new List<LakskongenSim.Market.Contract>(), 0, 10);

            Assert.Contains(fc, s => s.Action == "Høsting i vindu");
        }

        [Fact]
        public void FeedRecipeTune_SuggestedWhenFcrHigh()
        {
            // Recipe tuning is farm-global: it must surface even with no stocked
            // farm yet (regression guard for the early-return bug).
            var state = new GameState();
            var analytics = new AnalyticsTracker(state.DailySnapshots);

            var fc = InterventionAdvisor.Rank(state,
                ForecastTwin.Project(state, new SeaLiceModel(), 10.0, analytics),
                0.2, 80.0, 1.30, new List<LakskongenSim.Market.Contract>(), 0, 10);

            Assert.Contains(fc, s => s.Action == "Justere fôrresept");
        }
    }
}