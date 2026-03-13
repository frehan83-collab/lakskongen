using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Finance;

namespace LakskongenSim.Expansion
{
    public enum IntegrationTier
    {
        GENETICS,
        HATCHERY,
        FEED_MILL,
        SEA_FARM,
        PROCESSING,
        COLD_CHAIN,
        DISTRIBUTION
    }
    
    public class IntegrationAsset
    {
        public IntegrationTier Tier { get; set; }
        public string Name { get; set; } = string.Empty;
        public float CapexNOK { get; set; }
        public float AnnualOpexNOK { get; set; }
        public int Capacity { get; set; } // Varies by tier - tonnes/year, fish/year, etc.
        public float CostSavingPctPerKg { get; set; } // 0-1 (e.g. 0.05 = 5% cost reduction)
        public float QualityBonus { get; set; } // 0-1 (e.g. 0.03 = 3% quality premium)
        public float CompetitorLockoutRadius { get; set; } // km radius of competitive advantage
        public string Description { get; set; } = string.Empty;
        public Dictionary<string, object> UnlockConditions { get; set; } = new();
        public float MaintenanceCostPctPerYear { get; set; } // 0-1 (e.g. 0.02 = 2% of CAPEX annually)
        public bool IsOwned { get; set; } = false;
        public DateTime? ConstructionStarted { get; set; }
        public DateTime? ConstructionCompleted { get; set; }
        public int ConstructionTimeMonths { get; set; }
    }
    
    public class IntegrationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public IntegrationAsset? Asset { get; set; }
        public float ActualCapexPaid { get; set; }
        public float EstimatedPaybackYears { get; set; }
        public float CompetitiveAdvantageScore { get; set; }
        public Dictionary<string, float> SynergyBenefits { get; set; } = new();
    }
    
    public class VerticalIntegrationSystem
    {
        private readonly FinanceManager _financeManager;
        private readonly List<IntegrationAsset> _allAssets;
        private readonly List<IntegrationAsset> _ownedAssets;
        
        // Integration scoring constants
        private const float FULLY_INTEGRATED_BONUS = 0.20f; // 20% EBITDA bonus
        private const int FULLY_INTEGRATED_THRESHOLD = 3; // Need 3+ tiers
        private const float BASE_INTEGRATION_SCORE_PER_TIER = 0.15f;
        private const float SYNERGY_MULTIPLIER = 1.25f;
        
        public event Action<IntegrationTier, float>? OnAssetBuilt;
        public event Action<float>? OnIntegrationMilestoneReached;
        
        public VerticalIntegrationSystem(FinanceManager financeManager)
        {
            _financeManager = financeManager ?? throw new ArgumentNullException(nameof(financeManager));
            _allAssets = new List<IntegrationAsset>();
            _ownedAssets = new List<IntegrationAsset>();
        }
        
        public List<IntegrationAsset> GetBuildableAssets()
        {
            return _allAssets
                .Where(asset => !asset.IsOwned && IsUnlocked(asset))
                .OrderBy(asset => asset.CapexNOK)
                .ToList();
        }
        
        private bool IsUnlocked(IntegrationAsset asset)
        {
            // Check unlock conditions
            foreach (var condition in asset.UnlockConditions)
            {
                switch (condition.Key.ToLower())
                {
                    case "min_annual_production_kg":
                        var minProduction = Convert.ToSingle(condition.Value);
                        // This would check actual player production - placeholder
                        if (GetPlayerAnnualProduction() < minProduction)
                            return false;
                        break;
                        
                    case "required_tier":
                        var tierStr = condition.Value?.ToString() ?? string.Empty;
                        if (!Enum.TryParse<IntegrationTier>(tierStr, out var requiredTier))
                            break; // unrecognised tier value in data — skip condition
                        if (!_ownedAssets.Any(a => a.Tier == requiredTier))
                            return false;
                        break;
                        
                    case "min_capital_nok":
                        var minCapital = Convert.ToSingle(condition.Value);
                        if (_financeManager.GetAvailableCapital() < minCapital)
                            return false;
                        break;
                }
            }
            
            return true;
        }
        
        public IntegrationResult BuildAsset(IntegrationAsset asset, float playerCapitalNOK)
        {
            // Validate asset is buildable
            if (asset.IsOwned)
            {
                return new IntegrationResult
                {
                    Success = false,
                    Message = $"{asset.Name} is already owned.",
                    Asset = asset
                };
            }
            
            if (!IsUnlocked(asset))
            {
                return new IntegrationResult
                {
                    Success = false,
                    Message = $"{asset.Name} unlock conditions not met.",
                    Asset = asset
                };
            }
            
            // Check affordability
            if (asset.CapexNOK > playerCapitalNOK)
            {
                return new IntegrationResult
                {
                    Success = false,
                    Message = $"Insufficient funds. Need NOK {asset.CapexNOK:N0}, but only have NOK {playerCapitalNOK:N0}.",
                    Asset = asset,
                    ActualCapexPaid = asset.CapexNOK
                };
            }
            
            // Process construction
            _financeManager.DeductCapital(asset.CapexNOK);
            asset.IsOwned = true;
            asset.ConstructionStarted = DateTime.Now; // In real game, use game time
            asset.ConstructionCompleted = asset.ConstructionStarted.Value.AddMonths(asset.ConstructionTimeMonths);
            _ownedAssets.Add(asset);
            
            // Calculate benefits
            var result = new IntegrationResult
            {
                Success = true,
                Message = $"Construction of {asset.Name} started. Will complete in {asset.ConstructionTimeMonths} months.",
                Asset = asset,
                ActualCapexPaid = asset.CapexNOK,
                EstimatedPaybackYears = CalculatePaybackPeriod(asset),
                CompetitiveAdvantageScore = CalculateCompetitiveAdvantage(asset),
                SynergyBenefits = CalculateAssetSynergies(asset)
            };
            
            // Check for integration milestones
            float integrationScore = GetIntegrationScore();
            
            OnAssetBuilt?.Invoke(asset.Tier, asset.CapexNOK);
            OnIntegrationMilestoneReached?.Invoke(integrationScore);
            
            return result;
        }
        
        public float CalculateTotalCostSavings(List<IntegrationTier> ownedTiers, float annualProductionKg)
        {
            float totalSavings = 0f;
            
            foreach (var tier in ownedTiers)
            {
                var asset = _ownedAssets.FirstOrDefault(a => a.Tier == tier);
                if (asset != null && asset.ConstructionCompleted <= DateTime.Now)
                {
                    // Calculate direct cost savings
                    float directSavings = annualProductionKg * asset.CostSavingPctPerKg;
                    
                    // Apply synergy multipliers for adjacent tiers
                    float synergyMultiplier = 1f;
                    if (HasAdjacentTierSynergy(tier))
                        synergyMultiplier = SYNERGY_MULTIPLIER;
                    
                    totalSavings += directSavings * synergyMultiplier;
                }
            }
            
            // Apply full integration bonus
            if (ownedTiers.Count >= FULLY_INTEGRATED_THRESHOLD)
            {
                totalSavings *= (1f + FULLY_INTEGRATED_BONUS);
            }
            
            return totalSavings;
        }
        
        public float CalculateQualityPremium(List<IntegrationTier> ownedTiers)
        {
            float totalQualityBonus = 0f;
            
            foreach (var tier in ownedTiers)
            {
                var asset = _ownedAssets.FirstOrDefault(a => a.Tier == tier);
                if (asset != null && asset.ConstructionCompleted <= DateTime.Now)
                {
                    totalQualityBonus += asset.QualityBonus;
                }
            }
            
            // Diminishing returns on quality bonuses
            return (float)(1.0 - Math.Exp(-totalQualityBonus * 2.0));
        }
        
        public float GetIntegrationScore()
        {
            var completedTiers = _ownedAssets
                .Where(a => a.ConstructionCompleted <= DateTime.Now)
                .Select(a => a.Tier)
                .Distinct()
                .Count();
            
            float baseScore = completedTiers * BASE_INTEGRATION_SCORE_PER_TIER;
            
            // Bonus for full vertical coverage
            if (completedTiers >= FULLY_INTEGRATED_THRESHOLD)
            {
                baseScore += 0.25f; // 25% bonus
            }
            
            // Cap at 1.0
            return Math.Min(1.0f, baseScore);
        }
        
        private float CalculatePaybackPeriod(IntegrationAsset asset)
        {
            float annualProduction = GetPlayerAnnualProduction();
            float annualSavings = annualProduction * asset.CostSavingPctPerKg;
            float annualQualityPremium = annualProduction * asset.QualityBonus * 85f; // Assuming NOK 85/kg base price
            float totalAnnualBenefit = annualSavings + annualQualityPremium - asset.AnnualOpexNOK;
            
            if (totalAnnualBenefit <= 0)
                return float.PositiveInfinity;
            
            return asset.CapexNOK / totalAnnualBenefit;
        }
        
        private float CalculateCompetitiveAdvantage(IntegrationAsset asset)
        {
            // Competitive advantage based on lockout radius and tier importance
            float tierMultiplier = GetTierImportanceMultiplier(asset.Tier);
            float radiusScore = Math.Min(asset.CompetitorLockoutRadius / 100f, 1f); // Normalize to 0-1
            
            return tierMultiplier * radiusScore;
        }
        
        private float GetTierImportanceMultiplier(IntegrationTier tier)
        {
            return tier switch
            {
                IntegrationTier.FEED_MILL => 1.5f, // Most important - feed is 40-50% of costs
                IntegrationTier.PROCESSING => 1.3f,
                IntegrationTier.SEA_FARM => 1.2f, // Core business
                IntegrationTier.HATCHERY => 1.1f,
                IntegrationTier.COLD_CHAIN => 1.0f,
                IntegrationTier.DISTRIBUTION => 0.9f,
                IntegrationTier.GENETICS => 0.8f,
                _ => 1.0f
            };
        }
        
        private Dictionary<string, float> CalculateAssetSynergies(IntegrationAsset asset)
        {
            var synergies = new Dictionary<string, float>();
            
            // Calculate synergies with existing assets
            foreach (var ownedAsset in _ownedAssets.Where(a => a != asset))
            {
                float synergyValue = CalculateTierSynergy(asset.Tier, ownedAsset.Tier);
                if (synergyValue > 0)
                {
                    synergies[$"{ownedAsset.Tier}_synergy"] = synergyValue;
                }
            }
            
            return synergies;
        }
        
        private float CalculateTierSynergy(IntegrationTier tier1, IntegrationTier tier2)
        {
            // Adjacent tiers have higher synergy
            var adjacentPairs = new[]
            {
                (IntegrationTier.GENETICS, IntegrationTier.HATCHERY),
                (IntegrationTier.HATCHERY, IntegrationTier.FEED_MILL),
                (IntegrationTier.FEED_MILL, IntegrationTier.SEA_FARM),
                (IntegrationTier.SEA_FARM, IntegrationTier.PROCESSING),
                (IntegrationTier.PROCESSING, IntegrationTier.COLD_CHAIN),
                (IntegrationTier.COLD_CHAIN, IntegrationTier.DISTRIBUTION)
            };
            
            bool areAdjacent = adjacentPairs.Any(pair => 
                (pair.Item1 == tier1 && pair.Item2 == tier2) || 
                (pair.Item1 == tier2 && pair.Item2 == tier1));
            
            return areAdjacent ? 0.15f : 0.05f; // 15% synergy for adjacent, 5% for all others
        }
        
        private bool HasAdjacentTierSynergy(IntegrationTier tier)
        {
            var adjacentTiers = GetAdjacentTiers(tier);
            return adjacentTiers.Any(adjTier => _ownedAssets.Any(a => a.Tier == adjTier && a.ConstructionCompleted <= DateTime.Now));
        }
        
        private List<IntegrationTier> GetAdjacentTiers(IntegrationTier tier)
        {
            return tier switch
            {
                IntegrationTier.GENETICS => new List<IntegrationTier> { IntegrationTier.HATCHERY },
                IntegrationTier.HATCHERY => new List<IntegrationTier> { IntegrationTier.GENETICS, IntegrationTier.FEED_MILL },
                IntegrationTier.FEED_MILL => new List<IntegrationTier> { IntegrationTier.HATCHERY, IntegrationTier.SEA_FARM },
                IntegrationTier.SEA_FARM => new List<IntegrationTier> { IntegrationTier.FEED_MILL, IntegrationTier.PROCESSING },
                IntegrationTier.PROCESSING => new List<IntegrationTier> { IntegrationTier.SEA_FARM, IntegrationTier.COLD_CHAIN },
                IntegrationTier.COLD_CHAIN => new List<IntegrationTier> { IntegrationTier.PROCESSING, IntegrationTier.DISTRIBUTION },
                IntegrationTier.DISTRIBUTION => new List<IntegrationTier> { IntegrationTier.COLD_CHAIN },
                _ => new List<IntegrationTier>()
            };
        }
        
        private float GetPlayerAnnualProduction()
        {
            // Placeholder - in real game, this would fetch from game state
            return 10000f; // 10,000 kg/year
        }
        
        public List<IntegrationAsset> GetOwnedAssets()
        {
            return _ownedAssets.ToList();
        }
        
        public void LoadAssetsFromData(List<IntegrationAsset> assets)
        {
            _allAssets.Clear();
            _allAssets.AddRange(assets);
        }
        
        public float GetTotalIntegrationCapex()
        {
            return _ownedAssets.Sum(a => a.CapexNOK);
        }
        
        public float GetAnnualIntegrationOpex()
        {
            return _ownedAssets
                .Where(a => a.ConstructionCompleted <= DateTime.Now)
                .Sum(a => a.AnnualOpexNOK + (a.CapexNOK * a.MaintenanceCostPctPerYear));
        }
    }
}