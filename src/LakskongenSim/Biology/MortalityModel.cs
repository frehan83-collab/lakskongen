using System;

namespace LakskongenSim.Biology
{
    /// <summary>
    /// Mortality model applying daily mortality rates.
    /// </summary>
    public class MortalityModel
    {
        /// <summary>Apply mortality over days, return surviving count (rounded to int).</summary>
        public int ApplyMortality(int count, int days, LakskongenSim.Core.LifeStage stage)
        {
            if (count <= 0 || days <= 0) return count;
            double rate = LakskongenSim.Core.GameConstants.BaselineMortalityPerDay;
            // Stage modifiers
            switch (stage)
            {
                case LakskongenSim.Core.LifeStage.EyedEgg: rate *= 5.0; break;
                case LakskongenSim.Core.LifeStage.Smolt: rate *= 2.0; break;
                case LakskongenSim.Core.LifeStage.Juvenile: rate *= 1.0; break;
                case LakskongenSim.Core.LifeStage.Adult: rate *= 1.2; break;
                case LakskongenSim.Core.LifeStage.Harvestable: rate *= 1.1; break;
            }
            // survivors = count * (1-rate)^days
            var survivors = count * Math.Pow(1.0 - rate, days);
            return Math.Max(0, (int)Math.Round(survivors));
        }
    }
}