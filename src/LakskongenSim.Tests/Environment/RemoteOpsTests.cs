using Xunit;
using LakskongenSim.Optimization;

namespace LakskongenSim.Tests.StormPhase12
{
    /// <summary>Phase 4.2: attention budget math.</summary>
    public class RemoteOpsTests
    {
        [Fact]
        public void Budget_ScalesWithCrewCapped()
        {
            Assert.Equal(100, RemoteOpsCentre.BudgetFor(0));
            Assert.Equal(130, RemoteOpsCentre.BudgetFor(2));
            Assert.Equal(145, RemoteOpsCentre.BudgetFor(10));
        }

        [Fact]
        public void Autopilot_CostsNothingButSpillsAndDelays()
        {
            Assert.Equal(0, RemoteOpsCentre.CostFor("auto"));
            Assert.Equal(0.08, RemoteOpsCentre.FeedSpillFraction("auto"));
            Assert.Equal(0.0, RemoteOpsCentre.FeedSpillFraction("watch"));
            Assert.Equal(1.5, RemoteOpsCentre.EscapeResponseMult("auto"));
            Assert.Equal(1.0, RemoteOpsCentre.EscapeResponseMult("watch"));
        }
    }
}
