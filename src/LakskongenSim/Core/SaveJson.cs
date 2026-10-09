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
        IncludeFields = true
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
}