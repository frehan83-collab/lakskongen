using System;
using System.Collections.Generic;
using LakskongenSim.Calibration;

namespace LakskongenSim.Farm.Land
{
    /// <summary>
    /// Early maturation of land fish (roadmap F2-15 projection, F2-16 tick;
    /// docs/FARM_TYPES_SPEC.md §3.3 "Maturation"): an index that rises
    /// 0.004 a day per degree over 12 °C, × the light (continuous 1.0,
    /// short-day 0.3) × the stock (all-female 0.1); by harvest the index is the
    /// share of the males maturing, capped at 80 %. Pure: no state, no random
    /// numbers. The Vannstrategi presets' maturation risk reads it over the
    /// projected tank water.
    /// </summary>
    public static class LandMaturation
    {
        /// <summary>
        /// The index's rise on a day at <paramref name="temperatureC"/> under
        /// short-day light (<paramref name="shortDay"/>) or continuous light, for
        /// all-female or mixed-sex stock.
        /// </summary>
        public static double DailyRise(double temperatureC, bool shortDay, bool allFemale, LandMaturationCalibration calibration)
        {
            if (calibration == null) throw new ArgumentNullException(nameof(calibration));
            if (!double.IsFinite(temperatureC)) return 0.0;
            return calibration.RatePerDegreeDay * Math.Max(0.0, temperatureC - calibration.ThresholdC)
                * (shortDay ? calibration.ShortDayFactor : 1.0)
                * (allFemale ? calibration.AllFemaleFactor : 1.0);
        }

        /// <summary>Share of the males maturing at <paramref name="index"/> (0 to the cap).</summary>
        public static double MaturingMaleShare(double index, LandMaturationCalibration calibration)
        {
            if (calibration == null) throw new ArgumentNullException(nameof(calibration));
            return double.IsFinite(index) ? Math.Clamp(index, 0.0, calibration.MaxMaleShare) : 0.0;
        }

        /// <summary>Share of all the fish maturing at <paramref name="index"/> in a mixed-sex stock.</summary>
        public static double MaturingShareOfFish(double index, LandMaturationCalibration calibration)
            => (calibration ?? throw new ArgumentNullException(nameof(calibration))).MaleShare * MaturingMaleShare(index, calibration);

        /// <summary>
        /// The index a cohort reaches over <paramref name="cycleDays"/> days in
        /// water whose daily temperatures are <paramref name="tankC"/> (repeated
        /// from its start when the cycle is longer), under continuous light,
        /// mixed-sex stock.
        /// </summary>
        public static double IndexOverCycle(IReadOnlyList<double> tankC, int cycleDays, LandMaturationCalibration calibration)
        {
            if (tankC == null) throw new ArgumentNullException(nameof(tankC));
            if (calibration == null) throw new ArgumentNullException(nameof(calibration));
            if (tankC.Count == 0 || cycleDays <= 0) return 0.0;
            double index = 0.0;
            for (int d = 0; d < cycleDays; d++)
                index += DailyRise(tankC[d % tankC.Count], shortDay: false, allFemale: false, calibration);
            return index;
        }
    }
}
