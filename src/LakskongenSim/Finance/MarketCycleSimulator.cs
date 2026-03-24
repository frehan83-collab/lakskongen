using System;
using System.Collections.Generic;

namespace LakskongenSim.Finance
{
    /// <summary>
    /// Simulates global salmon market supply/demand dynamics.
    /// </summary>
    public class MarketCycleSimulator
    {
        private readonly Random _random = new();

        // Global supply (thousand tonnes per year)
        private static readonly Dictionary<string, double> GlobalSupply = new()
        {
            { "Norway", 1500 },
            { "Chile", 900 },
            { "Scotland", 180 },
            { "Faroe", 90 },
            { "Canada", 150 },
            { "Other", 200 },
        };

        private double _supplyShock = 0; // Temporary supply disruption
        private double _demandTrend = 1.0; // Long-term demand growth

        /// <summary>
        /// Simulate one month of market dynamics.
        /// </summary>
        public double SimulateMonth(int month, double currentSpotPrice)
        {
            // Supply events (disease, algae, disruptions)
            double eventChance = _random.NextDouble();
            if (eventChance < 0.02) // 2% chance of major event
            {
                _supplyShock = -0.1 * _random.NextDouble(); // Up to 10% supply reduction
            }
            else if (_supplyShock < 0)
            {
                _supplyShock *= 0.7; // Gradual recovery
            }

            // Demand grows ~3% per year
            _demandTrend *= 1.0 + (0.03 / 12.0);

            // Price adjustment based on supply/demand balance
            double supplyEffect = 1.0 + _supplyShock;
            double priceChange = (_demandTrend / supplyEffect - 1.0) * 0.1;

            // Add monthly noise
            double noise = (_random.NextDouble() - 0.5) * 0.03;

            return currentSpotPrice * (1.0 + priceChange + noise);
        }

        /// <summary>
        /// Forecast price for N days ahead.
        /// </summary>
        public double ForecastPrice(double currentPrice, int daysAhead)
        {
            double price = currentPrice;
            int months = daysAhead / 30;

            for (int m = 0; m < months; m++)
            {
                int futureMonth = (DateTime.Now.Month + m - 1) % 12 + 1;
                price = SimulateMonth(futureMonth, price);
            }

            return price;
        }

        /// <summary>
        /// Get 7, 30, 90 day price forecasts.
        /// </summary>
        public (double day7, double day30, double day90) GetForecasts(double currentPrice)
        {
            return (
                ForecastPrice(currentPrice, 7),
                ForecastPrice(currentPrice, 30),
                ForecastPrice(currentPrice, 90)
            );
        }

        /// <summary>
        /// Get total global supply in thousand tonnes.
        /// </summary>
        public double GetTotalGlobalSupply()
        {
            double total = 0;
            foreach (var kvp in GlobalSupply)
                total += kvp.Value;
            return total * (1.0 + _supplyShock);
        }
    }
}
