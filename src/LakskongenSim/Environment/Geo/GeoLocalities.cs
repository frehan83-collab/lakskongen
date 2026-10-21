using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LakskongenSim.Environment.Geo
{
    /// <summary>
    /// One active aquaculture site from <c>localities.geojson</c> (docs/geo/GEODATA.md
    /// §2.1). No owner, holder or operator field exists (D17): rivals get
    /// generated parody names elsewhere.
    /// </summary>
    /// <param name="Id">The registry's locality number (loknr).</param>
    /// <param name="Name">Title-case place name of the site.</param>
    /// <param name="Kind">"&lt;placement&gt;_&lt;group&gt;": sea/land/offshore × salmonid/marine_fish/shellfish/algae/other.</param>
    /// <param name="Stage">growout, smolt, broodstock, ranching, nursery or other.</param>
    /// <param name="MtbT">Maximum allowed biomass in tonnes, when the capacity is in tonnes; else null.</param>
    /// <param name="Po">Production area 1-13, or 0 when the registry names none.</param>
    /// <param name="X">EPSG:25833 easting, m.</param>
    /// <param name="Y">EPSG:25833 northing, m.</param>
    /// <param name="Municipality">The site's municipality.</param>
    /// <param name="HarvestPen">True when registered as a harvest (slaughter) pen.</param>
    public sealed record GeoLocality(
        int Id,
        string Name,
        string Kind,
        string Stage,
        double? MtbT,
        int Po,
        int X,
        int Y,
        string Municipality,
        bool HarvestPen)
    {
        /// <summary>True for a salmonid site of any placement (sea, land or offshore).</summary>
        public bool IsSalmonid => Kind.EndsWith("_salmonid", StringComparison.Ordinal);

        /// <summary>True for a producing sea grow-out salmonid site: sea_salmonid, grow-out, not a harvest pen.</summary>
        public bool IsSeaGrowoutProducing => Kind == "sea_salmonid" && Stage == "growout" && !HarvestPen;
    }

    /// <summary>
    /// The 1,781 active aquaculture localities of <c>localities.geojson</c>,
    /// read from the embedded copy once per process and sorted by id (track A,
    /// A1). Views: <see cref="Salmonid"/> (sea, land and offshore salmonid
    /// sites) and <see cref="SeaGrowoutProducing"/> (sea salmonid grow-out
    /// without harvest pens: the sites a rival layout distributes).
    /// </summary>
    public sealed class GeoLocalities
    {
        private const string FileName = "localities.geojson";

        private static readonly HashSet<string> Placements = new(StringComparer.Ordinal) { "sea", "land", "offshore" };
        private static readonly HashSet<string> Groups = new(StringComparer.Ordinal) { "salmonid", "marine_fish", "shellfish", "algae", "other" };

        private static readonly Lazy<GeoLocalities> LazyDefault =
            new(() => Parse(GeoResources.ReadText(GeoResources.LocalitiesGeoJson)));

        private readonly ReadOnlyCollection<GeoLocality> _all;
        private readonly ReadOnlyCollection<GeoLocality> _salmonid;
        private readonly ReadOnlyCollection<GeoLocality> _seaGrowoutProducing;
        private readonly Dictionary<int, GeoLocality> _byId;

        private GeoLocalities(GeoLocality[] all)
        {
            _all = Array.AsReadOnly(all);
            _salmonid = Array.AsReadOnly(all.Where(l => l.IsSalmonid).ToArray());
            _seaGrowoutProducing = Array.AsReadOnly(all.Where(l => l.IsSeaGrowoutProducing).ToArray());
            _byId = all.ToDictionary(l => l.Id);
        }

        /// <summary>The shipped localities (embedded; parsed once per process).</summary>
        public static GeoLocalities Default => LazyDefault.Value;

        /// <summary>Every locality, sorted by id.</summary>
        public IReadOnlyList<GeoLocality> All => _all;

        /// <summary>Salmonid sites at sea, on land and offshore, sorted by id.</summary>
        public IReadOnlyList<GeoLocality> Salmonid => _salmonid;

        /// <summary>Sea salmonid grow-out sites without harvest pens, sorted by id.</summary>
        public IReadOnlyList<GeoLocality> SeaGrowoutProducing => _seaGrowoutProducing;

        /// <summary>The locality with this id.</summary>
        public bool TryGet(int id, [MaybeNullWhen(false)] out GeoLocality l) => _byId.TryGetValue(id, out l);

        /// <summary>
        /// Parse and validate a localities GeoJSON text: unique integer ids, a
        /// known kind, a stage, a name and municipality, integer x/y, a
        /// production area of 1-13 or null, a non-negative mtb_t. Throws
        /// <see cref="InvalidDataException"/> naming the locality and the field.
        /// </summary>
        public static GeoLocalities Parse(string json)
        {
            using var doc = GeoJson.Parse(json, FileName);
            var features = GeoJson.Features(doc.RootElement, FileName);
            var all = new List<GeoLocality>(features.GetArrayLength());
            var seen = new HashSet<int>();
            int index = 0;
            foreach (var feature in features.EnumerateArray())
            {
                var p = GeoJson.RequiredObject(feature, "properties", GeoJson.Context(FileName, "feature #", index));
                int id = GeoJson.RequiredInt(p, "id", GeoJson.Context(FileName, "feature #", index));
                string context = GeoJson.Context(FileName, "locality", id);
                if (!seen.Add(id)) throw new InvalidDataException($"{context}: field 'id' appears twice");
                all.Add(ParseLocality(p, id, context));
                index++;
            }
            return new GeoLocalities(all.OrderBy(l => l.Id).ToArray());
        }

        private static GeoLocality ParseLocality(JsonElement p, int id, string context)
        {
            string kind = GeoJson.RequiredString(p, "kind", context);
            int cut = kind.IndexOf('_');
            if (cut <= 0 || !Placements.Contains(kind[..cut]) || !Groups.Contains(kind[(cut + 1)..]))
                throw new InvalidDataException($"{context}: field 'kind' is '{kind}', expected <sea|land|offshore>_<group>");
            int po = 0;
            if (GeoJson.Has(p, "po"))
            {
                po = GeoJson.RequiredInt(p, "po", context);
                if (!ProductionAreas.IsValid(po))
                    throw new InvalidDataException($"{context}: field 'po' is {po}, expected 1-13 or null");
            }
            double? mtb = GeoJson.OptionalDouble(p, "mtb_t", context);
            if (mtb < 0) throw new InvalidDataException($"{context}: field 'mtb_t' is negative");
            return new GeoLocality(
                id,
                GeoJson.RequiredString(p, "name", context),
                kind,
                GeoJson.RequiredString(p, "stage", context),
                mtb,
                po,
                GeoJson.RequiredInt(p, "x", context),
                GeoJson.RequiredInt(p, "y", context),
                GeoJson.RequiredString(p, "municipality", context),
                GeoJson.OptionalBool(p, "harvest_pen", context));
        }
    }
}
