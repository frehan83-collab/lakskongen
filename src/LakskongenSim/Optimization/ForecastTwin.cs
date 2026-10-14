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
    /// biomass / lice / cash. Steps cloned cohorts on the engine's growth law
    /// (S2-03 <see cref="TgcGrowthLaw"/>: pure, no RNG draws, so a UI read can
    /// never move a saved stream — review F521/F835) at each pen's ration, + a
    /// cloned lice model + cash slope from analytics. Never mutates live state.
    /// Cached per day by GameBridge. Labeled PROJECTION.
    /// </summary>
    public static class ForecastTwin
    {
        public const int HorizonDays = 7;

        public sealed record ForecastResult(
            double[] BiomassKg, double[] LicePerFish, double[] CashNok,
            string Confidence, int GeneratedDay);

        public static ForecastResult Project(
            GameState state, SeaLiceModel lice,
            double tempC, AnalyticsTracker analytics, double researchGrowthBonus = 0.0,
            TgcGrowthLaw? law = null)
        {
            law ??= TgcGrowthLaw.Default;
            var cohorts = new List<(int count, double weight, double cv, LifeStage stage, double genMult, double ration)>();
            foreach (var pop in state.Populations)
                foreach (var c in pop.Cohorts)
                    cohorts.Add((c.Count, c.MeanWeightKg, c.WeightCv, c.Stage,
                        c.Genetics?.GetGrowthMultiplier() ?? 1.0, pop.Ration?.EffectiveFraction ?? 1.0));

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
                    var (count, weight, cv, stage, genMult, ration) = cohorts[i];
                    // The daily tick's law (BiologyTickSystem → GrowAndFeedOneDay) in
                    // healthy water at the forecast temperature; the FCR does not
                    // move biomass, so the projection passes none.
                    var conditions = new GrowthConditions(tempC, ration,
                        GrowthMultiplier: genMult * (1.0 + researchGrowthBonus));
                    var day = law.GrowOneDay(weight, cv, stage, conditions, satiationFcr: 0.0);
                    cohorts[i] = (count, day.WeightKg, day.WeightCv, stage, genMult, ration);
                    bio += count * day.WeightKg;
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
