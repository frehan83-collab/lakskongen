using System;
using LakskongenSim.Environment;

namespace LakskongenSim.Biology
{
    /// <summary>
    /// Water quality calculations for dissolved oxygen saturation and stocking density effects.
    /// Implements DO dynamics per Brett (1979), FAO, and stocking density effects per Oppedal et al. (2011).
    /// All calculations are deterministic and use the simulation's seeded RNG for reproducibility.
    /// </summary>
    public static class WaterQuality
    {
        #region Constants (configurable via BalancingConfig)

        /// <summary>Critical DO saturation threshold (60%) below which exponential growth reduction applies.</summary>
        public static double CriticalDOSaturationThreshold = 0.60;

        /// <summary>Exponential decay coefficient for DO saturation modifier (Brett 1979 / FAO).</summary>
        public static double DOSaturationDecayCoefficient = 0.3;

        /// <summary>Minimum growth multiplier clamp for DO saturation modifier.</summary>
        public static double DOSaturationMinMultiplier = 0.2;

        /// <summary>Maximum growth multiplier for DO saturation modifier.</summary>
        public static double DOSaturationMaxMultiplier = 1.0;

        /// <summary>Stocking density threshold (kg/m³) above which growth is reduced (Oppedal et al. 2011).</summary>
        public static double StockingDensityThresholdKgM3 = 25.0;

        /// <summary>Density reduction coefficient: growth × max(0.5, 1 − (density−25)/75).</summary>
        public static double StockingDensityReductionDenominator = 75.0;

        /// <summary>Minimum growth multiplier for stocking density modifier.</summary>
        public static double StockingDensityMinMultiplier = 0.5;

        /// <summary>Temperature correction for oxygen saturation (mg/L at 100% saturation).</summary>
        /// <remarks>Based on standard oxygen solubility tables for seawater at 35 PSU.</remarks>
        private static readonly (double Temp, double O2Sat)[] OxygenSaturationTable =
        {
            (0.0, 14.6), (2.0, 13.8), (4.0, 13.1), (6.0, 12.4),
            (8.0, 11.8), (10.0, 11.3), (12.0, 10.8), (14.0, 10.3),
            (16.0, 9.8), (18.0, 9.4), (20.0, 9.0), (22.0, 8.6), (24.0, 8.3)
        };

        /// <summary>Oxygen consumption per kg fish per day (mg O₂/kg/day) at reference temperature.</summary>
        public static double OxygenConsumptionPerKgFishPerDay = 0.1 * 1000.0; // mg O₂/kg fish/day

        /// <summary>Oxygen consumption per kg feed (mg O₂/kg feed) - includes metabolic + waste.</summary>
        public static double OxygenConsumptionPerKgFeed = 0.5 * 1000.0; // mg O₂/kg feed

        /// <summary>Water exchange rate coefficient (fraction of pen volume exchanged per day per m/s current).</summary>
        public static double WaterExchangeCoefficient = 0.05; // 5% volume exchange per day per m/s current

        #endregion

        #region DO Saturation Calculations

        /// <summary>
        /// Calculate dissolved oxygen saturation concentration (mg/L) at given temperature and salinity.
        /// Uses standard seawater oxygen solubility (Garcia and Gordon, 1992 approximation).
        /// </summary>
        /// <param name="temperatureC">Water temperature in °C.</param>
        /// <param name="salinityPSU">Salinity in PSU (default 35 for seawater).</param>
        /// <returns>Oxygen saturation concentration in mg/L.</returns>
        public static double CalculateOxygenSaturation(double temperatureC, double salinityPSU = 35.0)
        {
            // Linear interpolation from table
            for (int i = 0; i < OxygenSaturationTable.Length - 1; i++)
            {
                var (t1, o1) = OxygenSaturationTable[i];
                var (t2, o2) = OxygenSaturationTable[i + 1];
                if (temperatureC >= t1 && temperatureC <= t2)
                {
                    double t = (temperatureC - t1) / (t2 - t1);
                    double sat = o1 + t * (o2 - o1);
                    // Salinity correction (approximate: ~0.1 mg/L reduction per PSU above 0)
                    return sat * (1.0 - 0.001 * salinityPSU);
                }
            }
            // Extrapolate for out-of-range temperatures
            if (temperatureC < OxygenSaturationTable[0].Temp)
                return OxygenSaturationTable[0].O2Sat * (1.0 - 0.001 * salinityPSU);
            return OxygenSaturationTable[^1].O2Sat * (1.0 - 0.001 * salinityPSU);
        }

        /// <summary>
        /// Calculate DO saturation percentage (0.0-1.0) from current oxygen concentration.
        /// </summary>
        /// <param name="currentOxygenMgL">Current dissolved oxygen concentration (mg/L).</param>
        /// <param name="temperatureC">Water temperature in °C.</param>
        /// <param name="salinityPSU">Salinity in PSU (default 35).</param>
        /// <returns>DO saturation as fraction (0.0-1.0).</returns>
        public static double CalculateDOSaturation(double currentOxygenMgL, double temperatureC, double salinityPSU = 35.0)
        {
            double saturation = CalculateOxygenSaturation(temperatureC, salinityPSU);
            if (saturation <= 0.0) return 0.0;
            return Math.Clamp(currentOxygenMgL / saturation, 0.0, 1.0);
        }

        /// <summary>
        /// Calculate DO saturation growth modifier per Brett (1979) / FAO.
        /// Formula: growth × (1 − exp(−DO_sat/0.3)), clamped to [0.2, 1.0].
        /// </summary>
        /// <param name="doSaturation">DO saturation as fraction (0.0-1.0).</param>
        /// <returns>Growth multiplier (0.2-1.0).</returns>
        public static double CalculateDOSaturationModifier(double doSaturation)
        {
            if (doSaturation >= CriticalDOSaturationThreshold)
                return 1.0;

            // Exponential reduction: 1 - exp(-DO_sat / 0.3)
            double modifier = 1.0 - Math.Exp(-doSaturation / DOSaturationDecayCoefficient);
            return Math.Clamp(modifier, DOSaturationMinMultiplier, DOSaturationMaxMultiplier);
        }

        #endregion

        #region Stocking Density Calculations

        /// <summary>
        /// Calculate stocking density growth modifier per Oppedal et al. (2011).
        /// Formula: growth × max(0.5, 1 − (density−25)/75) for density > 25 kg/m³.
        /// </summary>
        /// <param name="stockingDensityKgM3">Current stocking density in kg/m³.</param>
        /// <returns>Growth multiplier (0.5-1.0).</returns>
        public static double CalculateStockingDensityModifier(double stockingDensityKgM3)
        {
            if (stockingDensityKgM3 <= StockingDensityThresholdKgM3)
                return 1.0;

            double modifier = 1.0 - (stockingDensityKgM3 - StockingDensityThresholdKgM3) / StockingDensityReductionDenominator;
            return Math.Clamp(modifier, StockingDensityMinMultiplier, 1.0);
        }

        #endregion

        #region DO Dynamics (Daily Update)

        /// <summary>
        /// Calculate daily DO dynamics for a pen.
        /// DO_sat = temp_correction(T) − O₂_consumption(biomass, feed_rate, FCR) + water_exchange(pen_volume, current_speed)
        /// Updates the WaterChemistry.OxygenO2 in place.
        /// Note: Consumption rates are in mg/L/day (consistent with WaterChemistry units).
        /// </summary>
        /// <param name="waterChemistry">Water chemistry state to update.</param>
        /// <param name="temperatureC">Water temperature (°C).</param>
        /// <param name="biomassKg">Fish biomass in pen (kg).</param>
        /// <param name="feedRateKgPerDay">Daily feed rate (kg/day).</param>
        /// <param name="fcr">Feed conversion ratio.</param>
        /// <param name="penVolumeM3">Pen volume (m³).</param>
        /// <param name="currentSpeedMs">Current speed (m/s).</param>
        /// <param name="salinityPSU">Salinity (PSU).</param>
        public static void UpdateDailyDO(
            WaterChemistry waterChemistry,
            double temperatureC,
            double biomassKg,
            double feedRateKgPerDay,
            double fcr,
            double penVolumeM3,
            double currentSpeedMs,
            double salinityPSU = 35.0)
        {
            // Temperature correction: oxygen solubility at saturation
            double o2Saturation = CalculateOxygenSaturation(temperatureC, salinityPSU);

            // O₂ consumption by fish (metabolic) - mg O₂/L/day
            // Rate is per kg fish per day, consistent with WaterChemistry scaling
            double tempFactor = Math.Pow(2.0, (temperatureC - 10.0) / 10.0);
            double fishOxygenConsumptionMgPerLPerDay = biomassKg * OxygenConsumptionPerKgFishPerDay * tempFactor;

            // O₂ consumption from feed waste (uneaten feed + feces)
            double feedOxygenDemandMgPerLPerDay = feedRateKgPerDay * fcr * OxygenConsumptionPerKgFeed;

            // Total oxygen consumption (mg/L/day)
            double totalOxygenConsumptionMgPerLPerDay = fishOxygenConsumptionMgPerLPerDay + feedOxygenDemandMgPerLPerDay;

            // Water exchange: reaeration toward saturation
            // Match original WaterChemistry scaling: base flow + current-dependent flow
            // Original: waterFlowRate = 500 + currentSpeed * 3000 m³/day
            // Exchange fraction = waterFlowRate * 0.01 (applied directly to concentration)
            double waterFlowRate = 500.0 + currentSpeedMs * 3000.0; // m³/day
            double exchangeRate = waterFlowRate * 0.01; // Fraction per day (original scaling)
            
            double reaerationMgPerLPerDay = (o2Saturation - waterChemistry.OxygenO2) * exchangeRate;

            // Net oxygen change
            double netOxygenChange = reaerationMgPerLPerDay - totalOxygenConsumptionMgPerLPerDay;

            // Update oxygen concentration
            waterChemistry.OxygenO2 += netOxygenChange;

            // Clamp to realistic bounds (0 to saturation)
            waterChemistry.OxygenO2 = Math.Clamp(waterChemistry.OxygenO2, 0.0, o2Saturation * 1.2); // Allow slight supersaturation
        }

        /// <summary>
        /// Calculate combined environmental growth multiplier from DO and density.
        /// </summary>
        /// <param name="waterChemistry">Water chemistry state.</param>
        /// <param name="temperatureC">Water temperature (°C).</param>
        /// <param name="stockingDensityKgM3">Stocking density (kg/m³).</param>
        /// <param name="salinityPSU">Salinity (PSU).</param>
        /// <returns>Combined growth multiplier (0.1-1.0).</returns>
        public static double CalculateCombinedEnvironmentalMultiplier(
            WaterChemistry waterChemistry,
            double temperatureC,
            double stockingDensityKgM3,
            double salinityPSU = 35.0)
        {
            double doSaturation = CalculateDOSaturation(waterChemistry.OxygenO2, temperatureC, salinityPSU);
            double doModifier = CalculateDOSaturationModifier(doSaturation);
            double densityModifier = CalculateStockingDensityModifier(stockingDensityKgM3);

            return doModifier * densityModifier;
        }

        #endregion

        #region Configuration

        /// <summary>
        /// Apply configuration from BalancingConfig.
        /// </summary>
        public static void ApplyConfig(BalancingConfig config)
        {
            if (config == null) return;

            CriticalDOSaturationThreshold = config.CriticalDOSaturationThreshold;
            DOSaturationDecayCoefficient = config.DOSaturationDecayCoefficient;
            DOSaturationMinMultiplier = config.DOSaturationMinMultiplier;
            DOSaturationMaxMultiplier = config.DOSaturationMaxMultiplier;
            StockingDensityThresholdKgM3 = config.StockingDensityThresholdKgM3;
            StockingDensityReductionDenominator = config.StockingDensityReductionDenominator;
            StockingDensityMinMultiplier = config.StockingDensityMinMultiplier;
            OxygenConsumptionPerKgFishPerDay = config.OxygenConsumptionPerKgFishPerDay;
            OxygenConsumptionPerKgFeed = config.OxygenConsumptionPerKgFeed;
            WaterExchangeCoefficient = config.WaterExchangeCoefficient;
        }

        #endregion
    }

    /// <summary>
    /// Configuration extension for BalancingConfig to include water quality parameters.
    /// </summary>
    public partial class BalancingConfig
    {
        // DO Saturation parameters
        public double CriticalDOSaturationThreshold { get; set; } = 0.60;
        public double DOSaturationDecayCoefficient { get; set; } = 0.3;
        public double DOSaturationMinMultiplier { get; set; } = 0.2;
        public double DOSaturationMaxMultiplier { get; set; } = 1.0;

        // Stocking density parameters
        public double StockingDensityThresholdKgM3 { get; set; } = 25.0;
        public double StockingDensityReductionDenominator { get; set; } = 75.0;
        public double StockingDensityMinMultiplier { get; set; } = 0.5;

        // DO Dynamics parameters
        public double OxygenConsumptionPerKgFishPerDay { get; set; } = 100.0; // mg O₂/kg fish/day
        public double OxygenConsumptionPerKgFeed { get; set; } = 500.0; // mg O₂/kg feed
        public double WaterExchangeCoefficient { get; set; } = 0.05; // fraction/day per m/s current
    }
}