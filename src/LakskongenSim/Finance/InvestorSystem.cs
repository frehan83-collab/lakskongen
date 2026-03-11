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
    }
}
