class_name GeoRegionBridge
extends RefCounted
## ═══ TRACK-D STUB ADAPTER for the Track A and B bridge reads ═══
## (docs/geo/STAGE23_PLAN.md §3 "Geo bridge lane" and "Candidates bridge
## lane"). The region and site levels read the sim only through here:
##   A: get_geo_sites, get_geo_sites_signature, get_rival_summary,
##      get_zone_anchors, get_geo_offshore_candidates, get_geo_plot_binding
##   B: get_sea_candidates, get_player_site_locations, plan_site_build_at,
##      build_expansion_site_at, BuildSiteDialog.open_for_candidate
## Each read calls SimBridge when the lane is there (has_method) and falls
## back to a stub until it lands; stubbed() lists which reads are stubs now.
## At the merge of A and B every read goes live by itself: nothing here
## needs editing, and the stub branches can then be deleted (merge captain).
##
## The stubs are honest stand-ins, never invented rivals:
## - get_geo_sites / get_rival_summary / get_zone_anchors: empty, in the
##   lane's shape (no rival is drawn, the presence list says so);
## - get_sea_candidates: candidates.json's 12 open pens of the area by rank,
##   the zone a build would use and its verdict from the expansion overview
##   (the company's zone there with the most sites, else the area's catalogue
##   zone, as B's SeaBuildZoneOf), none taken, the first suggested;
## - get_player_site_locations: the company's sea sites at their area's label
##   point, marked approximate ("area");
## - get_geo_offshore_candidates: candidates.json's 33 offshore sites, the
##   hav_1 binding from get_geo_plot_binding when the sim has it, else none.
## Pure reads; `calls` counts every one (the hidden-map check: no geo read on
## a day signal while the map is closed).

## Every read through this adapter since the process started.
static var calls: int = 0
## The same, per read (the function's name).
static var reads: Dictionary = {}

const LANE_METHODS := {
	"geo_sites": "get_geo_sites",
	"geo_sites_signature": "get_geo_sites_signature",
	"rival_summary": "get_rival_summary",
	"zone_anchors": "get_zone_anchors",
	"offshore_candidates": "get_geo_offshore_candidates",
	"plot_binding": "get_geo_plot_binding",
	"sea_candidates": "get_sea_candidates",
	"player_site_locations": "get_player_site_locations",
	"plan_site_build_at": "plan_site_build_at",
	"build_expansion_site_at": "build_expansion_site_at",
}
## The localities' kinds when get_geo_sites carries no "kinds" list: Track A's
## order (GameBridge.Geo.cs GeoKinds, placement x group), which its kind bytes
## index. Track A's lane sends the list itself.
const DEFAULT_SITE_KINDS: Array[String] = [
	"sea_salmonid", "sea_marine_fish", "sea_shellfish", "sea_algae", "sea_other",
	"land_salmonid", "land_marine_fish", "land_shellfish", "land_algae", "land_other",
	"offshore_salmonid", "offshore_marine_fish", "offshore_shellfish", "offshore_algae", "offshore_other"]


## True when the sim has this lane read (a SimBridge method name).
static func live(sim: Object, method: String) -> bool:
	return sim != null and is_instance_valid(sim) and sim.has_method(method)


## The reads that fall back to their stub right now (LANE_METHODS keys).
static func stubbed(sim: Object) -> PackedStringArray:
	var out := PackedStringArray()
	for key: String in LANE_METHODS:
		if not live(sim, LANE_METHODS[key]):
			out.append(key)
	return out


# ── Track A ───────────────────────────────────────────────────────────────────

## get_geo_sites: {signature, ids, x, y, kind, po, mtb_t, owner, owners,
## kinds}. The stub is empty in the same shape.
static func geo_sites(sim: Object) -> Dictionary:
	_count("geo_sites")
	if live(sim, "get_geo_sites"):
		var d: Dictionary = sim.call("get_geo_sites")
		if not d.has("kinds"):
			d = d.duplicate()
			d["kinds"] = DEFAULT_SITE_KINDS
		return d
	return {"signature": "", "ids": PackedInt32Array(), "x": PackedInt32Array(), "y": PackedInt32Array(),
		"kind": PackedByteArray(), "po": PackedByteArray(), "mtb_t": PackedFloat32Array(),
		"owner": PackedInt32Array(), "owners": [], "kinds": DEFAULT_SITE_KINDS, "stub": true}


## get_geo_sites_signature: changes when the rival layout does ("" stubbed).
static func geo_sites_signature(sim: Object) -> String:
	_count("geo_sites_signature")
	if live(sim, "get_geo_sites_signature"):
		return String(sim.call("get_geo_sites_signature"))
	return ""


## get_rival_summary(po): {sites_by_kind, sea_growout_mtb_t, top_owners:
## [{id, name, sites, mtb_t, is_major}]}. The stub is empty.
static func rival_summary(sim: Object, po: int) -> Dictionary:
	_count("rival_summary")
	if live(sim, "get_rival_summary"):
		return sim.call("get_rival_summary", po)
	return {"sites_by_kind": {}, "sea_growout_mtb_t": 0.0, "top_owners": [], "stub": true}


## get_zone_anchors: [{zone_id, production_area, locality_id, name, x, y, source}].
static func zone_anchors(sim: Object) -> Array:
	_count("zone_anchors")
	if live(sim, "get_zone_anchors"):
		return sim.call("get_zone_anchors")
	return []


## get_geo_offshore_candidates: the 33 offshore sites, each with
## bound_plot_id ("hav_1" for the one hav_1 is bound to, else ""). The stub
## takes the binding from get_geo_plot_binding when the sim has it, else
## applies ruling OD2 itself for the home area (home_po): the candidate with
## the least squared distance from the area's label point (ties: the higher
## score, the lower rank, the ordinal id), as Track A's GeoSitePlotGeometry.
static func offshore_candidates(sim: Object, home_po: int = 0) -> Array:
	_count("offshore_candidates")
	if live(sim, "get_geo_offshore_candidates"):
		return sim.call("get_geo_offshore_candidates")
	var bound := ""
	if live(sim, "get_geo_plot_binding"):
		var b: Dictionary = sim.call("get_geo_plot_binding", "hav_1")
		if bool(b.get("bound", false)):
			bound = String(b.get("candidate_id", ""))
	else:
		bound = nearest_offshore_id(home_po)
	var out: Array = []
	for s: Dictionary in GeoData.candidates():
		if s.get("type", "") != "offshore":
			continue
		var row := s.duplicate()
		row["label"] = String(s.get("near", ""))
		row["bound_plot_id"] = "hav_1" if String(s["id"]) == bound else ""
		out.append(row)
	return out


# ── Track B ───────────────────────────────────────────────────────────────────

## get_sea_candidates(po): the area's 12 open pens by rank with the 'why
## here', who holds each, the zone a build there uses and its verdict.
static func sea_candidates(sim: Object, po: int) -> Array:
	_count("sea_candidates")
	if live(sim, "get_sea_candidates"):
		return sim.call("get_sea_candidates", po)
	if sim == null or not sim.has_method("get_expansion_overview"):
		return []
	var overview: Dictionary = sim.call("get_expansion_overview")
	var zone := _build_zone(overview, po)
	var weights: Dictionary = GeoData.rules().get("effective_weights", {})
	var out: Array = []
	var rows := GeoData.candidates().filter(func(s: Dictionary) -> bool:
		return s.get("type", "") == "open_pen" and int(s.get("po", 0)) == po)
	rows.sort_custom(func(a: Dictionary, b: Dictionary) -> bool: return int(a["rank"]) < int(b["rank"]))
	for s: Dictionary in rows:
		var row := s.duplicate(true)
		row["label"] = String(s.get("near", ""))
		row["weights"] = weights
		row["component_costs"] = component_costs(s, weights)
		row["taken_by_site_id"] = ""
		row["taken_by_site_label"] = ""
		row["suggested"] = out.is_empty()  # B: the first candidate whose own checks pass (none is taken here)
		row["build_zone_id"] = String(zone.get("zone_id", ""))
		row["can_build"] = bool(zone.get("can_build", false))
		row["build_reason_key"] = String(zone.get("build_reason_key", ""))
		row["build_label_key"] = String(zone.get("build_label_key", ""))
		row["stub"] = true
		out.append(row)
	return out


## get_player_site_locations: [{site_id, site_label, label, po, candidate_id,
## x, y, source}]. The stub puts each sea site at its area's label point.
static func player_site_locations(sim: Object) -> Array:
	_count("player_site_locations")
	if live(sim, "get_player_site_locations"):
		return sim.call("get_player_site_locations")
	if sim == null or not sim.has_method("get_all_pens") or not sim.has_method("get_expansion_overview"):
		return []
	var area_of_zone := {}
	for a: Dictionary in (sim.call("get_expansion_overview") as Dictionary).get("areas", []):
		for z: Dictionary in a.get("zones", []):
			area_of_zone[String(z.get("zone_id", ""))] = int(a.get("production_area", 0))
	var labels := {}
	for f: Dictionary in GeoData.production_areas("national"):
		labels[int(f["properties"]["id"])] = f["properties"]
	var out: Array = []
	for site: Dictionary in sim.call("get_all_pens"):
		var po := int(area_of_zone.get(String(site.get("zone", "")), 0))
		if not labels.has(po):
			continue
		var anchor: Array = labels[po].get("label_25833", [0, 0])
		out.append({"site_id": String(site.get("id", "")), "site_label": String(site.get("name", "")),
			"label": String(labels[po].get("name", "")), "po": po, "candidate_id": "",
			"x": int(anchor[0]), "y": int(anchor[1]), "source": "area"})
	return out


## Open the build dialog at a candidate: B's open_for_candidate, or (stub)
## the zone's own build with the candidate not preselected.
static func open_build_at(dialog: Control, zone_id: String, candidate_id: String) -> bool:
	if dialog == null:
		return false
	if dialog.has_method("open_for_candidate"):
		dialog.call("open_for_candidate", zone_id, candidate_id)
		return true
	dialog.call("open_for_zone", zone_id)
	return false


## What a build at a candidate would cost and add (B), or the zone's plan.
static func plan_site_build_at(sim: Object, zone_id: String, candidate_id: String) -> Dictionary:
	_count("plan_site_build_at")
	if live(sim, "plan_site_build_at"):
		return sim.call("plan_site_build_at", zone_id, candidate_id)
	return sim.call("plan_site_build", zone_id) if sim != null and sim.has_method("plan_site_build") else {}


# ── Shared ────────────────────────────────────────────────────────────────────

static func _count(read: String) -> void:
	calls += 1
	reads[read] = int(reads.get(read, 0)) + 1


## Ruling OD2's offshore candidate for a home area (stub only): "" for none.
static func nearest_offshore_id(home_po: int) -> String:
	var label: Array = []
	for f: Dictionary in GeoData.production_areas("national"):
		if int(f["properties"]["id"]) == home_po:
			label = f["properties"].get("label_25833", [])
	if label.size() != 2:
		return ""
	var best: Dictionary = {}
	var best_d := INF
	for s: Dictionary in GeoData.candidates():
		if s.get("type", "") != "offshore":
			continue
		var dx := float(s["x"]) - float(label[0])
		var dy := float(s["y"]) - float(label[1])
		var d := dx * dx + dy * dy
		if best.is_empty() or d < best_d or (d == best_d and _ranks_before(s, best)):
			best = s
			best_d = d
	return String(best.get("id", ""))


static func _ranks_before(a: Dictionary, b: Dictionary) -> bool:
	if float(a["score"]) != float(b["score"]):
		return float(a["score"]) > float(b["score"])
	if int(a["rank"]) != int(b["rank"]):
		return int(a["rank"]) < int(b["rank"])
	return String(a["id"]) < String(b["id"])


## The score each term costs a candidate (GEODATA §5.2): a component i costs
## w_i / sum(w) * (1 - c_i) * river factor; the river term costs the weighted
## mean * (1 - factor). B's bridge sends the engine's own; this is the stub's.
static func component_costs(site: Dictionary, weights: Dictionary) -> Dictionary:
	var components: Dictionary = site.get("components", {})
	var total := 0.0
	var mean := 0.0
	for k: String in weights:
		if components.has(k):
			total += float(weights[k])
	if total <= 0.0:
		return {}
	var factor := float(site.get("river_factor", 1.0))
	var out := {}
	for k: String in weights:
		if not components.has(k):
			continue
		var w := float(weights[k]) / total
		mean += w * float(components[k])
		out[k] = w * (1.0 - float(components[k])) * factor
	out["river"] = mean * (1.0 - factor)
	return out


## The zone a build in the area uses (B's SeaBuildZoneOf): the company's
## zone there with the most sites (ties by id), else the catalogue zone.
static func _build_zone(overview: Dictionary, po: int) -> Dictionary:
	for a: Dictionary in overview.get("areas", []):
		if int(a.get("production_area", 0)) != po:
			continue
		var best: Dictionary = {}
		var catalogue: Dictionary = {}
		for z: Dictionary in a.get("zones", []):
			if bool(z.get("in_catalogue", false)) and catalogue.is_empty():
				catalogue = z
			if not bool(z.get("in_game", false)):
				continue
			var sites := int(z.get("player_sites", 0))
			if best.is_empty() or sites > int(best.get("player_sites", 0)) \
					or (sites == int(best.get("player_sites", 0)) and String(z["zone_id"]) < String(best["zone_id"])):
				best = z
		return best if not best.is_empty() else catalogue
	return {}
