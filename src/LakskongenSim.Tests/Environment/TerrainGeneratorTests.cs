using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LakskongenSim.Environment;
using Xunit;

namespace LakskongenSim.Tests.Environment
{
    /// <summary>
    /// G1-01: the gradient noise, the U-trough fjord heightfield and its mesh.
    /// The old terrain warped every vertex by integer-hash white noise (F464)
    /// and wound its triangles counter-clockwise (F750): the fjord rendered as
    /// floating shards. These pin the replacement's shape, smoothness,
    /// determinism and winding.
    /// </summary>
    public class OpenSimplex2Tests
    {
        [Fact]
        public void Noise_IsDeterministicAndBounded()
        {
            var values = new List<float>();
            for (int i = 0; i < 40_000; i++)
            {
                float x = (i % 200) * 0.173f - 17f, y = (i / 200) * 0.191f + 3f;
                float v = OpenSimplex2.Noise2(7, x, y);
                Assert.Equal(v, OpenSimplex2.Noise2(7, x, y));
                Assert.InRange(v, -1f, 1f);
                values.Add(v);
            }
            Assert.True(values.Max() > 0.6f && values.Min() < -0.6f, $"range {values.Min()}..{values.Max()}");
            Assert.InRange(values.Average(), -0.05, 0.05);
        }

        [Fact]
        public void Noise_DependsOnTheSeed()
        {
            int differ = 0;
            for (int i = 0; i < 1000; i++)
                if (OpenSimplex2.Noise2(1, i * 0.37f, i * 0.11f) != OpenSimplex2.Noise2(2, i * 0.37f, i * 0.11f))
                    differ++;
            Assert.True(differ > 900, $"{differ}/1000 samples differ between seeds");
        }

        [Fact]
        public void Noise_IsContinuous()
        {
            // Gradient noise has a bounded slope (about 4 per lattice unit);
            // white noise sampled 0.001 apart jumps by up to 2.
            const float step = 0.001f;
            float worst = 0f;
            for (int i = 0; i < 20_000; i++)
            {
                float x = (i % 140) * 0.731f - 50f, y = (i / 140) * 0.613f - 40f;
                worst = Math.Max(worst, Math.Abs(OpenSimplex2.Noise2(3, x + step, y) - OpenSimplex2.Noise2(3, x, y)) / step);
                worst = Math.Max(worst, Math.Abs(OpenSimplex2.Noise2(3, x, y + step) - OpenSimplex2.Noise2(3, x, y)) / step);
            }
            Assert.True(worst < 8f, $"steepest noise slope {worst} per lattice unit");
        }

        [Fact]
        public void Ridged_StaysInItsRange()
        {
            for (int i = 0; i < 5000; i++)
            {
                float v = FractalNoise.Ridged(11, i * 0.071f, i * 0.053f, 4, 2f, 0.4f, 0.15f);
                Assert.InRange(v, 0f, 1f);
            }
        }
    }

    public class TerrainGeneratorTests
    {
        private static TerrainGenerator Gen(int seed) => new(new FjordTerrainSettings { Seed = seed });

        private static readonly float[] BasinSections = { -900f, -500f, -100f, 300f, 700f, 1100f };

        [Fact]
        public void Heightfield_IsSmoothOutsideTheWallBand_ForSeeds1To10()
        {
            // Acceptance 2: the 99th percentile of |Δh|/spacing is < 2.0
            // outside the wall band, at the shipped 512² grid (7.8 m).
            for (int seed = 1; seed <= 10; seed++)
            {
                var gen = Gen(seed);
                var st = gen.Settings;
                int n = st.Resolution;
                float sp = st.SpacingM, half = st.WorldSizeM / 2f;
                float[] h = gen.GenerateHeightmap();
                var wall = new bool[n * n];
                Parallel.For(0, n, r =>
                {
                    for (int c = 0; c < n; c++)
                        wall[r * n + c] = gen.IsWallBand(-half + c * sp, -half + r * sp);
                });
                var slopes = new List<float>(n * n * 2);
                for (int r = 0; r < n; r++)
                {
                    for (int c = 0; c < n; c++)
                    {
                        int i = r * n + c;
                        if (c + 1 < n && !wall[i] && !wall[i + 1])
                            slopes.Add(Math.Abs(h[i + 1] - h[i]) / sp);
                        if (r + 1 < n && !wall[i] && !wall[i + n])
                            slopes.Add(Math.Abs(h[i + n] - h[i]) / sp);
                    }
                }
                slopes.Sort();
                float p99 = slopes[(int)(slopes.Count * 0.99)];
                Assert.True(p99 < 2.0f, $"seed {seed}: p99 |Δh|/spacing outside the walls = {p99:F2}");
            }
        }

        [Fact]
        public void Heightfield_HasNoWarpAliasing()
        {
            // A white-noise warp throws neighbouring samples to unrelated
            // places, so heights 0.5 m apart differ by tens of metres.
            // A smooth field changes by at most its slope times 0.5 m.
            var gen = Gen(42);
            float worst = 0f;
            for (int i = 0; i < 4000; i++)
            {
                float x = -1900f + (i % 80) * 47.3f, z = -1900f + (i / 80) * 75.1f;
                float h = gen.HeightAt(x, z);
                worst = Math.Max(worst, Math.Abs(gen.HeightAt(x + 0.5f, z) - h) / 0.5f);
                worst = Math.Max(worst, Math.Abs(gen.HeightAt(x, z + 0.5f) - h) / 0.5f);
            }
            Assert.True(worst < 8f, $"steepest sub-grid slope {worst:F1} (white-noise warp shows > 50)");
        }

        [Fact]
        public void CrossSections_FollowTheUTroughProfile()
        {
            // Acceptance 3: floor −120 to −250 m, median wall slope 30–60°,
            // shoulders 350–700 m, half-width 350–600 m.
            for (int seed = 1; seed <= 5; seed++)
            {
                var gen = Gen(seed);
                var wallSlopes = new List<double>();
                var shoulders = new List<float>();
                foreach (float z in BasinSections)
                {
                    float cx = gen.CenterX(z);
                    float floor = float.MaxValue;
                    for (float x = cx - 300f; x <= cx + 300f; x += 5f)
                        floor = Math.Min(floor, gen.HeightAt(x, z));
                    Assert.InRange(floor, -250f, -120f);
                    foreach (int side in new[] { -1, 1 })
                    {
                        var (w, _, shoulder, run) = gen.ProfileAt(z, side);
                        Assert.InRange(w, 350f, 600f);
                        Assert.InRange(shoulder, 350f, 700f);
                        // Shoreline to shoulder: slope over 10 m steps of
                        // the land part of the wall.
                        for (float d = w + 10f; d < w + run - 10f; d += 10f)
                        {
                            float x0 = cx + side * d;
                            float h0 = gen.HeightAt(x0, z), h1 = gen.HeightAt(x0 + side * 10f, z);
                            if (h0 > 5f)
                                wallSlopes.Add(Math.Atan(Math.Abs(h1 - h0) / 10.0) * 180.0 / Math.PI);
                        }
                        shoulders.Add(gen.HeightAt(cx + side * (w + run), z));
                    }
                }
                wallSlopes.Sort();
                shoulders.Sort();
                double medianSlope = wallSlopes[wallSlopes.Count / 2];
                float medianShoulder = shoulders[shoulders.Count / 2];
                Assert.True(medianSlope is >= 30 and <= 60, $"seed {seed}: median wall slope {medianSlope:F1}°");
                Assert.True(medianShoulder is >= 350 and <= 700, $"seed {seed}: median shoulder {medianShoulder:F0} m");
            }
        }

        [Fact]
        public void Heightfield_IsIdenticalAcrossThreadsInstancesAndRuns()
        {
            // Acceptance 4: the same heightfield in every process. Parallel and
            // sequential builds and fresh instances agree bit for bit, and the
            // hash is pinned, so a headless run, a windowed run (FjordTerrain
            // logs the hash) and CI on another platform must all match.
            var a = Gen(42).GenerateHeightmap();
            var b = Gen(42).GenerateHeightmapSequential();
            Assert.Equal(TerrainGenerator.HeightfieldHash(a), TerrainGenerator.HeightfieldHash(b));
            Assert.NotEqual(TerrainGenerator.HeightfieldHash(a), TerrainGenerator.HeightfieldHash(Gen(43).GenerateHeightmap()));
            Assert.Equal(GoldenHashSeed42, TerrainGenerator.HeightfieldHash(a));
        }

        /// <summary>Seed 42 at 512² over 4 km (the game's fjord). Update only with a deliberate terrain change.</summary>
        internal const string GoldenHashSeed42 = "75de3c1de415c49e";

        [Fact]
        public void Channel_IsNavigableFromTheHarbourToTheLastPenSlot()
        {
            // Wellboats leave from z = −1880 (WellboatVisualManager) and sail
            // lanes ±45 m beside the centreline to every pen slot.
            for (int seed = 1; seed <= 5; seed++)
            {
                var gen = Gen(seed);
                var (_, end) = gen.PenSlotRange();
                for (float z = -1900f; z <= end + 50f; z += 10f)
                {
                    float cx = gen.CenterX(z);
                    foreach (float lane in new[] { -60f, 0f, 60f })
                        Assert.True(gen.HeightAt(cx + lane, z) < -10f, $"seed {seed}: shallow or dry at z={z}, lane {lane}");
                }
            }
        }

        [Fact]
        public void Mouth_HasASillAndASkerryStrandflat()
        {
            var gen = Gen(42);
            var st = gen.Settings;
            float sill = gen.HeightAt(gen.CenterX(st.MouthZ), st.MouthZ);
            Assert.InRange(sill, -60f, -30f);
            int skerries = 0;
            float highest = float.MinValue;
            // Past the irregular coastline (MouthZ − 40 ± 120 m, blended over 220 m).
            for (float z = st.MouthZ - 390f; z >= -1990f; z -= 20f)
            {
                for (float x = -1900f; x <= 1900f; x += 20f)
                {
                    float h = gen.HeightAt(x, z);
                    highest = Math.Max(highest, h);
                    if (h > 0.5f)
                        skerries++;
                }
            }
            Assert.True(skerries > 0, "no skerry breaks the surface outside the mouth");
            Assert.True(highest < 40f, $"the strandflat rises to {highest:F0} m");
        }

        [Fact]
        public void Head_ClosesTheFjordWithADelta()
        {
            var gen = Gen(42);
            var st = gen.Settings;
            float cx = gen.CenterX(st.HeadZ);
            float delta = gen.HeightAt(cx, st.HeadZ + 60f);
            Assert.InRange(delta, 0.5f, 6f);
            Assert.True(gen.HeightAt(cx, st.HeadZ + 600f) > 200f, "mountains close the fjord behind the head");
        }

        [Fact]
        public void PenSlots_LieInDeepOpenWater()
        {
            foreach (var p in Gen(42).GetRecommendedLocations(12))
                Assert.True(Gen(42).HeightAt(p.X, p.Z) < -20f, $"pen slot at ({p.X:F0}, {p.Z:F0}) is shallow");
        }
    }

    public class TerrainMeshTests
    {
        private static (TerrainMesh mesh, float[] heights) BuildFjord(int resolution)
        {
            var gen = new TerrainGenerator(new FjordTerrainSettings { Seed = 42, Resolution = resolution });
            float[] h = gen.GenerateHeightmap();
            return (TerrainMesh.Build(h, resolution, gen.Settings.WorldSizeM), h);
        }

        [Fact]
        public void Triangles_FaceUpAndAgreeWithTheirNormals()
        {
            // Acceptance 1, on the shipped 512² fjord: Godot's
            // Plane(v0, v1, v2) normal ((v0 − v2) × (v0 − v1), clockwise front
            // faces) points up for every triangle, and along every corner's
            // stored normal. Counter-clockwise winding made every one face down.
            var (mesh, _) = BuildFjord(512);
            long triangles = 0;
            foreach (var chunk in mesh.Chunks)
            {
                var p = chunk.Positions;
                var nrm = chunk.Normals;
                for (int t = 0; t < chunk.Indices.Length; t += 3)
                {
                    int a = chunk.Indices[t], b = chunk.Indices[t + 1], c = chunk.Indices[t + 2];
                    float ux = p[a * 3] - p[c * 3], uy = p[a * 3 + 1] - p[c * 3 + 1], uz = p[a * 3 + 2] - p[c * 3 + 2];
                    float vx = p[a * 3] - p[b * 3], vy = p[a * 3 + 1] - p[b * 3 + 1], vz = p[a * 3 + 2] - p[b * 3 + 2];
                    float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                    Assert.True(ny > 0f, $"triangle {t / 3} faces down");
                    foreach (int v in new[] { a, b, c })
                    {
                        float dot = nx * nrm[v * 3] + ny * nrm[v * 3 + 1] + nz * nrm[v * 3 + 2];
                        Assert.True(dot > 0f, $"triangle {t / 3} of chunk ({chunk.FirstColumn}, {chunk.FirstRow}) disagrees with vertex {v}: face ({nx:F1}, {ny:F1}, {nz:F1}), normal ({nrm[v * 3]:F2}, {nrm[v * 3 + 1]:F2}, {nrm[v * 3 + 2]:F2})");
                    }
                    triangles++;
                }
            }
            Assert.Equal(511L * 511L * 2L, triangles);
        }

        [Fact]
        public void Chunks_ShareTheirBorderVerticesExactly()
        {
            var (mesh, heights) = BuildFjord(129);
            int n = mesh.Resolution;
            var seen = new Dictionary<(int col, int row), (float y, float nx, float ny, float nz)>();
            foreach (var chunk in mesh.Chunks)
            {
                for (int r = 0; r < chunk.Rows; r++)
                {
                    for (int c = 0; c < chunk.Columns; c++)
                    {
                        int v = r * chunk.Columns + c;
                        var key = (chunk.FirstColumn + c, chunk.FirstRow + r);
                        var value = (chunk.Positions[v * 3 + 1], chunk.Normals[v * 3], chunk.Normals[v * 3 + 1], chunk.Normals[v * 3 + 2]);
                        Assert.Equal(heights[key.Item2 * n + key.Item1], value.Item1);
                        if (seen.TryGetValue(key, out var other))
                            Assert.Equal(other, value);
                        seen[key] = value;
                    }
                }
            }
            Assert.Equal(n * n, seen.Count);
        }

        [Fact]
        public void Normals_AreCentralDifferences()
        {
            var (mesh, heights) = BuildFjord(129);
            int n = mesh.Resolution;
            float sp = 4000f / (n - 1);
            var chunk = mesh.Chunks[mesh.Chunks.Count / 2];
            int r = chunk.Rows / 2, c = chunk.Columns / 2;
            int row = chunk.FirstRow + r, col = chunk.FirstColumn + c;
            float dhdx = (heights[row * n + col + 1] - heights[row * n + col - 1]) / (2f * sp);
            float dhdz = (heights[(row + 1) * n + col] - heights[(row - 1) * n + col]) / (2f * sp);
            float len = MathF.Sqrt(dhdx * dhdx + 1f + dhdz * dhdz);
            int v = r * chunk.Columns + c;
            Assert.Equal(-dhdx / len, chunk.Normals[v * 3], 5);
            Assert.Equal(1f / len, chunk.Normals[v * 3 + 1], 5);
            Assert.Equal(-dhdz / len, chunk.Normals[v * 3 + 2], 5);
        }
    }
}
