namespace LakskongenSim.Core
{
    /// <summary>
    /// Enumerations used across the simulation.
    /// </summary>
    public static class Enums
    {
        // Kept as static holder to avoid scattering small enums files.
    }

    /// <summary>Represents life stage of fish cohorts.</summary>
    public enum LifeStage
    {
        EyedEgg,
        Smolt,
        Juvenile,
        Adult,
        Harvestable
    }

    /// <summary>Types of products produced by farms.</summary>
    public enum ProductType
    {
        FreshWhole,
        Fillet,
        Frozen
    }
}