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
        public void UpdateScore(GameState state)
        {
            var newScore = CalculateScore(state);
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

            foreach (var pop in state.Populations)
            {
                var fishCount = pop.Cohorts.Sum(c => c.Count);
                totalFish += fishCount;
                // Assume sea lice data is tracked per population (future integration)
                // For now, use placeholder - will integrate with SeaLiceModel
                // totalLice += fishCount * pop.LicePerFish;
            }

            if (totalFish == 0) return WEIGHT_SEA_LICE; // No fish = perfect score (edge case)

            // Placeholder calculation - INTEGRATION POINT with Health/SeaLiceModel.cs
            // TODO: Access actual lice count from SeaLiceModel.AverageLicePerFish
            var avgLicePerFish = 0.3; // Placeholder

            int score;
            if (avgLicePerFish <= 0.2)
                score = WEIGHT_SEA_LICE;
            else if (avgLicePerFish <= 0.5)
                score = (int)((0.5 - avgLicePerFish) / 0.3 * WEIGHT_SEA_LICE);
            else
                score = 0;

            // Bonus for mechanical delousing (no chemicals)
            // TODO: Check if last delousing method was mechanical
            // if (state.UsedMechanicalDelousing) score = Math.Min(WEIGHT_SEA_LICE + 5, WEIGHT_SEA_LICE);

            return Math.Clamp(score, 0, WEIGHT_SEA_LICE);
        }

        private int CalculateEscapeScore(GameState state)
        {
            // Zero escapes/year = 20 pts, 1-10 = 10 pts, >10 = 0 pts
            // Bonus: +5 pts for closed containment systems

            // INTEGRATION POINT: SeaSite should track escape events
            var totalEscapes = 0;
            foreach (var site in state.SeaSites)
            {
                // TODO: Access site.EscapeCount (add to SeaSite.cs)
                // totalEscapes += site.EscapesThisYear;
            }

            int score;
            if (totalEscapes == 0)
                score = WEIGHT_ESCAPE_PREVENTION;
            else if (totalEscapes <= 10)
                score = WEIGHT_ESCAPE_PREVENTION / 2;
            else
                score = 0;

            // Bonus for closed containment (RAS, semi-closed)
            // TODO: Check if sites use closed systems
            // if (state.SeaSites.Any(s => s.IsClosedContainment)) score = Math.Min(score + 5, WEIGHT_ESCAPE_PREVENTION);

            return Math.Clamp(score, 0, WEIGHT_ESCAPE_PREVENTION);
        }

        private int CalculateFeedScore(GameState state)
        {
            // 100% sustainable sources = 20 pts
            // Insect meal/algae use = bonus
            // Fishmeal from overfished stocks = penalty

            // INTEGRATION POINT: FeedSystem should track feed source sustainability
            // TODO: Access FeedSystem.SustainabilityRating (0.0-1.0)
            var sustainabilityRating = 0.7f; // Placeholder: 70% sustainable

            var score = (int)(sustainabilityRating * WEIGHT_FEED_SUSTAINABILITY);

            // Bonus for advanced feed types
            // TODO: Check feed composition
            // if (feedSystem.UsesInsectMeal || feedSystem.UsesAlgae) score += 5;

            return Math.Clamp(score, 0, WEIGHT_FEED_SUSTAINABILITY);
        }

        private int CalculateCarbonScore(GameState state)
        {
            // Points based on:
            // - Renewable energy use (solar/wind/tidal)
            // - Transport efficiency (local processing vs long-haul)
            // - Vessel fuel type (diesel vs hybrid vs electric)

            // INTEGRATION POINT: LogisticsManager should track carbon footprint
            // TODO: Access LogisticsManager.CarbonFootprintScore (0.0-1.0)
            var carbonEfficiency = 0.5f; // Placeholder: 50% efficient

            var score = (int)(carbonEfficiency * WEIGHT_CARBON_FOOTPRINT);

            return Math.Clamp(score, 0, WEIGHT_CARBON_FOOTPRINT);
        }

        private int CalculateWelfareScore(GameState state)
        {
            // Points based on:
            // - Stocking density below max
            // - Handling stress minimization
            // - Slaughter methods (electrical stunning = best practice)

            // INTEGRATION POINT: FishPopulation/SeaSite should track stocking density
            var totalDensityScore = 0.0;
            var siteCount = state.SeaSites.Count;

            foreach (var site in state.SeaSites)
            {
                // TODO: Calculate actual density vs max allowed
                // var densityRatio = site.CurrentDensity / site.MaxDensity;
                // if (densityRatio < 0.8) totalDensityScore += 1.0; // Under 80% = good
                totalDensityScore += 0.75; // Placeholder
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

            // INTEGRATION POINT: New EcosystemMonitor system or add to Zone.cs
            // TODO: Track benthic oxygen levels, nutrient load
            var ecosystemHealth = 0.6f; // Placeholder: 60% healthy

            var score = (int)(ecosystemHealth * WEIGHT_ECOSYSTEM);

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
