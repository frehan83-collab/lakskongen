using Godot;
using LakskongenSim.Core;
using LakskongenSim.Farm;
using LakskongenSim.Logistics;
using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Manages game save/load functionality with JSON serialization.
/// Handles multiple save slots, autosave, and backup systems.
/// </summary>
public partial class SaveLoadManager : Node
{
    [Signal]
    public delegate void GameSavedEventHandler(string saveName, string filePath);
    
    [Signal]
    public delegate void GameLoadedEventHandler(string saveName);
    
    [Signal]
    public delegate void SaveFailedEventHandler(string error);
    
    [Signal]
    public delegate void LoadFailedEventHandler(string error);

    private const string DefaultSaveDirectory = "user://saves/";
    private const string AUTOSAVE_PREFIX = "autosave";
    private const string QUICKSAVE_PREFIX = "quicksave";
    private const string BACKUP_SUFFIX = ".backup";
    private const int MAX_AUTOSAVES = 5;
    private const int MAX_BACKUPS = 3;

    /// <summary>
    /// Save format version. Bump when GameState shape changes and add a
    /// migration step in MigrateSaveData. Old saves without the field read
    /// as version 0.
    /// </summary>
    public const int CurrentSaveVersion = 10;

    private JsonSerializerOptions _jsonOptions;
    private GameBridge _gameBridge;
    private string _saveDirectory = DefaultSaveDirectory;

    /// <summary>
    /// Reason for the most recent failed save/load (empty after a success).
    /// Lets GDScript show the real error instead of a blind success toast.
    /// </summary>
    public string LastError { get; private set; } = "";

    public override void _Ready()
    {
        SetupJsonOptions();
        EnsureSaveDirectory();
        ConnectToGameBridge();

        GD.Print("SaveLoadManager initialized");
    }

    /// <summary>
    /// QA/test seam: redirect saves to another user:// directory so headless
    /// UI tests never touch the player's real save slots.
    /// </summary>
    public void UseSaveDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return;
        _saveDirectory = directory.EndsWith("/") ? directory : directory + "/";
        EnsureSaveDirectory();
    }

    private void ReportSaveFailure(string error)
    {
        LastError = error;
        EmitSignal(SignalName.SaveFailed, error);
    }

    private void ReportLoadFailure(string error)
    {
        LastError = error;
        EmitSignal(SignalName.LoadFailed, error);
    }

    /// <summary>
    /// Save files as Variant-safe dictionaries for GDScript (newest first):
    /// file (pass to LoadGame/DeleteSave), name, day, cash, time (ISO 8601 UTC).
    /// <see cref="GetSaveFiles"/> returns a CLR list GDScript cannot marshal.
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetSaveList()
    {
        var list = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var save in GetSaveFiles())
        {
            list.Add(new Godot.Collections.Dictionary
            {
                ["file"] = save.FileName,
                ["name"] = save.SaveName,
                ["day"] = save.GameDay,
                ["cash"] = save.Cash,
                ["time"] = save.SaveTime.ToString("o"),
            });
        }
        return list;
    }

    private void SetupJsonOptions()
    {
        // Phase 1 wave 5: the ONE save format (SaveJson.Options). It still
        // READS legacy v1/v2 saves (camelCase via case-insensitive binding,
        // int enums via the string converter), but writes v3 PascalCase +
        // enum names + the runtime cadence snapshot.
        _jsonOptions = LakskongenSim.Core.SaveJson.Options;
    }

    private void EnsureSaveDirectory()
    {
        string absolute = ProjectSettings.GlobalizePath(_saveDirectory);
        if (!Godot.DirAccess.DirExistsAbsolute(absolute))
        {
            Godot.DirAccess.MakeDirRecursiveAbsolute(absolute);
            GD.Print($"Created saves directory {_saveDirectory}");
        }
    }

    /// <summary>
    /// Reduce a UI-supplied save file name to a bare file name inside the save
    /// directory (no "..", no separators), or "" when nothing usable is left.
    /// </summary>
    private static string ToSaveFileName(string fileName)
    {
        string bare = Path.GetFileName(fileName ?? "");
        return bare == "." || bare == ".." ? "" : bare;
    }

    private void ConnectToGameBridge()
    {
        _gameBridge = GetNode<GameBridge>("/root/GameBridge");
        if (_gameBridge != null)
        {
            // Auto-save every 30 game days
            _gameBridge.SimulationTicked += OnSimulationTicked;
        }
    }

    // === Public Save Methods ===

    /// <summary>
    /// Save the current game state with a custom name
    /// </summary>
    public bool SaveGame(string saveName)
    {
        LastError = "";
        if (_gameBridge?.SimulationEngine?.State == null)
        {
            ReportSaveFailure("No game in progress");
            return false;
        }

        try
        {
            string fileName = SanitizeFileName(saveName) + ".json";
            string fullPath = Path.Combine(_saveDirectory, fileName);
            
            // Create backup if save already exists
            CreateBackup(fullPath);
            
            // Sync live FinanceManager balance + empire systems into the
            // serializable GameState mirror BEFORE snapshotting — otherwise
            // saves persist stale cash and empire DTOs.
            _gameBridge.SimulationEngine.PrepareForSave();
            var saveData = CreateSaveData(_gameBridge.SimulationEngine.State, saveName);
            string jsonString = JsonSerializer.Serialize(saveData, _jsonOptions);

            // Atomic write: temp file + rename, so a crash can never leave
            // a half-written save behind.
            string tempPath = fullPath + ".tmp";
            using (var file = Godot.FileAccess.Open(tempPath, Godot.FileAccess.ModeFlags.Write))
            {
                if (file == null)
                {
                    throw new Exception($"Failed to create temp save file: {tempPath}");
                }
                file.StoreString(jsonString);
                file.Close();
            }
            System.IO.File.Move(
                ProjectSettings.GlobalizePath(tempPath),
                ProjectSettings.GlobalizePath(fullPath),
                true
            );
            
            EmitSignal(SignalName.GameSaved, saveName, fullPath);
            EventBus eventBus = GetNode<EventBus>("/root/EventBus");
            eventBus.PostNotification("Game Saved", $"Progress saved as '{saveName}'", "success");
            
            GD.Print($"Game saved: {saveName} -> {fullPath}");
            return true;
        }
        catch (Exception ex)
        {
            string error = $"Failed to save game '{saveName}': {ex.Message}";
            ReportSaveFailure(error);
            GD.PrintErr(error);
            return false;
        }
    }

    /// <summary>
    /// Quick save with automatic naming
    /// </summary>
    public bool QuickSave()
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string saveName = $"{QUICKSAVE_PREFIX}_{timestamp}";
        return SaveGame(saveName);
    }

    /// <summary>
    /// Auto save (called periodically)
    /// </summary>
    public bool AutoSave()
    {
        if (_gameBridge?.SimulationEngine?.State == null) return false;

        try
        {
            // Rotate autosave files
            RotateAutoSaves();
            
            string saveName = $"{AUTOSAVE_PREFIX}_latest";
            // Every autosave, the day cadence's or an explicit one, starts the
            // real minute the next day-cadence autosave waits for (U2-07), so
            // the cadence never overwrites a fresh autosave_latest at once.
            _autoSavedOnce = true;
            _lastAutoSaveMs = Time.GetTicksMsec();
            return SaveGame(saveName);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Auto save failed: {ex.Message}");
            return false;
        }
    }

    // === Public Load Methods ===

    /// <summary>
    /// Load a saved game by file name
    /// </summary>
    public bool LoadGame(string fileName)
    {
        LastError = "";
        fileName = ToSaveFileName(fileName);
        if (fileName.Length == 0)
        {
            ReportLoadFailure("No save file selected.");
            return false;
        }
        string fullPath = Path.Combine(_saveDirectory, fileName);
        
        try
        {
            using var file = Godot.FileAccess.Open(fullPath, Godot.FileAccess.ModeFlags.Read);
            if (file == null)
            {
                string error = $"Save file not found: {fileName}. Please check that the file exists.";
                ReportLoadFailure(error);
                GD.PrintErr(error);
                return false;
            }
            
            string jsonString = file.GetAsText();
            file.Close();
            
            // Validate JSON is not empty or corrupted
            if (string.IsNullOrWhiteSpace(jsonString))
            {
                string error = $"Save file '{fileName}' is empty or corrupted. Please try a different save file.";
                ReportLoadFailure(error);
                GD.PrintErr(error);
                TryLoadBackup(fileName);
                return false;
            }
            
            SaveGameData saveData;
            try
            {
                saveData = JsonSerializer.Deserialize<SaveGameData>(jsonString, _jsonOptions);
            }
            catch (JsonException jsonEx)
            {
                string error = $"Save file '{fileName}' is corrupted (invalid JSON). Please try a different save file.";
                ReportLoadFailure(error);
                GD.PrintErr($"{error} Details: {jsonEx.Message}");
                TryLoadBackup(fileName);
                return false;
            }
            
            if (saveData?.GameState == null)
            {
                string error = $"Save file '{fileName}' is missing game data. Please try a different save file.";
                ReportLoadFailure(error);
                GD.PrintErr(error);
                return false;
            }

            // Version gate: refuse newer formats, migrate older ones.
            if (saveData.SaveVersion > CurrentSaveVersion)
            {
                string error = $"Save file '{fileName}' needs a newer game version (save v{saveData.SaveVersion}, game v{CurrentSaveVersion}). Please update the game.";
                ReportLoadFailure(error);
                GD.PrintErr(error);
                return false;
            }
            if (saveData.SaveVersion < CurrentSaveVersion)
            {
                GD.Print($"Migrating save '{fileName}' from v{saveData.SaveVersion} to v{CurrentSaveVersion}.");
                saveData = MigrateSaveData(saveData);
            }
            
            // Sanitize BEFORE adoption: the engine copies collections and scalars
            // out of the snapshot, so fixes applied afterwards never reach it.
            ValidateAndFixGameState(saveData.GameState);

            // Load the game state into GameBridge
            _gameBridge.LoadGame(saveData.GameState);
            
            EmitSignal(SignalName.GameLoaded, saveData.SaveName);
            EventBus eventBus = GetNode<EventBus>("/root/EventBus");
            eventBus.PostNotification("Game Loaded", $"Loaded '{saveData.SaveName}'", "success");
            
            GD.Print($"Game loaded: {saveData.SaveName} (Day {saveData.GameState.Day})");
            return true;
        }
        catch (Exception ex)
        {
            string error = $"Unexpected error loading '{fileName}': {ex.Message}. Please try a different save file.";
            ReportLoadFailure(error);
            GD.PrintErr(error);
            return false;
        }
    }
    
    /// <summary>
    /// Attempt to load backup file if main save is corrupted
    /// </summary>
    private void TryLoadBackup(string fileName)
    {
        string backupPath = Path.Combine(_saveDirectory, fileName + BACKUP_SUFFIX);
        if (Godot.FileAccess.FileExists(backupPath))
        {
            GD.Print($"Found backup file for '{fileName}'. You may try loading the backup manually.");
            EventBus eventBus = GetNode<EventBus>("/root/EventBus");
            eventBus?.PostNotification("Backup Available", $"A backup save exists for this file", "info");
        }
    }

    /// <summary>
    /// Get list of available save files
    /// </summary>
    public List<SaveFileInfo> GetSaveFiles()
    {
        var saves = new List<SaveFileInfo>();
        
        using var dir = Godot.DirAccess.Open(_saveDirectory);
        if (dir == null) return saves;
        
        dir.ListDirBegin();
        string fileName = dir.GetNext();
        
        while (fileName != "")
        {
            if (fileName.EndsWith(".json") && !fileName.EndsWith(BACKUP_SUFFIX))
            {
                try
                {
                    var saveInfo = GetSaveFileInfo(fileName);
                    if (saveInfo != null)
                    {
                        saves.Add(saveInfo);
                    }
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"Error reading save file {fileName}: {ex.Message}");
                }
            }
            fileName = dir.GetNext();
        }
        
        dir.ListDirEnd();
        
        // Sort by save time (newest first)
        saves.Sort((a, b) => b.SaveTime.CompareTo(a.SaveTime));
        
        return saves;
    }

    /// <summary>
    /// Post-load validation: ensure all Phase 5+ fields have valid defaults.
    /// Runs after deserialization and migration, before game resumes.
    /// </summary>
    private void ValidateAndFixGameState(GameState state)
    {
        // SiteId on populations (added in Phase 5 performance optimization)
        foreach (var pop in state.Populations)
        {
            if (string.IsNullOrEmpty(pop.SiteId))
            {
                // Try to find site from pens
                var site = state.SeaSites.FirstOrDefault(s => s.Pens.Contains(pop));
                if (site != null) pop.SiteId = site.Id;
            }
        }

        // CarbonTracker on SeaSites (Phase 5.4)
        foreach (var site in state.SeaSites)
        {
            site.CarbonTracker ??= new LakskongenSim.Farm.CarbonTracker();
        }

        // CarbonTracker on OffshoreFarms (Phase 5.4; load-only since F2-03)
        state.OffshoreFarms ??= new List<LakskongenSim.Farm.OffshoreFarm>();
        state.PostSmoltFacilities ??= new List<LakskongenSim.Farm.PostSmoltFacility>();
        foreach (var off in state.OffshoreFarms)
        {
            off.CarbonTracker ??= new LakskongenSim.Farm.CarbonTracker();
        }

        // F2-03: an offshore sea site's structure (its own carbon tracker) and
        // the plots' state (attributes rejoin from the catalogue on adoption).
        foreach (var site in state.SeaSites)
        {
            if (site.Kind == LakskongenSim.Farm.SiteKind.Offshore)
                site.Offshore ??= new LakskongenSim.Farm.Offshore.OffshoreUnit();
            if (!double.IsFinite(site.AppliedContainmentCredit) || site.AppliedContainmentCredit <= 0.0)
                site.AppliedContainmentCredit = 1.0;
        }
        state.SitePlots ??= new List<LakskongenSim.Farm.SitePlot>();
        state.SitePlots.RemoveAll(p => p == null || string.IsNullOrEmpty(p.Id));

        // CarbonTracker on RASFacilities (Phase 5.4)
        foreach (var ras in state.RASFacilities)
        {
            ras.CarbonTracker ??= new LakskongenSim.Farm.CarbonTracker();
        }

        // CarbonTracker on ProcessingPlants (Phase 5.4)
        foreach (var proc in state.ProcessingSnapshots)
        {
            // ProcessingSnapshot doesn't have CarbonTracker; it's on ProcessingPlant
        }

        // Sludge fields on SeaSites (Phase 5.3)
        foreach (var site in state.SeaSites)
        {
            if (site.SludgeAccumulatedKgDs < 0) site.SludgeAccumulatedKgDs = 0;
            if (site.SludgeValorizedKgDs < 0) site.SludgeValorizedKgDs = 0;
            if (site.LastSludgeValorizationDay < -1) site.LastSludgeValorizationDay = -1;
        }

        // VaccinatedDay on populations (Phase 5.1)
        foreach (var pop in state.Populations)
        {
            if (pop.VaccinatedDay < -1) pop.VaccinatedDay = -1;
        }

        // SmoltQualityTier on cohorts (additive sea-transfer carryover field).
        // A save written before this field existed reads as Standard (0); a
        // hand-edited or future save may hold an undefined value — clamp to
        // Standard so the quality registry never has to guess.
        foreach (var pop in state.Populations)
        {
            if (pop?.Cohorts == null) continue;
            foreach (var cohort in pop.Cohorts)
            {
                if (cohort == null) continue;
                if (!Enum.IsDefined(typeof(LakskongenSim.Farm.SmoltQualityTier), cohort.Quality))
                {
                    cohort.Quality = LakskongenSim.Farm.SmoltQualityTier.Standard;
                }
            }
        }

        // WatchMode on SeaSites (Phase 4.2; F2-03: an offshore unit is crewed on board)
        foreach (var site in state.SeaSites)
        {
            if (string.IsNullOrEmpty(site.WatchMode))
                site.WatchMode = site.Kind == LakskongenSim.Farm.SiteKind.Offshore
                    ? LakskongenSim.Core.SimulationEngine.OffshoreWatchMode
                    : "watch";
        }

        // GillScore on SeaSites (Phase 4.3)
        foreach (var site in state.SeaSites)
        {
            if (site.GillScore < 0) site.GillScore = 100.0;
            else if (site.GillScore > 100) site.GillScore = 100.0;
        }

        // NightShiftPolicy on GameState (Phase 5.2)
        if (!Enum.IsDefined(typeof(LakskongenSim.Logistics.NightShiftPolicy), state.NightShiftPolicy))
        {
            state.NightShiftPolicy = LakskongenSim.Logistics.NightShiftPolicy.Standard;
        }

        // P11 insurance policies (additive): null-safe list + sanitize premiums /
        // coverages so a hand-edited save can never poison the payout math.
        state.InsurancePolicies ??= new List<InsurancePolicyState>();
        foreach (var policy in state.InsurancePolicies)
        {
            if (policy == null) continue;
            if (double.IsNaN(policy.Coverage) || double.IsInfinity(policy.Coverage))
                policy.Coverage = 0.0;
            if (double.IsNaN(policy.Premium) || double.IsInfinity(policy.Premium) || policy.Premium < 0.0)
                policy.Premium = 0.0;
            if (policy.Deductible < 0m)
                policy.Deductible = 0m;
        }

        // P11 bloom mitigation registry (additive): null-safe list.
        state.BloomMitigations ??= new List<BloomMitigationState>();

        // P9 resource-rent tax (additive): cumulative paid can never be negative.
        if (state.ResourceRentPaid < 0m)
            state.ResourceRentPaid = 0m;
    }

    /// <summary>
    /// Delete a save file
    /// </summary>
    public bool DeleteSave(string fileName)
    {
        try
        {
            fileName = ToSaveFileName(fileName);
            if (fileName.Length == 0) return false;
            
            using var dir = Godot.DirAccess.Open(_saveDirectory);
            if (dir != null && dir.FileExists(fileName))
            {
                dir.Remove(fileName);
                if (dir.FileExists(fileName + BACKUP_SUFFIX))
                    dir.Remove(fileName + BACKUP_SUFFIX);
                GD.Print($"Deleted save file: {fileName}");
                return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Failed to delete save file {fileName}: {ex.Message}");
            return false;
        }
    }

    // === Private Helper Methods ===

    private SaveGameData CreateSaveData(GameState gameState, string saveName)
    {
        return new SaveGameData
        {
            SaveName = saveName,
            SaveTime = DateTime.UtcNow,
            GameVersion = ProjectSettings.GetSetting("application/config/version", "1.0.0").AsString(),
            SaveVersion = CurrentSaveVersion,
            GameState = gameState
        };
    }

    /// <summary>
    /// Migrate an older save forward step by step. Add a case per version
    /// bump; each step must handle saves produced by the previous version.
    /// </summary>
    private SaveGameData MigrateSaveData(SaveGameData saveData)
    {
        // v0 (pre-version saves): no format changes yet, accept as-is.
        while (saveData.SaveVersion < CurrentSaveVersion)
        {
            switch (saveData.SaveVersion)
            {
                case 0:
                    // v0 -> v1: version stamp introduced, no data changes.
                    saveData.SaveVersion = 1;
                    break;
                case 1:
                    // v1 -> v2: Phase 5 fields (SiteId, CarbonTracker, Sludge, etc.)
                    // These are initialized with defaults in class constructors.
                    // Just bump version - deserializer will use defaults for missing fields.
                    saveData.SaveVersion = 2;
                    break;
                case 2:
                    // v2 -> v3: unified serializer (SaveJson.Options) + the
                    // EngineRuntimeSnapshot (interval clocks, storm edge).
                    // Payload shape is unchanged — defaults apply for the
                    // new members and the unified options read both formats.
                    saveData.SaveVersion = 3;
                    break;
                case 3:
                    // v3 -> v4 (S2-02): zones carry their production area and
                    // the game its home area; the weather carries the shared
                    // sea-temperature anomaly. A v3 save loads with 0s: the
                    // engine resolves each zone's area (from an id like "po3_nord",
                    // else the home area from the first site) and the anomaly
                    // restarts at zero.
                    saveData.SaveVersion = 4;
                    break;
                case 4:
                    // v4 -> v5 (S2-03): cohorts carry their weight CV and
                    // populations their ration policy. A v4 save loads with
                    // full-appetite rations and CV 0 ("not yet known"), which
                    // the first growth day sets from each cohort's weight
                    // (TgcGrowthLaw.InitialWeightCv).
                    saveData.SaveVersion = 5;
                    break;
                case 5:
                    // v5 -> v6 (S2-04): harvest orders, harvest reports, the year's harvest
                    // cost and a population's fasting and cold-water memory. A v5 save
                    // loads with no open orders, no reports, nobody fasting and no cold
                    // memory (the first growth days build it).
                    saveData.SaveVersion = 6;
                    break;
                case 6:
                    // v6 -> v7 (S2-01a; numbered v5 on wave2/sim-a-2, renumbered
                    // by the wave-2 sim integration after S2-03's v5 and S2-04's
                    // v6): the licence register (CapacityRules, LicenceHoldings,
                    // LicencesGranted, CapacityBreaches). A v6 save loads with no
                    // rules (null): the engine gives each sea site one licence of
                    // its old MAB in its production area and opens capacity v1
                    // (SimulationEngine.MigrateCapacityRegisterAfterLoad).
                    saveData.SaveVersion = 7;
                    break;
                case 7:
                    // v7 -> v8 (S2-01b): sea sites carry their pens (PhysicalPens:
                    // number, net, volume, the population each holds) and harvest
                    // orders the pen of each population (PenId). A v7 save loads
                    // with no pens: the engine gives each site its locality's layout
                    // with one pen per population, a population too large for a pen
                    // keeping the volume it was simulated in
                    // (SimulationEngine.MigratePensAfterLoad); order pens keep "".
                    saveData.SaveVersion = 8;
                    break;
                case 8:
                    // v8 -> v9 (F2-03): site kinds and plots. Sea sites carry their
                    // kind (Fjord/Offshore), plot, offshore structure and stored
                    // red-zone credit; land sites their kind (GrowOut/PostSmolt) and
                    // pad; the game its plots' state (SitePlots). A v8 save loads
                    // with fjord sites, no plots and its facilities in the pre-F2
                    // lists: the engine moves each offshore farm to an offshore sea
                    // site, each post-smolt facility to a post-smolt land site (its
                    // batches populations) and each RAS facility onto a pad, on
                    // overflow plots past the named ones, and empties the lists
                    // (SimulationEngine.MigrateFacilities, idempotent).
                    saveData.SaveVersion = 9;
                    break;
                case 9:
                    // v9 -> v10 (U2-04): a sea site may carry a feeding plan
                    // (SeaSite.FeedPlan: ration, recipe, window). A v9 save
                    // loads with no plans: every site feeds the standard feed to
                    // full appetite, as it did, and its pens keep the rations
                    // they were saved with.
                    saveData.SaveVersion = 10;
                    break;
                default:
                    GD.PrintErr($"No migrator for save version {saveData.SaveVersion}; loading as-is.");
                    return saveData;
            }
        }
        return saveData;
    }

    private SaveFileInfo GetSaveFileInfo(string fileName)
    {
        string fullPath = Path.Combine(_saveDirectory, fileName);
        
        try
        {
            using var file = Godot.FileAccess.Open(fullPath, Godot.FileAccess.ModeFlags.Read);
            if (file == null) return null;
            
            string jsonString = file.GetAsText();
            long fileSize = (long)file.GetLength(); // Cast to long
            file.Close();
            
            // Validate JSON is not empty
            if (string.IsNullOrWhiteSpace(jsonString))
            {
                GD.PrintErr($"Save file '{fileName}' is empty - skipping");
                return null;
            }
            
            SaveGameData saveData;
            try
            {
                saveData = JsonSerializer.Deserialize<SaveGameData>(jsonString, _jsonOptions);
            }
            catch (JsonException jsonEx)
            {
                GD.PrintErr($"Save file '{fileName}' has corrupted JSON - skipping. Details: {jsonEx.Message}");
                return null;
            }
            
            if (saveData?.GameState == null)
            {
                GD.PrintErr($"Save file '{fileName}' is missing game data - skipping");
                return null;
            }
            
            return new SaveFileInfo
            {
                FileName = fileName,
                SaveName = saveData.SaveName ?? "Unknown",
                SaveTime = saveData.SaveTime,
                GameDay = saveData.GameState.Day,
#pragma warning disable CS0618 // GameState.Balance is the intentional save-compat mirror
                Cash = (long)saveData.GameState.Balance, // Use Balance and cast to long
#pragma warning restore CS0618
                FileSize = (int)fileSize
            };
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Error reading save file info for '{fileName}': {ex.Message}");
            return null;
        }
    }

    private void CreateBackup(string originalPath)
    {
        if (!Godot.FileAccess.FileExists(originalPath)) return;
        
        try
        {
            string backupPath = originalPath + BACKUP_SUFFIX;
            
            using var original = Godot.FileAccess.Open(originalPath, Godot.FileAccess.ModeFlags.Read);
            using var backup = Godot.FileAccess.Open(backupPath, Godot.FileAccess.ModeFlags.Write);
            
            if (original != null && backup != null)
            {
                backup.StoreString(original.GetAsText());
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Failed to create backup: {ex.Message}");
        }
    }

    private void RotateAutoSaves()
    {
        var autoSaves = new List<string>();
        
        using var dir = Godot.DirAccess.Open(_saveDirectory);
        if (dir == null) return;
        
        dir.ListDirBegin();
        string fileName = dir.GetNext();
        
        while (fileName != "")
        {
            if (fileName.StartsWith(AUTOSAVE_PREFIX) && fileName.EndsWith(".json"))
            {
                autoSaves.Add(fileName);
            }
            fileName = dir.GetNext();
        }
        
        dir.ListDirEnd();
        
        // Sort and remove oldest if too many
        autoSaves.Sort();
        while (autoSaves.Count >= MAX_AUTOSAVES)
        {
            string oldestSave = autoSaves[0];
            dir.Remove(oldestSave);
            autoSaves.RemoveAt(0);
            GD.Print($"Deleted old autosave: {oldestSave}");
        }
    }

    private string SanitizeFileName(string fileName)
    {
        // Remove or replace invalid characters
        char[] invalidChars = Path.GetInvalidFileNameChars();
        string sanitized = fileName;
        
        foreach (char c in invalidChars)
        {
            sanitized = sanitized.Replace(c, '_');
        }
        
        return sanitized.Trim();
    }

    /// <summary>Real time between two day-cadence autosaves (U2-07).</summary>
    private const ulong MinAutoSaveIntervalMs = 60_000;
    private ulong _lastAutoSaveMs;
    private bool _autoSavedOnce;

    private void OnSimulationTicked(int day)
    {
        // Auto save every 30 days, but not more than once a real minute: at
        // 30× a 30-day cadence wrote the save (a 40-50 ms frame) every second
        // (U2-07). The settings' real-time interval is U2-08's.
        if (day % 30 == 0)
        {
            ulong now = Time.GetTicksMsec();
            if (_autoSavedOnce && now - _lastAutoSaveMs < MinAutoSaveIntervalMs)
                return;
            AutoSave();
        }
    }
}

// === Data Classes ===

public class SaveGameData
{
    public string SaveName { get; set; }
    public DateTime SaveTime { get; set; }
    public string GameVersion { get; set; }
    /// <summary>Save format version. See CurrentSaveVersion + MigrateSaveData.</summary>
    public int SaveVersion { get; set; }
    public GameState GameState { get; set; }
}

public class SaveFileInfo
{
    public string FileName { get; set; }
    public string SaveName { get; set; }
    public DateTime SaveTime { get; set; }
    public int GameDay { get; set; }
    public long Cash { get; set; }
    public int FileSize { get; set; }
}
