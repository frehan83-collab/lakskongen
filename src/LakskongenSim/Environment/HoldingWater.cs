namespace LakskongenSim.Environment
{
    /// <summary>
    /// Where a population's water comes from (roadmap S2-02, the farm-type seam).
    /// </summary>
    public enum HoldingKind
    {
        /// <summary>An open or semi-closed sea pen at a coastal site: zone water.</summary>
        SeaPen = 0,
        /// <summary>An offshore farm in exposed water (Ocean Farm 1, Havfarm class): sea water of its area.</summary>
        Offshore = 1,
        /// <summary>A land-based tank (RAS or flow-through) with its own controlled water.</summary>
        LandTank = 2,
    }

    /// <summary>
    /// The water a population lives in today: the input the daily growth,
    /// appetite, feeding, temperature-stress mortality and lice models take
    /// (roadmap S2-02). The models never read a zone or the weather themselves;
    /// whatever holds the fish (sea pen, offshore hull, land tank) supplies this
    /// value through <c>SimulationEngine.HoldingWaterOf</c>, so the land-based and
    /// offshore farm types plug in by supplying their own water.
    /// </summary>
    /// <param name="TemperatureC">Water temperature at cage or tank depth (°C).</param>
    /// <param name="OxygenMgPerL">Dissolved oxygen (mg/L).</param>
    /// <param name="SalinityPsu">Salinity (PSU).</param>
    /// <param name="ProductionArea">Production area (1-13) the water belongs to; land tanks report the area of their seawater intake.</param>
    /// <param name="Kind">What holds the fish.</param>
    public readonly record struct HoldingWater(
        double TemperatureC,
        double OxygenMgPerL,
        double SalinityPsu,
        int ProductionArea,
        HoldingKind Kind)
    {
        /// <summary>Full-strength seawater (PSU), the salinity sea pens and offshore farms see.</summary>
        public const double SeawaterSalinityPsu = 35.0;

        /// <summary>True when the fish meet the open sea: sea lice, algae, jellyfish and storms reach them.</summary>
        public bool OpenToSea => Kind != HoldingKind.LandTank;
    }
}
