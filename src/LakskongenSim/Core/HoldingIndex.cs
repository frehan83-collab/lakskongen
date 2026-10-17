using System;
using System.Collections.Generic;
using LakskongenSim.Biology;
using LakskongenSim.Environment;
using LakskongenSim.Farm;

namespace LakskongenSim.Core;

/// <summary>
/// What holds one population (the farm-type seam): a sea site's pen, an
/// offshore structure's pen or a land-based tank, with the objects the water
/// and the hazards are read from. Resolved by
/// <see cref="SimulationEngine.HoldingOf(FishPopulation)"/>; the water at the
/// fish is <see cref="SimulationEngine.HoldingWaterOf(FishPopulation)"/>.
/// </summary>
/// <param name="Kind">What holds the fish.</param>
/// <param name="Id">The holding's id, the mortality ledger's site key: the sea site id the population carries, the offshore farm's id or the land facility's id.</param>
/// <param name="Zone">The zone whose water a sea pen or an offshore pen takes (null for a land tank and for a zone id the game does not know).</param>
/// <param name="Site">The sea site whose pen list holds the population (null for a population in no site's pens).</param>
/// <param name="LandFacility">The land-based facility whose tank holds the population.</param>
/// <param name="OffshoreFarm">The offshore farm whose pen holds the population.</param>
/// <param name="Pen">The sea site's production unit holding the population (S2-01b): the volume its density is read in; null outside a site's pens.</param>
public readonly record struct PopulationHolding(
    HoldingKind Kind,
    string Id,
    Zone? Zone,
    SeaSite? Site,
    RASFacility? LandFacility,
    OffshoreFarm? OffshoreFarm,
    Pen? Pen = null);

/// <summary>
/// Every population's holding for one simulated step, resolved once
/// (sim-a-1: the hazards, growth, feeding and disease used to scan the facility
/// lists and the zones for each population, several times a day). Built at the
/// start of a step from the facility, site and population lists in the
/// precedence the resolver always had (land tanks, then offshore pens, then sea
/// pens); a population created during the step is resolved on first use and
/// added. Holds references only: the water is read live from the holding, so a
/// value that changes during the day (zone oxygen after a plankton crash, a
/// tank's temperature after the facility tick) is seen as before.
/// </summary>
internal sealed class HoldingIndex
{
    private readonly Dictionary<FishPopulation, PopulationHolding> _byPopulation =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>Populations resolved in this index.</summary>
    public int Count => _byPopulation.Count;

    public bool TryGet(FishPopulation population, out PopulationHolding holding)
        => _byPopulation.TryGetValue(population, out holding);

    /// <summary>Record a holding unless the population already has one (the first, highest-precedence holding wins).</summary>
    public void Add(FishPopulation population, in PopulationHolding holding)
        => _byPopulation.TryAdd(population, holding);

    /// <summary>
    /// Resolve every population the state holds, once: each population's unit
    /// by the one precedence (<see cref="UnitResolver.ForEach"/>, F2-03), mapped
    /// to its holding.
    /// </summary>
    public static HoldingIndex Build(GameState state)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        var index = new HoldingIndex();
        var zones = new Dictionary<string, Zone>(StringComparer.Ordinal);
        foreach (var zone in state.Zones)
            if (!string.IsNullOrEmpty(zone.Id))
                zones.TryAdd(zone.Id, zone); // the first zone with an id wins, as a linear search found it

        UnitResolver.ForEach(state, (population, unit) =>
        {
            if (!index._byPopulation.ContainsKey(population))
                index.Add(population, HoldingOfUnit(population, unit, zoneId => Lookup(zones, zoneId)));
        });
        return index;
    }

    /// <summary>
    /// The holding of <paramref name="population"/> in <paramref name="unit"/>:
    /// a land site's tank, an offshore unit's zone (or a pre-F2 offshore farm's
    /// pen), a fjord site's pen, or a sea pen in no site.
    /// </summary>
    internal static PopulationHolding HoldingOfUnit(FishPopulation population, in FarmUnit unit, Func<string?, Zone?> zoneOf)
    {
        if (unit.LandSite != null) return LandTank(unit.LandSite, population);
        if (unit.LegacyOffshoreFarm != null) return OffshorePen(unit.LegacyOffshoreFarm, zoneOf(unit.LegacyOffshoreFarm.ZoneId));
        if (unit.Site is { Kind: SiteKind.Offshore } offshore) return OffshoreSitePen(population, offshore, zoneOf(offshore.ZoneId));
        return SeaPen(population, unit.Site, zoneOf(population.ZoneId));
    }

    /// <summary>
    /// A land site's tank: the site, and (F2-12) the physical tank holding the
    /// population as its production unit (<see cref="Farm.Land.LandTank.Pen"/>:
    /// its volume and occupancy), null at a pre-F2 site or for fish in no tank.
    /// </summary>
    internal static PopulationHolding LandTank(RASFacility facility, FishPopulation? population = null)
        => new(HoldingKind.LandTank, facility.Id ?? "", null, null, facility, null, facility.TankOf(population)?.Pen);

    internal static PopulationHolding OffshorePen(OffshoreFarm farm, Zone? zone)
        => new(HoldingKind.Offshore, farm.Id ?? "", zone, null, null, farm);

    /// <summary>
    /// A zone of an offshore unit (F2-03): an offshore holding whose site is the
    /// unit's sea site and whose pen is the zone; its zone is the plot's water
    /// area, which no fjord zone is (null), so the water is the area's sea.
    /// </summary>
    internal static PopulationHolding OffshoreSitePen(FishPopulation population, SeaSite site, Zone? zone)
        => new(HoldingKind.Offshore, site.Id ?? "", zone, site, null, null, site.PenOf(population));

    internal static PopulationHolding SeaPen(FishPopulation population, SeaSite? site, Zone? zone)
        => new(HoldingKind.SeaPen, population.SiteId ?? "", zone, site, null, null, site?.PenOf(population));

    private static Zone? Lookup(Dictionary<string, Zone> zones, string? zoneId)
        => !string.IsNullOrEmpty(zoneId) && zones.TryGetValue(zoneId, out var zone) ? zone : null;
}
