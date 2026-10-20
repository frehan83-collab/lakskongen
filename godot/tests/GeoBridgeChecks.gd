extends RefCounted
## Geo stages 2-3, track A (docs/geo/STAGE23_PLAN.md §3 "Geo bridge lane"):
## the sim·geo lane's reads run in the real game, through GameBridge.Geo.cs
## (C#), SimProxies and SimBridge, headless. Data only: the map that draws
## them is tracks C and D. UiFlowTests runs these through run(self) and counts
## them with its own _check.
##
## 1. The 13 production areas with the source's traffic lights.
## 2. hav_1 is bound to a Havbruk til havs candidate; off_1 is not.
## 3. The 33 offshore candidates, exactly one bound to hav_1, outside every area.
## 4. Every locality as packed arrays, by kind; the game's own anchors unowned.
## 5. The PO3 rival summary: at least 2 owners, every owner name TextLint-clean.
## 6. The scenario's zones and the catalogue zones carry anchors in their area.
## 7. The 3D rival list keeps its six companies, ordered by home-area sites.
## 8. None of these reads changes the engine (qa_state_digest).
## 9. A game with no scenario on the same engine (GameBridge keeps one for the
##    session): the 33 offshore candidates are listed with no home distance
##    and none bound, and hav_1 is back at site_plots.json's position and
##    depth (captain's review of track A, findings 1 and 2).

const SCENARIO := "po3_red_sone"
const SEED := 91
const AREAS := 13
const GREEN_AREAS := [1, 12, 13]
const RED_AREAS := [3]
const OFFSHORE_CANDIDATES := 33
const LOCALITIES := 1781
## Kind counts in localities.geojson (GEODATA §2.1): sea, land and offshore salmonid, the rest other species.
const SALMONID_COUNTS := {"sea_salmonid": 1070, "land_salmonid": 309, "offshore_salmonid": 1}
const OTHER_SPECIES := 401
const MAJORS := 6
## distance_km of an offshore candidate when the game has no home area (GameBridge.Geo.cs NoHomeDistanceKm).
const NO_HOME_DISTANCE_KM := -1.0
const SITE_PLOTS := "res://data/site_plots.json"

const TextLint := preload("res://tests/TextLint.gd")

var t: Node = null


func run(runner: Node) -> void:
	t = runner
	var sim: Node = t._sim
	_production_areas(sim)
	_check(sim.start_qa_scenario(SCENARIO, SEED), "geo bridge: seeded scenario starts")
	sim.set_paused(true)
	var digest: String = sim.qa_state_digest()
	_plot_binding_and_candidates(sim)
	_sites_and_owners(sim)
	_rival_summary(sim)
	_zone_anchors(sim)
	_competitor_order(sim)
	_check(sim.qa_state_digest() == digest, "geo bridge: every geo read leaves the engine state digest unchanged")
	_no_scenario_game_after_the_scenario(sim)
	await t.get_tree().process_frame


func _check(ok: bool, label: String) -> void:
	t._check(ok, label)


func _production_areas(sim: Node) -> void:
	var areas: Array = sim.get_geo_production_areas()
	var ids := areas.map(func(a: Dictionary) -> int: return int(a.get("id", 0)))
	var lights_ok := areas.size() == AREAS
	for a in areas:
		var id := int(a.get("id", 0))
		var want := "green" if GREEN_AREAS.has(id) else ("red" if RED_AREAS.has(id) else "yellow")
		lights_ok = lights_ok and str(a.get("official_traffic_light", "")) == want \
			and str(a.get("code", "")) == "PO%d" % id and str(a.get("name", "")) != "" \
			and str(a.get("tint", "")).begins_with("#") and float(a.get("sea_area_km2", 0.0)) > 0.0
	_check(ids == range(1, AREAS + 1) and lights_ok,
		"geo bridge: 13 production areas with the source's lights (PO1/12/13 green, PO3 red, the rest yellow)")


func _plot_binding_and_candidates(sim: Node) -> void:
	var hav: Dictionary = sim.get_geo_plot_binding("hav_1")
	var candidate_id := str(hav.get("candidate_id", ""))
	_check(bool(hav.get("bound", false)) and candidate_id.begins_with("HTH") and float(hav.get("depth_m", 0.0)) > 100.0
		and float(hav.get("distance_km", 0.0)) > 0.0 and str(hav.get("area", "")) != "",
		"geo bridge: hav_1 is bound to a Havbruk til havs candidate (%s, %s, %.0f km)" % [candidate_id, str(hav.get("area", "")), float(hav.get("distance_km", 0.0))])
	var off: Dictionary = sim.get_geo_plot_binding("off_1")
	_check(not bool(off.get("bound", true)) and str(off.get("plot_id", "")) == "off_1",
		"geo bridge: off_1 stays procedural (not bound)")
	var candidates: Array = sim.get_geo_offshore_candidates()
	var bound := candidates.filter(func(c: Dictionary) -> bool: return str(c.get("bound_plot_id", "")) == "hav_1")
	var outside := true
	for c in candidates:
		outside = outside and int(sim.get_geo_area_at(float(c.get("x", 0)), float(c.get("y", 0)))) == 0
	_check(candidates.size() == OFFSHORE_CANDIDATES and bound.size() == 1 and str(bound[0].get("id", "")) == candidate_id and outside,
		"geo bridge: 33 offshore candidates, exactly one bound to hav_1, all outside every production area")


func _sites_and_owners(sim: Node) -> void:
	var sites: Dictionary = sim.get_geo_sites()
	var ids: PackedInt32Array = sites.get("ids", PackedInt32Array())
	var kind: PackedByteArray = sites.get("kind", PackedByteArray())
	var kinds: PackedStringArray = sites.get("kinds", PackedStringArray())
	var owner: PackedInt32Array = sites.get("owner", PackedInt32Array())
	var owners: Array = sites.get("owners", [])
	var lengths_ok := ids.size() == LOCALITIES
	for key in ["x", "y", "kind", "stage", "po", "mtb_t", "owner", "names"]:
		var packed: Variant = sites.get(key, [])
		lengths_ok = lengths_ok and packed.size() == LOCALITIES
	var counts := {}
	var owned := 0
	for i in ids.size():
		var k := kinds[kind[i]]
		counts[k] = int(counts.get(k, 0)) + 1
		if owner[i] >= 0:
			owned += 1
	var other := 0
	for k in counts:
		if not SALMONID_COUNTS.has(k):
			other += int(counts[k])
	var counts_ok := other == OTHER_SPECIES
	for k in SALMONID_COUNTS:
		counts_ok = counts_ok and int(counts.get(k, 0)) == int(SALMONID_COUNTS[k])
	_check(lengths_ok and counts_ok,
		"geo bridge: get_geo_sites packs all 1,781 localities (sea %d, land %d, offshore %d salmonid, %d other)" % [
			int(counts.get("sea_salmonid", 0)), int(counts.get("land_salmonid", 0)), int(counts.get("offshore_salmonid", 0)), other])
	var majors := owners.filter(func(o: Dictionary) -> bool: return bool(o.get("is_major", false)))
	var anchors_unowned := true
	for a in sim.get_zone_anchors():
		if bool(a.get("in_game", false)):
			var at := ids.find(int(a.get("locality_id", 0)))
			anchors_unowned = anchors_unowned and at >= 0 and owner[at] == -1
	_check(majors.size() == MAJORS and owners.size() > MAJORS and owned > 1000 and anchors_unowned
		and str(sites.get("signature", "")) != "" and sim.get_geo_sites_signature() == str(sites.get("signature", "")),
		"geo bridge: %d localities owned by %d owners (6 majors), the game's anchors unowned, the signature matches" % [owned, owners.size()])
	var lint := PackedStringArray()
	for o in owners:
		for finding in TextLint.check_text(str(o.get("name", "")), true):
			lint.append("%s: %s" % [str(o.get("name", "")), str(finding)])
	_check(lint.is_empty(), "geo bridge: every rival owner's name passes TextLint (%d owners) %s" % [owners.size(), str(lint)])


func _rival_summary(sim: Node) -> void:
	var summary: Dictionary = sim.get_rival_summary(3)
	var top: Array = summary.get("top_owners", [])
	var ordered := true
	for i in range(1, top.size()):
		ordered = ordered and int(top[i - 1].get("sites", 0)) >= int(top[i].get("sites", 0))
	var by_kind: Dictionary = summary.get("sites_by_kind", {})
	_check(top.size() >= 2 and ordered and int(by_kind.get("sea_salmonid", 0)) > 100 and float(summary.get("sea_growout_mtb_t", 0.0)) > 100000.0,
		"geo bridge: PO3's rival summary lists %d owners by sites (%s) and %.0f t of sea grow-out MTB" % [
			top.size(), ", ".join(top.map(func(o: Dictionary) -> String: return str(o.get("name", "")))), float(summary.get("sea_growout_mtb_t", 0.0))])


func _zone_anchors(sim: Node) -> void:
	var anchors: Array = sim.get_zone_anchors()
	var game := anchors.filter(func(a: Dictionary) -> bool: return bool(a.get("in_game", false)))
	var game_ok := game.map(func(a: Dictionary) -> String: return str(a.get("zone_id", ""))) == ["po3_nord", "po3_sor"]
	for a in game:
		game_ok = game_ok and int(a.get("production_area", 0)) == 3 and str(a.get("source", "")) == "authored" \
			and int(sim.get_geo_area_at(float(a.get("x", 0)), float(a.get("y", 0)))) == 3
	var catalogue := anchors.filter(func(a: Dictionary) -> bool: return not bool(a.get("in_game", true)))
	var catalogue_ok := catalogue.size() == AREAS
	for a in catalogue:
		catalogue_ok = catalogue_ok and int(a.get("locality_id", 0)) > 0 and ["authored", "municipality", "fallback"].has(str(a.get("source", "")))
	_check(game_ok and catalogue_ok,
		"geo bridge: the scenario's two zones are anchored at authored PO3 localities, and the 13 catalogue zones resolve")


func _competitor_order(sim: Node) -> void:
	var farms: Array = sim.get_competitor_farms()
	var ids := farms.map(func(f: Dictionary) -> String: return str(f.get("id", "")))
	var sorted_ids := ids.duplicate()
	sorted_ids.sort()
	var ordered := true
	for i in range(1, farms.size()):
		ordered = ordered and int(farms[i - 1].get("home_po_sites", -1)) >= int(farms[i].get("home_po_sites", -1))
	_check(sorted_ids == ["greed_gregarious", "heroy", "mowier", "nordlaks_nord", "offshore_optimists", "salmax"] and ordered
		and int(farms[0].get("home_po_sites", 0)) > 0,
		"geo bridge: the 3D rival list keeps its six companies, most PO3 sites first (%s)" % ", ".join(
			farms.map(func(f: Dictionary) -> String: return "%s %d" % [str(f.get("id", "")), int(f.get("home_po_sites", -1))])))


## A game with no scenario, started on the engine that just ran SCENARIO
## (GameBridge keeps one engine for the session): its home area is 0.
func _no_scenario_game_after_the_scenario(sim: Node) -> void:
	_check(sim.start_qa_new_game("Player", 10_000_000, SEED), "geo bridge: a game with no scenario starts on the same engine")
	sim.set_paused(true)
	var candidates: Array = sim.get_geo_offshore_candidates()
	var listed := candidates.size() == OFFSHORE_CANDIDATES
	for c in candidates:
		listed = listed and str(c.get("id", "")).begins_with("HTH") and int(c.get("rank", 0)) >= 1 \
			and not bool(c.get("bound", true)) and str(c.get("bound_plot_id", "?")) == "" \
			and float(c.get("distance_km", 0.0)) == NO_HOME_DISTANCE_KM
	_check(listed, "geo bridge: with no home area all %d offshore candidates are listed, none bound, distance_km %.0f" % [
		candidates.size(), NO_HOME_DISTANCE_KM])
	var file_hav := _file_slot("hav_1")
	var hav := {}
	for p in sim.get_build_plots("hav"):
		if str(p.get("id", "")) == "hav_1":
			hav = p
	var binding: Dictionary = sim.get_geo_plot_binding("hav_1")
	_check(not file_hav.is_empty() and not hav.is_empty() and not bool(binding.get("bound", true))
		and float(hav.get("z", 0.0)) == float(file_hav.get("z", 1.0))
		and float(hav.get("water_depth_m", 0.0)) == float(file_hav.get("water_depth_m", 1.0)),
		"geo bridge: a new game after a scenario is back on the default catalogue (hav_1 unbound at z %s, %s m deep)" % [
			str(hav.get("z", "?")), str(hav.get("water_depth_m", "?"))])


## An offshore slot of godot/data/site_plots.json (the default catalogue), or {}.
func _file_slot(slot_id: String) -> Dictionary:
	var data: Variant = JSON.parse_string(FileAccess.get_file_as_string(SITE_PLOTS))
	if data is Dictionary:
		for slot in data.get("offshore_slots", []):
			if slot is Dictionary and str(slot.get("id", "")) == slot_id:
				return slot
	return {}
