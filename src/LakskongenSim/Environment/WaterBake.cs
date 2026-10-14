using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LakskongenSim.Environment
{
    /// <summary>
    /// What the opaque water shader knows about the ground under it (G1-05):
    /// one texel per heightfield vertex, four channels.
    /// </summary>
    /// <param name="Size">Texels per side (the heightfield resolution).</param>
    /// <param name="Rgba">Row-major by Z, four floats per texel:
    /// R water depth below sea level in metres (negative over land);
    /// G signed distance to the shoreline in metres (positive over water);
    /// B exposure to the open sea, 0 (enclosed basin) to 1 (open coast);
    /// A seabed slope, rise over run, clamped to 2.</param>
    /// <param name="ShelteredFetchM">Effective fetch of the sheltered basin for the sea direction.</param>
    public sealed record WaterBakeResult(int Size, float[] Rgba, double ShelteredFetchM);

    /// <summary>
    /// Bakes the bathymetry, the shore distance field and the open-sea exposure
    /// from the terrain heightfield. Pure compute, deterministic (no RNG).
    ///
    /// <list type="bullet">
    /// <item>Shore distance: an exact Euclidean distance transform (Felzenszwalb
    /// &amp; Huttenlocher 2012) on a grid twice as fine as the heightfield,
    /// whose heights are the bilinear interpolant of the coarse grid, minus half
    /// a fine cell: within ±2 m of the true waterline at a 7.8 m grid.</item>
    /// <item>Exposure: from every point of a coarse sample grid, 16 rays march
    /// over the water; a ray that leaves the world without meeting land lets
    /// the open sea in, less the farther it ran over sheltered water to get
    /// there (exp(−distance / 1.5 km): swell loses itself in a long channel
    /// by refraction and diffraction). An open coast sees about half the
    /// compass at once (exposure 1); a basin a few kilometres up a fjord,
    /// behind a sill and skerries, sees a sliver of it far away (near 0).</item>
    /// <item>Sheltered fetch: the CEM effective fetch (mean over radials
    /// within ±15° of the wind) upwind of the basin's sheltered water, median
    /// over the basin.</item>
    /// </list>
    /// </summary>
    public static class WaterBake
    {
        private const float Infinity = 1e20f;
        /// <summary>Signed distances are clamped to this many metres.</summary>
        public const float MaxShoreDistanceM = 8000f;
        private const int ExposureDirections = 16;
        /// <summary>Exposure sample grid: one sample per this many heightfield texels.</summary>
        private const int ExposureStride = 4;
        /// <summary>Escaping share of the compass that counts as fully exposed (an open coast sees about half).</summary>
        private const float FullExposureShare = 0.4f;
        /// <summary>An escaping ray's weight falls by e over this much water before the world edge.</summary>
        private const float ExposureDecayM = 1500f;
        private const int ExposureBlurPasses = 2;
        /// <summary>Exposure below which water counts as sheltered basin.</summary>
        public const float ShelteredExposure = 0.25f;
        private const double FetchArcDeg = 15.0;
        private const double FetchArcStepDeg = 5.0;
        /// <summary>Fetch sample grid: one basin sample per this many heightfield texels.</summary>
        private const int FetchStride = 8;
        private const float MaxSlope = 2f;

        /// <summary>
        /// Bake the water texture for a heightfield (row-major by Z, the
        /// TerrainGenerator layout) of n² vertices spanning worldSize metres,
        /// centred on the origin. seaDirection is the direction the wind sea
        /// travels (x, z); the fetch is measured upwind of it.
        /// </summary>
        public static WaterBakeResult Build(float[] heights, int n, float worldSize, float seaLevel,
            float seaDirX, float seaDirZ)
        {
            if (heights.Length != n * n || n < 2)
                throw new ArgumentException($"heights must hold {n}² values");
            float spacing = worldSize / (n - 1);
            var rgba = new float[n * n * 4];
            float[] shore = ShoreDistance(heights, n, spacing, seaLevel);
            float[] exposure = Exposure(heights, n, spacing, seaLevel);
            Parallel.For(0, n, r =>
            {
                for (int c = 0; c < n; c++)
                {
                    int i = r * n + c;
                    float h = heights[i];
                    float gx = (heights[r * n + Math.Min(c + 1, n - 1)] - heights[r * n + Math.Max(c - 1, 0)]) / (2f * spacing);
                    float gz = (heights[Math.Min(r + 1, n - 1) * n + c] - heights[Math.Max(r - 1, 0) * n + c]) / (2f * spacing);
                    rgba[i * 4] = seaLevel - h;
                    rgba[i * 4 + 1] = shore[i];
                    rgba[i * 4 + 2] = h < seaLevel ? exposure[i] : 0f;
                    rgba[i * 4 + 3] = Math.Min(MathF.Sqrt(gx * gx + gz * gz), MaxSlope);
                }
            });
            double fetch = ShelteredFetch(heights, n, spacing, seaLevel, exposure, seaDirX, seaDirZ);
            return new WaterBakeResult(n, rgba, fetch);
        }

        // ── Shore distance ───────────────────────────────────────────────────

        /// <summary>
        /// Signed distance (m) from each vertex to the waterline: positive over
        /// water, negative over land, clamped to ±MaxShoreDistanceM.
        /// </summary>
        public static float[] ShoreDistance(float[] heights, int n, float spacing, float seaLevel)
        {
            int m = 2 * n - 1;
            float fine = spacing * 0.5f;
            var land = new bool[m * m];
            Parallel.For(0, m, fr =>
            {
                int r0 = fr / 2, r1 = Math.Min(r0 + (fr & 1), n - 1);
                for (int fc = 0; fc < m; fc++)
                {
                    int c0 = fc / 2, c1 = Math.Min(c0 + (fc & 1), n - 1);
                    float h = 0.25f * (heights[r0 * n + c0] + heights[r0 * n + c1] + heights[r1 * n + c0] + heights[r1 * n + c1]);
                    land[fr * m + fc] = h >= seaLevel;
                }
            });
            float[] toLand = SquaredDistance(land, m, true);
            float[] toWater = SquaredDistance(land, m, false);
            var result = new float[n * n];
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    int f = 2 * r * m + 2 * c;
                    float d = land[f]
                        ? -(MathF.Sqrt(toWater[f]) * fine - 0.5f * fine)
                        : MathF.Sqrt(toLand[f]) * fine - 0.5f * fine;
                    result[r * n + c] = Math.Clamp(d, -MaxShoreDistanceM, MaxShoreDistanceM);
                }
            }
            return result;
        }

        /// <summary>
        /// Squared distance, in cells, from every cell to the nearest cell whose
        /// mask equals `target` (exact, separable: columns then rows).
        /// </summary>
        public static float[] SquaredDistance(bool[] mask, int m, bool target)
        {
            var grid = new float[m * m];
            for (int i = 0; i < grid.Length; i++)
                grid[i] = mask[i] == target ? 0f : Infinity;
            Parallel.For(0, m, c =>
            {
                var f = new float[m];
                var d = new float[m];
                var v = new int[m];
                var z = new float[m + 1];
                for (int r = 0; r < m; r++)
                    f[r] = grid[r * m + c];
                Transform1D(f, d, v, z, m);
                for (int r = 0; r < m; r++)
                    grid[r * m + c] = d[r];
            });
            Parallel.For(0, m, r =>
            {
                var f = new float[m];
                var d = new float[m];
                var v = new int[m];
                var z = new float[m + 1];
                Array.Copy(grid, r * m, f, 0, m);
                Transform1D(f, d, v, z, m);
                Array.Copy(d, 0, grid, r * m, m);
            });
            return grid;
        }

        /// <summary>1D squared-distance transform of a sampled function (lower envelope of parabolas).</summary>
        private static void Transform1D(float[] f, float[] d, int[] v, float[] z, int n)
        {
            int k = 0;
            v[0] = 0;
            z[0] = -Infinity;
            z[1] = Infinity;
            for (int q = 1; q < n; q++)
            {
                if (f[q] >= Infinity)
                    continue;
                if (f[v[0]] >= Infinity)
                {
                    v[0] = q;
                    continue;
                }
                float s = Intersection(f, q, v[k]);
                while (k > 0 && s <= z[k])
                {
                    k--;
                    s = Intersection(f, q, v[k]);
                }
                k++;
                v[k] = q;
                z[k] = s;
                z[k + 1] = Infinity;
            }
            if (f[v[0]] >= Infinity)
            {
                for (int q = 0; q < n; q++)
                    d[q] = Infinity;
                return;
            }
            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q)
                    k++;
                float dq = q - v[k];
                d[q] = dq * dq + f[v[k]];
            }
        }

        private static float Intersection(float[] f, int q, int p)
            => ((f[q] + (float)q * q) - (f[p] + (float)p * p)) / (2f * q - 2f * p);

        // ── Exposure ─────────────────────────────────────────────────────────

        /// <summary>Open-sea exposure (0-1) at every vertex, smoothed and bilinearly upsampled.</summary>
        public static float[] Exposure(float[] heights, int n, float spacing, float seaLevel)
        {
            int s = (n - 1) / ExposureStride + 1;
            var coarse = new float[s * s];
            Parallel.For(0, s, sr =>
            {
                for (int sc = 0; sc < s; sc++)
                {
                    int c = Math.Min(sc * ExposureStride, n - 1), r = Math.Min(sr * ExposureStride, n - 1);
                    if (heights[r * n + c] >= seaLevel)
                    {
                        coarse[sr * s + sc] = 0f;
                        continue;
                    }
                    float escaped = 0f;
                    for (int k = 0; k < ExposureDirections; k++)
                    {
                        double a = 2.0 * Math.PI * k / ExposureDirections;
                        float dx = (float)Math.Cos(a), dz = (float)Math.Sin(a);
                        if (MarchToLand(heights, n, c, r, dx, dz, seaLevel, 2f) < 0f)
                            escaped += MathF.Exp(-EdgeDistanceCells(n, c, r, dx, dz) * spacing / ExposureDecayM);
                    }
                    float share = escaped / ExposureDirections;
                    coarse[sr * s + sc] = Math.Min(share / FullExposureShare, 1f);
                }
            });
            for (int pass = 0; pass < ExposureBlurPasses; pass++)
                coarse = BoxBlur(coarse, s);
            var result = new float[n * n];
            for (int r = 0; r < n; r++)
            {
                float fr = (float)r / ExposureStride;
                int r0 = Math.Min((int)fr, s - 1), r1 = Math.Min(r0 + 1, s - 1);
                float tr = fr - r0;
                for (int c = 0; c < n; c++)
                {
                    float fc = (float)c / ExposureStride;
                    int c0 = Math.Min((int)fc, s - 1), c1 = Math.Min(c0 + 1, s - 1);
                    float tc = fc - c0;
                    float top = coarse[r0 * s + c0] + (coarse[r0 * s + c1] - coarse[r0 * s + c0]) * tc;
                    float bottom = coarse[r1 * s + c0] + (coarse[r1 * s + c1] - coarse[r1 * s + c0]) * tc;
                    result[r * n + c] = top + (bottom - top) * tr;
                }
            }
            return result;
        }

        private static float[] BoxBlur(float[] src, int s)
        {
            var dst = new float[src.Length];
            for (int r = 0; r < s; r++)
            {
                for (int c = 0; c < s; c++)
                {
                    float sum = 0f;
                    int count = 0;
                    for (int dr = -2; dr <= 2; dr++)
                    {
                        for (int dc = -2; dc <= 2; dc++)
                        {
                            int rr = r + dr, cc = c + dc;
                            if (rr < 0 || cc < 0 || rr >= s || cc >= s)
                                continue;
                            sum += src[rr * s + cc];
                            count++;
                        }
                    }
                    dst[r * s + c] = sum / count;
                }
            }
            return dst;
        }

        /// <summary>
        /// March from vertex (c, r) along (dx, dz) in steps of `stepCells` grid
        /// cells. Returns the distance in cells to the first land vertex, or −1
        /// when the ray leaves the world over water.
        /// </summary>
        private static float MarchToLand(float[] heights, int n, int c, int r, float dx, float dz, float seaLevel, float stepCells)
        {
            float x = c, z = r;
            for (float travelled = stepCells; ; travelled += stepCells)
            {
                x += dx * stepCells;
                z += dz * stepCells;
                int ix = (int)MathF.Round(x), iz = (int)MathF.Round(z);
                if (ix < 0 || iz < 0 || ix >= n || iz >= n)
                    return -1f;
                if (heights[iz * n + ix] >= seaLevel)
                    return travelled;
            }
        }

        // ── Fetch ────────────────────────────────────────────────────────────

        /// <summary>
        /// Effective fetch (m) of the sheltered basin for a sea travelling along
        /// (dirX, dirZ): the mean fetch over radials within ±15° upwind, the
        /// median over sheltered water. A ray that leaves the world counts the
        /// distance to the edge. With no sheltered water, the exposed coast's.
        /// </summary>
        public static double ShelteredFetch(float[] heights, int n, float spacing, float seaLevel,
            float[] exposure, float dirX, float dirZ)
        {
            double len = Math.Sqrt(dirX * dirX + dirZ * dirZ);
            if (len < 1e-6)
                return SeaStateModel.ExposedFetchM;
            double upwind = Math.Atan2(-dirZ / len, -dirX / len);
            var fetches = new List<double>();
            for (int r = 0; r < n; r += FetchStride)
            {
                for (int c = 0; c < n; c += FetchStride)
                {
                    int i = r * n + c;
                    if (heights[i] >= seaLevel || exposure[i] >= ShelteredExposure)
                        continue;
                    double sum = 0.0;
                    int count = 0;
                    for (double a = -FetchArcDeg; a <= FetchArcDeg + 1e-9; a += FetchArcStepDeg)
                    {
                        double angle = upwind + a * Math.PI / 180.0;
                        float dx = (float)Math.Cos(angle), dz = (float)Math.Sin(angle);
                        float cells = MarchToLand(heights, n, c, r, dx, dz, seaLevel, 1f);
                        if (cells < 0f)
                            cells = EdgeDistanceCells(n, c, r, dx, dz);
                        sum += cells * spacing;
                        count++;
                    }
                    fetches.Add(sum / count);
                }
            }
            if (fetches.Count == 0)
                return SeaStateModel.ExposedFetchM;
            fetches.Sort();
            return fetches[fetches.Count / 2];
        }

        private static float EdgeDistanceCells(int n, int c, int r, float dx, float dz)
        {
            float tx = dx > 0f ? (n - 1 - c) / dx : dx < 0f ? -c / dx : float.MaxValue;
            float tz = dz > 0f ? (n - 1 - r) / dz : dz < 0f ? -r / dz : float.MaxValue;
            return Math.Min(tx, tz);
        }

        /// <summary>Pack RGBA floats as IEEE half floats (8 bytes per texel, little-endian).</summary>
        public static byte[] ToHalfBytes(float[] rgba)
        {
            var bytes = new byte[rgba.Length * 2];
            for (int i = 0; i < rgba.Length; i++)
            {
                ushort bits = BitConverter.HalfToUInt16Bits((Half)rgba[i]);
                bytes[i * 2] = (byte)(bits & 0xFF);
                bytes[i * 2 + 1] = (byte)(bits >> 8);
            }
            return bytes;
        }
    }
}
