using System;
using System.Collections.Generic;

namespace LakskongenSim.Environment
{
    /// <summary>
    /// Manages environmental zones and aggregated variables.
    /// </summary>
    public class EnvironmentModel
    {
        public List<Zone> Zones { get; } = new();

        public event Action? OnEnvironmentChanged;

        public void AdvanceDays(int days)
        {
            foreach (var z in Zones) z.AdvanceDays(days);
            OnEnvironmentChanged?.Invoke();
        }
    }
}