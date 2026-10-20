using LakskongenSim.Environment;

namespace LakskongenSim.Biology
{
    /// <summary>
    /// Compatibility names for <see cref="HoldingKind"/>, the one enum of what
    /// holds a population. S2-02 (<see cref="HoldingWater"/>) and S2-05 (this
    /// record) each introduced one; wave 2 sim-a-1 kept S2-02's
    /// <see cref="HoldingKind"/> and made these the S2-05 spellings of its
    /// values, so code and tests written against either still compile and
    /// compare equal (<c>FishHolding.LandBasedTank == HoldingKind.LandTank</c>).
    /// </summary>
    public static class FishHolding
    {
        /// <summary>An open or semi-closed net pen at a coastal sea site (<see cref="HoldingKind.SeaPen"/>).</summary>
        public const HoldingKind SeaPen = HoldingKind.SeaPen;

        /// <summary>A land-based tank, RAS or flow-through (<see cref="HoldingKind.LandTank"/>).</summary>
        public const HoldingKind LandBasedTank = HoldingKind.LandTank;

        /// <summary>An exposed offshore structure (<see cref="HoldingKind.Offshore"/>).</summary>
        public const HoldingKind OffshoreStructure = HoldingKind.Offshore;
    }

    /// <summary>
    /// The water and exposure a cohort lives in today, as the mortality hazards
    /// (<see cref="MortalityHazards"/>) read it. It is built from the one source
    /// of truth for the water at the fish, the population's
    /// <see cref="HoldingWater"/> (<c>SimulationEngine.HoldingWaterOf</c>), plus
    /// only what the hazards need beyond the water: the holding's id (the
    /// ledger's site key), dissolved CO2 and the wave height
    /// (<c>SimulationEngine.ResolveCohortEnvironment</c>). A sea pen passes its
    /// zone's water (and an in-cage oxygen sag, if one is running), a land
    /// tank its own controlled water, an offshore structure its area's sea plus
    /// its wave exposure. A new farm type adds hazards (power, O2, CO2 or H2S
    /// failure in a tank; storm exposure offshore) by filling these fields and
    /// booking a new <see cref="MortalityCause"/>, without reading a global
    /// zone value. Unknown readings are <see cref="double.NaN"/>.
    /// </summary>
    /// <param name="Holding">What holds the fish.</param>
    /// <param name="HoldingId">Site, facility or farm id (the ledger's site key).</param>
    /// <param name="WaterTemperatureC">Water temperature at the fish (°C).</param>
    /// <param name="OxygenMgL">Dissolved oxygen at the fish (mg/L), NaN when not modelled.</param>
    /// <param name="Co2MgL">Dissolved CO2 (mg/L), NaN when not modelled (sea water).</param>
    /// <param name="WaveHeightM">Significant wave height at the holding (m).</param>
    public readonly record struct CohortEnvironment(
        HoldingKind Holding,
        string HoldingId,
        double WaterTemperatureC,
        double OxygenMgL,
        double Co2MgL,
        double WaveHeightM)
    {
        /// <summary>
        /// The environment of a cohort in <paramref name="water"/>: its kind,
        /// temperature and oxygen come from the water, the rest from the
        /// holding. This is the only way the engine builds one, so the hazards
        /// and the growth, feeding and disease models never disagree about the
        /// water the fish are in.
        /// </summary>
        public static CohortEnvironment From(in HoldingWater water, string holdingId, double co2MgL, double waveHeightM)
            => new(water.Kind, holdingId ?? "", water.TemperatureC, water.OxygenMgPerL, co2MgL, waveHeightM);
    }
}
