using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Environment;

namespace LakskongenSim.Farm.Land;

/// <summary>
/// The land lane's side of the farm-type hooks (docs/FARM_TYPES_SPEC.md §3.3,
/// installed by F2-02): <c>SimulationEngine.FarmTypeHooks.cs</c> dispatches
/// land-tank holdings and the smolt hooks here. Each default is the pre-F2
/// behaviour, moved verbatim where there was code (<see cref="TankWater"/>).
/// Filled by F2-12 (water, energy, tanks), F2-13 (post-smolt and smolt tiers)
/// and F2-15/F2-16 (grow-out biology, incidents); the sea lane never edits
/// this file. Pure: no hook draws a random number or changes state.
/// </summary>
internal static class LandHooks
{
    /// <summary>
    /// The water in a land facility's tank: the facility's controlled
    /// temperature and oxygen, full-strength seawater, and the area of its
    /// seawater intake (the home area). The pre-F2 body of
    /// <c>SimulationEngine.WaterOf</c> for a land tank; F2-12 fills it (intake
    /// blend, heat pump, flows).
    /// </summary>
    internal static HoldingWater TankWater(SimulationEngine engine, in PopulationHolding holding, FishPopulation population)
        => new HoldingWater(holding.LandFacility!.CurrentWaterTemp, holding.LandFacility.OxygenLevel,
            HoldingWater.SeawaterSalinityPsu, engine.HomeProductionArea, HoldingKind.LandTank);

    /// <summary>A land tank's feed is always there before F2 (a feed-plan or power hook goes here).</summary>
    internal static bool FeedAvailableFor(SimulationEngine engine, in PopulationHolding holding, FishPopulation population)
        => true;

    /// <summary>Appetite a land tank's CO2 and TAN leave: 1.0 before F2-12.</summary>
    internal static double AppetiteFactorFor(SimulationEngine engine, in PopulationHolding holding, FishPopulation population)
        => 1.0;

    /// <summary>A land tank's own mortality terms: the anchor × 1 and no extra cause before F2-15/F2-16.</summary>
    internal static MortalityTerms MortalityTermsFor(SimulationEngine engine, in PopulationHolding holding, FishPopulation population)
        => MortalityTerms.None;

    /// <summary>Smolt-quality multiplier on the anchor hazard: 1.0 before F2-13 (the tier's mortality multiplier was never live).</summary>
    internal static double QualityMortalityMultiplier(SimulationEngine engine, FishPopulation population)
        => 1.0;

    /// <summary>
    /// Price of one smolt (NOK): the calibrated price × the strain's cost
    /// multiplier, whatever the tier and the source (pre-F2: a quality tier
    /// carried no premium). F2-13 charges the tier premium for bought smolts.
    /// </summary>
    internal static decimal SmoltTierCostPerFish(SimulationEngine engine, decimal smoltPricePerFish,
        SmoltQualityTier tier, SmoltStrain strain, SmoltSource source)
        => smoltPricePerFish * (decimal)strain.CostMultiplier;

    /// <summary>A land facility's harvest never counts toward the resource-rent tax.</summary>
    internal static bool IsResourceRentLiable(SimulationEngine engine, string unitId)
        => false;
}
