using System;
using System.Collections.Generic;

namespace LakskongenSim.Finance
{
    /// <summary>
    /// Phase 4.3: Comprehensive corporate tax system with:
    /// - 22% corporate tax on net income
    /// - 25% resource rent tax on norm price (Norwegian model)
    /// - Loss carry-forward with 80% annual limitation
    /// - Annual tax periods with closing entries
    /// </summary>
    public class TaxSystem
    {
        public decimal CorporateTaxRate { get; set; } = 0.22m;
        public decimal ResourceRentTaxRate { get; set; } = 0.25m;
        public decimal LossCarryForwardLimit { get; set; } = 0.80m; // 80% of taxable income can be offset

        public event Action<decimal, string>? OnTaxPaid; // amount, period

        private decimal _lossCarryForward = 0m;
        private int _currentPeriod = 0; // 0-based, 4 quarters per year

        /// <summary>Accumulated losses available for carry-forward.</summary>
        public decimal LossCarryForward => _lossCarryForward;

        /// <summary>Current quarter (0-3).</summary>
        public int CurrentQuarter => _currentPeriod % 4;

        /// <summary>Current year (1-based).</summary>
        public int CurrentYear => (_currentPeriod / 4) + 1;

        /// <summary>
        /// Advance to next quarter. Returns true if year-end.
        /// </summary>
        public bool AdvanceQuarter()
        {
            _currentPeriod++;
            bool isYearEnd = CurrentQuarter == 0;
            if (isYearEnd)
            {
                // Annual closing: reset period tracking, loss carry-forward persists
                OnTaxPaid?.Invoke(0m, $"Year {CurrentYear - 1} closing");
            }
            return isYearEnd;
        }

        /// <summary>
        /// Compute total tax for the period: corporate + resource rent.
        /// </summary>
        /// <param name="netIncome">Accounting net income before tax</param>
        /// <param name="resourceRentBase">Resource rent tax base (norm price × volume - deductions)</param>
        /// <returns>Total tax payable</returns>
        public decimal ComputeTotalTax(decimal netIncome, decimal resourceRentBase)
        {
            // Corporate tax: 22% on net income after loss carry-forward
            var taxableIncome = Math.Max(0m, netIncome - _lossCarryForward * LossCarryForwardLimit);
            var corporateTax = taxableIncome * CorporateTaxRate;

            // Resource rent tax: 25% on resource rent base
            var resourceRentTax = Math.Max(0m, resourceRentBase) * ResourceRentTaxRate;

            var totalTax = corporateTax + resourceRentTax;

            // Update loss carry-forward
            if (netIncome < 0m)
            {
                _lossCarryForward += Math.Abs(netIncome);
            }
            else
            {
                var utilized = Math.Min(_lossCarryForward * LossCarryForwardLimit, taxableIncome);
                _lossCarryForward = Math.Max(0m, _lossCarryForward - utilized);
            }

            OnTaxPaid?.Invoke(totalTax, $"Year {CurrentYear} Q{CurrentQuarter + 1}");
            return totalTax;
        }

        /// <summary>Reset for new game.</summary>
        public void Reset()
        {
            _lossCarryForward = 0m;
            _currentPeriod = 0;
        }

        /// <summary>Persistence snapshot.</summary>
        public (decimal LossCarryForward, int Period) ExportState()
            => (_lossCarryForward, _currentPeriod);

        internal void ImportState((decimal LossCarryForward, int Period) s)
        {
            _lossCarryForward = s.LossCarryForward;
            _currentPeriod = s.Period;
        }
    }
}