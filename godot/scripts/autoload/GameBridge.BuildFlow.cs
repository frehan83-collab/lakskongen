using Godot;
using System.Linq;
using LakskongenSim.Core;
using Farm = LakskongenSim.Farm;

// GameBridge.BuildFlow.cs — partial class slice (roadmap F2-31; ui·farm's).
// <summary>
// The family-first build flow (docs/FARM_TYPES_SPEC.md §6.1-6.2): the build
// preview of a farm-type variant on a plot (the land or sea lane's own
// preview plus the money: capex, capacity cost with the licence, first
// harvest, payback year, a 36-month cash runway and the bank's pre-filled
// loan), the site lens's plots and the diagnosis strip's constraints. Thin
// projections of SimulationEngine.BuildFlow.cs and .BuildDiagnosis.cs: every
// one a read, so a ghost placement or a preview changes nothing. Confirm uses
// the lanes' own verbs (apply_land_permits, build_land_site,
// build_offshore_unit).
// </summary>
/// <summary>
/// GameBridge partial — F2-31 family-first build flow (ui·farm's slice).
/// </summary>
public partial class GameBridge : Node
{
    /// <summary>
    /// The build preview of <paramref name="variantId"/> ("post_smolt", "hfs",
    /// "ras", "of1", "havfarm") on <paramref name="plotId"/>.
    /// <paramref name="option"/> picks the HFS size ("hfs_lot" for the full
    /// lot, else the pilot lot); <paramref name="contract"/> "managed" or
    /// "turnkey"; <paramref name="equityShare"/> 1.0 or the consortium's share
    /// (sea); <paramref name="mitigations"/> price a land plan's permit cases.
    /// Keys: "ok", "reason_key", "variant", "family", "plot_id", "template" (a
    /// land template id, "" at sea), "offshore_type" (the build verb's farm
    /// type at sea, "" on land), "economics" (see BuildEconomicsDictionary),
    /// "land" (PreviewLandBuild's keys, land only), "offshore"
    /// (PreviewOffshoreBuild's keys, sea only). A read.
    /// </summary>
    public Godot.Collections.Dictionary PreviewFarmBuild(string variantId, string plotId, string option, string contract,
        double equityShare, Godot.Collections.Array mitigations)
    {
        var result = new Godot.Collections.Dictionary
        {
            ["ok"] = false, ["variant"] = variantId ?? "", ["plot_id"] = plotId ?? "", ["template"] = "", ["offshore_type"] = "",
        };
        if (SimulationEngine?.State == null) { result["reason_key"] = "engine_not_ready"; return result; }
        Farm.Land.LandTemplate? asked = option == "hfs_lot" ? Farm.Land.LandTemplate.HfsLot : null;
        var preview = SimulationEngine.PreviewFarmBuild(variantId ?? "", plotId ?? "", LandContract(contract), equityShare,
            LandMitigations(mitigations), asked);
        result["ok"] = preview.Ok;
        result["reason_key"] = preview.ReasonKey;
        result["family"] = preview.FamilyId;
        if (!preview.Ok) return result;
        result["economics"] = BuildEconomicsDictionary(preview.Economics);
        if (preview.Land != null)
        {
            result["template"] = LandTemplateId(preview.Land.Template);
            result["land"] = LandPreviewDictionary(preview.Land);
        }
        if (preview.Offshore != null)
        {
            string type = preview.Offshore.Type == Farm.OffshoreType.ShipShaped ? "ship_shaped" : "semi_submersible";
            result["offshore_type"] = type;
            result["offshore"] = PreviewOffshoreBuild(plotId ?? "", type, contract, equityShare);
        }
        return result;
    }

    /// <summary>
    /// The money of a build preview. Keys: "capex_nok", "overrun_p50_nok",
    /// "overrun_p90_nok", "licence_nok", "startup_nok",
    /// "capacity_t_per_year", "capacity_cost_nok_per_t_year",
    /// "farming_cost_nok_per_kg" (float or null), "price_nok_per_kg",
    /// "permit_wait_days", "start_day", "first_harvest_day", "payback_day",
    /// "payback_year" (-1 when the margin never pays it back),
    /// "yearly_margin_nok", "equity_share", "operating_nok_per_month",
    /// "loan_nok", "loan_rate", "loan_term_months", "loan_room_nok",
    /// "lowest_cash_p50_nok", "lowest_cash_p90_nok", "lowest_month",
    /// "runway" [ { "month", "day", "cash_p50_nok", "cash_p90_nok",
    /// "cash_with_loan_nok", "payments_nok", "operating_nok", "new_unit_nok" } ].
    /// </summary>
    private static Godot.Collections.Dictionary BuildEconomicsDictionary(FarmBuildEconomics e)
    {
        var runway = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var m in e.Runway)
            runway.Add(new Godot.Collections.Dictionary
            {
                ["month"] = m.Month,
                ["day"] = m.Day,
                ["cash_p50_nok"] = (double)m.CashP50Nok,
                ["cash_p90_nok"] = (double)m.CashP90Nok,
                ["cash_with_loan_nok"] = (double)m.CashWithLoanNok,
                ["payments_nok"] = (double)m.PaymentsNok,
                ["operating_nok"] = (double)m.OperatingNok,
                ["new_unit_nok"] = (double)m.NewUnitNok,
            });
        return new Godot.Collections.Dictionary
        {
            ["capex_nok"] = (double)e.CompanyCapexNok,
            ["overrun_p50_nok"] = (double)e.OverrunP50Nok,
            ["overrun_p90_nok"] = (double)e.OverrunP90Nok,
            ["licence_nok"] = (double)e.LicenceNok,
            ["startup_nok"] = (double)e.StartupCostsNok,
            ["capacity_t_per_year"] = e.CapacityTonnesPerYear,
            ["capacity_cost_nok_per_t_year"] = e.CapacityCostNokPerTonneYear,
            ["farming_cost_nok_per_kg"] = e.FarmingCostNokPerKgHog.HasValue ? Variant.From(e.FarmingCostNokPerKgHog.Value) : default,
            ["price_nok_per_kg"] = e.PriceNokPerKgHog,
            ["permit_wait_days"] = e.PermitWaitDays,
            ["start_day"] = e.StartDay,
            ["first_harvest_day"] = e.FirstHarvestDay,
            ["payback_day"] = e.PaybackDay,
            ["payback_year"] = e.PaybackYear,
            ["yearly_margin_nok"] = (double)e.YearlyMarginNok,
            ["equity_share"] = e.EquityShare,
            ["operating_nok_per_month"] = (double)e.OperatingNokPerMonth,
            ["loan_nok"] = (double)e.LoanNok,
            ["loan_rate"] = e.LoanRate,
            ["loan_term_months"] = e.LoanTermMonths,
            ["loan_room_nok"] = (double)e.LoanRoomNok,
            ["lowest_cash_p50_nok"] = (double)e.LowestCashP50Nok,
            ["lowest_cash_p90_nok"] = (double)e.LowestCashP90Nok,
            ["lowest_month"] = e.LowestMonth,
            ["runway"] = runway,
        };
    }

    /// <summary>
    /// Which variants the player may start now. Keys: variant id → {
    /// "startable" (every gate the build path does not waive is met),
    /// "waived" (Array of the waived gates' kind names: "Funds" always,
    /// "ZonedPad" on land, "DevelopmentLicence" at sea) }.
    /// </summary>
    public Godot.Collections.Dictionary GetFarmTypeStarts()
    {
        var result = new Godot.Collections.Dictionary();
        if (SimulationEngine?.State == null) return result;
        foreach (var family in SimulationEngine.GetFarmTypeCatalog())
        {
            var waived = new Godot.Collections.Array<string>();
            foreach (var kind in SimulationEngine.StartWaivedGates(family.Family.Id)) waived.Add(kind.ToString());
            foreach (var v in family.Variants)
                result[v.Variant.Id] = new Godot.Collections.Dictionary
                {
                    ["startable"] = SimulationEngine.CanStartFarmType(v.Variant.Id),
                    ["waived"] = waived.Duplicate(),
                };
        }
        return result;
    }

    /// <summary>
    /// QA-only (F2-31 tests): a digest of the whole engine state, the save as a
    /// save writes it and every random stream's position. Two equal digests
    /// mean nothing in the game changed (a ghost placement, a preview). Not
    /// reachable from player UI.
    /// </summary>
    public string QaStateDigest()
    {
        var engine = SimulationEngine;
        if (engine?.State == null) return "";
        engine.PrepareForSave();
        var text = new System.Text.StringBuilder(System.Text.Json.JsonSerializer.Serialize(engine.State, SaveJson.Options));
        foreach (var kv in engine.Rng.CaptureStates().OrderBy(k => k.Key, System.StringComparer.Ordinal))
            text.Append('\n').Append(kv.Key).Append('=').Append(kv.Value);
        return System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text.ToString())));
    }

    /// <summary>
    /// QA-only (F2-31 tests): set up a family's gates the way a career
    /// reaches them, so a UI flow can then be driven by input alone: research
    /// a node at once; with <paramref name="postSmoltPlotId"/>, commission an
    /// operating post-smolt module there (the scenario/test build, no
    /// permits); with <paramref name="firstHarvest"/>, count a harvest sold.
    /// Not reachable from player UI.
    /// </summary>
    public bool QaOpenFarmFamily(string researchNodeId, string postSmoltPlotId, bool firstHarvest)
    {
        var engine = SimulationEngine;
        if (engine?.State == null || engine.Research == null) return false;
        if (!engine.Research.Nodes.ContainsKey(researchNodeId ?? "")) return false;
        engine.Research.ForceUnlock(researchNodeId!);
        if (firstHarvest && engine.State.HarvestBatchesSold < 1) engine.State.HarvestBatchesSold = 1;
        if (!string.IsNullOrEmpty(postSmoltPlotId)
            && !engine.CommissionLandSite(postSmoltPlotId, Farm.Land.LandTemplate.PostSmoltModule).Ok)
            return false;
        EmitSignal(SignalName.GameStateUpdated);
        return true;
    }

    /// <summary>
    /// The diagnosis strip. Keys: "binding" (the binding constraint, empty when
    /// the company farms nothing yet) and "constraints" (every one, binding
    /// first). A constraint: "key" ("lice", "capacity", "temperature",
    /// "oxygen", "capital"), "label_key" ("BUILD_DIAG_" + key), "score" (0-1),
    /// "area" (0 for the company), "value", "reference", "relieved_by"
    /// (Array of family ids: "land", "hav").
    /// </summary>
    public Godot.Collections.Dictionary GetBuildDiagnosis()
    {
        var all = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        var result = new Godot.Collections.Dictionary { ["binding"] = new Godot.Collections.Dictionary(), ["constraints"] = all };
        if (SimulationEngine?.State == null) return result;
        var diagnosis = SimulationEngine.GetBuildDiagnosis();
        foreach (var c in diagnosis.Constraints)
        {
            var relieved = new Godot.Collections.Array<string>();
            foreach (var family in c.RelievedBy) relieved.Add(family);
            all.Add(new Godot.Collections.Dictionary
            {
                ["key"] = c.Key,
                ["label_key"] = "BUILD_DIAG_" + c.Key.ToUpperInvariant(),
                ["score"] = c.Score,
                ["area"] = c.ProductionArea,
                ["value"] = c.Value,
                ["reference"] = c.Reference,
                ["relieved_by"] = relieved,
            });
        }
        if (all.Count > 0) result["binding"] = all[0];
        return result;
    }

    /// <summary>
    /// The site lens's plots of a family ("land" pads or "hav" slots). Keys:
    /// "id", "name", "family", "free", "owner_unit_id", "x", "z",
    /// "real_distance_km", "width_m", "depth_m", "grow_out_lots",
    /// "post_smolt_modules", "deep_intake_reach_m", "pump_m3s_per_lot",
    /// "grid_capacity_mw", "grid_feeds", "zoned", "pre_zoned",
    /// "nature_sensitivity", "blast_cost_nok", "exposure" ("" on a pad),
    /// "water_depth_m", "hs100_m", "winter_mean_hs_m", "summer_mean_hs_m",
    /// "lice_external_min", "lice_external_max".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetBuildPlots(string familyId)
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        if (SimulationEngine?.State == null) return result;
        foreach (var p in SimulationEngine.GetBuildPlots(familyId ?? ""))
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = p.Id,
                ["name"] = p.Name,
                ["family"] = p.FamilyId,
                ["free"] = p.IsFree,
                ["owner_unit_id"] = p.OwnerUnitId,
                ["x"] = p.WorldX,
                ["z"] = p.WorldZ,
                ["real_distance_km"] = p.RealDistanceKm,
                ["width_m"] = p.PadWidthM,
                ["depth_m"] = p.PadDepthM,
                ["grow_out_lots"] = p.GrowOutLots,
                ["post_smolt_modules"] = p.PostSmoltModules,
                ["deep_intake_reach_m"] = p.DeepIntakeReachM,
                ["pump_m3s_per_lot"] = p.PumpCapacityM3sPerLot,
                ["grid_capacity_mw"] = p.GridCapacityMW,
                ["grid_feeds"] = p.GridFeeds,
                ["zoned"] = p.Zoned,
                ["pre_zoned"] = p.PreZoned,
                ["nature_sensitivity"] = p.NatureSensitivity,
                ["blast_cost_nok"] = (double)p.BlastCostNok,
                ["exposure"] = p.Exposure,
                ["water_depth_m"] = p.WaterDepthM,
                ["hs100_m"] = p.Hs100M,
                ["winter_mean_hs_m"] = p.WinterMeanHsM,
                ["summer_mean_hs_m"] = p.SummerMeanHsM,
                ["lice_external_min"] = p.LiceExternalMin,
                ["lice_external_max"] = p.LiceExternalMax,
            });
        return result;
    }
}
