using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using Xunit;

namespace LakskongenSim.Tests.Environment
{
    /// <summary>
    /// F2-19 (docs/FARM_TYPES_SPEC.md §4, §7.2): the land pads are carved into
    /// the procedural fjord at the plot catalogue's positions, flat at the
    /// Indre Harøy plan level, with nothing growing or built on them; the
    /// offshore slots lie in the open sea beyond the water bake.
    /// </summary>
    public class TerrainPadTests
    {
        private static readonly Lazy<(TerrainGenerator gen, float[] heights, FjordGroundCover cover)> Fjord42 = new(() =>
        {
            var gen = new TerrainGenerator(42);
            var heights = gen.GenerateHeightmap();
            var st = gen.Settings;
            var water = WaterBake.Build(heights, st.Resolution, st.WorldSizeM, 0f, st.PrevailingSeaDirX, st.PrevailingSeaDirZ);
            return (gen, heights, FjordGroundCover.Build(gen, heights, water));
        });

        /// <summary>The heightfield vertices inside a pad's mask (its rectangle).</summary>
        private static IEnumerable<(float x, float z, int index)> MaskVertices(TerrainGenerator gen, LandPadSlot pad)
        {
            var st = gen.Settings;
            float half = st.WorldSizeM * 0.5f, sp = st.SpacingM;
            int n = st.Resolution;
            int c0 = (int)MathF.Ceiling((pad.CentreX - pad.WidthM * 0.5f + half) / sp);
            int c1 = (int)MathF.Floor((pad.CentreX + pad.WidthM * 0.5f + half) / sp);
            int r0 = (int)MathF.Ceiling((pad.CentreZ - pad.DepthM * 0.5f + half) / sp);
            int r1 = (int)MathF.Floor((pad.CentreZ + pad.DepthM * 0.5f + half) / sp);
            for (int r = Math.Max(r0, 0); r <= Math.Min(r1, n - 1); r++)
                for (int c = Math.Max(c0, 0); c <= Math.Min(c1, n - 1); c++)
                {
                    float x = -half + c * sp, z = -half + r * sp;
                    if (pad.Contains(x, z))
                        yield return (x, z, r * n + c);
                }
        }

        /// <summary>Acceptance 1: seeds 1-10, pad height 3.6 ± 0.3 m over ≥ 95% of each pad mask (the heightfield's own vertices).</summary>
        [Fact]
        public void EveryPad_IsFlatAtThePlanLevel_OnSeedsOneToTen()
        {
            for (int seed = 1; seed <= 10; seed++)
            {
                var gen = new TerrainGenerator(seed);
                foreach (var pad in gen.LandPadSlots())
                {
                    var heights = MaskVertices(gen, pad).Select(v => gen.HeightAt(v.x, v.z)).ToList();
                    Assert.True(heights.Count > 300, $"seed {seed} {pad.Id}: only {heights.Count} vertices in the mask");
                    int level = heights.Count(h => Math.Abs(h - TerrainGenerator.PadHeightM) <= 0.3f);
                    Assert.True(level >= 0.95 * heights.Count,
                        $"seed {seed} {pad.Id}: {level}/{heights.Count} vertices at 3.6 ± 0.3 m (min {heights.Min():F1}, max {heights.Max():F1})");
                }
            }
            // The game's own heightfield (seed 42, the grid the renderer builds) holds the same.
            var (gen42, grid, _) = Fjord42.Value;
            foreach (var pad in gen42.LandPadSlots())
                Assert.All(MaskVertices(gen42, pad), v => Assert.Equal(TerrainGenerator.PadHeightM, grid[v.index]));
        }

        /// <summary>
        /// The rim (spec §4, as F2-19 shaped it): the pad level holds on the
        /// pad's edge; into higher ground a cut face eases into it over
        /// <see cref="TerrainGenerator.PadCutM"/>; toward the sea a low rock
        /// shelf falls from the pad level to its lip and then eases into the
        /// seabed over the drop, never cutting below the natural ground; along
        /// the quay face the shelf is the quay's short lip and wall; nothing is
        /// carved past <see cref="TerrainGenerator.PadRimMaxM"/>, and the shelf
        /// fades out before the world's edge (the far field's seam).
        /// </summary>
        [Fact]
        public void ThePadRim_IsACutFaceIntoLand_AShelfToTheSea_AndAWallAtTheQuay()
        {
            var gen = new TerrainGenerator(42);
            float level = TerrainGenerator.PadHeightM;
            static float Smooth(float a, float b, float v)
            {
                float t = Math.Clamp((v - a) / (b - a), 0f, 1f);
                return t * t * (3f - 2f * t);
            }
            var pads = gen.LandPadSlots().ToDictionary(p => p.Id);
            var padA = pads["pad_a"];
            float north = padA.CentreZ + padA.DepthM * 0.5f;   // a natural (non-quay) side, far from the world's edge
            float east = padA.CentreX + padA.WidthM * 0.5f;    // pad_a's quay face (the fairway edge)
            foreach (float ground in new[] { -60f, -12f, 1.5f, 3.6f, 20f, 95f })
            {
                Assert.Equal(level, gen.CarvePads(ground, padA.CentreX, north));
                Assert.Equal(ground, gen.CarvePads(ground, padA.CentreX, north + TerrainGenerator.PadRimMaxM));
                for (float beyond = 0.5f; beyond < TerrainGenerator.PadRimMaxM; beyond += 1f)
                {
                    float x = padA.CentreX - 100f, z = north + beyond;
                    float want;
                    if (ground >= level)
                    {
                        // A higher face is wider, by the rim's slope bound.
                        float cut = Math.Max(TerrainGenerator.PadCutM, (ground - level) / TerrainGenerator.PadRimSlope);
                        want = beyond < cut ? level + (ground - level) * Smooth(0f, cut, beyond) : ground;
                    }
                    else
                    {
                        var (shelf, lip, drop) = gen.PadRimAt(x, z);
                        // A deeper seabed takes a longer drop, by the rim's slope bound, up to the longest drop.
                        drop = Math.Min(Math.Max(drop, (lip - ground) / TerrainGenerator.PadRimSlope), TerrainGenerator.PadDropMaxM);
                        float top = beyond <= shelf ? level + (lip - level) * Smooth(0f, shelf, beyond)
                            : beyond < shelf + drop ? lip + (ground - lip) * Smooth(shelf, shelf + drop, beyond) : ground;
                        want = Math.Max(ground, top);
                    }
                    Assert.Equal(want, gen.CarvePads(ground, x, z), 0.002f);
                }
            }
            // The quay face: a short lip at the deck's height, then a wall to the seabed within PadQuayShelfM + PadQuayDropM.
            var (qShelf, qLip, qDrop) = gen.PadRimAt(east + 2f, padA.CentreZ);
            Assert.Equal((TerrainGenerator.PadQuayShelfM, TerrainGenerator.PadQuayLipM, TerrainGenerator.PadQuayDropM), (qShelf, qLip, qDrop));
            float wallFoot = TerrainGenerator.PadQuayShelfM
                + Math.Max(TerrainGenerator.PadQuayDropM, (TerrainGenerator.PadQuayLipM + 25f) / TerrainGenerator.PadRimSlope);
            Assert.Equal(-25f, gen.CarvePads(-25f, east + wallFoot + 0.1f, padA.CentreZ));
            Assert.True(gen.CarvePads(-25f, east + TerrainGenerator.PadQuayShelfM, padA.CentreZ) >= TerrainGenerator.PadQuayLipM - 0.01f);
            // In front of the quay line past the face's ends the frontage runs on straight (no bulge before the berth).
            Assert.Equal((TerrainGenerator.PadQuayShelfM, TerrainGenerator.PadQuayLipM, TerrainGenerator.PadQuayDropM),
                gen.PadRimAt(east + 5f, padA.CentreZ + padA.DepthM * 0.5f + 30f));
            // The shelf wanders round the natural sides: headlands and coves.
            var shelves = new List<float>();
            for (float t = 0f; t < 1f; t += 0.002f)
            {
                float px = padA.CentreX - padA.WidthM * 0.5f + padA.WidthM * 0.9f * t;
                shelves.Add(gen.PadRimAt(px, north + 10f).shelf);
            }
            Assert.All(shelves, s => Assert.InRange(s, TerrainGenerator.PadShelfMinM, TerrainGenerator.PadShelfMaxM));
            Assert.True(shelves.Max() - shelves.Min() > 30f, $"the shelf barely wanders: {shelves.Min():F0}-{shelves.Max():F0} m");
            // Before the world's edge (pad_a's south side lies 70 m inside it) the shelf fades out: no step at the far field's seam.
            float worldEdge = -gen.Settings.WorldSizeM * 0.5f;
            Assert.Equal(-50f, gen.CarvePads(-50f, padA.CentreX, worldEdge + 4f));
            Assert.Equal(-50f, gen.CarvePads(-50f, padA.CentreX, worldEdge));
        }

        /// <summary>
        /// The rims keep the generator's smoothness (TerrainGeneratorTests'
        /// sub-grid slope bound of 8): the steepest cut face, quay wall or
        /// shelf drop, sampled every 2 m across every pad's rim at 0.5 m.
        /// </summary>
        [Fact]
        public void ThePadRims_StayUnderTheGeneratorsSlopeBound()
        {
            var gen = new TerrainGenerator(42);
            float worst = 0f;
            string where = "";
            foreach (var pad in gen.LandPadSlots())
            {
                float x0 = pad.CentreX - pad.WidthM * 0.5f - TerrainGenerator.PadRimMaxM;
                float x1 = pad.CentreX + pad.WidthM * 0.5f + TerrainGenerator.PadRimMaxM;
                float z0 = pad.CentreZ - pad.DepthM * 0.5f - TerrainGenerator.PadRimMaxM;
                float z1 = pad.CentreZ + pad.DepthM * 0.5f + TerrainGenerator.PadRimMaxM;
                for (float x = x0; x <= x1; x += 2f)
                    for (float z = z0; z <= z1; z += 2f)
                    {
                        if (pad.Contains(x, z))
                            continue;
                        float h = gen.HeightAt(x, z);
                        float s = Math.Max(Math.Abs(gen.HeightAt(x + 0.5f, z) - h), Math.Abs(gen.HeightAt(x, z + 0.5f) - h)) / 0.5f;
                        if (s > worst)
                        {
                            worst = s;
                            where = $"{pad.Id} ({x:F0}, {z:F0})";
                        }
                    }
            }
            Assert.True(worst < 8f, $"steepest rim slope {worst:F1} at {where}");
        }

        /// <summary>
        /// Acceptance 3: the terrain's pads and slots stand where the plot
        /// catalogue (godot/data/site_plots.json, the procedural fallback)
        /// puts them, within 1 m, with the catalogue's sizes; the intake reach
        /// is computed from the carved heightfield, never authored.
        /// </summary>
        [Fact]
        public void PadsAndSlots_StandAtTheCataloguePositions_WithAComputedIntake()
        {
            var gen = new TerrainGenerator(42);
            var catalog = SitePlotCatalog.Default;
            var joined = catalog.WithGeometry(gen);
            Assert.Equal(catalog.LandPads.Select(p => p.Id), gen.LandPadSlots().Select(p => p.Id));
            Assert.Equal(catalog.OffshoreSlots.Select(p => p.Id), gen.OffshoreSlots().Select(p => p.Id));
            foreach (var pad in gen.LandPadSlots())
            {
                var file = catalog.Find(pad.Id)!;
                Assert.InRange(Math.Abs(file.WorldX - pad.CentreX), 0.0, 1.0);
                Assert.InRange(Math.Abs(file.WorldZ - pad.CentreZ), 0.0, 1.0);
                Assert.Equal((file.PadWidthM, file.PadDepthM), ((double)pad.WidthM, (double)pad.DepthM));
                Assert.Equal(0.0, file.DeepIntakeReachM); // never authored
                float reach = gen.IntakeReachM(pad);
                Assert.True(reach > 0f, pad.Id);
                Assert.Equal(reach, joined.Find(pad.Id)!.DeepIntakeReachM, 3);
            }
            foreach (var slot in gen.OffshoreSlots())
            {
                var file = catalog.Find(slot.Id)!;
                Assert.InRange(Math.Abs(file.WorldX - slot.X), 0.0, 1.0);
                Assert.InRange(Math.Abs(file.WorldZ - slot.Z), 0.0, 1.0);
            }
        }

        /// <summary>
        /// The quay stands on the edge that faces the water nearest the fjord's
        /// channel, where the boats come from: pad_a's on the fairway (east)
        /// edge (spec §4), pad_b's on its west edge toward the channel, the
        /// delta's on its seaward (south) edge.
        /// </summary>
        [Fact]
        public void TheQuay_FacesTheChannel_OnWater()
        {
            var gen = new TerrainGenerator(42);
            var pads = gen.LandPadSlots().ToDictionary(p => p.Id);
            Assert.Equal((pads["pad_a"].CentreX + 320f, pads["pad_a"].CentreZ), (pads["pad_a"].QuayX, pads["pad_a"].QuayZ));
            Assert.Equal((pads["pad_b"].CentreX - 240f, pads["pad_b"].CentreZ), (pads["pad_b"].QuayX, pads["pad_b"].QuayZ));
            Assert.Equal((pads["pad_delta"].CentreX, pads["pad_delta"].CentreZ - 90f), (pads["pad_delta"].QuayX, pads["pad_delta"].QuayZ));
            foreach (var pad in pads.Values)
            {
                float ox = pad.QuayX - pad.CentreX, oz = pad.QuayZ - pad.CentreZ;
                float len = MathF.Sqrt(ox * ox + oz * oz);
                Assert.True(gen.HeightAt(pad.QuayX + ox / len * 60f, pad.QuayZ + oz / len * 60f) < 0f, $"{pad.Id}: no water off the quay");
            }
        }

        /// <summary>
        /// Acceptance 4: zero trees inside the pad masks (and none on their
        /// rims), no wood or meadow painted there, and no shore building on
        /// them, in the ground cover the renderer builds.
        /// </summary>
        [Fact]
        public void NothingGrowsOrStands_OnAPadOrItsRim()
        {
            var (gen, _, cover) = Fjord42.Value;
            var pads = gen.LandPadSlots();
            Assert.Equal(pads.Count, cover.Cleared.Count);
            for (int i = 0; i < pads.Count; i++)
            {
                var r = cover.Cleared[i];
                Assert.Equal((pads[i].CentreX - pads[i].WidthM * 0.5f - TerrainGenerator.PadRimMaxM, pads[i].CentreZ + pads[i].DepthM * 0.5f + TerrainGenerator.PadRimMaxM),
                    (r.MinX, r.MaxZ));
            }
            Assert.True(cover.Trees.Count > 12000, $"{cover.Trees.Count} trees");
            Assert.DoesNotContain(cover.Trees, t => pads.Any(p => p.Contains(t.X, t.Z)));
            Assert.DoesNotContain(cover.Trees, t => GroundRect.AnyContains(cover.Cleared, t.X, t.Z));
            Assert.DoesNotContain(cover.Props, p => GroundRect.AnyContains(cover.Cleared, p.X, p.Z));
            foreach (var pad in pads)
                foreach (var v in MaskVertices(gen, pad))
                {
                    Assert.Equal(0f, cover.Masks.Forest[v.index]);
                    Assert.Equal(0f, cover.Masks.Meadow[v.index]);
                }
            // Without the clearings the flat +3.6 m pads would grow trees and draw nausts (the exclusion is what keeps them off).
            var st = gen.Settings;
            var (_, heights, _) = Fjord42.Value;
            var water = WaterBake.Build(heights, st.Resolution, st.WorldSizeM, 0f, st.PrevailingSeaDirX, st.PrevailingSeaDirZ);
            var exposure = Enumerable.Range(0, heights.Length).Select(i => water.Rgba[i * 4 + 2]).ToArray();
            var open = VegetationScatter.BuildMasks(heights, st.Resolution, st.WorldSizeM, cover.Settings, exposure);
            Assert.Contains(pads, p => MaskVertices(gen, p).Any(v => open.Meadow[v.index] > 0f || open.Forest[v.index] > 0f));
        }

        /// <summary>
        /// Acceptance 5: every offshore slot lies outside the water bake (the
        /// world square the bake covers) in open sea with exposure 1, its depth
        /// the far field's.
        /// </summary>
        [Fact]
        public void EveryOffshoreSlot_LiesOutsideTheBake_WithExposureOne()
        {
            var (gen, heights, _) = Fjord42.Value;
            var st = gen.Settings;
            var water = WaterBake.Build(heights, st.Resolution, st.WorldSizeM, 0f, st.PrevailingSeaDirX, st.PrevailingSeaDirZ);
            Assert.Equal(st.Resolution, water.Size);
            float half = st.WorldSizeM * 0.5f;
            foreach (var slot in gen.OffshoreSlots())
            {
                Assert.True(Math.Abs(slot.X) > half || Math.Abs(slot.Z) > half, $"{slot.Id} lies inside the bake");
                Assert.Equal(1f, slot.Exposure);
                Assert.Equal(-gen.FarFieldHeightAt(slot.X, slot.Z), slot.WaterDepthM);
                Assert.True(slot.WaterDepthM > 100f, $"{slot.Id}: {slot.WaterDepthM:F0} m");
            }
        }
    }
}
