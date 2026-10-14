namespace LakskongenSim.Environment
{
    /// <summary>
    /// What holds a population and where its water comes from (roadmap S2-02,
    /// the farm-type seam). The one holding enum: the mortality hazards'
    /// <c>CohortEnvironment.Holding</c> is this type too, and
    /// <c>Biology.FishHolding</c> keeps S2-05's names for its values. The engine
    /// resolves it once per simulated step (<c>SimulationEngine.HoldingOf</c>).
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
    /// offshore farm types plug in by supplying their own water. It is the one
    /// source of truth for the water at the fish: the mortality hazards'
    /// <c>CohortEnvironment</c> is built from it (wave 2, sim-a-1).
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
