using Godot;
using LakskongenSim.Core;
using LakskongenSim.Finance;
using LakskongenSim.Biology;
using LakskongenSim.Data;
using System;
using System.Collections.Generic;
using System.Linq;

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
    [Signal] public delegate void TutorialStepCompletedEventHandler(int stepNumber, string title, double rewardNok);
    [Signal] public delegate void AudioAlertTriggeredEventHandler(string alertKey);
    [Signal] public delegate void AchievementUnlockedEventHandler(string name, string description, double rewardNok, string tier);

    // ── Engine reference ──────────────────────────────────────────────────────

    public SimulationEngine SimulationEngine { get; private set; }

    // ── Auto-tick timer ───────────────────────────────────────────────────────

    private Timer _simulationTimer;
    private float _timeSpeed = 1.0f;
    private bool _isPaused = false;
    private bool _gameRunning = false;

    // ── Achievement tracking ──────────────────────────────────────────────────

    private string _latestAchievementName = "";
    private string _latestAchievementDescription = "";

    // ── Research signals ──────────────────────────────────────────────────────

    [Signal] public delegate void ResearchCompletedEventHandler(string nodeId, string nodeName);
    [Signal] public delegate void ScenarioLoadedEventHandler(string scenarioId, string scenarioName);
    [Signal] public delegate void SustainabilityChangedEventHandler(int score, string level);
    [Signal] public delegate void FinanceTransactionEventHandler(string type, double amount, string description);
    [Signal] public delegate void FarmActionCompletedEventHandler(string action, string siteId, bool success);

    // ── Godot lifecycle ───────────────────────────────────────────────────────

    public override void _Ready()
    {
        SimulationEngine = new SimulationEngine();

        _simulationTimer = new Timer();
        _simulationTimer.WaitTime = 1.0f; // 1 second = 1 simulated day at 1x
        _simulationTimer.Timeout += OnSimulationTick;
        AddChild(_simulationTimer);

        GD.Print("GameBridge: initialized");
    }

    // ── Game management ───────────────────────────────────────────────────────

    /// <summary>Initialize and start a new game.</summary>
    public void StartNewGame(string playerName, int startingCapital = 10_000_000)
    {
        SimulationEngine.FinanceManager = new FinanceManager(startingCapital);
        SimulationEngine.Initialize();
        SimulationEngine.Tutorial.OnStepCompleted += step =>
            EmitSignal(SignalName.TutorialStepCompleted,
                step.StepNumber, step.Title, (double)step.RewardNok);
        SimulationEngine.OnAchievementUnlocked += HandleAchievementUnlocked;
        SimulationEngine.OnResearchCompleted += node =>
            EmitSignal(SignalName.ResearchCompleted, node.Id, node.Name);
        _gameRunning = true;

        EmitSignal(SignalName.GameStarted);
        EmitSignal(SignalName.GameStateUpdated);

        if (!_isPaused)
            _simulationTimer.Start();

        GD.Print($"GameBridge: new game for '{playerName}', capital {startingCapital:N0} NOK");
    }

    /// <summary>Restore a saved game state.</summary>
    public void LoadGame(GameState snapshot)
    {
        SimulationEngine.FinanceManager = new FinanceManager(snapshot.Balance);
        SimulationEngine.Initialize();

        // Wire tutorial signal (same as StartNewGame — must be done after Initialize())
        SimulationEngine.Tutorial.OnStepCompleted += step =>
            EmitSignal(SignalName.TutorialStepCompleted,
                step.StepNumber, step.Title, (double)step.RewardNok);
        SimulationEngine.OnAchievementUnlocked += HandleAchievementUnlocked;
        SimulationEngine.OnResearchCompleted += node =>
            EmitSignal(SignalName.ResearchCompleted, node.Id, node.Name);

        // Restore day counter and persistent collections
        SimulationEngine.State.Day = snapshot.Day;
        foreach (var zone in snapshot.Zones)
            SimulationEngine.State.Zones.Add(zone);
        foreach (var site in snapshot.SeaSites)
            SimulationEngine.State.SeaSites.Add(site);
        foreach (var pop in snapshot.Populations)
            SimulationEngine.State.Populations.Add(pop);
        foreach (var evt in snapshot.EventLog)
            SimulationEngine.State.EventLog.Add(evt);

        _gameRunning = true;
        EmitSignal(SignalName.GameStarted);
        EmitSignal(SignalName.GameStateUpdated);

        if (!_isPaused)
            _simulationTimer.Start();

        GD.Print($"GameBridge: game loaded at day {snapshot.Day}");
    }

    // ── Time control ──────────────────────────────────────────────────────────

    /// <summary>Set auto-tick speed (1x = 1 sec/day, 7x = ~143 ms/day, etc.).</summary>
    public void SetTimeSpeed(float speed)
    {
        _timeSpeed = Mathf.Max(0.1f, speed);
        _simulationTimer.WaitTime = 1.0f / _timeSpeed;
        GD.Print($"GameBridge: time speed → {_timeSpeed}x");
    }

    /// <summary>Pause or resume the auto-tick timer.</summary>
    public void SetPaused(bool paused)
    {
        _isPaused = paused;
        if (_isPaused)
            _simulationTimer.Stop();
        else if (_gameRunning)
            _simulationTimer.Start();

        GD.Print($"GameBridge: {(_isPaused ? "paused" : "resumed")}");
    }

    /// <summary>Manually advance simulation by exactly one day.</summary>
    public void AdvanceDay() => AdvanceDays(1);

    /// <summary>Manually advance simulation by N days (for skip buttons).</summary>
    public void AdvanceDays(int days)
    {
        if (!_gameRunning || SimulationEngine == null) return;

        try
        {
            SimulationEngine.Advance(days);
            EmitSignal(SignalName.SimulationTicked, SimulationEngine.State.Day);
            EmitSignal(SignalName.GameStateUpdated);
            CheckGameOverConditions();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"GameBridge.AdvanceDays error: {ex.Message}");
        }
    }

    // ── Computed properties for GDScript ─────────────────────────────────────
    // GDScript can read these directly via the autoload singleton.

    /// <summary>Current simulation day.</summary>
    public int CurrentDay => SimulationEngine?.State?.Day ?? 0;

    /// <summary>Current balance in NOK.</summary>
    public double Balance => (double)(SimulationEngine?.FinanceManager?.Balance ?? 0m);

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

    /// <summary>Number of export markets unlocked.</summary>
    public int ExportMarketCount => SimulationEngine?.State?.ExportMarkets?.Count ?? 0;

    /// <summary>Total processing throughput from snapshots (kg).</summary>
    public double ProcessingThroughputKg
    {
        get
        {
            var snapshots = SimulationEngine?.State?.ProcessingSnapshots;
            if (snapshots == null || snapshots.Count == 0) return 0.0;
            return (double)snapshots.Sum(s => s.InputKg);
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
    public double SeaLicePerFish => SimulationEngine?.SeaLice?.AverageLicePerFish ?? 0.0;

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
                ["name"]           = evt.Name,
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

    /// <summary>Current tutorial step number (1–12). Returns 12 when complete.</summary>
    public int TutorialStepNumber => SimulationEngine?.Tutorial?.CurrentStepNumber ?? 1;

    /// <summary>Title of the current tutorial step.</summary>
    public string TutorialStepTitle => SimulationEngine?.Tutorial?.CurrentStep?.Title ?? "";

    /// <summary>Description/objective of the current tutorial step.</summary>
    public string TutorialDescription => SimulationEngine?.Tutorial?.CurrentStep?.Description ?? "";

    /// <summary>Contextual hint for the current tutorial step.</summary>
    public string TutorialHint => SimulationEngine?.Tutorial?.CurrentStep?.Hint ?? "";

    /// <summary>True after all 12 tutorial steps are completed.</summary>
    public bool TutorialIsComplete => SimulationEngine?.Tutorial?.IsComplete ?? false;

    /// <summary>Number of completed tutorial steps (0–12).</summary>
    public int TutorialCompletedCount => SimulationEngine?.Tutorial?.CompletedCount ?? 0;

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
    ///       "tier" (string), "reward_nok" (double), "unlocked" (bool).
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
                ["reward_nok"]  = (double)a.RewardNOK,
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

    // ── Internal ──────────────────────────────────────────────────────────────

    private void OnSimulationTick()
    {
        if (!_gameRunning || SimulationEngine == null) return;

        try
        {
            SimulationEngine.Advance(1);
            EmitSignal(SignalName.SimulationTicked, SimulationEngine.State.Day);
            EmitSignal(SignalName.GameStateUpdated);

            // Fire audio alert signal if a new alert was queued this tick, then clear it
            var alertKey = SimulationEngine.Audio?.ActiveAlert ?? "";
            if (!string.IsNullOrEmpty(alertKey))
            {
                EmitSignal(SignalName.AudioAlertTriggered, alertKey);
                SimulationEngine.Audio?.ClearAlert();
            }

            CheckGameOverConditions();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"GameBridge tick error: {ex.Message}");
            EndGame($"Simulation error: {ex.Message}");
        }
    }

    private void HandleAchievementUnlocked(LakskongenSim.Core.Achievement achievement)
    {
        _latestAchievementName = achievement.Name;
        _latestAchievementDescription = achievement.Description;
        EmitSignal(SignalName.AchievementUnlocked,
            achievement.Name,
            achievement.Description,
            (double)achievement.RewardNOK,
            achievement.Tier.ToString());
        GD.Print($"GameBridge: achievement unlocked — {achievement.Name}");
    }

    private void CheckGameOverConditions()
    {
        if (Balance < -1_000_000.0)
            EndGame("Bankruptcy — debt exceeded 1 000 000 NOK!");
    }

    private void EndGame(string reason)
    {
        _gameRunning = false;
        _simulationTimer.Stop();
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
        var engine   = SimulationEngine;
        var research = engine?.Research;
        if (research == null) return false;

        if (!research.Nodes.TryGetValue(nodeId, out var node)) return false;
        if (!research.CanResearch(nodeId)) return false;

        // Balance check
        if (engine!.FinanceManager.Balance < node.CostNOK)
        {
            GD.Print($"GameBridge.StartResearch: insufficient funds for '{nodeId}' (need {node.CostNOK:N0} NOK)");
            return false;
        }

        engine.FinanceManager.Spend(node.CostNOK);
        research.StartResearch(nodeId);
        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: research started — {node.Name} ({node.ResearchDays}d, {node.CostNOK:N0} NOK)");
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

    // ── Scenario System API for GDScript ─────────────────────────────────────

    /// <summary>
    /// Return all available scenarios as an Array of Dictionaries.
    /// Keys: "id" (string), "name" (string), "description" (string),
    ///       "difficulty" (string), "region" (string), "starting_capital" (double).
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetAvailableScenarios()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var scenarios = SimulationEngine?.ScenarioSystem?.GetAvailableScenarios();
        if (scenarios == null) return result;

        foreach (var scenario in scenarios)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"]               = scenario.Id,
                ["name"]             = scenario.Name,
                ["description"]      = scenario.Description,
                ["difficulty"]       = scenario.Difficulty.ToString(),
                ["region"]           = scenario.Region,
                ["starting_capital"] = (double)scenario.StartingCapital
            });
        }
        return result;
    }

    /// <summary>
    /// Load and start a scenario by ID.
    /// Returns true on success, false if scenario not found.
    /// </summary>
    public bool StartScenario(string scenarioId)
    {
        if (SimulationEngine?.ScenarioSystem == null) return false;

        var scenario = SimulationEngine.ScenarioSystem.LoadScenario(scenarioId);
        if (scenario == null)
        {
            GD.PrintErr($"GameBridge.StartScenario: scenario '{scenarioId}' not found");
            return false;
        }

        // Initialize game with scenario parameters
        StartNewGame(scenario.Name, (int)scenario.StartingCapital);
        
        EmitSignal(SignalName.ScenarioLoaded, scenarioId, scenario.Name);
        GD.Print($"GameBridge: scenario loaded — {scenario.Name}");
        return true;
    }

    /// <summary>
    /// Return the ID of the currently active scenario.
    /// Returns empty string if no scenario is loaded.
    /// </summary>
    public string GetActiveScenarioId()
        => SimulationEngine?.ScenarioSystem?.ActiveScenarioId ?? "";

    // ── Sustainability API (expanded) ────────────────────────────────────────

    /// <summary>
    /// Return detailed sustainability breakdown as a Dictionary.
    /// Keys: "sea_lice" (double), "escape" (double), "feed" (double),
    ///       "carbon" (double), "welfare" (double), "ecosystem" (double).
    /// </summary>
    public Godot.Collections.Dictionary GetSustainabilityBreakdown()
    {
        var result = new Godot.Collections.Dictionary();
        var manager = SimulationEngine?.SustainabilityManager;
        if (manager == null)
        {
            result["sea_lice"] = 0.0;
            result["escape"] = 0.0;
            result["feed"] = 0.0;
            result["carbon"] = 0.0;
            result["welfare"] = 0.0;
            result["ecosystem"] = 0.0;
            return result;
        }

        var breakdown = manager.GetBreakdown();
        result["sea_lice"] = breakdown.SeaLiceScore;
        result["escape"] = breakdown.EscapeScore;
        result["feed"] = breakdown.FeedScore;
        result["carbon"] = breakdown.CarbonScore;
        result["welfare"] = breakdown.WelfareScore;
        result["ecosystem"] = breakdown.EcosystemScore;
        return result;
    }

    /// <summary>
    /// Return the current certification level as a string.
    /// Possible values: "NonCompliant", "Compliant", "ASCCertified", "OrganicCertified".
    /// </summary>
    public string GetCertificationLevel()
        => SimulationEngine?.SustainabilityManager?.CurrentCertification.ToString() ?? "NonCompliant";

    /// <summary>
    /// Return the current price multiplier from certification (e.g. 1.0 = no bonus, 1.15 = +15%).
    /// </summary>
    public double GetPriceMultiplier()
        => SimulationEngine?.SustainabilityManager?.GetPriceMultiplier() ?? 1.0;

    // ── Finance API ───────────────────────────────────────────────────────────

    /// <summary>Total revenue accumulated this period (read-only).</summary>
    public double Revenue => (double)(SimulationEngine?.FinanceManager?.Revenue ?? 0m);

    /// <summary>Total expenses accumulated this period (read-only).</summary>
    public double Expenses => (double)(SimulationEngine?.FinanceManager?.Expenses ?? 0m);

    /// <summary>Current profit (Revenue - Expenses).</summary>
    public double Profit => Revenue - Expenses;

    /// <summary>
    /// Return financial history for the last N days as an Array of Dictionaries.
    /// Keys: "day" (int), "revenue" (double), "expenses" (double), "balance" (double).
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetFinancialHistory(int days)
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var finance = SimulationEngine?.FinanceManager;
        if (finance == null) return result;

        var history = finance.GetHistory(days);
        foreach (var entry in history)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["day"]      = entry.Day,
                ["revenue"]  = (double)entry.Revenue,
                ["expenses"] = (double)entry.Expenses,
                ["balance"]  = (double)entry.Balance
            });
        }
        return result;
    }

    /// <summary>
    /// Return loan information as a Dictionary.
    /// Keys: "has_loan" (bool), "amount" (double), "interest_rate" (double), "remaining" (double).
    /// </summary>
    public Godot.Collections.Dictionary GetLoanInfo()
    {
        var result = new Godot.Collections.Dictionary();
        var finance = SimulationEngine?.FinanceManager;
        if (finance == null)
        {
            result["has_loan"] = false;
            result["amount"] = 0.0;
            result["interest_rate"] = 0.0;
            result["remaining"] = 0.0;
            return result;
        }

        var loanInfo = finance.GetLoanInfo();
        result["has_loan"] = loanInfo.HasLoan;
        result["amount"] = (double)loanInfo.Amount;
        result["interest_rate"] = loanInfo.InterestRate;
        result["remaining"] = (double)loanInfo.Remaining;
        return result;
    }

    /// <summary>
    /// Take out a loan for the specified amount.
    /// Returns true on success, false if already have a loan or amount is invalid.
    /// </summary>
    public bool TakeLoan(double amount)
    {
        if (SimulationEngine?.FinanceManager == null) return false;
        
        bool success = SimulationEngine.FinanceManager.TakeLoan((decimal)amount);
        if (success)
        {
            EmitSignal(SignalName.FinanceTransaction, "loan_taken", amount, $"Loan taken: {amount:N0} NOK");
            EmitSignal(SignalName.GameStateUpdated);
            GD.Print($"GameBridge: loan taken — {amount:N0} NOK");
        }
        return success;
    }

    /// <summary>
    /// Repay a portion of the active loan.
    /// Returns true on success, false if no loan exists or insufficient funds.
    /// </summary>
    public bool RepayLoan(double amount)
    {
        if (SimulationEngine?.FinanceManager == null) return false;
        
        bool success = SimulationEngine.FinanceManager.RepayLoan((decimal)amount);
        if (success)
        {
            EmitSignal(SignalName.FinanceTransaction, "loan_repaid", amount, $"Loan repayment: {amount:N0} NOK");
            EmitSignal(SignalName.GameStateUpdated);
            GD.Print($"GameBridge: loan repaid — {amount:N0} NOK");
        }
        return success;
    }

    // ── Farm Management API ───────────────────────────────────────────────────

    /// <summary>
    /// Stock smolts at a specific sea site.
    /// Returns true on success, false if site not found or stocking failed.
    /// </summary>
    public bool StockSmolts(string siteId, int count)
    {
        if (SimulationEngine?.Hatchery == null) return false;

        bool success = SimulationEngine.Hatchery.StockSmolts(siteId, count);
        if (success)
        {
            EmitSignal(SignalName.FarmActionCompleted, "stock_smolts", siteId, true);
            EmitSignal(SignalName.GameStateUpdated);
            GD.Print($"GameBridge: stocked {count} smolts at site '{siteId}'");
        }
        else
        {
            EmitSignal(SignalName.FarmActionCompleted, "stock_smolts", siteId, false);
        }
        return success;
    }

    /// <summary>
    /// Harvest all fish at a specific sea site.
    /// Returns the total biomass harvested in kg, or 0.0 if site not found or empty.
    /// </summary>
    public double HarvestSite(string siteId)
    {
        if (SimulationEngine == null) return 0.0;

        double biomassHarvested = SimulationEngine.HarvestSite(siteId);
        if (biomassHarvested > 0.0)
        {
            EmitSignal(SignalName.FarmActionCompleted, "harvest", siteId, true);
            EmitSignal(SignalName.GameStateUpdated);
            GD.Print($"GameBridge: harvested {biomassHarvested:N0} kg from site '{siteId}'");
        }
        else
        {
            EmitSignal(SignalName.FarmActionCompleted, "harvest", siteId, false);
        }
        return biomassHarvested;
    }

    /// <summary>
    /// Build a new sea site in the specified zone.
    /// Returns true on success, false if zone not found or insufficient funds.
    /// </summary>
    public bool BuildSeaSite(string zoneId, string name)
    {
        if (SimulationEngine == null) return false;

        bool success = SimulationEngine.BuildSeaSite(zoneId, name);
        if (success)
        {
            EmitSignal(SignalName.FarmActionCompleted, "build_site", zoneId, true);
            EmitSignal(SignalName.GameStateUpdated);
            GD.Print($"GameBridge: built sea site '{name}' in zone '{zoneId}'");
        }
        else
        {
            EmitSignal(SignalName.FarmActionCompleted, "build_site", zoneId, false);
        }
        return success;
    }

    /// <summary>
    /// Return detailed information about all sea sites as an Array of Dictionaries.
    /// Keys: "id" (string), "name" (string), "zone" (string), "fish_count" (int),
    ///       "biomass_kg" (double), "max_biomass" (double), "escapes_this_year" (int).
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetSeaSiteDetails()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var sites = SimulationEngine?.State?.SeaSites;
        var populations = SimulationEngine?.State?.Populations;
        if (sites == null || populations == null) return result;

        foreach (var site in sites)
        {
            var population = populations.FirstOrDefault(p => p.SiteId == site.Id);
            int fishCount = population?.Cohorts.Sum(c => c.Count) ?? 0;
            double biomassKg = population?.Cohorts.Sum(c => c.Count * (double)c.MeanWeightKg) ?? 0.0;

            result.Add(new Godot.Collections.Dictionary
            {
                ["id"]                = site.Id,
                ["name"]              = site.Name,
                ["zone"]              = site.ZoneId,
                ["fish_count"]        = fishCount,
                ["biomass_kg"]        = biomassKg,
                ["max_biomass"]       = site.MaxBiomassKg,
                ["escapes_this_year"] = site.EscapesThisYear
            });
        }
        return result;
    }

    /// <summary>
    /// Order a delousing treatment for a specific sea site using the specified method.
    /// Valid methods: "mechanical", "thermal", "chemical", "biological".
    /// Returns true on success, false if site not found, method invalid, or insufficient funds.
    /// </summary>
    public bool OrderDelousing(string siteId, string method)
    {
        if (SimulationEngine?.SeaLice == null) return false;

        bool success = SimulationEngine.SeaLice.OrderDelousing(siteId, method);
        if (success)
        {
            EmitSignal(SignalName.FarmActionCompleted, "delousing", siteId, true);
            EmitSignal(SignalName.GameStateUpdated);
            GD.Print($"GameBridge: ordered {method} delousing for site '{siteId}'");
        }
        else
        {
            EmitSignal(SignalName.FarmActionCompleted, "delousing", siteId, false);
        }
        return success;
    }

    // ── Genetics API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns list of broodstock genomes with their genetic traits.
    /// Each entry is a Dictionary with keys: "id", "generation", "traits" (Dictionary of trait values),
    /// "has_mutation", "summary".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetBreedingCandidates()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var state = SimulationEngine?.State;
        if (state == null) return result;

        int index = 0;
        foreach (var genome in state.Broodstock)
        {
            var traits = new Godot.Collections.Dictionary();
            foreach (var kv in genome.TraitValues)
                traits[kv.Key.ToString()] = kv.Value;

            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = index.ToString(),
                ["generation"] = genome.Generation,
                ["traits"] = traits,
                ["has_mutation"] = genome.HasMutation,
                ["summary"] = genome.GetSummary()
            });
            index++;
        }
        return result;
    }

    /// <summary>
    /// Returns trait definitions (name, heritability, current value, breeding value).
    /// For each trait, returns a Dictionary with keys: "name", "heritability", "current_value", "breeding_value".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetGeneticTraits()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var genetics = SimulationEngine?.Genetics;
        if (genetics == null) return result;

        foreach (GeneticTrait trait in Enum.GetValues(typeof(GeneticTrait)))
        {
            double heritability = Genetics.GetHeritability(trait);
            // For now, we'll return placeholder values; later we can compute current population average.
            double currentValue = 0.0; // placeholder
            double breedingValue = 0.0; // placeholder
            result.Add(new Godot.Collections.Dictionary
            {
                ["name"] = trait.ToString(),
                ["heritability"] = heritability,
                ["current_value"] = currentValue,
                ["breeding_value"] = breedingValue
            });
        }
        return result;
    }

    /// <summary>
    /// Returns current inbreeding depression level (coefficient between 0 and 1).
    /// Simplified: count of shared genomes in broodstock.
    /// </summary>
    public double GetInbreedingCoefficient()
    {
        var broodstock = SimulationEngine?.State?.Broodstock;
        if (broodstock == null || broodstock.Count < 2) return 0.0;

        // Dummy implementation: compute average relatedness based on generation difference.
        // For now, return a placeholder.
        return 0.05;
    }

    // ── Worker/Crew API ───────────────────────────────────────────────────────

    /// <summary>
    /// Return the crew roster as an Array of Dictionaries.
    /// Keys: "id" (string), "name" (string), "role" (string), "salary_nok" (double),
    ///       "skill_level" (int), "hire_date" (int).
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetCrewList()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var crew = SimulationEngine?.State?.Crew;
        if (crew == null) return result;

        foreach (var worker in crew)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"]         = worker.Id,
                ["name"]       = worker.Name,
                ["role"]       = worker.Role.ToString(),
                ["salary_nok"] = (double)worker.SalaryNOK,
                ["skill_level"] = worker.SkillLevel,
                ["hire_date"]  = worker.HireDate
            });
        }
        return result;
    }

    /// <summary>
    /// Hire a new worker with the specified role.
    /// Valid roles: "manager", "technician", "veterinarian", "diver", "feeder".
    /// Returns true on success, false if role invalid or insufficient funds.
    /// </summary>
    public bool HireWorker(string role)
    {
        if (SimulationEngine == null) return false;

        bool success = SimulationEngine.HireWorker(role);
        if (success)
        {
            EmitSignal(SignalName.FinanceTransaction, "hire_worker", 0.0, $"Hired {role}");
            EmitSignal(SignalName.GameStateUpdated);
            GD.Print($"GameBridge: hired worker — {role}");
        }
        return success;
    }

    /// <summary>
    /// Fire a worker by ID.
    /// Returns true on success, false if worker not found.
    /// </summary>
    public bool FireWorker(string workerId)
    {
        if (SimulationEngine == null) return false;

        bool success = SimulationEngine.FireWorker(workerId);
        if (success)
        {
            EmitSignal(SignalName.FinanceTransaction, "fire_worker", 0.0, $"Fired worker {workerId}");
            EmitSignal(SignalName.GameStateUpdated);
            GD.Print($"GameBridge: fired worker — {workerId}");
        }
        return success;
    }

    public override void _ExitTree()
    {
        _simulationTimer?.Stop();
        _simulationTimer?.QueueFree();
    }
}
