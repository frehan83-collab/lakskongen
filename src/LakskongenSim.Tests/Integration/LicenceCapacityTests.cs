using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using Xunit;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// S2-01a acceptance 3 and the register: a scenario founds the company's
/// licences (780 t, 945 t in Troms and Finnmark) pooled per production area;
/// a build adds a locality and 0 t of MTB; an auction win adds one licence of
/// the area's size; stocking is checked against the production plan's peak.
/// </summary>
public class LicenceCapacityTests
{
    private static SimulationEngine Started(string scenarioId, int seed = 11, decimal? cash = null)
    {
        var probe = new SimulationEngine();
        probe.Initialize(enableEvents: false, seed: seed);
        decimal capital = probe.Scenario.GetScenario(scenarioId)!.StartingCapitalNOK;
        var engine = new SimulationEngine { FinanceManager = new FinanceManager(capital) };
        engine.Initialize(enableEvents: false, seed: seed);
        engine.Scenario.Unlock(scenarioId);
        Assert.True(engine.StartScenario(scenarioId).Ok);
        if (cash.HasValue && cash.Value > engine.FinanceManager.Balance)
            engine.ReceiveIncome(cash.Value - engine.FinanceManager.Balance, "test_capital");
        return engine;
    }

    // ── The register a scenario founds ────────────────────────────────────

    [Fact]
    public void EveryScenario_OpensTheRegister_WithItsLicences_OfItsAreasSize()
    {
        var roster = new ScenarioSystem(new GameState());
        foreach (var scenario in roster.All.Values)
        {
            var engine = Started(scenario.Id);
            Assert.True(engine.CapacityRulesActive, scenario.Id);
            int expected = scenario.StartingSites.Sum(s => s.Licences);
            Assert.True(expected == engine.State.LicenceHoldings.Count, $"{scenario.Id}: {engine.State.LicenceHoldings.Count} licences");
            foreach (var licence in engine.State.LicenceHoldings)
            {
                Assert.Equal(ProductionAreas.Get(licence.PooledArea).LicenceMtbTonnes, licence.MtbTonnes);
                Assert.Equal(LicenceOrigin.Scenario, licence.Origin);
                Assert.Equal(0m, licence.PricePaidNok);
            }
            foreach (var def in scenario.StartingSites)
            {
                var site = engine.State.SeaSites.Single(s => s.Id == def.Id);
                double size = ProductionAreas.Get(engine.ProductionAreaOfSite(site)).LicenceMtbTonnes;
                double locality = def.LocalityMtbTonnes > 0 ? def.LocalityMtbTonnes : Math.Max(1, def.Licences) * size;
                Assert.True(locality == site.MABTonnes, $"{scenario.Id}/{def.Id}: locality {site.MABTonnes} t");
                Assert.Equal(def.Licences, engine.State.LicenceHoldings.Count(l => l.RegisteredSiteId == def.Id));
            }
        }
    }

    [Fact]
    public void TheLicenceSize_FollowsTheArea_945TonnesInTromsAndFinnmark()
    {
        var po3 = Started("po3_red_sone");
        Assert.Equal(1_560.0, po3.AreaLicenceMtbTonnes(3));
        Assert.Equal(new[] { "tl_3_1", "tl_3_2" }, po3.State.LicenceHoldings.Select(l => l.Id));

        var finnmark = Started("krise_manager");
        var licence = Assert.Single(finnmark.State.LicenceHoldings);
        Assert.Equal(12, licence.PooledArea);
        Assert.Equal(945.0, licence.MtbTonnes);
        Assert.Equal(945.0, finnmark.State.SeaSites.Single().MABTonnes);

        var free = Started("barekraft_mester");
        Assert.Equal(8 * 780.0, free.AreaLicenceMtbTonnes(4));
        Assert.Equal(3_120.0, free.State.SeaSites.Single().MABTonnes);
    }

    [Fact]
    public void ScenariosJson_CarriesTheRostersLicencesAndLocalities()
    {
        var roster = new ScenarioSystem(new GameState());
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "godot", "data", "scenarios.json")));
        foreach (var entry in json.RootElement.EnumerateArray())
        {
            var builtIn = roster.GetScenario(entry.GetProperty("id").GetString()!)!;
            foreach (var site in entry.GetProperty("starting_sites").EnumerateArray())
            {
                var def = builtIn.StartingSites.Single(s => s.Id == site.GetProperty("id").GetString());
                Assert.Equal(def.Licences, site.GetProperty("licences").GetInt32());
                double locality = site.TryGetProperty("locality_mtb_tonnes", out var l) ? l.GetDouble() : 0.0;
                Assert.Equal(def.LocalityMtbTonnes, locality);
            }
        }
    }

    // ── Acceptance 3: a build adds 0 t ────────────────────────────────────

    [Theory]
    [InlineData("po3_red_sone", "po3_nord", 3)]
    [InlineData("forste_sesong", "fjord_west", 3)]
    [InlineData("krise_manager", "fjord_north", 12)]
    public void BuildingASite_AddsALocality_AndZeroTonnesOfMtb(string scenarioId, string zone, int area)
    {
        var engine = Started(scenarioId, cash: 50_000_000m);
        double mtb = engine.AreaLicenceMtbTonnes(area);
        int licences = engine.State.LicenceHoldings.Count;
        var preview = engine.PreviewBuildSeaSite(zone);
        Assert.True(preview.Allowed, preview.Reason);
        Assert.Equal(0.0, preview.LicenceMtbAddedTonnes);
        Assert.Equal(3_120.0, preview.LocalityMtbTonnes);
        Assert.Equal(mtb, preview.AreaLicenceMtbTonnes);
        Assert.Equal(area, preview.ProductionArea);

        var built = engine.BuildSeaSite(zone, "nytt_anlegg");

        Assert.True(built.Ok, built.Message);
        Assert.Equal(mtb, engine.AreaLicenceMtbTonnes(area));
        Assert.Equal(licences, engine.State.LicenceHoldings.Count);
        var site = engine.State.SeaSites.Single(s => s.Id == "nytt_anlegg");
        Assert.Equal(engine.Calibration.Capacity.BuiltSiteLocalityMtbTonnes, site.MABTonnes);
        Assert.Equal(8, site.NumberOfPens);
        // The new locality holds only what the area's licences leave room for.
        var room = engine.PreviewStocking(site.Id, 0);
        Assert.True(room.MaxCount > 0);
        var over = engine.PreviewStocking(site.Id, room.MaxCount + 1_000);
        Assert.False(over.Allowed);
        Assert.Equal(CapacityReasons.AreaProjectedPeak, over.ReasonKey);
        Assert.True(over.AreaProjectedPeakTonnes > mtb && over.SiteProjectedPeakTonnes < site.MABTonnes);
    }

    [Fact]
    public void AnAuctionWin_GrantsOneLicence_OfTheAreasSize_InTheZonesArea()
    {
        var engine = Started("po3_red_sone", cash: 400_000_000m);
        engine.State.Zones.Add(new Zone("finnmark_vekst") { ProductionArea = 12 });
        foreach (var rival in engine.CompetitorAI.Competitors) { rival.IsDistressed = true; rival.CashReserves = 0m; }
        Assert.True(engine.ApplyForGrowthLicence("finnmark_vekst").Ok);
        Assert.True(engine.BidOnLicenseAuction("finnmark_vekst", engine.LicenseReservePriceNOK).Ok);
        var auction = engine.State.LicenseAuctions.Single(a => a.ZoneId == "finnmark_vekst");

        engine.State.Day = auction.ClosesDay + 1;
        engine.TickLicenseAuctions();

        Assert.Equal("won_player", auction.Status);
        var licence = Assert.Single(engine.State.LicenceHoldings, l => l.Origin == LicenceOrigin.Auction);
        Assert.Equal(12, licence.PooledArea);
        Assert.Equal(945.0, licence.MtbTonnes);
        Assert.Equal(engine.LicenseReservePriceNOK, licence.PricePaidNok);
        Assert.Equal(945.0, engine.AreaLicenceMtbTonnes(12));
        Assert.Equal(1_560.0, engine.AreaLicenceMtbTonnes(3));   // PO3's cap is untouched
    }

    // ── Stocking against the plan's peak ──────────────────────────────────

    [Fact]
    public void StockingPastThePlansRoom_IsRefusedWithoutCharging_AndTheRoomItselfIsTaken()
    {
        var engine = Started("po3_red_sone");
        // po3_nord's locality is the binding cap: the area has room left.
        var preview = engine.PreviewStocking("po3_nord", 0);
        int room = preview.MaxCount;
        Assert.True(room > 10_000, $"room {room}");

        decimal cash = engine.FinanceManager.Balance;
        var over = engine.StockSmolts("po3_nord", room + 1);
        Assert.False(over.Ok);
        Assert.Equal(CapacityReasons.SiteProjectedPeak, over.ErrorKey);
        Assert.Equal(cash, engine.FinanceManager.Balance);

        var fits = engine.StockSmolts("po3_nord", room);
        Assert.True(fits.Ok, fits.Message);
        var after = engine.PreviewStocking("po3_nord", 0);
        Assert.Equal(0, after.MaxCount);
        Assert.True(after.SiteProjectedPeakTonnes <= 780.0);

        // po3_sor still has room in its locality, until the area's licences run out.
        int sorRoom = engine.MaxStockableSmolts("po3_sor");
        Assert.True(sorRoom > 0);
        var areaFull = engine.PreviewStocking("po3_sor", sorRoom + 1_000);
        Assert.False(areaFull.Allowed);
    }

    [Fact]
    public void AnAreaWithoutLicences_TakesNoFish()
    {
        var engine = Started("po3_red_sone", cash: 50_000_000m);
        engine.State.Zones.Add(new Zone("vest_ny") { ProductionArea = 4 });
        engine.State.LicenseAuctions.Add(new LicenseAuctionState
        {
            Id = "lic_vest_ny", ZoneId = "vest_ny", Status = "won_player", ConsumedByBuild = false
        });
        Assert.True(engine.BuildSeaSite("vest_ny", "vest_anlegg").Ok);   // the zone right, no licence in PO4

        var refused = engine.StockSmolts("vest_anlegg", 1_000);

        Assert.Equal(CapacityReasons.NoLicence, refused.ErrorKey);
        Assert.Equal(0.0, engine.AreaLicenceMtbTonnes(4));
    }

    [Fact]
    public void TheOverview_ShowsEachAreasCapHeadroomAndPlanPeak_AndEachSitesLocality()
    {
        var engine = Started("po3_red_sone");
        engine.Advance(5);
        var overview = engine.GetCapacityOverview();

        Assert.Equal(CapacityRegime.LicencesV1, overview.Regime);
        var area = Assert.Single(overview.Areas);
        Assert.Equal(3, area.ProductionArea);
        Assert.Equal("PO3", area.Code);
        Assert.Equal(2, area.Licences);
        Assert.Equal(1_560.0, area.LicenceMtbTonnes);
        Assert.Equal(engine.AreaStandingBiomassTonnes(3), area.StandingTonnes, 9);
        Assert.Equal(area.LicenceMtbTonnes - area.StandingTonnes, area.HeadroomTonnes, 9);
        Assert.True(area.ProjectedPeakTonnes > area.StandingTonnes && area.ProjectedPeakDay > engine.State.Day);
        // The plan thins the fish at the expected cycle loss (15 % over 540 days):
        // about 0.87 of them stand at harvest weight, not all of them.
        double fish = engine.State.SeaSites.Sum(s => s.Pens.Sum(p => (double)p.TotalCount));
        double allAtHarvest = fish * engine.Calibration.Biology.TargetHarvestWeightKg / 1000.0;
        Assert.InRange(area.ProjectedPeakTonnes, 0.80 * allAtHarvest, 0.95 * allAtHarvest);
        Assert.False(area.BreachOpen);
        Assert.Equal(-1, area.OrderDeadlineDay);
        Assert.Equal(new[] { "po3_nord", "po3_sor" }, overview.Sites.Select(s => s.SiteId));
        Assert.All(overview.Sites, s =>
        {
            Assert.Equal(780.0, s.LocalityMtbTonnes);
            Assert.Equal(1, s.LicencesRegistered);
            Assert.True(s.HeadroomTonnes <= s.LocalityMtbTonnes - s.StandingTonnes + 1e-9);
        });
        Assert.Equal(2, overview.Licences.Count);
        Assert.Empty(overview.Breaches);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LakskongenSim.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
