using System.Collections.Generic;
using System.Threading.Tasks;

namespace LakskongenSim.Environment
{
    /// <summary>The two trees of the fjord slopes.</summary>
    public enum TreeSpecies : byte
    {
        /// <summary>Norway spruce: planted stands on the lower slopes.</summary>
        Spruce = 0,
        /// <summary>Downy birch: the fjord-side woods up to the treeline.</summary>
        Birch = 1,
    }

    /// <summary>One tree: base position (world metres), species, size, heading and a colour jitter.</summary>
    public readonly record struct TreeInstance(float X, float Y, float Z, TreeSpecies Species, float Scale, float Yaw, float Tint);

    /// <summary>
    /// Where vegetation may grow (G2-02). The slopes and altitudes follow
    /// western Norwegian fjord sides: birch woods from just above the splash
    /// zone to a treeline at 450-550 m, spruce stands low on the gentler
    /// ground, nothing on cliffs steeper than about 42° or on the wind-swept
    /// skerries outside the sill.
    /// </summary>
    public sealed record VegetationSettings
    {
        public int Seed { get; init; } = 42;
        /// <summary>Mean treeline altitude; noise moves it by ±<see cref="TreelineAmplitudeM"/>.</summary>
        public float TreelineMeanM { get; init; } = 500f;
        public float TreelineAmplitudeM { get; init; } = 50f;
        /// <summary>Wavelength scale of the treeline noise.</summary>
        public float TreelineNoiseM { get; init; } = 600f;
        /// <summary>Woods thin out over this band below the treeline (krummholz).</summary>
        public float TreelineFadeM { get; init; } = 70f;
        /// <summary>Lowest tree base: above the wet band (1.5 m) and the shingle.</summary>
        public float ShoreClearanceM { get; init; } = 3f;
        /// <summary>tan 30°: closed woods up to this slope.</summary>
        public float FullSlopeTan { get; init; } = 0.57735f;
        /// <summary>
        /// tan 55°: no tree on a steeper slope. Birch clings to the ledges and
        /// gullies of fjord walls up to about this; the cliff bands above it
        /// stay bare.
        /// </summary>
        public float MaxSlopeTan { get; init; } = 1.428148f;
        /// <summary>tan 38°: spruce stands need gentler ground than birch.</summary>
        public float SpruceMaxSlopeTan { get; init; } = 0.781286f;
        /// <summary>How much soil in a gully or on a ledge (cavity above 0.5) eases the slope limit, in tan units per unit cavity.</summary>
        public float SoilSlopeBonus { get; init; } = 0.8f;
        /// <summary>Open-sea exposure (WaterBake) from which the coast is too windy for trees.</summary>
        public float ExposureStart { get; init; } = 0.3f;
        public float ExposureEnd { get; init; } = 0.6f;
        /// <summary>Distance over which a shore inherits the exposure of its water.</summary>
        public float ExposureReachM { get; init; } = 240f;
        /// <summary>Spruce stands give way to birch between these altitudes.</summary>
        public float SpruceFullM { get; init; } = 180f;
        public float SpruceTopM { get; init; } = 340f;
        /// <summary>Largest spruce share of a stand.</summary>
        public float SpruceShareMax { get; init; } = 0.85f;
        /// <summary>Scatter grid: one candidate tree per cell of this size.</summary>
        public float CellM { get; init; } = 5.5f;
        /// <summary>Share of candidate cells that hold a tree in a closed wood.</summary>
        public float FillFraction { get; init; } = 0.8f;
        /// <summary>Density below which a cell holds no tree (the masks' feathered edge).</summary>
        public float MinDensity { get; init; } = 0.05f;
    }

    /// <summary>
    /// Per-vertex vegetation masks on the terrain grid (row-major by Z, the
    /// <see cref="TerrainGenerator"/> layout). The terrain shader reads them
    /// from the vertex colour (TerrainMesh: g forest, b birch share, a meadow)
    /// and the tree scatter samples the same arrays, so the woods painted on
    /// the ground and the trees standing on it agree.
    /// </summary>
    public sealed class VegetationMasks
    {
        public int Size { get; init; }
        public float WorldSizeM { get; init; }
        /// <summary>Wood density 0-1 (0 outside the slope, altitude, shore and exposure masks).</summary>
        public float[] Forest { get; init; } = Array.Empty<float>();
        /// <summary>Birch share of the wood 0-1 (the rest is spruce).</summary>
        public float[] Birch { get; init; } = Array.Empty<float>();
        /// <summary>Meadow 0-1: flat ground just above the shore (grass, hay fields).</summary>
        public float[] Meadow { get; init; } = Array.Empty<float>();
        /// <summary>Exposure of the land to the open sea 0-1 (from the water it faces).</summary>
        public float[] LandExposure { get; init; } = Array.Empty<float>();
    }

    /// <summary>
    /// Builds the vegetation masks and scatters the trees, deterministically
    /// from the heightfield and the seed (no RNG stream: position hashes).
    /// </summary>
    public static class VegetationScatter
    {
        private const int SeedTreeline = 2003, SeedStand = 2111, SeedSpruce = 2203, SeedMeadow = 2309;
        private const int SaltJitterX = 1, SaltJitterZ = 2, SaltAccept = 3, SaltSpecies = 4, SaltScale = 5, SaltYaw = 6, SaltTint = 7;
        /// <summary>tan 8° and tan 14°: meadow on flat ground only.</summary>
        private const float MeadowFlatTan = 0.140541f;
        private const float MeadowSteepTan = 0.249328f;
        private const float MeadowTopM = 60f;
        private const float StandNoiseM = 380f;
        private const float SpruceNoiseM = 520f;
        private const float TwoPi = 6.28318531f;

        /// <summary>Treeline altitude (m) at world (x, z): always within mean ± amplitude.</summary>
        public static float TreelineAt(VegetationSettings s, float x, float z)
            => s.TreelineMeanM + s.TreelineAmplitudeM * OpenSimplex2.Noise2(s.Seed + SeedTreeline, x / s.TreelineNoiseM, z / s.TreelineNoiseM);

        /// <summary>
        /// The masks for a heightfield of n² vertices spanning worldSize
        /// metres, centred on the origin. waterExposure is the WaterBake B
        /// channel (open-sea exposure of water, 0 on land), or null for none.
        /// </summary>
        public static VegetationMasks BuildMasks(float[] heights, int n, float worldSize, VegetationSettings s, float[]? waterExposure)
        {
            if (heights.Length != n * n || n < 3)
                throw new ArgumentException($"heights must hold {n}² values");
            float spacing = worldSize / (n - 1);
            float half = worldSize * 0.5f;
            float[] land = LandExposure(heights, n, spacing, s, waterExposure);
            var forest = new float[n * n];
            var birch = new float[n * n];
            var meadow = new float[n * n];
            Parallel.For(0, n, r =>
            {
                float z = -half + r * spacing;
                for (int c = 0; c < n; c++)
                {
                    int i = r * n + c;
                    float x = -half + c * spacing;
                    float h = heights[i];
                    float slope = SlopeTan(heights, n, spacing, c, r);
                    float soil = TerrainMesh.Cavity(heights, n, spacing, c, r) - 0.5f;
                    float m = MeadowAt(s, h, slope, land[i], x, z);
                    meadow[i] = m;
                    forest[i] = ForestAt(s, h, slope, soil, land[i], x, z) * (1f - 0.85f * m);
                    birch[i] = BirchShareAt(s, h, slope, x, z);
                }
            });
            return new VegetationMasks { Size = n, WorldSizeM = worldSize, Forest = forest, Birch = birch, Meadow = meadow, LandExposure = land };
        }

        /// <summary>
        /// Wood density at a point (before the meadow cut): slope × altitude ×
        /// shelter × stand. <paramref name="soil"/> is the cavity less 0.5:
        /// gullies and ledges (positive) hold soil and carry trees on steeper
        /// ground than ridges and slabs (negative), but never past the limit.
        /// </summary>
        public static float ForestAt(VegetationSettings s, float h, float slopeTan, float soil, float exposure, float x, float z)
        {
            if (h < s.ShoreClearanceM || slopeTan >= s.MaxSlopeTan)
                return 0f;
            float treeline = TreelineAt(s, x, z);
            if (h >= treeline)
                return 0f;
            float limit = Math.Clamp(s.MaxSlopeTan - 0.35f + s.SoilSlopeBonus * soil, s.FullSlopeTan + 0.05f, s.MaxSlopeTan);
            float slope = 1f - SmoothStep(s.FullSlopeTan, limit, slopeTan);
            // Walls carry scrub, not closed woods.
            slope *= 1f - 0.45f * SmoothStep(s.FullSlopeTan, s.MaxSlopeTan, slopeTan);
            float altitude = SmoothStep(s.ShoreClearanceM, s.ShoreClearanceM + 5f, h) * (1f - SmoothStep(treeline - s.TreelineFadeM, treeline, h));
            float shelter = 1f - SmoothStep(s.ExposureStart, s.ExposureEnd, exposure);
            // Stands and glades: the woods are broken, not a carpet.
            float stand = 0.4f + 0.6f * SmoothStep(-0.45f, 0.2f, FractalNoise.Fbm(s.Seed + SeedStand, x / StandNoiseM, z / StandNoiseM, 3));
            return slope * altitude * shelter * stand;
        }

        /// <summary>
        /// Birch share of a wood at a point: spruce stands on the lower,
        /// gentler slopes, birch above them, on the steep walls and to the
        /// treeline.
        /// </summary>
        public static float BirchShareAt(VegetationSettings s, float h, float slopeTan, float x, float z)
        {
            float low = 1f - SmoothStep(s.SpruceFullM, s.SpruceTopM, h);
            float gentle = 1f - SmoothStep(s.SpruceMaxSlopeTan - 0.15f, s.SpruceMaxSlopeTan, slopeTan);
            float patch = SmoothStep(-0.15f, 0.35f, OpenSimplex2.Noise2(s.Seed + SeedSpruce, x / SpruceNoiseM, z / SpruceNoiseM));
            return 1f - s.SpruceShareMax * low * gentle * patch;
        }

        /// <summary>Meadow at a point: nearly flat, sheltered ground just above the shore.</summary>
        public static float MeadowAt(VegetationSettings s, float h, float slopeTan, float exposure, float x, float z)
        {
            if (h < s.ShoreClearanceM - 1f)
                return 0f;
            float flat = 1f - SmoothStep(MeadowFlatTan, MeadowSteepTan, slopeTan);
            float low = SmoothStep(s.ShoreClearanceM - 1f, s.ShoreClearanceM + 1f, h) * (1f - SmoothStep(MeadowTopM * 0.6f, MeadowTopM, h));
            float shelter = 1f - SmoothStep(s.ExposureStart, s.ExposureEnd, exposure);
            float patch = SmoothStep(-0.5f, 0.1f, OpenSimplex2.Noise2(s.Seed + SeedMeadow, x / 260f, z / 260f));
            return flat * low * shelter * patch;
        }

        /// <summary>
        /// Trees for the masks: one candidate per <see cref="VegetationSettings.CellM"/>
        /// cell, jittered inside it, kept with probability density × fill.
        /// Every kept tree also passes the slope, altitude and treeline test at
        /// its own position, so none stands outside the masks, and none stands
        /// inside a clearing (x, z, radius: the shore buildings' yards).
        /// </summary>
        public static List<TreeInstance> Scatter(float[] heights, VegetationMasks masks, VegetationSettings s,
            IReadOnlyList<(float x, float z, float r)>? clearings = null)
        {
            int n = masks.Size;
            float world = masks.WorldSizeM;
            float spacing = world / (n - 1);
            float half = world * 0.5f;
            int cells = (int)(world / s.CellM);
            var rows = new List<TreeInstance>[cells];
            Parallel.For(0, cells, j =>
            {
                var list = new List<TreeInstance>();
                for (int i = 0; i < cells; i++)
                {
                    float x = -half + (i + Hash01(s.Seed, i, j, SaltJitterX)) * s.CellM;
                    float z = -half + (j + Hash01(s.Seed, i, j, SaltJitterZ)) * s.CellM;
                    float density = Bilinear(masks.Forest, n, spacing, half, x, z);
                    if (density < s.MinDensity || Hash01(s.Seed, i, j, SaltAccept) >= density * s.FillFraction)
                        continue;
                    float h = Bilinear(heights, n, spacing, half, x, z);
                    float slope = SlopeTanAt(heights, n, spacing, half, x, z);
                    if (!IsInsideMask(s, h, slope, x, z) || InClearing(clearings, x, z))
                        continue;
                    float birchShare = Bilinear(masks.Birch, n, spacing, half, x, z);
                    var species = Hash01(s.Seed, i, j, SaltSpecies) < birchShare || slope >= s.SpruceMaxSlopeTan || h >= s.SpruceTopM
                        ? TreeSpecies.Birch : TreeSpecies.Spruce;
                    // Mountain birch: smaller and more crooked toward the treeline.
                    float treeline = TreelineAt(s, x, z);
                    float stunting = SmoothStep(treeline - 160f, treeline, h);
                    float baseScale = 0.72f + 0.56f * Hash01(s.Seed, i, j, SaltScale);
                    float scale = baseScale * (1f - 0.45f * stunting);
                    list.Add(new TreeInstance(x, h, z, species, scale, TwoPi * Hash01(s.Seed, i, j, SaltYaw), Hash01(s.Seed, i, j, SaltTint)));
                }
                rows[j] = list;
            });
            var trees = new List<TreeInstance>();
            foreach (var row in rows)
                trees.AddRange(row);
            return trees;
        }

        /// <summary>True inside any of the clearings.</summary>
        public static bool InClearing(IReadOnlyList<(float x, float z, float r)>? clearings, float x, float z)
        {
            if (clearings == null)
                return false;
            foreach (var (cx, cz, r) in clearings)
            {
                if ((x - cx) * (x - cx) + (z - cz) * (z - cz) < r * r)
                    return true;
            }
            return false;
        }

        /// <summary>The hard mask every tree satisfies: above the shore, below the treeline, gentler than the cliff limit.</summary>
        public static bool IsInsideMask(VegetationSettings s, float h, float slopeTan, float x, float z)
            => h >= s.ShoreClearanceM && h < TreelineAt(s, x, z) && slopeTan < s.MaxSlopeTan;

        /// <summary>Slope (rise over run) at a world point from the bilinear heightfield's central differences.</summary>
        public static float SlopeTanAt(float[] heights, int n, float spacing, float half, float x, float z)
        {
            float e = spacing * 0.5f;
            float dx = (Bilinear(heights, n, spacing, half, x + e, z) - Bilinear(heights, n, spacing, half, x - e, z)) / (2f * e);
            float dz = (Bilinear(heights, n, spacing, half, x, z + e) - Bilinear(heights, n, spacing, half, x, z - e)) / (2f * e);
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Bilinear sample of an n² grid (row-major by Z) at world (x, z), clamped to the grid.</summary>
        public static float Bilinear(float[] grid, int n, float spacing, float half, float x, float z)
        {
            float fx = Math.Clamp((x + half) / spacing, 0f, n - 1f);
            float fz = Math.Clamp((z + half) / spacing, 0f, n - 1f);
            int ix = Math.Min((int)fx, n - 2), iz = Math.Min((int)fz, n - 2);
            float tx = fx - ix, tz = fz - iz;
            float a = grid[iz * n + ix] + (grid[iz * n + ix + 1] - grid[iz * n + ix]) * tx;
            float b = grid[(iz + 1) * n + ix] + (grid[(iz + 1) * n + ix + 1] - grid[(iz + 1) * n + ix]) * tx;
            return a + (b - a) * tz;
        }

        private static float SlopeTan(float[] heights, int n, float spacing, int c, int r)
        {
            int cl = Math.Max(c - 1, 0), cr = Math.Min(c + 1, n - 1);
            int rd = Math.Max(r - 1, 0), ru = Math.Min(r + 1, n - 1);
            float dx = (heights[r * n + cr] - heights[r * n + cl]) / ((cr - cl) * spacing);
            float dz = (heights[ru * n + c] - heights[rd * n + c]) / ((ru - rd) * spacing);
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// Exposure of the land: the most exposed water within
        /// <see cref="VegetationSettings.ExposureReachM"/> (a separable max filter
        /// over the water's exposure), so skerries and headlands outside the sill
        /// take the open coast's wind and the fjord sides the basin's shelter.
        /// </summary>
        private static float[] LandExposure(float[] heights, int n, float spacing, VegetationSettings s, float[]? water)
        {
            var result = new float[n * n];
            if (water == null)
                return result;
            int radius = Math.Max(1, (int)(s.ExposureReachM / spacing));
            var rowsMax = new float[n * n];
            Parallel.For(0, n, r =>
            {
                for (int c = 0; c < n; c++)
                {
                    float m = 0f;
                    int c0 = Math.Max(c - radius, 0), c1 = Math.Min(c + radius, n - 1);
                    for (int k = c0; k <= c1; k++)
                        m = Math.Max(m, water[r * n + k]);
                    rowsMax[r * n + c] = m;
                }
            });
            Parallel.For(0, n, c =>
            {
                for (int r = 0; r < n; r++)
                {
                    float m = 0f;
                    int r0 = Math.Max(r - radius, 0), r1 = Math.Min(r + radius, n - 1);
                    for (int k = r0; k <= r1; k++)
                        m = Math.Max(m, rowsMax[k * n + c]);
                    result[r * n + c] = m;
                }
            });
            return result;
        }

        /// <summary>Deterministic value in [0, 1) for a cell and a salt.</summary>
        private static float Hash01(int seed, int i, int j, int salt)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u ^ (uint)i * 0x85EBCA77u ^ (uint)j * 0xC2B2AE3Du ^ (uint)salt * 0x27D4EB2Fu;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                h *= 0x297A2D39u;
                h ^= h >> 15;
                return (h >> 8) * (1f / 16777216f);
            }
        }

        private static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = (x - edge0) / (edge1 - edge0);
            t = t < 0f ? 0f : (t > 1f ? 1f : t);
            return t * t * (3f - 2f * t);
        }
    }
}
