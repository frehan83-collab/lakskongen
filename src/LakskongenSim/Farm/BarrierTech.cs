using System;
using System.Collections.Generic;

namespace LakskongenSim.Farm
{
    /// <summary>
    /// Permeable-barrier technologies (P8). Additive to
    /// <see cref="ContainmentTech"/>: a barrier is a retrofit liner fitted to an
    /// existing open/skirt pen rather than a closed-containment rung, so it gets
    /// its own model, cost table and regulatory story.
    ///
    /// <para>
    /// <see cref="BarrierKind.Smoltscreen"/> models Salar Solutions' Smoltscreen:
    /// a permeable retrofit liner (150 µm + 200 µm mesh) that blocks all sea-lice
    /// stages and jellyfish while letting water through. Tank tests show no pumps
    /// are needed (full water exchange in under 3 minutes at 0.7 m/s tidal flow),
    /// cutting capex by more than half versus a closed-containment retrofit.
    /// </para>
    /// </summary>
    public enum BarrierKind
    {
        /// <summary>No barrier installed (control baseline).</summary>
        None = 0,

        /// <summary>Smoltscreen permeable liner (150 µm + 200 µm mesh).</summary>
        Smoltscreen = 1
    }

    /// <summary>
    /// Static specification for a barrier technology.
    /// </summary>
    /// <param name="Kind">The barrier technology this spec describes.</param>
    /// <param name="DisplayName">Localisable display name.</param>
    /// <param name="LiceMeshUm">Mesh aperture used for lice blocking (µm).</param>
    /// <param name="JellyMeshUm">Mesh aperture used for jellyfish blocking (µm).</param>
    /// <param name="CapexNok">One-time retrofit capex (NOK). Smoltscreen ≈ half a
    /// closed-containment retrofit (SemiClosed capex is 12,000,000 NOK).</param>
    /// <param name="WeeklyOpexNok">Standing cleaning/service opex (NOK/week).</param>
    /// <param name="CleaningIntervalDays">Days a barrier stays at full efficiency
    /// without cleaning before biofouling starts to degrade blocking.</param>
    /// <param name="MinCleaningFactor">Floor that cleaning decay can fall to.</param>
    /// <param name="Notes">Human-readable provenance / assumptions.</param>
    public sealed record BarrierSpec(
        BarrierKind Kind,
        string DisplayName,
        int LiceMeshUm,
        int JellyMeshUm,
        decimal CapexNok,
        decimal WeeklyOpexNok,
        int CleaningIntervalDays,
        double MinCleaningFactor,
        string Notes);

    /// <summary>
    /// Pure, deterministic effectiveness model for permeable barriers.
    ///
    /// <para>
    /// Blocking is the product of a <b>flow response</b> (full in the ~0.2–1.0 m/s
    /// tidal band, degraded below and above) and a <b>mesh cap</b> (finer mesh
    /// blocks more). No Godot or simulation dependencies; safe for unit tests and
    /// headless tools.
    /// </para>
    /// </summary>
    public static class BarrierTech
    {
        // ── Flow envelope (m/s) ────────────────────────────────────────────────
        /// <summary>Below this flow the exchange is too weak for the liner to work.</summary>
        public const double FlowFloorMs = 0.05;

        /// <summary>Lower edge of the full-blocking plateau (m/s).</summary>
        public const double FlowOptimalMinMs = 0.20;

        /// <summary>Upper edge of the full-blocking plateau (m/s).</summary>
        public const double FlowOptimalMaxMs = 1.00;

        /// <summary>Above this flow the liner mechanically degrades and drops to the high-flow floor.</summary>
        public const double FlowTearMs = 1.60;

        /// <summary>Fraction of peak blocking retained above <see cref="FlowTearMs"/>.</summary>
        public const double HighFlowFloor = 0.55;

        /// <summary>Sensible default tidal flow (m/s) when a site has no zone data.</summary>
        public const double DefaultFlowMs = 0.30;

        private const decimal SemiClosedRetrofitCapexNok = 12_000_000m;

        /// <summary>Registry of every modelled barrier technology.</summary>
        public static readonly IReadOnlyDictionary<BarrierKind, BarrierSpec> Registry =
            new Dictionary<BarrierKind, BarrierSpec>
            {
                [BarrierKind.None] = new(
                    Kind: BarrierKind.None,
                    DisplayName: "Ingen barriere",
                    LiceMeshUm: 0,
                    JellyMeshUm: 0,
                    CapexNok: 0m,
                    WeeklyOpexNok: 0m,
                    CleaningIntervalDays: 0,
                    MinCleaningFactor: 1.0,
                    Notes: "Kontrollbaseline."),
                [BarrierKind.Smoltscreen] = new(
                    Kind: BarrierKind.Smoltscreen,
                    DisplayName: "Permeabel lusebarriere",
                    LiceMeshUm: 150,
                    JellyMeshUm: 200,
                    CapexNok: 6_000_000m,
                    WeeklyOpexNok: 40_000m,
                    CleaningIntervalDays: 30,
                    MinCleaningFactor: 0.40,
                    Notes: "Permeabel returliner (150 µm + 200 µm). Full vanngjennomstrømming "
                        + "på under 3 min ved 0,7 m/s tidevannsstrøm — ingen pumper. Capex ≈ "
                        + "halvparten av lukket-løsning (SemiClosed 12 000 000 NOK). "
                        + "Ikke kvalifisert for miljøfleksibilitet: regulatorer leser «tett» som impermeabelt.")
            };

        /// <summary>Specification for a barrier kind; unknown kinds fall back to <see cref="BarrierKind.None"/>.</summary>
        public static BarrierSpec SpecFor(BarrierKind kind) =>
            Registry.TryGetValue(kind, out var spec) ? spec : Registry[BarrierKind.None];

        /// <summary>
        /// Flow response in [0,1]: 0 below <see cref="FlowFloorMs"/>, ramping to 1.0
        /// at <see cref="FlowOptimalMinMs"/>, held at 1.0 through
        /// <see cref="FlowOptimalMaxMs"/>, then decaying to <see cref="HighFlowFloor"/>
        /// by <see cref="FlowTearMs"/>.
        /// </summary>
        public static double FlowResponse(double flowMs)
        {
            if (flowMs <= FlowFloorMs) return 0.0;
            if (flowMs < FlowOptimalMinMs)
                return (flowMs - FlowFloorMs) / (FlowOptimalMinMs - FlowFloorMs);
            if (flowMs <= FlowOptimalMaxMs) return 1.0;
            if (flowMs < FlowTearMs)
                return 1.0 - (1.0 - HighFlowFloor) * (flowMs - FlowOptimalMaxMs) / (FlowTearMs - FlowOptimalMaxMs);
            return HighFlowFloor;
        }

        /// <summary>
        /// Mesh cap for sea-lice: finer aperture blocks more. Anchored so a 150 µm
        /// liner blocks ~97% of all lice stages at full flow.
        /// </summary>
        public static double MeshLiceCap(int meshUm) =>
            meshUm <= 0 ? 0.0 : Math.Clamp(1.0 - 0.0002 * meshUm, 0.85, 0.99);

        /// <summary>
        /// Mesh cap for jellyfish: jellyfish are larger than lice, so a given
        /// aperture blocks slightly more. Anchored so 200 µm blocks ~95% at full flow.
        /// </summary>
        public static double MeshJellyCap(int meshUm) =>
            meshUm <= 0 ? 0.0 : Math.Clamp(1.0 - 0.00025 * meshUm, 0.80, 0.98);

        /// <summary>
        /// Fraction of new lice settlement blocked by a mesh at a given flow, in
        /// [0,1]. Monotonic non-decreasing in flow up to the full-blocking plateau.
        /// </summary>
        public static double LiceBlockingFraction(double flowMs, int meshUm) =>
            FlowResponse(flowMs) * MeshLiceCap(meshUm);

        /// <summary>
        /// Fraction of jellyfish blocked by a mesh at a given flow, in [0,1].
        /// </summary>
        public static double JellyfishBlockingFraction(double flowMs, int meshUm) =>
            FlowResponse(flowMs) * MeshJellyCap(meshUm);

        /// <summary>
        /// Cleaning decay factor in [<see cref="BarrierSpec.MinCleaningFactor"/>,1].
        /// 1.0 while within the cleaning interval, then decays 1%/day overdue.
        /// </summary>
        public static double CleaningDecayFactor(BarrierKind kind, int daysSinceCleaning)
        {
            if (kind == BarrierKind.None) return 1.0;
            var spec = SpecFor(kind);
            int overdue = daysSinceCleaning - spec.CleaningIntervalDays;
            if (overdue <= 0) return 1.0;
            return Math.Max(spec.MinCleaningFactor, 1.0 - 0.01 * overdue);
        }

        /// <summary>
        /// Flow- and cleanliness-aware lice-settlement blocking for a barrier kind.
        /// <see cref="BarrierKind.None"/> always returns 0.
        /// </summary>
        public static double EffectiveLiceBlocking(BarrierKind kind, double flowMs, int daysSinceCleaning = 0)
        {
            if (kind == BarrierKind.None) return 0.0;
            var spec = SpecFor(kind);
            return LiceBlockingFraction(flowMs, spec.LiceMeshUm) * CleaningDecayFactor(kind, daysSinceCleaning);
        }

        /// <summary>
        /// Flow- and cleanliness-aware jellyfish blocking for a barrier kind.
        /// <see cref="BarrierKind.None"/> always returns 0.
        /// </summary>
        public static double EffectiveJellyfishBlocking(BarrierKind kind, double flowMs, int daysSinceCleaning = 0)
        {
            if (kind == BarrierKind.None) return 0.0;
            var spec = SpecFor(kind);
            return JellyfishBlockingFraction(flowMs, spec.JellyMeshUm) * CleaningDecayFactor(kind, daysSinceCleaning);
        }

        /// <summary>
        /// Capex of a barrier as a fraction of the equivalent closed-containment
        /// retrofit (SemiClosed). Smoltscreen ≈ 0.5 — the headline cost advantage.
        /// </summary>
        public static decimal CapexFractionOfClosedContainmentRetrofit(BarrierKind kind)
        {
            if (kind == BarrierKind.None) return 0m;
            return SpecFor(kind).CapexNok / SemiClosedRetrofitCapexNok;
        }

        /// <summary>
        /// Whether a barrier qualifies for the red-zone "environmental
        /// flexibility" capacity-recovery scheme. Always false: regulators read
        /// "tett" (tight/closed) as impermeable, and a permeable liner is not.
        /// </summary>
        public static bool IsEligibleForEnvironmentalFlexibility(BarrierKind kind) => false;

        /// <summary>
        /// Human-readable rationale for the environmental-flexibility decision.
        /// </summary>
        public static string EnvironmentalFlexibilityRationale(BarrierKind kind) =>
            kind == BarrierKind.Smoltscreen
                ? "Lusebarrieren er permeabel — vann passerer (full utskifting <3 min ved 0,7 m/s). "
                    + "Miljøfleksibilitetsordningen krever «tett» anlegg, som regulatorer tolker som "
                    + "impermeabelt. Permeabel returliner kvalifiserer derfor ikke — ennå."
                : "Ingen permeabel barriere installert.";
    }
}