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

    // ── Mod system ────────────────────────────────────────────────────────────

    private ModLoader? _modLoader;
    private string _modsDirectory = "";

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
            GD.Print($"GameBridge: ModLoader ready at {_modsDirectory} " +
                     $"({_modLoader.LoadedMods.Count} loaded, {_modLoader.LoadErrors.Count} errors)");
        }
        catch (Exception ex)
        {
            _modLoader = null;
            GD.PrintErr($"GameBridge: ModLoader init failed: {ex.Message}");
        }
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

    /// <summary>Initialize and start a new game.</summary>
    public void StartNewGame(string playerName, int startingCapital = 10_000_000)
    {
        SimulationEngine.FinanceManager = new FinanceManager(startingCapital);
        SimulationEngine.Initialize();

        // P11: a new game starts with no in-memory mitigation installs and a
        // clean serializable mirror (the registry outlives Initialize).
        _installedMitigations.Clear();
        SyncMitigationsToState();
        SimulationEngine.Tutorial.OnStepCompleted += step =>
            EmitSignal(SignalName.TutorialStepCompleted,
                step.StepNumber, step.Title, (double)step.RewardNok);
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

        // Restore day counter and persistent collections
        // (State outlives Initialize — clear first so repeat loads
        // never duplicate zones, sites, populations or log entries.)
        SimulationEngine.State.Day = snapshot.Day;
        // Ownership flags (were dropped on load; restored here incl. VAP tier).
        SimulationEngine.State.OwnsHatchery = snapshot.OwnsHatchery;
        SimulationEngine.State.OwnsFeedMill = snapshot.OwnsFeedMill;
        SimulationEngine.State.OwnsProcessingPlant = snapshot.OwnsProcessingPlant;
        SimulationEngine.State.ProcessingTier = snapshot.ProcessingTier ?? "";
        SimulationEngine.State.HasASCCertification = snapshot.HasASCCertification;
        SimulationEngine.State.Zones.Clear();
        SimulationEngine.State.SeaSites.Clear();
        SimulationEngine.State.Populations.Clear();
        SimulationEngine.State.EventLog.Clear();
        SimulationEngine.State.WellboatJobs.Clear();
        SimulationEngine.State.Crew.Clear();
        SimulationEngine.State.ActiveBlooms.Clear();
        SimulationEngine.State.ActiveCrises.Clear();
        SimulationEngine.State.ActiveLoans.Clear();
        SimulationEngine.State.TaxHistory.Clear();
        SimulationEngine.State.Acquisitions.Clear();
        SimulationEngine.State.Licenses.Clear();
        SimulationEngine.State.RecentMarketReports.Clear();
        SimulationEngine.State.AchievedVisualMilestones.Clear();
        if (SimulationEngine.State.CompetitorCompanies != null)
            SimulationEngine.State.CompetitorCompanies.Clear();
        foreach (var zone in snapshot.Zones)
            SimulationEngine.State.Zones.Add(zone);
        foreach (var site in snapshot.SeaSites)
            SimulationEngine.State.SeaSites.Add(site);
        foreach (var pop in snapshot.Populations)
            SimulationEngine.State.Populations.Add(pop);
        foreach (var evt in snapshot.EventLog)
            SimulationEngine.State.EventLog.Add(evt);
        if (snapshot.WellboatJobs != null)
            foreach (var job in snapshot.WellboatJobs)
                SimulationEngine.State.WellboatJobs.Add(job);
        if (snapshot.Crew != null)
            foreach (var member in snapshot.Crew)
                SimulationEngine.State.Crew.Add(member);
        if (snapshot.ActiveBlooms != null)
            foreach (var bloom in snapshot.ActiveBlooms)
                SimulationEngine.State.ActiveBlooms.Add(bloom);

        // Empire-system DTOs (save-rounded; engine rehydrates live systems)
        SimulationEngine.State.StockMarket = snapshot.StockMarket;
        SimulationEngine.State.BrandReputation = snapshot.BrandReputation;
        foreach (var crisis in snapshot.ActiveCrises)
            SimulationEngine.State.ActiveCrises.Add(crisis);
        SimulationEngine.State.VerticalIntegration = snapshot.VerticalIntegration;
        foreach (var loan in snapshot.ActiveLoans)
            SimulationEngine.State.ActiveLoans.Add(loan);
        foreach (var tax in snapshot.TaxHistory)
            SimulationEngine.State.TaxHistory.Add(tax);
        SimulationEngine.State.InvestorSystem = snapshot.InvestorSystem;
        foreach (var acq in snapshot.Acquisitions)
            SimulationEngine.State.Acquisitions.Add(acq);
        foreach (var lic in snapshot.Licenses)
            SimulationEngine.State.Licenses.Add(lic);
        foreach (var kvp in snapshot.FactionRelations)
            SimulationEngine.State.FactionRelations[kvp.Key] = kvp.Value;
        foreach (var rep in snapshot.RecentMarketReports)
            SimulationEngine.State.RecentMarketReports.Add(rep);
        SimulationEngine.State.FishWelfareCondition = snapshot.FishWelfareCondition;
        foreach (var m in snapshot.AchievedVisualMilestones)
            SimulationEngine.State.AchievedVisualMilestones.Add(m);
        if (snapshot.CompetitorCompanies != null)
            foreach (var c in snapshot.CompetitorCompanies)
                SimulationEngine.State.CompetitorCompanies.Add(c);
        SimulationEngine.State.EmpireProgression = snapshot.EmpireProgression;

        // CRITICAL: Re-inject dependencies into deserialized populations
        // Fixes P1-03 (Save/Load NRE Gap) - populations need GrowthModel/MortalityModel after deserialization
        SimulationEngine.RestoreFromLoad(snapshot);

        // P11 persistence: rebuild the in-memory mitigation registry from the
        // saved list, then re-normalize the serializable mirror.
        RestoreMitigationsFromState(snapshot.BloomMitigations);
        SyncMitigationsToState();

        // Phase 4.4: re-apply saved recipe to the fresh FeedSystem.
        ApplyFeedRecipe();

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
            ProcessDueWellboatJobs();
            EmitSignal(SignalName.SimulationTicked, SimulationEngine.State.Day);
            EmitSignal(SignalName.GameStateUpdated);
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
        var state = SimulationEngine?.State;
        if (state?.SeaSites?.All(s => s.Id != siteId) != false)
        {
            EmitSignal(SignalName.FarmActionCompleted, "wellboat", siteId, false);
            return false;
        }
        if (state.WellboatJobs.Any(j => j.SiteId == siteId && j.Kind == kind))
        {
            GD.Print($"GameBridge: Wellboat already booked for '{siteId}' ({kind})");
            return false;
        }
        int delay = kind switch { "delouse" => 3, "rov" => 2, "freshwater" => 4, _ => 5 };
        decimal hire = kind switch { "delouse" => 100_000m, "rov" => 75_000m, "freshwater" => 120_000m, _ => 150_000m };
        // Skipper: -25% hire via contacts, -1 day routing.
        hire *= LakskongenSim.Core.CrewEffects.WellboatHireMultiplier(state);
        delay = System.Math.Max(1, delay - LakskongenSim.Core.CrewEffects.WellboatDelayReduction(state));
        if (SimulationEngine!.FinanceManager.Balance < hire)
        {
            EmitSignal(SignalName.FarmActionCompleted, "wellboat", siteId, false);
            return false;
        }
        SimulationEngine.FinanceManager.Spend(hire);
        state.WellboatJobs.Add(new LakskongenSim.Logistics.WellboatJob
        {
            SiteId = siteId, Kind = kind, Method = method,
            DueDay = state.Day + delay, HireCost = hire
        });
        state.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = state.Day,
            Name = "Brønnbåt bestilt",
            Category = LakskongenSim.Events.EventCategory.Equipment,
            Description = $"Brønnbåt bestilt til {siteId} ({kind}, ankomst om {delay}d)",
            BalanceChange = -hire
        });
        EmitSignal(SignalName.FarmActionCompleted, "wellboat", siteId, true);
        EmitSignal(SignalName.GameStateUpdated);
        return true;
    }

    /// <summary>Execute due wellboat jobs after each advance (Phase 2.4).</summary>
    private void ProcessDueWellboatJobs()
    {
        var state = SimulationEngine?.State;
        if (state?.WellboatJobs.Count == 0) return;
        foreach (var job in state!.WellboatJobs.Where(j => j.DueDay <= state.Day).ToList())
        {
            if (job.Kind == "freshwater")
            {
                // Freshwater bath boat: Treat all site pens, zero resistance.
                var fwsite = state.SeaSites.FirstOrDefault(s => s.Id == job.SiteId);
                bool fwok = fwsite != null && fwsite.Pens.Any(p => p.TotalCount > 0);
                int fwdeaths = 0;
                if (fwok)
                {
                    var delousing = new LakskongenSim.Farm.DelousingSystem();
                    double tempC = SimulationEngine!.Weather?.CurrentTemperatureC ?? 10.0;
                    double mortMult = LakskongenSim.Core.CrewEffects.TreatmentMortalityMultiplier(state);
                    foreach (var pop in fwsite!.Pens)
                    {
                        if (pop.TotalCount <= 0) continue;
                        fwdeaths += delousing.Treat(pop,
                            LakskongenSim.Data.DelousingMethod.FreshwaterBath,
                            SimulationEngine.SeaLice, SimulationEngine.IntegratedDisease,
                            tempC, mortMult).Deaths;
                    }
                    SimulationEngine.FinanceManager.Spend(60_000m); // bath consumables
                }
                state.WellboatJobs.Remove(job);
                state.EventLog.Add(new LakskongenSim.Events.EventRecord
                {
                    Day = state.Day,
                    Name = fwok ? "Ferskvannsbehandling" : "Brønnbåt returnerte",
                    Category = LakskongenSim.Events.EventCategory.Disease,
                    Description = fwok
                        ? $"Ferskvannsbåt behandlet {job.SiteId} ({fwdeaths:N0} håndteringsdødsfall, null resistens)"
                        : $"Ferskvannsbåt kunne ikke behandle {job.SiteId}",
                    BalanceChange = fwok ? -60_000m : 0m
                });
                continue;
            }
            if (job.Kind == "rov")
            {
                // ROV inspection: cleans nets (+15 integrity) + findings report.
                var site = state.SeaSites.FirstOrDefault(s => s.Id == job.SiteId);
                bool ok = site != null;
                if (ok)
                {
                    site!.NetIntegrityPct = System.Math.Min(100.0, site.NetIntegrityPct + 15.0);
                    int fish = site.Pens.SelectMany(p => p.Cohorts).Sum(c => c.Count);
                    state.EventLog.Add(new LakskongenSim.Events.EventRecord
                    {
                        Day = state.Day,
                        Name = "ROV-inspeksjon",
                        Category = LakskongenSim.Events.EventCategory.Equipment,
                        Description = $"ROV ved {site.Id}: {fish:N0} fisk, lus {SimulationEngine!.SeaLice.AverageLicePerFish:F2}/fisk, gjeller {site.GillScore:F0}/100, nøter {site.NetIntegrityPct:F0}% (+15 renset), rømminger i år: {site.EscapesThisYear}",
                        BalanceChange = 0m
                    });
                }
                state.WellboatJobs.Remove(job);
                continue;
            }
            bool done = job.Kind == "delouse"
                ? OrderDelousing(job.SiteId, string.IsNullOrEmpty(job.Method) ? "hydrolicer" : job.Method)
                : HarvestSite(job.SiteId) > 0.0;
            state.WellboatJobs.Remove(job);
            state.EventLog.Add(new LakskongenSim.Events.EventRecord
            {
                Day = state.Day,
                Name = done ? "Brønnbåt ankommet" : "Brønnbåt returnerte",
                Category = LakskongenSim.Events.EventCategory.Equipment,
                Description = done
                    ? $"Brønnbåt utførte {job.Kind} ved {job.SiteId}"
                    : $"Brønnbåt kunne ikke utføre {job.Kind} ved {job.SiteId} (tom merd/manglende midler)",
                BalanceChange = 0m
            });
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
    public double SeaLicePerFish => SimulationEngine?.SeaLice?.AverageLicePerFish ?? 0.0;

    // ── Phase 1.1 live KPI scalars (additive; backed by Feed/Analytics/Zone/Weather) ──

    /// <summary>Current feed conversion ratio (live from FeedSystem).</summary>
    public double CurrentFCR => SimulationEngine?.Feed?.CurrentFCR ?? 1.15;

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
    ///       "mab_factor" (double), "capacity_tonnes" (double).
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
                ["capacity_tonnes"] = zone.CarryingCapacityTonnes
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
            ProcessDueWellboatJobs();
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
        EndGame($"Scenario '{scenario.Name}' failed: {reason}");
    }

    /// <summary>
    /// Project any mod-provided scenarios into the engine's ScenarioSystem so they
    /// become startable via <see cref="StartScenario"/> in addition to built-ins.
    /// Safe to call repeatedly (registering an id replaces it).
    /// </summary>
    private void RegisterModScenariosIntoEngine()
    {
        if (_modLoader == null || SimulationEngine?.Scenario == null) return;

        var definitions = new List<ScenarioDefinition>();
        foreach (var mod in _modLoader.GetAllScenarios())
        {
            definitions.Add(new ScenarioDefinition
            {
                Id = mod.Id,
                Name = string.IsNullOrWhiteSpace(mod.Name) ? mod.Id : mod.Name,
                Description = mod.Description,
                StartingCapitalNOK = mod.StartingCapitalNOK,
                StartingRegion = ParseRegion(mod.StartingRegion),
                Difficulty = Math.Clamp(mod.Difficulty, 1, 5),
                WinCondition = ParseWinCondition(mod.WinCondition),
                SpecialRules = new Dictionary<string, string>(),
                TimeLimitDays = null,
                IsUnlocked = true
            });
        }

        if (definitions.Count > 0)
            SimulationEngine.Scenario.RegisterScenarios(definitions);
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

            // P11: track the realized storm loss in NOK so a Storm policy can
            // settle one claim per weather event (fish loss + infrastructure).
            decimal stormSpotPrice = SimulationEngine.MarketSimulation?.PricePerKg ?? 70m;
            decimal stormFishLoss = 0m;
            
            // Storm damages pens and increases mortality (roekter halves losses).
            // Iterate each site's own pens: populations are ZoneId-tagged
            // (zone-shared), so State-wide filtering here would hit shared
            // populations once per site in the zone.
            double stormMult = LakskongenSim.Core.CrewEffects.StormLossMultiplier(SimulationEngine.State);
            foreach (var site in SimulationEngine.State.SeaSites)
            {
                foreach (var pop in site.Pens)
                {
                    foreach (var cohort in pop.Cohorts)
                    {
                        // Increase mortality due to storm stress (0.5-2% loss depending on severity)
                        int mortalityCount = (int)(cohort.Count * severity * 0.02 * stormMult);
                        if (mortalityCount > 0)
                        {
                            cohort.Count = Math.Max(0, cohort.Count - mortalityCount);
                            stormFishLoss += (decimal)(mortalityCount * cohort.MeanWeightKg) * stormSpotPrice;
                        }
                    }
                }
            }

            // Phase 1.2: nets degrade in heavy weather; weak nets tear → escapes.
            // Escape is player-fault-visible: only sites left below 40% tear.
            foreach (var site in SimulationEngine.State.SeaSites)
            {
                // Phase 4.1: containment storm/escape signature per site.
                var cmods = LakskongenSim.Farm.ContainmentTech.ModifiersFor(site.Containment);
                site.NetIntegrityPct = System.Math.Max(0.0, site.NetIntegrityPct
                    - severity * 25.0 * LakskongenSim.Core.CrewEffects.NetDecayMultiplier(SimulationEngine.State) * cmods.StormDamageMult);
                if (site.NetIntegrityPct >= 40.0) continue;

                int siteFish = site.Pens.SelectMany(p => p.Cohorts).Sum(c => c.Count);
                int escapees = (int)(siteFish * (0.02 + severity * 0.06) * stormMult * cmods.EscapeRiskMult
                    * LakskongenSim.Optimization.RemoteOpsCentre.EscapeResponseMult(site.WatchMode));
                if (escapees <= 0) continue;

                if (siteFish > 0)
                {
                    double siteBiomassKg = site.Pens.SelectMany(p => p.Cohorts)
                        .Sum(c => (double)c.Count * c.MeanWeightKg);
                    stormFishLoss += (decimal)(escapees * (siteBiomassKg / siteFish)) * stormSpotPrice;
                }

                int remaining = escapees;
                foreach (var pop in site.Pens)
                {
                    foreach (var cohort in pop.Cohorts)
                    {
                        int take = System.Math.Min(cohort.Count, remaining);
                        cohort.Count = System.Math.Max(0, cohort.Count - take);
                        remaining -= take;
                        if (remaining <= 0) break;
                    }
                    if (remaining <= 0) break;
                }
                site.RecordEscape(escapees);
                decimal escapeFine = escapees * 100m; // NOK 100/fish regulatory fine
                if (SimulationEngine.FinanceManager.Balance >= escapeFine)
                    SimulationEngine.FinanceManager.Spend(escapeFine);
                SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
                {
                    Day = SimulationEngine.State.Day,
                    Name = "Escape",
                    Category = LakskongenSim.Events.EventCategory.Equipment,
                    Description = $"RØMMING: {escapees:N0} fish escaped at {site.Id} (nets {site.NetIntegrityPct:F0}%)",
                    BalanceChange = -escapeFine
                });
                EmitSignal(SignalName.DiseaseOutbreak, "Escape", site.Id, 1.0, (double)escapeFine);
                GD.Print($"GameBridge: ESCAPE — {escapees:N0} fish at '{site.Id}' (fine {escapeFine:N0} NOK)");
            }
            // Storm repair bill (infrastructure damage, separate from escapes).
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

            // P11: settle the Storm insurance claim against the realized loss
            // (fish mortality + escapes + infrastructure repair).
            decimal stormLossNok = stormFishLoss + repairCost;
            decimal stormPayout = SimulationEngine.Insurance?.ComputePayout(InsuranceKind.Storm, stormLossNok) ?? 0m;
            if (stormPayout > 0m)
            {
                SimulationEngine.FinanceManager.AddRevenue(stormPayout);
                SimulationEngine.FinancialStatements?.RecordRevenue(stormPayout);
                SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
                {
                    Day = SimulationEngine.State.Day,
                    Id = "insurance-storm",
                    Name = "Forsikringsoppgjør",
                    Category = LakskongenSim.Events.EventCategory.Weather,
                    Description = $"Forsikring (Storm) dekket {stormPayout:N0} av {stormLossNok:N0} NOK stormtap",
                    BalanceChange = stormPayout
                });
                GD.Print($"GameBridge: Storm insurance payout {stormPayout:N0} NOK (loss {stormLossNok:N0} NOK)");
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
    /// Return all available scenarios (built-in roster + mod scenarios) as an
    /// Array of Dictionaries. Keys consumed by the UI:
    ///   "id", "name", "description", "difficulty" (int 1-5), "region",
    ///   "starting_capital" (double), "starting_capital_neg" (bool),
    ///   "starting_cash" (double), "story" (string), "tags" (Array[String]),
    ///   "restrictions" (Array[String]),
    ///   "win_condition", "win" (human text), "time_limit_days",
    ///   "is_unlocked"/"unlocked" (bool), "special_rules" (Dictionary),
    ///   "special" (Array[String]), "subtitle", "capital" (formatted).
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetAvailableScenarios()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        if (SimulationEngine == null) return result;

        ScenarioSystem? system = SimulationEngine.Scenario;
        system ??= new ScenarioSystem(SimulationEngine.State);

        foreach (var scenario in system.GetAll())
            result.Add(ScenarioToDictionary(scenario));

        // Merge mod scenarios that are not already in the engine roster.
        if (_modLoader != null)
        {
            foreach (var mod in _modLoader.GetAllScenarios())
            {
                if (system.GetScenario(mod.Id) != null) continue;
                result.Add(ModScenarioToDictionary(mod));
            }
        }
        return result;
    }

    /// <summary>
    /// Load and start a scenario by ID. Initializes the engine if needed,
    /// applies the scenario's starting capital, and emits <c>ScenarioLoaded</c>.
    /// Returns true on success, false if unknown/locked or on error.
    /// </summary>
    public bool StartScenario(string scenarioId)
    {
        if (string.IsNullOrWhiteSpace(scenarioId) || SimulationEngine == null)
            return false;

        // Not running yet (e.g. launched straight from the campaign menu):
        // validate against the roster, then initialize the engine first.
        if (!_gameRunning || SimulationEngine.Scenario == null)
        {
            var probe = SimulationEngine.Scenario ?? new ScenarioSystem(SimulationEngine.State);
            var candidate = probe.GetScenario(scenarioId);
            if (candidate == null || !candidate.IsUnlocked)
            {
                GD.Print($"GameBridge.StartScenario: '{scenarioId}' unavailable (unknown or locked)");
                return false;
            }
            StartNewGame("Player", (int)candidate.StartingCapitalNOK);
        }

        var scenario = SimulationEngine.Scenario!.GetScenario(scenarioId);
        if (scenario == null) return false;

        try
        {
            SimulationEngine.Scenario!.StartScenario(scenarioId);
            // Materialize the scenario's starting sites/populations/research into
            // the live GameState (idempotent; SimulationEngine is left untouched).
            scenario.ApplyStartingState(SimulationEngine.State);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"GameBridge.StartScenario('{scenarioId}') failed: {ex.Message}");
            return false;
        }

        EmitSignal(SignalName.ScenarioLoaded, scenario.Id, scenario.Name);
        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: scenario started — {scenario.Name} ({scenario.Id})");
        return true;
    }

    /// <summary>
    /// Return the ID of the currently active scenario (empty when none).
    /// </summary>
    public string GetActiveScenarioId()
        => SimulationEngine?.Scenario?.ActiveScenario?.Id ?? "";

    /// <summary>Whether the active scenario has been won.</summary>
    public bool IsScenarioWon => SimulationEngine?.Scenario?.IsWon ?? false;

    /// <summary>Whether the active scenario has been lost.</summary>
    public bool IsScenarioLost => SimulationEngine?.Scenario?.IsLost ?? false;

    /// <summary>Human-readable loss reason for the active scenario ("" if none).</summary>
    public string ScenarioLossReason => SimulationEngine?.Scenario?.LossReason ?? "";

    /// <summary>
    /// Apply a scenario supplied as a JSON string (used by ScenarioManager.gd).
    /// Accepts either the C# schema (win_condition/starting_balance) or the legacy
    /// Godot schema (goal/starting_balance). Unknown ids are registered first.
    /// Returns true when the scenario was started.
    /// </summary>
    public bool ApplyScenario(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || SimulationEngine == null) return false;

        ScenarioJsonDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ScenarioJsonDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
        }
        catch (Exception ex)
        {
            GD.PrintErr($"GameBridge.ApplyScenario: parse error: {ex.Message}");
            return false;
        }

        if (dto == null || string.IsNullOrWhiteSpace(dto.Id)) return false;

        decimal capital = dto.StartingBalance
                          ?? dto.StartingCapitalNok
                          ?? dto.InitialBalanceNok
                          ?? 10_000_000m;

        if (!_gameRunning || SimulationEngine.Scenario == null)
            StartNewGame("Player", (int)capital);

        if (SimulationEngine.Scenario!.GetScenario(dto.Id) == null)
        {
            SimulationEngine.Scenario.RegisterScenarios(new[]
            {
                new ScenarioDefinition
                {
                    Id = dto.Id,
                    Name = string.IsNullOrWhiteSpace(dto.Name) ? dto.Id : dto.Name,
                    Description = dto.Description,
                    StartingCapitalNOK = dto.StartingCapitalNok ?? capital,
                    StartingCash = dto.StartingCash ?? (dto.StartingCapitalNok ?? capital),
                    Story = string.IsNullOrWhiteSpace(dto.Story) ? dto.Description : dto.Story!,
                    StartingRegion = ParseRegion(dto.StartingRegion),
                    Difficulty = ParseDifficultyElement(dto.Difficulty),
                    WinCondition = ParseWinCondition(dto.WinCondition),
                    SpecialRules = new Dictionary<string, string>(),
                    StartingSites = dto.StartingSites ?? new List<StartingSiteDefinition>(),
                    StartingResearch = dto.StartingResearch ?? System.Array.Empty<string>(),
                    StartingBuildings = dto.StartingBuildings ?? System.Array.Empty<string>(),
                    Restrictions = dto.Restrictions ?? System.Array.Empty<string>(),
                    Tags = dto.Tags ?? System.Array.Empty<string>(),
                    TimeLimitDays = dto.TimeLimitDays,
                    IsUnlocked = dto.IsUnlocked ?? true
                }
            });
        }

        return StartScenario(dto.Id);
    }

    // ── Scenario dictionary projection ───────────────────────────────────────

    private static Godot.Collections.Dictionary ScenarioToDictionary(ScenarioDefinition s)
    {
        var rules = new Godot.Collections.Dictionary();
        foreach (var kv in s.SpecialRules)
            rules[kv.Key] = kv.Value;

        var special = new Godot.Collections.Array<string>();
        if (s.SpecialRules.ContainsKey("single_site")) special.Add("1 anlegg");
        if (s.SpecialRules.ContainsKey("harsh_storms")) special.Add("Harde stormer");
        if (s.SpecialRules.ContainsKey("premium_prices")) special.Add("Premiumpriser");
        if (s.SpecialRules.ContainsKey("start_disease")) special.Add("Aktiv sykdom");
        if (s.SpecialRules.ContainsKey("start_debt")) special.Add("Startgjeld");
        if (s.SpecialRules.ContainsKey("regulation_pressure")) special.Add("Reguleringstrykk");
        if (s.SpecialRules.ContainsKey("calm_weather")) special.Add("Rolig vær");
        if (s.SpecialRules.ContainsKey("easy_market")) special.Add("Stabile priser");
        if (s.SpecialRules.TryGetValue("ai_competitors", out var aiCount)) special.Add($"{aiCount} AI-rivaler");

        return new Godot.Collections.Dictionary
        {
            ["id"] = s.Id,
            ["name"] = s.Name,
            ["description"] = s.Description,
            ["difficulty"] = s.Difficulty,
            ["region"] = s.StartingRegion.ToString(),
            ["starting_capital"] = (double)s.StartingCapitalNOK,
            ["starting_capital_neg"] = s.StartingCapitalNOK < 0m,
            // Extended campaign metadata (backward-compatible additive keys).
            ["starting_cash"] = (double)(s.StartingCash != 0m ? s.StartingCash : s.StartingCapitalNOK),
            ["story"] = string.IsNullOrEmpty(s.Story) ? s.Description : s.Story,
            ["tags"] = ToGdArray(s.Tags),
            ["restrictions"] = ToGdArray(s.Restrictions),
            ["win_condition"] = s.WinCondition.ToString(),
            ["win"] = DescribeWinCondition(s),
            ["time_limit_days"] = s.TimeLimitDays ?? 0,
            ["is_unlocked"] = s.IsUnlocked,
            ["unlocked"] = s.IsUnlocked,
            ["special_rules"] = rules,
            ["special"] = special,
            ["subtitle"] = DifficultyLabel(s.Difficulty),
            ["capital"] = FormatCapital(s.StartingCapitalNOK)
        };
    }

    /// <summary>Project a C# string[] to a Godot Array[String] (empty when null).</summary>
    private static Godot.Collections.Array<string> ToGdArray(string[]? source)
    {
        var arr = new Godot.Collections.Array<string>();
        if (source != null)
            foreach (var value in source)
                if (!string.IsNullOrEmpty(value)) arr.Add(value);
        return arr;
    }

    private static Godot.Collections.Dictionary ModScenarioToDictionary(ModScenario m)
    {
        var rules = new Godot.Collections.Dictionary();
        return new Godot.Collections.Dictionary
        {
            ["id"] = m.Id,
            ["name"] = m.Name,
            ["description"] = m.Description,
            ["difficulty"] = Math.Clamp(m.Difficulty, 1, 5),
            ["region"] = m.StartingRegion,
            ["starting_capital"] = (double)m.StartingCapitalNOK,
            ["starting_capital_neg"] = m.StartingCapitalNOK < 0m,
            ["starting_cash"] = (double)m.StartingCapitalNOK,
            ["story"] = m.Description,
            ["tags"] = new Godot.Collections.Array<string>(),
            ["restrictions"] = new Godot.Collections.Array<string>(),
            ["win_condition"] = m.WinCondition,
            ["win"] = m.WinCondition,
            ["time_limit_days"] = 0,
            ["is_unlocked"] = true,
            ["unlocked"] = true,
            ["special_rules"] = rules,
            ["special"] = new Godot.Collections.Array<string>(),
            ["subtitle"] = "Mod",
            ["capital"] = FormatCapital(m.StartingCapitalNOK),
            ["is_mod"] = true
        };
    }

    private static string DifficultyLabel(int difficulty) => difficulty switch
    {
        1 => "Enkel",
        2 => "Lett",
        3 => "Middels",
        4 => "Vanskelig",
        _ => "Ekspert"
    };

    private static string DescribeWinCondition(ScenarioDefinition s) => s.WinCondition switch
    {
        WinCondition.Tutorial => "Fullfør opplæringen",
        WinCondition.Profit => $"Nå {FormatCapital(RuleDecimal(s, "profit_target", 5_000_000m))} i overskudd",
        WinCondition.Sustainability => $"Nå bærekraftsmål {RuleDecimal(s, "sustainability_target", 95m):0}",
        WinCondition.Survival => $"Overlev {(s.TimeLimitDays ?? 730)} dager med positiv saldo",
        WinCondition.Competition => $"Nå {FormatCapital(RuleDecimal(s, "profit_target", 10_000_000m))} før konkurrentene",
        _ => s.WinCondition.ToString()
    };

    private static decimal RuleDecimal(ScenarioDefinition s, string key, decimal fallback)
        => s.SpecialRules.TryGetValue(key, out var raw) &&
           decimal.TryParse(raw, System.Globalization.NumberStyles.Any,
               System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value : fallback;

    private static string FormatCapital(decimal amount)
        => $"{amount:N0} NOK".Replace(",", " ").Replace("\u00A0", " ");

    private static int ParseDifficultyElement(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var number))
            return Math.Clamp(number, 1, 5);
        if (el.ValueKind == JsonValueKind.String)
        {
            var raw = (el.GetString() ?? "").Trim().ToLowerInvariant();
            if (int.TryParse(raw, out var parsed)) return Math.Clamp(parsed, 1, 5);
            return raw switch { "easy" => 1, "medium" or "normal" => 3, "hard" => 5, _ => 3 };
        }
        return 3;
    }

    /// <summary>Parsed scenario JSON — union of C# and legacy Godot schemas.</summary>
    private sealed class ScenarioJsonDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")] public string Id { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("name")] public string Name { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("description")] public string Description { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("starting_balance")] public decimal? StartingBalance { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("starting_capital_nok")] public decimal? StartingCapitalNok { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("initial_balance_nok")] public decimal? InitialBalanceNok { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("starting_region")] public string StartingRegion { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("difficulty")] public JsonElement Difficulty { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("win_condition")] public string WinCondition { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("time_limit_days")] public int? TimeLimitDays { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("is_unlocked")] public bool? IsUnlocked { get; set; }
        // Extended campaign schema (additive; defaults are safe for legacy entries).
        [System.Text.Json.Serialization.JsonPropertyName("starting_cash")] public decimal? StartingCash { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("story")] public string? Story { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("tags")] public string[]? Tags { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("restrictions")] public string[]? Restrictions { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("starting_research")] public string[]? StartingResearch { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("starting_buildings")] public string[]? StartingBuildings { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("starting_sites")] public List<StartingSiteDefinition>? StartingSites { get; set; }
    }

    // ── Mod System API for GDScript ──────────────────────────────────────────

    /// <summary>Directory the ModLoader scans (user://mods preferred).</summary>
    public string ModsDirectory => _modsDirectory;

    /// <summary>
    /// Return all discovered mods as an Array of Dictionaries. Keys match
    /// ModManagerPanel.gd: "id", "name", "author", "version", "description",
    /// "enabled", "event_count", "research_count", "species_count",
    /// "scenario_count", "errors" (Array[String]).
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetModList()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        if (_modLoader == null) return result;

        foreach (var manifest in _modLoader.DiscoveredManifests)
        {
            var loaded = _modLoader.LoadedMods.FirstOrDefault(m => m.Manifest.Id == manifest.Id);

            var errors = new Godot.Collections.Array<string>();
            if (loaded != null)
                foreach (var error in loaded.Errors)
                    errors.Add(error);

            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = manifest.Id,
                ["name"] = manifest.Name,
                ["author"] = manifest.Author,
                ["version"] = manifest.Version,
                ["description"] = manifest.Description,
                ["enabled"] = manifest.Enabled,
                ["event_count"] = loaded?.Events.Count ?? 0,
                ["research_count"] = loaded?.ResearchNodes.Count ?? 0,
                ["species_count"] = loaded?.FishSpecies.Count ?? 0,
                ["scenario_count"] = loaded?.Scenarios.Count ?? 0,
                ["errors"] = errors
            });
        }
        return result;
    }

    /// <summary>Re-scan the mods directory and reload all enabled mods.</summary>
    public void ReloadMods()
    {
        _modLoader?.DiscoverAndLoad();
        EmitSignal(SignalName.GameStateUpdated);
    }

    /// <summary>
    /// Persist enable/disable state pushed from ModManagerPanel.gd. Accepts a
    /// Godot Array of {id, enabled} dictionaries (or a single dictionary),
    /// updates the mod config, and reloads.
    /// </summary>
    public bool ApplyModSettings(Godot.Variant settings)
    {
        if (_modLoader == null) return false;

        var entries = new List<(string Id, bool Enabled)>();

        if (settings.VariantType == Variant.Type.Array)
        {
            foreach (var item in settings.AsGodotArray())
            {
                if (item.VariantType != Variant.Type.Dictionary) continue;
                var dict = item.AsGodotDictionary();
                if (dict.ContainsKey("id"))
                    entries.Add((dict["id"].AsString(), dict.ContainsKey("enabled") && dict["enabled"].AsBool()));
            }
        }
        else if (settings.VariantType == Variant.Type.Dictionary)
        {
            var dict = settings.AsGodotDictionary();
            if (dict.ContainsKey("id"))
                entries.Add((dict["id"].AsString(), dict.ContainsKey("enabled") && dict["enabled"].AsBool()));
        }

        foreach (var (id, enabled) in entries)
            _modLoader.SetModEnabled(id, enabled);

        _modLoader.DiscoverAndLoad();
        EmitSignal(SignalName.GameStateUpdated);
        return true;
    }

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
    /// Stock smolts at a specific sea site (Phase 1.4: strain-aware).
    /// Strain selects release weight, cost, genome traits and lice load.
    /// Returns true on success, false if site not found or stocking failed.
    /// </summary>
    public bool StockSmolts(string siteId, int count, string strainId = "standard")
        => StockSmolts(siteId, count, strainId, Farm.SmoltQualityTier.Standard);

    /// <summary>
    /// Stock smolts with an explicit post-smolt quality tier (P5 carryover).
    /// The tier is stamped on the cohort so the sea phase applies its
    /// mortality / growth / time-to-harvest carryover multipliers.
    /// </summary>
    public bool StockSmolts(string siteId, int count, string strainId, Farm.SmoltQualityTier qualityTier)
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

        // Calculate stocking cost (hatchery rearing cost × strain multiplier × quality premium)
        var strain = LakskongenSim.Farm.SmoltStrainRegistry.Get(strainId);
        var quality = Farm.SmoltQualityRegistry.Get(qualityTier);
        decimal stockingCost = SimulationEngine.Hatchery.CalculateRearingCost(count, SimulationEngine.Hatchery.MaturationDays)
            * (decimal)strain.CostMultiplier;

        if (SimulationEngine.FinanceManager.Balance < stockingCost)
        {
            GD.PrintErr($"GameBridge: StockSmolts failed - insufficient funds (need {stockingCost:N0} NOK)");
            EmitSignal(SignalName.FarmActionCompleted, "stock_smolts", siteId, false);
            return false;
        }

        // Create a new fish population with strain smolts
        var population = new LakskongenSim.Biology.FishPopulation
        {
            ZoneId = site.ZoneId,
            SeaLiceLoad = strain.InitialLiceLoad
        };
        population.InjectDependencies(SimulationEngine.Growth, SimulationEngine.Mortality);
        
        var smoltCohort = new LakskongenSim.Biology.FishPopulation.Cohort(
            SimulationEngine.Hatchery.MaturationDays,
            count,
            strain.ReleaseWeightKg,
            LakskongenSim.Data.LifeStage.Smolt,
            strain.BuildGenome()
        )
        {
            Quality = quality.Tier
        };
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
            Description = $"Stocked {count:N0} {quality.Name} {strain.Name} smolts ({strain.ReleaseWeightKg * 1000:F0}g) at {siteId}",
            BalanceChange = -stockingCost
        });

        EmitSignal(SignalName.FarmActionCompleted, "stock_smolts", siteId, true);
        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: Stocked {count:N0} {quality.Name} {strain.Name} smolts at '{siteId}' for {stockingCost:N0} NOK");
        return true;
    }

    /// <summary>
    /// Buyable smolt strains for the stocking dialog (Phase 1.4).
    /// Keys: "id", "name", "description", "weight_g", "cost_multiplier",
    ///       "growth_pct", "disease_pct", "initial_lice".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetSmoltStrains()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var s in LakskongenSim.Farm.SmoltStrainRegistry.All)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"]              = s.Id,
                ["name"]            = s.Name,
                ["description"]     = s.Description,
                ["weight_g"]        = s.ReleaseWeightKg * 1000.0,
                ["cost_multiplier"] = s.CostMultiplier,
                ["growth_pct"]      = s.GrowthTrait * 15.0,
                ["disease_pct"]     = s.DiseaseTrait * 20.0,
                ["initial_lice"]    = s.InitialLiceLoad
            });
        }
        return result;
    }

    /// <summary>
    /// Post-smolt quality tiers for the stocking dialog (P5 carryover).
    /// Each entry: "id" (tier name), "name", "mortality_mult", "growth_mult",
    /// "days_mult", "quality" (0-1). Array order matches the enum order, so the
    /// UI's selected index equals the tier value passed to StockSmolts.
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetSmoltQualityTiers()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var q in Farm.SmoltQualityRegistry.All)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"]             = q.Tier.ToString(),
                ["name"]           = q.Name,
                ["mortality_mult"] = q.MortalityMultiplier,
                ["growth_mult"]    = q.GrowthMultiplier,
                ["days_mult"]      = q.TimeToHarvestMultiplier,
                ["quality"]        = q.Quality01
            });
        }
        return result;
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

        // Harvest ONLY this site's pens (site.Pens identity). Populations
        // carry ZoneId (shared by every site in a zone), so filtering
        // State.Populations by zone would wipe neighbouring sites too.
        double totalBiomassKg = 0.0;
        int totalFishCount = 0;
        var populations = site.Pens.ToList();

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

        // Calculate revenue: oldest contracts first at locked prices,
        // remainder at spot — all scaled by certification multiplier.
        // Partial deliveries accrue no penalty (expiry settles shortfalls).
        decimal revenue = 0m;
        double remainingKg = totalBiomassKg;
        var cs = SimulationEngine.ContractSystem;
        if (cs != null)
        {
            foreach (var c in cs.ActiveContracts.OrderBy(c => c.SignedAtWeek).ToList())
            {
                if (remainingKg <= 0.1) break;
                int deliver = (int)System.Math.Min(remainingKg, c.RemainingKg);
                if (deliver <= 0) continue;
                var (payment, _) = cs.FulfillContract(c.Id, deliver);
                revenue += payment;
                remainingKg -= deliver;
            }
        }
        decimal spotPrice = SimulationEngine.MarketSimulation.PricePerKg;
        revenue += (decimal)remainingKg * spotPrice;
        decimal certMultiplier = SimulationEngine.SustainabilityManager?.GetPriceMultiplier(
            SimulationEngine.SustainabilityManager.CurrentCertification) ?? 1.0m;
        revenue *= certMultiplier;
        // VAP fillet line (Phase 3.5): +12% when a plant is owned.
        // Phase 4.4: grade-C recipe (pale) halves the premium to +6%.
        bool vap = SimulationEngine.State.OwnsProcessingPlant;
        if (vap)
        {
            string grade = LakskongenSim.Processing.FeedRecipe.Grade(
                SimulationEngine.State.RecipeProteinPct, SimulationEngine.State.RecipeAstaxPpm);
            revenue *= LakskongenSim.Processing.FeedRecipe.VapMult(grade);
            if (grade == "C")
                SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
                {
                    Day = SimulationEngine.State.Day,
                    Name = "Blek filet (C)",
                    Category = LakskongenSim.Events.EventCategory.Market,
                    Description = "Lav astaxanthin i fôret — VAP-premie halvert til +6%",
                    BalanceChange = 0m
                });
        }

        // Apply harvest
        SimulationEngine.FinanceManager.AddRevenue(revenue);

        // P15: player-supply market impact — the harvested tonnage moves the
        // market (accumulates downward price pressure, decays over days).
        SimulationEngine.MarketSimulation?.ApplyPlayerHarvest(totalBiomassKg / 1000.0);

        // Remove exactly the harvested populations (this site only)
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

    // ── Insurance API (P11) ──────────────────────────────────────────────────

    private static readonly InsuranceKind[] InsuranceKinds =
    {
        InsuranceKind.Mortality, InsuranceKind.Bloom, InsuranceKind.Storm, InsuranceKind.ISA
    };

    /// <summary>
    /// Annual premium (NOK) at 100% coverage per catastrophe kind. Premiums are
    /// scaled linearly by the requested coverage fraction; the deductible is
    /// retained by the insured before cover applies (see <see cref="InsurancePolicy"/>).
    /// </summary>
    private static decimal BaseAnnualPremiumNok(InsuranceKind kind) => kind switch
    {
        InsuranceKind.Mortality => 120_000m,
        InsuranceKind.Bloom => 250_000m,
        InsuranceKind.Storm => 180_000m,
        InsuranceKind.ISA => 300_000m,
        _ => 0m
    };

    private static string InsuranceKindKey(InsuranceKind kind) => kind.ToString().ToLowerInvariant();

    private static bool TryParseInsuranceKind(string kind, out InsuranceKind parsed) =>
        Enum.TryParse(kind, ignoreCase: true, out parsed);

    /// <summary>
    /// Per-kind insurance status for the UI:
    /// <c>{ "mortality"|"bloom"|"storm"|"isa": { enabled, premium, coverage, deductible }, "total_premium" }</c>.
    /// Premiums are summed across stacked policies of a kind; coverage/deductible
    /// report the primary (first) policy of that kind.
    /// </summary>
    public Godot.Collections.Dictionary GetInsuranceStatus()
    {
        var result = new Godot.Collections.Dictionary();
        decimal totalPremium = 0m;

        var insurance = SimulationEngine?.Insurance;
        if (insurance == null)
        {
            result["total_premium"] = 0.0;
            return result;
        }

        foreach (var kind in InsuranceKinds)
        {
            var policies = insurance.Policies.Where(p => p.Kind == kind).ToList();
            var primary = policies.Count > 0 ? policies[0] : null;
            decimal premium = policies.Sum(p => p.PremiumNokPerYear);
            totalPremium += premium;

            result[InsuranceKindKey(kind)] = new Godot.Collections.Dictionary
            {
                ["enabled"] = policies.Count > 0,
                ["premium"] = (double)premium,
                ["coverage"] = primary?.CoverageFraction ?? 0.0,
                ["deductible"] = primary != null ? (double)primary.DeductibleNok : 0.0
            };
        }

        result["total_premium"] = (double)totalPremium;
        return result;
    }

    /// <summary>
    /// Enable/disable a catastrophe policy. <paramref name="kind"/> is one of
    /// "mortality" | "bloom" | "storm" | "isa" (case-insensitive). Replaces any
    /// existing policy of that kind while preserving all other kinds. Returns
    /// false for an unknown kind or an uninitialized engine.
    /// </summary>
    public bool SetInsurancePolicy(string kind, bool enabled, double coverage, double deductible)
    {
        var insurance = SimulationEngine?.Insurance;
        if (insurance == null) return false;
        if (!TryParseInsuranceKind(kind, out var parsed)) return false;

        double clampedCoverage = System.Math.Clamp(coverage, 0.0, 1.0);
        decimal ded = deductible > 0.0 ? (decimal)deductible : 0m;

        // InsuranceSystem exposes no per-kind removal, so rebuild: keep every
        // other kind's policies, then re-add this kind if enabled.
        var survivors = insurance.Policies.Where(p => p.Kind != parsed).ToList();
        insurance.ClearPolicies();
        foreach (var policy in survivors)
            insurance.Subscribe(policy);

        if (enabled && clampedCoverage > 0.0)
        {
            decimal premium = BaseAnnualPremiumNok(parsed) * (decimal)clampedCoverage;
            insurance.Subscribe(new InsurancePolicy(parsed, premium, clampedCoverage, ded));
        }

        EmitSignal(SignalName.GameStateUpdated);
        return true;
    }

    // ── Bloom mitigation API (P11 mitigation UI) ────────────────────────────

    private static readonly Farm.MitigationKind[] BloomMitigationKinds =
    {
        Farm.MitigationKind.BubbleCurtain,
        Farm.MitigationKind.O2Diffuser,
        Farm.MitigationKind.Pumping,
        Farm.MitigationKind.DeepFeeding
    };

    /// <summary>
    /// In-memory registry of non-flag-backed mitigation installs (pumping and
    /// deep feeding). Bubble curtain / O2 diffuser persist on the
    /// <see cref="Farm.SeaSite"/> flags; the remaining kinds have no modelled
    /// site field, so the bridge tracks them here for the UI only, without
    /// touching SimulationEngine or the site model.
    /// </summary>
    private readonly Dictionary<string, HashSet<Farm.MitigationKind>> _installedMitigations =
        new(StringComparer.OrdinalIgnoreCase);

    private static string MitigationKindKey(Farm.MitigationKind kind) => kind switch
    {
        Farm.MitigationKind.BubbleCurtain => "bubble",
        Farm.MitigationKind.O2Diffuser => "diffuser",
        Farm.MitigationKind.Pumping => "pumping",
        Farm.MitigationKind.DeepFeeding => "deep_feeding",
        _ => "none"
    };

    private static string MitigationKindLabel(Farm.MitigationKind kind) => kind switch
    {
        Farm.MitigationKind.BubbleCurtain => "Boblegardin",
        Farm.MitigationKind.O2Diffuser => "O2-diffuser",
        Farm.MitigationKind.Pumping => "Dypvannspumping",
        Farm.MitigationKind.DeepFeeding => "Dypfôring",
        _ => "Ingen"
    };

    private static string MitigationBestFor(Farm.MitigationKind kind) => kind switch
    {
        Farm.MitigationKind.BubbleCurtain => "jellyfish",
        Farm.MitigationKind.O2Diffuser => "algae/hypoxia",
        Farm.MitigationKind.Pumping => "hypoxia",
        Farm.MitigationKind.DeepFeeding => "algae",
        _ => ""
    };

    private static bool TryParseMitigationKind(string kind, out Farm.MitigationKind parsed)
    {
        switch ((kind ?? "").Trim().ToLowerInvariant())
        {
            case "bubble":
            case "bubblecurtain":
            case "bubble_curtain":
                parsed = Farm.MitigationKind.BubbleCurtain; return true;
            case "diffuser":
            case "o2":
            case "o2diffuser":
            case "o2_diffuser":
                parsed = Farm.MitigationKind.O2Diffuser; return true;
            case "pumping":
            case "pump":
                parsed = Farm.MitigationKind.Pumping; return true;
            case "deep_feeding":
            case "deepfeeding":
            case "deep_feed":
                parsed = Farm.MitigationKind.DeepFeeding; return true;
            default:
                parsed = Farm.MitigationKind.None; return false;
        }
    }

    private bool IsBloomMitigationInstalled(Farm.SeaSite site, Farm.MitigationKind kind)
    {
        if (site == null) return false;
        if (kind == Farm.MitigationKind.BubbleCurtain || kind == Farm.MitigationKind.O2Diffuser)
            return Farm.BloomMitigation.IsInstalled(kind, site);
        return _installedMitigations.TryGetValue(site.Id, out var set) && set.Contains(kind);
    }

    /// <summary>
    /// P11 persistence: mirror the in-memory mitigation registry into
    /// <see cref="GameState.BloomMitigations"/> so it is serialized on save.
    /// Flag-backed kinds (bubble curtain / O2 diffuser) persist on the site
    /// model and are intentionally not duplicated here.
    /// </summary>
    private void SyncMitigationsToState()
    {
        var state = SimulationEngine?.State;
        if (state == null) return;
        state.BloomMitigations ??= new List<BloomMitigationState>();
        state.BloomMitigations.Clear();
        foreach (var kvp in _installedMitigations)
        {
            foreach (var kind in kvp.Value)
            {
                if (kind == Farm.MitigationKind.BubbleCurtain || kind == Farm.MitigationKind.O2Diffuser)
                    continue;
                state.BloomMitigations.Add(new BloomMitigationState
                {
                    SiteId = kvp.Key,
                    Kind = MitigationKindKey(kind)
                });
            }
        }
    }

    /// <summary>
    /// P11 persistence: rebuild the in-memory mitigation registry from a saved
    /// <see cref="GameState.BloomMitigations"/> list. Backward-safe: null list
    /// or unknown kinds are ignored; flag-backed kinds are skipped (site flags
    /// are the source of truth for them).
    /// </summary>
    private void RestoreMitigationsFromState(List<BloomMitigationState>? saved)
    {
        _installedMitigations.Clear();
        if (saved == null) return;

        foreach (var entry in saved)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.SiteId)) continue;
            if (!TryParseMitigationKind(entry.Kind, out var kind) || kind == Farm.MitigationKind.None) continue;
            if (kind == Farm.MitigationKind.BubbleCurtain || kind == Farm.MitigationKind.O2Diffuser) continue;

            if (!_installedMitigations.TryGetValue(entry.SiteId, out var set))
            {
                set = new HashSet<Farm.MitigationKind>();
                _installedMitigations[entry.SiteId] = set;
            }
            set.Add(kind);
        }
    }

    /// <summary>
    /// Bloom-mitigation catalog for the UI: one entry per kind with its fixed
    /// deployment cost (NOK), peak mortality reduction (fraction), intended
    /// bloom kind and the sites where it is currently installed.
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetBloomMitigations()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var sites = SimulationEngine?.State?.SeaSites;
        foreach (var kind in BloomMitigationKinds)
        {
            var installed = new Godot.Collections.Array<string>();
            if (sites != null)
            {
                foreach (var s in sites)
                    if (IsBloomMitigationInstalled(s, kind)) installed.Add(s.Id);
            }
            result.Add(new Godot.Collections.Dictionary
            {
                ["kind"] = MitigationKindKey(kind),
                ["label"] = MitigationKindLabel(kind),
                ["fixed_cost"] = (double)Farm.BloomMitigation.FixedCostNok(kind),
                ["opex_at_full"] = (double)Farm.BloomMitigation.OpexPerSeverityNok(kind),
                ["max_reduction"] = Farm.BloomMitigation.MaxReduction(kind),
                ["best_for"] = MitigationBestFor(kind),
                ["installed_sites"] = installed
            });
        }
        return result;
    }

    /// <summary>
    /// Install (or toggle off) a bloom mitigation at a site. Bubble curtain and O2
    /// diffuser delegate to <see cref="DeployCountermeasure"/> so their existing
    /// costs and site flags are preserved; pumping and deep feeding are tracked by
    /// the bridge at their <see cref="Farm.BloomMitigation"/> fixed cost. Returns
    /// false for an unknown site/kind or when funds are insufficient.
    /// </summary>
    public bool InstallBloomMitigation(string siteId, string kind)
    {
        var site = SimulationEngine?.State?.SeaSites?.FirstOrDefault(s => s.Id == siteId);
        if (site == null) return false;
        if (!TryParseMitigationKind(kind, out var parsed) || parsed == Farm.MitigationKind.None) return false;

        if (parsed == Farm.MitigationKind.BubbleCurtain || parsed == Farm.MitigationKind.O2Diffuser)
            return DeployCountermeasure(siteId, MitigationKindKey(parsed));

        bool installed = IsBloomMitigationInstalled(site, parsed);
        if (!installed)
        {
            decimal cost = Farm.BloomMitigation.FixedCostNok(parsed);
            if (SimulationEngine!.FinanceManager.Balance < cost)
            {
                EmitSignal(SignalName.FarmActionCompleted, "countermeasure", siteId, false);
                return false;
            }
            SimulationEngine.FinanceManager.Spend(cost);
            if (!_installedMitigations.TryGetValue(site.Id, out var set))
            {
                set = new HashSet<Farm.MitigationKind>();
                _installedMitigations[site.Id] = set;
            }
            set.Add(parsed);
            SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
            {
                Day = SimulationEngine.State.Day,
                Name = $"{MitigationKindLabel(parsed)} installert",
                Category = LakskongenSim.Events.EventCategory.Equipment,
                Description = $"{MitigationKindLabel(parsed)} ved {siteId}",
                BalanceChange = -cost
            });
        }
        else
        {
            _installedMitigations[site.Id].Remove(parsed);
        }

        // P11 persistence: keep the serializable mirror in lock-step.
        SyncMitigationsToState();

        EmitSignal(SignalName.FarmActionCompleted, "countermeasure", siteId, true);
        EmitSignal(SignalName.GameStateUpdated);
        return true;
    }

    // ── P8 permeable-barrier API (Smoltscreen) ──────────────────────────────

    private static string BarrierKindKey(Farm.BarrierKind kind) => kind switch
    {
        Farm.BarrierKind.Smoltscreen => "smoltscreen",
        _ => "none"
    };

    private static bool TryParseBarrierKind(string kind, out Farm.BarrierKind parsed)
    {
        switch ((kind ?? "").Trim().ToLowerInvariant())
        {
            case "smoltscreen":
            case "smolt_screen":
            case "smolt-screen":
                parsed = Farm.BarrierKind.Smoltscreen; return true;
            case "none":
            case "remove":
            case "":
                parsed = Farm.BarrierKind.None; return true;
            default:
                parsed = Farm.BarrierKind.None; return false;
        }
    }

    /// <summary>P8: approximate tidal flow (m/s) at a site from its zone.</summary>
    private static double SiteFlowMs(SimulationEngine engine, Farm.SeaSite site)
    {
        var zone = engine?.State?.Zones?.FirstOrDefault(z => z.Id == site.ZoneId);
        return zone?.CurrentSpeed ?? Farm.BarrierTech.DefaultFlowMs;
    }

    /// <summary>
    /// Install (or toggle off) a permeable barrier at a site. On install the
    /// barrier's capex is charged; installing when the same kind is already
    /// present removes it (no refund), mirroring the mitigation toggle. Returns
    /// false for an unknown site/kind or when funds are insufficient.
    /// </summary>
    public bool InstallBarrier(string siteId, string kind)
    {
        var site = SimulationEngine?.State?.SeaSites?.FirstOrDefault(s => s.Id == siteId);
        if (site == null) return false;
        if (!TryParseBarrierKind(kind, out var parsed)) return false;

        // Toggle off when the same barrier is already installed.
        if (parsed == site.Barrier)
        {
            site.Barrier = Farm.BarrierKind.None;
            site.BarrierDaysSinceCleaning = 0;
            EmitSignal(SignalName.FarmActionCompleted, "install_barrier", siteId, true);
            EmitSignal(SignalName.GameStateUpdated);
            return true;
        }

        // Explicit "none"/"remove" with no barrier installed is a no-op success.
        if (parsed == Farm.BarrierKind.None)
        {
            site.Barrier = Farm.BarrierKind.None;
            site.BarrierDaysSinceCleaning = 0;
            EmitSignal(SignalName.FarmActionCompleted, "install_barrier", siteId, true);
            EmitSignal(SignalName.GameStateUpdated);
            return true;
        }

        decimal cost = Farm.BarrierTech.SpecFor(parsed).CapexNok;
        if (SimulationEngine!.FinanceManager.Balance < cost)
        {
            EmitSignal(SignalName.FarmActionCompleted, "install_barrier", siteId, false);
            return false;
        }

        SimulationEngine.FinanceManager.Spend(cost);
        site.Barrier = parsed;
        site.BarrierDaysSinceCleaning = 0;

        SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = SimulationEngine.State.Day,
            Name = $"{Farm.BarrierTech.SpecFor(parsed).DisplayName} installert",
            Category = LakskongenSim.Events.EventCategory.Equipment,
            Description = $"{Farm.BarrierTech.SpecFor(parsed).DisplayName} ved {siteId} ({cost:N0} NOK)",
            BalanceChange = -cost
        });

        EmitSignal(SignalName.FarmActionCompleted, "install_barrier", siteId, true);
        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: {siteId} barrier → {parsed} ({cost:N0} NOK)");
        return true;
    }

    /// <summary>
    /// P8: clean an installed barrier, resetting biofouling decay. Charges the
    /// barrier's standing (weekly) opex as a one-off cleaning service. Returns
    /// false when no barrier is installed or funds are insufficient.
    /// </summary>
    public bool CleanBarrier(string siteId)
    {
        var site = SimulationEngine?.State?.SeaSites?.FirstOrDefault(s => s.Id == siteId);
        if (site == null || site.Barrier == Farm.BarrierKind.None) return false;

        decimal cost = Farm.BarrierTech.SpecFor(site.Barrier).WeeklyOpexNok;
        if (SimulationEngine!.FinanceManager.Balance < cost) return false;
        SimulationEngine.FinanceManager.Spend(cost);
        site.BarrierDaysSinceCleaning = 0;

        SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = SimulationEngine.State.Day,
            Name = "Barriere rengjort",
            Category = LakskongenSim.Events.EventCategory.Equipment,
            Description = $"{Farm.BarrierTech.SpecFor(site.Barrier).DisplayName} rengjort ved {siteId}",
            BalanceChange = -cost
        });

        EmitSignal(SignalName.FarmActionCompleted, "clean_barrier", siteId, true);
        EmitSignal(SignalName.GameStateUpdated);
        return true;
    }

    /// <summary>
    /// P8: per-site permeable-barrier status. Keys: "site_id", "kind",
    /// "installed", "flow_ms", "lice_blocking", "jellyfish_blocking",
    /// "days_since_cleaning", "cleaning_due_in_days", "capex_nok",
    /// "weekly_opex_nok", "eligible_environmental_flexibility", "rationale".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetBarrierStatus()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var sites = SimulationEngine?.State?.SeaSites;
        if (sites == null) return result;

        foreach (var site in sites)
        {
            var spec = Farm.BarrierTech.SpecFor(site.Barrier);
            double flow = SiteFlowMs(SimulationEngine, site);
            int days = site.BarrierDaysSinceCleaning;
            int dueIn = site.Barrier != Farm.BarrierKind.None
                ? spec.CleaningIntervalDays - days
                : 0;
            result.Add(new Godot.Collections.Dictionary
            {
                ["site_id"] = site.Id,
                ["kind"] = BarrierKindKey(site.Barrier),
                ["installed"] = site.Barrier != Farm.BarrierKind.None,
                ["flow_ms"] = flow,
                ["lice_blocking"] = Farm.BarrierTech.EffectiveLiceBlocking(site.Barrier, flow, days),
                ["jellyfish_blocking"] = Farm.BarrierTech.EffectiveJellyfishBlocking(site.Barrier, flow, days),
                ["days_since_cleaning"] = days,
                ["cleaning_due_in_days"] = dueIn,
                ["capex_nok"] = (double)spec.CapexNok,
                ["weekly_opex_nok"] = (double)spec.WeeklyOpexNok,
                ["eligible_environmental_flexibility"] = Farm.BarrierTech.IsEligibleForEnvironmentalFlexibility(site.Barrier),
                ["rationale"] = Farm.BarrierTech.EnvironmentalFlexibilityRationale(site.Barrier)
            });
        }
        return result;
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
    /// Active blooms for UI (Phase 4.3).
    /// Keys: "id", "kind", "site_id", "severity", "days_left".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetActiveBlooms()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var blooms = SimulationEngine?.State?.ActiveBlooms;
        if (blooms == null) return result;
        int today = SimulationEngine?.State?.Day ?? 0;
        foreach (var b in blooms)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = b.Id,
                ["kind"] = b.Kind,
                ["site_id"] = b.SiteId,
                ["severity"] = b.Severity01,
                ["days_left"] = b.DaysLeft(today)
            });
        }
        return result;
    }

    /// <summary>
    /// Deploy a bloom countermeasure (Phase 4.3): "bubble" (1.8M + 90k/wk,
    /// jelly ×0.23) or "diffuser" (2.4M + 120k/wk, hypoxia ×0.3, algae ×0.7).
    /// Toggling an active measure OFF is free.
    /// </summary>
    public bool DeployCountermeasure(string siteId, string kind)
    {
        var site = SimulationEngine?.State?.SeaSites?.FirstOrDefault(s => s.Id == siteId);
        if (site == null) return false;
        bool isBubble = (kind ?? "").ToLower() == "bubble";
        if (!isBubble && (kind ?? "").ToLower() != "diffuser") return false;
        bool active = isBubble ? site.HasBubbleCurtain : site.HasO2Diffuser;
        if (!active)
        {
            decimal cost = isBubble ? 1_800_000m : 2_400_000m;
            if (SimulationEngine!.FinanceManager.Balance < cost)
            {
                EmitSignal(SignalName.FarmActionCompleted, "countermeasure", siteId, false);
                return false;
            }
            SimulationEngine.FinanceManager.Spend(cost);
            if (isBubble) site.HasBubbleCurtain = true; else site.HasO2Diffuser = true;
            SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
            {
                Day = SimulationEngine.State.Day,
                Name = isBubble ? "Boblegardin installert" : "O2-diffuser installert",
                Category = LakskongenSim.Events.EventCategory.Equipment,
                Description = $"{(isBubble ? "Boblegardin" : "O2-diffuser")} ved {siteId}",
                BalanceChange = -cost
            });
        }
        else if (isBubble) site.HasBubbleCurtain = false;
        else site.HasO2Diffuser = false;

        EmitSignal(SignalName.FarmActionCompleted, "countermeasure", siteId, true);
        EmitSignal(SignalName.GameStateUpdated);
        return true;
    }
    /// <summary>Vaccinated share of live fish 0-1 (Phase 5.1).</summary>
    public double VaccineCoverage
    {
        get
        {
            var state = SimulationEngine?.State;
            return state != null ? LakskongenSim.Health.VaccinationProgram.Coverage01(state) : 0.0;
        }
    }

    /// <summary>October window days left, -1 when closed (Phase 5.1).</summary>
    public int VaccineWindowDaysLeft =>
        LakskongenSim.Health.VaccinationProgram.WindowDaysLeft(SimulationEngine?.State?.Day ?? 0);

    // ── Intervention advisor (Phase 5.3) ──

    /// <summary>
    /// Ranked intervention suggestions from twin-lite projection (Phase 5.3).
    /// Keys: "id", "action", "site_id", "eta_days", "delta_nok", "confidence", "reasoning".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetInterventionRanking()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var engine = SimulationEngine;
        if (engine?.State == null || engine.Growth == null || engine.SeaLice == null || engine.Analytics == null) return result;

        var state = engine.State;
        double tempC = engine.Weather?.CurrentTemperatureC ?? 10.0;
        var forecast = LakskongenSim.Optimization.ForecastTwin.Project(
            state, engine.Growth, engine.SeaLice, tempC, engine.Analytics);
        double currentLice = engine.SeaLice.AverageLicePerFish;
        double gillScore = 0.0;
        if (state.SeaSites.Count > 0)
            gillScore = state.SeaSites.Average(s => s.GillScore);
        double feedFcr = engine.Feed?.CurrentFCR ?? 1.15;
        var contracts = engine.ContractSystem?.ActiveContracts ?? new List<LakskongenSim.Market.Contract>();
        int crewCount = state.Crew?.Count ?? 0;

        var suggestions = LakskongenSim.Advisor.InterventionAdvisor.Rank(
            state, forecast, currentLice, gillScore, feedFcr, contracts,
            crewCount, state.Day);

        foreach (var s in suggestions)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = s.Id,
                ["action"] = s.Action,
                ["site_id"] = s.SiteId,
                ["eta_days"] = s.DaysOut,
                ["delta_nok"] = (double)s.DeltaNok,
                ["confidence"] = s.Confidence,
                ["reasoning"] = s.Reasoning
            });
        }
        return result;
    }

    // ── Night-shift ROC policy (Phase 5.2) ──

    /// <summary>Current night-shift policy (Standard/ScanOnly/AutomatedROC).</summary>
    public string NightShiftPolicy => SimulationEngine?.State?.NightShiftPolicy.ToString() ?? "Standard";

    /// <summary>
    /// Set night-shift ROC policy (Phase 5.2).
    /// Standard / ScanOnly / AutomatedROC. Returns true on success.
    /// </summary>
    public bool SetNightShiftPolicy(string policyName)
    {
        if (!System.Enum.TryParse<LakskongenSim.Logistics.NightShiftPolicy>(policyName, true, out var policy))
            return false;
        var state = SimulationEngine?.State;
        if (state == null) return false;
        state.NightShiftPolicy = policy;
        EmitSignal(SignalName.GameStateUpdated);
        return true;
    }

    /// <summary>Night-shift policy premium / detection / fatigue scalars.</summary>
    public Godot.Collections.Dictionary GetNightShiftStatus()
    {
        var state = SimulationEngine?.State;
        var policy = state?.NightShiftPolicy ?? LakskongenSim.Logistics.NightShiftPolicy.Standard;
        return new Godot.Collections.Dictionary
        {
            ["policy"] = policy.ToString(),
            ["premium_monthly"] = (double)LakskongenSim.Logistics.NightShiftResolver.MonthlyCost(
                (LakskongenSim.Logistics.NightShiftPolicy)policy),
            ["detection_chance"] = LakskongenSim.Logistics.NightShiftResolver.DetectionChance(
                (LakskongenSim.Logistics.NightShiftPolicy)policy),
            ["fatigue_per_night"] = LakskongenSim.Logistics.NightShiftResolver.FatigueDelta(
                (LakskongenSim.Logistics.NightShiftPolicy)policy),
            ["misfire_chance"] = LakskongenSim.Logistics.NightShiftResolver.MisfireChance(
                (LakskongenSim.Logistics.NightShiftPolicy)policy,
                state?.Crew?.Count > 0 ? (int)(state.Crew.Average(c => c.Morale)) : 5)
        };
    }

    /// <summary>
    /// Vaccinate a site against ulcer/CMS (Phase 5.1). October window only,
    /// 3.2 NOK/fish + 0.6% handling loss (vet halves it), 12-mo protection.
    /// </summary>
    public bool VaccinateSite(string siteId)
    {
        var state = SimulationEngine?.State;
        var site = state?.SeaSites?.FirstOrDefault(s => s.Id == siteId);
        if (site == null) return false;
        if (!LakskongenSim.Health.VaccinationProgram.IsWindowOpen(state!.Day))
        {
            GD.Print($"GameBridge: VaccinateSite refused — October window closed (day {state.Day})");
            EmitSignal(SignalName.FarmActionCompleted, "vaccinate", siteId, false);
            return false;
        }
        int fish = site.Pens.SelectMany(p => p.Cohorts).Sum(c => c.Count);
        if (fish <= 0) return false;
        decimal cost = fish * LakskongenSim.Health.VaccinationProgram.CostPerFishNok;
        if (SimulationEngine!.FinanceManager.Balance < cost)
        {
            EmitSignal(SignalName.FarmActionCompleted, "vaccinate", siteId, false);
            return false;
        }
        bool vet = LakskongenSim.Core.CrewEffects.HasRole(state, "veterinaer");
        double lossRate = LakskongenSim.Health.VaccinationProgram.HandlingLoss * (vet ? 0.5 : 1.0);
        int lost = 0;
        foreach (var pop in site.Pens)
        {
            foreach (var cohort in pop.Cohorts)
            {
                int take = (int)(cohort.Count * lossRate);
                cohort.Count = System.Math.Max(0, cohort.Count - take);
                lost += take;
            }
            pop.VaccinatedDay = state.Day;
        }
        SimulationEngine.FinanceManager.Spend(cost);
        state.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = state.Day,
            Name = "Vaksinering",
            Category = LakskongenSim.Events.EventCategory.Disease,
            Description = $"{siteId} vaksinert mot sår/CMS ({fish:N0} fisk, {lost:N0} håndteringstap)",
            BalanceChange = -cost
        });
        EmitSignal(SignalName.FarmActionCompleted, "vaccinate", siteId, true);
        EmitSignal(SignalName.GameStateUpdated);
        return true;
    }

    /// <summary>
    /// Toggle a preventive mitigation at a site (Phase 1.5).
    /// kind: "skirt" (500k install, 2k/day, -settlement) or
    ///       "deterrent" (200k install, 1k/day, seal protection).
    /// Installing charges once; toggling an active mitigation OFF is free.
    /// </summary>
    public bool ToggleMitigation(string siteId, string kind)
    {
        var site = SimulationEngine?.State?.SeaSites?.FirstOrDefault(s => s.Id == siteId);
        if (site == null)
        {
            EmitSignal(SignalName.FarmActionCompleted, "mitigation", siteId, false);
            return false;
        }
        bool isSkirt = (kind ?? "").ToLower() == "skirt";
        bool active = isSkirt ? site.HasLiceSkirt : site.HasSealDeterrent;
        if (!active)
        {
            decimal cost = isSkirt ? 500_000m : 200_000m;
            if (SimulationEngine!.FinanceManager.Balance < cost)
            {
                EmitSignal(SignalName.FarmActionCompleted, "mitigation", siteId, false);
                return false;
            }
            SimulationEngine.FinanceManager.Spend(cost);
            if (isSkirt) site.HasLiceSkirt = true; else site.HasSealDeterrent = true;
            SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
            {
                Day = SimulationEngine.State.Day,
                Name = isSkirt ? "Lice Skirt Installed" : "Seal Deterrent Installed",
                Category = LakskongenSim.Events.EventCategory.Equipment,
                Description = $"{(isSkirt ? "Luseskjørt" : "Seldeterrent")} installert ved {siteId}",
                BalanceChange = -cost
            });
        }
        else if (isSkirt) site.HasLiceSkirt = false;
        else site.HasSealDeterrent = false;

        EmitSignal(SignalName.FarmActionCompleted, "mitigation", siteId, true);
        EmitSignal(SignalName.GameStateUpdated);
        return true;
    }
    ///       "biomass_kg" (double), "max_biomass" (double), "escapes_this_year" (int).
    /// </summary>
    /// <summary>
    /// Return detailed information about all sea sites as an Array of Dictionaries.
    /// Keys: "id", "name", "zone", "fish_count", "biomass_kg", "max_biomass",
    ///       "escapes_this_year", "net_integrity", "has_skirt", "has_deterrent".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetSeaSiteDetails()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var sites = SimulationEngine?.State?.SeaSites;
        if (sites == null) return result;

        foreach (var site in sites)
        {
            // Per-site truth lives in site.Pens: populations carry the
            // shared ZoneId, never the site Id, so State-wide filtering
            // by either key misattributes biomass across sites.
            int fishCount = site.Pens.SelectMany(p => p.Cohorts).Sum(c => c.Count);
            double biomassKg = site.Pens.SelectMany(p => p.Cohorts).Sum(c => c.Count * (double)c.MeanWeightKg);

            result.Add(new Godot.Collections.Dictionary
            {
                ["id"]                = site.Id,
                ["name"]              = site.Id,
                ["zone"]              = site.ZoneId,
                ["fish_count"]        = fishCount,
                ["biomass_kg"]        = biomassKg,
                ["max_biomass"]       = site.MABTonnes * 1000,
                ["escapes_this_year"] = site.EscapesThisYear,
                ["net_integrity"]     = site.NetIntegrityPct,
                ["has_skirt"]         = site.HasLiceSkirt,
                ["has_deterrent"]     = site.HasSealDeterrent,
                ["containment"]       = site.Containment.ToString(),
                ["watch_mode"]        = site.WatchMode,
                ["gill_score"]        = site.GillScore,
                ["has_bubble"]        = site.HasBubbleCurtain,
                ["has_diffuser"]      = site.HasO2Diffuser,
                // P8: permeable barrier (Smoltscreen) state + flow-dependent effectiveness.
                ["barrier"]           = site.Barrier.ToString(),
                ["has_barrier"]       = site.Barrier != Farm.BarrierKind.None,
                ["barrier_days_since_cleaning"] = site.BarrierDaysSinceCleaning,
                ["barrier_lice_blocking"] = Farm.BarrierTech.EffectiveLiceBlocking(
                    site.Barrier, SiteFlowMs(SimulationEngine, site), site.BarrierDaysSinceCleaning),
                ["barrier_jellyfish_blocking"] = Farm.BarrierTech.EffectiveJellyfishBlocking(
                    site.Barrier, SiteFlowMs(SimulationEngine, site), site.BarrierDaysSinceCleaning)
            });
        }
        return result;
    }

    /// <summary>
    /// Order a delousing treatment (Phase 2.2: full treatment cards).
    /// Methods: thermolicer, hydrolicer, cleanerfish, laser, freshwater,
    /// peroxide, medicatedfeed, skirt + legacy aliases mechanical/thermal/
    /// chemical/biological. Delegates to DelousingSystem profiles (efficacy,
    /// mortality, welfare, resistance) per population.
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

        // Map UI method names (incl. legacy) to DelousingMethod.
        if (!TryParseDelousingMethod(method, out var delousingMethod))
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

        // Calculate total fish count for cost (per-site pens — ZoneId is
        // zone-shared, so State-wide filtering would double-count sites
        // in the same zone).
        int totalFish = 0;
        foreach (var site in targetSites)
        {
            totalFish += site.Pens.SelectMany(p => p.Cohorts).Sum(c => c.Count);
        }

        // Profile-driven cost/efficacy (single source of truth).
        var profile = LakskongenSim.Farm.DelousingSystem.GetProfile(delousingMethod);
        decimal totalCost = totalFish * profile.CostPerFish;

        if (SimulationEngine.FinanceManager.Balance < totalCost)
        {
            GD.PrintErr($"GameBridge: OrderDelousing failed - insufficient funds (need {totalCost:N0} NOK)");
            EmitSignal(SignalName.FarmActionCompleted, "delousing", siteId, false);
            return false;
        }

        // Apply per-population via DelousingSystem (profile efficacy with
        // resistance, handling mortality, welfare). Veterinarian halves deaths.
        var delousing = new LakskongenSim.Farm.DelousingSystem();
        double tempC = SimulationEngine.Weather?.CurrentTemperatureC ?? 10.0;
        double mortMult = LakskongenSim.Core.CrewEffects.TreatmentMortalityMultiplier(SimulationEngine.State);
        // Phase 4.3: damaged gills can't take heat/handling — up to +150%.
        double worstGill = targetSites.Count > 0
            ? targetSites.Min(s => s.GillScore) : 100.0;
        mortMult *= 1.0 + (1.0 - worstGill / 100.0) * 1.5;
        int totalDeaths = 0;
        foreach (var site in targetSites)
        {
            foreach (var pop in site.Pens)
            {
                if (pop.TotalCount <= 0) continue;
                var result = delousing.Treat(pop, delousingMethod,
                    SimulationEngine.SeaLice, SimulationEngine.IntegratedDisease, tempC, mortMult);
                totalDeaths += result.Deaths;
            }
        }
        SimulationEngine.FinanceManager.Spend(totalCost);

        // Record event
        SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = SimulationEngine.State.Day,
            Name = "Delousing Treatment",
            Category = LakskongenSim.Events.EventCategory.Disease,
            Description = $"Applied {delousingMethod} delousing to {(siteId == "all_sites" ? "all sites" : siteId)} ({totalFish:N0} fish treated, {profile.Efficacy:P0} profile efficacy, {totalDeaths:N0} handling deaths)",
            BalanceChange = -totalCost
        });

        EmitSignal(SignalName.FarmActionCompleted, "delousing", siteId, true);
        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: Delousing treatment applied — {delousingMethod} ({totalFish:N0} fish, {totalCost:N0} NOK, {totalDeaths:N0} deaths)");
        return true;
    }

    /// <summary>
    /// Map UI/legacy method names to DelousingMethod (Phase 2.2).
    /// </summary>
    public static bool TryParseDelousingMethod(string name, out LakskongenSim.Data.DelousingMethod method)
    {
        switch ((name ?? "").ToLower())
        {
            case "thermolicer":
            case "thermal": method = LakskongenSim.Data.DelousingMethod.Thermolicer; return true;
            case "hydrolicer":
            case "mechanical": method = LakskongenSim.Data.DelousingMethod.Hydrolicer; return true;
            case "cleanerfish":
            case "cleaner_fish":
            case "biological": method = LakskongenSim.Data.DelousingMethod.CleanerFish; return true;
            case "laser":
            case "laserdelousing":
            case "stingray": method = LakskongenSim.Data.DelousingMethod.LaserDelousing; return true;
            case "freshwater":
            case "freshwaterbath": method = LakskongenSim.Data.DelousingMethod.FreshwaterBath; return true;
            case "peroxide":
            case "hydrogen_peroxide":
            case "chemical": method = LakskongenSim.Data.DelousingMethod.HydrogenPeroxide; return true;
            case "medicatedfeed":
            case "medicated_feed": method = LakskongenSim.Data.DelousingMethod.MedicatedFeed; return true;
            case "skirt":
            case "liceskirts": method = LakskongenSim.Data.DelousingMethod.LiceSkirts; return true;
            default: method = default; return false;
        }
    }

    /// <summary>
    /// Treatment cards for the delousing UI (Phase 2.2).
    /// Keys: "id", "efficacy", "mortality", "cost_per_fish", "welfare_impact",
    ///       "resistance_risk", "requires_wellboat".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetTreatmentProfiles()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (LakskongenSim.Data.DelousingMethod m in System.Enum.GetValues(typeof(LakskongenSim.Data.DelousingMethod)))
        {
            var p = LakskongenSim.Farm.DelousingSystem.GetProfile(m);
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"]              = m.ToString(),
                ["efficacy"]        = p.Efficacy,
                ["mortality"]       = p.MortalityRisk,
                ["cost_per_fish"]   = (double)p.CostPerFish,
                ["welfare_impact"]  = p.WelfareImpact,
                ["resistance_risk"] = p.ResistanceRiskIncrease,
                ["requires_wellboat"] = p.RequiresWellboat
            });
        }
        return result;
    }

    /// <summary>
    /// Current chemical/mechanical resistance 0-1 (Phase 2.2) for the UI.
    /// Keys: "pyrethroid", "organophosphate", "peroxide", "mechanical".
    /// </summary>
    public Godot.Collections.Dictionary GetResistanceLevels()
    {
        var lice = SimulationEngine?.SeaLice;
        return new Godot.Collections.Dictionary
        {
            ["pyrethroid"]      = lice?.PyrethroidResistance ?? 0.0,
            ["organophosphate"] = lice?.OrganophosphateResistance ?? 0.0,
            ["peroxide"]        = lice?.HydrogenPeroxideResistance ?? 0.0,
            ["mechanical"]      = lice?.MechanicalResistance ?? 0.0
        };
    }

    /// <summary>
    /// Buy a VAP processing plant (Phase 3.5). Tiers mirror ProcessingManager:
    /// Small 1M, Medium 5M, Large 15M. Owned plant adds +12% fillet margin on
    /// all harvest revenue at 5k NOK/day opex. One plant per game (upgrade
    /// = pay difference).
    /// </summary>
    public bool BuyProcessingPlant(string tier)
    {
        var state = SimulationEngine?.State;
        if (state == null) return false;
        tier = (tier ?? "").Trim();
        decimal cost = tier.ToLower() switch
        {
            "small" => 1_000_000m,
            "medium" => 5_000_000m,
            "large" => 15_000_000m,
            _ => -1m
        };
        if (cost < 0) return false;
        if (state.OwnsProcessingPlant && state.ProcessingTier.ToLower() == tier.ToLower())
            return true;
        decimal ownedValue = state.OwnsProcessingPlant ? state.ProcessingTier.ToLower() switch
        {
            "small" => 1_000_000m,
            "medium" => 5_000_000m,
            "large" => 15_000_000m,
            _ => 0m
        } : 0m;
        decimal due = cost - ownedValue;
        if (due <= 0) return true;
        if (SimulationEngine!.FinanceManager.Balance < due) return false;
        SimulationEngine.FinanceManager.Spend(due);
        state.OwnsProcessingPlant = true;
        state.ProcessingTier = tier;
        state.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = state.Day,
            Name = "Foredlingsanlegg kjøpt",
            Category = LakskongenSim.Events.EventCategory.Equipment,
            Description = $"Filetlinje ({tier}) satt i drift — +12% på all høsteinntekt",
            BalanceChange = -due
        });
        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: Processing plant bought ({tier}, {due:N0} NOK)");
        return true;
    }

    /// <summary>
    /// Containment ladder table for UI (Phase 4.1).
    /// Keys: "id", "capex_nok", "weekly_opex_nok", "lice_exclusion",
    ///       "oxygen_demand", "storm_mult", "escape_mult", "growth_mult".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetContainmentLevels()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var kv in LakskongenSim.Farm.ContainmentTech.Table)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = kv.Key.ToString(),
                ["capex_nok"] = (double)kv.Value.CapexNok,
                ["weekly_opex_nok"] = (double)kv.Value.WeeklyOpexNok,
                ["lice_exclusion"] = kv.Value.LiceExclusion01,
                ["oxygen_demand"] = kv.Value.OxygenDemandMult,
                ["storm_mult"] = kv.Value.StormDamageMult,
                ["escape_mult"] = kv.Value.EscapeRiskMult,
                ["growth_mult"] = kv.Value.GrowthMult
            });
        }
        return result;
    }

    /// <summary>
    /// Set ROC watch mode for a site (Phase 4.2). Refused when over budget.
    /// Budget = 100 + 15/crew (cap +45). Returns true on success.
    /// </summary>
    public bool SetWatchMode(string siteId, string mode)
    {
        var state = SimulationEngine?.State;
        var site = state?.SeaSites?.FirstOrDefault(s => s.Id == siteId);
        if (site == null) return false;
        mode = (mode ?? "").ToLower();
        if (mode != "watch" && mode != "scan" && mode != "auto") return false;
        int budget = LakskongenSim.Optimization.RemoteOpsCentre.BudgetFor(state!.Crew.Count);
        int used = state.SeaSites.Sum(s => LakskongenSim.Optimization.RemoteOpsCentre.CostFor(s.Id == siteId ? mode : s.WatchMode));
        if (used > budget) return false;
        site.WatchMode = mode;
        EmitSignal(SignalName.GameStateUpdated);
        return true;
    }

    /// <summary>ROC attention budget and usage (Phase 4.2).</summary>
    public Godot.Collections.Dictionary GetAttentionBudget()
    {
        var state = SimulationEngine?.State;
        int crew = state?.Crew.Count ?? 0;
        int sites = state?.SeaSites.Count ?? 0;
        return new Godot.Collections.Dictionary
        {
            ["budget"] = LakskongenSim.Optimization.RemoteOpsCentre.BudgetFor(crew),
            ["used"] = state?.SeaSites != null
                ? LakskongenSim.Optimization.RemoteOpsCentre.UsedBy(state.SeaSites) : 0,
            ["site_count"] = sites
        };
    }
    /// <summary>
    /// Set feed recipe (Phase 4.4): protein 38-48, replacement 0-60, astax 0-80.
    /// Updates feed price authority, FCR multiplier and novel-ingredient flag.
    /// </summary>
    public void SetFeedRecipe(double proteinPct, double replacePct, double astaxPpm)
    {
        var state = SimulationEngine?.State;
        if (state == null) return;
        var (p, r, a) = LakskongenSim.Processing.FeedRecipe.Clamp(proteinPct, replacePct, astaxPpm);
        state.RecipeProteinPct = p;
        state.RecipeReplacePct = r;
        state.RecipeAstaxPpm = a;
        ApplyFeedRecipe();
        EmitSignal(SignalName.GameStateUpdated);
    }

    /// <summary>Current recipe + live preview (cost, FCR mult, grade).</summary>
    public Godot.Collections.Dictionary GetFeedRecipe()
    {
        var state = SimulationEngine?.State;
        double p = state?.RecipeProteinPct ?? 42.0;
        double r = state?.RecipeReplacePct ?? 20.0;
        double a = state?.RecipeAstaxPpm ?? 50.0;
        string grade = LakskongenSim.Processing.FeedRecipe.Grade(p, a);
        return new Godot.Collections.Dictionary
        {
            ["protein"] = p,
            ["replace"] = r,
            ["astax"] = a,
            ["cost_per_kg"] = LakskongenSim.Processing.FeedRecipe.CostPerKg(p, r, a),
            ["fcr_mult"] = LakskongenSim.Processing.FeedRecipe.FcrMult(p, r, a),
            ["grade"] = grade,
            ["vap_mult"] = (double)LakskongenSim.Processing.FeedRecipe.VapMult(grade)
        };
    }

    private void ApplyFeedRecipe()
    {
        var state = SimulationEngine?.State;
        var feed = SimulationEngine?.Feed;
        if (state == null || feed == null) return;
        feed.FeedCostPerKg = (decimal)LakskongenSim.Processing.FeedRecipe.CostPerKg(
            state.RecipeProteinPct, state.RecipeReplacePct, state.RecipeAstaxPpm);
        feed.RecipeFCRMult = LakskongenSim.Processing.FeedRecipe.FcrMult(
            state.RecipeProteinPct, state.RecipeReplacePct, state.RecipeAstaxPpm);
        feed.UsesInsectMeal = state.RecipeReplacePct >= 30.0;
    }
    /// <summary>
    /// Twin-lite 7-day forecast (Phase 4.5, PROJECTION): biomass/lice/cash
    /// arrays + confidence. Cached per day. Read-only — never touches live sim.
    /// Keys: "biomass_kg", "lice", "cash_nok" (Array double), "confidence".
    /// </summary>
    private Godot.Collections.Dictionary? _forecastCache;
    private int _forecastCacheDay = -1;

    public Godot.Collections.Dictionary GetForecast()
    {
        int day = SimulationEngine?.State?.Day ?? 0;
        if (_forecastCache != null && _forecastCacheDay == day)
            return (Godot.Collections.Dictionary)_forecastCache.Duplicate();
        var result = new Godot.Collections.Dictionary
        {
            ["biomass_kg"] = new Godot.Collections.Array<double>(),
            ["lice"] = new Godot.Collections.Array<double>(),
            ["cash_nok"] = new Godot.Collections.Array<double>(),
            ["confidence"] = "low"
        };
        var engine = SimulationEngine;
        if (engine?.State != null && engine.Growth != null && engine.SeaLice != null && engine.Analytics != null)
        {
            var fc = LakskongenSim.Optimization.ForecastTwin.Project(
                engine.State, engine.Growth, engine.SeaLice,
                engine.Weather?.CurrentTemperatureC ?? 8.0, engine.Analytics);
            var bio = new Godot.Collections.Array<double>();
            var lice = new Godot.Collections.Array<double>();
            var cash = new Godot.Collections.Array<double>();
            foreach (var v in fc.BiomassKg) bio.Add(v);
            foreach (var v in fc.LicePerFish) lice.Add(v);
            foreach (var v in fc.CashNok) cash.Add(v);
            result["biomass_kg"] = bio;
            result["lice"] = lice;
            result["cash_nok"] = cash;
            result["confidence"] = fc.Confidence;
        }
        _forecastCache = result;
        _forecastCacheDay = day;
        return (Godot.Collections.Dictionary)result.Duplicate();
    }
    /// <summary>
    /// Upgrade site containment one rung up the ladder (Phase 4.1, no downgrade).
    /// Charges the target level's capex, syncs skirt flag at level 1+, applies
    /// immediate red-zone MAB credit when eligible. Returns true on success.
    /// </summary>
    public bool UpgradeContainment(string siteId, string levelName)
    {
        var state = SimulationEngine?.State;
        var site = state?.SeaSites?.FirstOrDefault(s => s.Id == siteId);
        if (site == null) return false;
        if (!System.Enum.TryParse<LakskongenSim.Farm.ContainmentLevel>(levelName, true, out var target))
            return false;
        if (target <= site.Containment) return false; // no downgrade / same-level
        var mods = LakskongenSim.Farm.ContainmentTech.ModifiersFor(target);
        if (SimulationEngine!.FinanceManager.Balance < mods.CapexNok) return false;
        SimulationEngine.FinanceManager.Spend(mods.CapexNok);
        site.Containment = target;
        if (target >= LakskongenSim.Farm.ContainmentLevel.Skirt)
            site.HasLiceSkirt = true;
        if (target >= LakskongenSim.Farm.ContainmentLevel.ClosedFloat)
            site.IsClosedContainment = true;
        // Immediate red-zone credit when upgrading inside a red zone.
        var tls = SimulationEngine.TrafficLightSystem;
        if (tls != null && tls.GetZoneColor(site.ZoneId).ToString() == "Red"
            && !site.RedZoneCreditApplied && target >= LakskongenSim.Farm.ContainmentLevel.SemiClosed)
        {
            double credit = LakskongenSim.Farm.ContainmentTech.MabCreditFor(target, true);
            site.MABTonnes *= credit;
            site.RedZoneCreditApplied = true;
        }
        state!.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = state.Day,
            Name = "Containment oppgradert",
            Category = LakskongenSim.Events.EventCategory.Equipment,
            Description = $"{siteId} → {target} ({mods.CapexNok:N0} NOK)",
            BalanceChange = -mods.CapexNok
        });
        EmitSignal(SignalName.FarmActionCompleted, "upgrade_containment", siteId, true);
        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: {siteId} containment → {target}");
        return true;
    }

    /// <summary>
    /// Repair nets at a sea site (Phase 1.2). Cost scales with damage:
    /// NOK 250k for a full 0→100 rebuild. Restores NetIntegrityPct to 100.
    /// Returns true on success, false if site not found or insufficient funds.
    /// </summary>
    public bool RepairNets(string siteId)
    {
        var site = SimulationEngine?.State?.SeaSites?.FirstOrDefault(s => s.Id == siteId);
        if (site == null)
        {
            GD.PrintErr($"GameBridge: RepairNets failed - site '{siteId}' not found");
            EmitSignal(SignalName.FarmActionCompleted, "repair_nets", siteId, false);
            return false;
        }
        if (site.NetIntegrityPct >= 99.5)
        {
            GD.Print($"GameBridge: RepairNets — '{siteId}' already intact");
            return true;
        }
        decimal cost = (decimal)(100.0 - site.NetIntegrityPct) / 100m * 250_000m;
        if (SimulationEngine!.FinanceManager.Balance < cost)
        {
            GD.PrintErr($"GameBridge: RepairNets failed - insufficient funds (need {cost:N0} NOK)");
            EmitSignal(SignalName.FarmActionCompleted, "repair_nets", siteId, false);
            return false;
        }
        site.NetIntegrityPct = 100.0;
        SimulationEngine.FinanceManager.Spend(cost);
        SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = SimulationEngine.State.Day,
            Name = "Net Repair",
            Category = LakskongenSim.Events.EventCategory.Equipment,
            Description = $"Repaired nets at {siteId} (100%)",
            BalanceChange = -cost
        });
        EmitSignal(SignalName.FarmActionCompleted, "repair_nets", siteId, true);
        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: Repaired nets at '{siteId}' for {cost:N0} NOK");
        return true;
    }

    // ── Phase 5.3: Sludge Valorization ───────────────────────────────────────────

    /// <summary>
    /// Get sludge status for all sea sites.
    /// Keys per site: "id", "accumulated_kg_ds", "valorized_kg_ds", "last_valorization_day",
    ///                 "has_hub_connection", "projected_daily_kg_ds".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetSludgeStatus()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var sites = SimulationEngine?.State?.SeaSites;
        if (sites == null) return result;

        foreach (var site in sites)
        {
            // Project daily sludge based on recent feed usage
            double dailyFeedKg = 0.0;
            var pops = site.Pens;
            foreach (var pop in pops)
            {
                // Approximate from biomass - rough estimate 1.5% body weight/day
                dailyFeedKg += pop.GetTotalBiomassKg() * 0.015;
            }
            var containmentMods = Farm.SludgeValorization.ContainmentCollectionMult.TryGetValue(site.Containment, out var m) ? m : Farm.SludgeValorization.Default.CollectionEfficiency;
            double projectedDaily = dailyFeedKg / 1000.0 * Farm.SludgeValorization.Default.KgDrySolidsPerTonneFeed * containmentMods;

            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = site.Id,
                ["accumulated_kg_ds"] = site.SludgeAccumulatedKgDs,
                ["valorized_kg_ds"] = site.SludgeValorizedKgDs,
                ["last_valorization_day"] = site.LastSludgeValorizationDay,
                ["has_hub_connection"] = site.HasSludgeHubConnection,
                ["projected_daily_kg_ds"] = projectedDaily
            });
        }
        return result;
    }

    /// <summary>
    /// Toggle centralized sludge hub connection for a site (Phase 5.3).
    /// CAPEX 40M NOK (shared hub), enables weekly auto-valorization at better efficiency.
    /// Returns true on success.
    /// </summary>
    public bool ToggleSludgeHub(string siteId)
    {
        var site = SimulationEngine?.State?.SeaSites?.FirstOrDefault(s => s.Id == siteId);
        if (site == null) return false;

        if (!site.HasSludgeHubConnection)
        {
            // Check if hub already built elsewhere or build new
            bool hubExists = SimulationEngine.State.SeaSites.Any(s => s.HasSludgeHubConnection);
            decimal cost = hubExists ? 5_000_000m : 40_000_000m; // Connection vs new hub
            if (SimulationEngine!.FinanceManager.Balance < cost) return false;
            SimulationEngine.FinanceManager.Spend(cost);
            site.HasSludgeHubConnection = true;

            SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
            {
                Day = SimulationEngine.State.Day,
                Name = hubExists ? "Slam-hub tilkobling" : "Slam-hub byggd",
                Category = LakskongenSim.Events.EventCategory.Equipment,
                Description = $"{site.Id} koblet til sentral slam-hub ({cost:N0} NOK)",
                BalanceChange = -cost
            });
        }
        else
        {
            site.HasSludgeHubConnection = false;
            SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
            {
                Day = SimulationEngine.State.Day,
                Name = "Slam-hub frakoblet",
                Category = LakskongenSim.Events.EventCategory.Equipment,
                Description = $"{site.Id} frakoblet fra sentral slam-hub",
                BalanceChange = 0m
            });
        }

        EmitSignal(SignalName.GameStateUpdated);
        return true;
    }

    /// <summary>
    /// Manually trigger sludge valorization at a site (Phase 5.3).
    /// Returns net revenue (biogas + fertilizer - opex).
    /// </summary>
    public decimal ValorizeSludgeNow(string siteId)
    {
        var site = SimulationEngine?.State?.SeaSites?.FirstOrDefault(s => s.Id == siteId);
        if (site == null) return 0m;
        if (site.SludgeAccumulatedKgDs < 100.0) return 0m;

        var sludgeParams = Farm.SludgeValorization.Default;
        var (biogasRev, fertRev, opex) = Farm.SludgeValorization.Valorize(site.SludgeAccumulatedKgDs, sludgeParams);
        decimal netRev = biogasRev + fertRev - opex;

        if (netRev > 0)
            SimulationEngine!.FinanceManager.AddRevenue(netRev);
        else
        {
            if (!SimulationEngine!.FinanceManager.Spend(-netRev))
                SimulationEngine!.FinanceManager.ForceSpend(-netRev);
        }

        site.SludgeValorizedKgDs += site.SludgeAccumulatedKgDs;
        site.LastSludgeValorizationDay = SimulationEngine!.State.Day;

        SimulationEngine.State.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = SimulationEngine.State.Day,
            Name = "Manuell slam-valorisering",
            Category = LakskongenSim.Events.EventCategory.Equipment,
            Description = $"{site.Id}: {site.SludgeAccumulatedKgDs:F0} kg DS → biogas {biogasRev:N0} + gjødsel {fertRev:N0} - OPEX {opex:N0} = {netRev:+#,0;-#,0;0} NOK",
            BalanceChange = netRev
        });
        SimulationEngine.State.TrimEventLog();

        site.SludgeAccumulatedKgDs = 0.0;
        EmitSignal(SignalName.GameStateUpdated);
        return netRev;
    }

    // ── Phase 5.3: Cohort Transfers ──────────────────────────────────────────────

    /// <summary>
    /// Transfer reason enum for UI.
    /// </summary>
    public enum TransferReason
    {
        Grading,
        Fallowing,
        MabManagement,
        DiseaseIsolation,
        OffshoreStocking,
        ContainmentUpgrade
    }

    /// <summary>
    /// Plan a cohort transfer between sites (Phase 5.3).
    /// Returns plan with cost, mortality estimate, ETA. Does not execute.
    /// Keys: "source_id", "dest_id", "fish_count", "mean_weight_kg", "reason",
    ///       "eta_days", "estimated_cost_nok", "estimated_mortality_pct", "requires_wellboat".
    /// </summary>
    public Godot.Collections.Dictionary PlanCohortTransfer(
        string sourceSiteId, string destSiteId, int fishCount, double meanWeightKg, string reasonStr)
    {
        var result = new Godot.Collections.Dictionary();
        if (!System.Enum.TryParse<Farm.CohortTransferSystem.TransferReason>(reasonStr, true, out var reason))
        {
            result["error"] = "Invalid reason";
            return result;
        }

        var state = SimulationEngine?.State;
        if (state == null) { result["error"] = "Engine not initialized"; return result; }

        bool hasVet = LakskongenSim.Core.CrewEffects.HasRole(state, "veterinaer");
        var plan = Farm.CohortTransferSystem.PlanTransfer(sourceSiteId, destSiteId, fishCount, meanWeightKg, reason, state, hasVet);

        result["source_id"] = plan.SourceSiteId;
        result["dest_id"] = plan.DestSiteId;
        result["fish_count"] = plan.FishCount;
        result["mean_weight_kg"] = plan.MeanWeightKg;
        result["reason"] = plan.Reason.ToString();
        result["eta_days"] = plan.EtaDays;
        result["estimated_cost_nok"] = (double)plan.EstimatedCostNok;
        result["estimated_mortality_pct"] = plan.EstimatedMortalityPct;
        result["requires_wellboat"] = plan.RequiresWellboat;
        return result;
    }

    /// <summary>
    /// Execute a planned cohort transfer (Phase 5.3).
    /// Called when wellboat arrives (or immediately for testing).
    /// Returns result dict: "success", "fish_transferred", "fish_lost", "actual_cost_nok", "message".
    /// </summary>
    public Godot.Collections.Dictionary ExecuteCohortTransfer(
        string sourceSiteId, string destSiteId, int fishCount, double meanWeightKg, string reasonStr)
    {
        var result = new Godot.Collections.Dictionary();
        if (!System.Enum.TryParse<Farm.CohortTransferSystem.TransferReason>(reasonStr, true, out var reason))
        {
            result["success"] = false;
            result["message"] = "Invalid reason";
            return result;
        }

        var engine = SimulationEngine;
        if (engine?.State == null) { result["success"] = false; result["message"] = "Engine not initialized"; return result; }

        bool hasVet = LakskongenSim.Core.CrewEffects.HasRole(engine.State, "veterinaer");
        var plan = Farm.CohortTransferSystem.PlanTransfer(sourceSiteId, destSiteId, fishCount, meanWeightKg, reason, engine.State, hasVet);
        var execResult = Farm.CohortTransferSystem.ExecuteTransfer(plan, engine.State, engine.FinanceManager, Random.Shared);

        result["success"] = execResult.Success;
        result["fish_transferred"] = execResult.FishTransferred;
        result["fish_lost"] = execResult.FishLost;
        result["actual_cost_nok"] = (double)execResult.ActualCostNok;
        result["message"] = execResult.Message;

        EmitSignal(SignalName.GameStateUpdated);
        return result;
    }

    /// <summary>
    /// Get valid destination sites for a transfer from source (Phase 5.3).
    /// Keys per site: "id", "name", "zone", "current_biomass_kg", "mab_tonnes", "available_mab_kg".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetTransferDestinations(string sourceSiteId, double incomingBiomassKg)
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var state = SimulationEngine?.State;
        if (state == null) return result;

        var dests = Farm.CohortTransferSystem.GetValidDestinations(sourceSiteId, state, incomingBiomassKg);
        foreach (var site in dests)
        {
            double currentBio = site.Pens.Sum(p => p.GetTotalBiomassKg());
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = site.Id,
                ["name"] = site.Id,
                ["zone"] = site.ZoneId,
                ["current_biomass_kg"] = currentBio,
                ["mab_tonnes"] = site.MABTonnes,
                ["available_mab_kg"] = site.MABTonnes * 1000.0 - currentBio
            });
        }
        return result;
    }

    // ── Phase 5.4: Carbon Ledger ─────────────────────────────────────────────────

    /// <summary>
    /// Get aggregated carbon ledger for all sites and facilities (Phase 5.4).
    /// Returns: "sites" (array), "offshore" (array), "ras" (array), "processing" (dict),
    ///          "total_emissions_kg", "total_credits_kg", "net_emissions_kg",
    ///          "scope1_kg", "scope2_kg", "scope3_kg", "this_year_net_kg".
    /// </summary>
    public Godot.Collections.Dictionary GetCarbonLedger()
    {
        var result = new Godot.Collections.Dictionary();
        var state = SimulationEngine?.State;
        if (state == null) return result;

        var sitesArray = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        double totalEmissions = 0, totalCredits = 0;
        double scope1 = 0, scope2 = 0, scope3 = 0;

        // Sea sites
        foreach (var site in state.SeaSites)
        {
            var tracker = site.CarbonTracker;
            double em = tracker.TotalEmissions();
            double cr = tracker.TotalCredits();
            totalEmissions += em;
            totalCredits += cr;
            var breakdown = tracker.ScopeBreakdown(0, state.Day);
            scope1 += breakdown["Scope1"];
            scope2 += breakdown["Scope2"];
            scope3 += breakdown["Scope3"];

            var topSources = new Godot.Collections.Array();
            foreach (var t in tracker.TopSources(0, state.Day, 3))
                topSources.Add(new Godot.Collections.Dictionary { ["source"] = t.Source, ["kg"] = t.KgCO2e });
            sitesArray.Add(new Godot.Collections.Dictionary
            {
                ["id"] = site.Id,
                ["emissions_kg"] = em,
                ["credits_kg"] = cr,
                ["net_kg"] = em - cr,
                ["scope1_kg"] = breakdown["Scope1"],
                ["scope2_kg"] = breakdown["Scope2"],
                ["scope3_kg"] = breakdown["Scope3"],
                ["this_year_net_kg"] = tracker.NetThisYear(state.Day),
                ["top_sources"] = topSources
            });
        }

        // Offshore farms
        var offshoreArray = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var off in SimulationEngine!.OffshoreFarms)
        {
            if (!off.IsOperational) continue;
            var tracker = off.CarbonTracker;
            double em = tracker.TotalEmissions();
            double cr = tracker.TotalCredits();
            totalEmissions += em;
            totalCredits += cr;
            var breakdown = tracker.ScopeBreakdown(0, state.Day);
            scope1 += breakdown["Scope1"];
            scope2 += breakdown["Scope2"];
            scope3 += breakdown["Scope3"];

            offshoreArray.Add(new Godot.Collections.Dictionary
            {
                ["id"] = off.Id,
                ["name"] = off.Name,
                ["emissions_kg"] = em,
                ["credits_kg"] = cr,
                ["net_kg"] = em - cr,
                ["scope1_kg"] = breakdown["Scope1"],
                ["scope2_kg"] = breakdown["Scope2"],
                ["scope3_kg"] = breakdown["Scope3"],
                ["this_year_net_kg"] = tracker.NetThisYear(state.Day)
            });
        }

        // RAS facilities
        var rasArray = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var ras in SimulationEngine!.RASFacilities)
        {
            if (!ras.IsOperational) continue;
            var tracker = ras.CarbonTracker;
            double em = tracker.TotalEmissions();
            double cr = tracker.TotalCredits();
            totalEmissions += em;
            totalCredits += cr;
            var breakdown = tracker.ScopeBreakdown(0, state.Day);
            scope1 += breakdown["Scope1"];
            scope2 += breakdown["Scope2"];
            scope3 += breakdown["Scope3"];

            rasArray.Add(new Godot.Collections.Dictionary
            {
                ["id"] = ras.Id,
                ["name"] = ras.Name,
                ["emissions_kg"] = em,
                ["credits_kg"] = cr,
                ["net_kg"] = em - cr,
                ["scope1_kg"] = breakdown["Scope1"],
                ["scope2_kg"] = breakdown["Scope2"],
                ["scope3_kg"] = breakdown["Scope3"],
                ["this_year_net_kg"] = tracker.NetThisYear(state.Day)
            });
        }

        // Processing plant (virtual)
        var procDict = new Godot.Collections.Dictionary();
        if (state.OwnsProcessingPlant && Enum.TryParse<Processing.ProcessingTier>(state.ProcessingTier, out var tier))
        {
            var firstSite = state.SeaSites.FirstOrDefault();
            if (firstSite != null)
            {
                var tracker = firstSite.CarbonTracker;
                double em = tracker.Emissions.Where(e => e.Source == "processing_plant").Sum(e => e.KgCO2e);
                double cr = tracker.Credits.Where(c => c.Source == "processing_plant").Sum(c => c.KgCO2e);
                procDict["emissions_kg"] = em;
                procDict["credits_kg"] = cr;
                procDict["net_kg"] = em - cr;
                procDict["tier"] = state.ProcessingTier;
            }
        }

        result["sites"] = sitesArray;
        result["offshore"] = offshoreArray;
        result["ras"] = rasArray;
        result["processing"] = procDict;
        result["total_emissions_kg"] = totalEmissions;
        result["total_credits_kg"] = totalCredits;
        result["net_emissions_kg"] = totalEmissions - totalCredits;
        result["scope1_kg"] = scope1;
        result["scope2_kg"] = scope2;
        result["scope3_kg"] = scope3;
        result["this_year_net_kg"] = state.SeaSites.Sum(s => s.CarbonTracker.NetThisYear(state.Day)) +
                                     SimulationEngine!.OffshoreFarms.Sum(o => o.CarbonTracker.NetThisYear(state.Day)) +
                                     SimulationEngine!.RASFacilities.Sum(r => r.CarbonTracker.NetThisYear(state.Day));

        return result;
    }

    /// <summary>
    /// Get carbon intensity per kg harvested (kg CO2e/kg HOG).
    /// </summary>
    public double GetCarbonIntensity()
    {
        var state = SimulationEngine?.State;
        if (state == null) return 0.0;

        double totalHarvestedKg = state.EventLog
            .Where(e => e.Category == Events.EventCategory.Market && e.Name == "Harvest")
            .Sum(e => Math.Abs((double)e.BalanceChange) / 80.0); // Rough: revenue / 80 NOK/kg
        
        if (totalHarvestedKg <= 0) return 0.0;

        double totalEmissions = state.SeaSites.Sum(s => s.CarbonTracker.TotalEmissions()) +
                                SimulationEngine!.OffshoreFarms.Sum(o => o.CarbonTracker.TotalEmissions()) +
                                SimulationEngine!.RASFacilities.Sum(r => r.CarbonTracker.TotalEmissions());
        
        return totalEmissions / totalHarvestedKg;
    }

    /// <summary>
    /// Check if carbon-neutral certification threshold met (net <= 0 for year).
    /// </summary>
    public bool IsCarbonNeutralYear()
    {
        var state = SimulationEngine?.State;
        if (state == null) return false;
        
        double netThisYear = state.SeaSites.Sum(s => s.CarbonTracker.NetThisYear(state.Day)) +
                             SimulationEngine!.OffshoreFarms.Sum(o => o.CarbonTracker.NetThisYear(state.Day)) +
                             SimulationEngine!.RASFacilities.Sum(r => r.CarbonTracker.NetThisYear(state.Day));
        
        return netThisYear <= 0;
    }

    // ── Phase 3 depth exposure (P7/P9/P10) ──────────────────────────────────────

    /// <summary>P9: binding monthly pen price (NOK/kg) set by the Price Council.</summary>
    public double PenPrice => (double)(SimulationEngine?.PenPriceCouncil?.MonthlyPenPrice ?? 0m);

    /// <summary>P9: whether the 25% resource-rent tax is enabled for the active scenario.</summary>
    public bool ResourceRentEnabled => SimulationEngine?.EnableResourceRentTax ?? false;

    /// <summary>P9: cumulative resource-rent tax paid (NOK).</summary>
    public double ResourceRentPaid => (double)(SimulationEngine?.ResourceRentPaid ?? 0m);

    /// <summary>
    /// P10: gill-health early-warning risk (0-1) for a site.
    /// Keys: "site_id", "risk", "warning" (bool), "critical" (bool), "reason".
    /// </summary>
    public Godot.Collections.Dictionary GetGillRisk(string siteId)
    {
        var res = new Godot.Collections.Dictionary
        {
            ["site_id"] = siteId, ["risk"] = 0.0, ["warning"] = false,
            ["critical"] = false, ["reason"] = "no data"
        };
        var engine = SimulationEngine;
        var site = engine?.State?.SeaSites?.FirstOrDefault(s => s.Id == siteId);
        if (site == null) return res;

        double temp = engine!.Weather?.CurrentTemperatureC ?? 10.0;
        double gill = site.GillScore;
        // Derive inputs we can trust; gill score is a direct reverse proxy for risk.
        double risk = System.Math.Clamp(1.0 - gill / 100.0, 0.0, 1.0);
        var r = LakskongenSim.Health.GillHealthWarning.Evaluate(temp, 8.0, 5.0, 0.2, site.GillScore < 60 ? 0.5 : 0.0);
        // Blend the model risk with the live gill score so both matter.
        risk = System.Math.Clamp((risk + r.Risk01) * 0.5, 0.0, 1.0);
        res["risk"] = risk;
        res["warning"] = risk >= 0.5;
        res["critical"] = risk >= 0.75;
        res["reason"] = r.Reason;
        return res;
    }

    /// <summary>P7: does a site's containment qualify for red-zone capacity recovery?</summary>
    public bool QualifiesForEnvironmentalFlexibility(string siteId)
    {
        var engine = SimulationEngine;
        var site = engine?.State?.SeaSites?.FirstOrDefault(s => s.Id == siteId);
        if (site == null || engine?.TrafficLightSystem == null) return false;
        var color = engine.TrafficLightSystem.GetZoneColor(site.ZoneId);
        return LakskongenSim.Farm.EnvironmentalFlexibility.QualifiesForRedZoneRecovery(site.Containment, color);
    }

    // ── Contract & Market API ──────────────────────────────────────────────────

    /// <summary>Offer cache: real ContractSystem offers for the current day.</summary>
    private List<LakskongenSim.Market.ContractOffer> _offerCache = new();
    private int _offerCacheDay = -1;

    private void RefreshOfferCache()
    {
        var cs = SimulationEngine?.ContractSystem;
        if (cs == null) return;
        int day = SimulationEngine?.State?.Day ?? 0;
        if (day == _offerCacheDay) return;
        _offerCacheDay = day;
        decimal spot = SimulationEngine?.MarketSimulation?.PricePerKg ?? 70m;
        double brand = (double)(SimulationEngine?.BrandReputation?.QualityReputation ?? 50m) / 100.0;
        _offerCache = cs.GenerateOffers(spot, brand);
    }

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
            float fulfilledPercent = contract.QuantityKg > 0 ? (float)contract.FulfilledKg / contract.QuantityKg * 100f : 0f;
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

        // Real offers from ContractSystem, cached per day (stable within a day).
        RefreshOfferCache();
        for (int i = 0; i < _offerCache.Count; i++)
        {
            var o = _offerCache[i];
            result.Add(new Godot.Collections.Dictionary
            {
                ["index"] = i,
                ["buyer"] = o.Buyer,
                ["type"] = o.Type.ToString(),
                ["price_per_kg"] = (double)o.PricePerKg,
                ["quantity_kg"] = (double)o.QuantityKg,
                ["quality"] = o.Quality.ToString(),
                ["penalty_pct"] = (double)o.PenaltyPct,
                ["duration_weeks"] = o.DurationWeeks
            });
        }
        return result;
    }

    /// <summary>
    /// Accept a cached offer by index (Phase 2.3). Returns the contract ID
    /// string on success, empty string on failure.
    /// </summary>
    public string AcceptOffer(int index)
    {
        var cs = SimulationEngine?.ContractSystem;
        if (cs == null || index < 0 || index >= _offerCache.Count) return "";
        var offer = _offerCache[index];
        var contract = cs.AcceptOffer(offer);
        _offerCache.RemoveAt(index);
        SimulationEngine?.State?.EventLog.Add(new LakskongenSim.Events.EventRecord
        {
            Day = SimulationEngine?.State?.Day ?? 0,
            Name = "Kontrakt signert",
            Category = LakskongenSim.Events.EventCategory.Market,
            Description = $"Kontrakt med {contract.Buyer}: {contract.QuantityKg:N0} kg @ {contract.PricePerKg:N0} NOK/kg ({contract.DurationWeeks} uker)",
            BalanceChange = 0m
        });
        EmitSignal(SignalName.GameStateUpdated);
        GD.Print($"GameBridge: Contract signed — {contract.Buyer} {contract.QuantityKg:N0}kg @ {contract.PricePerKg:N0} NOK/kg");
        return contract.Id.ToString();
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

    /// <summary>
    /// Wave 6: per-trait expected genetic gain for the current broodstock under
    /// genomic selection (ΔG = i·r·σ_A). Returns an empty array when there is no
    /// broodstock, so the UI can show an "insufficient data" note. Each entry has
    /// keys: "trait", "heritability", "accuracy", "gain".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetGenomicSelectionPreview()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var broodstock = SimulationEngine?.State?.Broodstock;
        if (broodstock == null || broodstock.Count == 0) return result;

        var gains = GenomicSelection.ExpectedGain(broodstock);
        foreach (GeneticTrait trait in Enum.GetValues(typeof(GeneticTrait)))
        {
            gains.TryGetValue(trait, out double gain);
            result.Add(new Godot.Collections.Dictionary
            {
                ["trait"] = trait.ToString(),
                ["heritability"] = Genetics.GetHeritability(trait),
                ["accuracy"] = GenomicSelection.SelectionAccuracy(trait, SelectionMethod.Genomic),
                ["gain"] = gain
            });
        }
        return result;
    }

    /// <summary>
    /// Wave 6: inbreeding status for the broodstock (falling back to live cohort
    /// genomes when no broodstock is held). Keys: "coefficient" (0..1),
    /// "penalty" (fractional growth loss 0..1), "penalty_percent" (0..100),
    /// "level" ("none"/"low"/"elevated"/"high"). Coefficient 0 → penalty 0.
    /// </summary>
    public Godot.Collections.Dictionary GetInbreedingStatus()
    {
        double coefficient = ComputePopulationInbreedingProxy();
        double penalty = GenomicSelection.InbreedingDepressionPenalty(
            GeneticTrait.GrowthRate, coefficient);

        string level = coefficient > 0.25 ? "high"
            : coefficient > 0.15 ? "elevated"
            : coefficient > 0.0 ? "low"
            : "none";

        return new Godot.Collections.Dictionary
        {
            ["coefficient"] = coefficient,
            ["penalty"] = penalty,
            ["penalty_percent"] = penalty * 100.0,
            ["level"] = level
        };
    }

    /// <summary>
    /// Wave 6: registered GE traits with their Norwegian regulatory status.
    /// Keys: "id", "name", "region_status", "reputation_cost", "target_trait",
    /// "modification". The game is set in Norway, so Norway status is reported.
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetGeTraits()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var trait in GeTraitRegistry.All)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = trait.Id,
                ["name"] = trait.Name,
                ["region_status"] = trait.StatusIn(GeRegion.Norway).ToString(),
                ["reputation_cost"] = trait.ReputationCost,
                ["target_trait"] = trait.TargetTrait.ToString(),
                ["modification"] = trait.Modification.ToString()
            });
        }
        return result;
    }

    /// <summary>
    /// Mean inbreeding proxy across the broodstock, falling back to live cohort
    /// genomes. Returns 0.0 when no genomes are available (no depression).
    /// </summary>
    private double ComputePopulationInbreedingProxy()
    {
        var state = SimulationEngine?.State;
        if (state == null) return 0.0;

        double total = 0.0;
        int samples = 0;

        foreach (var genome in state.Broodstock)
        {
            if (genome == null) continue;
            total += genome.InbreedingCoefficient;
            samples++;
        }

        if (samples == 0)
        {
            foreach (var population in state.Populations)
            {
                foreach (var cohort in population.Cohorts)
                {
                    if (cohort.Genetics == null) continue;
                    total += cohort.Genetics.InbreedingCoefficient;
                    samples++;
                }
            }
        }

        if (samples == 0) return 0.0;
        return Math.Clamp(total / samples, 0.0, 1.0);
    }

    // ── Worker/Crew API ───────────────────────────────────────────────────────

    /// <summary>
    /// Return the crew roster as an Array of Dictionaries (Phase 3.1, live).
    /// Keys: "id", "name", "role", "salary_nok" (double), "morale" (int),
    ///       "hired_day" (int).
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetCrewList()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var crew = SimulationEngine?.State?.Crew;
        if (crew == null) return result;
        foreach (var c in crew)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = c.Id,
                ["name"] = c.Name,
                ["role"] = c.Role,
                ["salary_nok"] = (double)c.MonthlySalaryNok,
                ["morale"] = c.Morale,
                ["hired_day"] = c.HiredDay
            });
        }
        return result;
    }

    /// <summary>
    /// Hireable candidates (Phase 3.1): role/name/salary triples.
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetHireCandidates()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var (role, name, salary) in LakskongenSim.Core.CrewEffects.Candidates)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["role"] = role,
                ["name"] = name,
                ["salary_nok"] = (double)salary
            });
        }
        return result;
    }

    /// <summary>
    /// Hire a worker by role (Phase 3.1). 50k signing cost + monthly payroll.
    /// Valid roles: "roekter", "veterinaer", "skipper".
    /// </summary>
    public bool HireWorker(string role)
    {
        var state = SimulationEngine?.State;
        if (state == null) return false;
        role = (role ?? "").ToLower();
        if (role != "roekter" && role != "veterinaer" && role != "skipper")
        {
            EmitSignal(SignalName.FinanceTransaction, "hire_worker", 0.0, $"Unknown role '{role}'");
            return false;
        }
        const decimal signingCost = 50_000m;
        if (SimulationEngine!.FinanceManager.Balance < signingCost) return false;
        var candidate = LakskongenSim.Core.CrewEffects.Candidates
            .FirstOrDefault(c => c.role == role);
        var rng = new Random();
        SimulationEngine.FinanceManager.Spend(signingCost);
        state.Crew.Add(new LakskongenSim.Core.CrewMember
        {
            Name = candidate.name,
            Role = role,
            MonthlySalaryNok = candidate.salary,
            Morale = 75 + rng.Next(21),
            HiredDay = state.Day
        });
        EmitSignal(SignalName.FinanceTransaction, "hire_worker", (double)signingCost, $"Hired {candidate.name} ({role})");
        EmitSignal(SignalName.GameStateUpdated);
        return true;
    }

    /// <summary>
    /// Fire a worker by ID (Phase 3.1, free).
    /// Returns true on success, false if worker not found.
    /// </summary>
    public bool FireWorker(string workerId)
    {
        var state = SimulationEngine?.State;
        if (state?.Crew == null) return false;
        int removed = state.Crew.RemoveAll(c => c.Id == workerId);
        if (removed > 0) EmitSignal(SignalName.GameStateUpdated);
        return removed > 0;
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

        // UI passes snake_case ("semi_submersible") — normalize before parse.
        var normalizedType = (farmType ?? "").Replace("_", "").Replace(" ", "");
        if (!Enum.TryParse<LakskongenSim.Farm.OffshoreType>(normalizedType, true, out var offshoreType))
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
            result[kvp.Key] = Variant.From(kvp.Value);
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
                dict[kvp.Key] = Variant.From(kvp.Value);
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
            result[kvp.Key] = Variant.From(kvp.Value);
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
            result[kvp.Key] = Variant.From(kvp.Value);
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
                dict[kvp.Key] = Variant.From(kvp.Value);
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
            result[kvp.Key] = Variant.From(kvp.Value);
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
                dict[kvp.Key] = Variant.From(kvp.Value);
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

