using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LakskongenSim.Environment.Geo
{
    /// <summary>A candidate's ocean values from the Copernicus climatologies (open pens only).</summary>
    /// <param name="CurrentSpeedMs">Annual mean current speed in the upper 30 m, m/s.</param>
    /// <param name="HsM">Highest monthly mean significant wave height, m.</param>
    /// <param name="O2MmolM3">Lowest monthly mean dissolved oxygen in the upper 30 m, mmol/m³.</param>
    public sealed record GeoOcean(double CurrentSpeedMs, double HsM, double O2MmolM3);

    /// <summary>
    /// One candidate site from <c>candidates.json</c> (docs/geo/GEODATA.md
    /// §2.1 and §5.3): an open pen (12 per production area, ranked) or an
    /// offshore site in a Havbruk til havs area (up to 3 per area). Immutable.
    /// </summary>
    /// <param name="Id">"PO03-01" or "HTH05-02".</param>
    /// <param name="Type"><see cref="GeoCandidates.OpenPenType"/> or <see cref="GeoCandidates.OffshoreType"/>.</param>
    /// <param name="Po">Production area: the one it lies in (open pens) or the nearest (offshore).</param>
    /// <param name="Rank">1..12 inside the area (open pens), 1..3 inside the HTH area (offshore).</param>
    /// <param name="Lon">Longitude, degrees.</param>
    /// <param name="Lat">Latitude, degrees.</param>
    /// <param name="X">EPSG:25833 easting, metres.</param>
    /// <param name="Y">EPSG:25833 northing, metres.</param>
    /// <param name="Score">Suitability score 0..1 (3 decimals).</param>
    /// <param name="Components">The score components, each 0..1, sorted by name (ordinal).</param>
    /// <param name="Binding">The term that costs the most score ("none" when nothing does).</param>
    /// <param name="RiverFactor">0.6 at an anadromous river mouth rising to 1 at 5 km.</param>
    /// <param name="DepthM">Water depth, m.</param>
    /// <param name="Openness">Share of sea within 3 km, 0..1.</param>
    /// <param name="NearestFarmKm">Distance to the nearest active sea salmonid site, km.</param>
    /// <param name="NearestRiverKm">Distance to the nearest anadromous river mouth, km.</param>
    /// <param name="Near">The nearest Kartverket N250 label text, as shipped.</param>
    /// <param name="Label">The readable place label (<see cref="PlaceLabels.Clean"/> of <paramref name="Near"/>).</param>
    /// <param name="Ocean">Ocean values (open pens), null offshore.</param>
    /// <param name="Area">The Havbruk til havs area's name (offshore), null for open pens.</param>
    /// <param name="Stage">The HTH area's latest planning stage (offshore), null for open pens.</param>
    /// <param name="StageFactor">The stage's score factor, 0.85..1 (offshore), null for open pens.</param>
    public sealed record GeoCandidate(
        string Id,
        string Type,
        int Po,
        int Rank,
        double Lon,
        double Lat,
        long X,
        long Y,
        double Score,
        IReadOnlyList<KeyValuePair<string, double>> Components,
        string Binding,
        double RiverFactor,
        double DepthM,
        double Openness,
        double NearestFarmKm,
        double NearestRiverKm,
        string Near,
        string Label,
        GeoOcean? Ocean,
        string? Area,
        string? Stage,
        double? StageFactor)
    {
        /// <summary>True for an open-pen candidate.</summary>
        public bool IsOpenPen => Type == GeoCandidates.OpenPenType;

        /// <summary>True for an offshore (Havbruk til havs) candidate.</summary>
        public bool IsOffshore => Type == GeoCandidates.OffshoreType;

        /// <summary>A component's value, or false when the candidate has no such component.</summary>
        public bool TryGetComponent(string name, out double value)
        {
            foreach (var pair in Components)
            {
                if (pair.Key != name) continue;
                value = pair.Value;
                return true;
            }
            value = 0;
            return false;
        }
    }

    /// <summary>
    /// The 189 candidate sites of <c>candidates.json</c> (156 open pens, 12 per
    /// production area, and 33 offshore), read from the embedded copy once per
    /// process (track A, A1). Validation: unique ids, a known type, open-pen
    /// ranks 1..12 in every area, offshore ranks 1..n per HTH area, components
    /// within 0..1, a score within (0, 1]. A defect throws
    /// <see cref="InvalidDataException"/> naming the candidate and the field.
    /// </summary>
    public sealed class GeoCandidates
    {
        private const string FileName = "candidates.json";

        /// <summary>The type of an open-pen candidate.</summary>
        public const string OpenPenType = "open_pen";

        /// <summary>The type of an offshore candidate.</summary>
        public const string OffshoreType = "offshore";

        /// <summary>Open-pen candidates per production area (suitability_rules.json candidates.per_po).</summary>
        public const int OpenPensPerArea = 12;

        private static readonly Lazy<GeoCandidates> LazyDefault =
            new(() => Parse(GeoResources.ReadText(GeoResources.CandidatesJson)));

        private readonly ReadOnlyCollection<GeoCandidate> _all;
        private readonly Dictionary<string, GeoCandidate> _byId;
        private readonly ReadOnlyCollection<GeoCandidate>[] _openPensByArea;
        private readonly ReadOnlyCollection<GeoCandidate> _offshore;

        private GeoCandidates(GeoCandidate[] all)
        {
            _all = Array.AsReadOnly(all);
            _byId = all.ToDictionary(c => c.Id, StringComparer.Ordinal);
            _openPensByArea = Enumerable.Range(1, ProductionAreas.Count)
                .Select(po => Array.AsReadOnly(all.Where(c => c.IsOpenPen && c.Po == po).OrderBy(c => c.Rank).ToArray()))
                .ToArray();
            _offshore = Array.AsReadOnly(all.Where(c => c.IsOffshore).ToArray());
        }

        /// <summary>The shipped candidates (embedded; parsed once per process).</summary>
        public static GeoCandidates Default => LazyDefault.Value;

        /// <summary>Every candidate in file order (open pens by area and rank, then offshore by HTH area and rank).</summary>
        public IReadOnlyList<GeoCandidate> All => _all;

        /// <summary>The area's open-pen candidates in rank order (empty for a number outside 1-13).</summary>
        public IReadOnlyList<GeoCandidate> OpenPensOf(int po) =>
            ProductionAreas.IsValid(po) ? _openPensByArea[po - 1] : Array.Empty<GeoCandidate>();

        /// <summary>The offshore candidates in file order (by HTH area id, then rank).</summary>
        public IReadOnlyList<GeoCandidate> Offshore => _offshore;

        /// <summary>The candidate with this id, or null.</summary>
        public GeoCandidate? Find(string id) =>
            id != null && _byId.TryGetValue(id, out var candidate) ? candidate : null;

        /// <summary>Parse and validate a <c>candidates.json</c> text (see the class summary).</summary>
        public static GeoCandidates Parse(string json)
        {
            using var doc = GeoJson.Parse(json, FileName);
            var sites = GeoJson.RequiredArray(doc.RootElement, "sites", FileName);
            var all = new List<GeoCandidate>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int index = 0;
            foreach (var site in sites.EnumerateArray())
            {
                string id = GeoJson.RequiredString(site, "id", GeoJson.Context(FileName, "site #", index));
                if (!seen.Add(id))
                    throw new InvalidDataException($"{FileName}: candidate {id}: field 'id' appears twice");
                all.Add(ParseSite(site, id));
                index++;
            }
            ValidateRanks(all);
            return new GeoCandidates(all.ToArray());
        }

        private static GeoCandidate ParseSite(JsonElement s, string id)
        {
            string context = $"{FileName}: candidate {id}";
            string type = GeoJson.RequiredString(s, "type", context);
            if (type != OpenPenType && type != OffshoreType)
                throw new InvalidDataException($"{context}: field 'type' is '{type}', expected open_pen or offshore");
            int po = GeoJson.RequiredInt(s, "po", context);
            if (!ProductionAreas.IsValid(po))
                throw new InvalidDataException($"{context}: field 'po' is {po}, expected 1-13");
            int rank = GeoJson.RequiredInt(s, "rank", context);
            if (rank < 1) throw new InvalidDataException($"{context}: field 'rank' is {rank}, expected 1 or more");
            double score = GeoJson.RequiredDouble(s, "score", context);
            if (score <= 0 || score > 1)
                throw new InvalidDataException($"{context}: field 'score' is {score}, expected within (0, 1]");
            double riverFactor = GeoJson.RequiredDouble(s, "river_factor", context);
            if (riverFactor <= 0 || riverFactor > 1)
                throw new InvalidDataException($"{context}: field 'river_factor' is {riverFactor}, expected within (0, 1]");
            string near = GeoJson.RequiredString(s, "near", context);
            string label = PlaceLabels.Clean(near);
            if (label.Length == 0) throw new InvalidDataException($"{context}: field 'near' gives an empty label");

            bool offshore = type == OffshoreType;
            string? area = offshore ? GeoJson.RequiredString(s, "area", context) : null;
            string? stage = offshore ? GeoJson.RequiredString(s, "stage", context) : null;
            double? stageFactor = offshore ? GeoJson.RequiredDouble(s, "stage_factor", context) : null;
            if (stageFactor is <= 0 or > 1)
                throw new InvalidDataException($"{context}: field 'stage_factor' is {stageFactor}, expected within (0, 1]");

            return new GeoCandidate(
                id, type, po, rank,
                GeoJson.RequiredDouble(s, "lon", context),
                GeoJson.RequiredDouble(s, "lat", context),
                GeoJson.RequiredLong(s, "x", context),
                GeoJson.RequiredLong(s, "y", context),
                score,
                ParseComponents(s, context),
                GeoJson.RequiredString(s, "binding", context),
                riverFactor,
                GeoJson.RequiredDouble(s, "depth_m", context),
                GeoJson.RequiredDouble(s, "openness", context),
                GeoJson.RequiredDouble(s, "nearest_farm_km", context),
                GeoJson.RequiredDouble(s, "nearest_river_km", context),
                near, label,
                ParseOcean(s, context),
                area, stage, stageFactor);
        }

        private static IReadOnlyList<KeyValuePair<string, double>> ParseComponents(JsonElement s, string context)
        {
            var obj = GeoJson.RequiredObject(s, "components", context);
            var list = new List<KeyValuePair<string, double>>();
            foreach (var property in obj.EnumerateObject())
            {
                double value = GeoJson.Number(property.Value, $"{context}: field 'components.{property.Name}'");
                if (value < 0 || value > 1)
                    throw new InvalidDataException($"{context}: field 'components.{property.Name}' is {value}, expected within 0..1");
                list.Add(new KeyValuePair<string, double>(property.Name, value));
            }
            if (list.Count == 0) throw new InvalidDataException($"{context}: field 'components' is empty");
            return Array.AsReadOnly(list.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray());
        }

        private static GeoOcean? ParseOcean(JsonElement s, string context)
        {
            if (!GeoJson.Has(s, "ocean")) return null;
            var o = GeoJson.RequiredObject(s, "ocean", context);
            string oc = context + " ocean";
            return new GeoOcean(
                GeoJson.RequiredDouble(o, "current_speed_ms", oc),
                GeoJson.RequiredDouble(o, "hs_m", oc),
                GeoJson.RequiredDouble(o, "o2_mmol_m3", oc));
        }

        /// <summary>Open pens: ranks 1..12 in every area. Offshore: ranks 1..n in every HTH area.</summary>
        private static void ValidateRanks(List<GeoCandidate> all)
        {
            for (int po = 1; po <= ProductionAreas.Count; po++)
            {
                var ranks = all.Where(c => c.IsOpenPen && c.Po == po).Select(c => c.Rank).OrderBy(r => r).ToArray();
                if (!ranks.SequenceEqual(Enumerable.Range(1, OpenPensPerArea)))
                    throw new InvalidDataException(
                        $"{FileName}: PO{po}: field 'rank' of the open pens is [{string.Join(", ", ranks)}], expected 1..{OpenPensPerArea}");
            }
            foreach (var group in all.Where(c => c.IsOffshore).GroupBy(c => c.Area, StringComparer.Ordinal))
            {
                var ranks = group.Select(c => c.Rank).OrderBy(r => r).ToArray();
                if (!ranks.SequenceEqual(Enumerable.Range(1, ranks.Length)))
                    throw new InvalidDataException(
                        $"{FileName}: offshore area '{group.Key}': field 'rank' is [{string.Join(", ", ranks)}], expected 1..{ranks.Length}");
            }
        }
    }
}
