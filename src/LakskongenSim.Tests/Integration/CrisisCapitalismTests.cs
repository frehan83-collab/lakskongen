using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Events;
using LakskongenSim.Finance;
using Xunit;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// Review F499: a rival's financial distress never opened a crisis
/// (OnCompetitorDistressed had no subscriber), so the distressed-asset market was
/// always empty. Wired up, it also exposed listings that expired four times too
/// fast (a monthly countdown run weekly) and were not saved.
/// </summary>
public class CrisisCapitalismTests
{
    /// <summary>First competitor quarterly review (CompetitorAITickSystem, 90 days).</summary>
    private const int AfterFirstQuarter = 95;

    private static SimulationEngine EngineWithInsolventRival(int seed, out CompetitorCompany rival)
    {
        var engine = new SimulationEngine();
        engine.FinanceManager = new FinanceManager(5_000_000_000m);
        engine.Initialize(enableEvents: false, seed: seed);
        rival = engine.CompetitorAI.Competitors[0];
        rival.CashReserves = -50_000_000_000m; // insolvent at the next quarterly review
        return engine;
    }

    [Fact]
    public void InsolventRival_OpensABankruptcy_AndListsItsAssets()
    {
        var engine = EngineWithInsolventRival(31, out var rival);
        engine.Advance(AfterFirstQuarter);

        var crisis = Assert.Single(engine.CrisisCapitalism.GetActiveCrises(engine.State.Day),
            c => c.AffectedCompetitorId == rival.Id);
        Assert.Equal(CrisisType.BANKRUPTCY, crisis.Type);
        var assets = engine.CrisisCapitalism.GetAllAvailableDistressedAssets()
            .Where(a => a.CrisisEventId == crisis.Id).ToList();
        Assert.InRange(assets.Count, 1, 5);
        Assert.All(assets, a =>
        {
            Assert.Equal(rival.Id, a.SourceCompetitorId);
            Assert.True(a.DiscountedPriceNOK < a.OriginalValueNOK);
            Assert.True(a.ExpiresOnDay > engine.State.Day);
        });
    }

    [Fact]
    public void SameSeed_ListsTheSameAssets()
    {
        static string Listing(int seed)
        {
            var engine = EngineWithInsolventRival(seed, out _);
            engine.Advance(AfterFirstQuarter);
            return string.Join("|", engine.CrisisCapitalism.GetAllAvailableDistressedAssets()
                .Select(a => $"{a.Id}:{a.DiscountedPriceNOK}:{a.ExpiresOnDay}:{a.SiteId}:{a.LicenseCount}"));
        }
        string first = Listing(33);
        Assert.False(string.IsNullOrEmpty(first));
        Assert.Equal(first, Listing(33));
    }

    [Fact]
    public void Listings_RunOnTheCalendar_NotOnTheWeeklyUpdate()
    {
        var engine = EngineWithInsolventRival(34, out _);
        engine.Advance(AfterFirstQuarter);
        var asset = engine.CrisisCapitalism.GetAllAvailableDistressedAssets()
            .OrderByDescending(a => a.ExpiresOnDay).First();
        int months = asset.TimeRemainingMonths;

        engine.Advance(28); // four weekly updates: the old countdown took four months off
        Assert.InRange(asset.TimeRemainingMonths, months - 1, months);
        Assert.Contains(asset, engine.CrisisCapitalism.GetAllAvailableDistressedAssets());

        engine.Advance(asset.ExpiresOnDay - engine.State.Day + 8);
        Assert.DoesNotContain(engine.CrisisCapitalism.GetAllAvailableDistressedAssets(), a => a.Id == asset.Id);
    }

    [Fact]
    public void Listings_SurviveASave_AndTheGameContinuesExactly()
    {
        var engine = EngineWithInsolventRival(35, out _);
        engine.Advance(AfterFirstQuarter);
        var resumed = SaveStateComparer.Resume(SaveStateComparer.Serialize(engine), events: false);

        static string Ids(SimulationEngine e) => string.Join(",",
            e.CrisisCapitalism.GetAllAvailableDistressedAssets().Select(a => $"{a.Id}@{a.ExpiresOnDay}"));
        Assert.NotEmpty(engine.CrisisCapitalism.GetAllAvailableDistressedAssets());
        Assert.Equal(Ids(engine), Ids(resumed));

        engine.Advance(60);
        resumed.Advance(60);
        string a = SaveStateComparer.Normalize(SaveStateComparer.Serialize(engine));
        string b = SaveStateComparer.Normalize(SaveStateComparer.Serialize(resumed));
        Assert.True(a == b, "resumed game diverged at " + SaveStateComparer.FirstDifference(a, b));
    }

    [Fact]
    public void Buying_TransfersExactlyTheAdvertisedAssets_AtTheListedPrice()
    {
        var engine = EngineWithInsolventRival(36, out _);
        engine.Advance(AfterFirstQuarter);
        var asset = engine.CrisisCapitalism.GetAllAvailableDistressedAssets()
            .OrderByDescending(a => a.SiteId != null).ThenBy(a => a.Id).First();
        var seller = engine.CompetitorAI.Competitors.Single(c => c.Id == asset.SourceCompetitorId);
        int sitesBefore = engine.State.SeaSites.Count;
        int licencesBefore = engine.State.Licenses.Count;
        int sellerSitesBefore = seller.SiteCount;
        decimal cashBefore = engine.FinanceManager.Balance;

        var bought = engine.AcquireDistressedAsset(asset.Id);

        Assert.True(bought.Ok, bought.Message);
        Assert.Equal(cashBefore - asset.DiscountedPriceNOK, engine.FinanceManager.Balance);
        if (asset.SiteId != null && asset.MABTonnes.HasValue)
        {
            Assert.Equal(sitesBefore + 1, engine.State.SeaSites.Count);
            Assert.Equal(asset.MABTonnes.Value, engine.State.SeaSites.Single(s => s.Id == asset.SiteId).MABTonnes, 3);
            Assert.Equal(sellerSitesBefore - 1, seller.SiteCount);
        }
        Assert.Equal(licencesBefore + (asset.LicenseCount ?? 0), engine.State.Licenses.Count);
        Assert.DoesNotContain(engine.CrisisCapitalism.GetAllAvailableDistressedAssets(), a => a.Id == asset.Id);
        Assert.Equal("asset_not_found", engine.AcquireDistressedAsset(asset.Id).ErrorKey);

        engine.Advance(1);
        Assert.Equal(0m, engine.UnbookedCashTotal);
    }
}
