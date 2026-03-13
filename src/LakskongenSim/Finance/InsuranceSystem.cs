using System;

namespace LakskongenSim.Finance
{
    /// <summary>
    /// Insurance model: premium based on insured value and risk factor.
    /// Can pay claims which reduce cash and reduce insured value if total loss.
    /// </summary>
    public class InsuranceSystem
    {
        public decimal InsuredValue { get; private set; }
        public decimal AnnualPremiumRate { get; private set; } = 0.01m; // 1% default

#pragma warning disable CS0067
        public event Action<decimal>? OnPremiumPaid;
#pragma warning restore CS0067
        public event Action<decimal>? OnClaimPaid;

        public void Insure(decimal value, decimal annualRate)
        {
            InsuredValue = value;
            AnnualPremiumRate = annualRate;
        }

        public decimal ComputePremium(int days)
        {
            var fraction = (decimal)days / 365m;
            return InsuredValue * AnnualPremiumRate * fraction;
        }

        public decimal PayClaim(decimal claimAmount, bool totalLoss = false)
        {
            var payout = Math.Min(claimAmount, InsuredValue);
            if (totalLoss) InsuredValue = 0m;
            OnClaimPaid?.Invoke(payout);
            return payout;
        }
    }
}
