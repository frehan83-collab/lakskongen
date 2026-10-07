using System;
using Xunit;
using Xunit.Abstractions;
using LakskongenSim.Market;

namespace LakskongenSim.Tests.Market
{
    /// <summary>
    /// P15 — player supply market impact. Verifies that larger player harvests
    /// create larger downward pressure, that the effect decays over days, that
    /// the −8% cap is respected, and that a zero harvest is a no-op.
    /// </summary>
    public class PlayerSupplyImpactTests
    {
        private readonly ITestOutputHelper _output;

        public PlayerSupplyImpactTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void LargerHarvest_ProducesLargerPressure()
        {
            var market = new MarketSimulation();

            market.ApplyPlayerHarvest(100.0);
            var pressureSmall = market.PlayerSupplyPressure;

            market.ApplyPlayerHarvest(1000.0);
            var pressureLarge = market.PlayerSupplyPressure;

            Assert.True(pressureSmall > 0.0, "A positive harvest must create pressure.");
            Assert.True(pressureLarge > pressureSmall,
                $"Larger harvest must create larger pressure ({pressureLarge} vs {pressureSmall}).");
        }

        [Fact]
        public void ZeroHarvest_HasNoEffect()
        {
            var market = new MarketSimulation();

            market.ApplyPlayerHarvest(0.0);
            market.ApplyPlayerHarvest(-50.0);
            market.ApplyPlayerHarvest(double.NaN);

            Assert.Equal(0.0, market.PlayerSupplyPressure);
            Assert.Equal(1.0, market.PlayerSupply.PriceMultiplier, precision: 12);

            // Advancing days with no harvest must not manufacture pressure.
            market.AdvanceDays(30);
            Assert.Equal(0.0, market.PlayerSupplyPressure);
            Assert.Equal(1.0, market.PlayerSupply.PriceMultiplier, precision: 12);
        }

        [Fact]
        public void Pressure_DecaysOverDays()
        {
            var market = new MarketSimulation();
            market.ApplyPlayerHarvest(1000.0);

            var initial = market.PlayerSupplyPressure;
            Assert.True(initial > 0.0);

            market.AdvanceDays(3);
            var afterThreeDays = market.PlayerSupplyPressure;
            Assert.True(afterThreeDays < initial,
                $"Pressure must decay over days ({afterThreeDays} !< {initial}).");

            market.AdvanceDays(60);
            var afterLongAbsorption = market.PlayerSupplyPressure;
            Assert.True(afterLongAbsorption < afterThreeDays);
            Assert.True(afterLongAbsorption < 0.01,
                $"Pressure should have essentially faded (got {afterLongAbsorption}).");
        }

        [Fact]
        public void CapRespected_AtMinusEightPercent()
        {
            var market = new MarketSimulation();

            // A harvest far larger than the national weekly baseline saturates the cap.
            market.ApplyPlayerHarvest(PlayerSupplyImpact.NationalWeeklyBaselineTonnes * 10.0);

            Assert.Equal(1.0, market.PlayerSupplyPressure, precision: 12);
            Assert.Equal(0.92, market.PlayerSupply.PriceMultiplier, precision: 12);
            Assert.True(market.PlayerSupply.PriceMultiplier >= (1.0 - PlayerSupplyImpact.MaxPriceImpact) - 1e-12);
        }

        [Fact]
        public void RepeatedSmallHarvests_DoNotCompoundUnboundedly()
        {
            var market = new MarketSimulation();

            // Simulate a harvest every day for a long horizon.
            for (var day = 0; day < 200; day++)
            {
                market.ApplyPlayerHarvest(200.0);
                market.AdvanceDays(1);
            }

            var steadyState = market.PlayerSupplyPressure;
            _output.WriteLine($"Steady-state pressure from repeated 200 t harvests: {steadyState:F4}");

            Assert.True(steadyState <= 1.0);
            Assert.True(steadyState < 1.0,
                "Repeated small harvests must converge below saturation, not compound to the cap.");
        }

        [Fact]
        public void MarketSimulation_Harvest_LowersPriceVersusUnharvested()
        {
            // Same deterministic seed => identical O-U diffusion; the only
            // difference is the player-supply pressure applied to the price.
            var baseline = new MarketSimulation(seed: 1234);
            var harvested = new MarketSimulation(seed: 1234);

            harvested.ApplyPlayerHarvest(PlayerSupplyImpact.NationalWeeklyBaselineTonnes * 5.0);

            baseline.AdvanceDays(1);
            harvested.AdvanceDays(1);

            Assert.True(harvested.PricePerKg < baseline.PricePerKg,
                $"Harvesting should push the price down ({harvested.PricePerKg} !< {baseline.PricePerKg}).");
        }
    }
}
