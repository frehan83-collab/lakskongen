using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Calibration;
using LakskongenSim.Core;
using LakskongenSim.Finance;
using LakskongenSim.Tests.Integration;
using Xunit;

namespace LakskongenSim.Tests.Core;

/// <summary>
/// F2-04: construction projects (docs/FARM_TYPES_SPEC.md §3.2, §3.6, §3.7).
/// Phases are paid as capex (planned at the start, the overrun at the end),
/// each phase's start draws exactly two uniforms from its own stream, and a
/// finished project's cost enters the asset register on its completion day.
/// </summary>
public class ConstructionProjectTests
{
    private static readonly FacilityConstructionCalibration Cal = CalibrationRegistry.Default.Facilities.Common.Construction;

    private static SimulationEngine NewEngine(int seed, decimal cash = 2_000_000_000m)
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: seed);
        if (cash > 0m) engine.ReceiveLoan(cash);
        return engine;
    }

    private static List<ConstructionPhase> ThreePhases() => new()
    {
        new ConstructionPhase { Key = "site", PlannedDays = 40, CapexShare = 0.2 },
        new ConstructionPhase { Key = "tanks", PlannedDays = 100, CapexShare = 0.7 },
        new ConstructionPhase { Key = "commissioning", PlannedDays = 20, CapexShare = 0.1 },
    };

    /// <summary>The two uniforms a phase's own stream gives first (a fresh generator on the same seed).</summary>
    private static (double Cost, double Duration) PhaseDraws(int seed, string projectId, string phaseKey)
    {
        var stream = new SimRng(seed).Stream(SimRngStreams.FacilityConstruction, projectId, phaseKey);
        return (stream.NextDouble(), stream.NextDouble());
    }

    // ── The arithmetic ─────────────────────────────────────────────────────

    [Fact]
    public void Excess_IsALognormal_SetByItsMedianAndP90_TruncatedAtTheP99()
    {
        double median = ConstructionSchedule.ExcessFraction(Cal.OverrunMedian, Cal.OverrunP90, 0.5 / ConstructionSchedule.TruncationQuantile);
        double p90 = ConstructionSchedule.ExcessFraction(Cal.OverrunMedian, Cal.OverrunP90, 0.9 / ConstructionSchedule.TruncationQuantile);
        Assert.Equal(Cal.OverrunMedian, median, 6);
        Assert.Equal(Cal.OverrunP90, p90, 6);
        double previous = -1.0;
        for (int i = 0; i < 1000; i++)
        {
            double x = ConstructionSchedule.ExcessFraction(Cal.OverrunMedian, Cal.OverrunP90, i / 1000.0);
            Assert.True(x >= 0.0 && x >= previous);
            previous = x;
        }
        double top = ConstructionSchedule.ExcessFraction(Cal.OverrunMedian, Cal.OverrunP90, Math.BitDecrement(1.0));
        double sigma = (Math.Log(Cal.OverrunP90) - Math.Log(Cal.OverrunMedian)) / StandardNormal.Z90;
        Assert.Equal(Cal.OverrunMedian * Math.Exp(sigma * 2.3263478740408408), top, 4); // the 99th percentile
        // A turnkey contract never overruns; its schedule still slips.
        Assert.Equal(1.0, ConstructionSchedule.OverrunFactor(Cal, ContractForm.Turnkey, 0.99));
        Assert.True(ConstructionSchedule.DurationFactor(Cal, 0.99) > 1.0);
    }

    [Fact]
    public void TheStandardNormalQuantile_MatchesKnownValues()
    {
        Assert.Equal(0.0, StandardNormal.Quantile(0.5), 9);
        Assert.Equal(StandardNormal.Z90, StandardNormal.Quantile(0.9), 7); // Acklam: relative error below 1.15e-9
        Assert.Equal(-1.6448536269514722, StandardNormal.Quantile(0.05), 7);
        Assert.Equal(2.3263478740408408, StandardNormal.Quantile(0.99), 7);
        Assert.Equal(-3.090232306167813, StandardNormal.Quantile(0.001), 7);
        Assert.Throws<ArgumentOutOfRangeException>(() => StandardNormal.Quantile(0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => StandardNormal.Quantile(1.0));
    }

    // ── A managed project ──────────────────────────────────────────────────

    [Fact]
    public void AManagedProject_PaysEachPhaseAtItsStart_AndItsOverrunAtItsEnd_DrawingTwiceAPhase()
    {
        const int seed = 21;
        var engine = NewEngine(seed);
        engine.Advance(3);
        decimal cash0 = engine.FinanceManager.Balance;
        decimal capex0 = engine.FinancialStatements.LifetimeCapex;
        var started = engine.StartConstructionProject("ras_9", FacilityAssetKind.LandSite, 1_000_000_000m, ThreePhases());
        Assert.True(started.Ok, started.Message);
        Assert.Equal("cp_1", started.Id);
        var project = engine.ConstructionProjects.Single();

        // Phase 1 started today: its planned share paid, its two uniforms drawn.
        var (cost1, duration1) = PhaseDraws(seed, "cp_1", "site");
        Assert.Equal(ConstructionStatus.Building, project.Status);
        Assert.Equal(200_000_000m, project.PaidNOK);
        Assert.Equal(cash0 - 200_000_000m, engine.FinanceManager.Balance);
        Assert.Equal(capex0 + 200_000_000m, engine.FinancialStatements.LifetimeCapex);
        Assert.Equal(ConstructionSchedule.OverrunFactor(Cal, ContractForm.Managed, cost1), project.OverrunFactor);
        Assert.Equal(ConstructionSchedule.DurationFactor(Cal, duration1), project.DurationFactor);
        Assert.Equal(ConstructionSchedule.PhaseDays(project.Phases[0], project.DurationFactor), project.PhaseDays);
        Assert.Equal(3, project.PhaseStartDay);
        Assert.True(project.OverrunFactor > 1.0 && project.PhaseDays >= 40);

        // Run to the end: every phase paid share × (overrun factor), the asset registered from completion.
        decimal expectedPaid = 0m;
        int guard = 0;
        var seenPhases = new HashSet<string>();
        while (project.Status != ConstructionStatus.Complete && guard++ < 2000)
        {
            if (project.Current is { } phase && seenPhases.Add(phase.Key))
                expectedPaid += 1_000_000_000m * (decimal)phase.CapexShare * (decimal)Math.Max(1.0, project.OverrunFactor);
            engine.Advance(1);
        }
        Assert.Equal(ConstructionStatus.Complete, project.Status);
        Assert.Equal(3, project.PhaseCompletedDays.Count);
        Assert.True(Math.Abs(expectedPaid - project.PaidNOK) < 0.0001m, $"{expectedPaid} vs {project.PaidNOK}");
        Assert.True(project.PaidNOK > 1_000_000_000m, "a managed build pays its overrun");
        Assert.Equal(engine.FinancialStatements.LifetimeCapex - capex0, project.PaidNOK);
        var asset = Assert.Single(engine.State.FacilityAssets);
        Assert.Equal(("ras_9", FacilityAssetKind.LandSite, project.PaidNOK, project.PhaseCompletedDays[^1], 20),
            (asset.UnitId, asset.Kind, asset.CapexNok, asset.StartDay, asset.LifeYears));
        // Each phase's own stream moved by exactly two draws.
        var positions = engine.Rng.CaptureStates();
        foreach (var key in new[] { "site", "tanks", "commissioning" })
        {
            Assert.Equal(new SimRng(seed).CaptureStatesAfter(SimRngStreams.FacilityConstruction, "cp_1", key, draws: 2),
                positions[SimRngStreams.FacilityConstruction + "\u0001cp_1\u0001" + key]);
        }
        Assert.Equal(0m, engine.UnbookedCashTotal);
    }

    [Fact]
    public void ATurnkeyProject_PaysThePremiumUpFront_AndNoOverrun_ButItsScheduleStillSlips()
    {
        var engine = NewEngine(22);
        var started = engine.StartConstructionProject("offshore_9", FacilityAssetKind.OffshoreUnit, 500_000_000m,
            new[] { new ConstructionPhase { Key = "yard", PlannedDays = 30, CapexShare = 1.0 } }, ContractForm.Turnkey);
        Assert.True(started.Ok, started.Message);
        var project = engine.ConstructionProjects.Single();
        Assert.Equal(500_000_000m * (1m + (decimal)Cal.TurnkeyPremium), project.PaidNOK);
        Assert.Equal(1.0, project.OverrunFactor);
        Assert.True(project.DurationFactor > 1.0);
        engine.Advance(project.PhaseDays + 1);
        Assert.Equal(ConstructionStatus.Complete, project.Status);
        Assert.Equal(500_000_000m * (1m + (decimal)Cal.TurnkeyPremium), project.PaidNOK);
        Assert.Equal(25, Assert.Single(engine.State.FacilityAssets).LifeYears);
    }

    [Fact]
    public void AConsortiumUnit_PaysItsEquityShareOnly()
    {
        var engine = NewEngine(23);
        engine.StartConstructionProject("offshore_9", FacilityAssetKind.OffshoreUnit, 1_000_000_000m,
            new[] { new ConstructionPhase { Key = "yard", PlannedDays = 10, CapexShare = 1.0 } }, ContractForm.Turnkey, equityShare: 0.35);
        Assert.Equal(350_000_000m * (1m + (decimal)Cal.TurnkeyPremium), engine.ConstructionProjects.Single().PaidNOK);
    }

    // ── Waiting phases ─────────────────────────────────────────────────────

    [Fact]
    public void APlayerStartedPhase_WaitsForThePlayer_WithoutDrawing()
    {
        var engine = NewEngine(24);
        var phases = new List<ConstructionPhase>
        {
            new() { Key = "yard", PlannedDays = 5, CapexShare = 0.8 },
            new() { Key = "tow", PlannedDays = 3, CapexShare = 0.2, PlayerStarted = true, MaxHsM = 2.5 },
        };
        engine.StartConstructionProject("offshore_9", FacilityAssetKind.OffshoreUnit, 100_000_000m, phases);
        var project = engine.ConstructionProjects.Single();
        Assert.Equal("not_ready", engine.StartConstructionPhase(project.Id).ErrorKey); // the yard is building
        engine.Advance(40);
        Assert.Equal((ConstructionStatus.Waiting, 1), (project.Status, project.CurrentPhase));
        Assert.DoesNotContain(engine.Rng.CaptureStates().Keys, k => k.EndsWith("\u0001tow", StringComparison.Ordinal));
        decimal paid = project.PaidNOK;

        var start = engine.StartConstructionPhase(project.Id);
        Assert.True(start.Ok, start.Message);
        Assert.Equal(ConstructionStatus.Building, project.Status);
        Assert.Equal(paid + 20_000_000m, project.PaidNOK);
        Assert.Equal("not_ready", engine.StartConstructionPhase(project.Id).ErrorKey);
        Assert.Equal("not_found", engine.StartConstructionPhase("cp_99").ErrorKey);
    }

    [Fact]
    public void APhaseInAClosedMonth_WaitsForTheFirstOpenDay()
    {
        var engine = NewEngine(25);
        // Day 0 is 1 January: a phase closed in January and February starts on 1 March (day 59).
        engine.StartConstructionProject("ras_9", FacilityAssetKind.LandSite, 10_000_000m,
            new[] { new ConstructionPhase { Key = "sea_works", PlannedDays = 10, CapexShare = 1.0, ClosedMonths = new[] { 1, 2 } } });
        var project = engine.ConstructionProjects.Single();
        Assert.Equal(ConstructionStatus.Waiting, project.Status);
        engine.Advance(58);
        Assert.Equal(ConstructionStatus.Waiting, project.Status);
        engine.Advance(1);
        Assert.Equal((ConstructionStatus.Building, 59), (project.Status, project.PhaseStartDay));
    }

    [Fact]
    public void APhaseWithoutTheMoney_WaitsWithoutDrawing_AndStartsOnceTheCashIsThere()
    {
        var engine = NewEngine(26, cash: 0m);
        decimal balance = engine.FinanceManager.Balance;
        var started = engine.StartConstructionProject("ras_9", FacilityAssetKind.LandSite, balance * 10m,
            new[] { new ConstructionPhase { Key = "all", PlannedDays = 10, CapexShare = 1.0 } });
        Assert.True(started.Ok);
        var project = engine.ConstructionProjects.Single();
        Assert.Equal((ConstructionStatus.Waiting, 0m), (project.Status, project.PaidNOK));
        Assert.Equal(balance, engine.FinanceManager.Balance);
        engine.Advance(5);
        Assert.Equal(ConstructionStatus.Waiting, project.Status);
        Assert.DoesNotContain(engine.Rng.CaptureStates().Keys, k => k.StartsWith(SimRngStreams.FacilityConstruction, StringComparison.Ordinal));
        engine.ReceiveLoan(balance * 20m);
        engine.Advance(1);
        Assert.Equal((ConstructionStatus.Building, engine.State.Day), (project.Status, project.PhaseStartDay));
    }

    [Theory]
    [InlineData("", 1.0, 1, "a project needs a unit")]
    [InlineData("u", 0.0, 1, "a project needs a positive budget")]
    [InlineData("u", 1.0, 0, "the equity share must lie in (0, 1]")]
    public void AnInvalidProject_ChangesNothing(string unitId, double budget, int equity, string why)
    {
        var engine = NewEngine(27);
        decimal cash = engine.FinanceManager.Balance;
        var result = engine.StartConstructionProject(unitId, FacilityAssetKind.LandSite, (decimal)budget * 1_000_000m, ThreePhases(),
            equityShare: equity);
        Assert.Equal(("invalid_project", why), (result.ErrorKey, result.Message));
        Assert.Empty(engine.ConstructionProjects);
        Assert.Equal(1, engine.State.NextConstructionProjectNumber);
        Assert.Equal(cash, engine.FinanceManager.Balance);
    }

    [Fact]
    public void BadPhaseLists_AreRefused()
    {
        var engine = NewEngine(28);
        IReadOnlyList<ConstructionPhase>[] bad =
        {
            Array.Empty<ConstructionPhase>(),
            new[] { new ConstructionPhase { Key = "a", PlannedDays = 10, CapexShare = 0.5 } },
            new[] { new ConstructionPhase { Key = "a", PlannedDays = 0, CapexShare = 1.0 } },
            new[] { new ConstructionPhase { Key = "a", PlannedDays = 5, CapexShare = 0.5 }, new ConstructionPhase { Key = "a", PlannedDays = 5, CapexShare = 0.5 } },
            new[] { new ConstructionPhase { Key = "a", PlannedDays = 5, CapexShare = 1.0, ClosedMonths = new[] { 13 } } },
            new[] { new ConstructionPhase { Key = "a", PlannedDays = 5, CapexShare = 1.0, ClosedMonths = Enumerable.Range(1, 12).ToArray() } },
        };
        foreach (var phases in bad)
            Assert.Equal("invalid_project", engine.StartConstructionProject("u", FacilityAssetKind.LandSite, 1_000m, phases).ErrorKey);
        Assert.Empty(engine.ConstructionProjects);
    }

    // ── Determinism ────────────────────────────────────────────────────────

    [Fact]
    public void AnotherProject_NeverShiftsAProjectsDraws()
    {
        var alone = NewEngine(29);
        var crowded = NewEngine(29);
        crowded.StartConstructionProject("other", FacilityAssetKind.LandSite, 5_000_000m,
            new[] { new ConstructionPhase { Key = "site", PlannedDays = 7, CapexShare = 1.0 } });
        alone.State.NextConstructionProjectNumber = 2; // the same id, "cp_2", in both games
        alone.StartConstructionProject("ras_9", FacilityAssetKind.LandSite, 1_000_000_000m, ThreePhases());
        crowded.StartConstructionProject("ras_9", FacilityAssetKind.LandSite, 1_000_000_000m, ThreePhases());
        var a = alone.ConstructionProjects.Single(p => p.UnitId == "ras_9");
        var b = crowded.ConstructionProjects.Single(p => p.UnitId == "ras_9");
        Assert.Equal((a.OverrunFactor, a.DurationFactor, a.PhaseDays), (b.OverrunFactor, b.DurationFactor, b.PhaseDays));
    }

    [Fact]
    public void AProjectMidPhase_SurvivesASave_AndContinuesExactly()
    {
        var engine = NewEngine(30);
        engine.StartConstructionProject("ras_9", FacilityAssetKind.LandSite, 1_000_000_000m, ThreePhases());
        engine.Advance(70); // inside the second phase
        var loaded = SaveStateComparer.Resume(SaveStateComparer.Serialize(engine), events: false);
        var before = engine.ConstructionProjects.Single();
        var after = loaded.ConstructionProjects.Single();
        Assert.Equal((before.CurrentPhase, before.Status, before.PhaseStartDay, before.PhaseDays, before.OverrunFactor, before.PaidNOK),
            (after.CurrentPhase, after.Status, after.PhaseStartDay, after.PhaseDays, after.OverrunFactor, after.PaidNOK));
        Assert.Equal(engine.State.NextConstructionProjectNumber, loaded.State.NextConstructionProjectNumber);
        engine.Advance(200);
        loaded.Advance(200);
        Assert.Equal(ConstructionStatus.Complete, engine.ConstructionProjects.Single().Status);
        Assert.Equal(ConstructionStatus.Complete, loaded.ConstructionProjects.Single().Status);
        Assert.Equal(engine.ConstructionProjects.Single().PaidNOK, loaded.ConstructionProjects.Single().PaidNOK);
        Assert.Equal(SaveStateComparer.Normalize(SaveStateComparer.Serialize(engine)),
            SaveStateComparer.Normalize(SaveStateComparer.Serialize(loaded)));
    }
}

/// <summary>Test helper: a stream's position after a number of draws, as <see cref="SimRng.CaptureStates"/> keys it.</summary>
internal static class SimRngTestExtensions
{
    public static ulong CaptureStatesAfter(this SimRng rng, string scope, string a, string b, int draws)
    {
        var stream = rng.Stream(scope, a, b);
        for (int i = 0; i < draws; i++) stream.NextDouble();
        return rng.CaptureStates()[scope + "\u0001" + a + "\u0001" + b];
    }
}
