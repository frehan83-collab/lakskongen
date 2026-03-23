using System;
using System.Collections.Generic;

namespace LakskongenSim.Biology
{
    /// <summary>
    /// Stress type categories for early-life epigenetic effects.
    /// Each stress type can reduce asymptotic size if experienced during early development.
    /// </summary>
    public enum StressType
    {
        /// <summary>Temperature shocks outside optimal range (below 2°C or above 18°C).</summary>
        TemperatureShock,

        /// <summary>Physical handling events such as grading, transport, or medical treatment.</summary>
        Handling,

        /// <summary>Disease outbreaks or pathogen exposure.</summary>
        Disease,

        /// <summary>High stocking density leading to competition and stress.</summary>
        Overcrowding,

        /// <summary>Low dissolved oxygen levels.</summary>
        LowOxygen
    }

    /// <summary>
    /// Records a single early-life stress event for epigenetic tracking.
    /// </summary>
    public class StressEvent
    {
        /// <summary>The type of stress experienced.</summary>
        public StressType Type { get; private set; }

        /// <summary>Severity of the stress event (0.0 to 1.0).</summary>
        public double Severity { get; private set; }

        /// <summary>Day index when the stress occurred.</summary>
        public int Day { get; private set; }

        /// <summary>
        /// Creates a new stress event record.
        /// </summary>
        /// <param name="type">The type of stress.</param>
        /// <param name="severity">Severity from 0.0 (mild) to 1.0 (severe).</param>
        /// <param name="day">Day index when stress occurred.</param>
        public StressEvent(StressType type, double severity, int day)
        {
            Type = type;
            Severity = Math.Clamp(severity, 0.0, 1.0);
            Day = day;
        }
    }

    /// <summary>
    /// Tracks early-life stress events and their cumulative epigenetic effects on asymptotic size.
    /// Each stress event reduces asymptotic size by 1-5% (cumulative, maximum 25% reduction).
    /// This models long-term developmental impacts from adverse conditions experienced during early life stages.
    /// </summary>
    public class EpigeneticMemory
    {
        #region Constants

        /// <summary>Maximum cumulative reduction in asymptotic size (25% max).</summary>
        public static readonly double MaxTotalReduction = 0.25;

        /// <summary>Minimum asymptotic size multiplier (0.75 = 75% of original size).</summary>
        public static readonly double MinSizeMultiplier = 1.0 - MaxTotalReduction;

        /// <summary>Stress impact factor: severity × this factor = reduction contribution (1% to 5% per event).</summary>
        public static readonly double BaseStressImpact = 0.05;

        /// <summary>Critical early-life period ends at this day (post-smolt stage).</summary>
        public static readonly double CriticalPeriodEndDay = 180.0; // ~6 months

        #endregion

        private readonly List<StressEvent> _stressEvents;
        private double _cumulativeReduction;
        private double _asymptoticMultiplier;

        /// <summary>
        /// Initializes a new instance of the EpigeneticMemory class.
        /// </summary>
        /// <param name="initialAsymptoticSize">Initial asymptotic weight factor (1.0 represents full genetic potential).</param>
        public EpigeneticMemory(double initialAsymptoticSize = 1.0)
        {
            _stressEvents = new List<StressEvent>();
            _cumulativeReduction = 0.0;
            _asymptoticMultiplier = Math.Clamp(initialAsymptoticSize, 0.75, 1.0);
        }

        /// <summary>
        /// Gets the list of recorded stress events.
        /// Read-only to prevent external modification.
        /// </summary>
        public IReadOnlyList<StressEvent> StressEvents => new List<StressEvent>(_stressEvents);

        /// <summary>
        /// Gets the current asymptotic size multiplier after all epigenetic effects.
        /// Returns a value between 0.75 and 1.0 (0.75 = 25% reduction).
        /// </summary>
        public double AsymptoticMultiplier
        {
            get { return _asymptoticMultiplier; }
        }

        /// <summary>
        /// Records an early-life stress event and updates cumulative epigenetic effects.
        /// </summary>
        /// <param name="type">The type of stress experienced.</param>
        /// <param name="severity">Severity from 0.0 (mild) to 1.0 (severe).</param>
        /// <param name="day">Day index when stress occurred.</param>
        public void RecordStressEvent(StressType type, double severity, int day = -1)
        {
            // Clamp severity to valid range
            severity = Math.Clamp(severity, 0.0, 1.0);

            // Record the stress event with current day if not specified
            int recordedDay = (day >= 0) ? day : _stressEvents.Count;
            var stressEvent = new StressEvent(type, severity, recordedDay);

            _stressEvents.Add(stressEvent);

            // Calculate impact: severity × base impact (5% max per event at full severity)
            double impact = severity * BaseStressImpact;

            // Cap cumulative reduction at maximum
            double newReduction = _cumulativeReduction + impact;
            _cumulativeReduction = Math.Min(newReduction, MaxTotalReduction);

            // Update asymptotic multiplier
            _asymptoticMultiplier = 1.0 - _cumulativeReduction;
        }

        /// <summary>
        /// Overload for backward compatibility - records with default day = -1.
        /// </summary>
        /// <param name="type">The type of stress experienced.</param>
        /// <param name="severity">Severity from 0.0 to 1.0.</param>
        public void RecordStressEvent(StressType type, double severity)
        {
            RecordStressEvent(type, severity, -1);
        }

        /// <summary>
        /// Gets the current cumulative reduction as a multiplier (0.75 to 1.0).
        /// This is equivalent to AsymptoticMultiplier but provided for API clarity.
        /// </summary>
        /// <returns>Asymptotic size multiplier (0.75 to 1.0).</returns>
        public double GetAsymptoticReduction()
        {
            return _asymptoticMultiplier;
        }

        /// <summary>
        /// Applies additional epigenetic reduction without recording a specific stress event.
        /// Useful for applying cumulative effects from external sources.
        /// </summary>
        /// <param name="additionalReduction">Additional reduction (0.0 to 0.25).</param>
        public void ApplyAdditionalReduction(double additionalReduction)
        {
            additionalReduction = Math.Clamp(additionalReduction, 0.0, MaxTotalReduction);
            _cumulativeReduction = Math.Min(_cumulativeReduction + additionalReduction, MaxTotalReduction);
            _asymptoticMultiplier = 1.0 - _cumulativeReduction;
        }

        /// <summary>
        /// Clears all recorded stress events and resets to initial multiplier.
        /// </summary>
        public void Reset()
        {
            _stressEvents.Clear();
            _cumulativeReduction = 0.0;
            _asymptoticMultiplier = 1.0;
        }

        /// <summary>
        /// Simulates common aquaculture stress scenarios during early life.
        /// </summary>
        /// <param name="rng">Random number generator for realistic severity simulation.</param>
        /// <param name="daysSimulation">Number of days to simulate.</param>
        public void SimulateStressScenarios(Random rng, int daysSimulation)
        {
            // Typical early-life stress patterns in salmon farming
            // First 90 days are critical period (~3 months post-hatch)

            for (int day = 0; day < Math.Min(daysSimulation, (int)CriticalPeriodEndDay); day++)
            {
                // ~15% chance of a stress event per day during critical period
                if (rng.NextDouble() < 0.15)
                {
                    // Random stress type with realistic distribution
                    StressType[] stressTypes = {
                        StressType.Handling,
                        StressType.LowOxygen,
                        StressType.Disease,
                        StressType.TemperatureShock,
                        StressType.Overcrowding
                    };

                    StressType stressType = stressTypes[rng.Next(stressTypes.Length)];

                    // Severity ranges: some stresses are typically mild, some severe
                    double severity;
                    switch (stressType)
                    {
                        case StressType.LowOxygen:
                        case StressType.TemperatureShock:
                            // More variable severity
                            severity = rng.NextDouble();
                            break;

                        case StressType.Handling:
                        case StressType.Overcrowding:
                            // Typically moderate
                            severity = rng.NextDouble() * 0.7 + 0.2;
                            break;

                        case StressType.Disease:
                            // Varies widely, can be severe
                            severity = rng.NextDouble() * 0.9 + 0.1;
                            break;

                        default:
                            severity = rng.NextDouble();
                            break;
                    }

                    RecordStressEvent(stressType, severity, day);
                }
            }
        }

        /// <summary>
        /// Gets the total number of recorded stress events.
        /// </summary>
        public int StressEventCount => _stressEvents.Count;

        /// <summary>
        /// Gets stress events by type.
        /// </summary>
        /// <param name="stressType">Type to filter by.</param>
        /// <returns>List of stress events of the specified type.</returns>
        public List<StressEvent> GetEventsByType(StressType stressType)
        {
            return _stressEvents.FindAll(e => e.Type == stressType);
        }

        /// <summary>
        /// Converts the current asymtotic multiplier to a human-readable percentage.
        /// </summary>
        /// <returns>String like "87.5% of genetic potential" (for multiplier 0.875).</returns>
        public string GetSizePotentialString()
        {
            double percentage = _asymptoticMultiplier * 100.0;
            return $"{percentage:0.0}% of genetic potential";
        }
    }
}
