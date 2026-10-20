using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Environment;
using Xunit;

namespace LakskongenSim.Tests.Environment
{
    /// <summary>
    /// G2-02 shore props: naust rows and a quay stand on sheltered, gentle,
    /// dry shore, face the water, keep their spacing, and clear the woods
    /// around them.
    /// </summary>
    public class ShorePropTests
    {
        private static readonly Lazy<Fixture> Shared = new(() => new Fixture(42));

        private sealed class Fixture
        {
            public readonly int N;
            public readonly float World, Spacing, Half;
            public readonly float[] Heights, ShoreDistance;
            public readonly VegetationMasks Masks;
            public readonly List<ShoreProp> Props;
            public readonly List<TreeInstance> Trees;
            public readonly ShorePropSettings S = new();

            public Fixture(int seed)
            {
                var generator = new TerrainGenerator(seed);
                N = generator.Settings.Resolution;
                World = generator.Settings.WorldSizeM;
                Spacing = generator.Settings.SpacingM;
                Half = World * 0.5f;
                Heights = generator.GenerateHeightmap();
                var bake = WaterBake.Build(Heights, N, World, 0f, generator.Settings.PrevailingSeaDirX, generator.Settings.PrevailingSeaDirZ);
                var exposure = new float[N * N];
                ShoreDistance = new float[N * N];
                for (int i = 0; i < exposure.Length; i++)
                {
                    exposure[i] = bake.Rgba[i * 4 + 2];
                    ShoreDistance[i] = bake.Rgba[i * 4 + 1];
                }
                // The ground cover the renderer builds (F2-19: kept off the
                // land pads and their rims, whose flat tops are not shore).
                var cover = FjordGroundCover.Build(generator, Heights, bake);
                Masks = cover.Masks;
                Props = cover.Props;
                Trees = cover.Trees;
                Cleared = cover.Cleared;
            }

            public readonly IReadOnlyList<GroundRect> Cleared;

            public float H(float x, float z) => VegetationScatter.Bilinear(Heights, N, Spacing, Half, x, z);
        }

        [Fact]
        public void TheFjordGetsNaustRowsAndOneQuay()
        {
            var f = Shared.Value;
            var naust = f.Props.Where(p => p.Kind == ShorePropKind.Naust).ToList();
            Assert.InRange(naust.Count, 2, f.S.MaxGroups * f.S.NaustPerGroup);
            Assert.Single(f.Props, p => p.Kind == ShorePropKind.Quay);
            // Both paints appear.
            Assert.Contains(naust, p => p.Variant == 0);
            Assert.Contains(naust, p => p.Variant == 1);
        }

        [Fact]
        public void EveryNaust_StandsOnGentleDryShore_FacingTheWater()
        {
            var f = Shared.Value;
            foreach (var p in f.Props.Where(p => p.Kind == ShorePropKind.Naust))
            {
                float h = f.H(p.X, p.Z);
                Assert.InRange(h, f.S.MinHeightM, f.S.MaxHeightM);
                Assert.True(ShorePropPlacer.NaustFits(f.Heights, f.N, f.Spacing, f.Half, f.ShoreDistance, f.S, p.X, p.Z, p.FacingX, p.FacingZ),
                    $"naust at ({p.X:F0}, {p.Z:F0}) does not fit its plot");
                // Water ahead of the door, dry land behind it.
                Assert.True(f.H(p.X + p.FacingX * 40f, p.Z + p.FacingZ * 40f) < 0f, "no water in front of the door");
                Assert.True(f.H(p.X - p.FacingX * 6f, p.Z - p.FacingZ * 6f) > 0f, "the back wall stands in the water");
                // The yaw turns the model's -Z onto the facing.
                Assert.Equal(p.FacingX, -MathF.Sin(p.Yaw), 3);
                Assert.Equal(p.FacingZ, -MathF.Cos(p.Yaw), 3);
                // The base sits on the ground: never below the centre, never above the highest corner.
                Assert.InRange(p.Y, h - 0.2f, h + f.S.NaustLengthM * f.S.MaxSlopeTan * 1.6f);
            }
        }

        [Fact]
        public void NaustRows_KeepTheirSpacingAndGroupsLieFarApart()
        {
            var f = Shared.Value;
            var naust = f.Props.Where(p => p.Kind == ShorePropKind.Naust).ToList();
            for (int i = 0; i < naust.Count; i++)
            {
                for (int j = i + 1; j < naust.Count; j++)
                {
                    float d = MathF.Sqrt((naust[i].X - naust[j].X) * (naust[i].X - naust[j].X) + (naust[i].Z - naust[j].Z) * (naust[i].Z - naust[j].Z));
                    // Same row: at least a naust's width plus a gap apart; other rows far away.
                    Assert.True(d >= f.S.NaustWidthM + 2f, $"two naust only {d:F1} m apart");
                    Assert.True(d <= f.S.RowSpacingM * 3.5f || d >= f.S.GroupSpacingM * 0.9f, $"naust {d:F0} m apart: neither one row nor two groups");
                }
            }
        }

        [Fact]
        public void TheQuay_RunsFromTheWaterlineIntoWaterDeepEnough()
        {
            var f = Shared.Value;
            var quay = f.Props.Single(p => p.Kind == ShorePropKind.Quay);
            Assert.InRange(f.H(quay.X, quay.Z), -1.5f, 0.31f);
            Assert.True(f.H(quay.X - quay.FacingX * 5f, quay.Z - quay.FacingZ * 5f) > 0f, "the quay's root is not on land");
            float head = -f.H(quay.X + quay.FacingX * f.S.QuayLengthM, quay.Z + quay.FacingZ * f.S.QuayLengthM);
            Assert.True(head >= f.S.QuayMinDepthM, $"only {head:F1} m of water at the quay head");
            var nearest = f.Props.Where(p => p.Kind == ShorePropKind.Naust)
                .Min(p => MathF.Sqrt((p.X - quay.X) * (p.X - quay.X) + (p.Z - quay.Z) * (p.Z - quay.Z)));
            Assert.True(nearest < 60f, $"the quay is {nearest:F0} m from the nearest naust");
        }

        [Fact]
        public void NoTreeStandsInAYard()
        {
            var f = Shared.Value;
            var clearings = ShorePropPlacer.Clearings(f.Props);
            Assert.All(f.Trees, t => Assert.False(VegetationScatter.InClearing(clearings, t.X, t.Z), $"tree at ({t.X:F0}, {t.Z:F0}) in a yard"));
            foreach (var p in f.Props)
                Assert.DoesNotContain(f.Trees, t => (t.X - p.X) * (t.X - p.X) + (t.Z - p.Z) * (t.Z - p.Z) < 100f);
        }

        [Fact]
        public void Placement_IsDeterministic()
        {
            var f = Shared.Value;
            var again = ShorePropPlacer.Place(f.Heights, f.N, f.World, f.ShoreDistance, f.Masks.LandExposure, null, f.Cleared);
            Assert.Equal(f.Props, again);
        }

        /// <summary>
        /// F2-19: no naust corner and no quay end stands on a land pad or its
        /// rim; with the clearings left out, the flat +3.6 m pads draw shore
        /// buildings (so the exclusion is what keeps them off).
        /// </summary>
        [Fact]
        public void NoShoreBuilding_StandsOnAPadOrItsRim()
        {
            var f = Shared.Value;
            Assert.NotEmpty(f.Cleared);
            foreach (var p in f.Props)
            {
                Assert.False(GroundRect.AnyContains(f.Cleared, p.X, p.Z), $"{p.Kind} at ({p.X:F0}, {p.Z:F0}) on a pad");
                float reach = p.Kind == ShorePropKind.Quay ? f.S.QuayLengthM : f.S.NaustLengthM * 0.5f;
                Assert.False(GroundRect.AnyContains(f.Cleared, p.X + p.FacingX * reach, p.Z + p.FacingZ * reach), $"{p.Kind} at ({p.X:F0}, {p.Z:F0}) reaches a pad");
                Assert.False(GroundRect.AnyContains(f.Cleared, p.X - p.FacingX * reach, p.Z - p.FacingZ * reach), $"{p.Kind} at ({p.X:F0}, {p.Z:F0}) reaches a pad");
            }
            var open = ShorePropPlacer.Place(f.Heights, f.N, f.World, f.ShoreDistance, f.Masks.LandExposure);
            Assert.Contains(open, p => GroundRect.AnyContains(f.Cleared, p.X, p.Z));
        }
    }
}
