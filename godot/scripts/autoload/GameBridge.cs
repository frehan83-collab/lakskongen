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
    [Signal] public delegate void AchievementUnlockedEventHandler(string id, string name, string description, double rewardNok, string tier);

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
    [Signal] public delegate void WeatherEventEventHandler(string eventType, double severity, string description);
    [Signal] public delegate void DiseaseOutbreakEventHandler(string diseaseType, string siteId, double severity, double treatmentCost);

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

        // CRITICAL: Re-inject dependencies into deserialized populations
        // Fixes P1-03 (Save/Load NRE Gap) - populations need GrowthModel/MortalityModel after deserialization
        SimulationEngine.RestoreFromLoad(snapshot);

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

    public string GetCurrentWeatherState() => SimulationEngine?.Weather?.CurrentState.ToString() ?? "Calm";
    public double GetCurrentTemperature() => SimulationEngine?.Weather?.CurrentTemperatureC ?? 0.0;
    public int GetCurrentDay() => SimulationEngine?.State?.Day ?? 0;

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

            // Check for weather events and apply impacts
            CheckWeatherImpacts();

            // Check for disease outbreaks
            CheckDiseaseOutbreaks();

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
            achievement.Id,
            achievement.Name,
            achievement.Description,
            (double)achievement.RewardNOK,
            achievement.Tier.ToString());
        GD.Print($"GameBridge: achievement unlocked — {achievement.Name}");
    }

    // Track last weather check to avoid spam
    private LakskongenSim.Environment.WeatherState _lastWeatherState = LakskongenSim.Environment.WeatherState.Calm;
    private int _lastDiseaseCheckDay = 0;

    private void CheckWeatherImpacts()
    {
        if (SimulationEngine?.Weather == null || SimulationEngine?.State?.SeaSites == null) return;

        var currentWeather = SimulationEngine.Weather.CurrentState;
        
        // Only trigger events when weather changes to severe states
        if (currentWeather != _lastWeatherState && (currentWeather == LakskongenSim.Environment.WeatherState.Storm || currentWeather == LakskongenSim.Environment.WeatherState.Severe))
        {
            double severity = currentWeather == LakskongenSim.Environment.WeatherState.Severe ? 1.0 : 0.5;
            
            // Storm damages pens and increases mortality
            foreach (var site in SimulationEngine.State.SeaSites)
            {
                var populations = SimulationEngine.State.Populations.Where(p => p.ZoneId == site.ZoneId).ToList();
                foreach (var pop in populations)
                {
                    foreach (var cohort in pop.Cohorts)
                    {
                        // Increase mortality due to storm stress (0.5-2% loss depending on severity)
                        int mortalityCount = (int)(cohort.Count * severity * 0.02);
                        if (mortalityCount > 0)
                        {
                            cohort.Count = Math.Max(0, cohort.Count - mortalityCount);
                        }
                    }
                }
            }

            // Calculate financial impact (pen repairs)
            decimal repairCost = (decimal)(SimulationEngine.State.SeaSites.Count * 50000 * severity); // 50k-100k NOK per site
            if (repairCost > 0 && SimulationEngine.FinanceManager.Balance >= repairCost)
            {
                SimulationEngine.FinanceManager.Spend(repairCost);
                SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
                {
                    Day = SimulationEngine.State.Day,
                    Name = "Storm Damage",
                    Category = LakskongenSim.Events.EventCategory.Weather,
                    Description = $"{(currentWeather == LakskongenSim.Environment.WeatherState.Severe ? "Severe storm" : "Storm")} damaged pen infrastructure",
                    BalanceChange = -repairCost
                });
            }

            string weatherDesc = currentWeather == LakskongenSim.Environment.WeatherState.Severe 
                ? "Severe storm causing major infrastructure damage and fish stress" 
                : "Storm causing moderate damage and increased mortality";
            
            EmitSignal(SignalName.WeatherEvent, currentWeather.ToString(), severity, weatherDesc);
            GD.Print($"GameBridge: Weather event — {currentWeather} (severity {severity:F2})");
        }

        // Temperature extremes stress fish
        double temp = SimulationEngine.Weather.CurrentTemperatureC;
        if (temp < 4.0 || temp > 16.0)
        {
            double tempStress = temp < 4.0 ? (4.0 - temp) * 0.01 : (temp - 16.0) * 0.01;
            foreach (var pop in SimulationEngine.State.Populations)
            {
                foreach (var cohort in pop.Cohorts)
                {
                    int tempMortality = (int)(cohort.Count * tempStress * 0.005); // 0.5% per degree deviation
                    if (tempMortality > 0)
                    {
                        cohort.Count = Math.Max(0, cohort.Count - tempMortality);
                    }
                }
            }
        }

        _lastWeatherState = currentWeather;
    }

    private void CheckDiseaseOutbreaks()
    {
        if (SimulationEngine?.SeaLice == null || SimulationEngine?.State?.SeaSites == null) return;
        
        // Check once per week to avoid spam
        if (SimulationEngine.State.Day - _lastDiseaseCheckDay < 7) return;
        _lastDiseaseCheckDay = SimulationEngine.State.Day;

        double licePerFish = SimulationEngine.SeaLice.AverageLicePerFish;
        
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
                ? $"Moderate sea lice levels detected ({licePerFish:F2} per fish). Consider treatment."
                : $"High sea lice levels detected ({licePerFish:F2} per fish)! Immediate treatment recommended.";
            
            EmitSignal(SignalName.DiseaseOutbreak, "SeaLice", "all_sites", severity, (double)treatmentCost);
            
            // Add warning to event log
            SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
            {
                Day = SimulationEngine.State.Day,
                Name = "Sea Lice Warning",
                Category = LakskongenSim.Events.EventCategory.Disease,
                Description = description,
                BalanceChange = 0m
            });
            
            GD.Print($"GameBridge: Disease outbreak — SeaLice (severity {severity:F2}, treatment cost {treatmentCost:N0} NOK)");
        }
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
        // Scenario system not yet implemented
        return new Godot.Collections.Array<Godot.Collections.Dictionary>();
    }

    /// <summary>
    /// Load and start a scenario by ID.
    /// Returns true on success, false if scenario not found.
    /// </summary>
    public bool StartScenario(string scenarioId)
    {
        // Scenario system not yet implemented
        return false;
    }

    /// <summary>
    /// Return the ID of the currently active scenario.
    /// Returns empty string if no scenario is loaded.
    /// </summary>
    public string GetActiveScenarioId()
        => "";

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

        var breakdown = manager.GetComponentScores(SimulationEngine.State);
        result["sea_lice"] = breakdown["SeaLiceManagement"];
        result["escape"] = breakdown["EscapePrevention"];
        result["feed"] = breakdown["FeedSustainability"];
        result["carbon"] = breakdown["CarbonFootprint"];
        result["welfare"] = breakdown["WelfareStandards"];
        result["ecosystem"] = breakdown["EcosystemImpact"];
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
        => (double)(SimulationEngine?.SustainabilityManager?.GetPriceMultiplier(SimulationEngine.SustainabilityManager.CurrentCertification) ?? 1.0m);

    // ── Finance API ───────────────────────────────────────────────────────────

    /// <summary>Total revenue accumulated this period (read-only).</summary>
    public double Revenue => (double)(SimulationEngine?.State?.Balance ?? 0m);

    /// <summary>Total expenses accumulated this period (read-only).</summary>
    public double Expenses => 0.0; // FinanceManager lacks Expenses tracking

    /// <summary>Current profit (Revenue - Expenses).</summary>
    public double Profit => Revenue - Expenses;



    // ── Farm Management API ───────────────────────────────────────────────────

    /// <summary>
    /// Stock smolts at a specific sea site.
    /// Returns true on success, false if site not found or stocking failed.
    /// </summary>
    public bool StockSmolts(string siteId, int count)
    {
        if (SimulationEngine?.State?.SeaSites == null || SimulationEngine?.Hatchery == null)
        {
            GD.PrintErr("GameBridge: StockSmolts failed - engine not initialized");
            EmitSignal(SignalName.FarmActionCompleted, "stock_smolts", siteId, false);
            return false;
        }

        var site = SimulationEngine.State.SeaSites.FirstOrDefault(s => s.Id == siteId);
        if (site == null)
        {
            GD.PrintErr($"GameBridge: StockSmolts failed - site '{siteId}' not found");
            EmitSignal(SignalName.FarmActionCompleted, "stock_smolts", siteId, false);
            return false;
        }

        // Calculate stocking cost (hatchery rearing cost)
        decimal stockingCost = SimulationEngine.Hatchery.CalculateRearingCost(count, SimulationEngine.Hatchery.MaturationDays);
        
        if (SimulationEngine.FinanceManager.Balance < stockingCost)
        {
            GD.PrintErr($"GameBridge: StockSmolts failed - insufficient funds (need {stockingCost:N0} NOK)");
            EmitSignal(SignalName.FarmActionCompleted, "stock_smolts", siteId, false);
            return false;
        }

        // Create a new fish population with smolts
        var population = new LakskongenSim.Biology.FishPopulation(site.ZoneId, SimulationEngine.Growth, SimulationEngine.Mortality);
        var smoltCohort = new LakskongenSim.Biology.FishPopulation.Cohort(
            SimulationEngine.Hatchery.MaturationDays,
            count,
            SimulationEngine.Hatchery.SmoltReleaseWeight,
            LakskongenSim.Data.LifeStage.Smolt
        );
        population.Cohorts.Add(smoltCohort);

        // Stock the site
        site.Stock(population);
        SimulationEngine.State.Populations.Add(population);

        // Charge the player
        SimulationEngine.FinanceManager.Spend(stockingCost);
        
        // Record event
        SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = SimulationEngine.State.Day,
            Name = "Smolt Stocking",
            Category = LakskongenSim.Events.EventCategory.Farm,
            Description = $"Stocked {count:N0} smolts at {siteId}",
            BalanceChange = -stockingCost
        });

        EmitSignal(SignalName.FarmActionCompleted, "stock_smolts", siteId, true);
        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: Stocked {count:N0} smolts at '{siteId}' for {stockingCost:N0} NOK");
        return true;
    }

    /// <summary>
    /// Harvest all fish at a specific sea site.
    /// Returns the total biomass harvested in kg, or 0.0 if site not found or empty.
    /// </summary>
    public double HarvestSite(string siteId)
    {
        if (SimulationEngine?.State?.SeaSites == null)
        {
            GD.PrintErr("GameBridge: HarvestSite failed - engine not initialized");
            EmitSignal(SignalName.FarmActionCompleted, "harvest", siteId, false);
            return 0.0;
        }

        var site = SimulationEngine.State.SeaSites.FirstOrDefault(s => s.Id == siteId);
        if (site == null)
        {
            GD.PrintErr($"GameBridge: HarvestSite failed - site '{siteId}' not found");
            EmitSignal(SignalName.FarmActionCompleted, "harvest", siteId, false);
            return 0.0;
        }

        // Calculate total biomass and count fish
        double totalBiomassKg = 0.0;
        int totalFishCount = 0;
        var populations = SimulationEngine.State.Populations.Where(p => p.ZoneId == site.ZoneId).ToList();

        foreach (var pop in populations)
        {
            foreach (var cohort in pop.Cohorts.ToList()) // ToList() to avoid modification during iteration
            {
                double cohortBiomass = cohort.Count * cohort.MeanWeightKg;
                totalBiomassKg += cohortBiomass;
                totalFishCount += cohort.Count;
            }
        }

        if (totalBiomassKg < 0.1)
        {
            GD.Print($"GameBridge: HarvestSite - site '{siteId}' is empty");
            EmitSignal(SignalName.FarmActionCompleted, "harvest", siteId, false);
            return 0.0;
        }

        // Calculate revenue (market price × biomass × certification multiplier)
        decimal revenue = (decimal)totalBiomassKg * SimulationEngine.MarketSimulation.PricePerKg;
        decimal certMultiplier = SimulationEngine.SustainabilityManager?.GetPriceMultiplier(
            SimulationEngine.SustainabilityManager.CurrentCertification) ?? 1.0m;
        revenue *= certMultiplier;

        // Apply harvest
        SimulationEngine.FinanceManager.Earn(revenue);

        // Remove harvested populations
        foreach (var pop in populations)
        {
            pop.Cohorts.Clear();
            SimulationEngine.State.Populations.Remove(pop);
        }
        site.Pens.Clear();

        // Reset site stocking density
        site.CurrentStockingDensity = 0f;

        // Record event
        SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = SimulationEngine.State.Day,
            Name = "Harvest",
            Category = LakskongenSim.Events.EventCategory.Finance,
            Description = $"Harvested {totalBiomassKg:N0} kg ({totalFishCount:N0} fish) at {siteId}",
            BalanceChange = revenue
        });

        // Trigger achievement check
        SimulationEngine.Audio?.TriggerAlert("harvest");

        EmitSignal(SignalName.FarmActionCompleted, "harvest", siteId, true);
        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: Harvested {totalBiomassKg:N0} kg at '{siteId}' for {revenue:N0} NOK");
        return totalBiomassKg;
    }

    /// <summary>
    /// Build a new sea site in the specified zone.
    /// Returns true on success, false if zone not found or insufficient funds.
    /// </summary>
    public bool BuildSeaSite(string zoneId, string name)
    {
        if (SimulationEngine?.State?.Zones == null || SimulationEngine?.State?.SeaSites == null)
        {
            GD.PrintErr("GameBridge: BuildSeaSite failed - engine not initialized");
            EmitSignal(SignalName.FarmActionCompleted, "build_site", zoneId, false);
            return false;
        }

        // Check if zone exists
        var zone = SimulationEngine.State.Zones.FirstOrDefault(z => z.Id == zoneId);
        if (zone == null)
        {
            GD.PrintErr($"GameBridge: BuildSeaSite failed - zone '{zoneId}' not found");
            EmitSignal(SignalName.FarmActionCompleted, "build_site", zoneId, false);
            return false;
        }

        // License cost: ~30M NOK for 780-tonne license (Norwegian market rate)
        decimal licenseCost = 30_000_000m;
        
        // Check if player can afford it
        if (SimulationEngine.FinanceManager.Balance < licenseCost)
        {
            GD.PrintErr($"GameBridge: BuildSeaSite failed - insufficient funds (need {licenseCost:N0} NOK)");
            EmitSignal(SignalName.FarmActionCompleted, "build_site", zoneId, false);
            return false;
        }

        // Create new sea site
        var siteId = string.IsNullOrWhiteSpace(name) ? $"site_{SimulationEngine.State.SeaSites.Count + 1}" : name;
        var newSite = new LakskongenSim.Farm.SeaSite(siteId, zoneId)
        {
            MABTonnes = 780.0, // Standard Norwegian license
            NumberOfPens = 8,
            PenVolume = 50000f,
            MaxAllowedDensity = 25.0f
        };

        SimulationEngine.State.SeaSites.Add(newSite);

        // Charge the player
        SimulationEngine.FinanceManager.Spend(licenseCost);

        // Record event
        SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = SimulationEngine.State.Day,
            Name = "License Acquisition",
            Category = LakskongenSim.Events.EventCategory.Farm,
            Description = $"Built new sea site '{siteId}' in zone '{zoneId}' (780T MAB)",
            BalanceChange = -licenseCost
        });

        EmitSignal(SignalName.FarmActionCompleted, "build_site", zoneId, true);
        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: Built sea site '{siteId}' in zone '{zoneId}' for {licenseCost:N0} NOK");
        return true;
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
            var population = populations.FirstOrDefault(p => p.ZoneId == site.Id);
            int fishCount = population?.Cohorts.Sum(c => c.Count) ?? 0;
            double biomassKg = population?.Cohorts.Sum(c => c.Count * (double)c.MeanWeightKg) ?? 0.0;

            result.Add(new Godot.Collections.Dictionary
            {
                ["id"]                = site.Id,
                ["name"]              = site.Id,
                ["zone"]              = site.ZoneId,
                ["fish_count"]        = fishCount,
                ["biomass_kg"]        = biomassKg,
                ["max_biomass"]       = site.MABTonnes * 1000,
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
        if (SimulationEngine?.SeaLice == null || SimulationEngine?.State?.SeaSites == null)
        {
            GD.PrintErr("GameBridge: OrderDelousing failed - engine not initialized");
            EmitSignal(SignalName.FarmActionCompleted, "delousing", siteId, false);
            return false;
        }

        // Validate method
        var validMethods = new[] { "mechanical", "thermal", "chemical", "biological" };
        if (!validMethods.Contains(method.ToLower()))
        {
            GD.PrintErr($"GameBridge: OrderDelousing failed - invalid method '{method}'");
            EmitSignal(SignalName.FarmActionCompleted, "delousing", siteId, false);
            return false;
        }

        // If siteId is "all_sites", treat all sites
        var targetSites = siteId == "all_sites" 
            ? SimulationEngine.State.SeaSites.ToList()
            : SimulationEngine.State.SeaSites.Where(s => s.Id == siteId).ToList();

        if (targetSites.Count == 0)
        {
            GD.PrintErr($"GameBridge: OrderDelousing failed - site '{siteId}' not found");
            EmitSignal(SignalName.FarmActionCompleted, "delousing", siteId, false);
            return false;
        }

        // Calculate total fish count for cost
        int totalFish = 0;
        foreach (var site in targetSites)
        {
            var populations = SimulationEngine.State.Populations.Where(p => p.ZoneId == site.ZoneId);
            totalFish += populations.SelectMany(p => p.Cohorts).Sum(c => c.Count);
        }

        // Treatment costs and effectiveness by method
        decimal costPerFish = method.ToLower() switch
        {
            "mechanical" => 50m,   // Most expensive but effective
            "thermal" => 40m,      // Moderate cost and effectiveness
            "chemical" => 30m,     // Cheaper but environmental concerns
            "biological" => 25m,   // Cheapest (cleaner fish)
            _ => 50m
        };

        double effectiveness = method.ToLower() switch
        {
            "mechanical" => 0.85,  // 85% reduction
            "thermal" => 0.75,     // 75% reduction
            "chemical" => 0.70,    // 70% reduction
            "biological" => 0.50,  // 50% reduction (ongoing)
            _ => 0.70
        };

        decimal totalCost = totalFish * costPerFish;

        if (SimulationEngine.FinanceManager.Balance < totalCost)
        {
            GD.PrintErr($"GameBridge: OrderDelousing failed - insufficient funds (need {totalCost:N0} NOK)");
            EmitSignal(SignalName.FarmActionCompleted, "delousing", siteId, false);
            return false;
        }

        // Apply treatment
        SimulationEngine.SeaLice.ApplyTreatment(effectiveness);
        SimulationEngine.FinanceManager.Spend(totalCost);

        // Record event
        SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = SimulationEngine.State.Day,
            Name = "Delousing Treatment",
            Category = LakskongenSim.Events.EventCategory.Disease,
            Description = $"Applied {method} delousing to {(siteId == "all_sites" ? "all sites" : siteId)} ({totalFish:N0} fish treated, {effectiveness:P0} effective)",
            BalanceChange = -totalCost
        });

        EmitSignal(SignalName.FarmActionCompleted, "delousing", siteId, true);
        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: Delousing treatment applied — {method} ({totalFish:N0} fish, {totalCost:N0} NOK, {effectiveness:P0} effective)");
        return true;
    }

    // ── Contract & Market API ──────────────────────────────────────────────────

    /// <summary>
    /// Returns list of active contracts.
    /// Each entry is a Dictionary with keys: "id", "buyer", "type", "price_per_kg", "quantity_kg", "quality", "penalty_pct", "fulfilled_kg", "fulfilled_percent".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetActiveContracts()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var contractSystem = SimulationEngine?.ContractSystem;
        if (contractSystem == null) return result;

        foreach (var contract in contractSystem.ActiveContracts)
        {
            float fulfilledPercent = contract.QuantityKg > 0 ? (float)(contract.FulfilledKg / contract.QuantityKg) * 100f : 0f;
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = contract.Id.ToString(),
                ["buyer"] = contract.Buyer,
                ["type"] = contract.Type.ToString(),
                ["price_per_kg"] = (float)contract.PricePerKg,
                ["quantity_kg"] = (float)contract.QuantityKg,
                ["quality"] = contract.Quality.ToString(),
                ["penalty_pct"] = (float)contract.PenaltyPct,
                ["fulfilled_kg"] = (float)contract.FulfilledKg,
                ["fulfilled_percent"] = fulfilledPercent
            });
        }
        return result;
    }

    /// <summary>
    /// Returns list of available contract offers (generated from market conditions).
    /// Each entry is a Dictionary with keys: "buyer", "type", "price_per_kg", "quantity_kg", "quality", "penalty_pct".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetAvailableOffers()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var contractSystem = SimulationEngine?.ContractSystem;
        if (contractSystem == null) return result;

        // Generate a few sample offers based on market price (simplified)
        // TODO: integrate with market simulation
        var marketPrice = SimulationEngine?.MarketSimulation?.PricePerKg ?? 70m;
        var random = new Random();
        var buyers = new[] { "NorgesGruppen", "Mowi", "Lerøy", "SalMar", "Cermaq", "International Seafood" };
        var types = new[] { "FixedPrice", "IndexLinked", "VolumeBased" };
        for (int i = 0; i < 5; i++)
        {
            var buyer = buyers[random.Next(buyers.Length)];
            var type = types[random.Next(types.Length)];
            var pricePerKg = (float)(marketPrice * (decimal)(0.9 + random.NextDouble() * 0.2)); // ±10%
            var quantityKg = (float)(random.NextDouble() * 50000 + 10000); // 10k-60k kg
            var quality = (float)(random.NextDouble() * 0.3 + 0.7); // 0.7-1.0
            var penaltyPct = (float)(random.NextDouble() * 0.05); // 0-5%
            result.Add(new Godot.Collections.Dictionary
            {
                ["buyer"] = buyer,
                ["type"] = type,
                ["price_per_kg"] = pricePerKg,
                ["quantity_kg"] = quantityKg,
                ["quality"] = quality,
                ["penalty_pct"] = penaltyPct
            });
        }
        return result;
    }

    /// <summary>
    /// Returns list of export markets with status and prices.
    /// Each entry is a Dictionary with keys: "region", "name", "base_price_nok", "premium_multiplier", "player_has_access", "logistics_cost_pct", "tariff_rate".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetExportMarkets()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var marketSystem = SimulationEngine?.InternationalMarketSystem;
        if (marketSystem == null) return result;

        var allMarkets = marketSystem.GetAllMarkets();
        foreach (var market in allMarkets.Values)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["region"] = market.Region.ToString(),
                ["name"] = market.Name,
                ["base_price_nok"] = (float)market.BasePriceNOK,
                ["premium_multiplier"] = (float)market.PremiumMultiplier,
                ["player_has_access"] = market.PlayerHasAccess,
                ["logistics_cost_pct"] = (float)market.LogisticsCostPctOfPrice,
                ["tariff_rate"] = (float)market.TariffRate,
                ["min_reputation_score"] = (float)market.MinReputationScore,
                ["required_certifications"] = new Godot.Collections.Array<string>(market.RequiredCertifications.ToArray())
            });
        }
        return result;
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
        // GameState.Crew not yet implemented
        GD.Print($"GameBridge: GetCrewList not implemented");
        return new Godot.Collections.Array<Godot.Collections.Dictionary>();
    }

    /// <summary>
    /// Hire a new worker with the specified role.
    /// Valid roles: "manager", "technician", "veterinarian", "diver", "feeder".
    /// Returns true on success, false if role invalid or insufficient funds.
    /// </summary>
    public bool HireWorker(string role)
    {
        // SimulationEngine.HireWorker not yet implemented
        GD.Print($"GameBridge: HireWorker not implemented for role '{role}'");
        EmitSignal(SignalName.FinanceTransaction, "hire_worker", 0.0, $"Failed to hire {role}: not implemented");
        return false;
    }

    /// <summary>
    /// Fire a worker by ID.
    /// Returns true on success, false if worker not found.
    /// </summary>
    public bool FireWorker(string workerId)
    {
        // SimulationEngine.FireWorker not yet implemented
        GD.Print($"GameBridge: FireWorker not implemented for workerId '{workerId}'");
        EmitSignal(SignalName.FinanceTransaction, "fire_worker", 0.0, $"Failed to fire worker {workerId}: not implemented");
        return false;
    }

    public override void _ExitTree()
    {
        _simulationTimer?.Stop();
        _simulationTimer?.QueueFree();
    }
}

