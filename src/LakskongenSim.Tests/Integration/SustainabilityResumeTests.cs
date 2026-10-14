using LakskongenSim.Core;
using Xunit;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// A storyteller regulatory penalty moves the sustainability manager's live
/// score between the weekly updates without touching the state's mirror. A
/// save rebuilt the manager from the mirror, so the resumed game skipped the
/// next certification change and its price multiplier: po3_red_sone seeds 75
/// and 156 of 1-400 diverged after a day-40 save (the market price on day 43,
/// 93.10 vs 88.00 and 103.05 vs 88.00 NOK/kg), which made
/// InitializeSeedTests.UnseededGame_RecordsTheSeedItRolled_AndASaveResumesExactly
/// fail on about one random seed in 200.
/// </summary>
public class SustainabilityResumeTests
{
    [Theory]
    [InlineData(75)]
    [InlineData(156)]
    public void AGameSavedAfterARegulatoryPenalty_ResumesExactly(int seed)
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: true, seed: seed);
        Assert.True(engine.StartScenario("po3_red_sone").Ok);
        engine.Advance(40);
        // The penalty has moved the live score off the state's weekly mirror.
        Assert.NotEqual(engine.State.SustainabilityScore, engine.SustainabilityManager.CurrentScore);

        var resumed = SaveStateComparer.Resume(SaveStateComparer.Serialize(engine), events: true);
        Assert.Equal(engine.SustainabilityManager.CurrentScore, resumed.SustainabilityManager.CurrentScore);
        Assert.Equal(engine.SustainabilityManager.CurrentCertification, resumed.SustainabilityManager.CurrentCertification);

        engine.Advance(40);
        resumed.Advance(40);
        string a = SaveStateComparer.Normalize(SaveStateComparer.Serialize(engine));
        string b = SaveStateComparer.Normalize(SaveStateComparer.Serialize(resumed));
        Assert.True(a == b, "resumed game diverged at " + SaveStateComparer.FirstDifference(a, b));
    }
}
