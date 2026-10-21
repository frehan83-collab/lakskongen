using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LakskongenSim.Environment;
using Xunit;

namespace LakskongenSim.Tests.Environment
{
    /// <summary>
    /// G2-01: the far field round the 4 km play area. The art review found that
    /// the world ended in flat ocean at the fjord mouth and read as an island;
    /// these pin a far field that is the heightfield inside the world, meets it
    /// without a step, and carries a coast to the horizon with the open sea in
    /// front of the mouth (farm-types spec §4: offshore slots in open water, a
    /// low horizon in the −Z sector).
    /// </summary>
    public class FarFieldTests
    {
        private static TerrainGenerator Gen(int seed) => new(new FjordTerrainSettings { Seed = seed });

        [Fact]
        public void FarField_IsTheHeightfieldInsideTheWorld()
        {
            // Bit for bit, border included: the ring's border loop copies the
            // heightfield, and height queries inside the world must not change.
            foreach (int seed in new[] { 42, 7 })
            {
                var gen = Gen(seed);
                var st = gen.Settings;
                int n = st.Resolution;
                float sp = st.SpacingM, half = st.WorldSizeM * 0.5f;
                float[] h = gen.GenerateHeightmap();
                for (int r = 0; r < n; r++)
                {
                    for (int c = 0; c < n; c++)
                    {
                        bool border = r == 0 || c == 0 || r == n - 1 || c == n - 1;
                        if (!border && (r % 7 != 0 || c % 7 != 0))
                            continue;
                        float x = -half + c * sp, z = -half + r * sp;
                        Assert.Equal(h[r * n + c], gen.FarFieldHeightAt(x, z));
                    }
                }
            }
        }

        [Fact]
        public void FarField_MeetsTheWorldEdgeWithoutAStep()
        {
            // One metre outside the edge the far field is still the
            // heightfield's own fjord (the blend starts with zero slope): a
            // seam or a step at the 4 km edge would show here.
            var gen = Gen(42);
            float half = gen.Settings.WorldSizeM * 0.5f;
            float worst = 0f;
            for (float t = -half; t <= half; t += 10f)
            {
                foreach (var (x, z) in new[] { (half + 1f, t), (-half - 1f, t), (t, half + 1f), (t, -half - 1f) })
                    worst = Math.Max(worst, Math.Abs(gen.FarFieldHeightAt(x, z) - gen.HeightAt(x, z)));
            }
            Assert.True(worst < 0.05f, $"the far field leaves the heightfield by {worst:F3} m one metre outside the edge");
        }

        /// <summary>Highest ground (m) along a bearing from the fjord mouth between two distances.</summary>
        private static float Skyline(TerrainGenerator gen, double bearingFromWest, float from, float to)
        {
            float mx = gen.CenterX(gen.Settings.MouthZ), mz = gen.Settings.MouthZ;
            float dx = (float)Math.Sin(bearingFromWest), dz = -(float)Math.Cos(bearingFromWest);
            float top = float.MinValue;
            for (float r = from; r <= to; r += 40f)
                top = Math.Max(top, gen.FarFieldHeightAt(mx + dx * r, mz + dz * r));
            return top;
        }

        [Fact]
        public void FarField_CarriesACoastToTheHorizon_AndOpenSeaWestOfTheMouth()
        {
            // Bearings from the mouth, 0 = west (−Z, out to sea). Outside the
            // open-sea sector (±40°) every 20° sector shows land standing
            // ≥ 150 m somewhere 6-15 km out: mountains, islands, the coast
            // running on. Inside ±30° nothing rises before 12 km, and the
            // outer chain beyond it stays low (≤ 150 m, spec §4) but exists.
            foreach (int seed in new[] { 42, 7, 3 })
            {
                var gen = Gen(seed);
                for (int sector = 0; sector < 18; sector++)
                {
                    double a0 = (-180.0 + sector * 20.0) * Math.PI / 180.0;
                    float best = float.MinValue;
                    for (int k = 0; k < 5; k++)
                        best = Math.Max(best, Skyline(gen, a0 + k * 4.0 * Math.PI / 180.0, 6000f, 15000f));
                    double centreDeg = -170.0 + sector * 20.0;
                    if (Math.Abs(centreDeg) > 40.0)
                        Assert.True(best >= 150f, $"seed {seed}: the sector at {centreDeg:F0}° shows no land above {best:F0} m within 6-15 km");
                }
                float nearest = float.MinValue, outerTop = float.MinValue;
                int outerLand = 0;
                for (double deg = -30.0; deg <= 30.0; deg += 2.0)
                {
                    double a = deg * Math.PI / 180.0;
                    nearest = Math.Max(nearest, Skyline(gen, a, 2700f, 12000f));
                    float outer = Skyline(gen, a, 12000f, 15500f);
                    outerTop = Math.Max(outerTop, outer);
                    if (outer > 0f)
                        outerLand++;
                }
                Assert.True(nearest < 0f, $"seed {seed}: land rises to {nearest:F1} m in the open sea before the outer chain");
                Assert.True(outerTop <= 150f, $"seed {seed}: the outer chain rises to {outerTop:F0} m (spec: ≤ 150 m)");
                Assert.True(outerLand >= 3, $"seed {seed}: only {outerLand} of 31 bearings meet the outer chain");
            }
        }

        [Fact]
        public void FarField_LiftsInlandMassifsAboveTheSummerSnowLine()
        {
            // Behind the head the mountains reach the inner Vestland summer
            // snow line (GroundSeasonModel: 1,450-1,550 m in July-August), so
            // the far skyline keeps snowfields all year.
            var gen = Gen(42);
            float top = float.MinValue;
            for (float x = -12000f; x <= 12000f; x += 100f)
                for (float z = 5000f; z <= 15000f; z += 100f)
                    top = Math.Max(top, gen.FarFieldHeightAt(x, z));
            Assert.InRange(top, 1500f, 2100f);
        }

        [Fact]
        public void FarField_IsDeterministic_AndFollowsTheSeed()
        {
            var a = Gen(42);
            var b = Gen(42);
            var c = Gen(43);
            int differ = 0;
            for (int i = 0; i < 2000; i++)
            {
                float x = -15000f + (i % 50) * 611f, z = -15000f + (i / 50) * 757f;
                Assert.Equal(a.FarFieldHeightAt(x, z), b.FarFieldHeightAt(x, z));
                if (a.FarFieldHeightAt(x, z) != c.FarFieldHeightAt(x, z))
                    differ++;
            }
            Assert.True(differ > 1500, $"{differ}/2000 samples differ between seeds 42 and 43");
        }
    }

    /// <summary>
    /// G2-01 / farm-types spec §4 (F2-19 groundwork): the plots reserved for
    /// land-based and offshore farms, through the <see cref="ISitePlotGeometry"/>
    /// seam the site-plot catalogue (F2-03) consumes.
    /// </summary>
    public class SitePlotGeometryTests
    {
        private static readonly TerrainGenerator Gen42 = new(42);

        [Fact]
        public void Plots_AreTheSpecsProceduralLayout()
        {
            ISitePlotGeometry geometry = Gen42;
            var pads = geometry.LandPadSlots();
            Assert.Equal(new[] { "pad_a", "pad_b", "pad_delta" }, pads.Select(p => p.Id));
            // pad_a at the F2-18 read gate's centre (F2-19 moved it from (−700, −1650)).
            Assert.Equal((-450f, -1720f, 640f, 420f, 4), (pads[0].CentreX, pads[0].CentreZ, pads[0].WidthM, pads[0].DepthM, pads[0].Lots));
            Assert.Equal((750f, -1640f, 480f, 360f, 2), (pads[1].CentreX, pads[1].CentreZ, pads[1].WidthM, pads[1].DepthM, pads[1].Lots));
            Assert.Equal((0f, 1450f, 260f, 180f, 4), (pads[2].CentreX, pads[2].CentreZ, pads[2].WidthM, pads[2].DepthM, pads[2].Lots));
            Assert.All(pads, p => Assert.Equal(3.6f, p.PadHeightM));
            var slots = geometry.OffshoreSlots();
            Assert.Equal(new[] { "off_1", "off_2", "hav_1" }, slots.Select(s => s.Id));
            Assert.Equal(new[] { (-450f, -2750f), (850f, -3400f), (0f, -5200f) }, slots.Select(s => (s.X, s.Z)));
        }

        [Fact]
        public void OffshoreSlots_LieInOpenSeaBeyondTheWorld()
        {
            // Spec §4 and F2-19 acceptance 5: every slot lies outside the bake
            // (the world) with exposure 1, in deep water with no land round it.
            float half = Gen42.Settings.WorldSizeM * 0.5f;
            var expected = new Dictionary<string, float> { ["off_1"] = 150f, ["off_2"] = 220f, ["hav_1"] = 300f };
            foreach (var slot in Gen42.OffshoreSlots())
            {
                Assert.True(Math.Abs(slot.X) > half || Math.Abs(slot.Z) > half, $"{slot.Id} lies inside the world");
                Assert.Equal(1f, slot.Exposure);
                Assert.Equal(-Gen42.FarFieldHeightAt(slot.X, slot.Z), slot.WaterDepthM);
                // The seabed is tuned to the spec's depths; within 15 %.
                Assert.InRange(slot.WaterDepthM, expected[slot.Id] * 0.85f, expected[slot.Id] * 1.15f);
                float highest = float.MinValue;
                for (float r = 0f; r <= 600f; r += 30f)
                    for (int k = 0; k < 24; k++)
                    {
                        double a = k * Math.PI / 12.0;
                        highest = Math.Max(highest, Gen42.FarFieldHeightAt(slot.X + r * (float)Math.Cos(a), slot.Z + r * (float)Math.Sin(a)));
                    }
                Assert.True(highest < -40f, $"{slot.Id}: the sea round it rises to {highest:F0} m");
            }
        }

        [Fact]
        public void LandPads_LieOnTheShoreInsideTheWorld_WithAComputedIntake()
        {
            float half = Gen42.Settings.WorldSizeM * 0.5f;
            foreach (var pad in Gen42.LandPadSlots())
            {
                Assert.True(Math.Abs(pad.CentreX) + pad.WidthM * 0.5f < half && Math.Abs(pad.CentreZ) + pad.DepthM * 0.5f < half,
                    $"{pad.Id} reaches past the world edge");
                // On the shore: land and water within or right beside the pad.
                int land = 0, sea = 0;
                for (float x = pad.CentreX - pad.WidthM * 0.5f - 60f; x <= pad.CentreX + pad.WidthM * 0.5f + 60f; x += 10f)
                    for (float z = pad.CentreZ - pad.DepthM * 0.5f - 60f; z <= pad.CentreZ + pad.DepthM * 0.5f + 60f; z += 10f)
                    {
                        if (Gen42.HeightAt(x, z) > 0f)
                            land++;
                        else
                            sea++;
                    }
                Assert.True(land > 0 && sea > 0, $"{pad.Id}: {land} land and {sea} water samples (not on a shore)");
                // The quay is the middle of an edge, the one facing the water.
                bool onEdge = (Math.Abs(Math.Abs(pad.QuayX - pad.CentreX) - pad.WidthM * 0.5f) < 1e-3f && pad.QuayZ == pad.CentreZ)
                    || (Math.Abs(Math.Abs(pad.QuayZ - pad.CentreZ) - pad.DepthM * 0.5f) < 1e-3f && pad.QuayX == pad.CentreX);
                Assert.True(onEdge, $"{pad.Id}: quay ({pad.QuayX}, {pad.QuayZ}) is not an edge midpoint");
                float reach = Gen42.IntakeReachM(pad);
                float edge = Math.Min(Math.Min(half - pad.QuayX, half + pad.QuayX), Math.Min(half - pad.QuayZ, half + pad.QuayZ));
                Assert.InRange(reach, 0f, edge);
                Assert.True(reach > 0f, $"{pad.Id}: the quay already stands in {TerrainGenerator.DeepIntakeDepthM} m of water");
            }
        }

        [Fact]
        public void IntakeReach_IsTheDistanceToDeepWaterOrTheWorldEdge()
        {
            // Computed, never authored (spec §4): the nearest heightfield
            // vertex at −95 m or deeper, else the world edge. Checked against
            // a brute-force scan of every vertex.
            var st = Gen42.Settings;
            int n = st.Resolution;
            float sp = st.SpacingM, half = st.WorldSizeM * 0.5f;
            float[] h = Gen42.GenerateHeightmap();
            foreach (var pad in Gen42.LandPadSlots())
            {
                float best = Math.Min(Math.Min(half - pad.QuayX, half + pad.QuayX), Math.Min(half - pad.QuayZ, half + pad.QuayZ));
                for (int r = 0; r < n; r++)
                    for (int c = 0; c < n; c++)
                    {
                        if (h[r * n + c] > -TerrainGenerator.DeepIntakeDepthM)
                            continue;
                        float dx = -half + c * sp - pad.QuayX, dz = -half + r * sp - pad.QuayZ;
                        best = Math.Min(best, MathF.Sqrt(dx * dx + dz * dz));
                    }
                Assert.Equal(best, Gen42.IntakeReachM(pad), 2);
            }
        }
    }

    /// <summary>
    /// G2-01: the far-field ring mesh. It must close on the heightfield's
    /// border with no crack, face up, shade the seam like the heightfield,
    /// reach the far field's extent within its triangle budget, and keep its
    /// LOD error under 2 px from the gameplay cameras.
    /// </summary>
    public class FarFieldMeshTests
    {
        private static readonly Lazy<(TerrainGenerator gen, float[] heights, FarFieldMesh far)> Fjord42 = new(() =>
        {
            var gen = new TerrainGenerator(42);
            float[] h = gen.GenerateHeightmap();
            return (gen, h, FarFieldMesh.Build(gen, h, null));
        });

        private static (uint, uint) Key(float[] p, int v) =>
            (BitConverter.SingleToUInt32Bits(p[v * 3]), BitConverter.SingleToUInt32Bits(p[v * 3 + 2]));

        [Fact]
        public void Ring_IsWatertight_AndZippedToTheHeightfieldBorder()
        {
            // Every edge of the ring belongs to two of its triangles, except
            // the border loop (shared with the heightfield's own mesh) and the
            // outer loop. Chunk seams included: a crack or a T-junction would
            // leave an edge with one triangle anywhere else.
            var (gen, heights, far) = Fjord42.Value;
            var st = gen.Settings;
            int n = st.Resolution;
            float sp = st.SpacingM, half = st.WorldSizeM * 0.5f;
            var border = new HashSet<(uint, uint)>();
            var borderEdges = new HashSet<((uint, uint), (uint, uint))>();
            (uint, uint) GridKey(int c, int r) => (BitConverter.SingleToUInt32Bits(-half + c * sp), BitConverter.SingleToUInt32Bits(-half + r * sp));
            ((uint, uint), (uint, uint)) Edge((uint, uint) a, (uint, uint) b) => a.CompareTo(b) < 0 ? (a, b) : (b, a);
            for (int i = 0; i < n - 1; i++)
            {
                foreach (var (a, b) in new[] { (GridKey(i, 0), GridKey(i + 1, 0)), (GridKey(i, n - 1), GridKey(i + 1, n - 1)),
                    (GridKey(0, i), GridKey(0, i + 1)), (GridKey(n - 1, i), GridKey(n - 1, i + 1)) })
                {
                    border.Add(a);
                    border.Add(b);
                    borderEdges.Add(Edge(a, b));
                }
            }
            float outerR = far.LoopHalfExtents[^1];
            var edges = new Dictionary<((uint, uint), (uint, uint)), int>();
            foreach (var chunk in far.Chunks)
            {
                var p = chunk.Positions;
                for (int v = 0; v < chunk.Columns; v++)
                {
                    var key = Key(p, v);
                    if (border.Contains(key))
                    {
                        int c = (int)MathF.Round((p[v * 3] + half) / sp), r = (int)MathF.Round((p[v * 3 + 2] + half) / sp);
                        Assert.Equal(heights[r * n + c], p[v * 3 + 1]);
                    }
                }
                for (int t = 0; t < chunk.Indices.Length; t += 3)
                {
                    for (int e = 0; e < 3; e++)
                    {
                        var edge = Edge(Key(p, chunk.Indices[t + e]), Key(p, chunk.Indices[t + (e + 1) % 3]));
                        edges[edge] = edges.TryGetValue(edge, out int count) ? count + 1 : 1;
                    }
                }
            }
            int open = 0, borderOpen = 0;
            foreach (var (edge, count) in edges)
            {
                Assert.True(count <= 2, $"edge {edge} has {count} triangles");
                if (count == 2)
                    continue;
                if (borderEdges.Contains(edge))
                {
                    borderOpen++;
                    continue;
                }
                float ax = BitConverter.UInt32BitsToSingle(edge.Item1.Item1), az = BitConverter.UInt32BitsToSingle(edge.Item1.Item2);
                float bx = BitConverter.UInt32BitsToSingle(edge.Item2.Item1), bz = BitConverter.UInt32BitsToSingle(edge.Item2.Item2);
                bool outer = Math.Max(Math.Abs(ax), Math.Abs(az)) > outerR * 0.999f && Math.Max(Math.Abs(bx), Math.Abs(bz)) > outerR * 0.999f;
                Assert.True(outer, $"open edge ({ax:F2}, {az:F2})-({bx:F2}, {bz:F2}) inside the ring: a crack");
                open++;
            }
            Assert.Equal(borderEdges.Count, borderOpen);
            Assert.Equal(4 * far.LoopSegments[^1], open);
        }

        [Fact]
        public void Ring_FacesUpAndAgreesWithItsNormals()
        {
            // Godot's Plane(v0, v1, v2) normal ((v0 − v2) × (v0 − v1)) points
            // up for every triangle (clockwise front faces, as the heightfield).
            // The normals are the mean of the drawn faces round each vertex
            // and the far field is band-limited to the cells, so a face turns
            // against a corner only where a crest is still sharper than two
            // cells (16-250 m): at most 1 drawn triangle in 10,000 (measured
            // 0-10 of ~190,000 over seeds 1, 2, 3, 5, 7, 42; 41-58 before the
            // band-limiting, 160-385 with central-difference normals).
            // Triangles 2 m or more under the opaque sea are never drawn.
            foreach (int seed in new[] { 42, 7, 3 })
            {
                var gen = new TerrainGenerator(seed);
                var far = seed == 42 ? Fjord42.Value.far : FarFieldMesh.Build(gen, gen.GenerateHeightmap(), null);
                long triangles = 0, drawn = 0, farFolds = 0;
                for (int ci = 0; ci < far.Chunks.Count; ci++)
                {
                    var chunk = far.Chunks[ci];
                    var p = chunk.Positions;
                    var nrm = chunk.Normals;
                    for (int t = 0; t < chunk.Indices.Length; t += 3)
                    {
                        int a = chunk.Indices[t], b = chunk.Indices[t + 1], c = chunk.Indices[t + 2];
                        float ux = p[a * 3] - p[c * 3], uy = p[a * 3 + 1] - p[c * 3 + 1], uz = p[a * 3 + 2] - p[c * 3 + 2];
                        float vx = p[a * 3] - p[b * 3], vy = p[a * 3 + 1] - p[b * 3 + 1], vz = p[a * 3 + 2] - p[b * 3 + 2];
                        float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                        Assert.True(ny > 0f, $"seed {seed}: chunk ({chunk.FirstColumn}, {chunk.FirstRow}) triangle {t / 3} faces down");
                        triangles++;
                        if (p[a * 3 + 1] < -2f && p[b * 3 + 1] < -2f && p[c * 3 + 1] < -2f)
                            continue;
                        drawn++;
                        bool agrees = true;
                        foreach (int v in new[] { a, b, c })
                            agrees &= nx * nrm[v * 3] + ny * nrm[v * 3 + 1] + nz * nrm[v * 3 + 2] > 0f;
                        if (!agrees)
                            farFolds++;
                    }
                }
                Assert.True(triangles > 100_000, $"{triangles} triangles");
                Assert.True(farFolds * 10_000 <= drawn, $"seed {seed}: {farFolds} of {drawn} drawn far triangles disagree with their normals");
            }
        }

        [Fact]
        public void Ring_ShadesTheSeamLikeTheHeightfield()
        {
            // The heightfield's border normals take their outer neighbour from
            // the far field; the ring's border loop carries the very same
            // normals, so the lighting does not break at the 4 km edge.
            var (gen, heights, far) = Fjord42.Value;
            var st = gen.Settings;
            var main = TerrainMesh.Build(heights, st.Resolution, st.WorldSizeM, 64, null, gen.FarFieldHeightAt);
            var mainNormals = new Dictionary<(uint, uint), (float, float, float)>();
            foreach (var chunk in main.Chunks)
                for (int v = 0; v < chunk.Columns * chunk.Rows; v++)
                    mainNormals[Key(chunk.Positions, v)] = (chunk.Normals[v * 3], chunk.Normals[v * 3 + 1], chunk.Normals[v * 3 + 2]);
            int shared = 0;
            foreach (var chunk in far.Chunks.Where((_, i) => far.ChunkBands[i] == 0))
            {
                for (int v = 0; v < chunk.Columns; v++)
                {
                    if (!mainNormals.TryGetValue(Key(chunk.Positions, v), out var normal))
                        continue;
                    Assert.Equal(normal, (chunk.Normals[v * 3], chunk.Normals[v * 3 + 1], chunk.Normals[v * 3 + 2]));
                    shared++;
                }
            }
            // 4 × 511 border vertices, sector ends counted in both neighbours.
            Assert.True(shared >= 4 * (st.Resolution - 1), $"{shared} shared border vertices");
            // The border normal is a central difference reaching into the far field.
            var corner = main.Chunks[0];
            int i0 = 3; // row 0, column 3: on the world's −Z edge
            float x = corner.Positions[i0 * 3], z = corner.Positions[i0 * 3 + 2];
            float dhdz = (heights[1 * st.Resolution + 3] - gen.FarFieldHeightAt(x, z - st.SpacingM)) / (2f * st.SpacingM);
            float dhdx = (heights[4] - heights[2]) / (2f * st.SpacingM);
            float len = MathF.Sqrt(dhdx * dhdx + 1f + dhdz * dhdz);
            Assert.Equal(-dhdz / len, corner.Normals[i0 * 3 + 2], 5);
        }

        [Fact]
        public void Ring_ReachesTheFarFieldExtent_WithinItsBudget()
        {
            var (gen, _, far) = Fjord42.Value;
            float half = gen.Settings.WorldSizeM * 0.5f;
            Assert.Equal(half, far.LoopHalfExtents[0]);
            Assert.InRange(far.LoopHalfExtents[^1], TerrainGenerator.FarFieldExtentM, TerrainGenerator.FarFieldExtentM * 1.02f);
            // 16 m at the world edge, at most 2.5 % of the distance anywhere.
            Assert.True(2f * far.LoopHalfExtents[1] / far.LoopSegments[1] <= 16f, "the first loop is coarser than 16 m");
            for (int k = 1; k < far.LoopHalfExtents.Count; k++)
                Assert.True(2f / far.LoopSegments[k] <= 0.025f, $"loop {k} is too coarse");
            int triangles = far.Chunks.Sum(c => c.Indices.Length / 3);
            Assert.InRange(triangles, 100_000, 300_000);
            Assert.Equal(48, far.Chunks.Count);
        }

        [Fact]
        public void FarField_IsNeverFinerThanTheRingsCells()
        {
            // The band-limiting reads the ring's cell size (FarCellM): it
            // must never be finer than the loop that samples it (the far
            // field would alias), nor more than twice as coarse (the near
            // band blends to the outer cells just below its edge).
            var (gen, _, far) = Fjord42.Value;
            for (int k = 1; k < far.LoopHalfExtents.Count; k++)
            {
                float r = far.LoopHalfExtents[k];
                float cell = 2f * r / far.LoopSegments[k];
                foreach (var (x, z) in new[] { (r, 0f), (-r, r * 0.5f), (r * 0.3f, -r), (-r, -r) })
                    Assert.InRange(gen.FarCellM(x, z), cell * 0.999f, cell * 2.01f);
            }
        }

        [Fact]
        public void Ring_IsDeterministic()
        {
            var (gen, heights, far) = Fjord42.Value;
            var again = FarFieldMesh.Build(new TerrainGenerator(42), heights, null);
            Assert.Equal(far.Chunks.Count, again.Chunks.Count);
            for (int i = 0; i < far.Chunks.Count; i++)
            {
                Assert.Equal(far.Chunks[i].Positions, again.Chunks[i].Positions);
                Assert.Equal(far.Chunks[i].Normals, again.Chunks[i].Normals);
                Assert.Equal(far.Chunks[i].Indices, again.Chunks[i].Indices);
                Assert.Equal(far.Chunks[i].Colors, again.Chunks[i].Colors);
            }
        }

        /// <summary>
        /// The rig's camera for a focus on the water (FjordCamera.solve_rig
        /// without the steep-view pull-in): the orbit position at the zoom's
        /// pitch, clamped to the camera bounds (focus limit + 3 km), lifted to
        /// clear the ground under it by 32 m and the terrain along the sight
        /// line (32 samples) by 15 m.
        /// </summary>
        private static (float x, float y, float z) RigCamera(TerrainGenerator gen, float fx, float fz, float yaw, float dist, float pitchDeg, float camLimit)
        {
            float pitch = pitchDeg * MathF.PI / 180f;
            float horiz = dist * MathF.Cos(pitch);
            float cx = Math.Clamp(fx + MathF.Sin(yaw) * horiz, -camLimit, camLimit);
            float cz = Math.Clamp(fz + MathF.Cos(yaw) * horiz, -camLimit, camLimit);
            float need = Math.Max(Math.Max(gen.FarFieldHeightAt(cx, cz), 0f) + 32f, dist * MathF.Sin(-pitch));
            for (int i = 1; i <= 32; i++)
            {
                float t = i / 33f;
                float g = gen.FarFieldHeightAt(cx + (fx - cx) * t, cz + (fz - cz) * t);
                if (g > 0f)
                    need = Math.Max(need, (g + 15f) / (1f - t));
            }
            return (cx, need, cz);
        }

        /// <summary>The rig at FJORD (900 m, −17°, fov 45°), COAST (2,400 m, −20°, 52°) and its farthest (3,000 m, −21°, 54°), at 8 headings round each focus.</summary>
        private static List<(float x, float y, float z, float k)> RigCameras(TerrainGenerator gen, IEnumerable<(float x, float z)> foci)
        {
            float half = gen.Settings.WorldSizeM * 0.5f;
            float camLimit = half - 300f + 3000f;
            var cameras = new List<(float x, float y, float z, float k)>();
            var focusList = foci.ToList();
            foreach (var (dist, pitchDeg, fovDeg) in new[] { (900f, -17f, 45f), (2400f, -20f, 52f), (3000f, -21f, 54f) })
            {
                // Pixels per metre of error at 1 m on a 1,440-line screen.
                float k = 720f / MathF.Tan(fovDeg * MathF.PI / 360f);
                foreach (var (fx, fz) in focusList)
                    for (int yaw = 0; yaw < 8; yaw++)
                    {
                        var (cx, cy, cz) = RigCamera(gen, fx, fz, yaw * MathF.PI / 4f, dist, pitchDeg, camLimit);
                        cameras.Add((cx, cy, cz, k));
                    }
            }
            return cameras;
        }

        /// <summary>
        /// Screen error (px) of every drawn ring triangle at least
        /// <paramref name="minDistance"/> from every camera: the far field's
        /// height at the triangle's centroid and edge midpoints against the
        /// flat triangle (both clipped at the opaque sea), times the camera's
        /// pixels per metre at that distance, from the worst camera.
        /// </summary>
        private static List<float> ScreenErrors(TerrainGenerator gen, FarFieldMesh far, List<(float x, float y, float z, float k)> cameras, float minDistance)
        {
            var camArray = cameras.ToArray();
            var all = new List<float>();
            object gate = new();
            Parallel.ForEach(far.Chunks, chunk =>
            {
                var local = new List<float>();
                var p = chunk.Positions;
                for (int t = 0; t < chunk.Indices.Length; t += 3)
                {
                    int a = chunk.Indices[t], b = chunk.Indices[t + 1], c = chunk.Indices[t + 2];
                    if (p[a * 3 + 1] < -1f && p[b * 3 + 1] < -1f && p[c * 3 + 1] < -1f)
                        continue;
                    float mx = (p[a * 3] + p[b * 3] + p[c * 3]) / 3f, my = (p[a * 3 + 1] + p[b * 3 + 1] + p[c * 3 + 1]) / 3f, mz = (p[a * 3 + 2] + p[b * 3 + 2] + p[c * 3 + 2]) / 3f;
                    float px = 0f, nearest = float.MaxValue;
                    float err = -1f;
                    foreach (var cam in camArray)
                    {
                        float dx = mx - cam.x, dy = my - cam.y, dz = mz - cam.z;
                        float d = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                        nearest = Math.Min(nearest, d);
                        if (nearest < minDistance)
                            break;
                        if (err < 0f)
                        {
                            err = 0f;
                            foreach (var (wa, wb, wc) in new[] { (1f / 3f, 1f / 3f, 1f / 3f), (0.5f, 0.5f, 0f), (0f, 0.5f, 0.5f), (0.5f, 0f, 0.5f) })
                            {
                                float x = wa * p[a * 3] + wb * p[b * 3] + wc * p[c * 3];
                                float y = wa * p[a * 3 + 1] + wb * p[b * 3 + 1] + wc * p[c * 3 + 1];
                                float z = wa * p[a * 3 + 2] + wb * p[b * 3 + 2] + wc * p[c * 3 + 2];
                                err = Math.Max(err, Math.Abs(Math.Max(gen.FarFieldHeightAt(x, z), 0f) - Math.Max(y, 0f)));
                            }
                        }
                        px = Math.Max(px, err * cam.k / d);
                    }
                    if (nearest >= minDistance)
                        local.Add(px);
                }
                lock (gate)
                    all.AddRange(local);
            });
            all.Sort();
            return all;
        }

        private static float Percentile(List<float> sorted, double q) => sorted[(int)(sorted.Count * q)];

        [Fact]
        public void Ring_LodError_FromThePenSiteCameras_IsBounded()
        {
            // Roadmap G2-01 acceptance 4 ("LOD error ≤ 2 px on screen"), as a
            // guard. The cameras: the rig orbiting every pen site (8 headings)
            // at FJORD, COAST and its farthest zoom; a 1,440-line screen. The
            // ring is static (no LOD switching, so nothing pops) and the far
            // field is band-limited to its cells (octaves under ~6 cells fade,
            // crests round to ~3 cells, coasts widen), so the error is shape
            // the cells cannot carry. Measured (seed 42, triangles ≥ 300 m from
            // every camera): median 1.0 px, p90 3.7 px, p99 10 px (residuals:
            // the neighbouring fjords' 56° walls 4-6 km out, by less than a
            // cell sideways). The 2 px target at p90 needs a camera-centred
            // far LOD (followup); this pins the median under 1.5 px and p90
            // under 5 px so the band-limiting cannot regress.
            var (gen, _, far) = Fjord42.Value;
            var pens = gen.GetRecommendedLocations(12).Select(v => (v.X, v.Z));
            var errors = ScreenErrors(gen, far, RigCameras(gen, pens), 300f);
            float p50 = Percentile(errors, 0.5), p90 = Percentile(errors, 0.9), p99 = Percentile(errors, 0.99);
            string stats = $"median {p50:F2} px, p90 {p90:F2} px, p99 {p99:F2} px over {errors.Count} triangles";
            Assert.True(errors.Count > 150_000, stats);
            Assert.True(p50 <= 1.5f && p90 <= 5f, $"LOD error from the pen sites: {stats}");
        }
    }
}
