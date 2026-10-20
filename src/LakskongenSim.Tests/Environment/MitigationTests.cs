using Xunit;
using LakskongenSim.Health;

namespace LakskongenSim.Tests.StormPhase12
{
    /// <summary>Phase 1.5: skirt coverage reduces settlement.</summary>
    public class MitigationTests
    {
        [Fact]
        public void FullSkirtCoverage_CutsNewSettlement()
        {
            var open = new SeaLiceModel();
            open.PreventiveCoverage01 = 0.0;
            var skirted = new SeaLiceModel();
            skirted.PreventiveCoverage01 = 1.0;
            open.SeedExternalInfection(1.0);
            skirted.SeedExternalInfection(1.0);
            double before = open.ChalimusPerFish;
            open.AdvanceDay();
            skirted.AdvanceDay();
            double openGain = open.ChalimusPerFish - before;
            double skirtGain = skirted.ChalimusPerFish - before;
            // Skirted settlement ≈ 30% of open (70% blocked), minus transitions.
            Assert.True(skirtGain < openGain);
        }
    }
}
