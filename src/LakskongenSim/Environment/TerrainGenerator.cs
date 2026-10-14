using System.Threading.Tasks;

namespace LakskongenSim.Environment
{
    /// <summary>
    /// Shape of the procedural fjord, in world metres. The defaults describe a
    /// Vestland sill fjord at the scale of Lysefjorden or the Geirangerfjord:
    /// about a kilometre of water between walls that rise 500–600 m to the
    /// trough shoulders, a basin near 190 m deep, a sill at the mouth and a
    /// skerry strandflat outside it.
    /// </summary>
    public sealed record FjordTerrainSettings
    {
        public int Seed { get; init; } = 42;
        /// <summary>Edge length of the square world, centred on the origin.</summary>
        public float WorldSizeM { get; init; } = 4000f;
        /// <summary>Grid vertices per side (512 gives 7.8 m spacing over 4 km).</summary>
        public int Resolution { get; init; } = 512;
        /// <summary>Waterline half-width of the basin (varies ±15 % along the fjord).</summary>
        public float HalfWidthM { get; init; } = 430f;
        /// <summary>Basin floor depth below sea level.</summary>
        public float FloorDepthM { get; init; } = 190f;
        /// <summary>Height of the trough shoulders, where the walls break onto the upland.</summary>
        public float ShoulderHeightM { get; init; } = 620f;
        /// <summary>Horizontal run of a wall from the shoreline to its shoulder; sets the wall slope.</summary>
        public float WallRunM { get; init; } = 420f;
        /// <summary>Water depth over the sill at the fjord mouth.</summary>
        public float SillDepthM { get; init; } = 45f;
        /// <summary>World Z of the sill crest; the strandflat lies on the −Z side.</summary>
        public float MouthZ { get; init; } = -1300f;
        /// <summary>World Z of the fjord head, where the water ends in a delta.</summary>
        public float HeadZ { get; init; } = 1450f;
        /// <summary>Half-width of the dredge-free fairway kept open through the skerries.</summary>
        public float FairwayHalfWidthM { get; init; } = 140f;

        /// <summary>Grid spacing in metres.</summary>
        public float SpacingM => WorldSizeM / (Resolution - 1);
    }

    /// <summary>
    /// The fjord terrain: one deterministic height function in world metres,
    /// shared by the headless sim tools and the Godot renderer (through
    /// <c>TerrainGeneratorBridge</c>), so every caller sees the same heightfield.
    ///
    /// Cross-section (distance d from the channel centreline, half-width w):
    /// a U-trough floor h = −D·(1 − (d/w)⁴) below the waterline, then a wall
    /// that leaves the water at the trough's own slope and rounds over onto
    /// the shoulder (a monotone cubic Hermite), then a gently rising upland.
    /// Detail is OpenSimplex2 FBM (1/1200 m⁻¹, 6 octaves, lacunarity 2,
    /// gain 0.5) on a smooth domain warp (170 m at 1/2500 m⁻¹, no warp
    /// fractal), masked by elevation so the floor, beaches and skerries stay
    /// smooth; ridged noise builds the peaks behind the shoulders only.
    ///
    /// Arithmetic only (no sin/cos/pow), so heightfields are bit-identical
    /// across processes and platforms; <see cref="HeightfieldHash"/> pins that.
    /// </summary>
    public sealed class TerrainGenerator
    {
        // Seed offsets for the independent noise fields.
        private const int SeedWidth = 101, SeedDepth = 211, SeedShoulder = 307, SeedBay = 401;
        private const int SeedWarpX = 503, SeedWarpZ = 601, SeedDetail = 701, SeedRidge = 809;
        private const int SeedStrand = 907, SeedCentre = 1009, SeedWall = 1103, SeedCoast = 1201;
        private const int SeedGully = 1301, SeedLedge = 1409;

        // Detail and relief (metres, 1/metres).
        private const float DetailFrequency = 1f / 1200f;
        private const int DetailOctaves = 6;
        private const float DetailAmplitudeM = 60f;
        private const float WarpFrequency = 1f / 2500f;
        private const float WarpAmplitudeM = 170f;
        private const float RidgeFrequency = 1f / 2000f;
        private const int RidgeOctaves = 4;
        private const float RidgeGain = 0.4f;
        private const float RidgeSoftness = 0.15f;
        private const float RidgeAmplitudeM = 380f;
        private const float RidgeBias = 0.3f;
        // Buttresses and gullies on the walls.
        private const float WallDetailFrequency = 1f / 320f;
        private const int WallDetailOctaves = 3;
        private const float WallDetailAmplitudeM = 22f;
        // Gullies: narrow incisions running down the walls (anisotropic noise,
        // fine along the shore, coarse down the slope).
        private const float GullySpacingM = 110f;
        private const float GullyLengthM = 380f;
        private const float GullyDepthM = 18f;
        private const float GullySoftness = 0.12f;
        // Ledges: soft terraces where plants hold on between cliff bands.
        private const float LedgeStepM = 48f;
        private const float LedgeStrength = 0.28f;
        private const float DeltaHeightM = 2.5f;
        /// <summary>Half-width of the blend between the two walls' parameters.</summary>
        private const float SideBlendM = 150f;
        /// <summary>Depth of the lead kept open through the skerries.</summary>
        private const float FairwayDepthM = 32f;
        private const float BayAmplitudeM = 55f;
        private const float BayFrequency = 1f / 520f;
        /// <summary>Upland slope just behind the shoulder (rise per metre).</summary>
        private const float UplandSlope = 0.2f;
        /// <summary>Distance over which the upland rise saturates.</summary>
        private const float UplandSaturationM = 1600f;
        /// <summary>Steepest shoreline slope the trough may hand to the wall (62°).</summary>
        private const float MaxShoreSlope = 1.9f;

        // Centreline: a Catmull-Rom spline through control points 800 m apart.
        private const float CentreSpacingM = 800f;
        private const int CentreFirstIndex = -4;
        private static readonly float[] CentreBaseX = { 140f, 160f, 60f, -170f, -200f, -40f, 170f, 220f, 160f };
        private const float CentreJitterM = 70f;

        private readonly float[] _centreX;

        public FjordTerrainSettings Settings { get; }

        public TerrainGenerator(FjordTerrainSettings? settings = null)
        {
            Settings = settings ?? new FjordTerrainSettings();
            _centreX = new float[CentreBaseX.Length];
            for (int k = 0; k < _centreX.Length; k++)
                _centreX[k] = CentreBaseX[k] + CentreJitterM * HashToSigned(Settings.Seed + SeedCentre, k);
        }

        public TerrainGenerator(int seed) : this(new FjordTerrainSettings { Seed = seed }) { }

        // ── Heightfield ──────────────────────────────────────────────────────

        /// <summary>
        /// The Resolution² grid of heights in metres, row-major by Z
        /// (index = row · Resolution + column), vertex (c, r) at
        /// x = −W/2 + c·spacing, z = −W/2 + r·spacing. Rows are generated in
        /// parallel; each cell is independent, so the result does not depend
        /// on thread scheduling.
        /// </summary>
        public float[] GenerateHeightmap()
        {
            int n = Settings.Resolution;
            var heights = new float[n * n];
            Parallel.For(0, n, row => FillRow(heights, row));
            return heights;
        }

        /// <summary>Single-threaded twin of <see cref="GenerateHeightmap"/> (for tests).</summary>
        public float[] GenerateHeightmapSequential()
        {
            int n = Settings.Resolution;
            var heights = new float[n * n];
            for (int row = 0; row < n; row++)
                FillRow(heights, row);
            return heights;
        }

        private void FillRow(float[] heights, int row)
        {
            int n = Settings.Resolution;
            float half = Settings.WorldSizeM * 0.5f;
            float spacing = Settings.SpacingM;
            float z = -half + row * spacing;
            for (int col = 0; col < n; col++)
                heights[row * n + col] = HeightAt(-half + col * spacing, z);
        }

        /// <summary>World X of the channel centreline at world Z.</summary>
        public float CenterX(float z)
        {
            float t = z / CentreSpacingM - CentreFirstIndex;
            int last = _centreX.Length - 1;
            if (t <= 1f)
                return _centreX[1];
            if (t >= last - 1)
                return _centreX[last - 1];
            int k = (int)t;
            float f = t - k;
            float p0 = _centreX[k - 1], p1 = _centreX[k], p2 = _centreX[k + 1], p3 = _centreX[k + 2];
            float f2 = f * f, f3 = f2 * f;
            return 0.5f * (2f * p1 + (p2 - p0) * f + (2f * p0 - 5f * p1 + 4f * p2 - p3) * f2 + (3f * p1 - p0 - 3f * p2 + p3) * f3);
        }

        /// <summary>Terrain height in metres at world (x, z); sea level is 0.</summary>
        public float HeightAt(float x, float z)
        {
            var s = Sample(x, z);
            return s.Height;
        }

        /// <summary>
        /// True inside the steep wall band (from just below the shoreline to
        /// just past the shoulder), where slopes of 30–60° are the design.
        /// </summary>
        public bool IsWallBand(float x, float z)
        {
            var s = Sample(x, z);
            return s.WallU > -0.3f && s.WallU < 1.3f;
        }

        /// <summary>Base cross-section parameters at world Z, for profile checks.</summary>
        public (float halfWidth, float floorDepth, float shoulderHeight, float wallRun) ProfileAt(float z, int side)
        {
            float w = HalfWidth(z, side);
            float depth = FloorDepth(z);
            float shoulder = ShoulderHeight(z, side);
            return (w, depth, shoulder, Settings.WallRunM * shoulder / Settings.ShoulderHeightM);
        }

        private readonly struct Point
        {
            public readonly float Height;
            public readonly float WallU;
            public Point(float height, float wallU) { Height = height; WallU = wallU; }
        }

        private Point Sample(float x, float z)
        {
            var st = Settings;
            float cx = CenterX(Math.Min(z, st.HeadZ));
            float bay = BayAmplitudeM * OpenSimplex2.Noise2(st.Seed + SeedBay, x * BayFrequency, z * BayFrequency);
            float dx = x - cx + bay;
            // Each wall has its own width and shoulder; blend them across the
            // centreline, where beyond the head d is large and a hard switch
            // would cut a step into the closing wall.
            float right = SmoothStep(-SideBlendM, SideBlendM, dx);
            // Beyond the head the distance runs to the head point, so the walls
            // close round the end of the fjord.
            float dz = z > st.HeadZ ? z - st.HeadZ : 0f;
            float d = dz > 0f ? MathF.Sqrt(dx * dx + dz * dz) : MathF.Abs(dx);
            float zc = Math.Min(z, st.HeadZ);

            float w = Lerp(HalfWidth(zc, -1), HalfWidth(zc, 1), right);
            float depth = FloorDepth(z);
            float shoulder = Lerp(ShoulderHeight(zc, -1), ShoulderHeight(zc, 1), right);
            float run = st.WallRunM * shoulder / st.ShoulderHeightM;
            float u = (d - w) / run;

            float h;
            if (d < w)
            {
                float r = d / w;
                float r2 = r * r;
                h = -depth * (1f - r2 * r2);
            }
            else if (u <= 1f)
            {
                float shoreSlope = Math.Min(4f * depth / w, MaxShoreSlope);
                h = shoulder * HermiteWall(u, shoreSlope * run / shoulder, UplandSlope * run / shoulder);
            }
            else
            {
                float e = d - w - run;
                h = shoulder + UplandSlope * e / (1f + e / UplandSaturationM);
            }

            h = DeltaAtHead(h, d, w, z);
            h = BlendStrandflat(h, x, z, cx, out float coast);
            h = WallRelief(h, x, z, d, u, coast);
            h += Detail(x, z, h, u, Coastal(z));
            return new Point(h, u);
        }

        /// <summary>
        /// Rock relief on the walls: gullies cut down the slope and soft
        /// ledges between cliff bands, faded out at the waterline and the
        /// shoulder so the floor and the upland stay smooth.
        /// </summary>
        private float WallRelief(float h, float x, float z, float d, float u, float coast)
        {
            float mask = SmoothStep(-0.05f, 0.2f, u) * SmoothStep(1.15f, 0.85f, u) * SmoothStep(8f, 60f, h);
            if (mask <= 0f)
                return h;
            int seed = Settings.Seed;
            // Gullies run down walls that face across the fjord (the noise is
            // fine along Z): fade them where the walls turn to face along it,
            // round the head and on the headlands at the coast, where they
            // would cut grooves along the contours instead.
            float gullyMask = mask * (1f - coast) * SmoothStep(Settings.HeadZ + 60f, Settings.HeadZ - 160f, z);
            if (gullyMask > 0f)
            {
                float g = OpenSimplex2.Noise2(seed + SeedGully, z / GullySpacingM + x / 900f, d / GullyLengthM);
                // Rounded V: a hard |g| crease is sub-grid detail the mesh cannot carry.
                float incision = 1f - (MathF.Sqrt(g * g + GullySoftness * GullySoftness) - GullySoftness);
                incision *= incision;
                h -= gullyMask * GullyDepthM * incision;
            }
            float t = h / LedgeStepM + 0.6f * FractalNoise.Fbm(seed + SeedLedge, x / 300f, z / 300f, 2);
            float step = MathF.Floor(t);
            float terraced = step + SmoothStep(0.25f, 0.75f, t - step);
            return h + (terraced - t) * LedgeStepM * LedgeStrength * mask;
        }

        /// <summary>Monotone cubic Hermite from (0, 0) to (1, 1) with end slopes m0, m1.</summary>
        private static float HermiteWall(float u, float m0, float m1)
        {
            float u2 = u * u, u3 = u2 * u;
            return (u3 - 2f * u2 + u) * m0 + (3f * u2 - 2f * u3) + (u3 - u2) * m1;
        }

        private float HalfWidth(float z, int side)
        {
            var st = Settings;
            float n = OpenSimplex2.Noise2(st.Seed + SeedWidth + side, z / 900f, side * 3.7f);
            return st.HalfWidthM * (1f + 0.15f * n);
        }

        private float FloorDepth(float z)
        {
            var st = Settings;
            float depth = st.FloorDepthM * (1f + 0.12f * OpenSimplex2.Noise2(st.Seed + SeedDepth, z / 700f, 0.5f));
            // The sill: the basin shallows toward the mouth.
            depth += (st.SillDepthM - depth) * SmoothStep(st.MouthZ + 420f, st.MouthZ, z);
            // The head: the floor rises to the delta.
            depth *= SmoothStep(st.HeadZ + 40f, st.HeadZ - 420f, z);
            return depth;
        }

        private float ShoulderHeight(float z, int side)
        {
            var st = Settings;
            float n = OpenSimplex2.Noise2(st.Seed + SeedShoulder + side, z / 800f, 1.3f * side);
            return st.ShoulderHeightM * (1f + 0.12f * n) * (1f - 0.6f * Coastal(z));
        }

        /// <summary>0 in the inner fjord, rising to 1 at the mouth: the mountains lower toward the coast.</summary>
        private float Coastal(float z) => SmoothStep(Settings.MouthZ + 1100f, Settings.MouthZ - 100f, z);

        /// <summary>A flat delta plain a few metres above the sea at the fjord head.</summary>
        private float DeltaAtHead(float h, float d, float w, float z)
        {
            var st = Settings;
            float t = SmoothStep(st.HeadZ - 150f, st.HeadZ + 100f, z) * SmoothStep(w * 0.95f, w * 0.6f, d);
            return h + (DeltaHeightM - h) * t * (h < DeltaHeightM ? 1f : 0f);
        }

        /// <summary>
        /// Outside the sill the walls give way to the strandflat: a low rock
        /// platform with skerries, a fairway kept clear along the centreline,
        /// shelving into open sea toward the world edge.
        /// </summary>
        private float BlendStrandflat(float h, float x, float z, float cx, out float coast)
        {
            var st = Settings;
            // An irregular coastline: headlands and coves, not a straight cut.
            float coastZ = st.MouthZ - 40f + 120f * OpenSimplex2.Noise2(st.Seed + SeedCoast, x / 700f, 0.7f);
            coast = SmoothStep(coastZ + 160f, coastZ - 220f, z);
            if (coast <= 0f)
                return h;
            float strand = -7f + 22f * FractalNoise.Fbm(st.Seed + SeedStrand, x / 260f, z / 260f, 4);
            strand -= 26f * SmoothStep(st.MouthZ - 450f, st.MouthZ - 900f, z);
            float lane = MathF.Abs(x - cx);
            float fairway = -FairwayDepthM;
            strand += (Math.Min(strand, fairway) - strand) * SmoothStep(st.FairwayHalfWidthM + 80f, st.FairwayHalfWidthM, lane);
            return h + (strand - h) * coast;
        }

        private float Detail(float x, float z, float h, float u, float coastal)
        {
            float mask = SmoothStep(3f, 80f, h);
            if (mask <= 0f)
                return 0f;
            int seed = Settings.Seed;
            float wx = WarpAmplitudeM * OpenSimplex2.Noise2(seed + SeedWarpX, x * WarpFrequency, z * WarpFrequency);
            float wz = WarpAmplitudeM * OpenSimplex2.Noise2(seed + SeedWarpZ, x * WarpFrequency + 31.4f, z * WarpFrequency - 17.2f);
            float px = x + wx, pz = z + wz;
            float detail = DetailAmplitudeM * FractalNoise.Fbm(seed + SeedDetail, px * DetailFrequency, pz * DetailFrequency, DetailOctaves);
            float ridgeMask = SmoothStep(150f, 400f, h) * SmoothStep(0.7f, 1.5f, u) * (1f - 0.75f * coastal);
            float ridge = ridgeMask > 0f
                ? ridgeMask * RidgeAmplitudeM * (FractalNoise.Ridged(seed + SeedRidge, px * RidgeFrequency, pz * RidgeFrequency,
                    RidgeOctaves, 2f, RidgeGain, RidgeSoftness) - RidgeBias)
                : 0f;
            float wallMask = SmoothStep(-0.1f, 0.15f, u) * SmoothStep(1.3f, 1.0f, u);
            float wall = wallMask > 0f
                ? wallMask * WallDetailAmplitudeM * FractalNoise.Fbm(seed + SeedWall, px * WallDetailFrequency, pz * WallDetailFrequency, WallDetailOctaves)
                : 0f;
            return mask * (detail + wall) + ridge;
        }

        // ── Placement helpers ───────────────────────────────────────────────

        /// <summary>
        /// World Z range for pen slots: inside the sill and short of the head
        /// delta, where the basin is deep open water.
        /// </summary>
        public (float start, float end) PenSlotRange() => (Settings.MouthZ + 160f, Settings.HeadZ - 160f);

        /// <summary>
        /// Pen anchors along the channel: evenly spaced in Z over
        /// <see cref="PenSlotRange"/>, each at the centreline offset (within
        /// ±250 m) whose depth lies in 20–100 m and is deepest, else on the
        /// centreline.
        /// </summary>
        public List<Vector3> GetRecommendedLocations(int maxPens = 12)
        {
            var locations = new List<Vector3>();
            var (startZ, endZ) = PenSlotRange();
            float[] offsets = { -250f, -150f, -75f, 0f, 75f, 150f, 250f };
            for (int i = 0; i < maxPens; i++)
            {
                float progress = maxPens > 1 ? (float)i / (maxPens - 1) : 0.5f;
                float z = startZ + progress * (endZ - startZ);
                float cx = CenterX(z);
                var best = new Vector3(cx, 0f, z);
                float bestDepth = -1f;
                foreach (float off in offsets)
                {
                    float depth = -HeightAt(cx + off, z);
                    if (depth > 20f && depth < 100f && depth > bestDepth)
                    {
                        bestDepth = depth;
                        best = new Vector3(cx + off, 0f, z);
                    }
                }
                locations.Add(best);
            }
            return locations;
        }

        // ── Hash ─────────────────────────────────────────────────────────────

        /// <summary>FNV-1a 64 over the IEEE-754 bits of every height, as 16 hex digits.</summary>
        public static string HeightfieldHash(float[] heights)
        {
            ulong hash = 14695981039346656037UL;
            foreach (float h in heights)
            {
                uint bits = BitConverter.SingleToUInt32Bits(h);
                for (int b = 0; b < 4; b++)
                {
                    hash ^= (bits >> (8 * b)) & 0xFFu;
                    hash *= 1099511628211UL;
                }
            }
            return hash.ToString("x16");
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = (x - edge0) / (edge1 - edge0);
            t = t < 0f ? 0f : (t > 1f ? 1f : t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>Deterministic value in [−1, 1] for (seed, index).</summary>
        private static float HashToSigned(int seed, int index)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u ^ (uint)index * 0x85EBCA77u;
                h = (h ^ (h >> 15)) * 0xC2B2AE3Du;
                h ^= h >> 13;
                return (h & 0xFFFFu) / 32767.5f - 1f;
            }
        }
    }
}
