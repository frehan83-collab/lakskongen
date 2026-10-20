using Godot;
using LakskongenSim.Core;
using Farm = LakskongenSim.Farm;

// GameBridge.Offshore.cs — partial class slice (roadmap F2-03; the sea lane's).
// <summary>
// Offshore slice: the offshore units (offshore sea sites) and the offshore
// slots they stand on, as lists. Thin projection of the engine's views
// (SimulationEngine.OffshoreUnitViews, SitePlotViews; Core/FarmUnitViews.cs).
// F2-03 owns the list verbs above the lane block; the sea lane (F2-06 metocean
// and forecast, F2-07 operations, F2-08 incidents, F2-09 licences, F2-11 the
// area round) appends its verbs only inside the marked block, and the land
// lane never edits this file (FARM_TYPES_SPEC §7.6).
// </summary>
/// <summary>
/// GameBridge partial — F2-03 offshore units and slots (the sea lane's slice).
/// </summary>
public partial class GameBridge : Node
{
    /// <summary>
    /// The company's offshore units, in the site list's order. Keys: "id",
    /// "name", "plot_id", "zone_id", "farm_type", "zones", "occupied_zones",
    /// "mtb_tonnes", "fish_count", "biomass_tonnes", "mean_weight_kg",
    /// "design_hs_m", "hs100_m" (its slot's), "external_lice_multiplier",
    /// "lice_per_fish", "operational", "construction_progress",
    /// "storm_lockdown", "wave_height_m", "crew_days_since_rotation",
    /// "crew_rotation_days", "structural_integrity", "employee_count", and
    /// "pens" (its zones, keys as GetSitePens).
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetOffshoreUnits()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        if (SimulationEngine?.State == null) return result;
        foreach (var unit in SimulationEngine.OffshoreUnitViews())
        {
            result.Add(new Godot.Collections.Dictionary
            {
                ["id"] = unit.Id,
                ["name"] = unit.Name,
                ["plot_id"] = unit.PlotId,
                ["zone_id"] = unit.ZoneId,
                ["farm_type"] = unit.FarmType,
                ["zones"] = unit.Zones,
                ["occupied_zones"] = unit.OccupiedZones,
                ["mtb_tonnes"] = unit.MtbTonnes,
                ["fish_count"] = unit.FishCount,
                ["biomass_tonnes"] = unit.BiomassTonnes,
                ["mean_weight_kg"] = unit.MeanWeightKg,
                ["design_hs_m"] = unit.DesignHsM,
                ["hs100_m"] = unit.Hs100M,
                ["external_lice_multiplier"] = unit.ExternalLiceMultiplier,
                ["lice_per_fish"] = unit.LicePerFish,
                ["operational"] = unit.Operational,
                ["construction_progress"] = unit.ConstructionProgress,
                ["storm_lockdown"] = unit.StormLockdown,
                ["wave_height_m"] = unit.WaveHeightM,
                ["crew_days_since_rotation"] = unit.CrewDaysSinceRotation,
                ["crew_rotation_days"] = unit.CrewRotationDays,
                ["structural_integrity"] = unit.StructuralIntegrity,
                ["employee_count"] = unit.EmployeeCount,
                ["pens"] = PenArrayOf(unit.Id),
            });
        }
        return result;
    }

    /// <summary>
    /// The offshore slots (spec §4: off_1 "Ytre kyst", off_2 "Havkanten",
    /// hav_1 "Havområde", then any overflow slot a migrated save uses), free
    /// or held. Keys as <see cref="PlotDictionary"/>.
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetOffshorePlots()
    {
        var result = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        if (SimulationEngine?.State == null) return result;
        foreach (var plot in SimulationEngine.SitePlotViews(Farm.PlotKind.OffshoreSlot))
            result.Add(PlotDictionary(plot));
        return result;
    }

    /// <summary>
    /// One plot as a dictionary (GetOffshorePlots, GetLandPlots): "id", "name",
    /// "kind" ("LandPad", "OffshoreSlot"), "x", "z" (world metres),
    /// "real_distance_km", "owner_unit_id" ("" when free), "free", "overflow",
    /// "pre_zoned", "width_m", "depth_m", "grow_out_lots", "post_smolt_modules",
    /// "grid_capacity_mw", "exposure" ("" on a pad), "hs100_m", "water_depth_m",
    /// "external_lice_multiplier".
    /// </summary>
    private static Godot.Collections.Dictionary PlotDictionary(SitePlotView plot) => new()
    {
        ["id"] = plot.Id,
        ["name"] = plot.Name,
        ["kind"] = plot.Kind.ToString(),
        ["x"] = plot.WorldX,
        ["z"] = plot.WorldZ,
        ["real_distance_km"] = plot.RealDistanceKm,
        ["owner_unit_id"] = plot.OwnerUnitId,
        ["free"] = plot.IsFree,
        ["overflow"] = plot.IsOverflow,
        ["pre_zoned"] = plot.PreZoned,
        ["width_m"] = plot.PadWidthM,
        ["depth_m"] = plot.PadDepthM,
        ["grow_out_lots"] = plot.GrowOutLots,
        ["post_smolt_modules"] = plot.PostSmoltModules,
        ["grid_capacity_mw"] = plot.GridCapacityMW,
        ["exposure"] = plot.Exposure,
        ["hs100_m"] = plot.Hs100M,
        ["water_depth_m"] = plot.WaterDepthM,
        ["external_lice_multiplier"] = plot.LiceExternalMultiplier,
    };

    // ═══ BEGIN sim·sea lane block (F2-06 … F2-11): append offshore verbs below this line ═══

    /// <summary>
    /// F2-06: the sea at an offshore unit and its honest 10-day forecast (the
    /// window board, spec §6.5). Keys: "ok" (false for a site that is no
    /// offshore unit or before the game has an open sea), "site_id",
    /// "plot_id", "issued_day", "today" { "hs", "tp", "swell_hs",
    /// "visibility_km", "wind_ms" }, "design_hs", "limits" { row → Hs limit in m,
    /// -1 when no sea state stops it }, "rows" (the board rows in order),
    /// "days" [ { "day", "lead", "hs_p10", "hs_p50", "hs_p90",
    /// "p_ok" { row → probability }, "unresolved" } ], "known_swell" [ {
    /// "onset_day", "duration_days", "peak_p10", "peak_p50", "peak_p90", "tp",
    /// "peak_known" } ]. Issued once per plot and day by the engine (cached);
    /// never draws.
    /// </summary>
    public Godot.Collections.Dictionary GetSeaStateForecast(string siteId)
    {
        var result = new Godot.Collections.Dictionary { ["ok"] = false, ["site_id"] = siteId ?? "" };
        var view = SimulationEngine?.State == null ? null : SimulationEngine.GetSeaStateForecastView(siteId ?? "");
        if (view == null) return result;
        result["ok"] = true;
        result["plot_id"] = view.PlotId;
        result["issued_day"] = view.IssuedDay;
        result["today"] = new Godot.Collections.Dictionary
        {
            ["hs"] = view.HsTodayM,
            ["tp"] = view.TpTodayS,
            ["swell_hs"] = view.SwellHsTodayM,
            ["visibility_km"] = view.VisibilityKm,
            ["wind_ms"] = view.WindMs,
        };
        result["design_hs"] = view.DesignHsM;
        var limits = new Godot.Collections.Dictionary();
        var rows = new Godot.Collections.Array();
        foreach (var (row, limit) in view.RowLimitsM)
        {
            limits[row] = double.IsInfinity(limit) ? -1.0 : limit;
            rows.Add(row);
        }
        result["limits"] = limits;
        result["rows"] = rows;
        var days = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var day in view.Days)
        {
            var ok = new Godot.Collections.Dictionary();
            foreach (var (row, p) in day.RowProbabilities)
                ok[row] = p;
            days.Add(new Godot.Collections.Dictionary
            {
                ["day"] = day.Day,
                ["lead"] = day.LeadDays,
                ["hs_p10"] = day.HsP10M,
                ["hs_p50"] = day.HsP50M,
                ["hs_p90"] = day.HsP90M,
                ["p_ok"] = ok,
                ["unresolved"] = day.UnresolvedProbability,
            });
        }
        result["days"] = days;
        var swell = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var s in view.KnownSwell)
        {
            swell.Add(new Godot.Collections.Dictionary
            {
                ["onset_day"] = s.OnsetDay,
                ["duration_days"] = s.DurationDays,
                ["peak_p10"] = s.PeakP10M,
                ["peak_p50"] = s.PeakP50M,
                ["peak_p90"] = s.PeakP90M,
                ["tp"] = s.PeakTpS,
                ["peak_known"] = s.PeakKnown,
            });
        }
        result["known_swell"] = swell;
        return result;
    }

    // ── F2-07: build, construction, the unit's state and its operations ──

    /// <summary>
    /// F2-07: what building an offshore unit on <paramref name="plotId"/> costs
    /// before the player commits (spec §6.2 step 4). <paramref name="farmType"/>
    /// as <see cref="BuildOffshoreFarm"/> parses it, <paramref name="contract"/>
    /// "managed" or "turnkey", <paramref name="equityShare"/> 1.0 alone or the
    /// consortium's share. Keys: "ok", "plot_id", "farm_type", "contract",
    /// "equity_share", "contract_capex_nok", "company_capex_nok",
    /// "turnkey_premium", "overrun_median_nok", "overrun_p90_nok", "payments"
    /// [ { "phase", "planned_start_day", "planned_days", "payment_nok",
    /// "overrun_median_nok", "overrun_p90_nok", "player_started" } ],
    /// "consortium_equity_share", "consortium_company_capex_nok",
    /// "buyout_multiple", "licences", "licence_tonnes_mtb",
    /// "licence_market_value_nok", "licence_price_nok_per_tonne",
    /// "conversion_fee_nok", "resource_rent_before_conversion",
    /// "teething_defects_min", "teething_defects_max", "teething_budget_nok",
    /// "shakedown_options_days", "planned_build_days", "first_stocking_day",
    /// "gates" (keys as the farm-type catalogue's), "first_payment_nok",
    /// "affordable", "refusal_key" ("" when it may be built). Never draws.
    /// </summary>
    public Godot.Collections.Dictionary PreviewOffshoreBuild(string plotId, string farmType, string contract, double equityShare)
    {
        var result = new Godot.Collections.Dictionary { ["ok"] = false, ["plot_id"] = plotId ?? "" };
        if (SimulationEngine?.State == null) { result["refusal_key"] = "engine_not_ready"; return result; }
        if (!Farm.FacilityNames.TryParseOffshoreType(farmType, out var type) || !TryParseSeaEnum(contract, out ContractForm form))
        {
            result["refusal_key"] = "out_of_range";
            return result;
        }
        var p = SimulationEngine.PreviewOffshoreBuild(plotId ?? "", type, form, equityShare);
        var payments = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var pay in p.Payments)
        {
            payments.Add(new Godot.Collections.Dictionary
            {
                ["phase"] = pay.Phase,
                ["planned_start_day"] = pay.PlannedStartDay,
                ["planned_days"] = pay.PlannedDays,
                ["payment_nok"] = (double)pay.PaymentNok,
                ["overrun_median_nok"] = (double)pay.OverrunMedianNok,
                ["overrun_p90_nok"] = (double)pay.OverrunP90Nok,
                ["player_started"] = pay.PlayerStarted,
            });
        }
        var gates = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var gate in p.Gates)
        {
            gates.Add(new Godot.Collections.Dictionary
            {
                ["kind"] = gate.Gate.Kind.ToString(),
                ["subject"] = gate.Gate.Subject,
                ["required"] = gate.Gate.Required,
                ["current"] = gate.Current,
                ["met"] = gate.Met,
            });
        }
        var shakedowns = new Godot.Collections.Array<int>();
        foreach (int days in p.ShakedownOptionsDays)
            shakedowns.Add(days);
        result["ok"] = true;
        result["farm_type"] = p.Type.ToString();
        result["contract"] = SeaKey(p.Contract);
        result["equity_share"] = p.EquityShare;
        result["contract_capex_nok"] = (double)p.ContractCapexNok;
        result["company_capex_nok"] = (double)p.CompanyCapexNok;
        result["turnkey_premium"] = p.TurnkeyPremium;
        result["overrun_median_nok"] = (double)p.OverrunMedianNok;
        result["overrun_p90_nok"] = (double)p.OverrunP90Nok;
        result["payments"] = payments;
        result["consortium_equity_share"] = p.ConsortiumEquityShare;
        result["consortium_company_capex_nok"] = (double)p.ConsortiumCompanyCapexNok;
        result["buyout_multiple"] = p.BuyoutMultiple;
        result["licences"] = p.Licences;
        result["licence_tonnes_mtb"] = p.LicenceTonnesMtb;
        result["licence_market_value_nok"] = (double)p.LicenceMarketValueNok;
        result["licence_price_nok_per_tonne"] = (double)p.LicencePriceNokPerTonne;
        result["conversion_fee_nok"] = (double)p.ConversionFeeNok;
        result["resource_rent_before_conversion"] = p.ResourceRentBeforeConversion;
        result["teething_defects_min"] = p.TeethingDefectsMin;
        result["teething_defects_max"] = p.TeethingDefectsMax;
        result["teething_budget_nok"] = (double)p.TeethingBudgetNok;
        result["shakedown_options_days"] = shakedowns;
        result["planned_build_days"] = p.PlannedBuildDays;
        result["first_stocking_day"] = p.FirstStockingDay;
        result["gates"] = gates;
        result["first_payment_nok"] = (double)p.FirstPaymentNok;
        result["affordable"] = p.Affordable;
        result["refusal_key"] = p.RefusalKey;
        return result;
    }

    /// <summary>
    /// F2-07: start building an offshore unit (arguments as
    /// <see cref="PreviewOffshoreBuild"/>). {success, error, message, value,
    /// value2, id}: id the new unit's.
    /// </summary>
    public Godot.Collections.Dictionary BuildOffshoreUnit(string plotId, string farmType, string contract, double equityShare)
    {
        if (!Farm.FacilityNames.TryParseOffshoreType(farmType, out var type) || !TryParseSeaEnum(contract, out ContractForm form))
            return SeaRefusal("out_of_range", $"Unknown farm type '{farmType}' or contract '{contract}'");
        return FacilityResult(SimulationEngine?.BuildOffshoreUnit(plotId ?? "", type, form, equityShare));
    }

    /// <summary>
    /// F2-07: an offshore unit's construction (spec §6.3). Keys: "ok",
    /// "site_id", "project_id", "complete", "current_phase",
    /// "current_phase_index", "phase_progress", "overall_progress",
    /// "waiting_reason" ("", "tow", "sheltering", "funds"),
    /// "shelter_until_day", "paid_nok", "phases" [ { "phase", "planned_days",
    /// "status" ("done", "building", "waiting", "pending"), "start_day",
    /// "end_day", "progress", "player_started", "payment_nok" } ].
    /// </summary>
    public Godot.Collections.Dictionary GetOffshoreConstruction(string siteId)
    {
        var result = new Godot.Collections.Dictionary { ["ok"] = false, ["site_id"] = siteId ?? "" };
        var view = SimulationEngine?.State == null ? null : SimulationEngine.GetOffshoreConstruction(siteId ?? "");
        if (view == null) return result;
        var phases = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var phase in view.Phases)
        {
            phases.Add(new Godot.Collections.Dictionary
            {
                ["phase"] = phase.Phase,
                ["planned_days"] = phase.PlannedDays,
                ["status"] = phase.Status,
                ["start_day"] = phase.StartDay,
                ["end_day"] = phase.EndDay,
                ["progress"] = phase.Progress,
                ["player_started"] = phase.PlayerStarted,
                ["payment_nok"] = (double)phase.PaymentNok,
            });
        }
        result["ok"] = true;
        result["project_id"] = view.ProjectId;
        result["complete"] = view.Complete;
        result["current_phase"] = view.CurrentPhase;
        result["current_phase_index"] = view.CurrentPhaseIndex;
        result["phase_progress"] = view.PhaseProgress;
        result["overall_progress"] = view.OverallProgress;
        result["waiting_reason"] = view.WaitingReason;
        result["shelter_until_day"] = view.ShelterUntilDay;
        result["paid_nok"] = (double)view.PaidNok;
        result["phases"] = phases;
        return result;
    }

    /// <summary>
    /// F2-07: an offshore unit's state as its gauges read it (spec §6.5).
    /// Keys: "ok", "site_id", "operational", "hs_today", "appetite_today",
    /// "silo_tonnes", "silo_capacity_tonnes", "silo_days" (-1 while it feeds
    /// nothing), "silo_empty_days", "silage_tonnes", "silage_capacity_tonnes",
    /// "crew_days_since_change", "crew_change_interval_days",
    /// "crew_overdue_days", "alarm_load", "alarm_drivers" { driver → value },
    /// "next_windows" [ { "row", "day" (-1 none at P ≥ 0.8), "probability" } ],
    /// "bookings" [ { "id", "op", "vessel", "issued_day", "booked_day",
    /// "status", "completed_day", "days_charged", "charged_nok", "count",
    /// "detail", "failure_key", "open" } ], "shakedown_days",
    /// "shakedown_end_day", "defects" [ { "id", "kind", "revealed_day",
    /// "fix_started_day", "fixed_day", "fired_day", "mortality_pp" } ],
    /// "hidden_defects", "fix_cost_nok", "sensors" [ { "kind",
    /// "installed_day", "last_service_day" } ], "sensor_prices" [ { "kind",
    /// "install_nok", "service_nok" } ], "structure_known",
    /// "structure_known_day", "net_known_pct", "net_known_day",
    /// "equity_share", "buyout_price_nok".
    /// </summary>
    public Godot.Collections.Dictionary GetOffshoreUnitState(string siteId)
    {
        var result = new Godot.Collections.Dictionary { ["ok"] = false, ["site_id"] = siteId ?? "" };
        var view = SimulationEngine?.State == null ? null : SimulationEngine.GetOffshoreUnitState(siteId ?? "");
        if (view == null) return result;
        var drivers = new Godot.Collections.Dictionary();
        foreach (var (driver, value) in view.AlarmDrivers)
            drivers[driver] = value;
        var windows = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var (row, day, probability) in view.NextWindows)
            windows.Add(new Godot.Collections.Dictionary { ["row"] = row, ["day"] = day, ["probability"] = probability });
        var bookings = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var b in view.Bookings)
        {
            bookings.Add(new Godot.Collections.Dictionary
            {
                ["id"] = b.Id,
                ["op"] = SeaKey(b.Op),
                ["vessel"] = SeaKey(b.Vessel),
                ["issued_day"] = b.IssuedDay,
                ["booked_day"] = b.BookedDay,
                ["status"] = SeaKey(b.Status),
                ["completed_day"] = b.CompletedDay,
                ["days_charged"] = b.DaysCharged,
                ["charged_nok"] = (double)b.ChargedNok,
                ["count"] = b.Count,
                ["detail"] = b.Detail,
                ["failure_key"] = b.FailureKey,
                ["open"] = b.IsOpen,
            });
        }
        var defects = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var d in view.Defects)
        {
            defects.Add(new Godot.Collections.Dictionary
            {
                ["id"] = d.Id,
                ["kind"] = SeaKey(d.Kind),
                ["revealed_day"] = d.RevealedDay,
                ["fix_started_day"] = d.FixStartedDay,
                ["fixed_day"] = d.FixedDay,
                ["fired_day"] = d.FiredDay,
                ["mortality_pp"] = d.MortalityPp,
            });
        }
        var sensors = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var s in view.Sensors)
            sensors.Add(new Godot.Collections.Dictionary { ["kind"] = SeaKey(s.Kind), ["installed_day"] = s.InstalledDay, ["last_service_day"] = s.LastServiceDay });
        var prices = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        foreach (var (kind, install, service) in view.SensorPrices)
            prices.Add(new Godot.Collections.Dictionary { ["kind"] = SeaKey(kind), ["install_nok"] = (double)install, ["service_nok"] = (double)service });
        result["ok"] = true;
        result["operational"] = view.Operational;
        result["hs_today"] = view.HsTodayM;
        result["appetite_today"] = view.AppetiteToday;
        result["silo_tonnes"] = view.SiloTonnes;
        result["silo_capacity_tonnes"] = view.SiloCapacityTonnes;
        result["silo_days"] = double.IsInfinity(view.SiloDays) ? -1.0 : view.SiloDays;
        result["silo_empty_days"] = view.SiloEmptyDays;
        result["silage_tonnes"] = view.SilageTonnes;
        result["silage_capacity_tonnes"] = view.SilageCapacityTonnes;
        result["crew_days_since_change"] = view.CrewDaysSinceChange;
        result["crew_change_interval_days"] = view.CrewChangeIntervalDays;
        result["crew_overdue_days"] = view.CrewOverdueDays;
        result["alarm_load"] = view.AlarmLoad;
        result["alarm_drivers"] = drivers;
        result["next_windows"] = windows;
        result["bookings"] = bookings;
        result["shakedown_days"] = view.ShakedownDays;
        result["shakedown_end_day"] = view.ShakedownEndDay;
        result["defects"] = defects;
        result["hidden_defects"] = view.HiddenDefects;
        result["fix_cost_nok"] = (double)view.FixCostNok;
        result["sensors"] = sensors;
        result["sensor_prices"] = prices;
        result["structure_known"] = view.StructureKnown;
        result["structure_known_day"] = view.StructureKnownAsOfDay;
        result["net_known_pct"] = view.NetKnownPct;
        result["net_known_day"] = view.NetKnownAsOfDay;
        result["equity_share"] = view.EquityShare;
        result["buyout_price_nok"] = (double)view.BuyOutPriceNok;
        return result;
    }

    /// <summary>
    /// F2-07: what booking <paramref name="op"/> ("resupply", "silage",
    /// "crew_change", "net_inspection", "stocking", "delousing", "harvest" …)
    /// with <paramref name="vessel"/> ("standard", "dp_walk_to_work",
    /// "offshore_wellboat", "helicopter") for <paramref name="day"/> would
    /// cost and when it would be done, from the unit's own forecast model
    /// (never draws). Keys: "ok", "booked_day", "p_booked_day", "done_by_day"
    /// [P(done by booked day + i)], "expected_days", "expected_cost_nok",
    /// "day_rate_nok", "not_done_in_horizon", "silo_days_on_booked_day" (-1
    /// while the unit feeds nothing).
    /// </summary>
    public Godot.Collections.Dictionary PreviewOffshoreBooking(string siteId, string op, int day, string vessel)
    {
        var result = new Godot.Collections.Dictionary { ["ok"] = false, ["site_id"] = siteId ?? "" };
        if (SimulationEngine?.State == null || !TryParseSeaEnum(op, out LakskongenSim.Environment.OffshoreOp kind)
            || !TryParseSeaEnum(vessel, out LakskongenSim.Environment.VesselClass boat))
            return result;
        var p = SimulationEngine.PreviewOffshoreBooking(siteId ?? "", kind, day, boat);
        if (p == null) return result;
        var doneBy = new Godot.Collections.Array<double>();
        foreach (double probability in p.DoneByDay)
            doneBy.Add(probability);
        result["ok"] = true;
        result["booked_day"] = p.BookedDay;
        result["p_booked_day"] = p.ProbabilityOnBookedDay;
        result["done_by_day"] = doneBy;
        result["expected_days"] = p.ExpectedChargedDays;
        result["expected_cost_nok"] = (double)p.ExpectedCostNok;
        result["day_rate_nok"] = (double)p.DayRateNok;
        result["not_done_in_horizon"] = p.NotDoneWithinHorizon;
        result["silo_days_on_booked_day"] = double.IsInfinity(p.SiloDaysOnBookedDay) ? -1.0 : p.SiloDaysOnBookedDay;
        return result;
    }

    /// <summary>
    /// F2-07: book a vessel operation at an offshore unit for a day (keys of
    /// <see cref="PreviewOffshoreBooking"/>); <paramref name="count"/> and
    /// <paramref name="detail"/> a stocking's smolts and strain or a
    /// delousing's method. {success, error, message, value (the day), value2
    /// (P on the day), id (the booking's)}.
    /// </summary>
    public Godot.Collections.Dictionary BookOffshoreOperation(string siteId, string op, int day, string vessel, int count = 0, string detail = "")
    {
        if (!TryParseSeaEnum(op, out LakskongenSim.Environment.OffshoreOp kind) || !TryParseSeaEnum(vessel, out LakskongenSim.Environment.VesselClass boat))
            return SeaRefusal("out_of_range", $"Unknown operation '{op}' or vessel '{vessel}'");
        return FacilityResult(SimulationEngine?.BookOffshoreOperation(siteId ?? "", kind, day, boat, count, detail ?? ""));
    }

    /// <summary>F2-07: book an operation into its first green window ("Book første grønne vindu", P ≥ 0.8). Result as <see cref="BookOffshoreOperation"/>.</summary>
    public Godot.Collections.Dictionary BookOffshoreFirstWindow(string siteId, string op, string vessel, int count = 0, string detail = "")
    {
        if (!TryParseSeaEnum(op, out LakskongenSim.Environment.OffshoreOp kind) || !TryParseSeaEnum(vessel, out LakskongenSim.Environment.VesselClass boat))
            return SeaRefusal("out_of_range", $"Unknown operation '{op}' or vessel '{vessel}'");
        return FacilityResult(SimulationEngine?.BookFirstGreenWindow(siteId ?? "", kind, boat, count: count, detail: detail ?? ""));
    }

    /// <summary>F2-07: cancel an open booking (the charter already paid stays paid).</summary>
    public Godot.Collections.Dictionary CancelOffshoreBooking(string siteId, string bookingId)
        => FacilityResult(SimulationEngine?.CancelOffshoreBooking(siteId ?? "", bookingId ?? ""));

    /// <summary>F2-07: the tow's odds and cost before it starts ("Start slep nå", spec §6.3). Keys: "ok", "p_hold", "hold_days", "limit_hs", "tugs_nok_per_day", "cost_if_holds_nok", "expected_cost_nok", "shelter_days_min", "shelter_days_max".</summary>
    public Godot.Collections.Dictionary PreviewOffshoreTow(string siteId)
    {
        var result = new Godot.Collections.Dictionary { ["ok"] = false, ["site_id"] = siteId ?? "" };
        var p = SimulationEngine?.State == null ? null : SimulationEngine.PreviewTow(siteId ?? "");
        if (p == null) return result;
        result["ok"] = true;
        result["p_hold"] = p.HoldProbability;
        result["hold_days"] = p.HoldDays;
        result["limit_hs"] = p.TowLimitHsM;
        result["tugs_nok_per_day"] = (double)p.TugsNokPerDay;
        result["cost_if_holds_nok"] = (double)p.CostIfHoldsNok;
        result["expected_cost_nok"] = (double)p.ExpectedCostNok;
        result["shelter_days_min"] = p.ShelterDaysMin;
        result["shelter_days_max"] = p.ShelterDaysMax;
        return result;
    }

    /// <summary>F2-07: start the tow to the plot (the player's call; value = P(the window holds)).</summary>
    public Godot.Collections.Dictionary StartOffshoreTow(string siteId)
        => FacilityResult(SimulationEngine?.StartTow(siteId ?? ""));

    /// <summary>F2-07: choose the shakedown (0, 60, 90 or 120 days) before the first stocking.</summary>
    public Godot.Collections.Dictionary ChooseOffshoreShakedown(string siteId, int days)
        => FacilityResult(SimulationEngine?.ChooseShakedown(siteId ?? "", days));

    /// <summary>F2-07: fix a revealed teething defect (value = its price, value2 = the day it is fixed).</summary>
    public Godot.Collections.Dictionary FixTeethingDefect(string siteId, string defectId)
        => FacilityResult(SimulationEngine?.FixTeethingDefect(siteId ?? "", defectId ?? ""));

    /// <summary>F2-07: install a sensor package ("mru", "subsea_cam", "echo", "ais_broadband").</summary>
    public Godot.Collections.Dictionary InstallOffshoreSensors(string siteId, string kind)
    {
        if (!TryParseSeaEnum(kind, out Farm.Offshore.SensorPackageKind package))
            return SeaRefusal("out_of_range", $"Unknown sensor package '{kind}'");
        return FacilityResult(SimulationEngine?.InstallSensorPackage(siteId ?? "", package));
    }

    /// <summary>F2-07: service every sensor package that is due.</summary>
    public Godot.Collections.Dictionary ServiceOffshoreSensors(string siteId)
        => FacilityResult(SimulationEngine?.ServiceSensors(siteId ?? ""));

    /// <summary>F2-07: repair the unit's "net", "structure" or "all" (value = the price paid).</summary>
    public Godot.Collections.Dictionary RepairOffshoreUnit(string siteId, string scope)
        => FacilityResult(SimulationEngine?.RepairOffshore(siteId ?? "", scope ?? ""));

    /// <summary>F2-07: buy the consortium partner out at book × the buy-out multiple (value = the price).</summary>
    public Godot.Collections.Dictionary BuyOutOffshorePartner(string siteId)
        => FacilityResult(SimulationEngine?.BuyOutPartner(siteId ?? ""));

    /// <summary>A refusal before the engine is asked (an argument the bridge cannot parse).</summary>
    private static Godot.Collections.Dictionary SeaRefusal(string errorKey, string message) => new()
    {
        ["success"] = false,
        ["error"] = errorKey,
        ["message"] = message,
        ["value"] = 0.0,
        ["value2"] = 0.0,
        ["id"] = "",
    };

    /// <summary>An engine enum value as the bridge's snake_case key ("CrewChange" → "crew_change").</summary>
    private static string SeaKey(System.Enum value) => UpperSnake(value.ToString()).ToLowerInvariant();

    /// <summary>Parse a snake_case key or an enum name ("crew_change", "CrewChange"); numbers are refused.</summary>
    private static bool TryParseSeaEnum<T>(string text, out T value) where T : struct, System.Enum
    {
        value = default;
        string name = (text ?? "").Replace("_", "", System.StringComparison.Ordinal).Trim();
        if (name.Length == 0 || !char.IsLetter(name[0])) return false;
        return System.Enum.TryParse(name, ignoreCase: true, out value) && System.Enum.IsDefined(value);
    }

    // ═══ END sim·sea lane block ═══
}
