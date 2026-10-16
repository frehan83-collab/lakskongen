using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Farm;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// Save/load matrix (review F1073/F884): a scripted player career is saved at
/// several days, loaded on a fresh engine through the game's adoption path
/// (AdoptSavedState) and continued to a horizon past the competitor quarters
/// and the year-end close. Every resumed run must serialize identically to the
/// uninterrupted run.
/// <para>
/// Pre-registered done-criterion: for every (seed, events, save day) cell the
/// GUID-normalized save JSON at <see cref="HorizonDay"/> equals the
/// uninterrupted run's, and saving (PrepareForSave) mid-run never changes the
/// trajectory. The script exercises every persistence gap the review found:
/// active scenario (F243/F244), contracts (F409), RAS/offshore/post-smolt
/// facilities with per-facility RNG (F1069), treatment-driven disease
/// prevalence (F478), loans/insurance, harvest, competitor quarters (F1071)
/// and the annual tax close (F1137).
/// </para>
/// </summary>
public class SaveLoadMatrixTests
{
    private const string ScenarioId = "po3_red_sone";
    private const int HorizonDay = 540;
    private static readonly int[] SaveDays = { 45, 100, 200, 300, 400 };

    private readonly ITestOutputHelper _output;

    public SaveLoadMatrixTests(ITestOutputHelper output) => _output = output;

    public static IEnumerable<object[]> Cells()
    {
        foreach (var seed in new[] { 3, 7, 11, 19, 23, 31, 42, 57 })
            foreach (var events in new[] { false, true })
                yield return new object[] { seed, events };
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public void SavedCareer_ContinuesBitIdentically_FromEverySaveDay(int seed, bool events)
    {
        // Pure uninterrupted run: never saves until the horizon.
        var pure = NewCareer(seed, events);
        RunScript(pure, HorizonDay);
        string expected = SaveStateComparer.Normalize(SaveStateComparer.Serialize(pure));

        // Snapshotting run: same script, takes a save at every save day.
        var saves = new Dictionary<int, string>();
        var snapshotting = NewCareer(seed, events);
        RunScript(snapshotting, HorizonDay, day =>
        {
            if (SaveDays.Contains(day))
                saves[day] = SaveStateComparer.Serialize(snapshotting);
        });
        string snapshottingFinal = SaveStateComparer.Normalize(SaveStateComparer.Serialize(snapshotting));
        Assert.True(expected == snapshottingFinal,
            $"seed {seed} events {events}: saving mid-run changed the trajectory at " +
            SaveStateComparer.FirstDifference(expected, snapshottingFinal));

        var failures = new List<string>();
        foreach (var saveDay in SaveDays)
        {
            var resumed = SaveStateComparer.Resume(saves[saveDay], events);
            Assert.Equal(saveDay, resumed.State.Day);
            RunScript(resumed, HorizonDay);
            string actual = SaveStateComparer.Normalize(SaveStateComparer.Serialize(resumed));
            if (actual != expected)
                failures.Add($"save day {saveDay}: {SaveStateComparer.FirstDifference(expected, actual)}");
        }
        foreach (var f in failures) _output.WriteLine(f);
        Assert.True(failures.Count == 0,
            $"seed {seed} events {events}: {failures.Count}/{SaveDays.Length} resumed runs diverged:\n" +
            string.Join("\n", failures));
    }

    // ── Scripted career ────────────────────────────────────────────────────────

    private static SimulationEngine NewCareer(int seed, bool events)
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: events, seed: seed);
        engine.State.RandomSeed = seed;
        var started = engine.StartScenario(ScenarioId);
        Assert.True(started.Ok, started.Message);
        return engine;
    }

    /// <summary>
    /// Advance day by day to <paramref name="horizon"/>, applying the player
    /// actions scheduled for each day. <paramref name="beforeDay"/> runs first
    /// each day (used to take saves BEFORE that day's actions, so a resumed
    /// run replays the same actions from its save day).
    /// </summary>
    private static void RunScript(SimulationEngine engine, int horizon, Action<int>? beforeDay = null)
    {
        while (engine.State.Day < horizon)
        {
            int day = engine.State.Day;
            beforeDay?.Invoke(day);
            ApplyActions(engine, day);
            engine.Advance(1);
        }
    }

    private static void ApplyActions(SimulationEngine engine, int day)
    {
        switch (day)
        {
            case 0:
                Ok(engine.SetInsurancePolicy("mortality", true, 1.0, 250_000));
                Ok(engine.SetInsurancePolicy("storm", true, 1.0, 150_000));
                Ok(engine.TakeLoan(4_000_000, 36, 0.07));
                Ok(engine.RequestWellboat("po3_nord", "rov"));
                break;
            case 5:
                Ok(engine.AcceptContractOffer(0));
                break;
            case 10:
                // Facility verbs need far more than starting cash; the test funds
                // them so the persistence path (not affordability) is exercised.
                engine.FinanceManager.AddRevenue(4_000_000_000m);
                Ok(engine.BuildRASFacility("Matrix RAS", RASScale.Small));
                engine.RASFacilities[^1].ConstructionDays = 40; // operational inside the horizon
                break;
            case 45:
                // F2-03: the legacy verbs build the F2 unit kinds (an offshore
                // sea site on off_1, a post-smolt land site on pad_delta).
                Ok(engine.BuildOffshoreFarm("Matrix Offshore", OffshoreType.SemiSubmersible));
                engine.OffshoreUnits[^1].Offshore!.ConstructionDays = 60;
                break;
            case 60:
                Ok(engine.BuildPostSmoltFacility("Matrix Post-smolt"));
                engine.PostSmoltSites[^1].ConstructionDays = 50;
                break;
            case 112:
                // F2-03: every new kind holds fish across the later saves: the
                // offshore unit's first zone and a vaccinated post-smolt batch.
                Ok(engine.StockOffshoreFarm(engine.OffshoreUnits[^1].Id, 30_000));
                Ok(engine.StartPostSmoltBatch(engine.PostSmoltSites[^1].Id, 120_000));
                break;
            case 118:
                Ok(engine.VaccinatePostSmoltBatch(engine.PostSmoltSites[^1].Id, engine.PostSmoltSites[^1].Tanks[^1].Id));
                break;
            case 120:
                Ok(engine.RequestWellboat("po3_sor", "delouse", "hydrolicer"));
                break;
            case 150:
                Ok(engine.AcceptContractOffer(0));
                break;
            case 170:
                // Summer water (thermolicer needs 6-18 °C); before the 200/300 saves.
                Ok(engine.DelouseSite("po3_nord", "thermolicer"));
                break;
            case 230:
                engine.HarvestSite("po3_sor");
                break;
        }
    }

    private static void Ok(CommandResult result)
        => Assert.True(result.Ok, $"{result.ErrorKey}: {result.Message}");
}
