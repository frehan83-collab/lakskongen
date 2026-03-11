using System;

namespace LakskongenSim.Core
{
    /// <summary>
    /// Manages in-game time and triggers tick events.
    /// </summary>
    public class TimeManager
    {
        /// <summary>Raised when days have advanced; subscribers should update systems.</summary>
        public event Action<int>? OnAdvanceDays;

        /// <summary>Current day index.</summary>
        public int CurrentDay { get; private set; }

        /// <summary>Advance the simulation by a number of days and notify subscribers.</summary>
        /// <param name="days">Days to advance.</param>
        public void AdvanceDays(int days)
        {
            if (days <= 0) return;
            CurrentDay += days;
            OnAdvanceDays?.Invoke(days);
        }
    }
}