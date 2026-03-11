namespace LakskongenSim.Logistics
{
    public abstract class Vehicle
    {
        public string Id { get; }
        public double SpeedKmPerH { get; }
        public double CostPerKm { get; }

        protected Vehicle(string id, double speedKmPerH, double costPerKm)
        {
            Id = id;
            SpeedKmPerH = speedKmPerH;
            CostPerKm = costPerKm;
        }

        public virtual double TravelTimeHours(double distanceKm) => distanceKm / SpeedKmPerH;
        public virtual double TravelCost(double distanceKm) => distanceKm * CostPerKm;
    }
}