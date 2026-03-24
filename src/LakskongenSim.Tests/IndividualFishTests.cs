using System;
using System.Linq;
using Xunit;
using LakskongenSim.Biology;
using LakskongenSim.Data;

namespace LakskongenSim.Tests
{
    /// <summary>
    /// Tests for individual fish tracking with unique genetic parameters
    /// and epigenetic memory.
    /// </summary>
    public class IndividualFishTests
    {
        private const int TestSeed = 42;

        #region Initialization Tests

        [Fact]
        public void Constructor_CreatesUniqueID()
        {
            var sdeModel = new SDEGrowthModel(TestSeed);
            var fish1 = new IndividualFish(sdeModel);
            var fish2 = new IndividualFish(sdeModel);

            Assert.NotEqual(fish1.Id, fish2.Id);
        }

        [Fact]
        public void Constructor_SetsInitialState()
        {
            var sdeModel = new SDEGrowthModel(TestSeed);
            var fish = new IndividualFish(sdeModel, 0.010, LifeStage.Parr);

            Assert.Equal(0.010, fish.Weight, 6);
            Assert.Equal(LifeStage.Parr, fish.LifeStage);
            Assert.Equal(0, fish.AgeDays);
            Assert.True(fish.IsAlive);
            Assert.Null(fish.DiedAt);
        }

        [Fact]
        public void Constructor_GeneratesUniqueGeneticParameters()
        {
            var sdeModel = new SDEGrowthModel(TestSeed);
            var fish1 = new IndividualFish(sdeModel);
            var fish2 = new IndividualFish(sdeModel);

            // Genetic parameters should vary between individuals
            bool hasDifference = 
                fish1.GeneticAsymptoticWeight != fish2.GeneticAsymptoticWeight ||
                fish1.GeneticGrowthRate != fish2.GeneticGrowthRate ||
                fish1.StochasticVariance != fish2.StochasticVariance;

            Assert.True(hasDifference, "Individual fish should have unique genetics");
        }

        [Fact]
        public void Constructor_GeneticParametersWithinExpectedRanges()
        {
            var sdeModel = new SDEGrowthModel(TestSeed);
            var fish = new IndividualFish(sdeModel);

            // Asymptotic weight: 5.0 ± 15% = 4.25 to 5.75 kg
            Assert.InRange(fish.GeneticAsymptoticWeight, 4.0, 6.0);
            
            // Growth rate: 0.015 ± 10% = 0.0135 to 0.0165
            Assert.InRange(fish.GeneticGrowthRate, 0.012, 0.018);
            
            // Variance: 0.02 ± 20% = 0.016 to 0.024
            Assert.InRange(fish.StochasticVariance, 0.015, 0.025);
        }

        [Fact]
        public void Constructor_InitializesEpigeneticMemory()
        {
            var sdeModel = new SDEGrowthModel(TestSeed);
            var fish = new IndividualFish(sdeModel);

            Assert.NotNull(fish.EpigeneticMemory);
            Assert.Equal(1.0, fish.EpigeneticMemory.AsymptoticMultiplier);
            Assert.Equal(fish.GeneticAsymptoticWeight, fish.EffectiveAsymptoticWeight);
        }

        #endregion

        #region Growth Tests

        [Fact]
        public void GrowOneDay_IncreasesWeightAndAge()
        {
            var sdeModel = new SDEGrowthModel(TestSeed);
            var fish = new IndividualFish(sdeModel, 1.0);
            double initialWeight = fish.Weight;

            fish.GrowOneDay(sdeModel, 11.0);

            Assert.True(fish.Weight > initialWeight, 
                $"Weight should increase: {initialWeight} -> {fish.Weight}");
            Assert.Equal(1, fish.AgeDays);
        }

        [Fact]
        public void GrowOneDay_DeadFish_NoChange()
        {
            var sdeModel = new SDEGrowthModel(TestSeed);
            var fish = new IndividualFish(sdeModel, 1.0);
            fish.Die();

            double weightBeforeDeath = fish.Weight;
            int ageBeforeDeath = fish.AgeDays;

            fish.GrowOneDay(sdeModel, 11.0);

            Assert.Equal(weightBeforeDeath, fish.Weight);
            Assert.Equal(ageBeforeDeath, fish.AgeDays);
        }

        [Fact]
        public void GrowMultipleDays_AccumulatesGrowth()
        {
            var sdeModel = new SDEGrowthModel(TestSeed);
            var fish = new IndividualFish(sdeModel, 1.0);
            double initialWeight = fish.Weight;

            fish.GrowMultipleDays(sdeModel, 30, 11.0);

            Assert.True(fish.Weight > initialWeight, "Should grow over 30 days");
            Assert.Equal(30, fish.AgeDays);
        }

        [Fact]
        public void GrowMultipleDays_UsesIndividualGeneticParameters()
        {
            var sdeModel = new SDEGrowthModel(TestSeed);
            
            // Create two fish with different genetics
            var fastGrower = new IndividualFish(5.5, 0.018, 0.02, 1.0);
            var slowGrower = new IndividualFish(4.5, 0.012, 0.02, 1.0);

            fastGrower.GrowMultipleDays(sdeModel, 90, 11.0);
            slowGrower.GrowMultipleDays(sdeModel, 90, 11.0);

            Assert.True(fastGrower.Weight > slowGrower.Weight,
                $"Fish with better genetics should grow more: {fastGrower.Weight} vs {slowGrower.Weight}");
        }

        #endregion

        #region Epigenetic Memory Tests

        [Fact]
        public void RecordStress_ReducesEffectiveAsymptoticWeight()
        {
            var sdeModel = new SDEGrowthModel(TestSeed);
            var fish = new IndividualFish(sdeModel);
            double initialEffective = fish.EffectiveAsymptoticWeight;

            fish.RecordStress(StressType.TemperatureShock, 0.8);

            Assert.True(fish.EffectiveAsymptoticWeight < initialEffective,
                "Stress should reduce effective asymptotic weight");
        }

        [Fact]
        public void RecordStress_AffectsLongTermGrowth()
        {
            var sdeModel = new SDEGrowthModel(TestSeed);
            
            // Two identical fish, one stressed
            var normalFish = new IndividualFish(5.0, 0.015, 0.02, 0.005);
            var stressedFish = new IndividualFish(5.0, 0.015, 0.02, 0.005);

            stressedFish.RecordStress(StressType.Disease, 1.0);
            stressedFish.RecordStress(StressType.Handling, 0.8);

            // Grow both to maturity
            normalFish.GrowMultipleDays(sdeModel, 730, 11.0);
            stressedFish.GrowMultipleDays(sdeModel, 730, 11.0);

            Assert.True(stressedFish.Weight < normalFish.Weight,
                $"Early stress should reduce final size: {stressedFish.Weight} vs {normalFish.Weight}");
        }

        [Fact]
        public void RecordStress_DeadFish_NoEffect()
        {
            var sdeModel = new SDEGrowthModel(TestSeed);
            var fish = new IndividualFish(sdeModel);
            fish.Die();

            int initialStressCount = fish.EpigeneticMemory.StressEventCount;
            fish.RecordStress(StressType.LowOxygen, 0.5);

            Assert.Equal(initialStressCount, fish.EpigeneticMemory.StressEventCount);
        }

        #endregion

        #region Life Stage Tests

        [Fact]
        public void UpdateLifeStage_AgeBasedTransition_EggToAlevin()
        {
            var sdeModel = new SDEGrowthModel(TestSeed);
            var fish = new IndividualFish(sdeModel, 0.0001, LifeStage.Egg);

            fish.GrowMultipleDays(sdeModel, 70, 8.0);
            fish.UpdateLifeStage();

            Assert.Equal(LifeStage.Alevin, fish.LifeStage);
        }

        [Fact]
        public void UpdateLifeStage_WeightBasedTransition_FryToParr()
        {
            var sdeModel = new SDEGrowthModel(TestSeed);
            var fish = new IndividualFish(sdeModel, 0.005, LifeStage.Fry);

            // Grow until weight > 0.01 kg (10g)
            while (fish.Weight < 0.01 && fish.AgeDays < 200)
            {
                fish.GrowOneDay(sdeModel, 11.0);
                fish.UpdateLifeStage();
            }

            Assert.Equal(LifeStage.Parr, fish.LifeStage);
        }

        [Fact]
        public void UpdateLifeStage_ProgressesToHarvest()
        {
            var sdeModel = new SDEGrowthModel(TestSeed);
            var fish = new IndividualFish(5.0, 0.018, 0.02, 1.0, LifeStage.Seawater);

            // Grow to harvest weight
            while (fish.Weight < 4.5 && fish.AgeDays < 1000)
            {
                fish.GrowOneDay(sdeModel, 11.0);
                fish.UpdateLifeStage();
            }

            Assert.Equal(LifeStage.Harvest, fish.LifeStage);
        }

        #endregion

        #region Death Tests

        [Fact]
        public void Die_SetsDeathState()
        {
            var sdeModel = new SDEGrowthModel(TestSeed);
            var fish = new IndividualFish(sdeModel, 2.0);

            Assert.True(fish.IsAlive);
            Assert.Null(fish.DiedAt);

            fish.Die("Test mortality");

            Assert.False(fish.IsAlive);
            Assert.NotNull(fish.DiedAt);
            Assert.Equal(0.0, fish.Weight);
        }

        [Fact]
        public void Die_AlreadyDead_NoChange()
        {
            var sdeModel = new SDEGrowthModel(TestSeed);
            var fish = new IndividualFish(sdeModel);
            
            fish.Die("First death");
            var firstDeathTime = fish.DiedAt;

            System.Threading.Thread.Sleep(10); // Ensure time difference

            fish.Die("Second death");

            Assert.Equal(firstDeathTime, fish.DiedAt);
        }

        #endregion

        #region Population Tests

        [Fact]
        public void CreatePopulation_CreatesCorrectCount()
        {
            var sdeModel = new SDEGrowthModel(TestSeed);
            int count = 50;

            var population = IndividualFish.CreatePopulation(sdeModel, count);

            Assert.Equal(count, population.Length);
        }

        [Fact]
        public void CreatePopulation_AllFishAlive()
        {
            var sdeModel = new SDEGrowthModel(TestSeed);
            var population = IndividualFish.CreatePopulation(sdeModel, 30);

            Assert.All(population, fish => Assert.True(fish.IsAlive));
        }

        [Fact]
        public void CreatePopulation_HasGeneticVariation()
        {
            var sdeModel = new SDEGrowthModel(TestSeed);
            var population = IndividualFish.CreatePopulation(sdeModel, 100);

            var weights = population.Select(f => f.GeneticAsymptoticWeight).ToArray();
            double mean = weights.Average();
            double variance = weights.Select(w => Math.Pow(w - mean, 2)).Average();

            Assert.True(variance > 0, "Population should have genetic variation");
        }

        [Fact]
        public void CreatePopulation_SimulateRealisticDistribution()
        {
            var sdeModel = new SDEGrowthModel(TestSeed);
            var population = IndividualFish.CreatePopulation(sdeModel, 100, 0.005);

            // Grow all fish for 2 years
            foreach (var fish in population)
            {
                fish.GrowMultipleDays(sdeModel, 730, 11.0);
            }

            var finalWeights = population.Where(f => f.IsAlive)
                                          .Select(f => f.Weight)
                                          .OrderBy(w => w)
                                          .ToArray();

            if (finalWeights.Length > 0)
            {
                double mean = finalWeights.Average();
                double median = finalWeights[finalWeights.Length / 2];
                double min = finalWeights.First();
                double max = finalWeights.Last();

                // Check realistic distribution
                Assert.InRange(mean, 2.0, 5.5);
                Assert.InRange(median, 2.0, 5.5);
                Assert.True(max > min, "Should have size variation");
            }
        }

        #endregion

        #region Integration Tests

        [Fact]
        public void IntegrationTest_CompleteLifecycle()
        {
            var sdeModel = new SDEGrowthModel(TestSeed);
            var fish = new IndividualFish(sdeModel, 0.001, LifeStage.Egg);

            // Simulate complete lifecycle with seasonal temperatures
            for (int day = 0; day < 730; day++)
            {
                double dayOfYear = day % 365;
                double temperature = 7.0 + 6.0 * Math.Sin((dayOfYear - 100) * 2 * Math.PI / 365);
                
                // Add occasional stress during early life
                if (day < 180 && day % 30 == 0)
                {
                    fish.RecordStress(StressType.Handling, 0.3);
                }

                fish.GrowOneDay(sdeModel, temperature);
                fish.UpdateLifeStage();

                if (!fish.IsAlive) break;
            }

            if (fish.IsAlive)
            {
                Assert.True(fish.Weight > 1.0, "Should reach significant size after 2 years");
                Assert.True(fish.AgeDays == 730, "Should track age correctly");
            }
        }

        [Fact]
        public void IntegrationTest_ToString_FormatsCorrectly()
        {
            var sdeModel = new SDEGrowthModel(TestSeed);
            var fish = new IndividualFish(sdeModel, 2.5, LifeStage.Seawater);

            string str = fish.ToString();

            Assert.Contains(fish.Id.ToString("N"), str);
            Assert.Contains("2.500", str);
            Assert.Contains("Seawater", str);
            Assert.Contains("Alive", str);
        }

        #endregion
    }
}
