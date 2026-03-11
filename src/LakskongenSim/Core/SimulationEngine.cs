using System;
using System.Collections.Generic;

namespace LakskongenSim.Core
{
    /// <summary>
    /// High-level orchestrator that connects subsystems and advances the simulation.
    /// </summary>
    public class SimulationEngine
    {
        private readonly List<object> _systems = new();
        public TimeManager Time { get; } = new();

        public GameState State { get; } = new();

        /// <summary>
        /// Register a system to be managed. Systems should subscribe to TimeManager themselves or expose Tick.
        /// </summary>
        public void RegisterSystem(object system)
        {
            _systems.Add(system);
        }

        /// <summary>
        /// Advance the whole simulation by given days.
        /// </summary>
        public void Advance(int days)
        {
            Time.AdvanceDays(days);
        }
    }
}