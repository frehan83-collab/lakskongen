using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LakskongenSim.Environment.Geo
{
    /// <summary>
    /// The geodata files the sim reads (docs/geo/STAGE23_PLAN.md track A),
    /// embedded in the library from <c>godot/data/geo</c> (LakskongenSim.csproj,
    /// ItemGroup "geo"). The engine never touches the file system: the game, the
    /// tests and the golden recorder read the same bytes, and each one's SHA-256
    /// equals its <c>manifest.json</c> entry. Follows
    /// <c>SitePlotCatalog.LoadEmbedded</c>: a missing resource throws
    /// <see cref="InvalidOperationException"/> naming its logical name.
    /// </summary>
    internal static class GeoResources
    {
        /// <summary>Logical-name prefix of every embedded geo file.</summary>
        internal const string Prefix = "LakskongenSim.Geo.";

        internal const string CandidatesJson = Prefix + "candidates.json";
        internal const string ProductionAreasGeoJson = Prefix + "production_areas.regional.geojson";
        internal const string SuitabilityJson = Prefix + "suitability.json";
        internal const string SuitabilityRulesJson = Prefix + "suitability_rules.json";
        internal const string LocalitiesGeoJson = Prefix + "localities.geojson";

        /// <summary>Logical name of a production area's suitability grid (PO1..PO13).</summary>
        internal static string SuitabilityGrid(int productionArea) =>
            FormattableString.Invariant($"{Prefix}Suitability.po{productionArea:00}.png");

        /// <summary>
        /// Every embedded geo file as (logical name, path under <c>godot/data/geo</c>
        /// as <c>manifest.json</c> keys it): the five data files and the 13 grids.
        /// </summary>
        internal static IReadOnlyList<(string LogicalName, string ManifestPath)> All { get; } = BuildAll();

        private static (string, string)[] BuildAll()
        {
            var all = new List<(string, string)>
            {
                (CandidatesJson, "candidates.json"),
                (ProductionAreasGeoJson, "production_areas.regional.geojson"),
                (SuitabilityJson, "suitability.json"),
                (SuitabilityRulesJson, "suitability_rules.json"),
                (LocalitiesGeoJson, "localities.geojson"),
            };
            for (int po = 1; po <= LakskongenSim.Environment.ProductionAreas.Count; po++)
                all.Add((SuitabilityGrid(po), FormattableString.Invariant($"suitability/po{po:00}.png")));
            return all.ToArray();
        }

        /// <summary>The bytes of an embedded geo file.</summary>
        /// <exception cref="InvalidOperationException">The resource is not embedded (its logical name is in the message).</exception>
        internal static byte[] ReadBytes(string logicalName)
        {
            using var stream = typeof(GeoResources).Assembly.GetManifestResourceStream(logicalName)
                ?? throw new InvalidOperationException($"Embedded resource '{logicalName}' is missing");
            using var buffer = new MemoryStream(stream.CanSeek ? (int)stream.Length : 0);
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }

        /// <summary>An embedded geo file as UTF-8 text (a leading byte-order mark is dropped).</summary>
        /// <exception cref="InvalidOperationException">The resource is not embedded (its logical name is in the message).</exception>
        internal static string ReadText(string logicalName)
        {
            string text = Encoding.UTF8.GetString(ReadBytes(logicalName));
            return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
        }
    }
}
