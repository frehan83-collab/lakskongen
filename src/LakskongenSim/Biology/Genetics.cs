using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Data;

namespace LakskongenSim.Biology
{
    /// <summary>
    /// Quantitative genetics system for salmon breeding programs.
    /// Models 8 heritable traits with realistic heritabilities (h²), genetic correlations,
    /// and breeding value estimation (BLUP-inspired simplified approach).
    /// Norwegian breeding programs: AquaGen, SalmoBreed, Mowi Genetics.
    /// </summary>
    public class Genetics
    {
        // ─── Heritability estimates (h²) for Atlantic salmon ───
        // Based on published Norwegian breeding program data
        private static readonly Dictionary<GeneticTrait, double> Heritabilities = new()
        {
            [GeneticTrait.GrowthRate] = 0.30,         // Moderate-high heritability
            [GeneticTrait.DiseaseResistance] = 0.15,  // Low-moderate (ISA/IPN resistance)
            [GeneticTrait.SeaLiceResistance] = 0.20,  // Moderate
            [GeneticTrait.FilletQuality] = 0.25,      // Moderate
            [GeneticTrait.FeedConversion] = 0.20,     // Moderate
            [GeneticTrait.StressTolerance] = 0.10,    // Low
            [GeneticTrait.LateMaturation] = 0.35,     // High
            [GeneticTrait.ColdTolerance] = 0.15       // Low-moderate
        };

        // ─── Genetic correlations (simplified: positive = beneficial linkage) ───
        // Growth rate and feed conversion are negatively correlated with disease resistance
        private static readonly Dictionary<(GeneticTrait, GeneticTrait), double> GeneticCorrelations = new()
        {
            [(GeneticTrait.GrowthRate, GeneticTrait.FeedConversion)] = 0.6,
            [(GeneticTrait.GrowthRate, GeneticTrait.DiseaseResistance)] = -0.2,
            [(GeneticTrait.GrowthRate, GeneticTrait.FilletQuality)] = -0.1,
            [(GeneticTrait.DiseaseResistance, GeneticTrait.SeaLiceResistance)] = 0.3,
            [(GeneticTrait.StressTolerance, GeneticTrait.DiseaseResistance)] = 0.2,
            [(GeneticTrait.LateMaturation, GeneticTrait.GrowthRate)] = -0.15,
        };

        private readonly Random _rng;

        public Genetics(int? seed = null)
        {
            _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        /// <summary>
        /// Generate a random genome with trait values centered around population mean.
        /// Each trait is a Breeding Value (BV) relative to population mean (0 = average).
        /// </summary>
        public Genome GenerateRandomGenome()
        {
            var genome = new Genome();
            foreach (GeneticTrait trait in Enum.GetValues(typeof(GeneticTrait)))
            {
                // Generate normally distributed breeding value
                double bv = NormalRandom(0.0, 1.0);
                genome.TraitValues[trait] = bv;
            }
            return genome;
        }

        /// <summary>
        /// Breed two parents to produce offspring genome.
        /// Implements: Mendelian segregation (50/50 parental contribution),
        /// environmental variance, genetic correlation effects.
        /// </summary>
        public Genome Breed(Genome parent1, Genome parent2)
        {
            var offspring = new Genome();
            offspring.Generation = Math.Max(parent1.Generation, parent2.Generation) + 1;

            foreach (GeneticTrait trait in Enum.GetValues(typeof(GeneticTrait)))
            {
                double h2 = Heritabilities[trait];

                // Mid-parent value (Mendelian inheritance)
                double midParent = (parent1.TraitValues[trait] + parent2.TraitValues[trait]) / 2.0;

                // Mendelian segregation variance (within-family variance)
                double segregationVariance = 0.5 * h2; // Half the additive genetic variance
                double segregation = NormalRandom(0.0, Math.Sqrt(segregationVariance));

                // Environmental variance (non-heritable component)
                double envVariance = 1.0 - h2;
                double envEffect = NormalRandom(0.0, Math.Sqrt(envVariance) * 0.3);

                // Offspring breeding value
                double offspringBV = midParent + segregation + envEffect;

                // Apply genetic correlations (correlated response)
                double correlationEffect = CalculateCorrelationEffect(trait, parent1, parent2);
                offspringBV += correlationEffect * 0.1;

                offspring.TraitValues[trait] = offspringBV;
            }

            // Rare mutation (0.1% chance per trait)
            foreach (GeneticTrait trait in Enum.GetValues(typeof(GeneticTrait)))
            {
                if (_rng.NextDouble() < 0.001)
                {
                    double mutation = NormalRandom(0.0, 0.5);
                    offspring.TraitValues[trait] += mutation;
                    offspring.HasMutation = true;
                    offspring.MutatedTraits.Add(trait);
                }
            }

            return offspring;
        }

        /// <summary>
        /// Estimate breeding value from phenotypic observations (simplified BLUP).
        /// accuracy = sqrt(h² × n / (1 + (n-1) × h²)) where n = number of observations
        /// </summary>
        public double EstimateBreedingValue(GeneticTrait trait, double phenotypicValue, int numObservations = 1)
        {
            double h2 = Heritabilities[trait];
            double accuracy = Math.Sqrt(h2 * numObservations / (1.0 + (numObservations - 1) * h2));
            return accuracy * phenotypicValue;
        }

        /// <summary>
        /// Calculate expected genetic gain per generation.
        /// ΔG = i × h² × σ_p where i = selection intensity, σ_p = phenotypic std dev
        /// </summary>
        public double ExpectedGeneticGain(GeneticTrait trait, double selectionIntensity = 1.76,
            double phenotypicStdDev = 1.0)
        {
            double h2 = Heritabilities[trait];
            return selectionIntensity * h2 * phenotypicStdDev;
        }

        /// <summary>
        /// Select best breeders from a population based on a trait or index.
        /// Returns top fraction ordered by breeding value.
        /// </summary>
        public List<Genome> SelectBreeders(List<Genome> population, GeneticTrait primaryTrait,
            double selectionFraction = 0.10)
        {
            int numToSelect = Math.Max(2, (int)(population.Count * selectionFraction));
            return population
                .OrderByDescending(g => g.TraitValues[primaryTrait])
                .Take(numToSelect)
                .ToList();
        }

        /// <summary>
        /// Select breeders using a multi-trait selection index.
        /// Weights determine relative importance of each trait.
        /// </summary>
        public List<Genome> SelectByIndex(List<Genome> population,
            Dictionary<GeneticTrait, double> traitWeights, double selectionFraction = 0.10)
        {
            int numToSelect = Math.Max(2, (int)(population.Count * selectionFraction));
            return population
                .OrderByDescending(g => CalculateSelectionIndex(g, traitWeights))
                .Take(numToSelect)
                .ToList();
        }

        /// <summary>Calculate weighted selection index for a genome.</summary>
        public double CalculateSelectionIndex(Genome genome, Dictionary<GeneticTrait, double> weights)
        {
            double index = 0.0;
            foreach (var (trait, weight) in weights)
            {
                if (genome.TraitValues.TryGetValue(trait, out double bv))
                    index += bv * weight;
            }
            return index;
        }

        /// <summary>Get heritability for a trait.</summary>
        public static double GetHeritability(GeneticTrait trait) =>
            Heritabilities.TryGetValue(trait, out double h2) ? h2 : 0.20;

        /// <summary>Get genetic correlation between two traits.</summary>
        public static double GetCorrelation(GeneticTrait trait1, GeneticTrait trait2)
        {
            if (GeneticCorrelations.TryGetValue((trait1, trait2), out double corr)) return corr;
            if (GeneticCorrelations.TryGetValue((trait2, trait1), out double corr2)) return corr2;
            return 0.0;
        }

        // ─── Internal helpers ───

        private double CalculateCorrelationEffect(GeneticTrait trait, Genome parent1, Genome parent2)
        {
            double effect = 0.0;
            foreach (GeneticTrait other in Enum.GetValues(typeof(GeneticTrait)))
            {
                if (other == trait) continue;
                double corr = GetCorrelation(trait, other);
                if (Math.Abs(corr) > 0.01)
                {
                    double otherMidParent = (parent1.TraitValues[other] + parent2.TraitValues[other]) / 2.0;
                    effect += corr * otherMidParent;
                }
            }
            return effect;
        }

        private double NormalRandom(double mean, double stdDev)
        {
            double u1 = Math.Max(1e-10, _rng.NextDouble());
            double u2 = _rng.NextDouble();
            double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            return mean + stdDev * z;
        }
    }

    /// <summary>
    /// A salmon's genetic profile — breeding values for all heritable traits.
    /// </summary>
    public class Genome
    {
        public Dictionary<GeneticTrait, double> TraitValues { get; set; } = new();
        public int Generation { get; set; } = 0;
        public bool HasMutation { get; set; } = false;
        public List<GeneticTrait> MutatedTraits { get; set; } = new();

        /// <summary>
        /// Cumulative inbreeding coefficient proxy for this genome's lineage (0..1).
        /// Additive, opt-in field: defaults to 0.0, so existing genomes and saves
        /// are unaffected until a breeding programme records a value. Only a value
        /// &gt; 0 triggers growth depression in the biology loop.
        /// </summary>
        public double InbreedingCoefficient { get; set; } = 0.0;

        public Genome()
        {
            // Initialize all traits to 0 (population mean)
            foreach (GeneticTrait trait in Enum.GetValues(typeof(GeneticTrait)))
            {
                TraitValues[trait] = 0.0;
            }
        }

        /// <summary>
        /// Get the phenotypic effect of this genome on growth rate.
        /// Returns multiplier (1.0 = average, 1.2 = 20% faster, etc.)
        /// </summary>
        public double GetGrowthMultiplier() => 1.0 + TraitValues[GeneticTrait.GrowthRate] * 0.15;

        /// <summary>Get disease resistance multiplier (higher = more resistant).</summary>
        public double GetDiseaseResistanceMultiplier() => 1.0 + TraitValues[GeneticTrait.DiseaseResistance] * 0.2;

        /// <summary>Get FCR multiplier (lower = better conversion).</summary>
        public double GetFCRMultiplier() => 1.0 - TraitValues[GeneticTrait.FeedConversion] * 0.1;

        /// <summary>Get fillet quality score (0-100, higher = better).</summary>
        public double GetFilletQuality() => 70.0 + TraitValues[GeneticTrait.FilletQuality] * 15.0;

        /// <summary>Get summary of this genome for display.</summary>
        public string GetSummary()
        {
            var top = TraitValues.OrderByDescending(kv => Math.Abs(kv.Value)).Take(3);
            return string.Join(", ", top.Select(kv =>
                $"{kv.Key}: {(kv.Value > 0 ? "+" : "")}{kv.Value:F2}"));
        }
    }
}