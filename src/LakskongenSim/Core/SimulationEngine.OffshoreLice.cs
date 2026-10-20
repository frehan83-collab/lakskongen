using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Farm;
using LakskongenSim.Finance;

namespace LakskongenSim.Core;

/// <summary>
/// The lice coupling of offshore units (roadmap F2-08, docs/FARM_TYPES_SPEC.md
/// §3.3 "Lice and zones", F2-08 acceptance 6-7):
/// <list type="bullet">
/// <item>The plot's multiplier (<c>ExternalLicePressureFor</c>) scales only the
/// external pressure: each unit's site lice model keeps the copepodids that
/// arrive from outside apart and scales them by the season × the plot, while
/// its own lice reproduce at the season's pressure as on any sea site
/// (<see cref="Health.SeaLiceModel.SeparatesExternalPressure"/>, set here in the
/// Metocean phase, before the HealthPrep phase advances the models).</item>
/// <item>Offshore units in one production area seed each other's own cloud
/// with their adult females (the engine's farm-to-farm coupling, outside the
/// plot's multiplier), on top of the share the engine's farm-to-farm loop
/// gives every site.</item>
/// <item>A fjord zone's traffic-light mean carries its production area's
/// units' larval output without their fish (they never dilute it), and a unit
/// whose licences are placed in the zone as the zone's own fish (F2-09).</item>
/// </list>
/// A game with no offshore unit runs none of this.
/// </summary>
public partial class SimulationEngine
{
    /// <summary>True when the game holds an offshore unit.</summary>
    internal bool HasOffshoreUnits() => OffshoreUnits.Count > 0;

    /// <summary>
    /// The Metocean phase's lice step for the units: each unit's model splits
    /// its pressure (own at the season's, outside at the season × the plot's),
    /// and units in one production area seed each other's own cloud.
    /// </summary>
    internal void CoupleOffshoreUnitLice()
    {
        var units = OffshoreUnits;
        if (units.Count == 0) return;
        double seasonal = SeasonalLicePressure(State.Day);
        var c = OffshoreCal.LiceCoupling;
        var seeds = new double[units.Count];
        for (int i = 0; i < units.Count; i++)
        {
            var site = units[i];
            var lice = site.Lice;
            lice.SeparatesExternalPressure = true;
            lice.OwnPressureMultiplier = seasonal;
            lice.ExternalOnlyMultiplier = seasonal * ExternalLicePressureFor(site);
            if (!site.Pens.Any(p => p.TotalCount > 0)) continue;
            int area = ProductionAreaOfSite(site);
            double inflow = 0.0;
            for (int j = 0; j < units.Count; j++)
            {
                if (j == i || !units[j].Pens.Any(p => p.TotalCount > 0) || ProductionAreaOfSite(units[j]) != area) continue;
                inflow += units[j].Lice.AdultFemalePerFish * c.UnitToUnitCoupling;
            }
            if (inflow > 0.0001) seeds[i] = Math.Min(inflow * c.SeedScale, c.SeedCap);
        }
        for (int i = 0; i < units.Count; i++)
            if (seeds[i] > 0.0)
                units[i].Lice.SeedLocalInfection(seeds[i]);
    }

    /// <summary>True when <paramref name="population"/> is the first population of its zone in the population list (the zone mean's anchor).</summary>
    internal bool IsFirstPopulationOfItsZone(FishPopulation population)
    {
        foreach (var pop in State.Populations)
            if (pop.ZoneId == population.ZoneId)
                return ReferenceEquals(pop, population);
        return false;
    }

    /// <summary>
    /// What the offshore units of a fjord zone's production area add to the
    /// zone's lice mean: a unit whose licences are placed in the zone its fish
    /// and their lice; every other unit with fish its larval output (fish ×
    /// lice per fish) at the calibrated weight, without fish.
    /// </summary>
    internal ZoneLiceShare OffshoreZoneLiceShare(string zoneId)
    {
        var zone = FindZone(zoneId);
        if (zone == null) return default;
        int area = ProductionAreaOf(zone);
        double weight = OffshoreCal.LiceCoupling.ZoneOutputWeight;
        double lice = 0.0;
        int fish = 0;
        foreach (var site in OffshoreUnits)
        {
            if (ProductionAreaOfSite(site) != area) continue;
            int unitFish = 0;
            double unitLice = 0.0;
            foreach (var pop in site.Pens)
            {
                int n = pop.Cohorts.Sum(c => c.Count);
                unitFish += n;
                unitLice += n * pop.SeaLiceLoad;
            }
            if (unitFish <= 0) continue;
            if (UnitLicencesPlacedIn(site, zoneId))
            {
                lice += unitLice;
                fish = checked(fish + unitFish);
            }
            else
            {
                lice += weight * unitLice;
            }
        }
        return new ZoneLiceShare(lice, fish);
    }

    /// <summary>True when every licence bound to the unit is converted and placed in <paramref name="zoneId"/>.</summary>
    internal bool UnitLicencesPlacedIn(SeaSite site, string zoneId)
    {
        if (!UnitLicencesPlaced(site)) return false;
        return State.LicenceHoldings.Where(l => l.BoundUnitId == site.Id).All(l => l.PlacedZoneId == zoneId);
    }

    /// <summary>
    /// True when a unit's fish count against its production area's licence
    /// pool (F2-09): its licences converted and placed in the area it stands in.
    /// </summary>
    internal bool UnitCountsInItsAreaPool(SeaSite site)
    {
        if (!UnitLicencesPlaced(site)) return false;
        int area = ProductionAreaOfSite(site);
        return State.LicenceHoldings.Where(l => l.BoundUnitId == site.Id).All(l => l.PooledArea == area);
    }
}
