using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Calibration;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Events;
using LakskongenSim.Farm;
using LakskongenSim.Farm.Offshore;
using LakskongenSim.Finance;
using LakskongenSim.Harvest;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Farm;

/// <summary>
/// Roadmap F2-08 acceptance 4 and the handling requirement (docs/FARM_TYPES_SPEC.md
/// §3.3 "Handling requirement and overdue days", §6.6): the requirement's
/// sources, the overdue clock a completed handling restarts, the 21/45/75-day
/// escalation on a forced series with its events, reports and pacing, the
/// withdrawal that loses the plot after 120 days, keeps the licences as
/// unplaced MTB and harvests the fish left by order, the appeal decided on the
/// handling done by then, the coastal lice order kept to the escalation, the
/// "handle when required" policy and a large unit's zone-by-zone delousing.
/// </summary>
public class OffshoreHandlingTests
{
    private static readonly OffshoreCalibration Cal = OffshoreCalibration.Default;
    private readonly ITestOutputHelper _out;

    public OffshoreHandlingTests(ITestOutputHelper output) => _out = output;

    /// <summary>A commissioned unit on a development-licence award (its eight licences bound), stocked, its standing orders cleared but the monthly net inspection, no incidents.</summary>
    internal static (SimulationEngine Engine, SeaSite Site) LicensedUnit(int seed, int fishPerZone = 300_000, bool events = false)
    {
        var engine = OffshoreTestUnits.RichEngine(seed, events);
        var site = OffshoreTestUnits.Commissioned(engine);
        OffshoreTestUnits.ClearDriftsplan(engine, site.Id);
        Assert.True(engine.SetDriftsplan(site.Id, OffshorePolicies.NetInspection).Ok); // the nets inspected when the sea allows
        var award = engine.AwardDevelopmentLicencesForTests(site.PlotId);
        engine.BindAwardToUnit(award, site);
        for (int z = 0; z < site.PhysicalPens.Count; z++)
            Assert.True(engine.StockOffshoreFarm(site.Id, fishPerZone).Ok);
        engine.IncidentHazardOverrideForTests = (s, k) => 0.0;
        return (engine, site);
    }

    /// <summary>
    /// Acceptance 4: a harvest ordered at a unit whose sea stays at 4.0 m (above
    /// both wellboats' handling limits) is never handled: the warning comes on
    /// overdue day 21 (deadline the order's day), Mattilsynet's order on day 45
    /// (an escalation on the same story, deadline the withdrawal day, with a
    /// report), the withdrawal case on day 75 (effective 120 days later, with a
    /// report naming the licences). On the effective day the plot is lost, the
    /// eight licences stay in the register unbound and unplaced, a stocking is
    /// refused, and the fish left are harvested by order the first day the sea
    /// lets the offshore wellboat handle them.
    /// </summary>
    [Fact]
    public void TheEscalation_Fires21_45_75_AndTheWithdrawalKeepsTheLicences()
    {
        var (engine, site) = LicensedUnit(101);
        var unit = site.Offshore!;
        var h = Cal.Handling;
        engine.Advance(30);
        engine.SeaOverrideForTests = (s, d) => 4.0;
        var delivered = new List<GameEvent>();
        engine.GameEvents.Delivered += e => { if (e.UnitId == site.Id || e.SiteId == site.Id) delivered.Add(e); };
        Assert.True(engine.OrderHarvest(site.Id).Ok);
        engine.Advance(1);
        int since = engine.State.Day;
        Assert.Equal((HandlingRequirement.Harvest, since, 0), (unit.Requirement, unit.RequirementSinceDay, unit.HandlingOverdueDays));

        void AdvanceTo(int overdue)
        {
            while (unit.HandlingOverdueDays < overdue) engine.Advance(1);
        }
        AdvanceTo(h.WarnDays - 1);
        Assert.Equal(HandlingStage.None, unit.HandlingStage);
        engine.Advance(1);
        Assert.Equal((HandlingStage.Warned, h.WarnDays), (unit.HandlingStage, unit.HandlingOverdueDays));
        var warning = Assert.Single(delivered, e => e.Type == GameEventType.HandlingOverdue);
        Assert.Equal(since + h.OrderDays, warning.DeadlineDay);
        Assert.Equal(h.WarnDays, unit.DaysWithoutHandlingWindow); // every day 4.0 m: no handling window

        AdvanceTo(h.OrderDays - 1);
        Assert.Equal(HandlingStage.Warned, unit.HandlingStage);
        engine.Advance(1);
        Assert.Equal(HandlingStage.Ordered, unit.HandlingStage);
        var order = Assert.Single(delivered, e => e.Type == GameEventType.MattilsynetOrder);
        Assert.True(order.IsEscalation, "the order escalates the warning's story");
        Assert.Equal(since + h.WithdrawalDays, order.DeadlineDay);
        var orderReport = Assert.Single(engine.IncidentReportsOf(site.Id), r => r.Kind == "mattilsynet_order");
        Assert.True(orderReport.Steps.Count >= 3);
        Assert.Equal(since, orderReport.Steps.Single(s => s.Key == "requirement").Day);
        Assert.Equal(h.OrderDays, orderReport.Steps.Single(s => s.Key == "no_window").Value);
        Assert.Equal(h.OrderDays, orderReport.Steps.Single(s => s.Key == "no_window").Drivers.Single(d => d.Name == "overdue_days").Value);

        AdvanceTo(h.WithdrawalDays - 1);
        Assert.Equal("", unit.WithdrawalCaseId);
        engine.Advance(1);
        var withdrawal = Assert.Single(engine.State.SiteWithdrawals);
        Assert.Equal((site.Id, site.PlotId, since + h.WithdrawalDays, since + h.WithdrawalDays + h.AppealDays, "harvest"),
            (withdrawal.UnitId, withdrawal.PlotId, withdrawal.OpenedDay, withdrawal.EffectiveDay, withdrawal.Requirement));
        Assert.Equal(HandlingStage.Withdrawal, unit.HandlingStage);
        var withdrawn = Assert.Single(delivered, e => e.Type == GameEventType.SiteApprovalWithdrawn);
        Assert.Equal(withdrawal.EffectiveDay, withdrawn.DeadlineDay);
        var withdrawalReport = Assert.Single(engine.IncidentReportsOf(site.Id), r => r.Kind == "site_approval_withdrawn");
        Assert.Equal(8, withdrawalReport.Steps.Single(s => s.Key == "licences").Value);
        Assert.Equal(SimulationEngine.OffshoreReasons.ApprovalWithdrawn, engine.StockOffshoreFarm(site.Id, 1_000).ErrorKey);
        Assert.Equal(SimulationEngine.OffshoreReasons.ApprovalWithdrawn,
            engine.BookOffshoreOperation(site.Id, OffshoreOp.Stocking, engine.State.Day + 2, VesselClass.Standard, 1_000).ErrorKey);

        while (engine.State.Day < withdrawal.EffectiveDay) engine.Advance(1);
        Assert.Equal(SiteWithdrawalStatus.Effective, withdrawal.Status);
        Assert.Equal(withdrawal.EffectiveDay, engine.FindPlot(site.PlotId)!.WithdrawnFromDay);
        Assert.False(engine.FindPlot(site.PlotId)!.IsFree);
        var licences = engine.LicenceHoldings.Where(l => l.AwardId == unit.AwardId).ToList();
        Assert.Equal(8, licences.Count); // never deleted
        Assert.All(licences, l => Assert.Equal((LicenceKind.Development, "", "", true), (l.Kind, l.BoundUnitId, l.PlacedZoneId, l.IsInForce(engine.State.Day))));
        Assert.Equal((8, 8 * 780.0), (withdrawal.LicencesReleased, withdrawal.ReleasedTonnes));
        Assert.Equal(0.0, engine.UnitMtbTonnes(site));
        Assert.True(site.Pens.Sum(p => p.TotalCount) > 0, "the sea at 4.0 m kept the fish in");

        engine.SeaOverrideForTests = (s, d) => 1.0;
        engine.Advance(1);
        Assert.Equal(0, site.Pens.Sum(p => p.TotalCount));
        Assert.Equal(engine.State.Day, withdrawal.ForcedHarvestDay);
        Assert.Contains(engine.HarvestReports, r => r.HoldingId == site.Id && r.Mode == HarvestMode.Forced);
        Assert.True(engine.MortalityLedger.ForSite(site.Id).Removed(FishRemoval.ForcedHarvest).Fish > 0);
        foreach (var e in delivered) _out.WriteLine($"{e.Day} {e.Type} deadline {e.DeadlineDay} escalation {e.IsEscalation}");
    }

    /// <summary>
    /// An appeal is decided on the withdrawal's effective day on the handling
    /// done by then: the harvest is completed in the calm that follows, so the
    /// unit stands under no requirement and the withdrawal is lifted; the plot
    /// keeps its approval and the licences stay bound. A case can be appealed once.
    /// </summary>
    [Fact]
    public void AnAppeal_LiftsTheWithdrawal_WhenTheHandlingIsDoneByItsDay()
    {
        var (engine, site) = LicensedUnit(102);
        var unit = site.Offshore!;
        engine.Advance(30);
        engine.SeaOverrideForTests = (s, d) => 4.0;
        Assert.True(engine.OrderHarvest(site.Id).Ok);
        while (unit.WithdrawalCaseId.Length == 0) engine.Advance(1);
        Assert.Equal(SimulationEngine.OffshoreReasons.NotFound, engine.AppealSiteWithdrawal("nope").ErrorKey);
        Assert.True(engine.AppealSiteWithdrawal(site.Id).Ok);
        Assert.Equal(SimulationEngine.OffshoreReasons.NotAppealable, engine.AppealSiteWithdrawal(site.Id).ErrorKey);
        var withdrawal = engine.WithdrawalCaseOf(unit)!;
        Assert.Equal(SiteWithdrawalStatus.Appealed, withdrawal.Status);
        engine.SeaOverrideForTests = (s, d) => 1.0;
        Assert.True(engine.SetDriftsplan(site.Id, OffshorePolicies.NetInspection).Ok); // the nets inspected again in the calm
        while (engine.State.Day < withdrawal.EffectiveDay) engine.Advance(1);
        Assert.Equal(HandlingRequirement.None, unit.Requirement);
        Assert.Equal(SiteWithdrawalStatus.Lifted, withdrawal.Status);
        Assert.Equal(-1, engine.FindPlot(site.PlotId)!.WithdrawnFromDay);
        Assert.Equal("", unit.WithdrawalCaseId);
        Assert.Equal(8, engine.LicenceHoldings.Count(l => l.BoundUnitId == site.Id));
        Assert.True(engine.StockOffshoreFarm(site.Id, 1_000).Ok);
    }

    /// <summary>
    /// The clock counts days without a completed handling: a delousing done in
    /// a window restarts it and stands the warning down (its pacing deadline
    /// resolved); the requirement closes when the count is under the limit again.
    /// </summary>
    [Fact]
    public void ACompletedHandling_RestartsTheClock_AndStandsTheWarningDown()
    {
        var (engine, site) = LicensedUnit(103);
        var unit = site.Offshore!;
        engine.Advance(150); // June: the weekly count runs (no counting below 4 °C)
        engine.SeaOverrideForTests = (s, d) => 4.0;
        site.Lice.AdultFemalePerFish = 5.0;
        site.ConsecutiveLiceBreaches = 2;
        engine.Advance(1);
        Assert.Equal(HandlingRequirement.Delouse, unit.Requirement);
        while (unit.HandlingStage != HandlingStage.Warned) engine.Advance(1);
        string key = GameEventBus.KeyOf(GameEventCatalog.Describe(GameEventType.HandlingOverdue).Category, site.Id, "");
        Assert.True(engine.Pacing.IsWatching(key));
        var booked = engine.BookOffshoreOperation(site.Id, OffshoreOp.Delousing, engine.State.Day + 1, VesselClass.Standard, detail: "hydrolicer");
        Assert.True(booked.Ok, booked.Message);
        int treated = engine.State.Day + 1;
        engine.SeaOverrideForTests = (s, d) => d == treated ? 1.5 : 4.0;
        engine.Advance(1);
        Assert.Equal(treated, unit.LastHandlingDoneDay);
        Assert.Equal((HandlingStage.None, 0), (unit.HandlingStage, unit.HandlingOverdueDays));
        Assert.False(engine.Pacing.IsWatching(key));
        engine.SeaOverrideForTests = (s, d) => 1.0;
        site.Lice.AdultFemalePerFish = 0.05; // the treatment worked
        engine.Advance(8); // the next weekly count finds the fish under the limit
        Assert.Equal(HandlingRequirement.None, unit.Requirement);
        Assert.Equal(-1, unit.RequirementSinceDay);
    }

    /// <summary>
    /// The requirement's sources (spec §3.3): lice over the limit at two counts
    /// (delousing), the projected biomass over 97 % of the unit's MTB within 14
    /// days (MTB relief), a harvest order (harvest), the nets' inspection 30
    /// days past 90 (nets); none for a sound unit.
    /// </summary>
    [Fact]
    public void TheRequirement_ComesFromLiceMtbHarvestAndTheNets()
    {
        var (engine, site) = LicensedUnit(104);
        var unit = site.Offshore!;
        int day = engine.Weather.SimulationDay;
        Assert.Equal(HandlingRequirement.None, engine.CurrentRequirement(site, unit, day));
        site.ConsecutiveLiceBreaches = 2;
        Assert.Equal(HandlingRequirement.Delouse, engine.CurrentRequirement(site, unit, day));
        site.ConsecutiveLiceBreaches = 0;
        double mtb = engine.UnitMtbTonnes(site);
        Assert.Equal(8 * 780.0, mtb);
        double now = site.Pens.Sum(p => p.GetTotalBiomassKg()) / 1000.0;
        double inTwoWeeks = engine.ProjectedUnitBiomassTonnes(site, Cal.Handling.MtbProjectionDays);
        Assert.True(inTwoWeeks > now);
        foreach (var c in site.Pens.SelectMany(p => p.Cohorts)) c.MeanWeightKg *= 0.98 * mtb / now; // standing at 98 % already
        Assert.True(engine.ProjectedUnitBiomassTonnes(site, Cal.Handling.MtbProjectionDays) > Cal.Handling.MtbReliefShare * mtb);
        Assert.Equal(HandlingRequirement.MtbRelief, engine.CurrentRequirement(site, unit, day));
        foreach (var c in site.Pens.SelectMany(p => p.Cohorts)) c.MeanWeightKg *= 0.5;
        Assert.Equal(HandlingRequirement.None, engine.CurrentRequirement(site, unit, day));
        Assert.True(engine.OrderHarvest(site.Id).Ok);
        Assert.Equal(HandlingRequirement.Harvest, engine.CurrentRequirement(site, unit, day));
        Assert.True(engine.CancelHarvestOrder(engine.HarvestOrders.Single().Id).Ok);
        unit.NetKnownAsOfDay = day - Cal.Handling.NetInspectionIntervalDays - Cal.Handling.NetInspectionOverdueDays;
        Assert.Equal(HandlingRequirement.None, engine.CurrentRequirement(site, unit, day));
        unit.NetKnownAsOfDay--;
        Assert.Equal(HandlingRequirement.NetInspection, engine.CurrentRequirement(site, unit, day));
    }

    /// <summary>
    /// At a unit the coastal lice order (treat within 14 days or Mattilsynet
    /// treats) follows the offshore escalation: its due day becomes the
    /// withdrawal day, so the census never force-treats fish the sea keeps out
    /// of reach (no forced delousing in 74 days at 4.0 m), and its own pacing
    /// deadline is resolved in favour of the escalation's.
    /// </summary>
    [Fact]
    public void AUnitsLiceOrder_FollowsTheEscalation_NotTheCoastalFourteenDays()
    {
        var (engine, site) = LicensedUnit(105);
        var unit = site.Offshore!;
        engine.Advance(10);
        engine.SeaOverrideForTests = (s, d) => 4.0;
        for (int d = 0; d < 74; d++)
        {
            site.Lice.AdultFemalePerFish = Math.Max(site.Lice.AdultFemalePerFish, 4.0);
            engine.Advance(1);
        }
        Assert.Equal(HandlingRequirement.Delouse, unit.Requirement);
        Assert.Equal(unit.HandlingAnchorDay + Cal.Handling.WithdrawalDays, site.TreatmentOrderDueDay);
        Assert.DoesNotContain(engine.State.EventLog, e => e.Name == "Forced delousing");
        Assert.Equal(0L, engine.MortalityLedger.ForSite(site.Id).Deaths(MortalityCause.Delousing).Fish);
        Assert.False(engine.Pacing.IsWatching(SimulationEngine.StandingOrderKey(GameEventType.LiceTreatmentOrder, site.Id)));
    }

    /// <summary>
    /// "Behandle ved krav" (off by default): on a delousing requirement it books
    /// the first green window with the chosen vessel (an offshore-class
    /// wellboat when asked), on MTB relief it orders the heaviest zone's
    /// harvest, and it waits while a harvest order stands. It never draws.
    /// </summary>
    [Fact]
    public void TheHandlingPolicy_BooksWhatAnswersTheRequirement()
    {
        var (engine, site) = LicensedUnit(106);
        var unit = site.Offshore!;
        Assert.DoesNotContain(engine.GetDriftsplan(site.Id), e => e.Policy == OffshorePolicies.Handling);
        var set = engine.SetDriftsplan(site.Id, OffshorePolicies.Handling, null,
            new Dictionary<string, string> { [HandlingPolicy.HandlingVessel.Key] = OffshorePolicies.VesselOffshoreWellboat });
        Assert.True(set.Ok, set.Message);
        engine.Advance(150); // June water: a delousing can run
        Assert.Empty(unit.Bookings.Where(b => b.Op == OffshoreOp.Delousing));
        site.Lice.AdultFemalePerFish = 5.0;
        site.ConsecutiveLiceBreaches = 2;
        engine.Advance(2);
        var booking = Assert.Single(unit.Bookings, b => b.Op == OffshoreOp.Delousing);
        Assert.Equal(VesselClass.OffshoreWellboat, booking.Vessel);

        var (engine2, site2) = LicensedUnit(107);
        Assert.True(engine2.SetDriftsplan(site2.Id, OffshorePolicies.Handling, null, null).Ok);
        double mtb = engine2.UnitMtbTonnes(site2);
        var heaviest = site2.PhysicalPens.Single(p => p.Number == 2);
        foreach (var c in site2.PopulationIn(heaviest)!.Cohorts) c.MeanWeightKg *= 1.5; // zone 2 clearly the heaviest
        double now2 = site2.Pens.Sum(p => p.GetTotalBiomassKg()) / 1000.0;
        foreach (var c in site2.Pens.SelectMany(p => p.Cohorts)) c.MeanWeightKg *= 0.98 * mtb / now2;
        engine2.Advance(2);
        var order = Assert.Single(engine2.HarvestOrders, o => o.HoldingId == site2.Id);
        Assert.Equal(new[] { site2.PopulationIn(heaviest)!.Id }, order.Pens.Select(p => p.PopulationId));
        engine2.Advance(1);
        Assert.Single(engine2.HarvestOrders, o => o.HoldingId == site2.Id); // waits for its wellboat, no second order
    }

    /// <summary>
    /// A booked delousing treats a day's capacity of fish (2,000 t) on each
    /// window day, zone by zone: a unit of about 4,500 t takes three window
    /// days, the booking in progress between them and chartered every day.
    /// </summary>
    [Fact]
    public void DelousingALargeUnit_TakesAWindowDayPerDaysCapacity()
    {
        var (engine, site) = LicensedUnit(108);
        engine.Advance(150); // June water: the treatment's temperature range
        foreach (var c in site.Pens.SelectMany(p => p.Cohorts)) c.MeanWeightKg = 5.0; // 3 zones × about 300,000 × 5 kg
        int first = engine.State.Day + 1;
        var windows = new HashSet<int> { first, first + 2, first + 4 };
        engine.SeaOverrideForTests = (s, d) => windows.Contains(d) ? 1.5 : 3.0;
        var booked = engine.BookOffshoreOperation(site.Id, OffshoreOp.Delousing, first, VesselClass.Standard, detail: "hydrolicer");
        Assert.True(booked.Ok, booked.Message);
        var booking = site.Offshore!.Bookings.Single(b => b.Id == booked.Id);
        var statuses = new List<OffshoreBookingStatus>();
        for (int d = 0; d < 5; d++)
        {
            engine.Advance(1);
            statuses.Add(booking.Status);
        }
        _out.WriteLine(string.Join(", ", statuses));
        Assert.Equal(OffshoreBookingStatus.InProgress, statuses[0]);
        Assert.Equal(OffshoreBookingStatus.Done, statuses[4]);
        Assert.Equal(first + 4, booking.CompletedDay);
        Assert.Equal(site.PhysicalPens.Select(p => p.Id).OrderBy(x => x), booking.TreatedZones.OrderBy(x => x));
        Assert.Equal(5, booking.DaysCharged);
    }
}
