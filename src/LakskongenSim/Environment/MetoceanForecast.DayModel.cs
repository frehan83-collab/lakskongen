using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Calibration;

namespace LakskongenSim.Environment
{
    /// <summary>The day model of <see cref="MetoceanForecast"/>: the swell terms and their integration.</summary>
    public static partial class MetoceanForecast
    {
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
    }
}
