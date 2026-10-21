#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Environment.Geo;
using LakskongenSim.Finance;
using LakskongenSim.Tests.Geo;
using StateHashProbe;
using Xunit;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// The geo reads are pure (STAGE23_PLAN track A, A3; §5 "golden traces
/// turning red by accident"): the rival layout, the summaries, the zone
/// anchors and the 3D order draw no random number, register no stream, write
/// nothing to the save, and build the layout once per roster and anchor
/// signature however often the map or the fjord asks.
/// </summary>
public class GeoReadPurityTests
{
    private static void ReadEverything(SimulationEngine engine)
    {
        engine.GetRivalLayout();
        engine.RivalLayoutSignature();
        engine.CompetitorsByHomePresence();
        for (int po = 0; po <= 14; po++)
            engine.GetRivalSummary(po);
        engine.GetZoneAnchors();
        engine.GeoBindingOf("hav_1");
    }

    [Fact]
    public void AHundredReads_BuildTheLayoutOnce_AndChangeNoStreamSaveOrDayHash()
    {
        var engine = GeoPlotEngineTests.Started("po3_red_sone");
        engine.Advance(3);
        string save = SaveStateComparer.Serialize(engine);
        var streams = engine.Rng.CaptureStates();
        string day = GoldenTraceState.Capture(engine).DayHash();
        Assert.Equal(0, engine.RivalLayoutBuilds);

        for (int i = 0; i < 100; i++)
            ReadEverything(engine);

        Assert.Equal(1, engine.RivalLayoutBuilds);
        Assert.Equal(streams.Keys.OrderBy(k => k, StringComparer.Ordinal), engine.Rng.CaptureStates().Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(streams, engine.Rng.CaptureStates());
        Assert.Equal(day, GoldenTraceState.Capture(engine).DayHash());
        Assert.Equal(save, SaveStateComparer.Serialize(engine));
    }

    [Fact]
    public void TheLayout_IsBuiltAgainOnlyWhenItsSignatureChanges()
    {
        var engine = GeoPlotEngineTests.Started("ressursrente");
        var first = engine.GetRivalLayout();
        Assert.Same(first, engine.GetRivalLayout());
        Assert.Equal(first.Signature, engine.RivalLayoutSignature());
        // A licence win adds MAB: a new signature and one more build.
        engine.CompetitorAI.Competitors[0].TotalMABTonnes += 780f;
        Assert.NotEqual(first.Signature, engine.RivalLayoutSignature());
        Assert.Equal(1, engine.RivalLayoutBuilds);
        var second = engine.GetRivalLayout();
        Assert.NotSame(first, second);
        Assert.Equal(2, engine.RivalLayoutBuilds);
        Assert.Same(second, engine.GetRivalLayout());
        Assert.Equal(2, engine.RivalLayoutBuilds);
    }

    [Fact]
    public void TheGamesOwnAnchors_AreKeptOutOfTheRivalLayout()
    {
        var engine = GeoPlotEngineTests.Started("isa_utbrudd");
        var layout = engine.GetRivalLayout();
        var anchors = engine.GetZoneAnchors().Where(a => a.InGame).ToArray();
        Assert.Equal(new[] { "isa_kjerne", "isa_nord", "isa_sor" }, anchors.Select(a => a.ZoneId));
        Assert.All(anchors, a => Assert.Equal(-1, layout.OwnerIndexOf(a.LocalityId)));
        Assert.All(anchors, a => Assert.Equal(ScenarioGeoAnchors.AuthoredSource, a.Source));
        // The catalogue zones follow, one per area.
        var catalogue = engine.GetZoneAnchors().Where(a => !a.InGame).ToArray();
        Assert.Equal(13, catalogue.Length);
        Assert.Equal(Enumerable.Range(1, 13), catalogue.Select(a => a.ProductionArea));
    }

    [Fact]
    public void TheLayout_IsTheSameForEverySeed_AndAfterALoad()
    {
        var layouts = new[] { 1, 7, 13 }.Select(seed => GeoPlotEngineTests.Started("forste_sesong", seed).GetRivalLayout()).ToArray();
        Assert.All(layouts, l => Assert.Equal(layouts[0].Signature, l.Signature));
        foreach (var l in layouts.Skip(1))
            foreach (var locality in GeoLocalities.Default.All)
                Assert.Equal(layouts[0].OwnerOf(locality.Id), l.OwnerOf(locality.Id));
        var engine = GeoPlotEngineTests.Started("forste_sesong", 7);
        var resumed = SaveStateComparer.Resume(SaveStateComparer.Serialize(engine), events: false);
        Assert.Equal(engine.GetRivalLayout().Signature, resumed.GetRivalLayout().Signature);
    }

    [Fact]
    public void TheThreeDOrder_IsByHomeAreaSitesThenRosterOrder_TheSameSixCompanies()
    {
        foreach (var (scenario, home) in new[] { ("po3_red_sone", 3), ("laksebaronene", 6), ("lofoten_eventyr", 9), ("krise_manager", 12) })
        {
            var engine = GeoPlotEngineTests.Started(scenario);
            var ordered = engine.CompetitorsByHomePresence();
            var roster = engine.CompetitorAI.Competitors;
            Assert.Equal(roster.Select(c => c.Id).OrderBy(i => i), ordered.Select(p => p.Company.Id).OrderBy(i => i));
            for (int i = 1; i < ordered.Count; i++)
            {
                Assert.True(ordered[i - 1].HomePoSites >= ordered[i].HomePoSites);
                if (ordered[i - 1].HomePoSites == ordered[i].HomePoSites)
                    Assert.True(IndexIn(roster, ordered[i - 1].Company) < IndexIn(roster, ordered[i].Company));
            }
            // Recount from the layout.
            var layout = engine.GetRivalLayout();
            foreach (var p in ordered)
                Assert.Equal(layout.SitesOf(layout.IndexOf(p.Company.Id)).Count(id => GeoLocalities.Default.TryGet(id, out var l) && l.Po == home && l.IsSeaGrowoutProducing),
                    p.HomePoSites);
        }
        Assert.Equal("salmax", GeoPlotEngineTests.Started("laksebaronene").CompetitorsByHomePresence()[0].Company.Id);
        Assert.Equal("nordlaks_nord", GeoPlotEngineTests.Started("lofoten_eventyr").CompetitorsByHomePresence()[0].Company.Id);
    }

    private static int IndexIn(IReadOnlyList<CompetitorCompany> roster, CompetitorCompany c)
    {
        for (int i = 0; i < roster.Count; i++)
            if (roster[i].Id == c.Id) return i;
        return -1;
    }

    [Fact]
    public void TheRivalSummary_CountsTheAreasLocalities_AndItsTopOwners()
    {
        var engine = GeoPlotEngineTests.Started("po3_red_sone");
        var summary = engine.GetRivalSummary(3);
        Assert.Equal(GeoLocalities.Default.All.Count(l => l.Po == 3), summary.SitesByKind.Sum(kv => kv.Value));
        Assert.Equal(GeoLocalities.Default.All.Where(l => l.Po == 3).Select(l => l.Kind).Distinct().OrderBy(k => k, StringComparer.Ordinal),
            summary.SitesByKind.Select(kv => kv.Key));
        Assert.Equal(GeoLocalities.Default.SeaGrowoutProducing.Where(l => l.Po == 3).Sum(l => l.MtbT ?? 0.0), summary.SeaGrowoutMtbT, 6);
        Assert.InRange(summary.TopOwners.Count, 2, 5);
        for (int i = 1; i < summary.TopOwners.Count; i++)
            Assert.True(summary.TopOwners[i - 1].Sites >= summary.TopOwners[i].Sites);
        Assert.All(summary.TopOwners, o => Assert.False(GeoTestData.HasBrand(o.Name)));
        Assert.Empty(engine.GetRivalSummary(0).TopOwners);
        Assert.Empty(engine.GetRivalSummary(14).SitesByKind);
    }
}
