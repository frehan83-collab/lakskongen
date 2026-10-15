using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Calibration;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Tests.Integration;
using Xunit;

namespace LakskongenSim.Tests.Core;

/// <summary>
/// F2-02 acceptance 4 (docs/FARM_TYPES_SPEC.md §7.1): each farm-type hook of
/// §3.3 has a test proving its default equals the pre-move expression on a
/// fixture state. The fixture is the pre-F2 facilities save
/// (save_v8_facilities.json: po3_red_sone's two sea sites, a RAS facility with
/// stocked tanks, an offshore farm with stocked pens, a post-smolt facility
/// and a hatchery cohort), resumed and run 40 days so every holding has
/// today's water. The pre-move expressions are written out here as they stood
/// on the wave-2 base (8c81754) at each call site; doubles and decimals are
/// compared bit for bit.
/// </summary>
public class FarmTypeHooksTests
{
    private static readonly Lazy<SimulationEngine> Fixture = new(() =>
    {
        var engine = FarmTypeMoveTests.Resume("as-saved/events");
        engine.Advance(40);
        // Irregular lice loads, so a sum in another order would round differently.
        for (int i = 0; i < engine.State.Populations.Count; i++)
            engine.State.Populations[i].SeaLiceLoad = 0.1 + 0.0371 * i + 1e-7 * i * i;
        return engine;
    });

    private static SimulationEngine Engine => Fixture.Value;

    private static IEnumerable<string> JobKinds => new[] { "delouse", "harvest", "freshwater", "rov", "" };

    private static void SameBits(double expected, double actual, string what)
        => Assert.True(BitConverter.DoubleToInt64Bits(expected) == BitConverter.DoubleToInt64Bits(actual),
            $"{what}: expected {expected:R}, actual {actual:R}");

    private static void SameBits(decimal expected, decimal actual, string what)
        => Assert.True(decimal.GetBits(expected).SequenceEqual(decimal.GetBits(actual)),
            $"{what}: expected {expected} ({string.Join(",", decimal.GetBits(expected))}), actual {actual} ({string.Join(",", decimal.GetBits(actual))})");

    [Fact]
    public void TheFixture_HoldsEveryHoldingKind_AndAPopulationInNoSite()
    {
        var kinds = Engine.State.Populations.Select(p => Engine.HoldingOf(p).Kind).Distinct().ToList();
        Assert.Contains(HoldingKind.SeaPen, kinds);
        Assert.Contains(HoldingKind.LandTank, kinds);
        Assert.Contains(HoldingKind.Offshore, kinds);
        Assert.Contains(Engine.State.Populations, p => Engine.HoldingOf(p).Site == null && Engine.HoldingOf(p).Kind == HoldingKind.SeaPen);
        Assert.Equal(2, Engine.State.SeaSites.Count);
    }

    /// <summary>Pre-move: <c>site.Lice.SetExternalPressure(SeasonalLicePressure(State.Day))</c>.</summary>
    [Fact]
    public void ExternalLicePressureFor_IsOne_SoEverySiteTakesTheSeasonalPressure()
    {
        foreach (var site in Engine.State.SeaSites)
        {
            double hook = Engine.ExternalLicePressureFor(site);
            Assert.Equal(1.0, hook);
            for (int day = 0; day < 2 * SimCalendar.DaysPerYear; day++)
                SameBits(SimulationEngine.SeasonalLicePressure(day), SimulationEngine.SeasonalLicePressure(day) * hook,
                    $"{site.Id} day {day}");
        }
    }

    /// <summary>
    /// Pre-move (UpdateTrafficLightZones): <c>pops = State.Populations.Where(p =&gt; p.ZoneId == zone.Id)</c>,
    /// <c>fish = pops.SelectMany(p =&gt; p.Cohorts).Sum(c =&gt; c.Count)</c>,
    /// <c>lice = fish &gt; 0 ? pops.Sum(p =&gt; p.Cohorts.Sum(c =&gt; c.Count) * p.SeaLiceLoad) / fish : 0</c>.
    /// </summary>
    [Fact]
    public void ZoneLiceContribution_IsEachPopulationsFishTimesItsLice_AndTheZoneMeanIsThePreMoveMean()
    {
        foreach (var population in Engine.State.Populations)
        {
            var share = Engine.ZoneLiceContribution(Engine.HoldingOf(population).Site, population);
            int fish = population.Cohorts.Sum(c => c.Count);
            Assert.Equal(fish, share.Fish);
            SameBits(population.Cohorts.Sum(c => c.Count) * population.SeaLiceLoad, share.LiceWeightedFish, population.ZoneId);
        }

        var zoneIds = Engine.State.Zones.Select(z => z.Id).Concat(new[] { "", "no_such_zone" }).ToList();
        Assert.Contains(zoneIds, id => Engine.State.Populations.Count(p => p.ZoneId == id) >= 2);
        foreach (var zoneId in zoneIds)
        {
            var pops = Engine.State.Populations.Where(p => p.ZoneId == zoneId).ToList();
            int fish = pops.SelectMany(p => p.Cohorts).Sum(c => c.Count);
            double lice = fish > 0
                ? pops.Sum(p => p.Cohorts.Sum(c => c.Count) * p.SeaLiceLoad) / fish
                : 0.0;
            var (hookFish, hookLice) = Engine.ZoneLiceForTrafficLight(zoneId);
            Assert.Equal(fish, hookFish);
            SameBits(lice, hookLice, $"zone '{zoneId}'");
        }
    }

    [Fact]
    public void SkipsFjordStormWear_IsFalse_OnEverySite()
    {
        Assert.All(Engine.State.SeaSites, site => Assert.False(Engine.SkipsFjordStormWear(site)));
    }

    /// <summary>Pre-move (ProcessDueWellboatJobs): every due job ran on its day.</summary>
    [Fact]
    public void OperationPossible_IsTrue_ForEveryJob_AtEverySite_OnEveryDay()
    {
        var sites = Engine.State.SeaSites.Cast<SeaSite?>().Append(null).ToList();
        foreach (var site in sites)
            foreach (var kind in JobKinds)
                for (int day = 0; day < 2 * SimCalendar.DaysPerYear; day += 7)
                    Assert.True(Engine.OperationPossible(site, kind, day), $"{site?.Id ?? "(none)"} {kind} day {day}");
    }

    [Fact]
    public void CountsTowardSiteLimits_IsTrue_OnEverySite()
    {
        Assert.All(Engine.State.SeaSites, site => Assert.True(Engine.CountsTowardSiteLimits(site)));
    }

    /// <summary>Pre-move (Biology feeding): every holding fed its ration (fasting aside).</summary>
    [Fact]
    public void FeedAvailableFor_IsTrue_ForEveryHolding()
    {
        Assert.All(Engine.State.Populations, p => Assert.True(Engine.FeedAvailableFor(p), Engine.HoldingOf(p).Kind.ToString()));
        // The husbandry record's default is the same: a holding with feed.
        Assert.True(new SimulationEngine.HoldingHusbandry(default, 1.0, 1.0, 1.0, 20.0, 0.9).FeedAvailable);
    }

    /// <summary>Pre-move (Biology feeding): the appetite was the oxygen × gill factor alone.</summary>
    [Fact]
    public void AppetiteFactorFor_IsOne_ForEveryHolding_SoTheAppetiteIsTheOxygenAndGillFactor()
    {
        foreach (var population in Engine.State.Populations)
        {
            double hook = Engine.AppetiteFactorFor(population);
            Assert.Equal(1.0, hook);
            foreach (double appetite in new[] { 0.0, 0.37, 0.6 + 0.4 * (73.3 / 100.0), 1.0 })
                SameBits(appetite, appetite * hook, Engine.HoldingOf(population).Kind.ToString());
        }
    }

    /// <summary>
    /// Pre-move (SimulationEngine.WaterOf, the growth and disease temperature):
    /// a land tank's water is its facility's CurrentWaterTemp and OxygenLevel at
    /// 35 PSU in the home area; an offshore pen's is its area's sea with the
    /// farm's OxygenBoost added; a sea pen's is its zone's sea water.
    /// </summary>
    [Fact]
    public void UnitWaterTemperatureC_IsThePreMoveWaterOfEveryHolding()
    {
        foreach (var population in Engine.State.Populations)
        {
            var holding = Engine.HoldingOf(population);
            var water = Engine.WaterOf(holding, population);
            double hook = Engine.UnitWaterTemperatureC(population);
            SameBits(water.TemperatureC, hook, "hook vs the one water");
            switch (holding.Kind)
            {
                case HoldingKind.LandTank:
                    var facility = holding.LandFacility!;
                    SameBits(facility.CurrentWaterTemp, hook, "tank temperature");
                    Assert.Equal(new HoldingWater(facility.CurrentWaterTemp, facility.OxygenLevel,
                        HoldingWater.SeawaterSalinityPsu, Engine.HomeProductionArea, HoldingKind.LandTank), water);
                    break;
                case HoldingKind.Offshore:
                    var farm = holding.OffshoreFarm!;
                    SameBits(Engine.SeaTemperatureOfZone(farm.ZoneId), hook, "offshore temperature");
                    double seaOxygen = holding.Zone?.WaterChemistry?.OxygenO2
                        ?? WaterQuality.CalculateOxygenSaturation(hook, HoldingWater.SeawaterSalinityPsu);
                    SameBits(seaOxygen + farm.OxygenBoost, water.OxygenMgPerL, "offshore oxygen");
                    Assert.Equal(HoldingWater.SeawaterSalinityPsu, water.SalinityPsu);
                    Assert.Equal(Engine.ProductionAreaOfZone(farm.ZoneId), water.ProductionArea);
                    Assert.Equal(HoldingKind.Offshore, water.Kind);
                    break;
                default:
                    SameBits(Engine.SeaTemperatureOfZone(population.ZoneId), hook, "sea temperature");
                    break;
            }
        }
    }

    /// <summary>Pre-move (Biology mortality): the calibrated anchor (× 1) and no other cause.</summary>
    [Fact]
    public void UnitMortalityTerms_AreTheAnchorTimesOne_WithNoExtraCause_ForEveryHolding()
    {
        foreach (var population in Engine.State.Populations)
        {
            var terms = Engine.UnitMortalityTerms(population);
            Assert.Equal(1.0, terms.AnchorMultiplier);
            Assert.Empty(terms.ExtraHazards);
            Assert.Equal(MortalityTerms.None, terms);
        }
    }

    /// <summary>
    /// Pre-move (Biology mortality): the anchor hazard was BaselineDailyRate ×
    /// StageMultiplier, unscaled. With the hooks' multipliers (terms × quality)
    /// it is that hazard × 1.0, the same bits: a population's deaths over a
    /// year match a population run without the multiplier, cohort for cohort.
    /// </summary>
    [Fact]
    public void QualityMortalityMultiplier_IsOne_WhateverTheSmoltTier_AndLeavesTheAnchorDeathsUnchanged()
    {
        foreach (var tier in Enum.GetValues<SmoltQualityTier>())
        {
            var population = new FishPopulation { ZoneId = "z", SiteId = "s" };
            population.Cohorts.Add(new FishPopulation.Cohort(400, 10_000, 1.0, LifeStage.Seawater) { Quality = tier });
            population.InjectDependencies(Engine.Growth, Engine.Mortality);
            Assert.Equal(1.0, Engine.QualityMortalityMultiplier(population));
        }
        foreach (var population in Engine.State.Populations)
            Assert.Equal(1.0, Engine.UnitMortalityTerms(population).AnchorMultiplier * Engine.QualityMortalityMultiplier(population));

        // The call site's multiplier of 1.0 kills exactly the fish the pre-move call did.
        var plain = AnchorOnlyEngine();
        var hooked = AnchorOnlyEngine();
        for (int day = 0; day < SimCalendar.DaysPerYear; day++)
        {
            plain.Pop.AdvanceDays(1, skipGrowth: true, 0.05, 0.0);
            hooked.Pop.AdvanceDays(1, skipGrowth: true, 0.05, 0.0, anchorHazardMultiplier: 1.0);
        }
        Assert.True(plain.Pop.TotalCount < 50_000, "the anchor must kill fish over a year");
        Assert.Equal(plain.Pop.Cohorts.Select(c => (c.Count, c.AgeDays)), hooked.Pop.Cohorts.Select(c => (c.Count, c.AgeDays)));
        Assert.Equal(plain.Engine.MortalityLedger.Cumulative().Total.Fish, hooked.Engine.MortalityLedger.Cumulative().Total.Fish);
        Assert.Equal(50_000 - plain.Pop.TotalCount, plain.Engine.MortalityLedger.Cumulative().Deaths(MortalityCause.BaselineAnchor).Fish);
    }

    private static (SimulationEngine Engine, FishPopulation Pop) AnchorOnlyEngine()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 9);
        var pop = new FishPopulation { ZoneId = "z", SiteId = "s" };
        pop.InjectDependencies(engine.Growth, engine.Mortality);
        pop.Cohorts.Add(new FishPopulation.Cohort(400, 25_000, 1.0, LifeStage.Seawater));
        pop.Cohorts.Add(new FishPopulation.Cohort(200, 25_000, 0.2, LifeStage.Parr));
        engine.State.Populations.Add(pop);
        engine.BeginMortalityDay();
        return (engine, pop);
    }

    /// <summary>
    /// Pre-move (StockSmolts → PlaceSmolts): <c>count * _balancing.SmoltCostPerFish * (decimal)strain.CostMultiplier</c>,
    /// whatever the quality tier (no premium). The engine's smolt price is the calibrated one.
    /// </summary>
    [Fact]
    public void SmoltTierCostPerFish_TimesCount_IsThePreMoveStockingCost_ForEveryStrainTierAndSource()
    {
        decimal price = CalibrationRegistry.Default.Economy.SmoltCostPerFishNOK;
        int[] counts = { 1, 7, 999, 10_000, 36_000, 200_000, 561_600, 1_000_003 };
        foreach (var strain in SmoltStrainRegistry.All)
            foreach (var tier in Enum.GetValues<SmoltQualityTier>())
                foreach (var source in Enum.GetValues<SmoltSource>())
                {
                    decimal perFish = Engine.SmoltTierCostPerFish(tier, strain, source);
                    foreach (int count in counts)
                        SameBits(count * price * (decimal)strain.CostMultiplier, count * perFish,
                            $"{strain.Id} {tier} {source} x{count}");
                }
    }

    /// <summary>
    /// Pre-move (FindHarvestHolding): <c>new HarvestHolding(site, ..., true, ...)</c>,
    /// <c>(ras, ..., false, ...)</c>, <c>(offshore, ..., true, ...)</c>.
    /// </summary>
    [Fact]
    public void IsResourceRentLiable_SeaAndOffshoreYes_LandTanksNever()
    {
        foreach (var site in Engine.State.SeaSites)
            Assert.True(Engine.IsResourceRentLiable(site.Id, HoldingKind.SeaPen));
        foreach (var facility in Engine.RASFacilities)
            Assert.False(Engine.IsResourceRentLiable(facility.Id, HoldingKind.LandTank));
        foreach (var farm in Engine.OffshoreFarms)
            Assert.True(Engine.IsResourceRentLiable(farm.Id, HoldingKind.Offshore));
    }

    // ── The moved containment code, against its pre-move expressions ──────

    /// <summary>Pre-move (Biology, storm hazards): <c>ContainmentTech.ModifiersFor(site?.Containment ?? OpenNet)</c>.</summary>
    [Fact]
    public void ContainmentModifiersOf_IsTheRungsRow_AndOpenNetForNoSite()
    {
        Assert.Equal(ContainmentTech.ModifiersFor(ContainmentLevel.OpenNet), SimulationEngine.ContainmentModifiersOf(null));
        foreach (var level in Enum.GetValues<ContainmentLevel>())
            Assert.Equal(ContainmentTech.ModifiersFor(level),
                SimulationEngine.ContainmentModifiersOf(new SeaSite("s", "z") { Containment = level }));
    }

    /// <summary>Pre-move (ProcessDailyFinance 3B): 2,000 NOK a skirt and 1,000 a seal deterrent, per site and day.</summary>
    [Fact]
    public void PreventiveMitigationDailyOpex_Is2000PerSkirt_And1000PerDeterrent()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 3);
        Assert.Equal(0m, engine.PreventiveMitigationDailyOpexNok());
        engine.State.SeaSites.Add(new SeaSite("a", "z") { HasLiceSkirt = true });
        engine.State.SeaSites.Add(new SeaSite("b", "z") { HasSealDeterrent = true });
        engine.State.SeaSites.Add(new SeaSite("c", "z") { HasLiceSkirt = true, HasSealDeterrent = true });
        engine.State.SeaSites.Add(new SeaSite("d", "z"));
        SameBits(0m + 2_000m + 1_000m + 2_000m + 1_000m, engine.PreventiveMitigationDailyOpexNok(), "mitigation opex");
    }

    /// <summary>
    /// Pre-move (RollBloomWeek): a jellyfish bloom at a site with a barrier had its
    /// counter-measure multiplier × (1 − clamp(EffectiveJellyfishBlocking)); any
    /// other bloom, or no barrier, left it as it was.
    /// </summary>
    [Fact]
    public void BarrierBloomPassFraction_IsThePreMoveJellyfishBlock()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 3);
        engine.State.Zones.Add(new Zone("z") { CurrentSpeed = 0.23 });
        var barrier = new SeaSite("b", "z") { Barrier = BarrierKind.Smoltscreen, BarrierDaysSinceCleaning = 41 };
        var none = new SeaSite("n", "z");
        engine.State.SeaSites.Add(barrier);
        engine.State.SeaSites.Add(none);

        double block = BarrierTech.EffectiveJellyfishBlocking(BarrierKind.Smoltscreen, 0.23, 41);
        Assert.InRange(block, 1e-6, 1.0);
        SameBits(1.0 - Math.Clamp(block, 0.0, 1.0), engine.BarrierBloomPassFraction(barrier, "jellyfish"), "jellyfish behind a barrier");
        foreach (var kind in new[] { "algae", "hypoxia" })
            Assert.Equal(1.0, engine.BarrierBloomPassFraction(barrier, kind));
        foreach (var kind in new[] { "jellyfish", "algae", "hypoxia" })
            Assert.Equal(1.0, engine.BarrierBloomPassFraction(none, kind));
    }

    /// <summary>Pre-move (RestoreFromLoad, Phase 4.1): an open-net save site that is closed → ClosedFloat, a skirt owner → Skirt.</summary>
    [Fact]
    public void MigrateLegacyContainmentFlags_MapsTheLegacyFlagsOntoRungs()
    {
        var closed = new SeaSite("c", "z") { IsClosedContainment = true, HasLiceSkirt = true };
        var skirted = new SeaSite("s", "z") { HasLiceSkirt = true };
        var open = new SeaSite("o", "z");
        var already = new SeaSite("a", "z") { Containment = ContainmentLevel.SemiClosed, IsClosedContainment = true };
        foreach (var site in new[] { closed, skirted, open, already })
            SimulationEngine.MigrateLegacyContainmentFlags(site);
        Assert.Equal(ContainmentLevel.ClosedFloat, closed.Containment);
        Assert.Equal(ContainmentLevel.Skirt, skirted.Containment);
        Assert.Equal(ContainmentLevel.OpenNet, open.Containment);
        Assert.Equal(ContainmentLevel.SemiClosed, already.Containment);
    }
}
