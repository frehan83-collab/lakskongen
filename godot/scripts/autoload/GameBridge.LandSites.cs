using Godot;
using System.Linq;
using LakskongenSim.Core;
using Farm = LakskongenSim.Farm;

// GameBridge.LandSites.cs — partial class slice (roadmap F2-03; the land lane's).
// <summary>
// Land slice: the land sites (grow-out and post-smolt) and the land pads they
// stand on, as lists. Thin projection of the engine's views
// (SimulationEngine.LandSiteViews, SitePlotViews; Core/FarmUnitViews.cs).
// F2-03 owns the list verbs above the lane block; the land lane (F2-12 water
// and tanks, F2-13 post-smolt, F2-14 permits and construction, F2-15 biology,
// F2-16 incidents) appends its verbs only inside the marked block, and the sea
// lane never edits this file (FARM_TYPES_SPEC §7.6).
// </summary>
/// <summary>
/// GameBridge partial — F2-03 land sites and pads (the land lane's slice).
/// </summary>
public partial class GameBridge : Node
{
    /// <summary>
    /// The company's land sites, grow-out and post-smolt, in list order. Keys:
    /// "id", "name", "kind" ("GrowOut", "PostSmolt"), "plot_id", "zone_id",
    /// "tanks", "tank_volume_m3", "capacity_tonnes_per_year", "modules",
    /// "fish_capacity" (a post-smolt site's fish at once), "transfer_weight_g",
    /// "populations", "fish_count", "biomass_tonnes", "mean_weight_kg",
    /// "water_temp_c", "oxygen_mgl", "co2_mgl", "operational",
    /// "construction_progress", "employee_count".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetLandSites()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        if (SimulationEngine?.State == null) return result;
        foreach (var site in SimulationEngine.LandSiteViews())
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = site.Id,
                ["name"] = site.Name,
                ["kind"] = site.Kind,
                ["plot_id"] = site.PlotId,
                ["zone_id"] = site.ZoneId,
                ["tanks"] = site.Tanks,
                ["tank_volume_m3"] = site.TankVolumeM3,
                ["capacity_tonnes_per_year"] = site.CapacityTonnesPerYear,
                ["modules"] = site.Modules,
                ["fish_capacity"] = site.FishCapacity,
                ["transfer_weight_g"] = site.TransferWeightG,
                ["populations"] = site.Populations,
                ["fish_count"] = site.FishCount,
                ["biomass_tonnes"] = site.BiomassTonnes,
                ["mean_weight_kg"] = site.MeanWeightKg,
                ["water_temp_c"] = site.WaterTemperatureC,
                ["oxygen_mgl"] = site.OxygenMgPerL,
                ["co2_mgl"] = site.Co2MgPerL,
                ["operational"] = site.Operational,
                ["construction_progress"] = site.ConstructionProgress,
                ["employee_count"] = site.EmployeeCount,
            });
        }
        return result;
    }

    /// <summary>
    /// The land pads (spec §4: pad_a "Storholmen", pad_b "Skjærvika",
    /// pad_delta "Elveøyra", then any overflow pad a migrated save uses), free
    /// or held. Keys as PlotDictionary (GameBridge.Offshore.cs).
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetLandPlots()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        if (SimulationEngine?.State == null) return result;
        foreach (var plot in SimulationEngine.SitePlotViews(Farm.PlotKind.LandPad))
            result.Add(PlotDictionary(plot));
        return result;
    }

    // ═══ BEGIN sim·land lane block (F2-12 … F2-16): append land verbs below this line ═══

    // F2-12 / F2-14 (wave4/sim-land-1): the land site's lots and tanks, the
    // build preview (capex, payments with the p50/p90 overrun, the schedule,
    // the first harvest, the permit timeline with refusal odds and drivers),
    // the permit, grid and construction verbs, and the construction state per
    // unit the presenter draws. Thin projections of SimulationEngine.LandSites.cs
    // and .FacilityPermits.cs; every refusal travels as a reason key with its
    // translation key (LAND_REASON_*), templates as ids ("hfs_lot",
    // "hfs_pilot_lot", "ras_hall", "post_smolt_module"), contracts as
    // "managed"/"turnkey". Money is NOK as double (Godot marshals no decimal).

    private static readonly (Farm.Land.LandTemplate Template, string Id)[] LandTemplateIds =
    {
        (Farm.Land.LandTemplate.HfsLot, "hfs_lot"),
        (Farm.Land.LandTemplate.HfsPilotLot, "hfs_pilot_lot"),
        (Farm.Land.LandTemplate.RasHall, "ras_hall"),
        (Farm.Land.LandTemplate.PostSmoltModule, "post_smolt_module"),
    };

    private static string LandTemplateId(Farm.Land.LandTemplate template)
        => LandTemplateIds.First(t => t.Template == template).Id;

    private static bool TryLandTemplate(string id, out Farm.Land.LandTemplate template)
    {
        foreach (var (t, name) in LandTemplateIds)
            if (name == id) { template = t; return true; }
        template = Farm.Land.LandTemplate.HfsLot;
        return false;
    }

    private static ContractForm LandContract(string id) => id == "turnkey" ? ContractForm.Turnkey : ContractForm.Managed;

    private static string LandLabelKey(string reasonKey)
        => string.IsNullOrEmpty(reasonKey) ? ""
            // F2-13: a transfer's destination refuses with S2-01's capacity keys (mtb_*, pen_*, pens_full).
            : reasonKey.StartsWith("mtb_", System.StringComparison.Ordinal) || reasonKey.StartsWith("pen", System.StringComparison.Ordinal)
                ? "CAPACITY_REASON_" + reasonKey.ToUpperInvariant()
                : "LAND_REASON_" + reasonKey.ToUpperInvariant();

    private static string[] LandMitigations(Godot.Collections.Array mitigations)
    {
        var list = new System.Collections.Generic.List<string>();
        if (mitigations != null)
            foreach (var item in mitigations)
                list.Add(item.AsString());
        return list.ToArray();
    }

    private static Godot.Collections.Dictionary LandResult(CommandResult outcome) => new()
    {
        ["success"] = outcome.Ok,
        ["reason_key"] = outcome.ErrorKey ?? "",
        ["label_key"] = LandLabelKey(outcome.ErrorKey ?? ""),
        ["message"] = outcome.Message ?? "",
        ["id"] = outcome.Id ?? "",
        ["value"] = outcome.Value,
        ["value2"] = outcome.Value2,
    };

    private static Godot.Collections.Dictionary LandNotReady() => LandResult(CommandResult.Fail("engine_not_ready", "Engine not initialized"));

    private Godot.Collections.Dictionary LandOutcome(CommandResult outcome)
    {
        if (outcome.Ok) EmitSignal(SignalName.GameStateUpdated);
        return LandResult(outcome);
    }

    /// <summary>
    /// One land site in full (F2-12): its Vannstrategi, energy and oxygen
    /// today, production cap and lots, each lot with its tanks and their water.
    /// Keys: "success", "id", "name", "kind", "plot_id", "reuse",
    /// "deep_intake_share", "setpoint_c", "blend_toward_setpoint",
    /// "daily_energy_kwh", "daily_lox_kg", "capacity_tonnes_per_year",
    /// "annual_cap_tonnes", "employee_count", "denitrification", "lots". A lot:
    /// "id", "template", "template_label_key", "heat_pump_mwe",
    /// "heat_recovery", "biofilter", "biofilter_maturity", "commissioned",
    /// "ready_tanks", "first_tanks_day", "complete_day",
    /// "construction_project_id", "expansion_project_id", "tanks". A tank:
    /// "id", "number", "role", "diameter_m", "volume_m3", "ready",
    /// "population_id", "fish_count", "biomass_kg", "mean_weight_kg",
    /// "density_kg_m3", "temperature_c", "do_mgl", "co2_mgl", "tan_mgl",
    /// "no3_mgl", "appetite".
    /// </summary>
    public Godot.Collections.Dictionary GetLandSiteDetail(string siteId)
    {
        var engine = SimulationEngine;
        var site = engine?.FindLandSite(siteId);
        if (engine?.State == null || site == null)
            return LandResult(CommandResult.Fail(LandSiteReasons.NotFound, $"No land site '{siteId}'"));
        var lots = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var lot in site.Lots)
        {
            var tanks = new Godot.Collections.Array<Godot.Collections.Dictionary>();
            for (int i = 0; i < lot.Tanks.Count; i++)
            {
                var tank = lot.Tanks[i];
                var fish = site.PopulationIn(tank);
                double kg = fish?.GetTotalBiomassKg() ?? 0.0;
                int count = fish?.TotalCount ?? 0;
                tanks.Add(new Godot.Collections.Dictionary
                {
                    ["id"] = tank.Id,
                    ["number"] = tank.Pen.Number,
                    ["role"] = tank.Role.ToString(),
                    ["diameter_m"] = tank.DiameterM,
                    ["volume_m3"] = tank.VolumeM3,
                    ["ready"] = engine.IsTankReadyNow(lot, i),
                    ["population_id"] = tank.Pen.PopulationId,
                    ["fish_count"] = count,
                    ["biomass_kg"] = kg,
                    ["mean_weight_kg"] = count > 0 ? kg / count : 0.0,
                    ["density_kg_m3"] = tank.VolumeM3 > 0.0 ? kg / tank.VolumeM3 : 0.0,
                    ["temperature_c"] = tank.TemperatureC,
                    ["do_mgl"] = tank.DoMgL,
                    ["co2_mgl"] = tank.Co2MgL,
                    ["tan_mgl"] = tank.TanMgL,
                    ["no3_mgl"] = tank.No3NMgL,
                    ["appetite"] = tank.AppetiteFactor,
                });
            }
            lots.Add(new Godot.Collections.Dictionary
            {
                ["id"] = lot.Id,
                ["template"] = LandTemplateId(lot.Template),
                ["template_label_key"] = "LAND_TEMPLATE_" + UpperSnake(lot.Template.ToString()),
                ["heat_pump_mwe"] = lot.HeatPumpMWe,
                ["heat_recovery"] = lot.HeatRecovery,
                ["biofilter"] = lot.Biofilter.ToString(),
                ["biofilter_maturity"] = lot.BiofilterMaturity,
                ["commissioned"] = engine.IsLotCommissionedNow(lot),
                ["ready_tanks"] = Enumerable.Range(0, lot.Tanks.Count).Count(i => engine.IsTankReadyNow(lot, i)),
                ["first_tanks_day"] = engine.LotFirstTanksDayNow(lot),
                ["complete_day"] = engine.LotCompleteDayNow(lot),
                ["construction_project_id"] = lot.ConstructionProjectId,
                ["expansion_project_id"] = lot.ExpansionProjectId,
                ["tanks"] = tanks,
            });
        }
        return new Godot.Collections.Dictionary
        {
            ["success"] = true,
            ["id"] = site.Id,
            ["name"] = site.Name,
            ["kind"] = site.Kind.ToString(),
            ["plot_id"] = site.PlotId,
            ["reuse"] = site.RecirculationRate,
            ["deep_intake_share"] = site.DeepIntakeShare,
            ["setpoint_c"] = site.TargetWaterTemp,
            ["blend_toward_setpoint"] = site.BlendTowardSetpoint,
            ["daily_energy_kwh"] = site.DailyEnergyKWh,
            ["daily_lox_kg"] = site.DailyLoxKg,
            ["capacity_tonnes_per_year"] = site.CapacityTonnesPerYear,
            ["annual_cap_tonnes"] = engine.AnnualProductionCapTonnes(site),
            ["employee_count"] = site.EmployeeCount,
            ["denitrification"] = site.DenitrificationCommitted,
            ["lots"] = lots,
        };
    }

    private Godot.Collections.Dictionary LandPreviewDictionary(LandBuildPreview preview)
    {
        var payments = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var p in preview.Payments)
            payments.Add(new Godot.Collections.Dictionary
            {
                ["phase"] = p.Key,
                ["label_key"] = "LAND_PHASE_" + p.Key.ToUpperInvariant(),
                ["planned_days"] = p.PlannedDays,
                ["start_day"] = p.StartDay,
                ["end_day"] = p.EndDay,
                ["payment_nok"] = (double)p.PaymentNok,
                ["overrun_p50_nok"] = (double)p.OverrunP50Nok,
                ["overrun_p90_nok"] = (double)p.OverrunP90Nok,
            });
        var permits = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var line in preview.Permits)
        {
            var drivers = new Godot.Collections.Array<Godot.Collections.Dictionary>();
            foreach (var d in line.Quote.Drivers)
                drivers.Add(new Godot.Collections.Dictionary
                {
                    ["key"] = d.Key,
                    ["label_key"] = "LAND_DRIVER_" + d.Key.ToUpperInvariant(),
                    ["effect"] = d.Effect,
                });
            permits.Add(new Godot.Collections.Dictionary
            {
                ["kind"] = line.Kind.ToString(),
                ["label_key"] = "LAND_PERMIT_" + UpperSnake(line.Kind.ToString()),
                ["status"] = line.Status,
                ["status_label_key"] = "LAND_PERMIT_STATUS_" + line.Status.ToUpperInvariant(),
                ["case_id"] = line.CaseId,
                ["decision_day"] = line.DecisionDay,
                ["min_days"] = line.Quote.MinDays,
                ["max_days"] = line.Quote.MaxDays,
                ["refusal_probability"] = line.Quote.RefusalProbability,
                ["fee_nok"] = (double)line.Quote.FeeNok,
                ["covered_tonnes"] = line.CoveredTonnes,
                ["drivers"] = drivers,
            });
        }
        var missing = new Godot.Collections.Array<string>();
        foreach (var kind in preview.MissingPermits) missing.Add(kind.ToString());
        return new Godot.Collections.Dictionary
        {
            ["success"] = preview.Verdict == "",
            ["allowed"] = preview.Verdict == "",
            ["reason_key"] = preview.Verdict,
            ["label_key"] = LandLabelKey(preview.Verdict),
            ["plot_id"] = preview.PlotId,
            ["site_id"] = preview.SiteId,
            ["template"] = LandTemplateId(preview.Template),
            ["template_label_key"] = "LAND_TEMPLATE_" + UpperSnake(preview.Template.ToString()),
            ["kind"] = preview.Kind == Farm.Land.LandBuildKind.PilotExpansion ? "pilot_expansion" : "new_lot",
            ["contract"] = preview.Contract == ContractForm.Turnkey ? "turnkey" : "managed",
            ["missing_permits"] = missing,
            ["unit_capex_nok"] = (double)preview.UnitCapexNok,
            ["blasting_nok"] = (double)preview.BlastingNok,
            ["denitrification_nok"] = (double)preview.DenitrificationNok,
            ["budget_nok"] = (double)preview.BudgetNok,
            ["premium_nok"] = (double)preview.PremiumNok,
            ["overrun_p50_nok"] = (double)preview.OverrunP50Nok,
            ["overrun_p90_nok"] = (double)preview.OverrunP90Nok,
            ["payments"] = payments,
            ["start_day"] = preview.StartDay,
            ["first_tanks_day"] = preview.FirstTanksDay,
            ["complete_day"] = preview.CompleteDay,
            ["first_tanks_p50_day"] = preview.Risk.FirstTanksP50Day,
            ["first_tanks_p90_day"] = preview.Risk.FirstTanksP90Day,
            ["complete_p50_day"] = preview.Risk.CompleteP50Day,
            ["complete_p90_day"] = preview.Risk.CompleteP90Day,
            ["cycle_days"] = preview.CycleDays,
            ["first_harvest_day"] = preview.FirstHarvestDay,
            ["permits"] = permits,
            ["package_refusal_probability"] = preview.PackageRefusalProbability,
            ["production_tonnes"] = preview.ProductionTonnes,
            ["design_power_mw"] = preview.DesignPowerMW,
            ["grid_capacity_mw"] = preview.GridCapacityMW,
            ["grid_feeds"] = preview.GridFeeds,
        };
    }

    /// <summary>
    /// The preview of building <paramref name="template"/> ("hfs_lot" …) as a
    /// new land site on a pad under <paramref name="contract"/> ("managed",
    /// "turnkey"), with <paramref name="mitigations"/> pricing the permit cases
    /// still to file. Keys: see LandPreviewDictionary (budget and payments with
    /// the p50/p90 overrun, first tanks and completion planned and p50/p90,
    /// first_harvest_day, permits with refusal odds and drivers, grid). A read.
    /// </summary>
    public Godot.Collections.Dictionary PreviewLandBuild(string plotId, string template, string contract, Godot.Collections.Array mitigations)
    {
        if (SimulationEngine?.State == null) return LandNotReady();
        if (!TryLandTemplate(template, out var t))
            return LandResult(CommandResult.Fail(LandSiteReasons.OutOfRange, $"Unknown template '{template}'"));
        return LandPreviewDictionary(SimulationEngine.PreviewLandBuild(plotId, t, LandContract(contract), LandMitigations(mitigations)));
    }

    /// <summary>The preview of expanding a land site (a pilot's expansion, or its next lot or module). Keys as PreviewLandBuild.</summary>
    public Godot.Collections.Dictionary PreviewLandExpansion(string siteId, string contract)
    {
        if (SimulationEngine?.State == null) return LandNotReady();
        return LandPreviewDictionary(SimulationEngine.PreviewLandExpansion(siteId, LandContract(contract)));
    }

    /// <summary>Apply for every permit a land plan on a pad still needs (<paramref name="lots"/> units of the template in total).</summary>
    public Godot.Collections.Dictionary ApplyLandPermits(string plotId, string template, int lots, Godot.Collections.Array mitigations)
    {
        if (SimulationEngine?.State == null) return LandNotReady();
        if (!TryLandTemplate(template, out var t))
            return LandResult(CommandResult.Fail(LandSiteReasons.OutOfRange, $"Unknown template '{template}'"));
        return LandOutcome(SimulationEngine.ApplyLandPermits(plotId, t, lots, LandMitigations(mitigations)));
    }

    /// <summary>Apply for the zoning of a pad alone.</summary>
    public Godot.Collections.Dictionary ApplyForZoning(string plotId, Godot.Collections.Array mitigations)
        => SimulationEngine?.State == null ? LandNotReady() : LandOutcome(SimulationEngine.ApplyForZoning(plotId, LandMitigations(mitigations)));

    /// <summary>Appeal a refused land permit case (decided in 180 days).</summary>
    public Godot.Collections.Dictionary AppealLandPermit(string caseId)
        => SimulationEngine?.State == null ? LandNotReady() : LandOutcome(SimulationEngine.AppealLandPermit(caseId));

    /// <summary>Apply again for a refused land permit with more mitigations.</summary>
    public Godot.Collections.Dictionary ReapplyLandPermit(string caseId, Godot.Collections.Array mitigations)
        => SimulationEngine?.State == null ? LandNotReady() : LandOutcome(SimulationEngine.ReapplyLandPermit(caseId, LandMitigations(mitigations)));

    /// <summary>Queue a grid project at a pad: "capacity" or "second_feed".</summary>
    public Godot.Collections.Dictionary StartGridProject(string plotId, string kind)
        => SimulationEngine?.State == null ? LandNotReady() : LandOutcome(SimulationEngine.StartGridProject(plotId, kind));

    /// <summary>Start building a land site on a pad (permits approved). Id = the new site.</summary>
    public Godot.Collections.Dictionary BuildLandSite(string plotId, string template, string contract, string name)
    {
        if (SimulationEngine?.State == null) return LandNotReady();
        if (!TryLandTemplate(template, out var t))
            return LandResult(CommandResult.Fail(LandSiteReasons.OutOfRange, $"Unknown template '{template}'"));
        var outcome = SimulationEngine.BuildLandSite(plotId, t, LandContract(contract), name);
        if (outcome.Ok) EmitSignal(SignalName.FarmActionCompleted, "build_land_site", plotId, true);
        return LandOutcome(outcome);
    }

    /// <summary>Expand a land site (a pilot to a full lot, or the next lot or module).</summary>
    public Godot.Collections.Dictionary ExpandLandSite(string siteId, string contract)
        => SimulationEngine?.State == null ? LandNotReady() : LandOutcome(SimulationEngine.ExpandLandSite(siteId, LandContract(contract)));

    /// <summary>Accelerate a land construction project's current phase.</summary>
    public Godot.Collections.Dictionary AccelerateLandConstruction(string projectId)
        => SimulationEngine?.State == null ? LandNotReady() : LandOutcome(SimulationEngine.AccelerateLandConstruction(projectId));

    /// <summary>Cancel a land construction project (the money paid stays spent).</summary>
    public Godot.Collections.Dictionary CancelLandConstruction(string projectId)
        => SimulationEngine?.State == null ? LandNotReady() : LandOutcome(SimulationEngine.CancelLandConstruction(projectId));

    /// <summary>
    /// Every land construction project: the presenter's construction state per
    /// unit. Keys: "site_id", "lot_id", "project_id", "kind" ("new_lot",
    /// "pilot_expansion"), "template", "contract", "status", "phase",
    /// "phase_label_key", "phase_index", "phase_count", "phase_progress" (0-1),
    /// "progress" (0-1), "suspended", "first_tanks_day", "complete_day",
    /// "budget_nok", "paid_nok", "accelerated".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetLandConstruction()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        if (SimulationEngine?.State == null) return result;
        foreach (var v in SimulationEngine.LandConstructionViews())
            result.Add(new Godot.Collections.Dictionary
            {
                ["site_id"] = v.SiteId,
                ["lot_id"] = v.LotId,
                ["project_id"] = v.ProjectId,
                ["kind"] = v.Kind,
                ["template"] = LandTemplateId(v.Template),
                ["contract"] = v.Contract == nameof(ContractForm.Turnkey) ? "turnkey" : "managed",
                ["status"] = v.Status,
                ["phase"] = v.PhaseKey,
                ["phase_label_key"] = v.PhaseKey == "" ? "" : "LAND_PHASE_" + v.PhaseKey.ToUpperInvariant(),
                ["phase_index"] = v.PhaseIndex,
                ["phase_count"] = v.PhaseCount,
                ["phase_progress"] = v.PhaseProgress,
                ["progress"] = v.Progress,
                ["suspended"] = v.Suspended,
                ["first_tanks_day"] = v.FirstTanksDay,
                ["complete_day"] = v.CompleteDay,
                ["budget_nok"] = (double)v.BudgetNok,
                ["paid_nok"] = (double)v.PaidNok,
                ["accelerated"] = v.Accelerated,
            });
        return result;
    }

    /// <summary>
    /// The permit cases filed on a pad ("" = every pad). Keys: "id", "kind",
    /// "label_key", "plot_id", "unit_id", "status", "submitted_day",
    /// "decision_day", "decided_day", "refusal_probability", "round", "mitigations".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetLandPermitCases(string plotId)
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        if (SimulationEngine?.State == null) return result;
        foreach (var c in SimulationEngine.PermitCases)
        {
            if (!string.IsNullOrEmpty(plotId) && c.PlotId != plotId) continue;
            var plot = SimulationEngine.SitePlots.FirstOrDefault(p => p.Id == c.PlotId);
            if (plot == null || plot.Kind != Farm.PlotKind.LandPad) continue;
            var mitigations = new Godot.Collections.Array<string>();
            foreach (var m in c.Mitigations) mitigations.Add(m);
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = c.Id,
                ["kind"] = c.Kind.ToString(),
                ["label_key"] = "LAND_PERMIT_" + UpperSnake(c.Kind.ToString()),
                ["plot_id"] = c.PlotId,
                ["unit_id"] = c.UnitId,
                ["status"] = c.Status.ToString(),
                ["submitted_day"] = c.SubmittedDay,
                ["decision_day"] = c.DecisionDay,
                ["decided_day"] = c.DecidedDay,
                ["refusal_probability"] = c.RefusalProbability,
                ["round"] = c.Round,
                ["mitigations"] = mitigations,
            });
        }
        return result;
    }

    /// <summary>What stocking a land tank would do. Keys: "success", "allowed", "reason_key", "label_key", "tank_id", "release_weight_kg", "peak_density_kg_m3", "cycle_days", "max_smolts", "density_room_smolts", "annual_room_smolts", "planned_hog_kg", "cost_nok".</summary>
    public Godot.Collections.Dictionary PreviewLandStocking(string siteId, string tankId, int count, string strainId)
    {
        if (SimulationEngine?.State == null) return LandNotReady();
        var p = SimulationEngine.PreviewLandStocking(siteId, tankId, count, strainId);
        return new Godot.Collections.Dictionary
        {
            ["success"] = p.Allowed,
            ["allowed"] = p.Allowed,
            ["reason_key"] = p.Reason,
            ["label_key"] = LandLabelKey(p.Reason),
            ["message"] = p.Message,
            ["tank_id"] = p.TankId,
            ["release_weight_kg"] = p.ReleaseWeightKg,
            ["peak_density_kg_m3"] = p.PeakDensityKgM3,
            ["cycle_days"] = p.CycleDays,
            ["max_smolts"] = p.MaxSmolts,
            ["density_room_smolts"] = p.DensityRoomSmolts,
            ["annual_room_smolts"] = p.AnnualRoomSmolts,
            ["planned_hog_kg"] = p.PlannedHogKg,
            ["cost_nok"] = (double)p.CostNok,
        };
    }

    /// <summary>Stock a land tank.</summary>
    public Godot.Collections.Dictionary StockLandTank(string siteId, string tankId, int count, string strainId)
        => SimulationEngine?.State == null ? LandNotReady() : LandOutcome(SimulationEngine.StockLandTank(siteId, tankId, count, strainId));

    /// <summary>Grade a share (0-1] of one land tank's fish into an empty, ready tank of the same site.</summary>
    public Godot.Collections.Dictionary GradeTank(string siteId, string fromTankId, string toTankId, double share)
        => SimulationEngine?.State == null ? LandNotReady() : LandOutcome(SimulationEngine.GradeTank(siteId, fromTankId, toTankId, share));

    /// <summary>Harvest a land tank now.</summary>
    public Godot.Collections.Dictionary HarvestLandTank(string siteId, string tankId)
        => SimulationEngine?.State == null ? LandNotReady() : LandOutcome(SimulationEngine.HarvestLandTank(siteId, tankId));

    /// <summary>Set a land site's Vannstrategi (reuse, deep-intake share, setpoint, blend toward the setpoint).</summary>
    public Godot.Collections.Dictionary SetVannstrategi(string siteId, double reuse, double deepIntakeShare, double setpointC, bool blend)
        => SimulationEngine?.State == null ? LandNotReady() : LandOutcome(SimulationEngine.SetVannstrategi(siteId, reuse, deepIntakeShare, setpointC, blend));

    /// <summary>Fit heat recovery to a lot.</summary>
    public Godot.Collections.Dictionary InstallHeatRecovery(string lotId)
        => SimulationEngine?.State == null ? LandNotReady() : LandOutcome(SimulationEngine.InstallHeatRecovery(lotId));

    // F2-13 / F2-15 (wave4/sim-land-2): the land boards' reads and verbs for
    // F2-32: the board (lots and tanks against their comfort bands with the
    // binding constraint, the cohorts against their plans), the Vannstrategi
    // presets with the engine's projection (kWh/kg, growth, maturation risk),
    // competence and the Oppkjøring with its levers, the feeding plan, the
    // harvest planner (weeks, the expected price, projected weights and size
    // grades; quote, plan, cancel), the post-smolt planner (salinity, tiers,
    // destinations with MTB headroom; transfer, sell) and the site's books.
    // Thin projections of SimulationEngine.LandBoard.cs, .LandBiology.cs and
    // .PostSmolt.cs. Statuses travel as "ok"/"warn"/"critical", constraints as
    // ids with LAND_CONSTRAINT_* keys, tiers as "standard"/"large"/"super_smolt".

    private static string LandLevelId(Farm.Land.LandStatusLevel level) => level switch
    {
        Farm.Land.LandStatusLevel.Critical => "critical",
        Farm.Land.LandStatusLevel.Warn => "warn",
        _ => "ok",
    };

    private static string LandTierId(Farm.SmoltQualityTier tier) => tier switch
    {
        Farm.SmoltQualityTier.SuperSmolt => "super_smolt",
        Farm.SmoltQualityTier.Large => "large",
        _ => "standard",
    };

    private static string LandConstraintKey(string constraint)
        => string.IsNullOrEmpty(constraint) ? "" : "LAND_CONSTRAINT_" + constraint.ToUpperInvariant();

    private static System.Collections.Generic.List<string> LandIds(Godot.Collections.Array ids)
    {
        var list = new System.Collections.Generic.List<string>();
        if (ids != null)
            foreach (var item in ids)
                list.Add(item.AsString());
        return list;
    }

    /// <summary>
    /// The land board of a site (spec §6.5 "Tankkart"): its lots in the 3D
    /// layout order, each with its worst stocked tank's status and binding
    /// constraint (the one click to the worst tank) and its exceptions; each
    /// tank with its water ring (o2, co2, tan, temperature, density: value, ok
    /// edge, critical edge, load, level), the binding constraint, its cohort
    /// against the plan (plan weight, share, shortfall days and driver), the
    /// tier it would carry to sea and any harvest plan. Keys: "success", "id",
    /// "name", "kind", "lots" (each: "id", "template", "commissioned", "level",
    /// "binding", "binding_label_key", "worst_tank_id", "exceptions", "tanks").
    /// </summary>
    public Godot.Collections.Dictionary GetLandBoard(string siteId)
    {
        var board = SimulationEngine?.LandBoardOf(siteId);
        if (board == null) return LandResult(CommandResult.Fail(LandSiteReasons.NotFound, $"No land site with tanks '{siteId}'"));
        var lots = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var lot in board.Lots)
        {
            var tanks = new Godot.Collections.Array<Godot.Collections.Dictionary>();
            foreach (var tank in lot.Tanks)
            {
                var segments = new Godot.Collections.Array<Godot.Collections.Dictionary>();
                foreach (var s in tank.Status.Segments)
                    segments.Add(new Godot.Collections.Dictionary
                    {
                        ["key"] = s.Key,
                        ["label_key"] = LandConstraintKey(s.Key),
                        ["value"] = s.Value,
                        ["ok_edge"] = s.OkEdge,
                        ["critical_edge"] = s.CriticalEdge,
                        ["load"] = s.Load,
                        ["level"] = LandLevelId(s.Level),
                    });
                tanks.Add(new Godot.Collections.Dictionary
                {
                    ["id"] = tank.TankId,
                    ["number"] = tank.Number,
                    ["role"] = tank.Role.ToString(),
                    ["ready"] = tank.Ready,
                    ["fish_count"] = tank.Fish,
                    ["mean_weight_kg"] = tank.MeanWeightKg,
                    ["biomass_kg"] = tank.BiomassKg,
                    ["density_kg_m3"] = tank.DensityKgM3,
                    ["level"] = LandLevelId(tank.Status.Level),
                    ["binding"] = tank.Status.Binding,
                    ["binding_label_key"] = LandConstraintKey(tank.Status.Binding),
                    ["load"] = tank.Status.Load,
                    ["segments"] = segments,
                    ["plan_weight_kg"] = tank.PlanWeightKg,
                    ["share_of_plan"] = tank.ShareOfPlan,
                    ["shortfall_days"] = tank.ShortfallDays,
                    ["shortfall_driver"] = tank.ShortfallDriver,
                    ["shortfall_driver_label_key"] = tank.ShortfallDriver == "" ? "" : "LAND_SHORTFALL_" + tank.ShortfallDriver.ToUpperInvariant(),
                    ["stocked_day"] = tank.StockedDay,
                    ["days_in_tank"] = tank.DaysInTank,
                    ["tier"] = LandTierId(tank.Tier),
                    ["tier_label_key"] = "LAND_TIER_" + LandTierId(tank.Tier).ToUpperInvariant(),
                    ["seawater_days"] = tank.SeawaterDays,
                    ["harvest_plan_id"] = tank.HarvestPlanId,
                });
            }
            lots.Add(new Godot.Collections.Dictionary
            {
                ["id"] = lot.LotId,
                ["template"] = LandTemplateId(lot.Template),
                ["commissioned"] = lot.Commissioned,
                ["level"] = LandLevelId(lot.Level),
                ["binding"] = lot.Binding,
                ["binding_label_key"] = LandConstraintKey(lot.Binding),
                ["worst_tank_id"] = lot.WorstTankId,
                ["exceptions"] = lot.Exceptions,
                ["tanks"] = tanks,
            });
        }
        return new Godot.Collections.Dictionary
        {
            ["success"] = true,
            ["id"] = board.SiteId,
            ["name"] = board.Name,
            ["kind"] = board.Kind.ToString(),
            ["lots"] = lots,
        };
    }

    /// <summary>
    /// The three Vannstrategi presets of a site with the engine's projection
    /// of each (spec §6.5): "id" ("sparsom", "balansert", "hoy_tetthet"),
    /// "label_key", "reuse", "deep_intake_share", "blend", "setpoint_c",
    /// "kwh_per_kg", "energy_nok_per_kg", "growth_tonnes_per_year",
    /// "production_index", "cycle_days", "mean_tank_c", "lox_per_kg",
    /// "mean_co2_mgl", "maturation_index", "maturation_risk", "active".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetVannstrategiPresets(string siteId)
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        if (SimulationEngine?.State == null) return result;
        foreach (var p in SimulationEngine.VannstrategiPresetsOf(siteId))
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = p.Id,
                ["label_key"] = "LAND_PRESET_" + p.Id.ToUpperInvariant(),
                ["reuse"] = p.Strategy.Reuse,
                ["deep_intake_share"] = p.Strategy.DeepIntakeShare,
                ["blend"] = p.Strategy.BlendTowardSetpoint,
                ["setpoint_c"] = p.Strategy.SetpointC,
                ["kwh_per_kg"] = p.KWhPerKg,
                ["energy_nok_per_kg"] = p.EnergyNokPerKg,
                ["growth_tonnes_per_year"] = p.GrowthTonnesPerYear,
                ["production_index"] = p.ProductionIndexKgM3Yr,
                ["cycle_days"] = p.CycleDays,
                ["mean_tank_c"] = p.MeanTankC,
                ["lox_per_kg"] = p.LoxPerKg,
                ["mean_co2_mgl"] = p.MeanCo2MgL,
                ["maturation_index"] = p.MaturationIndex,
                ["maturation_risk"] = p.MaturationRisk,
                ["active"] = p.Active,
            });
        return result;
    }

    /// <summary>Set a site's Vannstrategi to a preset ("sparsom", "balansert", "hoy_tetthet").</summary>
    public Godot.Collections.Dictionary ApplyVannstrategiPreset(string siteId, string presetId)
        => SimulationEngine?.State == null ? LandNotReady() : LandOutcome(SimulationEngine.ApplyVannstrategiPreset(siteId, presetId));

    /// <summary>
    /// A site's competence and Oppkjøring (spec §3.3, §6.5): "applies" (a
    /// grow-out site), "competence" and its parts ("start", "research",
    /// "driftsleder", "training", "consultancy", "early_hiring", "cycles"),
    /// "utilisation_cap", "growth_against_plan", "growth_multiplier",
    /// "completed_cycles", "rotation_fish", "harvested_fish", "milestones"
    /// ("cap", "competence", "reached"), "levers" ("id", "label_key", "gain",
    /// "one_off_nok", "per_year_nok", "active", "until_day"), "feed_ration_pct",
    /// "fcr_penalty", "fcr_penalty_until_day", "nameplate_tonnes", "annual_cap_tonnes".
    /// </summary>
    public Godot.Collections.Dictionary GetLandCompetence(string siteId)
    {
        var view = SimulationEngine?.LandCompetenceViewOf(siteId);
        if (view == null) return LandResult(CommandResult.Fail(LandSiteReasons.NotFound, $"No land site with tanks '{siteId}'"));
        var milestones = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var m in view.Milestones)
            milestones.Add(new Godot.Collections.Dictionary { ["cap"] = m.Cap, ["competence"] = m.Competence, ["reached"] = m.Reached });
        var levers = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var l in view.Levers)
            levers.Add(new Godot.Collections.Dictionary
            {
                ["id"] = l.Id,
                ["label_key"] = "LAND_LEVER_" + l.Id.ToUpperInvariant(),
                ["gain"] = l.Gain,
                ["one_off_nok"] = (double)l.OneOffNok,
                ["per_year_nok"] = (double)l.PerYearNok,
                ["active"] = l.Active,
                ["until_day"] = l.UntilDay,
            });
        var b = view.Breakdown;
        return new Godot.Collections.Dictionary
        {
            ["success"] = true,
            ["id"] = view.SiteId,
            ["applies"] = view.Applies,
            ["competence"] = b.Total,
            ["start"] = b.Start,
            ["research"] = b.Research,
            ["driftsleder"] = b.Driftsleder,
            ["training"] = b.Training,
            ["consultancy"] = b.Consultancy,
            ["early_hiring"] = b.EarlyHiring,
            ["cycles"] = b.Cycles,
            ["utilisation_cap"] = view.UtilisationCap,
            ["growth_against_plan"] = view.GrowthAgainstPlan,
            ["growth_multiplier"] = view.GrowthMultiplier,
            ["completed_cycles"] = view.CompletedCycles,
            ["rotation_fish"] = view.RotationFish,
            ["harvested_fish"] = view.HarvestedFish,
            ["milestones"] = milestones,
            ["levers"] = levers,
            ["feed_ration_pct"] = view.FeedRationFraction * 100.0,
            ["fcr_penalty"] = view.FcrPenalty,
            ["fcr_penalty_until_day"] = view.FcrPenaltyUntilDay,
            ["nameplate_tonnes"] = view.NameplateTonnes,
            ["annual_cap_tonnes"] = view.AnnualCapTonnes,
        };
    }

    /// <summary>Hire an operations manager (driftsleder) at a land grow-out site.</summary>
    public Godot.Collections.Dictionary HireDriftsleder(string siteId)
        => SimulationEngine?.State == null ? LandNotReady() : LandOutcome(SimulationEngine.HireDriftsleder(siteId));

    /// <summary>Start a land grow-out site's training programme.</summary>
    public Godot.Collections.Dictionary StartTrainingProgramme(string siteId)
        => SimulationEngine?.State == null ? LandNotReady() : LandOutcome(SimulationEngine.StartTrainingProgramme(siteId));

    /// <summary>Hire an external consultancy at a land grow-out site (12 months).</summary>
    public Godot.Collections.Dictionary HireConsultancy(string siteId)
        => SimulationEngine?.State == null ? LandNotReady() : LandOutcome(SimulationEngine.HireConsultancy(siteId));

    /// <summary>Hire a land grow-out site's operating staff before its first fish.</summary>
    public Godot.Collections.Dictionary HireLandStaffEarly(string siteId)
        => SimulationEngine?.State == null ? LandNotReady() : LandOutcome(SimulationEngine.HireLandStaffEarly(siteId));

    /// <summary>Set a land site's feeding plan: the share of the appetite fed (50-100 %).</summary>
    public Godot.Collections.Dictionary SetLandFeedPlan(string siteId, double rationPct)
        => SimulationEngine?.State == null ? LandNotReady() : LandOutcome(SimulationEngine.SetLandFeedPlan(siteId, rationPct));

    private static Godot.Collections.Dictionary LandHarvestPlanDictionary(Farm.Land.LandHarvestPlan p)
    {
        var tanks = new Godot.Collections.Array<string>();
        foreach (var id in p.TankIds) tanks.Add(id);
        return new Godot.Collections.Dictionary
        {
            ["id"] = p.Id,
            ["tank_ids"] = tanks,
            ["week_start_day"] = p.WeekStartDay,
            ["iso_week"] = p.IsoWeek,
            ["planned_on_day"] = p.PlannedOnDay,
            ["status"] = p.Status.ToString(),
            ["order_id"] = p.OrderId,
            ["ordered_day"] = p.OrderedDay,
            ["failure_key"] = p.FailureKey,
        };
    }

    /// <summary>
    /// A site's harvest planner (spec §6.5): "weeks" ("week_start_day",
    /// "iso_week", "expected_price_nok": the seasonal curve), "tanks"
    /// ("tank_id", "fish_count", "mean_weight_kg", "weight_by_week_kg",
    /// "size_factor_by_week", "harvest_plan_id"), "plans", "hall_tonnes_per_day".
    /// </summary>
    public Godot.Collections.Dictionary GetLandHarvestPlanner(string siteId)
    {
        var view = SimulationEngine?.LandHarvestPlannerOf(siteId);
        if (view == null) return LandResult(CommandResult.Fail(LandSiteReasons.NotFound, $"No land site with tanks '{siteId}'"));
        var weeks = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var w in view.Weeks)
            weeks.Add(new Godot.Collections.Dictionary
            {
                ["week_start_day"] = w.WeekStartDay,
                ["iso_week"] = w.IsoWeek,
                ["expected_price_nok"] = (double)w.ExpectedPriceNok,
            });
        var tanks = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var t in view.Tanks)
        {
            var byWeek = new Godot.Collections.Array<double>();
            foreach (var kg in t.WeightByWeekKg) byWeek.Add(kg);
            var factors = new Godot.Collections.Array<double>();
            foreach (var f in t.SizeFactorByWeek) factors.Add(f);
            tanks.Add(new Godot.Collections.Dictionary
            {
                ["tank_id"] = t.TankId,
                ["fish_count"] = t.Fish,
                ["mean_weight_kg"] = t.MeanWeightKg,
                ["weight_by_week_kg"] = byWeek,
                ["size_factor_by_week"] = factors,
                ["harvest_plan_id"] = t.HarvestPlanId,
            });
        }
        var plans = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var p in view.Plans) plans.Add(LandHarvestPlanDictionary(p));
        return new Godot.Collections.Dictionary
        {
            ["success"] = true,
            ["id"] = view.SiteId,
            ["weeks"] = weeks,
            ["tanks"] = tanks,
            ["plans"] = plans,
            ["hall_tonnes_per_day"] = view.HallTonnesPerDay,
        };
    }

    /// <summary>
    /// What harvesting tanks in the ISO week of a day would bring: "allowed",
    /// "reason_key", "label_key", "week_start_day", "iso_week", "slot_day",
    /// "first_slaughter_day", "last_slaughter_day", "fish", "hog_kg",
    /// "mean_live_weight_kg", "size_factor", "grade_factor",
    /// "expected_reference_price_nok", "gross_revenue_nok", "net_revenue_nok",
    /// "hall_tonnes_per_day", "other_planned_hog_kg". A read.
    /// </summary>
    public Godot.Collections.Dictionary QuoteLandHarvestPlan(string siteId, Godot.Collections.Array tankIds, int weekStartDay)
    {
        if (SimulationEngine?.State == null) return LandNotReady();
        var q = SimulationEngine.QuoteLandHarvestPlan(siteId, LandIds(tankIds), weekStartDay);
        return new Godot.Collections.Dictionary
        {
            ["success"] = q.Allowed,
            ["allowed"] = q.Allowed,
            ["reason_key"] = q.Reason,
            ["label_key"] = LandLabelKey(q.Reason),
            ["message"] = q.Message,
            ["week_start_day"] = q.WeekStartDay,
            ["iso_week"] = q.IsoWeek,
            ["slot_day"] = q.SlotDay,
            ["first_slaughter_day"] = q.FirstSlaughterDay,
            ["last_slaughter_day"] = q.LastSlaughterDay,
            ["fish"] = q.Fish,
            ["hog_kg"] = q.HogKg,
            ["mean_live_weight_kg"] = q.MeanLiveWeightKg,
            ["size_factor"] = q.SizeFactor,
            ["grade_factor"] = q.GradeFactor,
            ["expected_reference_price_nok"] = (double)q.ExpectedReferencePriceNok,
            ["gross_revenue_nok"] = (double)q.GrossRevenueNok,
            ["net_revenue_nok"] = (double)q.NetRevenueNok,
            ["hall_tonnes_per_day"] = q.HallTonnesPerDay,
            ["other_planned_hog_kg"] = q.OtherPlannedHogKg,
        };
    }

    /// <summary>Plan tanks' harvest into the ISO week of a day (the order goes out so the slot falls on the Monday).</summary>
    public Godot.Collections.Dictionary PlanLandHarvest(string siteId, Godot.Collections.Array tankIds, int weekStartDay)
        => SimulationEngine?.State == null ? LandNotReady() : LandOutcome(SimulationEngine.PlanLandHarvest(siteId, LandIds(tankIds), weekStartDay));

    /// <summary>Cancel a harvest plan whose order is not placed yet.</summary>
    public Godot.Collections.Dictionary CancelLandHarvestPlan(string siteId, string planId)
        => SimulationEngine?.State == null ? LandNotReady() : LandOutcome(SimulationEngine.CancelLandHarvestPlan(siteId, planId));

    /// <summary>
    /// A site's post-smolt planner (spec §6.5): "salinity_ppt",
    /// "has_river_intake", "sulphate_factor", the tier rules
    /// ("super_smolt_min_weight_kg", "large_min_weight_kg",
    /// "super_smolt_min_salinity_ppt", "seawater_record_days",
    /// "desmolt_salinity_ppt", "desmolt_days"), "tanks" ("tank_id",
    /// "fish_count", "mean_weight_kg", "target_weight_kg", "tier",
    /// "tier_label_key", "seawater_days", "low_salinity_days", "desmoltified",
    /// "vaccinated", "sale_value_nok", "bought_value_nok", "transfer_job_id",
    /// "transfer_due_day", "transfer_destination_id"), "destinations"
    /// ("sea_site_id", "label", "mtb_tonnes", "biomass_tonnes",
    /// "pending_tonnes", "headroom_tonnes").
    /// </summary>
    public Godot.Collections.Dictionary GetPostSmoltPlanner(string siteId)
    {
        var view = SimulationEngine?.PostSmoltPlannerOf(siteId);
        if (view == null) return LandResult(CommandResult.Fail(LandSiteReasons.NotFound, $"No land site with tanks '{siteId}'"));
        var tanks = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var t in view.Tanks)
            tanks.Add(new Godot.Collections.Dictionary
            {
                ["tank_id"] = t.TankId,
                ["fish_count"] = t.Fish,
                ["mean_weight_kg"] = t.MeanWeightKg,
                ["target_weight_kg"] = t.TargetWeightKg,
                ["tier"] = LandTierId(t.Tier),
                ["tier_label_key"] = "LAND_TIER_" + LandTierId(t.Tier).ToUpperInvariant(),
                ["seawater_days"] = t.SeawaterDays,
                ["low_salinity_days"] = t.LowSalinityDays,
                ["desmoltified"] = t.Desmoltified,
                ["vaccinated"] = t.Vaccinated,
                ["sale_value_nok"] = (double)t.SaleValueNok,
                ["bought_value_nok"] = (double)t.BoughtValueNok,
                ["transfer_job_id"] = t.TransferJobId,
                ["transfer_due_day"] = t.TransferDueDay,
                ["transfer_destination_id"] = t.TransferDestinationId,
            });
        var destinations = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var d in view.Destinations)
            destinations.Add(new Godot.Collections.Dictionary
            {
                ["sea_site_id"] = d.SeaSiteId,
                ["label"] = d.Label,
                ["mtb_tonnes"] = d.MtbTonnes,
                ["biomass_tonnes"] = d.BiomassTonnes,
                ["pending_tonnes"] = d.PendingTonnes,
                ["headroom_tonnes"] = d.HeadroomTonnes,
            });
        return new Godot.Collections.Dictionary
        {
            ["success"] = true,
            ["id"] = view.SiteId,
            ["salinity_ppt"] = view.SalinityPpt,
            ["has_river_intake"] = view.HasRiverIntake,
            ["sulphate_factor"] = view.SulphateFactor,
            ["super_smolt_min_weight_kg"] = view.SuperSmoltMinWeightKg,
            ["large_min_weight_kg"] = view.LargeMinWeightKg,
            ["super_smolt_min_salinity_ppt"] = view.SuperSmoltMinSalinityPpt,
            ["seawater_record_days"] = view.SeawaterRecordDays,
            ["desmolt_salinity_ppt"] = view.DesmoltSalinityPpt,
            ["desmolt_days"] = view.DesmoltDays,
            ["tanks"] = tanks,
            ["destinations"] = destinations,
        };
    }

    /// <summary>
    /// What moving a tank's fish to a fjord site would do (count 0 = the
    /// whole tank): "allowed", "reason_key", "label_key", "fish",
    /// "mean_weight_kg", "tier", "tier_label_key", "seawater_days",
    /// "low_salinity_days", "desmoltified", "days", "hire_nok", "due_day",
    /// "mortality_share", "expected_deaths", "bought_value_nok",
    /// "sale_value_nok", "sea_days_saved". A read.
    /// </summary>
    public Godot.Collections.Dictionary PreviewTransferTankToSea(string siteId, string tankId, string seaSiteId, int count)
    {
        if (SimulationEngine?.State == null) return LandNotReady();
        var p = SimulationEngine.PreviewTransferTankToSea(siteId, tankId, seaSiteId, count);
        return new Godot.Collections.Dictionary
        {
            ["success"] = p.Allowed,
            ["allowed"] = p.Allowed,
            ["reason_key"] = p.Reason,
            ["label_key"] = LandLabelKey(p.Reason),
            ["message"] = p.Message,
            ["tank_id"] = p.TankId,
            ["sea_site_id"] = p.SeaSiteId,
            ["fish"] = p.Fish,
            ["mean_weight_kg"] = p.MeanWeightKg,
            ["tier"] = LandTierId(p.Tier),
            ["tier_label_key"] = "LAND_TIER_" + LandTierId(p.Tier).ToUpperInvariant(),
            ["seawater_days"] = p.SeawaterDays,
            ["low_salinity_days"] = p.LowSalinityDays,
            ["desmoltified"] = p.Desmoltified,
            ["days"] = p.Days,
            ["hire_nok"] = (double)p.HireNok,
            ["due_day"] = p.DueDay,
            ["mortality_share"] = p.MortalityShare,
            ["expected_deaths"] = p.ExpectedDeaths,
            ["bought_value_nok"] = (double)p.BoughtValueNok,
            ["sale_value_nok"] = (double)p.SaleValueNok,
            ["sea_days_saved"] = p.SeaDaysSaved,
        };
    }

    /// <summary>Book a wellboat to move a tank's fish to a fjord site (count 0 = the whole tank). Id = the job.</summary>
    public Godot.Collections.Dictionary TransferTankToSea(string siteId, string tankId, string seaSiteId, int count)
        => SimulationEngine?.State == null ? LandNotReady() : LandOutcome(SimulationEngine.TransferTankToSea(siteId, tankId, seaSiteId, count));

    /// <summary>Sell a post-smolt tank's fish alive (count 0 = the whole tank).</summary>
    public Godot.Collections.Dictionary SellPostSmolt(string siteId, string tankId, int count)
        => SimulationEngine?.State == null ? LandNotReady() : LandOutcome(SimulationEngine.SellPostSmolt(siteId, tankId, count));

    /// <summary>Set a post-smolt site's salinity (a pad with a river intake).</summary>
    public Godot.Collections.Dictionary SetSalinity(string siteId, double salinityPpt)
        => SimulationEngine?.State == null ? LandNotReady() : LandOutcome(SimulationEngine.SetSalinity(siteId, salinityPpt));

    private static Godot.Collections.Dictionary LandAccountDictionary(Farm.Land.LandSiteAccount a) => new()
    {
        ["day"] = a.Day,
        ["farming_cost_nok"] = (double)a.FarmingCostNok,
        ["farming_cost_per_kg_hog"] = a.FarmingCostPerKgHog,
        ["energy_share"] = a.EnergyShare,
        ["feed_nok"] = (double)a.FeedNok,
        ["smolt_nok"] = (double)a.SmoltNok,
        ["energy_nok"] = (double)a.EnergyNok,
        ["oxygen_nok"] = (double)a.OxygenNok,
        ["staff_nok"] = (double)a.StaffNok,
        ["maintenance_nok"] = (double)a.MaintenanceNok,
        ["insurance_nok"] = (double)a.InsuranceNok,
        ["other_nok"] = (double)a.OtherNok,
        ["competence_nok"] = (double)a.CompetenceNok,
        ["harvest_nok"] = (double)a.HarvestNok,
        ["transfer_nok"] = (double)a.TransferNok,
        ["harvest_revenue_nok"] = (double)a.HarvestRevenueNok,
        ["post_smolt_revenue_nok"] = (double)a.PostSmoltRevenueNok,
        ["harvested_hog_kg"] = a.HarvestedHogKg,
        ["harvested_live_kg"] = a.HarvestedLiveKg,
        ["harvested_fish"] = a.HarvestedFish,
        ["sold_live_kg"] = a.SoldLiveKg,
        ["transferred_fish"] = a.TransferredFish,
        ["energy_kwh"] = a.EnergyKWh,
        ["feed_kg"] = a.FeedKg,
    };

    /// <summary>
    /// A land site's own books (F2-15): "since_built" and "last_year" (the last
    /// complete sim year), each with "farming_cost_per_kg_hog" (ex depreciation),
    /// "energy_share" and every line.
    /// </summary>
    public Godot.Collections.Dictionary GetLandAccount(string siteId)
    {
        var site = SimulationEngine?.FindLandSite(siteId);
        if (SimulationEngine?.State == null || site == null)
            return LandResult(CommandResult.Fail(LandSiteReasons.NotFound, $"No land site '{siteId}'"));
        return new Godot.Collections.Dictionary
        {
            ["success"] = true,
            ["id"] = site.Id,
            ["since_built"] = LandAccountDictionary(site.Account),
            ["last_year"] = LandAccountDictionary(site.LastYear),
        };
    }

    /// <summary>
    /// A land site built at once on a pad with no permits and no payment: the
    /// scenario-start and QA path (spec §3.1 decision 5). Only QA and tests
    /// call it (tests/test_land_bridge.py). Id = the site.
    /// </summary>
    public Godot.Collections.Dictionary CommissionLandSite(string plotId, string template, int lots, string name)
    {
        if (SimulationEngine?.State == null) return LandNotReady();
        if (!TryLandTemplate(template, out var t))
            return LandResult(CommandResult.Fail(LandSiteReasons.OutOfRange, $"Unknown template '{template}'"));
        return LandOutcome(SimulationEngine.CommissionLandSite(plotId, t, lots, name));
    }

    // ═══ END sim·land lane block ═══
}
