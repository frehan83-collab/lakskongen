using System;
using System.Collections.Generic;
using LakskongenSim.Core;

namespace LakskongenSim.Finance
{
    /// <summary>
    /// Phase 4.3: Corporate finance coordination system.
    /// Coordinates annual closing, tax computation, covenant testing, IPO mechanics,
    /// and quarterly guidance for public companies.
    /// </summary>
    public class CorporateFinanceSystem
    {
        private readonly SimRngStream _rng;
        internal SimRngStream RngForSnapshot => _rng;

        public CorporateFinanceSystem(SimRngStream rng)
        {
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        }

        public CovenantSystem Covenants { get; } = new CovenantSystem();
        public IPOSession IPO { get; } = new IPOSession();

        /// <summary>
        /// Execute annual closing: record the year's tax settlement (computed and
        /// paid/refunded by the engine ledger before this call), test covenants on
        /// the year's figures with interest-bearing debt = loan principal plus
        /// bonds, reset the P&amp;L, and report IPO state. Called once per year.
        /// </summary>
        public CorporateFinanceAnnualResult ExecuteAnnualClose(
            FinancialStatements statements,
            FinanceManager financeManager,
            InvestorSystem? investorSystem,
            AnnualTaxSettlement tax,
            decimal outstandingDebt,
            int currentDay)
        {
            ArgumentNullException.ThrowIfNull(statements);
            ArgumentNullException.ThrowIfNull(financeManager);
            ArgumentNullException.ThrowIfNull(tax);

            var result = new CorporateFinanceAnnualResult
            {
                Year = statements.CurrentYear,
                Day = currentDay,
                NetIncomeBeforeTax = tax.PreTaxIncome,
                CorporateTax = tax.CorporateTax + tax.ResourceRentTax,
                ResourceRentBase = tax.ResourceRentBase,
                LossCarryForward = tax.LossCarryForwardAfter,
                TaxPaid = tax.CorporateTax + tax.ResourceRentTax
            };

            // Covenants use the year's figures, so test them BEFORE the P&L reset
            // (the old order read interest and tax as 0 after AnnualClose).
            result.CovenantTest = TestCovenants(statements, financeManager, investorSystem, outstandingDebt);
            if (!result.CovenantTest.AllPassed)
                result.CovenantBreachCount = Covenants.GetActiveBreaches().Count;

            statements.AnnualClose();
            result.PnLReset = true;

            if (IPO.Stage == IPOSession.IPOStage.Listed)
            {
                // Next quarter guidance would be set by player via bridge
                result.IPOStage = IPO.Stage;
                result.SharePriceNOK = IPO.SharePriceNOK;
                result.MarketCapNOK = IPO.MarketCapNOK;
            }

            return result;
        }

        /// <summary>
        /// Quarterly processing: advance quarter, test covenants quarterly (debt =
        /// loan principal plus bonds), apply market drift to share price.
        /// </summary>
        public CorporateFinanceQuarterlyResult ExecuteQuarterly(
            FinancialStatements statements,
            FinanceManager financeManager,
            InvestorSystem? investorSystem,
            decimal outstandingDebt,
            int currentDay)
        {
            var result = new CorporateFinanceQuarterlyResult
            {
                Quarter = statements.CurrentPeriod % 4 + 1,
                Year = statements.CurrentYear,
                Day = currentDay
            };

            // Advance quarter
            var isYearEnd = statements.AdvanceQuarter();
            result.IsYearEnd = isYearEnd;

            result.CovenantTest = TestCovenants(statements, financeManager, investorSystem, outstandingDebt);

            // IPO market drift
            if (IPO.Stage == IPOSession.IPOStage.Listed)
            {
                IPO.ApplyMarketDrift(_rng);
                result.SharePriceNOK = IPO.SharePriceNOK;
                result.MarketCapNOK = IPO.MarketCapNOK;
            }

            return result;
        }

        /// <summary>
        /// Covenant test on the current (year-to-date) figures. EBITDA is
        /// earnings before interest, tax, depreciation and amortisation
        /// (revenue − COGS − OpEx; F2-04's facility depreciation is added back);
        /// net debt is interest-bearing debt minus cash (never negative).
        /// </summary>
        private CovenantTestResult TestCovenants(FinancialStatements statements, FinanceManager financeManager,
            InvestorSystem? investorSystem, decimal outstandingDebt)
        {
            var cash = financeManager?.Balance ?? 0m;
            var netDebt = Math.Max(0m, outstandingDebt - cash);
            var ebitda = statements.PreTaxIncome + statements.InterestExpense + statements.Depreciation;
            var undrawn = investorSystem?.AvailableCreditFacility ?? 0m;
            return Covenants.TestCovenants(netDebt, ebitda, statements.InterestExpense,
                statements.Equity, statements.Assets, cash, undrawn);
        }

        public void Reset()
        {
            Covenants.Reset();
            IPO.Reset();
        }

        /// <summary>Export state using Core namespace state classes for save/load compatibility.</summary>
        public Core.CorporateFinanceSystemState ExportState()
        {
            return new Core.CorporateFinanceSystemState
            {
                Covenants = Covenants.ExportState(),
                IPO = IPO.ExportCoreState()
            };
        }

        internal void ImportState(Core.CorporateFinanceSystemState state)
        {
            if (state.Covenants != null)
                Covenants.ImportState(state.Covenants);

            if (state.IPO != null)
                IPO.ImportCoreState(state.IPO);
        }
    }

    public class CorporateFinanceAnnualResult
    {
        public int Year { get; set; }
        public int Day { get; set; }
        public decimal NetIncomeBeforeTax { get; set; }
        public decimal CorporateTax { get; set; }
        public decimal ResourceRentBase { get; set; }
        public decimal LossCarryForward { get; set; }
        public decimal TaxPaid { get; set; }
        public bool PnLReset { get; set; }
        public CovenantTestResult CovenantTest { get; set; } = new();
        public int CovenantBreachCount { get; set; }
        public IPOSession.IPOStage IPOStage { get; set; }
        public decimal SharePriceNOK { get; set; }
        public decimal MarketCapNOK { get; set; }
    }

    public class CorporateFinanceQuarterlyResult
    {
        public int Quarter { get; set; }
        public int Year { get; set; }
        public int Day { get; set; }
        public bool IsYearEnd { get; set; }
        public CovenantTestResult CovenantTest { get; set; } = new();
        public IPOSession.IPOStage IPOStage { get; set; }
        public decimal SharePriceNOK { get; set; }
        public decimal MarketCapNOK { get; set; }
    }

    /// <summary>
    /// One fiscal year's tax settlement: final corporate tax (after loss
    /// carry-forward) plus resource-rent tax, against the provisional tax paid
    /// during the year. <see cref="Settlement"/> &gt; 0 was paid, &lt; 0 refunded.
    /// </summary>
    public sealed record AnnualTaxSettlement(
        decimal PreTaxIncome,
        decimal CorporateTax,
        decimal ResourceRentBase,
        decimal ResourceRentTax,
        decimal ProvisionalPaid,
        decimal Settlement,
        decimal LossCarryForwardAfter);
}