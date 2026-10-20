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

        /// <summary>Mirror the live game zones (same instances, no copies —
        /// water-chemistry state lives on the zones themselves).</summary>
        public void SyncZones(System.Collections.Generic.IEnumerable<Zone> zones)
        {
            Zones.Clear();
            Zones.AddRange(zones);
        }

        public void AdvanceDays(int days)
        {
            foreach (var z in Zones) z.AdvanceDays(days);
            OnEnvironmentChanged?.Invoke();
        }
    }
}