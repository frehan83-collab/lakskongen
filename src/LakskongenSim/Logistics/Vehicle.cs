namespace LakskongenSim.Logistics
{
    public enum FuelType
    {
        Diesel,
        Electric,
        Hybrid,
        Biodiesel
    }

    public abstract class Vehicle
    {
        public string Id { get; }
        public double SpeedKmPerH { get; }
        public double CostPerKm { get; }
        public FuelType FuelType { get; set; } = FuelType.Diesel;

        protected Vehicle(string id, double speedKmPerH, double costPerKm, FuelType fuelType = FuelType.Diesel)
        {
            Id = id;
            SpeedKmPerH = speedKmPerH;
            CostPerKm = costPerKm;
            FuelType = fuelType;
        }

        public virtual double TravelTimeHours(double distanceKm) => distanceKm / SpeedKmPerH;
        public virtual double TravelCost(double distanceKm) => distanceKm * CostPerKm;
    }
}