using System;
using System.Text.Json;
using Xunit;
using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Finance;
using LakskongenSim.Farm;
using LakskongenSim.Environment;

namespace LakskongenSim.Tests
{
    #pragma warning disable CS0618 // Balance is obsolete but needed for save/load tests

    public class SaveLoadRoundTripTests
    {
        [Fact]
        public void GameState_SerializeDeserialize_PreservesDay()
        {
            var state = new GameState { Day = 365, Balance = 5_000_000m };
            var json = JsonSerializer.Serialize(state);
            var restored = JsonSerializer.Deserialize<GameState>(json);
            Assert.NotNull(restored);
            Assert.Equal(365, restored!.Day);
            Assert.Equal(5_000_000m, restored.Balance);
        }

        [Fact]
        public void GameState_EmptyJson_DeserializesWithDefaults()
        {
            var state = JsonSerializer.Deserialize<GameState>("{}");
            Assert.NotNull(state);
            Assert.Equal(0, state!.Day);
        }

        [Fact]
        public void GameState_WithPopulations_RoundTrips()
        {
            var state = new GameState { Day = 100 };
            var pop = new FishPopulation();
            pop.Cohorts.Add(new FishPopulation.Cohort(0, 50000, 2.5, LifeStage.Seawater));
            state.Populations.Add(pop);
            var json = JsonSerializer.Serialize(state);
            var restored = JsonSerializer.Deserialize<GameState>(json)!;
            Assert.Single(restored.Populations);
            Assert.Equal(50000, restored.Populations[0].TotalCount);
        }
    }

    public class GrowthModelTests
    {
        [Fact]
        public void ApplyGrowth_SeawaterStage_GrowsFastest()
        {
            var model = new GrowthModel();
            var seawater = model.ApplyGrowth(1.0, 30, LifeStage.Seawater);
            var egg = model.ApplyGrowth(1.0, 30, LifeStage.Egg);
            var parr = model.ApplyGrowth(1.0, 30, LifeStage.Parr);
            Assert.True(seawater > parr);
            Assert.True(parr > egg);
        }

        [Fact]
        public void ApplyGrowth_ZeroDays_NoChange()
        {
            var model = new GrowthModel();
            Assert.Equal(2.5, model.ApplyGrowth(2.5, 0, LifeStage.Seawater));
        }

        [Fact]
        public void ApplyGrowth_NegativeDays_NoChange()
        {
            var model = new GrowthModel();
            Assert.Equal(2.5, model.ApplyGrowth(2.5, -5, LifeStage.Seawater));
        }

        [Fact]
        public void SDEGrowth_OptimalTemp_NonNegative()
        {
            var model = new GrowthModel();
            var result = model.ApplySDEGrowthOneDay(1.0, 11.0, 5.0, 0.05, 0.01, LifeStage.Seawater);
            Assert.True(result >= 0.0);
        }

        [Fact]
        public void SDEGrowth_ExtremeTemp_NoGrowth()
        {
            var model = new GrowthModel();
            Assert.Equal(1.0, model.ApplySDEGrowthOneDay(1.0, 1.0, 5.0, 0.05, 0.01, LifeStage.Seawater));
            Assert.Equal(1.0, model.ApplySDEGrowthOneDay(1.0, 19.0, 5.0, 0.05, 0.01, LifeStage.Seawater));
        }

        [Fact]
        public void SDEGrowth_500Days_SubstantialGrowth()
        {
            var model = new GrowthModel();
            var weight = model.ApplySDEGrowthMultipleDays(0.1, 500, 11.0, 5.0, 0.05, 0.005, LifeStage.Seawater);
            Assert.True(weight > 0.5, $"Expected > 0.5 kg after 500 days, got {weight:F3}");
        }
    }

    public class GeneticsBreedingTests
    {
        [Fact]
        public void GenerateRandomGenome_NotNull()
        {
            var genetics = new Genetics();
            var genome = genetics.GenerateRandomGenome();
            Assert.NotNull(genome);
            Assert.True(genome.GetGrowthMultiplier() > 0);
        }

        [Fact]
        public void Breed_ProducesViableOffspring()
        {
            var genetics = new Genetics();
            var p1 = genetics.GenerateRandomGenome();
            var p2 = genetics.GenerateRandomGenome();
            var offspring = genetics.Breed(p1, p2);
            Assert.NotNull(offspring);
            Assert.True(offspring.GetGrowthMultiplier() > 0);
            Assert.True(offspring.GetDiseaseResistanceMultiplier() > 0);
        }

        [Fact]
        public void Breed_100Offspring_AllValid()
        {
            var genetics = new Genetics();
            var p1 = genetics.GenerateRandomGenome();
            var p2 = genetics.GenerateRandomGenome();
            for (int i = 0; i < 100; i++)
            {
                var child = genetics.Breed(p1, p2);
                Assert.True(child.GetGrowthMultiplier() > 0.5 && child.GetGrowthMultiplier() < 2.0,
                    $"Growth multiplier out of range: {child.GetGrowthMultiplier()}");
            }
        }
    }

    public class FeedSystemFCRTests
    {
        [Fact]
        public void CalculateFCR_OptimalConditions_RealisticRange()
        {
            var feed = new FeedSystem();
            double fcr = feed.CalculateFCR(11.0, LifeStage.Seawater, 25.0);
            Assert.InRange(fcr, 0.8, 2.5);
        }

        [Fact]
        public void CalculateFCR_ColdWater_HigherFCR()
        {
            var feed = new FeedSystem();
            double fcrOptimal = feed.CalculateFCR(11.0, LifeStage.Seawater, 25.0);
            double fcrCold = feed.CalculateFCR(4.0, LifeStage.Seawater, 25.0);
            Assert.True(fcrCold >= fcrOptimal, $"Cold FCR ({fcrCold:F2}) should be >= optimal ({fcrOptimal:F2})");
        }

        [Fact]
        public void Feed_ProducesFeedResult()
        {
            var feed = new FeedSystem();
            var pop = new FishPopulation();
            pop.Cohorts.Add(new FishPopulation.Cohort(180, 10000, 2.0, LifeStage.Seawater));
            var result = feed.Feed(pop, 1, 11.0, 25.0);
            Assert.True(result.FeedUsedKg > 0, "Should use some feed");
        }
    }

    public class FinanceManagerTests
    {
        [Fact]
        public void Spend_InsufficientFunds_ReturnsFalse()
        {
            var fm = new FinanceManager(1000m);
            Assert.False(fm.Spend(2000m));
            Assert.Equal(1000m, fm.Balance);
        }

        [Fact]
        public void Spend_SufficientFunds_Deducts()
        {
            var fm = new FinanceManager(5000m);
            Assert.True(fm.Spend(3000m));
            Assert.Equal(2000m, fm.Balance);
        }

        [Fact]
        public void ForceSpend_AllowsNegative()
        {
            var fm = new FinanceManager(100m);
            fm.ForceSpend(500m);
            Assert.Equal(-400m, fm.Balance);
        }

        [Fact]
        public void ESGDiscount_HighScore_30Percent()
        {
            var fm = new FinanceManager(1_000_000m);
            fm.UpdateSustainabilityBonuses(85);
            Assert.Equal(70_000m, fm.ApplyESGDiscount(100_000m));
        }

        [Fact]
        public void GrantMultiplier_HighScore_1_5x()
        {
            var fm = new FinanceManager(1_000_000m);
            fm.UpdateSustainabilityBonuses(95);
            Assert.Equal(150_000m, fm.ApplyGrantMultiplier(100_000m));
        }
    }
}
