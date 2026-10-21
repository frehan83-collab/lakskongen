extends RefCounted
## Geo stages 2-3, the captain's C2 merge (docs/geo/STAGE23_PLAN.md §4 step 3):
## the review of geo/map-national-2's lower findings, pinned in the running
## game. GeoMapNationalChecks.gd runs these on the national map at the
## default layout; GeoMapHitChecks.gd holds the pointer's checks.
## 1. Counts of one read in the singular, in the split by kind and in an
##    owner's row ("1 laksefisklokalitet i sjø", "1 lokalitet").
## 2. The open-pen stars come from track B's get_sea_candidates: each stands
##    on its area's best free candidate and names it as track A cleans it
##    ("Selje", not "Selje kloster"); a farm built on that place moves the
##    star to the next free one, with a line saying how many are the
##    company's; where it farms all twelve there is no star.
## 3. The five salmon fjords with a hole: the hatch covers the fjord less its
##    hole, and a point in the hole is not the fjord.
## 4. Cluster numerals keep 11 screen pixels on a 1280x720 window with the
##    UI at 100 % (they drew at about 7): the badges and their grid grow.
## 5. GEO-VIS (the captain's integration close): no badge is drawn over
##    another badge or a single site at 1920x1080 and 1280x720 with the UI at
##    100 % and 125 %: the grown badges had stacked over each other and the
##    city names; and no plate's anchor diamond sits on a badge's numeral.

var t: Node = null


func run(runner: Node, map: Control, chart: GeoMapView) -> void:
	t = runner
	_singular_forms(map, chart)
	_stars_read_the_bridge(chart)
	await _stars_follow_the_companys_places(chart)
	_fjord_holes(chart)
	await _badge_numerals(chart)
	await _marks_never_overlap(chart)


func _check(ok: bool, label: String) -> void:
	t._check(ok, label)


## The split and an owner's row read right for a count of 1 and of many
## (Norwegian, the suite's locale).
func _singular_forms(map: Control, chart: GeoMapView) -> void:
	var one := GeoMapTips.split_text([1, 2, 0, 1])
	var many := GeoMapTips.split_text([153, 45, 0, 60])
	_check(one == "1 laksefisklokalitet i sjø · 2 på land · 0 til havs · 1 annen art"
			and many == "153 laksefisklokaliteter i sjø · 45 på land · 0 til havs · 60 andre arter",
		"geo map: the split by kind reads '%s' for one and '%s' for many" % [one, many])
	var facts: GeoAreaFacts = map.area_facts()
	var geo: Dictionary = chart.areas()[2]
	facts.show_area(3, geo, {"sites_by_kind": {"sea_salmonid": 1, "land_other": 1}, "sea_growout_mtb_t": 3120.0,
		"top_owners": [{"id": "a", "name": "Prøveeier", "is_major": false, "sites": 1, "mtb_t": 3120.0},
			{"id": "b", "name": "Prøveeier to", "is_major": false, "sites": 4, "mtb_t": 940.0}]})
	var row0 := facts.find_child("Owner_0", true, false)
	var row1 := facts.find_child("Owner_1", true, false)
	var split := facts.find_child("LocalitiesSplit", true, false) as Label
	var first := (row0.get_node("Sites") as Label).text if row0 != null else ""
	var second := (row1.get_node("Sites") as Label).text if row1 != null else ""
	_check(first == "1 lokalitet · " + SimFormatters.num(3120.0) + " t" and second == "4 lokaliteter · 940 t"
			and split != null and split.text == "1 laksefisklokalitet i sjø · 0 på land · 0 til havs · 1 annen art",
		"geo map: the detail column says '%s', '%s' and '%s'" % [first, second, split.text if split else "missing"])
	map.select_area(3)


func _star(chart: GeoMapView, po: int) -> Dictionary:
	for s: Dictionary in chart.candidate_layer().star_groups():
		if int(s["po"]) == po:
			return s
	return {}


func _stars_read_the_bridge(chart: GeoMapView) -> void:
	var started := Time.get_ticks_usec()
	var pens := chart.bridge.open_pen_candidates()
	print("  geo C2 merge: the stars' 13 get_sea_candidates reads (%d rows) took %.1f ms" % [
		pens.size(), float(Time.get_ticks_usec() - started) / 1000.0])
	var wrong := []
	for s: Dictionary in chart.candidate_layer().star_groups():
		var rows: Array = t._sim.get_sea_candidates(int(s["po"]))
		var free := rows.filter(func(r: Dictionary) -> bool: return str(r.get("taken_by_site_id", "")) == "")
		var want: Dictionary = free[0] if not free.is_empty() else rows[0]
		var tip := chart._get_tooltip(s["pos"])
		if str(s["best"].get("id", "")) != str(want.get("id", "")) or int(s["count"]) != rows.size() \
				or not tip.contains(" " + str(want.get("label", "")) + ",") \
				or (s["pos"] as Vector2).distance_to(chart.utm_to_local(float(want["x"]), float(want["y"]))) > 0.01:
			wrong.append("PO%d %s/%s '%s'" % [int(s["po"]), str(s["best"].get("id", "")), str(want.get("id", "")), tip])
	var po4 := _star(chart, 4)
	var tip4 := chart._get_tooltip(po4.get("pos", Vector2.ZERO))
	_check(wrong.is_empty() and tip4.contains("ved Selje,") and not tip4.contains("kloster"),
		"geo map: each star stands on its area's best free candidate from get_sea_candidates and names it as track A cleans it ('%s') %s" % [
			tip4, str(wrong.slice(0, 2))])


## A farm of the company's on PO3's best place, through the map's own
## refresh, even after a relayout took the farm in first: the star moves to
## the next free place and says one is the company's; the same state again records nothing; all twelve held, the
## area has no star (nothing to offer, and it would hide under a farm).
func _stars_follow_the_companys_places(chart: GeoMapView) -> void:
	var real := chart.bridge
	var po3: Array = t._sim.get_sea_candidates(3)
	var first: Dictionary = po3[0]
	var second: Dictionary = po3[1]
	var fake := _FakeHolder.new()
	fake.sim = t._sim
	fake.held = [str(first["id"])]
	fake.extra = [{"site_id": "geo_review_site", "site_label": "Prøvelokalitet", "label": str(first["label"]), "po": 3,
		"candidate_id": str(first["id"]), "x": float(first["x"]), "y": float(first["y"]), "source": "candidate"}]
	chart.bridge = GeoBridgeAdapter.new(fake)
	# A relayout first (a window resize while the tab is hidden) takes the new
	# farms in: the refresh after it must still move the star.
	chart.relayout(true)
	await t.get_tree().process_frame
	var before := chart.candidate_layer().redraw_count
	var changed := chart.live_update(true)
	await t.get_tree().process_frame
	var star := _star(chart, 3)
	var area := str(chart.areas()[2]["name"])
	var want := t.tr("MAP_GEO_TIP_CANDIDATES_FREE") % [12, area, str(second["label"]),
		SimFormatters.num(float(second["score"]), 2)] + "\n" + t.tr("MAP_GEO_TIP_CANDIDATES_YOURS") % 1
	var tip := chart._get_tooltip(star.get("pos", Vector2.ZERO))
	var moved := "candidates" in changed and str(star.get("best", {}).get("id", "")) == str(second["id"]) \
		and tip == want and chart.candidate_layer().redraw_count == before + 1
	# Another farm, on no candidate: the farms change, the stars do not.
	fake.extra = fake.extra + [{"site_id": "geo_review_site_1", "site_label": "Prøvelokalitet en", "label": "", "po": 3,
		"candidate_id": "", "x": float(first["x"]) + 20000.0, "y": float(first["y"]), "source": "anchor"}]
	var again := chart.live_update(true)
	await t.get_tree().process_frame
	var quiet := "players" in again and not "candidates" in again and chart.candidate_layer().redraw_count == before + 1
	fake.held = po3.map(func(r: Dictionary) -> String: return str(r["id"]))
	fake.extra = fake.extra + [{"site_id": "geo_review_site_2", "site_label": "Prøvelokalitet to", "label": "", "po": 3,
		"candidate_id": "", "x": float(second["x"]), "y": float(second["y"]), "source": "candidate"}]
	chart.live_update(true)
	var all_held := _star(chart, 3).is_empty() and chart.candidate_layer().star_groups().size() == 12
	chart.bridge = real
	chart.live_update(true)
	fake.free()
	_check(moved and quiet, "geo map: a farm on PO3's best place moves the star to %s and says one is yours ('%s'); a farm on no candidate records no star again" % [
		str(second["id"]), tip.replace("\n", " / ")])
	_check(all_held and str(_star(chart, 3).get("best", {}).get("id", "")) == str(first["id"])
			and chart.candidate_layer().star_groups().size() == 13,
		"geo map: with all twelve of PO3's places held PO3 has no star, and the game's own state puts it back on %s" % str(first["id"]))


## The game's facade, with candidates the test marks as the company's.
class _FakeHolder extends Node:
	var sim: Node = null
	var held: Array = []
	var extra: Array = []
	func get_player_site_locations() -> Array: return sim.get_player_site_locations() + extra
	func get_geo_sites_signature() -> String: return sim.get_geo_sites_signature()
	func get_geo_offshore_candidates() -> Array: return sim.get_geo_offshore_candidates()
	func get_sea_candidates(area: int) -> Array:
		return sim.get_sea_candidates(area).map(func(r: Dictionary) -> Dictionary:
			var c := r.duplicate()
			if str(r["id"]) in held:
				c["taken_by_site_id"] = "geo_review_site"
			return c)


## The salmon fjords with holes (exclusions.national: Porsanger,
## Namsfjorden, Trondheimsfjorden, Kysten Jæren-Dalane, Lindesnes -
## Mannefjorden): the hatched pieces' area is the fjord's less its holes'
## (within 0.5 %), every piece but a sliver triangulates (the fjord is
## hatched, not merely outlined), a point in a hole is not the fjord, a point
## in a piece is.
func _fjord_holes(chart: GeoMapView) -> void:
	var layer := chart.overlay_layer()
	var holed := layer.fjords().filter(func(f: Dictionary) -> bool: return not (f["holes"] as Array).is_empty())
	var wrong := []
	for f: Dictionary in holed:
		var want := GeoOverlayLayer.polygon_area(f["ring"])
		for h: PackedVector2Array in f["holes"]:
			want -= GeoOverlayLayer.polygon_area(h)
		var got := 0.0
		for q: PackedVector2Array in f["pieces"]:
			got += GeoOverlayLayer.polygon_area(q)
		var in_hole := str(layer.item_at(_inside(f["holes"][0])).get("kind", ""))
		var in_fjord := str(layer.item_at(_inside(f["pieces"][0])).get("name", ""))
		var hatched := str(f["name"]) in layer.fjords_hatched
		if absf(got - want) > want * 0.005 or in_hole == "fjord" or in_fjord != str(f["name"]) or not hatched:
			wrong.append("%s: %.1f/%.1f px², hole '%s', piece '%s'" % [str(f["name"]), got, want, in_hole, in_fjord])
	_check(holed.size() == 5 and wrong.is_empty(),
		"geo map: the %d salmon fjords with a hole are hatched around it and do not answer inside it %s" % [
			holed.size(), str(wrong.slice(0, 2))])


## A point inside a simple polygon: the centre of its first triangle.
static func _inside(poly: PackedVector2Array) -> Vector2:
	var tri := Geometry2D.triangulate_polygon(poly)
	return (poly[tri[0]] + poly[tri[1]] + poly[tri[2]]) / 3.0 if tri.size() >= 3 else poly[0]


func _frames(n: int) -> void:
	for i in n:
		await t.get_tree().process_frame


## The cluster numerals on screen, in physical pixels: on a 1920x1080 and a
## 1280x720 window with the UI at 100 %, and on 1920x1080 with the UI at
## 150 %. Each case: every numeral 11 px or more, inside its badge, badges
## no closer than the grown grid's gap; the small window re-lays the chart
## out by itself (canvas_items stretch keeps the layout size, so only the
## viewport's size_changed says so).
func _badge_numerals(chart: GeoMapView) -> void:
	var win := t.get_window()
	var old_size := win.size
	var old_factor := win.content_scale_factor
	var rows := []
	var ok := true
	for spec: Array in [[Vector2i(1920, 1080), 1.0], [Vector2i(1280, 720), 1.0], [Vector2i(1920, 1080), 1.5]]:
		var layouts := chart.layout_count
		var was_symbol := chart.site_layer().symbol_scale()
		win.size = spec[0]
		win.content_scale_factor = spec[1]
		await _frames(4)
		var layer := chart.site_layer()
		var screen := chart.screen_scale()
		var px := float(layer.badge_font_size()) * screen
		var font := layer.get_theme_font(&"font", &"BodySmallStrong")
		var fits := font != null
		var badges: Array = layer.clusters().filter(func(c: Dictionary) -> bool: return int(c["count"]) > 1)
		for c: Dictionary in badges:
			var n := int(c["count"])
			var w := font.get_string_size(str(n), HORIZONTAL_ALIGNMENT_LEFT, -1, layer.badge_font_size()).x if font else INF
			fits = fits and w <= 2.0 * (layer.badge_radius_now(n) - 1.0)
		var gap := INF
		for i in badges.size():
			for j in range(i + 1, badges.size()):
				gap = minf(gap, (badges[i]["pos"] as Vector2).distance_to(badges[j]["pos"]))
		var relaid := layer.symbol_scale() == was_symbol or chart.layout_count > layouts
		rows.append("%dx%d at %d %%: %.1f px, x%.2f, %d badges, fits %s, relaid %s, gap %.1f" % [spec[0].x, spec[0].y,
			int(float(spec[1]) * 100.0), px, layer.symbol_scale(), badges.size(), str(fits), str(relaid), gap])
		ok = ok and px >= GeoMapView.MIN_BADGE_SCREEN_PX - 0.01 and fits and not badges.is_empty() and relaid \
			and gap >= GeoSiteLayer.MIN_GAP_PX * layer.symbol_scale() - 0.01
	var small_grows := rows.size() == 3
	win.size = old_size
	win.content_scale_factor = old_factor
	await _frames(4)
	_check(ok and small_grows and chart.site_layer().symbol_scale() == 1.0,
		"geo map: cluster numerals draw at 11 screen px or more, inside their badges: %s" % ", ".join(rows))


## GEO-VIS: the site layer's marks keep apart at every window size and UI
## scale the review judged: two badges keep their discs, halos and the
## majors' arcs apart, a single site's icon keeps off a badge's disc (its
## numeral), and every site is still counted once.
func _marks_never_overlap(chart: GeoMapView) -> void:
	var win := t.get_window()
	var old_size := win.size
	var old_factor := win.content_scale_factor
	var rows := []
	var ok := true
	var anchor_rows := []
	var anchors_ok := true
	var sites := (chart.site_layer().sites().get("x", PackedInt32Array()) as PackedInt32Array).size()
	for spec: Array in [[Vector2i(1920, 1080), 1.0], [Vector2i(1280, 720), 1.0], [Vector2i(1280, 720), 1.25],
			[Vector2i(1920, 1080), 1.25]]:
		win.size = spec[0]
		win.content_scale_factor = spec[1]
		await _frames(4)
		var layer := chart.site_layer()
		var clusters := layer.clusters()
		var worst := INF
		var counted := 0
		for i in clusters.size():
			counted += int(clusters[i]["count"])
			for j in range(i + 1, clusters.size()):
				var gap := layer.gap_between(int(clusters[i]["count"]), int(clusters[j]["count"]))
				worst = minf(worst, (clusters[i]["pos"] as Vector2).distance_to(clusters[j]["pos"]) - gap)
		var badges := clusters.filter(func(c: Dictionary) -> bool: return int(c["count"]) > 1).size()
		rows.append("%dx%d at %d %%: %d clusters (%d badges), closest %.1f px clear" % [spec[0].x, spec[0].y,
			int(round(float(spec[1]) * 100.0)), clusters.size(), badges, worst])
		ok = ok and worst >= 0.0 and counted == sites and badges > 0
		# The plates' anchors: a diamond is drawn only clear of every badge,
		# and an anchor on a badge starts its stem at the badge's rim.
		var on_badges := 0
		for a: int in chart.plates():
			var plate := chart.plates()[a] as MapMarker
			var hit := {}
			for c: Dictionary in clusters:
				var r := layer.badge_radius_now(int(c["count"])) + 1.0 * layer.symbol_scale()
				if int(c["count"]) > 1 and (c["pos"] as Vector2).distance_to(plate.anchor) <= r + MapMarker.DOT_RADIUS + 1.5:
					hit = {"pos": c["pos"], "r": r}
					break
			if hit.is_empty():
				anchors_ok = anchors_ok and plate.draws_dot()
				continue
			on_badges += 1
			var stem := MapMarker.rim_point(plate.anchor, plate.get_rect().get_center(), hit["pos"], hit["r"])
			anchors_ok = anchors_ok and not plate.draws_dot() and plate.cover_centre == hit["pos"] \
				and (stem.distance_to(hit["pos"]) >= hit["r"] - 0.01 or plate.get_rect().has_point(stem))
		anchor_rows.append("%dx%d at %d %%: %d of 13 anchors on a badge" % [spec[0].x, spec[0].y,
			int(round(float(spec[1]) * 100.0)), on_badges])
	win.size = old_size
	win.content_scale_factor = old_factor
	await _frames(4)
	_check(ok and sites > 0, "geo map: no badge draws over another badge or a single site, every site counted once: %s" % ", ".join(rows))
	_check(anchors_ok and chart.plates().size() == 13,
		"geo map: no plate's anchor diamond hides a badge's numeral, the stem starts at the badge's rim: %s" % ", ".join(anchor_rows))
