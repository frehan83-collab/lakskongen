using System.ComponentModel.DataAnnotations;

namespace LakskongenSim.Calibration
{
    #region F2-08 offshore incidents, handling overdue, the regulator, lice coupling

    // Roadmap F2-08 (docs/FARM_TYPES_SPEC.md §3.3 "OffshoreTickSystem": handling
    // requirement and overdue days, alarm load, structure and net, tilt chain,
    // lice and zones; §3.9 facilities_offshore.json "handling", "incidents",
    // "alarmLoad", "reinforcement"; §6.6). Every leaf carries F2-04's [Range]
    // and exactly one [Source] or [GameBalance] (CalibrationGuardTests walks
    // facilities.offshore). Sources cited by name:
    // - FFE 2019: fishfarmingexpert.com, "Ocean Farm 1 escape total worked out at
    //   16,000" (2019): the September 2018 tilt released about 16,000 salmon,
    //   counted only after the unit was harvested; a hatch left open after an
    //   inspection let water into a ballast tank system.
    // - iLaks 2026-07-09: ilaks.no, "Brukte ti millioner kroner på tiltak etter
    //   lakserømming - kan likevel få maksstraff": 8,421 salmon escaped through a
    //   7 m net tear at Reitholmen (May 2024); over NOK 10 M of recapture,
    //   monitoring of 51 rivers and sampling; the Directorate's 15 G fee (1.86 M).
    // - FFE 2017 Hydrolicer: fishfarmingexpert.com, "Salmon giant SalMar doubles
    //   Hydrolicer capacity" (2017): a reference capacity of 360 t of fish an hour.

    /// <summary>
    /// The handling requirement and the regulator's escalation (spec §3.3
    /// "Handling requirement and overdue days (the AOF lesson)"): when a
    /// requirement is raised, how long it may stand without a completed handling
    /// operation before the warning, Mattilsynet's order and the site-withdrawal
    /// case, and how a large unit is deloused zone by zone.
    /// </summary>
    public sealed record OffshoreHandlingCalibration
    {
        /// <summary>Days a requirement stands without a completed handling operation before the warning (HandlingOverdue): 21.</summary>
        [Range(1, 120)]
        [GameBalance("spec §3.3: 21 d warning, scaled from AOF's 'up to 3 months' without delousing or harvest (V:5)")]
        public int WarnDays { get; init; } = 21;

        /// <summary>Days before Mattilsynet orders the handling done (MattilsynetOrder): 45.</summary>
        [Range(1, 240)]
        [GameBalance("spec §3.3: 45 d order, scaled from AOF's 'up to 3 months' (V:5)")]
        public int OrderDays { get; init; } = 45;

        /// <summary>Days the order gives to act: 30 (the order's deadline is the withdrawal day).</summary>
        [Range(1, 120)]
        [GameBalance("spec §3.9 handling.orderActDays: the order to act within 30 days")]
        public int OrderActDays { get; init; } = 30;

        /// <summary>Days before the site-withdrawal case opens: 75 (the order day plus its 30 days to act).</summary>
        [Range(1, 365)]
        [GameBalance("spec §3.3: 75 d withdrawal case, scaled from AOF's 'up to 3 months' (V:5)")]
        public int WithdrawalDays { get; init; } = 75;

        /// <summary>
        /// Days from the withdrawal decision to the day it takes effect (the
        /// approval is lost, the fish must be out): 120, the time an appeal
        /// takes; an appeal is decided on that day.
        /// </summary>
        [Range(14, 730)]
        [GameBalance("spec §3.3: an appeal takes 120 d; AOF's withdrawal took effect at the year end after the decision (V:5)")]
        public int AppealDays { get; init; } = 120;

        /// <summary>Share of the unit's MTB the projected biomass may reach before MTB relief is required: 0.97.</summary>
        [Range(0.5, 1.0)]
        [GameBalance("spec §3.3: projected biomass above 97 % of MTB within 14 days")]
        public double MtbReliefShare { get; init; } = 0.97;

        /// <summary>Days ahead the MTB-relief requirement projects the biomass: 14.</summary>
        [Range(1, 60)]
        [GameBalance("spec §3.3: projected biomass above 97 % of MTB within 14 days")]
        public int MtbProjectionDays { get; init; } = 14;

        /// <summary>Days between net inspections the regulator expects: 90.</summary>
        [Range(7, 365)]
        [GameBalance("spec §3.9 handling.netInspectionIntervalDays")]
        public int NetInspectionIntervalDays { get; init; } = 90;

        /// <summary>Days past the interval before an overdue net inspection becomes a requirement: 30.</summary>
        [Range(0, 180)]
        [GameBalance("spec §3.3: net inspection overdue by 30 days")]
        public int NetInspectionOverdueDays { get; init; } = 30;

        /// <summary>
        /// Tonnes of fish a delousing wellboat treats on one handling day at an
        /// offshore unit: 2,000, a large Hydrolicer's reference 360 t an hour
        /// (FFE 2017 Hydrolicer) over about six pumping hours a window day, with
        /// crowding and set-up; a full OF1-class unit takes two to three days.
        /// </summary>
        [Range(100, 20_000)]
        [GameBalance("FFE 2017 Hydrolicer: 360 t an hour reference capacity, about six effective pumping hours in a window day")]
        public double DelousingTonnesPerHandlingDay { get; init; } = 2_000;
    }

    /// <summary>
    /// The incidents an offshore unit can have (spec §3.3 "Structure and net",
    /// "Tilt chain"): the tilt chain's hazard and branches, the mooring failure
    /// above the design sea, the net tear below 70 % integrity, jellyfish wound
    /// days, and what an escape costs. One uniform of the unit's day picks the
    /// day's incident by inverse CDF over the hazards (draw, then decide).
    /// </summary>
    public sealed record OffshoreIncidentsCalibration
    {
        /// <summary>
        /// The tilt hazard's base is one in this many days: 9,000, so h =
        /// (1/9000)·(2 − procedure)·(1 + 2·alarm load)·(1 + 0.05·crew overdue
        /// days)·teething, about 10 % a year in a teething year and 5 % once fixed.
        /// </summary>
        [Range(500, 100_000)]
        [GameBalance("spec §3.3: h = (1/9000)·…, about 10 %/yr in a teething year, 5 %/yr once fixed")]
        public double TiltBaseDays { get; init; } = 9_000;

        /// <summary>The procedure term is this minus the hatch procedure level: 2.0 (level 0.6 gives 1.4, level 1.0 gives 1.0).</summary>
        [Range(1.0, 5.0)]
        [GameBalance("spec §3.3: (2 − HatchProcedureLevel)")]
        public double TiltProcedureCeiling { get; init; } = 2.0;

        /// <summary>The alarm term is 1 + this × the alarm load: 2.0.</summary>
        [Range(0.0, 10.0)]
        [GameBalance("spec §3.3: (1 + 2·AlarmLoad); an overloaded crew silences alarm classes (O:R3: over 20,000 points)")]
        public double TiltAlarmFactor { get; init; } = 2.0;

        /// <summary>The crew term is 1 + this × the days the crew change is overdue: 0.05.</summary>
        [Range(0.0, 1.0)]
        [GameBalance("spec §3.3: (1 + 0.05·CrewOverdueDays)")]
        public double TiltPerCrewOverdueDay { get; init; } = 0.05;

        /// <summary>Shortest tilt that is not closed by the standing order (hours): 1.</summary>
        [Range(0.1, 48.0)]
        [GameBalance("spec §3.3: duration 1-24 h")]
        public double TiltDurationMinHours { get; init; } = 1.0;

        /// <summary>Longest tilt (hours): 24.</summary>
        [Range(1.0, 72.0)]
        [GameBalance("spec §3.3: duration 1-24 h")]
        public double TiltDurationMaxHours { get; init; } = 24.0;

        /// <summary>Share of the unit's fish a full tilt releases: 0.015 (the 2018 tilt released about 16,000 of some 1.0 M fish, FFE 2019; spec 1.5 %).</summary>
        [Range(0.0, 0.2)]
        [Source("O:R10", "FFE 2019")]
        public double TiltEscapeFraction { get; init; } = 0.015;

        /// <summary>Hours of tilt that release the full share (a shorter tilt releases its part): 12.</summary>
        [Range(1.0, 72.0)]
        [GameBalance("spec §3.3: escape = 1.5 % × min(1, duration_h / 12)")]
        public double TiltFullEscapeHours { get; init; } = 12.0;

        /// <summary>The hatch procedure level from which the standing order closes the hatches when the deviation is caught: 0.8.</summary>
        [Range(0.0, 1.0)]
        [GameBalance("spec §3.3: HatchProcedureLevel ≥ 0.8, crew on board and none overdue")]
        public double AutoCloseProcedureMin { get; init; } = 0.8;

        /// <summary>Hours a tilt lasts when the standing order closes the hatches: 1.</summary>
        [Range(0.1, 12.0)]
        [GameBalance("spec §3.3: the standing order ends the tilt within 1 h")]
        public double AutoCloseHours { get; init; } = 1.0;

        /// <summary>Daily mooring-failure hazard on a day the sea is above the unit's design Hs (with reinforcement): 0.02.</summary>
        [Range(0.0, 0.5)]
        [GameBalance("spec §3.3: a 2 % mooring-failure hazard on each day above design Hs")]
        public double MooringHazardOverDesign { get; init; } = 0.02;

        /// <summary>Largest share of the unit's fish a mooring failure releases: 0.10 (the draw sets the share up to it).</summary>
        [Range(0.0, 0.5)]
        [GameBalance("spec §3.3 and UX-FD4: the escape is capped at 10 %")]
        public double MooringEscapeMax { get; init; } = 0.10;

        /// <summary>Net integrity (%) below which a tear becomes possible: 70.</summary>
        [Range(0.0, 100.0)]
        [GameBalance("spec §3.3: a tear becomes possible below 70 %")]
        public double NetTearBelowPct { get; init; } = 70.0;

        /// <summary>
        /// Daily tear hazard at 0 % net integrity, rising linearly from 0 at the
        /// threshold: 0.05, so a net left at 35 % tears within about 40 days.
        /// </summary>
        [Range(0.0, 1.0)]
        [GameBalance("a neglected net tears within weeks; AOF lost fish through net damage (O:R18, O:R60b)")]
        public double NetTearHazardAtZero { get; init; } = 0.05;

        /// <summary>Fewest of the torn zone's fish a tear releases on its first day (share): 0.005.</summary>
        [Range(0.0, 0.2)]
        [GameBalance("iLaks 2026-07-09: 8,421 fish through a 7 m tear, a few percent of a pen")]
        public double NetTearEscapeMin { get; init; } = 0.005;

        /// <summary>Most of the torn zone's fish a tear releases on its first day (share): 0.03.</summary>
        [Range(0.0, 0.5)]
        [GameBalance("iLaks 2026-07-09: 8,421 fish through a 7 m tear, a few percent of a pen")]
        public double NetTearEscapeMax { get; init; } = 0.03;

        /// <summary>Share of the torn zone's fish that slip out each further day the tear is not mended: 0.002.</summary>
        [Range(0.0, 0.1)]
        [GameBalance("a torn net leaks until divers or the ROV crew mend it in a window")]
        public double NetTearLeakPerDay { get; init; } = 0.002;

        /// <summary>Share of November and December days with stinging jellyfish (Apolemia) at the unit: 0.05.</summary>
        [Range(0.0, 0.5)]
        [GameBalance("spec §3.3: jellyfish wound days on 5 % of days in Nov-Dec (O:R17, Apolemia at the AOF site)")]
        public double JellyDayShareNovDec { get; init; } = 0.05;

        /// <summary>Share of the unit's fish a jellyfish day kills (gill and skin wounds): 0.003.</summary>
        [Range(0.0, 0.1)]
        [GameBalance("O:R17: Apolemia wounds at the AOF site; a few per mille a day of stinging")]
        public double JellyDayMortality { get; init; } = 0.003;

        /// <summary>
        /// The Directorate's infringement fee per escaped fish (NOK), up to the
        /// 15 G ceiling (the capacity section's FOR-2013-12-20-1675 § 8): 221,
        /// the Reitholmen escape's 1.86 M over its 8,421 fish (iLaks 2026-07-09).
        /// </summary>
        [Range(0, 10_000)]
        [Source("iLaks 2026-07-09")]
        public decimal EscapeFeeNokPerFish { get; init; } = 221m;

        /// <summary>
        /// Recapture, river monitoring and sampling per escaped fish (NOK):
        /// 1,190, Lerøy's over NOK 10 M for 8,421 fish (iLaks 2026-07-09); OF1's
        /// 2018 escape brought monitoring of 13 rivers.
        /// </summary>
        [Range(0, 20_000)]
        [Source("iLaks 2026-07-09")]
        public decimal EscapeMeasuresNokPerFish { get; init; } = 1_190m;

        /// <summary>Days an incident stays under investigation at most before it closes (a net inspection closes it earlier): 30.</summary>
        [Range(1, 365)]
        [GameBalance("the ROV checks the nets after an incident; an investigation closes within a month")]
        public int InvestigationMaxDays { get; init; } = 30;
    }

    /// <summary>The alarm review (spec §3.3 "Counter-verbs: StartAlarmReview (4 M, 30 d)").</summary>
    public sealed record OffshoreAlarmReviewCalibration
    {
        /// <summary>Alarm load the completed review takes off: 0.30.</summary>
        [Range(0.0, 1.0)]
        [GameBalance("spec §3.3: − 0.30 · alarmReviewActive")]
        public double Cut { get; init; } = 0.30;

        /// <summary>Price of a review (NOK, the whole unit): 4,000,000.</summary>
        [Range(100_000, 100_000_000)]
        [GameBalance("spec §3.3: StartAlarmReview 4 M")]
        public decimal CostNok { get; init; } = 4_000_000m;

        /// <summary>Days the review takes before its cut applies: 30.</summary>
        [Range(1, 365)]
        [GameBalance("spec §3.3: StartAlarmReview 30 d")]
        public int Days { get; init; } = 30;

        /// <summary>Days the review's cut lasts once done (alarm classes drift back as the systems change): 365.</summary>
        [Range(30, 3_650)]
        [GameBalance("a yearly alarm review, as the crew re-tunes the alarm classes")]
        public int EffectDays { get; init; } = 365;
    }

    /// <summary>The hatch procedure upgrade (spec §3.4 StartProcedureUpgrade; §6.6 "the procedure unlocked").</summary>
    public sealed record OffshoreProceduresCalibration
    {
        /// <summary>Level one upgrade adds: 0.2 (0.6 → 0.8 reaches the standing order's 0.8).</summary>
        [Range(0.05, 1.0)]
        [GameBalance("one upgrade reaches the auto-close level 0.8; a second the full 1.0")]
        public double HatchStep { get; init; } = 0.2;

        /// <summary>Price of one upgrade (NOK, the whole unit): 2,000,000 (procedures, training, a hatch interlock).</summary>
        [Range(100_000, 50_000_000)]
        [GameBalance("procedure work, crew training and a hatch interlock; half an alarm review")]
        public decimal CostNok { get; init; } = 2_000_000m;

        /// <summary>Days one upgrade takes: 60.</summary>
        [Range(1, 365)]
        [GameBalance("two crew rotations to train every shift on the new procedure")]
        public int Days { get; init; } = 60;
    }

    /// <summary>"Forsterkning" (spec §3.9 reinforcement): design Hs + 1 m.</summary>
    public sealed record OffshoreReinforcementCalibration
    {
        /// <summary>Design Hs added (m): 1.0.</summary>
        [Range(0.1, 5.0)]
        [GameBalance("spec §3.9 reinforcement.designHsBonusM; OF1-class 5.5 + 1.0 reaches off_2's 6.5 m")]
        public double DesignHsBonusM { get; init; } = 1.0;

        /// <summary>Price (NOK, the whole unit): 60,000,000.</summary>
        [Range(1_000_000, 1_000_000_000)]
        [GameBalance("spec §3.9 reinforcement.capexNOK")]
        public decimal CapexNok { get; init; } = 60_000_000m;

        /// <summary>Days the yard work at the unit takes: 90.</summary>
        [Range(1, 730)]
        [GameBalance("a summer season of mooring and column work at the plot")]
        public int Days { get; init; } = 90;
    }

    /// <summary>
    /// How offshore units' lice reach each other and their area (spec §3.3
    /// "Lice and zones"): units in one production area seed each other's
    /// external pool outside the plot multiplier, and a unit's larval output
    /// counts in its area's fjord zones' assessment without its fish.
    /// </summary>
    public sealed record OffshoreLiceCouplingCalibration
    {
        /// <summary>Coupling of one unit's adult females to another unit in its production area: 0.08 (the engine's cross-zone coupling).</summary>
        [Range(0.0, 1.0)]
        [Source("code")]
        public double UnitToUnitCoupling { get; init; } = 0.08;

        /// <summary>Copepodid seed per unit of coupled adult females (the engine's farm-to-farm scale): 0.02.</summary>
        [Range(0.0, 1.0)]
        [Source("code")]
        public double SeedScale { get; init; } = 0.02;

        /// <summary>Largest seed a day (the engine's farm-to-farm cap): 0.05.</summary>
        [Range(0.0, 1.0)]
        [Source("code")]
        public double SeedCap { get; init; } = 0.05;

        /// <summary>
        /// Weight of a unit's larval output (its fish × adult females per fish)
        /// in a fjord zone's lice mean of its production area, without its fish
        /// in the denominator: 0.08 (the engine's cross-zone coupling).
        /// </summary>
        [Range(0.0, 1.0)]
        [Source("code")]
        public double ZoneOutputWeight { get; init; } = 0.08;
    }

    #endregion

    /// <summary>The sea lane's F2-08 sections of the offshore calibration (facilities_offshore.json).</summary>
    public sealed partial record OffshoreCalibration
    {
        /// <summary>F2-08: the handling requirement, the escalation and zone-by-zone delousing.</summary>
        public OffshoreHandlingCalibration Handling { get; init; } = new();

        /// <summary>F2-08: tilt, mooring failure, net tear, jellyfish, escape costs, investigation.</summary>
        public OffshoreIncidentsCalibration Incidents { get; init; } = new();

        /// <summary>F2-08: the alarm review.</summary>
        public OffshoreAlarmReviewCalibration AlarmReview { get; init; } = new();

        /// <summary>F2-08: the hatch procedure upgrade.</summary>
        public OffshoreProceduresCalibration Procedures { get; init; } = new();

        /// <summary>F2-08: "Forsterkning".</summary>
        public OffshoreReinforcementCalibration Reinforcement { get; init; } = new();

        /// <summary>F2-08: units' lice coupling and their larval output in their area's zones.</summary>
        public OffshoreLiceCouplingCalibration LiceCoupling { get; init; } = new();
    }
}
