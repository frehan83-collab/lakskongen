extends RefCounted
## F2-31 (farm-types spec §6.1-6.2, §7.4): the family-first build flow in the
## running game, driven by real input. UiFlowTests runs these through
## run(self) and counts them with its own _check.
##
## 1. UI input only: the map's "Nytt anlegg" → the Land card → "Landanlegg
##    (hybrid)" → pad_a → confirm. Setup before the flow (land-based-ras
##    researched, a post-smolt module on pad_delta) goes through QA verbs; the
##    flow itself is clicks. Confirm files the pad's permit cases.
## 2. Three family cards come first, variants second; every bar, pip and
##    value equals the engine's catalogue, median and bad case.
## 3. A locked card lists every gate, money included, with a progress bar.
## 4. Choosing a plot ghosts the build onto it (lens and 3D) and leaves the
##    engine's state digest exactly where it was.
## 5. The diagnosis strip names the engine's binding constraint, and follows
##    it when the engine's data change (with the money there, the next one binds).
## 6. The Hav preview shows the licence-value line and the consortium option;
##    choosing the consortium previews the 35 % share; confirm contracts a unit.
##    With no development-licence award (F2-09) the confirm files the
##    application instead, and the award on its decision day opens the build.
## 7. Contrast, text and overlap lints are clean at 1280x720 at 100 % and 125 %,
##    and a keyboard path (arrows and Enter) runs the whole Land flow.
## 8. (tests/test_bridge_contract.py) no GDScript outside QA calls the legacy builds.

const DialogLayoutLint := preload("res://tests/DialogLayoutLint.gd")

const SCENARIO := "po3_red_sone"
const LAND_PLOT := "pad_a"
const SEA_PLOT := "off_1"
const LINT_SIZE := Vector2i(1280, 720)
const LINT_SCALES := [1.0, 1.25]
## The package a pad without zoning needs (F2-14): zoning, the land permit,
## discharge, sea works and biosecurity.
const LAND_PERMIT_KINDS := ["Zoning", "LandAquaculture", "Discharge", "SeaWorks", "Biosecurity"]
## A year without an escape opens the development-licence round (catalogue gate).
const ESCAPE_FREE_YEAR := 366

var t: Node = null
var sim: Node = null


func run(runner: Node) -> void:
	t = runner
	sim = t._sim
	await _land_flow_by_mouse()
	await _land_flow_by_keyboard()
	await _hav_preview_and_consortium()


func _check(ok: bool, label: String) -> void:
	t._check(ok, label)


# ── Setup and helpers ────────────────────────────────────────────────────────

func _start_land_ready(seed: int) -> Node:
	_check(sim.start_qa_scenario(SCENARIO, seed), "build flow: %s starts (seed %d)" % [SCENARIO, seed])
	sim.set_paused(true)
	_check(sim.qa_open_farm_family("land-based-ras", "pad_delta", false),
		"build flow: setup: land-based-ras researched and a post-smolt module on pad_delta")
	var scene: Node = await t._spawn_scene_over_current_game()
	return scene


func _open_dialog(scene: Node) -> BuildSiteDialog:
	var ui: Control = scene.get_node("UILayer/GameUI")
	var map: Control = await t._open_map_by_key(ui)
	if map == null:
		_check(false, "build flow: Cmd+2 opens the map")
		return null
	var btn: Button = map.new_farm_button()
	await t._click_control(btn)
	var dialog: BuildSiteDialog = map.build_dialog()
	_check(dialog != null and dialog.visible and dialog.family() == "",
		"build flow: 'Nytt anlegg' opens the build dialog on the family cards")
	return dialog


## Real mouse-wheel input over the dialog's steps until `control` is in view.
func _scroll_to(dialog: BuildSiteDialog, control: Control) -> void:
	var scroll: ScrollContainer = dialog.get_node("%StepScroll")
	for i in 80:
		await t.get_tree().process_frame
		var view := scroll.get_global_rect()
		var r := control.get_global_rect()
		if view.encloses(r.grow(-1.0)):
			return
		var down := r.get_center().y > view.get_center().y
		for pressed in [true, false]:
			var ev := InputEventMouseButton.new()
			ev.button_index = MOUSE_BUTTON_WHEEL_DOWN if down else MOUSE_BUTTON_WHEEL_UP
			ev.pressed = pressed
			ev.factor = 1.0
			ev.position = view.get_center()
			ev.global_position = ev.position
			t.get_viewport().push_input(ev, true)


func _family_ids(row: Node) -> Array:
	return row.get_children().map(func(c: Node) -> String: return str(c.name))


func _catalog_variant(cat: Dictionary, id: String) -> Dictionary:
	for f in cat.get("families", []):
		for v in f.get("variants", []):
			if str(v.get("id", "")) == id:
				return v
	return {}


# ── 1-5: Land by mouse ───────────────────────────────────────────────────────

func _land_flow_by_mouse() -> void:
	var scene := await _start_land_ready(95)
	var dialog := await _open_dialog(scene)
	if dialog == null:
		scene.queue_free()
		return
	# 2. Families first, variants second.
	var row: Node = dialog.get_node("%FamilyRow")
	_check(_family_ids(row) == ["Family_sea", "Family_land", "Family_offshore"] and not dialog.comparison().is_visible_in_tree(),
		"build flow: three family cards come first, no variant before a family is chosen %s" % str(_family_ids(row)))
	var cat: Dictionary = sim.get_farm_type_catalog()
	var land_card := dialog.family_card("land")
	_check(land_card != null and (land_card.get_node("%Variants") as Label).text.contains(tr("FARM_VARIANT_HFS")),
		"build flow: the Land card names its variants")
	await _lint_dialog(dialog, "family cards")
	await t._click_control(land_card)
	_check(dialog.family() == "land" and dialog.comparison().is_visible_in_tree(), "build flow: a click on the Land card shows the Land variants")
	_check_bars_equal_catalogue(dialog, cat, ["open_pen", "post_smolt", "hfs", "ras"])
	# 3. A locked card lists every gate, money included, with a progress bar.
	_check_gates(dialog, cat, "ras")
	_check_gates(dialog, cat, "hfs")
	# 5. The diagnosis strip names the engine's binding constraint.
	_check_diagnosis(dialog)
	# Choose the hybrid farm, then pad_a.
	var hfs := dialog.variant_button("hfs")
	await _scroll_to(dialog, hfs)
	await t._click_control(hfs)
	_check(dialog.variant() == "hfs" and dialog.plot_lens().is_visible_in_tree(),
		"build flow: 'Landanlegg (hybrid)' is chosen and its site lens shows")
	var digest_before: String = sim.qa_state_digest()
	var pad := dialog.plot_lens().plot_button(LAND_PLOT)
	await _scroll_to(dialog, pad)
	await t._click_control(pad)
	await t.get_tree().process_frame
	# 4. The ghost stands on pad_a, in the lens and in 3D, and the engine is untouched.
	var lens_ghost: PlotLens.GhostView = dialog.plot_lens().ghost()
	var tanks := lens_ghost.shapes.filter(func(s: Dictionary) -> bool: return str(s["kind"]) == "tank").size()
	var ghost := t.get_tree().get_first_node_in_group(FarmGhost.GROUP) as FarmGhost
	_check(dialog.plot_id() == LAND_PLOT and lens_ghost.plot_id == LAND_PLOT and tanks == 5 and lens_ghost.route.size() == 2,
		"build flow: pad_a is chosen and the lens ghosts the pilot lot's 5 tanks and the pipe route (%d tanks)" % tanks)
	_check(ghost != null and ghost.plot_id == LAND_PLOT and ghost.visible and ghost.get_child_count() > tanks,
		"build flow: the 3D ghost stands on pad_a (%d pieces)" % (ghost.get_child_count() if ghost else 0))
	_check(sim.qa_state_digest() == digest_before, "build flow: ghost placement and the preview leave the engine state digest unchanged")
	# 1. The preview: every figure the engine's.
	await _check_land_preview(dialog)
	await _lint_dialog(dialog, "land preview")
	# Confirm files the permit cases.
	_check(sim.get_land_permit_cases(LAND_PLOT).is_empty(), "build flow: no permit case on pad_a before the confirm")
	var confirm: Button = dialog.get_node("%ConfirmBtn")
	_check(not confirm.disabled and confirm.text == tr("BUILD_CONFIRM_APPLY") % LAND_PERMIT_KINDS.size(),
		"build flow: the confirm applies for the %d permits ('%s')" % [LAND_PERMIT_KINDS.size(), confirm.text])
	await t._click_control(confirm)
	var cases: Array = sim.get_land_permit_cases(LAND_PLOT)
	var kinds: Array = cases.map(func(c: Dictionary) -> String: return str(c.get("kind", "")))
	_check(kinds == LAND_PERMIT_KINDS and cases.all(func(c: Dictionary) -> bool: return str(c.get("status", "")) == "Submitted"),
		"build flow: confirm filed pad_a's permit cases %s" % str(kinds))
	_check(sim.qa_state_digest() != digest_before, "build flow: the confirm changed the engine's state")
	_check(dialog.get_node("%ResultBox").visible and (dialog.get_node("%ResultTitle") as Label).text == tr("BUILD_DONE_PERMITS_TITLE") % "Storholmen",
		"build flow: the dialog says the applications are filed")
	await t._click_control(dialog.get_node("%CancelBtn"))
	_check(not dialog.visible and (t.get_tree().get_first_node_in_group(FarmGhost.GROUP) as FarmGhost).get_child_count() == 0,
		"build flow: closing the dialog takes the ghost away")
	# 5 again: the strip follows the engine's data. The family farm's binding
	# constraint is capital; with the cheapest rung's money in the bank the
	# engine's next constraint binds and the strip names that one instead.
	var before: Dictionary = sim.get_build_diagnosis().get("binding", {})
	var gb := t.get_node("/root/GameBridge")
	_check(str(before.get("key", "")) == "capital" and bool(gb.QaGrantCash(float(before.get("reference", 0.0)))),
		"build flow: setup: capital binds, and the cheapest rung's money is granted (QA)")
	var binding: Dictionary = sim.get_build_diagnosis().get("binding", {})
	var map: Control = scene.get_node("UILayer/GameUI").find_child("Panel_map", true, false)
	await t._click_control(map.new_farm_button())
	var text := dialog.diagnosis_strip().text_label().text
	var want := tr("BUILD_DIAG_LINE") % [DiagnosisStrip.constraint_text(binding), DiagnosisStrip.relief_text(binding)]
	_check(str(binding.get("key", "")) not in ["", "capital"] and text == want,
		"build flow: with the money there the strip names the engine's next constraint '%s' ('%s')" % [str(binding.get("key", "")), text])
	dialog.close()
	scene.queue_free()
	await t.get_tree().process_frame


func _check_bars_equal_catalogue(dialog: BuildSiteDialog, cat: Dictionary, ids: Array) -> void:
	var axes: Array = cat.get("axes", [])
	var scale := {}
	for f in cat.get("families", []):
		for v in f.get("variants", []):
			for a in axes:
				var m: Variant = (v.get("axes", {}) as Dictionary).get(a, {}).get("median")
				if m != null:
					scale[a] = maxf(float(scale.get(a, 0.0)), absf(float(m)))
	var wrong: Array = []
	var cells := 0
	for id in ids:
		var v := _catalog_variant(cat, id)
		for a in axes:
			var entry: Dictionary = (v.get("axes", {}) as Dictionary).get(a, {})
			var bar := dialog.comparison().bar(id, a)
			var value := dialog.comparison().value_label(id, a)
			cells += 1
			if bar == null or value == null:
				wrong.append("%s.%s missing" % [id, a])
				continue
			var m: Variant = entry.get("median")
			var b: Variant = entry.get("bad")
			var want_m := -1.0 if m == null else float(m)
			var want_b := -1.0 if b == null else float(b)
			if not is_equal_approx(bar.median, want_m) or not is_equal_approx(bar.bad, want_b) \
					or bar.filled != ComparisonBars.pips_for(want_m, float(scale.get(a, 0.0))) \
					or value.text != ComparisonBars.format_axis(entry):
				wrong.append("%s.%s" % [id, a])
	_check(wrong.is_empty() and cells == ids.size() * axes.size(),
		"build flow: %d aligned bars, their pips and values equal the catalogue (median and bad case) %s" % [cells, str(wrong.slice(0, 4))])
	var survival := dialog.comparison().bar("hfs", "survival")
	_check(survival != null and survival.bad_pip > 0 and survival.bad < survival.median,
		"build flow: survival shows the median and the bad case (hollow pip %d)" % (survival.bad_pip if survival else -1))


func _check_gates(dialog: BuildSiteDialog, cat: Dictionary, id: String) -> void:
	var v := _catalog_variant(cat, id)
	var gates: Array = v.get("gates", [])
	var list := dialog.gate_list(id)
	if list == null:
		_check(false, "build flow: %s lists its gates" % id)
		return
	var rows: Array = list.rows()
	var wrong: Array = []
	for i in gates.size():
		var g: Dictionary = gates[i]
		if i >= rows.size():
			wrong.append("row %d missing" % i)
			continue
		var r: Node = rows[i]
		var bar := r.get_node_or_null("Progress") as ProgressBar
		if str(r.get_meta(&"kind")) != str(g.get("kind", "")) or bool(r.get_meta(&"met")) != bool(g.get("met", false)) \
				or bar == null or not is_equal_approx(bar.value, GateList.progress_of(g)):
			wrong.append(str(g.get("kind", "")))
	var funds := rows.filter(func(r: Node) -> bool: return str(r.get_meta(&"kind")) == "Funds")
	_check(rows.size() == gates.size() and wrong.is_empty() and funds.size() == 1,
		"build flow: %s lists every gate with a progress bar, money included (%d gates) %s" % [id, gates.size(), str(wrong)])


func _check_diagnosis(dialog: BuildSiteDialog) -> void:
	var d: Dictionary = sim.get_build_diagnosis()
	var binding: Dictionary = d.get("binding", {})
	var strip := dialog.diagnosis_strip()
	var text := strip.text_label().text
	var want := tr("BUILD_DIAG_LINE") % [DiagnosisStrip.constraint_text(binding), DiagnosisStrip.relief_text(binding)]
	_check(strip.visible and not binding.is_empty() and text == want,
		"build flow: the diagnosis strip names the engine's binding constraint '%s' ('%s')" % [str(binding.get("key", "")), text])


func _check_land_preview(dialog: BuildSiteDialog) -> void:
	var p := dialog.farm_preview()
	var e: Dictionary = p.get("economics", {})
	var land: Dictionary = p.get("land", {})
	var view := dialog.facility_preview()
	_check(bool(p.get("ok", false)) and str(p.get("template", "")) == "hfs_pilot_lot" and view.is_visible_in_tree(),
		"build flow: pad_a's preview of the pilot lot shows")
	_check(view.kpi("Capex").text == SimFormatters.format_nok(float(e.get("capex_nok", 0.0)))
			and view.kpi("CapacityCost").text == SimFormatters.format_nok(float(e.get("capacity_cost_nok_per_t_year", 0.0))) + "/t/år"
			and view.kpi("FirstHarvest").text == SimFormatters.format_day(int(e.get("first_harvest_day", 0))),
		"build flow: capex, capacity cost per t HOG a year and the first harvest are the engine's (%s, %s, %s)" % [
			view.kpi("Capex").text, view.kpi("CapacityCost").text, view.kpi("FirstHarvest").text])
	var year := int(e.get("payback_year", -1))
	_check(view.kpi("Payback").text == (str(year) if year > 0 else tr("BUILD_KPI_PAYBACK_NEVER"))
			and view.kpi("Farming").text == SimFormatters.num(float(e.get("farming_cost_nok_per_kg", 0.0)), 1) + " kr/kg",
		"build flow: the payback year and NOK/kg HOG are the engine's (%s, %s)" % [view.kpi("Payback").text, view.kpi("Farming").text])
	var pays: Array = land.get("payments", [])
	var grid := view.payments()
	var overrun_cell := grid.get_node_or_null("Row0_3") as Label
	var p0: Dictionary = pays[0] if not pays.is_empty() else {}
	_check(grid.get_child_count() == (pays.size() + 1) * grid.columns and overrun_cell != null
			and overrun_cell.text == "%s – %s" % [SimFormatters.format_nok(float(p0.get("overrun_p50_nok", 0.0))), SimFormatters.format_nok(float(p0.get("overrun_p90_nok", 0.0)))],
		"build flow: the payment schedule lists every phase with its p50/p90 overrun band (%d phases)" % pays.size())
	var plot := view.runway().plot()
	var months: Array = e.get("runway", [])
	var same := plot.p50.size() == 36 and months.size() == 36
	for i in mini(plot.p50.size(), months.size()):
		same = same and is_equal_approx(plot.p50[i], float(months[i].get("cash_p50_nok", 0.0))) \
			and is_equal_approx(plot.p90[i], float(months[i].get("cash_p90_nok", 0.0)))
	_check(same, "build flow: the 36-month cash runway draws the engine's months, p50 and p90")
	var permits: Array = land.get("permits", [])
	var bad: Array = []
	for pm in permits:
		var r := view.permits().row(str(pm.get("kind", "")))
		if r == null or not is_equal_approx(float(r.get_meta(&"refusal_probability")), float(pm.get("refusal_probability", -1.0))):
			bad.append(pm.get("kind"))
		elif str(pm.get("status", "")) == "needed" and r.get_node_or_null("Drivers") == null:
			bad.append("%s drivers" % str(pm.get("kind")))
	_check(permits.size() == LAND_PERMIT_KINDS.size() and bad.is_empty(),
		"build flow: the permit timeline shows every case with its refusal odds and drivers %s" % str(bad))


## Contrast, theme and Norwegian text over the open dialog, and nothing in it
## overlapping, cut or off the screen, at 1280x720 at every lint scale.
func _lint_dialog(dialog: BuildSiteDialog, where: String) -> void:
	var win := t.get_window()
	var old_size := win.size
	var old_scale := win.content_scale_factor
	for scale: float in LINT_SCALES:
		win.size = LINT_SIZE
		win.content_scale_factor = scale
		await t.get_tree().create_timer(0.3).timeout
		var label := "%s at %dx%d %d %%" % [where, LINT_SIZE.x, LINT_SIZE.y, int(round(scale * 100.0))]
		var problems: PackedStringArray = await DialogLayoutLint.settled_problems(dialog, t.get_tree())
		_check(problems.is_empty(), "build flow: nothing overlaps, is cut or leaves the screen: %s %s" % [label, str(problems.slice(0, 4))])
		await t._lint_screen("build dialog " + label)
	win.content_scale_factor = old_scale
	win.size = old_size
	await t.get_tree().process_frame


# ── 7: Land by keyboard ──────────────────────────────────────────────────────

func _land_flow_by_keyboard() -> void:
	var scene := await _start_land_ready(96)
	var dialog := await _open_dialog(scene)
	if dialog == null:
		scene.queue_free()
		return
	await t.get_tree().process_frame
	var focus := func() -> Control: return t.get_viewport().gui_get_focus_owner()
	var sea_card := dialog.family_card("sjo")
	_check(focus.call() == sea_card.choose_button(), "keyboard: the dialog opens with the Sjø card focused")
	await t._press_key(KEY_RIGHT)
	_check(focus.call() == dialog.family_card("land").choose_button(), "keyboard: Right moves to the Land card")
	await t._press_key(KEY_ENTER)
	await t.get_tree().process_frame
	_check(dialog.family() == "land" and focus.call() == dialog.variant_button("hfs"),
		"keyboard: Enter chooses Land and focuses the first open variant, the hybrid farm")
	await t._press_key(KEY_ENTER)
	await t.get_tree().process_frame
	_check(dialog.variant() == "hfs" and focus.call() == dialog.plot_lens().plot_button(LAND_PLOT),
		"keyboard: Enter chooses it and focuses the first free pad, pad_a")
	await t._press_key(KEY_ENTER)
	await t.get_tree().process_frame
	var confirm: Button = dialog.get_node("%ConfirmBtn")
	_check(dialog.plot_id() == LAND_PLOT and focus.call() == confirm, "keyboard: Enter chooses pad_a and focuses the confirm")
	await t._press_key(KEY_ENTER)
	_check(sim.get_land_permit_cases(LAND_PLOT).size() == LAND_PERMIT_KINDS.size(), "keyboard: Enter on the confirm files the permits")
	await t._press_key(KEY_ESCAPE)
	_check(not dialog.visible, "keyboard: Esc closes the dialog")
	scene.queue_free()
	await t.get_tree().process_frame


# ── 6: Hav ───────────────────────────────────────────────────────────────────

func _hav_preview_and_consortium() -> void:
	_check(sim.start_qa_new_game("Player", 600_000_000, 47), "hav: a seeded game with 600 MNOK starts")
	sim.set_paused(true)
	_check(sim.qa_open_farm_family("deep-sea-farming", "", true), "hav: setup: deep-sea-farming researched and a first harvest counted")
	sim.advance_days(ESCAPE_FREE_YEAR)
	_check(bool((sim.get_farm_type_starts().get("of1", {}) as Dictionary).get("startable", false)),
		"hav: setup: a year without an escape opens the offshore unit")
	await _hav_licence_by_the_dialog()
	var scene: Node = await t._spawn_scene_over_current_game()
	var dialog := await _open_dialog(scene)
	if dialog == null:
		scene.queue_free()
		return
	await t._click_control(dialog.family_card("hav"))
	_check(dialog.family() == "hav" and dialog.variant() == "of1", "hav: the Hav card chooses the open offshore unit")
	_check_gates(dialog, sim.get_farm_type_catalog(), "of1")
	var slot := dialog.plot_lens().plot_button(SEA_PLOT)
	await _scroll_to(dialog, slot)
	await t._click_control(slot)
	var p := dialog.farm_preview()
	var sea: Dictionary = p.get("offshore", {})
	var view := dialog.facility_preview()
	var line := view.sea_line("LicenceValue")
	_check(line != null and line.is_visible_in_tree() and line.text.contains(SimFormatters.format_nok(float(sea.get("licence_market_value_nok", 0.0))))
			and line.text.contains(SimFormatters.format_nok(float(sea.get("conversion_fee_nok", 0.0)))),
		"hav: the preview's licence-value line ('%s')" % (line.text if line else ""))
	var box := view.choice("Consortium") as CheckBox
	_check(box != null and box.text.contains(SimFormatters.percent(float(sea.get("consortium_equity_share", 0.0)))),
		"hav: the consortium option is offered ('%s')" % (box.text if box else ""))
	var alone := float((p.get("economics", {}) as Dictionary).get("capex_nok", 0.0))
	await _lint_dialog(dialog, "hav preview")
	if box != null:
		await _scroll_to(dialog, box)
		await t._click_control(box)
	var shared: Dictionary = dialog.farm_preview().get("economics", {})
	_check(is_equal_approx(float(shared.get("equity_share", 0.0)), float(sea.get("consortium_equity_share", -1.0)))
			and absf(float(shared.get("capex_nok", 0.0)) - alone * float(sea.get("consortium_equity_share", 0.0))) < 10.0,
		"hav: choosing the consortium previews its share (%s of %s)" % [SimFormatters.format_nok(float(shared.get("capex_nok", 0.0))), SimFormatters.format_nok(alone)])
	var before: int = sim.get_offshore_units().size()
	await t._click_control(dialog.get_node("%ConfirmBtn"))
	var units: Array = sim.get_offshore_units()
	_check(units.size() == before + 1 and str(units[units.size() - 1].get("plot_id", "")) == SEA_PLOT,
		"hav: confirm contracts the unit on off_1 (construction started)")
	dialog.close()
	scene.queue_free()
	await t.get_tree().process_frame


## F2-09 (merged on wave4/int): an offshore unit is built on a
## development-licence award for its plot. With none, the Hav confirm files
## the application in the open round (as the Land confirm files its permits)
## and contracts nothing; the award comes on the decision day, and only then
## does the confirm contract the unit (above).
func _hav_licence_by_the_dialog() -> void:
	var dev: Dictionary = sim.get_development_round()
	_check(bool(dev.get("open", false)) and (dev.get("awards", []) as Array).is_empty(),
		"hav: setup: the escape-free year opens the development-licence round, no award yet")
	var scene: Node = await t._spawn_scene_over_current_game()
	var dialog := await _open_dialog(scene)
	if dialog == null:
		scene.queue_free()
		return
	await t._click_control(dialog.family_card("hav"))
	var slot := dialog.plot_lens().plot_button(SEA_PLOT)
	await _scroll_to(dialog, slot)
	await t._click_control(slot)
	var confirm: Button = dialog.get_node("%ConfirmBtn")
	_check(str((dialog.farm_preview().get("offshore", {}) as Dictionary).get("refusal_key", "")) == "licence_missing"
			and not confirm.disabled and confirm.text == tr("BUILD_CONFIRM_APPLY_LICENCE"),
		"hav: with no award the confirm applies for development licences ('%s')" % confirm.text)
	await _lint_dialog(dialog, "hav licence")
	var before: int = sim.get_offshore_units().size()
	await t._click_control(confirm)
	var filed: Array = (sim.get_development_round().get("applications", []) as Array).filter(
		func(a: Dictionary) -> bool: return str(a.get("plot_id", "")) == SEA_PLOT and str(a.get("status", "")) == "Submitted")
	var plot_name := str(dialog.plot_lens().plot_by_id(SEA_PLOT).get("name", SEA_PLOT))
	var title := (dialog.get_node("%ResultTitle") as Label).text
	_check(filed.size() == 1 and sim.get_offshore_units().size() == before and title == tr("BUILD_DONE_LICENCE_TITLE") % plot_name,
		"hav: confirm files the application for %s and contracts nothing ('%s')" % [SEA_PLOT, title])
	dialog.close()
	scene.queue_free()
	await t.get_tree().process_frame
	if filed.is_empty():
		return
	sim.advance_days(int(filed[0].get("decision_day", 0)) - sim.get_current_day() + 1)
	var awards: Array = (sim.get_development_round().get("awards", []) as Array).filter(
		func(a: Dictionary) -> bool: return bool(a.get("company", false)) and str(a.get("plot_id", "")) == SEA_PLOT)
	_check(awards.size() == 1 and int(awards[0].get("licences", 0)) > 0,
		"hav: the decision day brings the award for %s (score %.2f against %.2f)" % [SEA_PLOT, float(filed[0].get("score", 0.0)), float(filed[0].get("bar", 0.0))])
