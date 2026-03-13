using System;
using System.Collections.Generic;

namespace LakskongenSim.Finance
{
    /// <summary>
    /// Simple P&amp;L, Balance Sheet, and Cash Flow records for deterministic simulation.
    /// Designed to be easy to test.
    /// </summary>
    public class FinancialStatements
    {
        public decimal Revenue { get; private set; }
        public decimal COGS { get; private set; }
        public decimal OperatingExpenses { get; private set; }
        public decimal InterestExpense { get; private set; }
        public decimal Taxes { get; private set; }

        public decimal Cash { get; private set; }
        public decimal Assets { get; private set; }
        public decimal Liabilities { get; private set; }
        public decimal Equity => Assets - Liabilities;

        public List<string> Events { get; } = new List<string>();

        public FinancialStatements(decimal startingCash = 0m, decimal startingAssets = 0m, decimal startingLiabilities = 0m)
        {
            Cash = startingCash;
            Assets = startingAssets;
            Liabilities = startingLiabilities;
        }

        public void RecordRevenue(decimal amount)
        {
            Revenue += amount;
            Cash += amount;
            Events.Add($"Revenue +{amount}");
        }

        public void RecordCOGS(decimal amount)
        {
            COGS += amount;
            Cash -= amount;
            Events.Add($"COGS -{amount}");
        }

        public void RecordOperatingExpense(decimal amount)
        {
            OperatingExpenses += amount;
            Cash -= amount;
            Events.Add($"OpEx -{amount}");
        }

        public void RecordInterest(decimal amount)
        {
            InterestExpense += amount;
            Cash -= amount;
            Events.Add($"Interest -{amount}");
        }

        public void RecordTax(decimal amount)
        {
            Taxes += amount;
            Cash -= amount;
            Events.Add($"Tax -{amount}");
        }

        public decimal ComputeNetIncome()
        {
            return Revenue - COGS - OperatingExpenses - InterestExpense - Taxes;
        }

        public void AddAsset(decimal amount)
        {
            Assets += amount;
            Cash -= amount;
            Events.Add($"Buy asset {amount}");
        }

        public void AddLiability(decimal amount)
        {
            Liabilities += amount;
            Cash += amount;
            Events.Add($"Take liability {amount}");
        }

        public void RepayLiability(decimal amount)
        {
            var repayment = Math.Min(amount, Liabilities);
            Liabilities -= repayment;
            Cash -= repayment;
            Events.Add($"Repay liability {repayment}");
        }
    }
}
