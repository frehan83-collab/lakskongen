using System;
using System.Collections.Generic;

namespace LakskongenSim.Farm
{
    /// <summary>
    /// Containment levels per site (Phase 4.1). Pure data table — no Godot deps.
    /// Levels: 0 OpenNet, 1 Skirt (existing skirt toggle maps here), 2 SemiClosed
    /// (tarpaulin + deep intake), 3 ClosedFloat (post-smolt tank), 4 Submersible,
    /// 5 OffshoreClosed (exposed, moored). Downgrade not allowed.
    /// </summary>
    public enum ContainmentLevel
    {
        OpenNet = 0,
        Skirt = 1,
        SemiClosed = 2,
        ClosedFloat = 3,
        Submersible = 4,
        OffshoreClosed = 5
    }

    /// <summary>Per-level modifiers. Sources: Aquatraz/Hydra/Nautilus/Havliljen figures.</summary>
    public sealed record ContainmentModifiers(
        double LiceExclusion01,   // fraction of settlement blocked
        double OxygenDemandMult,  // O2 consumption multiplier (pumps/aeration load)
        double StormDamageMult,   // storm mortality/integrity damage multiplier
        double EscapeRiskMult,    // escape probability multiplier
        double GrowthMult,        // density-stress growth effect
        decimal CapexNok,         // one-time upgrade cost to THIS level
        decimal WeeklyOpexNok     // standing energy/maintenance cost
    );

    public static class ContainmentTech
    {
        public static readonly IReadOnlyDictionary<ContainmentLevel, ContainmentModifiers> Table =
            new Dictionary<ContainmentLevel, ContainmentModifiers>
            {
                [ContainmentLevel.OpenNet] = new(0.00, 1.00, 1.00, 1.00, 1.00, 0m, 0m),
                [ContainmentLevel.Skirt] = new(0.35, 1.05, 1.10, 0.60, 1.00, 500_000m, 14_000m),
                [ContainmentLevel.SemiClosed] = new(0.85, 1.40, 0.70, 0.20, 0.98, 12_000_000m, 120_000m),
                [ContainmentLevel.ClosedFloat] = new(0.95, 1.60, 0.50, 0.05, 0.95, 28_000_000m, 420_000m),
                [ContainmentLevel.Submersible] = new(0.90, 1.60, 0.30, 0.30, 0.97, 55_000_000m, 540_000m),
                [ContainmentLevel.OffshoreClosed] = new(0.90, 1.10, 0.50, 0.75, 1.05, 120_000_000m, 800_000m),
            };

        public static ContainmentModifiers ModifiersFor(ContainmentLevel level) =>
            Table.TryGetValue(level, out var m) ? m : Table[ContainmentLevel.OpenNet];

        /// <summary>MAB credit factor: closed rungs keep more capacity in red zones.</summary>
        public static double MabCreditFor(ContainmentLevel level, bool isRedZone) =>
            (level, isRedZone) switch
            {
                (ContainmentLevel.ClosedFloat, true) => 1.06,
                (ContainmentLevel.Submersible, true) => 1.06,
                (ContainmentLevel.OffshoreClosed, true) => 1.10,
                (ContainmentLevel.SemiClosed, true) => 1.03,
                _ => 1.0
            };

        /// <summary>
        /// True for containment rungs that seal the farm from the surrounding
        /// water column (SemiClosed and above) and are therefore the technologies
        /// eligible for red-zone "environmental flexibility" capacity recovery.
        /// Additive helper — existing table and methods are unchanged.
        /// </summary>
        public static bool IsClosedContainment(ContainmentLevel level) =>
            level >= ContainmentLevel.SemiClosed;
    }
}
