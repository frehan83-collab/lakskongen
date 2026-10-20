using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LakskongenSim.Calibration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace LakskongenSim.Tests.Calibration
{
    /// <summary>
    /// F2-12's land calibration (docs/FARM_TYPES_SPEC.md §3.9): the draft
    /// <c>godot/data/calibration/facilities_land.json</c> holds exactly the
    /// code defaults, and every leaf documents its range and exactly one
    /// provenance. F2-04 turns the doc rule into [Range]/[Source]/[GameBalance]
    /// attributes and its reflection guard; until then this test holds it.
    /// </summary>
    public class LandCalibrationTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LakskongenSim.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
        }

        private static string DraftPath => Path.Combine(RepoRoot(), "godot", "data", "calibration", "facilities_land.json");

        private static string SourcePath
            => Path.Combine(RepoRoot(), "src", "LakskongenSim", "Calibration", "LandCalibration.cs");

        [Fact]
        public void TheDraftJson_HoldsExactlyTheCodeDefaults()
        {
            var strict = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            };
            string json = File.ReadAllText(DraftPath);
            var strictlyParsed = JsonSerializer.Deserialize<LandCalibration>(json, strict);
            Assert.Equal(LandCalibration.Default, strictlyParsed);
            Assert.Equal(LandCalibration.Default, LandCalibration.Parse(json));
        }

        [Fact]
        public void TheDraftJson_KeysAreSorted()
        {
            var root = JsonNode.Parse(File.ReadAllText(DraftPath))!.AsObject();
            void Check(JsonObject o, string path)
            {
                var keys = o.Select(kv => kv.Key).ToList();
                Assert.True(keys.SequenceEqual(keys.OrderBy(k => k, StringComparer.Ordinal)), $"keys of {path} are not sorted");
                foreach (var kv in o)
                    if (kv.Value is JsonObject child)
                        Check(child, path + "." + kv.Key);
            }
            Check(root, "facilities_land");
        }

        [Fact]
        public void Parse_KeepsTheDefaultsOfKeysTheFileLeavesOut()
        {
            var parsed = LandCalibration.Parse("{ \"hfs\": { \"defaultReuse\": 0.5 }, \"energy\": { \"cop\": 4.0 } }");
            Assert.Equal(0.5, parsed.Hfs.DefaultReuse);
            Assert.Equal(4.0, parsed.Energy.Cop);
            Assert.Equal(LandCalibration.Default.Energy.CopWithHeatRecovery, parsed.Energy.CopWithHeatRecovery);
            Assert.Equal(LandCalibration.Default.Ras, parsed.Ras);
        }

        private static readonly Regex RangePattern = new(@"Range:\s*(-?[\d,]+(?:\.\d+)?)\s*-\s*(-?[\d,]+(?:\.\d+)?)",
            RegexOptions.CultureInvariant);

        private static double ParseNumber(string text)
            => double.Parse(text.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture);

        /// <summary>Every init-only instance property of the calibration records, with its doc comment.</summary>
        private static List<(string Record, string Property, string Doc)> Leaves()
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(SourcePath));
            var leaves = new List<(string, string, string)>();
            foreach (var record in tree.GetRoot().DescendantNodes().OfType<RecordDeclarationSyntax>())
            {
                foreach (var property in record.Members.OfType<PropertyDeclarationSyntax>())
                {
                    if (property.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword))) continue;
                    bool init = property.AccessorList?.Accessors.Any(a => a.IsKind(SyntaxKind.InitAccessorDeclaration)) ?? false;
                    if (!init) continue;
                    string doc = string.Concat(property.GetLeadingTrivia()
                        .Where(t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia))
                        .Select(t => t.ToFullString()));
                    leaves.Add((record.Identifier.Text, property.Identifier.Text, doc.Replace("///", " ")));
                }
            }
            return leaves;
        }

        [Fact]
        public void EveryLeaf_DocumentsRangeAndOneProvenance()
        {
            var leaves = Leaves();
            Assert.True(leaves.Count >= 50, $"only {leaves.Count} calibration properties found");
            var sectionRecords = new HashSet<string> { nameof(LandCalibration) };
            foreach (var (record, property, doc) in leaves.Where(l => !sectionRecords.Contains(l.Record)))
            {
                string text = Regex.Replace(doc, @"\s+", " ");
                Assert.True(RangePattern.IsMatch(text), $"{record}.{property} documents no Range");
                int sources = Regex.Matches(text, @"\bSource:").Count;
                int balances = Regex.Matches(text, @"\bGame balance:").Count;
                Assert.True(sources + balances == 1,
                    $"{record}.{property} documents {sources} Source and {balances} Game balance provenances (exactly one)");
            }
        }

        [Fact]
        public void EveryLeafValue_LiesInItsDocumentedRange()
        {
            var d = LandCalibration.Default;
            var instances = new Dictionary<string, object[]>
            {
                [nameof(LandTemplateCalibration)] = new object[] { d.Hfs, d.HfsPilot, d.Ras, d.PostSmolt },
                [nameof(LandIntakeCalibration)] = new object[] { d.Intake },
                [nameof(LandOxygenCalibration)] = new object[] { d.Oxygen },
                [nameof(LandCo2Calibration)] = new object[] { d.Co2 },
                [nameof(LandTanCalibration)] = new object[] { d.Tan },
                [nameof(LandDensityCalibration)] = new object[] { d.Density },
                [nameof(LandEnergyCalibration)] = new object[] { d.Energy },
                [nameof(LandProductionCalibration)] = new object[] { d.Production },
            };
            int checkedValues = 0;
            foreach (var (record, property, doc) in Leaves())
            {
                if (!instances.TryGetValue(record, out var objects)) continue;
                var match = RangePattern.Match(Regex.Replace(doc, @"\s+", " "));
                Assert.True(match.Success, $"{record}.{property} documents no Range");
                double lo = ParseNumber(match.Groups[1].Value), hi = ParseNumber(match.Groups[2].Value);
                foreach (var o in objects)
                {
                    var p = o.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)!;
                    double value = Convert.ToDouble(p.GetValue(o), CultureInfo.InvariantCulture);
                    Assert.True(value >= lo && value <= hi, $"{record}.{property} = {value} lies outside its range {lo}-{hi}");
                    checkedValues++;
                }
            }
            Assert.True(checkedValues >= 100, $"only {checkedValues} values checked");
        }
    }
}
