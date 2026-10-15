using System;
using LakskongenSim.Biology;
using LakskongenSim.Calibration;
using LakskongenSim.Farm.Land;
using Xunit;

namespace LakskongenSim.Tests.Farm
{
    /// <summary>
    /// Roadmap F2-12 acceptance 5 (docs/FARM_TYPES_SPEC.md §3.3 "Density"):
    /// a land stocking whose projected peak passes 85 kg/m³ ([L:R2]) is
    /// refused with <c>density_exceeded</c>, and the projection never lets a
    /// stocking through that the lot then grows past the limit.
    /// </summary>
    public class LandStockingPlannerTests
    {
        private static readonly LandCalibration C = LandCalibration.Default;
        private static readonly TgcGrowthLaw Law = TgcGrowthLaw.Default;
        private static readonly double GrowOutVolume = LandTank.VolumeOf(C.Hfs.GrowOutDiameterM, C.Hfs.WaterDepthM);
        private static LandStockingPlan Plan => LandStockingPlan.For(C.Hfs, C.Production);

        [Fact]
        public void F2_12_Acceptance5_AStockingProjectedAbove85KgM3_IsRefused()
        {
            var forecast = LandStockingPlanner.Project(Law, Plan);
            int room = forecast.MaxSmolts(GrowOutVolume, C.Hfs.MaxDensityKgM3);
            Assert.True(room > 0);

            var atLimit = LandStockingPlanner.Check(Law, Plan, GrowOutVolume, room, C.Hfs.MaxDensityKgM3);
            Assert.True(atLimit.Allowed);
            Assert.Equal("", atLimit.Reason);
            Assert.True(atLimit.PeakDensityKgM3 <= 85.0);

            var over = LandStockingPlanner.Check(Law, Plan, GrowOutVolume, room + 1, C.Hfs.MaxDensityKgM3);
            Assert.False(over.Allowed);
            Assert.Equal(LandStockingPlanner.DensityExceeded, over.Reason);
            Assert.Equal("density_exceeded", over.Reason);
            Assert.True(over.PeakDensityKgM3 > 85.0);
            Assert.Equal(room, over.MaxSmolts);

            // 1 % over the room is clearly refused, 1 % under clearly allowed.
            Assert.False(LandStockingPlanner.Check(Law, Plan, GrowOutVolume, (int)(room * 1.01), 85.0).Allowed);
            Assert.True(LandStockingPlanner.Check(Law, Plan, GrowOutVolume, (int)(room * 0.99), 85.0).Allowed);
        }

        [Fact]
        public void TheProjectedPeak_IsTheHarvestWeightTimesTheCycleSurvival()
        {
            var forecast = LandStockingPlanner.Project(Law, Plan);
            Assert.Equal(forecast.CycleDays, forecast.PeakDay);
            Assert.InRange(forecast.PeakKgPerSmolt, C.Hfs.PlanHarvestWeightKg * C.Production.CycleSurvival,
                (C.Hfs.PlanHarvestWeightKg + 0.05) * C.Production.CycleSurvival);
            Assert.Equal(C.Production.CycleSurvival, Math.Pow(forecast.DailySurvival, forecast.CycleDays), 9);
        }

        [Fact]
        public void AWarmerTank_GrowsTheCycleFaster_ButTheSamePeak()
        {
            var cold = LandStockingPlanner.Project(Law, Plan with { TemperatureC = 10.0 });
            var warm = LandStockingPlanner.Project(Law, Plan with { TemperatureC = 13.0 });
            Assert.True(warm.CycleDays < cold.CycleDays);
            // The same harvest weight; the last day's step overshoots it a little more in warm water.
            Assert.InRange(warm.PeakKgPerSmolt / cold.PeakKgPerSmolt, 0.98, 1.02);
        }

        [Fact]
        public void ZeroFish_AreOutOfRange()
        {
            var verdict = LandStockingPlanner.Check(Law, Plan, GrowOutVolume, 0, 85.0);
            Assert.False(verdict.Allowed);
            Assert.Equal(LandStockingPlanner.OutOfRange, verdict.Reason);
        }

        [Fact]
        public void PostSmoltTanks_AreHeldTo80KgM3()
        {
            var plan = LandStockingPlan.For(C.PostSmolt, C.Production);
            double volume = LandTank.VolumeOf(C.PostSmolt.GrowOutDiameterM, C.PostSmolt.WaterDepthM);
            var forecast = LandStockingPlanner.Project(Law, plan);
            int room = forecast.MaxSmolts(volume, C.PostSmolt.MaxDensityKgM3);
            Assert.Equal(80.0, C.PostSmolt.MaxDensityKgM3);
            Assert.False(LandStockingPlanner.Check(Law, plan, volume, room + 1, C.PostSmolt.MaxDensityKgM3).Allowed);
        }

        [Fact]
        public void TheCheckIsConservative_NoTankStockedToTheRoomPassesTheLimit()
        {
            // The projection grows the fish at full appetite with no density or
            // CO2 brake; the lot's fish grow no faster, so a tank stocked to the
            // room never stands above 85 kg/m³ on any day of a year.
            var settings = LandTestSites.SalmonEvolutionYear(C) with { PlanPeakDensityKgM3 = C.Hfs.MaxDensityKgM3 };
            var r = LandLotProjector.Run(settings, C);
            Assert.True(r.HarvestedKg > 0.0);
            Assert.InRange(r.MaxTankDensityKgM3, 0.95 * C.Hfs.MaxDensityKgM3, C.Hfs.MaxDensityKgM3);
        }
    }
}
