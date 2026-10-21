extends RefCounted
## Geo stages 2-3, Track D (docs/geo/STAGE23_PLAN.md §2 D): the map's region
## and site levels. UiFlowTests runs these through run(self) and counts them
## with its own _check. Headless: the shader's transform and colours are
## pinned on the CPU (GeoRegionMath against the raw grids), the layers by
## their recorded counts, the flows by real clicks and keys.
##
## 1. Math: GeoRegionMath.suit_uv lands every one of the 156 open-pen
##    candidates in its own scored cell (test_geodata's lookup); the shader's
##    uniforms are the same numbers; the inverse projection; the heat colours.
## 2. Lazy: with the map never opened, days pass with no geo build or read.
## 3. Navigation: a double-click drills into PO3; breadcrumbs; Esc steps up
##    one level and then closes the tab; the keyboard path ('Utforsk
##    området', Up/Down cycle the candidates, Enter opens a card, Esc back);
##    a crumb click returns to national; a repeat drill reads from cache.
## 4. Layers: PO3's and PO6's drawn overlay counts equal GeoRegionData's and
##    the pinned reference (tests/test_geo_map_contract.py recomputes it with
##    test_geodata's utm33); a ring that does not triangulate draws its
##    outline; no engine error while drilling.
## 5. Rivals: the drawn icons are get_geo_sites' owned PO3 localities in their
##    owner bands; RivalPresenceList's order is get_rival_summary(3)'s.
## 6. Stars and cards: every star and its button open a card whose score,
##    binding and components are get_sea_candidates' own.
## 7. Actions: 'Bygg her' -> the build dialog at the candidate -> built ->
##    back at region level the site is drawn; a PO4 candidate offers 'Søk
##    vekstlisens' -> the auction card for po04_solund; 'Vis i 3D' -> the
##    fjord tab with that site selected; the hav_1 hexagon -> the Hav family
##    with hav_1 chosen.
## 8. Live data: a quiet day re-reads no candidate; with the map closed no
##    geo read happens on day signals. The national chart's features, by
##    real double-clicks (the captain's ruling at the merge: a click names
##    the feature, a double-click opens its card): a candidate's, a
##    hexagon's, a company site's and a rival's card; a fjord or an offshore
##    area keeps the national level.
## 9. Lints: the region view and the site card at 1280x720, 100 % and 125 %:
##    layout, theme, contrast and Norwegian text; every button answers.
## 10. The captain's review checks (GeoRegionReviewChecks.gd).
##
## Tracks A and B are merged: every read goes through the map's one adapter
## (GeoBridgeAdapter), and the checks compare with the same adapter's reads.

const DialogLayoutLint := preload("res://tests/DialogLayoutLint.gd")
const AREA := 3
const OTHER_AREA := 6
const LICENCE_AREA := 4
const LICENCE_ZONE := "po04_solund"
const SEED := 91
const OPEN_PENS := 156
const LINT_SIZE := Vector2i(1280, 720)
const LINT_SCALES := [1.0, 1.25]
## GeoRegionData's features per layer in PO3 and PO6 (the base map's bounds):
## tests/test_geo_map_contract.py recomputes these with test_geodata's utm33.
const OVERLAY_COUNTS := {
	3: {"locality_areas": 292, "mooring_areas": 310, "fairways": 217, "exclusions": 78, "constraint_points": 264},
	6: {"locality_areas": 253, "mooring_areas": 206, "fairways": 140, "exclusions": 108, "constraint_points": 431},
}

var t: Node = null
var sim: Node = null
var bridge: GeoBridgeAdapter = null
var _log: _ErrorLog = null


func run(runner: Node) -> void:
	t = runner
	sim = t._sim
	bridge = GeoBridgeAdapter.new(sim)
	var started := Time.get_ticks_msec()
	_log = _ErrorLog.new()
	OS.add_logger(_log)
	_check(bridge.missing().is_empty(), "geo region: the map's adapter has every read the region and site levels use %s" % str(bridge.missing()))
	_math()
	_heat_colours()
	await _outline_fallback()
	var scene := await _lazy_until_opened()
	if scene != null:
		await _flows(scene)
		scene.queue_free()
		await t.get_tree().process_frame
	OS.remove_logger(_log)
	print("  geo D checks took %d ms" % (Time.get_ticks_msec() - started))


func _check(ok: bool, label: String) -> void:
	t._check(ok, label)


# ── 1. Math ───────────────────────────────────────────────────────────────────

func _math() -> void:
	var uv_bad := PackedStringArray()
	var cell_bad := PackedStringArray()
	var n := 0
	for s: Dictionary in GeoData.candidates():
		if s["type"] != "open_pen":
			continue
		n += 1
		var po: int = s["po"]
		var grid := GeoData.suitability_grid(po)
		var b: Array = grid["bounds_25833"]
		var reference := GeoProjection.to_cell(s["x"], s["y"], b, grid["res_m"])  # test_geodata's (x - ox) // 500
		var px := GeoRegionMath.map_px(po, s["x"], s["y"])
		var via_uv := GeoRegionMath.cell_of_uv(po, GeoRegionMath.suit_uv(po, px))
		var raw := GeoRegionMath.sample(po, via_uv)
		if via_uv != reference or raw.is_empty() or not bool(raw["legal"]):
			uv_bad.append(String(s["id"]))
		if GeoRegionMath.suit_cell(po, s["x"], s["y"]) != reference:
			cell_bad.append(String(s["id"]))
	_check(n == OPEN_PENS and uv_bad.is_empty(),
		"geo region: suit_uv puts all %d open-pen candidates' base-map pixels in their own scored cell, test_geodata's lookup %s" % [n, str(uv_bad.slice(0, 4))])
	_check(cell_bad.is_empty(), "geo region: suit_cell is test_geodata's floor((x - ox) / 500), floor((oy - y) / 500) for all 156 %s" % str(cell_bad.slice(0, 4)))

	var wrong := PackedStringArray()
	for po in range(1, 14):
		var u := GeoRegionMath.shader_uniforms(po)
		var tr_ := GeoRegionMath.transform(po)
		var origin: Array = tr_["grid_origin"]
		# The grid's north-west corner sits at uv (0, 0); one base-map pixel
		# east moves u by res / (500 W).
		var corner := GeoRegionMath.map_px(po, float(origin[0]), float(origin[1]))
		var at := corner * (u["suit_scale"] as Vector2) + (u["suit_offset"] as Vector2)
		var step := (u["suit_scale"] as Vector2).x * (tr_["grid_size"] as Vector2i).x * 500.0
		# The uniforms are 32-bit, as the shader gets them: 1e-6 relative.
		if at.length() > 1e-5 or absf(step / float(tr_["map_res"]) - 1.0) > 1e-6 or u["map_size"] != tr_["map_size"]:
			wrong.append(str(po))
	_check(wrong.is_empty(), "geo region: the shader's uniforms put each grid's north-west corner at uv (0, 0) and scale by res / 500, all 13 areas (wrong: %s)" % ", ".join(wrong))

	var points: Array = (GeoData.read_json("manifest.json") as Dictionary).get("projection_check_25833", [])
	var worst := 0.0
	for p: Dictionary in points:
		var ll := GeoRegionData.utm_to_lonlat(p["x"], p["y"])
		worst = maxf(worst, maxf(absf(ll[0] - float(p["lon"])), absf(ll[1] - float(p["lat"]))))
	_check(points.size() == 6 and worst < 1e-7,
		"geo region: utm_to_lonlat inverts the six reference points within 1e-7° (worst %s°)" % str(worst))


func _heat_colours() -> void:
	var low := GeoRegionMath.heat_color(1, 16)
	var high := GeoRegionMath.heat_color(255, 24)
	var mid := GeoRegionMath.heat_color(1 + int(round(0.85 * 254)), 19)
	var ok := low.is_equal_approx(Color(GeoRegionMath.RAMP_COLORS[0], GeoRegionMath.HEAT_ALPHA_LOW)) \
		and high.is_equal_approx(Color(GeoRegionMath.RAMP_COLORS[3], GeoRegionMath.HEAT_ALPHA_HIGH)) \
		and GeoRegionMath.ramp(0.85).is_equal_approx(GeoRegionMath.RAMP_COLORS[2]) \
		and GeoRegionMath.ramp(0.3) == GeoRegionMath.RAMP_COLORS[0] and mid.a > low.a and mid.a < high.a \
		and GeoRegionMath.heat_color(0, 4) == GeoRegionMath.HATCH_COLOR \
		and GeoRegionMath.heat_color(0, 12) == GeoRegionMath.HATCH_COLOR \
		and GeoRegionMath.heat_color(0, 13) == GeoRegionMath.HATCH_QUIET_COLOR \
		and GeoRegionMath.heat_color(0, 1).a == 0.0 and GeoRegionMath.heat_color(0, 2).a == 0.0 \
		and GeoRegionMath.heat_color(0, 3).a == 0.0 and GeoRegionMath.HATCH_QUIET_COLOR.a < GeoRegionMath.HATCH_COLOR.a
	_check(ok, "geo region: the heat ramps cool to gold over the score, hatches codes 4-12, code 13 quieter, land clear")


func _outline_fallback() -> void:
	# A bow-tie (self-crossing) ring: Geometry2D.triangulate_polygon gives up on it.
	var degenerate := PackedVector2Array([Vector2(0, 0), Vector2(10, 10), Vector2(10, 0), Vector2(0, 10)])
	var tris := GeoRegionData.fill_indices(degenerate)
	var overlay := GeoRegionOverlay.new()
	overlay.size = Vector2(200, 200)
	t.add_child(overlay)
	var area := {"po": 0, "map_size": Vector2(100, 100), "res": 250.0, "bounds": [], "counts": {}, "fallbacks": 1,
		"layers": {"exclusions": [{"id": 0, "kind": "salmon_fjord", "name": "", "parts": [{"ring": degenerate, "holes": [], "tris": tris}]}],
			"locality_areas": [], "mooring_areas": [], "fairways": [], "constraint_points": []}}
	_log.take()
	overlay.set_area(area)
	overlay.queue_redraw()
	await t.get_tree().process_frame
	var errors := _log.take()
	_check(tris.is_empty() and overlay.redraw_count >= 1 and overlay.outline_only == 1 and int(overlay.drawn.get("exclusions", 0)) == 1
			and errors.is_empty(),
		"geo region: a ring that does not triangulate is drawn as its outline, with no engine error %s" % str(errors.slice(0, 2)))
	overlay.queue_free()


# ── 2. Lazy until the map opens ───────────────────────────────────────────────

func _lazy_until_opened() -> Node:
	GeoRegionData.reset_cache()
	var calls := GeoBridgeAdapter.calls
	var scene: Node = await t._spawn_po3_scene(SEED)
	sim.advance_days(10)
	await t.get_tree().process_frame
	_check(GeoRegionData.build_count == 0 and GeoBridgeAdapter.calls == calls,
		"geo region: ten days with the map never opened build no area and make no geo read (%d builds, %d reads)" % [
			GeoRegionData.build_count, GeoBridgeAdapter.calls - calls])
	return scene


# ── 3-9. The flows ────────────────────────────────────────────────────────────

func _flows(scene: Node) -> void:
	var ui: Control = scene.get_node("UILayer/GameUI")
	await t._drain_cards(scene.get_node("%CriticalEventCard"))
	var map: Control = await t._open_map_by_key(ui)
	_check(map != null and map.is_visible_in_tree() and map.map_level() == "national" and map.home_area() == AREA,
		"geo region: Cmd+2 opens the map at national level with the home area PO3 (%s)" % (map.map_level() if map != null else "no map"))
	if map == null:
		return
	await _navigation(ui, map)
	await _layers(map)
	await _rivals(map)
	await _stars_and_cards(map)
	await _live_data(map)
	await _lints(map)
	await _national_features(map)
	await _actions(ui, map)
	await preload("res://tests/GeoRegionReviewChecks.gd").new().run(t, self, ui, map)
	await _hidden_map(ui, map)


func _navigation(ui: Control, map: Control) -> void:
	# A double-click on the PO3 marker drills into it.
	var parses := GeoData.parse_count
	await _double_click(map.region_marker(AREA))
	var drill: GeoDrillStack = map.geo_drill()
	_check(drill != null and map.map_level() == "region" and drill.area() == AREA and drill.region_view().is_visible_in_tree(),
		"geo region: a double-click on PO3's marker drills into PO3 (%s)" % map.map_level())
	if drill == null:
		return
	var first_build_ms := float(GeoRegionData.area(AREA).get("build_ms", 0.0))
	print("  geo D: first drill into PO3 built its layers in %.1f ms (%d files parsed)" % [first_build_ms, GeoData.parse_count - parses])
	_check(drill.breadcrumbs().path_text() == PackedStringArray([tr("MAP_REGION_CRUMB_NORWAY"), tr("MAP_REGION_AREA_NAME") % [AREA, "Karmøy til Sotra"]]),
		"geo region: the breadcrumbs read 'Norge › PO3 Karmøy til Sotra' (%s)" % " › ".join(drill.breadcrumbs().path_text()))
	await t._press_key(KEY_ESCAPE)
	_check(map.visible and map.map_level() == "national" and not drill.visible and ui.get_active_tab() == "map",
		"geo region: Esc at region level returns to national and keeps the tab (%s, %s)" % [map.map_level(), ui.get_active_tab()])
	await t._press_key(KEY_ESCAPE)
	await t.get_tree().process_frame
	_check(ui.get_active_tab() == "dashboard", "geo region: Esc at national closes the map to the dashboard (%s)" % ui.get_active_tab())

	# The keyboard path: Cmd+2, the explore button, the candidates, a card.
	map = await t._open_map_by_key(ui)
	var explore: Button = map.explore_button()
	_check(map.map_level() == "national" and explore != null and explore.is_visible_in_tree() and not explore.disabled,
		"geo region: the map reopens at national, never drilled in, with 'Utforsk området' on offer")
	var builds := GeoRegionData.build_count
	parses = GeoData.parse_count
	explore.grab_focus()
	await t._press_key(KEY_ENTER)
	await t.get_tree().process_frame
	_check(map.map_level() == "region" and GeoRegionData.build_count == builds and GeoData.parse_count == parses,
		"geo region: Enter on 'Utforsk området' drills in again, from cache (builds +%d, parses +%d)" % [
			GeoRegionData.build_count - builds, GeoData.parse_count - parses])
	var buttons := drill.detail().candidate_buttons()
	var focus := func() -> Control: return t.get_viewport().gui_get_focus_owner()
	var suggested := _suggested_id(drill.region_view().candidates())
	_check(buttons.size() == 12 and focus.call() == drill.detail().candidate_button(suggested),
		"geo region: the drill focuses the suggested candidate's button (%s)" % suggested)
	buttons[0].grab_focus()
	await t._press_key(KEY_UP)
	var wrapped_up: bool = focus.call() == buttons[buttons.size() - 1]
	await t._press_key(KEY_DOWN)
	var wrapped_down: bool = focus.call() == buttons[0]
	await t._press_key(KEY_DOWN)
	_check(wrapped_up and wrapped_down and focus.call() == buttons[1],
		"geo region: Up and Down cycle the 12 candidate buttons, the ends wrapping")
	await t._press_key(KEY_ENTER)
	await t.get_tree().process_frame
	var card := drill.site_card()
	_check(map.map_level() == "site" and card.visible and card.kind() == "candidate"
			and String(card.row().get("id", "")) == String(drill.region_view().candidates()[1]["id"]) and focus.call() == card.action_button(),
		"geo region: Enter opens the focused candidate's card, its action focused (%s)" % card.title_text())
	await t._press_key(KEY_ESCAPE)
	await t.get_tree().process_frame
	_check(map.map_level() == "region" and not card.visible and focus.call() == buttons[1],
		"geo region: Esc on the card steps back to the region, focus back on the candidate's button")
	# The crumb back to national.
	await t._click_control(drill.breadcrumbs().crumb("national"))
	_check(map.map_level() == "national" and map.visible, "geo region: a click on the 'Norge' crumb returns to national")
	_check(not map.drill_into(14) and not map.drill_into(0) and map.map_level() == "national",
		"geo region: drill_into refuses an area that does not exist")


func _layers(map: Control) -> void:
	var errors := _log.take()
	for po in [AREA, OTHER_AREA]:
		map.drill_into(po)
		var view := (map.geo_drill() as GeoDrillStack).region_view()
		view.overlay().queue_redraw()
		await t.get_tree().process_frame
		await t.get_tree().process_frame
		var data := GeoRegionData.area(po)
		var drawn: Dictionary = view.overlay().drawn
		_check(drawn == data["counts"] and data["counts"] == OVERLAY_COUNTS[po],
			"geo region: PO%d's drawn layers equal GeoRegionData's filtered counts and the reference %s" % [po, str(drawn)])
	errors.append_array(_log.take())
	_check(errors.is_empty(), "geo region: drilling into PO3 and PO6 logs no engine error %s" % str(errors.slice(0, 3)))
	map.drill_into(AREA)
	await t.get_tree().process_frame


func _rivals(map: Control) -> void:
	var view := (map.geo_drill() as GeoDrillStack).region_view()
	var sites := bridge.sites()
	var expected := {}
	var ids: PackedInt32Array = sites["ids"]
	var owners: Array = sites["owners"]
	for i in ids.size():
		if int(sites["po"][i]) == AREA and int(sites["owner"][i]) >= 0:
			var o: Dictionary = owners[sites["owner"][i]]
			expected[ids[i]] = CompetitorFarmVisualManager.band_color_for(String(o["id"])) if bool(o.get("is_major", false)) else GeoRegionIcons.INDEPENDENT
	var drawn := {}
	for icon: Dictionary in (view.rival_layer() as Object).get("icons"):
		drawn[icon["id"]] = icon["color"]
	_check(drawn == expected and not drawn.is_empty(),
		"geo region: the drawn rival icons are get_geo_sites' owned PO3 localities in their owner bands (%d)" % drawn.size())
	var summary := bridge.rival_summary(AREA)
	var want := PackedStringArray()
	for o: Dictionary in summary.get("top_owners", []):
		want.append(String(o.get("id", "")))
	var list := (map.geo_drill() as GeoDrillStack).detail().rivals()
	_check(list.owner_ids() == want and not want.is_empty() and list.is_visible_in_tree(),
		"geo region: RivalPresenceList shows get_rival_summary(3)'s owners in its order (%s)" % ", ".join(want))


func _stars_and_cards(map: Control) -> void:
	var drill: GeoDrillStack = map.geo_drill()
	var view := drill.region_view()
	var rows := bridge.sea_candidates(AREA)
	var order := PackedStringArray()
	for row: Dictionary in rows:
		order.append(String(row["id"]))
	var shown := PackedStringArray()
	for b in drill.detail().candidate_buttons():
		shown.append(String(b.name).trim_prefix("MapCandidate_"))
	var stars := 0
	for id in order:
		if view.star(id) != null and view.star(id).is_visible_in_tree():
			stars += 1
	_check(rows.size() == 12 and shown == order and stars == 12,
		"geo region: 12 stars and 12 buttons, in get_sea_candidates' rank order (%s)" % ", ".join(order))
	var star_bad := PackedStringArray()
	var button_bad := PackedStringArray()
	for row: Dictionary in rows:
		var id := String(row["id"])
		await t._click_control(view.star(id))
		if not _card_matches(drill.site_card(), row):
			star_bad.append(id)
		drill.step_up()
		var b := drill.detail().candidate_button(id)
		b.pressed.emit()
		await t.get_tree().process_frame
		if not _card_matches(drill.site_card(), row):
			button_bad.append(id)
		drill.step_up()
	_check(star_bad.is_empty(), "geo region: a click on each of the 12 stars opens a card with its score, binding and components %s" % str(star_bad))
	_check(button_bad.is_empty(), "geo region: each of the 12 buttons opens the same card %s" % str(button_bad))
	# The hover readout reads the CPU grid under the pointer.
	var first: Dictionary = rows[0]
	var at := view.to_view(GeoRegionMath.map_px(AREA, first["x"], first["y"])) + Vector2(GeoRegionView.STAR_PX, 0)
	var cell := GeoRegionMath.cell_of_uv(AREA, GeoRegionMath.suit_uv(AREA, view.to_map(at)))
	var raw := GeoRegionMath.sample(AREA, cell)
	var motion := InputEventMouseMotion.new()
	motion.position = view.get_global_transform_with_canvas() * at
	motion.global_position = motion.position
	t.get_viewport().push_input(motion, true)
	await t.get_tree().process_frame
	var want := view.readout_at(at)
	_check(not raw.is_empty() and view.readout().text == want and want.contains(tr(GeoRegionMath.reason_key(int(raw["reason"]))))
			and want.contains(SimFormatters.integer(int(raw["depth_m"]))),
		"geo region: the hover readout names the cell's score or reason and depth from the raw grid ('%s')" % view.readout().text)


## The card shows the row's score, binding and every component and its cost.
func _card_matches(card: GeoSiteCard, row: Dictionary) -> bool:
	if not card.visible or card.kind() != "candidate" or String(card.row().get("id", "")) != String(row["id"]):
		return false
	var facts := card.facts()
	if facts.get("score", "") != SimFormatters.num(float(row["score"]), 3):
		return false
	if facts.get("binding", "") != tr("MAP_SITE_TERM_" + String(row["binding"]).to_upper()):
		return false
	var components: Dictionary = row.get("components", {})
	var costs: Dictionary = row.get("component_costs", {})
	for term: String in components:
		var value := SimFormatters.num(float(components[term]), 2)
		var cost := float(costs.get(term, 0.0))
		var shown := String(facts.get("term_" + term, ""))
		if not shown.begins_with(value) or (cost >= 0.0005 and not shown.contains(SimFormatters.num(cost, 3))):
			return false
	return true


func _live_data(map: Control) -> void:
	var reads := int(GeoBridgeAdapter.reads.get("get_sea_candidates", 0))
	t.get_viewport().gui_release_focus()
	for i in 3:
		await t._run_one_day_by_key()
	_check(map.map_level() == "region" and int(GeoBridgeAdapter.reads.get("get_sea_candidates", 0)) == reads,
		"geo region: three quiet days with the region shown re-read no candidate (%d reads)" % (int(GeoBridgeAdapter.reads.get("get_sea_candidates", 0)) - reads))


func _lints(map: Control) -> void:
	var drill: GeoDrillStack = map.geo_drill()
	var win: Window = t.get_window()
	var old_size := win.size
	var old_scale := win.content_scale_factor
	var rows := drill.region_view().candidates()
	for scale: float in LINT_SCALES:
		win.size = LINT_SIZE
		win.content_scale_factor = scale
		await t.get_tree().create_timer(0.3).timeout
		var label := "%dx%d %d %%" % [LINT_SIZE.x, LINT_SIZE.y, int(round(scale * 100.0))]
		if map.map_level() == "site":
			drill.step_up()
		await t.get_tree().create_timer(0.25).timeout
		var problems := _region_layout_problems(drill)
		_check(problems.is_empty(), "geo region: the region view's layer card, readout and crumbs fit apart at %s %s" % [label, str(problems.slice(0, 3))])
		await t._lint_screen("map region " + label)
		drill.open_candidate(rows[0])
		await t.get_tree().create_timer(0.3).timeout
		problems = DialogLayoutLint.problems(drill.site_card())
		_check(problems.is_empty(), "geo region: the site card has nothing overlapping, cut or off screen at %s %s" % [label, str(problems.slice(0, 3))])
		await t._lint_screen("map site card " + label)
	win.content_scale_factor = old_scale
	win.size = old_size
	await t.get_tree().process_frame
	# Every disabled control says why; every enabled one answers silently.
	var silent := PackedStringArray()
	var presses := 0
	var warned := PackedStringArray()
	_log.take()
	for level in ["site", "region"]:
		if level == "region" and map.map_level() == "site":
			drill.step_up()
		for node in drill.find_children("*", "BaseButton", true, false):
			var b := node as BaseButton
			if not b.is_visible_in_tree():
				continue
			if b.disabled:
				if b.tooltip_text.strip_edges() == "":
					silent.append(String(b.name))
				continue
			if String(b.name) in ["ActionBtn", "BackBtn"] or String(b.name).begins_with("Crumb_"):
				continue  # they leave the level: their own checks press them
			if b.toggle_mode:
				b.button_pressed = not b.button_pressed
				b.button_pressed = not b.button_pressed
			else:
				b.pressed.emit()
			presses += 1
			await t.get_tree().process_frame
			warned.append_array(_log.take())
	for b in drill.detail().candidate_buttons():
		if b.disabled and b.tooltip_text.strip_edges() == "":
			silent.append(String(b.name))
	_check(silent.is_empty() and warned.is_empty() and presses >= 4,
		"geo region: every region and card control answers without a warning, every disabled one says why (%d presses) %s %s" % [presses, str(silent), str(warned.slice(0, 2))])
	if map.map_level() == "site":
		drill.step_up()


## The national chart's features open their cards by a real double-click
## (C2's GeoMapView: feature_drill_requested -> MapPanel, the captain's
## ruling at the merge): a candidate star, hav_1's hexagon, a company farm
## and a single rival locality each open their card in their own area; a
## click on a salmon fjord or an offshore area keeps the national level and
## names no card. Replaces D's direct _on_feature_clicked calls (a click
## opened the card) with real input, RUN_LOG §7.
func _national_features(map: Control) -> void:
	var drill: GeoDrillStack = map.geo_drill()
	var chart: GeoMapView = map.chart()
	var card := drill.site_card()
	drill.go_national()
	await t.get_tree().process_frame
	var star := _feature_point(chart, "candidates", func(item: Dictionary) -> bool: return int(item["po"]) == LICENCE_AREA)
	var star_id := String((star.get("item", {}) as Dictionary).get("best", {}).get("id", ""))
	await _double_click_point(star.get("at", Vector2.INF))
	_check(map.map_level() == "site" and drill.area() == LICENCE_AREA and card.kind() == "candidate" and star_id != ""
			and String(card.row().get("id", "")) == star_id and int(map.expansion_section().area().get("production_area", 0)) == LICENCE_AREA,
		"geo region: a double-click on PO4's national star opens its candidate's card in PO4, the area picked (%s, %s)" % [star_id, map.map_level()])
	drill.go_national()
	await t.get_tree().process_frame
	var hex := _feature_point(chart, "hexagon", func(item: Dictionary) -> bool: return bool(item.get("bound", false)))
	var hex_id := String((hex.get("item", {}) as Dictionary).get("row", {}).get("id", ""))
	await _double_click_point(hex.get("at", Vector2.INF))
	_check(map.map_level() == "site" and card.kind() == "offshore" and hex_id != "" and String(card.row().get("id", "")) == hex_id
			and card.action() == "hav",
		"geo region: a double-click on hav_1's national hexagon opens its card with 'Bygg her (hav)' (%s)" % hex_id)
	drill.go_national()
	await t.get_tree().process_frame
	var farm := _feature_point(chart, "player", func(_item: Dictionary) -> bool: return true)
	var site_id := String((farm.get("item", {}) as Dictionary).get("row", {}).get("site_id", ""))
	await _double_click_point(farm.get("at", Vector2.INF))
	_check(map.map_level() == "site" and card.kind() == "site" and site_id != "" and String(card.row().get("site_id", "")) == site_id
			and card.action() == "view_3d",
		"geo region: a double-click on a company farm on the national map opens its card with 'Vis i 3D' (%s)" % site_id)
	drill.go_national()
	await t.get_tree().process_frame
	# A rival's locality in a production area (21 owned localities lie in none, inland or offshore: no region shows them).
	var owned := _feature_point(chart, "site", func(item: Dictionary) -> bool:
		var i := int(item["index"])
		return chart.site_layer().owner_of(i) >= 0 and int((chart.site_layer().sites()["po"] as PackedByteArray)[i]) > 0)
	var locality := int(chart.feature_id(owned.get("item", {}))) if not owned.is_empty() else -1
	await _double_click_point(owned.get("at", Vector2.INF))
	_check(map.map_level() == "site" and card.kind() == "rival" and locality > 0 and int(card.row().get("id", -1)) == locality,
		"geo region: a double-click on a rival's single locality on the national map opens its card (locality %d)" % locality)
	drill.go_national()
	await t.get_tree().process_frame
	var fjord := _feature_point(chart, "fjord", func(_item: Dictionary) -> bool: return true)
	var sea := _feature_point(chart, "offshore_area", func(_item: Dictionary) -> bool: return true)
	await t._click_at(fjord.get("at", Vector2.INF))
	var after_fjord: bool = map.map_level() == "national" and not map.feature_button().visible
	await t._click_at(sea.get("at", Vector2.INF))
	_check(not fjord.is_empty() and not sea.is_empty() and after_fjord and map.map_level() == "national" and not drill.visible
			and not map.feature_button().visible,
		"geo region: a click on a salmon fjord or an offshore area keeps the national level and names no card")
	map.drill_into(AREA)
	await t.get_tree().process_frame


## A chart point where the pointer finds a feature of the kind that passes
## the test, clear of the plates, chips and key: {item, at (viewport)}, or
## {} when none does. Deterministic: features in the chart's own order.
func _feature_point(chart: GeoMapView, kind: String, wanted: Callable) -> Dictionary:
	var points: Array = []
	match kind:
		"candidates":
			for s: Dictionary in chart.candidate_layer().star_groups():
				points.append(s["pos"])
		"hexagon":
			for h: Dictionary in chart.candidate_layer().hexagons():
				points.append(h["pos"])
		"player":
			for q in chart.player_marks().points():
				points.append(q)
		"site":
			for c: Dictionary in chart.site_layer().clusters():
				if int(c["count"]) == 1:
					points.append(c["pos"])
		"fjord":
			for f: Dictionary in chart.overlay_layer().fjords():
				points.append(_inside_point(f["ring"]))
		"offshore_area":
			for o: Dictionary in chart.overlay_layer().get("_offshore_local"):
				points.append(_inside_point(o["ring"]))
	var origin := chart.get_global_rect().position
	for q: Vector2 in points:
		if not q.is_finite() or not Rect2(Vector2.ZERO, chart.size).has_point(q) or _covered(chart, q):
			continue
		var item := chart.feature_at(q)
		if str(item.get("kind", "")) == kind and wanted.call(item):
			return {"item": item, "at": origin + q}
	return {}


## A point inside a ring: the middle of the longest horizontal run through
## the ring's centre row of its bounds.
static func _inside_point(ring: PackedVector2Array) -> Vector2:
	var box := Rect2(ring[0], Vector2.ZERO)
	for q in ring:
		box = box.expand(q)
	for f in [0.5, 0.4, 0.6, 0.3, 0.7]:
		var y: float = box.position.y + box.size.y * float(f)
		var xs: Array[float] = []
		for i in ring.size():
			var a := ring[i]
			var b := ring[(i + 1) % ring.size()]
			if (a.y <= y) != (b.y <= y):
				xs.append(a.x + (y - a.y) / (b.y - a.y) * (b.x - a.x))
		xs.sort()
		var best := Vector2.INF
		var best_w := 0.0
		for i in range(0, xs.size() - 1, 2):
			if xs[i + 1] - xs[i] > best_w:
				best_w = xs[i + 1] - xs[i]
				best = Vector2((xs[i] + xs[i + 1]) * 0.5, y)
		if best_w >= 4.0:
			return best
	return Vector2.INF


## A chart point under a plate, a layer chip or the key (they take the click).
static func _covered(chart: GeoMapView, q: Vector2) -> bool:
	for plate: MapMarker in chart.plates().values():
		if plate.get_rect().grow(4.0).has_point(q):
			return true
	var at := chart.get_global_rect().position + q
	for chip in chart.chips():
		if chip.is_visible_in_tree() and chip.get_global_rect().grow(4.0).has_point(at):
			return true
	var key := chart.legend()
	return key != null and key.is_visible_in_tree() and key.get_global_rect().grow(4.0).has_point(at)


## The region view's own overlays (crumbs, chips, legend, readout) stay inside
## the view, apart, with their text whole.
func _region_layout_problems(drill: GeoDrillStack) -> PackedStringArray:
	var out := PackedStringArray()
	var view := drill.region_view()
	var box := view.get_global_rect().grow(0.5)
	var parts: Array[Control] = [drill.breadcrumbs(), view.get_node("ChipsCard"), view.get_node("ReadoutCard")]
	for i in parts.size():
		var r := parts[i].get_global_rect()
		if not box.encloses(r):
			out.append("%s leaves the map" % parts[i].name)
		for j in range(i + 1, parts.size()):
			if r.grow(-0.5).intersects(parts[j].get_global_rect().grow(-0.5)):
				out.append("%s × %s" % [parts[i].name, parts[j].name])
	for node in view.find_children("*", "Label", true, false) + drill.breadcrumbs().find_children("*", "Label", true, false):
		var l := node as Label
		if l.is_visible_in_tree() and l.autowrap_mode == TextServer.AUTOWRAP_OFF and not l.clip_text \
				and l.get_minimum_size().x > l.size.x + 1.0:
			out.append("%s text cut" % l.name)
	return out


func _actions(ui: Control, map: Control) -> void:
	var drill: GeoDrillStack = map.geo_drill()
	var view := drill.region_view()
	# 'Bygg her' on the suggested candidate.
	var rows := view.candidates()
	var row: Dictionary = {}
	for r: Dictionary in rows:
		if bool(r.get("suggested", false)):
			row = r
	var sites_before: int = sim.get_all_pens().size()
	if row.is_empty():
		_check(false, "geo region: PO3 has a suggested candidate to build at")
		_check(false, "geo region: (no build)")
		_check(false, "geo region: (no build)")
	else:
		drill.open_candidate(row)
		await t.get_tree().process_frame
		var card := drill.site_card()
		var action_ok := card.action() == "build" and not card.action_button().disabled
		await t._click_control(card.action_button())
		var dialog: BuildSiteDialog = map.build_dialog()
		_check(action_ok and dialog.visible and dialog.zone_id() == String(row["build_zone_id"])
				and dialog.selected_candidate() == String(row["id"]),
			"geo region: 'Bygg her' opens the build dialog in zone %s with the candidate %s preselected" % [dialog.zone_id(), String(row["id"])])
		await t._click_control(dialog.get_node("%ConfirmBtn"))
		var built: bool = sim.get_all_pens().size() == sites_before + 1
		await t._click_control(dialog.get_node("%CancelBtn"))
		await t.get_tree().process_frame
		_check(built and not dialog.visible and map.map_level() == "site",
			"geo region: confirming builds the site and closing the dialog returns to the card (%d sites)" % sim.get_all_pens().size())
		drill.step_up()
		await t.get_tree().process_frame
		var locations := bridge.player_site_locations()
		var newest: Dictionary = {}
		for r: Dictionary in locations:
			if String(r.get("candidate_id", "")) == String(row["id"]):
				newest = r
		var marker := view.site_marker(String(newest.get("site_id", "")))
		var want_at := view.to_view(GeoRegionMath.map_px(AREA, float(newest.get("x", 0)), float(newest.get("y", 0))))
		var at_xy: bool = marker != null and (marker.get_meta(&"anchor", Vector2.INF) as Vector2).distance_to(want_at) < 0.5
		var fresh: Dictionary = {}
		for r: Dictionary in view.candidates():
			if r["id"] == row["id"]:
				fresh = r
		_check(view.state_of(fresh) == "yours" and String(newest.get("source", "")) == "candidate" and at_xy
				and int(newest.get("x", 0)) == int(row["x"]) and int(newest.get("y", 0)) == int(row["y"]),
			"geo region: back at region level the candidate is yours and the new site is drawn at its x/y (%s)" % String(newest.get("site_id", "")))
	# 'Søk vekstlisens' on a PO4 candidate in this PO3 game.
	map.drill_into(LICENCE_AREA)
	await t.get_tree().process_frame
	var po4: Array = drill.region_view().candidates()
	drill.open_candidate(po4[0])
	await t.get_tree().process_frame
	var card := drill.site_card()
	_check(card.action() == "licence" and card.subtitle_text().contains("Solund") and String(po4[0]["build_zone_id"]) == LICENCE_ZONE,
		"geo region: a PO4 candidate offers 'Søk vekstlisens' and names the zone's licence ('%s')" % card.subtitle_text())
	await t._click_control(card.action_button())
	var auction: LicenceAuctionCard = map.auction_card()
	_check(auction.visible and auction.zone_id() == LICENCE_ZONE, "geo region: 'Søk vekstlisens' opens the auction card for po04_solund (%s)" % auction.zone_id())
	auction.close()
	await t.get_tree().process_frame
	# 'Bygg her (hav)' on the hexagon hav_1 is bound to.
	var bound: Dictionary = {}
	for r: Dictionary in bridge.offshore_candidates():
		if String(r.get("bound_plot_id", "")) != "":
			bound = r
	map.drill_into(int(bound.get("po", AREA)))
	await t.get_tree().process_frame
	var hexes := drill.region_view().offshore()
	var others_say_no := true
	for r: Dictionary in hexes:
		if String(r.get("bound_plot_id", "")) == "":
			drill.open_offshore(r)
			others_say_no = others_say_no and drill.site_card().action() == "" and drill.site_card().get_node("%Dialog").find_child("Note", true, false).text == tr("MAP_REGION_HEX_NOT_OFFERED")
			drill.step_up()
	var hex := drill.region_view().hexagon(String(bound.get("id", "")))
	await t._click_control(hex)
	var hav_ok := drill.site_card().action() == "hav"
	await t._click_control(drill.site_card().action_button())
	var dialog: BuildSiteDialog = map.build_dialog()
	_check(hav_ok and others_say_no and dialog.visible and dialog.family() == "hav" and dialog.plot_id() == "hav_1",
		"geo region: the hav_1 hexagon (%s) opens the Hav family with hav_1 chosen; the other %d say they are not offered" % [
			String(bound.get("id", "")), hexes.size() - 1])
	dialog.close()
	await t.get_tree().process_frame
	# 'Vis i 3D' on a company site.
	map.drill_into(AREA)
	await t.get_tree().process_frame
	var sites := drill.region_view().player_sites()
	# Every company site has a mark of its own (spread apart where sites
	# share a spot); the first is clicked.
	var site_id := String(sites[0].get("site_id", "")) if not sites.is_empty() else ""
	await t._click_control(drill.region_view().site_marker(site_id))
	var view_3d := drill.site_card().action() == "view_3d"
	await t._click_control(drill.site_card().action_button())
	for i in 4:
		await t.get_tree().process_frame
	var vista: Node = ui.owner.get_node_or_null("%FjordVistaBridge") if ui.owner != null else null
	_check(view_3d and ui.get_active_tab() == "fjord" and vista != null and String(vista.get("selected_pen_id")) == site_id,
		"geo region: 'Vis i 3D' on a company site shows the fjord with that site selected (%s)" % site_id)


func _hidden_map(ui: Control, map: Control) -> void:
	_check(not map.visible and map.map_level() == "national",
		"geo region: leaving the map returns it to national (%s)" % map.map_level())
	var calls := GeoBridgeAdapter.calls
	sim.advance_days(5)
	await t.get_tree().process_frame
	_check(GeoBridgeAdapter.calls == calls, "geo region: five days with the map closed make no geo read (%d)" % (GeoBridgeAdapter.calls - calls))


# ── Helpers ───────────────────────────────────────────────────────────────────

func _suggested_id(rows: Array) -> String:
	for r: Dictionary in rows:
		if bool(r.get("suggested", false)):
			return String(r["id"])
	return String(rows[0]["id"]) if not rows.is_empty() else ""


## A real double-click at a control's centre (press, release, press with
## double_click, release).
func _double_click(control: Control) -> void:
	if control == null:
		return
	await _double_click_point(control.get_global_rect().get_center())


## A real double-click at a viewport position (nothing for Vector2.INF).
func _double_click_point(pos: Vector2) -> void:
	if not pos.is_finite():
		return
	var vp := t.get_viewport()
	vp.notification(Node.NOTIFICATION_VP_MOUSE_ENTER)
	for step in [[true, false], [false, false], [true, true], [false, false]]:
		var ev := InputEventMouseButton.new()
		ev.position = pos
		ev.global_position = pos
		ev.button_index = MOUSE_BUTTON_LEFT
		ev.pressed = step[0]
		ev.double_click = step[1]
		vp.push_input(ev, true)
	await t.get_tree().physics_frame
	await t.get_tree().process_frame
	await t.get_tree().process_frame


## Engine errors and warnings logged while the checks run.
class _ErrorLog extends Logger:
	var _lines: PackedStringArray = []
	var _mutex := Mutex.new()

	func _log_error(function: String, file: String, line: int, code: String, rationale: String,
			_editor_notify: bool, error_type: int, _script_backtraces: Array[ScriptBacktrace]) -> void:
		var kind := "warning" if error_type == ERROR_TYPE_WARNING else "error"
		_mutex.lock()
		_lines.append("%s: %s (%s:%d %s)" % [kind, rationale if rationale != "" else code, file.get_file(), line, function])
		_mutex.unlock()

	func take() -> PackedStringArray:
		_mutex.lock()
		var out := _lines.duplicate()
		_lines.clear()
		_mutex.unlock()
		return out
