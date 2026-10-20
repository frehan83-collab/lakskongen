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
using LakskongenSim.Tests.Integration;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Farm;

/// <summary>
/// Roadmap F2-08 (docs/FARM_TYPES_SPEC.md §7.1 F2-08 acceptance 1-4, 8, 9): an
/// offshore unit's incidents (the tilt chain, net tears, mooring failures),
/// their escapes into the escape record, the fees and the sustainability score,
/// the tilt hazard's terms, the standing order's auto-close, the incident
/// reports' causal chain, the 21/45/75-day escalation with the withdrawal that
/// keeps the licences, and a save and twin in the middle of an incident.
/// Acceptance 5-7 are in <c>OffshoreHandlingProbeTests</c> and
/// <c>OffshoreLiceCouplingTests</c>.
/// </summary>
public class OffshoreIncidentTests
{
    private static readonly OffshoreCalibration Cal = OffshoreCalibration.Default;
    private readonly ITestOutputHelper _out;

    public OffshoreIncidentTests(ITestOutputHelper output) => _out = output;

    /// <summary>A commissioned unit with its standing orders cleared and <paramref name="fish"/> fish in each zone.</summary>
    private static (SimulationEngine Engine, SeaSite Site) StockedUnit(int seed, int fish = 300_000, bool events = false, bool deck = false)
    {
        var engine = OffshoreTestUnits.RichEngine(seed, events);
        var site = OffshoreTestUnits.Commissioned(engine, deck);
        OffshoreTestUnits.ClearDriftsplan(engine, site.Id);
        for (int z = 0; z < site.PhysicalPens.Count; z++)
            Assert.True(engine.StockOffshoreFarm(site.Id, fish).Ok);
        engine.IncidentHazardOverrideForTests = (s, k) => 0.0; // no incident until a test asks for one
        return (engine, site);
    }

    /// <summary>Force one incident kind tomorrow (hazard 1), nothing else.</summary>
    private static void ForceTomorrow(SimulationEngine engine, OffshoreIncidentKind kind)
    {
        int tomorrow = engine.State.Day + 1;
        engine.IncidentHazardOverrideForTests = (s, k) => engine.Weather.SimulationDay == tomorrow ? (k == kind ? 1.0 : 0.0) : 0.0;
    }

    // ── Acceptance 1: Hs alone never causes an escape ───────────────────────

    /// <summary>
    /// Acceptance 1, first half: a year in a heavy sea that stays within the
    /// unit's design (4.0-5.4 m every day: the nets wear, the structure tires,
    /// the fjord's storms rage) releases no fish while the tilt chain and the
    /// nets hold (their hazards held at 0): no escape record, no escape in the
    /// mortality ledger, no fine. The sea is no escape path of its own.
    /// </summary>
    [Fact]
    public void HsAlone_NeverReleasesAFish_WithinTheDesignSea()
    {
        var (engine, site) = StockedUnit(81, events: true);
        engine.SeaOverrideForTests = (s, d) => 4.0 + 1.4 * ((d * 7919) % 100) / 100.0;
        engine.IncidentHazardOverrideForTests = (s, k) => k == OffshoreIncidentKind.MooringFailure ? null : 0.0;
        var fines = new List<decimal>();
        engine.PnlBooked += (day, category, amount, income) => { if (category is "fines" or "escape_measures") fines.Add(amount); };
        for (int d = 0; d < 365; d++)
        {
            engine.Advance(1);
            site.NetIntegrityPct = Math.Max(site.NetIntegrityPct, 80.0); // a crew that mends: the net never reaches tearing
        }
        Assert.True(site.Offshore!.CurrentWaveHeight > 4.0 && site.Offshore.CurrentWaveHeight < site.Offshore.DesignHsM);
        Assert.Empty(site.EscapeIncidents);
        Assert.Equal(0, site.EscapesThisYear);
        Assert.Equal(0L, engine.MortalityLedger.ForSite(site.Id).Removed(FishRemoval.Escape).Fish);
        Assert.Empty(fines);
        Assert.Empty(engine.OffshoreIncidentsOf(site.Id));
    }

    /// <summary>
    /// Acceptance 1, second half: over two years of the model's own sea at
    /// off_1 with its own hazards (events on, nets left to wear, the teething
    /// deck drawn), every fish that left the unit alive through an escape
    /// belongs to an incident: the site's cumulative escape count, the ledger's
    /// escapes and the incidents' counts are one number; each record in the
    /// escape score's rolling year is one incident's (its day, all its fish,
    /// leaks included), and the record holds every incident of the year up to
    /// the last one (older ones age out of the 365-day window as they always did).
    /// </summary>
    [Fact]
    public void EveryEscapeAtAUnit_BelongsToAnIncident()
    {
        int escapedTotal = 0;
        foreach (int seed in new[] { 82, 83, 84 })
        {
            var engine = OffshoreTestUnits.RichEngine(seed, events: true);
            var site = OffshoreTestUnits.Commissioned(engine, deck: true); // its standing orders on: crew rota, ROV, resupply
            for (int z = 0; z < site.PhysicalPens.Count; z++)
                Assert.True(engine.StockOffshoreFarm(site.Id, 300_000).Ok);
            engine.Advance(730);
            var incidents = engine.OffshoreIncidentsOf(site.Id);
            long ledger = engine.MortalityLedger.ForSite(site.Id).Removed(FishRemoval.Escape).Fish;
            int recorded = site.EscapeIncidents.Sum(e => e.Fish);
            _out.WriteLine($"seed {seed}: {incidents.Count} incidents ({string.Join(", ", incidents.Select(i => $"{i.Kind} day {i.StartDay} {i.EscapedFish}"))}), ledger {ledger}, record {recorded}, net {site.NetIntegrityPct:F0} %");
            Assert.Equal(incidents.Sum(i => (long)i.EscapedFish), ledger);
            Assert.Equal(ledger, site.EscapesThisYear);
            var escaping = incidents.Where(i => i.EscapedFish > 0).ToList();
            int lastRecord = escaping.Count == 0 ? 0 : escaping.Max(i => i.EscapeRecordDay);
            var inWindow = escaping.Where(i => i.EscapeRecordDay > lastRecord - SeaSite.EscapeWindowDays).ToList();
            Assert.Equal(inWindow.Count, site.EscapeIncidents.Count);
            Assert.Equal(inWindow.Sum(i => i.EscapedFish), recorded);
            Assert.All(site.EscapeIncidents, e => Assert.Equal(inWindow.Single(i => i.EscapeRecordDay == e.Day).EscapedFish, e.Fish));
            escapedTotal += (int)ledger;
        }
        _out.WriteLine($"escaped over three seeds: {escapedTotal}");
    }

    /// <summary>
    /// Acceptance 1: a tilt, a tear and a mooring failure each take their fish
    /// out through the mortality ledger as an escape, put one incident in the
    /// site's escape record (the escape score and the sustainability score read
    /// it), and book the Directorate's fee (per fish, up to 15 G) and the
    /// recapture and river monitoring.
    /// </summary>
    [Theory]
    [InlineData(OffshoreIncidentKind.Tilt)]
    [InlineData(OffshoreIncidentKind.NetTear)]
    [InlineData(OffshoreIncidentKind.MooringFailure)]
    public void EachIncidentsEscape_ReachesTheEscapeRecord_TheFeesAndSustainability(OffshoreIncidentKind kind)
    {
        var (engine, site) = StockedUnit(85);
        engine.Advance(20);
        if (kind == OffshoreIncidentKind.MooringFailure)
            engine.SeaOverrideForTests = (s, d) => 7.0; // above OF1-class's 5.5 m design
        if (kind == OffshoreIncidentKind.NetTear)
            site.NetIntegrityPct = 40.0;
        var booked = new Dictionary<string, decimal>(StringComparer.Ordinal);
        engine.PnlBooked += (day, category, amount, income) => booked[category] = (booked.TryGetValue(category, out var v) ? v : 0m) - amount;
        int before = site.Pens.Sum(p => p.TotalCount);
        int incidentsInYear = engine.State.SeaSites.Sum(s => s.EscapeIncidentsInLastYear(engine.State.Day));
        ForceTomorrow(engine, kind);
        engine.Advance(1);
        engine.IncidentHazardOverrideForTests = (s, k) => 0.0;

        var incident = Assert.Single(engine.OffshoreIncidentsOf(site.Id));
        Assert.Equal(kind, incident.Kind);
        Assert.True(incident.EscapedFish > 0, "no fish escaped");
        Assert.True(before - site.Pens.Sum(p => p.TotalCount) >= incident.EscapedFish, "the escaped fish are still in the zones");
        Assert.Equal(incident.EscapedFish, engine.MortalityLedger.ForSite(site.Id).Removed(FishRemoval.Escape).Fish);
        var record = Assert.Single(site.EscapeIncidents);
        Assert.Equal((incident.EscapeRecordDay, incident.EscapedFish), (record.Day, record.Fish));
        Assert.Equal(incidentsInYear + 1, engine.State.SeaSites.Sum(s => s.EscapeIncidentsInLastYear(engine.State.Day)));
        decimal ceiling = CalibrationRegistry.Default.Capacity.InfringementFeeCeilingNok;
        decimal fee = Math.Min(ceiling, incident.EscapedFish * Cal.Incidents.EscapeFeeNokPerFish);
        Assert.Equal(fee, incident.FeeNok);
        Assert.Equal(fee, booked["fines"]);
        Assert.Equal(incident.EscapedFish * Cal.Incidents.EscapeMeasuresNokPerFish, booked["escape_measures"]);
        Assert.Equal(site.Id, Assert.Single(engine.IncidentReportsOf(site.Id)).UnitId);
        _out.WriteLine($"{kind}: {incident.EscapedFish} fish ({incident.EscapeShare:P2}), fee {fee:N0}, measures {booked["escape_measures"]:N0}");
    }

    // ── Acceptance 2: the tilt hazard's terms ──────────────────────────────

    /// <summary>
    /// Acceptance 2: the tilt hazard is (1/9000)·(2 − procedure)·(1 + 2·alarm
    /// load)·(1 + 0.05·crew overdue days)·teething, teething ×3 while a revealed
    /// ballast-pipe leak is unfixed with fish in the unit; the unit's driver list
    /// (the bridge's) holds exactly those terms in that order, and the hazard
    /// the day's pick uses is their product (recorded on a forced tilt).
    /// </summary>
    [Fact]
    public void TheTiltHazard_FollowsTheSpecsTerms_AndTheDriverListIsThem()
    {
        var (engine, site) = StockedUnit(86);
        engine.Advance(10);
        var unit = site.Offshore!;
        int day = engine.Weather.SimulationDay;
        unit.TeethingDefects.Add(new TeethingDefect { Id = site.Id + "/td_9", Kind = TeethingDefectKind.BallastPipeLeak, RevealedDay = day - 5 });
        unit.HatchProcedureLevel = 0.6;
        unit.AlarmLoad = 0.3;
        unit.CrewOverdueDays = 2;
        var drivers = engine.TiltHazardDrivers(site);
        Assert.Equal(TiltHazardModel.DriverKeys, drivers.Select(d => d.Name));
        Assert.Equal(new[] { 1.0 / 9000.0, 1.4, 1.6, 1.1, 3.0 }, drivers.Select(d => d.Value).ToArray(), new ToleranceComparer(1e-12));
        double h = 1.0 / 9000.0 * 1.4 * 1.6 * 1.1 * 3.0;
        Assert.Equal(h, TiltHazardModel.Of(drivers), 15);
        // Spec §3.3: "≈ 10 % per year in a teething year and ≈ 5 % per year once fixed".
        var fixedUnit = new OffshoreUnit { HatchProcedureLevel = 0.6, AlarmLoad = 0.15 };
        double fixedYear = 1.0 - Math.Pow(1.0 - TiltHazardModel.Of(TiltHazardModel.Drivers(fixedUnit, day, true, Cal.Incidents, Cal.Teething)), 365);
        _out.WriteLine($"teething year {1.0 - Math.Pow(1.0 - h, 365):P1}, fixed {fixedYear:P1}");
        Assert.InRange(fixedYear, 0.04, 0.10);
        // The fixed leak drops the teething term; a ship-shaped unit has no tilt chain.
        unit.TeethingDefects.Single().FixedDay = day;
        Assert.Equal(1.0, engine.TiltHazardDrivers(site).Single(d => d.Name == "teething").Value);
        Assert.Equal(0.0, TiltHazardModel.Of(TiltHazardModel.Drivers(new OffshoreUnit { FarmType = OffshoreType.ShipShaped }, day, true, Cal.Incidents, Cal.Teething)));

        // The engine's own pick: a uniform below the hazard starts a tilt with that hazard on record.
        unit.TeethingDefects.Single().FixedDay = -1;
        engine.IncidentHazardOverrideForTests = (s, k) => k == OffshoreIncidentKind.Tilt ? null : 0.0;
        double expected = 0.0;
        int start = engine.State.Day;
        for (int d = 0; d < 4000 && engine.OffshoreIncidentsOf(site.Id).Count == 0; d++)
        {
            unit.HatchProcedureLevel = 0.6;
            expected = TiltHazardModel.Of(engine.TiltHazardDrivers(site));
            engine.Advance(1);
        }
        var tilt = Assert.Single(engine.OffshoreIncidentsOf(site.Id));
        Assert.Equal(OffshoreIncidentKind.Tilt, tilt.Kind);
        Assert.Equal(TiltHazardModel.Of(tilt.HazardDrivers), tilt.HazardPerDay, 15);
        _out.WriteLine($"first tilt after {engine.State.Day - start} days, hazard {tilt.HazardPerDay:E3} (the day before {expected:E3})");
    }

    private sealed class ToleranceComparer : IEqualityComparer<double>
    {
        private readonly double _tol;
        public ToleranceComparer(double tol) => _tol = tol;
        public bool Equals(double x, double y) => Math.Abs(x - y) <= _tol;
        public int GetHashCode(double obj) => 0;
    }

    // ── Acceptance 3: the standing order's auto-close ──────────────────────

    /// <summary>
    /// Acceptance 3: with the hatch procedure at 0.8, the crew on board and
    /// none overdue, the standing order closes the hatches when the ballast
    /// deviation is caught, so auto-close succeeds with p = 1 − alarm load:
    /// over 2,000 forced tilts (a tilt every day, the investigation closing
    /// the next day) at an alarm load of 0.35 (two sensor packages past their
    /// service) the share is 0.65 ± 0.03; each auto-closed tilt lasts the hour.
    /// Below the procedure level none closes by the order, whatever was caught.
    /// </summary>
    [Fact]
    public void AutoClose_SucceedsWithOneMinusTheAlarmLoad_OverTwoThousandForcedTilts()
    {
        var engine = OffshoreTestUnits.RichEngine(87);
        var site = OffshoreTestUnits.Commissioned(engine);
        var unit = site.Offshore!;
        engine.OffshoreCalOverrideForTests = Cal with { Incidents = Cal.Incidents with { InvestigationMaxDays = 1 } };
        OffshoreTestUnits.ClearDriftsplan(engine, site.Id);
        Assert.True(engine.SetDriftsplan(site.Id, OffshorePolicies.NetInspection).Ok); // the nets inspected monthly: no inspection overdue
        unit.CrewRotationDays = 1_000_000; // the crew is never overdue
        unit.SiloCapacityTonnes = unit.SiloTonnes = 1e9; // fed throughout
        site.MABTonnes = 1e9;
        for (int z = 0; z < site.PhysicalPens.Count; z++)
            Assert.True(engine.StockOffshoreFarm(site.Id, 4_000_000).Ok);
        Assert.True(engine.InstallSensorPackage(site.Id, SensorPackageKind.Mru).Ok);
        Assert.True(engine.InstallSensorPackage(site.Id, SensorPackageKind.Echo).Ok);
        foreach (var s in unit.Sensors) s.LastServiceDay = -1000;
        engine.SeaOverrideForTests = (s, d) => 1.0;
        unit.HatchProcedureLevel = 0.8;
        engine.IncidentHazardOverrideForTests = (s, k) => k == OffshoreIncidentKind.Tilt ? 1.0 : 0.0;
        engine.Advance(2100);
        var tilts = engine.OffshoreIncidentsOf(site.Id).Where(i => i.Kind == OffshoreIncidentKind.Tilt && i.StandingOrderReady).ToList();
        Assert.True(tilts.Count >= 2000, $"only {tilts.Count} tilts with the standing order ready");
        Assert.All(tilts, t => Assert.Equal(0.35, t.AlarmLoad, 12));
        double share = tilts.Count(t => t.AutoClosed) / (double)tilts.Count;
        _out.WriteLine($"{tilts.Count} tilts, auto-closed {share:P2} (1 - alarm load = 65 %); fish left {site.Pens.Sum(p => p.TotalCount):N0}");
        Assert.InRange(share, 0.65 - 0.03, 0.65 + 0.03);
        Assert.All(tilts.Where(t => t.AutoClosed), t => Assert.Equal(Cal.Incidents.AutoCloseHours, t.DurationHours));
        Assert.All(tilts.Where(t => !t.DeviationCaught), t => Assert.False(t.AutoClosed));

        // Below 0.8 the order cannot act: no tilt closes by it.
        unit.HatchProcedureLevel = 0.6;
        int seen = engine.OffshoreIncidentsOf(site.Id).Count;
        engine.Advance(200);
        var low = engine.OffshoreIncidentsOf(site.Id).Skip(seen).Where(i => i.Kind == OffshoreIncidentKind.Tilt).ToList();
        Assert.True(low.Count > 150);
        Assert.All(low, t => Assert.False(t.StandingOrderReady || t.AutoClosed));
        Assert.Contains(low, t => t.DeviationCaught);
    }

    // ── Acceptance 8: the incident report ──────────────────────────────────

    /// <summary>
    /// Acceptance 8: the report of a tilt with an unfixed ballast-pipe leak has
    /// at least three causal steps in order (the defect, the hazard and its
    /// drivers, the alarm load and its drivers, the deviation, the duration,
    /// the escape), and each reconciles to its record: the defect's reveal day,
    /// the incident's drivers, the escape record's fish and day, the ledger's
    /// escapes, fees and measures.
    /// </summary>
    [Fact]
    public void ATiltsReport_HasItsCausalChain_AndEveryStepReconcilesToItsRecord()
    {
        var (engine, site) = StockedUnit(88);
        engine.Advance(10);
        var unit = site.Offshore!;
        int day = engine.Weather.SimulationDay;
        var leak = new TeethingDefect { Id = site.Id + "/td_9", Kind = TeethingDefectKind.BallastPipeLeak, RevealedDay = day - 3 };
        unit.TeethingDefects.Add(leak);
        var booked = new Dictionary<string, decimal>(StringComparer.Ordinal);
        engine.PnlBooked += (d, category, amount, income) => booked[category] = (booked.TryGetValue(category, out var v) ? v : 0m) - amount;
        ForceTomorrow(engine, OffshoreIncidentKind.Tilt);
        engine.Advance(1);
        var incident = Assert.Single(engine.OffshoreIncidentsOf(site.Id));
        var report = Assert.Single(engine.IncidentReportsOf(site.Id));
        foreach (var s in report.Steps) _out.WriteLine($"{s.Order}. [{s.Key}] {s.Text} ({s.RecordRef})");
        Assert.True(report.Steps.Count >= 3);
        Assert.Equal(Enumerable.Range(1, report.Steps.Count), report.Steps.Select(s => s.Order));
        Assert.Equal(new[] { "defect", "hazard", "alarm_load", "deviation", "duration", "escape" }, report.Steps.Select(s => s.Key));
        Assert.Equal(("tilt", incident.Id), (report.Kind, report.IncidentId));
        var defect = report.Steps[0];
        Assert.Equal(("defect:" + leak.Id, leak.RevealedDay), (defect.RecordRef, defect.Day));
        var hazard = report.Steps[1];
        Assert.Equal(incident.HazardPerDay, hazard.Value);
        Assert.Equal(TiltHazardModel.DriverKeys, hazard.Drivers.Select(d => d.Name));
        Assert.Equal(3.0, hazard.Drivers.Single(d => d.Name == "teething").Value);
        var alarm = report.Steps[2];
        Assert.Equal(incident.AlarmLoad, alarm.Value);
        Assert.Equal(alarm.Value, Math.Clamp(alarm.Drivers.Sum(d => d.Value), 0.0, 1.0), 12);
        Assert.Equal(incident.DeviationCaught ? 1.0 : 0.0, report.Steps[3].Value);
        Assert.Equal(incident.DurationHours, report.Steps[4].Value);
        var escape = report.Steps[5];
        var record = Assert.Single(site.EscapeIncidents);
        Assert.Equal($"escape:{site.Id}:{record.Day}", escape.RecordRef);
        Assert.Equal(record.Fish, (int)escape.Value);
        Assert.Equal(report.FishLost, engine.MortalityLedger.ForSite(site.Id).Removed(FishRemoval.Escape).Fish);
        Assert.Equal(report.CostNok, booked["fines"] + booked["escape_measures"]);
        Assert.Contains(engine.State.EventLog, e => e.Id == report.Id);
        Assert.Equal(incident.BallastDefectId.Length > 0 ? "fix_defect" : "hatch_procedure", report.ProcedureUnlocked);
    }

    /// <summary>
    /// A tear leaks its zone every day until the net is mended in a window
    /// (the repair verb, 3.0 m); its leaks join its one escape record and its
    /// fee grows to the 15 G ceiling at most; mended and inspected, the
    /// investigation closes and a new incident may start.
    /// </summary>
    [Fact]
    public void ATear_LeaksUntilMended_InOneEscapeRecord_ThenCloses()
    {
        var (engine, site) = StockedUnit(89);
        engine.Advance(5);
        site.NetIntegrityPct = 30.0;
        engine.SeaOverrideForTests = (s, d) => 3.5; // above the repair limit
        ForceTomorrow(engine, OffshoreIncidentKind.NetTear);
        engine.Advance(1);
        engine.IncidentHazardOverrideForTests = (s, k) => 0.0;
        var tear = Assert.Single(engine.OffshoreIncidentsOf(site.Id));
        int first = tear.EscapedFish;
        Assert.Equal(OffshoreReasons(), engine.RepairOffshore(site.Id, "net").ErrorKey);
        engine.Advance(4);
        Assert.Equal(4, tear.LeakDays);
        Assert.True(tear.EscapedFish > first);
        var record = Assert.Single(site.EscapeIncidents);
        Assert.Equal(tear.EscapedFish, record.Fish);
        Assert.Equal(tear.EscapedFish, engine.MortalityLedger.ForSite(site.Id).Removed(FishRemoval.Escape).Fish);
        Assert.True(tear.IsOpen);
        engine.SeaOverrideForTests = (s, d) => 1.0;
        engine.Advance(1);
        Assert.True(engine.RepairOffshore(site.Id, "net").Ok);
        Assert.True(tear.Mended);
        int leaked = tear.EscapedFish;
        engine.Advance(1);
        Assert.Equal(leaked, tear.EscapedFish);
        Assert.False(tear.IsOpen); // the repair left the nets known as of today, after the tear
        Assert.Equal("", site.Offshore!.ActiveIncidentId);
    }

    private static string OffshoreReasons() => SimulationEngine.OffshoreReasons.NoWindow;

    // ── Acceptance 9: save and twin mid-incident ───────────────────────────

    /// <summary>
    /// Acceptance 9: a game saved the day after a tilt (its investigation open,
    /// its branches and report saved) and a game saved mid-tear (leaking)
    /// resume exactly as their twins over 60 days.
    /// </summary>
    [Theory]
    [InlineData(OffshoreIncidentKind.Tilt)]
    [InlineData(OffshoreIncidentKind.NetTear)]
    public void SaveAndTwin_MidIncident_AreIdentical(OffshoreIncidentKind kind)
    {
        SimulationEngine Mid()
        {
            var (engine, site) = StockedUnit(90, events: true, deck: true);
            engine.Advance(15);
            if (kind == OffshoreIncidentKind.NetTear) site.NetIntegrityPct = 35.0;
            ForceTomorrow(engine, kind);
            engine.Advance(1);
            engine.IncidentHazardOverrideForTests = null;
            Assert.True(engine.ActiveIncidentOf(site)?.IsOpen == true);
            return engine;
        }
        var engine = Mid();
        var twin = Mid();
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
        Assert.Equal(engine.State.OffshoreIncidents.Count, resumed.State.OffshoreIncidents.Count);
    }

    // ── The counter-verbs ──────────────────────────────────────────────────

    /// <summary>
    /// The alarm review (4 M NOK, 30 days) takes 0.30 off the alarm load for a
    /// year once done, shown as its own driver that never takes the terms
    /// below zero; the hatch procedure upgrade raises the level by 0.2 after 60
    /// days; the reinforcement adds 1.0 m of design Hs after 90 days. Each
    /// refuses before any change.
    /// </summary>
    [Fact]
    public void TheCounterVerbs_ReviewUpgradeAndReinforce_WorkAfterTheirDays()
    {
        var (engine, site) = StockedUnit(91);
        var unit = site.Offshore!;
        unit.CrewRotationDays = 1_000_000; // no overdue crew in the alarm load
        unit.TeethingDefects.Add(new TeethingDefect { Id = site.Id + "/td_9", Kind = TeethingDefectKind.NetSystemChange, RevealedDay = engine.State.Day });
        engine.Advance(1);
        Assert.Equal(0.30, unit.AlarmLoad, 12);
        decimal cash = engine.FinanceManager.Balance;
        Assert.True(engine.StartAlarmReview(site.Id).Ok);
        Assert.Equal(Cal.AlarmReview.CostNok, cash - engine.FinanceManager.Balance);
        Assert.Equal(SimulationEngine.OffshoreReasons.AlreadyRunning, engine.StartAlarmReview(site.Id).ErrorKey);
        engine.Advance(Cal.AlarmReview.Days - 1);
        Assert.Equal(0.30, unit.AlarmLoad, 12);
        engine.Advance(1);
        Assert.Equal(0.0, unit.AlarmLoad, 12);
        var drivers = engine.GetOffshoreUnitState(site.Id)!.AlarmDrivers;
        Assert.Equal(new[] { "base", "defects", "sensors", "crew", "review" }, drivers.Select(d => d.Driver));
        Assert.Equal(-0.30, drivers.Last().Value, 12);
        Assert.Equal(unit.AlarmLoad, drivers.Sum(d => d.Value), 12);

        Assert.Equal(SimulationEngine.OffshoreReasons.OutOfRange, engine.StartProcedureUpgrade(site.Id, "galley").ErrorKey);
        Assert.True(engine.StartProcedureUpgrade(site.Id, "hatch").Ok);
        Assert.Equal(SimulationEngine.OffshoreReasons.AlreadyRunning, engine.StartProcedureUpgrade(site.Id, "hatch").ErrorKey);
        engine.Advance(Cal.Procedures.Days);
        Assert.Equal(0.8, unit.HatchProcedureLevel, 12);
        Assert.True(engine.StandingOrderReady(unit) || unit.CrewOverdueDays > 0);

        Assert.True(engine.InstallReinforcement(site.Id).Ok);
        Assert.Equal(SimulationEngine.OffshoreReasons.AlreadyInstalled, engine.InstallReinforcement(site.Id).ErrorKey);
        Assert.Equal(0.0, unit.DesignHsBonusM);
        engine.Advance(Cal.Reinforcement.Days);
        Assert.Equal(1.0, unit.DesignHsBonusM);
        Assert.Contains(engine.State.FacilityAssets, a => a.UnitId == site.Id && a.CapexNok == Cal.Reinforcement.CapexNok);
    }

    /// <summary>
    /// A jellyfish day falls only in November and December (on the uniform's
    /// remainder, never on an incident day) and kills the calibrated share of
    /// every zone, booked as jellyfish deaths.
    /// </summary>
    [Fact]
    public void JellyfishDays_FallInNovemberAndDecember_AndBookJellyfishDeaths()
    {
        var (engine, site) = StockedUnit(92);
        engine.IncidentHazardOverrideForTests = (s, k) => 0.0;
        engine.Advance(365 + 30);
        var unit = site.Offshore!;
        long jelly = engine.MortalityLedger.ForSite(site.Id).Deaths(MortalityCause.Jellyfish).Fish;
        _out.WriteLine($"jellyfish days {unit.JellyDays}, last day {unit.LastJellyDay} (month {SimCalendar.Month(Math.Max(0, unit.LastJellyDay))}), deaths {jelly}");
        Assert.InRange(unit.JellyDays, 1, 15); // 61 Nov-Dec days at 5 %: about 3
        Assert.True(SimCalendar.Month(unit.LastJellyDay) >= 11);
        Assert.True(jelly > 0);
    }
}
