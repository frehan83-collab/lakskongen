using LakskongenSim.Calibration;

namespace LakskongenSim.Biology
{
    /// <summary>
    /// Mortality hazards as pure functions of a cohort's environment (S2-05).
    /// A hazard never looks up a zone, the weather or a facility: the engine
    /// resolves the <see cref="CohortEnvironment"/> from whatever holds the
    /// fish and passes it in, so the same rule serves a sea pen, a land-based
    /// tank (its own controlled water) and an offshore structure. The engine
    /// books the resulting deaths with <see cref="FishPopulation.RemoveDead"/>
    /// under the hazard's <see cref="MortalityCause"/>.
    /// </summary>
    public static class MortalityHazards
    {
        /// <summary>
        /// Daily fraction of a cohort killed by water temperature outside the
        /// stress-free band (<see cref="MortalityCalibration.TemperatureStressLowerC"/>
        /// to <see cref="MortalityCalibration.TemperatureStressUpperC"/>):
        /// <see cref="MortalityCalibration.TemperatureStressDailyFractionPerDegreeC"/>
        /// per °C of deviation. Zero inside the band and when the temperature
        /// is unknown. Cause: <see cref="MortalityCause.Temperature"/>.
        /// </summary>
        public static double TemperatureStressFraction(in CohortEnvironment environment, MortalityCalibration calibration)
        {
            double t = environment.WaterTemperatureC;
            if (double.IsNaN(t)) return 0.0;
            double deviation = t < calibration.TemperatureStressLowerC
                ? calibration.TemperatureStressLowerC - t
                : t > calibration.TemperatureStressUpperC
                    ? t - calibration.TemperatureStressUpperC
                    : 0.0;
            return deviation * calibration.TemperatureStressDailyFractionPerDegreeC;
        }

        /// <summary>
        /// Fraction of a cohort killed by one exposure to the oxygen at the
        /// fish (<see cref="CohortEnvironment.OxygenMgL"/>): none at or above
        /// the acute limit (<see cref="MortalityCalibration.AcuteLowOxygenMgL"/>,
        /// 3 mg/L, US EPA 1986 for salmonids), then
        /// <see cref="MortalityCalibration.LowOxygenDeathFraction"/> (a tenth),
        /// below <see cref="MortalityCalibration.LowOxygenSevereMgL"/> a
        /// quarter and below <see cref="MortalityCalibration.LowOxygenAnoxicMgL"/>
        /// half. Zero when the oxygen is unknown. One rule for every source of
        /// low oxygen: a plankton crash's fjord-wide collapse, a cage's oxygen
        /// sag (the bloom deck's <c>hypoxia</c> card, through the pen's
        /// water), and whatever a farm type's water does (a land tank's
        /// controlled oxygen, an offshore structure's current-fed water).
        /// Cause: <see cref="MortalityCause.LowOxygen"/>.
        /// </summary>
        public static double LowOxygenFraction(in CohortEnvironment environment, MortalityCalibration calibration)
        {
            double o2 = environment.OxygenMgL;
            if (double.IsNaN(o2) || o2 >= calibration.AcuteLowOxygenMgL) return 0.0;
            if (o2 < calibration.LowOxygenAnoxicMgL) return calibration.LowOxygenDeathFractionAnoxic;
            if (o2 < calibration.LowOxygenSevereMgL) return calibration.LowOxygenDeathFractionSevere;
            return calibration.LowOxygenDeathFraction;
        }

        /// <summary>Whole fish killed in a cohort of <paramref name="count"/> at a daily fraction (truncated, as the engine always counted).</summary>
        public static int Deaths(int count, double fraction)
            => count <= 0 || !(fraction > 0.0) ? 0 : (int)(count * fraction);
    }
}
