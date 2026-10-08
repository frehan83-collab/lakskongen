using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Environment;

namespace LakskongenSim.Health
{
    /// <summary>
    /// Integrated disease system combining the full stochastic engine with LOD optimization.
    /// Automatically switches between simulation fidelity levels to maintain 60+ FPS.
    /// Provides unified API for game systems to interact with disease simulation.
    /// </summary>
    public class IntegratedDiseaseSystem
    {
        private readonly DiseaseSystem _baseSystem;
        private readonly StochasticDiseaseEngine _stochasticEngine;
        private readonly DiseaseLODSystem _lodSystem;
        private readonly SeaLiceModel _liceModel;

        // Performance targets
        private const double TargetFPS = 60.0;
        private const double MaxUpdateTimeMs = 16.7; // 60 FPS target
        private const int PerformanceSampleCount = 60; // Track last second of performance

        // SIR tuning for the medium/low fidelity tiers (zone-level model).
        private const double MediumTierTransmissionRate = 0.15;
        private const double MediumTierRecoveryRate = 0.05;
        private const double LowTierDailyDecayRate = 0.01;
        private const double LowTierPrevalenceFloor = 0.1; // below: no mortality injection
        private const double WaterborneKernelRadiusKm = 15.0; // ISA spreads 5-15km via water

        // Disease mortality injected per unit prevalence (fraction of fish per day).
        private const double HighTierMortalityPerPrevalence = 0.002; // 0.2%/day at 100% prevalence
        private const double LowTierMortalityPerPrevalence = 0.0002; // 0.02%/day at 100% prevalence
        
        // Performance monitoring
        private readonly Queue<double> _updateTimes = new Queue<double>();
        private double _averageUpdateTimeMs = 0.0;
        private int _totalUpdates = 0;
        private double _externalDiseasePressureMultiplier = 1.0;
        
        // Current LOD mode (auto-adjusted based on performance)
        private LODMode _currentMode = LODMode.Auto;
        private int _performanceBasedHighDetailLimit = 1000;
        
        // Mortality model reference (injected from SimulationEngine)
        private Biology.MortalityModel? _mortalityModel;
        
        public event Action<LODMode>? OnLODModeChanged;
        public event Action<double>? OnPerformanceUpdate; // FPS update
        
        public IntegratedDiseaseSystem(SeaLiceModel liceModel)
        {
            _liceModel = liceModel ?? throw new ArgumentNullException(nameof(liceModel));
            _baseSystem = new DiseaseSystem(liceModel);
            _stochasticEngine = new StochasticDiseaseEngine(_baseSystem);
            _lodSystem = new DiseaseLODSystem(_stochasticEngine);
        }

        /// <summary>
        /// Inject MortalityModel reference to enable disease rate injection.
        /// Called by SimulationEngine during initialization.
        /// </summary>
        public void SetMortalityModel(Biology.MortalityModel mortalityModel)
        {
            _mortalityModel = mortalityModel ?? throw new ArgumentNullException(nameof(mortalityModel));
            _baseSystem.SetMortalityModel(mortalityModel); // base SIR injects here (single path)
            _lodSystem.SetMortalityModel(mortalityModel); // Pass to LOD system as well
            _lodSystem.SetDiseaseSystem(_baseSystem); // Pass DiseaseSystem for prevalence queries
        }

        /// <summary>
        /// Size the disease system to a calibrated slice of the whole-cycle sea
        /// mortality anchor. The disease mortality rate is set so that, at the SIR
        /// endemic equilibrium, the daily disease hazard integrates to
        /// <paramref name="diseasePerCycle"/> over the calibrated sea grow-out.
        /// Populations start at that equilibrium prevalence, so the realized
        /// per-cycle disease slice is deterministic and tracks the calibration.
        /// </summary>
        public void ConfigureDiseaseShare(double diseasePerCycle, double seaPhaseDays)
        {
            if (seaPhaseDays <= 0) seaPhaseDays = 540.0;
            double perCycle = Math.Clamp(diseasePerCycle, 0.0, 0.99);
            double daily = 1.0 - Math.Pow(1.0 - perCycle, 1.0 / seaPhaseDays);
            double endemic = _baseSystem.EquilibriumPrevalence();
            if (endemic < 1e-6) endemic = 1.0;
            _baseSystem.ExtraMortalityRatePerDay = daily / endemic;
            _baseSystem.InitialPrevalence = endemic;
        }
        
        /// <summary>
        /// Main update method. Runs the deterministic population-level SIR for
        /// every population and injects the resulting disease mortality rate into
        /// the shared <see cref="Biology.MortalityModel"/> (the single authoritative
        /// injection path; the LOD/stochastic tiers are telemetry/scenario only and
        /// must not inject independently).
        /// </summary>
        public void Update(List<Zone> zones, List<FishPopulation> populations, int days = 1)
        {
            if (days <= 0) return;

            // Stopwatch has sub-millisecond resolution; DateTime.Now is ~15ms (TD-29).
            var stopwatch = Stopwatch.StartNew();

            // Deterministic population-level SIR: evolve prevalence and inject the
            // disease mortality rate for the next biology step. No RNG on this path.
            TickBaseSystem(populations, days);

            // Auto-adjust LOD mode based on performance
            if (_currentMode == LODMode.Auto)
            {
                AdjustLODBasedOnPerformance(populations);
            }

            var endTime = stopwatch.Elapsed;
            double updateTimeMs = endTime.TotalMilliseconds;
            
            // Update performance tracking
            UpdatePerformanceMetrics(updateTimeMs);
            
            // Notify performance if significant change
            if (_totalUpdates % 10 == 0)
            {
                double currentFPS = 1000.0 / Math.Max(1.0, _averageUpdateTimeMs);
                OnPerformanceUpdate?.Invoke(currentFPS);
            }
        }
        
        /// <summary>
        /// Deterministic population-level SIR tick for every population. The base
        /// <see cref="DiseaseSystem"/> evolves prevalence and injects the disease
        /// mortality rate into the shared <see cref="Biology.MortalityModel"/>.
        /// </summary>
        private void TickBaseSystem(List<FishPopulation> populations, int days)
        {
            for (int i = 0; i < populations.Count; i++)
                _baseSystem.Tick(populations[i], days, 10.0);
        }

        /// <summary>
        /// Update with full high-fidelity simulation (for small populations or debugging).
        /// </summary>
        private void UpdateHighFidelity(List<Zone> zones, List<FishPopulation> populations, int days)
        {
            // High-fidelity path: deterministic base SIR injection (the stochastic
            // engine is retained for scenario/telemetry use only).
            TickBaseSystem(populations, days);
        }
        
        /// <summary>
        /// Update with medium fidelity (zone-level SIR, no individual tracking).
        /// </summary>
        private void UpdateMediumFidelity(List<Zone> zones, List<FishPopulation> populations, int days)
        {
            // Simplified zone-level transmission
            foreach (var zone in zones)
            {
                double zoneInfectionPressure = CalculateZoneInfectionPressure(zone, populations);
                
                foreach (var population in populations.Where(p => p.ZoneId == zone.Id))
                {
                    double prevalence = _baseSystem.GetPrevalence(population);
                    double transmissionRate = MediumTierTransmissionRate * _externalDiseasePressureMultiplier;
                    double recoveryRate = MediumTierRecoveryRate / Math.Max(1.0, _externalDiseasePressureMultiplier * 0.5);
                    
                    // Simple SIR update
                    for (int day = 0; day < days; day++)
                    {
                        double newInfections = transmissionRate * zoneInfectionPressure * prevalence * (1.0 - prevalence);
                        double recoveries = recoveryRate * prevalence;
                        prevalence += newInfections - recoveries;
                        prevalence = Math.Clamp(prevalence, 0.0, 1.0);
                    }
                    
                    // Update base system
                    UpdateBaseSystemPrevalence(population, prevalence);
                    
                    // Inject disease mortality rate (will be applied by MortalityModel during AdvanceDays)
                    InjectDiseaseMortalityRate(population, prevalence, days);
                }
            }
        }
        
        /// <summary>
        /// Update with low fidelity (minimal simulation, primarily for distant populations).
        /// </summary>
        private void UpdateLowFidelity(List<Zone> zones, List<FishPopulation> populations, int days)
        {
            // Very simple prevalence decay model
            foreach (var population in populations)
            {
                double prevalence = _baseSystem.GetPrevalence(population);
                
                // Simple exponential decay
                double decayRate = LowTierDailyDecayRate;
                prevalence *= Math.Pow(1.0 - decayRate, days);
                
                UpdateBaseSystemPrevalence(population, prevalence);
                
                // Inject minimal disease mortality rate
                if (prevalence > LowTierPrevalenceFloor)
                {
                    InjectMinimalDiseaseMortalityRate(population, prevalence, days);
                }
            }
        }
        
        /// <summary>
        /// Update using the LOD system (dynamic fidelity per population).
        /// </summary>
        private void UpdateWithLODSystem(List<Zone> zones, List<FishPopulation> populations, int days)
        {
            _lodSystem.Update(zones, populations, days);
        }
        
        /// <summary>
        /// Auto-adjust LOD based on performance measurements.
        /// </summary>
        private void AdjustLODBasedOnPerformance(List<FishPopulation> populations)
        {
            int totalFish = populations.Sum(p => p.Cohorts.Sum(c => c.Count));
            
            // Adjust based on current performance
            if (_averageUpdateTimeMs > MaxUpdateTimeMs * 1.5) // Falling behind
            {
                // Reduce detail
                if (_currentMode != LODMode.Low)
                {
                    _currentMode = LODMode.Low;
                    OnLODModeChanged?.Invoke(_currentMode);
                }
                
                // Reduce high-detail limit
                _performanceBasedHighDetailLimit = Math.Max(100, _performanceBasedHighDetailLimit / 2);
            }
            else if (_averageUpdateTimeMs < MaxUpdateTimeMs * 0.7) // Good headroom
            {
                // Can increase detail
                if (_currentMode == LODMode.Low && totalFish < 5000)
                {
                    _currentMode = LODMode.Auto;
                    OnLODModeChanged?.Invoke(_currentMode);
                }
                
                // Increase high-detail limit gradually
                _performanceBasedHighDetailLimit = Math.Min(2000, _performanceBasedHighDetailLimit * 3 / 2);
            }
            
            // Update LOD system limit
            UpdateLODSystemLimits();
        }
        
        /// <summary>
        /// Update LOD system with current performance-based limits.
        /// </summary>
        private void UpdateLODSystemLimits()
        {
            // In a full implementation, would configure the LOD system
            // For now, just log the adjustment
            SimLog.Debug($"[IntegratedDisease] Adjusted high-detail limit to {_performanceBasedHighDetailLimit}");
        }
        
        /// <summary>
        /// Calculate infection pressure for a zone based on nearby infected populations.
        /// </summary>
        private double CalculateZoneInfectionPressure(Zone zone, List<FishPopulation> populations)
        {
            double totalPressure = 0.0;
            
            foreach (var population in populations)
            {
                if (population.ZoneId == zone.Id) continue;
                
                double prevalence = _baseSystem.GetPrevalence(population);
                double distance = EstimateZoneDistance(zone.Id, population.ZoneId);
                
                // Gaussian kernel: pressure decays with distance
                double spreadFactor = Math.Exp(-Math.Pow(distance, 2) / (2 * Math.Pow(WaterborneKernelRadiusKm, 2)));
                totalPressure += prevalence * spreadFactor;
            }
            
            return totalPressure;
        }
        
        /// <summary>
        /// Update base system prevalence (maintains compatibility with existing code).
        /// </summary>
        private void UpdateBaseSystemPrevalence(FishPopulation population, double prevalence)
        {
            // This would update the internal state of _baseSystem
            // For now, just a placeholder
        }
        
        /// <summary>
        /// Inject disease mortality rate into MortalityModel (instead of directly modifying cohorts).
        /// This prevents double-application of mortality.
        /// </summary>
        private void InjectDiseaseMortalityRate(FishPopulation population, double prevalence, int days)
        {
            if (_mortalityModel == null) return; // Fallback if not wired
            
            double dailyMortalityRate = HighTierMortalityPerPrevalence * prevalence * _externalDiseasePressureMultiplier; // 0.2% per day at 100% prevalence
            
            System.Diagnostics.Debug.WriteLine($"[IntegratedDisease] Injecting disease rate: PopId={population.Id}, Prevalence={prevalence:F4}, DailyRate={dailyMortalityRate:F6}, Days={days}");
            
            _mortalityModel.SetDiseaseMortalityRate(population.Id, dailyMortalityRate);
        }
        
        /// <summary>
        /// Inject minimal disease mortality rate (for low-fidelity simulation).
        /// </summary>
        private void InjectMinimalDiseaseMortalityRate(FishPopulation population, double prevalence, int days)
        {
            if (_mortalityModel == null) return; // Fallback if not wired
            
            // For low-fidelity, use lower disease mortality to avoid over-killing distant populations
            double dailyMortalityRate = LowTierMortalityPerPrevalence * prevalence * _externalDiseasePressureMultiplier; // 0.02% per day at 100% prevalence
            
            System.Diagnostics.Debug.WriteLine($"[IntegratedDisease][Minimal] Injecting disease rate: PopId={population.Id}, Prevalence={prevalence:F4}, DailyRate={dailyMortalityRate:F6}");
            
            _mortalityModel.SetDiseaseMortalityRate(population.Id, dailyMortalityRate);
        }
        
        /// <summary>
        /// Estimate distance between zones deterministically based on zone IDs.
        /// Returns a consistent distance for the same zone pair.
        /// </summary>
        private double EstimateZoneDistance(string zoneId1, string zoneId2)
        {
            // Deterministic hash-based distance: same zone pair always returns same distance
            // Ensures disease spread calculations are reproducible
            int hash = HashCode.Combine(zoneId1, zoneId2);
            // Map to 0-30km range deterministically
            return (Math.Abs(hash) % 3000) / 100.0;
        }
        
        /// <summary>
        /// Update performance tracking metrics.
        /// </summary>
        private void UpdatePerformanceMetrics(double updateTimeMs)
        {
            _updateTimes.Enqueue(updateTimeMs);
            if (_updateTimes.Count > PerformanceSampleCount)
                _updateTimes.Dequeue();
            
            _averageUpdateTimeMs = _updateTimes.Average();
            _totalUpdates++;
        }
        
        /// <summary>
        /// Get current system status for UI display.
        /// </summary>
        public DiseaseSystemStatus GetStatus()
        {
            var lodStats = _lodSystem.GetPerformanceStats();
            
            return new DiseaseSystemStatus
            {
                CurrentLODMode = _currentMode,
                AverageUpdateTimeMs = _averageUpdateTimeMs,
                EstimatedFPS = 1000.0 / Math.Max(1.0, _averageUpdateTimeMs),
                HighDetailFishCount = lodStats.HighDetailCount,
                PerformanceImpact = lodStats.EstimatedPerformanceImpact,
                TotalUpdates = _totalUpdates,
                IsMeetingTargetFPS = (1000.0 / Math.Max(1.0, _averageUpdateTimeMs)) >= TargetFPS * 0.9
            };
        }
        
        /// <summary>
        /// Set LOD mode manually (for debugging or user preference).
        /// </summary>
        public void SetLODMode(LODMode mode)
        {
            if (_currentMode != mode)
            {
                _currentMode = mode;
                OnLODModeChanged?.Invoke(mode);
            }
        }
        
        /// <summary>
        /// Calibrates disease pressure from normalized external datasets.
        /// </summary>
        public void SetExternalDiseasePressure(double multiplier)
        {
            _externalDiseasePressureMultiplier = Math.Clamp(multiplier, 0.75, 1.75);
        }

        /// <summary>
        /// Get current prevalence for a population.
        /// </summary>
        public double GetPrevalence(FishPopulation population)
        {
            return _baseSystem.GetPrevalence(population);
        }
        
        /// <summary>
        /// Apply treatment to a population.
        /// </summary>
        public void ApplyTreatment(FishPopulation population, string treatmentType, double reductionFraction,
            double directTreatmentEfficacy, double temperatureC)
        {
            _baseSystem.ApplyTreatment(population, treatmentType, reductionFraction, directTreatmentEfficacy, temperatureC);
        }

        /// <summary>
        /// Apply treatment to a population (legacy 4-arg overload, defaults to mechanical).
        /// </summary>
        public void ApplyTreatment(FishPopulation population, double reductionFraction,
            double directTreatmentEfficacy, double temperatureC)
        {
            _baseSystem.ApplyTreatment(population, "mechanical", reductionFraction, directTreatmentEfficacy, temperatureC);
        }
    }
    
    /// <summary>
    /// Level of detail modes for disease simulation.
    /// </summary>
    public enum LODMode
    {
        Auto,     // Dynamically adjust based on performance
        High,     // Full stochastic engine (individual tracking)
        Medium,   // Zone-level SIR model
        Low       // Minimal simulation (prevalence tracking only)
    }
    
    /// <summary>
    /// Disease system status for UI display.
    /// </summary>
    public struct DiseaseSystemStatus
    {
        public LODMode CurrentLODMode;
        public double AverageUpdateTimeMs;
        public double EstimatedFPS;
        public int HighDetailFishCount;
        public double PerformanceImpact; // 0.0-1.0 scale
        public int TotalUpdates;
        public bool IsMeetingTargetFPS;
        
        public string GetStatusDescription()
        {
            return IsMeetingTargetFPS 
                ? $"Running at {EstimatedFPS:F1} FPS ({CurrentLODMode} mode)"
                : $"Performance limited: {EstimatedFPS:F1} FPS ({CurrentLODMode} mode)";
        }
    }
}