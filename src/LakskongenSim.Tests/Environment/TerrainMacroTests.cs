using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Environment;
using Xunit;

namespace LakskongenSim.Tests.Environment
{
    /// <summary>
    /// gfx-polish-1: the terrain mesh carries the slope and the cavity of the ground without
    /// the wall ledges' terraces, and the ledges' strength (<see cref="TerrainMesh.Chunk.Macro"/>,
    /// the terrain shader's CUSTOM0). The heightfield terraces its walls every 48 m of height
    /// (TerrainGenerator WallRelief); seen from 1-2 km the terraces' snowy flats and dark risers
    /// drew the walls as regular stripes, so the shader's masks read the ground without its
    /// terraces where a terrace is small on screen, with the snow its flats and risers would
    /// hold between them. The heights themselves do not change.
    /// </summary>
    public class TerrainMacroTests
    {
        private const float Spacing = 7.8f;
        // TerrainGenerator.LedgeStepM and LedgeStrength.
        private const float LedgeStepM = 48f;
        private const float LedgeStrength = 0.28f;
        private const int Stride = TerrainMesh.MacroStride;

        private static float SmoothStep(float a, float b, float x)
        {
            float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// A wall rising along +X at <paramref name="slope"/>, with an optional groove (a gully
        /// running down it, <paramref name="grooveM"/> deep), terraced at full strength as
        /// WallRelief terraces it; <paramref name="ledges"/> gets each height's terrace offset
        /// and strength.
        /// </summary>
        private static float[] TerracedWall(int n, float slope, float grooveM, out WallLedges ledges)
        {
            var h = new float[n * n];
            var offsets = new float[n * n];
            var strength = new float[n * n];
            for (int row = 0; row < n; row++)
            {
                float groove = grooveM * MathF.Exp(-MathF.Pow((row - n / 2f) * Spacing / 20f, 2f));
                for (int col = 0; col < n; col++)
                {
                    float plain = slope * col * Spacing - groove;
                    float t = plain / LedgeStepM;
                    float step = MathF.Floor(t);
                    float terraced = step + SmoothStep(0.25f, 0.75f, t - step);
                    float ledge = (terraced - t) * LedgeStepM * LedgeStrength;
                    offsets[row * n + col] = ledge;
                    strength[row * n + col] = LedgeStrength;
                    h[row * n + col] = plain + ledge;
                }
            }
            ledges = new WallLedges(offsets, strength);
            return h;
        }

        private static float StdDev(IReadOnlyList<float> xs)
        {
            float mean = xs.Average();
            return MathF.Sqrt(xs.Sum(x => (x - mean) * (x - mean)) / xs.Count);
        }

        private static float SlopeOf(float[] normals, int v)
        {
            float nx = normals[v * 3], ny = normals[v * 3 + 1], nz = normals[v * 3 + 2];
            return MathF.Sqrt(nx * nx + nz * nz) / ny;
        }

        [Fact]
        public void GenerateHeightmapWithLedges_KeepsTheHeightsBitIdentical_AndPutsLedgesOnTheWallsOnly()
        {
            var gen = new TerrainGenerator(new FjordTerrainSettings { Seed = 42, Resolution = 129 });
            float[] plain = gen.GenerateHeightmap();
            float[] heights = gen.GenerateHeightmap(out WallLedges ledges);
            Assert.True(plain.SequenceEqual(heights), "the heights must not change");
            Assert.Equal(plain.Length, ledges.Offsets.Length);
            Assert.Equal(plain.Length, ledges.Strength.Length);
            int n = gen.Settings.Resolution;
            float half = gen.Settings.WorldSizeM * 0.5f;
            float sp = gen.Settings.SpacingM;
            int onWalls = 0, offWalls = 0;
            for (int row = 0; row < n; row++)
            {
                for (int col = 0; col < n; col++)
                {
                    int i = row * n + col;
                    float offset = ledges.Offsets[i], strength = ledges.Strength[i];
                    // |terraced - t| peaks at 0.2724 of a step (where the smoothstep's slope is
                    // 1), scaled like the strength by the walls' mask.
                    Assert.InRange(strength, 0f, LedgeStrength);
                    Assert.InRange(offset, -0.2725f * LedgeStepM * strength - 1e-4f, 0.2725f * LedgeStepM * strength + 1e-4f);
                    bool inWallBand = gen.IsWallBand(-half + col * sp, -half + row * sp);
                    if (strength > 0f)
                    {
                        if (inWallBand)
                            onWalls++;
                        else
                            offWalls++;
                    }
                    // The floor (below 8 m, WallRelief's mask) carries none.
                    if (heights[i] < 0f)
                        Assert.Equal(0f, strength);
                }
            }
            Assert.True(onWalls > 500, $"ledges on the walls: {onWalls}");
            Assert.Equal(0, offWalls);
        }

        [Fact]
        public void Macro_IsTheSlopeAndCavityOfTheHeightsMinusTheLedges_AndTheLedgesStrength()
        {
            var gen = new TerrainGenerator(new FjordTerrainSettings { Seed = 42, Resolution = 129 });
            float[] h = gen.GenerateHeightmap(out WallLedges ledges);
            int n = gen.Settings.Resolution;
            float size = gen.Settings.WorldSizeM;
            float sp = size / (n - 1);
            var ground = h.Select((v, i) => v - ledges.Offsets[i]).ToArray();
            var mesh = TerrainMesh.Build(h, n, size, 16, null, gen.FarFieldHeightAt, ledges);
            int checkedVertices = 0, differs = 0;
            foreach (var chunk in mesh.Chunks)
            {
                Assert.Equal(chunk.Columns * chunk.Rows * Stride, chunk.Macro.Length);
                for (int r = 0; r < chunk.Rows; r += 3)
                {
                    for (int c = 0; c < chunk.Columns; c += 3)
                    {
                        int row = chunk.FirstRow + r, col = chunk.FirstColumn + c;
                        int v = r * chunk.Columns + c;
                        var (mx, my, mz) = TerrainMesh.GridNormal(ground, n, size, col, row, gen.FarFieldHeightAt);
                        Assert.Equal(MathF.Sqrt(mx * mx + mz * mz) / my, chunk.Macro[v * Stride], 5);
                        Assert.Equal(TerrainMesh.Cavity(ground, n, sp, col, row), chunk.Macro[v * Stride + 1], 5);
                        Assert.Equal(ledges.Strength[row * n + col], chunk.Macro[v * Stride + 2]);
                        Assert.Equal(0f, chunk.Macro[v * Stride + 3]);
                        // The vertex's own cavity stays the terraced ground's.
                        Assert.Equal(TerrainMesh.Cavity(h, n, sp, col, row), chunk.Colors[v * 4], 5);
                        if (MathF.Abs(SlopeOf(chunk.Normals, v) - chunk.Macro[v * Stride]) > 0.05f)
                            differs++;
                        checkedVertices++;
                    }
                }
            }
            Assert.True(checkedVertices > 1000);
            Assert.True(differs > 100, $"the terraces change the slope on the walls ({differs} vertices)");
        }

        [Theory]
        [InlineData(0.8f)]
        [InlineData(1.2f)]
        [InlineData(1.5f)]
        [InlineData(2.0f)]
        public void Macro_TakesTheTerracesOut_ExactlyAndNothingElse(float slope)
        {
            const int n = 65;
            var heights = TerracedWall(n, slope, grooveM: 0f, out WallLedges ledges);
            var mesh = TerrainMesh.Build(heights, n, (n - 1) * Spacing, chunkQuads: 16, ledges: ledges);
            var vertexSlopes = new List<float>();
            var macroSlopes = new List<float>();
            foreach (var chunk in mesh.Chunks)
            {
                for (int r = 0; r < chunk.Rows; r++)
                {
                    for (int c = 0; c < chunk.Columns; c++)
                    {
                        int col = chunk.FirstColumn + c;
                        // Away from the grid's ends, where the stencils (the cavity's 2
                        // cells) are one-sided.
                        if (col < 2 || col > n - 3)
                            continue;
                        int v = r * chunk.Columns + c;
                        vertexSlopes.Add(SlopeOf(chunk.Normals, v));
                        macroSlopes.Add(chunk.Macro[v * Stride]);
                        Assert.InRange(chunk.Macro[v * Stride + 1], 0.5f - 1e-4f, 0.5f + 1e-4f);
                        Assert.Equal(LedgeStrength, chunk.Macro[v * Stride + 2]);
                    }
                }
            }
            Assert.True(StdDev(vertexSlopes) > 0.08f * slope, $"the terraces swing the vertex slope (sd {StdDev(vertexSlopes):F3})");
            // A plane under the terraces: one slope, one cavity.
            Assert.True(macroSlopes.All(m => MathF.Abs(m - slope) < 1e-3f), $"slope {slope}: macro {macroSlopes.Min():F4}..{macroSlopes.Max():F4}");
        }

        [Fact]
        public void Macro_TheTerracesFlatsAndRisersSpreadTheSlopeByTheLedgesStrength()
        {
            // The shader holds half a terrace's snow at the slope times 1 - k and half at
            // 1 + k (k the ledges' strength): on the synthetic wall the vertex slopes (the
            // terraces as the grid samples them) lie within that spread, flats at the bottom.
            const int n = 65;
            const float slope = 1.4f;
            var heights = TerracedWall(n, slope, grooveM: 0f, out WallLedges ledges);
            var mesh = TerrainMesh.Build(heights, n, (n - 1) * Spacing, chunkQuads: 64, ledges: ledges);
            var chunk = mesh.Chunks.Single();
            var slopes = new List<float>();
            for (int col = 2; col < n - 2; col++)
                slopes.Add(SlopeOf(chunk.Normals, (n / 2) * n + col));
            float k = chunk.Macro[((n / 2) * n + n / 2) * Stride + 2];
            Assert.InRange(slopes.Min(), slope * (1f - k) - 0.02f, slope);
            // The riser's mean steepening is k (the smoothstep's mean slope over the riser is 2);
            // its peak, 2k, the grid samples part of.
            Assert.InRange(slopes.Max(), slope * (1f + k), slope * (1f + 2f * k) + 0.02f);
            // Half flat, half riser: the mean slope is the wall's.
            Assert.InRange(slopes.Average(), slope * 0.97f, slope * 1.03f);
        }

        [Fact]
        public void Macro_KeepsTheGulliesSharp()
        {
            // A 12 m deep gully down a terraced 1.4 wall: the macro cavity finds it at the
            // grid's own resolution (the gully's floor concave, its shoulders convex), at every
            // phase of the terraces along it.
            const int n = 65;
            var heights = TerracedWall(n, 1.4f, grooveM: 12f, out WallLedges ledges);
            var mesh = TerrainMesh.Build(heights, n, (n - 1) * Spacing, chunkQuads: 64, ledges: ledges);
            var chunk = mesh.Chunks.Single();
            int mid = n / 2;
            for (int col = 4; col < n - 4; col++)
            {
                float floor = chunk.Macro[(mid * n + col) * Stride + 1];
                float shoulder = chunk.Macro[((mid + 4) * n + col) * Stride + 1];
                float away = chunk.Macro[((mid + 14) * n + col) * Stride + 1];
                Assert.True(floor > 0.6f, $"col {col}: gully floor cavity {floor:F3}");
                Assert.True(shoulder < 0.5f, $"col {col}: gully shoulder cavity {shoulder:F3}");
                Assert.InRange(away, 0.49f, 0.51f);
            }
        }

        [Fact]
        public void Macro_WithoutLedges_IsTheVertexsOwnSlopeAndCavity()
        {
            var gen = new TerrainGenerator(new FjordTerrainSettings { Seed = 7, Resolution = 65 });
            float[] h = gen.GenerateHeightmap();
            var mesh = TerrainMesh.Build(h, 65, gen.Settings.WorldSizeM, 16);
            foreach (var chunk in mesh.Chunks)
            {
                int count = chunk.Columns * chunk.Rows;
                Assert.Equal(count * Stride, chunk.Macro.Length);
                for (int v = 0; v < count; v++)
                {
                    Assert.Equal(SlopeOf(chunk.Normals, v), chunk.Macro[v * Stride], 5);
                    Assert.Equal(chunk.Colors[v * 4], chunk.Macro[v * Stride + 1]);
                    Assert.Equal(0f, chunk.Macro[v * Stride + 2]);
                }
            }
        }

        [Fact]
        public void Macro_IsSharedExactlyAcrossChunkBorders()
        {
            var gen = new TerrainGenerator(new FjordTerrainSettings { Seed = 42, Resolution = 129 });
            float[] h = gen.GenerateHeightmap(out WallLedges ledges);
            var mesh = TerrainMesh.Build(h, 129, gen.Settings.WorldSizeM, 64, null, gen.FarFieldHeightAt, ledges);
            var seen = new Dictionary<(int, int), (float, float, float)>();
            foreach (var chunk in mesh.Chunks)
            {
                for (int r = 0; r < chunk.Rows; r++)
                {
                    for (int c = 0; c < chunk.Columns; c++)
                    {
                        int v = r * chunk.Columns + c;
                        var key = (chunk.FirstColumn + c, chunk.FirstRow + r);
                        var value = (chunk.Macro[v * Stride], chunk.Macro[v * Stride + 1], chunk.Macro[v * Stride + 2]);
                        if (seen.TryGetValue(key, out var other))
                            Assert.Equal(other, value);
                        seen[key] = value;
                    }
                }
            }
            Assert.Equal(129 * 129, seen.Count);
        }

        [Fact]
        public void Build_RejectsLedgesOfAnotherGrid()
        {
            var h = new float[9 * 9];
            var wrong = new WallLedges(new float[10], new float[81]);
            Assert.Throws<ArgumentException>(() => TerrainMesh.Build(h, 9, 64f, 8, null, null, wrong));
            var wrongStrength = new WallLedges(new float[81], new float[3]);
            Assert.Throws<ArgumentException>(() => TerrainMesh.Build(h, 9, 64f, 8, null, null, wrongStrength));
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
                Assert.Equal(count * Stride, chunk.Macro.Length);
                for (int v = 0; v < count; v++)
                {
                    Assert.Equal(SlopeOf(chunk.Normals, v), chunk.Macro[v * Stride], 4);
                    Assert.Equal(chunk.Colors[v * 4], chunk.Macro[v * Stride + 1]);
                    Assert.Equal(0f, chunk.Macro[v * Stride + 2]);
                }
            }
        }
    }
}
