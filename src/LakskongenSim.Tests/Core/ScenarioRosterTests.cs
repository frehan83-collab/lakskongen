using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Finance;
using Xunit;

namespace LakskongenSim.Tests.Core
{
    /// <summary>
    /// The scenario picker's roster (roadmap U1-07): every scenario is open
    /// until progression lands (U2-03), the data file agrees with the
    /// built-in roster on what the picker shows, and mod scenarios follow the
    /// mods directory the engine is pointed at.
    /// </summary>
    public class ScenarioRosterTests : IDisposable
    {
        private readonly List<string> _tempDirs = new();

        public void Dispose()
        {
            foreach (var dir in _tempDirs)
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
        }

        [Fact]
        public void EveryScenario_IsOpenUntilProgressionLands()
        {
            var roster = new ScenarioSystem(new GameState());

            var locked = roster.All.Values.Where(s => !s.IsUnlocked).Select(s => s.Id).ToList();

            Assert.Empty(locked);
            Assert.Equal(roster.All.Count, roster.GetUnlocked().Count());
        }

        [Fact]
        public void ScenariosJson_AgreesWithTheBuiltInRoster()
        {
            var roster = new ScenarioSystem(new GameState());
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "godot", "data", "scenarios.json")));

            var ids = new List<string>();
            foreach (var entry in json.RootElement.EnumerateArray())
            {
                var id = entry.GetProperty("id").GetString()!;
                ids.Add(id);
                var builtIn = roster.GetScenario(id);
                Assert.True(builtIn != null, $"{id}: in scenarios.json but not in the roster");
                Assert.Equal(builtIn!.IsUnlocked, entry.GetProperty("is_unlocked").GetBoolean());
                Assert.Equal(builtIn.StartingCapitalNOK, entry.GetProperty("starting_balance").GetDecimal());
            }
            Assert.Equal(roster.All.Keys.OrderBy(k => k), ids.OrderBy(k => k));
        }

        [Fact]
        public void LoadMods_AnotherDirectory_RegistersItsScenarios()
        {
            var first = ModDirectoryWithScenario("scenario_ci_first");
            var second = ModDirectoryWithScenario("scenario_ci_second");
            var engine = new SimulationEngine { FinanceManager = new FinanceManager(5_000_000m) };

            engine.LoadMods(first);
            engine.Initialize(enableEvents: false, seed: 3);
            Assert.NotNull(engine.Scenario.GetScenario("scenario_ci_first"));

            engine.LoadMods(second);
            engine.Initialize(enableEvents: false, seed: 3);

            Assert.NotNull(engine.Scenario.GetScenario("scenario_ci_second"));
            Assert.Null(engine.Scenario.GetScenario("scenario_ci_first"));
        }

        private string ModDirectoryWithScenario(string scenarioId)
        {
            var root = Path.Combine(Path.GetTempPath(), "LakskongenRoster_" + Path.GetRandomFileName());
            _tempDirs.Add(root);
            var modPath = Path.Combine(root, scenarioId + "_pack");
            Directory.CreateDirectory(modPath);
            var manifest = new ModManifest
            {
                Id = scenarioId + "_pack",
                Name = "CI pack",
                Author = "CI",
                Version = "1.0.0",
                Description = "Roster test mod",
                Files = new List<string> { "scenario_ci.json" },
                Dependencies = new List<string>(),
            };
            File.WriteAllText(Path.Combine(modPath, "manifest.json"), JsonSerializer.Serialize(manifest));
            File.WriteAllText(Path.Combine(modPath, "scenario_ci.json"),
                $$"""[{"id":"{{scenarioId}}","name":"CI","description":"CI scenario","starting_region":"Vestland","difficulty":2,"win_condition":"Profit","initial_balance_nok":5000000}]""");
            return root;
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LakskongenSim.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
        }
    }
}
