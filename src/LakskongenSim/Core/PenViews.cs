namespace LakskongenSim.Core;

/// <summary>
/// Stable reason keys of the per-pen stocking checks (S2-01b). The UI maps
/// them, like <see cref="CapacityReasons"/>, to translation keys
/// CAPACITY_REASON_*.
/// </summary>
public static class PenReasons
{
    /// <summary>The stocking would put more fish in one pen than § 47a allows (200,000).</summary>
    public const string FishLimit = "pen_fish_limit";

    /// <summary>The pen's projected peak density would exceed § 25's 25 kg/m³.</summary>
    public const string Density = "pen_density";

    /// <summary>The pen already holds fish (one stocking, one year class per pen).</summary>
    public const string Occupied = "pen_occupied";

    /// <summary>The site has no such pen.</summary>
    public const string NotFound = "pen_not_found";

    /// <summary>The site's empty pens cannot hold that many fish under the per-pen limits.</summary>
    public const string PensFull = "pens_full";
}

/// <summary>
/// What stocking a count of smolts in one pen would do (S2-01b): the per-pen
/// limits (§ 47a fish, § 25 projected density on the S2-03 growth law) and
/// the capacity check of S2-01a. The same checks
/// <c>SimulationEngine.StockSmoltsInPen</c> runs.
/// </summary>
/// <param name="Allowed">True when the stocking passes every check.</param>
/// <param name="ReasonKey">A <see cref="PenReasons"/> or <see cref="CapacityReasons"/> key; empty when allowed.</param>
/// <param name="Message">Human-readable outcome (English, logs).</param>
/// <param name="PenId">The pen's id ("" when not found).</param>
/// <param name="PenNumber">The pen's number (0 when not found).</param>
/// <param name="VolumeM3">The pen's volume (m³).</param>
/// <param name="MaxFishPerPen">The fish limit of one pen in force (§ 47a under capacity v1; int.MaxValue in an unregulated game).</param>
/// <param name="DensityLimitKgPerM3">The density limit (§ 25).</param>
/// <param name="ProjectedPeakDensityKgPerM3">The plan's peak density with this count (kg/m³).</param>
/// <param name="ProjectedPeakDay">Sim day of that peak.</param>
/// <param name="ProjectedPeakWeightKg">Mean weight on that day (kg).</param>
/// <param name="MaxCount">Most smolts of this strain the pen can take now under every check (0 when none).</param>
public sealed record PenStockingPreview(bool Allowed, string ReasonKey, string Message, string PenId, int PenNumber,
    double VolumeM3, int MaxFishPerPen, double DensityLimitKgPerM3, double ProjectedPeakDensityKgPerM3,
    int ProjectedPeakDay, double ProjectedPeakWeightKg, int MaxCount);

/// <summary>One pen of a site as the game reads it (S2-01b).</summary>
/// <param name="PenId">"po3_nord/p03".</param>
/// <param name="Number">Position in the mooring frame, from 1.</param>
/// <param name="PopulationId">The population in the pen; "" when empty.</param>
/// <param name="FishCount">Live fish.</param>
/// <param name="BiomassKg">Standing biomass (kg).</param>
/// <param name="MeanWeightKg">Mean live weight (kg; 0 when empty).</param>
/// <param name="WeightCv">Size spread of the pen's fish (S2-03).</param>
/// <param name="LicePerFish">The population's lice load (per fish).</param>
/// <param name="DensityKgPerM3">Biomass over the pen's volume.</param>
/// <param name="VolumeM3">The pen's volume (m³).</param>
/// <param name="CircumferenceM">Collar circumference (m).</param>
/// <param name="NetDepthM">Net depth, wall plus cone (m).</param>
/// <param name="Ration">Fraction of appetite the pen is fed (S2-03).</param>
/// <param name="Fasting">True while the pen's fish fast before slaughter (S2-04).</param>
public sealed record PenView(string PenId, int Number, string PopulationId, int FishCount, double BiomassKg,
    double MeanWeightKg, double WeightCv, double LicePerFish, double DensityKgPerM3, double VolumeM3,
    double CircumferenceM, double NetDepthM, double Ration, bool Fasting);
