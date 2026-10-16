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
    /// F2-06's offshore calibration (docs/FARM_TYPES_SPEC.md §3.9): the draft
    /// <c>godot/data/calibration/facilities_offshore.json</c> holds exactly the
    /// code defaults, and every leaf documents its range and exactly one
    /// provenance. F2-04 turns the doc rule into [Range]/[Source]/[GameBalance]
    /// attributes and its reflection guard; until then this test holds it.
    /// </summary>
    public class OffshoreCalibrationTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LakskongenSim.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
        }

        private static string DraftPath => Path.Combine(RepoRoot(), "godot", "data", "calibration", "facilities_offshore.json");

        private static string SourcePath
            => Path.Combine(RepoRoot(), "src", "LakskongenSim", "Calibration", "OffshoreCalibration.cs");

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
            var strictlyParsed = JsonSerializer.Deserialize<OffshoreCalibration>(json, strict);
            Assert.Equal(OffshoreCalibration.Default, strictlyParsed);
            Assert.Equal(OffshoreCalibration.Default, OffshoreCalibration.Parse(json));
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
            Check(root, "facilities_offshore");
        }

        [Fact]
        public void Parse_KeepsTheDefaultsOfKeysTheFileLeavesOut()
        {
            var parsed = OffshoreCalibration.Parse(
                "{ \"metocean\": { \"arPhi\": 0.8, \"swellRatePerDay\": { \"m01\": 0.12 } }, \"plots\": { \"off_2\": { \"hsScale\": 1.5 } } }");
            Assert.Equal(0.8, parsed.Metocean.ArPhi);
            Assert.Equal(0.12, parsed.Metocean.SwellRatePerDay[0]);
            Assert.Equal(1.5, parsed.Plots.Off2.HsScale);
            Assert.Equal(OffshoreCalibration.Default.Metocean.ArSigma, parsed.Metocean.ArSigma);
            Assert.Equal(OffshoreCalibration.Default.Operability, parsed.Operability);
            Assert.Equal(OffshoreCalibration.Default.Plots.Off1, parsed.Plots.Off1);
        }

        private static readonly Regex RangePattern = new(@"Range:\s*(-?[\d,]+(?:\.\d+)?)\s*-\s*(-?[\d,]+(?:\.\d+)?)",
            RegexOptions.CultureInvariant);

        private static double ParseNumber(string text)
            => double.Parse(text.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture);

        /// <summary>Records that only group other records or hold a container's twelve months or four states.</summary>
        private static readonly HashSet<string> NonLeafRecords = new()
        {
            nameof(OffshoreCalibration), nameof(OffshorePlotsCalibration), nameof(MonthlyValues), nameof(WeatherStateValues),
        };

        /// <summary>Every init-only instance property of the calibration records, with its doc comment.</summary>
        private static List<(string Record, string Property, string Doc)> Leaves()
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(SourcePath));
            var leaves = new List<(string, string, string)>();
            foreach (var record in tree.GetRoot().DescendantNodes().OfType<RecordDeclarationSyntax>())
            {
                if (NonLeafRecords.Contains(record.Identifier.Text)) continue;
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
            Assert.True(leaves.Count >= 40, $"only {leaves.Count} calibration properties found");
            foreach (var (record, property, doc) in leaves)
            {
                string text = Regex.Replace(doc, @"\s+", " ");
                Assert.True(RangePattern.IsMatch(text), $"{record}.{property} documents no Range");
                int sources = Regex.Matches(text, @"\bSource:").Count;
                int balances = Regex.Matches(text, @"\bGame balance:").Count;
                Assert.True(sources + balances == 1,
                    $"{record}.{property} documents {sources} Source and {balances} Game balance provenances (exactly one)");
            }
        }

        /// <summary>Every value, each month of a monthly container and each state of a per-state one, lies in its documented range.</summary>
        [Fact]
        public void EveryLeafValue_LiesInItsDocumentedRange()
        {
            var d = OffshoreCalibration.Default;
            var instances = new Dictionary<string, object[]>
            {
                [nameof(OffshoreMetoceanCalibration)] = new object[] { d.Metocean },
                [nameof(OffshoreOperabilityCalibration)] = new object[] { d.Operability },
                [nameof(OffshorePlotCalibration)] = new object[] { d.Plots.Off1, d.Plots.Off2, d.Plots.Hav1 },
            };
            int checkedValues = 0;
            foreach (var (record, property, doc) in Leaves())
            {
                var match = RangePattern.Match(Regex.Replace(doc, @"\s+", " "));
                Assert.True(match.Success, $"{record}.{property} documents no Range");
                double lo = ParseNumber(match.Groups[1].Value), hi = ParseNumber(match.Groups[2].Value);
                foreach (var o in instances[record])
                {
                    object? value = o.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)!.GetValue(o);
                    IEnumerable<double> values = value switch
                    {
                        MonthlyValues months => months.ToArray(),
                        WeatherStateValues states => new[] { states.Calm, states.Moderate, states.Storm, states.Severe },
                        _ => new[] { Convert.ToDouble(value, CultureInfo.InvariantCulture) },
                    };
                    foreach (double v in values)
                    {
                        Assert.True(v >= lo && v <= hi, $"{record}.{property} = {v} lies outside its range {lo}-{hi}");
                        checkedValues++;
                    }
                }
            }
            Assert.True(checkedValues >= 100, $"only {checkedValues} values checked");
        }
    }
}
