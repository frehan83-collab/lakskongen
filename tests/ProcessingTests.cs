using System;
using Xunit;
using LakskongenSim.Processing;
using LakskongenSim.Market;

public class ProcessingEconomicsTests
{
    [Fact]
    public void UtilizationMultiplier_Behaviour_IsDeterministic()
    {
        Assert.Equal(0m, ProcessingManager.UtilizationMultiplier(0m));
        // low utilization
        var v1 = ProcessingManager.UtilizationMultiplier(0.15m);
        Assert.True(v1 > 0m && v1 < 0.6m);
        // moderate
        var v2 = ProcessingManager.UtilizationMultiplier(0.5m);
        Assert.InRange(v2, 0.6m, 0.95m);
        // near full
        var v3 = ProcessingManager.UtilizationMultiplier(1.0m);
        Assert.InRange(v3, 0.9m, 1.0m);
        // overload
        var v4 = ProcessingManager.UtilizationMultiplier(1.5m);
        Assert.InRange(v4, 0m, 1.0m);
    }

    [Fact]
    public void Process_CalculatesProfit_Correctly_ForKnownInputs()
    {
        var pm = new ProcessingManager(ProcessingTier.Small);
        // Input 10000 kg, produce fillets at raw price 30 NOK/kg
        var res = pm.Process(10000, ProductType.Fillet, 30m, 1.0m);
        // Known yield 0.40 -> output 4000 kg
        Assert.Equal(4000, res.OutputKg);
        // Revenue = 4000 * 150 = 600000
        Assert.Equal(600000m, res.Revenue);
        // Raw cost = 10000 * 30 = 300000
        Assert.Equal(300000m, res.RawMaterialCost);
        // Opex should be Fixed + variable: Fixed 800 + variable  (10 tonnes * 200 per tonne) = 800 + 2000 = 2800
        Assert.Equal(2800m, res.Opex);
        // Depreciation daily = Capex(1,000,000)/7/365
        var expectedDep = Math.Round((double)(1000000m / 7m / 365m), 10);
        Assert.Equal((decimal)expectedDep, res.Depreciation);
        // Profit = revenue - raw - opex - depreciation
        var expectedProfit = 600000m - 300000m - 2800m - (1000000m / 7m / 365m);
        Assert.Equal(expectedProfit, res.Profit);
    }

    [Fact]
    public void EstimatePaybackYears_ReturnsMaxValue_WhenNoProfit()
    {
        var pm = new ProcessingManager(ProcessingTier.Small);
        var years = pm.EstimatePaybackYears(0m, 0m);
        Assert.Equal(decimal.MaxValue, years);
    }
}
