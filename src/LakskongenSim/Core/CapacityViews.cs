using System.Collections.Generic;
using LakskongenSim.Finance;

namespace LakskongenSim.Core;

/// <summary>Stable reason keys of the capacity checks (S2-01a); the UI maps them to translation keys CAPACITY_REASON_*.</summary>
public static class CapacityReasons
{
    /// <summary>The check passed.</summary>
    public const string None = "";

    /// <summary>The plan's peak would exceed the area's licence MTB.</summary>
    public const string AreaProjectedPeak = "mtb_area_projected_peak";

    /// <summary>The plan's peak would exceed the site's locality MTB.</summary>
    public const string SiteProjectedPeak = "mtb_site_projected_peak";

    /// <summary>A reduction order runs in the area: no stocking until it closes.</summary>
    public const string OrderOpen = "mtb_order_open";

    /// <summary>The company holds no licence in the site's production area.</summary>
    public const string NoLicence = "mtb_no_licence";

    /// <summary>S2-01b: the stocking would put more fish in one pen than § 47a allows (200,000).</summary>
    public const string PenFishLimit = "pen_fish_limit";

    /// <summary>S2-01b: the pen's projected peak density would exceed § 25's 25 kg/m³.</summary>
    public const string PenDensity = "pen_density";

    /// <summary>S2-01b: the pen already holds fish (one stocking, one year class per pen).</summary>
    public const string PenOccupied = "pen_occupied";

    /// <summary>S2-01b: the site has no such pen.</summary>
    public const string PenNotFound = "pen_not_found";

    /// <summary>S2-01b: the site's empty pens cannot hold that many fish under the per-pen limits.</summary>
    public const string PensFull = "pens_full";
}

/// <summary>
/// What stocking a count of smolts in one pen would do (S2-01b): the per-pen
/// limits (§ 47a fish, § 25 projected density on the S2-03 growth law) and
/// the capacity check of S2-01a. The same checks
/// <c>SimulationEngine.StockSmoltsInPen</c> runs.
/// </summary>
/// <param name="Allowed">True when the stocking passes every check.</param>
/// <param name="ReasonKey">A <see cref="CapacityReasons"/> key; empty when allowed.</param>
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

/// <summary>One production area of the company: its licences, its standing biomass and the plan's peak.</summary>
/// <param name="ProductionArea">Area number (1-13; 0 = national reference).</param>
/// <param name="Code">"PO3"; "" for the national reference.</param>
/// <param name="Licences">Licences pooled in the area.</param>
/// <param name="LicenceMtbTonnes">The area's biomass cap (t).</param>
/// <param name="StandingTonnes">Biomass standing in the area's sea sites today (t).</param>
/// <param name="HeadroomTonnes">Cap minus standing biomass (t; negative when over).</param>
/// <param name="ProjectedPeakTonnes">Peak standing biomass of today's fish on the production plan (t).</param>
/// <param name="ProjectedPeakDay">Sim day of that peak.</param>
/// <param name="BreachOpen">True while a reduction order runs in the area.</param>
/// <param name="OrderDeadlineDay">The open order's deadline (-1 when none).</param>
public sealed record AreaCapacityView(int ProductionArea, string Code, int Licences, double LicenceMtbTonnes,
    double StandingTonnes, double HeadroomTonnes, double ProjectedPeakTonnes, int ProjectedPeakDay,
    bool BreachOpen, int OrderDeadlineDay);

/// <summary>One sea site: its locality MTB and biomass.</summary>
/// <param name="SiteId">Site id.</param>
/// <param name="Label">The name the player reads.</param>
/// <param name="ProductionArea">Area the site lies in.</param>
/// <param name="LocalityMtbTonnes">The site's own cap from its locality clearance (t).</param>
/// <param name="StandingTonnes">Biomass at the site today (t).</param>
/// <param name="HeadroomTonnes">What the site can still take today: the lesser of its own and its area's headroom (t).</param>
/// <param name="ProjectedPeakTonnes">The site's peak on the production plan (t).</param>
/// <param name="LicencesRegistered">Licences registered to the locality (informational).</param>
public sealed record SiteCapacityView(string SiteId, string Label, int ProductionArea, double LocalityMtbTonnes,
    double StandingTonnes, double HeadroomTonnes, double ProjectedPeakTonnes, int LicencesRegistered);

/// <summary>The company's capacity picture for the expansion flow and the regulation hub (S2-01a).</summary>
public sealed class CapacityOverview
{
    /// <summary>The rules the game runs under.</summary>
    public CapacityRegime Regime { get; init; }

    /// <summary>One row per production area with a site or a licence, by area number.</summary>
    public IReadOnlyList<AreaCapacityView> Areas { get; init; } = new List<AreaCapacityView>();

    /// <summary>One row per sea site, by id.</summary>
    public IReadOnlyList<SiteCapacityView> Sites { get; init; } = new List<SiteCapacityView>();

    /// <summary>The licence register.</summary>
    public IReadOnlyList<LicenceHolding> Licences { get; init; } = new List<LicenceHolding>();

    /// <summary>Open and recent breach episodes.</summary>
    public IReadOnlyList<CapacityBreach> Breaches { get; init; } = new List<CapacityBreach>();
}

/// <summary>What a stocking would do to the caps (S2-01a): the same check <c>StockSmolts</c> runs.</summary>
/// <param name="Allowed">True when the stocking passes every capacity check.</param>
/// <param name="ReasonKey">A <see cref="CapacityReasons"/> key; empty when allowed.</param>
/// <param name="Message">Human-readable outcome (English, logs).</param>
/// <param name="ProductionArea">The site's area.</param>
/// <param name="AreaMtbTonnes">The area's licence MTB (t).</param>
/// <param name="AreaProjectedPeakTonnes">The area's plan peak with the stocking (t).</param>
/// <param name="SiteMtbTonnes">The site's locality MTB (t).</param>
/// <param name="SiteProjectedPeakTonnes">The site's plan peak with the stocking (t).</param>
/// <param name="PeakDay">Sim day of the area's plan peak.</param>
/// <param name="MaxCount">Most smolts of this strain the site can take now (0 when none).</param>
public sealed record StockingCapacityPreview(bool Allowed, string ReasonKey, string Message, int ProductionArea,
    double AreaMtbTonnes, double AreaProjectedPeakTonnes, double SiteMtbTonnes, double SiteProjectedPeakTonnes,
    int PeakDay, int MaxCount);

/// <summary>What building a site in a zone costs and adds (S2-01a; U2-01's "Bygg lokalitet" card).</summary>
/// <param name="Allowed">The build gate's verdict (<c>CanBuildSeaSite</c>) and enough cash.</param>
/// <param name="ReasonKey">"" when allowed; "growth_licence_required", "insufficient_funds" or "zone_not_found".</param>
/// <param name="Reason">Human-readable reason (English).</param>
/// <param name="CostNok">Construction cost.</param>
/// <param name="ProductionArea">The zone's area.</param>
/// <param name="LocalityMtbTonnes">The new site's own cap (t).</param>
/// <param name="LicenceMtbAddedTonnes">Licence MTB the build adds: always 0.</param>
/// <param name="AreaLicenceMtbTonnes">The area's licence MTB, before and after the build (t).</param>
/// <param name="Pens">Pens the new site gets.</param>
public sealed record SiteBuildPreview(bool Allowed, string ReasonKey, string Reason, decimal CostNok,
    int ProductionArea, double LocalityMtbTonnes, double LicenceMtbAddedTonnes, double AreaLicenceMtbTonnes, int Pens);
