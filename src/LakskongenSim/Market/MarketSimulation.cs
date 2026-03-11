using System;

namespace LakskongenSim.Market
{
    /// <summary>
    /// Simulates market price dynamics for products.
    /// </summary>
    public class MarketSimulation
    {
        public decimal PricePerKg { get; private set; } = 60m; // NOK/kg

        public event Action<decimal>? OnPriceChanged;

        public void AdvanceDays(int days)
        {
            // very simple mean-reverting noise
            var rand = new Random();
            var shock = (decimal)(rand.NextDouble() - 0.5) * 2m;
            PricePerKg = Math.Max(20m, PricePerKg + shock);
            OnPriceChanged?.Invoke(PricePerKg);
        }
    }
}