using System.Threading.Tasks;

namespace LakskongenSim.Environment
{
    /// <summary>
    /// Render arrays for a square heightfield, split into square chunks so the
    /// renderer can cull them and upload them a few per frame. Built off the
    /// main thread; the renderer only hands the arrays to the GPU.
    ///
    /// Triangles wind clockwise seen from +Y — Godot's front face — as
    /// (i, i+1, i+row) and (i+1, i+row+1, i+row). The old winding (i, i+row,
    /// i+1) was counter-clockwise, so with cull_back every upward-facing
    /// triangle was culled and the fjord showed as floating shards (F750).
    /// Normals are central differences of the global grid, so chunk borders
    /// share positions and normals exactly (no cracks, no shading seams).
    /// Tangents follow +X (the U direction) with w = −1 so Godot's binormal
    /// points along +Z (V). The colour's red channel carries a cavity term
    /// from the height Laplacian (0.5 flat, higher in gullies, lower on
    /// ridges) for the terrain shader's soil and occlusion; green, blue and
    /// alpha carry the vegetation masks (G2-02, <see cref="VegetationMasks"/>):
    /// wood density, birch share and meadow, so the shader paints the woods
    /// exactly where the tree scatter stands them. Without masks the ground is
    /// bare (g 0, b 1, a 0).
    /// </summary>
    public sealed class TerrainMesh
    {
        /// <summary>One chunk's vertex and index arrays.</summary>
        public sealed class Chunk
        {
            /// <summary>x, y, z per vertex (world metres).</summary>
            public float[] Positions { get; init; } = Array.Empty<float>();
            /// <summary>Unit normal (x, y, z) per vertex.</summary>
            public float[] Normals { get; init; } = Array.Empty<float>();
            /// <summary>Tangent (x, y, z, w) per vertex.</summary>
            public float[] Tangents { get; init; } = Array.Empty<float>();
            /// <summary>u, v in [0, 1] over the whole terrain, per vertex.</summary>
            public float[] Uvs { get; init; } = Array.Empty<float>();
            /// <summary>r, g, b, a per vertex: r = cavity, g = wood density, b = birch share, a = meadow.</summary>
            public float[] Colors { get; init; } = Array.Empty<float>();
            /// <summary>
            /// Four values per vertex: the slope (rise over run) and the cavity of the ground
            /// without the wall ledges' terraces (the heights minus <see cref="WallLedges.Offsets"/>,
            /// read with the same stencils as <see cref="Normals"/> and the cavity in
            /// <see cref="Colors"/>), the ledges' <see cref="WallLedges.Strength"/>, and 0. The
            /// terrain shader's masks use them where a terrace is too small on screen to draw: the
            /// walls' snow and soil follow their buttresses and gullies, with as much snow as the
            /// terraces' flats and risers would hold between them, instead of striping every 48 m
            /// of height (gfx-polish-1). Without ledges, the vertex's own slope and cavity, 0, 0.
            /// </summary>
            public float[] Macro { get; init; } = Array.Empty<float>();
            /// <summary>Three vertex indices per triangle (chunk-local).</summary>
            public int[] Indices { get; init; } = Array.Empty<int>();
            public int FirstColumn { get; init; }
            public int FirstRow { get; init; }
            public int Columns { get; init; }
            public int Rows { get; init; }
        }

        /// <summary>Cavity gain: metres⁻¹ of Laplacian per unit of cavity.</summary>
        private const float CavityGain = 3f;
        /// <summary>Laplacian stencil stride in grid cells (smooths out single-cell noise).</summary>
        private const int CavityStride = 2;
        /// <summary>Values per vertex in <see cref="Chunk.Macro"/> (the shader's CUSTOM0, RGBA float).</summary>
        public const int MacroStride = 4;

        public IReadOnlyList<Chunk> Chunks { get; }
        public int Resolution { get; }

        private TerrainMesh(IReadOnlyList<Chunk> chunks, int resolution)
        {
            Chunks = chunks;
            Resolution = resolution;
        }

        /// <summary>
        /// Build chunked arrays for an n×n grid of <paramref name="heights"/>
        /// (row-major by Z) spanning <paramref name="worldSize"/> metres,
        /// centred on the origin, with chunks of up to
        /// <paramref name="chunkQuads"/>² quads. <paramref name="ledges"/>
        /// (the same grid, <see cref="TerrainGenerator.GenerateHeightmap(out WallLedges)"/>)
        /// gives <see cref="Chunk.Macro"/> the ground without its terraces.
        /// </summary>
        public static TerrainMesh Build(float[] heights, int n, float worldSize, int chunkQuads = 64, VegetationMasks? masks = null,
            Func<float, float, float>? outsideHeight = null, WallLedges? ledges = null)
        {
            if (masks != null && masks.Size != n)
                throw new ArgumentException($"masks are {masks.Size}² for a {n}² grid", nameof(masks));
            if (heights.Length != n * n)
                throw new ArgumentException($"expected {n * n} heights, got {heights.Length}", nameof(heights));
            if (ledges is { } l && (l.Offsets.Length != n * n || l.Strength.Length != n * n))
                throw new ArgumentException($"expected {n * n} ledge values, got {l.Offsets.Length} and {l.Strength.Length}", nameof(ledges));
            if (chunkQuads < 1)
                throw new ArgumentOutOfRangeException(nameof(chunkQuads));
            float[] ground = heights;
            float[]? strength = null;
            if (ledges is { } wall)
            {
                ground = new float[heights.Length];
                for (int i = 0; i < ground.Length; i++)
                    ground[i] = heights[i] - wall.Offsets[i];
                strength = wall.Strength;
            }
            int perSide = (n - 1 + chunkQuads - 1) / chunkQuads;
            var chunks = new Chunk[perSide * perSide];
            Parallel.For(0, chunks.Length, c =>
            {
                int cx = c % perSide, cz = c / perSide;
                int col0 = cx * chunkQuads, row0 = cz * chunkQuads;
                int cols = Math.Min(chunkQuads, n - 1 - col0) + 1;
                int rows = Math.Min(chunkQuads, n - 1 - row0) + 1;
                chunks[c] = BuildChunk(heights, ground, strength, n, worldSize, col0, row0, cols, rows, masks, outsideHeight);
            });
            return new TerrainMesh(chunks, n);
        }

        private static Chunk BuildChunk(float[] heights, float[] ground, float[]? ledgeStrength, int n, float worldSize, int col0, int row0,
            int cols, int rows, VegetationMasks? masks, Func<float, float, float>? outsideHeight)
        {
            float spacing = worldSize / (n - 1);
            float half = worldSize * 0.5f;
            int count = cols * rows;
            var positions = new float[count * 3];
            var normals = new float[count * 3];
            var tangents = new float[count * 4];
            var uvs = new float[count * 2];
            var colors = new float[count * 4];
            var macro = new float[count * MacroStride];
            for (int r = 0; r < rows; r++)
            {
                int row = row0 + r;
                for (int c = 0; c < cols; c++)
                {
                    int col = col0 + c;
                    int g = row * n + col;
                    int v = r * cols + c;
                    positions[v * 3] = -half + col * spacing;
                    positions[v * 3 + 1] = heights[g];
                    positions[v * 3 + 2] = -half + row * spacing;
                    var (nx, ny, nz) = GridNormal(heights, n, worldSize, col, row, outsideHeight);
                    float dhdx = -nx / ny;
                    normals[v * 3] = nx;
                    normals[v * 3 + 1] = ny;
                    normals[v * 3 + 2] = nz;
                    // Tangent along +X on the surface, made orthogonal to the normal.
                    float tx = 1f, ty = dhdx, tz = 0f;
                    float dot = tx * nx + ty * ny + tz * nz;
                    tx -= dot * nx; ty -= dot * ny; tz -= dot * nz;
                    float tinv = 1f / MathF.Sqrt(tx * tx + ty * ty + tz * tz);
                    tangents[v * 4] = tx * tinv;
                    tangents[v * 4 + 1] = ty * tinv;
                    tangents[v * 4 + 2] = tz * tinv;
                    tangents[v * 4 + 3] = -1f;
                    uvs[v * 2] = col / (float)(n - 1);
                    uvs[v * 2 + 1] = row / (float)(n - 1);
                    colors[v * 4] = Cavity(heights, n, spacing, col, row);
                    colors[v * 4 + 1] = masks != null ? masks.Forest[g] : 0f;
                    colors[v * 4 + 2] = masks != null ? masks.Birch[g] : 1f;
                    colors[v * 4 + 3] = masks != null ? masks.Meadow[g] : 0f;
                    // The ground without its terraces, read as the vertex's own slope and
                    // cavity are (the far field beyond the border carries no ledges).
                    int m = v * MacroStride;
                    if (ReferenceEquals(ground, heights))
                    {
                        macro[m] = MathF.Sqrt(nx * nx + nz * nz) / ny;
                        macro[m + 1] = colors[v * 4];
                    }
                    else
                    {
                        var (mx, my, mz) = GridNormal(ground, n, worldSize, col, row, outsideHeight);
                        macro[m] = MathF.Sqrt(mx * mx + mz * mz) / my;
                        macro[m + 1] = Cavity(ground, n, spacing, col, row);
                    }
                    macro[m + 2] = ledgeStrength != null ? ledgeStrength[g] : 0f;
                }
            }

            var indices = new int[(cols - 1) * (rows - 1) * 6];
            int o = 0;
            for (int r = 0; r < rows - 1; r++)
            {
                for (int c = 0; c < cols - 1; c++)
                {
                    int i = r * cols + c;
                    indices[o++] = i;
                    indices[o++] = i + 1;
                    indices[o++] = i + cols;
                    indices[o++] = i + 1;
                    indices[o++] = i + cols + 1;
                    indices[o++] = i + cols;
                }
            }
            return new Chunk
            {
                Positions = positions,
                Normals = normals,
                Tangents = tangents,
                Uvs = uvs,
                Colors = colors,
                Macro = macro,
                Indices = indices,
                FirstColumn = col0,
                FirstRow = row0,
                Columns = cols,
                Rows = rows,
            };
        }

        /// <summary>
        /// Unit normal at grid vertex (col, row) from central differences of
        /// the heights. On the border a missing neighbour comes from
        /// <paramref name="outsideHeight"/> (world x, z → height) when given,
        /// so the border shades like the far field beyond it (G2-01); without
        /// it the difference is one-sided.
        /// </summary>
        public static (float x, float y, float z) GridNormal(float[] heights, int n, float worldSize, int col, int row,
            Func<float, float, float>? outsideHeight = null)
        {
            float spacing = worldSize / (n - 1);
            float half = worldSize * 0.5f;
            int colUp = Math.Min(col + 1, n - 1), colDown = Math.Max(col - 1, 0);
            int rowUp = Math.Min(row + 1, n - 1), rowDown = Math.Max(row - 1, 0);
            float hRight = heights[row * n + colUp], hLeft = heights[row * n + colDown];
            float hUp = heights[rowUp * n + col], hDown = heights[rowDown * n + col];
            float runX = (colUp - colDown) * spacing, runZ = (rowUp - rowDown) * spacing;
            if (outsideHeight != null)
            {
                float x = -half + col * spacing, z = -half + row * spacing;
                if (colUp == col) { hRight = outsideHeight(x + spacing, z); runX += spacing; }
                if (colDown == col) { hLeft = outsideHeight(x - spacing, z); runX += spacing; }
                if (rowUp == row) { hUp = outsideHeight(x, z + spacing); runZ += spacing; }
                if (rowDown == row) { hDown = outsideHeight(x, z - spacing); runZ += spacing; }
            }
            float dhdx = (hRight - hLeft) / runX;
            float dhdz = (hUp - hDown) / runZ;
            float inv = 1f / MathF.Sqrt(dhdx * dhdx + 1f + dhdz * dhdz);
            return (-dhdx * inv, inv, -dhdz * inv);
        }

        /// <summary>
        /// Cavity at grid vertex (col, row): 0.5 on a plane, toward 1 in a
        /// gully or on a ledge (concave), toward 0 on a ridge (convex).
        /// </summary>
        public static float Cavity(float[] heights, int n, float spacing, int col, int row)
        {
            int s = CavityStride;
            int cl = Math.Max(col - s, 0), cr = Math.Min(col + s, n - 1);
            int rd = Math.Max(row - s, 0), ru = Math.Min(row + s, n - 1);
            float h = heights[row * n + col];
            float lapX = (heights[row * n + cl] + heights[row * n + cr] - 2f * h) / (s * s * spacing * spacing);
            float lapZ = (heights[rd * n + col] + heights[ru * n + col] - 2f * h) / (s * s * spacing * spacing);
            float cavity = 0.5f + CavityGain * (lapX + lapZ);
            return cavity < 0f ? 0f : (cavity > 1f ? 1f : cavity);
        }
    }
}
