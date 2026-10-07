using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Health;

namespace LakskongenSim.Optimization
{
    /// <summary>
    /// Twin-lite 7-day projection (Phase 4.5): read-only forward estimate of
    /// biomass / lice / cash. Steps cloned cohorts (deterministic drift,
    /// sigma 0) + a cloned lice model + cash slope from analytics. NEVER
    /// mutates live state. Cached per day by GameBridge. Labeled PROJECTION.
    /// </summary>
    public static class ForecastTwin
    {
        public const int HorizonDays = 7;

        // Mirror of SimulationEngine SDE calibration (documented duplication).
        private const double SdeK = 5.5;
        private const double SdeAlpha = 20.0;

        public sealed record ForecastResult(
            double[] BiomassKg, double[] LicePerFish, double[] CashNok,
            string Confidence, int GeneratedDay);

        public static ForecastResult Project(
            GameState state, GrowthModel growth, SeaLiceModel lice,
            double tempC, AnalyticsTracker analytics)
        {
            var cohorts = new List<(int count, double weight, LifeStage stage, double genMult, double envMult)>();
            foreach (var pop in state.Populations)
                foreach (var c in pop.Cohorts)
                    cohorts.Add((c.Count, c.MeanWeightKg, c.Stage,
                        c.Genetics?.GetGrowthMultiplier() ?? 1.0, 1.0));

            var liceClone = lice.CloneForProjection();
            var biomass = new double[HorizonDays];
            var liceArr = new double[HorizonDays];
            var cash = new double[HorizonDays];

            double cash0;
            var cashSeries = analytics.GetTimeSeries("balance_nok", 8);
            double slope = 0.0;
            if (cashSeries.Count >= 2)
                slope = (cashSeries[^1] - cashSeries[0]) / (cashSeries.Count - 1);
            cash0 = cashSeries.Count > 0 ? cashSeries[^1] : 0.0;

            for (int d = 0; d < HorizonDays; d++)
            {
                double bio = 0.0;
                for (int i = 0; i < cohorts.Count; i++)
                {
                    var (count, weight, stage, genMult, envMult) = cohorts[i];
                    double w = growth.ApplySDEGrowthOneDay(
                        weight, tempC, SdeK, SdeAlpha, 0.0, stage, envMult);
                    double delta = w - weight;
                    w = Math.Max(0.0, weight + delta * genMult);
                    cohorts[i] = (count, w, stage, genMult, envMult);
                    bio += count * w;
                }
                liceClone.AdvanceDay();
                biomass[d] = bio;
                liceArr[d] = liceClone.AverageLicePerFish;
                cash[d] = cash0 + slope * (d + 1);
            }

            int snaps = analytics.SnapshotCount;
            string conf = snaps >= 30 ? "high" : snaps >= 14 ? "medium" : "low";
            return new ForecastResult(biomass, liceArr, cash, conf, state.Day);
        }
    }
}
