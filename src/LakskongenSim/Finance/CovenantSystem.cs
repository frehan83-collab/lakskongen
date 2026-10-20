using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Core;

namespace LakskongenSim.Finance
{
    /// <summary>
    /// Phase 4.3: Loan covenants and compliance monitoring.
    /// Typical Norwegian aquaculture covenants:
    /// - Leverage ratio (Net Debt / EBITDA) ≤ 4.0x
    /// - Interest coverage (EBITDA / Interest) ≥ 3.0x
    /// - Equity ratio (Equity / Total Assets) ≥ 30%
    /// - Minimum liquidity (Cash + undrawn facilities) ≥ 50M NOK
    /// </summary>
    public class CovenantSystem
    {
        public const decimal MaxLeverageRatio = 4.0m;
        public const decimal MinInterestCoverage = 3.0m;
        public const decimal MinEquityRatio = 0.30m;
        public const decimal MinLiquidityNOK = 50_000_000m;

        public event Action<string, bool, decimal>? OnCovenantTested; // name, passed, value

        private readonly List<CovenantBreach> _breaches = new();

        public IReadOnlyList<CovenantBreach> Breaches => _breaches;

        /// <summary>Test all covenants against current financials.</summary>
        public CovenantTestResult TestCovenants(
            decimal netDebt,
            decimal ebitda,
            decimal interestExpense,
            decimal equity,
            decimal totalAssets,
            decimal cash,
            decimal undrawnFacilities)
        {
            var result = new CovenantTestResult();

            // Leverage ratio: Net Debt / EBITDA
            var leverage = ebitda > 0m ? netDebt / ebitda : decimal.MaxValue;
            var leveragePassed = leverage <= MaxLeverageRatio;
            result.LeverageRatio = leverage;
            result.LeveragePassed = leveragePassed;
            TestAndRecord("Leverage Ratio (Net Debt/EBITDA ≤ 4.0x)", leveragePassed, leverage);

            // Interest coverage: EBITDA / Interest
            var coverage = interestExpense > 0m ? ebitda / interestExpense : decimal.MaxValue;
            var coveragePassed = coverage >= MinInterestCoverage;
            result.InterestCoverage = coverage;
            result.InterestCoveragePassed = coveragePassed;
            TestAndRecord("Interest Coverage (EBITDA/Interest ≥ 3.0x)", coveragePassed, coverage);

            // Equity ratio: Equity / Total Assets
            var equityRatio = totalAssets > 0m ? equity / totalAssets : 0m;
            var equityPassed = equityRatio >= MinEquityRatio;
            result.EquityRatio = equityRatio;
            result.EquityPassed = equityPassed;
            TestAndRecord("Equity Ratio (Equity/Assets ≥ 30%)", equityPassed, equityRatio);

            // Liquidity: Cash + undrawn facilities
            var liquidity = cash + undrawnFacilities;
            var liquidityPassed = liquidity >= MinLiquidityNOK;
            result.LiquidityNOK = liquidity;
            result.LiquidityPassed = liquidityPassed;
            TestAndRecord($"Liquidity (Cash+Undrawn ≥ {MinLiquidityNOK:N0} NOK)", liquidityPassed, liquidity);

            result.AllPassed = leveragePassed && coveragePassed && equityPassed && liquidityPassed;
            return result;
        }

        private void TestAndRecord(string name, bool passed, decimal value)
        {
            OnCovenantTested?.Invoke(name, passed, value);
            if (!passed)
            {
                _breaches.Add(new CovenantBreach
                {
                    CovenantName = name,
                    BreachValue = value,
                    BreachDay = 0, // Set by caller
                    IsResolved = false
                });
            }
        }

        /// <summary>Mark a breach as resolved (e.g., after cure period).</summary>
        public void ResolveBreach(string covenantName)
        {
            var breach = _breaches.Find(b => b.CovenantName == covenantName && !b.IsResolved);
            if (breach != null) breach.IsResolved = true;
        }

        /// <summary>Get unresolved breaches.</summary>
        public IReadOnlyList<CovenantBreach> GetActiveBreaches()
            => _breaches.Where(b => !b.IsResolved).ToList();

        public void Reset()
        {
            _breaches.Clear();
        }

        public List<CovenantBreachState> ExportState()
            => _breaches.Select(b => new CovenantBreachState
            {
                CovenantId = b.CovenantName,  // Use name as ID
                CovenantName = b.CovenantName,
                BreachValue = b.BreachValue,
                BreachDay = b.BreachDay,
                IsResolved = b.IsResolved
            }).ToList();

        internal void ImportState(IEnumerable<CovenantBreachState> states)
        {
            _breaches.Clear();
            foreach (var s in states)
                _breaches.Add(new CovenantBreach
                {
                    CovenantName = s.CovenantId,  // Use ID as name
                    BreachValue = s.BreachValue,
                    BreachDay = s.BreachDay,
                    IsResolved = s.IsResolved
                });
        }
    }

    public class CovenantTestResult
    {
        public decimal LeverageRatio { get; set; }
        public bool LeveragePassed { get; set; }
        public decimal InterestCoverage { get; set; }
        public bool InterestCoveragePassed { get; set; }
        public decimal EquityRatio { get; set; }
        public bool EquityPassed { get; set; }
        public decimal LiquidityNOK { get; set; }
        public bool LiquidityPassed { get; set; }
        public bool AllPassed { get; set; }
    }

    public class CovenantBreach
    {
        public string CovenantName { get; set; } = string.Empty;
        public decimal BreachValue { get; set; }
        public int BreachDay { get; set; }
        public bool IsResolved { get; set; }
    }

    // Serializable covenant breach record uses Core.GameState.CovenantBreachState
}