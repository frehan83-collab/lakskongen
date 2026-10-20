using System;
using System.Collections.Generic;

namespace LakskongenSim.Environment
{
    /// <summary>
    /// One water the company can expand into (roadmap U2-01): a growth zone in
    /// a production area where a new locality can be cleared once the company
    /// holds a growth licence for it.
    /// </summary>
    /// <param name="ZoneId">Stable zone id (a label; never parsed for its area).</param>
    /// <param name="ProductionArea">The production area the zone lies in (1-13).</param>
    /// <param name="Name">The place name the player reads.</param>
    public sealed record ExpansionZoneDefinition(string ZoneId, int ProductionArea, string Name);

    /// <summary>
    /// The expansion map's catalogue: one growth zone per production area, named
    /// after a coastal municipality with sea farming in that area. A catalogue
    /// zone is not part of the game until the company builds a site in it
    /// (<c>SimulationEngine.BuildSeaSite</c> adds it to <c>GameState.Zones</c>);
    /// until then it is only an address the licence auction and the build gate
    /// understand, so a game that never expands runs exactly as before (no new
    /// zone ticks water chemistry, no rival auction opens on it).
    ///
    /// <para>No place in the list lies in a national salmon fjord (nasjonale
    /// laksefjorder, where new farming is not allowed); the names are places,
    /// never companies (roadmap D17).</para>
    /// </summary>
    public static class ExpansionZones
    {
        private static readonly ExpansionZoneDefinition[] Catalogue =
        {
            new("po01_flekkefjord", 1, "Flekkefjord"),
            new("po02_finnoy", 2, "Finnøy"),
            new("po03_bomlo", 3, "Bømlo"),
            new("po04_solund", 4, "Solund"),
            new("po05_sande", 5, "Sande"),
            new("po06_hitra", 6, "Hitra"),
            new("po07_vikna", 7, "Vikna"),
            new("po08_luroy", 8, "Lurøy"),
            new("po09_steigen", 9, "Steigen"),
            new("po10_senja", 10, "Senja"),
            new("po11_skjervoy", 11, "Skjervøy"),
            new("po12_hasvik", 12, "Hasvik"),
            new("po13_lebesby", 13, "Lebesby"),
        };

        private static readonly Dictionary<string, ExpansionZoneDefinition> ById = BuildIndex();

        /// <summary>Every catalogue zone, by production area.</summary>
        public static IReadOnlyList<ExpansionZoneDefinition> All => Catalogue;

        /// <summary>The catalogue zone with this id, or false.</summary>
        public static bool TryGet(string? zoneId, out ExpansionZoneDefinition definition)
        {
            if (zoneId != null && ById.TryGetValue(zoneId, out var found))
            {
                definition = found;
                return true;
            }
            definition = null!;
            return false;
        }

        /// <summary>True for a catalogue zone id.</summary>
        public static bool Contains(string? zoneId) => zoneId != null && ById.ContainsKey(zoneId);

        /// <summary>The catalogue zone of a production area (1-13), or null.</summary>
        public static ExpansionZoneDefinition? OfArea(int productionArea)
        {
            foreach (var zone in Catalogue)
                if (zone.ProductionArea == productionArea)
                    return zone;
            return null;
        }

        private static Dictionary<string, ExpansionZoneDefinition> BuildIndex()
        {
            var index = new Dictionary<string, ExpansionZoneDefinition>(StringComparer.Ordinal);
            foreach (var zone in Catalogue)
                index.Add(zone.ZoneId, zone);
            return index;
        }
    }
}
