using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LakskongenSim.Advisor
{
    /// <summary>
    /// Tracks recommendation history and outcomes for learning and analytics.
    /// Provides insights into advisor effectiveness and player decision patterns.
    /// </summary>
    public class RecommendationHistory
    {
        private readonly List<Recommendation> _recommendations = new();
        private readonly Dictionary<string, RecommendationPerformance> _performanceCache = new();
        private readonly string? _storagePath;

        /// <summary>Event fired when a recommendation is added or updated.</summary>
        public event Action<Recommendation>? OnRecommendationChanged;

        /// <summary>All recommendations in chronological order.</summary>
        public IReadOnlyList<Recommendation> AllRecommendations => _recommendations.AsReadOnly();

        /// <summary>Only active (unresolved) recommendations.</summary>
        public IEnumerable<Recommendation> ActiveRecommendations => 
            _recommendations.Where(r => !r.IsDismissed && !r.WasFollowed);

        /// <summary>Unread recommendations.</summary>
        public IEnumerable<Recommendation> UnreadRecommendations =>
            _recommendations.Where(r => !r.IsRead && !r.IsDismissed);

        /// <summary>Recommendations sorted by urgency (highest first).</summary>
        public IEnumerable<Recommendation> PrioritizedRecommendations =>
            ActiveRecommendations.OrderByDescending(r => r.UrgencyRating)
                                .ThenByDescending(r => r.ConfidenceLevel);

        /// <summary>Maximum number of recommendations to keep in memory.</summary>
        public int MaxHistorySize { get; set; } = 1000;

        public RecommendationHistory(string? storagePath = null)
        {
            _storagePath = storagePath;
            if (!string.IsNullOrEmpty(storagePath) && System.IO.File.Exists(storagePath))
            {
                LoadFromDisk();
            }
        }

        // ─── Core Operations ─────────────────────────────────────────────────

        /// <summary>Add a new recommendation to the history.</summary>
        public void AddRecommendation(Recommendation recommendation)
        {
            _recommendations.Add(recommendation);
            
            // Trim history if needed
            if (_recommendations.Count > MaxHistorySize)
            {
                var toRemove = _recommendations.Count - MaxHistorySize;
                _recommendations.RemoveRange(0, toRemove);
            }

            OnRecommendationChanged?.Invoke(recommendation);
            SaveToDisk();
        }

        /// <summary>Mark a recommendation as read.</summary>
        public void MarkAsRead(string recommendationId)
        {
            var rec = _recommendations.FirstOrDefault(r => r.Id == recommendationId);
            if (rec != null && !rec.IsRead)
            {
                rec.IsRead = true;
                OnRecommendationChanged?.Invoke(rec);
                SaveToDisk();
            }
        }

        /// <summary>Mark all recommendations as read.</summary>
        public void MarkAllAsRead()
        {
            bool changed = false;
            foreach (var rec in _recommendations.Where(r => !r.IsRead))
            {
                rec.IsRead = true;
                changed = true;
            }
            
            if (changed)
            {
                OnRecommendationChanged?.Invoke(_recommendations.Last());
                SaveToDisk();
            }
        }

        /// <summary>Dismiss a recommendation (player chose not to follow).</summary>
        public void DismissRecommendation(string recommendationId, string? reason = null)
        {
            var rec = _recommendations.FirstOrDefault(r => r.Id == recommendationId);
            if (rec != null && !rec.IsDismissed)
            {
                rec.IsDismissed = true;
                rec.ResolvedAt = DateTime.UtcNow;
                OnRecommendationChanged?.Invoke(rec);
                SaveToDisk();
            }
        }

        /// <summary>Mark a recommendation as followed (player took the advice).</summary>
        public void MarkAsFollowed(string recommendationId)
        {
            var rec = _recommendations.FirstOrDefault(r => r.Id == recommendationId);
            if (rec != null && !rec.WasFollowed)
            {
                rec.WasFollowed = true;
                rec.ResolvedAt = DateTime.UtcNow;
                OnRecommendationChanged?.Invoke(rec);
                SaveToDisk();
            }
        }

        /// <summary>Record the actual outcome of a followed recommendation.</summary>
        public void RecordOutcome(string recommendationId, RecommendationOutcome outcome)
        {
            var rec = _recommendations.FirstOrDefault(r => r.Id == recommendationId);
            if (rec != null)
            {
                rec.Outcome = outcome;
                rec.WasFollowed = true;
                
                // Calculate feedback score based on expected vs actual
                rec.OutcomeFeedbackScore = CalculateFeedbackScore(rec, outcome);
                
                OnRecommendationChanged?.Invoke(rec);
                UpdatePerformanceCache(rec);
                SaveToDisk();
            }
        }

        /// <summary>Get a specific recommendation by ID.</summary>
        public Recommendation? GetRecommendation(string id)
        {
            return _recommendations.FirstOrDefault(r => r.Id == id);
        }

        /// <summary>Get recommendations by type.</summary>
        public IEnumerable<Recommendation> GetByType(RecommendationType type)
        {
            return _recommendations.Where(r => r.Type == type);
        }

        /// <summary>Get recommendations for a specific zone.</summary>
        public IEnumerable<Recommendation> GetByZone(string zoneId)
        {
            return _recommendations.Where(r => r.TargetZoneId == zoneId);
        }

        /// <summary>Get urgent recommendations (urgency >= 4).</summary>
        public IEnumerable<Recommendation> GetUrgentRecommendations()
        {
            return ActiveRecommendations.Where(r => r.UrgencyRating >= 4);
        }

        // ─── Analytics & Insights ────────────────────────────────────────────

        /// <summary>Get performance statistics for the advisor system.</summary>
        public AdvisorPerformanceMetrics GetPerformanceMetrics()
        {
            var completed = _recommendations.Where(r => r.WasFollowed && r.Outcome != null).ToList();
            var dismissed = _recommendations.Where(r => r.IsDismissed).ToList();
            
            if (completed.Count == 0)
            {
                return new AdvisorPerformanceMetrics();
            }

            var successful = completed.Where(r => r.Outcome?.WasSuccessful ?? false).ToList();
            var totalImpact = completed.Sum(r => r.Outcome?.ActualImpactNOK ?? 0);
            var expectedImpact = completed.Sum(r => r.ExpectedImpactNOK);

            return new AdvisorPerformanceMetrics
            {
                TotalRecommendations = _recommendations.Count,
                FollowedCount = completed.Count,
                DismissedCount = dismissed.Count,
                SuccessRate = (double)successful.Count / completed.Count,
                AverageConfidence = completed.Average(r => r.ConfidenceLevel),
                TotalActualImpactNOK = totalImpact,
                TotalExpectedImpactNOK = expectedImpact,
                AccuracyRatio = expectedImpact != 0 ? (double)(totalImpact / expectedImpact) : 1.0,
                AverageFeedbackScore = completed.Where(r => r.OutcomeFeedbackScore.HasValue)
                                                .Average(r => r.OutcomeFeedbackScore!.Value),
                ByType = completed.GroupBy(r => r.Type)
                    .ToDictionary(g => g.Key, g => new TypePerformanceMetrics
                    {
                        Count = g.Count(),
                        SuccessRate = g.Count(r => r.Outcome?.WasSuccessful ?? false) / (double)g.Count(),
                        AverageImpact = g.Average(r => r.Outcome?.ActualImpactNOK ?? 0)
                    })
            };
        }

        /// <summary>Get player decision patterns.</summary>
        public DecisionPatternAnalysis GetDecisionPatterns()
        {
            var all = _recommendations.Where(r => r.WasFollowed || r.IsDismissed).ToList();
            
            if (all.Count == 0)
            {
                return new DecisionPatternAnalysis();
            }

            return new DecisionPatternAnalysis
            {
                TotalDecisions = all.Count,
                FollowRate = (double)all.Count(r => r.WasFollowed) / all.Count,
                AverageResponseTimeDays = all.Where(r => r.ResolvedAt.HasValue)
                    .Average(r => (r.ResolvedAt!.Value - r.CreatedAt).TotalDays),
                FollowByUrgency = all.GroupBy(r => r.UrgencyRating)
                    .ToDictionary(g => g.Key, g => (double)g.Count(r => r.WasFollowed) / g.Count()),
                FollowByConfidence = all.GroupBy(r => GetConfidenceBucket(r.ConfidenceLevel))
                    .ToDictionary(g => g.Key, g => (double)g.Count(r => r.WasFollowed) / g.Count()),
                PreferredCategories = all.Where(r => r.WasFollowed)
                    .GroupBy(r => r.Category)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key)
                    .Take(3)
                    .ToList()
            };
        }

        /// <summary>Check if a similar recommendation was recently given.</summary>
        public bool WasRecentlyRecommended(RecommendationType type, string? targetId, int daysWindow = 14)
        {
            var cutoff = DateTime.UtcNow.AddDays(-daysWindow);
            return _recommendations.Any(r => 
                r.Type == type && 
                r.CreatedAt > cutoff &&
                (targetId == null || r.TargetSiteId == targetId || r.TargetZoneId == targetId));
        }

        /// <summary>Get recommendations that should be auto-dismissed (outdated).</summary>
        public List<Recommendation> GetOutdatedRecommendations(int currentSimulationDay, int maxAgeDays = 30)
        {
            return _recommendations
                .Where(r => !r.IsDismissed && !r.WasFollowed && 
                       (currentSimulationDay - r.SimulationDay) > maxAgeDays)
                .ToList();
        }

        /// <summary>Auto-dismiss outdated recommendations.</summary>
        public int CleanOutdatedRecommendations(int currentSimulationDay, int maxAgeDays = 30)
        {
            var outdated = GetOutdatedRecommendations(currentSimulationDay, maxAgeDays);
            foreach (var rec in outdated)
            {
                rec.IsDismissed = true;
                rec.ResolvedAt = DateTime.UtcNow;
            }
            
            if (outdated.Count > 0)
            {
                SaveToDisk();
            }
            
            return outdated.Count;
        }

        // ─── Persistence ─────────────────────────────────────────────────────

        private void SaveToDisk()
        {
            if (string.IsNullOrEmpty(_storagePath)) return;
            
            try
            {
                var json = JsonSerializer.Serialize(_recommendations, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                System.IO.File.WriteAllText(_storagePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save recommendation history: {ex.Message}");
            }
        }

        private void LoadFromDisk()
        {
            if (string.IsNullOrEmpty(_storagePath)) return;
            
            try
            {
                var json = System.IO.File.ReadAllText(_storagePath);
                var loaded = JsonSerializer.Deserialize<List<Recommendation>>(json);
                if (loaded != null)
                {
                    _recommendations.Clear();
                    _recommendations.AddRange(loaded);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load recommendation history: {ex.Message}");
            }
        }

        // ─── Private Helpers ─────────────────────────────────────────────────

        private double CalculateFeedbackScore(Recommendation rec, RecommendationOutcome outcome)
        {
            double score = 0;

            // Financial accuracy (40% weight)
            if (rec.ExpectedImpactNOK != 0)
            {
                var expected = Math.Abs((double)rec.ExpectedImpactNOK);
                var actual = Math.Abs((double)outcome.ActualImpactNOK);
                var financialAccuracy = 1.0 - Math.Min(1.0, Math.Abs(expected - actual) / expected);
                score += financialAccuracy * 0.4;
            }

            // Success match (30% weight)
            var predictedSuccess = rec.SuccessProbability > 0.5;
            score += (predictedSuccess == outcome.WasSuccessful ? 1.0 : 0.0) * 0.3;

            // Threshold checks (30% weight)
            double thresholdScore = 0;
            if (rec.BiologicalContext.MortalityRate > 0)
            {
                thresholdScore += outcome.ActualMortalityRate <= rec.BiologicalContext.MortalityRate ? 0.15 : 0;
            }
            score += thresholdScore;

            return Math.Clamp(score, -1.0, 1.0);
        }

        private void UpdatePerformanceCache(Recommendation rec)
        {
            if (!_performanceCache.TryGetValue(rec.Type.ToString(), out var perf))
            {
                perf = new RecommendationPerformance { Type = rec.Type };
                _performanceCache[rec.Type.ToString()] = perf;
            }

            perf.TotalCount++;
            if (rec.WasFollowed)
            {
                perf.FollowedCount++;
                if (rec.Outcome?.WasSuccessful ?? false)
                {
                    perf.SuccessCount++;
                }
            }
        }

        private string GetConfidenceBucket(double confidence)
        {
            return confidence switch
            {
                >= 0.8 => "High",
                >= 0.6 => "Medium",
                _ => "Low"
            };
        }
    }

    /// <summary>Performance metrics for the advisor system.</summary>
    public class AdvisorPerformanceMetrics
    {
        public int TotalRecommendations { get; set; }
        public int FollowedCount { get; set; }
        public int DismissedCount { get; set; }
        public double SuccessRate { get; set; }
        public double AverageConfidence { get; set; }
        public decimal TotalActualImpactNOK { get; set; }
        public decimal TotalExpectedImpactNOK { get; set; }
        public double AccuracyRatio { get; set; }
        public double AverageFeedbackScore { get; set; }
        public Dictionary<RecommendationType, TypePerformanceMetrics> ByType { get; set; } = new();
    }

    public class TypePerformanceMetrics
    {
        public int Count { get; set; }
        public double SuccessRate { get; set; }
        public decimal AverageImpact { get; set; }
    }

    /// <summary>Analysis of player decision patterns.</summary>
    public class DecisionPatternAnalysis
    {
        public int TotalDecisions { get; set; }
        public double FollowRate { get; set; }
        public double AverageResponseTimeDays { get; set; }
        public Dictionary<int, double> FollowByUrgency { get; set; } = new();
        public Dictionary<string, double> FollowByConfidence { get; set; } = new();
        public List<RecommendationCategory> PreferredCategories { get; set; } = new();
    }

    /// <summary>Cached performance data for a recommendation type.</summary>
    public class RecommendationPerformance
    {
        public RecommendationType Type { get; set; }
        public int TotalCount { get; set; }
        public int FollowedCount { get; set; }
        public int SuccessCount { get; set; }
        public double SuccessRate => FollowedCount > 0 ? (double)SuccessCount / FollowedCount : 0;
    }
}
