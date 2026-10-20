using System;
using System.Collections.Generic;
using LakskongenSim.Biology;
using LakskongenSim.Environment;
using LakskongenSim.Farm;

namespace LakskongenSim.Core;

/// <summary>
/// Production areas and their sea temperature (roadmap S2-02). Every zone lies
/// in one of the 13 production areas; its water takes that area's climatology
/// plus the anomaly all areas share (<see cref="WeatherSystem"/>). Growth,
/// appetite, feeding, lice, oxygen, temperature-stress deaths and treatments
/// read the water of whatever holds the fish through
/// <see cref="HoldingWaterOf(FishPopulation)"/>:
/// the seam the land-based (RAS, flow-through) and offshore farm types plug into.
/// </summary>
public partial class SimulationEngine
{
    /// <summary>
    /// The player's home production area: the scenario's
    /// (<see cref="GameState.HomeProductionArea"/>), else the area of the first
    /// site whose zone lies in one, else <see cref="ProductionAreas.NationalReference"/>
    /// (a game with no scenario). The HUD temperature
    /// (<see cref="WeatherSystem.CurrentTemperatureC"/>) reports this area.
    /// </summary>
    public int HomeProductionArea
    {
        get
        {
            if (State == null) return ProductionAreas.NationalReference;
            if (ProductionAreas.IsValid(State.HomeProductionArea)) return State.HomeProductionArea;
            for (int i = 0; i < State.SeaSites.Count; i++)
            {
                var zone = FindZone(State.SeaSites[i].ZoneId);
                if (zone != null && ProductionAreas.IsValid(zone.ProductionArea)) return zone.ProductionArea;
            }
            return ProductionAreas.NationalReference;
        }
    }

    /// <summary>
    /// The production area a zone lies in: the one it was given (scenario data,
    /// <see cref="Zone.ProductionArea"/>), else the home area. Zone ids are labels
    /// and are never parsed for an area.
    /// </summary>
    public int ProductionAreaOf(Zone zone)
    {
        if (zone == null) throw new ArgumentNullException(nameof(zone));
        return ProductionAreas.IsValid(zone.ProductionArea) ? zone.ProductionArea : HomeProductionArea;
    }

    /// <summary>
    /// The production area of a zone id: the zone's; a catalogue zone outside
    /// the game (U2-01, <see cref="ExpansionZones"/>) its catalogue area; an id
    /// with no zone (facilities) the home area.
    /// </summary>
    public int ProductionAreaOfZone(string? zoneId)
    {
        var zone = FindZone(zoneId);
        if (zone != null) return ProductionAreaOf(zone);
        return ExpansionZones.TryGet(zoneId, out var catalogue) ? catalogue.ProductionArea : HomeProductionArea;
    }

    /// <summary>The production area a sea site lies in.</summary>
    public int ProductionAreaOfSite(SeaSite site)
    {
        if (site == null) throw new ArgumentNullException(nameof(site));
        return ProductionAreaOfZone(site.ZoneId);
    }

    /// <summary>
    /// Today's water temperature (°C, cage depth) in a zone: its production
    /// area's sea temperature, or the zone's own when it was given one
    /// (<see cref="Zone.SetTemperature"/>).
    /// </summary>
    public double SeaTemperatureOfZone(string? zoneId)
    {
        var zone = FindZone(zoneId);
        return zone != null ? SeaTemperatureOf(zone) : SeaTemperatureOfArea(ProductionAreaOfZone(zoneId));
    }

    /// <summary>Today's water temperature (°C, cage depth) in <paramref name="zone"/> (see <see cref="SeaTemperatureOfZone"/>).</summary>
    private double SeaTemperatureOf(Zone zone)
        => !zone.FollowsSeaTemperature
            ? zone.WaterChemistry.Temperature
            : SeaTemperatureOfArea(ProductionAreaOf(zone));

    private double SeaTemperatureOfArea(int area)
        => Weather != null
            ? Weather.SeaTemperatureAt(area)
            : ProductionAreas.ClimatologyC(area, State?.Day ?? 0);

    /// <summary>Today's water temperature (°C) at a sea site.</summary>
    public double SeaTemperatureAtSite(SeaSite site)
    {
        if (site == null) throw new ArgumentNullException(nameof(site));
        return SeaTemperatureOfZone(site.ZoneId);
    }

    /// <summary>
    /// Normal size (t) of a standard licence held in a zone's production area:
    /// 945 t in Troms and Finnmark (PO10-PO13), 780 t elsewhere and on the
    /// national reference. Data for the licence mechanics (S2-01).
    /// </summary>
    public int StandardLicenceMtbTonnes(string? zoneId)
    {
        int area = ProductionAreaOfZone(zoneId);
        return ProductionAreas.IsValid(area)
            ? ProductionAreas.Get(area).LicenceMtbTonnes
            : ProductionAreas.StandardLicenceMtbTonnes;
    }

    /// <summary>True when a zone lies in the national PD zone (PO2-PO6; FOR-2017-08-29-1318).</summary>
    public bool IsInPdZone(string? zoneId)
    {
        int area = ProductionAreaOfZone(zoneId);
        return ProductionAreas.IsValid(area) && ProductionAreas.Get(area).InPdZone;
    }

    /// <summary>
    /// The lice limit (adult females per fish) a site must stay under today:
    /// 0.2 in its area's spring weeks (ISO 16-21 up to Nord-Trøndelag, 21-26 in
    /// Nordland, Troms and Finnmark), 0.5 otherwise (luseforskriften § 8).
    /// </summary>
    public double LiceLimitAtSite(SeaSite site)
        => ProductionAreas.LiceLimitAdultFemales(ProductionAreaOfSite(site), State.Day);

    /// <summary>
    /// Point the HUD temperature at the home area (scenario start, load).
    /// </summary>
    internal void SyncSeaTemperatureReference()
    {
        Weather?.SetReferenceProductionArea(HomeProductionArea);
    }

    /// <summary>
    /// A save from before S2-02 has no areas: give the active scenario's zones
    /// the areas its starting sites name, and the game its home area, so a
    /// resumed po3 game sits in PO3 water rather than on the national curve.
    /// Zones that already have an area are left alone.
    /// </summary>
    internal void AssignScenarioProductionAreas(ScenarioDefinition? scenario)
    {
        if (scenario?.StartingSites == null || State == null) return;
        bool first = true;
        foreach (var site in scenario.StartingSites)
        {
            if (string.IsNullOrWhiteSpace(site.Zone)) continue;
            int area = scenario.ProductionAreaOf(site);
            if (first && !ProductionAreas.IsValid(State.HomeProductionArea))
                State.HomeProductionArea = area;
            first = false;
            var zone = FindZone(site.Zone);
            if (zone != null && !ProductionAreas.IsValid(zone.ProductionArea))
                zone.ProductionArea = area;
        }
    }

    /// <summary>
    /// Daily, before the water chemistry runs: every zone that follows the sea
    /// takes its production area's temperature for today (a zone with no area
    /// takes the home area's).
    /// </summary>
    internal void ApplyProductionAreaSeaTemperatures()
    {
        int home = HomeProductionArea;
        Weather.SetReferenceProductionArea(home);
        for (int i = 0; i < State.Zones.Count; i++)
        {
            var zone = State.Zones[i];
            int area = ProductionAreas.IsValid(zone.ProductionArea) ? zone.ProductionArea : home;
            zone.TakeSeaTemperature(Weather.SeaTemperatureAt(area));
        }
    }

    /// <summary>
    /// The water a population lives in today (the S2-02 seam, the one source of
    /// truth for the water at the fish): sea pens get their zone's water;
    /// offshore pens the sea water of their farm's area with the farm's
    /// current-fed oxygen; land tanks the facility's controlled water. Growth,
    /// appetite, feeding, the disease tick, temperature-stress deaths and the
    /// mortality hazards (<see cref="ResolveCohortEnvironment"/>) take this
    /// value, never a global one. The holding is resolved once per simulated
    /// step (<see cref="HoldingOf"/>); the water is read live.
    /// </summary>
    public HoldingWater HoldingWaterOf(FishPopulation population)
        => WaterOf(HoldingOf(population), population);

    /// <summary>
    /// Temperature-stress deaths, each population in its own water: a PO13 pen
    /// in a 4 °C March and a PO1 pen in a 17 °C August no longer share one sea
    /// temperature. Land tanks run at controlled temperature and stay exempt.
    /// Deaths go through the S2-05 hazard and are booked by cause
    /// (<see cref="ApplyTemperatureStressHazard"/>). Each population's holding
    /// is resolved once (<see cref="HoldingOf"/>) and its environment built
    /// from it.
    /// </summary>
    internal void ApplyHoldingWaterTemperatureStress()
    {
        for (int i = 0; i < State.Populations.Count; i++)
        {
            var population = State.Populations[i];
            var holding = HoldingOf(population);
            if (holding.Kind == HoldingKind.LandTank) continue;
            ApplyTemperatureStressHazard(population, EnvironmentOf(holding, population, seaTemperatureC: null));
        }
    }

    private Zone? FindZone(string? zoneId)
    {
        if (State == null || string.IsNullOrEmpty(zoneId)) return null;
        for (int i = 0; i < State.Zones.Count; i++)
            if (State.Zones[i].Id == zoneId)
                return State.Zones[i];
        return null;
    }
}
