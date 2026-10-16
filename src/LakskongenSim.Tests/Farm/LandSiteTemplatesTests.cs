using System;
using System.Linq;
using LakskongenSim.Calibration;
using LakskongenSim.Farm.Land;
using Xunit;

namespace LakskongenSim.Tests.Farm
{
    /// <summary>
    /// Roadmap F2-12 (docs/FARM_TYPES_SPEC.md §2A, §3.2): the land templates
    /// build their tanks, wrap S2-01b's pen for each, and start their
    /// biofilters young.
    /// </summary>
    public class LandSiteTemplatesTests
    {
        private static readonly LandCalibration C = LandCalibration.Default;

        [Fact]
        public void AnHfsLot_Has12GrowOutAnd2PreGrowTanks_EachItsOwnBioZone()
        {
            var lot = LandSiteTemplates.BuildLot("land_1", 1, LandTemplate.HfsLot, C);
            Assert.Equal("land_1/lot1", lot.Id);
            Assert.Equal(14, lot.Tanks.Count);
            Assert.Equal(12, lot.Tanks.Count(t => t.Role == TankRole.GrowOut));
            Assert.Equal(2, lot.Tanks.Count(t => t.Role == TankRole.PreGrowOut));
            // [L:R3]: Ø28 m with ~8 m of water, 4,800-5,000 m³ ([L:R5], [L:R6]).
            Assert.All(lot.Tanks.Take(12), t => Assert.InRange(t.VolumeM3, 4_800.0, 5_000.0));
            Assert.All(lot.Tanks.Skip(12), t => Assert.InRange(t.VolumeM3, 1_550.0, 1_650.0));
            Assert.Equal(lot.Tanks.Sum(t => t.VolumeM3), lot.VolumeM3, 6);
            Assert.Equal(lot.Tanks.Select(t => t.Id).Distinct().Count(), lot.Tanks.Select(t => t.BioZoneId).Distinct().Count());
            Assert.Equal(BiofilterType.None, lot.Biofilter);
            Assert.Equal(1.0, lot.BiofilterMaturity);
            Assert.Equal(C.Hfs.HeatPumpMWe, lot.HeatPumpMWe);
            Assert.False(lot.HeatRecovery);
        }

        [Fact]
        public void ATank_WrapsAPenWithItsIdVolumeAndCircumference()
        {
            var lot = LandSiteTemplates.BuildLot("land_1", 1, LandTemplate.HfsLot, C);
            var tank = lot.Tanks[6];
            Assert.Equal("land_1/t07", tank.Id);
            Assert.Equal(7, tank.Pen.Number);
            Assert.True(tank.Pen.IsEmpty);
            Assert.Equal(LandTank.VolumeOf(28.0, 8.0), tank.Pen.VolumeM3, 9);
            Assert.Equal(Math.PI * 28.0, tank.Pen.CircumferenceM, 9);
            Assert.Equal(8.0, tank.Pen.NetDepthM, 9);
            Assert.Equal(60.0, tank.Pen.DensityKgPerM3(60.0 * tank.VolumeM3), 9);
            Assert.Equal(C.Hfs.DefaultSetpointC, tank.TemperatureC);
        }

        [Fact]
        public void LotsNumberTheirTanksAcrossTheSite()
        {
            var first = LandSiteTemplates.BuildLot("land_1", 1, LandTemplate.HfsLot, C);
            var second = LandSiteTemplates.BuildLot("land_1", 2, LandTemplate.HfsLot, C, firstTankNumber: first.Tanks.Count + 1);
            Assert.Equal("land_1/lot2", second.Id);
            Assert.Equal("land_1/t15", second.Tanks[0].Id);
            Assert.Empty(first.Tanks.Select(t => t.Id).Intersect(second.Tanks.Select(t => t.Id)));
        }

        [Fact]
        public void ThePilotLot_IsFourPlusOneTanks()
        {
            var lot = LandSiteTemplates.BuildLot("land_1", 1, LandTemplate.HfsPilotLot, C);
            Assert.Equal(4, lot.Tanks.Count(t => t.Role == TankRole.GrowOut));
            Assert.Equal(1, lot.Tanks.Count(t => t.Role == TankRole.PreGrowOut));
            Assert.True(C.HfsPilot.HeatPumpMWe < C.Hfs.HeatPumpMWe);
        }

        [Fact]
        public void TheRasHall_SharesOneLoop_OnAYoungFixedBed()
        {
            var lot = LandSiteTemplates.BuildLot("ras_1", 1, LandTemplate.RasHall, C);
            Assert.Equal(16, lot.Tanks.Count);
            Assert.All(lot.Tanks, t => Assert.Equal(lot.Id, t.BioZoneId));
            Assert.All(lot.Tanks, t => Assert.InRange(t.VolumeM3, 2_450.0, 2_550.0));
            Assert.Equal(BiofilterType.FixedBed, lot.Biofilter);
            Assert.Equal(C.Tan.MaturityAtStart, lot.BiofilterMaturity);
            Assert.Equal(0.97, LandSiteTemplates.DefaultStrategy(C.Ras).Reuse);
        }

        [Fact]
        public void ThePostSmoltModule_IsTwoSixteenMetreTanks_OnAMovingBed()
        {
            var lot = LandSiteTemplates.BuildLot("ps_1", 1, LandTemplate.PostSmoltModule, C);
            Assert.Equal(2, lot.Tanks.Count);
            Assert.All(lot.Tanks, t => Assert.Equal(TankRole.PostSmolt, t.Role));
            Assert.InRange(lot.VolumeM3, 3_150.0, 3_250.0);
            Assert.Equal(BiofilterType.MovingBed, lot.Biofilter);
        }

        [Fact]
        public void ABiofilter_MaturesLinearlyOverItsDays()
        {
            Assert.Equal(C.Tan.MaturityAtStart, LandSiteTemplates.BiofilterMaturity(0, C.Ras, C.Tan), 12);
            Assert.Equal(0.6, LandSiteTemplates.BiofilterMaturity(30, C.Ras, C.Tan), 12);
            Assert.Equal(1.0, LandSiteTemplates.BiofilterMaturity(60, C.Ras, C.Tan), 12);
            Assert.Equal(1.0, LandSiteTemplates.BiofilterMaturity(400, C.Ras, C.Tan), 12);
        }

        [Fact]
        public void BuildingALot_RejectsBadInput()
        {
            Assert.Throws<ArgumentException>(() => LandSiteTemplates.BuildLot("", 1, LandTemplate.HfsLot, C));
            Assert.Throws<ArgumentOutOfRangeException>(() => LandSiteTemplates.BuildLot("land_1", 0, LandTemplate.HfsLot, C));
            Assert.Throws<ArgumentOutOfRangeException>(() => C.For((LandTemplate)99));
        }
    }
}
