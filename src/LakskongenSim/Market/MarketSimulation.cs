using System;
using System.Collections.Generic;
using LakskongenSim.Sustainability;

namespace LakskongenSim.Market
{
    /// <summary>
    /// Realistic salmon market price simulation based on Norwegian industry dynamics.
    /// Models: seasonal patterns, mean-reversion, supply/demand shocks, Nasdaq Salmon Index correlation.
    /// Base price ~70 NOK/kg (2024-2026 range: 50-110 NOK/kg).
    /// </summary>
    public class MarketSimulation
    {
        // ─── Price state ───
        public decimal BasePricePerKg { get; private set; } = 70m;  // Current spot price
        public decimal PricePerKg { get; private set; } = 70m;      // With certification multiplier
        public decimal WeeklyAvgPrice { get; private set; } = 70m;  // 7-day rolling average

        // ─── Seasonal parameters (Norwegian salmon market) ───
        // Q1: Low (post-Christmas), Q2-Q3: Rising (summer demand), Q4: Peak (Christmas)
        private static readonly double[] MonthlySeasonalIndex = {
            0.92, 0.90, 0.95,  // Jan, Feb, Mar — low season
            1.00, 1.05, 1.08,  // Apr, May, Jun — rising
            1.10, 1.12, 1.05,  // Jul, Aug, Sep — summer peak
            1.00, 1.08, 1.15   // Oct, Nov, Dec — Christmas peak
        };

        // ─── Mean-reversion parameters ───
        private const decimal LongTermMean = 72m;    // Long-term equilibrium (NOK/kg)
        private const double MeanReversionSpeed = 0.02; // 2% daily pull toward mean
        private const double DailyVolatility = 0.015;   // 1.5% daily std dev

        // ─── Supply/demand ───
        public double GlobalSupplyIndex { get; private set; } = 1.0; // 1.0 = normal
        public double GlobalDemandIndex { get; private set; } = 1.0;

        // ─── Market access ───
        private HashSet<string> _accessibleMarkets = new() { "Domestic", "EU", "USA" };
        private decimal _certificationMultiplier = 1.0m;

        // ─── Price history (for charts/analytics) ───
        private readonly Queue<decimal> _priceHistory = new();
        private const int MaxHistoryLength = 365; // Keep 1 year

        // ─── Random ───
        private readonly Random _rng = new();
        private int _simulationDay = 0;

        // ─── Events ───
        public event Action<decimal>? OnPriceChanged;
        public event Action<HashSet<string>>? OnMarketAccessChanged;
        public event Action<string>? OnMarketEvent; // News headlines

        /// <summary>
        /// Advance market simulation by given days.
        /// Uses Ornstein-Uhlenbeck mean-reverting process with seasonal overlay.
        /// </summary>
        public void AdvanceDays(int days)
        {
            for (int d = 0; d < days; d++)
            {
                _simulationDay++;

                // Seasonal index (approximate month from simulation day)
                int month = (_simulationDay / 30) % 12;
                double seasonal = MonthlySeasonalIndex[month];

                // Mean-reverting price dynamics (Ornstein-Uhlenbeck)
                // dP = θ(μ - P)dt + σ·P·dW
                double theta = MeanReversionSpeed;
                double mu = (double)LongTermMean * seasonal;
                double sigma = DailyVolatility;

                // Box-Muller for normal noise
                double u1 = Math.Max(1e-10, _rng.NextDouble());
                double u2 = _rng.NextDouble();
                double dW = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);

                double currentPrice = (double)BasePricePerKg;
                double drift = theta * (mu - currentPrice);
                double diffusion = sigma * currentPrice * dW;

                // Supply/demand modifier
                double supplyDemandEffect = (GlobalDemandIndex - GlobalSupplyIndex) * 5.0;

                double newPrice = currentPrice + drift + diffusion + supplyDemandEffect;

                // Clamp to realistic range
                BasePricePerKg = (decimal)Math.Clamp(newPrice, 35.0, 150.0);

                // Apply certification multiplier
                PricePerKg = BasePricePerKg * _certificationMultiplier;

                // Update rolling average
                _priceHistory.Enqueue(PricePerKg);
                if (_priceHistory.Count > MaxHistoryLength)
                    _priceHistory.Dequeue();

                // Calculate 7-day average
                int windowSize = Math.Min(7, _priceHistory.Count);
                decimal sum = 0;
                int count = 0;
                foreach (var p in _priceHistory)
                {
                    if (count >= _priceHistory.Count - windowSize) sum += p;
                    count++;
                }
                WeeklyAvgPrice = sum / windowSize;

                // Random supply/demand shocks (2% chance per day)
                if (_rng.NextDouble() < 0.02)
                {
                    TriggerMarketShock();
                }
            }

            OnPriceChanged?.Invoke(PricePerKg);
        }

        /// <summary>
        /// Trigger a market shock event (supply disruption, demand spike, etc.)
        /// </summary>
        private void TriggerMarketShock()
        {
            double roll = _rng.NextDouble();

            if (roll < 0.3)
            {
                // Supply disruption (algae bloom, disease outbreak elsewhere)
                GlobalSupplyIndex *= 0.95;
                OnMarketEvent?.Invoke("📰 Chile reports major algae bloom — global supply tightens");
            }
            else if (roll < 0.5)
            {
                // Supply increase (good harvest season)
                GlobalSupplyIndex *= 1.03;
                OnMarketEvent?.Invoke("📰 Norwegian harvest volumes exceed expectations");
            }
            else if (roll < 0.7)
            {
                // Demand spike (health trend, sushi popularity)
                GlobalDemandIndex *= 1.04;
                OnMarketEvent?.Invoke("📰 Asian sushi market demand surges — salmon prices rise");
            }
            else if (roll < 0.85)
            {
                // Demand drop (recession fears, trade barriers)
                GlobalDemandIndex *= 0.96;
                OnMarketEvent?.Invoke("📰 EU imposes new tariffs on Norwegian salmon imports");
            }
            else
            {
                // Currency event
                double currencyShift = (_rng.NextDouble() - 0.5) * 0.05;
                BasePricePerKg *= (1.0m + (decimal)currencyShift);
                OnMarketEvent?.Invoke($"📰 NOK {(currencyShift > 0 ? "weakens" : "strengthens")} — export {(currencyShift > 0 ? "revenues up" : "revenues down")}");
            }

            // Mean-revert supply/demand indexes slowly
            GlobalSupplyIndex = 1.0 + (GlobalSupplyIndex - 1.0) * 0.95;
            GlobalDemandIndex = 1.0 + (GlobalDemandIndex - 1.0) * 0.95;
        }

        /// <summary>
        /// Apply an external supply or demand shock from a storyteller event.
        /// supplyFactor &lt; 1.0 tightens supply (prices rise), &gt; 1.0 increases supply (prices fall).
        /// demandFactor &gt; 1.0 increases demand (prices rise), &lt; 1.0 drops demand (prices fall).
        /// </summary>
        public void ApplyExternalShock(double supplyFactor, double demandFactor)
        {
            if (supplyFactor != 1.0) GlobalSupplyIndex *= supplyFactor;
            if (demandFactor != 1.0) GlobalDemandIndex *= demandFactor;

            GlobalSupplyIndex = Math.Clamp(GlobalSupplyIndex, 0.5, 2.0);
            GlobalDemandIndex = Math.Clamp(GlobalDemandIndex, 0.5, 2.0);

            OnPriceChanged?.Invoke(PricePerKg);
        }

        /// <summary>
        /// Update certification level and market access.
        /// </summary>
        public void UpdateCertificationLevel(CertificationLevel level, decimal priceMultiplier)
        {
            _certificationMultiplier = priceMultiplier;
            _accessibleMarkets = GetAccessibleMarkets(level);
            PricePerKg = BasePricePerKg * _certificationMultiplier;

            OnPriceChanged?.Invoke(PricePerKg);
            OnMarketAccessChanged?.Invoke(_accessibleMarkets);
        }

        private HashSet<string> GetAccessibleMarkets(CertificationLevel level)
        {
            var markets = new HashSet<string> { "Domestic" };
            if (level >= CertificationLevel.BasicCompliance)
            {
                markets.Add("EU"); markets.Add("USA"); markets.Add("China");
            }
            if (level >= CertificationLevel.ASCCertified)
            {
                markets.Add("WholeFoods"); markets.Add("Waitrose");
            }
            if (level >= CertificationLevel.OrganicCertified)
            {
                markets.Add("JapanPremium"); markets.Add("MichelinRestaurants");
            }
            return markets;
        }

        public bool CanAccessMarket(string marketName) => _accessibleMarkets.Contains(marketName);
        public HashSet<string> GetAccessibleMarkets() => new(_accessibleMarkets);

        /// <summary>Get price history for charting.</summary>
        public decimal[] GetPriceHistory() => _priceHistory.ToArray();

        /// <summary>Get current market status snapshot.</summary>
        public MarketStatus GetStatus() => new MarketStatus
        {
            SpotPrice = BasePricePerKg,
            EffectivePrice = PricePerKg,
            WeeklyAverage = WeeklyAvgPrice,
            SeasonalIndex = MonthlySeasonalIndex[(_simulationDay / 30) % 12],
            SupplyIndex = GlobalSupplyIndex,
            DemandIndex = GlobalDemandIndex,
            CertificationMultiplier = _certificationMultiplier,
            AccessibleMarkets = _accessibleMarkets.Count
        };
    }

    /// <summary>Market status snapshot for UI display.</summary>
    public struct MarketStatus
    {
        public decimal SpotPrice;
        public decimal EffectivePrice;
        public decimal WeeklyAverage;
        public double SeasonalIndex;
        public double SupplyIndex;
        public double DemandIndex;
        public decimal CertificationMultiplier;
        public int AccessibleMarkets;
    }
}