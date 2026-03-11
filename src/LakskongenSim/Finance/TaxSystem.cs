using System;

namespace LakskongenSim.Finance
{
    /// <summary>
    /// Simplified corporate tax system.
    /// </summary>
    public class TaxSystem
    {
        public decimal CorporateTaxRate = 0.22m;

        public event Action<decimal>? OnTaxPaid;

        public decimal ComputeTax(decimal profit)
        {
            var tax = Math.Max(0m, profit) * CorporateTaxRate;
            OnTaxPaid?.Invoke(tax);
            return tax;
        }
    }
}