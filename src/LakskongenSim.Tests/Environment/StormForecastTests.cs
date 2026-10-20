using Xunit;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Core;

namespace LakskongenSim.Tests.StormPhase12
{
    /// <summary>
    /// Phase 1.2 regression tests: honest Markov-chain storm forecast and
    /// net-integrity defaults/migration.
    /// </summary>
    public class StormForecastTests
    {
        private static WeatherSystem MakeWeather(int seed = 0)
        {
            var rng = new SimRng(seed);
            return new WeatherSystem(rng.Stream(SimRngStreams.Weather));
        }

        [Fact]
        public void StormRisk_IsProbability()
        {
            var wx = MakeWeather(42);
            double risk = wx.StormRiskNext3Days();
            Assert.InRange(risk, 0.0, 1.0);
        }

        [Fact]
        public void StormRisk_HigherFromStormThanFromCalm()
        {
            var calm = MakeWeather(1);
            calm.ForceWeatherState(WeatherState.Calm);
            var storm = MakeWeather(1);
            storm.ForceWeatherState(WeatherState.Storm);
            // Same day-of-year (day 0) → comparable rows.
            Assert.True(storm.StormRiskNext3Days() >= calm.StormRiskNext3Days());
        }

        [Fact]
        public void SeaSite_IntegrityDefaultsTo100()
        {
            var site = new SeaSite("test-site", "zone-1");
            Assert.Equal(100.0, site.NetIntegrityPct);
        }
    }
}
