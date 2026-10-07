using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Data;

namespace LakskongenSim.Biology
{
    /// <summary>
    /// Selection method used to estimate breeding values in a broodstock pool.
    /// Ordered by expected accuracy: phenotype &lt; marker-assisted &lt; genomic.
    /// </summary>
    public enum SelectionMethod
    {
        /// <summary>Individual phenotype only (accuracy = √h²).</summary>
        Phenotypic = 0,

        /// <summary>Phenotype plus a sparse panel of markers (QTL/candidate genes).</summary>
        MarkerAssisted = 1,

        /// <summary>Dense genome-wide markers with a reference population (GEBV).</summary>
        Genomic = 2
    }

    /// <summary>
    /// Marker-assisted and genomic selection maths for Atlantic salmon breeding programmes.
    ///
    /// Grounding (breeder's equation, quantitative genetics):
    /// <list type="bullet">
    /// <item>Response to selection: <b>ΔG = i · r · σ_A</b>, where <c>i</c> is the
    /// selection intensity (standardised selection differential), <c>r</c> is the
    /// accuracy of the selection criterion, and <c>σ_A = √h² · σ_P</c> is the additive
    /// genetic standard deviation.</item>
    /// <item>For individual phenotype selection, accuracy <c>r = √h²</c>; this recovers
    /// the classic form <c>ΔG = i · h² · σ_P</c> (see <see cref="Genetics"/> and
    /// <c>docs/Architecture_DeepDives.md</c>).</item>
    /// <item>Genomic/marker information raises <c>r</c> toward the reliability of the
    /// estimated breeding value, which is what makes genomic selection valuable for the
    /// low-heritability traits (disease/lice resistance, stress tolerance) that dominate
    /// Norwegian programmes (AquaGen, SalmoBreed, Mowi Genetics).</item>
    /// <item>Inbreeding: Wright's coefficient of a mated pair's offspring equals the
    /// kinship of the parents. Full-sib / parent-offspring pairs give F = 0.25, half-sibs
    /// F = 0.125. Inbreeding depression is strongest for fitness traits.</item>
    /// </list>
    /// All methods are pure and deterministic — no randomness, no I/O.
    /// </summary>
    public static class GenomicSelection
    {
        /// <summary>Selection intensity for the top 10% of a normal distribution (≈1.755).</summary>
        public const double DefaultSelectionIntensity = 1.755;

        /// <summary>
        /// Baseline inbreeding depression per unit F for a fitness-neutral trait
        /// (F = 0.10 → 5% reduction). Trait sensitivities scale this up/down.
        /// </summary>
        public const double BaseDepressionPerUnitF = 0.50;

        /// <summary>Maximum fraction of a trait that inbreeding may erode.</summary>
        public const double MaxDepressionFraction = 0.75;

        // Per-trait sensitivity to inbreeding depression (fitness traits erode fastest).
        private static readonly Dictionary<GeneticTrait, double> DepressionSensitivity = new()
        {
            [GeneticTrait.DiseaseResistance] = 1.60,
            [GeneticTrait.SeaLiceResistance] = 1.30,
            [GeneticTrait.StressTolerance] = 1.40,
            [GeneticTrait.ColdTolerance] = 1.30,
            [GeneticTrait.GrowthRate] = 1.00,
            [GeneticTrait.FeedConversion] = 0.80,
            [GeneticTrait.LateMaturation] = 0.60,
            [GeneticTrait.FilletQuality] = 0.40
        };

        // ─── Expected genetic gain ───

        /// <summary>
        /// Expected genetic gain per generation for every trait in a broodstock pool.
        /// ΔG = i · r · σ_A, with σ_A the observed additive genetic SD (population SD of
        /// the breeding values) and <c>r</c> the accuracy of the chosen method.
        /// </summary>
        /// <param name="broodstock">Candidate breeders (their breeding values).</param>
        /// <param name="selectionIntensity">Standardised selection differential (i).</param>
        /// <param name="method">Selection criterion accuracy model.</param>
        /// <param name="markerDensity01">Marker panel density, 0..1 (MA/GS only).</param>
        /// <param name="referencePopulation01">Reference-population size proxy, 0..1 (GS only).</param>
        public static Dictionary<GeneticTrait, double> ExpectedGain(
            IReadOnlyList<Genome> broodstock,
            double selectionIntensity = DefaultSelectionIntensity,
            SelectionMethod method = SelectionMethod.Genomic,
            double markerDensity01 = 0.75,
            double referencePopulation01 = 0.75)
        {
            var gains = new Dictionary<GeneticTrait, double>();
            foreach (GeneticTrait trait in Enum.GetValues(typeof(GeneticTrait)))
            {
                gains[trait] = ExpectedGainForTrait(
                    trait, broodstock, selectionIntensity, method, markerDensity01, referencePopulation01);
            }
            return gains;
        }

        /// <summary>
        /// Expected genetic gain for a single trait: ΔG = i · r · σ_A.
        /// Returns 0 when fewer than two breeders or no additive variance are present.
        /// </summary>
        public static double ExpectedGainForTrait(
            GeneticTrait trait,
            IReadOnlyList<Genome> broodstock,
            double selectionIntensity = DefaultSelectionIntensity,
            SelectionMethod method = SelectionMethod.Genomic,
            double markerDensity01 = 0.75,
            double referencePopulation01 = 0.75)
        {
            if (broodstock is null || broodstock.Count < 2) return 0.0;
            if (selectionIntensity <= 0.0) return 0.0;

            double sigmaA = AdditiveGeneticStdDev(broodstock, trait);
            if (sigmaA <= 0.0) return 0.0;

            double accuracy = SelectionAccuracy(trait, method, markerDensity01, referencePopulation01);
            return selectionIntensity * accuracy * sigmaA;
        }

        /// <summary>
        /// Additive genetic standard deviation of a trait in the pool:
        /// population SD of the breeding values (σ_A = √h²·σ_P when BVs are on the
        /// genetic scale).
        /// </summary>
        public static double AdditiveGeneticStdDev(IReadOnlyList<Genome> broodstock, GeneticTrait trait)
        {
            if (broodstock is null || broodstock.Count == 0) return 0.0;

            double mean = 0.0;
            for (int i = 0; i < broodstock.Count; i++)
                mean += broodstock[i].TraitValues.TryGetValue(trait, out double bv) ? bv : 0.0;
            mean /= broodstock.Count;

            double ss = 0.0;
            for (int i = 0; i < broodstock.Count; i++)
            {
                double bv = broodstock[i].TraitValues.TryGetValue(trait, out double v) ? v : 0.0;
                double d = bv - mean;
                ss += d * d;
            }
            return Math.Sqrt(ss / broodstock.Count);
        }

        /// <summary>
        /// Accuracy (r, correlation between the selection criterion and true breeding
        /// value) for a trait under a given method.
        ///
        /// <list type="bullet">
        /// <item>Phenotypic: <c>r = √h²</c>.</item>
        /// <item>Marker-assisted: partial recovery of the gap to perfect accuracy from a
        /// sparse marker panel — <c>r = √h² + 0.5·(1 − √h²)·density</c>.</item>
        /// <item>Genomic: near-complete recovery, driven by marker density and reference
        /// population size — <c>r = √h² + (1 − √h²)·density·reference</c>.</item>
        /// </list>
        /// Capped below 1.0 (no criterion is perfect).
        /// </summary>
        public static double SelectionAccuracy(
            GeneticTrait trait,
            SelectionMethod method = SelectionMethod.Genomic,
            double markerDensity01 = 0.75,
            double referencePopulation01 = 0.75)
        {
            double h2 = Genetics.GetHeritability(trait);
            double phenotype = Math.Sqrt(Math.Clamp(h2, 0.0, 1.0));
            double gainPotential = 1.0 - phenotype;

            double density = Math.Clamp(markerDensity01, 0.0, 1.0);
            double reference = Math.Clamp(referencePopulation01, 0.0, 1.0);

            double accuracy = method switch
            {
                SelectionMethod.MarkerAssisted => phenotype + 0.5 * gainPotential * density,
                SelectionMethod.Genomic => phenotype + gainPotential * density * reference,
                _ => phenotype
            };

            return Math.Clamp(accuracy, 0.0, 0.99);
        }

        /// <summary>Number of breeders to retain for a selected fraction (≥2).</summary>
        public static int BreedersToSelect(int poolSize, double selectionFraction)
        {
            if (poolSize <= 0) return 0;
            double f = Math.Clamp(selectionFraction, 0.001, 1.0);
            return Math.Clamp((int)Math.Round(poolSize * f), Math.Min(2, poolSize), poolSize);
        }

        /// <summary>
        /// Selection intensity <c>i = φ(z)/p</c> for truncation selection on the top
        /// fraction <paramref name="selectedFraction"/> of a standard normal.
        /// p=0.10 → ≈1.755; p=0.50 → ≈0.798; p=0.01 → ≈2.665.
        /// </summary>
        public static double IntensityFromSelectedFraction(double selectedFraction)
        {
            double p = Math.Clamp(selectedFraction, 1e-4, 0.9999);
            double z = InverseNormalCdf(1.0 - p);
            return NormalPdf(z) / p;
        }

        // ─── Inbreeding ───

        /// <summary>
        /// Wright's inbreeding coefficient of the offspring of a mated pair whose
        /// relatedness (coefficient of relationship, r) is known: <c>F = r/2</c>.
        /// Full sibs (r=0.5) and parent-offspring (r=0.5) give F=0.25; half-sibs
        /// (r=0.25) give F=0.125; unrelated (r=0) gives 0.
        /// </summary>
        public static double InbreedingCoefficientFromRelatedness(double parentalRelatedness)
            => Math.Clamp(parentalRelatedness, 0.0, 2.0) / 2.0;

        /// <summary>
        /// Pedigree-free proxy for cumulative inbreeding after <paramref name="generations"/>
        /// of a closed population of effective size <paramref name="effectivePopulationSize"/>:
        /// <c>F_t = 1 − (1 − 1/(2·N_e))^t</c>. Monotonic in t, decreasing in N_e.
        /// </summary>
        public static double InbreedingCoefficientProxy(double effectivePopulationSize, int generations)
        {
            if (effectivePopulationSize < 1.0) effectivePopulationSize = 1.0;
            if (generations <= 0) return 0.0;

            double perGen = 1.0 - 1.0 / (2.0 * effectivePopulationSize);
            perGen = Math.Clamp(perGen, 0.0, 1.0);
            return Math.Clamp(1.0 - Math.Pow(perGen, generations), 0.0, 1.0);
        }

        /// <summary>
        /// Expected inbreeding of a mated pair's offspring from a pedigree
        /// (Wright's path-counting / tabular kinship). Pass the sire and dam; the result
        /// is the kinship coefficient of the pair. Pedigrees may be arbitrarily deep.
        /// </summary>
        public static double MatedPairInbreeding(PedigreeNode? sire, PedigreeNode? dam)
        {
            var ctx = new KinshipContext(new[] { sire, dam }.Where(n => n is not null).Cast<PedigreeNode>());
            return Kinship(sire, dam, ctx);
        }

        /// <summary>
        /// Wright's inbreeding coefficient of a single individual from its pedigree
        /// (<c>F = kinship(sire, dam)</c>).
        /// </summary>
        public static double CoefficientOfInbreeding(PedigreeNode individual)
        {
            if (individual is null) throw new ArgumentNullException(nameof(individual));
            var ctx = new KinshipContext(new[] { individual });
            return Inbreeding(individual, ctx);
        }

        /// <summary>
        /// Inbreeding-depression penalty for a trait: the fractional reduction in the
        /// trait's value caused by an inbreeding coefficient <paramref name="inbreedingCoefficient"/>.
        /// Fitness traits (disease/lice resistance, stress/cold tolerance) are most
        /// sensitive; production traits less so.
        /// </summary>
        public static double InbreedingDepressionPenalty(GeneticTrait trait, double inbreedingCoefficient)
        {
            double sensitivity = DepressionSensitivity.TryGetValue(trait, out double s) ? s : 1.0;
            return InbreedingDepressionPenalty(inbreedingCoefficient, sensitivity);
        }

        /// <summary>
        /// Inbreeding-depression penalty for an arbitrary sensitivity: fraction of the
        /// trait value lost. Clamped to [0, <see cref="MaxDepressionFraction"/>].
        /// </summary>
        public static double InbreedingDepressionPenalty(double inbreedingCoefficient, double sensitivity)
        {
            double f = Math.Clamp(inbreedingCoefficient, 0.0, 1.0);
            double sens = Math.Max(0.0, sensitivity);
            return Math.Clamp(f * BaseDepressionPerUnitF * sens, 0.0, MaxDepressionFraction);
        }

        /// <summary>
        /// Multiply a trait's value by its inbreeding-depression survival factor
        /// (1.0 = unaffected, 0.75 = quarter loss).
        /// </summary>
        public static double ApplyInbreedingDepression(
            GeneticTrait trait, double traitValue, double inbreedingCoefficient)
            => traitValue * (1.0 - InbreedingDepressionPenalty(trait, inbreedingCoefficient));

        // ─── Internal helpers ───

        private sealed class KinshipContext
        {
            public Dictionary<PedigreeNode, int> Index { get; } = new();
            public Dictionary<(int, int), double> KinshipMemo { get; } = new();
            public Dictionary<PedigreeNode, double> InbreedingMemo { get; } = new();

            public KinshipContext(IEnumerable<PedigreeNode> roots)
            {
                foreach (var root in roots) AssignIndex(root);
            }

            private void AssignIndex(PedigreeNode? node)
            {
                if (node is null || Index.ContainsKey(node)) return;
                AssignIndex(node.Sire);
                AssignIndex(node.Dam);
                if (!Index.ContainsKey(node)) Index[node] = Index.Count;
            }
        }

        private static double Kinship(PedigreeNode? a, PedigreeNode? b, KinshipContext ctx)
        {
            if (a is null || b is null) return 0.0;
            if (ReferenceEquals(a, b)) return 0.5 * (1.0 + Inbreeding(a, ctx));

            // Always expand the descendant (higher index) so recursion terminates.
            if (ctx.Index[a] < ctx.Index[b]) (a, b) = (b, a);

            var key = (ctx.Index[a], ctx.Index[b]);
            if (ctx.KinshipMemo.TryGetValue(key, out double cached)) return cached;

            double result = 0.5 * (Kinship(a.Sire, b, ctx) + Kinship(a.Dam, b, ctx));
            ctx.KinshipMemo[key] = result;
            return result;
        }

        private static double Inbreeding(PedigreeNode node, KinshipContext ctx)
        {
            if (ctx.InbreedingMemo.TryGetValue(node, out double cached)) return cached;
            double f = Kinship(node.Sire, node.Dam, ctx);
            ctx.InbreedingMemo[node] = f;
            return f;
        }

        private static double NormalPdf(double z) =>
            Math.Exp(-0.5 * z * z) / Math.Sqrt(2.0 * Math.PI);

        /// <summary>
        /// Acklam's rational approximation to the inverse standard-normal CDF.
        /// Deterministic; relative error &lt; 1.15e-9 over the open interval (0,1).
        /// </summary>
        private static double InverseNormalCdf(double p)
        {
            if (p <= 0.0) return double.NegativeInfinity;
            if (p >= 1.0) return double.PositiveInfinity;

            const double a1 = -3.969683028665376e+01, a2 = 2.209460984245205e+02;
            const double a3 = -2.759285104469687e+02, a4 = 1.383577518672690e+02;
            const double a5 = -3.066479806614716e+01, a6 = 2.506628277459239e+00;
            const double b1 = -5.447609879822406e+01, b2 = 1.615858368580409e+02;
            const double b3 = -1.556989798598866e+02, b4 = 6.680131188771972e+01;
            const double b5 = -1.328068155288572e+01;
            const double c1 = -7.784894002430293e-03, c2 = -3.223964580411365e-01;
            const double c3 = -2.400758277161838e+00, c4 = -2.549732539343734e+00;
            const double c5 = 4.374664141464968e+00, c6 = 2.938163982698783e+00;
            const double d1 = 7.784695709041462e-03, d2 = 3.224671290700398e-01;
            const double d3 = 2.445134137142996e+00, d4 = 3.754408661907416e+00;
            const double pLow = 0.02425, pHigh = 1.0 - pLow;

            if (p < pLow)
            {
                double q = Math.Sqrt(-2.0 * Math.Log(p));
                return (((((c1 * q + c2) * q + c3) * q + c4) * q + c5) * q + c6) /
                       ((((d1 * q + d2) * q + d3) * q + d4) * q + 1.0);
            }
            if (p > pHigh)
            {
                double q = Math.Sqrt(-2.0 * Math.Log(1.0 - p));
                return -(((((c1 * q + c2) * q + c3) * q + c4) * q + c5) * q + c6) /
                        ((((d1 * q + d2) * q + d3) * q + d4) * q + 1.0);
            }

            double mid = p - 0.5;
            double r = mid * mid;
            return (((((a1 * r + a2) * r + a3) * r + a4) * r + a5) * r + a6) * mid /
                   (((((b1 * r + b2) * r + b3) * r + b4) * r + b5) * r + 1.0);
        }
    }

    /// <summary>
    /// A node in a breeding pedigree. Sire and Dam may be null for founders.
    /// Nodes are compared by object identity, so the same instance must be reused
    /// when a founder appears in multiple lineages.
    /// </summary>
    public sealed class PedigreeNode
    {
        /// <summary>Individual identifier (for display/serialisation only).</summary>
        public string Id { get; }

        /// <summary>Paternal parent, or null for a founder.</summary>
        public PedigreeNode? Sire { get; }

        /// <summary>Maternal parent, or null for a founder.</summary>
        public PedigreeNode? Dam { get; }

        /// <summary>Create a pedigree node.</summary>
        public PedigreeNode(string id, PedigreeNode? sire = null, PedigreeNode? dam = null)
        {
            Id = id;
            Sire = sire;
            Dam = dam;
        }
    }
}
