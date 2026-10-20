using System;
using System.Collections.Generic;
using System.Linq;

namespace LakskongenSim.Optimization
{
    /// <summary>
    /// ROC attention budget (Phase 4.2, slim): pure logic, no Godot deps.
    /// Budget 100 + 15 per crew member (cap +45). Watch 25 / Scan 10 / Auto 0.
    /// One hardcoded policy (Wave 2 adds night-shift variants).
    /// </summary>
    public static class RemoteOpsCentre
    {
        public const int BaseBudget = 100;
        public const int PerCrewBonus = 15;
        public const int MaxCrewBonus = 45;

        public static readonly IReadOnlyDictionary<string, int> ModeCost =
            new Dictionary<string, int> { ["watch"] = 25, ["scan"] = 10, ["auto"] = 0 };

        public static int BudgetFor(int crewCount) =>
            BaseBudget + Math.Min(MaxCrewBonus, crewCount * PerCrewBonus);

        public static int CostFor(string? mode) =>
            ModeCost.TryGetValue((mode ?? "").ToLower(), out var c) ? c : 25;

        public static int UsedBy(IEnumerable<Farm.SeaSite> sites) =>
            sites.Sum(s => CostFor(s.WatchMode));

        /// <summary>Escape-response multiplier: late detection lets more fish out.</summary>
        public static double EscapeResponseMult(string? mode) =>
            (mode ?? "").ToLower() switch { "auto" => 1.5, "scan" => 1.25, _ => 1.0 };

        /// <summary>Autopilot feed spill fraction (overfeeding without eyes).</summary>
        public static double FeedSpillFraction(string? mode) =>
            (mode ?? "").ToLower() == "auto" ? 0.08 : 0.0;
    }
}
