using System;
using System.Linq;
using System.Text.Json;
using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Data;
using Xunit;

namespace LakskongenSim.Tests.Biology
{
    /// <summary>
    /// S2-05 (roadmap D4): the mortality ledger books every death of a cohort
    /// to one cause, exactly. Unit level: the integer split, the population
    /// verbs that decrement and book in one step, the hazard split of the
    /// daily mortality, every cause mapped to one NVI category, save/load.
    /// </summary>
    public class MortalityLedgerTests
    {
        private static (MortalityModel Model, FishPopulation Pop, FishPopulation.Cohort Cohort) Wired(int count = 100_000,
            LifeStage stage = LifeStage.Seawater, double weightKg = 2.0)
        {
            var model = new MortalityModel();
            var pop = new FishPopulation(new GrowthModel(seed: 1), model) { Id = "pen-1", SiteId = "site-1" };
            var cohort = new FishPopulation.Cohort(400, count, weightKg, stage);
            pop.AddCohort(cohort);
            return (model, pop, cohort);
        }

        [Fact]
        public void Split_GivesWholeFish_ThatSumExactly_AndFollowTheWeights()
        {
            var rng = new Random(5);
            for (int trial = 0; trial < 5_000; trial++)
            {
                int deaths = rng.Next(0, 50_000);
                int n = rng.Next(1, 6);
                var weights = Enumerable.Range(0, n).Select(_ => rng.NextDouble() < 0.2 ? 0.0 : rng.NextDouble()).ToArray();
                if (weights.All(w => w == 0.0)) weights[0] = 1.0;
                var split = MortalityLedger.Split(deaths, weights);

                Assert.Equal(deaths, split.Sum());
                double total = weights.Sum();
                for (int i = 0; i < n; i++)
                {
                    Assert.True(split[i] >= 0);
                    // Largest remainder: each share is the floor or the ceiling of its exact share.
                    double exact = deaths * weights[i] / total;
                    Assert.InRange(split[i], Math.Floor(exact), Math.Ceiling(exact));
                    if (weights[i] == 0.0) Assert.Equal(0, split[i]);
                }
            }
        }

        [Fact]
        public void RemoveDead_DecrementsTheCohort_AndBooksTheCause_ClampedToTheFishItHolds()
        {
            var (model, pop, cohort) = Wired(count: 1_000, weightKg: 3.0);
            model.Ledger.BeginDay(12, 80m);

            Assert.Equal(40, pop.RemoveDead(cohort, 40, MortalityCause.Jellyfish));
            Assert.Equal(960, pop.RemoveDead(cohort, 5_000, MortalityCause.Storm));
            Assert.Equal(0, pop.RemoveDead(cohort, 3, MortalityCause.Storm));
            Assert.Equal(0, cohort.Count);

            var all = model.Ledger.Cumulative();
            Assert.Equal(40, all.Deaths(MortalityCause.Jellyfish).Fish);
            Assert.Equal(960, all.Deaths(MortalityCause.Storm).Fish);
            Assert.Equal(1_000, all.Deaths(MortalityCategory.Environment).Fish);
            Assert.Equal(1_000, all.Total.Fish);
            Assert.Equal(3_000.0, all.Total.Kg, 6);
            Assert.Equal(240_000m, all.Total.ValueNok);
            Assert.Equal(1_000, model.Ledger.ForSite("site-1").Total.Fish);
            Assert.Equal(1_000, model.Ledger.ForDays(12, 12).Total.Fish);
            Assert.Equal(1_000, model.Ledger.TodayByCohort[cohort].TotalDeaths);
        }

        [Fact]
        public void RemoveAlive_IsNotADeath()
        {
            var (model, pop, cohort) = Wired(count: 500);
            pop.RemoveAlive(cohort, 120, FishRemoval.Escape);
            pop.RemoveAlive(cohort, 380, FishRemoval.Harvest);

            var all = model.Ledger.Cumulative();
            Assert.Equal(0, all.Total.Fish);
            Assert.Equal(120, all.Removed(FishRemoval.Escape).Fish);
            Assert.Equal(380, all.Removed(FishRemoval.Harvest).Fish);
            Assert.Equal(500, model.Ledger.TodayByCohort[cohort].TotalRemoved);
            Assert.Equal(0, cohort.Count);
        }

        [Fact]
        public void UnwiredPopulation_StillLosesTheFish_AndBooksNothing()
        {
            var pop = new FishPopulation();
            var cohort = new FishPopulation.Cohort(400, 100, 2.0, LifeStage.Seawater);
            pop.AddCohort(cohort);
            Assert.Equal(10, pop.RemoveDead(cohort, 10, MortalityCause.Temperature));
            Assert.Equal(90, cohort.Count);
        }

        [Fact]
        public void DailyMortality_SplitsBetweenTheAnchorAndDisease_InProportionToTheirHazards()
        {
            var (model, pop, cohort) = Wired(count: 1_000_000);
            double anchor = model.SeaDailyMortalityRate * 1.2; // seawater stage multiplier
            double disease = anchor * 3.0;
            model.SetDiseaseMortalityRate(pop.Id, disease);
            int expectedSurvivors = model.ApplyMortality(1_000_000, 1, LifeStage.Seawater, pop.Id);

            pop.AdvanceDays(1, skipGrowth: true);

            // Same survivors as the count-only model (the ledger changes nothing)...
            Assert.Equal(expectedSurvivors, cohort.Count);
            var all = model.Ledger.Cumulative();
            int deaths = 1_000_000 - expectedSurvivors;
            // ...and every death is booked: three disease deaths per anchor death.
            Assert.Equal(deaths, all.Total.Fish);
            Assert.InRange(all.Deaths(MortalityCause.Disease).Fish, deaths * 0.75 - 1, deaths * 0.75 + 1);
            Assert.Equal(deaths, all.Deaths(MortalityCause.Disease).Fish + all.Deaths(MortalityCause.BaselineAnchor).Fish);
            Assert.Equal(all.Deaths(MortalityCause.Disease).Fish, all.Deaths(MortalityCategory.Infectious).Fish);
            Assert.Equal(all.Deaths(MortalityCause.BaselineAnchor).Fish, all.Deaths(MortalityCategory.Unexplained).Fish);
        }

        [Fact]
        public void ResearchReduction_ShiftsTheSplit_AwayFromTheReducedHazard()
        {
            var (model, pop, cohort) = Wired(count: 1_000_000);
            double anchor = model.SeaDailyMortalityRate * 1.2;
            model.SetDiseaseMortalityRate(pop.Id, anchor); // equal hazards before research
            pop.AdvanceDays(1, skipGrowth: true, mortalityReduction: 0.5, diseaseResistanceBonus: 0.0);

            var all = model.Ledger.Cumulative();
            // After research the anchor hazard is half the disease hazard.
            double anchorShare = (double)all.Deaths(MortalityCause.BaselineAnchor).Fish / all.Total.Fish;
            Assert.InRange(anchorShare, 1.0 / 3.0 - 0.01, 1.0 / 3.0 + 0.01);
            Assert.Equal(1_000_000 - cohort.Count, all.Total.Fish);
        }

        [Fact]
        public void EveryCause_BelongsToExactlyOneCategory_WithAnNviCodeExceptPredators()
        {
            foreach (var cause in MortalityCauses.All)
                Assert.Contains(MortalityCauses.CategoryOf(cause), MortalityCauses.AllCategories);
            Assert.Equal(new[] { "A", "B", "C", "D", "", "E", "F" },
                MortalityCauses.AllCategories.Select(MortalityCauses.NviCode).ToArray());
            // Ids are unique: the save file and the bridge key on them.
            Assert.Equal(MortalityCauses.All.Length, MortalityCauses.All.Select(MortalityCauses.Id).Distinct().Count());
            Assert.Equal(MortalityCauses.AllCategories.Length,
                MortalityCauses.AllCategories.Select(MortalityCauses.Id).Distinct().Count());
            Assert.Equal(MortalityCategory.Environment, MortalityCauses.CategoryOf(MortalityCauses.ForBloomKind("jellyfish")));
            Assert.Equal(MortalityCause.HarmfulAlgae, MortalityCauses.ForBloomKind("algae"));
            Assert.Equal(MortalityCause.LowOxygen, MortalityCauses.ForBloomKind("hypoxia"));
        }

        [Fact]
        public void Breakdown_CategoriesSumToTheTotal_AndTheTableNamesEveryCategory()
        {
            var (model, pop, cohort) = Wired(count: 10_000);
            int i = 0;
            foreach (var cause in MortalityCauses.All)
                pop.RemoveDead(cohort, 10 + i++, cause);
            pop.RemoveAlive(cohort, 7, FishRemoval.Escape);

            var all = model.Ledger.Cumulative();
            Assert.Equal(all.Total.Fish, MortalityCauses.AllCategories.Sum(c => all.Deaths(c).Fish));
            Assert.Equal(all.Total.Fish, MortalityCauses.All.Sum(c => all.Deaths(c).Fish));
            string table = all.FormatTable("test");
            foreach (var category in MortalityCauses.AllCategories)
                Assert.Contains(MortalityCauses.Id(category), table);
            Assert.Contains("left alive: escape", table);
        }

        [Fact]
        public void Ledger_RoundTripsThroughTheModelSnapshot_AndALegacySaveStartsEmpty()
        {
            var (model, pop, cohort) = Wired(count: 50_000);
            model.Ledger.BeginDay(3, 75m);
            pop.RemoveDead(cohort, 100, MortalityCause.Delousing);
            model.Ledger.BeginDay(4, 77m);
            pop.RemoveDead(cohort, 50, MortalityCause.Disease);
            pop.RemoveAlive(cohort, 25, FishRemoval.Escape);

            string json = JsonSerializer.Serialize(model.GetSaveSnapshot(), SaveJson.Options);
            var restored = new MortalityModel();
            restored.RestoreFromSave(JsonSerializer.Deserialize<MortalityModelSnapshot>(json, SaveJson.Options));

            var a = model.Ledger;
            var b = restored.Ledger;
            Assert.Equal(a.Day, b.Day);
            Assert.Equal(a.PriceNokPerKg, b.PriceNokPerKg);
            foreach (var cause in MortalityCauses.All)
            {
                Assert.Equal(a.Cumulative().Deaths(cause).Fish, b.Cumulative().Deaths(cause).Fish);
                Assert.Equal(a.Cumulative().Deaths(cause).ValueNok, b.Cumulative().Deaths(cause).ValueNok);
                Assert.Equal(a.ForSite("site-1").Deaths(cause).Fish, b.ForSite("site-1").Deaths(cause).Fish);
                Assert.Equal(a.ForDays(3, 3).Deaths(cause).Fish, b.ForDays(3, 3).Deaths(cause).Fish);
            }
            Assert.Equal(25, b.Cumulative().Removed(FishRemoval.Escape).Fish);
            Assert.Equal(json, JsonSerializer.Serialize(restored.GetSaveSnapshot(), SaveJson.Options));

            // A save written before S2-05 has no ledger: it loads empty.
            restored.RestoreFromSave(new MortalityModelSnapshot());
            Assert.Equal(0, restored.Ledger.Cumulative().Total.Fish);
            restored.RestoreFromSave(null);
            Assert.Equal(0, restored.Ledger.Cumulative().Total.Fish);
        }

        [Fact]
        public void History_KeepsTheLastHistoryDays_AndForgetsOlderDays()
        {
            var (model, pop, cohort) = Wired(count: 1_000_000);
            for (int day = 1; day <= MortalityLedger.HistoryDays + 50; day++)
            {
                model.Ledger.BeginDay(day, 80m);
                pop.RemoveDead(cohort, 1, MortalityCause.BaselineAnchor);
            }
            Assert.Equal(MortalityLedger.HistoryDays, model.Ledger.HistoryDaysHeld.Count());
            Assert.Equal(51, model.Ledger.HistoryDaysHeld.First());
            Assert.Equal(MortalityLedger.HistoryDays + 50, model.Ledger.Cumulative().Total.Fish);
            Assert.Equal(30, model.Ledger.ForDays(MortalityLedger.HistoryDays + 21, MortalityLedger.HistoryDays + 50).Total.Fish);
        }
    }

    /// <summary>
    /// S2-05: a delousing applies the deaths it draws, books them, and reports
    /// what it applied. Each cohort used to be capped at floor(count x rate),
    /// so a draw above the expectation was cut back while the result (and the
    /// event log) still carried the drawn number.
    /// </summary>
    public class DelousingDeathsTests
    {
        private static FishPopulation Pen(MortalityModel model, params int[] cohortCounts)
        {
            var pop = new FishPopulation(new GrowthModel(seed: 1), model) { Id = "pen-d", SiteId = "site-d" };
            foreach (int n in cohortCounts)
                pop.AddCohort(new FishPopulation.Cohort(400, n, 0.8, LifeStage.Seawater));
            return pop;
        }

        [Fact]
        public void Treatment_KillsWhatItReports_AndTheLedgerBooksIt_MeanPreserving()
        {
            const int draws = 300;
            double sum = 0.0;
            double expected = 0.0;
            int aboveExpectation = 0;
            for (int i = 0; i < draws; i++)
            {
                var model = new MortalityModel();
                var pop = Pen(model, 60_000, 25_000, 15_000);
                var lice = new LakskongenSim.Health.SeaLiceModel();
                lice.SeedExternalInfection(1.0);
                var disease = new LakskongenSim.Health.IntegratedDiseaseSystem(lice);
                var rng = new SimRng(5_000 + i).Stream(SimRngStreams.Delouse, "site-d", "0");
                var legacy = new LakskongenSim.Farm.DelousingSystem().Treat(Pen(new MortalityModel(), 60_000, 25_000, 15_000),
                    DelousingMethod.Hydrolicer, lice, disease, 10.0, 1.0);
                expected = legacy.Deaths;

                var result = new LakskongenSim.Farm.DelousingSystem().Treat(pop, DelousingMethod.Hydrolicer, lice, disease,
                    10.0, 1.0, rng: rng);

                Assert.Equal(100_000 - pop.TotalCount, result.Deaths);
                Assert.Equal(result.Deaths, model.Ledger.Cumulative().Deaths(MortalityCause.Delousing).Fish);
                Assert.Equal(result.Deaths, model.Ledger.Cumulative().Total.Fish);
                sum += result.Deaths;
                if (result.Deaths > expected) aboveExpectation++;
            }
            // A draw above the expectation is applied in full (the cap held
            // every treatment at or below it).
            Assert.InRange(aboveExpectation, draws / 4, 3 * draws / 4);
            // The drawn deaths are mean-preserving against the expected value;
            // the applied deaths now are too (they were biased low by the cap).
            Assert.InRange(sum / draws, expected * 0.99, expected * 1.01);
        }
    }
}
