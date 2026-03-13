using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Core;

namespace LakskongenSim.Events
{
    public enum CrisisType
    {
        DISEASE_OUTBREAK,
        BANKRUPTCY,
        REGULATORY_SHUTDOWN,
        MARKET_CRASH,
        ENVIRONMENTAL_SCANDAL
    }

    public enum AssetType
    {
        LICENSE,
        FARM_SITE,
        PROCESSING_PLANT,
        EQUIPMENT
    }

    public class CrisisEvent
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public CrisisType Type { get; set; }
        public string AffectedCompetitorId { get; set; } = string.Empty;
        public string CompetitorName { get; set; } = string.Empty;
        public float SeverityScore { get; set; } // 0-1 scale
        public float DiscountMultiplier { get; set; } // How much asset prices are reduced
        public int DurationMonths { get; set; }
        public DateTime StartDate { get; set; }
        public bool IsActive => DateTime.Now < StartDate.AddMonths(DurationMonths);

        public override string ToString()
        {
            return $"{Type} affecting {CompetitorName} - Severity: {SeverityScore:P0}, Duration: {DurationMonths} months";
        }
    }

    public class DistressedAsset
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public AssetType AssetType { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public decimal OriginalValueNOK { get; set; }
        public decimal DiscountedPriceNOK { get; set; }
        public int TimeRemainingMonths { get; set; }
        public float RiskScore { get; set; } // 0-1 scale (disease contamination, reputation damage, etc.)
        public string CrisisEventId { get; set; } = string.Empty;
        public string OwningCompetitor { get; set; } = string.Empty;
        
        public decimal PotentialSavings => OriginalValueNOK - DiscountedPriceNOK;
        public float DiscountPercentage => OriginalValueNOK == 0m ? 0f : (float)((OriginalValueNOK - DiscountedPriceNOK) / OriginalValueNOK);
        
        public override string ToString()
        {
            return $"{AssetType}: {Name} - {DiscountedPriceNOK:N0} NOK ({DiscountPercentage:P0} off), Risk: {RiskScore:P0}";
        }
    }

    public class AcquisitionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public decimal CostNOK { get; set; }
        public float IntegrationRisk { get; set; }
        public DistressedAsset? AcquiredAsset { get; set; }
        public float ReputationImpact { get; set; } // Negative for "vulture capitalism"
    }

    public delegate void CrisisTriggeredHandler(string competitorId, CrisisType type, float severity);
    public delegate void DistressedAssetAcquiredHandler(AssetType assetType, decimal price, float riskScore);

    public class CrisisCapitalismSystem
    {
        private readonly List<CrisisEvent> _activeCrises = new List<CrisisEvent>();
        private readonly List<DistressedAsset> _availableAssets = new List<DistressedAsset>();
        private readonly Random _random = new Random();

        public event CrisisTriggeredHandler? CrisisTriggered;
        public event DistressedAssetAcquiredHandler? DistressedAssetAcquired;

        private readonly Dictionary<CrisisType, float> _baseSeverityRanges = new Dictionary<CrisisType, float>
        {
            { CrisisType.DISEASE_OUTBREAK, 0.7f }, // High impact
            { CrisisType.BANKRUPTCY, 0.9f }, // Very high impact  
            { CrisisType.REGULATORY_SHUTDOWN, 0.8f }, // High impact
            { CrisisType.MARKET_CRASH, 0.6f }, // Medium-high impact
            { CrisisType.ENVIRONMENTAL_SCANDAL, 0.5f } // Medium impact
        };

        /// <summary>
        /// Get all currently active crisis events
        /// </summary>
        public List<CrisisEvent> GetActiveCrises()
        {
            return _activeCrises.Where(c => c.IsActive).ToList();
        }

        /// <summary>
        /// Get distressed assets available for purchase from a specific crisis
        /// </summary>
        public List<DistressedAsset> GetAvailableDistressedAssets(CrisisEvent crisis)
        {
            return _availableAssets.Where(a => a.CrisisEventId == crisis.Id && a.TimeRemainingMonths > 0).ToList();
        }

        /// <summary>
        /// Get all currently available distressed assets
        /// </summary>
        public List<DistressedAsset> GetAllAvailableDistressedAssets()
        {
            return _availableAssets.Where(a => a.TimeRemainingMonths > 0).ToList();
        }

        /// <summary>
        /// Attempt to purchase a distressed asset
        /// </summary>
        public AcquisitionResult PurchaseDistressedAsset(DistressedAsset asset, decimal playerCashNOK)
        {
            var result = new AcquisitionResult();

            if (!_availableAssets.Contains(asset))
            {
                result.Success = false;
                result.Message = "Asset is no longer available.";
                return result;
            }

            if (playerCashNOK < asset.DiscountedPriceNOK)
            {
                result.Success = false;
                result.Message = $"Insufficient funds. Need {asset.DiscountedPriceNOK:N0} NOK, have {playerCashNOK:N0} NOK.";
                return result;
            }

            if (asset.TimeRemainingMonths <= 0)
            {
                result.Success = false;
                result.Message = "Asset acquisition window has expired.";
                return result;
            }

            // Calculate integration risk and reputation impact
            result.IntegrationRisk = CalculateIntegrationRisk(asset);
            result.ReputationImpact = CalculateReputationImpact(asset);
            result.Success = true;
            result.Message = $"Successfully acquired {asset.Name} for {asset.DiscountedPriceNOK:N0} NOK";
            result.CostNOK = asset.DiscountedPriceNOK;
            result.AcquiredAsset = asset;

            // Remove asset from available list
            _availableAssets.Remove(asset);

            // Trigger event
            DistressedAssetAcquired?.Invoke(asset.AssetType, asset.DiscountedPriceNOK, asset.RiskScore);

            return result;
        }

        /// <summary>
        /// Calculate the integration risk for acquiring a distressed asset
        /// </summary>
        public float CalculateIntegrationRisk(DistressedAsset asset)
        {
            float baseRisk = asset.RiskScore;
            
            // Asset-specific risk factors
            float assetTypeRisk = asset.AssetType switch
            {
                AssetType.LICENSE => 0.1f, // Licenses are generally lower risk
                AssetType.FARM_SITE => 0.3f, // Farm sites may have contamination/disease issues
                AssetType.PROCESSING_PLANT => 0.2f, // Processing plants may need significant upgrades
                AssetType.EQUIPMENT => 0.15f, // Equipment may be outdated or damaged
                _ => 0.2f
            };

            // Discount level indicates distress level
            float discountRisk = asset.DiscountPercentage * 0.5f; // Higher discounts = higher risk

            // Time pressure adds risk
            float timeRisk = asset.TimeRemainingMonths <= 3 ? 0.2f : 0.0f;

            return Math.Min(1.0f, baseRisk + assetTypeRisk + discountRisk + timeRisk);
        }

        /// <summary>
        /// Calculate reputation impact of acquisition (negative for vulture capitalism)
        /// </summary>
        private float CalculateReputationImpact(DistressedAsset asset)
        {
            // Higher discounts and competitor distress = more negative reputation impact
            float baseImpact = -asset.DiscountPercentage * 0.3f; // Up to -30% reputation impact
            
            // Crisis severity adds to negative impact
            var relatedCrisis = _activeCrises.FirstOrDefault(c => c.Id == asset.CrisisEventId);
            if (relatedCrisis != null)
            {
                baseImpact -= relatedCrisis.SeverityScore * 0.2f; // Up to additional -20%
            }

            return baseImpact;
        }

        /// <summary>
        /// Trigger a crisis affecting a competitor
        /// </summary>
        public void TriggerCrisis(string competitorId, string competitorName, CrisisType type, float severity = -1)
        {
            // If severity not specified, use base range with some randomization
            if (severity < 0)
            {
                float baseSeverity = _baseSeverityRanges.GetValueOrDefault(type, 0.5f);
                severity = Math.Min(1.0f, baseSeverity + ((float)_random.NextDouble() - 0.5f) * 0.3f);
            }

            var crisis = new CrisisEvent
            {
                Type = type,
                AffectedCompetitorId = competitorId,
                CompetitorName = competitorName,
                SeverityScore = Math.Max(0.1f, Math.Min(1.0f, severity)),
                DiscountMultiplier = CalculateDiscountMultiplier(type, severity),
                DurationMonths = CalculateCrisisDuration(type, severity),
                StartDate = DateTime.Now
            };

            _activeCrises.Add(crisis);

            // Generate distressed assets based on crisis type and severity
            GenerateDistressedAssets(crisis);

            // Trigger event
            CrisisTriggered?.Invoke(competitorId, type, severity);
        }

        /// <summary>
        /// Calculate discount multiplier based on crisis type and severity
        /// </summary>
        private float CalculateDiscountMultiplier(CrisisType type, float severity)
        {
            var baseDiscounts = new Dictionary<CrisisType, (float min, float max)>
            {
                { CrisisType.DISEASE_OUTBREAK, (0.4f, 0.7f) }, // 40-70% discount
                { CrisisType.BANKRUPTCY, (0.6f, 0.8f) }, // 60-80% discount  
                { CrisisType.REGULATORY_SHUTDOWN, (0.5f, 0.75f) }, // 50-75% discount
                { CrisisType.MARKET_CRASH, (0.3f, 0.6f) }, // 30-60% discount
                { CrisisType.ENVIRONMENTAL_SCANDAL, (0.2f, 0.5f) } // 20-50% discount
            };

            var (min, max) = baseDiscounts.GetValueOrDefault(type, (0.2f, 0.5f));
            return min + (max - min) * severity;
        }

        /// <summary>
        /// Calculate crisis duration based on type and severity
        /// </summary>
        private int CalculateCrisisDuration(CrisisType type, float severity)
        {
            var baseDurations = new Dictionary<CrisisType, (int min, int max)>
            {
                { CrisisType.DISEASE_OUTBREAK, (6, 18) }, // 6-18 months
                { CrisisType.BANKRUPTCY, (3, 12) }, // 3-12 months  
                { CrisisType.REGULATORY_SHUTDOWN, (4, 24) }, // 4-24 months
                { CrisisType.MARKET_CRASH, (12, 36) }, // 12-36 months
                { CrisisType.ENVIRONMENTAL_SCANDAL, (6, 24) } // 6-24 months
            };

            var (min, max) = baseDurations.GetValueOrDefault(type, (6, 12));
            return (int)(min + (max - min) * severity);
        }

        /// <summary>
        /// Generate distressed assets based on a crisis event
        /// </summary>
        private void GenerateDistressedAssets(CrisisEvent crisis)
        {
            int assetCount = Math.Max(1, (int)(crisis.SeverityScore * 5)); // 1-5 assets based on severity

            for (int i = 0; i < assetCount; i++)
            {
                var assetType = GetRandomAssetType(crisis.Type);
                var baseValue = GenerateAssetValue(assetType);
                var discountedPrice = baseValue * (decimal)(1 - crisis.DiscountMultiplier);
                var riskScore = Math.Min(1.0f, crisis.SeverityScore + ((float)_random.NextDouble() - 0.5f) * 0.3f);

                var asset = new DistressedAsset
                {
                    AssetType = assetType,
                    Name = GenerateAssetName(assetType, crisis.CompetitorName),
                    Description = GenerateAssetDescription(assetType, crisis),
                    Location = GenerateNorwegianLocation(),
                    OriginalValueNOK = baseValue,
                    DiscountedPriceNOK = Math.Max(baseValue * 0.1m, discountedPrice), // Min 10% of original value
                    TimeRemainingMonths = _random.Next(2, crisis.DurationMonths + 3), // Available for 2 months to crisis duration + 3
                    RiskScore = Math.Max(0.0f, Math.Min(1.0f, riskScore)),
                    CrisisEventId = crisis.Id,
                    OwningCompetitor = crisis.CompetitorName
                };

                _availableAssets.Add(asset);
            }
        }

        /// <summary>
        /// Get appropriate asset types for a crisis type
        /// </summary>
        private AssetType GetRandomAssetType(CrisisType crisisType)
        {
            var assetWeights = crisisType switch
            {
                CrisisType.DISEASE_OUTBREAK => new[] 
                { 
                    (AssetType.FARM_SITE, 0.5f), 
                    (AssetType.EQUIPMENT, 0.3f), 
                    (AssetType.LICENSE, 0.2f) 
                },
                CrisisType.BANKRUPTCY => new[] 
                { 
                    (AssetType.PROCESSING_PLANT, 0.4f), 
                    (AssetType.EQUIPMENT, 0.3f), 
                    (AssetType.FARM_SITE, 0.2f), 
                    (AssetType.LICENSE, 0.1f) 
                },
                CrisisType.REGULATORY_SHUTDOWN => new[] 
                { 
                    (AssetType.LICENSE, 0.6f), 
                    (AssetType.FARM_SITE, 0.4f) 
                },
                CrisisType.MARKET_CRASH => new[] 
                { 
                    (AssetType.PROCESSING_PLANT, 0.4f), 
                    (AssetType.EQUIPMENT, 0.4f), 
                    (AssetType.FARM_SITE, 0.2f) 
                },
                _ => new[] 
                { 
                    (AssetType.FARM_SITE, 0.25f), 
                    (AssetType.PROCESSING_PLANT, 0.25f), 
                    (AssetType.EQUIPMENT, 0.25f), 
                    (AssetType.LICENSE, 0.25f) 
                }
            };

            float roll = (float)_random.NextDouble();
            float cumulative = 0;

            foreach (var (assetType, weight) in assetWeights)
            {
                cumulative += weight;
                if (roll <= cumulative) return assetType;
            }

            return AssetType.FARM_SITE; // Default fallback
        }

        /// <summary>
        /// Generate realistic asset values based on type
        /// </summary>
        private decimal GenerateAssetValue(AssetType assetType)
        {
            return assetType switch
            {
                AssetType.LICENSE => _random.Next(500_000, 2_000_000), // 0.5M - 2M NOK
                AssetType.FARM_SITE => _random.Next(5_000_000, 50_000_000), // 5M - 50M NOK
                AssetType.PROCESSING_PLANT => _random.Next(20_000_000, 200_000_000), // 20M - 200M NOK
                AssetType.EQUIPMENT => _random.Next(100_000, 10_000_000), // 100K - 10M NOK
                _ => 1_000_000 // Default 1M NOK
            };
        }

        /// <summary>
        /// Generate asset names based on type and competitor
        /// </summary>
        private string GenerateAssetName(AssetType assetType, string competitorName)
        {
            string[] locations = { "Troms", "Nordland", "Finnmark", "Sogn og Fjordane", "Møre og Romsdal", "Trøndelag" };
            string location = locations[_random.Next(locations.Length)];

            return assetType switch
            {
                AssetType.LICENSE => $"{competitorName} Aquaculture License - {location}",
                AssetType.FARM_SITE => $"{competitorName} Farm Site - {location}",
                AssetType.PROCESSING_PLANT => $"{competitorName} Processing Facility - {location}",
                AssetType.EQUIPMENT => $"{competitorName} Equipment Package - {location}",
                _ => $"{competitorName} Asset - {location}"
            };
        }

        /// <summary>
        /// Generate asset descriptions based on crisis context
        /// </summary>
        private string GenerateAssetDescription(AssetType assetType, CrisisEvent crisis)
        {
            string riskWarning = crisis.Type switch
            {
                CrisisType.DISEASE_OUTBREAK => "Warning: Potential disease contamination. Requires thorough decontamination.",
                CrisisType.BANKRUPTCY => "Liquidation sale. Asset may require significant capital investment.",
                CrisisType.REGULATORY_SHUTDOWN => "Regulatory compliance issues. May require lengthy approval processes.",
                CrisisType.MARKET_CRASH => "Market pressure sale. Good fundamentals but requires market recovery.",
                CrisisType.ENVIRONMENTAL_SCANDAL => "Environmental concerns. May face ongoing regulatory scrutiny.",
                _ => "Distressed sale opportunity."
            };

            string assetDetails = assetType switch
            {
                AssetType.LICENSE => "Includes production quotas and operational permissions.",
                AssetType.FARM_SITE => "Includes sea cages, mooring systems, and site infrastructure.",
                AssetType.PROCESSING_PLANT => "Includes processing equipment, cold storage, and distribution facilities.",
                AssetType.EQUIPMENT => "Includes feeding systems, monitoring equipment, and maintenance tools.",
                _ => "Mixed asset portfolio."
            };

            return $"{assetDetails} {riskWarning}";
        }

        /// <summary>
        /// Generate Norwegian location names
        /// </summary>
        private string GenerateNorwegianLocation()
        {
            string[] regions = { "Troms", "Nordland", "Finnmark", "Sogn og Fjordane", "Møre og Romsdal", "Trøndelag" };
            string[] waters = { "fjord", "bay", "sound", "inlet" };
            
            return $"{regions[_random.Next(regions.Length)]} {waters[_random.Next(waters.Length)]}";
        }

        /// <summary>
        /// Update system state (remove expired assets, update crisis status)
        /// </summary>
        public void Update()
        {
            // Remove expired crises
            _activeCrises.RemoveAll(c => !c.IsActive);

            // Decrement time remaining on assets and remove expired ones
            for (int i = _availableAssets.Count - 1; i >= 0; i--)
            {
                var asset = _availableAssets[i];
                asset.TimeRemainingMonths -= 1; // Assume this is called monthly
                
                if (asset.TimeRemainingMonths <= 0)
                {
                    _availableAssets.RemoveAt(i);
                }
            }
        }
    }
}