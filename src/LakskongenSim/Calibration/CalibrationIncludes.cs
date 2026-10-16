using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LakskongenSim.Calibration
{
    /// <summary>
    /// The include loader of the <c>facilities</c> calibration section (roadmap
    /// F2-04, docs/FARM_TYPES_SPEC.md §3.9). <c>calibration.json</c> names the
    /// include files under <see cref="ListKey"/>, each
    /// <c>calibration/facilities_&lt;name&gt;.json</c>; <see cref="Compose"/>
    /// puts each file's root object into <c>facilities.&lt;name&gt;</c>
    /// (<see cref="SectionNameOf"/>) before the registry is deserialized, so
    /// every lane owns one file and nobody edits another lane's numbers. An
    /// include file holds its section's values directly (no wrapper object).
    /// <para>
    /// A listed include that cannot be read is skipped and reported in
    /// <see cref="Composition.Missing"/>: its section keeps the code defaults,
    /// as every registry section does when its key is absent. The registry's
    /// loader never throws; <c>CalibrationGuardTests</c> holds the shipped list
    /// to the files on disk.
    /// </para>
    /// </summary>
    public static class CalibrationIncludes
    {
        /// <summary>Key of the include list in <c>calibration.json</c>.</summary>
        public const string ListKey = "facilitiesIncludes";

        /// <summary>Key of the section the includes fill.</summary>
        public const string SectionKey = "facilities";

        /// <summary>File-name prefix every include carries.</summary>
        public const string FilePrefix = "facilities_";

        /// <summary>Directory of the include files, relative to <c>calibration.json</c>.</summary>
        public const string IncludeDirectory = "calibration";

        /// <summary>Logical-name prefix of the embedded include files (LakskongenSim.csproj).</summary>
        public const string EmbeddedPrefix = "LakskongenSim.Calibration.Includes.";

        /// <summary>The composed registry JSON and the includes that could not be read.</summary>
        public sealed record Composition(string Json, IReadOnlyList<string> Included, IReadOnlyList<string> Missing);

        private static readonly JsonDocumentOptions DocumentOptions = new()
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        private static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = false };

        /// <summary>
        /// The section an include fills: <c>calibration/facilities_closed_sea.json</c>
        /// → <c>closedSea</c>. Throws on a path that breaks the naming rule
        /// (not <c>facilities_*.json</c>).
        /// </summary>
        public static string SectionNameOf(string includePath)
        {
            if (string.IsNullOrWhiteSpace(includePath))
                throw new ArgumentException("An include path is blank", nameof(includePath));
            string file = Path.GetFileName(includePath.Replace('\\', '/'));
            if (!file.StartsWith(FilePrefix, StringComparison.Ordinal)
                || !file.EndsWith(".json", StringComparison.Ordinal)
                || file.Length <= FilePrefix.Length + ".json".Length)
                throw new ArgumentException($"Include '{includePath}' is not named {FilePrefix}<name>.json", nameof(includePath));
            string snake = file.Substring(FilePrefix.Length, file.Length - FilePrefix.Length - ".json".Length);
            var camel = new StringBuilder(snake.Length);
            bool upper = false;
            foreach (char c in snake)
            {
                if (c == '_') { upper = camel.Length > 0; continue; }
                if (!char.IsAsciiLetterOrDigit(c))
                    throw new ArgumentException($"Include '{includePath}' has a character outside [a-z0-9_]", nameof(includePath));
                camel.Append(upper ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
                upper = false;
            }
            return camel.ToString();
        }

        /// <summary>The include paths <paramref name="registryJson"/> lists (empty when it lists none).</summary>
        public static IReadOnlyList<string> ListedIn(string registryJson)
            => ListedIn(JsonNode.Parse(registryJson, NodeOptions, DocumentOptions) as JsonObject);

        private static IReadOnlyList<string> ListedIn(JsonObject? root)
        {
            var list = new List<string>();
            if (root?[ListKey] is JsonArray array)
                foreach (var item in array)
                    if (item is JsonValue value && value.TryGetValue<string>(out var path))
                        list.Add(path);
            return list;
        }

        /// <summary>
        /// The registry JSON with every listed include placed in its section.
        /// <paramref name="readInclude"/> returns an include's text by its listed
        /// path, or null when it cannot be read. A malformed include throws (the
        /// registry's loader then falls back like for a malformed main file).
        /// </summary>
        public static Composition Compose(string registryJson, Func<string, string?> readInclude)
        {
            ArgumentNullException.ThrowIfNull(registryJson);
            ArgumentNullException.ThrowIfNull(readInclude);
            if (JsonNode.Parse(registryJson, NodeOptions, DocumentOptions) is not JsonObject root)
                return new Composition(registryJson, Array.Empty<string>(), Array.Empty<string>());

            var included = new List<string>();
            var missing = new List<string>();
            foreach (string path in ListedIn(root))
            {
                string section = SectionNameOf(path);
                string? text = readInclude(path);
                if (text == null) { missing.Add(path); continue; }
                var node = JsonNode.Parse(text, NodeOptions, DocumentOptions) as JsonObject
                           ?? throw new JsonException($"Include '{path}' is not a JSON object");
                if (root[SectionKey] is not JsonObject facilities)
                {
                    facilities = new JsonObject();
                    root[SectionKey] = facilities;
                }
                facilities[section] = node;
                included.Add(path);
            }
            return new Composition(root.ToJsonString(), included, missing);
        }

        /// <summary>An include read from the directory of the <c>calibration.json</c> that lists it, or null.</summary>
        public static string? ReadBeside(string calibrationDirectory, string includePath)
        {
            string full = Path.Combine(calibrationDirectory, includePath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(full) ? File.ReadAllText(full) : null;
        }

        /// <summary>An include read from the library's embedded copies (the build embeds every <c>godot/data/calibration/*.json</c>), or null.</summary>
        public static string? ReadEmbedded(string includePath)
        {
            string name = EmbeddedPrefix + Path.GetFileName(includePath.Replace('\\', '/'));
            using var stream = typeof(CalibrationIncludes).Assembly.GetManifestResourceStream(name);
            if (stream == null) return null;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
