using Godot;
using LakskongenSim.Core;
using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;

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

    private const string SAVE_DIRECTORY = "user://saves/";
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
    public const int CurrentSaveVersion = 1;

    private JsonSerializerOptions _jsonOptions;
    private GameBridge _gameBridge;

    public override void _Ready()
    {
        SetupJsonOptions();
        EnsureSaveDirectory();
        ConnectToGameBridge();
        
        GD.Print("SaveLoadManager initialized");
    }

    private void SetupJsonOptions()
    {
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            IncludeFields = true
        };
    }

    private void EnsureSaveDirectory()
    {
        using var dir = Godot.DirAccess.Open("user://");
        if (dir != null && !dir.DirExists("saves"))
        {
            dir.MakeDir("saves");
            GD.Print("Created saves directory");
        }
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
        if (_gameBridge?.SimulationEngine?.State == null)
        {
            EmitSignal(SignalName.SaveFailed, "No game in progress");
            return false;
        }

        try
        {
            string fileName = SanitizeFileName(saveName) + ".json";
            string fullPath = Path.Combine(SAVE_DIRECTORY, fileName);
            
            // Create backup if save already exists
            CreateBackup(fullPath);
            
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
            EmitSignal(SignalName.SaveFailed, error);
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
        string fullPath = Path.Combine(SAVE_DIRECTORY, fileName);
        
        try
        {
            using var file = Godot.FileAccess.Open(fullPath, Godot.FileAccess.ModeFlags.Read);
            if (file == null)
            {
                string error = $"Save file not found: {fileName}. Please check that the file exists.";
                EmitSignal(SignalName.LoadFailed, error);
                GD.PrintErr(error);
                return false;
            }
            
            string jsonString = file.GetAsText();
            file.Close();
            
            // Validate JSON is not empty or corrupted
            if (string.IsNullOrWhiteSpace(jsonString))
            {
                string error = $"Save file '{fileName}' is empty or corrupted. Please try a different save file.";
                EmitSignal(SignalName.LoadFailed, error);
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
                EmitSignal(SignalName.LoadFailed, error);
                GD.PrintErr($"{error} Details: {jsonEx.Message}");
                TryLoadBackup(fileName);
                return false;
            }
            
            if (saveData?.GameState == null)
            {
                string error = $"Save file '{fileName}' is missing game data. Please try a different save file.";
                EmitSignal(SignalName.LoadFailed, error);
                GD.PrintErr(error);
                return false;
            }

            // Version gate: refuse newer formats, migrate older ones.
            if (saveData.SaveVersion > CurrentSaveVersion)
            {
                string error = $"Save file '{fileName}' needs a newer game version (save v{saveData.SaveVersion}, game v{CurrentSaveVersion}). Please update the game.";
                EmitSignal(SignalName.LoadFailed, error);
                GD.PrintErr(error);
                return false;
            }
            if (saveData.SaveVersion < CurrentSaveVersion)
            {
                GD.Print($"Migrating save '{fileName}' from v{saveData.SaveVersion} to v{CurrentSaveVersion}.");
                saveData = MigrateSaveData(saveData);
            }
            
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
            EmitSignal(SignalName.LoadFailed, error);
            GD.PrintErr(error);
            return false;
        }
    }
    
    /// <summary>
    /// Attempt to load backup file if main save is corrupted
    /// </summary>
    private void TryLoadBackup(string fileName)
    {
        string backupPath = Path.Combine(SAVE_DIRECTORY, fileName + BACKUP_SUFFIX);
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
        
        using var dir = Godot.DirAccess.Open(SAVE_DIRECTORY);
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
    /// Delete a save file
    /// </summary>
    public bool DeleteSave(string fileName)
    {
        try
        {
            string fullPath = Path.Combine(SAVE_DIRECTORY, fileName);
            
            using var dir = Godot.DirAccess.Open(SAVE_DIRECTORY);
            if (dir != null && dir.FileExists(fileName))
            {
                dir.Remove(fileName);
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
                default:
                    GD.PrintErr($"No migrator for save version {saveData.SaveVersion}; loading as-is.");
                    return saveData;
            }
        }
        return saveData;
    }

    private SaveFileInfo GetSaveFileInfo(string fileName)
    {
        string fullPath = Path.Combine(SAVE_DIRECTORY, fileName);
        
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
                Cash = (long)saveData.GameState.Balance, // Use Balance and cast to long
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
        
        using var dir = Godot.DirAccess.Open(SAVE_DIRECTORY);
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

    private void OnSimulationTicked(int day)
    {
        // Auto save every 30 days
        if (day % 30 == 0)
        {
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
