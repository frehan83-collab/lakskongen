using Xunit;
using LakskongenSim.Core;
using LakskongenSim.Biology;
using LakskongenSim.Health;
using LakskongenSim.Optimization;

namespace LakskongenSim.Tests.StormPhase12
{
    /// <summary>Phase 4.5: projection is read-only and well-formed.</summary>
    public class ForecastTwinTests
    {
        [Fact]
        public void Project_SevenDays_AndLiveStateUntouched()
        {
            var state = new GameState();
            var growth = new GrowthModel();
            var lice = new SeaLiceModel();
            lice.Reset(0.5);
            var analytics = new AnalyticsTracker(state.DailySnapshots);
            double liceBefore = lice.AverageLicePerFish;

            var fc = ForecastTwin.Project(state, growth, lice, 10.0, analytics);

            Assert.Equal(7, fc.BiomassKg.Length);
            Assert.Equal(7, fc.LicePerFish.Length);
            Assert.Equal(7, fc.CashNok.Length);
            Assert.Equal(liceBefore, lice.AverageLicePerFish, precision: 9);
            Assert.Empty(state.Populations);
            Assert.Equal("low", fc.Confidence); // no history
        }
    }
}
