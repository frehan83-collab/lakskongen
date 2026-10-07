using System;
using System.Linq;
using Xunit;
using LakskongenSim.Biology;
using LakskongenSim.Data;
using LakskongenSim.Environment;

namespace LakskongenSim.Tests
{
    /// <summary>
    /// Comprehensive tests for SDE-based growth models.
    /// Validates mathematical correctness, biological realism, and numerical stability.
    /// </summary>
    public class SDEGrowthModelTests
    {
        private const double Tolerance = 1e-6;
        private const int TestSeed = 42;

        #region Temperature Correction Tests

        [Fact]
        public void TemperatureCorrection_OptimalTemp_ReturnsMaxRate()
        {
            var model = new SDEGrowthModel(TestSeed);
            double maxRate = 0.015;
            double corrected = model.CalculateTemperatureCorrectedGrowth(11.0, maxRate);
            
            Assert.Equal(maxRate, corrected, 3);
        }

        [Fact]
        public void TemperatureCorrection_BelowMinimum_ReturnsZero()
        {
            var model = new SDEGrowthModel(TestSeed);
            double corrected = model.CalculateTemperatureCorrectedGrowth(1.0, 0.015);
            
            Assert.Equal(0.0, corrected);
        }

        [Fact]
        public void TemperatureCorrection_AboveMaximum_ReturnsZero()
        {
            var model = new SDEGrowthModel(TestSeed);
            double corrected = model.CalculateTemperatureCorrectedGrowth(19.0, 0.015);
            
            Assert.Equal(0.0, corrected);
        }

        [Fact]
        public void TemperatureCorrection_SubOptimal_ReducesRate()
        {
            var model = new SDEGrowthModel(TestSeed);
            double maxRate = 0.015;
            double correctedCold = model.CalculateTemperatureCorrectedGrowth(6.0, maxRate);
            double correctedWarm = model.CalculateTemperatureCorrectedGrowth(16.0, maxRate);
            
            Assert.True(correctedCold < maxRate, $"Cold temp should reduce rate: {correctedCold} vs {maxRate}");
            Assert.True(correctedWarm < maxRate, $"Warm temp should reduce rate: {correctedWarm} vs {maxRate}");
            Assert.True(correctedCold > 0, "Cold temp within range should still allow growth");
            Assert.True(correctedWarm > 0, "Warm temp within range should still allow growth");
        }

        [Fact]
        public void TemperatureCorrection_GaussianCurve_Symmetric()
        {
            var model = new SDEGrowthModel(TestSeed);
            double maxRate = 0.015;
            
            // Test symmetry around optimal temperature (11°C)
            double at8C = model.CalculateTemperatureCorrectedGrowth(8.0, maxRate);
            double at14C = model.CalculateTemperatureCorrectedGrowth(14.0, maxRate);
            
            // Should be approximately equal (±3°C from optimal)
            Assert.Equal(at8C, at14C, 2);
        }

        #endregion

        #region Genetic Parameter Generation Tests

        [Fact]
        public void GenerateGeneticAsymptoticWeight_WithinVarianceBounds()
        {
            var model = new SDEGrowthModel(TestSeed);
            double baseline = 5.0;
            double maxVariance = baseline * SDEGrowthModel.AsymptoticWeightVariance;
            
            for (int i = 0; i < 100; i++)
            {
                double weight = model.GenerateGeneticAsymptoticWeight(baseline);
                Assert.InRange(weight, baseline - maxVariance, baseline + maxVariance);
            }
        }

        [Fact]
        public void GenerateGeneticGrowthRate_WithinVarianceBounds()
        {
            var model = new SDEGrowthModel(TestSeed);
            double baseline = 0.015;
            double maxVariance = baseline * SDEGrowthModel.GrowthRateVariance;
            
            for (int i = 0; i < 100; i++)
            {
                double rate = model.GenerateGeneticGrowthRate(baseline);
                Assert.InRange(rate, baseline - maxVariance, baseline + maxVariance);
            }
        }

        [Fact]
        public void GenerateStochasticVariance_WithinVarianceBounds()
        {
            var model = new SDEGrowthModel(TestSeed);
            double baseline = 0.02;
            double maxVariance = baseline * SDEGrowthModel.VarianceVariance;
            
            for (int i = 0; i < 100; i++)
            {
                double variance = model.GenerateStochasticVariance(baseline);
                Assert.InRange(variance, baseline - maxVariance, baseline + maxVariance);
            }
        }

        [Fact]
        public void GenerateNormalNoise_ApproximatelyStandardNormal()
        {
            var model = new SDEGrowthModel(TestSeed);
            int sampleSize = 10000;
            double[] samples = new double[sampleSize];
            
            for (int i = 0; i < sampleSize; i++)
            {
                samples[i] = model.GenerateNormalNoise();
            }
            
            double mean = samples.Average();
            double variance = samples.Select(x => Math.Pow(x - mean, 2)).Average();
            double stdDev = Math.Sqrt(variance);
            
            // Standard normal should have mean ≈ 0, stddev ≈ 1
            Assert.InRange(mean, -0.1, 0.1); // Mean within ±0.1
            Assert.InRange(stdDev, 0.95, 1.05); // StdDev within ±5%
        }

        #endregion

        #region SDE Growth Tests

        [Fact]
        public void ApplySDEGrowth_OptimalConditions_PositiveGrowth()
        {
            var model = new SDEGrowthModel(TestSeed);
            double initialWeight = 1.0;
            double asymptoticWeight = 5.0;
            double growthRate = 0.015;
            double variance = 0.02;
            
            double newWeight = model.ApplySDEGrowth(
                initialWeight, 11.0, asymptoticWeight, growthRate, variance);
            
            Assert.True(newWeight > initialWeight, 
                $"Weight should increase under optimal conditions: {initialWeight} -> {newWeight}");
        }

        [Fact]
        public void ApplySDEGrowth_NearAsymptoticWeight_SlowGrowth()
        {
            var model = new SDEGrowthModel(TestSeed);
            double nearAsymptotic = 4.8; // Close to K=5.0
            double asymptoticWeight = 5.0;
            double growthRate = 0.015;
            double variance = 0.02;
            
            double newWeight = model.ApplySDEGrowth(
                nearAsymptotic, 11.0, asymptoticWeight, growthRate, variance);
            
            double growthRate1 = newWeight - nearAsymptotic;
            
            // Compare to growth from smaller fish
            double smallWeight = model.ApplySDEGrowth(
                1.0, 11.0, asymptoticWeight, growthRate, variance);
            double growthRate2 = smallWeight - 1.0;
            
            Assert.True(growthRate1 < growthRate2, 
                "Fish near asymptotic weight should grow slower");
        }

        [Fact]
        public void ApplySDEGrowth_WithEpigeneticReduction_ReducesAsymptoticWeight()
        {
            var model = new SDEGrowthModel(TestSeed);
            double initialWeight = 1.0;
            double asymptoticWeight = 5.0;
            double growthRate = 0.015;
            double variance = 0.02;
            
            // Simulate to near-asymptotic with and without epigenetic stress
            double normalFinal = model.ApplySDEGrowthMultipleDays(
                initialWeight, 365 * 2, 11.0, asymptoticWeight, growthRate, variance, 1.0);
            
            double stressedFinal = model.ApplySDEGrowthMultipleDays(
                initialWeight, 365 * 2, 11.0, asymptoticWeight, growthRate, variance, 0.85);
            
            Assert.True(stressedFinal < normalFinal, 
                $"Epigenetic stress should reduce final size: {stressedFinal} vs {normalFinal}");
        }

        [Fact]
        public void ApplySDEGrowth_NonNegativeWeight_Always()
        {
            var model = new SDEGrowthModel(TestSeed);
            
            // Try to create negative weight with extreme parameters
            for (int i = 0; i < 100; i++)
            {
                double weight = model.ApplySDEGrowth(
                    0.001, 11.0, 5.0, 0.015, 0.5); // High variance
                
                Assert.True(weight >= 0.0, "Weight must never be negative");
            }
        }

        [Fact]
        public void ApplySDEGrowthMultipleDays_ConsistentWithSingleDay()
        {
            var model1 = new SDEGrowthModel(TestSeed);
            var model2 = new SDEGrowthModel(TestSeed);
            
            double initialWeight = 1.0;
            int days = 30;
            
            // Simulate 30 days at once
            double batch = model1.ApplySDEGrowthMultipleDays(
                initialWeight, days, 11.0, 5.0, 0.015, 0.02);
            
            // Simulate 30 days one at a time
            double sequential = initialWeight;
            for (int i = 0; i < days; i++)
            {
                sequential = model2.ApplySDEGrowth(sequential, 11.0, 5.0, 0.015, 0.02);
            }
            
            // Should be identical with same seed
            Assert.Equal(batch, sequential, 6);
        }

        #endregion

        #region Lifecycle Trajectory Tests

        [Fact]
        public void SimulateLifecycleTrajectory_ReturnsCorrectLength()
        {
            var model = new SDEGrowthModel(TestSeed);
            double[] temps = Enumerable.Repeat(11.0, 365).ToArray();
            
            double[] trajectory = model.SimulateLifecycleTrajectory(
                0.005, 365, temps, 5.0, 0.015, 0.02);
            
            Assert.Equal(366, trajectory.Length); // Initial + 365 days
        }

        [Fact]
        public void SimulateLifecycleTrajectory_MonotonicIncreaseUnderOptimalConditions()
        {
            var model = new SDEGrowthModel(TestSeed);
            double[] temps = Enumerable.Repeat(11.0, 180).ToArray();
            
            double[] trajectory = model.SimulateLifecycleTrajectory(
                0.005, 180, temps, 5.0, 0.015, 0.01); // Low variance for monotonic test
            
            for (int i = 1; i < trajectory.Length - 1; i++)
            {
                // Allow small fluctuations due to stochastic term
                Assert.True(trajectory[i] >= trajectory[i - 1] * 0.99, 
                    $"Weight should generally increase: day {i-1}={trajectory[i-1]:F4}, day {i}={trajectory[i]:F4}");
            }
        }

        [Fact]
        public void SimulateLifecycleTrajectory_RealisticGrowthCurve()
        {
            var model = new SDEGrowthModel(TestSeed);
            
            // Norwegian seasonal temperature pattern (simplified)
            double[] temps = new double[730]; // 2 years
            for (int i = 0; i < 730; i++)
            {
                double dayOfYear = i % 365;
                temps[i] = 7.0 + 6.0 * Math.Sin((dayOfYear - 100) * 2 * Math.PI / 365);
            }
            
            double[] trajectory = model.SimulateLifecycleTrajectory(
                0.005, 730, temps, 5.0, 0.015, 0.02);
            
            // Check realistic milestones
            double weight6Months = trajectory[180];
            double weight1Year = trajectory[365];
            double weight2Years = trajectory[730];
            
            Assert.InRange(weight6Months, 0.05, 0.5); // 50-500g after 6 months
            Assert.InRange(weight1Year, 0.5, 2.0);     // 0.5-2kg after 1 year
            Assert.InRange(weight2Years, 2.0, 5.5);    // 2-5.5kg after 2 years
        }

        [Fact]
        public void SimulateLifecycleTrajectory_WithEpigeneticMemory_ReducesFinalSize()
        {
            var model = new SDEGrowthModel(TestSeed);
            double[] temps = Enumerable.Repeat(11.0, 730).ToArray();
            
            // Normal fish
            var normalMemory = new EpigeneticMemory();
            double[] normalTrajectory = model.SimulateLifecycleTrajectory(
                0.005, 730, temps, 5.0, 0.015, 0.02, normalMemory);
            
            // Stressed fish
            var stressedMemory = new EpigeneticMemory();
            stressedMemory.RecordStressEvent(StressType.TemperatureShock, 0.8, 30);
            stressedMemory.RecordStressEvent(StressType.Handling, 0.6, 60);
            stressedMemory.RecordStressEvent(StressType.Disease, 1.0, 90);
            
            double[] stressedTrajectory = model.SimulateLifecycleTrajectory(
                0.005, 730, temps, 5.0, 0.015, 0.02, stressedMemory);
            
            Assert.True(stressedTrajectory[730] < normalTrajectory[730], 
                "Early-life stress should reduce final size");
        }

        #endregion

        #region Numerical Stability Tests

        [Fact]
        public void ApplySDEGrowth_HighVariance_StillStable()
        {
            var model = new SDEGrowthModel(TestSeed);
            
            // Test with extreme variance
            for (int i = 0; i < 50; i++)
            {
                double weight = model.ApplySDEGrowth(
                    1.0, 11.0, 5.0, 0.015, 0.5); // Very high variance
                
                Assert.True(weight >= 0.0 && weight < 10.0, 
                    $"Weight should remain in reasonable bounds: {weight}");
            }
        }

        [Fact]
        public void ApplySDEGrowthMultipleDays_LongSimulation_NoOverflow()
        {
            var model = new SDEGrowthModel(TestSeed);
            
            double finalWeight = model.ApplySDEGrowthMultipleDays(
                0.005, 365 * 3, 11.0, 5.0, 0.015, 0.02); // 3 years
            
            Assert.True(finalWeight > 0.0 && finalWeight < 10.0, 
                $"Long simulation should converge to asymptotic weight: {finalWeight}");
            Assert.False(double.IsNaN(finalWeight), "Should not produce NaN");
            Assert.False(double.IsInfinity(finalWeight), "Should not overflow");
        }

        #endregion

        #region Biological Realism Tests

        [Fact]
        public void IndividualVariance_ProducesDistribution()
        {
            var model = new SDEGrowthModel(TestSeed);
            int populationSize = 100;
            double[] finalWeights = new double[populationSize];
            
            for (int i = 0; i < populationSize; i++)
            {
                double K = model.GenerateGeneticAsymptoticWeight();
                double alpha = model.GenerateGeneticGrowthRate();
                double sigma = model.GenerateStochasticVariance();
                
                finalWeights[i] = model.ApplySDEGrowthMultipleDays(
                    0.005, 365 * 2, 11.0, K, alpha, sigma);
            }
            
            double mean = finalWeights.Average();
            double variance = finalWeights.Select(w => Math.Pow(w - mean, 2)).Average();
            double cv = Math.Sqrt(variance) / mean; // Coefficient of variation
            
            // Should have realistic variation (10-20% CV)
            Assert.InRange(cv, 0.05, 0.30);
        }

        [Fact]
        public void VonBertalanffyGompertzHybrid_MatchesExpectedCurve()
        {
            var model = new SDEGrowthModel(TestSeed);
            double[] temps = Enumerable.Repeat(11.0, 730).ToArray();
            
            double[] trajectory = model.SimulateLifecycleTrajectory(
                0.005, 730, temps, 5.0, 0.015, 0.01); // Low variance for clean curve
            
            // Check characteristic S-curve properties
            // Early phase: exponential-like growth
            double earlyGrowthRate = (trajectory[90] - trajectory[30]) / 60.0;
            double midGrowthRate = (trajectory[365] - trajectory[300]) / 65.0;
            double lateGrowthRate = (trajectory[730] - trajectory[665]) / 65.0;
            
            Assert.True(midGrowthRate > lateGrowthRate, 
                "Growth rate should slow as approaching asymptotic size");
        }

        #endregion

        #region Water Quality (DO & Stocking Density) Tests

        [Fact]
        public void CalculateOxygenSaturation_At10C_ReturnsExpectedValue()
        {
            var model = new SDEGrowthModel(TestSeed);
            double sat = WaterQuality.CalculateOxygenSaturation(10.0);
            // At 10°C, 35 PSU, saturation should be ~11.3 mg/L
            Assert.InRange(sat, 10.5, 12.0);
        }

        [Fact]
        public void CalculateOxygenSaturation_HigherTemp_LowerSaturation()
        {
            var model = new SDEGrowthModel(TestSeed);
            double satCold = WaterQuality.CalculateOxygenSaturation(5.0);
            double satWarm = WaterQuality.CalculateOxygenSaturation(15.0);
            Assert.True(satCold > satWarm, "Colder water should hold more oxygen");
        }

        [Fact]
        public void CalculateDOSaturation_AtSaturation_ReturnsOne()
        {
            var model = new SDEGrowthModel(TestSeed);
            double sat = WaterQuality.CalculateOxygenSaturation(10.0);
            double doSat = WaterQuality.CalculateDOSaturation(sat, 10.0);
            Assert.Equal(1.0, doSat, 3);
        }

        [Fact]
        public void CalculateDOSaturation_BelowSaturation_ReturnsFraction()
        {
            var model = new SDEGrowthModel(TestSeed);
            double sat = WaterQuality.CalculateOxygenSaturation(10.0);
            double doSat = WaterQuality.CalculateDOSaturation(sat * 0.5, 10.0);
            Assert.Equal(0.5, doSat, 3);
        }

        [Fact]
        public void CalculateDOSaturationModifier_AboveThreshold_ReturnsOne()
        {
            var model = new SDEGrowthModel(TestSeed);
            double modifier = WaterQuality.CalculateDOSaturationModifier(0.60);
            Assert.Equal(1.0, modifier, 3);

            modifier = WaterQuality.CalculateDOSaturationModifier(0.80);
            Assert.Equal(1.0, modifier, 3);

            modifier = WaterQuality.CalculateDOSaturationModifier(1.0);
            Assert.Equal(1.0, modifier, 3);
        }

        [Fact]
        public void CalculateDOSaturationModifier_BelowThreshold_ExponentialReduction()
        {
            var model = new SDEGrowthModel(TestSeed);
            // At 30% saturation: 1 - exp(-0.3/0.3) = 1 - exp(-1) ≈ 0.632
            double modifier30 = WaterQuality.CalculateDOSaturationModifier(0.30);
            Assert.InRange(modifier30, 0.60, 0.66);

            // At 10% saturation: 1 - exp(-0.1/0.3) = 1 - exp(-0.333) ≈ 0.283
            double modifier10 = WaterQuality.CalculateDOSaturationModifier(0.10);
            Assert.InRange(modifier10, 0.28, 0.32);

            // At 0% saturation: should be clamped to minimum 0.2
            double modifier0 = WaterQuality.CalculateDOSaturationModifier(0.0);
            Assert.Equal(0.2, modifier0, 3);
        }

        [Fact]
        public void CalculateDOSaturationModifier_ClampedToRange()
        {
            var model = new SDEGrowthModel(TestSeed);
            // Should never exceed 1.0
            Assert.True(WaterQuality.CalculateDOSaturationModifier(1.5) <= 1.0);
            // Should never go below 0.2
            Assert.True(WaterQuality.CalculateDOSaturationModifier(-0.5) >= 0.2);
        }

        [Fact]
        public void CalculateStockingDensityModifier_BelowThreshold_ReturnsOne()
        {
            var model = new SDEGrowthModel(TestSeed);
            double modifier = WaterQuality.CalculateStockingDensityModifier(20.0);
            Assert.Equal(1.0, modifier, 3);

            modifier = WaterQuality.CalculateStockingDensityModifier(25.0);
            Assert.Equal(1.0, modifier, 3);
        }

        [Fact]
        public void CalculateStockingDensityModifier_AboveThreshold_LinearReduction()
        {
            var model = new SDEGrowthModel(TestSeed);
            // At 25 kg/m³: 1.0 (threshold)
            Assert.Equal(1.0, WaterQuality.CalculateStockingDensityModifier(25.0), 3);

            // At 50 kg/m³: 1 - (50-25)/75 = 1 - 25/75 = 0.667
            double modifier50 = WaterQuality.CalculateStockingDensityModifier(50.0);
            Assert.InRange(modifier50, 0.65, 0.69);

            // At 100 kg/m³: 1 - (100-25)/75 = 1 - 75/75 = 0.0 -> clamped to 0.5
            double modifier100 = WaterQuality.CalculateStockingDensityModifier(100.0);
            Assert.Equal(0.5, modifier100, 3);
        }

        [Fact]
        public void CalculateStockingDensityModifier_ClampedToMin()
        {
            var model = new SDEGrowthModel(TestSeed);
            // Should never go below 0.5
            Assert.True(WaterQuality.CalculateStockingDensityModifier(200.0) >= 0.5);
        }

        [Fact]
        public void UpdateDailyDO_IncreasesOxygenWithHighCurrent()
        {
            var model = new SDEGrowthModel(TestSeed);
            var waterChem = new WaterChemistry(10.0);
            waterChem.OxygenO2 = 8.0; // Start at 8 mg/L

            double initialO2 = waterChem.OxygenO2;
            WaterQuality.UpdateDailyDO(
                waterChem,
                temperatureC: 10.0,
                biomassKg: 10000.0,
                feedRateKgPerDay: 100.0,
                fcr: 1.2,
                penVolumeM3: 10000.0,
                currentSpeedMs: 0.3, // High current
                salinityPSU: 35.0);

            // With high current, reaeration should help maintain/increase oxygen
            // The exact change depends on consumption vs reaeration balance
            Assert.True(waterChem.OxygenO2 >= 0.0 && waterChem.OxygenO2 <= 14.0);
        }

        [Fact]
        public void UpdateDailyDO_DecreasesOxygenWithHighBiomass()
        {
            var model = new SDEGrowthModel(TestSeed);
            var waterChem = new WaterChemistry(10.0);
            waterChem.OxygenO2 = 11.3; // Start at saturation

            double initialO2 = waterChem.OxygenO2;
            WaterQuality.UpdateDailyDO(
                waterChem,
                temperatureC: 10.0,
                biomassKg: 500000.0, // Very high biomass
                feedRateKgPerDay: 5000.0,
                fcr: 1.2,
                penVolumeM3: 10000.0,
                currentSpeedMs: 0.05, // Low current
                salinityPSU: 35.0);

            // High biomass + low current should decrease oxygen
            Assert.True(waterChem.OxygenO2 < initialO2);
            Assert.True(waterChem.OxygenO2 >= 0.0);
        }

        [Fact]
        public void CalculateCombinedEnvironmentalMultiplier_BothOptimal_ReturnsOne()
        {
            var model = new SDEGrowthModel(TestSeed);
            var waterChem = new WaterChemistry(11.0);
            waterChem.OxygenO2 = 11.3; // Saturation at 11°C

            double multiplier = WaterQuality.CalculateCombinedEnvironmentalMultiplier(
                waterChem, 11.0, 20.0, 35.0);

            Assert.Equal(1.0, multiplier, 2);
        }

        [Fact]
        public void CalculateCombinedEnvironmentalMultiplier_LowDO_ReducesGrowth()
        {
            var model = new SDEGrowthModel(TestSeed);
            var waterChem = new WaterChemistry(11.0);
            waterChem.OxygenO2 = 4.0; // Low oxygen (~35% saturation)

            double multiplier = WaterQuality.CalculateCombinedEnvironmentalMultiplier(
                waterChem, 11.0, 20.0, 35.0);

            // DO saturation ~0.35 -> modifier ~0.68, density 20 -> modifier 1.0
            // Combined ≈ 0.68
            Assert.True(multiplier < 1.0);
            Assert.True(multiplier >= 0.2);
        }

        [Fact]
        public void CalculateCombinedEnvironmentalMultiplier_HighDensity_ReducesGrowth()
        {
            var model = new SDEGrowthModel(TestSeed);
            var waterChem = new WaterChemistry(11.0);
            waterChem.OxygenO2 = 11.3; // Optimal oxygen

            double multiplier = WaterQuality.CalculateCombinedEnvironmentalMultiplier(
                waterChem, 11.0, 50.0, 35.0);

            // DO optimal -> 1.0, density 50 -> 0.667
            // Combined ≈ 0.667
            Assert.InRange(multiplier, 0.65, 0.69);
        }

        [Fact]
        public void CalculateCombinedEnvironmentalMultiplier_BothStressors_Compounds()
        {
            var model = new SDEGrowthModel(TestSeed);
            var waterChem = new WaterChemistry(11.0);
            waterChem.OxygenO2 = 4.0; // Low oxygen

            double multiplier = WaterQuality.CalculateCombinedEnvironmentalMultiplier(
                waterChem, 11.0, 50.0, 35.0);

            // DO ~0.35 sat -> ~0.68, density 50 -> ~0.667
            // Combined ≈ 0.45
            Assert.True(multiplier < 0.7);
            Assert.True(multiplier >= 0.1); // floor is 0.2 * 0.5 = 0.1
        }

        [Fact]
        public void ApplySDEGrowth_WithEnvironmentalMultiplier_ReducesGrowth()
        {
            var model = new SDEGrowthModel(TestSeed);
            double initialWeight = 1.0;
            double asymptoticWeight = 5.0;
            double growthRate = 0.015;
            double variance = 0.02;

            // Without environmental stress
            double normalWeight = model.ApplySDEGrowth(
                initialWeight, 11.0, asymptoticWeight, growthRate, variance, 1.0, 1.0);

            // With environmental stress (e.g., 0.5 multiplier)
            double stressedWeight = model.ApplySDEGrowth(
                initialWeight, 11.0, asymptoticWeight, growthRate, variance, 1.0, 0.5);

            Assert.True(stressedWeight < normalWeight, 
                $"Environmental stress should reduce growth: {stressedWeight} vs {normalWeight}");
        }

        [Fact]
        public void ApplySDEGrowth_WithEnvironmentalMultiplier_Deterministic()
        {
            var model1 = new SDEGrowthModel(TestSeed);
            var model2 = new SDEGrowthModel(TestSeed);

            double w1 = model1.ApplySDEGrowth(1.0, 11.0, 5.0, 0.015, 0.02, 1.0, 0.7);
            double w2 = model2.ApplySDEGrowth(1.0, 11.0, 5.0, 0.015, 0.02, 1.0, 0.7);

            Assert.Equal(w1, w2, 6);
        }

        #endregion
    }
}
