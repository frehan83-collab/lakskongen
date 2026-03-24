using Godot;
using LakskongenSim.Core;
using LakskongenSim.Finance;
using LakskongenSim.Biology;
using LakskongenSim.Data;
using LakskongenSim.Integration;
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

    // ── Dashboard Data Provider ──────────────────────────────────────────────────

    private DashboardDataProvider _dashboardDataProvider;

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
        _dashboardDataProvider = new DashboardDataProvider(SimulationEngine);

        _simulationTimer = new Timer();
        _simulationTimer.WaitTime = 1.0f; // 1 second = 1 simulated day at 1x
        _simulationTimer.Timeout += OnSimulationTick;
        AddChild(_simulationTimer);

        GD.Print("GameBridge: initialized with dashboard data provider");
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
        var population = new LakskongenSim.Biology.FishPopulation
        {
            ZoneId = site.ZoneId
        };
        population.InjectDependencies(SimulationEngine.Growth, SimulationEngine.Mortality);
        
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
            Category = LakskongenSim.Events.EventCategory.Equipment,
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
        SimulationEngine.FinanceManager.AddRevenue(revenue);

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
            Category = LakskongenSim.Events.EventCategory.Market,
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
            Category = LakskongenSim.Events.EventCategory.Regulation,
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

    // ── RAS Facility API ──────────────────────────────────────────────────────

    /// <summary>
    /// Return all RAS facilities as an Array of Dictionaries.
    /// Keys: "id", "name", "zone_id", "scale", "capacity_tonnes_per_year", "tank_count",
    ///       "current_water_temp", "oxygen_level", "water_ph", "co2_level".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetRASFacilities()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var facilities = SimulationEngine?.RASFacilities;
        if (facilities == null) return result;

        foreach (var facility in facilities)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = facility.Id,
                ["name"] = facility.Name,
                ["zone_id"] = facility.ZoneId,
                ["scale"] = facility.Scale.ToString(),
                ["capacity_tonnes_per_year"] = facility.CapacityTonnesPerYear,
                ["tank_count"] = facility.TankCount,
                ["current_water_temp"] = facility.CurrentWaterTemp,
                ["oxygen_level"] = facility.OxygenLevel,
                ["water_ph"] = facility.WaterPH,
                ["co2_level"] = facility.CO2Level
            });
        }
        return result;
    }

    /// <summary>
    /// Build a new RAS facility.
    /// Valid scales: "Small", "Medium", "Large", "Mega".
    /// Returns true on success, false if insufficient funds.
    /// </summary>
    public bool BuildRASFacility(string name, string scale)
    {
        if (SimulationEngine?.RASFacilities == null)
        {
            GD.PrintErr("GameBridge: BuildRASFacility failed - engine not initialized");
            EmitSignal(SignalName.FarmActionCompleted, "build_ras", name, false);
            return false;
        }

        if (!Enum.TryParse<LakskongenSim.Farm.RASScale>(scale, true, out var rasScale))
        {
            GD.PrintErr($"GameBridge: BuildRASFacility failed - invalid scale '{scale}'");
            EmitSignal(SignalName.FarmActionCompleted, "build_ras", name, false);
            return false;
        }

        // CAPEX by scale (based on real-world data)
        decimal buildCost = rasScale switch
        {
            LakskongenSim.Farm.RASScale.Small => 500_000_000m,    // 500M NOK (~5,000t)
            LakskongenSim.Farm.RASScale.Medium => 1_000_000_000m, // 1B NOK (~8,000t)
            LakskongenSim.Farm.RASScale.Large => 1_800_000_000m,  // 1.8B NOK (~20,000t)
            _ => 1_000_000_000m
        };

        if (SimulationEngine.FinanceManager.Balance < buildCost)
        {
            GD.PrintErr($"GameBridge: BuildRASFacility failed - insufficient funds (need {buildCost:N0} NOK)");
            EmitSignal(SignalName.FarmActionCompleted, "build_ras", name, false);
            return false;
        }

        var facility = new LakskongenSim.Farm.RASFacility
        {
            Id = $"ras_{SimulationEngine.RASFacilities.Count + 1}",
            Name = name,
            Scale = rasScale,
            CapacityTonnesPerYear = rasScale switch
            {
                LakskongenSim.Farm.RASScale.Small => 5_000,
                LakskongenSim.Farm.RASScale.Medium => 8_000,
                LakskongenSim.Farm.RASScale.Large => 20_000,
                _ => 8_000
            }
        };

        SimulationEngine.RASFacilities.Add(facility);
        SimulationEngine.FinanceManager.Spend(buildCost);

        SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = SimulationEngine.State.Day,
            Name = "RAS Facility Built",
            Category = LakskongenSim.Events.EventCategory.Equipment,
            Description = $"Built RAS facility '{name}' ({scale} scale, {facility.CapacityTonnesPerYear:N0}t capacity)",
            BalanceChange = -buildCost
        });

        EmitSignal(SignalName.FarmActionCompleted, "build_ras", name, true);
        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: Built RAS facility '{name}' ({scale}) for {buildCost:N0} NOK");
        return true;
    }

    /// <summary>
    /// Perform maintenance on a RAS facility (water system cleaning, filter replacement).
    /// Returns maintenance cost in NOK.
    /// </summary>
    public float PerformRASMaintenance(string facilityId)
    {
        if (SimulationEngine?.RASFacilities == null)
        {
            GD.PrintErr("GameBridge: PerformRASMaintenance failed - engine not initialized");
            return 0f;
        }

        var facility = SimulationEngine.RASFacilities.FirstOrDefault(f => f.Id == facilityId);
        if (facility == null)
        {
            GD.PrintErr($"GameBridge: PerformRASMaintenance failed - facility '{facilityId}' not found");
            return 0f;
        }

        // Maintenance cost: ~1M NOK/month for medium facility
        decimal maintenanceCost = facility.Scale switch
        {
            LakskongenSim.Farm.RASScale.Small => 500_000m,
            LakskongenSim.Farm.RASScale.Medium => 1_000_000m,
            LakskongenSim.Farm.RASScale.Large => 2_000_000m,
            _ => 1_000_000m
        };

        if (SimulationEngine.FinanceManager.Balance < maintenanceCost)
        {
            GD.PrintErr($"GameBridge: PerformRASMaintenance failed - insufficient funds (need {maintenanceCost:N0} NOK)");
            return 0f;
        }

        SimulationEngine.FinanceManager.Spend(maintenanceCost);

        // Restore optimal water conditions after maintenance
        facility.CurrentWaterTemp = facility.TargetWaterTemp;
        facility.OxygenLevel = 8.0;
        facility.WaterPH = 7.0;
        facility.CO2Level = 8.0;

        SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = SimulationEngine.State.Day,
            Name = "RAS Maintenance",
            Category = LakskongenSim.Events.EventCategory.Equipment,
            Description = $"Performed maintenance on RAS '{facility.Name}' (water system restored)",
            BalanceChange = -maintenanceCost
        });

        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: RAS maintenance '{facilityId}' completed for {maintenanceCost:N0} NOK");
        return (float)maintenanceCost;
    }

    // ── Offshore Farm API ─────────────────────────────────────────────────────

    /// <summary>
    /// Return all offshore farms as an Array of Dictionaries.
    /// Keys: "id", "name", "zone_id", "farm_type", "capacity_tonnes", "pen_count",
    ///       "sea_lice_pressure", "water_flow_rate", "fcr".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetOffshoreFarms()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var farms = SimulationEngine?.OffshoreFarms;
        if (farms == null) return result;

        foreach (var farm in farms)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = farm.Id,
                ["name"] = farm.Name,
                ["zone_id"] = farm.ZoneId,
                ["farm_type"] = farm.FarmType.ToString(),
                ["capacity_tonnes"] = farm.CapacityTonnes,
                ["pen_count"] = farm.PenCount,
                ["sea_lice_pressure"] = farm.SeaLicePressure,
                ["water_flow_rate"] = farm.WaterFlowRate,
                ["fcr"] = farm.FCR
            });
        }
        return result;
    }

    /// <summary>
    /// Build a new offshore farm.
    /// Valid farm types: "SemiSubmersible", "Ship", "Submerged".
    /// Returns true on success, false if insufficient funds.
    /// </summary>
    public bool BuildOffshoreFarm(string name, string farmType)
    {
        if (SimulationEngine?.OffshoreFarms == null)
        {
            GD.PrintErr("GameBridge: BuildOffshoreFarm failed - engine not initialized");
            EmitSignal(SignalName.FarmActionCompleted, "build_offshore", name, false);
            return false;
        }

        if (!Enum.TryParse<LakskongenSim.Farm.OffshoreType>(farmType, true, out var offshoreType))
        {
            GD.PrintErr($"GameBridge: BuildOffshoreFarm failed - invalid type '{farmType}'");
            EmitSignal(SignalName.FarmActionCompleted, "build_offshore", name, false);
            return false;
        }

        // CAPEX by type (based on real-world offshore structures)
        decimal buildCost = offshoreType switch
        {
            LakskongenSim.Farm.OffshoreType.SemiSubmersible => 800_000_000m,  // 800M NOK (Ocean Farm 1)
            LakskongenSim.Farm.OffshoreType.ShipShaped => 1_000_000_000m,     // 1B NOK (Havfarm concept)
            LakskongenSim.Farm.OffshoreType.Submersible => 1_200_000_000m,    // 1.2B NOK (Egg submersible)
            _ => 800_000_000m
        };

        if (SimulationEngine.FinanceManager.Balance < buildCost)
        {
            GD.PrintErr($"GameBridge: BuildOffshoreFarm failed - insufficient funds (need {buildCost:N0} NOK)");
            EmitSignal(SignalName.FarmActionCompleted, "build_offshore", name, false);
            return false;
        }

        var farm = new LakskongenSim.Farm.OffshoreFarm
        {
            Id = $"offshore_{SimulationEngine.OffshoreFarms.Count + 1}",
            Name = name,
            FarmType = offshoreType,
            CapacityTonnes = 8_000
        };

        SimulationEngine.OffshoreFarms.Add(farm);
        SimulationEngine.FinanceManager.Spend(buildCost);

        SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = SimulationEngine.State.Day,
            Name = "Offshore Farm Built",
            Category = LakskongenSim.Events.EventCategory.Equipment,
            Description = $"Built offshore farm '{name}' ({farmType} type, {farm.CapacityTonnes:N0}t capacity)",
            BalanceChange = -buildCost
        });

        EmitSignal(SignalName.FarmActionCompleted, "build_offshore", name, true);
        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: Built offshore farm '{name}' ({farmType}) for {buildCost:N0} NOK");
        return true;
    }

    /// <summary>
    /// Repair offshore farm damage (storm damage, equipment failure).
    /// Returns repair cost in NOK.
    /// </summary>
    public float RepairOffshore(string farmId)
    {
        if (SimulationEngine?.OffshoreFarms == null)
        {
            GD.PrintErr("GameBridge: RepairOffshore failed - engine not initialized");
            return 0f;
        }

        var farm = SimulationEngine.OffshoreFarms.FirstOrDefault(f => f.Id == farmId);
        if (farm == null)
        {
            GD.PrintErr($"GameBridge: RepairOffshore failed - farm '{farmId}' not found");
            return 0f;
        }

        // Repair cost: major offshore repairs are expensive
        decimal repairCost = 50_000_000m; // 50M NOK for major repair

        if (SimulationEngine.FinanceManager.Balance < repairCost)
        {
            GD.PrintErr($"GameBridge: RepairOffshore failed - insufficient funds (need {repairCost:N0} NOK)");
            return 0f;
        }

        SimulationEngine.FinanceManager.Spend(repairCost);

        SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = SimulationEngine.State.Day,
            Name = "Offshore Repair",
            Category = LakskongenSim.Events.EventCategory.Equipment,
            Description = $"Repaired offshore farm '{farm.Name}' (hull integrity and equipment restored)",
            BalanceChange = -repairCost
        });

        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: Offshore repair '{farmId}' completed for {repairCost:N0} NOK");
        return (float)repairCost;
    }

    // ── AI Feeding System API ─────────────────────────────────────────────────

    /// <summary>
    /// Return AI feeding system status as a Dictionary.
    /// Keys: "is_installed", "calibration_level", "days_since_installation",
    ///       "camera_count", "camera_uptime", "appetite_score", "feed_waste_rate",
    ///       "optimal_pellet_size_mm", "feed_rate_kg_per_min".
    /// </summary>
    public Godot.Collections.Dictionary GetAIFeedingStatus()
    {
        var result = new Godot.Collections.Dictionary();
        var aiFeeding = SimulationEngine?.AIFeeding;
        if (aiFeeding == null)
        {
            result["is_installed"] = false;
            return result;
        }

        result["is_installed"] = aiFeeding.IsInstalled;
        result["calibration_level"] = aiFeeding.CalibrationLevel;
        result["days_since_installation"] = aiFeeding.DaysSinceInstallation;
        result["camera_count"] = aiFeeding.CameraCount;
        result["camera_uptime"] = aiFeeding.CameraUptime;
        result["appetite_score"] = aiFeeding.AppetiteScore;
        result["feed_waste_rate"] = aiFeeding.FeedWasteRate;
        result["optimal_pellet_size_mm"] = aiFeeding.OptimalPelletSizeMm;
        result["feed_rate_kg_per_min"] = aiFeeding.FeedRateKgPerMin;
        return result;
    }

    /// <summary>
    /// Install AI feeding system across all sites.
    /// Returns installation cost in NOK.
    /// </summary>
    public float InstallAIFeeding()
    {
        if (SimulationEngine?.AIFeeding == null)
        {
            GD.PrintErr("GameBridge: InstallAIFeeding failed - engine not initialized");
            return 0f;
        }

        if (SimulationEngine.AIFeeding.IsInstalled)
        {
            GD.PrintErr("GameBridge: InstallAIFeeding failed - AI feeding already installed");
            return 0f;
        }

        // Installation cost: ~100M NOK for farm-wide AI feeding infrastructure
        decimal installCost = 100_000_000m;

        if (SimulationEngine.FinanceManager.Balance < installCost)
        {
            GD.PrintErr($"GameBridge: InstallAIFeeding failed - insufficient funds (need {installCost:N0} NOK)");
            return 0f;
        }

        SimulationEngine.FinanceManager.Spend(installCost);
        SimulationEngine.AIFeeding.IsInstalled = true;
        SimulationEngine.AIFeeding.DaysSinceInstallation = 0;
        SimulationEngine.AIFeeding.CalibrationLevel = 0.5; // Starts at 50%, improves over time

        SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = SimulationEngine.State.Day,
            Name = "AI Feeding Installed",
            Category = LakskongenSim.Events.EventCategory.Equipment,
            Description = "Installed AI-driven precision feeding system (cameras and automation)",
            BalanceChange = -installCost
        });

        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: AI feeding system installed for {installCost:N0} NOK");
        return (float)installCost;
    }

    /// <summary>
    /// Clean AI feeding cameras (biofouling reduces camera uptime over time).
    /// Returns cleaning cost in NOK.
    /// </summary>
    public float CleanAICameras()
    {
        if (SimulationEngine?.AIFeeding == null || !SimulationEngine.AIFeeding.IsInstalled)
        {
            GD.PrintErr("GameBridge: CleanAICameras failed - AI feeding not installed");
            return 0f;
        }

        // Camera cleaning cost
        decimal cleaningCost = 500_000m; // 500k NOK for diver cleaning/maintenance

        if (SimulationEngine.FinanceManager.Balance < cleaningCost)
        {
            GD.PrintErr($"GameBridge: CleanAICameras failed - insufficient funds (need {cleaningCost:N0} NOK)");
            return 0f;
        }

        SimulationEngine.FinanceManager.Spend(cleaningCost);
        SimulationEngine.AIFeeding.CameraUptime = 1.0; // Restore full camera uptime

        SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = SimulationEngine.State.Day,
            Name = "Camera Cleaning",
            Category = LakskongenSim.Events.EventCategory.Equipment,
            Description = "Cleaned AI feeding cameras (vision restored to 100%)",
            BalanceChange = -cleaningCost
        });

        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: AI camera cleaning completed for {cleaningCost:N0} NOK");
        return (float)cleaningCost;
    }

    // ── Post-Smolt Facility API ───────────────────────────────────────────────

    /// <summary>
    /// Return all post-smolt facilities as an Array of Dictionaries.
    /// Keys: "id", "name", "zone_id", "annual_smolt_capacity", "target_transfer_weight_g",
    ///       "tank_sections", "water_temp", "survival_rate", "fcr".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetPostSmoltFacilities()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var facilities = SimulationEngine?.PostSmoltFacilities;
        if (facilities == null) return result;

        foreach (var facility in facilities)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = facility.Id,
                ["name"] = facility.Name,
                ["zone_id"] = facility.ZoneId,
                ["annual_smolt_capacity"] = facility.AnnualSmoltCapacity,
                ["target_transfer_weight_g"] = facility.TargetTransferWeightG,
                ["tank_sections"] = facility.TankSections,
                ["water_temp"] = facility.WaterTemp,
                ["survival_rate"] = facility.SurvivalRate,
                ["fcr"] = facility.FCR
            });
        }
        return result;
    }

    /// <summary>
    /// Start a new smolt batch in a post-smolt facility.
    /// Returns true on success, false if facility not found or insufficient capacity.
    /// </summary>
    public bool StartSmoltBatch(string facilityId, int count)
    {
        if (SimulationEngine?.PostSmoltFacilities == null)
        {
            GD.PrintErr("GameBridge: StartSmoltBatch failed - engine not initialized");
            EmitSignal(SignalName.FarmActionCompleted, "start_smolt_batch", facilityId, false);
            return false;
        }

        var facility = SimulationEngine.PostSmoltFacilities.FirstOrDefault(f => f.Id == facilityId);
        if (facility == null)
        {
            GD.PrintErr($"GameBridge: StartSmoltBatch failed - facility '{facilityId}' not found");
            EmitSignal(SignalName.FarmActionCompleted, "start_smolt_batch", facilityId, false);
            return false;
        }

        // Cost per smolt (hatchery production cost)
        decimal smoltCost = 15m; // 15 NOK per smolt (industry average)
        decimal totalCost = smoltCost * count;

        if (SimulationEngine.FinanceManager.Balance < totalCost)
        {
            GD.PrintErr($"GameBridge: StartSmoltBatch failed - insufficient funds (need {totalCost:N0} NOK)");
            EmitSignal(SignalName.FarmActionCompleted, "start_smolt_batch", facilityId, false);
            return false;
        }

        SimulationEngine.FinanceManager.Spend(totalCost);

        // Create new batch (simplified - actual batch tracking would be in SmoltBatch class)
        SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = SimulationEngine.State.Day,
            Name = "Smolt Batch Started",
            Category = LakskongenSim.Events.EventCategory.Equipment,
            Description = $"Started batch of {count:N0} smolts in post-smolt facility '{facility.Name}'",
            BalanceChange = -totalCost
        });

        EmitSignal(SignalName.FarmActionCompleted, "start_smolt_batch", facilityId, true);
        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: Started smolt batch in '{facilityId}' ({count:N0} smolts, {totalCost:N0} NOK)");
        return true;
    }

    /// <summary>
    /// Vaccinate a smolt batch (disease prevention before sea transfer).
    /// Returns vaccination cost in NOK.
    /// </summary>
    public float VaccinateBatch(string batchId)
    {
        if (SimulationEngine?.PostSmoltFacilities == null)
        {
            GD.PrintErr("GameBridge: VaccinateBatch failed - engine not initialized");
            return 0f;
        }

        // Vaccination cost: ~5 NOK per fish (multi-component vaccine)
        int fishCount = 100_000; // Placeholder - would fetch from actual batch
        decimal vaccineCost = 5m * fishCount;

        if (SimulationEngine.FinanceManager.Balance < vaccineCost)
        {
            GD.PrintErr($"GameBridge: VaccinateBatch failed - insufficient funds (need {vaccineCost:N0} NOK)");
            return 0f;
        }

        SimulationEngine.FinanceManager.Spend(vaccineCost);

        SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = SimulationEngine.State.Day,
            Name = "Batch Vaccination",
            Category = LakskongenSim.Events.EventCategory.Disease,
            Description = $"Vaccinated smolt batch '{batchId}' ({fishCount:N0} fish)",
            BalanceChange = -vaccineCost
        });

        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: Vaccinated batch '{batchId}' for {vaccineCost:N0} NOK");
        return (float)vaccineCost;
    }

    /// <summary>
    /// Transfer a post-smolt batch to sea (move to sea site).
    /// Returns true on success, false if batch not found or sea site unavailable.
    /// </summary>
    public bool TransferBatchToSea(string batchId)
    {
        if (SimulationEngine?.PostSmoltFacilities == null || SimulationEngine?.State?.SeaSites == null)
        {
            GD.PrintErr("GameBridge: TransferBatchToSea failed - engine not initialized");
            EmitSignal(SignalName.FarmActionCompleted, "transfer_batch", batchId, false);
            return false;
        }

        // Transfer cost: logistics, stress mitigation
        decimal transferCost = 2_000_000m; // 2M NOK for batch transfer

        if (SimulationEngine.FinanceManager.Balance < transferCost)
        {
            GD.PrintErr($"GameBridge: TransferBatchToSea failed - insufficient funds (need {transferCost:N0} NOK)");
            EmitSignal(SignalName.FarmActionCompleted, "transfer_batch", batchId, false);
            return false;
        }

        if (SimulationEngine.State.SeaSites.Count == 0)
        {
            GD.PrintErr("GameBridge: TransferBatchToSea failed - no sea sites available");
            EmitSignal(SignalName.FarmActionCompleted, "transfer_batch", batchId, false);
            return false;
        }

        SimulationEngine.FinanceManager.Spend(transferCost);

        // Move batch to first available sea site (simplified)
        var targetSite = SimulationEngine.State.SeaSites.First();

        SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = SimulationEngine.State.Day,
            Name = "Batch Transferred to Sea",
            Category = LakskongenSim.Events.EventCategory.Equipment,
            Description = $"Transferred post-smolt batch '{batchId}' to sea site '{targetSite.Id}'",
            BalanceChange = -transferCost
        });

        EmitSignal(SignalName.FarmActionCompleted, "transfer_batch", batchId, true);
        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: Transferred batch '{batchId}' to sea site '{targetSite.Id}' for {transferCost:N0} NOK");
        return true;
    }

    // ── Empire Systems API (T65) ──────────────────────────────────────────────

    // ── CompetitorAI Methods ───────────────────────────────────────────────────

    /// <summary>
    /// Return all AI competitors as an Array of Dictionaries.
    /// Keys: "id", "name", "parody_of", "strategy", "positioning", "revenue", "market_share_norway".
    /// </summary>
    // NOTE: CompetitorAI, EmpireProgression, StockMarket methods stubbed
    // until the simulation engine implements the required interfaces.
    // These are advanced features — core gameplay works without them.

    public Godot.Collections.Array<Godot.Collections.Dictionary> GetCompetitors()
    {
        return new Godot.Collections.Array<Godot.Collections.Dictionary>();
    }

    public int GetCompetitorCount() => 0;

    public Godot.Collections.Dictionary GetMarketLeader()
    {
        return new Godot.Collections.Dictionary();
    }

    // ── EmpireProgression Methods (stubbed) ────────────────────────────────────

    public string GetEmpirePhase() => "SmallFarmer";
    public int GetEmpireScore() => 0;

    public Godot.Collections.Dictionary GetNextMilestone()
    {
        return new Godot.Collections.Dictionary();
    }

    public string GetGrowthStrategy() => "Organic";

    // ── StockMarket Methods (stubbed) ──────────────────────────────────────────

    public string GetOwnershipStatus() => "Private";
    public double GetSharePrice() => 0.0;
    public double GetMarketCap() => 0.0;
    public bool IsPublic() => false;

    public Godot.Collections.Dictionary AttemptIPO()
    {
        return new Godot.Collections.Dictionary
        {
            ["success"] = false,
            ["message"] = "IPO system not yet implemented"
        };
    }

    // ── BrandReputation Methods ────────────────────────────────────────────────

    /// <summary>Overall brand reputation score (0-100).</summary>
    public double GetBrandScore()
    {
        return (double)(SimulationEngine?.BrandReputation?.OverallScore ?? 50m);
    }

    /// <summary>Current brand strategy (Commodity, Organic, Premium, Luxury, Regional).</summary>
    public string GetBrandStrategy()
    {
        return SimulationEngine?.BrandReputation?.Strategy.ToString() ?? "Commodity";
    }

    /// <summary>
    /// Return active certifications as an Array of strings.
    /// Examples: "ASC", "MSC", "Organic", "GlobalGAP".
    /// </summary>
    public Godot.Collections.Array<string> GetCertifications()
    {
        var result = new Godot.Collections.Array<string>();
        var brand = SimulationEngine?.BrandReputation;
        if (brand == null) return result;

        foreach (var cert in brand.Certifications)
            result.Add(cert);
        return result;
    }

    /// <summary>Pricing power multiplier from brand reputation (1.0 = baseline, >1.0 = premium).</summary>
    public double GetPricingPower()
    {
        var brand = SimulationEngine?.BrandReputation;
        if (brand == null) return 1.0;
        
        // Return pricing power for FiletFersk as a baseline (FilletPremium not yet in ProductType)
        try { return (double)brand.GetPricingPowerMultiplier(LakskongenSim.Market.ProductType.Fillet); }
        catch { return 1.0; }
    }

    // ── CrisisCapitalism Methods ───────────────────────────────────────────────

    /// <summary>
    /// Return active competitor crises as an Array of Dictionaries.
    /// Keys: "id", "type", "competitor_name", "severity", "discount_multiplier".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetActiveCrises()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var system = SimulationEngine?.CrisisCapitalism;
        if (system == null) return result;

        foreach (var crisis in system.GetActiveCrises())
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = crisis.Id,
                ["type"] = crisis.Type.ToString(),
                ["competitor_name"] = crisis.CompetitorName,
                ["severity"] = (double)crisis.SeverityScore,
                ["discount_multiplier"] = (double)crisis.DiscountMultiplier,
                ["duration_months"] = crisis.DurationMonths
            });
        }
        return result;
    }

    /// <summary>
    /// Return available distressed assets for acquisition as an Array of Dictionaries.
    /// Keys: "id", "asset_type", "name", "location", "original_value", "discounted_price", "discount_pct", "risk_score".
    /// </summary>
    // CrisisCapitalism methods stubbed until engine implements GetAvailableAssets/AttemptAcquisition
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetAcquisitionOpportunities()
    {
        return new Godot.Collections.Array<Godot.Collections.Dictionary>();
    }

    public Godot.Collections.Dictionary AttemptAcquisition(string assetId)
    {
        return new Godot.Collections.Dictionary
        {
            ["success"] = false,
            ["message"] = "Acquisition system not yet implemented"
        };
    }

    // ── VerticalIntegration Methods ────────────────────────────────────────────

    /// <summary>
    /// Return all integration tiers with ownership status as an Array of Dictionaries.
    /// Keys: "tier", "name", "is_owned", "capex", "cost_saving_pct", "quality_bonus".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetIntegrationTiers()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var system = SimulationEngine?.VerticalIntegration;
        if (system == null) return result;

        foreach (var asset in system.GetBuildableAssets())
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["tier"] = asset.Tier.ToString(),
                ["name"] = asset.Name,
                ["is_owned"] = asset.IsOwned,
                ["capex"] = (double)asset.CapexNOK,
                ["annual_opex"] = (double)asset.AnnualOpexNOK,
                ["cost_saving_pct"] = (double)asset.CostSavingPctPerKg,
                ["quality_bonus"] = (double)asset.QualityBonus,
                ["description"] = asset.Description
            });
        }
        return result;
    }

    /// <summary>
    /// Return owned integration assets as an Array of Dictionaries.
    /// Keys: "tier", "name", "capacity", "maintenance_cost_pct".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetOwnedAssets()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var system = SimulationEngine?.VerticalIntegration;
        if (system == null) return result;

        foreach (var asset in system.GetOwnedAssets())
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["tier"] = asset.Tier.ToString(),
                ["name"] = asset.Name,
                ["capacity"] = asset.Capacity,
                ["maintenance_cost_pct"] = (double)asset.MaintenanceCostPctPerYear,
                ["construction_completed"] = asset.ConstructionCompleted?.ToString() ?? "N/A"
            });
        }
        return result;
    }

    /// <summary>Overall vertical integration score (0.0-1.0).</summary>
    public double GetIntegrationScore()
    {
        return (double)(SimulationEngine?.VerticalIntegration?.GetIntegrationScore() ?? 0.0f);
    }

    /// <summary>
    /// Attempt to purchase/build a vertical integration asset by ID.
    /// Returns a Dictionary with keys: "success", "message", "capex_paid", "payback_years".
    /// </summary>
    public Godot.Collections.Dictionary PurchaseAsset(string assetId)
    {
        var result = new Godot.Collections.Dictionary();
        var system = SimulationEngine?.VerticalIntegration;
        if (system == null)
        {
            result["success"] = false;
            result["message"] = "Vertical integration system not initialized";
            return result;
        }

        // Find the asset
        var asset = system.GetBuildableAssets().FirstOrDefault(a => a.Tier.ToString() == assetId);
        if (asset == null)
        {
            result["success"] = false;
            result["message"] = $"Asset '{assetId}' not found or already owned";
            return result;
        }

        var integrationResult = system.BuildAsset(asset, (float)SimulationEngine.FinanceManager.Balance);
        result["success"] = integrationResult.Success;
        result["message"] = integrationResult.Message;
        result["capex_paid"] = (double)integrationResult.ActualCapexPaid;
        result["payback_years"] = (double)integrationResult.EstimatedPaybackYears;
        return result;
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // WAVE 2 SYSTEMS API (T71) - LoanSystem, TaxSystem, InvestorSystem, etc.
    // ══════════════════════════════════════════════════════════════════════════════

    // ── LoanSystem Methods ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns active loans as an Array of Dictionaries.
    /// Keys: "id", "principal", "interest_rate", "term_months", "remaining_months", "monthly_payment".
    /// Note: Current LoanSystem is simplified and doesn't track individual loans.
    /// This returns a placeholder for future implementation.
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetActiveLoans()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        // TODO: When LoanSystem tracks individual loans, return them here
        return result;
    }

    /// <summary>Total debt outstanding (NOK). Returns 0 for now (no debt tracking yet).</summary>
    public double GetTotalDebt()
    {
        // TODO: Implement when LoanSystem tracks outstanding principal
        return 0.0;
    }

    /// <summary>Total monthly payment obligation (NOK). Returns 0 for now.</summary>
    public double GetMonthlyPayment()
    {
        // TODO: Calculate from active loans when implemented
        return 0.0;
    }

    /// <summary>
    /// Take out a new loan.
    /// Returns a Dictionary with keys: "success", "message", "loan_id", "principal", "monthly_payment".
    /// </summary>
    public Godot.Collections.Dictionary TakeLoan(double amount, int termMonths, double rate)
    {
        var result = new Godot.Collections.Dictionary();
        
        if (amount <= 0 || termMonths <= 0 || rate < 0)
        {
            result["success"] = false;
            result["message"] = "Invalid loan parameters";
            return result;
        }

        // For now, just add the principal to balance (simplified)
        // TODO: Implement proper loan tracking with amortization schedule
        SimulationEngine?.FinanceManager?.AddRevenue((decimal)amount);
        
        result["success"] = true;
        result["message"] = $"Loan of {amount:N0} NOK approved at {rate:P1} for {termMonths} months";
        result["loan_id"] = "loan_" + SimulationEngine?.State?.Day.ToString() ?? "0";
        result["principal"] = amount;
        
        // Calculate approximate monthly payment (simplified)
        double monthlyRate = rate / 12.0;
        double monthlyPayment = amount * (monthlyRate * Math.Pow(1 + monthlyRate, termMonths)) / 
                                (Math.Pow(1 + monthlyRate, termMonths) - 1);
        result["monthly_payment"] = monthlyPayment;
        
        return result;
    }

    // ── TaxSystem Methods ─────────────────────────────────────────────────────────

    /// <summary>Current corporate tax rate (0.22 = 22%).</summary>
    public double GetTaxRate()
    {
        return 0.22; // Norwegian corporate tax rate
    }

    /// <summary>Last tax payment amount (NOK). Returns 0 for now.</summary>
    public double GetLastTaxPaid()
    {
        // TODO: Track tax payments in TaxSystem
        return 0.0;
    }

    /// <summary>Estimated annual tax burden based on current profit (NOK).</summary>
    public double GetAnnualTaxBurden()
    {
        var profit = SimulationEngine?.FinanceManager?.Balance ?? 0m;
        if (profit <= 0m) return 0.0;
        
        return (double)(profit * 0.22m); // 22% corporate tax
    }

    // ── InvestorSystem Methods ────────────────────────────────────────────────────

    /// <summary>
    /// Returns all investors/shareholders as an Array of Dictionaries.
    /// Keys: "type", "amount", "shares", "equity_pct".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetInvestors()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var investorSystem = SimulationEngine?.InvestorSystem;
        if (investorSystem == null) return result;

        // Return bond holders
        if (investorSystem.OutstandingBondPrincipal > 0)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["type"] = "bond",
                ["amount"] = (double)investorSystem.OutstandingBondPrincipal,
                ["shares"] = 0.0,
                ["equity_pct"] = 0.0
            });
        }

        // Return equity holders (simplified - show total outstanding shares)
        if (investorSystem.OutstandingShares > 1_000_000m)
        {
            var dilutedShares = investorSystem.OutstandingShares - 1_000_000m;
            result.Add(new Godot.Collections.Dictionary
            {
                ["type"] = "equity",
                ["amount"] = (double)(dilutedShares * investorSystem.SharePrice),
                ["shares"] = (double)dilutedShares,
                ["equity_pct"] = (double)(dilutedShares / investorSystem.OutstandingShares)
            });
        }

        return result;
    }

    /// <summary>Total investment from bonds and equity (NOK).</summary>
    public double GetTotalInvestment()
    {
        var investorSystem = SimulationEngine?.InvestorSystem;
        if (investorSystem == null) return 0.0;
        
        return (double)(investorSystem.OutstandingBondPrincipal + 
                       (investorSystem.OutstandingShares - 1_000_000m) * investorSystem.SharePrice);
    }

    /// <summary>Current equity dilution (0.0-1.0, where 0.2 = 20% diluted).</summary>
    public double GetEquityDilution()
    {
        var investorSystem = SimulationEngine?.InvestorSystem;
        if (investorSystem == null) return 0.0;
        
        return (double)investorSystem.DilutionFraction();
    }

    // ── AcquisitionSystem Methods ─────────────────────────────────────────────────

    /// <summary>
    /// Returns available acquisition targets as an Array of Dictionaries.
    /// Keys: "id", "name", "asset_value", "license_value", "health_score", "is_distressed",
    ///       "asking_price", "region", "asset_type", "tonnage_capacity".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetAvailableTargets()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var acquisitionSystem = SimulationEngine?.AcquisitionSystem;
        if (acquisitionSystem == null) return result;

        foreach (var target in acquisitionSystem.GetAvailableTargets())
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = target.Id,
                ["name"] = target.Name,
                ["asset_value"] = (double)target.AssetValue,
                ["license_value"] = (double)target.LicenseValue,
                ["health_score"] = (double)target.HealthScore,
                ["is_distressed"] = target.IsDistressed,
                ["asking_price"] = (double)target.AskingPrice,
                ["regional_market_share"] = (double)target.RegionalMarketShare,
                ["region"] = target.Region,
                ["asset_type"] = target.AssetType,
                ["tonnage_capacity"] = (double)target.TonnageCapacity
            });
        }

        return result;
    }

    /// <summary>
    /// Returns owned acquisitions as an Array of Dictionaries.
    /// Keys: "id", "name", "acquired_price", "acquisition_date", "synergies_per_year".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetOwnedAcquisitions()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        // TODO: Track completed acquisitions in GameState
        // For now return empty array
        return result;
    }

    /// <summary>
    /// Attempt to acquire a target company.
    /// Returns a Dictionary with keys: "success", "message", "actual_price", "regulatory_delay_months", "synergies".
    /// </summary>
    public Godot.Collections.Dictionary AttemptAcquisitionTarget(string targetId)
    {
        var result = new Godot.Collections.Dictionary();
        var acquisitionSystem = SimulationEngine?.AcquisitionSystem;
        if (acquisitionSystem == null)
        {
            result["success"] = false;
            result["message"] = "Acquisition system not initialized";
            return result;
        }

        var targets = acquisitionSystem.GetAvailableTargets();
        var target = targets.FirstOrDefault(t => t.Id == targetId);
        if (target == null)
        {
            result["success"] = false;
            result["message"] = $"Target '{targetId}' not found";
            return result;
        }

        // Calculate acquisition price (90% of asking price as baseline offer)
        float offeredPrice = target.AskingPrice * 0.90f;
        float playerCash = (float)(SimulationEngine?.FinanceManager?.Balance ?? 0m);

        var acquisitionResult = acquisitionSystem.MakeOffer(target, offeredPrice, playerCash);
        
        if (acquisitionResult.Success)
        {
            // Deduct cost
            SimulationEngine?.FinanceManager?.Spend((decimal)acquisitionResult.ActualPrice * 1_000_000m);
            
            // Calculate synergies (simplified - 10% of asset value per year)
            float synergies = (target.AssetValue + target.LicenseValue) * 0.10f;
            
            // Complete acquisition
            acquisitionSystem.CompleteAcquisition(target, acquisitionResult.ActualPrice, synergies);
            
            // Record event
            SimulationEngine?.State?.EventLog.Add(new LakskongenSim.Events.EventRecord
            {
                Day = SimulationEngine.State.Day,
                Name = "Company Acquisition",
                Category = LakskongenSim.Events.EventCategory.Equipment,
                Description = $"Acquired {target.Name} for {acquisitionResult.ActualPrice:F1}M NOK",
                BalanceChange = -(decimal)acquisitionResult.ActualPrice * 1_000_000m
            });
        }

        result["success"] = acquisitionResult.Success;
        result["message"] = acquisitionResult.Success ? 
            $"Successfully acquired {target.Name}" : 
            acquisitionResult.FailureReason;
        result["actual_price"] = (double)acquisitionResult.ActualPrice;
        result["regulatory_delay_months"] = acquisitionResult.RegulatoryDelayMonths;
        result["synergies_per_year"] = (double)acquisitionResult.SynergiesNOKPerYear;
        result["requires_antitrust"] = acquisitionResult.RequiresAntitrustApproval;

        return result;
    }

    // ── LicenseSystem Methods ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns active licenses as an Array of Dictionaries.
    /// Keys: "site_id", "annual_fee", "zone_id", "mab_tonnes".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetActiveLicenses()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var sites = SimulationEngine?.State?.SeaSites;
        if (sites == null) return result;

        foreach (var site in sites)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["site_id"] = site.Id,
                ["annual_fee"] = 50000.0, // From LicenseSystem.AnnualFeePerSite
                ["zone_id"] = site.ZoneId,
                ["mab_tonnes"] = site.MABTonnes
            });
        }

        return result;
    }

    /// <summary>License cost for a new site (NOK).</summary>
    public double GetLicenseCost()
    {
        return 30_000_000.0; // ~30M NOK for 780-tonne MAB license
    }

    /// <summary>
    /// Apply for a new license in a zone.
    /// Returns a Dictionary with keys: "success", "message", "cost", "mab_tonnes", "site_id".
    /// </summary>
    public Godot.Collections.Dictionary ApplyForLicense(string zoneId)
    {
        var result = new Godot.Collections.Dictionary();
        
        // Check if zone exists
        var zone = SimulationEngine?.State?.Zones?.FirstOrDefault(z => z.Id == zoneId);
        if (zone == null)
        {
            result["success"] = false;
            result["message"] = $"Zone '{zoneId}' not found";
            return result;
        }

        // License cost
        decimal licenseCost = 30_000_000m;
        
        // Check funds
        if (SimulationEngine?.FinanceManager?.Balance < licenseCost)
        {
            result["success"] = false;
            result["message"] = $"Insufficient funds (need {licenseCost:N0} NOK)";
            return result;
        }

        // Create new site (license approval)
        var siteId = $"site_{SimulationEngine.State.SeaSites.Count + 1}";
        var success = BuildSeaSite(zoneId, siteId);
        
        result["success"] = success;
        result["message"] = success ? 
            $"License approved for zone {zoneId}" : 
            "License application failed";
        result["cost"] = (double)licenseCost;
        result["mab_tonnes"] = 780.0;
        result["site_id"] = siteId;

        return result;
    }

    // ── StakeholderFactionSystem Methods ──────────────────────────────────────────

    /// <summary>
    /// Returns all stakeholder factions as an Array of Dictionaries.
    /// Keys: "type", "name", "description", "relationship_score", "influence", "weighted_score".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetFactions()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var factionSystem = SimulationEngine?.StakeholderFactionSystem;
        if (factionSystem == null) return result;

        foreach (var faction in factionSystem.GetAllFactions().Values)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["type"] = faction.Type.ToString(),
                ["name"] = faction.Name,
                ["description"] = faction.Description,
                ["relationship_score"] = (double)faction.RelationshipScore,
                ["influence"] = (double)faction.Influence,
                ["weighted_score"] = (double)faction.WeightedRelationshipScore,
                ["active_demands_count"] = faction.ActiveDemands.Count,
                ["active_sanctions_count"] = faction.ActiveSanctions.Count,
                ["active_bonuses_count"] = faction.ActiveBonuses.Count
            });
        }

        return result;
    }

    /// <summary>Get relationship score with a specific faction (-1.0 to +1.0).</summary>
    public double GetFactionRelation(string factionId)
    {
        var factionSystem = SimulationEngine?.StakeholderFactionSystem;
        if (factionSystem == null) return 0.0;

        if (!Enum.TryParse<LakskongenSim.Politics.FactionType>(factionId, true, out var factionType))
            return 0.0;

        var faction = factionSystem.GetFaction(factionType);
        return faction != null ? (double)faction.RelationshipScore : 0.0;
    }

    /// <summary>
    /// Returns all active demands from all factions as an Array of Dictionaries.
    /// Keys: "id", "faction", "description", "cost_nok", "duration_months", "expiry_date", "consequence".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetActiveDemands()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var factionSystem = SimulationEngine?.StakeholderFactionSystem;
        if (factionSystem == null) return result;

        foreach (var demand in factionSystem.GetActiveDemands())
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = demand.Id,
                ["faction"] = demand.FactionType.ToString(),
                ["description"] = demand.Description,
                ["cost_nok"] = (double)demand.CostNOK,
                ["duration_months"] = demand.DurationMonths,
                ["issued_date"] = demand.IssuedDate.ToString("yyyy-MM-dd"),
                ["expiry_date"] = demand.ExpiryDate.ToString("yyyy-MM-dd"),
                ["consequence"] = demand.ConsequenceIfIgnored,
                ["is_expired"] = demand.IsExpired
            });
        }

        return result;
    }

    // ── MarketIntelligence Methods ────────────────────────────────────────────────

    /// <summary>
    /// Returns comprehensive market report as a Dictionary.
    /// Keys: "current_price", "weekly_avg", "trend", "active_events", "competitor_supply_tonnes",
    ///       "player_reputation_score", "forecasts" (Array).
    /// </summary>
    public Godot.Collections.Dictionary GetMarketReport()
    {
        var result = new Godot.Collections.Dictionary();
        var marketIntel = SimulationEngine?.MarketIntelligence;
        if (marketIntel == null)
        {
            result["current_price"] = 70.0;
            result["weekly_avg"] = 70.0;
            result["trend"] = "stable";
            result["active_events"] = new Godot.Collections.Array<Godot.Collections.Dictionary>();
            result["competitor_supply_tonnes"] = 0.0;
            result["player_reputation_score"] = 25.0;
            result["forecasts"] = new Godot.Collections.Array<Godot.Collections.Dictionary>();
            return result;
        }

        var currentPrice = SimulationEngine?.MarketSimulation?.PricePerKg ?? 70m;
        result["current_price"] = (double)currentPrice;
        result["weekly_avg"] = (double)(SimulationEngine?.MarketSimulation?.WeeklyAvgPrice ?? 70m);
        
        // Get active events
        var events = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var evt in marketIntel.ActiveEvents)
        {
            events.Add(new Godot.Collections.Dictionary
            {
                ["headline"] = evt.Headline,
                ["description"] = evt.Description,
                ["category"] = evt.Category.ToString(),
                ["remaining_weeks"] = evt.RemainingWeeks,
                ["supply_impact"] = evt.SupplyImpact,
                ["demand_impact"] = evt.DemandImpact
            });
        }
        result["active_events"] = events;
        
        result["competitor_supply_tonnes"] = marketIntel.GetTotalCompetitorSupplyTonnes();
        result["player_reputation_score"] = marketIntel.PlayerReputation.Score;
        
        // Get price forecasts
        var forecasts = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var forecastList = marketIntel.GetForecasts(currentPrice, 70m, 5m);
        foreach (var f in forecastList)
        {
            forecasts.Add(new Godot.Collections.Dictionary
            {
                ["weeks_ahead"] = f.WeeksAhead,
                ["predicted_price"] = (double)f.PredictedPrice,
                ["lower_bound"] = (double)f.LowerBound,
                ["upper_bound"] = (double)f.UpperBound,
                ["confidence"] = f.Confidence,
                ["trend"] = f.Trend.ToString()
            });
        }
        result["forecasts"] = forecasts;

        return result;
    }

    /// <summary>
    /// Returns competitor analysis as an Array of Dictionaries.
    /// Keys: "name", "country", "weekly_tonnes", "market_rank".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetCompetitorAnalysis()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var marketIntel = SimulationEngine?.MarketIntelligence;
        if (marketIntel == null) return result;

        var snapshot = marketIntel.GetCompetitorSnapshot();
        int rank = 1;
        foreach (var (name, country, weeklyTonnes) in snapshot)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["name"] = name,
                ["country"] = country,
                ["weekly_tonnes"] = weeklyTonnes,
                ["market_rank"] = rank++
            });
        }

        return result;
    }

    /// <summary>
    /// Returns price forecast for next 4 weeks as an Array of Dictionaries.
    /// Keys: "weeks_ahead", "predicted_price", "lower_bound", "upper_bound", "confidence", "trend".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetPriceForecast()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var marketIntel = SimulationEngine?.MarketIntelligence;
        if (marketIntel == null) return result;

        var currentPrice = SimulationEngine?.MarketSimulation?.PricePerKg ?? 70m;
        var forecasts = marketIntel.GetForecasts(currentPrice, 70m, 5m);
        
        foreach (var f in forecasts)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["weeks_ahead"] = f.WeeksAhead,
                ["predicted_price"] = (double)f.PredictedPrice,
                ["lower_bound"] = (double)f.LowerBound,
                ["upper_bound"] = (double)f.UpperBound,
                ["confidence"] = f.Confidence,
                ["trend"] = f.Trend.ToString()
            });
        }

        return result;
    }

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

    // ── VisualMilestones Methods ──────────────────────────────────────────────────

    /// <summary>
    /// Returns all achieved milestones as an Array of Dictionaries.
    /// Keys: "type", "title", "description", "achieved_date", "cinematic_type".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetAchievedMilestones()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var milestones = SimulationEngine?.VisualMilestones;
        if (milestones == null) return result;

        foreach (var milestone in milestones.GetAchievedMilestones())
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["type"] = milestone.Type.ToString(),
                ["title"] = milestone.Title,
                ["description"] = milestone.Description,
                ["achieved_date"] = milestone.AchievedDate?.ToString("yyyy-MM-dd") ?? "",
                ["cinematic_type"] = milestone.CinematicType
            });
        }

        return result;
    }

    /// <summary>
    /// Returns the next unachieved visual milestone as a Dictionary.
    /// Keys: "type", "title", "description", "cinematic_type".
    /// Returns empty Dictionary if all milestones achieved.
    /// </summary>
    public Godot.Collections.Dictionary GetNextVisualMilestone()
    {
        var result = new Godot.Collections.Dictionary();
        var milestones = SimulationEngine?.VisualMilestones;
        if (milestones == null) return result;

        var next = milestones.GetNextMilestone();
        if (next != null)
        {
            result["type"] = next.Type.ToString();
            result["title"] = next.Title;
            result["description"] = next.Description;
            result["cinematic_type"] = next.CinematicType;
        }

        return result;
    }

    /// <summary>Milestone completion progress (0.0-1.0).</summary>
    public double GetMilestoneProgress()
    {
        var milestones = SimulationEngine?.VisualMilestones;
        return milestones?.GetProgress() ?? 0.0;
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // DASHBOARD API - Financial Dashboards Integration
    // ══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns P&L statement data as a Godot Dictionary.
    /// Keys: "revenue", "cost_of_sales", "gross_profit", "operating_expenses",
    ///       "ebitda", "depreciation", "ebit", "interest", "tax", "net_profit",
    ///       "gross_margin_pct", "operating_margin_pct", "net_margin_pct".
    /// </summary>
    public Godot.Collections.Dictionary GetPLData()
    {
        if (_dashboardDataProvider == null)
        {
            GD.PrintErr("GameBridge: DashboardDataProvider not initialized");
            return new Godot.Collections.Dictionary();
        }

        var plData = _dashboardDataProvider.GetPLData();
        var result = new Godot.Collections.Dictionary();

        foreach (var kvp in plData)
        {
            result[kvp.Key] = kvp.Value;
        }

        return result;
    }

    /// <summary>
    /// Returns 12-month cash flow forecast as a Godot Array of Dictionaries.
    /// Each entry: "month", "operating_cash", "investing_cash", "financing_cash", "net_cash", "cumulative_cash".
    /// </summary>
    public Godot.Collections.Array GetCashFlowForecast()
    {
        if (_dashboardDataProvider == null)
        {
            GD.PrintErr("GameBridge: DashboardDataProvider not initialized");
            return new Godot.Collections.Array();
        }

        var forecast = _dashboardDataProvider.GetCashFlowForecast();
        var result = new Godot.Collections.Array();

        foreach (var month in forecast)
        {
            var dict = new Godot.Collections.Dictionary();
            foreach (var kvp in month)
            {
                dict[kvp.Key] = kvp.Value;
            }
            result.Add(dict);
        }

        return result;
    }

    /// <summary>
    /// Returns ROI analysis data as a Godot Dictionary.
    /// Keys: "total_investment", "annual_return", "roi_pct", "payback_months",
    ///       "npv", "irr_pct", "break_even_month".
    /// </summary>
    public Godot.Collections.Dictionary GetROIAnalysis()
    {
        if (_dashboardDataProvider == null)
        {
            GD.PrintErr("GameBridge: DashboardDataProvider not initialized");
            return new Godot.Collections.Dictionary();
        }

        var roiData = _dashboardDataProvider.GetROIAnalysis();
        var result = new Godot.Collections.Dictionary();

        foreach (var kvp in roiData)
        {
            result[kvp.Key] = kvp.Value;
        }

        return result;
    }

    /// <summary>
    /// Returns detailed cost breakdown as a Godot Dictionary.
    /// Keys: "opex_feed", "opex_labor", "opex_maintenance", "opex_licenses",
    ///       "opex_delousing", "opex_other", "opex_total",
    ///       "capex_licenses", "capex_pens", "capex_equipment", "capex_ras",
    ///       "capex_offshore", "capex_total".
    /// </summary>
    public Godot.Collections.Dictionary GetCostBreakdown()
    {
        if (_dashboardDataProvider == null)
        {
            GD.PrintErr("GameBridge: DashboardDataProvider not initialized");
            return new Godot.Collections.Dictionary();
        }

        var costData = _dashboardDataProvider.GetCostBreakdown();
        var result = new Godot.Collections.Dictionary();

        foreach (var kvp in costData)
        {
            result[kvp.Key] = kvp.Value;
        }

        return result;
    }

    /// <summary>
    /// Returns market price chart data (current + 30-day history) as a Godot Array of Dictionaries.
    /// Each entry: "day", "price_per_kg", "weekly_avg", "volume_tonnes".
    /// </summary>
    public Godot.Collections.Array GetMarketPrices()
    {
        if (_dashboardDataProvider == null)
        {
            GD.PrintErr("GameBridge: DashboardDataProvider not initialized");
            return new Godot.Collections.Array();
        }

        var prices = _dashboardDataProvider.GetMarketPrices();
        var result = new Godot.Collections.Array();

        foreach (var price in prices)
        {
            var dict = new Godot.Collections.Dictionary();
            foreach (var kvp in price)
            {
                dict[kvp.Key] = kvp.Value;
            }
            result.Add(dict);
        }

        return result;
    }

    /// <summary>
    /// Returns production cost per kg data as a Godot Dictionary.
    /// Keys: "total_cost_per_kg", "feed_cost_per_kg", "labor_cost_per_kg",
    ///       "maintenance_cost_per_kg", "other_cost_per_kg", "total_biomass_kg",
    ///       "fcr", "benchmark_cost_per_kg", "vs_benchmark_pct".
    /// </summary>
    public Godot.Collections.Dictionary GetProductionCosts()
    {
        if (_dashboardDataProvider == null)
        {
            GD.PrintErr("GameBridge: DashboardDataProvider not initialized");
            return new Godot.Collections.Dictionary();
        }

        var costData = _dashboardDataProvider.GetProductionCosts();
        var result = new Godot.Collections.Dictionary();

        foreach (var kvp in costData)
        {
            result[kvp.Key] = kvp.Value;
        }

        return result;
    }

    /// <summary>
    /// Returns scenario comparison data as a Godot Array of Dictionaries.
    /// Each scenario: "name", "revenue", "cost", "profit", "roi_pct", "risk_score".
    /// </summary>
    public Godot.Collections.Array GetScenarioComparison()
    {
        if (_dashboardDataProvider == null)
        {
            GD.PrintErr("GameBridge: DashboardDataProvider not initialized");
            return new Godot.Collections.Array();
        }

        var scenarios = _dashboardDataProvider.GetScenarioComparison();
        var result = new Godot.Collections.Array();

        foreach (var scenario in scenarios)
        {
            var dict = new Godot.Collections.Dictionary();
            foreach (var kvp in scenario)
            {
                dict[kvp.Key] = kvp.Value;
            }
            result.Add(dict);
        }

        return result;
    }

    /// <summary>
    /// Returns AI advisor recommendations as a Godot Array of strings.
    /// </summary>
    public Godot.Collections.Array<string> GetAdvisorRecommendations()
    {
        if (_dashboardDataProvider == null)
        {
            GD.PrintErr("GameBridge: DashboardDataProvider not initialized");
            return new Godot.Collections.Array<string>();
        }

        var recommendations = _dashboardDataProvider.GetAdvisorRecommendations();
        var result = new Godot.Collections.Array<string>();

        foreach (var recommendation in recommendations)
        {
            result.Add(recommendation);
        }

        return result;
    }

    public override void _ExitTree()
    {
        _simulationTimer?.Stop();
        _simulationTimer?.QueueFree();
    }
}

