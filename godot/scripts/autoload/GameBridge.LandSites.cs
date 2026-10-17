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
        => string.IsNullOrEmpty(reasonKey) ? "" : "LAND_REASON_" + reasonKey.ToUpperInvariant();

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

    /// <summary>Harvest a land tank now.</summary>
    public Godot.Collections.Dictionary HarvestLandTank(string siteId, string tankId)
        => SimulationEngine?.State == null ? LandNotReady() : LandOutcome(SimulationEngine.HarvestLandTank(siteId, tankId));

    /// <summary>Set a land site's Vannstrategi (reuse, deep-intake share, setpoint, blend toward the setpoint).</summary>
    public Godot.Collections.Dictionary SetVannstrategi(string siteId, double reuse, double deepIntakeShare, double setpointC, bool blend)
        => SimulationEngine?.State == null ? LandNotReady() : LandOutcome(SimulationEngine.SetVannstrategi(siteId, reuse, deepIntakeShare, setpointC, blend));

    /// <summary>Fit heat recovery to a lot.</summary>
    public Godot.Collections.Dictionary InstallHeatRecovery(string lotId)
        => SimulationEngine?.State == null ? LandNotReady() : LandOutcome(SimulationEngine.InstallHeatRecovery(lotId));

    // ═══ END sim·land lane block ═══
}
