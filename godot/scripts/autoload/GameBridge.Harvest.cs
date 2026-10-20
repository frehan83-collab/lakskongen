using Godot;
using System.Linq;
using LakskongenSim.Harvest;

// GameBridge.Harvest.cs — partial class slice (roadmap S2-04).
// <summary>
// Harvest slice: the planned harvest pipeline (quote, order, cancel, the open
// orders and the harvest reports) for the harvest planner and report (U2-05).
// The express harvest stays on GameBridge.HarvestSite (GameBridge.Farm.cs).
// </summary>
/// <summary>
/// GameBridge partial — the S2-04 harvest pipeline: quotes, orders, reports.
/// </summary>
public partial class GameBridge
{
    private static string[] PenIdsOf(Godot.Collections.Array<string>? penIds)
        => penIds == null ? System.Array.Empty<string>() : penIds.Select(id => (string)id).ToArray();

    /// <summary>The pens an order still holds: each pen's id ("site/p03"), or its population id for fish in no pen.</summary>
    private static Godot.Collections.Array<string> PenIdsInOrder(HarvestOrder order)
    {
        var ids = new Godot.Collections.Array<string>();
        foreach (var p in order.Pens)
            if (p.FishRemaining > 0)
                ids.Add(string.IsNullOrEmpty(p.PenId) ? p.PopulationId : p.PenId);
        return ids;
    }

    private static Godot.Collections.Array<double> SizeClassesOf(System.Collections.Generic.IEnumerable<double> kg)
    {
        var classes = new Godot.Collections.Array<double>();
        foreach (var v in kg) classes.Add(v);
        return classes;
    }

    /// <summary>
    /// What harvesting a holding (sea site, land facility, offshore farm) would
    /// bring if ordered now: success, error, message, route, mode, fish,
    /// live_kg, hog_kg, mean_kg, weight_cv, size_classes (HOG kg per class
    /// &lt;1 .. 7+ kg), superior / ordinary / production shares, size_factor,
    /// grade_factor, reference_price, contract_hog_kg, contract_nok,
    /// spot_hog_kg, spot_nok, vap, gross_nok (contracts, spot and the plant's
    /// premium: what a sale today books), transport_nok, slaughter_nok,
    /// surcharge_nok, net_nok, fasting_days, first_day, last_day, capacity_t.
    /// Empty pen_ids = every pen with fish; fraction (0, 1] of each pen's fish.
    /// </summary>
    public Godot.Collections.Dictionary QuoteHarvest(string holdingId, Godot.Collections.Array<string> penIds,
        double fraction = 1.0, bool express = false)
    {
        var engine = SimulationEngine;
        if (engine == null)
            return new Godot.Collections.Dictionary { ["success"] = false, ["error"] = "engine_not_ready", ["message"] = "Not initialized" };
        var q = engine.QuoteHarvest(holdingId, PenIdsOf(penIds), fraction, express);
        return new Godot.Collections.Dictionary
        {
            ["success"] = q.Ok,
            ["error"] = q.ErrorKey,
            ["message"] = q.Message,
            ["holding_id"] = q.HoldingId,
            ["route"] = q.Route.ToString(),
            ["mode"] = q.Mode.ToString(),
            ["fish"] = q.Fish,
            ["live_kg"] = q.LiveKg,
            ["hog_kg"] = q.HogKg,
            ["mean_kg"] = q.MeanLiveWeightKg,
            ["weight_cv"] = q.WeightCv,
            ["size_classes"] = SizeClassesOf(q.HogKgBySizeClass),
            ["superior"] = q.SuperiorShare,
            ["ordinary"] = q.OrdinaryShare,
            ["production"] = q.ProductionShare,
            ["size_factor"] = q.SizeFactor,
            ["grade_factor"] = q.GradeFactor,
            ["reference_price"] = (double)q.ReferencePriceNok,
            ["contract_hog_kg"] = q.ContractHogKg,
            ["contract_nok"] = (double)q.ContractRevenueNok,
            ["spot_hog_kg"] = q.SpotHogKg,
            ["spot_nok"] = (double)q.SpotRevenueNok,
            ["vap"] = (double)q.VapMultiplier,
            ["gross_nok"] = (double)q.GrossRevenueNok,
            ["transport_nok"] = (double)q.TransportCostNok,
            ["slaughter_nok"] = (double)q.SlaughterCostNok,
            ["surcharge_nok"] = (double)q.SurchargeNok,
            ["net_nok"] = (double)q.NetRevenueNok,
            ["fasting_days"] = q.FastingDays,
            ["first_day"] = q.FirstSlaughterDay,
            ["last_day"] = q.LastSlaughterDay,
            ["capacity_t"] = q.DailyCapacityTonnesHog
        };
    }

    /// <summary>
    /// Order a planned harvest (the fish fast, the wellboat comes on its slot,
    /// the slaughterhouse takes them within its daily capacity). Returns
    /// success, error, message, value (live kg planned), value2 (gross at
    /// today's price), id (the order id).
    /// </summary>
    public Godot.Collections.Dictionary OrderHarvest(string holdingId, Godot.Collections.Array<string> penIds,
        double fraction = 1.0)
        => FacilityResult(SimulationEngine?.OrderHarvest(holdingId, PenIdsOf(penIds), fraction));

    /// <summary>Call off an open harvest order; the fish feed again. Same result shape as <see cref="OrderHarvest"/>.</summary>
    public Godot.Collections.Dictionary CancelHarvestOrder(string orderId)
        => FacilityResult(SimulationEngine?.CancelHarvestOrder(orderId));

    /// <summary>
    /// The open harvest orders, oldest slot first: id, holding_id, route,
    /// status, fraction, ordered_day, slot_day, pens, pen_ids (the pens it
    /// still holds), fish_planned,
    /// fish_harvested, hog_kg, gross_nok, cost_nok.
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetHarvestOrders()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var engine = SimulationEngine;
        if (engine == null) return result;
        foreach (var o in engine.HarvestOrders)
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = o.Id,
                ["holding_id"] = o.HoldingId,
                ["route"] = o.Route.ToString(),
                ["status"] = o.Status.ToString(),
                ["fraction"] = o.Fraction,
                ["ordered_day"] = o.OrderedDay,
                ["slot_day"] = o.SlotDay,
                ["pens"] = o.Pens.Count,
                ["pen_ids"] = PenIdsInOrder(o),
                ["fish_planned"] = o.Pens.Sum(p => p.FishToHarvest),
                ["fish_harvested"] = o.FishHarvested,
                ["hog_kg"] = o.HogKg,
                ["gross_nok"] = (double)o.GrossRevenueNok,
                ["cost_nok"] = (double)o.HarvestCostNok
            });
        return result;
    }

    /// <summary>
    /// The latest harvest reports, newest last, exactly as booked: id, day,
    /// holding_id, holding_label, order_id, mode, route, fish, live_kg, hog_kg,
    /// mean_kg, weight_cv, size_classes, superior, ordinary, production,
    /// size_factor, grade_factor, reference_price, reference_kg (the kilos the
    /// price is paid on), contract_hog_kg, contract_nok, vap, price_multiplier,
    /// gross_nok, transport_nok, slaughter_nok, surcharge_nok, cost_nok, net_nok.
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetHarvestReports()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var engine = SimulationEngine;
        if (engine == null) return result;
        foreach (var r in engine.HarvestReports)
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = r.Id,
                ["day"] = r.Day,
                ["holding_id"] = r.HoldingId,
                ["holding_label"] = r.HoldingLabel,
                ["order_id"] = r.OrderId,
                ["mode"] = r.Mode.ToString(),
                ["route"] = r.Route.ToString(),
                ["fish"] = r.Fish,
                ["live_kg"] = r.LiveKg,
                ["hog_kg"] = r.HogKg,
                ["mean_kg"] = r.MeanLiveWeightKg,
                ["weight_cv"] = r.WeightCv,
                ["size_classes"] = SizeClassesOf(r.HogKgBySizeClass),
                ["superior"] = r.SuperiorShare,
                ["ordinary"] = r.OrdinaryShare,
                ["production"] = r.ProductionShare,
                ["size_factor"] = r.SizeFactor,
                ["grade_factor"] = r.GradeFactor,
                ["reference_price"] = (double)r.ReferencePriceNok,
                ["reference_kg"] = r.ReferenceEquivalentKg,
                ["contract_hog_kg"] = r.ContractHogKg,
                ["contract_nok"] = (double)r.ContractRevenueNok,
                ["vap"] = (double)r.VapMultiplier,
                ["price_multiplier"] = (double)r.PriceMultiplier,
                ["gross_nok"] = (double)r.GrossRevenueNok,
                ["transport_nok"] = (double)r.TransportCostNok,
                ["slaughter_nok"] = (double)r.SlaughterCostNok,
                ["surcharge_nok"] = (double)r.SurchargeNok,
                ["cost_nok"] = (double)r.HarvestCostNok,
                ["net_nok"] = (double)r.NetRevenueNok
            });
        return result;
    }
}
