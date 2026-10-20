using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Calibration;

namespace LakskongenSim.Farm.Offshore
{
    /// <summary>One named term of a driver list: what it is and its value (saved in incidents and reports).</summary>
    public sealed record OffshoreDriver
    {
        /// <summary>For the save.</summary>
        public OffshoreDriver() { }

        /// <summary>A driver <paramref name="name"/> with <paramref name="value"/>.</summary>
        public OffshoreDriver(string name, double value)
        {
            Name = name;
            Value = value;
        }

        /// <summary>The driver's key (stable, snake_case: the bridge and the incident report carry it).</summary>
        public string Name { get; init; } = "";

        /// <summary>Its value: an additive term of the alarm load, a factor of a hazard.</summary>
        public double Value { get; init; }
    }

    /// <summary>
    /// The alarm load of an offshore unit (spec §3.3 "Alarm load", emergent,
    /// with no mute toggle): <c>clamp(0.15 + 0.15·unfixed revealed defects +
    /// 0.10·sensor packages past service + 0.02·min(crew overdue days, 10) −
    /// 0.30·review in force, 0, 1)</c>. High load stands for a crew drowning in
    /// nuisance alarms from more than 20,000 points (O:R3): it silences
    /// classes, so a ballast deviation gets through less often. Pure.
    /// </summary>
    public static class AlarmLoadModel
    {
        /// <summary>The driver keys in the order the board lists them.</summary>
        public static readonly IReadOnlyList<string> DriverKeys = new[] { "base", "defects", "sensors", "crew", "review" };

        /// <summary>True when a completed alarm review's cut is in force on <paramref name="day"/>.</summary>
        public static bool ReviewInForce(OffshoreUnit unit, int day, OffshoreAlarmReviewCalibration review)
        {
            if (unit.AlarmReviewStartedDay < 0) return false;
            int done = unit.AlarmReviewStartedDay + review.Days;
            return day >= done && day < done + review.EffectDays;
        }

        /// <summary>True while an alarm review runs (started, not yet done) on <paramref name="day"/>.</summary>
        public static bool ReviewRunning(OffshoreUnit unit, int day, OffshoreAlarmReviewCalibration review)
            => unit.AlarmReviewStartedDay >= 0 && day < unit.AlarmReviewStartedDay + review.Days;

        /// <summary>The alarm load's terms on <paramref name="day"/>, in <see cref="DriverKeys"/> order (the review's term is negative).</summary>
        public static IReadOnlyList<OffshoreDriver> Drivers(OffshoreUnit unit, int day, OffshoreAlarmLoadCalibration load,
            OffshoreAlarmReviewCalibration review, int sensorServiceIntervalDays)
        {
            if (unit == null) throw new ArgumentNullException(nameof(unit));
            int defects = unit.TeethingDefects.Count(d => d.IsRevealed && !d.IsFixedBy(day));
            int sensors = unit.Sensors.Count(s => s.IsPastService(day, sensorServiceIntervalDays));
            return new[]
            {
                new OffshoreDriver("base", load.Base),
                new OffshoreDriver("defects", load.PerUnfixedRevealedDefect * defects),
                new OffshoreDriver("sensors", load.PerSensorPastService * sensors),
                new OffshoreDriver("crew", load.PerCrewOverdueDay * Math.Min(unit.CrewOverdueDays, load.CrewOverdueCapDays)),
                new OffshoreDriver("review", ReviewInForce(unit, day, review) ? -review.Cut : 0.0),
            };
        }

        /// <summary>The alarm load its terms give: their sum, clamped to 0-1.</summary>
        public static double Of(IReadOnlyList<OffshoreDriver> drivers) => Math.Clamp(drivers.Sum(d => d.Value), 0.0, 1.0);
    }

    /// <summary>
    /// The tilt chain's hazard (spec §3.3 "Tilt chain (OF1-class)"): <c>h =
    /// (1/9000)·(2 − HatchProcedureLevel)·(1 + 2·AlarmLoad)·(1 + 0.05·CrewOverdueDays)·teething</c>,
    /// teething ×3 while the ballast-pipe leak is revealed, unfixed and the unit
    /// holds fish (the deck's "fires", F2-07). Only an OF1-class semi-submersible
    /// ballasts this way; a ship-shaped unit has no tilt chain. Pure; the
    /// bridge's driver list is <see cref="Drivers"/>.
    /// </summary>
    public static class TiltHazardModel
    {
        /// <summary>The driver keys in the order of the spec's formula.</summary>
        public static readonly IReadOnlyList<string> DriverKeys = new[] { "base", "procedure", "alarm", "crew", "teething" };

        /// <summary>The hazard's factors on <paramref name="day"/> (all 0 for a class with no tilt chain).</summary>
        public static IReadOnlyList<OffshoreDriver> Drivers(OffshoreUnit unit, int day, bool hasFish,
            OffshoreIncidentsCalibration incidents, OffshoreTeethingCalibration teething)
        {
            if (unit == null) throw new ArgumentNullException(nameof(unit));
            bool chain = unit.FarmType == OffshoreType.SemiSubmersible;
            bool leak = hasFish && unit.TeethingDefects.Any(d => d.Kind == TeethingDefectKind.BallastPipeLeak
                                                                && d.IsRevealed && d.RevealedDay <= day && !d.IsFixedBy(day));
            return new[]
            {
                new OffshoreDriver("base", chain ? 1.0 / incidents.TiltBaseDays : 0.0),
                new OffshoreDriver("procedure", incidents.TiltProcedureCeiling - unit.HatchProcedureLevel),
                new OffshoreDriver("alarm", 1.0 + incidents.TiltAlarmFactor * unit.AlarmLoad),
                new OffshoreDriver("crew", 1.0 + incidents.TiltPerCrewOverdueDay * unit.CrewOverdueDays),
                new OffshoreDriver("teething", leak ? teething.BallastTiltMultiplier : 1.0),
            };
        }

        /// <summary>The daily hazard its factors give (their product, never negative, at most 1).</summary>
        public static double Of(IReadOnlyList<OffshoreDriver> drivers)
        {
            double h = 1.0;
            foreach (var d in drivers) h *= d.Value;
            return Math.Clamp(h, 0.0, 1.0);
        }
    }
}
