using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Data;
using LakskongenSim.Environment;
using LakskongenSim.Events;
using LakskongenSim.Farm;
using LakskongenSim.Harvest;

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
        MigrateFacilities(); // F2-03: a hand-built pre-F2 state becomes the unit kinds first
        if (count <= 0)
            return CommandResult.Fail("invalid_count", $"Smolt count must be positive (got {count})");
        var facility = RASFacilities.FirstOrDefault(f => f.Id == facilityId);
        if (facility == null)
            return CommandResult.Fail("facility_not_found", $"RAS facility '{facilityId}' not found");
        if (facility.Kind == LandSiteKind.PostSmolt)
            return CommandResult.Fail(WrongSiteKindKey,
                $"'{facility.Name}' is a post-smolt site: start a post-smolt batch there");
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
        MigrateFacilities(); // F2-03: a hand-built pre-F2 state becomes the unit kinds first
        var facility = RASFacilities.FirstOrDefault(f => f.Id == facilityId);
        if (facility == null)
            return CommandResult.Fail("facility_not_found", $"RAS facility '{facilityId}' not found");
        return HarvestNowAs(facility.Id, null, 1.0, HarvestMode.Express, "facility_empty",
            $"'{facility.Name}' has no fish to harvest");
    }

    // ── Offshore ───────────────────────────────────────────────────────────

    /// <summary>
    /// Stock smolts in an operational offshore unit (F2-03: an offshore sea
    /// site), in its first empty zone. The unit's MTB (its locality MTB until
    /// F2-09 binds its licences) caps the standing biomass; the smolts carry the
    /// strain's lice load scaled by the plot's external lice pressure. Fails
    /// before any mutation: engine_not_ready / invalid_count /
    /// facility_not_found / not_operational / capacity_exceeded / pens_full /
    /// insufficient_funds.
    /// </summary>
    public CommandResult StockOffshoreFarm(string farmId, int count, string strainId = "standard")
    {
        if (State == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        MigrateFacilities(); // F2-03: a hand-built pre-F2 state becomes the unit kinds first
        if (count <= 0)
            return CommandResult.Fail("invalid_count", $"Smolt count must be positive (got {count})");
        var site = FindOffshoreUnit(farmId);
        if (site?.Offshore == null)
            return CommandResult.Fail("facility_not_found", $"Offshore unit '{farmId}' not found");
        if (!site.Offshore.IsOperational)
            return CommandResult.Fail("not_operational",
                $"'{site.Label}' is still under construction ({site.Offshore.ConstructionProgress:P0})");
        if (site.Offshore.ShakedownEndDay > Weather.SimulationDay) // F2-07: the shakedown delays the first stocking (the sea day: in-day bookings too)
            return CommandResult.Fail(OffshoreReasons.ShakedownActive, $"'{site.Label}' runs its shakedown until day {site.Offshore.ShakedownEndDay}");

        var strain = SmoltStrainRegistry.Get(strainId);
        var refused = CheckFacilityCapacity(site.CurrentBiomassTonnes * 1000.0, count, strain, site.MABTonnes, site.Label);
        if (refused != null) return refused;
        var zone = site.PhysicalPens.OrderBy(p => p.Number).FirstOrDefault(site.IsPenFree);
        if (zone == null)
            return CommandResult.Fail(PenReasons.PensFull, $"Every zone of '{site.Label}' holds fish");

        decimal cost = count * _balancing.SmoltCostPerFish * (decimal)strain.CostMultiplier;
        if (!TryPayCost(cost, CostBooking.Cogs, "smolts"))
            return CommandResult.Fail("insufficient_funds",
                $"Insufficient funds: need {cost:N0} NOK for {count:N0} smolts", (double)cost);
        AttributeUnitCost(site.Id, HoldingKind.Offshore, cost); // F2-04 ring-fence (a liable unit keeps it)

        double liceLoad = strain.InitialLiceLoad * PlotLiceMultiplierOf(site);
        PlacePopulation(site, zone, new FishPopulation.Cohort(Hatchery.MaturationDays, count, strain.ReleaseWeightKg,
            LifeStage.Smolt, strain.BuildGenome()), liceLoad);
        site.LastStockedDay = State.Day;
        // The unit's own lice model starts from the smolts' load, as a fjord
        // stocking seeds its site's (at the plot's lower pressure).
        if (liceLoad > 0.0)
            site.Lice.SeedExternalInfection(liceLoad);
        LogFacilityEvent("Utsett av smolt",
            SimText.Nb($"Satte ut {count:N0} smolt ({strain.Name}) i {site.Label}, sone {zone.Number}"), -cost);
        return CommandResult.Success($"Stocked {count:N0} smolts at {site.Label} (zone {zone.Number})", count, (double)cost, site.Id);
    }

    /// <summary>
    /// Harvest every zone of an offshore unit now (express, through the S2-04
    /// pipeline with the offshore wellboat; sea-farmed, so it counts for the
    /// resource-rent tax through <see cref="IsResourceRentLiable"/>).
    /// </summary>
    public CommandResult HarvestOffshoreFarm(string farmId)
    {
        if (State == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        MigrateFacilities(); // F2-03: a hand-built pre-F2 state becomes the unit kinds first
        var site = FindOffshoreUnit(farmId);
        if (site == null)
            return CommandResult.Fail("facility_not_found", $"Offshore unit '{farmId}' not found");
        return HarvestNowAs(site.Id, null, 1.0, HarvestMode.Express, "facility_empty",
            $"'{site.Label}' has no fish to harvest");
    }

    // ── Post-smolt ─────────────────────────────────────────────────────────

    /// <summary>Weight (g) a post-smolt batch is grown to before it may go to sea (the pre-F2 facility's target).</summary>
    internal const double PostSmoltTargetWeightG = 300.0;

    /// <summary>Weight (g) a post-smolt batch starts at (the pre-F2 facility's 80 g smolts).</summary>
    internal const double PostSmoltStartWeightG = 80.0;

    /// <summary>Vaccine cost per fish (NOK; the pre-F2 facility's 3.5, NOK 3-4 per fish).</summary>
    internal const decimal PostSmoltVaccinationCostPerFish = 3.5m;

    /// <summary>Refusal key of a fjord verb asked of an offshore unit (F2-03).</summary>
    public const string OffshoreUnitKey = "offshore_unit";

    /// <summary>Refusal key of a land verb asked of the other land site kind (F2-03).</summary>
    public const string WrongSiteKindKey = "wrong_site_kind";

    /// <summary>
    /// The post-smolt batch <paramref name="batchId"/> of land site
    /// <paramref name="siteId"/>: one of the site's tank populations (F2-03: a
    /// pre-F2 batch became a population with the batch's id).
    /// </summary>
    private (RASFacility? Site, FishPopulation? Batch) FindPostSmoltBatch(string siteId, string batchId)
    {
        var site = FindLandSite(siteId);
        if (site == null || site.Kind != LandSiteKind.PostSmolt) return (null, null);
        return (site, site.Tanks.FirstOrDefault(p => p.Id == batchId));
    }

    /// <summary>
    /// Start a post-smolt batch of standard 80 g smolts in an operational
    /// post-smolt land site (paid as smolts): a population in the site's tanks
    /// that grows in the site's water until it reaches 300 g and is vaccinated,
    /// then <see cref="TransferPostSmoltToSea"/>. The site holds at most 1.1 M
    /// fish a module at once. Value = cost, Id = batch id ("&lt;site&gt;_batch_N").
    /// </summary>
    public CommandResult StartPostSmoltBatch(string facilityId, int count)
    {
        if (State == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        MigrateFacilities(); // F2-03: a hand-built pre-F2 state becomes the unit kinds first
        if (count <= 0)
            return CommandResult.Fail("invalid_count", $"Smolt count must be positive (got {count})");
        var site = FindLandSite(facilityId);
        if (site == null || site.Kind != LandSiteKind.PostSmolt)
            return CommandResult.Fail("facility_not_found", $"Post-smolt site '{facilityId}' not found");
        if (!site.IsOperational)
            return CommandResult.Fail("not_operational",
                $"'{site.Name}' is still under construction ({site.ConstructionProgress:P0})");
        long capacity = (long)PostSmoltModulesOf(site) * PostSmoltFishPerModule;
        if (site.Tanks.Sum(p => (long)p.TotalCount) + count > capacity)
            return CommandResult.Fail("capacity_exceeded",
                $"'{site.Name}' holds at most {capacity:N0} smolts at once");

        decimal cost = count * _balancing.SmoltCostPerFish;
        if (!TryPayCost(cost, CostBooking.Cogs, "smolts"))
            return CommandResult.Fail("insufficient_funds",
                $"Insufficient funds: need {cost:N0} NOK for {count:N0} smolts", (double)cost);
        AttributeUnitCost(site.Id, HoldingKind.LandTank, cost); // F2-04 ring-fence

        string batchId = NextFacilityId($"{site.Id}_batch", site.Tanks.Select(p => p.Id));
        var batch = new FishPopulation { Id = batchId, SiteId = site.Id, ZoneId = site.ZoneId, SeaLiceLoad = 0.0 };
        batch.InjectDependencies(Growth, Mortality);
        batch.Cohorts.Add(new FishPopulation.Cohort(Hatchery.MaturationDays, count, PostSmoltStartWeightG / 1000.0,
            LifeStage.Smolt, SmoltStrainRegistry.Get("standard").BuildGenome()));
        site.Tanks.Add(batch);
        State.Populations.Add(batch);
        LogFacilityEvent("Ny postsmoltgruppe", SimText.Nb($"{site.Name}: ny gruppe med {count:N0} smolt"), -cost);
        return CommandResult.Success($"Started batch {batchId} ({count:N0} smolts) at {site.Name}",
            (double)cost, id: batchId);
    }

    /// <summary>Vaccinate a post-smolt batch (OPEX, 3.5 NOK a fish). Required before transfer to sea.</summary>
    public CommandResult VaccinatePostSmoltBatch(string facilityId, string batchId)
    {
        if (State == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        MigrateFacilities(); // F2-03: a hand-built pre-F2 state becomes the unit kinds first
        var (site, batch) = FindPostSmoltBatch(facilityId, batchId);
        if (site == null || batch == null)
            return CommandResult.Fail("batch_not_found", $"Batch '{batchId}' not found");
        if (batch.VaccinatedDay >= 0)
            return CommandResult.Fail("already_vaccinated", $"Batch '{batchId}' is already vaccinated");
        decimal cost = batch.TotalCount * PostSmoltVaccinationCostPerFish;
        if (!TryPayCost(cost, CostBooking.Opex, "vaccination"))
            return CommandResult.Fail("insufficient_funds",
                $"Insufficient funds: need {cost:N0} NOK to vaccinate batch '{batchId}'", (double)cost);
        AttributeUnitCost(site.Id, HoldingKind.LandTank, cost); // F2-04 ring-fence
        batch.VaccinatedDay = State.Day;
        LogFacilityEvent("Postsmolt vaksinert", SimText.Nb($"{site.Name}: gruppe {batchId} vaksinert for {cost:N0} kr"), -cost);
        return CommandResult.Success($"Vaccinated batch {batchId}", (double)cost, id: batchId);
    }

    /// <summary>True when a post-smolt batch may go to sea: grown to 300 g and vaccinated.</summary>
    public static bool IsPostSmoltReady(FishPopulation batch)
        => batch != null && batch.TotalCount > 0 && batch.VaccinatedDay >= 0
            && batch.GetTotalBiomassKg() / batch.TotalCount * 1000.0 >= PostSmoltTargetWeightG;

    /// <summary>
    /// Move a ready post-smolt batch to a fjord site: its fish leave the land
    /// site (booked as a transfer out) and go into empty pens at the batch's
    /// weight and age, with its genome and vaccination; the quality tier follows
    /// the weight (SuperSmolt from 400 g, large smolt from 200 g). The site's MAB,
    /// the S2-01a capacity check and the pens' room apply, like any stocking.
    /// </summary>
    public CommandResult TransferPostSmoltToSea(string facilityId, string batchId, string siteId)
    {
        if (State == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        MigrateFacilities(); // F2-03: a hand-built pre-F2 state becomes the unit kinds first
        var (land, batch) = FindPostSmoltBatch(facilityId, batchId);
        if (land == null || batch == null || batch.TotalCount <= 0)
            return CommandResult.Fail("batch_not_found", $"Batch '{batchId}' not found");
        int count = batch.TotalCount;
        double weightKg = batch.GetTotalBiomassKg() / count;
        if (!IsPostSmoltReady(batch))
            return CommandResult.Fail("batch_not_ready",
                $"Batch '{batchId}' is not ready ({weightKg * 1000.0:F0} of {PostSmoltTargetWeightG:F0} g" +
                (batch.VaccinatedDay >= 0 ? ")" : ", not vaccinated)"));
        var site = State.SeaSites.FirstOrDefault(s => s.Id == siteId);
        if (site == null)
            return CommandResult.Fail("site_not_found", $"Site '{siteId}' not found");
        if (site.Kind != SiteKind.Fjord)
            return CommandResult.Fail(OffshoreUnitKey, $"'{site.Label}' is an offshore unit: smolts go in through its own verb");

        double incomingTonnes = count * weightKg / 1000.0;
        if (site.CurrentBiomassTonnes + incomingTonnes > site.MABTonnes)
            return CommandResult.Fail("mab_exceeded",
                $"Transferring {incomingTonnes:F1} t would put {siteId} over its MAB ({site.MABTonnes:F0} t)");
        // S2-01a: a transfer to sea is a stocking; the plan's peak must fit.
        var capacity = CheckStockingCapacity(site, count, weightKg);
        if (capacity != null) return capacity;
        // S2-01b: the post-smolts go into empty pens like any stocking.
        var penRoom = CheckSitePenRoom(site, count, ForecastPenStocking(site, weightKg), out var allocation);
        if (penRoom != null) return penRoom;

        double weightG = weightKg * 1000.0;
        var tier = weightG >= SuperSmoltTransferWeightG ? SmoltQualityTier.SuperSmolt
            : weightG >= LargeSmoltTransferWeightG ? SmoltQualityTier.Large
            : SmoltQualityTier.Standard;
        var source = batch.Cohorts.First(c => c.Count > 0);
        int ageDays = source.AgeDays;
        var strain = SmoltStrainRegistry.Get("standard");
        var genome = source.Genetics ?? strain.BuildGenome();

        // The fish leave the land site through the ledger (a transfer is a removal).
        foreach (var cohort in batch.Cohorts)
            batch.RemoveAlive(cohort, cohort.Count, FishRemoval.TransferOut);
        land.Tanks.Remove(batch);
        State.Populations.Remove(batch);

        foreach (var (pen, n) in allocation!)
        {
            var placed = PlacePopulation(site, pen, new FishPopulation.Cohort(ageDays, n, weightKg, LifeStage.Smolt, genome)
            {
                Quality = tier
            }, strain.InitialLiceLoad);
            placed.VaccinatedDay = batch.VaccinatedDay;
        }
        site.LastStockedDay = State.Day;
        if (strain.InitialLiceLoad > 0)
            site.Lice.SeedExternalInfection(Math.Max(0.05, strain.InitialLiceLoad));

        LogFacilityEvent("Postsmolt flyttet til sjø",
            SimText.Nb($"{land.Name}: {count:N0} postsmolt ({weightG:F0} g) til {site.Label}"), 0m);
        return CommandResult.Success($"Transferred {count:N0} post-smolt ({tier}) to {siteId}",
            count, weightG, siteId);
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
        AttributeUnitCost(facilityId, HoldingKind.LandTank, cost); // F2-04 ring-fence

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
