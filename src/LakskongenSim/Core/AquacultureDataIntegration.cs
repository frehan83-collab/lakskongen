using System;
using System.Collections.Generic;
using System.Linq;

namespace LakskongenSim.Core
{
    /// <summary>
    /// Compact regional rollup used by dashboards and simulation calibration.
    /// </summary>
    public class RegionalAquacultureProfile
    {
        public string RegionId { get; set; } = string.Empty;
        public string RegionName { get; set; } = string.Empty;
        public double BiomassUtilization { get; set; } = 0.82;
        public double LicePressure { get; set; } = 0.25;
        public double DiseasePressure { get; set; } = 1.0;
        public double EscapeRisk { get; set; } = 1.0;
        public double EnvironmentalCompliance { get; set; } = 1.0;
        public double TemperatureAnomaly { get; set; } = 1.0;
        public double TreatmentIntensity { get; set; } = 1.0;
        public double MortalityStress { get; set; } = 1.0;
        public double ProfitabilitySupport { get; set; } = 1.0;
        public int LocalityCount { get; set; }
    }

    /// <summary>
    /// Derived external-data analytics suitable for UI and reporting.
    /// </summary>
    public class ExternalDataAnalyticsSummary
    {
        public int SnapshotYear { get; set; }
        public int SnapshotWeek { get; set; }
        public string SourceSummary { get; set; } = string.Empty;
        public int SourceFreshnessDays { get; set; }
        public double DataConfidence { get; set; }
        public double NationalBiomassUtilization { get; set; }
        public double IndustryLicePressure { get; set; }
        public double DiseasePressureIndex { get; set; }
        public double EscapeRiskIndex { get; set; }
        public double EnvironmentalComplianceIndex { get; set; }
        public double TemperatureAnomalyIndex { get; set; }
        public double TreatmentIntensityIndex { get; set; }
        public double MortalityStressIndex { get; set; }
        public double MarketPriceSupportIndex { get; set; }
        public string ActiveSourceFlags { get; set; } = string.Empty;
        public string RiskBand { get; set; } = "Normal";
        public int RegionalProfileCount { get; set; }
    }

    /// <summary>
    /// Helper methods for consuming compact external aquaculture data safely.
    /// </summary>
    public static class AquacultureDataIntegration
    {
        public static RegionalAquacultureProfile GetProfileForZone(GameState state, string? zoneId)
        {
            if (state.ExternalAquacultureSnapshot == null)
            {
                return RegionalAquacultureProfileDefaults(zoneId);
            }

            if (!string.IsNullOrWhiteSpace(zoneId) && state.ExternalAquacultureSnapshot.RegionalProfiles.Count > 0)
            {
                var exact = state.ExternalAquacultureSnapshot.RegionalProfiles
                    .FirstOrDefault(p => string.Equals(p.RegionId, zoneId, StringComparison.OrdinalIgnoreCase));
                if (exact != null)
                    return exact;
            }

            return BuildNationalFallbackProfile(state.ExternalAquacultureSnapshot, zoneId);
        }

        public static ExternalDataAnalyticsSummary BuildAnalyticsSummary(GameState state)
        {
            var snapshot = state.ExternalAquacultureSnapshot;
            if (snapshot == null)
            {
                return new ExternalDataAnalyticsSummary
                {
                    SourceSummary = "No external aquaculture snapshot loaded",
                    RiskBand = "Offline"
                };
            }

            var compositeRisk = (
                snapshot.IndustryLicePressure * 0.20 +
                snapshot.DiseasePressureIndex * 0.20 +
                snapshot.EscapeRiskIndex * 0.10 +
                (2.0 - snapshot.EnvironmentalComplianceIndex) * 0.15 +
                snapshot.TreatmentIntensityIndex * 0.15 +
                snapshot.MortalityStressIndex * 0.20);

            return new ExternalDataAnalyticsSummary
            {
                SnapshotYear = snapshot.SnapshotYear,
                SnapshotWeek = snapshot.SnapshotWeek,
                SourceSummary = snapshot.SourceSummary,
                SourceFreshnessDays = snapshot.SourceFreshnessDays,
                DataConfidence = snapshot.DataConfidence,
                NationalBiomassUtilization = snapshot.NationalBiomassUtilization,
                IndustryLicePressure = snapshot.IndustryLicePressure,
                DiseasePressureIndex = snapshot.DiseasePressureIndex,
                EscapeRiskIndex = snapshot.EscapeRiskIndex,
                EnvironmentalComplianceIndex = snapshot.EnvironmentalComplianceIndex,
                TemperatureAnomalyIndex = snapshot.TemperatureAnomalyIndex,
                TreatmentIntensityIndex = snapshot.TreatmentIntensityIndex,
                MortalityStressIndex = snapshot.MortalityStressIndex,
                MarketPriceSupportIndex = snapshot.MarketPriceSupportIndex,
                ActiveSourceFlags = snapshot.ActiveSourceFlags,
                RegionalProfileCount = snapshot.RegionalProfiles.Count,
                RiskBand = compositeRisk switch
                {
                    >= 1.60 => "Severe",
                    >= 1.25 => "Elevated",
                    <= 0.85 => "Favorable",
                    _ => "Normal"
                }
            };
        }

        private static RegionalAquacultureProfile BuildNationalFallbackProfile(ExternalAquacultureSnapshot snapshot, string? zoneId)
        {
            return new RegionalAquacultureProfile
            {
                RegionId = string.IsNullOrWhiteSpace(zoneId) ? "national" : zoneId!,
                RegionName = string.IsNullOrWhiteSpace(zoneId) ? "National" : zoneId!,
                BiomassUtilization = snapshot.NationalBiomassUtilization,
                LicePressure = snapshot.IndustryLicePressure,
                DiseasePressure = snapshot.DiseasePressureIndex,
                EscapeRisk = snapshot.EscapeRiskIndex,
                EnvironmentalCompliance = snapshot.EnvironmentalComplianceIndex,
                TemperatureAnomaly = snapshot.TemperatureAnomalyIndex,
                TreatmentIntensity = snapshot.TreatmentIntensityIndex,
                MortalityStress = snapshot.MortalityStressIndex,
                ProfitabilitySupport = snapshot.MarketPriceSupportIndex,
                LocalityCount = 0
            };
        }

        private static RegionalAquacultureProfile RegionalAquacultureProfileDefaults(string? zoneId)
        {
            return new RegionalAquacultureProfile
            {
                RegionId = string.IsNullOrWhiteSpace(zoneId) ? "default" : zoneId!,
                RegionName = string.IsNullOrWhiteSpace(zoneId) ? "Default" : zoneId!,
                BiomassUtilization = 0.82,
                LicePressure = 0.25,
                DiseasePressure = 1.0,
                EscapeRisk = 1.0,
                EnvironmentalCompliance = 1.0,
                TemperatureAnomaly = 1.0,
                TreatmentIntensity = 1.0,
                MortalityStress = 1.0,
                ProfitabilitySupport = 1.0,
                LocalityCount = 0
            };
        }
    }
}
