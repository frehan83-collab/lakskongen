using System;

namespace LakskongenSim.Finance
{
    /// <summary>
    /// Simple investor mechanics: issue bonds (debt) and equity (dilution).
    /// Tracks outstanding equity shares and bond principal.
    /// </summary>
    public class InvestorSystem
    {
        public decimal OutstandingBondPrincipal { get; private set; }
        public decimal OutstandingShares { get; private set; } = 1_000_000m; // share count
        public decimal SharePrice { get; private set; } = 10m;
        /// <summary>Available undrawn credit facility (revolving credit facility).</summary>
        public decimal AvailableCreditFacility { get; private set; } = 200_000_000m; // Default 200M NOK facility

        public event Action<decimal, string>? OnIssued; // amount, type

        public void IssueBond(decimal principal)
        {
            if (principal <= 0) return;
            OutstandingBondPrincipal += principal;
            OnIssued?.Invoke(principal, "bond");
        }

        public decimal IssueEquity(decimal cashRaised, decimal pricePerShare)
        {
            if (cashRaised <= 0 || pricePerShare <= 0) return 0m;
            var shares = cashRaised / pricePerShare;
            OutstandingShares += shares;
            SharePrice = pricePerShare; // simple model: last issuance price
            OnIssued?.Invoke(cashRaised, "equity");
            return shares;
        }

        public decimal DilutionFraction()
        {
            // For simplicity, assume original base was 1_000_000 shares
            return Math.Max(0m, OutstandingShares - 1_000_000m) / OutstandingShares;
        }

        /// <summary>
        /// Exact state restore from save (no cash side effects, no events).
        /// Per-investor records are not tracked by this system; the save DTO
        /// keeps the list slot for a future cap-table implementation.
        /// </summary>
        public void RestoreState(decimal outstandingShares, decimal outstandingBondPrincipal)
        {
            if (outstandingShares > 0) OutstandingShares = outstandingShares;
            if (outstandingBondPrincipal >= 0) OutstandingBondPrincipal = outstandingBondPrincipal;
        }
    }
}
