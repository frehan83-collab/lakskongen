using System.Linq;
using System.Text.Json.Nodes;
using LakskongenSim.Core;
using StateHashProbe;
using Xunit;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// Found by F2-05's day-by-day save/load test: the market's daily price queue
/// (the weekly mean the HUD shows and the price chart's history) was not
/// saved, so a loaded game's weekly mean was the mean of one or two days for
/// a week and its chart lost the history (the traced market group differed
/// for six days after every load; the endpoint gates healed by then). The
/// runtime snapshot now carries the queue (<c>MktDailyPrices</c>); a save
/// from before it loads as it did.
/// </summary>
public class MarketDailyPricesSaveTests
{
    private static SimulationEngine Game(int seed)
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: true, seed: seed);
        engine.State.RandomSeed = seed;
        Assert.True(engine.StartScenario(PlayerScriptProbe.CareerScenarioId).Ok);
        return engine;
    }

    [Theory]
    [InlineData(7)]
    [InlineData(13)]
    public void ALoadedGame_KeepsTheWeeklyMeanAndThePriceHistory_EveryDay(int seed)
    {
        var uninterrupted = Game(seed);
        uninterrupted.Advance(100);
        var resumed = SaveStateComparer.Resume(SaveStateComparer.Serialize(uninterrupted), events: true);
        Assert.Equal(uninterrupted.MarketSimulation.GetPriceHistory(), resumed.MarketSimulation.GetPriceHistory());
        Assert.Equal(101, resumed.MarketSimulation.GetPriceHistory().Length); // the opening print and 100 days
        for (int day = 0; day < 14; day++)
        {
            uninterrupted.Advance(1);
            resumed.Advance(1);
            Assert.Equal(uninterrupted.MarketSimulation.WeeklyAvgPrice, resumed.MarketSimulation.WeeklyAvgPrice);
            Assert.Equal(GoldenTraceState.Capture(uninterrupted).DayHash(), GoldenTraceState.Capture(resumed).DayHash());
        }
        Assert.Equal(uninterrupted.MarketSimulation.GetPriceHistory(), resumed.MarketSimulation.GetPriceHistory());
    }

    /// <summary>A save without the queue (every save before F2-05) loads and runs as before: the queue starts afresh.</summary>
    [Fact]
    public void ASaveWithoutTheQueue_StillLoads_AndStartsTheQueueAfresh()
    {
        var engine = Game(3);
        engine.Advance(40);
        var json = JsonNode.Parse(SaveStateComparer.Serialize(engine))!.AsObject();
        Assert.True(json["Runtime"]!.AsObject().Remove("MktDailyPrices"));
        var resumed = SaveStateComparer.Resume(json.ToJsonString(SaveJson.Options), events: true);
        Assert.Single(resumed.MarketSimulation.GetPriceHistory());
        resumed.Advance(10);
        Assert.Equal(11, resumed.MarketSimulation.GetPriceHistory().Length);
    }

    /// <summary>The queue keeps at most a year, oldest first, through a save.</summary>
    [Fact]
    public void TheQueue_KeepsAYear_ThroughASave()
    {
        var engine = Game(5);
        engine.Advance(400);
        var history = engine.MarketSimulation.GetPriceHistory();
        Assert.Equal(365, history.Length);
        var resumed = SaveStateComparer.Resume(SaveStateComparer.Serialize(engine), events: true);
        Assert.Equal(history, resumed.MarketSimulation.GetPriceHistory());
        Assert.Equal(history.Last(), engine.MarketSimulation.PricePerKg);
    }
}
