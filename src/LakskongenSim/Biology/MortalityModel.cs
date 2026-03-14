using System;
using LakskongenSim.Data;

namespace LakskongenSim.Biology
{
    /// <summary>
    /// Mortality model applying daily mortality rates.
    /// Uses biologically-accurate 7-stage salmon lifecycle.
    /// </summary>
    public class MortalityModel
    {
        /// <summary>Apply mortality over days, return surviving count (rounded to int).</summary>
        public int ApplyMortality(int count, int days, LifeStage stage)
        {
            if (count <= 0 || days <= 0) return count;
            double rate = LakskongenSim.Core.GameConstants.BaselineMortalityPerDay;
            // Stage modifiers based on real salmon mortality patterns
            switch (stage)
            {
                case LifeStage.Egg: rate *= 5.0; break;       // Highest mortality in egg stage
                case LifeStage.Alevin: rate *= 4.0; break;    // Vulnerable yolk sac stage
                case LifeStage.Fry: rate *= 3.0; break;       // First feeding critical period
                case LifeStage.Parr: rate *= 2.0; break;      // Freshwater predation/disease
                case LifeStage.Smolt: rate *= 2.5; break;     // Smoltification stress
                case LifeStage.Seawater: rate *= 1.2; break;  // Sea lice, disease exposure
                case LifeStage.Harvest: rate *= 1.0; break;   // Stable if well-managed
            }
            // survivors = count * (1-rate)^days
            System.Diagnostics.Debug.WriteLine($"[MortalityModel] Applying mortality: Count={count}, Days={days}, Stage={stage}, Rate={rate:F6}");
            var survivors = count * Math.Pow(1.0 - rate, days);
            System.Diagnostics.Debug.WriteLine($"[MortalityModel] Survivors after mortality: {survivors:F0}");
            return Math.Max(0, (int)Math.Round(survivors));
        }
    }
}