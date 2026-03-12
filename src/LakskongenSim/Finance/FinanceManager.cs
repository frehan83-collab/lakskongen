using System;

namespace LakskongenSim.Finance
{
    /// <summary>
    /// Tracks cashflow, applies costs and revenue.
    /// </summary>
    public class FinanceManager
    {
        public decimal Balance { get; private set; }

        public event Action<decimal>? OnBalanceChanged;

        public FinanceManager(decimal startingBalance = 1_000_000m)
        {
            Balance = startingBalance;
        }

        public void AddRevenue(decimal amount)
        {
            Balance += amount;
            OnBalanceChanged?.Invoke(Balance);
        }

        public bool Spend(decimal amount)
        {
            if (amount <= 0) return true;
            if (Balance < amount) return false;
            Balance -= amount;
            OnBalanceChanged?.Invoke(Balance);
            return true;
        }

        /// <summary>
        /// Returns the current available capital (balance) as float.
        /// Used by expansion systems (VerticalIntegration, Acquisition) to check affordability.
        /// </summary>
        public float GetAvailableCapital() => (float)Balance;

        /// <summary>
        /// Deducts capital from balance for asset purchases.
        /// Wrapper around Spend() for semantic clarity in expansion context.
        /// </summary>
        /// <param name="amount">Amount to deduct (float for compatibility with expansion systems).</param>
        public void DeductCapital(float amount) => Spend((decimal)amount);
    }
}