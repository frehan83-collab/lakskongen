using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Environment;
using LakskongenSim.Events;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using LakskongenSim.Harvest;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// The site-kind audit (roadmap F2-03 acceptance 1; docs/FARM_TYPES_SPEC.md §3.1
/// decision 4): every consumer of the sea-site list in the sim and the bridge
/// (<c>SeaSites</c>, <c>GetSeaSiteDetails</c>) has an include, exclude or hook
/// decision in <c>docs/run/evidence/f2_03_sitekind_audit.md</c>, and each
/// category of decisions has a test here on a game that holds every kind: two
/// fjord sites of po3_red_sone (zones po3_nord, po3_sor), a stocked offshore
/// unit on off_1, a stocked grow-out land site on pad_a and a post-smolt site
/// on pad_delta. The audit-completeness test fails when a consumer appears,
/// moves to another member or disappears without the document following.
/// </summary>
public class SiteKindAuditTests
{
    private readonly ITestOutputHelper _output;
    public SiteKindAuditTests(ITestOutputHelper output) => _output = output;

    private const string Category = "SiteKind";

    /// <summary>The categories of the audit, each tested below (trait "SiteKind" = the category).</summary>
    internal static readonly string[] Categories =
    {
        "units-and-lists", "fjord-zone", "site-limits", "fjord-economics", "storm-wear", "lice",
        "capacity", "harvest-wellboat", "escapes-sustainability", "events", "fjord-hardware", "reports",
    };

    // ── The fixture: every kind in one game ─────────────────────────────────

    private sealed record Mixed(SimulationEngine Engine, SeaSite Nord, SeaSite Sor, SeaSite Unit, RASFacility Land, RASFacility PostSmolt);

    /// <summary>po3_red_sone stocked, plus an offshore unit, a grow-out land site and a post-smolt site, each stocked and operational.</summary>
    private static Mixed MixedGame(int seed, bool events = false, bool withUnit = true)
    {
        var engine = new SimulationEngine { FinanceManager = new FinanceManager(5_000_000_000m) };
        engine.Initialize(enableEvents: events, seed: seed);
        Assert.True(engine.StartScenario(PlayerScriptProbe.CareerScenarioId).Ok);
        engine.FinanceManager.AddRevenue(4_000_000_000m);
        Assert.True(engine.StockSmolts("po3_nord", 12_000, "standard").Ok);
        Assert.True(engine.StockSmolts("po3_sor", 10_000, "standard").Ok);
        Assert.True(engine.BuildRASFacility("Land", RASScale.Small).Ok);
        Assert.True(engine.BuildPostSmoltFacility("Elv").Ok);
        if (withUnit) Assert.True(engine.BuildOffshoreFarm("Hav", OffshoreType.SemiSubmersible).Ok);
        engine.RASFacilities.ForEach(f => f.ConstructionDays = 1);
        foreach (var u in engine.OffshoreUnits) u.Offshore!.ConstructionDays = 1;
        engine.Advance(2);
        var land = engine.RASFacilities.Single(f => f.Kind == LandSiteKind.GrowOut);
        var postSmolt = engine.PostSmoltSites.Single();
        Assert.True(engine.StockRASFacility(land.Id, 20_000).Ok);
        Assert.True(engine.StartPostSmoltBatch(postSmolt.Id, 50_000).Ok);
        SeaSite unit = null;
        if (withUnit)
        {
            unit = engine.OffshoreUnits.Single();
            Assert.True(engine.StockOffshoreFarm(unit.Id, 60_000).Ok);
        }
        var nord = engine.State.SeaSites.Single(s => s.Id == "po3_nord");
        var sor = engine.State.SeaSites.Single(s => s.Id == "po3_sor");
        return new Mixed(engine, nord, sor, unit!, land, postSmolt);
    }

    // ── units-and-lists ─────────────────────────────────────────────────────

    /// <summary>
    /// Include: lookups by id, the unit lists, the pen views, the harvest
    /// holding and the save's relink treat an offshore unit as the sea site it
    /// is; the fjord views (FjordSites) and the unit views split the kinds.
    /// </summary>
    [Fact]
    [Trait(Category, "units-and-lists")]
    public void UnitsAndLists_FindEveryKindById_AndSplitTheKinds()
    {
        var m = MixedGame(301);
        var e = m.Engine;
        Assert.Equal(new[] { "po3_nord", "po3_sor" }, e.FjordSites().Select(s => s.Id));
        Assert.Equal(new[] { m.Unit }, e.OffshoreUnits);
        Assert.Same(m.Unit, e.FindOffshoreUnit(m.Unit.Id));
        Assert.Null(e.FindOffshoreUnit("po3_nord"));
        Assert.Same(m.Land, e.FindLandSite(m.Land.Id));
        Assert.Equal(new[] { m.Land, m.PostSmolt }, e.LandSites);
        Assert.Equal(3, e.PenViewsOf(m.Unit.Id).Count);
        Assert.Equal(1, e.PenViewsOf(m.Unit.Id).Count(p => p.FishCount > 0));
        Assert.Equal(HarvestRouteKind.OffshoreWellboat, e.QuoteHarvest(m.Unit.Id).Route);
        var unitView = Assert.Single(e.OffshoreUnitViews());
        Assert.Equal((m.Unit.Id, "off_1", 3, 1, 60_000L), (unitView.Id, unitView.PlotId, unitView.Zones, unitView.OccupiedZones, unitView.FishCount));
        Assert.Equal(new[] { "GrowOut", "PostSmolt" }, e.LandSiteViews().Select(v => v.Kind));
        Assert.Equal(new[] { "pad_a", "pad_b", "pad_delta" }, e.SitePlotViews(PlotKind.LandPad).Select(p => p.Id));
        Assert.Equal(new[] { m.Land.Id, "", m.PostSmolt.Id }, e.SitePlotViews(PlotKind.LandPad).Select(p => p.OwnerUnitId));
        Assert.Equal(new[] { m.Unit.Id, "", "" }, e.SitePlotViews(PlotKind.OffshoreSlot).Select(p => p.OwnerUnitId));

        var resumed = SaveStateComparer.Resume(SaveStateComparer.Serialize(e), events: false);
        var unit = resumed.OffshoreUnits.Single();
        var pen = unit.Pens.Single();
        Assert.Contains(resumed.State.Populations, p => ReferenceEquals(p, pen));
        Assert.Equal((unit.Id, unit.ZoneId), (pen.SiteId, pen.ZoneId));
        Assert.Empty(UnitResolver.Violations(resumed.State));
    }

    // ── fjord-zone ──────────────────────────────────────────────────────────

    /// <summary>
    /// Exclude, by zone: an offshore unit's and a land site's fish sit in their
    /// plot's water area, which no fjord zone is, so the zone systems never see
    /// them: the traffic-light mean, the zone's biomass load on its water, the
    /// bloom deck, the per-zone Mattilsynet orders and red-zone credit, the
    /// licence auctions by zone and the cohort transfers between a zone's sites.
    /// </summary>
    [Fact]
    [Trait(Category, "fjord-zone")]
    public void FjordZoneSystems_NeverSeeAnOffshoreUnitOrALandSite()
    {
        var m = MixedGame(302, events: true);
        var e = m.Engine;
        var bare = MixedGame(302, events: true, withUnit: false);
        Assert.DoesNotContain(e.State.Zones, z => z.Id == m.Unit.ZoneId || z.Id == m.Land.ZoneId || z.Id == m.PostSmolt.ZoneId);
        foreach (var zone in e.State.Zones)
            Assert.Equal(bare.Engine.ZoneLiceForTrafficLight(zone.Id), e.ZoneLiceForTrafficLight(zone.Id));
        Assert.Equal((0, 0.0), e.ZoneLiceForTrafficLight(m.Unit.ZoneId));

        for (int week = 0; week < 40; week++)
        {
            e.Advance(7);
            // The zones' fish load is the fjord pens' fish, never the unit's or the land's.
            double fjordKg = e.State.SeaSites.Where(s => s.Kind == SiteKind.Fjord).Sum(s => s.Pens.Sum(p => p.GetTotalBiomassKg()));
            Assert.InRange(e.State.Zones.Sum(z => z.CurrentFishBiomassKg), 0.0, fjordKg * 1.0001 + 1.0);
            Assert.DoesNotContain(e.State.ActiveBlooms, b => b.SiteId == m.Unit.Id);
        }
        Assert.True(m.Unit.Pens.Sum(p => p.GetTotalBiomassKg()) > 0);
        Assert.Equal(100.0, m.Unit.GillScore);

        // Licences and transfers by zone: the unit opens no zone and is no transfer destination.
        Assert.True(e.CanBuildSeaSite("po3_nord", out _));
        Assert.DoesNotContain(CohortTransferSystem.GetValidDestinations("po3_nord", e.State, 1.0), s => s.Kind == SiteKind.Offshore);
        Assert.Empty(CohortTransferSystem.GetValidDestinations(m.Unit.Id, e.State, 1.0));
        // A transfer forced past the destination list moves nothing either way.
        long unitFish = m.Unit.Pens.Sum(p => (long)p.TotalCount), nordFish = m.Nord.Pens.Sum(p => (long)p.TotalCount);
        foreach (var (from, to) in new[] { (m.Unit.Id, m.Nord.Id), (m.Nord.Id, m.Unit.Id) })
        {
            var plan = CohortTransferSystem.PlanTransfer(from, to, 1_000, 0.5,
                CohortTransferSystem.TransferReason.OffshoreStocking, e.State, hasVeterinarian: false);
            decimal paid = 0m;
            var moved = CohortTransferSystem.ExecuteTransfer(plan, e.State, c => paid += c, new Random(1), e.Growth, e.Mortality);
            Assert.False(moved.Success);
            Assert.Equal((0, 0m), (moved.FishTransferred, paid));
        }
        Assert.Equal((unitFish, nordFish), (m.Unit.Pens.Sum(p => (long)p.TotalCount), m.Nord.Pens.Sum(p => (long)p.TotalCount)));
    }

    // ── site-limits ─────────────────────────────────────────────────────────

    /// <summary>Hook: the remote operations centre watches the fjord sites; an offshore unit is crewed on board.</summary>
    [Fact]
    [Trait(Category, "site-limits")]
    public void SiteLimits_CountTheFjordSites_NotTheOffshoreUnit()
    {
        var m = MixedGame(303);
        var e = m.Engine;
        Assert.True(e.CountsTowardSiteLimits(m.Nord));
        Assert.False(e.CountsTowardSiteLimits(m.Unit));
        Assert.Equal(2, e.SitesCountingTowardLimits());
        int fjord = Optimization.RemoteOpsCentre.CostFor(m.Nord.WatchMode) + Optimization.RemoteOpsCentre.CostFor(m.Sor.WatchMode);
        Assert.Equal(fjord, e.RemoteOpsAttentionUsed());
        Assert.Equal(fjord, e.RemoteOpsAttentionUsed(m.Unit.Id, "watch")); // a mode at the unit changes nothing
        Assert.Equal(SimulationEngine.OffshoreWatchMode, m.Unit.WatchMode);
        Assert.Equal(25, Optimization.RemoteOpsCentre.CostFor(m.Unit.WatchMode)); // what it would cost, were it counted
    }

    // ── fjord-economics ─────────────────────────────────────────────────────

    /// <summary>
    /// Exclude, by kind: the fjord site crews, upkeep and per-site carbon leave
    /// an offshore unit out (it books its own: <see cref="OffshoreTickSystem"/>),
    /// and the night shift's rota draws nothing for it.
    /// </summary>
    [Fact]
    [Trait(Category, "fjord-economics")]
    public void FjordEconomics_TheUnitPaysAndEmitsOnlyItsOwn()
    {
        var with = MixedGame(304).Engine;
        var without = MixedGame(304, withUnit: false).Engine;
        // The day's crew and upkeep with the unit equal those without it.
        with._dailyFarmSystemsOpex = 0m;
        without._dailyFarmSystemsOpex = 0m;
        decimal a0 = with.FinanceManager.Balance, b0 = without.FinanceManager.Balance;
        with.ProcessDailyFinance();
        without.ProcessDailyFinance();
        Assert.Equal(b0 - without.FinanceManager.Balance, a0 - with.FinanceManager.Balance);

        // One fuel and one power entry a day at the unit: its own, never the fjord formula's too.
        var unit = with.OffshoreUnits.Single();
        int before = unit.CarbonTracker.Emissions.Count;
        with.ProcessCarbonLedger();
        var added = unit.CarbonTracker.Emissions.Skip(before).ToList();
        Assert.Equal(1, added.Count(x => x.Category == "Scope1_Fuel"));
        Assert.Equal(1, added.Count(x => x.Category == "Scope2_Electricity"));
        Assert.Contains(added, x => x.Source == unit.Id + "_vessel");

        // The night shift (a non-standard rota) rolls the fjord sites only: same draws, same events.
        with.State.NightShiftPolicy = Logistics.NightShiftPolicy.AutomatedROC;
        without.State.NightShiftPolicy = Logistics.NightShiftPolicy.AutomatedROC;
        int logBefore = with.State.EventLog.Count, logBeforeWithout = without.State.EventLog.Count;
        decimal nightWith = with.FinanceManager.Balance, nightWithout = without.FinanceManager.Balance;
        for (int night = 0; night < 60; night++)
        {
            with.ProcessNightShiftPolicy();
            without.ProcessNightShiftPolicy();
        }
        Assert.Equal(without.Rng.CaptureStates().OrderBy(k => k.Key).Select(k => (k.Key, k.Value)),
            with.Rng.CaptureStates().OrderBy(k => k.Key).Select(k => (k.Key, k.Value)));
        var nightLog = with.State.EventLog.Skip(logBefore).ToList();
        foreach (var x in nightLog) _output.WriteLine($"{x.Day} {x.Name}: {x.Description}");
        Assert.DoesNotContain(nightLog, x => x.Description.Contains(unit.Label + " ") || x.Description.Contains(unit.Id));
        Assert.Equal(without.State.EventLog.Skip(logBeforeWithout).Select(x => x.Description), nightLog.Select(x => x.Description));
        Assert.Equal(nightWithout - without.FinanceManager.Balance, nightWith - with.FinanceManager.Balance);
    }

    // ── storm-wear ──────────────────────────────────────────────────────────

    /// <summary>Hook: a severe storm wears the fjord nets and kills fjord fish; the offshore unit takes none of it.</summary>
    [Fact]
    [Trait(Category, "storm-wear")]
    public void StormWear_HitsTheFjord_NeverTheOffshoreUnit()
    {
        var m = MixedGame(305, events: true);
        var e = m.Engine;
        e.Weather.ImportState(e.State.Day, 4.0, 30.0, 9.0, WeatherState.Severe);
        long unitFish = m.Unit.Pens.Sum(p => (long)p.TotalCount);
        e.BeginMortalityDay();
        for (int i = 0; i < 5; i++)
        {
            e.ApplyStormHazards();
            e.ApplyStormAndTemperatureImpacts();
        }
        Assert.True(m.Nord.NetIntegrityPct < 100.0 || m.Sor.NetIntegrityPct < 100.0, "the fjord nets took no wear");
        Assert.Equal(100.0, m.Unit.NetIntegrityPct);
        Assert.Equal(0, m.Unit.EscapesThisYear);
        Assert.True(e.SkipsFjordStormWear(m.Unit));
        Assert.False(e.SkipsFjordStormWear(m.Nord));
        Assert.Equal(0, e.MortalityLedger.ForSite(m.Unit.Id).Deaths(MortalityCause.Storm).Fish);
        Assert.Equal(unitFish, m.Unit.Pens.Sum(p => (long)p.TotalCount));
    }

    // ── lice ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Include, with a hook: the offshore unit runs its own site lice model at
    /// its plot's external pressure (0.12 at off_1, held at the model's 0.2
    /// floor), is counted weekly against its area's limit and gets a
    /// Mattilsynet order when it breaches; the company average counts its fish.
    /// </summary>
    [Fact]
    [Trait(Category, "lice")]
    public void Lice_TheUnitIsCountedAndRegulated_AtItsPlotsPressure()
    {
        var m = MixedGame(306);
        var e = m.Engine;
        e.AdvanceSiteLiceModels();
        double seasonal = SimulationEngine.SeasonalLicePressure(e.State.Day);
        Assert.Equal(Math.Clamp(seasonal, 0.2, 3.0), m.Nord.Lice.ExternalPressureMultiplier, 12);
        Assert.Equal(Math.Clamp(seasonal * 0.12, 0.2, 3.0), m.Unit.Lice.ExternalPressureMultiplier, 12);
        Assert.Equal(0.12, e.ExternalLicePressureFor(m.Unit));

        // The weekly census counts the unit (the game's own cadence).
        for (int week = 0; week < 3; week++)
            e.Advance(7);
        Assert.True(m.Unit.LastLiceCountDay > 0, "the weekly census never counted the unit");
        Assert.InRange(m.Unit.LastLiceCountDay, e.State.Day - 7, e.State.Day);

        // Over its area's limit on two counts: Mattilsynet orders a delousing at the unit.
        double limit = e.LiceLimitAtSite(m.Unit);
        int orders = e.State.EventLog.Count(x => x.Name == "Mattilsynet order");
        for (int count = 0; count < 2; count++)
        {
            m.Unit.Lice.AdultFemalePerFish = limit * 3.0;
            e.UpdateSiteLiceCounts();
        }
        foreach (var x in e.State.EventLog.Where(x => x.Description.Contains(m.Unit.Label + " "))) _output.WriteLine($"{x.Day} {x.Name}: {x.Description}");
        Assert.Equal(limit * 3.0, m.Unit.LastLiceCount, 12);
        Assert.Equal(e.State.Day + 14, m.Unit.TreatmentOrderDueDay);
        Assert.Equal(orders + 1, e.State.EventLog.Count(x => x.Name == "Mattilsynet order"));
        Assert.Contains(e.State.EventLog, x => x.Name == "Mattilsynet order" && x.Description.Contains(m.Unit.Label + " er over lusegrensen"));
        Assert.True(m.Nord.TreatmentOrderDueDay < 0 && m.Sor.TreatmentOrderDueDay < 0, "a fjord site was ordered for the unit's lice");
        long fish = e.State.SeaSites.Sum(s => s.Pens.Sum(p => (long)p.TotalCount));
        double expected = e.State.SeaSites.Sum(s => s.Pens.Sum(p => p.Cohorts.Sum(c => c.Count * s.Lice.AverageLicePerFish))) / fish;
        Assert.Equal(expected, e.GetCompanyLiceAverage(), 9);
        Assert.True(m.Unit.Pens.Sum(p => p.TotalCount) > 0);
    }

    // ── capacity ────────────────────────────────────────────────────────────

    /// <summary>
    /// Hook: an offshore unit's fish stay out of its area's pooled licence MTB
    /// until F2-09 binds its development licences; its own MTB caps it; the
    /// fjord stocking verbs refuse it; the enforcement never touches it.
    /// </summary>
    [Fact]
    [Trait(Category, "capacity")]
    public void Capacity_TheAreaPoolHoldsTheFjord_TheUnitHoldsItsOwnMtb()
    {
        var m = MixedGame(307);
        var e = m.Engine;
        int area = e.ProductionAreaOfSite(m.Nord);
        Assert.Equal(area, e.ProductionAreaOfSite(m.Unit)); // the scenario's area: the plot is in it
        Assert.Equal(new[] { "po3_nord", "po3_sor" }, e.SeaSitesInArea(area).Select(s => s.Id));
        Assert.Equal(m.Nord.CurrentBiomassTonnes + m.Sor.CurrentBiomassTonnes, e.AreaStandingBiomassTonnes(area), 9);
        Assert.False(e.CountsTowardAreaLicencePool(m.Unit));
        Assert.DoesNotContain(e.GetCapacityOverview().Sites, s => s.SiteId == m.Unit.Id);
        Assert.Equal(SimulationEngine.OffshoreUnitKey, e.StockSmolts(m.Unit.Id, 1_000).ErrorKey);
        Assert.Equal("capacity_exceeded", e.StockOffshoreFarm(m.Unit.Id, 70_000_000).ErrorKey);

        // A unit far over the area's licences: no order, no forced harvest at it.
        foreach (var c in m.Unit.Pens.Single().Cohorts) c.MeanWeightKg = 5.0; // 300 t
        e.EnforceCapacity();
        Assert.DoesNotContain(e.State.CapacityBreaches, b => b.IsOpen);
        Assert.Equal(60_000, m.Unit.Pens.Sum(p => p.TotalCount));
    }

    // ── harvest-wellboat ────────────────────────────────────────────────────

    /// <summary>
    /// Include: an offshore unit's harvest goes through the S2-04 pipeline with
    /// the offshore wellboat and counts for the rent; a planned order and a
    /// hired wellboat job run at it; a delousing treats its fish.
    /// </summary>
    [Fact]
    [Trait(Category, "harvest-wellboat")]
    public void HarvestAndWellboats_ReachTheOffshoreUnit()
    {
        var m = MixedGame(308);
        var e = m.Engine;
        e.Advance(120);
        var order = e.OrderHarvest(m.Unit.Id, null, 0.5);
        Assert.True(order.Ok, order.Message);
        for (int d = 0; d < 20 && e.HarvestOrders.Count > 0; d++) e.Advance(1);
        var report = Assert.Single(e.HarvestReports, r => r.HoldingId == m.Unit.Id);
        Assert.Equal(HarvestRouteKind.OffshoreWellboat, report.Route);
        Assert.True(e.State.ResourceRentHarvestValueThisTaxYear > 0m);

        Assert.True(e.RequestWellboat(m.Unit.Id, "rov").Ok);
        Assert.Contains(e.State.WellboatJobs, j => j.SiteId == m.Unit.Id);
        e.Advance(6);
        Assert.DoesNotContain(e.State.WellboatJobs, j => j.SiteId == m.Unit.Id);
        var deloused = e.DelouseSite(m.Unit.Id, "hydrolicer");
        Assert.True(deloused.Ok || deloused.ErrorKey == "insufficient_funds" || deloused.ErrorKey == "too_cold", deloused.ErrorKey);
    }

    // ── escapes-sustainability ──────────────────────────────────────────────

    /// <summary>
    /// Include: an escape at the unit counts in the escape score and the
    /// sustainability score; the welfare density reads the unit as one body of
    /// water (its zones' volume), not three times denser; the multi-site medal
    /// counts it as a site.
    /// </summary>
    [Fact]
    [Trait(Category, "escapes-sustainability")]
    public void EscapesAndSustainability_CountTheUnit()
    {
        var m = MixedGame(309);
        var e = m.Engine;
        Assert.Equal(250_000f, m.Unit.PenVolume);
        Assert.Equal(1, m.Unit.NumberOfPens);
        Assert.InRange(m.Unit.Pens.Sum(p => p.GetTotalBiomassKg()) / (m.Unit.PenVolume * m.Unit.MaxAllowedDensity), 0.0, 0.8);
        int incidents = e.State.SeaSites.Sum(s => s.EscapeIncidentsInLastYear(e.State.Day));
        m.Unit.RecordEscape(500, e.State.Day);
        Assert.Equal(incidents + 1, e.State.SeaSites.Sum(s => s.EscapeIncidentsInLastYear(e.State.Day)));
        Assert.True(e.State.SeaSites.Count >= 3); // MultiSiteOperator: two fjord sites and the unit
    }

    // ── events ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Exclude the fjord site cards (seals at the nets, net damage, escape
    /// fines, fjord-use disputes, the tourism view) from an offshore unit;
    /// include ISA, which culls a sea pen wherever it is.
    /// </summary>
    [Fact]
    [Trait(Category, "events")]
    public void Events_FjordCardsSkipTheUnit_IsaReachesIt()
    {
        var engine = new SimulationEngine { FinanceManager = new FinanceManager(5_000_000_000m) };
        engine.Initialize(enableEvents: true, seed: 310);
        Assert.True(engine.BuildOffshoreFarm("Hav", OffshoreType.SemiSubmersible).Ok);
        var unit = engine.OffshoreUnits.Single();
        unit.Offshore!.ConstructionDays = 1;
        engine.Advance(2);
        Assert.True(engine.StockOffshoreFarm(unit.Id, 30_000).Ok);
        Assert.Empty(engine.FjordSites());

        var fjordCards = new[] { "predator-attack", "escape-penalty", "net-damage", "fjord-access-dispute", "tourism-conflict" };
        var definitions = engine.Storyteller.GetEventDefinitions().ToDictionary(d => d.Id);
        foreach (var id in fjordCards)
            Assert.False(definitions[id].Condition!.Invoke(engine.State), $"{id} fires with only an offshore unit");
        Assert.Same(unit.Pens.Single(), NorwegianEvents.FirstSeaPen(engine.State));
        // Company-level cards still see the company's site.
        Assert.True(definitions["school-visit-pr"].Condition!.Invoke(engine.State));
    }

    // ── fjord-hardware ──────────────────────────────────────────────────────

    /// <summary>
    /// Exclude: the fjord-pen hardware (containment rungs and their red-zone
    /// credit, skirts and deterrents, barriers, bloom countermeasures) never
    /// applies to an offshore unit: it holds the open-net row, no upkeep, no
    /// credit (its zone is never a fjord zone's colour). The bridge's verbs
    /// refuse it (tests/test_bridge_contract.py).
    /// </summary>
    [Fact]
    [Trait(Category, "fjord-hardware")]
    public void FjordHardware_NeverAppliesToTheUnit()
    {
        var m = MixedGame(311);
        var e = m.Engine;
        Assert.Equal(ContainmentLevel.OpenNet, m.Unit.Containment);
        Assert.Equal(ContainmentTech.ModifiersFor(ContainmentLevel.OpenNet), SimulationEngine.ContainmentModifiersOf(m.Unit));
        decimal opex = e.PreventiveMitigationDailyOpexNok();
        m.Nord.HasLiceSkirt = true;
        Assert.Equal(opex + 2_000m, e.PreventiveMitigationDailyOpexNok());
        double mtb = m.Unit.MABTonnes;
        e.ApplyRedZoneContainmentCredit(m.Unit.ZoneId, ZoneColor.Red);
        Assert.Equal(mtb, m.Unit.MABTonnes);
        Assert.Equal(1.0, m.Unit.AppliedContainmentCredit);
        Assert.False(m.Unit.RedZoneCreditApplied);
        Assert.Equal(1.0, e.BarrierBloomPassFraction(m.Unit, "jellyfish"));
    }

    // ── reports ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Include, once: the day summary counts the unit's fish and the land
    /// sites' once each; the dashboard prices the unit as an offshore unit,
    /// never as a fjord licence and pens too.
    /// </summary>
    [Fact]
    [Trait(Category, "reports")]
    public void Reports_CountEveryUnitOnce()
    {
        var m = MixedGame(312);
        var e = m.Engine;
        var report = new DaySummary(e).GenerateSummary();
        long fish = e.State.SeaSites.Sum(s => s.Pens.Sum(p => (long)p.TotalCount)) + e.RASFacilities.Sum(f => f.Tanks.Sum(p => (long)p.TotalCount));
        Assert.Equal(fish, report.TotalFishCount);
        Assert.Equal(e.State.Populations.Sum(p => (long)p.TotalCount), fish);
        var costs = new LakskongenSim.Integration.DashboardDataProvider(e).GetCostBreakdown();
        Assert.Equal(2 * 30_000_000.0, Convert.ToDouble(costs["capex_licenses"]));
        Assert.Equal(1 * 800_000_000.0, Convert.ToDouble(costs["capex_offshore"]));
        Assert.Equal(1 * 1_000_000_000.0, Convert.ToDouble(costs["capex_ras"]));
    }

    // ── The audit is complete ───────────────────────────────────────────────

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LakskongenSim.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private const string AuditDoc = "docs/run/evidence/f2_03_sitekind_audit.md";

    private static readonly Regex Token = new(@"\bSeaSites\b|\bGetSeaSiteDetails\b", RegexOptions.Compiled);
    private static readonly Regex FjordViewToken = new(@"\bFjordSites\(", RegexOptions.Compiled);
    private static readonly Regex CsMember = new(
        @"^\s*(?:(?:public|private|internal|protected|static|override|virtual|async|sealed|readonly|partial)\s+)+[\w<>\[\],.?\s]+?\s+(\w+)\s*(?:\(|=>|\{|$)",
        RegexOptions.Compiled);
    private static readonly Regex GdMember = new(@"^(?:static\s+)?func\s+(\w+)", RegexOptions.Compiled);
    private static readonly string[] NotAMember = { "return", "var ", "if", "foreach", "for ", "while" };

    /// <summary>
    /// Every consumer of the sea-site list: (file, member) → uses, in the sim
    /// (src/LakskongenSim) and the bridge (godot/scripts/autoload C# and every
    /// GDScript file that calls GetSeaSiteDetails), comments stripped.
    /// </summary>
    internal static SortedDictionary<string, int> ScanConsumers(string root) => Scan(root, Token, gdScript: true);

    /// <summary>Every reader of the fjord view (<c>FjordSites(…)</c>, the engine's and the storyteller's) in the sim and the bridge.</summary>
    internal static SortedDictionary<string, int> ScanFjordViewConsumers(string root) => Scan(root, FjordViewToken, gdScript: false);

    private static SortedDictionary<string, int> Scan(string root, Regex token, bool gdScript)
    {
        var uses = new SortedDictionary<string, int>(StringComparer.Ordinal);
        IEnumerable<string> Files(string dir, string pattern) => Directory.EnumerateFiles(Path.Combine(root, dir), pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}addons{Path.DirectorySeparatorChar}"));
        var paths = Files("src/LakskongenSim", "*.cs").Concat(Files("godot/scripts/autoload", "*.cs"));
        if (gdScript) paths = paths.Concat(Files("godot/scripts", "*.gd"));
        foreach (var path in paths)
        {
            bool gd = path.EndsWith(".gd", StringComparison.Ordinal);
            string rel = Path.GetRelativePath(root, path).Replace('\\', '/');
            string member = "?";
            foreach (var raw in File.ReadAllLines(path))
            {
                int cut = raw.IndexOf(gd ? "#" : "//", StringComparison.Ordinal);
                string code = cut >= 0 ? raw[..cut] : raw;
                var m = (gd ? GdMember : CsMember).Match(code);
                if (m.Success && !NotAMember.Any(p => code.TrimStart().StartsWith(p, StringComparison.Ordinal)))
                    member = m.Groups[1].Value;
                int n = gd ? Regex.Matches(code, @"\bGetSeaSiteDetails\b").Count : token.Matches(code).Count;
                if (n == 0) continue;
                string key = $"{rel}|{member}";
                uses[key] = uses.TryGetValue(key, out int k) ? k + n : n;
            }
        }
        return uses;
    }

    private sealed record AuditRow(string File, string Member, int Uses, string Category, string Decision, string How);

    /// <summary>The rows of the audit's table under the heading <paramref name="section"/> ("## …", up to the next heading).</summary>
    private static List<AuditRow> AuditRows(string root, string section)
    {
        var rows = new List<AuditRow>();
        bool inTable = false;
        foreach (var line in File.ReadAllLines(Path.Combine(root, AuditDoc)))
        {
            if (line.StartsWith(section, StringComparison.Ordinal)) { inTable = true; continue; }
            if (inTable && line.StartsWith("## ", StringComparison.Ordinal)) break;
            if (!inTable || !line.StartsWith("| ", StringComparison.Ordinal) || line.StartsWith("| File", StringComparison.Ordinal)
                || line.StartsWith("|---", StringComparison.Ordinal)) continue;
            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();
            Assert.True(cells.Length == 6, $"audit row needs 6 cells: {line}");
            rows.Add(new AuditRow(cells[0].Trim('`'), cells[1].Trim('`'), int.Parse(cells[2]), cells[3], cells[4], cells[5]));
        }
        return rows;
    }

    /// <summary>
    /// The table lists exactly the scanned consumers (file, member, number of
    /// uses), each once, with a category of this file's, an include, exclude
    /// or hook decision and how it is kept; nothing listed is stale.
    /// </summary>
    private void AssertTableMatchesScan(IReadOnlyList<AuditRow> rows, SortedDictionary<string, int> scanned, string table)
    {
        var listed = rows.ToDictionary(r => $"{r.File}|{r.Member}", r => r.Uses);
        Assert.Equal(rows.Count, listed.Count); // no row twice

        var missing = scanned.Where(kv => !listed.TryGetValue(kv.Key, out int n) || n != kv.Value)
            .Select(kv => $"{kv.Key.Replace('|', ' ')}: {kv.Value} use(s), audit says {(listed.TryGetValue(kv.Key, out int n) ? n.ToString() : "nothing")}")
            .ToList();
        var stale = listed.Keys.Where(k => !scanned.ContainsKey(k)).ToList();
        foreach (var line in missing.Concat(stale.Select(s => "stale: " + s))) _output.WriteLine($"{table}: {line}");
        Assert.True(missing.Count == 0, $"{table}: consumers without an audit decision:\n" + string.Join("\n", missing));
        Assert.True(stale.Count == 0, $"{table}: audit rows with no consumer:\n" + string.Join("\n", stale));

        foreach (var row in rows)
        {
            Assert.Contains(row.Category, Categories);
            Assert.Contains(row.Decision, new[] { "include", "exclude", "hook" });
            Assert.False(string.IsNullOrWhiteSpace(row.How), $"{row.File} {row.Member}: no 'how'");
        }
    }

    /// <summary>
    /// Acceptance 1: the audit lists every consumer (file and member, with its
    /// number of uses), each with a category of this file's and an include,
    /// exclude or hook decision and how it is kept; nothing listed is stale;
    /// every category has its test here and is used by a row.
    /// </summary>
    [Fact]
    public void TheAudit_ListsEveryConsumer_WithADecision()
    {
        string root = RepoRoot();
        var rows = AuditRows(root, "## Consumers");
        Assert.True(rows.Count > 80, $"only {rows.Count} audit rows");
        AssertTableMatchesScan(rows, ScanConsumers(root), "Consumers");

        var traitValues = typeof(SiteKindAuditTests).GetMethods()
            .SelectMany(m => m.CustomAttributes.Where(a => a.AttributeType == typeof(TraitAttribute)))
            .Where(a => (string)a.ConstructorArguments[0].Value! == Category)
            .Select(a => (string)a.ConstructorArguments[1].Value!)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(Categories.OrderBy(c => c, StringComparer.Ordinal), traitValues.OrderBy(c => c, StringComparer.Ordinal));
        Assert.Equal(Categories.OrderBy(c => c, StringComparer.Ordinal), rows.Select(r => r.Category).Distinct().OrderBy(c => c, StringComparer.Ordinal));
        foreach (var decision in new[] { "include", "exclude", "hook" })
            Assert.Contains(rows, r => r.Decision == decision);
    }

    /// <summary>
    /// The consumers F2-03 moved off the list onto the fjord view (the fjord
    /// site cards, wages, upkeep, per-site carbon, the night shift, the
    /// first-site right) are listed too: every reader of <c>FjordSites(…)</c>
    /// excludes the offshore units by kind, and only the two views' own
    /// definitions are "include".
    /// </summary>
    [Fact]
    public void TheAudit_ListsEveryFjordViewConsumer()
    {
        string root = RepoRoot();
        var rows = AuditRows(root, "## Fjord-view consumers");
        AssertTableMatchesScan(rows, ScanFjordViewConsumers(root), "Fjord-view consumers");
        Assert.True(rows.Count >= 8, $"only {rows.Count} fjord-view rows");
        Assert.All(rows.Where(r => r.Member != "FjordSites"), r => Assert.Equal("exclude", r.Decision));
        Assert.Equal(2, rows.Count(r => r.Member == "FjordSites"));
    }

    /// <summary>The scan sees what it must (no vacuous pass): the hot file's consumers, a bridge partial's and a GDScript caller.</summary>
    [Fact]
    public void TheScan_FindsTheKnownConsumers()
    {
        var scanned = ScanConsumers(RepoRoot());
        Assert.Contains("src/LakskongenSim/Core/SimulationEngine.cs|AdvanceSiteLiceModels", scanned.Keys);
        Assert.Contains("godot/scripts/autoload/GameBridge.Farm.cs|GetSeaSiteDetails", scanned.Keys);
        Assert.Contains("godot/scripts/bridge/SimActions.gd", scanned.Keys.Select(k => k.Split('|')[0]));
        Assert.True(scanned.Values.Sum() > 150, $"only {scanned.Values.Sum()} uses scanned");
        var fjordView = ScanFjordViewConsumers(RepoRoot());
        Assert.Contains("src/LakskongenSim/Core/SimulationEngine.cs|ProcessCarbonLedger", fjordView.Keys);
        Assert.Contains("src/LakskongenSim/Core/SimulationEngine.cs|ProcessNightShiftPolicy", fjordView.Keys);
    }
}
