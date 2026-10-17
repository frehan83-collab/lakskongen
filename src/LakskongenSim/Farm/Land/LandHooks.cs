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
        return new HoldingWater(tank.TemperatureC, tank.DoMgL, HoldingWater.SeawaterSalinityPsu,
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
        double saturation = WaterQuality.CalculateDOSaturation(tank.DoMgL, tank.TemperatureC, HoldingWater.SeawaterSalinityPsu);
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
        return tank == null ? null : engine.Calibration.Facilities.Land.Production.BiologicalFcrAt2Kg;
    }

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
