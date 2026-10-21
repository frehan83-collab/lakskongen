using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LakskongenSim.Core;

namespace LakskongenSim.Environment.Geo
{
    /// <summary>
    /// Where a zone's farm is drawn on the real coast: a producing sea grow-out
    /// locality (<see cref="GeoLocalities.SeaGrowoutProducing"/>) in the zone's
    /// production area. Display only (ruling OD9): the sim's zones have no
    /// coordinates, and an anchor changes no sim state.
    /// </summary>
    /// <param name="LocalityId">The locality's registry number.</param>
    /// <param name="X">EPSG:25833 easting, m.</param>
    /// <param name="Y">EPSG:25833 northing, m.</param>
    /// <param name="Name">The locality's place name.</param>
    /// <param name="Source">
    /// <see cref="ScenarioGeoAnchors.AuthoredSource"/> (a shipped scenario's
    /// site), <see cref="ScenarioGeoAnchors.MunicipalitySource"/> (a catalogue
    /// zone in its municipality) or <see cref="ScenarioGeoAnchors.FallbackSource"/>
    /// (a deterministic pick in the area).
    /// </param>
    public sealed record GeoAnchor(int LocalityId, int X, int Y, string Name, string Source);

    /// <summary>
    /// The real localities the game draws its zones at (STAGE23_PLAN track A,
    /// A3; ruling OD9). Three rules, first match wins:
    /// <list type="number">
    /// <item>The 16 starting-site entries of the 9 shipped scenarios are
    /// authored, keyed by (zone id, production area): fjord_west appears in
    /// PO3, PO4 and PO6, fjord_north in PO9 and PO12. Each is a producing sea
    /// grow-out locality in that area, distinct within its scenario, chosen to
    /// fit the scenario's story (Første sesong's family farm is a one-licence
    /// site in Kvinnherad, Hardangerfjorden).</item>
    /// <item>A catalogue zone (<see cref="ExpansionZones"/>) takes the first
    /// producing grow-out locality, by id, in its own area and in the
    /// municipality its name names: the municipality's name itself, the part
    /// before " I &lt;county&gt;" (Sande I Møre Og Romsdal), one part of a
    /// two-language name ("Unjárga - Nesseby"), or the municipality a pre-2020
    /// one merged into (Finnøy into Stavanger, Vikna into Nærøysund).</item>
    /// <item>Otherwise (po13_lebesby, whose Lebesby sites are all PO12; a mod
    /// or free-text zone) the area's producing locality with the smallest
    /// <c>StableHash.Of("geo_anchor\u0001" + zoneId, localityId)</c>, ties by id.</item>
    /// </list>
    /// Pure functions of the embedded localities; no RNG, no game state.
    /// </summary>
    public static class ScenarioGeoAnchors
    {
        /// <summary>Source of an authored scenario anchor.</summary>
        public const string AuthoredSource = "authored";

        /// <summary>Source of a catalogue zone anchored in its municipality.</summary>
        public const string MunicipalitySource = "municipality";

        /// <summary>Source of a deterministic pick in the area.</summary>
        public const string FallbackSource = "fallback";

        /// <summary>
        /// The shipped scenarios' starting sites (godot/data/scenarios.json), as
        /// (scenario, zone, production area) → locality. The scenario is kept so
        /// the "distinct within a scenario" rule can be checked.
        /// </summary>
        private static readonly (string Scenario, string Zone, int Po, int LocalityId)[] AuthoredTable =
        {
            // Familiegården: grandfather's one-licence farm in Hardanger (OD9: Kvinnherad, 780 t).
            ("forste_sesong", "fjord_west", 3, 12128),
            // Nordlands Utfordring: the exposed outer Lofoten coast (Flakstad).
            ("lofoten_eventyr", "fjord_north", 9, 13291),
            // Trønder-krigen: in the rivals' Frøya heartland.
            ("laksebaronene", "fjord_west", 6, 12370),
            // Finnmarks Gjenfødsel: a small, struggling site in Nordkapp.
            ("krise_manager", "fjord_north", 12, 15461),
            // Lakskongen: an established Vestland site (Gulen).
            ("barekraft_mester", "fjord_west", 4, 11700),
            // PO3 Rød Sone: north (Øygarden, Sotra) and south (Karmøy) of the area.
            ("po3_red_sone", "po3_nord", 3, 20115),
            ("po3_red_sone", "po3_sor", 3, 18217),
            // ISA-utbruddet: a core site with neighbours 2.3 km north and 3.7 km south (Smøla).
            ("isa_utbrudd", "isa_kjerne", 6, 29116),
            ("isa_utbrudd", "isa_nord", 6, 45129),
            ("isa_utbrudd", "isa_sor", 6, 39217),
            // Algekatastrofen: outer south, middle and inner sheltered sites of one fjord (Steigen).
            ("algekatastrofe", "alge_sor", 9, 13931),
            ("algekatastrofe", "alge_midt", 9, 45014),
            ("algekatastrofe", "alge_indre", 9, 33457),
            // Ressursrente-overgangen: main site west (Solund), growth north (Askvoll), expansion south (Gulen).
            ("ressursrente", "vest_1", 4, 15403),
            ("ressursrente", "vest_2", 4, 11786),
            ("ressursrente", "vest_3", 4, 13340),
        };

        /// <summary>Pre-2020 municipalities in the catalogue and the municipality they merged into.</summary>
        private static readonly Dictionary<string, string> SuccessorMunicipality = new(StringComparer.Ordinal)
        {
            ["Finnøy"] = "Stavanger",
            ["Vikna"] = "Nærøysund",
        };

        private static readonly Lazy<Dictionary<(string Zone, int Po), GeoAnchor>> LazyAuthored = new(BuildAuthored);

        /// <summary>The authored keys: (scenario, zone id, production area), in table order.</summary>
        public static IReadOnlyList<(string Scenario, string ZoneId, int ProductionArea)> AuthoredKeys { get; } =
            AuthoredTable.Select(e => (e.Scenario, e.Zone, e.Po)).ToArray();

        /// <summary>
        /// The anchor of a zone in a production area (see the class summary).
        /// An area outside 1-13 picks from every producing locality.
        /// </summary>
        public static GeoAnchor AnchorOf(string zoneId, int productionArea)
            => Resolved.GetOrAdd((zoneId ?? "", productionArea), key => Resolve(key.Zone, key.Po));

        /// <summary>Every anchor resolved so far (a pure function, so caching it changes no answer).</summary>
        private static readonly ConcurrentDictionary<(string Zone, int Po), GeoAnchor> Resolved = new();

        private static GeoAnchor Resolve(string zone, int productionArea)
        {
            if (LazyAuthored.Value.TryGetValue((zone, productionArea), out var authored))
                return authored;
            if (ExpansionZones.TryGet(zone, out var catalogue) && catalogue.ProductionArea == productionArea)
            {
                var inMunicipality = FirstInMunicipality(catalogue.Name, productionArea);
                if (inMunicipality != null)
                    return Anchor(inMunicipality, MunicipalitySource);
            }
            return Anchor(Fallback(zone, productionArea), FallbackSource);
        }

        /// <summary>True when a locality's municipality is the one a catalogue zone's name names.</summary>
        internal static bool MunicipalityMatches(string zoneName, string municipality)
        {
            if (string.IsNullOrEmpty(zoneName) || string.IsNullOrEmpty(municipality)) return false;
            string wanted = SuccessorMunicipality.TryGetValue(zoneName, out var successor) ? successor : zoneName;
            if (string.Equals(municipality, wanted, StringComparison.OrdinalIgnoreCase)) return true;
            int county = municipality.IndexOf(" I ", StringComparison.Ordinal);
            if (county > 0 && string.Equals(municipality[..county], wanted, StringComparison.OrdinalIgnoreCase)) return true;
            return municipality.Split(" - ", StringSplitOptions.TrimEntries)
                .Any(part => string.Equals(part, wanted, StringComparison.OrdinalIgnoreCase));
        }

        private static GeoLocality? FirstInMunicipality(string zoneName, int productionArea)
        {
            foreach (var l in GeoLocalities.Default.SeaGrowoutProducing)   // sorted by id
                if (l.Po == productionArea && MunicipalityMatches(zoneName, l.Municipality))
                    return l;
            return null;
        }

        private static GeoLocality Fallback(string zoneId, int productionArea)
        {
            var pool = GeoLocalities.Default.SeaGrowoutProducing;
            bool anyArea = !ProductionAreas.IsValid(productionArea);
            GeoLocality? best = null;
            ulong bestHash = 0;
            foreach (var l in pool)
            {
                if (!anyArea && l.Po != productionArea) continue;
                ulong hash = StableHash.Of("geo_anchor\u0001" + zoneId, l.Id);
                if (best == null || hash < bestHash || (hash == bestHash && l.Id < best.Id))
                {
                    best = l;
                    bestHash = hash;
                }
            }
            return best ?? throw new InvalidDataException(
                $"localities.geojson: production area {productionArea} has no producing sea grow-out locality to anchor '{zoneId}'");
        }

        private static GeoAnchor Anchor(GeoLocality l, string source) => new(l.Id, l.X, l.Y, l.Name, source);

        /// <summary>
        /// The authored table, validated: each locality is a producing sea
        /// grow-out site in its key's area, and no scenario uses one twice.
        /// A defect throws <see cref="InvalidDataException"/> naming the entry.
        /// </summary>
        private static Dictionary<(string Zone, int Po), GeoAnchor> BuildAuthored()
        {
            var localities = GeoLocalities.Default;
            var table = new Dictionary<(string, int), GeoAnchor>();
            var usedInScenario = new HashSet<(string, int)>();
            foreach (var (scenario, zone, po, id) in AuthoredTable)
            {
                string where = $"scenario anchor {scenario}/{zone}/PO{po}";
                if (!localities.TryGet(id, out var l))
                    throw new InvalidDataException($"{where}: locality {id} is not in localities.geojson");
                if (!l.IsSeaGrowoutProducing)
                    throw new InvalidDataException($"{where}: locality {id} is not a producing sea grow-out site");
                if (l.Po != po)
                    throw new InvalidDataException($"{where}: locality {id} lies in PO{l.Po}");
                if (!usedInScenario.Add((scenario, id)))
                    throw new InvalidDataException($"{where}: locality {id} anchors two of the scenario's sites");
                if (!table.TryAdd((zone, po), Anchor(l, AuthoredSource)))
                    throw new InvalidDataException($"{where}: the key appears twice");
            }
            return table;
        }
    }
}
