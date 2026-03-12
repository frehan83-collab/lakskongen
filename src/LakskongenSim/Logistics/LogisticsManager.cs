using System;
using System.Collections.Generic;
using System.Linq;

namespace LakskongenSim.Logistics
{
    public class LogisticsManager
    {
        public List<Vehicle> Fleet { get; } = new List<Vehicle>();
        public List<Inventory> Inventories { get; } = new List<Inventory>();

        // Sustainability integration properties
        public float RenewableEnergyPercentage { get; set; } = 0.3f; // 30% renewable by default
        public bool HasLocalProcessing { get; set; } = true; // Reduces transport carbon
        public Dictionary<string, double> FuelUsageLog { get; } = new(); // vehicleId -> liters consumed

        public void RegisterVehicle(Vehicle v) => Fleet.Add(v);
        public void RegisterInventory(Inventory inv) => Inventories.Add(inv);

        // Simple scheduler: choose cheapest vehicle that can carry the load
        public Vehicle ChooseVehicleForLoad(double loadKg)
        {
            Vehicle best = null;
            double bestScore = double.MaxValue;
            foreach (var v in Fleet)
            {
                // Null check for vehicle
                if (v == null) continue;
                
                double capacity = v switch
                {
                    Truck t => t.CapacityKg,
                    Wellboat w => w.LiveCapacityKg,
                    _ => 0
                };
                if (capacity <= 0 || capacity < loadKg) continue;
                
                // Validate vehicle properties before calculating score
                if (v.SpeedKmPerH <= 0) continue;
                
                // score: cost per km (lower better) + inverse speed (faster preferred)
                double score = v.CostPerKm + (1.0 / v.SpeedKmPerH);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = v;
                }
            }
            return best;
        }

        public (Vehicle vehicle, double cost) ScheduleTransport((double lat, double lon) from, (double lat, double lon) to, double loadKg, double weatherMultiplier = 1.0)
        {
            var dist = RoutePlanner.EstimateDistanceKm(from, to);
            var vehicle = ChooseVehicleForLoad(loadKg);
            
            // Graceful handling when no vehicle is available
            if (vehicle == null)
            {
                // Return null vehicle with a high penalty cost to signal unavailability
                // Caller can check for null vehicle and handle appropriately
                return (null, double.MaxValue);
            }
            
            var cost = RoutePlanner.EstimateCost(vehicle, dist, weatherMultiplier);
            return (vehicle, cost);
        }

        /// <summary>
        /// Calculate carbon footprint score for sustainability system (0.0-1.0, higher is better).
        /// Based on renewable energy, transport efficiency, and vehicle fuel types.
        /// </summary>
        public float CarbonFootprintScore()
        {
            // 1. Renewable energy score (0-10 points)
            float energyScore = RenewableEnergyPercentage * 10.0f;

            // 2. Transport efficiency score (0-3 points)
            float transportScore = HasLocalProcessing ? 3.0f : 1.0f;

            // 3. Vessel fuel type score (0-2 points)
            float fuelScore = 0.0f;
            int electricCount = 0;
            int totalVehicles = 0;

            foreach (var vehicle in Fleet)
            {
                if (vehicle == null) continue;
                totalVehicles++;

                // Check for electric/hybrid vehicles (future property, defaulting to diesel)
                // In future: add vehicle.FuelType property
                // For now: assume all diesel (0 points)
                // if (vehicle.IsElectric || vehicle.IsHybrid) electricCount++;
            }

            if (totalVehicles > 0)
            {
                float electricRatio = (float)electricCount / totalVehicles;
                fuelScore = electricRatio * 2.0f;
            }

            // Total score (0-15 points → normalized to 0.0-1.0)
            float totalScore = (energyScore + transportScore + fuelScore) / 15.0f;
            return Math.Clamp(totalScore, 0.0f, 1.0f);
        }

        /// <summary>Track vehicle fuel usage for carbon reporting.</summary>
        public void RecordFuelUsage(string vehicleId, double liters, string fuelType = "Diesel")
        {
            if (!FuelUsageLog.ContainsKey(vehicleId))
                FuelUsageLog[vehicleId] = 0.0;
            
            FuelUsageLog[vehicleId] += liters;
        }
    }
}