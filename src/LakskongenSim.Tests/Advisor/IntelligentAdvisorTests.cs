#nullable enable annotations
using System;
using System.Linq;
using Xunit;
using LakskongenSim.Advisor;
using LakskongenSim.Environment;
using LakskongenSim.Market;
using LakskongenSim.Finance;
using LakskongenSim.Core;
using LakskongenSim.Farm;
using LakskongenSim.Biology;

namespace LakskongenSim.Tests
{
    /// <summary>
    /// Unit tests for the Intelligent Advisor System.
    /// </summary>
    public class IntelligentAdvisorTests
    {
        private GameState CreateTestGameState(double oxygen = 8.0, double temp = 11.0, double density = 15.0, double lice = 0.5)
        {
            var state = new GameState { Day = 365 };

            var zone = new Zone
            {
                Id = "PO03",
                WaterChemistry = new WaterChemistry { Temperature = temp, OxygenO2 = oxygen, pH = 7.8 },
                CurrentStockingDensity = (float)density,
                CurrentFishBiomassKg = density * 50000
            };
            state.Zones.Add(zone);

            var site = new SeaSite("TEST-001", "PO03")
            {
                MABTonnes = 780,
                CurrentStockingDensity = (float)density,
                PenVolume = 50000f
            };

            var population = new FishPopulation
            {
                Id = "POP-TEST",
                ZoneId = "PO03",
                SeaLiceLoad = lice
            };

            population.AddCohort(new FishPopulation.Cohort(400, 50000, 4.2, Data.LifeStage.Harvest));
            site.Pens.Add(population);
            state.SeaSites.Add(site);

            return state;
        }

        [Fact]
        public void Advisor_Initializes_Correctly()
        {
            var state = CreateTestGameState();
            var advisor = new IntelligentAdvisor(
                new MareanoEnvironmentalDataProvider(),
                new MarketSimulation(),
                new FinanceManager(1_000_000m),
                state
            );

            Assert.NotNull(advisor.Alerts);
            Assert.NotNull(advisor.History);
        }

        [Fact]
        public void Critical_Oxygen_Generates_Emergency()
        {
            var state = CreateTestGameState(oxygen: 4.5);
            var advisor = new IntelligentAdvisor(
                new MareanoEnvironmentalDataProvider(),
                new MarketSimulation(),
                new FinanceManager(5_000_000m),
                state
            );

            var recs = advisor.AnalyzeAndGenerateRecommendations();
            var emergency = recs.FirstOrDefault(r => r.UrgencyRating == 5);

            Assert.NotNull(emergency);
            Assert.Equal(RecommendationCategory.Emergency, emergency.Category);
        }

        [Fact]
        public void High_Sea_Lice_Generates_Compliance_Alert()
        {
            var state = CreateTestGameState(lice: 3.0);
            Alert? alert = null;
            var advisor = new IntelligentAdvisor(
                new MareanoEnvironmentalDataProvider(),
                new MarketSimulation(),
                new FinanceManager(5_000_000m),
                state
            );
            advisor.OnAlertGenerated += a => alert = a;

            advisor.AnalyzeAndGenerateRecommendations();

            Assert.NotNull(alert);
            Assert.Contains("lakselus", alert.Title.ToLower());
        }

        [Fact]
        public void Low_Cash_Generates_Financial_Alert()
        {
            var state = CreateTestGameState();
            Alert? alert = null;
            var advisor = new IntelligentAdvisor(
                new MareanoEnvironmentalDataProvider(),
                new MarketSimulation(),
                new FinanceManager(50_000m),
                state
            );
            advisor.OnAlertGenerated += a => alert = a;

            advisor.AnalyzeAndGenerateRecommendations();

            Assert.NotNull(alert);
            Assert.Equal(AlertSeverity.Critical, alert.Severity);
        }

        [Fact]
        public void Recommendations_Are_Tracked_In_History()
        {
            var state = CreateTestGameState();
            var advisor = new IntelligentAdvisor(
                new MareanoEnvironmentalDataProvider(),
                new MarketSimulation(),
                new FinanceManager(5_000_000m),
                state
            );

            var before = advisor.History.AllRecommendations.Count();
            advisor.AnalyzeAndGenerateRecommendations();
            var after = advisor.History.AllRecommendations.Count();

            Assert.True(after > before);
        }

        [Fact]
        public void FollowRecommendation_Updates_Status()
        {
            var state = CreateTestGameState();
            var advisor = new IntelligentAdvisor(
                new MareanoEnvironmentalDataProvider(),
                new MarketSimulation(),
                new FinanceManager(5_000_000m),
                state
            );

            var recs = advisor.AnalyzeAndGenerateRecommendations();
            var rec = recs.First();
            
            advisor.FollowRecommendation(rec.Id);
            
            Assert.True(rec.WasFollowed);
        }
    }
}
