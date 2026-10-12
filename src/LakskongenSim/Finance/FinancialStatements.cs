using System;
using System.Collections.Generic;
using System.Linq;

namespace LakskongenSim.Finance
{
    /// <summary>
    /// Simple Profit and Loss, Balance Sheet, and Cash Flow records for deterministic simulation.
    /// Designed to be easy to test.
    /// NOTE: Cash is NO LONGER tracked here - FinanceManager.Balance is the single source of truth.
    /// This class only tracks Profit and Loss flows for reporting purposes.
    /// Phase 4.3: Added biomass inventory tracking and period-end closing.
    /// </summary>
    public class FinancialStatements
    {
        public decimal Revenue { get; private set; }
        public decimal COGS { get; private set; }
        public decimal OperatingExpenses { get; private set; }
        public decimal InterestExpense { get; private set; }
        public decimal Taxes { get; private set; }

        /// <summary>
        /// Categorised cost breakdown (Phase 5.5 "feel" layer). Accumulated in
        /// parallel with the top-line buckets so a loss can be traced to its
        /// dominant causes (feed, delousing, insurance, interest, tax, …).
        /// Pure bookkeeping — no RNG, deterministic, round-trips on save/load.
        /// </summary>
        private readonly Dictionary<string, decimal> _costBreakdown = new(StringComparer.Ordinal);
        private readonly Dictionary<string, decimal> _revenueBreakdown = new(StringComparer.Ordinal);

        /// <summary>Read-only categorised cost totals (key = category, value = NOK).</summary>
        public IReadOnlyDictionary<string, decimal> CostBreakdown => _costBreakdown;

        /// <summary>Read-only categorised revenue totals (key = source, value = NOK).</summary>
        public IReadOnlyDictionary<string, decimal> RevenueBreakdown => _revenueBreakdown;

        [Obsolete("Use FinanceManager.Balance instead. Cash tracking moved to FinanceManager.")]
        public decimal Cash { get; private set; }
        
        public decimal Assets { get; private set; }
        public decimal Liabilities { get; private set; }
        public decimal Equity => Assets - Liabilities;

        /// <summary>Biomass inventory (live fish value) - Phase 4.3.</summary>
        public decimal BiomassInventoryValue { get; private set; }

        /// <summary>Current period (quarter) for reporting.</summary>
        public int CurrentPeriod { get; private set; } = 0;

        /// <summary>Current year (1-based).</summary>
        public int CurrentYear => (CurrentPeriod / 4) + 1;

        public List<string> Events { get; } = new List<string>();

        public const int MaxEventsRetained = 1000;

        private void AddEvent(string entry)
        {
            Events.Add(entry);
            if (Events.Count > MaxEventsRetained)
                Events.RemoveRange(0, Events.Count - MaxEventsRetained);
        }

        public FinancialStatements(decimal startingCash = 0m, decimal startingAssets = 0m, decimal startingLiabilities = 0m)
        {
            Assets = startingAssets;
            Liabilities = startingLiabilities;
        }

        /// <summary>
        /// Record revenue for Profit and Loss tracking only. Does NOT modify cash.
        /// FinanceManager.AddRevenue() handles the actual cash change.
        /// </summary>
        public void RecordRevenue(decimal amount, string category = "harvest")
        {
            Revenue += amount;
            _revenueBreakdown[category] = _revenueBreakdown.GetValueOrDefault(category) + amount;
            AddEvent($"Revenue +{amount}");
        }

        /// <summary>
        /// Record COGS for Profit and Loss tracking only. Does NOT modify cash.
        /// FinanceManager.Spend() handles the actual cash change.
        /// </summary>
        public void RecordCOGS(decimal amount, string category = "feed")
        {
            COGS += amount;
            _costBreakdown[category] = _costBreakdown.GetValueOrDefault(category) + amount;
            AddEvent($"COGS -{amount}");
        }

        /// <summary>
        /// Record operating expense for Profit and Loss tracking only. Does NOT modify cash.
        /// FinanceManager.Spend() handles the actual cash change.
        /// </summary>
        public void RecordOperatingExpense(decimal amount, string category = "operations")
        {
            OperatingExpenses += amount;
            _costBreakdown[category] = _costBreakdown.GetValueOrDefault(category) + amount;
            AddEvent($"OpEx -{amount}");
        }

        /// <summary>
        /// Record interest expense for Profit and Loss tracking only. Does NOT modify cash.
        /// FinanceManager.Spend() handles the actual cash change.
        /// </summary>
        public void RecordInterest(decimal amount)
        {
            InterestExpense += amount;
            _costBreakdown["interest"] = _costBreakdown.GetValueOrDefault("interest") + amount;
            AddEvent($"Interest -{amount}");
        }

        /// <summary>
        /// Record tax expense for Profit and Loss tracking only. Does NOT modify cash.
        /// FinanceManager.Spend() handles the actual cash change.
        /// </summary>
        public void RecordTax(decimal amount)
        {
            Taxes += amount;
            _costBreakdown["tax"] = _costBreakdown.GetValueOrDefault("tax") + amount;
            AddEvent($"Tax -{amount}");
        }

        /// <summary>Advance to next quarter. Returns true if year-end.</summary>
        public bool AdvanceQuarter()
        {
            CurrentPeriod++;
            return CurrentPeriod % 4 == 0; // true on year-end
        }

        /// <summary>
        /// Update biomass inventory valuation (Phase 4.3).
        /// Called daily from growth model with current biomass × norm price.
        /// </summary>
        public void UpdateBiomassInventory(decimal biomassKg, decimal normPricePerKg)
        {
            BiomassInventoryValue = biomassKg * normPricePerKg / 1000m; // Convert kg to tonnes
            Assets = Assets - (Assets > 0 ? BiomassInventoryValue : 0m) + BiomassInventoryValue; // Simplified: just track biomass as asset
        }

        /// <summary>Annual closing entries: reset period Profit and Loss, retain balance sheet.</summary>
        public void AnnualClose()
        {
            // Profit and Loss accounts reset at year-end
            Revenue = 0m;
            COGS = 0m;
            OperatingExpenses = 0m;
            InterestExpense = 0m;
            Taxes = 0m;
            _costBreakdown.Clear();
            _revenueBreakdown.Clear();
            AddEvent($"Year {CurrentYear} annual close");
        }

        /// <summary>Persistence (Phase 1 wave 6): the live Profit and Loss must survive
        /// a save or quarterly taxes/earnings diverge after a load.</summary>
        #pragma warning disable CS0618 // Cash mirror: save-compat field by design
        internal (decimal Revenue, decimal Cogs, decimal Opex, decimal Interest, decimal Taxes, decimal Cash, decimal Assets, decimal Liabilities, decimal BiomassInventoryValue, int CurrentPeriod, System.Collections.Generic.List<string> Events) ExportState()
            => (Revenue, COGS, OperatingExpenses, InterestExpense, Taxes, Cash, Assets, Liabilities, BiomassInventoryValue, CurrentPeriod,
                new System.Collections.Generic.List<string>(Events));
        #pragma warning restore CS0618

        internal void ImportState((decimal Revenue, decimal Cogs, decimal Opex, decimal Interest, decimal Taxes, decimal Cash, decimal Assets, decimal Liabilities, decimal BiomassInventoryValue, int CurrentPeriod, System.Collections.Generic.List<string> Events) s)
        {
#pragma warning disable CS0618 // Cash mirror: save-compat field by design
            Revenue = s.Revenue; COGS = s.Cogs; OperatingExpenses = s.Opex; InterestExpense = s.Interest;
            Taxes = s.Taxes; Cash = s.Cash; Assets = s.Assets; Liabilities = s.Liabilities;
            BiomassInventoryValue = s.BiomassInventoryValue; CurrentPeriod = s.CurrentPeriod;
            Events.Clear(); Events.AddRange(s.Events);
#pragma warning restore CS0618
        }

        /// <summary>Export the categorised cost breakdown (Phase 5.5). Ordered by key for
        /// deterministic serialization.</summary>
        internal Dictionary<string, decimal> ExportCostBreakdown()
            => _costBreakdown.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                             .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

        /// <summary>Export the categorised revenue breakdown (Phase 5.5).</summary>
        internal Dictionary<string, decimal> ExportRevenueBreakdown()
            => _revenueBreakdown.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

        /// <summary>Restore the categorised breakdowns after a load.</summary>
        internal void ImportBreakdowns(Dictionary<string, decimal>? costs, Dictionary<string, decimal>? revenues)
        {
            _costBreakdown.Clear();
            _revenueBreakdown.Clear();
            if (costs != null)
                foreach (var kv in costs) _costBreakdown[kv.Key] = kv.Value;
            if (revenues != null)
                foreach (var kv in revenues) _revenueBreakdown[kv.Key] = kv.Value;
        }

        public decimal ComputeNetIncome()
        {
            return Revenue - COGS - OperatingExpenses - InterestExpense - Taxes;
        }

        /// <summary>
        /// The dominant cost drivers, sorted descending by amount (Phase 5.5).
        /// Used to answer "why did I lose money" with the top 2–3 causes.
        /// Deterministic: ties broken by key (ordinal).
        /// </summary>
        public List<(string Category, decimal Amount)> GetTopCostDrivers(int topN = 3)
        {
            return _costBreakdown
                .Select(kv => (kv.Key, kv.Value))
                .OrderByDescending(x => x.Value)
                .ThenBy(x => x.Key, StringComparer.Ordinal)
                .Take(Math.Max(0, topN))
                .ToList();
        }

        /// <summary>
        /// The dominant revenue sources, sorted descending (Phase 5.5).
        /// </summary>
        public List<(string Category, decimal Amount)> GetTopRevenueSources(int topN = 3)
        {
            return _revenueBreakdown
                .Select(kv => (kv.Key, kv.Value))
                .OrderByDescending(x => x.Value)
                .ThenBy(x => x.Key, StringComparer.Ordinal)
                .Take(Math.Max(0, topN))
                .ToList();
        }

        /// <summary>
        /// Record asset purchase. Does NOT modify cash - caller must use FinanceManager.Spend().
        /// </summary>
        public void AddAsset(decimal amount)
        {
            Assets += amount;
            AddEvent($"Buy asset {amount}");
        }

        /// <summary>
        /// Record liability increase. Does NOT modify cash - caller must use FinanceManager.AddRevenue().
        /// </summary>
        public void AddLiability(decimal amount)
        {
            Liabilities += amount;
            AddEvent($"Take liability {amount}");
        }

        /// <summary>
        /// Record liability repayment. Does NOT modify cash - caller must use FinanceManager.Spend().
        /// </summary>
        public void RepayLiability(decimal amount)
        {
            var repayment = Math.Min(amount, Liabilities);
            Liabilities -= repayment;
            AddEvent($"Repay liability {repayment}");
        }
    }
}