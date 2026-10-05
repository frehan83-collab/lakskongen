using System;
using LakskongenSim.Environment;

namespace LakskongenSim.Biology
{
    /// <summary>
    /// Implements a stochastic differential equation (SDE) driven salmon growth model.
    /// 
    /// Uses a Von Bertalanffy-Gompertz hybrid with temperature-dependent metabolic scaling:
    /// dW = α(T) × W^γ × (1 − W/K) × dt + σ × sqrt(W) × sqrt(dt) × N(0,1)
    /// 
    /// Where:
    /// - W = current weight (kg)
    /// - α(T) = temperature-dependent growth coefficient
    /// - γ = allometric exponent (0.67 for metabolic scaling)
    /// - K = genetic asymptotic weight (4-6 kg ±15% individual variance)
    /// - σ = individual variance coefficient (epigenetic + stochastic)
    /// - dt = time step (days)
    /// - N(0,1) = standard normal distribution
    /// 
    /// The stochastic term scales with sqrt(W) to prevent negative weights for small fish
    /// while keeping variance proportionally smaller for larger fish.
    /// Individual fish get unique growth trajectories ±15% from genetic baseline.
    /// Early-life stress reduces asymptotic size via epigenetic memory.
    /// 
    /// Mareano/Geonorge Environmental Integration:
    /// - Oxygen-dependent metabolism (reduced growth below 6 mg/L O2)
    /// - Current-based feed availability (optimal 0.1-0.3 m/s)
    /// - Temperature stratification effects (depth-specific growth)
    /// - Combined environmental stress penalties
    /// </summary>
    public class SDEGrowthModel
    {
        #region Constants

        /// <summary>Optimal temperature for salmon growth (Norwegian coastal waters).</summary>
        public const double OptimalTemperature = 11.0;

        /// <summary>Thermal tolerance width (standard deviation) in °C.</summary>
        public const double ThermalTolerance = 3.0;

        /// <summary>Allometric exponent for metabolic scaling (W^γ term).</summary>
        public const double AllometricExponent = 0.67;

        /// <summary>Baseline asymptotic weight for standard genetics (kg).</summary>
        public const double BaselineAsymptoticWeight = 5.0;

        /// <summary>Maximum genetic variance in asymptotic weight (±15%).</summary>
        public const double AsymptoticWeightVariance = 0.15;

        /// <summary>Baseline growth rate coefficient at optimal temperature.</summary>
        public const double BaselineGrowthCoefficient = 0.015;

        /// <summary>Maximum genetic variance in growth rate (±10%).</summary>
        public const double GrowthRateVariance = 0.10;

        /// <summary>Baseline stochastic variance coefficient σ.</summary>
        public const double BaselineStochasticVariance = 0.02;

        /// <summary>Maximum variance in stochastic coefficient (±20%).</summary>
        public static readonly double VarianceVariance = 0.20;

        /// <summary>Minimum temperature for growth (°C).</summary>
        public static readonly double MinGrowthTemperature = 2.0;

        /// <summary>Maximum temperature for growth (°C).</summary>
        public static readonly double MaxGrowthTemperature = 18.0;

        // Mareano/Geonorge environmental integration constants
        /// <summary>Optimal dissolved oxygen for growth (mg/L).</summary>
        public const double OptimalOxygen = 8.0;

        /// <summary>Critical oxygen level below which growth is severely impacted (mg/L).</summary>
        public const double CriticalOxygen = 5.0;

        /// <summary>Optimal current speed for feed distribution (m/s).</summary>
        public const double OptimalCurrentMin = 0.1;

        /// <summary>Maximum optimal current speed (m/s).</summary>
        public const double OptimalCurrentMax = 0.3;

        /// <summary>Current speed below which water exchange is insufficient (m/s).</summary>
        public const double WeakCurrentThreshold = 0.05;

        /// <summary>Current speed above which fish stress occurs (m/s).</summary>
        public const double StrongCurrentThreshold = 0.5;

        /// <summary>Oxygen dependence coefficient (growth reduction per mg/L below optimal).</summary>
        public const double OxygenDependenceCoefficient = 0.08;

        #endregion

        #region Fields

        private readonly Random _random;
        
        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the SDEGrowthModel class.
        /// </summary>
        /// <param name="seed">Optional random seed for deterministic testing.</param>
        public SDEGrowthModel(int? seed = null)
        {
            _random = seed.HasValue ? new Random(seed.Value) : Random.Shared;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Calculates temperature-dependent growth coefficient using Gaussian temperature response.
        /// α(T) = α_max × exp(−(T − T_opt)^2 / (2τ^2))
        /// </summary>
        /// <param name="temperature">Water temperature in °C.</param>
        /// <param name="maxGrowthRate">Maximum growth rate α_max.</param>
        /// <returns>Temperature-corrected growth coefficient.</returns>
        public double CalculateTemperatureCorrectedGrowth(double temperature, double maxGrowthRate)
        {
            // No growth outside viable temperature range
            if (temperature < MinGrowthTemperature || temperature > MaxGrowthTemperature)
                return 0.0;

            // Gaussian temperature response curve
            double tempDiff = temperature - OptimalTemperature;
            double exponent = -Math.Pow(tempDiff, 2) / (2.0 * Math.Pow(ThermalTolerance, 2));
            return maxGrowthRate * Math.Exp(exponent);
        }

        /// <summary>
        /// Generates a unique genetic asymptotic weight with ±15% variance.
        /// </summary>
        /// <param name="baselineWeight">Baseline genetic asymptotic weight (default 5.0 kg).</param>
        /// <returns>Individual genetic asymptotic weight K.</returns>
        public double GenerateGeneticAsymptoticWeight(double baselineWeight = BaselineAsymptoticWeight)
        {
            double variance = (_random.NextDouble() - 0.5) * 2.0 * AsymptoticWeightVariance;
            return baselineWeight * (1.0 + variance);
        }

        /// <summary>
        /// Generates a unique genetic growth rate coefficient with ±10% variance.
        /// </summary>
        /// <param name="baselineRate">Baseline growth coefficient (default 0.015).</param>
        /// <returns>Individual genetic growth rate α_max.</returns>
        public double GenerateGeneticGrowthRate(double baselineRate = BaselineGrowthCoefficient)
        {
            double variance = (_random.NextDouble() - 0.5) * 2.0 * GrowthRateVariance;
            return baselineRate * (1.0 + variance);
        }

        /// <summary>
        /// Generates a unique stochastic variance coefficient with ±20% variance.
        /// </summary>
        /// <param name="baselineVariance">Baseline stochastic variance σ (default 0.02).</param>
        /// <returns>Individual variance coefficient.</returns>
        public double GenerateStochasticVariance(double baselineVariance = BaselineStochasticVariance)
        {
            double variance = (_random.NextDouble() - 0.5) * 2.0 * VarianceVariance;
            return baselineVariance * (1.0 + variance);
        }

        /// <summary>
        /// Generates a standard normal random variable N(0,1) using Box-Muller transform.
        /// </summary>
        /// <returns>Standard normal random variable.</returns>
        public double GenerateNormalNoise()
        {
            // Guard against Log(0): NextDouble() can return exactly 0.0.
            double u1 = 1.0 - _random.NextDouble();
            double u2 = _random.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        /// <summary>
        /// Applies SDE-based growth for a single time step (one day).
        /// 
        /// dW = α(T) × W^γ × (1 − W/K) × dt + σ × sqrt(W) × sqrt(dt) × N(0,1)
        /// 
        /// Note: The stochastic term scales with sqrt(W) to prevent negative weights for small fish
        /// while keeping noise proportionally smaller for larger fish.
        /// </summary>
        /// <param name="currentWeight">Current weight in kg.</param>
        /// <param name="temperature">Water temperature in °C.</param>
        /// <param name="asymptoticWeight">Genetic asymptotic weight K (kg).</param>
        /// <param name="maxGrowthRate">Genetic maximum growth rate α_max.</param>
        /// <param name="stochasticVariance">Individual stochastic variance σ.</param>
        /// <param name="epigeneticMultiplier">Epigenetic memory multiplier (0.75-1.0).</param>
        /// <param name="dt">Time step in days (default 1.0).</param>
        /// <returns>New weight after time step dt.</returns>
        public double ApplySDEGrowth(
            double currentWeight,
            double temperature,
            double asymptoticWeight,
            double maxGrowthRate,
            double stochasticVariance,
            double epigeneticMultiplier = 1.0,
            double dt = 1.0)
        {
            // Handle zero or negative weight
            if (currentWeight <= 0.0) return 0.0;

            // Apply epigenetic reduction to asymptotic weight
            double effectiveK = asymptoticWeight * Math.Clamp(epigeneticMultiplier, 0.75, 1.0);

            // Calculate temperature-corrected growth coefficient
            double alphaT = CalculateTemperatureCorrectedGrowth(temperature, maxGrowthRate);

            // Deterministic component: α(T) × W^γ × (1 − W/K) × dt
            double vonBertalanffyTerm = Math.Pow(currentWeight, AllometricExponent);
            double gompertzTerm = 1.0 - (currentWeight / effectiveK);
            double deterministicGrowth = alphaT * vonBertalanffyTerm * gompertzTerm * dt;

            // Stochastic component: σ × deterministicGrowth × sqrt(dt) × N(0,1)
            // Scales proportionally to the deterministic drift to prevent runaway variance
            // near the asymptotic weight (where deterministicGrowth → 0) and at very low
            // weights (eggs/fry). This ensures biological realism:
            // - Near K: small drift AND small noise (fish approaches asymptote smoothly)
            // - Egg stage: tiny drift AND tiny noise (no spurious death from stochastic term)
            // - Mid-growth: proportionate noise relative to growth rate
            double normalNoise = GenerateNormalNoise();
            double stochasticGrowth = stochasticVariance * deterministicGrowth * Math.Sqrt(dt) * normalNoise;

            // Update weight
            double newWeight = currentWeight + deterministicGrowth + stochasticGrowth;

            // Ensure non-negative weight
            return Math.Max(0.0, newWeight);
        }

        /// <summary>
        /// Applies SDE growth over multiple days using Euler-Maruyama method.
        /// </summary>
        /// <param name="initialWeight">Starting weight in kg.</param>
        /// <param name="days">Number of days to simulate.</param>
        /// <param name="temperature">Average water temperature in °C.</param>
        /// <param name="asymptoticWeight">Genetic asymptotic weight K (kg).</param>
        /// <param name="maxGrowthRate">Genetic maximum growth rate α_max.</param>
        /// <param name="stochasticVariance">Individual stochastic variance σ.</param>
        /// <param name="epigeneticMultiplier">Epigenetic memory multiplier (0.75-1.0).</param>
        /// <returns>Final weight after specified days.</returns>
        public double ApplySDEGrowthMultipleDays(
            double initialWeight,
            int days,
            double temperature,
            double asymptoticWeight,
            double maxGrowthRate,
            double stochasticVariance,
            double epigeneticMultiplier = 1.0)
        {
            if (days <= 0) return initialWeight;

            double weight = initialWeight;
            for (int i = 0; i < days; i++)
            {
                weight = ApplySDEGrowth(
                    weight,
                    temperature,
                    asymptoticWeight,
                    maxGrowthRate,
                    stochasticVariance,
                    epigeneticMultiplier,
                    1.0);

                // Early termination if fish dies (weight = 0)
                if (weight <= 0.0) break;
            }

            return weight;
        }

        /// <summary>
        /// Simulates complete lifecycle growth trajectory for a single fish.
        /// Returns array of weights at each time step.
        /// </summary>
        /// <param name="initialWeight">Starting weight (kg, typically 0.001-0.01 for fry).</param>
        /// <param name="days">Number of days to simulate.</param>
        /// <param name="temperatureTimeSeries">Daily temperature values (°C).</param>
        /// <param name="asymptoticWeight">Genetic asymptotic weight K (kg).</param>
        /// <param name="maxGrowthRate">Genetic maximum growth rate α_max.</param>
        /// <param name="stochasticVariance">Individual stochastic variance σ.</param>
        /// <param name="epigeneticMemory">Epigenetic memory instance (optional).</param>
        /// <returns>Array of daily weights.</returns>
        public double[] SimulateLifecycleTrajectory(
            double initialWeight,
            int days,
            double[] temperatureTimeSeries,
            double asymptoticWeight,
            double maxGrowthRate,
            double stochasticVariance,
            EpigeneticMemory? epigeneticMemory = null)
        {
            if (days <= 0 || temperatureTimeSeries == null)
                throw new ArgumentException("Invalid simulation parameters");

            double[] weights = new double[days + 1];
            weights[0] = initialWeight;

            double epigeneticMultiplier = epigeneticMemory?.AsymptoticMultiplier ?? 1.0;

            for (int day = 0; day < days; day++)
            {
                double temperature = temperatureTimeSeries[Math.Min(day, temperatureTimeSeries.Length - 1)];
                
                weights[day + 1] = ApplySDEGrowth(
                    weights[day],
                    temperature,
                    asymptoticWeight,
                    maxGrowthRate,
                    stochasticVariance,
                    epigeneticMultiplier,
                    1.0);

                // Update epigenetic multiplier if memory is being tracked
                if (epigeneticMemory != null && day < EpigeneticMemory.CriticalPeriodEndDay)
                {
                    epigeneticMultiplier = epigeneticMemory.AsymptoticMultiplier;
                }

                if (weights[day + 1] <= 0.0) break;
            }

            return weights;
        }

        /// <summary>
        /// Applies SDE-based growth with full environmental data integration.
        /// 
        /// Incorporates:
        /// - Temperature-dependent growth (Gaussian response)
        /// - Oxygen-dependent metabolism (reduced growth at low O2)
        /// - Current-based feed availability effects
        /// - Combined environmental stress penalties
        /// </summary>
        /// <param name="currentWeight">Current weight in kg.</param>
        /// <param name="environmentalData">Environmental data point with temperature, oxygen, current.</param>
        /// <param name="asymptoticWeight">Genetic asymptotic weight K (kg).</param>
        /// <param name="maxGrowthRate">Genetic maximum growth rate α_max.</param>
        /// <param name="stochasticVariance">Individual stochastic variance σ.</param>
        /// <param name="epigeneticMultiplier">Epigenetic memory multiplier (0.75-1.0).</param>
        /// <param name="environmentalMultiplier">Additional environmental multiplier (0.0-1.0+).</param>
        /// <param name="dt">Time step in days (default 1.0).</param>
        /// <returns>New weight after time step dt.</returns>
        public double ApplySDEGrowthWithEnvironment(
            double currentWeight,
            EnvironmentalDataPoint environmentalData,
            double asymptoticWeight,
            double maxGrowthRate,
            double stochasticVariance,
            double epigeneticMultiplier = 1.0,
            double environmentalMultiplier = 1.0,
            double dt = 1.0)
        {
            // Handle zero or negative weight
            if (currentWeight <= 0.0) return 0.0;

            // Apply epigenetic reduction to asymptotic weight
            double effectiveK = asymptoticWeight * Math.Clamp(epigeneticMultiplier, 0.75, 1.0);

            // Calculate temperature-corrected growth coefficient
            double alphaT = CalculateTemperatureCorrectedGrowth(environmentalData.TemperatureAtCageDepth, maxGrowthRate);

            // Calculate oxygen-dependent growth multiplier
            double oxygenMultiplier = CalculateOxygenGrowthMultiplier(environmentalData.OxygenAtCageDepth);

            // Calculate current-based feed availability multiplier
            double currentMultiplier = CalculateCurrentFeedMultiplier(environmentalData.CurrentSpeedAtCageDepth);

            // Combine all environmental multipliers
            double combinedMultiplier = environmentalMultiplier * oxygenMultiplier * currentMultiplier;
            combinedMultiplier = Math.Clamp(combinedMultiplier, 0.3, 1.5);

            // Deterministic component: α(T) × W^γ × (1 − W/K) × dt × environmental_multipliers
            double vonBertalanffyTerm = Math.Pow(currentWeight, AllometricExponent);
            double gompertzTerm = 1.0 - (currentWeight / effectiveK);
            double deterministicGrowth = alphaT * vonBertalanffyTerm * gompertzTerm * dt * combinedMultiplier;

            // Stochastic component: σ × deterministicGrowth × sqrt(dt) × N(0,1)
            // Scales proportionally to deterministic drift; environmental stress increases variance
            double stressVarianceFactor = 1.0 + (1.0 - environmentalData.EnvironmentalQualityScore) * 0.5;
            double normalNoise = GenerateNormalNoise();
            double stochasticGrowth = stochasticVariance * stressVarianceFactor *
                                     deterministicGrowth * Math.Sqrt(dt) * normalNoise;

            // Update weight
            double newWeight = currentWeight + deterministicGrowth + stochasticGrowth;

            // Ensure non-negative weight
            return Math.Max(0.0, newWeight);
        }

        /// <summary>
        /// Calculate oxygen-dependent growth multiplier.
        /// Growth reduced below optimal oxygen levels (8 mg/L).
        /// Critical below 5 mg/L.
        /// </summary>
        private double CalculateOxygenGrowthMultiplier(double oxygenMgPerL)
        {
            if (oxygenMgPerL >= OptimalOxygen)
            {
                return 1.0; // Optimal conditions
            }
            else if (oxygenMgPerL <= CriticalOxygen)
            {
                // Linear reduction from 0.6 at critical to 0.0 at 0 mg/L
                return Math.Max(0.0, 0.6 * (oxygenMgPerL / CriticalOxygen));
            }
            else
            {
                // Linear interpolation between critical and optimal
                double deficit = OptimalOxygen - oxygenMgPerL;
                return 1.0 - (deficit * OxygenDependenceCoefficient);
            }
        }

        /// <summary>
        /// Calculate current-based feed availability multiplier.
        /// 
        /// Optimal current (0.1-0.3 m/s): good feed distribution, water exchange
        /// Weak current (less than 0.05 m/s): poor water exchange, feed accumulation
        /// Strong current (greater than 0.5 m/s): feed washout, fish stress
        /// </summary>
        private double CalculateCurrentFeedMultiplier(double currentSpeedMS)
        {
            if (currentSpeedMS >= OptimalCurrentMin && currentSpeedMS <= OptimalCurrentMax)
            {
                // Optimal range - slight benefit from good water exchange
                return 1.02;
            }
            else if (currentSpeedMS < WeakCurrentThreshold)
            {
                // Too weak - poor feed distribution, waste accumulation
                double penalty = (WeakCurrentThreshold - currentSpeedMS) / WeakCurrentThreshold;
                return 1.0 - penalty * 0.15; // Up to 15% penalty
            }
            else if (currentSpeedMS > StrongCurrentThreshold)
            {
                // Too strong - feed washout, fish stress
                double excess = (currentSpeedMS - StrongCurrentThreshold) / 0.3; // Normalize
                return 1.0 - Math.Min(0.20, excess * 0.10); // Up to 20% penalty
            }
            else if (currentSpeedMS < OptimalCurrentMin)
            {
                // Suboptimal but acceptable
                double ratio = (currentSpeedMS - WeakCurrentThreshold) / 
                              (OptimalCurrentMin - WeakCurrentThreshold);
                return 0.85 + ratio * 0.17; // Linear interpolation to optimal
            }
            else
            {
                // Suboptimal but acceptable (above optimal max)
                double ratio = (StrongCurrentThreshold - currentSpeedMS) / 
                              (StrongCurrentThreshold - OptimalCurrentMax);
                return 0.80 + ratio * 0.22; // Linear interpolation to optimal
            }
        }

        #endregion
    }
}
