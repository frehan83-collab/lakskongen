using System;
using System.Collections.Generic;
using LakskongenSim.Data;

namespace LakskongenSim.Biology
{
    /// <summary>
    /// Mortality model applying daily mortality rates.
    /// Uses biologically-accurate 7-stage salmon lifecycle.
    /// Disease-aware: integrates disease mortality to prevent double-application.
    /// </summary>
    public class MortalityModel
    {
        // Disease mortality rates per population (injected by IntegratedDiseaseSystem)
        private readonly Dictionary<string, double> _diseaseRates = new Dictionary<string, double>();
        private int _callCount = 0; // For debugging

        /// <summary>
        /// Apply mortality over days, return surviving count (rounded to int).
        /// Combines baseline, stage, and disease mortality in a single application.
        /// </summary>
        public int ApplyMortality(int count, int days, LifeStage stage, string populationId = "")
        {
            if (count <= 0 || days <= 0) return count;
            
            double baseRate = LakskongenSim.Core.GameConstants.BaselineMortalityPerDay;
            
            // Stage modifiers based on real salmon mortality patterns
            double stageMultiplier = 1.0;
            switch (stage)
            {
                case LifeStage.Egg: stageMultiplier = 5.0; break;       // Highest mortality in egg stage
                case LifeStage.Alevin: stageMultiplier = 4.0; break;    // Vulnerable yolk sac stage
                case LifeStage.Fry: stageMultiplier = 3.0; break;       // First feeding critical period
                case LifeStage.Parr: stageMultiplier = 2.0; break;      // Freshwater predation/disease
                case LifeStage.Smolt: stageMultiplier = 2.5; break;     // Smoltification stress
                case LifeStage.Seawater: stageMultiplier = 1.2; break;  // Sea lice, disease exposure
                case LifeStage.Harvest: stageMultiplier = 1.0; break;   // Stable if well-managed
            }
            
            // Get disease mortality rate (if any) for this population
            double diseaseRate = 0.0;
            if (!string.IsNullOrEmpty(populationId) && _diseaseRates.TryGetValue(populationId, out double rate))
            {
                diseaseRate = rate;
            }
            
            // Combine all mortality sources: baseline * stage_multiplier + disease
            double totalRate = (baseRate * stageMultiplier) + diseaseRate;
            
            // Clamp to prevent negative or >100% mortality
            totalRate = Math.Clamp(totalRate, 0.0, 0.95); // Max 95% mortality per day
            
            // survivors = count * (1-rate)^days
            var survivors = count * Math.Pow(1.0 - totalRate, days);
            
            // Log to console for debugging (first 10 calls)
            if (_callCount < 10)
            {
                Console.WriteLine($"[MortalityModel] Call #{_callCount}: BaseRate={baseRate:F7}, Stage={stage}, StageMult={stageMultiplier:F2}, TotalRate={totalRate:F7}, Count={count}, Survivors={survivors:F0}");
                _callCount++;
            }
            
            return Math.Max(0, (int)Math.Round(survivors));
        }

        /// <summary>
        /// Set disease mortality rate for a population.
        /// Called by IntegratedDiseaseSystem before FishPopulation.AdvanceDays().
        /// </summary>
        public void SetDiseaseMortalityRate(string populationId, double dailyRate)
        {
            if (string.IsNullOrEmpty(populationId)) return;
            double clampedRate = Math.Clamp(dailyRate, 0.0, 0.5); // Cap at 50% per day
            System.Diagnostics.Debug.WriteLine($"[MortalityModel] SET DISEASE RATE: PopId={populationId}, Rate={clampedRate:F6}");
            _diseaseRates[populationId] = clampedRate;
        }

        /// <summary>
        /// Clear disease mortality rates (call at start of each tick).
        /// </summary>
        public void ClearDiseaseMortalityRates()
        {
            _diseaseRates.Clear();
        }
    }
}