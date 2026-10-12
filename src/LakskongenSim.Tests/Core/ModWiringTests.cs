using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Events;
using Xunit;

namespace LakskongenSim.Tests.Core
{
    /// <summary>
    /// Tests for Phase 4.6 mod wiring — verifies that mod data flows correctly
    /// from ModLoader into the engine systems (Storyteller, ResearchSystem,
    /// ScenarioSystem).
    /// </summary>
    public class ModWiringTests : IDisposable
    {
        private readonly string _testModsDirectory;

        public ModWiringTests()
        {
            _testModsDirectory = Path.Combine(Path.GetTempPath(), "LakskongenModWiring_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_testModsDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testModsDirectory))
                Directory.Delete(_testModsDirectory, true);
        }

        private void CreateModWithFiles(string modId, Dictionary<string, string> files)
        {
            var modPath = Path.Combine(_testModsDirectory, modId);
            Directory.CreateDirectory(modPath);

            var manifest = new ModManifest
            {
                Id = modId,
                Name = $"Mod {modId}",
                Author = "Test",
                Version = "1.0.0",
                Description = "Test mod",
                Files = new List<string>(files.Keys),
                Dependencies = new List<string>()
            };

            var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(modPath, "manifest.json"), manifestJson);

            foreach (var (fileName, content) in files)
            {
                File.WriteAllText(Path.Combine(modPath, fileName), content);
            }
        }

        [Fact]
        public void ModEvents_AreRegisteredInStoryteller()
        {
            // Arrange
            CreateModWithFiles("eventmod", new Dictionary<string, string>
            {
                ["events_test.json"] = """[{"id":"mod_event_1","name":"Mod Event","category":"General","weight":2.0,"cooldown_days":10,"description":"A mod event","effects":{"balance_change":50000.0}}]"""
            });

            var modLoader = new ModLoader(_testModsDirectory);
            modLoader.DiscoverAndLoad();

            var time = new TimeManager();
            var state = new GameState();
            var finance = new Finance.FinanceManager(1_000_000m);
            var storyteller = new Storyteller(time, state, finance, enableDefaultEvents: false);

            // Act
            var modEvents = modLoader.BuildEventDefinitions(finance);
            foreach (var evt in modEvents)
                storyteller.RegisterEvent(evt);

            // Assert
            var allEvents = storyteller.GetEventDefinitions();
            Assert.Contains(allEvents, e => e.Id == "mod_event_1");
        }

        [Fact]
        public void ModResearchNodes_AreAddedToResearchSystem()
        {
            // Arrange
            CreateModWithFiles("researchmod", new Dictionary<string, string>
            {
                ["research_test.json"] = """[{"id":"mod_research_1","name":"Mod Research","description":"A mod research node","branch":"Biology","tier":1,"cost_nok":500000,"research_days":30,"prerequisites":[],"effects":{"growth_rate_bonus":0.05}}]"""
            });

            var modLoader = new ModLoader(_testModsDirectory);
            modLoader.DiscoverAndLoad();
            var research = new ResearchSystem();

            // Act
            var modNodes = modLoader.GetAllResearchNodes();
            research.AddModNodes(modNodes);

            // Assert
            Assert.True(research.Nodes.ContainsKey("mod_research_1"));
            var node = research.Nodes["mod_research_1"];
            Assert.Equal("Mod Research", node.Name);
            Assert.Equal(ResearchBranch.Biology, node.Branch);
        }

        [Fact]
        public void ModResearchNodes_DoNotOverwriteCoreNodes()
        {
            // Arrange — mod tries to add a node with the same ID as a core node
            CreateModWithFiles("conflictmod", new Dictionary<string, string>
            {
                ["research_conflict.json"] = """[{"id":"selective-breeding","name":"Fake Breeding","description":"Should not overwrite","branch":"Biology","tier":1,"cost_nok":1,"research_days":1,"prerequisites":[],"effects":{}}]"""
            });

            var modLoader = new ModLoader(_testModsDirectory);
            modLoader.DiscoverAndLoad();
            var research = new ResearchSystem();
            var originalNode = research.Nodes["selective-breeding"];

            // Act
            var modNodes = modLoader.GetAllResearchNodes();
            research.AddModNodes(modNodes);

            // Assert — core node unchanged
            Assert.Equal(originalNode.Name, research.Nodes["selective-breeding"].Name);
            Assert.NotEqual("Fake Breeding", research.Nodes["selective-breeding"].Name);
        }

        [Fact]
        public void ModScenarios_AreRegisteredInScenarioSystem()
        {
            // Arrange
            CreateModWithFiles("scenariomod", new Dictionary<string, string>
            {
                ["scenarios_test.json"] = """[{"id":"mod_scenario_1","name":"Mod Scenario","description":"A mod scenario","starting_capital_nok":2000000,"starting_region":"Nordland","difficulty":3,"win_condition":"Profit","initial_balance_nok":2000000,"starting_fish_count":10000,"initial_events":[]}]"""
            });

            var modLoader = new ModLoader(_testModsDirectory);
            modLoader.DiscoverAndLoad();
            var state = new GameState();
            var finance = new Finance.FinanceManager(1_000_000m);
            var scenarioSystem = new ScenarioSystem(state, finance);

            // Act
            var modScenarios = modLoader.GetAllScenarios();
            scenarioSystem.RegisterModScenarios(modScenarios);

            // Assert
            Assert.True(scenarioSystem.All.ContainsKey("mod_scenario_1"));
            Assert.Equal("Mod Scenario", scenarioSystem.All["mod_scenario_1"].Name);
        }

        [Fact]
        public void ModScenarios_DoNotOverwriteCoreScenarios()
        {
            // Arrange
            CreateModWithFiles("conflictscen", new Dictionary<string, string>
            {
                ["scenarios_conflict.json"] = """[{"id":"forste_sesong","name":"Fake Scenario","description":"Should not overwrite","starting_capital_nok":1,"starting_region":"Vestland","difficulty":1,"win_condition":"Profit","initial_balance_nok":1,"starting_fish_count":1,"initial_events":[]}]"""
            });

            var modLoader = new ModLoader(_testModsDirectory);
            modLoader.DiscoverAndLoad();
            var state = new GameState();
            var finance = new Finance.FinanceManager(1_000_000m);
            var scenarioSystem = new ScenarioSystem(state, finance);
            var originalName = scenarioSystem.All["forste_sesong"].Name;

            // Act
            var modScenarios = modLoader.GetAllScenarios();
            scenarioSystem.RegisterModScenarios(modScenarios);

            // Assert
            Assert.Equal(originalName, scenarioSystem.All["forste_sesong"].Name);
        }

        [Fact]
        public void ModFishSpecies_AreLoaded()
        {
            // Arrange
            CreateModWithFiles("fishmod", new Dictionary<string, string>
            {
                ["fish_test.json"] = """[{"id":"test_trout","name":"Test Trout","scientific_name":"Oncorhynchus mykiss","description":"A test species","growth_rate_multiplier":1.2,"optimal_temp_min":8.0,"optimal_temp_max":16.0,"base_fcr":1.1,"price_multiplier":0.8,"max_harvest_weight_kg":4.0}]"""
            });

            var modLoader = new ModLoader(_testModsDirectory);
            modLoader.DiscoverAndLoad();

            // Act
            var species = modLoader.GetAllFishSpecies();

            // Assert
            Assert.Single(species);
            Assert.Equal("test_trout", species[0].Id);
            Assert.Equal("Test Trout", species[0].Name);
            Assert.Equal(1.2, species[0].GrowthRateMultiplier);
        }

        [Fact]
        public void EmptyModsDirectory_LoadsSuccessfully()
        {
            // Arrange — empty mods directory
            var modLoader = new ModLoader(_testModsDirectory);

            // Act
            var mods = modLoader.DiscoverAndLoad();

            // Assert
            Assert.Empty(mods);
        }

        [Fact]
        public void ModDependencies_AreResolved()
        {
            // Arrange — mod2 depends on mod1
            CreateModWithFiles("mod1", new Dictionary<string, string>
            {
                ["events_mod1.json"] = """[{"id":"evt1","name":"Event 1","category":"General","weight":1.0,"cooldown_days":30,"description":"","effects":{}}]"""
            });
            CreateModWithFiles("mod2", new Dictionary<string, string>
            {
                ["events_mod2.json"] = """[{"id":"evt2","name":"Event 2","category":"General","weight":1.0,"cooldown_days":30,"description":"","effects":{}}]"""
            });
            // Add dependency
            var mod2Path = Path.Combine(_testModsDirectory, "mod2", "manifest.json");
            var manifest = JsonSerializer.Deserialize<ModManifest>(File.ReadAllText(mod2Path));
            manifest.Dependencies.Add("mod1");
            File.WriteAllText(mod2Path, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

            var modLoader = new ModLoader(_testModsDirectory);

            // Act
            var mods = modLoader.DiscoverAndLoad();

            // Assert — both loaded, mod1 before mod2
            Assert.Equal(2, mods.Count);
            Assert.Equal("mod1", mods[0].Manifest.Id);
            Assert.Equal("mod2", mods[1].Manifest.Id);
        }

        [Fact]
        public void EngineLoadMods_AppliesEventsResearchAndScenarios()
        {
            // Arrange — one mod carrying one of each content type.
            CreateModWithFiles("enginemod", new Dictionary<string, string>
            {
                ["events_engine.json"] = """[{"id":"mod_engine_event","name":"Engine Event","category":"General","weight":1.0,"cooldown_days":30,"description":"","effects":{}}]""",
                ["research_engine.json"] = """[{"id":"mod_engine_research","name":"Engine Research","description":"","branch":"Biology","tier":1,"cost_nok":1000,"research_days":1,"prerequisites":[],"effects":{}}]""",
                ["scenarios_engine.json"] = """[{"id":"mod_engine_scenario","name":"Engine Scenario","description":"","starting_capital_nok":1000000,"starting_region":"Nordland","difficulty":2,"win_condition":"Profit","initial_balance_nok":1000000,"starting_fish_count":100,"initial_events":[]}]"""
            });

            var engine = new SimulationEngine();
            engine.FinanceManager = new Finance.FinanceManager(1_000_000m);

            // Act — configure before Initialize; mods apply during Initialize.
            engine.LoadMods(_testModsDirectory);
            engine.Initialize(enableEvents: false, seed: 1234);

            // Assert — all three content types reached the engine systems.
            Assert.Contains(engine.Storyteller.GetEventDefinitions(), e => e.Id == "mod_engine_event");
            Assert.True(engine.Research.Nodes.ContainsKey("mod_engine_research"));
            Assert.True(engine.Scenario.All.ContainsKey("mod_engine_scenario"));

            // A second Initialize (New Game) re-applies onto fresh systems
            // without double-registering events.
            engine.Initialize(enableEvents: false, seed: 1234);
            Assert.Single(engine.Storyteller.GetEventDefinitions()
                .Where(e => e.Id == "mod_engine_event"));
            Assert.True(engine.Research.Nodes.ContainsKey("mod_engine_research"));
            Assert.True(engine.Scenario.All.ContainsKey("mod_engine_scenario"));
        }
    }
}
