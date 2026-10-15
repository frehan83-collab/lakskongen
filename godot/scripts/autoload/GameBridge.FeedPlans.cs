using System;
using System.Linq;
using Godot;
using LakskongenSim.Core;
using LakskongenSim.Farm;

// GameBridge.FeedPlans.cs — partial class slice (see project docs).
// <summary>
// Feeding plans (roadmap U2-04): the Fôringsplan card in the Pens tab sets a
// site's ration (a share of its fish's appetite), its feed recipe and how
// long the plan holds, or the same plan for every site ("Fôr alle"), and
// previews a plan's next 30 days (twin-lite, SimulationEngine.ProjectFeedPlan:
// pure, on the engine's own growth law). Recipes travel as ids: "standard",
// "high_energy", "economy".
// </summary>
public partial class GameBridge
{
    /// <summary>
    /// Set <paramref name="siteId"/>'s feeding plan. Keys: success, message,
    /// error_key ("" | engine_not_ready | site_not_found | ration_out_of_range |
    /// window_out_of_range | unknown_recipe), plan (as <see cref="GetFeedPlan"/>).
    /// </summary>
    public Godot.Collections.Dictionary SetFeedPlan(string siteId, double rationPct, string recipe, int windowDays)
    {
        var engine = SimulationEngine;
        if (engine?.State == null) return FeedPlanFailure("engine_not_ready", "Engine not initialized");
        if (!TryParseRecipe(recipe, out var kind)) return FeedPlanFailure("unknown_recipe", $"Unknown recipe '{recipe}'");
        var outcome = engine.SetFeedPlan(siteId, rationPct, kind, windowDays);
        var result = FeedPlanOutcome(outcome);
        if (outcome.Ok)
        {
            result["plan"] = GetFeedPlan(siteId);
            EmitSignal(SignalName.FarmActionCompleted, "feed_plan", siteId, true);
            EmitSignal(SignalName.GameStateUpdated);
        }
        return result;
    }

    /// <summary>"Fôr alle": the same plan for every sea site. Keys as <see cref="SetFeedPlan"/> plus site_count.</summary>
    public Godot.Collections.Dictionary SetFeedPlanForAll(double rationPct, string recipe, int windowDays)
    {
        var engine = SimulationEngine;
        if (engine?.State == null) return FeedPlanFailure("engine_not_ready", "Engine not initialized");
        if (!TryParseRecipe(recipe, out var kind)) return FeedPlanFailure("unknown_recipe", $"Unknown recipe '{recipe}'");
        var outcome = engine.SetFeedPlanForAllSites(rationPct, kind, windowDays);
        var result = FeedPlanOutcome(outcome);
        result["site_count"] = outcome.Ok ? (int)outcome.Value : 0;
        if (outcome.Ok)
        {
            EmitSignal(SignalName.FarmActionCompleted, "feed_plan", "all_sites", true);
            EmitSignal(SignalName.GameStateUpdated);
        }
        return result;
    }

    /// <summary>
    /// The plan that feeds a site today. Keys: site_id, standard (bool: full
    /// appetite, standard feed), ration_pct, recipe (id), start_day, end_day
    /// (-1 = until changed), days_left (-1 = until changed). An unknown site
    /// reports the standard plan with site_id "".
    /// </summary>
    public Godot.Collections.Dictionary GetFeedPlan(string siteId)
    {
        var engine = SimulationEngine;
        bool known = engine?.State?.SeaSites.Any(s => s.Id == siteId) ?? false;
        var plan = known ? engine!.FeedPlanOf(siteId) : null;
        int today = engine?.State?.Day ?? 0;
        return new Godot.Collections.Dictionary
        {
            ["site_id"] = known ? siteId : "",
            ["standard"] = plan == null,
            ["ration_pct"] = (plan?.RationFraction ?? 1.0) * 100.0,
            ["recipe"] = RecipeId(plan?.Recipe ?? FeedRecipeKind.Standard),
            ["start_day"] = plan?.StartDay ?? -1,
            ["end_day"] = plan?.EndDay ?? -1,
            ["days_left"] = plan != null && plan.EndDay >= 0 ? Math.Max(0, plan.EndDay - today) : -1
        };
    }

    /// <summary>Every sea site's plan today, in site order (keys as <see cref="GetFeedPlan"/>).</summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetFeedPlans()
    {
        var list = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var sites = SimulationEngine?.State?.SeaSites;
        if (sites == null) return list;
        foreach (var site in sites)
            list.Add(GetFeedPlan(site.Id));
        return list;
    }

    /// <summary>
    /// A plan's next <paramref name="days"/> days at a site (0 = the card's
    /// calibrated horizon), projected without touching the game. Keys: ok,
    /// days, fish, ration_pct, recipe, growth_kg_per_day, feed_kg_per_day,
    /// cost_nok_per_day, fcr, biological_fcr, cost_per_kg_growth,
    /// start_mean_weight_kg, end_mean_weight_kg, growth_kg, feed_kg, cost_nok.
    /// </summary>
    public Godot.Collections.Dictionary PreviewFeedPlan(string siteId, double rationPct, string recipe, int days)
    {
        var engine = SimulationEngine;
        var result = new Godot.Collections.Dictionary { ["ok"] = false };
        if (engine?.State == null || !TryParseRecipe(recipe, out var kind)) return result;
        int horizon = days > 0 ? days : engine.FeedPlanCalibration.ProjectionDays;
        var p = engine.ProjectFeedPlan(siteId, rationPct, kind, horizon);
        if (p == null) return result;
        result["ok"] = true;
        result["days"] = p.Days;
        result["fish"] = p.Fish;
        result["ration_pct"] = p.RationFraction * 100.0;
        result["recipe"] = RecipeId(p.Recipe);
        result["growth_kg_per_day"] = p.GrowthKgPerDay;
        result["feed_kg_per_day"] = p.FeedKgPerDay;
        result["cost_nok_per_day"] = p.CostNokPerDay;
        result["fcr"] = p.Fcr;
        result["biological_fcr"] = p.BiologicalFcr;
        result["cost_per_kg_growth"] = p.CostPerKgGrowthNok;
        result["start_mean_weight_kg"] = p.Fish > 0 ? p.StartBiomassKg / p.Fish : 0.0;
        result["end_mean_weight_kg"] = p.EndMeanWeightKg;
        result["growth_kg"] = p.GrowthKg;
        result["feed_kg"] = p.FeedKg;
        result["cost_nok"] = p.CostNok;
        return result;
    }

    /// <summary>
    /// What the card offers. Keys: restricted_pct, moderate_pct, full_pct (the
    /// slider's stops), min_ration_pct, max_window_days, projection_days, and
    /// recipes (Array of {id, fcr_factor, price_factor, price_per_kg,
    /// astaxanthin_ppm, protein_pct, fillet_grade}).
    /// </summary>
    public Godot.Collections.Dictionary GetFeedPlanOptions()
    {
        var engine = SimulationEngine;
        var cal = engine?.FeedPlanCalibration ?? LakskongenSim.Calibration.CalibrationRegistry.Default.FeedPlan;
        decimal basePrice = engine?.Feed?.FeedCostPerKg ?? LakskongenSim.Calibration.CalibrationRegistry.Default.Feed.FeedPricePerKgFeedNOK;
        var recipes = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (FeedRecipeKind kind in Enum.GetValues<FeedRecipeKind>())
        {
            var effect = FeedRecipeEffect.Of(kind, cal);
            recipes.Add(new Godot.Collections.Dictionary
            {
                ["id"] = RecipeId(kind),
                ["fcr_factor"] = effect.FcrFactor,
                ["price_factor"] = effect.PriceFactor,
                ["price_per_kg"] = (double)basePrice * effect.PriceFactor,
                ["astaxanthin_ppm"] = effect.AstaxanthinPpm,
                ["protein_pct"] = effect.ProteinPct,
                ["fillet_grade"] = LakskongenSim.Processing.FeedRecipe.Grade(effect.ProteinPct, effect.AstaxanthinPpm)
            });
        }
        return new Godot.Collections.Dictionary
        {
            ["restricted_pct"] = cal.RestrictedRation * 100.0,
            ["moderate_pct"] = cal.ModerateRation * 100.0,
            ["full_pct"] = 100.0,
            ["min_ration_pct"] = cal.MinRation * 100.0,
            ["max_window_days"] = cal.MaxWindowDays,
            ["projection_days"] = cal.ProjectionDays,
            ["recipes"] = recipes
        };
    }

    private static Godot.Collections.Dictionary FeedPlanOutcome(CommandResult outcome) => new()
    {
        ["success"] = outcome.Ok,
        ["message"] = outcome.Message,
        ["error_key"] = outcome.ErrorKey
    };

    private static Godot.Collections.Dictionary FeedPlanFailure(string key, string message) => new()
    {
        ["success"] = false,
        ["message"] = message,
        ["error_key"] = key
    };

    /// <summary>The id GDScript uses for a recipe.</summary>
    private static string RecipeId(FeedRecipeKind kind) => kind switch
    {
        FeedRecipeKind.HighEnergy => "high_energy",
        FeedRecipeKind.Economy => "economy",
        _ => "standard"
    };

    private static bool TryParseRecipe(string id, out FeedRecipeKind kind)
    {
        switch (id ?? "")
        {
            case "standard": kind = FeedRecipeKind.Standard; return true;
            case "high_energy": kind = FeedRecipeKind.HighEnergy; return true;
            case "economy": kind = FeedRecipeKind.Economy; return true;
            default: kind = FeedRecipeKind.Standard; return false;
        }
    }
}
