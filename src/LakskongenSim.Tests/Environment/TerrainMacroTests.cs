using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Environment;
using Xunit;

namespace LakskongenSim.Tests.Environment
{
    /// <summary>
    /// gfx-polish-1: the terrain mesh carries the slope and the cavity with the
    /// wall ledges averaged out (<see cref="TerrainMesh.Chunk.Macro"/>, the
    /// terrain shader's UV2). The heightfield terraces its walls every 48 m of
    /// height (TerrainGenerator WallRelief); seen from 1-2 km the terraces'
    /// snowy flats and dark risers drew the walls as regular stripes, so the
    /// shader's masks read these averaged values where a terrace is small on
    /// screen.
    /// </summary>
    public class TerrainMacroTests
    {
        private const float Spacing = 7.8f;

        private static float SmoothStep(float a, float b, float x)
        {
            float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        /// <summary>A wall rising along +X at `slope`, terraced as WallRelief terraces it (48 m steps, strength 0.28).</summary>
        private static float[] TerracedWall(int n, float slope)
        {
            var h = new float[n * n];
            for (int row = 0; row < n; row++)
            {
                for (int col = 0; col < n; col++)
                {
                    float plain = slope * col * Spacing;
                    float t = plain / 48f;
                    float step = MathF.Floor(t);
                    float terraced = step + SmoothStep(0.25f, 0.75f, t - step);
                    h[row * n + col] = plain + (terraced - t) * 48f * 0.28f;
                }
            }
            return h;
        }

        private static float StdDev(IReadOnlyList<float> xs)
        {
            float mean = xs.Average();
            return MathF.Sqrt(xs.Sum(x => (x - mean) * (x - mean)) / xs.Count);
        }

        [Theory]
        [InlineData(0.8f)]
        [InlineData(1.2f)]
        [InlineData(1.5f)]
        [InlineData(2.0f)]
        public void Macro_AveragesTheTerracesOut_WhereTheVertexSlopeSwingsWithThem(float slope)
        {
            const int n = 65;
            var heights = TerracedWall(n, slope);
            var mesh = TerrainMesh.Build(heights, n, (n - 1) * Spacing, chunkQuads: 16);
            var vertexSlopes = new List<float>();
            var macroSlopes = new List<float>();
            foreach (var chunk in mesh.Chunks)
            {
                for (int r = 0; r < chunk.Rows; r++)
                {
                    for (int c = 0; c < chunk.Columns; c++)
                    {
                        int col = chunk.FirstColumn + c;
                        // Away from the grid's ends, where the stencils are one-sided.
                        if (col < TerrainMesh.MacroStride || col > n - 1 - TerrainMesh.MacroStride)
                            continue;
                        int v = r * chunk.Columns + c;
                        float nx = chunk.Normals[v * 3], ny = chunk.Normals[v * 3 + 1], nz = chunk.Normals[v * 3 + 2];
                        vertexSlopes.Add(MathF.Sqrt(nx * nx + nz * nz) / ny);
                        macroSlopes.Add(chunk.Macro[v * 2]);
                    }
                }
            }
            float vertexSd = StdDev(vertexSlopes);
            float macroSd = StdDev(macroSlopes);
            Assert.True(vertexSd > 0.2f, $"the terraces swing the vertex slope (sd {vertexSd:F3})");
            Assert.True(macroSd < 0.35f * vertexSd, $"slope {slope}: macro sd {macroSd:F3} vs vertex sd {vertexSd:F3}");
            Assert.InRange(macroSlopes.Average(), slope * 0.95f, slope * 1.05f);
        }

        [Fact]
        public void Macro_IsTheSlopeAndCavityOverMacroStrideCells()
        {
            var gen = new TerrainGenerator(new FjordTerrainSettings { Seed = 42, Resolution = 129 });
            float[] h = gen.GenerateHeightmap();
            int n = 129;
            float sp = gen.Settings.WorldSizeM / (n - 1);
            var mesh = TerrainMesh.Build(h, n, gen.Settings.WorldSizeM);
            var chunk = mesh.Chunks[mesh.Chunks.Count / 2];
            int r = chunk.Rows / 2, c = chunk.Columns / 2;
            int row = chunk.FirstRow + r, col = chunk.FirstColumn + c;
            int s = TerrainMesh.MacroStride;
            float dhdx = (h[row * n + col + s] - h[row * n + col - s]) / (2f * s * sp);
            float dhdz = (h[(row + s) * n + col] - h[(row - s) * n + col]) / (2f * s * sp);
            int v = r * chunk.Columns + c;
            Assert.Equal(MathF.Sqrt(dhdx * dhdx + dhdz * dhdz), chunk.Macro[v * 2], 5);
            Assert.Equal(TerrainMesh.Cavity(h, n, sp, col, row, s), chunk.Macro[v * 2 + 1], 5);
            Assert.Equal(TerrainMesh.Cavity(h, n, sp, col, row), chunk.Colors[v * 4], 5);
            Assert.Equal(4, TerrainMesh.MacroStride);
        }

        [Fact]
        public void Macro_IsSharedExactlyAcrossChunkBorders()
        {
            var gen = new TerrainGenerator(new FjordTerrainSettings { Seed = 42, Resolution = 129 });
            float[] h = gen.GenerateHeightmap();
            var mesh = TerrainMesh.Build(h, 129, gen.Settings.WorldSizeM);
            var seen = new Dictionary<(int, int), (float, float)>();
            foreach (var chunk in mesh.Chunks)
            {
                Assert.Equal(chunk.Columns * chunk.Rows * 2, chunk.Macro.Length);
                for (int r = 0; r < chunk.Rows; r++)
                {
                    for (int c = 0; c < chunk.Columns; c++)
                    {
                        int v = r * chunk.Columns + c;
                        var key = (chunk.FirstColumn + c, chunk.FirstRow + r);
                        var value = (chunk.Macro[v * 2], chunk.Macro[v * 2 + 1]);
                        if (seen.TryGetValue(key, out var other))
                            Assert.Equal(other, value);
                        seen[key] = value;
                    }
                }
            }
            Assert.Equal(129 * 129, seen.Count);
        }

        [Fact]
        public void FarFieldRing_CarriesItsOwnSlopeAndCavityAsItsMacro()
        {
            // The ring has no wall ledges (its relief fades out past the
            // heightfield's edge): its macro values are its own, so the shader
            // reads the same slope and cavity at any distance there.
            var gen = new TerrainGenerator(new FjordTerrainSettings { Seed = 42, Resolution = 129 });
            float[] h = gen.GenerateHeightmap();
            var far = FarFieldMesh.Build(gen, h, null);
            Assert.NotEmpty(far.Chunks);
            foreach (var chunk in far.Chunks)
            {
                int count = chunk.Positions.Length / 3;
                Assert.Equal(count * 2, chunk.Macro.Length);
                for (int v = 0; v < count; v++)
                {
                    float nx = chunk.Normals[v * 3], ny = chunk.Normals[v * 3 + 1], nz = chunk.Normals[v * 3 + 2];
                    Assert.Equal(MathF.Sqrt(nx * nx + nz * nz) / ny, chunk.Macro[v * 2], 4);
                    Assert.Equal(chunk.Colors[v * 4], chunk.Macro[v * 2 + 1]);
                }
            }
        }
    }
}
