using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Environment;

namespace LakskongenSim.Tests.Environment
{
    /// <summary>
    /// GENETICS depth (P12): genomic/marker-assisted selection gain, inbreeding
    /// depression, and the GE regulatory/reputation/recall model.
    /// See GenomicSelection.cs and GeneticEngineering.cs.
    /// </summary>
    public class GeneticsDepthTests
    {
        // ─── Selection gain ───

        [Fact]
        public void Gain_MonotonicInHeritability()
        {
            var pool = Pool(-1.0, 0.0, 1.0, 2.0, 3.0);

            double growth = GenomicSelection.ExpectedGainForTrait(
                GeneticTrait.GrowthRate, pool, 1.755, SelectionMethod.Phenotypic);
            double stress = GenomicSelection.ExpectedGainForTrait(
                GeneticTrait.StressTolerance, pool, 1.755, SelectionMethod.Phenotypic);

            Assert.True(Genetics.GetHeritability(GeneticTrait.GrowthRate)
                > Genetics.GetHeritability(GeneticTrait.StressTolerance));
            Assert.True(growth > stress,
                $"Higher-h² trait ({growth:F4}) should out-gain low-h² ({stress:F4}).");
        }

        [Fact]
        public void Gain_MonotonicInSelectionIntensity()
        {
            var pool = Pool(-1.0, 0.0, 1.0, 2.0, 3.0);

            double low = GenomicSelection.ExpectedGainForTrait(
                GeneticTrait.GrowthRate, pool, 1.0, SelectionMethod.Genomic);
            double high = GenomicSelection.ExpectedGainForTrait(
                GeneticTrait.GrowthRate, pool, 2.5, SelectionMethod.Genomic);

            Assert.True(high > low);
            // Linear in i: doubling i doubles the gain.
            Assert.Equal(2.5 * low, high, 9);
        }

        [Fact]
        public void Gain_ZeroWhenNoVarianceOrTooSmallPool()
        {
            var flat = Pool(1.0, 1.0, 1.0, 1.0);
            Assert.Equal(0.0, GenomicSelection.ExpectedGainForTrait(
                GeneticTrait.GrowthRate, flat, 1.755, SelectionMethod.Genomic), 9);

            var single = Pool(1.0);
            Assert.Equal(0.0, GenomicSelection.ExpectedGainForTrait(
                GeneticTrait.GrowthRate, single, 1.755, SelectionMethod.Genomic), 9);
        }

        [Fact]
        public void ExpectedGain_CoversAllEightTraits()
        {
            var gains = GenomicSelection.ExpectedGain(Pool(-1.0, 0.0, 1.0, 2.0));
            Assert.Equal(8, gains.Count);
            Assert.Equal(Enum.GetValues(typeof(GeneticTrait)).Length, gains.Count);
        }

        // ─── Marker / genomic accuracy ───

        [Fact]
        public void Accuracy_GenomicBeatsMarkerBeatsPhenotype()
        {
            double phenotype = GenomicSelection.SelectionAccuracy(
                GeneticTrait.GrowthRate, SelectionMethod.Phenotypic, 0.75, 0.75);
            double marker = GenomicSelection.SelectionAccuracy(
                GeneticTrait.GrowthRate, SelectionMethod.MarkerAssisted, 0.75, 0.75);
            double genomic = GenomicSelection.SelectionAccuracy(
                GeneticTrait.GrowthRate, SelectionMethod.Genomic, 0.75, 0.75);

            Assert.True(phenotype < marker);
            Assert.True(marker < genomic);
            Assert.True(genomic <= 0.99);
            Assert.Equal(Math.Sqrt(Genetics.GetHeritability(GeneticTrait.GrowthRate)), phenotype, 9);
        }

        [Fact]
        public void Accuracy_BenefitsMostForLowHeritabilityTraits()
        {
            // The genomic gap over phenotype is larger when h² (and √h²) is small.
            double gapGrowth = GenomicSelection.SelectionAccuracy(
                GeneticTrait.GrowthRate, SelectionMethod.Genomic, 1.0, 1.0)
                - GenomicSelection.SelectionAccuracy(
                    GeneticTrait.GrowthRate, SelectionMethod.Phenotypic, 1.0, 1.0);

            double gapStress = GenomicSelection.SelectionAccuracy(
                GeneticTrait.StressTolerance, SelectionMethod.Genomic, 1.0, 1.0)
                - GenomicSelection.SelectionAccuracy(
                    GeneticTrait.StressTolerance, SelectionMethod.Phenotypic, 1.0, 1.0);

            Assert.True(gapStress > gapGrowth,
                $"Low-h² trait gain ({gapStress:F4}) should exceed high-h² ({gapGrowth:F4}).");
        }

        [Fact]
        public void GenomicGain_ExceedsPhenotypicGain_ForSamePool()
        {
            var pool = Pool(-1.0, 0.0, 1.0, 2.0, 3.0);

            double pheno = GenomicSelection.ExpectedGainForTrait(
                GeneticTrait.DiseaseResistance, pool, 1.755, SelectionMethod.Phenotypic, 0.75, 0.75);
            double genomic = GenomicSelection.ExpectedGainForTrait(
                GeneticTrait.DiseaseResistance, pool, 1.755, SelectionMethod.Genomic, 0.75, 0.75);

            Assert.True(genomic > pheno);
        }

        [Fact]
        public void IntensityFromSelectedFraction_MatchesKnownValues_And_IsMonotonic()
        {
            Assert.Equal(1.755, GenomicSelection.IntensityFromSelectedFraction(0.10), 3);
            Assert.Equal(0.798, GenomicSelection.IntensityFromSelectedFraction(0.50), 3);
            Assert.Equal(2.665, GenomicSelection.IntensityFromSelectedFraction(0.01), 3);

            Assert.True(GenomicSelection.IntensityFromSelectedFraction(0.01)
                > GenomicSelection.IntensityFromSelectedFraction(0.10));
            Assert.True(GenomicSelection.IntensityFromSelectedFraction(0.10)
                > GenomicSelection.IntensityFromSelectedFraction(0.50));
        }

        [Fact]
        public void BreedersToSelect_ClampsToPoolAndMinimumTwo()
        {
            Assert.Equal(10, GenomicSelection.BreedersToSelect(100, 0.10));
            Assert.Equal(2, GenomicSelection.BreedersToSelect(5, 0.05));
            Assert.Equal(5, GenomicSelection.BreedersToSelect(5, 1.0));
            Assert.Equal(0, GenomicSelection.BreedersToSelect(0, 0.10));
        }

        // ─── Inbreeding ───

        [Fact]
        public void Inbreeding_FullSibs_ParentOffspring_HalfSibs_Unrelated()
        {
            var a = new PedigreeNode("A");
            var b = new PedigreeNode("B");
            var c = new PedigreeNode("C");

            var x = new PedigreeNode("X", a, b);
            var y = new PedigreeNode("Y", a, b);

            var p = new PedigreeNode("P", c, a);
            var q = new PedigreeNode("Q", c, b);

            Assert.Equal(0.25, GenomicSelection.MatedPairInbreeding(x, y), 6);   // full sibs
            Assert.Equal(0.25, GenomicSelection.MatedPairInbreeding(a, x), 6);   // parent-offspring
            Assert.Equal(0.125, GenomicSelection.MatedPairInbreeding(p, q), 6);  // half sibs
            Assert.Equal(0.0, GenomicSelection.MatedPairInbreeding(a, b), 6);    // unrelated founders
            Assert.Equal(0.0, GenomicSelection.MatedPairInbreeding(null, a), 6); // unknown parent
        }

        [Fact]
        public void CoefficientOfInbreeding_FromPedigree()
        {
            var a = new PedigreeNode("A");
            var b = new PedigreeNode("B");
            var x = new PedigreeNode("X", a, b);

            // Offspring of two full sibs (X and its sibling) is inbred.
            var sibling = new PedigreeNode("Sibling", a, b);
            var inbred = new PedigreeNode("Inbred", x, sibling);

            Assert.Equal(0.0, GenomicSelection.CoefficientOfInbreeding(x), 6);
            Assert.Equal(0.25, GenomicSelection.CoefficientOfInbreeding(inbred), 6);
        }

        [Fact]
        public void Inbreeding_FromRelatedness_Proxy()
        {
            Assert.Equal(0.25, GenomicSelection.InbreedingCoefficientFromRelatedness(0.50), 9);
            Assert.Equal(0.125, GenomicSelection.InbreedingCoefficientFromRelatedness(0.25), 9);
            Assert.Equal(0.0, GenomicSelection.InbreedingCoefficientFromRelatedness(0.0), 9);

            // Cumulative inbreeding rises with generations and falls with N_e.
            Assert.True(GenomicSelection.InbreedingCoefficientProxy(50, 5)
                > GenomicSelection.InbreedingCoefficientProxy(50, 1));
            Assert.True(GenomicSelection.InbreedingCoefficientProxy(10, 5)
                > GenomicSelection.InbreedingCoefficientProxy(100, 5));
            Assert.Equal(0.0, GenomicSelection.InbreedingCoefficientProxy(50, 0), 9);
        }

        [Fact]
        public void InbreedingDepression_Monotonic_TraitSensitive_AndBounded()
        {
            // More inbreeding → larger penalty; zero at F=0.
            Assert.Equal(0.0, GenomicSelection.InbreedingDepressionPenalty(
                GeneticTrait.DiseaseResistance, 0.0), 9);
            Assert.True(GenomicSelection.InbreedingDepressionPenalty(
                GeneticTrait.DiseaseResistance, 0.25)
                > GenomicSelection.InbreedingDepressionPenalty(
                    GeneticTrait.DiseaseResistance, 0.10));

            // Fitness traits erode faster than production traits.
            Assert.True(GenomicSelection.InbreedingDepressionPenalty(
                GeneticTrait.DiseaseResistance, 0.25)
                > GenomicSelection.InbreedingDepressionPenalty(
                    GeneticTrait.FilletQuality, 0.25));

            // Bounded survival factor in (0,1].
            double penalty = GenomicSelection.InbreedingDepressionPenalty(
                GeneticTrait.StressTolerance, 1.0);
            Assert.InRange(penalty, 0.0, GenomicSelection.MaxDepressionFraction);

            double depressed = GenomicSelection.ApplyInbreedingDepression(
                GeneticTrait.GrowthRate, 10.0, 0.25);
            Assert.True(depressed < 10.0 && depressed > 0.0);
        }

        // ─── Genetic engineering ───

        [Fact]
        public void Ge_RegionStatus_ReflectsAquAdvantage()
        {
            var aqua = GeTraitRegistry.AquAdvantageGrowth;

            Assert.Equal(RegulatoryStatus.Approved, aqua.StatusIn(GeRegion.UnitedStates));
            Assert.Equal(RegulatoryStatus.Approved, aqua.StatusIn(GeRegion.Canada));
            Assert.Equal(RegulatoryStatus.Banned, aqua.StatusIn(GeRegion.Norway));
            Assert.Equal(RegulatoryStatus.Banned, aqua.StatusIn(GeRegion.EuropeanUnion));

            Assert.True(aqua.IsApprovedIn(GeRegion.UnitedStates));
            Assert.False(aqua.IsApprovedIn(GeRegion.Norway));
        }

        [Fact]
        public void Ge_Assessment_BlockedWhenBanned_NoReputationCost()
        {
            var aqua = GeTraitRegistry.AquAdvantageGrowth;
            var result = GeneticEngineering.AssessDeployment(aqua, GeRegion.Norway, 0.9);

            Assert.False(result.Allowed);
            Assert.Equal(RegulatoryStatus.Banned, result.Status);
            Assert.Equal(0.0, result.ReputationCost, 9);
            Assert.Equal(0.0, result.ExpectedRecallLoss, 9);
        }

        [Fact]
        public void Ge_Assessment_Approved_ExposesReputationAndRecallRisk()
        {
            var aqua = GeTraitRegistry.AquAdvantageGrowth;
            var oneYear = GeneticEngineering.AssessDeployment(aqua, GeRegion.UnitedStates, 1.0);
            var twoYears = GeneticEngineering.AssessDeployment(
                aqua, GeRegion.UnitedStates, 1.0, horizonYears: 2);

            Assert.True(oneYear.Allowed);
            Assert.Equal(aqua.ReputationCost, oneYear.ReputationCost, 9);
            Assert.Equal(aqua.AnnualRecallRisk * 1 * aqua.RecallPenalty, oneYear.ExpectedRecallLoss, 6);
            Assert.True(twoYears.ExpectedRecallLoss > oneYear.ExpectedRecallLoss);
        }

        [Fact]
        public void Ge_Assessment_RequiresSterilityContainment()
        {
            var aqua = GeTraitRegistry.AquAdvantageGrowth;
            Assert.True(aqua.RequiresSterilityContainment);

            var without = GeneticEngineering.AssessDeployment(
                aqua, GeRegion.UnitedStates, 1.0, hasSterilityContainment: false);
            var with = GeneticEngineering.AssessDeployment(
                aqua, GeRegion.UnitedStates, 1.0, hasSterilityContainment: true);

            Assert.False(without.Allowed);
            Assert.False(without.SterilityContainmentSatisfied);
            Assert.True(with.Allowed);
        }

        [Fact]
        public void Ge_RecallOccurred_ThresholdBehaviour()
        {
            var aqua = GeTraitRegistry.AquAdvantageGrowth; // 5% annual recall risk

            Assert.True(aqua.RecallOccurred(0.0));
            Assert.True(aqua.RecallOccurred(aqua.AnnualRecallRisk - 0.001));
            Assert.False(aqua.RecallOccurred(aqua.AnnualRecallRisk));
            Assert.False(aqua.RecallOccurred(0.99));
        }

        [Fact]
        public void Ge_ReputationImpact_ClampedAndMonotonic()
        {
            Assert.Equal(0.70, GeneticEngineering.ApplyReputationImpact(0.80, 0.10), 9);
            Assert.Equal(0.0, GeneticEngineering.ApplyReputationImpact(0.20, 0.50), 9);
            Assert.Equal(1.0, GeneticEngineering.ApplyReputationImpact(1.0, 0.0), 9);
        }

        [Fact]
        public void Ge_EffectAvailableOnlyWhenApproved()
        {
            var aqua = GeTraitRegistry.AquAdvantageGrowth;

            Assert.True(GeneticEngineering.EffectiveTraitEffect(
                aqua, 1.0, GeRegion.UnitedStates) > 0.0);
            Assert.Equal(0.0, GeneticEngineering.EffectiveTraitEffect(
                aqua, 1.0, GeRegion.Norway), 9);
        }

        [Fact]
        public void Ge_Registry_LookupAndCount()
        {
            Assert.Equal(4, GeTraitRegistry.All.Count);
            Assert.NotNull(GeTraitRegistry.Get("ge_sterility"));
            Assert.Null(GeTraitRegistry.Get("does-not-exist"));
            Assert.False(GeTraitRegistry.InducedSterility.RequiresSterilityContainment);
        }

        // ─── Engine wiring (Wave 6) ───

        [Fact]
        public void Engine_InbreedingDepression_ReducesGrowth_ZeroIsNeutral()
        {
            double baseline = RunOneDayGrowth(0.0);
            double zeroAgain = RunOneDayGrowth(0.0);
            double inbred = RunOneDayGrowth(0.25);

            Assert.Equal(baseline, zeroAgain, 9);       // deterministic + zero inbreeding is a no-op
            Assert.True(inbred < baseline,
                $"Inbred cohort ({inbred:F6} kg) should grow less than baseline ({baseline:F6} kg).");
        }

        private static double RunOneDayGrowth(double inbreedingCoefficient)
        {
            var engine = new SimulationEngine();
            engine.Initialize(enableEvents: false, seed: 20260107);

            var zone = new Zone("InbreedingZone");
            zone.SetTemperature(11.0);
            engine.State.Zones.Add(zone);

            engine.Hatchery.ReleaseSmolt(1000);
            var population = engine.State.Populations[0];
            population.ZoneId = zone.Id;

            foreach (var cohort in population.Cohorts)
            {
                cohort.Genetics ??= engine.Genetics.GenerateRandomGenome();
                cohort.Genetics.InbreedingCoefficient = inbreedingCoefficient;
            }

            engine.Advance(1);
            return population.Cohorts.Sum(c => c.MeanWeightKg);
        }

        // ─── Helpers ───

        private static List<Genome> Pool(params double[] breedingValues)
        {
            var list = new List<Genome>();
            foreach (double bv in breedingValues)
            {
                var genome = new Genome();
                foreach (GeneticTrait trait in Enum.GetValues(typeof(GeneticTrait)))
                    genome.TraitValues[trait] = bv;
                list.Add(genome);
            }
            return list;
        }
    }
}
