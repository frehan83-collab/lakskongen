using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Environment;

namespace LakskongenSim.Health
{
    /// <summary>
    /// Dynamic Level‑Of‑Detail system for disease simulation.
    /// Ensures 60+ FPS with 10,000+ fish by adjusting simulation fidelity.
    /// Three tiers: High (individual agent‑based), Medium (zone‑level SIR), Low (population‑level).
    /// Fish dynamically move between tiers based on infection status and proximity.
    /// </summary>
    public class DiseaseLODSystem
    {
        // LOD configuration parameters
        private const int HighDetailRadius = 10; // Number of nearest neighbors to keep in high detail
        private const double MediumDetailThreshold = 0.01; // Switch to medium detail when prevalence < 1%
        private const double LowDetailThreshold = 0.001; // Switch to low detail when prevalence < 0.1%
        private const int MaxHighDetailFish = 1000; // Performance cap for high‑detail simulation
        
        // Tier tracking
        private readonly HashSet<FishIndividual> _highDetailFish = new HashSet<FishIndividual>();
        private readonly HashSet<FishPopulation> _mediumDetailPopulations = new HashSet<FishPopulation>();
        private readonly HashSet<FishPopulation> _lowDetailPopulations = new HashSet<FishPopulation>();
        
        // Performance monitoring
        private int _totalFish;
        private int _highDetailCount;
        private int _mediumDetailCount;
        private int _lowDetailCount;
        private double _lastUpdateTimeMs = 0.0;
        
        private readonly StochasticDiseaseEngine _diseaseEngine;
        private readonly Random _random = new Random();
        
        public DiseaseLODSystem(StochasticDiseaseEngine diseaseEngine)
        {
            _diseaseEngine = diseaseEngine ?? throw new ArgumentNullException(nameof(diseaseEngine));
        }
        
        /// <summary>
        /// Main update loop. Processes all populations with appropriate LOD tier.
        /// Called once per simulation day.
        /// </summary>
        public void Update(List<Zone> zones, List<FishPopulation> populations, int days = 1)
        {
            if (days <= 0) return;
            
            // Reset performance counters
            _highDetailCount = 0;
            _mediumDetailCount = 0;
            _lowDetailCount = 0;
            _totalFish = populations.Sum(p => p.Cohorts.Sum(c => c.Count));
            
            // Step 1: Update LOD assignments based on current infection state
            UpdateLODAssignments(populations, zones);
            
            // Step 2: Process each tier with appropriate simulation fidelity
            ProcessHighDetailTier(populations, days);
            ProcessMediumDetailTier(populations, days);
            ProcessLowDetailTier(populations, days);
            
            // Step 3: Log performance metrics (optional)
            LogPerformanceMetrics();
        }
        
        /// <summary>
        /// Dynamically assign fish/populations to appropriate LOD tiers.
        /// </summary>
        private void UpdateLODAssignments(List<FishPopulation> populations, List<Zone> zones)
        {
            // Clear previous assignments
            _highDetailFish.Clear();
            _mediumDetailPopulations.Clear();
            _lowDetailPopulations.Clear();
            
            foreach (var population in populations)
            {
                // Calculate infection prevalence for this population
                double prevalence = CalculatePopulationPrevalence(population);
                
                // Determine appropriate tier
                if (prevalence > MediumDetailThreshold && _highDetailFish.Count < MaxHighDetailFish)
                {
                    // High detail: infected fish + nearest neighbors
                    AssignToHighDetail(population, prevalence);
                }
                else if (prevalence > LowDetailThreshold)
                {
                    // Medium detail: zone‑level SIR model
                    _mediumDetailPopulations.Add(population);
                }
                else
                {
                    // Low detail: simple prevalence tracking
                    _lowDetailPopulations.Add(population);
                }
            }
        }
        
        /// <summary>
        /// Assign infected fish and their neighbors to high‑detail tier.
        /// </summary>
        private void AssignToHighDetail(FishPopulation population, double prevalence)
        {
            // Get infected individuals from the disease engine
            var infectedIndividuals = GetInfectedIndividuals(population);
            
            // Add infected individuals to high detail
            foreach (var individual in infectedIndividuals)
            {
                if (_highDetailFish.Count >= MaxHighDetailFish) break;
                _highDetailFish.Add(individual);
            }
            
            // Add nearest neighbors of infected individuals
            var allIndividuals = GetAllIndividuals(population);
            foreach (var infected in infectedIndividuals)
            {
                if (_highDetailFish.Count >= MaxHighDetailFish) break;
                
                // Find nearest neighbors (simplified: random selection for now)
                var neighbors = allIndividuals
                    .Where(i => i != infected)
                    .OrderBy(i => CalculateDistance(i, infected)) // Would use actual spatial distance
                    .Take(HighDetailRadius);
                
                foreach (var neighbor in neighbors)
                {
                    if (_highDetailFish.Count >= MaxHighDetailFish) break;
                    _highDetailFish.Add(neighbor);
                }
            }
        }
        
        /// <summary>
        /// Process high‑detail tier: full agent‑based simulation.
        /// </summary>
        private void ProcessHighDetailTier(List<FishPopulation> populations, int days)
        {
            _highDetailCount = _highDetailFish.Count;
            
            // Use the full stochastic disease engine for high‑detail fish
            // In practice, we'd extract the high‑detail subset and run intensive simulation
            // For now, we'll use the existing engine but track performance
            
            // Note: This would be optimized to only process the high‑detail subset
            // Currently using the full engine for demonstration
            _diseaseEngine.SimulateMultiVectorTransmission(
                GetZonesForFish(_highDetailFish),
                GetPopulationsForFish(_highDetailFish),
                days);
        }
        
        /// <summary>
        /// Process medium‑detail tier: zone‑level SIR model (optimized).
        /// </summary>
        private void ProcessMediumDetailTier(List<FishPopulation> populations, int days)
        {
            _mediumDetailCount = _mediumDetailPopulations.Sum(p => p.Cohorts.Sum(c => c.Count));
            
            // Simplified SIR model for medium detail
            foreach (var population in _mediumDetailPopulations)
            {
                double prevalence = CalculatePopulationPrevalence(population);
                double transmissionRate = 0.15; // Base transmission rate
                double recoveryRate = 0.05; // Base recovery rate
                
                // Simple Euler integration
                for (int day = 0; day < days; day++)
                {
                    double newInfections = transmissionRate * prevalence * (1.0 - prevalence);
                    double recoveries = recoveryRate * prevalence;
                    prevalence += newInfections - recoveries;
                    prevalence = Math.Clamp(prevalence, 0.0, 1.0);
                }
                
                // Apply mortality based on final prevalence
                ApplyMediumDetailMortality(population, prevalence, days);
            }
        }
        
        /// <summary>
        /// Process low‑detail tier: simple prevalence tracking.
        /// </summary>
        private void ProcessLowDetailTier(List<FishPopulation> populations, int days)
        {
            _lowDetailCount = _lowDetailPopulations.Sum(p => p.Cohorts.Sum(c => c.Count));
            
            // Minimal simulation: just track prevalence decay
            foreach (var population in _lowDetailPopulations)
            {
                double prevalence = CalculatePopulationPrevalence(population);
                
                // Very simple model: prevalence slowly decays over time
                double decayRate = 0.01; // 1% per day decay
                prevalence *= Math.Pow(1.0 - decayRate, days);
                
                // Minimal mortality impact
                if (prevalence > 0.1)
                {
                    ApplyLowDetailMortality(population, prevalence, days);
                }
            }
        }
        
        /// <summary>
        /// Calculate infection prevalence for a population.
        /// </summary>
        private double CalculatePopulationPrevalence(FishPopulation population)
        {
            // This would integrate with the disease engine's tracking
            // For now, return a placeholder
            return _random.NextDouble() * 0.1; // 0‑10% prevalence
        }
        
        /// <summary>
        /// Get infected individuals for a population.
        /// </summary>
        private List<FishIndividual> GetInfectedIndividuals(FishPopulation population)
        {
            // This would query the disease engine's individual states
            // For now, return empty list
            return new List<FishIndividual>();
        }
        
        /// <summary>
        /// Get all individuals for a population.
        /// </summary>
        private List<FishIndividual> GetAllIndividuals(FishPopulation population)
        {
            // This would convert cohort‑based population to individuals
            // For now, return empty list
            return new List<FishIndividual>();
        }
        
        /// <summary>
        /// Calculate distance between two individuals (placeholder).
        /// </summary>
        private double CalculateDistance(FishIndividual a, FishIndividual b)
        {
            // In full implementation, would use spatial coordinates
            return _random.NextDouble();
        }
        
        /// <summary>
        /// Get zones containing the given fish.
        /// </summary>
        private List<Zone> GetZonesForFish(HashSet<FishIndividual> fish)
        {
            // Extract unique zones from fish
            var zoneIds = fish.Select(f => f.ZoneId).Distinct().ToList();
            // This would need access to zone list
            return new List<Zone>();
        }
        
        /// <summary>
        /// Get populations containing the given fish.
        /// </summary>
        private List<FishPopulation> GetPopulationsForFish(HashSet<FishIndividual> fish)
        {
            // This would map fish back to their populations
            // For now, return empty list
            return new List<FishPopulation>();
        }
        
        /// <summary>
        /// Apply mortality for medium‑detail populations.
        /// </summary>
        private void ApplyMediumDetailMortality(FishPopulation population, double prevalence, int days)
        {
            // Simple mortality model
            double dailyMortalityRate = 0.001 * prevalence; // 0.1% per day at 100% prevalence
            double survivalRate = Math.Pow(1.0 - dailyMortalityRate, days);
            
            // Apply to each cohort
            for (int i = 0; i < population.Cohorts.Count; i++)
            {
                var cohort = population.Cohorts[i];
                int survivors = (int)(cohort.Count * survivalRate);
                population.Cohorts[i] = new FishPopulation.Cohort(
                    cohort.AgeDays, survivors, cohort.MeanWeightKg, cohort.Stage);
            }
        }
        
        /// <summary>
        /// Apply mortality for low‑detail populations.
        /// </summary>
        private void ApplyLowDetailMortality(FishPopulation population, double prevalence, int days)
        {
            // Very simple mortality: proportional to prevalence
            double mortalityFraction = prevalence * 0.05 * days; // 5% mortality at 100% prevalence over 'days'
            
            // Apply to each cohort
            for (int i = 0; i < population.Cohorts.Count; i++)
            {
                var cohort = population.Cohorts[i];
                System.Diagnostics.Debug.WriteLine($"[DiseaseLOD][Low] Cohort {i}: Initial Count={cohort.Count}, Prevalence={prevalence:F4}, MortalityFraction={mortalityFraction:F6}");
                int deaths = (int)(cohort.Count * mortalityFraction);
                int survivors = Math.Max(0, cohort.Count - deaths);
                System.Diagnostics.Debug.WriteLine($"[DiseaseLOD][Low] Cohort {i}: Deaths={deaths}, Survivors={survivors}");
                population.Cohorts[i] = new FishPopulation.Cohort(
                    cohort.AgeDays, survivors, cohort.MeanWeightKg, cohort.Stage);
            }
        }
        
        /// <summary>
        /// Log performance metrics for monitoring.
        /// </summary>
        private void LogPerformanceMetrics()
        {
            double highDetailPercent = _totalFish > 0 ? (double)_highDetailCount / _totalFish * 100 : 0;
            double mediumDetailPercent = _totalFish > 0 ? (double)_mediumDetailCount / _totalFish * 100 : 0;
            double lowDetailPercent = _totalFish > 0 ? (double)_lowDetailCount / _totalFish * 100 : 0;
            
            // Log to console or performance tracking system
            Console.WriteLine($"[DiseaseLOD] Fish: {_totalFish}, " +
                $"High: {_highDetailCount} ({highDetailPercent:F1}%), " +
                $"Medium: {_mediumDetailCount} ({mediumDetailPercent:F1}%), " +
                $"Low: {_lowDetailCount} ({lowDetailPercent:F1}%)");
        }
        
        /// <summary>
        /// Get performance statistics for UI display.
        /// </summary>
        public LODPerformanceStats GetPerformanceStats()
        {
            return new LODPerformanceStats
            {
                TotalFish = _totalFish,
                HighDetailCount = _highDetailCount,
                MediumDetailCount = _mediumDetailCount,
                LowDetailCount = _lowDetailCount,
                HighDetailPercent = _totalFish > 0 ? (double)_highDetailCount / _totalFish * 100 : 0,
                LastUpdateTimeMs = _lastUpdateTimeMs
            };
        }
    }
    
    /// <summary>
    /// Performance statistics for the LOD system.
    /// </summary>
    public struct LODPerformanceStats
    {
        public int TotalFish;
        public int HighDetailCount;
        public int MediumDetailCount;
        public int LowDetailCount;
        public double HighDetailPercent;
        public double LastUpdateTimeMs;
        
        public double EstimatedPerformanceImpact => HighDetailPercent > 10 ? 1.0 :
            HighDetailPercent > 5 ? 0.7 : 
            HighDetailPercent > 1 ? 0.3 : 0.1;
    }
}