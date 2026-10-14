namespace LakskongenSim.Environment
{
    /// <summary>
    /// The farm-type plots reserved in the procedural fjord (farm-types spec
    /// §4, roadmap G2-01 / F2-19 groundwork): three shore pads for land-based
    /// farms and three open-sea slots for offshore units, at the spec's
    /// procedural positions. The far field keeps the slots' water clear of
    /// islands (<see cref="SlotClearanceM"/>), and the sea there is the open
    /// ocean the water bake assumes beyond the play area.
    /// </summary>
    public sealed partial class TerrainGenerator : ISitePlotGeometry
    {
        /// <summary>Pad level above the sea (m): the Indre Harøy plan level (spec §4).</summary>
        public const float PadHeightM = 3.6f;
        /// <summary>Depth (m) a deep-water intake must reach (spec §4).</summary>
        public const float DeepIntakeDepthM = 95f;
        /// <summary>How far (m) beyond a pad edge the quay test looks for water.</summary>
        private const float QuayProbeM = 60f;

        /// <summary>Pad id, name, centre (x, z), width (x), depth (z) and lots (spec §4).</summary>
        private static readonly (string id, string name, float x, float z, float width, float depth, int lots)[] LandPadLayout =
        {
            ("pad_a", "Storholmen", -700f, -1650f, 640f, 420f, 4),
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
            foreach (var p in LandPadLayout)
            {
                var (qx, qz) = QuayOf(p.x, p.z, p.width, p.depth);
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
        /// The quay: the middle of the pad edge whose ground just outside is
        /// lowest (the side that faces the water).
        /// </summary>
        private (float x, float z) QuayOf(float cx, float cz, float width, float depth)
        {
            (float x, float z, float ox, float oz)[] edges =
            {
                (cx + width * 0.5f, cz, QuayProbeM, 0f),
                (cx - width * 0.5f, cz, -QuayProbeM, 0f),
                (cx, cz + depth * 0.5f, 0f, QuayProbeM),
                (cx, cz - depth * 0.5f, 0f, -QuayProbeM),
            };
            var best = edges[0];
            float lowest = float.MaxValue;
            foreach (var e in edges)
            {
                float h = HeightAt(e.x + e.ox, e.z + e.oz);
                if (h < lowest)
                {
                    lowest = h;
                    best = e;
                }
            }
            return (best.x, best.z);
        }
    }
}
