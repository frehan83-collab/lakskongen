using System.Collections.Generic;

namespace LakskongenSim.Environment
{
    /// <summary>The kinds of building on the fjord shore.</summary>
    public enum ShorePropKind : byte
    {
        /// <summary>A naust: the gabled boathouse on a dry-stone base, its door to the water.</summary>
        Naust = 0,
        /// <summary>A small solid quay (stone crib, concrete deck) running out from the shore.</summary>
        Quay = 1,
    }

    /// <summary>
    /// One shore building. (X, Y, Z) is the base point in world metres (for a
    /// quay, its landward end at the waterline); the model faces +<see cref="Yaw"/>
    /// toward the water (Godot: local −Z turned by Yaw about +Y);
    /// <see cref="Variant"/> picks a paint (0 red ochre, 1 tarred).
    /// </summary>
    public readonly record struct ShoreProp(ShorePropKind Kind, float X, float Y, float Z, float Yaw, int Variant, float FacingX, float FacingZ);

    /// <summary>Where the shore buildings may stand (G2-02).</summary>
    public sealed record ShorePropSettings
    {
        /// <summary>A naust stands this high above the sea, no lower (spring tide plus splash) and no higher.</summary>
        public float MinHeightM { get; init; } = 1.8f;
        public float MaxHeightM { get; init; } = 9f;
        /// <summary>Its footprint is gentler than this (tan 14°).</summary>
        public float MaxSlopeTan { get; init; } = 0.249f;
        /// <summary>Distance from the naust's centre to the waterline.</summary>
        public float MinShoreDistanceM { get; init; } = 5f;
        public float MaxShoreDistanceM { get; init; } = 28f;
        /// <summary>Shelter: the land's open-sea exposure stays below this.</summary>
        public float MaxExposure { get; init; } = 0.45f;
        /// <summary>Naust footprint (m) and the spacing along a row of them.</summary>
        public float NaustWidthM { get; init; } = 5f;
        public float NaustLengthM { get; init; } = 9f;
        public float RowSpacingM { get; init; } = 8.5f;
        /// <summary>Up to this many groups, this far apart, of up to NaustPerGroup boathouses.</summary>
        public int MaxGroups { get; init; } = 3;
        public float GroupSpacingM { get; init; } = 300f;
        public int NaustPerGroup { get; init; } = 3;
        /// <summary>The quay runs this far out and needs this much water at its head.</summary>
        public float QuayLengthM { get; init; } = 18f;
        public float QuayMinDepthM { get; init; } = 2.5f;
        /// <summary>Trees keep this far from a naust's centre and from the quay's root.</summary>
        public float TreeClearanceM { get; init; } = 14f;
    }

    /// <summary>
    /// Places naust rows and a quay on sheltered, gentle shore near the water,
    /// facing it, deterministically from the heightfield (no RNG stream).
    /// </summary>
    public static class ShorePropPlacer
    {
        /// <summary>
        /// Props for a heightfield of n² vertices spanning worldSize metres
        /// (row-major by Z, centred on the origin). shoreDistance is the
        /// WaterBake G channel (signed distance to the shoreline, positive over
        /// water); landExposure the vegetation masks' exposure.
        /// </summary>
        public static List<ShoreProp> Place(float[] heights, int n, float worldSize, float[] shoreDistance, float[] landExposure, ShorePropSettings? settings = null)
        {
            var s = settings ?? new ShorePropSettings();
            float spacing = worldSize / (n - 1);
            float half = worldSize * 0.5f;
            var candidates = new List<(float score, float x, float z)>();
            for (int r = 2; r < n - 2; r++)
            {
                for (int c = 2; c < n - 2; c++)
                {
                    int i = r * n + c;
                    float h = heights[i];
                    float d = -shoreDistance[i];
                    if (h < s.MinHeightM || h > s.MaxHeightM || d < s.MinShoreDistanceM || d > s.MaxShoreDistanceM || landExposure[i] > s.MaxExposure)
                        continue;
                    float x = -half + c * spacing, z = -half + r * spacing;
                    float slope = VegetationScatter.SlopeTanAt(heights, n, spacing, half, x, z);
                    if (slope > s.MaxSlopeTan)
                        continue;
                    // Flatter, closer to the water and lower is better.
                    candidates.Add((slope * 4f + d / s.MaxShoreDistanceM + h / s.MaxHeightM, x, z));
                }
            }
            candidates.Sort((a, b) => a.score != b.score ? a.score.CompareTo(b.score) : (a.x != b.x ? a.x.CompareTo(b.x) : a.z.CompareTo(b.z)));

            var props = new List<ShoreProp>();
            var groups = new List<(float x, float z)>();
            bool quayPlaced = false;
            foreach (var (_, cx, cz) in candidates)
            {
                if (groups.Count >= s.MaxGroups)
                    break;
                bool far = true;
                foreach (var g in groups)
                    far &= (g.x - cx) * (g.x - cx) + (g.z - cz) * (g.z - cz) >= s.GroupSpacingM * s.GroupSpacingM;
                if (!far)
                    continue;
                var row = NaustRow(heights, n, spacing, half, shoreDistance, s, cx, cz, groups.Count);
                if (row.Count < 2)
                    continue;
                groups.Add((cx, cz));
                props.AddRange(row);
                if (!quayPlaced && TryQuay(heights, n, spacing, half, shoreDistance, s, row, out var quay))
                {
                    props.Add(quay);
                    quayPlaced = true;
                }
            }
            return props;
        }

        /// <summary>True when a naust may stand at (x, z) facing (fx, fz).</summary>
        public static bool NaustFits(float[] heights, int n, float spacing, float half, float[] shoreDistance, ShorePropSettings s, float x, float z, float fx, float fz)
        {
            float h = VegetationScatter.Bilinear(heights, n, spacing, half, x, z);
            float d = -VegetationScatter.Bilinear(shoreDistance, n, spacing, half, x, z);
            if (h < s.MinHeightM || h > s.MaxHeightM || d < s.MinShoreDistanceM || d > s.MaxShoreDistanceM)
                return false;
            // Every corner on gentle, dry ground; the door end above the splash zone.
            float hw = s.NaustWidthM * 0.5f, hl = s.NaustLengthM * 0.5f;
            float lx = -fz, lz = fx;
            for (int k = 0; k < 4; k++)
            {
                float a = (k & 1) == 0 ? -hw : hw, b = (k & 2) == 0 ? -hl : hl;
                float px = x + lx * a + fx * b, pz = z + lz * a + fz * b;
                float ph = VegetationScatter.Bilinear(heights, n, spacing, half, px, pz);
                if (ph < s.MinHeightM - 0.6f || VegetationScatter.SlopeTanAt(heights, n, spacing, half, px, pz) > s.MaxSlopeTan * 1.6f)
                    return false;
            }
            // Water in front of the door.
            float ahead = d + 6f;
            return VegetationScatter.Bilinear(heights, n, spacing, half, x + fx * ahead, z + fz * ahead) < 0f;
        }

        private static List<ShoreProp> NaustRow(float[] heights, int n, float spacing, float half, float[] shoreDistance, ShorePropSettings s, float cx, float cz, int group)
        {
            var row = new List<ShoreProp>();
            var (fx, fz) = Facing(shoreDistance, n, spacing, half, cx, cz);
            if (fx == 0f && fz == 0f)
                return row;
            // Along the shore: 0, +1, -1, +2, ... steps of RowSpacingM.
            float lx = -fz, lz = fx;
            for (int k = 0; k < 6 && row.Count < s.NaustPerGroup; k++)
            {
                int step = (k + 1) / 2 * ((k & 1) == 1 ? 1 : -1);
                float x = cx + lx * step * s.RowSpacingM, z = cz + lz * step * s.RowSpacingM;
                var (ffx, ffz) = Facing(shoreDistance, n, spacing, half, x, z);
                // A row keeps one heading, near the local one.
                if (ffx * fx + ffz * fz < 0.85f || !NaustFits(heights, n, spacing, half, shoreDistance, s, x, z, fx, fz))
                    continue;
                float baseY = BaseHeight(heights, n, spacing, half, s, x, z, fx, fz);
                row.Add(new ShoreProp(ShorePropKind.Naust, x, baseY, z, Yaw(fx, fz), (group + row.Count) % 2, fx, fz));
            }
            return row;
        }

        private static bool TryQuay(float[] heights, int n, float spacing, float half, float[] shoreDistance, ShorePropSettings s, List<ShoreProp> row, out ShoreProp quay)
        {
            quay = default;
            // Beside the row: one spacing past its last naust, from the waterline out.
            var last = row[^1];
            float fx = last.FacingX, fz = last.FacingZ;
            float lx = -fz, lz = fx;
            foreach (float side in new[] { 1f, -1f })
            {
                float x = last.X + lx * side * s.RowSpacingM * 1.4f, z = last.Z + lz * side * s.RowSpacingM * 1.4f;
                // Walk toward the water to the waterline.
                for (float t = 0f; t < s.MaxShoreDistanceM + 10f; t += 1f)
                {
                    float px = x + fx * t, pz = z + fz * t;
                    if (VegetationScatter.Bilinear(heights, n, spacing, half, px, pz) > 0.3f)
                        continue;
                    float headDepth = -VegetationScatter.Bilinear(heights, n, spacing, half, px + fx * s.QuayLengthM, pz + fz * s.QuayLengthM);
                    if (headDepth < s.QuayMinDepthM)
                        break;
                    quay = new ShoreProp(ShorePropKind.Quay, px, 0f, pz, Yaw(fx, fz), 0, fx, fz);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Unit direction (x, z) toward the water: the shore distance's gradient.</summary>
        public static (float x, float z) Facing(float[] shoreDistance, int n, float spacing, float half, float x, float z)
        {
            float e = spacing * 2f;
            float gx = VegetationScatter.Bilinear(shoreDistance, n, spacing, half, x + e, z) - VegetationScatter.Bilinear(shoreDistance, n, spacing, half, x - e, z);
            float gz = VegetationScatter.Bilinear(shoreDistance, n, spacing, half, x, z + e) - VegetationScatter.Bilinear(shoreDistance, n, spacing, half, x, z - e);
            float len = MathF.Sqrt(gx * gx + gz * gz);
            return len < 1e-6f ? (0f, 0f) : (gx / len, gz / len);
        }

        /// <summary>Yaw (rad about +Y) that turns a model's local −Z onto (fx, fz).</summary>
        public static float Yaw(float fx, float fz) => MathF.Atan2(-fx, -fz);

        /// <summary>The highest corner of the footprint, less a little: the stone base sinks in uphill and shows downhill.</summary>
        private static float BaseHeight(float[] heights, int n, float spacing, float half, ShorePropSettings s, float x, float z, float fx, float fz)
        {
            float hw = s.NaustWidthM * 0.5f, hl = s.NaustLengthM * 0.5f;
            float lx = -fz, lz = fx;
            float top = float.MinValue;
            for (int k = 0; k < 4; k++)
            {
                float a = (k & 1) == 0 ? -hw : hw, b = (k & 2) == 0 ? -hl : hl;
                top = Math.Max(top, VegetationScatter.Bilinear(heights, n, spacing, half, x + lx * a + fx * b, z + lz * a + fz * b));
            }
            return top - 0.15f;
        }

        /// <summary>Clearings around the props (x, z, radius) for the tree scatter.</summary>
        public static List<(float x, float z, float r)> Clearings(IEnumerable<ShoreProp> props, ShorePropSettings? settings = null)
        {
            var s = settings ?? new ShorePropSettings();
            var list = new List<(float, float, float)>();
            foreach (var p in props)
            {
                list.Add((p.X, p.Z, s.TreeClearanceM));
                if (p.Kind == ShorePropKind.Quay)
                    list.Add((p.X - p.FacingX * 6f, p.Z - p.FacingZ * 6f, s.TreeClearanceM));
            }
            return list;
        }
    }
}
