using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Calibration;
using LakskongenSim.Core;
using LakskongenSim.Farm;
using LakskongenSim.Farm.Land;
using LakskongenSim.Finance;
using Xunit;

namespace LakskongenSim.Tests.FacilityFinance;

/// <summary>
/// F2-14 (docs/FARM_TYPES_SPEC.md §7.1 F2-14 acceptance 1-3, 7, 8): a land site
/// is applied for as a package of permit cases, each refused at the quoted
/// probability (its drivers: the base, the pad's nature sensitivity, the
/// plant's scale, the mitigations), decided on its own pre-drawn day; a
/// refusal can be appealed (180 days) or applied for again with a
/// mitigation; the moratorium flag refuses new applications; grid projects
/// queue 365-1,095 days and count only when finished.
/// </summary>
public class LandPermitTests
{
    /// <summary>
    /// A game the land family is open in: land-based-ras researched, a first
    /// harvest sold and (unless <paramref name="module"/> is false) an operating
    /// post-smolt module on pad_delta, the HFS card's gates but the zoned pad.
    /// </summary>
    internal static SimulationEngine LandReadyEngine(int seed, decimal cash = 5_000_000_000m, bool module = true)
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: seed);
        engine.ReceiveLoan(cash);
        engine.Research.ForceUnlock("land-based-ras");
        engine.State.HarvestBatchesSold = 1;
        if (module) Assert.True(engine.CommissionLandSite("pad_delta", LandTemplate.PostSmoltModule).Ok);
        return engine;
    }

    /// <summary>The land section with every refusal probability at zero (permits are then granted on their day).</summary>
    internal static void GrantEverything(SimulationEngine engine)
    {
        var p = engine.LandCalibration.Permits with
        {
            ZoningRefusalBase = 0, ZoningRefusalPerSensitivity = 0, LandAquacultureRefusalBase = 0,
            LandAquacultureRefusalPerSensitivity = 0, DischargeRefusalBase = 0, DischargeRefusalPerSensitivity = 0,
            SeaWorksRefusalBase = 0, SeaWorksRefusalPerSensitivity = 0, BiosecurityRefusalBase = 0,
        };
        engine.UseFacilitiesCalibration(engine.Calibration.Facilities with { Land = engine.LandCalibration with { Permits = p } });
    }

    private static LandPermitCalibration Permits => LandCalibration.Default.Permits;

    // ── Acceptance 1: the package, its cases and their fees ───────────────

    [Fact]
    public void ApplyingOnAnUnzonedPad_FilesZoningLandDischargeSeaWorksAndBiosecurity_AtTheirQuotedOdds()
    {
        var engine = LandReadyEngine(61);
        var quoted = engine.QuoteLandPermits("pad_a", LandTemplate.HfsPilotLot);
        Assert.Equal(LandPermitSystem.SiteKinds, quoted.Select(l => l.Kind));
        Assert.All(quoted, l => Assert.Equal("needed", l.Status));
        decimal cash = engine.FinanceManager.Balance;

        var applied = engine.ApplyLandPermits("pad_a", LandTemplate.HfsPilotLot);
        Assert.True(applied.Ok, applied.Message);
        var cases = engine.PermitCases.ToList();
        Assert.Equal(LandPermitSystem.SiteKinds, cases.Select(c => c.Kind));
        Assert.All(cases, c => Assert.Equal(("pad_a", PermitStatus.Submitted), (c.PlotId, c.Status)));
        // The probability shown is the probability decided, case by case.
        foreach (var line in quoted)
        {
            var c = cases.Single(k => k.Kind == line.Kind);
            Assert.Equal(line.Quote.RefusalProbability, c.RefusalProbability);
            Assert.InRange(c.DecisionDay - c.SubmittedDay, line.Quote.MinDays, line.Quote.MaxDays);
            Assert.Equal(line.Quote.RefusalProbability, line.Quote.Drivers.Sum(d => d.Effect), 12);
        }
        // Zoning takes 1-2 years; the sea works permit costs NOK 44,300 [L:R12]; the land permit is fee-free [L:L1].
        var zoning = cases.Single(c => c.Kind == PermitKind.Zoning);
        Assert.InRange(zoning.DecisionDay - zoning.SubmittedDay, 365, 730);
        Assert.Equal(cash - 44_300m, engine.FinanceManager.Balance);
        Assert.Equal(1.0 - quoted.Aggregate(1.0, (q, l) => q * (1.0 - l.Quote.RefusalProbability)), applied.Value, 12);
        // The pilot's permits cover its own production only.
        double pilotTonnes = engine.LandCalibration.HfsPilot.NameplateTonnesHogPerYear;
        Assert.Equal(pilotTonnes, cases.Single(c => c.Kind == PermitKind.LandAquaculture).Conditions[LandPermitSystem.MaxProductionKey]);

        // A second package while these are pending is refused, and changes nothing.
        int count = engine.PermitCases.Count;
        Assert.Equal(LandSiteReasons.AlreadyPending, engine.ApplyLandPermits("pad_a", LandTemplate.HfsPilotLot).ErrorKey);
        Assert.Equal(count, engine.PermitCases.Count);
    }

    [Fact]
    public void APreZonedPad_SkipsZoning_AndEachFailCodeRefusesBeforeAnyChange()
    {
        var engine = LandReadyEngine(62, module: false);
        var applied = engine.ApplyLandPermits("pad_delta", LandTemplate.PostSmoltModule, lots: 2);
        Assert.True(applied.Ok, applied.Message);
        Assert.DoesNotContain(engine.PermitCases, c => c.Kind == PermitKind.Zoning);
        Assert.Equal("pre_zoned", engine.QuoteLandPermits("pad_delta", LandTemplate.PostSmoltModule).Single(l => l.Kind == PermitKind.Zoning).Status);

        int cases = engine.PermitCases.Count;
        Assert.Equal(LandSiteReasons.NotFound, engine.ApplyLandPermits("pad_x", LandTemplate.HfsLot).ErrorKey);
        Assert.Equal(LandSiteReasons.NotFound, engine.ApplyLandPermits("off_1", LandTemplate.HfsLot).ErrorKey);
        Assert.Equal(LandSiteReasons.OutOfRange, engine.ApplyLandPermits("pad_a", LandTemplate.HfsLot, 0).ErrorKey);
        Assert.Equal(LandSiteReasons.OutOfRange, engine.ApplyLandPermits("pad_a", LandTemplate.HfsLot, 1, new[] { "bribe" }).ErrorKey);
        Assert.Equal(LandSiteReasons.LotsExhausted, engine.ApplyLandPermits("pad_a", LandTemplate.HfsLot, 5).ErrorKey);
        Assert.Equal(LandSiteReasons.LotsExhausted, engine.ApplyLandPermits("pad_delta", LandTemplate.HfsLot).ErrorKey);
        Assert.Equal(cases, engine.PermitCases.Count);

        var locked = new SimulationEngine();
        locked.Initialize(enableEvents: false, seed: 63);
        Assert.Equal(LandSiteReasons.NotUnlocked, locked.ApplyLandPermits("pad_a", LandTemplate.HfsLot).ErrorKey);
        // Zoning alone needs no research: the municipality's plan does not wait for it.
        Assert.True(locked.ApplyForZoning("pad_a").Ok);
        Assert.Equal(LandSiteReasons.AlreadyPending, locked.ApplyForZoning("pad_a").ErrorKey);
        Assert.Equal(LandSiteReasons.NothingToApply, locked.ApplyForZoning("pad_delta").ErrorKey);

        // A pad another unit holds.
        var other = LandReadyEngine(64);
        Assert.True(other.CommissionLandSite("pad_b", LandTemplate.HfsLot).Ok);
        other.FindPlot("pad_b")!.OwnerUnitId = "rival:nordlaks";
        Assert.Equal(LandSiteReasons.PlotTaken, other.ApplyLandPermits("pad_b", LandTemplate.HfsLot).ErrorKey);
    }

    // ── Acceptance 2: deterministic decisions, sensitivity, mitigations, appeals ─

    [Fact]
    public void Decisions_AreDeterministicPerSeed_AndFallOnTheirDrawnDays()
    {
        PermitCase[] Run(int seed)
        {
            var engine = LandReadyEngine(seed);
            Assert.True(engine.ApplyLandPermits("pad_b", LandTemplate.HfsLot).Ok);
            engine.Advance(731);
            return engine.PermitCases.ToArray();
        }
        var a = Run(65);
        var b = Run(65);
        Assert.Equal(a.Select(c => (c.Id, c.Status, c.DecisionDay, c.DecidedDay, c.OutcomeDraw)),
            b.Select(c => (c.Id, c.Status, c.DecisionDay, c.DecidedDay, c.OutcomeDraw)));
        Assert.All(a, c => Assert.Equal(c.DecisionDay, c.DecidedDay));
        Assert.All(a, c => Assert.Equal(c.OutcomeDraw < c.RefusalProbability ? PermitStatus.Refused : PermitStatus.Approved, c.Status));
    }

    [Fact]
    public void NatureSensitivity06_RefusesAtLeastThreeTimesAsOftenAs01()
    {
        // The quoted odds: every sensitive kind and the package.
        foreach (var kind in new[] { PermitKind.Zoning, PermitKind.LandAquaculture, PermitKind.Discharge, PermitKind.SeaWorks })
        {
            double low = LandPermitSystem.RefusalProbability(kind, 0.1, 1.0, null, Permits).Probability;
            double high = LandPermitSystem.RefusalProbability(kind, 0.6, 1.0, null, Permits).Probability;
            Assert.True(high >= 3.0 * low, $"{kind}: {low:F3} → {high:F3}");
        }
        double Package(double s) => 1.0 - LandPermitSystem.SiteKinds.Aggregate(1.0,
            (q, k) => q * (1.0 - LandPermitSystem.RefusalProbability(k, s, 1.0, null, Permits).Probability));
        Assert.True(Package(0.6) >= 3.0 * Package(0.1), $"{Package(0.1):F3} → {Package(0.6):F3}");

        // The engine's decisions: 400 zoning and discharge cases on a 0.1 and a 0.6 plot.
        double Rate(double sensitivity, int seed)
        {
            var engine = LandReadyEngine(seed);
            int refused = 0, total = 0;
            foreach (var kind in new[] { PermitKind.Zoning, PermitKind.Discharge })
            {
                var quote = LandPermitSystem.Quote(kind, sensitivity, 1.0, null, engine.LandCalibration.Permits);
                for (int i = 0; i < 200; i++)
                {
                    var filed = engine.SubmitPermitCase(kind, "pad_b", $"u{i}", quote.Terms);
                    Assert.True(filed.Ok, filed.Message);
                    var c = engine.PermitCases.Single(k => k.Id == filed.Id);
                    total++;
                    if (c.WillBeRefused) refused++;
                }
            }
            return (double)refused / total;
        }
        double rateLow = Rate(0.1, 66), rateHigh = Rate(0.6, 67);
        Assert.True(rateHigh >= 3.0 * rateLow, $"refused {rateLow:P1} at 0.1, {rateHigh:P1} at 0.6");
    }

    [Fact]
    public void Mitigations_CutTheRefusal_ByTheirRegistryAmounts_AndTheDriversSayHow()
    {
        var (bare, _) = LandPermitSystem.RefusalProbability(PermitKind.Discharge, 0.6, 1.0, null, Permits);
        var (surveyed, drivers) = LandPermitSystem.RefusalProbability(PermitKind.Discharge, 0.6, 1.0,
            new[] { LandPermitSystem.EnvironmentalSurvey }, Permits);
        Assert.Equal(bare * (1.0 - Permits.SurveyRefusalCut), surveyed, 12);
        Assert.Equal(new[] { "base", "nature_sensitivity", LandPermitSystem.EnvironmentalSurvey }, drivers.Select(d => d.Key));
        var (both, _) = LandPermitSystem.RefusalProbability(PermitKind.Discharge, 0.6, 1.0,
            new[] { LandPermitSystem.EnvironmentalSurvey, LandPermitSystem.Denitrification }, Permits);
        Assert.Equal(bare * (1.0 - Permits.SurveyRefusalCut) * (1.0 - Permits.DenitrificationRefusalCut), both, 12);
        // Denitrification reaches only the discharge permit; a smaller first phase lowers the scaled kinds.
        Assert.Equal(LandPermitSystem.RefusalProbability(PermitKind.Zoning, 0.6, 1.0, null, Permits).Probability,
            LandPermitSystem.RefusalProbability(PermitKind.Zoning, 0.6, 1.0, new[] { LandPermitSystem.Denitrification }, Permits).Probability);
        double pilotScale = LandPermitSystem.ScaleFactor(2_600, 7_900, Permits);
        Assert.Equal(Math.Sqrt(2_600.0 / 7_900.0), pilotScale, 12);
        Assert.Equal(bare * pilotScale, LandPermitSystem.RefusalProbability(PermitKind.Discharge, 0.6, pilotScale, null, Permits).Probability, 12);
        Assert.Equal(LandPermitSystem.RefusalProbability(PermitKind.SeaWorks, 0.6, 1.0, null, Permits).Probability,
            LandPermitSystem.RefusalProbability(PermitKind.SeaWorks, 0.6, pilotScale, null, Permits).Probability);

        // In the engine: the survey's cost is paid once, with the first case.
        var engine = LandReadyEngine(68);
        decimal cash = engine.FinanceManager.Balance;
        Assert.True(engine.ApplyLandPermits("pad_b", LandTemplate.HfsLot, 1, new[] { LandPermitSystem.EnvironmentalSurvey }).Ok);
        Assert.Equal(cash - Permits.SurveyCostNok - Permits.SeaWorksFeeNok, engine.FinanceManager.Balance);
        Assert.All(engine.PermitCases, c => Assert.Contains(LandPermitSystem.EnvironmentalSurvey, c.Mitigations));
    }

    [Fact]
    public void ARefusedCase_IsAppealedIn180Days_OrAppliedForAgainWithAMitigation()
    {
        // Find a seed whose discharge case on pad_b is refused.
        for (int seed = 100; seed < 400; seed++)
        {
            var engine = LandReadyEngine(seed);
            Assert.True(engine.ApplyLandPermits("pad_b", LandTemplate.HfsLot).Ok);
            var discharge = engine.PermitCases.Single(c => c.Kind == PermitKind.Discharge);
            if (!discharge.WillBeRefused) continue;
            Assert.Equal(LandSiteReasons.NotRefused, engine.AppealLandPermit(discharge.Id).ErrorKey);
            engine.Advance(discharge.DecisionDay - engine.State.Day);
            Assert.Equal(PermitStatus.Refused, discharge.Status);
            double refusedP = discharge.RefusalProbability;

            // Re-apply with denitrification: a new case at the lower odds.
            var again = engine.ReapplyLandPermit(discharge.Id, new[] { LandPermitSystem.Denitrification });
            Assert.True(again.Ok, again.Message);
            var second = engine.PermitCases.Single(c => c.Id == again.Id);
            Assert.Equal(refusedP * (1.0 - Permits.DenitrificationRefusalCut), second.RefusalProbability, 12);
            Assert.Equal(discharge.Conditions[LandPermitSystem.MaxProductionKey], second.Conditions[LandPermitSystem.MaxProductionKey]);

            // Or appeal: decided exactly 180 days on, at half the odds.
            int day = engine.State.Day;
            var appeal = engine.AppealLandPermit(discharge.Id);
            Assert.True(appeal.Ok, appeal.Message);
            Assert.Equal((PermitStatus.Appealed, day + 180, 2), (discharge.Status, discharge.DecisionDay, discharge.Round));
            Assert.Equal(refusedP * Permits.AppealRefusalFactor, discharge.RefusalProbability, 12);
            Assert.Equal(LandSiteReasons.NotFound, engine.AppealLandPermit("pc_999").ErrorKey);
            return;
        }
        Assert.Fail("no refused discharge case in 300 seeds");
    }

    // ── Acceptance 8: the moratorium ──────────────────────────────────────

    [Fact]
    public void TheMoratoriumFlag_RefusesNewLandApplications_WithMoratorium()
    {
        var engine = LandReadyEngine(69);
        engine.UseFacilitiesCalibration(engine.Calibration.Facilities with
        {
            Land = engine.LandCalibration with { Permits = engine.LandCalibration.Permits with { Moratorium = true } },
        });
        decimal cash = engine.FinanceManager.Balance;
        var refused = engine.ApplyLandPermits("pad_a", LandTemplate.HfsLot);
        Assert.Equal(LandSiteReasons.Moratorium, refused.ErrorKey);
        Assert.Equal(LandSiteReasons.Moratorium, engine.ApplyLandPermits("pad_a", LandTemplate.PostSmoltModule).ErrorKey);
        Assert.Empty(engine.PermitCases);
        Assert.Equal(cash, engine.FinanceManager.Balance);
        // Zoning is the municipality's: it does not wait for the moratorium.
        Assert.True(engine.ApplyForZoning("pad_a").Ok);
        Assert.False(LandCalibration.Default.Permits.Moratorium); // off by default
    }

    // ── Acceptance 7: the grid queue ──────────────────────────────────────

    [Fact]
    public void AGridProject_Queues365To1095Days_WithItsFee_AndASecondFeedCountsOnlyWhenFinished()
    {
        var engine = LandReadyEngine(70);
        double mw = engine.EffectiveGridCapacityMW("pad_a");
        int feeds = engine.EffectiveGridFeeds("pad_a");
        Assert.Equal((40.0, 1), (mw, feeds));
        decimal cash = engine.FinanceManager.Balance;
        var feed = engine.StartGridProject("pad_a", LandPermitSystem.GridSecondFeed);
        Assert.True(feed.Ok, feed.Message);
        var project = engine.PermitCases.Single(c => c.Id == feed.Id);
        Assert.Equal((PermitKind.GridConnection, 0.0), (project.Kind, project.RefusalProbability));
        Assert.InRange(project.DecisionDay - project.SubmittedDay, 365, 1095);
        Assert.Equal(cash - Permits.GridFeeNokPer10MW * 4m, engine.FinanceManager.Balance); // 40 MW on a new line
        Assert.Equal(LandSiteReasons.AlreadyPending, engine.StartGridProject("pad_a", LandPermitSystem.GridCapacity).ErrorKey);
        Assert.Equal(LandSiteReasons.OutOfRange, engine.StartGridProject("pad_a", "wind").ErrorKey);

        engine.Advance(project.DecisionDay - engine.State.Day - 1);
        Assert.Equal(1, engine.EffectiveGridFeeds("pad_a")); // not yet
        engine.Advance(1);
        Assert.Equal(PermitStatus.Approved, project.Status);
        Assert.Equal((2, 40.0), (engine.EffectiveGridFeeds("pad_a"), engine.EffectiveGridCapacityMW("pad_a")));

        var capacity = engine.StartGridProject("pad_a", LandPermitSystem.GridCapacity);
        Assert.True(capacity.Ok, capacity.Message);
        engine.Advance(engine.PermitCases.Single(c => c.Id == capacity.Id).DecisionDay - engine.State.Day);
        Assert.Equal(40.0 + Permits.GridProjectStepMW, engine.EffectiveGridCapacityMW("pad_a"));
    }

    [Fact]
    public void QuotingAndPreviewing_ChangeNothing_AndDrawNothing()
    {
        var engine = LandReadyEngine(71);
        engine.Advance(5);
        string before = LakskongenSim.Tests.Integration.SaveStateComparer.Serialize(engine);
        var streams = engine.Rng.CaptureStates();
        engine.QuoteLandPermits("pad_a", LandTemplate.HfsLot, 2, new[] { LandPermitSystem.EnvironmentalSurvey });
        engine.PreviewLandBuild("pad_a", LandTemplate.HfsLot, ContractForm.Managed);
        engine.PreviewLandBuild("pad_b", LandTemplate.HfsPilotLot, ContractForm.Turnkey);
        Assert.Equal(before, LakskongenSim.Tests.Integration.SaveStateComparer.Serialize(engine));
        Assert.Equal(streams, engine.Rng.CaptureStates());
    }
}
