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
        /// F2-04: depreciation of F2 units this period (docs/FARM_TYPES_SPEC.md
        /// §3.6), a non-cash P&amp;L line: part of the corporate tax base
        /// (<see cref="PreTaxIncome"/>), never part of the cash reconciliation
        /// (<see cref="LifetimeNetCashFlow"/>) or of the resource-rent base. It
        /// is also the cost line <see cref="DepreciationCategory"/>, which is how
        /// a save carries it (the cost breakdown is saved).
        /// </summary>
        public decimal Depreciation { get; private set; }

        /// <summary>Cost-breakdown category of <see cref="Depreciation"/>.</summary>
        public const string DepreciationCategory = "facility_depreciation";

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

        // ── Lifetime totals (never reset at the annual close; saved) ──────────
        // Every cash movement is booked to exactly one of these, which gives the
        // reconciliation identity the economy tests assert (review F1132):
        //   Δcash = LifetimeRevenue − LifetimeCOGS − LifetimeOperatingExpenses
        //         − LifetimeInterest − LifetimeTaxes − LifetimeCapex
        //         + LifetimeLiabilitiesIncurred − LifetimeLiabilitiesRepaid
        //         + LifetimeEquityRaised
        public decimal LifetimeRevenue { get; private set; }
        public decimal LifetimeCOGS { get; private set; }
        public decimal LifetimeOperatingExpenses { get; private set; }
        public decimal LifetimeInterest { get; private set; }
        /// <summary>Net tax paid (provisional and final payments minus refunds).</summary>
        public decimal LifetimeTaxes { get; private set; }
        public decimal LifetimeCapex { get; private set; }
        public decimal LifetimeLiabilitiesIncurred { get; private set; }
        public decimal LifetimeLiabilitiesRepaid { get; private set; }
        public decimal LifetimeEquityRaised { get; private set; }
        /// <summary>F2-04: depreciation of F2 units since the game started (non-cash: not in <see cref="LifetimeNetCashFlow"/>).</summary>
        public decimal LifetimeDepreciation { get; private set; }

        /// <summary>Net cash effect of every booked flow since the game started.</summary>
        public decimal LifetimeNetCashFlow =>
            LifetimeRevenue - LifetimeCOGS - LifetimeOperatingExpenses - LifetimeInterest - LifetimeTaxes
            - LifetimeCapex + LifetimeLiabilitiesIncurred - LifetimeLiabilitiesRepaid + LifetimeEquityRaised;

        /// <summary>
        /// Pre-tax income for the current period (excludes tax already booked).
        /// F2-04: less the period's facility depreciation (zero in a game with no F2 unit).
        /// </summary>
        public decimal PreTaxIncome => Revenue - COGS - OperatingExpenses - InterestExpense - Depreciation;

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
            LifetimeRevenue += amount;
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
            LifetimeCOGS += amount;
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
            LifetimeOperatingExpenses += amount;
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
            LifetimeInterest += amount;
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
            LifetimeTaxes += amount;
            _costBreakdown["tax"] = _costBreakdown.GetValueOrDefault("tax") + amount;
            AddEvent($"Tax -{amount}");
        }

        /// <summary>
        /// F2-04: record a day's depreciation of F2 units. Non-cash: it lowers
        /// pre-tax income and the book value of the assets, moves no cash and no
        /// lifetime cash total. A zero amount books nothing (no empty cost line).
        /// </summary>
        public void RecordDepreciation(decimal amount)
        {
            if (amount <= 0m) return;
            Depreciation += amount;
            LifetimeDepreciation += amount;
            Assets -= amount;
            _costBreakdown[DepreciationCategory] = _costBreakdown.GetValueOrDefault(DepreciationCategory) + amount;
            AddEvent($"Depreciation -{amount}");
        }

        /// <summary>
        /// Record a tax refund (provisional tax paid above the liability). Reduces
        /// booked tax. Does NOT modify cash - FinanceManager.AddRevenue() returns it.
        /// </summary>
        public void RecordTaxRefund(decimal amount)
        {
            Taxes -= amount;
            LifetimeTaxes -= amount;
            _costBreakdown["tax"] = _costBreakdown.GetValueOrDefault("tax") - amount;
            AddEvent($"Tax refund +{amount}");
        }

        /// <summary>
        /// Record new equity (share issue). Does NOT modify cash - caller must use
        /// FinanceManager.AddRevenue().
        /// </summary>
        public void RecordEquityRaised(decimal amount)
        {
            LifetimeEquityRaised += amount;
            AddEvent($"Equity raised {amount}");
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
            Depreciation = 0m;
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

        /// <summary>Lifetime totals for a save (see the reconciliation identity above).</summary>
        public LifetimeTotals ExportLifetime() => new()
        {
            Revenue = LifetimeRevenue, Cogs = LifetimeCOGS, Opex = LifetimeOperatingExpenses,
            Interest = LifetimeInterest, Taxes = LifetimeTaxes, Capex = LifetimeCapex,
            LiabilitiesIncurred = LifetimeLiabilitiesIncurred, LiabilitiesRepaid = LifetimeLiabilitiesRepaid,
            EquityRaised = LifetimeEquityRaised, Depreciation = LifetimeDepreciation
        };

        /// <summary>Restore lifetime totals after a load.</summary>
        public void ImportLifetime(LifetimeTotals t)
        {
            ArgumentNullException.ThrowIfNull(t);
            LifetimeRevenue = t.Revenue; LifetimeCOGS = t.Cogs; LifetimeOperatingExpenses = t.Opex;
            LifetimeInterest = t.Interest; LifetimeTaxes = t.Taxes; LifetimeCapex = t.Capex;
            LifetimeLiabilitiesIncurred = t.LiabilitiesIncurred; LifetimeLiabilitiesRepaid = t.LiabilitiesRepaid;
            LifetimeEquityRaised = t.EquityRaised;
            LifetimeDepreciation = t.Depreciation;
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
            // F2-04: the period's depreciation travels as its cost line.
            Depreciation = _costBreakdown.GetValueOrDefault(DepreciationCategory);
        }

        public decimal ComputeNetIncome()
        {
            return Revenue - COGS - OperatingExpenses - InterestExpense - Depreciation - Taxes;
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
            LifetimeCapex += amount;
            AddEvent($"Buy asset {amount}");
        }

        /// <summary>
        /// Record liability increase. Does NOT modify cash - caller must use FinanceManager.AddRevenue().
        /// </summary>
        public void AddLiability(decimal amount)
        {
            Liabilities += amount;
            LifetimeLiabilitiesIncurred += amount;
            AddEvent($"Take liability {amount}");
        }

        /// <summary>
        /// Record liability repayment. Does NOT modify cash - caller must use FinanceManager.Spend().
        /// </summary>
        public void RepayLiability(decimal amount)
        {
            var repayment = Math.Min(amount, Liabilities);
            Liabilities -= repayment;
            // The cash paid is the full amount even if older saves under-recorded
            // the liability, so the lifetime (cash) total takes all of it.
            LifetimeLiabilitiesRepaid += amount;
            AddEvent($"Repay liability {repayment}");
        }
    }

    /// <summary>Serializable lifetime ledger totals (EngineRuntimeSnapshot.FinLifetime).</summary>
    public sealed class LifetimeTotals
    {
        public decimal Revenue { get; set; }
        public decimal Cogs { get; set; }
        public decimal Opex { get; set; }
        public decimal Interest { get; set; }
        public decimal Taxes { get; set; }
        public decimal Capex { get; set; }
        public decimal LiabilitiesIncurred { get; set; }
        public decimal LiabilitiesRepaid { get; set; }
        public decimal EquityRaised { get; set; }
        /// <summary>F2-04: facility depreciation (non-cash; outside the reconciliation identity).</summary>
        public decimal Depreciation { get; set; }
    }
}