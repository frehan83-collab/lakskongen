using System;
using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.FacilityFinance;

/// <summary>
/// F2-04 acceptance 2 (docs/FARM_TYPES_SPEC.md §3.6): the resource-rent base is
/// ring-fenced. On a mixed company, the land site's opex, feed and smolts and
/// the F2 units' depreciation never lower the tax on the sea's rent (the rent
/// is keyed on the licence, not the company: V:3, V:4, V:16).
/// </summary>
public class RentRingFenceTests
{
    private readonly ITestOutputHelper _output;
    public RentRingFenceTests(ITestOutputHelper output) => _output = output;

    private const string Scenario = "ressursrente";

    private static SimulationEngine NewGame(int seed, string? scenario = Scenario)
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: seed);
        engine.State.RandomSeed = seed;
        if (scenario != null)
            Assert.True(engine.StartScenario(scenario).Ok);
        return engine;
    }

    /// <summary>A grow-out land site, commissioned and stocked today.</summary>
    private static RASFacility AddStockedLandSite(SimulationEngine engine)
    {
        engine.ReceiveLoan(2_000_000_000m);
        var built = engine.BuildRASFacility("Land", RASScale.Medium);
        Assert.True(built.Ok, built.Message);
        var site = engine.RASFacilities.Single(f => f.Id == built.Id);
        site.ConstructionProgress = 1.0;
        var stocked = engine.StockRASFacility(site.Id, 200_000);
        Assert.True(stocked.Ok, stocked.Message);
        return site;
    }

    private static double MeanWeight(SeaSite site)
    {
        int n = site.Pens.Sum(p => p.TotalCount);
        return n > 0 ? site.Pens.Sum(p => p.GetTotalBiomassKg()) / n : 0.0;
    }

    /// <summary>Advance <paramref name="days"/>, harvesting every fjord site that reaches 4.5 kg (rent-liable kilos).</summary>
    private static void Play(SimulationEngine engine, int days)
    {
        for (int d = 0; d < days; d++)
        {
            engine.Advance(1);
            foreach (var site in engine.FjordSites().ToList())
                if (MeanWeight(site) >= 4.5)
                    engine.HarvestSite(site.Id);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(13)]
    public void OnAMixedCompany_TheLandSitesCosts_NeverLowerTheSeaRentBase(int seed)
    {
        var fjordOnly = NewGame(seed);
        var mixed = NewGame(seed);
        fjordOnly.ReceiveLoan(2_000_000_000m); // the same cash, so nothing else differs
        AddStockedLandSite(mixed);
        Play(fjordOnly, 300);
        Play(mixed, 300);

        var a = fjordOnly.FinancialStatements;
        var b = mixed.FinancialStatements;
        decimal landCosts = (b.COGS + b.OperatingExpenses) - (a.COGS + a.OperatingExpenses);
        _output.WriteLine($"seed {seed}: harvest value {fjordOnly.State.ResourceRentHarvestValueThisTaxYear:N0} / {mixed.State.ResourceRentHarvestValueThisTaxYear:N0}; " +
            $"land costs {landCosts:N0}; ring-fenced {mixed.State.RentRingFencedCostsThisTaxYear:N0}; " +
            $"rent base {fjordOnly.ResourceRentBaseThisTaxYear():N0} / {mixed.ResourceRentBaseThisTaxYear():N0}");

        // The sea is the same in both games.
        Assert.Equal(fjordOnly.State.ResourceRentHarvestValueThisTaxYear, mixed.State.ResourceRentHarvestValueThisTaxYear);
        Assert.True(fjordOnly.State.ResourceRentHarvestValueThisTaxYear > 0m, "no rent-liable harvest in 300 days");
        // The land site cost real money (opex, feed, smolts) and lowered the company's profit ...
        Assert.True(landCosts > 10_000_000m, $"land costs only {landCosts:N0} NOK");
        Assert.True(b.PreTaxIncome < a.PreTaxIncome - 10_000_000m);
        Assert.True(Math.Abs(landCosts - mixed.State.RentRingFencedCostsThisTaxYear) < 0.000001m,
            $"land costs {landCosts} but ring-fenced {mixed.State.RentRingFencedCostsThisTaxYear}");
        // ... and none of it reached the rent base.
        Assert.True(Math.Abs(fjordOnly.ResourceRentBaseThisTaxYear() - mixed.ResourceRentBaseThisTaxYear()) < 0.000001m,
            $"rent base {fjordOnly.ResourceRentBaseThisTaxYear()} vs {mixed.ResourceRentBaseThisTaxYear()}");

        // The year-end settlement taxes the same rent and less corporate income.
        var settledA = fjordOnly.SettleAnnualTax();
        var settledB = mixed.SettleAnnualTax();
        Assert.True(Math.Abs(settledA.ResourceRentBase - settledB.ResourceRentBase) < 0.000001m);
        Assert.True(settledA.ResourceRentBase > 0m);
        Assert.Equal(settledA.ResourceRentTax, settledB.ResourceRentTax);
        Assert.True(settledB.PreTaxIncome < settledA.PreTaxIncome);
        Assert.Equal(0m, mixed.State.RentRingFencedCostsThisTaxYear);
    }

    [Fact]
    public void DepreciationOfF2Units_NeverEntersTheRentBase()
    {
        var plain = NewGame(4);
        var depreciating = NewGame(4);
        plain.ReceiveLoan(500_000_000m);
        depreciating.ReceiveLoan(500_000_000m);
        depreciating.RegisterFacilityAsset("ras_x", FacilityAssetKind.LandSite, 730_000_000m, 0);
        depreciating.RegisterFacilityAsset("offshore_x", FacilityAssetKind.OffshoreUnit, 912_500_000m, 0);
        Play(plain, 200);
        Play(depreciating, 200);

        Assert.True(depreciating.FinancialStatements.Depreciation > 0m);
        Assert.Equal(plain.ResourceRentBaseThisTaxYear(), depreciating.ResourceRentBaseThisTaxYear());
        Assert.Equal(plain.FinancialStatements.PreTaxIncome - depreciating.FinancialStatements.Depreciation,
            depreciating.FinancialStatements.PreTaxIncome);
    }

    [Fact]
    public void TheRentBase_IsTheLiableHarvestLessTheSeaPhaseCosts_WithoutTheRingFencedOnes()
    {
        var engine = NewGame(2, scenario: null);
        engine.State.ResourceRentHarvestValueThisTaxYear = 400_000_000m;
        engine.PayCost(100_000_000m, CostBooking.Cogs, "feed");           // a sea-phase cost
        engine.PayCost(20_000_000m, CostBooking.Cogs, "harvest");         // the harvest cost: never a deduction
        engine.State.HarvestCostThisTaxYear += 20_000_000m;
        engine.PayCost(30_000_000m, CostBooking.Opex, "operations");      // a land site's opex ...
        engine.AttributeUnitCost("ras_1", HoldingKind.LandTank, 30_000_000m);
        engine.PayCost(5_000_000m, CostBooking.Opex, "operations");       // ... an offshore unit's, rent-liable
        engine.AttributeUnitCost("offshore_1", HoldingKind.Offshore, 5_000_000m);
        engine.PayCost(7_000_000m, CostBooking.Opex, "operations");       // ... a fjord site's
        engine.AttributeUnitCost("site_1", HoldingKind.SeaPen, 7_000_000m);

        Assert.Equal(30_000_000m, engine.State.RentRingFencedCostsThisTaxYear);
        decimal companyCosts = engine.FinancialStatements.COGS + engine.FinancialStatements.OperatingExpenses;
        Assert.True(companyCosts >= 162_000_000m);
        // 400 M − (all costs − harvest 20 M − land 30 M).
        Assert.Equal(400_000_000m - (companyCosts - 20_000_000m - 30_000_000m), engine.ResourceRentBaseThisTaxYear());
        var settlement = engine.SettleAnnualTax();
        Assert.Equal(400_000_000m - (companyCosts - 50_000_000m), settlement.ResourceRentBase);
        Assert.Equal(0m, engine.State.RentRingFencedCostsThisTaxYear);
    }

    [Fact]
    public void TheRingFence_SurvivesASave()
    {
        var engine = NewGame(3);
        AddStockedLandSite(engine);
        Play(engine, 40);
        Assert.True(engine.State.RentRingFencedCostsThisTaxYear > 0m);
        var loaded = Integration.SaveStateComparer.Resume(Integration.SaveStateComparer.Serialize(engine), events: false);
        Assert.Equal(engine.State.RentRingFencedCostsThisTaxYear, loaded.State.RentRingFencedCostsThisTaxYear);
        Assert.Equal(engine.ResourceRentBaseThisTaxYear(), loaded.ResourceRentBaseThisTaxYear());
    }
}
