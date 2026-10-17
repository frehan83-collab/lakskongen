using System;
using LakskongenSim.Biology;
using LakskongenSim.Calibration;
using LakskongenSim.Data;

namespace LakskongenSim.Farm.Land
{
    /// <summary>What may hold a land cohort back from its plan (spec §3.3 "Growth shortfall").</summary>
    public static class GrowthShortfallDrivers
    {
        /// <summary>Dissolved CO2 above the comfort level cuts the appetite.</summary>
        public const string Co2 = "co2";
        /// <summary>The tank's water runs away from the plan's temperature.</summary>
        public const string Temperature = "temperature";
        /// <summary>The feeding plan gives less than the appetite.</summary>
        public const string FeedPlan = "feed_plan";
        /// <summary>The gills' load: ammonia over its limit and oxygen short of saturation.</summary>
        public const string GillHealth = "gill_health";
        /// <summary>Stocking density past the growth knee.</summary>
        public const string Density = "density";

        /// <summary>Every driver, in the order a tie is broken.</summary>
        public static readonly string[] All = { Co2, Temperature, FeedPlan, GillHealth, Density };
    }

    /// <summary>
    /// One land cohort's plan (roadmap F2-15, docs/FARM_TYPES_SPEC.md §3.3): the
    /// weight the farmer's plan says the tank's fish should have reached by now
    /// (the growth law at the setpoint, to appetite, in continuous light, at the
    /// site's competence when stocked), how long the fish have been behind it,
    /// and the growth each driver has cost lately. Kept on the tank and moved
    /// with its fish; the land tick advances it a day at a time.
    /// </summary>
    public sealed class LandCohortPlan
    {
        /// <summary>Days over which the drivers' losses are averaged (an exponential mean).</summary>
        public const double DriverMemoryDays = 30.0;

        /// <summary>Day the cohort was stocked.</summary>
        public int StartDay { get; set; }

        /// <summary>Weight the plan stocked (kg).</summary>
        public double ReleaseWeightKg { get; set; }

        /// <summary>The plan's weight today (kg).</summary>
        public double PlanWeightKg { get; set; }

        /// <summary>The plan's weight CV today.</summary>
        public double PlanWeightCv { get; set; }

        /// <summary>The plan's water temperature (the setpoint when stocked, °C).</summary>
        public double TemperatureC { get; set; } = 12.0;

        /// <summary>The plan's growth multiplier: the genome's × the competence's when stocked.</summary>
        public double GrowthMultiplier { get; set; } = 1.0;

        /// <summary>Days in a row the cohort has stood below the shortfall share of the plan.</summary>
        public int ShortfallDays { get; set; }

        /// <summary>Recent growth lost to CO2 (share of the appetite, averaged).</summary>
        public double LossCo2 { get; set; }

        /// <summary>Recent growth lost to the temperature (share of the plan's growth, averaged).</summary>
        public double LossTemperature { get; set; }

        /// <summary>Recent growth lost to the feeding plan (share of the appetite not fed, averaged).</summary>
        public double LossFeedPlan { get; set; }

        /// <summary>Recent growth lost to the gills' load: TAN and oxygen (share of the appetite, averaged).</summary>
        public double LossGill { get; set; }

        /// <summary>Recent growth lost to density (share of the growth, averaged).</summary>
        public double LossDensity { get; set; }

        /// <summary>
        /// A plan for fish of <paramref name="weightKg"/> stocked on
        /// <paramref name="day"/> at <paramref name="temperatureC"/> with
        /// <paramref name="growthMultiplier"/>.
        /// </summary>
        public static LandCohortPlan Start(int day, double weightKg, double weightCv, double temperatureC, double growthMultiplier)
            => new()
            {
                StartDay = day,
                ReleaseWeightKg = weightKg,
                PlanWeightKg = weightKg,
                PlanWeightCv = weightCv,
                TemperatureC = temperatureC,
                GrowthMultiplier = growthMultiplier,
            };

        /// <summary>A copy (grading moves a plan with its fish).</summary>
        public LandCohortPlan Clone() => (LandCohortPlan)MemberwiseClone();

        /// <summary>
        /// Advance the plan one day on <paramref name="law"/>: the fish the plan
        /// expects grow at the plan's temperature to appetite in continuous light.
        /// </summary>
        public void AdvancePlan(TgcGrowthLaw law)
        {
            if (law == null) throw new ArgumentNullException(nameof(law));
            var conditions = new GrowthConditions(TemperatureC, GrowthMultiplier: GrowthMultiplier,
                DayLengthHours: TgcGrowthLaw.ContinuousLightHours);
            var day = law.GrowOneDay(PlanWeightKg, PlanWeightCv, LifeStage.Seawater, conditions, satiationFcr: 0.0);
            PlanWeightKg = day.WeightKg;
            PlanWeightCv = day.WeightCv;
        }

        /// <summary>
        /// Take today's losses into the drivers' running means (each 0-1).
        /// </summary>
        public void NoteLosses(double co2, double temperature, double feedPlan, double gill, double density)
        {
            double a = 1.0 / DriverMemoryDays;
            LossCo2 += (Clamp01(co2) - LossCo2) * a;
            LossTemperature += (Clamp01(temperature) - LossTemperature) * a;
            LossFeedPlan += (Clamp01(feedPlan) - LossFeedPlan) * a;
            LossGill += (Clamp01(gill) - LossGill) * a;
            LossDensity += (Clamp01(density) - LossDensity) * a;
        }

        /// <summary>
        /// The driver that has cost the most growth lately (<see cref="GrowthShortfallDrivers"/>);
        /// ties go to the earlier one in <see cref="GrowthShortfallDrivers.All"/>.
        /// </summary>
        public string Driver()
        {
            var losses = new[] { LossCo2, LossTemperature, LossFeedPlan, LossGill, LossDensity };
            int best = 0;
            for (int i = 1; i < losses.Length; i++)
                if (losses[i] > losses[best] + 1e-12) best = i;
            return GrowthShortfallDrivers.All[best];
        }

        /// <summary>The actual weight against the plan's (1 = on plan).</summary>
        public double ShareOfPlan(double meanWeightKg)
            => PlanWeightKg > 0.0 ? meanWeightKg / PlanWeightKg : 1.0;

        /// <summary>
        /// Count today: the fish stand below <paramref name="calibration"/>'s
        /// share of the plan or not. Returns true when the shortfall has lasted
        /// the calibration's days (the day the warning holds).
        /// </summary>
        public bool NoteStanding(double meanWeightKg, LandGrowthCalibration calibration)
        {
            if (calibration == null) throw new ArgumentNullException(nameof(calibration));
            ShortfallDays = ShareOfPlan(meanWeightKg) < calibration.ShortfallShare ? ShortfallDays + 1 : 0;
            return ShortfallDays >= calibration.ShortfallDays;
        }

        private static double Clamp01(double v) => double.IsFinite(v) ? Math.Clamp(v, 0.0, 1.0) : 0.0;
    }
}
