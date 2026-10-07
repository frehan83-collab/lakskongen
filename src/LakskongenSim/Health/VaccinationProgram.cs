using System;
using System.Linq;
using LakskongenSim.Core;

namespace LakskongenSim.Health
{
    /// <summary>
    /// Ulcer/CMS vaccination (Phase 5.1): October window, per-site coverage,
    /// 12-month protection. Pure logic; ticked/applied by engine + GameBridge.
    /// </summary>
    public static class VaccinationProgram
    {
        public const decimal CostPerFishNok = 3.2m;
        public const int WindowStartDoy = 274; // Oct 1
        public const int WindowEndDoy = 304; // Oct 31
        public const int ProtectionDays = 365;
        public const double HandlingLoss = 0.006; // 0.6% vaccination loss

        public static bool IsWindowOpen(int day) =>
            day % 365 is int doy && doy >= WindowStartDoy && doy <= WindowEndDoy;

        public static int WindowDaysLeft(int day)
        {
            int doy = day % 365;
            return doy > WindowEndDoy ? -1 : WindowEndDoy - Math.Max(doy, WindowStartDoy);
        }

        /// <summary>Fraction of live fish protected right now (0-1).</summary>
        public static double Coverage01(GameState state)
        {
            int total = 0, covered = 0;
            foreach (var pop in state.Populations)
            {
                int n = pop.Cohorts.Sum(c => c.Count);
                total += n;
                if (pop.VaccinatedDay >= 0 && state.Day - pop.VaccinatedDay <= ProtectionDays)
                    covered += n;
            }
            return total > 0 ? (double)covered / total : 0.0;
        }

        /// <summary>Event-cost multiplier for ulcer/CMS outbreaks.</summary>
        public static double OutbreakCostMult(GameState state, bool vetOnStaff) =>
            1.0 - (vetOnStaff ? 0.85 : 0.75) * Coverage01(state);
    }
}
