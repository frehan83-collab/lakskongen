using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

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

        // Phase 1 wave 5: the one-serializer unification — the library writes and
        // reads through SaveJson.Options, the exact same format the game uses.
        private static readonly JsonSerializerOptions JsonOptions = SaveJson.Options;

        // Concurrent saves to one slot: a lock per slot file (full path) shared
        // by every manager in the process, and a counter for temp-file names.
        private static readonly ConcurrentDictionary<string, object> SlotLocks =
            new(StringComparer.OrdinalIgnoreCase);
        private static long _tempFileCounter;

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
        /// - Analytics (DailySnapshots, ProcessingSnapshots)
        /// - Events (EventLog)
        /// - Research (UnlockedResearchIds, ActiveResearchId, ResearchProgress)
        /// - Markets (ExportMarkets, ContractActive)
        /// - Expansion (AcquisitionCount)
        /// - Achievements (UnlockedAchievementIds)
        /// - Sustainability (SustainabilityScore, SustainabilityCertification, FeedSustainabilityRating)
        /// - Scenarios (ActiveScenarioId, ScenarioIsWon, ScenarioIsLost, ScenarioLossReason, ScenarioStartDay)
        /// - Tutorial (TutorialCurrentStepIndex, CompletedTutorialStepIds)
        ///
        /// Safe for concurrent calls on every OS. Saves to one slot run one at a
        /// time in this process (any number of managers on the directory share
        /// the lock), and the new save is written to a temp file next to the
        /// slot that then replaces the slot file atomically. A concurrent
        /// <see cref="LoadGame"/> therefore reads the previous save or the new
        /// one, never a partial write; a save that fails leaves the slot file
        /// as it was and deletes its temp file.
        /// </summary>
        public void SaveGame(GameState state, string saveName = "save")
        {
            string filePath = SlotPath(saveName);
            // Serialize outside the lock: it only reads the caller's state.
            byte[] json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(state, JsonOptions));

            lock (SlotLock(filePath))
            {
                string tempPath = WriteTempFile(filePath, json);
                try
                {
                    CommitSave(tempPath, filePath);
                }
                catch
                {
                    TryDeleteTemp(tempPath);
                    throw;
                }
            }
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

            return Deserialize(ReadSaveText(filePath));
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
                try { return Deserialize(ReadSaveText(filePath)); }
                catch { /* fall through to backups */ }
            }

            for (int i = 1; i <= MaxBackups; i++)
            {
                string backupPath = BackupPath(filePath, i);
                if (!File.Exists(backupPath)) continue;
                try { return Deserialize(ReadSaveText(backupPath)); }
                catch { /* try next backup */ }
            }

            return null;
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private string SlotPath(string saveName) =>
            Path.GetFullPath(Path.Combine(_saveDirectory, saveName + ".json"));

        private static string BackupPath(string filePath, int generation) =>
            filePath + $".bak{generation}";

        /// <summary>
        /// One lock per slot file for the whole process. Keys ignore case
        /// because macOS and Windows file names do: "Slot" and "slot" are one file.
        /// </summary>
        private static object SlotLock(string filePath) =>
            SlotLocks.GetOrAdd(filePath, _ => new object());

        /// <summary>
        /// Write <paramref name="content"/> to a new temp file beside the slot
        /// and flush it to disk. Returns the temp path; on failure the temp file
        /// is deleted and the exception propagates.
        /// </summary>
        private static string WriteTempFile(string filePath, byte[] content)
        {
            FileStream stream = CreateTempFile(filePath, out string tempPath);
            try
            {
                using (stream)
                {
                    stream.Write(content);
                    stream.Flush(flushToDisk: true);
                }
                return tempPath;
            }
            catch
            {
                TryDeleteTemp(tempPath);
                throw;
            }
        }

        /// <summary>
        /// Create "&lt;slot&gt;.json.&lt;pid&gt;.&lt;n&gt;.tmp" exclusively. The name is
        /// unique to this process and save; a stale file of an earlier process
        /// with the same id is skipped, never overwritten.
        /// </summary>
        private static FileStream CreateTempFile(string filePath, out string tempPath)
        {
            while (true)
            {
                long n = Interlocked.Increment(ref _tempFileCounter);
                tempPath = $"{filePath}.{System.Environment.ProcessId}.{n}.tmp";
                try
                {
                    return new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                }
                catch (IOException) when (File.Exists(tempPath))
                {
                    // Taken by a temp file an earlier process left: try the next name.
                }
            }
        }

        /// <summary>
        /// Make the temp file the slot: rotate .bak2→.bak3 and .bak1→.bak2 (the
        /// oldest backup drops off), then in one step the current save becomes
        /// .bak1 and the temp file takes the slot's name. The slot file never
        /// goes missing and never holds a partial write. Caller holds the slot lock.
        /// </summary>
        private static void CommitSave(string tempPath, string filePath)
        {
            if (!File.Exists(filePath))
            {
                File.Move(tempPath, filePath, overwrite: true);
                return;
            }

            for (int i = MaxBackups; i > 1; i--)
            {
                string older = BackupPath(filePath, i);
                string newer = BackupPath(filePath, i - 1);
                if (File.Exists(newer)) File.Move(newer, older, overwrite: true);
                else File.Delete(older);
            }

            File.Replace(tempPath, filePath, BackupPath(filePath, 1), ignoreMetadataErrors: true);
        }

        /// <summary>
        /// Remove a temp file after a failed save. A failure to delete it must
        /// not hide the save's own exception, which the caller rethrows.
        /// </summary>
        private static void TryDeleteTemp(string tempPath)
        {
            try { File.Delete(tempPath); }
            catch (IOException) { /* the save's exception is what matters */ }
            catch (UnauthorizedAccessException) { /* likewise */ }
        }

        /// <summary>
        /// Read a save without blocking a writer: FileShare.Delete lets a
        /// concurrent save replace the file while it is open (Windows refuses
        /// to replace a file opened without it).
        /// </summary>
        private static string ReadSaveText(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
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

            // P9/P11 additive depth-system collections (null for old saves or
            // hand-edited nulls) — keep them empty-safe.
            state.InsurancePolicies ??= new();
            state.BloomMitigations ??= new();

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

            // TD-04 snapshots: null snapshot = pre-snapshot save (models
            // resume cleared); non-null with null dicts = hand-edited save.
            if (state.MortalityState != null)
            {
                state.MortalityState.DiseaseRates ??= new();
                state.MortalityState.StressStates ??= new();
            }

            // Cohort SmoltQualityTier is an additive sea-transfer carryover
            // field. Older saves decode as Standard (0); hand-edited or future
            // saves may carry an undefined value — clamp to Standard so the
            // quality registry never has to guess.
            foreach (var pop in state.Populations)
            {
                if (pop?.Cohorts == null) continue;
                foreach (var cohort in pop.Cohorts)
                {
                    if (cohort == null) continue;
                    if (!Enum.IsDefined(typeof(LakskongenSim.Farm.SmoltQualityTier), cohort.Quality))
                        cohort.Quality = LakskongenSim.Farm.SmoltQualityTier.Standard;
                }
            }

            // S2: Pens and Populations are the same fish serialized twice —
            // re-link by Id so both lists share instances again.
            GameStateRelinker.RelinkIds(state);

            return state;
        }
    }
}
