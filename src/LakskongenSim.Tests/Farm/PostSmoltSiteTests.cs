using System;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using Xunit;

namespace LakskongenSim.Tests.Farm;

/// <summary>
/// The post-smolt site as a land site (roadmap F2-03, docs/FARM_TYPES_SPEC.md
/// §3.1 decision 1: a pre-F2 post-smolt facility becomes a
/// <see cref="RASFacility"/> with the post-smolt template, its batches
/// populations in its tanks). Replaces FacilityProductionTests' post-smolt
/// section (spec §3.10; F2-13's PostSmoltBridgeTests adds the tiers and the
/// salinity) with everything it asserted and more.
/// </summary>
public class PostSmoltSiteTests
{
    private static SimulationEngine RichEngine(int seed)
    {
        var engine = new SimulationEngine { FinanceManager = new FinanceManager(5_000_000_000m) };
        engine.Initialize(enableEvents: false, seed: seed);
        return engine;
    }

    private static RASFacility BuildOperational(SimulationEngine engine)
    {
        var built = engine.BuildPostSmoltFacility("Post-smolt");
        Assert.True(built.Ok, built.Message);
        var site = engine.PostSmoltSites.Single(s => s.Id == built.Id);
        site.ConstructionDays = 1;
        engine.Advance(2);
        Assert.True(site.IsOperational);
        return site;
    }

    [Fact]
    public void Build_CreatesAPostSmoltLandSite_OfFourModules_OnThePostSmoltPad()
    {
        var engine = RichEngine(70);
        decimal before = engine.FinanceManager.Balance;
        var built = engine.BuildPostSmoltFacility("Elveøyra post-smolt");
        Assert.True(built.Ok, built.Message);

        var site = Assert.Single(engine.PostSmoltSites);
        Assert.Equal("postsmolt_1", site.Id);
        Assert.Contains(site, engine.RASFacilities);
        Assert.Empty(engine.PostSmoltFacilities);
        Assert.Equal(LandSiteKind.PostSmolt, site.Kind);
        Assert.Equal("pad_delta", site.PlotId);
        Assert.Equal("pad_delta", site.ZoneId);
        Assert.Equal(site.Id, engine.FindPlot("pad_delta")!.OwnerUnitId);
        Assert.DoesNotContain(engine.State.Zones, z => z.Id == site.ZoneId);
        // 4 modules of 2 Ø16 m tanks (3,200 m³ a module), 480 t a year each (spec §2A).
        Assert.Equal(4, SimulationEngine.PostSmoltModulesOf(site));
        Assert.Equal(8, site.TankCount);
        Assert.Equal(1_600.0, site.TankVolumeM3);
        Assert.Equal(4 * 480.0, site.CapacityTonnesPerYear);
        // The pre-F2 facility's staff, water temperature, capex and build time.
        var legacy = new PostSmoltFacility();
        Assert.Equal(legacy.EmployeeCount, site.EmployeeCount);
        Assert.Equal(legacy.WaterTemp, site.TargetWaterTemp);
        Assert.Equal(legacy.ConstructionDays, site.ConstructionDays);
        Assert.Equal(legacy.GetCAPEX(), before - engine.FinanceManager.Balance);
        Assert.Equal(legacy.GetCAPEX(), site.CAPEX);
        Assert.Equal(engine.State.Day, site.ConstructionStartDay);
        Assert.Equal("not_operational", engine.StartPostSmoltBatch(site.Id, 1_000).ErrorKey);

        // The second goes to pad_a (it has a post-smolt hall); pad_b has no post-smolt room.
        var second = engine.BuildPostSmoltFacility("");
        Assert.True(second.Ok, second.Message);
        Assert.Equal("pad_a", engine.PostSmoltSites.Single(s => s.Id == second.Id).PlotId);
        Assert.Equal("Postsmoltanlegg 2", engine.PostSmoltSites.Single(s => s.Id == second.Id).Name);
        decimal cash = engine.FinanceManager.Balance;
        Assert.Equal(SimulationEngine.NoFreePlotKey, engine.BuildPostSmoltFacility("").ErrorKey);
        Assert.Equal(cash, engine.FinanceManager.Balance);
        Assert.Equal(2, engine.PostSmoltSites.Count);
    }

    /// <summary>
    /// FacilityProductionTests.PostSmolt_BatchIsVaccinated_GrowsAndGoesToSeaAsALargeSmolt
    /// on the F2 site: a batch starts today, may not go to sea before it is
    /// vaccinated and grown, is vaccinated once, grows to the 300 g target and
    /// goes to a sea site as one pen of its count and weight, a large smolt.
    /// More: the batch is a population in the site's tanks (its id the
    /// batch's), it grows in the site's controlled water, it leaves the land
    /// site and the game's list through the ledger (a transfer out) and keeps
    /// its age, genome and vaccination at sea.
    /// </summary>
    [Fact]
    public void PostSmolt_BatchIsVaccinated_GrowsAndGoesToSeaAsALargeSmolt()
    {
        var engine = RichEngine(56);
        var facility = BuildOperational(engine);
        var site = new SeaSite("post-smolt-sea", "zone-ps") { MABTonnes = 780 };
        engine.State.SeaSites.Add(site);

        var started = engine.StartPostSmoltBatch(facility.Id, 50_000);
        Assert.True(started.Ok, started.Message);
        string batchId = started.Id;
        Assert.Equal("postsmolt_1_batch_1", batchId);
        var batch = Assert.Single(facility.Tanks);
        Assert.Equal(batchId, batch.Id);
        Assert.Contains(batch, engine.State.Populations);
        Assert.Equal((facility.Id, facility.ZoneId), (batch.SiteId, batch.ZoneId));
        Assert.Equal(HoldingKind.LandTank, engine.HoldingOf(batch).Kind);
        var cohort = Assert.Single(batch.Cohorts);
        Assert.Equal(50_000, cohort.Count);
        Assert.Equal(0.080, cohort.MeanWeightKg, 12);
        Assert.Equal(engine.Hatchery.MaturationDays, cohort.AgeDays);
        Assert.Equal("batch_not_ready", engine.TransferPostSmoltToSea(facility.Id, batchId, site.Id).ErrorKey);
        Assert.True(engine.VaccinatePostSmoltBatch(facility.Id, batchId).Ok);
        Assert.Equal(engine.State.Day, batch.VaccinatedDay);
        Assert.Equal("already_vaccinated", engine.VaccinatePostSmoltBatch(facility.Id, batchId).ErrorKey);

        int guard = 0;
        while (!SimulationEngine.IsPostSmoltReady(batch) && guard++ < 400) engine.Advance(1);
        Assert.True(SimulationEngine.IsPostSmoltReady(batch), "batch never reached transfer weight");
        Assert.InRange(guard, 20, 200);
        // The site's controlled water (its 13 °C setpoint ± the tick's 0.15 °C noise), not the sea.
        Assert.Equal(facility.CurrentWaterTemp, engine.HoldingWaterOf(batch).TemperatureC, 12);
        Assert.InRange(engine.HoldingWaterOf(batch).TemperatureC, facility.TargetWaterTemp - 0.15, facility.TargetWaterTemp + 0.15);

        int count = batch.TotalCount;
        double weightKg = batch.GetTotalBiomassKg() / count;
        int age = cohort.AgeDays;
        var genome = cohort.Genetics;
        var removedBefore = engine.MortalityLedger.Cumulative().Removed(FishRemoval.TransferOut).Fish;

        var moved = engine.TransferPostSmoltToSea(facility.Id, batchId, site.Id);
        Assert.True(moved.Ok, moved.Message);
        Assert.Empty(facility.Tanks);
        Assert.DoesNotContain(batch, engine.State.Populations);
        var seaCohort = Assert.Single(Assert.Single(site.Pens).Cohorts);
        Assert.Equal(count, seaCohort.Count);
        Assert.Equal(weightKg, seaCohort.MeanWeightKg, 6);
        Assert.Equal(SmoltQualityTier.Large, seaCohort.Quality); // 300 g target: 200-400 g band
        Assert.Equal(age, seaCohort.AgeDays);
        Assert.Same(genome, seaCohort.Genetics);
        Assert.Equal(batch.VaccinatedDay, site.Pens.Single().VaccinatedDay);
        Assert.Equal(count, engine.MortalityLedger.Cumulative().Removed(FishRemoval.TransferOut).Fish - removedBefore);
    }

    [Fact]
    public void Batches_AreRefused_PastTheModulesRoom_AndOnTheWrongKindOfSite()
    {
        var engine = RichEngine(71);
        var facility = BuildOperational(engine);
        Assert.True(engine.BuildRASFacility("Grow-out", RASScale.Small).Ok);
        var growOut = engine.RASFacilities.Single(f => f.Kind == LandSiteKind.GrowOut);

        // 4 modules × 1.1 M fish.
        Assert.True(engine.StartPostSmoltBatch(facility.Id, 4_000_000).Ok);
        decimal cash = engine.FinanceManager.Balance;
        Assert.Equal("capacity_exceeded", engine.StartPostSmoltBatch(facility.Id, 400_001).ErrorKey);
        Assert.True(engine.StartPostSmoltBatch(facility.Id, 400_000).Ok);
        Assert.Equal(cash - 400_000 * LakskongenSim.Calibration.CalibrationRegistry.Default.Economy.SmoltCostPerFishNOK, engine.FinanceManager.Balance);
        Assert.Equal(new[] { "postsmolt_1_batch_1", "postsmolt_1_batch_2" }, facility.Tanks.Select(t => t.Id));

        // A grow-out land site is no post-smolt site, and the other way round.
        Assert.Equal("facility_not_found", engine.StartPostSmoltBatch(growOut.Id, 1_000).ErrorKey);
        Assert.Equal(SimulationEngine.WrongSiteKindKey, engine.StockRASFacility(facility.Id, 1_000).ErrorKey);
        Assert.Equal("batch_not_found", engine.VaccinatePostSmoltBatch(growOut.Id, "postsmolt_1_batch_1").ErrorKey);

        // A post-smolt batch goes to a fjord site, never an offshore unit through this verb.
        Assert.True(engine.BuildOffshoreFarm("", OffshoreType.SemiSubmersible).Ok);
        var unit = engine.OffshoreUnits.Single();
        var batch = facility.Tanks[0];
        batch.VaccinatedDay = engine.State.Day;
        foreach (var c in batch.Cohorts) c.MeanWeightKg = 0.35;
        Assert.Equal(SimulationEngine.OffshoreUnitKey, engine.TransferPostSmoltToSea(facility.Id, batch.Id, unit.Id).ErrorKey);
        Assert.Contains(batch, facility.Tanks);
    }
}
