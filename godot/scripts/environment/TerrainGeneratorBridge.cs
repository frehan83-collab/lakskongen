using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
    /// distance, exposure, seabed slope), sheltered_fetch_m and sea_direction;
    /// and for the vegetation (G2-02) the masks baked into the chunk colours
    /// (<see cref="VegetationMasks"/>), vegetation (an Array of chunk
    /// Dictionaries, <see cref="VegetationChunks"/>), tree_count,
    /// treeline_m and shore_props (<see cref="ShorePropsToGodot"/>); and for
    /// the far field (G2-01) far_field (chunk Dictionaries in the same format,
    /// each with its radial band, <see cref="FarFieldMesh"/>) and
    /// far_field_extent (m).
    /// </summary>
    public Godot.Collections.Dictionary Build()
    {
        var watch = Stopwatch.StartNew();
        var settings = _generator.Settings;
        int n = settings.Resolution;
        float[] heights = _generator.GenerateHeightmap();
        var water = WaterBake.Build(heights, n, settings.WorldSizeM, 0f,
            settings.PrevailingSeaDirX, settings.PrevailingSeaDirZ);
        var exposure = new float[n * n];
        for (int i = 0; i < exposure.Length; i++)
            exposure[i] = water.Rgba[i * 4 + 2];
        var vegetation = new VegetationSettings { Seed = settings.Seed };
        var masks = VegetationScatter.BuildMasks(heights, n, settings.WorldSizeM, vegetation, exposure);
        var shoreDistance = new float[n * n];
        for (int i = 0; i < shoreDistance.Length; i++)
            shoreDistance[i] = water.Rgba[i * 4 + 1];
        var props = ShorePropPlacer.Place(heights, n, settings.WorldSizeM, shoreDistance, masks.LandExposure);
        var trees = VegetationScatter.Scatter(heights, masks, vegetation, ShorePropPlacer.Clearings(props));
        var mesh = TerrainMesh.Build(heights, n, settings.WorldSizeM, ChunkQuads, masks, _generator.FarFieldHeightAt);
        var far = FarFieldMesh.Build(_generator, heights, masks);

        var chunks = new Godot.Collections.Array();
        foreach (var chunk in mesh.Chunks)
            chunks.Add(ToGodot(chunk));
        var farChunks = new Godot.Collections.Array();
        for (int i = 0; i < far.Chunks.Count; i++)
        {
            var chunk = ToGodot(far.Chunks[i]);
            chunk["band"] = far.ChunkBands[i];
            farChunks.Add(chunk);
        }
        float spacing = settings.SpacingM;
        var colliderHeights = new float[n * n];
        for (int i = 0; i < colliderHeights.Length; i++)
            colliderHeights[i] = heights[i] / spacing;

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
            ["vegetation"] = VegetationChunks(trees, heights, n, settings.WorldSizeM),
            ["tree_count"] = trees.Count,
            ["shore_props"] = ShorePropsToGodot(props),
            ["treeline_m"] = vegetation.TreelineMeanM,
            ["far_field"] = farChunks,
            ["far_field_extent"] = TerrainGenerator.FarFieldExtentM,
        };
        result["build_ms"] = watch.Elapsed.TotalMilliseconds;
        return result;
    }

    /// <summary>The shore buildings: kind (0 naust, 1 quay), position, yaw (rad), variant.</summary>
    public static Godot.Collections.Array ShorePropsToGodot(List<ShoreProp> props)
    {
        var result = new Godot.Collections.Array();
        foreach (var p in props)
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["kind"] = (int)p.Kind,
                ["position"] = new Godot.Vector3(p.X, p.Y, p.Z),
                ["yaw"] = p.Yaw,
                ["variant"] = p.Variant,
            });
        }
        return result;
    }

    /// <summary>Side of the square cells the trees are grouped in (one MultiMesh per cell and species).</summary>
    public const float VegetationChunkM = 250f;
    /// <summary>Floats per instance in a MultiMesh buffer: a 3×4 transform plus custom data.</summary>
    public const int FloatsPerTree = 16;

    /// <summary>
    /// The trees grouped by <see cref="VegetationChunkM"/> cell and species,
    /// as ready MultiMesh buffers (Godot's layout: the transform's rows
    /// basis.x.x, basis.y.x, basis.z.x, origin.x, … then custom data). Each
    /// tree turns about Y by its yaw and scales uniformly; its base sinks into
    /// the slope (0.25 m plus 0.35 m per unit of rise over run) so the trunk
    /// never floats over the downhill side. Custom data: tint 0-1, a second
    /// variation 0-1, the scale, and the base altitude in metres.
    /// Keys per chunk: species (int), center (Vector3), count, buffer.
    /// </summary>
    public static Godot.Collections.Array VegetationChunks(List<TreeInstance> trees, float[] heights, int n, float worldSize)
    {
        float half = worldSize * 0.5f;
        float spacing = worldSize / (n - 1);
        int cells = (int)MathF.Ceiling(worldSize / VegetationChunkM);
        var groups = new Dictionary<(int, int, TreeSpecies), List<TreeInstance>>();
        foreach (var t in trees)
        {
            int cx = Math.Clamp((int)((t.X + half) / VegetationChunkM), 0, cells - 1);
            int cz = Math.Clamp((int)((t.Z + half) / VegetationChunkM), 0, cells - 1);
            var key = (cx, cz, t.Species);
            if (!groups.TryGetValue(key, out var list))
                groups[key] = list = new List<TreeInstance>();
            list.Add(t);
        }
        var result = new Godot.Collections.Array();
        foreach (var ((cx, cz, species), list) in groups.OrderBy(g => g.Key.Item2).ThenBy(g => g.Key.Item1).ThenBy(g => g.Key.Item3))
        {
            var buffer = new float[list.Count * FloatsPerTree];
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                float slope = VegetationScatter.SlopeTanAt(heights, n, spacing, half, t.X, t.Z);
                float sink = 0.25f + 0.35f * slope;
                float c = MathF.Cos(t.Yaw) * t.Scale, s = MathF.Sin(t.Yaw) * t.Scale;
                int o = i * FloatsPerTree;
                // Rotation about +Y: basis.x = (c, 0, -s), basis.y = (0, k, 0), basis.z = (s, 0, c).
                buffer[o] = c; buffer[o + 1] = 0f; buffer[o + 2] = s; buffer[o + 3] = t.X;
                buffer[o + 4] = 0f; buffer[o + 5] = t.Scale; buffer[o + 6] = 0f; buffer[o + 7] = t.Y - sink;
                buffer[o + 8] = -s; buffer[o + 9] = 0f; buffer[o + 10] = c; buffer[o + 11] = t.Z;
                buffer[o + 12] = t.Tint;
                buffer[o + 13] = (t.Yaw * 2.3f) % 1f;
                buffer[o + 14] = t.Scale;
                buffer[o + 15] = t.Y;
            }
            result.Add(new Godot.Collections.Dictionary
            {
                ["species"] = (int)species,
                ["center"] = new Godot.Vector3(-half + (cx + 0.5f) * VegetationChunkM, 0f, -half + (cz + 0.5f) * VegetationChunkM),
                ["count"] = list.Count,
                ["buffer"] = buffer,
            });
        }
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

    /// <summary>Height in metres at any world (x, z) out to the far field (G2-01).</summary>
    public float FarFieldHeightAt(float x, float z) => _generator.FarFieldHeightAt(x, z);

    /// <summary>Half-extent (m) of the far field round the origin.</summary>
    public float FarFieldExtent() => TerrainGenerator.FarFieldExtentM;

    /// <summary>
    /// The land pads reserved for land-based farms (farm-types spec §4): id,
    /// name, centre (Vector3, y = pad level), size (Vector2: x width, y depth
    /// along Z), lots, quay (Vector3) and intake_reach_m (computed).
    /// </summary>
    public Godot.Collections.Array LandPadSlots()
    {
        var result = new Godot.Collections.Array();
        foreach (var p in _generator.LandPadSlots())
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = p.Id,
                ["name"] = p.Name,
                ["centre"] = new Godot.Vector3(p.CentreX, p.PadHeightM, p.CentreZ),
                ["size"] = new Godot.Vector2(p.WidthM, p.DepthM),
                ["lots"] = p.Lots,
                ["quay"] = new Godot.Vector3(p.QuayX, p.PadHeightM, p.QuayZ),
                ["intake_reach_m"] = _generator.IntakeReachM(p),
            });
        }
        return result;
    }

    /// <summary>
    /// The open-sea slots reserved for offshore units (spec §4): id, name,
    /// position (Vector3, y = 0), exposure and water_depth_m.
    /// </summary>
    public Godot.Collections.Array OffshoreSlots()
    {
        var result = new Godot.Collections.Array();
        foreach (var s in _generator.OffshoreSlots())
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = s.Id,
                ["name"] = s.Name,
                ["position"] = new Godot.Vector3(s.X, 0f, s.Z),
                ["exposure"] = s.Exposure,
                ["water_depth_m"] = s.WaterDepthM,
            });
        }
        return result;
    }

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
