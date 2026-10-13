using System;
using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Farm;
using Xunit;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// Paid lice mitigation must act on the lice that are counted and regulated.
/// Skirts, containment and barriers used to feed only the company-wide rollup
/// model, so a protected site's own count (traffic lights, Mattilsynet orders,
/// forced delousing) was identical to an unprotected one (measured: 0.7802 vs
/// 0.7802 lice/fish after 60 days of pressure).
/// </summary>
public class SiteLiceMitigationTests
{
    private static double SiteLiceAfterPressure(Action<SeaSite> protect)
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 77);
        var site = new SeaSite("site-lice", "zone-lice") { MABTonnes = 780 };
        protect(site);
        engine.State.SeaSites.Add(site);
        var pop = new FishPopulation { Id = "site-lice-pen" };
        pop.InjectDependencies(engine.Growth, engine.Mortality);
        pop.Cohorts.Add(new FishPopulation.Cohort(200, 50_000, 1.0, LifeStage.Seawater));
        site.Stock(pop);
        engine.State.Populations.Add(pop);

        engine.Advance(150);
        site.Lice.SeedExternalInfection(2.0);
        engine.Advance(60);
        return site.Lice.AverageLicePerFish;
    }

    private static readonly double Unprotected = SiteLiceAfterPressure(_ => { });

    [Fact]
    public void Baseline_PressureProducesLiceOnTheSite()
    {
        Assert.True(Unprotected > 0.1, $"the pressure scenario must infest the site ({Unprotected:F4})");
    }

    [Fact]
    public void LiceSkirt_LowersTheSitesOwnLice()
    {
        double skirted = SiteLiceAfterPressure(s => s.HasLiceSkirt = true);
        Assert.True(skirted < Unprotected * 0.9, $"skirt: {skirted:F4} vs open {Unprotected:F4}");
    }

    [Fact]
    public void SemiClosedContainment_LowersTheSitesOwnLice()
    {
        double contained = SiteLiceAfterPressure(s => s.Containment = ContainmentLevel.SemiClosed);
        Assert.True(contained < Unprotected * 0.9, $"semi-closed: {contained:F4} vs open {Unprotected:F4}");
    }

    [Fact]
    public void PermeableBarrier_LowersTheSitesOwnLice()
    {
        double screened = SiteLiceAfterPressure(s => s.Barrier = BarrierKind.Smoltscreen);
        Assert.True(screened < Unprotected * 0.95, $"barrier: {screened:F4} vs open {Unprotected:F4}");
    }

    [Fact]
    public void Protections_Stack()
    {
        double skirted = SiteLiceAfterPressure(s => s.HasLiceSkirt = true);
        double both = SiteLiceAfterPressure(s =>
        {
            s.HasLiceSkirt = true;
            s.Containment = ContainmentLevel.SemiClosed;
        });
        Assert.True(both < skirted, $"skirt + semi-closed {both:F4} vs skirt alone {skirted:F4}");
    }
}
