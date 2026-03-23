using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using LakskongenSim.Events;

#pragma warning disable CS0618 // Type or member is obsolete (GameState.Balance used in mod system)
namespace LakskongenSim.Core
{
    // ── Mod manifest ──────────────────────────────────────────────────────────

    /// <summary>
    /// Declares the metadata for a single mod package.
    /// Stored as manifest.json at the root of each mod folder.
    /// </summary>
    public class ModManifest
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("author")]
        public string Author { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0.0";

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Relative paths (from the mod folder) of JSON data files to load.
        /// Supported types: events, research_nodes, fish_species, scenarios.
        /// </summary>
        [JsonPropertyName("files")]
        public List<string> Files { get; set; } = new();

        /// <summary>Other mod IDs that must be loaded before this one.</summary>
        [JsonPropertyName("dependencies")]
        public List<string> Dependencies { get; set; } = new();

        /// <summary>Set at runtime; not part of the JSON file.</summary>
        public bool Enabled { get; set; } = true;

        [JsonIgnore]
        public string FolderPath { get; set; } = string.Empty;
    }

    // ── Mod data payloads ─────────────────────────────────────────────────────

    /// <summary>JSON-serializable event definition for mods (no delegate fields).</summary>
    public class ModEventDefinition
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("category")]
        public string Category { get; set; } = "General";

        [JsonPropertyName("weight")]
        public double Weight { get; set; } = 1.0;

        [JsonPropertyName("cooldown_days")]
        public int CooldownDays { get; set; } = 30;

        [JsonPropertyName("min_day")]
        public int MinDay { get; set; } = 0;

        [JsonPropertyName("max_day")]
        public int MaxDay { get; set; } = int.MaxValue;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Simple numeric effects applied when the event fires.
        /// Keys: "balance_change", "lice_change", "sustainability_change", "mortality_change".
        /// </summary>
        [JsonPropertyName("effects")]
        public Dictionary<string, double> Effects { get; set; } = new();

        /// <summary>Optional condition: minimum balance required (NOK) for the event to trigger.</summary>
        [JsonPropertyName("condition_min_balance")]
        public double? ConditionMinBalance { get; set; }

        /// <summary>Optional condition: minimum day index required.</summary>
        [JsonPropertyName("condition_min_day")]
        public int? ConditionMinDay { get; set; }
    }

    /// <summary>JSON-serializable research node for mods.</summary>
    public class ModResearchNode
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("branch")]
        public string Branch { get; set; } = "Biology";

        [JsonPropertyName("tier")]
        public int Tier { get; set; } = 1;

        [JsonPropertyName("cost_nok")]
        public decimal CostNOK { get; set; }

        [JsonPropertyName("research_days")]
        public int ResearchDays { get; set; } = 30;

        [JsonPropertyName("prerequisites")]
        public List<string> Prerequisites { get; set; } = new();

        [JsonPropertyName("effects")]
        public Dictionary<string, double> Effects { get; set; } = new();
    }

    /// <summary>JSON-serializable fish species data for mods.</summary>
    public class ModFishSpecies
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("scientific_name")]
        public string ScientificName { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        /// <summary>Base growth rate multiplier relative to Atlantic salmon (1.0).</summary>
        [JsonPropertyName("growth_rate_multiplier")]
        public double GrowthRateMultiplier { get; set; } = 1.0;

        /// <summary>Optimal temperature range in °C.</summary>
        [JsonPropertyName("optimal_temp_min")]
        public double OptimalTempMin { get; set; } = 8.0;

        [JsonPropertyName("optimal_temp_max")]
        public double OptimalTempMax { get; set; } = 14.0;

        /// <summary>Base FCR (Feed Conversion Ratio).</summary>
        [JsonPropertyName("base_fcr")]
        public double BaseFCR { get; set; } = 1.15;

        /// <summary>Market price multiplier relative to Atlantic salmon spot price.</summary>
        [JsonPropertyName("price_multiplier")]
        public double PriceMultiplier { get; set; } = 1.0;

        [JsonPropertyName("max_harvest_weight_kg")]
        public double MaxHarvestWeightKg { get; set; } = 5.0;
    }

    /// <summary>JSON-serializable custom scenario for mods.</summary>
    public class ModScenario
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("starting_capital_nok")]
        public decimal StartingCapitalNOK { get; set; } = 5_000_000m;

        [JsonPropertyName("starting_region")]
        public string StartingRegion { get; set; } = "Vestland";

        [JsonPropertyName("difficulty")]
        public int Difficulty { get; set; } = 2;

        [JsonPropertyName("win_condition")]
        public string WinCondition { get; set; } = string.Empty;

        /// <summary>Initial balance in NOK (may differ from StartingCapitalNOK for debt scenarios).</summary>
        [JsonPropertyName("initial_balance_nok")]
        public decimal InitialBalanceNOK { get; set; } = 5_000_000m;

        [JsonPropertyName("starting_fish_count")]
        public int StartingFishCount { get; set; } = 50_000;

        [JsonPropertyName("initial_events")]
        public List<string> InitialEventIds { get; set; } = new();
    }

    public class ModConfigEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;
    }

    public class ModConfiguration
    {
        [JsonPropertyName("mods")]
        public List<ModConfigEntry> Mods { get; set; } = new();

        public static ModConfiguration Load(string configPath, JsonSerializerOptions options)
        {
            if (!File.Exists(configPath))
            {
                return new ModConfiguration();
            }
            try
            {
                var json = File.ReadAllText(configPath);
                return JsonSerializer.Deserialize<ModConfiguration>(json, options) ?? new ModConfiguration();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ModLoader] Failed to load mod config from {configPath}: {ex.Message}");
                return new ModConfiguration();
            }
        }

        public void Save(string configPath, JsonSerializerOptions options)
        {
            try
            {
                var json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(configPath, json);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ModLoader] Failed to save mod config to {configPath}: {ex.Message}");
            }
        }
    }

    // ── Loaded mod container ──────────────────────────────────────────────────

    /// <summary>All data parsed from a single mod package.</summary>
    public class LoadedMod
    {
        public ModManifest Manifest { get; set; } = new();
        public List<ModEventDefinition> Events { get; set; } = new();
        public List<ModResearchNode> ResearchNodes { get; set; } = new();
        public List<ModFishSpecies> FishSpecies { get; set; } = new();
        public List<ModScenario> Scenarios { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public bool LoadedSuccessfully => Errors.Count == 0;
    }

    // ── ModLoader ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Discovers and loads mod packages from a <c>mods/</c> directory.
    ///
    /// Each mod is a subfolder containing a <c>manifest.json</c> plus any number
    /// of JSON data files referenced from the manifest's <c>files</c> list.
    ///
    /// Supported file naming conventions (matched by first path segment after the
    /// last slash, case-insensitive):
    ///   events_*.json        → ModEventDefinition[]
    ///   research_*.json      → ModResearchNode[]
    ///   fish_*.json          → ModFishSpecies[]
    ///   scenarios_*.json     → ModScenario[]
    /// </summary>
    public class ModLoader
    {
        private readonly string _modsDirectory;
        private readonly JsonSerializerOptions _jsonOptions;

        private readonly List<LoadedMod> _loadedMods = new();
        private readonly List<string> _loadErrors = new();

        private ModConfiguration _modConfiguration = new();
        private string _modConfigPath => Path.Combine(_modsDirectory, "mods_config.json");

        public IReadOnlyList<LoadedMod> LoadedMods => _loadedMods;
        public IReadOnlyList<string> LoadErrors => _loadErrors;

        public int EnabledModCount => _loadedMods.Count(m => m.Manifest.Enabled);

        public ModLoader(string modsDirectory)
        {
            _modsDirectory = modsDirectory;
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };
            _modConfiguration = ModConfiguration.Load(_modConfigPath, _jsonOptions);
        }

        // ── Discovery & loading ───────────────────────────────────────────────

        /// <summary>
        /// Scan the mods directory, load all valid mod packages in dependency order,
        /// and return the collection of successfully loaded mods.
        /// </summary>
        public IReadOnlyList<LoadedMod> DiscoverAndLoad()
        {
            _loadedMods.Clear();
            _loadErrors.Clear();

            if (!Directory.Exists(_modsDirectory))
            {
                Console.WriteLine($"[ModLoader] Mods directory not found: {_modsDirectory}");
                return _loadedMods;
            }

            var manifests = DiscoverManifests();
            var ordered   = SortByDependencies(manifests);

            foreach (var manifest in ordered)
            {
                if (!manifest.Enabled) continue;
                var mod = LoadMod(manifest);
                _loadedMods.Add(mod);

                if (mod.LoadedSuccessfully)
                    Console.WriteLine($"[ModLoader] ✅ Loaded mod: {manifest.Name} v{manifest.Version} by {manifest.Author}");
                else
                    Console.WriteLine($"[ModLoader] ⚠️ Mod {manifest.Name} loaded with {mod.Errors.Count} error(s)");
            }

            return _loadedMods;
        }

        /// <summary>
        /// Enable or disable a mod by ID.  Changes take effect on the next <see cref="DiscoverAndLoad"/> call.
        /// </summary>
        public void SetModEnabled(string modId, bool enabled)
        {
            var mod = _loadedMods.FirstOrDefault(m => m.Manifest.Id == modId);
            if (mod != null)
            {
                mod.Manifest.Enabled = enabled;

                var configEntry = _modConfiguration.Mods.FirstOrDefault(e => e.Id == modId);
                if (configEntry != null)
                {
                    configEntry.Enabled = enabled;
                }
                else
                {
                    _modConfiguration.Mods.Add(new ModConfigEntry { Id = modId, Enabled = enabled });
                }
                _modConfiguration.Save(_modConfigPath, _jsonOptions);
            }
        }

        /// <summary>
        /// Collect all ModEventDefinitions from every enabled loaded mod,
        /// converting them into runtime EventDefinition objects with simple numeric effects.
        /// </summary>
        public List<EventDefinition> BuildEventDefinitions()
        {
            var result = new List<EventDefinition>();

            foreach (var mod in _loadedMods.Where(m => m.Manifest.Enabled))
            {
                foreach (var def in mod.Events)
                {
                    var captured = def; // capture for closure
                    result.Add(new EventDefinition
                    {
                        Id          = captured.Id,
                        Name        = captured.Name,
                        Category    = ParseCategory(captured.Category),
                        Weight      = captured.Weight,
                        CooldownDays = captured.CooldownDays,
                        MinDay      = captured.MinDay,
                        MaxDay      = captured.MaxDay,
                        Condition   = state =>
                        {
                            if (captured.ConditionMinBalance.HasValue &&
                                (double)state.Balance < captured.ConditionMinBalance.Value)
                                return false;
                            if (captured.ConditionMinDay.HasValue &&
                                state.Day < captured.ConditionMinDay.Value)
                                return false;
                            return true;
                        },
                        Apply = (state, record) =>
                        {
                            record.Name        = captured.Name;
                            record.Description = captured.Description;
                            record.Category    = ParseCategory(captured.Category);

                            if (captured.Effects.TryGetValue("balance_change", out var balDelta))
                            {
                                // TODO: Route through FinanceManager when engine ref is available in closure
                                state.Balance += (decimal)balDelta;
                                record.BalanceChange = (decimal)balDelta;
                            }
                        }
                    });
                }
            }

            return result;
        }

        /// <summary>Returns all ModResearchNode entries from enabled mods.</summary>
        public List<ModResearchNode> GetAllResearchNodes() =>
            _loadedMods.Where(m => m.Manifest.Enabled)
                        .SelectMany(m => m.ResearchNodes)
                        .ToList();

        /// <summary>Returns all ModFishSpecies entries from enabled mods.</summary>
        public List<ModFishSpecies> GetAllFishSpecies() =>
            _loadedMods.Where(m => m.Manifest.Enabled)
                        .SelectMany(m => m.FishSpecies)
                        .ToList();

        /// <summary>Returns all ModScenario entries from enabled mods.</summary>
        public List<ModScenario> GetAllScenarios() =>
            _loadedMods.Where(m => m.Manifest.Enabled)
                        .SelectMany(m => m.Scenarios)
                        .ToList();

        // ── Private helpers ───────────────────────────────────────────────────

        private List<ModManifest> DiscoverManifests()
        {
            var manifests = new List<ModManifest>();

            Console.WriteLine($"[ModLoader.DiscoverManifests] Scanning directory: {_modsDirectory}");
            foreach (var dir in Directory.EnumerateDirectories(_modsDirectory))
            {
                Console.WriteLine($"[ModLoader.DiscoverManifests] Found subdirectory: {dir}");
                var manifestPath = Path.Combine(dir, "manifest.json");
                if (!File.Exists(manifestPath))
                {
                    Console.WriteLine($"[ModLoader] Skipping {dir} — no manifest.json");
                    continue;
                }

                try
                {
                    var json     = File.ReadAllText(manifestPath);
                    var manifest = JsonSerializer.Deserialize<ModManifest>(json, _jsonOptions);
                    if (manifest == null || string.IsNullOrWhiteSpace(manifest.Id))
                    {
                        _loadErrors.Add($"Invalid manifest in {dir}: missing 'id'");
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(manifest.Name))
                    {
                        _loadErrors.Add($"Invalid manifest in {dir} (ID: {manifest.Id}): missing 'name'");
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(manifest.Author))
                    {
                        _loadErrors.Add($"Invalid manifest in {dir} (ID: {manifest.Id}): missing 'author'");
                        continue;
                    }

                    if (manifests.Any(m => m.Id == manifest.Id))
                    {
                        _loadErrors.Add($"Duplicate mod ID found: '{manifest.Id}' in {dir}");
                        continue;
                    }
                    manifest.FolderPath = dir;
                    manifests.Add(manifest);

                    // Apply persisted enabled state
                    var configEntry = _modConfiguration.Mods.FirstOrDefault(e => e.Id == manifest.Id);
                    if (configEntry != null)
                    {
                        manifest.Enabled = configEntry.Enabled;
                    }
                }
                catch (Exception ex)
                {
                    _loadErrors.Add($"Failed to parse manifest in {dir}: {ex.Message}");
                }
            }

            return manifests;
        }

        private List<ModManifest> SortByDependencies(List<ModManifest> manifests)
        {
            var byId   = manifests.ToDictionary(m => m.Id);
            var sorted = new List<ModManifest>();
            var visited = new HashSet<string>();
            var inStack  = new HashSet<string>();

            void Visit(ModManifest m)
            {
                if (visited.Contains(m.Id)) return;
                if (inStack.Contains(m.Id))
                {
                    _loadErrors.Add($"Circular dependency detected involving mod '{m.Id}'");
                    return;
                }

                inStack.Add(m.Id);
                foreach (var dep in m.Dependencies)
                {
                    if (byId.TryGetValue(dep, out var depMod))
                        Visit(depMod);
                    else
                        _loadErrors.Add($"Mod '{m.Id}' depends on missing mod '{dep}'");
                }
                inStack.Remove(m.Id);
                visited.Add(m.Id);
                sorted.Add(m);
            }

            foreach (var m in manifests)
                Visit(m);

            return sorted;
        }

        private LoadedMod LoadMod(ModManifest manifest)
        {
            var mod = new LoadedMod { Manifest = manifest };

            foreach (var relativePath in manifest.Files)
            {
                var fullPath = Path.Combine(manifest.FolderPath, relativePath);
                if (!File.Exists(fullPath))
                {
                    mod.Errors.Add($"File not found: {relativePath}");
                    continue;
                }

                try
                {
                    var json     = File.ReadAllText(fullPath);
                    var fileName = Path.GetFileName(fullPath).ToLowerInvariant();

                    if (fileName.StartsWith("events"))
                    {
                        var items = JsonSerializer.Deserialize<List<ModEventDefinition>>(json, _jsonOptions);
                        if (items != null) mod.Events.AddRange(items);
                    }
                    else if (fileName.StartsWith("research"))
                    {
                        var items = JsonSerializer.Deserialize<List<ModResearchNode>>(json, _jsonOptions);
                        if (items != null) mod.ResearchNodes.AddRange(items);
                    }
                    else if (fileName.StartsWith("fish"))
                    {
                        var items = JsonSerializer.Deserialize<List<ModFishSpecies>>(json, _jsonOptions);
                        if (items != null) mod.FishSpecies.AddRange(items);
                    }
                    else if (fileName.StartsWith("scenario"))
                    {
                        var items = JsonSerializer.Deserialize<List<ModScenario>>(json, _jsonOptions);
                        if (items != null) mod.Scenarios.AddRange(items);
                    }
                    else
                    {
                        mod.Errors.Add($"Unknown file type (unrecognised prefix): {relativePath}");
                    }
                }
                catch (Exception ex)
                {
                    mod.Errors.Add($"Parse error in {relativePath}: {ex.Message}");
                }
            }

            return mod;
        }

        private static EventCategory ParseCategory(string raw) =>
            Enum.TryParse<EventCategory>(raw, ignoreCase: true, out var cat) ? cat : EventCategory.Other;
    }
}
#pragma warning restore CS0618
