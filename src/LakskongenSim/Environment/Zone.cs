using System;

namespace LakskongenSim.Environment
{
    /// <summary>
    /// Represents a management zone in the simulation with water chemistry integration.
    /// </summary>
    public class Zone
    {
        public string Id { get; init; }
        public double CarryingCapacityTonnes { get; set; }
        public WaterChemistry WaterChemistry { get; private set; }
        public double CurrentFishBiomassKg { get; set; }
        
        // Zone-specific parameters
        public double WaterFlowRate { get; set; } // m³/day (current speed affects dilution)
        public double FeedConversionRatio { get; set; } // FCR for fish in this zone

        public Zone(string id, double temperature = 10.0)
        {
            Id = id;
            CarryingCapacityTonnes = 1000.0;
            WaterChemistry = new WaterChemistry(temperature);
            CurrentFishBiomassKg = 0.0;
            WaterFlowRate = 1000.0; // Moderate flow
            FeedConversionRatio = 1.2; // Typical Norwegian FCR
        }

        public void AdvanceDays(int days)
        {
            for (int day = 0; day < days; day++)
            {
                // Update water chemistry based on current fish biomass
                WaterChemistry.UpdateOneDay(CurrentFishBiomassKg, FeedConversionRatio, WaterFlowRate);
                
                // TODO: implement currents, eutrophication, algal blooms
                // TODO: integrate with fish growth via growth penalty
            }
        }
        
        /// <summary>
        /// Get current zone status including water chemistry and carrying capacity.
        /// </summary>
        public ZoneStatus GetStatus()
        {
            var chemStatus = WaterChemistry.GetStatus();
            return new ZoneStatus
            {
                Id = Id,
                CarryingCapacityTonnes = CarryingCapacityTonnes,
                CurrentFishBiomassKg = CurrentFishBiomassKg,
                UtilizationPercent = CurrentFishBiomassKg / (CarryingCapacityTonnes * 1000.0) * 100.0,
                WaterChemistry = chemStatus,
                WaterFlowRate = WaterFlowRate,
                FeedConversionRatio = FeedConversionRatio
            };
        }
        
        /// <summary>
        /// Set zone temperature (affects water chemistry rates).
        /// </summary>
        public void SetTemperature(double temperature)
        {
            WaterChemistry.SetTemperature(temperature);
        }
    }
    
    /// <summary>
    /// Zone status snapshot for UI display and player decisions.
    /// </summary>
    public struct ZoneStatus
    {
        public string Id;
        public double CarryingCapacityTonnes;
        public double CurrentFishBiomassKg;
        public double UtilizationPercent;
        public WaterChemistryStatus WaterChemistry;
        public double WaterFlowRate;
        public double FeedConversionRatio;
    }
}