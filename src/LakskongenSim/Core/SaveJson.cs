using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("LakskongenSim.Tests")]

namespace LakskongenSim.Core;

/// <summary>
/// The ONE save-serialization format (Phase 1 wave 5). Both the Godot game
/// (SaveLoadManager wrapper) and the headless library (SaveLoadManager,
/// PlayerScriptProbe) read and write through these options so "game saves" and
/// "probe saves" can no longer drift apart.
///
/// Backward compatibility is a property of the OPTIONS, not a migration
/// step for the payload:
/// - PropertyNameCaseInsensitive: legacy saves written with
///   JsonNamingPolicy.CamelCase still bind to the PascalCase properties.
/// - JsonStringEnumConverter: writes enum NAMES; on read it also accepts
///   the legacy numeric enum values (SaveVersion 1/2 wrote ints).
/// - IncludeFields mirrors the old game-side behavior.
/// </summary>
public static class SaveJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        IgnoreReadOnlyProperties = false,
        IncludeFields = true,
        // NaN/Infinity (e.g. an uncalibrated R²) serialize as named
        // literals and read back identically on both sides.
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };
}

/// <summary>
/// Engine RUNTIME state that must survive a save so a loaded game continues
/// instead of resetting cadence/edges (Phase 1 wave 5):
/// - The interval clocks of the in-day tick systems (weekly/quarterly
///   cadence must not double-fire after a load),
/// - the storm-impact edge state (a mid-episode load must not re-settle
///   the same storm's insurance claim),
/// - the randomized-events gating flag.
///
/// RNG POSITIONS are deliberately NOT here yet: legacy System.Random
/// subsystems (Weather/Growth/Genetics/Market/Storyteller) cannot report a
/// replayable position — they migrate onto SimRng streams together with
/// the bit-equivalence golden tests, at which point THIS snapshot gains a
/// per-stream position table (documented gap, NEXT_LEVEL_PLAN).
/// </summary>
public sealed class EngineRuntimeSnapshot
{
    /// <summary>Serialized represenentation of the storm-impact edge state.</summary>
    public string WeatherEdgeState { get; set; } = "Calm";

    /// <summary>Highest storm severity reached in the running storm episode
    /// (S1-02); null in saves written before episodes were tracked.</summary>
    public double? StormEpisodePeakSeverity { get; set; }

    /// <summary>The randomized-events gating flag of the engine.</summary>
    public bool EventsEnabled { get; set; }

    /// <summary>
    /// Interval clocks of the in-day tick systems, aligned with the
    /// registration order of <c>SimulationEngine._inDayPhases</c>. Empty
    /// (or length-mismatched) snapshots fall back to the clockless restore.
    /// </summary>
    public int[] InDayClocks { get; set; } = System.Array.Empty<int>();

    /// <summary>
    /// Identity-keyed clock map (system type name -> clock). Preferred over
    /// the positional <see cref="InDayClocks"/> array so inserting/reordering
    /// tick phases in a future commit can never restore a clock onto the
    /// wrong system (post-review hardening).
    /// </summary>
    public System.Collections.Generic.Dictionary<string, int>? ClocksBySystem { get; set; }

    // ── RNG + stateful-model positions (Phase 1 wave 6) ───────────────────
    /// <summary>System.Random-compatible stream positions, keyed by subsystem
    /// name ("weather", "growth", "events", ...). Each value is 58 ints.</summary>
    public System.Collections.Generic.Dictionary<string, int[]>? RngPositions { get; set; }

    /// <summary>SimRng named-stream positions (stream key -> SplitMix64 state).</summary>
    public System.Collections.Generic.Dictionary<string, ulong>? SimStreamPositions { get; set; }

    /// <summary>PriceCouncil rolling weekly-spot history.</summary>
    public decimal[]? PriceCouncilSpots { get; set; }

    /// <summary>PriceCouncil bound monthly price at save time.</summary>
    public decimal PriceCouncilMonthly { get; set; }

    /// <summary>Storyteller last-event day (sentinel -9999 = never/legacy).</summary>
    public int StorytellerLastEventDay { get; set; } = -9999;

    /// <summary>Storyteller per-event cooldowns (day available again).</summary>
    public System.Collections.Generic.Dictionary<string, int>? StorytellerCooldowns { get; set; }

    /// <summary>True when currency state (below) was captured — legacy saves
    /// have zeros everywhere and must not be imported as real rates.</summary>
    public bool CurrencyStatePresent { get; set; }

    public int CurrencyDay { get; set; }
    public decimal CurrencyEur { get; set; }
    public decimal CurrencyUsd { get; set; }
    public decimal CurrencyJpy { get; set; }
    public decimal CurrencyGbp { get; set; }
    public decimal CurrencyBankRate { get; set; } = 2.5m;

    // ── Financial statements (live P&L must survive a save) ───────────────
    public decimal FinRevenue { get; set; }
    public decimal FinCogs { get; set; }
    public decimal FinOpex { get; set; }
    public decimal FinInterest { get; set; }
    public decimal FinTaxes { get; set; }
    public decimal FinCash { get; set; }
    public decimal FinAssets { get; set; }
    public decimal FinLiabilities { get; set; }
    public decimal FinBiomassInventoryValue { get; set; }
    public int FinCurrentPeriod { get; set; }
    public System.Collections.Generic.List<string>? FinEvents { get; set; }
    public System.Collections.Generic.Dictionary<string, decimal>? FinCostBreakdown { get; set; }
    public System.Collections.Generic.Dictionary<string, decimal>? FinRevenueBreakdown { get; set; }
    /// <summary>Lifetime ledger totals (cash reconciliation); null in older saves.</summary>
    public LakskongenSim.Finance.LifetimeTotals? FinLifetime { get; set; }

    // ── Market simulation (price trajectory + supply state) ───────────────
    public decimal MktBase { get; set; }
    public decimal MktPrice { get; set; }
    public decimal MktWeeklyAvg { get; set; }
    public decimal MktLongMean { get; set; }
    public decimal MktMeanRev { get; set; }
    public decimal MktVol { get; set; }
    public decimal MktCertMult { get; set; } = 1.0m;
    public double MktSupplyIdx { get; set; } = 1.0;
    public double MktDemandIdx { get; set; } = 1.0;
    public double MktLastR2 { get; set; } = double.NaN;
    public int MktLastObs { get; set; }
    public int MktSimDay { get; set; }
    public System.Collections.Generic.List<LakskongenSim.Market.HistoricalPrice>? MktHistory { get; set; }
    public double MktPlayerPressure { get; set; }
    public int MktShockDays { get; set; }
    public double MktSupplyOffset { get; set; }

    /// <summary>
    /// The sustainability manager's live score and level. A storyteller
    /// regulatory penalty moves them between the weekly updates without
    /// touching the state's mirror (GameState.SustainabilityScore), so a load
    /// that rebuilt the manager from the mirror skipped the next level change
    /// and its price multiplier (po3 seeds 75 and 156 of 1-400 diverged after
    /// a day-40 save). Null in older saves: the mirror is used.
    /// </summary>
    public int? SustainabilityLiveScore { get; set; }

    /// <summary>The live certification level beside <see cref="SustainabilityLiveScore"/>; null in older saves.</summary>
    public LakskongenSim.Sustainability.CertificationLevel? SustainabilityLiveLevel { get; set; }

    // ── Market intelligence (weekly news/competitor state) ────────────────
    public int IntelWeek { get; set; }
    public System.Collections.Generic.List<LakskongenSim.Market.MarketNewsEvent>? IntelActive { get; set; }
    public System.Collections.Generic.List<LakskongenSim.Market.MarketNewsEvent>? IntelHistory { get; set; }
    public System.Collections.Generic.List<LakskongenSim.Market.CompetitorProducer>? IntelCompetitors { get; set; }
    public System.Collections.Generic.List<decimal>? IntelPriceHistory { get; set; }
    public double IntelBrandScore { get; set; } = 25.0;

    // ── Plankton bloom deck (per-zone intensity/crash state) ───────────────
    public System.Collections.Generic.Dictionary<string, LakskongenSim.Biology.PlanktonBloom.BloomState>? PlanktonBloomStates { get; set; }

    // ── AI feeding live state ─────────────────────────────────────────────
    public bool AiFeedInstalled { get; set; }
    public double AiFeedCalibration { get; set; } = 0.5;
    public int AiFeedDays { get; set; }
    public double AiFeedCameraUptime { get; set; } = 1.0;
    public double AiFeedAppetite { get; set; } = 0.7;
    public double AiFeedWaste { get; set; } = 0.05;
    public decimal AiFeedSavings { get; set; }
    public double AiFeedFcrMult { get; set; } = 1.0;
    public System.Collections.Generic.List<double>? AiFeedHistory { get; set; }
    public int IntelBrandGood { get; set; }
    public int IntelBrandTotal { get; set; }

    /// <summary>Traffic-light per-zone color cache (the once-per-change rule).</summary>
    public System.Collections.Generic.Dictionary<string, string>? TrafficLightColors { get; set; }

    // ── Weather live state ────────────────────────────────────────────────
    /// <summary>True when weather state (below) was captured — legacy saves
    /// restart the season/Markov chain as before.</summary>
    public bool WeatherStatePresent { get; set; }

    public int WeatherDay { get; set; }
    public double WeatherTempC { get; set; } = 8.0;
    public double WeatherWindMs { get; set; } = 5.0;
    public double WeatherWaveM { get; set; } = 0.5;
    public string WeatherStateName { get; set; } = "Moderate";

    // ── Shared sea-temperature offset (S2-02); legacy saves load as zero ──
    public double WeatherSeaAnomalyC { get; set; }
    public double WeatherEventAnomalyC { get; set; }
    public double WeatherDailyFluctuationC { get; set; }
    public double WeatherStormCoolingC { get; set; }

    // ── FeedSystem adaptive state ─────────────────────────────────────────
    public double FeedFcr { get; set; } = LakskongenSim.Calibration.CalibrationRegistry.Default.Feed.BaseFCR;
    public double FeedUsedKg { get; set; }
    public double FeedGrowthKg { get; set; }
    public double FeedLastAppetite { get; set; } = 1.0;
    public float FeedSustainabilityRating { get; set; } = 0.7f;
}