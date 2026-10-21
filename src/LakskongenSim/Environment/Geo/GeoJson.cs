using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace LakskongenSim.Environment.Geo
{
    /// <summary>
    /// Strict readers for the geo JSON and GeoJSON files: every missing or
    /// mistyped field throws <see cref="InvalidDataException"/> naming the file,
    /// the record and the field, so a defect in shipped data fails loudly at
    /// load (and in the tests) instead of reaching the map as a zero.
    /// </summary>
    internal static class GeoJson
    {
        /// <summary>Parse a whole document (comments and trailing commas are refused: the files are machine-written).</summary>
        internal static JsonDocument Parse(string json, string file)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException($"{file} is empty");
            try
            {
                return JsonDocument.Parse(json);
            }
            catch (JsonException e)
            {
                throw new InvalidDataException($"{file} is not valid JSON: {e.Message}", e);
            }
        }

        internal static JsonElement Required(JsonElement obj, string name, string context)
        {
            if (obj.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"{context}: expected an object holding '{name}'");
            if (!obj.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
                throw new InvalidDataException($"{context}: field '{name}' is missing");
            return value;
        }

        internal static bool Has(JsonElement obj, string name) =>
            obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null;

        internal static string RequiredString(JsonElement obj, string name, string context)
        {
            var v = Required(obj, name, context);
            if (v.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(v.GetString()))
                throw new InvalidDataException($"{context}: field '{name}' must be a non-empty string");
            return v.GetString()!;
        }

        internal static string? OptionalString(JsonElement obj, string name, string context) =>
            Has(obj, name) ? RequiredString(obj, name, context) : null;

        internal static double RequiredDouble(JsonElement obj, string name, string context)
        {
            var v = Required(obj, name, context);
            if (v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out double d) || !double.IsFinite(d))
                throw new InvalidDataException($"{context}: field '{name}' must be a finite number");
            return d;
        }

        internal static double? OptionalDouble(JsonElement obj, string name, string context) =>
            Has(obj, name) ? RequiredDouble(obj, name, context) : null;

        internal static long RequiredLong(JsonElement obj, string name, string context)
        {
            var v = Required(obj, name, context);
            if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt64(out long n))
                throw new InvalidDataException($"{context}: field '{name}' must be an integer");
            return n;
        }

        internal static int RequiredInt(JsonElement obj, string name, string context)
        {
            long n = RequiredLong(obj, name, context);
            if (n < int.MinValue || n > int.MaxValue)
                throw new InvalidDataException($"{context}: field '{name}' is out of the integer range");
            return (int)n;
        }

        internal static bool OptionalBool(JsonElement obj, string name, string context)
        {
            if (!Has(obj, name)) return false;
            var v = obj.GetProperty(name);
            if (v.ValueKind != JsonValueKind.True && v.ValueKind != JsonValueKind.False)
                throw new InvalidDataException($"{context}: field '{name}' must be true or false");
            return v.GetBoolean();
        }

        internal static JsonElement RequiredArray(JsonElement obj, string name, string context)
        {
            var v = Required(obj, name, context);
            if (v.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException($"{context}: field '{name}' must be an array");
            return v;
        }

        internal static JsonElement RequiredObject(JsonElement obj, string name, string context)
        {
            var v = Required(obj, name, context);
            if (v.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"{context}: field '{name}' must be an object");
            return v;
        }

        /// <summary>A two-number array ([lon, lat] or [x, y]).</summary>
        internal static (double A, double B) RequiredPair(JsonElement obj, string name, string context)
        {
            var v = RequiredArray(obj, name, context);
            if (v.GetArrayLength() != 2)
                throw new InvalidDataException($"{context}: field '{name}' must hold two numbers");
            return (Number(v[0], $"{context}: field '{name}'"), Number(v[1], $"{context}: field '{name}'"));
        }

        /// <summary>
        /// An array element read as an integer: false for a fraction and for a
        /// non-number (a string, null or bool), where <see cref="JsonElement.TryGetInt64"/>
        /// would throw <see cref="InvalidOperationException"/> instead of letting
        /// the caller name the field.
        /// </summary>
        internal static bool TryInteger(JsonElement v, out long value)
        {
            value = 0;
            return v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out value);
        }

        internal static double Number(JsonElement v, string context)
        {
            if (v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out double d) || !double.IsFinite(d))
                throw new InvalidDataException($"{context} must hold finite numbers");
            return d;
        }

        /// <summary>The features of a GeoJSON FeatureCollection.</summary>
        internal static JsonElement Features(JsonElement root, string file)
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type)
                || type.GetString() != "FeatureCollection")
                throw new InvalidDataException($"{file}: not a GeoJSON FeatureCollection");
            return RequiredArray(root, "features", file);
        }

        internal static string Context(string file, string what, object id) =>
            string.Create(CultureInfo.InvariantCulture, $"{file}: {what} {id}");
    }

    /// <summary>
    /// A GeoJSON Polygon or MultiPolygon in plane coordinates (lon/lat, or
    /// UTM metres once projected): each polygon is an outer ring and its holes
    /// (RFC 7946). <see cref="Contains"/> is the even-odd rule of
    /// <c>tests/test_geodata.py::point_in_geom</c>: inside a polygon's outer
    /// ring and in none of its holes. Pure arithmetic, immutable.
    /// </summary>
    internal sealed class GeoMultiPolygon
    {
        private readonly Ring[][] _polygons;

        private GeoMultiPolygon(Ring[][] polygons)
        {
            _polygons = polygons;
            MinX = MinY = double.PositiveInfinity;
            MaxX = MaxY = double.NegativeInfinity;
            foreach (var polygon in polygons)
            {
                var outer = polygon[0];
                for (int i = 0; i < outer.Xs.Length; i++)
                {
                    MinX = Math.Min(MinX, outer.Xs[i]);
                    MaxX = Math.Max(MaxX, outer.Xs[i]);
                    MinY = Math.Min(MinY, outer.Ys[i]);
                    MaxY = Math.Max(MaxY, outer.Ys[i]);
                }
            }
        }

        /// <summary>Bounding box of the outer rings.</summary>
        internal double MinX { get; }
        internal double MinY { get; }
        internal double MaxX { get; }
        internal double MaxY { get; }

        /// <summary>Number of polygons (1 for a Polygon).</summary>
        internal int PolygonCount => _polygons.Length;

        /// <summary>Number of holes over all polygons.</summary>
        internal int HoleCount
        {
            get
            {
                int holes = 0;
                foreach (var polygon in _polygons) holes += polygon.Length - 1;
                return holes;
            }
        }

        /// <summary>Read a GeoJSON geometry; anything but a Polygon or MultiPolygon with rings of 4+ points throws.</summary>
        internal static GeoMultiPolygon FromGeoJson(JsonElement geometry, string context)
        {
            string type = GeoJson.RequiredString(geometry, "type", context + " geometry");
            var coordinates = GeoJson.RequiredArray(geometry, "coordinates", context + " geometry");
            var polygons = new List<Ring[]>();
            switch (type)
            {
                case "Polygon":
                    polygons.Add(ReadPolygon(coordinates, context));
                    break;
                case "MultiPolygon":
                    foreach (var polygon in coordinates.EnumerateArray())
                        polygons.Add(ReadPolygon(polygon, context));
                    break;
                default:
                    throw new InvalidDataException($"{context}: geometry type '{type}' is not a Polygon or MultiPolygon");
            }
            if (polygons.Count == 0) throw new InvalidDataException($"{context}: geometry has no polygon");
            return new GeoMultiPolygon(polygons.ToArray());
        }

        private static Ring[] ReadPolygon(JsonElement polygon, string context)
        {
            if (polygon.ValueKind != JsonValueKind.Array || polygon.GetArrayLength() == 0)
                throw new InvalidDataException($"{context}: a polygon has no rings");
            var rings = new List<Ring>();
            foreach (var ring in polygon.EnumerateArray())
            {
                if (ring.ValueKind != JsonValueKind.Array || ring.GetArrayLength() < 4)
                    throw new InvalidDataException($"{context}: a ring has fewer than 4 points");
                var xs = new double[ring.GetArrayLength()];
                var ys = new double[xs.Length];
                int i = 0;
                foreach (var point in ring.EnumerateArray())
                {
                    if (point.ValueKind != JsonValueKind.Array || point.GetArrayLength() < 2)
                        throw new InvalidDataException($"{context}: a ring point is not [x, y]");
                    xs[i] = GeoJson.Number(point[0], context + " ring point");
                    ys[i] = GeoJson.Number(point[1], context + " ring point");
                    i++;
                }
                rings.Add(new Ring(xs, ys));
            }
            return rings.ToArray();
        }

        /// <summary>True inside a polygon's outer ring and outside all of that polygon's holes.</summary>
        internal bool Contains(double x, double y)
        {
            if (x < MinX || x > MaxX || y < MinY || y > MaxY) return false;
            foreach (var polygon in _polygons)
            {
                if (!polygon[0].Contains(x, y)) continue;
                bool inHole = false;
                for (int h = 1; h < polygon.Length && !inHole; h++)
                    inHole = polygon[h].Contains(x, y);
                if (!inHole) return true;
            }
            return false;
        }

        /// <summary>The same shape with every vertex mapped (for example lon/lat to UTM 33).</summary>
        internal GeoMultiPolygon Map(Func<double, double, (double X, double Y)> project)
        {
            var polygons = new Ring[_polygons.Length][];
            for (int p = 0; p < _polygons.Length; p++)
            {
                polygons[p] = new Ring[_polygons[p].Length];
                for (int r = 0; r < _polygons[p].Length; r++)
                {
                    var src = _polygons[p][r];
                    var xs = new double[src.Xs.Length];
                    var ys = new double[src.Ys.Length];
                    for (int i = 0; i < xs.Length; i++)
                        (xs[i], ys[i]) = project(src.Xs[i], src.Ys[i]);
                    polygons[p][r] = new Ring(xs, ys);
                }
            }
            return new GeoMultiPolygon(polygons);
        }

        private sealed class Ring
        {
            internal Ring(double[] xs, double[] ys)
            {
                Xs = xs;
                Ys = ys;
            }

            internal double[] Xs { get; }
            internal double[] Ys { get; }

            /// <summary>Even-odd crossing test (test_geodata.py::point_in_ring, the same comparison order).</summary>
            internal bool Contains(double x, double y)
            {
                bool inside = false;
                for (int i = 0, j = Xs.Length - 1; i < Xs.Length; j = i++)
                {
                    double xi = Xs[i], yi = Ys[i], xj = Xs[j], yj = Ys[j];
                    if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi)
                        inside = !inside;
                }
                return inside;
            }
        }
    }
}
