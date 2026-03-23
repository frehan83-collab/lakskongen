using System;
using System.Linq;
using Xunit;
using LakskongenSim.Biology;
using LakskongenSim.Environment;
using LakskongenSim.Market;

namespace LakskongenSim.Tests
{
    public class EnergyBudgetTests
    {
        [Fact]
        public void CalculateNetEnergy_OptimalConditions_Positive()
        {
            double netEnergy = EnergyBudgetModel.CalculateNetEnergyForGrowth(2.0, 0.04, 11.0, 1.0);
            Assert.True(netEnergy > 0, $"Net energy should be positive at optimal temp. Got: {netEnergy:F2}");
        }

        [Fact]
        public void CalculateNetEnergy_NoFeed_Negative()
        {
            double netEnergy = EnergyBudgetModel.CalculateNetEnergyForGrowth(2.0, 0.0, 11.0, 1.0);
            Assert.True(netEnergy <= 0, "No feed = no surplus energy");
        }

        [Fact]
        public void ConvertEnergyToGrowth_PositiveEnergy_PositiveGrowth()
        {
            double growth = EnergyBudgetModel.ConvertEnergyToGrowth(80.0);
            Assert.True(growth > 0 && growth < 0.1, $"Growth should be small and positive. Got: {growth:F4}");
        }

        [Fact]
        public void ConvertEnergyToGrowth_NegativeEnergy_ZeroGrowth()
        {
            double growth = EnergyBudgetModel.ConvertEnergyToGrowth(-50.0);
            Assert.Equal(0.0, growth);
        }
    }

    public class EpigeneticMemoryTests
    {
        [Fact]
        public void NoStress_FullAsymptoticWeight()
        {
            var memory = new EpigeneticMemory();
            Assert.Equal(1.0, memory.GetAsymptoticReduction());
        }

        [Fact]
        public void StressEvent_ReducesAsymptoticWeight()
        {
            var memory = new EpigeneticMemory();
            memory.RecordStressEvent(StressType.TemperatureShock, 0.5);
            double reduction = memory.GetAsymptoticReduction();
            Assert.True(reduction < 1.0, $"Stress should reduce weight. Got: {reduction:F3}");
            Assert.True(reduction >= 0.75, $"Single event shouldn't exceed 25% max. Got: {reduction:F3}");
        }

        [Fact]
        public void MaxStress_CappedAt25Percent()
        {
            var memory = new EpigeneticMemory();
            // Slam with many severe stress events
            for (int i = 0; i < 20; i++)
                memory.RecordStressEvent(StressType.Disease, 1.0);
            Assert.Equal(0.75, memory.GetAsymptoticReduction(), 2);
        }
    }

    public class WaterColumnPhysicsTests
    {
        [Fact]
        public void Temperature_SurfaceWarmerThanDeep_InSummer()
        {
            var wcp = new WaterColumnPhysics();
            double surfaceTemp = wcp.GetTemperatureAtDepth(1.0, 200); // ~July
            double deepTemp = wcp.GetTemperatureAtDepth(50.0, 200);
            Assert.True(surfaceTemp > deepTemp, $"Surface ({surfaceTemp:F1}°C) should be warmer than deep ({deepTemp:F1}°C) in summer");
        }

        [Fact]
        public void Temperature_DeepWater_StableYearRound()
        {
            var wcp = new WaterColumnPhysics();
            double winterDeep = wcp.GetTemperatureAtDepth(50.0, 15);  // January
            double summerDeep = wcp.GetTemperatureAtDepth(50.0, 200); // July
            double diff = Math.Abs(summerDeep - winterDeep);
            Assert.True(diff < 4.0, $"Deep water should be stable. Diff: {diff:F1}°C");
        }

        [Fact]
        public void Oxygen_SurfaceHigherThanDeep()
        {
            var wcp = new WaterColumnPhysics();
            double surfaceO2 = wcp.GetOxygenAtDepth(1.0, 180, 10000);
            double deepO2 = wcp.GetOxygenAtDepth(50.0, 180, 10000);
            Assert.True(surfaceO2 > deepO2, $"Surface O2 ({surfaceO2:F1}) should exceed deep ({deepO2:F1})");
        }

        [Fact]
        public void Oxygen_HighBiomass_ReducesOxygen()
        {
            var wcp = new WaterColumnPhysics();
            double lowBiomassO2 = wcp.GetOxygenAtDepth(10.0, 180, 1000);
            double highBiomassO2 = wcp.GetOxygenAtDepth(10.0, 180, 500000);
            Assert.True(highBiomassO2 < lowBiomassO2, "More biomass should reduce dissolved oxygen");
        }
    }

    public class MonteCarloPriceEngineTests
    {
        [Fact]
        public void SimulatePrice_ReturnsReasonableRange()
        {
            var engine = new MonteCarloPriceEngine(seed: 42);
            var (mean, lower, upper) = engine.SimulatePrice(70.0, 90, 500);
            Assert.InRange(mean, 30.0, 200.0);    // Price shouldn't be crazy
            Assert.True(lower < mean, "5th percentile < mean");
            Assert.True(upper > mean, "95th percentile > mean");
            Assert.True(lower > 0, "Price should be positive");
        }

        [Fact]
        public void GeneratePricePath_CorrectLength()
        {
            var engine = new MonteCarloPriceEngine(seed: 42);
            double[] path = engine.GeneratePricePath(70.0, 365);
            Assert.Equal(366, path.Length); // start + 365 days
            Assert.Equal(70.0, path[0]);
            Assert.True(path.All(p => p > 0), "All prices should be positive");
        }

        [Fact]
        public void SeasonalMultiplier_ChristmasPeak()
        {
            double xmas = MonteCarloPriceEngine.GetSeasonalMultiplier(355);
            Assert.Equal(1.15, xmas);
        }

        [Fact]
        public void SeasonalMultiplier_SummerLull()
        {
            double summer = MonteCarloPriceEngine.GetSeasonalMultiplier(200);
            Assert.Equal(0.92, summer);
        }

        [Fact]
        public void SupplyShock_IncreasesPrice()
        {
            double shocked = MonteCarloPriceEngine.ApplySupplyShock(70.0, 20.0);
            Assert.True(shocked > 70.0, $"20% supply reduction should increase price. Got: {shocked:F2}");
        }
    }

    public class FuturesContractTests
    {
        [Fact]
        public void LongContract_SpotAboveStrike_Profit()
        {
            var contract = new FuturesContract
            {
                StrikePrice = 70.0,
                QuantityTonnes = 10,
                IsLong = true,
                ExpiryDay = 100
            };
            var pnl = contract.CalculateSettlement(80.0); // Spot 80, Strike 70
            Assert.True(pnl > 0, $"Long should profit when spot > strike. Got: {pnl}");
            Assert.Equal(100_000m, pnl); // (80-70) × 10 × 1000 = 100,000
        }

        [Fact]
        public void ShortContract_SpotAboveStrike_Loss()
        {
            var contract = new FuturesContract
            {
                StrikePrice = 70.0,
                QuantityTonnes = 10,
                IsLong = false,
                ExpiryDay = 100
            };
            var pnl = contract.CalculateSettlement(80.0);
            Assert.True(pnl < 0, "Short should lose when spot > strike");
        }

        [Fact]
        public void MarginRequirement_10Percent()
        {
            var contract = new FuturesContract
            {
                StrikePrice = 70.0,
                QuantityTonnes = 10,
                IsLong = true
            };
            var margin = contract.CalculateMarginRequirement();
            Assert.Equal(70_000m, margin); // 70 × 10 × 1000 × 0.10
        }

        [Fact]
        public void IsExpired_BeforeExpiry_False()
        {
            var contract = new FuturesContract { ExpiryDay = 100 };
            Assert.False(contract.IsExpired(50));
            Assert.True(contract.IsExpired(100));
        }
    }
}
