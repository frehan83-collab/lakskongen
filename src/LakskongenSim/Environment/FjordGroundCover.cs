namespace LakskongenSim.Environment
{
    /// <summary>
    /// What grows and stands on the fjord's ground (G2-02, F2-19), composed
    /// once for the renderer (<c>TerrainGeneratorBridge.Build</c>) and the
    /// tests alike: the vegetation masks, the shore buildings and the trees,
    /// all kept off the land pads and their rims
    /// (<see cref="TerrainGenerator.PadClearings"/>).
    /// </summary>
    /// <param name="Settings">The vegetation settings the masks and trees used.</param>
    /// <param name="Masks">The vegetation masks (the terrain mesh's vertex colours).</param>
    /// <param name="Props">The naust rows and the quay.</param>
    /// <param name="Trees">The trees.</param>
    /// <param name="Cleared">The ground nothing grows or stands on (the pads with their rims).</param>
    public sealed record FjordGroundCover(VegetationSettings Settings, VegetationMasks Masks, List<ShoreProp> Props,
        List<TreeInstance> Trees, IReadOnlyList<GroundRect> Cleared)
    {
        /// <summary>
        /// The ground cover of <paramref name="generator"/>'s heightfield
        /// <paramref name="heights"/> with its water bake: masks from the
        /// heights and the open-sea exposure, shore props on sheltered gentle
        /// shore, trees inside the masks clear of the props' yards; nothing on
        /// a pad or its rim. Deterministic (position hashes, no RNG stream).
        /// </summary>
        public static FjordGroundCover Build(TerrainGenerator generator, float[] heights, WaterBakeResult water)
        {
            var settings = generator.Settings;
            int n = settings.Resolution;
            var exposure = new float[n * n];
            var shoreDistance = new float[n * n];
            for (int i = 0; i < exposure.Length; i++)
            {
                shoreDistance[i] = water.Rgba[i * 4 + 1];
                exposure[i] = water.Rgba[i * 4 + 2];
            }
            var cleared = generator.PadClearings();
            var vegetation = new VegetationSettings { Seed = settings.Seed };
            var masks = VegetationScatter.BuildMasks(heights, n, settings.WorldSizeM, vegetation, exposure, cleared);
            var props = ShorePropPlacer.Place(heights, n, settings.WorldSizeM, shoreDistance, masks.LandExposure, null, cleared);
            var trees = VegetationScatter.Scatter(heights, masks, vegetation, ShorePropPlacer.Clearings(props), cleared);
            return new FjordGroundCover(vegetation, masks, props, trees, cleared);
        }
    }
}
