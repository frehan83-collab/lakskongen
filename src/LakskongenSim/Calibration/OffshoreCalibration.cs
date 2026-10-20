using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using LakskongenSim.Environment;

namespace LakskongenSim.Calibration
{
    #region F2-06 offshore metocean, forecast and operability

    // Every leaf below documents its value, unit, a "Range:" and exactly one of
    // "Source:" (a dossier key of docs/research/FARM_TYPES_RESEARCH.md, [O:x] or
    // [V:n], or a cited publication with year and URL) or "Game balance:" (a value
    // chosen for the game, with the acceptance test that decides it). Monthly and
    // per-weather-state values sit in the containers MonthlyValues and
    // WeatherStateValues; the property that holds a container documents its
    // range and provenance for all of its values.
    // OffshoreCalibrationTests.EveryLeaf_DocumentsRangeAndOneProvenance reads the
    // doc comments with Roslyn and holds that rule. The F2-06 wiring stage (F2-07
    // branch) adds F2-04's machine-checked attributes on top: every leaf carries a
    // [Range] and exactly one [Source] or [GameBalance], and every container is a
    // record of its own (OffshoreCalibrationContainers.cs) whose twelve months or
    // four states carry the holding property's range and provenance, so
    // CalibrationGuardTests' reflection guard walks the whole section
    // (facilities.offshore, FacilitiesCalibration.Offshore).
    //
    // Sources cited by name below:
    // - NORCE 2020: Ø. Johnsen, "MariCulture Smart Fishfarm, Metocean Design
    //   Basis, Haltenbanken II", NORCE report, project 103211, 12.11.2020: NORA10
    //   hindcast 1958-2019, 3-hourly, Haltenbanken area A (63.86 °N, 6.46 °E,
    //   221 m). Published by Fiskeridirektoratet with the hearing on the
    //   Norskehavet aquaculture site: https://fiskeridir.no/hoeringer/horing-om-klarering-av-lokalitet-for-akvakultur-i-norskehavet
    // - [O:R17] AOF summary report 1, Fiskeridirektoratet 2024 (site mean Hs 0.9 m,
    //   maximum 4.15 m; barge inaccessible 92 days in ~25 months, 62 % of them in
    //   January-March).
    // - [O:R6] NTNU thesis on OF1 (2018): OF1's 100-year design sea state Hs 5 m.
    // - [O:R37] Menon publication 131/2023: crowding advised below Hs 2 m,
    //   wellboats operate offshore to about 5 m.

    /// <summary>
    /// Calibration of the offshore sea (roadmap F2-06, docs/FARM_TYPES_SPEC.md
    /// §3.3 "Metocean" and the operability table, §3.9 facilities_offshore.json):
    /// the base sea and the storm swell the metocean model draws, the honest
    /// forecast's error growth, visibility, the operation limits, and the offshore
    /// plots' 100-year sea states with their fitted scales. Loaded from
    /// <c>godot/data/calibration/facilities_offshore.json</c> by F2-04's include
    /// loader; until then <see cref="Parse"/> reads the draft and the code
    /// defaults are the values the file holds. F2-07 adds the unit classes,
    /// their logistics, costs, structure, sensors, alarm load and teething deck
    /// (their records in <c>OffshoreUnitCalibration.cs</c>); the incident,
    /// licence and area sections of the spec's draft belong to F2-08..F2-11.
    /// </summary>
    public sealed partial record OffshoreCalibration
    {
        /// <summary>The base sea, the storm swell, the forecast and visibility.</summary>
        public OffshoreMetoceanCalibration Metocean { get; init; } = new();

        /// <summary>Operation limits by vessel, feeding appetite in heavy seas, helicopter limits.</summary>
        public OffshoreOperabilityCalibration Operability { get; init; } = new();

        /// <summary>The offshore plots' 100-year sea states and fitted Hs scales.</summary>
        public OffshorePlotsCalibration Plots { get; init; } = new();

        /// <summary>F2-07: the unit classes (OF1-class, Havfarm-class): contract, build phases, silo, crew.</summary>
        public OffshoreUnitClassesCalibration Units { get; init; } = new();

        /// <summary>F2-07: vessel charter, tugs, crew pay, energy, maintenance, insurance, the consortium buy-out.</summary>
        public OffshoreCostsCalibration Costs { get; init; } = new();

        /// <summary>F2-07: the logistics rhythm (crew rota, silo and silage warnings, helicopter fallback).</summary>
        public OffshoreLogisticsCalibration Logistics { get; init; } = new();

        /// <summary>F2-07: structure fatigue, net wear and their repair.</summary>
        public OffshoreStructureCalibration Structure { get; init; } = new();

        /// <summary>F2-07: the sensor packages (install, service).</summary>
        public OffshoreSensorsCalibration Sensors { get; init; } = new();

        /// <summary>F2-07: the alarm load's terms (spec §3.3; F2-08 adds the review).</summary>
        public OffshoreAlarmLoadCalibration AlarmLoad { get; init; } = new();

        /// <summary>F2-07: the teething deck and the shakedown (spec §3.3).</summary>
        public OffshoreTeethingCalibration Teething { get; init; } = new();

        /// <summary>The code defaults (the values the draft JSON holds).</summary>
        public static OffshoreCalibration Default { get; } = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        /// <summary>
        /// Read <c>facilities_offshore.json</c>. Keys missing from the file keep
        /// their code defaults; F2-04's include loader replaces this call.
        /// </summary>
        public static OffshoreCalibration Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            return JsonSerializer.Deserialize<OffshoreCalibration>(json, JsonOptions) ?? new OffshoreCalibration();
        }
    }

    /// <summary>
    /// The offshore sea model (spec §3.3 "Metocean"): a base sea on an AR(1) in
    /// the sea transform (lognormal below 1 m, a Box-Cox power above) coupled to the fjord weather chain, a queue of storm-swell
    /// events known 4-7 days ahead, and visibility. The reference climate is the
    /// sea at scale 1; a plot's sea is its <see cref="OffshorePlotCalibration.HsScale"/>
    /// times the reference. A day's Hs is the day's highest 3-hour significant
    /// wave height: the sea state a booked operation must clear, and the one
    /// whose annual maxima give the design 100-year level.
    /// </summary>
    public sealed record OffshoreMetoceanCalibration
    {
        /// <summary>
        /// Day-to-day persistence φ of the base sea's AR(1) residual (unit
        /// 1/day). 0.75. Range: 0.5-0.95. Game balance: spec §3.3; with the
        /// weather chain and the swell queue the reference sea's lag-1
        /// autocorrelation of ln Hs is 0.80 (docs/BALANCE.md §1q).
        /// </summary>
        [Range(0.5, 0.95)]
        [GameBalance("spec §3.3 AR(1) persistence; lag-1 autocorrelation of ln Hs 0.80 with the weather chain and swell (docs/BALANCE.md §1q)")]
        public double ArPhi { get; init; } = 0.75;

        /// <summary>
        /// Innovation standard deviation σ_ε of the AR(1) residual (unit transform
        /// units, ln units up to 1 m). 0.30. Range: 0.1-0.6. Game balance:
        /// spec §3.3; F2-06 acceptance 2 (share of days above 2 m) holds with it.
        /// </summary>
        [Range(0.1, 0.6)]
        [GameBalance("spec §3.3 AR(1) innovation; F2-06 acceptance 2 (share of days above 2 m) holds with it")]
        public double ArSigma { get; init; } = 0.30;

        /// <summary>
        /// Power λ of the sea transform above 1 m for the base sea (unitless;
        /// <see cref="Environment.MetoceanMath.UpperBoxCox"/>: ln h up to 1 m,
        /// (h^λ − 1)/λ above, so the sea is lognormal below 1 m and its upper
        /// tail thins as λ rises). 0.4. Range: 0-1. Game balance: with
        /// <see cref="SwellBoxCoxLambda"/> it sets the 100-year level to about
        /// 1.3 × the median annual maximum, the ratio of the NORA10 Weibull fit at
        /// Haltenbanken (16.2 m against an annual 1-year value of 11.6 m, NORCE
        /// 2020 Table 3.4); the spec's lognormal (λ = 0) gave 1.85 and failed
        /// F2-06 acceptance 1 and 3. A Box-Cox power over the whole range
        /// bottoms out at 0 and needed a floor that bound on 4 % of days; the
        /// lognormal lower piece needs none.
        /// </summary>
        [Range(0.0, 1.0)]
        [GameBalance("sea transform above 1 m: the 100-year level at about 1.3 x the median annual maximum, as NORA10 at Haltenbanken (NORCE 2020 Table 3.4); F2-06 acceptance 1 and 3")]
        public double BaseBoxCoxLambda { get; init; } = 0.4;

        /// <summary>
        /// Background swell (unit m Hs, reference scale): the swell from distant
        /// Atlantic storms that keeps the outer coast from falling flat, added to
        /// every day's sea in energy (√(base² + background² + swell²)). 0.34.
        /// Range: 0-0.6. Game balance: fitted to NORA10's calm end at
        /// Haltenbanken (NORCE 2020 Table 3.2: 3-hour sea states lie below 0.19
        /// × the annual mean 0.18 % of the time and below 0.38 × it 7.2 %; the
        /// 3-parameter Weibull locations of Table 3.4 sit at 0.68-1.05 m, 0.26-
        /// 0.40 × the annual mean, in every month). A day's peak lies about 10 %
        /// above its 3-hour values, so the reference sea's days below 0.385 × the
        /// mean land between 2 % and 7.2 % (4.9 %). Without it the lognormal base
        /// put 2.4 % of days below 0.19 × the mean and 19 % below 0.385 ×.
        /// </summary>
        [Range(0.0, 0.6)]
        [GameBalance("fitted to NORA10's calm end at Haltenbanken (NORCE 2020 Table 3.2)")]
        public double BackgroundSwellHsM { get; init; } = 0.34;

        /// <summary>
        /// Shift Δ of the base sea by the fjord weather state (unit transform
        /// units): Calm −0.30, Moderate 0, Storm +0.55, Severe +0.85. It couples
        /// the offshore sea to the weather chain so the two never visibly
        /// disagree. Range: -1-1.5. Game balance: spec §3.3.
        /// </summary>
        public WeatherDeltaValues WeatherDelta { get; init; } = new()
        {
            Calm = -0.30, Moderate = 0.0, Storm = 0.55, Severe = 0.85,
        };

        /// <summary>
        /// Monthly level μ of the base sea in the sea transform (unit transform
        /// units), January first. Range: -3-1. Game balance: fitted (the test
        /// OffshoreMetoceanFitTests.ShippedCalibration_IsTheFitResult reruns the
        /// fit on seeds 1001-1100 × 50 years) so the reference sea's monthly mean
        /// Hs is <see cref="ReferenceAnnualMeanHsM"/> × the monthly shape of
        /// <see cref="FitMonthlyMeanHsM"/>.
        /// </summary>
        public BaseMuMonthValues BaseMuMonth { get; init; } = MonthlyValues.Of<BaseMuMonthValues>(
            -0.524, -0.508, -0.608, -0.804, -1.104, -1.093, -1.281, -1.223, -0.765, -0.636, -0.665, -0.492);

        /// <summary>
        /// Annual mean of the reference sea's daily Hs (unit m). 1.0. Range:
        /// 0.5-2. Game balance: [O:R17]'s measured mean of 0.9 m at the AOF site
        /// raised to the day's highest 3-hour sea state (a day's peak lies about
        /// 10 % above the day's mean); off_1 lands at its fitted scale × 1.0 m
        /// (F2-06 acceptance 1: 0.8-1.1 m).
        /// </summary>
        [Range(0.5, 2.0)]
        [GameBalance("O:R17's 0.9 m site mean raised to the day's highest 3-hour sea; F2-06 acceptance 1")]
        public double ReferenceAnnualMeanHsM { get; init; } = 1.0;

        /// <summary>
        /// Monthly mean Hs whose shape (each month over the day-weighted annual
        /// mean) the base level is fitted to (unit m), January first: 3.7, 3.6,
        /// 3.2, 2.4, 1.8, 1.7, 1.5, 1.6, 2.3, 2.9, 3.2, 3.7. Range: 0.5-6.
        /// Source: NORCE 2020, Table 3.2 (NORA10 1958-2019, Haltenbanken A):
        /// January and December 1.42 × the annual mean, July 0.58 ×; the spec's
        /// off_1 anchors (January ≈ 1.4 m, July ≈ 0.55 m at a 0.9 m mean) follow
        /// the same shape.
        /// </summary>
        public FitMonthlyMeanHsValues FitMonthlyMeanHsM { get; init; } = MonthlyValues.Of<FitMonthlyMeanHsValues>(
            3.7, 3.6, 3.2, 2.4, 1.8, 1.7, 1.5, 1.6, 2.3, 2.9, 3.2, 3.7);

        /// <summary>
        /// Daily probability that a storm-swell event is created, by the month
        /// of the creation day (unit 1/day), January first: .10 .09 .08 .05 .03
        /// .02 .02 .03 .05 .07 .09 .10 (about 22 events a year). Range: 0-0.3.
        /// Game balance: spec §3.3; F2-06 acceptance 2 (8-14 % of days above 2 m,
        /// 20-35 % in January-March) holds with it.
        /// </summary>
        public SwellRateValues SwellRatePerDay { get; init; } = MonthlyValues.Of<SwellRateValues>(
            0.10, 0.09, 0.08, 0.05, 0.03, 0.02, 0.02, 0.03, 0.05, 0.07, 0.09, 0.10);

        /// <summary>
        /// Median peak Hs of a swell event created in January (unit m, reference
        /// scale); other months scale it by <see cref="SwellMedianShape1YearHsM"/>.
        /// 2.2. Range: 1-5. Game balance: the spec's 2.2 m median, kept as the
        /// January value; F2-06 acceptance 1 (median annual maximum) holds with it.
        /// </summary>
        [Range(1.0, 5.0)]
        [GameBalance("spec §3.3's 2.2 m swell median, kept as January's; F2-06 acceptance 1")]
        public double SwellMedianJanuaryM { get; init; } = 2.2;

        /// <summary>
        /// Monthly 1-year extreme Hs whose ratio to January scales the swell
        /// median by month (unit m), January first: 10.3, 9.8, 8.7, 7.2, 6.0, 5.7,
        /// 4.5, 5.4, 7.5, 8.4, 9.9, 10.2. Range: 3-15. Source: NORCE 2020, Table
        /// 3.4 (monthly Weibull extremes at Haltenbanken A, 1-year return
        /// period): summer storms peak at 0.44-0.55 × January's.
        /// </summary>
        public SwellMedianShapeValues SwellMedianShape1YearHsM { get; init; } = MonthlyValues.Of<SwellMedianShapeValues>(
            10.3, 9.8, 8.7, 7.2, 6.0, 5.7, 4.5, 5.4, 7.5, 8.4, 9.9, 10.2);

        /// <summary>
        /// Spread of a swell event's peak around its month's median (unit ln
        /// units at the median). 0.20. Range: 0.05-0.6. Game balance: the spec's
        /// 0.30 narrowed with the seasonal medians, so the share of days above
        /// 2 m and the median annual maximum stay in F2-06 acceptance 1-2.
        /// </summary>
        [Range(0.05, 0.6)]
        [GameBalance("spec §3.3's 0.30 narrowed with the seasonal medians; F2-06 acceptance 1-2")]
        public double SwellSigmaLn { get; init; } = 0.20;

        /// <summary>
        /// Power λ of the sea transform above 1 m for a swell event's peak (unitless). 0.8. Range: 0-1. Game
        /// balance: thins the peaks' upper tail like <see cref="BaseBoxCoxLambda"/>
        /// (F2-06 acceptance 1 and 3).
        /// </summary>
        [Range(0.0, 1.0)]
        [GameBalance("thins the swell peaks' upper tail like the base sea (F2-06 acceptance 1 and 3)")]
        public double SwellBoxCoxLambda { get; init; } = 0.8;

        /// <summary>
        /// Fewest days between an event's creation and its onset (unit days): an
        /// event is in the forecast at least this long before it arrives. 4.
        /// Range: 1-14. Game balance: spec §3.3 (F2-06 acceptance 4); swell from
        /// distant storms is forecast days ahead (J.-R. Bidlot, "Twenty-one years
        /// of wave forecast verification", ECMWF Newsletter 150, 2017,
        /// doi:10.21957/t4znuhb842).
        /// </summary>
        [Range(1, 14)]
        [GameBalance("spec §3.3: a swell event is forecast at least 4 days ahead (F2-06 acceptance 4)")]
        public int SwellLeadDaysMin { get; init; } = 4;

        /// <summary>Most days between an event's creation and its onset (unit days). 7. Range: 1-14. Game balance: spec §3.3.</summary>
        [Range(1, 14)]
        [GameBalance("spec §3.3: onset 4-7 days after creation")]
        public int SwellLeadDaysMax { get; init; } = 7;

        /// <summary>Shortest plateau of an event at its peak (unit days). 1. Range: 1-10. Game balance: spec §3.3.</summary>
        [Range(1, 10)]
        [GameBalance("spec §3.3: plateau 1-4 days")]
        public int SwellDurationDaysMin { get; init; } = 1;

        /// <summary>Longest plateau of an event at its peak (unit days). 4. Range: 1-10. Game balance: spec §3.3.</summary>
        [Range(1, 10)]
        [GameBalance("spec §3.3: plateau 1-4 days")]
        public int SwellDurationDaysMax { get; init; } = 4;

        /// <summary>
        /// Swell on the ramp day before and the ramp day after the plateau, as a
        /// share of the peak (unitless). 0.5. Range: 0-1. Game balance: spec §3.3
        /// (a 1-day ramp up and a 1-day ramp down).
        /// </summary>
        [Range(0.0, 1.0)]
        [GameBalance("spec §3.3: a 1-day ramp up and a 1-day ramp down at half the peak")]
        public double SwellRampFraction { get; init; } = 0.5;

        /// <summary>
        /// Shortest peak period of a swell event (unit s). 9. Range: 5-20.
        /// Source: NORCE 2020, Table 3.7: the mean Tp of 1-2 m seas at
        /// Haltenbanken is 8.4-9.5 s, its 95th percentile 12.8-13.8 s.
        /// </summary>
        [Range(5.0, 20.0)]
        [Source("NORCE 2020 Table 3.7")]
        public double SwellTpMinS { get; init; } = 9.0;

        /// <summary>Longest peak period of a swell event (unit s). 14. Range: 5-20. Game balance: spec §3.3 (9-14 s).</summary>
        [Range(5.0, 20.0)]
        [GameBalance("spec §3.3: swell peak periods 9-14 s")]
        public double SwellTpMaxS { get; init; } = 14.0;

        /// <summary>
        /// Wind-sea peak period per √Hs (unit s/√m): Tp = max(this × √Hs, swell
        /// Tp). 4.5. Range: 3-6. Game balance: spec §3.3; a developing sea, below
        /// the 5.0 of a fully developed Pierson-Moskowitz sea (derived from
        /// Pierson and Moskowitz 1964: Hs = 0.21 U²/g, ω_p = 0.877 g/U).
        /// </summary>
        [Range(3.0, 6.0)]
        [GameBalance("spec §3.3: a developing sea, below Pierson-Moskowitz 1964's fully developed 5.0")]
        public double WindSeaTpPerSqrtHs { get; init; } = 4.5;

        /// <summary>
        /// Lead (unit days) at which a known event's peak estimate has its full
        /// error, the prior spread; below it the error shrinks in proportion and
        /// is gone at the onset. 4. Range: 1-10. Game balance: spec §3.3
        /// (σ_ln·min(1, k/4)).
        /// </summary>
        [Range(1, 10)]
        [GameBalance("spec §3.3: the peak estimate's error scales by min(1, lead / 4)")]
        public int ForecastFullErrorLeadDays { get; init; } = 4;

        /// <summary>Days ahead the forecast covers (unit days). 10. Range: 3-16. Game balance: spec §3.3.</summary>
        [Range(3, 16)]
        [GameBalance("spec §3.3: a 10-day forecast")]
        public int ForecastHorizonDays { get; init; } = 10;

        /// <summary>
        /// Share of days with visibility below <see cref="LowVisibilityKm"/>, by
        /// month (unitless), January first: snow showers in winter, sea fog in
        /// summer. Range: 0-0.2. Game balance: anchored to ship reports north of
        /// 62 °N, where fog is reported in under 3 % of observations, most in
        /// June-August (ICOADS 1950-2007, "Marine fog over northern Europe",
        /// AMS 97th Annual Meeting 2017, https://ams.confex.com/ams/97Annual/webprogram/Paper307556.html);
        /// a low-visibility day counts any low hour in the flying window.
        /// </summary>
        public LowVisibilityShareValues LowVisibilityDayShare { get; init; } = MonthlyValues.Of<LowVisibilityShareValues>(
            0.05, 0.05, 0.04, 0.03, 0.03, 0.05, 0.06, 0.05, 0.03, 0.03, 0.04, 0.05);

        /// <summary>Visibility on the worst low-visibility day (unit km). 0.2. Range: 0-1.5. Game balance: low days spread evenly up to the threshold.</summary>
        [Range(0.0, 1.5)]
        [GameBalance("low-visibility days spread evenly up to the threshold")]
        public double LowVisibilityMinKm { get; init; } = 0.2;

        /// <summary>
        /// Visibility that separates low and clear days (unit km). 1.5. Range:
        /// 0.5-5. Game balance: the spec's helicopter minimum (§3.3 operability table).
        /// </summary>
        [Range(0.5, 5.0)]
        [GameBalance("spec §3.3's helicopter minimum visibility")]
        public double LowVisibilityKm { get; init; } = 1.5;

        /// <summary>Visibility on the clearest day (unit km). 50. Range: 10-100. Game balance: clear days spread evenly from the threshold up to it.</summary>
        [Range(10.0, 100.0)]
        [GameBalance("clear days spread evenly from the threshold up to it")]
        public double ClearVisibilityMaxKm { get; init; } = 50.0;

        /// <summary>The swell median of a month (m, reference scale).</summary>
        public double SwellMedianM(int monthIndex)
            => SwellMedianJanuaryM * SwellMedianShape1YearHsM[monthIndex] / SwellMedianShape1YearHsM[0];

        /// <summary>The fit target: the reference sea's mean daily Hs in a month (m).</summary>
        public double ReferenceMonthlyMeanHsM(int monthIndex)
        {
            double weighted = 0.0;
            for (int m = 0; m < 12; m++)
                weighted += FitMonthlyMeanHsM[m] * Core.SimCalendar.DaysInMonth(m);
            return ReferenceAnnualMeanHsM * FitMonthlyMeanHsM[monthIndex] / (weighted / Core.SimCalendar.DaysPerYear);
        }
    }

    /// <summary>
    /// Operation limits (spec §3.3 operability table): an operation is possible
    /// on a day when the plot's Hs is at or below its limit for the vessel used.
    /// </summary>
    public sealed record OffshoreOperabilityCalibration
    {
        /// <summary>
        /// Fish handling (crowding, delousing, harvest, stocking, transfer) with a
        /// standard or DP vessel (unit m Hs). 2.0. Range: 1-4. Source: [O:R37]
        /// (Menon 131/2023, citing Heskestad): crowding is advised only below Hs 2 m.
        /// </summary>
        [Range(1.0, 4.0)]
        [Source("O:R37")]
        public double FishHandlingHsM { get; init; } = 2.0;

        /// <summary>Fish handling from an offshore wellboat (unit m Hs). 2.5. Range: 1-4. Game balance: spec §3.3.</summary>
        [Range(1.0, 4.0)]
        [GameBalance("spec §3.3: an offshore wellboat handles fish to 2.5 m")]
        public double FishHandlingOffshoreWellboatHsM { get; init; } = 2.5;

        /// <summary>
        /// A wellboat alongside or in transit; pumping pauses above the handling
        /// limit (unit m Hs). 5.0. Range: 3-8. Source: [O:R37] (Menon 131/2023):
        /// wellboats operate offshore to about Hs 5 m.
        /// </summary>
        [Range(3.0, 8.0)]
        [Source("O:R37")]
        public double WellboatAlongsideHsM { get; init; } = 5.0;

        /// <summary>
        /// Boarding and crew change from a standard vessel (unit m Hs). 2.0.
        /// Range: 1-4. Game balance: spec §3.3, fitted to [O:R17]'s barge
        /// inaccessibility (about 12 % of days, 32 % of January-March days).
        /// </summary>
        [Range(1.0, 4.0)]
        [GameBalance("spec §3.3, fitted to O:R17's barge inaccessibility")]
        public double BoardingHsM { get; init; } = 2.0;

        /// <summary>Boarding with DP and walk-to-work (unit m Hs). 3.0. Range: 1-5. Game balance: spec §3.3.</summary>
        [Range(1.0, 5.0)]
        [GameBalance("spec §3.3: DP and walk-to-work boarding to 3 m")]
        public double BoardingDpHsM { get; init; } = 3.0;

        /// <summary>Net inspection by ROV from the unit's crane (unit m Hs). 3.0. Range: 1-5. Game balance: spec §3.3.</summary>
        [Range(1.0, 5.0)]
        [GameBalance("spec §3.3: ROV from the unit's crane to 3 m")]
        public double NetInspectionHsM { get; init; } = 3.0;

        /// <summary>Resupply and silage from a standard vessel (unit m Hs). 2.5. Range: 1-5. Game balance: spec §3.3.</summary>
        [Range(1.0, 5.0)]
        [GameBalance("spec §3.3: resupply and silage from a standard vessel to 2.5 m")]
        public double ResupplyHsM { get; init; } = 2.5;

        /// <summary>Resupply and silage with DP (unit m Hs). 3.0. Range: 1-5. Game balance: spec §3.3.</summary>
        [Range(1.0, 5.0)]
        [GameBalance("spec §3.3: resupply and silage with DP to 3 m")]
        public double ResupplyDpHsM { get; init; } = 3.0;

        /// <summary>A tow, held for <see cref="TowHoldDays"/> (unit m Hs). 2.5. Range: 1-5. Game balance: spec §3.3.</summary>
        [Range(1.0, 5.0)]
        [GameBalance("spec §3.3: a tow needs 2.5 m held for its days")]
        public double TowHsM { get; init; } = 2.5;

        /// <summary>Days a tow needs the sea at or below its limit (unit days). 3. Range: 1-10. Game balance: spec §3.3.</summary>
        [Range(1, 10)]
        [GameBalance("spec §3.3: a tow holds its sea state for 3 days")]
        public int TowHoldDays { get; init; } = 3;

        /// <summary>
        /// Hs up to which the fish eat their full ration (unit m). 4.0. Range:
        /// 2-8. Game balance: spec §3.3; OF1 lost no feeding days to weather [O:R7].
        /// </summary>
        [Range(2.0, 8.0)]
        [GameBalance("spec §3.3; OF1 lost no feeding days to weather (O:R7)")]
        public double AppetiteFullHsM { get; init; } = 4.0;

        /// <summary>Hs at and above which appetite stays at <see cref="AppetiteAtFloor"/> (unit m). 6.0. Range: 3-12. Game balance: spec §3.3.</summary>
        [Range(3.0, 12.0)]
        [GameBalance("spec §3.3: appetite falls linearly to its floor at 6 m")]
        public double AppetiteFloorHsM { get; init; } = 6.0;

        /// <summary>Appetite in the heaviest seas (unitless share of the ration). 0.7. Range: 0-1. Game balance: spec §3.3.</summary>
        [Range(0.0, 1.0)]
        [GameBalance("spec §3.3: 0.7 of the ration in the heaviest seas")]
        public double AppetiteAtFloor { get; init; } = 0.7;

        /// <summary>Highest wind a crew-change helicopter flies in (unit m/s). 25. Range: 10-35. Game balance: spec §3.3.</summary>
        [Range(10.0, 35.0)]
        [GameBalance("spec §3.3: a crew-change helicopter flies in wind under 25 m/s")]
        public double HelicopterMaxWindMs { get; init; } = 25.0;

        /// <summary>Lowest visibility a crew-change helicopter flies in (unit km). 1.5. Range: 0.5-5. Game balance: spec §3.3.</summary>
        [Range(0.5, 5.0)]
        [GameBalance("spec §3.3: a crew-change helicopter needs 1.5 km visibility")]
        public double HelicopterMinVisibilityKm { get; init; } = 1.5;
    }

    /// <summary>The offshore plots of the procedural fjord (spec §4: off_1 "Ytre kyst", off_2 "Havkanten", hav_1 "Havområde").</summary>
    public sealed record OffshorePlotsCalibration
    {
        /// <summary>The open-ocean plot hav_1, outside the production areas.</summary>
        [JsonPropertyName("hav_1")]
        public OffshorePlotCalibration Hav1 { get; init; } = new() { Hs100M = 13.0, HsScale = 2.752 };

        /// <summary>The outer-coast plot off_1.</summary>
        [JsonPropertyName("off_1")]
        public OffshorePlotCalibration Off1 { get; init; } = new() { Hs100M = 5.0, HsScale = 1.058 };

        /// <summary>The exposed outer-coast plot off_2.</summary>
        [JsonPropertyName("off_2")]
        public OffshorePlotCalibration Off2 { get; init; } = new() { Hs100M = 6.5, HsScale = 1.376 };

        /// <summary>The plot record of <paramref name="plotId"/> ("off_1", "off_2", "hav_1").</summary>
        public OffshorePlotCalibration For(string plotId) => plotId switch
        {
            "off_1" => Off1,
            "off_2" => Off2,
            "hav_1" => Hav1,
            _ => throw new ArgumentOutOfRangeException(nameof(plotId), plotId, "Unknown offshore plot"),
        };

        /// <summary>The plot ids, nearest the coast first.</summary>
        public static readonly string[] PlotIds = { "off_1", "off_2", "hav_1" };
    }

    /// <summary>One offshore plot's sea (spec §3.2 SitePlot.Hs100M and PlotHsScale).</summary>
    public sealed record OffshorePlotCalibration
    {
        /// <summary>
        /// The plot's 100-year significant wave height (unit m): off_1 5.0, off_2
        /// 6.5, hav_1 13. Range: 2-20. Source: [O:R6] (OF1's 100-year design Hs
        /// 5 m at its Frohavet site) for off_1; off_2 after AOF's approved 6.7 m
        /// [O:R19]; hav_1 inferred from the open-sea design's 100-year storm
        /// ([O:R12], [O:R29]), below NORA10's 16.2 m at Haltenbanken (NORCE 2020).
        /// </summary>
        [Range(2.0, 20.0)]
        [Source("O:R6", "O:R19", "O:R12", "O:R29", "NORCE 2020")]
        public double Hs100M { get; init; }

        /// <summary>
        /// The plot's sea as a multiple of the reference sea (unitless). Range:
        /// 0.3-5. Game balance: fitted (OffshoreMetoceanFitTests reruns it on
        /// seeds 1001-1100 × 50 years) so the plot's empirical 100-year level, the
        /// 99th percentile of annual maxima, equals <see cref="Hs100M"/> (F2-06
        /// acceptance 3).
        /// </summary>
        [Range(0.3, 5.0)]
        [GameBalance("fitted so the plot's empirical 100-year level equals its Hs100 (F2-06 acceptance 3; OffshoreMetoceanFitTests)")]
        public double HsScale { get; init; } = 1.0;
    }

    /// <summary>
    /// Twelve monthly values, January first (JSON keys m01-m12 sort in calendar
    /// order). Abstract: each use is a record of its own
    /// (<c>OffshoreCalibrationContainers.cs</c>) whose months carry the holding
    /// property's [Range] and provenance, so F2-04's reflection guard checks
    /// every month against the right range.
    /// </summary>
    public abstract record MonthlyValues
    {
        public abstract double M01 { get; init; }
        public abstract double M02 { get; init; }
        public abstract double M03 { get; init; }
        public abstract double M04 { get; init; }
        public abstract double M05 { get; init; }
        public abstract double M06 { get; init; }
        public abstract double M07 { get; init; }
        public abstract double M08 { get; init; }
        public abstract double M09 { get; init; }
        public abstract double M10 { get; init; }
        public abstract double M11 { get; init; }
        public abstract double M12 { get; init; }

        /// <summary>The value of month <paramref name="monthIndex"/> (0 = January).</summary>
        public double this[int monthIndex] => monthIndex switch
        {
            0 => M01, 1 => M02, 2 => M03, 3 => M04, 4 => M05, 5 => M06,
            6 => M07, 7 => M08, 8 => M09, 9 => M10, 10 => M11, 11 => M12,
            _ => throw new ArgumentOutOfRangeException(nameof(monthIndex), monthIndex, "Month index must be 0-11"),
        };

        /// <summary>The values as an array, January first.</summary>
        public double[] ToArray() => new[] { M01, M02, M03, M04, M05, M06, M07, M08, M09, M10, M11, M12 };

        /// <summary>Twelve values of the container <typeparamref name="T"/>, January first.</summary>
        public static T Of<T>(params double[] v) where T : MonthlyValues, new()
        {
            if (v == null || v.Length != 12) throw new ArgumentException("Twelve monthly values are needed", nameof(v));
            return new T
            {
                M01 = v[0], M02 = v[1], M03 = v[2], M04 = v[3], M05 = v[4], M06 = v[5],
                M07 = v[6], M08 = v[7], M09 = v[8], M10 = v[9], M11 = v[10], M12 = v[11],
            };
        }
    }

    /// <summary>
    /// One value per fjord weather state. Abstract like
    /// <see cref="MonthlyValues"/>: each use is its own record whose states carry
    /// the holding property's [Range] and provenance.
    /// </summary>
    public abstract record WeatherStateValues
    {
        public abstract double Calm { get; init; }
        public abstract double Moderate { get; init; }
        public abstract double Storm { get; init; }
        public abstract double Severe { get; init; }

        /// <summary>The value of <paramref name="state"/>.</summary>
        public double this[WeatherState state] => state switch
        {
            WeatherState.Calm => Calm,
            WeatherState.Moderate => Moderate,
            WeatherState.Storm => Storm,
            WeatherState.Severe => Severe,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown weather state"),
        };
    }

    #endregion
}
