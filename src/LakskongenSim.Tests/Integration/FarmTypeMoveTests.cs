using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Farm;
using StateHashProbe;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// F2-02 (docs/FARM_TYPES_SPEC.md §7.1): the facility and containment tick code
/// leaves the two hot files and the farm-type hooks go in with neutral
/// defaults, and none of it may change the game. The shipped-scenario golden
/// traces (F2-01) prove that for the sea farms; they never build a facility,
/// so the land-tank, offshore and post-smolt ticks and the containment paths
/// are pinned here on the pre-F2 facilities save, as recorded on the commit
/// before the move (<see cref="PreMoveRecording"/>).
/// </summary>
public class FarmTypeMoveTests
{
    private readonly ITestOutputHelper _output;
    public FarmTypeMoveTests(ITestOutputHelper output) => _output = output;

    /// <summary>Days the pre-F2 facilities save runs on.</summary>
    private const int ContinuationDays = 365;

    /// <summary>Snapshot interval of the continuation (the golden traces' own).</summary>
    private const int SnapshotEveryDays = 30;

    /// <summary>
    /// Continuation recordings of the pre-F2 facilities save, made on the
    /// pre-move engine (this file's first commit, 2190137, on the wave-2 base 8c81754):
    /// SHA-256 (64 bit) over every day's golden-trace state hash and
    /// the GUID-normalized save every 30 days. F2-02's pure move kept them; F2-03
    /// turns the save's facilities into the F2 unit kinds on load, by design, so
    /// they are history now (RUN_LOG §7, F2-03): <see cref="F2_03Recording"/>
    /// pins the migrated continuation.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> PreMoveRecording = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["as-saved/events"] = "66412d201cf6141c",
        ["as-saved/no-events"] = "e4cfbd3cf6eb0d09",
        ["contained/events"] = "6e7519ba98447711",
    };

    /// <summary>
    /// The same continuations recorded on F2-03's engine (the save's offshore
    /// farm an offshore unit on off_1, its post-smolt facility a post-smolt land
    /// site on pad_delta, its RAS facility a land site on pad_a, the unit's
    /// carbon on its sea site's one tracker), hashed as
    /// <see cref="PreMoveRecording"/>. F2-04 changes the facilities' days on
    /// purpose (RUN_LOG §7, F2-04), so these are history now:
    /// <see cref="F2_04Recording"/> pins the continuation.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> F2_03Recording = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["as-saved/events"] = "fc222260db75d617",
        ["as-saved/no-events"] = "e06067233951965a",
        ["contained/events"] = "cc5ee2b0971b9fb1",
    };

    /// <summary>
    /// The same continuations recorded on F2-04's engine (wave3/sim-core-3),
    /// hashed as <see cref="PreMoveRecording"/>. What moved: (1) the save holds
    /// deep-sea-farming, whose lice and disease effects now reach the offshore
    /// unit only (spec §3.1 decision 0: F2-04 narrows the research effects), so
    /// the fjord sites and the land and post-smolt tanks lose them; (2) the save
    /// carries F2-04's additive fields (asset register, construction projects,
    /// permit cases, the ring-fenced costs). The year end's resource-rent base
    /// now leaves out the land sites' costs (the ring-fence, spec §3.6) but
    /// stays under the 70 M basic deduction either way, so no cash moves for
    /// it. No unit enters the asset register: their capex was paid before F2
    /// (spec §8). With the ring-fence alone (before the narrowing), recorded on
    /// the same branch, cash, fish and kilos were F2-03's and only the saved
    /// fields moved the hashes: 9f4737c4826751a4, bc190ab10a29ab81, 0fe99725a94520ca. A later
    /// item that changes the facility behaviour on purpose replaces these with
    /// a RUN_LOG §7 row, as F2-03 and F2-04 did. F2-05 changes no facility
    /// behaviour: these still hold for the continuation hashed without the
    /// save's (empty) Driftsplan, and <see cref="F2_05Recording"/> pins it with.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> F2_04Recording = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["as-saved/events"] = "3d9dccd403596921",
        ["as-saved/no-events"] = "a006d57aa0681740",
        ["contained/events"] = "aa0ff910f3e60c5a",
    };

    /// <summary>
    /// The same continuations on F2-05's engine (wave3/sim-core-4), the whole
    /// save included: the save gains two fields, the Driftsplan (empty here,
    /// the player set no standing order) and the market's daily prices in the
    /// runtime snapshot (the weekly mean and the price chart continue after a
    /// load), and nothing else moves: hashed with those two fields left out,
    /// each continuation is <see cref="F2_04Recording"/> exactly (RUN_LOG §7, F2-05).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> F2_05Recording = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["as-saved/events"] = "bbf23e44ace6d48b",
        ["as-saved/no-events"] = "5f6371de23d25003",
        ["contained/events"] = "9983e073a8fd8e98",
    };

    /// <summary>
    /// The same continuations on the wave-3 integration's engine after U2-04
    /// (wave3/ui-a-2 merged onto F2-05), the whole save included: every sea
    /// site gains U2-04's feeding plan (null here: nobody set one, so every
    /// pen eats the standard feed to full appetite) and nothing else moves:
    /// hashed with that field left out, each continuation is
    /// <see cref="F2_05Recording"/> exactly (RUN_LOG §7, wave-3 integration).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> U2_04Recording = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["as-saved/events"] = "3ce29f646bfed040",
        ["as-saved/no-events"] = "e4fe250d4ace92d1",
        ["contained/events"] = "16853d36a059f3ae",
    };

    /// <summary>
    /// The same continuations on the wave-3 integration's engine after U2-03
    /// (wave3/ui-b-2 merged onto U2-04), the whole save included: the save
    /// gains the liquidity ladder's state and the P&amp;L story, and nothing
    /// else moves: the facilities save never closes a day below zero, so no
    /// overdraft interest is booked and the bank never warns or calls (each
    /// asserted), and hashed with those two fields left out each continuation
    /// is <see cref="U2_04Recording"/> exactly (RUN_LOG §7, wave-3 integration).
    /// F2-06 and F2-07 change the offshore unit's days on purpose (RUN_LOG §7,
    /// F2-07), so these are history now: <see cref="F2_07Recording"/> pins the
    /// continuation and <see cref="UnmovedByF2_07Recording"/> what it left as it was.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> U2_03Recording = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["as-saved/events"] = "04ed7001cefa3469",
        ["as-saved/no-events"] = "f03695d59d7cce0f",
        ["contained/events"] = "fef57de957a6a41d",
    };

    /// <summary>
    /// The same continuations on F2-06/F2-07's engine (wave4/sim-sea-1), the
    /// whole save included. The save's offshore farm, migrated to an offshore
    /// unit on off_1, now lives in the open sea (F2-06: its own metocean
    /// stream, its plot's Hs every day) with F2-07's logistics: it is
    /// commissioned on load with a full silo and its Driftsplan defaults,
    /// draws its eight uniforms a day, books its crew, energy, maintenance and
    /// insurance and its charters, and its nets and structure wear. What moved
    /// is the money (cash, the ledger), the operations group (the unit's nets
    /// and structure) and the unit's own streams; every other traced group
    /// (weather and water, market, fish, lice, capacity, mortality, rivals)
    /// and every other stream is exactly the wave-3 integration's on every
    /// day: <see cref="UnmovedByF2_07Recording"/>.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> F2_07Recording = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["as-saved/events"] = "33753d12f7c40deb",
        ["as-saved/no-events"] = "27dd7e4251d36b48",
        ["contained/events"] = "3d66542efa3911bf",
    };

    /// <summary>
    /// The same continuations on U2-02's engine (wave4/ui-core-1, built on the
    /// wave-3 integration alone), the whole save included. The facilities save
    /// is a po3_red_sone game, and owner decision D19 runs the tutorial in
    /// Første sesong only: U2-03's engine ran the old eighteen-step track here
    /// and completed its day-count steps (step 6 by day 120-150 of the
    /// continuation, step 7 by day 300), U2-02's runs none, keeps the save's
    /// five finished steps (renamed to the new track's first five on load) and
    /// saves the new TutorialSkipped flag (false). Nothing else moves: with
    /// those three fields written back as an engine without U2-02 wrote them
    /// (<see cref="AsBeforeU2_02Wrote"/>) each continuation was
    /// <see cref="U2_03Recording"/> exactly (RUN_LOG §7, U2-02). On wave4/int
    /// the unit runs its F2-07/F2-08 day, on purpose, so this is history:
    /// <see cref="U2_02F2_15F2_08Recording"/>.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> U2_02Recording = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["as-saved/events"] = "8f7ca6d95e889086",
        ["as-saved/no-events"] = "6278156c9b48de91",
        ["contained/events"] = "f475f30e2d95aeb7",
    };

    /// <summary>The old track's first seven step ids, in the order U2-03's engine completed them.</summary>
    private static readonly string[] OldTrackSteps =
        { "welcome", "setup-sea-site", "stock-smolts", "manage-feed", "monitor-health", "apply-delousing", "grow-to-harvest" };

    /// <summary>
    /// The old track's index on each day of the continuation as the engine
    /// without U2-02 ran it. On U2-03's engine (4f927a1, wave4/ui-core-1) the
    /// lice step opened on day 120 (day 150 in the contained variant) and the
    /// 4 kg mean opened the harvest step before day 300. On wave4/int that
    /// engine is the wave-4 lanes' merge (605040e), measured day by day: the
    /// lice step opens on day 120 (as-saved) and day 184 (contained: F2-08's
    /// lice coupling puts the unit's larvae in its area's zone means, which
    /// moves the contained zone's lice and with them the old step), the
    /// harvest step on day 273 (day 271 without events).
    /// </summary>
    internal static int BeforeU2_02TutorialIndex(string variant, int day)
    {
        int lice = variant == "contained/events" ? 184 : 120;
        int harvest = variant == "as-saved/no-events" ? 271 : 273;
        return day >= harvest ? 7 : day >= lice ? 6 : 5;
    }

    /// <summary>
    /// A save as an engine without U2-02 wrote it (U2-03's on wave4/ui-core-1,
    /// the merge of the wave-4 lanes before U2-02 on wave4/int): without
    /// U2-02's TutorialSkipped (it must be there and false) and with the old
    /// track's progress (the save must hold the new track's first five steps
    /// and index 5: the tutorial does not run in po3_red_sone). The rewrite is
    /// exact: the save written back with nothing changed is the save itself,
    /// byte for byte.
    /// </summary>
    internal static string AsBeforeU2_02Wrote(string savedJson, string variant, int day)
    {
        var root = JsonNode.Parse(savedJson)!.AsObject();
        Assert.Equal(savedJson, root.ToJsonString(SaveJson.Options));
        Assert.False(root["TutorialSkipped"]!.GetValue<bool>());
        Assert.True(root.Remove("TutorialSkipped"));
        Assert.Equal(5, root["TutorialCurrentStepIndex"]!.GetValue<int>());
        var ids = Assert.IsType<JsonArray>(root["CompletedTutorialStepIds"]).Select(n => n!.GetValue<string>()).ToArray();
        Assert.Equal(TutorialSystem.Template().Take(5).Select(t => t.Id), ids);
        int index = BeforeU2_02TutorialIndex(variant, day);
        root["TutorialCurrentStepIndex"] = index;
        root["CompletedTutorialStepIds"] = new JsonArray(OldTrackSteps.Take(index).Select(id => (JsonNode)JsonValue.Create(id)!).ToArray());
        return root.ToJsonString(SaveJson.Options);
    }

    /// <summary>
    /// The same continuations on the wave-4 integration's engine once U2-02
    /// joined it (wave4/int: F2-12 and F2-13/F2-15 on land, F2-06/F2-07 and
    /// F2-08/F2-09 at sea, U2-02's first season), the whole save included.
    /// Each step proves its drift: with U2-02's three tutorial fields written
    /// back as an engine without U2-02 wrote them (<see cref="AsBeforeU2_02Wrote"/>)
    /// each continuation is <see cref="F2_15F2_08Recording"/> exactly; with
    /// both land lanes' site fields left out too it is the sea lane's own
    /// <see cref="F2_08Recording"/> exactly; and what F2-08/F2-09 leave
    /// unmoved is <see cref="UnmovedByF2_08Recording"/> exactly (RUN_LOG §7,
    /// wave4/int).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> U2_02F2_15F2_08Recording = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["as-saved/events"] = "7290f9daa7585561",
        ["as-saved/no-events"] = "3e8a93d3f294a383",
        ["contained/events"] = "df43c271bfed0b99",
    };


    /// <summary>
    /// Every day's <see cref="UnmovedGroups"/> and every stream but the
    /// offshore unit's own (<see cref="UnmovedDayText"/>), chained over the
    /// continuation as <see cref="PreMoveRecording"/> chains the day hashes:
    /// recorded on the wave-3 integration's engine (4f927a1, the engine of
    /// <see cref="U2_03Recording"/>) and on F2-06/F2-07's alike (RUN_LOG §7, F2-07).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> UnmovedByF2_07Recording = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["as-saved/events"] = "f208f1c356f3a0ba",
        ["as-saved/no-events"] = "f6f34ab862ade197",
        ["contained/events"] = "2b152a90b3db43f4",
    };

    /// <summary>
    /// The same continuations on F2-12's wired engine (wave4/sim-land-1), the
    /// whole save included: every land site gains F2-12's additive fields (its
    /// lots, the Vannstrategi's deep share and blend flag, the denitrification
    /// commitment, the day's liquid oxygen and its upstream energy, the
    /// stocking log), and the save's pre-F2 RAS facility and post-smolt site
    /// have no lots, so they keep the pre-F2 tick: hashed with those fields
    /// left out (each at its default, asserted), each continuation was
    /// <see cref="U2_03Recording"/> exactly (RUN_LOG §7, F2-12 wiring). On the
    /// wave-4 engine that carries F2-07 too, the unit moves on purpose, so this
    /// is history now: <see cref="F2_12F2_07Recording"/> pins the continuation
    /// and, with F2-12's fields left out, it is <see cref="F2_07Recording"/>.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> F2_12Recording = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["as-saved/events"] = "37cea6a31ef10952",
        ["as-saved/no-events"] = "55c862b501e0c292",
        ["contained/events"] = "92c01fd0345613de",
    };

    /// <summary>
    /// The same continuations on the engine that carries both F2-12's land
    /// wiring (wave4/sim-land-1) and F2-06/F2-07's offshore unit
    /// (wave4/sim-sea-1), merged on wave4/ui-farm-1, the whole save included.
    /// Neither lane reaches the other's units: hashed with F2-12's land-site
    /// fields left out (<see cref="AsF2_07Wrote"/>), each continuation is
    /// <see cref="F2_07Recording"/> exactly, and what F2-07 left unmoved is
    /// <see cref="UnmovedByF2_07Recording"/> exactly (RUN_LOG §7, wave4/ui-farm-1 merge).
    /// On wave4/int, which carries F2-13/F2-15 too, this is the save as F2-12
    /// wrote it: <see cref="F2_15F2_07Recording"/> pins the continuation.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> F2_12F2_07Recording = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["as-saved/events"] = "0a97202b4a3b7646",
        ["as-saved/no-events"] = "b15b6ecaa680d10b",
        ["contained/events"] = "86bc2d3c159173d6",
    };

    /// <summary>
    /// The same continuations on F2-13/F2-15's engine (wave4/sim-land-2, built
    /// on F2-12 alone), the whole save included: every land site gains the
    /// post-smolt bridge's and the grow-out biology's additive fields (the
    /// salinity, the transfers, the competence and its levers, the feeding
    /// plan, the harvest plans, the site's books), and the save's pre-F2 RAS
    /// facility and post-smolt site have no lots, so none of it runs there:
    /// hashed with those fields left out (each at its default, asserted), each
    /// continuation was <see cref="F2_12Recording"/> exactly (RUN_LOG §7,
    /// F2-13/F2-15). On wave4/int the unit runs in its sea (F2-06/F2-07), on
    /// purpose, so this is history: <see cref="F2_15F2_07Recording"/>.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> F2_15Recording = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["as-saved/events"] = "3d85dd2ee5f663d5",
        ["as-saved/no-events"] = "724fe70bd8f5fcad",
        ["contained/events"] = "e31b57420b3bf461",
    };

    /// <summary>
    /// The same continuations on the wave-4 integration's engine (wave4/int:
    /// F2-12 and F2-13/F2-15 on land, F2-06/F2-07 at sea), the whole save
    /// included. Each step proves its drift: with F2-13/F2-15's land-site
    /// fields left out (<see cref="AsF2_12Wrote"/>) each continuation is
    /// <see cref="F2_12F2_07Recording"/> exactly, with F2-12's left out too
    /// (<see cref="AsF2_07Wrote"/>) it is <see cref="F2_07Recording"/>
    /// exactly, and what F2-07 left unmoved is <see cref="UnmovedByF2_07Recording"/>
    /// exactly (RUN_LOG §7, wave4/int).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> F2_15F2_07Recording = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["as-saved/events"] = "d42e2cda638970f2",
        ["as-saved/no-events"] = "f8c177bc64e9e81f",
        ["contained/events"] = "ff41282d8a01abf0",
    };

    /// <summary>
    /// A save as F2-12 wrote it: without F2-13/F2-15's additive land-site
    /// fields, each at its default (seawater, no transfer, no competence lever,
    /// the full ration, no harvest plan, empty books). The rewrite is exact:
    /// the save written back with nothing removed is the save itself.
    /// </summary>
    internal static string AsF2_12Wrote(string savedJson)
    {
        var root = JsonNode.Parse(savedJson)!.AsObject();
        Assert.Equal(savedJson, root.ToJsonString(SaveJson.Options));
        var sites = Assert.IsType<JsonArray>(root["RASFacilities"]);
        Assert.NotEmpty(sites);
        var defaults = JsonSerializer.SerializeToNode(new RASFacility(), SaveJson.Options)!.AsObject();
        string[] keys = { "SalinityPpt", "Transfers", "Competence", "FeedRationFraction", "HarvestPlans", "HarvestPlansMade",
            "Account", "AccountAtYearStart", "LastYear" };
        foreach (var site in sites)
        {
            var fields = site!.AsObject();
            foreach (var key in keys)
            {
                Assert.True(JsonNode.DeepEquals(defaults[key], fields[key]), $"{key}: {fields[key]?.ToJsonString()}");
                Assert.True(fields.Remove(key), key);
            }
        }
        return root.ToJsonString(SaveJson.Options);
    }

    /// <summary>
    /// A save as F2-07 wrote it (on the land branch: as U2-03 wrote it):
    /// without F2-12's additive land-site fields (each must be there at its
    /// default: no lot, the deep share 0.5, the intakes blended, no
    /// denitrification, no oxygen bought, an empty stocking log). The rewrite
    /// is exact: the save written back with nothing removed is the save itself,
    /// byte for byte.
    /// </summary>
    internal static string AsF2_07Wrote(string savedJson)
    {
        var root = JsonNode.Parse(savedJson)!.AsObject();
        Assert.Equal(savedJson, root.ToJsonString(SaveJson.Options));
        var sites = Assert.IsType<JsonArray>(root["RASFacilities"]);
        Assert.NotEmpty(sites);
        foreach (var site in sites)
        {
            var fields = site!.AsObject();
            Assert.Empty(Assert.IsType<JsonArray>(fields["Lots"]));
            Assert.Equal(0.5, fields["DeepIntakeShare"]!.GetValue<double>());
            Assert.True(fields["BlendTowardSetpoint"]!.GetValue<bool>());
            Assert.False(fields["DenitrificationCommitted"]!.GetValue<bool>());
            Assert.Equal(0.0, fields["DailyLoxKg"]!.GetValue<double>());
            Assert.Equal(0.0, fields["DailyLoxUpstreamKWh"]!.GetValue<double>());
            Assert.Empty(Assert.IsType<JsonArray>(fields["StockingLog"]));
            foreach (var key in new[] { "Lots", "DeepIntakeShare", "BlendTowardSetpoint", "DenitrificationCommitted",
                         "DailyLoxKg", "DailyLoxUpstreamKWh", "StockingLog" })
                Assert.True(fields.Remove(key), key);
        }
        return root.ToJsonString(SaveJson.Options);
    }

    /// <summary>
    /// The same continuations on F2-08/F2-09's engine (wave4/sim-sea-2), the
    /// whole save included. The migrated unit now runs F2-08's day: its lice
    /// model splits its pressure (its own lice at the season's pressure, the
    /// outside copepodids at its plot's share of it, the larvae it sheds counted
    /// in its area's zone means), it stands under the handling requirement (the
    /// coastal lice order follows the offshore escalation), its incidents draw
    /// from the day's u5-u7 (a tilt, a tear or a mooring failure, jellyfish in
    /// November and December) and its booked delousing treats zone by zone.
    /// What moved is the fish (populations, lice, the mortality ledger, the
    /// capacity group's standing biomass), the money and the operations group,
    /// and in the contained variant (its zone near red, the unit's larvae now in
    /// the zone's mean) the market and the rivals from day 138; the weather and
    /// every zone's water and every stream but the unit's own are exactly
    /// F2-07's on every day: <see cref="UnmovedByF2_08Recording"/>.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> F2_08Recording = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["as-saved/events"] = "96df902f8982747b",
        ["as-saved/no-events"] = "926992e6e80a6173",
        ["contained/events"] = "d5b85ea28b241fd8",
    };

    /// <summary>
    /// Every day's <see cref="UnmovedByF2_08Groups"/> and every stream but the
    /// offshore unit's own (<see cref="UnmovedByF2_08DayText"/>), chained over
    /// the continuation: recorded on F2-07's engine (93c79c5, the engine of
    /// <see cref="F2_07Recording"/>) and on F2-08/F2-09's alike (RUN_LOG §7, F2-08).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> UnmovedByF2_08Recording = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["as-saved/events"] = "6d9787e8e64ba9f0",
        ["as-saved/no-events"] = "d4aebb3286b0f754",
        ["contained/events"] = "34c42fccd1a5c492",
    };

    /// <summary>The traced group F2-08/F2-09 leave as it was on the facilities save: the weather and every zone's water (and, beside it, every stream but the unit's own).</summary>
    private static readonly string[] UnmovedByF2_08Groups = { "env" };

    /// <summary>The unit's own streams since F2-08: the open sea, the unit's day and any stream keyed by the unit (its delousing).</summary>
    private static bool IsTheOffshoreUnitsOwnStream(string name, string unitId)
        => IsTheOffshoreUnitsStream(name) || name.EndsWith("/" + unitId, StringComparison.Ordinal);

    /// <summary>A day's <see cref="UnmovedByF2_08Groups"/> and every stream but the unit's own, as canonical text.</summary>
    internal static string UnmovedByF2_08DayText(GoldenTraceState state, string unitId)
    {
        var sb = new StringBuilder();
        foreach (var group in UnmovedByF2_08Groups)
        {
            sb.Append(group).Append('\u001d');
            foreach (var kv in state.Groups[group])
                sb.Append(kv.Key).Append('\u001f').Append(kv.Value).Append('\u001e');
        }
        sb.Append("rng").Append('\u001d');
        foreach (var kv in state.Streams.Where(s => !IsTheOffshoreUnitsOwnStream(s.Key, unitId)))
            sb.Append(kv.Key).Append('\u001f').Append(GoldenTraceState.StateHex(kv.Value)).Append('\u001e');
        return sb.ToString();
    }

    /// <summary>
    /// The same continuations on the wave-4 integration's engine once
    /// F2-08/F2-09 joined it (wave4/int: F2-12 and F2-13/F2-15 on land,
    /// F2-06/F2-07 and F2-08/F2-09 at sea), the whole save included. Each step
    /// proves its drift: with both land lanes' site fields left out
    /// (<see cref="AsF2_12Wrote"/>, then <see cref="AsF2_07Wrote"/>) each
    /// continuation is the sea lane's own <see cref="F2_08Recording"/>
    /// exactly, so neither land lane reaches the unit's F2-08 day; what
    /// F2-08/F2-09 leave unmoved is <see cref="UnmovedByF2_08Recording"/>
    /// exactly; and F2-08 moves the unit's fish, lice and deaths on purpose,
    /// so the continuation is no longer <see cref="F2_15F2_07Recording"/>
    /// (RUN_LOG §7, wave4/int).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> F2_15F2_08Recording = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["as-saved/events"] = "7a7ec38f7b328fde",
        ["as-saved/no-events"] = "9af64844ccb6feef",
        ["contained/events"] = "22af5799827e925d",
    };

    /// <summary>
    /// The traced groups F2-06/F2-07 leave as they were on the facilities
    /// save: the weather and every zone's water, the market, every population,
    /// lice, capacity, the mortality ledger and the rivals. Cash, the ledger
    /// and the operations group move with the unit's lines, charters and wear.
    /// </summary>
    private static readonly string[] UnmovedGroups = { "env", "market", "pop", "lice", "mtb", "mort", "rivals" };

    /// <summary>The offshore unit's own streams (F2-06/F2-07): the open sea and the unit's day.</summary>
    private static bool IsTheOffshoreUnitsStream(string name)
        => name == SimRngStreams.OffshoreMetocean || name.StartsWith(SimRngStreams.OffshoreFarm + "/", StringComparison.Ordinal);

    /// <summary>A day's <see cref="UnmovedGroups"/> and every stream but the offshore unit's own, as canonical text.</summary>
    private static string UnmovedDayText(GoldenTraceState state)
    {
        var sb = new StringBuilder();
        foreach (var group in UnmovedGroups)
        {
            sb.Append(group).Append('\u001d');
            foreach (var kv in state.Groups[group])
                sb.Append(kv.Key).Append('\u001f').Append(kv.Value).Append('\u001e');
        }
        sb.Append("rng").Append('\u001d');
        foreach (var kv in state.Streams.Where(s => !IsTheOffshoreUnitsStream(s.Key)))
            sb.Append(kv.Key).Append('\u001f').Append(GoldenTraceState.StateHex(kv.Value)).Append('\u001e');
        return sb.ToString();
    }

    public static IEnumerable<object[]> Variants() => F2_05Recording.Keys.Select(k => new object[] { k });

    private static string FixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LakskongenSim.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "LakskongenSim.Tests", "Fixtures", "save_v8_facilities.json");
    }

    /// <summary>The pre-F2 facilities save's GameState as JSON.</summary>
    internal static string FixtureGameState()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(FixturePath()).Replace("\r\n", "\n"));
        return doc.RootElement.GetProperty("GameState").GetRawText();
    }

    /// <summary>
    /// The pre-F2 facilities save resumed through the game's adoption path. The
    /// "contained" variant puts the sea sites on every containment path the move
    /// touches: po3_nord semi-closed with a lice skirt, a Smoltscreen barrier and
    /// a seal deterrent, po3_sor on a skirt rung, and lice pressure that turns the
    /// zone red (the red-zone MTB credit of a contained site).
    /// </summary>
    internal static SimulationEngine Resume(string variant)
    {
        bool events = !variant.EndsWith("/no-events", StringComparison.Ordinal);
        var engine = SaveStateComparer.Resume(FixtureGameState(), events);
        if (variant.StartsWith("contained/", StringComparison.Ordinal))
        {
            var nord = engine.State.SeaSites.Single(s => s.Id == "po3_nord");
            nord.Containment = ContainmentLevel.SemiClosed;
            nord.HasLiceSkirt = true;
            nord.Barrier = BarrierKind.Smoltscreen;
            nord.HasSealDeterrent = true;
            var sor = engine.State.SeaSites.Single(s => s.Id == "po3_sor");
            sor.Containment = ContainmentLevel.Skirt;
            foreach (var site in engine.State.SeaSites)
                site.Lice.SeedExternalInfection(2.0);
        }
        return engine;
    }

    /// <summary>
    /// Run a variant and hash every day's traced state and the save every 30
    /// days; <c>BeforeU2_02Hash</c> hashes the same with each save as an engine
    /// without U2-02 wrote it (<see cref="AsBeforeU2_02Wrote"/>), <c>F2_12Hash</c>
    /// as F2-12 wrote it (<see cref="AsF2_12Wrote"/> of that), <c>SeaLaneHash</c> as the sea lane alone
    /// wrote it, with F2-12's fields left out too (<see cref="AsF2_07Wrote"/>
    /// of that); <c>UnmovedHash</c> chains every day's
    /// <see cref="UnmovedDayText"/> and <c>UnmovedBy08Hash</c> every day's
    /// <see cref="UnmovedByF2_08DayText"/>; <c>UnitDraws</c> counts the offshore
    /// unit's own day-stream draws over the run.
    /// </summary>
    private static (string Hash, string BeforeU2_02Hash, string F2_12Hash, string SeaLaneHash, string UnmovedHash, ulong UnitDraws, SimulationEngine Engine, int RedCreditDays, string UnmovedBy08Hash) RecordContinuation(string variant)
    {
        var engine = Resume(variant);
        string unitId = engine.OffshoreUnits.Single().Id;
        string unitStream = SimRngStreams.OffshoreFarm + "/" + unitId;
        ulong unitStreamAtLoad = engine.Rng.CaptureStates()[SimRngStreams.OffshoreFarm + "\u0001" + unitId];
        var sb = new StringBuilder();
        var sbBeforeU2_02 = new StringBuilder();
        var sbF2_12 = new StringBuilder();
        var sbF2_07 = new StringBuilder();
        var sbUnmoved = new StringBuilder();
        var sbUnmoved08 = new StringBuilder();
        int redCreditDays = 0;
        GoldenTraceState? state = null;
        for (int day = 1; day <= ContinuationDays; day++)
        {
            engine.Advance(1);
            state = GoldenTraceState.Capture(engine);
            string dayHash = state.DayHash();
            sb.Append(dayHash).Append('\n');
            sbBeforeU2_02.Append(dayHash).Append('\n');
            sbF2_12.Append(dayHash).Append('\n');
            sbF2_07.Append(dayHash).Append('\n');
            sbUnmoved.Append(GoldenTraceState.Hex(UnmovedDayText(state), 8)).Append('\n');
            sbUnmoved08.Append(GoldenTraceState.Hex(UnmovedByF2_08DayText(state, unitId), 8)).Append('\n');
            if (engine.State.SeaSites.Any(s => s.RedZoneCreditApplied)) redCreditDays++;
            if (day % SnapshotEveryDays == 0 || day == ContinuationDays)
            {
                string save = SaveStateComparer.Normalize(SaveStateComparer.Serialize(engine));
                string beforeU2_02 = AsBeforeU2_02Wrote(save, variant, day);
                string asF2_12 = AsF2_12Wrote(beforeU2_02);
                sb.Append(GoldenTraceState.Hex(save, 8)).Append('\n');
                sbBeforeU2_02.Append(GoldenTraceState.Hex(beforeU2_02, 8)).Append('\n');
                sbF2_12.Append(GoldenTraceState.Hex(asF2_12, 8)).Append('\n');
                sbF2_07.Append(GoldenTraceState.Hex(AsF2_07Wrote(asF2_12), 8)).Append('\n');
            }
        }
        static string Hash(StringBuilder text) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())), 0, 8).ToLowerInvariant();
        ulong unitDraws = GoldenTraceState.DrawsBetween(unitStreamAtLoad, state!.Streams[unitStream]);
        return (Hash(sb), Hash(sbBeforeU2_02), Hash(sbF2_12), Hash(sbF2_07), Hash(sbUnmoved), unitDraws, engine, redCreditDays, Hash(sbUnmoved08));
    }

    /// <summary>
    /// F2-02 acceptance 1 and 2 for the code the goldens never reach, on F2-03's
    /// unit kinds: the pre-F2 facilities save (a RAS facility with stocked
    /// tanks, an offshore farm with stocked pens, a post-smolt facility with
    /// batches, a hatchery cohort) loads into a grow-out land site, an offshore
    /// unit and a post-smolt land site and runs 365 days as recorded on F2-08's
    /// engine: every day's traced state (populations by tank and zone, the
    /// ledger, cash, every stream) and the whole save every 30 days (carbon
    /// trackers, facility water, crew rotation). Replaced F2-02's pre-move pins
    /// (<see cref="PreMoveRecording"/>; RUN_LOG §7, F2-03) and F2-03's
    /// (<see cref="F2_03Recording"/>; RUN_LOG §7, F2-04) and the wave-3
    /// integration's (<see cref="U2_03Recording"/>; RUN_LOG §7, F2-07, with what
    /// F2-07 left unmoved pinned against it) and F2-07's (<see cref="F2_07Recording"/>;
    /// RUN_LOG §7, F2-08, with what F2-08 leaves unmoved pinned against
    /// F2-07's engine), and asserts more: the
    /// land sites and the offshore unit are the save's facilities by id, each
    /// on its plot, the offshore unit's fish in its zones, and no unit whose
    /// capex was paid before F2 enters the asset register, and the unit runs its
    /// F2-07 day (commissioned, fed from its silo, its standing orders, its draws)
    /// and F2-08's (its lice split, every escape an incident's, no forced
    /// delousing at the unit).
    /// </summary>
    [Theory]
    [MemberData(nameof(Variants))]
    public void PreF2FacilitiesSave_Runs365Days_ExactlyAsBeforeTheMove(string variant)
    {
        var (hash, beforeU2_02Hash, f2_12Hash, seaLaneHash, unmovedHash, unitDraws, engine, redCreditDays, unmoved08) = RecordContinuation(variant);
        var ras = engine.RASFacilities.Single(f => f.Kind == LandSiteKind.GrowOut);
        var postSmolt = engine.RASFacilities.Single(f => f.Kind == LandSiteKind.PostSmolt);
        var unit = engine.OffshoreUnits.Single();
        _output.WriteLine($"{variant}: continuation {hash} (as an engine without U2-02 wrote the saves {beforeU2_02Hash}, as F2-12 {f2_12Hash}, as the sea lane alone {seaLaneHash}; unmoved by F2-06/F2-07 {unmovedHash}; unmoved by F2-08/F2-09 {unmoved08}; the unit's draws {unitDraws}); red-credit days {redCreditDays}; " +
            $"cash {engine.FinanceManager.Balance:N0}; fish {engine.State.Populations.Sum(p => (long)p.TotalCount):N0}; " +
            $"RAS kg {ras.GetTotalBiomassKg():N0}; post-smolt kg {postSmolt.GetTotalBiomassKg():N0}; " +
            $"offshore kg {unit.Pens.Sum(p => p.GetTotalBiomassKg()):N0}; the unit's bookings " +
            string.Join(", ", unit.Offshore!.Bookings.GroupBy(b => (b.Op, b.Status)).Select(g => $"{g.Key.Op}/{g.Key.Status} {g.Count()}")) +
            $"; silo {unit.Offshore!.SiloTonnes:F0} t");

        // The facilities did run: tanks and zones hold fish, the post-smolt
        // batches live on in their site's tanks, and each books its carbon.
        Assert.Equal(("ras_1", "pad_a"), (ras.Id, ras.PlotId));
        Assert.Equal(("postsmolt_1", "pad_delta"), (postSmolt.Id, postSmolt.PlotId));
        Assert.Equal(("offshore_1", "off_1"), (unit.Id, unit.PlotId));
        Assert.True(ras.GetTotalBiomassKg() > 0);
        Assert.Contains(unit.Pens, p => p.TotalCount > 0);
        Assert.All(unit.Pens, p => Assert.NotNull(unit.PenOf(p)));
        Assert.Contains(postSmolt.Tanks, p => p.TotalCount > 0);
        Assert.Contains(ras.CarbonTracker.Emissions, e => e.Source == ras.Id);
        Assert.Contains(unit.CarbonTracker.Emissions, e => e.Source == unit.Id);
        Assert.Empty(engine.OffshoreFarms);
        Assert.Empty(engine.PostSmoltFacilities);
        // F2-04: units migrated from a pre-F2 save were paid before F2 and depreciate nothing (spec §8).
        Assert.Empty(engine.State.FacilityAssets);
        Assert.Equal(0m, engine.FinancialStatements.LifetimeDepreciation);
        // F2-04: the land sites' costs stayed out of the rent base (the ring-fence) and the year end reset it.
        Assert.True(engine.State.RentRingFencedCostsThisTaxYear > 0m);
        if (variant.StartsWith("contained/", StringComparison.Ordinal))
            Assert.True(redCreditDays > 0, "the contained variant must reach the red-zone MTB credit");
        // F2-06/F2-07: the migrated unit runs in its own sea with its logistics: commissioned
        // on load (its first day), its silo never ran dry, its Driftsplan defaults booked its
        // crew changes (one a rota, give or take a slipped window), its monthly net inspections
        // and its resupplies, and its day drew exactly its eight uniforms every day.
        var offshore = unit.Offshore!;
        var logistics = LakskongenSim.Calibration.OffshoreCalibration.Default.Logistics;
        int Done(LakskongenSim.Environment.OffshoreOp op)
            => offshore.Bookings.Count(b => b.Op == op && b.Status == LakskongenSim.Farm.Offshore.OffshoreBookingStatus.Done);
        Assert.True(offshore.IsCommissioned);
        Assert.Equal(0, offshore.SiloEmptyDays);
        Assert.Equal(5, engine.GetDriftsplan(unit.Id).Count);
        Assert.InRange(Done(LakskongenSim.Environment.OffshoreOp.CrewChange), ContinuationDays / logistics.CrewChangeIntervalDays - 2, ContinuationDays / logistics.CrewChangeIntervalDays + 1);
        Assert.InRange(Done(LakskongenSim.Environment.OffshoreOp.NetInspection), ContinuationDays / 30 - 1, ContinuationDays / 30 + 1);
        if (!variant.EndsWith("/no-events", StringComparison.Ordinal)) // the no-events year's 450 t never bring the full silo to its trigger
            Assert.True(Done(LakskongenSim.Environment.OffshoreOp.Resupply) >= 1, "the standing order resupplied the unit");
        Assert.Equal((ulong)(SimulationEngine.OffshoreUnitUniformsPerDay * ContinuationDays), unitDraws);
        Assert.NotNull(engine.State.Metocean);

        // F2-08: the unit's lice model runs split (its own lice at the season's
        // pressure, the outside copepodids at its plot's share), its incidents
        // and the requirement run, and every escape it had belongs to an incident.
        Assert.True(unit.Lice.SeparatesExternalPressure);
        Assert.Equal(engine.ExternalLicePressureFor(unit) * unit.Lice.OwnPressureMultiplier, unit.Lice.ExternalOnlyMultiplier, 12);
        Assert.Equal(engine.OffshoreIncidentsOf(unit.Id).Sum(i => (long)i.EscapedFish),
            engine.MortalityLedger.ForSite(unit.Id).Removed(LakskongenSim.Biology.FishRemoval.Escape).Fish);
        Assert.DoesNotContain(engine.State.EventLog, e => e.Name == "Forced delousing" && e.Description.Contains(unit.Label));

        // Two runs agree everywhere.
        var again = RecordContinuation(variant);
        Assert.Equal((hash, beforeU2_02Hash, f2_12Hash, seaLaneHash, unmovedHash, unmoved08), (again.Hash, again.BeforeU2_02Hash, again.F2_12Hash, again.SeaLaneHash, again.UnmovedHash, again.UnmovedBy08Hash));

        if (!GoldenTraceFixtures.MathMatchesRecording)
        {
            _output.WriteLine("not enforced here: this platform's math library differs from the recording's");
            return;
        }
        Assert.Equal(U2_02F2_15F2_08Recording[variant], hash);
        Assert.Equal(F2_15F2_08Recording[variant], beforeU2_02Hash); // U2-02 changes no facility day: only the save's tutorial fields (D19)
        Assert.NotEqual(F2_15F2_08Recording[variant], hash);       // ... which the whole save does carry
        Assert.NotEqual(U2_02Recording[variant], hash);             // the wave-4 lanes run the unit in its sea and add the land sites' fields, on purpose
        Assert.Equal(F2_08Recording[variant], seaLaneHash);   // F2-12 and F2-13/F2-15 change no pre-F2 facility day and no unit's on F2-08's engine either: only the land sites' new fields
        Assert.NotEqual(F2_08Recording[variant], f2_12Hash);  // ... F2-12's, which the save as F2-12 wrote it does carry
        Assert.NotEqual(F2_08Recording[variant], beforeU2_02Hash); // ... and F2-13/F2-15's, which the save without U2-02's fields carries too
        Assert.Equal(UnmovedByF2_08Recording[variant], unmoved08); // F2-08/F2-09 move no weather, no zone's water and no other stream
        Assert.NotEqual(F2_15F2_07Recording[variant], beforeU2_02Hash); // F2-08 runs the unit's lice split, its requirement, incidents and zone-by-zone delousing, on purpose
        Assert.NotEqual(F2_12F2_07Recording[variant], f2_12Hash);
        Assert.NotEqual(F2_07Recording[variant], seaLaneHash);
        Assert.NotEqual(F2_07Recording[variant], hash);
        Assert.NotEqual(UnmovedByF2_07Recording[variant], unmovedHash); // and so moves the unit's fish, lice and deaths (and the fjord's lice through its larvae)
        Assert.NotEqual(F2_15Recording[variant], hash);     // F2-06/F2-07 run the unit in its sea, on purpose
        Assert.NotEqual(F2_12Recording[variant], hash);
        Assert.NotEqual(U2_03Recording[variant], hash);   // F2-06/F2-07 run the unit in its sea with its logistics and lines, on purpose
        Assert.NotEqual(U2_04Recording[variant], hash);
        Assert.NotEqual(F2_05Recording[variant], hash);
        Assert.NotEqual(F2_04Recording[variant], hash);
        Assert.NotEqual(F2_03Recording[variant], hash);   // F2-04 narrows deep-sea-farming and ring-fences the rent, on purpose
        Assert.NotEqual(PreMoveRecording[variant], hash); // the migration changes the facilities' days, on purpose
    }

    // ── Acceptance 3: no facility or containment tick code in the hot files ──

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LakskongenSim.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>The two hot files F2-02 empties of facility and containment tick code.</summary>
    private static readonly string[] HotFiles =
    {
        "src/LakskongenSim/Core/SimulationEngine.cs",
        "src/LakskongenSim/Core/InDayTickSystems.cs",
    };

    /// <summary>
    /// Facility and containment tick code: a facility list iterated or ticked, a
    /// facility stream or carbon tracker, the facility water, the containment
    /// table, the red-zone credit, the skirt and the barrier, and a lane's hook
    /// file called past the engine's hook (SimulationEngine.FarmTypeHooks.cs).
    /// </summary>
    private static readonly (string Name, Regex Pattern)[] TickCodeBans =
    {
        ("a facility list", new Regex(@"\b(RASFacilities|OffshoreFarms|PostSmoltFacilities)\b")),
        ("a facility's day", new Regex(@"(?<!AIFeeding)\.TickDay\(")),
        ("a facility stream", new Regex(@"SimRngStreams\.(RASFacility|OffshoreFarm)\b")),
        ("facility deaths booked by holding", new Regex(@"\bBookHoldingDeaths\(")),
        ("facility water", new Regex(@"\b(CurrentWaterTemp|OxygenBoost)\b")),
        ("facility carbon", new Regex(@"\b(ras|off|rasFacility|offshoreFarm|facility|farm)\.CarbonTracker\b")),
        ("the containment table", new Regex(@"\bContainmentTech\.|\bContainmentLevel\.")),
        ("a site's containment rung", new Regex(@"\.Containment\b")),
        ("the red-zone credit", new Regex(@"\b(RedZoneCreditApplied|MabCreditFor)\b")),
        ("the skirt or closed flag", new Regex(@"\b(HasLiceSkirt|IsClosedContainment)\b")),
        ("the barrier", new Regex(@"\bBarrierTech\.|\bBarrierDaysSinceCleaning\b")),
        ("containment upkeep or exclusion", new Regex(@"\b(WeeklyOpexNok|LiceExclusion01)\b")),
        ("a lane's hook file", new Regex(@"\b(LandHooks|OffshoreHooks)\.")),
    };

    /// <summary>
    /// Lines that name a facility list without ticking it: the engine's views of
    /// the save's lists and the save's population-id normalization.
    /// </summary>
    private static readonly string[] AllowedLines =
    {
        "public List<RASFacility> RASFacilities => State.RASFacilities;",
        "public List<OffshoreFarm> OffshoreFarms => State.OffshoreFarms;",
        "public List<PostSmoltFacility> PostSmoltFacilities => State.PostSmoltFacilities;",
        ".Concat(State.RASFacilities.SelectMany(f => f.Tanks).Select(p => p.Id))",
        ".Concat(State.OffshoreFarms.SelectMany(f => f.Pens).Select(p => p.Id));",
    };

    private static string StripComment(string line)
    {
        int i = line.IndexOf("//", StringComparison.Ordinal);
        return i >= 0 ? line[..i] : line;
    }

    private static List<string> TickCodeIn(string text, string file)
    {
        var found = new List<string>();
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string code = StripComment(lines[i]);
            if (AllowedLines.Contains(code.Trim())) continue;
            foreach (var (name, pattern) in TickCodeBans)
                if (pattern.IsMatch(code))
                    found.Add($"{file}:{i + 1}: {name}: {lines[i].Trim()}");
        }
        return found;
    }

    /// <summary>
    /// F2-02 acceptance 3: grep finds no facility or containment tick code in
    /// SimulationEngine.cs or InDayTickSystems.cs; it lives in
    /// LandSiteTickSystem.cs, OffshoreTickSystem.cs and SimulationEngine.Containment.cs.
    /// </summary>
    [Fact]
    public void TheHotFiles_HoldNoFacilityOrContainmentTickCode()
    {
        string root = RepoRoot();
        var found = HotFiles.SelectMany(rel => TickCodeIn(File.ReadAllText(Path.Combine(root, rel)), rel)).ToList();
        Assert.True(found.Count == 0, "facility or containment tick code in a hot file:\n" + string.Join("\n", found));
        // The views the allowlist names still exist (the allowlist is not stale).
        string engine = File.ReadAllText(Path.Combine(root, HotFiles[0]));
        Assert.All(AllowedLines, line => Assert.Contains(line, engine));
    }

    /// <summary>The ban list still recognises the code F2-02 moved out (as it stood on 8c81754).</summary>
    [Fact]
    public void TheBanList_CatchesThePreMoveTickCode()
    {
        string[] preMove =
        {
            "foreach (var rasFacility in engine.RASFacilities)",
            "rasFacility.SetRng(engine.Rng.Stream(SimRngStreams.RASFacility,",
            "var offshoreResult = offshoreFarm.TickDay(engine.State.Day, engine.Weather.CurrentWindSpeedMs, engine.Weather.CurrentWaveHeight, engine.Weather.CurrentState == WeatherState.Storm);",
            "var postSmoltResult = postSmoltFacility.TickDay(engine.State.Day);",
            "engine.MortalityLedger.BookHoldingDeaths(postSmoltFacility.Id, postSmoltResult.TotalMortality,",
            "ras.CarbonTracker.AddEmission(\"Scope2_Electricity\", ras.Id, elecCO2e, day);",
            "off.CarbonTracker.AddEmission(\"Scope1_Fuel\", off.Id, fuelCO2e, day);",
            "foreach (var off in OffshoreFarms)",
            "var containmentMods = Farm.ContainmentTech.ModifiersFor(",
            "homeSite?.Containment ?? Farm.ContainmentLevel.OpenNet);",
            "var cmods = Farm.ContainmentTech.ModifiersFor(site.Containment);",
            "if (site.HasLiceSkirt) mitigationOpex += 2_000m;",
            "if (after.ToString() == \"Red\" && !site.RedZoneCreditApplied",
            "double credit = Farm.ContainmentTech.MabCreditFor(site.Containment, true);",
            "site.BarrierDaysSinceCleaning += BARRIER_CLEANING_TICK_DAYS;",
            "decimal opex = Farm.ContainmentTech.ModifiersFor(site.Containment).WeeklyOpexNok;",
            "double exclusion = Farm.ContainmentTech.ModifiersFor(site.Containment).LiceExclusion01;",
            "double jellyBlock = Farm.BarrierTech.EffectiveJellyfishBlocking(",
            "return new HoldingWater(holding.LandFacility.CurrentWaterTemp, holding.LandFacility.OxygenLevel,",
            "return sea with { OxygenMgPerL = sea.OxygenMgPerL + holding.OffshoreFarm.OxygenBoost };",
        };
        foreach (var line in preMove)
            Assert.True(TickCodeBans.Any(b => b.Pattern.IsMatch(line)), $"not caught: {line}");
        // ...and leaves the calls that replaced it alone.
        string[] calls =
        {
            "LandSiteTickSystem.TickTanks(engine);",
            "var containmentMods = SimulationEngine.ContainmentModifiersOf(homeSite);",
            "ApplyRedZoneContainmentCredit(zone.Id, after);",
            "* LakskongenSim.Environment.BloomEventDeck.ContainmentMult(site, bloom.Kind)",
            "var aiFeedResult = engine.AIFeeding.TickDay(totalBiomassForAI, currentFeedPricePerKg);",
        };
        foreach (var line in calls)
            Assert.False(TickCodeBans.Any(b => b.Pattern.IsMatch(line)), $"wrongly caught: {line}");
    }

    /// <summary>The hooks of FARM_TYPES_SPEC §3.3 (F2-02 installs every one).</summary>
    private static readonly string[] SpecHooks =
    {
        "ExternalLicePressureFor", "ZoneLiceContribution", "SkipsFjordStormWear", "OperationPossible",
        "FeedAvailableFor", "AppetiteFactorFor", "UnitWaterTemperatureC", "UnitMortalityTerms",
        "QualityMortalityMultiplier", "SmoltTierCostPerFish", "IsResourceRentLiable", "CountsTowardSiteLimits",
    };

    /// <summary>
    /// Hooks with no engine call site yet, and why. None since F2-03 wired
    /// <c>CountsTowardSiteLimits</c> into the remote operations centre's budget
    /// (<see cref="SimulationEngine.RemoteOpsAttentionUsed"/>, which the bridge reads).
    /// </summary>
    private static readonly string[] HooksAwaitingTheirCaller = Array.Empty<string>();

    /// <summary>
    /// Every hook of §3.3 is declared in SimulationEngine.FarmTypeHooks.cs and,
    /// but for the one listed with its reason, called from the engine's tick
    /// outside the hooks and lane files: the lanes fill the hooks without
    /// touching a call site.
    /// </summary>
    [Fact]
    public void EveryHookOfTheSpec_IsDeclaredInTheHooksFile_AndCalledFromTheEngine()
    {
        string root = RepoRoot();
        string hooksFile = Path.Combine(root, "src", "LakskongenSim", "Core", "SimulationEngine.FarmTypeHooks.cs");
        string hooks = File.ReadAllText(hooksFile);
        var engineSources = Directory.EnumerateFiles(Path.Combine(root, "src", "LakskongenSim"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && Path.GetFileName(f) is not ("SimulationEngine.FarmTypeHooks.cs" or "LandHooks.cs" or "OffshoreHooks.cs"))
            .Select(f => string.Join("\n", File.ReadAllLines(f).Select(StripComment)))
            .ToList();

        foreach (var hook in SpecHooks)
        {
            Assert.Matches(new Regex($@"internal \S+ {hook}\("), hooks);
            Assert.NotNull(typeof(SimulationEngine).GetMethod(hook, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
            bool called = engineSources.Any(src => new Regex($@"\b{hook}\b").IsMatch(src));
            Assert.True(called || HooksAwaitingTheirCaller.Contains(hook), $"hook {hook} has no call site");
            Assert.False(called && HooksAwaitingTheirCaller.Contains(hook), $"hook {hook} is called now: take it off the awaiting list");
        }
    }

    /// <summary>
    /// The red-zone MTB credit of a contained site (Phase 4.1), which the move
    /// takes out of the traffic-light loop: a zone turning red multiplies a
    /// semi-closed or closed site's MTB by its credit once; the zone leaving red
    /// divides it back exactly; an open-net site in the zone keeps its MTB.
    /// </summary>
    [Fact]
    public void RedZoneCredit_MultipliesAContainedSitesMtbOnRed_AndRevertsItExactlyOnExit()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 5);
        engine.State.Zones.Add(new LakskongenSim.Environment.Zone("red_zone"));
        var contained = new SeaSite("contained", "red_zone") { MABTonnes = 3120.0, Containment = ContainmentLevel.ClosedFloat };
        var open = new SeaSite("open", "red_zone") { MABTonnes = 3120.0 };
        engine.State.SeaSites.Add(contained);
        engine.State.SeaSites.Add(open);
        var pop = new FishPopulation { ZoneId = "red_zone", SiteId = "open", SeaLiceLoad = 2.0 };
        pop.InjectDependencies(engine.Growth, engine.Mortality);
        pop.Cohorts.Add(new FishPopulation.Cohort(400, 10_000, 1.0, LifeStage.Seawater));
        engine.State.Populations.Add(pop);

        engine.UpdateTrafficLightZones();
        double credit = ContainmentTech.MabCreditFor(ContainmentLevel.ClosedFloat, true);
        Assert.Equal("Red", engine.TrafficLightSystem.GetZoneColor("red_zone").ToString());
        Assert.True(contained.RedZoneCreditApplied);
        Assert.Equal(3120.0 * credit, contained.MABTonnes);
        Assert.False(open.RedZoneCreditApplied);
        Assert.Equal(3120.0, open.MABTonnes);

        engine.UpdateTrafficLightZones(); // still red: never compounds
        Assert.Equal(3120.0 * credit, contained.MABTonnes);

        pop.SeaLiceLoad = 0.1;
        engine.UpdateTrafficLightZones();
        Assert.Equal("Green", engine.TrafficLightSystem.GetZoneColor("red_zone").ToString());
        Assert.False(contained.RedZoneCreditApplied);
        Assert.Equal(3120.0 * credit / credit, contained.MABTonnes);
        Assert.Equal(3120.0, open.MABTonnes);
    }
}
