using System.Linq;
using LakskongenSim.Farm;

namespace LakskongenSim.Core;

/// <summary>
/// Containment and the sea sites' preventive hardware (Phase 1.5, 4.1 and P8),
/// moved verbatim out of <c>SimulationEngine.cs</c> by F2-02
/// (docs/FARM_TYPES_SPEC.md §7.1): a site's containment rung and what it does
/// to oxygen demand, growth, storm wear and escapes
/// (<see cref="ContainmentModifiersOf"/>), the red-zone MTB credit of a
/// contained site (<see cref="ApplyRedZoneContainmentCredit"/>), the lice
/// settlement a skirt, a containment rung, a permeable barrier and research
/// block (<see cref="SiteSettlementBlocking"/>, <see cref="UpdatePreventiveCoverage"/>)
/// and the hardware's daily and weekly upkeep. F2-10 (closed and semi-closed
/// pens: one skirt model, the stored credit, pumps) changes it here.
/// </summary>
public partial class SimulationEngine
{
    private const int BARRIER_CLEANING_TICK_DAYS = 7; // P8: barrier cleaning decay cadence (weekly)

    /// <summary>Settlement blocked by a lice skirt (Phase 1.5).</summary>
    private const double LiceSkirtSettlementBlocking = 0.70;

    /// <summary>
    /// What a site's containment rung does (Phase 4.1): oxygen demand, growth,
    /// storm wear and escape risk, lice exclusion. Open net for no site (a
    /// population in no site's pens).
    /// </summary>
    internal static ContainmentModifiers ContainmentModifiersOf(SeaSite? site)
        => ContainmentTech.ModifiersFor(site?.Containment ?? ContainmentLevel.OpenNet);

    /// <summary>
    /// Phase 4.1: red-zone MAB credit for contained sites (exact revert on exit
    /// — re-entries never compound). Called by the traffic light when zone
    /// <paramref name="zoneId"/> changes color to <paramref name="after"/>.
    /// </summary>
    internal void ApplyRedZoneContainmentCredit(string zoneId, Data.ZoneColor after)
    {
        foreach (var site in State.SeaSites.Where(s => s.ZoneId == zoneId))
        {
            if (after.ToString() == "Red" && !site.RedZoneCreditApplied
                && site.Containment >= ContainmentLevel.SemiClosed)
            {
                double credit = ContainmentTech.MabCreditFor(site.Containment, true);
                site.MABTonnes *= credit;
                site.RedZoneCreditApplied = true;
                site.AppliedContainmentCredit = credit; // F2-03: the factor applied, stored (F2-10 divides by it)
                SimLog.Info($"🛡️ Site {site.Id} red-zone credit ×{credit:F2} ({site.Containment})");
            }
            else if (after.ToString() != "Red" && site.RedZoneCreditApplied)
            {
                double credit = ContainmentTech.MabCreditFor(site.Containment, true);
                site.MABTonnes /= credit;
                site.RedZoneCreditApplied = false;
                site.AppliedContainmentCredit = 1.0;
            }
        }
    }

    /// <summary>
    /// Preventive mitigation OPEX (Phase 1.5): silent daily upkeep of every sea
    /// site's lice skirt (2 000 NOK/site/day) and seal deterrent (1 000
    /// NOK/site/day), paid by the daily finance.
    /// </summary>
    internal decimal PreventiveMitigationDailyOpexNok()
    {
        decimal mitigationOpex = 0m;
        foreach (var site in State.SeaSites)
        {
            if (site.HasLiceSkirt) mitigationOpex += 2_000m;
            if (site.HasSealDeterrent) mitigationOpex += 1_000m;
        }
        return mitigationOpex;
    }

    /// <summary>
    /// P8: approximate tidal flow (m/s) at a site from its zone's current
    /// speed, falling back to a sensible default when no zone is found.
    /// </summary>
    private double SiteFlowMs(Farm.SeaSite site) =>
        State.Zones.FirstOrDefault(z => z.Id == site.ZoneId)?.CurrentSpeed
            ?? Farm.BarrierTech.DefaultFlowMs;

    /// <summary>
    /// Weekly preventive coverage (Phase 1.5 + 4.1): fish-weighted effective
    /// lice exclusion combining skirt toggles and containment levels.
    /// Per-site: 1-(1-skirt*0.7)*(1-exclusion). Drives settlement reduction.
    /// P8 adds a flow-dependent permeable-barrier factor (no-op when none).
    /// </summary>
    internal void UpdatePreventiveCoverage()
    {
        if (SeaLice == null) return;
        double total = 0, covered = 0;
        foreach (var site in State.SeaSites)
        {
            int fish = site.Pens.SelectMany(p => p.Cohorts).Sum(c => c.Count);
            total += fish;
            covered += fish * SiteSettlementBlocking(site);

            if (site.Barrier != Farm.BarrierKind.None)
            {
                // Periodic cleaning requirement: uncleaned barriers lose
                // efficiency (SiteSettlementBlocking), and cleaning costs standing opex.
                site.BarrierDaysSinceCleaning += BARRIER_CLEANING_TICK_DAYS;
                decimal barrierOpex = Farm.BarrierTech.SpecFor(site.Barrier).WeeklyOpexNok;
                PayCost(barrierOpex, CostBooking.Opex, "barriers");
            }

            // Phase 4.1: standing energy opex per containment level.
            decimal opex = Farm.ContainmentTech.ModifiersFor(site.Containment).WeeklyOpexNok;
            PayCost(opex, CostBooking.Opex, "containment");
        }
        SeaLice.PreventiveCoverage01 = total > 0 ? covered / total : 0.0;
    }

    /// <summary>
    /// P8: a permeable barrier also excludes jellyfish, reducing a bloom's
    /// counter-measure multiplier by the flow-dependent jellyfish blocking:
    /// the share of a <paramref name="bloomKind"/> bloom's damage that passes
    /// <paramref name="site"/>'s barrier (1 for any other bloom or no barrier).
    /// </summary>
    internal double BarrierBloomPassFraction(SeaSite site, string bloomKind)
    {
        if (bloomKind != "jellyfish" || site.Barrier == Farm.BarrierKind.None) return 1.0;
        double jellyBlock = Farm.BarrierTech.EffectiveJellyfishBlocking(
            site.Barrier, SiteFlowMs(site), site.BarrierDaysSinceCleaning);
        return 1.0 - System.Math.Clamp(jellyBlock, 0.0, 1.0);
    }

    /// <summary>
    /// Phase 4.1 load migration: a save from before containment rungs had only
    /// the legacy flags; a closed site becomes ClosedFloat and a skirt owner the
    /// Skirt rung (an open-net site with neither keeps OpenNet).
    /// </summary>
    internal static void MigrateLegacyContainmentFlags(SeaSite site)
    {
        if (site.Containment == ContainmentLevel.OpenNet)
        {
            if (site.IsClosedContainment)
                site.Containment = ContainmentLevel.ClosedFloat;
            else if (site.HasLiceSkirt)
                site.Containment = ContainmentLevel.Skirt;
        }
        // F2-03: a save from before the stored credit factor holds a credit
        // that was applied with the rung's factor the exit divides by today.
        if (site.RedZoneCreditApplied && site.AppliedContainmentCredit == 1.0)
            site.AppliedContainmentCredit = ContainmentTech.MabCreditFor(site.Containment, true);
        else if (!site.RedZoneCreditApplied)
            site.AppliedContainmentCredit = 1.0;
    }

    /// <summary>
    /// Fraction of new lice settlement blocked at one site: lice skirt,
    /// containment exclusion (Phase 4.1), a permeable barrier (P8, flow- and
    /// cleaning-dependent) and research lice reduction combine multiplicatively.
    /// </summary>
    private double SiteSettlementBlocking(Farm.SeaSite site)
    {
        double skirt = site.HasLiceSkirt ? LiceSkirtSettlementBlocking : 0.0;
        double exclusion = Farm.ContainmentTech.ModifiersFor(site.Containment).LiceExclusion01;
        double barrier = site.Barrier != Farm.BarrierKind.None
            ? Farm.BarrierTech.EffectiveLiceBlocking(site.Barrier, SiteFlowMs(site), site.BarrierDaysSinceCleaning)
            : 0.0;
        double research = ResearchEffectKeys.ClampReduction(LiceReduction);
        return 1.0 - (1.0 - skirt) * (1.0 - exclusion) * (1.0 - barrier) * (1.0 - research);
    }
}
