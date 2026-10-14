using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LakskongenSim.Core;
using LakskongenSim.Farm;

namespace StateHashProbe;

/// <summary>
/// Generator probe of the pre-F2 facilities save (FARM_TYPES_SPEC §3.8, F2-01
/// acceptance 4): a game file as the Godot SaveLoadManager writes it (save
/// format v<see cref="SaveVersion"/>, the version current before any F2 schema
/// change) holding one RAS facility with stocked tanks, one SemiSubmersible
/// offshore farm with two stocked pens, one post-smolt facility with two
/// batches and a hatchery cohort, next to po3_red_sone's sea sites. F2-03's
/// migration is tested against it (src/LakskongenSim.Tests/Fixtures/
/// save_v8_facilities.json). It is generated once, on the pre-F2 commit
/// (<c>StateHashProbe golden save-fixture --out FILE</c>); later commits
/// build these facilities as the new unit kinds, so it is never regenerated.
/// </summary>
public static class PreF2SaveFixture
{
    /// <summary>SaveLoadManager.CurrentSaveVersion on the pre-F2 commit (wave 2 S2-01b's v8).</summary>
    public const int SaveVersion = 8;

    public const string ScenarioId = "po3_red_sone";
    public const int Seed = 3;

    /// <summary>Fixed so the file regenerates byte for byte (a real save stores the wall clock).</summary>
    public const string SaveTime = "2026-10-15T00:00:00Z";

    /// <summary>Days the facilities take to build here (the real ones take two years; the fixture only needs them running).</summary>
    public const int FixtureConstructionDays = 20;

    public const int RasTankStocking = 20_000;
    public const int OffshorePenStocking = 100_000;
    public const int PostSmoltBatch = 150_000;
    public const int HatcheryEggs = 50_000;

    /// <summary>Days the stocked facilities run before the save is taken.</summary>
    public const int DaysAfterStocking = 10;

    /// <summary>Cash the generator gives the company for the builds (booked as unclassified, as SaveLoadMatrixTests does).</summary>
    public const decimal BuildFunding = 4_000_000_000m;

    private static readonly Regex GuidPattern = new(
        @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.Compiled);

    /// <summary>Write the fixture file.</summary>
    public static void Write(string path) => File.WriteAllText(path, Generate(), new UTF8Encoding(false));

    /// <summary>The fixture's text: the game's save wrapper around the GameState.</summary>
    public static string Generate()
    {
        var engine = Build();
        engine.PrepareForSave();
        var wrapper = new Dictionary<string, object>
        {
            ["SaveName"] = "pre-F2 facilities fixture (F2-01)",
            ["SaveTime"] = DateTime.Parse(SaveTime, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal),
            ["GameVersion"] = "1.0.0",
            ["SaveVersion"] = SaveVersion,
            ["GameState"] = engine.State,
        };
        return RenumberGuids(JsonSerializer.Serialize(wrapper, SaveJson.Options)) + "\n";
    }

    /// <summary>The game the fixture saves.</summary>
    public static SimulationEngine Build()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: true, seed: Seed);
        engine.State.RandomSeed = Seed;
        Require(engine.StartScenario(ScenarioId));
        engine.FinanceManager.AddRevenue(BuildFunding);

        Require(engine.BuildRASFacility("Fixture RAS", RASScale.Small));
        Require(engine.BuildOffshoreFarm("Fixture Offshore", OffshoreType.SemiSubmersible));
        Require(engine.BuildPostSmoltFacility("Fixture Post-smolt"));
        engine.RASFacilities[^1].ConstructionDays = FixtureConstructionDays;
        engine.OffshoreFarms[^1].ConstructionDays = FixtureConstructionDays;
        engine.PostSmoltFacilities[^1].ConstructionDays = FixtureConstructionDays;
        engine.Advance(FixtureConstructionDays + 1);

        var ras = engine.RASFacilities[^1];
        var offshore = engine.OffshoreFarms[^1];
        var postSmolt = engine.PostSmoltFacilities[^1];
        if (!ras.IsOperational || !offshore.IsOperational || !postSmolt.IsOperational)
            throw new InvalidOperationException("a fixture facility is not operational");
        Require(engine.StockRASFacility(ras.Id, RasTankStocking));
        Require(engine.StockRASFacility(ras.Id, RasTankStocking));
        Require(engine.StockOffshoreFarm(offshore.Id, OffshorePenStocking));
        Require(engine.StockOffshoreFarm(offshore.Id, OffshorePenStocking));
        Require(engine.StartPostSmoltBatch(postSmolt.Id, PostSmoltBatch));
        Require(engine.StartPostSmoltBatch(postSmolt.Id, PostSmoltBatch));
        engine.Hatchery.SpawnEggs(HatcheryEggs);
        engine.Advance(DaysAfterStocking);
        return engine;
    }

    /// <summary>
    /// Replace each process-random Guid by a fixed one in order of first
    /// appearance (references between populations and pens stay linked), so
    /// the file regenerates byte for byte.
    /// </summary>
    public static string RenumberGuids(string json)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return GuidPattern.Replace(json, m =>
        {
            if (!map.TryGetValue(m.Value, out var fixedId))
            {
                fixedId = $"00000000-0000-4000-8000-{map.Count + 1:x12}";
                map[m.Value] = fixedId;
            }
            return fixedId;
        });
    }

    private static void Require(CommandResult result)
    {
        if (!result.Ok)
            throw new InvalidOperationException($"{result.ErrorKey}: {result.Message}");
    }
}
