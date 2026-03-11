namespace LakskongenSim.Logistics
{
    public class Supplier
    {
        public string Name { get; }
        public double PricePerKg { get; }
        public double Reliability { get; } // 0-1
        public int LeadTimeDays { get; }

        public Supplier(string name, double pricePerKg, double reliability, int leadTimeDays)
        {
            Name = name;
            PricePerKg = pricePerKg;
            Reliability = reliability;
            LeadTimeDays = leadTimeDays;
        }
    }
}