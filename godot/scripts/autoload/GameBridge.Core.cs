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
        // S10: the live game was never seeded — every subsystem fell back to
        // unseeded System.Random. Pick one seed, persist it on the state.
        int seed = new System.Random().Next();
        SimulationEngine.Initialize(seed: seed);
        SimulationEngine.State.RandomSeed = seed;

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
        // S10: resume with the stored seed when present. NOTE: subsystem RNG
        // *positions* are not persisted (see ISnapshotable plan item), so a
        // loaded game replays the same seed stream from day 0 rather than
        // continuing the exact pre-save trajectory.
        int? seed = snapshot.RandomSeed != 0 ? snapshot.RandomSeed : null;
        SimulationEngine.Initialize(seed: seed);

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
    /// <summary>Phase 2.1: biomass-weighted mean across site lice models
    /// (falls back to the legacy company model when nothing is stocked).</summary>
    public double SeaLicePerFish => SimulationEngine?.GetCompanyLiceAverage() ?? 0.0;

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

            // Phase 1.2 / v1.0.3: net-integrity wear and storm escapes are now
            // owned by the engine (SimulationEngine.ApplyStormHazards), the single
            // source of truth. GameBridge no longer mutates NetIntegrityPct or
            // records escapes here, which prevents double-escape accounting.
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
