using System;
using System.Collections.Generic;
using System.Linq;

namespace LakskongenSim.Farm
{
    /// <summary>
    /// Represents a sea site where fish are kept in pens.
    /// </summary>
    public class SeaSite
    {
        public string Id { get; init; }
        public List<Biology.FishPopulation> Pens { get; } = new();

        // Sustainability integration properties
        public int EscapesThisYear { get; private set; } = 0;
        public bool IsClosedContainment { get; set; } = false;
        public float CurrentStockingDensity { get; private set; } = 0f; // kg/m³
        public float MaxAllowedDensity { get; set; } = 25.0f; // kg/m³ (Norwegian standard)
        public float PenVolume { get; set; } = 50000f; // m³ (default pen volume)

        public event Action<Biology.FishPopulation>? OnStocked;
        public event Action<int>? OnEscapeEvent; // Emits fish count

        public SeaSite(string id)
        {
            Id = id;
        }

        public void Stock(Biology.FishPopulation pop)
        {
            Pens.Add(pop);
            OnStocked?.Invoke(pop);
            UpdateStockingDensity();
        }

        /// <summary>Advance all pens by days.</summary>
        public void AdvanceDays(int days)
        {
            foreach (var p in Pens) p.AdvanceDays(days);
            UpdateStockingDensity();
        }

        /// <summary>Record escape event for sustainability tracking.</summary>
        public void RecordEscape(int fishCount)
        {
            EscapesThisYear += fishCount;
            OnEscapeEvent?.Invoke(fishCount);
        }

        /// <summary>Reset annual counters (call at year start).</summary>
        public void ResetAnnualCounters()
        {
            EscapesThisYear = 0;
        }

        /// <summary>Calculate current stocking density based on total biomass and pen volume.</summary>
        private void UpdateStockingDensity()
        {
            double totalBiomassKg = 0.0;
            foreach (var pen in Pens)
            {
                foreach (var cohort in pen.Cohorts)
                {
                    totalBiomassKg += cohort.Count * cohort.MeanWeightKg;
                }
            }
            CurrentStockingDensity = (float)(totalBiomassKg / PenVolume);
        }
    }
}