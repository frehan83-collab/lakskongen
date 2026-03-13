using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Sustainability;

namespace LakskongenSim.Core
{
    /// <summary>
    /// Converts normalized Norwegian aquaculture external data into gameplay modifiers.
    /// Keeps runtime dependencies small by operating on compact snapshot values.
    /// </summary>
    public static class AquacultureCalibrationEngine
    {
        public static AquacultureGameplayModifiers Calculate(GameState state, AquacultureDataConfig config)
        {
            var snapshot = state.ExternalAquacultureSnapshot;
            if (snapshot == null)
                return AquacultureGameplayModifiers.Default();

            var liceConfig = config.Calibration.LicePressure;
            var biomassConfig = config.Calibration.BiomassUtilization;
            var zoneConfig = config.Calibration.ZoneExposure;
            var productivityConfig = config.Calibration.IndustryProductivity;

            var lice = snapshot.IndustryLicePressure;
            var biomassUtilization = snapshot.NationalBiomassUtilization;
            var productionAreaRisk = snapshot.ProductionAreaRiskIndex;
            var feedSalesIndex = snapshot.FeedSalesVolumeIndex;
            var profitabilityIndex = snapshot.ProfitabilityIndex;
            var diseasePressure = snapshot.DiseasePressureIndex;
            var escapeRisk = snapshot.EscapeRiskIndex;
            var environmentalCompliance = snapshot.EnvironmentalComplianceIndex;
            var temperatureAnomaly = snapshot.TemperatureAnomalyIndex;
            var treatmentIntensity = snapshot.TreatmentIntensityIndex;
            var mortalityStress = snapshot.MortalityStressIndex;
            var marketSupport = snapshot.MarketPriceSupportIndex;

            double growthPenalty = lice <= liceConfig.GreenThreshold
                ? liceConfig.GrowthPenaltyGreen
                : lice <= liceConfig.YellowThreshold
                    ? liceConfig.GrowthPenaltyYellow
                    : liceConfig.GrowthPenaltyRed;

            double mortalityPenalty = lice <= liceConfig.GreenThreshold
                ? 0.0
                : lice <= liceConfig.YellowThreshold
                    ? liceConfig.MortalityPenaltyYellow
                    : liceConfig.MortalityPenaltyRed;

            double delousingMultiplier = lice <= liceConfig.GreenThreshold
                ? 1.0
                : lice <= liceConfig.YellowThreshold
                    ? liceConfig.DelousingCostMultiplierYellow
                    : liceConfig.DelousingCostMultiplierRed;

            decimal profitabilityAdjustment = 0m;
            if (biomassUtilization >= biomassConfig.HighUtilizationThreshold)
                profitabilityAdjustment += biomassConfig.ProfitabilityAdjustmentAtHigh;
            else if (biomassUtilization <= biomassConfig.LowUtilizationThreshold)
                profitabilityAdjustment += biomassConfig.ProfitabilityAdjustmentAtLow;

            profitabilityAdjustment += (decimal)(profitabilityIndex - 1.0) * 0.5m;
            profitabilityAdjustment += (decimal)(marketSupport - 1.0) * 0.35m;
            profitabilityAdjustment -= (decimal)(Math.Max(0.0, mortalityStress - 1.0) * 0.10);

            double fcrAdjustment = 0.0;
            if (feedSalesIndex >= productivityConfig.FeedSalesIndexHigh)
                fcrAdjustment += productivityConfig.FcrAdjustmentHighEfficiency;
            else if (feedSalesIndex <= productivityConfig.FeedSalesIndexLow)
                fcrAdjustment += productivityConfig.FcrAdjustmentLowEfficiency;

            double zoneRiskMultiplier = Math.Clamp(
                productionAreaRisk,
                zoneConfig.ProductionAreaRiskMultiplierMin,
                zoneConfig.ProductionAreaRiskMultiplierMax);

            double nutrientMultiplier = Math.Clamp(
                1.0 + ((productionAreaRisk - 1.0) * 0.6) + ((1.0 - environmentalCompliance) * 0.25),
                zoneConfig.NutrientMultiplierMin,
                zoneConfig.NutrientMultiplierMax);

            growthPenalty += Math.Max(0.0, temperatureAnomaly - 1.0) * 0.04;
            growthPenalty += Math.Max(0.0, diseasePressure - 1.0) * 0.03;
            growthPenalty = Math.Clamp(growthPenalty, 0.0, 0.20);

            mortalityPenalty += Math.Max(0.0, diseasePressure - 1.0) * 0.02;
            mortalityPenalty += Math.Max(0.0, mortalityStress - 1.0) * 0.015;
            mortalityPenalty = Math.Clamp(mortalityPenalty, 0.0, 0.10);

            delousingMultiplier *= Math.Clamp(1.0 + ((treatmentIntensity - 1.0) * 0.30), 0.90, 1.60);

            var profiles = snapshot.RegionalProfiles?.Count > 0
                ? snapshot.RegionalProfiles.ToDictionary(p => p.RegionId, p => p, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, RegionalAquacultureProfile>(StringComparer.OrdinalIgnoreCase);

            return new AquacultureGameplayModifiers
            {
                GrowthPenalty = growthPenalty,
                MortalityPenalty = mortalityPenalty,
                DelousingCostMultiplier = delousingMultiplier,
                ProfitabilityAdjustment = profitabilityAdjustment,
                FcrAdjustment = fcrAdjustment,
                ZoneRiskMultiplier = zoneRiskMultiplier,
                NutrientLoadMultiplier = nutrientMultiplier,
                DiseasePressureMultiplier = Math.Clamp(diseasePressure, 0.75, 1.75),
                WelfarePenalty = Math.Clamp(((treatmentIntensity - 1.0) * 0.12) + ((diseasePressure - 1.0) * 0.10), 0.0, 0.20),
                EscapeFineMultiplier = Math.Clamp(escapeRisk, 0.80, 1.80),
                MarketPriceSupportMultiplier = Math.Clamp(marketSupport, 0.85, 1.20),
                TemperatureStressMultiplier = Math.Clamp(temperatureAnomaly, 0.85, 1.30),
                TreatmentIntensityMultiplier = Math.Clamp(treatmentIntensity, 0.80, 1.60),
                RegionalProfiles = profiles,
                SourceFreshnessDays = snapshot.SourceFreshnessDays,
                DataConfidence = snapshot.DataConfidence
            };
        }
    }

    public class AquacultureGameplayModifiers
    {
        public double GrowthPenalty { get; set; }
        public double MortalityPenalty { get; set; }
        public double DelousingCostMultiplier { get; set; } = 1.0;
        public decimal ProfitabilityAdjustment { get; set; }
        public double FcrAdjustment { get; set; }
        public double ZoneRiskMultiplier { get; set; } = 1.0;
        public double NutrientLoadMultiplier { get; set; } = 1.0;
        public double DiseasePressureMultiplier { get; set; } = 1.0;
        public double WelfarePenalty { get; set; }
        public double EscapeFineMultiplier { get; set; } = 1.0;
        public double MarketPriceSupportMultiplier { get; set; } = 1.0;
        public double TemperatureStressMultiplier { get; set; } = 1.0;
        public double TreatmentIntensityMultiplier { get; set; } = 1.0;
        public Dictionary<string, RegionalAquacultureProfile> RegionalProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public int SourceFreshnessDays { get; set; }
        public double DataConfidence { get; set; } = 1.0;

        public static AquacultureGameplayModifiers Default() => new();
    }

    public class ExternalAquacultureSnapshot
    {
        public DateTime RetrievedAtUtc { get; set; } = DateTime.UtcNow;
        public string SourceSummary { get; set; } = string.Empty;
        public int SnapshotYear { get; set; }
        public int SnapshotWeek { get; set; }
        public double NationalBiomassUtilization { get; set; } = 0.82;
        public double IndustryLicePressure { get; set; } = 0.25;
        public double ProductionAreaRiskIndex { get; set; } = 1.0;
        public double FeedSalesVolumeIndex { get; set; } = 1.0;
        public double ProfitabilityIndex { get; set; } = 1.0;
        public double DiseasePressureIndex { get; set; } = 1.0;
        public double EscapeRiskIndex { get; set; } = 1.0;
        public double EnvironmentalComplianceIndex { get; set; } = 1.0;
        public double TemperatureAnomalyIndex { get; set; } = 1.0;
        public double TreatmentIntensityIndex { get; set; } = 1.0;
        public double MortalityStressIndex { get; set; } = 1.0;
        public double LocalityCrowdingIndex { get; set; } = 1.0;
        public double MarketPriceSupportIndex { get; set; } = 1.0;
        public string ActiveSourceFlags { get; set; } = string.Empty;
        public List<RegionalAquacultureProfile> RegionalProfiles { get; set; } = new();
        public int SourceFreshnessDays { get; set; }
        public double DataConfidence { get; set; } = 1.0;
    }

    public class AquacultureDataConfig
    {
        public AquacultureCalibration Calibration { get; set; } = new();

        public class AquacultureCalibration
        {
            public BiomassUtilizationConfig BiomassUtilization { get; set; } = new();
            public LicePressureConfig LicePressure { get; set; } = new();
            public ZoneExposureConfig ZoneExposure { get; set; } = new();
            public IndustryProductivityConfig IndustryProductivity { get; set; } = new();
        }

        public class BiomassUtilizationConfig
        {
            public double NationalMabUtilizationNeutral { get; set; } = 0.82;
            public double HighUtilizationThreshold { get; set; } = 0.90;
            public double LowUtilizationThreshold { get; set; } = 0.70;
            public decimal ProfitabilityAdjustmentAtHigh { get; set; } = 0.04m;
            public decimal ProfitabilityAdjustmentAtLow { get; set; } = -0.03m;
        }

        public class LicePressureConfig
        {
            public double GreenThreshold { get; set; } = 0.20;
            public double YellowThreshold { get; set; } = 0.35;
            public double RedThreshold { get; set; } = 0.50;
            public double GrowthPenaltyGreen { get; set; } = 0.0;
            public double GrowthPenaltyYellow { get; set; } = 0.05;
            public double GrowthPenaltyRed { get; set; } = 0.12;
            public double MortalityPenaltyYellow { get; set; } = 0.01;
            public double MortalityPenaltyRed { get; set; } = 0.03;
            public double DelousingCostMultiplierYellow { get; set; } = 1.15;
            public double DelousingCostMultiplierRed { get; set; } = 1.35;
        }

        public class ZoneExposureConfig
        {
            public double ProductionAreaRiskMultiplierMin { get; set; } = 0.90;
            public double ProductionAreaRiskMultiplierMax { get; set; } = 1.25;
            public double NutrientMultiplierMin { get; set; } = 0.95;
            public double NutrientMultiplierMax { get; set; } = 1.20;
        }

        public class IndustryProductivityConfig
        {
            public double FeedSalesVolumeIndexNeutral { get; set; } = 1.0;
            public double FeedSalesIndexHigh { get; set; } = 1.08;
            public double FeedSalesIndexLow { get; set; } = 0.94;
            public double FcrAdjustmentHighEfficiency { get; set; } = -0.03;
            public double FcrAdjustmentLowEfficiency { get; set; } = 0.04;
        }
    }
}