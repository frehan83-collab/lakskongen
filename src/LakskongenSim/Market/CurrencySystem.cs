using System;
using System.Collections.Generic;

namespace LakskongenSim.Market
{
    /// <summary>
    /// Currency exchange rate simulation system affecting export revenue.
    /// Models NOK exchange rates against major export currencies (EUR, USD, JPY, GBP)
    /// using Ornstein-Uhlenbeck mean-reverting dynamics aligned with MarketSimulation.
    ///
    /// Key markets:
    /// - EUR: European Union (largest export destination ~45% of Norwegian seafood)
    /// - USD: USA (growing market ~10%)
    /// - JPY: Japan (premium sushi market ~7%)
    /// - GBP: UK (post-Brexit volatility ~5%)
    ///
    /// Currency strength affects export profitability:
    /// - Weak NOK → Higher export revenue (foreign buyers pay same in their currency, you get more NOK)
    /// - Strong NOK → Lower export revenue (foreign buyers pay same, you get fewer NOK)
    ///
    /// Calibration based on historical NOK ranges 2018-2025:
    /// - NOK/EUR: 9.5-11.5 (mean ~10.5)
    /// - NOK/USD: 8.0-11.5 (mean ~9.5)
    /// - NOK/JPY: 0.07-0.09 (mean ~0.08)
    /// - NOK/GBP: 11.0-13.5 (mean ~12.0)
    /// </summary>
    public class CurrencySystem
    {
        // ─── Base rates (NOK per 1 unit of foreign currency) ───
        private const decimal BaseNokPerEur = 10.5m;
        private const decimal BaseNokPerUsd = 9.5m;
        private const decimal BaseNokPerJpy = 0.08m;
        private const decimal BaseNokPerGbp = 12.0m;

        // ─── O-U parameters (calibrated to weekly moves, similar to MarketSimulation) ───
        private const decimal WeeklyTheta = 0.015m;  // Mean reversion speed (similar to market)
        private const decimal WeeklySigma = 0.12m;   // Volatility ~1.2% per week (FX is less volatile than salmon spot)
        private const int DaysPerWeek = 7;

        // ─── Exchange rate floors/ceilings (prevent unrealistic extremes) ───
        private const decimal MinNokPerEur = 9.0m;
        private const decimal MaxNokPerEur = 12.0m;
        private const decimal MinNokPerUsd = 7.5m;
        private const decimal MaxNokPerUsd = 12.0m;
        private const decimal MinNokPerJpy = 0.06m;
        private const decimal MaxNokPerJpy = 0.10m;
        private const decimal MinNokPerGbp = 10.0m;
        private const decimal MaxNokPerGbp = 14.0m;

        // ─── Current exchange rates ───
        public decimal NokPerEur { get; private set; } = BaseNokPerEur;
        public decimal NokPerUsd { get; private set; } = BaseNokPerUsd;
        public decimal NokPerJpy { get; private set; } = BaseNokPerJpy;
        public decimal NokPerGbp { get; private set; } = BaseNokPerGbp;

        // ─── Central bank interest rate (affects NOK strength) ───
        /// <summary>
        /// Norges Bank policy rate (%). Higher rate → stronger NOK (capital inflows).
        /// Typical range: 0.0% (COVID era) to 4.5% (inflation fighting).
        /// </summary>
        public decimal CentralBankRate { get; private set; } = 2.5m;

        // ─── Export market weights (approx % of Norwegian seafood exports) ───
        private static readonly Dictionary<string, (decimal weight, Func<CurrencySystem, decimal> rateGetter)> ExportMarkets = new()
        {
            { "EU", (0.45m, cs => cs.NokPerEur) },
            { "USA", (0.10m, cs => cs.NokPerUsd) },
            { "Japan", (0.07m, cs => cs.NokPerJpy * 100m) },  // JPY quoted per 100 yen
            { "UK", (0.05m, cs => cs.NokPerGbp) }
        };

        // ─── Simulation state ───
        private readonly Random _rng = new();
        private int _simulationDay;

        // ─── Events ───
        public event Action<string>? OnCurrencyEvent;

        /// <summary>
        /// Initialize the currency system with default exchange rates.
        /// </summary>
        public void Initialize()
        {
            NokPerEur = BaseNokPerEur;
            NokPerUsd = BaseNokPerUsd;
            NokPerJpy = BaseNokPerJpy;
            NokPerGbp = BaseNokPerGbp;
            CentralBankRate = 2.5m;
            _simulationDay = 0;
        }

        /// <summary>
        /// Advance currency simulation by a number of days.
        /// Uses Ornstein-Uhlenbeck mean reversion with central bank rate influence.
        /// </summary>
        public void AdvanceDays(int days)
        {
            for (int d = 0; d < days; d++)
            {
                _simulationDay++;

                // Daily O-U updates for each currency pair
                NokPerEur = UpdateExchangeRate(NokPerEur, BaseNokPerEur, MinNokPerEur, MaxNokPerEur);
                NokPerUsd = UpdateExchangeRate(NokPerUsd, BaseNokPerUsd, MinNokPerUsd, MaxNokPerUsd);
                NokPerJpy = UpdateExchangeRate(NokPerJpy, BaseNokPerJpy, MinNokPerJpy, MaxNokPerJpy);
                NokPerGbp = UpdateExchangeRate(NokPerGbp, BaseNokPerGbp, MinNokPerGbp, MaxNokPerGbp);

                // Random currency events (1% chance per day)
                if (_rng.NextDouble() < 0.01)
                    TriggerCurrencyEvent();
            }
        }

        /// <summary>
        /// Update a single exchange rate using O-U mean reversion.
        /// Higher central bank rate → NOK strengthens → rate decreases (you get fewer NOK per foreign currency).
        /// </summary>
        private decimal UpdateExchangeRate(decimal currentRate, decimal baseRate, decimal minRate, decimal maxRate)
        {
            // Central bank rate effect: higher rate → stronger NOK → lower exchange rate
            // Sensitivity: 1% rate change → ~0.5% exchange rate change
            var rateBias = (CentralBankRate - 2.5m) * 0.005m;
            var targetRate = baseRate * (1.0m - rateBias);

            var thetaDaily = WeeklyTheta / DaysPerWeek;
            var sigmaDaily = WeeklySigma / (decimal)Math.Sqrt(DaysPerWeek);

            // O-U dynamics
            double u1 = Math.Max(1e-10, _rng.NextDouble());
            double u2 = _rng.NextDouble();
            double normalShock = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);

            var drift = thetaDaily * (targetRate - currentRate);
            var diffusion = sigmaDaily * (decimal)normalShock;

            var newRate = currentRate + drift + diffusion;
            return Math.Clamp(newRate, minRate, maxRate);
        }

        /// <summary>
        /// Trigger random currency events (NOK weakness/strength, central bank actions).
        /// </summary>
        private void TriggerCurrencyEvent()
        {
            double roll = _rng.NextDouble();

            if (roll < 0.25)
            {
                // NOK weakens (oil price drop, risk-off sentiment)
                NokPerEur *= 1.02m;
                NokPerUsd *= 1.02m;
                NokPerJpy *= 1.02m;
                NokPerGbp *= 1.02m;
                OnCurrencyEvent?.Invoke("💱 NOK weakens — export revenues more profitable");
            }
            else if (roll < 0.50)
            {
                // NOK strengthens (oil rally, higher interest rates)
                NokPerEur *= 0.98m;
                NokPerUsd *= 0.98m;
                NokPerJpy *= 0.98m;
                NokPerGbp *= 0.98m;
                OnCurrencyEvent?.Invoke("💱 Strong NOK — imports cheaper, exports less profitable");
            }
            else if (roll < 0.65)
            {
                // Central bank raises rates
                CentralBankRate += 0.25m;
                CentralBankRate = Math.Min(CentralBankRate, 5.0m);
                OnCurrencyEvent?.Invoke($"💱 Norges Bank raises policy rate to {CentralBankRate:F2}% — NOK strengthens");
            }
            else if (roll < 0.80)
            {
                // Central bank cuts rates
                CentralBankRate -= 0.25m;
                CentralBankRate = Math.Max(CentralBankRate, 0.0m);
                OnCurrencyEvent?.Invoke($"💱 Norges Bank cuts policy rate to {CentralBankRate:F2}% — NOK weakens");
            }
            else
            {
                // EUR/USD volatility spike (global macro event)
                var usdShock = (_rng.NextDouble() - 0.5) * 0.04;
                NokPerUsd *= (1.0m + (decimal)usdShock);
                NokPerUsd = Math.Clamp(NokPerUsd, MinNokPerUsd, MaxNokPerUsd);
                OnCurrencyEvent?.Invoke("💱 USD volatility spike — global macro uncertainty");
            }

            // Re-clamp after events
            NokPerEur = Math.Clamp(NokPerEur, MinNokPerEur, MaxNokPerEur);
            NokPerUsd = Math.Clamp(NokPerUsd, MinNokPerUsd, MaxNokPerUsd);
            NokPerJpy = Math.Clamp(NokPerJpy, MinNokPerJpy, MaxNokPerJpy);
            NokPerGbp = Math.Clamp(NokPerGbp, MinNokPerGbp, MaxNokPerGbp);
        }

        /// <summary>
        /// Calculate the weighted average exchange rate impact on export revenue.
        /// Returns a multiplier: greater than 1.0 = weak NOK (more profitable exports), less than 1.0 = strong NOK (less profitable).
        /// </summary>
        public decimal GetExportRevenueMultiplier()
        {
            decimal weightedImpact = 0m;

            foreach (var (market, (weight, rateGetter)) in ExportMarkets)
            {
                var currentRate = rateGetter(this);
                var baseRate = market switch
                {
                    "EU" => BaseNokPerEur,
                    "USA" => BaseNokPerUsd,
                    "Japan" => BaseNokPerJpy * 100m,
                    "UK" => BaseNokPerGbp,
                    _ => 1m
                };

                // Rate deviation from base: higher rate = weaker NOK = better for exports
                var deviation = (currentRate - baseRate) / baseRate;
                weightedImpact += weight * deviation;
            }

            // Convert to multiplier: 1.0 + weighted impact
            return 1.0m + weightedImpact;
        }

        /// <summary>
        /// Get a snapshot of current currency system status.
        /// </summary>
        public CurrencyStatus GetStatus() => new CurrencyStatus
        {
            NokPerEur = NokPerEur,
            NokPerUsd = NokPerUsd,
            NokPerJpy = NokPerJpy,
            NokPerGbp = NokPerGbp,
            CentralBankRate = CentralBankRate,
            ExportRevenueMultiplier = GetExportRevenueMultiplier(),
            SimulationDay = _simulationDay
        };
    }

    /// <summary>Currency status snapshot for UI display.</summary>
    public struct CurrencyStatus
    {
        public decimal NokPerEur;
        public decimal NokPerUsd;
        public decimal NokPerJpy;
        public decimal NokPerGbp;
        public decimal CentralBankRate;
        public decimal ExportRevenueMultiplier;
        public int SimulationDay;
    }
}
