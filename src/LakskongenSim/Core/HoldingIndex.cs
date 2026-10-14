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
public readonly record struct PopulationHolding(
    HoldingKind Kind,
    string Id,
    Zone? Zone,
    SeaSite? Site,
    RASFacility? LandFacility,
    OffshoreFarm? OffshoreFarm);

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

    /// <summary>Resolve every population the state holds, once.</summary>
    public static HoldingIndex Build(GameState state)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        var index = new HoldingIndex();
        var zones = new Dictionary<string, Zone>(StringComparer.Ordinal);
        foreach (var zone in state.Zones)
            if (!string.IsNullOrEmpty(zone.Id))
                zones.TryAdd(zone.Id, zone); // the first zone with an id wins, as a linear search found it

        foreach (var facility in state.RASFacilities)
            foreach (var tank in facility.Tanks)
                index.Add(tank, LandTank(facility));
        foreach (var farm in state.OffshoreFarms)
        {
            var zone = Lookup(zones, farm.ZoneId);
            foreach (var pen in farm.Pens)
                index.Add(pen, OffshorePen(farm, zone));
        }
        foreach (var site in state.SeaSites)
            foreach (var pen in site.Pens)
                index.Add(pen, SeaPen(pen, site, Lookup(zones, pen.ZoneId)));
        foreach (var population in state.Populations)
            index.Add(population, SeaPen(population, null, Lookup(zones, population.ZoneId)));
        return index;
    }

    internal static PopulationHolding LandTank(RASFacility facility)
        => new(HoldingKind.LandTank, facility.Id ?? "", null, null, facility, null);

    internal static PopulationHolding OffshorePen(OffshoreFarm farm, Zone? zone)
        => new(HoldingKind.Offshore, farm.Id ?? "", zone, null, null, farm);

    internal static PopulationHolding SeaPen(FishPopulation population, SeaSite? site, Zone? zone)
        => new(HoldingKind.SeaPen, population.SiteId ?? "", zone, site, null, null);

    private static Zone? Lookup(Dictionary<string, Zone> zones, string? zoneId)
        => !string.IsNullOrEmpty(zoneId) && zones.TryGetValue(zoneId, out var zone) ? zone : null;
}
