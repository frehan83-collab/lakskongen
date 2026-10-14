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
        /// Oxygen (mg/L) below which a plankton crash kills fish: the
        /// <c>plankton-crash</c> event's trigger.
        /// </summary>
        public const double PlanktonCrashOxygenMgL = 3.0;

        /// <summary>
        /// Fraction of a cohort killed by a plankton-crash oxygen collapse at
        /// the cohort's own oxygen: half below 1 mg/L, a quarter below 2 mg/L,
        /// a tenth below <see cref="PlanktonCrashOxygenMgL"/>, none above (a
        /// land-based tank's controlled oxygen or an offshore structure's
        /// current-fed water can stay above it while the fjord collapses).
        /// Cause: <see cref="MortalityCause.LowOxygen"/>.
        /// </summary>
        public static double PlanktonCrashFraction(in CohortEnvironment environment)
        {
            double o2 = environment.OxygenMgL;
            if (double.IsNaN(o2) || o2 >= PlanktonCrashOxygenMgL) return 0.0;
            return o2 < 1.0 ? 0.5 : o2 < 2.0 ? 0.25 : 0.1;
        }

        /// <summary>Whole fish killed in a cohort of <paramref name="count"/> at a daily fraction (truncated, as the engine always counted).</summary>
        public static int Deaths(int count, double fraction)
            => count <= 0 || !(fraction > 0.0) ? 0 : (int)(count * fraction);
    }
}
