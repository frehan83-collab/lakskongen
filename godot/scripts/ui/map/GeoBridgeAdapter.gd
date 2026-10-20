class_name GeoBridgeAdapter
extends RefCounted
## The national map's one door to the sim (geo stages 2-3 plan §3, Track C).
##
## Every read goes to SimBridge by the plan's exact name: Track A's geo lane
## (get_geo_production_areas, get_geo_offshore_candidates, get_geo_area_at,
## get_geo_sites_signature, get_geo_sites, get_rival_summary) and Track B's
## get_player_site_locations. Until a lane is merged its read is missing from
## the facade, and the adapter answers from the STUB below instead.
##
## ── STUB ADAPTER (geo C2) ──────────────────────────────────────────────────
## The stubs read the same shipped files the sim embeds (godot/data/geo), so
## positions, kinds, counts and areas are real; what they cannot know is
## marked: no rival owners (owner -1 everywhere, no top owners) and the
## player's farms only approximately at their area's label point. The stub
## for get_geo_sites parses localities.geojson in GDScript once per process,
## which the plan forbids for the real path (§8): it exists only until Track
## A's packed arrays arrive. Delete the _stub_* functions and the STUB block
## when A and B are merged; nothing else changes.
## ───────────────────────────────────────────────────────────────────────────
##
## get_geo_sites' "kind" bytes: the index into "kinds" when the dictionary
## carries that table, else into KIND_TABLE (placement × group, the order of
## GeoLocalities' placements and groups). display_class() folds a kind into
## the four icon classes the map draws.

const KIND_TABLE: Array[String] = [
	"sea_salmonid", "sea_marine_fish", "sea_shellfish", "sea_algae", "sea_other",
	"land_salmonid", "land_marine_fish", "land_shellfish", "land_algae", "land_other",
	"offshore_salmonid", "offshore_marine_fish", "offshore_shellfish", "offshore_algae", "offshore_other",
]
## The map's icon classes (GEODATA §7.3): sea ring, land tank, offshore unit, other species.
enum { CLASS_SEA, CLASS_LAND, CLASS_OFFSHORE, CLASS_OTHER }
const CLASS_COUNT := 4
const CLASS_KEYS: Array[String] = ["sea", "land", "offshore", "other"]

## The facade reads the adapter makes (plan §3), for the evidence lines.
const READS: Array[String] = ["get_geo_production_areas", "get_geo_offshore_candidates", "get_geo_area_at",
	"get_geo_sites_signature", "get_geo_sites", "get_rival_summary", "get_player_site_locations"]

var _sim: Node = null


func _init(sim: Node = null) -> void:
	_sim = sim


## True when the facade has the real read (its lane is merged).
func has_real(method: String) -> bool:
	return _sim != null and is_instance_valid(_sim) and _sim.has_method(method)


## The reads still answered by the stub.
func stubbed() -> PackedStringArray:
	var out := PackedStringArray()
	for m in READS:
		if not has_real(m):
			out.append(m)
	return out


## The 13 areas: {id, code, name, official_traffic_light, tint, area_km2,
## sea_area_km2, label_x, label_y}, by id.
func production_areas() -> Array:
	if has_real("get_geo_production_areas"):
		return _sim.get_geo_production_areas()
	return _stub_production_areas()


## The 33 offshore candidates, each with bound_plot_id ("hav_1" on the one
## the game's open-sea plot is bound to, else "").
func offshore_candidates() -> Array:
	if has_real("get_geo_offshore_candidates"):
		return _sim.get_geo_offshore_candidates()
	return _stub_offshore_candidates()


## The production area (1-13) at an EPSG:25833 point, 0 for none.
func area_at(x: float, y: float) -> int:
	if has_real("get_geo_area_at"):
		return int(_sim.get_geo_area_at(x, y))
	return _stub_area_at(x, y)


## Changes when the rival layout changes (get_geo_sites would differ).
func sites_signature() -> String:
	if has_real("get_geo_sites_signature"):
		return str(_sim.get_geo_sites_signature())
	return str(_stub_sites().get("signature", ""))


## Every locality as packed arrays: {signature, ids, x, y, kind, po, mtb_t,
## owner, owners[{id, name, is_major}]} (plan §3).
func sites() -> Dictionary:
	if has_real("get_geo_sites"):
		return _sim.get_geo_sites()
	return _stub_sites()


## One area's rival picture: {sites_by_kind, sea_growout_mtb_t, top_owners}.
func rival_summary(po: int) -> Dictionary:
	if has_real("get_rival_summary"):
		return _sim.get_rival_summary(po)
	return _stub_rival_summary(po)


## The company's sea sites on the coast: {site_id, site_label, label, po,
## candidate_id, x, y, source ("candidate" exact, "anchor" approximate)}.
func player_site_locations() -> Array:
	if has_real("get_player_site_locations"):
		return _sim.get_player_site_locations()
	return _stub_player_site_locations()


# ── Kinds ─────────────────────────────────────────────────────────────────────

## The kind name of site i ("sea_salmonid" ...), "" when unknown.
static func kind_name(sites: Dictionary, i: int) -> String:
	var codes: PackedByteArray = sites.get("kind", PackedByteArray())
	if i < 0 or i >= codes.size():
		return ""
	var table: Array = sites.get("kinds", KIND_TABLE)
	var code := codes[i]
	return str(table[code]) if code < table.size() else ""


## The icon class of a kind name.
static func display_class(kind: String) -> int:
	match kind:
		"sea_salmonid":
			return CLASS_SEA
		"land_salmonid":
			return CLASS_LAND
		"offshore_salmonid":
			return CLASS_OFFSHORE
	return CLASS_OTHER


## Every site's icon class, in site order.
static func classes_of(sites: Dictionary) -> PackedByteArray:
	var codes: PackedByteArray = sites.get("kind", PackedByteArray())
	var table: Array = sites.get("kinds", KIND_TABLE)
	var by_code := PackedByteArray()
	for k in table:
		by_code.append(display_class(str(k)))
	var out := PackedByteArray()
	out.resize(codes.size())
	for i in codes.size():
		out[i] = by_code[codes[i]] if codes[i] < by_code.size() else CLASS_OTHER
	return out


## A rival summary's sites by kind as {kind: count}, whichever shape it has.
static func sites_by_kind(summary: Dictionary) -> Dictionary:
	var raw: Variant = summary.get("sites_by_kind", {})
	if raw is Dictionary:
		return raw
	var out := {}
	if raw is Array:
		for row: Variant in raw:
			if row is Dictionary:
				out[str(row.get("kind", row.get("key", "")))] = int(row.get("sites", row.get("count", row.get("value", 0))))
	return out


# ── STUB (geo C2: delete when Track A's and Track B's lanes are merged) ───────

static var _stub_cache: Dictionary = {}


func _stub_production_areas() -> Array:
	if _stub_cache.has("areas"):
		return _stub_cache["areas"]
	var out: Array = []
	for f: Dictionary in GeoData.production_areas("national"):
		var p: Dictionary = f["properties"]
		var label: Array = p.get("label_25833", [0, 0])
		out.append({
			"id": int(p["id"]), "code": str(p.get("code", "")), "name": str(p.get("name", "")),
			"official_traffic_light": str(p.get("traffic_light", "")), "tint": str(p.get("tint", "")),
			"area_km2": float(p.get("area_km2", 0.0)), "sea_area_km2": float(p.get("sea_area_km2", 0.0)),
			"label_x": float(label[0]), "label_y": float(label[1]),
		})
	_stub_cache["areas"] = out
	return out


func _stub_offshore_candidates() -> Array:
	var home := _stub_home_area()
	var key := "offshore:%d" % home
	if _stub_cache.has(key):
		return _stub_cache[key]
	var rows: Array = []
	for s: Dictionary in GeoData.candidates():
		if str(s.get("type", "")) != "offshore":
			continue
		rows.append({
			"id": str(s["id"]), "po": int(s.get("po", 0)), "rank": int(s.get("rank", 0)),
			"label": str(s.get("near", "")), "near": str(s.get("near", "")), "score": float(s.get("score", 0.0)),
			"area": str(s.get("area", "")), "stage": str(s.get("stage", "")),
			"stage_factor": float(s.get("stage_factor", 1.0)), "depth_m": float(s.get("depth_m", 0.0)),
			"x": float(s["x"]), "y": float(s["y"]), "lon": float(s.get("lon", 0.0)), "lat": float(s.get("lat", 0.0)),
			"bound_plot_id": "",
		})
	# hav_1 binds the candidate nearest the home area's label (OD1/OD2): the
	# least integer squared distance, then the higher score, the lower rank, the id.
	if home > 0:
		var area: Dictionary = _stub_production_areas()[home - 1]
		var best := -1
		for i in rows.size():
			if best < 0 or _stub_closer(rows[i], rows[best], area):
				best = i
		if best >= 0:
			rows[best]["bound_plot_id"] = "hav_1"
	_stub_cache[key] = rows
	return rows


static func _stub_closer(a: Dictionary, b: Dictionary, area: Dictionary) -> bool:
	var da := _stub_d2(a, area)
	var db := _stub_d2(b, area)
	if da != db:
		return da < db
	if a["score"] != b["score"]:
		return a["score"] > b["score"]
	if a["rank"] != b["rank"]:
		return a["rank"] < b["rank"]
	return str(a["id"]) < str(b["id"])


static func _stub_d2(c: Dictionary, area: Dictionary) -> int:
	var dx := int(c["x"]) - int(area["label_x"])
	var dy := int(c["y"]) - int(area["label_y"])
	return dx * dx + dy * dy


func _stub_home_area() -> int:
	if _sim == null or not _sim.has_method("get_expansion_overview"):
		return 0
	var best := 0
	var best_sites := 0
	for a: Dictionary in _sim.get_expansion_overview().get("areas", []):
		var n := 0
		for z: Dictionary in a.get("zones", []):
			n += int(z.get("player_sites", 0))
		if n > best_sites:
			best_sites = n
			best = int(a.get("production_area", 0))
	return best


func _stub_area_at(x: float, y: float) -> int:
	if not _stub_cache.has("polygons"):
		var b: Array = GeoData.basemap("national").get("bounds_25833", [0, 0, 0, 0])
		var polys: Array = []
		for f: Dictionary in GeoData.production_areas("regional"):
			var ring := PackedVector2Array()
			for ll: Array in (f["geometry"]["coordinates"] as Array)[0]:
				var xy := GeoProjection.utm33(float(ll[0]), float(ll[1]))
				ring.append(Vector2((xy[0] - float(b[0])) / 1000.0, (xy[1] - float(b[1])) / 1000.0))
			polys.append([int(f["properties"]["id"]), ring])
		_stub_cache["polygons"] = polys
		_stub_cache["origin"] = b
	var o: Array = _stub_cache["origin"]
	var p := Vector2((x - float(o[0])) / 1000.0, (y - float(o[1])) / 1000.0)
	for entry: Array in _stub_cache["polygons"]:
		if Geometry2D.is_point_in_polygon(p, entry[1]):
			return entry[0]
	return 0


func _stub_sites() -> Dictionary:
	if _stub_cache.has("sites"):
		return _stub_cache["sites"]
	var ids := PackedInt32Array()
	var xs := PackedInt32Array()
	var ys := PackedInt32Array()
	var kinds := PackedByteArray()
	var pos := PackedByteArray()
	var mtb := PackedFloat32Array()
	var owners := PackedInt32Array()
	for f: Dictionary in _stub_localities():
		var p: Dictionary = f["properties"]
		ids.append(int(p["id"]))
		xs.append(int(p["x"]))
		ys.append(int(p["y"]))
		kinds.append(maxi(KIND_TABLE.find(str(p.get("kind", ""))), 0))
		pos.append(int(p["po"]) if p.get("po") != null else 0)
		mtb.append(float(p["mtb_t"]) if p.get("mtb_t") != null else 0.0)
		owners.append(-1)
	var out := {"signature": "stub:%d" % ids.size(), "ids": ids, "x": xs, "y": ys, "kind": kinds,
		"po": pos, "mtb_t": mtb, "owner": owners, "owners": []}
	_stub_cache["sites"] = out
	return out


func _stub_rival_summary(po: int) -> Dictionary:
	var by_kind := {}
	var mtb := 0.0
	for f: Dictionary in _stub_localities():
		var p: Dictionary = f["properties"]
		if p.get("po") == null or int(p["po"]) != po:
			continue
		var kind := str(p.get("kind", ""))
		by_kind[kind] = int(by_kind.get(kind, 0)) + 1
		if kind == "sea_salmonid" and str(p.get("stage", "")) == "growout" and not bool(p.get("harvest_pen", false)) \
				and p.get("mtb_t") != null:
			mtb += float(p["mtb_t"])
	return {"sites_by_kind": by_kind, "sea_growout_mtb_t": mtb, "top_owners": []}


func _stub_player_site_locations() -> Array:
	if _sim == null or not _sim.has_method("get_all_pens") or not _sim.has_method("get_expansion_overview"):
		return []
	var zone_area := {}
	for a: Dictionary in _sim.get_expansion_overview().get("areas", []):
		for z: Dictionary in a.get("zones", []):
			zone_area[str(z.get("zone_id", ""))] = int(a.get("production_area", 0))
	var areas := _stub_production_areas()
	var out: Array = []
	var per_area := {}
	for site: Dictionary in _sim.get_all_pens():
		var po := int(zone_area.get(str(site.get("zone", "")), 0))
		if po < 1 or po > areas.size():
			continue
		var n := int(per_area.get(po, 0))
		per_area[po] = n + 1
		var area: Dictionary = areas[po - 1]
		out.append({"site_id": str(site.get("id", "")), "site_label": str(site.get("name", "")),
			"label": str(area["name"]), "po": po, "candidate_id": "",
			"x": float(area["label_x"]) + 4000.0 * n, "y": float(area["label_y"]) - 3000.0 * n, "source": "anchor"})
	return out


func _stub_localities() -> Array:
	var features: Variant = (GeoData.read_json("localities.geojson") as Dictionary).get("features", [])
	return features if features is Array else []
