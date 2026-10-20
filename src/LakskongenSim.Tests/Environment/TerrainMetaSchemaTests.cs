using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LakskongenSim.Environment;
using Xunit;

namespace LakskongenSim.Tests.Environment
{
    /// <summary>
    /// F2-19 (docs/FARM_TYPES_SPEC.md section 4): <c>schemas/terrain/meta.schema.json</c>
    /// describes a baked region's <c>land_pads[]</c> and <c>offshore_slots[]</c>
    /// with the fields of <see cref="LandPadSlot"/> and <see cref="OffshoreSlot"/>
    /// (snake_case), so a bake and the procedural layout fill the same
    /// <see cref="ISitePlotGeometry"/>. The procedural generator's own slots,
    /// written as a meta.json, validate against it; an authored intake reach
    /// (the spec computes it) does not.
    /// </summary>
    public class TerrainMetaSchemaTests
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LakskongenSim.sln")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        private static JsonElement Schema()
        {
            var text = File.ReadAllText(Path.Combine(RepoRoot(), "schemas", "terrain", "meta.schema.json"));
            return JsonDocument.Parse(text).RootElement.Clone();
        }

        private static string SnakeCase(string pascal)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < pascal.Length; i++)
            {
                char c = pascal[i];
                if (char.IsUpper(c) && i > 0)
                    sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        /// <summary>A record's positional properties as a snake_case JSON object.</summary>
        private static JsonObject AsMetaObject<T>(T record) where T : notnull
        {
            var obj = new JsonObject();
            foreach (var prop in typeof(T).GetProperties().Where(p => p.DeclaringType == typeof(T) && p.GetIndexParameters().Length == 0))
            {
                if (prop.Name == "EqualityContract")
                    continue;
                obj[SnakeCase(prop.Name)] = JsonSerializer.SerializeToNode(prop.GetValue(record));
            }
            return obj;
        }

        private static JsonObject ProceduralMeta(int seed)
        {
            var gen = new TerrainGenerator(seed);
            var pads = new JsonArray(gen.LandPadSlots().Select(p => (JsonNode)AsMetaObject(p)).ToArray());
            var slots = new JsonArray(gen.OffshoreSlots().Select(s => (JsonNode)AsMetaObject(s)).ToArray());
            return new JsonObject { ["land_pads"] = pads, ["offshore_slots"] = slots };
        }

        /// <summary>
        /// The schema keywords meta.schema.json uses (type, required,
        /// properties, additionalProperties false, items, $ref into $defs,
        /// minimum, maximum, exclusiveMinimum, pattern, minLength): the
        /// failures, by JSON path.
        /// </summary>
        private static List<string> Validate(JsonElement schema, JsonNode? node, JsonElement root, string path)
        {
            var errors = new List<string>();
            if (schema.TryGetProperty("$ref", out var reference))
            {
                var name = reference.GetString()!.Split('/').Last();
                return Validate(root.GetProperty("$defs").GetProperty(name), node, root, path);
            }
            if (schema.TryGetProperty("type", out var type))
            {
                bool ok = type.GetString() switch
                {
                    "object" => node is JsonObject,
                    "array" => node is JsonArray,
                    "string" => node is JsonValue v && v.TryGetValue<string>(out _),
                    "integer" => node is JsonValue n && n.GetValueKind() == JsonValueKind.Number && Math.Abs(n.GetValue<double>() % 1.0) < 1e-12,
                    "number" => node is JsonValue m && m.GetValueKind() == JsonValueKind.Number,
                    _ => false,
                };
                if (!ok)
                {
                    errors.Add($"{path}: not a {type.GetString()}");
                    return errors;
                }
            }
            if (node is JsonObject obj)
            {
                if (schema.TryGetProperty("required", out var required))
                    foreach (var key in required.EnumerateArray().Select(k => k.GetString()!))
                        if (!obj.ContainsKey(key))
                            errors.Add($"{path}: missing {key}");
                schema.TryGetProperty("properties", out var props);
                bool closed = schema.TryGetProperty("additionalProperties", out var extra) && extra.ValueKind == JsonValueKind.False;
                foreach (var (key, value) in obj)
                {
                    if (props.ValueKind == JsonValueKind.Object && props.TryGetProperty(key, out var sub))
                        errors.AddRange(Validate(sub, value, root, $"{path}.{key}"));
                    else if (closed)
                        errors.Add($"{path}: {key} is not allowed");
                }
            }
            if (node is JsonArray arr && schema.TryGetProperty("items", out var items))
                for (int i = 0; i < arr.Count; i++)
                    errors.AddRange(Validate(items, arr[i], root, $"{path}[{i}]"));
            if (node is JsonValue val && val.GetValueKind() == JsonValueKind.Number)
            {
                double x = val.GetValue<double>();
                if (schema.TryGetProperty("minimum", out var min) && x < min.GetDouble())
                    errors.Add($"{path}: {x} < {min.GetDouble()}");
                if (schema.TryGetProperty("maximum", out var max) && x > max.GetDouble())
                    errors.Add($"{path}: {x} > {max.GetDouble()}");
                if (schema.TryGetProperty("exclusiveMinimum", out var emin) && x <= emin.GetDouble())
                    errors.Add($"{path}: {x} <= {emin.GetDouble()}");
            }
            if (node is JsonValue str && str.TryGetValue<string>(out var s))
            {
                if (schema.TryGetProperty("pattern", out var pattern) && !Regex.IsMatch(s, pattern.GetString()!))
                    errors.Add($"{path}: '{s}' does not match {pattern.GetString()}");
                if (schema.TryGetProperty("minLength", out var minLength) && s.Length < minLength.GetInt32())
                    errors.Add($"{path}: '{s}' is shorter than {minLength.GetInt32()}");
            }
            return errors;
        }

        [Fact]
        public void TheSchema_NamesEveryFieldOfThePadAndSlotRecords_AndNothingElse()
        {
            var defs = Schema().GetProperty("$defs");
            foreach (var (def, record) in new[] { ("land_pad", typeof(LandPadSlot)), ("offshore_slot", typeof(OffshoreSlot)) })
            {
                var fields = record.GetConstructors().Single().GetParameters().Select(p => SnakeCase(char.ToUpperInvariant(p.Name![0]) + p.Name[1..])).OrderBy(n => n).ToList();
                var required = defs.GetProperty(def).GetProperty("required").EnumerateArray().Select(e => e.GetString()!).OrderBy(n => n).ToList();
                var described = defs.GetProperty(def).GetProperty("properties").EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToList();
                Assert.Equal(fields, required);
                Assert.Equal(fields, described);
            }
        }

        [Theory]
        [InlineData(42)]
        [InlineData(1)]
        [InlineData(7)]
        public void TheProceduralSlots_WrittenAsAMetaJson_Validate(int seed)
        {
            var schema = Schema();
            var meta = ProceduralMeta(seed);
            var errors = Validate(schema, meta, schema, "$");
            Assert.True(errors.Count == 0, string.Join("; ", errors));
            Assert.Equal(new TerrainGenerator(seed).LandPadSlots().Count, meta["land_pads"]!.AsArray().Count);
            Assert.Equal(new TerrainGenerator(seed).OffshoreSlots().Count, meta["offshore_slots"]!.AsArray().Count);
        }

        [Fact]
        public void AnAuthoredIntakeReach_OrAnOffPlanPadLevel_IsRefused()
        {
            var schema = Schema();
            var meta = ProceduralMeta(42);
            meta["land_pads"]![0]!["intake_reach_m"] = 280.0;
            Assert.Contains(Validate(schema, meta, schema, "$"), e => e.Contains("intake_reach_m is not allowed"));
            meta = ProceduralMeta(42);
            meta["land_pads"]![0]!["pad_height_m"] = 5.0;
            Assert.Contains(Validate(schema, meta, schema, "$"), e => e.Contains("pad_height_m"));
            meta = ProceduralMeta(42);
            meta.Remove("offshore_slots");
            Assert.Contains(Validate(schema, meta, schema, "$"), e => e.Contains("missing offshore_slots"));
        }
    }
}
