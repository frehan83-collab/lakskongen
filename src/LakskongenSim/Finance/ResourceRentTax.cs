using System;

namespace LakskongenSim.Finance
{
    /// <summary>
    /// Norwegian resource-rent tax (grunnrenteskatt) on salmon farming, modelled
    /// as a pure cash-flow tax on the sea phase.
    ///
    /// <para>
    /// Research anchors (Norwegian Ministry of Finance / Skatteetaten):
    /// <list type="bullet">
    /// <item><description>Effective rate: <b>25%</b> of resource-rent income (was 22% corporate tax on top).</description></item>
    /// <item><description>Standard deduction (friinntekt-equivalent): <b>NOK 70,000,000</b> per company per year.</description></item>
    /// <item><description>Cash-flow semantics: the base is the net profit of the <b>sea phase</b> only.
    /// A loss-year produces <b>no tax</b> (the negative base carries forward, not refunded here).</description></item>
    /// <item><description>Only resource-rent-eligible sea production is taxed; smolt/hatchery onshore phases are not.</description></item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// This type is intentionally stateless and free of Godot/simulation dependencies
    /// so it can be unit-tested and reused by any accounting path.
    /// </para>
    /// </summary>
    public static class ResourceRentTax
    {
        /// <summary>Statutory effective resource-rent rate (25%).</summary>
        public const decimal DefaultRate = 0.25m;

        /// <summary>Annual standard deduction before resource-rent tax applies (NOK 70,000,000).</summary>
        public const decimal StandardDeductionNok = 70_000_000m;

        /// <summary>
        /// Compute resource-rent tax owed for a period.
        ///
        /// Cash-flow semantics: the deduction is subtracted from the net sea-phase
        /// profit and only a positive remainder is taxed. Profits at or below the
        /// deduction (including losses) yield <b>zero</b> tax — losses are never
        /// taxed and are not refunded by this pure function.
        /// </summary>
        /// <param name="netSeaPhaseProfit">Net profit attributable to the sea phase (NOK). May be negative.</param>
        /// <param name="deduction">Standard deduction applied before the rate (NOK). Negative values are treated as 0.</param>
        /// <param name="rate">Effective tax rate in [0,1]. Negative values are treated as 0, values above 1 are clamped to 1.</param>
        /// <param name="isResourceRentEligible">False for onshore/non-eligible activity, which is not subject to resource rent.</param>
        /// <returns>Tax owed (NOK), never negative.</returns>
        public static decimal Compute(
            decimal netSeaPhaseProfit,
            decimal deduction = StandardDeductionNok,
            decimal rate = DefaultRate,
            bool isResourceRentEligible = true)
        {
            if (!isResourceRentEligible)
                return 0m;

            if (netSeaPhaseProfit <= 0m)
                return 0m;

            var effectiveDeduction = deduction > 0m ? deduction : 0m;
            var taxableBase = netSeaPhaseProfit - effectiveDeduction;
            if (taxableBase <= 0m)
                return 0m;

            var effectiveRate = Math.Clamp(rate, 0m, 1m);
            return taxableBase * effectiveRate;
        }
    }
}
