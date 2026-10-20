using Xunit;
using LakskongenSim.Finance;
using LakskongenSim.Environment;

namespace LakskongenSim.Tests.StormPhase12
{
    /// <summary>
    /// Phase 1.3 regression tests: assessment thresholds and one-shot MAB.
    /// </summary>
    public class TrafficLightTests
    {
        [Theory]
        [InlineData(0.1, "Green")]
        [InlineData(0.5, "Green")]
        [InlineData(0.7, "Yellow")]
        [InlineData(1.0, "Red")]
        [InlineData(2.0, "Red")]
        public void AssessZone_Thresholds(double lice, string expected)
        {
            var tls = new TrafficLightSystem();
            Assert.Equal(expected, tls.AssessZone("z1", lice).ToString());
        }

        [Fact]
        public void MabConsequence_AppliesOncePerChange()
        {
            var tls = new TrafficLightSystem();
            var zone = new Zone("z1", 8.0) { CarryingCapacityTonnes = 1000.0 };
            tls.AssessZone("z1", 0.1); // Green
            double f = tls.ApplyMABConsequence(zone);
            Assert.Equal(1.06, f, precision: 6);
            Assert.Equal(1060.0, zone.CarryingCapacityTonnes, precision: 6);
            // Same color again must NOT be auto-applied by callers; factor is stable.
            double f2 = TrafficLightSystem.MabFactorFor(tls.GetZoneColor("z1"));
            Assert.Equal(1.06, f2, precision: 6);
        }
    }
}
