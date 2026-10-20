#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LakskongenSim.Core;
using LakskongenSim.Environment.Geo;
using LakskongenSim.Finance;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Rivals;

/// <summary>
/// The static rival layout over the real salmonid localities (STAGE23_PLAN
/// track A, A3; BALANCE §1ab): the pool, each major's MTB share drafted
/// round-robin with its home areas first, locality 45196 to Offshore
/// Optimists, independents grouped by (area, municipality) in sixes, and a
/// layout that is a pure function of the roster and the excluded anchors.
/// </summary>
public class RivalLayoutTests
{
    private readonly ITestOutputHelper _output;

    public RivalLayoutTests(ITestOutputHelper output) => _output = output;

    private static GeoLocalities Localities => GeoLocalities.Default;

    /// <summary>The game's starting roster (no RNG is drawn to build it).</summary>
    private static IReadOnlyList<CompetitorCompany> Roster() => new CompetitorAISystem(7).Competitors.Select(c => c.Clone()).ToArray();

    private static readonly IReadOnlySet<int> NoAnchors = new HashSet<int>();

    private static IEnumerable<GeoLocality> Pool(IReadOnlySet<int> excluded) =>
        Localities.SeaGrowoutProducing
            .Concat(Localities.Salmonid.Where(l => l.Kind == RivalLayout.LandSalmonidKind))
            .Concat(Localities.All.Where(l => l.Id == RivalLayout.OffshoreLocalityId))
            .Where(l => !excluded.Contains(l.Id));

    /// <summary>A process-stable digest of the whole layout: owner ids and names by locality.</summary>
    private static string Digest(RivalLayoutResult layout)
    {
        var text = new StringBuilder();
        foreach (var l in Localities.All)
        {
            var owner = layout.OwnerOf(l.Id);
            if (owner != null) text.Append(l.Id).Append('=').Append(owner.Id).Append('/').Append(owner.Name).Append('\n');
        }
        return StableHash.Of(text.ToString()).ToString("x16", CultureInfo.InvariantCulture);
    }

    [Fact]
    public void EveryPoolLocality_HasExactlyOneOwner_AndNothingElseHasOne()
    {
        var layout = RivalLayout.Build(Roster(), NoAnchors);
        var pool = Pool(NoAnchors).Select(l => l.Id).ToHashSet();
        Assert.Equal(994 + 309 + 1, pool.Count);
        Assert.Equal(pool.Count, layout.OwnedCount);
        foreach (var l in Localities.All)
            Assert.Equal(pool.Contains(l.Id), layout.OwnerIndexOf(l.Id) >= 0);
        // Each owner's site list partitions the pool.
        var listed = Enumerable.Range(0, layout.Owners.Count).SelectMany(layout.SitesOf).ToArray();
        Assert.Equal(pool.Count, listed.Length);
        Assert.Equal(pool.OrderBy(i => i), listed.OrderBy(i => i));
        Assert.All(Enumerable.Range(0, layout.Owners.Count), o => Assert.All(layout.SitesOf(o), id => Assert.Equal(o, layout.OwnerIndexOf(id))));
    }

    [Fact]
    public void ExcludedAnchors_HaveNoOwner()
    {
        var anchors = new HashSet<int> { 12128, 20115, 18217 };
        var layout = RivalLayout.Build(Roster(), anchors);
        Assert.All(anchors, id => Assert.Equal(-1, layout.OwnerIndexOf(id)));
        Assert.Equal(Pool(anchors).Count(), layout.OwnedCount);
    }

    [Fact]
    public void TheOffshoreLocality_GoesToOffshoreOptimists_OrTheLargestMajorAfterAMerger()
    {
        var roster = Roster();
        var layout = RivalLayout.Build(roster, NoAnchors);
        Assert.Equal(RivalLayout.OffshoreOwnerId, layout.OwnerOf(RivalLayout.OffshoreLocalityId)!.Id);

        var merged = roster.Where(c => c.Id != RivalLayout.OffshoreOwnerId).ToArray();
        var after = RivalLayout.Build(merged, NoAnchors);
        string largest = merged.OrderByDescending(c => c.TotalMABTonnes).First().Id;
        Assert.Equal("mowier", largest);
        Assert.Equal(largest, after.OwnerOf(RivalLayout.OffshoreLocalityId)!.Id);
        // Every pool locality still has one owner after the merger.
        Assert.Equal(Pool(NoAnchors).Count(), after.OwnedCount);
        Assert.DoesNotContain(after.Owners, o => o.Id == RivalLayout.OffshoreOwnerId);
    }

    [Fact]
    public void EachMajor_HoldsItsMtbShareOfSeaAndLandSites_WithAHomeAreaSite()
    {
        var roster = Roster();
        var layout = RivalLayout.Build(roster, NoAnchors);
        foreach (var c in roster)
        {
            int index = layout.IndexOf(c.Id);
            var sites = layout.SitesOf(index).Select(id => { Localities.TryGet(id, out var l); return l!; }).ToArray();
            int sea = sites.Count(l => l.IsSeaGrowoutProducing);
            int land = sites.Count(l => l.Kind == RivalLayout.LandSalmonidKind);
            int seaShare = (int)Math.Round(c.TotalMABTonnes / 1_250_000.0 * 994, MidpointRounding.AwayFromZero);
            int landShare = (int)Math.Round(c.TotalMABTonnes / 1_250_000.0 * 309, MidpointRounding.AwayFromZero);
            Assert.InRange(sea, seaShare - 1, seaShare + 1);
            Assert.InRange(land, landShare - 1, landShare + 1);
            Assert.Equal(RivalLayout.ShareOf(c, 994), seaShare);
            var home = RivalLayout.HomeAreasOf(c);
            Assert.NotEmpty(home);
            Assert.Contains(sites, l => l.IsSeaGrowoutProducing && home.Contains(l.Po));
            _output.WriteLine($"{c.Id,-20} sea {sea,3} (share {seaShare,3}) land {land,3} (share {landShare,3}) home {string.Join("/", home)}: "
                + string.Join(" ", Enumerable.Range(1, 13).Select(po => $"PO{po}:{sites.Count(l => l.Po == po && l.IsSeaGrowoutProducing)}")));
        }
        Assert.Equal(new[] { 3, 4 }, RivalLayout.HomeAreasOf(roster.First(c => c.Id == "mowier")));
        Assert.Equal(new[] { 6 }, RivalLayout.HomeAreasOf(roster.First(c => c.Id == "salmax")));
        Assert.Equal(new[] { 6 }, RivalLayout.HomeAreasOf(roster.First(c => c.Id == RivalLayout.OffshoreOwnerId)));
        Assert.Equal(new[] { 9 }, RivalLayout.HomeAreasOf(roster.First(c => c.Id == "nordlaks_nord")));
    }

    [Fact]
    public void EveryAreaWithSites_HasAtLeastTwoOwners()
    {
        var layout = RivalLayout.Build(Roster(), NoAnchors);
        for (int po = 1; po <= 13; po++)
        {
            var owners = Pool(NoAnchors).Where(l => l.Po == po).Select(l => layout.OwnerIndexOf(l.Id)).Distinct().ToArray();
            Assert.True(owners.Length >= 2, $"PO{po} has {owners.Length} owner(s)");
            int independents = owners.Count(o => !layout.Owners[o].IsMajor);
            _output.WriteLine($"PO{po,-2} sites {Pool(NoAnchors).Count(l => l.Po == po),4} owners {owners.Length,3} (independents {independents,3})");
        }
    }

    [Fact]
    public void Independents_HoldAtMostSixSites_InOneAreaAndMunicipality_NumberedPerArea()
    {
        var layout = RivalLayout.Build(Roster(), NoAnchors);
        var id = new Regex(@"^ind_(\d{2})_(\d{3})$");
        var numbers = new Dictionary<int, List<int>>();
        for (int o = 0; o < layout.Owners.Count; o++)
        {
            var owner = layout.Owners[o];
            if (owner.IsMajor) continue;
            var m = id.Match(owner.Id);
            Assert.True(m.Success, owner.Id);
            int po = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            int number = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            var sites = layout.SitesOf(o).Select(s => { Localities.TryGet(s, out var l); return l!; }).ToArray();
            Assert.InRange(sites.Length, 1, 6);   // plan A3: at most 6 sites per independent
            Assert.Equal(6, RivalLayout.MaxSitesPerIndependent);
            Assert.Single(sites.Select(l => (l.Po, l.Municipality)).Distinct());
            Assert.Equal(po, sites[0].Po);
            Assert.Equal(ParodyNames.Of(po, number - 1), owner.Name);
            (numbers.TryGetValue(po, out var list) ? list : numbers[po] = new List<int>()).Add(number);
        }
        foreach (var (po, list) in numbers)
            Assert.Equal(Enumerable.Range(1, list.Count), list);
        // A municipality's sites split evenly: no group is two or more smaller than another of its municipality.
        foreach (var group in Enumerable.Range(0, layout.Owners.Count).Where(o => !layout.Owners[o].IsMajor)
                     .GroupBy(o => { Localities.TryGet(layout.SitesOf(o)[0], out var l); return (l!.Po, l.Municipality); }))
        {
            var sizes = group.Select(o => layout.SitesOf(o).Count).ToArray();
            Assert.True(sizes.Max() - sizes.Min() <= 1, $"{group.Key}: {string.Join(",", sizes)}");
        }
        _output.WriteLine($"{layout.Owners.Count(o => !o.IsMajor)} independents; per area: "
            + string.Join(" ", numbers.OrderBy(kv => kv.Key).Select(kv => $"PO{kv.Key}:{kv.Value.Count}")));
    }

    [Fact]
    public void TheLayout_IsAPureFunctionOfTheRosterAndAnchors_AcrossSeedsAndProcesses()
    {
        var anchors = new HashSet<int> { 12128 };
        string first = Digest(RivalLayout.Build(Roster(), anchors));
        Assert.Equal(first, Digest(RivalLayout.Build(Roster(), anchors)));
        foreach (int seed in new[] { 1, 7, 13 })
        {
            var roster = new CompetitorAISystem(seed).Competitors.Select(c => c.Clone()).ToArray();
            Assert.Equal(first, Digest(RivalLayout.Build(roster, anchors)));
        }
        // Pinned: a different process (or platform) must deal the same layout.
        _output.WriteLine($"digest {first}");
        Assert.Equal(PinnedDigest, first);
    }

    /// <summary>The layout digest for the starting roster with Første sesong's anchor excluded.</summary>
    private const string PinnedDigest = "96b964c47ae4d738";

    [Fact]
    public void TheSignature_FollowsIdsTonnesAndAnchors()
    {
        var roster = Roster();
        string a = RivalLayout.SignatureOf(roster, new HashSet<int>());
        Assert.Equal(a, RivalLayout.SignatureOf(Roster(), new HashSet<int>()));
        Assert.NotEqual(a, RivalLayout.SignatureOf(roster, new HashSet<int> { 12128 }));
        Assert.NotEqual(a, RivalLayout.SignatureOf(roster.Skip(1).ToArray(), new HashSet<int>()));
        var grown = Roster();
        grown[0].TotalMABTonnes += 780f;
        Assert.NotEqual(a, RivalLayout.SignatureOf(grown, new HashSet<int>()));
        // Anchor order does not matter.
        Assert.Equal(RivalLayout.SignatureOf(roster, new HashSet<int> { 1, 2 }), RivalLayout.SignatureOf(roster, new List<int> { 2, 1, 2 }));
    }

    [Fact]
    public void AMajorThatGrows_KeepsItsSites_AndOnlyTakesMore()
    {
        var before = RivalLayout.Build(Roster(), NoAnchors);
        var grown = Roster();
        grown[0].TotalMABTonnes += 5_000f;   // +4 sea sites, +1 land site
        var after = RivalLayout.Build(grown, NoAnchors);
        var had = before.SitesOf(0).ToHashSet();
        var has = after.SitesOf(0).ToHashSet();
        Assert.Subset(has, had);
        Assert.InRange(has.Count - had.Count, 4, 6);
        // The other majors keep exactly their sites.
        for (int m = 1; m < grown.Count; m++)
            Assert.Equal(before.SitesOf(m), after.SitesOf(m));
    }

    [Fact]
    public void Owners_AreTheMajorsInRosterOrder_ThenUniqueIndependents()
    {
        var roster = Roster();
        var layout = RivalLayout.Build(roster, NoAnchors);
        Assert.Equal(roster.Select(c => c.Id), layout.Owners.Take(roster.Count).Select(o => o.Id));
        Assert.Equal(roster.Select(c => c.Name), layout.Owners.Take(roster.Count).Select(o => o.Name));
        Assert.All(layout.Owners.Take(roster.Count), o => Assert.True(o.IsMajor));
        Assert.All(layout.Owners.Skip(roster.Count), o => Assert.False(o.IsMajor));
        Assert.Equal(layout.Owners.Count, layout.Owners.Select(o => o.Id).Distinct().Count());
        Assert.Equal(layout.Owners.Count, layout.Owners.Select(o => o.Name).Distinct().Count());
    }
}
