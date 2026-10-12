using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LakskongenSim.Environment;

namespace LakskongenSim.Environment
{
    /// <summary>
    /// C# terrain generator using FastNoise with ridged fractal and domain warp.
    /// Generates heightmaps asynchronously for better performance.
    /// </summary>
    public sealed class TerrainGenerator
    {
        private readonly FastNoise _baseNoise;
        private readonly FastNoise _islandNoise;
        private readonly FastNoise _inletNoise;
        
        // Terrain parameters (matching GDScript FjordTerrain)
        public float FjordWidth { get; set; } = 0.15f;
        public float FjordDepth { get; set; } = 0.8f;
        public float CliffSteepness { get; set; } = 2.0f;
        public int IslandCount { get; set; } = 4;
        public float ChannelFloor { get; set; } = 0.10f;
        public int TerrainSeed { get; set; } = 42;
        public int TerrainResolution { get; set; } = 256;
        public Vector2 TerrainSize { get; set; } = new Vector2(4000f, 4000f);
        public float MaxHeight { get; set; } = 400f;
        public float WaterLevel { get; set; } = 0f;

        public TerrainGenerator(int seed = 42)
        {
            TerrainSeed = seed;
            uint seedVal = (uint)seed;
            
            // Base terrain noise (ridged fractal with domain warp)
            _baseNoise = new FastNoise(
                seed: seedVal,
                fractalOctaves: 4,
                frequency: 1.0f,
                lacunarity: 2.0f,
                gain: 0.5f,
                warpAmplitude: 30.0f,
                warpFrequency: 0.5f,
                ridgedOffset: 1.0f,
                ridgedGain: 0.5f
            );
            
            // Island noise
            _islandNoise = new FastNoise(
                seed: seedVal + 1000,
                fractalOctaves: 3,
                frequency: 0.5f,
                lacunarity: 2.0f,
                gain: 0.5f
            );
            
            // Inlet noise
            _inletNoise = new FastNoise(
                seed: seedVal + 2000,
                fractalOctaves: 3,
                frequency: 0.3f,
                lacunarity: 2.0f,
                gain: 0.5f
            );
        }

        /// <summary>
        /// Generate heightmap asynchronously.
        /// Returns height values in [-1, 1] range (water level = 0).
        /// </summary>
        public async Task<float[]> GenerateHeightmapAsync(int resolution = -1)
        {
            int res = resolution > 0 ? resolution : TerrainResolution;
            var heightmap = new float[res * res];
            
            // Parallel generation for better performance
            await Task.Run(() =>
            {
                Parallel.For(0, res, z =>
                {
                    // NOTE: normalized by res (not res-1) to match
                    // FjordTerrain.gd exactly, so the sim-side channel,
                    // fairway clamp and island math agree cell-for-cell
                    // with the visual heightfield.
                    float nz = (z / (float)res - 0.5f) * 2.0f;

                    for (int x = 0; x < res; x++)
                    {
                        float nx = (x / (float)res - 0.5f) * 2.0f;
                        float height = GenerateHeightAt(nx, nz);
                        heightmap[z * res + x] = height;
                    }
                });
            });
            
            return heightmap;
        }

        /// <summary>
        /// Generate heightmap synchronously (for testing).
        /// </summary>
        public float[] GenerateHeightmap(int resolution = -1)
        {
            int res = resolution > 0 ? resolution : TerrainResolution;
            var heightmap = new float[res * res];
            
            for (int z = 0; z < res; z++)
            {
                // Same res-normalization as the parallel path above.
                float nz = (z / (float)res - 0.5f) * 2.0f;
                for (int x = 0; x < res; x++)
                {
                    float nx = (x / (float)res - 0.5f) * 2.0f;
                    heightmap[z * res + x] = GenerateHeightAt(nx, nz);
                }
            }
            
            return heightmap;
        }

        private float GenerateHeightAt(float nx, float nz)
        {
            float height = 0.0f;

            // Base landmass (raised around edges, low in center for fjord)
            float edgeDist = Math.Max(Math.Abs(nx), Math.Abs(nz));
            height += SmoothStep(0.3f, 0.9f, edgeDist) * 0.7f;

            // Main fjord channel (runs roughly through center, Z-axis)
            float fjordCenter = MathF.Sin(nz * 2.0f) * 0.1f;
            float distToFjord = Math.Abs(nx - fjordCenter);
            float fjordFactor = SmoothStep(FjordWidth, FjordWidth * 0.3f, distToFjord);
            height -= fjordFactor * FjordDepth;

            // Cliff walls (steep transition over a wider valley)
            float cliffZone = SmoothStep(FjordWidth * 0.3f, FjordWidth * 1.6f, distToFjord);
            height += MathF.Pow(cliffZone, 1.0f / CliffSteepness) * 0.3f;

            // Secondary inlet (branching fjord)
            float inletX = nz * 0.5f + 0.2f;
            float inletDist = Math.Abs(nx - inletX) + Math.Abs(nz - 0.3f) * 0.3f;
            float inletFactor = SmoothStep(0.15f, 0.05f, inletDist);
            height -= inletFactor * FjordDepth * 0.6f;

            // Noise for natural variation
            float noiseVal = _baseNoise.GetNoise(nx * 3.0f, nz * 3.0f);
            height += noiseVal * 0.15f;

            // Islands (positions are pure functions of the index — no RNG
            // state, so per-pixel generation stays allocation-free and
            // bit-deterministic under Parallel.For).
            for (int i = 0; i < IslandCount; i++)
            {
                // Deterministic island positions
                float islandX = MathF.Sin(i * 2.3f + 0.5f) * 0.4f;
                float islandZ = MathF.Cos(i * 1.7f + 0.3f) * 0.4f;
                float islandDist = MathF.Sqrt((nx - islandX) * (nx - islandX) + (nz - islandZ) * (nz - islandZ));
                float islandRadius = 0.05f + (i % 3) * 0.02f;
                float islandHeight = SmoothStep(islandRadius, islandRadius * 0.3f, islandDist) * 0.3f;
                height += islandHeight;
            }

            // Fairway floor: the navigable channel core must stay deep enough
            if (distToFjord < FjordWidth * 0.35f)
            {
                height = Math.Min(height, -ChannelFloor);
            }

            // Beaches (gentle slope near water level)
            if (height > -0.05f && height < 0.1f)
            {
                height = Lerp(height, 0.02f, 0.3f);
            }

            return height;
        }

        private float Lerp(float a, float b, float t) => a + (b - a) * t;

        private float SmoothStep(float edge0, float edge1, float x)
        {
            float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// Get deterministic island position for a given island index.
        /// </summary>
        public (float x, float z) GetIslandPosition(int index)
        {
            float islandX = MathF.Sin(index * 2.3f + 0.5f) * 0.4f;
            float islandZ = MathF.Cos(index * 1.7f + 0.3f) * 0.4f;
            return (islandX, islandZ);
        }

        /// <summary>
        /// Get recommended pen locations along the fjord channel.
        /// </summary>
        public List<Vector3> GetRecommendedLocations(int maxPens = 12)
        {
            var locations = new List<Vector3>();
            float fjordLength = TerrainSize.Y;
            float startZ = -fjordLength * 0.4f;

            for (int i = 0; i < maxPens; i++)
            {
                float progress = maxPens > 1 ? (float)i / (maxPens - 1) : 0.5f;
                float z = startZ + progress * fjordLength * 0.8f;

                // TRUE channel center, mirroring FjordTerrain's generator
                float nz = (z + TerrainSize.Y / 2.0f) / TerrainSize.Y * 2.0f - 1.0f;
                float fjordCenter = MathF.Sin(nz * 2.0f) * 0.1f;
                float x = fjordCenter * TerrainSize.X / 2.0f;

                // Find deep water
                float bestDepth = -999f;
                Vector3 bestPos = new Vector3(fjordCenter * TerrainSize.X / 2.0f, 0, z);
                
                for (int offsetIdx = 0; offsetIdx < 7; offsetIdx++)
                {
                    float[] offsets = { -250, -150, -75, 0, 75, 150, 250 };
                    float testX = fjordCenter * TerrainSize.X / 2.0f + offsets[offsetIdx];
                    float nx = (testX + TerrainSize.X / 2.0f) / TerrainSize.X * 2.0f - 1.0f;
                    // nz already computed above for this z
                    
                    float height = GenerateHeightAt(nx, nz);
                    float depth = -height * MaxHeight; // Convert to meters
                    
                    if (depth > 20 && depth < 100 && depth > bestDepth)
                    {
                        bestDepth = depth;
                        bestPos = new Vector3(testX, 0, z);
                    }
                }
                locations.Add(bestPos);
            }
            return locations;
        }
    }
}