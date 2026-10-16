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
    public static class MetoceanForecast
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

        // ─── One target day ───

        /// <summary>
        /// A swell term at the target day: shape × peak, the peak a latent normal
        /// with posterior (mean, sd) in standard units of its month's peak
        /// distribution.
        /// </summary>
        internal readonly struct SwellComponent
        {
            public readonly double Shape;
            public readonly double LatentMean;
            public readonly double LatentSd;
            private readonly double _tMedian;
            private readonly double _sigmaT;
            private readonly double _lambda;

            public SwellComponent(OffshoreMetoceanCalibration cal, double shape, double median, double latentMean, double latentSd)
            {
                Shape = shape;
                LatentMean = latentMean;
                LatentSd = latentSd;
                _lambda = cal.SwellBoxCoxLambda;
                _tMedian = MetoceanMath.UpperBoxCox(median, _lambda);
                _sigmaT = OffshoreMetoceanModel.SwellSigmaTransformed(cal, median);
            }

            /// <summary>The swell (m, reference scale) at standardized posterior coordinate ζ (latent z = mean + sd·ζ).</summary>
            public double ValueAtStandard(double zeta)
                => Shape * MetoceanMath.UpperBoxCoxInverse(_tMedian + _sigmaT * (LatentMean + LatentSd * zeta), _lambda);

            /// <summary>The standardized coordinate ζ at which the swell reaches <paramref name="value"/>.</summary>
            public double StandardFor(double value)
            {
                double peak = value / Shape;
                if (peak <= 0.0) return double.NegativeInfinity;
                double z = (MetoceanMath.UpperBoxCox(peak, _lambda) - _tMedian) / _sigmaT;
                return (z - LatentMean) / LatentSd;
            }
        }

        /// <summary>One combination of swells not yet created: its probability and its components (shape, month median).</summary>
        internal sealed record UnknownCombination(double Probability, (double Shape, double Median)[] Components);

        /// <summary>
        /// The distribution of the reference sea on one target day: the base
        /// mixture, the known energy, the terms integrated on their own (no
        /// uncertain swell, one, or more in a combination of at least
        /// <see cref="QuadratureMinProbability"/>) and the tail (every other
        /// combination, together well under 10 % of the probability),
        /// integrated by one Hammersley rule over the tail's mixture.
        /// </summary>
        internal sealed class DayModel
        {
            private readonly double[] _baseWeights;
            private readonly double[] _baseMeans;
            private readonly double _sd;
            private readonly double _lambda;
            private readonly double _knownEnergy;
            private readonly double[] _termProbability;
            private readonly SwellComponent[][] _termComponents;
            private readonly double _tailMass;
            private readonly SwellComponent[][] _tailPointComponents;
            private readonly List<(double P, SwellComponent[] Uncertain)> _tailTerms;

            public DayModel(double[] baseWeights, double[] baseMeans, double sd, double lambda, double knownEnergy,
                List<(double P, SwellComponent[] Uncertain)> terms, double unresolved)
            {
                _baseWeights = baseWeights;
                _baseMeans = baseMeans;
                _sd = sd;
                _lambda = lambda;
                _knownEnergy = knownEnergy;
                var exact = terms.Where(t => IsQuadratureTerm(t.P, t.Uncertain.Length)).ToList();
                var tail = terms.Where(t => !IsQuadratureTerm(t.P, t.Uncertain.Length)).ToList();
                _termProbability = exact.Select(t => t.P).ToArray();
                _termComponents = exact.Select(t => t.Uncertain).ToArray();
                _tailTerms = tail;
                _tailMass = tail.Sum(t => t.P);
                _tailPointComponents = TailPoints(tail, _tailMass);
                Unresolved = unresolved;
                if (_tailMass > 0.0 || exact.Any(t => t.Uncertain.Length >= 2))
                    _energyTable = BuildEnergyTable();
            }

            // ─── The base CDF by energy, tabulated for the heavy days ───
            // G(u) = P(base² ≤ e) at u = ln e. Days whose terms need thousands of
            // base CDF values (two or more uncertain swells) read it from a cubic
            // Hermite table on a uniform grid in u with exact slopes (error below
            // 1e-7, MetoceanForecastAccuracyTests); the others evaluate it directly.
            // Which one is used depends only on the day's terms, never on how
            // often the day is asked.

            private const double TableSpanSd = 8.0;
            private const double TableStepsPerSd = 12.0;
            private readonly EnergyTable? _energyTable;

            private sealed record EnergyTable(double U0, double Step, double[] G, double[] Slope);

            private EnergyTable BuildEnergyTable()
            {
                double lowest = _baseMeans.Min(), highest = _baseMeans.Max();
                double u0 = 2.0 * Math.Log(MetoceanMath.UpperBoxCoxInverse(lowest - TableSpanSd * _sd, _lambda));
                double u1 = 2.0 * Math.Log(MetoceanMath.UpperBoxCoxInverse(highest + TableSpanSd * _sd, _lambda));
                double step = _sd / TableStepsPerSd;
                int n = (int)Math.Ceiling((u1 - u0) / step) + 1;
                var g = new double[n];
                var slope = new double[n];
                for (int i = 0; i < n; i++)
                {
                    double u = u0 + i * step, b = Math.Exp(0.5 * u);
                    double y = MetoceanMath.UpperBoxCox(b, _lambda);
                    double dyDu = 0.5 * MetoceanMath.UpperBoxCoxSlopePerLn(b, _lambda);
                    double p = 0.0, d = 0.0;
                    for (int w = 0; w < _baseWeights.Length; w++)
                    {
                        double x = (y - _baseMeans[w]) / _sd;
                        p += _baseWeights[w] * MetoceanMath.NormalCdf(x);
                        d += _baseWeights[w] * MetoceanMath.NormalPdf(x);
                    }
                    g[i] = p;
                    slope[i] = d / _sd * dyDu * step;
                }
                return new EnergyTable(u0, step, g, slope);
            }

            /// <summary>P(base² ≤ e).</summary>
            internal double BaseCdfOfEnergy(double e)
            {
                if (!(e > 0.0)) return 0.0;
                var table = _energyTable;
                if (table == null) return BaseCdf(Math.Sqrt(e));
                double x = (Math.Log(e) - table.U0) / table.Step;
                if (x <= 0.0) return table.G[0];
                int i = (int)x;
                if (i >= table.G.Length - 1) return table.G[^1];
                double t = x - i, t2 = t * t, t3 = t2 * t;
                return (2 * t3 - 3 * t2 + 1) * table.G[i] + (t3 - 2 * t2 + t) * table.Slope[i]
                     + (-2 * t3 + 3 * t2) * table.G[i + 1] + (t3 - t2) * table.Slope[i + 1];
            }

            private static bool IsQuadratureTerm(double p, int components)
                => components <= 1 || p >= QuadratureMinProbability;

            /// <summary>
            /// The tail combination each Hammersley point integrates: point j
            /// takes the combination whose cumulative probability (in the tail's
            /// order) covers (j + ½)/N of the tail's mass.
            /// </summary>
            private static SwellComponent[][] TailPoints(List<(double P, SwellComponent[] Uncertain)> tail, double mass)
            {
                if (tail.Count == 0) return Array.Empty<SwellComponent[]>();
                var points = new SwellComponent[HaltonPoints][];
                int t = 0;
                double cumulative = tail[0].P;
                for (int j = 0; j < HaltonPoints; j++)
                {
                    double target = (j + 0.5) / HaltonPoints * mass;
                    while (target > cumulative && t < tail.Count - 1)
                        cumulative += tail[++t].P;
                    points[j] = tail[t].Uncertain;
                }
                return points;
            }

            /// <summary>Probability mass in combinations not integrated (counted as above every limit).</summary>
            public double Unresolved { get; }

            /// <summary>Probability mass integrated by the tail's Hammersley rule.</summary>
            internal double TailMass => _tailMass;

            /// <summary>The terms integrated by quadrature: probability and uncertain swells (tests).</summary>
            internal IEnumerable<(double P, SwellComponent[] Uncertain)> Terms
                => _termProbability.Select((p, i) => (p, _termComponents[i]));

            /// <summary>Every term, the quadrature terms first, then the tail's (tests).</summary>
            internal IEnumerable<(double P, SwellComponent[] Uncertain)> AllTerms => Terms.Concat(_tailTerms);

            /// <summary>Energy of the background swell and the swells whose peak is known (m², reference scale).</summary>
            internal double KnownEnergy => _knownEnergy;

            /// <summary>P(base ≤ b): the weather mixture of normals in the sea transform.</summary>
            internal double BaseCdf(double b)
            {
                if (!(b > 0.0)) return 0.0;
                double y = MetoceanMath.UpperBoxCox(b, _lambda);
                double p = 0.0;
                for (int w = 0; w < _baseWeights.Length; w++)
                    p += _baseWeights[w] * MetoceanMath.NormalCdf((y - _baseMeans[w]) / _sd);
                return p;
            }

            /// <summary>P(reference Hs ≤ h).</summary>
            public double Cdf(double h)
            {
                if (!(h > 0.0)) return 0.0;
                double c = h * h - _knownEnergy;
                if (c <= 0.0) return 0.0;
                double total = 0.0;
                for (int t = 0; t < _termProbability.Length; t++)
                    total += _termProbability[t] * Probability(c, _termComponents[t]);
                if (_tailMass > 0.0) total += _tailMass * Tail(c);
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
                    case 0: return BaseCdfOfEnergy(c);
                    case 1: return One(c, uncertain[0]);
                    case 2:
                        // The swell nearer to reaching the limit alone goes inside, on the finer panels.
                        double limit = Math.Sqrt(c);
                        return uncertain[0].StandardFor(limit) <= uncertain[1].StandardFor(limit)
                            ? Two(c, uncertain[0], uncertain[1])
                            : Two(c, uncertain[1], uncertain[0]);
                    default: return Conditional(c, uncertain);
                }
            }

            /// <summary>
            /// P(base² + Σ s² ≤ c) for three or more uncertain swells in a likely
            /// combination (several known events still to arrive): the swell
            /// nearest to reaching the limit alone by <see cref="One"/>, the
            /// others by a tensor Gauss-Hermite rule (12 nodes each for two
            /// others, 7 for three; four or more by the antithetic Halton rule).
            /// </summary>
            private double Conditional(double c, SwellComponent[] uncertain)
            {
                double limit = Math.Sqrt(c);
                int dominant = 0;
                double lowest = double.PositiveInfinity;
                for (int i = 0; i < uncertain.Length; i++)
                {
                    double zetaStar = uncertain[i].StandardFor(limit);
                    if (zetaStar < lowest)
                    {
                        lowest = zetaStar;
                        dominant = i;
                    }
                }
                var others = new SwellComponent[uncertain.Length - 1];
                for (int i = 0, o = 0; i < uncertain.Length; i++)
                    if (i != dominant) others[o++] = uncertain[i];
                var dom = uncertain[dominant];
                if (others.Length > MetoceanMath.GaussHermiteRules.Length)
                {
                    double sum = 0.0;
                    for (int j = 0; j < HaltonPoints; j++)
                    {
                        double rest = c;
                        for (int i = 0; i < others.Length && rest > 0.0; i++)
                        {
                            double s = others[i].ValueAtStandard(HaltonNormals[i][j]);
                            rest -= s * s;
                        }
                        if (rest > 0.0) sum += One(rest, dom);
                    }
                    return sum / HaltonPoints;
                }
                var (nodes, weights) = MetoceanMath.GaussHermiteRules[others.Length - 1];
                return Hermite(c, others, 0, nodes, weights, dom);
            }

            private double Hermite(double c, SwellComponent[] others, int depth, double[] nodes, double[] weights, in SwellComponent dominant)
            {
                double sum = 0.0;
                for (int j = 0; j < nodes.Length; j++)
                {
                    double s = others[depth].ValueAtStandard(nodes[j]);
                    double rest = c - s * s;
                    if (rest <= 0.0) continue;
                    sum += weights[j] * (depth + 1 == others.Length
                        ? One(rest, dominant)
                        : Hermite(rest, others, depth + 1, nodes, weights, dominant));
                }
                return sum;
            }

            /// <summary>P(base² + s² ≤ c) for one uncertain swell: refined Gauss-Legendre over its ζ.</summary>
            internal double One(double c, in SwellComponent comp)
            {
                double zetaStar = comp.StandardFor(Math.Sqrt(c));
                if (zetaStar <= -ZetaMax) return 0.0;
                Span<double> edges = stackalloc double[FlatPanelEdges.Length + InnerSteepOffsets.Length];
                int n = PanelEdges(zetaStar, InnerSteepOffsets, edges);
                double sum = 0.0;
                for (int i = 0; i + 1 < n; i++)
                {
                    double half = 0.5 * (edges[i + 1] - edges[i]), mid = 0.5 * (edges[i + 1] + edges[i]), panel = 0.0;
                    for (int j = 0; j < 8; j++)
                    {
                        double zeta = mid + half * MetoceanMath.GaussLegendreNodes[j];
                        double s = comp.ValueAtStandard(zeta);
                        double rest = c - s * s;
                        if (rest <= 0.0) continue;
                        panel += MetoceanMath.GaussLegendreWeights[j] * MetoceanMath.NormalPdf(zeta) * BaseCdfOfEnergy(rest);
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
                Span<double> edges = stackalloc double[FlatPanelEdges.Length + OuterSteepOffsets.Length];
                int n = PanelEdges(zetaStar, OuterSteepOffsets, edges);
                double sum = 0.0;
                for (int i = 0; i + 1 < n; i++)
                {
                    double half = 0.5 * (edges[i + 1] - edges[i]), mid = 0.5 * (edges[i + 1] + edges[i]), panel = 0.0;
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

            /// <summary>
            /// P(base² + Σ s² ≤ c | the tail): the Hammersley rule over the tail
            /// mixture, point j's combination from its first coordinate and its
            /// swells' ζ from the Halton coordinates in bases 2, 3, 5, 7, 11, 13.
            /// </summary>
            private double Tail(double c)
            {
                double sum = 0.0;
                for (int j = 0; j < HaltonPoints; j++)
                {
                    var uncertain = _tailPointComponents[j];
                    double rest = c;
                    for (int i = 0; i < uncertain.Length && rest > 0.0; i++)
                    {
                        double s = uncertain[i].ValueAtStandard(HaltonNormals[i][j]);
                        rest -= s * s;
                    }
                    if (rest > 0.0) sum += BaseCdfOfEnergy(rest);
                }
                return sum / HaltonPoints;
            }

            /// <summary>
            /// Integration panel edges in ζ, written to <paramref name="edges"/>
            /// (returns their count): the even panels −8, −4, 0, 4, 8 up to the
            /// refined zone, then panels refined toward ζ* (where the swell alone
            /// reaches the limit, the integrand's end) at the steep offsets below
            /// it when ζ* lies in the bulk (at most 5). With ζ* above 5 the even
            /// panels up to ζ*, at or above 8 the even panels alone.
            /// </summary>
            private static int PanelEdges(double zetaStar, double[] steepOffsets, Span<double> edges)
            {
                if (double.IsNaN(zetaStar) || zetaStar >= ZetaMax)
                {
                    FlatPanelEdges.CopyTo(edges);
                    return FlatPanelEdges.Length;
                }
                int n = 0;
                edges[n++] = -ZetaMax;
                // Beyond ζ = 5 lies 3e-7 of the weight: no refinement there, the even panels up to ζ*.
                bool refine = zetaStar <= RefineBelowZeta;
                double refinedFrom = refine ? zetaStar - steepOffsets[0] : zetaStar;
                for (int i = 1; i + 1 < FlatPanelEdges.Length; i++)
                    if (FlatPanelEdges[i] < refinedFrom) edges[n++] = FlatPanelEdges[i];
                if (refine)
                    foreach (double offset in steepOffsets)
                    {
                        double edge = zetaStar - offset;
                        if (edge <= edges[n - 1]) continue;
                        edges[n++] = edge;
                    }
                edges[n++] = zetaStar;
                return n;
            }
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
