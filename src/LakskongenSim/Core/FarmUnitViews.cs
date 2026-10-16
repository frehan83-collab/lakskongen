using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Farm;

namespace LakskongenSim.Core;

/// <summary>One plot of the game as the map and the build flow read it (F2-03).</summary>
/// <param name="Id">"pad_a", "off_1", "overflow_land_1"…</param>
/// <param name="Name">The place name ("Storholmen").</param>
/// <param name="Kind">Land pad or offshore slot.</param>
/// <param name="WorldX">Centre x (world metres).</param>
/// <param name="WorldZ">Centre z (world metres).</param>
/// <param name="RealDistanceKm">Logistics distance (km).</param>
/// <param name="OwnerUnitId">The unit on it; "" when free.</param>
/// <param name="IsFree">True when no unit holds it and its approval stands.</param>
/// <param name="IsOverflow">True for a reserved overflow plot.</param>
/// <param name="PreZoned">True when zoned for aquaculture.</param>
/// <param name="PadWidthM">Pad width (m; 0 for a slot).</param>
/// <param name="PadDepthM">Pad depth (m; 0 for a slot).</param>
/// <param name="GrowOutLots">Grow-out lots the pad holds.</param>
/// <param name="PostSmoltModules">Post-smolt modules the pad holds.</param>
/// <param name="GridCapacityMW">Grid capacity (MW).</param>
/// <param name="Exposure">Exposure class of a slot ("OuterCoast"…; "" for a pad).</param>
/// <param name="Hs100M">100-year Hs of a slot (m; 0 for a pad).</param>
/// <param name="WaterDepthM">Water depth of a slot (m; 0 for a pad).</param>
/// <param name="LiceExternalMultiplier">The external lice pressure at a slot (1 for a pad).</param>
public sealed record SitePlotView(string Id, string Name, PlotKind Kind, double WorldX, double WorldZ, double RealDistanceKm,
    string OwnerUnitId, bool IsFree, bool IsOverflow, bool PreZoned, double PadWidthM, double PadDepthM, int GrowOutLots,
    int PostSmoltModules, double GridCapacityMW, string Exposure, double Hs100M, double WaterDepthM, double LiceExternalMultiplier);

/// <summary>One offshore unit as the game reads it (F2-03).</summary>
/// <param name="Id">The unit's (sea site's) id.</param>
/// <param name="Name">The name the player reads.</param>
/// <param name="PlotId">Its slot.</param>
/// <param name="ZoneId">Its water area (the slot's id).</param>
/// <param name="FarmType">Structure class ("SemiSubmersible", "ShipShaped", "Submersible").</param>
/// <param name="Zones">Zones (pens) of the unit.</param>
/// <param name="OccupiedZones">Zones holding fish.</param>
/// <param name="MtbTonnes">The unit's MTB (t).</param>
/// <param name="FishCount">Live fish.</param>
/// <param name="BiomassTonnes">Standing biomass (t).</param>
/// <param name="MeanWeightKg">Mean live weight (kg; 0 when empty).</param>
/// <param name="DesignHsM">Design Hs (m, with any reinforcement).</param>
/// <param name="Hs100M">Its slot's 100-year Hs (m).</param>
/// <param name="ExternalLiceMultiplier">Its slot's external lice pressure.</param>
/// <param name="LicePerFish">Its lice model's mean per fish.</param>
/// <param name="Operational">True once built.</param>
/// <param name="ConstructionProgress">0-1.</param>
/// <param name="StormLockdown">True on a day the sea keeps the crew from working.</param>
/// <param name="WaveHeightM">Today's Hs at the unit (m).</param>
/// <param name="CrewDaysSinceRotation">Days since the last crew change.</param>
/// <param name="CrewRotationDays">Days between crew changes.</param>
/// <param name="StructuralIntegrity">0-1.</param>
/// <param name="EmployeeCount">Crew in rotation.</param>
public sealed record OffshoreUnitView(string Id, string Name, string PlotId, string ZoneId, string FarmType, int Zones,
    int OccupiedZones, double MtbTonnes, long FishCount, double BiomassTonnes, double MeanWeightKg, double DesignHsM,
    double Hs100M, double ExternalLiceMultiplier, double LicePerFish, bool Operational, double ConstructionProgress,
    bool StormLockdown, double WaveHeightM, int CrewDaysSinceRotation, int CrewRotationDays, double StructuralIntegrity,
    int EmployeeCount);

/// <summary>One land site as the game reads it (F2-03).</summary>
/// <param name="Id">The land site's id.</param>
/// <param name="Name">The name the player reads.</param>
/// <param name="Kind">"GrowOut" or "PostSmolt".</param>
/// <param name="PlotId">Its pad.</param>
/// <param name="ZoneId">Its water area (the pad's id).</param>
/// <param name="Tanks">Tanks of the site.</param>
/// <param name="TankVolumeM3">Water volume of one tank (m³).</param>
/// <param name="CapacityTonnesPerYear">Nameplate production (t a year).</param>
/// <param name="Modules">Post-smolt modules (0 on a grow-out site).</param>
/// <param name="Populations">Populations (batches) in the tanks.</param>
/// <param name="FishCount">Live fish.</param>
/// <param name="BiomassTonnes">Standing biomass (t).</param>
/// <param name="MeanWeightKg">Mean live weight (kg; 0 when empty).</param>
/// <param name="WaterTemperatureC">The site's water today (°C).</param>
/// <param name="OxygenMgPerL">Its oxygen (mg/L).</param>
/// <param name="Co2MgPerL">Its CO2 (mg/L).</param>
/// <param name="Operational">True once built.</param>
/// <param name="ConstructionProgress">0-1.</param>
/// <param name="EmployeeCount">Staff.</param>
/// <param name="FishCapacity">Fish a post-smolt site's modules hold at once (0 on a grow-out site).</param>
/// <param name="TransferWeightG">Weight a post-smolt batch goes to sea at (g; 0 on a grow-out site).</param>
public sealed record LandSiteView(string Id, string Name, string Kind, string PlotId, string ZoneId, int Tanks,
    double TankVolumeM3, double CapacityTonnesPerYear, int Modules, int Populations, long FishCount, double BiomassTonnes,
    double MeanWeightKg, double WaterTemperatureC, double OxygenMgPerL, double Co2MgPerL, bool Operational,
    double ConstructionProgress, int EmployeeCount, long FishCapacity, double TransferWeightG);

/// <summary>
/// The farm-type list views (F2-03): the plots, the offshore units and the
/// land sites as the bridge's list verbs project them
/// (<c>GameBridge.Offshore.cs</c>, <c>GameBridge.LandSites.cs</c>). Reads only.
/// </summary>
public partial class SimulationEngine
{
    /// <summary>The game's plots of <paramref name="kind"/> (named plots, then the overflow plots a migrated save uses).</summary>
    public IReadOnlyList<SitePlotView> SitePlotViews(PlotKind kind)
        => SitePlots.Where(p => p.Kind == kind).Select(p => new SitePlotView(p.Id, p.Name, p.Kind, p.WorldX, p.WorldZ,
            p.RealDistanceKm, p.OwnerUnitId, p.IsFree, p.IsOverflow, p.PreZoned, p.PadWidthM, p.PadDepthM, p.PhaseLots,
            p.PostSmoltModules, p.GridCapacityMW, p.Kind == PlotKind.OffshoreSlot ? p.Exposure.ToString() : "",
            p.Hs100M, p.WaterDepthM, p.Kind == PlotKind.OffshoreSlot ? p.LiceExternalMultiplier : 1.0)).ToList();

    /// <summary>The company's offshore units, in the site list's order.</summary>
    public IReadOnlyList<OffshoreUnitView> OffshoreUnitViews()
    {
        var views = new List<OffshoreUnitView>();
        foreach (var site in OffshoreUnits)
        {
            var unit = site.Offshore;
            if (unit == null) continue;
            long fish = site.Pens.Sum(p => (long)p.TotalCount);
            double kg = site.Pens.Sum(p => p.GetTotalBiomassKg());
            var plot = FindPlot(site.PlotId);
            views.Add(new OffshoreUnitView(site.Id, site.Label, site.PlotId, site.ZoneId, unit.FarmType.ToString(),
                site.PhysicalPens.Count, site.PhysicalPens.Count(p => site.PopulationIn(p)?.TotalCount > 0),
                site.MABTonnes, fish, kg / 1000.0, fish > 0 ? kg / fish : 0.0, unit.DesignHsM + unit.DesignHsBonusM,
                plot?.Hs100M ?? 0.0, PlotLiceMultiplierOf(site), site.Lice.AverageLicePerFish, unit.IsOperational,
                unit.ConstructionProgress, unit.IsStormLockdown, unit.CurrentWaveHeight, unit.DaysSinceCrewRotation,
                unit.CrewRotationDays, unit.StructuralIntegrity, unit.EmployeeCount));
        }
        return views;
    }

    /// <summary>The company's land sites (grow-out and post-smolt), in list order.</summary>
    public IReadOnlyList<LandSiteView> LandSiteViews()
    {
        var views = new List<LandSiteView>();
        foreach (var site in LandSites)
        {
            long fish = site.Tanks.Sum(p => (long)p.TotalCount);
            double kg = site.GetTotalBiomassKg();
            bool postSmolt = site.Kind == LandSiteKind.PostSmolt;
            int modules = postSmolt ? PostSmoltModulesOf(site) : 0;
            views.Add(new LandSiteView(site.Id, site.Name, site.Kind.ToString(), site.PlotId, site.ZoneId, site.TankCount,
                site.TankVolumeM3, site.CapacityTonnesPerYear, modules, site.Tanks.Count, fish, kg / 1000.0,
                fish > 0 ? kg / fish : 0.0, site.CurrentWaterTemp, site.OxygenLevel, site.CO2Level, site.IsOperational,
                site.ConstructionProgress, site.EmployeeCount, (long)modules * PostSmoltFishPerModule,
                postSmolt ? PostSmoltTargetWeightG : 0.0));
        }
        return views;
    }
}
