namespace LakskongenSim.Environment
{
    /// <summary>
    /// G2-01 far field: the land and sea around the 4 km play area, out to
    /// <see cref="FarFieldExtentM"/>, so the fjord sits in a coast that runs to
    /// the horizon instead of ending as an island.
    ///
    /// <see cref="FarFieldHeightAt"/> is <see cref="HeightAt"/> inside the
    /// world square (bit for bit) and blends, over <see cref="FarBlendM"/>
    /// beyond its edge, into the region: the same generator read through a
    /// regional coast (headlands and bays shift the mainland shore along Z),
    /// with two neighbouring fjords cut into the plateau and the inland
    /// massifs lifted to 1,400-1,800 m (summer snowfields). Within
    /// <see cref="SeaBlendM"/> of the edge the sea floor falls to the open
    /// shelf (85 m at the edge to 320 m) and islands rise from it: large
    /// islands with a fringe of skerries along the mainland coast to the north
    /// and south, and a low outer chain (at most 150 m) no nearer than
    /// <see cref="OuterChainStartM"/> in the sector that opens west from the
    /// fjord mouth (farm-types spec §4: the offshore horizon stays low, as in
    /// the owner's photo O3). That sector keeps the offshore slots
    /// (<see cref="OffshoreSlots"/>) in open water.
    ///
    /// Arithmetic only, like the rest of the generator (no sin/cos/pow).
    /// </summary>
    public sealed partial class TerrainGenerator
    {
        private const int SeedRegionCoast = 2401, SeedMassif = 2503, SeedMassifRidge = 2609;
        private const int SeedIsland = 2707, SeedIslet = 2801, SeedSideFjord = 2903, SeedIslandRidge = 3001;

        /// <summary>Half-extent (m, Chebyshev) of the far field around the origin.</summary>
        public const float FarFieldExtentM = 16000f;
        /// <summary>Distance beyond the world edge over which the play-area fjord blends into the region.</summary>
        public const float FarBlendM = 1800f;
        /// <summary>Distance beyond the world edge over which the sea floor falls to the open shelf.</summary>
        public const float SeaBlendM = 500f;

        // Regional coast: the mainland shore wanders along Z beyond the world
        // (positive: a bay; negative: a headland), in two scales.
        private const float CoastShiftM = 3600f;
        private const float CoastShiftWavelengthM = 11000f;
        private const float CoastWiggleM = 700f;
        private const float CoastWiggleWavelengthM = 2600f;

        // The outer coast is low: land within this distance (m) of the
        // regional shore keeps this share of its height, rising inland to all
        // of it (the plateau would otherwise meet the sea as one long cliff).
        private const float CoastLowlandM = 3800f;
        private const float CoastLowlandShare = 0.3f;

        // Inland massifs behind the head and the plateau.
        private const float MassifLiftM = 560f;
        private const float MassifRidgeM = 320f;
        private const float MassifWavelengthM = 5200f;
        private const float MassifRidgeWavelengthM = 2600f;

        // Islands: large islands of the coastal belt, the low outer chain, and skerries.
        private const float SideIslandHeightM = 300f;
        private const float OuterIslandHeightM = 140f;
        private const float IsletHeightM = 16f;
        private const float IslandWavelengthM = 5200f;
        private const float IslandDetailWavelengthM = 1300f;
        private const float IslandThreshold = 0.16f;
        /// <summary>No island in the open-sea sector west of the mouth nearer than this (m).</summary>
        public const float OuterChainStartM = 11500f;
        /// <summary>Half-angle cosine of that open-sea sector (about 32°).</summary>
        private const float OpenSeaSectorCos = 0.85f;
        /// <summary>Clear water (m) kept round every offshore slot.</summary>
        public const float SlotClearanceM = 1100f;

        // Seabed beyond the world: depth (m) at the world edge, its slope
        // (m per m) out to the shelf break and beyond it, and the deepest.
        // Tuned so the offshore slots sit near the spec's depths (§4: 150,
        // 220 and 300 m at 0.75, 1.4 and 3.2 km beyond the edge).
        private const float SeabedEdgeDepthM = 85f;
        private const float SeabedInnerSlope = 0.1f;
        private const float SeabedBreakM = 1400f;
        private const float SeabedOuterSlope = 0.042f;
        private const float SeabedMaxDepthM = 320f;

        /// <summary>
        /// The neighbouring fjords: x of the mouth (m), centreline drift per
        /// metre inland, half-width, depth and the head's z (m).
        /// </summary>
        private static readonly (float x, float drift, float halfWidth, float depth, float headZ)[] SideFjords =
        {
            (6900f, 0.18f, 480f, 210f, 9000f),
            (-7600f, -0.32f, 400f, 170f, 5200f),
        };
        private const float SideFjordWallSlope = 1.5f;
        private const float SideFjordBlendM = 70f;
        private const float SideFjordMeanderM = 650f;

        /// <summary>
        /// Terrain height (m) at any world point out to the far field:
        /// <see cref="HeightAt"/> inside the world square, the region beyond it.
        /// </summary>
        public float FarFieldHeightAt(float x, float z)
        {
            float half = Settings.WorldSizeM * 0.5f;
            float ox = MathF.Abs(x) - half, oz = MathF.Abs(z) - half;
            float near = HeightAt(x, z);
            if (ox <= 0f && oz <= 0f)
                return near;
            float dx = ox > 0f ? ox : 0f, dz = oz > 0f ? oz : 0f;
            float beyond = MathF.Sqrt(dx * dx + dz * dz);
            float w = SmoothStep(0f, FarBlendM, beyond);
            float zr = z - w * RegionalCoastShift(x);
            float h = near + (RegionLand(x, zr) - near) * w;
            // The open sea: below the shore the bottom falls to the shelf's
            // depth within 40 m of water; the islands rise from it.
            float ws = SmoothStep(0f, SeaBlendM, beyond);
            if (h < 0f)
                h = Lerp(h, Math.Min(h, -SeabedDepthM(beyond)), SmoothStep(0f, -40f, h) * ws);
            float island = IslandHeight(x, z, zr);
            return island > -SeabedMaxDepthM ? SmoothMax(h, island, 12f) : h;
        }

        /// <summary>
        /// Exposure (0-1) of far-field land to the open sea, for the
        /// vegetation masks: islands and the outer coast are wind-swept, the
        /// mainland sheltered a kilometre or so inland and in the side fjords.
        /// </summary>
        public float FarFieldLandExposure(float x, float z)
        {
            float half = Settings.WorldSizeM * 0.5f;
            float dx = Math.Max(MathF.Abs(x) - half, 0f), dz = Math.Max(MathF.Abs(z) - half, 0f);
            float w = SmoothStep(0f, FarBlendM, MathF.Sqrt(dx * dx + dz * dz));
            float zr = z - w * RegionalCoastShift(x);
            float offshore = Settings.MouthZ - 40f - zr;
            return SmoothStep(-1200f, 100f, offshore);
        }

        /// <summary>The region's land at (x, zr) (zr: z after the coast shift).</summary>
        private float RegionLand(float x, float zr)
        {
            float h = Sample(x, zr, shelfAtWorldEdge: false).Height;
            float coastZ = Settings.MouthZ - 40f;
            float inland = zr - coastZ;
            if (h > 0f)
                h *= CoastLowlandShare + (1f - CoastLowlandShare) * SmoothStep(150f, CoastLowlandM, inland);
            // Inland of the shore: lift the massifs behind the fjord head and
            // the plateau, more the farther from the sea.
            if (h > 0f && inland > 0f)
            {
                float lift = SmoothStep(1500f, 7000f, inland) * SmoothStep(30f, 400f, h);
                if (lift > 0f)
                {
                    int seed = Settings.Seed;
                    float m = FractalNoise.Fbm(seed + SeedMassif, x / MassifWavelengthM, zr / MassifWavelengthM, 3);
                    float r = FractalNoise.Ridged(seed + SeedMassifRidge, x / MassifRidgeWavelengthM, zr / MassifRidgeWavelengthM, 4, 2f, 0.45f, 0.12f);
                    h += lift * (MassifLiftM * (0.55f + 0.45f * m) + MassifRidgeM * (r - 0.35f));
                }
            }
            return SmoothMin(h, SideFjordHeight(x, zr), SideFjordBlendM);
        }

        /// <summary>Depth (m) of the open seabed this far (m) beyond the world edge.</summary>
        private static float SeabedDepthM(float beyond)
        {
            float d = SeabedEdgeDepthM + SeabedInnerSlope * Math.Min(beyond, SeabedBreakM)
                + SeabedOuterSlope * Math.Max(beyond - SeabedBreakM, 0f);
            return Math.Min(d, SeabedMaxDepthM);
        }

        /// <summary>How far (m) the mainland shore moves inland (positive) at x, beyond the world.</summary>
        private float RegionalCoastShift(float x)
        {
            int seed = Settings.Seed;
            float n = CoastShiftM * FractalNoise.Fbm(seed + SeedRegionCoast, x / CoastShiftWavelengthM, 0.37f, 2)
                + CoastWiggleM * FractalNoise.Fbm(seed + SeedRegionCoast + 3, x / CoastWiggleWavelengthM, 2.11f, 2);
            // Grows away from the play area, where the shore is the fjord's own.
            return n * SmoothStep(2200f, 6500f, MathF.Abs(x));
        }

        /// <summary>
        /// The neighbouring fjords as troughs: a U floor under water and walls
        /// that keep rising (the smooth minimum with the plateau caps them).
        /// </summary>
        private float SideFjordHeight(float x, float z)
        {
            float best = float.MaxValue;
            float coastZ = Settings.MouthZ - 40f;
            for (int k = 0; k < SideFjords.Length; k++)
            {
                var f = SideFjords[k];
                float cz = Math.Min(z, f.headZ);
                float cx = f.x + f.drift * Math.Max(cz - coastZ, 0f)
                    + SideFjordMeanderM * OpenSimplex2.Noise2(Settings.Seed + SeedSideFjord + k, cz / 3200f, 0.5f + k);
                float dxk = x - cx;
                float dzk = z > f.headZ ? z - f.headZ : 0f;
                float d = dzk > 0f ? MathF.Sqrt(dxk * dxk + dzk * dzk) : MathF.Abs(dxk);
                float wdt = f.halfWidth * (1f + 0.2f * OpenSimplex2.Noise2(Settings.Seed + SeedSideFjord + 17 + k, cz / 1500f, 1.5f));
                float depth = f.depth * SmoothStep(f.headZ + 60f, f.headZ - 900f, z);
                float h;
                if (d < wdt)
                {
                    float r = d / wdt;
                    float r2 = r * r;
                    h = -depth * (1f - r2 * r2);
                }
                else
                {
                    h = SideFjordWallSlope * (d - wdt);
                }
                best = Math.Min(best, h);
            }
            return best;
        }

        /// <summary>
        /// Islands at (x, z): an envelope (where islands may stand and how
        /// high) times a thresholded noise field (large islands with a
        /// detailed shore), with skerries just off their shores and the
        /// mainland's; −<see cref="SeabedMaxDepthM"/> where no island may stand.
        /// </summary>
        private float IslandHeight(float x, float z, float zr)
        {
            int seed = Settings.Seed;
            float coastZ = Settings.MouthZ - 40f;
            float mouthX = CenterX(Settings.MouthZ);
            float px = x - mouthX, pz = z - Settings.MouthZ;
            float r = MathF.Sqrt(px * px + pz * pz);
            float cosWest = r > 1f ? -pz / r : 0f;
            // The open-sea sector west of the mouth: no island before the outer chain.
            float openSea = SmoothStep(OpenSeaSectorCos - 0.08f, OpenSeaSectorCos + 0.04f, cosWest);
            float outer = SmoothStep(OuterChainStartM - 500f, OuterChainStartM + 1500f, r);
            // Seaward of the mainland shore, beyond the play area's own skerries.
            float offshore = coastZ - zr;
            float seaward = SmoothStep(-200f, 300f, offshore);
            float side = SmoothStep(2600f, 4600f, MathF.Abs(x)) * (1f - SmoothStep(5000f, 11000f, offshore));
            float belt = side * (1f - openSea);
            float envelope = Math.Max(belt * SideIslandHeightM, outer * OuterIslandHeightM);
            float clear = SlotClearance(x, z) * seaward;
            envelope *= clear;
            if (envelope <= 0f)
                return -SeabedMaxDepthM;
            float n = FractalNoise.Fbm(seed + SeedIsland, x / IslandWavelengthM, z / IslandWavelengthM, 3)
                + 0.35f * FractalNoise.Fbm(seed + SeedIsland + 5, x / IslandDetailWavelengthM, z / IslandDetailWavelengthM, 3);
            float t = (n - IslandThreshold) * 3f;
            float land;
            if (t > 0f)
            {
                t = Math.Min(t, 1f);
                // Rounded hills with a rocky crest line.
                float crest = FractalNoise.Ridged(seed + SeedIslandRidge, x / 1800f, z / 1800f, 3, 2f, 0.5f, 0.15f);
                land = envelope * t * (2f - t) * (0.6f + 0.55f * crest);
            }
            else
            {
                // The shallows round an island (under water: they only keep
                // the sea floor continuous where the envelope fades).
                land = 60f * Math.Max(t, -1f) * Math.Min(envelope / 60f, 1f) - SeabedMaxDepthM * (1f - Math.Min(envelope / 60f, 1f));
            }
            // Skerries: a fringe just off the island and mainland shores.
            float fringe = (1f - SmoothStep(0f, 0.12f, IslandThreshold - n)) * belt
                + (1f - SmoothStep(300f, 1600f, offshore)) * side;
            fringe = Math.Min(fringe, 1f) * clear;
            if (fringe > 0f)
            {
                float islet = OpenSimplex2.Noise2(seed + SeedIslet, x / 330f, z / 330f)
                    + 0.5f * OpenSimplex2.Noise2(seed + SeedIslet + 1, x / 120f, z / 120f);
                land = Math.Max(land, fringe * IsletHeightM * Math.Clamp((islet - 0.55f) * 3f, -1f, 1f));
            }
            return land;
        }

        /// <summary>0 within the clearance of an offshore slot, 1 well away from every slot.</summary>
        private static float SlotClearance(float x, float z)
        {
            float k = 1f;
            foreach (var slot in OffshoreSlotLayout)
            {
                float dx = x - slot.x, dz = z - slot.z;
                float d = MathF.Sqrt(dx * dx + dz * dz);
                k = Math.Min(k, SmoothStep(SlotClearanceM * 0.6f, SlotClearanceM, d));
            }
            return k;
        }

        /// <summary>Polynomial smooth minimum (Inigo Quilez): min(a, b) rounded over k.</summary>
        private static float SmoothMin(float a, float b, float k)
        {
            float h = Math.Clamp(0.5f + 0.5f * (b - a) / k, 0f, 1f);
            return b + (a - b) * h - k * h * (1f - h);
        }

        private static float SmoothMax(float a, float b, float k) => -SmoothMin(-a, -b, k);
    }
}
