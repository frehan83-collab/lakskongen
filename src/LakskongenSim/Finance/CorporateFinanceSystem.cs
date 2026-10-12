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
        /// Execute annual closing: reset Profit and Loss, test covenants, process IPO quarterly guidance.
        /// Called once per year (every 4 quarters).
        /// </summary>
        public CorporateFinanceAnnualResult ExecuteAnnualClose(
            FinancialStatements statements,
            TaxSystem taxSystem,
            FinanceManager financeManager,
            InvestorSystem investorSystem,
            decimal totalBiomassKg,
            decimal normPricePerKg,
            int currentDay)
        {
            var result = new CorporateFinanceAnnualResult
            {
                Year = statements.CurrentYear,
                Day = currentDay
            };

            // 1. Compute resource rent tax base (norm price × volume - standard deductions)
            var resourceRentBase = ComputeResourceRentBase(totalBiomassKg, normPricePerKg, statements);

            // 2. Compute total tax (corporate + resource rent)
            var netIncome = statements.ComputeNetIncome();
            var totalTax = taxSystem.ComputeTotalTax(netIncome, resourceRentBase);
            result.CorporateTax = totalTax; // TaxSystem combines both
            result.NetIncomeBeforeTax = netIncome;
            result.ResourceRentBase = resourceRentBase;
            result.LossCarryForward = taxSystem.LossCarryForward;

            // 3. Pay tax from cash
            if (totalTax > 0m)
            {
                financeManager.Spend(totalTax);
                statements.RecordTax(totalTax);
                result.TaxPaid = totalTax;
            }

            // 4. Annual close Profit and Loss accounts
            statements.AnnualClose();
            result.PnLReset = true;

            // 5. Test covenants
            var netDebt = investorSystem?.OutstandingBondPrincipal ?? 0m;
            if (financeManager != null) netDebt -= financeManager.Balance; // Net debt = debt - cash
            netDebt = Math.Max(0m, netDebt);

            var ebitda = netIncome + statements.InterestExpense + statements.Taxes; // Simplified
            var interestExpense = statements.InterestExpense;
            var equity = statements.Equity;
            var totalAssets = statements.Assets;
            var cash = financeManager?.Balance ?? 0m;
            var undrawn = investorSystem?.AvailableCreditFacility ?? 0m;

            var covenantResult = Covenants.TestCovenants(netDebt, ebitda, interestExpense, equity, totalAssets, cash, undrawn);
            result.CovenantTest = covenantResult;

            if (!covenantResult.AllPassed)
            {
                result.CovenantBreachCount = Covenants.GetActiveBreaches().Count;
            }

            // 6. IPO quarterly guidance (if public)
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
        /// Compute resource rent tax base per Norwegian model:
        /// Norm price × harvested volume - standard deductions (feed, smolt, etc.)
        /// </summary>
        private decimal ComputeResourceRentBase(decimal totalBiomassKg, decimal normPricePerKg, FinancialStatements statements)
        {
            // Simplified: resource rent = (norm price × harvest volume) - operating costs allocated to harvest
            // In reality: complex calculation with standard deductions for feed, smolt, wellboat, etc.
            var harvestValue = totalBiomassKg / 1000m * normPricePerKg; // tonnes × norm price
            var standardDeductionRate = 0.65m; // 65% standard deduction for resource rent
            var deductibleCosts = statements.COGS + statements.OperatingExpenses * 0.5m; // Approximate
            var resourceRentBase = Math.Max(0m, harvestValue * (1m - standardDeductionRate) - deductibleCosts);
            return resourceRentBase;
        }

        /// <summary>
        /// Quarterly processing: advance quarter, test covenants quarterly, apply market drift to share price.
        /// </summary>
        public CorporateFinanceQuarterlyResult ExecuteQuarterly(
            FinancialStatements statements,
            TaxSystem taxSystem,
            FinanceManager financeManager,
            InvestorSystem investorSystem,
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

            // Quarterly covenant test (lightweight)
            var netDebt = investorSystem?.OutstandingBondPrincipal ?? 0m;
            if (financeManager != null) netDebt -= financeManager.Balance;
            netDebt = Math.Max(0m, netDebt);

            var ebitda = statements.ComputeNetIncome() + statements.InterestExpense + statements.Taxes;
            var interestExpense = statements.InterestExpense;
            var equity = statements.Equity;
            var totalAssets = statements.Assets;
            var cash = financeManager?.Balance ?? 0m;
            var undrawn = investorSystem?.AvailableCreditFacility ?? 0m;

            var covenantResult = Covenants.TestCovenants(netDebt, ebitda, interestExpense, equity, totalAssets, cash, undrawn);
            result.CovenantTest = covenantResult;

            // IPO market drift
            if (IPO.Stage == IPOSession.IPOStage.Listed)
            {
                IPO.ApplyMarketDrift(_rng);
                result.SharePriceNOK = IPO.SharePriceNOK;
                result.MarketCapNOK = IPO.MarketCapNOK;
            }

            return result;
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
}