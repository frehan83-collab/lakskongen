using System;
using System.Collections.Generic;
using System.Linq;

namespace LakskongenSim.Markets
{
    public enum MarketRegion
    {
        JAPAN,
        USA,
        EU,
        UK,
        CHINA,
        MIDDLE_EAST
    }

    public enum MarketOpportunityType
    {
        TRADE_AGREEMENT,
        REGULATORY_CHANGE,
        COMPETITOR_EXIT,
        CONSUMER_TREND_SHIFT,
        CURRENCY_ADVANTAGE
    }

    public class InternationalMarket
    {
        public MarketRegion Region { get; set; }
        public string Name { get; set; }
        public float BasePriceNOK { get; set; } = 71.44f; // Q1 2026 baseline
        public float PremiumMultiplier { get; set; } // 1.15 = 15% premium
        public List<string> RequiredCertifications { get; set; } = new List<string>();
        public float MinReputationScore { get; set; }
        public float LogisticsCostPctOfPrice { get; set; }
        public float TariffRate { get; set; }
        public float MarketSharePlayer { get; set; } = 0.0f;
        public float MarketShareCompetitors { get; set; } = 0.85f;
        public bool PlayerHasAccess { get; set; } = false;
        public float EntryInvestmentNOK { get; set; }
        public int RegulatoryApprovalMonths { get; set; }

        public float GetEffectivePrice(float basePrice, float qualityScore)
        {
            float price = basePrice * PremiumMultiplier;
            
            // Quality adjustment (-10% to +10%)
            float qualityAdjustment = (qualityScore - 0.5f) * 0.2f;
            price *= (1.0f + qualityAdjustment);
            
            // Subtract logistics and tariffs
            price -= (price * LogisticsCostPctOfPrice);
            price -= (price * TariffRate);
            
            return price;
        }
    }

    public class MarketEntryResult
    {
        public bool Success { get; set; }
        public float RequiredInvestmentNOK { get; set; }
        public int MonthsToEntry { get; set; }
        public bool RegulatoryApprovalNeeded { get; set; }
        public string FailureReason { get; set; } = "";
        public List<string> MissingCertifications { get; set; } = new List<string>();
    }

    public class SalesResult
    {
        public bool Success { get; set; }
        public float VolumeKgSold { get; set; }
        public float TotalRevenueNOK { get; set; }
        public float EffectivePricePerKg { get; set; }
        public float MarketShareGained { get; set; }
        public string Message { get; set; } = "";
    }

    public class MarketOpportunity
    {
        public MarketRegion Region { get; set; }
        public MarketOpportunityType OpportunityType { get; set; }
        public string Description { get; set; }
        public float PotentialPremiumIncrease { get; set; }
        public int DurationMonths { get; set; }
        public float ProbabilityOfSuccess { get; set; }
    }

    public class InternationalMarketSystem
    {
        private Dictionary<MarketRegion, InternationalMarket> markets = new Dictionary<MarketRegion, InternationalMarket>();
        private List<MarketOpportunity> activeOpportunities = new List<MarketOpportunity>();

        // Events
        public event Action<MarketRegion, float> MarketEntered;
        public event Action<MarketRegion, float, float> SaleCompleted; // region, volume, revenue
        public event Action<MarketOpportunity> OpportunityAvailable;

        public InternationalMarketSystem()
        {
            InitializeMarkets();
        }

        private void InitializeMarkets()
        {
            // Japan - Premium quality market
            markets[MarketRegion.JAPAN] = new InternationalMarket
            {
                Region = MarketRegion.JAPAN,
                Name = "Japan (Premium)",
                BasePriceNOK = 71.44f,
                PremiumMultiplier = 1.29f, // 29% premium
                RequiredCertifications = new List<string> { "ASC", "Quality_4_5kg" },
                MinReputationScore = 0.85f,
                LogisticsCostPctOfPrice = 0.12f,
                TariffRate = 0.05f,
                EntryInvestmentNOK = 8_500_000f,
                RegulatoryApprovalMonths = 6
            };

            // USA - Organic/sustainability market
            markets[MarketRegion.USA] = new InternationalMarket
            {
                Region = MarketRegion.USA,
                Name = "USA (Organic Premium)",
                BasePriceNOK = 71.44f,
                PremiumMultiplier = 1.24f, // 24% premium
                RequiredCertifications = new List<string> { "Organic", "ASC", "FDA_Compliance" },
                MinReputationScore = 0.88f,
                LogisticsCostPctOfPrice = 0.15f,
                TariffRate = 0.08f,
                EntryInvestmentNOK = 12_000_000f,
                RegulatoryApprovalMonths = 9
            };

            // EU - Varied market
            markets[MarketRegion.EU] = new InternationalMarket
            {
                Region = MarketRegion.EU,
                Name = "EU (Mixed Markets)",
                BasePriceNOK = 71.44f,
                PremiumMultiplier = 1.12f, // 12% premium
                RequiredCertifications = new List<string> { "EU_Compliance" },
                MinReputationScore = 0.65f,
                LogisticsCostPctOfPrice = 0.08f,
                TariffRate = 0.0f, // EEA member
                EntryInvestmentNOK = 4_500_000f,
                RegulatoryApprovalMonths = 3
            };

            // UK - Post-Brexit opportunities
            markets[MarketRegion.UK] = new InternationalMarket
            {
                Region = MarketRegion.UK,
                Name = "United Kingdom",
                BasePriceNOK = 71.44f,
                PremiumMultiplier = 1.08f, // 8% premium
                RequiredCertifications = new List<string> { "UK_Compliance" },
                MinReputationScore = 0.68f,
                LogisticsCostPctOfPrice = 0.09f,
                TariffRate = 0.02f, // Post-Brexit tariff
                EntryInvestmentNOK = 5_200_000f,
                RegulatoryApprovalMonths = 4
            };

            // China - Growing but volatile
            markets[MarketRegion.CHINA] = new InternationalMarket
            {
                Region = MarketRegion.CHINA,
                Name = "China (Emerging)",
                BasePriceNOK = 71.44f,
                PremiumMultiplier = 1.13f, // 13% premium (volatile)
                RequiredCertifications = new List<string> { "China_Import_License" },
                MinReputationScore = 0.60f,
                LogisticsCostPctOfPrice = 0.18f, // High logistics cost
                TariffRate = 0.12f,
                EntryInvestmentNOK = 9_500_000f,
                RegulatoryApprovalMonths = 12
            };

            // Middle East - Halal certified
            markets[MarketRegion.MIDDLE_EAST] = new InternationalMarket
            {
                Region = MarketRegion.MIDDLE_EAST,
                Name = "Middle East (Halal)",
                BasePriceNOK = 71.44f,
                PremiumMultiplier = 1.17f, // 17% premium
                RequiredCertifications = new List<string> { "Halal" },
                MinReputationScore = 0.70f,
                LogisticsCostPctOfPrice = 0.14f,
                TariffRate = 0.06f,
                EntryInvestmentNOK = 6_800_000f,
                RegulatoryApprovalMonths = 5
            };
        }

        public List<InternationalMarket> GetAccessibleMarkets(float playerReputation, List<string> playerCertifications)
        {
            var accessible = new List<InternationalMarket>();

            foreach (var market in markets.Values)
            {
                if (playerReputation >= market.MinReputationScore)
                {
                    // Check certifications
                    bool hasCerts = market.RequiredCertifications.All(cert => playerCertifications.Contains(cert));
                    if (hasCerts || market.RequiredCertifications.Count == 0)
                    {
                        accessible.Add(market);
                    }
                }
            }

            return accessible;
        }

        public MarketEntryResult EnterMarket(MarketRegion region, float investmentNOK, float playerReputation, List<string> playerCertifications)
        {
            if (!markets.ContainsKey(region))
            {
                return new MarketEntryResult
                {
                    Success = false,
                    FailureReason = "Invalid market region"
                };
            }

            var market = markets[region];

            // Check if already entered
            if (market.PlayerHasAccess)
            {
                return new MarketEntryResult
                {
                    Success = false,
                    FailureReason = "Market already entered"
                };
            }

            // Check reputation
            if (playerReputation < market.MinReputationScore)
            {
                return new MarketEntryResult
                {
                    Success = false,
                    FailureReason = $"Reputation too low. Required: {market.MinReputationScore:F2}",
                    RequiredInvestmentNOK = market.EntryInvestmentNOK,
                    MonthsToEntry = market.RegulatoryApprovalMonths
                };
            }

            // Check certifications
            var missingCerts = market.RequiredCertifications.Where(cert => !playerCertifications.Contains(cert)).ToList();
            if (missingCerts.Any())
            {
                return new MarketEntryResult
                {
                    Success = false,
                    FailureReason = "Missing required certifications",
                    MissingCertifications = missingCerts,
                    RequiredInvestmentNOK = market.EntryInvestmentNOK,
                    MonthsToEntry = market.RegulatoryApprovalMonths
                };
            }

            // Check investment
            if (investmentNOK < market.EntryInvestmentNOK)
            {
                return new MarketEntryResult
                {
                    Success = false,
                    FailureReason = $"Insufficient investment. Required: {market.EntryInvestmentNOK:N0} NOK",
                    RequiredInvestmentNOK = market.EntryInvestmentNOK,
                    MonthsToEntry = market.RegulatoryApprovalMonths
                };
            }

            // Success - enter market
            market.PlayerHasAccess = true;
            market.MarketSharePlayer = 0.01f; // Start with 1% market share

            MarketEntered?.Invoke(region, investmentNOK);

            return new MarketEntryResult
            {
                Success = true,
                RequiredInvestmentNOK = investmentNOK,
                MonthsToEntry = market.RegulatoryApprovalMonths,
                RegulatoryApprovalNeeded = true
            };
        }

        public SalesResult SellToMarket(MarketRegion region, float volumeKg, float qualityScore, float basePrice = 71.44f)
        {
            if (!markets.ContainsKey(region))
            {
                return new SalesResult
                {
                    Success = false,
                    Message = "Invalid market region"
                };
            }

            var market = markets[region];

            if (!market.PlayerHasAccess)
            {
                return new SalesResult
                {
                    Success = false,
                    Message = "Market not yet entered"
                };
            }

            // Calculate effective price
            float effectivePrice = market.GetEffectivePrice(basePrice, qualityScore);

            // Calculate revenue
            float totalRevenue = volumeKg * effectivePrice;

            // Update market share (simplified)
            float marketShareGain = Math.Min(0.05f, volumeKg / 10_000_000f); // Cap at 5% per sale
            market.MarketSharePlayer += marketShareGain;
            market.MarketSharePlayer = Math.Min(0.30f, market.MarketSharePlayer); // Cap at 30%

            SaleCompleted?.Invoke(region, volumeKg, totalRevenue);

            return new SalesResult
            {
                Success = true,
                VolumeKgSold = volumeKg,
                TotalRevenueNOK = totalRevenue,
                EffectivePricePerKg = effectivePrice,
                MarketShareGained = marketShareGain,
                Message = $"Sold {volumeKg:N0} kg to {market.Name} at {effectivePrice:F2} NOK/kg"
            };
        }

        public float CalculateNetPrice(MarketRegion region, float basePrice, float qualityScore)
        {
            if (!markets.ContainsKey(region))
                return basePrice;

            return markets[region].GetEffectivePrice(basePrice, qualityScore);
        }

        public void UpdateMarketShare(MarketRegion region, float playerSales, float competitorSales)
        {
            if (!markets.ContainsKey(region))
                return;

            var market = markets[region];
            float totalSales = playerSales + competitorSales;

            if (totalSales > 0)
            {
                market.MarketSharePlayer = playerSales / totalSales;
                market.MarketShareCompetitors = competitorSales / totalSales;
            }
        }

        public List<MarketOpportunity> GetEmergingOpportunities()
        {
            return activeOpportunities;
        }

        public void AddOpportunity(MarketOpportunity opportunity)
        {
            activeOpportunities.Add(opportunity);
            OpportunityAvailable?.Invoke(opportunity);
        }

        public Dictionary<MarketRegion, InternationalMarket> GetAllMarkets()
        {
            return new Dictionary<MarketRegion, InternationalMarket>(markets);
        }

        public InternationalMarket GetMarket(MarketRegion region)
        {
            return markets.ContainsKey(region) ? markets[region] : null;
        }
    }
}
