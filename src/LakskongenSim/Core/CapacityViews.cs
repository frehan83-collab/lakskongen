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
}

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
