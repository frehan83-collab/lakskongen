using System;
using LakskongenSim.Logistics;

class Program
{
    static int Main()
    {
        try
        {
            Console.WriteLine("Logistics deterministic tests start");

            var supplier = new Supplier("FeedCo", 10.0, 0.95, 3);
            var inv = new Inventory("Feed", 500, 200, 1000, supplier);
            if (!inv.NeedsReorder()) Console.WriteLine("PASS: initial inventory not needing reorder"); else { Console.WriteLine("FAIL: initial inventory should not need reorder"); return 1; }

            inv.Consume(350); // leaves 150
            if (inv.NeedsReorder()) Console.WriteLine("PASS: inventory needs reorder after consumption"); else { Console.WriteLine("FAIL: expected reorder"); return 1; }

            var order = inv.CreateReorder(10);
            if (order == null) { Console.WriteLine("FAIL: order should have been created"); return 1; }
            if (order.ExpectedDeliveryDay != 13) { Console.WriteLine($"FAIL: expected delivery day 13 got {order.ExpectedDeliveryDay}"); return 1; }
            Console.WriteLine("PASS: reorder created with correct expected delivery day");

            var manager = new LogisticsManager();
            var truck = new Truck("T1", 2000, 80, 2.0, 100);
            var wellboat = new Wellboat("W1", 50000, 20, 10.0, 0.8);
            manager.RegisterVehicle(truck);
            manager.RegisterVehicle(wellboat);

            var (v, cost) = manager.ScheduleTransport((60.4, 5.3), (62.0, 5.5), 1000);
            Console.WriteLine($"Scheduled vehicle: {v.Id}, estimated cost: {cost:F2}");
            if (v is Truck) Console.WriteLine("PASS: truck chosen for 1000kg load"); else { Console.WriteLine("FAIL: expected truck for 1000kg"); return 1; }

            // Cost determinism: calculate distance independently
            var dist = RoutePlanner.EstimateDistanceKm((60.4,5.3),(62.0,5.5));
            var expectedCost = v.TravelCost(dist);
            if (Math.Abs(expectedCost - RoutePlanner.EstimateCost(v, dist)) < 1e-6) Console.WriteLine("PASS: cost estimator deterministic"); else { Console.WriteLine("FAIL: cost mismatch"); return 1; }

            Console.WriteLine("All logistics tests passed");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("EXCEPTION: " + ex);
            return 2;
        }
    }
}
