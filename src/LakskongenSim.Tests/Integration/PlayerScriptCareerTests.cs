using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using Xunit;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// Career gates for the PlayerScriptProbe (NEXT_LEVEL_PLAN Phase 1 exit:
/// the player path is exercised end to end with events ON, zones ON and a
/// mid-run save/load. Bands here are deliberately wide — the tight bands in
/// docs/BALANCE.md are SimProbe's no-zone measurements; zone oxygen dynamics
/// and stochastic events shift the same metrics in a real career.
/// </summary>
public class PlayerScriptCareerTests
{
    [Fact]
    public void Po3RedZone_CommandDrivenCareer_CompletesInBand()
    {
        var r = PlayerScriptProbe.RunCareer(seed: 7);

        Assert.True(r.SaveLoadRoundTripped, "save/load mid-run did not execute");
        Assert.True(r.HarvestedAllSites, $"not all sites harvested (kg={r.HarvestedKg:N0})");
        Assert.True(r.HarvestedKg > 50_000, $"harvest too small: {r.HarvestedKg:N0} kg");
        Assert.InRange(r.RealizedPricePerKg, 70.0, 96.0);
        Assert.InRange(r.EconomicFcr, 0.9, 2.6);
        // Wide ceiling on purpose: the career deliberately books early
        // treatments (wellboat delouse day ~3) and runs events + red zone,
        // so grow-out mortality runs above the clean SimProbe band; the
        // ceiling only guards against a silent full wipe.
        Assert.InRange(r.SeaMortalityPct, 0.0, 65.0);
        Assert.True(r.EventLogEntries > 5, "events never fired — the events-on path is broken");
        Assert.True(double.IsFinite(r.PeakAdultFemaleLice), "lice metric went non-finite");
        Assert.True(r.PeakAdultFemaleLice < 5.0, $"lice ran away: {r.PeakAdultFemaleLice:F2}/fish");
        Assert.True(r.EndingBalance > -10_000_000m, $"bankrupt at career end: {r.EndingBalance:N0} NOK");

        // The wellboat bookings must have been executed by the engine's daily
        // advance, through the same note ledger the game's bridge projects.
        Assert.Contains("wellboat_rov", r.NoteKinds);
        Assert.Contains("wellboat_delouse", r.NoteKinds);
    }

    [Fact]
    public void Po3RedZone_SecondSeed_CareerAlsoCompletes()
    {
        var r = PlayerScriptProbe.RunCareer(seed: 13);

        Assert.True(r.HarvestedAllSites, $"not all sites harvested (kg={r.HarvestedKg:N0})");
        Assert.True(r.HarvestedKg > 50_000, $"harvest too small: {r.HarvestedKg:N0} kg");
        Assert.InRange(r.RealizedPricePerKg, 70.0, 96.0);
        Assert.InRange(r.EconomicFcr, 0.9, 2.6);
        Assert.True(r.EndingBalance > -10_000_000m, $"bankrupt at career end: {r.EndingBalance:N0} NOK");
    }
}

/// <summary>
/// Error-path contract for the typed command surface: stable error keys the
/// GameBridge can project onto the legacy bool/dictionary surface.
/// </summary>
public class CommandErrorPathTests
{
    private static SimulationEngine NewEngine()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 1);
        return engine;
    }

    private static SimulationEngine NewEngineWithSite(string siteId = "site-1")
    {
        var engine = NewEngine();
        var zone = new Zone("zone-1");
        engine.State.Zones.Add(zone);
        engine.State.SeaSites.Add(new SeaSite(siteId, zone.Id));
        return engine;
    }

    /// <summary>
    /// Review F884: the day-300 save/load round trip used to be checked only by
    /// counts. The resumed career must end in exactly the state of the same
    /// career played without a save.
    /// </summary>
    [Theory]
    [InlineData(7)]
    [InlineData(13)]
    public void Po3RedZone_SaveLoadCareer_EndsExactlyLikeTheUninterruptedCareer(int seed)
    {
        var roundTripped = PlayerScriptProbe.RunCareer(seed, roundTripSaveLoad: true);
        var straight = PlayerScriptProbe.RunCareer(seed, roundTripSaveLoad: false);

        Assert.True(roundTripped.SaveLoadRoundTripped);
        Assert.Equal(straight.DaysRun, roundTripped.DaysRun);
        Assert.True(straight.FinalStateJson == roundTripped.FinalStateJson,
            "resumed career diverged at " +
            SaveStateComparer.FirstDifference(straight.FinalStateJson, roundTripped.FinalStateJson));
        Assert.Equal(straight.HarvestedKg, roundTripped.HarvestedKg);
    }

    [Fact]
    public void StockSmolts_UnknownSite_FailsWithSiteNotFound()
    {
        var engine = NewEngineWithSite();
        var result = engine.StockSmolts("nope", 500, "standard");
        Assert.False(result.Ok);
        Assert.Equal("site_not_found", result.ErrorKey);
    }

    [Fact]
    public void StockSmolts_Unaffordable_FailsWithInsufficientFunds()
    {
        var engine = NewEngineWithSite();
        engine.FinanceManager = new FinanceManager(1m);
        var result = engine.StockSmolts("site-1", 50_000, "standard");
        Assert.False(result.Ok);
        Assert.Equal("insufficient_funds", result.ErrorKey);
    }

    [Fact]
    public void StockSmolts_Success_ChargesMoneyAndAddsFish()
    {
        var engine = NewEngineWithSite();
        decimal before = engine.FinanceManager.Balance;
        var result = engine.StockSmolts("site-1", 500, "standard");
        Assert.True(result.Ok);
        Assert.True(engine.FinanceManager.Balance < before, "stocking was free");
        Assert.True(engine.State.Populations.Count == 1, "population missing after stock");
        Assert.True(engine.State.SeaSites[0].Pens.Count == 1, "pen missing after stock");
    }

    [Fact]
    public void HarvestSite_EmptySite_FailsWithSiteEmpty()
    {
        var engine = NewEngineWithSite();
        var result = engine.HarvestSite("site-1");
        Assert.False(result.Ok);
        Assert.Equal("site_empty", result.ErrorKey);
        Assert.Equal(0.0, result.Value);
    }

    [Fact]
    public void TakeLoan_InvalidParameters_Fails()
    {
        var engine = NewEngine();
        var result = engine.TakeLoan(-5, 12, 0.05);
        Assert.False(result.Ok);
        Assert.Equal("invalid_loan_params", result.ErrorKey);
    }

    [Fact]
    public void TakeLoan_BooksLiabilityAndLiquidity()
    {
        var engine = NewEngine();
        decimal before = engine.FinanceManager.Balance;
        var result = engine.TakeLoan(1_000_000, 24, 0.06);
        Assert.True(result.Ok);
        Assert.True(engine.FinanceManager.Balance == before + 1_000_000m, "principal never landed");
        Assert.True(engine.State.ActiveLoans.Count == 1, "liability not recorded");
        Assert.True(engine.State.ActiveLoans[0].Principal == 1_000_000m);
        Assert.Equal(result.Id, engine.State.ActiveLoans[0].Id);
    }

    [Fact]
    public void SetInsurancePolicy_UnknownKind_FailsWithUnknownKind()
    {
        var engine = NewEngine();
        var result = engine.SetInsurancePolicy("volcano", true, 1.0, 0);
        Assert.False(result.Ok);
        Assert.Equal("unknown_kind", result.ErrorKey);
    }

    [Fact]
    public void SetInsurancePolicy_ReplacesOnlyTheRequestedKind()
    {
        var engine = NewEngine();
        Assert.True(engine.SetInsurancePolicy("mortality", true, 1.0, 250_000).Ok);
        Assert.True(engine.SetInsurancePolicy("storm", true, 0.5, 150_000).Ok);
        Assert.True(engine.SetInsurancePolicy("mortality", false, 1.0, 0).Ok);

        Assert.False(engine.Insurance.Policies.Any(p => p.Kind == InsuranceKind.Mortality));
        Assert.True(engine.Insurance.Policies.Count(p => p.Kind == InsuranceKind.Storm) == 1,
            "storm policy should survive the mortality rebuild");
        Assert.True(engine.Insurance.Policies[0].CoverageFraction == 0.5, "storm coverage altered");
    }
}