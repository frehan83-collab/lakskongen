using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Calibration;
using LakskongenSim.Environment;
using LakskongenSim.Farm;

namespace LakskongenSim.Core;

/// <summary>
/// The open sea the offshore units stand in (roadmap F2-06 wiring,
/// docs/FARM_TYPES_SPEC.md §3.3 "Metocean"): one <see cref="OffshoreMetocean"/>
/// per game (<see cref="GameState.Metocean"/>), created the first time the game
/// has an offshore unit and ticked from then on in the in-day phase right
/// after the weather (<see cref="OffshoreSeaTickSystem"/>), where it reads the
/// day's fjord weather state and draws exactly 8 uniforms from its own stream.
/// Each unit's sea is its plot's fitted scale times the reference sea
/// (<see cref="OffshorePlotCalibration.HsScale"/>). The honest forecast is issued
/// once per plot and day from the saved state and cached here (presentation
/// state, never saved, never drawn from a stream).
/// </summary>
public partial class SimulationEngine
{
    /// <summary>The sea lane's calibration (facilities.offshore, F2-06/F2-07).</summary>
    internal OffshoreCalibration OffshoreCal => _calibration.Facilities.Offshore;

    private readonly Dictionary<(string Plot, int Day, WeatherState Weather, bool Quantiles), SeaStateForecast> _seaForecasts = new();
    private OffshoreMetocean? _seaForecastsOf;

    /// <summary>The game's open sea, or null while it has never had an offshore unit.</summary>
    public OffshoreMetocean? Metocean => State?.Metocean;

    /// <summary>
    /// Create the game's open sea if it has none (the game's first offshore
    /// unit) and bring it to today: a new sea ticks once for the day the weather
    /// is on, so a forecast exists from the build on. Idempotent; draws only
    /// the new sea's own 8 uniforms of that day.
    /// </summary>
    internal OffshoreMetocean EnsureOffshoreSea()
    {
        if (State.Metocean == null)
        {
            var sea = new OffshoreMetocean();
            sea.ApplyCalibration(OffshoreCal.Metocean);
            State.Metocean = sea;
        }
        if (State.Metocean.LastTickDay < Weather.SimulationDay)
            TickOffshoreSea();
        return State.Metocean;
    }

    /// <summary>
    /// The in-day Metocean phase (right after the weather): once the game has
    /// an open sea it ticks every day, then every offshore unit takes its
    /// plot's Hs for the day. A game that never had an offshore unit does
    /// nothing here (no stream, no state).
    /// </summary>
    internal void AdvanceOffshoreSea()
    {
        if (State.Metocean == null)
        {
            if (!State.SeaSites.Any(s => s.Kind == SiteKind.Offshore)) return;
            EnsureOffshoreSea(); // a migrated or hand-built unit: its sea starts today
        }
        else if (State.Metocean.LastTickDay < Weather.SimulationDay)
        {
            TickOffshoreSea();
        }
        foreach (var site in State.SeaSites)
            if (site.Kind == SiteKind.Offshore && site.Offshore != null)
                TakeTodaysSea(site);
    }

    /// <summary>
    /// An offshore unit takes the sea's day at its plot (the Metocean phase for
    /// every unit; a unit built between days at once, so its gauges and the
    /// window board read the same sea on its first day). Draws nothing.
    /// </summary>
    internal void TakeTodaysSea(SeaSite site)
    {
        var sea = State.Metocean;
        if (sea == null || sea.LastTickDay < 0 || site.Offshore == null) return;
        site.Offshore.CurrentWaveHeight = SeaOverrideForTests?.Invoke(site, sea.LastTickDay) ?? sea.HsAt(PlotHsScaleOf(site));
    }

    /// <summary>
    /// Test seam (F2-07 acceptance 3, "forced series"): when set, a unit's Hs for
    /// a day is this function's value instead of its plot's sea (null keeps
    /// the sea's). The sea still draws its 8 uniforms; nothing else changes.
    /// Never set by the game.
    /// </summary>
    internal Func<SeaSite, int, double?>? SeaOverrideForTests { get; set; }

    private void TickOffshoreSea()
    {
        State.Metocean!.TickDay(Weather.SimulationDay, Weather.CurrentState, Rng.Stream(SimRngStreams.OffshoreMetocean));
        _seaForecasts.Clear();
    }

    /// <summary>
    /// The fitted Hs scale of a unit's plot (facilities.offshore.plots: off_1,
    /// off_2, hav_1); an overflow slot of a migrated save takes its catalogue
    /// scale (neutral, 1.0).
    /// </summary>
    internal double PlotHsScaleOf(SeaSite site) => PlotHsScaleOf(site.PlotId);

    /// <summary>The fitted Hs scale of the plot with <paramref name="plotId"/>.</summary>
    internal double PlotHsScaleOf(string? plotId)
    {
        if (!string.IsNullOrEmpty(plotId) && Array.IndexOf(OffshorePlotsCalibration.PlotIds, plotId) >= 0)
            return OffshoreCal.Plots.For(plotId).HsScale;
        var plot = SitePlots.FirstOrDefault(p => p.Id == plotId);
        return plot != null && plot.PlotHsScale > 0.0 ? plot.PlotHsScale : 1.0;
    }

    /// <summary>Today's Hs at an offshore unit (m), as the Metocean phase gave it; 0 while the game has no open sea.</summary>
    public double SeaHsAt(SeaSite site)
        => State?.Metocean is { LastTickDay: >= 0 } ? site.Offshore?.CurrentWaveHeight ?? 0.0 : 0.0;

    /// <summary>
    /// The honest forecast at an offshore unit's plot (spec §3.3 point 6), issued
    /// from today's sea and weather and cached for the day; null while the game
    /// has no open sea or the sea is not on today's weather day. Pure: no
    /// stream moves, nothing in the game changes. With
    /// <paramref name="withQuantiles"/> each day carries its p10/p50/p90 (the
    /// bridge); without, only the probabilities (policies, booking).
    /// </summary>
    public SeaStateForecast? SeaForecastFor(SeaSite site, bool withQuantiles = false)
    {
        var sea = State?.Metocean;
        if (sea == null || sea.LastTickDay < 0 || Weather == null || Weather.SimulationDay != sea.LastTickDay) return null;
        if (!ReferenceEquals(_seaForecastsOf, sea))
        {
            _seaForecasts.Clear(); // a new game or a load: another sea
            _seaForecastsOf = sea;
        }
        var key = (site.PlotId ?? "", sea.LastTickDay, Weather.CurrentState, withQuantiles);
        if (_seaForecasts.TryGetValue(key, out var cached)) return cached;
        var forecast = MetoceanForecast.Issue(sea, Weather, PlotHsScaleOf(site), ForecastLimits(), withQuantiles);
        _seaForecasts[key] = forecast;
        return forecast;
    }

    /// <summary>The operation limits every forecast day carries (m, ascending, each once).</summary>
    internal IReadOnlyList<double> ForecastLimits()
    {
        var o = OffshoreCal.Operability;
        return new[]
        {
            o.FishHandlingHsM, o.FishHandlingOffshoreWellboatHsM, o.BoardingHsM, o.BoardingDpHsM, o.NetInspectionHsM,
            o.ResupplyHsM, o.ResupplyDpHsM, o.TowHsM, o.WellboatAlongsideHsM,
        }.Distinct().OrderBy(x => x).ToArray();
    }

    /// <summary>
    /// Whether <paramref name="op"/> with <paramref name="vessel"/> can run at an
    /// offshore unit today: its plot's Hs within the limit, a helicopter's wind
    /// and visibility too (spec §3.3 operability table).
    /// </summary>
    internal bool OffshoreOperationPossibleToday(SeaSite site, OffshoreOp op, VesselClass vessel)
    {
        var sea = State?.Metocean;
        double hs = site.Offshore?.CurrentWaveHeight ?? 0.0;
        double visibility = sea?.VisibilityKmToday ?? OffshoreCal.Metocean.ClearVisibilityMaxKm;
        return OperabilityWindows.IsPossible(op, vessel, hs, Weather?.CurrentWindSpeedMs ?? 0.0, visibility, OffshoreCal.Operability);
    }

    /// <summary>
    /// The weather-window board's rows (spec §6.5 "Værvinduer"): each row an
    /// operation with the vessel it is booked with, in board order.
    /// </summary>
    public static readonly IReadOnlyList<(string Id, OffshoreOp Op, VesselClass Vessel)> SeaBoardRows = new[]
    {
        ("feeding", OffshoreOp.Feeding, VesselClass.Standard),
        ("resupply", OffshoreOp.Resupply, VesselClass.Standard),
        ("resupply_dp", OffshoreOp.Resupply, VesselClass.DpWalkToWork),
        ("crew_change", OffshoreOp.CrewChange, VesselClass.Standard),
        ("crew_change_dp", OffshoreOp.CrewChange, VesselClass.DpWalkToWork),
        ("crew_change_helicopter", OffshoreOp.CrewChange, VesselClass.Helicopter),
        ("net_inspection", OffshoreOp.NetInspection, VesselClass.Standard),
        ("wellboat", OffshoreOp.Stocking, VesselClass.Standard),
        ("wellboat_offshore", OffshoreOp.Stocking, VesselClass.OffshoreWellboat),
        ("delousing_harvest", OffshoreOp.Delousing, VesselClass.Standard),
        ("tow", OffshoreOp.Tow, VesselClass.Standard),
    };

    /// <summary>
    /// The sea-state forecast at an offshore unit as the bridge hands it to the
    /// window board (F2-06 wiring; <c>GameBridge.Offshore.GetSeaStateForecast</c>):
    /// today's sea, ten days of Hs p10/p50/p90 with each board row's probability,
    /// and the known swell. Null for a site that is no offshore unit or a game
    /// with no open sea yet. Pure (the cached forecast).
    /// </summary>
    public SeaStateForecastView? GetSeaStateForecastView(string siteId)
    {
        var site = FindOffshoreUnit(siteId);
        if (site == null) return null;
        var forecast = SeaForecastFor(site, withQuantiles: true);
        var sea = State.Metocean;
        if (forecast == null || sea == null) return null;
        double scale = PlotHsScaleOf(site);
        var operability = OffshoreCal.Operability;
        var metocean = OffshoreCal.Metocean;
        var days = new List<SeaStateForecastDayView>(forecast.HorizonDays);
        foreach (var day in forecast.Days)
        {
            var rows = new List<(string, double)>(SeaBoardRows.Count);
            foreach (var (id, op, vessel) in SeaBoardRows)
                rows.Add((id, OperabilityWindows.Probability(forecast, day.LeadDays, op, vessel, operability, metocean)));
            days.Add(new SeaStateForecastDayView(day.Day, day.LeadDays, day.HsP10M, day.HsP50M, day.HsP90M, rows, day.UnresolvedProbability));
        }
        var limits = SeaBoardRows
            .Where(r => r.Op != OffshoreOp.Feeding)
            .Select(r => (r.Id, OperabilityWindows.HsLimitM(r.Op, r.Vessel, operability) ?? 0.0))
            .ToList();
        return new SeaStateForecastView(site.Id, site.PlotId, forecast.IssuedDay, forecast.HsTodayM, sea.TpAt(scale),
            sea.SwellHsToday * scale, sea.VisibilityKmToday, Weather.CurrentWindSpeedMs,
            (site.Offshore?.DesignHsM ?? 0.0) + (site.Offshore?.DesignHsBonusM ?? 0.0), limits, days, forecast.KnownSwell);
    }

    /// <summary>Adopt a loaded save's open sea (a save from before F2-06's wiring has none) and its calibration.</summary>
    internal void AdoptOffshoreState(GameState snapshot)
    {
        State.Metocean = snapshot.Metocean;
        State.Metocean?.ApplyCalibration(OffshoreCal.Metocean);
        _seaForecasts.Clear();
    }
}

/// <summary>
/// IN-DAY phase Metocean (F2-06, spec §3.3 phase order "Weather → Metocean"):
/// the open sea's day, right after the fjord weather it reads. A game that
/// never had an offshore unit draws nothing here.
/// </summary>
internal sealed class OffshoreSeaTickSystem : ITickSystem
{
    public void Tick(SimulationEngine engine) => OffshoreTickSystem.AdvanceSea(engine);
}

/// <summary>One forecast day at a unit as the window board reads it.</summary>
/// <param name="Day">The sim day.</param>
/// <param name="LeadDays">Days ahead (1 = tomorrow).</param>
/// <param name="HsP10M">Hs 10th percentile (m).</param>
/// <param name="HsP50M">Hs median (m).</param>
/// <param name="HsP90M">Hs 90th percentile (m).</param>
/// <param name="RowProbabilities">Each board row's probability that its operation can run that day.</param>
/// <param name="UnresolvedProbability">Probability mass the forecast counts above every limit unresolved.</param>
public sealed record SeaStateForecastDayView(int Day, int LeadDays, double HsP10M, double HsP50M, double HsP90M,
    IReadOnlyList<(string Row, double Probability)> RowProbabilities, double UnresolvedProbability);

/// <summary>The sea at an offshore unit today and the honest forecast (F2-06 wiring).</summary>
/// <param name="SiteId">The unit.</param>
/// <param name="PlotId">Its plot.</param>
/// <param name="IssuedDay">The day the forecast was issued (today).</param>
/// <param name="HsTodayM">Today's Hs at the plot (m).</param>
/// <param name="TpTodayS">Today's peak period (s).</param>
/// <param name="SwellHsTodayM">Today's storm swell at the plot (m).</param>
/// <param name="VisibilityKm">Today's visibility (km).</param>
/// <param name="WindMs">Today's wind (m/s, the fjord weather's).</param>
/// <param name="DesignHsM">The unit's design Hs with any reinforcement (m).</param>
/// <param name="RowLimitsM">Each board row's Hs limit (m; +∞ for the helicopter).</param>
/// <param name="Days">Lead 1 first.</param>
/// <param name="KnownSwell">The swell events the game knows, earliest onset first.</param>
public sealed record SeaStateForecastView(string SiteId, string PlotId, int IssuedDay, double HsTodayM, double TpTodayS,
    double SwellHsTodayM, double VisibilityKm, double WindMs, double DesignHsM,
    IReadOnlyList<(string Row, double LimitM)> RowLimitsM, IReadOnlyList<SeaStateForecastDayView> Days,
    IReadOnlyList<SwellOutlook> KnownSwell);
