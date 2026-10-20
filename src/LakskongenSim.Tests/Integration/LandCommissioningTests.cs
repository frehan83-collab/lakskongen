using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Calibration;
using LakskongenSim.Core;
using LakskongenSim.Farm;
using LakskongenSim.Farm.Land;
using LakskongenSim.Finance;
using LakskongenSim.Tests.FacilityFinance;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// F2-14 (docs/FARM_TYPES_SPEC.md §7.1 F2-14 acceptance 1, 3-6): no land build
/// without its permits; the HFS lot's first tanks at 22 months and the whole
/// lot at 30 from construction start ([L:R1]); sea works never in April-June;
/// a managed build's overrun over 200 seeds has its median at +5-10 % and its
/// 90th percentile at most +30 % ([L:A4] anchored), a turnkey build pays +12 %
/// and no overrun; phase payments are capex and a cancelled build's money stays
/// spent; a pilot and its expansion cost 2.15 bn.
/// </summary>
public class LandCommissioningTests
{
    private readonly ITestOutputHelper _output;
    public LandCommissioningTests(ITestOutputHelper output) => _output = output;

    private static LandCalibration Land => LandCalibration.Default;
    private static FacilityConstructionCalibration Risk => CalibrationRegistry.Default.Facilities.Common.Construction;

    /// <summary>1 June of the first year: Salmon Evolution decided phase 2 in June 2024 ([L:R1]).</summary>
    private const int FirstOfJune = 151;

    private static IReadOnlyList<ConstructionPhase> HfsLotPhases(decimal blasting = 0m)
        => LandConstructionPhases.Phases(Land.Build.HfsLotPhases, Land.Permits, Land.Build.HfsLotCapexNok, blasting);

    // ── Acceptance 6: 22 and 30 months; pilot + expansion = 2.15 bn ───────

    [Fact]
    public void Acceptance6_AnHfsLotStartedInJune_HasItsFirstTanksAt22Months_AndIsCompleteAt30()
    {
        var phases = HfsLotPhases(120_000_000m);
        Assert.Equal(new[] { "site_works", "tank_rings", "treatment_hall", "sea_works", "first_tanks", "completion" },
            phases.Select(p => p.Key));
        Assert.Equal(1.0, phases.Sum(p => p.CapexShare), 12);
        Assert.Equal((670, 913), (Land.Build.HfsLotPhases.PlannedFirstTanksDays(), Land.Build.HfsLotPhases.PlannedCompleteDays()));

        var plan = LandConstructionPhases.Schedule(phases, FirstOfJune);
        Assert.Equal(22, SimCalendar.WholeMonthsBetween(FirstOfJune, plan.FirstTanksDay));
        Assert.Equal(30, SimCalendar.WholeMonthsBetween(FirstOfJune, plan.CompleteDay));
        Assert.Equal((FirstOfJune + 670, FirstOfJune + 913), (plan.FirstTanksDay, plan.CompleteDay));

        // The engine's own slip over 200 seeds (the uniforms cp_1's phases draw): the median lot is a month late at most.
        var firsts = new List<int>();
        var completes = new List<int>();
        for (int seed = 1; seed <= 200; seed++)
        {
            var factors = phases.Select(p => ConstructionSchedule.DurationFactor(Risk, Draws(seed, p.Key).Duration)).ToList();
            var schedule = LandConstructionPhases.Schedule(phases, FirstOfJune, factors);
            firsts.Add(SimCalendar.WholeMonthsBetween(FirstOfJune, schedule.FirstTanksDay));
            completes.Add(SimCalendar.WholeMonthsBetween(FirstOfJune, schedule.CompleteDay));
        }
        firsts.Sort();
        completes.Sort();
        _output.WriteLine($"first tanks p50 {firsts[100]} p90 {firsts[180]} months; complete p50 {completes[100]} p90 {completes[180]} months");
        // The plan is [L:R1]'s; the realized schedule carries the shared slip
        // (facilities.common.construction, per phase) and a slipped sea works
        // phase meets the closed season: recorded, RUN_LOG §8 (F2-14 finding).
        Assert.InRange(firsts[100], 22, 27);
        Assert.InRange(firsts[180], firsts[100], 30);
        Assert.InRange(completes[100], 30, 36);
        Assert.All(firsts, m => Assert.True(m >= 22, "nothing is built faster than its plan"));
        // The preview reads the same distribution on its quasi-random grid.
        var risk = LandConstructionPhases.Risk(phases, ContractForm.Managed, Risk, FirstOfJune);
        Assert.InRange(SimCalendar.WholeMonthsBetween(FirstOfJune, risk.FirstTanksP50Day), firsts[100] - 1, firsts[100] + 1);
        Assert.InRange(SimCalendar.WholeMonthsBetween(FirstOfJune, risk.CompleteP50Day), completes[100] - 1, completes[100] + 1);
    }

    [Fact]
    public void Acceptance6_APilotAndItsExpansion_Cost215Billion_AgainstTheCataloguesFullLot()
    {
        Assert.Equal(2_150_000_000m, Land.Build.HfsPilotCapexNok + Land.Build.HfsPilotExpansionCapexNok);
        var catalog = CalibrationRegistry.Default.Facilities.Common.Catalog.Hfs;
        Assert.Equal(catalog.PilotLotCapexNok, Land.Build.HfsPilotCapexNok);
        Assert.InRange((double)Land.Build.HfsLotCapexNok, (double)catalog.CapexNokPerTonneHog * catalog.LotCapacityTonnesHog * 0.99,
            (double)catalog.CapexNokPerTonneHog * catalog.LotCapacityTonnesHog * 1.01);
        foreach (var plan in new[] { Land.Build.HfsLotPhases, Land.Build.HfsPilotPhases, Land.Build.HfsExpansionPhases,
                     Land.Build.PostSmoltPhases, Land.Build.RasHallPhases })
        {
            double shares = plan.SiteWorksShare + plan.TankRingsShare + plan.TreatmentHallShare + plan.SeaWorksShare
                + plan.FirstTanksShare + plan.CompletionShare;
            Assert.Equal(1.0, shares, 12);
        }
        Assert.Equal(548, Land.Build.PostSmoltPhases.PlannedCompleteDays()); // the pre-F2 module's build [code]
        Assert.Equal(913, Land.Build.RasHallPhases.PlannedCompleteDays());   // spec §2A: 30 months
    }

    // ── Acceptance 3: no sea works in April-June ──────────────────────────

    [Fact]
    public void Acceptance3_SeaWorksNeverRunInAprilToJune_WhateverTheStartAndTheSlip()
    {
        var phases = HfsLotPhases();
        int sea = phases.ToList().FindIndex(p => p.Key == LandConstructionPhases.SeaWorks);
        Assert.Equal(new[] { 4, 5, 6 }, phases[sea].ClosedMonths);
        Assert.True(phases[sea].SuspendedInClosedMonths);
        Assert.All(phases.Where(p => p.Key != LandConstructionPhases.SeaWorks), p => Assert.Empty(p.ClosedMonths));
        int checkedDays = 0, closedSkipped = 0;
        for (int start = 0; start < SimCalendar.DaysPerYear; start += 7)
        {
            foreach (int seed in new[] { 3, 17, 99 })
            {
                var factors = phases.Select(p => ConstructionSchedule.DurationFactor(Risk, Draws(seed + start, p.Key).Duration)).ToList();
                var span = LandConstructionPhases.Schedule(phases, start, factors).Phases[sea];
                int phaseDays = ConstructionSchedule.PhaseDays(phases[sea], factors[sea]);
                Assert.NotInRange(SimCalendar.Month(span.StartDay), 4, 6);
                int worked = 0;
                for (int d = span.StartDay + 1; d <= span.EndDay; d++)
                {
                    if (SimCalendar.Month(d) is >= 4 and <= 6) { closedSkipped++; continue; }
                    worked++;
                }
                Assert.Equal(phaseDays, worked);
                Assert.NotInRange(SimCalendar.Month(span.EndDay), 4, 6);
                checkedDays += worked;
            }
        }
        Assert.True(closedSkipped > 0, "some starts must meet the closed season");
        _output.WriteLine($"{checkedDays} sea-works days checked, {closedSkipped} closed days stood still");
    }

    // ── Acceptance 4: managed overrun, turnkey premium ────────────────────

    [Fact]
    public void Acceptance4_ManagedOverrunOver200Seeds_HasItsMedianAt5To10Percent_AndItsP90AtMost30()
    {
        var phases = HfsLotPhases(120_000_000m);
        var overruns = new List<double>();
        for (int seed = 1; seed <= 200; seed++)
            overruns.Add(phases.Sum(p => p.CapexShare
                * (ConstructionSchedule.OverrunFactor(Risk, ContractForm.Managed, Draws(seed, p.Key).Cost) - 1.0)));
        overruns.Sort();
        double p50 = overruns[100], p90 = overruns[180];
        _output.WriteLine($"managed overrun over 200 seeds: p50 {p50:P1}, p90 {p90:P1}");
        Assert.InRange(p50, 0.05, 0.10);
        Assert.InRange(p90, p50, 0.30);
        // The preview's band is the same distribution.
        var risk = LandConstructionPhases.Risk(phases, ContractForm.Managed, Risk, FirstOfJune);
        Assert.InRange(risk.OverrunP50, p50 - 0.01, p50 + 0.01);
        Assert.InRange(risk.OverrunP90, p90 - 0.03, p90 + 0.03);
        // Turnkey: +12 % up front and no overrun, ever.
        var turnkey = LandConstructionPhases.Risk(phases, ContractForm.Turnkey, Risk, FirstOfJune);
        Assert.Equal((0.0, 0.0), (turnkey.OverrunP50, turnkey.OverrunP90));
        var payments = LandConstructionPhases.Payments(phases, 2_190_000_000m, ContractForm.Turnkey, Risk, FirstOfJune);
        Assert.Equal(2_190_000_000m * 1.12m, payments.Sum(p => p.PaymentNok), 0);
        Assert.All(payments, p => Assert.Equal((0m, 0m), (p.OverrunP50Nok, p.OverrunP90Nok)));
        var managed = LandConstructionPhases.Payments(phases, 2_190_000_000m, ContractForm.Managed, Risk, FirstOfJune);
        Assert.Equal(2_190_000_000m, managed.Sum(p => p.PaymentNok), 0);
        Assert.All(managed, p => Assert.True(p.OverrunP50Nok < p.OverrunP90Nok));
    }

    /// <summary>The two uniforms phase <paramref name="key"/> of project cp_1 draws in a game of <paramref name="seed"/>.</summary>
    private static (double Cost, double Duration) Draws(int seed, string key)
    {
        var stream = new SimRng(seed).Stream(SimRngStreams.FacilityConstruction, "cp_1", key);
        return (stream.NextDouble(), stream.NextDouble());
    }

    // ── Acceptance 1 and 5: the engine's build, end to end ────────────────

    /// <summary>A game ready for land with every permit granted and zoning in 30 days (the build is what is measured).</summary>
    private static SimulationEngine PermittedEngine(int seed, LandTemplate template, string plot = "pad_a")
    {
        var engine = LandPermitTests.LandReadyEngine(seed);
        LandPermitTests.GrantEverything(engine);
        engine.UseFacilitiesCalibration(engine.Calibration.Facilities with
        {
            Land = engine.LandCalibration with { Permits = engine.LandCalibration.Permits with { ZoningDaysMin = 30, ZoningDaysMax = 30 } },
        });
        Assert.Equal(LandSiteReasons.PermitsMissing, engine.BuildLandSite(plot, template).ErrorKey);
        var applied = engine.ApplyLandPermits(plot, template);
        Assert.True(applied.Ok, applied.Message);
        int last = engine.PermitCases.Max(c => c.DecisionDay);
        engine.Advance(last - engine.State.Day);
        Assert.All(engine.PermitCases, c => Assert.Equal(PermitStatus.Approved, c.Status));
        return engine;
    }

    [Fact]
    public void Acceptance1_NoBuildWithoutItsPermits_EachMissingPermitNamed()
    {
        var engine = LandPermitTests.LandReadyEngine(81);
        var preview = engine.PreviewLandBuild("pad_a", LandTemplate.HfsPilotLot);
        Assert.Equal(LandSiteReasons.PermitsMissing, preview.Verdict);
        Assert.Equal(LandPermitSystem.SiteKinds, preview.MissingPermits);
        decimal cash = engine.FinanceManager.Balance;
        var refused = engine.BuildLandSite("pad_a", LandTemplate.HfsPilotLot);
        Assert.Equal(LandSiteReasons.PermitsMissing, refused.ErrorKey);
        foreach (var kind in LandPermitSystem.SiteKinds)
            Assert.Contains(kind.ToString(), refused.Message);
        Assert.Equal(cash, engine.FinanceManager.Balance);
        Assert.Empty(engine.ConstructionProjects);
        Assert.True(engine.FindPlot("pad_a")?.IsFree ?? true);
        // A refused permit leaves it missing.
        LandPermitTests.GrantEverything(engine);
        Assert.True(engine.ApplyLandPermits("pad_a", LandTemplate.HfsPilotLot).Ok);
        var discharge = engine.PermitCases.Single(c => c.Kind == PermitKind.Discharge);
        discharge.OutcomeDraw = 0.0; discharge.RefusalProbability = 1.0; // forced refusal
        engine.Advance(engine.PermitCases.Max(c => c.DecisionDay) - engine.State.Day);
        Assert.Equal(new[] { PermitKind.Discharge }, engine.PreviewLandBuild("pad_a", LandTemplate.HfsPilotLot).MissingPermits);
    }

    [Fact]
    public void AManagedHfsLot_IsBuiltPhaseByPhase_PaidAsCapex_AndHandsOverItsTanksAsThePhasesEnd()
    {
        var engine = PermittedEngine(82, LandTemplate.HfsLot);
        var preview = engine.PreviewLandBuild("pad_a", LandTemplate.HfsLot);
        Assert.Equal("", preview.Verdict);
        Assert.Equal(engine.LandCalibration.Build.HfsLotCapexNok + engine.FindPlot("pad_a")!.BlastCostNOK, preview.BudgetNok);
        Assert.Equal(preview.FirstTanksDay + preview.CycleDays, preview.FirstHarvestDay);
        decimal capex0 = engine.FinancialStatements.LifetimeCapex;
        var built = engine.BuildLandSite("pad_a", LandTemplate.HfsLot);
        Assert.True(built.Ok, built.Message);
        var site = engine.FindLandSite(built.Id)!;
        var lot = Assert.Single(site.Lots);
        var project = engine.ConstructionProjects.Single(p => p.Id == lot.ConstructionProjectId);
        Assert.Equal((site.Id, FacilityAssetKind.LandSite, ContractForm.Managed), (project.UnitId, project.AssetKind, project.Contract));
        Assert.Equal(site.Id, engine.FindPlot("pad_a")!.OwnerUnitId);
        Assert.False(site.IsOperational);
        Assert.Equal(LandSiteReasons.NotOperational, engine.StockLandTank(site.Id, "t01", 1000).ErrorKey);

        // Run to the first tanks: every day the capex the ledger booked is the project's money paid.
        var views = new List<(int Day, LandConstructionView View)>();
        while (lot.FirstTanksDay < 0 && engine.State.Day < built.Value2 + 400)
        {
            engine.Advance(1);
            Assert.Equal(capex0 + project.PaidNOK, engine.FinancialStatements.LifetimeCapex);
            views.Add((engine.State.Day, engine.LandConstructionViews().Single()));
        }
        int firstIndex = project.Phases.FindIndex(p => p.Key == LandConstructionPhases.FirstTanks);
        Assert.Equal(project.PhaseCompletedDays[firstIndex], lot.FirstTanksDay);
        Assert.Equal(6, lot.ReadyTankCount); // half the grow-out tanks ([L:R1])
        Assert.True(site.IsOperational);
        Assert.True(engine.StockLandTank(site.Id, "t01", 50_000).Ok);
        Assert.Equal(LandSiteReasons.NotOperational, engine.StockLandTank(site.Id, "t07", 50_000).ErrorKey);
        // The views walked the phases in order, progress never fell, and the sea works stood still in the closed season.
        Assert.Equal(project.Phases.Take(firstIndex + 2).Select(p => p.Key),
            views.Select(v => v.View.PhaseKey).Where(k => k != "").Distinct());
        for (int i = 1; i < views.Count; i++) Assert.True(views[i].View.Progress >= views[i - 1].View.Progress - 1e-12);
        Assert.All(views.Where(v => v.View.Suspended), v => Assert.Equal("sea_works", v.View.PhaseKey));
        Assert.All(views.Where(v => v.View.PhaseKey == "sea_works" && v.View.Status == "Building"),
            v => Assert.Equal(SimCalendar.Month(v.Day) is >= 4 and <= 6, v.View.Suspended));
        Assert.All(views, v => Assert.InRange(v.View.PhaseProgress, 0.0, 1.0));

        // Complete: every tank ready, the money paid on the asset register from that day.
        while (project.Status != ConstructionStatus.Complete && engine.State.Day < built.Value2 + 800) engine.Advance(1);
        Assert.Equal(ConstructionStatus.Complete, project.Status);
        engine.Advance(1);
        Assert.Equal(14, lot.ReadyTankCount);
        Assert.Equal(project.PhaseCompletedDays[^1], lot.CompleteDay);
        var asset = Assert.Single(engine.State.FacilityAssets, a => a.UnitId == site.Id);
        Assert.Equal((project.PaidNOK, project.PhaseCompletedDays[^1]), (asset.CapexNok, asset.StartDay));
        Assert.True(project.PaidNOK > preview.BudgetNok, "a managed build pays its overrun");
        Assert.True(engine.StockLandTank(site.Id, "t07", 50_000).Ok);
        _output.WriteLine($"planned first tanks day {preview.FirstTanksDay}, built {lot.FirstTanksDay}; planned complete {preview.CompleteDay}, built {lot.CompleteDay}; " +
                          $"paid {project.PaidNOK:N0} of {preview.BudgetNok:N0} (p50 overrun {preview.OverrunP50Nok:N0}, p90 {preview.OverrunP90Nok:N0})");
    }

    [Fact]
    public void Acceptance5_CancellingKeepsTheSunkCost_AndFreesThePad()
    {
        var engine = PermittedEngine(83, LandTemplate.HfsPilotLot);
        decimal capex0 = engine.FinancialStatements.LifetimeCapex;
        var built = engine.BuildLandSite("pad_a", LandTemplate.HfsPilotLot, ContractForm.Turnkey);
        Assert.True(built.Ok, built.Message);
        var site = engine.FindLandSite(built.Id)!;
        var project = engine.ConstructionProjects.Single();
        engine.Advance(200); // into the second phase
        decimal paid = project.PaidNOK;
        decimal cash = engine.FinanceManager.Balance;
        Assert.True(paid > 0m);
        var cancelled = engine.CancelLandConstruction(project.Id);
        Assert.True(cancelled.Ok, cancelled.Message);
        Assert.Equal(ConstructionStatus.Cancelled, project.Status);
        Assert.Null(engine.FindLandSite(site.Id));
        Assert.True(engine.FindPlot("pad_a")!.IsFree);
        Assert.Equal(cash, engine.FinanceManager.Balance);                           // nothing refunded
        Assert.Equal(capex0 + paid, engine.FinancialStatements.LifetimeCapex);      // the capex stays booked
        engine.Advance(400);
        Assert.Equal(paid, project.PaidNOK);                                         // no later phase is paid
        Assert.Equal(capex0 + paid, engine.FinancialStatements.LifetimeCapex);
        Assert.Empty(engine.State.FacilityAssets.Where(a => a.UnitId == site.Id));
        Assert.Equal(LandSiteReasons.NotReady, engine.CancelLandConstruction(project.Id).ErrorKey);
    }

    [Fact]
    public void APilotLot_ExpandsToAFullLot_KeepingItsFishAndTanks()
    {
        var engine = PermittedEngine(84, LandTemplate.HfsPilotLot);
        var built = engine.BuildLandSite("pad_a", LandTemplate.HfsPilotLot, ContractForm.Turnkey);
        Assert.True(built.Ok, built.Message);
        var site = engine.FindLandSite(built.Id)!;
        while (!site.Lots[0].IsCommissioned) engine.Advance(10);
        Assert.Equal(5, site.Lots[0].ReadyTankCount);
        Assert.True(engine.StockLandTank(site.Id, "t01", 40_000).Ok);
        var fish = site.PopulationIn(site.Lots[0].Tanks[0])!;

        // The pilot's permits cover the pilot only: the expansion needs the full lot's.
        var preview = engine.PreviewLandExpansion(site.Id);
        Assert.Equal((LandBuildKind.PilotExpansion, LandTemplate.HfsLot), (preview.Kind, preview.Template));
        Assert.Equal(LandSiteReasons.PermitsMissing, preview.Verdict);
        Assert.Equal(engine.LandCalibration.Build.HfsPilotExpansionCapexNok, preview.BudgetNok); // no blasting, no sea works phase
        Assert.True(engine.ApplyLandPermits("pad_a", LandTemplate.HfsLot).Ok);
        engine.Advance(engine.PermitCases.Max(c => c.DecisionDay) - engine.State.Day);
        var expand = engine.ExpandLandSite(site.Id, ContractForm.Turnkey);
        Assert.True(expand.Ok, expand.Message);
        Assert.Equal(LandSiteReasons.NotReady, engine.ExpandLandSite(site.Id).ErrorKey);
        var project = engine.ConstructionProjects.Single(p => p.Id == expand.Id);
        Assert.DoesNotContain(project.Phases, p => p.Key == LandConstructionPhases.SeaWorks || p.Key == LandConstructionPhases.SiteWorks);
        while (project.Status != ConstructionStatus.Complete) engine.Advance(10);
        engine.Advance(1);
        var lot = Assert.Single(site.Lots);
        Assert.Equal((LandTemplate.HfsLot, 14, 14), (lot.Template, lot.Tanks.Count, lot.ReadyTankCount));
        Assert.Same(fish, site.PopulationIn(lot.Tanks[0]));
        Assert.Equal(engine.LandCalibration.Hfs.HeatPumpMWe, lot.HeatPumpMWe);
        Assert.Equal(engine.LandCalibration.Hfs.NameplateTonnesHogPerYear, site.CapacityTonnesPerYear);
    }

    [Fact]
    public void Acceleration_PaysItsShareOfTheRemainingPhase_AndCutsItsDays()
    {
        var engine = PermittedEngine(85, LandTemplate.HfsPilotLot);
        Assert.True(engine.BuildLandSite("pad_a", LandTemplate.HfsPilotLot).Ok);
        var project = engine.ConstructionProjects.Single();
        engine.Advance(20);
        int end = project.PhaseEndDay;
        int remaining = end - engine.State.Day;
        int phaseDays = project.PhaseDays;
        decimal phaseBudget = project.BudgetNok * (decimal)project.Current!.CapexShare;
        decimal paid = project.PaidNOK;
        var fast = engine.AccelerateLandConstruction(project.Id);
        Assert.True(fast.Ok, fast.Message);
        decimal expected = phaseBudget * (decimal)(0.15 * remaining / phaseDays);
        Assert.Equal(paid + expected, project.PaidNOK, 2);
        Assert.Equal(end - (int)Math.Round(remaining * 0.2, MidpointRounding.AwayFromZero), project.PhaseEndDay);
        Assert.Equal(LandSiteReasons.NotReady, engine.AccelerateLandConstruction(project.Id).ErrorKey);
    }

    [Fact]
    public void TheGrid_CarriesThreeFullLotsOnPadA_AndTheFourthNeedsAGridProject()
    {
        var engine = LandPermitTests.LandReadyEngine(86);
        var plot = engine.SitePlots.Single(p => p.Id == "pad_a");
        var site = new LandIntakeSite(plot.StaticLiftM, 0.0, 0.0, plot.PumpCapacityM3sPerLot);
        double perLot = LandConstructionPhases.DesignPowerMW(LandTemplate.HfsLot, engine.LandCalibration, site);
        _output.WriteLine($"HFS lot design load {perLot:F2} MW; pad_a grid {plot.GridCapacityMW} MW");
        Assert.InRange(perLot, 9.0, 13.0);     // 8 MW of heat pumps plus pumps, treatment, UV and base load
        Assert.True(3 * perLot <= plot.GridCapacityMW && 4 * perLot > plot.GridCapacityMW);

        Assert.True(engine.CommissionLandSite("pad_a", LandTemplate.HfsLot, 3).Ok);
        var ras = engine.LandSites.Single(s => s.PlotId == "pad_a");
        LandPermitTests.GrantEverything(engine);
        Assert.True(engine.ApplyLandPermits("pad_a", LandTemplate.HfsLot, 4).Ok);
        engine.Advance(engine.PermitCases.Max(c => c.DecisionDay) - engine.State.Day);
        var preview = engine.PreviewLandExpansion(ras.Id);
        Assert.Equal(LandSiteReasons.GridCapacity, preview.Verdict);
        Assert.Equal(LandSiteReasons.GridCapacity, engine.ExpandLandSite(ras.Id).ErrorKey);
        var grid = engine.StartGridProject("pad_a", LandPermitSystem.GridCapacity);
        Assert.True(grid.Ok, grid.Message);
        engine.Advance(engine.PermitCases.Single(c => c.Id == grid.Id).DecisionDay - engine.State.Day);
        Assert.Equal("", engine.PreviewLandExpansion(ras.Id).Verdict);
        Assert.True(engine.ExpandLandSite(ras.Id).Ok);
        Assert.Equal(LandSiteReasons.NotReady, engine.PreviewLandExpansion(ras.Id).Verdict); // the fourth lot is being built
        Assert.Equal(4, ras.Lots.Count);
    }

    [Fact]
    public void ReadingALandSiteMidConstruction_ChangesNothing_AndDrawsNothing()
    {
        var engine = PermittedEngine(88, LandTemplate.HfsLot);
        var built = engine.BuildLandSite("pad_a", LandTemplate.HfsLot);
        Assert.True(built.Ok, built.Message);
        var site = engine.FindLandSite(built.Id)!;
        var lot = site.Lots[0];
        // Run to the post-tick that hands over the first tanks, and read before the next land tick.
        while (!engine.IsLotCommissionedNow(lot)) engine.Advance(1);
        Assert.Equal(-1, lot.FirstTanksDay);                      // the land tick writes it tomorrow
        Assert.True(engine.LotFirstTanksDayNow(lot) >= 0);
        string before = SaveStateComparer.Serialize(engine);
        var streams = engine.Rng.CaptureStates();
        var stocking = engine.PreviewLandStocking(site.Id, "t01", 10_000);
        Assert.True(stocking.Allowed, stocking.Message);           // the read sees the handed-over tank
        Assert.Equal(LandSiteReasons.NotOperational, engine.PreviewLandStocking(site.Id, "t07", 10_000).Reason);
        engine.LandConstructionViews();
        engine.PreviewLandExpansion(site.Id);
        engine.QuoteLandPermits("pad_a", LandTemplate.HfsLot, 2);
        Assert.Equal(before, SaveStateComparer.Serialize(engine));
        Assert.Equal(streams, engine.Rng.CaptureStates());
        // The command syncs and stocks the same tank.
        Assert.True(engine.StockLandTank(site.Id, "t01", 10_000).Ok);
        Assert.Equal(engine.LotFirstTanksDayNow(lot), lot.FirstTanksDay);
    }

    [Fact]
    public void AGameMidConstruction_SavesAndResumesBitIdentically()
    {
        var engine = PermittedEngine(87, LandTemplate.HfsLot);
        Assert.True(engine.BuildLandSite("pad_a", LandTemplate.HfsLot).Ok);
        engine.Advance(300);
        string saved = SaveStateComparer.Serialize(engine);
        var resumed = SaveStateComparer.Resume(saved, events: false);
        engine.Advance(500);
        resumed.Advance(500);
        Assert.Equal(SaveStateComparer.Normalize(SaveStateComparer.Serialize(engine)),
            SaveStateComparer.Normalize(SaveStateComparer.Serialize(resumed)));
        Assert.Equal(engine.LandConstructionViews(), resumed.LandConstructionViews());
    }
}
