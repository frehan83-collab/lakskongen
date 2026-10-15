using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Data;
using LakskongenSim.Farm;
using LakskongenSim.Farm.Offshore;

namespace LakskongenSim.Core;

/// <summary>
/// What one facility migration did (F2-03), for tests and the load log.
/// </summary>
/// <param name="OffshoreUnits">Pre-F2 offshore farms that became offshore sea sites.</param>
/// <param name="PostSmoltSites">Pre-F2 post-smolt facilities that became post-smolt land sites.</param>
/// <param name="LandSitesPlaced">Land sites given a pad (pre-F2 RAS facilities, and post-smolt sites).</param>
/// <param name="OverflowPlots">Units that went to an overflow plot (no named plot was free and fitting).</param>
/// <param name="BatchesToPopulations">Post-smolt batches that became populations.</param>
/// <param name="ZonesMerged">Pre-F2 offshore pens merged into another to fit the unit's zones.</param>
public readonly record struct FacilityMigrationReport(int OffshoreUnits, int PostSmoltSites, int LandSitesPlaced,
    int OverflowPlots, int BatchesToPopulations, int ZonesMerged)
{
    /// <summary>True when the migration changed nothing (the state was already F2).</summary>
    public bool IsEmpty => OffshoreUnits == 0 && PostSmoltSites == 0 && LandSitesPlaced == 0;
}

/// <summary>
/// The pre-F2 facilities become the F2 unit kinds (roadmap F2-03,
/// docs/FARM_TYPES_SPEC.md §3.8): run once after a load
/// (<see cref="AdoptSavedState"/>), at the start of a step for a hand-built
/// state (<see cref="BeginHoldingDay"/>), and never again, because it is keyed
/// on what is still pre-F2 (a non-empty legacy list, a land site with no pad).
/// Running it twice changes nothing.
/// <list type="number">
/// <item><b>Offshore farm → offshore sea site</b> with the same id, on the
/// first free named slot its class is rated for (Hs100 ≤ design Hs), else an
/// overflow slot. The class gets its zones (3 semi-submersible, 6 ship, 3
/// submersible) holding the farm's pen volume; the farm's pens become the
/// zones' populations, merged pairwise when there are more pens than zones
/// (cohorts kept whole, stage, quality and genome with them). The farm's live
/// members carry over to its <see cref="OffshoreUnit"/>; its 12 m "max wave"
/// becomes the class's design Hs, and its 14-day crew change the 7-day rota.</item>
/// <item><b>Post-smolt facility → post-smolt land site</b> with the same id, on
/// a pad with post-smolt room (pad_delta first), else an overflow pad, with
/// min(4, ceil(capacity / 1.1 M fish)) modules of 2 tanks. Each smolt batch
/// becomes a population in the site's tanks with its count, weight, age
/// (smolt age + days in the facility) and vaccination, its id the batch's.</item>
/// <item><b>RAS facility without a pad</b> gets the first free pad with the
/// grow-out lots it needs (min(4, ceil(t/yr / 7,900))), else an overflow pad.
/// (The lots and physical tanks are F2-12's.)</item>
/// <item><b>The relinker runs.</b></item>
/// </list>
/// </summary>
public partial class SimulationEngine
{
    /// <summary>Fish one post-smolt module holds (≈ 1.1 M fish at 450 g per 2-tank module, spec §2A [GB]).</summary>
    internal const int PostSmoltFishPerModule = 1_100_000;

    /// <summary>Tanks per post-smolt module (2 × Ø16 m, spec §2A).</summary>
    internal const int PostSmoltTanksPerModule = 2;

    /// <summary>Water volume of one post-smolt tank (m³): a module's 3,200 m³ over its 2 tanks (spec §2A [GB]).</summary>
    internal const double PostSmoltTankVolumeM3 = 1_600.0;

    /// <summary>Yearly production of one post-smolt module (t): 480 t a year (spec §2A [GB]).</summary>
    internal const double PostSmoltTonnesPerYearPerModule = 480.0;

    /// <summary>Most post-smolt modules on one site (spec §2A).</summary>
    internal const int MaxPostSmoltModules = 4;

    /// <summary>
    /// Daily energy (kWh) of a post-smolt site: the pre-F2 facility's 30,000 NOK
    /// a day at the land tick's 0.80 NOK/kWh (derived); F2-12/F2-13 build it
    /// from the module's physical parts (3.4-5.4 kWh/kg [L:T1, L:S3]).
    /// </summary>
    internal const double PostSmoltDailyEnergyKWh = 30_000.0 / 0.80;

    /// <summary>Yearly grow-out production of one HFS lot (t HOG): 7,900 [L:R8] (lots of a pre-F2 RAS facility, spec §3.8).</summary>
    internal const double GrowOutTonnesPerLot = 7_900.0;

    /// <summary>Most grow-out lots on one pad (spec §4: pad_a holds 4).</summary>
    internal const int MaxGrowOutLots = 4;

    /// <summary>
    /// Migrate whatever is still pre-F2 (see the class summary). Returns what
    /// it did; an F2 state comes back <see cref="FacilityMigrationReport.IsEmpty"/>.
    /// </summary>
    internal FacilityMigrationReport MigrateFacilities()
    {
        if (State == null) return default;
        bool legacyOffshore = State.OffshoreFarms is { Count: > 0 };
        bool legacyPostSmolt = State.PostSmoltFacilities is { Count: > 0 };
        bool unplaced = State.RASFacilities.Any(f => string.IsNullOrEmpty(f.PlotId));
        if (!legacyOffshore && !legacyPostSmolt && !unplaced) return default;

        int offshore = 0, postSmolt = 0, placed = 0, overflow = 0, batches = 0, merged = 0;

        foreach (var farm in State.OffshoreFarms.ToList())
        {
            var (site, usedOverflow, mergedPens) = MigrateOffshoreFarm(farm);
            State.SeaSites.Add(site);
            State.OffshoreFarms.Remove(farm);
            offshore++;
            merged += mergedPens;
            if (usedOverflow) overflow++;
        }

        // Land sites already on land (pre-F2 RAS facilities) take their pads
        // first: they need grow-out lots, which fewer pads have than the
        // post-smolt room a post-smolt site needs.
        foreach (var land in State.RASFacilities)
        {
            if (!string.IsNullOrEmpty(land.PlotId)) continue;
            if (PlaceLandSite(land)) overflow++;
            placed++;
        }

        foreach (var legacy in State.PostSmoltFacilities.ToList())
        {
            var (site, populations, usedOverflow) = MigratePostSmoltFacility(legacy);
            State.RASFacilities.Add(site);
            State.PostSmoltFacilities.Remove(legacy);
            postSmolt++;
            batches += populations;
            if (usedOverflow) overflow++;
        }

        GameStateRelinker.RelinkIds(State);
        var report = new FacilityMigrationReport(offshore, postSmolt, placed, overflow, batches, merged);
        SimLog.Info($"F2-03 facility migration: {report}");
        return report;
    }

    // ── Offshore farm → offshore sea site ───────────────────────────────────

    private (SeaSite Site, bool UsedOverflow, int MergedPens) MigrateOffshoreFarm(OffshoreFarm farm)
    {
        double designHs = OffshoreUnit.DefaultDesignHsM(farm.FarmType);
        var plot = FirstFreePlot(p => p.AcceptsDesignHs(designHs));
        bool usedOverflow = plot == null;
        plot = ClaimPlot(plot ?? NextFreeOverflowPlot(PlotKind.OffshoreSlot), farm.Id);

        var unit = new OffshoreUnit
        {
            FarmType = farm.FarmType,
            StructuralIntegrity = farm.StructuralIntegrity,
            CurrentWaveHeight = farm.CurrentWaveHeight,
            IsStormLockdown = farm.IsStormLockdown,
            DaysSinceCrewRotation = farm.DaysSinceCrewRotation,
            CrewRotationDays = farm.CrewRotationDays == OffshoreUnit.LegacyCrewRotationDays
                ? OffshoreUnit.DefaultCrewRotationDays
                : farm.CrewRotationDays,
            CAPEX = farm.CAPEX,
            ConstructionProgress = farm.ConstructionProgress,
            ConstructionDays = farm.ConstructionDays,
            ConstructionStartDay = farm.ConstructionStartDay,
            EmployeeCount = farm.EmployeeCount,
            CarbonTracker = farm.CarbonTracker ?? new CarbonTracker(),
            OxygenBoost = farm.OxygenBoost,
            DesignHsM = designHs,
        };

        double totalVolume = Math.Max(1, farm.PenCount) * Math.Max(0.0, farm.PenVolumeM3);
        int zoneCount = OffshoreUnit.ZonesFor(farm.FarmType);
        var site = new SeaSite
        {
            Id = farm.Id,
            ZoneId = plot.ZoneId,
            DisplayName = farm.Name ?? "",
            Kind = SiteKind.Offshore,
            PlotId = plot.Id,
            Offshore = unit,
            MABTonnes = farm.CapacityTonnes,
            // The legacy one-figure density reads the unit as one body of
            // water (its zones are the pens); its crew and upkeep are its own.
            NumberOfPens = 1,
            PenVolume = (float)totalVolume,
            WatchMode = OffshoreWatchMode,
            PhysicalPens = OffshoreUnit.BuildZones(farm.Id, farm.FarmType, totalVolume),
        };

        // The farm's pens become the zones' populations: pairwise merges until
        // they fit, then one population per zone in order.
        var pens = farm.Pens.Where(p => p != null).ToList();
        int merged = 0;
        while (pens.Count > zoneCount)
        {
            for (int i = 0; i + 1 < pens.Count && pens.Count > zoneCount; i++)
            {
                MergePopulationInto(pens[i], pens[i + 1]);
                State.Populations.Remove(pens[i + 1]);
                pens.RemoveAt(i + 1);
                merged++;
            }
        }
        for (int i = 0; i < pens.Count; i++)
        {
            var population = pens[i];
            population.SiteId = site.Id;
            population.ZoneId = site.ZoneId;
            site.Pens.Add(population);
            site.PhysicalPens[i].PopulationId = population.Id;
            if (!State.Populations.Contains(population))
                State.Populations.Add(population);
        }
        farm.Pens.Clear();
        return (site, usedOverflow, merged);
    }

    /// <summary>
    /// Merge <paramref name="from"/>'s fish into <paramref name="into"/>, as a
    /// cohort transfer merges into a destination pen: every cohort moves whole
    /// (its age, stage, weight, CV, quality and genome with it); the lice load
    /// and the cold-water memory become the fish-weighted means; the
    /// destination keeps its ration, fasting and treatment state.
    /// </summary>
    private static void MergePopulationInto(FishPopulation into, FishPopulation from)
    {
        long intoFish = into.TotalCount, fromFish = from.TotalCount, total = intoFish + fromFish;
        if (total > 0)
        {
            into.SeaLiceLoad = (into.SeaLiceLoad * intoFish + from.SeaLiceLoad * fromFish) / total;
            into.ColdExposure = (into.ColdExposure * intoFish + from.ColdExposure * fromFish) / total;
        }
        if (into.VaccinatedDay < 0 || (from.VaccinatedDay >= 0 && from.VaccinatedDay < into.VaccinatedDay))
            into.VaccinatedDay = from.VaccinatedDay;
        into.Cohorts.AddRange(from.Cohorts);
        from.Cohorts.Clear();
    }

    // ── Post-smolt facility → post-smolt land site ──────────────────────────

    private (RASFacility Site, int Populations, bool UsedOverflow) MigratePostSmoltFacility(PostSmoltFacility legacy)
    {
        int modules = Math.Clamp((int)Math.Ceiling(legacy.AnnualSmoltCapacity / (double)PostSmoltFishPerModule),
            1, MaxPostSmoltModules);
        var site = NewPostSmoltSite(legacy.Id, legacy.Name, modules);
        site.CAPEX = legacy.CAPEX;
        site.ConstructionProgress = legacy.ConstructionProgress;
        site.ConstructionDays = legacy.ConstructionDays;
        site.ConstructionStartDay = legacy.ConstructionStartDay;
        site.EmployeeCount = legacy.EmployeeCount;
        site.TargetWaterTemp = legacy.WaterTemp;
        site.CurrentWaterTemp = legacy.WaterTemp;
        bool usedOverflow = PlaceLandSite(site);

        var strain = SmoltStrainRegistry.Get("standard");
        int populations = 0;
        foreach (var batch in legacy.Batches)
        {
            if (batch == null) continue;
            var population = new FishPopulation
            {
                Id = batch.Id,
                SiteId = site.Id,
                ZoneId = site.ZoneId,
                SeaLiceLoad = 0.0,
                VaccinatedDay = batch.VaccinationComplete ? State.Day : -1,
            };
            population.InjectDependencies(Growth, Mortality);
            population.Cohorts.Add(new FishPopulation.Cohort(Hatchery.MaturationDays + batch.DaysInFacility,
                batch.Count, batch.AverageWeightG / 1000.0, LifeStage.Smolt, strain.BuildGenome()));
            site.Tanks.Add(population);
            State.Populations.Add(population);
            populations++;
        }
        legacy.Batches.Clear();
        return (site, populations, usedOverflow);
    }

    /// <summary>
    /// A post-smolt land site of <paramref name="modules"/> 2-tank modules (not
    /// yet on a pad): the pre-F2 post-smolt facility's staff and water
    /// temperature, the module tanks and yearly production of spec §2A.
    /// </summary>
    internal RASFacility NewPostSmoltSite(string id, string name, int modules)
    {
        var legacy = new PostSmoltFacility();
        int m = Math.Clamp(modules, 1, MaxPostSmoltModules);
        return new RASFacility
        {
            Id = id,
            Name = name,
            Kind = LandSiteKind.PostSmolt,
            Scale = RASScale.Small,
            TankCount = PostSmoltTanksPerModule * m,
            TankVolumeM3 = PostSmoltTankVolumeM3,
            CapacityTonnesPerYear = PostSmoltTonnesPerYearPerModule * m,
            EmployeeCount = legacy.EmployeeCount,
            DailyEnergyKWh = PostSmoltDailyEnergyKWh,
            TargetWaterTemp = legacy.WaterTemp,
            CurrentWaterTemp = legacy.WaterTemp,
            ConstructionDays = legacy.ConstructionDays,
        };
    }

    /// <summary>Post-smolt modules of a post-smolt land site (its tanks over the module's two).</summary>
    public static int PostSmoltModulesOf(RASFacility site)
        => site == null ? 0 : Math.Max(1, site.TankCount / PostSmoltTanksPerModule);

    // ── Land sites onto pads ────────────────────────────────────────────────

    /// <summary>Grow-out lots a land site of <paramref name="tonnesPerYear"/> needs (1-4).</summary>
    internal static int GrowOutLotsFor(double tonnesPerYear)
        => Math.Clamp((int)Math.Ceiling(Math.Max(0.0, tonnesPerYear) / GrowOutTonnesPerLot), 1, MaxGrowOutLots);

    /// <summary>True when <paramref name="plot"/> can take <paramref name="land"/> (its lots or its modules).</summary>
    internal static bool PadFits(SitePlot plot, RASFacility land)
        => plot.Kind == PlotKind.LandPad && (land.Kind == LandSiteKind.PostSmolt
            ? plot.PostSmoltModules >= PostSmoltModulesOf(land)
            : plot.PhaseLots >= GrowOutLotsFor(land.CapacityTonnesPerYear));

    /// <summary>
    /// The first free named pad <paramref name="land"/> fits on: a post-smolt
    /// site prefers a pad without grow-out lots (pad_delta), a grow-out site
    /// takes catalogue order. Null when none is free.
    /// </summary>
    internal SitePlot? FirstFreePadFor(RASFacility land)
    {
        if (land.Kind == LandSiteKind.PostSmolt)
            return FirstFreePlot(p => PadFits(p, land) && p.PhaseLots == 0)
                ?? FirstFreePlot(p => PadFits(p, land));
        return FirstFreePlot(p => PadFits(p, land));
    }

    /// <summary>
    /// Put <paramref name="land"/> on its pad (the first free fitting pad, else
    /// an overflow pad) and its tank fish in the pad's zone. Returns true when it
    /// went to an overflow pad.
    /// </summary>
    private bool PlaceLandSite(RASFacility land)
    {
        var plot = FirstFreePadFor(land);
        bool usedOverflow = plot == null;
        plot = ClaimPlot(plot ?? NextFreeOverflowPlot(PlotKind.LandPad), land.Id);
        land.PlotId = plot.Id;
        land.ZoneId = plot.ZoneId;
        foreach (var tank in land.Tanks)
        {
            tank.SiteId = land.Id;
            tank.ZoneId = land.ZoneId;
        }
        return usedOverflow;
    }
}
