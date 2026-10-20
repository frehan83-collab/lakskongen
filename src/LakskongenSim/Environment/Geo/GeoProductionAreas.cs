using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace LakskongenSim.Environment.Geo
{
    /// <summary>
    /// One production area as the national map data draws it
    /// (<c>production_areas.regional.geojson</c>; docs/geo/GEODATA.md §2.1).
    /// <see cref="ProductionAreas"/> stays the source of names, temperatures,
    /// lice weeks and licence sizes; this adds the area's shape and the map
    /// attributes.
    /// </summary>
    /// <param name="Number">1-13.</param>
    /// <param name="Code">"PO1".."PO13".</param>
    /// <param name="Name">The official name, equal to <see cref="ProductionAreaInfo.Name"/>.</param>
    /// <param name="OfficialTrafficLight">The source's traffic-light status: green, yellow or red. Not the engine's light.</param>
    /// <param name="Tint">Light hex colour for the national map ("#RRGGBB"); four hues alternate so neighbours differ.</param>
    /// <param name="Label">Label point (lon, lat): the centroid of the area's coastal sea cells, snapped onto its water.</param>
    /// <param name="Label25833">The label point in EPSG:25833 metres, as the pipeline wrote it.</param>
    /// <param name="Bbox25833">Bounding box in EPSG:25833 metres: min x, min y, max x, max y. Each read returns a copy.</param>
    /// <param name="AreaKm2">Area of the polygon (land and sea), km².</param>
    /// <param name="SeaAreaKm2">Sea part of the area, km².</param>
    public sealed record GeoProductionArea(
        int Number,
        string Code,
        string Name,
        string OfficialTrafficLight,
        string Tint,
        (double Lon, double Lat) Label,
        (long X, long Y) Label25833,
        long[] Bbox25833,
        double AreaKm2,
        double SeaAreaKm2)
    {
        private readonly long[] _bbox25833 = (long[])Bbox25833.Clone();

        /// <summary>Bounding box in EPSG:25833 metres (min x, min y, max x, max y): a fresh copy on every read.</summary>
        public long[] Bbox25833
        {
            get => (long[])_bbox25833.Clone();
            init => _bbox25833 = (long[])value.Clone();
        }
    }

    /// <summary>
    /// The 13 production areas' polygons and map attributes, read from the
    /// embedded <c>production_areas.regional.geojson</c> (track A, A1).
    /// <see cref="AreaOf"/> answers "which area is this point in" with pure
    /// arithmetic on the lon/lat rings (safe for the sim); <see cref="AreaAtUtm"/>
    /// answers it for EPSG:25833 points on rings projected once with
    /// <see cref="Utm33"/> (display and bridge only). Loading checks the 13 ids
    /// and that every name equals <see cref="ProductionAreas"/>' own.
    /// </summary>
    public sealed class GeoProductionAreas
    {
        private const string FileName = "production_areas.regional.geojson";

        /// <summary>The source's traffic-light values.</summary>
        public static readonly IReadOnlyList<string> TrafficLights = new[] { "green", "yellow", "red" };

        private static readonly Lazy<GeoProductionAreas> LazyDefault =
            new(() => Parse(GeoResources.ReadText(GeoResources.ProductionAreasGeoJson)));

        private readonly ReadOnlyCollection<GeoProductionArea> _areas;
        private readonly GeoMultiPolygon[] _lonLat;
        private readonly Lazy<GeoMultiPolygon[]> _utm;

        private GeoProductionAreas(GeoProductionArea[] areas, GeoMultiPolygon[] lonLat)
        {
            _areas = Array.AsReadOnly(areas);
            _lonLat = lonLat;
            _utm = new Lazy<GeoMultiPolygon[]>(() => _lonLat.Select(p => p.Map(Utm33.Forward)).ToArray());
        }

        /// <summary>The shipped areas (embedded; parsed once per process).</summary>
        public static GeoProductionAreas Default => LazyDefault.Value;

        /// <summary>All 13 areas, PO1 first.</summary>
        public IReadOnlyList<GeoProductionArea> All => _areas;

        /// <summary>The area numbered <paramref name="po"/> (1-13); anything else throws.</summary>
        public GeoProductionArea Get(int po)
        {
            if (!ProductionAreas.IsValid(po))
                throw new ArgumentOutOfRangeException(nameof(po), po, "Production areas are numbered 1-13");
            return _areas[po - 1];
        }

        /// <summary>
        /// The area whose polygon holds the point (lon, lat in degrees), or 0
        /// when none does. Even-odd test over every polygon and hole, lowest
        /// number first. Pure arithmetic on the shipped coordinates: safe for the sim.
        /// </summary>
        public int AreaOf(double lonDeg, double latDeg) => FirstContaining(_lonLat, lonDeg, latDeg);

        /// <summary>
        /// The area holding an EPSG:25833 point (metres), or 0. Tests the rings
        /// projected once with <see cref="Utm33"/>, so it is for the display and
        /// the bridge only, never game state.
        /// </summary>
        public int AreaAtUtm(double x, double y) => FirstContaining(_utm.Value, x, y);

        /// <summary>The area's polygon in lon/lat (tests and the bridge).</summary>
        internal GeoMultiPolygon PolygonOf(int po)
        {
            Get(po);
            return _lonLat[po - 1];
        }

        private static int FirstContaining(GeoMultiPolygon[] polygons, double x, double y)
        {
            for (int i = 0; i < polygons.Length; i++)
                if (polygons[i].Contains(x, y))
                    return i + 1;
            return 0;
        }

        /// <summary>
        /// Parse and validate a production-areas GeoJSON text: 13 features with
        /// ids 1..13 in order, codes "PO1".."PO13", names equal to
        /// <see cref="ProductionAreas"/>' names, a known traffic light, a
        /// "#RRGGBB" tint, a 4-number bbox and Polygon or MultiPolygon rings.
        /// Throws <see cref="InvalidDataException"/> naming the area and the field.
        /// </summary>
        public static GeoProductionAreas Parse(string json)
        {
            using var doc = GeoJson.Parse(json, FileName);
            var features = GeoJson.Features(doc.RootElement, FileName);
            if (features.GetArrayLength() != ProductionAreas.Count)
                throw new InvalidDataException($"{FileName}: expected {ProductionAreas.Count} areas, found {features.GetArrayLength()}");

            var areas = new GeoProductionArea[ProductionAreas.Count];
            var polygons = new GeoMultiPolygon[ProductionAreas.Count];
            int index = 0;
            foreach (var feature in features.EnumerateArray())
            {
                int expected = index + 1;
                string context = GeoJson.Context(FileName, "area", expected);
                var p = GeoJson.RequiredObject(feature, "properties", context);
                int id = GeoJson.RequiredInt(p, "id", context);
                if (id != expected)
                    throw new InvalidDataException($"{context}: field 'id' is {id}; the areas must be numbered 1-13 in order");
                areas[index] = ParseArea(p, id, context);
                polygons[index] = GeoMultiPolygon.FromGeoJson(GeoJson.RequiredObject(feature, "geometry", context), context);
                index++;
            }
            return new GeoProductionAreas(areas, polygons);
        }

        private static GeoProductionArea ParseArea(System.Text.Json.JsonElement p, int id, string context)
        {
            string code = GeoJson.RequiredString(p, "code", context);
            if (code != "PO" + id)
                throw new InvalidDataException($"{context}: field 'code' is '{code}', expected 'PO{id}'");
            string name = GeoJson.RequiredString(p, "name", context);
            if (name != ProductionAreas.Get(id).Name)
                throw new InvalidDataException($"{context}: field 'name' is '{name}', expected ProductionAreas' '{ProductionAreas.Get(id).Name}'");
            string light = GeoJson.RequiredString(p, "traffic_light", context);
            if (!TrafficLights.Contains(light))
                throw new InvalidDataException($"{context}: field 'traffic_light' is '{light}', expected green, yellow or red");
            string tint = GeoJson.RequiredString(p, "tint", context);
            if (tint.Length != 7 || tint[0] != '#' || !tint.Skip(1).All(Uri.IsHexDigit))
                throw new InvalidDataException($"{context}: field 'tint' is '{tint}', expected #RRGGBB");
            var bboxElement = GeoJson.RequiredArray(p, "bbox_25833", context);
            if (bboxElement.GetArrayLength() != 4)
                throw new InvalidDataException($"{context}: field 'bbox_25833' must hold four integers");
            var bbox = bboxElement.EnumerateArray().Select(v => GeoJson.TryInteger(v, out long n)
                ? n
                : throw new InvalidDataException($"{context}: field 'bbox_25833' must hold four integers")).ToArray();
            if (bbox[0] >= bbox[2] || bbox[1] >= bbox[3])
                throw new InvalidDataException($"{context}: field 'bbox_25833' is not min x, min y, max x, max y");
            var label = GeoJson.RequiredPair(p, "label", context);
            var label25833 = GeoJson.RequiredArray(p, "label_25833", context);
            if (label25833.GetArrayLength() != 2 || !GeoJson.TryInteger(label25833[0], out long lx) || !GeoJson.TryInteger(label25833[1], out long ly))
                throw new InvalidDataException($"{context}: field 'label_25833' must hold two integers");
            double areaKm2 = GeoJson.RequiredDouble(p, "area_km2", context);
            double seaKm2 = GeoJson.RequiredDouble(p, "sea_area_km2", context);
            if (areaKm2 <= 0 || seaKm2 <= 0 || seaKm2 > areaKm2)
                throw new InvalidDataException($"{context}: field 'sea_area_km2' must be positive and at most 'area_km2'");
            return new GeoProductionArea(id, code, name, light, tint, label, (lx, ly), bbox, areaKm2, seaKm2);
        }
    }
}
