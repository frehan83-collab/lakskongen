using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Calibration;
using LakskongenSim.Events;
using LakskongenSim.Farm;
using LakskongenSim.Farm.Offshore;
using LakskongenSim.Finance;

namespace LakskongenSim.Core;

/// <summary>
/// An offshore unit's incidents (roadmap F2-08, docs/FARM_TYPES_SPEC.md §3.3
/// "Structure and net", "Tilt chain (OF1-class)", §3.5, §6.6): each day one of
/// the unit's eight uniforms (u5) picks the day's incident by inverse CDF over
/// the tilt, tear and mooring hazards and its remainder decides a jellyfish
/// day; u6 and u7 are the incident's pre-drawn branches. An incident is a
/// critical event with an incident report of at least three causal steps;
/// the escaped fish leave their zones through the mortality ledger as an
/// escape, are recorded in the site's escape incidents (the escape score and
/// the sustainability score read them) and cost the Directorate's fee and the
/// recapture and river monitoring. Hs alone never releases a fish: the fjord's
/// storm escapes skip the unit (F2-02 hook) and every escape here has an
/// incident behind it. An incident stays open while it is investigated (a
/// tear until it is mended); a new one cannot start meanwhile.
/// </summary>
public partial class SimulationEngine
{
    /// <summary>The uniforms of the unit's day F2-08 reads (spec §3.3: u5-u7 reserved for F2-08).</summary>
    internal const int IncidentPickUniform = 5, IncidentBranchUniform = 6, IncidentCatchUniform = 7;

    /// <summary>
    /// Test seam (F2-08 acceptance 3 and 4, "forced events"): when set, an
    /// incident kind's daily hazard at a unit is this function's value instead
    /// of the model's (null keeps the model's). The unit still draws its eight
    /// uniforms; nothing else changes. Never set by the game.
    /// </summary>
    internal Func<SeaSite, OffshoreIncidentKind, double?>? IncidentHazardOverrideForTests { get; set; }

    /// <summary>The incidents (open and closed) at a unit, in the order they started.</summary>
    public IReadOnlyList<OffshoreIncident> OffshoreIncidentsOf(string siteId)
        => State?.OffshoreIncidents?.Where(i => i.UnitId == siteId).ToList() ?? (IReadOnlyList<OffshoreIncident>)Array.Empty<OffshoreIncident>();

    /// <summary>The incident under investigation at a unit, or null.</summary>
    public OffshoreIncident? ActiveIncidentOf(SeaSite site)
    {
        string id = site.Offshore?.ActiveIncidentId ?? "";
        return id.Length == 0 ? null : State?.OffshoreIncidents?.FirstOrDefault(i => i.Id == id);
    }

    /// <summary>The incident reports, in the order written (every unit's, or one unit's).</summary>
    public IReadOnlyList<IncidentReport> IncidentReportsOf(string? siteId = null)
        => State?.IncidentReports?.Where(r => siteId == null || r.UnitId == siteId).ToList()
           ?? (IReadOnlyList<IncidentReport>)Array.Empty<IncidentReport>();

    /// <summary>The tilt hazard's drivers at a unit today (the bridge's list, spec §6.5 "alarm load with its drivers").</summary>
    public IReadOnlyList<OffshoreDriver> TiltHazardDrivers(SeaSite site)
    {
        var unit = site.Offshore ?? throw new ArgumentException("not an offshore unit", nameof(site));
        bool hasFish = site.Pens.Any(p => p.TotalCount > 0);
        return TiltHazardModel.Drivers(unit, Weather.SimulationDay, hasFish, OffshoreCal.Incidents, OffshoreCal.Teething);
    }

    // ─── The day (called from RunOffshoreUnitDay after the alarm load) ─────

    /// <summary>
    /// The day's incidents at a commissioned unit: the open incident's day
    /// (a tear leaks until mended; an investigation closes on a net inspection
    /// or its time), then, with fish in the unit and no incident open, the
    /// day's pick over the hazards (u5) and its branches (u6, u7); a day with
    /// no incident may be a jellyfish day in November or December (u5's
    /// remainder). On its commissioning day the unit's uniforms are the
    /// teething deck's, so nothing happens then.
    /// </summary>
    private void RunOffshoreIncidentDay(SeaSite site, OffshoreUnit unit, int seaDay, bool hasFish, double[] u)
    {
        AdvanceActiveIncident(site, unit, seaDay);
        if (!hasFish || unit.CommissionedDay == seaDay) return;
        var cal = OffshoreCal.Incidents;
        bool open = unit.ActiveIncidentId.Length > 0;
        var tiltDrivers = TiltHazardModel.Drivers(unit, seaDay, hasFish, cal, OffshoreCal.Teething);
        double design = unit.DesignHsM + unit.DesignHsBonusM;
        double tilt = open ? 0.0 : HazardOf(site, OffshoreIncidentKind.Tilt, TiltHazardModel.Of(tiltDrivers));
        double tear = open ? 0.0 : HazardOf(site, OffshoreIncidentKind.NetTear, OffshoreIncidentModel.TearHazard(site.NetIntegrityPct, cal));
        double mooring = open ? 0.0 : HazardOf(site, OffshoreIncidentKind.MooringFailure, OffshoreIncidentModel.MooringHazard(unit.CurrentWaveHeight, design, cal));
        var (kind, remainder) = OffshoreIncidentModel.Pick(u[IncidentPickUniform], tilt, tear, mooring);
        switch (kind)
        {
            case OffshoreIncidentKind.Tilt:
                StartTilt(site, unit, seaDay, tilt, tiltDrivers, u[IncidentBranchUniform], u[IncidentCatchUniform]);
                break;
            case OffshoreIncidentKind.NetTear:
                StartNetTear(site, unit, seaDay, tear, u[IncidentBranchUniform], u[IncidentCatchUniform]);
                break;
            case OffshoreIncidentKind.MooringFailure:
                StartMooringFailure(site, unit, seaDay, mooring, design, u[IncidentBranchUniform]);
                break;
            default:
                MaybeJellyfishDay(site, unit, seaDay, remainder);
                break;
        }
    }

    private double HazardOf(SeaSite site, OffshoreIncidentKind kind, double model)
        => IncidentHazardOverrideForTests?.Invoke(site, kind) ?? model;

    /// <summary>
    /// The tilt (spec §3.3): the ballast deviation is caught with p = 1 − the
    /// alarm load (u7); with the hatch procedure at 0.8 or above, the crew on
    /// board and none overdue, the standing order "Lukk luker ved ballastavvik"
    /// closes the hatches when it is caught and the tilt ends within the hour;
    /// otherwise it lasts 1-24 h (u6). Escape = 1.5 % × min(1, hours / 12) of the unit's fish.
    /// </summary>
    private void StartTilt(SeaSite site, OffshoreUnit unit, int seaDay, double hazard, IReadOnlyList<OffshoreDriver> drivers,
        double durationDraw, double catchDraw)
    {
        var cal = OffshoreCal.Incidents;
        var incident = NewIncident(site, unit, seaDay, OffshoreIncidentKind.Tilt, hazard, drivers);
        incident.CatchProbability = 1.0 - unit.AlarmLoad;
        incident.DeviationCaught = catchDraw < incident.CatchProbability;
        incident.StandingOrderReady = StandingOrderReady(unit);
        incident.AutoClosed = incident.DeviationCaught && incident.StandingOrderReady;
        incident.DurationHours = incident.AutoClosed ? cal.AutoCloseHours : OffshoreIncidentModel.TiltDurationHours(durationDraw, cal);
        incident.EscapeShare = OffshoreIncidentModel.TiltEscapeShare(incident.DurationHours, cal);
        incident.BallastDefectId = unit.TeethingDefects.FirstOrDefault(d => d.Kind == TeethingDefectKind.BallastPipeLeak
            && d.IsRevealed && d.RevealedDay <= seaDay && !d.IsFixedBy(seaDay))?.Id ?? "";
        int escaped = EscapeFromUnit(site, site.Pens, incident.EscapeShare);
        incident.EscapedFirstDay = escaped;
        BookEscape(site, unit, incident, escaped);
        PublishIncident(site, GameEventType.TiltEvent, incident,
            SimText.Nb($"{site.Label} krenget i {incident.DurationHours:F0} timer: {escaped:N0} fisk rømte"));
        WriteIncidentReport(site, incident);
    }

    /// <summary>True when the standing order can close the hatches: procedure ≥ 0.8, crew on board, none overdue (spec §3.3).</summary>
    internal bool StandingOrderReady(OffshoreUnit unit)
        => unit.HatchProcedureLevel >= OffshoreCal.Incidents.AutoCloseProcedureMin && unit.EmployeeCount > 0 && unit.CrewOverdueDays == 0;

    /// <summary>
    /// A tear in a worn net (spec §3.3: possible below 70 %): one zone (u7)
    /// loses a share of its fish (u6, 0.5-3 %) the first day and leaks a little
    /// every further day until the net is mended in a window.
    /// </summary>
    private void StartNetTear(SeaSite site, OffshoreUnit unit, int seaDay, double hazard, double sizeDraw, double zoneDraw)
    {
        var cal = OffshoreCal.Incidents;
        var drivers = new List<OffshoreDriver>
        {
            new("net_integrity_pct", site.NetIntegrityPct),
            new("tear_below_pct", cal.NetTearBelowPct),
            new("hazard_at_zero", cal.NetTearHazardAtZero),
        };
        var incident = NewIncident(site, unit, seaDay, OffshoreIncidentKind.NetTear, hazard, drivers);
        var zones = site.PhysicalPens.Where(p => site.PopulationIn(p)?.TotalCount > 0).OrderBy(p => p.Number).ToList();
        var zone = zones.Count == 0 ? null : zones[Math.Clamp((int)(zoneDraw * zones.Count), 0, zones.Count - 1)];
        incident.ZoneNumber = zone?.Number ?? 0;
        incident.EscapeShare = cal.NetTearEscapeMin + Math.Clamp(sizeDraw, 0.0, 1.0) * (cal.NetTearEscapeMax - cal.NetTearEscapeMin);
        var pop = zone == null ? null : site.PopulationIn(zone);
        int escaped = pop == null ? 0 : EscapeFromUnit(site, new[] { pop }, incident.EscapeShare);
        incident.EscapedFirstDay = escaped;
        BookEscape(site, unit, incident, escaped);
        PublishIncident(site, GameEventType.NetTear, incident,
            SimText.Nb($"Hull i nota i sone {incident.ZoneNumber} på {site.Label}: {escaped:N0} fisk rømte, nota må repareres i et værvindu"));
        WriteIncidentReport(site, incident);
    }

    /// <summary>
    /// A mooring line parts on a day above the unit's design sea (spec §3.3: a
    /// 2 % hazard a day above design, the escape capped at 10 %): up to 10 % of
    /// the unit's fish escape (u6).
    /// </summary>
    private void StartMooringFailure(SeaSite site, OffshoreUnit unit, int seaDay, double hazard, double design, double shareDraw)
    {
        var cal = OffshoreCal.Incidents;
        var drivers = new List<OffshoreDriver>
        {
            new("hs_m", unit.CurrentWaveHeight),
            new("design_hs_m", design),
            new("hazard_over_design", cal.MooringHazardOverDesign),
        };
        var incident = NewIncident(site, unit, seaDay, OffshoreIncidentKind.MooringFailure, hazard, drivers);
        incident.EscapeShare = Math.Clamp(shareDraw, 0.0, 1.0) * cal.MooringEscapeMax;
        int escaped = EscapeFromUnit(site, site.Pens, incident.EscapeShare);
        incident.EscapedFirstDay = escaped;
        BookEscape(site, unit, incident, escaped);
        PublishIncident(site, GameEventType.MooringFailure, incident,
            SimText.Nb($"Fortøyningsbrudd på {site.Label} i {unit.CurrentWaveHeight:F1} m sjø (dimensjonert for {design:F1} m): {escaped:N0} fisk rømte"));
        WriteIncidentReport(site, incident);
    }

    private OffshoreIncident NewIncident(SeaSite site, OffshoreUnit unit, int seaDay, OffshoreIncidentKind kind, double hazard,
        IReadOnlyList<OffshoreDriver> hazardDrivers)
    {
        var incident = new OffshoreIncident
        {
            Id = $"{site.Id}/inc_{unit.NextIncidentNumber++}",
            Kind = kind,
            UnitId = site.Id,
            StartDay = seaDay,
            HazardPerDay = hazard,
            HazardDrivers = hazardDrivers.ToList(),
            AlarmLoad = unit.AlarmLoad,
            AlarmDrivers = AlarmLoadDrivers(unit, seaDay).Select(d => new OffshoreDriver(d.Driver, d.Value)).ToList(),
            HatchProcedureLevel = unit.HatchProcedureLevel,
            CrewOverdueDays = unit.CrewOverdueDays,
            HsM = unit.CurrentWaveHeight,
            DesignHsM = unit.DesignHsM + unit.DesignHsBonusM,
            NetIntegrityPct = site.NetIntegrityPct,
        };
        State.OffshoreIncidents.Add(incident);
        unit.ActiveIncidentId = incident.Id;
        return incident;
    }

    /// <summary>
    /// The open incident's day: a tear leaks its zone until mended; an
    /// investigation closes when the nets were inspected after the incident
    /// (an ROV inspection or the subsea cameras) and any tear is mended, or
    /// when its time runs out with nothing left leaking.
    /// </summary>
    private void AdvanceActiveIncident(SeaSite site, OffshoreUnit unit, int seaDay)
    {
        var incident = ActiveIncidentOf(site);
        if (incident == null)
        {
            unit.ActiveIncidentId = "";
            return;
        }
        if (seaDay <= incident.StartDay) return;
        var cal = OffshoreCal.Incidents;
        if (incident.Kind == OffshoreIncidentKind.NetTear && !incident.Mended)
        {
            var zone = site.PhysicalPens.FirstOrDefault(p => p.Number == incident.ZoneNumber);
            var pop = zone == null ? null : site.PopulationIn(zone);
            if (pop != null && pop.TotalCount > 0)
            {
                int leaked = EscapeFromUnit(site, new[] { pop }, cal.NetTearLeakPerDay);
                incident.LeakDays++;
                BookEscape(site, unit, incident, leaked);
            }
            return;
        }
        bool inspected = unit.NetKnownAsOfDay > incident.StartDay;
        if (inspected || seaDay - incident.StartDay >= cal.InvestigationMaxDays)
        {
            incident.Stage = OffshoreIncidentStage.Closed;
            incident.ClosedDay = seaDay;
            unit.ActiveIncidentId = "";
        }
    }

    /// <summary>A net repair at a unit mends an open tear (its leak stops; the investigation may close).</summary>
    internal void MendTearAfterRepair(SeaSite site)
    {
        var incident = ActiveIncidentOf(site);
        if (incident is { Kind: OffshoreIncidentKind.NetTear, Mended: false })
            incident.Mended = true;
    }

    /// <summary>
    /// A jellyfish day (spec §3.3: stinging <i>Apolemia</i> on 5 % of days in
    /// November and December, O:R17): <paramref name="u"/> below the share kills
    /// the calibrated share of every zone's fish, booked as jellyfish deaths.
    /// </summary>
    private void MaybeJellyfishDay(SeaSite site, OffshoreUnit unit, int seaDay, double u)
    {
        var cal = OffshoreCal.Incidents;
        int month = SimCalendar.Month(seaDay);
        if (month < 11 || u >= cal.JellyDayShareNovDec) return;
        int dead = 0;
        foreach (var pop in site.Pens)
        {
            if (pop.TotalCount <= 0) continue;
            foreach (var cohort in pop.Cohorts)
            {
                int n = (int)Math.Round(cohort.Count * cal.JellyDayMortality, MidpointRounding.AwayFromZero);
                if (n > 0) dead += pop.RemoveDead(cohort, n, MortalityCause.Jellyfish);
            }
            pop.TriggerOnPopulationChanged();
        }
        unit.JellyDays++;
        unit.LastJellyDay = seaDay;
        State.EventLog.Add(new EventRecord
        {
            Day = State.Day,
            Name = "Brennmaneter",
            Category = EventCategory.Disease,
            Description = SimText.Nb($"Brennmaneter drev inn i {site.Label}: {dead:N0} fisk døde av sår på gjeller og hud"),
        });
        State.TrimEventLog();
    }

    // ─── Escapes ───────────────────────────────────────────────────────────

    /// <summary>
    /// Let <paramref name="share"/> of the fish in <paramref name="pops"/> escape
    /// (each cohort's count × share, rounded half away from zero), through the
    /// mortality ledger as an escape; returns the fish that left.
    /// </summary>
    private int EscapeFromUnit(SeaSite site, IEnumerable<FishPopulation> pops, double share)
    {
        if (share <= 0.0) return 0;
        int escaped = 0;
        foreach (var pop in pops.ToList())
        {
            if (pop.TotalCount <= 0) continue;
            foreach (var cohort in pop.Cohorts.ToList())
            {
                int n = (int)Math.Round(cohort.Count * Math.Min(1.0, share), MidpointRounding.AwayFromZero);
                if (n > 0) escaped += pop.RemoveAlive(cohort, n, FishRemoval.Escape);
            }
            pop.TriggerOnPopulationChanged();
        }
        return escaped;
    }

    /// <summary>
    /// Book an incident's escaped fish: the site's escape record (escape score,
    /// sustainability), the Directorate's fee (up to 15 G) and the recapture
    /// and river monitoring, at the company's equity share, ring-fenced like the
    /// unit's other lines.
    /// </summary>
    private void BookEscape(SeaSite site, OffshoreUnit unit, OffshoreIncident incident, int escaped)
    {
        if (escaped <= 0) return;
        var cal = OffshoreCal.Incidents;
        if (incident.EscapedFish == 0)
        {
            site.RecordEscape(escaped, State.Day); // one escape incident in the record per incident
            incident.EscapeRecordDay = State.Day;
        }
        else
        {
            // A tear's later leak joins its incident's escape record (the escape score counts incidents).
            site.EscapesThisYear += escaped;
            var record = site.EscapeIncidents.LastOrDefault(e => e.Day == incident.EscapeRecordDay);
            if (record != null)
            {
                record.Fish += escaped;
            }
            else
            {
                site.RecordEscape(escaped, State.Day); // its first record aged out of the rolling year: a fresh one
                incident.EscapeRecordDay = State.Day;
            }
        }
        incident.EscapedFish += escaped;
        decimal share = (decimal)unit.EquityShare;
        decimal ceiling = _calibration.Capacity.InfringementFeeCeilingNok;
        decimal feeBefore = incident.FeeNok;
        decimal fee = Math.Min(ceiling, incident.EscapedFish * cal.EscapeFeeNokPerFish) - feeBefore; // one fee per incident, up to 15 G
        decimal measures = escaped * cal.EscapeMeasuresNokPerFish;
        if (fee > 0m)
        {
            PayCost(fee * share, CostBooking.Opex, "fines");
            RingFenceOwnLine(site, fee * share);
            incident.FeeNok += fee;
        }
        if (measures > 0m)
        {
            PayCost(measures * share, CostBooking.Opex, "escape_measures");
            RingFenceOwnLine(site, measures * share);
            incident.MeasuresNok += measures;
        }
    }

    private void PublishIncident(SeaSite site, GameEventType type, OffshoreIncident incident, string message)
    {
        PublishGameEvent(new GameEventRequest(type, State.Day)
        {
            SiteId = site.Id,
            UnitId = site.Id,
            Variant = incident.Id,
            Value = incident.EscapedFirstDay,
            Message = message,
        });
    }

    // ─── Reports ───────────────────────────────────────────────────────────

    /// <summary>
    /// The incident report of an incident (spec §6.6): its causal chain with
    /// the drivers behind each probability, the losses and the counter it
    /// unlocks; the title goes to the event log.
    /// </summary>
    private IncidentReport WriteIncidentReport(SeaSite site, OffshoreIncident incident)
    {
        var steps = incident.Kind switch
        {
            OffshoreIncidentKind.Tilt => TiltSteps(site, incident),
            OffshoreIncidentKind.NetTear => TearSteps(site, incident),
            _ => MooringSteps(site, incident),
        };
        string kind = incident.Kind switch
        {
            OffshoreIncidentKind.Tilt => "tilt",
            OffshoreIncidentKind.NetTear => "net_tear",
            _ => "mooring_failure",
        };
        string counter = incident.Kind switch
        {
            OffshoreIncidentKind.Tilt => incident.BallastDefectId.Length > 0 ? "fix_defect"
                : incident.HatchProcedureLevel < OffshoreCal.Incidents.AutoCloseProcedureMin ? "hatch_procedure" : "alarm_review",
            OffshoreIncidentKind.NetTear => "repair_net",
            _ => "reinforcement",
        };
        return AddReport(site, kind, incident.Id, steps, incident.EscapedFish, incident.FeeNok + incident.MeasuresNok, counter);
    }

    internal IncidentReport AddReport(SeaSite site, string kind, string incidentId, List<IncidentStep> steps, int fishLost, decimal costNok,
        string counter)
    {
        for (int i = 0; i < steps.Count; i++) steps[i].Order = i + 1;
        var report = new IncidentReport
        {
            Id = $"ir_{State.NextIncidentReportNumber++}",
            UnitId = site.Id,
            Day = State.Day,
            Kind = kind,
            IncidentId = incidentId,
            Steps = steps,
            FishLost = fishLost,
            CostNok = costNok,
            ProcedureUnlocked = counter,
        };
        State.IncidentReports.Add(report);
        State.EventLog.Add(new EventRecord
        {
            Id = report.Id,
            Day = State.Day,
            Name = "Hendelsesrapport",
            Category = EventCategory.Equipment,
            Description = SimText.Nb($"Hendelsesrapport for {site.Label}: {string.Join(" → ", steps.Select(s => s.Text))}"),
            BalanceChange = -costNok,
        });
        State.TrimEventLog();
        return report;
    }

    private static string F(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

    private List<IncidentStep> TiltSteps(SeaSite site, OffshoreIncident i)
    {
        var steps = new List<IncidentStep>();
        var unit = site.Offshore!;
        if (i.BallastDefectId.Length > 0)
        {
            var defect = unit.TeethingDefects.First(d => d.Id == i.BallastDefectId);
            steps.Add(new IncidentStep
            {
                Key = "defect", Day = defect.RevealedDay, Value = OffshoreCal.Teething.BallastTiltMultiplier,
                Text = SimText.Nb($"Lekkasjen i ballastrøret (kjent fra dag {defect.RevealedDay}) var ikke utbedret: krengefaren ×{F(OffshoreCal.Teething.BallastTiltMultiplier, "0")}"),
                RecordRef = "defect:" + defect.Id,
            });
        }
        steps.Add(new IncidentStep
        {
            Key = "hazard", Day = i.StartDay, Value = i.HazardPerDay, Drivers = i.HazardDrivers.ToList(),
            Text = SimText.Nb($"Krengefaren den dagen tilsvarte {F((1.0 - Math.Pow(1.0 - Math.Min(1.0, i.HazardPerDay), SimCalendar.DaysPerYear)) * 100.0, "0.0")} % i året (lukeprosedyre {F(i.HatchProcedureLevel, "0.0")}, mannskapsbytte {i.CrewOverdueDays} døgn på overtid)"),
            RecordRef = "incident:" + i.Id,
        });
        steps.Add(new IncidentStep
        {
            Key = "alarm_load", Day = i.StartDay, Value = i.AlarmLoad, Drivers = i.AlarmDrivers.ToList(),
            Text = SimText.Nb($"Alarmbelastningen var {F(i.AlarmLoad, "0.00")}: ballastavviket kom gjennom med sannsynlighet {F(i.CatchProbability, "0.00")}"),
            RecordRef = "incident:" + i.Id,
        });
        steps.Add(new IncidentStep
        {
            Key = "deviation", Day = i.StartDay, Value = i.DeviationCaught ? 1.0 : 0.0,
            Text = i.AutoClosed
                ? SimText.Nb($"Ballastavviket ble fanget opp, og den stående ordren «Lukk luker ved ballastavvik» lukket lukene")
                : i.DeviationCaught
                    ? SimText.Nb($"Ballastavviket ble fanget opp, men lukeprosedyren ({F(i.HatchProcedureLevel, "0.0")}) eller mannskapet holdt ikke for den stående ordren")
                    : SimText.Nb($"Ballastavviket ble ikke fanget opp: alarmklassen var dempet"),
            RecordRef = "incident:" + i.Id,
        });
        steps.Add(new IncidentStep
        {
            Key = "duration", Day = i.StartDay, Value = i.DurationHours,
            Text = SimText.Nb($"Anlegget krenget i {F(i.DurationHours, "0.0")} timer"),
            RecordRef = "incident:" + i.Id,
        });
        steps.Add(EscapeStep(site, i));
        return steps;
    }

    private List<IncidentStep> TearSteps(SeaSite site, OffshoreIncident i) => new()
    {
        new IncidentStep
        {
            Key = "net", Day = site.Offshore!.NetKnownAsOfDay, Value = i.NetIntegrityPct, Drivers = i.HazardDrivers.ToList(),
            Text = SimText.Nb($"Nota var slitt ned til {F(i.NetIntegrityPct, "0")} % (under {F(OffshoreCal.Incidents.NetTearBelowPct, "0")} % kan den revne)"),
            RecordRef = "incident:" + i.Id,
        },
        new IncidentStep
        {
            Key = "hazard", Day = i.StartDay, Value = i.HazardPerDay, Drivers = i.HazardDrivers.ToList(),
            Text = SimText.Nb($"Faren for at nota revnet var {F(i.HazardPerDay * 100.0, "0.00")} % den dagen"),
            RecordRef = "incident:" + i.Id,
        },
        new IncidentStep
        {
            Key = "tear", Day = i.StartDay, Value = i.ZoneNumber,
            Text = SimText.Nb($"Nota revnet i sone {i.ZoneNumber}: {F(i.EscapeShare * 100.0, "0.0")} % av fisken i sonen slapp ut"),
            RecordRef = "incident:" + i.Id,
        },
        EscapeStep(site, i),
    };

    private List<IncidentStep> MooringSteps(SeaSite site, OffshoreIncident i) => new()
    {
        new IncidentStep
        {
            Key = "sea", Day = i.StartDay, Value = i.HsM, Drivers = i.HazardDrivers.ToList(),
            Text = SimText.Nb($"Sjøen nådde {F(i.HsM, "0.0")} m, over anleggets dimensjonering på {F(i.DesignHsM, "0.0")} m"),
            RecordRef = "incident:" + i.Id,
        },
        new IncidentStep
        {
            Key = "hazard", Day = i.StartDay, Value = i.HazardPerDay,
            Text = SimText.Nb($"Over dimensjoneringen er faren for fortøyningsbrudd {F(i.HazardPerDay * 100.0, "0")} % i døgnet"),
            RecordRef = "incident:" + i.Id,
        },
        new IncidentStep
        {
            Key = "mooring", Day = i.StartDay, Value = i.EscapeShare,
            Text = SimText.Nb($"En fortøyningsline røk og anlegget slet: {F(i.EscapeShare * 100.0, "0.0")} % av fisken slapp ut"),
            RecordRef = "incident:" + i.Id,
        },
        EscapeStep(site, i),
    };

    private IncidentStep EscapeStep(SeaSite site, OffshoreIncident i) => new()
    {
        Key = "escape", Day = i.StartDay, Value = i.EscapedFirstDay,
        Text = SimText.Nb($"{i.EscapedFirstDay:N0} fisk rømte; gebyr {i.FeeNok:N0} kr og gjenfangst og elveovervåking {i.MeasuresNok:N0} kr"),
        RecordRef = $"escape:{site.Id}:{i.EscapeRecordDay.ToString(CultureInfo.InvariantCulture)}",
    };

    // ─── The counter-verbs (spec §3.4) ─────────────────────────────────────

    /// <summary>
    /// Start an alarm review ("Alarmgjennomgang", spec §3.3: 4 M NOK, 30 days):
    /// once done, it takes 0.30 off the alarm load for a year (the crew
    /// re-tunes the alarm classes). Fails: not_found, not_operational,
    /// already_running (one runs or is in force), insufficient_funds.
    /// </summary>
    public CommandResult StartAlarmReview(string siteId)
    {
        var site = FindOffshoreUnit(siteId);
        var unit = site?.Offshore;
        if (site == null || unit == null)
            return CommandResult.Fail(OffshoreReasons.NotFound, $"No offshore unit '{siteId}'");
        if (!unit.IsCommissioned)
            return CommandResult.Fail(OffshoreReasons.NotOperational, $"{site.Label} is not commissioned yet");
        var review = OffshoreCal.AlarmReview;
        if (AlarmLoadModel.ReviewRunning(unit, State.Day, review) || AlarmLoadModel.ReviewInForce(unit, State.Day, review))
            return CommandResult.Fail(OffshoreReasons.AlreadyRunning, $"{site.Label} has a review running or in force");
        decimal cost = review.CostNok * (decimal)unit.EquityShare;
        if (!TryPayCost(cost, CostBooking.Opex, "offshore_alarm_review"))
            return CommandResult.Fail(OffshoreReasons.InsufficientFunds, $"Insufficient funds: the review costs {cost:N0} NOK", (double)cost);
        RingFenceOwnLine(site, cost);
        unit.AlarmReviewStartedDay = State.Day;
        return CommandResult.Success($"{site.Label}: alarm review until day {State.Day + review.Days}", (double)cost, State.Day + review.Days, site.Id);
    }

    /// <summary>
    /// Upgrade a procedure (spec §3.4 <c>StartProcedureUpgrade(kind)</c>): "hatch"
    /// raises the hatch procedure level by 0.2 after 60 days (0.6 → 0.8 lets the
    /// standing order close the hatches on a caught deviation). Fails: not_found,
    /// out_of_range (an unknown kind), already_running, max_level, insufficient_funds.
    /// </summary>
    public CommandResult StartProcedureUpgrade(string siteId, string kind)
    {
        var site = FindOffshoreUnit(siteId);
        var unit = site?.Offshore;
        if (site == null || unit == null)
            return CommandResult.Fail(OffshoreReasons.NotFound, $"No offshore unit '{siteId}'");
        if (kind != "hatch")
            return CommandResult.Fail(OffshoreReasons.OutOfRange, $"Procedure '{kind}' is not known (hatch)");
        var p = OffshoreCal.Procedures;
        if (unit.ProcedureUpgradeDoneDay > State.Day)
            return CommandResult.Fail(OffshoreReasons.AlreadyRunning, $"{site.Label} upgrades its procedure until day {unit.ProcedureUpgradeDoneDay}");
        if (unit.HatchProcedureLevel >= 1.0 - 1e-9)
            return CommandResult.Fail(OffshoreReasons.MaxLevel, $"{site.Label}'s hatch procedure is at its best");
        decimal cost = p.CostNok * (decimal)unit.EquityShare;
        if (!TryPayCost(cost, CostBooking.Opex, "offshore_procedures"))
            return CommandResult.Fail(OffshoreReasons.InsufficientFunds, $"Insufficient funds: the upgrade costs {cost:N0} NOK", (double)cost);
        RingFenceOwnLine(site, cost);
        unit.ProcedureUpgradeTarget = Math.Min(1.0, unit.HatchProcedureLevel + p.HatchStep);
        unit.ProcedureUpgradeDoneDay = State.Day + p.Days;
        return CommandResult.Success($"{site.Label}: hatch procedure to {unit.ProcedureUpgradeTarget:F1} on day {unit.ProcedureUpgradeDoneDay}",
            (double)cost, unit.ProcedureUpgradeDoneDay, site.Id);
    }

    /// <summary>
    /// "Forsterkning" (spec §3.4 <c>InstallReinforcement</c>, §3.9): +1.0 m of
    /// design Hs after 90 days of yard work at the plot (fewer days above design,
    /// less fatigue). Fails: not_found, not_operational, already_installed,
    /// insufficient_funds.
    /// </summary>
    public CommandResult InstallReinforcement(string siteId)
    {
        var site = FindOffshoreUnit(siteId);
        var unit = site?.Offshore;
        if (site == null || unit == null)
            return CommandResult.Fail(OffshoreReasons.NotFound, $"No offshore unit '{siteId}'");
        if (!unit.IsCommissioned)
            return CommandResult.Fail(OffshoreReasons.NotOperational, $"{site.Label} is not commissioned yet");
        if (unit.ReinforcementDoneDay >= 0 || unit.DesignHsBonusM > 0.0)
            return CommandResult.Fail(OffshoreReasons.AlreadyInstalled, $"{site.Label} is reinforced or being reinforced");
        var r = OffshoreCal.Reinforcement;
        decimal cost = r.CapexNok * (decimal)unit.EquityShare;
        if (!TryPayCost(cost, CostBooking.Capex, "facilities"))
            return CommandResult.Fail(OffshoreReasons.InsufficientFunds, $"Insufficient funds: the reinforcement costs {cost:N0} NOK", (double)cost);
        unit.ReinforcementDoneDay = State.Day + r.Days;
        RegisterFacilityAsset(site.Id, FacilityAssetKind.OffshoreUnit, cost, unit.ReinforcementDoneDay);
        return CommandResult.Success($"{site.Label}: reinforced on day {unit.ReinforcementDoneDay}", (double)cost, unit.ReinforcementDoneDay, site.Id);
    }

    /// <summary>The counter-verbs' work that completes on a day: the procedure upgrade and the reinforcement.</summary>
    private void CompleteOffshoreUpgrades(SeaSite site, OffshoreUnit unit, int seaDay)
    {
        if (unit.ProcedureUpgradeDoneDay >= 0 && seaDay >= unit.ProcedureUpgradeDoneDay && unit.ProcedureUpgradeTarget > unit.HatchProcedureLevel)
            unit.HatchProcedureLevel = unit.ProcedureUpgradeTarget;
        if (unit.ReinforcementDoneDay >= 0 && seaDay >= unit.ReinforcementDoneDay && unit.DesignHsBonusM <= 0.0)
            unit.DesignHsBonusM = OffshoreCal.Reinforcement.DesignHsBonusM;
    }
}
