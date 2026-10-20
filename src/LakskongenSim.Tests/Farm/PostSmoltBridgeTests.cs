using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Farm.Land;
using LakskongenSim.Finance;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Farm;

/// <summary>
/// F2-13, the post-smolt bridge on seawater (docs/FARM_TYPES_SPEC.md §7.1
/// F2-13, §2B-2C, §3.3, §3.4): the tier a land-grown fish carries to sea by
/// its weight and salinity record, its live mortality and time carryover,
/// desmoltification and the H2S sulphate factor, the wellboat transfer with
/// its MTB checks at booking and execution, the tier premium of bought smolts,
/// the post-smolt sale, and modules scaling the site. Acceptance 1 (no money
/// or fish in the bridge) is tests/test_land_bridge.py's.
/// </summary>
public class PostSmoltBridgeTests
{
    private readonly ITestOutputHelper _output;
    public PostSmoltBridgeTests(ITestOutputHelper output) => _output = output;

    private static SimulationEngine RichEngine(int seed)
    {
        var engine = new SimulationEngine { FinanceManager = new FinanceManager(5_000_000_000m) };
        engine.Initialize(enableEvents: false, seed: seed);
        return engine;
    }

    /// <summary>A post-smolt site of <paramref name="modules"/> modules on pad_delta (river intake), its biofilters matured.</summary>
    private static RASFacility Module(SimulationEngine engine, int modules = 1, string plot = "pad_delta")
    {
        var placed = engine.CommissionLandSite(plot, LandTemplate.PostSmoltModule, modules, "Elveøyra");
        Assert.True(placed.Ok, placed.Message);
        var site = engine.FindLandSite(placed.Id)!;
        Assert.Equal(LandSiteKind.PostSmolt, site.Kind);
        engine.Advance(engine.LandCalibration.PostSmolt.BiofilterMaturationDays + 1);
        return site;
    }

    private static SeaSite FjordSite(SimulationEngine engine, string id, double mtbTonnes = 3_000)
    {
        var site = new SeaSite(id, "zone-ps") { MABTonnes = mtbTonnes };
        engine.State.SeaSites.Add(site);
        return site;
    }

    private static FishPopulation Stock(SimulationEngine engine, RASFacility site, string tankId, int count, string strain = "standard")
    {
        var stocked = engine.StockLandTank(site.Id, tankId, count, strain);
        Assert.True(stocked.Ok, stocked.Message);
        return site.PopulationIn(site.FindTank(tankId)!)!;
    }

    /// <summary>Grow a tank's fish to <paramref name="weightKg"/> (a test shortcut past the module's weeks).</summary>
    private static void SetWeight(FishPopulation fish, double weightKg)
    {
        foreach (var cohort in fish.Cohorts) cohort.MeanWeightKg = weightKg;
    }

    private static double MeanWeight(IEnumerable<FishPopulation> pens)
    {
        var list = pens.Where(p => p.TotalCount > 0).ToList();
        int n = list.Sum(p => p.TotalCount);
        return n > 0 ? list.Sum(p => p.GetTotalBiomassKg()) / n : 0.0;
    }

    // ── Acceptance 2: the tier by weight and salinity ─────────────────────

    [Fact]
    public void Acceptance2_TheTier_IsSetByTheWeight_AndTheLast30DaysAtOrAbove25Ppt()
    {
        var cal = LakskongenSim.Calibration.CalibrationRegistry.Default.Facilities.Land.Smolt;
        Assert.Equal(SmoltQualityTier.SuperSmolt, PostSmoltBridge.TierOf(0.400, 30, cal));
        Assert.Equal(SmoltQualityTier.Large, PostSmoltBridge.TierOf(0.400, 29, cal));     // the record is short
        Assert.Equal(SmoltQualityTier.Large, PostSmoltBridge.TierOf(0.399, 90, cal));     // the fish is light
        Assert.Equal(SmoltQualityTier.Large, PostSmoltBridge.TierOf(0.200, 0, cal));
        Assert.Equal(SmoltQualityTier.Standard, PostSmoltBridge.TierOf(0.199, 90, cal));
        Assert.True(PostSmoltBridge.CountsAsSeawater(25.0, cal) && !PostSmoltBridge.CountsAsSeawater(24.9, cal));

        // In the engine: full seawater builds the record day by day; a blend under 25 ppt breaks it.
        var engine = RichEngine(301);
        var site = Module(engine);
        var fish = Stock(engine, site, site.Lots[0].Tanks[0].Id, 20_000);
        var tank = site.TankOf(fish)!;
        Assert.Equal(35.0, engine.LandSalinityOf(site));
        Assert.Equal(35.0, engine.HoldingWaterOf(fish).SalinityPsu);
        engine.Advance(30);
        Assert.InRange(tank.SeawaterDays, 30, 31);
        SetWeight(fish, 0.45);
        Assert.Equal(SmoltQualityTier.SuperSmolt, engine.PostSmoltTierOf(site, tank));
        Assert.True(engine.SetSalinity(site.Id, 20.0).Ok);
        engine.Advance(1);
        Assert.Equal(0, tank.SeawaterDays);
        Assert.Equal(20.0, engine.HoldingWaterOf(fish).SalinityPsu);
        Assert.Equal(SmoltQualityTier.Large, engine.PostSmoltTierOf(site, tank));      // 450 g, no record
        Assert.True(engine.SetSalinity(site.Id, 25.0).Ok);
        engine.Advance(29);
        Assert.Equal(29, tank.SeawaterDays);
        Assert.Equal(SmoltQualityTier.Large, engine.PostSmoltTierOf(site, tank));
        engine.Advance(1);
        Assert.Equal(SmoltQualityTier.SuperSmolt, engine.PostSmoltTierOf(site, tank));
        SetWeight(fish, 0.25);
        Assert.Equal(SmoltQualityTier.Large, engine.PostSmoltTierOf(site, tank));
        SetWeight(fish, 0.15);
        Assert.Equal(SmoltQualityTier.Standard, engine.PostSmoltTierOf(site, tank));

        // A pad without a river intake draws seawater only.
        var pad = Module(engine, 1, "pad_a");
        Assert.Equal(LandSiteReasons.NoRiverIntake, engine.SetSalinity(pad.Id, 20.0).ErrorKey);
        Assert.Equal(LandSiteReasons.OutOfRange, engine.SetSalinity(site.Id, 36.0).ErrorKey);
    }

    // ── Acceptance 2: the multipliers are live ─────────────────────────────

    /// <summary>
    /// The tier's sea-phase mortality carryover is live in the engine: the same
    /// smolts bought at each tier, stocked the same day in the same water, lose
    /// 0.82 (large) and 0.67 (SuperSmolt) of the standard smolts' anchor deaths,
    /// and grow faster by the tier's growth carryover (P5).
    /// </summary>
    [Fact]
    public void Acceptance2_TheTiersMortalityCarryover_IsLiveAtSea()
    {
        var engine = RichEngine(302);
        var sites = new Dictionary<SmoltQualityTier, SeaSite>();
        foreach (var tier in Enum.GetValues<SmoltQualityTier>())
        {
            sites[tier] = FjordSite(engine, $"sea_{tier}");
            Assert.True(engine.StockSmolts(sites[tier].Id, 150_000, "standard", tier).Ok);
        }
        engine.Advance(240);
        var lost = sites.ToDictionary(kv => kv.Key, kv => 150_000.0 - kv.Value.Pens.Sum(p => p.TotalCount));
        double large = lost[SmoltQualityTier.Large] / lost[SmoltQualityTier.Standard];
        double super = lost[SmoltQualityTier.SuperSmolt] / lost[SmoltQualityTier.Standard];
        _output.WriteLine($"240 days at sea: lost standard {lost[SmoltQualityTier.Standard]:N0}, large {large:F3}x, SuperSmolt {super:F3}x");
        Assert.True(lost[SmoltQualityTier.Standard] > 5_000, "the anchor must kill fish");
        Assert.InRange(large, 0.82 - 0.04, 0.82 + 0.04);
        Assert.InRange(super, 0.67 - 0.04, 0.67 + 0.04);
        Assert.True(MeanWeight(sites[SmoltQualityTier.SuperSmolt].Pens) > MeanWeight(sites[SmoltQualityTier.Large].Pens));
        Assert.True(MeanWeight(sites[SmoltQualityTier.Large].Pens) > MeanWeight(sites[SmoltQualityTier.Standard].Pens));
    }

    /// <summary>
    /// The time carryover is live: over 20 seeds, land-grown SuperSmolts (450 g
    /// after 30 days at 35 ppt) moved to a fjord site reach 4.5 kg at least 20 %
    /// sooner than standard 100 g smolts stocked the same week in the same
    /// water, every seed. A land-grown fish carries the tier's quality
    /// carryover (P5: the Chilean trial's 0.70 time, at equal stocking) and its
    /// own weight on top, so its sea phase is at most the tier's
    /// time-to-harvest multiplier of the standard's: SuperSmolt ≤ 0.70, large
    /// (250 g) ≤ 0.88, and SuperSmolt shorter than large on every seed
    /// (docs/BALANCE.md §1y; without the quality carryover the weight alone
    /// gives 0.76 and 0.90, RUN_LOG §7).
    /// </summary>
    [Fact]
    public void Acceptance2_SuperSmoltSeaPhases_AreAtLeast20PercentShorter_OverTwentySeeds()
    {
        var superRatios = new List<double>();
        var largeRatios = new List<double>();
        for (int seed = 1; seed <= 20; seed++)
        {
            var engine = RichEngine(1_000 + seed);
            var module = Module(engine);
            var standard = FjordSite(engine, "sea_standard");
            var super = FjordSite(engine, "sea_super");
            var large = FjordSite(engine, "sea_large");
            var tanks = module.Lots[0].Tanks;
            var superFish = Stock(engine, module, tanks[0].Id, 100_000);
            var largeFish = Stock(engine, module, tanks[1].Id, 100_000);
            engine.Advance(30);   // the seawater record
            SetWeight(superFish, 0.45);
            SetWeight(largeFish, 0.25);
            Assert.True(engine.StockSmolts(standard.Id, 100_000, "standard").Ok);
            var toSea = engine.TransferTankToSea(module.Id, tanks[0].Id, super.Id, 0);
            Assert.True(toSea.Ok, toSea.Message);
            Assert.True(engine.TransferTankToSea(module.Id, tanks[1].Id, large.Id, 0).Ok);
            int start = engine.State.Day;
            engine.Advance(2);
            Assert.Equal(SmoltQualityTier.SuperSmolt, super.Pens.Single(p => p.TotalCount > 0).Cohorts.Single().Quality);
            Assert.Equal(SmoltQualityTier.Large, large.Pens.Single(p => p.TotalCount > 0).Cohorts.Single().Quality);
            var reached = new Dictionary<string, int>();
            while (reached.Count < 3 && engine.State.Day < start + 900)
            {
                engine.Advance(1);
                foreach (var site in new[] { standard, super, large })
                    if (!reached.ContainsKey(site.Id) && MeanWeight(site.Pens) >= 4.5)
                        reached[site.Id] = engine.State.Day - start;
            }
            Assert.Equal(3, reached.Count);
            superRatios.Add((double)reached[super.Id] / reached[standard.Id]);
            largeRatios.Add((double)reached[large.Id] / reached[standard.Id]);
            _output.WriteLine($"seed {seed}: standard {reached[standard.Id]} d, large {reached[large.Id]} d, SuperSmolt {reached[super.Id]} d");
        }
        double median(List<double> v) => v.OrderBy(x => x).ElementAt(v.Count / 2);
        _output.WriteLine($"median SuperSmolt/standard {median(superRatios):F3} (max {superRatios.Max():F3}), large/standard {median(largeRatios):F3}");
        Assert.All(superRatios, r => Assert.True(r <= 0.80, $"a SuperSmolt sea phase only {1 - r:P0} shorter"));
        Assert.InRange(median(superRatios), 0.5, SmoltQualityRegistry.SuperSmolt.TimeToHarvestMultiplier);
        Assert.InRange(median(largeRatios), 0.75, SmoltQualityRegistry.Large.TimeToHarvestMultiplier);
        Assert.All(largeRatios, r => Assert.True(r < 1.0, "a large smolt's sea phase is shorter than a standard smolt's"));
        Assert.All(largeRatios.Zip(superRatios), p => Assert.True(p.Second < p.First));
    }

    // ── Acceptance 3: desmoltification and the sulphate factor ─────────────

    [Fact]
    public void Acceptance3_UnderTwelvePptForMoreThan60Days_DoublesTheTransferMortality_AndSulphateIsSalinityOver35()
    {
        var cal = LakskongenSim.Calibration.CalibrationRegistry.Default.Facilities.Land.Smolt;
        foreach (double ppt in new[] { 0.0, 6.0, 12.0, 17.5, 25.0, 35.0 })
            Assert.Equal(ppt / 35.0, PostSmoltBridge.SulphateFactor(ppt, cal), 12);
        double fresh = PostSmoltBridge.TransferMortalityShare(0.45, false, false, cal);
        Assert.Equal(2.0 * fresh, PostSmoltBridge.TransferMortalityShare(0.45, true, false, cal), 12);
        Assert.False(PostSmoltBridge.IsDesmoltified(60, cal));
        Assert.True(PostSmoltBridge.IsDesmoltified(61, cal));

        var engine = RichEngine(303);
        var site = Module(engine);
        var sea = FjordSite(engine, "sea_ps");
        var fish = Stock(engine, site, site.Lots[0].Tanks[0].Id, 50_000);
        var tank = site.TankOf(fish)!;
        Assert.True(engine.SetSalinity(site.Id, 10.0).Ok);
        Assert.Equal(10.0 / 35.0, engine.LandSulphateFactorOf(site), 12);
        engine.Advance(60);
        SetWeight(fish, 0.45);
        Assert.InRange(tank.LowSalinityDays, 60, 61);
        var before = engine.PreviewTransferTankToSea(site.Id, tank.Id, sea.Id, 0);
        engine.Advance(2);
        Assert.True(engine.IsTankDesmoltified(tank));
        var after = engine.PreviewTransferTankToSea(site.Id, tank.Id, sea.Id, 0);
        Assert.True(after.Desmoltified);
        Assert.Equal(2.0 * PostSmoltBridge.TransferMortalityShare(0.45, false, false, cal), after.MortalityShare, 6);
        if (!before.Desmoltified)
            Assert.Equal(2.0 * before.MortalityShare, after.MortalityShare, 6);

        // The handling kills the doubled share when the boat comes.
        long handlingBefore = engine.MortalityLedger.Cumulative().Deaths(MortalityCause.Handling).Fish;
        int count = fish.TotalCount;
        Assert.True(engine.TransferTankToSea(site.Id, tank.Id, sea.Id, 0).Ok);
        engine.Advance(2);
        long handling = engine.MortalityLedger.Cumulative().Deaths(MortalityCause.Handling).Fish - handlingBefore;
        Assert.InRange(handling, (long)(count * after.MortalityShare) - 2, (long)(count * after.MortalityShare) + 2);
        Assert.True(handling > 0);
        // Back to seawater, the run ends.
        Assert.True(engine.SetSalinity(site.Id, 35.0).Ok);
    }

    // ── Acceptance 4: the transfer ─────────────────────────────────────────

    [Fact]
    public void Acceptance4_StrainAndGenomeSurvive_TheBoatTakesOneToThreeDays_AndTheMtbIsCheckedAtBookingAndArrival()
    {
        var engine = RichEngine(304);
        var site = Module(engine, 2);
        var sea = FjordSite(engine, "sea_ps", mtbTonnes: 60);
        var tanks = site.Lots.SelectMany(l => l.Tanks).ToList();
        var fish = Stock(engine, site, tanks[0].Id, 60_000, "robust");
        engine.Advance(31);
        SetWeight(fish, 0.45);
        var genome = fish.Cohorts.Single().Genetics;
        int age = fish.Cohorts.Single().AgeDays;
        Assert.True(engine.VaccinateSmoltBatch(fish.Id).Ok);
        int vaccinated = fish.VaccinatedDay;

        // MTB at booking: 60,000 × 0.45 kg = 27 t fits 60 t; with 40 t standing it does not.
        var heavy = new FishPopulation { ZoneId = sea.ZoneId, SiteId = sea.Id };
        heavy.InjectDependencies(engine.Growth, engine.Mortality);
        heavy.Cohorts.Add(new FishPopulation.Cohort(500, 10_000, 4.0, LifeStage.Seawater));
        sea.Stock(heavy);
        engine.State.Populations.Add(heavy);
        var refused = engine.PreviewTransferTankToSea(site.Id, tanks[0].Id, sea.Id, 0);
        Assert.Equal(LandSiteReasons.MabExceeded, refused.Reason);
        Assert.Equal(LandSiteReasons.MabExceeded, engine.TransferTankToSea(site.Id, tanks[0].Id, sea.Id, 0).ErrorKey);
        Assert.Empty(site.Transfers);
        heavy.RemoveAlive(heavy.Cohorts[0], 10_000, FishRemoval.Harvest);

        // Booked: the hire paid now, the boat comes in 1-3 days.
        decimal cash = engine.FinanceManager.Balance;
        var preview = engine.PreviewTransferTankToSea(site.Id, tanks[0].Id, sea.Id, 0);
        Assert.True(preview.Allowed, preview.Message);
        Assert.InRange(preview.Days, 1, 3);
        var booked = engine.TransferTankToSea(site.Id, tanks[0].Id, sea.Id, 0);
        Assert.True(booked.Ok, booked.Message);
        Assert.Equal(cash - preview.HireNok, engine.FinanceManager.Balance);
        var job = Assert.Single(site.Transfers);
        Assert.Equal(LandTransferJob.Kind, job.Boat.Kind);
        Assert.Equal(engine.State.Day + preview.Days, job.Boat.DueDay);
        Assert.Equal(LandSiteReasons.NoWellboatSlot, engine.TransferTankToSea(site.Id, tanks[0].Id, sea.Id, 0).ErrorKey);
        Assert.True(PostSmoltBridge.TransferDays(4_000.0, engine.LandCalibration.Smolt) > engine.LandCalibration.Smolt.MaxTransferDays,
            "a 4,000 t transfer has no wellboat slot");

        int count = fish.TotalCount;
        while (site.Transfers.Count > 0) engine.Advance(1);
        Assert.InRange(engine.State.Day - job.Boat.BookedDay, 1, 3);
        var arrived = sea.Pens.Where(p => p.TotalCount > 0 && !ReferenceEquals(p, heavy)).ToList();
        var cohort = Assert.Single(Assert.Single(arrived).Cohorts);
        Assert.Same(genome, cohort.Genetics);
        Assert.InRange(cohort.AgeDays, age, age + (engine.State.Day - job.Boat.BookedDay) + 1);
        Assert.Equal(SmoltQualityTier.SuperSmolt, cohort.Quality);
        Assert.Equal(vaccinated, arrived[0].VaccinatedDay);
        Assert.Equal(0.0, arrived[0].SeaLiceLoad);
        Assert.Null(site.PopulationIn(tanks[0]));
        Assert.Equal(-1, tanks[0].StockedDay);
        // The handling's share, and a day or two of land and sea baseline deaths around the boat.
        Assert.InRange(cohort.Count, (int)(count * (1.0 - preview.MortalityShare) * 0.995), (int)(count * (1.0 - preview.MortalityShare)));

        // MTB at arrival: booked within the MTB, then the site filled before the boat came.
        var second = Stock(engine, site, tanks[1].Id, 60_000, "robust");
        engine.Advance(1);
        SetWeight(second, 0.45);
        Assert.True(engine.TransferTankToSea(site.Id, tanks[1].Id, sea.Id, 0).Ok);
        var filler = new FishPopulation { ZoneId = sea.ZoneId, SiteId = sea.Id };
        filler.InjectDependencies(engine.Growth, engine.Mortality);
        filler.Cohorts.Add(new FishPopulation.Cohort(500, 7_000, 4.0, LifeStage.Seawater));
        sea.Stock(filler);
        engine.State.Populations.Add(filler);
        int secondCount = second.TotalCount;
        while (site.Transfers.Count > 0) engine.Advance(1);
        Assert.Same(second, site.PopulationIn(tanks[1]));           // the fish stayed on land
        Assert.True(second.TotalCount >= secondCount - 100);
        Assert.Contains(engine.State.EventLog, e => e.Name == "Brønnbåt returnerte");
        Assert.Empty(UnitResolver.Violations(engine.State));
    }

    // ── Acceptance 5: the tier premium only on bought smolts ───────────────

    [Fact]
    public void Acceptance5_BoughtTieredSmoltsPayThePremium_TheOwnPostSmoltPayNoSmoltPrice()
    {
        var engine = RichEngine(305);
        decimal price = LakskongenSim.Calibration.CalibrationRegistry.Default.Economy.SmoltCostPerFishNOK;
        var sea = FjordSite(engine, "sea_ps");
        foreach (var (tier, factor) in new[] { (SmoltQualityTier.Standard, 1.0m), (SmoltQualityTier.Large, 1.6m), (SmoltQualityTier.SuperSmolt, 2.5m) })
        {
            decimal before = engine.FinanceManager.Balance;
            Assert.True(engine.StockSmolts(sea.Id, 10_000, "standard", tier).Ok);
            Assert.Equal(10_000m * price * factor, before - engine.FinanceManager.Balance);
        }

        // The own module's SuperSmolts go to sea for the wellboat's hire alone: no smolt bought.
        var site = Module(engine);
        var fish = Stock(engine, site, site.Lots[0].Tanks[0].Id, 30_000);
        engine.Advance(31);
        SetWeight(fish, 0.45);
        var smolts = new List<decimal>();
        engine.PnlBooked += (_, category, amount, income) => { if (!income && category == "smolts") smolts.Add(amount); };
        var preview = engine.PreviewTransferTankToSea(site.Id, site.Lots[0].Tanks[0].Id, sea.Id, 0);
        Assert.Equal(SmoltQualityTier.SuperSmolt, preview.Tier);
        Assert.Equal(preview.Fish * price * 2.5m, preview.BoughtValueNok); // what the same fish would cost bought
        Assert.True(engine.TransferTankToSea(site.Id, site.Lots[0].Tanks[0].Id, sea.Id, 0).Ok);
        engine.Advance(3);
        Assert.Empty(smolts);
        Assert.Equal(price, engine.SmoltTierCostPerFish(SmoltQualityTier.SuperSmolt, SmoltStrainRegistry.Get("standard"), SmoltSource.OwnFacility));
    }

    // ── Acceptance 6: the sale ─────────────────────────────────────────────

    [Fact]
    public void Acceptance6_SellPostSmolt_BooksTheRevenue_AndTheFishLeaveThroughTheLedger()
    {
        var engine = RichEngine(306);
        var site = Module(engine);
        var tank = site.Lots[0].Tanks[0];
        var fish = Stock(engine, site, tank.Id, 40_000);
        Assert.Equal(LandSiteReasons.BelowMinWeight, engine.SellPostSmolt(site.Id, tank.Id, 0).ErrorKey); // 80 g
        SetWeight(fish, 0.45);
        var income = new List<(string Category, decimal Amount)>();
        engine.PnlBooked += (_, category, amount, isIncome) => { if (isIncome) income.Add((category, amount)); };
        decimal cash = engine.FinanceManager.Balance;
        long movedBefore = engine.MortalityLedger.Cumulative().Removed(FishRemoval.TransferOut).Fish;
        double kg = 15_000 * 0.45;
        var half = engine.SellPostSmolt(site.Id, tank.Id, 15_000);
        Assert.True(half.Ok, half.Message);
        decimal revenue = (decimal)kg * engine.LandCalibration.Smolt.SalePriceNokPerKgLive;
        Assert.Equal(revenue, engine.FinanceManager.Balance - cash);
        Assert.Equal(("post_smolt_sales", revenue), Assert.Single(income));
        Assert.Equal(25_000, fish.TotalCount);
        Assert.Equal(15_000, engine.MortalityLedger.Cumulative().Removed(FishRemoval.TransferOut).Fish - movedBefore);
        Assert.Equal(revenue, site.Account.PostSmoltRevenueNok);
        Assert.Equal(LandSiteReasons.OutOfRange, engine.SellPostSmolt(site.Id, tank.Id, -1).ErrorKey);
        Assert.Equal(LandSiteReasons.OutOfRange, engine.SellPostSmolt(site.Id, tank.Id, 25_001).ErrorKey);
        Assert.True(engine.SellPostSmolt(site.Id, tank.Id, 0).Ok);   // the rest
        Assert.Null(site.PopulationIn(tank));
        Assert.DoesNotContain(fish, engine.State.Populations);
        Assert.Equal("holding_empty", engine.SellPostSmolt(site.Id, tank.Id, 0).ErrorKey);

        // A grow-out site harvests; it sells no post-smolt.
        var growOut = engine.CommissionLandSite("pad_a", LandTemplate.HfsPilotLot, 1, "Storholmen");
        Assert.Equal(LandSiteReasons.WrongSiteKind, engine.SellPostSmolt(growOut.Id, "t01", 0).ErrorKey);
    }

    // ── Acceptance 7: modules scale linearly ───────────────────────────────

    [Fact]
    public void Acceptance7_OneToFourModules_ScaleTanksCapacityEnergyAndStaffLinearly()
    {
        var one = default((int Tanks, double Capacity, int Staff, double Energy));
        for (int modules = 1; modules <= 4; modules++)
        {
            var engine = RichEngine(307);
            var site = Module(engine, modules);
            foreach (var lot in site.Lots)
                Stock(engine, site, lot.Tanks[0].Id, 100_000);
            engine.Advance(20);
            var measured = (site.TankCount, site.CapacityTonnesPerYear, site.EmployeeCount, site.DailyEnergyKWh);
            _output.WriteLine($"{modules} modules: {measured.TankCount} tanks, {measured.CapacityTonnesPerYear:N0} t/yr, {measured.EmployeeCount} staff, {measured.DailyEnergyKWh:N0} kWh/day");
            if (modules == 1) { one = measured; continue; }
            Assert.Equal(modules * one.Tanks, measured.TankCount);
            Assert.Equal(modules * one.Capacity, measured.CapacityTonnesPerYear, 9);
            Assert.Equal(modules * one.Staff, measured.EmployeeCount);
            Assert.InRange(measured.DailyEnergyKWh / one.Energy, modules * 0.97, modules * 1.03);
        }
        Assert.Equal((2, 400.0, 6), (one.Tanks, one.Capacity, one.Staff));
        var plot = new SimulationEngine();
        plot.Initialize(enableEvents: false, seed: 1);
        Assert.Equal(LandSiteReasons.LotsExhausted, plot.CommissionLandSite("pad_delta", LandTemplate.PostSmoltModule, 5, "").ErrorKey);
    }

    // ── The panel's batch verbs reach an F2 site through its tanks ─────────

    [Fact]
    public void TheBatchVerbs_StockATank_Vaccinate_AndBookTheWellboat_ToTheFjordSiteWithTheMostRoom()
    {
        var engine = RichEngine(308);
        var site = Module(engine);
        var small = FjordSite(engine, "sea_small", 200);
        var roomy = FjordSite(engine, "sea_roomy", 3_000);
        var started = engine.StartSmoltBatch(site.Id, 40_000);
        Assert.True(started.Ok, started.Message);
        var batch = site.Tanks.Single(p => p.Id == started.Id);
        Assert.Same(site.Lots[0].Tanks[0], site.TankOf(batch));
        Assert.Equal(0.08, batch.Cohorts.Single().MeanWeightKg, 9);
        Assert.Equal(40_000m * LakskongenSim.Calibration.CalibrationRegistry.Default.Economy.SmoltCostPerFishNOK, (decimal)started.Value2);
        Assert.True(engine.VaccinateSmoltBatch(batch.Id).Ok);
        Assert.Equal("already_vaccinated", engine.VaccinateSmoltBatch(batch.Id).ErrorKey);
        Assert.Equal("batch_not_found", engine.VaccinateSmoltBatch("nope").ErrorKey);
        engine.Advance(31);
        SetWeight(batch, 0.45);
        var moved = engine.TransferSmoltBatchToSea(batch.Id, "");
        Assert.True(moved.Ok, moved.Message);
        Assert.Equal(roomy.Id, Assert.Single(site.Transfers).DestinationSiteId);
        engine.Advance(3);
        Assert.Contains(roomy.Pens, p => p.TotalCount > 0);
        Assert.DoesNotContain(small.Pens, p => p.TotalCount > 0);
    }
}
