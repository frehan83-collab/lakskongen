using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Environment;

namespace LakskongenSim.Farm.Offshore;

/// <summary>
/// The sea lane's side of the farm-type hooks (docs/FARM_TYPES_SPEC.md §3.3,
/// installed by F2-02): <c>SimulationEngine.FarmTypeHooks.cs</c> dispatches
/// every sea site and every offshore holding here. Until F2-03 gives a
/// <see cref="SeaSite"/> its kind, a site hook answers for every sea site, so
/// each default is the pre-F2 behaviour of a fjord site, moved verbatim where
/// there was code (<see cref="ZoneLiceContribution"/>, <see cref="PenWater"/>).
/// Filled by F2-06 (operability), F2-07 (feed, appetite, storm wear),
/// F2-08 (lice, mortality), F2-09 (rent by licence) and F2-03 (site limits);
/// the land lane never edits this file. Pure: no hook draws a random number
/// or changes state.
/// </summary>
internal static class OffshoreHooks
{
    // ── Sea sites ──────────────────────────────────────────────────────────

    /// <summary>Multiplier on a sea site's external lice pressure: 1.0 before F2-08.</summary>
    internal static double ExternalLicePressureFor(SimulationEngine engine, SeaSite site)
        => 1.0;

    /// <summary>
    /// A population's part of its zone's lice mean: its fish × its lice load,
    /// and its fish (the pre-F2 terms of the traffic-light mean). F2-08 keeps an
    /// offshore unit's fish out of the mean.
    /// </summary>
    internal static ZoneLiceShare ZoneLiceContribution(SimulationEngine engine, SeaSite? site, FishPopulation population)
    {
        int fish = population.Cohorts.Sum(c => c.Count);
        return new ZoneLiceShare(fish * population.SeaLiceLoad, fish);
    }

    /// <summary>Whether a sea site skips the fjord storm wear: never before F2-07.</summary>
    internal static bool SkipsFjordStormWear(SimulationEngine engine, SeaSite site)
        => false;

    /// <summary>Whether a vessel operation can run at a sea site today: always before F2-06.</summary>
    internal static bool OperationPossible(SimulationEngine engine, SeaSite? site, string jobKind, int day)
        => true;

    /// <summary>Whether a sea site counts toward the site limits: every site before F2-03.</summary>
    internal static bool CountsTowardSiteLimits(SimulationEngine engine, SeaSite site)
        => true;

    // ── Offshore holdings ──────────────────────────────────────────────────

    /// <summary>
    /// The water in an offshore farm's pen: the sea water of its area
    /// (<paramref name="sea"/>) with the oxygen its current-fed exchange adds
    /// (<see cref="OffshoreFarm.OxygenBoost"/>). The pre-F2 body of
    /// <c>SimulationEngine.WaterOf</c> for an offshore pen.
    /// </summary>
    internal static HoldingWater PenWater(SimulationEngine engine, in PopulationHolding holding, FishPopulation population,
        in HoldingWater sea)
        => sea with { OxygenMgPerL = sea.OxygenMgPerL + holding.OffshoreFarm!.OxygenBoost };

    /// <summary>Whether an offshore unit can feed today: always before F2-07 (silo).</summary>
    internal static bool FeedAvailableFor(SimulationEngine engine, in PopulationHolding holding, FishPopulation population)
        => true;

    /// <summary>Appetite an offshore unit's sea state leaves: 1.0 before F2-07 (Hs).</summary>
    internal static double AppetiteFactorFor(SimulationEngine engine, in PopulationHolding holding, FishPopulation population)
        => 1.0;

    /// <summary>An offshore unit's own mortality terms: the anchor × 1 and no extra cause before F2-08.</summary>
    internal static MortalityTerms MortalityTermsFor(SimulationEngine engine, in PopulationHolding holding, FishPopulation population)
        => MortalityTerms.None;

    /// <summary>
    /// Whether a sea pen's or an offshore farm's harvest counts toward the
    /// resource-rent tax: always before F2-04 (base) and F2-09 (licence kind).
    /// </summary>
    internal static bool IsResourceRentLiable(SimulationEngine engine, string unitId, HoldingKind holding)
        => true;
}
