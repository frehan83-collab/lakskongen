using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Market;

namespace LakskongenSim.Processing
{
    /// <summary>
    /// Brand positioning strategy affects pricing power and market access
    /// </summary>
    public enum BrandStrategy
    {
        Commodity,        // Compete on price, no branding
        Organic,          // Sustainable/organic positioning
        Premium,          // Quality focus, Norwegian heritage
        Luxury,           // Ultra-premium, exclusive
        Regional          // Local/regional emphasis
    }
    
    /// <summary>
    /// Reputation events that affect brand value
    /// </summary>
    public enum ReputationEvent
    {
        QualityIssue,         // Quality problems, recalls
        CertificationEarned,  // ASC, MSC, organic
        AwardWon,             // Industry recognition
        MediaCoverage,        // Positive press
        CustomerComplaint,    // Negative feedback
        EnvironmentalIncident,// Pollution, fish escape
        MarketExpansion,      // Enter new market
        ConsistentQuality     // Long track record
    }
    
    /// <summary>
    /// Tracks brand reputation and its impact on pricing power
    /// </summary>
    public class BrandReputation
    {
        public string BrandName { get; set; } = string.Empty;
        public BrandStrategy Strategy { get; set; } = BrandStrategy.Commodity;
        
        // Reputation metrics (0-100 scale)
        public decimal QualityReputation { get; set; } = 50m;
        public decimal SustainabilityReputation { get; set; } = 50m;
        public decimal ReliabilityReputation { get; set; } = 50m;
        public decimal InnovationReputation { get; set; } = 50m;
        
        // Market presence
        public HashSet<string> ActiveMarkets { get; set; } = new();
        public Dictionary<string, decimal> MarketShareByRegion { get; set; } = new();
        
        // Certifications and awards
        public HashSet<string> Certifications { get; set; } = new();
        public int AwardsCount { get; set; } = 0;
        
        // Historical tracking
        public List<ReputationHistoryEntry> History { get; set; } = new();
        
        /// <summary>
        /// Calculate overall brand score (0-100)
        /// </summary>
        public decimal OverallScore
        {
            get
            {
                var weights = Strategy switch
                {
                    BrandStrategy.Commodity => (quality: 0.2m, sustainability: 0.1m, reliability: 0.5m, innovation: 0.2m),
                    BrandStrategy.Organic => (quality: 0.3m, sustainability: 0.5m, reliability: 0.1m, innovation: 0.1m),
                    BrandStrategy.Premium => (quality: 0.5m, sustainability: 0.2m, reliability: 0.2m, innovation: 0.1m),
                    BrandStrategy.Luxury => (quality: 0.6m, sustainability: 0.2m, reliability: 0.1m, innovation: 0.1m),
                    BrandStrategy.Regional => (quality: 0.3m, sustainability: 0.3m, reliability: 0.3m, innovation: 0.1m),
                    _ => (quality: 0.25m, sustainability: 0.25m, reliability: 0.25m, innovation: 0.25m)
                };
                
                return QualityReputation * weights.quality +
                       SustainabilityReputation * weights.sustainability +
                       ReliabilityReputation * weights.reliability +
                       InnovationReputation * weights.innovation;
            }
        }
        
        /// <summary>
        /// Calculate pricing power multiplier based on reputation
        /// </summary>
        public decimal GetPricingPowerMultiplier(ProductType product)
        {
            decimal baseMultiplier = 1.0m;
            
            // Overall brand strength
            decimal brandBonus = (OverallScore - 50m) / 100m * 0.4m;  // Max ±20% from brand
            
            // Strategy alignment with product
            if (!ProductCatalog.Products.TryGetValue(product, out var def))
                return baseMultiplier;
            
            decimal strategyBonus = (Strategy, def.Position) switch
            {
                (BrandStrategy.Luxury, MarketPosition.Luxury) => 0.15m,
                (BrandStrategy.Premium, MarketPosition.Premium) => 0.10m,
                (BrandStrategy.Premium, MarketPosition.Luxury) => 0.08m,
                (BrandStrategy.Organic, MarketPosition.Differentiated) => 0.12m,
                (BrandStrategy.Regional, MarketPosition.Differentiated) => 0.08m,
                (BrandStrategy.Commodity, MarketPosition.Commodity) => 0.02m,
                _ => 0m
            };
            
            // Certifications add value for certified products
            decimal certBonus = 0m;
            if (def.RequiresCertification && Certifications.Count > 0)
                certBonus = Math.Min(0.10m, Certifications.Count * 0.03m);
            
            // Awards add prestige
            decimal awardBonus = Math.Min(0.08m, AwardsCount * 0.02m);
            
            return baseMultiplier + brandBonus + strategyBonus + certBonus + awardBonus;
        }
        
        /// <summary>
        /// Record a reputation event
        /// </summary>
        public void RecordEvent(ReputationEvent eventType, string description, int severity = 1)
        {
            var entry = new ReputationHistoryEntry
            {
                Date = DateTime.UtcNow,
                EventType = eventType,
                Description = description,
                Severity = severity
            };
            
            History.Add(entry);
            
            // Apply reputation impact
            ApplyReputationImpact(eventType, severity);
        }
        
        /// <summary>
        /// Apply reputation change from an event
        /// </summary>
        private void ApplyReputationImpact(ReputationEvent eventType, int severity)
        {
            decimal impact = severity * 2m; // Base impact
            
            switch (eventType)
            {
                case ReputationEvent.QualityIssue:
                    QualityReputation = Math.Max(0, QualityReputation - impact * 3m);
                    ReliabilityReputation = Math.Max(0, ReliabilityReputation - impact * 2m);
                    break;
                    
                case ReputationEvent.CertificationEarned:
                    SustainabilityReputation = Math.Min(100, SustainabilityReputation + impact * 2m);
                    QualityReputation = Math.Min(100, QualityReputation + impact);
                    break;
                    
                case ReputationEvent.AwardWon:
                    AwardsCount++;
                    QualityReputation = Math.Min(100, QualityReputation + impact * 2m);
                    InnovationReputation = Math.Min(100, InnovationReputation + impact);
                    break;
                    
                case ReputationEvent.MediaCoverage:
                    // Boost all metrics moderately
                    QualityReputation = Math.Min(100, QualityReputation + impact);
                    SustainabilityReputation = Math.Min(100, SustainabilityReputation + impact);
                    break;
                    
                case ReputationEvent.CustomerComplaint:
                    QualityReputation = Math.Max(0, QualityReputation - impact);
                    ReliabilityReputation = Math.Max(0, ReliabilityReputation - impact * 1.5m);
                    break;
                    
                case ReputationEvent.EnvironmentalIncident:
                    SustainabilityReputation = Math.Max(0, SustainabilityReputation - impact * 4m);
                    QualityReputation = Math.Max(0, QualityReputation - impact);
                    break;
                    
                case ReputationEvent.MarketExpansion:
                    InnovationReputation = Math.Min(100, InnovationReputation + impact);
                    break;
                    
                case ReputationEvent.ConsistentQuality:
                    QualityReputation = Math.Min(100, QualityReputation + impact * 0.5m);
                    ReliabilityReputation = Math.Min(100, ReliabilityReputation + impact);
                    break;
            }
        }
        
        /// <summary>
        /// Natural reputation decay over time (use sparingly)
        /// </summary>
        public void ApplyNaturalDecay(decimal decayRate = 0.01m)
        {
            // Reputation slowly drifts toward 50 (neutral) without reinforcement
            QualityReputation += (50m - QualityReputation) * decayRate;
            SustainabilityReputation += (50m - SustainabilityReputation) * decayRate;
            ReliabilityReputation += (50m - ReliabilityReputation) * decayRate;
            InnovationReputation += (50m - InnovationReputation) * decayRate;
        }
        
        /// <summary>
        /// Get marketing investment needed to maintain/grow reputation
        /// </summary>
        public decimal GetRecommendedMarketingBudget()
        {
            return Strategy switch
            {
                BrandStrategy.Commodity => 50_000m,      // Minimal
                BrandStrategy.Organic => 200_000m,       // Moderate
                BrandStrategy.Premium => 500_000m,       // Significant
                BrandStrategy.Luxury => 1_000_000m,      // Heavy investment
                BrandStrategy.Regional => 150_000m,
                _ => 100_000m
            };
        }
        
        /// <summary>
        /// Check if brand is strong enough for a market entry
        /// </summary>
        public bool CanEnterMarket(string market, MarketPosition requiredPosition)
        {
            decimal minScore = requiredPosition switch
            {
                MarketPosition.Luxury => 75m,
                MarketPosition.Premium => 60m,
                MarketPosition.Differentiated => 50m,
                MarketPosition.Commodity => 30m,
                _ => 40m
            };
            
            return OverallScore >= minScore;
        }
    }
    
    /// <summary>
    /// Historical record of reputation event
    /// </summary>
    public class ReputationHistoryEntry
    {
        public DateTime Date { get; set; }
        public ReputationEvent EventType { get; set; }
        public string Description { get; set; } = string.Empty;
        public int Severity { get; set; }
    }
}
