using System;

namespace LakskongenSim.Core
{
    /// <summary>
    /// Severity levels for simulation log messages.
    /// </summary>
    public enum SimLogLevel
    {
        Debug = 0,
        Info = 1,
        Warn = 2,
        Error = 3
    }

    /// <summary>
    /// Centralized, thread-safe logging for the pure-C# simulation layer (TD-21).
    ///
    /// Replaces ad-hoc <c>Console.WriteLine</c> calls so log output can be filtered
    /// by level and redirected (e.g. to Godot's debug output or a log file) without
    /// touching call sites. The default sink writes to <see cref="Console"/> to
    /// preserve existing behavior for tests and headless runs.
    ///
    /// Redirect output by subscribing to <see cref="OnMessage"/>:
    /// <code>SimLog.OnMessage += (level, text) => GD.Print(text);</code>
    /// </summary>
    public static class SimLog
    {
        private static readonly object _lock = new();

        /// <summary>Messages below this level are dropped. Defaults to Debug (log everything).</summary>
        public static SimLogLevel MinLevel { get; set; } = SimLogLevel.Debug;

        /// <summary>
        /// Optional external sink. When set, receives every message at or above
        /// <see cref="MinLevel"/> instead of <see cref="Console"/>.
        /// </summary>
        public static event Action<SimLogLevel, string>? OnMessage;

        /// <summary>Log a diagnostic message (high-frequency internals, perf ticks).</summary>
        public static void Debug(string message) => Write(SimLogLevel.Debug, message);

        /// <summary>Log a normal operational message (init, config loaded, milestones).</summary>
        public static void Info(string message) => Write(SimLogLevel.Info, message);

        /// <summary>Log a recoverable problem (missing config, insufficient funds, fallbacks).</summary>
        public static void Warn(string message) => Write(SimLogLevel.Warn, message);

        /// <summary>Log a non-recoverable failure (save corruption, unhandled subsystem error).</summary>
        public static void Error(string message) => Write(SimLogLevel.Error, message);

        private static void Write(SimLogLevel level, string message)
        {
            if (level < MinLevel) return;
            lock (_lock)
            {
                var sink = OnMessage;
                if (sink != null)
                {
                    sink(level, message);
                }
                else
                {
                    Console.WriteLine(message);
                }
            }
        }
    }
}
