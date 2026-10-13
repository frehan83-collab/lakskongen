using System;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using Xunit;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// Review F383: RAS facilities and offshore farms could be built (and became
/// operational) but never held fish, so their CAPEX only bought OPEX; post-smolt
/// batches could never reach the sea. Also: facility CAPEX bypassed the ledger.
/// </summary>
public class FacilityProductionTests
{
    private static SimulationEngine RichEngine(int seed)
    {
        var engine = new SimulationEngine();
        engine.FinanceManager = new FinanceManager(5_000_000_000m);
        engine.Initialize(enableEvents: false, seed: seed);
        return engine;
    }

    /// <summary>Shortcut the build period (the natural path has its own test).</summary>
    private static void FinishConstruction(SimulationEngine engine, Action shorten)
    {
        shorten();
        engine.Advance(2);
    }

    private static double MeanWeight(System.Collections.Generic.IEnumerable<FishPopulation> pens)
    {
        var list = pens.ToList();
        return list.Sum(p => p.GetTotalBiomassKg()) / Math.Max(1, list.Sum(p => p.TotalCount));
    }

    [Fact]
    public void Ras_IsOperationalAfterItsConstructionDays_AndItsCapexIsBooked()
    {
        var engine = RichEngine(51);
        decimal cash = engine.FinanceManager.Balance;
        var built = engine.BuildRASFacility("Test RAS", RASScale.Small);
        Assert.True(built.Ok, built.Message);
        var ras = engine.RASFacilities.Single();
        Assert.Equal(cash - ras.CAPEX, engine.FinanceManager.Balance);

        engine.Advance(ras.ConstructionDays - 2);
        Assert.False(ras.IsOperational);
        engine.Advance(3);
        Assert.True(ras.IsOperational);
        // Booked as an asset through the ledger, not reconciled as unclassified cash.
        Assert.Equal(0m, engine.UnbookedCashTotal);
    }

    [Fact]
    public void Ras_StocksGrowsAndHarvests_LikeASeaSite_OutsideTheRentTax()
    {
        var engine = RichEngine(52);
        Assert.True(engine.BuildRASFacility("Grow-out", RASScale.Small).Ok);
        var ras = engine.RASFacilities.Single();
        FinishConstruction(engine, () => ras.ConstructionDays = 1);

        decimal cash = engine.FinanceManager.Balance;
        var stocked = engine.StockRASFacility(ras.Id, 100_000);
        Assert.True(stocked.Ok, stocked.Message);
        var tank = Assert.Single(ras.Tanks);
        Assert.Contains(tank, engine.State.Populations);
        Assert.True(engine.FinanceManager.Balance < cash);

        engine.Advance(300);
        Assert.True(MeanWeight(ras.Tanks) > 1.0, $"RAS fish did not grow: {MeanWeight(ras.Tanks):F2} kg");

        decimal rentBase = engine.State.ResourceRentHarvestValueThisTaxYear;
        decimal sales = engine.State.TotalSalesRevenueNok;
        var harvest = engine.HarvestRASFacility(ras.Id);
        Assert.True(harvest.Ok, harvest.Message);
        Assert.True(harvest.Value2 > 0.0);
        Assert.Empty(ras.Tanks);
        Assert.DoesNotContain(tank, engine.State.Populations);
        Assert.True(Math.Abs(sales + (decimal)harvest.Value2 - engine.State.TotalSalesRevenueNok) < 0.01m,
            "the facility sale must be booked as a sale");
        Assert.Equal(rentBase, engine.State.ResourceRentHarvestValueThisTaxYear);
        Assert.Equal("facility_empty", engine.HarvestRASFacility(ras.Id).ErrorKey);
    }

    [Fact]
    public void RasFish_GrowInTheTanksWater_NotTheWinterSea()
    {
        var engine = RichEngine(53);
        Assert.True(engine.BuildRASFacility("Winter test", RASScale.Small).Ok);
        var ras = engine.RASFacilities.Single();
        FinishConstruction(engine, () => ras.ConstructionDays = 1);
        Assert.True(engine.StockRASFacility(ras.Id, 10_000).Ok);
        var tank = ras.Tanks.Single();

        // The same fish outside any site or facility: no lice, gills or zone,
        // so water temperature is the only difference from the tank.
        var reference = new FishPopulation { Id = "reference-pen" };
        reference.InjectDependencies(engine.Growth, engine.Mortality);
        var c = tank.Cohorts.Single();
        reference.Cohorts.Add(new FishPopulation.Cohort(c.AgeDays, c.Count, c.MeanWeightKg, c.Stage, c.Genetics));
        engine.State.Populations.Add(reference);

        engine.Advance(60); // January-February: the sea is far below the tanks' 12 °C
        Assert.True(engine.Weather.CurrentTemperatureC < ras.CurrentWaterTemp);
        Assert.True(MeanWeight(ras.Tanks) > MeanWeight(new[] { reference }) * 1.05,
            $"RAS {MeanWeight(ras.Tanks):F3} kg vs sea-temperature reference {MeanWeight(new[] { reference }):F3} kg");
    }

    /// <summary>Re-review: facility fish joined State.Populations and took the
    /// sea's cold-water stress deaths although the tanks run at 12 °C.</summary>
    [Fact]
    public void SeaTemperatureStress_SparesRasTanks_ButNotSeaPens()
    {
        var engine = RichEngine(59);
        Assert.True(engine.BuildRASFacility("Warm tanks", RASScale.Small).Ok);
        var ras = engine.RASFacilities.Single();
        FinishConstruction(engine, () => ras.ConstructionDays = 1);
        Assert.True(engine.StockRASFacility(ras.Id, 100_000).Ok);
        var site = new SeaSite("cold-sea", "zone-cold") { MABTonnes = 780 };
        engine.State.SeaSites.Add(site);
        Assert.True(engine.StockSmolts(site.Id, 100_000).Ok);
        int tank = ras.Tanks.Sum(p => p.TotalCount);
        int pen = site.Pens.Sum(p => p.TotalCount);

        engine.ApplySeaTemperatureStress(0.0); // 4 degrees below the band: 0.02% a day

        Assert.Equal(tank, ras.Tanks.Sum(p => p.TotalCount));
        Assert.Equal(pen - (int)(pen * 0.04 * 0.005), site.Pens.Sum(p => p.TotalCount));
    }

    [Fact]
    public void IsaOutbreak_CullsASeaPen_NeverARasTank()
    {
        var engine = new SimulationEngine();
        engine.FinanceManager = new FinanceManager(5_000_000_000m);
        engine.Initialize(enableEvents: true, seed: 58);
        Assert.True(engine.BuildRASFacility("Biosecure", RASScale.Small).Ok);
        var ras = engine.RASFacilities.Single();
        FinishConstruction(engine, () => ras.ConstructionDays = 1);
        Assert.True(engine.StockRASFacility(ras.Id, 50_000).Ok);
        var isa = engine.Storyteller.GetEventDefinitions().Single(e => e.Id == "isa-outbreak");

        // RAS only: the outbreak cannot fire.
        Assert.False(isa.Condition!(engine.State));

        var site = new SeaSite("isa-sea", "zone-isa") { MABTonnes = 780 };
        engine.State.SeaSites.Add(site);
        Assert.True(engine.StockSmolts(site.Id, 20_000).Ok);
        Assert.True(isa.Condition!(engine.State));
        engine.Storyteller.Fire(isa, engine.State.Day);

        Assert.Equal(0, site.Pens.Sum(p => p.TotalCount));
        Assert.True(ras.Tanks.Sum(p => p.TotalCount) > 0, "the ISA cull hit the RAS tank");
    }

    [Fact]
    public void FacilityStocking_RefusesBeforeAnyMutation()
    {
        var engine = RichEngine(54);
        Assert.True(engine.BuildRASFacility("Refusals", RASScale.Small).Ok);
        var ras = engine.RASFacilities.Single();
        Assert.Equal("not_operational", engine.StockRASFacility(ras.Id, 1_000).ErrorKey);
        FinishConstruction(engine, () => ras.ConstructionDays = 1);
        Assert.Equal("facility_not_found", engine.StockRASFacility("nope", 1_000).ErrorKey);
        Assert.Equal("invalid_count", engine.StockRASFacility(ras.Id, 0).ErrorKey);
        // 12 x 3,000 m3 at 60 kg/m3 = 2,160 t; 30M smolts x 0.1 kg = 3,000 t.
        Assert.Equal("capacity_exceeded", engine.StockRASFacility(ras.Id, 30_000_000).ErrorKey);
        Assert.Empty(ras.Tanks);

        var poor = RichEngine(54);
        Assert.True(poor.BuildRASFacility("Poor", RASScale.Small).Ok);
        var poorRas = poor.RASFacilities.Single();
        FinishConstruction(poor, () => poorRas.ConstructionDays = 1);
        poor.FinanceManager.ForceSpend(poor.FinanceManager.Balance);
        Assert.Equal("insufficient_funds", poor.StockRASFacility(poorRas.Id, 100_000).ErrorKey);
        Assert.Empty(poorRas.Tanks);
    }

    [Fact]
    public void Offshore_StocksAndHarvests_AndCountsForTheRentTax()
    {
        var engine = RichEngine(55);
        Assert.True(engine.BuildOffshoreFarm("Havfarm", OffshoreType.SemiSubmersible).Ok);
        var farm = engine.OffshoreFarms.Single();
        FinishConstruction(engine, () => farm.ConstructionDays = 1);

        Assert.True(engine.StockOffshoreFarm(farm.Id, 50_000).Ok);
        var pen = Assert.Single(farm.Pens);
        Assert.Contains(pen, engine.State.Populations);
        engine.Advance(200);

        decimal rentBase = engine.State.ResourceRentHarvestValueThisTaxYear;
        var harvest = engine.HarvestOffshoreFarm(farm.Id);
        Assert.True(harvest.Ok, harvest.Message);
        Assert.Empty(farm.Pens);
        Assert.True(engine.State.ResourceRentHarvestValueThisTaxYear > rentBase);
    }

    [Fact]
    public void PostSmolt_BatchIsVaccinated_GrowsAndGoesToSeaAsALargeSmolt()
    {
        var engine = RichEngine(56);
        Assert.True(engine.BuildPostSmoltFacility("Post-smolt").Ok);
        var facility = engine.PostSmoltFacilities.Single();
        FinishConstruction(engine, () => facility.ConstructionDays = 1);
        var site = new SeaSite("post-smolt-sea", "zone-ps") { MABTonnes = 780 };
        engine.State.SeaSites.Add(site);

        var started = engine.StartPostSmoltBatch(facility.Id, 50_000);
        Assert.True(started.Ok, started.Message);
        string batchId = started.Id;
        Assert.Equal(engine.State.Day, facility.Batches.Single().StartDay);
        Assert.Equal("batch_not_ready", engine.TransferPostSmoltToSea(facility.Id, batchId, site.Id).ErrorKey);
        Assert.True(engine.VaccinatePostSmoltBatch(facility.Id, batchId).Ok);
        Assert.Equal("already_vaccinated", engine.VaccinatePostSmoltBatch(facility.Id, batchId).ErrorKey);

        int guard = 0;
        while (!facility.Batches.Single().IsReadyForTransfer && guard++ < 400) engine.Advance(1);
        var batch = facility.Batches.Single();
        Assert.True(batch.IsReadyForTransfer, "batch never reached transfer weight");

        var moved = engine.TransferPostSmoltToSea(facility.Id, batchId, site.Id);
        Assert.True(moved.Ok, moved.Message);
        Assert.Empty(facility.Batches);
        var cohort = Assert.Single(Assert.Single(site.Pens).Cohorts);
        Assert.Equal(batch.Count, cohort.Count);
        Assert.Equal(batch.AverageWeightG / 1000.0, cohort.MeanWeightKg, 6);
        Assert.Equal(SmoltQualityTier.Large, cohort.Quality); // 300 g target: 200-400 g band
    }

    [Fact]
    public void FacilityFish_StayOneInstanceAcrossASave_AndTheGameContinuesExactly()
    {
        var engine = RichEngine(57);
        Assert.True(engine.BuildRASFacility("Persist", RASScale.Small).Ok);
        var ras = engine.RASFacilities.Single();
        FinishConstruction(engine, () => ras.ConstructionDays = 1);
        Assert.True(engine.StockRASFacility(ras.Id, 50_000).Ok);
        engine.Advance(30);

        var resumed = SaveStateComparer.Resume(SaveStateComparer.Serialize(engine), events: false);
        var tank = Assert.Single(resumed.RASFacilities.Single().Tanks);
        Assert.Contains(resumed.State.Populations, p => ReferenceEquals(p, tank));

        engine.Advance(60);
        resumed.Advance(60);
        string a = SaveStateComparer.Normalize(SaveStateComparer.Serialize(engine));
        string b = SaveStateComparer.Normalize(SaveStateComparer.Serialize(resumed));
        Assert.True(a == b, "resumed game diverged at " + SaveStateComparer.FirstDifference(a, b));
        Assert.True(resumed.HarvestRASFacility(resumed.RASFacilities.Single().Id).Ok);
    }
}
