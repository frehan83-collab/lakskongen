using System;
using LakskongenSim.Calibration;
using LakskongenSim.Data;
using LakskongenSim.Environment;
using LakskongenSim.Farm;

namespace LakskongenSim.Biology
{
    /// <summary>
    /// The conditions one cohort grows in today: the input seam of the S2-03
    /// growth law. Whatever holds the fish (a sea pen, an offshore hull, a
    /// land-based tank at a controlled temperature) supplies its water and its
    /// husbandry here; the law never reads a zone, a site or the weather.
    /// </summary>
    /// <param name="TemperatureC">Water temperature the fish live in (°C): <see cref="HoldingWater.TemperatureC"/>.</param>
    /// <param name="RationFraction">Fraction of appetite fed (0-1): the holding's <see cref="RationPolicy"/>.</param>
    /// <param name="AppetiteModifier">Share of a healthy fish's appetite left by the water and the fish's health (0-1): dissolved-oxygen saturation and gill state.</param>
    /// <param name="GrowthMultiplier">Genetics, smolt quality, containment and research on the growth potential (1 = none).</param>
    /// <param name="EnvironmentMultiplier">Stocking-density stress on the growth potential (1 = none).</param>
    /// <param name="SpreadQuality">Feed-spreading precision (0 poor - 1 perfect): widens or narrows the size spread.</param>
    /// <param name="DayLengthHours">Hours of light the holding gets today: natural day length at a sea site, the lighting regime in a land tank (12 = neutral).</param>
    public readonly record struct GrowthConditions(
        double TemperatureC,
        double RationFraction = 1.0,
        double AppetiteModifier = 1.0,
        double GrowthMultiplier = 1.0,
        double EnvironmentMultiplier = 1.0,
        double SpreadQuality = FeedSystem.DefaultSpreadQuality,
        double DayLengthHours = TgcGrowthLaw.NeutralDayLengthHours)
    {
        /// <summary>Healthy fish in <paramref name="water"/>, fed <paramref name="rationFraction"/> of their appetite.</summary>
        public static GrowthConditions In(in HoldingWater water, double rationFraction = 1.0)
            => new(water.TemperatureC, rationFraction);
    }

    /// <summary>One cohort's day on the growth law (per fish).</summary>
    /// <param name="WeightKg">Mean weight after the day (kg).</param>
    /// <param name="WeightCv">Weight coefficient of variation after the day.</param>
    /// <param name="GrowthKg">Weight gained (kg/fish/day; negative when the ration is below maintenance).</param>
    /// <param name="SatiationGrowthKg">Gain healthy fish would have made eating to satiation (kg/fish/day).</param>
    /// <param name="AppetiteFeedKg">What these fish would eat at full ration today (kg feed/fish/day): satiation intake times the appetite modifier.</param>
    /// <param name="EatenFeedKg">Feed eaten (kg/fish/day): the ration fed times the appetite.</param>
    /// <param name="Ration">Eaten share of a healthy fish's satiation (ration fraction × appetite modifier).</param>
    public readonly record struct CohortGrowthDay(
        double WeightKg,
        double WeightCv,
        double GrowthKg,
        double SatiationGrowthKg,
        double AppetiteFeedKg,
        double EatenFeedKg,
        double Ration);

    /// <summary>
    /// Sea-phase growth by the thermal growth coefficient (roadmap S2-03).
    ///
    /// Growth at satiation adds TGC/1000 × E(T) × light to the cube root of the
    /// weight in grams every day, where E(T) is the day's effective degrees: the
    /// water temperature itself up to the optimum (degree-days, the TGC law),
    /// falling to zero at <see cref="GrowthCalibration.UpperGrowthLimitC"/> above
    /// it, and light is the photoperiod factor of the holding's day length. Above
    /// <see cref="GrowthCalibration.TgcValidToKg"/> the gain shrinks toward an
    /// asymptote, so fish grown on pass 6 kg but not without limit.
    ///
    /// Appetite (satiation intake) is the satiation gain times the biological FCR
    /// at satiation, so it follows temperature and fish size (Cho 1992: a feeding
    /// standard = expected gain × feed conversion). A ration below satiation grows
    /// the fish along a decelerating growth-ration curve: growth falls less than
    /// the ration down to the optimum ration, so a restricted ration converts
    /// better; below maintenance the fish lose weight. The cohort's weight CV moves
    /// toward the spread its feeding sets as the fish grow.
    ///
    /// Pure: draws no random numbers, so the daily tick and read-only projections
    /// (ForecastTwin, the feeding plan of U2-04) give the same answer.
    /// </summary>
    public sealed class TgcGrowthLaw
    {
        /// <summary>Release weight of the standard S1 smolt (kg) the initial CV refers to.</summary>
        public const double ReferenceSmoltWeightKg = 0.10;

        /// <summary>Day length (h) at which the photoperiod factor is 1.</summary>
        public const double NeutralDayLengthHours = 12.0;

        /// <summary>Hours in a day: continuous light.</summary>
        public const double ContinuousLightHours = 24.0;

        private const double GramsPerKg = 1000.0;
        private const double TgcScale = 1000.0;
        /// <summary>Smallest weight a starving cohort can fall to (kg): weight stays positive.</summary>
        private const double MinWeightKg = 1e-4;

        /// <summary>The calibration the law runs on.</summary>
        public GrowthCalibration Calibration { get; }

        /// <summary>Create the law on a calibration (the engine passes its registry's).</summary>
        public TgcGrowthLaw(GrowthCalibration calibration)
        {
            Calibration = calibration ?? throw new ArgumentNullException(nameof(calibration));
        }

        /// <summary>The law on the process-wide default calibration.</summary>
        public static TgcGrowthLaw Default { get; } = new(CalibrationRegistry.Default.Growth);

        // ── Temperature ─────────────────────────────────────────────────────

        /// <summary>
        /// Effective degrees one day in water at <paramref name="temperatureC"/>
        /// adds to the TGC sum (°C·day): T up to the optimum (degree-days),
        /// T × (Tmax − T)/(Tmax − Topt) above it (zero at Tmax), zero at or below 0 °C.
        /// </summary>
        public double EffectiveDegrees(double temperatureC)
        {
            if (!(temperatureC > 0.0)) return 0.0;
            var c = Calibration;
            if (temperatureC <= c.OptimalTemperatureC) return temperatureC;
            if (temperatureC >= c.UpperGrowthLimitC) return 0.0;
            return temperatureC * (c.UpperGrowthLimitC - temperatureC) / (c.UpperGrowthLimitC - c.OptimalTemperatureC);
        }

        /// <summary>Temperature (°C) with the most growth: the optimum, or Tmax/2 when the fall above it is gentler.</summary>
        public double PeakGrowthTemperatureC
            => Math.Max(Calibration.OptimalTemperatureC, 0.5 * Calibration.UpperGrowthLimitC);

        /// <summary>
        /// Growth at <paramref name="temperatureC"/> relative to growth at the best
        /// temperature, for the same fish (0-1). 0.31 at 4 °C, 0.38 at 5 °C.
        /// </summary>
        public double RelativeGrowthAt(double temperatureC)
            => EffectiveDegrees(temperatureC) / EffectiveDegrees(PeakGrowthTemperatureC);

        /// <summary>
        /// Thermal appetite index (0-1): 1 where the TGC law counts the full
        /// temperature (up to the optimum), lower in warm water above it. The
        /// UI's appetite reads it with the water and gill modifiers, so a normal
        /// winter does not read as sick fish while a heatwave does.
        /// </summary>
        public double ThermalAppetiteIndex(double temperatureC)
            => temperatureC > 0.0 ? Math.Clamp(EffectiveDegrees(temperatureC) / temperatureC, 0.0, 1.0) : 0.0;

        // ── Size ────────────────────────────────────────────────────────────

        /// <summary>
        /// Share of the TGC gain a fish of <paramref name="weightKg"/> makes: 1 up
        /// to <see cref="GrowthCalibration.TgcValidToKg"/>, then falling linearly in
        /// cube-root weight to 0 at <see cref="GrowthCalibration.AsymptoticWeightKg"/>.
        /// </summary>
        public double SizeFactor(double weightKg)
        {
            var c = Calibration;
            if (weightKg <= c.TgcValidToKg) return 1.0;
            double l = Math.Cbrt(weightKg), lValid = Math.Cbrt(c.TgcValidToKg), lMax = Math.Cbrt(c.AsymptoticWeightKg);
            if (l >= lMax) return 0.0;
            return (lMax - l) / (lMax - lValid);
        }

        /// <summary>
        /// Size factor of the biological FCR: (W / reference)^exponent. Small fish
        /// convert better (0.81 at 100 g, 1.07 at 5 kg with the defaults).
        /// </summary>
        public double FcrSizeFactor(double weightKg)
        {
            var c = Calibration;
            double w = Math.Max(weightKg, MinWeightKg);
            return Math.Pow(w / c.FcrReferenceWeightKg, c.FcrSizeExponent);
        }

        /// <summary>
        /// Growth-law factor of a life stage: 1 for the sea phase (smolt, sea
        /// water, harvest size: the stage is age-based and says nothing about the
        /// biology at sea). Freshwater stages keep the pre-S2-03 relative factors;
        /// no engine holding grows eggs to parr on this law.
        /// </summary>
        public static double StageFactor(LifeStage stage) => stage switch
        {
            LifeStage.Egg => 0.1 / 1.2,
            LifeStage.Alevin => 0.2 / 1.2,
            LifeStage.Fry => 0.5 / 1.2,
            LifeStage.Parr => 0.8 / 1.2,
            _ => 1.0
        };

        // ── Light ───────────────────────────────────────────────────────────

        /// <summary>
        /// Photoperiod factor of the satiation gain for <paramref name="dayLengthHours"/>
        /// of light: 1 + slope × (D − 12) / 12 (1.2 under continuous light and 0.8
        /// in polar night with the default slope).
        /// </summary>
        public double PhotoperiodFactor(double dayLengthHours)
        {
            double d = double.IsFinite(dayLengthHours)
                ? Math.Clamp(dayLengthHours, 0.0, ContinuousLightHours) : NeutralDayLengthHours;
            return Math.Max(0.0, 1.0 + Calibration.PhotoperiodGrowthSlope * (d - NeutralDayLengthHours) / NeutralDayLengthHours);
        }

        // ── Satiation growth ────────────────────────────────────────────────

        /// <summary>
        /// Daily gain (kg/fish) of healthy fish of <paramref name="weightKg"/>
        /// eating to satiation in water at <paramref name="temperatureC"/> under
        /// <paramref name="dayLengthHours"/> of light; the multiplier scales the
        /// cube-root gain (TGC).
        /// </summary>
        public double SatiationGrowthKg(double weightKg, double temperatureC, double multiplier = 1.0,
            LifeStage stage = LifeStage.Seawater, double dayLengthHours = NeutralDayLengthHours)
        {
            if (!(weightKg > 0.0)) return 0.0;
            double gain = CubeRootGain(weightKg, temperatureC, stage) * Math.Max(0.0, multiplier)
                * PhotoperiodFactor(dayLengthHours);
            double l0 = CubeRootG(weightKg);
            double l1 = l0 + gain;
            return (l1 * l1 * l1 - l0 * l0 * l0) / GramsPerKg;
        }

        /// <summary>Satiation gain in the cube root of grams for one day.</summary>
        private double CubeRootGain(double weightKg, double temperatureC, LifeStage stage)
            => Calibration.ThermalGrowthCoefficient / TgcScale * EffectiveDegrees(temperatureC)
               * SizeFactor(weightKg) * StageFactor(stage);

        private static double CubeRootG(double weightKg) => Math.Cbrt(weightKg * GramsPerKg);

        // ── Ration ──────────────────────────────────────────────────────────

        /// <summary>
        /// Growth at ration <paramref name="ration"/> (eaten share of satiation,
        /// 0-1) relative to growth at satiation:
        /// g(r) = (1 − e^(−a(r − m))) / (1 − e^(−a(1 − m))), m = maintenance ration.
        /// 0 at maintenance, 1 at satiation, negative below maintenance.
        /// </summary>
        public double RationGrowthFraction(double ration)
        {
            var c = Calibration;
            double r = Math.Clamp(double.IsFinite(ration) ? ration : 0.0, 0.0, 1.0);
            double m = c.MaintenanceRationFraction, a = c.RationCurvature;
            if (a <= 1e-9) return (r - m) / (1.0 - m);
            return (1.0 - Math.Exp(-a * (r - m))) / (1.0 - Math.Exp(-a * (1.0 - m)));
        }

        /// <summary>
        /// Biological FCR at ration <paramref name="ration"/> relative to the FCR at
        /// satiation: r / g(r). Below 1 between the optimum ration and satiation.
        /// Undefined (infinite) at or below maintenance.
        /// </summary>
        public double RelativeFcrAtRation(double ration)
        {
            double g = RationGrowthFraction(ration);
            return g > 0.0 ? Math.Clamp(ration, 0.0, 1.0) / g : double.PositiveInfinity;
        }

        // ── Size spread ─────────────────────────────────────────────────────

        /// <summary>
        /// Weight CV a cohort settles at under feed spreading
        /// <paramref name="spreadQuality"/> and eaten ration <paramref name="ration"/>.
        /// </summary>
        public double SettledWeightCv(double spreadQuality, double ration)
        {
            var c = Calibration;
            double spreadExtra = FeedSystem.CalculateSizeVarianceCv(spreadQuality) - FeedSystem.BaseSizeVarianceCv;
            double rationExtra = c.RestrictedRationExtraCv * (1.0 - Math.Clamp(ration, 0.0, 1.0));
            return c.BaseWeightCv + spreadExtra + rationExtra;
        }

        /// <summary>
        /// Weight CV of a cohort of <paramref name="weightKg"/> grown from a
        /// standard smolt under standard feeding: the smolt CV relaxed toward the
        /// settled CV, CV* + (CV₀ − CV*) × (L₀/L)^k. A cohort with no CV yet
        /// (stocked by a path that sets none, or loaded from a pre-S2-03 save)
        /// starts here.
        /// </summary>
        public double InitialWeightCv(double weightKg)
        {
            var c = Calibration;
            if (!(weightKg > ReferenceSmoltWeightKg)) return c.SmoltWeightCv;
            double settled = SettledWeightCv(FeedSystem.DefaultSpreadQuality, 1.0);
            double ratio = Math.Cbrt(ReferenceSmoltWeightKg / weightKg);
            return settled + (c.SmoltWeightCv - settled) * Math.Pow(ratio, c.WeightCvRelaxation);
        }

        // ── One day ─────────────────────────────────────────────────────────

        /// <summary>
        /// Grow one cohort (mean weight <paramref name="weightKg"/>, weight CV
        /// <paramref name="weightCv"/>, 0 = not yet known) for one day under
        /// <paramref name="conditions"/>. <paramref name="satiationFcr"/> is the
        /// biological FCR of these fish eating to satiation today
        /// (<c>FeedSystem.SatiationFcr</c>): appetite = satiation gain × it.
        /// </summary>
        public CohortGrowthDay GrowOneDay(double weightKg, double weightCv, LifeStage stage,
            in GrowthConditions conditions, double satiationFcr)
        {
            double w0 = weightKg > 0.0 ? weightKg : 0.0;
            double cv0 = weightCv > 0.0 ? weightCv : InitialWeightCv(w0);
            if (w0 <= 0.0)
                return new CohortGrowthDay(0.0, cv0, 0.0, 0.0, 0.0, 0.0, 0.0);

            double multiplier = Math.Max(0.0, conditions.GrowthMultiplier) * Math.Max(0.0, conditions.EnvironmentMultiplier);
            double satiationGain = SatiationGrowthKg(w0, conditions.TemperatureC, multiplier, stage, conditions.DayLengthHours);
            double appetite = Clamp01(conditions.AppetiteModifier);
            double ration = Clamp01(conditions.RationFraction) * appetite;
            double gain = satiationGain * RationGrowthFraction(ration);
            double w1 = Math.Max(MinWeightKg, w0 + gain);

            double fcr = Math.Max(0.0, satiationFcr);
            double appetiteFeed = satiationGain * fcr * appetite;
            double eaten = satiationGain * fcr * ration;

            double cv1 = cv0;
            if (w1 > w0)
            {
                double l0 = Math.Cbrt(w0), l1 = Math.Cbrt(w1);
                double step = Math.Min(1.0, Calibration.WeightCvRelaxation * (l1 - l0) / l1);
                cv1 = cv0 + (SettledWeightCv(conditions.SpreadQuality, ration) - cv0) * step;
            }
            return new CohortGrowthDay(w1, cv1, w1 - w0, satiationGain, appetiteFeed, eaten, ration);
        }

        private static double Clamp01(double v) => double.IsFinite(v) ? Math.Clamp(v, 0.0, 1.0) : 0.0;
    }
}
