using System;
using System.Collections.Generic;
using System.Linq;

namespace LakskongenSim.Expansion
{
    public class AcquisitionTarget
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public float AssetValue { get; set; } // NOK millions
        public float LicenseValue { get; set; } // NOK millions (MAB value)
        public float HealthScore { get; set; } // 0-1 (1 = excellent health)
        public bool IsDistressed { get; set; } // Post-disease, bankruptcy, etc.
        public float AskingPrice { get; set; } // NOK millions
        public float RegionalMarketShare { get; set; } // 0-1 (percentage as decimal)
        public string Region { get; set; } = string.Empty;
        public string AssetType { get; set; } = string.Empty; // "Fish Farm", "Processing Plant", "Hatchery"
        public float TonnageCapacity { get; set; } // For MAB licenses
    }

    public class AcquisitionResult
    {
        public bool Success { get; set; }
        public float ActualPrice { get; set; } // NOK millions
        public int RegulatoryDelayMonths { get; set; }
        public float SynergiesNOKPerYear { get; set; } // NOK millions per year
        public string FailureReason { get; set; } = string.Empty; // If Success = false
        public bool RequiresAntitrustApproval { get; set; }
    }

    public class AcquisitionSystem
    {
        private List<AcquisitionTarget> availableTargets = null!;
        private Random rng;
        
        // Economic constants based on Norwegian salmon industry
        private const float MAB_VALUE_PER_TONNE = 170300f; // NOK per tonne MAB
        private const float STANDARD_MAB_SIZE = 780f; // Standard MAB license tonnage
        private const float ANTITRUST_THRESHOLD = 0.30f; // 30% market share triggers review
        
        public AcquisitionSystem(int? seed = null)
        {
            rng = seed.HasValue ? new Random(seed.Value) : new Random();
            LoadAvailableTargets();
        }

        public List<AcquisitionTarget> GetAvailableTargets()
        {
            return availableTargets.Where(t => t != null).ToList();
        }

        public float CalculateAcquisitionPrice(AcquisitionTarget target, float playerMarketShare)
        {
            float baseValue = target.AssetValue + target.LicenseValue;
            float premium = 1.0f;
            
            // Distressed companies get 40-70% discount
            if (target.IsDistressed)
            {
                float discountPercent = 0.4f + (rng.NextSingle() * 0.3f); // 40-70%
                premium = 1.0f - discountPercent;
            }
            else
            {
                // Healthy companies command 10-50% premium
                float premiumPercent = 0.1f + (rng.NextSingle() * 0.4f); // 10-50%
                premium = 1.0f + premiumPercent;
            }
            
            // Health score affects pricing
            float healthMultiplier = 0.7f + (target.HealthScore * 0.6f); // 0.7x to 1.3x based on health
            
            // Market concentration affects pricing (competitive bidding)
            float competitionMultiplier = 1.0f;
            if (playerMarketShare + target.RegionalMarketShare > 0.2f)
            {
                competitionMultiplier = 1.1f + (playerMarketShare * 0.5f); // More expensive for dominant players
            }
            
            return baseValue * premium * healthMultiplier * competitionMultiplier;
        }

        public bool RequiresAntitrustReview(float newCombinedMarketShare)
        {
            return newCombinedMarketShare > ANTITRUST_THRESHOLD;
        }

        public AcquisitionResult MakeOffer(AcquisitionTarget target, float offeredPrice, float playerCashNOK)
        {
            var result = new AcquisitionResult();
            
            // Check if player has enough cash
            if (offeredPrice > playerCashNOK)
            {
                result.Success = false;
                result.FailureReason = "Insufficient funds";
                return result;
            }
            
            float fairValue = target.AskingPrice;
            float acceptanceThreshold = fairValue * 0.85f; // Accept offers above 85% of asking price
            
            // Distressed companies are more likely to accept lower offers
            if (target.IsDistressed)
            {
                acceptanceThreshold = fairValue * 0.70f;
            }
            
            if (offeredPrice < acceptanceThreshold)
            {
                result.Success = false;
                result.FailureReason = $"Offer too low. Minimum acceptable: {acceptanceThreshold:F1}M NOK";
                return result;
            }
            
            // Successful acquisition
            result.Success = true;
            result.ActualPrice = offeredPrice;
            
            // Calculate regulatory delay for antitrust review
            bool requiresReview = RequiresAntitrustReview(target.RegionalMarketShare); // Would need player's market share
            result.RequiresAntitrustApproval = requiresReview;
            
            if (requiresReview)
            {
                result.RegulatoryDelayMonths = 6 + rng.Next(0, 13); // 6-18 months for antitrust review
            }
            else
            {
                result.RegulatoryDelayMonths = rng.Next(1, 4); // 1-3 months standard approval
            }
            
            return result;
        }

        public float CalculateIntegrationSynergies(AcquisitionTarget target, float playerIntegrationLevel)
        {
            float baseRevenue = (target.AssetValue + target.LicenseValue) * 0.15f; // Assume 15% revenue ratio
            float synergyPercent = 0.05f + (playerIntegrationLevel * 0.15f); // 5-20% synergies based on integration capability
            
            // Processing plants have higher synergies due to vertical integration
            if (target.AssetType == "Processing Plant")
            {
                synergyPercent *= 1.5f;
            }
            
            // Healthy companies provide better synergies
            synergyPercent *= (0.5f + target.HealthScore * 0.5f);
            
            return baseRevenue * synergyPercent;
        }

        private void LoadAvailableTargets()
        {
            // This would normally load from JSON file or database
            // For now, creating some sample targets with realistic Norwegian values
            availableTargets = new List<AcquisitionTarget>
            {
                new AcquisitionTarget
                {
                    Id = "nordfjord_salmon",
                    Name = "Nordfjord Salmon AS",
                    AssetValue = 85f,
                    LicenseValue = 132.8f, // ~780T MAB
                    HealthScore = 0.85f,
                    IsDistressed = false,
                    AskingPrice = 250f,
                    RegionalMarketShare = 0.08f,
                    Region = "Nordfjord",
                    AssetType = "Fish Farm",
                    TonnageCapacity = 780f
                },
                new AcquisitionTarget
                {
                    Id = "arctic_processing",
                    Name = "Arctic Fish Processing",
                    AssetValue = 180f,
                    LicenseValue = 0f, // Processing plant, no MAB
                    HealthScore = 0.92f,
                    IsDistressed = false,
                    AskingPrice = 220f,
                    RegionalMarketShare = 0.12f,
                    Region = "Troms",
                    AssetType = "Processing Plant",
                    TonnageCapacity = 0f
                }
            };
        }

        // Event for notifying when acquisition completes
        public event Action<string, float, float>? AcquisitionCompleted;

        protected virtual void OnAcquisitionCompleted(string targetId, float price, float synergies)
        {
            AcquisitionCompleted?.Invoke(targetId, price, synergies);
        }

        public void CompleteAcquisition(AcquisitionTarget target, float actualPrice, float synergies)
        {
            // Remove from available targets
            availableTargets.RemoveAll(t => t.Id == target.Id);
            
            // Fire completion signal
            OnAcquisitionCompleted(target.Id, actualPrice, synergies);
        }
    }
}