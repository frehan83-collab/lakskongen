using Xunit;
using LakskongenSim.Processing;

namespace LakskongenSim.Tests.StormPhase12
{
    /// <summary>Phase 4.4: recipe math bounds.</summary>
    public class FeedRecipeTests
    {
        [Fact]
        public void Cheap_IsCheapestButGradeC()
        {
            Assert.True(FeedRecipe.CostPerKg(38, 0, 0) < FeedRecipe.CostPerKg(48, 60, 80));
            Assert.Equal("C", FeedRecipe.Grade(38, 0));
        }

        [Fact]
        public void Premium_GradeA_AndBetterFcr()
        {
            Assert.Equal("A", FeedRecipe.Grade(48, 70));
            Assert.True(FeedRecipe.FcrMult(48, 60, 60) < FeedRecipe.FcrMult(38, 0, 0));
            Assert.InRange(FeedRecipe.FcrMult(48, 60, 60), 0.89, 1.0);
        }

        [Fact]
        public void GradeC_HalvesVap()
        {
            Assert.Equal(1.06m, FeedRecipe.VapMult("C"));
            Assert.Equal(1.12m, FeedRecipe.VapMult("A"));
        }
    }
}
