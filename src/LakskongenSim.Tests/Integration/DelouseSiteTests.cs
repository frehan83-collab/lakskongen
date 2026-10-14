using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using Xunit;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// Contract + correctness tests for the moved delousing verb
/// (NEXT_LEVEL_PLAN Phase 1 wave 2). The flagship invariant: gill damage
/// lands ONCE per site, not once per pen (the old bridge loop multiplied the
/// identical dose-derived scalar by the pen count).
/// </summary>
public class DelouseSiteTests
{
    private static SimulationEngine NewEngineWithSite(string siteId)
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 1);
        var zone = new Zone("zone-1");
        // S2-02: a treatment runs in the site's own water. On 1 January that is
        // about 5 °C, below the Thermolicer profile's 6 °C minimum (the verb used
        // to read a placeholder 8 °C on day 0). These tests check the verb's
        // accounting, so the site's water is pinned at the 10 °C treatment
        // reference; SeaTemperatureCouplingTests checks the cold-water refusal.
        zone.SetTemperature(10.0);
        engine.State.Zones.Add(zone);
        engine.State.SeaSites.Add(new SeaSite(siteId, zone.Id));
        return engine;
    }

    private static SimulationEngine NewStockedEngine(string siteId, int pens, int perPen)
    {
        var engine = NewEngineWithSite(siteId);
        for (int i = 0; i < pens; i++)
            Assert.True(engine.StockSmolts(siteId, perPen, "standard").Ok, $"stock {i} failed");
        return engine;
    }

    [Fact]
    public void DelouseSite_UnknownSite_FailsWithSiteNotFound()
    {
        var engine = NewEngineWithSite("site-1");
        var result = engine.DelouseSite("nope", "thermal");
        Assert.False(result.Ok);
        Assert.Equal("site_not_found", result.ErrorKey);
    }

    [Fact]
    public void DelouseSite_UnknownMethod_FailsBeforeAnyMutation()
    {
        var engine = NewStockedEngine("site-1", 1, 500);
        double gillBefore = engine.State.SeaSites[0].GillScore;
        var result = engine.DelouseSite("site-1", "plasma_whip");
        Assert.False(result.Ok);
        Assert.Equal("unknown_method", result.ErrorKey);
        Assert.Equal(gillBefore, engine.State.SeaSites[0].GillScore);
    }

    [Fact]
    public void DelouseSite_EmptySite_FailsWithSiteEmpty()
    {
        var engine = NewEngineWithSite("site-1");
        var result = engine.DelouseSite("site-1", "thermal");
        Assert.False(result.Ok);
        Assert.Equal("site_empty", result.ErrorKey);
    }

    [Fact]
    public void DelouseSite_Unaffordable_FailsWithInsufficientFunds()
    {
        var engine = NewStockedEngine("site-1", 1, 500);
        engine.FinanceManager = new LakskongenSim.Finance.FinanceManager(10m);
        var result = engine.DelouseSite("site-1", "thermal");
        Assert.False(result.Ok);
        Assert.Equal("insufficient_funds", result.ErrorKey);
    }

    [Fact]
    public void DelouseSite_Success_ChargesProfileCostOnce()
    {
        var engine = NewStockedEngine("site-1", 1, 500);
        decimal before = engine.FinanceManager.Balance;
        var profile = DelousingSystem.GetProfile(LakskongenSim.Data.DelousingMethod.Thermolicer);
        decimal expected = 500 * profile.CostPerFish;

        var result = engine.DelouseSite("site-1", "thermal");

        Assert.True(result.Ok);
        Assert.Equal(before - expected, engine.FinanceManager.Balance);
        Assert.True(engine.State.EventLog.Any(e => e.Name == "Delousing Treatment"));
    }

    [Fact]
    public void DelouseSite_GillDamage_AppliedOncePerSite_NotPerPen()
    {
        // Same fish count, different pen layout: the gill hit must be
        // identical (dose-derived scalar, once per site), not multiplied
        // by the pen count as the old bridge loop did.
        var onePens = NewStockedEngine("one", 1, 500);
        var fourPens = NewStockedEngine("four", 4, 125);

        var r1 = onePens.DelouseSite("one", "thermal");
        var r4 = fourPens.DelouseSite("four", "thermal");
        Assert.True(r1.Ok && r4.Ok);

        double delta1 = 100.0 - onePens.State.SeaSites[0].GillScore;
        double delta4 = 100.0 - fourPens.State.SeaSites[0].GillScore;

        Assert.True(delta1 > 0.0, "gill damage should be non-zero for thermal");
        Assert.Equal(delta1, delta4);
    }

    [Fact]
    public void DelouseSite_TreatmentHarm_AppliesPerPen()
    {
        // Harm state is per-population biology and must still compound on
        // every pen — only the site-level gill score is deduplicated.
        var engine = NewStockedEngine("site-1", 4, 125);
        Assert.True(engine.DelouseSite("site-1", "thermal").Ok);

        foreach (var pop in engine.State.SeaSites[0].Pens)
            Assert.True(pop.TreatmentHarm.InjuryLoad > 0.0, "pen escaped treatment harm");
    }
}