using System;

namespace LakskongenSim.Farm
{
    /// <summary>
    /// On-site interventions that can blunt a harmful algal bloom / jellyfish /
    /// hypoxia event. Aligned with the flags already modelled on <see cref="SeaSite"/>
    /// (<see cref="SeaSite.HasBubbleCurtain"/>, <see cref="SeaSite.HasO2Diffuser"/>).
    /// </summary>
    public enum MitigationKind
    {
        /// <summary>No intervention (control baseline).</summary>
        None,

        /// <summary>Bubble curtain: rising bubbles keep jellyfish and surface algae off the pens.</summary>
        BubbleCurtain,

        /// <summary>Deep feeding: feed below the bloom layer so fish avoid the surface.</summary>
        DeepFeeding,

        /// <summary>Pumping: draw cooler, oxygenated water from depth into the pens.</summary>
        Pumping,

        /// <summary>O2 diffuser: directly oxygenate the water column through a crash.</summary>
        O2Diffuser
    }

    /// <summary>
    /// Result of evaluating a mitigation: how much of the expected mortality it
    /// removes and what it costs (NOK) for the incident.
    /// </summary>
    /// <param name="MortalityReductionFraction">Fraction of expected mortality removed, in [0,1].</param>
    /// <param name="CostNok">Incident cost of deploying the mitigation in NOK.</param>
    public readonly record struct MitigationOutcome(
        double MortalityReductionFraction,
        decimal CostNok);

    /// <summary>
    /// Pure, deterministic effectiveness model for bloom mitigations.
    ///
    /// <para>
    /// Effectiveness grows linearly with bloom severity <c>s ∈ [0,1]</c>: a stronger
    /// bloom gives the intervention more mortality to remove. Cost is a fixed
    /// deployment cost plus a severity-proportional operating cost. Both are
    /// monotonic non-decreasing in severity, and stronger interventions cost more
    /// at any given severity.
    /// </para>
    ///
    /// <para>
    /// Effectiveness anchors from the existing sea-site model: a bubble curtain
    /// cuts jellyfish impact to ≈23% (≈77% reduction) and an O2 diffuser cuts
    /// hypoxia to ≈30% (≈70% reduction).
    /// </para>
    /// </summary>
    public static class BloomMitigation
    {
        /// <summary>
        /// Mortality-reduction fraction and incident cost for a mitigation at a
        /// given bloom severity (severity clamped to [0,1]).
        /// </summary>
        public static MitigationOutcome MitigationEffectiveness(MitigationKind kind, double severity01)
        {
            var severity = Math.Clamp(severity01, 0.0, 1.0);
            var reduction = MaxReduction(kind) * severity;
            var cost = FixedCostNok(kind) + OpexPerSeverityNok(kind) * (decimal)severity;
            return new MitigationOutcome(reduction, cost);
        }

        /// <summary>
        /// Severity- and target-aware effectiveness. The mitigation is fully
        /// effective against the bloom kind it is designed for (see
        /// <see cref="BestFor"/>), half-effective against other bloom kinds, and
        /// confers no benefit when the site is unprotected (<see cref="MitigationKind.None"/>).
        /// </summary>
        public static MitigationOutcome MitigationEffectiveness(
            MitigationKind kind, double severity01, string? bloomKind)
        {
            var baseOutcome = MitigationEffectiveness(kind, severity01);
            var match = MatchMultiplier(kind, bloomKind);
            return baseOutcome with { MortalityReductionFraction = baseOutcome.MortalityReductionFraction * match };
        }

        /// <summary>Best mitigation for a given bloom/incident kind (case-insensitive).</summary>
        public static MitigationKind BestFor(string? bloomKind)
        {
            var key = (bloomKind ?? "").Trim().ToLowerInvariant();
            return key switch
            {
                "jellyfish" or "jelly" or "manet" => MitigationKind.BubbleCurtain,
                "algae" or "algal" or "algalbloom" or "algal_bloom" or "bloom"
                    or "plankton" or "hypoxia" or "o2" or "oxygen" or "lowoxygen"
                    => MitigationKind.O2Diffuser,
                // Storms/escapes and disease culls are not addressed by bloom kit.
                "storm" or "escape" or "isa" or "disease" or "ulcer"
                    or "winterulcer" or "winter_ulcer" => MitigationKind.None,
                _ => MitigationKind.None
            };
        }

        /// <summary>
        /// Multiplier applied to a mitigation's effectiveness against a given bloom kind.
        /// </summary>
        public static double MatchMultiplier(MitigationKind kind, string? bloomKind)
        {
            if (kind == MitigationKind.None) return 0.0;
            var best = BestFor(bloomKind);
            if (best == kind) return 1.0;
            // A mismatched intervention still helps somewhat but is not the right tool.
            return 0.5;
        }

        /// <summary>Whether the mitigation is already installed on the site (flag-backed kinds only).</summary>
        public static bool IsInstalled(MitigationKind kind, SeaSite site)
        {
            if (site is null) return false;
            return kind switch
            {
                MitigationKind.BubbleCurtain => site.HasBubbleCurtain,
                MitigationKind.O2Diffuser => site.HasO2Diffuser,
                _ => false
            };
        }

        /// <summary>Peak mortality reduction at full severity (s = 1).</summary>
        public static double MaxReduction(MitigationKind kind) => kind switch
        {
            MitigationKind.None => 0.00,
            MitigationKind.DeepFeeding => 0.30,
            MitigationKind.Pumping => 0.45,
            MitigationKind.O2Diffuser => 0.70,
            MitigationKind.BubbleCurtain => 0.77,
            _ => 0.0
        };

        /// <summary>Fixed deployment cost (NOK), independent of severity.</summary>
        public static decimal FixedCostNok(MitigationKind kind) => kind switch
        {
            MitigationKind.None => 0m,
            MitigationKind.DeepFeeding => 25_000m,
            MitigationKind.Pumping => 120_000m,
            MitigationKind.O2Diffuser => 200_000m,
            MitigationKind.BubbleCurtain => 250_000m,
            _ => 0m
        };

        /// <summary>Severity-proportional operating cost (NOK at s = 1).</summary>
        public static decimal OpexPerSeverityNok(MitigationKind kind) => kind switch
        {
            MitigationKind.None => 0m,
            MitigationKind.DeepFeeding => 60_000m,
            MitigationKind.Pumping => 150_000m,
            MitigationKind.O2Diffuser => 180_000m,
            MitigationKind.BubbleCurtain => 200_000m,
            _ => 0m
        };
    }
}
