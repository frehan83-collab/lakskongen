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
/// <para>
/// F2-12 wiring: a land site with lots (<see cref="RASFacility.Lots"/>) holds
/// its fish in physical tanks; the water, the appetite and the density stress
/// of a population in a tank are that tank's, as the land tick
/// (<c>LandSiteTickSystem</c>) solved it yesterday. A pre-F2 site (no lots), or
/// a population at an F2 site that sits in no tank yet, keeps the pre-F2 values.
/// </para>
/// </summary>
internal static class LandHooks
{
    /// <summary>
    /// The water in a land site's tank. At an F2 site, the tank's own water:
    /// the temperature the heat pumps reached, the outlet oxygen, full-strength
    /// seawater (F2-13 brings post-smolt salinity) and the production area of
    /// the pad's intake. At a pre-F2 site (the pre-F2 body of
    /// <c>SimulationEngine.WaterOf</c>): the facility's controlled temperature
    /// and oxygen in the home area.
    /// </summary>
    internal static HoldingWater TankWater(SimulationEngine engine, in PopulationHolding holding, FishPopulation population)
    {
        var site = holding.LandFacility!;
        var tank = site.HasLots ? site.TankOf(population) : null;
        if (tank == null)
            return new HoldingWater(site.CurrentWaterTemp, site.OxygenLevel,
                HoldingWater.SeawaterSalinityPsu, engine.HomeProductionArea, HoldingKind.LandTank);
        // F2-13: a post-smolt site on a pad with a river intake may blend its water to brackish.
        return new HoldingWater(tank.TemperatureC, tank.DoMgL, engine.LandSalinityOf(site),
            engine.ProductionAreaOfZone(site.ZoneId), HoldingKind.LandTank);
    }

    /// <summary>A land tank's feed is always there (the LOX store limits oxygen, not feed; a power hook goes here, F2-16).</summary>
    internal static bool FeedAvailableFor(SimulationEngine engine, in PopulationHolding holding, FishPopulation population)
        => true;

    /// <summary>
    /// Share of a healthy appetite the tank leaves (F2-12): the CO2 and TAN
    /// factor the land tick wrote yesterday (<see cref="LandTank.AppetiteFactor"/>,
    /// spec §3.3) times the dissolved-oxygen saturation factor at the tank's
    /// outlet DO. The biology's own oxygen path reads a fjord zone's water and
    /// never a land pad's (a pad is no fjord zone), so the tank's oxygen bites
    /// here, once. 1.0 at a pre-F2 site.
    /// </summary>
    internal static double AppetiteFactorFor(SimulationEngine engine, in PopulationHolding holding, FishPopulation population)
    {
        var site = holding.LandFacility;
        var tank = site != null && site.HasLots ? site.TankOf(population) : null;
        if (tank == null) return 1.0;
        double saturation = WaterQuality.CalculateDOSaturation(tank.DoMgL, tank.TemperatureC, engine.LandSalinityOf(site!));
        return tank.AppetiteFactor * WaterQuality.CalculateDOSaturationModifier(saturation);
    }

    /// <summary>
    /// The density stress on growth in a land tank (F2-12, spec §3.3
    /// "Density"): 1 up to 50 kg/m³, 0.85 at 85 kg/m³ ([L:S2], [L:R2]), on the
    /// fish in the tank's water volume. The sea's curve (from 25 kg/m³) is a
    /// fjord zone's and never reaches a land pad. 1.0 at a pre-F2 site.
    /// </summary>
    internal static double DensityGrowthMultiplier(SimulationEngine engine, in PopulationHolding holding, FishPopulation population)
    {
        var site = holding.LandFacility;
        var tank = site != null && site.HasLots ? site.TankOf(population) : null;
        if (tank == null || !(tank.VolumeM3 > 0.0)) return 1.0;
        double density = population.GetTotalBiomassKg() / tank.VolumeM3;
        return LandWaterModel.DensityGrowthMultiplier(density, engine.Calibration.Facilities.Land.Density);
    }

    /// <summary>
    /// The biological FCR at 2 kg of fish in a land tank (F2-12): the land
    /// plan's ([L:A3], [L:R2], <see cref="Calibration.LandProductionCalibration.BiologicalFcrAt2Kg"/>),
    /// which the growth law's size factor scales, the FCR the land physics'
    /// oxygen and energy were calibrated on; null at a pre-F2 site or for fish
    /// in no tank (the engine's sea FCR model then applies, as before F2).
    /// F2-15 brings competence and the feed-plan penalty.
    /// </summary>
    internal static double? BiologicalFcrAt2Kg(SimulationEngine engine, in PopulationHolding holding, FishPopulation population)
    {
        var site = holding.LandFacility;
        var tank = site != null && site.HasLots ? site.TankOf(population) : null;
        if (tank == null) return null;
        var land = engine.Calibration.Facilities.Land;
        // F2-15: the unit's own FCR (RAS hall 1.15, [L:R7]/[L:S9]) and the
        // penalty a low-competence plant pays after changing its feeding plan.
        var lot = site!.LotOf(tank);
        double factor = lot == null ? 1.0 : land.For(lot.Template).FcrFactor;
        return land.Production.BiologicalFcrAt2Kg * factor + engine.LandFcrPenaltyOf(site);
    }

    /// <summary>
    /// The growth a land grow-out site's competence gives its tanks' fish (F2-15,
    /// spec §3.3: the achieved result at competence 0.4, the plan at 1.0), on
    /// the growth law's environment multiplier with the density stress; 1.0 at a
    /// post-smolt site, a pre-F2 site or for fish in no tank.
    /// </summary>
    internal static double CompetenceGrowthMultiplier(SimulationEngine engine, in PopulationHolding holding, FishPopulation population)
    {
        var site = holding.LandFacility;
        var tank = site != null && site.HasLots ? site.TankOf(population) : null;
        return tank == null ? 1.0 : engine.LandGrowthMultiplierOf(site!);
    }

    /// <summary>
    /// A land tank's own mortality terms (F2-15): the fish in a tank of an F2
    /// site die at their plan's daily hazard (the stocking plan's cycle survival,
    /// about 97 % a land cycle [L:R2], spread over its cycle days), booked as
    /// the baseline anchor, in place of the sea's anchor (15 % a sea cycle,
    /// which put a land cycle near 90 %). A tank without a plan (fish seated by
    /// a load) and a pre-F2 site keep the anchor × 1 and no extra cause.
    /// F2-16 adds the land incident causes.
    /// </summary>
    internal static MortalityTerms MortalityTermsFor(SimulationEngine engine, in PopulationHolding holding, FishPopulation population)
    {
        var site = holding.LandFacility;
        var tank = site != null && site.HasLots ? site.TankOf(population) : null;
        if (tank?.Plan is not { DailyHazard: > 0.0 } plan) return MortalityTerms.None;
        return new MortalityTerms(0.0, new[] { new UnitHazard(MortalityCause.BaselineAnchor, plan.DailyHazard) });
    }

    /// <summary>
    /// Smolt-quality multiplier on the anchor hazard (F2-13): the sea-phase
    /// carryover of the fish's tier, 0.67 for a SuperSmolt and 0.82 for a large
    /// smolt (<see cref="SmoltQuality.MortalityMultiplier"/>, the Chilean
    /// SuperSmolt trial), fish-weighted over the population's cohorts. Land
    /// fish carry none: the carryover is the sea phase's (1.0 in a tank, and
    /// 1.0 for standard smolts, as before F2-13).
    /// </summary>
    internal static double QualityMortalityMultiplier(SimulationEngine engine, FishPopulation population)
    {
        if (population == null || engine.HoldingOf(population).Kind == HoldingKind.LandTank) return 1.0;
        double fish = 0.0, weighted = 0.0;
        bool standardOnly = true;
        foreach (var cohort in population.Cohorts)
        {
            if (cohort.Count <= 0) continue;
            if (cohort.Quality != SmoltQualityTier.Standard) standardOnly = false;
            fish += cohort.Count;
            weighted += cohort.Count * SmoltQualityRegistry.Get(cohort.Quality).MortalityMultiplier;
        }
        // Standard smolts multiply by exactly 1 (no rounding of a weighted mean of ones).
        return standardOnly || fish <= 0.0 ? 1.0 : weighted / fish;
    }

    /// <summary>
    /// Price of one smolt (NOK, F2-13): the calibrated price × the strain's
    /// cost multiplier, × the tier's premium for a bought large smolt (1.6) or
    /// SuperSmolt (2.5) (<c>facilities.land.smolt</c>); a smolt from the
    /// player's own post-smolt site carries no premium (the exploit of buying
    /// the tier's carryover at the standard price is closed; the player paid
    /// for the land days in the module). A standard smolt costs what it always did.
    /// </summary>
    internal static decimal SmoltTierCostPerFish(SimulationEngine engine, decimal smoltPricePerFish,
        SmoltQualityTier tier, SmoltStrain strain, SmoltSource source)
    {
        decimal price = smoltPricePerFish * (decimal)strain.CostMultiplier;
        if (source == SmoltSource.OwnFacility || tier == SmoltQualityTier.Standard) return price;
        var smolt = engine.Calibration.Facilities.Land.Smolt;
        double premium = tier == SmoltQualityTier.SuperSmolt ? smolt.TierPremiumSuperSmolt : smolt.TierPremiumLarge;
        return price * (decimal)premium;
    }

    /// <summary>A land facility's harvest never counts toward the resource-rent tax.</summary>
    internal static bool IsResourceRentLiable(SimulationEngine engine, string unitId)
        => false;
}
