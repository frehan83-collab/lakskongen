using System;
using System.Linq;
using LakskongenSim.Calibration;
using LakskongenSim.Core;
using LakskongenSim.Data;
using LakskongenSim.Farm;
using LakskongenSim.Farm.Land;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// The land boards' reads for F2-32 (roadmap F2-15, docs/FARM_TYPES_SPEC.md
/// §6.5): each tank's water ring against its comfort band and the constraint
/// that binds it, each lot's worst tank, the Vannstrategi presets with the
/// engine's projection (kWh/kg, growth, maturation risk), the post-smolt
/// planner. The bridge projects these verbatim (GameBridge.LandSites.cs).
/// </summary>
public class LandBoardTests
{
    private readonly ITestOutputHelper _output;
    public LandBoardTests(ITestOutputHelper output) => _output = output;

    private static readonly LandCalibration Cal = CalibrationRegistry.Default.Facilities.Land;

    [Fact]
    public void TheWaterRing_HasFiveSegments_EachAgainstItsBand_AndTheMostLoadedOneBinds()
    {
        var calm = LandStatus.Evaluate(8.0, 6.0, 0.5, 12.0, 12.0, 30.0, 85.0, Cal);
        Assert.Equal(LandConstraints.All, calm.Segments.Select(s => s.Key));
        Assert.Equal(LandStatusLevel.Ok, calm.Level);
        Assert.Equal(LandConstraints.Oxygen, calm.Binding);            // 7/8 outloads 30/50
        Assert.Equal(Cal.Status.DoWarnMgL / 8.0, calm.Load, 12);
        var dense = LandStatus.Evaluate(8.0, 6.0, 0.5, 12.0, 12.0, 45.0, 85.0, Cal);
        Assert.Equal((LandConstraints.Density, LandStatusLevel.Ok), (dense.Binding, dense.Level)); // 45/50 binds, still in band
        Assert.Equal(45.0 / Cal.Density.KneeKgM3, dense.Load, 12);

        // Each segment's edges: ok, warn past the ok edge, critical past the critical edge.
        Assert.Equal(LandStatusLevel.Warn, Seg(LandStatus.Evaluate(6.0, 6, 0.5, 12, 12, 30, 85, Cal), LandConstraints.Oxygen).Level);
        Assert.Equal(LandStatusLevel.Critical, Seg(LandStatus.Evaluate(4.9, 6, 0.5, 12, 12, 30, 85, Cal), LandConstraints.Oxygen).Level);
        Assert.Equal(LandStatusLevel.Warn, Seg(LandStatus.Evaluate(8, 13, 0.5, 12, 12, 30, 85, Cal), LandConstraints.Co2).Level);
        Assert.Equal(LandStatusLevel.Critical, Seg(LandStatus.Evaluate(8, 21, 0.5, 12, 12, 30, 85, Cal), LandConstraints.Co2).Level);
        Assert.Equal(LandStatusLevel.Warn, Seg(LandStatus.Evaluate(8, 6, 2.5, 12, 12, 30, 85, Cal), LandConstraints.Tan).Level);
        Assert.Equal(LandStatusLevel.Critical, Seg(LandStatus.Evaluate(8, 6, 4.1, 12, 12, 30, 85, Cal), LandConstraints.Tan).Level);
        Assert.Equal(LandStatusLevel.Warn, Seg(LandStatus.Evaluate(8, 6, 0.5, 13.5, 12, 30, 85, Cal), LandConstraints.Temperature).Level);
        Assert.Equal(LandStatusLevel.Critical, Seg(LandStatus.Evaluate(8, 6, 0.5, 9.0, 12, 30, 85, Cal), LandConstraints.Temperature).Level);
        Assert.Equal(LandStatusLevel.Warn, Seg(LandStatus.Evaluate(8, 6, 0.5, 12, 12, 70, 85, Cal), LandConstraints.Density).Level);
        Assert.Equal(LandStatusLevel.Critical, Seg(LandStatus.Evaluate(8, 6, 0.5, 12, 12, 86, 85, Cal), LandConstraints.Density).Level);

        // CO2 at 18 mg/L binds a tank otherwise in its bands, and makes it a warning.
        var co2 = LandStatus.Evaluate(8.0, 18.0, 0.5, 12.0, 12.0, 40.0, 85.0, Cal);
        Assert.Equal((LandConstraints.Co2, LandStatusLevel.Warn), (co2.Binding, co2.Level));
    }

    private static LandSegment Seg(LandTankStatus status, string key) => status.Segments.Single(s => s.Key == key);

    [Fact]
    public void TheBoard_ListsEveryLotInOrder_WithItsWorstTankAndTheBindingConstraint_AndReadsNothingIntoTheSave()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 480);
        engine.ReceiveLoan(5_000_000_000m);
        var site = engine.FindLandSite(engine.CommissionLandSite("pad_a", LandTemplate.HfsLot, 4, "Storholmen").Id)!;
        site.Competence.CompletedCycles = 3;
        foreach (var lot in site.Lots)
            Assert.True(engine.StockLandTank(site.Id, lot.Tanks[0].Id, engine.PreviewLandStocking(site.Id, lot.Tanks[0].Id, 0).MaxSmolts).Ok);
        engine.Advance(150);
        string saved = SaveStateComparer.Serialize(engine);
        var board = engine.LandBoardOf(site.Id)!;
        Assert.Equal(saved, SaveStateComparer.Serialize(engine));
        Assert.Equal(site.Lots.Select(l => l.Id), board.Lots.Select(l => l.LotId));
        Assert.Equal(4, board.Lots.Count);                                  // 4 lot glyphs on the default view (≤ 12)
        foreach (var (lot, view) in site.Lots.Zip(board.Lots))
        {
            Assert.Equal(lot.Tanks.Select(t => t.Id), view.Tanks.Select(t => t.TankId));   // the 3D layout order
            var stocked = view.Tanks.Where(t => t.Fish > 0).ToList();
            var worst = stocked.OrderByDescending(t => t.Status.Level).ThenByDescending(t => t.Status.Load).First();
            Assert.Equal((worst.TankId, worst.Status.Binding, worst.Status.Level), (view.WorstTankId, view.Binding, view.Level));
            foreach (var t in view.Tanks)
            {
                var tank = site.FindTank(t.TankId)!;
                var expected = LandStatus.Evaluate(tank.DoMgL, tank.Co2MgL, tank.TanMgL, tank.TemperatureC, site.TargetWaterTemp,
                    t.DensityKgM3, Cal.Hfs.MaxDensityKgM3, Cal);
                Assert.Equal(expected.Binding, t.Status.Binding);
                Assert.Equal(expected.Segments, t.Status.Segments);
            }
            var cohort = stocked.Single();
            Assert.Equal(site.FindTank(cohort.TankId)!.Plan!.PlanWeightKg, cohort.PlanWeightKg);
            Assert.InRange(cohort.ShareOfPlan, 0.85, 1.05);
        }
        Assert.Null(engine.LandBoardOf("ras_99"));
    }

    [Fact]
    public void ThePresets_AreTheEnginesProjection_OfEachStrategy_AndApplyingOneSetsIt()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 481);
        engine.ReceiveLoan(5_000_000_000m);
        engine.State.HomeProductionArea = 5;
        var site = engine.FindLandSite(engine.CommissionLandSite("pad_a", LandTemplate.HfsLot, 1, "Storholmen").Id)!;
        var streams = engine.Rng.CaptureStates();
        string saved = SaveStateComparer.Serialize(engine);
        var presets = engine.VannstrategiPresetsOf(site.Id);
        Assert.Equal(saved, SaveStateComparer.Serialize(engine));
        Assert.Equal(streams, engine.Rng.CaptureStates());
        Assert.Same(presets, engine.VannstrategiPresetsOf(site.Id));          // cached for the day
        Assert.Equal(SimulationEngine.VannstrategiPresetIds, presets.Select(p => p.Id));
        var v = Cal.Vannstrategi;
        Assert.Equal((v.Sparsom.Reuse, v.Sparsom.SetpointC, false), (presets[0].Strategy.Reuse, presets[0].Strategy.SetpointC, presets[0].Strategy.BlendTowardSetpoint));
        Assert.Equal((v.Balansert.Reuse, v.Balansert.SetpointC, true), (presets[1].Strategy.Reuse, presets[1].Strategy.SetpointC, presets[1].Strategy.BlendTowardSetpoint));
        Assert.Equal((v.HoyTetthet.Reuse, v.HoyTetthet.SetpointC, false), (presets[2].Strategy.Reuse, presets[2].Strategy.SetpointC, presets[2].Strategy.BlendTowardSetpoint));
        foreach (var p in presets)
            _output.WriteLine($"{p.Id}: r {p.Strategy.Reuse} {p.Strategy.SetpointC} °C: {p.KWhPerKg:F2} kWh/kg ({p.EnergyNokPerKg:F2} NOK/kg), {p.GrowthTonnesPerYear:N0} t/yr, cycle {p.CycleDays} d, tanks {p.MeanTankC:F2} °C, maturation index {p.MaturationIndex:F2} → {p.MaturationRisk:P0} of the fish");

        // Each is the projector's run of its strategy on the site's pad, water and plan.
        var balansert = presets[1];
        var plan = LandStockingPlan.For(Cal.Hfs, Cal.Production) with
        {
            TemperatureC = balansert.Strategy.SetpointC,
            GrowthMultiplier = engine.LandGrowthMultiplierOf(site) * SmoltStrainRegistry.Get("standard").BuildGenome().GetGrowthMultiplier(),
        };
        var run = LandLotProjector.Run(new LandProjectionSettings
        {
            Template = LandTemplate.HfsLot,
            Strategy = balansert.Strategy,
            Site = engine.LandIntakeOf(site),
            HeatRecovery = true,
            ProductionArea = 5,
            StartDay = engine.State.Day,
            Days = SimCalendar.DaysPerYear,
            Plan = plan,
            RecordDays = true,
        }, Cal, engine.Growth.Law);
        Assert.Equal(run.KWhPerKgGrowth, balansert.KWhPerKg, 9);
        Assert.Equal(run.NetGrowthKg / 1000.0, balansert.GrowthTonnesPerYear, 9);
        Assert.Equal(LandStockingPlanner.Project(engine.Growth.Law, plan).CycleDays, balansert.CycleDays);
        double index = LandMaturation.IndexOverCycle(run.Daily.Select(d => d.TankC).ToList(), balansert.CycleDays, Cal.Maturation);
        Assert.Equal(index, balansert.MaturationIndex, 12);
        Assert.Equal(Cal.Maturation.MaleShare * Math.Min(index, Cal.Maturation.MaxMaleShare), balansert.MaturationRisk, 12);

        // Warmer water: faster fish, more maturation; the deep, sparing preset uses the least energy.
        Assert.True(presets[2].CycleDays < presets[0].CycleDays);
        Assert.True(presets[2].MaturationRisk > presets[1].MaturationRisk && presets[1].MaturationRisk > presets[0].MaturationRisk);
        Assert.All(presets, p => Assert.InRange(p.KWhPerKg, 3.0, 12.0));

        // Applying a preset sets the strategy and marks it active.
        Assert.DoesNotContain(presets, p => p.Active && p.Id == "hoy_tetthet");
        Assert.True(engine.ApplyVannstrategiPreset(site.Id, "hoy_tetthet").Ok);
        Assert.Equal((0.80, 1.0, 13.0, false), (site.RecirculationRate, site.DeepIntakeShare, site.TargetWaterTemp, site.BlendTowardSetpoint));
        Assert.Single(engine.VannstrategiPresetsOf(site.Id), p => p.Active).Id.Equals("hoy_tetthet");
        Assert.Equal("hoy_tetthet", engine.VannstrategiPresetsOf(site.Id).Single(p => p.Active).Id);
        Assert.Equal(LandSiteReasons.OutOfRange, engine.ApplyVannstrategiPreset(site.Id, "turbo").ErrorKey);
        Assert.Equal(LandSiteReasons.NotFound, engine.ApplyVannstrategiPreset("ras_99", "sparsom").ErrorKey);

        // A post-smolt module keeps its loop's reuse under every preset.
        var module = engine.FindLandSite(engine.CommissionLandSite("pad_delta", LandTemplate.PostSmoltModule, 1, "Elveøyra").Id)!;
        Assert.All(engine.VannstrategiPresetsOf(module.Id), p => Assert.Equal(module.RecirculationRate, p.Strategy.Reuse));
    }

    [Fact]
    public void ThePostSmoltPlanner_ShowsTiersSalinityAndTheDestinationsHeadroom()
    {
        var engine = new SimulationEngine { FinanceManager = new LakskongenSim.Finance.FinanceManager(5_000_000_000m) };
        engine.Initialize(enableEvents: false, seed: 482);
        var site = engine.FindLandSite(engine.CommissionLandSite("pad_delta", LandTemplate.PostSmoltModule, 1, "Elveøyra").Id)!;
        engine.Advance(61);
        var sea = new SeaSite("sea_ps", "zone-ps") { MABTonnes = 500 };
        engine.State.SeaSites.Add(sea);
        Assert.True(engine.StockLandTank(site.Id, site.Lots[0].Tanks[0].Id, 40_000).Ok);
        Assert.True(engine.SetSalinity(site.Id, 30.0).Ok);
        engine.Advance(31);
        var fish = site.PopulationIn(site.Lots[0].Tanks[0])!;
        foreach (var c in fish.Cohorts) c.MeanWeightKg = 0.45;
        var planner = engine.PostSmoltPlannerOf(site.Id)!;
        Assert.Equal((30.0, true), (planner.SalinityPpt, planner.HasRiverIntake));
        Assert.Equal(30.0 / 35.0, planner.SulphateFactor, 12);
        var tank = planner.Tanks.First();
        Assert.Equal((fish.TotalCount, SmoltQualityTier.SuperSmolt, 0.45), (tank.Fish, tank.Tier, Math.Round(tank.MeanWeightKg, 9)));
        Assert.Equal(Cal.PostSmolt.PlanHarvestWeightKg, tank.TargetWeightKg);
        Assert.Equal((decimal)(fish.TotalCount * 0.45) * Cal.Smolt.SalePriceNokPerKgLive, tank.SaleValueNok);
        var dest = Assert.Single(planner.Destinations);
        Assert.Equal((sea.Id, 500.0, 500.0), (dest.SeaSiteId, dest.MtbTonnes, dest.HeadroomTonnes));
        Assert.True(engine.TransferTankToSea(site.Id, tank.TankId, sea.Id, 0).Ok);
        var booked = engine.PostSmoltPlannerOf(site.Id)!;
        Assert.Equal(fish.TotalCount * 0.45 / 1000.0, booked.Destinations.Single().PendingTonnes, 6);
        Assert.Equal(sea.Id, booked.Tanks.First().TransferDestinationId);
    }
}
