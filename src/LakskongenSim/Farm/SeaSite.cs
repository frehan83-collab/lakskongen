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
        public string ZoneId { get; set; } = ""; // Links to Environment.Zone
        public List<Biology.FishPopulation> Pens { get; } = new();

        // License & MAB (Maximum Allowed Biomass)
        public double MABTonnes { get; set; } = 780.0; // Standard license: 780 tonnes
        public double CurrentBiomassTonnes => CurrentStockingDensity * PenVolume / 1000.0;
        public bool IsOverMAB => CurrentBiomassTonnes > MABTonnes;

        // Sustainability integration properties
        public int EscapesThisYear { get; private set; } = 0;
        public bool IsClosedContainment { get; set; } = false;
        public float CurrentStockingDensity { get; private set; } = 0f; // kg/m³
        public float MaxAllowedDensity { get; set; } = 25.0f; // kg/m³ (Norwegian standard)
        public float PenVolume { get; set; } = 50000f; // m³ (default pen volume)
        public int NumberOfPens { get; set; } = 8; // Typical Norwegian site: 6-10 pens

        public event Action<Biology.FishPopulation>? OnStocked;
        public event Action<int>? OnEscapeEvent;
        public event Action? OnMABExceeded;

        public SeaSite(string id, string zoneId = "")
        {
            Id = id;
            ZoneId = zoneId;
        }

        public void Stock(Biology.FishPopulation pop)
        {
            pop.ZoneId = ZoneId; // Link population to this site's zone
            Pens.Add(pop);
            OnStocked?.Invoke(pop);
            UpdateStockingDensity();

            // Check MAB limit
            if (IsOverMAB)
            {
                OnMABExceeded?.Invoke();
            }
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