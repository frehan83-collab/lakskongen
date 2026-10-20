using System;
using LakskongenSim.Data;

namespace LakskongenSim.Farm
{
    /// <summary>
    /// Red-zone environmental-flexibility (miljøfleksibilitet) capacity recovery.
    ///
    /// <para>
    /// Norway's 2026 traffic-light regime assesses the 13 production zones as
    /// 3 green (+6% MAB), 9 yellow (hold) and 1 red (PO3, −6% MAB). A red zone
    /// would normally be forced to cut Maximum Allowable Biomass. The regulator
    /// allows a red-zone producer to <b>recover capacity</b> by farming in
    /// closed containment, which blocks sea-lice settlement and protects wild
    /// salmon — the "environmental flexibility" pathway.
    /// </para>
    ///
    /// <para>
    /// This type decides eligibility from the site's <see cref="ContainmentLevel"/>
    /// plus the zone's <see cref="ZoneColor"/>, and returns the resulting MAB
    /// multiplier. It reuses the existing <see cref="ContainmentTech"/> table and
    /// the additive <see cref="ContainmentTech.IsClosedContainment"/> helper, so
    /// existing containment behavior is untouched.
    /// </para>
    /// </summary>
    public static class EnvironmentalFlexibility
    {
        /// <summary>Baseline red-zone MAB factor (−6%), mirroring the traffic-light rule.</summary>
        public const double RedZoneMabFactor = 0.94;

        /// <summary>
        /// A site qualifies for red-zone capacity recovery only when the zone is
        /// RED and the site uses closed containment (SemiClosed or higher).
        /// </summary>
        public static bool QualifiesForRedZoneRecovery(ContainmentLevel level, ZoneColor color) =>
            color == ZoneColor.Red && ContainmentTech.IsClosedContainment(level);

        /// <summary>
        /// MAB multiplier to apply to a site's baseline capacity.
        ///
        /// <list type="bullet">
        /// <item><description>Non-red zones: <c>1.0</c> (recovery is a red-zone-only mechanism;
        /// green/yellow consequences are handled by the traffic-light system).</description></item>
        /// <item><description>Red + non-closed site: <c>0.94</c> (the standard −6% cut).</description></item>
        /// <item><description>Red + qualifying closed site: the cut is offset by the containment
        /// MAB credit (SemiClosed +0.03, ClosedFloat/Submersible +0.06, OffshoreClosed +0.10),
        /// capped at full recovery (<c>1.0</c>).</description></item>
        /// </list>
        /// </summary>
        public static double MabMultiplier(ContainmentLevel level, ZoneColor color)
        {
            if (color != ZoneColor.Red)
                return 1.0;

            if (!QualifiesForRedZoneRecovery(level, color))
                return RedZoneMabFactor;

            var credit = ContainmentTech.MabCreditFor(level, isRedZone: true);
            return Math.Min(1.0, RedZoneMabFactor + (credit - 1.0));
        }

        /// <summary>
        /// Convenience snapshot of a site's red-zone flexibility outcome.
        /// </summary>
        public static EnvironmentalFlexibilityResult Evaluate(ContainmentLevel level, ZoneColor color)
        {
            var eligible = QualifiesForRedZoneRecovery(level, color);
            var multiplier = MabMultiplier(level, color);
            var isClosed = ContainmentTech.IsClosedContainment(level);
            return new EnvironmentalFlexibilityResult(eligible, isClosed, multiplier);
        }
    }

    /// <summary>Outcome of a red-zone environmental-flexibility evaluation.</summary>
    /// <param name="QualifiesForRecovery">True when a red zone with closed containment may recover capacity.</param>
    /// <param name="IsClosedContainment">True for SemiClosed containment and above.</param>
    /// <param name="MabMultiplier">MAB multiplier applied to baseline capacity.</param>
    public sealed record EnvironmentalFlexibilityResult(
        bool QualifiesForRecovery,
        bool IsClosedContainment,
        double MabMultiplier);
}
