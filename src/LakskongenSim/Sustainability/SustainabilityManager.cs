using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Core;

namespace LakskongenSim.Sustainability
{
    /// <summary>
    /// Certification levels based on sustainability score (0-100).
    /// Higher tiers unlock premium markets and grant financial bonuses.
    /// </summary>
    public enum CertificationLevel
    {
        NonCompliant,       // 0-49: Can't export to EU, penalties
        BasicCompliance,    // 50-69: Standard markets only
        ASCCertified,       // 70-84: Premium buyers +8% price
        OrganicCertified,   // 85-94: Specialty markets +15% price
        SustainabilityLeader // 95-100: ESG bonuses, grants, awards
    }

    /// <summary>
    /// Manages sustainability scoring (0-100) and green certification tiers.
    /// Integrates with existing systems (Disease, Feed, Logistics, Fish Population)
    /// to calculate a composite score across 6 categories.
    /// 
    /// Differentiator vs Farming Simulator 25: Deep sustainability mechanics
    /// that unlock premium markets (+12-29% margins) and ESG investment bonuses.
    /// </summary>
    public class SustainabilityManager
    {
        // Score weights (must sum to 100)
        private const int WEIGHT_SEA_LICE = 25;
        private const int WEIGHT_ESCAPE_PREVENTION = 20;
        private const int WEIGHT_FEED_SUSTAINABILITY = 20;
        private const int WEIGHT_CARBON_FOOTPRINT = 15;
        private const int WEIGHT_WELFARE = 10;
        private const int WEIGHT_ECOSYSTEM = 10;

        // Events for UI updates
        public event Action<int, CertificationLevel>? OnScoreChanged; // score, level
        public event Action<CertificationLevel>? OnCertificationChanged;

        // Configuration (loaded from JSON)
        private SustainabilityConfig _config;

        /// <summary>
        /// Days between sustainability assessments; the first is on this day.
        /// Before it the score is not yet assessed (it reads 0), which the UI
        /// must not present as a verdict (SHIP_PLAN cycle 1: every new game
        /// warned "Bærekraft under kravet: 0/100" through its first week).
        /// </summary>
        public const int AssessmentIntervalDays = 7;

        public int CurrentScore { get; private set; }
        public CertificationLevel CurrentCertification { get; private set; }

        public SustainabilityManager(SustainabilityConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            CurrentScore = 0;
            CurrentCertification = CertificationLevel.NonCompliant;
        }

        /// <summary>
        /// Calculate sustainability score (0-100) from current game state.
        /// Pure function - no side effects, testable.
        /// </summary>
        public int CalculateScore(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            var scores = GetComponentScores(state);
            var total = scores.Values.Sum();
            return Math.Clamp(total, 0, 100);
        }

        /// <summary>
        /// Get detailed breakdown of score components for UI display.
        /// Returns dictionary: category -> points earned (0-max for that category)
        /// </summary>
        public Dictionary<string, int> GetComponentScores(GameState state)
        {
            var scores = new Dictionary<string, int>
            {
                ["SeaLiceManagement"] = CalculateSeaLiceScore(state),
                ["EscapePrevention"] = CalculateEscapeScore(state),
                ["FeedSustainability"] = CalculateFeedScore(state),
                ["CarbonFootprint"] = CalculateCarbonScore(state),
                ["WelfareStandards"] = CalculateWelfareScore(state),
                ["EcosystemImpact"] = CalculateEcosystemScore(state)
            };

            return scores;
        }

        /// <summary>
        /// Get certification level from score (0-100).
        /// </summary>
        public CertificationLevel GetCertificationLevel(int score)
        {
            score = Math.Clamp(score, 0, 100);

            if (score >= 95) return CertificationLevel.SustainabilityLeader;
            if (score >= 85) return CertificationLevel.OrganicCertified;
            if (score >= 70) return CertificationLevel.ASCCertified;
            if (score >= 50) return CertificationLevel.BasicCompliance;
            return CertificationLevel.NonCompliant;
        }

        /// <summary>
        /// Get price multiplier based on certification level.
        /// Premium markets pay more for sustainable products.
        /// </summary>
        public decimal GetPriceMultiplier(CertificationLevel level)
        {
            return level switch
            {
                CertificationLevel.SustainabilityLeader => 1.15m, // +15% (same as Organic, plus grants)
                CertificationLevel.OrganicCertified => 1.15m,     // +15%
                CertificationLevel.ASCCertified => 1.08m,         // +8%
                CertificationLevel.BasicCompliance => 1.00m,      // Standard price
                CertificationLevel.NonCompliant => 0.85m,         // -15% penalty (limited markets)
                _ => 1.00m
            };
        }

        /// <summary>
        /// Apply a direct score penalty from a regulatory storyteller event.
        /// Immediately reduces score and fires change events if certification level shifts.
        /// </summary>
        public void ApplyRegulatoryPenalty(int penaltyPoints)
        {
            var penalizedScore = Math.Clamp(CurrentScore - penaltyPoints, 0, 100);
            var penalizedLevel = GetCertificationLevel(penalizedScore);

            var levelChanged = penalizedLevel != CurrentCertification;
            CurrentScore = penalizedScore;
            CurrentCertification = penalizedLevel;

            OnScoreChanged?.Invoke(CurrentScore, CurrentCertification);
            if (levelChanged) OnCertificationChanged?.Invoke(CurrentCertification);
        }

        /// <summary>
        /// Update score and trigger events if changed.
        /// Call this periodically (e.g., weekly/monthly) from simulation engine.
        /// </summary>
        /// <summary>Persistence (Phase 1 wave 6): rehydrate the live score
        /// and level from saved state so the first post-load update does not
        /// fire a spurious certification change (which would overwrite the
        /// fully-computed market price with a partial formula).</summary>
        internal void ImportState(int score, CertificationLevel level)
        {
            CurrentScore = score;
            CurrentCertification = level;
        }

        /// <summary>
        /// Calculate sustainability score (0-100) from current game state.
        /// Pure function - no side effects, testable.
        /// </summary>
        public int CalculateScore(GameState state, double bonus = 0.0)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            var scores = GetComponentScores(state);
            var total = scores.Values.Sum();
            return Math.Clamp((int)(total + bonus), 0, 100);
        }

        /// <summary>
        /// Update score with research bonus.
        /// </summary>
        public void UpdateScore(GameState state, double bonus = 0.0)
        {
            var newScore = CalculateScore(state, bonus);
            var newLevel = GetCertificationLevel(newScore);

            var scoreChanged = newScore != CurrentScore;
            var levelChanged = newLevel != CurrentCertification;

            CurrentScore = newScore;
            CurrentCertification = newLevel;

            if (scoreChanged)
                OnScoreChanged?.Invoke(CurrentScore, CurrentCertification);

            if (levelChanged)
                OnCertificationChanged?.Invoke(CurrentCertification);
        }

        // ===== COMPONENT SCORING METHODS =====
        // Each returns 0 to MAX_POINTS for that category

        private int CalculateSeaLiceScore(GameState state)
        {
            // Average lice per fish across all populations
            // Target: <0.2 lice/fish = 25 pts, >0.5 = 0 pts
            // Bonus: +5 pts if using mechanical delousing (tracked via state metadata)

            var totalFish = 0;
            var totalLice = 0.0;

            foreach (var pop in state.Populations)
            {
                var fishCount = pop.Cohorts.Sum(c => c.Count);
                totalFish += fishCount;
                totalLice += fishCount * pop.SeaLiceLoad;
            }

            if (totalFish == 0) return WEIGHT_SEA_LICE; // No fish = perfect score (edge case)

            // Calculate weighted average lice per fish
            var avgLicePerFish = totalLice / totalFish;

            int score;
            if (avgLicePerFish <= 0.2)
                score = WEIGHT_SEA_LICE;
            else if (avgLicePerFish <= 0.5)
                score = (int)((0.5 - avgLicePerFish) / 0.3 * WEIGHT_SEA_LICE);
            else
                score = 0;

            // Bonus for mechanical delousing (no chemicals)
            // Check EventLog for recent mechanical delousing events in last 365 days
            var recentMechanicalDelousing = state.EventLog.Any(e => 
                e.Id.Contains("mechanical", StringComparison.OrdinalIgnoreCase) && 
                e.Id.Contains("delous", StringComparison.OrdinalIgnoreCase) &&
                e.Day >= state.Day - 365);
            
            if (recentMechanicalDelousing) 
                score = Math.Min(score + 5, WEIGHT_SEA_LICE);

            return Math.Clamp(score, 0, WEIGHT_SEA_LICE);
        }

        private int CalculateEscapeScore(GameState state)
        {
            // Escape incidents in the last 365 days, not fish (F457): this
            // used to add every site's escaped-fish counter, which is never
            // reset, so one storm escape of 11+ fish zeroed the category for
            // the rest of the game (its event-log Id filter matched nothing:
            // escape records carry no Id). Zero incidents = full points, up to
            // AcceptableMax = half, more = none; closed containment adds a bonus.
            var thresholds = _config.Escapes;
            int incidents = state.SeaSites.Sum(s => s.EscapeIncidentsInLastYear(state.Day));

            int score;
            if (incidents <= thresholds.PerfectMax)
                score = WEIGHT_ESCAPE_PREVENTION;
            else if (incidents <= thresholds.AcceptableMax)
                score = WEIGHT_ESCAPE_PREVENTION / 2;
            else
                score = 0;

            // Bonus for closed containment (RAS, semi-closed)
            if (state.SeaSites.Any(s => s.IsClosedContainment))
                score = Math.Min(score + thresholds.ClosedContainmentBonus, WEIGHT_ESCAPE_PREVENTION);

            return Math.Clamp(score, 0, WEIGHT_ESCAPE_PREVENTION);
        }

        private int CalculateFeedScore(GameState state)
        {
            // 100% sustainable sources = 20 pts
            // Insect meal/algae use = bonus
            // Fishmeal from overfished stocks = penalty

            // Use FeedSustainabilityRating from GameState (0.0-1.0)
            var sustainabilityRating = (float)state.FeedSustainabilityRating;

            var score = (int)(sustainabilityRating * WEIGHT_FEED_SUSTAINABILITY);

            // Bonus for advanced feed types (insect meal, algae)
            // Check if research has been unlocked
            var hasInsectMeal = state.UnlockedResearchIds.Contains("insect-meal", StringComparer.OrdinalIgnoreCase);
            var hasAlgaeFeed = state.UnlockedResearchIds.Contains("algae-feed", StringComparer.OrdinalIgnoreCase);
            
            if (hasInsectMeal || hasAlgaeFeed) 
                score += 5;

            return Math.Clamp(score, 0, WEIGHT_FEED_SUSTAINABILITY);
        }

        private int CalculateCarbonScore(GameState state)
        {
            // Points based on:
            // - Renewable energy use (solar/wind/tidal)
            // - Transport efficiency (local processing vs long-haul)
            // - Vessel fuel type (diesel vs hybrid vs electric)

            float carbonEfficiency;
            
            if (state.LogisticsManager != null)
            {
                // Use actual carbon footprint score from LogisticsManager
                carbonEfficiency = state.LogisticsManager.CarbonFootprintScore();
            }
            else
            {
                // Fallback: estimate based on research unlocks
                float renewableBonus = 0.0f;
                float electricBonus = 0.0f;
                
                // Check for renewable energy research
                if (state.UnlockedResearchIds.Contains("solar-power", StringComparer.OrdinalIgnoreCase) ||
                    state.UnlockedResearchIds.Contains("wind-power", StringComparer.OrdinalIgnoreCase) ||
                    state.UnlockedResearchIds.Contains("renewable-energy", StringComparer.OrdinalIgnoreCase))
                {
                    renewableBonus = 0.3f;
                }
                
                // Check for electric vehicles research
                if (state.UnlockedResearchIds.Contains("electric-vehicles", StringComparer.OrdinalIgnoreCase) ||
                    state.UnlockedResearchIds.Contains("hybrid-vessels", StringComparer.OrdinalIgnoreCase))
                {
                    electricBonus = 0.2f;
                }
                
                carbonEfficiency = 0.3f + renewableBonus + electricBonus; // Base 30% + bonuses
            }

            var score = (int)(carbonEfficiency * WEIGHT_CARBON_FOOTPRINT);

            return Math.Clamp(score, 0, WEIGHT_CARBON_FOOTPRINT);
        }

        private int CalculateWelfareScore(GameState state)
        {
            // Points based on:
            // - Stocking density below max
            // - Handling stress minimization
            // - Slaughter methods (electrical stunning = best practice)

            var totalDensityScore = 0.0;
            var siteCount = state.SeaSites.Count;

            foreach (var site in state.SeaSites)
            {
                // Calculate actual biomass from populations
                double totalBiomassKg = 0.0;
                
                // Sum biomass from all pens at this site
                foreach (var pen in site.Pens)
                {
                    foreach (var cohort in pen.Cohorts)
                    {
                        totalBiomassKg += cohort.Count * cohort.MeanWeightKg;
                    }
                }
                
                // Calculate density ratio vs max allowed
                double maxBiomassKg = site.PenVolume * site.MaxAllowedDensity;
                double densityRatio = maxBiomassKg > 0 ? totalBiomassKg / maxBiomassKg : 0.0;
                
                // Score: Under 80% capacity = 1.0, 80-100% = partial, over 100% = 0
                if (densityRatio < 0.8)
                    totalDensityScore += 1.0;
                else if (densityRatio < 1.0)
                    totalDensityScore += (1.0 - densityRatio) / 0.2; // Linear decline from 0.8 to 1.0
                else
                    totalDensityScore += 0.0; // Overcrowded
            }

            var avgDensityScore = siteCount > 0 ? totalDensityScore / siteCount : 1.0;
            var score = (int)(avgDensityScore * WEIGHT_WELFARE);

            return Math.Clamp(score, 0, WEIGHT_WELFARE);
        }

        private int CalculateEcosystemScore(GameState state)
        {
            // Points based on:
            // - Benthic monitoring (seabed health under pens)
            // - Nutrient discharge levels
            // - Wild fish attraction/displacement

            var totalEcosystemScore = 0.0;
            var zoneCount = state.Zones.Count;

            if (zoneCount == 0)
                return WEIGHT_ECOSYSTEM; // No zones = perfect score (edge case)

            foreach (var zone in state.Zones)
            {
                // 1. Oxygen level score (0.0-0.5)
                // Good oxygen (>7 mg/L) = 0.5, poor (<5 mg/L) = 0.0
                double oxygenScore = zone.WaterChemistry.OxygenO2 >= 7.0 ? 0.5 : 
                                    zone.WaterChemistry.OxygenO2 >= 5.0 ? (zone.WaterChemistry.OxygenO2 - 5.0) / 4.0 : 0.0;

                // 2. Nutrient load score (0.0-0.3)
                // Low nutrients = better. Nitrate <1.0 mg/L = 0.3, >5.0 = 0.0
                double nitrateLevel = zone.WaterChemistry.NitrateNO3;
                double nutrientScore = nitrateLevel <= 1.0 ? 0.3 : 
                                      nitrateLevel <= 5.0 ? 0.3 * (5.0 - nitrateLevel) / 4.0 : 0.0;

                // 3. pH stability score (0.0-0.2)
                // pH 7.5-8.5 = ideal (0.2), outside this range = reduced
                double pH = zone.WaterChemistry.pH;
                double pHScore = (pH >= 7.5 && pH <= 8.5) ? 0.2 : 
                                (pH >= 6.5 && pH <= 9.0) ? 0.1 : 0.0;

                totalEcosystemScore += oxygenScore + nutrientScore + pHScore;
            }

            // Average ecosystem health across all zones
            var avgEcosystemHealth = (float)(totalEcosystemScore / zoneCount);

            // Check for fallow rotation (improves ecosystem recovery)
            // Look for fallow events in EventLog
            var hasFallowRotation = state.EventLog.Any(e => 
                e.Id.Contains("fallow", StringComparison.OrdinalIgnoreCase) && 
                e.Day >= state.Day - 365);
            
            if (hasFallowRotation)
                avgEcosystemHealth = Math.Min(avgEcosystemHealth + 0.1f, 1.0f);

            var score = (int)(avgEcosystemHealth * WEIGHT_ECOSYSTEM);

            return Math.Clamp(score, 0, WEIGHT_ECOSYSTEM);
        }
    }

    /// <summary>
    /// Configuration data for sustainability scoring.
    /// Loaded from Data/SustainabilityConfig.json.
    /// </summary>
    public class SustainabilityConfig
    {
        // Thresholds for each category (can be tuned for game balance)
        public SeaLiceThresholds SeaLice { get; set; } = new();
        public EscapeThresholds Escapes { get; set; } = new();
        public FeedThresholds Feed { get; set; } = new();
        public CarbonThresholds Carbon { get; set; } = new();
        public WelfareThresholds Welfare { get; set; } = new();
        public EcosystemThresholds Ecosystem { get; set; } = new();

        // Certification requirements
        public Dictionary<string, int> CertificationMinScores { get; set; } = new()
        {
            ["NonCompliant"] = 0,
            ["BasicCompliance"] = 50,
            ["ASCCertified"] = 70,
            ["OrganicCertified"] = 85,
            ["SustainabilityLeader"] = 95
        };

        // Price multipliers
        public Dictionary<string, decimal> PriceMultipliers { get; set; } = new()
        {
            ["NonCompliant"] = 0.85m,
            ["BasicCompliance"] = 1.00m,
            ["ASCCertified"] = 1.08m,
            ["OrganicCertified"] = 1.15m,
            ["SustainabilityLeader"] = 1.15m
        };

        // Bonus/penalty values
        public decimal ESGInvestmentBonus { get; set; } = 0.30m; // 30% subsidy for green tech at 80+ score
        public decimal GovernmentGrantMultiplier { get; set; } = 1.5m; // 1.5x grants at 95+ score
        public decimal NonComplianceFinePerQuarter { get; set; } = 50_000m; // NOK fine if <50 score
    }

    // Threshold classes for JSON deserialization
    public class SeaLiceThresholds
    {
        public double ExcellentMax { get; set; } = 0.2; // <0.2 lice/fish
        public double AcceptableMax { get; set; } = 0.5; // 0.2-0.5
        public int MechanicalDelouseBonus { get; set; } = 5; // Bonus points
    }

    public class EscapeThresholds
    {
        public int PerfectMax { get; set; } = 0; // Zero escapes
        public int AcceptableMax { get; set; } = 10; // 1-10 escapes
        public int ClosedContainmentBonus { get; set; } = 5;
    }

    public class FeedThresholds
    {
        public double SustainableMinimum { get; set; } = 0.8; // 80% sustainable sources
        public int AdvancedFeedBonus { get; set; } = 5; // Insect meal/algae bonus
    }

    public class CarbonThresholds
    {
        public double RenewableEnergyTarget { get; set; } = 0.5; // 50% renewable
        public int ElectricVesselBonus { get; set; } = 3;
    }

    public class WelfareThresholds
    {
        public double MaxDensityRatio { get; set; } = 0.8; // Stocking <80% of max
        public int ElectricalStunningBonus { get; set; } = 2;
    }

    public class EcosystemThresholds
    {
        public double BenthicHealthMinimum { get; set; } = 0.7; // 70% healthy seabed
        public double NutrientDischargeMax { get; set; } = 100.0; // kg/day limit
    }
}
