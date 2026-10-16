using System;
using System.Collections.Generic;
using LakskongenSim.Calibration;
using LakskongenSim.Core;

namespace LakskongenSim.Environment
{
    /// <summary>An operation at an offshore unit (spec §3.2).</summary>
    public enum OffshoreOp { Feeding, Resupply, Silage, CrewChange, NetInspection, Stocking, Transfer, Crowding, Delousing, Harvest, Tow }

    /// <summary>The vessel an operation uses (spec §3.2).</summary>
    public enum VesselClass { Standard, DpWalkToWork, OffshoreWellboat, Helicopter }

    /// <summary>
    /// What an offshore unit can do on a day, and the odds of doing it on each
    /// forecast day (roadmap F2-06, spec §3.3 point 5 operability table). Pure.
    /// F2-07's <c>OffshoreHooks.OperationPossible(site, op, day, vessel)</c> calls
    /// <see cref="IsPossible"/> with the plot's Hs, the day's wind and visibility.
    /// </summary>
    public static class OperabilityWindows
    {
        /// <summary>Fish handling: the operations the 2 m crowding limit governs.</summary>
        public static bool IsFishHandling(OffshoreOp op)
            => op is OffshoreOp.Stocking or OffshoreOp.Transfer or OffshoreOp.Crowding or OffshoreOp.Delousing or OffshoreOp.Harvest;

        /// <summary>
        /// The Hs limit of an operation with a vessel (m): null when that vessel
        /// cannot do it, +∞ when no sea state stops it (feeding from the silo; a
        /// helicopter, which wind and visibility gate instead).
        /// </summary>
        public static double? HsLimitM(OffshoreOp op, VesselClass vessel, OffshoreOperabilityCalibration cal)
        {
            if (cal == null) throw new ArgumentNullException(nameof(cal));
            if (op == OffshoreOp.Feeding) return double.PositiveInfinity;
            if (IsFishHandling(op))
                return vessel switch
                {
                    VesselClass.Standard or VesselClass.DpWalkToWork => cal.FishHandlingHsM,
                    VesselClass.OffshoreWellboat => cal.FishHandlingOffshoreWellboatHsM,
                    _ => null,
                };
            return op switch
            {
                OffshoreOp.CrewChange => vessel switch
                {
                    VesselClass.Standard => cal.BoardingHsM,
                    VesselClass.DpWalkToWork => cal.BoardingDpHsM,
                    VesselClass.Helicopter => double.PositiveInfinity,
                    _ => null,
                },
                OffshoreOp.NetInspection => vessel is VesselClass.Standard or VesselClass.DpWalkToWork ? cal.NetInspectionHsM : null,
                OffshoreOp.Resupply or OffshoreOp.Silage => vessel switch
                {
                    VesselClass.Standard => cal.ResupplyHsM,
                    VesselClass.DpWalkToWork => cal.ResupplyDpHsM,
                    _ => null,
                },
                OffshoreOp.Tow => vessel == VesselClass.Standard ? cal.TowHsM : null,
                _ => null,
            };
        }

        /// <summary>Consecutive days an operation needs its sea state (the tow's hold; 1 otherwise).</summary>
        public static int DaysNeeded(OffshoreOp op, OffshoreOperabilityCalibration cal)
            => op == OffshoreOp.Tow ? cal.TowHoldDays : 1;

        /// <summary>
        /// Whether the operation can start today: Hs at or below its limit, and
        /// for a helicopter wind below and visibility at or above its limits.
        /// A tow also needs <see cref="DaysNeeded"/> days of it (F2-07 checks the
        /// hold against the forecast).
        /// </summary>
        public static bool IsPossible(OffshoreOp op, VesselClass vessel, double hsM, double windMs, double visibilityKm,
            OffshoreOperabilityCalibration cal)
        {
            double? limit = HsLimitM(op, vessel, cal);
            if (limit == null || hsM > limit.Value) return false;
            if (vessel == VesselClass.Helicopter)
                return windMs < cal.HelicopterMaxWindMs && visibilityKm >= cal.HelicopterMinVisibilityKm;
            return true;
        }

        /// <summary>
        /// A wellboat may stay alongside (pumping pauses above the handling
        /// limit) up to <see cref="OffshoreOperabilityCalibration.WellboatAlongsideHsM"/>.
        /// </summary>
        public static bool WellboatCanStayAlongside(double hsM, OffshoreOperabilityCalibration cal)
            => hsM <= cal.WellboatAlongsideHsM;

        /// <summary>
        /// Feeding appetite in heavy seas (share of the ration): 1 up to
        /// <see cref="OffshoreOperabilityCalibration.AppetiteFullHsM"/>, falling
        /// linearly to <see cref="OffshoreOperabilityCalibration.AppetiteAtFloor"/>
        /// at <see cref="OffshoreOperabilityCalibration.AppetiteFloorHsM"/> and
        /// staying there.
        /// </summary>
        public static double AppetiteFactor(double hsM, OffshoreOperabilityCalibration cal)
        {
            if (hsM <= cal.AppetiteFullHsM) return 1.0;
            if (hsM >= cal.AppetiteFloorHsM) return cal.AppetiteAtFloor;
            double share = (hsM - cal.AppetiteFullHsM) / (cal.AppetiteFloorHsM - cal.AppetiteFullHsM);
            return 1.0 - share * (1.0 - cal.AppetiteAtFloor);
        }

        /// <summary>
        /// Probability that the operation can start on the day
        /// <paramref name="leadDays"/> ahead, exact under the forecast: the
        /// plot's P(Hs ≤ limit), and for a helicopter the weather chain's P(wind
        /// below the limit) times the month's P(visibility at or above it)
        /// (visibility is drawn independently each day). A multi-day hold (the
        /// tow) is the marginal of its first day; the joint hold is F2-07's.
        /// </summary>
        public static double Probability(SeaStateForecast forecast, int leadDays, OffshoreOp op, VesselClass vessel,
            OffshoreOperabilityCalibration operability, OffshoreMetoceanCalibration metocean)
        {
            if (forecast == null) throw new ArgumentNullException(nameof(forecast));
            double? limit = HsLimitM(op, vessel, operability);
            if (limit == null) return 0.0;
            if (vessel == VesselClass.Helicopter)
            {
                var weather = forecast.WeatherDistribution(leadDays);
                double windOk = 0.0;
                foreach (WeatherState w in Enum.GetValues<WeatherState>())
                    windOk += weather[(int)w] * WeatherSystem.WindSpeedBelowProbability(w, operability.HelicopterMaxWindMs);
                int month = SimCalendar.MonthIndex(forecast.IssuedDay + leadDays);
                double visOk = 1.0 - OffshoreMetoceanModel.VisibilityBelowProbability(metocean, month, operability.HelicopterMinVisibilityKm);
                return windOk * visOk;
            }
            return double.IsPositiveInfinity(limit.Value) ? 1.0 : forecast.ProbabilityAtOrBelow(leadDays, limit.Value);
        }

        /// <summary>The operation's probability on every forecast day, lead 1 first.</summary>
        public static double[] Outlook(SeaStateForecast forecast, OffshoreOp op, VesselClass vessel,
            OffshoreOperabilityCalibration operability, OffshoreMetoceanCalibration metocean)
        {
            if (forecast == null) throw new ArgumentNullException(nameof(forecast));
            var p = new double[forecast.HorizonDays];
            for (int k = 1; k <= p.Length; k++)
                p[k - 1] = Probability(forecast, k, op, vessel, operability, metocean);
            return p;
        }

        /// <summary>The first lead (days) whose probability reaches <paramref name="minProbability"/>; null if none does.</summary>
        public static int? FirstWindowLead(IReadOnlyList<double> outlook, double minProbability)
        {
            if (outlook == null) throw new ArgumentNullException(nameof(outlook));
            for (int i = 0; i < outlook.Count; i++)
                if (outlook[i] >= minProbability) return i + 1;
            return null;
        }

        /// <summary>
        /// The longest run of days in <paramref name="hsSeries"/> on which no
        /// window starts: a window is <paramref name="windowDays"/> consecutive
        /// days at or below <paramref name="limitM"/>. The climatology the plot
        /// selection quotes ("Ytre kyst: 1-3 weeks without a handling window per
        /// winter", spec §4).
        /// </summary>
        public static int LongestSpellWithoutWindow(IReadOnlyList<double> hsSeries, double limitM, int windowDays)
        {
            if (hsSeries == null) throw new ArgumentNullException(nameof(hsSeries));
            if (windowDays < 1) throw new ArgumentOutOfRangeException(nameof(windowDays), windowDays, "A window is at least one day");
            int best = 0, current = 0;
            int n = hsSeries.Count;
            // calmAhead[i]: consecutive calm days starting at i, computed from the end.
            var calmAhead = new int[n + 1];
            for (int i = n - 1; i >= 0; i--)
                calmAhead[i] = hsSeries[i] <= limitM ? calmAhead[i + 1] + 1 : 0;
            for (int i = 0; i < n; i++)
            {
                bool windowStarts = calmAhead[i] >= windowDays;
                current = windowStarts ? 0 : current + 1;
                if (current > best) best = current;
            }
            return best;
        }
    }
}
