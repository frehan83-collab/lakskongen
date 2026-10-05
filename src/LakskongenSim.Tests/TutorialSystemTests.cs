using Xunit;
using LakskongenSim.Core;

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
    }
}
