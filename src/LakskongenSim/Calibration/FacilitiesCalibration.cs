using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace LakskongenSim.Calibration
{
    #region F2-04 facilities: the shared section (facilities_common.json)

    /// <summary>
    /// Calibration section <c>facilities</c> (roadmap F2-04, docs/FARM_TYPES_SPEC.md
    /// §3.9). It is assembled from include files: <c>calibration.json</c> lists
    /// them under <c>facilitiesIncludes</c>, and the loader
    /// (<see cref="CalibrationIncludes"/>) puts each file
    /// <c>calibration/facilities_&lt;name&gt;.json</c> into this section as
    /// <c>facilities.&lt;name&gt;</c> (snake case to camel case:
    /// <c>closed_sea</c> → <c>closedSea</c>). Every lane owns its own file and
    /// its own C# record: sim·core owns <c>facilities_common.json</c>
    /// (<see cref="Common"/>), sim·land <c>facilities_land.json</c>, sim·sea
    /// <c>facilities_offshore.json</c> and <c>facilities_closed_sea.json</c>.
    /// A lane adds its section to this partial record from its own file
    /// (<c>public sealed partial record FacilitiesCalibration { [JsonPropertyName("land")] public LandCalibration Land { get; init; } = new(); }</c>).
    /// <para>
    /// Every leaf under this section carries a <c>[Range]</c> and exactly one
    /// of <see cref="SourceAttribute"/> or <see cref="GameBalanceAttribute"/>;
    /// <c>CalibrationGuardTests</c> walks the section by reflection and fails
    /// on a leaf without them or with a value outside its range.
    /// </para>
    /// </summary>
    public sealed partial record FacilitiesCalibration
    {
        /// <summary>
        /// The section every farm type shares (sim·core, <c>facilities_common.json</c>):
        /// depreciation of F2 units, construction-project overruns and the
        /// farm-type catalogue's reference values.
        /// </summary>
        [JsonPropertyName("common")]
        public FacilityCommonCalibration Common { get; init; } = new();
    }

    /// <summary>The shared facility section (<c>facilities_common.json</c>).</summary>
    public sealed record FacilityCommonCalibration
    {
        /// <summary>Straight-line lives of F2 units (spec §3.6).</summary>
        public FacilityDepreciationCalibration Depreciation { get; init; } = new();

        /// <summary>Cost and schedule risk of a construction project (spec §3.2 <c>ConstructionProject</c>).</summary>
        public FacilityConstructionCalibration Construction { get; init; } = new();

        /// <summary>The reference values behind the farm-type comparison card (spec §2A-2C).</summary>
        public FarmTypeCatalogCalibration Catalog { get; init; } = new();
    }

    /// <summary>
    /// Depreciation of F2 units (spec §3.6): a non-cash P&amp;L line,
    /// <c>FacilityDepreciation</c>, part of the corporate tax base and outside
    /// the resource-rent base. Straight line from the day a unit is commissioned;
    /// sea sites and processing plants are deliberately left out (§8).
    /// </summary>
    public sealed record FacilityDepreciationCalibration
    {
        /// <summary>
        /// Life of a land site (unit years): 20. Game balance: spec §3.6's
        /// straight-line stand-in for the declining-balance groups of the
        /// Norwegian tax code (skatteloven § 14-43: buildings and plants 4 % a
        /// year, machinery 20 %); a land site is mostly concrete and pipework.
        /// </summary>
        [Range(5, 50)]
        [GameBalance("spec §3.6: straight-line stand-in for the declining-balance groups of skatteloven § 14-43 (plants 4 %, machinery 20 % a year)")]
        public int LandLifeYears { get; init; } = 20;

        /// <summary>
        /// Life of an offshore unit's steel (unit years): 25. Game balance: spec
        /// §3.6; OF1-class units are classed for a 25-year design life (the
        /// offshore dossier's structure notes) and the tax code's 4 % plant
        /// group spans the same order.
        /// </summary>
        [Range(5, 50)]
        [GameBalance("spec §3.6: an offshore unit's steel over a 25-year design life")]
        public int OffshoreLifeYears { get; init; } = 25;
    }

    /// <summary>
    /// Cost and schedule risk of one construction phase (spec §3.2, §3.7): at
    /// a phase's start the <c>facility_construction</c> stream gives two
    /// uniforms, read by inverse CDF as a lognormal cost factor and a lognormal
    /// duration factor, each set by its median and 90th percentile. A turnkey
    /// contract pays a premium up front and carries no cost overrun.
    /// </summary>
    public sealed record FacilityConstructionCalibration
    {
        /// <summary>
        /// Median cost overrun of a managed phase (unit fraction of its budget):
        /// 0.07. Game balance: anchored on the first OF1-class unit, 720 M at
        /// start-up plus 157 M of first-cycle fixes (+22 %, O:R4), and on land
        /// builds whose real cost came in far above early assumptions (L:C1).
        /// </summary>
        [Range(0.0, 1.0)]
        [GameBalance("median of a lognormal cost factor; anchored on O:R4 (+22 % on the first OF1-class unit) and L:C1")]
        public double OverrunMedian { get; init; } = 0.07;

        /// <summary>
        /// 90th-percentile cost overrun of a managed phase (unit fraction): 0.25.
        /// Game balance: the first OF1-class unit's +22 % (O:R4) sits just below it.
        /// </summary>
        [Range(0.0, 2.0)]
        [GameBalance("90th percentile of the lognormal cost factor; O:R4's +22 % sits just below it")]
        public double OverrunP90 { get; init; } = 0.25;

        /// <summary>
        /// Median schedule slip of a phase (unit fraction of its planned days):
        /// 0.05. Game balance: land builds that changed contractor and slipped
        /// (L:A10) and an offshore unit delivered on its plan (O:R1).
        /// </summary>
        [Range(0.0, 1.0)]
        [GameBalance("median of a lognormal duration factor; L:A10 slipped, O:R1 delivered on plan")]
        public double DurationSlipMedian { get; init; } = 0.05;

        /// <summary>
        /// 90th-percentile schedule slip of a phase (unit fraction): 0.35.
        /// Game balance: a phase that runs a third over its plan in one game in ten.
        /// </summary>
        [Range(0.0, 2.0)]
        [GameBalance("90th percentile of the lognormal duration factor")]
        public double DurationSlipP90 { get; init; } = 0.35;

        /// <summary>
        /// Premium of a turnkey contract over a managed one (unit fraction of the
        /// budget): 0.12. Game balance (spec §3.9 <c>turnkeyPremium</c>): the
        /// contractor carries the cost overrun, so the turnkey price includes it.
        /// </summary>
        [Range(0.0, 0.5)]
        [GameBalance("spec §3.9: the contractor prices in the cost risk a managed build carries")]
        public double TurnkeyPremium { get; init; } = 0.12;
    }

    /// <summary>
    /// Reference values of the farm-type comparison card (spec §2A-2C, §6.2):
    /// the inputs every card value is computed from (<c>Farm/FarmTypeCatalog.cs</c>),
    /// each with its source key. After F2-17 the card shows measured probe
    /// medians instead; until then these are the honest reference values.
    /// </summary>
    public sealed record FarmTypeCatalogCalibration
    {
        /// <summary>
        /// Yearly HOG production of one tonne of MTB (unit t HOG per t MTB per
        /// year): 1.2. Game balance: spec §2's normalisation rule, an 18-month
        /// cycle at 25 kg/m³; it puts licence-bound and land capacity on one axis.
        /// </summary>
        [Range(0.5, 2.0)]
        [GameBalance("spec §2 normalisation rule: an 18-month cycle at 25 kg/m³")]
        public double HogTonnesPerMtbTonneYear { get; init; } = 1.2;

        /// <summary>
        /// Fee to convert one development licence to a commercial licence (unit
        /// NOK): 13,804,348. Source V:2: Fiskeridirektoratet decision 25/17287
        /// (13.03.2026), NOK 10 M of November 2015 indexed to January 2026.
        /// </summary>
        [Range(5_000_000, 50_000_000)]
        [Source("V:2")]
        public decimal ConversionFeeNokPerLicence { get; init; } = 13_804_348m;

        /// <summary>
        /// MTB of one standard licence (unit t): 780. Source: the game's S2-01a
        /// capacity model (<c>Finance/LicenceHolding.cs</c>, 945 t in Troms and
        /// Finnmark); a converted development licence carries the same 780 t (O:R4).
        /// </summary>
        [Range(500, 1000)]
        [Source("code", "O:R4")]
        public double LicenceSizeTonnesMtb { get; init; } = 780;

        /// <summary>Sjø: the open net pen ("Åpen merd").</summary>
        public OpenPenReference OpenPen { get; init; } = new();

        /// <summary>Sjø: the semi-closed or closed pen upgrade ("Semi-lukket / lukket merd").</summary>
        public ClosedPenReference ClosedPen { get; init; } = new();

        /// <summary>Land: the post-smolt module ("Postsmoltmodul").</summary>
        public PostSmoltReference PostSmolt { get; init; } = new();

        /// <summary>Land: the hybrid flow-through lot ("Landanlegg (hybrid)").</summary>
        public HfsReference Hfs { get; init; } = new();

        /// <summary>Land: the RAS hall ("Landanlegg (RAS)", stage 3).</summary>
        public RasReference Ras { get; init; } = new();

        /// <summary>Hav: the OF1-class semi-submersible ("Havmerd").</summary>
        public Of1Reference Of1 { get; init; } = new();

        /// <summary>Hav: the Havfarm-class ship ("Skipsmerd", stage 3).</summary>
        public HavfarmReference Havfarm { get; init; } = new();
    }

    /// <summary>The open net pen (spec §2A-2B, column Open).</summary>
    public sealed record OpenPenReference
    {
        /// <summary>
        /// Build cost of a sea site (unit NOK): 6,000,000. Source: the game's
        /// registry, <c>infrastructure.seaSiteBuildCostNOK</c>.
        /// </summary>
        [Range(1_000_000, 50_000_000)]
        [Source("code")]
        public decimal SiteCapexNok { get; init; } = 6_000_000m;

        /// <summary>
        /// MTB a built site's locality clears (unit t): 3,120, four standard
        /// licences. Source: the game's registry, <c>capacity.builtSiteLocalityMtbTonnes</c>
        /// (NFD letter 20.09.2017).
        /// </summary>
        [Range(780, 10_000)]
        [Source("code")]
        public double LocalityMtbTonnes { get; init; } = 3_120;

        /// <summary>
        /// Licence price at auction (unit NOK per t MTB): 305,000. Source V:1:
        /// regjeringen.no, results of the 2024 auction, 17,330 t for NOK 5.29 bn.
        /// </summary>
        [Range(100_000, 500_000)]
        [Source("V:1")]
        public decimal LicencePriceNokPerTonneMtb { get; init; } = 305_000m;

        /// <summary>
        /// Farming cost of the open pens (unit NOK per kg round weight): 64.68.
        /// Source O:R57: Fiskeridirektoratet key figures, 2023 average; the card
        /// divides by the registry's live-to-gutted 0.83 (≈ 78 NOK/kg HOG).
        /// </summary>
        [Range(30, 120)]
        [Source("O:R57")]
        public double CostNokPerKgRound { get; init; } = 64.68;

        /// <summary>
        /// National sea-phase mortality (unit fraction a year): 0.142. Source
        /// V:14: Veterinærinstituttet, Fiskehelserapporten 2025.
        /// </summary>
        [Range(0.0, 0.5)]
        [Source("V:14")]
        public double AnnualMortality { get; init; } = 0.142;

        /// <summary>
        /// Sea-phase mortality of the worst production area (unit fraction a
        /// year): 0.201, PO4 in 2025. Source O:R55 (Fiskehelserapporten 2025, by area).
        /// </summary>
        [Range(0.0, 0.5)]
        [Source("O:R55")]
        public double AnnualMortalityWorstArea { get; init; } = 0.201;

        /// <summary>
        /// Months at sea of a cycle the card's survival covers (unit months): 16.
        /// Game balance: inside the S2-03 band of 14-18 months from 100 g to
        /// 4.5-5.5 kg; 14.2 % a year over 16 months is ≈ 18 % (spec §2B).
        /// </summary>
        [Range(12, 24)]
        [GameBalance("inside S2-03's 14-18 months at sea; spec §2B's ≈ 18 % per 16-month cycle")]
        public double CycleMonths { get; init; } = 16;

        /// <summary>
        /// External lice pressure on an open pen (unit factor of the area's
        /// pressure): 1.0. Source: the game's lice model (the reference the
        /// other types are scaled against).
        /// </summary>
        [Range(0.0, 1.0)]
        [Source("code")]
        public double ExternalLiceFactor { get; init; } = 1.0;

        /// <summary>
        /// Electricity of an open pen (unit kWh per kg produced): 0.07. Source
        /// O:R50: a farm's grid report, 3.87 kWh/kg in a closed pen against 0.07
        /// in its open pens.
        /// </summary>
        [Range(0.0, 15.0)]
        [Source("O:R50")]
        public double EnergyKWhPerKg { get; init; } = 0.07;

        /// <summary>Days from the build order to a usable site (unit days): 0, the same day. Source: the game's build verb.</summary>
        [Range(0, 1500)]
        [Source("code")]
        public int BuildDays { get; init; }
    }

    /// <summary>The semi-closed or closed pen upgrade on a Sjø site (spec §2A-2B, column Semi/closed).</summary>
    public sealed record ClosedPenReference
    {
        /// <summary>
        /// Capex of one semi-closed pen (unit NOK): 50,000,000. Source O:R44:
        /// a semi-closed steel pen's serial-unit price (2021).
        /// </summary>
        [Range(10_000_000, 200_000_000)]
        [Source("O:R44")]
        public decimal UpgradeCapexNokPerPen { get; init; } = 50_000_000m;

        /// <summary>
        /// Production cost in the semi-closed pen (unit NOK per kg): 36.47.
        /// Source O:R44 (2021); the card scales the open pen's cost by this
        /// over <see cref="ControlPenCostNokPerKg"/>.
        /// </summary>
        [Range(10, 120)]
        [Source("O:R44")]
        public double ProductionCostNokPerKg { get; init; } = 36.47;

        /// <summary>Production cost in the same trial's open control pen (unit NOK per kg): 38.78. Source O:R44 (2021).</summary>
        [Range(10, 120)]
        [Source("O:R44")]
        public double ControlPenCostNokPerKg { get; init; } = 38.78;

        /// <summary>Mortality over the semi-closed pen's cycle (unit fraction): 0.0208, against 0.1097 in the control pen. Source O:R44 (2021).</summary>
        [Range(0.0, 0.5)]
        [Source("O:R44")]
        public double CycleMortality { get; init; } = 0.0208;

        /// <summary>
        /// Survival in a bad cycle (unit fraction): 0.92. Game balance: a pump
        /// or power failure inside a closed bag takes the oxygen within hours
        /// (spec §2C); no published loss figure.
        /// </summary>
        [Range(0.5, 1.0)]
        [GameBalance("a pump or power failure in a closed bag (spec §2C); no published loss figure")]
        public double BadCaseSurvival { get; init; } = 0.92;

        /// <summary>
        /// External lice pressure inside a semi-closed pen (unit factor): 0.15.
        /// Source: the game's containment model (<c>ContainmentTech</c>: the
        /// semi-closed rung excludes 85 % of new settlement).
        /// </summary>
        [Range(0.0, 1.0)]
        [Source("code")]
        public double ExternalLiceFactor { get; init; } = 0.15;

        /// <summary>Electricity of a closed pen (unit kWh per kg): 3.87. Source O:R50.</summary>
        [Range(0.0, 15.0)]
        [Source("O:R50")]
        public double EnergyKWhPerKg { get; init; } = 3.87;

        /// <summary>Days to refit an open pen as a closed one (unit days): 60. Game balance: spec §2A.</summary>
        [Range(0, 1500)]
        [GameBalance("spec §2A: a 60-day refit")]
        public int RefitDays { get; init; } = 60;
    }

    /// <summary>The post-smolt module (spec §2A-2B, column Post-smolt).</summary>
    public sealed record PostSmoltReference
    {
        /// <summary>
        /// Capex of one 2-tank module (unit NOK): 110,000,000. Game balance:
        /// spec §2A, ≈ 230 k NOK per t of yearly capacity; L:T1's reference
        /// plant publishes no price.
        /// </summary>
        [Range(20_000_000, 500_000_000)]
        [GameBalance("spec §2A: ≈ 230 k NOK per t of yearly post-smolt capacity; no published module price")]
        public decimal ModuleCapexNok { get; init; } = 110_000_000m;

        /// <summary>Water volume of one module (unit m³): 3,200, two Ø16 m tanks. Game balance: spec §2A.</summary>
        [Range(500, 20_000)]
        [GameBalance("spec §2A: two Ø16 m tanks of 1,600 m³")]
        public double ModuleVolumeM3 { get; init; } = 3_200;

        /// <summary>
        /// Production index (unit kg of post-smolt per m³ a year): 150. Game
        /// balance: spec §2A, above early grow-out RAS (L:R6, 120-140) because
        /// post-smolt turns over faster.
        /// </summary>
        [Range(50, 400)]
        [GameBalance("spec §2A; above L:R6's 120-140 for grow-out because post-smolt turns over faster")]
        public double ProductionIndexKgPerM3Year { get; init; } = 150;

        /// <summary>Survival through the post-smolt phase (unit fraction): 0.97. Source: the game's post-smolt facility (<c>PostSmoltFacility.SurvivalRate</c>).</summary>
        [Range(0.5, 1.0)]
        [Source("code")]
        public double Survival { get; init; } = 0.97;

        /// <summary>
        /// Survival in a bad batch (unit fraction): 0.90. Game balance: H₂S in a
        /// seawater loop (V:6, V:8) or desmoltification; one H₂S event killed 250 t (L:B6).
        /// </summary>
        [Range(0.5, 1.0)]
        [GameBalance("H₂S in a seawater loop (V:6, V:8) or desmoltification; L:B6 lost 250 t in one event")]
        public double BadCaseSurvival { get; init; } = 0.90;

        /// <summary>Electricity (unit kWh per kg): 4.4, the middle of 3.4-5.4. Source L:T1, L:S3.</summary>
        [Range(0.0, 15.0)]
        [Source("L:T1", "L:S3")]
        public double EnergyKWhPerKg { get; init; } = 4.4;

        /// <summary>Days to build a module (unit days): 548. Source: the game's post-smolt facility (<c>PostSmoltFacility.ConstructionDays</c>).</summary>
        [Range(0, 1500)]
        [Source("code")]
        public int ConstructionDays { get; init; } = 548;

        /// <summary>Days for a new biofilter to mature before first fish (unit days): 45. Game balance: spec §2A.</summary>
        [Range(0, 365)]
        [GameBalance("spec §2A: 45-day biofilter maturation")]
        public int BiofilterMaturationDays { get; init; } = 45;
    }

    /// <summary>The hybrid flow-through land lot (spec §2A-2B, column HFS).</summary>
    public sealed record HfsReference
    {
        /// <summary>
        /// Capex (unit NOK per t of yearly HOG capacity): 262,000. Source L:R1
        /// (Q2 2026 report; derived in the land dossier from the phase-2 budget
        /// and capacity): 2.07 bn NOK per full lot of 7,900 t.
        /// </summary>
        [Range(100_000, 600_000)]
        [Source("L:R1")]
        public decimal CapexNokPerTonneHog { get; init; } = 262_000m;

        /// <summary>Yearly HOG capacity of a full lot (unit t): 7,900. Source L:R8 (Q4 2022 report).</summary>
        [Range(1_000, 20_000)]
        [Source("L:R8")]
        public double LotCapacityTonnesHog { get; init; } = 7_900;

        /// <summary>
        /// Capex of the pilot lot (4 grow-out + 1 pre-grow-out tank), the land
        /// family's entry rung (unit NOK): 900,000,000, expandable to a full lot
        /// for 1.25 bn more. Game balance: spec §2A.
        /// </summary>
        [Range(100_000_000, 3_000_000_000)]
        [GameBalance("spec §2A: the pilot lot, the land family's entry rung")]
        public decimal PilotLotCapexNok { get; init; } = 900_000_000m;

        /// <summary>
        /// Farming cost (unit NOK per kg HOG): 70.7 at about half the capacity
        /// in use. Source L:R1 (Q2 2026); the target is 60-65 by mid-2027 (L:R9).
        /// </summary>
        [Range(30, 150)]
        [Source("L:R1")]
        public double CostNokPerKgHog { get; init; } = 70.7;

        /// <summary>Survival per cycle (unit fraction): 0.97. Source L:R2 (annual report 2025, planned mortality 3-5 %).</summary>
        [Range(0.5, 1.0)]
        [Source("L:R2")]
        public double Survival { get; init; } = 0.97;

        /// <summary>
        /// Survival in a bad cycle (unit fraction): 0.90. Game balance: a grid
        /// outage takes the oxygen within 40 minutes (L:F2) and single events
        /// have killed 250-500 t of a plant's stock (L:B1, L:B6).
        /// </summary>
        [Range(0.5, 1.0)]
        [GameBalance("a grid outage (L:F2) or a single event of the kind that killed 250-500 t (L:B1, L:B6)")]
        public double BadCaseSurvival { get; init; } = 0.90;

        /// <summary>Electricity (unit kWh per kg live weight): 6.55, the middle of 6.3-6.8. Source L:R2.</summary>
        [Range(0.0, 15.0)]
        [Source("L:R2")]
        public double EnergyKWhPerKg { get; init; } = 6.55;

        /// <summary>External lice pressure (unit factor): 0, intake water is disinfected. Source L:L2.</summary>
        [Range(0.0, 1.0)]
        [Source("L:L2")]
        public double ExternalLiceFactor { get; init; }

        /// <summary>Days from the investment decision to a complete lot (unit days): 910, about 30 months. Source L:R1.</summary>
        [Range(0, 1500)]
        [Source("L:R1")]
        public int BuildDays { get; init; } = 910;
    }

    /// <summary>The RAS hall, stage 3 (spec §2A-2B, column RAS).</summary>
    public sealed record RasReference
    {
        /// <summary>Capex of built land plants (unit USD per t of yearly capacity): 30,000, a floor ("above USD 30 k/t"). Source L:C1 (2025).</summary>
        [Range(10_000, 80_000)]
        [Source("L:C1")]
        public decimal CapexUsdPerTonne { get; init; } = 30_000m;

        /// <summary>Exchange rate for the capex (unit NOK per USD): 10.7. Game balance: spec §2A.</summary>
        [Range(7.0, 14.0)]
        [GameBalance("spec §2A: FX for the L:C1 capex")]
        public decimal NokPerUsd { get; init; } = 10.7m;

        /// <summary>Grow-out tanks in one hall (unit tanks): 16. Game balance: spec §2A.</summary>
        [Range(1, 64)]
        [GameBalance("spec §2A: one hall of 16 tanks")]
        public int HallTanks { get; init; } = 16;

        /// <summary>Volume of one RAS tank (unit m³): 2,500. Game balance: spec §2A.</summary>
        [Range(500, 10_000)]
        [GameBalance("spec §2A: 2,500 m³ tanks")]
        public double TankVolumeM3 { get; init; } = 2_500;

        /// <summary>Production index (unit kg live per m³ a year): 130, the middle of early RAS's 120-140. Source L:R6 (2024).</summary>
        [Range(50, 250)]
        [Source("L:R6")]
        public double ProductionIndexKgPerM3Year { get; init; } = 130;

        /// <summary>Cost premium over a hybrid flow-through lot (unit fraction): 0.10, the middle of +5-15 %. Game balance: spec §2A.</summary>
        [Range(0.0, 0.5)]
        [GameBalance("spec §2A: HFS +5-15 %")]
        public double CostPremiumOverHfs { get; init; } = 0.10;

        /// <summary>Survival per cycle (unit fraction): 0.98, inside 97-99.7 %. Source L:P1, L:P2, L:B5.</summary>
        [Range(0.5, 1.0)]
        [Source("L:P1", "L:P2", "L:B5")]
        public double Survival { get; init; } = 0.98;

        /// <summary>
        /// Survival in a bad cycle (unit fraction): 0.88. Game balance: one
        /// H₂S or design failure killed 250-500 t at once (L:B1, L:B6), about a
        /// tenth of a hall's yearly output.
        /// </summary>
        [Range(0.5, 1.0)]
        [GameBalance("one H₂S or design failure of the kind that killed 250-500 t at once (L:B1, L:B6)")]
        public double BadCaseSurvival { get; init; } = 0.88;

        /// <summary>Electricity (unit kWh per kg): 7.0, inside 4-10. Source L:R7, L:S3, L:S4.</summary>
        [Range(0.0, 15.0)]
        [Source("L:R7", "L:S3", "L:S4")]
        public double EnergyKWhPerKg { get; init; } = 7.0;

        /// <summary>External lice pressure (unit factor): 0. Source L:L2.</summary>
        [Range(0.0, 1.0)]
        [Source("L:L2")]
        public double ExternalLiceFactor { get; init; }

        /// <summary>Days to build a hall (unit days): 913, 30 months. Game balance: spec §2A.</summary>
        [Range(0, 1500)]
        [GameBalance("spec §2A: 30 months")]
        public int ConstructionDays { get; init; } = 913;

        /// <summary>Days for the biofilter to mature (unit days): 60. Game balance: spec §2A.</summary>
        [Range(0, 365)]
        [GameBalance("spec §2A: 60-day biofilter maturation")]
        public int BiofilterMaturationDays { get; init; } = 60;
    }

    /// <summary>The OF1-class semi-submersible (spec §2A-2B, column OF1).</summary>
    public sealed record Of1Reference
    {
        /// <summary>Capex (unit NOK): 877,000,000, the 720 M contract plus 157 M of first-cycle fixes. Source O:R4 (2020).</summary>
        [Range(300_000_000, 3_000_000_000)]
        [Source("O:R4")]
        public decimal UnitCapexNok { get; init; } = 877_000_000m;

        /// <summary>Development licences bound to the unit (unit licences): 8, 6,240 t MTB. Source O:R4.</summary>
        [Range(1, 30)]
        [Source("O:R4")]
        public int Licences { get; init; } = 8;

        /// <summary>
        /// Farming cost against the coast (unit factor): 1.3, a pilot unit's
        /// 56 NOK/kg against about 43 on the coast. Source O:R37 (Menon 131/2023).
        /// </summary>
        [Range(0.5, 3.0)]
        [Source("O:R37")]
        public double CostRatioToCoastal { get; init; } = 1.3;

        /// <summary>Survival when the unit works (unit fraction): 0.93, 7 % total mortality in the first generation. Source O:R3.</summary>
        [Range(0.5, 1.0)]
        [Source("O:R3")]
        public double Survival { get; init; } = 0.93;

        /// <summary>Survival when the biology fails (unit fraction): 0.81, the first Havfarm-class generation. Source O:R26.</summary>
        [Range(0.5, 1.0)]
        [Source("O:R26")]
        public double BadCaseSurvival { get; init; } = 0.81;

        /// <summary>
        /// External lice pressure (unit factor of the area's): 0.12. Game
        /// balance: spec §2B's 0.10-0.15, fitted to 0.01-0.06 adult females and
        /// no treatment in two cycles (O:R7, O:R8, O:R11); off_1's plot value.
        /// </summary>
        [Range(0.0, 1.0)]
        [GameBalance("spec §2B: 0.10-0.15 fitted to O:R7, O:R8, O:R11 (no treatment in two cycles)")]
        public double ExternalLiceFactor { get; init; } = 0.12;

        /// <summary>Days at the yard (unit days): 548, 18 months. Source O:R1, O:R33.</summary>
        [Range(0, 1500)]
        [Source("O:R1", "O:R33")]
        public int YardDays { get; init; } = 548;

        /// <summary>Days of the voyage to the site (unit days): 77, eleven weeks. Game balance: spec §2A after a 2017 trade-press report of the delivery voyage.</summary>
        [Range(0, 365)]
        [GameBalance("spec §2A: an 11-week delivery voyage (trade press, 2017)")]
        public int VoyageDays { get; init; } = 77;

        /// <summary>Days to moor and certify on site (unit days): 45. Game balance: spec §2A.</summary>
        [Range(0, 365)]
        [GameBalance("spec §2A: mooring and certificate ≈ 45 d")]
        public int MooringAndCertificationDays { get; init; } = 45;

        /// <summary>
        /// The company's share of a consortium unit, the Hav family's entry rung
        /// (unit fraction): 0.35. Game balance: spec §2C ("Consortium entry: 35 % equity").
        /// </summary>
        [Range(0.1, 1.0)]
        [GameBalance("spec §2C: consortium entry at 35 % equity")]
        public double ConsortiumEquityShare { get; init; } = 0.35;

        /// <summary>
        /// Escape-free days before the development-licence round opens to the
        /// company (unit days): 365. Game balance: spec §2C ("12 escape-free months").
        /// </summary>
        [Range(0, 1095)]
        [GameBalance("spec §2C: the round opens after 12 escape-free months")]
        public int RoundEscapeFreeDays { get; init; } = 365;
    }

    /// <summary>The Havfarm-class ship, stage 3 (spec §2A-2B, column Havfarm).</summary>
    public sealed record HavfarmReference
    {
        /// <summary>Capex (unit NOK): about 1,000,000,000. Source O:R26 (2021).</summary>
        [Range(300_000_000, 3_000_000_000)]
        [Source("O:R26")]
        public decimal UnitCapexNok { get; init; } = 1_000_000_000m;

        /// <summary>Development licences converted (unit licences): 13. Source O:R22 (2022).</summary>
        [Range(1, 30)]
        [Source("O:R22")]
        public int Licences { get; init; } = 13;

        /// <summary>MTB of the unit (unit t): 10,000 (13 × 788 = 10,244). Source O:R22, O:R23.</summary>
        [Range(1_000, 30_000)]
        [Source("O:R22", "O:R23")]
        public double MtbTonnes { get; init; } = 10_000;

        /// <summary>Farming cost against the coast (unit factor): 1.3, as OF1-class plus shore power and silage. Source O:R37.</summary>
        [Range(0.5, 3.0)]
        [Source("O:R37")]
        public double CostRatioToCoastal { get; init; } = 1.3;

        /// <summary>Survival of the second generation (unit fraction): 0.972. Source O:R22.</summary>
        [Range(0.5, 1.0)]
        [Source("O:R22")]
        public double Survival { get; init; } = 0.972;

        /// <summary>Survival of the first generation (unit fraction): 0.81, winter sores in mixed smolt. Source O:R26.</summary>
        [Range(0.5, 1.0)]
        [Source("O:R26")]
        public double BadCaseSurvival { get; init; } = 0.81;

        /// <summary>
        /// External lice pressure (unit factor): 0.30. Game balance: spec §2B,
        /// fitted to a 0.49 adult-female peak that needed treatment (O:R22).
        /// </summary>
        [Range(0.0, 1.0)]
        [GameBalance("spec §2B: fitted to O:R22's treated 0.49 adult-female peak")]
        public double ExternalLiceFactor { get; init; } = 0.30;

        /// <summary>Days from the order to first fish (unit days): 880, about 29 months. Game balance: the offshore dossier's §7 band (O:R22/O:R23 dates).</summary>
        [Range(0, 1500)]
        [GameBalance("offshore dossier §7: 29 months to first fish (dates in O:R22, O:R23)")]
        public int BuildDays { get; init; } = 880;
    }

    #endregion
}
