using System;
using LakskongenSim.Data;

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

        // Thread-safe random using .NET 6+ Random.Shared
        // For deterministic seeding per instance, use instance field instead
        private Random _random = Random.Shared;
        private const double AllometricExponent = 0.67; // Metabolism scaling exponent
        private const double OptimalTemperature = 11.0; // Norwegian optimal
        private const double ThermalTolerance = 3.0;    // Thermal tolerance width

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
            // Genetic variance (±15% on K, ±10% on α_max, ±20% on σ)
            double asymptoticWeightK = geneticAsymptoticWeight * (1.0 + (_random.NextDouble() - 0.5) * 0.15);
            
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

            double maxGrowthRate = geneticGrowthRate * (1.0 + (_random.NextDouble() - 0.5) * 0.1) * stageModifier;
            double variance = individualVariance * (1.0 + (_random.NextDouble() - 0.5) * 0.2);

            // Temperature-dependent growth rate: α(T) = α_max × exp(−(T − T_opt)^2 / (2τ^2))
            if (temperature < 2.0 || temperature > 18.0) return weight; // No growth outside range
            double alphaT = maxGrowthRate * Math.Exp(-Math.Pow(temperature - OptimalTemperature, 2) / (2.0 * Math.Pow(ThermalTolerance, 2)));

            // Proper SDE with time step dt = 1 day
            // Box-Muller transform for normal noise N(0,1)
            double u1 = _random.NextDouble();
            double u2 = _random.NextDouble();
            double normalNoise = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            
            double deterministicGrowth = alphaT * Math.Pow(weight, AllometricExponent) * (1.0 - weight / asymptoticWeightK);
            double stochasticGrowth = variance * Math.Sqrt(1.0) * normalNoise; // sqrt(dt) where dt=1
            double newWeight = weight + deterministicGrowth + stochasticGrowth;

            return newWeight > 0.0 ? newWeight : 0.0; // Non-negative weight
        }

        /// <summary>
        /// Apply SDE growth over multiple days (batch processing optimized).
        /// </summary>
        public double ApplySDEGrowthMultipleDays(double initialWeight, int days, double temperature, double geneticAsymptoticWeight, double geneticGrowthRate, double individualVariance, LifeStage lifeStage)
        {
            if (days <= 0) return initialWeight;
            double weight = initialWeight;
            
            // Batch processing: reuse noise samples, avoid repeated Random() calls
            // For performance: pre-generate normal noises for all days
            double[] noises = new double[days];
            for (int i = 0; i < days; i++)
            {
                double u1 = _random.NextDouble();
                double u2 = _random.NextDouble();
                noises[i] = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            }

            // Genetic parameters (fixed for this batch)
            double asymptoticWeightK = geneticAsymptoticWeight * (1.0 + (_random.NextDouble() - 0.5) * 0.15);
            double maxGrowthRate = geneticGrowthRate * (1.0 + (_random.NextDouble() - 0.5) * 0.1);
            double variance = individualVariance * (1.0 + (_random.NextDouble() - 0.5) * 0.2);
            
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
            
            maxGrowthRate *= stageModifier;

            // Temperature effect (constant over batch)
            if (temperature < 2.0 || temperature > 18.0) return weight; // No growth
            double alphaT = maxGrowthRate * Math.Exp(-Math.Pow(temperature - OptimalTemperature, 2) / (2.0 * Math.Pow(ThermalTolerance, 2)));

            for (int day = 0; day < days; day++)
            {
                double deterministic = alphaT * Math.Pow(weight, AllometricExponent) * (1.0 - weight / asymptoticWeightK);
                double stochastic = variance * noises[day]; // sqrt(dt)=1
                weight += deterministic + stochastic;
                if (weight <= 0.0) weight = 0.0;
            }
            
            return weight;
        }
    }
}
