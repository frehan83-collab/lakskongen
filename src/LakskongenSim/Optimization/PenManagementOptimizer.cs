using System;
using System.Collections.Generic;
using System.Linq;

namespace LakskongenSim.Optimization
{
    public class StockingRecommendation
    {
        public double CurrentDensityKgM3 { get; set; }
        public double RecommendedDensityKgM3 { get; set; }
        public int RecommendedFishCount { get; set; }
        public double MaxBiomassKg { get; set; }
        public string WelfareStatus { get; set; } = "OK";
        public string Action { get; set; } = "None";
    }

    public class FallowPlan
    {
        public List<FallowPeriod> Periods { get; set; } = new();
        public int TotalFallowDays { get; set; }
        public double EstimatedBenthicRecoveryPct { get; set; }
    }

    public class FallowPeriod
    {
        public string PenId { get; set; } = "";
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int DurationDays { get; set; }
        public string Reason { get; set; } = "";
    }

    public class RotationPlan
    {
        public List<RotationEntry> Entries { get; set; } = new();
        public double EstimatedProductionTonnes { get; set; }
        public double CapacityUtilizationPct { get; set; }
    }

    public class RotationEntry
    {
        public string SiteId { get; set; } = "";
        public string PenId { get; set; } = "";
        public string FishGroupId { get; set; } = "";
        public DateTime StockDate { get; set; }
        public DateTime ExpectedHarvestDate { get; set; }
        public DateTime FallowStart { get; set; }
        public DateTime FallowEnd { get; set; }
    }

    public class PenManagementOptimizer
    {
        // Norwegian regulatory limits
        private const double MaxDensityKgM3 = 25.0;
        private const double OptimalDensityKgM3 = 20.0;
        private const double MinFallowDays = 60; // 2 months minimum
        private const double RecommendedFallowDays = 90;

        /// <summary>
        /// Optimize stocking density for a pen given welfare constraints.
        /// </summary>
        public StockingRecommendation OptimizeStockingDensity(
            double penVolumeM3,
            int currentFishCount,
            double avgWeightKg,
            double mabLimitTonnes = 0)
        {
            double currentBiomass = currentFishCount * avgWeightKg;
            double currentDensity = penVolumeM3 > 0 ? currentBiomass / penVolumeM3 : 0;

            double maxBiomass = penVolumeM3 * MaxDensityKgM3;
            if (mabLimitTonnes > 0)
                maxBiomass = Math.Min(maxBiomass, mabLimitTonnes * 1000);

            double targetBiomass = penVolumeM3 * OptimalDensityKgM3;
            if (mabLimitTonnes > 0)
                targetBiomass = Math.Min(targetBiomass, mabLimitTonnes * 1000 * 0.85);

            int recommendedCount = avgWeightKg > 0
                ? (int)(targetBiomass / avgWeightKg)
                : currentFishCount;

            string welfare;
            string action;

            if (currentDensity > MaxDensityKgM3)
            {
                welfare = "CRITICAL — Over regulatory limit";
                action = "Immediate harvest or split required";
            }
            else if (currentDensity > OptimalDensityKgM3)
            {
                welfare = "Warning — Approaching limit";
                action = "Plan harvest within 2-4 weeks";
            }
            else if (currentDensity < 5.0)
            {
                welfare = "Underutilized";
                action = "Consider adding smolt or consolidating pens";
            }
            else
            {
                welfare = "Good";
                action = "None required";
            }

            return new StockingRecommendation
            {
                CurrentDensityKgM3 = currentDensity,
                RecommendedDensityKgM3 = OptimalDensityKgM3,
                RecommendedFishCount = recommendedCount,
                MaxBiomassKg = maxBiomass,
                WelfareStatus = welfare,
                Action = action
            };
        }

        /// <summary>
        /// Generate a fallow schedule for a set of pens.
        /// </summary>
        public FallowPlan GenerateFallowSchedule(
            string[] penIds,
            DateTime[] expectedHarvestDates,
            bool extendedFallow = false)
        {
            var plan = new FallowPlan();
            int fallowDays = extendedFallow ? (int)RecommendedFallowDays : (int)MinFallowDays;

            for (int i = 0; i < penIds.Length; i++)
            {
                var harvestDate = i < expectedHarvestDates.Length
                    ? expectedHarvestDates[i]
                    : DateTime.Today.AddMonths(6 + i);

                plan.Periods.Add(new FallowPeriod
                {
                    PenId = penIds[i],
                    StartDate = harvestDate.AddDays(7), // 1 week for cleaning
                    EndDate = harvestDate.AddDays(7 + fallowDays),
                    DurationDays = fallowDays,
                    Reason = extendedFallow ? "Extended recovery" : "Regulatory minimum"
                });
            }

            plan.TotalFallowDays = plan.Periods.Sum(p => p.DurationDays);
            plan.EstimatedBenthicRecoveryPct = extendedFallow ? 95.0 : 75.0;

            return plan;
        }

        /// <summary>
        /// Calculate rotation plan across multiple sites.
        /// </summary>
        public RotationPlan CalculateRotationPlan(
            string[] siteIds,
            string[][] penIdsPerSite,
            int fishGroupCount,
            int productionCycleMonths = 18)
        {
            var plan = new RotationPlan();
            int totalPens = penIdsPerSite.Sum(p => p.Length);
            int groupIndex = 0;

            for (int s = 0; s < siteIds.Length; s++)
            {
                for (int p = 0; p < penIdsPerSite[s].Length; p++)
                {
                    if (groupIndex >= fishGroupCount) break;

                    // Stagger stocking dates by 2 months per pen
                    int offsetMonths = (groupIndex * 2) % 12;
                    DateTime stockDate = DateTime.Today.AddMonths(offsetMonths);
                    DateTime harvestDate = stockDate.AddMonths(productionCycleMonths);
                    DateTime fallowStart = harvestDate.AddDays(7);
                    DateTime fallowEnd = fallowStart.AddDays(MinFallowDays);

                    plan.Entries.Add(new RotationEntry
                    {
                        SiteId = siteIds[s],
                        PenId = penIdsPerSite[s][p],
                        FishGroupId = $"FG-{groupIndex + 1:D3}",
                        StockDate = stockDate,
                        ExpectedHarvestDate = harvestDate,
                        FallowStart = fallowStart,
                        FallowEnd = fallowEnd
                    });

                    groupIndex++;
                }
            }

            plan.EstimatedProductionTonnes = plan.Entries.Count * 500; // ~500t per pen cycle
            plan.CapacityUtilizationPct = totalPens > 0
                ? (plan.Entries.Count / (double)totalPens) * 100.0
                : 0;

            return plan;
        }
    }
}
