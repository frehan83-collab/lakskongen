extends RefCounted
## Geo stages 2-3, the captain's D merge (docs/geo/STAGE23_PLAN.md §4 step 3):
## the review of geo/map-region's findings, pinned in the running game, and
## the national -> region -> site -> Esc path by input alone. GeoRegionChecks
## runs these after its own flows, with the map on the fjord tab's side.
## 1. The path by input only: Cmd+2, a double-click on PO3's plate, a click
##    on a star, Esc, Esc, Enter on 'Utforsk området', Down, Enter, Esc to
##    the dashboard; every level fully faded in (motion on), the focus where
##    the keyboard needs it.
## 2. A click on a national feature names it in the detail column; Enter on
##    that button opens its card; another area picked forgets it.
## 3. The crossfade: a card opened while the region still fades in, then
##    Esc, leaves every level opaque (review finding 1).
## 4. The overlay counts what it emitted, per style and kind, its triangles
##    and its segments, against GeoRegionData (finding 3).
## 5. All 13 areas' layer counts equal the reference (finding 6: the
##    prefilter grows each feature by its buffer or radius); no fill covers
##    a hole of a salmon fjord or a protected area (the captain's own find).
## 6. The keyboard reaches the company's sites and hav_1 from the detail
##    column, and Esc from any card puts the focus back (finding 7).
## 7. A rival's card names the locality by its number only (finding 5).
## 8. A build through the bridge, then one day by key: the region and the
##    open card follow the sim's signals, no dialog involved (finding 4).
## 9. Company sites on one spot are spread apart, each answering its own
##    click (finding 8).
## 10. The card's crop draws its heat as one shaded rect (finding 9).

## Long enough for a 160 ms crossfade under load.
const FADE_WAIT_S := 0.45
const AREA := 3
const OTHER_AREA := 6
const LICENCE_AREA := 4
## GeoRegionData's features per layer in every area (the base map's bounds,
## a fairway's buffer and a point's radius counted as its extent):
## tests/test_geo_map_contract.py recomputes these with test_geodata's utm33.
const ALL_OVERLAY_COUNTS := {
	1: {"locality_areas": 167, "mooring_areas": 154, "fairways": 305, "exclusions": 259, "constraint_points": 327},
	2: {"locality_areas": 158, "mooring_areas": 166, "fairways": 164, "exclusions": 62, "constraint_points": 257},
	3: {"locality_areas": 292, "mooring_areas": 310, "fairways": 217, "exclusions": 78, "constraint_points": 264},
	4: {"locality_areas": 233, "mooring_areas": 221, "fairways": 234, "exclusions": 136, "constraint_points": 119},
	5: {"locality_areas": 133, "mooring_areas": 119, "fairways": 170, "exclusions": 117, "constraint_points": 187},
	6: {"locality_areas": 253, "mooring_areas": 206, "fairways": 140, "exclusions": 108, "constraint_points": 431},
	7: {"locality_areas": 175, "mooring_areas": 140, "fairways": 66, "exclusions": 52, "constraint_points": 156},
	8: {"locality_areas": 192, "mooring_areas": 169, "fairways": 104, "exclusions": 58, "constraint_points": 102},
	9: {"locality_areas": 241, "mooring_areas": 265, "fairways": 148, "exclusions": 97, "constraint_points": 565},
	10: {"locality_areas": 133, "mooring_areas": 155, "fairways": 101, "exclusions": 44, "constraint_points": 91},
	11: {"locality_areas": 117, "mooring_areas": 138, "fairways": 113, "exclusions": 48, "constraint_points": 133},
	12: {"locality_areas": 102, "mooring_areas": 119, "fairways": 97, "exclusions": 37, "constraint_points": 97},
	13: {"locality_areas": 15, "mooring_areas": 20, "fairways": 34, "exclusions": 18, "constraint_points": 21},
}

var t: Node = null
## GeoRegionChecks (its input helpers).
var g: RefCounted = null
var sim: Node = null
var bridge: GeoBridgeAdapter = null


func run(runner: Node, region_checks: RefCounted, ui: Control, map: Control) -> void:
	t = runner
	g = region_checks
	sim = t._sim
	bridge = GeoBridgeAdapter.new(sim)
	var motion: int = MotionPrefs._forced
	MotionPrefs.force(false)
	map = await _input_only_path(ui)
	if map == null:
		MotionPrefs._forced = motion
		return
	await _named_feature(map)
	await _crossfade_mid_fade(map)
	MotionPrefs._forced = motion
	await _overlay_emitted(map)
	_all_area_counts()
	_holes_stay_open()
	await _keyboard_rows_and_focus(map)
	await _day_refresh(map)
	await _spread_sites()
	await _crop_heat(map)
	# Leave the map as GeoRegionChecks' last check expects it: closed.
	(map.geo_drill() as GeoDrillStack).go_national()
	await t._press_key(KEY_ESCAPE)
	await t.get_tree().process_frame


func _check(ok: bool, label: String) -> void:
	t._check(ok, label)


func _wait(seconds: float) -> void:
	await t.get_tree().create_timer(seconds).timeout


func _focus() -> Control:
	return t.get_viewport().gui_get_focus_owner()


## The level's three faded nodes are opaque (the stack, the region, the card).
static func _opaque(drill: GeoDrillStack) -> bool:
	return is_equal_approx(drill.modulate.a, 1.0) and is_equal_approx(drill.region_view().modulate.a, 1.0) \
		and is_equal_approx(drill.site_card().modulate.a, 1.0)


static func _suggested(rows: Array) -> String:
	for r: Dictionary in rows:
		if bool(r.get("suggested", false)):
			return String(r["id"])
	return String(rows[0]["id"]) if not rows.is_empty() else ""


# ── 1. The path by input only ─────────────────────────────────────────────────

func _input_only_path(ui: Control) -> Control:
	var map: Control = await t._open_map_by_key(ui)
	_check(map != null and map.is_visible_in_tree() and map.map_level() == "national",
		"geo region review: Cmd+2 opens the map at national level (%s)" % (map.map_level() if map != null else "no map"))
	if map == null:
		return null
	await g._double_click(map.region_marker(AREA))
	await _wait(FADE_WAIT_S)
	var drill: GeoDrillStack = map.geo_drill()
	var view := drill.region_view()
	var suggested := _suggested(view.candidates())
	_check(map.map_level() == "region" and drill.area() == AREA and _opaque(drill) and drill.is_visible_in_tree()
			and _focus() == drill.detail().candidate_button(suggested),
		"geo region review: a double-click on PO3's plate shows the region, fully faded in, the suggested candidate's button focused (%s, alpha %.2f)" % [
			map.map_level(), drill.modulate.a])
	var star := view.star(suggested)
	await t._click_at(star.get_global_rect().get_center() if star != null else Vector2.INF)
	await _wait(FADE_WAIT_S)
	var card := drill.site_card()
	var action := card.action_button()
	var focus_to: Control = action if action.visible and not action.disabled else card.back_button()
	_check(map.map_level() == "site" and card.kind() == "candidate" and String(card.row().get("id", "")) == suggested
			and _opaque(drill) and card.is_visible_in_tree() and _focus() == focus_to,
		"geo region review: a click on the star opens its card, every level opaque, its action (or Back, when refused) focused (%s, %s, card alpha %.2f)" % [
			suggested, focus_to.name, card.modulate.a])
	await t._press_key(KEY_ESCAPE)
	await _wait(FADE_WAIT_S)
	_check(map.map_level() == "region" and not card.visible and _opaque(drill) and _focus() == drill.detail().candidate_button(suggested),
		"geo region review: Esc steps back to the region, opaque, the focus on the candidate's button")
	await t._press_key(KEY_ESCAPE)
	await t.get_tree().process_frame
	await t.get_tree().process_frame
	_check(map.map_level() == "national" and not drill.visible and map.is_visible_in_tree() and _focus() == map.explore_button(),
		"geo region review: Esc at the region returns to national, the focus on 'Utforsk området' (%s)" % (_focus().name if _focus() != null else "none"))
	await t._press_key(KEY_ENTER)
	await _wait(FADE_WAIT_S)
	var buttons := drill.detail().candidate_buttons()
	var at := buttons.find(drill.detail().candidate_button(suggested))
	var next: Button = buttons[(at + 1) % buttons.size()] if at >= 0 else null
	await t._press_key(KEY_DOWN)
	var down_ok: bool = next != null and _focus() == next
	await t._press_key(KEY_ENTER)
	await _wait(FADE_WAIT_S)
	_check(down_ok and map.map_level() == "site" and next != null
			and "MapCandidate_" + String(card.row().get("id", "")) == String(next.name) and _opaque(drill),
		"geo region review: Enter on 'Utforsk området' drills in again, Down moves to the next candidate, Enter opens its card (%s)" % (
			String(next.name) if next != null else "none"))
	for i in 3:
		await t._press_key(KEY_ESCAPE)
		await t.get_tree().process_frame
	await t.get_tree().process_frame
	_check(ui.get_active_tab() == "dashboard" and map.map_level() == "national",
		"geo region review: Esc, Esc, Esc steps site -> region -> national -> the dashboard (%s)" % ui.get_active_tab())
	map = await t._open_map_by_key(ui)
	_check(map.map_level() == "national" and map.is_visible_in_tree(), "geo region review: Cmd+2 reopens the map at national level (OD11)")
	return map


# ── 2. A click names the feature ──────────────────────────────────────────────

func _named_feature(map: Control) -> void:
	var drill: GeoDrillStack = map.geo_drill()
	var chart: GeoMapView = map.chart()
	var star: Dictionary = g._feature_point(chart, "candidates", func(item: Dictionary) -> bool: return int(item["po"]) == LICENCE_AREA)
	var best: Dictionary = (star.get("item", {}) as Dictionary).get("best", {})
	await t._click_at(star.get("at", Vector2.INF))
	var label := TranslationServer.translate("MAP_REGION_PICK_CANDIDATE") % [int(best.get("rank", 0)), String(best.get("label", ""))]
	var button: Button = map.feature_button()
	_check(map.map_level() == "national" and int(map.expansion_section().area().get("production_area", 0)) == LICENCE_AREA
			and button != null and button.is_visible_in_tree() and button.text == TranslationServer.translate("MAP_REGION_PICK_OPEN") % label
			and button.tooltip_text != "",
		"geo region review: a click on PO4's star keeps the national level, picks PO4 and names it ('%s')" % (button.text if button != null else ""))
	if button != null:
		button.grab_focus()
	await t._press_key(KEY_ENTER)
	await t.get_tree().process_frame
	_check(map.map_level() == "site" and drill.site_card().kind() == "candidate"
			and String(drill.site_card().row().get("id", "")) == String(best.get("id", "")),
		"geo region review: Enter on the named feature opens its card (%s)" % String(best.get("id", "")))
	drill.go_national()
	await t.get_tree().process_frame
	await t._click_at(star.get("at", Vector2.INF))
	var named: bool = button != null and button.is_visible_in_tree()
	await t._click_control(map.region_marker(OTHER_AREA))
	_check(named and not button.is_visible_in_tree() and int(map.expansion_section().area().get("production_area", 0)) == OTHER_AREA,
		"geo region review: picking another area (its plate) forgets the named feature")


# ── 3. The crossfade ──────────────────────────────────────────────────────────

func _crossfade_mid_fade(map: Control) -> void:
	var drill: GeoDrillStack = map.geo_drill()
	drill.go_national()
	await t.get_tree().process_frame
	# The drill and the card in one frame, as the national feature path does:
	# the stack's fade has just begun (alpha 0) when the card's begins.
	map.drill_into(AREA)
	drill.open_candidate(drill.region_view().candidates()[0])
	var mid: float = drill.modulate.a
	await t.get_tree().process_frame
	drill.step_up()
	await _wait(FADE_WAIT_S)
	_check(mid < 1.0 and map.map_level() == "region" and _opaque(drill),
		"geo region review: a card opened while the region fades in, then Esc, leaves every level opaque (stack %.2f, region %.2f, card %.2f; mid-fade %.2f)" % [
			drill.modulate.a, drill.region_view().modulate.a, drill.site_card().modulate.a, mid])
	# The national chart's own double-click on a feature: drill and card in one frame.
	drill.go_national()
	await t.get_tree().process_frame
	var star: Dictionary = g._feature_point(map.chart(), "candidates", func(item: Dictionary) -> bool: return int(item["po"]) == OTHER_AREA)
	await g._double_click_point(star.get("at", Vector2.INF))
	await _wait(FADE_WAIT_S)
	_check(map.map_level() == "site" and drill.area() == OTHER_AREA and _opaque(drill) and drill.site_card().is_visible_in_tree(),
		"geo region review: a double-click on a national star opens its card in one frame, every level opaque (stack %.2f)" % drill.modulate.a)
	drill.go_national()


# ── 4. What the overlay emitted ───────────────────────────────────────────────

func _overlay_emitted(map: Control) -> void:
	var drill: GeoDrillStack = map.geo_drill()
	for po in [AREA, OTHER_AREA]:
		map.drill_into(po)
		var overlay := drill.region_view().overlay()
		overlay.queue_redraw()
		await t.get_tree().process_frame
		await t.get_tree().process_frame
		var want := expected_emission(GeoRegionData.area(po))
		_check(overlay.drawn == GeoRegionData.area(po)["counts"] and overlay.drawn_kinds == want["kinds"]
				and overlay.drawn_triangles == want["triangles"] and overlay.drawn_segments == want["segments"]
				and not (want["triangles"] as Dictionary).is_empty(),
			"geo region review: PO%d's overlay emitted every feature per style and kind, with GeoRegionData's triangles and segments %s %s" % [
				po, str(overlay.drawn_kinds), str(overlay.drawn_triangles)])


## What a recording of an area's layers must emit, from GeoRegionData's rows:
## {kinds, triangles, segments}, counted the overlay's way (a polygon by its
## style, a fairway and a point by kind; positive counts only).
static func expected_emission(area: Dictionary) -> Dictionary:
	var kinds := {}
	var triangles := {}
	var segments := {}
	var layers: Dictionary = area["layers"]
	for layer: String in ["exclusions", "locality_areas", "mooring_areas"]:
		for row: Dictionary in layers[layer]:
			var style: String = row["kind"] if layer == "exclusions" else layer
			if not GeoRegionOverlay.STYLE.has(style):
				style = "protected_area"
			var filled := (GeoRegionOverlay.STYLE[style]["fill"] as Color).a > 0.0
			var out := false
			for part: Dictionary in row["parts"]:
				for ring: PackedVector2Array in [part["ring"]] + Array(part["holes"]):
					if ring.size() >= 2:
						segments[style] = int(segments.get(style, 0)) + ring.size()
						out = true
				var tris: PackedInt32Array = part["tris"]
				if filled and not tris.is_empty():
					triangles[style] = int(triangles.get(style, 0)) + tris.size() / 3
					out = true
			if out:
				kinds[style] = int(kinds.get(style, 0)) + 1
	for row: Dictionary in layers["fairways"]:
		if (row["line"] as PackedVector2Array).size() >= 2:
			kinds[row["kind"]] = int(kinds.get(row["kind"], 0)) + 1
	for row: Dictionary in layers["constraint_points"]:
		kinds[row["kind"]] = int(kinds.get(row["kind"], 0)) + 1
	return {"kinds": kinds, "triangles": triangles, "segments": segments}


# ── 5. Every area's layers ────────────────────────────────────────────────────

func _all_area_counts() -> void:
	var wrong := PackedStringArray()
	for po in range(1, 14):
		var counts: Dictionary = GeoRegionData.area(po).get("counts", {})
		if counts != ALL_OVERLAY_COUNTS[po]:
			wrong.append("PO%d %s" % [po, str(counts)])
	_check(wrong.is_empty(),
		"geo region review: all 13 areas' layer counts equal the reference, a point's radius and a fairway's buffer counted (wrong: %s)" % ", ".join(wrong))


# ── 4b. Holes stay open ───────────────────────────────────────────────────────

## exclusions.regional's polygons with holes (islands in a salmon fjord,
## enclaves in a protected area): no fill triangle covers a point inside a
## hole, in any area (the region view and the card's crop fill from these).
func _holes_stay_open() -> void:
	var parts := 0
	var covered := PackedStringArray()
	for po in range(1, 14):
		for row: Dictionary in GeoRegionData.area(po)["layers"]["exclusions"]:
			for part: Dictionary in row["parts"]:
				var tris: PackedInt32Array = part["tris"]
				if (part["holes"] as Array).is_empty() or tris.is_empty():
					continue
				parts += 1
				var pts: PackedVector2Array = part["fill_points"]
				for hole: PackedVector2Array in part["holes"]:
					var inside := _hole_point(hole)
					if not inside.is_finite():
						continue
					for k in range(0, tris.size(), 3):
						if _in_triangle(inside, pts[tris[k]], pts[tris[k + 1]], pts[tris[k + 2]]):
							covered.append("PO%d %s" % [po, String(row.get("name", ""))])
							break
	_check(parts > 0 and covered.is_empty(),
		"geo region review: no fill covers a hole of the %d holed exclusion parts on the 13 areas' maps %s" % [parts, str(covered.slice(0, 3))])


## A point well inside a hole: the middle of its widest horizontal run, at
## least 0.5 px from its edge, or INF for a sliver.
static func _hole_point(ring: PackedVector2Array) -> Vector2:
	var box := Rect2(ring[0], Vector2.ZERO)
	for q in ring:
		box = box.expand(q)
	for f: float in [0.5, 0.4, 0.6, 0.3, 0.7]:
		var y: float = box.position.y + box.size.y * f
		var xs: Array[float] = []
		for i in ring.size():
			var a := ring[i]
			var b := ring[(i + 1) % ring.size()]
			if (a.y <= y) != (b.y <= y):
				xs.append(a.x + (y - a.y) / (b.y - a.y) * (b.x - a.x))
		xs.sort()
		for i in range(0, xs.size() - 1, 2):
			if xs[i + 1] - xs[i] >= 1.0:
				var p := Vector2((xs[i] + xs[i + 1]) * 0.5, y)
				if Geometry2D.is_point_in_polygon(p, ring):
					return p
	return Vector2.INF


## Strictly inside a triangle (either winding).
static func _in_triangle(p: Vector2, a: Vector2, b: Vector2, c: Vector2) -> bool:
	var d1 := (b - a).cross(p - a)
	var d2 := (c - b).cross(p - b)
	var d3 := (a - c).cross(p - c)
	return (d1 > 1e-6 and d2 > 1e-6 and d3 > 1e-6) or (d1 < -1e-6 and d2 < -1e-6 and d3 < -1e-6)


# ── 6. The keyboard's rows and the focus after a card ─────────────────────────

func _keyboard_rows_and_focus(map: Control) -> void:
	var drill: GeoDrillStack = map.geo_drill()
	map.drill_into(AREA)
	await t.get_tree().process_frame
	await t.get_tree().process_frame
	var detail := drill.detail()
	var view := drill.region_view()
	var sites := view.player_sites()
	var rows := detail.yours_buttons()
	var hav := detail.hav_button()
	var bound_in_view := view.offshore().any(func(r: Dictionary) -> bool: return String(r.get("bound_plot_id", "")) != "")
	_check(not sites.is_empty() and rows.size() == sites.size() + (1 if bound_in_view else 0) and (hav != null) == bound_in_view
			and rows.all(func(b: Button) -> bool: return b.focus_mode == Control.FOCUS_ALL and b.tooltip_text != ""),
		"geo region review: the detail column lists the company's %d sites in PO3 and hav_1's plot as focusable rows" % sites.size())
	var site_row := detail.site_button(String(sites[0].get("site_id", ""))) if not sites.is_empty() else null
	if site_row != null:
		site_row.grab_focus()
	await t._press_key(KEY_ENTER)
	await t.get_tree().process_frame
	var card := drill.site_card()
	var opened: bool = map.map_level() == "site" and card.kind() == "site" and String(card.row().get("site_id", "")) == String(sites[0].get("site_id", ""))
	await t._press_key(KEY_ESCAPE)
	await t.get_tree().process_frame
	await t.get_tree().process_frame
	_check(opened and map.map_level() == "region" and site_row != null and _focus() == site_row,
		"geo region review: Enter on a site's row opens its card and Esc puts the focus back on the row")
	if hav != null:
		hav.grab_focus()
	await t._press_key(KEY_ENTER)
	await t.get_tree().process_frame
	var hav_ok: bool = map.map_level() == "site" and card.kind() == "offshore" and card.action() == "hav"
	await t._press_key(KEY_ESCAPE)
	await t.get_tree().process_frame
	await t.get_tree().process_frame
	_check(hav != null and hav_ok and _focus() == hav,
		"geo region review: Enter on hav_1's row opens its card with 'Bygg her (hav)', Esc puts the focus back on the row")
	# A rival's card: a real click on its icon; Esc falls back to the suggested candidate.
	var icons: Array = (view.rival_layer() as Object).get("icons")
	var icon: Dictionary = {}
	for i: Dictionary in icons:
		var local := view.to_view(i["px"])
		if Rect2(Vector2.ZERO, view.size).grow(-40.0).has_point(local) and _rival_alone(view, drill.breadcrumbs(), i):
			icon = i
			break
	await t._click_at(view.get_global_rect().position + view.to_view(icon.get("px", Vector2.INF)))
	await t.get_tree().process_frame
	var subtitle := card.subtitle_text()
	var rival_ok: bool = map.map_level() == "site" and card.kind() == "rival" and int(card.row().get("id", -1)) == int(icon.get("id", -2))
	await t._press_key(KEY_ESCAPE)
	await t.get_tree().process_frame
	await t.get_tree().process_frame
	_check(rival_ok and _focus() == detail.candidate_button(_suggested(view.candidates())),
		"geo region review: a click on a rival's icon opens its card, Esc puts the focus on the suggested candidate's button")
	# D17: the card names the locality by its number, never the registry's name.
	_check(subtitle == TranslationServer.translate("MAP_SITE_LOCALITY_NUMBER") % int(icon.get("id", 0))
			and icons.all(func(i: Dictionary) -> bool: return not i.has("name")),
		"geo region review: a rival's card says '%s', and no rival icon carries the registry's locality name" % subtitle)


## No other rival icon within 8 px, and no mark, crumb, chip or readout over
## it (the click is the icon's own).
static func _rival_alone(view: GeoRegionView, crumbs: Control, icon: Dictionary) -> bool:
	var at := view.to_view(icon["px"])
	for other: Dictionary in (view.rival_layer() as Object).get("icons"):
		if other != icon and view.to_view(other["px"]).distance_to(at) < 8.0:
			return false
	for m in view.get_node("Markers").get_children():
		if (m as Control).get_rect().grow(4.0).has_point(at):
			return false
	var global := view.get_global_rect().position + at
	for cover: Control in [crumbs, view.get_node("ChipsCard"), view.get_node("ReadoutCard")]:
		if cover.is_visible_in_tree() and cover.get_global_rect().grow(4.0).has_point(global):
			return false
	return true


# ── 8. The sim's own signals refresh the region and the card ──────────────────

## The card the player watches follows the sim with no dialog in between:
## the suggested candidate is refused for lack of funds; a loan taken
## through the bridge and one day by key turn its 'Bygg her' on; a build
## through the bridge turns it into the company's site with 'Vis i 3D'.
func _day_refresh(map: Control) -> void:
	var drill: GeoDrillStack = map.geo_drill()
	map.drill_into(AREA)
	await t.get_tree().process_frame
	var view := drill.region_view()
	var id := _suggested(view.candidates())
	var row: Dictionary = {}
	for r: Dictionary in view.candidates():
		if String(r["id"]) == id:
			row = r
	drill.open_candidate(row)
	await t.get_tree().process_frame
	var card := drill.site_card()
	var refused: bool = card.action() == "build" and card.action_button().disabled \
		and String(row.get("build_reason_key", "")) == "insufficient_funds"
	var zone := String(row.get("build_zone_id", ""))
	var plan: Dictionary = sim.plan_site_build_at(zone, id)
	var need := float(plan.get("cost_nok", 0.0)) - float(plan.get("balance_nok", 0.0))
	var loan: Dictionary = sim.take_loan(maxf(need, 0.0) + 0.1 * float(plan.get("cost_nok", 0.0)) + 2_000_000.0, 36, 0.0)
	var reads := int(GeoBridgeAdapter.reads.get("get_sea_candidates", 0))
	await t._run_one_day_by_key()
	await t.get_tree().process_frame
	_check(refused and bool(loan.get("success", false)) and map.map_level() == "site" and String(card.row().get("id", "")) == id
			and card.action() == "build" and not card.action_button().disabled
			and int(GeoBridgeAdapter.reads.get("get_sea_candidates", 0)) > reads,
		"geo region review: a loan through the bridge and a day by key turn the open card's 'Bygg her' on, no dialog involved (%s, %s)" % [
			id, str(loan.get("message", loan.get("reason_key", "")))])
	var result: Dictionary = sim.build_expansion_site_at(zone, id)
	await t._run_one_day_by_key()
	await t.get_tree().process_frame
	var fresh: Dictionary = {}
	for r: Dictionary in view.candidates():
		if String(r["id"]) == id:
			fresh = r
	_check(bool(result.get("success", false)) and view.state_of(fresh) == "yours" and card.action() == "view_3d"
			and String(card.row().get("id", "")) == id,
		"geo region review: a build through the bridge turns the open card's candidate into the company's site, 'Vis i 3D' (%s)" % id)
	drill.step_up()


# ── 9. Company sites on one spot ──────────────────────────────────────────────

func _spread_sites() -> void:
	var fake := _FakeFacade.new()
	var layer := CanvasLayer.new()
	layer.layer = 120
	t.add_child(layer)
	layer.add_child(fake)
	var view := GeoRegionView.new()
	view.size = Vector2(900, 700)
	layer.add_child(view)
	await t.get_tree().process_frame
	var xy: Array = []
	for f: Dictionary in GeoData.production_areas("national"):
		if int(f["properties"]["id"]) == AREA:
			xy = f["properties"]["label_25833"]
	fake.players = [
		{"site_id": "spot_a", "site_label": "A", "label": "Sted", "po": AREA, "candidate_id": "", "x": int(xy[0]), "y": int(xy[1]), "source": "anchor"},
		{"site_id": "spot_b", "site_label": "B", "label": "Sted", "po": AREA, "candidate_id": "", "x": int(xy[0]), "y": int(xy[1]), "source": "anchor"},
	]
	view.show_area(AREA, GeoBridgeAdapter.new(fake))
	await t.get_tree().process_frame
	var a := view.site_marker("spot_a")
	var b := view.site_marker("spot_b")
	var chosen: Array = []
	view.player_site_chosen.connect(func(r: Dictionary) -> void: chosen.append(String(r["site_id"])))
	if a != null and b != null:
		await t._click_at(a.get_global_rect().get_center())
		await t._click_at(b.get_global_rect().get_center())
	var true_at := view.to_view(GeoRegionMath.map_px(AREA, float(xy[0]), float(xy[1])))
	_check(a != null and b != null and a.get_rect().get_center().distance_to(b.get_rect().get_center()) >= GeoRegionView.SITE_PX - 0.5
			and (a.get_meta(&"anchor") as Vector2).distance_to(true_at) < 0.5 and (b.get_meta(&"anchor") as Vector2).distance_to(true_at) < 0.5
			and chosen == ["spot_a", "spot_b"],
		"geo region review: two company sites on one spot draw apart, both anchored there, each answering its own click %s" % str(chosen))
	layer.queue_free()
	await t.get_tree().process_frame


class _FakeFacade extends Node:
	var players: Array = []
	func get_sea_candidates(_po: int) -> Array: return []
	func get_player_site_locations() -> Array: return players
	func get_geo_sites_signature() -> String: return "fake"
	func get_geo_sites() -> Dictionary: return {}
	func get_geo_offshore_candidates() -> Array: return []


# ── 10. The crop's heat ───────────────────────────────────────────────────────

func _crop_heat(map: Control) -> void:
	var drill: GeoDrillStack = map.geo_drill()
	map.drill_into(AREA)
	await t.get_tree().process_frame
	drill.open_candidate(drill.region_view().candidates()[0])
	await t.get_tree().process_frame
	await t.get_tree().process_frame
	var crop: Control = drill.site_card().crop()
	var heat := crop.get_node_or_null("CropHeat") as TextureRect
	var mat := heat.material as ShaderMaterial if heat != null else null
	var u := GeoRegionMath.shader_uniforms(AREA)
	var map_size: Vector2 = u["map_size"]
	var ok: bool = heat != null and heat.visible and mat != null and mat.shader == GeoRegionView.SuitabilityShader \
		and mat.get_shader_parameter("map_size") == map_size and mat.get_shader_parameter("suit_scale") == u["suit_scale"] \
		and mat.get_shader_parameter("suit_offset") == u["suit_offset"] and mat.get_shader_parameter("suit_tex") == GeoRegionView.heat_texture(AREA) \
		and heat.position.distance_to(crop.call("to_crop", Vector2.ZERO)) < 0.5 \
		and heat.size.distance_to(map_size * float(crop.call("scale_px"))) < 0.5 and not crop.has_method("_draw_heat")
	# Evidence only: a frame that re-records the crop against an idle one.
	var idle := await _frame_ms(func() -> void: pass)
	var redraw := await _frame_ms(func() -> void: crop.call("sync"))
	print("  geo D review: a frame re-recording the crop took %.1f ms (idle %.1f ms)" % [redraw, idle])
	_check(ok, "geo region review: the card's crop draws its heat as one rect through the region's shader and raw grid, laid on the base map")
	drill.step_up()


func _frame_ms(action: Callable) -> float:
	var best := INF
	for i in 3:
		await t.get_tree().process_frame
		action.call()
		var started := Time.get_ticks_usec()
		await t.get_tree().process_frame
		best = minf(best, (Time.get_ticks_usec() - started) / 1000.0)
	return best
