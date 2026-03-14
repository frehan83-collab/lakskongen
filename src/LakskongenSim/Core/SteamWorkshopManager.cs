using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace LakskongenSim.Core
{
    // ── Workshop Item Model ───────────────────────────────────────────────────

    /// <summary>
    /// Represents a mod or item on the Steam Workshop.
    /// </summary>
    public class WorkshopItem
    {
        [JsonPropertyName("id")]
        public ulong Id { get; set; } // Steam Workshop item ID

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("tags")]
        public List<string> Tags { get; set; } = new();

        [JsonPropertyName("preview_image_path")]
        public string PreviewImagePath { get; set; } = string.Empty; // Absolute path to preview image

        [JsonPropertyName("mod_folder_path")]
        public string ModFolderPath { get; set; } = string.Empty; // Absolute path to the mod's content folder

        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0.0"; // Version of the mod, derived from ModManifest
    }

    // ── ISteamWorkshop Interface ──────────────────────────────────────────────

    /// <summary>
    /// Interface for Steam Workshop integration, allowing for dependency injection
    /// and abstraction from the actual Steamworks SDK.
    /// </summary>
    public interface ISteamWorkshop
    {
        Task<WorkshopItem?> UploadModAsync(string modFolderPath, WorkshopItem? existingItem = null);
        Task<bool> DownloadModAsync(ulong workshopItemId, string targetFolderPath);
        Task<bool> SubscribeToModAsync(ulong workshopItemId);
        Task<bool> UnsubscribeFromModAsync(ulong workshopItemId);
        Task<WorkshopItem?> GetWorkshopItemDetailsAsync(ulong workshopItemId);
        Task<bool> CheckForUpdateAsync(ulong workshopItemId, string localModVersion);
        Task<bool> VoteForModAsync(ulong workshopItemId, bool upvote);
        Task<bool> ReportModAsync(ulong workshopItemId, string reason);
    }

    // ── SteamWorkshopManager Implementation ───────────────────────────────────

    /// <summary>
    /// Manages interaction with the Steam Workshop for mod publishing and consumption.
    /// This is an abstraction layer and does not directly use the Steamworks SDK.
    /// </summary>
    public class SteamWorkshopManager : ISteamWorkshop
    {
        private readonly ModLoader _modLoader;
        private readonly string _workshopDownloadPath;
        private readonly JsonSerializerOptions _jsonOptions;

        public SteamWorkshopManager(ModLoader modLoader, string workshopDownloadPath)
        {
            _modLoader = modLoader;
            _workshopDownloadPath = workshopDownloadPath;
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                WriteIndented = true // For readability of saved workshop metadata
            };

            if (!Directory.Exists(_workshopDownloadPath))
            {
                Directory.CreateDirectory(_workshopDownloadPath);
            }
        }

        /// <summary>
        /// Uploads a mod to the Steam Workshop.
        /// Flow: validate mod -> create workshop item -> set metadata -> upload content.
        /// </summary>
        /// <param name="modFolderPath">Absolute path to the mod's root folder.</param>
        /// <param name="existingItem">Optional existing WorkshopItem if updating.</param>
        /// <returns>The created or updated WorkshopItem, or null if upload fails.</returns>
        public async Task<WorkshopItem?> UploadModAsync(string modFolderPath, WorkshopItem? existingItem = null)
        {
            Console.WriteLine($"[SteamWorkshopManager] Attempting to upload mod from: {modFolderPath}");

            // 1. Validate mod
            var manifestPath = Path.Combine(modFolderPath, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                Console.Error.WriteLine($"[SteamWorkshopManager] Mod validation failed: manifest.json not found in {modFolderPath}");
                return null;
            }

            ModManifest? manifest;
            try
            {
                var json = await File.ReadAllTextAsync(manifestPath);
                manifest = JsonSerializer.Deserialize<ModManifest>(json, _jsonOptions);
                if (manifest == null || string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Name))
                {
                    Console.Error.WriteLine($"[SteamWorkshopManager] Mod validation failed: Invalid manifest.json in {modFolderPath}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[SteamWorkshopManager] Error reading manifest.json for {modFolderPath}: {ex.Message}");
                return null;
            }

            // In a real implementation, you'd interact with Steamworks SDK here.
            // For now, we simulate success and assign a dummy ID.
            WorkshopItem workshopItem = existingItem ?? new WorkshopItem();

            if (existingItem == null)
            {
                // Simulate creation of a new workshop item
                workshopItem.Id = GenerateDummyWorkshopId(); // Replace with actual Steamworks ID
                Console.WriteLine($"[SteamWorkshopManager] Simulated new workshop item created with ID: {workshopItem.Id}");
            }
            else
            {
                Console.WriteLine($"[SteamWorkshopManager] Updating existing workshop item with ID: {workshopItem.Id}");
            }

            // Set metadata from manifest
            workshopItem.Title = manifest.Name;
            workshopItem.Description = manifest.Description;
            workshopItem.ModFolderPath = modFolderPath;
            workshopItem.Version = manifest.Version;
            // Tags and preview image would typically be managed via UI or manifest extension

            // Simulate content upload
            // In a real scenario, this would involve zipping the modFolderPath and uploading it
            await Task.Delay(1000); // Simulate network operation

            Console.WriteLine($"[SteamWorkshopManager] Successfully {(existingItem == null ? "uploaded" : "updated")} mod: {workshopItem.Title} ({workshopItem.Id})");
            return workshopItem;
        }

        /// <summary>
        /// Downloads a mod from the Steam Workshop to the specified target folder.
        /// Flow: subscribe -> download -> extract to mods folder -> trigger ModLoader discovery.
        /// </summary>
        /// <param name="workshopItemId">The ID of the workshop item to download.</param>
        /// <param name="targetFolderPath">The folder where the mod content will be extracted.</param>
        /// <returns>True if download and extraction are successful, false otherwise.</returns>
        public async Task<bool> DownloadModAsync(ulong workshopItemId, string targetFolderPath)
        {
            Console.WriteLine($"[SteamWorkshopManager] Attempting to download workshop item: {workshopItemId}");

            // 1. Simulate subscription
            var subscribed = await SubscribeToModAsync(workshopItemId);
            if (!subscribed)
            {
                Console.Error.WriteLine($"[SteamWorkshopManager] Failed to subscribe to item {workshopItemId}.");
                return false;
            }

            // 2. Simulate download
            string tempDownloadDir = Path.Combine(_workshopDownloadPath, workshopItemId.ToString());
            if (Directory.Exists(tempDownloadDir))
            {
                Directory.Delete(tempDownloadDir, true);
            }
            Directory.CreateDirectory(tempDownloadDir);

            // Simulate writing a dummy manifest for the downloaded mod
            var dummyManifest = new ModManifest
            {
                Id = $"downloaded_mod_{workshopItemId}",
                Name = $"Downloaded Mod {workshopItemId}",
                Author = "Workshop",
                Version = "1.0.0",
                Description = $"This is a simulated downloaded mod with ID {workshopItemId}.",
                Files = new List<string> { "data.json" }
            };
            await File.WriteAllTextAsync(Path.Combine(tempDownloadDir, "manifest.json"), JsonSerializer.Serialize(dummyManifest, _jsonOptions));
            await File.WriteAllTextAsync(Path.Combine(tempDownloadDir, "data.json"), "{ \"some_data\": \"value\" }");

            await Task.Delay(2000); // Simulate download time

            // 3. Extract to mods folder (targetFolderPath)
            // In a real scenario, this would involve unpacking the downloaded content (e.g., zip)
            string finalModPath = Path.Combine(targetFolderPath, dummyManifest.Id);
            if (Directory.Exists(finalModPath))
            {
                Directory.Delete(finalModPath, true);
            }
            Directory.Move(tempDownloadDir, finalModPath); // Move the simulated mod

            Console.WriteLine($"[SteamWorkshopManager] Successfully downloaded and extracted mod {workshopItemId} to {finalModPath}");

            // 4. Trigger ModLoader discovery (optional, but good practice after new mods)
            _modLoader.DiscoverAndLoad();
            Console.WriteLine($"[SteamWorkshopManager] ModLoader re-scanned for new mods.");

            return true;
        }

        /// <summary>
        /// Stubs for subscribing to a workshop item.
        /// </summary>
        public async Task<bool> SubscribeToModAsync(ulong workshopItemId)
        {
            Console.WriteLine($"[SteamWorkshopManager] Simulating subscription to workshop item: {workshopItemId}");
            await Task.Delay(500); // Simulate API call
            return true;
        }

        /// <summary>
        /// Stubs for unsubscribing from a workshop item.
        /// </summary>
        public async Task<bool> UnsubscribeFromModAsync(ulong workshopItemId)
        {
            Console.WriteLine($"[SteamWorkshopManager] Simulating unsubscription from workshop item: {workshopItemId}");
            await Task.Delay(500); // Simulate API call
            return true;
        }

        /// <summary>
        /// Stubs for getting workshop item details.
        /// </summary>
        public async Task<WorkshopItem?> GetWorkshopItemDetailsAsync(ulong workshopItemId)
        {
            Console.WriteLine($"[SteamWorkshopManager] Simulating fetching details for workshop item: {workshopItemId}");
            await Task.Delay(750); // Simulate API call
            // Return a dummy item for now
            return new WorkshopItem
            {
                Id = workshopItemId,
                Title = $"Workshop Item {workshopItemId}",
                Description = "A description from the workshop.",
                Version = "1.0.1", // Simulate a newer version available
                Tags = new List<string> { "Farming", "Salmon" }
            };
        }

        /// <summary>
        /// Checks if a newer version of a mod is available on the workshop.
        /// </summary>
        /// <param name="workshopItemId">The ID of the workshop item.</param>
        /// <param name="localModVersion">The version string of the locally installed mod.</param>
        /// <returns>True if an update is available, false otherwise.</returns>
        public async Task<bool> CheckForUpdateAsync(ulong workshopItemId, string localModVersion)
        {
            Console.WriteLine($"[SteamWorkshopManager] Checking for update for item {workshopItemId} (local: {localModVersion})");
            var workshopItem = await GetWorkshopItemDetailsAsync(workshopItemId);
            if (workshopItem == null)
            {
                Console.WriteLine($"[SteamWorkshopManager] Could not get workshop item details for {workshopItemId}.");
                return false;
            }

            // Simple version comparison (e.g., "1.0.0" vs "1.0.1")
            // A more robust solution would use System.Version or semantic versioning library
            if (string.Compare(workshopItem.Version, localModVersion, StringComparison.OrdinalIgnoreCase) > 0)
            {
                Console.WriteLine($"[SteamWorkshopManager] Update available for {workshopItemId}: {workshopItem.Version} > {localModVersion}");
                return true;
            }
            Console.WriteLine($"[SteamWorkshopManager] No update available for {workshopItemId} (workshop: {workshopItem.Version}, local: {localModVersion})");
            return false;
        }

        /// <summary>
        /// Stubs for rating/voting for a workshop item.
        /// </summary>
        public async Task<bool> VoteForModAsync(ulong workshopItemId, bool upvote)
        {
            Console.WriteLine($"[SteamWorkshopManager] Simulating {(upvote ? "upvote" : "downvote")} for workshop item: {workshopItemId}");
            await Task.Delay(300); // Simulate API call
            return true;
        }

        /// <summary>
        /// Stubs for reporting a workshop item.
        /// </summary>
        public async Task<bool> ReportModAsync(ulong workshopItemId, string reason)
        {
            Console.WriteLine($"[SteamWorkshopManager] Simulating report for workshop item: {workshopItemId} with reason: {reason}");
            await Task.Delay(300); // Simulate API call
            return true;
        }

        private ulong GenerateDummyWorkshopId()
        {
            // In a real scenario, this would be generated by Steamworks API
            return (ulong)new Random().Next(100000000, 999999999);
        }
    }
}
