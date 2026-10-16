using System.Linq;
using Godot;
using LakskongenSim.Core;
using LakskongenSim.Harvest;

// GameBridge.HarvestPlanner.cs — partial class slice (roadmap U2-05).
// <summary>
// The harvest planner and the harvest report: a planned harvest projected
// today or after a wait (SimulationEngine.ProjectHarvest, twin-lite: pure, it
// moves no RNG stream), the planner's options, an express harvest of chosen
// pens, and the ledger's category totals the report is checked against.
// </summary>
/// <summary>
/// GameBridge partial — the harvest planner (U2-05): projections, options,
/// the express verb for chosen pens and the ledger totals.
/// </summary>
public partial class GameBridge
{
    /// <summary>The waits the planner compares (days): now, two, four and eight weeks.</summary>
    private static readonly int[] HarvestPlannerWaits = { 0, 14, 28, 56 };

    /// <summary>
    /// A planned harvest of <paramref name="holdingId"/>'s chosen pens (all
    /// with fish when empty; <paramref name="fraction"/> of each pen's fish)
    /// ordered after <paramref name="waitDays"/> days. Keys: success, error,
    /// message, holding_id, route, wait_days, order_day, fasting_days,
    /// slot_day, first_day, last_day, fish, live_kg, hog_kg, mean_kg,
    /// weight_cv, size_classes (HOG kg per class), superior, ordinary,
    /// production, size_factor, grade_factor, spot_today, expected_price,
    /// contract_hog_kg, contract_nok, spot_hog_kg, spot_nok, vap, gross_nok,
    /// transport_nok, slaughter_nok, net_nok, feed_kg, feed_nok,
    /// net_after_feed_nok, daily_mortality, expected_deaths, days (day,
    /// fish, hog_kg, price, gross_nok per slaughter day).
    /// </summary>
    public Godot.Collections.Dictionary ProjectHarvest(string holdingId, Godot.Collections.Array<string> penIds,
        double fraction = 1.0, int waitDays = 0)
    {
        var engine = SimulationEngine;
        if (engine == null)
            return new Godot.Collections.Dictionary { ["success"] = false, ["error"] = "engine_not_ready", ["message"] = "Not initialized" };
        return ProjectionToDictionary(engine.ProjectHarvest(holdingId, PenIdsOf(penIds), fraction, waitDays));
    }

    private static Godot.Collections.Dictionary ProjectionToDictionary(HarvestProjection p)
    {
        var days = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var d in p.Days)
            days.Add(new Godot.Collections.Dictionary
            {
                ["day"] = d.Day,
                ["fish"] = d.Fish,
                ["hog_kg"] = d.HogKg,
                ["price"] = (double)d.ReferencePriceNok,
                ["gross_nok"] = (double)d.GrossRevenueNok
            });
        return new Godot.Collections.Dictionary
        {
            ["success"] = p.Ok,
            ["error"] = p.ErrorKey,
            ["message"] = p.Message,
            ["holding_id"] = p.HoldingId,
            ["route"] = p.Route.ToString(),
            ["wait_days"] = p.WaitDays,
            ["order_day"] = p.OrderDay,
            ["fasting_days"] = p.FastingDays,
            ["slot_day"] = p.SlotDay,
            ["first_day"] = p.FirstSlaughterDay,
            ["last_day"] = p.LastSlaughterDay,
            ["fish"] = p.Fish,
            ["live_kg"] = p.LiveKg,
            ["hog_kg"] = p.HogKg,
            ["mean_kg"] = p.MeanLiveWeightKg,
            ["weight_cv"] = p.WeightCv,
            ["size_classes"] = SizeClassesOf(p.HogKgBySizeClass),
            ["superior"] = p.SuperiorShare,
            ["ordinary"] = p.OrdinaryShare,
            ["production"] = p.ProductionShare,
            ["size_factor"] = p.SizeFactor,
            ["grade_factor"] = p.GradeFactor,
            ["spot_today"] = (double)p.SpotTodayNok,
            ["expected_price"] = (double)p.ExpectedReferencePriceNok,
            ["contract_hog_kg"] = p.ContractHogKg,
            ["contract_nok"] = (double)p.ContractRevenueNok,
            ["spot_hog_kg"] = p.SpotHogKg,
            ["spot_nok"] = (double)p.SpotRevenueNok,
            ["vap"] = (double)p.VapMultiplier,
            ["gross_nok"] = (double)p.GrossRevenueNok,
            ["transport_nok"] = (double)p.TransportCostNok,
            ["slaughter_nok"] = (double)p.SlaughterCostNok,
            ["net_nok"] = (double)p.NetRevenueNok,
            ["feed_kg"] = p.FeedKg,
            ["feed_nok"] = (double)p.FeedCostNok,
            ["net_after_feed_nok"] = (double)p.NetAfterFeedNok,
            ["daily_mortality"] = p.DailyMortality,
            ["expected_deaths"] = p.ExpectedDeaths,
            ["days"] = days
        };
    }

    /// <summary>
    /// What the planner offers and how it reads: waits (days), the most it
    /// projects, the size classes (label, price factor, the reference class),
    /// the wellboat lead time, the fasting rule, the slaughterhouse capacity
    /// (tonnes HOG a day), the HOG yield and the express surcharge (NOK/kg HOG).
    /// </summary>
    public Godot.Collections.Dictionary GetHarvestPlannerOptions()
    {
        var engine = SimulationEngine;
        var cal = LakskongenSim.Calibration.CalibrationRegistry.Default.Harvest;
        var waits = new Godot.Collections.Array<int>();
        foreach (int w in HarvestPlannerWaits) waits.Add(w);
        var labels = new Godot.Collections.Array<string>();
        var factors = new Godot.Collections.Array<double>();
        for (int c = 0; c < SizeClassPriceLadder.ClassCount; c++)
        {
            labels.Add(SizeClassPriceLadder.Label(c));
            factors.Add(SizeClassPriceLadder.Factor(cal, c));
        }
        return new Godot.Collections.Dictionary
        {
            ["waits"] = waits,
            ["max_wait_days"] = SimulationEngine.MaxHarvestWaitDays,
            ["size_labels"] = labels,
            ["size_factors"] = factors,
            ["reference_class"] = SizeClassPriceLadder.ReferenceClass,
            ["lead_days"] = cal.WellboatLeadDays,
            ["fasting_degree_days"] = cal.FastingDegreeDays,
            ["min_fasting_days"] = cal.MinFastingDays,
            ["capacity_t"] = System.Math.Min(cal.SlaughterCapacityTonnesHogPerDay,
                cal.WellboatCapacityTonnesLivePerDay * cal.HogYieldFraction),
            ["hog_yield"] = cal.HogYieldFraction,
            ["express_surcharge_per_kg"] = (double)cal.ExpressSurchargePerKgHogNok,
            ["today"] = engine?.State?.Day ?? 0
        };
    }

    /// <summary>
    /// Harvest the chosen pens now (S2-04 express: a spot wellboat to the
    /// slaughterhouse's waiting pen, sold today with the surcharge). Keys:
    /// success, error, message, value (live kg), value2 (gross NOK), id (the
    /// holding), report_id (the batch's report). Emits FarmActionCompleted
    /// ("harvest") for the harvest's one toast and the report.
    /// </summary>
    public Godot.Collections.Dictionary HarvestExpress(string holdingId, Godot.Collections.Array<string> penIds,
        double fraction = 1.0)
    {
        var engine = SimulationEngine;
        if (engine?.State == null)
            return new Godot.Collections.Dictionary { ["success"] = false, ["error"] = "engine_not_ready", ["message"] = "Not initialized" };
        int reports = engine.HarvestReports.Count;
        var outcome = engine.HarvestNow(holdingId, PenIdsOf(penIds), fraction);
        var result = new Godot.Collections.Dictionary
        {
            ["success"] = outcome.Ok,
            ["error"] = outcome.ErrorKey,
            ["message"] = outcome.Message,
            ["value"] = outcome.Value,
            ["value2"] = outcome.Value2,
            ["id"] = outcome.Id,
            ["report_id"] = outcome.Ok && engine.HarvestReports.Count > 0 ? engine.HarvestReports[^1].Id : ""
        };
        EmitSignal(SignalName.FarmActionCompleted, "harvest", holdingId, outcome.Ok);
        if (outcome.Ok) EmitSignal(SignalName.GameStateUpdated);
        return result;
    }

    /// <summary>
    /// The ledger's running totals by category (this period's statements):
    /// revenue (category -> NOK) and cost (category -> NOK). The harvest
    /// report's figures are these entries ("harvest" revenue and cost).
    /// </summary>
    public Godot.Collections.Dictionary GetLedgerTotals()
    {
        var revenue = new Godot.Collections.Dictionary();
        var cost = new Godot.Collections.Dictionary();
        var fs = SimulationEngine?.FinancialStatements;
        if (fs != null)
        {
            foreach (var kv in fs.RevenueBreakdown.OrderBy(k => k.Key, System.StringComparer.Ordinal))
                revenue[kv.Key] = (double)kv.Value;
            foreach (var kv in fs.CostBreakdown.OrderBy(k => k.Key, System.StringComparer.Ordinal))
                cost[kv.Key] = (double)kv.Value;
        }
        return new Godot.Collections.Dictionary { ["revenue"] = revenue, ["cost"] = cost };
    }
}
