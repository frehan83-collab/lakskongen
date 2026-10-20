using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using LakskongenSim.Health;
using LakskongenSim.Tests.Integration;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Farm;

/// <summary>
/// Roadmap F2-08 acceptance 6 and 7 (docs/FARM_TYPES_SPEC.md §3.3 "Lice and
/// zones"): the plot's external multiplier scales only the pressure from
/// outside (a unit's own lice reproduce as on any sea site), offshore units in
/// one production area infect each other, a fjord zone's traffic-light mean
/// carries a unit's larval output but never its fish, and building a unit never
/// makes a neighbouring fjord site's lice pressure or its zone's colour better.
/// </summary>
public class OffshoreLiceCouplingTests
{
    private readonly ITestOutputHelper _out;

    public OffshoreLiceCouplingTests(ITestOutputHelper output) => _out = output;

    private static SeaLiceModel SplitModel(double outsideMultiplier)
    {
        var model = new SeaLiceModel
        {
            ChalimusPerFish = 0.2,
            PreAdultPerFish = 0.1,
            AdultFemalePerFish = 0.3,
            AdultMalePerFish = 0.3,
            CopepodidDensity = 4.0,
            SeparatesExternalPressure = true,
            OwnPressureMultiplier = 1.1,
            ExternalOnlyMultiplier = outsideMultiplier,
        };
        model.SetEnvironment(10.0, 34.0, 0.2, 15.0);
        return model;
    }

    /// <summary>
    /// Acceptance 7, the model: with the split on, a unit's own copepodids infect
    /// at the season's pressure whatever the plot's multiplier (the lice of a unit
    /// with no outside seeds develop identically at 0.12 and at 1.0), while the
    /// copepodids that arrive from outside infect in proportion to it; a fjord
    /// site's model (no split) still scales all of its infection by its one
    /// multiplier, and its save carries none of the split's fields.
    /// </summary>
    [Fact]
    public void ThePlotsMultiplier_ScalesTheOutsidePoolOnly()
    {
        var low = SplitModel(0.12);
        var high = SplitModel(1.0);
        for (int d = 0; d < 60; d++)
        {
            low.AdvanceDay();
            high.AdvanceDay();
        }
        Assert.Equal(high.AdultFemalePerFish, low.AdultFemalePerFish);
        Assert.Equal(high.AverageLicePerFish, low.AverageLicePerFish);

        // Outside copepodids only: the new infection scales with the multiplier
        // (against a model whose outside multiplier is 0: the same day without it).
        var models = new[] { SplitModel(0.0), SplitModel(0.12), SplitModel(1.0) };
        foreach (var m in models)
        {
            m.CopepodidDensity = 0.0;
            m.ChalimusPerFish = m.PreAdultPerFish = m.AdultFemalePerFish = 0.0;
            m.AdultMalePerFish = 0.05; // males lay no eggs: the own cloud stays empty
            m.SeedExternalInfection(0.4);
            m.ChalimusPerFish = 0.0;
            Assert.Equal(4.0, m.ExternalCopepodidDensity, 12);
            Assert.Equal(0.0, m.CopepodidDensity);
            m.AdvanceDay();
        }
        double lowGain = models[1].ChalimusPerFish - models[0].ChalimusPerFish;
        double highGain = models[2].ChalimusPerFish - models[0].ChalimusPerFish;
        Assert.True(highGain > 0.0);
        Assert.Equal(0.12, lowGain / highGain, 6);

        // A fjord site's model: no split, one multiplier on all of its infection, nothing new in its save.
        var fjord = new SeaLiceModel { CopepodidDensity = 4.0, AdultFemalePerFish = 0.3, ChalimusPerFish = 0.0 };
        fjord.SetEnvironment(10.0, 34.0, 0.2, 15.0);
        fjord.SetExternalPressure(0.5);
        var fjordDouble = new SeaLiceModel { CopepodidDensity = 4.0, AdultFemalePerFish = 0.3, ChalimusPerFish = 0.0 };
        fjordDouble.SetEnvironment(10.0, 34.0, 0.2, 15.0);
        fjordDouble.SetExternalPressure(1.0);
        fjord.AdvanceDay();
        fjordDouble.AdvanceDay();
        Assert.True(fjordDouble.ChalimusPerFish > fjord.ChalimusPerFish * 1.9);
        string json = System.Text.Json.JsonSerializer.Serialize(fjord, SaveJson.Options);
        Assert.DoesNotContain("SeparatesExternalPressure", json);
        Assert.DoesNotContain("ExternalCopepodidDensity", json);
    }

    private static (SimulationEngine Engine, SeaSite A, SeaSite B) TwoUnits(int seed, bool infectA)
    {
        var engine = OffshoreTestUnits.RichEngine(seed);
        Assert.True(engine.BuildOffshoreFarm("A", OffshoreType.SemiSubmersible).Ok);
        Assert.True(engine.BuildOffshoreFarm("B", OffshoreType.ShipShaped).Ok); // 8.0 m: off_2
        foreach (var u in engine.OffshoreUnits) u.Offshore!.ConstructionDays = 1;
        engine.Advance(3);
        var a = engine.OffshoreUnits.Single(u => u.PlotId == "off_1");
        var b = engine.OffshoreUnits.Single(u => u.PlotId == "off_2");
        Assert.True(engine.StockOffshoreFarm(a.Id, 300_000).Ok);
        Assert.True(engine.StockOffshoreFarm(b.Id, 300_000).Ok);
        engine.IncidentHazardOverrideForTests = (s, k) => 0.0;
        if (infectA)
        {
            a.Lice.AdultFemalePerFish = 3.0;
            a.Lice.CopepodidDensity = 30.0;
        }
        return (engine, a, b);
    }

    /// <summary>
    /// Acceptance 7, the engine: each unit's model runs split, its own pressure
    /// the season's and its outside pressure the season × its plot's
    /// multiplier (0.12 at off_1); two units in one production area infect each
    /// other: the clean unit B gets more lice when unit A carries a heavy load
    /// than when A is clean, through its own cloud (outside the multiplier).
    /// </summary>
    [Fact]
    public void TwoUnitsInOneArea_InfectEachOther_OutsideThePlotsMultiplier()
    {
        var (engine, a, b) = TwoUnits(121, infectA: true);
        var (control, _, cb) = TwoUnits(121, infectA: false);
        Assert.Equal(engine.ProductionAreaOfSite(a), engine.ProductionAreaOfSite(b));
        engine.Advance(1);
        control.Advance(1);
        double seasonal = SimulationEngine.SeasonalLicePressure(engine.State.Day - 1);
        Assert.True(a.Lice.SeparatesExternalPressure && b.Lice.SeparatesExternalPressure);
        Assert.Equal(seasonal, a.Lice.OwnPressureMultiplier, 12);
        Assert.Equal(seasonal * engine.ExternalLicePressureFor(a), a.Lice.ExternalOnlyMultiplier, 12);
        Assert.Equal(0.12, engine.ExternalLicePressureFor(a), 12);
        for (int d = 0; d < 60; d++)
        {
            a.Lice.AdultFemalePerFish = Math.Max(a.Lice.AdultFemalePerFish, 3.0);
            engine.Advance(1);
            control.Advance(1);
        }
        _out.WriteLine($"B with an infected neighbour: {b.Lice.AverageLicePerFish:F4} lice/fish (own cloud {b.Lice.CopepodidDensity:F3}); with a clean one: {cb.Lice.AverageLicePerFish:F4} ({cb.Lice.CopepodidDensity:F3})");
        Assert.True(b.Lice.AverageLicePerFish > cb.Lice.AverageLicePerFish * 1.05, "unit B took no lice from unit A");
        Assert.True(b.Lice.CopepodidDensity > cb.Lice.CopepodidDensity, "A's larvae never reached B's own cloud");
    }

    /// <summary>
    /// The traffic light (spec §3.3): a fjord zone's mean carries its production
    /// area's units' larval output (fish × lice per fish × 0.08) on the zone's
    /// first population and never their fish, so a unit can only raise the
    /// mean; a game with no unit (every shipped scenario) is unchanged bit for bit.
    /// </summary>
    [Fact]
    public void AFjordZonesMean_CarriesTheUnitsOutput_NeverItsFish()
    {
        var (with, without, unit) = FjordGameWithAndWithout(122);
        foreach (var zone in with.State.Zones)
        {
            var (fishWith, liceWith) = with.ZoneLiceForTrafficLight(zone.Id);
            var (fishWithout, liceWithout) = without.ZoneLiceForTrafficLight(zone.Id);
            if (fishWith == 0) continue;
            Assert.Equal(fishWithout, fishWith);
            var output = with.OffshoreZoneLiceShare(zone.Id);
            Assert.Equal(0, output.Fish);
            double unitOutput = unit.Pens.Sum(p => p.Cohorts.Sum(c => c.Count) * p.SeaLiceLoad);
            Assert.Equal(0.08 * unitOutput, output.LiceWeightedFish, 9);
            Assert.Equal(liceWithout + output.LiceWeightedFish / fishWith, liceWith, 9);
            Assert.True(liceWith >= liceWithout);
            _out.WriteLine($"{zone.Id}: {liceWithout:F4} without the unit, {liceWith:F4} with it");
        }
    }

    private static (SimulationEngine With, SimulationEngine Without, SeaSite Unit) FjordGameWithAndWithout(int seed, bool events = false)
    {
        SimulationEngine Game(bool withUnit)
        {
            var engine = new SimulationEngine { FinanceManager = new FinanceManager(5_000_000_000m) };
            engine.Initialize(enableEvents: events, seed: seed);
            Assert.True(engine.StartScenario(PlayerScriptProbe.CareerScenarioId).Ok);
            engine.FinanceManager.AddRevenue(4_000_000_000m);
            Assert.True(engine.StockSmolts("po3_nord", 12_000, "standard").Ok);
            Assert.True(engine.StockSmolts("po3_sor", 10_000, "standard").Ok);
            if (withUnit)
            {
                Assert.True(engine.BuildOffshoreFarm("Hav", OffshoreType.SemiSubmersible).Ok);
                engine.OffshoreUnits.Single().Offshore!.ConstructionDays = 1;
            }
            engine.Advance(2);
            if (withUnit)
            {
                var u = engine.OffshoreUnits.Single();
                Assert.True(engine.StockOffshoreFarm(u.Id, 200_000).Ok);
                foreach (var pop in u.Pens) pop.SeaLiceLoad = 0.4;
                u.Lice.AdultFemalePerFish = 0.5;
            }
            return engine;
        }
        var with = Game(true);
        return (with, Game(false), with.OffshoreUnits.Single());
    }

    /// <summary>
    /// Acceptance 6 (probe, 20 seeds): po3_red_sone's two stocked fjord sites
    /// run a year with and without a stocked offshore unit carrying lice on
    /// off_1 in their production area. On every day of every seed, each fjord
    /// site's lice (adult females and all stages) and its zone's traffic-light
    /// mean are at least the game without the unit's, and its zone's colour is
    /// never better: the unit's fish never dilute the mean and its larvae only add.
    /// </summary>
    [Fact]
    public void BuildingAUnit_NeverImprovesANeighboursLiceOrTrafficLight_Over20Seeds()
    {
        int days = 365, compared = 0, worse = 0;
        foreach (int seed in Enumerable.Range(1, 20))
        {
            var (with, without, unit) = FjordGameWithAndWithout(4000 + seed, events: true);
            for (int d = 0; d < days; d++)
            {
                with.Advance(1);
                without.Advance(1);
                foreach (var site in without.FjordSites())
                {
                    var twin = with.State.SeaSites.Single(s => s.Id == site.Id);
                    Assert.True(twin.Lice.AdultFemalePerFish >= site.Lice.AdultFemalePerFish - 1e-12,
                        $"seed {seed} day {with.State.Day}: {site.Id} adult females {twin.Lice.AdultFemalePerFish} with the unit < {site.Lice.AdultFemalePerFish}");
                    Assert.True(twin.Lice.AverageLicePerFish >= site.Lice.AverageLicePerFish - 1e-12,
                        $"seed {seed} day {with.State.Day}: {site.Id} lice {twin.Lice.AverageLicePerFish} with the unit < {site.Lice.AverageLicePerFish}");
                    if (twin.Lice.AverageLicePerFish > site.Lice.AverageLicePerFish) worse++;
                    compared++;
                }
                foreach (var zone in without.State.Zones)
                {
                    var (_, liceWithout) = without.ZoneLiceForTrafficLight(zone.Id);
                    var (_, liceWith) = with.ZoneLiceForTrafficLight(zone.Id);
                    Assert.True(liceWith >= liceWithout - 1e-12, $"seed {seed} day {with.State.Day}: zone {zone.Id} mean {liceWith} < {liceWithout}");
                    Assert.True(Severity(with.TrafficLightSystem.GetZoneColor(zone.Id)) >= Severity(without.TrafficLightSystem.GetZoneColor(zone.Id)),
                        $"seed {seed} day {with.State.Day}: zone {zone.Id} greener with the unit");
                }
            }
            Assert.True(unit.Pens.Sum(p => p.TotalCount) > 0);
        }
        _out.WriteLine($"{compared} site-days compared over 20 seeds; the unit's larvae raised the fjord's lice on {worse}");
        Assert.True(worse > 0, "the unit's larvae never reached the fjord: the probe saw nothing");
    }

    private static int Severity(ZoneColor color) => color switch { ZoneColor.Red => 2, ZoneColor.Yellow => 1, _ => 0 };
}
