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
/// Every subsystem stream is seeded and saved, so a career is exactly
/// reproducible for a seed and the round-tripped career must equal the
/// uninterrupted one (see <see cref="CareerResult.FinalStateJson"/>).
/// </para>
/// </summary>
public static class PlayerScriptProbe
{
    /// <summary>Scenario for the scripted career (red-zone regulatory scenario with pre-stocked zones).</summary>
    public const string CareerScenarioId = "po3_red_sone";

    /// <summary>
    /// Harvest rule (roadmap D2): a site at or above this mean weight is sold
    /// on a day when spot is at or above its <see cref="SpotMeanWindowDays"/>-day
    /// mean, like a farmer who waits out a price trough.
    /// </summary>
    public const double HarvestTriggerWeightKg = 4.5;

    /// <summary>Harvest rule (roadmap D2): a site at this mean weight is sold whatever the price.</summary>
    public const double ForcedHarvestWeightKg = 6.0;

    /// <summary>Window of the spot mean the harvest rule compares against (days).</summary>
    public const int SpotMeanWindowDays = 30;

    /// <summary>Days the probe skips while no site is ready to sell (it advances one day at a time once one is).</summary>
    public const int AdvanceChunkDays = 15;

    /// <summary>Latest day the career may run before the test fails (guard against silent non-growth).</summary>
    public const int CareerHorizonDays = 1200;

    /// <summary>
    /// Mid-run save/load happens at this day, while both sites still hold fish.
    /// It was day 300; with F1024's exact oxygen (no daily hypoxia penalty) the
    /// careers reach harvest weight from about day 280, so a day-300 save could
    /// land on an empty farm and round-trip nothing alive (S1-01/S1-02).
    /// </summary>
    public const int SaveLoadDay = 210;

    public sealed class CareerResult
    {
        /// <summary>Live kilos slaughtered.</summary>
        public double HarvestedKg = 0.0;
        /// <summary>Gutted (HOG) kilos sold (S2-04).</summary>
        public double HarvestedHogKg = 0.0;
        /// <summary>Reference-equivalent kilos sold: HOG kg × size-class × grade factors (S2-04).</summary>
        public double ReferenceEquivalentKg = 0.0;
        /// <summary>Gross sales (NOK), as booked as harvest revenue.</summary>
        public double RevenueNok = 0.0;
        /// <summary>Harvest cost (NOK), as booked as cost of goods sold (S2-04, F270).</summary>
        public double HarvestCostNok = 0.0;
        /// <summary>
        /// Price realized per reference-equivalent kilo (NOK/kg): gross sales over
        /// the HOG kilos weighted by their size-class and grade factors, i.e. the
        /// price per kilo of the product the spot is quoted for (superior HOG).
        /// Before S2-04 the game sold live kilos at that price.
        /// </summary>
        public double RealizedPricePerKg = 0.0;
        /// <summary>Price achievement: gross sales per HOG kilo over the spot of the harvest days (S2-04).</summary>
        public double PriceAchievement = 0.0;
        /// <summary>Superior share of the HOG kilos sold (S2-04).</summary>
        public double SuperiorShare = 0.0;
        public double EconomicFcr = 0.0;
        public double SeaMortalityPct = 0.0;
        public double PeakAdultFemaleLice = 0.0;
        /// <summary>Spot price on each slaughter day, weighted by the reference-equivalent kilos sold that day (NOK/kg).</summary>
        public double MeanSpotOnHarvestDaysNok = 0.0;
        /// <summary>Mean of every daily spot price the career saw (NOK/kg).</summary>
        public double MeanSpotOverCareerNok = 0.0;
        /// <summary>Sites sold because they reached <see cref="ForcedHarvestWeightKg"/>.</summary>
        public int ForcedHarvests = 0;
        /// <summary>Mean weight of each site when it was sold (kg).</summary>
        public List<double> HarvestWeightsKg = new();
        public int DaysRun = 0;
        public int EventLogEntries = 0;
        public bool SaveLoadRoundTripped = false;
        public bool HarvestedAllSites = false;
        public decimal EndingBalance = 0m;
        public List<string> NoteKinds = new();
        /// <summary>GUID-normalized save JSON at the end of the career (exact-continuation checks).</summary>
        public string FinalStateJson = "";
        /// <summary>
        /// The career on the mortality ledger (S2-05): every death by cause and
        /// category, and every fish that left alive (escape, harvest, forced
        /// harvest, transfer), from the scenario start to the end of the career.
        /// </summary>
        public LakskongenSim.Biology.MortalityBreakdown Mortality;
        /// <summary>Fish in the sea sites after the scenario start and the probe's stocking.</summary>
        public double FishBeforeGrowOut = 0.0;
        /// <summary>
        /// Fish the career's harvest orders slaughtered (summed over the
        /// S2-04 harvest reports): every one must be on the ledger as a harvest.
        /// </summary>
        public double FishAtHarvest = 0.0;
        /// <summary>Fish still in the sea sites when the career ended.</summary>
        public double FishLeftAtEnd = 0.0;
        /// <summary>Harvest orders the career placed (S2-04).</summary>
        public int HarvestOrders = 0;
        /// <summary>Slaughter days the orders took (S2-04).</summary>
        public int SlaughterDays = 0;
    }

    /// <summary>
    /// The probe's mortality printout (S2-05): the career's deaths in the NVI
    /// categories with their causes, then the fish that left alive.
    /// </summary>
    public static string FormatMortality(int seed, CareerResult r)
        => r.Mortality.FormatTable(
            $"{CareerScenarioId} seed {seed}: {r.FishBeforeGrowOut:N0} fish at the start, {r.DaysRun} days, " +
            $"sea loss {r.SeaMortalityPct:F2} % (deaths + escapes + forced harvest)");

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
        var orderedSites = new HashSet<string>();
        var orderIds = new HashSet<string>();
        // Daily spot as the career saw it (kept across the save/load round trip,
        // like the player's memory of the market).
        var spotHistory = new List<double>();
        int sitesStocked = engine.State.SeaSites.Count(s => SiteFishCount(s) > 0);
        while (engine.State.Day < CareerHorizonDays)
        {
            bool busy = engine.HarvestOrders.Count > 0 || AnySiteReadyToSell(engine.State, orderedSites);
            int step = busy ? 1 : AdvanceChunkDays;
            for (int d = 0; d < step; d++)
            {
                engine.Advance(1);
                spotHistory.Add((double)engine.MarketSimulation.PricePerKg);
            }
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
                // The load restores the feed totals (review F896: re-anchoring at
                // 0 counted the pre-save feed twice).
                lastFeedUsed = engine.Feed.TotalFeedUsedKg;
                lastGrowth = engine.Feed.TotalGrowthKg;
                // No `continue`: the resumed engine is on the same day, so it takes
                // the same harvest/termination check as the uninterrupted career.
                // Skipping it ran a career that finished on the save day one
                // extra chunk (15 days) and broke the exact-continuation check.
            }

            // ===== Harvest through the command surface, per site =====
            // Each site is sold on its own (holding a ready site for the slowest
            // one feeds fish at their growth ceiling for months, review F1130).
            // Rule (roadmap D2): from 4.5 kg, order the harvest on a day when spot
            // is at or above its 30-day mean; at 6 kg, order it whatever the price.
            // S2-04: the order is the planned pipeline the harvest planner books:
            // the fish fast, the wellboat comes on its slot and the slaughterhouse
            // takes them within its daily capacity, each batch at that day's price.
            double spotToday = (double)engine.MarketSimulation.PricePerKg;
            double spotMean = spotHistory.Skip(Math.Max(0, spotHistory.Count - SpotMeanWindowDays)).Average();
            foreach (var site in engine.State.SeaSites.OrderBy(s => s.Id).ToList())
            {
                if (orderedSites.Contains(site.Id) || SiteFishCount(site) == 0) continue;
                double weightKg = SiteMeanWeightKg(site);
                if (weightKg < HarvestTriggerWeightKg) continue;
                bool forced = weightKg >= ForcedHarvestWeightKg;
                if (!forced && spotToday < spotMean) continue;
                var cmd = engine.OrderHarvest(site.Id);
                Assert.True(cmd.Ok, $"harvest order at {site.Id} failed: {cmd.ErrorKey}");
                orderedSites.Add(site.Id);
                orderIds.Add(cmd.Id);
                result.HarvestOrders++;
                result.HarvestWeightsKg.Add(weightKg);
                if (forced) result.ForcedHarvests++;
            }

            // Both careers (with and without the round trip) run at least to
            // the save day, so they stay comparable day for day.
            if (engine.State.Day >= SaveLoadDay && engine.HarvestOrders.Count == 0 &&
                engine.State.SeaSites.All(s => orderedSites.Contains(s.Id) || SiteFishCount(s) == 0))
                break;
        }

        Assert.True(!roundTripSaveLoad || saveLoadDone, "save/load round trip never ran (horizon hit first)");
        Assert.True(orderedSites.Count > 0 && engine.HarvestOrders.Count == 0
            && engine.State.SeaSites.All(s => orderedSites.Contains(s.Id) || SiteFishCount(s) == 0),
            $"career never reached harvest weight on every site within {CareerHorizonDays} days");

        // The harvest reports carry what each slaughtered batch sold, exactly as booked.
        double fishSlaughtered = 0.0, spotTimesReferenceKg = 0.0, spotTimesHogKg = 0.0, superiorKg = 0.0;
        var slaughterDays = new HashSet<int>();
        foreach (var report in engine.HarvestReports.Where(r => orderIds.Contains(r.OrderId)))
        {
            fishSlaughtered += report.Fish;
            result.HarvestedKg += report.LiveKg;
            result.HarvestedHogKg += report.HogKg;
            result.ReferenceEquivalentKg += report.ReferenceEquivalentKg;
            result.RevenueNok += (double)report.GrossRevenueNok;
            result.HarvestCostNok += (double)report.HarvestCostNok;
            spotTimesReferenceKg += (double)report.ReferencePriceNok * report.ReferenceEquivalentKg;
            spotTimesHogKg += (double)report.ReferencePriceNok * report.HogKg;
            superiorKg += report.SuperiorShare * report.HogKg;
            slaughterDays.Add(report.Day);
        }
        result.SlaughterDays = slaughterDays.Count;
        result.HarvestedAllSites = orderedSites.Count == sitesStocked;
        result.DaysRun = engine.State.Day;
        result.EndingBalance = engine.FinanceManager.Balance;
        result.RealizedPricePerKg = result.ReferenceEquivalentKg > 0.001
            ? result.RevenueNok / result.ReferenceEquivalentKg
            : 0.0;
        result.MeanSpotOnHarvestDaysNok = result.ReferenceEquivalentKg > 0.001
            ? spotTimesReferenceKg / result.ReferenceEquivalentKg
            : 0.0;
        result.PriceAchievement = spotTimesHogKg > 0.001 ? result.RevenueNok / spotTimesHogKg : 0.0;
        result.SuperiorShare = result.HarvestedHogKg > 0.001 ? superiorKg / result.HarvestedHogKg : 0.0;
        result.MeanSpotOverCareerNok = spotHistory.Count > 0 ? spotHistory.Average() : 0.0;
        result.EconomicFcr = growthAccumulator > 0.001
            ? feedAccumulator / growthAccumulator
            : double.MaxValue;
        result.FinalStateJson = SaveStateComparer.Normalize(SaveStateComparer.Serialize(engine));
        result.SeaMortalityPct = fishBeforeGrowOut > 0
            ? (fishBeforeGrowOut - fishSlaughtered) / fishBeforeGrowOut * 100.0
            : 0.0;
        result.Mortality = engine.MortalityLedger.Cumulative();
        result.FishBeforeGrowOut = fishBeforeGrowOut;
        result.FishAtHarvest = fishSlaughtered;
        result.FishLeftAtEnd = TotalFish(engine.State);

        return result;
    }

    /// <summary>
    /// Mid-run save/load: write via the library SaveLoadManager, then resume on
    /// a fresh engine through <see cref="SimulationEngine.AdoptSavedState"/> —
    /// the same adoption path GameBridge.LoadGame drives. Since Phase 1 wave 5
    /// both sides share the ONE serializer (SaveJson.Options), and every RNG
    /// stream position is restored, so the resumed career must end in exactly
    /// the uninterrupted career's state (PlayerScriptCareerTests, review F884).
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

    /// <summary>True when an unsold, stocked site has reached the harvest-rule weight.</summary>
    private static bool AnySiteReadyToSell(GameState state, HashSet<string> harvestedSites)
        => state.SeaSites.Any(s => !harvestedSites.Contains(s.Id) && SiteFishCount(s) > 0
            && SiteMeanWeightKg(s) >= HarvestTriggerWeightKg);
}
