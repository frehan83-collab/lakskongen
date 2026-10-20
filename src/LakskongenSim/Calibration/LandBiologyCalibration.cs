using System.ComponentModel.DataAnnotations;

namespace LakskongenSim.Calibration
{
    #region F2-13 post-smolt bridge, F2-15 grow-out biology, competence and running costs (facilities_land.json)

    // Every leaf carries [Range] and exactly one of [Source] or [GameBalance]
    // (CalibrationGuardTests walks facilities.land by reflection) and states
    // its unit, its range and its provenance in its doc comment. The sections
    // join LandCalibration as Smolt, Competence, Growth and Costs.

    /// <summary>
    /// The post-smolt bridge on seawater (roadmap F2-13, docs/FARM_TYPES_SPEC.md
    /// §2B-2C, §3.3, §3.9 <c>postSmolt</c>): the quality tier a land-grown fish
    /// carries to sea by its weight and its salinity record, desmoltification at
    /// low salinity, the river blend of a pad with a river intake, the wellboat
    /// transfer and the sale of post-smolt, and the tier premium a bought smolt
    /// costs.
    /// </summary>
    public sealed record LandSmoltCalibration
    {
        /// <summary>
        /// Weight from which a land-grown fish counts as a SuperSmolt (unit kg
        /// live weight), with its salinity record (<see cref="SuperSmoltMinSalinityPpt"/>).
        /// 0.4. Range: 0.2-1.5. Source: code (SmoltQualityRegistry, P5: the
        /// Chilean SuperSmolt trial's 400-1,000 g RAS post-smolt) and [L:T1]
        /// (target 300-750 g).
        /// </summary>
        [Range(0.2, 1.5)]
        [Source("code", "L:T1")]
        public double SuperSmoltMinWeightKg { get; init; } = 0.4;

        /// <summary>
        /// Weight from which a land-grown fish counts as a large smolt (unit kg
        /// live weight). 0.2. Range: 0.1-0.6. Source: code (SmoltQualityRegistry,
        /// P5: "~200-300 g large smolt"); Fiskeridirektoratet's profitability
        /// survey counts smolt over 250 g as large (2023, published 14.11.2024).
        /// </summary>
        [Range(0.1, 0.6)]
        [Source("code", "Fiskeridirektoratet, Lønnsomhetsundersøkelse 2023")]
        public double LargeMinWeightKg { get; init; } = 0.2;

        /// <summary>
        /// Lowest salinity a fish must have stood in on each of the last
        /// <see cref="SeawaterRecordDays"/> days to count as a SuperSmolt (unit
        /// ppt). 25. Range: 15-35. Game balance: spec §3.9 (superSmoltMinPpt):
        /// a fish adapted to near-full seawater before transfer.
        /// </summary>
        [Range(15, 35)]
        [GameBalance("spec §3.9 superSmoltMinPpt: a SuperSmolt is adapted to near-full seawater before transfer")]
        public double SuperSmoltMinSalinityPpt { get; init; } = 25.0;

        /// <summary>
        /// Days of the seawater record a SuperSmolt needs (unit days). 30.
        /// Range: 7-90. Game balance: spec F2-13 acceptance 2 ("≥ 25 ppt over the last 30 d").
        /// </summary>
        [Range(7, 90)]
        [GameBalance("spec F2-13 acceptance 2: >= 25 ppt over the last 30 days")]
        public int SeawaterRecordDays { get; init; } = 30;

        /// <summary>
        /// Salinity under which fish desmoltify when they stay in it (unit ppt).
        /// 12. Range: 5-20. Game balance: spec §2C ("desmoltification at low
        /// salinity"), §3.9 salinityMinPpt; the river blend's floor.
        /// </summary>
        [Range(5, 20)]
        [GameBalance("spec §2C/§3.9: desmoltification at low salinity, the river blend's floor")]
        public double DesmoltSalinityPpt { get; init; } = 12.0;

        /// <summary>
        /// Days in a row under <see cref="DesmoltSalinityPpt"/> after which the
        /// fish have desmoltified (unit days). 60. Range: 14-180. Game balance:
        /// spec §3.9 desmoltDays and F2-13 acceptance 3.
        /// </summary>
        [Range(14, 180)]
        [GameBalance("spec §3.9 desmoltDays, F2-13 acceptance 3")]
        public int DesmoltDays { get; init; } = 60;

        /// <summary>
        /// Multiplier on a desmoltified fish's transfer mortality (unit ×).
        /// 2.0. Range: 1-10. Game balance: spec §3.9 desmoltTransferMortalityMult
        /// and F2-13 acceptance 3 ("doubles transfer mortality").
        /// </summary>
        [Range(1, 10)]
        [GameBalance("spec §3.9 desmoltTransferMortalityMult, F2-13 acceptance 3")]
        public double DesmoltTransferMortalityMultiplier { get; init; } = 2.0;

        /// <summary>
        /// Lowest salinity a pad with a river intake can blend its post-smolt
        /// water down to (unit ppt). 0: the river alone. Range: 0-35. Game
        /// balance: spec §3.3 keeps the fish at 12-35 ppt, and F2-13 acceptance 3
        /// makes the water under 12 ppt a choice with a price (desmoltification
        /// after 60 days, against a lower H2S sulphate factor), so the blend
        /// reaches below 12.
        /// </summary>
        [Range(0, 35)]
        [GameBalance("spec §3.3 keeps the fish at 12-35 ppt; F2-13 acceptance 3 prices water under 12 ppt (desmoltification), so the blend reaches below it")]
        public double MinBlendSalinityPpt { get; init; } = 0.0;

        /// <summary>
        /// Salinity of the fjord water a pad draws (unit ppt): the most a blend
        /// can reach, and every pad's water without a river intake. 35. Range:
        /// 30-36. Source: [V:6] (FFE/NIVA 2018: seawater sulphate at 35 ppt).
        /// </summary>
        [Range(30, 36)]
        [Source("V:6")]
        public double SeawaterSalinityPpt { get; init; } = 35.0;

        /// <summary>
        /// Salinity the H2S sulphate factor is measured against (unit ppt): the
        /// factor is salinity / this (spec §3.3 "Sulphate factor: salinity/35").
        /// 35. Range: 30-36. Source: [V:6] (sulphate ≈ 2,700 mg/L in seawater
        /// against ≈ 2 mg/L in fresh water, so it scales with the seawater share).
        /// </summary>
        [Range(30, 36)]
        [Source("V:6")]
        public double SulphateReferenceSalinityPpt { get; init; } = 35.0;

        /// <summary>
        /// Lightest fish a post-smolt site may send to sea or sell (unit kg live
        /// weight): a conventional smolt. 0.1. Range: 0.05-0.3. Source: code
        /// (SmoltStrainRegistry "standard": 100 g; SmoltQualityRegistry: the
        /// conventional 80-120 g smolt).
        /// </summary>
        [Range(0.05, 0.3)]
        [Source("code")]
        public double MinTransferWeightKg { get; init; } = 0.1;

        /// <summary>
        /// Live fish one wellboat moves from a land site to sea in a day (unit
        /// t). 450. Range: 50-1,500. Source: Sølvtrans' smolt wellboat Ronja Vita
        /// (FishFarmingExpert 2019): two wells of 3,000 m³ in all, "equivalent to
        /// 450 tonnes of fish", built for calls at smolt facilities.
        /// </summary>
        [Range(50, 1500)]
        [Source("FishFarmingExpert 2019 (Ronja Vita, 3,000 m³ = 450 t)")]
        public double TransferTonnesPerDay { get; init; } = 450.0;

        /// <summary>
        /// Most days a transfer may take (unit days): a larger one has no
        /// wellboat slot. 3. Range: 1-7. Game balance: spec §3.4
        /// (TransferTankToSea, WellboatJob "transfer", 1-3 d).
        /// </summary>
        [Range(1, 7)]
        [GameBalance("spec §3.4: a transfer is a 1-3 day wellboat job")]
        public int MaxTransferDays { get; init; } = 3;

        /// <summary>
        /// Wellboat hire per day of a transfer (unit NOK). 150,000. Range:
        /// 20,000-1,000,000. Source: code (SimulationEngine.RequestWellboat: the
        /// game's spot wellboat hire for a harvest).
        /// </summary>
        [Range(20_000, 1_000_000)]
        [Source("code")]
        public decimal TransferHireNokPerDay { get; init; } = 150_000m;

        /// <summary>
        /// Price a buyer pays for post-smolt (unit NOK per kg live weight). 90.
        /// Range: 30-300. Game balance: spec §2A (post-smolt sale price 90
        /// NOK/kg live); 450 g at 90 NOK/kg is 40.5 NOK a fish against a sold
        /// smolt's 24.64 NOK (Fiskeridirektoratet, Lønnsomhetsundersøkelse 2023,
        /// table D.3.1, published 14.11.2024).
        /// </summary>
        [Range(30, 300)]
        [GameBalance("spec §2A post-smolt sale price 90 NOK/kg live; 450 g = 40.5 NOK a fish against a sold smolt's 24.64 NOK (Fiskeridirektoratet 2023)")]
        public decimal SalePriceNokPerKgLive { get; init; } = 90m;

        /// <summary>
        /// Price of a bought large smolt against a standard smolt (unit ×). 1.6.
        /// Range: 1-5. Game balance: spec §3.9 tierPremiumLarge; Fiskeridirektoratet's
        /// smolt producers that sell smolt over 250 g sit in the group costing
        /// 37.52 NOK a fish against 19.10 for pure producers of smaller smolt
        /// (×1.96; Lønnsomhetsundersøkelse 2023, table D.4.4).
        /// </summary>
        [Range(1, 5)]
        [GameBalance("spec §3.9 tierPremiumLarge; Fiskeridirektoratet 2023: producers of smolt over 250 g sit at 37.52 against 19.10 NOK a fish (x1.96)")]
        public double TierPremiumLarge { get; init; } = 1.6;

        /// <summary>
        /// Price of a bought SuperSmolt against a standard smolt (unit ×). 2.5.
        /// Range: 1-8. Game balance: spec §3.9 tierPremiumSuper (see
        /// <see cref="TierPremiumLarge"/>; a 400 g fish takes more than twice a
        /// 250 g fish's land days).
        /// </summary>
        [Range(1, 8)]
        [GameBalance("spec §3.9 tierPremiumSuper; above the large smolt's x1.96 of Fiskeridirektoratet 2023")]
        public double TierPremiumSuperSmolt { get; init; } = 2.5;
    }

    /// <summary>
    /// Competence on a land grow-out site (roadmap F2-15, spec §3.3 "Growth and
    /// competence"): where it starts, what each lever adds and costs, the
    /// utilisation cap it sets and the growth it gives. The plant's own fish
    /// grow on the plan at competence 1.0 and at the achieved result at the
    /// start ([L:R1], [V:17]).
    /// </summary>
    public sealed record LandCompetenceCalibration
    {
        /// <summary>
        /// Competence of a new grow-out site (unit 0-1). 0.4. Range: 0-1.
        /// Game balance: spec §3.3 ("competence starts at 0.4"): the level that
        /// gives the achieved result of [L:R1]/[V:17].
        /// </summary>
        [Range(0, 1)]
        [GameBalance("spec §3.3: competence starts at 0.4, the level of the achieved result ([L:R1], [V:17])")]
        public double Start { get; init; } = 0.4;

        /// <summary>
        /// Competence each completed production cycle adds (unit 0-1). 0.2.
        /// Range: 0-0.5. Game balance: spec §3.3 (three cycles take a plant from
        /// 0.4 to the plan, the Oppkjøring 55 → 70 → 85 → 100 %).
        /// </summary>
        [Range(0, 0.5)]
        [GameBalance("spec §3.3: three cycles take a plant from 0.4 to the plan (55 -> 70 -> 85 -> 100 %)")]
        public double PerCycle { get; init; } = 0.2;

        /// <summary>
        /// Competence the research <c>land-based-ras</c> adds (unit 0-1). 0.1.
        /// Range: 0-0.3. Game balance: spec §3.3.
        /// </summary>
        [Range(0, 0.3)]
        [GameBalance("spec §3.3: research land-based-ras +0.1")]
        public double Research { get; init; } = 0.1;

        /// <summary>
        /// Competence a hired operations manager (driftsleder) adds while
        /// employed (unit 0-1). 0.1. Range: 0-0.3. Game balance: spec §3.3.
        /// </summary>
        [Range(0, 0.3)]
        [GameBalance("spec §3.3: hiring a driftsleder +0.1")]
        public double Driftsleder { get; init; } = 0.1;

        /// <summary>
        /// Yearly cost of the operations manager (unit NOK per year). 2,500,000.
        /// Range: 500,000-10,000,000. Game balance: spec §3.3 (2.5 M a year).
        /// </summary>
        [Range(500_000, 10_000_000)]
        [GameBalance("spec §3.3: a driftsleder costs 2.5 M a year")]
        public decimal DriftslederNokPerYear { get; init; } = 2_500_000m;

        /// <summary>
        /// Competence a training programme adds per completed step (unit 0-1).
        /// 0.05. Range: 0-0.2. Game balance: spec §3.3 (+0.05 per 6 months).
        /// </summary>
        [Range(0, 0.2)]
        [GameBalance("spec §3.3: training +0.05 per 6 months")]
        public double TrainingPerStep { get; init; } = 0.05;

        /// <summary>
        /// Days of a training step (unit days). 182. Range: 30-365. Game
        /// balance: spec §3.3 (6 months).
        /// </summary>
        [Range(30, 365)]
        [GameBalance("spec §3.3: a training step is 6 months")]
        public int TrainingStepDays { get; init; } = 182;

        /// <summary>
        /// Most competence the training programme adds (unit 0-1). 0.15.
        /// Range: 0-0.5. Game balance: spec §3.3 (up to +0.15).
        /// </summary>
        [Range(0, 0.5)]
        [GameBalance("spec §3.3: training up to +0.15")]
        public double TrainingMax { get; init; } = 0.15;

        /// <summary>
        /// Cost of the training programme, paid when it starts (unit NOK).
        /// 3,000,000. Range: 0-20,000,000. Game balance: spec §3.3 (3 M).
        /// </summary>
        [Range(0, 20_000_000)]
        [GameBalance("spec §3.3: a training programme costs 3 M")]
        public decimal TrainingNok { get; init; } = 3_000_000m;

        /// <summary>
        /// Competence an external consultancy adds while it runs (unit 0-1).
        /// 0.1. Range: 0-0.3. Game balance: spec §3.3.
        /// </summary>
        [Range(0, 0.3)]
        [GameBalance("spec §3.3: an external consultancy +0.1 for 12 months")]
        public double Consultancy { get; init; } = 0.1;

        /// <summary>
        /// Days the consultancy runs (unit days). 365. Range: 30-730. Game
        /// balance: spec §3.3 (12 months).
        /// </summary>
        [Range(30, 730)]
        [GameBalance("spec §3.3: the consultancy runs 12 months")]
        public int ConsultancyDays { get; init; } = 365;

        /// <summary>
        /// Cost of the consultancy, paid when it starts (unit NOK). 8,000,000.
        /// Range: 0-50,000,000. Game balance: spec §3.3 (8 M).
        /// </summary>
        [Range(0, 50_000_000)]
        [GameBalance("spec §3.3: an external consultancy costs 8 M")]
        public decimal ConsultancyNok { get; init; } = 8_000_000m;

        /// <summary>
        /// Competence the operating staff add when they were hired well before
        /// the first fish (unit 0-1). 0.1. Range: 0-0.3. Game balance: spec §3.3.
        /// </summary>
        [Range(0, 0.3)]
        [GameBalance("spec §3.3: hiring staff >= 180 days before first fish +0.1")]
        public double EarlyHiring { get; init; } = 0.1;

        /// <summary>
        /// Days before the site's first fish the staff must be hired for the
        /// early-hiring bonus (unit days). 180. Range: 30-730. Game balance: spec §3.3.
        /// </summary>
        [Range(30, 730)]
        [GameBalance("spec §3.3: staff hired >= 180 days before first fish")]
        public int EarlyHiringLeadDays { get; init; } = 180;

        /// <summary>
        /// Utilisation cap at the start competence (unit share of nameplate a
        /// year). 0.55. Range: 0.2-1. Game balance: spec §3.3 ("early plants never
        /// exceeded 50-60 %"); the cap rises linearly to 1 at competence 1.
        /// </summary>
        [Range(0.2, 1)]
        [GameBalance("spec §3.3: early plants never exceeded 50-60 % of nameplate")]
        public double UtilisationCapAtStart { get; init; } = 0.55;

        /// <summary>
        /// Growth at the start competence against the plan (unit × the plan's
        /// growth, <c>production.planGrowthMultiplier</c>). 0.79. Range: 0.4-1.
        /// Game balance: fitted so a cohort at competence 0.4 reaches the achieved
        /// result, 400 g → 3.5-4.2 kg HOG in 9-11 months ([L:R1] Q2 2026: 3.5 kg
        /// HOG average; [V:17] guidance cut for weak biomass growth); F2-15
        /// acceptance 1 decides it. It rises linearly to 1 (the plan, 400 g →
        /// 5.2 kg LW in 8-9 months, [L:R1]) at competence 1.
        /// </summary>
        [Range(0.4, 1)]
        [GameBalance("fitted so competence 0.4 gives the achieved result ([L:R1], [V:17]); F2-15 acceptance 1 decides it")]
        public double GrowthAtStart { get; init; } = 0.79;
    }

    /// <summary>
    /// A land cohort against its plan (roadmap F2-15, spec §3.3 "Growth
    /// shortfall"): when a tank's fish count as behind, and the feed-plan
    /// penalty a low-competence plant pays for changing its feeding.
    /// </summary>
    public sealed record LandGrowthCalibration
    {
        /// <summary>
        /// Share of the plan's weight under which a cohort is behind (unit
        /// fraction). 0.85. Range: 0.5-1. Game balance: spec §3.3 and F2-15
        /// acceptance 3 ("below 85 % of plan").
        /// </summary>
        [Range(0.5, 1)]
        [GameBalance("spec §3.3, F2-15 acceptance 3: below 85 % of plan")]
        public double ShortfallShare { get; init; } = 0.85;

        /// <summary>
        /// Days in a row a cohort must be behind before the growth shortfall is
        /// raised (unit days). 30. Range: 7-120. Game balance: spec §3.3 and F2-15
        /// acceptance 3 ("for 30 d").
        /// </summary>
        [Range(7, 120)]
        [GameBalance("spec §3.3, F2-15 acceptance 3: for 30 days")]
        public int ShortfallDays { get; init; } = 30;

        /// <summary>
        /// FCR added after a feed-plan change at low competence (unit kg feed
        /// per kg gain). 0.12. Range: 0-0.5. Game balance: spec §3.3 after [L:R2]
        /// (the 2025 feed change); the size is game balance.
        /// </summary>
        [Range(0, 0.5)]
        [GameBalance("spec §3.3 after the 2025 feed change in [L:R2]; the size is game balance")]
        public double FeedPlanFcrPenalty { get; init; } = 0.12;

        /// <summary>
        /// Days the feed-plan penalty lasts (unit days). 60. Range: 7-180. Game
        /// balance: spec §3.3.
        /// </summary>
        [Range(7, 180)]
        [GameBalance("spec §3.3: the penalty lasts 60 days")]
        public int FeedPlanPenaltyDays { get; init; } = 60;

        /// <summary>
        /// Competence under which a feed-plan change costs the penalty (unit
        /// 0-1). 0.6. Range: 0-1. Game balance: spec §3.3.
        /// </summary>
        [Range(0, 1)]
        [GameBalance("spec §3.3: a feed-plan change at competence < 0.6")]
        public double FeedPlanPenaltyBelowCompetence { get; init; } = 0.6;

        /// <summary>
        /// Weeks ahead the harvest planner and PlanHarvest reach (unit weeks).
        /// 26. Range: 4-104. Game balance: half a year, past a land cohort's
        /// remaining months at harvest weight.
        /// </summary>
        [Range(4, 104)]
        [GameBalance("half a year: past a land cohort's remaining months at harvest weight")]
        public int HarvestPlanWeeks { get; init; } = 26;
    }

    /// <summary>
    /// Running costs of a land site beyond its electricity, oxygen and staff
    /// (roadmap F2-15, the farming-cost band of spec §2A): maintenance and
    /// insurance on the plant, fish insurance, sludge disposal, and the
    /// laboratory, chemicals and office costs. Sourced from Bjørndal &amp;
    /// Tusvik's land-based cost model (NTNU Working Paper 1/2017, 2017 NOK;
    /// published in Aquaculture Economics &amp; Management 2019) and brought to
    /// 2025 NOK with SSB's consumer price index (table 03014: 105.5 in 2017,
    /// 137.7 in 2025, ×1.305).
    /// </summary>
    public sealed record LandCostCalibration
    {
        /// <summary>
        /// Service, maintenance and repairs a year (unit share of the unit's
        /// capex). 0.0088. Range: 0-0.05. Source: Bjørndal &amp; Tusvik 2017,
        /// tables 4.1 and 4.3: NOK 3.5 M a year on a NOK 398.6 M plant.
        /// </summary>
        [Range(0, 0.05)]
        [Source("Bjørndal & Tusvik 2017 (NTNU WP 1/2017), tables 4.1 and 4.3")]
        public double MaintenanceShareOfCapexPerYear { get; init; } = 0.0088;

        /// <summary>
        /// Insurance on buildings and equipment a year (unit share of the unit's
        /// capex). 0.0015. Range: 0-0.02. Source: Bjørndal &amp; Tusvik 2017,
        /// table 4.5: NOK 0.604 M on NOK 398.6 M of buildings and equipment.
        /// </summary>
        [Range(0, 0.02)]
        [Source("Bjørndal & Tusvik 2017 (NTNU WP 1/2017), table 4.5")]
        public double EquipmentInsuranceShareOfCapexPerYear { get; init; } = 0.0015;

        /// <summary>
        /// Fish insurance per kg of feed fed (unit NOK/kg feed). 0.86. Range:
        /// 0-5. Source: Bjørndal &amp; Tusvik 2017, table 4.5: 0.7 NOK per kg live
        /// weight produced (2017 NOK) × 1.305 (SSB table 03014) ÷ an economic
        /// FCR of 1.06 (Salmon Evolution Q2 2026, SalmonBusiness 18.08.2026).
        /// </summary>
        [Range(0, 5)]
        [Source("Bjørndal & Tusvik 2017, table 4.5", "SSB table 03014", "SalmonBusiness 18.08.2026 (eFCR 1.06)")]
        public decimal FishInsuranceNokPerKgFeed { get; init; } = 0.86m;

        /// <summary>
        /// Sludge, waste and wastewater per kg of feed fed (unit NOK/kg feed).
        /// 1.57. Range: 0-10. Source: Bjørndal &amp; Tusvik 2017, tables 4.3 and
        /// 4.4: 1.5 kg of wet sludge per kg feed at NOK 800 a tonne (2017 NOK) ×
        /// 1.305 (SSB table 03014).
        /// </summary>
        [Range(0, 10)]
        [Source("Bjørndal & Tusvik 2017, tables 4.3-4.4", "SSB table 03014")]
        public decimal SludgeNokPerKgFeed { get; init; } = 1.57m;

        /// <summary>
        /// Laboratory, chemicals, salinity, office and administration a year per
        /// tonne of the unit's nameplate (unit NOK per t HOG of nameplate a
        /// year). 3,617. Range: 0-20,000. Source: Bjørndal &amp; Tusvik 2017,
        /// tables 4.3-4.4: other annual operating costs NOK 10 M and office and
        /// administration NOK 1.5 M on 5,000 t live weight (2017 NOK) × 1.305
        /// (SSB table 03014) ÷ 0.83 kg HOG per kg live weight ([L:R1]).
        /// </summary>
        [Range(0, 20_000)]
        [Source("Bjørndal & Tusvik 2017, tables 4.3-4.4", "SSB table 03014", "L:R1")]
        public decimal OtherNokPerTonneNameplateYear { get; init; } = 3_617m;
    }

    /// <summary>
    /// Early maturation of land fish (spec §3.3 "Maturation", §3.9
    /// <c>maturation</c>): an index that rises with the degree-days over a
    /// threshold, under continuous or short-day light, slower for all-female
    /// stock; the share of males maturing by harvest. F2-15 projects it for the
    /// Vannstrategi presets' maturation risk (<c>Farm/Land/LandMaturation.cs</c>);
    /// F2-16 runs it in the tick and downgrades the maturing fish.
    /// </summary>
    public sealed record LandMaturationCalibration
    {
        /// <summary>
        /// Water temperature over which maturation builds (unit °C). 12. Range:
        /// 8-16. Source: [L:S8] (Frontiers 2024: post-smolt reared at or under
        /// 12 °C to hold back early maturation); [L:R6] rearing target 12-14 °C.
        /// </summary>
        [Range(8, 16)]
        [Source("L:S8")]
        public double ThresholdC { get; init; } = 12.0;

        /// <summary>
        /// Rise of the index per degree-day over the threshold (unit per °C·day).
        /// 0.004. Range: 0-0.05. Game balance: spec §3.3 (0.004, [L:S1], [L:S8],
        /// [L:S10] give the direction; the rate is the game's): a cohort at
        /// 13 °C for 250 days reaches 1.0.
        /// </summary>
        [Range(0, 0.05)]
        [GameBalance("spec §3.3 rate after [L:S1], [L:S8], [L:S10]; the rate is the game's")]
        public double RatePerDegreeDay { get; init; } = 0.004;

        /// <summary>
        /// Factor on the rise under a short-day photoperiod against continuous
        /// light (unit ×). 0.3. Range: 0-1. Game balance: spec §3.3 after [L:S10]
        /// (continuous light in RAS advances maturation).
        /// </summary>
        [Range(0, 1)]
        [GameBalance("spec §3.3 after [L:S10]: continuous light advances maturation")]
        public double ShortDayFactor { get; init; } = 0.3;

        /// <summary>
        /// Factor on the rise for all-female stock (unit ×). 0.1. Range: 0-1.
        /// Game balance: spec §3.3: females mature late; all-female stock is the
        /// industry's counter.
        /// </summary>
        [Range(0, 1)]
        [GameBalance("spec §3.3: all-female stock matures late")]
        public double AllFemaleFactor { get; init; } = 0.1;

        /// <summary>
        /// Most of the males that mature by harvest (unit fraction). 0.8.
        /// Range: 0-1. Game balance: spec §3.3 (the downgrade's cap).
        /// </summary>
        [Range(0, 1)]
        [GameBalance("spec §3.3: the maturing share of males is capped at 80 %")]
        public double MaxMaleShare { get; init; } = 0.8;

        /// <summary>
        /// Share of males in a mixed-sex stock (unit fraction). 0.5. Range:
        /// 0.4-0.6. Source: Atlantic salmon's 1:1 primary sex ratio (Taranger
        /// et al. 2010, ICES J. Mar. Sci. 67:1176-1182, risks of early maturation
        /// in farmed salmon).
        /// </summary>
        [Range(0.4, 0.6)]
        [Source("Taranger et al. 2010, ICES J. Mar. Sci. 67:1176-1182")]
        public double MaleShare { get; init; } = 0.5;
    }

    /// <summary>
    /// One named Vannstrategi preset (spec §6.5 "Vannstrategi": three presets,
    /// each with its predicted kWh/kg, growth and maturation risk from the
    /// engine's projection; the sliders sit behind "Avansert").
    /// </summary>
    public sealed record LandPresetCalibration
    {
        /// <summary>
        /// Reuse r of a flow-through unit (unit fraction). Range: 0-0.99. Game
        /// balance: spec §6.5. A loop unit (RAS hall, post-smolt module) keeps
        /// its own reuse.
        /// </summary>
        [Range(0, 0.99)]
        [GameBalance("spec §6.5 preset; a loop unit keeps its own reuse")]
        public double Reuse { get; init; }

        /// <summary>
        /// Share of the new water from the deep intake (unit fraction), used
        /// when the preset does not blend toward the setpoint. Range: 0-1. Game
        /// balance: spec §6.5 ("max deep share" = 1).
        /// </summary>
        [Range(0, 1)]
        [GameBalance("spec §6.5 preset: max deep share = 1")]
        public double DeepIntakeShare { get; init; } = 1.0;

        /// <summary>
        /// True when the intakes blend toward the setpoint every day (unit flag,
        /// 0 or 1). Range: 0-1. Game balance: spec §6.5 ("seasonal blend").
        /// </summary>
        [Range(0, 1)]
        [GameBalance("spec §6.5 preset: seasonal blend")]
        public bool BlendTowardSetpoint { get; init; }

        /// <summary>
        /// Tank setpoint (unit °C). Range: 6-16. Game balance: spec §6.5.
        /// </summary>
        [Range(6, 16)]
        [GameBalance("spec §6.5 preset")]
        public double SetpointC { get; init; } = 12.0;
    }

    /// <summary>
    /// The three Vannstrategi presets of spec §6.5 (F2-15, the land board's
    /// projections; F2-32 shows them): Sparsom (r 0.75, max deep share, 12 °C),
    /// Balansert (r 0.65, seasonal blend, 12-13 °C) and Høy tetthet (r 0.80,
    /// max deep share, 13 °C).
    /// </summary>
    public sealed record LandVannstrategiCalibration
    {
        /// <summary>"Sparsom": r 0.75, max deep share, 12 °C.</summary>
        public LandPresetCalibration Sparsom { get; init; } = new() { Reuse = 0.75, DeepIntakeShare = 1.0, BlendTowardSetpoint = false, SetpointC = 12.0 };

        /// <summary>"Balansert": r 0.65, the intakes blended toward the setpoint, 12.5 °C (the middle of 12-13).</summary>
        public LandPresetCalibration Balansert { get; init; } = new() { Reuse = 0.65, DeepIntakeShare = 0.5, BlendTowardSetpoint = true, SetpointC = 12.5 };

        /// <summary>"Høy tetthet": r 0.80, max deep share, 13 °C.</summary>
        public LandPresetCalibration HoyTetthet { get; init; } = new() { Reuse = 0.80, DeepIntakeShare = 1.0, BlendTowardSetpoint = false, SetpointC = 13.0 };
    }

    /// <summary>
    /// The comfort bands the land board's water glyph reads (spec §6.5
    /// "Vannmiljø": a 5-segment ring of O2, CO2, TAN, temperature and density
    /// against its comfort band; status ok / warn / critical). The ok edges are
    /// the model's own (outlet O2 floor, CO2 and TAN appetite limits, the
    /// density knee); these are the critical edges.
    /// </summary>
    public sealed record LandStatusCalibration
    {
        /// <summary>
        /// Dissolved oxygen under which a tank is critical (unit mg/L). 5.
        /// Range: 2-8. Source: [L:F2] (an outage takes O2 under 5 mg/L in about
        /// 40 minutes; the danger line the alarms use).
        /// </summary>
        [Range(2, 8)]
        [Source("L:F2")]
        public double DoCriticalMgL { get; init; } = 5.0;

        /// <summary>
        /// Dissolved oxygen under which a tank is in warning (unit mg/L). 7.
        /// Range: 4-10. Game balance: spec §3.3's usable floor (7.0 mg/L, about
        /// 80 % saturation at 12 °C); the outlet is held at 8.
        /// </summary>
        [Range(4, 10)]
        [GameBalance("spec §3.3: the usable floor 7.0 mg/L (about 80 % saturation)")]
        public double DoWarnMgL { get; init; } = 7.0;

        /// <summary>
        /// TAN over which a tank is critical (unit × the appetite limit). 2.
        /// Range: 1-5. Game balance: twice the 2 mg/L where appetite starts to
        /// fall (spec §3.3), where the mortality hazard runs.
        /// </summary>
        [Range(1, 5)]
        [GameBalance("twice the TAN appetite limit of spec §3.3")]
        public double TanCriticalFactor { get; init; } = 2.0;

        /// <summary>
        /// Distance from the setpoint at which a tank's temperature is in
        /// warning (unit °C). 1. Range: 0.2-5. Game balance: the heat pumps
        /// hold the setpoint within capacity; a degree off means they do not.
        /// </summary>
        [Range(0.2, 5)]
        [GameBalance("the heat pumps hold the setpoint within capacity; a degree off means they do not")]
        public double TemperatureWarnC { get; init; } = 1.0;

        /// <summary>
        /// Distance from the setpoint at which a tank's temperature is critical
        /// (unit °C). 2.5. Range: 0.5-8. Game balance: past it growth and
        /// maturation leave the plan (the law's optimum 13 °C, the maturation
        /// threshold 12 °C).
        /// </summary>
        [Range(0.5, 8)]
        [GameBalance("past it growth and maturation leave the plan (optimum 13 degC, maturation from 12 degC)")]
        public double TemperatureCriticalC { get; init; } = 2.5;
    }

    #endregion
}
