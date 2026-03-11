using System;

namespace LakskongenSim.Environment
{
    /// <summary>
    /// Represents a management zone in the simulation (carrying capacity, water quality, ...)
    /// </summary>
    public class Zone
    {
        public string Id { get; init; }
        public double CarryingCapacityTonnes { get; set; }

        public Zone(string id)
        {
            Id = id;
            CarryingCapacityTonnes = 1000.0; // placeholder
        }

        public void AdvanceDays(int days)
        {
            // TODO: implement water quality dynamics, currents, eutrophication
        }
    }
}