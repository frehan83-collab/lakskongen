using Godot;
using LakskongenSim.Core;
using Farm = LakskongenSim.Farm;

// GameBridge.Pens.cs — partial class slice (roadmap S2-01b).
// <summary>
// Pens slice: a site's real pens (production units) for the stocking card and
// the fjord's pen layout, the per-pen stocking verb with its preview, and a
// delousing of chosen pens. Thin projection: the engine owns the pens, the
// limits (§ 47a fish, § 25 projected density) and the checks
// (SimulationEngine.Pens.cs); reasons travel as keys whose text is in
// translations.csv (CAPACITY_REASON_PEN_*, PENS_*).
// </summary>
/// <summary>
/// GameBridge partial — S2-01b real pens: per-pen state, stocking, delousing.
/// </summary>
public partial class GameBridge : Node
{
    /// <summary>
    /// One pen as a dictionary (the entries of GetSitePens and of
    /// GetSeaSiteDetails' "pens"): "id" ("po3_nord/p03"), "number" (from 1,
    /// the mooring order), "population_id" ("" when empty), "occupied",
    /// "fish_count", "biomass_kg", "mean_weight_kg", "weight_cv",
    /// "lice_per_fish", "density_kg_m3", "volume_m3", "circumference_m",
    /// "net_depth_m", "ration", "fasting".
    /// </summary>
    private static Godot.Collections.Dictionary PenDictionary(PenView pen) => new()
    {
        ["id"] = pen.PenId,
        ["number"] = pen.Number,
        ["population_id"] = pen.PopulationId,
        ["occupied"] = pen.FishCount > 0,
        ["fish_count"] = pen.FishCount,
        ["biomass_kg"] = pen.BiomassKg,
        ["mean_weight_kg"] = pen.MeanWeightKg,
        ["weight_cv"] = pen.WeightCv,
        ["lice_per_fish"] = pen.LicePerFish,
        ["density_kg_m3"] = pen.DensityKgPerM3,
        ["volume_m3"] = pen.VolumeM3,
        ["circumference_m"] = pen.CircumferenceM,
        ["net_depth_m"] = pen.NetDepthM,
        ["ration"] = pen.Ration,
        ["fasting"] = pen.Fasting,
    };

    /// <summary>The pens of a site in mooring order (empty when the site is unknown); keys as <see cref="PenDictionary"/>.</summary>
    private Godot.Collections.Array<Godot.Collections.Dictionary> PenArrayOf(string siteId)
    {
        var pens = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        if (SimulationEngine == null) return pens;
        foreach (var view in SimulationEngine.PenViewsOf(siteId))
            pens.Add(PenDictionary(view));
        return pens;
    }

    /// <summary>
    /// The pens of a sea site, in mooring order: "id", "number",
    /// "population_id", "occupied", "fish_count", "biomass_kg",
    /// "mean_weight_kg", "weight_cv", "lice_per_fish", "density_kg_m3",
    /// "volume_m3", "circumference_m", "net_depth_m", "ration", "fasting".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetSitePens(string siteId) => PenArrayOf(siteId);

    /// <summary>
    /// What stocking <paramref name="count"/> smolts in one pen would do (the
    /// checks stock_smolts_in_pen runs). Keys: "success", "allowed",
    /// "reason_key" (engine key, "" when allowed), "label_key"
    /// (CAPACITY_REASON_*; "" when allowed), "message", "pen_id", "pen_number",
    /// "volume_m3", "max_fish_per_pen", "density_limit_kg_m3",
    /// "projected_peak_density_kg_m3", "projected_peak_day",
    /// "projected_peak_weight_kg", "max_count" (most smolts the pen takes now).
    /// </summary>
    public Godot.Collections.Dictionary PreviewPenStocking(string siteId, string penId, int count,
        string strainId = "standard", int qualityTier = 0)
    {
        if (SimulationEngine?.State == null)
            return new Godot.Collections.Dictionary { ["success"] = false, ["allowed"] = false, ["message"] = "Not initialized" };
        var p = SimulationEngine.PreviewPenStocking(siteId, penId, count, strainId, (Farm.SmoltQualityTier)qualityTier);
        return new Godot.Collections.Dictionary
        {
            ["success"] = p.ReasonKey != "site_not_found" && p.ReasonKey != PenReasons.NotFound,
            ["allowed"] = p.Allowed,
            ["reason_key"] = p.ReasonKey,
            ["label_key"] = ReasonLabelKey(p.ReasonKey),
            ["message"] = p.Message,
            ["pen_id"] = p.PenId,
            ["pen_number"] = p.PenNumber,
            ["volume_m3"] = p.VolumeM3,
            ["max_fish_per_pen"] = p.MaxFishPerPen,
            ["density_limit_kg_m3"] = p.DensityLimitKgPerM3,
            ["projected_peak_density_kg_m3"] = p.ProjectedPeakDensityKgPerM3,
            ["projected_peak_day"] = p.ProjectedPeakDay,
            ["projected_peak_weight_kg"] = p.ProjectedPeakWeightKg,
            ["max_count"] = p.MaxCount,
        };
    }

    /// <summary>
    /// Stock smolts in one pen of a site (S2-01b). Keys: "success",
    /// "reason_key", "label_key", "message", "fish", "site_id", "pen_id".
    /// Emits FarmActionCompleted("stock_smolts", siteId, success) and, on
    /// success, GameStateUpdated.
    /// </summary>
    public Godot.Collections.Dictionary StockSmoltsInPen(string siteId, string penId, int count,
        string strainId = "standard", int qualityTier = 0)
    {
        var engine = SimulationEngine;
        if (engine == null)
        {
            EmitSignal(SignalName.FarmActionCompleted, "stock_smolts", siteId, false);
            return new Godot.Collections.Dictionary { ["success"] = false, ["reason_key"] = "engine_not_ready", ["message"] = "Not initialized" };
        }
        var result = engine.StockSmoltsInPen(siteId, penId, count, strainId, (Farm.SmoltQualityTier)qualityTier);
        EmitSignal(SignalName.FarmActionCompleted, "stock_smolts", siteId, result.Ok);
        if (result.Ok) EmitSignal(SignalName.GameStateUpdated);
        else GD.PrintErr($"GameBridge: StockSmoltsInPen failed - {result.Message}");
        return new Godot.Collections.Dictionary
        {
            ["success"] = result.Ok,
            ["reason_key"] = result.ErrorKey,
            ["label_key"] = ReasonLabelKey(result.ErrorKey),
            ["message"] = result.Message,
            ["fish"] = (int)result.Value,
            ["site_id"] = siteId,
            ["pen_id"] = penId,
        };
    }

    /// <summary>
    /// Delouse chosen pens of a site (pen ids or numbers; empty = the whole
    /// site, as OrderDelousing). Returns true on success; emits
    /// FarmActionCompleted("delousing", siteId, success).
    /// </summary>
    public bool DelousePens(string siteId, string method, Godot.Collections.Array<string> penIds)
    {
        var engine = SimulationEngine;
        if (engine == null)
        {
            EmitSignal(SignalName.FarmActionCompleted, "delousing", siteId, false);
            return false;
        }
        var result = engine.DelousePens(siteId, method, PenIdsOf(penIds));
        EmitSignal(SignalName.FarmActionCompleted, "delousing", siteId, result.Ok);
        if (result.Ok) EmitSignal(SignalName.GameStateUpdated);
        else GD.PrintErr($"GameBridge: DelousePens failed - {result.Message}");
        return result.Ok;
    }

    /// <summary>The translation key of a capacity or pen reason ("" for none): CAPACITY_REASON_&lt;KEY&gt;.</summary>
    private static string ReasonLabelKey(string reasonKey)
        => string.IsNullOrEmpty(reasonKey) ? "" : "CAPACITY_REASON_" + reasonKey.ToUpperInvariant();
}
