using System;
using System.IO;
using System.Linq;
using LakskongenSim.Calibration;
using Xunit;

namespace LakskongenSim.Tests.Calibration
{
    /// <summary>
    /// F2-12 wiring (docs/run/evidence/f2_12_land_integration_notes.md §4): the
    /// land calibration is the registry's <c>facilities.land</c>, read through
    /// F2-04's include loader from <c>calibration/facilities_land.json</c>, and
    /// every leaf carries the attributes F2-04's reflection guard requires.
    /// </summary>
    public class LandFacilitiesSectionTests
    {
        private static readonly CalibrationRegistry Registry = CalibrationRegistry.Load();

        [Fact]
        public void TheRegistrysLandSection_IsTheIncludeFile_AndTheFileHoldsTheCodeDefaults()
        {
            Assert.Contains("calibration/facilities_land.json",
                CalibrationIncludes.ListedIn(File.ReadAllText(Path.Combine(RepoRoot(), "godot", "data", "calibration.json"))));
            Assert.Equal(LandCalibration.Default, Registry.Facilities.Land);
            Assert.Equal(LandCalibration.Default, CalibrationRegistry.Default.Facilities.Land);
            // Not vacuous: an edited include reaches the section.
            string json = File.ReadAllText(Path.Combine(RepoRoot(), "godot", "data", "calibration.json"));
            var edited = CalibrationRegistry.Parse(json, path => path.EndsWith("facilities_land.json", StringComparison.Ordinal)
                ? "{ \"permits\": { \"moratorium\": true }, \"hfs\": { \"defaultReuse\": 0.5 } }"
                : CalibrationIncludes.ReadEmbedded(path));
            Assert.True(edited.Facilities.Land.Permits.Moratorium);
            Assert.Equal(0.5, edited.Facilities.Land.Hfs.DefaultReuse);
            Assert.Equal(LandCalibration.Default.Energy, edited.Facilities.Land.Energy);
        }

        [Fact]
        public void EveryLandLeaf_IsWalkedByTheGuard_WithItsRangeAndOneProvenance()
        {
            var leaves = CalibrationProvenanceGuard.Leaves(Registry.Facilities.Land, "facilities.land");
            // 4 templates × 21 leaves, intake 4, oxygen 8, CO2 4, TAN 5, density 3, energy 14, production 4,
            // build 10 + 5 plans × 12, permits 31, operations 1.
            Assert.Equal(4 * 21 + 4 + 8 + 4 + 5 + 3 + 14 + 4 + 10 + 5 * 12 + 31 + 1, leaves.Count);
            Assert.Empty(CalibrationProvenanceGuard.Violations(Registry.Facilities.Land, "facilities.land"));
            Assert.Contains(leaves, l => l.Path == "facilities.land.Permits.SeaWorksFeeNok" && l.Source!.Keys.SequenceEqual(new[] { "L:R12" }));
            Assert.Contains(leaves, l => l.Path == "facilities.land.Build.HfsLotCapexNok" && l.Source != null);
            Assert.Contains(leaves, l => l.Path == "facilities.land.Permits.Moratorium" && l.GameBalance != null);
            Assert.DoesNotContain(leaves, l => l.Path.EndsWith(".TankCount", StringComparison.Ordinal)); // derived, not a leaf
        }

        [Fact]
        public void TheRegistryVersion_CountsUpForTheLandSection()
        {
            Assert.True(Version.Parse(Registry.Version) >= new Version(1, 1, 2), Registry.Version);
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LakskongenSim.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
        }
    }
}
