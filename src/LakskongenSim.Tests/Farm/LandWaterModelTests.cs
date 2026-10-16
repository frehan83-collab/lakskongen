using System;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Calibration;
using LakskongenSim.Data;
using LakskongenSim.Farm.Land;
using Xunit;

namespace LakskongenSim.Tests.Farm
{
    /// <summary>
    /// Roadmap F2-12 (docs/FARM_TYPES_SPEC.md §3.3, §7.1): the land unit's
    /// water. Unit tests hold each mass balance to its closed form; the
    /// acceptance tests measure the spec's oxygen (2) and CO2 (3) criteria on a
    /// full HFS lot with the default pumps.
    /// </summary>
    public class LandWaterModelTests
    {
        private static readonly LandCalibration C = LandCalibration.Default;
        private static double DayM3(double m3s) => m3s * LandWaterModel.SecondsPerDay;

        // ── Flows ───────────────────────────────────────────────────────────

        [Fact]
        public void Flows_WithRoomInThePumps_SplitTheDesignLoopByTheReuse()
        {
            const double volume = 10_000.0;
            var flows = LandWaterModel.Flows(volume, C.Hfs, 0.70, pumpCapacityM3s: 100.0, C.Intake);
            double design = C.Hfs.LoopVolumesPerDay * volume;
            Assert.Equal(design, flows.DesignLoopM3, 9);
            Assert.Equal(0.30 * design, flows.NewM3, 6);
            Assert.Equal(0.70 * design, flows.ReusedM3, 6);
            Assert.Equal(C.Hfs.StripperDesignReuse * design, flows.StrippedM3, 6);
            Assert.False(flows.PumpLimited);
            Assert.Equal(design, flows.TotalM3, 6);
        }

        [Fact]
        public void Flows_BelowTheStripperDesign_StripAllReusedWater()
        {
            var flows = LandWaterModel.Flows(10_000.0, C.Hfs, 0.50, pumpCapacityM3s: 100.0, C.Intake);
            Assert.Equal(flows.ReusedM3, flows.StrippedM3, 9);
        }

        [Fact]
        public void Flows_WhenThePumpsCannotDeliver_KeepTheReuseShareAndTheCirculationFalls()
        {
            // Spec §3.3: "If the pumps cannot deliver (1 − r)·Q_tot, circulation falls".
            double volume = C.Hfs.GrowOutTanks * LandTank.VolumeOf(C.Hfs.GrowOutDiameterM, C.Hfs.WaterDepthM);
            var flows = LandWaterModel.Flows(volume, C.Hfs, 0.30, C.Energy.PumpCapacityM3sPerLot, C.Intake);
            Assert.True(flows.PumpLimited);
            Assert.Equal(DayM3(C.Energy.PumpCapacityM3sPerLot), flows.NewM3, 6);
            Assert.Equal(0.30, flows.ReusedM3 / flows.TotalM3, 9);
            Assert.True(flows.TotalM3 < flows.DesignLoopM3);
        }

        [Fact]
        public void Flows_OfAnEmptyLot_AreZero()
        {
            Assert.Equal(LandFlows.None, LandWaterModel.Flows(0.0, C.Hfs, 0.65, 5.0, C.Intake));
        }

        [Fact]
        public void Flows_ClampReuseToTheMaximum()
        {
            var flows = LandWaterModel.Flows(1_000.0, C.Ras, 1.0, 100.0, C.Intake);
            Assert.Equal(1.0 - C.Intake.MaxReuse, flows.NewM3 / flows.TotalM3, 9);
            Assert.True(flows.NewM3 > 0.0);
        }

        // ── Intake ──────────────────────────────────────────────────────────

        [Fact]
        public void IntakeBlend_IsLinearInTheDeepShare()
        {
            Assert.Equal(6.0, LandWaterModel.IntakeTemperatureC(6.0, 10.0, 0.0), 12);
            Assert.Equal(10.0, LandWaterModel.IntakeTemperatureC(6.0, 10.0, 1.0), 12);
            Assert.Equal(7.0, LandWaterModel.IntakeTemperatureC(6.0, 10.0, 0.25), 12);
            Assert.Equal(10.0, LandWaterModel.IntakeTemperatureC(6.0, 10.0, 3.0), 12);
        }

        [Fact]
        public void DeepShare_FixedOrBlendedTowardTheSetpoint()
        {
            var fixedW = new LandWaterStrategy(0.65, 0.3, 12.0);
            Assert.Equal(0.3, LandWaterModel.DeepShare(fixedW, 15.0, 10.0), 12);
            var blend = fixedW with { BlendTowardSetpoint = true };
            // Summer: 15 °C at 25 m, 10 °C at 95 m: 60 % deep water makes 12 °C.
            Assert.Equal(0.6, LandWaterModel.DeepShare(blend, 15.0, 10.0), 12);
            Assert.Equal(12.0, LandWaterModel.IntakeTemperatureC(15.0, 10.0, LandWaterModel.DeepShare(blend, 15.0, 10.0)), 12);
            // Winter: both colder than the setpoint, take the warmer (all deep).
            Assert.Equal(1.0, LandWaterModel.DeepShare(blend, 5.5, 10.0), 12);
            // An explicit target (the setpoint less the loop's heat gain).
            Assert.Equal(0.7, LandWaterModel.DeepShare(blend, 15.0, 10.0, 11.5), 12);
        }

        // ── One tank's mass balances ────────────────────────────────────────

        private static readonly TankFlows Hfs5000 = new(NewM3: 35_000.0, ReusedM3: 65_000.0, StrippedM3: 65_000.0);

        [Fact]
        public void Co2_IsTheSteadyStateMassBalance()
        {
            var load = new TankLoad(300_000.0, 2_400.0);
            var w = LandWaterModel.SolveTank(load, Hfs5000, 5_000.0, 12.0, 10.0, 35.0, C.Hfs, 0.0, 0.1, C);
            double fishO2 = C.Oxygen.KgO2PerKgFeed * 2_400.0 * 1000.0;
            double expected = (C.Intake.Co2MgL * 35_000.0 + C.Co2.Co2PerO2 * fishO2)
                / (35_000.0 + C.Hfs.StripEfficiency * 65_000.0);
            Assert.Equal(expected, w.Co2MgL, 9);
            Assert.Equal(fishO2 / 1000.0, w.FishO2Kg, 9);
        }

        [Fact]
        public void Co2_UnstrippedReusedWater_RaisesTheTank()
        {
            var load = new TankLoad(300_000.0, 2_400.0);
            var all = LandWaterModel.SolveTank(load, Hfs5000, 5_000.0, 12.0, 10.0, 35.0, C.Hfs, 0.0, 0.1, C);
            var part = LandWaterModel.SolveTank(load, Hfs5000 with { StrippedM3 = 40_000.0 }, 5_000.0, 12.0, 10.0, 35.0,
                C.Hfs, 0.0, 0.1, C);
            Assert.True(part.Co2MgL > all.Co2MgL);
        }

        [Fact]
        public void Oxygen_AtFullSupply_HoldsTheOutletAndInjectsTheBalance()
        {
            var load = new TankLoad(300_000.0, 2_400.0);
            var w = LandWaterModel.SolveTank(load, Hfs5000, 5_000.0, 12.0, 10.0, 35.0, C.Hfs, 0.0, 0.1, C);
            double doIn = WaterQuality.CalculateOxygenSaturation(10.0, 35.0);
            double doSat = WaterQuality.CalculateOxygenSaturation(12.0, 35.0);
            double outlet = C.Oxygen.TankOutletDoMgL;
            double need = C.Oxygen.KgO2PerKgFeed * 2_400.0 * 1000.0
                - 35_000.0 * (doIn - outlet) - C.Hfs.StripEfficiency * 65_000.0 * (doSat - outlet);
            Assert.Equal(outlet, w.DoMgL, 12);
            Assert.Equal(need / 1000.0, w.OxygenNeedKg, 9);
            Assert.Equal(w.OxygenNeedKg, w.OxygenInjectedKg, 12);
        }

        [Fact]
        public void Oxygen_WhenLiquidOxygenRunsShort_TheTankFallsToWhatTheSupplyHolds()
        {
            var load = new TankLoad(300_000.0, 2_400.0);
            var full = LandWaterModel.SolveTank(load, Hfs5000, 5_000.0, 12.0, 10.0, 35.0, C.Hfs, 0.0, 0.1, C);
            var half = LandWaterModel.SolveTank(load, Hfs5000, 5_000.0, 12.0, 10.0, 35.0, C.Hfs, 0.0, 0.1, C, 0.5);
            var none = LandWaterModel.SolveTank(load, Hfs5000, 5_000.0, 12.0, 10.0, 35.0, C.Hfs, 0.0, 0.1, C, 0.0);
            Assert.Equal(0.5 * full.OxygenNeedKg, half.OxygenInjectedKg, 9);
            Assert.True(half.DoMgL < full.DoMgL && none.DoMgL < half.DoMgL);
            Assert.True(none.DoMgL >= 0.0);
            // The balance closes: what the water brings and the injection
            // dissolve equals what the fish use.
            double doIn = WaterQuality.CalculateOxygenSaturation(10.0, 35.0);
            double doSat = WaterQuality.CalculateOxygenSaturation(12.0, 35.0);
            double supply = 35_000.0 * (doIn - half.DoMgL) + C.Hfs.StripEfficiency * 65_000.0 * (doSat - half.DoMgL)
                + half.OxygenInjectedKg * 1000.0;
            Assert.Equal(half.FishO2Kg * 1000.0, supply, 3);
        }

        [Fact]
        public void Tan_WithoutABiofilter_LeavesWithTheNewWater()
        {
            var load = new TankLoad(300_000.0, 2_400.0);
            var w = LandWaterModel.SolveTank(load, Hfs5000, 5_000.0, 12.0, 10.0, 35.0, C.Hfs, 0.0, 0.1, C);
            Assert.Equal(C.Tan.KgTanPerKgFeed * 2_400.0 * 1000.0 / 35_000.0, w.TanMgL, 9);
            Assert.Equal(0.0, w.TanRemovedKg);
            Assert.Equal(0.0, w.NitrificationO2Kg);
        }

        [Fact]
        public void Tan_ABiofilterWithRoom_RemovesItsPassShare_AndOneAtCapacity_RemovesItsCapacity()
        {
            var flows = new TankFlows(2_000.0, 60_000.0, 60_000.0);
            var load = new TankLoad(150_000.0, 1_200.0);
            double excreted = C.Tan.KgTanPerKgFeed * 1_200.0 * 1000.0;
            var room = LandWaterModel.SolveTank(load, flows, 2_500.0, 12.0, 10.0, 35.0, C.Ras, 1_000.0, 0.1, C);
            double tan = excreted / (2_000.0 + C.Tan.PassRemovalEfficiency * 60_000.0);
            Assert.Equal(tan, room.TanMgL, 9);
            Assert.Equal(C.Tan.PassRemovalEfficiency * tan * 60_000.0 / 1000.0, room.TanRemovedKg, 9);
            Assert.Equal(C.Oxygen.NitrificationGO2PerGTan * room.TanRemovedKg, room.NitrificationO2Kg, 9);

            var full = LandWaterModel.SolveTank(load, flows, 2_500.0, 12.0, 10.0, 35.0, C.Ras, 10.0, 0.1, C);
            Assert.Equal(10.0, full.TanRemovedKg, 9);
            Assert.Equal((excreted - 10_000.0) / 2_000.0, full.TanMgL, 9);
        }

        [Fact]
        public void Nitrate_RelaxesTowardTheDilutionSteadyState()
        {
            var flows = new TankFlows(2_000.0, 60_000.0, 60_000.0);
            var load = new TankLoad(150_000.0, 1_200.0);
            var day = LandWaterModel.SolveTank(load, flows, 2_500.0, 12.0, 10.0, 35.0, C.Ras, 1_000.0, 0.0, C);
            double steady = C.Intake.No3NMgL + day.TanRemovedKg * 1000.0 / 2_000.0;
            Assert.Equal(steady * (1.0 - Math.Exp(-2_000.0 / 2_500.0)), day.No3NMgL, 9);
            double no3 = 0.0;
            for (int d = 0; d < 60; d++)
                no3 = LandWaterModel.SolveTank(load, flows, 2_500.0, 12.0, 10.0, 35.0, C.Ras, 1_000.0, no3, C).No3NMgL;
            Assert.Equal(steady, no3, 6);
        }

        // ── Effects on the fish ─────────────────────────────────────────────

        [Fact]
        public void Co2Appetite_IsFullTo12_And06At20()
        {
            Assert.Equal(1.0, LandWaterModel.Co2AppetiteFactor(5.0, C.Co2));
            Assert.Equal(1.0, LandWaterModel.Co2AppetiteFactor(12.0, C.Co2));
            Assert.Equal(0.8, LandWaterModel.Co2AppetiteFactor(16.0, C.Co2), 12);
            Assert.Equal(0.6, LandWaterModel.Co2AppetiteFactor(20.0, C.Co2), 12);
            Assert.Equal(0.0, LandWaterModel.Co2AppetiteFactor(1000.0, C.Co2));
        }

        [Fact]
        public void TanAppetite_IsFullTo2_AndHalfAt4()
        {
            Assert.Equal(1.0, LandWaterModel.TanAppetiteFactor(2.0, C.Tan));
            Assert.Equal(0.5, LandWaterModel.TanAppetiteFactor(4.0, C.Tan), 12);
            Assert.Equal(0.0, LandWaterModel.TanAppetiteFactor(double.PositiveInfinity, C.Tan));
        }

        [Fact]
        public void TankAppetite_IsTheProductOfItsCo2AndTanFactors()
        {
            // A heavily fed tank on little new water and no biofilter.
            var flows = new TankFlows(30_000.0, 60_000.0, 60_000.0);
            var w = LandWaterModel.SolveTank(new TankLoad(300_000.0, 3_000.0), flows, 5_000.0, 12.0, 10.0, 35.0, C.Hfs, 0.0,
                0.1, C);
            Assert.True(w.TanMgL > C.Tan.AppetiteLimitMgL && w.Co2MgL > C.Co2.AppetiteFullMgL);
            Assert.InRange(w.AppetiteFactor, 0.2, 0.8);
            Assert.Equal(LandWaterModel.Co2AppetiteFactor(w.Co2MgL, C.Co2) * LandWaterModel.TanAppetiteFactor(w.TanMgL, C.Tan),
                w.AppetiteFactor, 12);
        }

        [Fact]
        public void DensityGrowth_IsFullTo50_And085At85()
        {
            Assert.Equal(1.0, LandWaterModel.DensityGrowthMultiplier(50.0, C.Density));
            Assert.Equal(0.85, LandWaterModel.DensityGrowthMultiplier(85.0, C.Density), 12);
            Assert.Equal(0.925, LandWaterModel.DensityGrowthMultiplier(67.5, C.Density), 12);
            Assert.True(LandWaterModel.DensityGrowthMultiplier(125.0, C.Density) < 0.85);
        }

        // ── F2-12 acceptance 2 and 3 on a full HFS lot ──────────────────────

        /// <summary>
        /// The weight and feed rate of the default plan's fish on the day their
        /// tank stands at <paramref name="density"/> (12 °C, continuous light,
        /// fed to appetite).
        /// </summary>
        internal static (double WeightKg, double FeedPerKg) PlanFishAt(double density, LandCalibration calibration)
        {
            var law = TgcGrowthLaw.Default;
            var plan = LandStockingPlan.For(calibration.Hfs, calibration.Production);
            var forecast = LandStockingPlanner.Project(law, plan);
            const double volume = 10_000.0;
            double perM3 = forecast.MaxSmolts(volume, calibration.Hfs.PlanPeakDensityKgM3) / volume;
            double w = plan.ReleaseWeightKg, cv = law.Calibration.SmoltWeightCv, alive = 1.0;
            var conditions = new GrowthConditions(plan.TemperatureC, DayLengthHours: TgcGrowthLaw.ContinuousLightHours);
            for (int d = 0; d < forecast.CycleDays; d++)
            {
                double fcr = calibration.Production.BiologicalFcrAt2Kg * law.FcrSizeFactor(w);
                var grown = law.GrowOneDay(w, cv, LifeStage.Seawater, conditions, fcr);
                alive *= forecast.DailySurvival;
                if (perM3 * alive * grown.WeightKg >= density)
                    return (grown.WeightKg, grown.EatenFeedKg / grown.WeightKg);
                w = grown.WeightKg;
                cv = grown.WeightCv;
            }
            throw new InvalidOperationException($"the plan never stands at {density} kg/m³");
        }

        /// <summary>Every tank of a full HFS lot at <paramref name="density"/> of fish eating <paramref name="feedPerKg"/>, on pad_a.</summary>
        internal static LandLotDayResult FullLotAt(double density, double feedPerKg, double reuse, LandCalibration calibration)
        {
            var lot = LandSiteTemplates.BuildLot("hfs_1", 1, LandTemplate.HfsLot, calibration);
            var loads = lot.Tanks.Select(t => new TankLoad(density * t.VolumeM3, density * t.VolumeM3 * feedPerKg)).ToList();
            var strategy = LandSiteTemplates.DefaultStrategy(calibration.Hfs) with { Reuse = reuse };
            return LandLotDaySolver.Solve(new LandLotDayInput(lot, strategy, LandTestSites.PadA(calibration),
                ShallowWaterC: calibration.Hfs.DefaultSetpointC, loads), calibration);
        }

        [Fact]
        public void F2_12_Acceptance3_Co2At60KgM3_12C_R065_DefaultPumps_Is10To15()
        {
            var (weight, feed) = PlanFishAt(60.0, C);
            var day = FullLotAt(60.0, feed, 0.65, C);
            Assert.Equal(12.0, day.Heat.TankC, 1);
            double co2 = day.Tanks[0].Co2MgL;
            Assert.InRange(weight, 3.0, 4.8);
            Assert.InRange(co2, 10.0, 15.0);
        }

        [Fact]
        public void F2_12_Acceptance3_Co2Exceeds15_At70And80KgM3()
        {
            var (_, feed) = PlanFishAt(60.0, C);
            double at60 = FullLotAt(60.0, feed, 0.65, C).Tanks[0].Co2MgL;
            double at70 = FullLotAt(70.0, feed, 0.65, C).Tanks[0].Co2MgL;
            double at80 = FullLotAt(80.0, feed, 0.65, C).Tanks[0].Co2MgL;
            Assert.True(at70 > 15.0, $"CO2 at 70 kg/m³ is {at70:F2} mg/L");
            Assert.True(at80 > 15.0, $"CO2 at 80 kg/m³ is {at80:F2} mg/L");
            Assert.True(at60 < at70 && at70 < at80);
        }

        [Fact]
        public void F2_12_Acceptance3_Co2Exceeds15_AboveR08_AndRisesWithReusePastTheStrippers()
        {
            var (_, feed) = PlanFishAt(60.0, C);
            double previous = FullLotAt(60.0, feed, 0.70, C).Tanks[0].Co2MgL;
            foreach (double r in new[] { 0.75, 0.80, 0.85, 0.90 })
            {
                double co2 = FullLotAt(60.0, feed, r, C).Tanks[0].Co2MgL;
                Assert.True(co2 > previous, $"CO2 at r {r} ({co2:F2}) does not rise above r {r - 0.05} ({previous:F2})");
                previous = co2;
            }
            Assert.True(FullLotAt(60.0, feed, 0.80, C).Tanks[0].Co2MgL > 15.0);
            Assert.True(FullLotAt(60.0, feed, 0.85, C).Tanks[0].Co2MgL > 15.0);
        }

        [Fact]
        public void F2_12_Acceptance2_LoxMovesUnder15Percent_FromR0ToR09_AtEqualDensity()
        {
            // Spec: "rises < 15% from r 0 to r 0.9 at equal density". Measured
            // both ways (the strippers give the reused water back oxygen), so
            // the test holds the change under 15 % in either direction.
            var (_, feed) = PlanFishAt(60.0, C);
            double lox0 = FullLotAt(60.0, feed, 0.0, C).LoxPurchasedKg;
            double lox9 = FullLotAt(60.0, feed, 0.9, C).LoxPurchasedKg;
            double change = lox9 / lox0 - 1.0;
            Assert.True(change < 0.15, $"LOX rises {change:P1} from r 0 to r 0.9");
            Assert.True(change > -0.15, $"LOX falls {change:P1} from r 0 to r 0.9");
        }

        [Fact]
        public void Solver_LeavesTheLotUntouched_AndApplyWritesTheDay()
        {
            var lot = LandSiteTemplates.BuildLot("hfs_1", 1, LandTemplate.HfsLot, C);
            var loads = lot.Tanks.Select(t => new TankLoad(50.0 * t.VolumeM3, 0.4 * t.VolumeM3)).ToList();
            var input = new LandLotDayInput(lot, LandSiteTemplates.DefaultStrategy(C.Hfs), LandTestSites.PadA(C), 14.0, loads);
            double co2Before = lot.Tanks[0].Co2MgL;
            var a = LandLotDaySolver.Solve(input, C);
            var b = LandLotDaySolver.Solve(input, C);
            Assert.Equal(co2Before, lot.Tanks[0].Co2MgL);
            Assert.Equal(a.Energy, b.Energy);
            Assert.Equal(a.Tanks, b.Tanks);
            LandLotDaySolver.Apply(lot, a);
            Assert.Equal(a.Tanks[0].Co2MgL, lot.Tanks[0].Co2MgL);
            Assert.Equal(a.Heat.TankC, lot.Tanks[3].TemperatureC);
            Assert.Equal(a.Tanks[5].AppetiteFactor, lot.Tanks[5].AppetiteFactor);
        }

        [Fact]
        public void Solver_LiquidOxygenShort_SharesTheShortfallAndLowersTheTanks()
        {
            var lot = LandSiteTemplates.BuildLot("hfs_1", 1, LandTemplate.HfsLot, C);
            var loads = lot.Tanks.Select(t => new TankLoad(60.0 * t.VolumeM3, 0.5 * t.VolumeM3)).ToList();
            var strategy = LandSiteTemplates.DefaultStrategy(C.Hfs);
            var full = LandLotDaySolver.Solve(new LandLotDayInput(lot, strategy, LandTestSites.PadA(C), 12.0, loads), C);
            var shortDay = LandLotDaySolver.Solve(new LandLotDayInput(lot, strategy, LandTestSites.PadA(C), 12.0, loads,
                OxygenAvailableKg: full.LoxPurchasedKg / 2.0), C);
            Assert.Equal(full.LoxPurchasedKg / 2.0, shortDay.LoxPurchasedKg, 6);
            Assert.All(shortDay.Tanks, t => Assert.True(t.DoMgL < C.Oxygen.TankOutletDoMgL));
            Assert.Equal(full.OxygenNeedKg, shortDay.OxygenNeedKg, 6);
        }
    }
}
