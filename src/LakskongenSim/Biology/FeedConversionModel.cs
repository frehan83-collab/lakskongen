using System;
using LakskongenSim.Environment;

namespace LakskongenSim.Biology
{
    /// <summary>
    /// Feed composition specification for dynamic FCR calculations.
    /// </summary>
    public class FeedComposition
    {
        /// <summary>Protein content as a fraction (0.0 to 1.0, typical 0.40-0.50).</summary>
        public double ProteinFraction { get; set; } = 0.45;

        /// <summary>Fat content as a fraction (0.0 to 1.0, typical 0.20-0.30).</summary>
        public double FatFraction { get; set; } = 0.25;

        /// <summary>Pellet size in millimeters (1-12 mm).</summary>
        public double PelletSizeMm { get; set; } = 6.0;

        /// <summary>Energy density in kJ/g (typical 20-24 kJ/g).</summary>
        public double EnergyDensity { get; set; } = 22.0;
    }

    /// <summary>
    /// Feeding schedule specification for dynamic FCR calculations.
    /// </summary>
    public class FeedingSchedule
    {
        /// <summary>Number of feeding events per day (1-24).</summary>
        public int FeedingsPerDay { get; set; } = 6;

        /// <summary>Daily feed ration as percentage of body weight (0.5-3.0%).</summary>
        public double DailyRationPercent { get; set; } = 1.5;

        /// <summary>Feed utilization efficiency (0.0-1.0, accounts for waste).</summary>
        public double UtilizationEfficiency { get; set; } = 0.90;
    }

    /// <summary>
    /// Implements dynamic Feed Conversion Ratio (FCR) calculation based on:
    /// - Protein/fat ratios in feed composition
    /// - Pellet size relative to fish mouth size
    /// - Feeding frequency and schedule
    /// - Fish size and metabolic state
    /// - Environmental conditions (temperature, oxygen)
    /// - Current strength (affects feed distribution and utilization)
    /// 
    /// FCR = Feed consumed (kg) / Weight gain (kg)
    /// 
    /// Lower FCR = better feed efficiency (industry target: 1.1-1.3)
    /// Higher FCR = poor conversion (wasteful, uneconomical)
    /// 
    /// Mareano/Geonorge Environmental Integration:
    /// - Current speed affects feed distribution (too weak = waste, too strong = washout)
    /// - Temperature/oxygen stress directly impact metabolic efficiency
    /// - Combined environmental stress multiplier applied to FCR
    /// </summary>
    public class FeedConversionModel
    {
        #region Constants

        /// <summary>Baseline FCR under optimal conditions (Norwegian best practice).</summary>
        public static readonly double BaselineFCR = 1.05;

        /// <summary>Optimal protein fraction for salmon growth.</summary>
        public static readonly double OptimalProteinFraction = 0.45;

        /// <summary>Optimal fat fraction for salmon growth.</summary>
        public static readonly double OptimalFatFraction = 0.25;

        /// <summary>Optimal feeding frequency (feedings per day).</summary>
        public static readonly int OptimalFeedingsPerDay = 6;

        /// <summary>Optimal pellet size to fish length ratio (empirically ~4-5% of body length).</summary>
        public static readonly double OptimalPelletToLengthRatio = 0.04;

        /// <summary>Maximum FCR penalty for suboptimal conditions.</summary>
        public static readonly double MaxFCRPenalty = 0.50; // 50% worse FCR

        /// <summary>Minimum FCR (best possible efficiency with perfect conditions).</summary>
        public static readonly double MinFCR = 1.0;

        /// <summary>Maximum FCR (worst case before feeding stops being viable).</summary>
        public static readonly double MaxFCR = 2.0;

        // Mareano/Geonorge current integration constants
        /// <summary>Optimal current speed for feed distribution (m/s).</summary>
        public const double OptimalFeedCurrentMin = 0.1;

        /// <summary>Maximum optimal current speed for feed distribution (m/s).</summary>
        public const double OptimalFeedCurrentMax = 0.25;

        /// <summary>Current below which feed distribution is poor (m/s).</summary>
        public const double WeakFeedCurrent = 0.05;

        /// <summary>Current above which feed washout occurs (m/s).</summary>
        public const double StrongFeedCurrent = 0.4;

        /// <summary>Maximum FCR penalty from poor current conditions.</summary>
        public const double MaxCurrentFCRPenalty = 0.15;

        #endregion

        #region Public Methods

        /// <summary>
        /// Calculates dynamic FCR based on feed composition, schedule, fish state, and environment.
        /// </summary>
        /// <param name="fishWeight">Current fish weight in kg.</param>
        /// <param name="temperature">Water temperature in °C.</param>
        /// <param name="oxygenSaturation">Dissolved oxygen saturation (0.0-1.0).</param>
        /// <param name="feedComposition">Feed composition specification.</param>
        /// <param name="feedingSchedule">Feeding schedule specification.</param>
        /// <returns>Dynamic FCR value (typically 1.0-2.0).</returns>
        public double CalculateDynamicFCR(
            double fishWeight,
            double temperature,
            double oxygenSaturation,
            FeedComposition feedComposition,
            FeedingSchedule feedingSchedule)
        {
            // Start with baseline FCR
            double fcr = BaselineFCR;

            // Apply composition penalties/bonuses
            double proteinPenalty = CalculateProteinPenalty(feedComposition.ProteinFraction);
            double fatPenalty = CalculateFatPenalty(feedComposition.FatFraction);

            // Apply pellet size penalty
            double fishLengthCm = EstimateFishLength(fishWeight);
            double pelletPenalty = CalculatePelletSizePenalty(feedComposition.PelletSizeMm, fishLengthCm);

            // Apply feeding frequency penalty
            double frequencyPenalty = CalculateFeedingFrequencyPenalty(feedingSchedule.FeedingsPerDay);

            // Apply environmental penalties
            double temperaturePenalty = CalculateTemperaturePenalty(temperature);
            double oxygenPenalty = CalculateOxygenPenalty(oxygenSaturation);

            // Apply utilization efficiency relative to 90% baseline
            // (90% utilization = no penalty; worse utilization increases FCR proportionally)
            const double baselineUtilization = 0.90;
            double utilizationFactor = baselineUtilization / Math.Max(0.5, feedingSchedule.UtilizationEfficiency);

            // Combine all penalties multiplicatively
            fcr *= (1.0 + proteinPenalty);
            fcr *= (1.0 + fatPenalty);
            fcr *= (1.0 + pelletPenalty);
            fcr *= (1.0 + frequencyPenalty);
            fcr *= (1.0 + temperaturePenalty);
            fcr *= (1.0 + oxygenPenalty);
            fcr *= utilizationFactor;

            // Clamp to realistic bounds
            return Math.Clamp(fcr, MinFCR, MaxFCR);
        }

        /// <summary>
        /// Calculates penalty for deviation from optimal protein fraction.
        /// Optimal is ~45% protein. Too high = expensive, too low = slow growth.
        /// </summary>
        private double CalculateProteinPenalty(double proteinFraction)
        {
            double deviation = Math.Abs(proteinFraction - OptimalProteinFraction);
            // 10% deviation = ~5% FCR penalty
            return Math.Min(deviation * 0.5, MaxFCRPenalty * 0.2);
        }

        /// <summary>
        /// Calculates penalty for deviation from optimal fat fraction.
        /// Optimal is ~25% fat. Too high = poor protein utilization, too low = energy deficit.
        /// </summary>
        private double CalculateFatPenalty(double fatFraction)
        {
            double deviation = Math.Abs(fatFraction - OptimalFatFraction);
            // 10% deviation = ~8% FCR penalty (fat ratio is more critical than protein)
            return Math.Min(deviation * 0.8, MaxFCRPenalty * 0.3);
        }

        /// <summary>
        /// Calculates penalty for suboptimal pellet size relative to fish size.
        /// Pellets too large = fish can't eat them. Too small = wasteful.
        /// </summary>
        private double CalculatePelletSizePenalty(double pelletSizeMm, double fishLengthCm)
        {
            double pelletToLengthRatio = pelletSizeMm / (fishLengthCm * 10.0); // Convert cm to mm
            double deviation = Math.Abs(pelletToLengthRatio - OptimalPelletToLengthRatio);
            
            // Large deviations severely impact FCR
            if (pelletToLengthRatio > OptimalPelletToLengthRatio * 2.0)
            {
                // Pellets too large - fish can't eat properly; severe penalty
                return MaxFCRPenalty * 0.6;
            }
            
            // 50% deviation from optimal = ~10% FCR penalty
            return Math.Min(deviation * 5.0, MaxFCRPenalty * 0.25);
        }

        /// <summary>
        /// Calculates penalty for suboptimal feeding frequency.
        /// Too few feedings = poor nutrient distribution. Too many = stress.
        /// </summary>
        private double CalculateFeedingFrequencyPenalty(int feedingsPerDay)
        {
            // Optimal is 6 feedings per day (every 4 hours during daylight)
            int deviation = Math.Abs(feedingsPerDay - OptimalFeedingsPerDay);
            
            // Severe penalties for extreme frequencies
            if (feedingsPerDay < 2)
            {
                return MaxFCRPenalty * 0.3; // Too infrequent
            }
            if (feedingsPerDay > 12)
            {
                return MaxFCRPenalty * 0.2; // Too frequent (stress)
            }
            
            // Each deviation = ~3% FCR penalty
            return Math.Min(deviation * 0.03, MaxFCRPenalty * 0.15);
        }

        /// <summary>
        /// Calculates penalty for suboptimal temperature.
        /// Uses same Gaussian temperature response as growth model.
        /// </summary>
        private double CalculateTemperaturePenalty(double temperature)
        {
            const double optimalTemp = 11.0;
            const double tolerance = 3.0;

            // Outside viable range = maximum penalty
            if (temperature < 4.0 || temperature > 16.0)
            {
                return MaxFCRPenalty;
            }

            // Gaussian penalty based on temperature deviation
            double tempDiff = Math.Abs(temperature - optimalTemp);
            double penalty = (tempDiff / tolerance) * 0.15; // 15% penalty at ±1 tolerance

            return Math.Min(penalty, MaxFCRPenalty * 0.4);
        }

        /// <summary>
        /// Calculates penalty for low dissolved oxygen.
        /// Below 80% saturation significantly impacts feed efficiency.
        /// </summary>
        private double CalculateOxygenPenalty(double oxygenSaturation)
        {
            const double optimalSaturation = 0.90; // 90%+
            const double criticalSaturation = 0.60; // Below 60% = severe impact

            if (oxygenSaturation >= optimalSaturation)
            {
                return 0.0; // No penalty
            }

            if (oxygenSaturation < criticalSaturation)
            {
                return MaxFCRPenalty * 0.5; // Severe penalty
            }

            // Linear penalty between critical and optimal
            double deficit = (optimalSaturation - oxygenSaturation) / (optimalSaturation - criticalSaturation);
            return deficit * MaxFCRPenalty * 0.3;
        }

        /// <summary>
        /// Estimates fish total length from weight using empirical length-weight relationship.
        /// L = a × W^b where a ≈ 1.22, b ≈ 0.33 for Atlantic salmon.
        /// </summary>
        /// <param name="weightKg">Fish weight in kg.</param>
        /// <returns>Estimated total length in cm.</returns>
        private double EstimateFishLength(double weightKg)
        {
            const double lengthCoefficient = 1.22;
            const double lengthExponent = 0.33;
            
            // Convert to grams for calculation
            double weightGrams = weightKg * 1000.0;
            return lengthCoefficient * Math.Pow(weightGrams, lengthExponent);
        }

        /// <summary>
        /// Calculates optimal daily feed ration as percentage of body weight.
        /// Larger fish need less feed (% body weight) but more absolute feed.
        /// </summary>
        /// <param name="fishWeight">Fish weight in kg.</param>
        /// <param name="temperature">Water temperature in °C.</param>
        /// <returns>Optimal daily ration as decimal fraction (e.g., 0.015 = 1.5%).</returns>
        public double CalculateOptimalDailyRation(double fishWeight, double temperature)
        {
            // Base ration scales inversely with weight (power law)
            // Small fish (0.01 kg) = ~3% body weight/day
            // Large fish (5 kg) = ~0.8% body weight/day
            double baseRation = 0.03 * Math.Pow(fishWeight, -0.2);

            // Temperature correction (using same Q10 model as metabolism)
            double tempCorrection = EnergyBudgetModel.CalculateTemperatureCorrection(temperature);
            
            // Adjust ration by temperature (warmer = more active = more feed needed)
            double adjustedRation = baseRation * Math.Pow(tempCorrection, 0.5);

            // Clamp to realistic bounds (0.5% to 3% body weight)
            return Math.Clamp(adjustedRation, 0.005, 0.03);
        }

        /// <summary>
        /// Calculates expected weight gain for a given feed input using current FCR.
        /// </summary>
        /// <param name="feedAmountKg">Feed consumed in kg.</param>
        /// <param name="fcr">Current Feed Conversion Ratio.</param>
        /// <returns>Expected weight gain in kg.</returns>
        public double CalculateWeightGainFromFeed(double feedAmountKg, double fcr)
        {
            if (fcr <= 0.0 || feedAmountKg <= 0.0)
                return 0.0;

            // FCR = Feed / Gain, therefore Gain = Feed / FCR
            return feedAmountKg / fcr;
        }

        /// <summary>
        /// Calculates feed required to achieve target weight gain using current FCR.
        /// </summary>
        /// <param name="targetWeightGainKg">Desired weight gain in kg.</param>
        /// <param name="fcr">Current Feed Conversion Ratio.</param>
        /// <returns>Feed required in kg.</returns>
        public double CalculateFeedForTargetGain(double targetWeightGainKg, double fcr)
        {
            if (fcr <= 0.0 || targetWeightGainKg <= 0.0)
                return 0.0;

            // FCR = Feed / Gain, therefore Feed = Gain × FCR
            return targetWeightGainKg * fcr;
        }

        /// <summary>
        /// Calculates dynamic FCR with full environmental data integration.
        /// 
        /// Incorporates Mareano/Geonorge environmental data:
        /// - Current speed affects feed distribution and utilization
        /// - Combined environmental stress impacts metabolic efficiency
        /// - Temperature and oxygen integrated from environmental data
        /// </summary>
        /// <param name="fishWeight">Current fish weight in kg.</param>
        /// <param name="environmentalData">Environmental data point with temperature, oxygen, current.</param>
        /// <param name="feedComposition">Feed composition specification.</param>
        /// <param name="feedingSchedule">Feeding schedule specification.</param>
        /// <returns>Dynamic FCR value (typically 1.0-2.0).</returns>
        public double CalculateDynamicFCR(
            double fishWeight,
            EnvironmentalDataPoint environmentalData,
            FeedComposition feedComposition,
            FeedingSchedule feedingSchedule)
        {
            // Start with baseline FCR
            double fcr = BaselineFCR;

            // Apply composition penalties/bonuses
            double proteinPenalty = CalculateProteinPenalty(feedComposition.ProteinFraction);
            double fatPenalty = CalculateFatPenalty(feedComposition.FatFraction);

            // Apply pellet size penalty
            double fishLengthCm = EstimateFishLength(fishWeight);
            double pelletPenalty = CalculatePelletSizePenalty(feedComposition.PelletSizeMm, fishLengthCm);

            // Apply feeding frequency penalty
            double frequencyPenalty = CalculateFeedingFrequencyPenalty(feedingSchedule.FeedingsPerDay);

            // Apply environmental penalties from Mareano data
            double temperaturePenalty = CalculateTemperaturePenalty(environmentalData.TemperatureAtCageDepth);
            double oxygenPenalty = CalculateOxygenPenalty(environmentalData.OxygenSaturation);
            double currentPenalty = CalculateCurrentFeedPenalty(environmentalData.CurrentSpeedAtCageDepth);

            // Apply combined environmental stress (from environmental quality score)
            double environmentalStressPenalty = (1.0 - environmentalData.EnvironmentalQualityScore) * 0.2;

            // Apply utilization efficiency relative to 90% baseline
            const double baselineUtilization = 0.90;
            double utilizationFactor = baselineUtilization / Math.Max(0.5, feedingSchedule.UtilizationEfficiency);

            // Combine all penalties multiplicatively
            fcr *= (1.0 + proteinPenalty);
            fcr *= (1.0 + fatPenalty);
            fcr *= (1.0 + pelletPenalty);
            fcr *= (1.0 + frequencyPenalty);
            fcr *= (1.0 + temperaturePenalty);
            fcr *= (1.0 + oxygenPenalty);
            fcr *= (1.0 + currentPenalty);
            fcr *= (1.0 + environmentalStressPenalty);
            fcr *= utilizationFactor;

            // Clamp to realistic bounds
            return Math.Clamp(fcr, MinFCR, MaxFCR);
        }

        /// <summary>
        /// Calculates penalty for suboptimal current speed affecting feed distribution.
        /// 
        /// Too weak (less than 0.05 m/s): Poor feed distribution, waste accumulation, overconsumption by dominant fish
        /// Optimal (0.1-0.25 m/s): Good distribution, optimal feed utilization
        /// Too strong (greater than 0.4 m/s): Feed washout, reduced consumption time, fish stress
        /// </summary>
        private double CalculateCurrentFeedPenalty(double currentSpeedMS)
        {
            if (currentSpeedMS >= OptimalFeedCurrentMin && currentSpeedMS <= OptimalFeedCurrentMax)
            {
                // Optimal range - slight bonus
                return -0.02; // 2% FCR improvement
            }
            else if (currentSpeedMS < WeakFeedCurrent)
            {
                // Too weak - poor distribution causes waste and uneven feeding
                double deficit = WeakFeedCurrent - currentSpeedMS;
                double severity = deficit / WeakFeedCurrent;
                return MaxCurrentFCRPenalty * severity; // Up to 15% worse FCR
            }
            else if (currentSpeedMS > StrongFeedCurrent)
            {
                // Too strong - feed washout, fish can't eat enough
                double excess = currentSpeedMS - StrongFeedCurrent;
                double severity = Math.Min(1.0, excess / 0.2);
                return MaxCurrentFCRPenalty * 1.2 * severity; // Up to 18% worse FCR (washout is worse)
            }
            else if (currentSpeedMS < OptimalFeedCurrentMin)
            {
                // Suboptimal but acceptable (weak)
                double range = OptimalFeedCurrentMin - WeakFeedCurrent;
                double position = currentSpeedMS - WeakFeedCurrent;
                double severity = 1.0 - (position / range);
                return MaxCurrentFCRPenalty * 0.5 * severity; // Up to 7.5% penalty
            }
            else
            {
                // Suboptimal but acceptable (strong)
                double range = StrongFeedCurrent - OptimalFeedCurrentMax;
                double position = StrongFeedCurrent - currentSpeedMS;
                double severity = 1.0 - (position / range);
                return MaxCurrentFCRPenalty * 0.6 * severity; // Up to 9% penalty
            }
        }

        /// <summary>
        /// Get the optimal daily ration adjusted for environmental conditions.
        /// Environmental stress reduces appetite and metabolic efficiency.
        /// </summary>
        public double CalculateOptimalDailyRationWithEnvironment(
            double fishWeight, 
            EnvironmentalDataPoint environmentalData)
        {
            // Base ration from temperature
            double baseRation = CalculateOptimalDailyRation(fishWeight, environmentalData.TemperatureAtCageDepth);
            
            // Environmental stress reduces feeding
            double stressFactor = environmentalData.EnvironmentalQualityScore;
            
            // Low oxygen significantly reduces appetite
            if (environmentalData.OxygenAtCageDepth < 6.0)
            {
                stressFactor *= 0.7; // 30% appetite reduction
            }
            
            // Very strong currents make feeding difficult
            if (environmentalData.CurrentSpeedAtCageDepth > 0.4)
            {
                stressFactor *= 0.85; // 15% appetite reduction
            }
            
            return baseRation * Math.Max(0.5, stressFactor);
        }

        #endregion
    }
}
