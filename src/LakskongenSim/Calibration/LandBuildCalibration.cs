using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace LakskongenSim.Calibration
{
    /// <summary>
    /// The land lane's section of the facilities calibration (F2-12 wiring,
    /// docs/FARM_TYPES_SPEC.md §3.9): <c>calibration/facilities_land.json</c>,
    /// read by F2-04's include loader into <c>facilities.land</c>.
    /// </summary>
    public sealed partial record FacilitiesCalibration
    {
        /// <summary>The land sites' physics, build and permits (<c>facilities_land.json</c>).</summary>
        [JsonPropertyName("land")]
        public LandCalibration Land { get; init; } = new();
    }

    #region F2-14 land build, permits and running costs (facilities_land.json)

    // Every leaf carries [Range] and exactly one of [Source] or [GameBalance]
    // (CalibrationGuardTests walks facilities.land by reflection) and states
    // its unit, its range and its provenance in its doc comment.

    /// <summary>
    /// What a land unit costs and how long it takes to build (roadmap F2-14,
    /// docs/FARM_TYPES_SPEC.md §2A, §3.9 <c>capex</c>/<c>build</c>, §6.3): the
    /// budget of each unit and its construction phases. The overrun and slip of
    /// a phase are the shared construction risk (<c>facilities.common.construction</c>).
    /// </summary>
    public sealed record LandBuildCalibration
    {
        /// <summary>
        /// Budget of a full HFS lot (unit NOK), before the pad's blasting:
        /// 2,070,000,000, ≈ 262,000 NOK per t of yearly HOG capacity × 7,900 t.
        /// Range: 500,000,000-5,000,000,000. Source: [L:R1] (Q2 2026 report:
        /// phase 2's cost per tonne) and [L:R8] (7,900 t HOG a lot).
        /// </summary>
        [Range(500_000_000, 5_000_000_000)]
        [Source("L:R1", "L:R8")]
        public decimal HfsLotCapexNok { get; init; } = 2_070_000_000m;

        /// <summary>
        /// Budget of the HFS pilot lot (4 + 1 tanks) (unit NOK): 900,000,000.
        /// Range: 200,000,000-2,000,000,000. Game balance: spec §2A, the land
        /// family's entry rung (the catalogue's pilot gate is the same money).
        /// </summary>
        [Range(200_000_000, 2_000_000_000)]
        [GameBalance("spec §2A: the pilot lot, the land family's entry rung")]
        public decimal HfsPilotCapexNok { get; init; } = 900_000_000m;

        /// <summary>
        /// Budget of expanding a pilot lot to a full lot (unit NOK): 1,250,000,000,
        /// so pilot and expansion cost 2.15 bn against 2.07 bn built at once.
        /// Range: 300,000,000-3,000,000,000. Game balance: spec §2A ("expandable
        /// to a full lot for +1.25 bn"); F2-14 acceptance 6 holds the sum.
        /// </summary>
        [Range(300_000_000, 3_000_000_000)]
        [GameBalance("spec §2A: a pilot lot expands to a full lot for +1.25 bn (2.15 bn in all)")]
        public decimal HfsPilotExpansionCapexNok { get; init; } = 1_250_000_000m;

        /// <summary>
        /// Budget of a post-smolt module (2 tanks) (unit NOK): 110,000,000.
        /// Range: 30,000,000-400,000,000. Game balance: spec §2A (≈ 230,000 NOK
        /// per t a year of post-smolt).
        /// </summary>
        [Range(30_000_000, 400_000_000)]
        [GameBalance("spec §2A: one 2-tank module of 480 t a year")]
        public decimal PostSmoltModuleCapexNok { get; init; } = 110_000_000m;

        /// <summary>
        /// Budget of a RAS hall (16 tanks) (unit NOK): 1,376,000,000, USD 30,000
        /// per t × NOK 10.7 per USD × 4,300 t HOG. Range: 500,000,000-4,000,000,000.
        /// Source: [L:C1] (Undercurrent 2025: built land plants above USD 30 k/t).
        /// </summary>
        [Range(500_000_000, 4_000_000_000)]
        [Source("L:C1")]
        public decimal RasHallCapexNok { get; init; } = 1_376_000_000m;

        /// <summary>
        /// Capex of heat recovery fitted to a lot after it is built (unit NOK per
        /// MW of heat-pump input): 6,000,000. Range: 1,000,000-20,000,000. Game
        /// balance: heat pumps with seawater exchangers of 5-6 MW each ([V:11]);
        /// no published price, the order of a large industrial heat pump.
        /// </summary>
        [Range(1_000_000, 20_000_000)]
        [GameBalance("heat pumps with seawater exchangers of 5-6 MW each [V:11]; no published price")]
        public decimal HeatRecoveryCapexNokPerMWe { get; init; } = 6_000_000m;

        /// <summary>
        /// Share of a unit's budget a denitrification commitment adds (unit
        /// fraction): 0.05. Range: 0-0.3. Game balance: a denitrification stage
        /// that removes half the dissolved nitrogen (spec §3.3 "Discharge").
        /// </summary>
        [Range(0, 0.3)]
        [GameBalance("a denitrification stage removing half the dissolved nitrogen (spec §3.3)")]
        public double DenitrificationCapexShare { get; init; } = 0.05;

        /// <summary>
        /// Share of a full lot's grow-out tanks the first commissioning hands
        /// over (unit fraction): 0.5; the rest come at completion. Range: 0.1-1.
        /// Game balance: [L:R1] (Q2 2026): phase 2's first tanks in April 2026,
        /// the whole phase by the end of the year.
        /// </summary>
        [Range(0.1, 1)]
        [GameBalance("[L:R1]: phase 2's first tanks came in April 2026, the rest by the end of the year")]
        public double FirstTanksShare { get; init; } = 0.5;

        /// <summary>
        /// Share of the remaining phase's budget an acceleration costs (unit
        /// fraction): 0.15. Range: 0-1. Game balance: spec §3.9
        /// <c>accelerateCostShare</c> and §6.3.
        /// </summary>
        [Range(0, 1)]
        [GameBalance("spec §3.9 accelerateCostShare (§6.3: +15 % of the remaining phase)")]
        public double AccelerateCostShare { get; init; } = 0.15;

        /// <summary>
        /// Share of the current phase's remaining days an acceleration cuts
        /// (unit fraction): 0.2. Range: 0-0.9. Game balance: spec §3.9
        /// <c>accelerateTimeCut</c> and §6.3.
        /// </summary>
        [Range(0, 0.9)]
        [GameBalance("spec §3.9 accelerateTimeCut (§6.3: −20 % of the remaining time)")]
        public double AccelerateTimeCut { get; init; } = 0.2;

        /// <summary>The phases of a full HFS lot: first tanks at 670 days (22 months), complete at 913 (30 months).</summary>
        public LandPhasePlanCalibration HfsLotPhases { get; init; } = LandPhasePlanCalibration.HfsLot;

        /// <summary>The phases of the HFS pilot lot.</summary>
        public LandPhasePlanCalibration HfsPilotPhases { get; init; } = LandPhasePlanCalibration.HfsPilot;

        /// <summary>The phases of expanding a pilot lot to a full lot.</summary>
        public LandPhasePlanCalibration HfsExpansionPhases { get; init; } = LandPhasePlanCalibration.HfsExpansion;

        /// <summary>The phases of a post-smolt module.</summary>
        public LandPhasePlanCalibration PostSmoltPhases { get; init; } = LandPhasePlanCalibration.PostSmolt;

        /// <summary>The phases of a RAS hall.</summary>
        public LandPhasePlanCalibration RasHallPhases { get; init; } = LandPhasePlanCalibration.RasHall;
    }

    /// <summary>
    /// The construction phases of one land unit (spec §6.3 "Land phase"): site
    /// works (blasting and foundations), the tank rings, the treatment hall,
    /// the sea works (intake and outfall, never in the closed season), the
    /// commissioning of the first tanks, and the completion of the rest. Each
    /// phase has its planned days and its share of the budget; a phase of 0
    /// days is not built (its share is 0), and the shares add up to 1.
    /// </summary>
    public sealed record LandPhasePlanCalibration
    {
        /// <summary>
        /// Days of the site works (unit days): blasting the pad and the
        /// foundations. Range: 0-720. Game balance: spec §6.3 phase list; the
        /// sum to the first tanks is the template's schedule.
        /// </summary>
        [Range(0, 720)]
        [GameBalance("spec §6.3: blasting and foundations")]
        public int SiteWorksDays { get; init; }

        /// <summary>Share of the budget paid for the site works (unit fraction). Range: 0-1. Game balance: spec §6.3.</summary>
        [Range(0, 1)]
        [GameBalance("spec §6.3: share of the budget in the site works")]
        public double SiteWorksShare { get; init; }

        /// <summary>Days of the tank rings rising (unit days). Range: 0-720. Game balance: spec §6.3.</summary>
        [Range(0, 720)]
        [GameBalance("spec §6.3: the tank rings rise")]
        public int TankRingsDays { get; init; }

        /// <summary>Share of the budget paid for the tank rings (unit fraction). Range: 0-1. Game balance: spec §6.3.</summary>
        [Range(0, 1)]
        [GameBalance("spec §6.3: share of the budget in the tanks")]
        public double TankRingsShare { get; init; }

        /// <summary>Days of the treatment hall (unit days). Range: 0-720. Game balance: spec §6.3.</summary>
        [Range(0, 720)]
        [GameBalance("spec §6.3: the treatment hall")]
        public int TreatmentHallDays { get; init; }

        /// <summary>Share of the budget paid for the treatment hall (unit fraction). Range: 0-1. Game balance: spec §6.3.</summary>
        [Range(0, 1)]
        [GameBalance("spec §6.3: share of the budget in the hall and process")]
        public double TreatmentHallShare { get; init; }

        /// <summary>
        /// Days of the sea works: intake and outfall pipes, underwater blasting
        /// (unit days), counted only outside the closed season. Range: 0-365.
        /// Game balance: [L:R12] (2024) restricts the works to protect salmon
        /// migration and a cod spawning ground; no published length.
        /// </summary>
        [Range(0, 365)]
        [GameBalance("[L:R12]: the sea works of a phase, outside the closed season; no published length")]
        public int SeaWorksDays { get; init; }

        /// <summary>Share of the budget paid for the sea works (unit fraction). Range: 0-1. Game balance: spec §6.3.</summary>
        [Range(0, 1)]
        [GameBalance("spec §6.3: share of the budget in the intake and outfall")]
        public double SeaWorksShare { get; init; }

        /// <summary>
        /// Days of commissioning the first tanks (unit days): at its end the
        /// first tanks take fish. Range: 1-365. Game balance: spec §6.3
        /// "Commissioning"; the HFS lot's sum to here is [L:R1]'s 22 months.
        /// </summary>
        [Range(1, 365)]
        [GameBalance("spec §6.3: commissioning; the HFS lot reaches it at [L:R1]'s 22 months")]
        public int FirstTanksDays { get; init; }

        /// <summary>Share of the budget paid for the first commissioning (unit fraction). Range: 0-1. Game balance: spec §6.3.</summary>
        [Range(0, 1)]
        [GameBalance("spec §6.3: share of the budget in commissioning")]
        public double FirstTanksShare { get; init; }

        /// <summary>
        /// Days from the first tanks to the whole unit (unit days); 0 when the
        /// first commissioning hands over every tank. Range: 0-720. Game
        /// balance: the HFS lot's sum is [L:R1]'s 30 months.
        /// </summary>
        [Range(0, 720)]
        [GameBalance("the HFS lot completes at [L:R1]'s 30 months")]
        public int CompletionDays { get; init; }

        /// <summary>Share of the budget paid for the completion (unit fraction). Range: 0-1. Game balance: spec §6.3.</summary>
        [Range(0, 1)]
        [GameBalance("spec §6.3: share of the budget in the remaining tanks")]
        public double CompletionShare { get; init; }

        /// <summary>Days from the start to the first tanks with no slip and no closed season.</summary>
        public int PlannedFirstTanksDays() => SiteWorksDays + TankRingsDays + TreatmentHallDays + SeaWorksDays + FirstTanksDays;

        /// <summary>Days from the start to the whole unit with no slip and no closed season.</summary>
        public int PlannedCompleteDays() => PlannedFirstTanksDays() + CompletionDays;

        /// <summary>
        /// A full HFS lot: 180 + 240 + 150 + 60 + 40 = 670 days (22 months) to
        /// the first tanks and 913 (30 months) to the whole lot ([L:R1]: phase 2,
        /// decided June 2024, first tanks April 2026, complete at the end of 2026).
        /// </summary>
        public static LandPhasePlanCalibration HfsLot { get; } = new()
        {
            SiteWorksDays = 180, SiteWorksShare = 0.25,
            TankRingsDays = 240, TankRingsShare = 0.38,
            TreatmentHallDays = 150, TreatmentHallShare = 0.20,
            SeaWorksDays = 60, SeaWorksShare = 0.08,
            FirstTanksDays = 40, FirstTanksShare = 0.04,
            CompletionDays = 243, CompletionShare = 0.05,
        };

        /// <summary>The pilot lot: the pad, 5 tanks and the intake for a full lot; every tank at the first commissioning (550 days).</summary>
        public static LandPhasePlanCalibration HfsPilot { get; } = new()
        {
            SiteWorksDays = 180, SiteWorksShare = 0.30,
            TankRingsDays = 150, TankRingsShare = 0.32,
            TreatmentHallDays = 120, TreatmentHallShare = 0.20,
            SeaWorksDays = 60, SeaWorksShare = 0.10,
            FirstTanksDays = 40, FirstTanksShare = 0.08,
        };

        /// <summary>The pilot's expansion to a full lot: 9 more tanks and hall on the built pad and intake (400 days).</summary>
        public static LandPhasePlanCalibration HfsExpansion { get; } = new()
        {
            TankRingsDays = 210, TankRingsShare = 0.55,
            TreatmentHallDays = 90, TreatmentHallShare = 0.25,
            FirstTanksDays = 40, FirstTanksShare = 0.10,
            CompletionDays = 60, CompletionShare = 0.10,
        };

        /// <summary>A post-smolt module: 548 days, the pre-F2 facility's build [code].</summary>
        public static LandPhasePlanCalibration PostSmolt { get; } = new()
        {
            SiteWorksDays = 90, SiteWorksShare = 0.20,
            TankRingsDays = 270, TankRingsShare = 0.45,
            TreatmentHallDays = 90, TreatmentHallShare = 0.15,
            SeaWorksDays = 60, SeaWorksShare = 0.10,
            FirstTanksDays = 38, FirstTanksShare = 0.10,
        };

        /// <summary>A RAS hall: 30 months (spec §2A), every tank at commissioning.</summary>
        public static LandPhasePlanCalibration RasHall { get; } = new()
        {
            SiteWorksDays = 180, SiteWorksShare = 0.20,
            TankRingsDays = 270, TankRingsShare = 0.35,
            TreatmentHallDays = 270, TreatmentHallShare = 0.30,
            SeaWorksDays = 60, SeaWorksShare = 0.08,
            FirstTanksDays = 133, FirstTanksShare = 0.07,
        };
    }

    /// <summary>
    /// The land permits (roadmap F2-14, spec §2C, §3.9 <c>permits</c>, §6.2):
    /// how long each case takes, what it costs, how likely a refusal is and
    /// what drives it, the appeal, the grid queue and the moratorium flag. A
    /// case's refusal probability is (base + per-sensitivity × the plot's
    /// nature sensitivity) × the unit's scale factor × each mitigation's cut.
    /// </summary>
    public sealed record LandPermitCalibration
    {
        /// <summary>
        /// Shortest zoning case (unit days): 365. Range: 90-1,095. Game balance:
        /// spec §2A "Zoning 1-2 years if the pad is not pre-zoned" ([L:R13]: the
        /// Indre Harøy detailed plan).
        /// </summary>
        [Range(90, 1095)]
        [GameBalance("spec §2A: zoning takes 1-2 years unless the pad is pre-zoned ([L:R13])")]
        public int ZoningDaysMin { get; init; } = 365;

        /// <summary>Longest zoning case (unit days): 730. Range: 90-1,460. Game balance: spec §2A.</summary>
        [Range(90, 1460)]
        [GameBalance("spec §2A: zoning takes 1-2 years")]
        public int ZoningDaysMax { get; init; } = 730;

        /// <summary>
        /// Typical land aquaculture permit case (unit days): 180. Range: 30-720.
        /// Game balance: spec §6.2 permit timeline (laksetildelingsforskriften
        /// § 7-2 cases, [L:L1]).
        /// </summary>
        [Range(30, 720)]
        [GameBalance("spec §6.2: the land permit in 180 days ([L:L1])")]
        public int LandAquacultureDays { get; init; } = 180;

        /// <summary>Typical discharge permit case (unit days): 240. Range: 30-720. Game balance: spec §6.2 (the county governor's case, [L:L4]).</summary>
        [Range(30, 720)]
        [GameBalance("spec §6.2: the discharge permit in 240 days ([L:L4])")]
        public int DischargeDays { get; init; } = 240;

        /// <summary>Typical sea-works permit case (unit days): 120. Range: 30-720. Game balance: spec §6.2 ([L:R12]).</summary>
        [Range(30, 720)]
        [GameBalance("spec §6.2: the sea-works permit in 120 days ([L:R12])")]
        public int SeaWorksDays { get; init; } = 120;

        /// <summary>Typical biosecurity approval (unit days): 90. Range: 30-720. Game balance: spec §6.2 (Mattilsynet, [L:L5]).</summary>
        [Range(30, 720)]
        [GameBalance("spec §6.2: the biosecurity approval in 90 days ([L:L5])")]
        public int BiosecurityDays { get; init; } = 90;

        /// <summary>
        /// Spread of a non-zoning case's decision around its typical days (unit
        /// fraction): 0.25, so a 180-day case is decided in 135-225 days.
        /// Range: 0-0.9. Game balance: case handling times vary; no published spread.
        /// </summary>
        [Range(0, 0.9)]
        [GameBalance("case handling times vary around the typical days; no published spread")]
        public double DecisionSpread { get; init; } = 0.25;

        /// <summary>
        /// Length of an appeal (unit days): 180. Range: 30-730. Game balance:
        /// spec §3.9 <c>appealDays</c> (F2-14 acceptance 2).
        /// </summary>
        [Range(30, 730)]
        [GameBalance("spec §3.9 appealDays")]
        public int AppealDays { get; init; } = 180;

        /// <summary>
        /// Refusal probability of an appeal as a share of the refused case's
        /// (unit fraction): 0.5. Range: 0-1. Game balance: a refusal can be
        /// overturned, as Havfarm's refused conversion was granted on appeal ([V:12]).
        /// </summary>
        [Range(0, 1)]
        [GameBalance("a refusal can be overturned on appeal, as Havfarm's was a year later [V:12]")]
        public double AppealRefusalFactor { get; init; } = 0.5;

        /// <summary>
        /// Fee of a sea-works permit (unit NOK): 44,300. Range: 0-1,000,000.
        /// Source: [L:R12] (2024): Salmon Evolution phase 2's sea-works permit.
        /// </summary>
        [Range(0, 1_000_000)]
        [Source("L:R12")]
        public decimal SeaWorksFeeNok { get; init; } = 44_300m;

        /// <summary>
        /// First month of the season closed to sea works (unit month 1-12): 4
        /// (April). Range: 1-12. Source: [L:R12] (2024): the works were
        /// restricted to protect the salmon smolt run and a cod spawning ground;
        /// spec §3.9 closes April-June.
        /// </summary>
        [Range(1, 12)]
        [Source("L:R12")]
        public int SeaWorksClosedFromMonth { get; init; } = 4;

        /// <summary>Last month of the closed season (unit month 1-12): 6 (June). Range: 1-12. Source: [L:R12] (2024).</summary>
        [Range(1, 12)]
        [Source("L:R12")]
        public int SeaWorksClosedToMonth { get; init; } = 6;

        /// <summary>
        /// Refusal probability of a zoning case on a plot of no nature
        /// sensitivity (unit probability): 0.01. Range: 0-0.5. Game balance:
        /// F2-14 acceptance 2 (sensitivity 0.6 refuses ≥ 3× as often as 0.1).
        /// </summary>
        [Range(0, 0.5)]
        [GameBalance("F2-14 acceptance 2: sensitivity drives refusals, not the base")]
        public double ZoningRefusalBase { get; init; } = 0.01;

        /// <summary>
        /// Zoning refusal added per unit of nature sensitivity (unit probability):
        /// 0.35. Range: 0-1. Game balance: Gigante's Verholmen plan was refused
        /// for its encroachment on untouched nature and red-listed birds ([L:G4], 2024).
        /// </summary>
        [Range(0, 1)]
        [GameBalance("Verholmen was refused for encroaching on untouched nature [L:G4]")]
        public double ZoningRefusalPerSensitivity { get; init; } = 0.35;

        /// <summary>
        /// Refusal probability of the land aquaculture permit at no sensitivity
        /// (unit probability): 0.005. Range: 0-0.5. Game balance: the permit is
        /// fee-free and granted to plants that meet the rules ([L:L1], [L:L3]).
        /// </summary>
        [Range(0, 0.5)]
        [GameBalance("the fee-free land permit is granted to plants that meet the rules [L:L1]")]
        public double LandAquacultureRefusalBase { get; init; } = 0.005;

        /// <summary>Land permit refusal added per unit of nature sensitivity (unit probability): 0.10. Range: 0-1. Game balance: the 2025 rules' land-sea boundary ([L:L3]).</summary>
        [Range(0, 1)]
        [GameBalance("the 2025 rules draw a boundary between land and sea plants [L:L3]")]
        public double LandAquacultureRefusalPerSensitivity { get; init; } = 0.10;

        /// <summary>
        /// Refusal probability of a discharge permit at no sensitivity (unit
        /// probability): 0.01. Range: 0-0.5. Game balance: the discharge permit
        /// is the real capacity limit ([L:L4]).
        /// </summary>
        [Range(0, 0.5)]
        [GameBalance("the discharge permit is the real capacity limit [L:L4]")]
        public double DischargeRefusalBase { get; init; } = 0.01;

        /// <summary>
        /// Discharge refusal added per unit of nature sensitivity (unit
        /// probability): 0.35. Range: 0-1. Game balance: Verholmen's untreated
        /// nitrogen, the sewage of 190,000 people, was a reason for its refusal ([L:G4]).
        /// </summary>
        [Range(0, 1)]
        [GameBalance("Verholmen's untreated nitrogen was a reason for its refusal [L:G4]")]
        public double DischargeRefusalPerSensitivity { get; init; } = 0.35;

        /// <summary>Refusal probability of a sea-works permit at no sensitivity (unit probability): 0.005. Range: 0-0.5. Game balance: granted with conditions ([L:R12]).</summary>
        [Range(0, 0.5)]
        [GameBalance("sea works are granted with seasonal conditions [L:R12]")]
        public double SeaWorksRefusalBase { get; init; } = 0.005;

        /// <summary>Sea-works refusal added per unit of nature sensitivity (unit probability): 0.10. Range: 0-1. Game balance: a spawning ground near the intake ([L:R12]).</summary>
        [Range(0, 1)]
        [GameBalance("a spawning ground near the works raises the stakes [L:R12]")]
        public double SeaWorksRefusalPerSensitivity { get; init; } = 0.10;

        /// <summary>Refusal probability of the biosecurity approval (unit probability): 0.01. Range: 0-0.5. Game balance: Mattilsynet approves a water-treatment plan that disinfects the intake ([L:L2], [L:L5]).</summary>
        [Range(0, 0.5)]
        [GameBalance("Mattilsynet approves a plan that disinfects the intake [L:L2], [L:L5]")]
        public double BiosecurityRefusalBase { get; init; } = 0.01;

        /// <summary>Biosecurity refusal added per unit of nature sensitivity (unit probability): 0. Range: 0-1. Game balance: biosecurity reads the water treatment, not the plot.</summary>
        [Range(0, 1)]
        [GameBalance("biosecurity reads the water treatment, not the plot")]
        public double BiosecurityRefusalPerSensitivity { get; init; }

        /// <summary>
        /// Exponent of the scale factor on the zoning and discharge refusal
        /// (unit exponent): 0.5, so the factor is (the unit's nameplate / a full
        /// lot's)^0.5: a pilot lot 0.57, a post-smolt module 0.23, two lots 1.41.
        /// Range: 0-2. Game balance: the spec's "smaller first phase" mitigation
        /// (§6.2); a smaller plant discharges less ([L:G4]'s 19,087 t plan was refused).
        /// </summary>
        [Range(0, 2)]
        [GameBalance("spec §6.2 'smaller first phase': a smaller plant discharges less")]
        public double ScaleExponent { get; init; } = 0.5;

        /// <summary>
        /// Share of the zoning, discharge and sea-works refusal an environmental
        /// survey removes (unit fraction): 0.3. Range: 0-1. Game balance: spec
        /// §3.9 <c>surveyRefusalCut</c>.
        /// </summary>
        [Range(0, 1)]
        [GameBalance("spec §3.9 surveyRefusalCut")]
        public double SurveyRefusalCut { get; init; } = 0.3;

        /// <summary>
        /// Cost of the environmental survey filed with an application (unit NOK):
        /// 4,000,000. Range: 0-50,000,000. Game balance: an impact assessment of
        /// birds, seabed and currents by consultants; no published price.
        /// </summary>
        [Range(0, 50_000_000)]
        [GameBalance("an impact assessment of birds, seabed and currents by consultants; no published price")]
        public decimal SurveyCostNok { get; init; } = 4_000_000m;

        /// <summary>
        /// Share of the discharge refusal a denitrification commitment removes
        /// (unit fraction): 0.5. Range: 0-1. Game balance: it halves the
        /// dissolved nitrogen (spec §3.3 "Discharge").
        /// </summary>
        [Range(0, 1)]
        [GameBalance("denitrification halves the dissolved nitrogen (spec §3.3)")]
        public double DenitrificationRefusalCut { get; init; } = 0.5;

        /// <summary>
        /// Regulatory flag: a moratorium on new land aquaculture applications
        /// (unit flag, 0 = off). Off. Range: 0-1. Game balance: the freeze from
        /// 20 December 2022 to 1 July 2025 ([V:13], [L:L1]) as a scenario lever.
        /// </summary>
        [Range(0, 1)]
        [GameBalance("the 2022-25 freeze on land applications [V:13] as a scenario lever; off by default")]
        public bool Moratorium { get; init; }

        /// <summary>
        /// Shortest wait in the grid connection queue (unit days): 365. Range:
        /// 30-1,825. Game balance: spec §3.9 <c>gridQueueDaysMin</c> (F2-14 acceptance 7).
        /// </summary>
        [Range(30, 1825)]
        [GameBalance("spec §3.9 gridQueueDaysMin")]
        public int GridQueueDaysMin { get; init; } = 365;

        /// <summary>Longest wait in the grid connection queue (unit days): 1,095. Range: 30-2,555. Game balance: spec §3.9 <c>gridQueueDaysMax</c>.</summary>
        [Range(30, 2555)]
        [GameBalance("spec §3.9 gridQueueDaysMax")]
        public int GridQueueDaysMax { get; init; } = 1_095;

        /// <summary>
        /// Connection fee of a grid project per 10 MW (unit NOK): 40,000,000.
        /// Range: 1,000,000-200,000,000. Game balance: spec §3.9 <c>gridFeeNOKPer10MW</c>.
        /// </summary>
        [Range(1_000_000, 200_000_000)]
        [GameBalance("spec §3.9 gridFeeNOKPer10MW")]
        public decimal GridFeeNokPer10MW { get; init; } = 40_000_000m;

        /// <summary>Grid capacity one capacity project adds (unit MW): 10. Range: 1-100. Game balance: the fee's 10 MW step.</summary>
        [Range(1, 100)]
        [GameBalance("the connection fee's 10 MW step")]
        public double GridProjectStepMW { get; init; } = 10.0;
    }

    /// <summary>The running costs of a land site beyond its energy and oxygen (F2-12).</summary>
    public sealed record LandOperationsCalibration
    {
        /// <summary>
        /// Cost of one employee a year (unit NOK per FTE): 650,000. Range:
        /// 300,000-1,500,000. Source: [code] the pre-F2 RAS facility's staff
        /// cost (RASFacility.TickDay).
        /// </summary>
        [Range(300_000, 1_500_000)]
        [Source("code")]
        public decimal StaffCostNokPerFteYear { get; init; } = 650_000m;
    }

    #endregion
}
