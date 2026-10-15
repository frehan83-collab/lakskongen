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
        LicenceAuctionLost
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
        GameEventDeadlineRule Deadline);

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

        /// <summary>Every verb a type may declare.</summary>
        public static readonly IReadOnlyList<string> CounterVerbs = new[]
        {
            NoCounterVerb, VerbDelouse, VerbFreshwaterBath, VerbInspectNets, VerbCheckWater,
            VerbReviewFeeding, VerbRaiseCapital, VerbOpenSustainability, VerbInspectSite, VerbBidLicence
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
    }
}
