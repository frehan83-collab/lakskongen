using System;
using System.Collections.Generic;

namespace LakskongenSim.Finance
{
    /// <summary>A group of fish in a production plan: where it stands and how many at what weight.</summary>
    /// <param name="SiteId">Sea site holding the fish.</param>
    /// <param name="ProductionArea">Production area whose water the plan grows it in.</param>
    /// <param name="Count">Fish today.</param>
    /// <param name="WeightKg">Mean live weight today (kg).</param>
    public readonly record struct PlannedCohort(string SiteId, int ProductionArea, double Count, double WeightKg);

    /// <summary>How a farmer plans biomass (S2-01a: the projected-peak stocking check).</summary>
    /// <param name="ThermalGrowthCoefficient">TGC (unit: g^(1/3) per 1000 degree-days): W^(1/3) grows by TGC x T / 1000 a day.</param>
    /// <param name="HarvestWeightKg">The plan harvests a cohort the day it reaches this live weight.</param>
    /// <param name="DailySurvival">Share of fish alive the next day (from the cycle mortality the plan expects).</param>
    /// <param name="HorizonDays">Days projected at most (a cohort still short of harvest weight stands to the end).</param>
    public sealed record CapacityPlanSettings(double ThermalGrowthCoefficient, double HarvestWeightKg,
        double DailySurvival, int HorizonDays);

    /// <summary>Peak standing biomass of a plan, for the whole set of cohorts and per site.</summary>
    public sealed class CapacityProjection
    {
        /// <summary>Highest total standing biomass (t) over the horizon.</summary>
        public double PeakTonnes { get; init; }

        /// <summary>Days from the start of the plan to <see cref="PeakTonnes"/> (the first such day).</summary>
        public int PeakDay { get; init; }

        /// <summary>Highest standing biomass (t) of each site.</summary>
        public IReadOnlyDictionary<string, double> PeakTonnesBySite { get; init; } = new Dictionary<string, double>();

        /// <summary>Days to each site's peak.</summary>
        public IReadOnlyDictionary<string, int> PeakDayBySite { get; init; } = new Dictionary<string, int>();
    }

    /// <summary>
    /// Projects a production plan forward on the area's climatological sea
    /// temperature (S2-01a). Each cohort grows by the thermal growth
    /// coefficient model, loses fish at the plan's daily survival, and is
    /// harvested the day it reaches the plan's harvest weight; the result is
    /// the plan's peak standing biomass and when it falls. Pure and
    /// deterministic: no RNG, no engine state (the farmer's plan, not the
    /// simulated growth).
    /// </summary>
    public static class CapacityPlanner
    {
        private const double GramsPerKg = 1000.0;
        private const double KgPerTonne = 1000.0;
        private const double DegreeDaysPerTgcUnit = 1000.0;

        /// <summary>
        /// Project <paramref name="cohorts"/> from sim day <paramref name="startDay"/>.
        /// <paramref name="temperatureC"/> gives the water temperature (°C) of a
        /// production area on a sim day.
        /// </summary>
        public static CapacityProjection Project(IReadOnlyList<PlannedCohort> cohorts, int startDay,
            CapacityPlanSettings settings, Func<int, int, double> temperatureC)
        {
            if (cohorts == null) throw new ArgumentNullException(nameof(cohorts));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (temperatureC == null) throw new ArgumentNullException(nameof(temperatureC));

            int n = cohorts.Count;
            var siteIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            var siteOf = new int[n];
            var count = new double[n];
            var cubeRootGrams = new double[n];
            var active = new bool[n];
            for (int i = 0; i < n; i++)
            {
                var c = cohorts[i];
                string site = c.SiteId ?? "";
                if (!siteIndex.TryGetValue(site, out int s))
                {
                    s = siteIndex.Count;
                    siteIndex[site] = s;
                }
                siteOf[i] = s;
                count[i] = Math.Max(0.0, c.Count);
                cubeRootGrams[i] = Math.Cbrt(Math.Max(0.0, c.WeightKg) * GramsPerKg);
                active[i] = count[i] > 0.0;
            }

            var siteSum = new double[siteIndex.Count];
            var sitePeak = new double[siteIndex.Count];
            var sitePeakDay = new int[siteIndex.Count];
            double peak = 0.0;
            int peakDay = 0;
            double harvestCubeRoot = Math.Cbrt(settings.HarvestWeightKg * GramsPerKg);
            double tgcPerDegreeDay = settings.ThermalGrowthCoefficient / DegreeDaysPerTgcUnit;
            int horizon = Math.Max(0, settings.HorizonDays);

            for (int day = 0; day <= horizon; day++)
            {
                Array.Clear(siteSum, 0, siteSum.Length);
                double total = 0.0;
                bool any = false;
                for (int i = 0; i < n; i++)
                {
                    if (!active[i]) continue;
                    any = true;
                    double g = cubeRootGrams[i];
                    double kg = count[i] * g * g * g / GramsPerKg;
                    total += kg;
                    siteSum[siteOf[i]] += kg;
                }
                if (!any) break;

                if (total > peak) { peak = total; peakDay = day; }
                for (int s = 0; s < siteSum.Length; s++)
                    if (siteSum[s] > sitePeak[s]) { sitePeak[s] = siteSum[s]; sitePeakDay[s] = day; }

                // The next day: a cohort at harvest weight leaves; the rest grow and thin.
                for (int i = 0; i < n; i++)
                {
                    if (!active[i]) continue;
                    if (cubeRootGrams[i] >= harvestCubeRoot) { active[i] = false; continue; }
                    double t = Math.Max(0.0, temperatureC(cohorts[i].ProductionArea, startDay + day));
                    cubeRootGrams[i] += tgcPerDegreeDay * t;
                    count[i] *= settings.DailySurvival;
                }
            }

            var bySite = new Dictionary<string, double>(StringComparer.Ordinal);
            var daysBySite = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var kv in siteIndex)
            {
                bySite[kv.Key] = sitePeak[kv.Value] / KgPerTonne;
                daysBySite[kv.Key] = sitePeakDay[kv.Value];
            }
            return new CapacityProjection
            {
                PeakTonnes = peak / KgPerTonne,
                PeakDay = peakDay,
                PeakTonnesBySite = bySite,
                PeakDayBySite = daysBySite,
            };
        }

        /// <summary>
        /// Daily survival of a plan that expects to lose <paramref name="cycleLossFraction"/>
        /// of the fish over <paramref name="cycleDays"/> days at sea.
        /// </summary>
        public static double DailySurvivalFor(double cycleLossFraction, int cycleDays)
        {
            if (cycleDays <= 0) return 1.0;
            double survive = Math.Clamp(1.0 - cycleLossFraction, 0.0, 1.0);
            return Math.Pow(survive, 1.0 / cycleDays);
        }
    }
}
