extends RefCounted
## Geo stages 2-3, track B (docs/geo/STAGE23_PLAN.md §2 B): the open-pen
## candidates in the build dialog's Sjø step, checked in the running game
## with real input. UiFlowTests runs these through run(self) and counts them
## with its own _check. One scenario (Første sesong, seed 5), about 20 s.
##
## 1. The tutorial's route (the home zone's «Bygg lokalitet», then the
##    confirm) opens on the area's 12 places with the first free one, PO03-01,
##    preselected; the ledger is the zone's plan; the 'why here' shows the
##    bridge's score, weights, bars, measurements, binding sentence, zone and
##    honest note; choosing other places changes nothing in the engine.
## 2. The dialog with its places passes the layout, theme, contrast and text
##    lints at 1280x720 at 100 % and 125 %, the confirm on screen.
## 3. The confirm builds at PO03-01: the site lies there ('candidate', the
##    candidate's x/y), the starting site at its zone's anchor ('anchor'); the
##    dialog opened again shows PO03-01 disabled, its tooltip naming the site.
## 4. «Nytt anlegg» → Sjø with no zone lists the home area's places, nothing
##    chosen, the confirm waiting; keys alone (Enter, Down, Up, Enter, Enter)
##    choose a place, follow it in the 'why here' and build there.
## 5. The Hav lens: hav_1 shows its open-sea area, planning stage, real
##    distance and depth with the climate note; off_1 has no geo rows.
## 6-8. The captain's review of track B (findings 1-5): a build goes to the
##    place the player chose, not the suggestion, by a click and by keys that
##    skip a held place, and the picker's subtitle frames its list (one
##    instruction); open_for_candidate keeps the place it is given (the zone
##    follows a place in another area, a held or unknown place stays chosen
##    with its check's text); with every place in the home area held, Sjø
##    takes the home zone, the 'why here' says so, the confirm builds there.

const DialogLayoutLint := preload("res://tests/DialogLayoutLint.gd")

const SCENARIO := "forste_sesong"
const SEED := 5
const HOME_ZONE := "fjord_west"
const FIRST := "PO03-01"
const LINT_SIZE := Vector2i(1280, 720)
const LINT_SCALES := [1.0, 1.25]

var t: Node = null
var sim: Node = null


func run(runner: Node) -> void:
	t = runner
	sim = t._sim
	GameplayPrefs.force_tutorial(false)
	GameplayPrefs.force_auto_pause(true)
	_check(sim.start_qa_scenario(SCENARIO, SEED), "geo sjø: %s starts (seed %d)" % [SCENARIO, SEED])
	sim.set_paused(true)
	_check(bool(t.get_node("/root/GameBridge").QaGrantCash(20_000_000.0)), "geo sjø: setup: a QA grant pays for two sites")
	var scene: Node = await t._spawn_scene_over_current_game()
	var map: Control = await t._open_map_by_key(scene.get_node("UILayer/GameUI"))
	if map == null:
		_check(false, "geo sjø: Cmd+2 opens the map")
	else:
		await _tutorial_route_builds_at_the_first_place(map)
		await _the_picker_builds_by_keys_alone(map)
		_the_hav_lens_names_the_open_sea_area(map)
		await _a_chosen_place_is_where_the_build_goes(map)
		await _open_for_candidate_keeps_the_place_asked_for(map)
		await _every_place_held(map)
	scene.queue_free()
	await t.get_tree().process_frame
	GameplayPrefs.force_auto_pause(null)
	GameplayPrefs.force_tutorial(null)


func _check(ok: bool, label: String) -> void:
	t._check(ok, label)


# ── Helpers ──────────────────────────────────────────────────────────────────

func _row(rows: Array, id: String) -> Dictionary:
	for r in rows:
		if str(r.get("id", "")) == id:
			return r
	return {}


func _ids(rows: Array) -> Array:
	return rows.map(func(r: Dictionary) -> String: return str(r.get("id", "")))


## Real mouse-wheel input over the steps (beside the place list, whose own
## scroll would take the wheel) until `control` lies in the steps' view.
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
			ev.position = Vector2(view.end.x - 24.0, view.get_center().y)
			ev.global_position = ev.position
			t.get_viewport().push_input(ev, true)


func _open_home_build(map: Control) -> BuildSiteDialog:
	await t._click_control(map.region_marker(3))
	var card: Control = map.expansion_section().zone_card(HOME_ZONE)
	if card == null:
		_check(false, "geo sjø: the home zone has a card")
		return null
	await t._click_control(card.get_node("Body/Build/BuildBtn"))
	await t.get_tree().process_frame
	return map.build_dialog()


## The 'why here' shows the bridge's row: score, every term's weight, bar,
## points and measurement, the binding sentence, the zone and the note.
func _why_equals_the_bridge(why: CandidateWhyHere, c: Dictionary, zone_id: String) -> void:
	var weights: Dictionary = c.get("weights", {})
	var total := 0.0
	for w in weights.values():
		total += float(w)
	var bad: Array = []
	for term in (c.get("component_costs", {}) as Dictionary).keys():
		var key := str(term)
		var river := key == "river"
		var value := float(c.get("river_factor", 0.0)) if river else float((c.get("components", {}) as Dictionary).get(key, -1.0))
		var bar := why.component_bar(key)
		var points := why.component_value(key)
		var weight := why.component_weight(key)
		var measured := why.component_measured(key)
		if bar == null or not is_equal_approx(bar.value, value * 100.0):
			bad.append(key + " bar")
		if points == null or points.text != (("×" + SimFormatters.num(value, 2)) if river else str(int(round(value * 100.0)))):
			bad.append(key + " points")
		if weight == null or weight.text != ("" if river else SimFormatters.percent(float(weights.get(key, 0.0)) / total)):
			bad.append(key + " weight")
		if measured == null or measured.text != CandidateWhyHere.measured_text(c, key):
			bad.append(key + " measured")
		if (bar.theme_type_variation == &"ProgressBarWarn") != (key == str(c.get("binding", ""))):
			bad.append(key + " binding mark")
	_check(bad.is_empty() and (c.get("component_costs", {}) as Dictionary).size() == 8,
		"geo sjø: the 'why here' of %s shows the bridge's 7 weighted terms and the river factor %s" % [c.get("id"), str(bad)])
	var depth := why.component_measured("depth")
	var farm := why.component_measured("farm_distance")
	var current := why.component_measured("current")
	_check(depth.text == SimFormatters.num(float(c.get("depth_m", 0.0)), 0) + " m"
			and farm.text == SimFormatters.num(float(c.get("nearest_farm_km", 0.0)), 1) + " km"
			and current.text == SimFormatters.num(float((c.get("ocean", {}) as Dictionary).get("current_speed_ms", 0.0)), 2) + " m/s",
		"geo sjø: the 'why here' measures depth, nearest farm and current (%s, %s, %s)" % [depth.text, farm.text, current.text])
	_check(why.score_label().text == tr("SEA_CANDIDATE_SCORE") % int(round(float(c.get("score", 0.0)) * 100.0))
			and why.binding_label().text == tr("SEA_CANDIDATE_BINDING_" + str(c.get("binding", "")).to_upper())
			and why.zone_label().text == tr("SEA_CANDIDATE_ZONE") % SimLabels.zone(zone_id)
			and why.note_label().is_visible_in_tree() and why.note_label().text == tr("SEA_CANDIDATE_HONEST_NOTE"),
		"geo sjø: score, binding sentence, zone and honest note ('%s' · '%s')" % [why.score_label().text, why.binding_label().text])


## Layout, theme, contrast and text over the open dialog at 1280x720 at
## 100 % and 125 %, the confirm inside the window.
func _lint_dialog(dialog: BuildSiteDialog, where: String) -> void:
	var win := t.get_window()
	var old_size := win.size
	var old_scale := win.content_scale_factor
	for scale: float in LINT_SCALES:
		win.size = LINT_SIZE
		win.content_scale_factor = scale
		await t.get_tree().create_timer(0.3).timeout
		var label := "%s at %dx%d %d %%" % [where, LINT_SIZE.x, LINT_SIZE.y, int(round(scale * 100.0))]
		# Settled, frame-based: on a loaded runner 0.3 s can be one frame of a
		# resize's multi-frame sort cascade (PR #11 Quality gate).
		var problems: PackedStringArray = await DialogLayoutLint.settled_problems(dialog, t.get_tree())
		var confirm: Button = dialog.get_node("%ConfirmBtn")
		_check(problems.is_empty() and dialog.get_viewport_rect().encloses(confirm.get_global_rect()),
			"geo sjø: nothing overlaps, is cut or leaves the screen, the confirm on screen: %s %s" % [label, str(problems.slice(0, 4))])
		await t._lint_screen("geo sjø dialog " + label)
	win.content_scale_factor = old_scale
	win.size = old_size
	await t.get_tree().process_frame


# ── 1-3: the tutorial's route ────────────────────────────────────────────────

func _tutorial_route_builds_at_the_first_place(map: Control) -> void:
	var rows: Array = sim.get_sea_candidates(3)
	var dialog := await _open_home_build(map)
	if dialog == null:
		return
	var digest_start: String = sim.qa_state_digest()
	var list := dialog.candidate_list() as SeaCandidateList
	var shown_ids: Array = list.candidates.map(func(r: Dictionary) -> String: return str(r.get("id", "")))
	var named := shown_ids.all(func(id: String) -> bool: return list.row_button(id) != null and list.row_button(id).is_visible_in_tree())
	_check(dialog.visible and dialog.zone_id() == HOME_ZONE and list.is_visible_in_tree() and shown_ids == _ids(rows)
			and shown_ids.size() == 12 and named,
		"geo sjø: «Bygg lokalitet» opens on PO 3's 12 places by rank %s" % str(shown_ids.slice(0, 3)))
	_check(dialog.selected_candidate() == FIRST and bool(_row(rows, FIRST).get("suggested", false))
			and list.row_button(FIRST).button_pressed,
		"geo sjø: the first free place, %s, is preselected (the tutorial's route is unchanged)" % dialog.selected_candidate())
	var plan: Dictionary = dialog.plan()
	var zone_plan: Dictionary = sim.plan_site_build(HOME_ZONE)
	var same := true
	for key in zone_plan.keys():
		same = same and plan.get(key) == zone_plan[key]
	var ledger: Node = dialog.get_node("%Ledger")
	_check(same and plan == sim.plan_site_build_at(HOME_ZONE, FIRST) and str((plan.get("candidate", {}) as Dictionary).get("id", "")) == FIRST
			and (ledger.get_node("ValueCost") as Label).text == SimFormatters.format_nok(float(zone_plan.get("cost_nok", 0.0))),
		"geo sjø: the ledger is the zone's plan, every figure unchanged, at %s" % FIRST)
	_check((dialog.get_node("%TitleLabel") as Label).text == tr("SEA_CANDIDATE_BUILD_TITLE") % str(_row(rows, FIRST).get("label", "")),
		"geo sjø: the title names the place ('%s')" % (dialog.get_node("%TitleLabel") as Label).text)
	_why_equals_the_bridge(dialog.why_here(), _row(rows, FIRST), HOME_ZONE)
	# Choosing another place by a click follows it; choosing back; the engine is untouched.
	var third := list.row_button("PO03-03")
	await _scroll_to(dialog, third)
	await t._click_control(third)
	_check(dialog.selected_candidate() == "PO03-03" and str(dialog.why_here().shown.get("id", "")) == "PO03-03"
			and third.button_pressed and not list.row_button(FIRST).button_pressed,
		"geo sjø: a click on PO03-03 chooses it and the 'why here' follows")
	_why_equals_the_bridge(dialog.why_here(), _row(rows, "PO03-03"), HOME_ZONE)
	await t._click_control(list.row_button(FIRST))
	_check(sim.qa_state_digest() == digest_start and dialog.selected_candidate() == FIRST,
		"geo sjø: previewing and choosing places leave the engine state digest unchanged")
	await _lint_dialog(dialog, "the places")
	# 3. The confirm builds at the place.
	var before: int = sim.get_all_pens().size()
	await t._click_control(dialog.get_node("%ConfirmBtn"))
	var locations: Array = sim.get_player_site_locations()
	var built: Dictionary = locations[locations.size() - 1] if not locations.is_empty() else {}
	var first: Dictionary = _row(rows, FIRST)
	_check(sim.get_all_pens().size() == before + 1 and str(built.get("source", "")) == "candidate"
			and str(built.get("candidate_id", "")) == FIRST and int(built.get("x", 0)) == int(first.get("x", -1))
			and int(built.get("y", 0)) == int(first.get("y", -1)) and str(built.get("label", "")) == str(first.get("label", "")),
		"geo sjø: the confirm builds the second site at %s, and it lies there (%s)" % [FIRST, str(built)])
	_check(locations.size() == 2 and str(locations[0].get("source", "")) == "anchor" and str(locations[0].get("candidate_id", "-")) == "",
		"geo sjø: the starting site lies at its zone's anchor, marked approximate ('anchor')")
	_check((dialog.get_node("%ResultTitle") as Label).text == tr("SEA_CANDIDATE_DONE_TITLE") % [str(built.get("site_label", "")), str(first.get("label", ""))],
		"geo sjø: the dialog says where the site is built ('%s')" % (dialog.get_node("%ResultTitle") as Label).text)
	await t._click_control(dialog.get_node("%CancelBtn"))
	# Opened again: the place is held, the next free one is chosen.
	dialog = await _open_home_build(map)
	if dialog == null:
		return
	list = dialog.candidate_list() as SeaCandidateList
	var held := list.row_button(FIRST)
	_check(held != null and held.disabled and held.focus_mode == Control.FOCUS_NONE
			and held.tooltip_text == tr("SEA_CANDIDATE_TAKEN_TIP") % str(built.get("site_label", "")),
		"geo sjø: reopened, %s is disabled and its tooltip names the site ('%s')" % [FIRST, held.tooltip_text if held else ""])
	_check(dialog.selected_candidate() == "PO03-02", "geo sjø: the next free place, PO03-02, is preselected")
	dialog.close()


# ── 4: the no-zone picker, by keys alone ─────────────────────────────────────

func _the_picker_builds_by_keys_alone(map: Control) -> void:
	await t._click_control(map.new_farm_button())
	var dialog: BuildSiteDialog = map.build_dialog()
	await t.get_tree().process_frame
	var focus := func() -> Control: return t.get_viewport().gui_get_focus_owner()
	_check(dialog.visible and focus.call() == dialog.family_card("sjo").choose_button(),
		"geo sjø: «Nytt anlegg» opens on the family cards, the Sjø card focused")
	await t._press_key(KEY_ENTER)
	await t.get_tree().process_frame
	var list := dialog.candidate_list() as SeaCandidateList
	var rows: Array = sim.get_sea_candidates(3)
	var confirm: Button = dialog.get_node("%ConfirmBtn")
	var verdict: Label = dialog.get_node("%VerdictLabel")
	_check(dialog.family() == "sjo" and dialog.zone_id() == "" and list.is_visible_in_tree() and _ids(list.candidates) == _ids(rows)
			and dialog.selected_candidate() == "" and confirm.disabled and verdict.text == tr("SEA_CANDIDATE_PICK")
			and dialog.why_here().shown.is_empty(),
		"geo sjø: Enter on Sjø with no zone lists the home area's places, nothing chosen, the confirm waiting ('%s')" % verdict.text)
	_check(focus.call() == list.row_button("PO03-02"), "geo sjø: keyboard: the first free place has the focus")
	var digest: String = sim.qa_state_digest()
	await t._press_key(KEY_DOWN)
	_check(dialog.selected_candidate() == "PO03-03" and focus.call() == list.row_button("PO03-03")
			and dialog.zone_id() == str(_row(rows, "PO03-03").get("build_zone_id", "-")) and dialog.zone_id() == HOME_ZONE
			and str(dialog.why_here().shown.get("id", "")) == "PO03-03" and not confirm.disabled,
		"geo sjø: keyboard: Down chooses PO03-03; the build zone, the ledger and the 'why here' follow")
	await t._press_key(KEY_UP)
	await t._press_key(KEY_UP)
	_check(dialog.selected_candidate() == "PO03-02" and focus.call() == list.row_button("PO03-02"),
		"geo sjø: keyboard: Up skips nothing free and stops at the first free place (PO03-01 is held)")
	_check(sim.qa_state_digest() == digest, "geo sjø: keyboard browsing leaves the engine state digest unchanged")
	await t._press_key(KEY_ENTER)
	await t.get_tree().process_frame
	_check(focus.call() == confirm, "geo sjø: keyboard: Enter on the place hands on to the confirm")
	var before: int = sim.get_all_pens().size()
	await t._press_key(KEY_ENTER)
	var locations: Array = sim.get_player_site_locations()
	var last: Dictionary = locations[locations.size() - 1] if not locations.is_empty() else {}
	_check(sim.get_all_pens().size() == before + 1 and str(last.get("candidate_id", "")) == "PO03-02"
			and dialog.get_node("%ResultBox").visible,
		"geo sjø: keyboard: Enter on the confirm builds at PO03-02 (%d sites)" % sim.get_all_pens().size())
	await t._press_key(KEY_ESCAPE)
	_check(not dialog.visible, "geo sjø: keyboard: Esc closes the dialog")


# ── 5: the Hav lens ──────────────────────────────────────────────────────────

func _fact_values(lens: PlotLens) -> Dictionary:
	var out := {}
	var facts := lens.get_node("Lens/Facts")
	for i in range(0, facts.get_child_count() - 1, 2):
		out[(facts.get_child(i) as Label).text] = (facts.get_child(i + 1) as Label).text
	return out


func _the_hav_lens_names_the_open_sea_area(map: Control) -> void:
	var dialog: BuildSiteDialog = map.build_dialog()
	var lens := dialog.plot_lens()
	var plots: Array = sim.get_build_plots("hav")
	var geo: Dictionary = sim.get_geo_plot_binding("hav_1")
	lens.show_plots(plots, "hav_1", "of1", "")
	var facts := _fact_values(lens)
	_check(bool(geo.get("bound", false)) and str(geo.get("candidate_id", "")).begins_with("HTH")
			and facts.get(tr("GEO_PLOT_AREA"), "") == tr("GEO_PLOT_AREA_VALUE") % [str(geo.get("area", "")), int(geo.get("po", 0))]
			and facts.get(tr("GEO_PLOT_STAGE"), "") == tr("GEO_PLOT_STAGE_" + str(geo.get("stage", "")).to_upper()),
		"geo hav: hav_1's lens names its open-sea area and planning stage (%s)" % str(facts))
	_check(facts.get(tr("BUILD_PLOT_DISTANCE"), "") == tr("GEO_PLOT_DISTANCE_VALUE") % SimFormatters.num(float(geo.get("distance_km", 0.0)), 0)
			and facts.get(tr("BUILD_PLOT_DEPTH"), "") == SimFormatters.num(float(geo.get("depth_m", 0.0)), 0) + " m"
			and lens.geo_note().visible and lens.geo_note().text == tr("GEO_PLOT_CLIMATE_NOTE"),
		"geo hav: hav_1's lens gives the real distance and depth with the climate note")
	lens.show_plots(plots, "off_1", "of1", "")
	var off := _fact_values(lens)
	var off_1: Dictionary = {}
	for p in plots:
		if str(p.get("id", "")) == "off_1":
			off_1 = p
	_check(not bool(sim.get_geo_plot_binding("off_1").get("bound", true)) and not off.has(tr("GEO_PLOT_AREA"))
			and not off.has(tr("GEO_PLOT_STAGE")) and not lens.geo_note().visible
			and off.get(tr("BUILD_PLOT_DISTANCE"), "") == SimFormatters.num(float(off_1.get("real_distance_km", 0.0)), 1) + " km",
		"geo hav: off_1 has no geo rows and keeps its own distance")


# ── 6-8: the captain's review of track B ─────────────────────────────────────

## The newest of the company's site locations ({} none).
func _last_location() -> Dictionary:
	var locations: Array = sim.get_player_site_locations()
	return locations[locations.size() - 1] if not locations.is_empty() else {}


func _suggested_id(rows: Array) -> String:
	for r in rows:
		if bool(r.get("suggested", false)):
			return str(r.get("id", ""))
	return ""


## Finding 2: the build goes to the place the player chose, not to the
## suggestion (a click on a later place; keys that skip a held place).
## Finding 4: the picker's subtitle frames its list instead of pointing at the
## map, so the dialog gives one instruction.
func _a_chosen_place_is_where_the_build_goes(map: Control) -> void:
	_check(bool(t.get_node("/root/GameBridge").QaGrantCash(100_000_000.0)),
		"geo sjø: setup: a second QA grant pays for every place in PO 3")
	var rows: Array = sim.get_sea_candidates(3)
	var suggested := _suggested_id(rows)
	var dialog := await _open_home_build(map)
	if dialog == null:
		return
	var list := dialog.candidate_list() as SeaCandidateList
	var pick := "PO03-05"
	list.scroll().ensure_control_visible(list.row_button(pick))
	await _scroll_to(dialog, list.row_button(pick))
	await t._click_control(list.row_button(pick))
	var count: int = sim.get_player_site_locations().size()
	await t._click_control(dialog.get_node("%ConfirmBtn"))
	var built := _last_location()
	var row := _row(rows, pick)
	_check(suggested == "PO03-03" and sim.get_player_site_locations().size() == count + 1
			and str(built.get("candidate_id", "")) == pick and str(built.get("source", "")) == "candidate"
			and int(built.get("x", 0)) == int(row.get("x", -1)) and int(built.get("y", 0)) == int(row.get("y", -1))
			and (dialog.get_node("%ResultTitle") as Label).text == tr("SEA_CANDIDATE_DONE_TITLE") % [str(built.get("site_label", "")), str(row.get("label", ""))],
		"geo sjø: a click on %s, not the suggested %s, builds the site there (%s)" % [pick, suggested, str(built)])
	await t._click_control(dialog.get_node("%CancelBtn"))
	# Keys alone, from «Nytt anlegg»: the picker's first free place is PO03-03.
	await t._click_control(map.new_farm_button())
	dialog = map.build_dialog()
	await t.get_tree().process_frame
	await t._press_key(KEY_ENTER)
	await t.get_tree().process_frame
	var subtitle := (dialog.get_node("%SubtitleLabel") as Label).text
	_check(dialog.zone_id() == "" and subtitle == tr("SEA_CANDIDATE_SUBTITLE_PICK") % 3
			and subtitle != tr("BUILD_SUBTITLE_SEA_PICK_ZONE")
			and (dialog.get_node("%VerdictLabel") as Label).text == tr("SEA_CANDIDATE_PICK"),
		"geo sjø: the picker's subtitle frames its list ('%s'), one instruction with the verdict" % subtitle)
	list = dialog.candidate_list() as SeaCandidateList
	# The steps fit their content and scroll to the places a few frames on.
	for i in 4:
		await t.get_tree().process_frame
	var view := (dialog.get_node("%StepScroll") as Control).get_global_rect()
	var shown: Array = [dialog.get_node("%PlotHeader")]
	for id: String in ["PO03-01", "PO03-02", "PO03-03", "PO03-04"]:
		shown.append(list.row_button(id))
	_check(shown.all(func(c: Control) -> bool: return view.encloses(c.get_global_rect().grow(-1.0))),
		"geo sjø: «Nytt anlegg» → Sjø opens on the places: the list's head and first four rows lie in the steps' view (%s)" % str(view))
	await t._lint_screen("geo sjø picker")
	var focus := func() -> Control: return t.get_viewport().gui_get_focus_owner()
	await t._press_key(KEY_DOWN)
	await t._press_key(KEY_DOWN)
	var keyed := "PO03-06"
	_check(focus.call() == list.row_button(keyed) and dialog.selected_candidate() == keyed and dialog.zone_id() == HOME_ZONE,
		"geo sjø: keyboard: Down, Down from PO03-03 skips the held %s to %s" % [pick, dialog.selected_candidate()])
	await t._press_key(KEY_ENTER)
	await t.get_tree().process_frame
	count = sim.get_player_site_locations().size()
	await t._press_key(KEY_ENTER)
	built = _last_location()
	row = _row(rows, keyed)
	_check(sim.get_player_site_locations().size() == count + 1 and str(built.get("candidate_id", "")) == keyed
			and int(built.get("x", 0)) == int(row.get("x", -1)) and int(built.get("y", 0)) == int(row.get("y", -1)),
		"geo sjø: keyboard: Enter, Enter builds at %s, the place chosen, not the suggestion" % keyed)
	await t._press_key(KEY_ESCAPE)


## Finding 1: open_for_candidate (the map's 'Bygg her') opens on the place it
## is given: the zone follows a place given with no zone or a zone in another
## area; a held place or an id that names no open-pen place stays chosen, and
## the verdict is its check's text with the confirm waiting.
func _open_for_candidate_keeps_the_place_asked_for(map: Control) -> void:
	var dialog: BuildSiteDialog = map.build_dialog()
	var list := dialog.candidate_list() as SeaCandidateList
	var confirm: Button = dialog.get_node("%ConfirmBtn")
	var verdict: Label = dialog.get_node("%VerdictLabel")
	var digest: String = sim.qa_state_digest()
	var po3: Array = sim.get_sea_candidates(3)
	var po4: Array = sim.get_sea_candidates(4)
	var free3 := "PO03-07"
	var zone4 := str(_row(po4, "PO04-03").get("build_zone_id", ""))
	dialog.open_for_candidate(HOME_ZONE, free3)
	await t.get_tree().process_frame
	_check(dialog.zone_id() == HOME_ZONE and dialog.selected_candidate() == free3 and list.row_button(free3).button_pressed
			and str(dialog.why_here().shown.get("id", "")) == free3 and not confirm.disabled
			and (dialog.get_node("%TitleLabel") as Label).text == tr("SEA_CANDIDATE_BUILD_TITLE") % str(_row(po3, free3).get("label", "")),
		"geo sjø: open_for_candidate(%s, %s) opens on that place, ready to build" % [HOME_ZONE, free3])
	var opened: Array = []
	for zone: String in ["", HOME_ZONE]:
		dialog.open_for_candidate(zone, "PO04-03")
		await t.get_tree().process_frame
		opened.append([dialog.zone_id(), dialog.selected_candidate(), _ids(list.candidates) == _ids(po4),
			str(dialog.why_here().shown.get("id", ""))])
	var want := [zone4, "PO04-03", true, "PO04-03"]
	_check(zone4 != "" and zone4 != HOME_ZONE and opened == [want, want],
		"geo sjø: open_for_candidate with no zone or %s opens PO04-03 in its own zone %s %s" % [HOME_ZONE, zone4, str(opened)])
	dialog.open_for_candidate(zone4, free3)
	await t.get_tree().process_frame
	_check(dialog.zone_id() == HOME_ZONE and dialog.selected_candidate() == free3 and _ids(list.candidates) == _ids(po3),
		"geo sjø: open_for_candidate(%s, %s) follows the place back to %s" % [zone4, free3, dialog.zone_id()])
	dialog.open_for_candidate(HOME_ZONE, FIRST)
	await t.get_tree().process_frame
	var taken := tr("SEA_CANDIDATE_REASON_CANDIDATE_TAKEN")
	_check(dialog.selected_candidate() == FIRST and list.row_button(FIRST).disabled and list.row_button(FIRST).button_pressed
			and str(dialog.why_here().shown.get("id", "")) == FIRST and verdict.visible and verdict.text == taken
			and confirm.disabled and confirm.tooltip_text == taken,
		"geo sjø: open_for_candidate on the held %s keeps it and says why ('%s')" % [FIRST, verdict.text])
	dialog.open_for_candidate(HOME_ZONE, "HTH02-01")
	await t.get_tree().process_frame
	var missing := tr("SEA_CANDIDATE_REASON_CANDIDATE_NOT_FOUND")
	var none_pressed := _ids(list.candidates).all(func(id: String) -> bool: return not list.row_button(id).button_pressed)
	_check(dialog.selected_candidate() == "HTH02-01" and none_pressed and verdict.visible and verdict.text == missing
			and confirm.disabled,
		"geo sjø: open_for_candidate on an offshore id keeps it, builds nothing and says why ('%s')" % verdict.text)
	_check(sim.qa_state_digest() == digest, "geo sjø: opening the dialog at places leaves the engine state digest unchanged")
	dialog.close()


## Findings 3 and 5: with every place in the home area held, «Nytt anlegg» →
## Sjø is no dead end. It takes the home zone, the 'why here' says every place
## is the company's and the new locality has none of its own, and the confirm
## builds it there (anchored, approximate). The zone's own route says the same.
func _every_place_held(map: Control) -> void:
	var free: Array = sim.get_sea_candidates(3).filter(func(r: Dictionary) -> bool: return str(r.get("taken_by_site_id", "")) == "")
	var built := 0
	for r: Dictionary in free:
		if bool(sim.build_expansion_site_at(HOME_ZONE, str(r.get("id", ""))).get("success", false)):
			built += 1
	var held: bool = sim.get_sea_candidates(3).all(func(r: Dictionary) -> bool: return str(r.get("taken_by_site_id", "")) != "")
	_check(not free.is_empty() and built == free.size() and held, "geo sjø: setup: the other %d places in PO 3 are built" % built)
	await t._click_control(map.new_farm_button())
	var dialog: BuildSiteDialog = map.build_dialog()
	await t.get_tree().process_frame
	await t._press_key(KEY_ENTER)
	await t.get_tree().process_frame
	var list := dialog.candidate_list() as SeaCandidateList
	var confirm: Button = dialog.get_node("%ConfirmBtn")
	var why := dialog.why_here()
	var all_disabled := list.candidates.size() == 12 and _ids(list.candidates).all(func(id: String) -> bool: return list.row_button(id).disabled)
	_check(dialog.zone_id() == HOME_ZONE and dialog.selected_candidate() == "" and all_disabled and not confirm.disabled
			and not (dialog.get_node("%VerdictLabel") as Label).visible and t.get_viewport().gui_get_focus_owner() == confirm,
		"geo sjø: every place held: Enter on Sjø takes the home zone, the confirm ready and focused (zone '%s')" % dialog.zone_id())
	_check(why.all_held and why.hint_label().is_visible_in_tree()
			and why.hint_label().text == tr("SEA_CANDIDATE_WHY_ALL_HELD") % SimLabels.zone(HOME_ZONE),
		"geo sjø: every place held: the 'why here' says so and where the new locality goes ('%s')" % why.hint_label().text)
	await _lint_dialog(dialog, "every place held")
	confirm.grab_focus()
	var count: int = sim.get_player_site_locations().size()
	await t._press_key(KEY_ENTER)
	var last := _last_location()
	_check(sim.get_player_site_locations().size() == count + 1 and str(last.get("source", "")) == "anchor"
			and str(last.get("candidate_id", "-")) == "",
		"geo sjø: every place held: the confirm builds the zone's locality without a place, at the zone's anchor")
	await t._press_key(KEY_ESCAPE)
	dialog = await _open_home_build(map)
	if dialog == null:
		return
	_check(dialog.why_here().all_held and dialog.selected_candidate() == "" and not (dialog.get_node("%ConfirmBtn") as Button).disabled,
		"geo sjø: every place held: the zone's «Bygg lokalitet» says so too, its build ready")
	dialog.close()
