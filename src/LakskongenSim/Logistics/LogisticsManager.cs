using System;
using System.Collections.Generic;

namespace LakskongenSim.Logistics
{
    public class LogisticsManager
    {
        public List<Vehicle> Fleet { get; } = new List<Vehicle>();
        public List<Inventory> Inventories { get; } = new List<Inventory>();

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
    }
}