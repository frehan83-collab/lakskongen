using System;
using System.Linq;
using LakskongenSim.Calibration;
using LakskongenSim.Environment;
using LakskongenSim.Farm.Land;

namespace LakskongenSim.Tests.Farm
{
    /// <summary>
    /// The standalone F2-12 harness's plots and lots. pad_a is the procedural
    /// fjord's first land pad (seed 42, the game's default terrain): its deep
    /// line is the computed <see cref="ISitePlotGeometry.IntakeReachM"/> (F2-19)
    /// and its shallow line the distance from the quay to the nearest
    /// heightfield vertex at 25 m depth, measured the same way.
    /// </summary>
    internal static class LandTestSites
    {
        /// <summary>Depth (m) of the shallow intake (spec §3.3, T25).</summary>
        private const float ShallowIntakeDepthM = 25f;

        /// <summary>Salmon Evolution's production area (Hustadvika: PO5, Stadt til Hustadvika).</summary>
        public const int SalmonEvolutionArea = 5;

        private static readonly Lazy<(double shallowKm, double deepKm)> PadALines = new(MeasurePadA);

        /// <summary>pad_a's shallow and deep intake line lengths (km).</summary>
        public static (double ShallowKm, double DeepKm) PadALineKm => PadALines.Value;

        /// <summary>pad_a with the calibration's lift and pumps and UV disinfection.</summary>
        public static LandIntakeSite PadA(LandCalibration calibration)
            => LandIntakeSite.FromCalibration(calibration.Energy, PadALineKm.ShallowKm, PadALineKm.DeepKm);

        /// <summary>
        /// A flow-through plot like Andfjord's pools ([L:A1], [L:A5]): 2 m of
        /// lift, pumps sized for the whole loop at r 0, no UV (a pre-2025
        /// plant, [L:L2]), a 1 km intake line.
        /// </summary>
        public static LandIntakeSite FlowThroughPlot(LandCalibration calibration)
        {
            var hfs = calibration.Hfs;
            double volume = hfs.GrowOutTanks * LandTank.VolumeOf(hfs.GrowOutDiameterM, hfs.WaterDepthM)
                + hfs.PreGrowTanks * LandTank.VolumeOf(hfs.PreGrowDiameterM, hfs.WaterDepthM);
            double fullLoopM3s = hfs.LoopVolumesPerDay * volume / LandWaterModel.SecondsPerDay;
            return new LandIntakeSite(2.0, 1.0, 1.0, fullLoopM3s * 1.05, Disinfection: false);
        }

        /// <summary>The Salmon Evolution-like strategy: r 0.65, the intakes blended toward 12 °C.</summary>
        public static LandWaterStrategy SalmonEvolutionStrategy(LandCalibration calibration)
            => LandSiteTemplates.DefaultStrategy(calibration.Hfs);

        /// <summary>A Salmon Evolution-like HFS lot on pad_a in PO5 water, measured over one year in steady rotation.</summary>
        public static LandProjectionSettings SalmonEvolutionYear(LandCalibration calibration) => new()
        {
            Template = LandTemplate.HfsLot,
            Strategy = SalmonEvolutionStrategy(calibration),
            Site = PadA(calibration),
            HeatRecovery = true,
            ProductionArea = SalmonEvolutionArea,
            StartDay = LakskongenSim.Core.SimCalendar.DaysPerYear,
            Days = LakskongenSim.Core.SimCalendar.DaysPerYear,
        };

        private static (double, double) MeasurePadA()
        {
            var terrain = new TerrainGenerator(new FjordTerrainSettings());
            var pad = terrain.LandPadSlots().First(p => p.Id == "pad_a");
            float deep = terrain.IntakeReachM(pad);
            var st = terrain.Settings;
            int n = st.Resolution;
            float spacing = st.SpacingM, half = st.WorldSizeM * 0.5f;
            float[] heights = terrain.GenerateHeightmap();
            float shallow = Math.Min(Math.Min(half - pad.QuayX, half + pad.QuayX), Math.Min(half - pad.QuayZ, half + pad.QuayZ));
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                {
                    if (heights[r * n + c] > -ShallowIntakeDepthM) continue;
                    float dx = -half + c * spacing - pad.QuayX, dz = -half + r * spacing - pad.QuayZ;
                    shallow = Math.Min(shallow, MathF.Sqrt(dx * dx + dz * dz));
                }
            return (shallow / 1000.0, deep / 1000.0);
        }
    }
}
