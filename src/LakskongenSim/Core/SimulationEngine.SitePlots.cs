using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Farm;

namespace LakskongenSim.Core;

/// <summary>
/// The game's farm-type plots (roadmap F2-03, docs/FARM_TYPES_SPEC.md §3.2 and
/// §4): the land pads and offshore slots of <see cref="SitePlotCatalog"/> with
/// the game's state of each (<see cref="GameState.SitePlots"/>: the unit on it,
/// its zoning and grid feeds). A game holds no plot state until it first puts
/// a land site or an offshore unit on a plot (a build or a migration), so a
/// fjord-only game's save is unchanged and a refused build changes nothing;
/// the named plots then join in catalogue order, and an overflow plot only
/// when a migrated save needs one.
/// </summary>
public partial class SimulationEngine
{
    /// <summary>The plot catalogue the game reads (the embedded <c>site_plots.json</c>).</summary>
    internal SitePlotCatalog PlotCatalog { get; set; } = SitePlotCatalog.Default;

    /// <summary>
    /// The game's plots: every named plot of the catalogue (free or held) and
    /// the overflow plots a migrated save uses, in that order. A read: a game
    /// that holds no plot state yet sees the catalogue's starting state, and
    /// nothing is added to the save.
    /// </summary>
    public IReadOnlyList<SitePlot> SitePlots
    {
        get
        {
            if (State == null) return Array.Empty<SitePlot>();
            var plots = new List<SitePlot>();
            foreach (var template in PlotCatalog.Plots)
                plots.Add(FindPlot(template.Id) ?? SitePlot.NewGame(template));
            if (State.SitePlots != null)
                foreach (var plot in State.SitePlots)
                    if (PlotCatalog.Plots.All(t => t.Id != plot.Id))
                        plots.Add(plot);
            return plots;
        }
    }

    /// <summary>The game's saved state of the plot with <paramref name="plotId"/>, or null when it holds none.</summary>
    public SitePlot? FindPlot(string? plotId)
    {
        if (State?.SitePlots == null || string.IsNullOrEmpty(plotId)) return null;
        foreach (var plot in State.SitePlots)
            if (plot.Id == plotId)
                return plot;
        return null;
    }

    /// <summary>
    /// The first free named plot (catalogue order) that <paramref name="fits"/>,
    /// or null. Overflow plots are for migrated saves only (spec §3.8 step 4).
    /// A read (<see cref="SitePlots"/>): claim the plot to hold it.
    /// </summary>
    internal SitePlot? FirstFreePlot(Func<SitePlot, bool> fits)
    {
        if (fits == null) throw new ArgumentNullException(nameof(fits));
        foreach (var plot in SitePlots)
            if (!plot.IsOverflow && plot.IsFree && fits(plot))
                return plot;
        return null;
    }

    /// <summary>
    /// The first free overflow plot of <paramref name="kind"/>, numbered from
    /// 1: one the game already holds, else the next one of the catalogue's
    /// overflow row (reserved positions first, generated ones past them). A
    /// read: claim the plot to hold it.
    /// </summary>
    internal SitePlot NextFreeOverflowPlot(PlotKind kind)
    {
        for (int index = 1; ; index++)
        {
            var template = PlotCatalog.Overflow(kind, index);
            var plot = FindPlot(template.Id);
            if (plot == null) return SitePlot.NewGame(template);
            if (plot.IsFree) return plot;
        }
    }

    /// <summary>
    /// Record that <paramref name="unitId"/> stands on <paramref name="plot"/>
    /// (a plot <see cref="FirstFreePlot"/> or <see cref="NextFreeOverflowPlot"/>
    /// found): the game takes its named plots' state if it held none, adds an
    /// overflow plot it did not hold, and the plot's owner is the unit. Returns
    /// the game's state of the plot.
    /// </summary>
    internal SitePlot ClaimPlot(SitePlot plot, string unitId)
    {
        if (plot == null) throw new ArgumentNullException(nameof(plot));
        if (string.IsNullOrEmpty(unitId)) throw new ArgumentException("A plot is claimed by a unit id", nameof(unitId));
        EnsureSitePlots();
        var held = FindPlot(plot.Id);
        if (held == null)
        {
            held = plot.WithTemplate(plot.Template);
            State.SitePlots.Add(held);
        }
        if (!held.IsFree && held.OwnerUnitId != unitId)
            throw new InvalidOperationException($"Plot '{held.Id}' is held by '{held.OwnerUnitId}'");
        held.OwnerUnitId = unitId;
        return held;
    }

    /// <summary>
    /// Give the game its named plots if it holds none of them yet (catalogue
    /// order, unowned, with the catalogue's starting zoning and grid feeds),
    /// keeping any state it holds; overflow plots stay after them. Idempotent.
    /// </summary>
    private void EnsureSitePlots()
    {
        State.SitePlots ??= new List<SitePlot>();
        var named = PlotCatalog.Plots.Select(t => FindPlot(t.Id) ?? SitePlot.NewGame(t)).ToList();
        var others = State.SitePlots.Where(p => PlotCatalog.Plots.All(t => t.Id != p.Id)).ToList();
        if (State.SitePlots.Count == named.Count + others.Count
            && State.SitePlots.Take(named.Count).SequenceEqual(named))
            return;
        State.SitePlots.Clear();
        State.SitePlots.AddRange(named);
        State.SitePlots.AddRange(others);
    }

    /// <summary>
    /// A load: the save keeps each plot's state, the catalogue gives its
    /// attributes. A plot the catalogue no longer knows keeps its state with no
    /// attributes (never dropped: a unit may stand on it).
    /// </summary>
    internal void RehydrateSitePlotsAfterLoad()
    {
        if (State?.SitePlots == null || State.SitePlots.Count == 0) return;
        var rehydrated = State.SitePlots
            .Select(saved => saved.WithTemplate(PlotCatalog.Find(saved.Id) ?? SitePlotTemplate.Unknown(saved.Id)))
            .ToList();
        State.SitePlots.Clear();
        State.SitePlots.AddRange(rehydrated);
    }
}
