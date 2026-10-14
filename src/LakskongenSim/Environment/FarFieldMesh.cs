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
    /// <see cref="TerrainGenerator.FarFieldHeightAt"/>; normals are the
    /// area-weighted mean of the drawn faces round each vertex (a crest
    /// sharper than the loop spacing never turns a face against its corners),
    /// the border loop keeping the heightfield mesh's own; the colour carries
    /// the heightfield's masks (cavity, woods, birch share, meadow) from the
    /// same vegetation rules, so the terrain shader paints the far slopes like
    /// the near ones.
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
            ring.MeshNormals(bands);
            ring.Masks();

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

            /// <summary>Loop k ≥ 1: positions from the far field (normals and masks follow once every loop is in place).</summary>
            private void SampleLoop(int k)
            {
                int s = _segments[k];
                int count = 4 * s;
                var pos = new float[count * 3];
                float rk = _radii[k];
                for (int j = 0; j < count; j++)
                {
                    var (x, z) = LoopPoint(rk, j, s);
                    pos[j * 3] = x;
                    pos[j * 3 + 1] = _gen.FarFieldHeightAt(x, z);
                    pos[j * 3 + 2] = z;
                }
                _pos[k] = pos;
                _nrm[k] = new float[count * 3];
                _col[k] = new float[count * 4];
            }

            /// <summary>
            /// Loop k ≥ 1's masks, as TerrainMesh and VegetationScatter make
            /// the heightfield's: cavity from the Laplacian over the ring's own
            /// cells (along the loop and across to the loops either side, at
            /// the same fraction of the perimeter; 16 m at the seam, the
            /// heightfield's 2-cell stride), the woods, birch share and meadow
            /// from the mesh normal's slope and the far field's exposure.
            /// </summary>
            public void Masks()
            {
                Parallel.For(1, _radii.Count, k =>
                {
                    int s = _segments[k];
                    int count = 4 * s;
                    var pos = _pos[k];
                    var nrm = _nrm[k];
                    var col = _col[k];
                    float spacing = 2f * _radii[k] / s;
                    for (int j = 0; j < count; j++)
                    {
                        float x = pos[j * 3], h = pos[j * 3 + 1], z = pos[j * 3 + 2];
                        float along = pos[((j + 1) % count) * 3 + 1] + pos[((j + count - 1) % count) * 3 + 1] - 2f * h;
                        float lap = along / (spacing * spacing);
                        if (k + 1 < _radii.Count)
                        {
                            float inner = HeightAtFraction(k - 1, j, s), outer = HeightAtFraction(k + 1, j, s);
                            float din = _radii[k] - _radii[k - 1], dout = _radii[k + 1] - _radii[k];
                            // Second difference on an uneven step.
                            lap += 2f * (outer * din + inner * dout - h * (din + dout)) / (din * dout * (din + dout));
                        }
                        else
                        {
                            lap *= 2f;
                        }
                        float cavity = Math.Clamp(0.5f + 3f * lap, 0f, 1f);
                        float nx = nrm[j * 3], ny = nrm[j * 3 + 1], nz = nrm[j * 3 + 2];
                        float slope = MathF.Sqrt(nx * nx + nz * nz) / Math.Max(ny, 1e-4f);
                        float exposure = _gen.FarFieldLandExposure(x, z);
                        float meadow = VegetationScatter.MeadowAt(_vegetation, h, slope, exposure, x, z);
                        col[j * 4] = cavity;
                        col[j * 4 + 1] = VegetationScatter.ForestAt(_vegetation, h, slope, cavity - 0.5f, exposure, x, z) * (1f - 0.85f * meadow);
                        col[j * 4 + 2] = VegetationScatter.BirchShareAt(_vegetation, h, slope, x, z);
                        col[j * 4 + 3] = meadow;
                    }
                });
            }

            /// <summary>Height of loop k's vertex nearest the same fraction of the perimeter as vertex j of a loop with s segments per side.</summary>
            private float HeightAtFraction(int k, int j, int s)
            {
                int sk = _segments[k];
                int count = 4 * sk;
                int i = (int)MathF.Round((float)j * sk / s) % count;
                return _pos[k][i * 3 + 1];
            }

            /// <summary>
            /// Normals of loops k ≥ 1: the area-weighted mean of the faces round
            /// each vertex, over exactly the triangles the chunks draw (every
            /// sector's strips, the zippers included). A sharp crest the loop
            /// spacing cannot carry then never leaves a face turned against
            /// its corners' normals. The border loop keeps the heightfield
            /// mesh's normals, so the seam shades alike on both sides.
            /// </summary>
            public void MeshNormals(IReadOnlyList<(int first, int last)> bands)
            {
                var acc = new double[_radii.Count][];
                for (int k = 1; k < _radii.Count; k++)
                    acc[k] = new double[_pos[k].Length];
                int sectors = 4 * SectorsPerSide;
                foreach (var (first, last) in bands)
                {
                    for (int sector = 0; sector < sectors; sector++)
                    {
                        for (int k = first; k < last; k++)
                        {
                            var (a0, a1) = SectorRange(k, sector);
                            var (b0, b1) = SectorRange(k + 1, sector);
                            int na = a1 - a0 + 1, nb = b1 - b0 + 1;
                            int ta = 4 * _segments[k], tb = 4 * _segments[k + 1];
                            foreach (var (i, j, l) in ZipTriangles(na, nb))
                            {
                                var v0 = Ref(i, na, k, a0, ta, b0, tb);
                                var v1 = Ref(j, na, k, a0, ta, b0, tb);
                                var v2 = Ref(l, na, k, a0, ta, b0, tb);
                                AddFace(acc, v0, v1, v2);
                            }
                        }
                    }
                }
                for (int k = 1; k < _radii.Count; k++)
                {
                    var a = acc[k];
                    var nrm = _nrm[k];
                    for (int j = 0; j < a.Length; j += 3)
                    {
                        double len = Math.Sqrt(a[j] * a[j] + a[j + 1] * a[j + 1] + a[j + 2] * a[j + 2]);
                        nrm[j] = (float)(a[j] / len);
                        nrm[j + 1] = (float)(a[j + 1] / len);
                        nrm[j + 2] = (float)(a[j + 2] / len);
                    }
                }
            }

            /// <summary>Global (loop, vertex) of zip vertex v: row A (loop k) below na, row B (loop k + 1) above.</summary>
            private static (int loop, int index) Ref(int v, int na, int k, int a0, int ta, int b0, int tb)
                => v < na ? (k, (a0 + v) % ta) : (k + 1, (b0 + v - na) % tb);

            private void AddFace(double[][] acc, (int loop, int index) a, (int loop, int index) b, (int loop, int index) c)
            {
                var pa = _pos[a.loop]; var pb = _pos[b.loop]; var pc = _pos[c.loop];
                double ux = pb[b.index * 3] - pa[a.index * 3], uy = pb[b.index * 3 + 1] - pa[a.index * 3 + 1], uz = pb[b.index * 3 + 2] - pa[a.index * 3 + 2];
                double vx = pc[c.index * 3] - pa[a.index * 3], vy = pc[c.index * 3 + 1] - pa[a.index * 3 + 1], vz = pc[c.index * 3 + 2] - pa[a.index * 3 + 2];
                // u × v, turned up (twice the area as its length: area weighting).
                double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                if (ny < 0.0)
                {
                    nx = -nx; ny = -ny; nz = -nz;
                }
                foreach (var (loop, index) in new[] { a, b, c })
                {
                    if (loop == 0)
                        continue;
                    var t = acc[loop];
                    t[index * 3] += nx;
                    t[index * 3 + 1] += ny;
                    t[index * 3 + 2] += nz;
                }
            }

            /// <summary>
            /// Triangles of the strip between an inner row of na vertices and
            /// an outer row of nb that span the same stretch: walk both by the
            /// fraction of the stretch each vertex sits at, always advancing the
            /// row whose next vertex comes first (equal counts give quads).
            /// Indices below na are the inner row's, the rest the outer row's
            /// (offset by na). Winding is left to the caller.
            /// </summary>
            private static IEnumerable<(int, int, int)> ZipTriangles(int na, int nb)
            {
                int ia = 0, ib = 0;
                int sa = na - 1, sb = nb - 1;
                while (ia < sa || ib < sb)
                {
                    float fa = ia < sa ? (float)(ia + 1) / sa : float.MaxValue;
                    float fb = ib < sb ? (float)(ib + 1) / sb : float.MaxValue;
                    if (fa <= fb)
                    {
                        yield return (ia, ia + 1, na + ib);
                        ia++;
                    }
                    else
                    {
                        yield return (ia, na + ib + 1, na + ib);
                        ib++;
                    }
                }
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
                {
                    int a0 = rowStart[l], na = rowLength[l], b0 = rowStart[l + 1];
                    foreach (var (i, j, k) in ZipTriangles(na, rowLength[l + 1]))
                        Tri(positions, indices, i < na ? a0 + i : b0 + i - na, j < na ? a0 + j : b0 + j - na, k < na ? a0 + k : b0 + k - na);
                }

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
