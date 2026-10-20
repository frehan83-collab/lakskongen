using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Farm.Land;
using LakskongenSim.Farm.Offshore;
using LakskongenSim.Finance;
using LakskongenSim.Tests.FacilityFinance;
using LakskongenSim.Tests.Farm;
using Xunit;
using StateHashProbe;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// Roadmap F2-31 (docs/FARM_TYPES_SPEC.md §6.1-6.2, §7.4), the engine side
/// of the family-first build flow: the build preview of a variant on a plot
/// (the lane's own preview, the company's capex, the capacity cost with the
/// licence, the first harvest, the payback year, the 36-month cash runway
/// and the bank's pre-filled loan), the site lens's plots and the diagnosis
/// strip's binding constraint. Every one is a read that changes and draws
/// nothing, so a ghost placement and a preview leave the engine's hash where
/// it was (acceptance 4).
/// </summary>
public class BuildFlowTests
{
    private readonly ITestOutputHelper _output;

    public BuildFlowTests(ITestOutputHelper output) => _output = output;

    private static string Snapshot(SimulationEngine engine)
        => SaveStateComparer.Normalize(SaveStateComparer.Serialize(engine));

    private static SimulationEngine Started(string scenarioId, int seed = 17)
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: seed);
        engine.Scenario.Unlock(scenarioId);
        Assert.True(engine.StartScenario(scenarioId).Ok);
        return engine;
    }

    // ── Acceptance 4: reads only ─────────────────────────────────────────

    [Fact]
    public void EveryPreviewPlotAndDiagnosis_ChangesNothing_AndDrawsNothing()
    {
        var engine = LandPermitTests.LandReadyEngine(31);
        engine.Research.ForceUnlock("deep-sea-farming");
        engine.Advance(3);
        string before = Snapshot(engine);
        var streams = engine.Rng.CaptureStates();
        string day = GoldenTraceState.Capture(engine).DayHash();

        int previews = 0;
        foreach (var (variant, plots) in new[]
                 {
                     (FarmTypeIds.PostSmolt, new[] { "pad_delta", "pad_a" }),
                     (FarmTypeIds.Hfs, new[] { "pad_a", "pad_b", "pad_delta" }),
                     (FarmTypeIds.Ras, new[] { "pad_a" }),
                     (FarmTypeIds.Of1, new[] { "off_1", "off_2", "hav_1" }),
                     (FarmTypeIds.Havfarm, new[] { "off_2" }),
                 })
        {
            foreach (var plot in plots)
            foreach (var contract in new[] { ContractForm.Managed, ContractForm.Turnkey })
            {
                var preview = engine.PreviewFarmBuild(variant, plot, contract,
                    SimulationEngine.FamilyOfVariant(variant) == FarmTypeIds.Hav ? 0.35 : 1.0, new[] { "environmental_survey" },
                    variant == FarmTypeIds.Hfs ? LandTemplate.HfsLot : null);
                Assert.True(preview.Ok, $"{variant} on {plot}: {preview.ReasonKey}");
                Assert.Equal(SimulationEngine.BuildRunwayMonths, preview.Economics.Runway.Count);
                previews++;
            }
        }
        var diagnosis = engine.GetBuildDiagnosis();
        Assert.Equal(3, engine.GetBuildPlots(FarmTypeIds.Land).Count);
        Assert.Equal(3, engine.GetBuildPlots(FarmTypeIds.Hav).Count);
        _output.WriteLine($"{previews} previews; binding constraint {diagnosis.Binding?.Key}");

        Assert.Equal(streams, engine.Rng.CaptureStates());
        Assert.Equal(day, GoldenTraceState.Capture(engine).DayHash());
        Assert.Equal(before, Snapshot(engine));
    }

    [Fact]
    public void ASjoVariantOrAnUnknownOne_HasNoPlotPreview()
    {
        var engine = LandPermitTests.LandReadyEngine(32);
        Assert.Equal(SimulationEngine.BuildFlowReasons.NotAPlotBuild, engine.PreviewFarmBuild(FarmTypeIds.OpenPen, "pad_a").ReasonKey);
        Assert.Equal(SimulationEngine.BuildFlowReasons.NotAPlotBuild, engine.PreviewFarmBuild(FarmTypeIds.ClosedPen, "pad_a").ReasonKey);
        Assert.Equal(SimulationEngine.BuildFlowReasons.UnknownVariant, engine.PreviewFarmBuild("salmon_castle", "pad_a").ReasonKey);
        Assert.False(engine.PreviewFarmBuild(FarmTypeIds.Hfs, "pad_a").Economics.Runway.Count == 0);
    }

    // ── Which cards the player may start (acceptance 3) ──────────────────

    [Fact]
    public void ACardIsStartable_ExactlyWhenItsBuildVerbWouldNotRefuseItAsLocked()
    {
        // A bare game: nothing off the fjord is open.
        var bare = OffshoreTestUnits.RichEngine(39);
        Assert.False(bare.CanStartFarmType(FarmTypeIds.Hfs));
        Assert.Equal(LandSiteReasons.NotUnlocked, bare.ApplyLandPermits("pad_a", LandTemplate.HfsPilotLot).ErrorKey);
        Assert.False(bare.CanStartFarmType(FarmTypeIds.Of1));
        Assert.Equal(SimulationEngine.OffshoreReasons.NotUnlocked, bare.BuildOffshoreUnit("off_1", OffshoreType.SemiSubmersible, ContractForm.Managed).ErrorKey);
        Assert.True(bare.CanStartFarmType(FarmTypeIds.OpenPen));

        // The land family open but no zoned free pad (the module holds the pre-zoned delta):
        // the catalogue says locked, yet the permit package zones the chosen pad.
        var land = LandPermitTests.LandReadyEngine(40);
        var hfs = land.FarmTypeStatus(FarmTypeIds.Hfs)!;
        Assert.False(hfs.Unlocked);
        Assert.Contains(hfs.Gates, g => g.Gate.Kind == FarmTypeGateKind.ZonedPad && !g.Met);
        Assert.True(land.CanStartFarmType(FarmTypeIds.Hfs));
        Assert.True(land.ApplyLandPermits("pad_a", LandTemplate.HfsPilotLot).Ok);

        // OF1 with every gate but the development licence F2-09 brings.
        var sea = OffshoreTestUnits.UnlockedForOf1(41);
        Assert.False(sea.FarmTypeStatus(FarmTypeIds.Of1)!.Unlocked);
        Assert.True(sea.CanStartFarmType(FarmTypeIds.Of1));
        Assert.True(sea.BuildOffshoreUnit("off_1", OffshoreType.SemiSubmersible, ContractForm.Managed).Ok);
        Assert.Equal(new[] { FarmTypeGateKind.ZonedPad, FarmTypeGateKind.Funds }, SimulationEngine.StartWaivedGates(FarmTypeIds.Land));
        Assert.Equal(new[] { FarmTypeGateKind.DevelopmentLicence, FarmTypeGateKind.Funds }, SimulationEngine.StartWaivedGates(FarmTypeIds.Hav));
    }

    // ── The land preview (acceptance 1) ──────────────────────────────────

    [Fact]
    public void TheHfsPilotOnPadA_PaysItsPhasesAfterThePermits_AndTheRunwayAddsUp()
    {
        var engine = LandPermitTests.LandReadyEngine(33, cash: 1_500_000_000m);
        engine.Advance(40);
        var preview = engine.PreviewFarmBuild(FarmTypeIds.Hfs, "pad_a");
        var land = preview.Land!;
        var e = preview.Economics;
        Assert.Equal(LandTemplate.HfsPilotLot, preview.Template);
        Assert.Equal(LandTemplate.HfsPilotLot, land.Template);

        // pad_a is not zoned: the build waits for the slowest needed case (zoning, 1-2 years).
        var needed = land.Permits.Where(p => p.Status == "needed").ToList();
        Assert.Contains(needed, p => p.Kind == PermitKind.Zoning);
        int wait = needed.Max(p => (p.Quote.MinDays + p.Quote.MaxDays) / 2);
        Assert.InRange(wait, 365, 730);
        Assert.Equal(wait, e.PermitWaitDays);
        Assert.Equal(engine.State.Day + wait, e.StartDay);
        Assert.Equal(land.FirstHarvestDay + wait, e.FirstHarvestDay);
        Assert.Equal(land.Payments.Sum(p => p.PaymentNok), e.CompanyCapexNok);
        Assert.Equal(land.OverrunP50Nok, e.OverrunP50Nok);
        decimal fees = needed.Sum(p => p.Quote.FeeNok);
        Assert.Equal(fees, e.StartupCostsNok);
        Assert.Equal(land.ProductionTonnes, e.CapacityTonnesPerYear);
        Assert.Equal((double)(e.CompanyCapexNok + e.OverrunP50Nok + fees) / land.ProductionTonnes, e.CapacityCostNokPerTonneYear, 6);
        Assert.Equal(engine.FarmTypeStatus(FarmTypeIds.Hfs)!.Variant.Axis(FarmTypeIds.OpexPerKg).Median, e.FarmingCostNokPerKgHog);

        // 36 month ends, one calendar month apart.
        int today = engine.State.Day;
        Assert.Equal(Enumerable.Range(1, 36).Select(i => SimCalendar.AddMonths(today, i)), e.Runway.Select(r => r.Day));
        Assert.All(e.Runway, r => Assert.Equal(e.OperatingNokPerMonth, r.OperatingNok));
        // Every payment due inside the horizon is in the runway, after the permit wait.
        int end = e.Runway[^1].Day;
        decimal due = land.Payments.Where(p => p.StartDay + wait < end).Sum(p => p.PaymentNok) + fees;
        Assert.Equal(due, e.Runway.Sum(r => r.PaymentsNok));
        Assert.Equal(fees, e.Runway[0].PaymentsNok - land.Payments.Where(p => p.StartDay + wait < e.Runway[0].Day).Sum(p => p.PaymentNok));
        decimal overrun = land.Payments.Where(p => p.EndDay + wait < end).Sum(p => p.OverrunP50Nok);
        decimal cash0 = engine.FinanceManager.Balance;
        decimal expected = cash0 + 36m * e.OperatingNokPerMonth + e.Runway.Sum(r => r.NewUnitNok) - due - overrun;
        Assert.Equal(expected, e.Runway[^1].CashP50Nok, 6);
        Assert.All(e.Runway, r => Assert.True(r.CashP90Nok <= r.CashP50Nok, $"month {r.Month}: p90 above p50"));
        Assert.Equal(e.Runway.Min(r => r.CashP50Nok), e.LowestCashP50Nok);
        Assert.Equal(e.Runway.Min(r => r.CashP90Nok), e.LowestCashP90Nok);
        _output.WriteLine($"HFS pilot on pad_a: capex {e.CompanyCapexNok:N0}, overrun p50 {e.OverrunP50Nok:N0} p90 {e.OverrunP90Nok:N0}, " +
            $"wait {wait} d, first harvest day {e.FirstHarvestDay}, capacity cost {e.CapacityCostNokPerTonneYear:N0} NOK/t/yr, " +
            $"price {e.PriceNokPerKgHog:F1}, payback {e.PaybackYear}, runway low {e.LowestCashP50Nok:N0} (p90 {e.LowestCashP90Nok:N0}), loan {e.LoanNok:N0}");
    }

    [Fact]
    public void APostSmoltModule_HasNoHogPayback_ItGrowsFishForThePens()
    {
        var engine = LandPermitTests.LandReadyEngine(34, module: false);
        var e = engine.PreviewFarmBuild(FarmTypeIds.PostSmolt, "pad_delta").Economics;
        Assert.Equal(0m, e.YearlyMarginNok);
        Assert.Equal((-1, -1), (e.PaybackDay, e.PaybackYear));
        Assert.All(e.Runway, r => Assert.Equal(0m, r.NewUnitNok));
        Assert.True(e.CompanyCapexNok > 0m);
    }

    // ── The Hav preview (acceptance 6) ───────────────────────────────────

    [Fact]
    public void TheOf1Preview_PricesItsCapacityWithTheLicenceAndTheTeethingBudget()
    {
        var engine = OffshoreTestUnits.UnlockedForOf1(35);
        var alone = engine.PreviewFarmBuild(FarmTypeIds.Of1, "off_1", ContractForm.Managed, 1.0);
        var sea = alone.Offshore!;
        var e = alone.Economics;
        var axis = engine.FarmTypeStatus(FarmTypeIds.Of1)!.Variant.Axis(FarmTypeIds.CapexPerTonneYear);
        double capacity = engine.FarmTypeStatus(FarmTypeIds.Of1)!.Variant.Axis(FarmTypeIds.Capacity).Median!.Value;
        Assert.Equal(capacity, e.CapacityTonnesPerYear);
        Assert.Equal(sea.ConversionFeeNok, e.LicenceNok);
        Assert.Equal(sea.TeethingBudgetNok, e.StartupCostsNok);
        Assert.Equal(sea.CompanyCapexNok, e.CompanyCapexNok);
        Assert.Equal((double)(sea.CompanyCapexNok + sea.OverrunMedianNok + sea.TeethingBudgetNok + sea.ConversionFeeNok) / capacity,
            e.CapacityCostNokPerTonneYear, 6);
        // The unit's own capacity cost lies between the catalogue's median (no overrun) and its bad case.
        Assert.InRange(e.CapacityCostNokPerTonneYear, axis.Median!.Value, axis.Bad!.Value);
        Assert.True(sea.LicenceMarketValueNok > sea.ConversionFeeNok, "the licences are worth more than converting them costs");

        // The consortium pays and earns its 35 %; the unit's capacity cost is the same.
        double share = engine.Calibration.Facilities.Common.Catalog.Of1.ConsortiumEquityShare;
        var partner = engine.PreviewFarmBuild(FarmTypeIds.Of1, "off_1", ContractForm.Managed, share).Economics;
        Assert.Equal(share, partner.EquityShare);
        Assert.Equal((double)e.CompanyCapexNok * share, (double)partner.CompanyCapexNok, 0);
        Assert.Equal((double)e.LicenceNok * share, (double)partner.LicenceNok, 0);
        Assert.Equal(e.CapacityCostNokPerTonneYear, partner.CapacityCostNokPerTonneYear, 0);
        Assert.Equal((double)e.YearlyMarginNok * share, (double)partner.YearlyMarginNok, 0);
        Assert.True(partner.LowestCashP50Nok > e.LowestCashP50Nok, "the consortium leaves the company more cash");
    }

    // ── Payback ──────────────────────────────────────────────────────────

    [Fact]
    public void ThePaybackDay_IsTheFirstHarvestPlusWhatWasPutInOverTheYearlyMargin()
    {
        var engine = OffshoreTestUnits.UnlockedForOf1(36);
        var preview = engine.PreviewFarmBuild(FarmTypeIds.Of1, "off_1");
        var e = preview.Economics;
        double cost = e.FarmingCostNokPerKgHog!.Value;
        Assert.Equal((double)engine.MarketSimulation.WeeklyAvgPrice, e.PriceNokPerKgHog);
        decimal margin = (decimal)(e.CapacityTonnesPerYear * 1000.0 * (e.PriceNokPerKgHog - cost));
        Assert.Equal(margin, e.YearlyMarginNok);
        Assert.Equal(preview.Offshore!.FirstStockingDay + engine.DaysToHarvestFrom(preview.Offshore.FirstStockingDay, engine.HomeProductionArea),
            e.FirstHarvestDay);
        Assert.InRange(e.FirstHarvestDay - preview.Offshore.FirstStockingDay, 300, 700);
        if (margin > 0m)
        {
            decimal invested = e.CompanyCapexNok + e.OverrunP50Nok + e.StartupCostsNok + e.LicenceNok;
            int expected = e.FirstHarvestDay + (int)Math.Ceiling((double)(invested / margin) * SimCalendar.DaysPerYear);
            Assert.Equal(expected, e.PaybackDay);
            Assert.Equal(SimCalendar.Year(expected), e.PaybackYear);
        }
        else
        {
            Assert.Equal(-1, e.PaybackDay);
        }
        _output.WriteLine($"OF1 off_1: price {e.PriceNokPerKgHog:F2}, cost {cost:F2}, margin {e.YearlyMarginNok:N0}/yr, " +
            $"first harvest day {e.FirstHarvestDay}, payback {e.PaybackDay} ({e.PaybackYear})");
    }

    // ── The pre-filled loan ──────────────────────────────────────────────

    [Fact]
    public void ThePreFilledLoan_IsTheBanksRoom_UpToTheMedianLinesShortfall()
    {
        var engine = OffshoreTestUnits.UnlockedForOf1(37, cash: 200_000_000m);
        var e = engine.PreviewFarmBuild(FarmTypeIds.Of1, "off_1").Economics;
        decimal shortfall = Math.Max(0m, -Math.Min(engine.FinanceManager.Balance, e.LowestCashP50Nok));
        Assert.True(shortfall > 0m, "a 200 MNOK company cannot pay for the unit alone");
        decimal room = Math.Max(0m, engine.LoanCapacityNok() - engine.State.ActiveLoans.Sum(l => l.Principal));
        Assert.Equal(room, e.LoanRoomNok);
        Assert.Equal(Math.Min(room, shortfall), e.LoanNok);
        Assert.Equal(engine.Calibration.Facilities.Common.Depreciation.OffshoreLifeYears * 12, e.LoanTermMonths);
        Assert.True(e.Runway[0].CashWithLoanNok > e.Runway[0].CashP50Nok || e.LoanNok == 0m);
        _output.WriteLine($"200 MNOK company: shortfall {shortfall:N0}, room {room:N0}, loan {e.LoanNok:N0} at {e.LoanRate:P1} over {e.LoanTermMonths} months");
    }

    // ── The diagnosis strip (acceptance 5) ───────────────────────────────

    [Fact]
    public void ARedArea_MakesLiceTheBindingConstraint_WhichLandAndHavRemove()
    {
        var engine = Started("po3_red_sone");
        engine.TrafficLightSystem.SetZoneColor("po3_nord", ZoneColor.Red);
        var binding = engine.GetBuildDiagnosis().Binding!;
        Assert.Equal((SimulationEngine.BuildConstraintKeys.Lice, 1.0, 3), (binding.Key, binding.Score, binding.ProductionArea));
        Assert.Equal(new[] { FarmTypeIds.Land, FarmTypeIds.Hav }, binding.RelievedBy);

        engine.TrafficLightSystem.SetZoneColor("po3_nord", ZoneColor.Green);
        engine.TrafficLightSystem.SetZoneColor("po3_sor", ZoneColor.Green);
        var lice = engine.GetBuildDiagnosis().Constraints.Single(c => c.Key == SimulationEngine.BuildConstraintKeys.Lice);
        Assert.Equal(0.15, lice.Score);
    }

    [Fact]
    public void TheDiagnosis_ReadsCapacityTemperatureAndCapitalFromTheEngine_AndRanksThem()
    {
        var engine = Started("po3_red_sone");
        engine.TrafficLightSystem.SetZoneColor("po3_nord", ZoneColor.Green);
        engine.TrafficLightSystem.SetZoneColor("po3_sor", ZoneColor.Green);
        var area = engine.GetExpansionOverview().Areas.Single(a => a.ProductionArea == 3);
        var d = engine.GetBuildDiagnosis();
        Assert.Equal(d.Constraints.OrderByDescending(c => c.Score).ThenBy(c => c.Key, StringComparer.Ordinal), d.Constraints);
        var capacity = d.Constraints.Single(c => c.Key == SimulationEngine.BuildConstraintKeys.Capacity);
        Assert.Equal(area.StandingTonnes / area.LicenceMtbTonnes, capacity.Value, 9);
        var growth = engine.Growth.Law.Calibration;
        var temperature = d.Constraints.Single(c => c.Key == SimulationEngine.BuildConstraintKeys.Temperature);
        Assert.Equal(area.SeaTemperatureMaxC, temperature.Value);
        Assert.Equal(Math.Clamp((area.SeaTemperatureMaxC - growth.OptimalTemperatureC) / (growth.UpperGrowthLimitC - growth.OptimalTemperatureC), 0, 1),
            temperature.Score, 12);
        Assert.Equal(new[] { FarmTypeIds.Land }, temperature.RelievedBy);
        // The cheapest stage-1 rung off the fjord is the post-smolt module (110 MNOK); a family farm's cash is far from it.
        var capital = d.Constraints.Single(c => c.Key == SimulationEngine.BuildConstraintKeys.Capital);
        double rung = (double)engine.Calibration.Facilities.Common.Catalog.PostSmolt.ModuleCapexNok;
        Assert.Equal(rung, capital.Reference);
        double cash = (double)engine.FinanceManager.Balance;
        Assert.Equal(cash >= rung ? 0.0 : 1.0 - Math.Max(0.0, cash) / rung, capital.Score, 12);
        Assert.Empty(capital.RelievedBy);
        foreach (var c in d.Constraints)
            _output.WriteLine($"{c.Key}: score {c.Score:F3}, area {c.ProductionArea}, value {c.Value:F3}, reference {c.Reference:F1}");
    }

    // ── The site lens ────────────────────────────────────────────────────

    [Fact]
    public void TheSiteLens_ReadsThePadsAndTheSlots_TheSlotsSeaFromTheSeaModel()
    {
        var engine = LandPermitTests.LandReadyEngine(38, module: false);
        var pads = engine.GetBuildPlots(FarmTypeIds.Land);
        Assert.Equal(new[] { "pad_a", "pad_b", "pad_delta" }, pads.Select(p => p.Id).OrderBy(i => i, StringComparer.Ordinal));
        var delta = pads.Single(p => p.Id == "pad_delta");
        var a = pads.Single(p => p.Id == "pad_a");
        Assert.True(delta.PreZoned && delta.Zoned && !a.Zoned && !a.PreZoned);
        Assert.Equal(engine.EffectiveGridCapacityMW("pad_a"), a.GridCapacityMW);
        var padA = engine.SitePlots.Single(p => p.Id == "pad_a");
        Assert.Equal((padA.BlastCostNOK, padA.NatureSensitivity, padA.DeepIntakeReachM, padA.PumpCapacityM3sPerLot, padA.PhaseLots),
            (a.BlastCostNok, a.NatureSensitivity, a.DeepIntakeReachM, a.PumpCapacityM3sPerLot, a.GrowOutLots));
        Assert.True(a.BlastCostNok > 0m && a.NatureSensitivity > 0.0 && a.PumpCapacityM3sPerLot > 0.0);
        Assert.Equal("", a.Exposure);

        var slots = engine.GetBuildPlots(FarmTypeIds.Hav);
        var cal = engine.Calibration.Facilities.Offshore;
        foreach (var slot in slots)
        {
            var plot = cal.Plots.For(slot.Id);
            Assert.Equal(plot.Hs100M, slot.Hs100M);
            Assert.Equal(cal.Metocean.ReferenceMonthlyMeanHsM(0) * plot.HsScale, slot.WinterMeanHsM, 12);
            Assert.Equal(cal.Metocean.ReferenceMonthlyMeanHsM(6) * plot.HsScale, slot.SummerMeanHsM, 12);
            Assert.True(slot.WinterMeanHsM > slot.SummerMeanHsM);
            Assert.True(slot.LiceExternalMin <= slot.LiceExternalMax);
            Assert.NotEqual("", slot.Exposure);
        }
        Assert.Empty(engine.GetBuildPlots(FarmTypeIds.Sjo));
    }
}
