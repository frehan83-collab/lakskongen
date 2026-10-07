using Xunit;
using LakskongenSim.Farm;

namespace LakskongenSim.Tests.StormPhase12
{
    /// <summary>Phase 2.1: appetite curve sanity.</summary>
    public class AppetiteTests
    {
        [Fact]
        public void OptimalConditions_FullRation()
        {
            Assert.Equal(1.0, FeedSystem.CalculateAppetite(12.0, 1.0));
        }

        [Fact]
        public void Hypoxia_ReducesAppetite()
        {
            double good = FeedSystem.CalculateAppetite(12.0, 1.0);
            double bad = FeedSystem.CalculateAppetite(12.0, 0.3);
            Assert.True(bad < good);
            Assert.InRange(bad, 0.3, 1.0);
        }

        [Fact]
        public void ColdWater_ReducesAppetite()
        {
            Assert.True(FeedSystem.CalculateAppetite(1.0, 1.0) < 1.0);
        }
    }
}
