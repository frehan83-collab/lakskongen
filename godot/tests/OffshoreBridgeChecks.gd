extends RefCounted
## F2-06 wiring and F2-07 (farm-types spec §7.1, §6.2-§6.5): the sea lane's
## bridge verbs run in the real game, through GameBridge (C#), SimProxies and
## SimBridge, headless. Data only: the boards that read them are F2-31/F2-33.
## UiFlowTests runs these through run(self) and counts them with its own _check.
##
## 1. Before the game has an offshore unit there is no sea to forecast.
## 2. The build preview carries the contract, its payment schedule, the
##    consortium option, the licence value, the teething deck, the first
##    stocking and the gates; a locked build is refused with the preview's own
##    key and changes nothing; arguments the bridge cannot parse are refused.
## 3. Once a unit stands (a scenario-path unit from the next day's Metocean
##    phase), the sea-state forecast holds ten ordered days with a probability
##    for every board row, and the unit's gauges read today's sea.
## 4. The booking and tow previews give P(done by day), P(the window holds)
##    and their charter; the operation verbs refuse an unfinished unit before
##    any change.
## 5. A day later the forecast is issued for the new day.

const SCENARIO := "po3_red_sone"
const PLOT := "off_1"
const FARM_TYPE := "semi_submersible"
const FORECAST_DAYS := 10
const PREVIEW_HORIZON_DAYS := 30
const CONSORTIUM_SHARE := 0.35
const SHAKEDOWN_OPTIONS := [0, 60, 90, 120]
const QA_GRANT_NOK := 1_000_000_000.0
## A failed tow window shelters 5-15 days (F2-07 acceptance 4).
const TOW_SHELTER_DAYS := [5, 15]

var t: Node = null


func run(runner: Node) -> void:
	t = runner
	var sim: Node = t._sim
	_check(sim.start_qa_scenario(SCENARIO, 91), "sea bridge: seeded scenario starts")
	sim.set_paused(true)
	_check(not bool(sim.get_sea_state_forecast("offshore_1").get("ok", true)),
		"sea bridge: no forecast before the game has an offshore unit")
	_build_preview_and_refusals(sim)
	var unit_id := _legacy_unit(sim)
	if unit_id == "":
		return
	# A scenario-path unit's sea starts with the game's next Metocean phase.
	sim.advance_days(1)
	sim.set_paused(true)
	_forecast_and_gauges(sim, unit_id)
	_booking_preview_and_refusals(sim, unit_id)
	var issued := int(sim.get_sea_state_forecast(unit_id).get("issued_day", -1))
	sim.advance_days(1)
	sim.set_paused(true)
	var next: Dictionary = sim.get_sea_state_forecast(unit_id)
	_check(bool(next.get("ok", false)) and int(next.get("issued_day", -1)) == issued + 1,
		"sea bridge: a day later the forecast is issued for the new day (%d -> %d)" % [issued, int(next.get("issued_day", -1))])
	await t.get_tree().process_frame


func _check(ok: bool, label: String) -> void:
	t._check(ok, label)


## The build preview, alone and in consortium, and the refusals before any change.
func _build_preview_and_refusals(sim: Node) -> void:
	var alone: Dictionary = sim.preview_offshore_build(PLOT, FARM_TYPE, "managed", 1.0)
	var contract := float(alone.get("contract_capex_nok", 0.0))
	var payments: Array = alone.get("payments", [])
	var paid := 0.0
	var phases_named := true
	for pay in payments:
		paid += float((pay as Dictionary).get("payment_nok", 0.0))
		phases_named = phases_named and str((pay as Dictionary).get("phase", "")) != ""
	_check(bool(alone.get("ok", false)) and contract > 0.0 and payments.size() >= 4 and phases_named
		and is_equal_approx(paid, float(alone.get("company_capex_nok", -1.0))),
		"sea bridge: the build preview lists the contract and its payment schedule (%d phases, %.0f of %.0f NOK)" % [payments.size(), paid, contract])
	_check(is_equal_approx(float(alone.get("consortium_equity_share", 0.0)), CONSORTIUM_SHARE)
		and is_equal_approx(float(alone.get("consortium_company_capex_nok", 0.0)), contract * CONSORTIUM_SHARE),
		"sea bridge: the preview offers the consortium at 35 %% of the contract (%.0f NOK)" % float(alone.get("consortium_company_capex_nok", 0.0)))
	_check(int(alone.get("licences", 0)) > 0 and float(alone.get("licence_market_value_nok", 0.0)) > 0.0
		and float(alone.get("licence_tonnes_mtb", 0.0)) > 0.0,
		"sea bridge: the preview carries the licence value line (%d licences, %.0f NOK)" % [int(alone.get("licences", 0)), float(alone.get("licence_market_value_nok", 0.0))])
	var shakedowns: Array = alone.get("shakedown_options_days", [])
	_check(shakedowns == SHAKEDOWN_OPTIONS and int(alone.get("teething_defects_max", 0)) >= int(alone.get("teething_defects_min", 1))
		and int(alone.get("teething_defects_min", 0)) > 0,
		"sea bridge: the preview shows the teething deck and the shakedown options (%s)" % str(shakedowns))
	_check(int(alone.get("planned_build_days", 0)) > 0
		and int(alone.get("first_stocking_day", -1)) == int(sim.current_day) + int(alone.get("planned_build_days", 0)),
		"sea bridge: the first stocking is the planned build away (day %d)" % int(alone.get("first_stocking_day", -1)))
	var gates: Array = alone.get("gates", [])
	var unmet := gates.filter(func(g: Dictionary) -> bool: return not bool(g.get("met", true)))
	var refusal := str(alone.get("refusal_key", ""))
	_check(gates.size() > 0 and unmet.size() > 0 and refusal != "",
		"sea bridge: a locked unit shows its unmet gates and its refusal (%s; %d of %d gates unmet)" % [refusal, unmet.size(), gates.size()])

	var shared: Dictionary = sim.preview_offshore_build(PLOT, FARM_TYPE, "managed", CONSORTIUM_SHARE)
	_check(bool(shared.get("ok", false)) and is_equal_approx(float(shared.get("equity_share", 0.0)), CONSORTIUM_SHARE)
		and is_equal_approx(float(shared.get("company_capex_nok", 0.0)), contract * CONSORTIUM_SHARE),
		"sea bridge: in consortium the company pays its 35 %% (%.0f NOK)" % float(shared.get("company_capex_nok", 0.0)))

	var balance := float(sim.balance)
	var built: Dictionary = sim.build_offshore_unit(PLOT, FARM_TYPE, "managed", 1.0)
	_check(not bool(built.get("success", true)) and str(built.get("error", "")) == refusal
		and sim.get_offshore_units().is_empty() and is_equal_approx(float(sim.balance), balance),
		"sea bridge: the locked build is refused with the preview's key and changes nothing (%s)" % str(built.get("error", "")))
	var bad_type: Dictionary = sim.preview_offshore_build(PLOT, "zeppelin", "managed", 1.0)
	var bad_contract: Dictionary = sim.build_offshore_unit(PLOT, FARM_TYPE, "handshake", 1.0)
	_check(not bool(bad_type.get("ok", true)) and str(bad_type.get("refusal_key", "")) == "out_of_range"
		and str(bad_contract.get("error", "")) == "out_of_range",
		"sea bridge: an unknown farm type or contract is refused as out_of_range")


## A scenario-path unit (legacy build, its construction running) on off_1.
func _legacy_unit(sim: Node) -> String:
	var bridge: Node = t.get_node("/root/GameBridge")
	_check(bool(bridge.QaGrantCash(QA_GRANT_NOK)), "sea bridge: a QA grant pays for the unit")
	_check(sim.build_offshore_farm("Havmerd bro", "semi_submersible"), "sea bridge: an OF1-class unit is built")
	var units: Array = sim.get_offshore_units()
	var unit_id := str((units[0] as Dictionary).get("id", "")) if units.size() == 1 else ""
	_check(unit_id != "", "sea bridge: the unit is listed (%s)" % unit_id)
	return unit_id


## The window board's forecast and the unit's gauges.
func _forecast_and_gauges(sim: Node, unit_id: String) -> void:
	var forecast: Dictionary = sim.get_sea_state_forecast(unit_id)
	var days: Array = forecast.get("days", [])
	var rows: Array = forecast.get("rows", [])
	var limits: Dictionary = forecast.get("limits", {})
	_check(bool(forecast.get("ok", false)) and str(forecast.get("plot_id", "")) == PLOT
		and int(forecast.get("issued_day", -1)) == int(sim.current_day) and days.size() == FORECAST_DAYS,
		"sea bridge: the forecast at the unit holds %d days issued today (%d)" % [days.size(), int(forecast.get("issued_day", -1))])
	var ordered := true
	var every_row := rows.size() > 0
	for i in days.size():
		var day: Dictionary = days[i]
		ordered = ordered and int(day.get("lead", 0)) == i + 1
		ordered = ordered and float(day.get("hs_p10", -1.0)) >= 0.0
		ordered = ordered and float(day.get("hs_p10", 0.0)) <= float(day.get("hs_p50", -1.0))
		ordered = ordered and float(day.get("hs_p50", 0.0)) <= float(day.get("hs_p90", -1.0))
		var p_ok: Dictionary = day.get("p_ok", {})
		for row in rows:
			var p := float(p_ok.get(row, -1.0))
			every_row = every_row and p >= 0.0 and p <= 1.0
	_check(ordered, "sea bridge: each forecast day has its lead and Hs p10 <= p50 <= p90")
	_check(every_row and limits.size() == rows.size(),
		"sea bridge: each forecast day gives every board row a probability (%d rows)" % rows.size())
	var today: Dictionary = forecast.get("today", {})
	var state: Dictionary = sim.get_offshore_unit_state(unit_id)
	_check(bool(state.get("ok", false)) and not bool(state.get("operational", true))
		and is_equal_approx(float(state.get("hs_today", -1.0)), float(today.get("hs", -2.0))),
		"sea bridge: the unit's gauges read today's sea (Hs %.2f m) while it is built" % float(state.get("hs_today", -1.0)))
	var drivers: Dictionary = state.get("alarm_drivers", {})
	var load := 0.0
	for value in drivers.values():
		load += float(value)
	_check(drivers.size() > 0 and is_equal_approx(load, float(state.get("alarm_load", -1.0)))
		and float(state.get("silo_capacity_tonnes", 0.0)) > 0.0 and int(state.get("crew_change_interval_days", 0)) > 0
		and state.get("next_windows", null) is Array,
		"sea bridge: the gauges carry the silo, the crew clock, the alarm load and the next windows (alarm %.2f)" % load)
	var build: Dictionary = sim.get_offshore_construction(unit_id)
	var progress := float(build.get("overall_progress", -1.0))
	_check(bool(build.get("ok", false)) and not bool(build.get("complete", true)) and progress >= 0.0 and progress < 1.0,
		"sea bridge: the construction view gives the unit's progress (%.3f)" % progress)


## The booking preview and the operation verbs on a unit still being built.
func _booking_preview_and_refusals(sim: Node, unit_id: String) -> void:
	var day := int(sim.current_day) + 2
	var preview: Dictionary = sim.preview_offshore_booking(unit_id, "resupply", day, "standard")
	var done_by: Array = preview.get("done_by_day", [])
	var monotone := done_by.size() == PREVIEW_HORIZON_DAYS
	for i in done_by.size():
		var p := float(done_by[i])
		monotone = monotone and p >= 0.0 and p <= 1.0 and (i == 0 or p >= float(done_by[i - 1]))
	_check(bool(preview.get("ok", false)) and int(preview.get("booked_day", -1)) == day and monotone
		and is_equal_approx(float(preview.get("p_booked_day", -1.0)), float(done_by[0]) if done_by.size() > 0 else -2.0),
		"sea bridge: the booking preview gives P(done by day) over %d days (%.2f on the booked day)" % [done_by.size(), float(preview.get("p_booked_day", -1.0))])
	_check(float(preview.get("day_rate_nok", 0.0)) > 0.0 and float(preview.get("expected_cost_nok", 0.0)) >= float(preview.get("day_rate_nok", 0.0)),
		"sea bridge: the preview prices the charter from the booked day (%.0f NOK expected)" % float(preview.get("expected_cost_nok", 0.0)))
	var bad: Dictionary = sim.preview_offshore_booking(unit_id, "teleport", day, "standard")
	_check(not bool(bad.get("ok", true)), "sea bridge: an unknown operation has no preview")

	var balance := float(sim.balance)
	var booked: Dictionary = sim.book_offshore_operation(unit_id, "resupply", day, "standard")
	var first: Dictionary = sim.book_offshore_first_window(unit_id, "crew_change", "standard")
	var tow: Dictionary = sim.start_offshore_tow(unit_id)
	var buyout: Dictionary = sim.buy_out_offshore_partner(unit_id)
	var bookings: Array = sim.get_offshore_unit_state(unit_id).get("bookings", [])
	_check(str(booked.get("error", "")) == "not_operational" and str(first.get("error", "")) == "not_operational"
		and bookings.is_empty() and is_equal_approx(float(sim.balance), balance),
		"sea bridge: a unit still being built takes no booking and pays nothing (%s, %s)" % [str(booked.get("error", "")), str(first.get("error", ""))])
	_check(str(tow.get("error", "")) == "not_found" and str(buyout.get("error", "")) == "not_consortium",
		"sea bridge: a scenario unit has no tow to start and no partner to buy out (%s, %s)" % [str(tow.get("error", "")), str(buyout.get("error", ""))])
	# The tow's odds read the plot's sea whatever the unit's stage (spec §6.3 "P(vinduet holder 3 døgn)").
	var odds: Dictionary = sim.preview_offshore_tow(unit_id)
	var p_hold := float(odds.get("p_hold", -1.0))
	_check(bool(odds.get("ok", false)) and p_hold >= 0.0 and p_hold <= 1.0 and float(odds.get("tugs_nok_per_day", 0.0)) > 0.0
		and [int(odds.get("shelter_days_min", 0)), int(odds.get("shelter_days_max", 0))] == TOW_SHELTER_DAYS
		and float(odds.get("expected_cost_nok", 0.0)) >= float(odds.get("cost_if_holds_nok", 1.0)),
		"sea bridge: the tow preview gives P(the window holds) and its cost (%.2f, %.0f NOK expected)" % [p_hold, float(odds.get("expected_cost_nok", 0.0))])
