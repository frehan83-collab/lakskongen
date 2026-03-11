using System;

namespace LakskongenSim.Finance
{
    /// <summary>
    /// Management of licenses for sites; charges fees.
    /// </summary>
    public class LicenseSystem
    {
        public decimal AnnualFeePerSite = 50000m;

        public event Action<string, decimal>? OnLicenseCharged; // siteId, amount

        public void ChargeAnnual(string siteId)
        {
            OnLicenseCharged?.Invoke(siteId, AnnualFeePerSite);
        }
    }
}