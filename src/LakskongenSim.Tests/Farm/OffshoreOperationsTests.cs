using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Calibration;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Farm.Offshore;
using LakskongenSim.Tests.Integration;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Farm;

/// <summary>
/// F2-07's operations on a running unit (roadmap F2-07, docs/FARM_TYPES_SPEC.md
/// §3.3 "OffshoreTickSystem", §3.4, §7.1 acceptance 1, 3, 5, 9): feeding from
/// the silo in heavy seas, bookings that wait for their sea, fish handling that
/// never runs above its limit, the shakedown and the teething deck, and the
/// save mid-booking and mid-tow.
/// </summary>
public class OffshoreOperationsTests
{
    private static readonly OffshoreCalibration Cal = OffshoreCalibration.Default;
    private readonly ITestOutputHelper _out;
    public OffshoreOperationsTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// Acceptance 1: on a 6 m day the fish eat at appetite 0.7 of a calm day's
    /// (the same game, the same fish, only the sea forced) while the silo holds
    /// feed, and nothing once it is empty (a counted silo-empty day); feeding
    /// needs no window, and the silo gives what they ate.
    /// </summary>
    [Fact]
    public void OnASixMetreDay_TheFishEatAtSevenTenths_WhileTheSiloLasts_AndNothingOnceItIsEmpty()
    {
        (SimulationEngine Engine, SeaSite Site) Unit()
        {
            var engine = OffshoreTestUnits.RichEngine(31);
            var site = OffshoreTestUnits.Commissioned(engine);
            OffshoreTestUnits.ClearDriftsplan(engine, site.Id);
            Assert.True(engine.StockOffshoreFarm(site.Id, 300_000).Ok);
            engine.Advance(20);
            return (engine, site);
        }
        var calm = Unit();
        var rough = Unit();
        int day = calm.Engine.State.Day + 1;
        calm.Engine.SeaOverrideForTests = (s, d) => d == day ? 3.0 : null;
        rough.Engine.SeaOverrideForTests = (s, d) => d == day ? 6.0 : null;
        double siloCalm = calm.Site.Offshore!.SiloTonnes, siloRough = rough.Site.Offshore!.SiloTonnes;
        calm.Engine.Advance(1);
        rough.Engine.Advance(1);
        var c = calm.Site.Offshore!;
        var r = rough.Site.Offshore!;
        Assert.Equal(1.0, OperabilityWindows.AppetiteFactor(3.0, Cal.Operability));
        Assert.Equal(0.7, OperabilityWindows.AppetiteFactor(6.0, Cal.Operability), 12);
        var pop = rough.Site.Pens[0];
        Assert.Equal(0.7, rough.Engine.AppetiteFactorFor(pop), 12);
        Assert.Equal(day, r.LastFeedDay);
        // The fish eat less than on the calm day: their appetite is 0.7 of it, and the growth law
        // takes the lost appetite as a water-quality penalty on the FCR too (FeedSystem.SatiationFcr),
        // so the feed per fish falls by less than the appetite (measured 0.91 of the calm day's).
        Assert.InRange(r.FeedTodayTonnes / c.FeedTodayTonnes, 0.7, 0.99);
        Assert.Equal(siloCalm - c.FeedTodayTonnes, c.SiloTonnes, 9);
        Assert.Equal(siloRough - r.FeedTodayTonnes, r.SiloTonnes, 9);
        Assert.Equal(0, r.SiloEmptyDays);

        // An empty silo on a 6 m day: no feed, the day counted.
        r.SiloTonnes = 0.0;
        double weight = rough.Site.Pens.Sum(p => p.GetTotalBiomassKg());
        int next = rough.Engine.State.Day + 1;
        rough.Engine.SeaOverrideForTests = (s, d) => d == next ? 6.0 : null;
        Assert.False(rough.Engine.FeedAvailableFor(pop));
        rough.Engine.Advance(1);
        Assert.NotEqual(next, r.LastFeedDay);
        Assert.Equal(1, r.SiloEmptyDays);
        Assert.True(rough.Site.Pens.Sum(p => p.GetTotalBiomassKg()) <= weight, "the fish grew without feed");
    }

    /// <summary>
    /// Acceptance 3 on a forced series: a delousing booked for a wellboat waits
    /// alongside with its pumping paused at 3.0 m, leaves to shelter at 5.5 m,
    /// pauses again at 4.0 m and treats the fish at 1.8 m; its charter runs from
    /// the booked day through every slipped day. A harvest order at the unit
    /// slaughters only on days its sea allows the offshore wellboat (2.5 m).
    /// </summary>
    [Fact]
    public void FishHandling_NeverRunsAboveItsLimit_TheWellboatPausesAndLeaves_OnAForcedSeries()
    {
        var engine = OffshoreTestUnits.RichEngine(33);
        var site = OffshoreTestUnits.Commissioned(engine);
        OffshoreTestUnits.ClearDriftsplan(engine, site.Id);
        Assert.True(engine.StockOffshoreFarm(site.Id, 300_000).Ok);
        engine.Advance(30);
        var unit = site.Offshore!;
        int booked = engine.State.Day + 1;
        var series = new Dictionary<int, double> { [booked] = 3.0, [booked + 1] = 5.5, [booked + 2] = 4.0, [booked + 3] = 1.8 };
        engine.SeaOverrideForTests = (s, d) => series.TryGetValue(d, out var hs) ? hs : 1.0;
        var result = engine.BookOffshoreOperation(site.Id, OffshoreOp.Delousing, booked, VesselClass.Standard, detail: "hydrolicer");
        Assert.True(result.Ok, result.Message);
        var booking = unit.Bookings.Single(b => b.Id == result.Id);
        int fish = site.Pens.Sum(p => p.TotalCount);
        var expected = new[] { OffshoreBookingStatus.Paused, OffshoreBookingStatus.Sheltering, OffshoreBookingStatus.Paused, OffshoreBookingStatus.Done };
        foreach (var status in expected)
        {
            int liceDayBefore = site.LastLiceCountDay;
            engine.Advance(1);
            Assert.Equal(status, booking.Status);
        }
        Assert.Equal(booked + 3, booking.CompletedDay);
        Assert.Equal(4, booking.DaysCharged);
        Assert.Equal(4 * Cal.Costs.WellboatNokPerDay, booking.ChargedNok);
        Assert.True(site.Pens.Sum(p => p.TotalCount) < fish, "the treatment never ran");

        // The S2-04 harvest order: batches only on a day within 2.5 m. The sea stays at
        // 3.5 m through the order's slot and 3 days past it, then is rough every third day:
        // the order waits out the slot, and every batch's day (the sea the unit stood in
        // that day, read right after it) is within the offshore wellboat's limit.
        var order = engine.OrderHarvest(site.Id);
        Assert.True(order.Ok, order.Message);
        int slot = engine.HarvestOrders.Single().SlotDay;
        int calmFrom = slot + 4;
        engine.SeaOverrideForTests = (s, d) => d < calmFrom || d % 3 == 0 ? 3.5 : 1.2;
        int reportsBefore = engine.HarvestReports.Count, seen = reportsBefore, firstBatchDay = -1;
        for (int d = 0; d < 60 && engine.HarvestOrders.Count > 0; d++)
        {
            engine.Advance(1);
            if (engine.HarvestReports.Count > seen)
            {
                Assert.True(unit.CurrentWaveHeight <= Cal.Operability.FishHandlingOffshoreWellboatHsM,
                    $"a batch was slaughtered on day {engine.State.Day} at {unit.CurrentWaveHeight} m");
                if (firstBatchDay < 0) firstBatchDay = engine.State.Day;
                seen = engine.HarvestReports.Count;
            }
        }
        Assert.Empty(engine.HarvestOrders);
        Assert.True(engine.HarvestReports.Count > reportsBefore);
        Assert.True(firstBatchDay > slot + 3, $"the order slaughtered on day {firstBatchDay}, in the rough days after its slot {slot}");
    }

    /// <summary>
    /// Acceptance 7, first half: no fjord storm wear on an offshore unit. Over
    /// a winter at off_1 with fish in the unit, every day's loss of net
    /// integrity and structural integrity is exactly the unit's own wear for
    /// the sea it stood in (spec §3.3: the nets' daily decay plus its share
    /// above 3 m, the structure's fatigue by the cube of Hs over its design),
    /// fjord storm days included: the fjord's storm damage, net decay and
    /// storm fish loss skip the unit (the F2-02 hook <c>SkipsFjordStormWear</c>).
    /// </summary>
    [Fact]
    public void AFjordStorm_WearsTheUnitOnlyByItsOwnSea()
    {
        var engine = OffshoreTestUnits.RichEngine(37);
        var site = OffshoreTestUnits.Commissioned(engine);
        OffshoreTestUnits.ClearDriftsplan(engine, site.Id);
        Assert.True(engine.StockOffshoreFarm(site.Id, 300_000).Ok);
        var unit = site.Offshore!;
        var s = Cal.Structure;
        double design = unit.DesignHsM + unit.DesignHsBonusM;
        int stormDays = 0;
        for (int d = 0; d < 240; d++)
        {
            double net = site.NetIntegrityPct, structure = unit.StructuralIntegrity;
            engine.Advance(1);
            double hs = unit.CurrentWaveHeight;
            double ownWear = s.NetDecayPctPerDay + s.NetDecayPerMetreOver * Math.Max(0.0, hs - s.NetDecayThresholdHsM);
            Assert.Equal(Math.Max(0.0, net - ownWear), site.NetIntegrityPct, 9);
            Assert.Equal(Math.Max(0.0, structure - s.FatigueCoeff * Math.Pow(hs / design, 3.0)), unit.StructuralIntegrity, 12);
            if (engine.Weather.CurrentState is WeatherState.Storm or WeatherState.Severe) stormDays++;
        }
        Assert.True(stormDays > 0, "no fjord storm in the window");
        Assert.True(engine.SkipsFjordStormWear(site));
    }

    /// <summary>
    /// The booking verbs refuse before any change (spec §3.4), and the first
    /// green window is the first forecast day at P ≥ 0.8.
    /// </summary>
    [Fact]
    public void Bookings_RefuseBeforeAnyChange_AndTheFirstGreenWindowIsTheFirstDayAtPointEight()
    {
        var engine = OffshoreTestUnits.RichEngine(35);
        var site = OffshoreTestUnits.Commissioned(engine);
        OffshoreTestUnits.ClearDriftsplan(engine, site.Id);
        var unit = site.Offshore!;
        int tomorrow = engine.State.Day + 1;
        Assert.Equal(SimulationEngine.OffshoreReasons.NotFound, engine.BookOffshoreOperation("nope", OffshoreOp.Resupply, tomorrow, VesselClass.Standard).ErrorKey);
        Assert.Equal("not_bookable", engine.BookOffshoreOperation(site.Id, OffshoreOp.Tow, tomorrow, VesselClass.Standard).ErrorKey);
        Assert.Equal("not_bookable", engine.BookOffshoreOperation(site.Id, OffshoreOp.Harvest, tomorrow, VesselClass.Standard).ErrorKey);
        Assert.Equal("no_vessel", engine.BookOffshoreOperation(site.Id, OffshoreOp.Resupply, tomorrow, VesselClass.Helicopter).ErrorKey);
        Assert.Equal("invalid_day", engine.BookOffshoreOperation(site.Id, OffshoreOp.Resupply, engine.State.Day, VesselClass.Standard).ErrorKey);
        Assert.Equal("invalid_count", engine.BookOffshoreOperation(site.Id, OffshoreOp.Stocking, tomorrow, VesselClass.Standard).ErrorKey);
        Assert.Equal("unknown_method", engine.BookOffshoreOperation(site.Id, OffshoreOp.Delousing, tomorrow, VesselClass.Standard, detail: "magic").ErrorKey);
        Assert.Empty(unit.Bookings);

        var first = engine.BookFirstGreenWindow(site.Id, OffshoreOp.Resupply, VesselClass.Standard);
        Assert.True(first.Ok, first.Message);
        var forecast = engine.SeaForecastFor(site)!;
        int lead = (int)first.Value - engine.State.Day;
        double p(int k) => OperabilityWindows.Probability(forecast, k, OffshoreOp.Resupply, VesselClass.Standard, Cal.Operability, Cal.Metocean);
        if (p(lead) >= SimulationEngine.GreenWindowProbability)
            for (int k = 1; k < lead; k++)
                Assert.True(p(k) < SimulationEngine.GreenWindowProbability);
        Assert.Equal(p(lead), first.Value2, 12);
        Assert.Equal("already_booked", engine.BookOffshoreOperation(site.Id, OffshoreOp.Resupply, tomorrow + 2, VesselClass.Standard).ErrorKey);
        // The helicopter is a second way to change the crew, not a duplicate.
        Assert.True(engine.BookOffshoreOperation(site.Id, OffshoreOp.CrewChange, tomorrow, VesselClass.Standard).Ok);
        Assert.True(engine.BookOffshoreOperation(site.Id, OffshoreOp.CrewChange, tomorrow, VesselClass.Helicopter).Ok);
        var cancel = engine.CancelOffshoreBooking(site.Id, first.Id);
        Assert.True(cancel.Ok);
        Assert.Equal("not_open", engine.CancelOffshoreBooking(site.Id, first.Id).ErrorKey);
    }

    /// <summary>
    /// Acceptance 5: a 90-day shakedown reveals at least one teething defect in
    /// at least 70 % of seeds (each hidden defect at 0.5 per 30 days); a defect
    /// fixed before the first stocking never fires; one left unfixed fires once
    /// fish are in the unit, and a defect that kills fish adds its hazard.
    /// </summary>
    [Fact]
    public void ANinetyDayShakedown_RevealsADefect_InSeventyPercentOfSeeds_AndAFixedDefectNeverFires()
    {
        const int Seeds = 40;
        int revealed = 0;
        foreach (int seed in Enumerable.Range(1, Seeds))
        {
            var engine = OffshoreTestUnits.RichEngine(100 + seed);
            var site = OffshoreTestUnits.Commissioned(engine, deck: true, shakedownDays: 90);
            var unit = site.Offshore!;
            Assert.InRange(unit.TeethingDefects.Count, 3, 4);
            Assert.Equal(unit.CommissionedDay + 90, unit.ShakedownEndDay);
            Assert.Equal(SimulationEngine.OffshoreReasons.ShakedownActive, engine.StockOffshoreFarm(site.Id, 10_000).ErrorKey);
            while (engine.State.Day < unit.ShakedownEndDay) engine.Advance(1);
            if (unit.TeethingDefects.Any(d => d.IsRevealed)) revealed++;
            Assert.All(unit.TeethingDefects, d => Assert.Equal(-1, d.FiredDay)); // no fish, nothing fires
        }
        _out.WriteLine($"{revealed} of {Seeds} 90-day shakedowns revealed a defect");
        Assert.True(revealed >= 0.7 * Seeds, $"{revealed} of {Seeds} shakedowns revealed a defect");

        // Fixed before stocking: never fires. Left unfixed: fires with the fish.
        var e = OffshoreTestUnits.RichEngine(201);
        var s = OffshoreTestUnits.Commissioned(e, deck: true, shakedownDays: 120);
        var u = s.Offshore!;
        while (e.State.Day < u.ShakedownEndDay) e.Advance(1);
        var known = u.TeethingDefects.Where(d => d.IsRevealed).ToList();
        Assert.NotEmpty(known);
        decimal cash = e.FinanceManager.Balance;
        foreach (var d in known)
            Assert.True(e.FixTeethingDefect(s.Id, d.Id).Ok);
        Assert.Equal(known.Count * e.TeethingFixCostNok(u), cash - e.FinanceManager.Balance, 2);
        Assert.Equal(SimulationEngine.OffshoreReasons.AlreadyFixing, e.FixTeethingDefect(s.Id, known[0].Id).ErrorKey);
        var hidden = u.TeethingDefects.Where(d => !d.IsRevealed).ToList();
        if (hidden.Count > 0)
            Assert.Equal(SimulationEngine.OffshoreReasons.NotRevealed, e.FixTeethingDefect(s.Id, hidden[0].Id).ErrorKey);
        e.Advance(Cal.Teething.FixDays + 1);
        Assert.True(e.StockOffshoreFarm(s.Id, 200_000).Ok);
        e.Advance(Cal.Teething.Cycle1RevealWindowDays + 2);
        foreach (var d in known)
            Assert.Equal(-1, d.FiredDay);
        foreach (var d in hidden)
        {
            Assert.True(d.IsRevealed, $"{d.Id} never revealed itself in cycle 1");
            Assert.Equal(u.FirstStockedDay + d.RevealOffsetDays, d.RevealedDay);
            Assert.Equal(d.RevealedDay, d.FiredDay);
        }
        var killer = hidden.FirstOrDefault(d => d.MortalityPp > 0.0);
        if (killer != null)
            Assert.Contains(e.UnitMortalityTerms(s.Pens[0]).ExtraHazards, h => h.DailyFraction == TeethingDeck.DailyMortalityFraction(killer));
    }

    /// <summary>
    /// Acceptance 9: a save taken mid-booking (a resupply waiting for its window
    /// with charter running) and one taken mid-tow (the tugs under way) resume
    /// bit for bit: the resumed game and the uninterrupted one save the same
    /// game 60 days later, and so does a twin that never saved.
    /// </summary>
    [Fact]
    public void ASaveMidBookingAndMidTow_ResumesBitIdentical_AsDoesATwin()
    {
        // Mid-booking.
        SimulationEngine Booked()
        {
            var engine = OffshoreTestUnits.RichEngine(37, events: true);
            var site = OffshoreTestUnits.Commissioned(engine);
            Assert.True(engine.StockOffshoreFarm(site.Id, 300_000).Ok);
            engine.Advance(10);
            int day = engine.State.Day + 1;
            engine.SeaOverrideForTests = (s, d) => d <= day + 1 ? 3.4 : null;
            Assert.True(engine.BookOffshoreOperation(site.Id, OffshoreOp.Resupply, day, VesselClass.Standard).Ok);
            engine.Advance(2);
            engine.SeaOverrideForTests = null;
            Assert.Contains(site.Offshore!.Bookings, b => b.Op == OffshoreOp.Resupply && b.Status == OffshoreBookingStatus.Waiting && b.DaysCharged == 2);
            return engine;
        }
        AssertResumesIdentically(Booked(), Booked());

        // Mid-tow.
        SimulationEngine Towing()
        {
            var engine = OffshoreTestUnits.UnlockedForOf1(38, events: true);
            var built = engine.BuildOffshoreUnit("off_1", OffshoreType.SemiSubmersible, ContractForm.Managed, 0.35);
            Assert.True(built.Ok, built.Message);
            var site = engine.FindOffshoreUnit(built.Id)!;
            OffshoreTestUnits.ShortenProject(engine, site);
            OffshoreTestUnits.AdvanceToTow(engine, site, 30);
            Assert.True(engine.StartTow(site.Id).Ok);
            engine.Advance(1);
            Assert.Equal(OffshoreProfiles.TowPhase, engine.OffshoreProjectOf(site)!.Current!.Key);
            return engine;
        }
        AssertResumesIdentically(Towing(), Towing());
    }

    private static void AssertResumesIdentically(SimulationEngine engine, SimulationEngine twin)
    {
        string saved = SaveStateComparer.Serialize(engine);
        var resumed = SaveStateComparer.Resume(saved, events: true);
        Assert.Equal(SaveStateComparer.Normalize(saved), SaveStateComparer.Normalize(SaveStateComparer.Serialize(twin)));
        for (int day = 0; day < 60; day++)
        {
            engine.Advance(1);
            resumed.Advance(1);
            twin.Advance(1);
        }
        string a = SaveStateComparer.Normalize(SaveStateComparer.Serialize(engine));
        string b = SaveStateComparer.Normalize(SaveStateComparer.Serialize(resumed));
        string c = SaveStateComparer.Normalize(SaveStateComparer.Serialize(twin));
        Assert.True(a == b, "the resumed game diverged at " + SaveStateComparer.FirstDifference(a, b));
        Assert.True(a == c, "the twin diverged at " + SaveStateComparer.FirstDifference(a, c));
    }

    /// <summary>
    /// The unit's gauges (spec §6.5): today's sea and appetite, the silo's days,
    /// the crew clock, the alarm load with its drivers summing to it, each
    /// board row's next green window, the bookings, the teething defects the
    /// player knows (hidden ones only counted).
    /// </summary>
    [Fact]
    public void TheUnitState_ReadsTheGauges_AndHidesTheHiddenDefects()
    {
        var engine = OffshoreTestUnits.RichEngine(39);
        var site = OffshoreTestUnits.Commissioned(engine, deck: true);
        Assert.True(engine.StockOffshoreFarm(site.Id, 300_000).Ok);
        engine.Advance(15);
        var unit = site.Offshore!;
        var state = engine.GetOffshoreUnitState(site.Id)!;
        Assert.True(state.Operational);
        Assert.Equal(unit.CurrentWaveHeight, state.HsTodayM);
        Assert.Equal(OperabilityWindows.AppetiteFactor(unit.CurrentWaveHeight, Cal.Operability), state.AppetiteToday);
        Assert.Equal(unit.SiloTonnes / unit.RecentFeedTonnes.Average(), state.SiloDays, 9);
        Assert.Equal((unit.DaysSinceCrewRotation, unit.CrewRotationDays), (state.CrewDaysSinceChange, state.CrewChangeIntervalDays));
        Assert.Equal(unit.AlarmLoad, state.AlarmDrivers.Sum(d => d.Value), 12);
        Assert.Equal(new[] { "base", "defects", "sensors", "crew" }, state.AlarmDrivers.Select(d => d.Driver));
        Assert.Equal(SimulationEngine.SeaBoardRows.Count(r => r.Op is not (OffshoreOp.Feeding or OffshoreOp.Tow)), state.NextWindows.Count);
        Assert.All(state.NextWindows, w => Assert.True(w.Day == -1 || w.Probability >= SimulationEngine.GreenWindowProbability));
        Assert.Equal(unit.TeethingDefects.Count, state.Defects.Count + state.HiddenDefects);
        Assert.All(state.Defects, d => Assert.True(d.IsRevealed));
        Assert.Null(engine.GetOffshoreUnitState("nope"));
    }
}
