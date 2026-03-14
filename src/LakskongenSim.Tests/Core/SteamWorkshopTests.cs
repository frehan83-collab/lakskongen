using Xunit;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LakskongenSim.Core;
using System.Collections.Generic;
using System.Text.Json;

namespace LakskongenSim.Tests.Core
{
    public class SteamWorkshopTests : IDisposable
    {
        private readonly string _testModsDirectory;
        private readonly string _testWorkshopDownloadDirectory;
        private readonly ModLoader _modLoader;
        private readonly SteamWorkshopManager _workshopManager;

        public SteamWorkshopTests()
        {
            _testModsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "mods");
            _testWorkshopDownloadDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "workshop_downloads");

            Directory.CreateDirectory(_testModsDirectory);
            Directory.CreateDirectory(_testWorkshopDownloadDirectory);

            _modLoader = new ModLoader(_testModsDirectory);
            _workshopManager = new SteamWorkshopManager(_modLoader, _testWorkshopDownloadDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testModsDirectory))
            {
                Directory.Delete(_testModsDirectory, true);
            }
            if (Directory.Exists(_testWorkshopDownloadDirectory))
            {
                Directory.Delete(_testWorkshopDownloadDirectory, true);
            }
        }

        private async Task CreateDummyMod(string modId, string modName, string version = "1.0.0")
        {
            var modPath = Path.Combine(_testModsDirectory, modId);
            Directory.CreateDirectory(modPath);

            var manifest = new ModManifest
            {
                Id = modId,
                Name = modName,
                Author = "Test Author",
                Version = version,
                Description = "A dummy mod for testing.",
                Files = new List<string> { "data.json" }
            };

            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            await File.WriteAllTextAsync(Path.Combine(modPath, "manifest.json"), JsonSerializer.Serialize(manifest, jsonOptions));
            await File.WriteAllTextAsync(Path.Combine(modPath, "data.json"), "{ \"value\": 123 }");
        }

        [Fact]
        public async Task UploadModAsync_NewMod_ReturnsWorkshopItemWithId()
        {
            // Arrange
            string modId = "test_mod_upload_new";
            string modName = "Test Mod Upload New";
            await CreateDummyMod(modId, modName);
            string modFolderPath = Path.Combine(_testModsDirectory, modId);

            // Act
            var workshopItem = await _workshopManager.UploadModAsync(modFolderPath);

            // Assert
            Assert.NotNull(workshopItem);
            Assert.True(workshopItem.Id > 0);
            Assert.Equal(modName, workshopItem.Title);
            Assert.Equal(modFolderPath, workshopItem.ModFolderPath);
        }

        [Fact]
        public async Task UploadModAsync_UpdateExistingMod_ReturnsUpdatedWorkshopItem()
        {
            // Arrange
            string modId = "test_mod_upload_update";
            string modName = "Test Mod Upload Update";
            await CreateDummyMod(modId, modName, "1.0.0");
            string modFolderPath = Path.Combine(_testModsDirectory, modId);

            var existingWorkshopItem = new WorkshopItem
            {
                Id = 987654321, // Pre-existing workshop ID
                Title = "Old Title",
                Description = "Old Description",
                Version = "0.9.0",
                ModFolderPath = modFolderPath
            };

            // Act
            var updatedWorkshopItem = await _workshopManager.UploadModAsync(modFolderPath, existingWorkshopItem);

            // Assert
            Assert.NotNull(updatedWorkshopItem);
            Assert.Equal(existingWorkshopItem.Id, updatedWorkshopItem.Id);
            Assert.Equal(modName, updatedWorkshopItem.Title); // Should update title from manifest
            Assert.Equal("1.0.0", updatedWorkshopItem.Version);
        }

        [Fact]
        public async Task DownloadModAsync_ValidId_DownloadsAndLoadsMod()
        {
            // Arrange
            ulong workshopItemId = 123456789;
            string targetModsFolder = _testModsDirectory;

            // Ensure ModLoader starts with no mods
            _modLoader.DiscoverAndLoad();
            Assert.Empty(_modLoader.LoadedMods);

            // Act
            var success = await _workshopManager.DownloadModAsync(workshopItemId, targetModsFolder);

            // Assert
            Assert.True(success);

            // Verify mod is downloaded to the target folder
            string expectedModPath = Path.Combine(targetModsFolder, $"downloaded_mod_{workshopItemId}");
            Assert.True(Directory.Exists(expectedModPath));
            Assert.True(File.Exists(Path.Combine(expectedModPath, "manifest.json")));
            Assert.True(File.Exists(Path.Combine(expectedModPath, "data.json")));

            // Verify ModLoader has discovered the new mod
            _modLoader.DiscoverAndLoad(); // Re-trigger discovery explicitly after download
            Assert.NotEmpty(_modLoader.LoadedMods);
            var downloadedMod = _modLoader.LoadedMods.FirstOrDefault(m => m.Manifest.Id == $"downloaded_mod_{workshopItemId}");
            Assert.NotNull(downloadedMod);
            Assert.Equal($"Downloaded Mod {workshopItemId}", downloadedMod.Manifest.Name);
        }

        [Fact]
        public async Task CheckForUpdateAsync_NewerVersionAvailable_ReturnsTrue()
        {
            // Arrange
            ulong workshopItemId = 555555555;
            string localVersion = "1.0.0";
            
            // The GetWorkshopItemDetailsAsync stub returns "1.0.1"

            // Act
            var updateAvailable = await _workshopManager.CheckForUpdateAsync(workshopItemId, localVersion);

            // Assert
            Assert.True(updateAvailable);
        }

        [Fact]
        public async Task CheckForUpdateAsync_NoNewerVersionAvailable_ReturnsFalse()
        {
            // Arrange
            ulong workshopItemId = 666666666;
            string localVersion = "1.0.1";
            
            // The GetWorkshopItemDetailsAsync stub returns "1.0.1"

            // Act
            var updateAvailable = await _workshopManager.CheckForUpdateAsync(workshopItemId, localVersion);

            // Assert
            Assert.False(updateAvailable);
        }
    }
}
