namespace LakskongenSim.Environment
{
    /// <summary>
    /// The wall ledges' terraces on a heightfield grid (gfx-polish-1), from
    /// <see cref="TerrainGenerator.GenerateHeightmap(out WallLedges)"/>, row-major like the heights:
    /// <see cref="Offsets"/> is the part of each height the terraces add (m, 0 off the walls), and
    /// <see cref="Strength"/> how far they flatten the terraces' flats and steepen their risers
    /// (a share of the wall's slope: 0 off the walls, up to the generator's ledge strength on them).
    /// </summary>
    public readonly record struct WallLedges(float[] Offsets, float[] Strength);
}
