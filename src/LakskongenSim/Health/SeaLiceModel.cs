using System;

namespace LakskongenSim.Health
{
    /// <summary>
    /// Simplified sea lice model that tracks average lice per fish and affects welfare.
    /// </summary>
    public class SeaLiceModel
    {
        public double AverageLicePerFish { get; private set; } = 0.0;

        public event Action<double>? OnLiceChanged;

        public void AdvanceDays(int days)
        {
            // Simple seasonal growth placeholder
            AverageLicePerFish *= Math.Pow(1.01, days);
            AverageLicePerFish += 0.01 * days; // slow increase
            OnLiceChanged?.Invoke(AverageLicePerFish);
        }

        public void ApplyTreatment(double reductionFraction)
        {
            AverageLicePerFish *= (1.0 - reductionFraction);
            OnLiceChanged?.Invoke(AverageLicePerFish);
        }
    }
}