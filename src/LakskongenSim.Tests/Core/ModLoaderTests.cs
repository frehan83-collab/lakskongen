using System.IO;
using System.Linq;
using Xunit;
using LakskongenSim.Core;
using LakskongenSim.Events; // Assuming EventCategory is needed here

namespace LakskongenSim.Tests.Core
{
    public class ModLoaderTests
    {
        private readonly string _testModsDirectory;
        private readonly string _testModConfigPath;

        public ModLoaderTests()
        {
            // Set up a temporary directory for testing mods
            _testModsDirectory = Path.Combine(Path.GetTempPath(), "LakskongenTestMods_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_testModsDirectory);
            _testModConfigPath = Path.Combine(_testModsDirectory, "mods_config.json");
        }

        // Clean up the temporary directory after all tests in this class run
        public void Dispose()
        {
            if (Directory.Exists(_testModsDirectory))
            {
                Directory.Delete(_testModsDirectory, true);
            }
        }

        private void CreateTestMod(string id, string name, string author, string version, bool enabled = true)
        {
            var modPath = Path.Combine(_testModsDirectory, id);
            Directory.CreateDirectory(modPath);

            var manifest = new ModManifest
            {
                Id = id,
                Name = name,
                Author = author,
                Version = version,
                Description = "A test mod.",
                Files = new System.Collections.Generic.List<string>(),
                Dependencies = new System.Collections.Generic.List<string>(),
                Enabled = enabled // This will be overwritten by config if it exists
            };

            var manifestJson = System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(modPath, "manifest.json"), manifestJson);
        }

        [Fact]
        public void DiscoverAndLoad_LoadsEnabledStateFromConfig()
        {
            // Arrange
            CreateTestMod("mod1", "Test Mod 1", "Author A", "1.0.0", true);
            CreateTestMod("mod2", "Test Mod 2", "Author B", "1.0.0", false);

            // Create a config file that disables mod1 and enables mod2
            var initialConfig = new ModConfiguration();
            initialConfig.Mods.Add(new ModConfigEntry { Id = "mod1", Enabled = false });
            initialConfig.Mods.Add(new ModConfigEntry { Id = "mod2", Enabled = true });
            initialConfig.Save(_testModConfigPath, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

            var modLoader = new ModLoader(_testModsDirectory);

            // Act
            var loadedMods = modLoader.DiscoverAndLoad();

            // Assert — DiscoverAndLoad only loads ENABLED mods
            Assert.NotNull(loadedMods);
            // Config disables mod1, enables mod2 → only mod2 loaded
            Assert.Equal(1, loadedMods.Count);

            var mod2 = loadedMods.FirstOrDefault(m => m.Manifest.Id == "mod2");
            Assert.NotNull(mod2);
            Assert.True(mod2.Manifest.Enabled);
        }

        [Fact]
        public void SetModEnabled_PersistsStateToConfig()
        {
            // Arrange — mod3 starts enabled, gets loaded
            CreateTestMod("mod3", "Test Mod 3", "Author C", "1.0.0", true);
            var modLoader = new ModLoader(_testModsDirectory);
            var mods = modLoader.DiscoverAndLoad();
            Assert.Single(mods); // mod3 loaded

            // Act: Disable mod3 — persists config to disk
            modLoader.SetModEnabled("mod3", false);

            // Re-load: disabled mod should NOT appear in loaded mods
            var newModLoader = new ModLoader(_testModsDirectory);
            var reloadedMods = newModLoader.DiscoverAndLoad();
            Assert.Empty(reloadedMods); // mod3 is disabled → not loaded

            // Verify config file was written with enabled=false
            var configPath = Path.Combine(_testModsDirectory, "mods_config.json");
            Assert.True(File.Exists(configPath), "Config file should exist after SetModEnabled");
            var configJson = File.ReadAllText(configPath);
            Assert.Contains("false", configJson); // enabled field set to false
        }

        [Fact]
        public void BuildEventDefinitions_WithFinance_SettlesCashThroughFinanceManager()
        {
            // Arrange — mod with a cash-grant event
            var modPath = Path.Combine(_testModsDirectory, "cashmod");
            Directory.CreateDirectory(modPath);
            var manifest = new ModManifest
            {
                Id = "cashmod", Name = "Cash Mod", Author = "Test", Version = "1.0.0",
                Files = new System.Collections.Generic.List<string> { "events.json" },
                Dependencies = new System.Collections.Generic.List<string>()
            };
            File.WriteAllText(Path.Combine(modPath, "manifest.json"),
                System.Text.Json.JsonSerializer.Serialize(manifest));
            File.WriteAllText(Path.Combine(modPath, "events.json"),
                """[{"id":"grant","name":"Grant","category":"General","weight":1.0,"cooldown_days":30,"description":"Cash grant","effects":{"balance_change":250000.0}}]""");

            var modLoader = new ModLoader(_testModsDirectory);
            modLoader.DiscoverAndLoad();
            var finance = new LakskongenSim.Finance.FinanceManager(1_000_000m);
            var defs = modLoader.BuildEventDefinitions(finance);
            var grant = Assert.Single(defs);

            // Act — invoke the built Apply closure directly
            var state = new GameState();
            var record = new EventRecord();
            grant.Apply?.Invoke(state, record);

            // Assert — finance moved, ledger recorded, mirror untouched
            Assert.Equal(1_250_000m, finance.Balance);
            Assert.Equal(250000m, record.BalanceChange);
#pragma warning disable CS0618 // mirror must stay untouched by event settlement
            Assert.Equal(0m, state.Balance);
#pragma warning restore CS0618
        }
    }
}