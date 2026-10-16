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
    /// The honest sea-state forecast of one plot (roadmap F2-06, spec §3.3 point
    /// 6), issued after the day's tick: for each of the next
    /// <see cref="OffshoreMetoceanCalibration.ForecastHorizonDays"/> days the
    /// exact conditional distribution of the plot's Hs given what the game knows
    /// today. Built by <see cref="MetoceanForecast.Issue(OffshoreMetocean, WeatherSystem, double, IReadOnlyList{double}, bool)"/>;
    /// further limits and quantiles can be asked of it without reissuing.
    /// </summary>
    public sealed class SeaStateForecast
    {
        private readonly MetoceanForecast.DayModel[] _models;
        private readonly double[][] _weatherByLead;

        internal SeaStateForecast(int issuedDay, double plotHsScale, double hsToday, SeaStateForecastDay[] days,
            SwellOutlook[] knownSwell, MetoceanForecast.DayModel[] models, double[][] weatherByLead)
        {
            IssuedDay = issuedDay;
            PlotHsScale = plotHsScale;
            HsTodayM = hsToday;
            Days = days;
            KnownSwell = knownSwell;
            _models = models;
            _weatherByLead = weatherByLead;
        }

        /// <summary>The day the forecast was issued (after that day's tick).</summary>
        public int IssuedDay { get; }

        /// <summary>The plot's Hs scale (its sea over the reference sea).</summary>
        public double PlotHsScale { get; }

        /// <summary>Today's Hs at the plot (m), known exactly.</summary>
        public double HsTodayM { get; }

        /// <summary>Lead 1 first.</summary>
        public IReadOnlyList<SeaStateForecastDay> Days { get; }

        /// <summary>Every swell event the game knows today, earliest onset first.</summary>
        public IReadOnlyList<SwellOutlook> KnownSwell { get; }

        /// <summary>Days ahead the forecast covers.</summary>
        public int HorizonDays => _models.Length;

        /// <summary>P(Hs ≤ <paramref name="limitM"/>) at the plot on the day <paramref name="leadDays"/> ahead (1 = tomorrow).</summary>
        public double ProbabilityAtOrBelow(int leadDays, double limitM)
            => Model(leadDays).Cdf(limitM / PlotHsScale);

        /// <summary>The <paramref name="p"/>-quantile of the plot's Hs (m) on the day <paramref name="leadDays"/> ahead.</summary>
        public double Quantile(int leadDays, double p) => PlotHsScale * Model(leadDays).Quantile(p);

        /// <summary>The fjord weather state distribution on the day <paramref name="leadDays"/> ahead (indexed by <see cref="WeatherState"/>).</summary>
        public IReadOnlyList<double> WeatherDistribution(int leadDays)
        {
            Model(leadDays);
            return _weatherByLead[leadDays - 1];
        }

        private MetoceanForecast.DayModel Model(int leadDays)
        {
            if (leadDays < 1 || leadDays > _models.Length)
                throw new ArgumentOutOfRangeException(nameof(leadDays), leadDays, $"Lead must be 1-{_models.Length} days");
            return _models[leadDays - 1];
        }
    }

    /// <summary>
    /// Issues <see cref="SeaStateForecast"/>s (spec §3.3 point 6). Pure: reads
    /// the metocean state and the weather chain, never draws, never writes.
    /// <para>For a target day T = t + k the forecast is exact given:</para>
    /// <list type="bullet">
    /// <item>the base sea: a mixture over the fjord weather state on T
    /// (<see cref="WeatherSystem.StateDistribution(int)"/>, the sampler's own
    /// transition rows) of the AR(1)'s Gaussian at lead k, BoxCox⁻¹-transformed
    /// and floored as the tick does;</item>
    /// <item>known events: their timing exactly, and the posterior of each peak
    /// given its estimate, whose latent error is the event's
    /// <see cref="SwellEvent.EstimateLogError"/> × min(1, days to onset / full-error
    /// lead) against a standard normal prior (a conjugate normal update);</item>
    /// <item>events not yet created: every creation day between tomorrow and
    /// T − the shortest lead, with each onset and plateau length the tick can
    /// draw, at the month's rate and peak distribution.</item>
    /// </list>
    /// P(Hs ≤ L) = P(base² + Σ swell² ≤ (L/scale)²). The base enters in closed
    /// form; swells whose peak is known (on and after their onset) subtract their
    /// energy; the rest are integrated over their latent normals: one or two by
    /// composite 8-point Gauss-Legendre panels that refine toward the point where
    /// the swell alone reaches the limit, three or more (combinations of well
    /// under 1 % of probability) by a fixed 1,024-point Halton rule
    /// (MetoceanForecastTests holds both against a brute-force reference).
    /// Combinations of more than <see cref="MaxSwellComponents"/> uncertain
    /// swells, or below 1e-12, are counted as exceeding every limit and reported
    /// as <see cref="SeaStateForecastDay.UnresolvedProbability"/>.
    /// </summary>
    public static class MetoceanForecast
    {
        /// <summary>Most simultaneous uncertain swell components integrated.</summary>
        public const int MaxSwellComponents = 6;

        /// <summary>Points of the Halton rule for three or more uncertain swells.</summary>
        public const int HaltonPoints = 1024;

        /// <summary>The 10/50/90 % quantiles every forecast day carries.</summary>
        private static readonly double[] DisplayQuantiles = { 0.1, 0.5, 0.9 };

        private const double ZetaMax = 8.0;
        private const double NegligibleCombo = 1e-12;
        private const double QuantileLowM = 0.005;
        private const double QuantileHighM = 60.0;
        private const double QuantileTolLn = 1e-7;
        private const int QuantileMaxIterations = 100;
        // Panels for the innermost swell (40 nodes) and the outer one of a pair (24 nodes),
        // as distances below the point where that swell alone reaches the limit.
        private static readonly double[] InnerSteepOffsets = { 3.0, 1.0, 0.3, 0.08 };
        private static readonly double[] OuterSteepOffsets = { 1.5, 0.3 };
        private static readonly double[] FlatPanelEdges = { -ZetaMax, -4.0, 0.0, 4.0, ZetaMax };
        private static readonly int[] HaltonBases = { 2, 3, 5, 7, 11, 13 };
        private static readonly double[][] HaltonNormals = BuildHaltonNormals();

        private static double[][] BuildHaltonNormals()
        {
            var z = new double[HaltonBases.Length][];
            for (int d = 0; d < HaltonBases.Length; d++)
            {
                z[d] = new double[HaltonPoints];
                for (int j = 0; j < HaltonPoints; j++)
                    z[d][j] = MetoceanMath.NormalQuantile(RadicalInverse(j + 1, HaltonBases[d]));
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
            limitsM ??= Array.Empty<double>();
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
                var limits = limitsM.ToArray();
                var probs = limits.Select(l => model.Cdf(l / plotHsScale)).ToArray();
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
            const double z90 = 1.2815515655446004;
            return new SwellOutlook(e.Serial, e.CreatedDay, e.OnsetDay, e.DurationDays,
                scale * OffshoreMetoceanModel.SwellPeak(cal, median, mean - z90 * sd),
                scale * OffshoreMetoceanModel.SwellPeak(cal, median, mean),
                scale * OffshoreMetoceanModel.SwellPeak(cal, median, mean + z90 * sd),
                e.PeakTpS, false);
        }

        // ─── One target day ───

        /// <summary>A swell term at the target day: shape × peak, the peak's latent normal (mean, sd; sd 0 = known).</summary>
        internal readonly struct SwellComponent
        {
            public readonly double Shape;
            public readonly double Median;
            public readonly double LatentMean;
            public readonly double LatentSd;
            public readonly double KnownPeak;
            private readonly double _bcMedian;
            private readonly double _sigmaBc;
            private readonly double _lambda;

            public SwellComponent(OffshoreMetoceanCalibration cal, double shape, double median, double latentMean,
                double latentSd, double knownPeak)
            {
                Shape = shape;
                Median = median;
                LatentMean = latentMean;
                LatentSd = latentSd;
                KnownPeak = knownPeak;
                _lambda = cal.SwellBoxCoxLambda;
                _bcMedian = MetoceanMath.BoxCox(median, _lambda);
                _sigmaBc = OffshoreMetoceanModel.SwellSigmaBoxCox(cal, median);
            }

            /// <summary>The swell (m, reference scale) at latent z.</summary>
            public double ValueAt(double z) => Shape * MetoceanMath.BoxCoxInverse(_bcMedian + _sigmaBc * z, _lambda);

            /// <summary>The swell at standardized posterior coordinate ζ (z = mean + sd·ζ).</summary>
            public double ValueAtStandard(double zeta) => ValueAt(LatentMean + LatentSd * zeta);

            /// <summary>The standardized coordinate ζ at which the swell reaches <paramref name="value"/>.</summary>
            public double StandardFor(double value)
            {
                double peak = value / Shape;
                if (peak <= 0.0) return double.NegativeInfinity;
                double z = (MetoceanMath.BoxCox(peak, _lambda) - _bcMedian) / _sigmaBc;
                return (z - LatentMean) / LatentSd;
            }
        }

        /// <summary>One combination of swells not yet created: its probability and its components.</summary>
        internal sealed record UnknownCombination(double Probability, (double Shape, double Median)[] Components);

        /// <summary>The distribution of the reference sea on one target day.</summary>
        internal sealed class DayModel
        {
            private readonly double[] _weights;
            private readonly double[] _means;
            private readonly double _sd;
            private readonly double _floor;
            private readonly double _lambda;
            private readonly double _knownEnergy;
            private readonly List<(double P, SwellComponent[] Uncertain)> _combos;

            public DayModel(double[] weights, double[] means, double sd, double floor, double lambda,
                double knownEnergy, List<(double, SwellComponent[])> combos, double unresolved)
            {
                _weights = weights;
                _means = means;
                _sd = sd;
                _floor = floor;
                _lambda = lambda;
                _knownEnergy = knownEnergy;
                _combos = combos;
                Unresolved = unresolved;
            }

            /// <summary>Probability mass in combinations not integrated (counted as above every limit).</summary>
            public double Unresolved { get; }

            /// <summary>The combinations integrated: probability and uncertain swells (tests).</summary>
            internal IReadOnlyList<(double P, SwellComponent[] Uncertain)> Combinations => _combos;

            /// <summary>Energy of the swells whose peak is known (m², reference scale).</summary>
            internal double KnownEnergy => _knownEnergy;

            /// <summary>P(base ≤ b).</summary>
            internal double BaseCdf(double b)
            {
                if (b < _floor) return 0.0;
                double y = MetoceanMath.BoxCox(b, _lambda);
                double p = 0.0;
                for (int w = 0; w < _weights.Length; w++)
                    if (_weights[w] > 0.0)
                        p += _weights[w] * MetoceanMath.NormalCdf((y - _means[w]) / _sd);
                return p;
            }

            /// <summary>P(reference Hs ≤ h).</summary>
            public double Cdf(double h)
            {
                if (!(h > 0.0)) return 0.0;
                double c = h * h - _knownEnergy;
                if (c <= 0.0) return 0.0;
                double total = 0.0;
                foreach (var (p, uncertain) in _combos)
                    total += p * Probability(c, uncertain);
                return Math.Clamp(total, 0.0, 1.0);
            }

            /// <summary>The p-quantile of the reference Hs (Illinois regula falsi on ln h).</summary>
            public double Quantile(double p)
            {
                if (!(p > 0.0 && p < 1.0)) throw new ArgumentOutOfRangeException(nameof(p), p, "Quantile level must lie in (0, 1)");
                double a = Math.Log(QuantileLowM), b = Math.Log(QuantileHighM);
                double fa = Cdf(Math.Exp(a)) - p, fb = Cdf(Math.Exp(b)) - p;
                if (fa >= 0.0) return Math.Exp(a);
                if (fb <= 0.0) return Math.Exp(b);
                int side = 0;
                for (int it = 0; it < QuantileMaxIterations && b - a > QuantileTolLn; it++)
                {
                    double x = (a * fb - b * fa) / (fb - fa);
                    double fx = Cdf(Math.Exp(x)) - p;
                    if (fx == 0.0) return Math.Exp(x);
                    if ((fx < 0.0) == (fa < 0.0))
                    {
                        a = x; fa = fx;
                        if (side == -1) fb /= 2.0;
                        side = -1;
                    }
                    else
                    {
                        b = x; fb = fx;
                        if (side == 1) fa /= 2.0;
                        side = 1;
                    }
                }
                return Math.Exp((a + b) / 2.0);
            }

            /// <summary>P(base² + Σ uncertain² ≤ c).</summary>
            internal double Probability(double c, SwellComponent[] uncertain)
            {
                if (c <= 0.0) return 0.0;
                switch (uncertain.Length)
                {
                    case 0: return BaseCdf(Math.Sqrt(c));
                    case 1: return One(c, uncertain[0]);
                    case 2: return Two(c, uncertain[0], uncertain[1]);
                    default: return Halton(c, uncertain);
                }
            }

            /// <summary>P(base² + s² ≤ c) for one uncertain swell: refined Gauss-Legendre over its ζ.</summary>
            private double One(double c, in SwellComponent comp)
            {
                double zetaStar = comp.StandardFor(Math.Sqrt(c));
                if (zetaStar <= -ZetaMax) return 0.0;
                double sum = 0.0;
                foreach (var (lo, hi) in Panels(zetaStar, InnerSteepOffsets))
                {
                    double half = 0.5 * (hi - lo), mid = 0.5 * (hi + lo), panel = 0.0;
                    for (int j = 0; j < 8; j++)
                    {
                        double zeta = mid + half * MetoceanMath.GaussLegendreNodes[j];
                        double s = comp.ValueAtStandard(zeta);
                        double rest = c - s * s;
                        if (rest <= 0.0) continue;
                        panel += MetoceanMath.GaussLegendreWeights[j] * MetoceanMath.NormalPdf(zeta) * BaseCdf(Math.Sqrt(rest));
                    }
                    sum += half * panel;
                }
                return sum;
            }

            /// <summary>P(base² + s₁² + s₂² ≤ c): refined Gauss-Legendre over the second swell, <see cref="One"/> inside.</summary>
            private double Two(double c, in SwellComponent inner, in SwellComponent outer)
            {
                double zetaStar = outer.StandardFor(Math.Sqrt(c));
                if (zetaStar <= -ZetaMax) return 0.0;
                double sum = 0.0;
                foreach (var (lo, hi) in Panels(zetaStar, OuterSteepOffsets))
                {
                    double half = 0.5 * (hi - lo), mid = 0.5 * (hi + lo), panel = 0.0;
                    for (int j = 0; j < 8; j++)
                    {
                        double zeta = mid + half * MetoceanMath.GaussLegendreNodes[j];
                        double s = outer.ValueAtStandard(zeta);
                        double rest = c - s * s;
                        if (rest <= 0.0) continue;
                        panel += MetoceanMath.GaussLegendreWeights[j] * MetoceanMath.NormalPdf(zeta) * One(rest, inner);
                    }
                    sum += half * panel;
                }
                return sum;
            }

            /// <summary>P(base² + Σ s² ≤ c) for three or more uncertain swells: the fixed Halton rule over their ζ.</summary>
            private double Halton(double c, SwellComponent[] uncertain)
            {
                double sum = 0.0;
                for (int j = 0; j < HaltonPoints; j++)
                {
                    double rest = c;
                    for (int i = 0; i < uncertain.Length && rest > 0.0; i++)
                    {
                        double s = uncertain[i].ValueAtStandard(HaltonNormals[i][j]);
                        rest -= s * s;
                    }
                    if (rest > 0.0) sum += BaseCdf(Math.Sqrt(rest));
                }
                return sum / HaltonPoints;
            }

            /// <summary>
            /// Integration panels in ζ: refined toward ζ* (where the swell alone
            /// reaches the limit) when it lies inside ±<see cref="ZetaMax"/>, four
            /// even panels otherwise.
            /// </summary>
            private static IEnumerable<(double Lo, double Hi)> Panels(double zetaStar, double[] steepOffsets)
            {
                if (double.IsNaN(zetaStar) || zetaStar >= ZetaMax)
                {
                    for (int i = 0; i + 1 < FlatPanelEdges.Length; i++)
                        yield return (FlatPanelEdges[i], FlatPanelEdges[i + 1]);
                    yield break;
                }
                double lo = -ZetaMax;
                foreach (double offset in steepOffsets)
                {
                    double edge = zetaStar - offset;
                    if (edge <= lo) continue;
                    yield return (lo, edge);
                    lo = edge;
                }
                yield return (lo, zetaStar);
            }
        }

        /// <summary>Build the distribution of the reference sea on the day <paramref name="lead"/> after the last tick.</summary>
        internal static DayModel BuildDay(OffshoreMetocean state, OffshoreMetoceanCalibration cal, double[] weatherAtLead, int lead)
        {
            if (weatherAtLead == null || weatherAtLead.Length != 4)
                throw new ArgumentException("The weather distribution needs one probability per weather state", nameof(weatherAtLead));
            int issue = state.LastTickDay;
            int target = issue + lead;
            int month = SimCalendar.MonthIndex(target);

            // Base: the AR(1) residual at lead k is Gaussian around φ^k X_t.
            double phiK = Math.Pow(cal.ArPhi, lead);
            double sd = cal.ArSigma * Math.Sqrt((1.0 - phiK * phiK) / (1.0 - cal.ArPhi * cal.ArPhi));
            var means = new double[4];
            var weights = new double[4];
            foreach (WeatherState w in Enum.GetValues<WeatherState>())
            {
                means[(int)w] = OffshoreMetoceanModel.BaseLevel(cal, month, w) + phiK * state.ResidualX;
                weights[(int)w] = weatherAtLead[(int)w];
            }

            // Known events active on the target day: a known peak adds its energy, an estimated one is integrated.
            double knownEnergy = 0.0;
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
                uncertainKnown.Add(new SwellComponent(cal, shape, median, mean, posteriorSd, double.NaN));
            }

            // Events not created yet, enumerated over creation day, onset and plateau.
            var combos = new List<(double, SwellComponent[])>();
            double unresolved = 0.0;
            foreach (var combo in UnknownCombinationsCached(cal, issue, lead))
            {
                int count = uncertainKnown.Count + combo.Components.Length;
                if (count > MaxSwellComponents || combo.Probability < NegligibleCombo)
                {
                    unresolved += combo.Probability;
                    continue;
                }
                var comps = new SwellComponent[count];
                for (int i = 0; i < uncertainKnown.Count; i++)
                    comps[i] = uncertainKnown[i];
                for (int i = 0; i < combo.Components.Length; i++)
                    comps[uncertainKnown.Count + i] = new SwellComponent(cal, combo.Components[i].Shape, combo.Components[i].Median, 0.0, 1.0, double.NaN);
                combos.Add((combo.Probability, comps));
            }
            return new DayModel(weights, means, sd, cal.BaseFloorM, cal.BaseBoxCoxLambda, knownEnergy, combos, unresolved);
        }

        private static readonly ConditionalWeakTable<OffshoreMetoceanCalibration, ConcurrentDictionary<int, UnknownCombination[]>> CombinationCache = new();

        /// <summary>
        /// <see cref="UnknownEventCombinations"/> for an issue day and lead; they
        /// depend only on the calendar (day of year) and the calibration, so they
        /// are computed once per calibration and day of year.
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
                foreach (var kv in dist)
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
