using System;

namespace LakskongenSim.Finance
{
    /// <summary>
    /// Tracks cashflow, applies costs and revenue.
    /// Integrates with sustainability system for bonuses and penalties.
    /// </summary>
    public class FinanceManager
    {
        /// <summary>
        /// Current balance in NOK. This is the SINGLE SOURCE OF TRUTH for player cash.
        /// All balance changes must go through AddRevenue() or Spend().
        /// </summary>
        public decimal Balance { get; private set; }

        // Sustainability bonuses/penalties
        public decimal ESGInvestmentDiscount { get; private set; } = 0m; // 0-0.30 (0-30%)
        public decimal GovernmentGrantMultiplier { get; private set; } = 1.0m; // 1.0-1.5x
        
        public event Action<decimal>? OnBalanceChanged;

        public FinanceManager(decimal startingBalance = 1_000_000m)
        {
            Balance = startingBalance;
        }

        /// <summary>
        /// Add revenue to balance. Use for all income (sales, grants, investor injections).
        /// </summary>
        public void AddRevenue(decimal amount)
        {
            if (amount < 0)
            {
                Console.WriteLine($"⚠️ Warning: AddRevenue called with negative amount {amount}. Use Spend() instead.");
                return;
            }
            Balance += amount;
            OnBalanceChanged?.Invoke(Balance);
        }

        /// <summary>
        /// Deduct expense from balance. Returns false if insufficient funds.
        /// IMPORTANT: Callers MUST check the return value and handle bankruptcy/debt logic.
        /// </summary>
        public bool Spend(decimal amount)
        {
            if (amount <= 0) return true;
            if (Balance < amount)
            {
                Console.WriteLine($"⚠️ Insufficient funds: tried to spend {amount:N0} NOK but only have {Balance:N0} NOK");
                return false;
            }
            Balance -= amount;
            OnBalanceChanged?.Invoke(Balance);
            return true;
        }
        
        /// <summary>
        /// Check if an amount can be afforded without actually spending it.
        /// Use before attempting purchases to provide better UX.
        /// </summary>
        public bool CanAfford(decimal amount)
        {
            return Balance >= amount;
        }
        
        /// <summary>
        /// Force spend even if balance goes negative (for debt/loans).
        /// Only use when bankruptcy/debt mechanics are intentional.
        /// </summary>
        public void ForceSpend(decimal amount)
        {
            if (amount <= 0) return;
            Balance -= amount;
            OnBalanceChanged?.Invoke(Balance);
            Console.WriteLine($"💳 Forced spend: {amount:N0} NOK (balance now {Balance:N0} NOK)");
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

        // ===== SUSTAINABILITY INTEGRATION =====

        /// <summary>
        /// Apply ESG investment discount (for sustainability score >= 80).
        /// Call when purchasing green technology/equipment.
        /// </summary>
        /// <param name="baseCost">Original cost before discount</param>
        /// <returns>Discounted cost</returns>
        public decimal ApplyESGDiscount(decimal baseCost)
        {
            if (ESGInvestmentDiscount > 0)
            {
                var discount = baseCost * ESGInvestmentDiscount;
                Console.WriteLine($"💚 ESG Discount Applied: {discount:N0} NOK saved ({ESGInvestmentDiscount:P0} off)");
                return baseCost - discount;
            }
            return baseCost;
        }

        /// <summary>
        /// Apply government grant multiplier (for sustainability score >= 95).
        /// Call when receiving grants/subsidies.
        /// </summary>
        /// <param name="baseGrant">Base grant amount</param>
        /// <returns>Multiplied grant amount</returns>
        public decimal ApplyGrantMultiplier(decimal baseGrant)
        {
            if (GovernmentGrantMultiplier > 1.0m)
            {
                var bonus = baseGrant * (GovernmentGrantMultiplier - 1.0m);
                Console.WriteLine($"🏅 Grant Multiplier Applied: +{bonus:N0} NOK ({GovernmentGrantMultiplier:N1}x)");
                return baseGrant * GovernmentGrantMultiplier;
            }
            return baseGrant;
        }

        /// <summary>
        /// Update sustainability bonuses based on current score.
        /// Called by SimulationEngine when sustainability score changes.
        /// </summary>
        public void UpdateSustainabilityBonuses(int score)
        {
            // ESG investment discount at 80+ score (30% discount)
            if (score >= 80)
                ESGInvestmentDiscount = 0.30m;
            else
                ESGInvestmentDiscount = 0m;

            // Government grant multiplier at 95+ score (1.5x grants)
            if (score >= 95)
                GovernmentGrantMultiplier = 1.5m;
            else
                GovernmentGrantMultiplier = 1.0m;
        }

        /// <summary>
        /// Apply quarterly fine for non-compliance (score &lt; 50).
        /// Called by SimulationEngine quarterly for non-compliant farms.
        /// </summary>
        public void ApplyComplianceFine(decimal fineAmount = 50_000m)
        {
            if (Spend(fineAmount))
            {
                Console.WriteLine($"💸 Compliance Fine Paid: {fineAmount:N0} NOK");
            }
            else
            {
                Console.WriteLine($"⚠️ Insufficient funds for compliance fine: {fineAmount:N0} NOK");
            }
        }
    }
}