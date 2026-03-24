using System;
using System.Collections.Generic;

namespace LakskongenSim.Optimization
{
    public class FeedingPlan
    {
        public DateTime Date { get; set; }
        public double DailyRationKg { get; set; }
        public double FeedCostNOK { get; set; }
        public double ExpectedGrowthKg { get; set; }
        public string Notes { get; set; } = "";
    }

    public class FeedingOptimizer
    {
        // Welfare constraints
        private const double MinSafeOxygenMgL = 5.0;
        private const double OptimalTempMinC = 8.0;
        private const double OptimalTempMaxC = 14.0;
        private const double StressTempC = 18.0;
        private const double CriticalTempC = 2.0;

        // Feeding parameters (% of body weight per day)
        private const double MaxFeedingRatePct = 1.5;
        private const double MinFeedingRatePct = 0.2;
        private const double OptimalFeedingRatePct = 1.2;

        /// <summary>
        /// Calculate optimal daily feeding ration for a single pen.
        /// Returns kg of feed per fish per day.
        /// </summary>
        public double CalculateOptimalDailyRation(
            double fishWeightKg,
            double tempC,
            double oxygenMgL,
            double feedCostPerKg,
            double marketPricePerKg)
        {
            if (fishWeightKg <= 0) return 0;

            // Base feeding rate as % of body weight
            double feedingRatePct = OptimalFeedingRatePct;

            // Temperature adjustment (Gaussian response centered on 11°C)
            double tempMultiplier = CalculateTemperatureMultiplier(tempC);
            feedingRatePct *= tempMultiplier;

            // Oxygen adjustment — reduce feeding if oxygen is low
            double oxygenMultiplier = CalculateOxygenMultiplier(oxygenMgL);
            feedingRatePct *= oxygenMultiplier;

            // Economic adjustment — feed more aggressively when margin is high
            double marginPerKg = marketPricePerKg - feedCostPerKg * 1.25; // 1.25 FCR
            double economicMultiplier = marginPerKg > 30 ? 1.1 :
                                        marginPerKg > 15 ? 1.0 :
                                        marginPerKg > 0 ? 0.9 : 0.7;
            feedingRatePct *= economicMultiplier;

            // Fish size adjustment — smaller fish eat more relative to body weight
            double sizeMultiplier = fishWeightKg < 0.5 ? 1.3 :
                                    fishWeightKg < 2.0 ? 1.1 :
                                    fishWeightKg > 5.0 ? 0.85 : 1.0;
            feedingRatePct *= sizeMultiplier;

            // Clamp to safe range
            feedingRatePct = Math.Clamp(feedingRatePct, MinFeedingRatePct, MaxFeedingRatePct);

            return fishWeightKg * feedingRatePct / 100.0;
        }

        /// <summary>
        /// Generate a 30-day feeding schedule for a pen.
        /// </summary>
        public List<FeedingPlan> GetFeedingSchedule(
            int fishCount,
            double avgWeightKg,
            double[] dailyTempC,
            double[] dailyOxygenMgL,
            double feedCostPerKg,
            double marketPricePerKg)
        {
            var plan = new List<FeedingPlan>();
            int days = Math.Min(dailyTempC.Length, dailyOxygenMgL.Length);

            double currentWeight = avgWeightKg;

            for (int d = 0; d < days; d++)
            {
                double ration = CalculateOptimalDailyRation(
                    currentWeight, dailyTempC[d], dailyOxygenMgL[d],
                    feedCostPerKg, marketPricePerKg);

                double totalFeed = ration * fishCount;
                double dailyCost = totalFeed * feedCostPerKg;

                // Estimate growth (FCR ~1.25)
                double growth = totalFeed / 1.25 / fishCount;
                currentWeight += growth;

                string notes = "";
                if (dailyOxygenMgL[d] < MinSafeOxygenMgL)
                    notes = "⚠ Low oxygen — feeding reduced";
                else if (dailyTempC[d] > StressTempC)
                    notes = "⚠ Heat stress — feeding reduced";
                else if (dailyTempC[d] < CriticalTempC)
                    notes = "⚠ Cold stress — feeding reduced";

                plan.Add(new FeedingPlan
                {
                    Date = DateTime.Today.AddDays(d),
                    DailyRationKg = totalFeed,
                    FeedCostNOK = dailyCost,
                    ExpectedGrowthKg = growth * fishCount,
                    Notes = notes
                });
            }

            return plan;
        }

        /// <summary>
        /// Estimate NOK savings from optimized vs current feeding plan.
        /// </summary>
        public double EstimateFeedCostSavings(
            double currentDailyFeedKg,
            double optimizedDailyFeedKg,
            double feedCostPerKg,
            int daysRemaining)
        {
            double dailySaving = (currentDailyFeedKg - optimizedDailyFeedKg) * feedCostPerKg;
            return dailySaving * daysRemaining;
        }

        private double CalculateTemperatureMultiplier(double tempC)
        {
            if (tempC < CriticalTempC || tempC > StressTempC) return 0.3;
            // Gaussian centered on 11°C, σ=3
            double diff = tempC - 11.0;
            return Math.Exp(-(diff * diff) / (2 * 3.0 * 3.0));
        }

        private double CalculateOxygenMultiplier(double oxygenMgL)
        {
            if (oxygenMgL < 3.0) return 0.0; // Lethal — stop feeding
            if (oxygenMgL < MinSafeOxygenMgL) return 0.4;
            if (oxygenMgL < 7.0) return 0.7;
            return 1.0; // >7 mg/L is good
        }
    }
}
