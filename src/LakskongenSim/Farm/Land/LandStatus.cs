using System;
using System.Collections.Generic;
using LakskongenSim.Calibration;

namespace LakskongenSim.Farm.Land
{
    /// <summary>A tank's or a lot's state against its comfort band (spec §6.5: ● ok, ▲ warn, ■ critical).</summary>
    public enum LandStatusLevel
    {
        /// <summary>Inside the comfort band.</summary>
        Ok = 0,
        /// <summary>Outside the comfort band, short of the critical edge.</summary>
        Warn = 1,
        /// <summary>Past the critical edge.</summary>
        Critical = 2,
    }

    /// <summary>The five segments of a tank's water ring (spec §6.5 "Vannmiljø").</summary>
    public static class LandConstraints
    {
        /// <summary>Dissolved oxygen.</summary>
        public const string Oxygen = "o2";
        /// <summary>Dissolved CO2.</summary>
        public const string Co2 = "co2";
        /// <summary>Total ammonia nitrogen.</summary>
        public const string Tan = "tan";
        /// <summary>Water temperature against the setpoint.</summary>
        public const string Temperature = "temperature";
        /// <summary>Stocking density.</summary>
        public const string Density = "density";

        /// <summary>The ring's segments in order (a tie in load goes to the earlier one).</summary>
        public static readonly string[] All = { Oxygen, Co2, Tan, Temperature, Density };
    }

    /// <summary>
    /// One segment of a tank's water ring: its value, where its comfort band
    /// ends (the ok edge) and where it turns critical, its load (1 at the ok
    /// edge, the critical load at the critical edge) and its status.
    /// </summary>
    /// <param name="Key">The segment (<see cref="LandConstraints"/>).</param>
    /// <param name="Value">The tank's value (mg/L, °C or kg/m³).</param>
    /// <param name="OkEdge">Where the comfort band ends.</param>
    /// <param name="CriticalEdge">Where the segment turns critical.</param>
    /// <param name="Load">How far toward its limit the segment stands: under 1 inside the band.</param>
    /// <param name="Level">The segment's status.</param>
    public readonly record struct LandSegment(string Key, double Value, double OkEdge, double CriticalEdge, double Load,
        LandStatusLevel Level);

    /// <summary>
    /// A tank's water ring and the constraint that binds it (the most loaded
    /// segment, "Begrenset av CO₂"), named even when every segment is inside its band.
    /// </summary>
    /// <param name="Segments">O2, CO2, TAN, temperature and density, in that order.</param>
    /// <param name="Binding">The binding constraint: the segment with the highest load.</param>
    /// <param name="Level">The worst segment's status.</param>
    /// <param name="Load">The binding segment's load.</param>
    public sealed record LandTankStatus(IReadOnlyList<LandSegment> Segments, string Binding, LandStatusLevel Level, double Load);

    /// <summary>
    /// The land board's comfort bands (roadmap F2-15 for F2-32, spec §6.5): each
    /// segment's load is its value over its ok edge (O2: the edge over the
    /// value; temperature: the distance from the setpoint over the warning
    /// distance), so the most loaded segment binds. The ok edges are the
    /// model's own (the usable O2 floor, the CO2 and TAN appetite limits, the
    /// density knee); the critical edges are <see cref="LandStatusCalibration"/>'s.
    /// Pure.
    /// </summary>
    public static class LandStatus
    {
        /// <summary>The status of a tank's water and fish.</summary>
        public static LandTankStatus Evaluate(double doMgL, double co2MgL, double tanMgL, double temperatureC, double setpointC,
            double densityKgM3, double maxDensityKgM3, LandCalibration calibration)
        {
            if (calibration == null) throw new ArgumentNullException(nameof(calibration));
            var s = calibration.Status;
            var segments = new[]
            {
                Lower(LandConstraints.Oxygen, doMgL, s.DoWarnMgL, s.DoCriticalMgL),
                Upper(LandConstraints.Co2, co2MgL, calibration.Co2.AppetiteFullMgL, calibration.Co2.AppetiteLimitMgL),
                Upper(LandConstraints.Tan, tanMgL, calibration.Tan.AppetiteLimitMgL, calibration.Tan.AppetiteLimitMgL * s.TanCriticalFactor),
                Distance(LandConstraints.Temperature, temperatureC, setpointC, s.TemperatureWarnC, s.TemperatureCriticalC),
                Upper(LandConstraints.Density, densityKgM3, calibration.Density.KneeKgM3, maxDensityKgM3),
            };
            int binding = 0;
            for (int i = 1; i < segments.Length; i++)
                if (segments[i].Load > segments[binding].Load + 1e-12) binding = i;
            var level = LandStatusLevel.Ok;
            foreach (var segment in segments)
                if (segment.Level > level) level = segment.Level;
            return new LandTankStatus(segments, segments[binding].Key, level, segments[binding].Load);
        }

        /// <summary>A segment whose value must stay above its edges (oxygen).</summary>
        private static LandSegment Lower(string key, double value, double okEdge, double criticalEdge)
        {
            double v = double.IsFinite(value) ? Math.Max(0.0, value) : 0.0;
            double load = v > 0.0 ? okEdge / v : double.MaxValue;
            var level = v < criticalEdge ? LandStatusLevel.Critical : v < okEdge ? LandStatusLevel.Warn : LandStatusLevel.Ok;
            return new LandSegment(key, v, okEdge, criticalEdge, load, level);
        }

        /// <summary>A segment whose value must stay below its edges (CO2, TAN, density).</summary>
        private static LandSegment Upper(string key, double value, double okEdge, double criticalEdge)
        {
            double v = double.IsFinite(value) ? Math.Max(0.0, value) : 0.0;
            double load = okEdge > 0.0 ? v / okEdge : 0.0;
            var level = v > criticalEdge ? LandStatusLevel.Critical : v > okEdge ? LandStatusLevel.Warn : LandStatusLevel.Ok;
            return new LandSegment(key, v, okEdge, criticalEdge, load, level);
        }

        /// <summary>A segment whose value must stay near a target (temperature).</summary>
        private static LandSegment Distance(string key, double value, double target, double warnDistance, double criticalDistance)
        {
            double v = double.IsFinite(value) ? value : target;
            double off = Math.Abs(v - target);
            double load = warnDistance > 0.0 ? off / warnDistance : 0.0;
            var level = off > criticalDistance ? LandStatusLevel.Critical : off > warnDistance ? LandStatusLevel.Warn : LandStatusLevel.Ok;
            return new LandSegment(key, v, warnDistance, criticalDistance, load, level);
        }
    }
}
