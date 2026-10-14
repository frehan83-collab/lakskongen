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

    /// <summary>Resolve every population's holding for the step that starts now.</summary>
    internal void BeginHoldingDay()
    {
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
    /// (<see cref="Farm.OffshoreFarm.OxygenBoost"/>); a sea pen's zone water.
    /// </summary>
    internal HoldingWater WaterOf(in PopulationHolding holding, FishPopulation population)
    {
        switch (holding.Kind)
        {
            case HoldingKind.LandTank when holding.LandFacility != null:
                return new HoldingWater(holding.LandFacility.CurrentWaterTemp, holding.LandFacility.OxygenLevel,
                    HoldingWater.SeawaterSalinityPsu, HomeProductionArea, HoldingKind.LandTank);
            case HoldingKind.Offshore when holding.OffshoreFarm != null:
            {
                var sea = SeaWaterOf(holding.Zone, holding.OffshoreFarm.ZoneId, HoldingKind.Offshore);
                return sea with { OxygenMgPerL = sea.OxygenMgPerL + holding.OffshoreFarm.OxygenBoost };
            }
            default:
                return SeaWaterOf(holding.Zone, population.ZoneId, HoldingKind.SeaPen);
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
            HoldingKind.Offshore => holding.OffshoreFarm?.CurrentWaveHeight ?? 0.0,
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

    /// <summary>The holding of a population with no index entry: the facility, site and population lists, scanned.</summary>
    private PopulationHolding ScanForHolding(FishPopulation population)
    {
        HoldingScans++;
        foreach (var facility in State.RASFacilities)
            if (HoldsPopulation(facility.Tanks, population))
                return HoldingIndex.LandTank(facility);
        foreach (var farm in State.OffshoreFarms)
            if (HoldsPopulation(farm.Pens, population))
                return HoldingIndex.OffshorePen(farm, FindZone(farm.ZoneId));
        var zone = FindZone(population.ZoneId);
        foreach (var site in State.SeaSites)
            if (HoldsPopulation(site.Pens, population))
                return HoldingIndex.SeaPen(population, site, zone);
        return HoldingIndex.SeaPen(population, null, zone);
    }

    private static bool HoldsPopulation(System.Collections.Generic.List<FishPopulation> pens, FishPopulation population)
    {
        for (int i = 0; i < pens.Count; i++)
            if (ReferenceEquals(pens[i], population))
                return true;
        return false;
    }
}
