using System;
using System.Collections.Generic;
using LakskongenSim.Biology;
using LakskongenSim.Environment;
using LakskongenSim.Farm;

namespace LakskongenSim.Core;

/// <summary>
/// The production unit a population lives in (roadmap F2-03,
/// docs/FARM_TYPES_SPEC.md §3.1 decision 2): a sea site (a fjord locality or
/// an offshore unit) or a land site. <see cref="Site"/> and
/// <see cref="LandSite"/> are never both set; <see cref="LegacyOffshoreFarm"/>
/// is a pre-F2 offshore farm in a state the load migration has not yet seen
/// (a hand-built state; the next step normalizes it).
/// </summary>
public readonly record struct FarmUnit(SeaSite? Site, RASFacility? LandSite, OffshoreFarm? LegacyOffshoreFarm)
{
    /// <summary>No unit holds the population (a hatchery cohort, or a population outside every unit).</summary>
    public static readonly FarmUnit None = new(null, null, null);

    /// <summary>True when no unit holds the population.</summary>
    public bool IsNone => Site == null && LandSite == null && LegacyOffshoreFarm == null;

    /// <summary>What holds the fish: a land tank, an offshore pen or a sea pen.</summary>
    public HoldingKind Kind => LandSite != null ? HoldingKind.LandTank
        : LegacyOffshoreFarm != null || Site?.Kind == SiteKind.Offshore ? HoldingKind.Offshore
        : HoldingKind.SeaPen;

    /// <summary>The unit's id ("" for none).</summary>
    public string Id => Site?.Id ?? LandSite?.Id ?? LegacyOffshoreFarm?.Id ?? "";

    /// <summary>The unit's zone: the fjord zone, the plot's own water area, or "" (a pre-F2 facility not yet placed).</summary>
    public string ZoneId => Site?.ZoneId ?? LandSite?.ZoneId ?? LegacyOffshoreFarm?.ZoneId ?? "";
}

/// <summary>
/// Resolves every non-hatchery population to exactly one unit (roadmap F2-03,
/// docs/FARM_TYPES_SPEC.md §3.1 decision 2 and §3.1a). One precedence for the
/// whole game, the one the holding seam always had: land tanks, then a pre-F2
/// offshore farm's pens (only in a state the migration has not seen), then the
/// sea sites' pens in list order (offshore units and fjord localities alike),
/// then nothing. <see cref="HoldingIndex.Build"/> resolves a whole step through
/// <see cref="ForEach"/>; a lookup outside a step goes through
/// <see cref="Resolve"/>, the same precedence for one population.
/// <para>
/// A population no unit holds is exempt when it is a hatchery cohort
/// (<see cref="IsHatcheryExempt"/>: the eggs and juveniles
/// <c>Hatchery.OnNewCohort</c> creates have no site by design); any other is a
/// defect <see cref="Violations"/> names.
/// </para>
/// </summary>
public static class UnitResolver
{
    /// <summary>The unit that holds <paramref name="population"/>, by the one precedence (<see cref="FarmUnit.None"/> when none).</summary>
    public static FarmUnit Resolve(GameState state, FishPopulation population)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (population == null) throw new ArgumentNullException(nameof(population));
        foreach (var land in state.RASFacilities)
            if (Holds(land.Tanks, population))
                return new FarmUnit(null, land, null);
        foreach (var farm in state.OffshoreFarms)
            if (Holds(farm.Pens, population))
                return new FarmUnit(null, null, farm);
        foreach (var site in state.SeaSites)
            if (Holds(site.Pens, population))
                return new FarmUnit(site, null, null);
        return FarmUnit.None;
    }

    /// <summary>
    /// Visit every population a unit holds, in the precedence order (land
    /// tanks, pre-F2 offshore pens, sea-site pens in list order), then every
    /// population of <see cref="GameState.Populations"/> with
    /// <see cref="FarmUnit.None"/>. A population is visited once per list that
    /// holds it; the caller keeps the first (the highest precedence).
    /// </summary>
    public static void ForEach(GameState state, Action<FishPopulation, FarmUnit> visit)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (visit == null) throw new ArgumentNullException(nameof(visit));
        foreach (var land in state.RASFacilities)
            foreach (var tank in land.Tanks)
                visit(tank, new FarmUnit(null, land, null));
        foreach (var farm in state.OffshoreFarms)
            foreach (var pen in farm.Pens)
                visit(pen, new FarmUnit(null, null, farm));
        foreach (var site in state.SeaSites)
            foreach (var pen in site.Pens)
                visit(pen, new FarmUnit(site, null, null));
        foreach (var population in state.Populations)
            visit(population, FarmUnit.None);
    }

    /// <summary>
    /// True when <paramref name="population"/> is a hatchery cohort, which has
    /// no site by design: the only populations created without a site id are
    /// the hatchery's (<c>Hatchery.OnNewCohort</c>, the engine's wiring in
    /// <c>SimulationEngine.Initialize</c>); every stocking, transfer, scenario
    /// start and facility verb gives its population its unit's id.
    /// </summary>
    public static bool IsHatcheryExempt(FishPopulation population)
    {
        if (population == null) throw new ArgumentNullException(nameof(population));
        return string.IsNullOrEmpty(population.SiteId);
    }

    /// <summary>
    /// Every way the game breaks "each non-hatchery population lives in exactly
    /// one unit, with its site id and zone id" (F2-03 acceptance 5): a
    /// population in no unit (and not a hatchery cohort), in two units' lists,
    /// with a site id that is not its unit's, or with an empty zone id. Empty
    /// when the game is sound. Ids name the population by its list index, never
    /// by its Guid.
    /// </summary>
    public static IReadOnlyList<string> Violations(GameState state)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        var holders = new Dictionary<FishPopulation, List<FarmUnit>>(ReferenceEqualityComparer.Instance);
        ForEach(state, (population, unit) =>
        {
            if (unit.IsNone) return;
            if (!holders.TryGetValue(population, out var list))
                holders[population] = list = new List<FarmUnit>();
            list.Add(unit);
        });

        var problems = new List<string>();
        for (int i = 0; i < state.Populations.Count; i++)
        {
            var population = state.Populations[i];
            string name = $"population #{i} ({population.TotalCount} fish)";
            if (!holders.TryGetValue(population, out var units))
            {
                if (!IsHatcheryExempt(population))
                    problems.Add($"{name}: in no unit (site id '{population.SiteId}')");
                continue;
            }
            if (units.Count > 1)
                problems.Add($"{name}: held by {units.Count} units ({string.Join(", ", units.ConvertAll(u => u.Id))})");
            var unit = units[0];
            if (population.SiteId != unit.Id)
                problems.Add($"{name}: site id '{population.SiteId}' but held by '{unit.Id}'");
            if (string.IsNullOrEmpty(population.ZoneId))
                problems.Add($"{name}: no zone id (held by '{unit.Id}')");
            else if (population.ZoneId != unit.ZoneId)
                problems.Add($"{name}: zone id '{population.ZoneId}' but its unit '{unit.Id}' is in '{unit.ZoneId}'");
        }
        foreach (var population in holders.Keys)
            if (!ContainsReference(state.Populations, population))
                problems.Add($"a population of '{holders[population][0].Id}' ({population.TotalCount} fish) is not in the game's population list");
        return problems;
    }

    private static bool Holds(List<FishPopulation> pens, FishPopulation population) => ContainsReference(pens, population);

    private static bool ContainsReference(List<FishPopulation> list, FishPopulation population)
    {
        for (int i = 0; i < list.Count; i++)
            if (ReferenceEquals(list[i], population))
                return true;
        return false;
    }
}
