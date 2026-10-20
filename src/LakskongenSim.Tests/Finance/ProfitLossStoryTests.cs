using System.Collections.Generic;
using Xunit;
using LakskongenSim.Core;
using LakskongenSim.Finance;

namespace LakskongenSim.Tests
{
    /// <summary>
    /// Phase 5.5 "feel" layer: the categorised P&L ledger must (a) attribute the
    /// dominant cost drivers so "why did I lose money" can be answered, and
    /// (b) round-trip through the FinancialStatements export/import path exactly
    /// (deterministic bookkeeping, no RNG).
    /// </summary>
    public class ProfitLossStoryTests
    {
        [Fact]
        public void RecordMethods_AttributeCategories()
        {
            var fs = new FinancialStatements();
            fs.RecordRevenue(1_000_000m, "harvest");
            fs.RecordCOGS(500_000m, "feed");
            fs.RecordOperatingExpense(200_000m, "delousing");
            fs.RecordOperatingExpense(100_000m, "insurance");

            Assert.Equal(500_000m, fs.CostBreakdown["feed"]);
            Assert.Equal(200_000m, fs.CostBreakdown["delousing"]);
            Assert.Equal(100_000m, fs.CostBreakdown["insurance"]);
            Assert.Equal(1_000_000m, fs.RevenueBreakdown["harvest"]);
        }

        [Fact]
        public void TopCostDrivers_SortedDescending_ByAmount()
        {
            var fs = new FinancialStatements();
            fs.RecordCOGS(600_000m, "feed");
            fs.RecordOperatingExpense(200_000m, "delousing");
            fs.RecordOperatingExpense(50_000m, "insurance");

            var top = fs.GetTopCostDrivers(3);

            Assert.Equal(3, top.Count);
            Assert.Equal("feed", top[0].Category);
            Assert.Equal("delousing", top[1].Category);
            Assert.Equal("insurance", top[2].Category);
        }

        [Fact]
        public void TopCostDrivers_TiesBrokenByKey_Deterministic()
        {
            var fs = new FinancialStatements();
            fs.RecordOperatingExpense(100m, "b");
            fs.RecordOperatingExpense(100m, "a");

            var top = fs.GetTopCostDrivers(2);
            Assert.Equal("a", top[0].Category); // ordinal tie-break: "a" < "b"
            Assert.Equal("b", top[1].Category);
        }

        [Fact]
        public void Breakdowns_RoundTripThroughExportImport()
        {
            var fs = new FinancialStatements();
            fs.RecordCOGS(700_000m, "feed");
            fs.RecordOperatingExpense(150_000m, "delousing");
            fs.RecordRevenue(2_000_000m, "harvest");

            var costExport = fs.ExportCostBreakdown();
            var revenueExport = fs.ExportRevenueBreakdown();

            var restored = new FinancialStatements();
            restored.ImportBreakdowns(costExport, revenueExport);

            Assert.Equal(700_000m, restored.CostBreakdown["feed"]);
            Assert.Equal(150_000m, restored.CostBreakdown["delousing"]);
            Assert.Equal(2_000_000m, restored.RevenueBreakdown["harvest"]);
            // Determinism: exported dicts are key-ordered.
            Assert.Equal(new List<string> { "delousing", "feed" }, new List<string>(costExport.Keys));
        }

        [Fact]
        public void AnnualClose_ResetsBreakdowns()
        {
            var fs = new FinancialStatements();
            fs.RecordCOGS(500_000m, "feed");
            fs.RecordRevenue(900_000m, "harvest");

            fs.AnnualClose();

            Assert.Empty(fs.CostBreakdown);
            Assert.Empty(fs.RevenueBreakdown);
            Assert.Equal(0m, fs.COGS);
            Assert.Equal(0m, fs.Revenue);
        }
    }
}
