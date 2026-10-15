using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Sustainability;
using Xunit;

namespace LakskongenSim.SaveLoadTests;

/// <summary>
/// Comprehensive test suite for SaveLoadManager covering:
/// - Basic save/load functionality
/// - Backup rotation
/// - Backward compatibility
/// - Error handling
/// - Stress testing (large saves, rapid cycles, corruption, concurrency)
/// </summary>
public class SaveLoadManagerTests : IDisposable
{
    private readonly string _testDir;
    private readonly SaveLoadManager _manager;

    public SaveLoadManagerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "SaveLoadTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testDir);
        _manager = new SaveLoadManager(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Basic Functionality Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SaveGame_CreatesFile()
    {
        var state = CreateBasicState();
        _manager.SaveGame(state, "test");

        var path = Path.Combine(_testDir, "test.json");
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void LoadGame_RestoresState()
    {
        var original = CreateBasicState();
        original.Day = 42;
        original.Balance = 1000000m;

        _manager.SaveGame(original, "test");
        var loaded = _manager.LoadGame("test");

        Assert.Equal(42, loaded.Day);
        Assert.Equal(1000000m, loaded.Balance);
    }

    [Fact]
    public void LoadGame_NonexistentFile_ThrowsFileNotFoundException()
    {
        Assert.Throws<FileNotFoundException>(() => _manager.LoadGame("nonexistent"));
    }

    [Fact]
    public void SaveGame_CreatesBackup()
    {
        var state1 = CreateBasicState();
        state1.Day = 10;
        _manager.SaveGame(state1, "test");

        var state2 = CreateBasicState();
        state2.Day = 20;
        _manager.SaveGame(state2, "test");

        var backupPath = Path.Combine(_testDir, "test.json.bak1");
        Assert.True(File.Exists(backupPath));

        // Verify backup contains the first save
        var backupContent = File.ReadAllText(backupPath);
        Assert.Contains("\"Day\": 10", backupContent);
    }

    [Fact]
    public void SaveGame_RotatesBackups()
    {
        var state = CreateBasicState();

        // Create 4 saves to test rotation
        for (int i = 1; i <= 4; i++)
        {
            state.Day = i * 10;
            _manager.SaveGame(state, "test");
        }

        // Current file should have day 40
        var current = _manager.LoadGame("test");
        Assert.Equal(40, current.Day);

        // bak1 should have day 30
        var bak1Path = Path.Combine(_testDir, "test.json.bak1");
        var bak1Content = File.ReadAllText(bak1Path);
        Assert.Contains("\"Day\": 30", bak1Content);

        // bak2 should have day 20
        var bak2Path = Path.Combine(_testDir, "test.json.bak2");
        var bak2Content = File.ReadAllText(bak2Path);
        Assert.Contains("\"Day\": 20", bak2Content);

        // bak3 should have day 10
        var bak3Path = Path.Combine(_testDir, "test.json.bak3");
        var bak3Content = File.ReadAllText(bak3Path);
        Assert.Contains("\"Day\": 10", bak3Content);

        // bak4 should not exist (only 3 backups max)
        var bak4Path = Path.Combine(_testDir, "test.json.bak4");
        Assert.False(File.Exists(bak4Path));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Data Integrity Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SaveLoad_PreservesCollections()
    {
        var state = CreateBasicState();
        state.Zones.Add(new Environment.Zone { Id = "PO1" });
        state.SeaSites.Add(new Farm.SeaSite { Id = "Site1", ZoneId = "PO1" });
        state.UnlockedResearchIds.Add("research_fcr");
        state.ExportMarkets.Add("EU");
        state.UnlockedAchievementIds.Add("first_harvest");
        state.CompletedTutorialStepIds.Add("step_01");

        _manager.SaveGame(state, "test");
        var loaded = _manager.LoadGame("test");

        Assert.Single(loaded.Zones);
        Assert.Equal("PO1", loaded.Zones[0].Id);
        Assert.Single(loaded.SeaSites);
        Assert.Equal("Site1", loaded.SeaSites[0].Id);
        Assert.Contains("research_fcr", loaded.UnlockedResearchIds);
        Assert.Contains("EU", loaded.ExportMarkets);
        Assert.Contains("first_harvest", loaded.UnlockedAchievementIds);
        Assert.Contains("step_01", loaded.CompletedTutorialStepIds);
    }

    [Fact]
    public void SaveLoad_PreservesScalarProperties()
    {
        var state = CreateBasicState();
        state.Day = 365;
        state.Balance = 5_000_000m;
        state.ActiveResearchId = "research_breeding";
        state.ResearchProgress = 15;
        state.AcquisitionCount = 3;
        state.ContractActive = true;
        state.OwnsFeedMill = true;
        state.OwnsHatchery = true;
        state.OwnsProcessingPlant = false;
        state.HasASCCertification = true;
        state.BestFCR = 1.15;
        state.BestGrowthBreedingValue = 120.5;
        state.BestDiseaseResistanceBreedingValue = 95.3;
        state.MinimumBalanceEver = -500_000m;
        state.FeedSustainabilityRating = 0.85;
        state.SustainabilityScore = 75;
        state.SustainabilityCertification = CertificationLevel.ASCCertified;
        state.ActiveScenarioId = "familiegarden";
        state.ScenarioIsWon = false;
        state.ScenarioIsLost = false;
        state.ScenarioLossReason = "";
        state.ScenarioStartDay = 0;
        state.TutorialCurrentStepIndex = 5;

        _manager.SaveGame(state, "test");
        var loaded = _manager.LoadGame("test");

        Assert.Equal(365, loaded.Day);
        Assert.Equal(5_000_000m, loaded.Balance);
        Assert.Equal("research_breeding", loaded.ActiveResearchId);
        Assert.Equal(15, loaded.ResearchProgress);
        Assert.Equal(3, loaded.AcquisitionCount);
        Assert.True(loaded.ContractActive);
        Assert.True(loaded.OwnsFeedMill);
        Assert.True(loaded.OwnsHatchery);
        Assert.False(loaded.OwnsProcessingPlant);
        Assert.True(loaded.HasASCCertification);
        Assert.Equal(1.15, loaded.BestFCR, precision: 2);
        Assert.Equal(120.5, loaded.BestGrowthBreedingValue, precision: 1);
        Assert.Equal(95.3, loaded.BestDiseaseResistanceBreedingValue, precision: 1);
        Assert.Equal(-500_000m, loaded.MinimumBalanceEver);
        Assert.Equal(0.85, loaded.FeedSustainabilityRating, precision: 2);
        Assert.Equal(75, loaded.SustainabilityScore);
        Assert.Equal(CertificationLevel.ASCCertified, loaded.SustainabilityCertification);
        Assert.Equal("familiegarden", loaded.ActiveScenarioId);
        Assert.False(loaded.ScenarioIsWon);
        Assert.False(loaded.ScenarioIsLost);
        Assert.Equal("", loaded.ScenarioLossReason);
        Assert.Equal(0, loaded.ScenarioStartDay);
        Assert.Equal(5, loaded.TutorialCurrentStepIndex);
    }

    [Fact]
    public void SaveLoad_PreservesAnalytics()
    {
        var state = CreateBasicState();
        state.DailySnapshots.Add(new DailySnapshot
        {
            Day = 1,
            BalanceNok = 1_000_000.0,
            FishCount = 5000,
            AvgWeightKg = 0.1
        });

        _manager.SaveGame(state, "test");
        var loaded = _manager.LoadGame("test");

        Assert.Single(loaded.DailySnapshots);
        Assert.Equal(1, loaded.DailySnapshots[0].Day);
        Assert.Equal(1_000_000.0, loaded.DailySnapshots[0].BalanceNok);
        Assert.Equal(5000, loaded.DailySnapshots[0].FishCount);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Backward Compatibility Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Load_MissingProperties_UsesDefaults()
    {
        // Simulate an old save file missing new properties
        var minimalJson = @"{
            ""Day"": 100,
            ""Balance"": 2000000
        }";

        File.WriteAllText(Path.Combine(_testDir, "old_save.json"), minimalJson);
        var loaded = _manager.LoadGame("old_save");

        Assert.Equal(100, loaded.Day);
        Assert.Equal(2000000m, loaded.Balance);
        Assert.Empty(loaded.Zones);
        Assert.Empty(loaded.SeaSites);
        Assert.Empty(loaded.Populations);
        Assert.Equal(double.MaxValue, loaded.BestFCR);
        Assert.Equal(decimal.MaxValue, loaded.MinimumBalanceEver);
        Assert.Equal(0.7, loaded.FeedSustainabilityRating, precision: 2);
        Assert.Equal("", loaded.ScenarioLossReason);
    }

    [Fact]
    public void Load_NullCollections_InitializesToEmpty()
    {
        var jsonWithNulls = @"{
            ""Day"": 50,
            ""Balance"": 1500000,
            ""Zones"": null,
            ""SeaSites"": null,
            ""Populations"": null,
            ""DailySnapshots"": null,
            ""UnlockedResearchIds"": null,
            ""ExportMarkets"": null
        }";

        File.WriteAllText(Path.Combine(_testDir, "null_save.json"), jsonWithNulls);
        var loaded = _manager.LoadGame("null_save");

        Assert.NotNull(loaded.Zones);
        Assert.Empty(loaded.Zones);
        Assert.NotNull(loaded.SeaSites);
        Assert.Empty(loaded.SeaSites);
        Assert.NotNull(loaded.Populations);
        Assert.Empty(loaded.Populations);
        Assert.NotNull(loaded.DailySnapshots);
        Assert.Empty(loaded.DailySnapshots);
        Assert.NotNull(loaded.UnlockedResearchIds);
        Assert.Empty(loaded.UnlockedResearchIds);
        Assert.NotNull(loaded.ExportMarkets);
        Assert.Empty(loaded.ExportMarkets);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Error Handling Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TryLoadWithFallback_PrimaryCorrupted_LoadsFromBackup()
    {
        var goodState = CreateBasicState();
        goodState.Day = 100;
        _manager.SaveGame(goodState, "test");

        var corruptedState = CreateBasicState();
        corruptedState.Day = 200;
        _manager.SaveGame(corruptedState, "test");

        // Corrupt the primary file
        var primaryPath = Path.Combine(_testDir, "test.json");
        File.WriteAllText(primaryPath, "{ corrupted json");

        var loaded = _manager.TryLoadWithFallback("test");

        Assert.NotNull(loaded);
        Assert.Equal(100, loaded.Day);
    }

    [Fact]
    public void TryLoadWithFallback_AllCorrupted_ReturnsNull()
    {
        // Create a save and backups
        var state = CreateBasicState();
        _manager.SaveGame(state, "test");
        _manager.SaveGame(state, "test");
        _manager.SaveGame(state, "test");

        // Corrupt all files
        var primaryPath = Path.Combine(_testDir, "test.json");
        File.WriteAllText(primaryPath, "{ corrupted");
        File.WriteAllText(primaryPath + ".bak1", "{ also corrupted");
        File.WriteAllText(primaryPath + ".bak2", "{ still corrupted");

        var loaded = _manager.TryLoadWithFallback("test");

        Assert.Null(loaded);
    }

    [Fact]
    public void TryLoadWithFallback_NoFiles_ReturnsNull()
    {
        var loaded = _manager.TryLoadWithFallback("nonexistent");
        Assert.Null(loaded);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // STRESS TESTS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void StressTest_LargeGameState()
    {
        // Create a massive game state: 10 zones, 100k+ fish (20 populations), 50+ days of analytics
        var state = CreateBasicState();
        state.Day = 365;

        // 10 zones
        for (int i = 0; i < 10; i++)
        {
            state.Zones.Add(new Environment.Zone($"PO{i + 1}", temperature: 10.0 + i * 0.5));
        }

        // 10 sea sites (1 per zone)
        for (int i = 0; i < 10; i++)
        {
            state.SeaSites.Add(new Farm.SeaSite($"Site_{i}", $"PO{i + 1}"));
        }

        // 20 populations (100k+ fish total)
        for (int i = 0; i < 20; i++)
        {
            var pop = new FishPopulation();
            pop.Id = $"Pop_{i}";
            pop.ZoneId = $"PO{(i % 10) + 1}";
            // Each population gets a cohort with 5000 fish
            pop.Cohorts.Add(new FishPopulation.Cohort(
                365 + i * 10,
                5000,
                3.0 + i * 0.1,
                Data.LifeStage.Seawater,
                null
            ));
            state.Populations.Add(pop);
        }

        // 50+ days of analytics
        for (int day = 1; day <= 50; day++)
        {
            state.DailySnapshots.Add(new DailySnapshot
            {
                Day = day,
                BalanceNok = 1_000_000.0 + day * 10_000.0,
                FishCount = 100_000 - day * 100,
                AvgWeightKg = 0.1 + day * 0.01
            });
        }

        // Add event log entries
        for (int i = 0; i < 100; i++)
        {
            state.EventLog.Add(new Events.EventRecord
            {
                Id = $"event_{i}",
                Day = i,
                Name = $"Event {i}",
                Description = $"Description for event {i}",
                Category = Events.EventCategory.Weather
            });
        }

        var startTime = DateTime.Now;
        _manager.SaveGame(state, "large_save");
        var saveTime = (DateTime.Now - startTime).TotalSeconds;

        startTime = DateTime.Now;
        var loaded = _manager.LoadGame("large_save");
        var loadTime = (DateTime.Now - startTime).TotalSeconds;

        // Performance requirements: <2s for save and load
        Assert.True(saveTime < 2.0, $"Save took {saveTime:F2}s, should be <2s");
        Assert.True(loadTime < 2.0, $"Load took {loadTime:F2}s, should be <2s");

        // Verify data integrity
        Assert.Equal(10, loaded.Zones.Count);
        Assert.Equal(10, loaded.SeaSites.Count);
        Assert.Equal(20, loaded.Populations.Count);
        Assert.Equal(50, loaded.DailySnapshots.Count);
        Assert.Equal(100, loaded.EventLog.Count);
        
        // Total fish count
        var totalFish = loaded.Populations.Sum(p => p.Cohorts.Sum(c => c.Count));
        Assert.True(totalFish >= 100_000, $"Total fish: {totalFish}, expected >= 100,000");
    }

    [Fact]
    public void StressTest_RapidSaveLoadCycles()
    {
        // Save → Load → Save loop (10 iterations)
        var state = CreateBasicState();
        state.Day = 1;

        for (int cycle = 0; cycle < 10; cycle++)
        {
            state.Day = cycle * 10;
            state.Balance = 1_000_000m + cycle * 100_000m;

            _manager.SaveGame(state, "cycle_test");
            var loaded = _manager.LoadGame("cycle_test");

            Assert.Equal(cycle * 10, loaded.Day);
            Assert.Equal(1_000_000m + cycle * 100_000m, loaded.Balance);

            state = loaded;
        }

        // Final state should be cycle 9
        Assert.Equal(90, state.Day);
        Assert.Equal(1_900_000m, state.Balance);
    }

    [Fact]
    public void StressTest_CorruptedSave_PartialJson()
    {
        // Test various corruption scenarios
        var testCases = new[]
        {
            "{ \"Day\": 100",  // Missing closing brace
            "{ \"Day\": 100, \"Balance\":",  // Incomplete value
            "{",  // Empty object start
            "",  // Completely empty
            "not json at all"
        };

        foreach (var corruptedJson in testCases)
        {
            var saveName = $"corrupt_{Array.IndexOf(testCases, corruptedJson)}";
            File.WriteAllText(Path.Combine(_testDir, $"{saveName}.json"), corruptedJson);

            // Should throw when loading corrupted save
            Assert.ThrowsAny<Exception>(() => _manager.LoadGame(saveName));

            // But TryLoadWithFallback should handle it gracefully
            var loaded = _manager.TryLoadWithFallback(saveName);
            Assert.Null(loaded);
        }
    }

    [Fact]
    public void StressTest_CorruptedSave_MissingFields()
    {
        // Save with missing critical fields should still load with defaults
        var incompleteJson = @"{
            ""Day"": 50,
            ""Zones"": [
                { ""Id"": ""PO1"" }
            ]
        }";

        File.WriteAllText(Path.Combine(_testDir, "incomplete.json"), incompleteJson);
        var loaded = _manager.LoadGame("incomplete");

        Assert.Equal(50, loaded.Day);
        Assert.Equal(0m, loaded.Balance); // Default
        Assert.Single(loaded.Zones);
        Assert.Equal("PO1", loaded.Zones[0].Id);
    }

    [Fact]
    public async Task StressTest_ConcurrentSaves()
    {
        // Test concurrent save requests (race conditions) - different slots
        var tasks = new List<Task>();

        for (int i = 0; i < 10; i++)
        {
            int threadId = i;
            tasks.Add(Task.Run(() =>
            {
                var localState = CreateBasicState();
                localState.Day = threadId * 10;
                localState.Balance = threadId * 100_000m;

                _manager.SaveGame(localState, $"concurrent_{threadId}");
            }));
        }

        await Task.WhenAll(tasks);

        // Verify all saves completed successfully
        for (int i = 0; i < 10; i++)
        {
            var loaded = _manager.LoadGame($"concurrent_{i}");
            Assert.Equal(i * 10, loaded.Day);
            Assert.Equal(i * 100_000m, loaded.Balance);
        }
    }

    // Concurrent saves to one slot. The test this replaces EXPECTED an
    // IOException from 20 concurrent saves ("file locking prevents data
    // corruption"): that held on macOS/Windows by timing and failed on Linux,
    // which has no mandatory file locks (CI run 38616565220). Saves to a slot
    // are now serialized and land by atomic replace on every OS, so the
    // replacement asserts what a player needs instead: no save fails, and the
    // slot and every backup always hold one complete save.

    private const string SharedSlot = "shared_slot";

    [Fact]
    public async Task StressTest_ConcurrentSavesToSameSlot()
    {
        const int writers = 20;
        var references = WriterSaves(writers);
        using var go = new ManualResetEventSlim(false);
        var exceptions = new ConcurrentQueue<Exception>();

        var tasks = Enumerable.Range(0, writers).Select(writer => Task.Factory.StartNew(() =>
        {
            go.Wait();
            try { _manager.SaveGame(WriterState(writer), SharedSlot); }
            catch (Exception ex) { exceptions.Enqueue(ex); }
        }, TaskCreationOptions.LongRunning)).ToArray();
        go.Set();
        await Task.WhenAll(tasks);

        Assert.True(exceptions.IsEmpty, "every concurrent save must succeed: " + string.Join("; ", exceptions));

        // The slot is byte for byte one writer's complete save, and loads as that writer's state.
        string slotPath = Path.Combine(_testDir, SharedSlot + ".json");
        int slotWriter = WriterOf(File.ReadAllBytes(slotPath), references, slotPath);
        var loaded = _manager.LoadGame(SharedSlot);
        Assert.Equal((slotWriter, slotWriter * 1000m), (loaded.Day, loaded.Balance));

        // The saves ran one after another: the slot and its three backups are
        // four complete saves by four different writers.
        var generations = new List<int> { slotWriter };
        for (int i = 1; i <= SaveLoadManager.MaxBackups; i++)
        {
            string backup = slotPath + $".bak{i}";
            Assert.True(File.Exists(backup), $"{Path.GetFileName(backup)} is missing");
            generations.Add(WriterOf(File.ReadAllBytes(backup), references, backup));
        }
        Assert.Equal(generations.Count, generations.Distinct().Count());

        // Nothing else is left behind: no temp file of any save.
        AssertOnlySlotFilesRemain();
    }

    [Fact]
    public async Task StressTest_LoadsDuringConcurrentSavesToSameSlot_AlwaysSeeOneCompleteSave()
    {
        const int writers = 8;
        const int savesPerWriter = 12;
        const int readers = 2;
        var references = WriterSaves(writers);
        string slotPath = Path.Combine(_testDir, SharedSlot + ".json");
        _manager.SaveGame(WriterState(0), SharedSlot); // the slot exists before anyone reads it

        int writersRunning = writers;
        using var readersStarted = new CountdownEvent(readers);
        using var go = new ManualResetEventSlim(false);

        // One load through the manager and one raw read of the slot; never throws.
        void ReadOnce(int load, List<string> failures)
        {
            try
            {
                var state = _manager.LoadGame(SharedSlot);
                if (state.Day < 0 || state.Day >= writers || state.Balance != state.Day * 1000m)
                    failures.Add($"load {load}: Day {state.Day} / Balance {state.Balance} is no writer's save");
            }
            catch (Exception ex)
            {
                failures.Add($"load {load}: {ex.GetType().Name}: {ex.Message}");
            }

            try
            {
                byte[] bytes = ReadAllBytesShared(slotPath);
                if (!references.Any(reference => reference.AsSpan().SequenceEqual(bytes)))
                    failures.Add($"read {load}: {bytes.Length} bytes that are no writer's complete save");
            }
            catch (Exception ex)
            {
                failures.Add($"read {load}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        var readerTasks = Enumerable.Range(0, readers).Select(_ => Task.Factory.StartNew(() =>
        {
            var failures = new List<string>();
            int loads = 0;
            do
            {
                ReadOnce(loads, failures);
                if (++loads == 1) readersStarted.Signal();
            } while (Volatile.Read(ref writersRunning) > 0);
            return (Loads: loads, Failures: failures);
        }, TaskCreationOptions.LongRunning)).ToArray();

        readersStarted.Wait(); // every reader is in its loop before the first write
        var writerTasks = Enumerable.Range(0, writers).Select(writer => Task.Factory.StartNew(() =>
        {
            go.Wait();
            try
            {
                for (int save = 0; save < savesPerWriter; save++)
                    _manager.SaveGame(WriterState(writer), SharedSlot);
            }
            finally
            {
                Interlocked.Decrement(ref writersRunning);
            }
        }, TaskCreationOptions.LongRunning)).ToArray();
        go.Set();

        await Task.WhenAll(writerTasks); // a save that throws fails the test here
        var results = await Task.WhenAll(readerTasks);

        Assert.All(results, result => Assert.True(result.Loads > 0));
        var failures = results.SelectMany(result => result.Failures).ToList();
        Assert.True(failures.Count == 0,
            $"{failures.Count} of {results.Sum(r => r.Loads)} reads during the saves saw no complete save: "
            + string.Join("; ", failures.Take(5)));
        WriterOf(File.ReadAllBytes(slotPath), references, slotPath);
        AssertOnlySlotFilesRemain();
    }

    [Fact]
    public void SaveGame_ThatCannotCommit_LeavesTheSlotAsItWasAndNoTempFile()
    {
        var references = WriterSaves(3);
        _manager.SaveGame(WriterState(1), SharedSlot);
        string slotPath = Path.Combine(_testDir, SharedSlot + ".json");
        // A directory holds the .bak1 name, so the replace that would move the
        // current save there fails (IOException or UnauthorizedAccessException by OS).
        Directory.CreateDirectory(slotPath + ".bak1");

        Assert.ThrowsAny<Exception>(() => _manager.SaveGame(WriterState(2), SharedSlot));

        Assert.Equal(1, WriterOf(File.ReadAllBytes(slotPath), references, slotPath));
        Assert.Equal(1, _manager.LoadGame(SharedSlot).Day);
        Assert.Equal(new[] { SharedSlot + ".json" }, Directory.GetFiles(_testDir).Select(path => Path.GetFileName(path)));
    }

    [Fact]
    public void StressTest_ExtremelyLargeState()
    {
        // Push limits: 500k fish, 100 zones, 365 days analytics
        var state = CreateBasicState();
        state.Day = 365;

        // 100 zones
        for (int i = 0; i < 100; i++)
        {
            state.Zones.Add(new Environment.Zone($"Zone_{i}", temperature: 8.0 + (i % 10) * 0.5));
        }

        // 100 populations of 5k each = 500k fish
        for (int i = 0; i < 100; i++)
        {
            var pop = new FishPopulation();
            pop.Id = $"Pop_{i}";
            pop.ZoneId = $"Zone_{i}";
            pop.Cohorts.Add(new FishPopulation.Cohort(
                400,
                5000,
                3.0 + i * 0.01,
                Data.LifeStage.Seawater,
                null
            ));
            state.Populations.Add(pop);
        }

        // Full year of daily analytics
        for (int day = 1; day <= 365; day++)
        {
            state.DailySnapshots.Add(new DailySnapshot
            {
                Day = day,
                BalanceNok = 10_000_000.0 + day * 50_000.0,
                FishCount = 500_000 - day * 100,
                AvgWeightKg = 0.1 + day * 0.005
            });
        }

        var startTime = DateTime.Now;
        _manager.SaveGame(state, "extreme_save");
        var saveTime = (DateTime.Now - startTime).TotalSeconds;

        startTime = DateTime.Now;
        var loaded = _manager.LoadGame("extreme_save");
        var loadTime = (DateTime.Now - startTime).TotalSeconds;

        // More lenient for extreme case, but should still be reasonable
        Assert.True(saveTime < 5.0, $"Extreme save took {saveTime:F2}s, should be <5s");
        Assert.True(loadTime < 5.0, $"Extreme load took {loadTime:F2}s, should be <5s");

        Assert.Equal(100, loaded.Zones.Count);
        Assert.Equal(100, loaded.Populations.Count);
        Assert.Equal(365, loaded.DailySnapshots.Count);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helper Methods
    // ═══════════════════════════════════════════════════════════════════════

    private static GameState CreateBasicState()
    {
        return new GameState
        {
            Day = 0,
            Balance = 1_000_000m
        };
    }

    /// <summary>
    /// Writer <paramref name="writer"/>'s state: Day = writer, Balance = writer × 1000,
    /// and a year of analytics, so a save is ~100 KB and takes many writes.
    /// </summary>
    private static GameState WriterState(int writer)
    {
        var state = new GameState { Day = writer, Balance = writer * 1000m };
        for (int day = 1; day <= 365; day++)
        {
            state.DailySnapshots.Add(new DailySnapshot
            {
                Day = day,
                BalanceNok = writer * 1_000_000.0 + day,
                FishCount = writer * 1000 + day,
                AvgWeightKg = 0.1 + day * 0.01
            });
        }
        return state;
    }

    /// <summary>
    /// The exact bytes of each writer's save, from the save format itself
    /// (UTF-8 JSON through <see cref="SaveJson.Options"/>), not from SaveGame.
    /// </summary>
    private static byte[][] WriterSaves(int writers) =>
        Enumerable.Range(0, writers)
            .Select(writer => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(WriterState(writer), SaveJson.Options)))
            .ToArray();

    /// <summary>The writer whose complete save <paramref name="bytes"/> is; fails otherwise.</summary>
    private static int WriterOf(byte[] bytes, byte[][] references, string path)
    {
        int writer = Array.FindIndex(references, reference => reference.AsSpan().SequenceEqual(bytes));
        Assert.True(writer >= 0, $"{Path.GetFileName(path)} ({bytes.Length} bytes) is no writer's complete save");
        return writer;
    }

    /// <summary>
    /// Reads a file the way the game's loader does: without blocking a writer
    /// that replaces it (on Windows a plain read would hold the file open).
    /// </summary>
    private static byte[] ReadAllBytesShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private void AssertOnlySlotFilesRemain()
    {
        var expected = new[] { "", ".bak1", ".bak2", ".bak3" }.Select(suffix => SharedSlot + ".json" + suffix);
        var left = Directory.GetFiles(_testDir).Select(path => Path.GetFileName(path)).OrderBy(name => name, StringComparer.Ordinal);
        Assert.Equal(expected.OrderBy(name => name, StringComparer.Ordinal), left);
    }
}
