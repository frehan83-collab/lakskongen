using System;
using Xunit;
using LakskongenSim.Biology;
using LakskongenSim.Health;
using LakskongenSim.Data;

namespace LakskongenSim.Health.Tests
{
    public class DiseaseTests
    {
        [Fact]
        public void InfectionProgression_IncreasesPrevalenceAndCausesDeaths()
        {
            var lice = new SeaLiceModel();
            var ds = new DiseaseSystem(lice)
            {
                Beta = 0.3,
                Gamma = 0.01,
                ExtraMortalityRatePerDay = 0.005
            };

            var pop = new FishPopulation(new GrowthModel(), new MortalityModel());
            // Use Seawater stage - represents fish in sea pens where disease/lice are most relevant
            pop.AddCohort(new FishPopulation.Cohort(400, 10000, 1.0, LifeStage.Seawater));

            ds.Tick(pop, 30, temperatureC: 10.0);
            var p = ds.GetPrevalence(pop);
            Assert.True(p > 0.0);

            // After 30 days we expect some extra mortality (not all fish gone)
            var total = 0;
            foreach (var c in pop.Cohorts) total += c.Count;
            Assert.InRange(total, 0, 10000);
            Assert.True(total < 10000);
        }

        [Fact]
        public void TreatmentReducesPrevalenceAndLice()
        {
            var lice = new SeaLiceModel();
            lice.ApplyTreatment(0.5); // start with some reduction so average non-zero
            typeof(SeaLiceModel).GetProperty("AverageLicePerFish").SetValue(lice, 2.0); // seed lice

            var ds = new DiseaseSystem(lice)
            {
                Beta = 0.3,
                Gamma = 0.01,
                ExtraMortalityRatePerDay = 0.005
            };

            var pop = new FishPopulation(new GrowthModel(), new MortalityModel());
            // Use Seawater stage - represents fish in sea pens where treatment is applied
            pop.AddCohort(new FishPopulation.Cohort(400, 10000, 1.0, LifeStage.Seawater));

            ds.Tick(pop, 14, temperatureC: 8.0);
            var beforeP = ds.GetPrevalence(pop);

            ds.ApplyTreatment(pop, reductionFraction: 0.8, directTreatmentEfficacy: 0.6, temperatureC: 8.0);
            var afterP = ds.GetPrevalence(pop);
            Assert.True(afterP <= beforeP);
            Assert.True(lice.AverageLicePerFish < 2.0);
        }
    }
}
