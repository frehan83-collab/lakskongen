using System;
using System.Collections.Generic;

namespace LakskongenSim.Finance
{
    /// <summary>
    /// Simple P&amp;L (Profit and Loss), Balance Sheet, and Cash Flow records for deterministic simulation.
    /// Designed to be easy to test.
    /// NOTE: Cash is NO LONGER tracked here - FinanceManager.Balance is the single source of truth.
    /// This class only tracks P&amp;L flows for reporting purposes.
    /// </summary>
    public class FinancialStatements
    {
        public decimal Revenue { get; private set; }
        public decimal COGS { get; private set; }
        public decimal OperatingExpenses { get; private set; }
        public decimal InterestExpense { get; private set; }
        public decimal Taxes { get; private set; }

        /// <summary>
        /// Cash is tracked by FinanceManager, not here.
        /// This property is kept for backward compatibility but should not be used.
        /// Query FinanceManager.Balance directly instead.
        /// </summary>
        [Obsolete("Use FinanceManager.Balance instead. Cash tracking moved to FinanceManager.")]
        public decimal Cash { get; private set; }
        
        public decimal Assets { get; private set; }
        public decimal Liabilities { get; private set; }
        public decimal Equity => Assets - Liabilities;

        public List<string> Events { get; } = new List<string>();

        public FinancialStatements(decimal startingCash = 0m, decimal startingAssets = 0m, decimal startingLiabilities = 0m)
        {
            // startingCash is ignored - cash is tracked by FinanceManager
            Assets = startingAssets;
            Liabilities = startingLiabilities;
        }

        /// <summary>
        /// Record revenue for P&amp;L tracking only. Does NOT modify cash.
        /// FinanceManager.AddRevenue() handles the actual cash change.
        /// </summary>
        public void RecordRevenue(decimal amount)
        {
            Revenue += amount;
            Events.Add($"Revenue +{amount}");
        }

        /// <summary>
        /// Record COGS for P&amp;L tracking only. Does NOT modify cash.
        /// FinanceManager.Spend() handles the actual cash change.
        /// </summary>
        public void RecordCOGS(decimal amount)
        {
            COGS += amount;
            Events.Add($"COGS -{amount}");
        }

        /// <summary>
        /// Record operating expense for P&amp;L tracking only. Does NOT modify cash.
        /// FinanceManager.Spend() handles the actual cash change.
        /// </summary>
        public void RecordOperatingExpense(decimal amount)
        {
            OperatingExpenses += amount;
            Events.Add($"OpEx -{amount}");
        }

        /// <summary>
        /// Record interest expense for P&amp;L tracking only. Does NOT modify cash.
        /// FinanceManager.Spend() handles the actual cash change.
        /// </summary>
        public void RecordInterest(decimal amount)
        {
            InterestExpense += amount;
            Events.Add($"Interest -{amount}");
        }

        /// <summary>
        /// Record tax expense for P&amp;L tracking only. Does NOT modify cash.
        /// FinanceManager.Spend() handles the actual cash change.
        /// </summary>
        public void RecordTax(decimal amount)
        {
            Taxes += amount;
            Events.Add($"Tax -{amount}");
        }

        public decimal ComputeNetIncome()
        {
            return Revenue - COGS - OperatingExpenses - InterestExpense - Taxes;
        }

        /// <summary>
        /// Record asset purchase. Does NOT modify cash - caller must use FinanceManager.Spend().
        /// </summary>
        public void AddAsset(decimal amount)
        {
            Assets += amount;
            Events.Add($"Buy asset {amount}");
        }

        /// <summary>
        /// Record liability increase. Does NOT modify cash - caller must use FinanceManager.AddRevenue().
        /// </summary>
        public void AddLiability(decimal amount)
        {
            Liabilities += amount;
            Events.Add($"Take liability {amount}");
        }

        /// <summary>
        /// Record liability repayment. Does NOT modify cash - caller must use FinanceManager.Spend().
        /// </summary>
        public void RepayLiability(decimal amount)
        {
            var repayment = Math.Min(amount, Liabilities);
            Liabilities -= repayment;
            Events.Add($"Repay liability {repayment}");
        }
    }
}
