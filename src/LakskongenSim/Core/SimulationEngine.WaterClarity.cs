using System;
using LakskongenSim.Environment;
using LakskongenSim.Farm;

namespace LakskongenSim.Core;

/// <summary>
/// The water a site's fish swim in, as the underwater view sees it (roadmap
/// G3-04): the season's clarity and an algal bloom at the site
/// (<see cref="WaterClarity"/>). A pure read: it draws no random number and
/// writes no state, so the golden traces never see it.
/// </summary>
public partial class SimulationEngine
{
    /// <summary>
    /// Clarity of the water at <paramref name="site"/> today: the season's
    /// visibility, cut by the worst algal bloom active at the site (the
    /// <c>ActiveBlooms</c> entries of kind <see cref="WaterClarity.AlgaeBloomKind"/>
    /// on this site that have not run out). Jellyfish and hypoxia leave the
    /// water as clear as it was.
    /// </summary>
    public WaterClarityConditions WaterClarityOf(SeaSite site)
    {
        int day = State?.Day ?? 0;
        return WaterClarity.Derive(day, AlgaeBloomSeverityAt(site, day));
    }

    /// <summary>Severity (0-1) of the worst algal bloom active at a site on a day; 0 without one.</summary>
    internal double AlgaeBloomSeverityAt(SeaSite? site, int day)
    {
        var blooms = State?.ActiveBlooms;
        if (site == null || blooms == null)
            return 0.0;
        double worst = 0.0;
        foreach (var bloom in blooms)
        {
            if (bloom.SiteId != site.Id || bloom.IsExpired(day)
                || !string.Equals(bloom.Kind, WaterClarity.AlgaeBloomKind, StringComparison.OrdinalIgnoreCase))
                continue;
            worst = Math.Max(worst, Math.Clamp(bloom.Severity01, 0.0, 1.0));
        }
        return worst;
    }
}
