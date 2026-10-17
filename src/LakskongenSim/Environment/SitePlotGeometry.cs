namespace LakskongenSim.Environment
{
    /// <summary>
    /// A flat shore pad reserved for a land-based farm (farm-types spec §4):
    /// an axis-aligned rectangle of <see cref="WidthM"/> (along X) by
    /// <see cref="DepthM"/> (along Z) round its centre, carved to
    /// <see cref="PadHeightM"/> above the sea, with its quay at
    /// (<see cref="QuayX"/>, <see cref="QuayZ"/>) on the side that faces the
    /// water. World metres, shared with the terrain and the renderer.
    /// </summary>
    public sealed record LandPadSlot(
        string Id,
        string Name,
        float CentreX,
        float CentreZ,
        float WidthM,
        float DepthM,
        int Lots,
        float PadHeightM,
        float QuayX,
        float QuayZ)
    {
        /// <summary>True when (x, z) lies on the pad.</summary>
        public bool Contains(float x, float z)
            => MathF.Abs(x - CentreX) <= WidthM * 0.5f && MathF.Abs(z - CentreZ) <= DepthM * 0.5f;
    }

    /// <summary>
    /// An axis-aligned rectangle of ground in world metres (F2-19): a pad with
    /// its rim, which trees and shore buildings keep off.
    /// </summary>
    public readonly record struct GroundRect(float MinX, float MinZ, float MaxX, float MaxZ)
    {
        /// <summary>True when (x, z) lies in the rectangle (edges included).</summary>
        public bool Contains(float x, float z) => x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;

        /// <summary>True when (x, z) lies in any of <paramref name="rects"/> (none: false).</summary>
        public static bool AnyContains(IReadOnlyList<GroundRect>? rects, float x, float z)
        {
            if (rects == null)
                return false;
            foreach (var r in rects)
            {
                if (r.Contains(x, z))
                    return true;
            }
            return false;
        }
    }

    /// <summary>
    /// An open-sea slot reserved for an offshore unit (farm-types spec §4),
    /// outside the play area's water bake: <see cref="Exposure"/> is the
    /// open-sea exposure there (1, the bake's "open ocean"), and
    /// <see cref="WaterDepthM"/> the depth the terrain gives at the slot.
    /// </summary>
    public sealed record OffshoreSlot(string Id, string Name, float X, float Z, float Exposure, float WaterDepthM);

    /// <summary>
    /// The geometric half of the site-plot catalogue (farm-types spec F2-03 /
    /// F2-19): where the land pads and offshore slots lie and what the ground
    /// and sea there are like. The catalogue joins these by id with the
    /// gameplay attributes in <c>godot/data/site_plots.json</c>; for a baked
    /// real region (G2-01) the same shapes come from the bake's
    /// <c>meta.json</c> instead of the procedural layout.
    /// </summary>
    public interface ISitePlotGeometry
    {
        /// <summary>The land pads, in catalogue order.</summary>
        IReadOnlyList<LandPadSlot> LandPadSlots();

        /// <summary>The offshore slots, in catalogue order.</summary>
        IReadOnlyList<OffshoreSlot> OffshoreSlots();

        /// <summary>
        /// Length (m) of a pad's deep-water intake: from its quay to the
        /// nearest heightfield vertex at <see cref="TerrainGenerator.DeepIntakeDepthM"/>
        /// or deeper, or to the world edge (open ocean beyond it), whichever
        /// is nearer. Computed, never authored.
        /// </summary>
        float IntakeReachM(LandPadSlot pad);
    }
}
