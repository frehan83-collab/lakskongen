using System;

namespace LakskongenSim.Logistics
{
    public static class RoutePlanner
    {
        // Simple distance-based cost estimator with weather/season multipliers
        public static double EstimateDistanceKm((double lat, double lon) a, (double lat, double lon) b)
        {
            // Haversine
            double R = 6371; // km
            double dLat = ToRad(b.lat - a.lat);
            double dLon = ToRad(b.lon - a.lon);
            double lat1 = ToRad(a.lat);
            double lat2 = ToRad(b.lat);
            double sinDLat = System.Math.Sin(dLat/2);
            double sinDLon = System.Math.Sin(dLon/2);
            double h = sinDLat*sinDLat + sinDLon*sinDLon * System.Math.Cos(lat1)*System.Math.Cos(lat2);
            double c = 2*System.Math.Asin(System.Math.Min(1, System.Math.Sqrt(h)));
            return R * c;
        }

        public static double EstimateCost(Vehicle vehicle, double distanceKm, double weatherMultiplier = 1.0)
        {
            return vehicle.TravelCost(distanceKm) * weatherMultiplier;
        }

        private static double ToRad(double deg) => deg * System.Math.PI / 180.0;
    }
}