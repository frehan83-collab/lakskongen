using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using Xunit;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// Post-tick phase pipeline (Phase 1 wave 3): storm impacts -> wellboat
/// execution -> balance mirror, running once per simulated day after
/// State.Day++ with registration order as execution order.
/// </summary>
public class PostTickPipelineTests
{
    private static SimulationEngine NewEngineWithSite(string siteId)
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 1);
        var zone = new Zone("zone-1");
        engine.State.Zones.Add(zone);
        engine.State.SeaSites.Add(new SeaSite(siteId, zone.Id));
        return engine;
    }

    [Fact]
    public void BalanceMirror_IsFreshAtTheEndOfEveryAdvanceCall()
    {
        // The last post-tick phase re-syncs the mirror every day, so even
        // wellboat money moved after the in-day mirror is reflected before
        // the snapshot the UI reads.
        var engine = NewEngineWithSite("site-1");
        Assert.True(engine.StockSmolts("site-1", 500, "standard").Ok);
        Assert.True(engine.RequestWellboat("site-1", "harvest").Ok);

        engine.Advance(6); // wellboat harvest executes on day 5, mirror runs daily

        #pragma warning disable CS0618 // Balance mirror: save-compat field by design
        Assert.Equal(engine.FinanceManager.Balance, engine.State.Balance);
        #pragma warning restore CS0618
    }

    [Fact]
    public void ReInitialize_RebuildsDayState_NoLeakedNotes()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: true, seed: 7);
        engine.Advance(10);
        _ = engine.DrainDayNotes();

        engine.Initialize(enableEvents: true, seed: 7);
        Assert.Empty(engine.DrainDayNotes());
    }

    [Fact]
    public void WellboatExecution_StillRunsThroughThePhasePipeline()
    {
        var engine = NewEngineWithSite("site-1");
        Assert.True(engine.RequestWellboat("site-1", "rov").Ok);

        engine.Advance(2);

        Assert.True(engine.State.WellboatJobs.Count == 0, "job should run on due day via the pipeline");
        Assert.Contains(engine.DrainDayNotes(), n => n.Kind == "wellboat_rov" && n.Success);
    }
}