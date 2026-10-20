using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using LakskongenSim.Tests.Integration;
using Xunit;

namespace LakskongenSim.Tests.FacilityFinance;

/// <summary>
/// F2-04 acceptance 1 (docs/FARM_TYPES_SPEC.md §3.6, §7.1): facility
/// depreciation is Σ capex / life per day for F2 units only, a non-cash P&amp;L
/// line in the corporate tax base; the ledger's cash reconciliation over 730
/// days and three seeds is unchanged, and non-F2 capex is untouched.
/// </summary>
public class FacilityAssetRegisterTests
{
    private const string Scenario = "po3_red_sone";

    private static SimulationEngine NewGame(int seed, bool events = false, string? scenario = Scenario)
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: events, seed: seed);
        engine.State.RandomSeed = seed;
        if (scenario != null)
            Assert.True(engine.StartScenario(scenario).Ok);
        return engine;
    }

    private static decimal Expected(IEnumerable<FacilityAsset> assets, int day)
        => assets.Sum(a => FacilityAssetRegister.AccumulatedThrough(a, day));

    /// <summary>Equal to a billionth of a krone (decimal sums round at their 28th digit).</summary>
    private static void AssertNok(decimal expected, decimal actual)
        => Assert.True(Math.Abs(expected - actual) < 0.000000001m, $"expected {expected} NOK, got {actual}");

    // ── The arithmetic ─────────────────────────────────────────────────────

    [Fact]
    public void Accumulated_IsCapexOverLifePerDay_FromTheStartDay_AndStopsAtTheCapex()
    {
        var asset = new FacilityAsset { UnitId = "u", CapexNok = 730_000_000m, StartDay = 100, LifeYears = 20 };
        decimal daily = FacilityAssetRegister.DailyNok(asset);
        Assert.Equal(100_000m, daily); // 730 M over 20 × 365 days

        Assert.Equal(0m, FacilityAssetRegister.AccumulatedThrough(asset, 99));
        Assert.Equal(daily, FacilityAssetRegister.AccumulatedThrough(asset, 100));
        Assert.Equal(daily * 365, FacilityAssetRegister.AccumulatedThrough(asset, 464));
        Assert.Equal(asset.CapexNok, FacilityAssetRegister.AccumulatedThrough(asset, 100 + 20 * 365 - 1));
        Assert.Equal(asset.CapexNok, FacilityAssetRegister.AccumulatedThrough(asset, 100 + 50 * 365));
    }

    [Fact]
    public void BookingOnceAfterManyDays_BooksWhatDailyBookingsBook_AndNeverTwice()
    {
        // An awkward capex (a 28-digit daily quotient) over an offshore life.
        static FacilityAsset Asset() => new() { UnitId = "u", CapexNok = 877_000_000m, StartDay = 3, LifeYears = 25 };
        var daily = new List<FacilityAsset> { Asset() };
        var once = new List<FacilityAsset> { Asset() };
        decimal dailyTotal = 0m;
        for (int day = 0; day <= 400; day++)
            dailyTotal += FacilityAssetRegister.BookThrough(daily, day);
        decimal onceTotal = FacilityAssetRegister.BookThrough(once, 400);

        Assert.Equal(onceTotal, dailyTotal);
        Assert.Equal(FacilityAssetRegister.AccumulatedThrough(Asset(), 400), dailyTotal);
        Assert.Equal(0m, FacilityAssetRegister.BookThrough(once, 400));
        Assert.Equal(0m, FacilityAssetRegister.BookThrough(once, 399));
        // Within a millionth of a krone of 398 days at capex / (25 × 365).
        Assert.True(Math.Abs(dailyTotal - 398m * 877_000_000m / 9_125m) < 0.000001m);
    }

    // ── Engine: F2 units enter the register at commissioning ──────────────────

    [Fact]
    public void TheBuildVerbs_PutF2UnitsOnTheRegister_FromTheirCommissioningDay()
    {
        var engine = NewGame(5, scenario: null);
        engine.ReceiveLoan(3_000_000_000m);
        engine.Advance(4);
        Assert.True(engine.BuildRASFacility("Land", RASScale.Medium).Ok);
        Assert.True(engine.BuildOffshoreFarm("Hav", OffshoreType.SemiSubmersible).Ok);
        Assert.True(engine.BuildPostSmoltFacility("Post").Ok);

        var ras = engine.RASFacilities.Single(f => f.Kind == LandSiteKind.GrowOut);
        var post = engine.PostSmoltSites.Single();
        var unit = engine.OffshoreUnits.Single();
        var byUnit = engine.State.FacilityAssets.ToDictionary(a => a.UnitId);
        Assert.Equal(3, byUnit.Count);
        Assert.Equal((FacilityAssetKind.LandSite, ras.CAPEX, 4 + ras.ConstructionDays, 20),
            (byUnit[ras.Id].Kind, byUnit[ras.Id].CapexNok, byUnit[ras.Id].StartDay, byUnit[ras.Id].LifeYears));
        Assert.Equal((FacilityAssetKind.LandSite, post.CAPEX, 4 + post.ConstructionDays, 20),
            (byUnit[post.Id].Kind, byUnit[post.Id].CapexNok, byUnit[post.Id].StartDay, byUnit[post.Id].LifeYears));
        Assert.Equal((FacilityAssetKind.OffshoreUnit, unit.Offshore!.CAPEX, 4 + unit.Offshore.ConstructionDays, 25),
            (byUnit[unit.Id].Kind, byUnit[unit.Id].CapexNok, byUnit[unit.Id].StartDay, byUnit[unit.Id].LifeYears));
        // Nothing is depreciated while they are being built.
        engine.Advance(60);
        Assert.Equal(0m, engine.FinancialStatements.LifetimeDepreciation);
        Assert.False(engine.FinancialStatements.CostBreakdown.ContainsKey(FinancialStatements.DepreciationCategory));
    }

    [Fact]
    public void ACommissionedUnit_BooksCapexOverLifeEachDay_AsANonCashLine()
    {
        var engine = NewGame(6, scenario: null);
        engine.ReceiveIncome(50_000_000m, "harvest");
        engine.Advance(2);
        decimal assetsBefore = engine.FinancialStatements.Assets;
        decimal cashBefore = engine.FinanceManager.Balance;
        var asset = engine.RegisterFacilityAsset("ras_9", FacilityAssetKind.LandSite, 730_000_000m, engine.State.Day)!;

        // Registered on its start day: today's depreciation is booked at once.
        Assert.Equal(100_000m, engine.FinancialStatements.Depreciation);
        Assert.Equal(cashBefore, engine.FinanceManager.Balance);
        engine.Advance(20);

        Assert.Equal(21 * 100_000m, asset.DepreciatedNok);
        Assert.Equal(21 * 100_000m, engine.FinancialStatements.Depreciation);
        Assert.Equal(21 * 100_000m, engine.FinancialStatements.LifetimeDepreciation);
        Assert.Equal(21 * 100_000m, engine.FinancialStatements.CostBreakdown[FinancialStatements.DepreciationCategory]);
        Assert.Equal(assetsBefore - 21 * 100_000m, engine.FinancialStatements.Assets);
        Assert.Equal(engine.State.Day, asset.DepreciatedThroughDay);
        Assert.Equal(0m, engine.UnbookedCashTotal);
    }

    [Fact]
    public void Depreciation_LowersTheCorporateTaxBase_ByExactlyItself()
    {
        static AnnualTaxSettlement Settle(bool withAsset)
        {
            var engine = NewGame(8, scenario: null);
            engine.ReceiveIncome(500_000_000m, "harvest");
            if (withAsset)
                engine.RegisterFacilityAsset("offshore_9", FacilityAssetKind.OffshoreUnit, 912_500_000m, engine.State.Day);
            engine.Advance(10);
            return engine.SettleAnnualTax();
        }
        var plain = Settle(false);
        var depreciating = Settle(true);
        decimal depreciation = 11 * 912_500_000m / (25 * 365m); // days 0..10
        Assert.Equal(plain.PreTaxIncome - depreciation, depreciating.PreTaxIncome);
        Assert.Equal(plain.CorporateTax - depreciation * 0.22m, depreciating.CorporateTax);
        // ... and never the resource-rent base (F2-04 acceptance 2).
        Assert.Equal(plain.ResourceRentBase, depreciating.ResourceRentBase);
    }

    [Fact]
    public void TheAnnualClose_ResetsTheYearsDepreciation_ButNotTheLifetimeOrTheRegister()
    {
        var engine = NewGame(9, scenario: null);
        engine.ReceiveLoan(100_000_000m);
        var asset = engine.RegisterFacilityAsset("ras_9", FacilityAssetKind.LandSite, 73_000_000m, 0)!;
        engine.Advance(370); // through 31 December (day 364)
        Assert.Equal(370 * 10_000m + 10_000m, asset.DepreciatedNok); // days 0..370
        Assert.Equal(6 * 10_000m, engine.FinancialStatements.Depreciation); // days 365..370
        Assert.Equal(asset.DepreciatedNok, engine.FinancialStatements.LifetimeDepreciation);
    }

    // ── Acceptance 1: 730 days × 3 seeds, cash still reconciles ───────────────

    private static double SiteMeanWeight(SeaSite site)
    {
        int count = site.Pens.Sum(p => p.TotalCount);
        return count > 0 ? site.Pens.Sum(p => p.GetTotalBiomassKg()) / count : 0.0;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(13)]
    public void A730DayCareerWithF2Units_DepreciatesCapexOverLifePerDay_AndCashStillReconciles(int seed)
    {
        var engine = NewGame(seed, events: true);
        engine.ReceiveLoan(2_000_000_000m);
        Assert.True(engine.BuildPostSmoltFacility("Post").Ok);           // commissioned on day 548
        Assert.True(engine.BuildRASFacility("Land", RASScale.Small).Ok);  // on day 730
        Assert.True(engine.BuildOffshoreFarm("Hav", OffshoreType.SemiSubmersible).Ok); // after the run
        engine.RegisterFacilityAsset("land_bought", FacilityAssetKind.LandSite, 120_000_000m, 30);
        var expectedEvery = new List<(int Day, decimal Expected)>();
        while (engine.State.Day < 730)
        {
            engine.Advance(1);
            foreach (var site in engine.FjordSites().ToList())
                if (SiteMeanWeight(site) >= 4.5)
                    engine.HarvestSite(site.Id);
            if (engine.State.Day % 73 == 0)
                expectedEvery.Add((engine.State.Day, Expected(engine.State.FacilityAssets, engine.State.Day)));
            // Σ capex / life per day, to the 28th digit of decimal arithmetic.
            AssertNok(Expected(engine.State.FacilityAssets, engine.State.Day), engine.FinancialStatements.LifetimeDepreciation);
        }

        var fs = engine.FinancialStatements;
        Assert.True(fs.LifetimeDepreciation > 0m);
        Assert.Contains(expectedEvery, e => e.Expected > 0m);
        // Days 548..730 of the post-smolt site, 730 of the RAS, 30..730 of the bought land.
        var post = engine.State.FacilityAssets.Single(a => a.UnitId == engine.PostSmoltSites.Single().Id);
        AssertNok(post.CapexNok * 183 / (20 * 365), post.DepreciatedNok);
        // The reconciliation identity is the cash flows' alone: depreciation is not in it.
        Assert.Equal(0m, engine.UnbookedCashTotal);
        decimal gap = engine.FinanceManager.Balance - engine.State.OpeningBalanceNok - fs.LifetimeNetCashFlow;
        Assert.True(Math.Abs(gap) < 0.000001m, $"cash and booked flows differ by {gap} NOK");
        Assert.Equal(fs.LifetimeRevenue - fs.LifetimeCOGS - fs.LifetimeOperatingExpenses - fs.LifetimeInterest
                     - fs.LifetimeTaxes - fs.LifetimeCapex + fs.LifetimeLiabilitiesIncurred - fs.LifetimeLiabilitiesRepaid
                     + fs.LifetimeEquityRaised, fs.LifetimeNetCashFlow);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(13)]
    public void NonF2Capex_IsNeverDepreciated(int seed)
    {
        // A fjord career that builds a sea site and buys the AI feeding system:
        // capex on the balance sheet, no asset on the register, no depreciation.
        var engine = NewGame(seed, events: true);
        engine.ReceiveLoan(50_000_000m);
        Assert.True(engine.BuildSeaSite("po3_nord", "").Ok);
        decimal capexAfterBuild = engine.FinancialStatements.LifetimeCapex;
        Assert.True(capexAfterBuild > 0m);
        while (engine.State.Day < 730)
        {
            engine.Advance(1);
            foreach (var site in engine.FjordSites().ToList())
                if (SiteMeanWeight(site) >= 4.5)
                    engine.HarvestSite(site.Id);
        }
        var fs = engine.FinancialStatements;
        Assert.Empty(engine.State.FacilityAssets);
        Assert.Equal(0m, fs.LifetimeDepreciation);
        Assert.Equal(0m, fs.Depreciation);
        Assert.False(fs.CostBreakdown.ContainsKey(FinancialStatements.DepreciationCategory));
        Assert.True(fs.LifetimeCapex >= capexAfterBuild);
        Assert.Equal(0m, engine.UnbookedCashTotal);
    }

    // ── Save and load ────────────────────────────────────────────────────────

    [Fact]
    public void TheRegisterAndTheYearsDepreciation_SurviveASave_AndTheContinuationIsIdentical()
    {
        var engine = NewGame(11, scenario: null);
        engine.ReceiveLoan(500_000_000m);
        engine.RegisterFacilityAsset("ras_7", FacilityAssetKind.LandSite, 146_000_000m, 3);
        engine.RegisterFacilityAsset("offshore_7", FacilityAssetKind.OffshoreUnit, 91_250_000m, 40);
        engine.Advance(50);

        string json = SaveStateComparer.Serialize(engine);
        var loaded = SaveStateComparer.Resume(json, events: false);
        Assert.Equal(engine.FinancialStatements.Depreciation, loaded.FinancialStatements.Depreciation);
        Assert.Equal(engine.FinancialStatements.LifetimeDepreciation, loaded.FinancialStatements.LifetimeDepreciation);
        Assert.Equal(
            engine.State.FacilityAssets.Select(a => (a.UnitId, a.Kind, a.CapexNok, a.StartDay, a.LifeYears, a.DepreciatedNok, a.DepreciatedThroughDay)),
            loaded.State.FacilityAssets.Select(a => (a.UnitId, a.Kind, a.CapexNok, a.StartDay, a.LifeYears, a.DepreciatedNok, a.DepreciatedThroughDay)));

        engine.Advance(400);
        loaded.Advance(400);
        Assert.Equal(engine.FinancialStatements.LifetimeDepreciation, loaded.FinancialStatements.LifetimeDepreciation);
        Assert.Equal(SaveStateComparer.Normalize(SaveStateComparer.Serialize(engine)),
            SaveStateComparer.Normalize(SaveStateComparer.Serialize(loaded)));
    }
}
