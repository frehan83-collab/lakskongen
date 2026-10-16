using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Farm;
using LakskongenSim.Farm.Offshore;
using LakskongenSim.Finance;

namespace LakskongenSim.Core;

/// <summary>One unlock gate of a farm-type variant with its status in this game.</summary>
public sealed record FarmTypeGateStatus(FarmTypeGate Gate, bool Met, double Current);

/// <summary>
/// One farm-type variant with its status in this game: unlocked when every gate
/// but money is met, affordable when the money gate is met too.
/// </summary>
public sealed record FarmTypeVariantStatus(FarmTypeVariant Variant, IReadOnlyList<FarmTypeGateStatus> Gates)
{
    /// <summary>Every gate other than money is met (the player may build once the money is there).</summary>
    public bool Unlocked => Gates.Where(g => g.Gate.Kind != FarmTypeGateKind.Funds).All(g => g.Met);

    /// <summary>The money gate is met.</summary>
    public bool Affordable => Gates.Where(g => g.Gate.Kind == FarmTypeGateKind.Funds).All(g => g.Met);
}

/// <summary>One farm-type family with its variants' status.</summary>
public sealed record FarmTypeFamilyStatus(FarmTypeFamily Family, IReadOnlyList<FarmTypeVariantStatus> Variants);

/// <summary>
/// The farm-type catalogue in this game (roadmap F2-04, docs/FARM_TYPES_SPEC.md
/// §2C, §6.1): the three families and seven variants of
/// <see cref="FarmTypeCatalog"/> with every unlock gate evaluated on the game's
/// state (research, harvests, operating units, zoned pads, escapes, money).
/// The bridge's <c>GetFarmTypeCatalog</c> hands it to the UI, which holds no
/// farm-type number of its own. A read: it changes nothing and draws nothing.
/// </summary>
public partial class SimulationEngine
{
    /// <summary>The catalogue with every gate's status.</summary>
    public IReadOnlyList<FarmTypeFamilyStatus> GetFarmTypeCatalog()
    {
        var families = FarmTypeCatalog.Build(_calibration, Research ?? new ResearchSystem());
        return families.Select(f => new FarmTypeFamilyStatus(f, f.Variants.Select(StatusOf).ToList())).ToList();
    }

    /// <summary>The status of one variant (null for an unknown id).</summary>
    public FarmTypeVariantStatus? FarmTypeStatus(string variantId)
        => GetFarmTypeCatalog().SelectMany(f => f.Variants).FirstOrDefault(v => v.Variant.Id == variantId);

    /// <summary>True when every gate of <paramref name="variantId"/> but money is met (the build verbs' not_unlocked check).</summary>
    public bool IsFarmTypeUnlocked(string variantId) => FarmTypeStatus(variantId)?.Unlocked == true;

    private FarmTypeVariantStatus StatusOf(FarmTypeVariant variant)
        => new(variant, variant.Gates.Select(Evaluate).ToList());

    private FarmTypeGateStatus Evaluate(FarmTypeGate gate)
    {
        double current = gate.Kind switch
        {
            FarmTypeGateKind.Start => 1.0,
            FarmTypeGateKind.Research => Research?.Nodes.TryGetValue(gate.Subject, out var node) == true && node.Unlocked ? 1.0 : 0.0,
            FarmTypeGateKind.FirstHarvest => State?.HarvestBatchesSold ?? 0,
            FarmTypeGateKind.OperatingPostSmoltModule => PostSmoltSites.Count(s => s.IsOperational),
            FarmTypeGateKind.ZonedPad => ZonedFreePads(),
            FarmTypeGateKind.EscapeFreeDays => EscapeFreeDays(),
            FarmTypeGateKind.DevelopmentLicence => 0.0, // F2-09 brings the development-licence round
            FarmTypeGateKind.OwnUnitCompletedCycle => CompletedCyclesOf(gate.Subject),
            FarmTypeGateKind.Funds => (double)(FinanceManager?.Balance ?? 0m),
            _ => 0.0,
        };
        return new FarmTypeGateStatus(gate, current >= gate.Required, current);
    }

    /// <summary>Free land pads the company may build a lot on without a zoning case: pre-zoned, or zoned by an approved case.</summary>
    private int ZonedFreePads()
    {
        var zonedByCase = new HashSet<string>(
            (State?.PermitCases ?? new List<PermitCase>())
                .Where(c => c.Kind == PermitKind.Zoning && c.Status == PermitStatus.Approved)
                .Select(c => c.PlotId), StringComparer.Ordinal);
        return SitePlots.Count(p => p.Kind == PlotKind.LandPad && p.IsFree && (p.PreZoned || zonedByCase.Contains(p.Id)));
    }

    /// <summary>Days since the company's last escape (the game's age when there has been none in the escape window).</summary>
    private int EscapeFreeDays()
    {
        if (State == null) return 0;
        int last = int.MinValue;
        foreach (var site in State.SeaSites)
            foreach (var incident in site.EscapeIncidents)
                if (incident.Day > last) last = incident.Day;
        return last == int.MinValue ? State.Day : Math.Max(0, State.Day - last);
    }

    /// <summary>Cycles the company's own units of a variant have completed (OF1-class units count theirs; land sites do not count yet).</summary>
    private int CompletedCyclesOf(string variantId) => variantId switch
    {
        FarmTypeIds.Of1 => OffshoreUnits.Where(s => s.Offshore?.FarmType == OffshoreType.SemiSubmersible).Sum(s => s.Offshore!.CompletedCycles),
        FarmTypeIds.Havfarm => OffshoreUnits.Where(s => s.Offshore?.FarmType == OffshoreType.ShipShaped).Sum(s => s.Offshore!.CompletedCycles),
        _ => 0, // a land site's cycles arrive with F2-12/F2-15
    };
}
