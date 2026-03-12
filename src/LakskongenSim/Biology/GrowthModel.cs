using System;
using LakskongenSim.Data;

namespace LakskongenSim.Biology
{
    /// <summary>
    /// Simplified growth model that adjusts mean weight.
    /// Uses biologically-accurate 7-stage salmon lifecycle.
    /// </summary>
    public class GrowthModel
    {
        /// <summary>Apply growth over a number of days for a cohort.</summary>
        public double ApplyGrowth(double currentWeightKg, int days, LifeStage stage)
        {
            if (days <= 0) return currentWeightKg;
            // Very simple exponential-ish growth based on baseline rate and stage modifiers.
            double rate = LakskongenSim.Core.GameConstants.BaselineGrowthPerDay;
            switch (stage)
            {
                case LifeStage.Egg: rate *= 0.1; break;        // Minimal growth in egg stage
                case LifeStage.Alevin: rate *= 0.2; break;     // Yolk sac absorption
                case LifeStage.Fry: rate *= 0.5; break;        // First feeding, rapid early growth
                case LifeStage.Parr: rate *= 0.8; break;       // Freshwater growth phase
                case LifeStage.Smolt: rate *= 0.9; break;      // Transitioning to seawater
                case LifeStage.Seawater: rate *= 1.2; break;   // Peak growth in sea pens
                case LifeStage.Harvest: rate *= 0.8; break;    // Slowing as approaching harvest weight
            }
            // approximate compound growth
            var newWeight = currentWeightKg * Math.Pow(1.0 + rate, days);
            return newWeight;
        }
    }
}