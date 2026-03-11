using System;

namespace LakskongenSim.Finance
{
    /// <summary>
    /// Calculates core financial KPIs from FinancialStatements.
    /// </summary>
    public static class KPIs
    {
        public static decimal EBITDA(FinancialStatements fs)
        {
            // EBITDA = Revenue - COGS - OperatingExpenses + (Interest+Taxes added back)
            return fs.Revenue - fs.COGS - fs.OperatingExpenses;
        }

        public static decimal OperatingMargin(FinancialStatements fs)
        {
            if (fs.Revenue == 0) return 0m;
            return EBITDA(fs) / fs.Revenue;
        }

        public static decimal DebtToEquity(FinancialStatements fs)
        {
            var equity = fs.Equity;
            if (equity == 0) return decimal.MaxValue;
            return fs.Liabilities / equity;
        }

        public static decimal BreakEvenRevenue(decimal fixedCosts, decimal contributionMargin)
        {
            if (contributionMargin <= 0) return decimal.MaxValue;
            return fixedCosts / contributionMargin;
        }
    }
}
