using System.Collections.Generic;
using Xunit;
using LakskongenSim.Data;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using LakskongenSim.Market;

namespace LakskongenSim.Tests
{
    /// <summary>
    /// Finance/market depth: Norwegian resource-rent tax math, the binding
    /// Price Council pen-price smoothing, and red-zone environmental-flexibility
    /// eligibility. All pure-model assertions — no Godot, no simulation engine.
    /// </summary>
    public class FinanceDepthTests
    {
        // ─── ResourceRentTax ────────────────────────────────────────────

        [Fact]
        public void ResourceRent_Loss_YieldsNoTax()
        {
            Assert.Equal(0m, ResourceRentTax.Compute(-50_000_000m));
            Assert.Equal(0m, ResourceRentTax.Compute(0m));
        }

        [Fact]
        public void ResourceRent_ProfitBelowDeduction_YieldsNoTax()
        {
            Assert.Equal(0m, ResourceRentTax.Compute(50_000_000m));
            Assert.Equal(0m, ResourceRentTax.Compute(ResourceRentTax.StandardDeductionNok));
        }

        [Fact]
        public void ResourceRent_StandardCase_AppliesDeductionThen25Percent()
        {
            // (100M profit − 70M deduction) × 25% = 7.5M
            var tax = ResourceRentTax.Compute(100_000_000m);
            Assert.Equal(7_500_000m, tax);
        }

        [Fact]
        public void ResourceRent_Defaults_Are25PercentAnd70M()
        {
            Assert.Equal(0.25m, ResourceRentTax.DefaultRate);
            Assert.Equal(70_000_000m, ResourceRentTax.StandardDeductionNok);
            Assert.Equal(7_500_000m, ResourceRentTax.Compute(100_000_000m));
        }

        [Fact]
        public void ResourceRent_NotEligible_YieldsNoTax()
        {
            Assert.Equal(0m, ResourceRentTax.Compute(1_000_000_000m, isResourceRentEligible: false));
        }

        [Fact]
        public void ResourceRent_CustomRateAndDeduction_AreRespected()
        {
            // (200M − 20M) × 50% = 90M
            Assert.Equal(90_000_000m, ResourceRentTax.Compute(200_000_000m, 20_000_000m, 0.50m));
        }

        [Fact]
        public void ResourceRent_NegativeInputs_AreClampedSafely()
        {
            // Negative deduction treated as 0; negative rate treated as 0.
            Assert.Equal(25_000_000m, ResourceRentTax.Compute(100_000_000m, -5m));
            Assert.Equal(0m, ResourceRentTax.Compute(100_000_000m, 0m, -0.5m));
        }

        // ─── PriceCouncil ───────────────────────────────────────────────

        [Fact]
        public void PriceCouncil_EmptyHistory_ReturnsBasePrice()
        {
            var council = new PriceCouncil(100m);
            Assert.Equal(100m, council.MonthlyPenPrice);
            Assert.Equal(100m, PriceCouncil.Compute(new List<decimal>(), 100m));
        }

        [Fact]
        public void PriceCouncil_LaggedFourWeekAverage_IsApplied()
        {
            var council = new PriceCouncil(100m, bandFraction: 0.25m);
            // Spots above base but inside band; 4-week average 105 → binding price.
            var result = council.Update(new List<decimal> { 90m, 100m, 110m, 120m });
            Assert.Equal(105m, result);
            Assert.Equal(105m, council.MonthlyPenPrice);
        }

        [Fact]
        public void PriceCouncil_UsesOnlyLastFourWeeks()
        {
            // band 1.0 keeps everything in band so the average is unclamped.
            var council = new PriceCouncil(100m, bandFraction: 1.0m);
            var result = council.Update(new List<decimal> { 10m, 20m, 30m, 40m, 50m });
            // last four: 20,30,40,50 → mean 35
            Assert.Equal(35m, result);
            Assert.Equal(PriceCouncil.SmoothingWeeks, council.RecentWeeklySpots.Count);
        }

        [Fact]
        public void PriceCouncil_ClampsToBand()
        {
            var council = new PriceCouncil(100m, bandFraction: 0.10m);
            // Spot spikes far above the +10% ceiling → clamped to 110.
            var result = council.Update(new List<decimal> { 200m, 200m, 200m, 200m });
            Assert.Equal(110m, result);
            Assert.Equal(110m, PriceCouncil.ClampToBand(200m, 100m, 0.10m));

            // And far below the −10% floor → clamped to 90.
            Assert.Equal(90m, PriceCouncil.ClampToBand(10m, 100m, 0.10m));
        }

        [Fact]
        public void PriceCouncil_RaisesEventOnlyOnChange()
        {
            var council = new PriceCouncil(100m, bandFraction: 1.0m);
            var changes = new List<decimal>();
            council.OnMonthlyPenPriceChanged += p => changes.Add(p);

            council.Update(100m); // average 100 = base, no change
            council.Update(120m); // average 110 ≠ 100 → change
            council.Update(120m); // average (100+120+120)/3 = 113.33 → change

            Assert.Equal(2, changes.Count);
            Assert.Equal(110m, changes[0]);
        }

        // ─── EnvironmentalFlexibility ──────────────────────────────────

        [Theory]
        [InlineData(ContainmentLevel.OpenNet, false)]
        [InlineData(ContainmentLevel.Skirt, false)]
        [InlineData(ContainmentLevel.SemiClosed, true)]
        [InlineData(ContainmentLevel.ClosedFloat, true)]
        [InlineData(ContainmentLevel.Submersible, true)]
        [InlineData(ContainmentLevel.OffshoreClosed, true)]
        public void Flexibility_Eligibility_RequiresClosedContainmentInRedZone(
            ContainmentLevel level, bool expected)
        {
            Assert.Equal(expected, EnvironmentalFlexibility.QualifiesForRedZoneRecovery(level, ZoneColor.Red));
        }

        [Theory]
        [InlineData(ZoneColor.Green)]
        [InlineData(ZoneColor.Yellow)]
        public void Flexibility_NonRedZones_DoNotQualify(ZoneColor color)
        {
            Assert.False(EnvironmentalFlexibility.QualifiesForRedZoneRecovery(ContainmentLevel.OffshoreClosed, color));
            Assert.Equal(1.0, EnvironmentalFlexibility.MabMultiplier(ContainmentLevel.OffshoreClosed, color));
        }

        [Theory]
        [InlineData(ContainmentLevel.OpenNet, 0.94)]
        [InlineData(ContainmentLevel.Skirt, 0.94)]
        [InlineData(ContainmentLevel.SemiClosed, 0.97)]
        [InlineData(ContainmentLevel.ClosedFloat, 1.00)]
        [InlineData(ContainmentLevel.Submersible, 1.00)]
        [InlineData(ContainmentLevel.OffshoreClosed, 1.00)]
        public void Flexibility_MabMultiplier_RecoversCapacityByContainment(
            ContainmentLevel level, double expected)
        {
            Assert.Equal(expected, EnvironmentalFlexibility.MabMultiplier(level, ZoneColor.Red), precision: 10);
        }

        [Fact]
        public void Flexibility_Evaluate_SnapshotsOutcome()
        {
            var closed = EnvironmentalFlexibility.Evaluate(ContainmentLevel.ClosedFloat, ZoneColor.Red);
            Assert.True(closed.QualifiesForRecovery);
            Assert.True(closed.IsClosedContainment);
            Assert.Equal(1.00, closed.MabMultiplier, precision: 10);

            var open = EnvironmentalFlexibility.Evaluate(ContainmentLevel.OpenNet, ZoneColor.Red);
            Assert.False(open.QualifiesForRecovery);
            Assert.False(open.IsClosedContainment);
            Assert.Equal(0.94, open.MabMultiplier, precision: 10);
        }
    }
}
