using System.Linq;
using Xunit;
using LakskongenSim.Core;
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
            var lice = new SeaLiceModel();
            lice.Reset(0.5);
            var analytics = new AnalyticsTracker(state.DailySnapshots);
            double liceBefore = lice.AverageLicePerFish;

            var fc = ForecastTwin.Project(state, lice, 10.0, analytics);

            Assert.Equal(7, fc.BiomassKg.Length);
            Assert.Equal(7, fc.LicePerFish.Length);
            Assert.Equal(7, fc.CashNok.Length);
            Assert.Equal(liceBefore, lice.AverageLicePerFish, precision: 9);
            Assert.Empty(state.Populations);
            Assert.Equal("low", fc.Confidence); // no history
        }

        /// <summary>
        /// Review F521/F835: with real cohorts, projecting must not draw from any
        /// engine RNG stream (the old twin consumed the live growth stream on every
        /// UI read), must not touch cohort weights, and must be repeatable.
        /// </summary>
        [Fact]
        public void Project_WithStockedCohorts_DrawsNoRngAndIsRepeatable()
        {
            var engine = new SimulationEngine();
            engine.Initialize(enableEvents: false, seed: 11);
            Assert.True(engine.StartScenario("po3_red_sone").Ok);
            engine.Advance(5);
            Assert.NotEmpty(engine.State.Populations.SelectMany(p => p.Cohorts));
            var streamsBefore = engine.Rng.CaptureStates();
            var weightsBefore = engine.State.Populations.SelectMany(p => p.Cohorts).Select(c => c.MeanWeightKg).ToList();

            var a = ForecastTwin.Project(engine.State, engine.SeaLice, 10.0, engine.Analytics);
            var b = ForecastTwin.Project(engine.State, engine.SeaLice, 10.0, engine.Analytics);

            Assert.Equal(streamsBefore, engine.Rng.CaptureStates());
            Assert.Equal(weightsBefore, engine.State.Populations.SelectMany(p => p.Cohorts).Select(c => c.MeanWeightKg).ToList());
            Assert.Equal(a.BiomassKg, b.BiomassKg);
            Assert.True(a.BiomassKg[^1] > a.BiomassKg[0], "projection should grow stocked fish at 10 °C");
        }
    }
}
