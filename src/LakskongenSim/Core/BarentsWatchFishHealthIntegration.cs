using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LakskongenSim.Environment;

namespace LakskongenSim.Core
{
    /// <summary>
    /// Secure client configuration for BarentsWatch Fishhealth API access.
    /// Supports bearer-token auth and an offline/local snapshot mode for deterministic testing.
    /// </summary>
    public class BarentsWatchClientOptions
    {
        public string BaseUrl { get; set; } = "https://www.barentswatch.no";
        public string FishHealthPath { get; set; } = "/api/fishhealth/v1/snapshot";
        public string? BearerToken { get; set; }
        public string? LocalSnapshotPath { get; set; }
        public int TimeoutSeconds { get; set; } = 20;
        public string UserAgent { get; set; } = "LakskongenSim/1.0";

        [JsonIgnore]
        public bool HasToken => !string.IsNullOrWhiteSpace(BearerToken);
    }

    /// <summary>
    /// Raw/transport payload from Fishhealth-like sources before normalization into gameplay-friendly indexes.
    /// </summary>
    public class BarentsWatchFishHealthRawSnapshot
    {
        public DateTime RetrievedAtUtc { get; set; } = DateTime.UtcNow;
        public int Year { get; set; }
        public int Week { get; set; }
        public string SourceName { get; set; } = "BarentsWatch Fishhealth";
        public string SourceUrl { get; set; } = string.Empty;
        public string SourceMode { get; set; } = "offline";
        public List<BarentsWatchProductionAreaRecord> ProductionAreas { get; set; } = new();
    }

    public class BarentsWatchProductionAreaRecord
    {
        public string RegionId { get; set; } = string.Empty;
        public string RegionName { get; set; } = string.Empty;
        public double BiomassUtilization { get; set; }
        public double LicePerFish { get; set; }
        public double DiseaseCasesPer100Sites { get; set; }
        public double MortalityPercent { get; set; }
        public double TemperatureDeviationC { get; set; }
        public double TreatmentCountPerMonth { get; set; }
        public double LocalityCount { get; set; }
        public double EscapesReported { get; set; }
        public double EnvironmentalAlerts { get; set; }
    }

    /// <summary>
    /// File-persisted wrapper used by the data pipeline.
    /// </summary>
    public class BarentsWatchStoredSnapshot
    {
        public string Source { get; set; } = "BarentsWatch Fishhealth";
        public DateTime RetrievedAtUtc { get; set; } = DateTime.UtcNow;
        public BarentsWatchFishHealthRawSnapshot Raw { get; set; } = new();
        public ExternalAquacultureSnapshot Normalized { get; set; } = new();
        public ExternalDataAnalyticsSummary Analytics { get; set; } = new();
        public List<HealthTrendWarning> Warnings { get; set; } = new();
    }

    /// <summary>
    /// In-game warning produced from external health trend analysis.
    /// </summary>
    public class HealthTrendWarning
    {
        public string Code { get; set; } = string.Empty;
        public string Severity { get; set; } = "info";
        public string Message { get; set; } = string.Empty;
        public string RegionId { get; set; } = string.Empty;
        public double Score { get; set; }
    }

    /// <summary>
    /// Crisis modifiers derived from BarentsWatch-aligned health signals.
    /// </summary>
    public class HealthCrisisScenario
    {
        public string CrisisType { get; set; } = "none";
        public string Severity { get; set; } = "normal";
        public double GrowthPenaltyMultiplier { get; set; } = 1.0;
        public double MortalityPenaltyMultiplier { get; set; } = 1.0;
        public double DelousingCostMultiplier { get; set; } = 1.0;
        public double MarketPriceSupportMultiplier { get; set; } = 1.0;
        public double EnvironmentalStressMultiplier { get; set; } = 1.0;
        public string RecommendedIntervention { get; set; } = "Monitor health indicators";
    }

    /// <summary>
    /// Compact report for dashboards/UI/gameplay.
    /// </summary>
    public class ExternalHealthAnalyticsReport
    {
        public string Headline { get; set; } = string.Empty;
        public string RiskBand { get; set; } = "Offline";
        public int WarningCount { get; set; }
        public string RecommendedAction { get; set; } = string.Empty;
        public double CompositeRiskScore { get; set; }
        public List<HealthTrendWarning> Warnings { get; set; } = new();
    }

    /// <summary>
    /// Minimal secure client for fetching Fishhealth payloads from BarentsWatch or local fallback files.
    /// </summary>
    public class BarentsWatchFishHealthClient
    {
        private readonly BarentsWatchClientOptions _options;

        public BarentsWatchFishHealthClient(BarentsWatchClientOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public BarentsWatchFishHealthRawSnapshot FetchLatest()
        {
            if (!string.IsNullOrWhiteSpace(_options.LocalSnapshotPath) && File.Exists(_options.LocalSnapshotPath))
            {
                var json = File.ReadAllText(_options.LocalSnapshotPath);
                var local = JsonSerializer.Deserialize<BarentsWatchFishHealthRawSnapshot>(json, JsonOptions())
                            ?? new BarentsWatchFishHealthRawSnapshot();
                local.SourceMode = "offline-file";
                local.SourceUrl = _options.LocalSnapshotPath!;
                local.RetrievedAtUtc = DateTime.UtcNow;
                return local;
            }

            if (!_options.HasToken)
                throw new InvalidOperationException("BarentsWatch bearer token missing. Configure LocalSnapshotPath for offline mode or provide token for API mode.");

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 5, 120)) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.BearerToken);
            http.DefaultRequestHeaders.UserAgent.ParseAdd(_options.UserAgent);

            var url = BuildUrl();
            var response = http.GetAsync(url).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();

            var jsonString = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var payload = JsonSerializer.Deserialize<BarentsWatchFishHealthRawSnapshot>(jsonString, JsonOptions())
                          ?? throw new InvalidOperationException("Unable to deserialize BarentsWatch fishhealth response.");
            payload.SourceMode = "api";
            payload.SourceUrl = url;
            payload.RetrievedAtUtc = DateTime.UtcNow;
            return payload;
        }

        private string BuildUrl()
        {
            var baseUrl = (_options.BaseUrl ?? string.Empty).TrimEnd('/');
            var path = (_options.FishHealthPath ?? string.Empty).Trim();
            if (!path.StartsWith("/", StringComparison.Ordinal))
                path = "/" + path;
            return baseUrl + path;
        }

        private static JsonSerializerOptions JsonOptions() => new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
    }

    /// <summary>
    /// Converts BarentsWatch raw values into Lakskongen normalized health/environment signals.
    /// </summary>
    public static class BarentsWatchFishHealthNormalizer
    {
        public static ExternalAquacultureSnapshot Normalize(BarentsWatchFishHealthRawSnapshot raw)
        {
            if (raw == null) throw new ArgumentNullException(nameof(raw));

            var profiles = raw.ProductionAreas.Select(area => new RegionalAquacultureProfile
            {
                RegionId = area.RegionId,
                RegionName = area.RegionName,
                BiomassUtilization = Clamp(area.BiomassUtilization, 0.40, 0.99),
                LicePressure = Clamp(area.LicePerFish, 0.01, 1.50),
                DiseasePressure = NormalizeDiseasePressure(area.DiseaseCasesPer100Sites),
                EscapeRisk = NormalizeEscapeRisk(area.EscapesReported),
                EnvironmentalCompliance = NormalizeEnvironmentalCompliance(area.EnvironmentalAlerts),
                TemperatureAnomaly = NormalizeTemperatureAnomaly(area.TemperatureDeviationC),
                TreatmentIntensity = NormalizeTreatmentIntensity(area.TreatmentCountPerMonth),
                MortalityStress = NormalizeMortalityStress(area.MortalityPercent),
                ProfitabilitySupport = NormalizeProfitabilitySupport(area.BiomassUtilization, area.DiseaseCasesPer100Sites, area.MortalityPercent),
                LocalityCount = (int)Math.Round(area.LocalityCount)
            }).ToList();

            double avg(Func<RegionalAquacultureProfile, double> selector, double fallback) => profiles.Count == 0 ? fallback : profiles.Average(selector);
            double totalLocalities = Math.Max(1.0, raw.ProductionAreas.Sum(a => a.LocalityCount));
            double crowdingIndex = raw.ProductionAreas.Count == 0
                ? 1.0
                : Clamp(raw.ProductionAreas.Sum(a => a.LocalityCount * a.BiomassUtilization) / totalLocalities, 0.70, 1.40);

            return new ExternalAquacultureSnapshot
            {
                RetrievedAtUtc = raw.RetrievedAtUtc,
                SourceSummary = $"{raw.SourceName} ({raw.SourceMode})",
                SnapshotYear = raw.Year,
                SnapshotWeek = raw.Week,
                NationalBiomassUtilization = avg(p => p.BiomassUtilization, 0.82),
                IndustryLicePressure = avg(p => p.LicePressure, 0.25),
                ProductionAreaRiskIndex = avg(p => (p.LicePressure + p.DiseasePressure + p.MortalityStress) / 3.0, 1.0),
                FeedSalesVolumeIndex = Clamp(1.0 + ((avg(p => p.BiomassUtilization, 0.82) - 0.82) * 0.6), 0.85, 1.20),
                ProfitabilityIndex = avg(p => p.ProfitabilitySupport, 1.0),
                DiseasePressureIndex = avg(p => p.DiseasePressure, 1.0),
                EscapeRiskIndex = avg(p => p.EscapeRisk, 1.0),
                EnvironmentalComplianceIndex = avg(p => p.EnvironmentalCompliance, 1.0),
                TemperatureAnomalyIndex = avg(p => p.TemperatureAnomaly, 1.0),
                TreatmentIntensityIndex = avg(p => p.TreatmentIntensity, 1.0),
                MortalityStressIndex = avg(p => p.MortalityStress, 1.0),
                LocalityCrowdingIndex = crowdingIndex,
                MarketPriceSupportIndex = avg(p => p.ProfitabilitySupport, 1.0),
                ActiveSourceFlags = "barentswatch,fishhealth,disease,lice",
                RegionalProfiles = profiles,
                SourceFreshnessDays = Math.Max(0, (int)Math.Round((DateTime.UtcNow - raw.RetrievedAtUtc).TotalDays)),
                DataConfidence = profiles.Count == 0 ? 0.35 : Clamp(0.70 + (profiles.Count / 50.0), 0.70, 0.98)
            };
        }

        private static double NormalizeDiseasePressure(double casesPer100Sites) => Clamp(0.85 + (casesPer100Sites / 20.0), 0.85, 1.80);
        private static double NormalizeEscapeRisk(double escapesReported) => Clamp(0.90 + (escapesReported / 40.0), 0.90, 1.60);
        private static double NormalizeEnvironmentalCompliance(double environmentalAlerts) => Clamp(1.10 - (environmentalAlerts / 20.0), 0.70, 1.10);
        private static double NormalizeTemperatureAnomaly(double deviationC) => Clamp(1.0 + (Math.Abs(deviationC) * 0.10), 0.90, 1.40);
        private static double NormalizeTreatmentIntensity(double treatmentsPerMonth) => Clamp(0.90 + (treatmentsPerMonth * 0.15), 0.90, 1.80);
        private static double NormalizeMortalityStress(double mortalityPercent) => Clamp(0.90 + (mortalityPercent / 10.0), 0.90, 1.90);

        private static double NormalizeProfitabilitySupport(double biomassUtilization, double diseaseCases, double mortalityPercent)
        {
            var support = 1.02 + ((biomassUtilization - 0.82) * 0.25) - (diseaseCases * 0.01) - (mortalityPercent * 0.015);
            return Clamp(support, 0.80, 1.15);
        }

        private static double Clamp(double value, double min, double max) => Math.Clamp(value, min, max);
    }

    /// <summary>
    /// Persist/read normalized BarentsWatch snapshots for reproducible gameplay and testing.
    /// </summary>
    public class BarentsWatchSnapshotRepository
    {
        private readonly string _snapshotDirectory;

        public BarentsWatchSnapshotRepository(string snapshotDirectory)
        {
            _snapshotDirectory = snapshotDirectory;
        }

        public string Save(BarentsWatchStoredSnapshot snapshot)
        {
            Directory.CreateDirectory(_snapshotDirectory);
            var fileName = $"fishhealth-{snapshot.Normalized.SnapshotYear:D4}-W{snapshot.Normalized.SnapshotWeek:D2}.json";
            var path = Path.Combine(_snapshotDirectory, fileName);
            File.WriteAllText(path, JsonSerializer.Serialize(snapshot, JsonOptions()));
            return path;
        }

        public BarentsWatchStoredSnapshot? LoadLatest()
        {
            if (!Directory.Exists(_snapshotDirectory))
                return null;

            var latest = new DirectoryInfo(_snapshotDirectory)
                .GetFiles("fishhealth-*.json")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();

            if (latest == null)
                return null;

            var json = File.ReadAllText(latest.FullName);
            return JsonSerializer.Deserialize<BarentsWatchStoredSnapshot>(json, JsonOptions());
        }

        private static JsonSerializerOptions JsonOptions() => new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
    }

    /// <summary>
    /// Warning/crisis logic mapped from normalized health data to gameplay-readable outputs.
    /// </summary>
    public static class BarentsWatchHealthCrisisEngine
    {
        public static List<HealthTrendWarning> BuildWarnings(ExternalAquacultureSnapshot snapshot)
        {
            var warnings = new List<HealthTrendWarning>();
            if (snapshot == null)
                return warnings;

            if (snapshot.IndustryLicePressure >= 0.45)
            {
                warnings.Add(new HealthTrendWarning
                {
                    Code = "lice-high",
                    Severity = snapshot.IndustryLicePressure >= 0.60 ? "critical" : "warning",
                    Message = "Industry lice pressure is elevated. Expect treatment cost spikes and tighter welfare margins.",
                    Score = snapshot.IndustryLicePressure
                });
            }

            if (snapshot.DiseasePressureIndex >= 1.20)
            {
                warnings.Add(new HealthTrendWarning
                {
                    Code = "disease-escalation",
                    Severity = snapshot.DiseasePressureIndex >= 1.45 ? "critical" : "warning",
                    Message = "Regional disease activity is climbing. Infection pressure and mortality risk are above baseline.",
                    Score = snapshot.DiseasePressureIndex
                });
            }

            if (snapshot.TemperatureAnomalyIndex >= 1.15 || snapshot.MortalityStressIndex >= 1.20)
            {
                warnings.Add(new HealthTrendWarning
                {
                    Code = "environment-stress",
                    Severity = snapshot.MortalityStressIndex >= 1.40 ? "critical" : "warning",
                    Message = "Environmental stress indicators are elevated. Growth, welfare, and water quality resilience are weakening.",
                    Score = Math.Max(snapshot.TemperatureAnomalyIndex, snapshot.MortalityStressIndex)
                });
            }

            foreach (var region in snapshot.RegionalProfiles.Where(r => r.LicePressure >= 0.50 || r.DiseasePressure >= 1.30))
            {
                warnings.Add(new HealthTrendWarning
                {
                    Code = "regional-hotspot",
                    Severity = (region.LicePressure >= 0.70 || region.DiseasePressure >= 1.50) ? "critical" : "warning",
                    Message = $"{region.RegionName} is a hotspot for fish-health disruption.",
                    RegionId = region.RegionId,
                    Score = Math.Max(region.LicePressure, region.DiseasePressure)
                });
            }

            return warnings;
        }

        public static HealthCrisisScenario BuildScenario(ExternalAquacultureSnapshot snapshot)
        {
            if (snapshot == null)
                return new HealthCrisisScenario();

            var lice = snapshot.IndustryLicePressure;
            var disease = snapshot.DiseasePressureIndex;
            var stress = snapshot.MortalityStressIndex;
            var temp = snapshot.TemperatureAnomalyIndex;

            if (disease >= 1.45 || stress >= 1.45)
            {
                return new HealthCrisisScenario
                {
                    CrisisType = "disease-wave",
                    Severity = "critical",
                    GrowthPenaltyMultiplier = 1.35,
                    MortalityPenaltyMultiplier = 1.75,
                    DelousingCostMultiplier = 1.20,
                    MarketPriceSupportMultiplier = 1.05,
                    EnvironmentalStressMultiplier = 1.30,
                    RecommendedIntervention = "Reduce stocking pressure, intensify biosurveillance, and prioritize rapid-response treatment windows."
                };
            }

            if (lice >= 0.55)
            {
                return new HealthCrisisScenario
                {
                    CrisisType = "lice-surge",
                    Severity = lice >= 0.70 ? "critical" : "elevated",
                    GrowthPenaltyMultiplier = 1.15,
                    MortalityPenaltyMultiplier = 1.20,
                    DelousingCostMultiplier = 1.45,
                    MarketPriceSupportMultiplier = 1.00,
                    EnvironmentalStressMultiplier = 1.10,
                    RecommendedIntervention = "Deploy preventive barriers and reserve high-efficacy delousing capacity before thresholds are breached."
                };
            }

            if (temp >= 1.15 || stress >= 1.20)
            {
                return new HealthCrisisScenario
                {
                    CrisisType = "environmental-stress",
                    Severity = "elevated",
                    GrowthPenaltyMultiplier = 1.20,
                    MortalityPenaltyMultiplier = 1.25,
                    DelousingCostMultiplier = 1.10,
                    MarketPriceSupportMultiplier = 0.98,
                    EnvironmentalStressMultiplier = 1.25,
                    RecommendedIntervention = "Lower biomass density and shift husbandry toward water-quality protection and welfare recovery."
                };
            }

            return new HealthCrisisScenario
            {
                CrisisType = "stable",
                Severity = "normal",
                RecommendedIntervention = "Monitor weekly fish-health trendline and preserve intervention capacity."
            };
        }

        public static ExternalHealthAnalyticsReport BuildReport(ExternalAquacultureSnapshot snapshot)
        {
            var warnings = BuildWarnings(snapshot);
            var scenario = BuildScenario(snapshot);
            var compositeRisk = Math.Round(
                (snapshot.IndustryLicePressure * 0.25) +
                (snapshot.DiseasePressureIndex * 0.30) +
                (snapshot.MortalityStressIndex * 0.20) +
                (snapshot.TreatmentIntensityIndex * 0.10) +
                (snapshot.TemperatureAnomalyIndex * 0.15), 2);

            return new ExternalHealthAnalyticsReport
            {
                Headline = $"Fishhealth {snapshot.SnapshotYear}-W{snapshot.SnapshotWeek:D2}: {scenario.CrisisType}",
                RiskBand = AquacultureDataIntegration.BuildAnalyticsSummary(new GameState { ExternalAquacultureSnapshot = snapshot }).RiskBand,
                WarningCount = warnings.Count,
                RecommendedAction = scenario.RecommendedIntervention,
                CompositeRiskScore = compositeRisk,
                Warnings = warnings
            };
        }
    }

    /// <summary>
    /// End-to-end data pipeline: fetch → normalize → analyze → persist.
    /// </summary>
    public class BarentsWatchFishHealthPipeline
    {
        private readonly BarentsWatchFishHealthClient _client;
        private readonly BarentsWatchSnapshotRepository _repository;

        public BarentsWatchFishHealthPipeline(BarentsWatchFishHealthClient client, BarentsWatchSnapshotRepository repository)
        {
            _client = client;
            _repository = repository;
        }

        public BarentsWatchStoredSnapshot IngestLatest()
        {
            var raw = _client.FetchLatest();
            var normalized = BarentsWatchFishHealthNormalizer.Normalize(raw);
            var warnings = BarentsWatchHealthCrisisEngine.BuildWarnings(normalized);
            var analytics = AquacultureDataIntegration.BuildAnalyticsSummary(new GameState
            {
                ExternalAquacultureSnapshot = normalized
            });

            var stored = new BarentsWatchStoredSnapshot
            {
                Source = raw.SourceName,
                RetrievedAtUtc = raw.RetrievedAtUtc,
                Raw = raw,
                Normalized = normalized,
                Analytics = analytics,
                Warnings = warnings
            };

            _repository.Save(stored);
            return stored;
        }

        public BarentsWatchStoredSnapshot? LoadLatest() => _repository.LoadLatest();
    }
}
