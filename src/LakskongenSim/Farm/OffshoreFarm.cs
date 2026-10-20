using System;
using System.Collections.Generic;
using LakskongenSim.Core;
namespace LakskongenSim.Farm
{
    /// <summary>
    /// Offshore/Exposed Aquaculture Farm — Semi-submersible or ship-shaped structures.
    /// Based on real data from SalMar Ocean Farm 1, Nordlaks Havfarm 1, Norway Royal Salmon.
    /// 
    /// Key characteristics:
    /// - Massive capacity (6,000-10,000+ tonnes per unit)
    /// - Better water quality (ocean currents, lower lice pressure)
    /// - Extreme weather vulnerability (storms, waves)
    /// - High CAPEX (700M-1B+ NOK), specialized crew/logistics
    /// </summary>
    public class OffshoreFarm
    {
        // ─── Identity ───
        public string Id { get; init; } = "";
        public string Name { get; set; } = "Offshore-anlegg";
        public string ZoneId { get; set; } = "";
        public OffshoreType FarmType { get; set; } = OffshoreType.SemiSubmersible;

        // ─── Capacity ───
        /// <summary>Maximum biomass capacity in tonnes.</summary>
        public double CapacityTonnes { get; set; } = 8_000; // Ocean Farm 1: ~8,000t
        
        /// <summary>Number of containment units/net pens.</summary>
        public int PenCount { get; set; } = 6; // Ocean Farm 1: 6 pens inside structure
        
        /// <summary>Volume per pen in m³.</summary>
        public double PenVolumeM3 { get; set; } = 40_000; // Much larger than fjord pens
        
        /// <summary>Fish populations in offshore pens.</summary>
        public List<Biology.FishPopulation> Pens { get; set; } = new();

        // ─── Environmental Advantages ───
        /// <summary>Sea lice pressure multiplier (lower offshore due to distance from coast). 0-1 scale.</summary>
        public double SeaLicePressure { get; set; } = 0.3; // 70% lower than fjord sites
        
        /// <summary>Water flow rate (m/s). Higher offshore = better oxygen, waste removal.</summary>
        public double WaterFlowRate { get; set; } = 0.8; // vs 0.1-0.3 in fjords
        
        /// <summary>Natural oxygen level boost from ocean currents (+mg/L).</summary>
        public double OxygenBoost { get; set; } = 1.5;
        
        /// <summary>FCR advantage from better water flow (fish exercise more, better conversion).</summary>
        public double FCR { get; set; } = 1.12; // Slightly better than fjord 1.15

        // ─── Weather Vulnerability ───
        /// <summary>Maximum wave height the structure can handle (meters).</summary>
        public double MaxWaveHeightM { get; set; } = 12.0; // Ocean Farm 1 rated for 12m waves
        
        /// <summary>Current significant wave height (meters).</summary>
        public double CurrentWaveHeight { get; set; } = 2.0;
        
        /// <summary>Structural integrity (0-1). Degrades in storms, repairs needed.</summary>
        public double StructuralIntegrity { get; set; } = 1.0;
        
        /// <summary>Is the farm in storm lockdown (no crew transfer, no feeding possible)?</summary>
        public bool IsStormLockdown { get; set; } = false;
        
        /// <summary>Days since last crew rotation.</summary>
        public int DaysSinceCrewRotation { get; set; } = 0;
        
        /// <summary>Crew rotation interval (days). Offshore workers do 2-week shifts.</summary>
        public int CrewRotationDays { get; set; } = 14;

        // ─── Financial ───
        public decimal CAPEX { get; set; }
        public double ConstructionProgress { get; set; } = 0.0;
        public int ConstructionDays { get; set; } = 1095; // ~3 years for offshore structure
        public int ConstructionStartDay { get; set; } = -1;
        
        public bool IsOperational => ConstructionProgress >= 1.0;
        
        public int EmployeeCount { get; set; } = 25; // Smaller crew, more automated

        // Phase 5.4: Carbon ledger
        /// <summary>Carbon emissions/credits tracker for this offshore farm.</summary>
        public CarbonTracker CarbonTracker { get; set; } = new();

        // ─── Events (not serialized) ───
        public event Action<string>? OnAlert;
        public event Action<string>? OnMilestone;

        // ─── Deterministic RNG (SimRngStreams.OffshoreFarm, per-farm suffix) ───
        // Not serialized: stream position is captured via engine Rng.CaptureStates()
        // once the tick loop binds this farm to Rng.Stream(scope, Id).
        private SimRngStream _rng = SimRng.CreateUnseeded().Stream(SimRngStreams.OffshoreFarm);
        internal SimRngStream RngForSnapshot => _rng;

        public OffshoreFarm() { }

        public OffshoreFarm(SimRngStream rng)
        {
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        }

        /// <summary>Bind this farm to the engine's named stream (per-farm identity).</summary>
        public void SetRng(SimRngStream rng) => _rng = rng ?? throw new ArgumentNullException(nameof(rng));

        /// <summary>
        /// Daily tick: weather exposure, logistics, crew management.
        /// </summary>
        public OffshoreTickResult TickDay(int currentDay, double windSpeedMs, double waveHeightM, bool isStorm)
        {
            var result = new OffshoreTickResult();
            CurrentWaveHeight = waveHeightM;

            // Construction phase
            if (!IsOperational)
            {
                if (ConstructionStartDay >= 0)
                {
                    int elapsed = currentDay - ConstructionStartDay;
                    ConstructionProgress = Math.Min(1.0, (double)elapsed / ConstructionDays);
                    if (IsOperational)
                    {
                        OnMilestone?.Invoke($"Offshore-anlegget '{Name}' er sjøsatt og operativt!");
                        result.MilestoneReached = true;
                    }
                }
                return result;
            }

            // ─── Storm Assessment ───
            if (isStorm || waveHeightM > 6.0)
            {
                IsStormLockdown = true;
                result.StormLockdown = true;
                
                // Structural damage from extreme waves
                if (waveHeightM > MaxWaveHeightM)
                {
                    double damageRate = (waveHeightM - MaxWaveHeightM) * 0.05; // 5% per meter over max
                    StructuralIntegrity = Math.Max(0.0, StructuralIntegrity - damageRate);
                    OnAlert?.Invoke($"KRITISK: Bølgehøyde {waveHeightM:F1}m overskrider maks {MaxWaveHeightM:F1}m! Strukturskade.");
                    result.StructuralDamage = damageRate;
                }

                // Fish stress and escape risk during storms
                if (waveHeightM > 8.0)
                {
                    double escapeChance = (waveHeightM - 8.0) * 0.02; // 2% per meter over 8m
                    if (_rng.NextDouble() < escapeChance)
                    {
                        int escapes = (int)(GetTotalFishCount() * _rng.NextDouble() * 0.05); // Up to 5%
                        result.FishEscaped = escapes;
                        OnAlert?.Invoke($"RØMMING: {escapes:N0} fisk har rømt under stormen!");
                    }
                }

                // No feeding during storm lockdown
                result.FeedingPossible = false;
            }
            else
            {
                IsStormLockdown = false;
                result.FeedingPossible = true;
            }

            // ─── Crew Management ───
            DaysSinceCrewRotation++;
            if (DaysSinceCrewRotation >= CrewRotationDays && !IsStormLockdown)
            {
                DaysSinceCrewRotation = 0;
                result.CrewRotationCost = 150_000m; // Helicopter/boat transfer cost
            }
            else if (DaysSinceCrewRotation > CrewRotationDays + 7)
            {
                // Extended shift beyond rotation window - morale penalty
                result.CrewMoralePenalty = 0.05;
                OnAlert?.Invoke("Mannskap overdue for rotasjon. Moral synker.");
            }

            // ─── Operating Costs ───
            // Staff costs (offshore premium: ~900K NOK/year per employee)
            result.DailyStaffCost = (decimal)EmployeeCount * 900_000m / LakskongenSim.Core.SimCalendar.DaysPerYear;
            
            // Energy (diesel generators + grid where available)
            result.DailyEnergyCost = 80_000m; // ~29M NOK/year for offshore ops
            
            // Maintenance (higher offshore due to salt exposure)
            result.DailyMaintenanceCost = (decimal)(CapacityTonnes * 2.0); // ~2 NOK/t/day
            
            // Logistics (supply vessel runs)
            result.DailyLogisticsCost = 50_000m; // Supply boat runs

            result.TotalDailyOPEX = result.DailyStaffCost + result.DailyEnergyCost 
                                  + result.DailyMaintenanceCost + result.DailyLogisticsCost
                                  + result.CrewRotationCost;

            // ─── Structural Maintenance ───
            if (StructuralIntegrity < 0.90)
            {
                OnAlert?.Invoke($"Strukturell integritet: {StructuralIntegrity:P0}. Reparasjon anbefales.");
            }

            return result;
        }

        /// <summary>Start construction of the offshore farm.</summary>
        public void StartConstruction(int currentDay)
        {
            ConstructionStartDay = currentDay;
            ConstructionProgress = 0.0;
            CAPEX = GetCAPEX();
        }

        /// <summary>Repair structural damage. Returns cost.</summary>
        public decimal RepairStructure()
        {
            double damagePct = 1.0 - StructuralIntegrity;
            decimal cost = (decimal)(damagePct * 50_000_000); // Up to 50M NOK for full repair
            StructuralIntegrity = 1.0;
            OnMilestone?.Invoke($"Strukturell reparasjon fullført på '{Name}'.");
            return cost;
        }

        public int GetTotalFishCount()
        {
            int total = 0;
            foreach (var pop in Pens) total += pop.TotalCount;
            return total;
        }

        public decimal GetCAPEX() => FarmType switch
        {
            OffshoreType.SemiSubmersible => 700_000_000m,  // 700M NOK — Ocean Farm 1 style
            OffshoreType.ShipShaped => 900_000_000m,       // 900M NOK — Havfarm 1 style (394m long ship)
            OffshoreType.Submersible => 1_200_000_000m,    // 1.2B NOK — Fully submersible (future tech)
            _ => 700_000_000m
        };
    }

    public enum OffshoreType
    {
        SemiSubmersible,  // SalMar Ocean Farm 1 — circular, 110m diameter
        ShipShaped,       // Nordlaks Havfarm 1 — 394m converted ship hull
        Submersible       // Future concept — fully submerging during storms
    }

    public class OffshoreTickResult
    {
        public decimal DailyStaffCost { get; set; }
        public decimal DailyEnergyCost { get; set; }
        public decimal DailyMaintenanceCost { get; set; }
        public decimal DailyLogisticsCost { get; set; }
        public decimal CrewRotationCost { get; set; }
        public decimal TotalDailyOPEX { get; set; }
        public bool StormLockdown { get; set; }
        public bool FeedingPossible { get; set; } = true;
        public double StructuralDamage { get; set; }
        public int FishEscaped { get; set; }
        public double CrewMoralePenalty { get; set; }
        public bool MilestoneReached { get; set; }
    }
}
