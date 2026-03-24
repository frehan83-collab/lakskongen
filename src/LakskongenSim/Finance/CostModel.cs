using System;
using System.Collections.Generic;

namespace LakskongenSim.Finance
{
    /// <summary>
    /// CAPEX and OPEX cost model for salmon farming operations.
    /// </summary>
    public class CostModel
    {
        // License costs per MAB tonne (Norwegian average)
        private const double LicenseCostPerMabTonneMin = 100_000_000; // 100M NOK
        private const double LicenseCostPerMabTonneMax = 200_000_000; // 200M NOK

        /// <summary>
        /// Calculate total CAPEX for a new farm setup.
        /// </summary>
        public double CalculateCapex(int penCount, double mabTonnes, bool includesLicense = true)
        {
            double licenseCost = includesLicense ? mabTonnes * 150_000_000 : 0; // 150M avg
            double penCost = penCount * 8_000_000; // 8M NOK per pen (cage + net + mooring)
            double feedBarge = 25_000_000; // Feed barge
            double anchorSystem = penCount * 2_000_000;
            double monitoring = 5_000_000; // Cameras, sensors, software
            double other = 10_000_000; // Permits, planning, misc

            return licenseCost + penCost + feedBarge + anchorSystem + monitoring + other;
        }

        /// <summary>
        /// Calculate annual OPEX.
        /// </summary>
        public double CalculateAnnualOpex(
            string zoneId,
            double totalBiomassKg,
            double fcr,
            int employeeCount = 8,
            double avgSalaryNOK = 650_000)
        {
            var engine = new EconomicEngine();
            double feedCostPerKg = engine.GetFeedCostPerZone(zoneId);
            double electricityCost = engine.GetElectricityCostPerZone(zoneId);

            double feedCost = totalBiomassKg * fcr * feedCostPerKg;
            double energy = totalBiomassKg * 0.5 * electricityCost; // ~0.5 kWh per kg produced
            double labor = employeeCount * avgSalaryNOK;
            double treatments = totalBiomassKg * 2.50; // ~2.50 NOK/kg for treatments
            double insurance = totalBiomassKg * 1.50; // ~1.50 NOK/kg
            double maintenance = 5_000_000; // Annual maintenance
            double transport = totalBiomassKg * 1.00; // ~1.00 NOK/kg transport

            return feedCost + energy + labor + treatments + insurance + maintenance + transport;
        }

        /// <summary>
        /// Calculate OPEX per kg of fish produced.
        /// </summary>
        public double CalculateOpexPerKg(string zoneId, double totalBiomassKg, double fcr)
        {
            double totalOpex = CalculateAnnualOpex(zoneId, totalBiomassKg, fcr);
            return totalBiomassKg > 0 ? totalOpex / totalBiomassKg : 0;
        }
    }
}
