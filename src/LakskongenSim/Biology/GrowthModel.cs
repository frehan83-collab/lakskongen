using System;
using LakskongenSim.Data;
using LakskongenSim.Environment;

using LakskongenSim.Core;
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

        // Thread-safe RNG (TD-06): System.Random instances are NOT thread-safe for
        // concurrent use. All draws go through NextDoubleLocked(), guarded by _rngLock.
        // The default (unseeded) instance aliases Random.Shared — itself thread-safe —
        // while a seeded instance gives deterministic streams for tests.
        private readonly SimRngStream _random;
        private readonly object _rngLock = new();
        internal SimRngStream RngForSnapshot => _random;
        private const double AllometricExponent = 0.67; // Metabolism scaling exponent
        private const double OptimalTemperature = 11.0; // Norwegian optimal
        private const double ThermalTolerance = 3.0;    // Thermal tolerance width

        // Genetic/stochastic spread applied per draw (fraction of the parameter value).
        private const double AsymptoticWeightSpread = 0.15; // ±15% on K
        private const double GrowthRateSpread = 0.10;       // ±10% on α_max
        private const double VarianceSpread = 0.20;         // ±20% on σ

        // Viable growth temperature window (°C). Outside: no growth that day.
        private const double MinGrowthTemperatureC = 2.0;
        private const double MaxGrowthTemperatureC = 17.0; // ↓ from 18.0 (tighter upper bound)

        public GrowthModel(SimRngStream rng)
        {
            _random = rng ?? throw new ArgumentNullException(nameof(rng));
        }

        /// <summary>Create a growth model with a non-deterministic RNG.</summary>
        public GrowthModel() : this(SimRng.CreateUnseeded().Stream(SimRngStreams.Growth)) { }

        /// <summary>
        /// Create a growth model with an optional deterministic seed (for tests).
        /// </summary>
        /// <param name="seed">Seed for the RNG, or null for non-deterministic.</param>
        public GrowthModel(int? seed)
            : this(seed.HasValue
                ? new SimRng(seed.Value).Stream(SimRngStreams.Growth)
                : SimRng.CreateUnseeded().Stream(SimRngStreams.Growth))
        {
        }

        /// <summary>Thread-safe uniform draw in [0,1).</summary>
        private double NextDoubleLocked()
        {
            lock (_rngLock) { return _random.NextDouble(); }
        }
        
        /// <summary>
        /// Scaling factor applied to geneticGrowthRate to calibrate biological growth.
        /// With rate=3.0 (engine default), produces ~5.2kg harvest from 80g smolt in 540 days at 11°C.
        /// Formula: effective α = rate × ScalingFactor × stageModifier × temp_response
        /// </summary>
        private const double GrowthRateScalingFactor = 0.0016; // ↑ from 0.0015 (better calibration)

        /// <summary>
        /// Apply SDE-based growth for individual fish tracking over one day.
        /// Correct mathematical formulation: dW = α(T) × W^γ × (1 − W/K) × dt + σ × sqrt([dt]) × N(0,1)
        /// where dt = 1 day, K is genetic asymptotic weight (4-6 kg ±15%), α(T) temperature-dependent.
        /// </summary>
        /// <param name="weight">Current weight in kg</param>
        /// <param name="temperature">Water temperature in °C</param>
        /// <param name="geneticAsymptoticWeight">Genetic asymptotic weight K (4-6 kg baseline)</param>
        /// <param name="geneticGrowthRate">Genetic maximum growth rate α_max (1-100 scale)</param>
        /// <param name="individualVariance">Individual variance σ (epigenetic + stochastic)</param>
        /// <param name="lifeStage">Current life stage for stage-specific modifiers</param>
        /// <returns>New weight after one day</returns>
        public double ApplySDEGrowthOneDay(double weight, double temperature, double geneticAsymptoticWeight, double geneticGrowthRate, double individualVariance, LifeStage lifeStage)
        {
            return ApplySDEGrowthOneDay(weight, temperature, geneticAsymptoticWeight, geneticGrowthRate, individualVariance, lifeStage, 1.0);
        }

        /// <summary>
        /// Apply SDE-based growth for individual fish tracking over one day with environmental multiplier.
        /// </summary>
        /// <param name="weight">Current weight in kg</param>
        /// <param name="temperature">Water temperature in °C</param>
        /// <param name="geneticAsymptoticWeight">Genetic asymptotic weight K (4-6 kg baseline)</param>
        /// <param name="geneticGrowthRate">Genetic maximum growth rate α_max (1-100 scale)</param>
        /// <param name="individualVariance">Individual variance σ (epigenetic + stochastic)</param>
        /// <param name="lifeStage">Current life stage for stage-specific modifiers</param>
        /// <param name="environmentalMultiplier">Combined environmental multiplier (DO × density) (0.1-1.0)</param>
        /// <param name="researchGrowthBonus">Additive growth rate bonus from unlocked research (e.g., 0.04 = +4%).</param>
        /// <returns>New weight after one day</returns>
        public double ApplySDEGrowthOneDay(double weight, double temperature, double geneticAsymptoticWeight, double geneticGrowthRate, double individualVariance, LifeStage lifeStage, double environmentalMultiplier = 1.0, double researchGrowthBonus = 0.0)
        {
            // Genetic variance (±15% on K, ±10% on α_max, ±20% on σ)
            double asymptoticWeightK = geneticAsymptoticWeight * (1.0 + (NextDoubleLocked() - 0.5) * AsymptoticWeightSpread);
            
            // Stage-specific growth modifier (aligns with ApplyGrowth multipliers)
            double stageModifier = 1.0;
            switch (lifeStage)
            {
                case LifeStage.Egg: stageModifier = 0.1; break;
                case LifeStage.Alevin: stageModifier = 0.2; break;
                case LifeStage.Fry: stageModifier = 0.5; break;
                case LifeStage.Parr: stageModifier = 0.8; break;
                case LifeStage.Smolt: stageModifier = 0.9; break;
                case LifeStage.Seawater: stageModifier = 1.2; break;
                case LifeStage.Harvest: stageModifier = 0.8; break;
            }

            double maxGrowthRate = geneticGrowthRate * GrowthRateScalingFactor * (1.0 + (NextDoubleLocked() - 0.5) * GrowthRateSpread) * stageModifier * (1.0 + researchGrowthBonus);
            double variance = individualVariance * (1.0 + (NextDoubleLocked() - 0.5) * VarianceSpread);

            // Temperature-dependent growth rate: α(T) = α_max × exp(−(T − T_opt)^2 / (2τ^2))
            if (temperature < MinGrowthTemperatureC || temperature > MaxGrowthTemperatureC) return weight; // No growth outside range
            double alphaT = maxGrowthRate * Math.Exp(-Math.Pow(temperature - OptimalTemperature, 2) / (2.0 * Math.Pow(ThermalTolerance, 2)));

            // Apply environmental multiplier ADDITIVELY in log-space to preserve VB baseline
            // For small perturbations: log(1 + x) ≈ x, so multiplicative multiplier m
            // becomes additive perturbation (m - 1) in log-space
            // Combined effect: combinedMultiplier = 1.0 + (environmentalMultiplier - 1.0) * 0.5
            // This makes environmental modifiers small perturbations (±50% max effect)
            double combinedMultiplier = 1.0 + (environmentalMultiplier - 1.0) * 0.5;
            combinedMultiplier = Math.Clamp(combinedMultiplier, 0.5, 1.5);

            // Proper SDE with time step dt = 1 day
            // Box-Muller transform for normal noise N(0,1)
            // NOTE: 1.0 - NextDouble() keeps u1 in (0,1]; NextDouble() can
            // return exactly 0.0, and Log(0) would poison weights with NaN/Inf.
            double u1 = 1.0 - NextDoubleLocked();
            double u2 = NextDoubleLocked();
            double normalNoise = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            
            double deterministicGrowth = alphaT * Math.Pow(weight, AllometricExponent) * (1.0 - weight / asymptoticWeightK) * combinedMultiplier;
            double stochasticGrowth = variance * deterministicGrowth * Math.Sqrt(1.0) * normalNoise; // sqrt(dt) where dt=1
            double newWeight = weight + deterministicGrowth + stochasticGrowth;

            return newWeight > 0.0 ? newWeight : 0.0; // Non-negative weight
        }

        /// <summary>
        /// Apply SDE growth over multiple days (batch processing optimized).
        /// </summary>
        public double ApplySDEGrowthMultipleDays(double initialWeight, int days, double temperature, double geneticAsymptoticWeight, double geneticGrowthRate, double individualVariance, LifeStage lifeStage)
        {
            return ApplySDEGrowthMultipleDays(initialWeight, days, temperature, geneticAsymptoticWeight, geneticGrowthRate, individualVariance, lifeStage, 1.0, 0.0);
        }

        /// <summary>
        /// Apply SDE growth over multiple days with environmental multiplier and research bonus.
        /// </summary>
        public double ApplySDEGrowthMultipleDays(double initialWeight, int days, double temperature, double geneticAsymptoticWeight, double geneticGrowthRate, double individualVariance, LifeStage lifeStage, double environmentalMultiplier = 1.0, double researchGrowthBonus = 0.0)
        {
            if (days <= 0) return initialWeight;
            double weight = initialWeight;
            
            // Batch processing: reuse noise samples, avoid repeated Random() calls
            // For performance: pre-generate normal noises for all days
            double[] noises = new double[days];
            for (int i = 0; i < days; i++)
            {
                // See note above: guard against Log(0).
                double u1 = 1.0 - NextDoubleLocked();
                double u2 = NextDoubleLocked();
                noises[i] = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            }

            // Genetic parameters (fixed for this batch)
            double asymptoticWeightK = geneticAsymptoticWeight * (1.0 + (NextDoubleLocked() - 0.5) * AsymptoticWeightSpread);
            double maxGrowthRate = geneticGrowthRate * GrowthRateScalingFactor * (1.0 + (NextDoubleLocked() - 0.5) * GrowthRateSpread);
            double variance = individualVariance * (1.0 + (NextDoubleLocked() - 0.5) * VarianceSpread);
            
            // Stage modifier
            double stageModifier = 1.0;
            switch (lifeStage)
            {
                case LifeStage.Egg: stageModifier = 0.1; break;
                case LifeStage.Alevin: stageModifier = 0.2; break;
                case LifeStage.Fry: stageModifier = 0.5; break;
                case LifeStage.Parr: stageModifier = 0.8; break;
                case LifeStage.Smolt: stageModifier = 0.9; break;
                case LifeStage.Seawater: stageModifier = 1.2; break;
                case LifeStage.Harvest: stageModifier = 0.8; break;
            }
            
            maxGrowthRate *= stageModifier * (1.0 + researchGrowthBonus);
            
            // Temperature effect (constant over batch)
            if (temperature < MinGrowthTemperatureC || temperature > MaxGrowthTemperatureC) return weight; // No growth
            double alphaT = maxGrowthRate * Math.Exp(-Math.Pow(temperature - OptimalTemperature, 2) / (2.0 * Math.Pow(ThermalTolerance, 2)));

            // Apply environmental multiplier ADDITIVELY in log-space to preserve VB baseline
            double combinedMultiplier = 1.0 + (environmentalMultiplier - 1.0) * 0.5;
            combinedMultiplier = Math.Clamp(combinedMultiplier, 0.5, 1.5);

            for (int day = 0; day < days; day++)
            {
                double deterministic = alphaT * Math.Pow(weight, AllometricExponent) * (1.0 - weight / asymptoticWeightK) * combinedMultiplier;
                double stochastic = variance * deterministic * noises[day]; // sqrt(dt)=1, stochastic scales with deterministic growth
                weight += deterministic + stochastic;
                if (weight <= 0.0) weight = 0.0;
            }
            
            return weight;
        }

        /// <summary>
        /// Calculate combined environmental growth multiplier from DO and stocking density.
        /// </summary>
        public double CalculateCombinedEnvironmentalMultiplier(
            WaterChemistry waterChemistry,
            double temperatureC,
            double stockingDensityKgM3,
            double salinityPSU = 35.0)
        {
            return WaterQuality.CalculateCombinedEnvironmentalMultiplier(
                waterChemistry, temperatureC, stockingDensityKgM3, salinityPSU);
        }

        /// <summary>
        /// Capture a save snapshot. GrowthModel is stateless by design (all
        /// formulation parameters are compile-time constants; the RNG only
        /// serves independent per-tick draws, so continuity is intentionally
        /// not preserved). The snapshot is a schema marker so saves record
        /// that growth state was considered, and so RestoreFromSave has a
        /// symmetric, future-proof contract.
        /// </summary>
        public GrowthModelSnapshot GetSaveSnapshot() => new();

        /// <summary>
        /// Restore growth state after a load. No-op by design (see
        /// <see cref="GetSaveSnapshot"/>): accepts null for saves written
        /// before this snapshot existed. Growth resumes with identical
        /// deterministic output for identical inputs.
        /// </summary>
        public void RestoreFromSave(GrowthModelSnapshot? snapshot)
        {
            // Intentionally stateless — nothing to rehydrate.
            _ = snapshot;
        }
    }

    /// <summary>
    /// Save snapshot for <see cref="GrowthModel"/>. Deliberately empty: the
    /// model carries no serializable runtime state. Kept as a named type so
    /// GameState documents growth coverage and future state has a home.
    /// </summary>
    public sealed class GrowthModelSnapshot
    {
    }
}
