using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using LakskongenSim.Calibration;
using Xunit;

namespace LakskongenSim.Tests.Calibration
{
    /// <summary>
    /// The facilities include loader (roadmap F2-04, docs/FARM_TYPES_SPEC.md §3.9):
    /// <c>calibration.json</c> lists one include file per lane, each lands in its
    /// own <c>facilities.&lt;name&gt;</c> section, from disk next to the main file
    /// or from the library's embedded copies.
    /// </summary>
    public class FacilitiesIncludeLoaderTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LakskongenSim.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
        }

        private static string DataDir => Path.Combine(RepoRoot(), "godot", "data");
        private static string ShippedJson => File.ReadAllText(Path.Combine(DataDir, CalibrationRegistry.FileName));

        /// <summary>The section names <see cref="FacilitiesCalibration"/> declares (its JSON names).</summary>
        private static HashSet<string> DeclaredSections() => typeof(FacilitiesCalibration)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        [Fact]
        public void ShippedList_NamesOneFilePerLane_InTheCalibrationDirectory()
        {
            var listed = CalibrationIncludes.ListedIn(ShippedJson);
            Assert.Equal(new[]
            {
                "calibration/facilities_common.json",
                "calibration/facilities_land.json",
                "calibration/facilities_offshore.json",
                "calibration/facilities_closed_sea.json",
            }, listed);
            Assert.Equal(listed.Count, listed.Select(CalibrationIncludes.SectionNameOf).Distinct().Count());
            Assert.All(listed, p => Assert.StartsWith(CalibrationIncludes.IncludeDirectory + "/", p));
        }

        [Fact]
        public void EveryIncludeFileOnDisk_IsListed()
        {
            // No orphan file: a lane's file that nobody lists would never load.
            var listed = CalibrationIncludes.ListedIn(ShippedJson).Select(Path.GetFileName).ToHashSet();
            var onDisk = Directory.GetFiles(Path.Combine(DataDir, CalibrationIncludes.IncludeDirectory), "*.json")
                .Select(Path.GetFileName).ToList();
            Assert.Contains("facilities_common.json", onDisk);
            foreach (var file in onDisk)
                Assert.True(listed.Contains(file), $"godot/data/calibration/{file} is not in facilitiesIncludes");
        }

        [Fact]
        public void EveryListedFileThatExists_LoadsIntoItsDeclaredSection()
        {
            var declared = DeclaredSections();
            var composition = CalibrationIncludes.Compose(ShippedJson, p => CalibrationIncludes.ReadBeside(DataDir, p));
            // sim·core's file is always there; the lanes' files arrive with their branches.
            Assert.Contains("calibration/facilities_common.json", composition.Included);
            var loaded = CalibrationRegistry.Load(DataDir).Facilities;
            var options = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };
            foreach (var path in composition.Included)
            {
                string section = CalibrationIncludes.SectionNameOf(path);
                if (!declared.Contains(section)) continue; // a lane wires its section from its own record file
                // The section the registry holds is exactly the file read on its own.
                var property = typeof(FacilitiesCalibration).GetProperties()
                    .Single(p => string.Equals(p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? p.Name, section, StringComparison.OrdinalIgnoreCase));
                object? alone = System.Text.Json.JsonSerializer.Deserialize(CalibrationIncludes.ReadBeside(DataDir, path)!, property.PropertyType, options);
                Assert.Equal(alone, property.GetValue(loaded));
            }
            // Every declared section has a listed include.
            var listedSections = CalibrationIncludes.ListedIn(ShippedJson).Select(CalibrationIncludes.SectionNameOf).ToHashSet();
            Assert.All(declared, s => Assert.Contains(s, listedSections));
        }

        [Fact]
        public void DiskAndEmbeddedCopies_LoadTheSameSection()
        {
            var fromDisk = CalibrationRegistry.Load(DataDir);
            var embedded = CalibrationRegistry.Parse(ShippedJson);
            Assert.Equal(fromDisk.Facilities, embedded.Facilities);
            Assert.Equal(new FacilityCommonCalibration(), embedded.Facilities.Common);
            // Every embedded include is the file on disk, byte for byte.
            foreach (var path in CalibrationIncludes.ListedIn(ShippedJson))
            {
                string? disk = CalibrationIncludes.ReadBeside(DataDir, path);
                Assert.Equal(disk, CalibrationIncludes.ReadEmbedded(path));
            }
        }

        [Fact]
        public void AnIncludeBesideTheMainFile_WinsOverTheEmbeddedCopy()
        {
            string dir = Path.Combine(Path.GetTempPath(), "lk_f204_" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(dir, CalibrationIncludes.IncludeDirectory));
            try
            {
                File.WriteAllText(Path.Combine(dir, CalibrationRegistry.FileName),
                    "{ \"version\": \"9.9.9\", \"facilitiesIncludes\": [\"calibration/facilities_common.json\"] }");
                File.WriteAllText(Path.Combine(dir, CalibrationIncludes.IncludeDirectory, "facilities_common.json"),
                    "{ // a comment, as calibration.json allows\n \"depreciation\": { \"landLifeYears\": 30 }, }");
                var loaded = CalibrationRegistry.Load(dir);
                Assert.Equal("9.9.9", loaded.Version);
                Assert.Equal(30, loaded.Facilities.Common.Depreciation.LandLifeYears);
                // Keys the file leaves out keep their code defaults.
                Assert.Equal(25, loaded.Facilities.Common.Depreciation.OffshoreLifeYears);
                Assert.Equal(new FarmTypeCatalogCalibration(), loaded.Facilities.Common.Catalog);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void AMissingInclude_KeepsTheCodeDefaults_AndIsReported()
        {
            const string json = "{ \"facilitiesIncludes\": [\"calibration/facilities_common.json\", \"calibration/facilities_nowhere.json\"] }";
            var composition = CalibrationIncludes.Compose(json, _ => null);
            Assert.Equal(new[] { "calibration/facilities_common.json", "calibration/facilities_nowhere.json" }, composition.Missing);
            Assert.Empty(composition.Included);
            var registry = CalibrationRegistry.Parse(json, _ => null);
            Assert.Equal(new FacilitiesCalibration(), registry.Facilities);
        }

        [Fact]
        public void AnInclude_FillsItsOwnSection_AndReplacesAnInlineCopy()
        {
            const string json = "{ \"facilities\": { \"common\": { \"depreciation\": { \"landLifeYears\": 7 } }, \"other\": { \"x\": 1 } }," +
                                " \"facilitiesIncludes\": [\"calibration/facilities_common.json\"] }";
            var composition = CalibrationIncludes.Compose(json,
                p => p == "calibration/facilities_common.json" ? "{ \"depreciation\": { \"landLifeYears\": 11 } }" : null);
            Assert.Contains("\"landLifeYears\":11", composition.Json);
            Assert.DoesNotContain("\"landLifeYears\":7", composition.Json);
            Assert.Contains("\"other\":{\"x\":1}", composition.Json);
            Assert.Equal(11, CalibrationRegistry.Parse(json, p => "{ \"depreciation\": { \"landLifeYears\": 11 } }")
                .Facilities.Common.Depreciation.LandLifeYears);
        }

        [Theory]
        [InlineData("calibration/facilities_common.json", "common")]
        [InlineData("calibration/facilities_land.json", "land")]
        [InlineData("calibration/facilities_offshore.json", "offshore")]
        [InlineData("calibration/facilities_closed_sea.json", "closedSea")]
        [InlineData("facilities_a_b_c.json", "aBC")]
        public void SectionName_IsTheFileNameAfterThePrefix_InCamelCase(string path, string section)
            => Assert.Equal(section, CalibrationIncludes.SectionNameOf(path));

        [Theory]
        [InlineData("calibration/land.json")]
        [InlineData("calibration/facilities_.json")]
        [InlineData("calibration/facilities_land.txt")]
        [InlineData("calibration/facilities_la-nd.json")]
        [InlineData("")]
        public void SectionName_RefusesAPathThatBreaksTheNamingRule(string path)
            => Assert.Throws<ArgumentException>(() => CalibrationIncludes.SectionNameOf(path));
    }
}
