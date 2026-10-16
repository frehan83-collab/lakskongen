using Godot;
using LakskongenSim.Core;
using LakskongenSim.Finance;
using LakskongenSim.Biology;
using LakskongenSim.Data;
using LakskongenSim.Integration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
// Namespace aliases for the Phase 5/6 features (sludge, carbon, cohort transfers,
// scenarios) so qualified references like Farm.SludgeValorization resolve from
// this global-namespace file.
using Farm = LakskongenSim.Farm;
using Events = LakskongenSim.Events;
using Processing = LakskongenSim.Processing;
using Logistics = LakskongenSim.Logistics;

// GameBridge.Core.cs — partial class slice (see project docs).
// <summary>
// Core slice: Godot lifecycle, [Signal] declarations, engine/state references,
// game management (new/load), time control, the spot-hire wellboat pool, all
// flat computed properties for GDScript, analytics/tutorial/achievement/audio
// accessors, the per-tick simulation loop and health checks, the research API,
// the visual-milestone readers, and _ExitTree.
// </summary>
/// <summary>
/// Singleton C# autoload — bridges SimulationEngine to Godot.
/// Manages engine lifecycle, exposes flat scalar properties for GDScript consumption,
/// and emits Godot signals whenever state changes.
/// </summary>
public partial class GameBridge : Node
{
    // ── Signals ──────────────────────────────────────────────────────────────

    [Signal] public delegate void GameStateUpdatedEventHandler();
    [Signal] public delegate void SimulationTickedEventHandler(int day);
    [Signal] public delegate void GameStartedEventHandler();
    [Signal] public delegate void GameEndedEventHandler(string reason);
    /// <summary>A tutorial step was completed (D16: steps pay no cash, so no reward argument).</summary>
    [Signal] public delegate void TutorialStepCompletedEventHandler(int stepNumber, string title);
    [Signal] public delegate void AudioAlertTriggeredEventHandler(string alertKey);
    /// <summary>An achievement unlocked; <c>medal</c> is "bronze" | "silver" | "gold" | "platinum" (D16: medals, never cash).</summary>
    [Signal] public delegate void AchievementUnlockedEventHandler(string id, string name, string description, string medal, string tier);

    // ── Engine reference ──────────────────────────────────────────────────────

    public SimulationEngine SimulationEngine { get; private set; }

    // ── Mod system ────────────────────────────────────────────────────────────

    private ModLoader? _modLoader;
    private string _modsDirectory = "";

    // ── Dashboard Data Provider ──────────────────────────────────────────────────

    private DashboardDataProvider _dashboardDataProvider;

    // ── Auto-tick timer ───────────────────────────────────────────────────────

    private Timer _simulationTimer;
    private float _timeSpeed = 1.0f;
    /// <summary>The player's own pause (holds aside; GameBridge.Clock.cs).</summary>
    private bool _isPaused = false;
    private bool _gameRunning = false;

    // ── Achievement tracking ──────────────────────────────────────────────────

    private string _latestAchievementName = "";
    private string _latestAchievementDescription = "";

    // ── Research signals ──────────────────────────────────────────────────────

    [Signal] public delegate void ResearchCompletedEventHandler(string nodeId, string nodeName);
    [Signal] public delegate void ScenarioLoadedEventHandler(string scenarioId, string scenarioName);
    [Signal] public delegate void ScenarioWonEventHandler(string scenarioId, string scenarioName);
    [Signal] public delegate void ScenarioLostEventHandler(string scenarioId, string reason);
    [Signal] public delegate void SustainabilityChangedEventHandler(int score, string level);
    [Signal] public delegate void FinanceTransactionEventHandler(string type, double amount, string description);
    [Signal] public delegate void FarmActionCompletedEventHandler(string action, string siteId, bool success);
    [Signal] public delegate void WeatherEventEventHandler(string eventType, double severity, string description);
    [Signal] public delegate void DiseaseOutbreakEventHandler(string diseaseType, string siteId, double severity, double treatmentCost);

    // ── Godot lifecycle ───────────────────────────────────────────────────────

    public override void _Ready()
    {
        SimulationEngine = new SimulationEngine();
        _dashboardDataProvider = new DashboardDataProvider(SimulationEngine);

        _simulationTimer = new Timer();
        _simulationTimer.WaitTime = 1.0f; // 1 second = 1 simulated day at 1x
        _simulationTimer.Timeout += OnSimulationTick;
        AddChild(_simulationTimer);

        InitializeModLoader();

        GD.Print("GameBridge: initialized with dashboard data provider");
    }

    /// <summary>
    /// Create the ModLoader against a per-user mods directory (preferred) or,
    /// as a fallback, a <c>mods/</c> folder beside the executable.
    /// </summary>
    private void InitializeModLoader()
    {
        try
        {
            _modsDirectory = ResolveModsDirectory();
            DirAccess.MakeDirRecursiveAbsolute(_modsDirectory);
            _modLoader = new ModLoader(_modsDirectory);
            _modLoader.DiscoverAndLoad();
            // Phase 4.6: hand the resolved directory to the engine; every
            // Initialize() then applies mod events/research/scenarios.
            SimulationEngine?.LoadMods(_modsDirectory);
            GD.Print($"GameBridge: ModLoader ready at {_modsDirectory} " +
                     $"({_modLoader.LoadedMods.Count} loaded, {_modLoader.LoadErrors.Count} errors)");
        }
        catch (Exception ex)
        {
            _modLoader = null;
            GD.PrintErr($"GameBridge: ModLoader init failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Point the mod loader (and the engine) at another mods directory and
    /// load it; an empty path goes back to the player's user://mods. A test
    /// seam like SaveLoadManager.UseSaveDirectory: the UI flow tests load a
    /// mod from a per-checkout folder instead of the player's. Returns the
    /// number of mods loaded.
    /// </summary>
    public int UseModsDirectory(string directory)
    {
        _modsDirectory = string.IsNullOrWhiteSpace(directory) ? ResolveModsDirectory() : directory;
        DirAccess.MakeDirRecursiveAbsolute(_modsDirectory);
        _modLoader = new ModLoader(_modsDirectory);
        var loaded = _modLoader.DiscoverAndLoad();
        SimulationEngine?.LoadMods(_modsDirectory);
        return loaded.Count;
    }

    private static string ResolveModsDirectory()
    {
        // user://mods is writable and survives game updates/installs.
        var userMods = ProjectSettings.GlobalizePath("user://mods");
        if (!string.IsNullOrEmpty(userMods))
            return userMods;

        // Fallback: <exe dir>/mods
        var exeDir = Path.GetDirectoryName(OS.GetExecutablePath());
        return Path.Combine(string.IsNullOrEmpty(exeDir) ? "." : exeDir, "mods");
    }

    // ── Game management ───────────────────────────────────────────────────────

    /// <summary>Initialize and start a new game. It starts paused on its briefing card (U2-07).</summary>
    public void StartNewGame(string playerName, int startingCapital = 10_000_000)
        => StartNewGameInternal(playerName, startingCapital, seed: null, briefing: true);

    /// <summary>
    /// Initialize and start a new game with an explicit RNG seed. QA/screenshot
    /// path only (deterministic runs); players always get a fresh random seed
    /// through <see cref="StartNewGame"/>. Not reachable from player UI.
    /// </summary>
    public void StartQaNewGame(string playerName, int startingCapital, int seed)
        => StartNewGameInternal(playerName, startingCapital, seed, briefing: false);

    /// <summary>
    /// <paramref name="briefing"/>: a player's game, which starts paused on its
    /// briefing card; a QA game keeps the clock as it was (GameBridge.Clock.cs).
    /// </summary>
    private void StartNewGameInternal(string playerName, int startingCapital, int? seed, bool briefing)
    {
        SimulationEngine.FinanceManager = new FinanceManager(startingCapital);
        // S10: the live game was never seeded — every subsystem fell back to
        // unseeded System.Random. A player game rolls one non-zero seed in
        // Initialize, which records it on the state (P0-07); the QA path passes
        // a fixed seed so screenshot/headless runs replay.
        SimulationEngine.Initialize(seed: seed);
        int resolvedSeed = SimulationEngine.State.RandomSeed;

        // P11: a new game starts with no in-memory mitigation installs and a
        // clean serializable mirror (the registry outlives Initialize).
        _installedMitigations.Clear();
        SyncMitigationsToState();
        ResetSessionState();
        SimulationEngine.Tutorial.OnStepCompleted += step =>
            EmitSignal(SignalName.TutorialStepCompleted, step.StepNumber, step.Title);
        // Single-wire: StartNewGame/LoadGame may run repeatedly on one
        // engine instance — unhook first so handlers never stack.
        SimulationEngine.OnAchievementUnlocked -= HandleAchievementUnlocked;
        SimulationEngine.OnAchievementUnlocked += HandleAchievementUnlocked;
        SimulationEngine.OnResearchCompleted -= HandleResearchCompleted;
        SimulationEngine.OnResearchCompleted += HandleResearchCompleted;
        SimulationEngine.OnScenarioWon -= HandleScenarioWon;
        SimulationEngine.OnScenarioWon += HandleScenarioWon;
        SimulationEngine.OnScenarioLost -= HandleScenarioLost;
        SimulationEngine.OnScenarioLost += HandleScenarioLost;
        RegisterModScenariosIntoEngine();
        _gameRunning = true;
        BeginClockForNewGame(briefing);

        EmitSignal(SignalName.GameStarted);
        EmitSignal(SignalName.GameStateUpdated);

        GD.Print($"GameBridge: new game for '{playerName}', capital {startingCapital:N0} NOK, seed {resolvedSeed}");
    }

    /// <summary>Restore a saved game state.</summary>
    public void LoadGame(GameState snapshot)
    {
        SimulationEngine.FinanceManager = new FinanceManager(snapshot.Balance);
        // S10: resume with the stored seed when present. RestoreFromLoad then
        // imports every RNG stream position (including lazily created
        // per-site/per-facility streams), so the loaded game continues the
        // exact pre-save trajectory (SaveLoadMatrixTests).
        int? seed = snapshot.RandomSeed != 0 ? snapshot.RandomSeed : null;
        SimulationEngine.Initialize(seed: seed);

        // Wire tutorial signal (same as StartNewGame — must be done after Initialize())
        SimulationEngine.Tutorial.OnStepCompleted += step =>
            EmitSignal(SignalName.TutorialStepCompleted, step.StepNumber, step.Title);
        // Single-wire: LoadGame may run repeatedly on one engine
        // instance — unhook first so handlers never stack.
        SimulationEngine.OnAchievementUnlocked -= HandleAchievementUnlocked;
        SimulationEngine.OnAchievementUnlocked += HandleAchievementUnlocked;
        SimulationEngine.OnResearchCompleted -= HandleResearchCompleted;
        SimulationEngine.OnResearchCompleted += HandleResearchCompleted;
        SimulationEngine.OnScenarioWon -= HandleScenarioWon;
        SimulationEngine.OnScenarioWon += HandleScenarioWon;
        SimulationEngine.OnScenarioLost -= HandleScenarioLost;
        SimulationEngine.OnScenarioLost += HandleScenarioLost;
        RegisterModScenariosIntoEngine();

        // Phase 1: the load path has ONE owner. AdoptSavedState restores the
        // day counter, ownership flags and all persistent collections, then
        // re-injects model dependencies (the old hand-copied list moved there
        // verbatim; PlayerScriptProbe drives the identical path headless).
        SimulationEngine.AdoptSavedState(snapshot);

        // P11 persistence: rebuild the in-memory mitigation registry from the
        // saved list, then re-normalize the serializable mirror.
        RestoreMitigationsFromState(snapshot.BloomMitigations);
        SyncMitigationsToState();

        // U2-04: the feed price is the engine's calibrated one after a load as
        // in a new game (the Phase 4.4 recipe used to replace it here, so a
        // loaded game paid 14.97 NOK/kg against a new game's 22.3); each site's
        // recipe is part of its feeding plan, saved with the site.
        ResetSessionState();

        _gameRunning = true;
        BeginClockForLoadedGame();
        EmitSignal(SignalName.GameStarted);
        EmitSignal(SignalName.GameStateUpdated);

        GD.Print($"GameBridge: game loaded at day {snapshot.Day}");
    }

    // ── Time control ──────────────────────────────────────────────────────────
    // Speeds, pause, holds, "Til neste hendelse" and the pacing rules live in
    // GameBridge.Clock.cs (U2-07).

    /// <summary>Manually advance simulation by exactly one day.</summary>
    public void AdvanceDay() => AdvanceDays(1);

    /// <summary>Manually advance simulation by N days (for skip buttons).</summary>
    public void AdvanceDays(int days)
    {
        if (!_gameRunning || SimulationEngine == null) return;

        try
        {
            SimulationEngine.Advance(days);
            ProjectDayNotes();
            AnnounceDay();
            CheckGameOverConditions();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"GameBridge.AdvanceDays error: {ex.Message}");
        }
    }

    // ── Phase 2.4 spot-hire wellboat pool ──

    /// <summary>Pending wellboat jobs (for UI queue + alerts).</summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetWellboatJobs()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var jobs = SimulationEngine?.State?.WellboatJobs;
        if (jobs == null) return result;
        int today = SimulationEngine?.State?.Day ?? 0;
        foreach (var j in jobs)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = j.Id,
                ["site_id"] = j.SiteId,
                ["kind"] = j.Kind,
                ["method"] = j.Method,
                ["eta_days"] = System.Math.Max(0, j.DueDay - today)
            });
        }
        return result;
    }

    /// <summary>
    /// Request a spot-hire wellboat (Phase 2.4) or ROV inspection (Phase 3.4):
    /// harvest (150k, 5d), delouse (100k + treatment at execution, 3d),
    /// rov (75k, 2d: net cleaning +15 integrity + findings report),
    /// freshwater (120k + bath at execution, 4d: lice + AGD, zero resistance).
    /// One pending job per site+kind. Returns true when booked.
    /// </summary>
    public bool RequestWellboat(string siteId, string kind, string method = "")
    {
        // Phase 1 wave 2: booking and execution live in the library
        // (SimulationEngine.RequestWellboat + engine-side post-tick execution).
        // The bridge preserves the bool + FarmActionCompleted surface; the
        // duplicate-booking path stays silent exactly like before.
        if (SimulationEngine == null)
        {
            EmitSignal(SignalName.FarmActionCompleted, "wellboat", siteId, false);
            return false;
        }
        var result = SimulationEngine.RequestWellboat(siteId, kind, method);
        if (!result.Ok)
        {
            if (result.ErrorKey != "already_booked")
            {
                GD.PrintErr($"GameBridge: {result.Message}");
                EmitSignal(SignalName.FarmActionCompleted, "wellboat", siteId, false);
            }
            return false;
        }
        EmitSignal(SignalName.FarmActionCompleted, "wellboat", siteId, true);
        EmitSignal(SignalName.GameStateUpdated);
        return true;
    }

    /// <summary>
    /// Project the engine's per-advance DayNote ledger onto the legacy signal
    /// surface (Phase 1 wave 2). The engine executes and logs; this layer only
    /// re-emits what the UI already knows how to consume.
    /// </summary>
    private void ProjectDayNotes()
    {
        var engine = SimulationEngine;
        if (engine == null) return;
        foreach (var note in engine.DrainDayNotes())
        {
            // Listeners date what they hear by the note's own day: a
            // multi-day advance drains every day's notes at once (U1-05).
            ProjectedNoteDay = note.Day;
            switch (note.Kind)
            {
                case "wellboat_delouse":
                    EmitSignal(SignalName.FarmActionCompleted, "wellboat", note.SiteId, note.Success);
                    EmitSignal(SignalName.FarmActionCompleted, "delousing", note.SiteId, note.Success);
                    break;
                case "wellboat_harvest":
                    // U2-05: the harvest's stinger plays once, with its one
                    // toast (ConsequenceDirector), not here as well.
                    EmitSignal(SignalName.FarmActionCompleted, "wellboat", note.SiteId, note.Success);
                    EmitSignal(SignalName.FarmActionCompleted, "harvest", note.SiteId, note.Success);
                    break;
                case "harvest_batch":
                    // S2-04: a planned harvest slaughtered a batch (cash moved).
                    EmitSignal(SignalName.GameStateUpdated);
                    break;
                case "harvest_completed":
                    // S2-04: the order's last batch: one harvest toast per harvest
                    // (U2-05: the toast carries the stinger and the report).
                    EmitSignal(SignalName.FarmActionCompleted, "harvest", note.SiteId, note.Success);
                    EmitSignal(SignalName.GameStateUpdated);
                    break;
                case "wellboat_rov":
                case "wellboat_freshwater":
                    EmitSignal(SignalName.FarmActionCompleted, "wellboat", note.SiteId, note.Success);
                    break;
                case "storm_impact":
                    EmitSignal(SignalName.WeatherEvent, note.EventType, note.Severity, note.Message);
                    break;
                case "solvency_warning":
                case "bankruptcy":
                    // S1-03: a warning rung or the bankruptcy verdict; the
                    // condition alerts (SimState) and the inbox re-read the
                    // ladder, the game-over check ends the game.
                    engine.Audio?.TriggerAlert("market_crash"); // financial_crisis.wav
                    EmitSignal(SignalName.GameStateUpdated);
                    break;
                case "license_auction":
                    // Phase 4.1: growth-licence auction opened/settled — push a
                    // generic refresh so map/UI toasts re-read auction state.
                    EmitSignal(SignalName.GameStateUpdated);
                    break;
                case "capacity_breach":
                case "capacity_complied":
                    // S2-01a: a biomass-over-MTB order opened, closed or ended in
                    // a forced harvest; the capacity overview re-reads it.
                    if (note.Kind == "capacity_breach") engine.Audio?.TriggerAlert("market_crash");
                    EmitSignal(SignalName.GameStateUpdated);
                    break;
            }
        }
        ProjectedNoteDay = -1;
        // U2-06: what the engine's event bus delivered during the advance
        // (the notes above, storyteller events, traffic lights, insurance).
        ProjectGameEvents();
    }

    // ── Computed properties for GDScript ─────────────────────────────────────
    // GDScript can read these directly via the autoload singleton.

    /// <summary>Current simulation day.</summary>
    public int CurrentDay => SimulationEngine?.State?.Day ?? 0;

    /// <summary>
    /// While a day note's signal is being emitted, the sim day the note
    /// happened on (-1 otherwise). SimState dates event alerts by it (U1-05).
    /// </summary>
    public int ProjectedNoteDay { get; private set; } = -1;

    /// <summary>
    /// True once a game was started or loaded (and not ended). GameScene uses it
    /// to keep a game the main menu already started or loaded (F056).
    /// </summary>
    public bool IsGameRunning => _gameRunning;

    /// <summary>Current balance in NOK.</summary>
    public double Balance => (double)(SimulationEngine?.FinanceManager?.Balance ?? 0m);

    /// <summary>Bankruptcy floor (NOK, negative) of the active scenario (S1-03).</summary>
    public double BankruptcyFloorNok => (double)(SimulationEngine?.BankruptcyFloorNok ?? -1_000_000m);

    /// <summary>Bankruptcy warning rung of today's cash: 0 solvent, 1 overdraft, 2 bank warning, 3 final warning (S1-03).</summary>
    public int SolvencyWarningLevel => SimulationEngine?.SolvencyWarningLevel ?? 0;

    /// <summary>True once the engine declared bankruptcy (S1-03).</summary>
    public bool IsBankrupt => SimulationEngine?.IsBankrupt ?? false;

    /// <summary>Total live fish count across all populations.</summary>
    public int TotalFishCount =>
        SimulationEngine?.State?.Populations
            .SelectMany(p => p.Cohorts)
            .Sum(c => c.Count) ?? 0;

    /// <summary>Total fish biomass in kg across all populations.</summary>
    public double TotalBiomassKg =>
        SimulationEngine?.State?.Populations
            .SelectMany(p => p.Cohorts)
            .Sum(c => (double)c.Count * c.MeanWeightKg) ?? 0.0;

    /// <summary>Current sea temperature in °C.</summary>
    public double CurrentTemperatureC => SimulationEngine?.Weather?.CurrentTemperatureC ?? 0.0;

    /// <summary>Current effective market price (NOK/kg, includes certification multiplier).</summary>
    public double MarketPricePerKg =>
        (double)(SimulationEngine?.MarketSimulation?.PricePerKg ?? 70m);

    /// <summary>Weekly average market price (NOK/kg).</summary>
    public double WeeklyAvgPricePerKg =>
        (double)(SimulationEngine?.MarketSimulation?.WeeklyAvgPrice ?? 70m);

    /// <summary>Sustainability score (0–100).</summary>
    public int SustainabilityScore =>
        SimulationEngine?.SustainabilityManager?.CurrentScore ?? 0;

    /// <summary>Current certification level as a string (e.g. "ASCCertified").</summary>
    public string SustainabilityLevel =>
        SimulationEngine?.SustainabilityManager?.CurrentCertification.ToString() ?? "NonCompliant";

    // 🔗 Production Chain API ─────────────────────────────────────────────────
    
    /// <summary>Count of fish at each life stage across all populations.</summary>
    public int GetFishCountByStage(string stageName)
    {
        if (SimulationEngine?.State?.Populations == null) return 0;
        if (!Enum.TryParse<LakskongenSim.Data.LifeStage>(stageName, true, out var stage)) return 0;
        return SimulationEngine.State.Populations
            .SelectMany(p => p.Cohorts)
            .Where(c => c.Stage == stage)
            .Sum(c => c.Count);
    }

    /// <summary>Biomass (kg) of fish at a specific life stage.</summary>
    public double GetBiomassByStage(string stageName)
    {
        if (SimulationEngine?.State?.Populations == null) return 0.0;
        if (!Enum.TryParse<LakskongenSim.Data.LifeStage>(stageName, true, out var stage)) return 0.0;
        return SimulationEngine.State.Populations
            .SelectMany(p => p.Cohorts)
            .Where(c => c.Stage == stage)
            .Sum(c => c.Count * c.MeanWeightKg);
    }

    /// <summary>Whether the player owns a hatchery.</summary>
    public bool OwnsHatchery => SimulationEngine?.State?.OwnsHatchery ?? false;

    /// <summary>Whether the player owns a processing plant.</summary>
    public bool OwnsProcessingPlant => SimulationEngine?.State?.OwnsProcessingPlant ?? false;

    /// <summary>VAP plant tier (Phase 3.5): "", "Small", "Medium", "Large".</summary>
    public string ProcessingTier => SimulationEngine?.State?.ProcessingTier ?? "";

    /// <summary>Number of export markets unlocked.</summary>
    public int ExportMarketCount => SimulationEngine?.State?.ExportMarkets?.Count ?? 0;

    /// <summary>Total processing throughput from snapshots (kg).</summary>
    public double ProcessingThroughputKg
    {
        get
        {
            var snapshots = SimulationEngine?.State?.ProcessingSnapshots;
            if (snapshots == null || snapshots.Count == 0) return 0.0;
            return (double)snapshots.Sum(s => s.LastInputKg);
        }
    }

    // 📊 External Analytics ──────────────────────────────────────────────────

    public int ExternalWarningCount => SimulationEngine?.State?.ExternalHealthAnalytics?.WarningCount ?? 0;
    
    public double ExternalCompositeRisk => SimulationEngine?.State?.ExternalHealthAnalytics?.CompositeRiskScore ?? 0.0;
    
    public bool ExternalCrisisActive => (SimulationEngine?.State?.ExternalHealthCrisis?.CrisisType ?? "none") != "none";
    
    public string ExternalCrisisLabel => SimulationEngine?.State?.ExternalHealthCrisis?.Severity ?? "normal";
    
    public string ExternalSnapshotSource => SimulationEngine?.State?.ExternalDataAnalytics?.SourceSummary ?? "Offline";
    
    public int ExternalSnapshotFreshnessDays => SimulationEngine?.State?.ExternalDataAnalytics?.SourceFreshnessDays ?? -1;

    /// <summary>Number of active environment zones.</summary>
    public int ZoneCount => SimulationEngine?.State?.Zones?.Count ?? 0;

    /// <summary>Number of active sea sites.</summary>
    public int SiteCount => SimulationEngine?.State?.SeaSites?.Count ?? 0;

    /// <summary>Average sea lice per fish across the farm.</summary>
    /// <summary>Phase 2.1: biomass-weighted mean across site lice models
    /// (falls back to the legacy company model when nothing is stocked).</summary>
    public double SeaLicePerFish => SimulationEngine?.GetCompanyLiceAverage() ?? 0.0;

    // ── Phase 1.1 live KPI scalars (additive; backed by Feed/Analytics/Zone/Weather) ──

    /// <summary>Current feed conversion ratio (live from FeedSystem).</summary>
    public double CurrentFCR => SimulationEngine?.Feed?.CurrentFCR ?? 1.15;

    /// <summary>The player's company as the UI names it (the brand the engine
    /// trades under; the dashboard title is "&lt;company&gt; — Oversikt").</summary>
    public string CompanyName =>
        SimulationEngine?.BrandReputation?.BrandName is { Length: > 0 } name ? name : "Lakskongen";

    /// <summary>
    /// The FCR goal and the ceiling where feeding is failing, read from
    /// CalibrationRegistry (roadmap U1-03: one FCR target for every panel; the
    /// banner, dashboard, telemetry and alerts each kept their own before).
    /// Keys: "target" (best-practice FCR), "poor" (above it feeding is failing).
    /// </summary>
    public Godot.Collections.Dictionary GetFcrTargets()
    {
        var bio = SimulationEngine?.Calibration?.Biology
            ?? LakskongenSim.Calibration.CalibrationRegistry.Default.Biology;
        return new Godot.Collections.Dictionary { ["target"] = bio.OptimalFCR, ["poor"] = bio.PoorFCR };
    }

    /// <summary>Days since the latest stocking at a site that holds fish, or -1
    /// when no stocked site has a known stocking day (roadmap U1-08: the FCR
    /// alarm waits until freshly stocked fish have settled).</summary>
    public int DaysSinceStocking
    {
        get
        {
            var state = SimulationEngine?.State;
            if (state?.SeaSites == null) return -1;
            int latest = -1;
            foreach (var site in state.SeaSites)
                if (site.LastStockedDay >= 0 && site.Pens.Any(p => p.Cohorts.Any(c => c.Count > 0)))
                    latest = Math.Max(latest, site.LastStockedDay);
            return latest < 0 ? -1 : state.Day - latest;
        }
    }

    /// <summary>Current appetite 0-1 (Phase 2.1). Low = fish won't eat; cut ration.</summary>
    public double Appetite01 => SimulationEngine?.Feed?.LastAppetite01 ?? 1.0;

    /// <summary>Average individual fish weight in kg across all populations.</summary>
    public double AvgWeightKg
    {
        get
        {
            var cohorts = SimulationEngine?.State?.Populations
                .SelectMany(p => p.Cohorts).ToList();
            if (cohorts == null || cohorts.Count == 0) return 0.0;
            double biomass = cohorts.Sum(c => (double)c.Count * c.MeanWeightKg);
            int count = cohorts.Sum(c => c.Count);
            return count > 0 ? biomass / count : 0.0;
        }
    }

    /// <summary>Today's mortality rate as a fraction (latest analytics snapshot).</summary>
    public double MortalityRateToday => SimulationEngine?.Analytics?.GetLatest("mortality_rate") ?? double.NaN;

    /// <summary>Dissolved oxygen (mg/L) in the first zone's water chemistry.</summary>
    public double ZoneOxygenMgL => SimulationEngine?.State?.Zones?.FirstOrDefault()?.WaterChemistry?.OxygenO2 ?? double.NaN;

    /// <summary>
    /// Oxygen the first zone's water holds at 100% saturation (mg/L) at its own
    /// temperature, from the sim's seawater solubility. The telemetry panel kept
    /// its own copy of the old freshwater table and read 82% for saturated water
    /// once the sim's solubility was corrected (S1-01).
    /// </summary>
    public double ZoneOxygenSaturationMgL
    {
        get
        {
            var chemistry = SimulationEngine?.State?.Zones?.FirstOrDefault()?.WaterChemistry;
            return chemistry == null
                ? double.NaN
                : LakskongenSim.Biology.WaterQuality.CalculateOxygenSaturation(chemistry.Temperature);
        }
    }

    /// <summary>pH in the first zone's water chemistry.</summary>
    public double ZonePh => SimulationEngine?.State?.Zones?.FirstOrDefault()?.WaterChemistry?.pH ?? double.NaN;

    /// <summary>Current wind speed in m/s (for forecast widget).</summary>
    public double WindSpeedMs => SimulationEngine?.Weather?.CurrentWindSpeedMs ?? 0.0;

    /// <summary>Current significant wave height in meters (for forecast widget).</summary>
    public double WaveHeightM => SimulationEngine?.Weather?.CurrentWaveHeight ?? 0.0;

    /// <summary>Most recent value of a named analytics metric, or NaN when empty.</summary>
    public double GetAnalyticsLatest(string metric)
        => SimulationEngine?.Analytics?.GetLatest(metric) ?? double.NaN;

    // ── Phase 1.2 storm/net scalars ──

    /// <summary>Lowest net integrity across all sea sites (100 when none).</summary>
    public double MinNetIntegrity =>
        SimulationEngine?.State?.SeaSites?.Count > 0
            ? SimulationEngine.State.SeaSites.Min(s => s.NetIntegrityPct)
            : 100.0;

    /// <summary>Honest 3-day storm probability from the weather Markov chain.</summary>
    public double StormRisk3Days => SimulationEngine?.Weather?.StormRiskNext3Days() ?? 0.0;

    // ── Phase 1.3 traffic-light zones ──

    /// <summary>
    /// Live traffic-light status per zone as an Array of Dictionaries.
    /// Keys: "zone" (string), "color" (string: Green/Yellow/Red),
    ///       "mab_factor" (double), "capacity_tonnes" (double),
    ///       "production_area" (int 1-13; 0 = national reference, no scenario),
    ///       "water_temp_c" (double, the zone's water today; S2-02).
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetZoneTrafficLights()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var engine = SimulationEngine;
        if (engine?.State?.Zones == null || engine.TrafficLightSystem == null) return result;

        foreach (var zone in engine.State.Zones)
        {
            var color = engine.TrafficLightSystem.GetZoneColor(zone.Id);
            result.Add(new Godot.Collections.Dictionary
            {
                ["zone"]            = zone.Id,
                ["color"]           = color.ToString(),
                ["mab_factor"]      = LakskongenSim.Finance.TrafficLightSystem.MabFactorFor(color),
                ["capacity_tonnes"] = zone.CarryingCapacityTonnes,
                ["production_area"] = engine.ProductionAreaOf(zone),
                ["water_temp_c"]    = engine.SeaTemperatureOfZone(zone.Id)
            });
        }
        return result;
    }

    /// <summary>Number of zones currently under red traffic light.</summary>
    public int RedZoneCount
    {
        get
        {
            var engine = SimulationEngine;
            if (engine?.State?.Zones == null || engine.TrafficLightSystem == null) return 0;
            return engine.State.Zones
                .Count(z => engine.TrafficLightSystem.GetZoneColor(z.Id).ToString() == "Red");
        }
    }

    /// <summary>
    /// Return the N most recent event log entries as an Array of Dictionaries,
    /// so GDScript can iterate them without needing C# type knowledge.
    /// Keys: "day" (int), "name" (string), "category" (string),
    ///       "description" (string), "balance_change" (double).
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetRecentEvents(int count = 10)
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var log = SimulationEngine?.State?.EventLog;
        if (log == null || log.Count == 0) return result;

        foreach (var evt in log.TakeLast(count))
        {
            var d = new Godot.Collections.Dictionary
            {
                ["day"]            = evt.Day,
                ["name"]           = _tr_or("EVENT_" + UpperSnake(evt.Name), evt.Name),
                ["category"]       = evt.Category.ToString(),
                ["description"]    = evt.Description,
                ["balance_change"] = (double)evt.BalanceChange
            };
            result.Add(d);
        }
        return result;
    }

    // ── Analytics API for GDScript ────────────────────────────────────────────

    /// <summary>
    /// Return time-series values for a named metric over the last <paramref name="days"/> days.
    /// Returns a Godot Array of floats (oldest → newest).
    /// <para>Valid metric keys are defined in <see cref="AnalyticsMetric"/> (e.g. "fish_count",
    /// "total_biomass_kg", "market_price_nok", "balance_nok", "temperature_c",
    /// "fcr", "lice_per_fish", "sustainability_score", "mortality_rate", "avg_weight_kg").</para>
    /// </summary>
    public Godot.Collections.Array<double> GetAnalyticsData(string metric, int days = 30)
    {
        var result = new Godot.Collections.Array<double>();
        var analytics = SimulationEngine?.Analytics;
        if (analytics == null) return result;

        foreach (var v in analytics.GetTimeSeries(metric, days))
            result.Add(v);

        return result;
    }

    /// <summary>
    /// Return a moving average of <paramref name="metric"/> over the last <paramref name="days"/> days
    /// using a rolling window of <paramref name="windowDays"/>.
    /// </summary>
    public Godot.Collections.Array<double> GetAnalyticsMovingAverage(string metric, int days = 30, int windowDays = 7)
    {
        var result = new Godot.Collections.Array<double>();
        var analytics = SimulationEngine?.Analytics;
        if (analytics == null) return result;

        foreach (var v in analytics.GetMovingAverage(metric, days, windowDays))
            result.Add(v);

        return result;
    }

    /// <summary>
    /// Return the trend for <paramref name="metric"/> over the last <paramref name="days"/> days.
    /// Returns "up", "down", or "stable".
    /// </summary>
    public string GetAnalyticsTrend(string metric, int days = 14)
    {
        var analytics = SimulationEngine?.Analytics;
        if (analytics == null) return "stable";

        return analytics.GetTrend(metric, days) switch
        {
            TrendDirection.Up   => "up",
            TrendDirection.Down => "down",
            _                   => "stable"
        };
    }

    /// <summary>All-time high value for <paramref name="metric"/>.</summary>
    public double GetAnalyticsAllTimeHigh(string metric)
        => SimulationEngine?.Analytics?.GetAllTimeHigh(metric) ?? double.NaN;

    /// <summary>All-time low value for <paramref name="metric"/>.</summary>
    public double GetAnalyticsAllTimeLow(string metric)
        => SimulationEngine?.Analytics?.GetAllTimeLow(metric) ?? double.NaN;

    // ── Tutorial properties for GDScript ─────────────────────────────────────

    /// <summary>Current tutorial step number (1–TotalSteps). Returns TotalSteps when complete.</summary>
    public int TutorialStepNumber => SimulationEngine?.Tutorial?.CurrentStepNumber ?? 1;

    /// <summary>Title of the current tutorial step.</summary>
    public string TutorialStepTitle => SimulationEngine?.Tutorial?.CurrentStep?.Title ?? "";

    /// <summary>Description/objective of the current tutorial step.</summary>
    public string TutorialDescription => SimulationEngine?.Tutorial?.CurrentStep?.Description ?? "";

    /// <summary>Contextual hint for the current tutorial step.</summary>
    public string TutorialHint => SimulationEngine?.Tutorial?.CurrentStep?.Hint ?? "";

    /// <summary>True after all tutorial steps are completed.</summary>
    public bool TutorialIsComplete => SimulationEngine?.Tutorial?.IsComplete ?? false;

    /// <summary>Number of completed tutorial steps.</summary>
    public int TutorialCompletedCount => SimulationEngine?.Tutorial?.CompletedCount ?? 0;

    /// <summary>Total tutorial steps (drives UI progress bars — never hardcode).</summary>
    public int TutorialTotalSteps => SimulationEngine?.Tutorial?.TotalSteps ?? 0;

    /// <summary>Skip the tutorial silently (no rewards). From TutorialUI skip button.</summary>
    public void SkipTutorial()
    {
        SimulationEngine?.Tutorial?.Skip();
    }

    // ── Achievement properties for GDScript ──────────────────────────────────

    /// <summary>Number of achievements the player has unlocked.</summary>
    public int AchievementUnlockedCount => SimulationEngine?.Achievements?.UnlockedCount ?? 0;

    /// <summary>Total number of achievements in the game.</summary>
    public int AchievementTotalCount => SimulationEngine?.Achievements?.TotalCount ?? 0;

    /// <summary>Name of the most recently unlocked achievement (empty if none yet).</summary>
    public string LatestAchievementName => _latestAchievementName;

    /// <summary>Description of the most recently unlocked achievement.</summary>
    public string LatestAchievementDescription => _latestAchievementDescription;

    /// <summary>
    /// Return all achievements as an Array of Dictionaries.
    /// Keys: "id" (string), "name" (string), "description" (string),
    ///       "tier" (string), "medal" (string: bronze|silver|gold|platinum), "unlocked" (bool).
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetAchievementList()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var sys = SimulationEngine?.Achievements;
        if (sys == null) return result;

        var all = sys.GetUnlocked().Concat(sys.GetLocked());
        foreach (var a in all)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"]          = a.Id,
                ["name"]        = a.Name,
                ["description"] = a.Description,
                ["tier"]        = a.Tier.ToString(),
                ["medal"]       = MedalKey(a.Medal),
                ["unlocked"]    = sys.IsUnlocked(a.Id)
            });
        }
        return result;
    }

    // ── Audio state properties for GDScript ──────────────────────────────────

    /// <summary>Primary ambient base track key (calm_sea | storm | wind | harbor | processing_plant).</summary>
    public string AudioBaseTrack => SimulationEngine?.Audio?.GetAmbienceProfile().BaseTrack ?? "calm_sea";

    /// <summary>Weather overlay layer key ("" | wind | rain | storm).</summary>
    public string AudioWeatherLayer => SimulationEngine?.Audio?.GetAmbienceProfile().WeatherLayer ?? "";

    /// <summary>Pending one-shot alert key ("" | disease_outbreak | market_crash | achievement | harvest | lice_warning).</summary>
    public string AudioAlertLayer => SimulationEngine?.Audio?.ActiveAlert ?? "";

    /// <summary>Master audio volume scalar 0–1.</summary>
    public float AudioVolume => SimulationEngine?.Audio?.MasterVolume ?? 1.0f;

    /// <summary>Current seasonal music track name (spring_theme | summer_theme | autumn_theme | winter_theme).</summary>
    public string AudioSeasonalTrack => SimulationEngine?.Audio?.GetSeasonalTrack() ?? "main_theme";

    /// <summary>Current weather state as a lowercase string (calm | wind | storm).</summary>
    public string CurrentWeatherState => SimulationEngine?.Audio?.CurrentWeatherKey ?? "calm";

    public string GetCurrentWeatherState() => SimulationEngine?.Weather?.CurrentState.ToString() ?? "Calm";
    public double GetCurrentTemperature() => SimulationEngine?.Weather?.CurrentTemperatureC ?? 0.0;
    public int GetCurrentDay() => SimulationEngine?.State?.Day ?? 0;

    // ── Data export (DataExportPanel) ───────────────────────────────────────

    /// <summary>Write the daily snapshots as CSV. Rows written, -1 on failure.</summary>
    public int ExportDailySnapshots(string path) => RunExport(e => e.ExportDailySnapshots(path));

    /// <summary>Write the 30-day financial report as CSV. Rows written, -1 on failure.</summary>
    public int ExportFinancialReport(string path) => RunExport(e => e.ExportFinancialReport(path));

    /// <summary>Write the daily population figures as CSV. Rows written, -1 on failure.</summary>
    public int ExportPopulationData(string path) => RunExport(e => e.ExportPopulationData(path));

    /// <summary>Write the event history as CSV. Rows written, -1 on failure.</summary>
    public int ExportEventHistory(string path) => RunExport(e => e.ExportEventHistory(path));

    private int RunExport(Func<LakskongenSim.Core.DataExporter, int> export)
    {
        var state = SimulationEngine?.State;
        if (state == null) return -1;
        try
        {
            return export(new LakskongenSim.Core.DataExporter(state));
        }
        catch (Exception ex)
        {
            GD.PrintErr($"GameBridge: export failed - {ex.Message}");
            return -1;
        }
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private void OnSimulationTick()
    {
        if (!_gameRunning || SimulationEngine == null) return;

        _inTick = true;
        long tickStart = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            SimulationEngine.Advance(1);
            LastTickEngineMs = System.Diagnostics.Stopwatch.GetElapsedTime(tickStart).TotalMilliseconds;
            ProjectDayNotes();
            AnnounceDay();

            // Fire audio alert signal if a new alert was queued this tick, then clear it
            var alertKey = SimulationEngine.Audio?.ActiveAlert ?? "";
            if (!string.IsNullOrEmpty(alertKey))
            {
                EmitSignal(SignalName.AudioAlertTriggered, alertKey);
                SimulationEngine.Audio?.ClearAlert();
            }

            // Storm/temperature impacts now run inside the engine's daily
            // advance (one path for tick and skip); the DayNote ledger
            // projects the WeatherEvent signal via ProjectDayNotes.

            // Check for disease outbreaks
            CheckDiseaseOutbreaks();

            CheckGameOverConditions();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"GameBridge tick error: {ex.Message}");
            EndGame(string.Format(UiCulture, _tr_or("GAME_OVER_SIM_ERROR", "Simulation error: {0}"), ex.Message));
        }
        finally
        {
            _inTick = false;
        }
        // U2-07: the day and its events are in; the pacing rules may slow or
        // stop the clock before the next day.
        ApplyPacing();
        LastTickMs = System.Diagnostics.Stopwatch.GetElapsedTime(tickStart).TotalMilliseconds;
        TickCount++;
    }

    private void HandleAchievementUnlocked(LakskongenSim.Core.Achievement achievement)
    {
        _latestAchievementName = achievement.Name;
        _latestAchievementDescription = achievement.Description;
        EmitSignal(SignalName.AchievementUnlocked,
            achievement.Id,
            achievement.Name,
            achievement.Description,
            MedalKey(achievement.Medal),
            achievement.Tier.ToString());
        GD.Print($"GameBridge: achievement unlocked — {achievement.Name}");
    }

    /// <summary>Stable lower-case medal key for GDScript ("bronze" .. "platinum").</summary>
    private static string MedalKey(LakskongenSim.Core.AchievementMedal medal)
        => medal.ToString().ToLowerInvariant();

    private void HandleResearchCompleted(LakskongenSim.Core.ResearchNode node)
    {
        EmitSignal(SignalName.ResearchCompleted, node.Id, node.Name);
    }

    private void HandleScenarioWon(ScenarioDefinition scenario)
    {
        EmitSignal(SignalName.ScenarioWon, scenario.Id, scenario.Name);
        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: scenario won — {scenario.Name}");
    }

    private void HandleScenarioLost(ScenarioDefinition scenario, string reason)
    {
        EmitSignal(SignalName.ScenarioLost, scenario.Id, reason);
        EndGame(string.Format(UiCulture, _tr_or("GAME_OVER_SCENARIO_FAILED", "Scenario '{0}' is lost: {1}"), scenario.Name, reason));
    }

    /// <summary>
    /// Phase 4.6: mod scenarios (plus events and research) are applied by the
    /// engine itself — <see cref="SimulationEngine.Initialize"/> re-applies the
    /// directory configured via <see cref="SimulationEngine.LoadMods"/>.
    /// This bridge method just re-asserts the directory so a late-resolved
    /// mods folder still lands; the engine guards against double registration.
    /// </summary>
    private void RegisterModScenariosIntoEngine()
    {
        if (!string.IsNullOrEmpty(_modsDirectory))
            SimulationEngine?.LoadMods(_modsDirectory);
    }

    private static LakskongenSim.Data.ProductionRegion ParseRegion(string raw)
        => Enum.TryParse<LakskongenSim.Data.ProductionRegion>(raw, true, out var r)
            ? r
            : LakskongenSim.Data.ProductionRegion.Vestland;

    private static LakskongenSim.Data.WinCondition ParseWinCondition(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return LakskongenSim.Data.WinCondition.Profit;
        if (Enum.TryParse<LakskongenSim.Data.WinCondition>(raw, true, out var w)) return w;

        return raw.Trim().ToLowerInvariant() switch
        {
            "tutorial" => LakskongenSim.Data.WinCondition.Tutorial,
            "profit" or "revenue" => LakskongenSim.Data.WinCondition.Profit,
            "sustainability" or "reputation_and_profit" => LakskongenSim.Data.WinCondition.Sustainability,
            "survival" or "debt_and_profit" => LakskongenSim.Data.WinCondition.Survival,
            "competition" or "ipo" => LakskongenSim.Data.WinCondition.Competition,
            _ => LakskongenSim.Data.WinCondition.Profit
        };
    }

    // Track last weather check to avoid spam
    private int _lastDiseaseCheckDay = 0;

    /// <summary>
    /// Bridge state that belongs to one game (review F064): the weekly lice
    /// check's last day (a load or new game after day N skipped the checks until
    /// day N + 7, so a reload could go ~100 days without a lice warning) and
    /// the forecast cache (keyed by day number only, so a reload on the same day
    /// served the pre-reload projection). Called by both start paths.
    /// </summary>
    private void ResetSessionState()
    {
        _lastDiseaseCheckDay = SimulationEngine?.State?.Day ?? 0;
        _forecastCache = null;
        _forecastCacheDay = -1;
    }

    

    private void CheckDiseaseOutbreaks()
    {
        if (SimulationEngine?.SeaLice == null || SimulationEngine?.State?.SeaSites == null) return;
        
        // Check once per week to avoid spam
        if (SimulationEngine.State.Day - _lastDiseaseCheckDay < 7) return;
        _lastDiseaseCheckDay = SimulationEngine.State.Day;

        // Phase 1 wave 2: the per-site lice models (Phase 2.1) are the
        // simulation's authority; the legacy company model underreported.
        double licePerFish = SimulationEngine.GetCompanyLiceAverage();
        
        // Trigger outbreak notification when lice threshold is exceeded
        if (licePerFish > 0.5)
        {
            double severity = Math.Min(1.0, licePerFish / 2.0); // 0.5 lice = 0.25 severity, 2.0+ lice = 1.0 severity
            
            // Calculate treatment cost (mechanical delousing: ~50 NOK/fish)
            int totalFish = SimulationEngine.State.Populations
                .SelectMany(p => p.Cohorts)
                .Sum(c => c.Count);
            
            decimal treatmentCost = totalFish * 50m;
            
            string description = severity < 0.5 
                ? LakskongenSim.Core.SimText.Nb($"Moderat lusenivå ({licePerFish:F2} per fisk). Vurder behandling.")
                : LakskongenSim.Core.SimText.Nb($"Høyt lusenivå ({licePerFish:F2} per fisk)! Behandle snarest.");
            
            EmitSignal(SignalName.DiseaseOutbreak, "SeaLice", "all_sites", severity, (double)treatmentCost);
            // U2-06: the weekly reminder is a lice warning on the typed bus; it
            // joins the lice condition's key, so it is quiet while that holds.
            SimulationEngine.PublishGameEvent(new LakskongenSim.Events.GameEventRequest(
                LakskongenSim.Events.GameEventType.LiceTreatmentDue, SimulationEngine.State.Day)
            {
                Message = description,
                Value = licePerFish
            });
            ProjectGameEvents();
            
            // Add warning to event log
            SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
            {
                Day = SimulationEngine.State.Day,
                Name = "Luseadvarsel",
                Category = LakskongenSim.Events.EventCategory.Disease,
                Description = description,
                BalanceChange = 0m
            });
            
            GD.Print($"GameBridge: Disease outbreak — SeaLice (severity {severity:F2}, treatment cost {treatmentCost:N0} NOK)");
        }
    }

    /// <summary>
    /// The engine owns the bankruptcy verdict (S1-03, owner decision D16): a
    /// per-scenario floor from BalancingConfig with a warning ladder before it.
    /// This used to end every game at a hard-coded -1M NOK, 85 days before the
    /// first harvest of po3_red_sone, isa_utbrudd and algekatastrofe. The
    /// reason is a translation key (GAME_OVER_BANKRUPTCY).
    /// </summary>
    private void CheckGameOverConditions()
    {
        if (SimulationEngine?.IsBankrupt == true)
            EndGame(LakskongenSim.Core.SimulationEngine.BankruptcyReasonKey);
    }

    private void EndGame(string reason)
    {
        _gameRunning = false;
        _simulationTimer.Stop();
        EmitClock();
        EmitSignal(SignalName.GameEnded, reason);
        GD.Print($"GameBridge: game ended — {reason}");
    }

    // ── Research API for GDScript ─────────────────────────────────────────────

    /// <summary>ID of the research currently in progress (empty string if none).</summary>
    public string ActiveResearchId => SimulationEngine?.Research?.ActiveResearchId ?? "";

    /// <summary>Days elapsed on the active research.</summary>
    public int ResearchProgress => SimulationEngine?.Research?.ResearchProgress ?? 0;

    /// <summary>Days remaining on the active research (0 if none).</summary>
    public int ResearchDaysRemaining
    {
        get
        {
            var r = SimulationEngine?.Research;
            if (r == null || r.ActiveResearchId == null) return 0;
            if (!r.Nodes.TryGetValue(r.ActiveResearchId, out var node)) return 0;
            return Math.Max(0, node.ResearchDays - r.ResearchProgress);
        }
    }

    /// <summary>
    /// Return all research nodes as an Array of Dictionaries.
    /// Keys: "id", "name", "description", "branch", "tier",
    ///       "cost_nok", "research_days", "prerequisites" (Array[String]),
    ///       "unlocked" (bool), "is_active" (bool), "progress_days" (int),
    ///       "can_research" (bool), "effects" (Dictionary).
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetResearchNodes()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var research = SimulationEngine?.Research;
        if (research == null) return result;

        foreach (var node in research.Nodes.Values)
        {
            var prereqs = new Godot.Collections.Array<string>();
            foreach (var p in node.Prerequisites)
                prereqs.Add(p);

            var effects = new Godot.Collections.Dictionary();
            foreach (var kv in node.Effects)
                effects[kv.Key] = kv.Value;

            bool isActive = research.ActiveResearchId == node.Id;
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"]            = node.Id,
                ["name"]          = node.Name,
                ["description"]   = node.Description,
                ["branch"]        = node.Branch.ToString(),
                ["tier"]          = node.Tier,
                ["cost_nok"]      = (double)node.CostNOK,
                ["research_days"] = node.ResearchDays,
                ["prerequisites"] = prereqs,
                ["unlocked"]      = node.Unlocked,
                ["is_active"]     = isActive,
                ["progress_days"] = isActive ? research.ResearchProgress : 0,
                ["can_research"]  = research.CanResearch(node.Id),
                ["effects"]       = effects,
            });
        }
        return result;
    }

    /// <summary>
    /// Start researching a node. Returns true on success, false if the player cannot
    /// afford it, prerequisites are missing, or research is already in progress.
    /// </summary>
    public bool StartResearch(string nodeId)
    {
        var result = SimulationEngine?.StartResearchProject(nodeId);
        if (result == null || !result.Ok)
        {
            GD.Print($"GameBridge.StartResearch('{nodeId}'): {result?.ErrorKey ?? "engine_not_ready"} {result?.Message}");
            return false;
        }
        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: {result.Message}");
        return true;
    }

    /// <summary>Cancel the active research (no cost refund).</summary>
    public void CancelResearch()
    {
        SimulationEngine?.Research?.CancelResearch();
        EmitSignal(SignalName.GameStateUpdated);
    }

    /// <summary>True if the node with the given ID is unlocked.</summary>
    public bool IsResearchUnlocked(string nodeId)
        => SimulationEngine?.Research?.Nodes.TryGetValue(nodeId, out var n) == true && n.Unlocked;

    /// <summary>
    /// Combined effect value for a given key across all unlocked research nodes.
    /// Returns 0.0 if no unlocked node contributes to that key.
    /// </summary>
    public double GetResearchEffect(string effectKey)
        => SimulationEngine?.Research?.GetTotalEffect(effectKey) ?? 0.0;

    // ── FishWelfare Methods ───────────────────────────────────────────────────────

    /// <summary>Overall fish welfare score (0-100, where 100 is best).</summary>
    public double GetWelfareScore()
    {
        var welfare = SimulationEngine?.FishWelfare;
        if (welfare == null) return 50.0;
        
        // Convert ConditionScore (0-2) to 0-100 scale
        return welfare.ConditionScore * 50.0;
    }

    /// <summary>
    /// Returns welfare breakdown as a Dictionary.
    /// Keys: "condition_score", "stress_level", "mortality_risk", "health_status".
    /// </summary>
    public Godot.Collections.Dictionary GetWelfareBreakdown()
    {
        var result = new Godot.Collections.Dictionary();
        var welfare = SimulationEngine?.FishWelfare;
        if (welfare == null)
        {
            result["condition_score"] = 50.0;
            result["stress_level"] = "normal";
            result["mortality_risk"] = "low";
            result["health_status"] = "good";
            return result;
        }

        var conditionScore = welfare.ConditionScore;
        result["condition_score"] = conditionScore * 50.0; // 0-100 scale
        
        // Determine stress level
        string stressLevel = conditionScore switch
        {
            < 0.5 => "critical",
            < 0.8 => "high",
            < 1.2 => "normal",
            < 1.5 => "low",
            _ => "optimal"
        };
        result["stress_level"] = stressLevel;
        
        // Determine mortality risk
        string mortalityRisk = conditionScore switch
        {
            < 0.6 => "high",
            < 1.0 => "moderate",
            _ => "low"
        };
        result["mortality_risk"] = mortalityRisk;
        
        // Overall health status
        string healthStatus = conditionScore switch
        {
            < 0.7 => "poor",
            < 1.0 => "fair",
            < 1.3 => "good",
            _ => "excellent"
        };
        result["health_status"] = healthStatus;

        return result;
    }

    /// <summary>
    /// Returns welfare alerts as an Array of strings.
    /// Example: ["High stress levels detected", "Mortality risk increasing"].
    /// </summary>
    public Godot.Collections.Array<string> GetWelfareAlerts()
    {
        var result = new Godot.Collections.Array<string>();
        var welfare = SimulationEngine?.FishWelfare;
        if (welfare == null) return result;

        var conditionScore = welfare.ConditionScore;
        
        if (conditionScore < 0.6)
            result.Add("CRITICAL: Fish condition extremely poor");
        else if (conditionScore < 0.8)
            result.Add("WARNING: High stress levels detected");
        
        if (conditionScore < 0.7)
            result.Add("High mortality risk - immediate action required");
        
        // Check other factors (simplified)
        if (SimulationEngine?.SeaLice?.AverageLicePerFish > 2.0)
            result.Add("Sea lice levels affecting fish welfare");
        
        if (SimulationEngine?.Weather?.CurrentTemperatureC > 16.0 || 
            SimulationEngine?.Weather?.CurrentTemperatureC < 6.0)
            result.Add("Temperature stress affecting fish welfare");

        return result;
    }

    public override void _ExitTree()
    {
        _simulationTimer?.Stop();
        _simulationTimer?.QueueFree();
    }
}
