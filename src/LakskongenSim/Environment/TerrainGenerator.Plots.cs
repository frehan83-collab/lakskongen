namespace LakskongenSim.Environment
{
    /// <summary>
    /// The farm-type plots of the procedural fjord (farm-types spec §4,
    /// roadmap G2-01 groundwork, F2-19): three shore pads for land-based farms
    /// and three open-sea slots for offshore units, at the spec's procedural
    /// positions (godot/data/site_plots.json, the same within 1 m). The far
    /// field keeps the slots' water clear of islands, and the sea there is the
    /// open ocean the water bake assumes beyond the play area.
    ///
    /// F2-19 carves the pads into the heightfield (<see cref="CarvePads(float, float, float)"/>):
    /// every pad is a flat platform at <see cref="PadHeightM"/> (the Indre
    /// Harøy plan level [L:R13]) on the strandflat, cut into the shore and
    /// filled out over the skerries, with a rim that falls to the sea as a
    /// rocky shore or rises to the land as a cut face. The rim drops straight
    /// to deep water along the quay face (<see cref="PadQuayDropM"/>) and runs
    /// out as a low rock shelf of wandering width round the rest of the pad
    /// (<see cref="PadRimAt"/>), so an unbuilt pad's coast reads as a low
    /// strandflat holm's, not a ruled edge, and a built one keeps its berth.
    /// The heightfield never changes
    /// at runtime: an unbuilt pad is painted as heath and a blasted one as
    /// granite by the terrain shader's <c>pad_state</c>, and the vegetation and
    /// shore props keep off the pads and their rims (<see cref="PadClearings"/>).
    /// </summary>
    public sealed partial class TerrainGenerator : ISitePlotGeometry
    {
        /// <summary>Pad level above the sea (m): the Indre Harøy plan level (spec §4).</summary>
        public const float PadHeightM = 3.6f;
        /// <summary>
        /// The rim round a pad (m). Toward the sea a low rock shelf runs out
        /// from the pad's edge, falling from the pad level to its lip
        /// (<see cref="PadShelfLipM"/>) over a width that wanders between
        /// <see cref="PadShelfMinM"/> and <see cref="PadShelfMaxM"/> (headlands
        /// and coves), then drops to the natural seabed over
        /// <see cref="PadShelfDropM"/>; along the quay face it is a
        /// <see cref="PadQuayShelfM"/> lip and a <see cref="PadQuayDropM"/>
        /// wall to deep water. Into higher ground the pad leaves a cut face
        /// <see cref="PadCutM"/> wide. Spec §4 sets a 30 m blend; F2-19 widened
        /// the seaward rim so an unbuilt pad's coast reads as a strandflat
        /// holm's, not a ruled edge (RUN_LOG §7 2026-10-17).
        /// </summary>
        public const float PadShelfMinM = 6f, PadShelfMaxM = 70f, PadShelfDropM = 18f, PadShelfLipM = 0.4f;
        /// <summary>The longest drop (m): a deeper seabed than the slope bound allows over it is met more steeply.</summary>
        public const float PadDropMaxM = 35f;
        /// <summary>The quay face: a short lip at the quay's deck height and a near-vertical wall to the seabed.</summary>
        public const float PadQuayShelfM = 1.5f, PadQuayDropM = 9f, PadQuayLipM = 3.0f;
        /// <summary>Width (m) of a cut face where the pad is blasted into ground above its level.</summary>
        public const float PadCutM = 24f;
        /// <summary>The widest rim (m): nothing beyond it is carved.</summary>
        public const float PadRimMaxM = PadShelfMaxM + PadDropMaxM;
        /// <summary>Wavelengths (m) of the shelf's wander: headlands and coves, and their ragged detail.</summary>
        private const float PadRimWaveM = 340f, PadRimDetailM = 90f;
        /// <summary>Distance (m) round a quay face's ends over which the shelf widens to the natural shore's.</summary>
        private const float PadQuayFaceEaseM = 60f;
        /// <summary>The rim ends at least this far (m) inside the world's edge (the far field's seam).</summary>
        private const float PadEdgeMarginM = 10f, PadEdgeShelfShare = 0.4f;
        /// <summary>
        /// Mean slope (rise over run) a cut face or a drop to the seabed may
        /// take: a smoothstep's steepest is 1.5 times it, under the generator's
        /// sub-grid bound of 8 (TerrainGeneratorTests).
        /// </summary>
        internal const float PadRimSlope = 3f;
        private const int SeedPadRim = 3101;
        /// <summary>Depth (m) a deep-water intake must reach (spec §4).</summary>
        public const float DeepIntakeDepthM = 95f;
        /// <summary>How far (m) beyond a pad edge the quay test looks for water.</summary>
        private const float QuayProbeM = 60f;

        /// <summary>
        /// Pad id, name, centre (x, z), width (x), depth (z) and lots (spec §4).
        /// pad_a stands where the F2-18 read gate moved it, (−450, −1720)
        /// (revision 2 had (−700, −1650), where the western headland hid 70% of
        /// the campus at the home view).
        /// </summary>
        private static readonly (string id, string name, float x, float z, float width, float depth, int lots)[] LandPadLayout =
        {
            ("pad_a", "Storholmen", -450f, -1720f, 640f, 420f, 4),
            ("pad_b", "Skjærvika", 750f, -1640f, 480f, 360f, 2),
            ("pad_delta", "Elveøyra", 0f, 1450f, 260f, 180f, 4),
        };

        /// <summary>Offshore slot id, name and position (x, z) (spec §4), all beyond the world edge.</summary>
        private static readonly (string id, string name, float x, float z)[] OffshoreSlotLayout =
        {
            ("off_1", "Ytre kyst", -450f, -2750f),
            ("off_2", "Havkanten", 850f, -3400f),
            ("hav_1", "Havområde", 0f, -5200f),
        };

        /// <inheritdoc />
        public IReadOnlyList<LandPadSlot> LandPadSlots()
        {
            var pads = new List<LandPadSlot>(LandPadLayout.Length);
            for (int i = 0; i < LandPadLayout.Length; i++)
            {
                var p = LandPadLayout[i];
                var (qx, qz) = QuayOf(i);
                pads.Add(new LandPadSlot(p.id, p.name, p.x, p.z, p.width, p.depth, p.lots, PadHeightM, qx, qz));
            }
            return pads;
        }

        /// <inheritdoc />
        public IReadOnlyList<OffshoreSlot> OffshoreSlots()
        {
            var slots = new List<OffshoreSlot>(OffshoreSlotLayout.Length);
            foreach (var s in OffshoreSlotLayout)
                slots.Add(new OffshoreSlot(s.id, s.name, s.x, s.z, 1f, -FarFieldHeightAt(s.x, s.z)));
            return slots;
        }

        /// <summary>
        /// The ground the vegetation and the shore props keep off (F2-19): every
        /// pad with its widest rim (<see cref="PadRimMaxM"/>), so no tree
        /// stands on a pad, its cut faces or its rock fill, and no naust or quay
        /// is built there. Catalogue order.
        /// </summary>
        public IReadOnlyList<GroundRect> PadClearings()
        {
            var rects = new List<GroundRect>(LandPadLayout.Length);
            foreach (var p in LandPadLayout)
            {
                float hw = p.width * 0.5f + PadRimMaxM, hd = p.depth * 0.5f + PadRimMaxM;
                rects.Add(new GroundRect(p.x - hw, p.z - hd, p.x + hw, p.z + hd));
            }
            return rects;
        }

        /// <summary>
        /// The seaward rim's shape at a point beyond a pad's edge: the shelf's
        /// width, its lip height and the drop to the seabed (m). The shelf
        /// wanders between <see cref="PadShelfMinM"/> and <see cref="PadShelfMaxM"/>
        /// along the pad's perimeter with two octaves of the generator's noise
        /// (headlands and coves ~340 m apart, a little ragged at ~90 m), read at the
        /// nearest point of the nearest pad's edge, so it never changes along a
        /// line straight out from the pad (the drop keeps its width); along the
        /// quay face it is the quay's short lip and wall, easing to the natural
        /// shore over <see cref="PadQuayFaceEaseM"/> beside it.
        /// </summary>
        internal (float shelf, float lip, float drop) PadRimAt(float x, float z)
        {
            int seed = Settings.Seed + SeedPadRim;
            var (ex, ez) = NearestPadEdgePoint(x, z);
            float wave = OpenSimplex2.Noise2(seed, ex / PadRimWaveM, ez / PadRimWaveM);
            float detail = OpenSimplex2.Noise2(seed + 1, ex / PadRimDetailM, ez / PadRimDetailM);
            float t = 0.5f + 0.5f * (0.95f * wave + 0.15f * detail);
            t = t < 0f ? 0f : (t > 1f ? 1f : t);
            float shelf = PadShelfMinM + (PadShelfMaxM - PadShelfMinM) * t;
            float face = 0f;
            for (int i = 0; i < LandPadLayout.Length; i++)
                face = Math.Max(face, QuayFaceAt(i, x, z));
            return (Lerp(shelf, PadQuayShelfM, face), Lerp(PadShelfLipM, PadQuayLipM, face), Lerp(PadShelfDropM, PadQuayDropM, face));
        }

        /// <summary>The point of the nearest pad's rectangle nearest (x, z).</summary>
        private static (float x, float z) NearestPadEdgePoint(float x, float z)
        {
            float best = float.MaxValue;
            (float x, float z) edge = (x, z);
            foreach (var p in LandPadLayout)
            {
                float hw = p.width * 0.5f, hd = p.depth * 0.5f;
                float qx = Math.Clamp(x, p.x - hw, p.x + hw), qz = Math.Clamp(z, p.z - hd, p.z + hd);
                float d = (x - qx) * (x - qx) + (z - qz) * (z - qz);
                if (d < best)
                {
                    best = d;
                    edge = (qx, qz);
                }
            }
            return edge;
        }

        /// <summary>
        /// 1 in front of pad <paramref name="index"/>'s quay edge (on its water
        /// side, out to the widest rim): the frontage runs on as one straight
        /// line past the edge's ends, so the shore on the adjacent sides never
        /// bulges out in front of the berth. Behind the quay line, beside the
        /// pad, it falls to 0 over <see cref="PadQuayFaceEaseM"/> back from the
        /// line (continuous across it).
        /// </summary>
        private float QuayFaceAt(int index, float x, float z)
        {
            var p = LandPadLayout[index];
            int side = _padQuaySides[index];
            // Out (beyond the edge) and along (from the edge's middle), in metres.
            float hw = p.width * 0.5f, hd = p.depth * 0.5f;
            float outward, along, halfLength;
            switch (side)
            {
                case 0: outward = x - p.x - hw; along = MathF.Abs(z - p.z); halfLength = hd; break;
                case 1: outward = p.x - x - hw; along = MathF.Abs(z - p.z); halfLength = hd; break;
                case 2: outward = z - p.z - hd; along = MathF.Abs(x - p.x); halfLength = hw; break;
                default: outward = p.z - z - hd; along = MathF.Abs(x - p.x); halfLength = hw; break;
            }
            if (outward > PadRimMaxM)
                return 0f;
            if (outward >= 0f)
                return 1f;
            // Behind the quay line (beside the pad), the shelf widens away from the frontage.
            return along > halfLength ? SmoothStep(-PadQuayFaceEaseM, 0f, outward) : 0f;
        }

        /// <inheritdoc />
        public float IntakeReachM(LandPadSlot pad)
        {
            var st = Settings;
            float half = st.WorldSizeM * 0.5f;
            float spacing = st.SpacingM;
            int n = st.Resolution;
            // The world edge caps the search: open ocean lies beyond it.
            float edge = Math.Min(Math.Min(half - pad.QuayX, half + pad.QuayX), Math.Min(half - pad.QuayZ, half + pad.QuayZ));
            float best = Math.Max(edge, 0f);
            int c0 = Math.Max(0, (int)MathF.Floor((pad.QuayX - best + half) / spacing));
            int c1 = Math.Min(n - 1, (int)MathF.Ceiling((pad.QuayX + best + half) / spacing));
            int r0 = Math.Max(0, (int)MathF.Floor((pad.QuayZ - best + half) / spacing));
            int r1 = Math.Min(n - 1, (int)MathF.Ceiling((pad.QuayZ + best + half) / spacing));
            for (int r = r0; r <= r1; r++)
            {
                float z = -half + r * spacing;
                for (int c = c0; c <= c1; c++)
                {
                    float x = -half + c * spacing;
                    float dx = x - pad.QuayX, dz = z - pad.QuayZ;
                    float d = MathF.Sqrt(dx * dx + dz * dz);
                    if (d < best && HeightAt(x, z) <= -DeepIntakeDepthM)
                        best = d;
                }
            }
            return best;
        }

        /// <summary>
        /// The pads carved into a height (F2-19, spec §4). On a pad:
        /// <see cref="PadHeightM"/>. Beyond it, at distance d from its edge (the
        /// distance to the rectangle, so the rim rounds the corners):
        /// <list type="bullet">
        /// <item>where the ground stands above the pad, a cut face: the level
        /// eases into the ground by the smoothstep of d over
        /// <see cref="PadCutM"/> (wider for a higher face, so no face is
        /// steeper than <see cref="PadRimSlope"/> on average);</item>
        /// <item>elsewhere a low rock shelf (<see cref="PadRimAt"/>) falls by a
        /// smoothstep from the pad level to its lip over the shelf's width, then
        /// eases from the lip into the natural seabed over the drop (longer for
        /// deeper water, by the same slope); the ground keeps its own height
        /// where it stands higher than that. The shelf narrows so the rim ends
        /// <see cref="PadEdgeMarginM"/> inside the world's edge.</item>
        /// </list>
        /// Both branches meet where the ground stands at the pad level, and
        /// every profile is continuous. Arithmetic only, like the rest of the
        /// generator, so every process carves the same heightfield.
        /// </summary>
        internal float CarvePads(float h, float x, float z) => CarvePads(h, x, z, out _);

        /// <summary>
        /// <see cref="CarvePads(float, float, float)"/>, and in
        /// <paramref name="keep"/> the share of a change in the given height
        /// that the carve passes on (its slope in h, the rim's widths held): 1
        /// where nothing is carved, 0 on a pad and where a shelf fills the
        /// ground up, a cut face's or a drop's smoothstep where they ease into
        /// the ground. The wall ledges' terraces (gfx-polish-1) are scaled by
        /// it, so the carved ground carries none of the uncarved wall's
        /// terraces (<see cref="Ground"/>). The height is computed exactly as
        /// without it.
        /// </summary>
        internal float CarvePads(float h, float x, float z, out float keep)
        {
            keep = 1f;
            float half = Settings.WorldSizeM * 0.5f;
            foreach (var p in LandPadLayout)
            {
                float hw = p.width * 0.5f, hd = p.depth * 0.5f;
                float dx = MathF.Abs(x - p.x) - hw;
                float dz = MathF.Abs(z - p.z) - hd;
                if (dx >= PadRimMaxM || dz >= PadRimMaxM)
                    continue;
                // Room (m) from the pad's edge to the world's edge on the sides the point lies beyond.
                float room = float.MaxValue;
                if (dx > 0f)
                    room = half - MathF.Abs(p.x + MathF.Sign(x - p.x) * hw);
                if (dz > 0f)
                    room = Math.Min(room, half - MathF.Abs(p.z + MathF.Sign(z - p.z) * hd));
                dx = dx > 0f ? dx : 0f;
                dz = dz > 0f ? dz : 0f;
                float d = MathF.Sqrt(dx * dx + dz * dz);
                if (d >= PadRimMaxM)
                    continue;
                if (d <= 0f)
                {
                    keep = 0f;
                    return PadHeightM;
                }
                if (h >= PadHeightM)
                {
                    // A cut face, wider into higher ground so it stays under the slope bound.
                    float cut = Math.Max(PadCutM, (h - PadHeightM) / PadRimSlope);
                    if (d < cut)
                    {
                        float face = SmoothStep(0f, cut, d);
                        h = PadHeightM + (h - PadHeightM) * face;
                        keep *= face;
                    }
                    continue;
                }
                var (shelf, lip, drop) = PadRimAt(x, z);
                // A deeper seabed takes a longer drop (the slope bound), and the
                // rim ends inside the world, clear of the far field's seam.
                drop = Math.Min(Math.Max(drop, (lip - h) / PadRimSlope), PadDropMaxM);
                shelf = Math.Min(shelf, Math.Max((room - PadEdgeMarginM) * PadEdgeShelfShare, 0f));
                float top, pass;
                if (d <= shelf)
                {
                    top = PadHeightM + (lip - PadHeightM) * SmoothStep(0f, shelf, d);
                    pass = 0f;
                }
                else if (d < shelf + drop)
                {
                    pass = SmoothStep(shelf, shelf + drop, d);
                    top = lip + (h - lip) * pass;
                }
                else
                    continue;
                if (top > h)
                    keep *= pass;
                h = Math.Max(h, top);
            }
            return h;
        }

        /// <summary>
        /// The side (0 +x, 1 −x, 2 +z, 3 −z) of every pad's quay, fixed when
        /// the generator is made from the uncarved ground: the edge that faces
        /// water (the ground <see cref="QuayProbeM"/> beyond its middle lies
        /// under the sea) nearest the fjord's channel, where the boats come
        /// from (spec §4: pad_a's quay on the fairway edge); with no edge on
        /// water, the edge whose ground just outside is lowest.
        /// </summary>
        private int[] QuaySides()
        {
            var sides = new int[LandPadLayout.Length];
            for (int i = 0; i < LandPadLayout.Length; i++)
            {
                var (cx, cz, width, depth) = (LandPadLayout[i].x, LandPadLayout[i].z, LandPadLayout[i].width, LandPadLayout[i].depth);
                (float x, float z, float ox, float oz)[] edges =
                {
                    (cx + width * 0.5f, cz, QuayProbeM, 0f),
                    (cx - width * 0.5f, cz, -QuayProbeM, 0f),
                    (cx, cz + depth * 0.5f, 0f, QuayProbeM),
                    (cx, cz - depth * 0.5f, 0f, -QuayProbeM),
                };
                int best = -1;
                float nearest = float.MaxValue;
                for (int k = 0; k < edges.Length; k++)
                {
                    var e = edges[k];
                    if (Sample(e.x + e.ox, e.z + e.oz).Height >= 0f)
                        continue;
                    float channel = MathF.Abs(e.x - CenterX(e.z));
                    if (channel < nearest)
                    {
                        nearest = channel;
                        best = k;
                    }
                }
                if (best < 0)
                {
                    float lowest = float.MaxValue;
                    for (int k = 0; k < edges.Length; k++)
                    {
                        float h = Sample(edges[k].x + edges[k].ox, edges[k].z + edges[k].oz).Height;
                        if (h < lowest)
                        {
                            lowest = h;
                            best = k;
                        }
                    }
                }
                sides[i] = best;
            }
            return sides;
        }

        /// <summary>The middle of pad <paramref name="index"/>'s quay edge.</summary>
        private (float x, float z) QuayOf(int index)
        {
            var p = LandPadLayout[index];
            return _padQuaySides[index] switch
            {
                0 => (p.x + p.width * 0.5f, p.z),
                1 => (p.x - p.width * 0.5f, p.z),
                2 => (p.x, p.z + p.depth * 0.5f),
                _ => (p.x, p.z - p.depth * 0.5f),
            };
        }
    }
}
