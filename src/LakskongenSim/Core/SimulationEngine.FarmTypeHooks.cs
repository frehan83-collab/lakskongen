using System;
using System.Collections.Generic;
using LakskongenSim.Biology;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Farm.Land;
using LakskongenSim.Farm.Offshore;

namespace LakskongenSim.Core;

/// <summary>
/// The farm-type hooks (docs/FARM_TYPES_SPEC.md §3.3, installed by F2-02 with
/// neutral defaults; this file is frozen after F2-02). Each hook is called from
/// one place in the engine's tick and dispatches to a lane's own file:
/// <see cref="OffshoreHooks"/> (the sea lane: every sea site, and offshore
/// holdings) or <see cref="LandHooks"/> (the land lane: land-tank holdings and
/// the smolt hooks), so the two lanes never edit the same file and never the
/// hot files. A population hook dispatches on the holding the step resolved
/// once (<see cref="HoldingOf"/>, §3.1a); a sea pen takes the neutral default
/// here. Every default is the pre-F2 behaviour, exactly
/// (<c>FarmTypeHooksTests</c> holds each one against the pre-move expression).
/// </summary>
public partial class SimulationEngine
{
    // ── Sea sites (the sea lane; F2-03 gives a site its kind) ────────────────

    /// <summary>
    /// Multiplier on the external copepodid pressure a sea site's own lice model
    /// takes (HealthPrep, <see cref="AdvanceSiteLiceModels"/>). Default 1.0;
    /// F2-08 scales an offshore unit's by its plot.
    /// </summary>
    internal double ExternalLicePressureFor(SeaSite site) => OffshoreHooks.ExternalLicePressureFor(this, site);

    /// <summary>
    /// What one population adds to its zone's fish-weighted lice mean, the
    /// traffic-light assessment (<see cref="UpdateTrafficLightZones"/>), with the
    /// sea site that holds it (null for a population in no site's pens).
    /// Default: its fish × its lice load, and its fish. F2-08 keeps an offshore
    /// unit's fish out of the mean.
    /// </summary>
    internal ZoneLiceShare ZoneLiceContribution(SeaSite? site, FishPopulation population)
        => OffshoreHooks.ZoneLiceContribution(this, site, population);

    /// <summary>
    /// True when a sea site takes none of the fjord weather's storm wear: net
    /// wear and storm escapes (<see cref="ApplyStormHazards"/>), storm stress
    /// deaths and the storm repair bill (<see cref="ApplyStormAndTemperatureImpacts"/>).
    /// Default false; F2-07 skips offshore units (they meet their own sea, F2-06).
    /// </summary>
    internal bool SkipsFjordStormWear(SeaSite site) => OffshoreHooks.SkipsFjordStormWear(this, site);

    /// <summary>
    /// True when the vessel operation <paramref name="jobKind"/> ("delouse",
    /// "harvest", "freshwater", "rov") can run at <paramref name="site"/> on
    /// <paramref name="day"/>; a due wellboat job that cannot run waits in the
    /// queue (<see cref="ProcessDueWellboatJobs"/>). Default true; F2-06 compares
    /// the plot's sea state with the operation's limit.
    /// </summary>
    internal bool OperationPossible(SeaSite? site, string jobKind, int day)
        => OffshoreHooks.OperationPossible(this, site, jobKind, day);

    /// <summary>
    /// True when a sea site counts toward the per-zone site limit and the remote
    /// operations centre's attention budget. Default true; F2-03 leaves offshore
    /// units out (no engine caller yet: the budget is summed in the bridge).
    /// </summary>
    internal bool CountsTowardSiteLimits(SeaSite site) => OffshoreHooks.CountsTowardSiteLimits(this, site);

    // ── Populations (dispatched on the holding the step resolved once) ────────

    /// <summary>
    /// True when the holding of <paramref name="population"/> can feed it today
    /// (Biology feeding). Default true; F2-07 stops an offshore unit's feeding
    /// when its silo is empty.
    /// </summary>
    internal bool FeedAvailableFor(FishPopulation population)
    {
        var holding = HoldingOf(population);
        return holding.Kind switch
        {
            HoldingKind.Offshore => OffshoreHooks.FeedAvailableFor(this, holding, population),
            HoldingKind.LandTank => LandHooks.FeedAvailableFor(this, holding, population),
            _ => true,
        };
    }

    /// <summary>
    /// Share of a healthy appetite the holding's own conditions leave (Biology
    /// feeding; multiplies the oxygen and gill factor). Default 1.0; F2-07 for an
    /// offshore unit's wave height, F2-12 for a land tank's CO2 and TAN.
    /// </summary>
    internal double AppetiteFactorFor(FishPopulation population)
    {
        var holding = HoldingOf(population);
        return holding.Kind switch
        {
            HoldingKind.Offshore => OffshoreHooks.AppetiteFactorFor(this, holding, population),
            HoldingKind.LandTank => LandHooks.AppetiteFactorFor(this, holding, population),
            _ => 1.0,
        };
    }

    /// <summary>
    /// The water temperature at the fish (°C): the temperature of the one water
    /// growth, appetite, the disease tick and the hazards read
    /// (<see cref="HoldingWaterOf(FishPopulation)"/>). The lanes fill it through
    /// their holding's water (<see cref="LandHooks.TankWater"/>,
    /// <see cref="OffshoreHooks.PenWater"/>, called by <see cref="WaterOf"/>), so
    /// the fish never grow in one water and die in another. F2-12 fills the tank's.
    /// </summary>
    internal double UnitWaterTemperatureC(FishPopulation population) => HoldingWaterOf(population).TemperatureC;

    /// <summary>
    /// The holding's own mortality terms (Biology mortality): a multiplier on the
    /// baseline anchor hazard and extra hazards booked by their own cause.
    /// Default <see cref="MortalityTerms.None"/> (the anchor × 1, no extra
    /// cause); F2-08 offshore causes, F2-15/F2-16 land causes.
    /// </summary>
    internal MortalityTerms UnitMortalityTerms(FishPopulation population)
    {
        var holding = HoldingOf(population);
        return holding.Kind switch
        {
            HoldingKind.Offshore => OffshoreHooks.MortalityTermsFor(this, holding, population),
            HoldingKind.LandTank => LandHooks.MortalityTermsFor(this, holding, population),
            _ => MortalityTerms.None,
        };
    }

    /// <summary>
    /// Multiplier the fish's smolt quality puts on the baseline anchor hazard
    /// (Biology mortality). Default 1.0; F2-13 makes the SuperSmolt and Large
    /// tiers' multipliers live (<see cref="SmoltQuality.MortalityMultiplier"/>).
    /// </summary>
    internal double QualityMortalityMultiplier(FishPopulation population)
        => LandHooks.QualityMortalityMultiplier(this, population);

    /// <summary>
    /// Price of one smolt of <paramref name="strain"/> at quality
    /// <paramref name="tier"/> from <paramref name="source"/>, NOK (stocking a
    /// sea pen). Default: the calibrated smolt price × the strain's cost
    /// multiplier, whatever the tier and the source. F2-13 charges the tier's
    /// premium for bought smolts and none for the player's own post-smolt.
    /// </summary>
    internal decimal SmoltTierCostPerFish(SmoltQualityTier tier, SmoltStrain strain, SmoltSource source)
        => LandHooks.SmoltTierCostPerFish(this, _balancing.SmoltCostPerFish, tier, strain, source);

    /// <summary>
    /// True when a harvest from <paramref name="unitId"/>, a holding of
    /// <paramref name="holding"/>, counts toward the resource-rent tax. Default:
    /// sea pens and offshore farms yes, land tanks never. F2-04 (base) and F2-09
    /// (keyed on the licence, not the location).
    /// </summary>
    internal bool IsResourceRentLiable(string unitId, HoldingKind holding)
        => holding == HoldingKind.LandTank
            ? LandHooks.IsResourceRentLiable(this, unitId)
            : OffshoreHooks.IsResourceRentLiable(this, unitId, holding);
}

/// <summary>
/// One population's part of its zone's fish-weighted lice mean (the traffic
/// light): the sum of fish × lice per fish, and the fish it counts.
/// </summary>
/// <param name="LiceWeightedFish">Fish × lice per fish the mean's numerator takes.</param>
/// <param name="Fish">Fish the mean's denominator takes.</param>
public readonly record struct ZoneLiceShare(double LiceWeightedFish, int Fish);

/// <summary>
/// A holding's own mortality terms (<see cref="SimulationEngine.UnitMortalityTerms"/>):
/// a multiplier on the baseline anchor hazard and extra hazards, each booked by
/// its own <see cref="MortalityCause"/>.
/// </summary>
/// <param name="AnchorMultiplier">Multiplier on the baseline anchor hazard (1 = the calibrated anchor).</param>
/// <param name="ExtraHazards">Daily hazards beyond the anchor, applied after it and booked by cause.</param>
public readonly record struct MortalityTerms(double AnchorMultiplier, IReadOnlyList<UnitHazard> ExtraHazards)
{
    /// <summary>The calibrated anchor and no extra cause: every holding before F2.</summary>
    public static readonly MortalityTerms None = new(1.0, Array.Empty<UnitHazard>());
}

/// <summary>One extra daily hazard of a holding: the fraction of each cohort it kills a day, booked as <paramref name="Cause"/>.</summary>
/// <param name="Cause">The cause the deaths are booked under.</param>
/// <param name="DailyFraction">Fraction of each cohort's fish lost a day (0-1).</param>
public readonly record struct UnitHazard(MortalityCause Cause, double DailyFraction);

/// <summary>Where a stocking's smolts come from (<see cref="SimulationEngine.SmoltTierCostPerFish"/>).</summary>
public enum SmoltSource
{
    /// <summary>Bought from a smolt producer (every stocking before F2-13).</summary>
    Purchased = 0,
    /// <summary>Grown in the player's own post-smolt facility (F2-13).</summary>
    OwnFacility = 1,
}
