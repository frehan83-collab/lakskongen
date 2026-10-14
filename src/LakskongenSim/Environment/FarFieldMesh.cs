using System.Threading.Tasks;

namespace LakskongenSim.Environment
{
    /// <summary>
    /// G2-01: the far-field ring around the play area's heightfield, built off
    /// the main thread like <see cref="TerrainMesh"/> and in the same chunk
    /// format, so the renderer draws it with the terrain material.
    ///
    /// Geometry: square loops round the origin (Chebyshev rings). Loop 0 is the
    /// heightfield's own border (its vertices, heights, normals and colours,
    /// exactly as <see cref="TerrainMesh"/> builds them); each further loop
    /// lies (1 + 2/S) times farther out than the one before, S being its
    /// segments per side, so every cell is square and the spacing grows in
    /// proportion to the distance from the centre: a constant angular density
    /// seen from the play area, which is what a far LOD needs. The near band
    /// (to <see cref="BandEdgesM"/>[0]) has <see cref="NearSegmentsPerSide"/>
    /// segments (16 m at the world edge), the rest
    /// <see cref="FarSegmentsPerSide"/> (31 m at 4 km, 250 m at 16 km).
    /// Neighbouring loops are joined by a zipper (a strip that merges the two
    /// vertex rows by their position along the side): plain quads where the
    /// counts agree, fans where they do not (the border's 511 segments to the
    /// first loop's 256; 256 to 128 at the band edge), so no T-junction or
    /// crack opens anywhere.
    ///
    /// Chunks: <see cref="SectorsPerSide"/> sectors per side × radial bands,
    /// each a separate mesh the renderer can cull. Heights come from
    /// <see cref="TerrainGenerator.FarFieldHeightAt"/>; normals are central
    /// differences over a step that grows from the heightfield's spacing at
    /// the world edge (so the slopes, and with them the rock and soil the
    /// material picks, match across the seam) to the loop spacing farther out;
    /// the colour carries the heightfield's masks (cavity, woods, birch share,
    /// meadow) from the same vegetation rules, so the terrain shader paints
    /// the far slopes like the near ones.
    /// </summary>
    public sealed class FarFieldMesh
    {
        /// <summary>Sectors per side of the square (chunks along the perimeter).</summary>
        public const int SectorsPerSide = 4;
        /// <summary>Segments per side of the loops in the near band and beyond it.</summary>
        public const int NearSegmentsPerSide = 256;
        public const int FarSegmentsPerSide = 128;
        /// <summary>Outer half-extent (m) of each radial band but the last (which runs to the extent).</summary>
        public static readonly float[] BandEdgesM = { 4000f, 8000f };

        public IReadOnlyList<TerrainMesh.Chunk> Chunks { get; }
        /// <summary>Radial band of each chunk (0 adjoins the play area).</summary>
        public IReadOnlyList<int> ChunkBands { get; }
        /// <summary>Half-extent (m) of every loop, loop 0 (the heightfield border) first.</summary>
        public IReadOnlyList<float> LoopHalfExtents { get; }
        /// <summary>Segments per side of every loop, loop 0 first.</summary>
        public IReadOnlyList<int> LoopSegments { get; }

        private FarFieldMesh(IReadOnlyList<TerrainMesh.Chunk> chunks, IReadOnlyList<int> bands, IReadOnlyList<float> loops, IReadOnlyList<int> segments)
        {
            Chunks = chunks;
            ChunkBands = bands;
            LoopHalfExtents = loops;
            LoopSegments = segments;
        }

        /// <summary>
        /// Build the ring for the n² heightfield <paramref name="heights"/>
        /// (row-major by Z, spanning the generator's world) out to
        /// <paramref name="extentM"/>. <paramref name="masks"/> (the
        /// heightfield's vegetation masks) colour the border loop exactly as
        /// the heightfield's own mesh does.
        /// </summary>
        public static FarFieldMesh Build(TerrainGenerator generator, float[] heights, VegetationMasks? masks,
            float extentM = TerrainGenerator.FarFieldExtentM)
        {
            var st = generator.Settings;
            int n = st.Resolution;
            if (heights.Length != n * n)
                throw new ArgumentException($"expected {n * n} heights, got {heights.Length}", nameof(heights));
            float half = st.WorldSizeM * 0.5f;
            if (extentM <= half)
                throw new ArgumentOutOfRangeException(nameof(extentM));

            // Loops and bands: band b holds loops [first, last]; neighbouring
            // bands share their edge loop.
            var radii = new List<float> { half };
            var segments = new List<int> { n - 1 };
            var bands = new List<(int first, int last)>();
            int first = 0;
            for (int b = 0; b <= BandEdgesM.Length; b++)
            {
                float edge = b < BandEdgesM.Length ? Math.Min(BandEdgesM[b], extentM) : extentM;
                int s = b == 0 ? NearSegmentsPerSide : FarSegmentsPerSide;
                float r = radii[^1];
                while (r < edge)
                {
                    r *= 1f + 2f / s;
                    radii.Add(r);
                    segments.Add(s);
                }
                if (radii.Count - 1 > first)
                {
                    bands.Add((first, radii.Count - 1));
                    first = radii.Count - 1;
                }
                if (r >= extentM)
                    break;
            }

            var ring = new RingSampler(generator, heights, masks, radii, segments);
            ring.SampleAll();

            int sectors = 4 * SectorsPerSide;
            var chunks = new TerrainMesh.Chunk[bands.Count * sectors];
            Parallel.For(0, chunks.Length, i =>
            {
                int band = i / sectors, sector = i % sectors;
                chunks[i] = ring.BuildChunk(sector, band, bands[band].first, bands[band].last);
            });
            var chunkBands = new int[chunks.Length];
            for (int i = 0; i < chunks.Length; i++)
                chunkBands[i] = i / sectors;
            return new FarFieldMesh(chunks, chunkBands, radii, segments);
        }

        /// <summary>
        /// Point j of a loop with <paramref name="segments"/> segments per side
        /// at half-extent <paramref name="r"/>: side 0 runs along −Z from
        /// (−r, −r) toward +X, then the +X, +Z and −X sides in turn.
        /// </summary>
        public static (float x, float z) LoopPoint(float r, int j, int segments)
        {
            int count = 4 * segments;
            j = ((j % count) + count) % count;
            int side = j / segments;
            float t = -r + 2f * r * (j % segments) / segments;
            return side switch
            {
                0 => (t, -r),
                1 => (r, t),
                2 => (-t, r),
                _ => (-r, -t),
            };
        }

        /// <summary>
        /// Every loop's vertices, sampled once (loop 0 from the heightfield,
        /// the rest from the far-field function), then cut into chunks.
        /// </summary>
        private sealed class RingSampler
        {
            private readonly TerrainGenerator _gen;
            private readonly float[] _heights;
            private readonly VegetationMasks? _masks;
            private readonly IReadOnlyList<float> _radii;
            private readonly IReadOnlyList<int> _segments;
            private readonly int _n;
            private readonly float _half;
            private readonly float _spacing;
            private readonly VegetationSettings _vegetation;
            // Per loop: 4·segments vertices of 3 position, 3 normal, 4 colour floats.
            private readonly float[][] _pos;
            private readonly float[][] _nrm;
            private readonly float[][] _col;

            public RingSampler(TerrainGenerator gen, float[] heights, VegetationMasks? masks, IReadOnlyList<float> radii, IReadOnlyList<int> segments)
            {
                _gen = gen;
                _heights = heights;
                _masks = masks;
                _radii = radii;
                _segments = segments;
                _n = gen.Settings.Resolution;
                _half = gen.Settings.WorldSizeM * 0.5f;
                _spacing = gen.Settings.SpacingM;
                _vegetation = new VegetationSettings { Seed = gen.Settings.Seed };
                _pos = new float[radii.Count][];
                _nrm = new float[radii.Count][];
                _col = new float[radii.Count][];
            }

            public void SampleAll()
            {
                SampleBorder();
                Parallel.For(1, _radii.Count, SampleLoop);
            }

            /// <summary>Loop 0: the heightfield's border vertices, as TerrainMesh builds them.</summary>
            private void SampleBorder()
            {
                int m = _n - 1;
                int count = 4 * m;
                var pos = new float[count * 3];
                var nrm = new float[count * 3];
                var col = new float[count * 4];
                float world = 2f * _half;
                for (int i = 0; i < count; i++)
                {
                    int side = i / m, o = i % m;
                    var (c, r) = side switch
                    {
                        0 => (o, 0),
                        1 => (m, o),
                        2 => (m - o, m),
                        _ => (0, m - o),
                    };
                    int g = r * _n + c;
                    pos[i * 3] = -_half + c * _spacing;
                    pos[i * 3 + 1] = _heights[g];
                    pos[i * 3 + 2] = -_half + r * _spacing;
                    var (nx, ny, nz) = TerrainMesh.GridNormal(_heights, _n, world, c, r, _gen.FarFieldHeightAt);
                    nrm[i * 3] = nx; nrm[i * 3 + 1] = ny; nrm[i * 3 + 2] = nz;
                    col[i * 4] = TerrainMesh.Cavity(_heights, _n, _spacing, c, r);
                    col[i * 4 + 1] = _masks != null ? _masks.Forest[g] : 0f;
                    col[i * 4 + 2] = _masks != null ? _masks.Birch[g] : 1f;
                    col[i * 4 + 3] = _masks != null ? _masks.Meadow[g] : 0f;
                }
                _pos[0] = pos;
                _nrm[0] = nrm;
                _col[0] = col;
            }

            private void SampleLoop(int k)
            {
                int s = _segments[k];
                int count = 4 * s;
                var pos = new float[count * 3];
                var nrm = new float[count * 3];
                var col = new float[count * 4];
                float rk = _radii[k];
                float loopSpacing = 2f * rk / s;
                // The slope step: the heightfield's spacing at the seam, the loop's farther out.
                float step = _spacing + (loopSpacing - _spacing) * SmoothStep01((rk - _half) / _half);
                for (int j = 0; j < count; j++)
                {
                    var (x, z) = LoopPoint(rk, j, s);
                    float h = _gen.FarFieldHeightAt(x, z);
                    float hx0 = _gen.FarFieldHeightAt(x - step, z), hx1 = _gen.FarFieldHeightAt(x + step, z);
                    float hz0 = _gen.FarFieldHeightAt(x, z - step), hz1 = _gen.FarFieldHeightAt(x, z + step);
                    float dhdx = (hx1 - hx0) / (2f * step), dhdz = (hz1 - hz0) / (2f * step);
                    float inv = 1f / MathF.Sqrt(dhdx * dhdx + 1f + dhdz * dhdz);
                    pos[j * 3] = x; pos[j * 3 + 1] = h; pos[j * 3 + 2] = z;
                    nrm[j * 3] = -dhdx * inv; nrm[j * 3 + 1] = inv; nrm[j * 3 + 2] = -dhdz * inv;
                    // Cavity as TerrainMesh (gain 3 per m⁻¹ of Laplacian), over the slope step.
                    float lap = (hx0 + hx1 + hz0 + hz1 - 4f * h) / (step * step);
                    float cavity = Math.Clamp(0.5f + 3f * lap, 0f, 1f);
                    float slope = MathF.Sqrt(dhdx * dhdx + dhdz * dhdz);
                    float exposure = _gen.FarFieldLandExposure(x, z);
                    float meadow = VegetationScatter.MeadowAt(_vegetation, h, slope, exposure, x, z);
                    col[j * 4] = cavity;
                    col[j * 4 + 1] = VegetationScatter.ForestAt(_vegetation, h, slope, cavity - 0.5f, exposure, x, z) * (1f - 0.85f * meadow);
                    col[j * 4 + 2] = VegetationScatter.BirchShareAt(_vegetation, h, slope, x, z);
                    col[j * 4 + 3] = meadow;
                }
                _pos[k] = pos;
                _nrm[k] = nrm;
                _col[k] = col;
            }

            private static float SmoothStep01(float t)
            {
                t = t < 0f ? 0f : (t > 1f ? 1f : t);
                return t * t * (3f - 2f * t);
            }

            /// <summary>First and last vertex index (inclusive, may run past the loop's end) of a sector on loop k.</summary>
            private (int start, int end) SectorRange(int k, int sector)
            {
                int s = _segments[k];
                int side = sector / SectorsPerSide, sub = sector % SectorsPerSide;
                int start = side * s + (int)MathF.Round((float)sub * s / SectorsPerSide);
                int end = side * s + (int)MathF.Round((float)(sub + 1) * s / SectorsPerSide);
                return (start, end);
            }

            public TerrainMesh.Chunk BuildChunk(int sector, int band, int firstLoop, int lastLoop)
            {
                int rows = lastLoop - firstLoop + 1;
                var rowStart = new int[rows];
                var rowLength = new int[rows];
                int count = 0;
                for (int l = 0; l < rows; l++)
                {
                    var (a, b) = SectorRange(firstLoop + l, sector);
                    rowStart[l] = count;
                    rowLength[l] = b - a + 1;
                    count += rowLength[l];
                }
                var positions = new float[count * 3];
                var normals = new float[count * 3];
                var tangents = new float[count * 4];
                var uvs = new float[count * 2];
                var colors = new float[count * 4];
                float world = 2f * _half;
                for (int l = 0; l < rows; l++)
                {
                    int k = firstLoop + l;
                    int total = 4 * _segments[k];
                    var (a, _) = SectorRange(k, sector);
                    var pos = _pos[k];
                    var nrm = _nrm[k];
                    var col = _col[k];
                    for (int c = 0; c < rowLength[l]; c++)
                    {
                        int j = (a + c) % total;
                        int v = rowStart[l] + c;
                        Array.Copy(pos, j * 3, positions, v * 3, 3);
                        Array.Copy(nrm, j * 3, normals, v * 3, 3);
                        Array.Copy(col, j * 4, colors, v * 4, 4);
                        float nx = nrm[j * 3], ny = nrm[j * 3 + 1], nz = nrm[j * 3 + 2];
                        // Tangent along +X on the surface, orthogonal to the normal (as TerrainMesh).
                        float tx = 1f, ty = ny > 1e-6f ? -nx / ny : 0f, tz = 0f;
                        float dot = tx * nx + ty * ny + tz * nz;
                        tx -= dot * nx; ty -= dot * ny; tz -= dot * nz;
                        float tinv = 1f / MathF.Sqrt(tx * tx + ty * ty + tz * tz);
                        tangents[v * 4] = tx * tinv; tangents[v * 4 + 1] = ty * tinv; tangents[v * 4 + 2] = tz * tinv; tangents[v * 4 + 3] = -1f;
                        uvs[v * 2] = (pos[j * 3] + _half) / world;
                        uvs[v * 2 + 1] = (pos[j * 3 + 2] + _half) / world;
                    }
                }

                var indices = new List<int>(count * 6);
                for (int l = 0; l < rows - 1; l++)
                    Zip(positions, indices, rowStart[l], rowLength[l], rowStart[l + 1], rowLength[l + 1]);

                return new TerrainMesh.Chunk
                {
                    Positions = positions,
                    Normals = normals,
                    Tangents = tangents,
                    Uvs = uvs,
                    Colors = colors,
                    Indices = indices.ToArray(),
                    FirstColumn = sector,
                    FirstRow = band,
                    Columns = count,
                    Rows = 1,
                };
            }

            /// <summary>
            /// Triangulate the strip between an inner row (a) and an outer row
            /// (b) that span the same stretch of the sector: walk both by the
            /// fraction of the stretch each vertex sits at, always advancing
            /// the row whose next vertex comes first (equal counts give quads).
            /// </summary>
            private static void Zip(float[] positions, List<int> indices, int a0, int na, int b0, int nb)
            {
                int ia = 0, ib = 0;
                int sa = na - 1, sb = nb - 1;
                while (ia < sa || ib < sb)
                {
                    float ta = ia < sa ? (float)(ia + 1) / sa : float.MaxValue;
                    float tb = ib < sb ? (float)(ib + 1) / sb : float.MaxValue;
                    if (ta <= tb)
                    {
                        Tri(positions, indices, a0 + ia, a0 + ia + 1, b0 + ib);
                        ia++;
                    }
                    else
                    {
                        Tri(positions, indices, a0 + ia, b0 + ib + 1, b0 + ib);
                        ib++;
                    }
                }
            }

            /// <summary>Add a triangle wound clockwise seen from +Y (Godot's front face; cross.y &lt; 0, as TerrainMesh).</summary>
            private static void Tri(float[] positions, List<int> indices, int a, int b, int c)
            {
                float ax = positions[a * 3], az = positions[a * 3 + 2];
                float e1x = positions[b * 3] - ax, e1z = positions[b * 3 + 2] - az;
                float e2x = positions[c * 3] - ax, e2z = positions[c * 3 + 2] - az;
                if (e1z * e2x - e1x * e2z > 0f)
                    (b, c) = (c, b);
                indices.Add(a);
                indices.Add(b);
                indices.Add(c);
            }
        }
    }
}
