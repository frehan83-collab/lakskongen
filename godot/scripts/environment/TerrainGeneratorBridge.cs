using Godot;
using LakskongenSim.Environment;

/// <summary>
/// Thin Godot wrapper over <see cref="TerrainGenerator"/> (LakskongenSim).
///
/// Lets GDScript terrain code consume the seeded C# ridged-fractal +
/// domain-warp heightfield (which generates on background threads via
/// Parallel.For inside <c>GenerateHeightmapAsync</c>) with a GDScript
/// FastNoiseLite fallback when .NET is unavailable.
///
/// All methods are pure compute (no scene-tree access) so they are safe to
/// call from a GDScript worker <c>Thread</c>; mesh building must still
/// happen on the main thread via <c>call_deferred</c>.
/// </summary>
public partial class TerrainGeneratorBridge : Node
{
    /// <summary>
    /// Generate a heightfield synchronously. Returns height values in the
    /// same units as <c>FjordTerrain._generate_heightmap</c>
    /// (fraction of <paramref name="maxHeight"/>, water level = 0).
    /// </summary>
    public float[] GenerateHeightmap(
        int resolution,
        int seed,
        float fjordWidth = 0.15f,
        float fjordDepth = 0.8f,
        float cliffSteepness = 2.0f,
        int islandCount = 4,
        float channelFloor = 0.10f)
    {
        var gen = new TerrainGenerator(seed)
        {
            TerrainResolution = resolution,
            FjordWidth = fjordWidth,
            FjordDepth = fjordDepth,
            CliffSteepness = cliffSteepness,
            IslandCount = islandCount,
            ChannelFloor = channelFloor,
        };
        return gen.GenerateHeightmap(resolution);
    }

    /// <summary>
    /// Recommended pen anchor positions along the fjord channel, in world
    /// metres (Y = 0, water level). Mirrors the C# channel-center math so
    /// GDScript pen placement agrees with the sim side.
    /// </summary>
    public Godot.Vector3[] GetRecommendedPenPositions(
        int maxPens,
        int seed,
        float terrainSizeX = 4000f,
        float terrainSizeY = 4000f)
    {
        var gen = new TerrainGenerator(seed)
        {
            TerrainSize = new LakskongenSim.Environment.Vector2(terrainSizeX, terrainSizeY),
        };
        var locs = gen.GetRecommendedLocations(maxPens);
        var out_ = new Godot.Vector3[locs.Count];
        for (int i = 0; i < locs.Count; i++)
            out_[i] = new Godot.Vector3(locs[i].X, 0f, locs[i].Z);
        return out_;
    }
}
