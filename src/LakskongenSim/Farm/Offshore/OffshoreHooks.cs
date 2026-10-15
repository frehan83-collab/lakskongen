using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Environment;

namespace LakskongenSim.Farm.Offshore;

/// <summary>
/// The sea lane's side of the farm-type hooks (docs/FARM_TYPES_SPEC.md §3.3,
/// installed by F2-02): <c>SimulationEngine.FarmTypeHooks.cs</c> dispatches
/// every sea site and every offshore holding here, and each site hook checks
/// the site's kind (F2-03: <see cref="SeaSite.Kind"/>). A fjord site keeps the
/// pre-F2 behaviour exactly (moved verbatim where there was code); an offshore
/// unit gets F2-03's site-kind decisions (docs/run/evidence/f2_03_sitekind_audit.md):
/// out of the site limits and the area's licence pool, out of the fjord storm
/// wear (as the pre-F2 offshore farm was), its plot's external lice pressure.
/// Filled further by F2-06 (operability), F2-07 (feed, appetite, its own sea),
/// F2-08 (lice coupling, mortality, the zone mean), F2-09 (licences, rent by
/// licence); the land lane never edits this file. Pure: no hook draws a random
/// number or changes state.
/// </summary>
internal static class OffshoreHooks
{
    // ── Sea sites ──────────────────────────────────────────────────────────

    /// <summary>
    /// Multiplier on a sea site's external lice pressure: 1.0 at a fjord site;
    /// at an offshore unit its plot's multiplier
    /// (<see cref="SitePlot.LiceExternalMultiplier"/>: 0.12 at off_1, 0.10 at
    /// off_2 [GB], spec §2B and §4), so its own lice model meets the open sea's
    /// lower pressure (F2-03; F2-08 adds the coupling between units).
    /// </summary>
    internal static double ExternalLicePressureFor(SimulationEngine engine, SeaSite site)
        => site.Kind == SiteKind.Offshore ? engine.PlotLiceMultiplierOf(site) : 1.0;

    /// <summary>
    /// A population's part of its zone's lice mean: its fish × its lice load,
    /// and its fish (the pre-F2 terms of the traffic-light mean). An offshore
    /// unit's fish add nothing (F2-03): they sit in its plot's water area, which
    /// no fjord zone is, and an offshore unit must never dilute a fjord zone's
    /// mean (spec §3.1 decision 1; F2-09 counts placed licences).
    /// </summary>
    internal static ZoneLiceShare ZoneLiceContribution(SimulationEngine engine, SeaSite? site, FishPopulation population)
    {
        if (site?.Kind == SiteKind.Offshore) return default;
        int fish = population.Cohorts.Sum(c => c.Count);
        return new ZoneLiceShare(fish * population.SeaLiceLoad, fish);
    }

    /// <summary>
    /// Whether a sea site skips the fjord storm wear: an offshore unit does
    /// (F2-03; the pre-F2 offshore farm never took it: its structure meets its
    /// own sea, the unit's day, and F2-06/F2-07 its plot's metocean); a fjord
    /// site never does.
    /// </summary>
    internal static bool SkipsFjordStormWear(SimulationEngine engine, SeaSite site)
        => site.Kind == SiteKind.Offshore;

    /// <summary>Whether a vessel operation can run at a sea site today: always before F2-06.</summary>
    internal static bool OperationPossible(SimulationEngine engine, SeaSite? site, string jobKind, int day)
        => true;

    /// <summary>
    /// Whether a sea site counts toward the site limits (the per-zone site
    /// limit and the remote operations centre's attention budget): a fjord
    /// site does; an offshore unit does not (F2-03: it is crewed on board).
    /// </summary>
    internal static bool CountsTowardSiteLimits(SimulationEngine engine, SeaSite site)
        => site.Kind != SiteKind.Offshore;

    /// <summary>
    /// Whether a sea site's fish count against its production area's pooled
    /// licence MTB (S2-01a): a fjord site's do; an offshore unit's do not until
    /// F2-09 binds its development licences to it (F2-03: its own MTB is its
    /// locality MTB, <see cref="SeaSite.MABTonnes"/>).
    /// </summary>
    internal static bool CountsTowardAreaLicencePool(SimulationEngine engine, SeaSite site)
        => site.Kind != SiteKind.Offshore;

    // ── Offshore holdings ──────────────────────────────────────────────────

    /// <summary>
    /// The water in an offshore unit's zone: the sea water of its area
    /// (<paramref name="sea"/>) with the oxygen its current-fed exchange adds
    /// (<see cref="OffshoreUnit.OxygenBoost"/>; a pre-F2 farm's
    /// <see cref="OffshoreFarm.OxygenBoost"/> until the step normalizes it).
    /// </summary>
    internal static HoldingWater PenWater(SimulationEngine engine, in PopulationHolding holding, FishPopulation population,
        in HoldingWater sea)
        => sea with { OxygenMgPerL = sea.OxygenMgPerL + (holding.Site?.Offshore?.OxygenBoost ?? holding.OffshoreFarm!.OxygenBoost) };

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
