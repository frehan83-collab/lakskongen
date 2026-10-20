using System.Text.Json;
using LakskongenSim.Core;
using LakskongenSim.Farm;
using LakskongenSim.Sustainability;
using Xunit;

namespace LakskongenSim.Tests.Farm;

/// <summary>
/// F457: the escape score counts escape incidents in a rolling 365-day
/// window. It used to add the site's escaped-fish counter, which was never
/// reset, to thresholds written for incidents (0 / 1-10 / more), so a single
/// storm escape of 11+ fish zeroed the category for the rest of the game.
/// </summary>
public class EscapeScoreTests
{
    [Fact]
    public void AStormEscape_CostsHalfTheEscapeScore_ForAYear_ThenItRecovers()
    {
        var (state, site) = NewState();
        state.Day = 100;
        site.RecordEscape(5_000, state.Day);

        Assert.Equal(10, EscapeScore(state));
        state.Day = 464; // still within 365 days of day 100
        Assert.Equal(10, EscapeScore(state));
        state.Day = 465;
        Assert.Equal(20, EscapeScore(state));
        state.Day = 500;
        Assert.Equal(20, EscapeScore(state));
        Assert.Equal(5_000, site.EscapesThisYear); // the cumulative fish count the balance probe reads
    }

    [Fact]
    public void MoreThanTenIncidentsInAYear_ZeroTheCategory()
    {
        var (state, site) = NewState();
        for (int day = 1; day <= 11; day++)
            site.RecordEscape(3, day);
        state.Day = 11;

        Assert.Equal(0, EscapeScore(state));
        Assert.Equal(33, site.EscapedFishInLastYear(state.Day));
    }

    [Fact]
    public void EscapeIncidents_SurviveASaveRoundTrip()
    {
        var (state, site) = NewState();
        site.RecordEscape(1_200, 40);

        string json = JsonSerializer.Serialize(state, SaveJson.Options);
        var loaded = JsonSerializer.Deserialize<GameState>(json, SaveJson.Options)!;

        var incident = Assert.Single(loaded.SeaSites[0].EscapeIncidents);
        Assert.Equal(40, incident.Day);
        Assert.Equal(1_200, incident.Fish);
    }

    private static (GameState state, SeaSite site) NewState()
    {
        var state = new GameState();
        var site = new SeaSite("site-f457", "zone-f457");
        state.SeaSites.Add(site);
        return (state, site);
    }

    private static int EscapeScore(GameState state)
        => new SustainabilityManager(new SustainabilityConfig()).GetComponentScores(state)["EscapePrevention"];
}
