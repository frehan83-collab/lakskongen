using System.Linq;
using Xunit;
using LakskongenSim.Advisor;

namespace LakskongenSim.Tests.Advisor
{
    public class EconomicAdvisorTests
    {
        private readonly EconomicAdvisor _advisor = new();

        [Fact]
        public void MonitorMarket_HighPrice_RecommendsHarvest()
        {
            var recs = _advisor.MonitorMarketConditions(100.0, 6); // 100 NOK/kg in June (avg ~75)
            Assert.Contains(recs, r => r.Title.Contains("Premium"));
        }

        [Fact]
        public void MonitorMarket_LowPrice_RecommendsDelay()
        {
            var recs = _advisor.MonitorMarketConditions(45.0, 6); // 45 NOK/kg in June (avg ~75)
            Assert.Contains(recs, r => r.Title.Contains("Dip") || r.Title.Contains("Delay"));
        }

        [Fact]
        public void MonitorMarket_Q4Approaching_AlertsUser()
        {
            var recs = _advisor.MonitorMarketConditions(70.0, 9); // September
            Assert.Contains(recs, r => r.Title.Contains("Q4"));
        }

        [Fact]
        public void CostEfficiency_LowMargin_Flags()
        {
            var recs = _advisor.AnalyzeCostEfficiency("PO04", 65.0, 70.0);
            Assert.True(recs.Count > 0, "Should flag low margin");
            Assert.Contains(recs, r => r.Category == "Cost");
        }

        [Fact]
        public void CostEfficiency_HighOpex_Flags()
        {
            var recs = _advisor.AnalyzeCostEfficiency("PO12", 60.0, 80.0);
            Assert.Contains(recs, r => r.Title.Contains("OPEX"));
        }

        [Fact]
        public void FeedStrategy_HighMarginOptimalTemp_MaximizeFeeding()
        {
            var rec = _advisor.RecommendFeedStrategy(11.0, 38.0, 80.0);
            Assert.Contains("Maximize", rec.Title);
        }

        [Fact]
        public void FeedStrategy_LowMargin_ReduceFeeding()
        {
            var rec = _advisor.RecommendFeedStrategy(11.0, 65.0, 70.0);
            Assert.Contains("Reduce", rec.Title);
        }

        [Fact]
        public void AllRecommendations_HaveValidUrgency()
        {
            var marketRecs = _advisor.MonitorMarketConditions(100.0, 6);
            var costRecs = _advisor.AnalyzeCostEfficiency("PO04", 60.0, 70.0);
            var feedRec = _advisor.RecommendFeedStrategy(11.0, 38.0, 80.0);

            foreach (var r in marketRecs.Concat(costRecs).Append(feedRec))
            {
                Assert.InRange(r.UrgencyRating, 1, 5);
                Assert.False(string.IsNullOrEmpty(r.Title));
                Assert.False(string.IsNullOrEmpty(r.Category));
            }
        }
    }
}
