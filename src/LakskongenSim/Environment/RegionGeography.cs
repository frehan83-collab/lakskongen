using LakskongenSim.Data;

namespace LakskongenSim.Environment
{
    /// <summary>
    /// Where each production region lies, for the sky (G1-04, D20): the sun's
    /// path, polar night, midnight sun and the aurora follow the latitude of
    /// the scenario's region. One representative coastal town per region.
    /// </summary>
    public static class RegionGeography
    {
        /// <summary>Latitude (degrees north) of a region's representative town.</summary>
        public static double LatitudeDeg(ProductionRegion region) => region switch
        {
            ProductionRegion.Sørlandet => 58.15,     // Kristiansand
            ProductionRegion.Rogaland => 58.97,      // Stavanger
            ProductionRegion.Vestland => 60.39,      // Bergen
            ProductionRegion.MøreOgRomsdal => 62.47, // Ålesund
            ProductionRegion.Trøndelag => 63.43,     // Trondheim
            ProductionRegion.Nordland => 67.28,      // Bodø
            ProductionRegion.Troms => 69.65,         // Tromsø
            ProductionRegion.Finnmark => 70.66,      // Hammerfest
            _ => 60.39,
        };
    }
}
