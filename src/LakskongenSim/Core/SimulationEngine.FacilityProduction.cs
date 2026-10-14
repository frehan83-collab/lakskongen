using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Data;
using LakskongenSim.Events;
using LakskongenSim.Farm;

namespace LakskongenSim.Core;

/// <summary>
/// Production in land-based and offshore facilities (review F383). A RAS
/// facility or offshore farm could be built (and, since the facility verbs,
/// even became operational) but never held fish, so 0.5-2 B NOK of CAPEX only
/// bought OPEX; post-smolt batches could never reach the sea. Facility fish
/// live in <see cref="GameState.Populations"/> like sea pens, so the daily
/// biology (growth, mortality, feed) runs on them; RAS tanks grow at the
/// facility's controlled water temperature.
/// </summary>
public partial class SimulationEngine
{
    /// <summary>Stocking density limit in RAS grow-out tanks (kg/m³).</summary>
    internal const double RasMaxDensityKgM3 = 60.0;

    /// <summary>Post-smolt weight (g) from which a transferred batch counts as SuperSmolt.</summary>
    internal const double SuperSmoltTransferWeightG = 400.0;

    /// <summary>Post-smolt weight (g) from which a transferred batch counts as a large smolt.</summary>
    internal const double LargeSmoltTransferWeightG = 200.0;

    // ── RAS ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Stock smolts in an operational RAS facility's tanks. Fails before any
    /// mutation: engine_not_ready / invalid_count / facility_not_found /
    /// not_operational / capacity_exceeded / insufficient_funds.
    /// </summary>
    public CommandResult StockRASFacility(string facilityId, int count, string strainId = "standard")
    {
        if (State == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        if (count <= 0)
            return CommandResult.Fail("invalid_count", $"Smolt count must be positive (got {count})");
        var facility = RASFacilities.FirstOrDefault(f => f.Id == facilityId);
        if (facility == null)
            return CommandResult.Fail("facility_not_found", $"RAS facility '{facilityId}' not found");
        if (!facility.IsOperational)
            return CommandResult.Fail("not_operational",
                $"'{facility.Name}' is still under construction ({facility.ConstructionProgress:P0})");

        var strain = SmoltStrainRegistry.Get(strainId);
        double capacityTonnes = facility.TankCount * facility.TankVolumeM3 * RasMaxDensityKgM3 / 1000.0;
        var refused = CheckFacilityCapacity(facility.GetTotalBiomassKg(), count, strain, capacityTonnes, facility.Name);
        if (refused != null) return refused;

        return StockFacility(facility.Id, facility.ZoneId, facility.Name, facility.Tanks, count, strain, liceLoad: 0.0);
    }

    /// <summary>Harvest every tank of a RAS facility (land-based: no resource rent).</summary>
    public CommandResult HarvestRASFacility(string facilityId)
    {
        if (State == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        var facility = RASFacilities.FirstOrDefault(f => f.Id == facilityId);
        if (facility == null)
            return CommandResult.Fail("facility_not_found", $"RAS facility '{facilityId}' not found");
        return HarvestPopulations(facility.Tanks, facility.Id, facility.Name, "facility_empty",
            $"'{facility.Name}' has no fish to harvest", seaFarmed: false);
    }

    // ── Offshore ───────────────────────────────────────────────────────────

    /// <summary>
    /// Stock smolts in an operational offshore farm. The exposed site carries
    /// the strain's lice load scaled by the farm's lower lice pressure. Same
    /// error keys as <see cref="StockRASFacility"/>.
    /// </summary>
    public CommandResult StockOffshoreFarm(string farmId, int count, string strainId = "standard")
    {
        if (State == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        if (count <= 0)
            return CommandResult.Fail("invalid_count", $"Smolt count must be positive (got {count})");
        var farm = OffshoreFarms.FirstOrDefault(f => f.Id == farmId);
        if (farm == null)
            return CommandResult.Fail("facility_not_found", $"Offshore farm '{farmId}' not found");
        if (!farm.IsOperational)
            return CommandResult.Fail("not_operational",
                $"'{farm.Name}' is still under construction ({farm.ConstructionProgress:P0})");

        var strain = SmoltStrainRegistry.Get(strainId);
        double standing = farm.Pens.Sum(p => p.GetTotalBiomassKg());
        var refused = CheckFacilityCapacity(standing, count, strain, farm.CapacityTonnes, farm.Name);
        if (refused != null) return refused;

        return StockFacility(farm.Id, farm.ZoneId, farm.Name, farm.Pens, count, strain,
            liceLoad: strain.InitialLiceLoad * farm.SeaLicePressure);
    }

    /// <summary>Harvest every pen of an offshore farm (sea-farmed: counts for resource rent).</summary>
    public CommandResult HarvestOffshoreFarm(string farmId)
    {
        if (State == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        var farm = OffshoreFarms.FirstOrDefault(f => f.Id == farmId);
        if (farm == null)
            return CommandResult.Fail("facility_not_found", $"Offshore farm '{farmId}' not found");
        return HarvestPopulations(farm.Pens, farm.Id, farm.Name, "facility_empty",
            $"'{farm.Name}' has no fish to harvest", seaFarmed: true);
    }

    // ── Post-smolt ─────────────────────────────────────────────────────────

    /// <summary>
    /// Start a post-smolt batch of standard smolts in an operational facility
    /// (paid as smolts). The batch grows in the facility until it reaches the
    /// target weight and is vaccinated, then <see cref="TransferPostSmoltToSea"/>.
    /// Value = cost, Id = batch id.
    /// </summary>
    public CommandResult StartPostSmoltBatch(string facilityId, int count)
    {
        if (State == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        if (count <= 0)
            return CommandResult.Fail("invalid_count", $"Smolt count must be positive (got {count})");
        var facility = PostSmoltFacilities.FirstOrDefault(f => f.Id == facilityId);
        if (facility == null)
            return CommandResult.Fail("facility_not_found", $"Post-smolt facility '{facilityId}' not found");
        if (!facility.IsOperational)
            return CommandResult.Fail("not_operational",
                $"'{facility.Name}' is still under construction ({facility.ConstructionProgress:P0})");
        if (facility.Batches.Sum(b => b.Count) + count > facility.AnnualSmoltCapacity)
            return CommandResult.Fail("capacity_exceeded",
                $"'{facility.Name}' holds at most {facility.AnnualSmoltCapacity:N0} smolts at once");

        decimal cost = count * _balancing.SmoltCostPerFish;
        if (!TryPayCost(cost, CostBooking.Cogs, "smolts"))
            return CommandResult.Fail("insufficient_funds",
                $"Insufficient funds: need {cost:N0} NOK for {count:N0} smolts", (double)cost);

        string batchId = NextFacilityId($"{facility.Id}_batch", facility.Batches.Select(b => b.Id));
        var batch = facility.StartNewBatch(batchId, count);
        batch.StartDay = State.Day;
        LogFacilityEvent("Ny postsmoltgruppe", SimText.Nb($"{facility.Name}: ny gruppe med {count:N0} smolt"), -cost);
        return CommandResult.Success($"Started batch {batchId} ({count:N0} smolts) at {facility.Name}",
            (double)cost, id: batchId);
    }

    /// <summary>Vaccinate a post-smolt batch (OPEX). Required before transfer to sea.</summary>
    public CommandResult VaccinatePostSmoltBatch(string facilityId, string batchId)
    {
        if (State == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        var facility = PostSmoltFacilities.FirstOrDefault(f => f.Id == facilityId);
        var batch = facility?.Batches.FirstOrDefault(b => b.Id == batchId);
        if (facility == null || batch == null)
            return CommandResult.Fail("batch_not_found", $"Batch '{batchId}' not found");
        if (batch.VaccinationComplete)
            return CommandResult.Fail("already_vaccinated", $"Batch '{batchId}' is already vaccinated");
        decimal cost = batch.Count * facility.VaccinationCostPerFish;
        if (!TryPayCost(cost, CostBooking.Opex, "vaccination"))
            return CommandResult.Fail("insufficient_funds",
                $"Insufficient funds: need {cost:N0} NOK to vaccinate batch '{batchId}'", (double)cost);
        facility.VaccinateBatch(batchId);
        return CommandResult.Success($"Vaccinated batch {batchId}", (double)cost, id: batchId);
    }

    /// <summary>
    /// Move a ready post-smolt batch to a sea site: a new pen at the batch's
    /// weight whose quality tier follows that weight (SuperSmolt from 400 g,
    /// large smolt from 200 g). The site's MAB applies, like any stocking.
    /// </summary>
    public CommandResult TransferPostSmoltToSea(string facilityId, string batchId, string siteId)
    {
        if (State == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        var facility = PostSmoltFacilities.FirstOrDefault(f => f.Id == facilityId);
        var batch = facility?.Batches.FirstOrDefault(b => b.Id == batchId);
        if (facility == null || batch == null)
            return CommandResult.Fail("batch_not_found", $"Batch '{batchId}' not found");
        if (!batch.IsReadyForTransfer)
            return CommandResult.Fail("batch_not_ready",
                $"Batch '{batchId}' is not ready ({batch.AverageWeightG:F0} of {facility.TargetTransferWeightG:F0} g" +
                (batch.VaccinationComplete ? ")" : ", not vaccinated)"));
        var site = State.SeaSites.FirstOrDefault(s => s.Id == siteId);
        if (site == null)
            return CommandResult.Fail("site_not_found", $"Site '{siteId}' not found");

        double weightKg = batch.AverageWeightG / 1000.0;
        double incomingTonnes = batch.Count * weightKg / 1000.0;
        if (site.CurrentBiomassTonnes + incomingTonnes > site.MABTonnes)
            return CommandResult.Fail("mab_exceeded",
                $"Transferring {incomingTonnes:F1} t would put {siteId} over its MAB ({site.MABTonnes:F0} t)");
        // S2-01a: a transfer to sea is a stocking; the plan's peak must fit.
        var capacity = CheckStockingCapacity(site, batch.Count, weightKg);
        if (capacity != null) return capacity;

        facility.TransferToSea(batchId);
        var tier = batch.AverageWeightG >= SuperSmoltTransferWeightG ? SmoltQualityTier.SuperSmolt
            : batch.AverageWeightG >= LargeSmoltTransferWeightG ? SmoltQualityTier.Large
            : SmoltQualityTier.Standard;
        var strain = SmoltStrainRegistry.Get("standard");
        var population = new FishPopulation { ZoneId = site.ZoneId, SeaLiceLoad = strain.InitialLiceLoad };
        population.InjectDependencies(Growth, Mortality);
        population.Cohorts.Add(new FishPopulation.Cohort(Hatchery.MaturationDays + batch.DaysInFacility,
            batch.Count, weightKg, LifeStage.Smolt, strain.BuildGenome())
        {
            Quality = tier
        });
        site.Stock(population);
        site.LastStockedDay = State.Day;
        State.Populations.Add(population);
        if (strain.InitialLiceLoad > 0)
            site.Lice.SeedExternalInfection(Math.Max(0.05, strain.InitialLiceLoad));

        LogFacilityEvent("Postsmolt flyttet til sjø",
            SimText.Nb($"{facility.Name}: {batch.Count:N0} postsmolt ({batch.AverageWeightG:F0} g) til {site.Label}"), 0m);
        return CommandResult.Success($"Transferred {batch.Count:N0} post-smolt ({tier}) to {siteId}",
            batch.Count, batch.AverageWeightG, siteId);
    }

    // ── Shared ─────────────────────────────────────────────────────────────

    private static CommandResult? CheckFacilityCapacity(double standingKg, int count, SmoltStrain strain,
        double capacityTonnes, string name)
    {
        double incomingTonnes = count * strain.ReleaseWeightKg / 1000.0;
        if (standingKg / 1000.0 + incomingTonnes <= capacityTonnes) return null;
        return CommandResult.Fail("capacity_exceeded",
            $"Stocking {count:N0} smolts would put '{name}' over its {capacityTonnes:N0} t capacity");
    }

    private CommandResult StockFacility(string facilityId, string zoneId, string name,
        List<FishPopulation> pens, int count, SmoltStrain strain, double liceLoad)
    {
        decimal cost = count * _balancing.SmoltCostPerFish * (decimal)strain.CostMultiplier;
        if (!TryPayCost(cost, CostBooking.Cogs, "smolts"))
            return CommandResult.Fail("insufficient_funds",
                $"Insufficient funds: need {cost:N0} NOK for {count:N0} smolts", (double)cost);

        var population = new FishPopulation { ZoneId = zoneId, SiteId = facilityId, SeaLiceLoad = liceLoad };
        population.InjectDependencies(Growth, Mortality);
        population.Cohorts.Add(new FishPopulation.Cohort(Hatchery.MaturationDays, count,
            strain.ReleaseWeightKg, LifeStage.Smolt, strain.BuildGenome()));
        pens.Add(population);
        State.Populations.Add(population);

        LogFacilityEvent("Utsett av smolt", SimText.Nb($"Satte ut {count:N0} smolt ({strain.Name}) i {name}"), -cost);
        return CommandResult.Success($"Stocked {count:N0} smolts at {name}", count, (double)cost, facilityId);
    }

    private void LogFacilityEvent(string title, string description, decimal balanceChange)
    {
        State.EventLog.Add(new EventRecord
        {
            Day = State.Day,
            Name = title,
            Category = EventCategory.Equipment,
            Description = description,
            BalanceChange = balanceChange
        });
        State.TrimEventLog();
    }
}
