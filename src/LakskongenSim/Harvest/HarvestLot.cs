using System;
using LakskongenSim.Calibration;

namespace LakskongenSim.Harvest
{
    /// <summary>
    /// The fish of one slaughtered batch on their way to the price (S2-04): live
    /// kilos gutted at the HOG yield, split into size classes on the gutted
    /// weight and into grades by each pen's condition. Pure arithmetic, no
    /// random numbers: a quote and the slaughter itself build the same lot.
    /// </summary>
    public sealed class HarvestLot
    {
        private readonly HarvestCalibration _calibration;
        private readonly double[] _hogKgByClass = new double[SizeClassPriceLadder.ClassCount];
        private double _sumSquaresKg2;

        /// <summary>An empty lot priced on <paramref name="calibration"/>.</summary>
        public HarvestLot(HarvestCalibration calibration)
        {
            _calibration = calibration ?? throw new ArgumentNullException(nameof(calibration));
        }

        /// <summary>Fish in the lot.</summary>
        public int Fish { get; private set; }

        /// <summary>Live kilos.</summary>
        public double LiveKg { get; private set; }

        /// <summary>Gutted (HOG) kilos.</summary>
        public double HogKg { get; private set; }

        /// <summary>Gutted kilos graded superior.</summary>
        public double SuperiorHogKg { get; private set; }

        /// <summary>Gutted kilos graded ordinary.</summary>
        public double OrdinaryHogKg { get; private set; }

        /// <summary>Gutted kilos graded production.</summary>
        public double ProductionHogKg { get; private set; }

        /// <summary>
        /// Reference-equivalent kilos: Σ gutted kilos × size-class factor × grade
        /// factor. Gross sales = this × the base price (spot or contract).
        /// </summary>
        public double ValueKg { get; private set; }

        /// <summary>Gutted kilos per size class (&lt;1, 1-2 ... 7+ kg).</summary>
        public ReadOnlySpan<double> HogKgBySizeClass => _hogKgByClass;

        /// <summary>Gutted kilos per size class as a fresh array.</summary>
        public double[] HogKgBySizeClassArray() => (double[])_hogKgByClass.Clone();

        /// <summary>Mean live weight (kg).</summary>
        public double MeanLiveWeightKg => Fish > 0 ? LiveKg / Fish : 0.0;

        /// <summary>Weight CV of the lot (within and between its cohorts).</summary>
        public double WeightCv
        {
            get
            {
                if (Fish <= 0 || LiveKg <= 0.0) return 0.0;
                double mean = LiveKg / Fish;
                double variance = Math.Max(0.0, _sumSquaresKg2 / Fish - mean * mean);
                return Math.Sqrt(variance) / mean;
            }
        }

        /// <summary>Mean size-class price factor.</summary>
        public double SizeFactor => SizeClassPriceLadder.MeanFactor(_calibration, _hogKgByClass);

        /// <summary>Mean grade price factor.</summary>
        public double GradeFactor => HogKg > 0.0
            ? (SuperiorHogKg + OrdinaryHogKg * _calibration.OrdinaryPriceFactor
               + ProductionHogKg * _calibration.ProductionPriceFactor) / HogKg
            : 1.0;

        /// <summary>Reference-equivalent kilos per gutted kilo: the lot's price relative to the reference.</summary>
        public double ValueFactor => HogKg > 0.0 ? ValueKg / HogKg : 1.0;

        /// <summary>Superior share of the gutted kilos.</summary>
        public double SuperiorShare => HogKg > 0.0 ? SuperiorHogKg / HogKg : 0.0;

        /// <summary>Ordinary share of the gutted kilos.</summary>
        public double OrdinaryShare => HogKg > 0.0 ? OrdinaryHogKg / HogKg : 0.0;

        /// <summary>Production share of the gutted kilos.</summary>
        public double ProductionShare => HogKg > 0.0 ? ProductionHogKg / HogKg : 0.0;

        /// <summary>
        /// Add <paramref name="count"/> fish of one cohort (mean live weight,
        /// weight CV) from a pen in <paramref name="condition"/>.
        /// </summary>
        public void AddFish(int count, double meanLiveKg, double weightCv, in HarvestCondition condition)
        {
            if (count <= 0 || !(meanLiveKg > 0.0)) return;
            Span<double> classes = stackalloc double[SizeClassPriceLadder.ClassCount];
            double hog = SizeClassPriceLadder.AddCohort(classes, count, meanLiveKg, weightCv, _calibration.HogYieldFraction);
            var grades = HarvestGrading.Grade(_calibration, condition with { MeanLiveWeightKg = meanLiveKg });
            double gradeFactor = grades.PriceFactor(_calibration);

            double sizeValue = 0.0;
            for (int c = 0; c < classes.Length; c++)
            {
                _hogKgByClass[c] += classes[c];
                sizeValue += classes[c] * SizeClassPriceLadder.Factor(_calibration, c);
            }

            double cv = double.IsFinite(weightCv) && weightCv > 0.0 ? weightCv : 0.0;
            double sd = cv * meanLiveKg;
            Fish += count;
            LiveKg += count * meanLiveKg;
            _sumSquaresKg2 += count * (sd * sd + meanLiveKg * meanLiveKg);
            HogKg += hog;
            SuperiorHogKg += hog * grades.Superior;
            OrdinaryHogKg += hog * grades.Ordinary;
            ProductionHogKg += hog * grades.Production;
            ValueKg += sizeValue * gradeFactor;
        }
    }
}
