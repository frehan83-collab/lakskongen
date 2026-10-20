using System.Linq;
using Godot;
using LakskongenSim.Core;
using LakskongenSim.Farm.Land;
using Farm = LakskongenSim.Farm;
using Finance = LakskongenSim.Finance;

// GameBridge.FacilityVisuals.cs — partial class slice (roadmap F2-20; gfx's).
// <summary>
// The facility presenter's read: every land site and offshore unit as the 3D
// scene draws it (plot, lots and tanks, zones, the construction phase, fish),
// a thin projection of SimulationEngine.FacilityVisualViews (a pure read).
// Plus the QA-only verbs UiFlowTests, the capture scenes and the perf harness
// call on the autoload (never the player's UI; tests/test_facility_presenter.py
// fails on any other caller): QaPlaceFarmTypeSet builds the farm-type QA set
// through the engine's own verbs, QaStartLandBuild starts a land build with
// its permits approved at once, QaStageConstruction stands a unit's
// construction project in a chosen phase.
// </summary>
/// <summary>
/// GameBridge partial — F2-20 facility presenter (gfx's slice).
/// </summary>
public partial class GameBridge : Node
{
    /// <summary>
    /// The farm-type units for the 3D presenter, land sites first. Keys:
    /// "unit_id", "name", "family" ("land", "hav"), "variant" ("hfs", "ras",
    /// "post_smolt", "of1", "havfarm"), "plot_id", "plot_x", "plot_z",
    /// "lots" (Array: "lot_id", "template" ("hfs_lot", "hfs_pilot_lot",
    /// "ras_hall", "post_smolt_module"), "tanks" (Array: "id", "number",
    /// "role" ("grow_out", "pre_grow_out", "post_smolt"), "diameter_m",
    /// "ready", "stocked", "fish_count"), "build", "expansion" ({} for none)),
    /// "tanks" (count), "zones", "stocked_zones", "build" ("project_id",
    /// "status" ("none", "waiting", "building", "complete"), "phase_key" (the
    /// project's phase key, "operational" once built), "phase_index",
    /// "phase_count", "phase_progress", "progress", "suspended",
    /// "waiting_reason"), the unit build's "phase_key", "phase_index",
    /// "phase_count", "phase_progress" again at the top, "operational",
    /// "feeding", "fish_count", "biomass_tonnes", "has_river_intake", "legacy".
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetFacilityVisuals()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        if (SimulationEngine?.State == null) return result;
        foreach (var v in SimulationEngine.FacilityVisualViews())
        {
            var lots = new Godot.Collections.Array<Godot.Collections.Dictionary>();
            foreach (var lot in v.Lots)
            {
                var tanks = new Godot.Collections.Array<Godot.Collections.Dictionary>();
                foreach (var t in lot.Tanks)
                {
                    tanks.Add(new Godot.Collections.Dictionary
                    {
                        ["id"] = t.Id,
                        ["number"] = t.Number,
                        ["role"] = t.Role,
                        ["diameter_m"] = t.DiameterM,
                        ["ready"] = t.Ready,
                        ["stocked"] = t.Stocked,
                        ["fish_count"] = t.FishCount,
                    });
                }
                lots.Add(new Godot.Collections.Dictionary
                {
                    ["lot_id"] = lot.LotId,
                    ["template"] = lot.Template,
                    ["tanks"] = tanks,
                    ["build"] = BuildDict(lot.Build),
                    ["expansion"] = lot.Expansion == null ? new Godot.Collections.Dictionary() : BuildDict(lot.Expansion),
                });
            }
            var build = BuildDict(v.Build);
            result.Add(new Godot.Collections.Dictionary
            {
                ["unit_id"] = v.UnitId,
                ["name"] = v.Name,
                ["family"] = v.Family,
                ["variant"] = v.Variant,
                ["plot_id"] = v.PlotId,
                ["plot_x"] = v.PlotX,
                ["plot_z"] = v.PlotZ,
                ["lots"] = lots,
                ["tanks"] = v.TankCount,
                ["zones"] = v.Zones,
                ["stocked_zones"] = v.StockedZones,
                ["build"] = build,
                ["phase_key"] = v.Build.PhaseKey,
                ["phase_index"] = v.Build.PhaseIndex,
                ["phase_count"] = v.Build.PhaseCount,
                ["phase_progress"] = v.Build.PhaseProgress,
                ["operational"] = v.Operational,
                ["feeding"] = v.Feeding,
                ["fish_count"] = v.FishCount,
                ["biomass_tonnes"] = v.BiomassTonnes,
                ["has_river_intake"] = v.HasRiverIntake,
                ["legacy"] = v.Legacy,
            });
        }
        return result;
    }

    private static Godot.Collections.Dictionary BuildDict(FacilityBuildView b) => new()
    {
        ["project_id"] = b.ProjectId,
        ["status"] = b.Status,
        ["phase_key"] = b.PhaseKey,
        ["phase_index"] = b.PhaseIndex,
        ["phase_count"] = b.PhaseCount,
        ["phase_progress"] = b.PhaseProgress,
        ["progress"] = b.Progress,
        ["suspended"] = b.Suspended,
        ["waiting_reason"] = b.WaitingReason,
    };

    // ── QA only ──────────────────────────────────────────────────────────────

    /// <summary>The QA set's plots (spec §4): the hero pad, the delta, the outer-coast slot.</summary>
    private const string QaLandPlot = "pad_a", QaPostSmoltPlot = "pad_delta", QaOffshorePlot = "off_1", QaSecondOffshorePlot = "off_2";

    /// <summary>Grid projects a QA expansion may start before it gives up (each adds the calibration's step).</summary>
    private const int QaGridSteps = 3;

    /// <summary>
    /// QA-only (F2-20): the farm-type QA set through the engine's own verbs,
    /// paid by a QA grant, every unit built: an HFS site of
    /// <paramref name="growOutLots"/> full lots on pad_a (0-4; 0 = none,
    /// CommissionLandSite), a post-smolt site of <paramref name="postSmoltModules"/>
    /// modules on pad_delta (0-4; 0 = none) and <paramref name="offshoreUnits"/>
    /// offshore units (0-2): an OF1-class unit on off_1, then a Havfarm-class
    /// unit on off_2, each contracted through the engine's QA seam
    /// (SimulationEngine.QaContractOffshoreUnit: BuildOffshoreUnit's contract
    /// with the unlock gates waived and a QA award) and its project stood
    /// complete (the engine commissions it on its next day). The research and
    /// the first harvest the land gates ask for are given.
    /// Keys: "success", "message", "land_id", "post_smolt_id", "offshore_id",
    /// "offshore_ids". Not reachable from player UI.
    /// </summary>
    public Godot.Collections.Dictionary QaPlaceFarmTypeSet(int growOutLots, int postSmoltModules, int offshoreUnits)
    {
        var result = new Godot.Collections.Dictionary { ["success"] = false, ["land_id"] = "", ["post_smolt_id"] = "", ["offshore_id"] = "" };
        var engine = SimulationEngine;
        if (engine?.State == null || engine.FinanceManager == null)
        {
            result["message"] = "engine_not_ready";
            return result;
        }
        if (growOutLots < 0 || growOutLots > 4 || postSmoltModules < 0 || postSmoltModules > 4 || offshoreUnits < 0 || offshoreUnits > 2)
        {
            result["message"] = "invalid_set";
            return result;
        }
        if (!QaOpenFarmTypeGates())
        {
            result["message"] = "gates_refused";
            return result;
        }
        var messages = new System.Collections.Generic.List<string>();
        bool ok = true;
        if (growOutLots > 0)
        {
            var land = engine.CommissionLandSite(QaLandPlot, LandTemplate.HfsLot, growOutLots);
            ok &= land.Ok;
            messages.Add(land.Message);
            if (land.Ok) result["land_id"] = land.Id ?? "";
        }
        if (postSmoltModules > 0)
        {
            var smolt = engine.CommissionLandSite(QaPostSmoltPlot, LandTemplate.PostSmoltModule, postSmoltModules);
            ok &= smolt.Ok;
            messages.Add(smolt.Message);
            if (smolt.Ok) result["post_smolt_id"] = smolt.Id ?? "";
        }
        var offshoreIds = new Godot.Collections.Array<string>();
        for (int i = 0; i < offshoreUnits; i++)
        {
            var contracted = QaContractOffshoreUnit(i == 0 ? QaOffshorePlot : QaSecondOffshorePlot,
                i == 0 ? Farm.OffshoreType.SemiSubmersible : Farm.OffshoreType.ShipShaped);
            ok &= contracted.Ok;
            messages.Add(contracted.Message);
            if (!contracted.Ok) continue;
            offshoreIds.Add(contracted.Id ?? "");
            ok &= QaStageConstruction(contracted.Id ?? "", FacilityBuildStatus.Operational, 1.0);
        }
        result["success"] = ok;
        result["message"] = string.Join(" | ", messages);
        result["offshore_id"] = offshoreIds.Count > 0 ? offshoreIds[0] : "";
        result["offshore_ids"] = offshoreIds;
        EmitSignal(SignalName.GameStateUpdated);
        return result;
    }

    /// <summary>
    /// QA-only (F2-20): contract an offshore unit of <paramref name="type"/>
    /// on <paramref name="plotId"/> through BuildOffshoreUnit (managed, full
    /// equity) on a QA development-licence award, its project at the yard
    /// contract. Keys: "success", "message", "unit_id". Not reachable from
    /// player UI.
    /// </summary>
    public Godot.Collections.Dictionary QaContractOffshore(string plotId, string type)
    {
        var result = new Godot.Collections.Dictionary { ["success"] = false, ["unit_id"] = "" };
        if (SimulationEngine?.State == null || !QaOpenFarmTypeGates())
        {
            result["message"] = "engine_not_ready";
            return result;
        }
        var kind = type == "havfarm" ? Farm.OffshoreType.ShipShaped : Farm.OffshoreType.SemiSubmersible;
        var contracted = QaContractOffshoreUnit(plotId, kind);
        result["success"] = contracted.Ok;
        result["message"] = contracted.Message;
        result["unit_id"] = contracted.Id ?? "";
        EmitSignal(SignalName.GameStateUpdated);
        return result;
    }

    private CommandResult QaContractOffshoreUnit(string plotId, Farm.OffshoreType type)
    {
        QaGrantCash(QaGrantCashMaxNok);
        return SimulationEngine!.QaContractOffshoreUnit(plotId, type);
    }

    /// <summary>
    /// QA-only (F2-20): start building <paramref name="template"/> ("hfs_lot",
    /// "hfs_pilot_lot", "post_smolt_module") as a new land site on
    /// <paramref name="plotId"/>: ApplyLandPermits files the package, the QA
    /// approves its cases today, BuildLandSite starts the construction project
    /// (paid by a QA grant). Keys: "success", "message", "unit_id",
    /// "project_id". Not reachable from player UI.
    /// </summary>
    public Godot.Collections.Dictionary QaStartLandBuild(string plotId, string template)
    {
        var result = new Godot.Collections.Dictionary { ["success"] = false, ["unit_id"] = "", ["project_id"] = "" };
        var engine = SimulationEngine;
        if (engine?.State == null || !QaOpenFarmTypeGates())
        {
            result["message"] = "engine_not_ready";
            return result;
        }
        LandTemplate t = template switch
        {
            "hfs_lot" => LandTemplate.HfsLot,
            "hfs_pilot_lot" => LandTemplate.HfsPilotLot,
            "post_smolt_module" => LandTemplate.PostSmoltModule,
            _ => (LandTemplate)(-1),
        };
        if (!System.Enum.IsDefined(t))
        {
            result["message"] = "invalid_template";
            return result;
        }
        QaGrantCash(QaGrantCashMaxNok);
        var applied = engine.ApplyLandPermits(plotId, t);
        if (!applied.Ok && applied.ErrorKey != LandSiteReasons.NothingToApply)
        {
            result["message"] = applied.Message;
            return result;
        }
        QaApprovePending(plotId);
        var built = engine.BuildLandSite(plotId, t);
        result["success"] = built.Ok;
        result["message"] = built.Message;
        result["unit_id"] = built.Ok ? built.Id ?? "" : "";
        if (built.Ok)
        {
            var site = engine.FindLandSite(built.Id ?? "");
            result["project_id"] = site?.Lots.LastOrDefault()?.ConstructionProjectId ?? "";
        }
        EmitSignal(SignalName.GameStateUpdated);
        return result;
    }

    /// <summary>
    /// QA-only (F2-20): add the next lot of land site <paramref name="unitId"/>
    /// (a full campus grows lot by lot, spec §5.1): its lots' projects stood
    /// complete first (the engine expands no site while a lot is being built),
    /// the permits for the production after it filed and approved today,
    /// ExpandLandSite starts the lot's project (paid by a QA grant). Keys:
    /// "success", "message", "project_id". Not reachable from player UI.
    /// </summary>
    public Godot.Collections.Dictionary QaExpandLandSite(string unitId)
    {
        var result = new Godot.Collections.Dictionary { ["success"] = false, ["project_id"] = "" };
        var engine = SimulationEngine;
        var site = engine?.FindLandSite(unitId);
        if (engine?.State == null || site == null || !site.HasLots || !QaOpenFarmTypeGates())
        {
            result["message"] = "not_found";
            return result;
        }
        QaStageConstruction(unitId, FacilityBuildStatus.Operational, 1.0);
        var template = site.Lots[^1].Template == LandTemplate.HfsPilotLot ? LandTemplate.HfsLot : site.Lots[^1].Template;
        QaGrantCash(QaGrantCashMaxNok);
        var applied = engine.ApplyLandPermits(site.PlotId, template, site.Lots.Count + 1);
        if (!applied.Ok && applied.ErrorKey != LandSiteReasons.NothingToApply)
        {
            result["message"] = applied.Message;
            return result;
        }
        QaApprovePending(site.PlotId);
        // A full campus outgrows the pad's grid (pad_a: 40 MW for four lots'
        // 43 MW): the engine's grid project raises it, its case approved today.
        for (int step = 0; step < QaGridSteps && engine.PreviewLandExpansion(unitId).Verdict == LandSiteReasons.GridCapacity; step++)
        {
            if (!engine.StartGridProject(site.PlotId, Finance.LandPermitSystem.GridCapacity).Ok) break;
            QaApprovePending(site.PlotId);
        }
        var expanded = engine.ExpandLandSite(unitId);
        result["success"] = expanded.Ok;
        result["message"] = expanded.Message;
        result["project_id"] = expanded.Ok ? expanded.Id ?? "" : "";
        EmitSignal(SignalName.GameStateUpdated);
        return result;
    }

    /// <summary>QA: every pending permit case on <paramref name="plotId"/> decided approved today.</summary>
    private void QaApprovePending(string plotId)
    {
        var engine = SimulationEngine!;
        foreach (var permit in engine.PermitCases.Where(c => c.PlotId == plotId && c.IsPending))
        {
            permit.Status = Finance.PermitStatus.Approved;
            permit.DecidedDay = engine.State.Day;
        }
    }

    /// <summary>
    /// QA-only (F2-20): stand unit <paramref name="unitId"/>'s construction
    /// project in phase <paramref name="phaseKey"/> (the project's own key:
    /// "site_works" … on land, "yard_contract" … offshore; "operational" =
    /// complete) with <paramref name="phaseProgress"/> (0-1) of it worked:
    /// earlier phases finished on the days before, the phase started as many
    /// of its days ago. A land site's project-built lots all stand there
    /// (their first-tank and completion days follow the project again; a lot
    /// whose project has no such phase is left as it is); an offshore unit is
    /// operational once complete (the engine commissions it on its next day).
    /// Moves no money and no fish. False for a unit with no project in that
    /// phase or a progress outside 0-1. Not reachable from player UI.
    /// </summary>
    public bool QaStageConstruction(string unitId, string phaseKey, double phaseProgress)
    {
        var engine = SimulationEngine;
        if (engine?.State == null || !double.IsFinite(phaseProgress) || phaseProgress < 0.0 || phaseProgress > 1.0)
            return false;
        int day = engine.State.Day;
        var staged = false;
        var land = engine.FindLandSite(unitId);
        if (land != null)
        {
            foreach (var lot in land.Lots)
            {
                var project = engine.State.ConstructionProjects.FirstOrDefault(p => p.Id == lot.ConstructionProjectId);
                if (project == null || !QaStandProject(project, phaseKey, phaseProgress, day)) continue;
                lot.FirstTanksDay = -1;
                lot.CompleteDay = -1;
                staged = true;
            }
        }
        var offshore = engine.FindOffshoreUnit(unitId);
        if (offshore?.Offshore != null)
        {
            var project = engine.OffshoreProjectOf(offshore);
            if (project != null && QaStandProject(project, phaseKey, phaseProgress, day))
            {
                var unit = offshore.Offshore;
                unit.ConstructionProgress = project.Status == ConstructionStatus.Complete
                    ? 1.0
                    : System.Math.Min(0.999, engine.GetOffshoreConstruction(unitId)?.OverallProgress ?? 0.0);
                staged = true;
            }
        }
        if (staged) EmitSignal(SignalName.GameStateUpdated);
        return staged;
    }

    private static bool QaStandProject(ConstructionProject project, string phaseKey, double phaseProgress, int day)
    {
        int count = project.Phases.Count;
        int index = phaseKey == FacilityBuildStatus.Operational ? count : project.Phases.FindIndex(p => p.Key == phaseKey);
        if (index < 0) return false;
        project.SuspendedDays = 0;
        project.PhaseCompletedDays.Clear();
        // Finished phases ended on the days before (never before day 0: the
        // engine reads a day below 0 as "not yet"); the phase under way may
        // have started before the game did.
        if (index >= count)
        {
            for (int k = 0; k < count; k++) project.PhaseCompletedDays.Add(System.Math.Max(0, day - (count - 1 - k)));
            project.CurrentPhase = count;
            project.Status = ConstructionStatus.Complete;
            project.PhaseStartDay = day - 1;
            project.PhaseDays = 1;
            return true;
        }
        int phaseDays = System.Math.Max(1, project.Phases[index].PlannedDays);
        int start = day - (int)System.Math.Round(phaseProgress * phaseDays);
        for (int k = 0; k < index; k++) project.PhaseCompletedDays.Add(System.Math.Max(0, System.Math.Min(day, start - (index - k))));
        project.CurrentPhase = index;
        project.Status = ConstructionStatus.Building;
        project.PhaseStartDay = start;
        project.PhaseDays = phaseDays;
        return true;
    }

    /// <summary>The research and the first harvest the farm-type gates ask for (QA: given at once).</summary>
    private bool QaOpenFarmTypeGates()
    {
        var engine = SimulationEngine;
        if (engine?.State == null || engine.Research == null) return false;
        foreach (var node in new[] { "land-based-ras", "deep-sea-farming" })
        {
            if (!engine.Research.Nodes.ContainsKey(node)) return false;
            engine.Research.ForceUnlock(node);
        }
        if (engine.State.HarvestBatchesSold < 1) engine.State.HarvestBatchesSold = 1;
        return true;
    }
}
