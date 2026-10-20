using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Environment;
using LakskongenSim.Events;
using LakskongenSim.Farm;
using LakskongenSim.Farm.Offshore;

namespace LakskongenSim.Core;

/// <summary>
/// An offshore unit's operations and its day (roadmap F2-07,
/// docs/FARM_TYPES_SPEC.md §3.3 "OffshoreTickSystem", §3.4 "BookOffshoreOperation",
/// §6.5 "Værvinduer"): vessel bookings into forecast windows with their
/// charter paid from the booked day (slipped days included) and their
/// preview, the silo the fish eat from, the crew rota, the ensilage tank,
/// the structure and nets, the teething deck's reveals, the sensors, the
/// alarm load, the unit's own lines (at its equity share) and the
/// consortium's settlement. Each unit draws exactly 8 uniforms a day from
/// <c>offshore_farm/&lt;unit&gt;</c> (spec §3.7), whatever happens.
/// </summary>
public partial class SimulationEngine
{
    /// <summary>The operations a booking can carry (harvests go through the S2-04 orders; transfers are F2-13's; the tow is <see cref="StartTow"/>).</summary>
    public static readonly IReadOnlyList<OffshoreOp> BookableOffshoreOps = new[]
    {
        OffshoreOp.Resupply, OffshoreOp.Silage, OffshoreOp.CrewChange, OffshoreOp.NetInspection, OffshoreOp.Stocking, OffshoreOp.Delousing,
    };

    /// <summary>Uniforms an offshore unit draws a day (spec §3.7).</summary>
    public const int OffshoreUnitUniformsPerDay = 8;

    /// <summary>The probability a "first green window" booking asks for (spec §6.5: P ≥ 0.8 solid green).</summary>
    public const double GreenWindowProbability = 0.8;

    // ─── Bookings ──────────────────────────────────────────────────────────

    /// <summary>
    /// Book <paramref name="op"/> at an offshore unit for <paramref name="day"/>
    /// with <paramref name="vessel"/> (spec §3.4). The vessel is chartered from
    /// that day until the operation is done: each day the unit's sea is within
    /// the operation's limit for the vessel it is done, otherwise it slips a
    /// day with charter paid; a fish-handling vessel waits alongside (pumping
    /// paused) up to the alongside limit and leaves to shelter above it. A
    /// stocking carries <paramref name="count"/> smolts of strain
    /// <paramref name="detail"/>; a delousing its method in <paramref name="detail"/>.
    /// The result's second value is the forecast's P(the operation can run on
    /// that day): below 0.5 the booking stands with the warning outside_window.
    /// Fails before any change: engine_not_ready, not_found, not_operational,
    /// not_bookable, no_vessel (that vessel cannot do it), invalid_day (today
    /// or earlier), already_booked (one open booking per operation and vessel
    /// kind), invalid_count, shakedown_active, unknown_method.
    /// </summary>
    public CommandResult BookOffshoreOperation(string siteId, OffshoreOp op, int day, VesselClass vessel, int count = 0, string detail = "")
    {
        if (State == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        var site = FindOffshoreUnit(siteId);
        var unit = site?.Offshore;
        if (site == null || unit == null)
            return CommandResult.Fail(OffshoreReasons.NotFound, $"No offshore unit '{siteId}'");
        if (!unit.IsCommissioned)
            return CommandResult.Fail(OffshoreReasons.NotOperational, $"{site.Label} is not commissioned yet");
        if (!BookableOffshoreOps.Contains(op))
            return CommandResult.Fail("not_bookable", $"{op} is not booked here");
        if (OperabilityWindows.HsLimitM(op, vessel, OffshoreCal.Operability) == null)
            return CommandResult.Fail("no_vessel", $"A {vessel} cannot do {op}");
        if (day <= State.Day)
            return CommandResult.Fail("invalid_day", $"Book from day {State.Day + 1} on, not day {day}");
        if (unit.Bookings.Any(b => b.IsOpen && b.Op == op && (b.Vessel == VesselClass.Helicopter) == (vessel == VesselClass.Helicopter)))
            return CommandResult.Fail("already_booked", $"{op} is already booked at {site.Label}");
        detail ??= "";
        if (op == OffshoreOp.Stocking)
        {
            if (count <= 0) return CommandResult.Fail("invalid_count", $"A stocking needs smolts (got {count})");
            var held = OffshoreStockingRefusal(site); // F2-08/F2-09
            if (held != null) return held;
            if (unit.ShakedownEndDay > day)
                return CommandResult.Fail(OffshoreReasons.ShakedownActive, $"{site.Label} runs its shakedown until day {unit.ShakedownEndDay}");
            if (detail.Length == 0) detail = "standard";
        }
        if (op == OffshoreOp.Delousing)
        {
            if (detail.Length == 0) detail = "hydrolicer";
            if (!DelousingSystem.TryParseMethodName(detail, out _))
                return CommandResult.Fail("unknown_method", $"Invalid delousing method '{detail}'");
        }
        var booking = new OffshoreBooking
        {
            Id = $"{site.Id}/ob_{unit.NextBookingNumber++}",
            Op = op,
            Vessel = vessel,
            IssuedDay = State.Day,
            BookedDay = day,
            Count = op == OffshoreOp.Stocking ? count : 0,
            Detail = op is OffshoreOp.Stocking or OffshoreOp.Delousing ? detail : "",
        };
        unit.Bookings.Add(booking);
        double p = ProbabilityOfWindow(site, op, vessel, day);
        return CommandResult.Success(p < 0.5
                ? $"{op} booked at {site.Label} for day {day} (outside_window: P = {p:F2})"
                : $"{op} booked at {site.Label} for day {day}", day, p, booking.Id);
    }

    /// <summary>
    /// Book <paramref name="op"/> into the first forecast day whose probability
    /// reaches <paramref name="minProbability"/> ("Book første grønne vindu",
    /// spec §6.5); when no day of the forecast does, the likeliest day. Fails
    /// as <see cref="BookOffshoreOperation"/>, and no_forecast before the game
    /// has an open sea.
    /// </summary>
    public CommandResult BookFirstGreenWindow(string siteId, OffshoreOp op, VesselClass vessel,
        double minProbability = GreenWindowProbability, int count = 0, string detail = "")
    {
        var site = FindOffshoreUnit(siteId);
        if (site == null)
            return CommandResult.Fail(OffshoreReasons.NotFound, $"No offshore unit '{siteId}'");
        var window = FirstWindow(site, op, vessel, minProbability);
        if (window.Day < 0)
            return CommandResult.Fail("no_forecast", $"{site.Label} has no forecast yet");
        return BookOffshoreOperation(siteId, op, window.Day, vessel, count, detail);
    }

    /// <summary>
    /// The first forecast day (from tomorrow) whose P(op possible) reaches
    /// <paramref name="minProbability"/>, else the likeliest day of the
    /// horizon; (-1, 0) without a forecast. Pure.
    /// </summary>
    public (int Day, double Probability) FirstWindow(SeaSite site, OffshoreOp op, VesselClass vessel, double minProbability)
    {
        var forecast = SeaForecastFor(site);
        if (forecast == null) return (-1, 0.0);
        int bestLead = 1;
        double best = -1.0;
        for (int lead = 1; lead <= forecast.HorizonDays; lead++)
        {
            double p = OperabilityWindows.Probability(forecast, lead, op, vessel, OffshoreCal.Operability, OffshoreCal.Metocean);
            if (p >= minProbability) return (forecast.IssuedDay + lead, p);
            if (p > best)
            {
                best = p;
                bestLead = lead;
            }
        }
        return (forecast.IssuedDay + bestLead, best);
    }

    /// <summary>The forecast's P(op possible on <paramref name="day"/>); 1 inside the horizon's end is not assumed: beyond it, 0.</summary>
    private double ProbabilityOfWindow(SeaSite site, OffshoreOp op, VesselClass vessel, int day)
    {
        var forecast = SeaForecastFor(site);
        if (forecast == null) return 0.0;
        int lead = day - forecast.IssuedDay;
        if (lead < 1 || lead > forecast.HorizonDays) return 0.0;
        return OperabilityWindows.Probability(forecast, lead, op, vessel, OffshoreCal.Operability, OffshoreCal.Metocean);
    }

    /// <summary>Cancel an open booking (the charter paid so far stays paid). Fails: not_found, not_open.</summary>
    public CommandResult CancelOffshoreBooking(string siteId, string bookingId)
    {
        var unit = FindOffshoreUnit(siteId)?.Offshore;
        var booking = unit?.Bookings.FirstOrDefault(b => b.Id == bookingId);
        if (booking == null)
            return CommandResult.Fail(OffshoreReasons.NotFound, $"No booking '{bookingId}' at '{siteId}'");
        if (!booking.IsOpen)
            return CommandResult.Fail("not_open", $"{bookingId} is {booking.Status}");
        booking.Status = OffshoreBookingStatus.Cancelled;
        booking.CompletedDay = State.Day;
        return CommandResult.Success($"{bookingId} cancelled", (double)booking.ChargedNok, id: booking.Id);
    }

    /// <summary>Resupply the silo (spec §3.4 <c>ResupplyFeed</c>): a feed run booked into the first green window.</summary>
    public CommandResult ResupplyFeed(string siteId, VesselClass vessel = VesselClass.Standard)
        => BookFirstGreenWindow(siteId, OffshoreOp.Resupply, vessel);

    /// <summary>Inspect the nets and structure by ROV (spec §3.4 <c>InspectOffshore</c>): booked into the first green window.</summary>
    public CommandResult InspectOffshore(string siteId) => BookFirstGreenWindow(siteId, OffshoreOp.NetInspection, VesselClass.Standard);

    /// <summary>
    /// The booking preview (spec §6.5 "Booking preview", F2-07 acceptance 2):
    /// over sampled futures of the model's own conditional distribution, the
    /// day the operation would be done, so P(done by day X) for each day of
    /// the horizon and the expected charter including slipped days; the
    /// silo's days of feed on the booked day. Null without a forecast or for
    /// an invalid booking. Pure: no game stream moves.
    /// </summary>
    public OffshoreBookingPreview? PreviewOffshoreBooking(string siteId, OffshoreOp op, int day, VesselClass vessel,
        int horizonDays = 30, int paths = OffshoreSeaSampler.DefaultPaths)
    {
        var site = FindOffshoreUnit(siteId);
        var unit = site?.Offshore;
        var sea = State?.Metocean;
        if (State == null || site == null || unit == null || sea == null || sea.LastTickDay < 0 || Weather.SimulationDay != sea.LastTickDay) return null;
        var operability = OffshoreCal.Operability;
        if (OperabilityWindows.HsLimitM(op, vessel, operability) == null || day <= State.Day || horizonDays < 1) return null;
        int lead = day - sea.LastTickDay;
        int span = lead - 1 + horizonDays;
        var futures = OffshoreSeaSampler.Sample(sea, Weather.CurrentState, Weather.StormFrequencyMultiplier, Weather.MaxState,
            PlotHsScaleOf(site), span, paths, OffshoreSeaSampler.QuestionKey(site.PlotId, op, vessel, day), operability);
        var doneBy = new double[horizonDays];
        double chargedDays = 0.0;
        int onBookedDay = 0, notDone = 0;
        foreach (var path in futures)
        {
            int done = -1;
            for (int i = 0; i < horizonDays; i++)
                if (OffshoreSeaSampler.Possible(path[lead - 1 + i], op, vessel, operability))
                {
                    done = i;
                    break;
                }
            if (done == 0) onBookedDay++;
            if (done < 0)
            {
                notDone++;
                chargedDays += horizonDays;
                continue;
            }
            chargedDays += done + 1;
            for (int i = done; i < horizonDays; i++) doneBy[i] += 1.0;
        }
        double n = futures.Length;
        for (int i = 0; i < horizonDays; i++) doneBy[i] /= n;
        decimal rate = OffshoreCharter.DayRateNok(op, vessel, OffshoreCal.Costs) * (decimal)unit.EquityShare;
        decimal perOp = OffshoreCharter.PerOperationNok(op, vessel, OffshoreCal.Costs) * (decimal)unit.EquityShare;
        double meanDays = chargedDays / n;
        decimal expected = rate * (decimal)meanDays + perOp * (decimal)(1.0 - notDone / n);
        return new OffshoreBookingPreview(day, onBookedDay / n, doneBy, meanDays, expected, rate, notDone / n,
            SiloDaysAt(unit, day - State.Day));
    }

    // ─── Reads ────────────────────────────────────────────────────────────

    /// <summary>The unit's feeding rate (t a day), the mean of its recent days; 0 while it feeds nothing.</summary>
    public static double FeedingRateTonnesPerDay(OffshoreUnit unit)
        => unit.RecentFeedTonnes.Count == 0 ? 0.0 : unit.RecentFeedTonnes.Average();

    /// <summary>Days of feed in the silo at the recent rate (+∞ while the unit feeds nothing).</summary>
    public static double SiloDaysOf(OffshoreUnit unit)
    {
        double rate = FeedingRateTonnesPerDay(unit);
        return rate <= 1e-9 ? double.PositiveInfinity : unit.SiloTonnes / rate;
    }

    private static double SiloDaysAt(OffshoreUnit unit, int daysAhead)
    {
        double rate = FeedingRateTonnesPerDay(unit);
        return rate <= 1e-9 ? double.PositiveInfinity : Math.Max(0.0, unit.SiloTonnes - rate * daysAhead) / rate;
    }

    /// <summary>
    /// An offshore unit's state as its gauges read it (spec §6.5): today's sea
    /// and appetite, silo and silage, the crew clock, the alarm load with its
    /// drivers, each board row's next green window, bookings, shakedown,
    /// the teething defects the player knows, sensors, and the structure
    /// and nets as last known. Null for an unknown site. Pure.
    /// </summary>
    public OffshoreUnitStateView? GetOffshoreUnitState(string siteId)
    {
        var site = FindOffshoreUnit(siteId);
        var unit = site?.Offshore;
        if (site == null || unit == null) return null;
        int today = State.Day;
        var drivers = AlarmLoadDrivers(unit, today);
        var windows = new List<(string, int, double)>();
        foreach (var (row, op, vessel) in SeaBoardRows)
        {
            if (op is OffshoreOp.Feeding or OffshoreOp.Tow) continue;
            var w = FirstWindow(site, op, vessel, GreenWindowProbability);
            windows.Add((row, w.Probability >= GreenWindowProbability ? w.Day : -1, Math.Max(0.0, w.Probability)));
        }
        var known = unit.TeethingDefects.Where(d => d.IsRevealed).ToList();
        return new OffshoreUnitStateView(site.Id, unit.IsCommissioned, unit.CurrentWaveHeight,
            OperabilityWindows.AppetiteFactor(unit.CurrentWaveHeight, OffshoreCal.Operability),
            unit.SiloTonnes, unit.SiloCapacityTonnes, SiloDaysOf(unit), unit.SiloEmptyDays, unit.SilageTonnes,
            unit.SilageCapacityTonnes, unit.DaysSinceCrewRotation, unit.CrewRotationDays, unit.CrewOverdueDays,
            unit.AlarmLoad, drivers, windows,
            unit.Bookings.OrderBy(b => b.IsOpen ? 0 : 1).ThenBy(b => b.BookedDay).ToList(),
            unit.ShakedownEndDay, known, unit.TeethingDefects.Count - known.Count, unit.Sensors.ToList(),
            unit.StructureKnown, unit.StructureKnownAsOfDay, unit.NetKnownPct, unit.NetKnownAsOfDay, unit.EquityShare,
            unit.ShakedownDays, TeethingFixCostNok(unit), BuyOutPriceNok(site),
            Enum.GetValues<SensorPackageKind>().Select(k => (k, SensorCalibration(k).InstallNok * (decimal)unit.EquityShare,
                SensorCalibration(k).ServiceNok * (decimal)unit.EquityShare)).ToList());
    }

    /// <summary>
    /// The alarm load's terms on <paramref name="day"/> (spec §3.3,
    /// <see cref="AlarmLoadModel"/>): base, defects, sensors, crew and, once the
    /// unit has had an alarm review, the review's cut (F2-08), never taking the
    /// sum below zero, so the terms add up to the load the gauges show.
    /// </summary>
    internal IReadOnlyList<(string Driver, double Value)> AlarmLoadDrivers(OffshoreUnit unit, int day)
    {
        var drivers = AlarmLoadModel.Drivers(unit, day, OffshoreCal.AlarmLoad, OffshoreCal.AlarmReview, OffshoreCal.Sensors.ServiceIntervalDays);
        var terms = new List<(string, double)>(drivers.Count);
        double others = 0.0;
        foreach (var d in drivers)
        {
            if (d.Name != "review")
            {
                terms.Add((d.Name, d.Value));
                others += d.Value;
            }
            else if (unit.AlarmReviewStartedDay >= 0)
            {
                terms.Add((d.Name, -Math.Min(-d.Value, Math.Max(0.0, others))));
            }
        }
        return terms;
    }

    // ─── The day (OffshoreTickSystem) ─────────────────────────────────────

    /// <summary>
    /// The Metocean phase's second half: an operational unit that never ran
    /// its logistics (built by the legacy verb, or migrated from a pre-F2
    /// save) is commissioned now, before its fish eat: its silo filled, the
    /// Driftsplan's offshore defaults set. A legacy unit draws no teething deck
    /// (one a scenario gives a deck commissions in its own day, which draws).
    /// </summary>
    internal void CommissionReadyLegacyUnits()
    {
        foreach (var site in State.SeaSites)
        {
            var unit = site.Offshore;
            if (site.Kind != SiteKind.Offshore || unit == null || unit.IsCommissioned || !unit.IsOperational) continue;
            if (OffshoreProjectOf(site) != null || unit.HasTeethingDeck) continue; // commissioned in its own day, with its deck
            Commission(site, Weather.SimulationDay, null);
        }
    }

    /// <summary>
    /// One offshore unit's day (FarmOpex, after the biology): its 8 uniforms,
    /// the construction and tow, then, once commissioned, crew, bookings,
    /// silo and silage, structure and nets, the teething deck, sensors, the
    /// alarm load, its own lines, its events and the partner settlement.
    /// </summary>
    internal void RunOffshoreUnitDay(SeaSite site)
    {
        var unit = site.Offshore!;
        int seaDay = Weather.SimulationDay;
        var stream = Rng.Stream(SimRngStreams.OffshoreFarm, site.Id);
        var u = new double[OffshoreUnitUniformsPerDay];
        for (int i = 0; i < u.Length; i++) u[i] = stream.NextDouble(); // draw, then decide: always 8

        var project = OffshoreProjectOf(site);
        if (project != null)
        {
            AdvanceTow(site, project, seaDay, u[0]);
            if (!unit.IsCommissioned)
            {
                if (project.Status == ConstructionStatus.Complete)
                    Commission(site, seaDay, u);
                else
                    unit.ConstructionProgress = Math.Min(0.999, GetOffshoreConstruction(site.Id)?.OverallProgress ?? 0.0);
            }
        }
        else
        {
            if (!unit.IsOperational)
                unit.AdvanceLegacyConstruction(State.Day); // the legacy unit's day count (commissioned in the next sea phase)
            else if (!unit.IsCommissioned && unit.HasTeethingDeck)
                Commission(site, seaDay, u); // a legacy-built unit given a deck (scenario or test) draws it today
        }
        if (!unit.IsCommissioned) return;

        double hs = unit.CurrentWaveHeight;
        unit.IsStormLockdown = hs > OffshoreCal.Operability.WellboatAlongsideHsM;
        unit.DaysSinceCrewRotation++;
        RunOffshoreBookings(site, seaDay);
        unit.CrewOverdueDays = Math.Max(0, unit.DaysSinceCrewRotation - unit.CrewRotationDays);

        bool hasFish = site.Pens.Any(p => p.TotalCount > 0);
        if (hasFish && unit.FirstStockedDay < 0) unit.FirstStockedDay = seaDay;
        TrackProductionCycle(site, unit, seaDay, hasFish);                  // F2-08/F2-09: completed cycles
        TrackFeeding(unit, seaDay, hasFish);
        CollectSilage(site, unit);
        WearStructureAndNets(site, unit, seaDay, hasFish);
        AdvanceTeething(site, unit, seaDay, hasFish, u);
        UpdateKnownState(site, unit, seaDay);
        CompleteOffshoreUpgrades(site, unit, seaDay);                       // F2-08: procedure, reinforcement
        unit.AlarmLoad = Math.Clamp(AlarmLoadDrivers(unit, seaDay).Sum(d => d.Value), 0.0, 1.0);
        RunOffshoreIncidentDay(site, unit, seaDay, hasFish, u);             // F2-08: u5-u7
        RunOffshoreHandlingDay(site, unit, seaDay);                         // F2-08: requirement, escalation, withdrawal
        BookOffshoreUnitLines(site, unit);
        PublishOffshoreWarnings(site, unit, seaDay, hasFish);
        SettlePartner(site, seaDay);
    }

    /// <summary>
    /// Commission a unit on <paramref name="seaDay"/>: operational, its silo
    /// filled (the first fill comes with the unit), its shakedown clock set,
    /// its teething deck drawn from the day's uniforms <paramref name="u"/>
    /// (a project-built unit; none for a legacy or migrated unit), its
    /// Driftsplan defaults applied.
    /// </summary>
    private void Commission(SeaSite site, int seaDay, double[]? u)
    {
        var unit = site.Offshore!;
        unit.ConstructionProgress = 1.0;
        unit.CommissionedDay = seaDay;
        unit.SiloTonnes = unit.SiloCapacityTonnes;
        unit.StructureKnownAsOfDay = seaDay;
        unit.NetKnownAsOfDay = seaDay;
        unit.StructureKnown = unit.StructuralIntegrity;
        unit.NetKnownPct = site.NetIntegrityPct;
        if (unit.ShakedownDays > 0) unit.ShakedownEndDay = seaDay + unit.ShakedownDays;
        if (u != null && unit.HasTeethingDeck && unit.TeethingDefects.Count == 0)
        {
            bool first = !OffshoreUnits.Any(o => o.Id != site.Id && o.Offshore?.FarmType == unit.FarmType && o.Offshore.HasTeethingDeck && o.Offshore.IsCommissioned);
            unit.TeethingDefects = TeethingDeck.Draw(site.Id, first, OffshoreCal.Teething, u[1], u[2], new[] { u[3], u[4], u[5], u[6] }, u[7]);
        }
        ApplyDriftsplanDefaults(site.Id);
        State.EventLog.Add(new EventRecord
        {
            Day = State.Day,
            Name = "Havmerd i drift",
            Category = EventCategory.Equipment,
            Description = unit.TeethingDefects.Count > 0
                ? SimText.Nb($"{site.Label} er i drift: siloen er fylt ({unit.SiloTonnes:N0} t), {unit.TeethingDefects.Count} ukjente barnesykdommer venter")
                : SimText.Nb($"{site.Label} er i drift: siloen er fylt ({unit.SiloTonnes:N0} t)"),
        });
        State.TrimEventLog();
    }

    /// <summary>
    /// The tow's day: a tow under way needs its sea within the tow limit; on a
    /// day above it the tow fails (the phase waits for the player again) and
    /// the unit shelters 5-15 days (uniform <paramref name="shelterDraw"/>),
    /// with no structural damage. The tugs are paid every tow and shelter day.
    /// </summary>
    private void AdvanceTow(SeaSite site, ConstructionProject project, int seaDay, double shelterDraw)
    {
        var unit = site.Offshore!;
        var costs = OffshoreCal.Costs;
        bool towing = project.Status == ConstructionStatus.Building && project.Current?.Key == OffshoreProfiles.TowPhase;
        bool sheltering = unit.TowShelterUntilDay >= seaDay;
        if (!towing && !sheltering) return;
        decimal tugs = costs.AnchorTugNokPerDay * costs.TowTugs * (decimal)unit.EquityShare;
        PayCost(tugs, CostBooking.Opex, "offshore_tow");
        RingFenceOwnLine(site, tugs);
        if (!towing || unit.CurrentWaveHeight <= OffshoreCal.Operability.TowHsM) return;

        int range = costs.TowShelterDaysMax - costs.TowShelterDaysMin + 1;
        int shelter = costs.TowShelterDaysMin + Math.Clamp((int)(shelterDraw * range), 0, range - 1);
        unit.TowShelterUntilDay = seaDay + shelter - 1;
        unit.TowFailures++;
        unit.TowStartedDay = -1;
        project.Status = ConstructionStatus.Waiting;
        project.PlayerStartRequested = false;
        project.PhaseStartDay = -1;
        project.PhaseDays = 0;
        State.EventLog.Add(new EventRecord
        {
            Day = State.Day,
            Name = "Slepet søker ly",
            Category = EventCategory.Weather,
            Description = SimText.Nb($"Sjøen ved {site.Label} gikk over {OffshoreCal.Operability.TowHsM:F1} m ({unit.CurrentWaveHeight:F1} m): slepet søker ly i {shelter} døgn, slepebåtene betales"),
        });
        State.TrimEventLog();
    }

    /// <summary>Each open booking whose day has come: its charter for today, then done if the sea allows, else it slips.</summary>
    private void RunOffshoreBookings(SeaSite site, int seaDay)
    {
        var unit = site.Offshore!;
        var operability = OffshoreCal.Operability;
        var costs = OffshoreCal.Costs;
        foreach (var booking in unit.Bookings.Where(b => b.IsOpen && b.BookedDay <= seaDay)
                     .OrderBy(b => b.BookedDay).ThenBy(b => b.Id, StringComparer.Ordinal).ToList())
        {
            if (!booking.IsOpen) continue; // a crew change done today closed its twin
            decimal charter = OffshoreCharter.DayRateNok(booking.Op, booking.Vessel, costs) * (decimal)unit.EquityShare;
            if (charter > 0m)
            {
                PayCost(charter, CostBooking.Opex, "offshore_charter");
                RingFenceOwnLine(site, charter);
                booking.DaysCharged++;
                booking.ChargedNok += charter;
            }
            if (!OffshoreOperationPossibleToday(site, booking.Op, booking.Vessel))
            {
                booking.Status = !OperabilityWindows.IsFishHandling(booking.Op) ? OffshoreBookingStatus.Waiting
                    : OperabilityWindows.WellboatCanStayAlongside(unit.CurrentWaveHeight, operability) ? OffshoreBookingStatus.Paused
                    : OffshoreBookingStatus.Sheltering;
                continue;
            }
            var outcome = ExecuteOffshoreBooking(site, booking, seaDay);
            if (outcome.Ok && booking.Op == OffshoreOp.Delousing && outcome.Value2 > 0)
            {
                booking.Status = OffshoreBookingStatus.InProgress; // zones left for the next window day (F2-08)
                continue;
            }
            booking.CompletedDay = seaDay;
            if (!outcome.Ok)
            {
                booking.Status = OffshoreBookingStatus.Failed;
                booking.FailureKey = outcome.ErrorKey;
                continue;
            }
            booking.Status = OffshoreBookingStatus.Done;
            decimal perOp = OffshoreCharter.PerOperationNok(booking.Op, booking.Vessel, costs) * (decimal)unit.EquityShare;
            if (perOp > 0m)
            {
                PayCost(perOp, CostBooking.Opex, "offshore_charter");
                RingFenceOwnLine(site, perOp);
                booking.ChargedNok += perOp;
            }
            if (booking.Op == OffshoreOp.CrewChange)
                foreach (var twin in unit.Bookings.Where(b => b.IsOpen && b.Op == OffshoreOp.CrewChange))
                {
                    twin.Status = OffshoreBookingStatus.Cancelled;
                    twin.CompletedDay = seaDay;
                }
        }
    }

    /// <summary>The work a booked vessel does on its window day.</summary>
    private CommandResult ExecuteOffshoreBooking(SeaSite site, OffshoreBooking booking, int seaDay)
    {
        var unit = site.Offshore!;
        switch (booking.Op)
        {
            case OffshoreOp.Resupply:
                unit.SiloTonnes = unit.SiloCapacityTonnes;
                return CommandResult.Success("resupplied");
            case OffshoreOp.Silage:
                unit.SilageTonnes = 0.0;
                return CommandResult.Success("silage collected");
            case OffshoreOp.CrewChange:
                unit.DaysSinceCrewRotation = 0;
                unit.CrewOverdueDays = 0;
                return CommandResult.Success("crew changed");
            case OffshoreOp.NetInspection:
                unit.NetKnownPct = site.NetIntegrityPct;
                unit.StructureKnown = unit.StructuralIntegrity;
                unit.NetKnownAsOfDay = seaDay;
                unit.StructureKnownAsOfDay = seaDay;
                return CommandResult.Success("inspected");
            case OffshoreOp.Stocking:
                if (unit.ShakedownEndDay > seaDay)
                    return CommandResult.Fail(OffshoreReasons.ShakedownActive, "shakedown");
                return StockOffshoreFarm(site.Id, booking.Count, booking.Detail);
            case OffshoreOp.Delousing:
                return DelouseUnitZones(site, booking);
            default:
                return CommandResult.Fail("not_bookable", booking.Op.ToString());
        }
    }

    /// <summary>
    /// A production cycle runs from the day fish come into an empty unit until
    /// it is empty again; it is completed when fish were harvested from it in
    /// that time (the development-licence round's and the conversion's
    /// "completed cycle", the Havfarm-class gate).
    /// </summary>
    private void TrackProductionCycle(SeaSite site, OffshoreUnit unit, int seaDay, bool hasFish)
    {
        if (hasFish)
        {
            if (unit.CycleStartDay < 0) unit.CycleStartDay = seaDay;
            return;
        }
        if (unit.CycleStartDay < 0) return;
        int start = unit.CycleStartDay;
        if (HarvestReports.Any(r => r.HoldingId == site.Id && r.Day >= start)) unit.CompletedCycles++;
        unit.CycleStartDay = -1;
    }

    /// <summary>
    /// A window day of a booked delousing (F2-08): the wellboat treats the
    /// zones not yet treated, in zone order, up to the day's capacity of fish
    /// (at least one zone); the result's second value is the zones still to
    /// treat (0 when the unit is done).
    /// </summary>
    private CommandResult DelouseUnitZones(SeaSite site, OffshoreBooking booking)
    {
        double capacityKg = OffshoreCal.Handling.DelousingTonnesPerHandlingDay * 1000.0;
        var left = site.PhysicalPens.OrderBy(p => p.Number)
            .Select(p => (Pen: p, Pop: site.PopulationIn(p)))
            .Where(z => z.Pop != null && z.Pop.TotalCount > 0 && !booking.TreatedZones.Contains(z.Pen.Id))
            .ToList();
        if (left.Count == 0)
            return booking.TreatedZones.Count > 0 ? CommandResult.Success("deloused", 0, 0) : DelouseSite(site.Id, booking.Detail);
        var today = new List<string>();
        double kg = 0.0;
        foreach (var (pen, pop) in left)
        {
            double zoneKg = pop!.GetTotalBiomassKg();
            if (today.Count > 0 && kg + zoneKg > capacityKg) break;
            today.Add(pen.Id);
            kg += zoneKg;
        }
        var result = DelousePens(site.Id, booking.Detail, today);
        if (!result.Ok) return result;
        booking.TreatedZones.AddRange(today);
        int remaining = left.Count - today.Count;
        return CommandResult.Success($"{today.Count} zone(s) deloused, {remaining} left", today.Count, remaining, site.Id);
    }

    // ─── Feed, ring-fence and the partner ─────────────────────────────────

    /// <summary>
    /// The feed an offshore population ate today leaves its unit's silo (called
    /// by the growth step for an offshore holding, after the feed is booked).
    /// </summary>
    internal void TakeOffshoreFeed(FishPopulation population, double feedKg)
    {
        var unit = HoldingOf(population).Site?.Offshore;
        if (unit == null || !unit.IsCommissioned || feedKg <= 0.0) return;
        int seaDay = Weather.SimulationDay;
        if (unit.LastFeedDay != seaDay)
        {
            unit.LastFeedDay = seaDay;
            unit.FeedTodayTonnes = 0.0;
        }
        unit.FeedTodayTonnes += feedKg / 1000.0;
        unit.SiloTonnes = Math.Max(0.0, unit.SiloTonnes - feedKg / 1000.0);
    }

    /// <summary>
    /// A cost another system booked in full for an offshore unit (its feed, its
    /// smolts, a permit fee; F2-04's <see cref="AttributeUnitCost"/> reports
    /// it): a consortium unit's partner owes its share, settled with the day.
    /// </summary>
    internal void NotePartnerShareOfUnitCost(string unitId, HoldingKind kind, decimal amount)
    {
        if (kind != HoldingKind.Offshore || amount <= 0m) return;
        var unit = FindOffshoreUnit(unitId)?.Offshore;
        if (unit == null || !unit.IsConsortium) return;
        unit.PartnerPendingNok += amount * (decimal)(1.0 - unit.EquityShare);
    }

    /// <summary>
    /// The F2-04 rent ring-fence for a line the unit books itself (already at
    /// its equity share, so it bypasses the partner's note): out of the rent
    /// base when the unit is not liable.
    /// </summary>
    internal void RingFenceOwnLine(SeaSite site, decimal amount)
    {
        if (State == null || amount <= 0m) return;
        if (!IsResourceRentLiable(site.Id, HoldingKind.Offshore))
            State.RentRingFencedCostsThisTaxYear += amount;
    }

    /// <summary>
    /// The day's settlement with a consortium partner (spec §3.3 "a
    /// partner-settlement line for the rest"): the partner pays its share of
    /// the costs booked in full for the unit and takes its share of the net of
    /// the unit's harvests reported through yesterday, one line a day.
    /// </summary>
    internal void SettlePartner(SeaSite site, int seaDay)
    {
        var unit = site.Offshore!;
        if (!unit.IsConsortium && unit.PartnerPendingNok == 0m)
        {
            unit.PartnerSettledThroughDay = seaDay - 1;
            return;
        }
        decimal partnerShare = (decimal)(1.0 - unit.EquityShare);
        foreach (var report in HarvestReports)
            if (report.HoldingId == site.Id && report.Day > unit.PartnerSettledThroughDay && report.Day <= seaDay - 1)
                unit.PartnerPendingNok -= report.NetRevenueNok * partnerShare;
        unit.PartnerSettledThroughDay = seaDay - 1;
        decimal pending = unit.PartnerPendingNok;
        unit.PartnerPendingNok = 0m;
        if (pending > 0m) ReceiveIncome(pending, "partner_settlement");
        else if (pending < 0m) PayCost(-pending, CostBooking.Opex, "partner_settlement");
    }

    /// <summary>
    /// Whether an S2-04 harvest order's batch may be slaughtered today: always
    /// at a fjord site or a land hall; at an offshore unit only when its sea
    /// allows the offshore wellboat to handle fish (the F2-06 hook,
    /// <see cref="OffshoreHooks.HarvestOrderJobKind"/>). A day it does not, the
    /// order waits (the fast goes on), as a wellboat alongside pauses pumping.
    /// </summary>
    internal bool SeaAllowsHarvestToday(string holdingId, HoldingKind kind)
        => kind != HoldingKind.Offshore
           || OperationPossible(State.SeaSites.FirstOrDefault(s => s.Id == holdingId), OffshoreHooks.HarvestOrderJobKind, State.Day);

    /// <summary>
    /// Refuse fish handling at an offshore unit today when its sea is above the
    /// standard limit (the legacy bridge verbs' guard: the old offshore panel
    /// stocks and harvests at once); null when the work may go ahead or the
    /// site is no offshore unit.
    /// </summary>
    public CommandResult? OffshoreFishHandlingRefusal(string siteId, OffshoreOp op)
    {
        var site = FindOffshoreUnit(siteId);
        if (site?.Offshore == null || !site.Offshore.IsCommissioned) return null;
        return OffshoreOperationPossibleToday(site, op, VesselClass.Standard)
            ? null
            : CommandResult.Fail(OffshoreReasons.NoWindow,
                $"{site.Label}: the sea ({site.Offshore.CurrentWaveHeight:F1} m) is above the fish-handling limit today");
    }
}
