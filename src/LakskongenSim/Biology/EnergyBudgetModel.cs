using System;

namespace LakskongenSim.Biology
{
    /// <summary>
    /// Implements a bioenergetic energy budget model for individual salmon.
    /// 
    /// The energy budget follows the equation:
    /// Energy Budget = Intake - Maintenance - Activity - Waste
    /// 
    /// Where:
    /// - Maintenance cost scales with W^0.8 (allometric scaling of standard metabolic rate)
    /// - Activity cost = 1.3 × maintenance (typical swimming activity)
    /// - Waste = 0.15 × intake (fecal and urinary losses)
    /// - All metabolic rates are temperature-corrected using Q10 = 2.0 (doubling per 10°C increase)
    /// - Baseline temperature is 11°C (typical Norwegian coastal water temperature)
    /// 
    /// Returns net energy available for growth in kJ/day, which can be converted to weight gain.
    /// </summary>
    public static class EnergyBudgetModel
    {
        #region Constants (public static readonly for modding)

        /// <summary>
        /// Baseline temperature for Q10 temperature correction (°C).
        /// Standard reference temperature for metabolic rate calculations.
        /// </summary>
        public static readonly double BaselineTemperature = 11.0;

        /// <summary>
        /// Q10 temperature coefficient.
        /// Represents the factor by which metabolic rates increase for every 10°C temperature rise.
        /// Q10 = 2.0 means rate doubles per 10°C increase.
        /// </summary>
        public static readonly double Q10 = 2.0;

        /// <summary>
        /// Allometric exponent for standard metabolic rate scaling.
        /// Metabolic rate scales with body weight to the power of 0.8 (Kleiber's law variant).
        /// </summary>
        public static readonly double MaintenanceExponent = 0.8;

        /// <summary>
        /// Maintenance coefficient (kJ/kg^0.8/day at baseline temperature).
        /// Represents the baseline standard metabolic rate for a 1 kg salmon.
        /// Tunable parameter based on empirical data.
        /// </summary>
        public static readonly double MaintenanceCoefficient = 50.0;

        /// <summary>
        /// Activity cost multiplier relative to maintenance.
        /// Typical swimming activity costs 1.3 × baseline maintenance rate.
        /// </summary>
        public static readonly double ActivityMultiplier = 1.3;

        /// <summary>
        /// Waste fraction of intake energy (0.15 = 15%).
        /// Represents losses through feces and urine (uneaten feed + metabolic waste).
        /// </summary>
        public static readonly double WasteFraction = 0.15;

        /// <summary>
        /// Energy density of salmon tissue (8.0 kJ/g = 8,000 kJ/kg).
        /// Energy required to synthesize 1 kg of new biomass.
        /// Typical value for fish muscle tissue including protein and lipid.
        /// </summary>
        public static readonly double EnergyDensityKJPerGram = 8.0;

        /// <summary>
        /// Conversion factor from grams to kilograms.
        /// </summary>
        public static readonly double GramsPerKilogram = 1000.0;

        /// <summary>
        /// Energy density of typical salmon feed (20 kJ/g = 20,000 kJ/kg).
        /// Represents caloric value of commercial aquaculture feed.
        /// </summary>
        public static readonly double FeedEnergyDensityKJPerGram = 20.0;

        #endregion

        #region Public Methods

        /// <summary>
        /// Calculates the temperature correction factor using the Q10 model.
        /// 
        /// TemperatureCorrection = Q10^((T - T0) / 10)
        /// 
        /// </summary>
        /// <param name="temperatureC">Current water temperature in °C.</param>
        /// <returns>
        /// Correction factor: 
        /// - = 1.0 at baseline temperature (11°C)
        /// - &gt; 1.0 at temperatures above baseline
        /// - &lt; 1.0 at temperatures below baseline
        /// </returns>
        public static double CalculateTemperatureCorrection(double temperatureC)
        {
            return Math.Pow(Q10, (temperatureC - BaselineTemperature) / 10.0);
        }

        /// <summary>
        /// Calculates the standard metabolic rate (maintenance energy) for a fish.
        /// 
        /// Maintenance = Coefficient × W^0.8 × Q10_correction
        /// 
        /// This represents the energy required for basic bodily functions
        /// (respiration, circulation, osmoregulation, cell maintenance)
        /// at zero activity level.
        /// </summary>
        /// <param name="weightKg">Fish body weight in kilograms.</param>
        /// <param name="temperatureC">Water temperature in °C.</param>
        /// <returns>
        /// Maintenance energy requirement in kJ/day.
        /// </returns>
        public static double CalculateMaintenanceEnergy(double weightKg, double temperatureC)
        {
            double baseRate = MaintenanceCoefficient * Math.Pow(weightKg, MaintenanceExponent);
            return baseRate * CalculateTemperatureCorrection(temperatureC);
        }

        /// <summary>
        /// Calculates the activity energy cost for swimming and movement.
        /// 
        /// Activity = ActivityMultiplier × Maintenance × ActivityLevel
        /// 
        /// Activity level is a dimensionless factor:
        /// - 0.0 = Complete rest (stationary)
        /// - 1.0 = Normal swimming (baseline activity)
        /// - &gt;1.0 = High activity (escape responses, feeding bursts)
        /// 
        /// </summary>
        /// <param name="weightKg">Fish body weight in kilograms.</param>
        /// <param name="temperatureC">Water temperature in °C.</param>
        /// <param name="activityLevel">
        /// Activity level multiplier (0.0 = rest, 1.0 = normal, &gt;1.0 = high activity).
        /// </param>
        /// <returns>
        /// Activity energy cost in kJ/day.
        /// </returns>
        public static double CalculateActivityEnergy(double weightKg, double temperatureC, double activityLevel)
        {
            double maintenance = CalculateMaintenanceEnergy(weightKg, temperatureC);
            return ActivityMultiplier * maintenance * activityLevel;
        }

        /// <summary>
        /// Calculates the energy lost through waste (fecal and urinary).
        /// 
        /// Waste = WasteFraction × IntakeEnergy
        /// 
        /// This represents uneaten feed, digestive inefficiency, and metabolic waste.
        /// </summary>
        /// <param name="feedIntakeKg">Daily feed intake in kilograms.</param>
        /// <returns>
        /// Waste energy loss in kJ/day.
        /// </returns>
        public static double CalculateWasteEnergy(double feedIntakeKg)
        {
            double intakeEnergy = CalculateFeedEnergy(feedIntakeKg);
            return WasteFraction * intakeEnergy;
        }

        /// <summary>
        /// Calculates the total energy intake from feed.
        /// </summary>
        /// <param name="feedIntakeKg">Feed intake in kilograms.</param>
        /// <returns>
        /// Total energy intake in kJ.
        /// </returns>
        public static double CalculateFeedEnergy(double feedIntakeKg)
        {
            return feedIntakeKg * FeedEnergyDensityKJPerGram * GramsPerKilogram;
        }

        /// <summary>
        /// Calculates the net energy available for growth after all energy costs.
        /// 
        /// NetEnergy = Intake - Maintenance - Activity - Waste
        /// 
        /// This is the energy that can be allocated to somatic growth, reproduction,
        /// or energy storage. Negative values indicate the fish is in an energy deficit
        /// and will lose weight.
        /// </summary>
        /// <param name="weightKg">Current fish weight in kilograms.</param>
        /// <param name="feedIntakeKg">Daily feed intake in kilograms.</param>
        /// <param name="temperatureC">Water temperature in °C.</param>
        /// <param name="activityLevel">Activity level multiplier.</param>
        /// <returns>
        /// Net energy for growth in kJ/day.
        /// Returns 0.0 if the result is negative (maximum growth is zero, not negative).
        /// Weight loss is handled by the growth model, not this function.
        /// </returns>
        public static double CalculateNetEnergyForGrowth(
            double weightKg,
            double feedIntakeKg,
            double temperatureC,
            double activityLevel)
        {
            // Calculate all energy components
            double intakeEnergy = CalculateFeedEnergy(feedIntakeKg);
            double maintenanceEnergy = CalculateMaintenanceEnergy(weightKg, temperatureC);
            double activityEnergy = CalculateActivityEnergy(weightKg, temperatureC, activityLevel);
            double wasteEnergy = CalculateWasteEnergy(feedIntakeKg);

            // Calculate net energy
            double netEnergy = intakeEnergy - maintenanceEnergy - activityEnergy - wasteEnergy;

            // Growth cannot be negative; fish simply doesn't grow if net energy is negative
            // Weight loss is handled by growth models using this as input
            return Math.Max(0.0, netEnergy);
        }

        /// <summary>
        /// Converts net energy available for growth to weight gain.
        /// 
        /// WeightGain = NetEnergy / EnergyDensity
        /// 
        /// Uses the standard conversion factor for salmon tissue growth
        /// (8.0 kJ/g = energy required to synthesize 1 g of new biomass).
        /// </summary>
        /// <param name="netEnergyKJ">Net energy available for growth in kJ/day.</param>
        /// <returns>
        /// Weight gain in kilograms.
        /// </returns>
        public static double ConvertEnergyToGrowth(double netEnergyKJ)
        {
            if (netEnergyKJ <= 0) return 0.0; // No growth from negative energy budget
            // Convert kJ to grams, then to kg
            double weightGainGrams = netEnergyKJ / EnergyDensityKJPerGram;
            return weightGainGrams / GramsPerKilogram;
        }

        #endregion
    }
}