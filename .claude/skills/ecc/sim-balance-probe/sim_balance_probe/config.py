"""
Sim Balance Probe — Configuration
"""

import os
from pathlib import Path
from typing import Dict, List, Tuple, Optional

# ─── Paths ───
REPO_ROOT = Path(__file__).resolve().parents[5]  # lakskongen/ (config.py -> sim_balance_probe -> sim-balance-probe -> ecc -> skills -> .claude -> lakskongen)
SRC_ROOT = REPO_ROOT / "src"
TEST_PROJECT = SRC_ROOT / "LakskongenSim.Tests" / "LakskongenSim.Tests.csproj"
ARTIFACTS_DIR = REPO_ROOT / "artifacts" / "balance_probe"

# ─── Scenarios ───
# Empty list = auto-discover all unlocked from ScenarioSystem
SCENARIOS: List[str] = [
    "po3_red_sone",
    "forste_sesong",
    "lofoten_eventyr",
    "laksebaronene",
    "krise_manager",
    "barekraft_mester",
    "isa_utbrudd",
]

# ─── Run Configuration ───
DEFAULT_SEEDS = 100
SEED_START = 1
CAREER_HORIZON_DAYS = 1200
SAVE_LOAD_DAY = 300
ADVANCE_CHUNK_DAYS = 15

# ─── CI Gate Bands (p95) ───
# Metric -> (lower_bound, upper_bound); upper_bound can be float('inf')
CI_BANDS: Dict[str, Tuple[float, float]] = {
    "mortality_pct_per_cycle": (14.0, 16.0),
    "fcr": (1.15, 1.35),
    "realized_price_per_kg": (70.0, 96.0),
    "ending_equity_nok": (0.0, float("inf")),
}

# ─── Sobol Sensitivity Parameters ───
# param_name -> (min_multiplier, max_multiplier) relative to calibrated defaults
SOBOL_PARAMS: Dict[str, Tuple[float, float]] = {
    "sea_mortality": (0.8, 1.2),      # base mortality rate multiplier
    "feed_cost": (0.85, 1.15),         # feed cost NOK/kg multiplier
    "lice_pressure": (0.7, 1.5),       # external lice pressure multiplier
    "storm_loss": (0.5, 2.0),          # storm containment failure prob multiplier
}

# ─── Output ───
DEFAULT_OUT_DIR = ARTIFACTS_DIR
CSV_PATTERN = "balance_probe_{scenario}_{timestamp}.csv"
HTML_PATTERN = "balance_probe_{scenario}_{timestamp}.html"
SOBOL_PATTERN = "sobol_{scenario}_{timestamp}.json"

# ─── Parallel Execution ───
DEFAULT_PARALLEL_JOBS = min(8, (os.cpu_count() or 4))

# ─── .NET Runner Configuration ───
DOTNET_RUNNER_NAME = "SimBalanceProbeRunner"
DOTNET_RUNNER_CODE = '''
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LakskongenSim.Core;
using LakskongenSim.Farm;
using LakskongenSim.Finance;

class SimBalanceProbeRunner
{
    static int Main(string[] args)
    {
        if (args.Length < 4)
        {
            Console.Error.WriteLine("Usage: SimBalanceProbeRunner <scenario> <seed> <horizonDays> [saveLoad]");
            return 1;
        }

        string scenarioId = args[0];
        int seed = int.Parse(args[1]);
        int horizonDays = int.Parse(args[2]);
        bool saveLoad = args.Length > 3 ? bool.Parse(args[3]) : true;

        try
        {
            var engine = new SimulationEngine();
            engine.Initialize(enableEvents: true, seed: seed);

            var scenario = engine.Scenario.GetScenario(scenarioId);
            if (scenario == null)
            {
                Console.Error.WriteLine($"Scenario not found: {scenarioId}");
                return 1;
            }
            engine.Scenario.StartScenario(scenarioId);
            scenario.ApplyStartingState(engine.State, engine);

            // Player verbs through the SAME surface the game uses
            var stockNord = engine.StockSmolts("po3_nord", 12_000, "robust");
            var stockSor = engine.StockSmolts("po3_sor", 10_000, "standard");
            var mortalityIns = engine.SetInsurancePolicy("mortality", true, 1.0, 250_000);
            var stormIns = engine.SetInsurancePolicy("storm", true, 1.0, 150_000);
            var loan = engine.TakeLoan(4_000_000, 36, 0.07);
            var rov = engine.RequestWellboat("po3_nord", "rov");
            var delouse = engine.RequestWellboat("po3_sor", "delouse", "hydrolicer");

            if (!stockNord.Ok || !stockSor.Ok || !mortalityIns.Ok || !stormIns.Ok || !loan.Ok || !rov.Ok || !delouse.Ok)
            {
                Console.Error.WriteLine($"Setup failed: nord={stockNord.Ok}/{stockNord.ErrorKey}, sor={stockSor.Ok}/{stockSor.ErrorKey}, mortIns={mortalityIns.Ok}/{mortalityIns.ErrorKey}, stormIns={stormIns.Ok}/{stormIns.ErrorKey}, loan={loan.Ok}/{loan.ErrorKey}, rov={rov.Ok}/{rov.ErrorKey}, delouse={delouse.Ok}/{delouse.ErrorKey}");
                return 1;
            }

            double fishBeforeGrowOut = TotalFish(engine.State);
            double lastFeedUsed = engine.Feed.TotalFeedUsedKg;
            double lastGrowth = engine.Feed.TotalGrowthKg;
            double feedAccumulator = 0.0;
            double growthAccumulator = 0.0;
            double peakLice = 0.0;
            int eventLogEntries = 0;
            List<string> noteKinds = new();
            bool saveLoadDone = false;

            const int AdvanceChunkDays = 15;
            int saveLoadDay = Math.Min(300, horizonDays / 4);

            while (engine.State.Day < horizonDays)
            {
                engine.Advance(AdvanceChunkDays);
                foreach (var note in engine.DrainDayNotes())
                    noteKinds.Add(note.Kind);
                
                feedAccumulator += engine.Feed.TotalFeedUsedKg - lastFeedUsed;
                growthAccumulator += engine.Feed.TotalGrowthKg - lastGrowth;
                lastFeedUsed = engine.Feed.TotalFeedUsedKg;
                lastGrowth = engine.Feed.TotalGrowthKg;
                peakLice = Math.Max(peakLice, PeakLice(engine.State));
                eventLogEntries = Math.Max(eventLogEntries, engine.State.EventLog.Count);

                if (!double.IsFinite((double)engine.FinanceManager.Balance))
                {
                    Console.Error.WriteLine("Balance went non-finite");
                    return 1;
                }

                if (saveLoad && !saveLoadDone && engine.State.Day >= saveLoadDay)
                {
                    saveLoadDone = true;
                    engine = ResumeAfterSaveLoadRoundTrip(engine, seed);
                    lastFeedUsed = 0.0;
                    lastGrowth = 0.0;
                    continue;
                }

                if (AllSitesAtHarvestWeight(engine.State, 4.5))
                {
                    break;
                }
            }

            if (saveLoad && !saveLoadDone)
            {
                Console.Error.WriteLine("Save/load round trip never ran (horizon hit first)");
                return 1;
            }
            if (!AllSitesAtHarvestWeight(engine.State, 4.5))
            {
                Console.Error.WriteLine($"Career never reached harvest weight within {horizonDays} days");
                return 1;
            }

            double fishBeforeHarvest = TotalFish(engine.State);
            int nonEmptySitesBeforeHarvest = engine.State.SeaSites.Count(s => SiteFishCount(s) > 0);

            int sitesHarvested = 0;
            double harvestedKg = 0.0;
            double revenueNok = 0.0;
            foreach (var site in engine.State.SeaSites.OrderBy(s => s.Id).ToList())
            {
                var cmd = engine.HarvestSite(site.Id);
                if (!cmd.Ok)
                {
                    if (cmd.ErrorKey != "site_empty")
                    {
                        Console.Error.WriteLine($"Harvest failed: {cmd.ErrorKey}");
                        return 1;
                    }
                    continue;
                }
                sitesHarvested++;
                harvestedKg += cmd.Value;
                revenueNok += cmd.Value2;
            }

            bool harvestedAllSites = sitesHarvested == nonEmptySitesBeforeHarvest;
            int daysRun = engine.State.Day;
            decimal endingBalance = engine.FinanceManager.Balance;
            double realizedPricePerKg = harvestedKg > 0.001 ? revenueNok / harvestedKg : 0.0;
            double economicFcr = growthAccumulator > 0.001 ? feedAccumulator / growthAccumulator : double.MaxValue;
            double seaMortalityPct = fishBeforeGrowOut > 0 ? (fishBeforeGrowOut - fishBeforeHarvest) / fishBeforeGrowOut * 100.0 : 0.0;

            var result = new CareerResult
            {
                HarvestedKg = harvestedKg,
                RevenueNok = revenueNok,
                RealizedPricePerKg = realizedPricePerKg,
                EconomicFcr = economicFcr,
                SeaMortalityPct = seaMortalityPct,
                PeakAdultFemaleLice = peakLice,
                DaysRun = daysRun,
                EventLogEntries = eventLogEntries,
                SaveLoadRoundTripped = saveLoadDone,
                HarvestedAllSites = harvestedAllSites,
                EndingBalance = endingBalance,
                NoteKinds = noteKinds,
            };
            
            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                IncludeFields = true,
            });
            Console.WriteLine(json);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    private static double TotalFish(GameState state)
        => state.SeaSites.Sum(s => s.Pens.Sum(p => (double)p.TotalCount));

    private static int SiteFishCount(SeaSite site) => site.Pens.Sum(p => p.TotalCount);

    private static double PeakLice(GameState state)
        => state.SeaSites.Count == 0 ? 0.0 : state.SeaSites.Max(s => s.Lice.AdultFemalePerFish);

    private static double SiteMeanWeightKg(SeaSite site)
    {
        double biomass = site.Pens.Sum(p => p.TotalCount > 0 ? p.GetTotalBiomassKg() : 0.0);
        int count = SiteFishCount(site);
        return count > 0 ? biomass / count : 0.0;
    }

    private static bool AllSitesAtHarvestWeight(GameState state, double kg)
        => state.SeaSites.Count > 0 && state.SeaSites.All(s => SiteMeanWeightKg(s) >= kg);

    private static SimulationEngine ResumeAfterSaveLoadRoundTrip(SimulationEngine engine, int seed)
    {
        engine.PrepareForSave();
        string tmpDir = Path.Combine(Path.GetTempPath(), "lks_probe_" + Guid.NewGuid().ToString("N"));
        try
        {
            var saveLoad = new SaveLoadManager(tmpDir);
            saveLoad.SaveGame(engine.State, "midrun");
            var snapshot = saveLoad.LoadGame("midrun");
            if (snapshot == null) throw new Exception("Snapshot is null");
            if (snapshot.SeaSites.Count != 2) throw new Exception("site count lost in save");
            if (snapshot.ActiveLoans.Count != 1) throw new Exception("loan liability lost in save");

            var resumed = new SimulationEngine();
            resumed.FinanceManager = new FinanceManager(snapshot.Balance);
            int? seedArg = snapshot.RandomSeed != 0 ? snapshot.RandomSeed : null;
            resumed.Initialize(enableEvents: true, seed: seedArg ?? seed);
            resumed.AdoptSavedState(snapshot);

            if (resumed.State.Day != snapshot.Day) throw new Exception("Day mismatch on resume");
            if (resumed.State.SeaSites.Count != 2) throw new Exception("sites lost on resume");
            if (resumed.State.ActiveLoans.Count != 1) throw new Exception("liability lost on resume");
            if (resumed.State.Populations.Count == 0) throw new Exception("no populations after load");
            if (resumed.State.EventLog.Count < snapshot.EventLog.Count) throw new Exception("event log shrank on load");

            return resumed;
        }
        finally
        {
            try { Directory.Delete(tmpDir, true); } catch { }
        }
    }

    public sealed class CareerResult
    {
        public double HarvestedKg = 0.0;
        public double RevenueNok = 0.0;
        public double RealizedPricePerKg = 0.0;
        public double EconomicFcr = 0.0;
        public double SeaMortalityPct = 0.0;
        public double PeakAdultFemaleLice = 0.0;
        public int DaysRun = 0;
        public int EventLogEntries = 0;
        public bool SaveLoadRoundTripped = false;
        public bool HarvestedAllSites = false;
        public decimal EndingBalance = 0m;
        public List<string> NoteKinds = new();
    }
}
'''

# ─── Metric Extraction Mapping ───
# CareerResult property -> output metric name
METRIC_MAP = {
    "HarvestedKg": "harvested_kg",
    "RevenueNok": "revenue_nok",
    "RealizedPricePerKg": "realized_price_per_kg",
    "EconomicFcr": "fcr",
    "SeaMortalityPct": "mortality_pct_per_cycle",
    "PeakAdultFemaleLice": "peak_lice_per_fish",
    "DaysRun": "days_to_harvest",
    "EventLogEntries": "event_count",
    "SaveLoadRoundTripped": "save_load_ok",
    "HarvestedAllSites": "harvested_all_sites",
    "EndingBalance": "ending_balance_nok",
    "NoteKinds": "note_kinds",
}

# ─── Additional Engine Metrics (require engine instance) ───
ENGINE_METRICS = [
    "storm_escape_events",
    "sustainability_score",
    "total_loan_principal_nok",
    "ending_equity_nok",
]


def get_out_dir() -> Path:
    """Get output directory, creating if needed."""
    out_dir = Path(os.environ.get("SIM_BALANCE_PROBE_OUT_DIR", DEFAULT_OUT_DIR))
    out_dir.mkdir(parents=True, exist_ok=True)
    return out_dir


def get_scenarios() -> List[str]:
    """Get scenarios to run. If SCENARIOS empty, return all known."""
    if SCENARIOS:
        return SCENARIOS
    # Fallback: return known scenarios
    return [
        "po3_red_sone",
        "forste_sesong",
        "lofoten_eventyr",
        "laksebaronene",
        "krise_manager",
        "barekraft_mester",
        "isa_utbrudd",
    ]


def get_ci_bands() -> Dict[str, Tuple[float, float]]:
    """Get CI gate bands, allowing env override."""
    bands = CI_BANDS.copy()
    # Allow env override: SIM_BALANCE_CI_MORTALITY_MIN=10 SIM_BALANCE_CI_MORTALITY_MAX=20
    for metric in bands:
        min_key = f"SIM_BALANCE_CI_{metric.upper()}_MIN"
        max_key = f"SIM_BALANCE_CI_{metric.upper()}_MAX"
        if min_key in os.environ or max_key in os.environ:
            lower = float(os.environ.get(min_key, bands[metric][0]))
            upper = float(os.environ.get(max_key, bands[metric][1]))
            bands[metric] = (lower, upper)
    return bands