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

    /// <summary>
    /// The fixture after F2-03's load migration: po3_red_sone's two fjord sites,
    /// the offshore farm as an offshore unit (an offshore sea site), the RAS and
    /// post-smolt facilities as land sites, and the hatchery cohort in no site.
    /// (Replaces F2-02's "two sea sites": the fjord sites are still exactly two,
    /// and the offshore unit is the farm, by id, with its kind.)
    /// </summary>
    [Fact]
    public void TheFixture_HoldsEveryHoldingKind_AndAPopulationInNoSite()
    {
        var kinds = Engine.State.Populations.Select(p => Engine.HoldingOf(p).Kind).Distinct().ToList();
        Assert.Contains(HoldingKind.SeaPen, kinds);
        Assert.Contains(HoldingKind.LandTank, kinds);
        Assert.Contains(HoldingKind.Offshore, kinds);
        Assert.Contains(Engine.State.Populations, p => Engine.HoldingOf(p).Site == null && Engine.HoldingOf(p).Kind == HoldingKind.SeaPen);
        Assert.Equal(new[] { "po3_nord", "po3_sor" },
            Engine.State.SeaSites.Where(s => s.Kind == SiteKind.Fjord).Select(s => s.Id));
        var unit = Assert.Single(Engine.OffshoreUnits);
        Assert.Equal("offshore_1", unit.Id);
        Assert.NotNull(unit.Offshore);
        Assert.Equal(3, Engine.State.SeaSites.Count);
        Assert.Empty(Engine.OffshoreFarms);
        Assert.Empty(Engine.PostSmoltFacilities);
        Assert.All(Engine.State.Populations.Where(p => Engine.HoldingOf(p).Kind == HoldingKind.Offshore),
            p => Assert.Same(unit, Engine.HoldingOf(p).Site));
    }

    /// <summary>
    /// Pre-move: <c>site.Lice.SetExternalPressure(SeasonalLicePressure(State.Day))</c>
    /// at every site, which every fjord site keeps bit for bit; F2-03 gives an
    /// offshore unit its plot's lower external pressure (off_1: 0.12, spec §4).
    /// </summary>
    [Fact]
    public void ExternalLicePressureFor_IsOne_SoEverySiteTakesTheSeasonalPressure()
    {
        foreach (var site in Engine.State.SeaSites)
        {
            double hook = Engine.ExternalLicePressureFor(site);
            if (site.Kind == SiteKind.Offshore)
            {
                Assert.Equal("off_1", site.PlotId);
                Assert.Equal(0.12, hook);
                Assert.Equal(Engine.FindPlot(site.PlotId)!.LiceExternalMultiplier, hook);
                continue;
            }
            Assert.Equal(1.0, hook);
            for (int day = 0; day < 2 * SimCalendar.DaysPerYear; day++)
                SameBits(SimulationEngine.SeasonalLicePressure(day), SimulationEngine.SeasonalLicePressure(day) * hook,
                    $"{site.Id} day {day}");
        }
        Assert.Contains(Engine.State.SeaSites, s => s.Kind == SiteKind.Offshore);
    }

    /// <summary>
    /// Pre-move (UpdateTrafficLightZones): <c>pops = State.Populations.Where(p =&gt; p.ZoneId == zone.Id)</c>,
    /// <c>fish = pops.SelectMany(p =&gt; p.Cohorts).Sum(c =&gt; c.Count)</c>,
    /// <c>lice = fish &gt; 0 ? pops.Sum(p =&gt; p.Cohorts.Sum(c =&gt; c.Count) * p.SeaLiceLoad) / fish : 0</c>.
    /// </summary>
    /// <summary>
    /// The pre-move terms for every population not in an offshore unit and not
    /// the first of its zone, bit for bit; the first population of each fjord
    /// zone adds exactly its production area's offshore units' larval output
    /// (F2-08, spec §3.3 "Lice and zones": each unit's fish × lice per fish ×
    /// the calibrated 0.08, summed in the pens' order, and no fish), so each
    /// fjord zone's mean is the pre-move mean plus that output over the zone's
    /// fish, never less, and a zone with no unit in its area (or a zone the game
    /// does not hold) is the pre-move mean bit for bit; an offshore unit's fish
    /// add nothing to any zone's fish count (F2-03: they never dilute a fjord
    /// zone, spec §3.1 decision 1), and its own water area's mean is empty.
    /// Replaces F2-02's <c>..._AndTheZoneMeanIsThePreMoveMean</c> (every term the
    /// pre-move one): F2-08 fills this hook on purpose (RUN_LOG §7, F2-08;
    /// docs/run/evidence/f2_08_replaced_tests.md).
    /// </summary>
    [Fact]
    public void ZoneLiceContribution_IsEachPopulationsFishTimesItsLice_AndTheZoneMeanAddsOnlyTheUnitsOutput()
    {
        double weight = LakskongenSim.Calibration.OffshoreCalibration.Default.LiceCoupling.ZoneOutputWeight;
        var unit = Assert.Single(Engine.OffshoreUnits);
        int area = Engine.ProductionAreaOfSite(unit);
        double unitOutput = 0.0;
        foreach (var pop in unit.Pens)
            unitOutput += pop.Cohorts.Sum(c => c.Count) * pop.SeaLiceLoad;
        Assert.True(unitOutput > 0.0, "the fixture's unit carries no lice: the output term would be vacuous");
        double output = weight * unitOutput;

        int offshorePopulations = 0, anchors = 0;
        foreach (var population in Engine.State.Populations)
        {
            var holding = Engine.HoldingOf(population);
            var share = Engine.ZoneLiceContribution(holding.Site, population);
            if (holding.Site?.Kind == SiteKind.Offshore)
            {
                offshorePopulations++;
                Assert.Equal(default(ZoneLiceShare), share);
                continue;
            }
            int fish = population.Cohorts.Sum(c => c.Count);
            Assert.Equal(fish, share.Fish);
            double own = population.Cohorts.Sum(c => c.Count) * population.SeaLiceLoad;
            var zone = Engine.State.Zones.FirstOrDefault(z => z.Id == population.ZoneId);
            bool anchor = ReferenceEquals(Engine.State.Populations.First(p => p.ZoneId == population.ZoneId), population);
            if (anchor && zone != null && Engine.ProductionAreaOf(zone) == area)
            {
                anchors++;
                SameBits(own + output, share.LiceWeightedFish, population.ZoneId + " (its first population carries the unit's larvae)");
            }
            else
            {
                SameBits(own, share.LiceWeightedFish, population.ZoneId);
            }
        }
        Assert.Equal(2, offshorePopulations);
        Assert.True(anchors >= 1, "no fjord zone shares the unit's production area");

        var zoneIds = Engine.State.Zones.Select(z => z.Id).Concat(new[] { "", "no_such_zone" }).ToList();
        Assert.Contains(zoneIds, id => Engine.State.Populations.Count(p => p.ZoneId == id) >= 2);
        foreach (var zoneId in zoneIds)
        {
            var pops = Engine.State.Populations.Where(p => p.ZoneId == zoneId).ToList();
            Assert.DoesNotContain(pops, p => Engine.HoldingOf(p).Site?.Kind == SiteKind.Offshore);
            int fish = pops.SelectMany(p => p.Cohorts).Sum(c => c.Count);
            var zone = Engine.State.Zones.FirstOrDefault(z => z.Id == zoneId);
            bool carries = zone != null && Engine.ProductionAreaOf(zone) == area && pops.Count > 0;
            double weighted = 0.0;
            for (int i = 0; i < pops.Count; i++)
                weighted += pops[i].Cohorts.Sum(c => c.Count) * pops[i].SeaLiceLoad + (i == 0 && carries ? output : 0.0);
            double preMove = fish > 0 ? pops.Sum(p => p.Cohorts.Sum(c => c.Count) * p.SeaLiceLoad) / fish : 0.0;
            double lice = fish > 0 ? weighted / fish : 0.0;
            var (hookFish, hookLice) = Engine.ZoneLiceForTrafficLight(zoneId);
            Assert.Equal(fish, hookFish);
            if (carries)
            {
                // The exact sum the engine makes (the anchor's share holds its own term plus the output).
                double engineSum = 0.0;
                foreach (var p in pops)
                    engineSum += Engine.ZoneLiceContribution(Engine.HoldingOf(p).Site, p).LiceWeightedFish;
                SameBits(engineSum / fish, hookLice, $"zone '{zoneId}'");
                Assert.Equal(lice, hookLice, 12);
                Assert.True(hookLice >= preMove, $"zone '{zoneId}' looks better with the unit");
            }
            else
            {
                SameBits(preMove, hookLice, $"zone '{zoneId}'");
            }
        }
        Assert.Equal((0, 0.0), Engine.ZoneLiceForTrafficLight(unit.ZoneId));
    }

    /// <summary>
    /// Pre-move: every sea site took the fjord storm wear, which every fjord
    /// site still does; F2-03 leaves an offshore unit out, as the pre-F2
    /// offshore farm (not a sea site) never took it.
    /// </summary>
    [Fact]
    public void SkipsFjordStormWear_IsFalse_OnEverySite()
    {
        Assert.All(Engine.State.SeaSites.Where(s => s.Kind == SiteKind.Fjord), site => Assert.False(Engine.SkipsFjordStormWear(site)));
        Assert.All(Engine.OffshoreUnits, site => Assert.True(Engine.SkipsFjordStormWear(site)));
        Assert.NotEmpty(Engine.OffshoreUnits);
    }

    /// <summary>Pre-move (ProcessDueWellboatJobs): every due job ran on its day.</summary>
    [Fact]
    public void OperationPossible_IsTrue_ForEveryJob_AtEverySite_OnEveryDay()
    {
        var sites = Engine.State.SeaSites.Append(null).ToList(); // and a job for no known site
        foreach (var site in sites)
            foreach (var kind in JobKinds)
                for (int day = 0; day < 2 * SimCalendar.DaysPerYear; day += 7)
                    Assert.True(Engine.OperationPossible(site, kind, day), $"{site?.Id ?? "(none)"} {kind} day {day}");
    }

    /// <summary>
    /// Pre-move: every sea site counted (the bridge summed the remote operations
    /// centre's budget over the site list); every fjord site still does, an
    /// offshore unit (crewed on board) does not (F2-03), and the engine's
    /// budget sum is the fjord sites' watch modes.
    /// </summary>
    [Fact]
    public void CountsTowardSiteLimits_IsTrue_OnEverySite()
    {
        var fjord = Engine.State.SeaSites.Where(s => s.Kind == SiteKind.Fjord).ToList();
        Assert.All(fjord, site => Assert.True(Engine.CountsTowardSiteLimits(site)));
        Assert.All(Engine.OffshoreUnits, site => Assert.False(Engine.CountsTowardSiteLimits(site)));
        Assert.Equal(LakskongenSim.Optimization.RemoteOpsCentre.UsedBy(fjord), Engine.RemoteOpsAttentionUsed());
        Assert.Equal(fjord.Count, Engine.SitesCountingTowardLimits());
    }

    /// <summary>Pre-move (Biology feeding): every holding fed its ration (fasting aside).</summary>
    [Fact]
    public void FeedAvailableFor_IsTrue_ForEveryHolding()
    {
        Assert.All(Engine.State.Populations, p => Assert.True(Engine.FeedAvailableFor(p), Engine.HoldingOf(p).Kind.ToString()));
        // The husbandry record's default is the same: a holding with feed
        // (U2-04's recipe is a required argument since the wave-3 integration;
        // the standard feed changes nothing).
        Assert.True(new SimulationEngine.HoldingHusbandry(default, 1.0, 1.0, 1.0, 20.0, 0.9,
            FeedRecipeEffect.Standard(new FeedPlanCalibration())).FeedAvailable);
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
                    // F2-03: the farm is an offshore unit on off_1; its zone is
                    // the plot's water area, which no fjord zone is, so its sea is
                    // the home area's, as the pre-F2 farm's zone "" gave it.
                    var site = holding.Site!;
                    var unit = site.Offshore!;
                    Assert.Null(holding.Zone);
                    SameBits(Engine.SeaTemperatureOfZone(site.ZoneId), hook, "offshore temperature");
                    SameBits(Engine.SeaTemperatureOfZone(""), hook, "the pre-F2 farm's sea temperature");
                    double seaOxygen = holding.Zone?.WaterChemistry?.OxygenO2
                        ?? WaterQuality.CalculateOxygenSaturation(hook, HoldingWater.SeawaterSalinityPsu);
                    SameBits(seaOxygen + unit.OxygenBoost, water.OxygenMgPerL, "offshore oxygen");
                    Assert.Equal(1.5, unit.OxygenBoost);
                    Assert.Equal(HoldingWater.SeawaterSalinityPsu, water.SalinityPsu);
                    Assert.Equal(Engine.ProductionAreaOfZone(site.ZoneId), water.ProductionArea);
                    Assert.Equal(Engine.HomeProductionArea, water.ProductionArea);
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
    /// StageMultiplier, unscaled. F2-13 makes the smolt tier's carryover live
    /// (RUN_LOG §7, 2026-10-20; replaces F2-02's
    /// <c>QualityMortalityMultiplier_IsOne_WhateverTheSmoltTier_AndLeavesTheAnchorDeathsUnchanged</c>,
    /// whose every assertion still holds where the behaviour is unchanged):
    /// a standard smolt's multiplier is exactly 1.0, so every fixture
    /// population (all standard) and a population run with the multiplier kill
    /// exactly the fish the pre-move call did; and more: a sea population of
    /// large smolts dies at 0.82 and of SuperSmolts at 0.67 of the anchor
    /// (<see cref="SmoltQuality.MortalityMultiplier"/>, the Chilean trial),
    /// fish-weighted over mixed cohorts, and a land tank's fish carry no
    /// sea-phase carryover whatever their tier.
    /// </summary>
    [Fact]
    public void QualityMortalityMultiplier_IsOneForStandardSmolts_AndTheTiersCarryoverIsLiveAtSea()
    {
        foreach (var tier in Enum.GetValues<SmoltQualityTier>())
            SameBits(SmoltQualityRegistry.Get(tier).MortalityMultiplier, Engine.QualityMortalityMultiplier(Population(tier)), $"{tier}");
        Assert.Equal(1.0, Engine.QualityMortalityMultiplier(Population(SmoltQualityTier.Standard)));
        Assert.Equal(0.82, Engine.QualityMortalityMultiplier(Population(SmoltQualityTier.Large)));
        Assert.Equal(0.67, Engine.QualityMortalityMultiplier(Population(SmoltQualityTier.SuperSmolt)));
        // Fish-weighted over the cohorts: 30,000 SuperSmolts and 10,000 standard smolts.
        var mixed = Population(SmoltQualityTier.SuperSmolt, 30_000);
        mixed.Cohorts.Add(new FishPopulation.Cohort(400, 10_000, 1.0, LifeStage.Seawater));
        Assert.Equal((30_000 * 0.67 + 10_000 * 1.0) / 40_000, Engine.QualityMortalityMultiplier(mixed), 12);
        // Every fixture population is standard: the call site's multiplier is exactly 1.0, as before F2-13.
        Assert.All(Engine.State.Populations, p => Assert.All(p.Cohorts, c => Assert.Equal(SmoltQualityTier.Standard, c.Quality)));
        foreach (var population in Engine.State.Populations)
            Assert.Equal(1.0, Engine.UnitMortalityTerms(population).AnchorMultiplier * Engine.QualityMortalityMultiplier(population));
        // A land tank's fish carry no sea-phase carryover, whatever their tier.
        var land = Engine.RASFacilities.SelectMany(f => f.Tanks).First(p => p.TotalCount > 0);
        var tiers = land.Cohorts.Select(c => c.Quality).ToList();
        try
        {
            foreach (var cohort in land.Cohorts) cohort.Quality = SmoltQualityTier.SuperSmolt;
            Assert.Equal(HoldingKind.LandTank, Engine.HoldingOf(land).Kind);
            Assert.Equal(1.0, Engine.QualityMortalityMultiplier(land));
        }
        finally
        {
            for (int i = 0; i < tiers.Count; i++) land.Cohorts[i].Quality = tiers[i];
        }

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
        // ... and the SuperSmolt's multiplier kills about 0.67 of them.
        var super = AnchorOnlyEngine();
        for (int day = 0; day < SimCalendar.DaysPerYear; day++)
            super.Pop.AdvanceDays(1, skipGrowth: true, 0.05, 0.0, anchorHazardMultiplier: 0.67);
        double ratio = (50_000.0 - super.Pop.TotalCount) / (50_000.0 - plain.Pop.TotalCount);
        Assert.InRange(ratio, 0.6, 0.7);
    }

    private static FishPopulation Population(SmoltQualityTier tier, int count = 10_000)
    {
        var population = new FishPopulation { ZoneId = "z", SiteId = "s" };
        population.Cohorts.Add(new FishPopulation.Cohort(400, count, 1.0, LifeStage.Seawater) { Quality = tier });
        population.InjectDependencies(Engine.Growth, Engine.Mortality);
        return population;
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
    /// whatever the quality tier (no premium). F2-13 closes the exploit of
    /// buying the tier's carryover at the standard price (RUN_LOG §7,
    /// 2026-10-20; replaces F2-02's
    /// <c>SmoltTierCostPerFish_TimesCount_IsThePreMoveStockingCost_ForEveryStrainTierAndSource</c>,
    /// whose assertion still holds bit for bit for every standard smolt and
    /// every smolt from the player's own source): a bought large smolt costs
    /// ×1.6 and a bought SuperSmolt ×2.5 (<c>facilities.land.smolt</c>), and a
    /// stocking through the sea verb pays exactly that.
    /// </summary>
    [Fact]
    public void SmoltTierCostPerFish_IsThePreMoveCost_ForStandardAndOwnSmolts_AndChargesTheTierPremiumOnBoughtOnes()
    {
        decimal price = CalibrationRegistry.Default.Economy.SmoltCostPerFishNOK;
        var smolt = CalibrationRegistry.Default.Facilities.Land.Smolt;
        int[] counts = { 1, 7, 999, 10_000, 36_000, 200_000, 561_600, 1_000_003 };
        foreach (var strain in SmoltStrainRegistry.All)
            foreach (var tier in Enum.GetValues<SmoltQualityTier>())
                foreach (var source in Enum.GetValues<SmoltSource>())
                {
                    decimal perFish = Engine.SmoltTierCostPerFish(tier, strain, source);
                    decimal premium = source == SmoltSource.OwnFacility || tier == SmoltQualityTier.Standard ? 1m
                        : tier == SmoltQualityTier.SuperSmolt ? (decimal)smolt.TierPremiumSuperSmolt : (decimal)smolt.TierPremiumLarge;
                    foreach (int count in counts)
                    {
                        if (premium == 1m)
                            SameBits(count * price * (decimal)strain.CostMultiplier, count * perFish, $"{strain.Id} {tier} {source} x{count}");
                        else
                            Assert.Equal(count * price * (decimal)strain.CostMultiplier * premium, count * perFish);
                    }
                }
        Assert.Equal((1.6, 2.5), (smolt.TierPremiumLarge, smolt.TierPremiumSuperSmolt));

        // The sea verb pays the hook's price: a bought SuperSmolt costs 2.5 standard smolts.
        foreach (var (tier, factor) in new[] { (SmoltQualityTier.Standard, 1m), (SmoltQualityTier.Large, 1.6m), (SmoltQualityTier.SuperSmolt, 2.5m) })
        {
            var engine = new SimulationEngine();
            engine.Initialize(enableEvents: false, seed: 3);
            Assert.True(engine.StartScenario("po3_red_sone").Ok);
            engine.ReceiveLoan(100_000_000m);
            var site = engine.FjordSites().First();
            decimal before = engine.FinanceManager.Balance;
            var stocked = engine.StockSmolts(site.Id, 10_000, "standard", tier);
            Assert.True(stocked.Ok, stocked.Message);
            Assert.Equal(10_000 * price * factor, before - engine.FinanceManager.Balance);
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
        // F2-03: the offshore farm is an offshore unit now; its harvests still count.
        Assert.NotEmpty(Engine.OffshoreUnits);
        foreach (var unit in Engine.OffshoreUnits)
            Assert.True(Engine.IsResourceRentLiable(unit.Id, HoldingKind.Offshore));
        Assert.Equal(2, Engine.RASFacilities.Count);
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
