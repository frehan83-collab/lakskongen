using Xunit;
using LakskongenSim.Farm;

namespace LakskongenSim.Tests.StormPhase12
{
    /// <summary>Phase 1.4: strain registry sanity + genome wiring.</summary>
    public class SmoltStrainTests
    {
        [Fact]
        public void Registry_HasFiveStrains()
        {
            Assert.Equal(5, SmoltStrainRegistry.All.Count);
        }

        [Fact]
        public void Get_UnknownFallsBackToStandard()
        {
            Assert.Equal("standard", SmoltStrainRegistry.Get("nope").Id);
        }

        [Fact]
        public void Postsmolt_OutgrowsStandard()
        {
            var std = SmoltStrainRegistry.Get("standard").BuildGenome();
            var post = SmoltStrainRegistry.Get("postsmolt").BuildGenome();
            Assert.True(post.GetGrowthMultiplier() > std.GetGrowthMultiplier());
            Assert.True(SmoltStrainRegistry.Get("postsmolt").ReleaseWeightKg > 0.2);
        }
    }
}
