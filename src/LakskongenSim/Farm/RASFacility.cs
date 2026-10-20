using System;
using System.Collections.Generic;
using LakskongenSim.Core;
namespace LakskongenSim.Farm
{
    /// <summary>
    /// Recirculating Aquaculture System (RAS) — Land-based closed containment facility.
    /// Based on real data from Atlantic Sapphire, Salmon Evolution, Andfjord Salmon.
    /// 
    /// Key characteristics:
    /// - No sea lice, no escapes, year-round controlled production
    /// - Higher CAPEX (500M-2B NOK) and OPEX (energy, water treatment)
    /// - Higher FCR (~1.3 vs 1.15 open net) due to lower water flow
    /// - Full environmental control: temperature, oxygen, pH, CO2
    /// </summary>
    public class RASFacility
    {
        // ─── Identity ───
        public string Id { get; init; } = "";
        public string Name { get; set; } = "RAS Anlegg";
        public string ZoneId { get; set; } = "";
        public RASScale Scale { get; set; } = RASScale.Medium;

        /// <summary>
        /// What the land site produces (F2-03): grow-out, or post-smolt for the
        /// sea sites (a pre-F2 post-smolt facility migrates to one).
        /// </summary>
        public LandSiteKind Kind { get; set; } = LandSiteKind.GrowOut;

        /// <summary>
        /// The land pad the site stands on ("pad_a"), "" for a facility a pre-F2
        /// save has not yet placed (F2-03's migration gives it a pad); its
        /// <see cref="ZoneId"/> is the pad's (<see cref="SitePlot"/>).
        /// </summary>
        public string PlotId { get; set; } = "";

        // ─── Capacity & Production ───
        /// <summary>Maximum annual production capacity in tonnes.</summary>
        public double CapacityTonnesPerYear { get; set; } = 8_000; // Medium-scale (Salmon Evolution = 7,900t)
        
        /// <summary>Current fish populations in the facility.</summary>
        public List<Biology.FishPopulation> Tanks { get; set; } = new();
        
        /// <summary>Number of grow-out tanks.</summary>
        public int TankCount { get; set; } = 12;
        
        /// <summary>Volume per tank in m³.</summary>
        public double TankVolumeM3 { get; set; } = 3_000; // Typical large RAS tank

        // ─── Water Treatment System ───
        /// <summary>Target water temperature (°C). RAS can maintain optimal year-round.</summary>
        public double TargetWaterTemp { get; set; } = 12.0; // Optimal growth: 12-14°C
        
        /// <summary>Current water temperature (°C).</summary>
        public double CurrentWaterTemp { get; set; } = 12.0;
        
        /// <summary>Dissolved oxygen level (mg/L). Target: 7-9 mg/L.</summary>
        public double OxygenLevel { get; set; } = 8.0;
        
        /// <summary>Water pH. Target: 6.8-7.2 for salmon.</summary>
        public double WaterPH { get; set; } = 7.0;
        
        /// <summary>CO2 level (mg/L). Must stay below 15 mg/L.</summary>
        public double CO2Level { get; set; } = 8.0;
        
        /// <summary>Biofilter efficiency (0-1). Converts ammonia to nitrate.</summary>
        public double BiofilterEfficiency { get; set; } = 0.92;
        
        /// <summary>UV treatment active for pathogen control.</summary>
        public bool UVTreatmentActive { get; set; } = true;
        
        /// <summary>Ozone treatment active for water quality.</summary>
        public bool OzoneTreatmentActive { get; set; } = true;
        
        /// <summary>Water recirculation rate (%). Typically 95-99%.</summary>
        public double RecirculationRate { get; set; } = 0.97; // 97% water reuse

        // ─── Energy & Costs ───
        /// <summary>Daily energy consumption in kWh.</summary>
        public double DailyEnergyKWh { get; set; } = 50_000; // ~18 GWh/year for medium facility
        
        /// <summary>Energy price NOK/kWh.</summary>
        public decimal EnergyPricePerKWh { get; set; } = 0.80m; // Norwegian industrial rate
        
        /// <summary>RAS-specific FCR (higher than open net due to lower exercise/flow).</summary>
        public double FCR { get; set; } = 1.30; // vs 1.15 open net

        // ─── Financial ───
        /// <summary>Total CAPEX invested (NOK).</summary>
        public decimal CAPEX { get; set; }
        
        /// <summary>Construction progress (0-1). Facility operational when = 1.</summary>
        public double ConstructionProgress { get; set; } = 0.0;
        
        /// <summary>Days to build from scratch.</summary>
        public int ConstructionDays { get; set; } = 730; // ~2 years typical
        
        /// <summary>Day construction started.</summary>
        public int ConstructionStartDay { get; set; } = -1;
        
        /// <summary>Is facility fully operational?</summary>
        public bool IsOperational => ConstructionProgress >= 1.0;

        // Phase 5.4: Carbon ledger
        /// <summary>Carbon emissions/credits tracker for this RAS facility.</summary>
        public CarbonTracker CarbonTracker { get; set; } = new();

        // ─── Status ───
        public int DaysSinceLastMaintenance { get; set; } = 0;
        public int MaintenanceIntervalDays { get; set; } = 90; // Quarterly biofilter maintenance
        public bool NeedsMaintenance => DaysSinceLastMaintenance >= MaintenanceIntervalDays;
        public int EmployeeCount { get; set; } = 35; // Medium RAS: 30-40 FTEs

        // ─── Events (not serialized) ───
        public event Action<string>? OnAlert;
        public event Action<string>? OnMilestone;

        // ─── Deterministic RNG (SimRngStreams.RASFacility, per-facility suffix) ───
        private SimRngStream _rng = SimRng.CreateUnseeded().Stream(SimRngStreams.RASFacility);
        internal SimRngStream RngForSnapshot => _rng;

        public RASFacility() { }

        public RASFacility(SimRngStream rng)
        {
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        }

        /// <summary>Bind this facility to the engine's named stream (per-facility identity).</summary>
        public void SetRng(SimRngStream rng) => _rng = rng ?? throw new ArgumentNullException(nameof(rng));

        /// <summary>
        /// Daily tick: update water chemistry, energy costs, construction progress.
        /// </summary>
        public RASTickResult TickDay(int currentDay, double ambientTemp)
        {
            var result = new RASTickResult();

            // Construction phase
            if (!IsOperational)
            {
                if (ConstructionStartDay >= 0)
                {
                    int elapsed = currentDay - ConstructionStartDay;
                    ConstructionProgress = Math.Min(1.0, (double)elapsed / ConstructionDays);
                    if (IsOperational)
                    {
                        OnMilestone?.Invoke($"RAS-anlegget '{Name}' er ferdigstilt og operativt!");
                        result.MilestoneReached = true;
                    }
                }
                return result;
            }

            // ─── Water Chemistry ───
            // Temperature control (energy cost scales with delta from ambient)
            double tempDelta = Math.Abs(TargetWaterTemp - ambientTemp);
            double heatingCoolingFactor = 1.0 + (tempDelta * 0.05); // 5% more energy per °C difference
            CurrentWaterTemp = TargetWaterTemp + (_rng.NextDouble() - 0.5) * 0.3; // ±0.15°C noise

            // Oxygen management
            double biomassKg = GetTotalBiomassKg();
            double oxygenDemand = biomassKg * 0.004; // ~4g O2/kg fish/hour baseline
            OxygenLevel = Math.Max(5.0, 8.5 - (oxygenDemand / (TankCount * TankVolumeM3) * 0.001));

            // CO2 buildup (fish respiration)
            CO2Level = 6.0 + (biomassKg / (TankCount * TankVolumeM3) * 0.5);
            if (CO2Level > 15.0)
            {
                OnAlert?.Invoke("ADVARSEL: CO2-nivå over 15 mg/L! Akutt risiko for fisken.");
                result.CO2Critical = true;
            }

            // Biofilter degradation over time without maintenance
            if (NeedsMaintenance)
            {
                BiofilterEfficiency = Math.Max(0.70, BiofilterEfficiency - 0.001); // Slow degradation
                if (BiofilterEfficiency < 0.80)
                {
                    OnAlert?.Invoke("ADVARSEL: Biofilter-effektivitet under 80%. Vedlikehold nødvendig!");
                    result.BiofilterDegraded = true;
                }
            }

            // ─── Energy Costs ───
            double adjustedEnergy = DailyEnergyKWh * heatingCoolingFactor;
            if (UVTreatmentActive) adjustedEnergy *= 1.05;
            if (OzoneTreatmentActive) adjustedEnergy *= 1.03;
            result.DailyEnergyCost = (decimal)adjustedEnergy * EnergyPricePerKWh;
            result.DailyEnergyKWh = adjustedEnergy;

            // ─── Operating Costs ───
            // Staff costs (~650K NOK/year per employee)
            result.DailyStaffCost = (decimal)EmployeeCount * 650_000m / LakskongenSim.Core.SimCalendar.DaysPerYear;
            
            // Consumables (chemicals, oxygen, filters)
            result.DailyConsumablesCost = (decimal)(biomassKg * 0.02); // ~20 øre/kg biomass/day
            
            // Water intake (even with 97% recirculation, need fresh water)
            double freshWaterM3 = TankCount * TankVolumeM3 * (1.0 - RecirculationRate);
            result.DailyWaterCost = (decimal)(freshWaterM3 * 5.0); // ~5 NOK/m³

            result.TotalDailyOPEX = result.DailyEnergyCost + result.DailyStaffCost 
                                  + result.DailyConsumablesCost + result.DailyWaterCost;

            // ─── Maintenance tracking ───
            DaysSinceLastMaintenance++;

            // ─── Advantages: No sea lice, no escapes ───
            result.SeaLiceRisk = 0.0; // Zero!
            result.EscapeRisk = 0.0;  // Zero!

            return result;
        }

        /// <summary>Perform scheduled maintenance on biofilter and systems.</summary>
        public decimal PerformMaintenance()
        {
            DaysSinceLastMaintenance = 0;
            BiofilterEfficiency = Math.Min(0.95, BiofilterEfficiency + 0.10);
            
            // Maintenance cost: ~500K-1M NOK per event
            decimal cost = 500_000m + (decimal)(_rng.NextDouble() * 500_000);
            OnMilestone?.Invoke($"Vedlikehold gjennomført på '{Name}'. Biofilter: {BiofilterEfficiency:P0}");
            return cost;
        }

        /// <summary>Start construction of the facility.</summary>
        public void StartConstruction(int currentDay)
        {
            ConstructionStartDay = currentDay;
            ConstructionProgress = 0.0;
            CAPEX = GetCAPEX();
        }

        /// <summary>Get total biomass across all tanks (kg).</summary>
        public double GetTotalBiomassKg()
        {
            double total = 0;
            foreach (var pop in Tanks)
            {
                foreach (var cohort in pop.Cohorts)
                    total += cohort.Count * cohort.MeanWeightKg;
            }
            return total;
        }

        /// <summary>Nameplate production capacity (tonnes/year) for a facility scale.</summary>
        public static double DefaultCapacityTonnesPerYear(RASScale scale) => scale switch
        {
            RASScale.Small => 5_000,
            RASScale.Medium => 8_000,   // Salmon Evolution phase 1 ≈ 7,900 t
            RASScale.Large => 20_000,   // Atlantic Sapphire target
            _ => 8_000
        };

        /// <summary>Get CAPEX based on facility scale.</summary>
        public decimal GetCAPEX() => Scale switch
        {
            RASScale.Small => 500_000_000m,    // 500M NOK — pilot/demo (3,000t/yr)
            RASScale.Medium => 1_200_000_000m,  // 1.2B NOK — commercial (8,000t/yr, Salmon Evolution)
            RASScale.Large => 2_000_000_000m,   // 2B NOK — mega facility (20,000t/yr, Atlantic Sapphire)
            _ => 1_200_000_000m
        };

        /// <summary>Get annual OPEX estimate based on current configuration.</summary>
        public decimal EstimatedAnnualOPEX =>
            (decimal)DailyEnergyKWh * EnergyPricePerKWh * LakskongenSim.Core.SimCalendar.DaysPerYear  // Energy
            + (decimal)EmployeeCount * 650_000m                   // Staff
            + 15_000_000m;                                        // Consumables & maintenance
    }

    public enum RASScale
    {
        Small,   // 3,000 t/yr — Pilot/Demo
        Medium,  // 8,000 t/yr — Commercial (Salmon Evolution)
        Large    // 20,000 t/yr — Mega (Atlantic Sapphire target)
    }

    public class RASTickResult
    {
        public decimal DailyEnergyCost { get; set; }
        public double DailyEnergyKWh { get; set; }
        public decimal DailyStaffCost { get; set; }
        public decimal DailyConsumablesCost { get; set; }
        public decimal DailyWaterCost { get; set; }
        public decimal TotalDailyOPEX { get; set; }
        public double SeaLiceRisk { get; set; }
        public double EscapeRisk { get; set; }
        public bool CO2Critical { get; set; }
        public bool BiofilterDegraded { get; set; }
        public bool MilestoneReached { get; set; }
    }
}
