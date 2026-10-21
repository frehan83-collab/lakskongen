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

var t: Node = null


func run(runner: Node, map: Control, chart: GeoMapView) -> void:
	t = runner
	_singular_forms(map, chart)
	_stars_read_the_bridge(chart)
	await _stars_follow_the_companys_places(chart)


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
## refresh: the star moves to the next free place and says one is the
## company's; the same state again records nothing; all twelve held, the
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
