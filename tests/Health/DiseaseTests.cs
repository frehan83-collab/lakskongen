using System;
using Xunit;
using LakskongenSim.Biology;
using LakskongenSim.Health;
using LakskongenSim.Data;

namespace LakskongenSim.Health.Tests
{
    /// <summary>
    /// Review F906: these tests used to pass with the disease model removed
    /// (prevalence "> 0" held for the 1e-6 seed alone, "deaths" came from
    /// baseline mortality, and "after &lt;= before" held for equal values).
    /// Each now compares against a disease-off control from a prevalence large
    /// enough to give signal, with bounds derived from the model's parameters.
    /// </summary>
    public class DiseaseTests
    {
        private const double StartPrevalence = 0.05;

        private static FishPopulation SeaPopulation(MortalityModel mortality)
        {
            var pop = new FishPopulation(new GrowthModel(), mortality);
            // Seawater stage: fish in sea pens, where disease and lice matter.
            pop.AddCohort(new FishPopulation.Cohort(400, 10000, 1.0, LifeStage.Seawater));
            return pop;
        }

        private static int Survivors(FishPopulation pop)
        {
            var total = 0;
            foreach (var c in pop.Cohorts) total += c.Count;
            return total;
        }

        [Fact]
        public void InfectionProgression_IncreasesPrevalenceAndCausesDeaths()
        {
            var lice = new SeaLiceModel();
            var diseaseMortality = new MortalityModel();
            var ds = new DiseaseSystem(lice)
            {
                Beta = 0.3,
                Gamma = 0.01,
                ExtraMortalityRatePerDay = 0.005,
                InitialPrevalence = StartPrevalence
            };
            // Disease injects its rate through the MortalityModel (T76).
            ds.SetMortalityModel(diseaseMortality);
            var sick = SeaPopulation(diseaseMortality);

            // Control: the same fish with transmission and disease deaths off.
            var controlMortality = new MortalityModel();
            var control = new DiseaseSystem(new SeaLiceModel())
            {
                Beta = 0.0,
                Gamma = 0.01,
                ExtraMortalityRatePerDay = 0.0,
                InitialPrevalence = StartPrevalence
            };
            control.SetMortalityModel(controlMortality);
            var healthy = SeaPopulation(controlMortality);

            ds.Tick(sick, 30, temperatureC: 10.0);
            control.Tick(healthy, 30, temperatureC: 10.0);

            // From 5% the logistic spread (beta 0.3, gamma 0.01) reaches its
            // endemic equilibrium (about 97%) well inside 30 days; with no
            // transmission the control only recovers.
            double p = ds.GetPrevalence(sick);
            Assert.True(p > 10 * StartPrevalence, $"prevalence {p:F4} did not spread");
            Assert.InRange(p, ds.EquilibriumPrevalence() - 0.05, ds.EquilibriumPrevalence() + 1e-9);
            Assert.True(control.GetPrevalence(healthy) < StartPrevalence);

            sick.AdvanceDays(30);
            healthy.AdvanceDays(30);

            // Disease adds ExtraMortalityRatePerDay x prevalence (~0.48%/day)
            // on top of the shared baseline: about 0.866 survival over 30 days.
            int withDisease = Survivors(sick);
            int withoutDisease = Survivors(healthy);
            Assert.True(withoutDisease > 0);
            double ratio = (double)withDisease / withoutDisease;
            Assert.InRange(ratio, 0.80, 0.92);
        }

        [Fact]
        public void TreatmentReducesPrevalenceAndLice()
        {
            var lice = new SeaLiceModel();
            lice.ApplyTreatment("medicinal", 0.5);
            lice.Reset(2.0); // lice above the action threshold

            var ds = new DiseaseSystem(lice)
            {
                Beta = 0.3,
                Gamma = 0.01,
                ExtraMortalityRatePerDay = 0.005,
                InitialPrevalence = StartPrevalence
            };

            var pop = SeaPopulation(new MortalityModel());

            ds.Tick(pop, 14, temperatureC: 8.0);
            var beforeP = ds.GetPrevalence(pop);
            Assert.True(beforeP > 1e-3, $"prevalence {beforeP:F4} too small to measure a treatment");

            ds.ApplyTreatment(pop, "medicinal", reductionFraction: 0.8, directTreatmentEfficacy: 0.6, temperatureC: 8.0);
            var afterP = ds.GetPrevalence(pop);

            // At 8 °C the efficacy modifier is 1.02; half the benefit comes via
            // lice (0.8) and half directly (0.6): a 71.4% cut in prevalence.
            double tempModifier = 1.0 + ds.TreatmentTempSensitivityPerDegC * (8.0 - ds.TreatmentRefTempC);
            double expectedCut = (0.8 + 0.6) * tempModifier * 0.5;
            Assert.Equal(beforeP * (1.0 - expectedCut), afterP, 9);
            Assert.True(afterP < beforeP * 0.35);

            // The lice model takes the same treatment (an unspecified agent kills
            // half at full dose): at least a quarter of the lice are gone.
            Assert.True(lice.AverageLicePerFish < 2.0 * 0.75, $"lice {lice.AverageLicePerFish:F2}/fish after treatment");
        }
    }
}
