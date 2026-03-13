using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LakskongenSim.Core
{
    /// <summary>
    /// Manages JSON serialization/deserialization of GameState with backup file rotation.
    /// Pure C# — no Godot dependency. Keeps the last 3 backup copies (.bak1/.bak2/.bak3)
    /// and shifts them on each save so .bak1 is always the most recent backup.
    /// 
    /// Handles backward compatibility: missing properties use default values from GameState constructor.
    /// All new GameState properties are automatically serialized via System.Text.Json.
    /// </summary>
    public class SaveLoadManager
    {
        private readonly string _saveDirectory;

        /// <summary>Number of backup generations to keep per save slot.</summary>
        public const int MaxBackups = 3;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
            DefaultIgnoreCondition = JsonIgnoreCondition.Never, // Serialize all properties
            IgnoreReadOnlyProperties = false,
            IncludeFields = false
        };

        public SaveLoadManager(string saveDirectory)
        {
            _saveDirectory = saveDirectory;
            Directory.CreateDirectory(_saveDirectory);
        }

        // ─── Save ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Serialize <paramref name="state"/> to &lt;saveName&gt;.json.
        /// The existing file is rotated into backup slots before being overwritten.
        /// All public properties in GameState are automatically serialized, including:
        /// - Research (UnlockedResearchIds, ActiveResearchId, ResearchProgress)
        /// - Achievements (ExportMarkets, AcquisitionCount, UnlockedAchievementIds, etc.)
        /// - Sustainability (SustainabilityScore, SustainabilityCertification, FeedSustainabilityRating)
        /// - Scenarios (ActiveScenarioId, ScenarioIsWon, ScenarioIsLost, ScenarioLossReason, ScenarioStartDay)
        /// - Tutorial (TutorialCurrentStepIndex, CompletedTutorialStepIds)
        /// </summary>
        public void SaveGame(GameState state, string saveName = "save")
        {
            string filePath = SlotPath(saveName);
            RotateBackups(filePath);
            string json = JsonSerializer.Serialize(state, JsonOptions);
            File.WriteAllText(filePath, json);
        }

        // ─── Load ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Deserialize a <see cref="GameState"/> from &lt;saveName&gt;.json.
        /// Throws <see cref="FileNotFoundException"/> if the file does not exist.
        /// Backward compatible: missing properties use default values from GameState.
        /// </summary>
        public GameState LoadGame(string saveName = "save")
        {
            string filePath = SlotPath(saveName);
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Save file not found: {filePath}");

            string json = File.ReadAllText(filePath);
            return Deserialize(json);
        }

        /// <summary>
        /// Try to load the primary save; on failure fall back through backup slots.
        /// Returns <c>null</c> if no readable file is found.
        /// </summary>
        public GameState? TryLoadWithFallback(string saveName = "save")
        {
            string filePath = SlotPath(saveName);

            if (File.Exists(filePath))
            {
                try { return Deserialize(File.ReadAllText(filePath)); }
                catch { /* fall through to backups */ }
            }

            for (int i = 1; i <= MaxBackups; i++)
            {
                string backupPath = filePath + $".bak{i}";
                if (!File.Exists(backupPath)) continue;
                try { return Deserialize(File.ReadAllText(backupPath)); }
                catch { /* try next backup */ }
            }

            return null;
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private string SlotPath(string saveName) =>
            Path.Combine(_saveDirectory, saveName + ".json");

        /// <summary>
        /// Rotate existing backups: .bak2→.bak3, .bak1→.bak2, primary→.bak1.
        /// The oldest backup (.bak3) is deleted to make room.
        /// </summary>
        private static void RotateBackups(string filePath)
        {
            if (!File.Exists(filePath)) return;

            for (int i = MaxBackups; i > 1; i--)
            {
                string older = filePath + $".bak{i}";
                string newer = filePath + $".bak{i - 1}";
                if (File.Exists(older)) File.Delete(older);
                if (File.Exists(newer)) File.Move(newer, older);
            }

            File.Copy(filePath, filePath + ".bak1", overwrite: true);
        }

        /// <summary>
        /// Deserialize JSON to GameState with backward compatibility handling.
        /// Missing properties will use default values from the GameState constructor.
        /// System.Text.Json handles:
        /// - Missing properties → default/initialized values
        /// - Null collections → empty collections (via property initializers)
        /// - New enum values → default enum value
        /// - Nullable properties → null if missing
        /// </summary>
        private static GameState Deserialize(string json)
        {
            var state = JsonSerializer.Deserialize<GameState>(json, JsonOptions)
                ?? throw new InvalidOperationException("Deserialized GameState was null.");

            // Ensure collections are never null (backward compatibility for older saves)
            state.Zones ??= new();
            state.SeaSites ??= new();
            state.Populations ??= new();
            state.ProcessingSnapshots ??= new();
            state.EventLog ??= new();
            state.DailySnapshots ??= new();
            state.UnlockedResearchIds ??= new();
            state.ExportMarkets ??= new();
            state.UnlockedAchievementIds ??= new();
            state.CompletedTutorialStepIds ??= new();

            // Ensure new scalar properties have sensible defaults if missing
            // (System.Text.Json uses 0/false/null for missing primitives, but we can override)
            if (state.BestFCR == 0.0)
                state.BestFCR = double.MaxValue; // Uninitialized = no record yet

            if (state.MinimumBalanceEver == 0.0m)
                state.MinimumBalanceEver = decimal.MaxValue; // Uninitialized = no low yet

            if (state.FeedSustainabilityRating == 0.0)
                state.FeedSustainabilityRating = 0.7; // Default 70% sustainable

            // ScenarioLossReason should be empty string, not null
            state.ScenarioLossReason ??= string.Empty;

            return state;
        }
    }
}
