using System;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("LakskongenSim.Health.Tests")]

namespace LakskongenSim.Health
{
    /// <summary>
    /// Simplified sea lice model that tracks average lice per fish and affects welfare.
    /// </summary>
    public class SeaLiceModel
    {
        public double AverageLicePerFish { get; internal set; } = 0.0;

        /// <summary>External multiplier from real-world lice pressure snapshots.</summary>
        public double ExternalPressureMultiplier { get; private set; } = 1.0;

        public event Action<double>? OnLiceChanged;

        public void AdvanceDays(int days)
        {
            // Simple seasonal growth placeholder, scaled by external real-world pressure.
            var pressure = Math.Max(0.5, ExternalPressureMultiplier);
            AverageLicePerFish *= Math.Pow(1.01 * pressure, days);
            AverageLicePerFish += 0.01 * days * pressure;
            OnLiceChanged?.Invoke(AverageLicePerFish);
        }

        public void SetExternalPressureMultiplier(double multiplier)
        {
            ExternalPressureMultiplier = Math.Clamp(multiplier, 0.5, 2.0);
        }

        public void ApplyTreatment(double reductionFraction)
        {
            AverageLicePerFish *= (1.0 - reductionFraction);
            OnLiceChanged?.Invoke(AverageLicePerFish);
        }
    }
}