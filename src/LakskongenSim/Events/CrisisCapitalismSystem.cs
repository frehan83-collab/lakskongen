using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Finance;

namespace LakskongenSim.Events
{
    /// <summary>
    /// Types of crises that can affect competitor companies
    /// </summary>
    public enum CrisisType
    {
        /// <summary>Disease outbreak affecting fish stocks</summary>
        DISEASE_OUTBREAK,
        /// <summary>Company bankruptcy</summary>
        BANKRUPTCY,
        /// <summary>Regulatory shutdown</summary>
        REGULATORY_SHUTDOWN,
        /// <summary>Market crash</summary>
        MARKET_CRASH,
        /// <summary>Environmental scandal</summary>
        ENVIRONMENTAL_SCANDAL
    }

    /// <summary>
    /// Types of assets available for acquisition
    /// </summary>
    public enum AssetType
    {
        /// <summary>Aquaculture license</summary>
        LICENSE,
        /// <summary>Farm site</summary>
        FARM_SITE,
        /// <summary>Processing plant</summary>
        PROCESSING_PLANT,
        /// <summary>Equipment</summary>
        EQUIPMENT
    }

    public class CrisisEvent
    {
        /// <summary>Unique identifier for the crisis event</summary>
        public string Id { get; set; } = string.Empty;
        /// <summary>Type of crisis</summary>
        public CrisisType Type { get; set; }
        /// <summary>ID of the affected competitor</summary>
        public string AffectedCompetitorId { get; set; } = string.Empty;
        /// <summary>Name of the affected competitor</summary>
        public string CompetitorName { get; set; } = string.Empty;
        /// <summary>Severity score on a 0-1 scale</summary>
        public float SeverityScore { get; set; }
        /// <summary>Multiplier for asset price discounts</summary>
        public float DiscountMultiplier { get; set; }
        /// <summary>Duration of the crisis in months</summary>
        public int DurationMonths { get; set; }
        /// <summary>Start day of the crisis (simulation day)</summary>
        public int StartDay { get; set; }
        /// <summary>Whether the crisis is currently active</summary>
        public bool IsActive(int currentDay) => currentDay < StartDay + DurationMonths * 30;

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"{Type} affecting {CompetitorName} - Severity: {SeverityScore:P0}, Duration: {DurationMonths} months";
        }
    }

    /// <summary>
    /// Represents an asset available for acquisition at a distressed price
    /// </summary>
    public class DistressedAsset
    {
        /// <summary>Unique identifier for the asset</summary>
        public string Id { get; set; } = string.Empty;
        /// <summary>Type of asset</summary>
        public AssetType AssetType { get; set; }
        /// <summary>Asset name</summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>Asset description</summary>
        public string Description { get; set; } = string.Empty;
        /// <summary>Asset location</summary>
        public string Location { get; set; } = string.Empty;
        /// <summary>Original fair market value in NOK</summary>
        public decimal OriginalValueNOK { get; set; }
        /// <summary>Current discounted price in NOK</summary>
        public decimal DiscountedPriceNOK { get; set; }
        /// <summary>Months remaining before offer expires</summary>
        public int TimeRemainingMonths { get; set; }

        /// <summary>Sim day the listing closes (TimeRemainingMonths is derived from it).</summary>
        public int ExpiresOnDay { get; set; }
        /// <summary>Risk score on a 0-1 scale (disease contamination, reputation damage, etc.)</summary>
        public float RiskScore { get; set; }
        /// <summary>ID of the associated crisis event</summary>
        public string CrisisEventId { get; set; } = string.Empty;
        /// <summary>Name of the distressed competitor</summary>
        public string OwningCompetitor { get; set; } = string.Empty;
        
        /// <summary>Phase 4.2: Link to actual competitor assets for transfer on acquisition</summary>
        public string? SourceCompetitorId { get; set; }
        public string? SiteId { get; set; }
        public int? LicenseCount { get; set; }
        public float? MABTonnes { get; set; }

        /// <summary>Potential savings from the discount</summary>
        public decimal PotentialSavings => OriginalValueNOK - DiscountedPriceNOK;
        /// <summary>Discount percentage</summary>
        public float DiscountPercentage => OriginalValueNOK == 0m ? 0f : (float)((OriginalValueNOK - DiscountedPriceNOK) / OriginalValueNOK);
        
        /// <inheritdoc/>
        public override string ToString()
        {
            return $"{AssetType}: {Name} - {DiscountedPriceNOK:N0} NOK ({DiscountPercentage:P0} off), Risk: {RiskScore:P0}";
        }
    }

    /// <summary>
    /// Result of attempting to acquire a distressed asset
    /// </summary>
    public class AcquisitionResult
    {
        /// <summary>Whether the acquisition was successful</summary>
        public bool Success { get; set; }
        /// <summary>Result message</summary>
        public string Message { get; set; } = string.Empty;
        /// <summary>Total acquisition cost in NOK</summary>
        public decimal CostNOK { get; set; }
        /// <summary>Risk level of integrating the acquired asset</summary>
        public float IntegrationRisk { get; set; }
        /// <summary>The acquired asset, if successful</summary>
        public DistressedAsset? AcquiredAsset { get; set; }
        /// <summary>Impact on reputation (negative for "vulture capitalism")</summary>
        public float ReputationImpact { get; set; }
    }

    /// <summary>Handler for crisis triggered events</summary>
    /// <param name="competitorId">ID of affected competitor</param>
    /// <param name="type">Type of crisis</param>
    /// <param name="severity">Severity score</param>
    public delegate void CrisisTriggeredHandler(string competitorId, CrisisType type, float severity);
    
    /// <summary>Handler for distressed asset acquisitions</summary>
    /// <param name="assetType">Type of asset acquired</param>
    /// <param name="price">Acquisition price</param>
    /// <param name="riskScore">Risk score of the asset</param>
    public delegate void DistressedAssetAcquiredHandler(AssetType assetType, decimal price, float riskScore);

    /// <summary>
    /// System for simulating competitor crises and opportunities for distressed asset acquisition
    /// </summary>
    public class CrisisCapitalismSystem
    {
        private readonly List<CrisisEvent> _activeCrises = new List<CrisisEvent>();
        private readonly List<DistressedAsset> _availableAssets = new List<DistressedAsset>();
        private readonly SimRngStream _random;
        internal SimRngStream RngForSnapshot => _random;

        public CrisisCapitalismSystem(SimRngStream rng)
        {
            _random = rng ?? throw new ArgumentNullException(nameof(rng));
        }

        public CrisisCapitalismSystem(int? seed = null)
            : this((seed.HasValue ? new SimRng(seed.Value) : SimRng.CreateUnseeded()).Stream(SimRngStreams.CrisisCapitalism))
        {
        }

        /// <summary>Event raised when a crisis is triggered for a competitor</summary>
        public event CrisisTriggeredHandler? CrisisTriggered;
        /// <summary>Event raised when a distressed asset is acquired</summary>
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
        public List<CrisisEvent> GetActiveCrises(int currentDay)
        {
            return _activeCrises.Where(c => c.IsActive(currentDay)).ToList();
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
        /// Trigger a crisis affecting a competitor
        /// </summary>
        public void TriggerCrisis(string competitorId, string competitorName, CrisisType type, int currentDay, IReadOnlyList<CompetitorCompany> competitors, float severity = -1)
        {
            // If severity not specified, use base range with some randomization
            if (severity < 0)
            {
                float baseSeverity = _baseSeverityRanges.GetValueOrDefault(type, 0.5f);
                severity = Math.Min(1.0f, baseSeverity + ((float)_random.NextDouble() - 0.5f) * 0.3f);
            }

            // Deterministic ID: crisis_{competitorId}_{type}_{day}
            var crisis = new CrisisEvent
            {
                Id = $"crisis_{competitorId}_{type}_{currentDay}",
                Type = type,
                AffectedCompetitorId = competitorId,
                CompetitorName = competitorName,
                SeverityScore = Math.Max(0.1f, Math.Min(1.0f, severity)),
                DiscountMultiplier = CalculateDiscountMultiplier(type, severity),
                DurationMonths = CalculateCrisisDuration(type, severity),
                StartDay = currentDay
            };

            _activeCrises.Add(crisis);

            // Generate distressed assets based on crisis type and severity, linked to actual competitor
            GenerateDistressedAssets(crisis, competitors);

            // Trigger event
            CrisisTriggered?.Invoke(competitorId, type, severity);
        }

        /// <summary>
        /// Restore a saved crisis without firing events or generating new
        /// assets (load path only — TriggerCrisis is the live path).
        /// Expired crises (IsActive == false) are skipped.
        /// </summary>
        public void RestoreCrisis(CrisisEvent crisis, int currentDay)
        {
            if (crisis == null || !crisis.IsActive(currentDay)) return;
            if (_activeCrises.Any(c => c.Id == crisis.Id)) return;
            _activeCrises.Add(crisis);
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
        /// Generate distressed assets based on a crisis event, linked to actual competitor assets
        /// </summary>
        public void GenerateDistressedAssets(CrisisEvent crisis, IReadOnlyList<CompetitorCompany> competitors)
        {
            var targetCompetitor = competitors.FirstOrDefault(c => c.Id == crisis.AffectedCompetitorId);
            if (targetCompetitor == null) return;

            int assetCount = Math.Max(1, (int)(crisis.SeverityScore * 5)); // 1-5 assets based on severity

            for (int i = 0; i < assetCount; i++)
            {
                var assetType = GetRandomAssetType(crisis.Type);
                
                // Generate asset linked to actual competitor state
                var (baseValue, siteId, licenseCount, mabTonnes) = GenerateAssetFromCompetitor(assetType, targetCompetitor);
                var discountedPrice = baseValue * (decimal)(1 - crisis.DiscountMultiplier);
                var riskScore = Math.Min(1.0f, crisis.SeverityScore + ((float)_random.NextDouble() - 0.5f) * 0.3f);
                int listedMonths = _random.Next(2, crisis.DurationMonths + 3);

                // Deterministic ID: asset_{crisisId}_{type}_{index}
                var asset = new DistressedAsset
                {
                    Id = $"asset_{crisis.Id}_{assetType}_{i}",
                    AssetType = assetType,
                    Name = GenerateAssetName(assetType, crisis.CompetitorName),
                    Description = GenerateAssetDescription(assetType, crisis),
                    Location = GenerateNorwegianLocation(),
                    OriginalValueNOK = baseValue,
                    DiscountedPriceNOK = Math.Max(baseValue * 0.1m, discountedPrice), // Min 10% of original value
                    TimeRemainingMonths = listedMonths,
                    ExpiresOnDay = crisis.StartDay + listedMonths * DaysPerMonth,
                    RiskScore = Math.Max(0.0f, Math.Min(1.0f, riskScore)),
                    CrisisEventId = crisis.Id,
                    OwningCompetitor = crisis.CompetitorName,
                    SourceCompetitorId = targetCompetitor.Id,
                    SiteId = siteId,
                    LicenseCount = licenseCount,
                    MABTonnes = mabTonnes
                };

                _availableAssets.Add(asset);
            }
        }

        /// <summary>
        /// Generate asset value and link from actual competitor state
        /// </summary>
        private (decimal value, string? siteId, int? licenseCount, float? mabTonnes) GenerateAssetFromCompetitor(AssetType assetType, CompetitorCompany competitor)
        {
            return assetType switch
            {
                AssetType.LICENSE => (
                    value: competitor.LicenseCount > 0 
                        ? (decimal)(150_000_000m * Math.Max(1, competitor.LicenseCount / 10)) // ~150M per license batch
                        : _random.Next(500_000, 2_000_000),
                    siteId: null,
                    licenseCount: Math.Max(1, competitor.LicenseCount / 10),
                    mabTonnes: 780f * Math.Max(1, competitor.LicenseCount / 10)
                ),
                AssetType.FARM_SITE => (
                    value: competitor.SiteCount > 0
                        ? (decimal)(competitor.SiteCount * 50_000_000m) // ~50M per site
                        : _random.Next(5_000_000, 50_000_000),
                    siteId: competitor.SiteCount > 0 ? $"site_{competitor.Id}_{_random.Next(1000)}" : null,
                    licenseCount: null,
                    mabTonnes: competitor.SiteCount > 0 ? 780f : null
                ),
                AssetType.PROCESSING_PLANT => (
                    value: _random.Next(20_000_000, 200_000_000),
                    siteId: null,
                    licenseCount: null,
                    mabTonnes: null
                ),
                AssetType.EQUIPMENT => (
                    value: _random.Next(100_000, 10_000_000),
                    siteId: null,
                    licenseCount: null,
                    mabTonnes: null
                ),
                _ => (1_000_000m, null, null, null)
            };
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
        /// Update system state (remove expired assets, update crisis status)
        /// </summary>
        public void Update(int currentDay)
        {
            // Remove expired crises
            _activeCrises.RemoveAll(c => !c.IsActive(currentDay));

            // Listings run on the calendar: the engine calls this weekly, and the
            // old "one month per call" countdown expired them four times too fast.
            for (int i = _availableAssets.Count - 1; i >= 0; i--)
            {
                var asset = _availableAssets[i];
                int daysLeft = asset.ExpiresOnDay - currentDay;
                asset.TimeRemainingMonths = Math.Max(0, (daysLeft + DaysPerMonth - 1) / DaysPerMonth);
                if (daysLeft <= 0)
                    _availableAssets.RemoveAt(i);
            }
        }

        /// <summary>Days in a listing month.</summary>
        public const int DaysPerMonth = 30;

        /// <summary>
        /// Restore a saved listing without firing events (load path). Expired or
        /// duplicate listings are skipped.
        /// </summary>
        public void RestoreAsset(DistressedAsset asset, int currentDay)
        {
            if (asset == null || asset.ExpiresOnDay <= currentDay) return;
            if (_availableAssets.Any(a => a.Id == asset.Id)) return;
            _availableAssets.Add(asset);
        }
    }
}