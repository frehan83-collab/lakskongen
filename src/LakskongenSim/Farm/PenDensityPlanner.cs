using System;
using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Environment;

namespace LakskongenSim.Farm
{
    /// <summary>
    /// How a pen's stocking is planned against the density limit (S2-01b): the
    /// smolts grow on the S2-03 growth law in their production area's normal
    /// sea temperature and natural day length, fed to appetite, lose fish at the
    /// plan's daily survival, and are harvested the day they reach the plan's
    /// harvest weight.
    /// </summary>
    /// <param name="ProductionArea">Area whose climatology the fish grow in.</param>
    /// <param name="StartDay">Sim day of the stocking.</param>
    /// <param name="ReleaseWeightKg">Smolt weight at stocking (kg).</param>
    /// <param name="GrowthMultiplier">The strain's and smolt quality's growth multiplier (1 = standard).</param>
    /// <param name="HarvestWeightKg">The plan harvests the pen the day the fish reach this weight (kg).</param>
    /// <param name="DailySurvival">Share of the fish alive the next day.</param>
    /// <param name="HorizonDays">Days projected at most.</param>
    public readonly record struct PenStockingPlan(int ProductionArea, int StartDay, double ReleaseWeightKg,
        double GrowthMultiplier, double HarvestWeightKg, double DailySurvival, int HorizonDays);

    /// <summary>
    /// The plan's trajectory of one stocked smolt (S2-01b): the most biomass a
    /// smolt stocked today stands for at any day of the plan (its weight times
    /// the share of fish still alive), and when.
    /// </summary>
    /// <param name="PeakKgPerSmolt">Highest standing kilos per smolt stocked (kg).</param>
    /// <param name="PeakDay">Days from the stocking to that peak.</param>
    /// <param name="WeightAtPeakKg">Mean live weight on the peak day (kg).</param>
    /// <param name="SurvivalAtPeak">Share of the stocked fish alive on the peak day.</param>
    public readonly record struct PenDensityForecast(double PeakKgPerSmolt, int PeakDay, double WeightAtPeakKg,
        double SurvivalAtPeak)
    {
        /// <summary>The pen's peak density (kg/m³) when <paramref name="smolts"/> are stocked in <paramref name="volumeM3"/>.</summary>
        public double PeakDensityKgPerM3(int smolts, double volumeM3)
            => volumeM3 > 0.0 ? Math.Max(0, smolts) * PeakKgPerSmolt / volumeM3 : double.PositiveInfinity;

        /// <summary>
        /// The most smolts <paramref name="volumeM3"/> can take with the plan's
        /// peak density at or under <paramref name="maxDensityKgPerM3"/>. The
        /// peak is linear in the count (the plan grows each fish the same), so
        /// the room is the limit's kilos over the peak kilos per smolt.
        /// </summary>
        public int MaxSmolts(double volumeM3, double maxDensityKgPerM3)
        {
            if (volumeM3 <= 0.0 || maxDensityKgPerM3 <= 0.0) return 0;
            if (PeakKgPerSmolt <= 0.0) return int.MaxValue;
            double room = Math.Floor(maxDensityKgPerM3 * volumeM3 / PeakKgPerSmolt);
            int n = room >= int.MaxValue ? int.MaxValue : (int)Math.Max(0.0, room);
            while (n > 0 && PeakDensityKgPerM3(n, volumeM3) > maxDensityKgPerM3) n--;
            return n;
        }
    }

    /// <summary>
    /// Projects a pen's stocking on the S2-03 growth law (S2-01b). Pure and
    /// deterministic: no RNG, no engine state; the density check of the
    /// stocking verbs and the stocking preview the UI shows call the same
    /// projection.
    /// </summary>
    public static class PenDensityPlanner
    {
        /// <summary>
        /// Grow one smolt of <paramref name="plan"/> on <paramref name="law"/>
        /// and return the peak of its standing kilos (weight × survival) until
        /// the plan harvests it or the horizon ends.
        /// </summary>
        public static PenDensityForecast Project(TgcGrowthLaw law, in PenStockingPlan plan)
        {
            if (law == null) throw new ArgumentNullException(nameof(law));
            int area = ProductionAreas.HasClimatology(plan.ProductionArea) ? plan.ProductionArea : ProductionAreas.NationalReference;
            double latitude = ProductionAreas.LatitudeDeg(area);
            double survivalPerDay = Math.Clamp(plan.DailySurvival, 0.0, 1.0);
            double weight = Math.Max(0.0, plan.ReleaseWeightKg);
            double cv = law.Calibration.SmoltWeightCv;
            double survival = 1.0;

            double peak = weight;
            int peakDay = 0;
            double weightAtPeak = weight;
            double survivalAtPeak = 1.0;
            int horizon = Math.Max(0, plan.HorizonDays);
            for (int d = 0; d < horizon && weight < plan.HarvestWeightKg; d++)
            {
                int day = plan.StartDay + d;
                var conditions = new GrowthConditions(ProductionAreas.ClimatologyC(area, day),
                    GrowthMultiplier: plan.GrowthMultiplier,
                    DayLengthHours: DayLength.Hours(latitude, SimCalendar.DayOfYear(day)));
                var grown = law.GrowOneDay(weight, cv, LifeStage.Seawater, conditions, satiationFcr: 0.0);
                weight = grown.WeightKg;
                cv = grown.WeightCv;
                survival *= survivalPerDay;
                double kg = weight * survival;
                if (kg > peak)
                {
                    peak = kg;
                    peakDay = d + 1;
                    weightAtPeak = weight;
                    survivalAtPeak = survival;
                }
            }
            return new PenDensityForecast(peak, peakDay, weightAtPeak, survivalAtPeak);
        }
    }
}
