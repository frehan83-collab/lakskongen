using LakskongenSim.Logistics;
using Xunit;

namespace LakskongenSim.LogisticsTests;

public class LogisticsTests
{
    [Fact]
    public void Inventory_ReorderThreshold_Works()
    {
        var supplier = new Supplier("FeedCo", 10.0, 0.95, 3);
        var inv = new Inventory("Feed", 500, 200, 1000, supplier);
        Assert.False(inv.NeedsReorder());

        inv.Consume(350); // leaves 150
        Assert.True(inv.NeedsReorder());
    }

    [Fact]
    public void Inventory_Reorder_HasExpectedDeliveryDay()
    {
        var supplier = new Supplier("FeedCo", 10.0, 0.95, 3);
        var inv = new Inventory("Feed", 500, 200, 1000, supplier);
        inv.Consume(350);

        var order = inv.CreateReorder(10);
        Assert.NotNull(order);
        Assert.Equal(13, order.ExpectedDeliveryDay);
    }

    [Fact]
    public void ScheduleTransport_ChoosesTruckForSmallLoad()
    {
        var manager = new LogisticsManager();
        var truck = new Truck("T1", 2000, 80, 2.0, 100);
        var wellboat = new Wellboat("W1", 50000, 20, 10.0, 0.8);
        manager.RegisterVehicle(truck);
        manager.RegisterVehicle(wellboat);

        var (v, _) = manager.ScheduleTransport((60.4, 5.3), (62.0, 5.5), 1000);
        Assert.NotNull(v);
        Assert.IsType<Truck>(v);
    }

    [Fact]
    public void CostEstimator_IsDeterministic()
    {
        var truck = new Truck("T1", 2000, 80, 2.0, 100);
        var dist = RoutePlanner.EstimateDistanceKm((60.4, 5.3), (62.0, 5.5));
        var expectedCost = truck.TravelCost(dist);
        Assert.True(Math.Abs(expectedCost - RoutePlanner.EstimateCost(truck, dist)) < 1e-6);
    }
}
