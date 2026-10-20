using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Farm;
using LakskongenSim.Farm.Offshore;

namespace LakskongenSim.Core;

/// <summary>
/// Site kinds (roadmap F2-03, docs/FARM_TYPES_SPEC.md §3.1 decisions 1 and 4):
/// everything in the water is a <see cref="SeaSite"/> (a fjord locality or an
/// offshore unit) and everything on land a <see cref="RASFacility"/> (a
/// grow-out or a post-smolt land site). The site-kind audit
/// (<c>docs/run/evidence/f2_03_sitekind_audit.md</c>) decides for every
/// consumer of the site list whether it includes, excludes or hooks each
/// kind; the views and predicates it decides with live here, and the
/// per-kind rules of a sea site in the sea lane's hooks
/// (<see cref="OffshoreHooks"/>).
/// </summary>
public partial class SimulationEngine
{
    /// <summary>
    /// The watch mode an offshore unit carries: it is crewed on board, so it
    /// is neither the remote operations centre's "watch", "scan" nor "auto"
    /// (its feed spread is the default, as the pre-F2 offshore farm's was).
    /// </summary>
    public const string OffshoreWatchMode = "onboard";

    /// <summary>The company's fjord localities, in list order (the sea sites that are not offshore units).</summary>
    internal IEnumerable<SeaSite> FjordSites()
    {
        foreach (var site in State.SeaSites)
            if (site.Kind == SiteKind.Fjord)
                yield return site;
    }

    /// <summary>The company's offshore units (offshore sea sites), in list order.</summary>
    public IReadOnlyList<SeaSite> OffshoreUnits
        => State?.SeaSites == null ? Array.Empty<SeaSite>() : State.SeaSites.Where(s => s.Kind == SiteKind.Offshore).ToList();

    /// <summary>The company's land sites (grow-out and post-smolt), in list order.</summary>
    public IReadOnlyList<RASFacility> LandSites
        => State?.RASFacilities ?? (IReadOnlyList<RASFacility>)Array.Empty<RASFacility>();

    /// <summary>The company's post-smolt land sites, in list order.</summary>
    public IReadOnlyList<RASFacility> PostSmoltSites
        => State?.RASFacilities == null
            ? Array.Empty<RASFacility>()
            : State.RASFacilities.Where(f => f.Kind == LandSiteKind.PostSmolt).ToList();

    /// <summary>The offshore unit with <paramref name="siteId"/>, or null.</summary>
    public SeaSite? FindOffshoreUnit(string? siteId)
        => string.IsNullOrEmpty(siteId) ? null : State?.SeaSites?.FirstOrDefault(s => s.Id == siteId && s.Kind == SiteKind.Offshore);

    /// <summary>The land site with <paramref name="siteId"/> (grow-out or post-smolt), or null.</summary>
    public RASFacility? FindLandSite(string? siteId)
        => string.IsNullOrEmpty(siteId) ? null : State?.RASFacilities?.FirstOrDefault(f => f.Id == siteId);

    /// <summary>
    /// True when a sea site's fish count against its production area's
    /// pooled licence MTB (S2-01a): every fjord site; an offshore unit not
    /// until F2-09 binds its development licences (the sea lane's hook,
    /// <see cref="OffshoreHooks.CountsTowardAreaLicencePool"/>).
    /// </summary>
    internal bool CountsTowardAreaLicencePool(SeaSite site) => OffshoreHooks.CountsTowardAreaLicencePool(this, site);

    /// <summary>
    /// The multiplier on the external lice pressure at an offshore unit's
    /// plot (<see cref="SitePlot.LiceExternalMultiplier"/>); 1.0 for a site on
    /// no known plot.
    /// </summary>
    internal double PlotLiceMultiplierOf(SeaSite site)
    {
        var plot = FindPlot(site.PlotId);
        return plot != null && plot.Kind == PlotKind.OffshoreSlot ? plot.LiceExternalMultiplier : 1.0;
    }

    /// <summary>
    /// Attention points the remote operations centre spends on the sites
    /// that count toward the site limits (Phase 4.2 watch modes; F2-03: the
    /// hook <see cref="CountsTowardSiteLimits"/> leaves offshore units out).
    /// With <paramref name="siteId"/> and <paramref name="mode"/>, that site is
    /// counted in <paramref name="mode"/> (the preview of a mode change).
    /// </summary>
    public int RemoteOpsAttentionUsed(string? siteId = null, string? mode = null)
    {
        if (State?.SeaSites == null) return 0;
        int used = 0;
        foreach (var site in State.SeaSites)
        {
            if (!CountsTowardSiteLimits(site)) continue;
            used += Optimization.RemoteOpsCentre.CostFor(siteId != null && site.Id == siteId ? mode : site.WatchMode);
        }
        return used;
    }

    /// <summary>Sea sites that count toward the site limits (the remote operations centre's watch list).</summary>
    public int SitesCountingTowardLimits()
        => State?.SeaSites == null ? 0 : State.SeaSites.Count(CountsTowardSiteLimits);
}
