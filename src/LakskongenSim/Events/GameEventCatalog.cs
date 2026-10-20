using System;
using System.Collections.Generic;
using System.Linq;

namespace LakskongenSim.Events
{
    /// <summary>
    /// How loud an event is (roadmap U2-06: one channel per severity).
    /// Critical pauses the game behind a card with actions, Warning is an
    /// inbox item with a badge and one toast, Info is written to the log only.
    /// </summary>
    public enum GameEventSeverity
    {
        Info = 0,
        Warning = 1,
        Critical = 2
    }

    /// <summary>When an event has to be answered by (F2-05 reads it for its deadline rule).</summary>
    public enum GameEventDeadlineRule
    {
        /// <summary>No deadline: <see cref="GameEvent.DeadlineDay"/> is -1.</summary>
        None = 0,

        /// <summary>The publisher knows the last day (a window that closes, an order's due day).</summary>
        SetByPublisher = 1
    }

    /// <summary>
    /// Every kind of event the game reports. One typed value per situation, so
    /// nothing downstream has to read the alert text to know what it is about
    /// (U2-06 acceptance 1). Conditions are reported by the snapshot layer
    /// while they hold; incidents are reported by the engine when they happen.
    /// </summary>
    public enum GameEventType
    {
        // ── Conditions (they hold for a while) ──────────────────────────────
        LiceAboveLimit,
        LiceRising,
        OxygenCritical,
        OxygenLow,
        NetsCritical,
        NetsWorn,
        StormForecast,
        MortalityHigh,
        AppetiteLow,
        FeedConversionPoor,
        VaccineWindowOpen,
        VaccineCoverageLow,
        WellboatScheduled,
        AlgalBloom,
        GillHealthCritical,
        GillHealthPoor,
        SustainabilityLow,
        TemperatureHigh,

        // ── Incidents (they happen on a day) ────────────────────────────────
        Escape,
        StormDamage,
        LiceTreatmentDue,
        TrafficLightRed,
        TrafficLightChanged,
        SolvencyOverdraft,
        SolvencyBankWarning,
        SolvencyFinalWarning,
        InsurancePayout,
        WellboatCompleted,
        WellboatReturned,
        StoryEvent,

        // ── Standing orders (they hold until answered or enforced; U2-07) ──
        LiceTreatmentOrder,
        BiomassOrder,

        // ── Growth-licence auctions (U2-01) ─────────────────────────────────
        LicenceOutbid,
        LicenceAuctionWon,
        LicenceAuctionLost,

        // ── Liquidity ladder (U2-03) ─────────────────────────────────────────
        LiquidityWarning,
        BankCall,
        RescueLoanTaken,

        // ── Farm types (docs/FARM_TYPES_SPEC.md §3.5; declared by F2-05, ──
        // ── published by the lanes; each carries UnitId) ─────────────────
        [FarmTypeEvent] LandPowerOutageUncovered,
        [FarmTypeEvent] H2sAcute,
        [FarmTypeEvent] TiltEvent,
        [FarmTypeEvent] NetTear,
        [FarmTypeEvent] MooringFailure,
        [FarmTypeEvent] SiteApprovalWithdrawn,
        [FarmTypeEvent] MattilsynetOrder,
        [FarmTypeEvent] HandlingOverdue,
        [FarmTypeEvent] LandO2Low,
        [FarmTypeEvent] LoxShortage,
        [FarmTypeEvent] H2sChronic,
        [FarmTypeEvent] BiofilterDegraded,
        [FarmTypeEvent] OffFlavourDetected,
        [FarmTypeEvent] MaturationRising,
        [FarmTypeEvent] GrowthShortfall,
        [FarmTypeEvent] DischargeNearCap,
        [FarmTypeEvent] SiloLow,
        [FarmTypeEvent] CrewOverdue,
        [FarmTypeEvent] StormSwellForecast,
        [FarmTypeEvent] CapexOverrun,
        [FarmTypeEvent] ConversionPaymentDue,
        [FarmTypeEvent] PermitDecision,
        [FarmTypeEvent] ConstructionPhaseComplete,
        [FarmTypeEvent] TowWindowLikely,
        [FarmTypeEvent] DevLicenceRoundOpened,
        [FarmTypeEvent] DevLicenceAwarded,
        [FarmTypeEvent] ConversionDecision,
        [FarmTypeEvent] TeethingDefectRevealed,
        [FarmTypeEvent] OutageCovered,
        [FarmTypeEvent] DrillCompleted,
        [FarmTypeEvent] OffshoreAreaRoundOpened,

        // ── The Driftsplan (F2-05): a standing order could not do its job ──
        [FarmTypeEvent] DriftsplanCommandFailed
    }

    /// <summary>
    /// Marks a <see cref="GameEventType"/> of the farm-types work (F2,
    /// docs/FARM_TYPES_SPEC.md §3.5). Every marked type is declared in the
    /// catalog through its farm-type row, which names its severity, its
    /// deadline rule and its counter verb (or "none") with no default
    /// (F2-05 acceptance 3; a reflection test pins it).
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class FarmTypeEventAttribute : Attribute
    {
    }

    /// <summary>
    /// What a type is: its stable id (the bridge and GDScript use it), the
    /// category events are deduplicated by, its default severity, the verb
    /// that answers it ("none" when there is nothing to do) and its deadline
    /// rule.
    /// </summary>
    public sealed record GameEventTypeInfo(
        GameEventType Type,
        string Id,
        string Category,
        GameEventSeverity Severity,
        string CounterVerb,
        GameEventDeadlineRule Deadline)
    {
        /// <summary>True for a farm-type row: severity, deadline rule and verb were each declared, none defaulted (F2-05).</summary>
        public bool DeclaredForFarmTypes { get; init; }
    }

    /// <summary>The single table of event types (one row per <see cref="GameEventType"/>).</summary>
    public static class GameEventCatalog
    {
        /// <summary>The counter verb of an event nothing can be done about.</summary>
        public const string NoCounterVerb = "none";

        // Counter verbs: what the player can do about an event. The UI maps
        // each to the place the verb lives (a tab, a site, the camera).
        public const string VerbDelouse = "delouse";
        public const string VerbFreshwaterBath = "freshwater_bath";
        public const string VerbInspectNets = "inspect_nets";
        public const string VerbCheckWater = "check_water";
        public const string VerbReviewFeeding = "review_feeding";
        public const string VerbRaiseCapital = "raise_capital";
        public const string VerbOpenSustainability = "open_sustainability";
        public const string VerbInspectSite = "inspect_site";
        public const string VerbBidLicence = "bid_licence";

        // Farm-type verbs (F2-05; spec §3.4-§3.5, §6.6): each names the place
        // the counter lives, the offshore unit's board or the land site's.
        public const string VerbBookOperation = "book_operation";
        public const string VerbRepairUnit = "repair_unit";
        public const string VerbReviewAlarms = "review_alarms";
        public const string VerbFixDefect = "fix_defect";
        public const string VerbStartTow = "start_tow";
        public const string VerbPayConversion = "pay_conversion";
        public const string VerbResolveOutage = "resolve_outage";
        public const string VerbAdjustWater = "adjust_water";
        public const string VerbOrderOxygen = "order_oxygen";
        public const string VerbStartPurge = "start_purge";
        public const string VerbBuildCompetence = "build_competence";
        public const string VerbOpenDriftsplan = "open_driftsplan";

        /// <summary>Every verb a type may declare.</summary>
        public static readonly IReadOnlyList<string> CounterVerbs = new[]
        {
            NoCounterVerb, VerbDelouse, VerbFreshwaterBath, VerbInspectNets, VerbCheckWater,
            VerbReviewFeeding, VerbRaiseCapital, VerbOpenSustainability, VerbInspectSite,
            VerbBookOperation, VerbRepairUnit, VerbReviewAlarms, VerbFixDefect, VerbStartTow,
            VerbPayConversion, VerbResolveOutage, VerbAdjustWater, VerbOrderOxygen, VerbStartPurge,
            VerbBuildCompetence, VerbOpenDriftsplan, VerbBidLicence
        };

        private static readonly GameEventTypeInfo[] Rows =
        {
            Row(GameEventType.LiceAboveLimit, "lice_above_limit", "lice", GameEventSeverity.Critical, VerbDelouse),
            Row(GameEventType.LiceRising, "lice_rising", "lice", GameEventSeverity.Warning, VerbDelouse),
            Row(GameEventType.OxygenCritical, "oxygen_critical", "oxygen", GameEventSeverity.Critical, VerbCheckWater),
            Row(GameEventType.OxygenLow, "oxygen_low", "oxygen", GameEventSeverity.Warning, VerbCheckWater),
            Row(GameEventType.NetsCritical, "nets_critical", "nets", GameEventSeverity.Critical, VerbInspectNets),
            Row(GameEventType.NetsWorn, "nets_worn", "nets", GameEventSeverity.Warning, VerbInspectNets),
            Row(GameEventType.StormForecast, "storm_forecast", "storm_forecast", GameEventSeverity.Warning, VerbInspectNets),
            Row(GameEventType.MortalityHigh, "mortality_high", "mortality", GameEventSeverity.Warning, VerbCheckWater),
            Row(GameEventType.AppetiteLow, "appetite_low", "appetite", GameEventSeverity.Warning, VerbReviewFeeding),
            Row(GameEventType.FeedConversionPoor, "feed_conversion_poor", "feed_conversion", GameEventSeverity.Warning, VerbReviewFeeding),
            Row(GameEventType.VaccineWindowOpen, "vaccine_window_open", "vaccination", GameEventSeverity.Warning, NoCounterVerb, GameEventDeadlineRule.SetByPublisher),
            Row(GameEventType.VaccineCoverageLow, "vaccine_coverage_low", "vaccination", GameEventSeverity.Info, NoCounterVerb),
            Row(GameEventType.WellboatScheduled, "wellboat_scheduled", "wellboat", GameEventSeverity.Info, NoCounterVerb),
            Row(GameEventType.AlgalBloom, "algal_bloom", "bloom", GameEventSeverity.Warning, VerbInspectSite),
            Row(GameEventType.GillHealthCritical, "gill_health_critical", "gill", GameEventSeverity.Critical, VerbFreshwaterBath),
            Row(GameEventType.GillHealthPoor, "gill_health_poor", "gill", GameEventSeverity.Warning, VerbFreshwaterBath),
            Row(GameEventType.SustainabilityLow, "sustainability_low", "sustainability", GameEventSeverity.Warning, VerbOpenSustainability),
            Row(GameEventType.TemperatureHigh, "temperature_high", "temperature", GameEventSeverity.Warning, VerbCheckWater),

            Row(GameEventType.Escape, "escape", "escape", GameEventSeverity.Critical, VerbInspectNets),
            Row(GameEventType.StormDamage, "storm_damage", "storm", GameEventSeverity.Info, VerbInspectNets),
            Row(GameEventType.LiceTreatmentDue, "lice_treatment_due", "lice", GameEventSeverity.Warning, VerbDelouse),
            Row(GameEventType.TrafficLightRed, "traffic_light_red", "traffic_light", GameEventSeverity.Warning, VerbDelouse),
            Row(GameEventType.TrafficLightChanged, "traffic_light_changed", "traffic_light", GameEventSeverity.Info, NoCounterVerb),
            Row(GameEventType.SolvencyOverdraft, "solvency_overdraft", "solvency", GameEventSeverity.Warning, VerbRaiseCapital),
            Row(GameEventType.SolvencyBankWarning, "solvency_bank_warning", "solvency", GameEventSeverity.Critical, VerbRaiseCapital),
            Row(GameEventType.SolvencyFinalWarning, "solvency_final_warning", "solvency", GameEventSeverity.Critical, VerbRaiseCapital),
            Row(GameEventType.InsurancePayout, "insurance_payout", "insurance", GameEventSeverity.Info, NoCounterVerb),
            Row(GameEventType.WellboatCompleted, "wellboat_completed", "wellboat", GameEventSeverity.Info, NoCounterVerb),
            Row(GameEventType.WellboatReturned, "wellboat_returned", "wellboat", GameEventSeverity.Warning, VerbInspectSite),
            Row(GameEventType.StoryEvent, "story_event", "story", GameEventSeverity.Info, NoCounterVerb),

            Row(GameEventType.LiceTreatmentOrder, "lice_treatment_order", "lice_order", GameEventSeverity.Warning, VerbDelouse, GameEventDeadlineRule.SetByPublisher),
            Row(GameEventType.BiomassOrder, "biomass_order", "biomass_order", GameEventSeverity.Warning, VerbInspectSite, GameEventDeadlineRule.SetByPublisher),

            Row(GameEventType.LicenceOutbid, "licence_outbid", "licence_auction", GameEventSeverity.Warning, VerbBidLicence, GameEventDeadlineRule.SetByPublisher),
            Row(GameEventType.LicenceAuctionWon, "licence_auction_won", "licence_auction", GameEventSeverity.Info, NoCounterVerb),
            Row(GameEventType.LicenceAuctionLost, "licence_auction_lost", "licence_auction", GameEventSeverity.Info, NoCounterVerb),

            Row(GameEventType.LiquidityWarning, "liquidity_warning", "liquidity", GameEventSeverity.Warning, VerbRaiseCapital, GameEventDeadlineRule.SetByPublisher),
            Row(GameEventType.BankCall, "bank_call", "liquidity", GameEventSeverity.Critical, VerbRaiseCapital, GameEventDeadlineRule.SetByPublisher),
            Row(GameEventType.RescueLoanTaken, "rescue_loan_taken", "rescue_loan", GameEventSeverity.Info, NoCounterVerb),

            // Farm types (spec §3.5). A deadline the publisher sets is the
            // day the counter stops working: the order's or the appeal's due
            // day, the day the silo, the oxygen or the discharge cap runs out,
            // the swell's onset, the projected acute H2S day, the conversion
            // payment's last day, the planned harvest an off-flavour must be
            // purged before. Categories pair a warning with the critical it
            // escalates to (handling, h2s), so the bus delivers it as one story.
            F2Row(GameEventType.LandPowerOutageUncovered, "land_power_outage_uncovered", "land_power", GameEventSeverity.Critical, VerbResolveOutage, GameEventDeadlineRule.None),
            F2Row(GameEventType.H2sAcute, "h2s_acute", "h2s", GameEventSeverity.Critical, VerbAdjustWater, GameEventDeadlineRule.None),
            F2Row(GameEventType.TiltEvent, "tilt_event", "tilt", GameEventSeverity.Critical, VerbReviewAlarms, GameEventDeadlineRule.None),
            F2Row(GameEventType.NetTear, "net_tear", "net_tear", GameEventSeverity.Critical, VerbRepairUnit, GameEventDeadlineRule.None),
            F2Row(GameEventType.MooringFailure, "mooring_failure", "mooring", GameEventSeverity.Critical, VerbRepairUnit, GameEventDeadlineRule.None),
            F2Row(GameEventType.SiteApprovalWithdrawn, "site_approval_withdrawn", "site_approval", GameEventSeverity.Critical, VerbBookOperation, GameEventDeadlineRule.SetByPublisher),
            F2Row(GameEventType.MattilsynetOrder, "mattilsynet_order", "handling", GameEventSeverity.Critical, VerbBookOperation, GameEventDeadlineRule.SetByPublisher),
            F2Row(GameEventType.HandlingOverdue, "handling_overdue", "handling", GameEventSeverity.Warning, VerbBookOperation, GameEventDeadlineRule.SetByPublisher),
            F2Row(GameEventType.LandO2Low, "land_o2_low", "land_oxygen", GameEventSeverity.Warning, VerbAdjustWater, GameEventDeadlineRule.None),
            F2Row(GameEventType.LoxShortage, "lox_shortage", "lox", GameEventSeverity.Warning, VerbOrderOxygen, GameEventDeadlineRule.SetByPublisher),
            F2Row(GameEventType.H2sChronic, "h2s_chronic", "h2s", GameEventSeverity.Warning, VerbAdjustWater, GameEventDeadlineRule.SetByPublisher),
            F2Row(GameEventType.BiofilterDegraded, "biofilter_degraded", "biofilter", GameEventSeverity.Warning, VerbAdjustWater, GameEventDeadlineRule.None),
            F2Row(GameEventType.OffFlavourDetected, "off_flavour_detected", "off_flavour", GameEventSeverity.Warning, VerbStartPurge, GameEventDeadlineRule.SetByPublisher),
            F2Row(GameEventType.MaturationRising, "maturation_rising", "maturation", GameEventSeverity.Warning, VerbAdjustWater, GameEventDeadlineRule.None),
            F2Row(GameEventType.GrowthShortfall, "growth_shortfall", "growth_shortfall", GameEventSeverity.Warning, VerbBuildCompetence, GameEventDeadlineRule.None),
            F2Row(GameEventType.DischargeNearCap, "discharge_near_cap", "discharge", GameEventSeverity.Warning, VerbAdjustWater, GameEventDeadlineRule.SetByPublisher),
            F2Row(GameEventType.SiloLow, "silo_low", "silo", GameEventSeverity.Warning, VerbBookOperation, GameEventDeadlineRule.SetByPublisher),
            F2Row(GameEventType.CrewOverdue, "crew_overdue", "crew", GameEventSeverity.Warning, VerbBookOperation, GameEventDeadlineRule.None),
            F2Row(GameEventType.StormSwellForecast, "storm_swell_forecast", "storm_swell", GameEventSeverity.Warning, VerbBookOperation, GameEventDeadlineRule.SetByPublisher),
            F2Row(GameEventType.CapexOverrun, "capex_overrun", "construction_cost", GameEventSeverity.Warning, NoCounterVerb, GameEventDeadlineRule.None),
            F2Row(GameEventType.ConversionPaymentDue, "conversion_payment_due", "conversion_payment", GameEventSeverity.Warning, VerbPayConversion, GameEventDeadlineRule.SetByPublisher),
            F2Row(GameEventType.PermitDecision, "permit_decision", "permit", GameEventSeverity.Info, NoCounterVerb, GameEventDeadlineRule.None),
            F2Row(GameEventType.ConstructionPhaseComplete, "construction_phase_complete", "construction", GameEventSeverity.Info, NoCounterVerb, GameEventDeadlineRule.None),
            F2Row(GameEventType.TowWindowLikely, "tow_window_likely", "tow", GameEventSeverity.Info, VerbStartTow, GameEventDeadlineRule.None),
            F2Row(GameEventType.DevLicenceRoundOpened, "dev_licence_round_opened", "dev_licence_round", GameEventSeverity.Info, NoCounterVerb, GameEventDeadlineRule.None),
            F2Row(GameEventType.DevLicenceAwarded, "dev_licence_awarded", "dev_licence", GameEventSeverity.Info, NoCounterVerb, GameEventDeadlineRule.None),
            F2Row(GameEventType.ConversionDecision, "conversion_decision", "conversion", GameEventSeverity.Info, NoCounterVerb, GameEventDeadlineRule.None),
            F2Row(GameEventType.TeethingDefectRevealed, "teething_defect_revealed", "teething", GameEventSeverity.Info, VerbFixDefect, GameEventDeadlineRule.None),
            F2Row(GameEventType.OutageCovered, "outage_covered", "land_power", GameEventSeverity.Info, NoCounterVerb, GameEventDeadlineRule.None),
            F2Row(GameEventType.DrillCompleted, "drill_completed", "drill", GameEventSeverity.Info, NoCounterVerb, GameEventDeadlineRule.None),
            F2Row(GameEventType.OffshoreAreaRoundOpened, "offshore_area_round_opened", "offshore_area_round", GameEventSeverity.Info, NoCounterVerb, GameEventDeadlineRule.None),

            F2Row(GameEventType.DriftsplanCommandFailed, "driftsplan_command_failed", "driftsplan", GameEventSeverity.Warning, VerbOpenDriftsplan, GameEventDeadlineRule.None),
        };

        private static readonly Dictionary<GameEventType, GameEventTypeInfo> ByType =
            Rows.ToDictionary(r => r.Type);

        private static readonly Dictionary<string, GameEventTypeInfo> ById =
            Rows.ToDictionary(r => r.Id, StringComparer.Ordinal);

        /// <summary>All types, in declaration order.</summary>
        public static IReadOnlyList<GameEventTypeInfo> All => Rows;

        /// <summary>The row of a type (every enum value has one; a test pins it).</summary>
        public static GameEventTypeInfo Describe(GameEventType type) =>
            ByType.TryGetValue(type, out var info)
                ? info
                : throw new ArgumentOutOfRangeException(nameof(type), type, "GameEventType without a catalog row");

        /// <summary>The type with this stable id ("lice_above_limit"), or false.</summary>
        public static bool TryParse(string id, out GameEventType type)
        {
            if (id != null && ById.TryGetValue(id, out var info))
            {
                type = info.Type;
                return true;
            }
            type = default;
            return false;
        }

        /// <summary>"info" | "warning" | "critical" (the bridge's spelling).</summary>
        public static string SeverityId(GameEventSeverity severity) => severity switch
        {
            GameEventSeverity.Critical => "critical",
            GameEventSeverity.Warning => "warning",
            _ => "info"
        };

        /// <summary>Parses the bridge's spelling of a severity.</summary>
        public static bool TryParseSeverity(string id, out GameEventSeverity severity)
        {
            switch (id)
            {
                case "critical": severity = GameEventSeverity.Critical; return true;
                case "warning": severity = GameEventSeverity.Warning; return true;
                case "info": severity = GameEventSeverity.Info; return true;
                default: severity = GameEventSeverity.Info; return false;
            }
        }

        private static GameEventTypeInfo Row(GameEventType type, string id, string category,
            GameEventSeverity severity, string verb, GameEventDeadlineRule deadline = GameEventDeadlineRule.None)
            => new(type, id, category, severity, verb, deadline);

        /// <summary>A farm-type row: every column is declared, nothing defaults (F2-05 acceptance 3).</summary>
        private static GameEventTypeInfo F2Row(GameEventType type, string id, string category,
            GameEventSeverity severity, string verb, GameEventDeadlineRule deadline)
            => new(type, id, category, severity, verb, deadline) { DeclaredForFarmTypes = true };
    }
}
