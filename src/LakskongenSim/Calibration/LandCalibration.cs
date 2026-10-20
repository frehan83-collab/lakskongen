using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LakskongenSim.Calibration
{
    #region F2-12 land water, energy and tanks

    // Every leaf below documents its value, unit, a "Range:" and exactly one of
    // "Source:" (a dossier key of docs/research/FARM_TYPES_RESEARCH.md, [L:x] or
    // [V:n], or a cited publication) or "Game balance:" (a value chosen for the
    // game, with the acceptance test that decides it), and carries the same as
    // F2-04's attributes: [Range] and exactly one of [Source]/[GameBalance]
    // (CalibrationGuardTests walks them by reflection in facilities.land;
    // LandCalibrationTests.EveryLeaf_DocumentsRangeAndOneProvenance still reads
    // the doc comments with Roslyn and holds the same rule). The section joins
    // FacilitiesCalibration in LandBuildCalibration.cs.

    /// <summary>
    /// Calibration of the land-based farm physics (roadmap F2-12,
    /// docs/FARM_TYPES_SPEC.md §3.3 and §3.9): the tank templates, the water
    /// loop (flows, reuse, intake blend, oxygen, CO2, TAN), the energy built from
    /// its physical parts, and the projection the stocking check and the
    /// Vannstrategi sweep run on; and (F2-14) what a land unit costs and how
    /// long it takes to build (<see cref="Build"/>), its permits
    /// (<see cref="Permits"/>) and its running costs (<see cref="Operations"/>).
    /// Loaded from <c>godot/data/calibration/facilities_land.json</c> by F2-04's
    /// include loader into <c>facilities.land</c>; <see cref="Parse"/> reads
    /// the file alone, and the code defaults are the values the file holds.
    /// </summary>
    public sealed record LandCalibration
    {
        /// <summary>The full hybrid flow-through lot (HFS, the Salmon Evolution phase-1 plant).</summary>
        public LandTemplateCalibration Hfs { get; init; } = LandTemplateCalibration.HfsLot;

        /// <summary>The HFS pilot lot (4 grow-out + 1 pre-grow-out tank), expandable to a full lot.</summary>
        public LandTemplateCalibration HfsPilot { get; init; } = LandTemplateCalibration.HfsPilotLot;

        /// <summary>The RAS hall (stage 3): grow-out tanks on one recirculation loop.</summary>
        public LandTemplateCalibration Ras { get; init; } = LandTemplateCalibration.RasHall;

        /// <summary>The post-smolt module (seawater RAS, 2 tanks).</summary>
        public LandTemplateCalibration PostSmolt { get; init; } = LandTemplateCalibration.PostSmoltModule;

        /// <summary>Intake water shared by every template.</summary>
        public LandIntakeCalibration Intake { get; init; } = new();

        /// <summary>Oxygen demand, supply and liquid oxygen.</summary>
        public LandOxygenCalibration Oxygen { get; init; } = new();

        /// <summary>Dissolved CO2 and its effect on appetite.</summary>
        public LandCo2Calibration Co2 { get; init; } = new();

        /// <summary>Ammonia (TAN), the biofilter and nitrate.</summary>
        public LandTanCalibration Tan { get; init; } = new();

        /// <summary>Stocking density and its effect on growth.</summary>
        public LandDensityCalibration Density { get; init; } = new();

        /// <summary>Pumping, treatment, heating and cooling energy.</summary>
        public LandEnergyCalibration Energy { get; init; } = new();

        /// <summary>The production plan the stocking check and the projections grow fish on.</summary>
        public LandProductionCalibration Production { get; init; } = new();

        /// <summary>Capex and construction phases of each land unit (F2-14).</summary>
        public LandBuildCalibration Build { get; init; } = new();

        /// <summary>The land permits: decision windows, refusal odds and their drivers, fees, grid projects (F2-14).</summary>
        public LandPermitCalibration Permits { get; init; } = new();

        /// <summary>Running costs of a land site beyond energy and oxygen (F2-12).</summary>
        public LandOperationsCalibration Operations { get; init; } = new();

        /// <summary>The code defaults (the values the draft JSON holds).</summary>
        public static LandCalibration Default { get; } = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        /// <summary>
        /// Read <c>facilities_land.json</c>. Keys missing from the file keep
        /// their code defaults; F2-04's include loader replaces this call.
        /// </summary>
        public static LandCalibration Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            return JsonSerializer.Deserialize<LandCalibration>(json, JsonOptions) ?? new LandCalibration();
        }

        /// <summary>The template record of <paramref name="template"/>.</summary>
        public LandTemplateCalibration For(Farm.Land.LandTemplate template) => template switch
        {
            Farm.Land.LandTemplate.HfsLot => Hfs,
            Farm.Land.LandTemplate.HfsPilotLot => HfsPilot,
            Farm.Land.LandTemplate.RasHall => Ras,
            Farm.Land.LandTemplate.PostSmoltModule => PostSmolt,
            _ => throw new ArgumentOutOfRangeException(nameof(template), template, "Unknown land template"),
        };
    }

    /// <summary>
    /// One land template (spec §2A, §3.2): its tanks, its water loop and
    /// treatment, its heat pumps and base load, and the production plan its
    /// tanks are stocked to.
    /// </summary>
    public sealed record LandTemplateCalibration
    {
        /// <summary>
        /// Grow-out tanks in one unit (unit tanks). HFS lot 12. Range: 1-24.
        /// Source: [L:R1] (Q2 2026 report) and [L:R5] (DESMI case, c.2023):
        /// Salmon Evolution phase 1 has 12 grow-out tanks.
        /// </summary>
        [Range(1, 24)]
        [Source("L:R1", "L:R5")]
        public int GrowOutTanks { get; init; }

        /// <summary>
        /// Inside diameter of a grow-out tank (unit m). HFS 28. Range: 8-40.
        /// Source: [L:R3] (FishFarmingExpert 2021): tanks 28 m across and 13 m high.
        /// </summary>
        [Range(8, 40)]
        [Source("L:R3")]
        public double GrowOutDiameterM { get; init; }

        /// <summary>
        /// Pre-grow-out tanks in one unit (unit tanks). HFS lot 2. Range: 0-8.
        /// Game balance: the spec's lot (§2A) after [L:R1]'s 4 pre-grow-out tanks
        /// for phase 2's 10,100 t; F2-12 acceptance 1 (energy per kg) holds with it.
        /// </summary>
        [Range(0, 8)]
        [GameBalance("the spec's lot (§2A) after [L:R1]'s 4 pre-grow-out tanks for phase 2's 10,100 t")]
        public int PreGrowTanks { get; init; }

        /// <summary>
        /// Inside diameter of a pre-grow-out tank (unit m). HFS 16 (1,608 m³);
        /// 0 where the unit has none. Range: 0-30. Game balance: spec §3.9
        /// (1,600 m³); no published size.
        /// </summary>
        [Range(0, 30)]
        [GameBalance("spec §3.9 (1,600 m³)")]
        public double PreGrowDiameterM { get; init; }

        /// <summary>
        /// Water depth in every tank of the unit (unit m). HFS 8. Range: 3-15.
        /// Source: [L:R3] (2021): "about 8 m of water" in the 13 m high tanks,
        /// 28 m × 8 m = 4,926 m³ against the published 4,800-5,000 m³ ([L:R5], [L:R6]).
        /// </summary>
        [Range(3, 15)]
        [Source("L:R3", "L:R5", "L:R6")]
        public double WaterDepthM { get; init; }

        /// <summary>
        /// Design loop flow (unit tank volumes per day): new plus reused water
        /// through each tank at the design reuse. HFS 20. Range: 5-60.
        /// Source: [L:R5] (DESMI case, c.2023): phases 2+3 take in 16,000 L/s and
        /// circulate above 50,000 L/s over 48 tanks, 18-22 tank volumes a day
        /// depending on the tanks' mix of 5,000 and 1,600 m³ (derived).
        /// </summary>
        [Range(5, 60)]
        [Source("L:R5")]
        public double LoopVolumesPerDay { get; init; }

        /// <summary>
        /// Default reuse r (unit fraction of the loop flow that is reused). HFS
        /// 0.65. Range: 0-0.995. Source: [L:R2] (annual report 2025): "reuses
        /// ~65% of the water ... our sweet spot".
        /// </summary>
        [Range(0, 0.995)]
        [Source("L:R2")]
        public double DefaultReuse { get; init; }

        /// <summary>
        /// Reuse the CO2 strippers are sized for (unit fraction): they strip at
        /// most this share of the design loop flow; reused water above it returns
        /// unstripped. HFS 0.65. Range: 0-1. Game balance: [L:R2] says higher
        /// reuse "require[s] more extensive filtration and water treatment";
        /// the plant is built for its sweet spot. F2-12 acceptance 3 (CO2 above
        /// 15 mg/L past r 0.8) decides it.
        /// </summary>
        [Range(0, 1)]
        [GameBalance("[L:R2] says higher reuse \"require[s] more extensive filtration and water treatment\"")]
        public double StripperDesignReuse { get; init; }

        /// <summary>
        /// Share of the CO2 (and of the oxygen deficit to air saturation) one
        /// pass through the strippers removes (unit fraction). HFS 0.85.
        /// Range: 0.3-0.98. Game balance: spec §3.3 η_s; F2-12 acceptance 3
        /// (CO2 10-15 mg/L at 60 kg/m³, r 0.65) decides it.
        /// </summary>
        [Range(0.3, 0.98)]
        [GameBalance("spec §3.3 η_s")]
        public double StripEfficiency { get; init; }

        /// <summary>
        /// Treatment energy per m³ of reused water (unit kWh/m³): drum filters,
        /// degassing fans, oxygenation. HFS 0.02. Range: 0-0.2. Game balance:
        /// spec §3.9; F2-12 acceptance 1 (energy per kg) decides it.
        /// </summary>
        [Range(0, 0.2)]
        [GameBalance("spec §3.9")]
        public double TreatKWhPerM3Reused { get; init; }

        /// <summary>
        /// Biofilter nitrification capacity at full maturity and health (unit kg
        /// TAN-N per m³ of tank per day). HFS 0: the hybrid flow-through has no
        /// biofilter. Range: 0-0.2. Source: [L:R7] (SalmonBusiness 2024): HFS
        /// reuses water "until total ammonia nitrogen (TAN) levels" reach their
        /// limit; [L:R2] lists CO2 stripping and oxygenation, no biofilter.
        /// </summary>
        [Range(0, 0.2)]
        [Source("L:R7", "L:R2")]
        public double BiofilterKgTanPerM3Day { get; init; }

        /// <summary>
        /// Days a new biofilter takes from its start maturity to full capacity
        /// (unit days). RAS 60. Range: 0-180. Game balance: spec §3.3 (RAS and
        /// post-smolt 60 days).
        /// </summary>
        [Range(0, 180)]
        [GameBalance("spec §3.3 (RAS and post-smolt 60 days)")]
        public int BiofilterMaturationDays { get; init; }

        /// <summary>
        /// Electrical input capacity of the unit's heat pumps (unit MW). HFS 8:
        /// 88 MW of heat at the heat-recovery COP. Range: 0-20. Source: [V:11]
        /// (World Fishing 2025): Salmon Evolution phase 2 gets six heat pumps of
        /// 5-6 MW with seawater exchangers, over 100 MW of heat combined.
        /// </summary>
        [Range(0, 20)]
        [Source("V:11")]
        public double HeatPumpMWe { get; init; }

        /// <summary>
        /// Base load of the unit (unit kWh/day): lights, feeders, sludge
        /// handling, buildings. HFS 6,000 (250 kW). Range: 0-30,000. Game
        /// balance: spec §3.9 had 2,000 (83 kW); an itemised estimate for a
        /// 12-tank lot with 33 staff is about 250 kW: pneumatic feeding blowers
        /// ~100 kW, continuous LED light over 12 tanks ~60 kW, drum-filter
        /// backwash and sludge dewatering ~50 kW, offices, workshop and control
        /// ~40 kW. F2-12 acceptance 1 holds it.
        /// </summary>
        [Range(0, 30000)]
        [GameBalance("an itemised base load of about 250 kW for a 12-tank lot with 33 staff; spec §3.9 had 2,000 kWh/day")]
        public double BaseKWhPerDay { get; init; }

        /// <summary>
        /// Default tank temperature setpoint (unit °C). 12. Range: 6-16.
        /// Source: [L:S1] (Nofima 2023) and [L:S8] (Frontiers 2024): keep at or
        /// under 12 °C against early maturation; [L:R6] rearing target 12-14 °C.
        /// </summary>
        [Range(6, 16)]
        [Source("L:S1", "L:S8", "L:R6")]
        public double DefaultSetpointC { get; init; }

        /// <summary>
        /// Highest density a stocking may be projected to reach (unit kg/m³).
        /// HFS 85. Range: 20-150. Source: [L:R2] (2026): HFS density 7 (min) to
        /// 85 (max) kg/m³; post-smolt [L:S1] (Nofima 2023): kept under 80.
        /// </summary>
        [Range(20, 150)]
        [Source("L:R2", "L:S1")]
        public double MaxDensityKgM3 { get; init; }

        /// <summary>
        /// Peak density the default plan stocks a tank for (unit kg/m³). HFS 80.
        /// Range: 20-150. Game balance: a 5 kg/m³ margin under
        /// <see cref="MaxDensityKgM3"/>, so the plan stocks and is not refused.
        /// </summary>
        [Range(20, 150)]
        [GameBalance("a 5 kg/m³ margin under MaxDensityKgM3, so the plan stocks and is not refused")]
        public double PlanPeakDensityKgM3 { get; init; }

        /// <summary>
        /// Weight the default plan stocks (unit kg live weight). HFS 0.4.
        /// Range: 0.05-2. Source: [L:R1] (Q2 2026): Salmon Evolution's new plan
        /// stocks at about 400 g.
        /// </summary>
        [Range(0.05, 2)]
        [Source("L:R1")]
        public double PlanReleaseWeightKg { get; init; }

        /// <summary>
        /// Weight the default plan harvests at (unit kg live weight). HFS 5.2.
        /// Range: 0.1-8. Source: [L:R1] (Q2 2026): harvest at about 5.2 kg LW
        /// after 8-9 months.
        /// </summary>
        [Range(0.1, 8)]
        [Source("L:R1")]
        public double PlanHarvestWeightKg { get; init; }

        /// <summary>
        /// Nameplate production of one unit (unit t HOG per year). HFS 7,900.
        /// Range: 0-30,000. Source: [L:R8] (Q4 2022 results): phase 1 is
        /// designed for 7,900 t HOG a year.
        /// </summary>
        [Range(0, 30000)]
        [Source("L:R8")]
        public double NameplateTonnesHogPerYear { get; init; }

        /// <summary>
        /// Staff one unit employs (unit FTE). HFS 33. Range: 0-200. Source:
        /// [L:R2] (annual report 2025): 33 FTE in the parent company that runs
        /// phase 1 (89 in the group with the smolt site and head office).
        /// </summary>
        [Range(0, 200)]
        [Source("L:R2")]
        public int Staff { get; init; }

        /// <summary>Total tanks in one unit.</summary>
        [JsonIgnore]
        public int TankCount => GrowOutTanks + PreGrowTanks;

        /// <summary>The full HFS lot (spec §2A, §3.9).</summary>
        public static LandTemplateCalibration HfsLot { get; } = new()
        {
            GrowOutTanks = 12,
            GrowOutDiameterM = 28.0,
            PreGrowTanks = 2,
            PreGrowDiameterM = 16.0,
            WaterDepthM = 8.0,
            LoopVolumesPerDay = 20.0,
            DefaultReuse = 0.65,
            StripperDesignReuse = 0.65,
            StripEfficiency = 0.85,
            TreatKWhPerM3Reused = 0.02,
            BiofilterKgTanPerM3Day = 0.0,
            BiofilterMaturationDays = 30,
            HeatPumpMWe = 8.0,
            BaseKWhPerDay = 6_000.0,
            DefaultSetpointC = 12.0,
            MaxDensityKgM3 = 85.0,
            PlanPeakDensityKgM3 = 80.0,
            PlanReleaseWeightKg = 0.4,
            PlanHarvestWeightKg = 5.2,
            NameplateTonnesHogPerYear = 7_900.0,
            Staff = 33,
        };

        /// <summary>
        /// The HFS pilot lot (spec §2A: "Pilot lot (4 + 1 tanks)"): a third of a
        /// lot's grow-out tanks; heat pumps, base load, nameplate and staff scale
        /// with the tank volume (game balance).
        /// </summary>
        public static LandTemplateCalibration HfsPilotLot { get; } = HfsLot with
        {
            GrowOutTanks = 4,
            PreGrowTanks = 1,
            HeatPumpMWe = 3.0,
            BaseKWhPerDay = 2_000.0,
            NameplateTonnesHogPerYear = 2_600.0,
            Staff = 15,
        };

        /// <summary>
        /// The RAS hall (spec §2A: 16 × 2,500 m³ at 120-140 kg/m³/yr [L:R6]),
        /// r 0.97 (the code's RAS default, [L:R1] RAS 95-99 %), a fixed-bed
        /// biofilter maturing in 60 days.
        /// </summary>
        public static LandTemplateCalibration RasHall { get; } = new()
        {
            GrowOutTanks = 16,
            GrowOutDiameterM = 20.0,
            PreGrowTanks = 0,
            PreGrowDiameterM = 0.0,
            WaterDepthM = 8.0,
            LoopVolumesPerDay = 24.0,
            DefaultReuse = 0.97,
            StripperDesignReuse = 0.97,
            StripEfficiency = 0.90,
            TreatKWhPerM3Reused = 0.05,
            BiofilterKgTanPerM3Day = 0.02,
            BiofilterMaturationDays = 60,
            HeatPumpMWe = 1.5,
            BaseKWhPerDay = 8_000.0,
            DefaultSetpointC = 12.0,
            MaxDensityKgM3 = 85.0,
            PlanPeakDensityKgM3 = 80.0,
            PlanReleaseWeightKg = 0.15,
            PlanHarvestWeightKg = 4.5,
            NameplateTonnesHogPerYear = 4_300.0,
            Staff = 35,
        };

        /// <summary>
        /// The post-smolt module (spec §2A: 2 × Ø16 m tanks, 3,200 m³, at most
        /// 80 kg/m³ [L:S1]; 480 t a year), seawater RAS with a moving-bed
        /// biofilter.
        /// </summary>
        public static LandTemplateCalibration PostSmoltModule { get; } = new()
        {
            GrowOutTanks = 2,
            GrowOutDiameterM = 16.0,
            PreGrowTanks = 0,
            PreGrowDiameterM = 0.0,
            WaterDepthM = 8.0,
            LoopVolumesPerDay = 36.0,
            DefaultReuse = 0.97,
            StripperDesignReuse = 0.97,
            StripEfficiency = 0.90,
            TreatKWhPerM3Reused = 0.05,
            BiofilterKgTanPerM3Day = 0.06,
            BiofilterMaturationDays = 60,
            HeatPumpMWe = 0.3,
            BaseKWhPerDay = 1_000.0,
            DefaultSetpointC = 12.0,
            MaxDensityKgM3 = 80.0,
            PlanPeakDensityKgM3 = 75.0,
            PlanReleaseWeightKg = 0.08,
            PlanHarvestWeightKg = 0.45,
            NameplateTonnesHogPerYear = 400.0,
            Staff = 6,
        };
    }

    /// <summary>The intake water every land template draws (spec §3.3 "Water temperature").</summary>
    public sealed record LandIntakeCalibration
    {
        /// <summary>
        /// Temperature of the deep (95 m) intake water (unit °C). 10. Range: 4-14.
        /// Source: [V:10] (SalmonBusiness 2024) and [L:R7]: water below the
        /// thermocline "maintains a year-round temperature of 10°C".
        /// </summary>
        [Range(4, 14)]
        [Source("V:10", "L:R7")]
        public double DeepWaterTemperatureC { get; init; } = 10.0;

        /// <summary>
        /// Dissolved CO2 of the intake seawater (unit mg/L). 0.6. Range: 0-3.
        /// Game balance: free CO2 of air-equilibrated seawater at pH 8.0-8.1 is
        /// 10-20 µmol/kg, 0.45-0.9 mg/L (carbonate chemistry, derived).
        /// </summary>
        [Range(0, 3)]
        [GameBalance("free CO2 of air-equilibrated seawater at pH 8.0-8.1 is 10-20 µmol/kg, 0.45-0.9 mg/L (carbonate chemistry, derived)")]
        public double Co2MgL { get; init; } = 0.6;

        /// <summary>
        /// Nitrate nitrogen of the intake seawater (unit mg NO3-N/L). 0.1.
        /// Range: 0-1. Game balance: coastal winter nitrate of about 7-10 µmol/L
        /// is 0.1-0.14 mg N/L (derived).
        /// </summary>
        [Range(0, 1)]
        [GameBalance("coastal winter nitrate of about 7-10 µmol/L is 0.1-0.14 mg N/L (derived)")]
        public double No3NMgL { get; init; } = 0.1;

        /// <summary>
        /// Highest reuse a unit can run (unit fraction). 0.995. Range: 0.9-1.
        /// Game balance: zero-exchange systems are out of scope; at least 0.5 %
        /// new water keeps every mass balance finite.
        /// </summary>
        [Range(0.9, 1)]
        [GameBalance("zero-exchange systems are out of scope")]
        public double MaxReuse { get; init; } = 0.995;
    }

    /// <summary>Oxygen in the tanks (spec §3.3 "Oxygen, from metabolism").</summary>
    public sealed record LandOxygenCalibration
    {
        /// <summary>
        /// Oxygen the fish use per kg of feed eaten (unit kg O2/kg feed). 0.37.
        /// Range: 0.2-0.6. Game balance: spec §3.3, back-calculated from
        /// [L:R2]'s 0.55 kg O2 per kg LW and [L:S5]; F2-12 acceptance 2 (LOX
        /// 0.45-0.65 kg O2/kg LW) decides it.
        /// </summary>
        [Range(0.2, 0.6)]
        [GameBalance("spec §3.3, back-calculated from [L:R2]'s 0.55 kg O2 per kg LW and [L:S5]")]
        public double KgO2PerKgFeed { get; init; } = 0.37;

        /// <summary>
        /// Oxygen nitrification takes per g of TAN-N it oxidises (unit g O2/g N).
        /// 4.57. Range: 4.0-4.6. Source: stoichiometry of NH4+ + 2 O2 → NO3- +
        /// 2 H+ + H2O, 64/14 (Timmons &amp; Ebeling, Recirculating Aquaculture,
        /// 2010).
        /// </summary>
        [Range(4.0, 4.6)]
        [Source("Timmons & Ebeling 2010 (stoichiometry)")]
        public double NitrificationGO2PerGTan { get; init; } = 4.57;

        /// <summary>
        /// Dissolved oxygen the tank outlet is held at by oxygen injection (unit
        /// mg/L). 8.0: about 93 % of air saturation at 12 °C and 35 PSU. Range:
        /// 5-12. Game balance: spec §3.3 put the floor at 7.0 (80 %); Remen et
        /// al. 2016 (Aquaculture 464:582-592) found the oxygen threshold for
        /// full feed intake of post-smolts at 42-76 % saturation across
        /// temperatures, so a farm holds its outlet near saturation with a
        /// margin. F2-12 acceptance 2 (LOX 0.45-0.65 kg O2 per kg LW growth,
        /// [L:R2] 0.55) decides it: at 7.0 the new water and the strippers
        /// supply 27 % of the fish's oxygen and LOX reads 0.37 (0.75 injection).
        /// </summary>
        [Range(5, 12)]
        [GameBalance("outlet held near 93 % saturation, above the 42-76 % threshold for full feed intake (Remen et al. 2016); F2-12 acceptance 2 decides it")]
        public double TankOutletDoMgL { get; init; } = 8.0;

        /// <summary>
        /// Share of the injected oxygen that dissolves (unit fraction). 0.70.
        /// Range: 0.5-1. Game balance: spec §3.3 (0.75); between low-head
        /// oxygenators and fine-bubble diffusers in 8 m deep tanks. F2-12
        /// acceptance 2 decides it (0.75 gives 0.447 kg O2/kg LW growth).
        /// </summary>
        [Range(0.5, 1)]
        [GameBalance("between low-head oxygenators and fine-bubble diffusers in 8 m deep tanks; F2-12 acceptance 2 (LOX 0.45-0.65 kg O2/kg LW) decides it")]
        public double InjectionEfficiency { get; init; } = 0.70;

        /// <summary>
        /// Heat the fish release per kg of oxygen they use (unit kWh/kg O2).
        /// 3.77. Range: 3.5-4.0. Source: the oxycaloric equivalent 13.56 J/mg O2
        /// (Elliott &amp; Davison 1975, Oecologia 19:195-201).
        /// </summary>
        [Range(3.5, 4.0)]
        [Source("Elliott & Davison 1975")]
        public double OxycaloricKWhPerKgO2 { get; init; } = 3.77;

        /// <summary>
        /// Electricity of an on-site oxygen generator (unit kWh/kg O2). 0.8.
        /// Range: 0.3-1.5. Game balance: spec §3.6 (Scope 2 when installed).
        /// </summary>
        [Range(0.3, 1.5)]
        [GameBalance("spec §3.6 (Scope 2 when installed)")]
        public double OnSiteGeneratorKWhPerKgO2 { get; init; } = 0.8;

        /// <summary>
        /// Upstream electricity of purchased liquid oxygen (unit kWh/kg O2).
        /// 0.75. Range: 0.5-1.0. Game balance: spec §3.6 (Scope 3, off the site's meter).
        /// </summary>
        [Range(0.5, 1.0)]
        [GameBalance("spec §3.6 (Scope 3, off the site's meter)")]
        public double PurchasedLoxUpstreamKWhPerKg { get; init; } = 0.75;

        /// <summary>
        /// Price of liquid oxygen delivered (unit NOK/kg). 4.0. Range: 1-10.
        /// Source: [L:S6] (World Fishing, n.d.): ~50,000 ISK/t in Iceland, about
        /// NOK 4/kg (derived conversion).
        /// </summary>
        [Range(1, 10)]
        [Source("L:S6")]
        public double LoxPriceNokPerKg { get; init; } = 4.0;
    }

    /// <summary>Dissolved CO2 (spec §3.3 "CO₂, steady-state mass balance per tank").</summary>
    public sealed record LandCo2Calibration
    {
        /// <summary>
        /// CO2 the fish breathe out per kg of oxygen they use (unit kg CO2/kg O2).
        /// 1.375. Range: 1.0-1.4. Source: a respiratory quotient of 1 mol/mol,
        /// 44/32 by mass (Timmons &amp; Ebeling 2010).
        /// </summary>
        [Range(1.0, 1.4)]
        [Source("Timmons & Ebeling 2010 (respiratory quotient)")]
        public double Co2PerO2 { get; init; } = 1.375;

        /// <summary>
        /// CO2 up to which appetite is full (unit mg/L). 12. Range: 5-20.
        /// Source: [L:S1] (Nofima 2023): keep CO2 under 15 mg/L in brackish
        /// water; growth is already hit at 12.
        /// </summary>
        [Range(5, 20)]
        [Source("L:S1")]
        public double AppetiteFullMgL { get; init; } = 12.0;

        /// <summary>
        /// CO2 at which appetite has fallen to <see cref="AppetiteAtLimit"/>
        /// (unit mg/L). 20. Range: 12-40. Game balance: spec §3.3 slope.
        /// </summary>
        [Range(12, 40)]
        [GameBalance("spec §3.3 slope")]
        public double AppetiteLimitMgL { get; init; } = 20.0;

        /// <summary>
        /// Appetite left at <see cref="AppetiteLimitMgL"/> (unit fraction). 0.6.
        /// Range: 0-1. Game balance: spec §3.3 ("falling linearly to 0.6 at
        /// 20 mg/L"); the slope continues beyond it.
        /// </summary>
        [Range(0, 1)]
        [GameBalance("spec §3.3 (\"falling linearly to 0.6 at 20 mg/L\")")]
        public double AppetiteAtLimit { get; init; } = 0.6;
    }

    /// <summary>Ammonia, biofilter and nitrate (spec §3.3 "TAN, biofilter and nitrate").</summary>
    public sealed record LandTanCalibration
    {
        /// <summary>
        /// Total ammonia nitrogen the fish excrete per kg of feed (unit kg
        /// TAN-N/kg feed). 0.03. Range: 0.015-0.05. Game balance: spec §3.3
        /// (feed of 45 % protein holds 0.072 kg N; about 45 % leaves as TAN).
        /// </summary>
        [Range(0.015, 0.05)]
        [GameBalance("spec §3.3: feed of 45 % protein holds 0.072 kg N, about 45 % of it leaves as TAN")]
        public double KgTanPerKgFeed { get; init; } = 0.03;

        /// <summary>
        /// Share of the TAN in the water one pass through a biofilter removes,
        /// when the filter has room (unit fraction). 0.6. Range: 0.1-0.95.
        /// Game balance: spec §3.3.
        /// </summary>
        [Range(0.1, 0.95)]
        [GameBalance("spec §3.3")]
        public double PassRemovalEfficiency { get; init; } = 0.6;

        /// <summary>
        /// Maturity of a new biofilter at its start (unit fraction of capacity).
        /// 0.2. Range: 0-1. Game balance: spec §3.3.
        /// </summary>
        [Range(0, 1)]
        [GameBalance("spec §3.3")]
        public double MaturityAtStart { get; init; } = 0.2;

        /// <summary>
        /// TAN up to which appetite is full (unit mg/L). 2.0. Range: 0.5-5.
        /// Game balance: spec §3.3 ("above 2 mg/L TAN, appetite falls").
        /// </summary>
        [Range(0.5, 5)]
        [GameBalance("spec §3.3 (\"above 2 mg/L TAN, appetite falls\")")]
        public double AppetiteLimitMgL { get; init; } = 2.0;

        /// <summary>
        /// Appetite lost per mg/L of TAN above <see cref="AppetiteLimitMgL"/>
        /// (unit fraction per mg/L). 0.25. Range: 0-1. Game balance: spec §3.3
        /// leaves the slope open; 0.25 halves appetite at 4 mg/L.
        /// </summary>
        [Range(0, 1)]
        [GameBalance("spec §3.3 leaves the slope open")]
        public double AppetiteLossPerMgL { get; init; } = 0.25;
    }

    /// <summary>Stocking density on land (spec §3.3 "Density").</summary>
    public sealed record LandDensityCalibration
    {
        /// <summary>
        /// Density up to which growth is unaffected (unit kg/m³). 50. Range:
        /// 20-100. Source: [L:S2] (Nofima 2017): growth (SGR) falls from 50 kg/m³.
        /// </summary>
        [Range(20, 100)]
        [Source("L:S2")]
        public double KneeKgM3 { get; init; } = 50.0;

        /// <summary>
        /// Density at which growth has fallen to <see cref="GrowthAtReference"/>
        /// (unit kg/m³). 85. Range: 50-150. Source: [L:R2] (the HFS maximum).
        /// </summary>
        [Range(50, 150)]
        [Source("L:R2")]
        public double ReferenceKgM3 { get; init; } = 85.0;

        /// <summary>
        /// Growth multiplier at <see cref="ReferenceKgM3"/> (unit fraction). 0.85.
        /// Range: 0.5-1. Game balance: spec §3.3 shape; the line continues
        /// beyond it toward [L:S2]'s stress overload at 125 kg/m³.
        /// </summary>
        [Range(0.5, 1)]
        [GameBalance("spec §3.3 shape")]
        public double GrowthAtReference { get; init; } = 0.85;
    }

    /// <summary>Energy, built from its parts (spec §3.3 "Energy, built from its parts").</summary>
    public sealed record LandEnergyCalibration
    {
        /// <summary>
        /// Intake pump capacity of one lot (unit m³/s). 5.0. Range: 0.5-20.
        /// Source: [L:R5] (DESMI case, c.2023): 16,000 L/s of new seawater over
        /// phases 2+3's 48 tanks, 5.1 m³/s for a lot of 12 + 2 tanks (derived).
        /// </summary>
        [Range(0.5, 20)]
        [Source("L:R5")]
        public double PumpCapacityM3sPerLot { get; init; } = 5.0;

        /// <summary>
        /// Static lift from the sea to the tank water (unit m). 13. Range: 0-40.
        /// Source: [L:R13] (Hustadvika plan 201905, 2024): the pad at +3.6 m,
        /// plus 8 m of water and 1.5 m of freeboard (derived, spec §3.2).
        /// </summary>
        [Range(0, 40)]
        [Source("L:R13")]
        public double StaticLiftM { get; init; } = 13.0;

        /// <summary>
        /// Friction head of an intake line per km when it carries the lot's
        /// full pump capacity (unit m/km); it scales with the square of the
        /// flow (Darcy-Weisbach). 1.2. Range: 0-10. Game balance: spec §3.9.
        /// </summary>
        [Range(0, 10)]
        [GameBalance("spec §3.9")]
        public double FrictionMPerKmAtCapacity { get; init; } = 1.2;

        /// <summary>
        /// Head of the reuse loop: drum filter, degassing tower, oxygen cones
        /// (unit m). 4.0. Range: 1-20. Game balance: spec §3.9; F2-12 acceptance
        /// 1 decides it.
        /// </summary>
        [Range(1, 20)]
        [GameBalance("spec §3.9")]
        public double LoopHeadM { get; init; } = 4.0;

        /// <summary>
        /// Combined pump and motor efficiency, wire to water (unit fraction).
        /// 0.75. Range: 0.5-0.9. Game balance: spec §3.3 η.
        /// </summary>
        [Range(0.5, 0.9)]
        [GameBalance("spec §3.3 η")]
        public double PumpEfficiency { get; init; } = 0.75;

        /// <summary>
        /// Electricity of UV disinfection per m³ of intake water (unit kWh/m³).
        /// 0.026. Range: 0-0.15. Source: US DOE/LBNL Wastewater Tipsheet #6
        /// (December 2021): low-pressure UV systems use 100-250 kWh per million
        /// gallons; clear deep seawater takes the low end (0.026 kWh/m³). New
        /// land plants must disinfect their intake ([L:L2], 2025).
        /// </summary>
        [Range(0, 0.15)]
        [Source("US DOE/LBNL Wastewater Tipsheet #6 (2021)", "L:L2")]
        public double IntakeUvKWhPerM3 { get; init; } = 0.026;

        /// <summary>
        /// Heating COP without heat recovery (unit kWh heat/kWh). 3.5. Range:
        /// 1-8. Game balance: spec §3.3.
        /// </summary>
        [Range(1, 8)]
        [GameBalance("spec §3.3")]
        public double Cop { get; init; } = 3.5;

        /// <summary>
        /// Heating COP with heat recovery from the outlet water (unit kWh
        /// heat/kWh). 11. Range: 6-30. Source: [V:11] (World Fishing 2025): the
        /// heat pumps at Indre Harøy have a COP "exceeding 12"; 11 is the
        /// seasonal value below that design point (spec §3.3).
        /// </summary>
        [Range(6, 30)]
        [Source("V:11")]
        public double CopWithHeatRecovery { get; init; } = 11.0;

        /// <summary>
        /// COP of cooling the tank water when the intake runs warmer than the
        /// setpoint (unit kWh heat removed/kWh). 3.5. Range: 1-8. Game balance:
        /// a chiller rejecting heat to the sea; heat recovery does not help.
        /// </summary>
        [Range(1, 8)]
        [GameBalance("a chiller rejecting heat to the sea")]
        public double CoolingCop { get; init; } = 3.5;

        /// <summary>
        /// How far the tanks may run above the setpoint before the chillers
        /// start (unit K): the heat pumps heat to the setpoint, the chillers cool
        /// only what lies above setpoint + this band. 0.5. Range: 0-2. Game
        /// balance: no plant runs chillers to take a few hundredths of a degree
        /// of pump and fish heat out of a flow-through loop; growth is still
        /// rising at 12.5 °C (TGC optimum 13 °C, GrowthCalibration).
        /// </summary>
        [Range(0, 2)]
        [GameBalance("no plant runs chillers to take a few hundredths of a degree of pump and fish heat out of a flow-through loop")]
        public double CoolingDeadbandK { get; init; } = 0.5;

        /// <summary>
        /// Electricity price the projections and sweeps cost energy at (unit
        /// NOK/kWh). 0.80. Range: 0.3-3. Source: [L:E1] (SSB 2026): a sensible
        /// game range is NOK 0.66-1.0/kWh with grid and tax; the midpoint. The
        /// wiring stage replaces it with the area's power price (S2-02).
        /// </summary>
        [Range(0.3, 3)]
        [Source("L:E1")]
        public double PowerPriceNokPerKWh { get; init; } = 0.80;

        /// <summary>
        /// Heat to warm 1 m³ of seawater by 1 K (unit kWh/(m³·K)). 1.14. Range:
        /// 1.0-1.2. Source: physics: seawater of 35 PSU has c_p ≈ 3.99 kJ/(kg·K)
        /// and ρ ≈ 1,027 kg/m³ (TEOS-10), 4.10 MJ/(m³·K).
        /// </summary>
        [Range(1.0, 1.2)]
        [Source("TEOS-10 (seawater physics)")]
        public double HeatKWhPerM3PerK { get; init; } = 1.14;

        /// <summary>
        /// Density of seawater for the pumping head (unit kg/m³). 1,027. Range:
        /// 1,000-1,030. Source: physics (TEOS-10, 35 PSU, 10 °C).
        /// </summary>
        [Range(1000, 1030)]
        [Source("TEOS-10 (seawater physics)")]
        public double SeawaterDensityKgM3 { get; init; } = 1_027.0;

        /// <summary>
        /// Share of the reuse-loop pumping and UV energy that ends as heat in the
        /// tank water (unit fraction). 0.85. Range: 0-1. Game balance: pump work
        /// dissipates by friction in the water ([L:B3]: RAS halls need chillers).
        /// </summary>
        [Range(0, 1)]
        [GameBalance("pump work dissipates by friction in the water ([L:B3]: RAS halls need chillers)")]
        public double PumpHeatToWaterShare { get; init; } = 0.85;
    }

    /// <summary>
    /// The production plan the stocking check and the projections run (spec
    /// §3.3 "Density", F2-15 refines biology and competence).
    /// </summary>
    public sealed record LandProductionCalibration
    {
        /// <summary>
        /// Biological FCR of land fish of 2 kg eating to appetite (unit kg
        /// feed/kg gain); the growth law's size factor scales it. 1.0. Range:
        /// 0.8-1.4. Source: [L:A3] (Andfjord 2026, accumulated FCR 1.02) and
        /// [L:R2] (9,300 t feed for 7,900 t HOG, 1.03-1.06 per kg LW).
        /// </summary>
        [Range(0.8, 1.4)]
        [Source("L:A3", "L:R2")]
        public double BiologicalFcrAt2Kg { get; init; } = 1.0;

        /// <summary>
        /// Share of the stocked fish alive at harvest (unit fraction per cycle).
        /// 0.97. Range: 0.8-1. Source: [L:R2] (2026): survival about 97 %
        /// (planned mortality 3-5 %).
        /// </summary>
        [Range(0.8, 1)]
        [Source("L:R2")]
        public double CycleSurvival { get; init; } = 0.97;

        /// <summary>
        /// Days a tank stands empty between a harvest and the next stocking
        /// (unit days). 7. Range: 0-60. Game balance: wash and disinfect.
        /// </summary>
        [Range(0, 60)]
        [GameBalance("wash and disinfect")]
        public int FallowDays { get; init; } = 7;

        /// <summary>
        /// Gutted (HOG) weight per kg live weight (unit fraction). 0.83. Range:
        /// 0.8-0.9. Source: [L:R1] (Q2 2026): 3.5 kg HOG = 4.2 kg LW.
        /// </summary>
        [Range(0.8, 0.9)]
        [Source("L:R1")]
        public double HogPerLw { get; init; } = 0.83;
    }

    #endregion
}
