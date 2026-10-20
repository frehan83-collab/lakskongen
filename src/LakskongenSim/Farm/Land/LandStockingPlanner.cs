using System;
using LakskongenSim.Biology;
using LakskongenSim.Calibration;
using LakskongenSim.Data;

namespace LakskongenSim.Farm.Land
{
    /// <summary>
    /// How a land tank's stocking is planned against the density limit (spec
    /// §3.3 "Stocking is refused when the projected peak exceeds 85"): the fish
    /// grow on the S2-03 growth law at the tank's setpoint under continuous
    /// light, fed to appetite, until the plan's harvest weight, and lose fish
    /// evenly so that the plan's cycle survival is left at harvest.
    /// </summary>
    /// <param name="ReleaseWeightKg">Weight stocked (kg).</param>
    /// <param name="HarvestWeightKg">The plan harvests the tank the day the fish reach this weight (kg).</param>
    /// <param name="TemperatureC">Tank temperature the fish grow at (°C): the unit's setpoint.</param>
    /// <param name="CycleSurvival">Share of the stocked fish alive at harvest.</param>
    /// <param name="GrowthMultiplier">Strain and smolt-quality multiplier of the growth potential (1 = standard).</param>
    /// <param name="HorizonDays">Days projected at most.</param>
    public readonly record struct LandStockingPlan(double ReleaseWeightKg, double HarvestWeightKg, double TemperatureC,
        double CycleSurvival, double GrowthMultiplier = 1.0, int HorizonDays = LandStockingPlanner.DefaultHorizonDays)
    {
        /// <summary>
        /// The default plan of a template at its setpoint, growing at the plan's
        /// multiplier (<see cref="LandProductionCalibration.PlanGrowthMultiplier"/>,
        /// competence 1.0, F2-15); a caller with a strain multiplies it in.
        /// </summary>
        public static LandStockingPlan For(LandTemplateCalibration template, LandProductionCalibration production)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (production == null) throw new ArgumentNullException(nameof(production));
            return new LandStockingPlan(template.PlanReleaseWeightKg, template.PlanHarvestWeightKg, template.DefaultSetpointC,
                production.CycleSurvival, production.PlanGrowthMultiplier);
        }
    }

    /// <summary>The plan's trajectory of one smolt stocked in a land tank.</summary>
    /// <param name="PeakKgPerSmolt">Highest standing kilos per smolt stocked (weight × survival, kg).</param>
    /// <param name="PeakDay">Days from the stocking to that peak.</param>
    /// <param name="CycleDays">Days from the stocking to the harvest weight (the horizon when it is never reached).</param>
    /// <param name="DailySurvival">Share of the fish alive the next day.</param>
    public readonly record struct LandStockingForecast(double PeakKgPerSmolt, int PeakDay, int CycleDays, double DailySurvival)
    {
        /// <summary>Peak density (kg/m³) of <paramref name="smolts"/> in <paramref name="volumeM3"/>.</summary>
        public double PeakDensityKgPerM3(double smolts, double volumeM3)
            => volumeM3 > 0.0 ? Math.Max(0.0, smolts) * PeakKgPerSmolt / volumeM3 : double.PositiveInfinity;

        /// <summary>The most smolts <paramref name="volumeM3"/> takes with the peak at or under <paramref name="maxDensityKgPerM3"/>.</summary>
        public int MaxSmolts(double volumeM3, double maxDensityKgPerM3)
        {
            if (!(volumeM3 > 0.0) || !(maxDensityKgPerM3 > 0.0)) return 0;
            if (!(PeakKgPerSmolt > 0.0)) return int.MaxValue;
            double room = Math.Floor(maxDensityKgPerM3 * volumeM3 / PeakKgPerSmolt);
            int n = room >= int.MaxValue ? int.MaxValue : (int)Math.Max(0.0, room);
            while (n > 0 && PeakDensityKgPerM3(n, volumeM3) > maxDensityKgPerM3) n--;
            return n;
        }
    }

    /// <summary>The verdict on a land stocking.</summary>
    /// <param name="Allowed">True when the stocking's projected peak is within the limit.</param>
    /// <param name="Reason">Empty, <see cref="LandStockingPlanner.DensityExceeded"/> or <see cref="LandStockingPlanner.OutOfRange"/>.</param>
    /// <param name="PeakDensityKgM3">The projected peak density (kg/m³).</param>
    /// <param name="MaxSmolts">The most smolts the tank takes on this plan.</param>
    /// <param name="Forecast">The projection the verdict rests on.</param>
    public readonly record struct LandStockingVerdict(bool Allowed, string Reason, double PeakDensityKgM3, int MaxSmolts,
        LandStockingForecast Forecast);

    /// <summary>
    /// Projects a land tank's stocking on the S2-03 growth law (roadmap F2-12
    /// acceptance 5). Pure and deterministic. The projection grows the fish at
    /// full appetite with no density or CO2 brake, so it reaches the harvest
    /// weight no later and with no fewer fish than the tank will: the check is
    /// conservative and never lets a stocking through that the tick will see
    /// pass the limit on the plan.
    /// </summary>
    public static class LandStockingPlanner
    {
        /// <summary>Reason key of a refused stocking (spec §3.4 <c>StockLandTank</c>).</summary>
        public const string DensityExceeded = "density_exceeded";

        /// <summary>Reason key of a stocking of no fish (spec §3.4 <c>out_of_range</c>).</summary>
        public const string OutOfRange = "out_of_range";

        /// <summary>Days projected at most: two years, past any land cycle.</summary>
        public const int DefaultHorizonDays = 730;

        /// <summary>Grow one smolt of <paramref name="plan"/> and return its standing kilos' peak.</summary>
        public static LandStockingForecast Project(TgcGrowthLaw law, in LandStockingPlan plan)
        {
            if (law == null) throw new ArgumentNullException(nameof(law));
            double weight = Math.Max(0.0, plan.ReleaseWeightKg);
            double cv = law.Calibration.SmoltWeightCv;
            int horizon = Math.Max(0, plan.HorizonDays);
            var conditions = new GrowthConditions(plan.TemperatureC, GrowthMultiplier: plan.GrowthMultiplier,
                DayLengthHours: TgcGrowthLaw.ContinuousLightHours);
            var weights = new double[horizon + 1];
            weights[0] = weight;
            int cycle = horizon;
            for (int d = 0; d < horizon; d++)
            {
                var grown = law.GrowOneDay(weight, cv, LifeStage.Seawater, conditions, satiationFcr: 0.0);
                weight = grown.WeightKg;
                cv = grown.WeightCv;
                weights[d + 1] = weight;
                if (weight >= plan.HarvestWeightKg)
                {
                    cycle = d + 1;
                    break;
                }
            }
            double survival = Math.Clamp(plan.CycleSurvival, 1e-9, 1.0);
            double daily = cycle > 0 ? Math.Pow(survival, 1.0 / cycle) : 1.0;
            double peak = weights[0];
            int peakDay = 0;
            double alive = 1.0;
            for (int d = 1; d <= cycle; d++)
            {
                alive *= daily;
                double kg = weights[d] * alive;
                if (kg > peak)
                {
                    peak = kg;
                    peakDay = d;
                }
            }
            return new LandStockingForecast(peak, peakDay, cycle, daily);
        }

        /// <summary>
        /// The verdict on stocking <paramref name="smolts"/> into a tank of
        /// <paramref name="volumeM3"/> on <paramref name="plan"/>: refused with
        /// <see cref="DensityExceeded"/> when the projected peak passes
        /// <paramref name="maxDensityKgM3"/> (85 kg/m³ on an HFS lot, [L:R2]).
        /// </summary>
        public static LandStockingVerdict Check(TgcGrowthLaw law, in LandStockingPlan plan, double volumeM3, int smolts,
            double maxDensityKgM3)
        {
            var forecast = Project(law, plan);
            double peak = forecast.PeakDensityKgPerM3(smolts, volumeM3);
            int room = forecast.MaxSmolts(volumeM3, maxDensityKgM3);
            bool allowed = smolts > 0 && peak <= maxDensityKgM3;
            string reason = allowed ? "" : smolts <= 0 ? OutOfRange : DensityExceeded;
            return new LandStockingVerdict(allowed, reason, peak, room, forecast);
        }
    }
}
