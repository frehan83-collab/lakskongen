using System;
using Xunit;
using LakskongenSim.Biology;

namespace LakskongenSim.Tests
{
    /// <summary>
    /// Comprehensive tests for dynamic Feed Conversion Ratio calculations.
    /// Validates FCR penalties for suboptimal feed composition, pellet size, 
    /// feeding frequency, and environmental conditions.
    /// </summary>
    public class FeedConversionModelTests
    {
        private const double Tolerance = 0.01;

        #region Baseline FCR Tests

        [Fact]
        public void CalculateDynamicFCR_OptimalConditions_NearBaseline()
        {
            var model = new FeedConversionModel();
            var composition = new FeedComposition
            {
                ProteinFraction = 0.45,
                FatFraction = 0.25,
                PelletSizeMm = 6.0,
                EnergyDensity = 22.0
            };
            var schedule = new FeedingSchedule
            {
                FeedingsPerDay = 6,
                DailyRationPercent = 1.5,
                UtilizationEfficiency = 0.90
            };

            double fcr = model.CalculateDynamicFCR(
                2.0, 11.0, 0.90, composition, schedule);

            // Should be near baseline (1.15) under optimal conditions
            Assert.InRange(fcr, 1.0, 1.3);
        }

        [Fact]
        public void CalculateDynamicFCR_WithinRealisticBounds()
        {
            var model = new FeedConversionModel();
            var composition = new FeedComposition();
            var schedule = new FeedingSchedule();

            // Test various conditions
            for (double temp = 6.0; temp <= 14.0; temp += 2.0)
            {
                for (double o2 = 0.70; o2 <= 1.0; o2 += 0.10)
                {
                    double fcr = model.CalculateDynamicFCR(
                        2.0, temp, o2, composition, schedule);

                    Assert.InRange(fcr, FeedConversionModel.MinFCR, 
                        FeedConversionModel.MaxFCR);
                }
            }
        }

        #endregion

        #region Protein/Fat Penalty Tests

        [Fact]
        public void CalculateDynamicFCR_OptimalProtein_BetterThanSubOptimal()
        {
            var model = new FeedConversionModel();
            var schedule = new FeedingSchedule();

            var optimalComposition = new FeedComposition 
            { 
                ProteinFraction = 0.45, 
                FatFraction = 0.25 
            };
            var lowProteinComposition = new FeedComposition 
            { 
                ProteinFraction = 0.30, 
                FatFraction = 0.25 
            };

            double optimalFCR = model.CalculateDynamicFCR(
                2.0, 11.0, 0.90, optimalComposition, schedule);
            double lowProteinFCR = model.CalculateDynamicFCR(
                2.0, 11.0, 0.90, lowProteinComposition, schedule);

            Assert.True(optimalFCR < lowProteinFCR, 
                $"Optimal protein should have better FCR: {optimalFCR} vs {lowProteinFCR}");
        }

        [Fact]
        public void CalculateDynamicFCR_OptimalFat_BetterThanSubOptimal()
        {
            var model = new FeedConversionModel();
            var schedule = new FeedingSchedule();

            var optimalComposition = new FeedComposition 
            { 
                ProteinFraction = 0.45, 
                FatFraction = 0.25 
            };
            var highFatComposition = new FeedComposition 
            { 
                ProteinFraction = 0.45, 
                FatFraction = 0.40 
            };

            double optimalFCR = model.CalculateDynamicFCR(
                2.0, 11.0, 0.90, optimalComposition, schedule);
            double highFatFCR = model.CalculateDynamicFCR(
                2.0, 11.0, 0.90, highFatComposition, schedule);

            Assert.True(optimalFCR < highFatFCR, 
                "Optimal fat should have better FCR");
        }

        #endregion

        #region Pellet Size Tests

        [Fact]
        public void CalculateDynamicFCR_PelletTooLarge_SeverelyPenalized()
        {
            var model = new FeedConversionModel();
            var schedule = new FeedingSchedule();

            var normalPellet = new FeedComposition { PelletSizeMm = 6.0 };
            var largePellet = new FeedComposition { PelletSizeMm = 15.0 }; // Too large

            double normalFCR = model.CalculateDynamicFCR(
                1.0, 11.0, 0.90, normalPellet, schedule); // 1kg fish
            double largePelletFCR = model.CalculateDynamicFCR(
                1.0, 11.0, 0.90, largePellet, schedule);

            Assert.True(largePelletFCR > normalFCR * 1.2, 
                "Pellets too large should severely increase FCR");
        }

        [Fact]
        public void CalculateDynamicFCR_PelletSizeScalesWithFishSize()
        {
            var model = new FeedConversionModel();
            var schedule = new FeedingSchedule();
            var composition = new FeedComposition { PelletSizeMm = 6.0 };

            // Same pellet size should be better for larger fish
            double smallFishFCR = model.CalculateDynamicFCR(
                0.5, 11.0, 0.90, composition, schedule); // 0.5kg
            double largeFishFCR = model.CalculateDynamicFCR(
                2.0, 11.0, 0.90, composition, schedule); // 2.0kg

            // Larger fish can handle 6mm pellets better than small fish
            Assert.True(largeFishFCR <= smallFishFCR, 
                "Larger fish should handle same pellet size better");
        }

        #endregion

        #region Feeding Frequency Tests

        [Fact]
        public void CalculateDynamicFCR_OptimalFrequency_BetterThanExtreme()
        {
            var model = new FeedConversionModel();
            var composition = new FeedComposition();

            var optimalSchedule = new FeedingSchedule { FeedingsPerDay = 6 };
            var infrequentSchedule = new FeedingSchedule { FeedingsPerDay = 1 };
            var frequentSchedule = new FeedingSchedule { FeedingsPerDay = 15 };

            double optimalFCR = model.CalculateDynamicFCR(
                2.0, 11.0, 0.90, composition, optimalSchedule);
            double infrequentFCR = model.CalculateDynamicFCR(
                2.0, 11.0, 0.90, composition, infrequentSchedule);
            double frequentFCR = model.CalculateDynamicFCR(
                2.0, 11.0, 0.90, composition, frequentSchedule);

            Assert.True(optimalFCR < infrequentFCR, 
                "Optimal frequency should be better than 1x/day");
            Assert.True(optimalFCR < frequentFCR, 
                "Optimal frequency should be better than 15x/day");
        }

        #endregion

        #region Environmental Condition Tests

        [Fact]
        public void CalculateDynamicFCR_OptimalTemperature_BetterThanCold()
        {
            var model = new FeedConversionModel();
            var composition = new FeedComposition();
            var schedule = new FeedingSchedule();

            double optimalFCR = model.CalculateDynamicFCR(
                2.0, 11.0, 0.90, composition, schedule);
            double coldFCR = model.CalculateDynamicFCR(
                2.0, 5.0, 0.90, composition, schedule);

            Assert.True(optimalFCR < coldFCR, 
                $"Optimal temp should have better FCR: {optimalFCR} vs {coldFCR}");
        }

        [Fact]
        public void CalculateDynamicFCR_LowOxygen_PenalizesFCR()
        {
            var model = new FeedConversionModel();
            var composition = new FeedComposition();
            var schedule = new FeedingSchedule();

            double highO2FCR = model.CalculateDynamicFCR(
                2.0, 11.0, 0.95, composition, schedule);
            double lowO2FCR = model.CalculateDynamicFCR(
                2.0, 11.0, 0.65, composition, schedule);

            Assert.True(lowO2FCR > highO2FCR, 
                "Low oxygen should increase FCR");
        }

        [Fact]
        public void CalculateDynamicFCR_CriticalLowOxygen_SeverePenalty()
        {
            var model = new FeedConversionModel();
            var composition = new FeedComposition();
            var schedule = new FeedingSchedule();

            double normalO2FCR = model.CalculateDynamicFCR(
                2.0, 11.0, 0.90, composition, schedule);
            double criticalO2FCR = model.CalculateDynamicFCR(
                2.0, 11.0, 0.55, composition, schedule);

            Assert.True(criticalO2FCR > normalO2FCR * 1.2, 
                "Critical low oxygen should severely increase FCR");
        }

        #endregion

        #region Utilization Efficiency Tests

        [Fact]
        public void CalculateDynamicFCR_LowUtilization_IncreasesFCR()
        {
            var model = new FeedConversionModel();
            var composition = new FeedComposition();

            var highUtilization = new FeedingSchedule { UtilizationEfficiency = 0.95 };
            var lowUtilization = new FeedingSchedule { UtilizationEfficiency = 0.70 };

            double highUtilFCR = model.CalculateDynamicFCR(
                2.0, 11.0, 0.90, composition, highUtilization);
            double lowUtilFCR = model.CalculateDynamicFCR(
                2.0, 11.0, 0.90, composition, lowUtilization);

            Assert.True(lowUtilFCR > highUtilFCR, 
                "Poor feed utilization should increase FCR");
        }

        #endregion

        #region Optimal Daily Ration Tests

        [Fact]
        public void CalculateOptimalDailyRation_SmallerFishNeedMorePercentage()
        {
            var model = new FeedConversionModel();

            double smallFishRation = model.CalculateOptimalDailyRation(0.1, 11.0);
            double largeFishRation = model.CalculateOptimalDailyRation(4.0, 11.0);

            Assert.True(smallFishRation > largeFishRation, 
                $"Small fish need higher % body weight: {smallFishRation:F3} vs {largeFishRation:F3}");
        }

        [Fact]
        public void CalculateOptimalDailyRation_WithinRealisticBounds()
        {
            var model = new FeedConversionModel();

            for (double weight = 0.01; weight <= 5.0; weight *= 2)
            {
                double ration = model.CalculateOptimalDailyRation(weight, 11.0);
                Assert.InRange(ration, 0.005, 0.030); // 0.5% to 3%
            }
        }

        [Fact]
        public void CalculateOptimalDailyRation_WarmerWaterIncreasesRation()
        {
            var model = new FeedConversionModel();

            double coldRation = model.CalculateOptimalDailyRation(2.0, 8.0);
            double warmRation = model.CalculateOptimalDailyRation(2.0, 14.0);

            Assert.True(warmRation > coldRation, 
                "Warmer water should increase feed ration");
        }

        #endregion

        #region Weight Gain and Feed Calculation Tests

        [Fact]
        public void CalculateWeightGainFromFeed_BaseFCR_CorrectConversion()
        {
            var model = new FeedConversionModel();
            double feed = 1.15; // 1.15 kg
            double fcr = 1.15;  // Baseline FCR

            double gain = model.CalculateWeightGainFromFeed(feed, fcr);

            Assert.Equal(1.0, gain, 2); // Should gain 1.0 kg
        }

        [Fact]
        public void CalculateWeightGainFromFeed_BetterFCR_MoreGain()
        {
            var model = new FeedConversionModel();
            double feed = 1.0;

            double gainGoodFCR = model.CalculateWeightGainFromFeed(feed, 1.0);
            double gainPoorFCR = model.CalculateWeightGainFromFeed(feed, 1.5);

            Assert.True(gainGoodFCR > gainPoorFCR, 
                "Better FCR should yield more weight gain");
        }

        [Fact]
        public void CalculateFeedForTargetGain_BaseFCR_CorrectAmount()
        {
            var model = new FeedConversionModel();
            double targetGain = 1.0;
            double fcr = 1.15;

            double feed = model.CalculateFeedForTargetGain(targetGain, fcr);

            Assert.Equal(1.15, feed, 2); // Should need 1.15 kg feed
        }

        [Fact]
        public void CalculateFeedForTargetGain_RoundTrip_Consistent()
        {
            var model = new FeedConversionModel();
            double targetGain = 1.5;
            double fcr = 1.20;

            double feed = model.CalculateFeedForTargetGain(targetGain, fcr);
            double actualGain = model.CalculateWeightGainFromFeed(feed, fcr);

            Assert.Equal(targetGain, actualGain, 6);
        }

        [Fact]
        public void CalculateWeightGainFromFeed_ZeroOrNegative_ReturnsZero()
        {
            var model = new FeedConversionModel();

            Assert.Equal(0.0, model.CalculateWeightGainFromFeed(0.0, 1.15));
            Assert.Equal(0.0, model.CalculateWeightGainFromFeed(-1.0, 1.15));
            Assert.Equal(0.0, model.CalculateWeightGainFromFeed(1.0, 0.0));
        }

        #endregion

        #region Integration Tests

        [Fact]
        public void IntegrationTest_RealisticFarmScenario()
        {
            var model = new FeedConversionModel();
            
            // Typical commercial farm conditions
            var composition = new FeedComposition
            {
                ProteinFraction = 0.45,
                FatFraction = 0.26,
                PelletSizeMm = 6.0,
                EnergyDensity = 22.0
            };
            var schedule = new FeedingSchedule
            {
                FeedingsPerDay = 8, // Automated feeding
                DailyRationPercent = 1.3,
                UtilizationEfficiency = 0.88
            };

            // Simulate 2kg fish in Norwegian summer conditions
            double fcr = model.CalculateDynamicFCR(
                2.0, 12.0, 0.85, composition, schedule);

            // Should achieve good but not perfect FCR
            Assert.InRange(fcr, 1.10, 1.35);
        }

        [Fact]
        public void IntegrationTest_SubOptimalConditions_HigherFCR()
        {
            var model = new FeedConversionModel();
            
            // Poor conditions: cold water, low oxygen, suboptimal feed
            var composition = new FeedComposition
            {
                ProteinFraction = 0.38, // Low protein
                FatFraction = 0.32,     // High fat
                PelletSizeMm = 9.0      // Too large for small fish
            };
            var schedule = new FeedingSchedule
            {
                FeedingsPerDay = 2,     // Too infrequent
                UtilizationEfficiency = 0.75
            };

            double fcr = model.CalculateDynamicFCR(
                0.8, 7.0, 0.70, composition, schedule);

            // Should have significantly worse FCR
            Assert.InRange(fcr, 1.4, 2.0);
        }

        #endregion
    }
}
