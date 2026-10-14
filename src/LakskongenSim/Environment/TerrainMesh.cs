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
    /// ridges) for the terrain shader's soil and occlusion.
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
            /// <summary>r, g, b, a per vertex: r = cavity, g = b = a = 1.</summary>
            public float[] Colors { get; init; } = Array.Empty<float>();
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
        /// <paramref name="chunkQuads"/>² quads.
        /// </summary>
        public static TerrainMesh Build(float[] heights, int n, float worldSize, int chunkQuads = 64)
        {
            if (heights.Length != n * n)
                throw new ArgumentException($"expected {n * n} heights, got {heights.Length}", nameof(heights));
            if (chunkQuads < 1)
                throw new ArgumentOutOfRangeException(nameof(chunkQuads));
            int perSide = (n - 1 + chunkQuads - 1) / chunkQuads;
            var chunks = new Chunk[perSide * perSide];
            Parallel.For(0, chunks.Length, c =>
            {
                int cx = c % perSide, cz = c / perSide;
                int col0 = cx * chunkQuads, row0 = cz * chunkQuads;
                int cols = Math.Min(chunkQuads, n - 1 - col0) + 1;
                int rows = Math.Min(chunkQuads, n - 1 - row0) + 1;
                chunks[c] = BuildChunk(heights, n, worldSize, col0, row0, cols, rows);
            });
            return new TerrainMesh(chunks, n);
        }

        private static Chunk BuildChunk(float[] heights, int n, float worldSize, int col0, int row0, int cols, int rows)
        {
            float spacing = worldSize / (n - 1);
            float half = worldSize * 0.5f;
            int count = cols * rows;
            var positions = new float[count * 3];
            var normals = new float[count * 3];
            var tangents = new float[count * 4];
            var uvs = new float[count * 2];
            var colors = new float[count * 4];
            for (int r = 0; r < rows; r++)
            {
                int row = row0 + r;
                int rowUp = Math.Min(row + 1, n - 1), rowDown = Math.Max(row - 1, 0);
                for (int c = 0; c < cols; c++)
                {
                    int col = col0 + c;
                    int g = row * n + col;
                    int v = r * cols + c;
                    int colUp = Math.Min(col + 1, n - 1), colDown = Math.Max(col - 1, 0);
                    float dhdx = (heights[row * n + colUp] - heights[row * n + colDown]) / ((colUp - colDown) * spacing);
                    float dhdz = (heights[rowUp * n + col] - heights[rowDown * n + col]) / ((rowUp - rowDown) * spacing);
                    positions[v * 3] = -half + col * spacing;
                    positions[v * 3 + 1] = heights[g];
                    positions[v * 3 + 2] = -half + row * spacing;
                    float inv = 1f / MathF.Sqrt(dhdx * dhdx + 1f + dhdz * dhdz);
                    float nx = -dhdx * inv, ny = inv, nz = -dhdz * inv;
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
                    colors[v * 4 + 1] = 1f;
                    colors[v * 4 + 2] = 1f;
                    colors[v * 4 + 3] = 1f;
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
                Indices = indices,
                FirstColumn = col0,
                FirstRow = row0,
                Columns = cols,
                Rows = rows,
            };
        }

        private static float Cavity(float[] heights, int n, float spacing, int col, int row)
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
