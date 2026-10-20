using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using LakskongenSim.Calibration;

namespace LakskongenSim.Farm.Offshore
{
    /// <summary>The incidents an offshore unit can have (F2-08, spec §3.3).</summary>
    public enum OffshoreIncidentKind
    {
        /// <summary>The OF1-class tilt: a ballast deviation the crew did not catch in time.</summary>
        Tilt = 0,
        /// <summary>A tear in a worn net.</summary>
        NetTear = 1,
        /// <summary>A mooring line parted in a sea above the unit's design.</summary>
        MooringFailure = 2,
    }

    /// <summary>Where an incident stands.</summary>
    public enum OffshoreIncidentStage
    {
        /// <summary>It happened; the nets are checked and the losses counted (and a tear leaks until mended).</summary>
        Investigation = 0,
        /// <summary>Closed: the nets inspected (a tear mended), or the investigation's time ran out.</summary>
        Closed = 1,
    }

    /// <summary>
    /// One incident at an offshore unit (F2-08, spec §3.2 <c>ActiveIncident</c>,
    /// §3.3): its kind, the day it started and its stage, every branch drawn
    /// when it started ("draw, then decide": the tilt's duration, whether the
    /// ballast deviation was caught and whether the hatches closed by the
    /// standing order; a tear's zone and size; a mooring failure's share), the
    /// drivers behind its probability that day, and its losses. Saved in
    /// <c>GameState.OffshoreIncidents</c>; the unit names its open one.
    /// </summary>
    public sealed class OffshoreIncident
    {
        /// <summary>Deterministic id "&lt;unit&gt;/inc_N".</summary>
        public string Id { get; set; } = "";

        /// <summary>What happened.</summary>
        public OffshoreIncidentKind Kind { get; set; }

        /// <summary>The unit.</summary>
        public string UnitId { get; set; } = "";

        /// <summary>The sea day it started.</summary>
        public int StartDay { get; set; }

        /// <summary>Where it stands.</summary>
        public OffshoreIncidentStage Stage { get; set; } = OffshoreIncidentStage.Investigation;

        /// <summary>The day it closed (-1 while open).</summary>
        public int ClosedDay { get; set; } = -1;

        /// <summary>The daily probability of this kind on its day (the hazard the day's uniform fell under).</summary>
        public double HazardPerDay { get; set; }

        /// <summary>The hazard's drivers on its day (the tilt chain's factors; a tear's net; a mooring failure's sea).</summary>
        public List<OffshoreDriver> HazardDrivers { get; set; } = new();

        /// <summary>The alarm load on its day.</summary>
        public double AlarmLoad { get; set; }

        /// <summary>The alarm load's terms on its day.</summary>
        public List<OffshoreDriver> AlarmDrivers { get; set; } = new();

        /// <summary>The hatch procedure level on its day.</summary>
        public double HatchProcedureLevel { get; set; }

        /// <summary>Days the crew change was overdue on its day.</summary>
        public int CrewOverdueDays { get; set; }

        /// <summary>The sea (Hs, m) at the unit on its day.</summary>
        public double HsM { get; set; }

        /// <summary>The unit's design Hs with any reinforcement (m).</summary>
        public double DesignHsM { get; set; }

        /// <summary>The nets' integrity (%) on its day.</summary>
        public double NetIntegrityPct { get; set; }

        /// <summary>The ballast-pipe leak defect firing that day ("" when none): the teething factor's defect.</summary>
        public string BallastDefectId { get; set; } = "";

        /// <summary>A tilt: hours it lasted.</summary>
        public double DurationHours { get; set; }

        /// <summary>A tilt: the probability the ballast deviation was caught (1 − the alarm load).</summary>
        public double CatchProbability { get; set; }

        /// <summary>A tilt: the deviation's alarm got through to the crew.</summary>
        public bool DeviationCaught { get; set; }

        /// <summary>A tilt: the standing order "Lukk luker ved ballastavvik" could act (procedure ≥ 0.8, crew on board, none overdue).</summary>
        public bool StandingOrderReady { get; set; }

        /// <summary>A tilt: the hatches were closed by the standing order (caught and ready), ending it within the hour.</summary>
        public bool AutoClosed { get; set; }

        /// <summary>Share of the fish the incident's first day released (of the unit for a tilt or mooring failure; of the zone for a tear).</summary>
        public double EscapeShare { get; set; }

        /// <summary>A tear: the torn zone's number (0 for a whole-unit incident).</summary>
        public int ZoneNumber { get; set; }

        /// <summary>Fish that escaped, the first day and every leaking day after it.</summary>
        public int EscapedFish { get; set; }

        /// <summary>The engine day the escape went into the site's escape record (-1 before any fish escaped).</summary>
        public int EscapeRecordDay { get; set; } = -1;

        /// <summary>Fish that escaped on the first day.</summary>
        public int EscapedFirstDay { get; set; }

        /// <summary>A tear: days it leaked after the first.</summary>
        public int LeakDays { get; set; }

        /// <summary>A tear: true once mended (a net repair in a window).</summary>
        public bool Mended { get; set; }

        /// <summary>The infringement fee booked (NOK).</summary>
        public decimal FeeNok { get; set; }

        /// <summary>Recapture, river monitoring and sampling booked (NOK).</summary>
        public decimal MeasuresNok { get; set; }

        /// <summary>True while it is under investigation.</summary>
        [JsonIgnore] public bool IsOpen => Stage == OffshoreIncidentStage.Investigation;
    }

    /// <summary>One causal step of an incident report (spec §6.6: ≥ 3 steps, the drivers behind each probability, the losses).</summary>
    public sealed class IncidentStep
    {
        /// <summary>1, 2, 3 … in causal order.</summary>
        public int Order { get; set; }

        /// <summary>Stable key of the step ("defect", "alarm_load", "deviation", "duration", "escape", "requirement" …).</summary>
        public string Key { get; set; } = "";

        /// <summary>The day the step is about.</summary>
        public int Day { get; set; }

        /// <summary>The step's number (an alarm load, a probability, hours, fish, days).</summary>
        public double Value { get; set; }

        /// <summary>The step as the player reads it (Norwegian).</summary>
        public string Text { get; set; } = "";

        /// <summary>The drivers behind the step's probability or number.</summary>
        public List<OffshoreDriver> Drivers { get; set; } = new();

        /// <summary>The record the step reconciles to: "incident:&lt;id&gt;", "defect:&lt;id&gt;", "escape:&lt;site&gt;:&lt;day&gt;", "booking:&lt;id&gt;", "withdrawal:&lt;id&gt;" …</summary>
        public string RecordRef { get; set; } = "";
    }

    /// <summary>
    /// The report written after every critical event at an offshore unit (spec
    /// §3.5 "Every critical event writes an IncidentReport ... with ≥ 3 causal
    /// steps, the drivers behind each probability, the losses and the procedure
    /// unlocked"; §6.6). Saved in <c>GameState.IncidentReports</c>; its title
    /// also goes to the event log.
    /// </summary>
    public sealed class IncidentReport
    {
        /// <summary>Deterministic id "ir_N".</summary>
        public string Id { get; set; } = "";

        /// <summary>The unit.</summary>
        public string UnitId { get; set; } = "";

        /// <summary>The day it was written.</summary>
        public int Day { get; set; }

        /// <summary>What it reports: "tilt", "net_tear", "mooring_failure", "mattilsynet_order", "site_approval_withdrawn".</summary>
        public string Kind { get; set; } = "";

        /// <summary>The incident it reports ("" for a regulator's step).</summary>
        public string IncidentId { get; set; } = "";

        /// <summary>The causal chain, in order.</summary>
        public List<IncidentStep> Steps { get; set; } = new();

        /// <summary>Fish lost (escaped) in what it reports.</summary>
        public int FishLost { get; set; }

        /// <summary>Money it cost (fees, measures; NOK).</summary>
        public decimal CostNok { get; set; }

        /// <summary>The counter the report points to: "hatch_procedure", "alarm_review", "fix_defect", "repair_net", "reinforcement", "book_operation", "appeal".</summary>
        public string ProcedureUnlocked { get; set; } = "";
    }

    /// <summary>
    /// The incident model's pure arithmetic (spec §3.3): which incident a day's
    /// uniform picks over the day's hazards (inverse CDF; the uniform's remainder
    /// is a fresh uniform for the no-incident branch), and each kind's branches
    /// from the day's other two uniforms. Draws nothing.
    /// </summary>
    public static class OffshoreIncidentModel
    {
        /// <summary>
        /// The incident <paramref name="u"/> picks over the hazards in the order
        /// tilt, tear, mooring (null for none), and the remainder of the
        /// uniform rescaled to [0, 1) in the branch it fell in.
        /// </summary>
        public static (OffshoreIncidentKind? Kind, double Remainder) Pick(double u, double tilt, double tear, double mooring)
        {
            if (!(u >= 0.0 && u < 1.0)) throw new ArgumentOutOfRangeException(nameof(u), u, "a uniform in [0, 1)");
            double lo = 0.0;
            foreach (var (kind, h) in new[] { (OffshoreIncidentKind.Tilt, tilt), (OffshoreIncidentKind.NetTear, tear), (OffshoreIncidentKind.MooringFailure, mooring) })
            {
                double p = Math.Clamp(h, 0.0, 1.0);
                if (p <= 0.0) continue;
                double hi = Math.Min(1.0, lo + p);
                if (u < hi) return (kind, (u - lo) / (hi - lo));
                lo = hi;
                if (lo >= 1.0) break;
            }
            return (null, lo >= 1.0 ? 0.0 : (u - lo) / (1.0 - lo));
        }

        /// <summary>A tilt's duration (hours) from its uniform: uniform over the calibrated 1-24 h.</summary>
        public static double TiltDurationHours(double u, OffshoreIncidentsCalibration cal)
            => cal.TiltDurationMinHours + Math.Clamp(u, 0.0, 1.0) * (cal.TiltDurationMaxHours - cal.TiltDurationMinHours);

        /// <summary>Share of the unit's fish a tilt of <paramref name="hours"/> releases: 1.5 % × min(1, hours / 12).</summary>
        public static double TiltEscapeShare(double hours, OffshoreIncidentsCalibration cal)
            => cal.TiltEscapeFraction * Math.Min(1.0, Math.Max(0.0, hours) / cal.TiltFullEscapeHours);

        /// <summary>The daily tear hazard at <paramref name="netPct"/>: 0 at or above the threshold, rising linearly to the calibrated hazard at 0 %.</summary>
        public static double TearHazard(double netPct, OffshoreIncidentsCalibration cal)
            => netPct >= cal.NetTearBelowPct || cal.NetTearBelowPct <= 0.0
                ? 0.0
                : cal.NetTearHazardAtZero * (cal.NetTearBelowPct - Math.Max(0.0, netPct)) / cal.NetTearBelowPct;

        /// <summary>The daily mooring hazard: the calibrated hazard on a day above the design sea, else 0.</summary>
        public static double MooringHazard(double hs, double designHs, OffshoreIncidentsCalibration cal)
            => hs > designHs ? cal.MooringHazardOverDesign : 0.0;
    }
}
