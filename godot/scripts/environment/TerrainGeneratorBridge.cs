using System.Diagnostics;
using Godot;
using LakskongenSim.Environment;

/// <summary>
/// Godot face of <see cref="TerrainGenerator"/> (LakskongenSim): the one
/// fjord heightfield, shared by headless and windowed runs.
///
/// <see cref="Build"/> is pure compute (no scene-tree access), so
/// <c>FjordTerrain.gd</c> calls it on a worker <c>Thread</c> and only hands
/// the finished arrays to the GPU on the main thread. The point queries
/// (<see cref="HeightAt"/>, <see cref="CenterX"/>) read the immutable
/// generator and are safe from any thread.
/// </summary>
public partial class TerrainGeneratorBridge : RefCounted
{
    private TerrainGenerator _generator = new();

    /// <summary>Set the seed, world size (metres) and grid resolution.</summary>
    public void Configure(int seed, float worldSize, int resolution)
    {
        _generator = new TerrainGenerator(new FjordTerrainSettings
        {
            Seed = seed,
            WorldSizeM = worldSize,
            Resolution = resolution,
        });
    }

    /// <summary>
    /// Generate the heightfield and the chunked render arrays. Keys: heights
    /// (metres, row-major by Z), collider_heights (heights / spacing, for a
    /// HeightMapShape3D scaled by the spacing), chunks (an Array of
    /// Dictionaries with vertices, normals, tangents, uvs, colors, indices),
    /// hash, spacing, resolution, build_ms; and for the water (G1-05)
    /// water_bake (RGBA half floats, <see cref="WaterBake"/>: depth, shore
    /// distance, exposure, seabed slope), sheltered_fetch_m and sea_direction.
    /// </summary>
    public Godot.Collections.Dictionary Build()
    {
        var watch = Stopwatch.StartNew();
        var settings = _generator.Settings;
        int n = settings.Resolution;
        float[] heights = _generator.GenerateHeightmap();
        var mesh = TerrainMesh.Build(heights, n, settings.WorldSizeM, ChunkQuads);

        var chunks = new Godot.Collections.Array();
        foreach (var chunk in mesh.Chunks)
            chunks.Add(ToGodot(chunk));
        float spacing = settings.SpacingM;
        var colliderHeights = new float[n * n];
        for (int i = 0; i < colliderHeights.Length; i++)
            colliderHeights[i] = heights[i] / spacing;

        var water = WaterBake.Build(heights, n, settings.WorldSizeM, 0f,
            settings.PrevailingSeaDirX, settings.PrevailingSeaDirZ);

        var result = new Godot.Collections.Dictionary
        {
            ["water_bake"] = WaterBake.ToHalfBytes(water.Rgba),
            ["sheltered_fetch_m"] = water.ShelteredFetchM,
            ["sea_direction"] = new Godot.Vector2(settings.PrevailingSeaDirX, settings.PrevailingSeaDirZ).Normalized(),
            ["heights"] = heights,
            ["collider_heights"] = colliderHeights,
            ["chunks"] = chunks,
            ["hash"] = TerrainGenerator.HeightfieldHash(heights),
            ["spacing"] = spacing,
            ["resolution"] = n,
        };
        result["build_ms"] = watch.Elapsed.TotalMilliseconds;
        return result;
    }

    /// <summary>Quads per chunk side: 64 gives 8×8 chunks of 4,225 vertices for a 512 grid.</summary>
    private const int ChunkQuads = 64;

    private static Godot.Collections.Dictionary ToGodot(TerrainMesh.Chunk chunk)
    {
        int count = chunk.Columns * chunk.Rows;
        var vertices = new Godot.Vector3[count];
        var normals = new Godot.Vector3[count];
        var uvs = new Godot.Vector2[count];
        var colors = new Godot.Color[count];
        for (int i = 0; i < count; i++)
        {
            vertices[i] = new Godot.Vector3(chunk.Positions[i * 3], chunk.Positions[i * 3 + 1], chunk.Positions[i * 3 + 2]);
            normals[i] = new Godot.Vector3(chunk.Normals[i * 3], chunk.Normals[i * 3 + 1], chunk.Normals[i * 3 + 2]);
            uvs[i] = new Godot.Vector2(chunk.Uvs[i * 2], chunk.Uvs[i * 2 + 1]);
            colors[i] = new Godot.Color(chunk.Colors[i * 4], chunk.Colors[i * 4 + 1], chunk.Colors[i * 4 + 2], chunk.Colors[i * 4 + 3]);
        }
        return new Godot.Collections.Dictionary
        {
            ["vertices"] = vertices,
            ["normals"] = normals,
            ["tangents"] = chunk.Tangents,
            ["uvs"] = uvs,
            ["colors"] = colors,
            ["indices"] = chunk.Indices,
            ["first_column"] = chunk.FirstColumn,
            ["first_row"] = chunk.FirstRow,
        };
    }

    /// <summary>Heightfield hash for the configured settings (FNV-1a 64, hex).</summary>
    public string HeightfieldHash() => TerrainGenerator.HeightfieldHash(_generator.GenerateHeightmap());

    /// <summary>Analytic height in metres at world (x, z), before the grid exists.</summary>
    public float HeightAt(float x, float z) => _generator.HeightAt(x, z);

    /// <summary>World X of the channel centreline at world Z.</summary>
    public float CenterX(float z) => _generator.CenterX(z);

    /// <summary>True inside the steep fjord-wall band.</summary>
    public bool IsWallBand(float x, float z) => _generator.IsWallBand(x, z);

    /// <summary>Half-width of the fairway kept open through the skerries, in metres.</summary>
    public float FairwayHalfWidth() => _generator.Settings.FairwayHalfWidthM;

    /// <summary>World Z range (start, end) for pen slots along the basin.</summary>
    public Godot.Vector2 PenSlotRange()
    {
        var (start, end) = _generator.PenSlotRange();
        return new Godot.Vector2(start, end);
    }

    /// <summary>World Z of the sill (x) and of the fjord head (y).</summary>
    public Godot.Vector2 MouthAndHead() => new(_generator.Settings.MouthZ, _generator.Settings.HeadZ);

    /// <summary>
    /// Recommended pen anchor positions along the fjord channel, in world
    /// metres (Y = 0, water level).
    /// </summary>
    public Godot.Vector3[] GetRecommendedPenPositions(int maxPens)
    {
        var locs = _generator.GetRecommendedLocations(maxPens);
        var result = new Godot.Vector3[locs.Count];
        for (int i = 0; i < locs.Count; i++)
            result[i] = new Godot.Vector3(locs[i].X, 0f, locs[i].Z);
        return result;
    }
}
