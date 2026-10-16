using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LakskongenSim.Data;
using LakskongenSim.Core;
using LakskongenSim.Farm;
using StateHashProbe;
using Xunit;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// The pre-F2 facilities save (FARM_TYPES_SPEC §3.8, F2-01 acceptance 4):
/// src/LakskongenSim.Tests/Fixtures/save_v8_facilities.json, generated on the
/// pre-F2 commit by <see cref="PreF2SaveFixture"/> (the spec's "v3" is the save
/// version current before any F2 schema change, v8). F2-03 migrates it; these
/// tests pin what it holds and that the pre-F2 engine loads and runs it.
/// </summary>
public class PreF2SaveFixtureTests
{
    /// <summary>SHA-256 of the fixture with "\n" line ends: the file must never change (F2-03 reads it, never rewrites it).</summary>
    private const string FixtureSha256 = "97ed19f13d7180857e01e37226f8712a669f0e2e9289836fdba9c02baba186c2";

    private static string FixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LakskongenSim.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "LakskongenSim.Tests", "Fixtures", "save_v8_facilities.json");
    }

    private static string FixtureText() => File.ReadAllText(FixturePath()).Replace("\r\n", "\n");

    private static GameState FixtureState()
    {
        using var doc = JsonDocument.Parse(FixtureText());
        return JsonSerializer.Deserialize<GameState>(doc.RootElement.GetProperty("GameState").GetRawText(), SaveJson.Options)
            ?? throw new InvalidOperationException("no GameState in the fixture");
    }

    [Fact]
    public void TheFixture_IsUnchanged()
    {
        string sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(FixtureText()))).ToLowerInvariant();
        Assert.Equal(FixtureSha256, sha);
    }

    /// <summary>The game's save wrapper (godot SaveLoadManager's SaveGameData) at the pre-F2 save version.</summary>
    [Fact]
    public void TheFixture_IsAGameSaveFile_AtThePreF2SaveVersion()
    {
        using var doc = JsonDocument.Parse(FixtureText());
        var root = doc.RootElement;
        Assert.Equal(new[] { "SaveName", "SaveTime", "GameVersion", "SaveVersion", "GameState" },
            root.EnumerateObject().Select(p => p.Name));
        Assert.Equal(PreF2SaveFixture.SaveVersion, root.GetProperty("SaveVersion").GetInt32());
        Assert.Equal(8, PreF2SaveFixture.SaveVersion);
        Assert.DoesNotContain(FixtureText(), c => c == '\r');
    }

    /// <summary>
    /// F2-01 acceptance 4: one RAS facility with stocked tanks, one SemiSubmersible
    /// offshore farm with two stocked pens, one post-smolt facility with two
    /// batches and a hatchery cohort (beside po3_red_sone's sea sites).
    /// </summary>
    [Fact]
    public void TheFixture_HoldsTheFacilitiesF2_03Migrates()
    {
        var state = FixtureState();

        var ras = Assert.Single(state.RASFacilities);
        Assert.True(ras.IsOperational);
        Assert.Equal(2, ras.Tanks.Count(t => t.TotalCount > 0));

        var offshore = Assert.Single(state.OffshoreFarms);
        Assert.Equal(OffshoreType.SemiSubmersible, offshore.FarmType);
        Assert.True(offshore.IsOperational);
        Assert.Equal(2, offshore.Pens.Count);
        Assert.All(offshore.Pens, p => Assert.True(p.TotalCount > 0));

        var postSmolt = Assert.Single(state.PostSmoltFacilities);
        Assert.Equal(2, postSmolt.Batches.Count);
        Assert.All(postSmolt.Batches, b => Assert.True(b.Count > 0));

        var hatchery = Assert.Single(state.Populations, p => string.IsNullOrEmpty(p.SiteId));
        Assert.True(hatchery.TotalCount > 0);
        Assert.All(hatchery.Cohorts, c => Assert.Equal(LifeStage.Egg, c.Stage));

        // The facility fish are in the game's population list, as the engine keeps them.
        Assert.Equal(2, state.Populations.Count(p => p.SiteId == ras.Id));
        Assert.Equal(2, state.Populations.Count(p => p.SiteId == offshore.Id));
        Assert.Equal(new[] { "po3_nord", "po3_sor" }, state.SeaSites.Select(s => s.Id).OrderBy(i => i, StringComparer.Ordinal));
        Assert.True(state.SeaSites.All(s => s.Pens.Sum(p => p.TotalCount) > 0));
    }

    /// <summary>
    /// The engine loads the fixture through the game's adoption path with
    /// every fish and kilo in place, runs 30 more days with the facilities
    /// growing fish, and two loads run identically. F2-03 replaces the pre-F2
    /// form of this test (the facilities stayed the pre-F2 kinds): the load
    /// migrates them, one grow-out land site, one post-smolt land site and one
    /// offshore unit by the facilities' ids, and the pre-F2 lists are empty;
    /// every fish and kilo of the save is still there, in a population, and
    /// the post-smolt batches are populations that grow. The migration's own
    /// acceptance tests are <c>FacilityMigrationTests</c>.
    /// </summary>
    [Fact]
    public void TheFixture_LoadsOnThePreF2Engine_KeepsEveryFish_AndRuns30Days()
    {
        var saved = FixtureState();
        long savedFish = saved.Populations.Sum(p => (long)p.TotalCount)
            + saved.PostSmoltFacilities.Sum(f => f.Batches.Sum(b => (long)b.Count));
        double savedKg = saved.Populations.Sum(p => p.GetTotalBiomassKg())
            + saved.PostSmoltFacilities.Sum(f => f.Batches.Sum(b => b.Count * b.AverageWeightG / 1000.0));
        string gameState;
        using (var doc = JsonDocument.Parse(FixtureText()))
            gameState = doc.RootElement.GetProperty("GameState").GetRawText();

        var first = SaveStateComparer.Resume(gameState, events: true);
        Assert.Equal(saved.Day, first.State.Day);
        Assert.Equal(savedFish, first.State.Populations.Sum(p => (long)p.TotalCount));
        Assert.Equal(savedKg, first.State.Populations.Sum(p => p.GetTotalBiomassKg()), 9);
        var ras = Assert.Single(first.RASFacilities, f => f.Kind == LandSiteKind.GrowOut);
        var postSmolt = Assert.Single(first.PostSmoltSites);
        var unit = Assert.Single(first.OffshoreUnits);
        Assert.Equal((saved.RASFacilities[0].Id, saved.PostSmoltFacilities[0].Id, saved.OffshoreFarms[0].Id),
            (ras.Id, postSmolt.Id, unit.Id));
        Assert.Empty(first.OffshoreFarms);
        Assert.Empty(first.PostSmoltFacilities);

        double rasKg = ras.GetTotalBiomassKg();
        double postSmoltKg = postSmolt.GetTotalBiomassKg();
        first.Advance(30);
        Assert.True(ras.GetTotalBiomassKg() > rasKg, "RAS fish did not grow after the load");
        Assert.Equal(2, unit.Pens.Count);
        Assert.True(unit.Pens.All(p => p.TotalCount > 0));
        Assert.True(postSmolt.Tanks.Sum(p => p.TotalCount) > 0);
        Assert.True(postSmolt.GetTotalBiomassKg() > postSmoltKg, "post-smolt batches did not grow after the load");

        var second = SaveStateComparer.Resume(gameState, events: true);
        second.Advance(30);
        string a = SaveStateComparer.Normalize(SaveStateComparer.Serialize(first));
        string b = SaveStateComparer.Normalize(SaveStateComparer.Serialize(second));
        Assert.True(a == b, "two loads diverged: " + SaveStateComparer.FirstDifference(a, b));
    }
}
