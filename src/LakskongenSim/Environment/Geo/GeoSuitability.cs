using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LakskongenSim.Environment.Geo
{
    /// <summary>
    /// One 500 m suitability cell (docs/geo/GEODATA.md §2.1): R is the score
    /// byte (0 = no score, else score = (R − 1) / 254), G the reason code, B
    /// the mean depth over 4 m. A cell is legal for an open pen when R &gt; 0.
    /// </summary>
    /// <param name="InGrid">False when the point lies outside the area's grid (then every other value is 0).</param>
    /// <param name="ScoreByte">The R byte.</param>
    /// <param name="Reason">The G byte: a hard-constraint code (1-13) or 16 + the binding component (16-24).</param>
    /// <param name="DepthM">B × 4 m (0 on land or where the depth is unknown).</param>
    /// <param name="Legal">R &gt; 0: no hard constraint holds in the cell's best 250 m sub-cell.</param>
    public sealed record SuitabilityCell(bool InGrid, byte ScoreByte, byte Reason, double DepthM, bool Legal)
    {
        /// <summary>The score 0..1 the R byte encodes.</summary>
        public double Score => ScoreByte == 0 ? 0.0 : (ScoreByte - 1) / 254.0;
    }

    /// <summary>A production area's grid georeference from <c>suitability.json</c>.</summary>
    /// <param name="Po">1-13.</param>
    /// <param name="OriginX">EPSG:25833 x of the grid's north-west corner, m.</param>
    /// <param name="OriginY">EPSG:25833 y of the grid's north-west corner, m.</param>
    /// <param name="ResM">Cell size, m.</param>
    /// <param name="Width">Columns.</param>
    /// <param name="Height">Rows (row 0 is north).</param>
    public sealed record GeoSuitabilityGrid(int Po, long OriginX, long OriginY, int ResM, int Width, int Height);

    /// <summary>
    /// The suitability grids of the 13 production areas: <c>suitability.json</c>
    /// and the embedded <c>suitability/poNN.png</c> (track A, A1). Each grid is
    /// decoded lazily, once per process, on its first read.
    /// <see cref="CellAt"/> samples a point: column = floor((x − ox) / res),
    /// row = floor((oy − y) / res), origin at the north-west corner, row 0
    /// north (test_geodata.py's sampler).
    /// </summary>
    public sealed class GeoSuitability
    {
        private const string FileName = "suitability.json";

        /// <summary>The lowest reason code of a scored cell (16 + the binding component).</summary>
        public const byte FirstScoredReason = 16;

        /// <summary>A point outside the area's grid.</summary>
        public static readonly SuitabilityCell Outside = new(false, 0, 0, 0.0, false);

        private static readonly Lazy<GeoSuitability> LazyDefault =
            new(() => Parse(GeoResources.ReadText(GeoResources.SuitabilityJson), GeoResources.ReadBytes));

        private readonly GeoSuitabilityGrid[] _grids;
        private readonly Lazy<PngGrid>[] _images;
        private readonly ReadOnlyDictionary<int, string> _reasons;

        private GeoSuitability(GeoSuitabilityGrid[] grids, Dictionary<int, string> reasons, Func<string, byte[]> readBytes)
        {
            _grids = grids;
            _reasons = new ReadOnlyDictionary<int, string>(reasons);
            _images = grids.Select(g => new Lazy<PngGrid>(() => DecodeGrid(g, readBytes))).ToArray();
        }

        /// <summary>The shipped grids (embedded; each decoded once per process on first use).</summary>
        public static GeoSuitability Default => LazyDefault.Value;

        /// <summary>The reason codes and their names (suitability.json "codes").</summary>
        public IReadOnlyDictionary<int, string> ReasonCodes => _reasons;

        /// <summary>The georeference of the area's grid (1-13; anything else throws).</summary>
        public GeoSuitabilityGrid GridOf(int po)
        {
            if (!ProductionAreas.IsValid(po))
                throw new ArgumentOutOfRangeException(nameof(po), po, "Production areas are numbered 1-13");
            return _grids[po - 1];
        }

        /// <summary>The area's decoded grid image (decoded on the first call, then cached).</summary>
        public PngGrid Image(int po)
        {
            GridOf(po);
            return _images[po - 1].Value;
        }

        /// <summary>How many of the 13 grids have been decoded in this process (tests pin decode-once).</summary>
        internal int DecodedGridCount => _images.Count(i => i.IsValueCreated);

        /// <summary>
        /// The cell of the area's grid holding an EPSG:25833 point (metres), or
        /// <see cref="Outside"/> when the point lies off the grid.
        /// </summary>
        public SuitabilityCell CellAt(int po, long x25833, long y25833)
        {
            var grid = GridOf(po);
            long column = FloorDiv(x25833 - grid.OriginX, grid.ResM);
            long row = FloorDiv(grid.OriginY - y25833, grid.ResM);
            if (column < 0 || row < 0 || column >= grid.Width || row >= grid.Height) return Outside;
            var image = Image(po);
            byte r = image.Sample((int)column, (int)row, 0);
            byte g = image.Sample((int)column, (int)row, 1);
            byte b = image.Sample((int)column, (int)row, 2);
            return new SuitabilityCell(true, r, g, b * 4.0, r > 0);
        }

        private static long FloorDiv(long a, long b)
        {
            long q = a / b;
            return (a % b != 0 && (a < 0) != (b < 0)) ? q - 1 : q;
        }

        private static PngGrid DecodeGrid(GeoSuitabilityGrid grid, Func<string, byte[]> readBytes)
        {
            var image = PngGrid.Decode(readBytes(GeoResources.SuitabilityGrid(grid.Po)));
            if (image.Width != grid.Width || image.Height != grid.Height || image.ColourType != 2)
                throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture,
                    $"suitability/po{grid.Po:00}.png is {image.Width}x{image.Height} colour type {image.ColourType}; {FileName} says {grid.Width}x{grid.Height} RGB"));
            return image;
        }

        /// <summary>
        /// Parse and validate a <c>suitability.json</c> text: EPSG:25833, row 0
        /// north, a grid for each of the 13 areas with a positive size and cell
        /// and the file <c>suitability/poNN.png</c>, and the reason codes.
        /// Throws <see cref="InvalidDataException"/> naming the field.
        /// <paramref name="readBytes"/> reads a grid PNG by its logical name.
        /// </summary>
        internal static GeoSuitability Parse(string json, Func<string, byte[]> readBytes)
        {
            using var doc = GeoJson.Parse(json, FileName);
            var root = doc.RootElement;
            string crs = GeoJson.RequiredString(root, "crs_grid", FileName);
            if (crs != "EPSG:25833") throw new InvalidDataException($"{FileName}: field 'crs_grid' is '{crs}', expected EPSG:25833");
            string row0 = GeoJson.RequiredString(root, "row0", FileName);
            if (row0 != "north") throw new InvalidDataException($"{FileName}: field 'row0' is '{row0}', expected north");

            var reasons = new Dictionary<int, string>();
            foreach (var code in GeoJson.RequiredObject(root, "codes", FileName).EnumerateObject())
            {
                if (!int.TryParse(code.Name, NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n > byte.MaxValue)
                    throw new InvalidDataException($"{FileName}: field 'codes' has the non-byte key '{code.Name}'");
                if (code.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(code.Value.GetString()))
                    throw new InvalidDataException($"{FileName}: field 'codes.{code.Name}' must be a non-empty string");
                reasons[n] = code.Value.GetString()!;
            }

            var gridsElement = GeoJson.RequiredObject(root, "grids", FileName);
            var grids = new GeoSuitabilityGrid[ProductionAreas.Count];
            for (int po = 1; po <= ProductionAreas.Count; po++)
            {
                string context = string.Create(CultureInfo.InvariantCulture, $"{FileName}: grid {po}");
                var g = GeoJson.RequiredObject(gridsElement, po.ToString(CultureInfo.InvariantCulture), FileName + " grids");
                string file = GeoJson.RequiredString(g, "file", context);
                string expectedFile = string.Create(CultureInfo.InvariantCulture, $"suitability/po{po:00}.png");
                if (file != expectedFile) throw new InvalidDataException($"{context}: field 'file' is '{file}', expected '{expectedFile}'");
                var origin = GeoJson.RequiredArray(g, "origin_25833", context);
                if (origin.GetArrayLength() != 2 || !GeoJson.TryInteger(origin[0], out long ox) || !GeoJson.TryInteger(origin[1], out long oy))
                    throw new InvalidDataException($"{context}: field 'origin_25833' must hold two integers");
                int res = GeoJson.RequiredInt(g, "res_m", context);
                int width = GeoJson.RequiredInt(g, "width", context);
                int height = GeoJson.RequiredInt(g, "height", context);
                if (res <= 0) throw new InvalidDataException($"{context}: field 'res_m' must be positive");
                if (width <= 0 || height <= 0) throw new InvalidDataException($"{context}: field 'width' and 'height' must be positive");
                grids[po - 1] = new GeoSuitabilityGrid(po, ox, oy, res, width, height);
            }
            return new GeoSuitability(grids, reasons, readBytes);
        }
    }
}
