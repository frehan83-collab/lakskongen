namespace LakskongenSim.Logistics
{
    public class Truck : Vehicle
    {
        public double CapacityKg { get; }
        public double FixedCostPerTrip { get; }

        public Truck(string id, double capacityKg, double speedKmPerH, double costPerKm, double fixedCostPerTrip = 0, FuelType fuelType = FuelType.Diesel)
            : base(id, speedKmPerH, costPerKm, fuelType)
        {
            CapacityKg = capacityKg;
            FixedCostPerTrip = fixedCostPerTrip;
        }

        public double TripCost(double distanceKm, double loadKg)
        {
            // cost scales with distance and includes fixed trip cost
            var distCost = TravelCost(distanceKm);
            var loadFactor = System.Math.Min(1.0, loadKg / CapacityKg);
            return FixedCostPerTrip + distCost * (0.5 + 0.5 * loadFactor); // partial loads slightly cheaper per kg
        }
    }
}