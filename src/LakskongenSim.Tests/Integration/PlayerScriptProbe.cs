using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using Xunit;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// PlayerScriptProbe (NEXT_LEVEL_PLAN Phase 1): scripted careers driven through
/// the SAME commands the Godot UI uses (SimulationEngine stock/harvest/loan/
/// insurance), with events ON, zones ON and a mid-run save/load round trip that
/// adopts state through the identical engine path as GameBridge.LoadGame
/// (SimulationEngine.AdoptSavedState). One implementation, one path.
/// <para>
/// Known determinism caveat (documented, fixed in the SimRng work):
/// disease-RNG streams (IntegratedDiseaseSystem, StochasticDiseaseEngine,
/// DiseaseLODSystem) are still unseeded, so band values can shift between
/// runs of the same seed; the assertions use wide bands accordingly.
/// </para>
/// </summary>
public static class PlayerScriptProbe
{
    /// <summary>Scenario for the scripted career (red-zone regulatory scenario with pre-stocked zones).</summary>
    public const string CareerScenarioId = "po3_red_sone";

    /// <summary>Site mean weight that triggers harvest (operational margin vs calibrated 5.2 kg).</summary>
    public const double HarvestTriggerWeightKg = 4.5;

    /// <summary>Skip-ahead chunk for the engine day loop (bulk Advance keeps the probe fast).</summary>
    public const int AdvanceChunkDays = 15;

    /// <summary>Latest day the career may run before the test fails (guard against silent non-growth).</summary>
    public const int CareerHorizonDays = 1200;

    /// <summary>Mid-run save/load happens at this day.</summary>
    public const int SaveLoadDay = 300;

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

    /// <summary>
    /// Run the red-zone career from scenario start to harvest, optionally with
    /// a mid-run save/load round trip through the same adoption path the game
    /// uses. Bands are asserted by the caller; this is the harness.
    /// </summary>
    public static CareerResult RunCareer(int seed, bool roundTripSaveLoad = true)
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: true, seed: seed);

        var scenario = engine.Scenario.GetScenario(CareerScenarioId);
        Assert.NotNull(scenario);
        scenario.ApplyStartingState(engine.State, engine);

        var result = new CareerResult();

        // ===== Player verbs through the SAME surface the game uses =====
        Assert.True(engine.StockSmolts("po3_nord", 12_000, "robust").Ok, "stock po3_nord failed");
        Assert.True(engine.StockSmolts("po3_sor", 10_000, "standard").Ok, "stock po3_sor failed");
        Assert.True(engine.SetInsurancePolicy("mortality", true, 1.0, 250_000).Ok, "mortality insurance failed");
        Assert.True(engine.SetInsurancePolicy("storm", true, 1.0, 150_000).Ok, "storm insurance failed");
        var loan = engine.TakeLoan(4_000_000, 36, 0.07);
        Assert.True(loan.Ok, $"loan failed: {loan.ErrorKey}");
        Assert.False(string.IsNullOrEmpty(loan.Id), "loan must return an id");

        // Wellboat verbs through the same surface the game uses: an ROV
        // inspection (net cleaning) and a contracted delousing. Both must be
        // executed by the engine in the daily advance and reported as notes.
        Assert.True(engine.RequestWellboat("po3_nord", "rov").Ok, "ROV booking failed");
        Assert.True(engine.RequestWellboat("po3_sor", "delouse", "hydrolicer").Ok, "wellboat delouse booking failed");

        double fishBeforeGrowOut = TotalFish(engine.State);
        // Feed/growth counters reset on the resumed engine, so accumulate in
        // segments: bank the delta each loop iteration, re-anchoring the
        // counters after the save/load round trip.
        double lastFeedUsed = engine.Feed.TotalFeedUsedKg;
        double lastGrowth = engine.Feed.TotalGrowthKg;
        double feedAccumulator = 0.0;
        double growthAccumulator = 0.0;

        bool saveLoadDone = false;
        while (engine.State.Day < CareerHorizonDays)
        {
            engine.Advance(AdvanceChunkDays);
            foreach (var note in engine.DrainDayNotes())
                result.NoteKinds.Add(note.Kind);
            feedAccumulator += engine.Feed.TotalFeedUsedKg - lastFeedUsed;
            growthAccumulator += engine.Feed.TotalGrowthKg - lastGrowth;
            lastFeedUsed = engine.Feed.TotalFeedUsedKg;
            lastGrowth = engine.Feed.TotalGrowthKg;
            result.PeakAdultFemaleLice = Math.Max(result.PeakAdultFemaleLice, PeakLice(engine.State));
            result.EventLogEntries = Math.Max(result.EventLogEntries, engine.State.EventLog.Count);

            Assert.True(double.IsFinite((double)engine.FinanceManager.Balance), "balance went non-finite");
            Assert.True(engine.FinanceManager.Balance > -100_000_000m,
                $"runaway debt: {engine.FinanceManager.Balance:N0} NOK");

            if (roundTripSaveLoad && !saveLoadDone && engine.State.Day >= SaveLoadDay)
            {
                saveLoadDone = true;
                engine = ResumeAfterSaveLoadRoundTrip(engine, seed);
                result.SaveLoadRoundTripped = true;
                lastFeedUsed = 0.0;   // resumed engine counters start at 0
                lastGrowth = 0.0;
                continue;
            }

            if (AllSitesAtHarvestWeight(engine.State, HarvestTriggerWeightKg))
                break;
        }

        Assert.True(!roundTripSaveLoad || saveLoadDone, "save/load round trip never ran (horizon hit first)");
        Assert.True(AllSitesAtHarvestWeight(engine.State, HarvestTriggerWeightKg),
            $"career never reached harvest weight within {CareerHorizonDays} days");

        double fishBeforeHarvest = TotalFish(engine.State);
        int nonEmptySitesBeforeHarvest = engine.State.SeaSites.Count(s => SiteFishCount(s) > 0);

        // ===== Harvest through the command surface, per site =====
        int sitesHarvested = 0;
        foreach (var site in engine.State.SeaSites.OrderBy(s => s.Id).ToList())
        {
            var cmd = engine.HarvestSite(site.Id);
            if (!cmd.Ok)
            {
                Assert.Equal("site_empty", cmd.ErrorKey);
                continue;
            }
            sitesHarvested++;
            result.HarvestedKg += cmd.Value;
            result.RevenueNok += cmd.Value2;
        }

        result.HarvestedAllSites = sitesHarvested == nonEmptySitesBeforeHarvest;
        result.DaysRun = engine.State.Day;
        result.EndingBalance = engine.FinanceManager.Balance;
        result.RealizedPricePerKg = result.HarvestedKg > 0.001
            ? result.RevenueNok / result.HarvestedKg
            : 0.0;
        result.EconomicFcr = growthAccumulator > 0.001
            ? feedAccumulator / growthAccumulator
            : double.MaxValue;
        result.SeaMortalityPct = fishBeforeGrowOut > 0
            ? (fishBeforeGrowOut - fishBeforeHarvest) / fishBeforeGrowOut * 100.0
            : 0.0;

        return result;
    }

    /// <summary>
    /// Mid-run save/load: write via the library SaveLoadManager, then resume on
    /// a fresh engine through <see cref="SimulationEngine.AdoptSavedState"/> —
    /// the same adoption path GameBridge.LoadGame drives. Two honest caveats:
    /// (1) the library serializer writes PascalCase/enum-strings while the game
    /// writes camelCase/enum-ints — adoption is unified, the formats are not
    /// (ISnapshotable rewrite, per plan); (2) bridge-only extras on load
    /// (mitigation-registry restore, feed-recipe re-apply) are not exercised
    /// because this career uses neither — a future wave must close that gap.
    /// </summary>
    private static SimulationEngine ResumeAfterSaveLoadRoundTrip(SimulationEngine engine, int seed)
    {
        engine.PrepareForSave();
        string tmpDir = Path.Combine(Path.GetTempPath(), "lks_probe_" + Guid.NewGuid().ToString("N"));
        try
        {
            var saveLoad = new SaveLoadManager(tmpDir);
            saveLoad.SaveGame(engine.State, "midrun");
            var snapshot = saveLoad.LoadGame("midrun");
            Assert.NotNull(snapshot);
            Assert.Equal(engine.State.Day, snapshot.Day);
            Assert.True(snapshot.SeaSites.Count == 2, "site count lost in save");
            Assert.True(snapshot.ActiveLoans.Count == 1, "loan liability lost in save");

            // Same adoption path as GameBridge.LoadGame (headless).
            var resumed = new SimulationEngine();
            resumed.FinanceManager = new FinanceManager(snapshot.Balance);
            int? seedArg = snapshot.RandomSeed != 0 ? snapshot.RandomSeed : null;
            resumed.Initialize(enableEvents: true, seed: seedArg ?? seed);
            resumed.AdoptSavedState(snapshot);

            // Continuity guards: the adopted state carries the mid-run world.
            Assert.Equal(snapshot.Day, resumed.State.Day);
            Assert.True(resumed.State.SeaSites.Count == 2, "sites lost on resume");
            Assert.True(resumed.State.ActiveLoans.Count == 1, "liability lost on resume");
            Assert.True(resumed.State.Populations.Count > 0, "no populations after load");
            Assert.True(resumed.State.EventLog.Count >= snapshot.EventLog.Count, "event log shrank on load");

            return resumed;
        }
        finally
        {
            try { Directory.Delete(tmpDir, true); } catch { /* best effort */ }
        }
    }

    private static double TotalFish(GameState state)
        => state.SeaSites.Sum(s => s.Pens.Sum(p => (double)p.TotalCount));

    private static int SiteFishCount(SeaSite site) => site.Pens.Sum(p => p.TotalCount);

    private static double PeakLice(GameState state)
        => state.SeaSites.Count == 0
            ? 0.0
            : state.SeaSites.Max(s => s.Lice.AdultFemalePerFish);

    private static double SiteMeanWeightKg(SeaSite site)
    {
        double biomass = site.Pens.Sum(p => p.TotalCount > 0 ? p.GetTotalBiomassKg() : 0.0);
        int count = SiteFishCount(site);
        return count > 0 ? biomass / count : 0.0;
    }

    private static bool AllSitesAtHarvestWeight(GameState state, double kg)
        => state.SeaSites.Count > 0 && state.SeaSites.All(s => SiteMeanWeightKg(s) >= kg);
}