extends RefCounted
## Geo stages 2-3, the captain's C2 merge (docs/geo/STAGE23_PLAN.md §4 step 3):
## what a pointer finds on the national map. GeoMapNationalChecks.gd runs
## these at both layouts (the default and the 1280x720 one, UI at 150 %).
##
## The review of geo/map-national-2 found the candidate hit-tests taking the
## first item in list order within the hit radius: the ringed hav_1 hexagon
## then answered with another candidate's tooltip and click id (Track D opens
## a card by that id) for most home areas. These checks pin:
## 1. every offshore candidate, ringed as hav_1's in turn, answers its own
##    tooltip with the hav_1 line, and a real click on it emits its own id;
## 2. every hexagon and star centre answers for itself (or for one drawn on
##    top of it on the same point);
## 3. the company's farms: each mark answers for itself, the hover ring sits
##    on the mark (not on the pointer), and farms that share a place are
##    spread apart so each keeps a disc of its own.

var t: Node = null


func run(runner: Node, chart: GeoMapView, where: String) -> void:
	t = runner
	_hexagons_answer_for_themselves(chart, where)
	await _every_ringed_hexagon(chart, where)
	_stars_answer_for_themselves(chart, where)
	_player_marks(chart, where)
	_crowded_farms(chart, where)


func _check(ok: bool, label: String) -> void:
	t._check(ok, label)


static func _id(item: Dictionary) -> String:
	match str(item.get("kind", "")):
		"hexagon":
			return "hexagon:" + str((item["row"] as Dictionary).get("id", ""))
		"candidates":
			return "star:%d" % int(item.get("po", 0))
		"player":
			return "player:" + str((item["row"] as Dictionary).get("site_id", ""))
	return str(item.get("kind", "none"))


## With the game's own binding: each hexagon's centre finds that hexagon, or
## one drawn after it (above) on the very same point.
func _hexagons_answer_for_themselves(chart: GeoMapView, where: String) -> void:
	var hexes := chart.candidate_layer().hexagons()
	var wrong := []
	for k in hexes.size():
		var h: Dictionary = hexes[k]
		var got := _id(chart.candidate_layer().item_at(h["pos"]))
		if got == "hexagon:" + str(h["row"]["id"]):
			continue
		var on_top := false
		for o: Dictionary in hexes.slice(k + 1):
			on_top = on_top or (got == "hexagon:" + str(o["row"]["id"]) and (o["pos"] as Vector2).distance_to(h["pos"]) < 0.01)
		if not on_top:
			wrong.append("%s->%s" % [str(h["row"]["id"]), got])
	_check(hexes.size() == 33 and wrong.is_empty(),
		"geo map: each of the 33 hexagons answers for itself at %s (%d wrong %s)" % [where, wrong.size(), str(wrong.slice(0, 4))])


## Ring each offshore candidate as hav_1's in turn (as a game in another home
## area binds it): its tooltip is its own with the hav_1 line, and a real click
## on its centre emits feature_clicked("hexagon", its id).
func _every_ringed_hexagon(chart: GeoMapView, where: String) -> void:
	var layer := chart.candidate_layer()
	var real: Array = chart.bridge.offshore_candidates()
	var origin := chart.get_global_rect().position
	var clicks := []
	var on_click := func(kind: String, id: String) -> void: clicks.append("%s:%s" % [kind, id])
	chart.feature_clicked.connect(on_click)
	var tip_wrong := []
	var click_wrong := []
	var covered := []
	for target: Dictionary in real:
		var id := str(target["id"])
		var rows := real.map(func(r: Dictionary) -> Dictionary:
			var c := r.duplicate()
			c["bound_plot_id"] = "hav_1" if str(r["id"]) == id else ""
			return c)
		layer.set_offshore_candidates(rows)
		var h: Dictionary = {}
		for o: Dictionary in layer.hexagons():
			if str(o["row"]["id"]) == id:
				h = o
		if h.is_empty() or not h["bound"] or layer.hexagons().back() != h:
			tip_wrong.append(id + " not ringed, or not drawn on top")
			continue
		var own := t.tr("MAP_GEO_TIP_HEXAGON") % [str(target.get("area", "")), str(target.get("near", "")),
			SimFormatters.num(float(target.get("depth_m", 0.0)))]
		var tip := chart._get_tooltip(h["pos"])
		if not tip.begins_with(own) or not tip.contains(t.tr("MAP_GEO_TIP_HAV1")):
			tip_wrong.append("%s: '%s'" % [id, tip.replace("\n", " / ")])
		# A real click, unless a plate, a chip or the key covers the point.
		var at: Vector2 = origin + h["pos"]
		if _covered(chart, h["pos"]):
			covered.append(id)
			continue
		clicks.clear()
		await t._click_at(at)
		if not clicks.has("hexagon:" + id):
			click_wrong.append("%s->%s" % [id, str(clicks)])
	chart.feature_clicked.disconnect(on_click)
	layer.set_offshore_candidates(real)
	_check(tip_wrong.is_empty(),
		"geo map: each offshore candidate, ringed as hav_1's, answers its own tooltip with the hav_1 line at %s (%d wrong %s)" % [
			where, tip_wrong.size(), str(tip_wrong.slice(0, 3))])
	_check(click_wrong.is_empty() and covered.size() < real.size(),
		"geo map: a real click on each ringed hexagon emits its own id at %s (%d wrong %s; %d under a plate or the key)" % [
			where, click_wrong.size(), str(click_wrong.slice(0, 3)), covered.size()])


## A chart point under a plate, a layer chip or the key (they take the click).
static func _covered(chart: GeoMapView, p: Vector2) -> bool:
	for plate: MapMarker in chart.plates().values():
		if plate.get_rect().has_point(p):
			return true
	for chip in chart.chips():
		if Rect2(chip.get_parent().position + chip.position, chip.size).has_point(p):
			return true
	var key := chart.legend()
	return key != null and key.get_rect().has_point(p)


func _stars_answer_for_themselves(chart: GeoMapView, where: String) -> void:
	var stars := chart.candidate_layer().star_groups()
	var wrong := []
	for k in stars.size():
		var s: Dictionary = stars[k]
		var got := _id(chart.feature_at(s["pos"]))
		if got != "star:%d" % int(s["po"]):
			wrong.append("PO%d->%s" % [int(s["po"]), got])
	_check(stars.size() == 13 and wrong.is_empty(),
		"geo map: each of the 13 stars answers for itself at %s %s" % [where, str(wrong.slice(0, 4))])


## The company's farms on the chart, and farms that share a place.
func _player_marks(chart: GeoMapView, where: String) -> void:
	var marks := chart.player_marks()
	var pts := marks.points()
	var wrong := []
	var ring_off := []
	for i in pts.size():
		var row: Dictionary = marks.rows()[i]
		var item := chart.feature_at(pts[i])
		if _id(item) != "player:" + str(row.get("site_id", "")):
			wrong.append("%s->%s" % [str(row.get("site_id", "")), _id(item)])
		var near := chart.feature_at(pts[i] + Vector2(5.0, 0.0))
		if str(near.get("kind", "")) == "player" and (near.get("pos", Vector2.INF) as Vector2).distance_to(pts[i]) > 0.01 \
				and _id(near) == "player:" + str(row.get("site_id", "")):
			ring_off.append(str(row.get("site_id", "")))
	var apart := true
	for i in pts.size():
		for j in range(i + 1, pts.size()):
			apart = apart and pts[i].distance_to(pts[j]) >= GeoMapView.PLAYER_SPREAD_PX - 0.5
	# The marks are the rows' own points, spread only where they crowd.
	var raw := PackedVector2Array()
	for row: Dictionary in marks.rows():
		raw.append(chart.utm_to_local(float(row.get("x", 0.0)), float(row.get("y", 0.0))))
	apart = apart and pts == GeoMapView.spread_marks(raw)
	_check(not pts.is_empty() and wrong.is_empty() and ring_off.is_empty() and apart,
		"geo map: each of the company's %d farms answers for itself, its hover ring on the mark, the marks apart at %s %s %s" % [
			pts.size(), where, str(wrong.slice(0, 3)), str(ring_off.slice(0, 3))])
	# Three farms on one anchor and one 0.9 px off it (as isa_utbrudd's
	# authored anchors lie): each gets a disc of its own around their place.
	var p := chart.drawn_rect().get_center()
	var same := PackedVector2Array([p, p, p + Vector2(0.9, 0.0), p + Vector2(40.0, 0.0)])
	var spread := GeoMapView.spread_marks(same)
	var min_gap := INF
	for i in 3:
		for j in range(i + 1, 3):
			min_gap = minf(min_gap, spread[i].distance_to(spread[j]))
	var centre := (spread[0] + spread[1] + spread[2]) / 3.0
	var was := (same[0] + same[1] + same[2]) / 3.0
	# Where two discs' reach overlaps, the pointer takes the nearer farm.
	var probe := GeoSiteLayer.PlayerMarks.new()
	probe.set_marks([{"site_id": "a"}, {"site_id": "b"}], PackedVector2Array([p, p + Vector2(GeoMapView.PLAYER_SPREAD_PX, 0.0)]))
	var nearer := probe.index_at(p + Vector2(GeoMapView.PLAYER_SPREAD_PX - 5.0, 0.0)) == 1 and probe.index_at(p + Vector2(5.0, 0.0)) == 0
	probe.free()
	_check(min_gap >= GeoMapView.PLAYER_SPREAD_PX - 0.5 and centre.distance_to(was) < 0.5 and spread[3] == same[3]
			and GeoMapView.spread_marks(same) == spread and nearer,
		"geo map: farms that share a place are spread %.1f px apart around it, a farm apart stays put, deterministically, and the pointer takes the nearer disc" % min_gap)


## Through the map's own refresh (live_update): three farms anchored on one
## spot inland, and one farm 5 px from PO3's star. The three are spread apart
## and each answers for itself; the star's centre still answers as the star
## (the nearer feature), the farm's as the farm.
func _crowded_farms(chart: GeoMapView, where: String) -> void:
	var real := chart.bridge
	var land := GeoProjection.utm33(GeoMapView.LAND_CENTRE_LONLAT.x, GeoMapView.LAND_CENTRE_LONLAT.y)
	var star: Dictionary = {}
	for s: Dictionary in chart.candidate_layer().star_groups():
		if int(s["po"]) == 3:
			star = s
	var by_star := chart.local_to_utm((star.get("pos", Vector2.ZERO) as Vector2) + Vector2(5.0, 0.0))
	var nudge := chart.local_to_utm(chart.utm_to_local(land[0], land[1]) + Vector2(0.9, 0.0))
	var rows := [_row("crowd_a", land[0], land[1]), _row("crowd_b", land[0], land[1]),
		_row("crowd_c", nudge[0], nudge[1]), _row("by_star", by_star[0], by_star[1])]
	var fake := _FakePlayers.new()
	fake.sim = t._sim
	fake.rows = rows
	chart.bridge = GeoBridgeAdapter.new(fake)
	chart.live_update(true)
	var marks := chart.player_marks()
	var pts := marks.points()
	var apart := pts.size() == 4
	for i in pts.size():
		for j in range(i + 1, pts.size()):
			apart = apart and pts[i].distance_to(pts[j]) >= GeoMapView.PLAYER_SPREAD_PX - 0.5
	var own := []
	for i in pts.size():
		var got := _id(chart.feature_at(pts[i]))
		if got != "player:" + str(rows[i]["site_id"]):
			own.append("%s->%s" % [str(rows[i]["site_id"]), got])
	var star_ok := not star.is_empty() and _id(chart.feature_at(star["pos"])) == "star:3"
	chart.bridge = real
	chart.live_update(true)
	fake.free()
	_check(apart and own.is_empty() and star_ok and marks.rows() == real.player_site_locations(),
		"geo map: three farms on one spot are spread apart and each answers for itself, and a star 5 px from a farm still answers as the star, at %s %s" % [
			where, str(own)])


static func _row(id: String, x: float, y: float) -> Dictionary:
	return {"site_id": id, "site_label": id, "label": "", "po": 3, "candidate_id": "", "x": x, "y": y, "source": "anchor"}


## The player's farms from the test, every other read from the game's own facade.
class _FakePlayers extends Node:
	var sim: Node = null
	var rows: Array = []
	func get_player_site_locations() -> Array: return rows
	func get_geo_sites_signature() -> String: return sim.get_geo_sites_signature()
	func get_geo_offshore_candidates() -> Array: return sim.get_geo_offshore_candidates()
	func get_sea_candidates(area: int) -> Array: return sim.get_sea_candidates(area)
