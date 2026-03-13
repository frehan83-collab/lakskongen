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
    /// </summary>
    public class SaveLoadManager
    {
        private readonly string _saveDirectory;

        /// <summary>Number of backup generations to keep per save slot.</summary>
        public const int MaxBackups = 3;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
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

        private static GameState Deserialize(string json) =>
            JsonSerializer.Deserialize<GameState>(json, JsonOptions)
                ?? throw new InvalidOperationException("Deserialized GameState was null.");
    }
}
