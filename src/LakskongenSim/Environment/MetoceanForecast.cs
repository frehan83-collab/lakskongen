using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using LakskongenSim.Calibration;
using LakskongenSim.Core;

namespace LakskongenSim.Environment
{
    /// <summary>One forecast day: the plot's Hs quantiles and P(Hs ≤ limit) for the limits asked for.</summary>
    public sealed record SeaStateForecastDay(int Day, int LeadDays, double HsP10M, double HsP50M, double HsP90M,
        IReadOnlyList<double> LimitsM, IReadOnlyList<double> ProbabilityAtOrBelow, double UnresolvedProbability);

    /// <summary>
    /// A known swell event as the forecast shows it: its timing (known exactly)
    /// and the posterior of its peak at the plot given today's noisy estimate.
    /// </summary>
    public sealed record SwellOutlook(int Serial, int CreatedDay, int OnsetDay, int DurationDays,
        double PeakP10M, double PeakP50M, double PeakP90M, double PeakTpS, bool PeakKnown);

    /// <summary>
    /// Issues <see cref="SeaStateForecast"/>s (spec §3.3 point 6). Pure: reads
    /// the metocean state and the weather chain, never draws, never writes.
    /// <para>For a target day T = t + k the forecast is exact given:</para>
    /// <list type="bullet">
    /// <item>the base sea: a mixture over the fjord weather state on T
    /// (<see cref="WeatherSystem.StateDistribution(int)"/>, the sampler's own
    /// transition rows) of the AR(1)'s Gaussian at lead k in the sea transform,
    /// mapped back as the tick does;</item>
    /// <item>known events: their timing exactly, and the posterior of each peak
    /// given its estimate, whose latent error is the event's
    /// <see cref="SwellEvent.EstimateLogError"/> × min(1, days to onset / full-error
    /// lead) against a standard normal prior (a conjugate normal update);</item>
    /// <item>events not yet created: every creation day between tomorrow and
    /// T − the shortest lead, with each onset and plateau length the tick can
    /// draw, at the month's rate and peak distribution.</item>
    /// </list>
    /// P(Hs ≤ L) = P(base² + background² + Σ swell² ≤ (L/scale)²). The base
    /// enters in closed form; the background swell and swells whose peak is
    /// known (on and after their onset) subtract their energy; the rest are integrated over their latent normals: one or two by
    /// composite 8-point Gauss-Legendre panels that refine toward the point where
    /// the swell alone reaches the limit, and in a combination of at least
    /// <see cref="QuadratureMinProbability"/> two by nested panels, three or
    /// more with the one nearest the limit on panels and the others by tensor
    /// Gauss-Hermite; all rarer combinations (together well under 10 % of the
    /// probability) by one Hammersley rule over their mixture (MetoceanForecastAccuracyTests holds the sum against a
    /// high-resolution reference and against simulated futures). Combinations of more than <see cref="MaxSwellComponents"/>
    /// uncertain swells, or below <see cref="NegligibleCombination"/>, are
    /// counted as exceeding every limit and reported as
    /// <see cref="SeaStateForecastDay.UnresolvedProbability"/>.
    /// </summary>
    public static partial class MetoceanForecast
    {
        /// <summary>Most simultaneous uncertain swell components integrated.</summary>
        public const int MaxSwellComponents = 6;

        /// <summary>Points of the Hammersley rule over the tail combinations.</summary>
        public const int HaltonPoints = 1024;

        /// <summary>
        /// Combinations of two or more uncertain swells at least this likely
        /// are integrated on their own (two by nested Gauss-Legendre, more with
        /// the one nearest the limit by Gauss-Legendre and the rest by tensor
        /// Gauss-Hermite); rarer ones join the tail's Hammersley rule.
        /// </summary>
        public const double QuadratureMinProbability = 0.02;

        /// <summary>Combinations below this probability are not integrated (counted as above every limit).</summary>
        public const double NegligibleCombination = 1e-9;

        /// <summary>The 10/50/90 % quantiles every forecast day carries.</summary>
        private static readonly double[] DisplayQuantiles = { 0.1, 0.5, 0.9 };

        private const double ZetaMax = 8.0;
        private const double RefineBelowZeta = 5.0;
        private const double QuantileLowM = 0.005;
        private const double QuantileHighM = 60.0;
        private const double QuantileTolLn = 1e-6;
        private const int QuantileMaxIterations = 100;
        private const int WeatherStates = 4;
        private const double Z90 = 1.2815515655446004;
        // Refined panels for the innermost swell and the outer one of a pair, as distances
        // below the point where that swell alone reaches the limit (after the even panels).
        private static readonly double[] InnerSteepOffsets = { 3.0, 1.0, 0.3, 0.08 };
        private static readonly double[] OuterSteepOffsets = { 1.5, 0.3 };
        private static readonly double[] FlatPanelEdges = { -ZetaMax, -4.0, 0.0, 4.0, ZetaMax };
        private static readonly int[] HaltonBases = { 2, 3, 5, 7, 11, 13 };
        private static readonly double[][] HaltonNormals = BuildHaltonNormals();

        /// <summary>
        /// The Halton points as standard normals in antithetic pairs: point 2i is
        /// Φ⁻¹ of the radical inverse of i + 1 in each base, point 2i + 1 its
        /// mirror. The pairs make every rule over a prefix of even length
        /// symmetric, which removes the one-sided offset of the radical
        /// inverse's grid (its points sit at the left ends of their strata).
        /// </summary>
        private static double[][] BuildHaltonNormals()
        {
            var z = new double[HaltonBases.Length][];
            for (int d = 0; d < HaltonBases.Length; d++)
            {
                z[d] = new double[HaltonPoints];
                for (int i = 0; i < HaltonPoints / 2; i++)
                {
                    double normal = MetoceanMath.NormalQuantile(RadicalInverse(i + 1, HaltonBases[d]));
                    z[d][2 * i] = normal;
                    z[d][2 * i + 1] = -normal;
                }
            }
            return z;
        }

        private static double RadicalInverse(int index, int b)
        {
            double inv = 1.0 / b, f = inv, r = 0.0;
            while (index > 0)
            {
                r += f * (index % b);
                index /= b;
                f *= inv;
            }
            return r;
        }

        /// <summary>
        /// Issue the forecast at a plot from the game's weather system (its state
        /// must be the metocean's last tick day).
        /// </summary>
        public static SeaStateForecast Issue(OffshoreMetocean state, WeatherSystem weather, double plotHsScale,
            IReadOnlyList<double> limitsM, bool withQuantiles = true)
        {
            if (weather == null) throw new ArgumentNullException(nameof(weather));
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (weather.SimulationDay != state.LastTickDay)
                throw new InvalidOperationException($"Weather is on day {weather.SimulationDay}, the sea on day {state.LastTickDay}");
            return Issue(state, k => weather.StateDistribution(k), plotHsScale, limitsM, withQuantiles);
        }

        /// <summary>
        /// Issue the forecast with the weather state distribution at each lead
        /// supplied by <paramref name="weatherAtLead"/> (k ↦ distribution indexed
        /// by <see cref="WeatherState"/>).
        /// </summary>
        public static SeaStateForecast Issue(OffshoreMetocean state, Func<int, double[]> weatherAtLead, double plotHsScale,
            IReadOnlyList<double> limitsM, bool withQuantiles = true)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (weatherAtLead == null) throw new ArgumentNullException(nameof(weatherAtLead));
            if (!(plotHsScale > 0.0)) throw new ArgumentOutOfRangeException(nameof(plotHsScale), plotHsScale, "Scale must be positive");
            if (state.LastTickDay < 0) throw new InvalidOperationException("The sea has not ticked yet");
            var limits = (limitsM ?? Array.Empty<double>()).ToArray();
            var cal = state.Calibration();
            int horizon = cal.ForecastHorizonDays;
            var models = new DayModel[horizon];
            var weatherByLead = new double[horizon][];
            var days = new SeaStateForecastDay[horizon];
            for (int k = 1; k <= horizon; k++)
            {
                weatherByLead[k - 1] = (double[])weatherAtLead(k).Clone();
                var model = BuildDay(state, cal, weatherByLead[k - 1], k);
                models[k - 1] = model;
                var probs = new double[limits.Length];
                for (int i = 0; i < limits.Length; i++)
                    probs[i] = model.Cdf(limits[i] / plotHsScale);
                double q10 = double.NaN, q50 = double.NaN, q90 = double.NaN;
                if (withQuantiles)
                {
                    q10 = plotHsScale * model.Quantile(DisplayQuantiles[0]);
                    q50 = plotHsScale * model.Quantile(DisplayQuantiles[1]);
                    q90 = plotHsScale * model.Quantile(DisplayQuantiles[2]);
                }
                days[k - 1] = new SeaStateForecastDay(state.LastTickDay + k, k, q10, q50, q90, limits, probs, model.Unresolved);
            }
            var known = state.Events
                .OrderBy(e => e.OnsetDay).ThenBy(e => e.Serial)
                .Select(e => Outlook(cal, e, state.LastTickDay, plotHsScale))
                .ToArray();
            return new SeaStateForecast(state.LastTickDay, plotHsScale, state.HsAt(plotHsScale), days, known, models, weatherByLead);
        }

        /// <summary>
        /// P(Hs ≤ <paramref name="limitM"/>) at a plot on the day
        /// <paramref name="leadDays"/> after the sea's last tick, the weather on
        /// that day being distributed as <paramref name="weatherAtLead"/>.
        /// </summary>
        public static double ProbabilityAtOrBelow(OffshoreMetocean state, double[] weatherAtLead, int leadDays,
            double plotHsScale, double limitM)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (leadDays < 1) throw new ArgumentOutOfRangeException(nameof(leadDays), leadDays, "Lead must be at least one day");
            return BuildDay(state, state.Calibration(), weatherAtLead, leadDays).Cdf(limitM / plotHsScale);
        }

        // ─── The posterior of a known event's peak ───

        /// <summary>Latent units of the estimate error at a lead (0 on and after the onset).</summary>
        internal static double EstimateErrorScale(OffshoreMetoceanCalibration cal, int daysToOnset)
            => daysToOnset <= 0 ? 0.0 : Math.Min(1.0, (double)daysToOnset / cal.ForecastFullErrorLeadDays);

        /// <summary>The posterior (mean, sd) of a known event's latent peak on <paramref name="issueDay"/>.</summary>
        internal static (double Mean, double Sd) PeakPosterior(OffshoreMetoceanCalibration cal, SwellEvent e, int issueDay)
        {
            double median = cal.SwellMedianM(SimCalendar.MonthIndex(e.CreatedDay));
            double zTrue = OffshoreMetoceanModel.SwellLatent(cal, median, e.PeakHsM);
            double tau = EstimateErrorScale(cal, e.OnsetDay - issueDay);
            if (tau == 0.0) return (zTrue, 0.0);
            double estimate = zTrue + tau * e.EstimateLogError;
            double precision = 1.0 + tau * tau;
            return (estimate / precision, tau / Math.Sqrt(precision));
        }

        private static SwellOutlook Outlook(OffshoreMetoceanCalibration cal, SwellEvent e, int issueDay, double scale)
        {
            double median = cal.SwellMedianM(SimCalendar.MonthIndex(e.CreatedDay));
            var (mean, sd) = PeakPosterior(cal, e, issueDay);
            if (sd == 0.0)
            {
                double known = scale * e.PeakHsM;
                return new SwellOutlook(e.Serial, e.CreatedDay, e.OnsetDay, e.DurationDays, known, known, known, e.PeakTpS, true);
            }
            return new SwellOutlook(e.Serial, e.CreatedDay, e.OnsetDay, e.DurationDays,
                scale * OffshoreMetoceanModel.SwellPeak(cal, median, mean - Z90 * sd),
                scale * OffshoreMetoceanModel.SwellPeak(cal, median, mean),
                scale * OffshoreMetoceanModel.SwellPeak(cal, median, mean + Z90 * sd),
                e.PeakTpS, false);
        }

        /// <summary>Build the distribution of the reference sea on the day <paramref name="lead"/> after the last tick.</summary>
        internal static DayModel BuildDay(OffshoreMetocean state, OffshoreMetoceanCalibration cal, double[] weatherAtLead, int lead)
        {
            if (weatherAtLead == null || weatherAtLead.Length != WeatherStates)
                throw new ArgumentException("The weather distribution needs one probability per weather state", nameof(weatherAtLead));
            int issue = state.LastTickDay;
            int target = issue + lead;
            int month = SimCalendar.MonthIndex(target);

            // Base: the AR(1) residual at lead k is Gaussian around φ^k X_t.
            double phiK = Math.Pow(cal.ArPhi, lead);
            double sd = cal.ArSigma * Math.Sqrt((1.0 - phiK * phiK) / (1.0 - cal.ArPhi * cal.ArPhi));
            int live = 0;
            for (int w = 0; w < WeatherStates; w++)
                if (weatherAtLead[w] > 0.0) live++;
            var means = new double[live];
            var weights = new double[live];
            for (int w = 0, i = 0; w < WeatherStates; w++)
            {
                if (!(weatherAtLead[w] > 0.0)) continue;
                means[i] = OffshoreMetoceanModel.BaseLevel(cal, month, (WeatherState)w) + phiK * state.ResidualX;
                weights[i] = weatherAtLead[w];
                i++;
            }

            // The background swell and known events active on the target day: a
            // known peak adds its energy, an estimated one is integrated.
            double knownEnergy = cal.BackgroundSwellHsM * cal.BackgroundSwellHsM;
            var uncertainKnown = new List<SwellComponent>();
            foreach (var e in state.Events)
            {
                double shape = e.ShapeOn(target, cal.SwellRampFraction);
                if (shape <= 0.0) continue;
                var (mean, posteriorSd) = PeakPosterior(cal, e, issue);
                if (posteriorSd == 0.0)
                {
                    double s = shape * e.PeakHsM;
                    knownEnergy += s * s;
                    continue;
                }
                double median = cal.SwellMedianM(SimCalendar.MonthIndex(e.CreatedDay));
                uncertainKnown.Add(new SwellComponent(cal, shape, median, mean, posteriorSd));
            }

            // Events not created yet, enumerated over creation day, onset and plateau.
            var terms = new List<(double, SwellComponent[])>();
            double unresolved = 0.0;
            foreach (var combo in UnknownCombinationsCached(cal, issue, lead))
            {
                int count = uncertainKnown.Count + combo.Components.Length;
                if (count > MaxSwellComponents || combo.Probability < NegligibleCombination)
                {
                    unresolved += combo.Probability;
                    continue;
                }
                var comps = new SwellComponent[count];
                for (int i = 0; i < uncertainKnown.Count; i++)
                    comps[i] = uncertainKnown[i];
                for (int i = 0; i < combo.Components.Length; i++)
                    comps[uncertainKnown.Count + i] = new SwellComponent(cal, combo.Components[i].Shape, combo.Components[i].Median, 0.0, 1.0);
                terms.Add((combo.Probability, comps));
            }
            return new DayModel(weights, means, sd, cal.BaseBoxCoxLambda, knownEnergy, terms, unresolved);
        }

        private static readonly ConditionalWeakTable<OffshoreMetoceanCalibration, ConcurrentDictionary<int, UnknownCombination[]>> CombinationCache = new();

        /// <summary>
        /// <see cref="UnknownEventCombinations"/> for an issue day and lead; they
        /// depend only on the calendar (day of year) and the calibration, so they
        /// are computed once per calibration, day of year and lead.
        /// </summary>
        internal static UnknownCombination[] UnknownCombinationsCached(OffshoreMetoceanCalibration cal, int issueDay, int lead)
        {
            var table = CombinationCache.GetValue(cal, _ => new ConcurrentDictionary<int, UnknownCombination[]>());
            int key = SimCalendar.DayOfYear(issueDay) * 64 + lead;
            return table.GetOrAdd(key, _ =>
            {
                int start = SimCalendar.DayOfYear(issueDay);
                return UnknownEventCombinations(cal, start, start + lead)
                    .Select(c => new UnknownCombination(c.P, c.Comps.ToArray()))
                    .ToArray();
            });
        }

        /// <summary>
        /// The joint distribution of the swell events created after
        /// <paramref name="issueDay"/> that are active on <paramref name="targetDay"/>:
        /// each combination's probability and its components (shape, month median).
        /// Creation days of one month share a median, so their events are counted
        /// by shape; months combine independently.
        /// </summary>
        internal static List<(double P, List<(double Shape, double Median)> Comps)> UnknownEventCombinations(
            OffshoreMetoceanCalibration cal, int issueDay, int targetDay)
        {
            int leads = cal.SwellLeadDaysMax - cal.SwellLeadDaysMin + 1;
            int plateaus = cal.SwellDurationDaysMax - cal.SwellDurationDaysMin + 1;
            double each = 1.0 / (leads * plateaus);
            // Per month of creation: distribution over (ramps, plateaus) active on the target day.
            var groups = new List<(double Median, Dictionary<(int R, int P), double> Dist)>();
            for (int created = issueDay + 1; created <= targetDay - cal.SwellLeadDaysMin; created++)
            {
                int month = SimCalendar.MonthIndex(created);
                double rate = cal.SwellRatePerDay[month];
                double qRamp = 0.0, qPlateau = 0.0;
                for (int o = cal.SwellLeadDaysMin; o <= cal.SwellLeadDaysMax; o++)
                {
                    int onset = created + o;
                    if (onset > targetDay) continue;
                    for (int d = cal.SwellDurationDaysMin; d <= cal.SwellDurationDaysMax; d++)
                    {
                        int last = onset + d + 1;
                        if (targetDay > last) continue;
                        if (targetDay == onset || targetDay == last) qRamp += each;
                        else qPlateau += each;
                    }
                }
                qRamp *= rate;
                qPlateau *= rate;
                double median = cal.SwellMedianM(month);
                if (groups.Count == 0 || groups[^1].Median != median)
                    groups.Add((median, new Dictionary<(int, int), double> { [(0, 0)] = 1.0 }));
                var dist = groups[^1].Dist;
                var next = new Dictionary<(int, int), double>();
                foreach (var kv in dist.OrderBy(kv => kv.Key.R).ThenBy(kv => kv.Key.P))
                {
                    Add(next, kv.Key, kv.Value * (1.0 - qRamp - qPlateau));
                    if (qRamp > 0.0) Add(next, (kv.Key.R + 1, kv.Key.P), kv.Value * qRamp);
                    if (qPlateau > 0.0) Add(next, (kv.Key.R, kv.Key.P + 1), kv.Value * qPlateau);
                }
                groups[^1] = (median, next);
            }

            var result = new List<(double, List<(double, double)>)> { (1.0, new List<(double, double)>()) };
            foreach (var (median, dist) in groups)
            {
                var combined = new List<(double, List<(double, double)>)>();
                foreach (var (p, comps) in result)
                    foreach (var kv in dist.OrderBy(kv => kv.Key.R).ThenBy(kv => kv.Key.P))
                    {
                        var list = new List<(double, double)>(comps);
                        for (int i = 0; i < kv.Key.R; i++) list.Add((cal.SwellRampFraction, median));
                        for (int i = 0; i < kv.Key.P; i++) list.Add((1.0, median));
                        combined.Add((p * kv.Value, list));
                    }
                result = combined;
            }
            return result;
        }

        private static void Add(Dictionary<(int, int), double> d, (int, int) key, double value)
            => d[key] = d.TryGetValue(key, out var v) ? v + value : value;
    }
}
