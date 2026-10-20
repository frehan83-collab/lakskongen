using System;
using System.Collections.Generic;
using LakskongenSim.Calibration;
using LakskongenSim.Core;

namespace LakskongenSim.Environment
{
    /// <summary>
    /// A storm-swell event (spec §3.2): created on <see cref="CreatedDay"/> and
    /// known to the forecast from then on, it ramps up on <see cref="OnsetDay"/>,
    /// holds <see cref="PeakHsM"/> (reference scale) for <see cref="DurationDays"/>
    /// and ramps down the day after. <see cref="EstimateLogError"/> is the
    /// standard normal error of the forecast's estimate of the peak, in the
    /// peak's latent units; the forecast scales it by min(1, lead / full-error
    /// lead), so it shrinks as the onset nears and is gone on the onset day.
    /// </summary>
    public sealed record SwellEvent(int Serial, int CreatedDay, int OnsetDay, int DurationDays,
        double PeakHsM, double PeakTpS, double EstimateLogError)
    {
        /// <summary>The ramp-down day, the last day the event raises the sea.</summary>
        public int LastActiveDay() => OnsetDay + DurationDays + 1;

        /// <summary>
        /// The event's swell on <paramref name="day"/> as a share of its peak:
        /// the ramp fraction on the onset and ramp-down days, 1 on the plateau, 0
        /// outside.
        /// </summary>
        public double ShapeOn(int day, double rampFraction)
        {
            if (day < OnsetDay || day > LastActiveDay()) return 0.0;
            return day == OnsetDay || day == LastActiveDay() ? rampFraction : 1.0;
        }
    }

    /// <summary>
    /// The offshore sea of one game (roadmap F2-06, docs/FARM_TYPES_SPEC.md §3.3
    /// "Metocean"; one per game in GameState.Metocean once F2-02/F2-03 wire it).
    /// Each day <see cref="TickDay"/> draws exactly <see cref="UniformsPerDay"/>
    /// uniforms from its own stream (<see cref="StreamScope"/>), always, in a
    /// fixed order, and turns them into the day by inverse CDF:
    /// <list type="number">
    /// <item>u0: the base sea's AR(1) innovation. The base is
    /// T⁻¹(μ_month + Δ_weather + X), X_t = φ·X_{t−1} + σ_ε·Φ⁻¹(u0), with T the sea transform
    /// (<see cref="MetoceanMath.UpperBoxCox"/>: lognormal below 1 m, a thinner power tail above);
    /// Δ couples it to the fjord weather chain, which it reads and never draws.</item>
    /// <item>u1: whether a storm-swell event is created today (monthly rate).</item>
    /// <item>u2-u6: the new event's onset (4-7 days ahead), peak (sea transform around
    /// the month's median), plateau length, peak period and the forecast
    /// estimate's error. They are drawn on every day and used only when u1
    /// creates an event.</item>
    /// <item>u7: the day's visibility (it gates the helicopter).</item>
    /// </list>
    /// A plot's Hs is its scale × √(base² + background² + Σ swell²): the
    /// day's wind sea, the ever-present background swell and the storm swell
    /// add in energy, as do overlapping events. The fjord weather stream is never touched. Everything the
    /// forecast needs is in the saved state, so <see cref="MetoceanForecast"/>
    /// is the exact conditional distribution and never draws.
    /// </summary>
    public sealed class OffshoreMetocean
    {
        /// <summary>The SimRng scope of the metocean stream (moves to SimRngStreams when F2-02 wires the tick).</summary>
        public const string StreamScope = "offshore_metocean";

        /// <summary>Uniforms drawn every day, whatever happens (spec §3.3).</summary>
        public const int UniformsPerDay = 8;

        // ─── Saved state ───

        /// <summary>The last day <see cref="TickDay"/> ran (−1 before the first tick).</summary>
        public int LastTickDay { get; set; } = -1;

        /// <summary>The base sea's AR(1) residual X (sea-transform units).</summary>
        public double ResidualX { get; set; }

        /// <summary>Known swell events: created and not yet past their ramp-down day.</summary>
        public List<SwellEvent> Events { get; set; } = new();

        /// <summary>Serial of the next event.</summary>
        public int NextSerial { get; set; } = 1;

        /// <summary>The fjord weather state today's base sea was drawn under.</summary>
        public WeatherState WeatherToday { get; set; } = WeatherState.Moderate;

        /// <summary>Today's base sea (m, reference scale).</summary>
        public double BaseHsToday { get; set; }

        /// <summary>Today's swell, the root sum of squares of the active events (m, reference scale).</summary>
        public double SwellHsToday { get; set; }

        /// <summary>Peak period of today's strongest swell (s; 0 without swell).</summary>
        public double SwellTpToday { get; set; }

        /// <summary>Today's visibility (km).</summary>
        public double VisibilityKmToday { get; set; }

        private OffshoreMetoceanCalibration _calibration = OffshoreCalibration.Default.Metocean;

        /// <summary>The calibration the model draws with (set by the engine; not saved).</summary>
        public OffshoreMetoceanCalibration Calibration() => _calibration;

        /// <summary>Use the calibration the engine loaded (the registry default otherwise).</summary>
        public void ApplyCalibration(OffshoreMetoceanCalibration calibration)
            => _calibration = calibration ?? throw new ArgumentNullException(nameof(calibration));

        /// <summary>
        /// A copy of the saved state and the calibration (F2-07's booking
        /// previews sample futures on it with their own stream; the game's sea
        /// and its stream are never touched). The swell events are immutable
        /// records, shared.
        /// </summary>
        internal OffshoreMetocean CopyForSampling()
        {
            var copy = new OffshoreMetocean
            {
                LastTickDay = LastTickDay,
                ResidualX = ResidualX,
                Events = new List<SwellEvent>(Events),
                NextSerial = NextSerial,
                WeatherToday = WeatherToday,
                BaseHsToday = BaseHsToday,
                SwellHsToday = SwellHsToday,
                SwellTpToday = SwellTpToday,
                VisibilityKmToday = VisibilityKmToday,
            };
            copy._calibration = _calibration;
            return copy;
        }

        /// <summary>
        /// Today's sea at the reference scale (m): the base sea, the background
        /// swell and the storm swell added in energy.
        /// </summary>
        public double ReferenceHsToday() => OffshoreMetoceanModel.CombineHs(_calibration, BaseHsToday, SwellHsToday * SwellHsToday);

        /// <summary>Today's Hs at a plot of scale <paramref name="plotHsScale"/> (m).</summary>
        public double HsAt(double plotHsScale) => plotHsScale * ReferenceHsToday();

        /// <summary>
        /// Today's peak period at a plot (s): the wind sea's
        /// <see cref="OffshoreMetoceanCalibration.WindSeaTpPerSqrtHs"/> × √Hs or the
        /// swell's period, whichever is longer (spec §3.3).
        /// </summary>
        public double TpAt(double plotHsScale)
            => Math.Max(_calibration.WindSeaTpPerSqrtHs * Math.Sqrt(HsAt(plotHsScale)), SwellTpToday);

        /// <summary>
        /// Advance the sea to <paramref name="day"/> under today's fjord weather.
        /// Draws exactly <see cref="UniformsPerDay"/> uniforms from
        /// <paramref name="rng"/> (the <see cref="StreamScope"/> stream) and
        /// nothing else. Days must increase.
        /// </summary>
        public void TickDay(int day, WeatherState weather, SimRngStream rng)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (day <= LastTickDay)
                throw new InvalidOperationException($"Metocean already ticked day {LastTickDay}; day {day} is not later");

            // Always the same eight draws, in this order (spec §3.3).
            double u0 = rng.NextDouble();
            double u1 = rng.NextDouble();
            double u2 = rng.NextDouble();
            double u3 = rng.NextDouble();
            double u4 = rng.NextDouble();
            double u5 = rng.NextDouble();
            double u6 = rng.NextDouble();
            double u7 = rng.NextDouble();

            var cal = _calibration;
            int month = SimCalendar.MonthIndex(day);

            ResidualX = cal.ArPhi * ResidualX + cal.ArSigma * MetoceanMath.UniformToNormal(u0);
            WeatherToday = weather;
            BaseHsToday = OffshoreMetoceanModel.BaseHs(cal, OffshoreMetoceanModel.BaseLevel(cal, month, weather) + ResidualX);

            Events.RemoveAll(e => e.LastActiveDay() < day);
            if (u1 < cal.SwellRatePerDay[month])
            {
                double median = cal.SwellMedianM(month);
                Events.Add(new SwellEvent(
                    Serial: NextSerial++,
                    CreatedDay: day,
                    OnsetDay: day + OffshoreMetoceanModel.OnsetOffsetDays(cal, u2),
                    DurationDays: OffshoreMetoceanModel.PlateauDays(cal, u4),
                    PeakHsM: OffshoreMetoceanModel.SwellPeak(cal, median, MetoceanMath.UniformToNormal(u3)),
                    PeakTpS: cal.SwellTpMinS + u5 * (cal.SwellTpMaxS - cal.SwellTpMinS),
                    EstimateLogError: MetoceanMath.UniformToNormal(u6)));
            }

            double energy = 0.0, strongest = 0.0, tp = 0.0;
            foreach (var e in Events)
            {
                double s = e.ShapeOn(day, cal.SwellRampFraction) * e.PeakHsM;
                if (s <= 0.0) continue;
                energy += s * s;
                if (s > strongest)
                {
                    strongest = s;
                    tp = e.PeakTpS;
                }
            }
            SwellHsToday = Math.Sqrt(energy);
            SwellTpToday = tp;
            VisibilityKmToday = OffshoreMetoceanModel.VisibilityKm(cal, month, u7);
            LastTickDay = day;
        }
    }

    /// <summary>
    /// The pure pieces of the metocean model that the tick, the forecast and the
    /// fit share, so the three can never disagree (F2-06).
    /// </summary>
    public static class OffshoreMetoceanModel
    {
        /// <summary>
        /// The reference sea (m) of a base sea and a storm-swell energy: √(base² +
        /// background² + swell energy), the one place the parts are combined.
        /// </summary>
        public static double CombineHs(OffshoreMetoceanCalibration cal, double baseHs, double swellEnergy)
            => Math.Sqrt(baseHs * baseHs + cal.BackgroundSwellHsM * cal.BackgroundSwellHsM + swellEnergy);

        /// <summary>The base sea's level in the sea transform before the residual: μ_month + Δ_weather.</summary>
        public static double BaseLevel(OffshoreMetoceanCalibration cal, int monthIndex, WeatherState weather)
            => cal.BaseMuMonth[monthIndex] + cal.WeatherDelta[weather];

        /// <summary>The base sea (m, reference scale) of a value in the sea transform (<see cref="MetoceanMath.UpperBoxCox"/>).</summary>
        public static double BaseHs(OffshoreMetoceanCalibration cal, double latent)
            => MetoceanMath.UpperBoxCoxInverse(latent, cal.BaseBoxCoxLambda);

        /// <summary>
        /// The spread of a swell peak in the sea transform for a month with
        /// median <paramref name="median"/>: <see cref="OffshoreMetoceanCalibration.SwellSigmaLn"/>
        /// (ln units) times the transform's slope per ln unit at the median.
        /// </summary>
        public static double SwellSigmaTransformed(OffshoreMetoceanCalibration cal, double median)
            => cal.SwellSigmaLn * MetoceanMath.UpperBoxCoxSlopePerLn(median, cal.SwellBoxCoxLambda);

        /// <summary>A swell peak (m, reference scale) at latent standard normal <paramref name="z"/>.</summary>
        public static double SwellPeak(OffshoreMetoceanCalibration cal, double median, double z)
            => MetoceanMath.UpperBoxCoxInverse(MetoceanMath.UpperBoxCox(median, cal.SwellBoxCoxLambda)
                + SwellSigmaTransformed(cal, median) * z, cal.SwellBoxCoxLambda);

        /// <summary>The latent standard normal of a swell peak (inverse of <see cref="SwellPeak"/>).</summary>
        public static double SwellLatent(OffshoreMetoceanCalibration cal, double median, double peak)
        {
            if (peak <= 0.0) return double.NegativeInfinity;
            return (MetoceanMath.UpperBoxCox(peak, cal.SwellBoxCoxLambda) - MetoceanMath.UpperBoxCox(median, cal.SwellBoxCoxLambda))
                / SwellSigmaTransformed(cal, median);
        }

        /// <summary>Days from creation to onset for a uniform draw (each lead equally likely).</summary>
        public static int OnsetOffsetDays(OffshoreMetoceanCalibration cal, double u)
            => cal.SwellLeadDaysMin + UniformIndex(u, cal.SwellLeadDaysMax - cal.SwellLeadDaysMin + 1);

        /// <summary>Plateau days for a uniform draw (each length equally likely).</summary>
        public static int PlateauDays(OffshoreMetoceanCalibration cal, double u)
            => cal.SwellDurationDaysMin + UniformIndex(u, cal.SwellDurationDaysMax - cal.SwellDurationDaysMin + 1);

        private static int UniformIndex(double u, int count) => Math.Min(count - 1, (int)(u * count));

        /// <summary>
        /// Visibility (km) for a uniform draw: the month's low-visibility share of
        /// draws spreads evenly from the worst low visibility up to the threshold,
        /// the rest evenly from the threshold up to the clearest.
        /// </summary>
        public static double VisibilityKm(OffshoreMetoceanCalibration cal, int monthIndex, double u)
        {
            double low = cal.LowVisibilityDayShare[monthIndex];
            if (u < low)
                return cal.LowVisibilityMinKm + (cal.LowVisibilityKm - cal.LowVisibilityMinKm) * (u / low);
            return cal.LowVisibilityKm + (cal.ClearVisibilityMaxKm - cal.LowVisibilityKm) * ((u - low) / (1.0 - low));
        }

        /// <summary>Probability that a day of the month has visibility below <paramref name="km"/> (the CDF of <see cref="VisibilityKm"/>).</summary>
        public static double VisibilityBelowProbability(OffshoreMetoceanCalibration cal, int monthIndex, double km)
        {
            double low = cal.LowVisibilityDayShare[monthIndex];
            if (km <= cal.LowVisibilityMinKm) return 0.0;
            if (km <= cal.LowVisibilityKm)
                return low * (km - cal.LowVisibilityMinKm) / (cal.LowVisibilityKm - cal.LowVisibilityMinKm);
            if (km >= cal.ClearVisibilityMaxKm) return 1.0;
            return low + (1.0 - low) * (km - cal.LowVisibilityKm) / (cal.ClearVisibilityMaxKm - cal.LowVisibilityKm);
        }
    }
}
