using System;
using System.Collections.Generic;
using LakskongenSim.Calibration;
using LakskongenSim.Core;
using LakskongenSim.Environment;

namespace LakskongenSim.Farm.Offshore
{
    /// <summary>
    /// Futures of one plot's sea as the game knows it today (F2-07: the booking
    /// preview's "P(done by day X)" and expected charter, the tow's "P(the window
    /// holds 3 days)"). These are joint probabilities over consecutive days,
    /// which the F2-06 forecast's per-day marginals cannot give, so the preview
    /// samples the model's own conditional distribution: the fjord weather
    /// chain from today's state through the sampler's own transition rows, the
    /// open sea from a copy of its saved state ticked by the model's own
    /// <see cref="OffshoreMetocean.TickDay"/>, and each known swell event's
    /// unknown peak drawn from its posterior given today's estimate (what the
    /// player is told, never the truth). The draws come from a private
    /// SplitMix64 stream seeded from the question: the game's streams never
    /// move, and the same question gets the same answer. Calibrated by
    /// construction up to sampling error (±0.008 on a probability at 4,000 paths).
    /// </summary>
    public static class OffshoreSeaSampler
    {
        /// <summary>Paths a preview samples.</summary>
        public const int DefaultPaths = 4000;

        /// <summary>Uniforms one path day draws beside the sea's eight: the weather step and the helicopter's wind.</summary>
        private const int ExtraDrawsPerDay = 2;

        /// <summary>One future day of a path: the plot's Hs, the visibility and whether the wind lets a helicopter fly.</summary>
        public readonly record struct FutureDay(double HsM, double VisibilityKm, bool HelicopterWind);

        /// <summary>
        /// <paramref name="paths"/> futures of <paramref name="days"/> days after
        /// the sea's last tick (index 0 = tomorrow) at a plot of scale
        /// <paramref name="plotHsScale"/>. Pure.
        /// </summary>
        public static FutureDay[][] Sample(OffshoreMetocean sea, WeatherState weatherToday, double stormFrequencyMultiplier,
            WeatherState maxWeather, double plotHsScale, int days, int paths, ulong question, OffshoreOperabilityCalibration operability)
        {
            if (sea == null) throw new ArgumentNullException(nameof(sea));
            if (operability == null) throw new ArgumentNullException(nameof(operability));
            if (sea.LastTickDay < 0) throw new InvalidOperationException("The sea has not ticked yet");
            if (days < 1 || paths < 1) throw new ArgumentOutOfRangeException(nameof(days), "At least one day and one path");
            var cal = sea.Calibration();
            var stream = new SimRngStream(SimRng.Mix(question ^ 0x6F6666_73686F7265UL ^ (ulong)(uint)sea.LastTickDay));
            var row = new double[4];
            var result = new FutureDay[paths][];
            for (int p = 0; p < paths; p++)
            {
                var path = sea.CopyForSampling();
                RedrawUnknownPeaks(path, cal, stream);
                var weather = weatherToday;
                var future = new FutureDay[days];
                for (int k = 1; k <= days; k++)
                {
                    int day = sea.LastTickDay + k;
                    WeatherSystem.FillTransitionRow(weather, WeatherSystem.IsStormSeason(day), stormFrequencyMultiplier, maxWeather, row);
                    weather = Pick(row, stream.NextDouble(), maxWeather);
                    double windDraw = stream.NextDouble();
                    path.TickDay(day, weather, stream);
                    bool windOk = windDraw < WeatherSystem.WindSpeedBelowProbability(weather, operability.HelicopterMaxWindMs);
                    future[k - 1] = new FutureDay(path.HsAt(plotHsScale), path.VisibilityKmToday, windOk);
                }
                result[p] = future;
            }
            return result;
        }

        /// <summary>
        /// Replace each known event whose peak the forecast still estimates by
        /// a draw from its posterior (the truth stays in the game's sea).
        /// </summary>
        private static void RedrawUnknownPeaks(OffshoreMetocean path, OffshoreMetoceanCalibration cal, SimRngStream stream)
        {
            for (int i = 0; i < path.Events.Count; i++)
            {
                var e = path.Events[i];
                var (mean, sd) = MetoceanForecast.PeakPosterior(cal, e, path.LastTickDay);
                if (sd == 0.0) continue;
                double z = mean + sd * MetoceanMath.UniformToNormal(stream.NextDouble());
                double median = cal.SwellMedianM(SimCalendar.MonthIndex(e.CreatedDay));
                path.Events[i] = e with { PeakHsM = OffshoreMetoceanModel.SwellPeak(cal, median, z) };
            }
        }

        private static WeatherState Pick(double[] row, double u, WeatherState maxWeather)
        {
            double cumulative = 0.0;
            for (int s = 0; s < row.Length; s++)
            {
                cumulative += row[s];
                if (u < cumulative) return (WeatherState)s;
            }
            return maxWeather;
        }

        /// <summary>Whether the operation could start on a sampled day.</summary>
        public static bool Possible(in FutureDay day, OffshoreOp op, VesselClass vessel, OffshoreOperabilityCalibration operability)
        {
            double? limit = OperabilityWindows.HsLimitM(op, vessel, operability);
            if (limit == null || day.HsM > limit.Value) return false;
            if (vessel == VesselClass.Helicopter)
                return day.HelicopterWind && day.VisibilityKm >= operability.HelicopterMinVisibilityKm;
            return true;
        }

        /// <summary>A stable 64-bit key of a preview question (plot, operation, vessel, booked day).</summary>
        public static ulong QuestionKey(string plotId, OffshoreOp op, VesselClass vessel, int bookedDay)
        {
            ulong h = 0xcbf29ce484222325UL;
            foreach (char c in plotId ?? "")
            {
                h ^= c;
                h *= 0x100000001b3UL;
            }
            h = SimRng.Mix(h ^ (ulong)(int)op);
            h = SimRng.Mix(h ^ ((ulong)(int)vessel << 8));
            return SimRng.Mix(h ^ (ulong)(uint)bookedDay);
        }
    }

    /// <summary>
    /// A booking preview (spec §6.5 "Booking preview"): the charter's expected
    /// cost including slipped days, P(done by day X) day by day, the silo's
    /// days of feed on the booked day, and the chance the operation runs on
    /// the booked day itself.
    /// </summary>
    /// <param name="BookedDay">The day the vessel is booked for.</param>
    /// <param name="ProbabilityOnBookedDay">P(the operation can run on the booked day).</param>
    /// <param name="DoneByDay">P(done by day BookedDay + i), i = 0 … the preview's horizon.</param>
    /// <param name="ExpectedChargedDays">Expected days of charter until done (capped at the horizon).</param>
    /// <param name="ExpectedCostNok">Expected charter and per-operation price (NOK, the company's share).</param>
    /// <param name="DayRateNok">The vessel's day rate (NOK, the company's share).</param>
    /// <param name="NotDoneWithinHorizon">P(not done within the horizon).</param>
    /// <param name="SiloDaysOnBookedDay">The silo's days of feed on the booked day at the recent rate (+∞ when it feeds nothing).</param>
    public sealed record OffshoreBookingPreview(int BookedDay, double ProbabilityOnBookedDay, IReadOnlyList<double> DoneByDay,
        double ExpectedChargedDays, decimal ExpectedCostNok, decimal DayRateNok, double NotDoneWithinHorizon, double SiloDaysOnBookedDay);
}
