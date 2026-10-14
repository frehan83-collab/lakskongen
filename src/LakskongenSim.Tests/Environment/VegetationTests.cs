using System;
using System.Linq;
using LakskongenSim.Environment;
using Xunit;

namespace LakskongenSim.Tests.Environment
{
    /// <summary>
    /// G2-02: woods only where western Norwegian fjord sides carry them —
    /// above the splash zone, below a 450-550 m treeline, gentler than about
    /// 42°, sheltered from the open sea — and the trees stand exactly inside
    /// the masks the terrain shader paints.
    /// </summary>
    public class VegetationTests
    {
        private static readonly Lazy<Fixture> Shared = new(() => new Fixture(42));

        private sealed class Fixture
        {
            public readonly int N;
            public readonly float World;
            public readonly float Spacing;
            public readonly float[] Heights;
            public readonly float[] WaterExposure;
            public readonly VegetationSettings Settings;
            public readonly VegetationMasks Masks;
            public readonly System.Collections.Generic.List<TreeInstance> Trees;

            public Fixture(int seed)
            {
                var generator = new TerrainGenerator(seed);
                N = generator.Settings.Resolution;
                World = generator.Settings.WorldSizeM;
                Spacing = generator.Settings.SpacingM;
                Heights = generator.GenerateHeightmap();
                var bake = WaterBake.Build(Heights, N, World, 0f, generator.Settings.PrevailingSeaDirX, generator.Settings.PrevailingSeaDirZ);
                WaterExposure = new float[N * N];
                for (int i = 0; i < WaterExposure.Length; i++)
                    WaterExposure[i] = bake.Rgba[i * 4 + 2];
                Settings = new VegetationSettings { Seed = seed };
                Masks = VegetationScatter.BuildMasks(Heights, N, World, Settings, WaterExposure);
                Trees = VegetationScatter.Scatter(Heights, Masks, Settings);
            }
        }

        private static float IndependentSlopeTan(Fixture f, int c, int r)
        {
            int n = f.N;
            float dx = (f.Heights[r * n + Math.Min(c + 1, n - 1)] - f.Heights[r * n + Math.Max(c - 1, 0)]) / ((Math.Min(c + 1, n - 1) - Math.Max(c - 1, 0)) * f.Spacing);
            float dz = (f.Heights[Math.Min(r + 1, n - 1) * n + c] - f.Heights[Math.Max(r - 1, 0) * n + c]) / ((Math.Min(r + 1, n - 1) - Math.Max(r - 1, 0)) * f.Spacing);
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        [Fact]
        public void Treeline_StaysBetween450And550Metres()
        {
            var s = new VegetationSettings();
            float lo = float.MaxValue, hi = float.MinValue;
            for (float x = -2000f; x <= 2000f; x += 37f)
            {
                for (float z = -2000f; z <= 2000f; z += 37f)
                {
                    float t = VegetationScatter.TreelineAt(s, x, z);
                    lo = Math.Min(lo, t);
                    hi = Math.Max(hi, t);
                }
            }
            Assert.InRange(lo, 450f, 550f);
            Assert.InRange(hi, 450f, 550f);
            // The noise really moves it (not a flat line).
            Assert.True(hi - lo > 50f, $"treeline spans only {lo:F0}-{hi:F0} m");
        }

        [Fact]
        public void ForestMask_IsZeroOutsideTheSlopeAltitudeAndShoreMasks()
        {
            var f = Shared.Value;
            int n = f.N;
            float half = f.World * 0.5f;
            int inside = 0;
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    int i = r * n + c;
                    float h = f.Heights[i];
                    float slope = IndependentSlopeTan(f, c, r);
                    float x = -half + c * f.Spacing, z = -half + r * f.Spacing;
                    bool outside = h < f.Settings.ShoreClearanceM || slope >= f.Settings.MaxSlopeTan
                        || h >= VegetationScatter.TreelineAt(f.Settings, x, z);
                    if (outside)
                        Assert.True(f.Masks.Forest[i] == 0f, $"wood {f.Masks.Forest[i]:F3} at ({x:F0}, {z:F0}) h {h:F1} slope {slope:F2}");
                    else if (f.Masks.Forest[i] > 0f)
                        inside++;
                    Assert.InRange(f.Masks.Forest[i], 0f, 1f);
                    Assert.InRange(f.Masks.Birch[i], 0f, 1f);
                    Assert.InRange(f.Masks.Meadow[i], 0f, 1f);
                }
            }
            Assert.True(inside > n * n / 50, $"only {inside} wooded vertices");
        }

        [Fact]
        public void EveryTree_StandsInsideTheMasks_OnTheGround()
        {
            var f = Shared.Value;
            float half = f.World * 0.5f;
            Assert.NotEmpty(f.Trees);
            foreach (var t in f.Trees)
            {
                float h = VegetationScatter.Bilinear(f.Heights, f.N, f.Spacing, half, t.X, t.Z);
                Assert.Equal(h, t.Y, 3);
                Assert.True(h >= f.Settings.ShoreClearanceM, $"tree at ({t.X:F0}, {t.Z:F0}) only {h:F2} m above the sea");
                Assert.True(h < VegetationScatter.TreelineAt(f.Settings, t.X, t.Z), $"tree at {h:F0} m above the treeline");
                float slope = VegetationScatter.SlopeTanAt(f.Heights, f.N, f.Spacing, half, t.X, t.Z);
                Assert.True(slope < f.Settings.MaxSlopeTan, $"tree on a {MathF.Atan(slope) * 57.2958f:F1}° slope");
                float density = VegetationScatter.Bilinear(f.Masks.Forest, f.N, f.Spacing, half, t.X, t.Z);
                Assert.True(density >= f.Settings.MinDensity, $"tree where the wood density is {density:F3}");
                float exposure = VegetationScatter.Bilinear(f.Masks.LandExposure, f.N, f.Spacing, half, t.X, t.Z);
                Assert.True(exposure < f.Settings.ExposureEnd, $"tree on a coast with exposure {exposure:F2}");
                Assert.InRange(t.Scale, 0.3f, 1.3f);
                Assert.InRange(t.Yaw, 0f, 6.2832f);
            }
        }

        [Fact]
        public void Woods_HaveADensityAndAMixLikeAFjordSide()
        {
            var f = Shared.Value;
            // Tens of thousands of trees on the seed-42 fjord, not a token few
            // and not a GPU-melting carpet.
            Assert.InRange(f.Trees.Count, 12000, 90000);
            var spruce = f.Trees.Where(t => t.Species == TreeSpecies.Spruce).ToList();
            var birch = f.Trees.Where(t => t.Species == TreeSpecies.Birch).ToList();
            Assert.True(birch.Count > spruce.Count, $"birch {birch.Count} vs spruce {spruce.Count}: birch dominates western fjord sides");
            Assert.True(spruce.Count >= 200, $"spruce {spruce.Count}: the planted stands are missing");
            // Spruce stands keep to the lower, gentler slopes; the walls are birch.
            float half = f.World * 0.5f;
            Assert.All(spruce, t =>
            {
                Assert.True(t.Y < f.Settings.SpruceTopM, $"spruce at {t.Y:F0} m");
                float slope = VegetationScatter.SlopeTanAt(f.Heights, f.N, f.Spacing, half, t.X, t.Z);
                Assert.True(slope < f.Settings.SpruceMaxSlopeTan, $"spruce on a {MathF.Atan(slope) * 57.2958f:F1}° slope");
            });
            Assert.True(Median(spruce.Select(t => t.Y)) < Median(birch.Select(t => t.Y)));
            // Near the treeline the birch is stunted.
            var high = f.Trees.Where(t => t.Y > 430f).ToList();
            var low = f.Trees.Where(t => t.Y < 250f).ToList();
            Assert.NotEmpty(high);
            Assert.True(high.Average(t => t.Scale) < 0.85f * low.Average(t => t.Scale));
        }

        [Fact]
        public void SkerriesOutsideTheSill_AreTreeless()
        {
            var f = Shared.Value;
            var settings = new TerrainGenerator(42).Settings;
            // The strandflat lies beyond the coastline outside the mouth.
            int strand = f.Trees.Count(t => t.Z < settings.MouthZ - 450f);
            Assert.True(strand == 0, $"{strand} trees on the skerries");
        }

        [Fact]
        public void Meadows_LieOnFlatLowGroundOnly()
        {
            var f = Shared.Value;
            int n = f.N;
            int meadow = 0;
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    int i = r * n + c;
                    if (f.Masks.Meadow[i] <= 0f)
                        continue;
                    meadow++;
                    Assert.True(f.Heights[i] < 60f && f.Heights[i] >= f.Settings.ShoreClearanceM - 1f);
                    Assert.True(IndependentSlopeTan(f, c, r) < 0.25f);
                }
            }
            Assert.True(meadow > 0, "the delta and the shore terraces hold no meadow");
        }

        [Fact]
        public void Scatter_IsDeterministic_AndFollowsTheSeed()
        {
            var f = Shared.Value;
            var again = VegetationScatter.Scatter(f.Heights, f.Masks, f.Settings);
            Assert.Equal(f.Trees, again);
            var other = VegetationScatter.Scatter(f.Heights, f.Masks, f.Settings with { Seed = 7 });
            Assert.False(f.Trees.SequenceEqual(other), "a different seed scattered the same woods");
        }

        [Fact]
        public void TerrainMesh_CarriesTheMasksInItsVertexColours()
        {
            var f = Shared.Value;
            var mesh = TerrainMesh.Build(f.Heights, f.N, f.World, 64, f.Masks);
            foreach (var chunk in mesh.Chunks)
            {
                for (int r = 0; r < chunk.Rows; r += 7)
                {
                    for (int c = 0; c < chunk.Columns; c += 7)
                    {
                        int v = r * chunk.Columns + c;
                        int g = (chunk.FirstRow + r) * f.N + chunk.FirstColumn + c;
                        Assert.Equal(f.Masks.Forest[g], chunk.Colors[v * 4 + 1]);
                        Assert.Equal(f.Masks.Birch[g], chunk.Colors[v * 4 + 2]);
                        Assert.Equal(f.Masks.Meadow[g], chunk.Colors[v * 4 + 3]);
                    }
                }
            }
            // Without masks the ground is bare.
            var bare = TerrainMesh.Build(f.Heights, f.N, f.World, 64);
            Assert.All(bare.Chunks, ch => Assert.Equal(0f, ch.Colors[1]));
        }

        private static float Median(System.Collections.Generic.IEnumerable<float> values)
        {
            var sorted = values.OrderBy(v => v).ToArray();
            return sorted[sorted.Length / 2];
        }
    }
}
