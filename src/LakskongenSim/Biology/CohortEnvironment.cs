namespace LakskongenSim.Biology
{
    /// <summary>What holds a population's fish (S2-05: hazards read the holding, not a global zone value).</summary>
    public enum FishHolding
    {
        /// <summary>An open or semi-closed net pen at a coastal sea site.</summary>
        SeaPen,
        /// <summary>A land-based tank (RAS or flow-through): its own controlled water, pumped seawater, no lice.</summary>
        LandBasedTank,
        /// <summary>An exposed offshore structure (Ocean Farm 1-class semi-submersible, Havfarm-class ship).</summary>
        OffshoreStructure,
    }

    /// <summary>
    /// The water and exposure a cohort lives in today, resolved from whatever
    /// holds it (<c>SimulationEngine.ResolveCohortEnvironment</c>). This is the
    /// seam between the mortality hazards (<see cref="MortalityHazards"/>) and
    /// the farm types: a sea pen passes the sea and its zone's water, a
    /// land-based tank its own controlled water, an offshore structure the sea
    /// plus its wave exposure. A hazard reads only this record, so a new farm
    /// type adds hazards (power, O2, CO2 or H2S failure in a tank; storm
    /// exposure offshore) by filling these fields and booking a new
    /// <see cref="MortalityCause"/>, without reading a global zone value.
    /// Unknown readings are <see cref="double.NaN"/>.
    /// </summary>
    /// <param name="Holding">What holds the fish.</param>
    /// <param name="HoldingId">Site, facility or farm id (the ledger's site key).</param>
    /// <param name="WaterTemperatureC">Water temperature at the fish (°C).</param>
    /// <param name="OxygenMgL">Dissolved oxygen at the fish (mg/L), NaN when not modelled.</param>
    /// <param name="Co2MgL">Dissolved CO2 (mg/L), NaN when not modelled (sea water).</param>
    /// <param name="WaveHeightM">Significant wave height at the holding (m).</param>
    public readonly record struct CohortEnvironment(
        FishHolding Holding,
        string HoldingId,
        double WaterTemperatureC,
        double OxygenMgL,
        double Co2MgL,
        double WaveHeightM);
}
