using System;

namespace LakskongenSim.Biology
{
    /// <summary>
    /// Simplified growth model that adjusts mean weight.
    /// </summary>
    public class GrowthModel
    {
        /// <summary>Apply growth over a number of days for a cohort.</summary>
        public double ApplyGrowth(double currentWeightKg, int days, LakskongenSim.Core.LifeStage stage)
        {
            if (days <= 0) return currentWeightKg;
            // Very simple exponential-ish growth based on baseline rate and stage modifiers.
            double rate = LakskongenSim.Core.GameConstants.BaselineGrowthPerDay;
            switch (stage)
            {
                case LakskongenSim.Core.LifeStage.EyedEgg: rate *= 0.2; break;
                case LakskongenSim.Core.LifeStage.Smolt: rate *= 0.8; break;
                case LakskongenSim.Core.LifeStage.Juvenile: rate *= 1.0; break;
                case LakskongenSim.Core.LifeStage.Adult: rate *= 1.2; break;
                case LakskongenSim.Core.LifeStage.Harvestable: rate *= 0.9; break;
            }
            // approximate compound growth
            var newWeight = currentWeightKg * Math.Pow(1.0 + rate, days);
            return newWeight;
        }
    }
}