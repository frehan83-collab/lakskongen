using System;

namespace LakskongenSim.Finance
{
    /// <summary>
    /// Very simple loan system.
    /// </summary>
    public class LoanSystem
    {
        public decimal InterestRateAnnual = 0.05m;

        public event Action<decimal>? OnInterestCharged;

        public decimal ChargeInterest(decimal principal, int days)
        {
            var fraction = (decimal)days / 365m;
            var interest = principal * InterestRateAnnual * fraction;
            OnInterestCharged?.Invoke(interest);
            return interest;
        }
    }
}