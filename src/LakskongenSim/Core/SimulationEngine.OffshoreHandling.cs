using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LakskongenSim.Environment;
using LakskongenSim.Events;
using LakskongenSim.Farm;
using LakskongenSim.Farm.Offshore;
using LakskongenSim.Finance;
using LakskongenSim.Harvest;

namespace LakskongenSim.Core;

/// <summary>
/// The AOF lesson (roadmap F2-08, docs/FARM_TYPES_SPEC.md §3.3 "Handling
/// requirement and overdue days", §6.6 "Handling overdue at 21 (warning), 45
/// (Mattilsynet order, critical), 75 (withdrawal case)"; V:5): an offshore
/// unit must be able to handle its fish when they need it. A requirement is
/// raised when the lice are over the limit at two counts in a row (delousing),
/// the projected biomass reaches 97 % of the unit's MTB within 14 days or a
/// harvest is ordered (harvest / MTB relief), or the nets' inspection is 30
/// days past the regulator's 90. Each day the requirement stands without a
/// completed handling operation counts: at 21 days the warning, at 45 the
/// order to act within 30 days, at 75 a site-withdrawal case. The withdrawal
/// takes effect after 120 days unless an appeal lifts it (decided on the
/// handling done by then): the plot's approval is lost, any fish left are
/// harvested by order at the first sea that allows it, and the licences
/// behind the unit stay with the company, released and unplaced (never
/// deleted). At a unit the coastal 14-day lice order follows this
/// escalation: Mattilsynet cannot treat fish it cannot reach either.
/// </summary>
public partial class SimulationEngine
{
    /// <summary>Snake_case key of a requirement (bridge, reports, withdrawal cases).</summary>
    public static string RequirementKey(HandlingRequirement requirement) => requirement switch
    {
        HandlingRequirement.Delouse => "delouse",
        HandlingRequirement.Harvest => "harvest",
        HandlingRequirement.MtbRelief => "mtb_relief",
        HandlingRequirement.NetInspection => "net_inspection",
        _ => "none",
    };

    /// <summary>The operation that answers a requirement (the board's row: fish handling, or the ROV for the nets).</summary>
    internal static OffshoreOp OperationFor(HandlingRequirement requirement) => requirement switch
    {
        HandlingRequirement.Delouse => OffshoreOp.Delousing,
        HandlingRequirement.NetInspection => OffshoreOp.NetInspection,
        _ => OffshoreOp.Harvest,
    };

    // ─── The day (called from RunOffshoreUnitDay after the incidents) ──────

    /// <summary>
    /// The unit's handling day: the withdrawal case's day, then the
    /// requirement it stands under, the overdue clock (reset by a completed
    /// handling), the days without a handling window, the escalation and the
    /// lice order kept to it.
    /// </summary>
    private void RunOffshoreHandlingDay(SeaSite site, OffshoreUnit unit, int seaDay)
    {
        AdvanceWithdrawalCase(site, unit, seaDay);
        var requirement = CurrentRequirement(site, unit, seaDay);
        if (requirement == HandlingRequirement.None)
        {
            ClearRequirement(site, unit);
            return;
        }
        if (unit.Requirement == HandlingRequirement.None || unit.RequirementSinceDay < 0)
        {
            unit.RequirementSinceDay = seaDay;
            unit.HandlingAnchorDay = seaDay;
            unit.DaysWithoutHandlingWindow = 0;
        }
        unit.Requirement = requirement;

        int done = LastHandlingDay(site, unit, requirement);
        if (done > unit.LastHandlingDoneDay) unit.LastHandlingDoneDay = done;
        if (done > unit.HandlingAnchorDay)
        {
            // A completed handling restarts the clock (spec §3.3: "consecutive days ... no completed handling operation").
            unit.HandlingAnchorDay = done;
            unit.DaysWithoutHandlingWindow = 0;
            StandDownEscalation(site, unit);
        }
        if (seaDay > unit.HandlingAnchorDay && !OffshoreOperationPossibleToday(site, OperationFor(requirement), VesselClass.Standard))
            unit.DaysWithoutHandlingWindow++; // the days the clock counts (after its anchor) on which the sea allowed no handling
        unit.HandlingOverdueDays = Math.Max(0, seaDay - unit.HandlingAnchorDay);
        Escalate(site, unit, seaDay);
        KeepLiceOrderToTheEscalation(site, unit);
    }

    /// <summary>
    /// The requirement a unit stands under today (one at a time, in this
    /// order): delousing (lice over the limit at two counts in a row), MTB
    /// relief (projected biomass within 14 days over 97 % of the unit's MTB),
    /// harvest (an S2-04 order waits at the unit), the nets' inspection
    /// (overdue by 30 days past 90). None while the unit holds no fish but for the nets.
    /// </summary>
    internal HandlingRequirement CurrentRequirement(SeaSite site, OffshoreUnit unit, int seaDay)
    {
        var h = OffshoreCal.Handling;
        bool hasFish = site.Pens.Any(p => p.TotalCount > 0);
        if (hasFish && site.ConsecutiveLiceBreaches >= 2) return HandlingRequirement.Delouse;
        if (hasFish)
        {
            double mtb = UnitMtbTonnes(site);
            if (mtb > 0.0 && ProjectedUnitBiomassTonnes(site, h.MtbProjectionDays) > h.MtbReliefShare * mtb)
                return HandlingRequirement.MtbRelief;
            if (HarvestOrders.Any(o => o.HoldingId == site.Id)) return HandlingRequirement.Harvest;
        }
        if (seaDay - unit.NetKnownAsOfDay > h.NetInspectionIntervalDays + h.NetInspectionOverdueDays) return HandlingRequirement.NetInspection;
        return HandlingRequirement.None;
    }

    /// <summary>
    /// The unit's standing biomass in <paramref name="days"/> days on the
    /// farmer's plan (S2-01a's projection: the planning TGC on the area's
    /// climatology, no losses, no harvest), in tonnes. Pure.
    /// </summary>
    internal double ProjectedUnitBiomassTonnes(SeaSite site, int days)
    {
        int area = ProductionAreaOfSite(site);
        var cohorts = new List<PlannedCohort>();
        foreach (var pop in site.Pens)
            foreach (var cohort in pop.Cohorts)
                if (cohort.Count > 0)
                    cohorts.Add(new PlannedCohort(site.Id, area, cohort.Count, cohort.MeanWeightKg));
        if (cohorts.Count == 0) return 0.0;
        var plan = new CapacityPlanSettings(_calibration.Capacity.PlanningThermalGrowthCoefficient, double.MaxValue / 4, 1.0, Math.Max(0, days));
        var projection = CapacityPlanner.Project(cohorts, State.Day, plan, PlanTemperatureC);
        return projection.PeakTonnes;
    }

    /// <summary>
    /// The last day the requirement's handling was done at the unit: fish
    /// handling (a delousing of any of its zones, a harvest batch from it) for
    /// a delousing, harvest or MTB relief; an inspection of the nets for the
    /// nets. -1 when never.
    /// </summary>
    private int LastHandlingDay(SeaSite site, OffshoreUnit unit, HandlingRequirement requirement)
    {
        if (requirement == HandlingRequirement.NetInspection) return unit.NetKnownAsOfDay;
        int seaDay = Weather.SimulationDay;
        int last = -1;
        foreach (var pop in site.Pens)
        {
            int since = pop.TreatmentHarm.DaysSinceLastTreatment;
            if (since < 10_000) last = Math.Max(last, seaDay - since);
        }
        foreach (var report in HarvestReports)
            if (report.HoldingId == site.Id && report.Day > last)
                last = report.Day;
        return last;
    }

    /// <summary>The escalation of a requirement on its overdue days (21 warning, 45 order, 75 withdrawal case).</summary>
    private void Escalate(SeaSite site, OffshoreUnit unit, int seaDay)
    {
        var h = OffshoreCal.Handling;
        int overdue = unit.HandlingOverdueDays;
        if (unit.HandlingStage < HandlingStage.Withdrawal && overdue >= h.WithdrawalDays && unit.WithdrawalCaseId.Length == 0)
        {
            OpenWithdrawalCase(site, unit, seaDay);
            return;
        }
        if (unit.HandlingStage < HandlingStage.Ordered && overdue >= h.OrderDays)
        {
            unit.HandlingStage = HandlingStage.Ordered;
            unit.OrderDay = seaDay;
            State.EventLog.Add(new EventRecord
            {
                Day = State.Day,
                Name = "Pålegg fra Mattilsynet",
                Category = EventCategory.Regulation,
                Description = SimText.Nb($"Mattilsynet pålegger {site.Label} å {RequirementText(unit.Requirement)} innen dag {unit.HandlingAnchorDay + h.WithdrawalDays}: kravet har stått i {overdue} døgn uten behandling"),
            });
            State.TrimEventLog();
            WriteHandlingReport(site, unit, "mattilsynet_order", seaDay);
        }
        else if (unit.HandlingStage < HandlingStage.Warned && overdue >= h.WarnDays)
        {
            unit.HandlingStage = HandlingStage.Warned;
        }
        PublishHandlingCondition(site, unit);
    }

    /// <summary>The escalation's standing event on the bus while it stands (the bus delivers a run once; the order escalates the warning).</summary>
    private void PublishHandlingCondition(SeaSite site, OffshoreUnit unit)
    {
        var h = OffshoreCal.Handling;
        switch (unit.HandlingStage)
        {
            case HandlingStage.Warned:
                PublishGameEvent(new GameEventRequest(GameEventType.HandlingOverdue, State.Day)
                {
                    SiteId = site.Id,
                    UnitId = site.Id,
                    DeadlineDay = unit.HandlingAnchorDay + h.OrderDays,
                    Value = unit.HandlingOverdueDays,
                    Message = SimText.Nb($"{site.Label} må {RequirementText(unit.Requirement)}: {unit.HandlingOverdueDays} døgn uten behandling, Mattilsynet gir pålegg dag {unit.HandlingAnchorDay + h.OrderDays}"),
                });
                break;
            case HandlingStage.Ordered:
                PublishGameEvent(new GameEventRequest(GameEventType.MattilsynetOrder, State.Day)
                {
                    SiteId = site.Id,
                    UnitId = site.Id,
                    DeadlineDay = unit.HandlingAnchorDay + h.WithdrawalDays,
                    Value = unit.HandlingOverdueDays,
                    Message = SimText.Nb($"Pålegg: {site.Label} må {RequirementText(unit.Requirement)} innen dag {unit.HandlingAnchorDay + h.WithdrawalDays}, ellers trekkes lokalitetsgodkjenningen"),
                });
                break;
        }
    }

    /// <summary>A completed handling or a closed requirement stands the warning and the order down (the withdrawal case, once open, runs its course).</summary>
    private void StandDownEscalation(SeaSite site, OffshoreUnit unit)
    {
        if (unit.HandlingStage is HandlingStage.Warned or HandlingStage.Ordered)
        {
            Pacing.Resolve(HandlingPacingKey(site));
            unit.HandlingStage = HandlingStage.None;
            unit.OrderDay = -1;
        }
    }

    private void ClearRequirement(SeaSite site, OffshoreUnit unit)
    {
        if (unit.Requirement == HandlingRequirement.None && unit.RequirementSinceDay < 0) return;
        StandDownEscalation(site, unit);
        unit.Requirement = HandlingRequirement.None;
        unit.RequirementSinceDay = -1;
        unit.HandlingAnchorDay = -1;
        unit.HandlingOverdueDays = 0;
        unit.DaysWithoutHandlingWindow = 0;
    }

    /// <summary>The pacing key of the unit's handling events (the bus key of HandlingOverdue / MattilsynetOrder).</summary>
    private static string HandlingPacingKey(SeaSite site)
        => GameEventBus.KeyOf(GameEventCatalog.Describe(GameEventType.HandlingOverdue).Category, site.Id, "");

    /// <summary>
    /// At a unit the coastal lice order (two counts over the limit: treat
    /// within 14 days or Mattilsynet treats) follows the offshore escalation:
    /// its deadline is the order's (the withdrawal day), then the withdrawal's
    /// effective day, so the census never treats fish the sea keeps out of
    /// reach; its own pacing deadline is the escalation's.
    /// </summary>
    private void KeepLiceOrderToTheEscalation(SeaSite site, OffshoreUnit unit)
    {
        if (site.TreatmentOrderDueDay < 0 || unit.Requirement != HandlingRequirement.Delouse) return;
        int due = unit.HandlingAnchorDay + OffshoreCal.Handling.WithdrawalDays;
        var withdrawal = WithdrawalCaseOf(unit);
        if (withdrawal != null) due = Math.Max(due, withdrawal.EffectiveDay);
        if (site.TreatmentOrderDueDay < due)
        {
            site.TreatmentOrderDueDay = due;
            Pacing.Resolve(StandingOrderKey(GameEventType.LiceTreatmentOrder, site.Id));
        }
    }

    internal static string RequirementText(HandlingRequirement requirement) => requirement switch
    {
        HandlingRequirement.Delouse => "avluse fisken",
        HandlingRequirement.MtbRelief => "slakte ned under MTB",
        HandlingRequirement.Harvest => "slakte fisken som er bestilt",
        HandlingRequirement.NetInspection => "inspisere nota",
        _ => "håndtere fisken",
    };

    // ─── The site withdrawal ───────────────────────────────────────────────

    /// <summary>The unit's open or effective withdrawal case, or null.</summary>
    public SiteWithdrawalCase? WithdrawalCaseOf(OffshoreUnit unit)
        => unit.WithdrawalCaseId.Length == 0 ? null : State?.SiteWithdrawals?.FirstOrDefault(c => c.Id == unit.WithdrawalCaseId);

    private void OpenWithdrawalCase(SeaSite site, OffshoreUnit unit, int seaDay)
    {
        var h = OffshoreCal.Handling;
        var withdrawal = new SiteWithdrawalCase
        {
            Id = $"sw_{State.NextSiteWithdrawalNumber++}",
            UnitId = site.Id,
            PlotId = site.PlotId,
            OpenedDay = seaDay,
            EffectiveDay = seaDay + h.AppealDays,
            Requirement = RequirementKey(unit.Requirement),
        };
        State.SiteWithdrawals.Add(withdrawal);
        unit.WithdrawalCaseId = withdrawal.Id;
        unit.HandlingStage = HandlingStage.Withdrawal;
        Pacing.Resolve(HandlingPacingKey(site));
        State.EventLog.Add(new EventRecord
        {
            Day = State.Day,
            Name = "Lokalitetsgodkjenning trukket",
            Category = EventCategory.Regulation,
            Description = SimText.Nb($"Mattilsynet trekker godkjenningen av {site.Label} fra dag {withdrawal.EffectiveDay}: {unit.HandlingOverdueDays} døgn uten å {RequirementText(unit.Requirement)}. Fisken må ut innen fristen; klage avgjøres samme dag."),
        });
        State.TrimEventLog();
        WriteHandlingReport(site, unit, "site_approval_withdrawn", seaDay);
        PublishWithdrawal(site, withdrawal);
    }

    private void PublishWithdrawal(SeaSite site, SiteWithdrawalCase withdrawal)
    {
        PublishGameEvent(new GameEventRequest(GameEventType.SiteApprovalWithdrawn, State.Day)
        {
            SiteId = site.Id,
            UnitId = site.Id,
            DeadlineDay = withdrawal.EffectiveDay,
            Message = withdrawal.Status == SiteWithdrawalStatus.Appealed
                ? SimText.Nb($"Klagen på tilbaketrekkingen av {site.Label} avgjøres dag {withdrawal.EffectiveDay}: gjør behandlingen ferdig før det")
                : SimText.Nb($"Lokalitetsgodkjenningen for {site.Label} trekkes tilbake dag {withdrawal.EffectiveDay}: slakt eller flytt fisken, eller klag"),
        });
    }

    /// <summary>
    /// The withdrawal case's day: while pending it stands on the bus; on its
    /// effective day an appeal is decided (lifted when the unit stands under no
    /// requirement then) or the withdrawal takes effect; once effective, any
    /// fish left are harvested by order at the first day the sea lets the
    /// offshore wellboat handle them.
    /// </summary>
    private void AdvanceWithdrawalCase(SeaSite site, OffshoreUnit unit, int seaDay)
    {
        var withdrawal = WithdrawalCaseOf(unit);
        if (withdrawal == null) return;
        if (withdrawal.IsPending)
        {
            if (seaDay < withdrawal.EffectiveDay)
            {
                PublishWithdrawal(site, withdrawal);
                return;
            }
            bool lifted = withdrawal.Status == SiteWithdrawalStatus.Appealed && CurrentRequirement(site, unit, seaDay) == HandlingRequirement.None;
            withdrawal.ClosedDay = seaDay;
            Pacing.Resolve(GameEventBus.KeyOf(GameEventCatalog.Describe(GameEventType.SiteApprovalWithdrawn).Category, site.Id, ""));
            if (lifted)
            {
                withdrawal.Status = SiteWithdrawalStatus.Lifted;
                unit.WithdrawalCaseId = "";
                unit.HandlingStage = HandlingStage.None;
                PublishGameEvent(new GameEventRequest(GameEventType.PermitDecision, State.Day)
                {
                    SiteId = site.Id,
                    UnitId = site.Id,
                    Variant = withdrawal.Id,
                    Message = SimText.Nb($"Klagen ble tatt til følge: {site.Label} beholder lokalitetsgodkjenningen"),
                });
                State.EventLog.Add(new EventRecord
                {
                    Day = State.Day,
                    Name = "Klage tatt til følge",
                    Category = EventCategory.Regulation,
                    Description = SimText.Nb($"Mattilsynet opphever tilbaketrekkingen av {site.Label}: behandlingen ble gjort innen klagen ble avgjort"),
                });
                State.TrimEventLog();
                return;
            }
            withdrawal.Status = SiteWithdrawalStatus.Effective;
            var plot = FindPlot(site.PlotId);
            if (plot != null) plot.WithdrawnFromDay = seaDay;
            var released = ReleaseUnitLicences(site);
            withdrawal.LicencesReleased = released.Count;
            withdrawal.ReleasedTonnes = released.Sum(l => l.MtbTonnes);
            State.EventLog.Add(new EventRecord
            {
                Day = State.Day,
                Name = "Lokalitetsgodkjenning tapt",
                Category = EventCategory.Regulation,
                Description = SimText.Nb($"{site.Label} har mistet lokalitetsgodkjenningen. {released.Count} tillatelser ({withdrawal.ReleasedTonnes:N0} t MTB) beholdes, men må plasseres på nytt."),
            });
            State.TrimEventLog();
        }
        if (withdrawal.Status == SiteWithdrawalStatus.Effective && withdrawal.ForcedHarvestDay < 0 && site.Pens.Any(p => p.TotalCount > 0)
            && OperationPossible(site, OffshoreHooks.HarvestOrderJobKind, State.Day))
        {
            withdrawal.ForcedHarvestDay = seaDay;
            ForceHarvestWholeUnit(site);
        }
    }

    /// <summary>
    /// Appeal a site withdrawal (spec §3.3: "an appeal takes 120 d"): decided on
    /// its effective day, lifted when the unit then stands under no
    /// requirement. Fails: not_found, not_appealable (no pending, unappealed case).
    /// </summary>
    public CommandResult AppealSiteWithdrawal(string siteId)
    {
        var site = FindOffshoreUnit(siteId);
        var unit = site?.Offshore;
        if (site == null || unit == null)
            return CommandResult.Fail(OffshoreReasons.NotFound, $"No offshore unit '{siteId}'");
        var withdrawal = WithdrawalCaseOf(unit);
        if (withdrawal == null || withdrawal.Status != SiteWithdrawalStatus.Decided)
            return CommandResult.Fail(OffshoreReasons.NotAppealable, $"{site.Label} has no withdrawal to appeal");
        withdrawal.Status = SiteWithdrawalStatus.Appealed;
        withdrawal.AppealedDay = State.Day;
        return CommandResult.Success($"{site.Label}: the withdrawal is appealed, decided on day {withdrawal.EffectiveDay}",
            withdrawal.EffectiveDay, id: withdrawal.Id);
    }

    /// <summary>
    /// The day's fish at a unit whose approval was withdrawn leave by order:
    /// every fish through the S2-04 pipeline as a forced harvest (the
    /// capacity rules' penalty price, the express surcharge), booked in the
    /// mortality ledger as a forced harvest.
    /// </summary>
    private void ForceHarvestWholeUnit(SeaSite site)
    {
        var lots = new Dictionary<string, (HarvestHolding Holding, HarvestLot Lot)>(StringComparer.Ordinal);
        double kg = site.Pens.Sum(p => p.GetTotalBiomassKg());
        double removed = TakeHeaviestFish(site, kg + 1.0, lots);
        if (!lots.TryGetValue(site.Id, out var entry) || entry.Lot.Fish <= 0) return;
        var report = SellLot(entry.Lot, entry.Holding, HarvestMode.Forced, orderId: "",
            (decimal)_calibration.Capacity.ForcedHarvestPriceFactor, "forced_harvest");
        RemoveEmptyPens(entry.Holding);
        State.EventLog.Add(new EventRecord
        {
            Day = State.Day,
            Name = "Tvangsslakting (lokalitet trukket)",
            Category = EventCategory.Regulation,
            Description = SimText.Nb($"{site.Label}: {removed / 1000.0:F1} t ble slaktet etter pålegg til {_calibration.Capacity.ForcedHarvestPriceFactor:P0} av prisen ({report.GrossRevenueNok:N0} kr brutto)"),
            BalanceChange = report.NetRevenueNok,
        });
        State.TrimEventLog();
    }

    /// <summary>
    /// A withdrawal releases the licences behind the unit: they stay in the
    /// register, bound to no unit, a development licence still a development
    /// licence (to be built on again or converted), a placed licence still
    /// placed. Never deleted (spec §3.3, V:2). Returns the licences released.
    /// </summary>
    private List<LicenceHolding> ReleaseUnitLicences(SeaSite site)
    {
        var released = State.LicenceHoldings.Where(l => l.BoundUnitId == site.Id).ToList();
        foreach (var licence in released)
            licence.BoundUnitId = "";
        return released;
    }

    /// <summary>
    /// Refusal of new fish at a unit whose approval is being or was withdrawn
    /// (approval_withdrawn), or whose licences do not cover a stocking
    /// (licence_expired, licences_unplaced; F2-09); null when it may be stocked.
    /// </summary>
    public CommandResult? OffshoreStockingRefusal(SeaSite site)
    {
        var unit = site.Offshore;
        if (unit == null) return null;
        if (WithdrawalCaseOf(unit) is { Status: not SiteWithdrawalStatus.Lifted })
            return CommandResult.Fail(OffshoreReasons.ApprovalWithdrawn, $"{site.Label}'s site approval is withdrawn");
        return OffshoreLicenceStockingRefusal(site);
    }

    // ─── The regulator's reports ───────────────────────────────────────────

    /// <summary>
    /// The report of a Mattilsynet order or a site withdrawal (spec §6.6): the
    /// requirement and when it was raised, the days without a handling window,
    /// what was booked for it, the escalation so far, and for a withdrawal the
    /// licences released.
    /// </summary>
    private void WriteHandlingReport(SeaSite site, OffshoreUnit unit, string kind, int seaDay)
    {
        var h = OffshoreCal.Handling;
        var op = OperationFor(unit.Requirement);
        double limit = OperabilityWindows.HsLimitM(op, VesselClass.Standard, OffshoreCal.Operability) ?? 0.0;
        var steps = new List<IncidentStep>
        {
            new()
            {
                Key = "requirement", Day = unit.RequirementSinceDay, Value = unit.RequirementSinceDay,
                Text = SimText.Nb($"Krav fra dag {unit.RequirementSinceDay}: {RequirementText(unit.Requirement)} ({RequirementCause(site, unit)})"),
                RecordRef = $"requirement:{site.Id}:{unit.RequirementSinceDay.ToString(CultureInfo.InvariantCulture)}",
            },
            new()
            {
                Key = "no_window", Day = unit.HandlingAnchorDay, Value = unit.DaysWithoutHandlingWindow,
                Drivers = new List<OffshoreDriver>
                {
                    new("overdue_days", unit.HandlingOverdueDays),
                    new("days_without_window", unit.DaysWithoutHandlingWindow),
                    new("handling_limit_hs_m", limit),
                },
                Text = SimText.Nb($"{unit.DaysWithoutHandlingWindow} av {unit.HandlingOverdueDays} døgn siden dag {unit.HandlingAnchorDay} hadde sjø over {limit:F1} m for {RequirementText(unit.Requirement)}"),
                RecordRef = $"handling:{site.Id}:{unit.HandlingAnchorDay.ToString(CultureInfo.InvariantCulture)}",
            },
            BookingStep(site, unit, op),
            new()
            {
                Key = "escalation", Day = seaDay, Value = unit.HandlingOverdueDays,
                Drivers = new List<OffshoreDriver> { new("warn_days", h.WarnDays), new("order_days", h.OrderDays), new("withdrawal_days", h.WithdrawalDays) },
                Text = kind == "mattilsynet_order"
                    ? SimText.Nb($"Varsel etter {h.WarnDays} døgn, pålegg etter {h.OrderDays} døgn: frist dag {unit.HandlingAnchorDay + h.WithdrawalDays}")
                    : SimText.Nb($"Pålegg dag {unit.OrderDay} ble ikke fulgt: etter {h.WithdrawalDays} døgn trekkes godkjenningen fra dag {seaDay + h.AppealDays}"),
                RecordRef = $"handling:{site.Id}:{unit.HandlingAnchorDay.ToString(CultureInfo.InvariantCulture)}",
            },
        };
        if (kind == "site_approval_withdrawn")
        {
            int licences = State.LicenceHoldings.Count(l => l.BoundUnitId == site.Id);
            steps.Add(new IncidentStep
            {
                Key = "licences", Day = seaDay + h.AppealDays, Value = licences,
                Text = SimText.Nb($"{licences} tillatelser bundet til anlegget beholdes som uplassert MTB når godkjenningen faller bort"),
                RecordRef = "withdrawal:" + unit.WithdrawalCaseId,
            });
        }
        AddReport(site, kind, "", steps, 0, 0m, kind == "mattilsynet_order" ? "book_operation" : "appeal");
    }

    private IncidentStep BookingStep(SeaSite site, OffshoreUnit unit, OffshoreOp op)
    {
        var bookings = unit.Bookings.Where(b => b.Op == op && b.BookedDay >= unit.HandlingAnchorDay - 1).ToList();
        if (op == OffshoreOp.Harvest)
        {
            var order = HarvestOrders.FirstOrDefault(o => o.HoldingId == site.Id);
            return new IncidentStep
            {
                Key = "booking", Day = order?.SlotDay ?? -1, Value = order == null ? 0 : 1,
                Text = order == null
                    ? SimText.Nb($"Ingen slakteordre var lagt inn")
                    : SimText.Nb($"Slakteordren med slott dag {order.SlotDay} ventet på et vindu for brønnbåten"),
                RecordRef = order == null ? "booking:none" : "harvest_order:" + order.Id,
            };
        }
        var latest = bookings.LastOrDefault();
        return new IncidentStep
        {
            Key = "booking", Day = latest?.BookedDay ?? -1, Value = latest?.DaysCharged ?? 0,
            Text = latest == null
                ? SimText.Nb($"Ingen fartøy var bestilt for behandlingen")
                : SimText.Nb($"Fartøyet bestilt til dag {latest.BookedDay} har ventet i {latest.DaysCharged} døgn ({latest.ChargedNok:N0} kr leie)"),
            RecordRef = latest == null ? "booking:none" : "booking:" + latest.Id,
        };
    }

    private string RequirementCause(SeaSite site, OffshoreUnit unit) => unit.Requirement switch
    {
        HandlingRequirement.Delouse => SimText.Nb($"lus {site.LastLiceCount:F2} over grensen {LiceLimitAtSite(site):F2} to tellinger på rad"),
        HandlingRequirement.MtbRelief => SimText.Nb($"biomassen ventes over {OffshoreCal.Handling.MtbReliefShare:P0} av {UnitMtbTonnes(site):N0} t MTB"),
        HandlingRequirement.Harvest => SimText.Nb($"slakting er bestilt"),
        HandlingRequirement.NetInspection => SimText.Nb($"nota sist inspisert dag {unit.NetKnownAsOfDay}"),
        _ => "",
    };
}
