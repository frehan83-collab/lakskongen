using LakskongenSim.Core;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using LakskongenSim.Tests.Integration;
using Xunit;

namespace LakskongenSim.Tests.Farm;

/// <summary>
/// F2-24 (roadmap, docs/FARM_TYPES_SPEC.md §5.4 "Tilt incident: a ≤ 3° list pose
/// until resolved"): the presenter lists an offshore unit while the engine has
/// a tilt incident open at it, and asks for that through the unit's handling
/// view (<see cref="SimulationEngine.GetOffshoreHandlingState"/>, the window
/// board's own read). A read the presenter polls must be a pure one: this holds
/// it to the bar the sea forecast's read is held to.
/// </summary>
public class OffshoreHandlingViewTests
{
    /// <summary>
    /// A thousand polls of the handling view, in the first weeks and well into
    /// the unit's life, move no random stream and change no save.
    /// </summary>
    [Fact]
    public void AThousandHandlingStatePolls_MoveNoStream_AndChangeNoSave()
    {
        var engine = new SimulationEngine { FinanceManager = new FinanceManager(5_000_000_000m) };
        engine.Initialize(enableEvents: true, seed: 17);
        var built = engine.BuildOffshoreFarm("Havmerd", OffshoreType.SemiSubmersible);
        Assert.True(built.Ok, built.Message);
        foreach (int days in new[] { 40, 400 })
        {
            engine.Advance(days);
            var streams = engine.Rng.CaptureStates();
            string save = SaveStateComparer.Serialize(engine);
            for (int i = 0; i < 1000; i++)
            {
                var view = engine.GetOffshoreHandlingState(built.Id);
                Assert.NotNull(view);
                Assert.Equal(built.Id, view!.SiteId);
            }
            Assert.Equal(streams, engine.Rng.CaptureStates());
            Assert.Equal(save, SaveStateComparer.Serialize(engine));
        }
    }

    /// <summary>
    /// The incident the presenter lists a unit for is the view's own: an unknown
    /// site has no view, and a unit with no incident open names none.
    /// </summary>
    [Fact]
    public void AUnitWithNoIncidentOpen_NamesNone_AndAnUnknownSiteHasNoView()
    {
        var engine = new SimulationEngine { FinanceManager = new FinanceManager(5_000_000_000m) };
        engine.Initialize(enableEvents: false, seed: 5);
        var built = engine.BuildOffshoreFarm("Havmerd", OffshoreType.SemiSubmersible);
        Assert.True(built.Ok, built.Message);
        engine.Advance(10);
        var view = engine.GetOffshoreHandlingState(built.Id)!;
        Assert.Equal("", view.ActiveIncidentId);
        Assert.Equal("", view.ActiveIncidentKind);
        Assert.Null(engine.GetOffshoreHandlingState("site_does_not_exist"));
    }
}
