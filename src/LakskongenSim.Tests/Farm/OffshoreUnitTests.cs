using System;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Farm.Offshore;
using LakskongenSim.Finance;
using LakskongenSim.Harvest;
using LakskongenSim.Tests.Integration;
using Xunit;

namespace LakskongenSim.Tests.Farm;

/// <summary>
/// The offshore unit as a sea site (roadmap F2-03, docs/FARM_TYPES_SPEC.md
/// §3.1 decision 1 and §3.2): the build creates an offshore <see cref="SeaSite"/>
/// with its class's zones on a slot its class is rated for; its fish live in
/// the zones and in the site lice model, harvest and escapes like a sea site's;
/// its own day books its crew, energy and carbon. Replaces
/// FacilityProductionTests' offshore section (spec §3.10) with everything it
/// asserted and more; F2-07 grows this file (operations, logistics).
/// </summary>
public class OffshoreUnitTests
{
    private static SimulationEngine RichEngine(int seed, bool events = false)
    {
        var engine = new SimulationEngine { FinanceManager = new FinanceManager(5_000_000_000m) };
        engine.Initialize(enableEvents: events, seed: seed);
        return engine;
    }

    private static SeaSite BuildOperational(SimulationEngine engine, OffshoreType type = OffshoreType.SemiSubmersible)
    {
        var built = engine.BuildOffshoreFarm("Havmerd", type);
        Assert.True(built.Ok, built.Message);
        var unit = engine.OffshoreUnits.Single(u => u.Id == built.Id);
        unit.Offshore!.ConstructionDays = 1;
        engine.Advance(2);
        Assert.True(unit.Offshore.IsOperational);
        return unit;
    }

    [Fact]
    public void Build_CreatesAnOffshoreSeaSite_WithItsClassZones_OnTheFirstRatedSlot()
    {
        var engine = RichEngine(55);
        decimal before = engine.FinanceManager.Balance;
        var built = engine.BuildOffshoreFarm("Ytre kyst 1", OffshoreType.SemiSubmersible);
        Assert.True(built.Ok, built.Message);

        var unit = Assert.Single(engine.OffshoreUnits);
        Assert.Equal(built.Id, unit.Id);
        Assert.Equal("offshore_1", unit.Id);
        Assert.Contains(unit, engine.State.SeaSites);
        Assert.Empty(engine.OffshoreFarms);
        Assert.Equal(SiteKind.Offshore, unit.Kind);
        Assert.True(unit.IsOffshore);
        Assert.Equal("Ytre kyst 1", unit.Label);
        // OF1-class: off_1 is the one slot it is rated for (5.5 m ≥ Hs100 5.0; off_2 is 6.5 m).
        Assert.Equal("off_1", unit.PlotId);
        Assert.Equal("off_1", unit.ZoneId);
        Assert.Equal(unit.Id, engine.FindPlot("off_1")!.OwnerUnitId);
        Assert.DoesNotContain(engine.State.Zones, z => z.Id == unit.ZoneId);
        var structure = unit.Offshore!;
        Assert.Equal(OffshoreType.SemiSubmersible, structure.FarmType);
        Assert.Equal(5.5, structure.DesignHsM);
        Assert.Equal(7, structure.CrewRotationDays);
        Assert.Equal(12, structure.EmployeeCount);
        Assert.Equal(6_240.0, unit.MABTonnes);
        Assert.Equal(SimulationEngine.OffshoreWatchMode, unit.WatchMode);
        // Three zones of a third of OF1's 250,000 m³ each, numbered from 1, empty.
        Assert.Equal(3, unit.PhysicalPens.Count);
        Assert.Equal(new[] { 1, 2, 3 }, unit.PhysicalPens.Select(p => p.Number));
        Assert.All(unit.PhysicalPens, p => Assert.Equal(250_000.0 / 3, p.VolumeM3, 9));
        Assert.All(unit.PhysicalPens, p => Assert.True(p.IsEmpty));
        Assert.Equal(250_000.0, unit.PhysicalPens.Sum(p => p.VolumeM3), 6);
        // Paid as capex, construction started today, not yet operational.
        Assert.Equal(OffshoreUnit.LegacyCapexNok(OffshoreType.SemiSubmersible), before - engine.FinanceManager.Balance);
        Assert.Equal(engine.State.Day, structure.ConstructionStartDay);
        Assert.False(structure.IsOperational);
        Assert.Equal("not_operational", engine.StockOffshoreFarm(unit.Id, 1_000).ErrorKey);
    }

    [Fact]
    public void Build_PlacesEachClassOnlyOnASlotItIsRatedFor_AndRefusesBeforeAnyMutation_WhenNoneIsFree()
    {
        var engine = RichEngine(56);
        Assert.True(engine.BuildOffshoreFarm("", OffshoreType.SemiSubmersible).Ok);
        // A second OF1-class unit has no rated slot left: off_2's 6.5 m and hav_1's 13 m exceed 5.5 m.
        decimal before = engine.FinanceManager.Balance;
        int plots = engine.State.SitePlots.Count;
        var refused = engine.BuildOffshoreFarm("", OffshoreType.SemiSubmersible);
        Assert.False(refused.Ok);
        Assert.Equal(SimulationEngine.NoFreePlotKey, refused.ErrorKey);
        Assert.Equal(before, engine.FinanceManager.Balance);
        Assert.Single(engine.OffshoreUnits);
        Assert.Equal(plots, engine.State.SitePlots.Count);
        // A Havfarm-class ship (8.0 m) fits off_2 and has 6 zones of ~69,000 m³ and 10,000 t MTB.
        var ship = engine.BuildOffshoreFarm("", OffshoreType.ShipShaped);
        Assert.True(ship.Ok, ship.Message);
        var hull = engine.OffshoreUnits.Single(u => u.Id == ship.Id);
        Assert.Equal("off_2", hull.PlotId);
        Assert.Equal(6, hull.PhysicalPens.Count);
        Assert.All(hull.PhysicalPens, p => Assert.Equal(69_000.0, p.VolumeM3, 9));
        Assert.Equal(10_000.0, hull.MABTonnes);
        Assert.Equal(8.0, hull.Offshore!.DesignHsM);
        Assert.Equal("Skipsmerd 2", hull.Label);
        Assert.Equal(SimulationEngine.NoFreePlotKey, engine.BuildOffshoreFarm("", OffshoreType.ShipShaped).ErrorKey);
        // Every built unit stands on a slot whose 100-year sea it is rated for.
        foreach (var unit in engine.OffshoreUnits)
            Assert.True(engine.FindPlot(unit.PlotId)!.AcceptsDesignHs(unit.Offshore!.DesignHsM), unit.Id);
    }

    /// <summary>
    /// FacilityProductionTests.Offshore_StocksAndHarvests_AndCountsForTheRentTax,
    /// on the F2 unit: the stocking is one population in the unit's first zone
    /// and in the game's list; after 200 days the harvest takes every fish
    /// through the offshore wellboat, frees the zone and adds to the
    /// resource-rent base. More: the zone holds the population, the lice
    /// model counts the unit's fish at its plot's lower pressure, the fish
    /// grew in the unit's water.
    /// </summary>
    [Fact]
    public void Offshore_StocksAndHarvests_AndCountsForTheRentTax()
    {
        var engine = RichEngine(55);
        var unit = BuildOperational(engine);

        Assert.True(engine.StockOffshoreFarm(unit.Id, 50_000).Ok);
        var pen = Assert.Single(unit.Pens);
        Assert.Contains(pen, engine.State.Populations);
        Assert.Same(unit.PhysicalPens[0], unit.PenOf(pen));
        Assert.Equal((unit.Id, unit.ZoneId), (pen.SiteId, pen.ZoneId));
        Assert.Equal(HoldingKind.Offshore, engine.HoldingOf(pen).Kind);
        double startKg = pen.GetTotalBiomassKg();
        engine.Advance(200);
        Assert.True(pen.GetTotalBiomassKg() > startKg * 5, "the unit's fish did not grow");
        Assert.True(unit.LastLiceCountDay >= 0, "the site lice count never ran at the unit");
        Assert.True(unit.Lice.AverageLicePerFish > 0.0, "the unit's lice model is empty");

        decimal rentBase = engine.State.ResourceRentHarvestValueThisTaxYear;
        var harvest = engine.HarvestOffshoreFarm(unit.Id);
        Assert.True(harvest.Ok, harvest.Message);
        Assert.Empty(unit.Pens);
        Assert.True(unit.PhysicalPens[0].IsEmpty);
        Assert.True(engine.State.ResourceRentHarvestValueThisTaxYear > rentBase);
        Assert.Equal(HarvestRouteKind.OffshoreWellboat, engine.HarvestReports[^1].Route);
    }

    [Fact]
    public void Stocking_RefusesBeforeAnyMutation()
    {
        var engine = RichEngine(57);
        var unit = BuildOperational(engine);
        int populations = engine.State.Populations.Count;
        decimal cash = engine.FinanceManager.Balance;

        Assert.Equal("facility_not_found", engine.StockOffshoreFarm("nope", 1_000).ErrorKey);
        Assert.Equal("invalid_count", engine.StockOffshoreFarm(unit.Id, 0).ErrorKey);
        // 6,240 t MTB: 70M smolts × 0.1 kg = 7,000 t.
        Assert.Equal("capacity_exceeded", engine.StockOffshoreFarm(unit.Id, 70_000_000).ErrorKey);
        Assert.Equal(populations, engine.State.Populations.Count);
        Assert.Equal(cash, engine.FinanceManager.Balance);

        // Three zones take three stockings; the fourth finds every zone full.
        for (int i = 0; i < 3; i++)
            Assert.True(engine.StockOffshoreFarm(unit.Id, 10_000).Ok);
        Assert.Equal(new[] { 1, 2, 3 }, unit.Pens.Select(p => unit.PenOf(p)!.Number));
        cash = engine.FinanceManager.Balance;
        Assert.Equal(PenReasons.PensFull, engine.StockOffshoreFarm(unit.Id, 10_000).ErrorKey);
        Assert.Equal(3, unit.Pens.Count);
        Assert.Equal(cash, engine.FinanceManager.Balance);

        var poor = RichEngine(57);
        var poorUnit = BuildOperational(poor);
        poor.FinanceManager.ForceSpend(poor.FinanceManager.Balance);
        Assert.Equal("insufficient_funds", poor.StockOffshoreFarm(poorUnit.Id, 100_000).ErrorKey);
        Assert.Empty(poorUnit.Pens);
    }

    /// <summary>The fjord stocking verbs never stock an offshore unit: its own verb does, against its own MTB.</summary>
    [Fact]
    public void TheFjordStockingVerbs_RefuseAnOffshoreUnit()
    {
        var engine = RichEngine(58);
        var unit = BuildOperational(engine);
        Assert.Equal(SimulationEngine.OffshoreUnitKey, engine.StockSmolts(unit.Id, 10_000).ErrorKey);
        Assert.Equal(SimulationEngine.OffshoreUnitKey, engine.StockSmoltsInPen(unit.Id, "1", 10_000).ErrorKey);
        Assert.Equal(SimulationEngine.OffshoreUnitKey, engine.PreviewStocking(unit.Id, 10_000).ReasonKey);
        Assert.False(engine.PreviewPenStocking(unit.Id, "1", 10_000).Allowed);
        Assert.Equal(0, engine.MaxStockableSmolts(unit.Id));
        Assert.Empty(unit.Pens);
    }

    /// <summary>
    /// The unit's day (F2-07; replaces TheUnitsDay_BooksItsCrewEnergyAndCarbon_OnTheUnit,
    /// docs/run/evidence/f2_07_replaced_tests.md, with everything it asserted and more):
    /// every day the unit books its own lines in their own ledger categories:
    /// crew in rotation, energy, maintenance and insurance on its contract price;
    /// its crew changes on the Driftsplan rota by a chartered vessel, each
    /// change paid its vessel's day rate for every day it was chartered; its
    /// carbon goes on its own tracker, one fuel entry a day; a day above the
    /// wellboat-alongside limit locks it down and fatigues its structure by the
    /// coefficient × (Hs / design Hs)³ (the pre-F2 12 m damage rule is gone).
    /// </summary>
    [Fact]
    public void TheUnitsDay_BooksItsLinesCrewChangesAndCarbon_OnTheUnit()
    {
        var engine = RichEngine(59);
        var unit = BuildOperational(engine);
        Assert.True(engine.StockOffshoreFarm(unit.Id, 40_000).Ok);
        var structure = unit.Offshore!;
        var costs = LakskongenSim.Calibration.OffshoreCalibration.Default.Costs;
        var lines = new System.Collections.Generic.Dictionary<string, decimal>();
        engine.PnlBooked += (day, category, amount, income) =>
        {
            if (!income) lines[category] = (lines.TryGetValue(category, out var v) ? v : 0m) - amount;
        };
        engine.Advance(1);
        decimal perDay = 1m / SimCalendar.DaysPerYear;
        Assert.Equal(12 * costs.CrewSalaryNokPerYear * perDay, lines["offshore_crew"]);
        Assert.Equal((decimal)engine.OffshoreFuelLitresToday(unit) * costs.DieselNokPerLitre, lines["offshore_energy"]);
        Assert.Equal(structure.CAPEX * (decimal)costs.MaintenanceShareOfCapexPerYear * perDay, lines["offshore_maintenance"]);
        Assert.Equal(structure.CAPEX * (decimal)costs.InsuranceShareOfCapexPerYear * perDay, lines["offshore_insurance"]);
        // The unit's lines are its own: none go into the fjord's "operations" line.
        // (this company has no fjord site, so the day's "operations" line is empty).
        Assert.False(lines.TryGetValue("operations", out var operations) && operations > 0m, "the unit's lines left the operations line");

        // 120 days of the default crew rota: a change about every 7 days, each by a chartered vessel.
        int maxSince = 0;
        for (int d = 0; d < 120; d++)
        {
            engine.Advance(1);
            maxSince = Math.Max(maxSince, structure.DaysSinceCrewRotation);
        }
        var changes = structure.Bookings.Where(b => b.Op == OffshoreOp.CrewChange && b.Status == OffshoreBookingStatus.Done).ToList();
        Assert.InRange(changes.Count, 12, 18);
        foreach (var change in changes)
        {
            decimal expected = change.Vessel == VesselClass.Helicopter
                ? costs.HelicopterNokPerChange
                : change.DaysCharged * OffshoreCharter.DayRateNok(OffshoreOp.CrewChange, change.Vessel, costs);
            Assert.Equal(expected, change.ChargedNok);
        }
        Assert.True(maxSince <= structure.CrewRotationDays + 14, $"a crew stayed {maxSince} days");
        Assert.True(lines["offshore_charter"] >= changes.Sum(b => b.ChargedNok));

        int fuelBefore = unit.CarbonTracker.Emissions.Count(e => e.Source == unit.Id && e.Category == "Scope1_Fuel");
        engine.Advance(10);
        // The unit's own emissions (fuel, power, vessel), on its sea site's one
        // tracker; the fjord sites' per-site diesel formula never ran on it:
        // one fuel entry a day, not two.
        Assert.Contains(unit.CarbonTracker.Emissions, e => e.Source == unit.Id && e.Category == "Scope1_Fuel");
        Assert.Contains(unit.CarbonTracker.Emissions, e => e.Source == unit.Id + "_vessel");
        Assert.Equal(fuelBefore + 10, unit.CarbonTracker.Emissions.Count(e => e.Source == unit.Id && e.Category == "Scope1_Fuel"));

        // A 13 m day (forced): no vessel stays alongside, the structure fatigues by the cube of Hs over its design.
        double integrity = structure.StructuralIntegrity;
        int forcedDay = engine.State.Day + 1;
        engine.SeaOverrideForTests = (site, day) => day == forcedDay ? 13.0 : null;
        engine.Advance(1);
        Assert.True(structure.IsStormLockdown);
        var s = LakskongenSim.Calibration.OffshoreCalibration.Default.Structure;
        Assert.Equal(integrity - s.FatigueCoeff * Math.Pow(13.0 / structure.DesignHsM, 3.0), structure.StructuralIntegrity, 12);
        engine.Advance(1);
        Assert.False(structure.IsStormLockdown && structure.CurrentWaveHeight <= 5.0);
    }

    /// <summary>An escape at the unit is a sea site's escape: it counts in the rolling year the sustainability score reads.</summary>
    [Fact]
    public void AnEscapeAtTheUnit_CountsInTheSustainabilityScore()
    {
        var engine = RichEngine(60);
        var unit = BuildOperational(engine);
        Assert.Equal(0, engine.State.SeaSites.Sum(s => s.EscapeIncidentsInLastYear(engine.State.Day)));
        unit.RecordEscape(1_500, engine.State.Day);
        Assert.Equal(1, engine.State.SeaSites.Sum(s => s.EscapeIncidentsInLastYear(engine.State.Day)));
        Assert.Equal(1_500, unit.EscapedFishInLastYear(engine.State.Day));
    }

    /// <summary>The unit survives a save and a load, fish, zones and structure, and continues exactly.</summary>
    [Fact]
    public void TheUnit_SurvivesASave_AndContinuesExactly()
    {
        var engine = RichEngine(61, events: true);
        var unit = BuildOperational(engine);
        Assert.True(engine.StockOffshoreFarm(unit.Id, 30_000).Ok);
        engine.Advance(20);

        var resumed = SaveStateComparer.Resume(SaveStateComparer.Serialize(engine), events: true);
        var copy = Assert.Single(resumed.OffshoreUnits);
        Assert.Equal((unit.Id, unit.PlotId, unit.ZoneId, unit.Kind), (copy.Id, copy.PlotId, copy.ZoneId, copy.Kind));
        Assert.Equal(unit.Offshore!.DesignHsM, copy.Offshore!.DesignHsM);
        var pen = Assert.Single(copy.Pens);
        Assert.Contains(resumed.State.Populations, p => ReferenceEquals(p, pen));
        Assert.Same(copy.PhysicalPens[0], copy.PenOf(pen));

        engine.Advance(60);
        resumed.Advance(60);
        string a = SaveStateComparer.Normalize(SaveStateComparer.Serialize(engine));
        string b = SaveStateComparer.Normalize(SaveStateComparer.Serialize(resumed));
        Assert.True(a == b, "resumed game diverged at " + SaveStateComparer.FirstDifference(a, b));
    }
}
