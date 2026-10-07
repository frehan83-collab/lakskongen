using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Data;

namespace LakskongenSim.Farm
{
    /// <summary>
    /// Smolt supplier strain (Phase 1.4): the stocking-time strategic choice.
    /// Cheap standard smolt for fast turnover vs robust coastal vs expensive
    /// RAS post-smolt that shortens the sea phase and dodges a lice peak.
    /// Trait values are 0-1 genome breeding values (see Genome multipliers).
    /// </summary>
    public sealed class SmoltStrain
    {
        public string Id { get; init; } = "standard";
        public string Name { get; init; } = "Standard S1";
        public string Description { get; init; } = "";
        public double ReleaseWeightKg { get; init; } = 0.10;
        public double CostMultiplier { get; init; } = 1.0;
        public double GrowthTrait { get; init; } = 0.1;
        public double DiseaseTrait { get; init; } = 0.1;
        public double LiceTrait { get; init; } = 0.1;
        public double FcrTrait { get; init; } = 0.1;
        public double InitialLiceLoad { get; init; } = 0.3;

        /// <summary>Build the cohort genome from this strain's traits.</summary>
        public Genome BuildGenome()
        {
            var g = new Genome();
            g.TraitValues[GeneticTrait.GrowthRate] = GrowthTrait;
            g.TraitValues[GeneticTrait.DiseaseResistance] = DiseaseTrait;
            g.TraitValues[GeneticTrait.SeaLiceResistance] = LiceTrait;
            g.TraitValues[GeneticTrait.FeedConversion] = FcrTrait;
            return g;
        }
    }

    /// <summary>Static registry of buyable smolt strains (expandable; JSON later).</summary>
    public static class SmoltStrainRegistry
    {
        public static readonly IReadOnlyList<SmoltStrain> All = new List<SmoltStrain>
        {
            new() {
                Id = "standard", Name = "Standard S1",
                Description = "Billig standardsmolt (100g). Rask omløp, full lusutsatthet.",
                ReleaseWeightKg = 0.10, CostMultiplier = 1.0,
                GrowthTrait = 0.10, DiseaseTrait = 0.10, LiceTrait = 0.10,
                FcrTrait = 0.10, InitialLiceLoad = 0.30
            },
            new() {
                Id = "robust", Name = "Kystrobust",
                Description = "Selektert for kystsone: sykdoms- og luseresistens (120g).",
                ReleaseWeightKg = 0.12, CostMultiplier = 1.4,
                GrowthTrait = 0.05, DiseaseTrait = 0.60, LiceTrait = 0.50,
                FcrTrait = 0.00, InitialLiceLoad = 0.20
            },
            new() {
                Id = "postsmolt", Name = "RAS Postsmolt 250g",
                Description = "Landbasert postsmolt: korter sjøfasen ~30%, dyr i innkjøp.",
                ReleaseWeightKg = 0.25, CostMultiplier = 2.5,
                GrowthTrait = 0.50, DiseaseTrait = 0.30, LiceTrait = 0.20,
                FcrTrait = 0.30, InitialLiceLoad = 0.15
            },
            new() {
                Id = "allfemale", Name = "Hunsortert (ultralyd)",
                Description = "96-99% hunner: jevnere vekst, mindre tidlig kjønnsmodning.",
                ReleaseWeightKg = 0.11, CostMultiplier = 1.6,
                GrowthTrait = 0.30, DiseaseTrait = 0.20, LiceTrait = 0.20,
                FcrTrait = 0.50, InitialLiceLoad = 0.25
            },
            new() {
                Id = "qtl", Name = "QTL lusresistent",
                Description = "Avlsresistens mot lus (heritabilitet utnyttet): færre påslag.",
                ReleaseWeightKg = 0.10, CostMultiplier = 2.0,
                GrowthTrait = 0.10, DiseaseTrait = 0.80, LiceTrait = 0.80,
                FcrTrait = 0.10, InitialLiceLoad = 0.10
            },
        };

        public static SmoltStrain Get(string? id) =>
            All.FirstOrDefault(s => s.Id.Equals(id ?? "", StringComparison.OrdinalIgnoreCase))
            ?? All[0];
    }
}
