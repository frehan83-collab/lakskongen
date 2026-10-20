using System;
using LakskongenSim.Calibration;

namespace LakskongenSim.Harvest
{
    /// <summary>The industry's quality grades after slaughter (Mattilsynet slaughter reports).</summary>
    public enum HarvestGrade
    {
        /// <summary>Superior: the reference quality the spot price is quoted for.</summary>
        Superior = 0,
        /// <summary>Ordinary: minor defects.</summary>
        Ordinary = 1,
        /// <summary>Production: wounds, maturation, deformities; sold for processing at a discount.</summary>
        Production = 2,
    }

    /// <summary>Shares of a lot's gutted kilos in each grade (they sum to 1).</summary>
    public readonly record struct GradeShares(double Superior, double Ordinary, double Production)
    {
        /// <summary>The lot's mean price factor over the grades.</summary>
        public double PriceFactor(HarvestCalibration calibration)
            => Superior + Ordinary * calibration.OrdinaryPriceFactor + Production * calibration.ProductionPriceFactor;

        /// <summary>Share of grade <paramref name="grade"/>.</summary>
        public double Of(HarvestGrade grade) => grade switch
        {
            HarvestGrade.Superior => Superior,
            HarvestGrade.Ordinary => Ordinary,
            _ => Production
        };
    }

    /// <summary>
    /// What a pen's fish carry to the slaughterhouse (S2-04): the inputs the
    /// grading reads. Whatever holds the fish fills it (sea pen, offshore pen,
    /// land tank): the grading never reads a zone, the weather or a site.
    /// </summary>
    /// <param name="MeanLiveWeightKg">Mean live weight of the fish slaughtered (kg).</param>
    /// <param name="ColdExposure">Memory of cold water, 0-1 (<c>FishPopulation.ColdExposure</c>): winter wounds.</param>
    /// <param name="TreatmentInjuryLoad">Delousing injury load (<c>DelousingInjuryState.InjuryLoad</c>, 0-3).</param>
    /// <param name="AdultFemaleLicePerFish">Adult female lice per fish where the fish live (0 in a land tank).</param>
    /// <param name="ExtraProductionShare">
    /// A holding's own downgrade (0-1): the farm-types hooks fill it (a land
    /// farm's maturation index or an unpurged off-flavour, roadmap F2). 0 today.
    /// </param>
    public readonly record struct HarvestCondition(
        double MeanLiveWeightKg,
        double ColdExposure = 0.0,
        double TreatmentInjuryLoad = 0.0,
        double AdultFemaleLicePerFish = 0.0,
        double ExtraProductionShare = 0.0);

    /// <summary>
    /// Quality grading of a slaughtered lot (S2-04): independent causes of
    /// downgrading to production (defects, winter wounds, delousing injuries,
    /// lice wounds, maturation, the holding's own causes) combine as
    /// 1 − Π(1 − p); a small share of the rest is ordinary. Expected shares by
    /// weight, no random numbers. Calibrated on Norway 2024 (Fiskehelserapporten
    /// 2024 §5.8): ~90% superior after summer, ~65% after a winter at sea.
    /// </summary>
    public static class HarvestGrading
    {
        /// <summary>Grade shares of a lot in <paramref name="condition"/>.</summary>
        public static GradeShares Grade(HarvestCalibration calibration, in HarvestCondition condition)
        {
            if (calibration == null) throw new ArgumentNullException(nameof(calibration));
            double keep = 1.0;
            keep *= 1.0 - Clamp01(calibration.BaseProductionShare);
            keep *= 1.0 - Clamp01(calibration.WinterWoundProductionShare * Clamp01(condition.ColdExposure));
            keep *= 1.0 - Math.Min(calibration.MaxTreatmentWoundProductionShare,
                calibration.TreatmentWoundProductionSharePerInjury * Positive(condition.TreatmentInjuryLoad));
            keep *= 1.0 - Math.Min(calibration.MaxLiceWoundProductionShare,
                calibration.LiceWoundProductionSharePerLouse * Positive(condition.AdultFemaleLicePerFish));
            keep *= 1.0 - Math.Min(calibration.MaxMaturationProductionShare,
                calibration.MaturationProductionSharePerKg * Positive(condition.MeanLiveWeightKg - calibration.MaturationStartWeightKg));
            keep *= 1.0 - Clamp01(condition.ExtraProductionShare);

            double production = Clamp01(1.0 - keep);
            double ordinary = (1.0 - production) * Clamp01(calibration.OrdinaryShare);
            double superior = Math.Max(0.0, 1.0 - production - ordinary);
            return new GradeShares(superior, ordinary, production);
        }

        /// <summary>
        /// The cold-water memory after one more day in water of
        /// <paramref name="temperatureC"/>: an exponential average (e-folding
        /// <see cref="HarvestCalibration.ColdExposureEFoldingDays"/>) of the days
        /// below <see cref="HarvestCalibration.WinterWoundTemperatureC"/>.
        /// </summary>
        public static double NextColdExposure(HarvestCalibration calibration, double exposure, double temperatureC)
        {
            double cold = temperatureC < calibration.WinterWoundTemperatureC ? 1.0 : 0.0;
            double keep = Math.Exp(-1.0 / Math.Max(1.0, calibration.ColdExposureEFoldingDays));
            return Clamp01(Clamp01(exposure) * keep + cold * (1.0 - keep));
        }

        private static double Clamp01(double v) => double.IsFinite(v) ? Math.Clamp(v, 0.0, 1.0) : 0.0;

        private static double Positive(double v) => double.IsFinite(v) ? Math.Max(0.0, v) : 0.0;
    }
}
