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

        // Oxygen solubility in water at 1 atm, mg/L: Benson & Krause (1984),
        // Limnol. Oceanogr. 29(3):620-632, in the form of APHA Standard Methods
        // 4500-O (the USGS DOTABLES equation):
        //   ln C* = A0 + A1/T + A2/T² + A3/T³ + A4/T⁴ − S·(B0 − B1/T + B2/T²),
        // T in kelvin, S in PSU. Within 0.01 mg/L of Garcia & Gordon (1992) over
        // 0-30 °C, 0-40 PSU. Seawater (35 PSU) at 10 °C holds 9.02 mg/L. The old
        // table was the freshwater curve (11.3 mg/L at 10 °C) times a linear
        // 0.1%/PSU factor (10.9 mg/L), about 21% too high at sea (S1-01).
        private const double KelvinOffset = 273.15;
        private const double SolubilityA0 = -139.34411;
        private const double SolubilityA1 = 1.575701e5;
        private const double SolubilityA2 = -6.642308e7;
        private const double SolubilityA3 = 1.243800e10;
        private const double SolubilityA4 = -8.621949e11;
        private const double SalinityB0 = 1.7674e-2;
        private const double SalinityB1 = 1.0754e1;
        private const double SalinityB2 = 2.1407e3;
        /// <summary>Validity range of the solubility fit; inputs are clamped to it.</summary>
        private const double SolubilityMinTempC = 0.0;
        private const double SolubilityMaxTempC = 40.0;
        private const double SolubilityMaxSalinityPSU = 42.0;

        /// <summary>Oxygen consumption per kg fish per day (mg O₂/kg/day) at reference temperature.</summary>
        public static double OxygenConsumptionPerKgFishPerDay = 0.1 * 1000.0; // mg O₂/kg fish/day

        /// <summary>Oxygen consumption per kg feed (mg O₂/kg feed) - includes metabolic + waste.</summary>
        public static double OxygenConsumptionPerKgFeed = 0.5 * 1000.0; // mg O₂/kg feed

        /// <summary>Water exchange rate coefficient (fraction of pen volume exchanged per day per m/s current).</summary>
        public static double WaterExchangeCoefficient = 0.05; // 5% volume exchange per day per m/s current

        #endregion

        #region DO Saturation Calculations

        /// <summary>
        /// Dissolved oxygen concentration (mg/L) at 100% saturation, 1 atm, for the
        /// given temperature and salinity: Benson &amp; Krause (1984) as given in
        /// APHA Standard Methods 4500-O (equal to Garcia &amp; Gordon 1992 within
        /// 0.01 mg/L). Temperature is clamped to 0-40 °C and salinity to 0-42 PSU.
        /// </summary>
        /// <param name="temperatureC">Water temperature in °C.</param>
        /// <param name="salinityPSU">Salinity in PSU (default 35 for seawater).</param>
        /// <returns>Oxygen saturation concentration in mg/L.</returns>
        public static double CalculateOxygenSaturation(double temperatureC, double salinityPSU = 35.0)
        {
            double t = double.IsFinite(temperatureC)
                ? Math.Clamp(temperatureC, SolubilityMinTempC, SolubilityMaxTempC)
                : SolubilityMinTempC;
            double s = double.IsFinite(salinityPSU) ? Math.Clamp(salinityPSU, 0.0, SolubilityMaxSalinityPSU) : 0.0;
            double k = t + KelvinOffset;
            double lnFresh = SolubilityA0 + SolubilityA1 / k + SolubilityA2 / (k * k)
                + SolubilityA3 / (k * k * k) + SolubilityA4 / (k * k * k * k);
            double salting = s * (SalinityB0 - SalinityB1 / k + SalinityB2 / (k * k));
            return Math.Exp(lnFresh - salting);
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
        /// Calculate daily DO dynamics for a pen and update
        /// <c>WaterChemistry.OxygenO2</c> in place. Fish and feed oxygen demand are
        /// normalized by the pen volume (mg/day ÷ liters → mg/L/day); current-driven
        /// exchange pulls toward saturation. Integrated exactly over the day like
        /// <see cref="Environment.WaterChemistry.UpdateOneDay"/>: the old explicit
        /// step applied an exchange rate of 6.5-20/day as a fraction and its clamp
        /// hid the divergence (F1024).
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
            double o2Saturation = CalculateOxygenSaturation(temperatureC, salinityPSU);
            double volumeL = Math.Max(1.0, penVolumeM3 * 1000.0);

            // Fish (metabolic) and feed (metabolic + waste) oxygen demand, mg/L/day.
            double tempFactor = Math.Pow(2.0, (temperatureC - 10.0) / 10.0);
            double demand = (biomassKg * OxygenConsumptionPerKgFishPerDay * tempFactor
                + feedRateKgPerDay * fcr * OxygenConsumptionPerKgFeed) / volumeL;

            // Same flow index and exchange scaling as the zone chemistry.
            double waterFlowRate = 500.0 + currentSpeedMs * 3000.0;
            double exchange = WaterChemistry.ExchangeRatePerDay(waterFlowRate);

            waterChemistry.OxygenO2 = WaterChemistry.RelaxOneDay(
                waterChemistry.OxygenO2, exchange * o2Saturation - demand, exchange);

            // Physical bounds: anoxia floor, slight supersaturation ceiling.
            waterChemistry.OxygenO2 = Math.Clamp(waterChemistry.OxygenO2, 0.0, o2Saturation * 1.2);
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