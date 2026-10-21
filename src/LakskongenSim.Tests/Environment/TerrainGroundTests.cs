using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LakskongenSim.Environment;
using Xunit;

namespace LakskongenSim.Tests.Environment
{
    /// <summary>
    /// wave4b int, gfx-polish-1 merged onto F2-19: the heightfield's grid, <see cref="TerrainGenerator.HeightAt"/>
    /// and the wall ledges come from one function (<c>TerrainGenerator.Ground</c>). gfx-polish-1's grid read the
    /// uncarved sample, so the merge lost F2-19's pads in every generated heightfield while placement still saw
    /// them; and the ledges of the uncarved walls would have drawn phantom terraces into the carved ground's macro
    /// (<see cref="TerrainMesh.Chunk.Macro"/>). The generator's own ledges were also untested: a sign flip passed
    /// every check (review finding 3).
    /// </summary>
    public class TerrainGroundTests
    {
        // TerrainGenerator.LedgeStrength.
        private const float LedgeStrength = 0.28f;

        private static TerrainGenerator Gen(int seed, int resolution) =>
            new(new FjordTerrainSettings { Seed = seed, Resolution = resolution });

        /// <summary>HeightAt at every vertex of the grid (rows in parallel).</summary>
        private static float[] HeightAtGrid(TerrainGenerator gen)
        {
            int n = gen.Settings.Resolution;
            float half = gen.Settings.WorldSizeM * 0.5f, sp = gen.Settings.SpacingM;
            var expected = new float[n * n];
            Parallel.For(0, n, r =>
            {
                for (int c = 0; c < n; c++)
                    expected[r * n + c] = gen.HeightAt(-half + c * sp, -half + r * sp);
            });
            return expected;
        }

        private static void AssertBitEqual(float[] expected, float[] actual, string what)
        {
            Assert.Equal(expected.Length, actual.Length);
            for (int i = 0; i < expected.Length; i++)
            {
                if (BitConverter.SingleToInt32Bits(expected[i]) != BitConverter.SingleToInt32Bits(actual[i]))
                    Assert.Fail($"{what}: vertex {i} is {actual[i]:R}, HeightAt gives {expected[i]:R}");
            }
        }

        /// <summary>The grid vertices (index, x, z) on any land pad.</summary>
        private static IEnumerable<(int index, float x, float z)> PadVertices(TerrainGenerator gen)
        {
            int n = gen.Settings.Resolution;
            float half = gen.Settings.WorldSizeM * 0.5f, sp = gen.Settings.SpacingM;
            var pads = gen.LandPadSlots();
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    float x = -half + c * sp, z = -half + r * sp;
                    if (pads.Any(p => p.Contains(x, z)))
                        yield return (r * n + c, x, z);
                }
            }
        }

        [Fact]
        public void EveryGridPath_IsHeightAt_BitForBit()
        {
            // The game's grid (seed 42, 512²) on both parallel paths; seeds 1-10 on all three.
            var game = Gen(42, 512);
            var expected = HeightAtGrid(game);
            AssertBitEqual(expected, game.GenerateHeightmap(), "seed 42 GenerateHeightmap()");
            AssertBitEqual(expected, game.GenerateHeightmap(out _), "seed 42 GenerateHeightmap(out WallLedges)");
            for (int seed = 1; seed <= 10; seed++)
            {
                var gen = Gen(seed, 129);
                expected = HeightAtGrid(gen);
                AssertBitEqual(expected, gen.GenerateHeightmap(), $"seed {seed} GenerateHeightmap()");
                AssertBitEqual(expected, gen.GenerateHeightmap(out _), $"seed {seed} GenerateHeightmap(out WallLedges)");
                AssertBitEqual(expected, gen.GenerateHeightmapSequential(), $"seed {seed} GenerateHeightmapSequential()");
            }
        }

        [Fact]
        public void ThePads_CarryNoTerraces_AndTheirGroundWithoutThemIsThePlanLevel()
        {
            // Seed 7 at 512² has 145 pad vertices whose uncarved wall carries a terrace (up to 2.3 m),
            // seed 42 (the game's) 11; seeds 1-10 at 129² more. The macro ground (heights minus
            // offsets) on a pad is the pad, with no ledge strength to spread its slope.
            int uncarvedLedges = 0, checkedVertices = 0;
            foreach (var (seed, resolution) in new[] { (7, 512), (42, 512) }.Concat(Enumerable.Range(1, 10).Select(s => (s, 129))))
            {
                var gen = Gen(seed, resolution);
                float[] heights = gen.GenerateHeightmap(out WallLedges ledges);
                foreach (var (i, x, z) in PadVertices(gen))
                {
                    Assert.Equal(TerrainGenerator.PadHeightM, heights[i]);
                    Assert.True(ledges.Offsets[i] == 0f, $"seed {seed}: a pad vertex at ({x}, {z}) carries a terrace offset {ledges.Offsets[i]}");
                    Assert.True(ledges.Strength[i] == 0f, $"seed {seed}: a pad vertex at ({x}, {z}) carries a ledge strength {ledges.Strength[i]}");
                    Assert.Equal(TerrainGenerator.PadHeightM, heights[i] - ledges.Offsets[i]);
                    if (gen.UncarvedAt(x, z).Ledge != 0f)
                        uncarvedLedges++;
                    checkedVertices++;
                }
            }
            Assert.True(checkedVertices > 5000, $"pad vertices checked: {checkedVertices}");
            Assert.True(uncarvedLedges > 150, $"pad vertices whose uncarved wall carries a terrace: {uncarvedLedges}");
        }

        [Fact]
        public void TheCarvesKeptShare_IsItsSlopeInTheHeightItWasGiven()
        {
            // CarvePads(h, x, z, out keep): keep is the share of a change in h the carve passes on
            // (1 uncarved, 0 on a pad and a shelf's fill, the cut face's or the drop's smoothstep).
            // Read by central differences round every pad's rim, away from the branch switches (the
            // pad level; a cut face wider than PadCutM from 75.6 m; a drop longer than its own, from
            // 3 - 3 x 9 = -24 m at a quay face; the shelf meeting the ground), where the rim's widths
            // do not move with h.
            var gen = Gen(42, 512);
            const float delta = 0.01f;
            int cut = 0, drop = 0, fill = 0, untouched = 0;
            foreach (var pad in gen.LandPadSlots())
            {
                for (int k = 0; k < 400; k++)
                {
                    // A deterministic spiral of points over the rim and a little beyond it.
                    float angle = k * 2.399963f;
                    float reach = (k % 50) / 50f * (TerrainGenerator.PadRimMaxM + 20f) + 0.5f;
                    float x = pad.CentreX + MathF.Cos(angle) * (pad.WidthM * 0.5f + reach);
                    float z = pad.CentreZ + MathF.Sin(angle) * (pad.DepthM * 0.5f + reach);
                    foreach (float h in new[] { -20f, -12f, -6f, -1f, 1.5f, 6f, 15f, 40f, 70f })
                    {
                        float lo = gen.CarvePads(h - delta, x, z), hi = gen.CarvePads(h + delta, x, z);
                        float carved = gen.CarvePads(h, x, z, out float keep);
                        Assert.Equal(gen.CarvePads(h, x, z), carved);
                        float slope = (hi - lo) / (2f * delta);
                        // A switch inside [h - delta, h + delta] (the shelf meeting the ground) shows
                        // as a slope between its two sides: skip the rare point where the two halves
                        // of the difference disagree.
                        float left = (carved - lo) / delta, right = (hi - carved) / delta;
                        if (MathF.Abs(left - right) > 0.02f)
                            continue;
                        Assert.True(MathF.Abs(slope - keep) < 0.01f,
                            $"{pad.Id} ({x:F1}, {z:F1}) h {h}: the carve passes on {slope:F4} of a change, keep says {keep:F4}");
                        if (keep >= 1f)
                            untouched++;
                        else if (keep <= 0f)
                            fill++;
                        else if (h > TerrainGenerator.PadHeightM)
                            cut++;
                        else
                            drop++;
                    }
                }
            }
            Assert.True(cut > 100 && drop > 100 && fill > 100 && untouched > 100,
                $"cut faces {cut}, drops {drop}, fills {fill}, untouched {untouched}");
        }

        /// <summary>
        /// Total variation of the slope along the grid's rows (across the fjord, the walls'
        /// fall line) over the vertices whose ledges are at least half strength.
        /// </summary>
        private static float SlopeVariationAlongRows(float[] g, int n, float spacing, float[] strength, out int count)
        {
            float tv = 0f;
            count = 0;
            for (int r = 0; r < n; r++)
            {
                for (int c = 2; c < n - 2; c++)
                {
                    int i = r * n + c;
                    float min = 0.5f * LedgeStrength;
                    if (strength[i - 1] < min || strength[i] < min || strength[i + 1] < min)
                        continue;
                    float s0 = MathF.Abs(g[i] - g[i - 2]) / (2f * spacing);
                    float s1 = MathF.Abs(g[i + 1] - g[i - 1]) / (2f * spacing);
                    tv += MathF.Abs(s1 - s0);
                    count++;
                }
            }
            return tv;
        }

        [Theory]
        [InlineData(42)]
        [InlineData(1)]
        [InlineData(7)]
        [InlineData(13)]
        public void TheGeneratorsLedges_TakeTheTerracesOutOfTheWalls(int seed)
        {
            // The heights minus the generator's offsets are the walls without their terraces: down
            // the fall line their slope swings far less than the terraced heights' (measured
            // 0.38-0.43 of it on these seeds). Offsets of the wrong sign double the terraces
            // (1.83-1.87), half the offsets leave most of them (0.60-0.63), none leave all (1.0).
            var gen = Gen(seed, 512);
            float[] heights = gen.GenerateHeightmap(out WallLedges ledges);
            int n = gen.Settings.Resolution;
            var ground = heights.Select((h, i) => h - ledges.Offsets[i]).ToArray();
            float terraced = SlopeVariationAlongRows(heights, n, gen.Settings.SpacingM, ledges.Strength, out int count);
            float without = SlopeVariationAlongRows(ground, n, gen.Settings.SpacingM, ledges.Strength, out _);
            Assert.True(count > 20000, $"seed {seed}: wall vertices {count}");
            Assert.True(without < 0.5f * terraced,
                $"seed {seed}: the slope's variation down the walls is {without / terraced:F3} of the terraced heights' without the ledges");
        }
    }
}
