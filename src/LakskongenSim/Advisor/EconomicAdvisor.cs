using System;
using System.Collections.Generic;

namespace LakskongenSim.Advisor
{
    /// <summary>
    /// Economic advisor that generates actionable recommendations based on
    /// market conditions, cost efficiency, and feeding strategy.
    /// </summary>
    public class EconomicAdvisor
    {
        public class EconomicRecommendation
        {
            public string Title { get; set; } = "";
            public string Description { get; set; } = "";
            public int UrgencyRating { get; set; } // 1-5
            public double ExpectedImpactNOK { get; set; }
            public string Category { get; set; } = "";
        }

        /// <summary>
        /// Monitor market conditions and recommend harvest timing.
        /// </summary>
        public List<EconomicRecommendation> MonitorMarketConditions(
            double currentSpotPrice, int currentMonth)
        {
            var recommendations = new List<EconomicRecommendation>();

            // Seasonal averages (NOK/kg)
            double[] seasonalAvg = { 0, 60, 63, 66, 78, 80, 75, 68, 65, 70, 78, 85, 88 };
            double average = seasonalAvg[Math.Clamp(currentMonth, 1, 12)];
            double deviation = (currentSpotPrice - average) / average * 100;

            if (deviation > 15)
            {
                recommendations.Add(new EconomicRecommendation
                {
                    Title = "Market Premium — Consider Harvest",
                    Description = $"Spot price {currentSpotPrice:F0} NOK/kg is {deviation:F0}% above seasonal average ({average:F0}). Consider accelerating harvest to capture premium.",
                    UrgencyRating = 4,
                    ExpectedImpactNOK = currentSpotPrice * 0.15 * 5000, // ~5t harvest at premium
                    Category = "Market"
                });
            }
            else if (deviation < -15)
            {
                recommendations.Add(new EconomicRecommendation
                {
                    Title = "Market Dip — Delay Harvest",
                    Description = $"Spot price {currentSpotPrice:F0} NOK/kg is {Math.Abs(deviation):F0}% below seasonal average ({average:F0}). Consider delaying harvest if fish welfare allows.",
                    UrgencyRating = 3,
                    ExpectedImpactNOK = Math.Abs(deviation) / 100 * average * 5000,
                    Category = "Market"
                });
            }

            // Q4 premium alert
            if (currentMonth >= 9 && currentMonth <= 10)
            {
                recommendations.Add(new EconomicRecommendation
                {
                    Title = "Q4 Premium Approaching",
                    Description = "Christmas demand spike (Nov-Dec) typically adds 10-20% to spot prices. Plan harvest timing accordingly.",
                    UrgencyRating = 2,
                    ExpectedImpactNOK = currentSpotPrice * 0.15 * 10000,
                    Category = "Market"
                });
            }

            return recommendations;
        }

        /// <summary>
        /// Analyze cost efficiency and flag underperforming farms.
        /// </summary>
        public List<EconomicRecommendation> AnalyzeCostEfficiency(
            string zoneId, double opexPerKg, double revenuePerKg)
        {
            var recommendations = new List<EconomicRecommendation>();
            double margin = revenuePerKg - opexPerKg;
            double marginPct = revenuePerKg > 0 ? margin / revenuePerKg * 100 : 0;

            if (marginPct < 10)
            {
                recommendations.Add(new EconomicRecommendation
                {
                    Title = $"Low Margin Alert — Zone {zoneId}",
                    Description = $"Operating margin is only {marginPct:F1}% ({margin:F0} NOK/kg). Industry target is 20-35%. Review feed costs and operational efficiency.",
                    UrgencyRating = marginPct < 0 ? 5 : 4,
                    ExpectedImpactNOK = Math.Abs(margin) * 1000,
                    Category = "Cost"
                });
            }

            if (opexPerKg > 55)
            {
                recommendations.Add(new EconomicRecommendation
                {
                    Title = $"High OPEX — Zone {zoneId}",
                    Description = $"OPEX at {opexPerKg:F0} NOK/kg exceeds industry benchmark (40-50 NOK/kg). Investigate feed, energy, and treatment costs.",
                    UrgencyRating = 3,
                    ExpectedImpactNOK = (opexPerKg - 50) * 5000,
                    Category = "Cost"
                });
            }

            return recommendations;
        }

        /// <summary>
        /// Recommend feeding strategy based on environmental and economic conditions.
        /// </summary>
        public EconomicRecommendation RecommendFeedStrategy(
            double temperatureC, double feedCostPerKg, double marketPricePerKg)
        {
            double marginPerKg = marketPricePerKg - feedCostPerKg * 1.15; // ~1.15 FCR
            bool optimalTemp = temperatureC >= 8 && temperatureC <= 14;

            if (marginPerKg > 30 && optimalTemp)
            {
                return new EconomicRecommendation
                {
                    Title = "Maximize Feeding — High Margin + Optimal Conditions",
                    Description = $"Margin {marginPerKg:F0} NOK/kg with optimal water temp {temperatureC:F1}°C. Feed at maximum rate to accelerate growth.",
                    UrgencyRating = 3,
                    ExpectedImpactNOK = marginPerKg * 100, // Per 100kg growth
                    Category = "Feeding"
                };
            }
            else if (marginPerKg < 10)
            {
                return new EconomicRecommendation
                {
                    Title = "Reduce Feeding — Low Margin",
                    Description = $"Margin only {marginPerKg:F0} NOK/kg. Reduce feeding to maintenance level to minimize costs until prices improve.",
                    UrgencyRating = 4,
                    ExpectedImpactNOK = feedCostPerKg * 0.3 * 1000, // Save 30% of feed for 1000kg
                    Category = "Feeding"
                };
            }
            else
            {
                return new EconomicRecommendation
                {
                    Title = "Standard Feeding",
                    Description = $"Margin {marginPerKg:F0} NOK/kg at {temperatureC:F1}°C. Maintain standard feeding protocols.",
                    UrgencyRating = 1,
                    ExpectedImpactNOK = 0,
                    Category = "Feeding"
                };
            }
        }
    }
}
