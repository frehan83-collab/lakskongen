using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LakskongenSim.Core;
using LakskongenSim.Events;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using LakskongenSim.Tests.Integration;
using StateHashProbe;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Core;

/// <summary>
/// Roadmap F2-05 (docs/FARM_TYPES_SPEC.md §7.1, §1 pillar 5, §3.1 decision 7,
/// §3.4): the Driftsplan. A standing order issues exactly the commands a
/// scripted player would, at the start of the day, and the two games stay the
/// same game every day (acceptance 1); planning never moves a stream position
/// (2); a game with a plan saves and loads bit-identically (5). The F2 event
/// catalogue (3) and the notification meter (4) have their own files
/// (<c>EventBus/FarmTypeEventCatalogTests</c>, <c>EventBus/NotificationMeterTests</c>);
/// the shipped goldens (6) are <c>GoldenTraceSeed*Tests</c>.
/// </summary>
public class DriftsplanTests
{
    private readonly ITestOutputHelper _output;
    public DriftsplanTests(ITestOutputHelper output) => _output = output;

    private const string Nord = "po3_nord";
    private const string Sor = "po3_sor";

    /// <summary>po3_red_sone as the golden traces start it (seeded, events on): two stocked fjord sites in a red zone.</summary>
    private static SimulationEngine Game(int seed, bool events = true)
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: events, seed: seed);
        engine.State.RandomSeed = seed;
        Assert.True(engine.StartScenario(PlayerScriptProbe.CareerScenarioId).Ok);
        return engine;
    }

    /// <summary>
    /// A wave of lice on both sites every <see cref="LiceWaveEveryDays"/> days,
    /// the same in every game it is called on (before the day's commands):
    /// po3_red_sone's own counts stay near 0.01 adult females in a year, so a
    /// standing order on the count would never act.
    /// </summary>
    private static void LiceWave(SimulationEngine engine, int day)
    {
        if (day % LiceWaveEveryDays != 0) return;
        foreach (var site in engine.State.SeaSites)
        {
            site.Lice.AdultFemalePerFish += 0.5;
            site.Lice.SeedExternalInfection(1.0);
        }
    }

    private const int LiceWaveEveryDays = 60;

    private static Dictionary<string, double> Numbers(params (string Key, double Value)[] values)
        => values.ToDictionary(v => v.Key, v => v.Value);

    private static string DayHash(SimulationEngine engine) => GoldenTraceState.Capture(engine).DayHash();

    private static string Line(int day, string command, CommandResult result)
        => $"{day} {command}: {(result.Ok ? "ok" : result.ErrorKey)}";

    private static List<string> PlanLines(SimulationEngine engine)
        => engine.DriftsplanLog.Select(i => $"{i.Day} {i.Command}: {(i.Ok ? "ok" : i.ErrorKey)}").ToList();

    /// <summary>The save without its Driftsplan (the plan itself is the one thing a scripted player's game does not hold).</summary>
    private static string SaveWithoutThePlan(SimulationEngine engine)
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(SaveStateComparer.Normalize(SaveStateComparer.Serialize(engine)))!.AsObject();
        Assert.True(json.Remove("Driftsplan"), "the save has no Driftsplan");
        return json.ToJsonString();
    }

    // ── Acceptance 1: a standing order is a scripted player ───────────────

    /// <summary>
    /// A weekly job (net care every 7 days, the ROV wellboat) issued by the
    /// Driftsplan and the same job issued by a scripted player at the start of
    /// the day, in a full year of po3_red_sone with events on and the golden
    /// player farming both games (harvests, restocking, delousing, research):
    /// the commands are the same, day for day and outcome for outcome, the
    /// traced state (every group and every stream) is the same every day, and
    /// after 365 days the whole save is the same but for the plan itself.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(13)]
    public void AWeeklyJob_IssuesExactlyTheScriptedPlayersCommands_AndTheGameIsTheSameEveryDayFor365Days(int seed)
    {
        var planned = Game(seed);
        var scripted = Game(seed);
        var plannedFarmer = new GoldenPlayer();
        var scriptedFarmer = new GoldenPlayer();
        Assert.True(planned.SetDriftsplan(Nord, FjordPolicies.NetCleaning, Numbers(("interval_days", 7))).Ok);

        var playerLines = new List<string>();
        int lastBooked = -1;
        for (int day = 0; day < 365; day++)
        {
            plannedFarmer.Act(planned, day);
            scriptedFarmer.Act(scripted, day);

            // The scripted player: every 7 days while the site holds fish and no ROV is booked.
            var site = scripted.State.SeaSites.Single(s => s.Id == Nord);
            bool fish = site.Pens.Sum(p => p.TotalCount) > 0;
            bool booked = scripted.State.WellboatJobs.Any(j => j.SiteId == Nord && j.Kind == "rov");
            if (fish && !booked && (lastBooked < 0 || day - lastBooked >= 7))
            {
                var result = scripted.RequestWellboat(Nord, "rov");
                playerLines.Add(Line(day, $"request_wellboat {Nord} rov", result));
                if (result.Ok) lastBooked = day;
            }

            planned.Advance(1);
            scripted.Advance(1);
            Assert.True(DayHash(scripted) == DayHash(planned), $"seed {seed}: the games part on day {day + 1}");
        }

        var planLines = PlanLines(planned);
        _output.WriteLine($"seed {seed}: {planLines.Count} jobs, first {planLines.FirstOrDefault()}, last {planLines.LastOrDefault()}");
        Assert.Equal(playerLines, planLines);
        // The golden player spends down to its reserve, so some jobs wait for money (the order retries daily, as the player does).
        Assert.True(planLines.Count(l => l.EndsWith(": ok", StringComparison.Ordinal)) >= 20, $"only {planLines.Count} jobs");
        Assert.Equal(SaveWithoutThePlan(scripted), SaveWithoutThePlan(planned));
        var entry = Assert.Single(planned.State.Driftsplan);
        Assert.Equal(planLines.Count(l => l.EndsWith(": ok", StringComparison.Ordinal)), entry.IssuedCount);
    }

    /// <summary>
    /// A standing order on a condition (delouse on the weekly count over 0.8
    /// of the legal limit, at most every 14 days) against a scripted player who
    /// reads the same count: the same treatments on the same days, the same
    /// game every day for a year.
    /// </summary>
    [Theory]
    [InlineData(7)]
    [InlineData(13)]
    public void AStandingOrderOnTheCount_IssuesExactlyTheScriptedPlayersCommands_AndTheGameIsTheSameEveryDay(int seed)
    {
        var planned = Game(seed);
        var scripted = Game(seed);
        foreach (var site in new[] { Nord, Sor })
            Assert.True(planned.SetDriftsplan(site, FjordPolicies.DelouseOnCount).Ok);

        var playerLines = new List<string>();
        var lastTreated = new Dictionary<string, int>();
        for (int day = 0; day < 365; day++)
        {
            LiceWave(planned, day);
            LiceWave(scripted, day);
            foreach (var id in new[] { Nord, Sor })
            {
                var site = scripted.State.SeaSites.Single(s => s.Id == id);
                if (site.Pens.Sum(p => p.TotalCount) <= 0) continue;
                int last = lastTreated.TryGetValue(id, out int d) ? d : -1;
                bool newCountAbove = site.LastLiceCountDay > last
                    && site.LastLiceCount > 0.8 * scripted.LiceLimitAtSite(site);
                if (!newCountAbove || (last >= 0 && day - last < 14)) continue;
                var result = scripted.DelouseSite(id, "hydrolicer");
                playerLines.Add(Line(day, $"delouse_site {id} hydrolicer", result));
                if (result.Ok) lastTreated[id] = day;
            }
            planned.Advance(1);
            scripted.Advance(1);
            Assert.True(DayHash(scripted) == DayHash(planned), $"seed {seed}: the games part on day {day + 1}");
        }

        var planLines = PlanLines(planned);
        _output.WriteLine($"seed {seed}: {string.Join(" | ", planLines)}");
        Assert.Equal(playerLines, planLines);
        Assert.True(planLines.Count(l => l.EndsWith(": ok", StringComparison.Ordinal)) >= 8, $"only {planLines.Count} treatments: the order hardly acted");
        Assert.Equal(SaveWithoutThePlan(scripted), SaveWithoutThePlan(planned));
    }

    // ── Acceptance 2: planning never moves a stream ────────────────────────

    /// <summary>
    /// Both fjord policies on both sites for 200 days: before every day the
    /// plan is read every way the UI and the engine read it (every unit's
    /// orders, jobs, next job, kind and label, three times over); no stream
    /// moves, nothing in the save changes, and the game ends where an
    /// identical game that never read its plan ends.
    /// </summary>
    [Fact]
    public void PlanningNeverMovesAStreamPosition_AndNeverChangesTheGame()
    {
        var read = Game(7);
        var untouched = Game(7);
        foreach (var engine in new[] { read, untouched })
        {
            Assert.True(engine.SetDriftsplan(Nord, FjordPolicies.NetCleaning, Numbers(("interval_days", 10))).Ok);
            Assert.True(engine.SetDriftsplan(Nord, FjordPolicies.DelouseOnCount).Ok);
            Assert.True(engine.SetDriftsplan(Sor, FjordPolicies.DelouseOnCount, Numbers(("share_of_limit", 0.5))).Ok);
        }
        int jobsSeen = 0;
        for (int day = 0; day < 200; day++)
        {
            // Both games are saved every day: a save refreshes the state's mirrors
            // (the pen-price council's last value), so only the reading differs.
            SaveStateComparer.Serialize(untouched);
            var streams = read.Rng.CaptureStates();
            string save = SaveStateComparer.Serialize(read);
            for (int pass = 0; pass < 3; pass++)
            {
                foreach (var site in read.State.SeaSites)
                {
                    jobsSeen += read.GetPlannedJobs(site.Id).Count;
                    read.GetNextPlanned(site.Id);
                    read.GetDriftsplan(site.Id);
                    read.DriftsplanUnitKindOf(site.Id);
                    read.UnitLabel(site.Id);
                    foreach (var entry in read.State.Driftsplan.Where(e => e.UnitId == site.Id))
                        read.EvaluateDriftsplanEntry(DriftsplanPolicies.Find(entry.Policy)!, entry);
                }
            }
            Assert.Equal(streams, read.Rng.CaptureStates());
            string after = SaveStateComparer.Serialize(read);
            Assert.True(save == after, $"day {day}: reading the plan changed {SaveStateComparer.FirstDifference(save, after)}");
            read.Advance(1);
            untouched.Advance(1);
        }
        Assert.True(jobsSeen > 600, $"only {jobsSeen} jobs planned");
        Assert.Equal(DayHash(untouched), DayHash(read));
        Assert.Equal(SaveStateComparer.Normalize(SaveStateComparer.Serialize(untouched)),
            SaveStateComparer.Normalize(SaveStateComparer.Serialize(read)));
    }

    /// <summary>A policy that draws.</summary>
    private sealed class DrawingPolicy : IDriftsplanPolicy
    {
        public string Kind => "test_drawing";
        public string Title => "test";
        public IReadOnlyList<DriftsplanUnitKind> AppliesTo => new[] { DriftsplanUnitKind.FjordSite };
        public IReadOnlyList<DriftsplanSetting> Settings => Array.Empty<DriftsplanSetting>();
        public bool IsDefaultFor(DriftsplanUnitKind kind) => false;
        public DriftsplanJob? Plan(DriftsplanContext context)
        {
            context.Engine.Rng.Stream(SimRngStreams.Weather).NextDouble();
            return null;
        }
    }

    /// <summary>A policy that opens a stream it does not draw from.</summary>
    private sealed class OpeningPolicy : IDriftsplanPolicy
    {
        public string Kind => "test_opening";
        public string Title => "test";
        public IReadOnlyList<DriftsplanUnitKind> AppliesTo => new[] { DriftsplanUnitKind.FjordSite };
        public IReadOnlyList<DriftsplanSetting> Settings => Array.Empty<DriftsplanSetting>();
        public bool IsDefaultFor(DriftsplanUnitKind kind) => false;
        public DriftsplanJob? Plan(DriftsplanContext context)
        {
            context.Engine.Rng.Stream("test_opened", context.UnitId);
            return null;
        }
    }

    /// <summary>The guard catches a policy that draws or opens a stream, and names the policy and the stream.</summary>
    [Fact]
    public void APolicyThatDraws_OrOpensAStream_IsCaught_WithItsNameAndTheStream()
    {
        var engine = Game(3, events: false);
        var entry = new DriftsplanEntry { UnitId = Nord, Policy = "test_drawing" };
        var drew = Assert.Throws<DriftsplanPurityException>(() => engine.EvaluateDriftsplanEntry(new DrawingPolicy(), entry));
        Assert.Equal(("test_drawing", "weather"), (drew.Policy, drew.Stream));

        var opened = Assert.Throws<DriftsplanPurityException>(() => engine.EvaluateDriftsplanEntry(new OpeningPolicy(), entry));
        Assert.Equal(("test_opening", $"test_opened/{Nord}"), (opened.Policy, opened.Stream));

        // Every shipped policy, on every unit it applies to, plans clean.
        foreach (var policy in DriftsplanPolicies.All)
            foreach (var site in engine.State.SeaSites)
                engine.EvaluateDriftsplanEntry(policy, new DriftsplanEntry { UnitId = site.Id, Policy = policy.Kind });
    }

    // ── The verbs ──────────────────────────────────────────────────────────

    [Fact]
    public void SetDriftsplan_RefusesWhatItCannotDo_BeforeAnyChange()
    {
        var engine = Game(5, events: false);
        engine.ReceiveLoan(2_000_000_000m);
        Assert.True(engine.BuildRASFacility("Land", RASScale.Small).Ok);
        string land = engine.State.RASFacilities.Single().Id;

        Assert.Equal("unknown_policy", engine.SetDriftsplan(Nord, "feed_everything").ErrorKey);
        Assert.Equal("not_found", engine.SetDriftsplan("po9_nowhere", FjordPolicies.NetCleaning).ErrorKey);
        Assert.Equal("not_applicable", engine.SetDriftsplan(land, FjordPolicies.NetCleaning).ErrorKey);
        foreach (var bad in new[] { 6.0, 61.0, 7.5, double.NaN, double.PositiveInfinity })
            Assert.Equal("out_of_range", engine.SetDriftsplan(Nord, FjordPolicies.NetCleaning, Numbers(("interval_days", bad))).ErrorKey);
        Assert.Equal("out_of_range", engine.SetDriftsplan(Nord, FjordPolicies.NetCleaning, Numbers(("days", 14))).ErrorKey);
        Assert.Equal("out_of_range", engine.SetDriftsplan(Nord, FjordPolicies.DelouseOnCount, Numbers(("share_of_limit", 0.49))).ErrorKey);
        Assert.Equal("out_of_range", engine.SetDriftsplan(Nord, FjordPolicies.DelouseOnCount, Numbers(("method", 1))).ErrorKey);
        Assert.Equal("out_of_range", engine.SetDriftsplan(Nord, FjordPolicies.DelouseOnCount, null,
            new Dictionary<string, string> { ["method"] = "laser" }).ErrorKey);
        Assert.Equal("out_of_range", engine.SetDriftsplan(Nord, FjordPolicies.DelouseOnCount, null,
            new Dictionary<string, string> { ["share_of_limit"] = "0.8" }).ErrorKey);
        Assert.Equal("not_found", engine.ClearDriftsplan(Nord, FjordPolicies.NetCleaning).ErrorKey);
        Assert.Empty(engine.State.Driftsplan);
    }

    /// <summary>
    /// A new order takes the defaults; a change keeps the settings not given
    /// and the history (the cadence runs on); the list is sorted by unit and
    /// policy whatever order the orders were set in; ending one removes it.
    /// </summary>
    [Fact]
    public void Orders_TakeDefaults_KeepTheirHistoryWhenChanged_AndActInUnitAndPolicyOrder()
    {
        var engine = Game(5, events: false);
        Assert.True(engine.SetDriftsplan(Sor, FjordPolicies.NetCleaning).Ok);
        Assert.True(engine.SetDriftsplan(Nord, FjordPolicies.NetCleaning).Ok);
        Assert.True(engine.SetDriftsplan(Nord, FjordPolicies.DelouseOnCount, null,
            new Dictionary<string, string> { ["method"] = "thermolicer" }).Ok);
        Assert.Equal(new[] { $"{Nord}/{FjordPolicies.DelouseOnCount}", $"{Nord}/{FjordPolicies.NetCleaning}", $"{Sor}/{FjordPolicies.NetCleaning}" },
            engine.State.Driftsplan.Select(e => $"{e.UnitId}/{e.Policy}"));

        var delouse = engine.GetDriftsplan(Nord).First();
        Assert.Equal((0.8, 14.0, "thermolicer"), (delouse.Number("share_of_limit"), delouse.Number("min_interval_days"), delouse.Choice("method")));
        Assert.Equal(new[] { "share_of_limit", "min_interval_days" }, delouse.Numbers.Keys);

        engine.Advance(1); // net care's first job: the day it was set
        var net = engine.GetDriftsplan(Nord).Single(e => e.Policy == FjordPolicies.NetCleaning);
        Assert.Equal((0, 1, "ok"), (net.LastIssuedDay, net.IssuedCount, net.LastOutcome));
        Assert.True(engine.SetDriftsplan(Nord, FjordPolicies.NetCleaning, Numbers(("interval_days", 21))).Ok);
        Assert.Same(net, engine.GetDriftsplan(Nord).Single(e => e.Policy == FjordPolicies.NetCleaning));
        Assert.Equal((0, 1, 21.0), (net.LastIssuedDay, net.IssuedCount, net.Number("interval_days")));
        Assert.Equal(21, engine.GetPlannedJobs(Nord).Single(j => j.Policy == FjordPolicies.NetCleaning).Day);
        Assert.True(engine.SetDriftsplan(Nord, FjordPolicies.DelouseOnCount, Numbers(("share_of_limit", 0.6))).Ok);
        Assert.Equal("thermolicer", engine.GetDriftsplan(Nord).First().Choice("method"));

        Assert.True(engine.ClearDriftsplan(Nord, FjordPolicies.NetCleaning).Ok);
        Assert.Equal(2, engine.State.Driftsplan.Count);
    }

    /// <summary>
    /// "Neste planlagt": the cadence's day, the order that waits for its
    /// trigger (a count over 0.8 of today's limit: 0.4, and 0.16 in the
    /// area's spring weeks), the order on an empty site that waits for fish,
    /// the soonest first.
    /// </summary>
    [Fact]
    public void NextPlanned_ShowsTheCadence_TheTrigger_AndTheWaitForFish()
    {
        var engine = Game(5, events: false);
        Assert.Null(engine.GetNextPlanned(Nord));
        Assert.True(engine.SetDriftsplan(Nord, FjordPolicies.DelouseOnCount).Ok);
        var waiting = engine.GetNextPlanned(Nord)!;
        Assert.Equal((-1, FjordPolicies.TriggerLiceCountAbove, "delouse_site po3_nord hydrolicer"), (waiting.Day, waiting.Trigger, waiting.Command));
        var nord = engine.State.SeaSites.Single(s => s.Id == Nord);
        Assert.Equal(0.8 * engine.LiceLimitAtSite(nord), waiting.TriggerValue, 12);
        Assert.Null(waiting.Issue);

        Assert.True(engine.SetDriftsplan(Nord, FjordPolicies.NetCleaning, Numbers(("interval_days", 30))).Ok);
        var next = engine.GetNextPlanned(Nord)!;
        Assert.Equal((FjordPolicies.NetCleaning, engine.State.Day, "request_wellboat po3_nord rov"), (next.Policy, next.Day, next.Command));
        engine.Advance(1);
        Assert.Equal(30, engine.GetNextPlanned(Nord)!.Day);
        Assert.Equal(new[] { FjordPolicies.NetCleaning, FjordPolicies.DelouseOnCount }, engine.GetPlannedJobs(Nord).Select(j => j.Policy));

        // The spring weeks lower the limit, and the order's level with it.
        var levels = new HashSet<double>();
        for (int day = 1; day < 160; day++)
        {
            engine.Advance(1);
            levels.Add(Math.Round(engine.GetPlannedJobs(Nord).Single(j => j.Policy == FjordPolicies.DelouseOnCount).TriggerValue, 9));
        }
        Assert.Equal(new[] { 0.16, 0.4 }, levels.OrderBy(v => v));

        // A site without fish: both orders wait for a stocking.
        Assert.True(engine.HarvestSite(Nord).Ok);
        Assert.All(engine.GetPlannedJobs(Nord), j => Assert.Equal((-1, FjordPolicies.TriggerSiteStocked), (j.Day, j.Trigger)));
    }

    // ── A job that cannot be done ──────────────────────────────────────────

    /// <summary>
    /// With no money the ROV cannot be hired: the job slips day by day (the
    /// order tries every day and keeps its last good day), the player is told
    /// once with the unit, the site and the verb that opens the plan, the meter
    /// counts it under the site, and the job goes through once there is money.
    /// </summary>
    [Fact]
    public void AJobThatCannotBeDone_SlipsDayByDay_IsReportedOnce_AndGoesThroughWhenItCan()
    {
        var engine = Game(9, events: false);
        engine.FinanceManager.ForceSpend(engine.FinanceManager.Balance);
        Assert.True(engine.SetDriftsplan(Nord, FjordPolicies.NetCleaning).Ok);
        engine.GameEvents.DrainDelivered();
        for (int day = 0; day < 5; day++)
            engine.Advance(1);
        var entry = engine.State.Driftsplan.Single();
        Assert.Equal((-1, 0, 4, "insufficient_funds"), (entry.LastIssuedDay, entry.IssuedCount, entry.LastAttemptDay, entry.LastOutcome));
        Assert.Equal(5, engine.DriftsplanLog.Count(i => !i.Ok));
        var told = Assert.Single(engine.GameEvents.DrainDelivered(), e => e.Type == GameEventType.DriftsplanCommandFailed);
        Assert.Equal((Nord, Nord, FjordPolicies.NetCleaning, GameEventSeverity.Warning, GameEventCatalog.VerbOpenDriftsplan),
            (told.UnitId, told.SiteId, told.Variant, told.Severity, told.CounterVerb));
        Assert.Contains("for lite penger", told.Message);
        Assert.Equal(1, engine.Notifications.Warnings(Nord, NotificationMeter.MonthOf(told.Day)));
        Assert.Equal(0, engine.GetNextPlanned(Nord)!.Day); // still due from the day it was set: the job slipped

        engine.FinanceManager.AddRevenue(5_000_000m);
        engine.Advance(1);
        Assert.Equal((5, 1, "ok"), (entry.LastIssuedDay, entry.IssuedCount, entry.LastOutcome));
        Assert.Contains(engine.State.WellboatJobs, j => j.SiteId == Nord && j.Kind == "rov");
    }

    // ── Acceptance 5: a game with a plan saves and loads bit-identically ──

    /// <summary>
    /// Both fjord policies on both sites, events on: saved mid-cadence on day
    /// 100 and resumed on a fresh engine, the loaded game and the one that
    /// never stopped are the same every day for 120 more days and serialize
    /// to the same save, the plan's own state included.
    /// </summary>
    [Theory]
    [InlineData(7)]
    [InlineData(13)]
    public void AGameWithAPlan_SavesAndLoads_BitIdentically(int seed)
    {
        var uninterrupted = Game(seed);
        uninterrupted.ReceiveLoan(40_000_000m);
        LiceWave(uninterrupted, 0);
        Assert.True(uninterrupted.SetDriftsplan(Nord, FjordPolicies.NetCleaning, Numbers(("interval_days", 9))).Ok);
        Assert.True(uninterrupted.SetDriftsplan(Nord, FjordPolicies.DelouseOnCount).Ok);
        Assert.True(uninterrupted.SetDriftsplan(Sor, FjordPolicies.DelouseOnCount, Numbers(("share_of_limit", 0.6), ("min_interval_days", 21))).Ok);
        uninterrupted.Advance(100);
        Assert.Contains(uninterrupted.State.Driftsplan, e => e.IssuedCount > 0 && e.LastIssuedDay > 90 - 9);

        var resumed = SaveStateComparer.Resume(SaveStateComparer.Serialize(uninterrupted), events: true);
        Assert.Equal(SaveStateComparer.Serialize(uninterrupted), SaveStateComparer.Serialize(resumed));
        for (int day = 0; day < 120; day++)
        {
            uninterrupted.Advance(1);
            resumed.Advance(1);
            Assert.True(DayHash(uninterrupted) == DayHash(resumed), $"seed {seed}: the loaded game parts on day {uninterrupted.State.Day}");
        }
        // A bloom that starts after the load gets a process-random id (an allowlisted Guid): normalized.
        string a = SaveStateComparer.Normalize(SaveStateComparer.Serialize(uninterrupted));
        string b = SaveStateComparer.Normalize(SaveStateComparer.Serialize(resumed));
        Assert.True(a == b, SaveStateComparer.FirstDifference(a, b));
        Assert.True(uninterrupted.State.Driftsplan.Sum(e => e.IssuedCount) >= 15);
    }

    /// <summary>Two engines on one seed with the same plan stay the same game (the twin gate with a plan).</summary>
    [Fact]
    public void TwinEnginesWithThePlan_StayIdentical()
    {
        var a = Game(21);
        var b = Game(21);
        foreach (var engine in new[] { a, b })
        {
            Assert.True(engine.SetDriftsplan(Sor, FjordPolicies.NetCleaning, Numbers(("interval_days", 7))).Ok);
            Assert.True(engine.SetDriftsplan(Sor, FjordPolicies.DelouseOnCount).Ok);
        }
        for (int day = 0; day < 150; day++)
        {
            a.Advance(1);
            b.Advance(1);
            Assert.Equal(DayHash(a), DayHash(b));
        }
        Assert.Equal(PlanLines(a), PlanLines(b));
        Assert.Equal(SaveStateComparer.Normalize(SaveStateComparer.Serialize(a)), SaveStateComparer.Normalize(SaveStateComparer.Serialize(b)));
    }

    // ── The player path: a plan changes the outcome ───────────────────────

    /// <summary>
    /// The player path (AGENTS.md: a feature is done when a player's game
    /// changes): over a year of po3_red_sone under lice pressure, with money
    /// for the jobs, net care every 14 days keeps the nets of po3_nord in
    /// better repair than leaving them (mean and worst integrity) and pays the
    /// wellboat for it; delousing on the count keeps the sites' lice lower
    /// than doing nothing and brings fewer Mattilsynet treatment orders.
    /// </summary>
    [Theory]
    [InlineData(3)]
    [InlineData(11)]
    public void APlan_ChangesThePlayersGame(int seed)
    {
        var none = Game(seed);
        var nets = Game(seed);
        var lice = Game(seed);
        foreach (var engine in new[] { none, nets, lice })
            engine.ReceiveLoan(40_000_000m);
        Assert.True(nets.SetDriftsplan(Nord, FjordPolicies.NetCleaning).Ok);
        foreach (var site in new[] { Nord, Sor })
            Assert.True(lice.SetDriftsplan(site, FjordPolicies.DelouseOnCount).Ok);

        double netNone = 0, netPlan = 0, liceNone = 0, licePlan = 0;
        double worstNone = 100, worstPlan = 100;
        int counts = 0, ordersNone = 0, ordersPlan = 0;
        decimal hireNone = 0m, hirePlan = 0m;
        for (int day = 0; day < 365; day++)
        {
            LiceWave(none, day);
            LiceWave(lice, day);
            none.Advance(1);
            nets.Advance(1);
            lice.Advance(1);
            double a0 = none.State.SeaSites.Single(s => s.Id == Nord).NetIntegrityPct;
            double b0 = nets.State.SeaSites.Single(s => s.Id == Nord).NetIntegrityPct;
            netNone += a0;
            netPlan += b0;
            worstNone = Math.Min(worstNone, a0);
            worstPlan = Math.Min(worstPlan, b0);
            ordersNone += none.State.SeaSites.Count(s => s.TreatmentOrderDueDay == none.State.Day + 13);
            ordersPlan += lice.State.SeaSites.Count(s => s.TreatmentOrderDueDay == lice.State.Day + 13);
            if (day == 299) // the cost lines before the year-end close empties them
            {
                hireNone = none.FinancialStatements.ExportCostBreakdown().GetValueOrDefault("wellboat");
                hirePlan = nets.FinancialStatements.ExportCostBreakdown().GetValueOrDefault("wellboat");
            }
            foreach (var id in new[] { Nord, Sor })
            {
                var a = none.State.SeaSites.Single(s => s.Id == id);
                var b = lice.State.SeaSites.Single(s => s.Id == id);
                if (a.Pens.Sum(p => p.TotalCount) <= 0 || b.Pens.Sum(p => p.TotalCount) <= 0) continue;
                liceNone += a.Lice.AdultFemalePerFish;
                licePlan += b.Lice.AdultFemalePerFish;
                counts++;
            }
        }
        _output.WriteLine($"seed {seed}: net integrity mean {netNone / 365:F2} % / worst {worstNone:F1} % without, " +
            $"{netPlan / 365:F2} % / {worstPlan:F1} % with net care ({nets.State.Driftsplan.Single().IssuedCount} jobs, " +
            $"wellboat {hireNone:N0} -> {hirePlan:N0} NOK); mean adult females {liceNone / Math.Max(1, counts):F3} without, " +
            $"{licePlan / Math.Max(1, counts):F3} with the standing order ({lice.State.Driftsplan.Sum(e => e.IssuedCount)} treatments); " +
            $"treatment orders {ordersNone} without, {ordersPlan} with");
        Assert.True(netPlan > netNone, "net care must keep the nets in better repair");
        Assert.True(worstPlan >= worstNone);
        Assert.True(nets.State.Driftsplan.Single().IssuedCount >= 25);
        Assert.True(hirePlan >= hireNone + 20 * 75_000m * 0.75m, "the jobs are paid (20 by day 300, at least the skipper's hire)");
        Assert.Equal(nets.State.Driftsplan.Single().IssuedCount, nets.State.WellboatJobsBooked - none.State.WellboatJobsBooked);
        Assert.True(lice.State.Driftsplan.Sum(e => e.IssuedCount) >= 8);
        Assert.True(licePlan < 0.8 * liceNone, "the standing order must keep the lice lower");
        Assert.True(ordersPlan < ordersNone, "and bring fewer treatment orders");
    }

    // ── The registry ───────────────────────────────────────────────────────

    /// <summary>
    /// Every policy has a unique kind (the list sorted by it), a Norwegian
    /// title, at least one unit kind, and settings whose defaults lie in their
    /// range (or list) with the default's source.
    /// </summary>
    [Fact]
    public void EveryPolicy_IsUnique_AndEverySettingHasAValidDefaultAndASource()
    {
        var kinds = DriftsplanPolicies.All.Select(p => p.Kind).ToList();
        Assert.Equal(kinds.OrderBy(k => k, StringComparer.Ordinal), kinds);
        Assert.Equal(kinds.Count, kinds.Distinct().Count());
        Assert.Contains(FjordPolicies.NetCleaning, kinds);
        Assert.Contains(FjordPolicies.DelouseOnCount, kinds);
        foreach (var policy in DriftsplanPolicies.All)
        {
            Assert.Matches("^[a-z][a-z0-9_]*$", policy.Kind);
            Assert.False(string.IsNullOrWhiteSpace(policy.Title));
            Assert.NotEmpty(policy.AppliesTo);
            Assert.Same(policy, DriftsplanPolicies.Find(policy.Kind));
            Assert.Equal(policy.Settings.Count, policy.Settings.Select(s => s.Key).Distinct().Count());
            foreach (var setting in policy.Settings)
            {
                Assert.Matches("^[a-z][a-z0-9_]*$", setting.Key);
                Assert.False(string.IsNullOrWhiteSpace(setting.Source), $"{policy.Kind}.{setting.Key} has no source");
                if (setting.Kind == DriftsplanSettingKind.Number)
                {
                    Assert.True(setting.Min < setting.Max, $"{policy.Kind}.{setting.Key}");
                    Assert.Null(setting.NumberProblem(setting.Default));
                    Assert.NotNull(setting.NumberProblem(setting.Min - 1));
                    Assert.NotNull(setting.NumberProblem(setting.Max + 1));
                }
                else
                {
                    Assert.NotEmpty(setting.Choices);
                    Assert.Null(setting.ChoiceProblem(setting.DefaultChoice));
                }
            }
        }
        Assert.Null(DriftsplanPolicies.Find("nothing"));
    }

    /// <summary>The fjord policies are never on by default: a fjord game plays as before until the player sets one.</summary>
    [Fact]
    public void NoFjordPolicyIsADefault_AndApplyingDefaultsToAFjordSiteSetsNothing()
    {
        Assert.All(DriftsplanPolicies.All.Where(p => p.AppliesTo.Contains(DriftsplanUnitKind.FjordSite)),
            p => Assert.False(p.IsDefaultFor(DriftsplanUnitKind.FjordSite)));
        var engine = Game(2, events: false);
        Assert.Equal(0, engine.ApplyDriftsplanDefaults(Nord));
        Assert.Equal(0, engine.ApplyDriftsplanDefaults("nowhere"));
        Assert.Empty(engine.State.Driftsplan);
    }

    /// <summary>The log of issued commands is presentation state: a new game empties it, and it is not saved.</summary>
    [Fact]
    public void TheLog_IsEmptiedByANewGame_AndIsNotSaved()
    {
        var engine = Game(4, events: false);
        Assert.True(engine.SetDriftsplan(Nord, FjordPolicies.NetCleaning).Ok);
        engine.Advance(1);
        Assert.Single(engine.DriftsplanLog);
        Assert.DoesNotContain(typeof(GameState).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            p => p.PropertyType == typeof(List<DriftsplanIssue>) || p.PropertyType == typeof(NotificationMeter));
        engine.Initialize(enableEvents: false, seed: 4);
        Assert.Empty(engine.DriftsplanLog);
        Assert.Empty(engine.State.Driftsplan);
    }

    /// <summary>A unit's kind and label by id: a fjord site, an offshore unit, a grow-out and a post-smolt land site.</summary>
    [Fact]
    public void UnitKindsAndLabels_ResolveEveryKindById()
    {
        var engine = Game(6, events: false);
        engine.ReceiveLoan(3_000_000_000m);
        Assert.True(engine.BuildRASFacility("Landanlegg Vest", RASScale.Small).Ok);
        Assert.True(engine.BuildPostSmoltFacility("Elv").Ok);
        Assert.True(engine.BuildOffshoreFarm("Hav", OffshoreType.SemiSubmersible).Ok);
        var land = engine.LandSites.Single(f => f.Kind == LandSiteKind.GrowOut);
        var post = engine.PostSmoltSites.Single();
        var unit = engine.OffshoreUnits.Single();
        Assert.Equal(DriftsplanUnitKind.FjordSite, engine.DriftsplanUnitKindOf(Nord));
        Assert.Equal(DriftsplanUnitKind.OffshoreUnit, engine.DriftsplanUnitKindOf(unit.Id));
        Assert.Equal(DriftsplanUnitKind.LandSite, engine.DriftsplanUnitKindOf(land.Id));
        Assert.Equal(DriftsplanUnitKind.PostSmoltSite, engine.DriftsplanUnitKindOf(post.Id));
        Assert.Null(engine.DriftsplanUnitKindOf(""));
        Assert.Equal("PO3 Nord", engine.UnitLabel(Nord));
        Assert.Equal("Landanlegg Vest", engine.UnitLabel(land.Id));
        Assert.Equal("", engine.UnitLabel(""));
        Assert.Equal("not_applicable", engine.SetDriftsplan(unit.Id, FjordPolicies.NetCleaning).ErrorKey);
    }

    // ── The farm-type events sim·core publishes ────────────────────────────

    /// <summary>
    /// A managed build's phases end with a typed event each (information) and a
    /// warning for the overrun owed, and a permit case's decision is reported:
    /// each names its unit, the meter counts the warnings under the unit, and
    /// information is not counted.
    /// </summary>
    [Fact]
    public void ConstructionAndPermits_ReportTypedEvents_WithTheUnit()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 8);
        engine.ReceiveLoan(2_000_000_000m);
        var phases = new List<ConstructionPhase>
        {
            new() { Key = "site", PlannedDays = 10, CapexShare = 0.4 },
            new() { Key = "tanks", PlannedDays = 10, CapexShare = 0.6 },
        };
        Assert.True(engine.StartConstructionProject("ras_9", FacilityAssetKind.LandSite, 500_000_000m, phases, ContractForm.Managed).Ok);
        Assert.True(engine.SubmitPermitCase(PermitKind.Discharge, "pad_a", "ras_9", new PermitTerms(3, 3, 1.0)).Ok);
        var events = new List<GameEvent>();
        for (int day = 0; day < 60; day++)
        {
            engine.Advance(1);
            events.AddRange(engine.GameEvents.DrainDelivered());
        }
        var phaseEvents = events.Where(e => e.Type == GameEventType.ConstructionPhaseComplete).ToList();
        Assert.Equal(2, phaseEvents.Count);
        Assert.All(phaseEvents, e => Assert.Equal(("ras_9", GameEventSeverity.Info), (e.UnitId, e.Severity)));
        Assert.Contains("er ferdig bygget", phaseEvents[1].Message);
        var overruns = events.Where(e => e.Type == GameEventType.CapexOverrun).ToList();
        decimal paidOverrun = engine.ConstructionProjects.Single().PaidNOK - 500_000_000m;
        Assert.True(paidOverrun > 0m, "a managed build's overrun");
        Assert.Equal((double)paidOverrun, overruns.Sum(e => e.Value), 3);
        Assert.All(overruns, e => Assert.Equal(("ras_9", GameEventSeverity.Warning), (e.UnitId, e.Severity)));
        var decision = Assert.Single(events, e => e.Type == GameEventType.PermitDecision);
        Assert.Equal(("ras_9", 0.0), (decision.UnitId, decision.Value));
        Assert.Contains("avslått", decision.Message);
        Assert.Equal(overruns.Count, engine.Notifications.Months.Where(m => m.UnitId == "ras_9").Sum(m => m.Warnings));
        Assert.Equal(0, engine.Notifications.Months.Sum(m => m.Criticals));
    }
}
