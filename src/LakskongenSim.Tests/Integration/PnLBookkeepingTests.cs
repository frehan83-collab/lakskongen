using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using Xunit;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// D1: the P&L finally sees the real money flows — harvest sales book
/// revenue, feed consumption books COGS — so the bridge's Revenue/Expenses
/// properties (and the UI gates built on them, like IPO eligibility) stop
/// reading lies.
/// </summary>
public class PnLBookkeepingTests
{
    private static SimulationEngine NewEngineWithSite(string siteId)
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 1);
        var zone = new Zone("zone-1");
        engine.State.Zones.Add(zone);
        engine.State.SeaSites.Add(new SeaSite(siteId, zone.Id));
        return engine;
    }

    [Fact]
    public void HarvestSite_BooksRevenueExactlyMatchingTheCashFlow()
    {
        var engine = NewEngineWithSite("site-1");
        Assert.True(engine.StockSmolts("site-1", 500, "standard").Ok);

        decimal before = engine.FinanceManager.Balance;
        decimal cogsBefore = engine.FinancialStatements.LifetimeCOGS;
        var result = engine.HarvestSite("site-1");
        Assert.True(result.Ok);
        decimal cashDelta = engine.FinanceManager.Balance - before;

        Assert.True(engine.FinancialStatements.Revenue > 0m, "revenue not booked");
        // S2-04, review F270: the harvest is not free. The harvest cost is booked
        // as cost of goods sold and paid from the sale, so the cash moves by the
        // revenue less that cost (it used to move by the whole revenue).
        var report = engine.HarvestReports[^1];
        Assert.True(report.HarvestCostNok > 0m, "the harvest cost nothing");
        Assert.Equal(report.GrossRevenueNok, engine.FinancialStatements.Revenue);
        Assert.Equal(report.HarvestCostNok, engine.FinancialStatements.LifetimeCOGS - cogsBefore);
        Assert.Equal(engine.FinancialStatements.Revenue - report.HarvestCostNok, cashDelta);
    }

    [Fact]
    public void FeedConsumption_BooksCOGS_DuringGrowOut()
    {
        var engine = NewEngineWithSite("site-1");
        Assert.True(engine.StockSmolts("site-1", 2_000, "standard").Ok);

        engine.Advance(30);

        Assert.True(engine.FinancialStatements.COGS > 0m,
            $"feed should have booked COGS over a month of grow-out (COGS={engine.FinancialStatements.COGS})");
    }

    [Fact]
    public void OperatingExpense_IsBooked_ForRegularSpending()
    {
        var engine = NewEngineWithSite("site-1");
        Assert.True(engine.StockSmolts("site-1", 500, "standard").Ok);
        decimal opexBefore = engine.FinancialStatements.OperatingExpenses;

        Assert.True(engine.TakeLoan(1_000_000, 24, 0.06).Ok);
        engine.Advance(30); // loan amortization books interest/principal as OpEx

        Assert.True(engine.FinancialStatements.OperatingExpenses > opexBefore,
            "amortization should book operating expenses over a month");
    }
}