using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    /// a RUN_LOG §7 row, as F2-03 and F2-04 did.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> F2_04Recording = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["as-saved/events"] = "3d9dccd403596921",
        ["as-saved/no-events"] = "a006d57aa0681740",
        ["contained/events"] = "aa0ff910f3e60c5a",
    };

    public static IEnumerable<object[]> Variants() => F2_04Recording.Keys.Select(k => new object[] { k });

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
    /// F2-02 acceptance 1 and 2 for the code the goldens never reach, on F2-03's
    /// unit kinds: the pre-F2 facilities save (a RAS facility with stocked
    /// tanks, an offshore farm with stocked pens, a post-smolt facility with
    /// batches, a hatchery cohort) loads into a grow-out land site, an offshore
    /// unit and a post-smolt land site and runs 365 days as recorded on F2-04's
    /// engine: every day's traced state (populations by tank and zone, the
    /// ledger, cash, every stream) and the whole save every 30 days (carbon
    /// trackers, facility water, crew rotation). Replaced F2-02's pre-move pins
    /// (<see cref="PreMoveRecording"/>; RUN_LOG §7, F2-03) and F2-03's
    /// (<see cref="F2_03Recording"/>; RUN_LOG §7, F2-04), and asserts more: the
    /// land sites and the offshore unit are the save's facilities by id, each
    /// on its plot, the offshore unit's fish in its zones, and no unit whose
    /// capex was paid before F2 enters the asset register.
    /// </summary>
    [Theory]
    [MemberData(nameof(Variants))]
    public void PreF2FacilitiesSave_Runs365Days_ExactlyAsBeforeTheMove(string variant)
    {
        var (hash, engine, redCreditDays) = RecordContinuation(variant);
        var ras = engine.RASFacilities.Single(f => f.Kind == LandSiteKind.GrowOut);
        var postSmolt = engine.RASFacilities.Single(f => f.Kind == LandSiteKind.PostSmolt);
        var unit = engine.OffshoreUnits.Single();
        _output.WriteLine($"{variant}: continuation {hash}; red-credit days {redCreditDays}; " +
            $"cash {engine.FinanceManager.Balance:N0}; fish {engine.State.Populations.Sum(p => (long)p.TotalCount):N0}; " +
            $"RAS kg {ras.GetTotalBiomassKg():N0}; post-smolt kg {postSmolt.GetTotalBiomassKg():N0}; " +
            $"offshore kg {unit.Pens.Sum(p => p.GetTotalBiomassKg()):N0}");

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

        // Two runs agree everywhere.
        Assert.Equal(hash, RecordContinuation(variant).Hash);

        if (!GoldenTraceFixtures.MathMatchesRecording)
        {
            _output.WriteLine("not enforced here: this platform's math library differs from the recording's");
            return;
        }
        Assert.Equal(F2_04Recording[variant], hash);
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
