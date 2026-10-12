using System;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Environment;

namespace LakskongenSim.Biology
{
    /// <summary>
    /// Represents an individual fish with unique genetic parameters and growth trajectory.
    /// Each fish tracks its own:
    /// - Genetic asymptotic weight (±15% variance)
    /// - Genetic growth rate (±10% variance)
    /// - Stochastic variance coefficient (±20% variance)
    /// - Epigenetic memory from early-life stress
    /// - Current weight and life stage
    /// - Survival status
    /// - Environmental stress accumulation
    /// 
    /// Environmental Integration (Mareano/Geonorge):
    /// - Temperature-dependent growth (Gaussian response curve)
    /// - Oxygen stress affects growth and survival
    /// - Current strength affects feed availability
    /// - Accumulated stress affects long-term health
    /// </summary>
    public class IndividualFish
    {
        #region Properties

        /// <summary>Unique identifier for this fish.</summary>
        public Guid Id { get; private set; }

        /// <summary>Current weight in kg.</summary>
        public double Weight { get; private set; }

        /// <summary>Genetic asymptotic weight K in kg (4-6 kg ±15%).</summary>
        public double GeneticAsymptoticWeight { get; private set; }

        /// <summary>Genetic maximum growth rate α_max.</summary>
        public double GeneticGrowthRate { get; private set; }

        /// <summary>Individual stochastic variance coefficient σ.</summary>
        public double StochasticVariance { get; private set; }

        /// <summary>Epigenetic memory tracking early-life stress effects.</summary>
        public EpigeneticMemory EpigeneticMemory { get; private set; }

        /// <summary>Current life stage.</summary>
        public LifeStage LifeStage { get; set; }

        /// <summary>Age in days.</summary>
        public int AgeDays { get; private set; }

        /// <summary>Is the fish alive?</summary>
        public bool IsAlive { get; private set; }

        /// <summary>Timestamp when fish was spawned.</summary>
        public DateTime SpawnedAt { get; private set; }

        /// <summary>Timestamp when fish died (null if still alive).</summary>
        public DateTime? DiedAt { get; private set; }

        /// <summary>Effective asymptotic weight after epigenetic effects.</summary>
        public double EffectiveAsymptoticWeight =>
            GeneticAsymptoticWeight * EpigeneticMemory.AsymptoticMultiplier;

        // Environmental stress tracking (Mareano/Geonorge integration)
        /// <summary>Accumulated thermal stress (degree-days above optimal).</summary>
        public double AccumulatedThermalStress { get; private set; }
        
        /// <summary>Accumulated oxygen stress (days below critical threshold).</summary>
        public double AccumulatedOxygenStress { get; private set; }
        
        /// <summary>Current environmental stress multiplier (0.0-1.0, 1.0 = optimal).</summary>
        public double CurrentEnvironmentalMultiplier { get; private set; } = 1.0;
        
        /// <summary>Daily stress history (rolling 30-day window).</summary>
        private readonly Queue<double> _dailyStressHistory = new Queue<double>(30);
        
        /// <summary>Average stress over last 30 days.</summary>
        public double AverageStressLevel => _dailyStressHistory.Count > 0 ? 
            _dailyStressHistory.Average() : 0.0;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new individual fish with unique genetic parameters.
        /// </summary>
        /// <param name="sdeModel">SDE growth model for generating genetic parameters.</param>
        /// <param name="initialWeight">Starting weight in kg (default 0.005 kg = 5g fry).</param>
        /// <param name="lifeStage">Starting life stage (default Fry).</param>
        public IndividualFish(
            SDEGrowthModel sdeModel,
            double initialWeight = 0.005,
            LifeStage lifeStage = LifeStage.Fry)
        {
            Id = Guid.NewGuid();
            Weight = initialWeight;
            LifeStage = lifeStage;
            AgeDays = 0;
            IsAlive = true;
            SpawnedAt = DateTime.UtcNow;
            DiedAt = null;

            // Generate unique genetic parameters
            GeneticAsymptoticWeight = sdeModel.GenerateGeneticAsymptoticWeight();
            GeneticGrowthRate = sdeModel.GenerateGeneticGrowthRate();
            StochasticVariance = sdeModel.GenerateStochasticVariance();

            // Initialize epigenetic memory
            EpigeneticMemory = new EpigeneticMemory();
        }

        /// <summary>
        /// Initializes a fish with specific genetic parameters (for testing or breeding scenarios).
        /// </summary>
        public IndividualFish(
            double asymptoticWeight,
            double growthRate,
            double variance,
            double initialWeight = 0.005,
            LifeStage lifeStage = LifeStage.Fry)
        {
            Id = Guid.NewGuid();
            Weight = initialWeight;
            LifeStage = lifeStage;
            AgeDays = 0;
            IsAlive = true;
            SpawnedAt = DateTime.UtcNow;
            DiedAt = null;

            GeneticAsymptoticWeight = asymptoticWeight;
            GeneticGrowthRate = growthRate;
            StochasticVariance = variance;

            EpigeneticMemory = new EpigeneticMemory();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Applies one day of SDE-based growth using the fish's unique parameters.
        /// Basic version - temperature only.
        /// </summary>
        /// <param name="sdeModel">SDE growth model instance.</param>
        /// <param name="temperature">Water temperature in °C.</param>
        public void GrowOneDay(SDEGrowthModel sdeModel, double temperature)
        {
            if (!IsAlive) return;

            double newWeight = sdeModel.ApplySDEGrowth(
                Weight,
                temperature,
                GeneticAsymptoticWeight,
                GeneticGrowthRate,
                StochasticVariance,
                EpigeneticMemory.AsymptoticMultiplier,
                1.0);

            Weight = newWeight;
            AgeDays++;

            // Check for death (weight = 0)
            if (Weight <= 0.0)
            {
                Die("Growth failure");
            }
        }
        
        /// <summary>
        /// Applies one day of SDE-based growth with full environmental data integration.
        /// This is the Mareano/Geonorge-integrated growth method.
        /// </summary>
        /// <param name="sdeModel">SDE growth model instance.</param>
        /// <param name="environmentalData">Environmental data for the zone.</param>
        public void GrowOneDay(SDEGrowthModel sdeModel, EnvironmentalDataPoint environmentalData)
        {
            if (!IsAlive) return;

            // Update environmental stress tracking
            UpdateEnvironmentalStress(environmentalData);
            
            // Calculate environmental multiplier for growth
            CurrentEnvironmentalMultiplier = CalculateEnvironmentalMultiplier(environmentalData);
            
            // Apply growth with environmental corrections
            double newWeight = sdeModel.ApplySDEGrowthWithEnvironment(
                Weight,
                environmentalData,
                GeneticAsymptoticWeight,
                GeneticGrowthRate,
                StochasticVariance,
                EpigeneticMemory.AsymptoticMultiplier,
                CurrentEnvironmentalMultiplier,
                1.0);

            Weight = newWeight;
            AgeDays++;

            // Check for death (weight = 0 or extreme stress)
            if (Weight <= 0.0)
            {
                Die("Growth failure");
            }
            else if (AccumulatedOxygenStress > 10.0 && _rng.NextDouble() < 0.01)
            {
                // Small chance of death from chronic oxygen stress
                Die("Chronic hypoxia");
            }
        }
        
        // Chronic-hypoxia roll. Defaults to an unseeded stream (status quo);
        // population/disease ticks may bind a per-fish named stream via SetRng.
        private SimRngStream _rng = SimRng.CreateUnseeded().Stream(SimRngStreams.StochasticDisease);
        /// <summary>Bind a deterministic stream (per-fish identity) for mortality rolls.</summary>
        public void SetRng(SimRngStream rng) => _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        
        /// <summary>
        /// Update accumulated stress metrics based on environmental conditions.
        /// </summary>
        private void UpdateEnvironmentalStress(EnvironmentalDataPoint data)
        {
            // Thermal stress: accumulate degree-days above optimal (11°C) + 3°C tolerance
            const double optimalTemp = 11.0;
            const double tempTolerance = 3.0;
            double tempExcess = data.TemperatureAtCageDepth - (optimalTemp + tempTolerance);
            if (tempExcess > 0)
            {
                AccumulatedThermalStress += tempExcess;
            }
            else
            {
                // Recovery from thermal stress
                AccumulatedThermalStress = Math.Max(0, AccumulatedThermalStress - 0.5);
            }
            
            // Oxygen stress: count days below critical threshold (5 mg/L)
            const double criticalOxygen = 5.0;
            if (data.OxygenAtCageDepth < criticalOxygen)
            {
                AccumulatedOxygenStress += 1.0;
            }
            else
            {
                // Recovery from oxygen stress
                AccumulatedOxygenStress = Math.Max(0, AccumulatedOxygenStress - 0.3);
            }
            
            // Track daily stress for 30-day average
            double dailyStress = (data.TemperatureStressIndex + data.OxygenStressIndex) / 2.0;
            _dailyStressHistory.Enqueue(dailyStress);
            if (_dailyStressHistory.Count > 30)
            {
                _dailyStressHistory.Dequeue();
            }
        }
        
        /// <summary>
        /// Calculate environmental multiplier for growth (0.0-1.0+).
        /// </summary>
        private double CalculateEnvironmentalMultiplier(EnvironmentalDataPoint data)
        {
            // Base multiplier from environmental quality score
            double multiplier = data.EnvironmentalQualityScore;
            
            // Apply accumulated stress penalties
            double stressPenalty = Math.Min(0.3, (AccumulatedThermalStress * 0.001) + (AccumulatedOxygenStress * 0.01));
            multiplier -= stressPenalty;
            
            // Current effect: moderate currents improve feed distribution
            // Optimal current: 0.1-0.3 m/s
            if (data.CurrentSpeedAtCageDepth < 0.05)
            {
                // Too little current - poor water exchange, feed accumulation
                multiplier *= 0.95;
            }
            else if (data.CurrentSpeedAtCageDepth > 0.5)
            {
                // Too much current - fish stress, energy expenditure
                multiplier *= 0.90;
            }
            else if (data.CurrentSpeedAtCageDepth >= 0.1 && data.CurrentSpeedAtCageDepth <= 0.3)
            {
                // Optimal current - slight bonus
                multiplier *= 1.05;
            }
            
            return Math.Max(0.5, multiplier);
        }

        /// <summary>
        /// Applies multiple days of growth.
        /// </summary>
        /// <param name="sdeModel">SDE growth model instance.</param>
        /// <param name="days">Number of days to simulate.</param>
        /// <param name="temperature">Average water temperature in °C.</param>
        public void GrowMultipleDays(SDEGrowthModel sdeModel, int days, double temperature)
        {
            if (!IsAlive || days <= 0) return;

            for (int i = 0; i < days; i++)
            {
                GrowOneDay(sdeModel, temperature);
                if (!IsAlive) break;
            }
        }

        /// <summary>
        /// Records an early-life stress event affecting epigenetic memory.
        /// </summary>
        /// <param name="stressType">Type of stress experienced.</param>
        /// <param name="severity">Severity from 0.0 to 1.0.</param>
        public void RecordStress(StressType stressType, double severity)
        {
            if (!IsAlive) return;

            EpigeneticMemory.RecordStressEvent(stressType, severity, AgeDays);
        }

        /// <summary>
        /// Marks the fish as dead.
        /// </summary>
        /// <param name="cause">Cause of death (for logging/analytics).</param>
        public void Die(string cause = "Unknown")
        {
            if (!IsAlive) return;

            IsAlive = false;
            DiedAt = DateTime.UtcNow;
            Weight = 0.0;
        }

        /// <summary>
        /// Advances life stage based on age or weight thresholds.
        /// </summary>
        public void UpdateLifeStage()
        {
            if (!IsAlive) return;

            // Typical life stage transitions for Atlantic salmon
            if (LifeStage == LifeStage.Egg && AgeDays >= 60)
            {
                LifeStage = LifeStage.Alevin;
            }
            else if (LifeStage == LifeStage.Alevin && AgeDays >= 90)
            {
                LifeStage = LifeStage.Fry;
            }
            else if (LifeStage == LifeStage.Fry && Weight >= 0.01) // 10g
            {
                LifeStage = LifeStage.Parr;
            }
            else if (LifeStage == LifeStage.Parr && Weight >= 0.05) // 50g
            {
                LifeStage = LifeStage.Smolt;
            }
            else if (LifeStage == LifeStage.Smolt && Weight >= 0.15) // 150g (post-smolt)
            {
                LifeStage = LifeStage.Seawater;
            }
            else if (LifeStage == LifeStage.Seawater && Weight >= 4.0) // 4kg (approaching harvest)
            {
                LifeStage = LifeStage.Harvest;
            }
        }

        /// <summary>
        /// Returns a summary string for this fish.
        /// Uses invariant culture for numeric formatting to ensure consistent decimal separators.
        /// </summary>
        public override string ToString()
        {
            string status = IsAlive ? "Alive" : "Dead";
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            return $"Fish {Id:N} [{status}] {Weight.ToString("F3", inv)}kg @ {AgeDays}d, Stage: {LifeStage}, " +
                   $"K={GeneticAsymptoticWeight.ToString("F2", inv)}kg, Epigenetic={EpigeneticMemory.AsymptoticMultiplier.ToString("F2", inv)}";
        }

        #endregion

        #region Static Factory Methods

        /// <summary>
        /// Creates a population of individual fish with unique genetic parameters.
        /// </summary>
        /// <param name="sdeModel">SDE growth model instance.</param>
        /// <param name="count">Number of fish to create.</param>
        /// <param name="initialWeight">Starting weight for all fish (kg).</param>
        /// <param name="lifeStage">Starting life stage.</param>
        /// <returns>Array of individual fish.</returns>
        public static IndividualFish[] CreatePopulation(
            SDEGrowthModel sdeModel,
            int count,
            double initialWeight = 0.005,
            LifeStage lifeStage = LifeStage.Fry)
        {
            var population = new IndividualFish[count];
            for (int i = 0; i < count; i++)
            {
                population[i] = new IndividualFish(sdeModel, initialWeight, lifeStage);
            }
            return population;
        }

        #endregion
    }
}
