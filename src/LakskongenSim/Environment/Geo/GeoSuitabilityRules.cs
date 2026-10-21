using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LakskongenSim.Environment.Geo
{
    /// <summary>
    /// The published suitability rules (<c>suitability_rules.json</c>, written
    /// verbatim by tools/geodata/geo_rules.py; docs/geo/GEODATA.md §5.2): the
    /// effective component weights, the river factor, the temperature ramp and
    /// the offshore stage factors, with the score and binding formulas the
    /// pipeline used, so a candidate's 'why here' can show each weighted
    /// component and what binds it.
    /// <list type="bullet">
    /// <item>Open pen: score = (Σ wᵢcᵢ / Σw) × river factor. Component i costs
    /// wᵢ/Σw × (1 − cᵢ) × river factor; the river factor costs base × (1 − factor).</item>
    /// <item>Offshore: score = depth × stage factor × river factor; the binding is
    /// "depth" below 1, else "stage" below 1, else "none" (build_suitability.py).</item>
    /// </list>
    /// Pure functions of the embedded file; nothing here feeds game state in this wave.
    /// </summary>
    public sealed class GeoSuitabilityRules
    {
        private const string FileName = "suitability_rules.json";

        /// <summary>The river-factor term's name in costs and bindings.</summary>
        public const string RiverTerm = "river";

        /// <summary>The offshore planning-stage term's name in costs and bindings.</summary>
        public const string StageTerm = "stage";

        /// <summary>The binding when no term costs anything.</summary>
        public const string NoBinding = "none";

        /// <summary>Costs at or below this are no cost (the pipeline's 1e-9).</summary>
        private const double CostEpsilon = 1e-9;

        /// <summary>
        /// The pipeline's component order (build_suitability.py score_cells;
        /// the scored reason codes 16-23 in suitability.json without river):
        /// the first of equal costs binds, and the river term comes last.
        /// </summary>
        private static readonly string[] PipelineOrder =
            { "depth", "exchange", "farm_distance", "temperature", "current", "waves", "oxygen" };

        private static readonly Lazy<GeoSuitabilityRules> LazyDefault =
            new(() => Parse(GeoResources.ReadText(GeoResources.SuitabilityRulesJson)));

        private readonly ReadOnlyDictionary<string, double> _weights;
        private readonly ReadOnlyDictionary<string, double> _stageFactors;
        private readonly string[] _order;

        private GeoSuitabilityRules(Dictionary<string, double> weights, Dictionary<string, double> stageFactors,
            double riverMinFactor, double riverRadiusM, (double, double, double, double) temperatureRamp)
        {
            _weights = new ReadOnlyDictionary<string, double>(weights);
            _stageFactors = new ReadOnlyDictionary<string, double>(stageFactors);
            _order = PipelineOrder.Where(weights.ContainsKey)
                .Concat(weights.Keys.Where(k => !PipelineOrder.Contains(k)).OrderBy(k => k, StringComparer.Ordinal))
                .ToArray();
            WeightSum = _order.Sum(k => weights[k]);
            RiverMinFactor = riverMinFactor;
            RiverRadiusM = riverRadiusM;
            TemperatureRampC = temperatureRamp;
        }

        /// <summary>The shipped rules (embedded; parsed once per process).</summary>
        public static GeoSuitabilityRules Default => LazyDefault.Value;

        /// <summary>The weight of each score component in this build (Copernicus layers in).</summary>
        public IReadOnlyDictionary<string, double> EffectiveWeights => _weights;

        /// <summary>Σ of <see cref="EffectiveWeights"/>.</summary>
        public double WeightSum { get; }

        /// <summary>The weighted components in the pipeline's order (ties bind to the first).</summary>
        public IReadOnlyList<string> ComponentOrder => _order;

        /// <summary>The river factor at a river mouth (it rises linearly to 1 at <see cref="RiverRadiusM"/>).</summary>
        public double RiverMinFactor { get; }

        /// <summary>Distance at which the river factor reaches 1, m.</summary>
        public double RiverRadiusM { get; }

        /// <summary>The offshore score factor of each Havbruk til havs planning stage.</summary>
        public IReadOnlyDictionary<string, double> OffshoreStageFactors => _stageFactors;

        /// <summary>The temperature trapezoid (°C): 0 at A, 1 from B to C, 0 at D.</summary>
        public (double A, double B, double C, double D) TemperatureRampC { get; }

        /// <summary>Σ wᵢcᵢ / Σw over the weighted components (open pens).</summary>
        public double BaseScore(GeoCandidate candidate)
        {
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            double sum = 0;
            foreach (var k in _order)
                sum += _weights[k] * ComponentValue(candidate, k);
            return sum / WeightSum;
        }

        /// <summary>The score the published formula gives the candidate's (rounded) components.</summary>
        public double Score(GeoCandidate candidate)
        {
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            if (candidate.IsOffshore)
                return ComponentValue(candidate, "depth") * StageFactorOf(candidate) * candidate.RiverFactor;
            return BaseScore(candidate) * candidate.RiverFactor;
        }

        /// <summary>
        /// What a term costs the candidate's score. Open pen: a weighted component
        /// k costs w_k/Σw·(1 − c_k)·river_factor, "river" costs base·(1 − river_factor),
        /// "stage" costs 0. Offshore: each factor of depth × stage × river costs
        /// (1 − its value) times the other two. A name that is neither a weighted
        /// component, "river" nor "stage" throws.
        /// </summary>
        public double ComponentCost(GeoCandidate candidate, string component)
        {
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            if (component == null) throw new ArgumentNullException(nameof(component));
            if (component != RiverTerm && component != StageTerm && !_weights.ContainsKey(component))
                throw new ArgumentException($"'{component}' is not a score term of suitability_rules.json", nameof(component));
            return candidate.IsOffshore ? OffshoreCost(candidate, component) : OpenPenCost(candidate, component);
        }

        private double OpenPenCost(GeoCandidate c, string component) => component switch
        {
            RiverTerm => BaseScore(c) * (1 - c.RiverFactor),
            StageTerm => 0,
            _ => _weights[component] / WeightSum * (1 - ComponentValue(c, component)) * c.RiverFactor,
        };

        private double OffshoreCost(GeoCandidate c, string component)
        {
            double depth = ComponentValue(c, "depth"), stage = StageFactorOf(c), river = c.RiverFactor;
            return component switch
            {
                "depth" => (1 - depth) * stage * river,
                StageTerm => depth * (1 - stage) * river,
                RiverTerm => depth * stage * (1 - river),
                _ => 0,
            };
        }

        /// <summary>
        /// The binding term by the pipeline's rule: for an open pen the term with
        /// the largest cost (first in <see cref="ComponentOrder"/> on a tie, river
        /// last), "none" when no term costs more than 1e-9; offshore "depth" when
        /// depth is below 1, else "stage" when the stage factor is below 1, else "none".
        /// </summary>
        public string BindingOf(GeoCandidate candidate)
        {
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            if (candidate.IsOffshore)
                return ComponentValue(candidate, "depth") < 1 ? "depth" : StageFactorOf(candidate) < 1 ? StageTerm : NoBinding;
            string best = NoBinding;
            double bestCost = CostEpsilon;
            foreach (var term in _order.Append(RiverTerm))
            {
                double cost = OpenPenCost(candidate, term);
                if (cost > bestCost)
                {
                    best = term;
                    bestCost = cost;
                }
            }
            return best;
        }

        /// <summary>
        /// The temperature component of an area's climatology: the mean over the
        /// months of the temperature trapezoid (build_suitability.py temp_score).
        /// </summary>
        public double TemperatureComponent(IReadOnlyList<double> monthlyC)
        {
            if (monthlyC == null || monthlyC.Count == 0) throw new ArgumentException("No monthly temperatures", nameof(monthlyC));
            var (a, b, c, d) = TemperatureRampC;
            return monthlyC.Average(t => Trapezoid(t, a, b, c, d));
        }

        /// <summary>0 at or below a, rising to 1 at b, 1 to c, falling to 0 at d (geo_rules.py trapezoid).</summary>
        public static double Trapezoid(double v, double a, double b, double c, double d)
        {
            double up = b > a ? Math.Clamp((v - a) / Math.Max(b - a, 1e-9), 0, 1) : (v >= a ? 1 : 0);
            double down = d > c ? Math.Clamp((d - v) / Math.Max(d - c, 1e-9), 0, 1) : (v <= d ? 1 : 0);
            return Math.Min(up, down);
        }

        private static double ComponentValue(GeoCandidate c, string name) =>
            c.TryGetComponent(name, out double value)
                ? value
                : throw new InvalidOperationException($"candidate {c.Id} has no '{name}' component");

        private static double StageFactorOf(GeoCandidate c) =>
            c.StageFactor ?? throw new InvalidOperationException($"offshore candidate {c.Id} has no stage factor");

        /// <summary>
        /// Parse and validate a <c>suitability_rules.json</c> text: non-negative
        /// effective weights with a positive sum, a river factor in (0, 1], an
        /// ascending temperature ramp and stage factors in (0, 1]. Throws
        /// <see cref="InvalidDataException"/> naming the field.
        /// </summary>
        public static GeoSuitabilityRules Parse(string json)
        {
            using var doc = GeoJson.Parse(json, FileName);
            var root = doc.RootElement;
            var weights = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var w in GeoJson.RequiredObject(root, "effective_weights", FileName).EnumerateObject())
            {
                double value = GeoJson.Number(w.Value, $"{FileName}: field 'effective_weights.{w.Name}'");
                if (value < 0) throw new InvalidDataException($"{FileName}: field 'effective_weights.{w.Name}' is negative");
                weights[w.Name] = value;
            }
            if (weights.Values.Sum() <= 0) throw new InvalidDataException($"{FileName}: field 'effective_weights' sums to 0");

            var river = GeoJson.RequiredObject(root, "river_factor", FileName);
            double minFactor = GeoJson.RequiredDouble(river, "min_factor", FileName + " river_factor");
            double radius = GeoJson.RequiredDouble(river, "radius_m", FileName + " river_factor");
            if (minFactor <= 0 || minFactor > 1 || radius <= 0)
                throw new InvalidDataException($"{FileName}: field 'river_factor' needs min_factor in (0, 1] and a positive radius_m");

            var temperature = GeoJson.RequiredObject(GeoJson.RequiredObject(root, "components", FileName), "temperature", FileName);
            var ramp = GeoJson.RequiredArray(temperature, "ramp_c", FileName + " components.temperature");
            if (ramp.GetArrayLength() != 4)
                throw new InvalidDataException($"{FileName}: field 'components.temperature.ramp_c' must hold four numbers");
            var r = ramp.EnumerateArray().Select(v => GeoJson.Number(v, $"{FileName}: field 'components.temperature.ramp_c'")).ToArray();
            if (!(r[0] <= r[1] && r[1] <= r[2] && r[2] <= r[3]))
                throw new InvalidDataException($"{FileName}: field 'components.temperature.ramp_c' is not ascending");

            var stages = new Dictionary<string, double>(StringComparer.Ordinal);
            var offshore = GeoJson.RequiredObject(root, "offshore", FileName);
            foreach (var s in GeoJson.RequiredObject(offshore, "stage_bonus", FileName + " offshore").EnumerateObject())
            {
                double value = GeoJson.Number(s.Value, $"{FileName}: field 'offshore.stage_bonus.{s.Name}'");
                if (value <= 0 || value > 1)
                    throw new InvalidDataException($"{FileName}: field 'offshore.stage_bonus.{s.Name}' is {value}, expected within (0, 1]");
                stages[s.Name] = value;
            }
            return new GeoSuitabilityRules(weights, stages, minFactor, radius, (r[0], r[1], r[2], r[3]));
        }
    }
}
