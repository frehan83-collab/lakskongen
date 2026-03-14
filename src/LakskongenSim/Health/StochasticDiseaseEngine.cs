using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Environment;

namespace LakskongenSim.Health
{
    /// <summary>
    /// Stochastic disease engine with multi‑vector transmission, resistance evolution,
    /// and agent‑based outbreak modeling.
    /// Extends existing DiseaseSystem with advanced mechanics.
    /// </summary>
    public class StochasticDiseaseEngine
    {
        // Transmission vectors
        private const double WaterborneTransmissionRadius = 15.0; // km (ISA spreads 5‑15km)
        private const double FomiteTransmissionProbability = 0.005; // Equipment/personnel
        private const double LiceborneTransmissionMultiplier = 1.5; // Lice increase transmission
        
        // Resistance evolution
        private const double ResistanceSelectionPressure = 0.1; // Treatment eliminates susceptible strains
        private const double ResistanceFitnessCost = 0.1; // Resistant strains grow 10% slower without treatment
        private const double ResistanceGeneTransferRate = 0.001; // Plasmid‑borne transfer
        
        // Superspreader events (Pareto: 20% cause 80% transmission)
        private const double SuperspreaderFraction = 0.2;
        private const double SuperspreaderTransmissionMultiplier = 4.0;
        
        // Latent periods (infection → symptoms → detection → reporting)
        private const double LatentPeriodMean = 7.0; // days
        private const double DetectionDelayMean = 3.0; // days after symptoms
        private const double ReportingDelayMean = 2.0; // days after detection
        
        private readonly DiseaseSystem _baseSystem;
        private readonly Random _random = new Random();
        
        // Agent‑based tracking
        private Dictionary<FishIndividual, DiseaseState> _individualStates = new Dictionary<FishIndividual, DiseaseState>();
        private Dictionary<Zone, double> _zoneInfectionPressure = new Dictionary<Zone, double>();
        
        // Resistance tracking
        private Dictionary<string, double> _resistanceGeneFrequency = new Dictionary<string, double>();
        
        public StochasticDiseaseEngine(DiseaseSystem baseSystem)
        {
            _baseSystem = baseSystem ?? throw new ArgumentNullException(nameof(baseSystem));
            
            // Initialize resistance genes for common pathogens
            _resistanceGeneFrequency["ISA"] = 0.01; // 1% initial resistance
            _resistanceGeneFrequency["PD"] = 0.05;  // 5% initial resistance
            _resistanceGeneFrequency["CMS"] = 0.02; // 2% initial resistance
        }
        
        /// <summary>
        /// Simulate multi‑vector disease transmission across zones.
        /// Water‑borne spread via Gaussian kernel (5‑15km), lice‑borne, fomite transmission.
        /// </summary>
        public void SimulateMultiVectorTransmission(List<Zone> zones, List<FishPopulation> populations, int days)
        {
            if (days <= 0) return;
            
            // Ensure all zones have entries in infection pressure map
            foreach (var zone in zones)
            {
                if (!_zoneInfectionPressure.ContainsKey(zone))
                    _zoneInfectionPressure[zone] = 0.0;
            }
            
            // Update zone infection pressure from water‑borne spread
            UpdateWaterborneSpread(zones);
            
            for (int day = 0; day < days; day++)
            {
                // 1. Water‑borne transmission (distance‑dependent)
                foreach (var zone in zones)
                {
                    if (!_zoneInfectionPressure.TryGetValue(zone, out double infectionPressure))
                        infectionPressure = 0.0;
                    foreach (var population in populations.Where(p => p.ZoneId == zone.Id))
                    {
                        double waterborneProbability = infectionPressure * 0.01; // Pressure → probability
                        if (_random.NextDouble() < waterborneProbability)
                        {
                            InfectPopulation(population, "waterborne");
                        }
                    }
                }
                
                // 2. Lice‑borne transmission (enhanced by lice count)
                foreach (var population in populations)
                {
                    double liceFactor = _baseSystem.LiceSusceptibilityFactor * _baseSystem.LiceModel.AverageLicePerFish;
                    double liceborneProbability = liceFactor * 0.1;
                    if (_random.NextDouble() < liceborneProbability)
                    {
                        InfectPopulation(population, "liceborne");
                    }
                }
                
                // 3. Fomite transmission (equipment/personnel movement)
                if (populations.Count > 0 && _random.NextDouble() < FomiteTransmissionProbability)
                {
                    // Random population gets infected via equipment
                    var targetPopulation = populations[_random.Next(populations.Count)];
                    InfectPopulation(targetPopulation, "fomite");
                }
                
                // 4. Superspreader events (Pareto distribution)
                if (populations.Count > 0 && _random.NextDouble() < SuperspreaderFraction)
                {
                    var superspreaderPopulation = populations[_random.Next(populations.Count)];
                    SuperspreaderEvent(superspreaderPopulation);
                }
                
                // 5. Resistance evolution update
                UpdateResistanceEvolution(populations);
                
                // 6. Apply base system tick (existing SIR model)
                foreach (var population in populations)
                {
                    _baseSystem.Tick(population, 1, 10.0); // Temperature default
                }
            }
        }
        
        /// <summary>
        /// Water‑borne spread via Gaussian kernel (simplified advection‑diffusion).
        /// Infection pressure decays with distance: exp(−distance² / (2×radius²))
        /// </summary>
        private void UpdateWaterborneSpread(List<Zone> zones)
        {
            // Simplified: infection pressure propagates from infected zones
            foreach (var sourceZone in zones)
            {
                double sourceInfection = GetZoneInfectionLevel(sourceZone);
                if (sourceInfection > 0.1) // Significant infection
                {
                    foreach (var targetZone in zones)
                    {
                        if (sourceZone.Id == targetZone.Id) continue;
                        
                        // Distance‑based spread (would use actual coordinates in full impl)
                        double distance = EstimateZoneDistance(sourceZone, targetZone);
                        double spreadFactor = Math.Exp(-Math.Pow(distance, 2) / (2 * Math.Pow(WaterborneTransmissionRadius, 2)));
                        
                        double pressureIncrease = sourceInfection * spreadFactor * 0.1;
                        if (!_zoneInfectionPressure.ContainsKey(targetZone))
                            _zoneInfectionPressure[targetZone] = 0.0;
                        _zoneInfectionPressure[targetZone] += pressureIncrease;
                    }
                }
            }
        }
        
        /// <summary>
        /// Infect a population with specified transmission vector.
        /// Includes latent period, detection delay, reporting delay.
        /// </summary>
        private void InfectPopulation(FishPopulation population, string transmissionVector)
        {
            // Check resistance gene frequency for this population's zone
            double resistanceProbability = GetResistanceProbability(population.ZoneId);
            
            // Base infection probability modified by resistance
            double baseProbability = 0.05; // 5% chance per exposure
            double actualProbability = baseProbability * (1.0 - resistanceProbability);
            
            if (_random.NextDouble() < actualProbability)
            {
                // Latent period before symptoms
                double latentPeriod = LatentPeriodMean + (_random.NextDouble() - 0.5) * 2.0;
                
                // Detection delay after symptoms
                double detectionDelay = DetectionDelayMean + (_random.NextDouble() - 0.5) * 1.0;
                
                // Reporting delay after detection
                double reportingDelay = ReportingDelayMean + (_random.NextDouble() - 0.5) * 1.0;
                
                // Total delay before player notification
                double totalDelay = latentPeriod + detectionDelay + reportingDelay;
                
                // Apply infection with delay
                ApplyDelayedInfection(population, totalDelay, transmissionVector);
            }
        }
        
        /// <summary>
        /// Superspreader event: 20% of infected fish cause 80% of transmission.
        /// </summary>
        private void SuperspreaderEvent(FishPopulation population)
        {
            // Identify superspreader individuals (20% of infected)
            var infectedIndividuals = _individualStates.Where(s => s.Value.IsInfected).ToList();
            if (infectedIndividuals.Count > 0)
            {
                int superspreaderCount = (int)(infectedIndividuals.Count * SuperspreaderFraction);
                for (int i = 0; i < superspreaderCount; i++)
                {
                    var superspreader = infectedIndividuals[_random.Next(infectedIndividuals.Count)].Key;
                    
                    // Superspreader causes enhanced transmission
                    double enhancedTransmission = SuperspreaderTransmissionMultiplier;
                    
                    // Spread to nearby individuals
                    SpreadFromSuperspreader(superspreader, enhancedTransmission);
                }
            }
        }
        
        /// <summary>
        /// Update resistance evolution via selection pressure and gene transfer.
        /// Treatment eliminates susceptible strains, resistant strains proliferate.
        /// </summary>
        private void UpdateResistanceEvolution(List<FishPopulation> populations)
        {
            // Selection pressure: treatment increases resistance frequency
            foreach (var population in populations)
            {
                if (population.HasRecentTreatment)
                {
                    // Treatment eliminates susceptible strains
                    double resistanceIncrease = ResistanceSelectionPressure * population.TreatmentIntensity;
                    UpdateResistanceGeneFrequency(population.ZoneId, resistanceIncrease);
                }
            }
            
            // Gene transfer between bacteria (plasmid‑borne)
            // Copy keys to avoid modifying collection during iteration
            var geneKeys = _resistanceGeneFrequency.Keys.ToList();
            foreach (var gene in geneKeys)
            {
                double transferRate = ResistanceGeneTransferRate;
                double currentFrequency = _resistanceGeneFrequency[gene];
                double newFrequency = currentFrequency + transferRate * (1.0 - currentFrequency);
                _resistanceGeneFrequency[gene] = Math.Min(newFrequency, 0.95); // Cap at 95%
            }
            
            // Fitness cost: resistant strains grow slower without treatment
            ApplyResistanceFitnessCost(populations);
        }
        
        /// <summary>
        /// Apply fitness cost to resistant populations (5‑15% slower growth).
        /// </summary>
        private void ApplyResistanceFitnessCost(List<FishPopulation> populations)
        {
            foreach (var population in populations)
            {
                double resistanceLevel = GetResistanceProbability(population.ZoneId);
                if (resistanceLevel > 0.1 && !population.HasRecentTreatment)
                {
                    // Growth penalty: resistant strains grow slower without treatment
                    double growthPenalty = ResistanceFitnessCost * resistanceLevel;
                    population.ApplyGrowthPenalty(growthPenalty);
                }
            }
        }
        
        /// <summary>
        /// Get current resistance probability for a zone.
        /// </summary>
        private double GetResistanceProbability(string zoneId)
        {
            // Simplified: average of all resistance genes
            double averageResistance = 0.0;
            foreach (var frequency in _resistanceGeneFrequency.Values)
            {
                averageResistance += frequency;
            }
            return averageResistance / _resistanceGeneFrequency.Count;
        }
        
        /// <summary>
        /// Update resistance gene frequency for a zone.
        /// </summary>
        private void UpdateResistanceGeneFrequency(string zoneId, double increase)
        {
            var keys = _resistanceGeneFrequency.Keys.ToList();
            foreach (var gene in keys)
            {
                _resistanceGeneFrequency[gene] += increase;
                _resistanceGeneFrequency[gene] = Math.Min(_resistanceGeneFrequency[gene], 0.95);
            }
        }
        
        /// <summary>
        /// Estimate zone distance (placeholder for actual coordinate system).
        /// </summary>
        private double EstimateZoneDistance(Zone zone1, Zone zone2)
        {
            // Simplified: random distance between 0‑30km
            return _random.NextDouble() * 30.0;
        }
        
        /// <summary>
        /// Get zone infection level from individual states.
        /// </summary>
        private double GetZoneInfectionLevel(Zone zone)
        {
            var zoneIndividuals = _individualStates.Where(s => s.Key.ZoneId == zone.Id);
            int infectedCount = zoneIndividuals.Count(s => s.Value.IsInfected);
            int totalCount = zoneIndividuals.Count();
            
            return totalCount > 0 ? (double)infectedCount / totalCount : 0.0;
        }
        
        /// <summary>
        /// Apply delayed infection with notification timing.
        /// </summary>
        private void ApplyDelayedInfection(FishPopulation population, double delayDays, string transmissionVector)
        {
            // Record infection with delay metadata
            foreach (var individual in population.GetIndividuals())
            {
                if (_random.NextDouble() < 0.3) // 30% of individuals infected
                {
                    _individualStates[individual] = new DiseaseState
                    {
                        IsInfected = true,
                        InfectionTime = delayDays,
                        TransmissionVector = transmissionVector,
                        Detected = false,
                        Reported = false
                    };
                }
            }
        }
        
        /// <summary>
        /// Spread from superspreader to nearby individuals.
        /// </summary>
        private void SpreadFromSuperspreader(FishIndividual superspreader, double transmissionMultiplier)
        {
            // Find nearby individuals (same zone)
            var nearbyIndividuals = _individualStates.Where(s => s.Key.ZoneId == superspreader.ZoneId).ToList();
            
            foreach (var nearby in nearbyIndividuals)
            {
                if (!nearby.Value.IsInfected)
                {
                    double infectionProbability = 0.1 * transmissionMultiplier;
                    if (_random.NextDouble() < infectionProbability)
                    {
                        nearby.Value.IsInfected = true;
                        nearby.Value.TransmissionVector = "superspreader";
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Disease state for individual fish tracking.
    /// </summary>
    public class DiseaseState
    {
        public bool IsInfected { get; set; }
        public double InfectionTime { get; set; } // Days until symptoms
        public string TransmissionVector { get; set; } = null!;
        public bool Detected { get; set; }
        public bool Reported { get; set; }
    }
    
    /// <summary>
    /// Individual fish representation for agent‑based modeling.
    /// </summary>
    public class FishIndividual
    {
        public string Id { get; set; } = null!;
        public string ZoneId { get; set; } = null!;
        public double Weight { get; set; }
        public double GrowthRate { get; set; }
        public bool HasResistanceGene { get; set; }
    }
}