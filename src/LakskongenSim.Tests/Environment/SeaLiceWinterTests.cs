using Xunit;
using LakskongenSim.Health;

namespace LakskongenSim.Tests.StormPhase12
{
    /// <summary>Winter lice dynamics: development must slow but never freeze
    /// in cold water (the Q10 x Gaussian-optimal compound stalled everything
    /// below ~5C before the MIN_WINTER_ENV_FACTOR floor).</summary>
    public class SeaLiceWinterTests
    {
        private static SeaLiceModel ColdModel(double chalimus = 1.0)
        {
            var m = new SeaLiceModel();
            m.SetEnvironment(temperatureC: 4.0, salinityPSU: 34.0, currentSpeedMs: 0.15, stockingDensityKgM3: 20.0);
            m.SeedExternalInfection(chalimus);
            m.AdvanceDay(); // settle the seed into chalimus
            return m;
        }

        [Fact]
        public void ColdWater_ChalimusStillMaturesOverWinter()
        {
            var m = ColdModel();
            for (int d = 0; d < 180; d++) m.AdvanceDay();
            double matured = m.PreAdultPerFish + m.AdultFemalePerFish + m.AdultMalePerFish;
            Assert.True(matured > 0.2,
                $"180d at 4C should mature a visible fraction of chalimus, got {matured:F3}");
        }

        [Fact]
        public void ColdWater_DevelopsSlowerThanOptimal()
        {
            var cold = ColdModel();
            var warm = new SeaLiceModel();
            warm.SetEnvironment(temperatureC: 12.0, salinityPSU: 34.0, currentSpeedMs: 0.15, stockingDensityKgM3: 20.0);
            warm.SeedExternalInfection(1.0);
            warm.AdvanceDay();
            for (int d = 0; d < 30; d++) { cold.AdvanceDay(); warm.AdvanceDay(); }
            double coldMatured = cold.PreAdultPerFish + cold.AdultFemalePerFish + cold.AdultMalePerFish;
            double warmMatured = warm.PreAdultPerFish + warm.AdultFemalePerFish + warm.AdultMalePerFish;
            Assert.True(coldMatured < warmMatured,
                $"cold ({coldMatured:F3}) must lag optimal ({warmMatured:F3})");
        }

        [Fact]
        public void ColdWater_NoExplosionWithoutTreatment()
        {
            var m = ColdModel();
            for (int d = 0; d < 180; d++) m.AdvanceDay();
            Assert.True(m.AverageLicePerFish < 5.0,
                $"winter grow-out must stay bounded, got {m.AverageLicePerFish:F3}");
        }
    }
}
