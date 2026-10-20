#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Finance;

namespace LakskongenSim.Tests.Balance
{
    /// <summary>One seeded grow-out of standard S1 smolts at one sea site, on the engine path.</summary>
    public sealed record GrowthCycle(
        int Seed,
        int ProductionArea,
        int ReleaseDay,
        int Stocked,
        int Survivors,
        double StockedKg,
        double FinalKg,
        double DeadKg,
        double FeedKg,
        int? DaysTo4_5Kg,
        int? DaysTo5Kg,
        int? DaysTo5_5Kg,
        double FinalMeanKg,
        double FinalWeightCv,
        IReadOnlyList<double> MeanKgByDay)
    {
        /// <summary>Feed per kg of biomass the fish built, including fish that died after growing.</summary>
        public double BiologicalFcr => FeedKg / Math.Max(1e-9, FinalKg + DeadKg - StockedKg);

        /// <summary>Feed per kg of net harvested gain (industry economic FCR: losses count against it).</summary>
        public double EconomicFcr => FeedKg / Math.Max(1e-9, FinalKg - StockedKg);

        /// <summary>Survival in numbers (escapes and deaths).</summary>
        public double Survival => (double)Survivors / Stocked;

        /// <summary>Mean weight after <paramref name="days"/> at sea (NaN when the cycle ended earlier).</summary>
        public double MeanKgAfter(int days) => days >= 0 && days < MeanKgByDay.Count ? MeanKgByDay[days] : double.NaN;
    }

    /// <summary>
    /// Engine-path grow-out probe (roadmap S2-03): stocks standard S1 smolts in a
    /// site whose zone lies in a production area (by data, <see cref="Zone.ProductionArea"/>),
    /// advances the engine one day at a time and records the weight path, feed,
    /// deaths and the per-cohort weight CV until the stock reaches a stop weight.
    /// Deterministic for a seed; draws only the engine's own streams.
    /// </summary>
    public static class GrowthCycleProbe
    {
        /// <summary>Mid-April (ISO week 16): the spring S1 release day on the sim calendar.</summary>
        public const int SpringReleaseDay = 105;

        /// <summary>Average month on a 365-day year (days).</summary>
        public const double DaysPerMonth = 365.0 / 12.0;

        /// <summary>A fresh engine (events off) with one site in production area <paramref name="area"/>.</summary>
        public static (SimulationEngine Engine, SeaSite Site) EngineWithSite(int seed, int area, string zoneId = "probe",
            bool enableEvents = false)
        {
            var engine = new SimulationEngine { FinanceManager = new FinanceManager(500_000_000m) };
            engine.Initialize(enableEvents: enableEvents, seed: seed);
            var zone = new Zone(zoneId) { ProductionArea = area };
            engine.State.Zones.Add(zone);
            var site = new SeaSite(zoneId.ToLowerInvariant() + "_site", zoneId) { MABTonnes = 50_000 };
            engine.State.SeaSites.Add(site);
            return (engine, site);
        }

        private static double SiteFish(SeaSite site) => site.Pens.Sum(p => (double)p.TotalCount);
        private static double SiteKg(SeaSite site) => site.Pens.Sum(p => p.TotalCount > 0 ? p.GetTotalBiomassKg() : 0.0);
        private static double SiteMeanKg(SeaSite site) => SiteFish(site) > 0 ? SiteKg(site) / SiteFish(site) : 0.0;

        /// <summary>
        /// Run one grow-out: release <paramref name="smolts"/> standard S1 smolts on
        /// <paramref name="releaseDay"/> and grow them until the mean weight reaches
        /// <paramref name="stopKg"/> or <paramref name="horizonDays"/> pass.
        /// </summary>
        public static GrowthCycle Run(int seed, int area, int releaseDay = SpringReleaseDay,
            int smolts = 10_000, double stopKg = 5.5, int horizonDays = 900,
            Action<SimulationEngine, SeaSite>? configure = null)
        {
            var (engine, site) = EngineWithSite(seed, area);
            configure?.Invoke(engine, site);
            if (releaseDay > 0) engine.Advance(releaseDay);
            double feed0 = engine.Feed.TotalFeedUsedKg;
            if (!engine.StockSmolts(site.Id, smolts, "standard").Ok)
                throw new InvalidOperationException("stocking failed");
            double stockedKg = SiteKg(site);

            var path = new List<double> { SiteMeanKg(site) };
            double deadKg = 0.0;
            int? d45 = null, d50 = null, d55 = null;
            for (int day = 1; day <= horizonDays; day++)
            {
                double fishBefore = SiteFish(site);
                engine.Advance(1);
                double mean = SiteMeanKg(site);
                deadKg += Math.Max(0.0, fishBefore - SiteFish(site)) * mean;
                path.Add(mean);
                if (d45 == null && mean >= 4.5) d45 = day;
                if (d50 == null && mean >= 5.0) d50 = day;
                if (d55 == null && mean >= 5.5) d55 = day;
                if (mean >= stopKg) break;
            }

            double cv = WeightCvOf(site);
            return new GrowthCycle(seed, area, releaseDay, smolts, (int)SiteFish(site), stockedKg, SiteKg(site), deadKg,
                engine.Feed.TotalFeedUsedKg - feed0, d45, d50, d55, SiteMeanKg(site), cv, path);
        }

        /// <summary>Count-weighted weight CV across the site's cohorts (S2-03 per-cohort CV).</summary>
        public static double WeightCvOf(SeaSite site)
        {
            double fish = 0, cv = 0;
            foreach (var pen in site.Pens)
                foreach (var c in pen.Cohorts)
                {
                    fish += c.Count;
                    cv += c.Count * CohortCv(c);
                }
            return fish > 0 ? cv / fish : double.NaN;
        }

        private static double CohortCv(LakskongenSim.Biology.FishPopulation.Cohort cohort)
        {
            return cohort.WeightCv;
        }

        /// <summary>Median of a sample (mean of the middle two for an even count).</summary>
        public static double Median(IEnumerable<double> values)
        {
            var s = values.OrderBy(v => v).ToArray();
            if (s.Length == 0) return double.NaN;
            return s.Length % 2 == 1 ? s[s.Length / 2] : 0.5 * (s[s.Length / 2 - 1] + s[s.Length / 2]);
        }
    }
}
