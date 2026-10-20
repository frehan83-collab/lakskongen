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
        /// Final corporate tax for a fiscal year's PRE-tax income, applying and
        /// updating the loss carry-forward (review F1137): a loss year adds the
        /// whole pre-tax loss to the carry-forward; a profit year may offset at
        /// most <see cref="LossCarryForwardLimit"/> of its income with it.
        /// </summary>
        public decimal ComputeCorporateTax(decimal preTaxIncome)
        {
            if (preTaxIncome <= 0m)
            {
                _lossCarryForward += -preTaxIncome;
                return 0m;
            }
            var offset = Math.Min(_lossCarryForward, preTaxIncome * LossCarryForwardLimit);
            _lossCarryForward -= offset;
            return (preTaxIncome - offset) * CorporateTaxRate;
        }

        /// <summary>
        /// Corporate tax on <paramref name="preTaxIncome"/> (with carry-forward)
        /// plus resource-rent tax on a positive <paramref name="resourceRentBase"/>.
        /// </summary>
        public decimal ComputeTotalTax(decimal preTaxIncome, decimal resourceRentBase)
        {
            var totalTax = ComputeCorporateTax(preTaxIncome)
                + Math.Max(0m, resourceRentBase) * ResourceRentTaxRate;
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