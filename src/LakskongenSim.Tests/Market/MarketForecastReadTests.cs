using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Tests.Integration;
using Xunit;

namespace LakskongenSim.Tests.Market;

/// <summary>
/// Re-review (bridge): the price forecast drew its noise from the Intelligence
/// stream, which also rolls the weekly news and rival volumes, so every UI read
/// (the attention inbox refreshes it each tick while open) changed the economy
/// and a loaded game diverged from the uninterrupted one. Same class as F521.
/// </summary>
public class MarketForecastReadTests
{
    [Fact]
    public void ReadingTheForecast_DrawsNoRng_AndIsRepeatable()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: true, seed: 17);
        engine.Advance(60);
        var before = engine.Rng.CaptureStates();

        var a = engine.MarketIntelligence.GetForecasts(75m, 70m, 5m);
        var b = engine.MarketIntelligence.GetForecasts(75m, 70m, 5m);

        Assert.Equal(before, engine.Rng.CaptureStates());
        Assert.Equal(4, a.Count);
        Assert.Equal(a.Select(f => f.PredictedPrice), b.Select(f => f.PredictedPrice));
    }

    [Fact]
    public void ReadingTheForecast_DoesNotChangeTheGame()
    {
        static string Run(bool readForecasts)
        {
            var engine = new SimulationEngine();
            engine.Initialize(enableEvents: true, seed: 23);
            for (int day = 0; day < 120; day++)
            {
                if (readForecasts)
                    engine.MarketIntelligence.GetForecasts(75m, 70m, 5m);
                engine.Advance(1);
            }
            return SaveStateComparer.Normalize(SaveStateComparer.Serialize(engine));
        }

        string untouched = Run(readForecasts: false);
        string watched = Run(readForecasts: true);
        Assert.True(untouched == watched, "forecast reads changed the game at "
            + SaveStateComparer.FirstDifference(untouched, watched));
    }
}
