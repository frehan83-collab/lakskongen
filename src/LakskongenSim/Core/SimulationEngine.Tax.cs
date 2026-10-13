using System;
using System.Linq;
using LakskongenSim.Finance;

namespace LakskongenSim.Core;

/// <summary>
/// Corporate and resource-rent tax (review F1128/F1131/F1137/F342).
/// <para>
/// Before: every 30 days the engine charged 22% of the year-to-date NET income
/// (already net of the tax it had charged), so the same profit was taxed again
/// each month and never credited back; the annual close then taxed the
/// after-tax income a second time; the resource-rent base was 1000x too small
/// and built from standing biomass, so the rent tax was always 0.
/// </para>
/// <para>
/// Now: a monthly provisional payment brings the tax paid this fiscal year up
/// to rate × max(0, year-to-date PRE-tax income); the year-end settlement
/// computes the final corporate tax (with loss carry-forward) plus resource-rent
/// tax on the harvest valued at the norm price, and pays the difference or
/// refunds the excess provisional tax.
/// </para>
/// </summary>
public partial class SimulationEngine
{
    /// <summary>
    /// Monthly provisional tax: pay up to the rate on year-to-date pre-tax
    /// income. Excess (e.g. after a loss month) is refunded at the year-end
    /// settlement, not mid-year.
    /// </summary>
    internal void SettleProvisionalTax()
    {
        var fs = FinancialStatements;
        if (fs == null || TaxSystem == null) return;
        decimal due = Math.Max(0m, fs.PreTaxIncome) * TaxSystem.CorporateTaxRate;
        decimal delta = due - fs.Taxes;
        if (delta > 0m)
        {
            PayCost(delta, CostBooking.Tax, "tax",
                $"⚠️ Cannot pay {delta:N0} NOK in provisional tax. Balance: {FinanceManager.Balance:N0} NOK");
        }
    }

    /// <summary>
    /// Year-end settlement: final corporate tax on the year's pre-tax income
    /// (after loss carry-forward) plus resource-rent tax when enabled, settled
    /// against the provisional tax already paid. Cash short? The payment goes
    /// on credit like every other liability (the old close discarded the
    /// refused Spend and still booked the tax as paid).
    /// </summary>
    internal AnnualTaxSettlement SettleAnnualTax()
    {
        var fs = FinancialStatements ?? throw new InvalidOperationException("Engine not initialized");
        decimal preTax = fs.PreTaxIncome;
        decimal corporate = TaxSystem.ComputeCorporateTax(preTax);

        // Resource rent: harvest valued at the norm price minus the year's
        // sea-phase costs, taxed above the calibrated basic deduction.
        decimal rentBase = State.ResourceRentHarvestValueThisTaxYear - (fs.COGS + fs.OperatingExpenses);
        decimal rentTax = EnableResourceRentTax
            ? ResourceRentTax.Compute(rentBase, _calibration.Finance.ResourceRentBasicDeductionNOK,
                TaxSystem.ResourceRentTaxRate)
            : 0m;

        decimal provisional = fs.Taxes;
        decimal settlement = corporate + rentTax - provisional;
        if (settlement > 0m)
            PayCost(settlement, CostBooking.Tax, "tax",
                $"⚠️ Cannot pay {settlement:N0} NOK year-end tax. Balance: {FinanceManager.Balance:N0} NOK");
        else if (settlement < 0m)
            ReceiveTaxRefund(-settlement);

        if (rentTax > 0m)
        {
            ResourceRentPaid += rentTax;
            State.ResourceRentPaid = ResourceRentPaid;
        }
        State.HarvestedKgThisTaxYear = 0.0;
        State.ResourceRentHarvestValueThisTaxYear = 0m;

        return new AnnualTaxSettlement(preTax, corporate, Math.Max(0m, rentBase), rentTax,
            provisional, settlement, TaxSystem.LossCarryForward);
    }

    /// <summary>Interest-bearing debt (NOK): outstanding loan principal plus bonds.</summary>
    public decimal TotalDebtNok => OutstandingDebt();

    /// <summary>Interest-bearing debt: outstanding loan principal plus bonds.</summary>
    internal decimal OutstandingDebt()
        => State.ActiveLoans.Sum(l => Math.Max(0m, l.Principal))
           + (InvestorSystem?.OutstandingBondPrincipal ?? 0m);

    /// <summary>
    /// Add a harvest to this tax year's resource-rent base, valued at the Price
    /// Council norm price (spot when the council has not priced yet).
    /// </summary>
    internal void RecordHarvestForResourceRent(double harvestedKg)
    {
        if (harvestedKg <= 0.0) return;
        decimal normPrice = PenPriceCouncil?.MonthlyPenPrice > 0m
            ? PenPriceCouncil.MonthlyPenPrice
            : MarketSimulation?.PricePerKg ?? 0m;
        State.HarvestedKgThisTaxYear += harvestedKg;
        State.ResourceRentHarvestValueThisTaxYear += (decimal)harvestedKg * normPrice;
    }
}
