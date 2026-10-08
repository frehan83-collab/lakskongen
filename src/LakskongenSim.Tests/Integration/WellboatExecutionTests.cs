using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using Xunit;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// Wellboat booking + engine-side execution (Phase 1 wave 2): the booking
/// verb is a library command, and due jobs execute inside Advance after
/// State.Day++ — the same path both time controls now share.
/// </summary>
public class WellboatExecutionTests
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

    private static SimulationEngine NewStockedEngine(string siteId, int count = 500)
    {
        var engine = NewEngineWithSite(siteId);
        Assert.True(engine.StockSmolts(siteId, count, "standard").Ok);
        return engine;
    }

    private static List<DayNote> Drain(SimulationEngine engine) => engine.DrainDayNotes();

    [Fact]
    public void RequestWellboat_UnknownSite_FailsWithSiteNotFound()
    {
        var engine = NewEngineWithSite("site-1");
        var result = engine.RequestWellboat("nope", "rov");
        Assert.False(result.Ok);
        Assert.Equal("site_not_found", result.ErrorKey);
        Assert.False(engine.State.WellboatJobs.Any());
    }

    [Fact]
    public void RequestWellboat_BooksJob_ChargesHire_LogsEvent()
    {
        var engine = NewEngineWithSite("site-1");
        decimal before = engine.FinanceManager.Balance;

        var result = engine.RequestWellboat("site-1", "rov");

        Assert.True(result.Ok);
        Assert.True(engine.FinanceManager.Balance < before, "hire was free");
        var job = Assert.Single(engine.State.WellboatJobs);
        Assert.Equal("site-1", job.SiteId);
        Assert.Equal("rov", job.Kind);
        Assert.Equal(engine.State.Day + 2, job.DueDay);
        Assert.True(engine.State.EventLog.Any(e => e.Name == "Brønnbåt bestilt"));
    }

    [Fact]
    public void RequestWellboat_DuplicateBooking_FailsWithAlreadyBooked()
    {
        var engine = NewEngineWithSite("site-1");
        Assert.True(engine.RequestWellboat("site-1", "rov").Ok);
        var result = engine.RequestWellboat("site-1", "rov");
        Assert.False(result.Ok);
        Assert.Equal("already_booked", result.ErrorKey);
        Assert.Single(engine.State.WellboatJobs);
    }

    [Fact]
    public void Advance_ExecutesRovJob_OnDueDay_WithNote()
    {
        var engine = NewEngineWithSite("site-1");
        double integrityBefore = engine.State.SeaSites[0].NetIntegrityPct;
        Assert.True(engine.RequestWellboat("site-1", "rov").Ok);

        engine.Advance(2);
        Assert.True(engine.State.WellboatJobs.Count == 0, "job should run on due day");
        var notes = Drain(engine);
        Assert.Contains(notes, n => n.Kind == "wellboat_rov" && n.Success);
        Assert.True(engine.State.EventLog.Any(e => e.Name == "ROV-inspeksjon"));
        Assert.True(engine.State.SeaSites[0].NetIntegrityPct >= integrityBefore);
    }

    [Fact]
    public void Advance_ExecutesHarvestJob_ViaWellboat()
    {
        var engine = NewStockedEngine("site-1");
        Assert.True(engine.RequestWellboat("site-1", "harvest").Ok);

        engine.Advance(6); // eta 5 days

        Assert.True(engine.State.WellboatJobs.Count == 0);
        Assert.Contains(Drain(engine), n => n.Kind == "wellboat_harvest" && n.Success);
        Assert.True(engine.State.SeaSites[0].Pens.Count == 0, "fish should be harvested");
        Assert.True(engine.State.EventLog.Any(e => e.Name == "Brønnbåt ankommet"));
        Assert.True(engine.State.EventLog.Any(e => e.Name == "Harvest"));
    }

    [Fact]
    public void Advance_ExecutesFreshwaterJob_LogsTreatment()
    {
        var engine = NewStockedEngine("site-1");
        Assert.True(engine.RequestWellboat("site-1", "freshwater").Ok);

        engine.Advance(5); // eta 4 days

        Assert.True(engine.State.WellboatJobs.Count == 0);
        Assert.Contains(Drain(engine), n => n.Kind == "wellboat_freshwater" && n.Success);
        Assert.True(engine.State.EventLog.Any(e => e.Name == "Ferskvannsbehandling"));
    }

    [Fact]
    public void Advance_ExecutesDelouseJob_ViaWellboat()
    {
        var engine = NewEngineWithSite("site-1");
        // January water can fall below every treatment window; advance into
        // spring before booking so the execution lands inside the hydrolicer
        // window (4-18°C) and the success path is the one under test.
        engine.Advance(120);
        Assert.True(engine.StockSmolts("site-1", 500, "standard").Ok);
        Assert.True(engine.RequestWellboat("site-1", "delouse", "hydrolicer").Ok);

        engine.Advance(4); // eta 3 days

        Assert.True(engine.State.WellboatJobs.Count == 0);
        Assert.Contains(Drain(engine), n => n.Kind == "wellboat_delouse" && n.Success);
        Assert.True(engine.State.EventLog.Any(e => e.Name == "Delousing Treatment"));
    }
}