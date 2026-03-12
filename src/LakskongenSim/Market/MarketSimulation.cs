using System;
using System.Collections.Generic;
using LakskongenSim.Sustainability;

namespace LakskongenSim.Market
{
    /// <summary>
    /// Simulates market price dynamics for products.
    /// Integrates with sustainability certification for premium market access and pricing.
    /// </summary>
    public class MarketSimulation
    {
        public decimal BasePricePerKg { get; private set; } = 60m; // NOK/kg base price
        public decimal PricePerKg { get; private set; } = 60m; // Actual price (with multipliers)
        
        // Market access based on certification
        private HashSet<string> _accessibleMarkets = new() { "Domestic", "Russia" };
        private decimal _certificationMultiplier = 1.0m;

        public event Action<decimal>? OnPriceChanged;
        public event Action<HashSet<string>>? OnMarketAccessChanged;

        public void AdvanceDays(int days)
        {
            // very simple mean-reverting noise
            var rand = new Random();
            var shock = (decimal)(rand.NextDouble() - 0.5) * 2m;
            BasePricePerKg = Math.Max(20m, BasePricePerKg + shock);
            
            // Apply certification multiplier
            PricePerKg = BasePricePerKg * _certificationMultiplier;
            OnPriceChanged?.Invoke(PricePerKg);
        }

        /// <summary>
        /// Update market access and pricing based on sustainability certification level.
        /// Called by SimulationEngine when certification changes.
        /// </summary>
        public void UpdateCertificationLevel(CertificationLevel level, decimal priceMultiplier)
        {
            _certificationMultiplier = priceMultiplier;
            
            // Update accessible markets based on certification
            _accessibleMarkets = GetAccessibleMarkets(level);
            
            // Recalculate price with new multiplier
            PricePerKg = BasePricePerKg * _certificationMultiplier;
            
            OnPriceChanged?.Invoke(PricePerKg);
            OnMarketAccessChanged?.Invoke(_accessibleMarkets);
            
            Console.WriteLine($"💰 Market Update: Price multiplier {priceMultiplier:P0}, Access: {string.Join(", ", _accessibleMarkets)}");
        }

        /// <summary>
        /// Get accessible markets based on certification level.
        /// Higher certification unlocks premium markets.
        /// </summary>
        private HashSet<string> GetAccessibleMarkets(CertificationLevel level)
        {
            var markets = new HashSet<string>();
            
            switch (level)
            {
                case CertificationLevel.NonCompliant:
                    // Can only sell to: Domestic, Russia (limited markets)
                    markets.Add("Domestic");
                    markets.Add("Russia");
                    break;

                case CertificationLevel.BasicCompliance:
                    // Add: EU, USA, China (standard markets)
                    markets.Add("Domestic");
                    markets.Add("EU");
                    markets.Add("USA");
                    markets.Add("Russia");
                    markets.Add("China");
                    break;

                case CertificationLevel.ASCCertified:
                    // Add: Whole Foods, Waitrose (premium retail)
                    markets.Add("Domestic");
                    markets.Add("EU");
                    markets.Add("USA");
                    markets.Add("Russia");
                    markets.Add("China");
                    markets.Add("WholeFoods");
                    markets.Add("Waitrose");
                    break;

                case CertificationLevel.OrganicCertified:
                case CertificationLevel.SustainabilityLeader:
                    // Add: Japan Premium, Michelin restaurants (ultra-premium)
                    markets.Add("Domestic");
                    markets.Add("EU");
                    markets.Add("USA");
                    markets.Add("Russia");
                    markets.Add("China");
                    markets.Add("WholeFoods");
                    markets.Add("Waitrose");
                    markets.Add("JapanPremium");
                    markets.Add("MichelinRestaurants");
                    break;
            }
            
            return markets;
        }

        /// <summary>
        /// Check if a specific market is accessible with current certification.
        /// </summary>
        public bool CanAccessMarket(string marketName)
        {
            return _accessibleMarkets.Contains(marketName);
        }

        /// <summary>
        /// Get all currently accessible markets.
        /// </summary>
        public HashSet<string> GetAccessibleMarkets()
        {
            return new HashSet<string>(_accessibleMarkets);
        }
    }
}