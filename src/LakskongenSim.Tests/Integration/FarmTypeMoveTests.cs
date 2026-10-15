using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    /// pre-move engine (this file's first commit, on the wave-2 base 8c81754):
    /// SHA-256 (64 bit) over every day's golden-trace state hash and
    /// the GUID-normalized save every 30 days. Enforced where the platform's C
    /// math library matches the golden traces' recording (as they are).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> PreMoveRecording = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["as-saved/events"] = "66412d201cf6141c",
        ["as-saved/no-events"] = "e4cfbd3cf6eb0d09",
        ["contained/events"] = "6e7519ba98447711",
    };

    public static IEnumerable<object[]> Variants() => PreMoveRecording.Keys.Select(k => new object[] { k });

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

    /// <summary>Run a variant and hash every day's traced state and the save every 30 days.</summary>
    private static (string Hash, SimulationEngine Engine, int RedCreditDays) RecordContinuation(string variant)
    {
        var engine = Resume(variant);
        var sb = new StringBuilder();
        int redCreditDays = 0;
        for (int day = 1; day <= ContinuationDays; day++)
        {
            engine.Advance(1);
            sb.Append(GoldenTraceState.Capture(engine).DayHash()).Append('\n');
            if (engine.State.SeaSites.Any(s => s.RedZoneCreditApplied)) redCreditDays++;
            if (day % SnapshotEveryDays == 0 || day == ContinuationDays)
                sb.Append(GoldenTraceState.Hex(SaveStateComparer.Normalize(SaveStateComparer.Serialize(engine)), 8)).Append('\n');
        }
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())), 0, 8).ToLowerInvariant();
        return (hash, engine, redCreditDays);
    }

    /// <summary>
    /// F2-02 acceptance 1 and 2 for the code the goldens never reach: the pre-F2
    /// facilities save (a RAS facility with stocked tanks, an offshore farm with
    /// stocked pens, a post-smolt facility with batches, a hatchery cohort) runs
    /// 365 days exactly as it did before the move: every day's traced state
    /// (populations by tank and pen, the ledger, cash, every stream) and the
    /// whole save every 30 days (carbon trackers, facility water, crew rotation).
    /// </summary>
    [Theory]
    [MemberData(nameof(Variants))]
    public void PreF2FacilitiesSave_Runs365Days_ExactlyAsBeforeTheMove(string variant)
    {
        var (hash, engine, redCreditDays) = RecordContinuation(variant);
        _output.WriteLine($"{variant}: continuation {hash}; red-credit days {redCreditDays}; " +
            $"cash {engine.FinanceManager.Balance:N0}; fish {engine.State.Populations.Sum(p => (long)p.TotalCount):N0}; " +
            $"RAS kg {engine.RASFacilities.Sum(f => f.GetTotalBiomassKg()):N0}; offshore kg {engine.OffshoreFarms.Sum(f => f.Pens.Sum(p => p.GetTotalBiomassKg())):N0}");

        // The facilities did run: tanks and pens hold fish, post-smolt batches live.
        Assert.True(engine.RASFacilities.Single().GetTotalBiomassKg() > 0);
        Assert.Contains(engine.OffshoreFarms.Single().Pens, p => p.TotalCount > 0);
        Assert.Contains(engine.RASFacilities.Single().CarbonTracker.Emissions, e => e.Source == engine.RASFacilities.Single().Id);
        if (variant.StartsWith("contained/", StringComparison.Ordinal))
            Assert.True(redCreditDays > 0, "the contained variant must reach the red-zone MTB credit");

        // Two runs agree everywhere.
        Assert.Equal(hash, RecordContinuation(variant).Hash);

        if (!GoldenTraceFixtures.MathMatchesRecording)
        {
            _output.WriteLine("not enforced here: this platform's math library differs from the recording's");
            return;
        }
        Assert.Equal(PreMoveRecording[variant], hash);
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
