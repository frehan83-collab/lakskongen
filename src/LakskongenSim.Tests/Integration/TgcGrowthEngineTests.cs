using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using LakskongenSim.Optimization;
using LakskongenSim.Tests.Balance;
using Xunit;

namespace LakskongenSim.Tests.Integration
{
    /// <summary>
    /// Roadmap S2-03 on the engine path: every holding grows on the TGC law in
    /// its own water (the seam the land-based and offshore farm types use), the
    /// ration policy restricts growth and feed, the cohort weight CV is kept and
    /// saved, and the projection twin runs the same law.
    /// </summary>
    public class TgcGrowthEngineTests
    {
        private static SimulationEngine RichEngine(int seed, bool events = false)
        {
            var engine = new SimulationEngine { FinanceManager = new FinanceManager(5_000_000_000m) };
            engine.Initialize(enableEvents: events, seed: seed);
            return engine;
        }

        /// <summary>A site with no zone in PO4 water: no blooms or zone chemistry, only the sea's temperature.</summary>
        private static SeaSite OpenWaterSite(SimulationEngine engine, string id)
        {
            engine.State.HomeProductionArea = 4;
            var site = new SeaSite(id) { MABTonnes = 5_000 };
            engine.State.SeaSites.Add(site);
            return site;
        }

        private static void StripEverywhere(JsonNode? node, params string[] names)
        {
            if (node is JsonObject obj)
            {
                foreach (var name in names) obj.Remove(name);
                foreach (var child in obj.Select(kv => kv.Value).ToList()) StripEverywhere(child, names);
            }
            else if (node is JsonArray array)
            {
                foreach (var child in array) StripEverywhere(child, names);
            }
        }

        private static double MeanWeight(System.Collections.Generic.IEnumerable<FishPopulation> pens)
        {
            var list = pens.ToList();
            return list.Sum(p => p.GetTotalBiomassKg()) / Math.Max(1, list.Sum(p => p.TotalCount));
        }

        [Fact]
        public void ALandTankAtTwelveDegrees_GrowsOnTheSameLaw_InItsOwnWaterAndLight()
        {
            var engine = RichEngine(53);
            Assert.True(engine.BuildRASFacility("Tank", RASScale.Small).Ok);
            var ras = engine.RASFacilities.Single();
            ras.ConstructionDays = 1;
            engine.Advance(2);
            Assert.True(engine.StockRASFacility(ras.Id, 10_000).Ok);
            var cohort = ras.Tanks.Single().Cohorts.Single();
            var water = engine.HoldingWaterOf(ras.Tanks.Single());
            Assert.Equal(HoldingKind.LandTank, water.Kind);
            Assert.Equal(TgcGrowthLaw.ContinuousLightHours, engine.DayLengthHoursOf(water));

            // The law alone, fed the tank's controlled water each day, must give
            // the tank's weight: nothing else (no sea, no zone) reaches the fish.
            var law = engine.Growth.Law;
            double weight = cohort.MeanWeightKg, cv = cohort.WeightCv;
            double multiplier = cohort.Genetics!.GetGrowthMultiplier()
                * SmoltQualityRegistry.Get(cohort.Quality).GrowthMultiplier;
            for (int day = 0; day < 90; day++)
            {
                var conditions = new GrowthConditions(ras.CurrentWaterTemp, GrowthMultiplier: multiplier,
                    DayLengthHours: TgcGrowthLaw.ContinuousLightHours);
                var grown = law.GrowOneDay(weight, cv, cohort.Stage, conditions, satiationFcr: 0.0);
                weight = grown.WeightKg;
                cv = grown.WeightCv;
                engine.Advance(1);
            }
            Assert.Equal(weight, cohort.MeanWeightKg, 9);
            Assert.Equal(cv, cohort.WeightCv, 9);
            // January-March: the PO4 sea is near 6 °C, the tank at 12 °C under
            // continuous light grows ~2.4x as many degree-days.
            Assert.True(engine.Weather.CurrentTemperatureC < ras.CurrentWaterTemp - 3.0);
            double tgc = (Math.Cbrt(cohort.MeanWeightKg * 1000) - Math.Cbrt(0.10 * 1000)) / (12.0 * 90) * 1000;
            Assert.InRange(tgc, 3.0 * 1.2 * multiplier * 0.99, 3.0 * 1.2 * multiplier * 1.01);
        }

        [Fact]
        public void ARestrictedRation_OnThePen_GrowsLess_ConvertsBetter_AndCostsLessPerDay()
        {
            // The engine-level half of U2-04's acceptance 1: 120 days at 70% of
            // appetite against full appetite, same seed, same water.
            (double gainKg, double feedKg) Run(double ration)
            {
                var engine = RichEngine(21);
                var site = OpenWaterSite(engine, "ration");
                engine.Advance(GrowthCycleProbe.SpringReleaseDay);
                Assert.True(engine.StockSmolts(site.Id, 10_000, "standard").Ok);
                engine.Advance(60);
                site.Pens.Single().Ration = RationPolicy.OfAppetite(ration);
                double biomass0 = site.Pens.Sum(p => p.GetTotalBiomassKg());
                int fish0 = site.Pens.Sum(p => p.TotalCount);
                double feed0 = engine.Feed.TotalFeedUsedKg;
                engine.Advance(120);
                double perFishGain = MeanWeight(site.Pens) - biomass0 / fish0;
                return (perFishGain * site.Pens.Sum(p => p.TotalCount), engine.Feed.TotalFeedUsedKg - feed0);
            }

            var full = Run(1.0);
            var restricted = Run(0.7);
            Assert.True(restricted.gainKg < full.gainKg * 0.9, $"gain {restricted.gainKg:N0} vs {full.gainKg:N0} kg");
            Assert.True(restricted.feedKg / restricted.gainKg < full.feedKg / full.gainKg,
                $"FCR {restricted.feedKg / restricted.gainKg:F3} at 70% vs {full.feedKg / full.gainKg:F3}");
            Assert.True(restricted.feedKg / 120 < full.feedKg / 120 * 0.8, "feed (and its cost) per day");
        }

        [Fact]
        public void ACohortsWeightCv_AndAPensRation_SurviveASave_AndTheGameContinuesExactly()
        {
            var engine = RichEngine(31);
            var site = OpenWaterSite(engine, "saved");
            Assert.True(engine.StockSmolts(site.Id, 10_000, "standard").Ok);
            engine.Advance(120);
            site.Pens.Single().Ration = RationPolicy.OfAppetite(0.8);
            engine.Advance(30);
            var cohort = site.Pens.Single().Cohorts.Single();
            Assert.InRange(cohort.WeightCv, 0.15, 0.35);

            var resumed = SaveStateComparer.Resume(SaveStateComparer.Serialize(engine), events: false);
            var resumedPen = resumed.State.SeaSites.Single(s => s.Id == "saved").Pens.Single();
            Assert.Equal(cohort.WeightCv, resumedPen.Cohorts.Single().WeightCv, 15);
            Assert.Equal(0.8, resumedPen.Ration.FractionOfAppetite, 15);

            engine.Advance(60);
            resumed.Advance(60);
            Assert.Equal("identical", SaveStateComparer.FirstDifference(
                SaveStateComparer.Normalize(SaveStateComparer.Serialize(engine)),
                SaveStateComparer.Normalize(SaveStateComparer.Serialize(resumed))));
        }

        [Fact]
        public void ASaveFromBeforeS2_03_LoadsWithFullRations_AndCvsSetFromTheWeights()
        {
            var engine = RichEngine(32);
            var site = OpenWaterSite(engine, "legacy");
            Assert.True(engine.StockSmolts(site.Id, 10_000, "standard").Ok);
            engine.Advance(200);
            var json = JsonNode.Parse(SaveStateComparer.Serialize(engine))!;
            // Strip what S2-03 added everywhere a population is written (the
            // population list and each site's pens): the save a v4 game wrote.
            StripEverywhere(json, "Ration", "WeightCv");
            string legacy = json.ToJsonString(SaveJson.Options);
            Assert.DoesNotContain("WeightCv", legacy);
            Assert.DoesNotContain("\"Ration\"", legacy);

            var resumed = SaveStateComparer.Resume(legacy, events: false);
            var pen = resumed.State.SeaSites.Single(s => s.Id == "legacy").Pens.Single();
            Assert.Equal(1.0, pen.Ration.FractionOfAppetite);
            var c0 = pen.Cohorts.Single();
            Assert.Equal(0.0, c0.WeightCv); // unknown until its first growth day
            double weight = c0.MeanWeightKg;
            resumed.Advance(1);
            Assert.InRange(c0.WeightCv, resumed.Growth.Law.InitialWeightCv(weight) - 0.01,
                resumed.Growth.Law.InitialWeightCv(weight) + 0.01);
        }

        [Fact]
        public void TheProjectionTwin_GrowsOnTheTicksLaw()
        {
            var engine = RichEngine(33);
            var site = OpenWaterSite(engine, "twin");
            Assert.True(engine.StockSmolts(site.Id, 10_000, "standard").Ok);
            engine.Advance(100);
            var pen = site.Pens.Single();
            var law = engine.Growth.Law;
            var cohort = pen.Cohorts.Single();
            var forecast = ForecastTwin.Project(engine.State, engine.SeaLice, 10.0, engine.Analytics, 0.0, law);

            double w = cohort.MeanWeightKg, cv = cohort.WeightCv;
            for (int d = 0; d < ForecastTwin.HorizonDays; d++)
            {
                var day = law.GrowOneDay(w, cv, cohort.Stage,
                    new GrowthConditions(10.0, GrowthMultiplier: cohort.Genetics!.GetGrowthMultiplier()), 0.0);
                w = day.WeightKg;
                cv = day.WeightCv;
                Assert.Equal(cohort.Count * w, forecast.BiomassKg[d], 6);
            }
        }

        [Fact]
        public void AScenarioFarm_StartsWithTheGenerationItsReleaseWindowPutToSea()
        {
            var engine = RichEngine(4);
            Assert.True(engine.StartScenario("forste_sesong").Ok);
            var site = engine.State.SeaSites.Single();
            var cohort = site.Pens.Single().Cohorts.Single();
            var def = engine.Scenario.GetScenario("forste_sesong")!.StartingSites.Single();
            var strain = SmoltStrainRegistry.Get(def.SmoltQuality);
            var expected = SimulationEngine.GrowOnClimatology(engine.Growth.Law, 3, 0, def.DaysAtSea,
                strain.ReleaseWeightKg, strain.BuildGenome().GetGrowthMultiplier());

            Assert.Equal(110, def.DaysAtSea); // S0 smolts released mid-September (PO3)
            Assert.Equal(expected.WeightKg, cohort.MeanWeightKg, 12);
            Assert.Equal(expected.WeightCv, cohort.WeightCv, 12);
            Assert.InRange(cohort.MeanWeightKg, 0.3, 0.7);
            Assert.True(cohort.WeightCv > engine.Growth.Law.Calibration.SmoltWeightCv);
            Assert.Equal(LifeStage.Seawater, cohort.Stage);
            // Farther north the sea warms later: S1 smolts released mid-June.
            Assert.Equal(200, engine.Scenario.GetScenario("krise_manager")!.StartingSites.Single().DaysAtSea);
            // The same day count grows less in the colder north.
            Assert.True(SimulationEngine.GrowOnClimatology(engine.Growth.Law, 13, 0, 110, 0.1).WeightKg
                < SimulationEngine.GrowOnClimatology(engine.Growth.Law, 3, 0, 110, 0.1).WeightKg);
        }

        [Fact]
        public void ScenariosJson_CarriesTheRostersDaysAtSea_AndStockingCounts()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LakskongenSim.sln")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            var roster = new ScenarioSystem(new GameState());
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "godot", "data", "scenarios.json")));
            int sites = 0;
            foreach (var entry in json.RootElement.EnumerateArray())
            {
                var builtIn = roster.GetScenario(entry.GetProperty("id").GetString()!)!;
                var jsonSites = entry.GetProperty("starting_sites").EnumerateArray().ToList();
                for (int i = 0; i < jsonSites.Count; i++)
                {
                    var s = builtIn.StartingSites[i];
                    Assert.Equal(s.DaysAtSea, jsonSites[i].GetProperty("days_at_sea").GetInt32());
                    Assert.Equal(s.SmoltCount, jsonSites[i].GetProperty("smolt_count").GetInt32());
                    Assert.Equal(s.ProductionArea >= 8 ? 200 : 110, s.DaysAtSea);
                    sites++;
                }
            }
            Assert.True(sites >= 16, $"{sites} sites");
        }

        [Fact]
        public void TheAppetiteTheUiShows_IsHealthyInWinter_AndFallsInAHeatwave()
        {
            var engine = RichEngine(34);
            var site = OpenWaterSite(engine, "appetite");
            Assert.True(engine.StockSmolts(site.Id, 10_000, "standard").Ok);
            engine.Advance(30); // January: ~6 °C in PO4
            Assert.Equal(1.0, engine.Feed.LastAppetite01, 9);
            engine.Weather.ApplyTemperatureAnomaly(+12.0); // a heatwave: ~18 °C
            engine.Advance(1);
            Assert.True(engine.Feed.LastAppetite01 < 0.8, $"appetite {engine.Feed.LastAppetite01:F2} at {engine.Weather.CurrentTemperatureC:F1} °C");
        }
    }
}
