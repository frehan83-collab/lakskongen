using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Calibration;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Events;
using LakskongenSim.Farm;
using LakskongenSim.Farm.Land;
using LakskongenSim.Harvest;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// F2-15, grow-out biology, competence and harvest planning on land
/// (docs/FARM_TYPES_SPEC.md §7.1 F2-15, §2A-2C, §3.3 "Growth and competence",
/// §3.4, §6.5): competence 0.4 gives the achieved result and 1.0 the plan; the
/// FCR bands of HFS and RAS; the growth shortfall naming its driver and the
/// feed-plan penalty; the utilisation cap and its levers with their costs;
/// planned harvests in their week at the seasonal price with the S2-04 size
/// discount; the steady-state farming cost per kg HOG ex depreciation with
/// its energy share. Numbers: docs/BALANCE.md §1y.
/// </summary>
public class LandCycleTests
{
    private readonly ITestOutputHelper _output;
    public LandCycleTests(ITestOutputHelper output) => _output = output;

    /// <summary>A month (days): a twelfth of the year.</summary>
    private const double Month = SimCalendar.DaysPerYear / 12.0;

    /// <summary>Salmon Evolution's production area (Hustadvika: PO5).</summary>
    private const int Po5 = 5;

    private static SimulationEngine NewEngine(int seed, decimal cash = 5_000_000_000m, bool events = false)
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: events, seed: seed);
        engine.ReceiveLoan(cash);
        engine.State.HomeProductionArea = Po5;
        return engine;
    }

    private static RASFacility Commission(SimulationEngine engine, LandTemplate template = LandTemplate.HfsLot, string plot = "pad_a")
    {
        var placed = engine.CommissionLandSite(plot, template, 1, "Storholmen");
        Assert.True(placed.Ok, placed.Message);
        var site = engine.FindLandSite(placed.Id)!;
        int maturation = engine.LandCalibration.For(template).BiofilterMaturationDays;
        if (site.Lots[0].Biofilter != BiofilterType.None) engine.Advance(maturation + 1);
        return site;
    }

    /// <summary>Hold a site at competence 1.0: three completed cycles (spec §3.3, 0.4 + 3 × 0.2).</summary>
    private static void AtFullCompetence(RASFacility site) => site.Competence.CompletedCycles = 3;

    private static double Mean(FishPopulation fish) => fish.GetTotalBiomassKg() / Math.Max(1, fish.TotalCount);

    // ── Acceptance 1: competence 0.4 → the achieved result, 1.0 → the plan ─

    /// <summary>
    /// Over 20 seeds at a 12 and a 13 °C setpoint, a 400 g cohort at competence
    /// 0.4 passes through the achieved result, 3.5-4.2 kg HOG ([L:R1] Q2 2026:
    /// 3.5 kg HOG, [V:17]), within 9-11 months: under 4.2 at 9 months, over 3.5
    /// at 11, and its middle (3.85) in between. At competence 1.0 it passes
    /// through 4.8-5.6 kg LW within 8-10 months and reaches the plan's 5.2 kg
    /// in 8-9 months ([L:R1]: 400 g → 5.2 kg in 8-9 months). HOG is SE's own
    /// 0.83 of live weight ([L:R1]). The cycle keeps about 97 % of the fish ([L:R2]).
    /// </summary>
    [Fact]
    public void Acceptance1_Competence04_GivesTheAchievedResult_AndCompetence10_ThePlan_OverTwentySeeds()
    {
        var cal = CalibrationRegistry.Default.Facilities.Land;
        double hog = cal.Production.HogPerLw;
        var low = new List<double>();
        var full = new List<double>();
        foreach (double setpoint in new[] { 12.0, 13.0 })
            for (int seed = 1; seed <= 20; seed++)
                foreach (bool plan in new[] { false, true })
                {
                    var engine = NewEngine(400 + seed);
                    var site = Commission(engine);
                    if (plan) AtFullCompetence(site);
                    Assert.True(engine.SetVannstrategi(site.Id, site.RecirculationRate, site.DeepIntakeShare, setpoint, true).Ok);
                    engine.Advance(5);
                    Assert.Equal(plan ? 1.0 : 0.4, engine.LandCompetenceOf(site).Total, 12);
                    var tank = site.Lots[0].Tanks[0];
                    var stocked = engine.StockLandTank(site.Id, tank.Id, engine.PreviewLandStocking(site.Id, tank.Id, 0).MaxSmolts);
                    Assert.True(stocked.Ok, stocked.Message);
                    var fish = site.PopulationIn(tank)!;
                    int n = fish.TotalCount;
                    var weights = new List<double> { Mean(fish) };
                    var counts = new List<int> { n };
                    for (int d = 1; d <= (int)Math.Ceiling(11 * Month); d++)
                    {
                        engine.Advance(1);
                        weights.Add(Mean(fish));
                        counts.Add(fish.TotalCount);
                    }
                    double At(double months) => weights[(int)Math.Round(months * Month)];
                    int Crossing(double kg) => weights.FindIndex(w => w >= kg);
                    if (plan)
                    {
                        int day = Crossing(cal.Hfs.PlanHarvestWeightKg);
                        full.Add(day / Month);
                        Assert.True(At(8) <= 5.6 && At(10) >= 4.8, $"seed {seed} {setpoint} °C: {At(8):F2} kg at 8 months, {At(10):F2} at 10");
                        Assert.InRange(day / Month, 8.0, 9.0);
                        Assert.InRange((double)counts[day] / n, 0.96, 0.98); // the plan's ~97 % survival [L:R2]
                    }
                    else
                    {
                        int day = Crossing(3.85 / hog);
                        low.Add(day / Month);
                        Assert.True(At(9) * hog <= 4.2 && At(11) * hog >= 3.5, $"seed {seed} {setpoint} °C: {At(9) * hog:F2} kg HOG at 9 months, {At(11) * hog:F2} at 11");
                        Assert.InRange(day / Month, 9.0, 11.0);
                    }
                }
        _output.WriteLine($"competence 0.4: 3.85 kg HOG at {low.Min():F2}-{low.Max():F2} months; competence 1.0: 5.2 kg LW at {full.Min():F2}-{full.Max():F2} months");
    }

    // ── Acceptance 2: FCR ──────────────────────────────────────────────────

    /// <summary>
    /// The economic FCR of a cohort's cycle (feed delivered over the live
    /// weight it put on, harvested less stocked) is 1.02-1.15 on an HFS lot
    /// ([L:A3] 1.02, [L:R2] 1.03-1.06, [L:G2] 1.1) and 1.1-1.3 in the RAS
    /// hall ([L:R7], [L:S9]), at competence 0.4 and 1.0.
    /// </summary>
    [Theory]
    [InlineData(LandTemplate.HfsLot, 1.02, 1.15)]
    [InlineData(LandTemplate.RasHall, 1.10, 1.30)]
    public void Acceptance2_TheFcr_IsInItsBand(LandTemplate template, double min, double max)
    {
        foreach (bool plan in new[] { false, true })
        {
            var engine = NewEngine(420);
            var site = Commission(engine, template);
            if (plan) AtFullCompetence(site);
            var t = engine.LandCalibration.For(template);
            var tank = site.Lots[0].Tanks[0];
            int count = engine.PreviewLandStocking(site.Id, tank.Id, 0).MaxSmolts;
            Assert.True(engine.StockLandTank(site.Id, tank.Id, count).Ok);
            var fish = site.PopulationIn(tank)!;
            double stockedKg = fish.GetTotalBiomassKg();
            double feedBefore = site.Account.FeedKg;
            int guard = 0;
            while (Mean(fish) < t.PlanHarvestWeightKg && guard++ < 700) engine.Advance(1);
            double fcr = (site.Account.FeedKg - feedBefore) / (fish.GetTotalBiomassKg() - stockedKg);
            _output.WriteLine($"{template} competence {(plan ? 1.0 : 0.4)}: eFCR {fcr:F3} over {guard} days to {t.PlanHarvestWeightKg} kg");
            Assert.InRange(fcr, min, max);
        }
    }

    // ── Acceptance 3: the growth shortfall and the feed-plan penalty ───────

    /// <summary>
    /// A cohort below 85 % of its plan for 30 days raises GrowthShortfall
    /// (warning, counter verb build_competence) naming the driver that cost it
    /// the most: a 50 % feeding plan names the feeding plan; reuse past the
    /// HFS lot's means (no biofilter: the ammonia rises before the CO2 does,
    /// docs/BALANCE.md §1y) names the gills; a setpoint dropped to 7 °C after
    /// stocking on the 12 °C plan names the temperature. It is told at 30 days
    /// and every 30 days while it lasts (the notification budget), not every day.
    /// </summary>
    [Theory]
    [InlineData("feed_plan", 50.0, -1.0, 12.0)]
    [InlineData("gill_health", 100.0, 0.995, 12.0)]
    [InlineData("temperature", 100.0, -1.0, 7.0)]
    public void Acceptance3_ACohortBehindItsPlanFor30Days_RaisesGrowthShortfall_NamingItsDriver(string driver, double rationPct, double reuse,
        double setpointC)
    {
        var engine = NewEngine(430);
        var site = Commission(engine);
        AtFullCompetence(site);
        var events = new List<GameEvent>();
        engine.GameEvents.Delivered += events.Add;
        var tank = site.Lots[0].Tanks[0];
        Assert.True(engine.StockLandTank(site.Id, tank.Id, engine.PreviewLandStocking(site.Id, tank.Id, 0).MaxSmolts).Ok);
        if (rationPct < 100.0)
            Assert.True(engine.SetLandFeedPlan(site.Id, rationPct).Ok);
        if (reuse >= 0.0 || setpointC != site.TargetWaterTemp)
            Assert.True(engine.SetVannstrategi(site.Id, reuse >= 0.0 ? reuse : site.RecirculationRate, site.DeepIntakeShare, setpointC, true).Ok);
        var fish = site.PopulationIn(tank)!;
        int behindSince = -1, raisedOn = -1;
        for (int d = 0; d < 330 && raisedOn < 0; d++)
        {
            engine.Advance(1);
            engine.DrainDayNotes();
            if (behindSince < 0 && tank.Plan!.ShareOfPlan(Mean(fish)) < 0.85) behindSince = engine.State.Day;
            if (events.Any(e => e.Type == GameEventType.GrowthShortfall)) raisedOn = engine.State.Day;
        }
        Assert.True(behindSince >= 0, "the cohort never fell behind");
        Assert.True(raisedOn >= 0, $"no shortfall raised (behind from day {behindSince})");
        var raised = Assert.Single(events, e => e.Type == GameEventType.GrowthShortfall);
        _output.WriteLine($"{driver}: behind from day {behindSince}, raised on day {raisedOn}: {raised.Message}");
        Assert.InRange(raisedOn - behindSince, 29, 31);
        Assert.Equal(driver, raised.Variant);
        Assert.Equal(site.Id, raised.UnitId);
        Assert.Equal(GameEventSeverity.Warning, raised.Severity);
        Assert.Equal(tank.Plan!.Driver(), driver);
        // 30 days on, still behind: told once more, not every day.
        for (int d = 0; d < 30; d++) { engine.Advance(1); engine.DrainDayNotes(); }
        Assert.Equal(2, events.Count(e => e.Type == GameEventType.GrowthShortfall));
    }

    /// <summary>
    /// A feed-plan change at competence under 0.6 raises the FCR by the
    /// registry's 0.12 for 60 days ([L:R2] 2025's feed change); at 0.6 and over
    /// it costs nothing. The penalty is the land plan's FCR at 2 kg, plus 0.12.
    /// </summary>
    [Fact]
    public void Acceptance3_AFeedPlanChange_AtCompetenceUnder06_RaisesTheFcrByTheRegistryAmountFor60Days()
    {
        var engine = NewEngine(431);
        var site = Commission(engine);
        var growth = engine.LandCalibration.Growth;
        var tank = site.Lots[0].Tanks[0];
        Assert.True(engine.StockLandTank(site.Id, tank.Id, 50_000).Ok);
        var fish = site.PopulationIn(tank)!;
        var holding = engine.HoldingOf(fish);
        double baseFcr = LandHooks.BiologicalFcrAt2Kg(engine, holding, fish)!.Value;
        Assert.Equal(engine.LandCalibration.Production.BiologicalFcrAt2Kg, baseFcr, 12);
        var changed = engine.SetLandFeedPlan(site.Id, 80.0);
        Assert.True(changed.Ok);
        Assert.Equal(growth.FeedPlanFcrPenalty, changed.Value2, 12);
        int start = engine.State.Day;
        for (int d = 0; d < growth.FeedPlanPenaltyDays; d++)
        {
            Assert.Equal(baseFcr + growth.FeedPlanFcrPenalty, LandHooks.BiologicalFcrAt2Kg(engine, engine.HoldingOf(fish), fish)!.Value, 12);
            engine.Advance(1);
        }
        Assert.Equal(start + growth.FeedPlanPenaltyDays, engine.State.Day);
        Assert.Equal(baseFcr, LandHooks.BiologicalFcrAt2Kg(engine, engine.HoldingOf(fish), fish)!.Value, 12);
        Assert.Equal(0.8, fish.Ration!.EffectiveFraction, 12);

        // At 0.6 (one completed cycle) a change costs nothing.
        site.Competence.CompletedCycles = 1;
        var free = engine.SetLandFeedPlan(site.Id, 100.0);
        Assert.Equal(0.0, free.Value2);
        Assert.Equal(baseFcr, LandHooks.BiologicalFcrAt2Kg(engine, engine.HoldingOf(fish), fish)!.Value, 12);
        Assert.Equal(LandSiteReasons.OutOfRange, engine.SetLandFeedPlan(site.Id, 40.0).ErrorKey);
    }

    // ── Acceptance 4: the utilisation cap and its levers ───────────────────

    /// <summary>
    /// The cap starts at 55 % of the nameplate (competence 0.4) and rises
    /// linearly to 100 % at 1.0: each lever adds its competence and costs its
    /// price (research +0.1; a driftsleder +0.1 at 2.5 M a year; training +0.05
    /// a half-year up to +0.15 for 3 M; consultants +0.1 for 12 months for 8 M;
    /// staff hired 180 days before the first fish +0.1; each completed cycle
    /// +0.2), and a stocking past the cap is refused with util_cap.
    /// </summary>
    [Fact]
    public void Acceptance4_TheUtilisationCap_StartsAt55Percent_AndRisesPerLeverAndCycle_EachAtItsCost()
    {
        var engine = NewEngine(440);
        var cal = engine.LandCalibration.Competence;
        var built = engine.CommissionLandSite("pad_a", LandTemplate.HfsLot, 1, "Storholmen");
        var site = engine.FindLandSite(built.Id)!;
        double Cap() => engine.LandUtilisationCapOf(site);
        double Expected(double competence) => 0.55 + 0.45 * (competence - 0.4) / 0.6;
        Assert.Equal(0.55, Cap(), 12);

        // Early hiring: staff 180 days before the first fish.
        Assert.True(engine.HireLandStaffEarly(site.Id).Ok);
        Assert.Equal(LandSiteReasons.AlreadyActive, engine.HireLandStaffEarly(site.Id).ErrorKey);
        engine.Advance(cal.EarlyHiringLeadDays);

        // The cap refuses at 55 % of the nameplate's planned production.
        var t = engine.LandCalibration.Hfs;
        double hogPerSmolt = t.PlanHarvestWeightKg * engine.LandCalibration.Production.CycleSurvival * engine.LandCalibration.Production.HogPerLw;
        var preview = engine.PreviewLandStocking(site.Id, "t01", 0);
        Assert.Equal((int)Math.Floor(0.55 * t.NameplateTonnesHogPerYear * 1000.0 / hogPerSmolt), preview.UtilisationRoomSmolts);
        int stocked = 0;
        foreach (var tank in site.Lots[0].Tanks.Where(k => k.Role == TankRole.GrowOut))
        {
            int room = engine.PreviewLandStocking(site.Id, tank.Id, 0).DensityRoomSmolts;
            var r = engine.StockLandTank(site.Id, tank.Id, room);
            if (!r.Ok) { Assert.Equal(LandSiteReasons.UtilCap, r.ErrorKey); break; }
            stocked += room;
        }
        Assert.True(stocked * hogPerSmolt <= 0.55 * t.NameplateTonnesHogPerYear * 1000.0 + 1e-6);
        Assert.True(CompetenceModel.HiredEarly(site.Competence, cal));
        Assert.Equal(Expected(0.5), Cap(), 12);

        // Research.
        engine.Research.ForceUnlock(SimulationEngine.LandResearchNodeId);
        Assert.Equal(Expected(0.6), Cap(), 12);

        // A driftsleder: +0.1 and 2.5 M a year, booked day by day.
        var booked = new List<(string Category, decimal Amount)>();
        engine.PnlBooked += (_, category, amount, income) => { if (!income) booked.Add((category, amount)); };
        Assert.True(engine.HireDriftsleder(site.Id).Ok);
        Assert.Equal(Expected(0.7), Cap(), 12);
        engine.Advance(10);
        Assert.Equal(10 * cal.DriftslederNokPerYear / SimCalendar.DaysPerYear,
            -booked.Where(b => b.Category == "land_competence").Sum(b => b.Amount), 6); // costs book negative

        // Training: 3 M now, +0.05 a half-year up to +0.15.
        decimal cash = engine.FinanceManager.Balance;
        booked.Clear();
        Assert.True(engine.StartTrainingProgramme(site.Id).Ok);
        Assert.Equal(cal.TrainingNok, cash - engine.FinanceManager.Balance);
        Assert.Equal(LandSiteReasons.AlreadyActive, engine.StartTrainingProgramme(site.Id).ErrorKey);
        Assert.Equal(Expected(0.7), Cap(), 12);
        engine.Advance(cal.TrainingStepDays);
        Assert.Equal(Expected(0.75), Cap(), 12);

        // Consultants: 8 M, +0.1 for 12 months.
        cash = engine.FinanceManager.Balance;
        Assert.True(engine.HireConsultancy(site.Id).Ok);
        Assert.Equal(cal.ConsultancyNok, cash - engine.FinanceManager.Balance);
        Assert.Equal(Expected(0.85), Cap(), 12);
        Assert.Equal(LandSiteReasons.AlreadyActive, engine.HireConsultancy(site.Id).ErrorKey);
        engine.Advance(2 * cal.TrainingStepDays);
        Assert.Equal(Expected(0.95), Cap(), 12);                   // training complete (+0.15)
        engine.Advance(cal.ConsultancyDays - 2 * cal.TrainingStepDays);
        Assert.Equal(Expected(0.85), Cap(), 12);                   // the consultants left

        // A completed cycle: +0.2, capped at 1.
        site.Competence.CompletedCycles = 1;
        Assert.Equal(1.0, Cap(), 12);
        Assert.Equal(1.0, engine.LandCompetenceOf(site).Total, 12);

        // Every lever is the grow-out site's: a post-smolt site runs on the plan.
        var module = engine.CommissionLandSite("pad_delta", LandTemplate.PostSmoltModule, 1, "Elveøyra");
        Assert.Equal(LandSiteReasons.WrongSiteKind, engine.HireDriftsleder(module.Id).ErrorKey);
        Assert.Equal(1.0, engine.LandUtilisationCapOf(engine.FindLandSite(module.Id)!));
    }

    /// <summary>
    /// The cap binds: stocking every tank to its room, harvesting and stocking
    /// again within the rolling year is refused with util_cap once the year's
    /// planned production would pass 55 % of the nameplate, and never sooner
    /// than the room allows (the planned production stays within the cap).
    /// </summary>
    [Fact]
    public void Acceptance4_AStockingPastTheUtilisationCap_IsRefused()
    {
        var engine = NewEngine(442);
        var site = Commission(engine);
        var cal = engine.LandCalibration;
        double hogPerSmolt = cal.Hfs.PlanHarvestWeightKg * cal.Production.CycleSurvival * cal.Production.HogPerLw;
        double capKg = 0.55 * cal.Hfs.NameplateTonnesHogPerYear * 1000.0;
        int stocked = 0;
        string? refusal = null;
        for (int round = 0; round < 5 && refusal == null; round++)
        {
            foreach (var tank in site.Lots[0].Tanks)
            {
                int room = engine.PreviewLandStocking(site.Id, tank.Id, 0).DensityRoomSmolts;
                if (room <= 0 || !tank.Pen.IsEmpty) continue;
                var result = engine.StockLandTank(site.Id, tank.Id, room);
                if (!result.Ok) { refusal = result.ErrorKey; break; }
                stocked += room;
            }
            foreach (var tank in site.Lots[0].Tanks.Where(k => !k.Pen.IsEmpty).ToList())
                Assert.True(engine.HarvestLandTank(site.Id, tank.Id).Ok);
        }
        Assert.Equal(LandSiteReasons.UtilCap, refusal);
        Assert.True(stocked * hogPerSmolt <= capKg + 1e-6, $"{stocked * hogPerSmolt / 1000.0:N0} t planned over the cap");
        Assert.True(stocked * hogPerSmolt > 0.5 * capKg);
        Assert.Equal(engine.PreviewLandStocking(site.Id, "t01", 0).UtilisationRoomSmolts,
            (int)Math.Max(0.0, Math.Floor((capKg - site.StockingLog.Sum(r => r.PlannedHogKg)) / hogPerSmolt)));
    }

    /// <summary>
    /// Harvesting a full rotation of the grow-out tanks' planned fish completes
    /// a cycle (+0.2 competence, the cap 55 → 70 %), as the Oppkjøring shows.
    /// </summary>
    [Fact]
    public void Acceptance4_AHarvestedRotation_CompletesACycle_AndRaisesTheCap()
    {
        var engine = NewEngine(441);
        var site = Commission(engine, LandTemplate.HfsPilotLot);
        long rotation = engine.LandRotationFish(site);
        Assert.True(rotation > 0);
        Assert.Equal(0.55, engine.LandUtilisationCapOf(site), 12);
        var view = engine.LandCompetenceViewOf(site.Id)!;
        Assert.Equal(new[] { 0.55, 0.70, 0.85, 1.00 }, view.Milestones.Select(m => m.Cap));
        Assert.Equal(new[] { true, false, false, false }, view.Milestones.Select(m => m.Reached));
        Assert.Equal(new[] { 0.4, 0.6, 0.8, 1.0 }, view.Milestones.Select(m => Math.Round(m.Competence, 9)));
        // Harvest fish until a rotation's worth has left the tanks.
        while (site.Competence.CompletedCycles == 0)
        {
            foreach (var tank in site.Lots[0].Tanks.Where(k => k.Role == TankRole.GrowOut))
            {
                var fish = site.PopulationIn(tank);
                if (fish == null)
                {
                    int room = engine.PreviewLandStocking(site.Id, tank.Id, 0).MaxSmolts;
                    if (room > 0) Assert.True(engine.StockLandTank(site.Id, tank.Id, room).Ok);
                }
                else if (Mean(fish) >= 2.0)
                    Assert.True(engine.HarvestLandTank(site.Id, tank.Id).Ok);
            }
            engine.Advance(1);
            Assert.True(engine.State.Day < 2_000, "no cycle completed");
        }
        Assert.True(site.Competence.HarvestedFish >= rotation);
        Assert.Equal(0.6, engine.LandCompetenceOf(site).Total, 12);
        Assert.Equal(0.70, engine.LandUtilisationCapOf(site), 12);
        Assert.Equal(new[] { true, true, false, false }, engine.LandCompetenceViewOf(site.Id)!.Milestones.Select(m => m.Reached));
    }

    // ── Acceptance 5: planned harvests, the seasonal price, the size discount ─

    /// <summary>
    /// A harvest planned into an ISO week is slaughtered in that week; the
    /// planner's weekly price follows the market's seasonal curve (the monthly
    /// offsets); fish under 3 kg HOG take the S2-04 size discount and fish at
    /// the plan's weight do not ([L:A7], [L:P1]).
    /// </summary>
    [Fact]
    public void Acceptance5_PlannedHarvests_RunInTheirWeek_AtTheSeasonalPrice_SmallFishTakeTheSizeDiscount()
    {
        var engine = NewEngine(450);
        var site = Commission(engine);
        AtFullCompetence(site);
        var tanks = site.Lots[0].Tanks;
        Assert.True(engine.StockLandTank(site.Id, tanks[0].Id, 60_000).Ok);
        Assert.True(engine.StockLandTank(site.Id, tanks[1].Id, 60_000).Ok);
        var small = site.PopulationIn(tanks[0])!;
        var big = site.PopulationIn(tanks[1])!;
        while (Mean(small) < 3.0 / CalibrationRegistry.Default.Harvest.HogYieldFraction) engine.Advance(1);
        foreach (var c in big.Cohorts) c.MeanWeightKg = 5.2;

        // The planner's weeks follow the seasonal curve.
        var planner = engine.LandHarvestPlannerOf(site.Id)!;
        Assert.Equal(engine.LandCalibration.Growth.HarvestPlanWeeks, planner.Weeks.Count);
        Assert.All(planner.Weeks, w => Assert.Equal(1, SimCalendar.IsoWeekday(w.WeekStartDay)));
        var offsets = CalibrationRegistry.Default.Market.MonthlySeasonalOffsets;
        var pairs = planner.Weeks.Skip(6).Select(w => ((double)w.ExpectedPriceNok, (double)offsets[SimCalendar.MonthIndex(w.WeekStartDay + 3)])).ToList();
        Assert.True(Correlation(pairs) > 0.6, $"the expected weekly price follows the seasonal offsets (r {Correlation(pairs):F2})");
        var smallTank = planner.Tanks.Single(t => t.TankId == tanks[0].Id);
        Assert.True(smallTank.SizeFactorByWeek[0] < 1.0, $"size factor {smallTank.SizeFactorByWeek[0]:F3} under 3 kg HOG");
        Assert.True(smallTank.WeightByWeekKg.Zip(smallTank.WeightByWeekKg.Skip(1)).All(p => p.Second > p.First));

        // A plan five weeks out: the fish go in that ISO week.
        int monday = planner.Weeks[4].WeekStartDay;
        var smallQuote = engine.QuoteLandHarvestPlan(site.Id, new[] { tanks[0].Id }, monday);
        var bigQuote = engine.QuoteLandHarvestPlan(site.Id, new[] { tanks[1].Id }, monday);
        Assert.True(smallQuote.Allowed && bigQuote.Allowed, smallQuote.Message + " / " + bigQuote.Message);
        Assert.True(smallQuote.SizeFactor < bigQuote.SizeFactor);
        Assert.True(bigQuote.SizeFactor >= 1.0);
        var planned = engine.PlanLandHarvest(site.Id, new[] { tanks[1].Id }, monday);
        Assert.True(planned.Ok, planned.Message);
        Assert.Equal(LandSiteReasons.AlreadyPlanned, engine.QuoteLandHarvestPlan(site.Id, new[] { tanks[1].Id }, monday).Reason);
        Assert.Equal(LandSiteReasons.NoSlot, engine.QuoteLandHarvestPlan(site.Id, new[] { tanks[0].Id }, engine.State.Day - 7).Reason);
        int reportsBefore = engine.State.HarvestBatchesSold;
        while (engine.State.Day < monday + 2 * SimCalendar.DaysPerWeek) engine.Advance(1);
        var reports = engine.State.HarvestReports.Where(r => r.HoldingId == site.Id).ToList();
        Assert.NotEmpty(reports);
        Assert.True(engine.State.HarvestBatchesSold > reportsBefore);
        Assert.All(reports, r => Assert.InRange(r.Day, monday, monday + SimCalendar.DaysPerWeek - 1));
        Assert.Null(site.PopulationIn(tanks[1]));
        Assert.Equal(LandHarvestPlanStatus.Done, site.HarvestPlans.Single().Status);
        Assert.Equal(reports.Sum(r => r.HogKg), site.Account.HarvestedHogKg, 6);
        _output.WriteLine($"planned week {SimCalendar.IsoWeek(monday)} (day {monday}): slaughtered days {string.Join(",", reports.Select(r => r.Day))}; quoted {bigQuote.ExpectedReferencePriceNok:F2} NOK/kg");
    }

    private static double Correlation(IReadOnlyList<(double X, double Y)> pairs)
    {
        double mx = pairs.Average(p => p.X), my = pairs.Average(p => p.Y);
        double sxy = pairs.Sum(p => (p.X - mx) * (p.Y - my));
        double sx = Math.Sqrt(pairs.Sum(p => (p.X - mx) * (p.X - mx)));
        double sy = Math.Sqrt(pairs.Sum(p => (p.Y - my) * (p.Y - my)));
        return sx > 0 && sy > 0 ? sxy / (sx * sy) : 0.0;
    }

    // ── Acceptance 6: the steady-state farming cost ────────────────────────

    /// <summary>
    /// A full HFS lot in PO5 run in rotation for three years, measured on its
    /// third year's own books: at full utilisation (competence 1.0, the plan's
    /// 5.2 kg) the farming cost ex depreciation is 60-75 NOK/kg HOG ([L:R1]
    /// Q2 2026 run-rate 70-75 and the mid-2027 target 60-65, [L:R9]); held at
    /// 55 % (competence 0.4, harvesting the achieved result's 4.2 kg LW = 3.5
    /// kg HOG, [L:R1]) it is 70-80 (Salmon Evolution 2025: 77.1 at 56 %). The
    /// energy share of the cost is reported and inside 5-20 %.
    /// </summary>
    [Theory]
    [InlineData(true, 60.0, 75.0)]
    [InlineData(false, 70.0, 80.0)]
    public void Acceptance6_TheSteadyStateFarmingCost_IsInItsBand_WithItsEnergyShare(bool fullUtilisation, double min, double max)
    {
        var engine = NewEngine(460);
        var site = Commission(engine);
        if (fullUtilisation) AtFullCompetence(site);
        double harvestKg = fullUtilisation ? engine.LandCalibration.Hfs.PlanHarvestWeightKg : 4.2;
        var year = RunRotation(engine, site, harvestKg, holdAtStart: !fullUtilisation);
        double share = year.HarvestedHogKg / 1000.0 / engine.LandCalibration.Hfs.NameplateTonnesHogPerYear;
        _output.WriteLine($"{(fullUtilisation ? "full" : "55 %")}: {year.FarmingCostPerKgHog:F1} NOK/kg HOG on {year.HarvestedHogKg / 1000.0:N0} t ({share:P0} of the nameplate), energy share {year.EnergyShare:P1}, cap {engine.LandUtilisationCapOf(site):P0}");
        Assert.InRange(year.FarmingCostPerKgHog, min, max);
        Assert.InRange(year.EnergyShare, 0.05, 0.20);
        Assert.Equal(fullUtilisation ? 1.0 : 0.55, engine.LandUtilisationCapOf(site), 12);
    }

    /// <summary>
    /// Three years of a lot in rotation (each tank stocked to its room in turn,
    /// ordered to the S2-04 harvest at <paramref name="harvestKg"/>); returns the
    /// third year's window of the site's books. With <paramref name="holdAtStart"/>
    /// the competence is held at 0.4 (no cycle counts).
    /// </summary>
    internal static LandSiteAccount RunRotation(SimulationEngine engine, RASFacility site, double harvestKg, bool holdAtStart)
    {
        var lot = site.Lots[0];
        int stagger = SimCalendar.DaysPerYear / lot.Tanks.Count;
        var ordered = new HashSet<FishPopulation>(ReferenceEqualityComparer.Instance);
        LandSiteAccount? start = null;
        int first = engine.State.Day;
        for (int day = 0; day < 3 * SimCalendar.DaysPerYear; day++)
        {
            if (day == 2 * SimCalendar.DaysPerYear) start = site.Account.Clone();
            for (int i = 0; i < lot.Tanks.Count; i++)
            {
                var tank = lot.Tanks[i];
                var fish = site.PopulationIn(tank);
                if (fish != null && fish.TotalCount > 0)
                {
                    if (!ordered.Contains(fish) && Mean(fish) >= harvestKg && engine.OrderHarvest(site.Id, new[] { fish.Id }, 1.0).Ok)
                        ordered.Add(fish);
                }
                else if (tank.Pen.IsEmpty && day >= i * stagger)
                {
                    int n = engine.PreviewLandStocking(site.Id, tank.Id, 0).MaxSmolts;
                    if (n > 1_000) Assert.True(engine.StockLandTank(site.Id, tank.Id, n).Ok);
                }
            }
            engine.Advance(1);
            if (holdAtStart)
            {
                site.Competence.HarvestedFish = 0;
                site.Competence.CompletedCycles = 0;
            }
        }
        Assert.Equal(first + 3 * SimCalendar.DaysPerYear, engine.State.Day);
        return site.Account.Minus(start!);
    }

    // ── Determinism: twins agree, a land game saves and resumes bit for bit ─

    [Fact]
    public void TwinLandGames_WithEveryNewVerb_AgreeBitForBit_AndKeepEveryFishInOneUnit()
    {
        string Run()
        {
            var engine = NewEngine(471, events: true);
            var site = Commission(engine);
            var module = engine.FindLandSite(engine.CommissionLandSite("pad_delta", LandTemplate.PostSmoltModule, 1, "Elveøyra").Id)!;
            engine.Advance(61);
            var sea = new SeaSite("sea_twin", "zone-twin") { MABTonnes = 3_000 };
            engine.State.SeaSites.Add(sea);
            Assert.True(engine.HireConsultancy(site.Id).Ok);
            Assert.True(engine.StockLandTank(site.Id, "t01", 60_000).Ok);
            Assert.True(engine.StockLandTank(site.Id, "t02", 60_000).Ok);
            Assert.True(engine.StockLandTank(module.Id, module.Lots[0].Tanks[0].Id, 50_000).Ok);
            Assert.True(engine.ApplyVannstrategiPreset(site.Id, "balansert").Ok);
            engine.Advance(150);
            Assert.True(engine.SellPostSmolt(module.Id, module.Lots[0].Tanks[0].Id, 10_000).Ok);
            Assert.True(engine.TransferTankToSea(module.Id, module.Lots[0].Tanks[0].Id, sea.Id, 0).Ok);
            var weeks = engine.LandHarvestPlannerOf(site.Id)!.Weeks;
            Assert.True(engine.PlanLandHarvest(site.Id, new[] { "t02" }, weeks[12].WeekStartDay).Ok);
            Assert.True(engine.SetLandFeedPlan(site.Id, 90.0).Ok);
            engine.Advance(200);
            Assert.Empty(UnitResolver.Violations(engine.State));
            Assert.Contains(sea.Pens, p => p.TotalCount > 0);
            return SaveStateComparer.Normalize(SaveStateComparer.Serialize(engine));
        }
        Assert.Equal(Run(), Run());
    }

    // ── Determinism: a land game saves and resumes bit for bit ─────────────

    [Fact]
    public void ALandGameMidCycle_WithItsPlansCompetenceAndATransfer_SavesAndResumesBitIdentically()
    {
        var engine = NewEngine(470);
        var site = Commission(engine);
        var module = engine.FindLandSite(engine.CommissionLandSite("pad_delta", LandTemplate.PostSmoltModule, 1, "Elveøyra").Id)!;
        engine.Advance(61);
        var sea = new SeaSite("sea_ps", "zone-ps") { MABTonnes = 3_000 };
        engine.State.SeaSites.Add(sea);
        Assert.True(engine.HireDriftsleder(site.Id).Ok);
        Assert.True(engine.StartTrainingProgramme(site.Id).Ok);
        Assert.True(engine.StockLandTank(site.Id, "t01", 60_000).Ok);
        Assert.True(engine.StockLandTank(module.Id, module.Lots[0].Tanks[0].Id, 40_000).Ok);
        Assert.True(engine.SetSalinity(module.Id, 20.0).Ok);
        Assert.True(engine.SetLandFeedPlan(site.Id, 85.0).Ok);
        engine.Advance(120);
        foreach (var c in module.PopulationIn(module.Lots[0].Tanks[0])!.Cohorts) c.MeanWeightKg = 0.3;
        Assert.True(engine.TransferTankToSea(module.Id, module.Lots[0].Tanks[0].Id, sea.Id, 0).Ok);
        var planner = engine.LandHarvestPlannerOf(site.Id)!;
        Assert.True(engine.PlanLandHarvest(site.Id, new[] { "t01" }, planner.Weeks[20].WeekStartDay).Ok);

        string saved = SaveStateComparer.Serialize(engine);
        var resumed = SaveStateComparer.Resume(saved, events: false);
        Assert.Equal(SaveStateComparer.Normalize(saved), SaveStateComparer.Normalize(SaveStateComparer.Serialize(resumed)));
        engine.Advance(200);
        resumed.Advance(200);
        Assert.Equal(SaveStateComparer.Normalize(SaveStateComparer.Serialize(engine)),
            SaveStateComparer.Normalize(SaveStateComparer.Serialize(resumed)));
        Assert.Contains(sea.Pens, p => p.TotalCount > 0);
        Assert.Equal(engine.FindLandSite(site.Id)!.Account.FeedNok, resumed.FindLandSite(site.Id)!.Account.FeedNok);
        // Reading the boards changes nothing and draws nothing.
        string before = SaveStateComparer.Serialize(engine);
        var streams = engine.Rng.CaptureStates();
        engine.LandBoardOf(site.Id);
        engine.VannstrategiPresetsOf(site.Id);
        engine.LandCompetenceViewOf(site.Id);
        engine.LandHarvestPlannerOf(site.Id);
        engine.PostSmoltPlannerOf(module.Id);
        engine.QuoteLandHarvestPlan(site.Id, new[] { "t02" }, planner.Weeks[10].WeekStartDay);
        Assert.Equal(before, SaveStateComparer.Serialize(engine));
        Assert.Equal(streams, engine.Rng.CaptureStates());
    }
}
