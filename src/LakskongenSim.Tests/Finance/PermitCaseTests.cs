using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Finance;
using LakskongenSim.Tests.Integration;
using Xunit;

namespace LakskongenSim.Tests.FacilityFinance;

/// <summary>
/// F2-04: permit cases (docs/FARM_TYPES_SPEC.md §3.2, §3.7). A case draws
/// exactly two uniforms per decision round at submission ("draw, then
/// decide"), its fee is opex (ring-fenced when the plot serves a land site),
/// and the decision falls on its drawn day by its pre-drawn outcome.
/// </summary>
public class PermitCaseTests
{
    private static SimulationEngine NewEngine(int seed)
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: seed);
        engine.ReceiveLoan(100_000_000m);
        return engine;
    }

    private static readonly PermitTerms Zoning = new(365, 730, 0.3, 44_300m);

    private static (double Day, double Outcome) RoundDraws(int seed, string caseId, int round)
    {
        var stream = new SimRng(seed).Stream(SimRngStreams.FacilityPermits, caseId, round.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return (stream.NextDouble(), stream.NextDouble());
    }

    [Fact]
    public void ASubmission_PaysItsFee_DrawsTwice_AndIsDecidedOnItsDay_ByItsPreDrawnOutcome()
    {
        const int seed = 41;
        var engine = NewEngine(seed);
        engine.Advance(10);
        decimal cash = engine.FinanceManager.Balance;
        var submitted = engine.SubmitPermitCase(PermitKind.Zoning, "pad_a", "", Zoning, new[] { "survey" },
            new Dictionary<string, double> { ["maxStandingT"] = 13_300 });
        Assert.True(submitted.Ok, submitted.Message);
        var permit = engine.PermitCases.Single();
        var (dayDraw, outcomeDraw) = RoundDraws(seed, "pc_1", 1);

        Assert.Equal("pc_1", permit.Id);
        Assert.Equal(cash - 44_300m, engine.FinanceManager.Balance);
        Assert.Equal(44_300m, engine.FinancialStatements.CostBreakdown["permit_fees"]);
        Assert.Equal(44_300m, engine.State.RentRingFencedCostsThisTaxYear); // a land pad's fee stays out of the rent base
        Assert.Equal(PermitCase.DecisionDayFor(10, Zoning, dayDraw), permit.DecisionDay);
        Assert.InRange(permit.DecisionDay, 10 + 365, 10 + 730);
        Assert.Equal((outcomeDraw, 0.3), (permit.OutcomeDraw, permit.RefusalProbability));
        Assert.Equal(new[] { "survey" }, permit.Mitigations);
        Assert.Equal(13_300, permit.Conditions["maxStandingT"]);

        engine.Advance(permit.DecisionDay - engine.State.Day - 1);
        Assert.Equal(PermitStatus.Submitted, permit.Status);
        engine.Advance(1);
        Assert.Equal(outcomeDraw < 0.3 ? PermitStatus.Refused : PermitStatus.Approved, permit.Status);
        Assert.Equal(permit.DecisionDay, permit.DecidedDay);
    }

    [Fact]
    public void CertainTerms_AlwaysDecideTheSameWay()
    {
        for (int seed = 1; seed <= 20; seed++)
        {
            var engine = NewEngine(seed);
            engine.SubmitPermitCase(PermitKind.SeaWorks, "pad_a", "u1", new PermitTerms(0, 0, 0.0));
            engine.SubmitPermitCase(PermitKind.Discharge, "pad_a", "u1", new PermitTerms(0, 0, 1.0));
            engine.Advance(1);
            Assert.Equal(PermitStatus.Approved, engine.PermitCases[0].Status);
            Assert.Equal(PermitStatus.Refused, engine.PermitCases[1].Status);
        }
    }

    [Fact]
    public void OverManyCases_TheRefusalShareIsTheProbability_AndDecisionDaysFillTheWindow()
    {
        var engine = NewEngine(42);
        const int cases = 600;
        for (int i = 0; i < cases; i++)
            Assert.True(engine.SubmitPermitCase(PermitKind.LandAquaculture, $"plot_{i}", "", new PermitTerms(10, 19, 0.25)).Ok);
        engine.Advance(25);
        double refused = engine.PermitCases.Count(c => c.Status == PermitStatus.Refused) / (double)cases;
        Assert.InRange(refused, 0.25 - 0.05, 0.25 + 0.05); // ±2.8 standard errors
        var days = engine.PermitCases.GroupBy(c => c.DecisionDay).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(Enumerable.Range(10, 10), days.Keys.OrderBy(d => d));
        Assert.All(days.Values, n => Assert.InRange(n, 30, 90));
    }

    [Fact]
    public void ARefusal_CanBeAppealed_InANewRoundWithItsOwnTwoDraws()
    {
        const int seed = 43;
        var engine = NewEngine(seed);
        engine.SubmitPermitCase(PermitKind.Conversion, "off_1", "offshore_1", new PermitTerms(5, 5, 1.0));
        var permit = engine.PermitCases.Single();
        Assert.Equal("not_refused", engine.AppealPermitCase(permit.Id, new PermitTerms(10, 20, 0.0)).ErrorKey);
        engine.Advance(6);
        Assert.Equal(PermitStatus.Refused, permit.Status);

        var appealed = engine.AppealPermitCase(permit.Id, new PermitTerms(10, 20, 0.4));
        Assert.True(appealed.Ok, appealed.Message);
        var (dayDraw, outcomeDraw) = RoundDraws(seed, permit.Id, 2);
        Assert.Equal((PermitStatus.Appealed, 2), (permit.Status, permit.Round));
        Assert.Equal(PermitCase.DecisionDayFor(engine.State.Day, new PermitTerms(10, 20, 0.4), dayDraw), permit.DecisionDay);
        Assert.Equal(outcomeDraw, permit.OutcomeDraw);
        engine.Advance(21);
        Assert.Equal(outcomeDraw < 0.4 ? PermitStatus.Refused : PermitStatus.Approved, permit.Status);
        // An offshore slot's case is the sea's: its fee is not ring-fenced.
        Assert.Equal(0m, engine.State.RentRingFencedCostsThisTaxYear);
    }

    [Fact]
    public void Refusals_ChangeNothing()
    {
        var engine = NewEngine(44);
        engine.SubmitPermitCase(PermitKind.Zoning, "pad_a", "", Zoning);
        decimal cash = engine.FinanceManager.Balance;
        int next = engine.State.NextPermitCaseNumber;
        Assert.Equal("already_pending", engine.SubmitPermitCase(PermitKind.Zoning, "pad_a", "", Zoning).ErrorKey);
        Assert.Equal("invalid_terms", engine.SubmitPermitCase(PermitKind.Zoning, "pad_b", "", new PermitTerms(10, 5, 0.1)).ErrorKey);
        Assert.Equal("invalid_terms", engine.SubmitPermitCase(PermitKind.Zoning, "pad_b", "", new PermitTerms(1, 5, 1.5)).ErrorKey);
        Assert.Equal("invalid_terms", engine.SubmitPermitCase(PermitKind.Zoning, "pad_b", "", null!).ErrorKey);
        Assert.Equal("insufficient_funds", engine.SubmitPermitCase(PermitKind.Zoning, "pad_b", "", new PermitTerms(1, 5, 0.1, 1_000_000_000_000m)).ErrorKey);
        Assert.Equal("not_found", engine.AppealPermitCase("pc_9", Zoning).ErrorKey);
        Assert.Equal("not_found", engine.WithdrawPermitCase("pc_9").ErrorKey);
        Assert.Equal((cash, next, 1), (engine.FinanceManager.Balance, engine.State.NextPermitCaseNumber, engine.PermitCases.Count));

        Assert.True(engine.WithdrawPermitCase("pc_1").Ok);
        Assert.Equal(PermitStatus.Withdrawn, engine.PermitCases.Single().Status);
        Assert.Equal("not_pending", engine.WithdrawPermitCase("pc_1").ErrorKey);
        engine.Advance(800);
        Assert.Equal(PermitStatus.Withdrawn, engine.PermitCases.Single().Status);
    }

    [Fact]
    public void AFeeWithoutAPlot_IsTheCasesKind_Cost()
    {
        // A licence case tied to no plot is the offshore unit's (rent-liable):
        // its fee stays a sea-phase cost; a land permit's fee is ring-fenced.
        var engine = NewEngine(46);
        Assert.True(engine.SubmitPermitCase(PermitKind.DevelopmentLicence, "", "", new PermitTerms(10, 20, 0.2, 1_000_000m)).Ok);
        Assert.Equal(0m, engine.State.RentRingFencedCostsThisTaxYear);
        Assert.True(engine.SubmitPermitCase(PermitKind.Dispensation, "", "site_1", new PermitTerms(10, 20, 0.2, 300_000m)).Ok);
        Assert.Equal(0m, engine.State.RentRingFencedCostsThisTaxYear);
        Assert.True(engine.SubmitPermitCase(PermitKind.GridConnection, "", "", new PermitTerms(10, 20, 0.2, 500_000m)).Ok);
        Assert.Equal(500_000m, engine.State.RentRingFencedCostsThisTaxYear);
        Assert.Equal(1_800_000m, engine.FinancialStatements.CostBreakdown["permit_fees"]);
    }

    [Fact]
    public void APendingCase_SurvivesASave_AndIsDecidedTheSameWay()
    {
        var engine = NewEngine(45);
        engine.SubmitPermitCase(PermitKind.Discharge, "pad_b", "ras_1", new PermitTerms(30, 90, 0.5),
            conditions: new Dictionary<string, double> { ["nitrogenTPerYear"] = 120 });
        engine.Advance(10);
        var loaded = SaveStateComparer.Resume(SaveStateComparer.Serialize(engine), events: false);
        var a = engine.PermitCases.Single();
        var b = loaded.PermitCases.Single();
        Assert.Equal((a.Id, a.Kind, a.PlotId, a.UnitId, a.DecisionDay, a.OutcomeDraw, a.Status, a.Conditions["nitrogenTPerYear"]),
            (b.Id, b.Kind, b.PlotId, b.UnitId, b.DecisionDay, b.OutcomeDraw, b.Status, b.Conditions["nitrogenTPerYear"]));
        Assert.Equal(engine.State.NextPermitCaseNumber, loaded.State.NextPermitCaseNumber);
        engine.Advance(100);
        loaded.Advance(100);
        Assert.Equal(engine.PermitCases.Single().Status, loaded.PermitCases.Single().Status);
        Assert.NotEqual(PermitStatus.Submitted, loaded.PermitCases.Single().Status);
    }
}
