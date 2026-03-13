namespace LakskongenSim.Logistics
{
    public class Wellboat : Vehicle
    {
        // capacity in live-fish units (kg)
        public double LiveCapacityKg { get; }
        public double PortAccessScore { get; }

        public Wellboat(string id, double liveCapacityKg, double speedKmPerH, double costPerKm, double portAccessScore = 1.0)
            : base(id, speedKmPerH, costPerKm)
        {
            LiveCapacityKg = liveCapacityKg;
            PortAccessScore = portAccessScore;
        }

        public double TripCost(double distanceKm, double loadKg)
        {
            var distCost = TravelCost(distanceKm);
            var loadFactor = System.Math.Min(1.0, loadKg / LiveCapacityKg);
            // wellboats have higher base cost and port access affects availability/cost
            var baseMultiplier = 1.5 / System.Math.Max(0.001, PortAccessScore); // guard against zero
            return distCost * baseMultiplier * (0.7 + 0.3 * loadFactor);
        }
    }
}