using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Calibration;
using LakskongenSim.Farm;

namespace LakskongenSim.Core;

/// <summary>
/// The build dialog's diagnosis strip and site lens (roadmap F2-31,
/// docs/FARM_TYPES_SPEC.md §1 "what limits you" and §6.2): which constraint
/// binds the company's fjord farming today, from the engine's own data, and
/// which families remove it; and every land pad and offshore slot as the site
/// lens reads it. Pure reads.
/// </summary>
public partial class SimulationEngine
{
    /// <summary>The diagnosis strip's constraint keys.</summary>
    public static class BuildConstraintKeys
    {
        /// <summary>Lice: the traffic light of the areas the company farms.</summary>
        public const string Lice = "lice";
        /// <summary>Capacity: licence MTB in use in the fullest area.</summary>
        public const string Capacity = "capacity";
        /// <summary>Temperature: the warmest month against the growth law's optimum.</summary>
        public const string Temperature = "temperature";
        /// <summary>Oxygen and the water: the share of the company's deaths from low oxygen, algae and jellyfish.</summary>
        public const string Oxygen = "oxygen";
        /// <summary>Capital: cash against the cheapest Land or Hav entry rung.</summary>
        public const string Capital = "capital";
    }

    private static readonly string[] BothFamilies = { FarmTypeIds.Land, FarmTypeIds.Hav };
    private static readonly string[] LandOnly = { FarmTypeIds.Land };

    /// <summary>
    /// The constraints on the company's farming, the binding one first: lice
    /// (red 1.0, yellow 0.6, green 0.15 in the worst farmed area), capacity
    /// (standing biomass over licence MTB), temperature (the warmest month's
    /// normal past the growth law's optimum, as a share of the way to its
    /// upper limit), oxygen (the share of deaths from low oxygen, algae and
    /// jellyfish) and capital (how far the cash is from the cheapest locked
    /// Land or Hav rung). Lice and capacity are removed by Land and Hav,
    /// temperature and oxygen by Land; nothing removes capital (the Hav
    /// consortium lowers it).
    /// </summary>
    public BuildDiagnosis GetBuildDiagnosis()
    {
        if (State == null) return new BuildDiagnosis(Array.Empty<BuildConstraint>());
        var areas = FarmedAreas();
        var overview = GetExpansionOverview().Areas.Where(a => areas.Contains(a.ProductionArea)).ToList();
        var list = new List<BuildConstraint>();
        if (overview.Count > 0)
        {
            var worst = overview.OrderByDescending(a => LightIndex(a.TrafficLight)).ThenBy(a => a.ProductionArea).First();
            int light = LightIndex(worst.TrafficLight);
            list.Add(new BuildConstraint(BuildConstraintKeys.Lice, light switch { 2 => 1.0, 1 => 0.6, _ => 0.15 },
                worst.ProductionArea, light, 2, BothFamilies));
            var licensed = overview.Where(a => a.LicenceMtbTonnes > 0.0).ToList();
            if (licensed.Count > 0)
            {
                var fullest = licensed.OrderByDescending(a => a.StandingTonnes / a.LicenceMtbTonnes).ThenBy(a => a.ProductionArea).First();
                double use = fullest.StandingTonnes / fullest.LicenceMtbTonnes;
                list.Add(new BuildConstraint(BuildConstraintKeys.Capacity, Math.Clamp(use, 0.0, 1.0), fullest.ProductionArea,
                    use, fullest.LicenceMtbTonnes, BothFamilies));
            }
            var warmest = overview.OrderByDescending(a => a.SeaTemperatureMaxC).ThenBy(a => a.ProductionArea).First();
            var growth = (Growth?.Law ?? TgcGrowthLaw.Default).Calibration;
            double heat = (warmest.SeaTemperatureMaxC - growth.OptimalTemperatureC) / (growth.UpperGrowthLimitC - growth.OptimalTemperatureC);
            list.Add(new BuildConstraint(BuildConstraintKeys.Temperature, Math.Clamp(heat, 0.0, 1.0), warmest.ProductionArea,
                warmest.SeaTemperatureMaxC, growth.OptimalTemperatureC, LandOnly));
        }
        var deaths = MortalityLedger.Cumulative();
        long all = deaths.Total.Fish;
        if (all > 0)
        {
            long water = deaths.Deaths(MortalityCause.LowOxygen).Fish + deaths.Deaths(MortalityCause.HarmfulAlgae).Fish
                + deaths.Deaths(MortalityCause.Jellyfish).Fish;
            double share = (double)water / all;
            list.Add(new BuildConstraint(BuildConstraintKeys.Oxygen, share, 0, share, 1.0, LandOnly));
        }
        double rung = CheapestLockedRungNok();
        double cash = (double)(FinanceManager?.Balance ?? 0m);
        if (rung > 0.0)
            list.Add(new BuildConstraint(BuildConstraintKeys.Capital, cash >= rung ? 0.0 : 1.0 - Math.Max(0.0, cash) / rung,
                0, cash, rung, Array.Empty<string>()));
        return new BuildDiagnosis(list.OrderByDescending(c => c.Score).ThenBy(c => c.Key, StringComparer.Ordinal).ToList());
    }

    /// <summary>The production areas of the company's fjord sites (the home area when it has none).</summary>
    private HashSet<int> FarmedAreas()
    {
        var areas = new HashSet<int>(FjordSites().Select(ProductionAreaOfSite));
        if (areas.Count == 0) areas.Add(HomeProductionArea);
        return areas;
    }

    private static int LightIndex(string light) => light switch { "red" => 2, "yellow" => 1, _ => 0 };

    /// <summary>The cash the cheapest Land or Hav variant's money gate asks for (0 when every rung is paid for).</summary>
    private double CheapestLockedRungNok()
        => GetFarmTypeCatalog()
            .Where(f => f.Family.Id != FarmTypeIds.Sjo)
            .SelectMany(f => f.Variants)
            .Where(v => v.Variant.Stage == 1)
            .SelectMany(v => v.Gates)
            .Where(g => g.Gate.Kind == FarmTypeGateKind.Funds)
            .Select(g => g.Gate.Required)
            .DefaultIfEmpty(0.0)
            .Min();

    // ── The site lens ──────────────────────────────────────────────────────

    /// <summary>Every land pad ("land") or offshore slot ("hav") with what the site lens shows, overflow plots left out.</summary>
    public IReadOnlyList<BuildPlotView> GetBuildPlots(string familyId)
    {
        if (State == null) return Array.Empty<BuildPlotView>();
        var kind = familyId == FarmTypeIds.Land ? PlotKind.LandPad : familyId == FarmTypeIds.Hav ? PlotKind.OffshoreSlot : (PlotKind?)null;
        if (kind == null) return Array.Empty<BuildPlotView>();
        return SitePlots.Where(p => p.Kind == kind && !p.IsOverflow).Select(PlotLens).ToList();
    }

    private BuildPlotView PlotLens(SitePlot p)
    {
        bool known = Array.IndexOf(OffshorePlotsCalibration.PlotIds, p.Id) >= 0;
        double scale = PlotHsScaleOf(p.Id);
        var metocean = OffshoreCal.Metocean;
        bool offshore = p.Kind == PlotKind.OffshoreSlot;
        bool revealed = !double.IsNaN(p.LiceExternalRevealed);
        return new BuildPlotView
        {
            Id = p.Id,
            Name = p.Name,
            FamilyId = offshore ? FarmTypeIds.Hav : FarmTypeIds.Land,
            IsFree = p.IsFree,
            OwnerUnitId = p.OwnerUnitId,
            WorldX = p.WorldX,
            WorldZ = p.WorldZ,
            RealDistanceKm = p.RealDistanceKm,
            PadWidthM = p.PadWidthM,
            PadDepthM = p.PadDepthM,
            GrowOutLots = p.PhaseLots,
            PostSmoltModules = p.PostSmoltModules,
            DeepIntakeReachM = p.DeepIntakeReachM,
            PumpCapacityM3sPerLot = p.PumpCapacityM3sPerLot,
            GridCapacityMW = offshore ? 0.0 : EffectiveGridCapacityMW(p.Id),
            GridFeeds = offshore ? 0 : EffectiveGridFeeds(p.Id),
            Zoned = !offshore && IsPadZoned(p.Id),
            PreZoned = p.PreZoned,
            NatureSensitivity = p.NatureSensitivity,
            BlastCostNok = p.BlastCostNOK,
            Exposure = offshore ? p.Exposure.ToString() : "",
            WaterDepthM = p.WaterDepthM,
            Hs100M = offshore ? known ? OffshoreCal.Plots.For(p.Id).Hs100M : p.Hs100M : 0.0,
            WinterMeanHsM = offshore ? metocean.ReferenceMonthlyMeanHsM(0) * scale : 0.0,
            SummerMeanHsM = offshore ? metocean.ReferenceMonthlyMeanHsM(6) * scale : 0.0,
            LiceExternalMin = revealed ? p.LiceExternalRevealed : p.LiceExternalMin,
            LiceExternalMax = revealed ? p.LiceExternalRevealed : p.LiceExternalMax,
        };
    }
}
