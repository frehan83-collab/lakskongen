using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace LakskongenSim.Calibration
{
    #region F2-07 offshore unit: classes, logistics, costs, structure, sensors, alarm load, teething

    // Roadmap F2-07 (docs/FARM_TYPES_SPEC.md §3.3 "OffshoreTickSystem", §3.6,
    // §3.9 facilities_offshore.json, §6.2-6.3). Every leaf carries F2-04's
    // [Range] and exactly one [Source] or [GameBalance] (CalibrationGuardTests
    // walks facilities.offshore). A property that two unit classes share has one
    // provenance for both: a [GameBalance] reason names the source of the class
    // that has one. Sources cited by name:
    // - Seabrokers 2024: Seabrokers Chartering, "Seabreeze" December 2024,
    //   North Sea average spot day rates for 2024 (PSV < 900 m² £10,462; AHTS
    //   < 22,000 bhp £35,017): https://seabrokers.no/chartering/wp-content/uploads/sites/8/2025/01/Seabreeze-December.pdf
    // - valutafx 2024: the 2024 average NOK per GBP, 13.7 (0.0728 GBP per NOK):
    //   https://www.valutafx.com/history/nok-gbp-2024
    // - FFE 2017: fishfarmingexpert.com, "Picture special: anchoring the offshore
    //   fish farm" (2017): three anchor-handling tugs towed OF1 into position.
    // - iLaks feed barge: ilaks.no, "Bakkafrost sikrer seg 40 meter lang
    //   fôrflåte": a 700 t feed barge (GroAqua has delivered 700 t and 1,100 t ones).
    // - iLaks 2017: ilaks.no, "Skal halvere dieselforbruket til oppdrettsanlegg
    //   ved hjelp av sol og vind" (9 Aug 2017): diesel-powered sites burn on
    //   average 200,000 litres a year, nearly 1.5 M NOK.
    // - Clarksons 2023: "Essential vessels for offshore windfarm construction and
    //   operations" (clarksons.com, 2023): smaller offshore wind vessels incl.
    //   CTVs at €4,000-8,000 a day.
    // - O:R37 Menon 131/2023 (Oct 2023), table on p. 25: operating cost 56 NOK/kg
    //   for the offshore pilots, "a 30 % increase on conventional production"
    //   (Fiskeridirektoratet's profitability survey 2021); offshore wages about
    //   10 % above coastal per man-year, labour cost per kg about half.

    /// <summary>The offshore unit classes the player builds (spec §2A, §3.9 "of1" and "havfarm").</summary>
    public sealed record OffshoreUnitClassesCalibration
    {
        /// <summary>OF1-class semi-submersible (the "Havmerd").</summary>
        public OffshoreClassCalibration Of1 { get; init; } = new();

        /// <summary>Havfarm-class ship (the "Skipsmerd", stage 3, F2-11).</summary>
        public OffshoreClassCalibration Havfarm { get; init; } = new()
        {
            ContractCapexNok = 1_000_000_000m,
            TeethingBudgetNok = 218_000_000m,
            HullDays = 641,
            VoyageDays = 60,
            MooringDays = 70,
            SiloTonnes = 1_500,
            SilageTonnes = 300,
            CrewInRotation = 18,
        };

        /// <summary>The stage payments of a unit's yard contract (shares of the contract price).</summary>
        public OffshorePaymentScheduleCalibration Payments { get; init; } = new();
    }

    /// <summary>
    /// One unit class: its contract price, the teething budget the player is
    /// shown, the build phases (yard contract, hull, voyage, the player's tow,
    /// mooring, commissioning), the silo and silage tank and the crew. The
    /// defaults are OF1-class.
    /// </summary>
    public sealed record OffshoreClassCalibration
    {
        /// <summary>
        /// Contract price of the unit (NOK): 720 M OF1-class [O:R4], 1,000 M
        /// Havfarm-class [O:R26]. The first cycle's fixes are the teething
        /// budget on top (OF1: 720 M + 157 M = 877 M).
        /// </summary>
        [Range(100_000_000, 5_000_000_000)]
        [Source("O:R4", "O:R26")]
        public decimal ContractCapexNok { get; init; } = 720_000_000m;

        /// <summary>
        /// Budget of the first cycle's teething fixes (NOK): 157 M on OF1 [O:R4]
        /// (14 fixes); a Havfarm-class unit takes the same 157/720 share of its
        /// contract, 218 M (derived). Shown before construction ("budsjett 157 M")
        /// and spent defect by defect.
        /// </summary>
        [Range(0, 1_000_000_000)]
        [Source("O:R4")]
        public decimal TeethingBudgetNok { get; init; } = 157_000_000m;

        /// <summary>
        /// Days of the yard contract phase (design, steel order): 92, the first
        /// quarter of OF1's 18 yard months [O:R1, O:R33]. Game balance: the split
        /// of the yard months is the game's.
        /// </summary>
        [Range(1, 365)]
        [GameBalance("the first quarter of OF1's 18 yard months (O:R1, O:R33); the split is the game's")]
        public int YardContractDays { get; init; } = 92;

        /// <summary>
        /// Days the hull takes at the yard after the contract phase: 456 on OF1
        /// (92 + 456 = 548 days, 18 months [O:R1, O:R33]); 641 Havfarm-class, so
        /// its phases with the tow make the 880 days to first fish of the
        /// offshore dossier §7.
        /// </summary>
        [Range(30, 1500)]
        [GameBalance("OF1: 18 yard months in all (O:R1, O:R33); Havfarm-class: 880 days to first fish (offshore dossier §7)")]
        public int HullDays { get; init; } = 456;

        /// <summary>Days of the delivery voyage: 77 (11 weeks, OF1 from China in 2017 [FFE 2017]); Havfarm-class 60.</summary>
        [Range(1, 365)]
        [GameBalance("spec §2A: OF1's 11-week delivery voyage (trade press 2017); Havfarm-class built nearer, 60 d")]
        public int VoyageDays { get; init; } = 77;

        /// <summary>
        /// Days of mooring after the tow: 28 on OF1, so tow (3) + mooring (28) +
        /// commissioning (14) are the catalogue's 45 days of "mooring and
        /// certificate" (spec §2A); Havfarm-class 70 (turret mooring, 11 lines).
        /// </summary>
        [Range(1, 365)]
        [GameBalance("with the 3-day tow and 14 days of commissioning, spec §2A's ≈ 45 d of mooring and certificate")]
        public int MooringDays { get; init; } = 28;

        /// <summary>Days of commissioning and certification: 14 (spec §3.9 certificationDays).</summary>
        [Range(1, 120)]
        [GameBalance("spec §3.9: 14 days of certification after mooring")]
        public int CommissioningDays { get; init; } = 14;

        /// <summary>
        /// Feed silo (t): 700 OF1-class, 1,500 Havfarm-class. OF1's silo capacity
        /// is unpublished (offshore dossier §10); 700 t is a feed barge's
        /// (iLaks feed barge).
        /// </summary>
        [Range(50, 5000)]
        [GameBalance("OF1's silo capacity is unpublished; a 700 t feed barge (iLaks feed barge); Havfarm-class 1,500 t (spec §3.9)")]
        public double SiloTonnes { get; init; } = 700;

        /// <summary>Ensilage tank (t): 120 OF1-class, 300 Havfarm-class (spec §3.9).</summary>
        [Range(10, 2000)]
        [GameBalance("spec §3.9 silageT")]
        public double SilageTonnes { get; init; } = 120;

        /// <summary>Crew in rotation: 12 on OF1 [O:R13] (3-4 on board on a 9-on/12-off rota); Havfarm-class 18.</summary>
        [Range(1, 100)]
        [GameBalance("O:R13: 12 in rotation on OF1; Havfarm-class 18 (spec stage 3)")]
        public int CrewInRotation { get; init; } = 12;
    }

    /// <summary>
    /// The yard contract's stage payments (spec §6.2 "payment schedule per
    /// phase"): shares of the contract price paid at the start of each phase.
    /// The tow (tugs are chartered, an operating cost) and commissioning take
    /// none. The shares add up to 1 (OffshoreUnitBuildTests).
    /// </summary>
    public sealed record OffshorePaymentScheduleCalibration
    {
        /// <summary>Share at signing (the yard-contract phase): 0.2.</summary>
        [Range(0.0, 1.0)]
        [GameBalance("a shipyard's signing instalment, 20 %")]
        public double ContractShare { get; init; } = 0.2;

        /// <summary>Share over steel cutting, keel and launch (the hull phase): 0.5.</summary>
        [Range(0.0, 1.0)]
        [GameBalance("a shipyard's steel, keel and launch instalments, 50 %")]
        public double HullShare { get; init; } = 0.5;

        /// <summary>Share at delivery (the voyage phase): 0.2.</summary>
        [Range(0.0, 1.0)]
        [GameBalance("a shipyard's delivery instalment, 20 %")]
        public double DeliveryShare { get; init; } = 0.2;

        /// <summary>Share of the mooring spread and its installation (the mooring phase): 0.1.</summary>
        [Range(0.0, 1.0)]
        [GameBalance("the mooring spread is its own contract: 8 lines and anchors (O:R6), 10 %")]
        public double MooringShare { get; init; } = 0.1;
    }

    /// <summary>
    /// What an offshore unit costs to run (spec §3.3 "Opex", §3.6): crew, energy,
    /// maintenance and insurance by the year, vessels by the day they are
    /// chartered (from the booked day, slipped days included), the tow's tugs,
    /// and the consortium's buy-out. A consortium unit books these lines at its
    /// equity share.
    /// </summary>
    public sealed record OffshoreCostsCalibration
    {
        /// <summary>
        /// Pay of one crew member in rotation (NOK a year): 900,000. Game balance:
        /// spec §3.3 (salary GB); offshore wages run about 10 % above coastal per
        /// man-year [O:R37].
        /// </summary>
        [Range(400_000, 2_000_000)]
        [GameBalance("spec §3.3 (salary GB); O:R37: offshore wages about 10 % above coastal per man-year")]
        public decimal CrewSalaryNokPerYear { get; init; } = 900_000m;

        /// <summary>
        /// Diesel the unit's generators burn a day before its fish (litres):
        /// 1,100, twice a diesel-powered fjord site's average (200,000 litres a
        /// year, 548 a day, iLaks 2017) for a unit of about twice a site's MTB
        /// with ballast pumps, cranes and quarters. The same litres are its
        /// Scope 1 carbon.
        /// </summary>
        [Range(0, 10_000)]
        [GameBalance("twice a diesel-powered fjord site's 200,000 L a year (iLaks 2017) for a unit of about twice its MTB")]
        public double FuelLitresPerDay { get; init; } = 1_100;

        /// <summary>Diesel per tonne of standing biomass a day (litres): 0.1, the feeding systems' share.</summary>
        [Range(0.0, 2.0)]
        [GameBalance("feed blowers and pumps scale with the fish; a 3,000 t unit burns 300 L a day more")]
        public double FuelLitresPerTonnePerDay { get; init; } = 0.1;

        /// <summary>
        /// Price of the generators' diesel (NOK a litre): 7.5, iLaks 2017's
        /// "nearly 1.5 million NOK" for a site's 200,000 litres a year (derived).
        /// </summary>
        [Range(3.0, 30.0)]
        [Source("iLaks 2017")]
        public decimal DieselNokPerLitre { get; init; } = 7.5m;

        /// <summary>Maintenance of an operational unit (share of its contract price a year): 0.025.</summary>
        [Range(0.0, 0.1)]
        [GameBalance("spec §3.3: maintenance 2.5 % of capex a year")]
        public double MaintenanceShareOfCapexPerYear { get; init; } = 0.025;

        /// <summary>Insurance of an operational unit (share of its contract price a year): 0.008.</summary>
        [Range(0.0, 0.05)]
        [GameBalance("spec §3.3: insurance 0.8 % of capex a year")]
        public double InsuranceShareOfCapexPerYear { get; init; } = 0.008;

        /// <summary>
        /// A standard service vessel (NOK a day): 143,000, the 2024 North Sea
        /// average spot rate of a PSV under 900 m² (£10,462, Seabrokers 2024) at
        /// 13.7 NOK per GBP (valutafx 2024).
        /// </summary>
        [Range(50_000, 500_000)]
        [Source("Seabrokers 2024", "valutafx 2024")]
        public decimal ServiceVesselNokPerDay { get; init; } = 143_000m;

        /// <summary>
        /// A DP vessel with walk-to-work access (NOK a day): 290,000, about twice
        /// a PSV's, under an offshore-wind service operation vessel's ~£30,000
        /// (guidetofloatingoffshorewind.com, O.4.2).
        /// </summary>
        [Range(100_000, 1_000_000)]
        [GameBalance("between a PSV (Seabrokers 2024) and a wind farm's walk-to-work SOV (~£30,000/day, guidetofloatingoffshorewind.com O.4.2)")]
        public decimal DpVesselNokPerDay { get; init; } = 290_000m;

        /// <summary>
        /// A crew transfer boat for a crew change (NOK a day): 50,000, the low end
        /// of Clarksons 2023's €4,000-8,000 a day for small offshore vessels and
        /// above guidetofloatingoffshorewind.com's ~£2,000 for a wind farm CTV.
        /// </summary>
        [Range(10_000, 300_000)]
        [GameBalance("between a wind farm CTV's ~£2,000/day (guidetofloatingoffshorewind.com O.4.1) and Clarksons 2023's €4,000-8,000 for small offshore vessels")]
        public decimal CrewBoatNokPerDay { get; init; } = 50_000m;

        /// <summary>
        /// An ROV net inspection from the unit's crane (NOK an inspection, paid
        /// when done; the ROV waits on board, no vessel is chartered): 75,000, the
        /// game's ROV job.
        /// </summary>
        [Range(10_000, 1_000_000)]
        [Source("code")]
        public decimal RovInspectionNok { get; init; } = 75_000m;

        /// <summary>A wellboat for fish handling (NOK a day): 150,000, the game's spot harvest wellboat hire.</summary>
        [Range(50_000, 1_000_000)]
        [Source("code")]
        public decimal WellboatNokPerDay { get; init; } = 150_000m;

        /// <summary>Price factor of an offshore-class wellboat (handles fish to 2.5 m): 1.5.</summary>
        [Range(1.0, 3.0)]
        [GameBalance("spec §3.9 offshoreWellboatPremium; S2-04's offshore route prices the same factor")]
        public double OffshoreWellboatPremium { get; init; } = 1.5;

        /// <summary>A crew change by helicopter (NOK a change): 150,000, the pre-F2 offshore farm's crew change.</summary>
        [Range(10_000, 1_000_000)]
        [Source("code")]
        public decimal HelicopterNokPerChange { get; init; } = 150_000m;

        /// <summary>
        /// An anchor-handling tug (NOK a day): 480,000, the 2024 North Sea average
        /// spot rate of an AHTS under 22,000 bhp (£35,017, Seabrokers 2024) at
        /// 13.7 NOK per GBP (valutafx 2024).
        /// </summary>
        [Range(50_000, 2_000_000)]
        [Source("Seabrokers 2024", "valutafx 2024")]
        public decimal AnchorTugNokPerDay { get; init; } = 480_000m;

        /// <summary>Tugs a tow takes: 3 (three anchor-handling tugs towed OF1 into position, FFE 2017).</summary>
        [Range(1, 10)]
        [Source("FFE 2017")]
        public int TowTugs { get; init; } = 3;

        /// <summary>Fewest days a unit shelters after a failed tow window: 5.</summary>
        [Range(1, 60)]
        [GameBalance("spec §6.3: bad luck means sheltering 5-15 d at charter cost")]
        public int TowShelterDaysMin { get; init; } = 5;

        /// <summary>Most days a unit shelters after a failed tow window: 15.</summary>
        [Range(1, 60)]
        [GameBalance("spec §6.3: bad luck means sheltering 5-15 d at charter cost")]
        public int TowShelterDaysMax { get; init; } = 15;

        /// <summary>The partner's share is bought out at its book value times this: 1.2.</summary>
        [Range(1.0, 3.0)]
        [GameBalance("spec §6.1: buy-out later at book × 1.2")]
        public double BuyoutMultiple { get; init; } = 1.2;
    }

    /// <summary>The logistics rhythm of an operational unit (spec §3.3 "Silo and feed", "Crew").</summary>
    public sealed record OffshoreLogisticsCalibration
    {
        /// <summary>Days between crew changes: 7 (9-on/12-off in 3 shifts [O:R3], derived).</summary>
        [Range(1, 42)]
        [Source("O:R3")]
        public int CrewChangeIntervalDays { get; init; } = 7;

        /// <summary>Days a crew change may be overdue before the crew rota calls the helicopter: 2.</summary>
        [Range(0, 14)]
        [GameBalance("spec §3.3: the helicopter is the fallback; two days late is the point it flies")]
        public int HelicopterFallbackOverdueDays { get; init; } = 2;

        /// <summary>
        /// The silo warning (SiloLow) comes when the silo holds fewer days of feed
        /// than this: 5. Game balance: the median January spell without a
        /// handling day at off_1 is 7 days (F2-06 finding), so 5 days leaves a
        /// standard resupply window most of the time.
        /// </summary>
        [Range(1.0, 30.0)]
        [GameBalance("under the median 7-day winter spell without a window at off_1 (F2-06 finding, docs/RUN_LOG.md §8)")]
        public double SiloLowWarningDays { get; init; } = 5.0;

        /// <summary>Days of feeding the silo's days-of-feed figure averages over: 7.</summary>
        [Range(1, 30)]
        [GameBalance("a week of feeding smooths the day-to-day appetite")]
        public int FeedAverageDays { get; init; } = 7;

        /// <summary>Tonnes of ensilage per tonne of dead fish collected: 1.0 (ground fish plus acid).</summary>
        [Range(0.5, 2.0)]
        [GameBalance("ground dead fish preserved with formic acid keep about their weight")]
        public double SilagePerDeadTonne { get; init; } = 1.0;
    }

    /// <summary>
    /// The structure's fatigue and the nets' wear (spec §3.3 "Structure and
    /// net"): deterministic, from the plot's sea; the player sees them as of
    /// the last inspection (or live with the motion or net sensor package).
    /// F2-08 adds the incidents (tears, mooring failure).
    /// </summary>
    public sealed record OffshoreStructureCalibration
    {
        /// <summary>Structural integrity lost per day: this × (Hs / design Hs)³: 0.0005.</summary>
        [Range(0.0, 0.01)]
        [GameBalance("spec §3.3 fatigueCoeff")]
        public double FatigueCoeff { get; init; } = 0.0005;

        /// <summary>Net integrity lost per day in any sea (percentage points): 0.04.</summary>
        [Range(0.0, 1.0)]
        [GameBalance("spec §3.3 netDecayPctPerDay")]
        public double NetDecayPctPerDay { get; init; } = 0.04;

        /// <summary>Extra net integrity lost per metre of Hs above <see cref="NetDecayThresholdHsM"/> (percentage points a day): 0.2.</summary>
        [Range(0.0, 2.0)]
        [GameBalance("spec §3.3 netDecayPerMOver3")]
        public double NetDecayPerMetreOver { get; init; } = 0.2;

        /// <summary>Hs above which the nets wear faster (m): 3.0.</summary>
        [Range(1.0, 8.0)]
        [GameBalance("spec §3.3: net wear rises above Hs 3 m")]
        public double NetDecayThresholdHsM { get; init; } = 3.0;

        /// <summary>A full structural repair, from integrity 0 to 1 (NOK, pro rata): 50 M, the bridge's major offshore repair.</summary>
        [Range(1_000_000, 500_000_000)]
        [Source("code")]
        public decimal StructureRepairFullNok { get; init; } = 50_000_000m;

        /// <summary>Net repair per percentage point of integrity restored (NOK): 250,000.</summary>
        [Range(10_000, 5_000_000)]
        [GameBalance("a full OF1 net set of ~250,000 m³ (O:R6) at 25 M NOK, pro rata")]
        public decimal NetRepairNokPerPct { get; init; } = 250_000m;
    }

    /// <summary>The sensor packages an offshore unit can carry (spec §3.6 "Information quality").</summary>
    public sealed record OffshoreSensorsCalibration
    {
        /// <summary>Motion reference unit: the structure's state is known continuously.</summary>
        public OffshoreSensorPackageCalibration Mru { get; init; } = new() { InstallNok = 1_500_000m, ServiceNok = 100_000m };

        /// <summary>Subsea cameras: the nets' state is known continuously.</summary>
        public OffshoreSensorPackageCalibration SubseaCam { get; init; } = new() { InstallNok = 3_000_000m, ServiceNok = 200_000m };

        /// <summary>Upward-looking echo sounders: biomass known to ±3 % (spec §3.6).</summary>
        public OffshoreSensorPackageCalibration Echo { get; init; } = new() { InstallNok = 2_000_000m, ServiceNok = 150_000m };

        /// <summary>AIS and marine broadband to a shore control centre.</summary>
        public OffshoreSensorPackageCalibration AisBroadband { get; init; } = new() { InstallNok = 1_000_000m, ServiceNok = 50_000m };

        /// <summary>Days between services before a package counts as past its service (alarm load): 180.</summary>
        [Range(30, 730)]
        [GameBalance("a half-yearly service; a package past it adds to the alarm load (spec §3.3)")]
        public int ServiceIntervalDays { get; init; } = 180;
    }

    /// <summary>One sensor package's price to install and to service.</summary>
    public sealed record OffshoreSensorPackageCalibration
    {
        /// <summary>Price to install (NOK).</summary>
        [Range(10_000, 50_000_000)]
        [GameBalance("OF1 carries these systems (owner diagram O1); the prices are the game's")]
        public decimal InstallNok { get; init; } = 1_000_000m;

        /// <summary>Price of one service (NOK).</summary>
        [Range(1_000, 5_000_000)]
        [GameBalance("a service is about a tenth of the package's price")]
        public decimal ServiceNok { get; init; } = 100_000m;
    }

    /// <summary>
    /// The alarm load (spec §3.3): emergent, from the unit's unfixed revealed
    /// defects, sensor packages past their service and overdue crew; the
    /// alarm review that cuts it is F2-08's.
    /// </summary>
    public sealed record OffshoreAlarmLoadCalibration
    {
        /// <summary>Alarm load of a sound unit: 0.15.</summary>
        [Range(0.0, 1.0)]
        [GameBalance("spec §3.3 alarmLoad.base")]
        public double Base { get; init; } = 0.15;

        /// <summary>Added per revealed, unfixed teething defect: 0.15.</summary>
        [Range(0.0, 1.0)]
        [GameBalance("spec §3.3 alarmLoad.perDefect")]
        public double PerUnfixedRevealedDefect { get; init; } = 0.15;

        /// <summary>Added per sensor package past its service: 0.10.</summary>
        [Range(0.0, 1.0)]
        [GameBalance("spec §3.3 alarmLoad.perSensorPastService")]
        public double PerSensorPastService { get; init; } = 0.10;

        /// <summary>Added per day the crew change is overdue: 0.02.</summary>
        [Range(0.0, 0.2)]
        [GameBalance("spec §3.3 alarmLoad.perCrewOverdueDay")]
        public double PerCrewOverdueDay { get; init; } = 0.02;

        /// <summary>Overdue crew days counted at most: 10.</summary>
        [Range(0, 60)]
        [GameBalance("spec §3.3: min(CrewOverdueDays, 10)")]
        public int CrewOverdueCapDays { get; init; } = 10;
    }

    /// <summary>
    /// The teething deck (spec §3.3 "Teething deck"): the first unit of a class
    /// draws 3-4 unknown defects, a later one 1-2; each has a reveal day in
    /// the first cycle drawn at commissioning, and a shakedown before stocking
    /// reveals them early so they can be fixed before there are fish. A
    /// defect revealed and unfixed while the unit holds fish fires.
    /// </summary>
    public sealed record OffshoreTeethingCalibration
    {
        /// <summary>Fewest defects of the first unit of a class: 3.</summary>
        [Range(0, 10)]
        [GameBalance("spec §3.3: 3-4 unknown teething defects")]
        public int FirstUnitDefectsMin { get; init; } = 3;

        /// <summary>Most defects of the first unit of a class: 4.</summary>
        [Range(0, 10)]
        [GameBalance("spec §3.3: 3-4 unknown teething defects")]
        public int FirstUnitDefectsMax { get; init; } = 4;

        /// <summary>Fewest defects of a later unit of a class (a halved deck): 1.</summary>
        [Range(0, 10)]
        [GameBalance("spec §3.3: later units of a class draw from a halved deck")]
        public int LaterUnitDefectsMin { get; init; } = 1;

        /// <summary>Most defects of a later unit of a class: 2.</summary>
        [Range(0, 10)]
        [GameBalance("spec §3.3: later units of a class draw from a halved deck")]
        public int LaterUnitDefectsMax { get; init; } = 2;

        /// <summary>
        /// Rate a shakedown reveals each hidden defect at (per 30 days, a Poisson
        /// rate): 0.5, so a 90-day shakedown reveals a given defect with
        /// probability 1 − e^−1.5 = 0.78.
        /// </summary>
        [Range(0.0, 3.0)]
        [GameBalance("spec §3.3: a shakedown reveals each defect at 0.5 per 30 days")]
        public double ShakedownRevealPer30Days { get; init; } = 0.5;

        /// <summary>The short shakedown the player may choose (days; none, 0, is always a choice): 60.</summary>
        [Range(1, 365)]
        [GameBalance("spec §6.3: shakedown 0 / 60 / 90 / 120 d")]
        public int ShakedownShortDays { get; init; } = 60;

        /// <summary>The middle shakedown (days): 90.</summary>
        [Range(1, 365)]
        [GameBalance("spec §6.3: shakedown 0 / 60 / 90 / 120 d")]
        public int ShakedownMediumDays { get; init; } = 90;

        /// <summary>The long shakedown (days): 120.</summary>
        [Range(1, 365)]
        [GameBalance("spec §6.3: shakedown 0 / 60 / 90 / 120 d")]
        public int ShakedownLongDays { get; init; } = 120;

        /// <summary>The shakedowns the player may choose (days), none first.</summary>
        public int[] ShakedownOptions() => new[] { 0, ShakedownShortDays, ShakedownMediumDays, ShakedownLongDays };

        /// <summary>A hidden defect reveals itself within this many days of the first stocking: 365 (cycle 1).</summary>
        [Range(30, 730)]
        [GameBalance("spec §3.3: each defect has a reveal day in cycle 1")]
        public int Cycle1RevealWindowDays { get; init; } = 365;

        /// <summary>Days a defect's fix takes (it fires until fixed): 30.</summary>
        [Range(1, 365)]
        [GameBalance("a yard team on board for a month per fix")]
        public int FixDays { get; init; } = 30;

        /// <summary>Extra mortality over a cycle while the harvest-zone crush defect fires (percentage points): 3.</summary>
        [Range(0.0, 20.0)]
        [Source("O:R3")]
        public double HarvestZoneCrushPp { get; init; } = 3.0;

        /// <summary>Fewest extra mortality points of "Biologien flytter ikke med" (winter ulcers): 4.</summary>
        [Range(0.0, 30.0)]
        [GameBalance("spec §3.3: winter ulcers +4-8 pp, after O:R26")]
        public double BiologyPpMin { get; init; } = 4.0;

        /// <summary>Most extra mortality points of winter ulcers: 8.</summary>
        [Range(0.0, 30.0)]
        [GameBalance("spec §3.3: winter ulcers +4-8 pp, after O:R26")]
        public double BiologyPpMax { get; init; } = 8.0;

        /// <summary>Structural fatigue multiplier while the cable-chain wear defect fires: 2.</summary>
        [Range(1.0, 10.0)]
        [GameBalance("cable-chain wear (O:R3) doubles the structure's fatigue until fixed")]
        public double CableChainFatigueMultiplier { get; init; } = 2.0;

        /// <summary>Net wear multiplier while the net-system defect fires: 2.</summary>
        [Range(1.0, 10.0)]
        [GameBalance("the net system was changed on OF1 (O:R4); an unfixed one wears twice as fast")]
        public double NetSystemWearMultiplier { get; init; } = 2.0;

        /// <summary>Tilt hazard multiplier while the ballast-pipe leak is unfixed (F2-08 reads it): 3.</summary>
        [Range(1.0, 10.0)]
        [GameBalance("spec §3.3: teething ×3 while the ballast-pipe leak is unfixed")]
        public double BallastTiltMultiplier { get; init; } = 3.0;
    }

    #endregion

    /// <summary>The sea lane's section of the facilities calibration (F2-06/F2-07; facilities_offshore.json).</summary>
    public sealed partial record FacilitiesCalibration
    {
        /// <summary>
        /// The offshore section (sim·sea, <c>facilities_offshore.json</c>): the
        /// metocean and operability (F2-06), the plots' sea, and the unit classes,
        /// costs, logistics, structure, sensors, alarm load and teething deck (F2-07).
        /// </summary>
        [JsonPropertyName("offshore")]
        public OffshoreCalibration Offshore { get; init; } = new();
    }
}
