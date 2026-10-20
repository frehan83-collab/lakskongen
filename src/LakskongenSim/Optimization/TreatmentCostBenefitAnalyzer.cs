using System;
using System.Collections.Generic;
using System.Linq;

namespace LakskongenSim.Optimization
{
    public class DelousingOption
    {
        public string Method { get; set; } = "";
        public string Description { get; set; } = "";
        public double CostPerFishNOK { get; set; }
        public double TotalCostNOK { get; set; }
        public double EfficacyPct { get; set; }
        public double MortalityRiskPct { get; set; }
        public double StressDays { get; set; }
        public double CostPerLiceRemovedNOK { get; set; }
        public string WelfareImpact { get; set; } = "";
        public bool RequiresWellboat { get; set; }
        public int TreatmentDays { get; set; }
    }

    public class VaccinationAnalysis
    {
        public double CostPerFishNOK { get; set; }
        public double TotalCostNOK { get; set; }
        public double MortalityReductionPct { get; set; }
        public double FishSavedEstimate { get; set; }
        public double ValueOfSavedFishNOK { get; set; }
        public double NetBenefitNOK { get; set; }
        public double ROIPct { get; set; }
        public string Recommendation { get; set; } = "";
    }

    public class TreatmentCostBenefitAnalyzer
    {
        /// <summary>
        /// Analyze available delousing options and rank by cost-effectiveness.
        /// </summary>
        public List<DelousingOption> AnalyzeDelousingOptions(
            double licePerFish,
            int fishCount,
            double avgWeightKg)
        {
            double totalLice = licePerFish * fishCount;

            var options = new List<DelousingOption>
            {
                new DelousingOption
                {
                    Method = "Thermal (Thermolicer)",
                    Description = "Warm water treatment (28-34°C for 30 seconds)",
                    CostPerFishNOK = 1.50 + (avgWeightKg * 0.10),
                    EfficacyPct = 92.0,
                    MortalityRiskPct = 0.8,
                    StressDays = 3,
                    WelfareImpact = "Moderate — thermal stress",
                    RequiresWellboat = true,
                    TreatmentDays = 2
                },
                new DelousingOption
                {
                    Method = "Mechanical (Hydrolicer)",
                    Description = "High-pressure water flushing",
                    CostPerFishNOK = 1.20 + (avgWeightKg * 0.08),
                    EfficacyPct = 88.0,
                    MortalityRiskPct = 1.2,
                    StressDays = 2,
                    WelfareImpact = "Moderate — physical stress",
                    RequiresWellboat = true,
                    TreatmentDays = 2
                },
                new DelousingOption
                {
                    Method = "Cleaner Fish (Lumpfish/Wrasse)",
                    Description = "Biological control — 5-10% of fish population",
                    CostPerFishNOK = 3.00, // Higher upfront, ongoing
                    EfficacyPct = 60.0,
                    MortalityRiskPct = 0.0,
                    StressDays = 0,
                    WelfareImpact = "Low — natural behavior",
                    RequiresWellboat = false,
                    TreatmentDays = 0 // Continuous
                },
                new DelousingOption
                {
                    Method = "Freshwater Treatment",
                    Description = "Freshwater bath in wellboat (3-5 hours)",
                    CostPerFishNOK = 2.00 + (avgWeightKg * 0.15),
                    EfficacyPct = 85.0,
                    MortalityRiskPct = 0.3,
                    StressDays = 2,
                    WelfareImpact = "Low-Moderate — osmotic stress",
                    RequiresWellboat = true,
                    TreatmentDays = 1
                },
                new DelousingOption
                {
                    Method = "Medicinal (Emamectin benzoate)",
                    Description = "In-feed medication — 7-day course",
                    CostPerFishNOK = 0.80,
                    EfficacyPct = 70.0, // Declining due to resistance
                    MortalityRiskPct = 0.1,
                    StressDays = 0,
                    WelfareImpact = "Low — no handling required",
                    RequiresWellboat = false,
                    TreatmentDays = 7
                },
                new DelousingOption
                {
                    Method = "Laseravlusing",
                    Description = "Automated laser targeting of sea lice",
                    CostPerFishNOK = 0.50, // Capital cost amortized
                    EfficacyPct = 50.0,
                    MortalityRiskPct = 0.0,
                    StressDays = 0,
                    WelfareImpact = "Minimal — non-contact",
                    RequiresWellboat = false,
                    TreatmentDays = 0 // Continuous
                }
            };

            // Calculate derived values
            foreach (var option in options)
            {
                option.TotalCostNOK = option.CostPerFishNOK * fishCount;
                double liceRemoved = totalLice * (option.EfficacyPct / 100.0);
                option.CostPerLiceRemovedNOK = liceRemoved > 0
                    ? option.TotalCostNOK / liceRemoved
                    : double.MaxValue;
            }

            return options.OrderBy(o => o.CostPerLiceRemovedNOK).ToList();
        }

        /// <summary>
        /// Calculate ROI of vaccination program.
        /// </summary>
        public VaccinationAnalysis CalculateVaccinationROI(
            int fishCount,
            double mortalityReductionPct,
            double vaccineCostPerFish,
            double baselineMortalityPct = 15.0,
            double avgHarvestWeightKg = 5.0,
            double marketPricePerKg = 70.0)
        {
            double totalCost = vaccineCostPerFish * fishCount;
            double fishSaved = fishCount * (baselineMortalityPct / 100.0) * (mortalityReductionPct / 100.0);
            double valueOfSaved = fishSaved * avgHarvestWeightKg * marketPricePerKg;
            double netBenefit = valueOfSaved - totalCost;
            double roi = totalCost > 0 ? (netBenefit / totalCost) * 100.0 : 0;

            string recommendation;
            if (roi > 200) recommendation = "★★★ Highly recommended — exceptional ROI";
            else if (roi > 100) recommendation = "★★ Recommended — strong ROI";
            else if (roi > 50) recommendation = "★ Consider — positive ROI";
            else if (roi > 0) recommendation = "Marginal — break-even territory";
            else recommendation = "Not recommended — negative ROI";

            return new VaccinationAnalysis
            {
                CostPerFishNOK = vaccineCostPerFish,
                TotalCostNOK = totalCost,
                MortalityReductionPct = mortalityReductionPct,
                FishSavedEstimate = fishSaved,
                ValueOfSavedFishNOK = valueOfSaved,
                NetBenefitNOK = netBenefit,
                ROIPct = roi,
                Recommendation = recommendation
            };
        }
    }
}
