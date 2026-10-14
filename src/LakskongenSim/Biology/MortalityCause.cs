using System;

namespace LakskongenSim.Biology
{
    /// <summary>
    /// Death categories of the mortality ledger (S2-05, roadmap D4). The first
    /// six are the level-1 categories the Norwegian Veterinary Institute reports
    /// from the industry's cause-of-death database ("Fiskehelsedatabasen",
    /// Sjømat Norge / AquaCloud): A infectious diseases, B environmental
    /// conditions, C injuries (trauma), D physiological causes, E other causes,
    /// F unknown cause. National 2024 shares of registered dead salmon: A 32.9 %,
    /// B 8.8 %, C 26.6 %, D 5.7 %, E 4.8 %, F 21.2 % (Fiskehelserapporten 2024,
    /// Veterinærinstituttet rapportserie 1a/2025, table 2.4.2, p. 35,
    /// https://www.vetinst.no/rapporter-og-publikasjoner/rapporter/2025/fiskehelserapporten-2024).
    /// <see cref="Predators"/> is the roadmap's own row (the NVI table has no
    /// predator class); escapes are not deaths and are booked as a
    /// <see cref="FishRemoval"/>.
    /// </summary>
    public enum MortalityCategory
    {
        /// <summary>A "Infeksjonssykdommer": disease deaths and disease-control culls.</summary>
        Infectious,
        /// <summary>B "Miljøforhold": temperature, oxygen, harmful algae, jellyfish, weather.</summary>
        Environment,
        /// <summary>C "Skader (traume)": handling injuries and non-medicinal (mechanical/thermal) delousing.</summary>
        InjuryAndTreatment,
        /// <summary>D "Fysiologiske årsaker": smoltification failure, maladaptation, maturation.</summary>
        Physiological,
        /// <summary>Seals, otters and birds (roadmap row; NVI files them inside its six classes).</summary>
        Predators,
        /// <summary>E "Andre årsaker".</summary>
        Other,
        /// <summary>F "Ukjent årsak": deaths the model cannot attribute (today: the calibrated baseline anchor).</summary>
        Unexplained,
    }

    /// <summary>
    /// The cause one death is booked to. Each cause belongs to exactly one
    /// <see cref="MortalityCategory"/> (<see cref="MortalityCauses.CategoryOf"/>).
    /// New hazards (land-based power/O2/CO2/H2S failures, offshore storm
    /// exposure, predators) add a cause here and book through
    /// <see cref="FishPopulation.RemoveDead"/>.
    /// </summary>
    public enum MortalityCause
    {
        /// <summary>
        /// The calibrated whole-cycle sea anchor (and the freshwater baseline):
        /// a top-down hazard with no mechanism, so the model books it as
        /// unexplained until S4-03 replaces it with hazards per cause.
        /// </summary>
        BaselineAnchor,
        /// <summary>The disease system's daily hazard (SIR prevalence × lethality).</summary>
        Disease,
        /// <summary>A notifiable-disease cull order (ISA: the whole pen is destroyed).</summary>
        DiseaseCull,
        /// <summary>Deaths during and after a delousing treatment.</summary>
        Delousing,
        /// <summary>Handling losses: transfer between sites, vaccination.</summary>
        Handling,
        /// <summary>Water temperature outside the stress-free band.</summary>
        Temperature,
        /// <summary>Hypoxia: a plankton-crash oxygen collapse or a low-oxygen event.</summary>
        LowOxygen,
        /// <summary>A harmful algal bloom.</summary>
        HarmfulAlgae,
        /// <summary>Jellyfish (gill and skin damage).</summary>
        Jellyfish,
        /// <summary>Storm episode stress and mechanical damage.</summary>
        Storm,
        /// <summary>Seals, otters, birds.</summary>
        Predator,
    }

    /// <summary>Fish that leave a cohort alive: not deaths, but booked so every fish is accounted for.</summary>
    public enum FishRemoval
    {
        /// <summary>Escaped through a damaged net.</summary>
        Escape,
        /// <summary>Harvested and sold by the player.</summary>
        Harvest,
        /// <summary>Harvested on a Mattilsynet order (biomass over the MAB).</summary>
        ForcedHarvest,
        /// <summary>Moved alive to another site.</summary>
        TransferOut,
    }

    /// <summary>Mapping and labels for <see cref="MortalityCause"/> and <see cref="MortalityCategory"/>.</summary>
    public static class MortalityCauses
    {
        /// <summary>Every cause, in declaration order.</summary>
        public static readonly MortalityCause[] All = Enum.GetValues<MortalityCause>();

        /// <summary>Every category, in declaration order.</summary>
        public static readonly MortalityCategory[] AllCategories = Enum.GetValues<MortalityCategory>();

        /// <summary>Every live-removal kind, in declaration order.</summary>
        public static readonly FishRemoval[] AllRemovals = Enum.GetValues<FishRemoval>();

        /// <summary>The category a cause is reported under.</summary>
        public static MortalityCategory CategoryOf(MortalityCause cause) => cause switch
        {
            MortalityCause.BaselineAnchor => MortalityCategory.Unexplained,
            MortalityCause.Disease => MortalityCategory.Infectious,
            MortalityCause.DiseaseCull => MortalityCategory.Infectious,
            MortalityCause.Delousing => MortalityCategory.InjuryAndTreatment,
            MortalityCause.Handling => MortalityCategory.InjuryAndTreatment,
            MortalityCause.Temperature => MortalityCategory.Environment,
            MortalityCause.LowOxygen => MortalityCategory.Environment,
            MortalityCause.HarmfulAlgae => MortalityCategory.Environment,
            MortalityCause.Jellyfish => MortalityCategory.Environment,
            MortalityCause.Storm => MortalityCategory.Environment,
            MortalityCause.Predator => MortalityCategory.Predators,
            _ => throw new ArgumentOutOfRangeException(nameof(cause), cause, "unmapped mortality cause"),
        };

        /// <summary>NVI level-1 code (A-F) of a category; empty for the roadmap's predator row.</summary>
        public static string NviCode(MortalityCategory category) => category switch
        {
            MortalityCategory.Infectious => "A",
            MortalityCategory.Environment => "B",
            MortalityCategory.InjuryAndTreatment => "C",
            MortalityCategory.Physiological => "D",
            MortalityCategory.Other => "E",
            MortalityCategory.Unexplained => "F",
            _ => "",
        };

        /// <summary>Stable snake_case id (save files, bridge dictionaries).</summary>
        public static string Id(MortalityCategory category) => category switch
        {
            MortalityCategory.Infectious => "infectious",
            MortalityCategory.Environment => "environment",
            MortalityCategory.InjuryAndTreatment => "injury_treatment",
            MortalityCategory.Physiological => "physiological",
            MortalityCategory.Predators => "predators",
            MortalityCategory.Other => "other",
            MortalityCategory.Unexplained => "unexplained",
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, "unknown category"),
        };

        /// <summary>Stable snake_case id (save files, bridge dictionaries).</summary>
        public static string Id(MortalityCause cause) => cause switch
        {
            MortalityCause.BaselineAnchor => "baseline_anchor",
            MortalityCause.Disease => "disease",
            MortalityCause.DiseaseCull => "disease_cull",
            MortalityCause.Delousing => "delousing",
            MortalityCause.Handling => "handling",
            MortalityCause.Temperature => "temperature",
            MortalityCause.LowOxygen => "low_oxygen",
            MortalityCause.HarmfulAlgae => "harmful_algae",
            MortalityCause.Jellyfish => "jellyfish",
            MortalityCause.Storm => "storm",
            MortalityCause.Predator => "predator",
            _ => throw new ArgumentOutOfRangeException(nameof(cause), cause, "unknown cause"),
        };

        /// <summary>Stable snake_case id (save files, bridge dictionaries).</summary>
        public static string Id(FishRemoval removal) => removal switch
        {
            FishRemoval.Escape => "escape",
            FishRemoval.Harvest => "harvest",
            FishRemoval.ForcedHarvest => "forced_harvest",
            FishRemoval.TransferOut => "transfer_out",
            _ => throw new ArgumentOutOfRangeException(nameof(removal), removal, "unknown removal"),
        };

        /// <summary>Translation key of a category label ("MORTALITY_CATEGORY_INFECTIOUS").</summary>
        public static string TranslationKey(MortalityCategory category) => "MORTALITY_CATEGORY_" + Id(category).ToUpperInvariant();

        /// <summary>Translation key of a cause label ("MORTALITY_CAUSE_JELLYFISH").</summary>
        public static string TranslationKey(MortalityCause cause) => "MORTALITY_CAUSE_" + Id(cause).ToUpperInvariant();

        /// <summary>Translation key of a live-removal label ("FISH_REMOVAL_ESCAPE").</summary>
        public static string TranslationKey(FishRemoval removal) => "FISH_REMOVAL_" + Id(removal).ToUpperInvariant();

        /// <summary>The cause a bloom-deck event kind kills under ("jellyfish", "algae", "hypoxia").</summary>
        public static MortalityCause ForBloomKind(string? kind) => kind switch
        {
            "jellyfish" => MortalityCause.Jellyfish,
            "algae" => MortalityCause.HarmfulAlgae,
            _ => MortalityCause.LowOxygen,
        };
    }
}
