using System;
using LakskongenSim.Biology;
using LakskongenSim.Environment;

namespace LakskongenSim.Core;

/// <summary>
/// The farm-type seam, one source of truth (wave 2, sim-a-1). Whatever holds a
/// population (a sea site's pen, an offshore structure's pen, a land-based
/// tank) is resolved once per simulated step (<see cref="HoldingOf"/>, backed
/// by a <see cref="HoldingIndex"/>); the water at the fish is
/// <see cref="HoldingWaterOf(FishPopulation)"/>, which growth, appetite,
/// feeding, the disease tick and the temperature-stress pass take (S2-02); the
/// mortality hazards take a <see cref="CohortEnvironment"/> built from that
/// same water plus the holding's id, CO2 and wave height
/// (<see cref="ResolveCohortEnvironment"/>, S2-05). The F2 hooks
/// (docs/FARM_TYPES_SPEC.md §3.3: <c>UnitWaterTemperatureC</c>,
/// <c>AppetiteFactorFor</c>, <c>UnitMortalityTerms</c>) hang off
/// <see cref="HoldingOf"/> and <see cref="WaterOf"/>.
/// </summary>
public partial class SimulationEngine
{
    private HoldingIndex? _holdingIndex;

    /// <summary>Holding indexes built (one per simulated step): test observability for the once-per-day rule.</summary>
    internal int HoldingIndexBuilds { get; private set; }

    /// <summary>
    /// Holdings resolved by scanning the facility, site and population lists
    /// (outside a step, or a population created during one). A step resolves
    /// every population once through its index, so the hazards add none.
    /// </summary>
    internal long HoldingScans { get; private set; }

    /// <summary>
    /// Resolve every population's holding for the step that starts now. A
    /// state that still holds pre-F2 facilities (built by hand: a load migrates
    /// its own, F2-03) is normalized into the F2 unit kinds first, so the step
    /// resolves every population to its unit; an F2 state passes untouched.
    /// </summary>
    internal void BeginHoldingDay()
    {
        MigrateFacilities();
        _holdingIndex = HoldingIndex.Build(State);
        HoldingIndexBuilds++;
    }

    /// <summary>The step is over: commands between steps may move fish, so the next lookup resolves afresh.</summary>
    internal void EndHoldingDay() => _holdingIndex = null;

    /// <summary>
    /// What holds <paramref name="population"/>: a land facility's tank, an
    /// offshore farm's pen or a sea pen (in that precedence, as the S2-02 and
    /// S2-05 resolvers had it). During a simulated step the answer comes from
    /// the step's index; between steps it is resolved afresh.
    /// </summary>
    public PopulationHolding HoldingOf(FishPopulation population)
    {
        if (population == null) throw new ArgumentNullException(nameof(population));
        if (_holdingIndex != null && _holdingIndex.TryGet(population, out var holding))
            return holding;
        var fresh = ScanForHolding(population);
        _holdingIndex?.Add(population, fresh);
        return fresh;
    }

    /// <summary>
    /// The water a population in <paramref name="holding"/> lives in today,
    /// read live from the holding: a land tank's controlled temperature and
    /// oxygen (the area of its sea-water intake is the home area); an offshore
    /// pen's area sea water, with the oxygen its current-fed exchange adds
    /// (<see cref="Farm.OffshoreFarm.OxygenBoost"/>); a sea pen's zone water,
    /// less the oxygen a running in-cage sag takes (<see cref="HypoxiaSagDepthAt"/>).
    /// The land tank's and the offshore pen's water are the lanes' (F2-02:
    /// <see cref="Farm.Land.LandHooks.TankWater"/>, <see cref="Farm.Offshore.OffshoreHooks.PenWater"/>),
    /// the one water behind <see cref="UnitWaterTemperatureC"/>.
    /// </summary>
    internal HoldingWater WaterOf(in PopulationHolding holding, FishPopulation population)
    {
        switch (holding.Kind)
        {
            case HoldingKind.LandTank when holding.LandFacility != null:
                return Farm.Land.LandHooks.TankWater(this, holding, population);
            case HoldingKind.Offshore when holding.Site?.Offshore != null:
                // An offshore unit's zone (F2-03): its plot's sea, not a fjord zone's.
                return Farm.Offshore.OffshoreHooks.PenWater(this, holding, population,
                    SeaWaterOf(holding.Zone, holding.Site.ZoneId, HoldingKind.Offshore));
            case HoldingKind.Offshore when holding.OffshoreFarm != null:
                return Farm.Offshore.OffshoreHooks.PenWater(this, holding, population,
                    SeaWaterOf(holding.Zone, holding.OffshoreFarm.ZoneId, HoldingKind.Offshore));
            default:
            {
                var sea = SeaWaterOf(holding.Zone, population.ZoneId, HoldingKind.SeaPen);
                // An oxygen sag running at the site (the bloom deck's hypoxia
                // card): the cage's water holds that fraction less oxygen than
                // the zone's (SimulationEngine.Oxygen.cs).
                double sag = holding.Site != null ? HypoxiaSagDepthAt(holding.Site) : 0.0;
                return sag > 0.0 ? sea with { OxygenMgPerL = sea.OxygenMgPerL * (1.0 - sag) } : sea;
            }
        }
    }

    /// <summary>
    /// The environment the hazards read for <paramref name="population"/> in
    /// <paramref name="holding"/>: its water (<see cref="WaterOf"/>) with the
    /// sea temperature pinned to <paramref name="seaTemperatureC"/> when one is
    /// given (never a land tank's), plus the holding's id, CO2 and wave height.
    /// </summary>
    internal CohortEnvironment EnvironmentOf(in PopulationHolding holding, FishPopulation population, double? seaTemperatureC)
    {
        var water = WaterOf(holding, population);
        if (seaTemperatureC is double pinned && water.Kind != HoldingKind.LandTank)
            water = water with { TemperatureC = pinned };
        double co2 = holding.LandFacility?.CO2Level ?? double.NaN;
        double waveHeight = holding.Kind switch
        {
            HoldingKind.LandTank => 0.0,
            HoldingKind.Offshore => holding.Site?.Offshore?.CurrentWaveHeight ?? holding.OffshoreFarm?.CurrentWaveHeight ?? 0.0,
            _ => Weather?.CurrentWaveHeight ?? 0.0,
        };
        return CohortEnvironment.From(water, holding.Id, co2, waveHeight);
    }

    /// <summary>Sea water of a zone: its temperature, its chemistry's oxygen (saturation when the zone is unknown) and its area.</summary>
    private HoldingWater SeaWaterOf(Zone? zone, string? zoneId, HoldingKind kind)
    {
        double temperature = zone != null ? SeaTemperatureOf(zone) : SeaTemperatureOfZone(zoneId);
        double oxygen = zone?.WaterChemistry?.OxygenO2
            ?? WaterQuality.CalculateOxygenSaturation(temperature, HoldingWater.SeawaterSalinityPsu);
        int area = zone != null ? ProductionAreaOf(zone) : ProductionAreaOfZone(zoneId);
        return new HoldingWater(temperature, oxygen, HoldingWater.SeawaterSalinityPsu, area, kind);
    }

    /// <summary>
    /// The holding of a population with no index entry: its unit, resolved by
    /// scanning the unit lists (<see cref="UnitResolver.Resolve"/>, F2-03).
    /// </summary>
    private PopulationHolding ScanForHolding(FishPopulation population)
    {
        HoldingScans++;
        return HoldingIndex.HoldingOfUnit(population, UnitResolver.Resolve(State, population), FindZone);
    }
}
