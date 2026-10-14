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
        // Roadmap D2 (replaces the absolute 70-96 NOK/kg band, which F1024's
        // faster growth broke by moving the sale into a market trough the
        // player did not cause): the player sells by the probe's harvest rule
        // (from 4.5 kg on a day spot >= its 30-day mean, forced at 6 kg), and
        // the sale must realize the spot of the day it was made.
        Assert.All(r.HarvestWeightsKg, w => Assert.InRange(w, PlayerScriptProbe.HarvestTriggerWeightKg,
            PlayerScriptProbe.ForcedHarvestWeightKg + 0.1));
        Assert.True(r.RealizedPricePerKg >= 0.95 * r.MeanSpotOnHarvestDaysNok,
            $"realized {r.RealizedPricePerKg:F2} NOK/kg < 0.95 x spot on the harvest days {r.MeanSpotOnHarvestDaysNok:F2}");
        // The rule keeps the sale out of the trough a fixed 4.5 kg trigger sold
        // into (48.6 NOK/kg against a career mean of ~80; measured 0.94 x mean).
        Assert.True(r.MeanSpotOnHarvestDaysNok >= 0.9 * r.MeanSpotOverCareerNok,
            $"sold at {r.MeanSpotOnHarvestDaysNok:F2} NOK/kg, career mean spot {r.MeanSpotOverCareerNok:F2}");
        // FCR in kg feed per kg gain (D2 band 1.0-1.6; was 0.9-2.6).
        Assert.InRange(r.EconomicFcr, 1.0, 1.6);
        // Wide ceiling on purpose: the career deliberately books early
        // treatments (wellboat delouse day ~3) and runs events + red zone,
        // so grow-out mortality runs above the clean SimProbe band; the
        // ceiling only guards against a silent full wipe.
        Assert.InRange(r.SeaMortalityPct, 0.0, 65.0);
        Assert.True(r.EventLogEntries > 5, "events never fired — the events-on path is broken");
        Assert.True(double.IsFinite(r.PeakAdultFemaleLice), "lice metric went non-finite");
        Assert.True(r.PeakAdultFemaleLice < 5.0, $"lice ran away: {r.PeakAdultFemaleLice:F2}/fish");
        // Solvent by the game's own rule (GameBridge ends the game below -1M NOK).
        // Strengthened from -10M once the ledger stopped waiving unpaid feed.
        Assert.True(r.EndingBalance > -1_000_000m, $"bankrupt at career end: {r.EndingBalance:N0} NOK");

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
        // Seed 13 is a disaster career (~61% sea mortality). Its old -10M bound
        // held only while unpaid feed was waived (~18-20M NOK, review F1130) and
        // starting pens were immune to disease (F1072); with an honest ledger it
        // ends near -10.6M. This bound guards against runaway debt, not losses:
        // see RUN_LOG §8 dispute "seed-13 career bound".
        Assert.True(r.EndingBalance > -15_000_000m, $"runaway debt at career end: {r.EndingBalance:N0} NOK");
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
    /// Review F884: the mid-run save/load round trip (day 210 since S1-01, while
    /// both sites hold fish) used to be checked only by counts. The resumed
    /// career must end in exactly the state of the same career played without a save.
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