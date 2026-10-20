extends RefCounted
## Geo stages 2-3, Track C2 (docs/geo/STAGE23_PLAN.md): the national map in
## the running game. GeoMapChecks.gd runs these after the C1 checks, so the
## suite keeps one _run() line per track; UiFlowTests counts them with its
## own _check. Headless: no shader is compiled and no image is rendered;
## the checks read node state, the layers' records (redraw counters, what
## each _draw drew), the raw po_index on the CPU and real input.
##
## 1. The adapter answers the plan's reads (stubbed until Track A and B),
##    and calls the facade's reads once they exist.
## 2. Plates: each anchor within 1 px of its label_25833 in drawn_rect(), at
##    the default size, at a 1280x720 layout (UI at 150 %) and after the
##    resize back; no plate overlaps a plate, a city name or another area's
##    anchor, all inside the map, at both sizes (stronger than U1-05's check).
## 3. A real click on each of the 13 label points selects that area (the raw
##    po_index, then the polygon fallback for PO1 and PO3).
## 4. Layers: the cluster totals equal get_geo_sites by kind (1,070 / 309 /
##    1 / 401); 19 offshore areas and 30 salmon fjords drawn with no engine
##    error; the outline fallback; 156 candidates in 13 stars and 33
##    hexagons, hav_1's ringed.
## 5. The detail column equals the bridge (official light, sea area,
##    localities, MTB, owners), with no invented stat and no emoji; the data
##    overlay tints a farmed area's plate (the per-area fix).
## 6. Chips, tooltips and the key answer; ThemeLint models lint_background;
##    the screen lints clean at both sizes.
## 7. Ten days at 30x with the map open record no layer again and parse no
##    file; closing and reopening parses nothing.

const ThemeLint := preload("res://tests/ThemeLint.gd")
const TextLint := preload("res://tests/TextLint.gd")

const SCENARIO_SEED := 20261020
## 1920x1080 / 1.5: the panel lays out as at 1280x720.
const SMALL_SCALE := 1.5
const KIND_TOTALS := [1070, 309, 1, 401]
const AREAS := 13
const OPEN_PENS := 156
const OFFSHORE_SITES := 33
const OFFSHORE_FEATURES := 19
const SALMON_FJORDS := 30
const DAYS_AT_30X := 10
## The label points the 800 m open-sea mask leaves out (plan §8).
const LABELS_OFF_MASK := [1, 3]
const NATIONAL_FILES := ["basemap.json", "production_areas.national.geojson", "offshore_areas.national.geojson",
	"exclusions.national.geojson", "candidates.json"]
const EMOJI_PATTERN := "[\\x{1F000}-\\x{1FAFF}\\x{2600}-\\x{27BF}\\x{FE0F}]"

var t: Node = null
var sim: Node = null


func run(runner: Node) -> void:
	t = runner
	sim = t._sim
	var started := Time.get_ticks_msec()
	GeoData.reset_cache()
	var scene: Node = await t._spawn_po3_scene(SCENARIO_SEED)
	await t._drain_cards(scene.get_node("%CriticalEventCard"))
	var ui: Control = scene.get_node("UILayer/GameUI")
	var map: Control = await t._open_map_by_key(ui)
	_check(map != null and map.is_visible_in_tree() and map.chart() is GeoMapView,
		"geo map: Cmd+2 opens the national map (a GeoMapView)")
	if map == null or map.chart() == null:
		scene.queue_free()
		return
	await _frames(4)
	var chart: GeoMapView = map.chart()
	_adapter(chart)
	_adapter_uses_the_real_reads()
	_plates_anchored(chart, "the default size")
	_plates_clear(map, chart, "the default size")
	await _small_size(map, chart)
	await _clicks_select_areas(map, chart)
	await _layers(chart)
	await _fjord_fallback()
	_detail(map, chart)
	_overlay_fix(map)
	await _chips(chart)
	_tooltips(chart)
	await _legend(chart)
	await _lint_background()
	map.select_area(3)
	await t._lint_screen("map national")
	await _thirty_times(scene, map, chart)
	await _rebinding(chart)
	await _reopen(ui, map)
	var stubbed := ", ".join(chart.bridge.stubbed())
	scene.queue_free()
	await t.get_tree().process_frame
	# Leave a cold cache for the checks that follow (Track D's lazy loads).
	GeoData.reset_cache()
	print("  geo C2 checks took %d ms (stubbed reads: %s)" % [Time.get_ticks_msec() - started, stubbed])


func _check(ok: bool, label: String) -> void:
	t._check(ok, label)


## UiFlowTests' warning and error collector (a Logger).
func _logger() -> Logger:
	var suite: GDScript = load("res://tests/UiFlowTests.gd")
	return suite.get_script_constant_map()["_WarningLog"].new()


func _frames(n: int) -> void:
	for i in n:
		await t.get_tree().process_frame


# ── 1. The adapter ────────────────────────────────────────────────────────────

func _adapter(chart: GeoMapView) -> void:
	var areas := chart.bridge.production_areas()
	var ok := areas.size() == AREAS
	for i in areas.size():
		var a: Dictionary = areas[i]
		ok = ok and int(a.get("id", 0)) == i + 1 and str(a.get("name", "")) != "" \
			and str(a.get("official_traffic_light", "")) in ["green", "yellow", "red"] \
			and float(a.get("label_x", 0.0)) != 0.0 and float(a.get("sea_area_km2", 0.0)) > 0.0
	_check(ok and chart.areas().size() == AREAS,
		"geo map: get_geo_production_areas gives the 13 areas with names, official lights and label points (stubbed: %s)"
			% ", ".join(chart.bridge.stubbed()))


## Once Tracks A and B are merged the facade has the reads: the adapter then
## calls them, never the stub, and hands the sites over in the plan's packed
## types whatever the bridge marshals.
func _adapter_uses_the_real_reads() -> void:
	var fake := _FakeSim.new()
	var adapter := GeoBridgeAdapter.new(fake)
	var sites := adapter.sites()
	var ok := adapter.stubbed().is_empty() and adapter.production_areas() == [{"id": 99}] \
		and adapter.offshore_candidates() == [{"id": "HTH99"}] and adapter.area_at(1.0, 2.0) == 7 \
		and adapter.sites_signature() == "fake:2" and adapter.rival_summary(3) == {"po": 3} \
		and adapter.player_site_locations() == [{"site_id": "s1"}]
	_check(ok and sites["ids"] is PackedInt32Array and sites["x"] is PackedInt32Array and sites["kind"] is PackedByteArray
			and sites["po"] is PackedByteArray and sites["mtb_t"] is PackedFloat32Array and sites["owner"] is PackedInt32Array
			and Array(sites["x"]) == [1, 2] and Array(sites["kind"]) == [0, 5] and Array(sites["owner"]) == [-1, 0]
			and is_equal_approx(sites["mtb_t"][0], 1.5) and (sites["owners"] as Array).size() == 1,
		"geo map: with the lanes merged the adapter calls the seven reads, and normalises the sites' arrays")
	fake.free()


class _FakeSim extends Node:
	func get_geo_production_areas() -> Array: return [{"id": 99}]
	func get_geo_offshore_candidates() -> Array: return [{"id": "HTH99"}]
	func get_geo_area_at(_x: float, _y: float) -> int: return 7
	func get_geo_sites_signature() -> String: return "fake:2"
	func get_geo_sites() -> Dictionary:
		return {"signature": "fake:2", "ids": [5, 7], "x": PackedInt64Array([1, 2]), "y": [3.0, 4.0],
			"kind": PackedInt32Array([0, 5]), "po": [3, 4], "mtb_t": PackedFloat64Array([1.5, 0.0]),
			"owner": [-1, 0], "owners": [{"id": "fake", "name": "Fake", "is_major": true}]}
	func get_rival_summary(po: int) -> Dictionary: return {"po": po}
	func get_player_site_locations() -> Array: return [{"site_id": "s1"}]


# ── 2. Plates ─────────────────────────────────────────────────────────────────

## Each plate's anchor against its label point mapped into the drawn rect,
## recomputed here from the texture's aspect (TextureRect's keep-aspect rule).
func _plates_anchored(chart: GeoMapView, where: String) -> void:
	var info := GeoData.basemap("national")
	var tex := Vector2(float(info["width"]), float(info["height"]))
	var w := tex.x * chart.size.y / tex.y
	var h := chart.size.y
	if w > chart.size.x:
		w = chart.size.x
		h = tex.y * w / tex.x
	var drawn := Rect2((chart.size - Vector2(w, h)) * 0.5, Vector2(w, h))
	var off := []
	for a: Dictionary in chart.areas():
		var plate := chart.plate(int(a["id"]))
		var want := drawn.position + GeoProjection.to_px(float(a["label_x"]), float(a["label_y"]),
			info["bounds_25833"], info["res_m"]) * (w / tex.x)
		if plate == null or plate.anchor.distance_to(want) > 1.0:
			off.append("PO%d %s vs %s" % [int(a["id"]), str(plate.anchor) if plate else "missing", str(want)])
	_check(off.is_empty() and chart.drawn_rect().is_equal_approx(drawn),
		"geo map: each of the 13 anchors lies within 1 px of its label point in drawn_rect() at %s %s" % [where, str(off.slice(0, 3))])


func _plates_clear(map: Control, chart: GeoMapView, where: String) -> void:
	var plates: Array = chart.plates().values()
	var hits := []
	for i in plates.size():
		for j in range(i + 1, plates.size()):
			if (plates[i] as MapMarker).get_rect().intersects((plates[j] as MapMarker).get_rect()):
				hits.append("%s/%s" % [plates[i].region_id, plates[j].region_id])
	_check(hits.is_empty(), "geo map: no plate overlaps another plate at %s %s" % [where, str(hits.slice(0, 3))])
	var cities: Array[Rect2] = map.city_label_rects()
	var city_hits := []
	for p: MapMarker in plates:
		for c in cities:
			if p.get_rect().intersects(c) or p.dot_rect().intersects(c):
				city_hits.append("%s/%s" % [p.region_id, str(c)])
	_check(city_hits.is_empty() and cities.size() >= 3,
		"geo map: no plate or anchor overlaps a city name, %d names placed at %s %s" % [cities.size(), where, str(city_hits.slice(0, 2))])
	var outside := []
	var hidden := []
	var bounds := Rect2(Vector2.ZERO, chart.size)
	for p: MapMarker in plates:
		if not bounds.encloses(p.get_rect()):
			outside.append(p.region_id)
		for q: MapMarker in plates:
			if q != p and p.get_rect().has_point(q.anchor):
				hidden.append("%s hides %s" % [p.region_id, q.region_id])
	_check(outside.is_empty(), "geo map: every plate lies inside the map at %s %s" % [where, str(outside)])
	_check(hidden.is_empty(), "geo map: no plate hides another area's anchor at %s %s" % [where, str(hidden.slice(0, 3))])
	var rects: Array[Rect2] = map.region_label_rects()
	_check(rects.size() == 2 * AREAS, "geo map: region_label_rects() gives an anchor and a plate per area (%d) at %s" % [rects.size(), where])


func _small_size(map: Control, chart: GeoMapView) -> void:
	var win := t.get_window()
	var old_scale := win.content_scale_factor
	var big := chart.size
	var layouts := chart.layout_count
	var sites := chart.site_layer().redraw_count
	var areas := chart.overlay_layer().redraw_count
	win.content_scale_factor = SMALL_SCALE
	await _frames(6)
	_check(chart.size.x < big.x - 100.0 and chart.layout_count > layouts and chart.site_layer().redraw_count > sites
			and chart.overlay_layer().redraw_count > areas,
		"geo map: a resize lays the chart out again and re-records its layers (%s -> %s, compact %s)" % [str(big), str(chart.size), str(chart.compact)])
	_plates_anchored(chart, "a 1280x720 layout")
	_plates_clear(map, chart, "a 1280x720 layout")
	await t._lint_screen("map national at a 1280x720 layout")
	win.content_scale_factor = old_scale
	await _frames(6)
	_plates_anchored(chart, "the default size after the resize back")


# ── 3. Clicks ─────────────────────────────────────────────────────────────────

func _clicks_select_areas(map: Control, chart: GeoMapView) -> void:
	var section: RegionExpansionSection = map.expansion_section()
	var fallback := []
	for a: Dictionary in chart.areas():
		var area := int(a["id"])
		var plate := chart.plate(area)
		map.select_area(2 if area != 2 else 4)
		await t.get_tree().process_frame
		var raw := chart.po_index_at_local(plate.anchor)
		if raw == 0:
			fallback.append(area)
		await t._click_at(chart.get_global_rect().position + plate.anchor)
		_check(int(section.area().get("production_area", 0)) == area and map._selected_region_id == plate.region_id
				and chart.tint_material().get_shader_parameter("selected_id") == area,
			"geo map: a click on PO%d's label point selects it (%s)" % [area, "po_index %d" % raw if raw > 0 else "polygon fallback"])
	_check(fallback == LABELS_OFF_MASK,
		"geo map: PO1's and PO3's label points sample 0 in po_index and select through get_geo_area_at (%s)" % str(fallback))


# ── 4. Layers ─────────────────────────────────────────────────────────────────

func _layers(chart: GeoMapView) -> void:
	var sites := chart.bridge.sites()
	var by_class := PackedInt32Array([0, 0, 0, 0])
	var codes: PackedByteArray = sites.get("kind", PackedByteArray())
	for i in codes.size():
		by_class[GeoBridgeAdapter.display_class(GeoBridgeAdapter.kind_name(sites, i))] += 1
	var clusters := chart.site_layer().clusters()
	var totals := GeoClusterer.totals(clusters)
	var seen := {}
	var once := true
	for c: Dictionary in clusters:
		for i in (c["members"] as PackedInt32Array):
			once = once and not seen.has(i)
			seen[i] = true
	_check(totals == by_class and seen.size() == codes.size() and once,
		"geo map: the %d clusters hold every site once, by kind as get_geo_sites (%s)" % [clusters.size(), str(totals)])
	_check(Array(totals) == KIND_TOTALS,
		"geo map: 1,070 sea, 309 land, 1 offshore salmonid sites and 401 of other species (%s)" % str(totals))
	var overlay := chart.overlay_layer()
	var log := _logger()
	OS.add_logger(log)
	overlay.queue_redraw()
	chart.candidate_layer().queue_redraw()
	await _frames(2)
	OS.remove_logger(log)
	var errors: PackedStringArray = log.take()
	_check(overlay.offshore_drawn == OFFSHORE_FEATURES,
		"geo map: all 19 offshore areas are drawn, dashed by stage (%d)" % overlay.offshore_drawn)
	_check(overlay.fjords_filled + overlay.fjords_outlined == SALMON_FJORDS and errors.is_empty(),
		"geo map: all 30 salmon fjords are drawn (%d hatched, %d outlined) with no engine error %s" % [
			overlay.fjords_filled, overlay.fjords_outlined, str(errors.slice(0, 2))])
	var stars := chart.candidate_layer().star_groups()
	var represented := 0
	for s: Dictionary in stars:
		represented += int(s["count"])
	_check(stars.size() == AREAS and represented == OPEN_PENS,
		"geo map: the 156 open-pen candidates are 13 stars, one per area (%d in %d)" % [represented, stars.size()])
	var hexes := chart.candidate_layer().hexagons()
	var bound := hexes.filter(func(h: Dictionary) -> bool: return h["bound"])
	var want := chart.bridge.offshore_candidates().filter(func(r: Dictionary) -> bool:
		return str(r.get("bound_plot_id", "")) == "hav_1")
	_check(hexes.size() == OFFSHORE_SITES, "geo map: the 33 offshore candidates are hexagons (%d)" % hexes.size())
	_check(bound.size() == 1 and want.size() == 1 and str(bound[0]["row"]["id"]) == str(want[0]["id"]),
		"geo map: exactly one hexagon is ringed, the candidate hav_1 is bound to (%s)" % (str(want[0]["id"]) if want.size() == 1 else "none"))


## The outline fallback: a polygon that does not triangulate is outlined,
## never filled, and logs no engine error; a triangle is filled.
func _fjord_fallback() -> void:
	var log := _logger()
	# A bow tie crosses itself: Geometry2D cannot triangulate it, and
	# draw_colored_polygon would log "triangulation failed".
	var flat := _Probe.new()
	flat.ring = PackedVector2Array([Vector2(10, 10), Vector2(60, 60), Vector2(60, 10), Vector2(10, 60)])
	var tri := _Probe.new()
	tri.ring = PackedVector2Array([Vector2(10, 10), Vector2(60, 10), Vector2(30, 50)])
	OS.add_logger(log)
	t.add_child(flat)
	t.add_child(tri)
	await _frames(2)
	OS.remove_logger(log)
	var errors: PackedStringArray = log.take()
	_check(flat.result == 0 and tri.result == 1 and errors.is_empty(),
		"geo map: a fjord that does not triangulate is outlined with no engine error, a triangle is hatched (%d, %d) %s" % [
			flat.result, tri.result, str(errors.slice(0, 2))])
	flat.queue_free()
	tri.queue_free()


class _Probe extends Control:
	var ring := PackedVector2Array()
	var result := -1

	func _draw() -> void:
		result = 1 if GeoOverlayLayer.fill_or_outline(self, ring, Color.RED, Color.RED, null) else 0


# ── 5. The detail column and the overlay fix ──────────────────────────────────

func _detail(map: Control, chart: GeoMapView) -> void:
	map.select_area(3)
	var facts: GeoAreaFacts = map.area_facts()
	var geo: Dictionary = chart.bridge.production_areas()[2]
	var summary := chart.bridge.rival_summary(3)
	var light := str(geo["official_traffic_light"])
	var chip := facts.find_child("OfficialLight", true, false) as Label
	_check(chip != null and chip.text == t.tr("MAP_GEO_OFFICIAL_LIGHT") % t.tr("EXPANSION_LIGHT_" + light.to_upper())
			and chip.tooltip_text != "",
		"geo map: the detail column shows PO3's official traffic light from the bridge ('%s')" % (chip.text if chip else "missing"))
	var sea := facts.find_child("SeaArea", true, false) as Label
	_check(sea != null and sea.text == t.tr("MAP_GEO_KM2") % SimFormatters.num(float(geo["sea_area_km2"])),
		"geo map: PO3's sea area equals the bridge (%s)" % (sea.text if sea else "missing"))
	var by_kind := GeoBridgeAdapter.sites_by_kind(summary)
	var total := 0
	for k: String in by_kind:
		total += int(by_kind[k])
	var count := facts.find_child("Localities", true, false) as Label
	var split := facts.find_child("LocalitiesSplit", true, false) as Label
	_check(count != null and count.text == SimFormatters.integer(total) and total > 0 and split != null
			and split.text.begins_with(str(int(by_kind.get("sea_salmonid", 0)))),
		"geo map: PO3's localities by kind equal get_rival_summary (%d: %s)" % [total, split.text if split else "missing"])
	var mtb := facts.find_child("SeaMtb", true, false) as Label
	_check(mtb != null and mtb.text == t.tr("MAP_GEO_TONNES") % SimFormatters.num(float(summary.get("sea_growout_mtb_t", 0.0)))
			and float(summary.get("sea_growout_mtb_t", 0.0)) > 0.0,
		"geo map: PO3's summed sea grow-out MTB equals get_rival_summary (%s)" % (mtb.text if mtb else "missing"))
	var owners: Array = summary.get("top_owners", [])
	var shown := []
	for i in 3:
		var row := facts.find_child("Owner_%d" % i, true, false)
		if row != null:
			shown.append((row.get_node("Name") as Label).text)
	var want := owners.slice(0, 3).map(func(o: Dictionary) -> String: return str(o.get("name", "")))
	_check(shown == want and (not owners.is_empty() or facts.find_child("NoOwners", true, false) != null),
		"geo map: the top owners are get_rival_summary's first three, or 'no owner data' (%s)" % str(shown))
	var invented := []
	for n in ["StatsGrid", "RiskSection", "DetailDesc", "DetailIcon", "MapIcon", "KeyOperators"]:
		if map.find_child(n, true, false) != null:
			invented.append(n)
	var emoji := RegEx.create_from_string(EMOJI_PATTERN)
	for l in map.find_children("*", "Label", true, false):
		if (l as Label).is_visible_in_tree() and emoji.search((l as Label).text) != null:
			invented.append((l as Label).text)
	_check(invented.is_empty(), "geo map: no invented stat and no emoji in the map's labels %s" % str(invented.slice(0, 3)))


func _overlay_fix(map: Control) -> void:
	var legend: Node = map.find_child("SiteOverlayLegend", true, false)
	var zone_area: Dictionary = map._zone_areas()
	var t3: float = legend.get_area_intensity(3, zone_area)
	var po3: MapMarker = map.region_marker(3)
	_check(t3 >= 0.0 and po3.overlay_active and po3.overlay_color == legend.intensity_to_color(t3)
			and float(legend.get_zone_intensity("PO03")) < 0.0,
		"geo map: the %s overlay tints the farmed PO3's plate through the zones' area (%.2f; the old 'PO03' lookup never matched)" % [
			legend.get_metric(), t3])
	var po4: MapMarker = map.region_marker(4)
	_check(not po4.overlay_active and float(legend.get_area_intensity(4, zone_area)) < 0.0,
		"geo map: an area the company does not farm keeps a plain plate")


# ── 6. Chips, tooltips, the key, the lints ────────────────────────────────────

func _chips(chart: GeoMapView) -> void:
	var log := _logger()
	OS.add_logger(log)
	var shown := {
		"sites": func() -> bool: return chart.site_layer().visible,
		"candidates": func() -> bool: return chart.candidate_layer().show_candidates,
		"offshore": func() -> bool: return chart.overlay_layer().show_offshore,
		"fjords": func() -> bool: return chart.overlay_layer().show_fjords,
	}
	var chips := chart.chips()
	_check(chips.size() == 4, "geo map: four layer chips (%d)" % chips.size())
	for chip in chips:
		var layer := str(chip.get_meta(&"layer", ""))
		var probe: Callable = shown.get(layer, func() -> bool: return true)
		await t._click_control(chip)
		var off: bool = not probe.call() and not chip.button_pressed
		await t._click_control(chip)
		_check(off and probe.call() and chip.button_pressed and chip.tooltip_text != "",
			"geo map: the chip '%s' hides and shows its layer, and says what it shows" % chip.text)
	OS.remove_logger(log)
	var lines: PackedStringArray = log.take()
	_check(lines.is_empty(), "geo map: the chips push no warning %s" % str(lines.slice(0, 2)))


func _tooltips(chart: GeoMapView) -> void:
	var texts := []
	var star: Dictionary = {}
	for s: Dictionary in chart.candidate_layer().star_groups():
		if str(chart.feature_at(s["pos"]).get("kind", "")) == "candidates":
			star = s
			break
	var star_tip := chart._get_tooltip(star.get("pos", Vector2.ZERO))
	texts.append(star_tip)
	_check(not star.is_empty() and star_tip.begins_with(str(int(star["count"])))
			and star_tip.contains(str(chart.areas()[int(star.get("po", 1)) - 1]["name"])),
		"geo map: a star's tooltip counts its area's candidates ('%s')" % star_tip.replace("\n", " / "))
	var cluster: Dictionary = {}
	for c: Dictionary in chart.site_layer().clusters():
		if int(c["count"]) > 1 and chart.candidate_layer().item_at(c["pos"]).is_empty():
			cluster = c
			break
	var cluster_tip := chart._get_tooltip(cluster.get("pos", Vector2.ZERO))
	texts.append(cluster_tip)
	_check(not cluster.is_empty() and cluster_tip == GeoMapTips.cluster(chart.site_layer(), cluster),
		"geo map: a cluster's tooltip gives its sites by kind ('%s')" % cluster_tip.replace("\n", " / "))
	var bound := chart.candidate_layer().hexagons().filter(func(h: Dictionary) -> bool: return h["bound"])
	var hex_tip := chart._get_tooltip(bound[0]["pos"]) if not bound.is_empty() else ""
	texts.append(hex_tip)
	_check(hex_tip.contains(t.tr("MAP_GEO_TIP_HAV1")), "geo map: the ringed hexagon says the game's open-sea plot lies there")
	# An area's own water, away from every icon: the area's name.
	var p6: MapMarker = chart.plate(6)
	var area_tip := ""
	for k in range(1, 30):
		for d in MapLabelPlacer.DIRECTIONS:
			var p := p6.anchor + d * 3.0 * k
			if area_tip == "" and chart.feature_at(p).is_empty() and chart.area_at_local(p) == 6:
				area_tip = chart._get_tooltip(p)
	texts.append(area_tip)
	_check(area_tip == t.tr("UI_MAP_ZONE") % [6, str(chart.areas()[5]["name"])],
		"geo map: the water of an area answers with its name ('%s')" % area_tip)
	var bad := []
	for text: String in texts:
		bad.append_array(TextLint.check_text(text, true))
	_check(bad.is_empty() and not texts.has(""), "geo map: the tooltips read as Norwegian, with no ids %s" % str(bad.slice(0, 3)))


func _legend(chart: GeoMapView) -> void:
	var legend := chart.legend()
	var glyphs := legend.find_children("Glyph_*", "", true, false)
	var layouts := chart.layout_count
	var was_open := legend.is_open()
	await t._click_control(legend.toggle_button())
	await _frames(2)
	_check(glyphs.size() == MapLegend.ROWS.size() and legend.is_open() != was_open and chart.layout_count > layouts,
		"geo map: the key shows the %d glyphs, folds by click and the plates make room (%s -> %s)" % [
			glyphs.size(), str(was_open), str(legend.is_open())])
	await t._click_control(legend.toggle_button())
	await _frames(2)
	_plates_anchored(chart, "the default size after the key moved")


## ThemeLint's lint_background (add-only strengthening): dark text over a
## picture on chart paper passes until the picture says it reads as deep water.
func _lint_background() -> void:
	var host := Control.new()
	host.theme = load("res://theme/lakskongen_chart_theme.tres")
	host.position = Vector2(40, 40)
	host.size = Vector2(300, 120)
	t.add_child(host)
	var paper := ColorRect.new()
	paper.color = MapPalette.chart("base")
	paper.size = host.size
	host.add_child(paper)
	var picture := TextureRect.new()
	picture.size = host.size
	host.add_child(picture)
	var label := Label.new()
	label.name = "PlantedLabel"
	label.text = "Kystkart"
	label.position = Vector2(20, 20)
	host.add_child(label)
	await _frames(2)
	var before := _planted(ThemeLint.low_contrast(host))
	picture.set_meta(&"lint_background", MapPalette.deep_water())
	var after := _planted(ThemeLint.low_contrast(host))
	_check(before.is_empty() and after.size() == 1,
		"geo map: ThemeLint judges text over a picture by its lint_background (paper %d, deep water %d)" % [before.size(), after.size()])
	host.queue_free()


func _planted(lines: PackedStringArray) -> Array:
	return Array(lines).filter(func(line: String) -> bool: return line.contains("PlantedLabel"))


# ── 7. Ticks and reopening ────────────────────────────────────────────────────

func _thirty_times(scene: Node, map: Control, chart: GeoMapView) -> void:
	GameplayPrefs.force_auto_pause(false)
	sim.set_auto_slow_deadlines(false)
	var card: Control = scene.get_node("%CriticalEventCard")
	var records := [chart.site_layer().redraw_count, chart.overlay_layer().redraw_count,
		chart.candidate_layer().redraw_count]
	var parses := GeoData.parse_count
	var layouts := chart.layout_count
	var day0 := int(sim.current_day)
	sim.set_time_speed(30.0)
	sim.set_paused(false)
	var ran: bool = await t._wait_until(func() -> bool:
		if card.is_open():
			card.continue_button.pressed.emit()
		if sim.is_paused():
			sim.set_paused(false)
		return int(sim.current_day) >= day0 + DAYS_AT_30X, 40.0)
	sim.set_paused(true)
	await _frames(2)
	var after := [chart.site_layer().redraw_count, chart.overlay_layer().redraw_count,
		chart.candidate_layer().redraw_count]
	_check(ran and map.is_visible_in_tree() and after == records,
		"geo map: %d days at 30x with the map open re-record no layer (%s -> %s)" % [int(sim.current_day) - day0, str(records), str(after)])
	_check(GeoData.parse_count == parses and chart.layout_count == layouts,
		"geo map: the days parse no geo file and lay nothing out again (parse_count %d)" % GeoData.parse_count)
	GameplayPrefs.force_auto_pause(null)
	sim.set_auto_slow_deadlines(true)


## A loaded game may bind hav_1 to another candidate: the next overview
## change re-records the candidates once, and an unchanged binding never.
func _rebinding(chart: GeoMapView) -> void:
	var real := chart.bridge
	var rows := real.offshore_candidates().duplicate(true)
	var moved := ""
	for r: Dictionary in rows:
		if str(r.get("bound_plot_id", "")) == "hav_1":
			r["bound_plot_id"] = ""
		elif moved == "":
			moved = str(r["id"])
			r["bound_plot_id"] = "hav_1"
	var fake := _FakeBinding.new()
	fake.rows = rows
	fake.signature = real.sites_signature()
	fake.players = real.player_site_locations()
	chart.bridge = GeoBridgeAdapter.new(fake)
	var before := chart.candidate_layer().redraw_count
	var first := chart.live_update(true)
	await _frames(2)
	var ringed := chart.candidate_layer().hexagons().filter(func(h: Dictionary) -> bool: return h["bound"])
	var second := chart.live_update(true)
	_check(Array(first) == ["candidates"] and second.is_empty() and chart.candidate_layer().redraw_count == before + 1
			and ringed.size() == 1 and str(ringed[0]["row"]["id"]) == moved,
		"geo map: a new hav_1 binding re-records the candidates once and rings %s; an unchanged one re-records nothing (%s, %s)" % [
			moved, str(first), str(second)])
	chart.bridge = real
	chart.live_update(true)
	fake.free()


class _FakeBinding extends Node:
	var rows: Array = []
	var signature := ""
	var players: Array = []
	func get_geo_offshore_candidates() -> Array: return rows
	func get_geo_sites_signature() -> String: return signature
	func get_player_site_locations() -> Array: return players


func _reopen(ui: Control, map: Control) -> void:
	var parses := GeoData.parse_count
	await t._press_key(KEY_ESCAPE)
	await _frames(2)
	var closed: bool = ui.get_active_tab() == "dashboard"
	var again: Control = await t._open_map_by_key(ui)
	# At most once: a file another part already holds (the adapter's stub
	# keeps its rows across GeoData.reset_cache) is not read again at all.
	var once := NATIONAL_FILES.all(func(rel: String) -> bool: return GeoData.parses_of(rel) <= 1) \
		and GeoData.parses_of("basemap.json") == 1
	_check(closed and again == map and again.is_visible_in_tree() and GeoData.parse_count == parses and once,
		"geo map: Esc closes the map and Cmd+2 reopens it with no file parsed again (each national file at most once, parse_count %d)" % GeoData.parse_count)
	_check(int(map.expansion_section().area().get("production_area", 0)) == 3 and map.chart().is_visible_in_tree()
			and not map.drill_into(3),
		"geo map: the map reopens on the national chart with the area still picked (drill_into is Track D's hook)")
