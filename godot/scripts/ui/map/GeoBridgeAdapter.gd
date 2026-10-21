class_name GeoBridgeAdapter
extends RefCounted
## The national map's one door to the sim (geo stages 2-3 plan §3, Track C).
##
## Every read goes to SimBridge by the plan's exact name: Track A's sim·geo
## lane (get_geo_production_areas, get_geo_offshore_candidates,
## get_geo_area_at, get_geo_sites_signature, get_geo_sites, get_rival_summary)
## and Track B's sim·candidates lane (get_player_site_locations, and
## get_sea_candidates for the open-pen stars). Both lanes are merged, so
## nothing is stubbed: a facade without a read (no SimBridge,
## as in a bare scene) answers empty, never with invented data.
##
## get_geo_sites' "kind" bytes index its "kinds" table (track A: placement ×
## group, GeoLocalities' order); KIND_TABLE is the same table for a
## dictionary without one. display_class() folds a kind into the four icon
## classes the map draws.

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
	"get_geo_sites_signature", "get_geo_sites", "get_rival_summary", "get_player_site_locations",
	"get_sea_candidates"]
const AREA_COUNT := 13

var _sim: Node = null


func _init(sim: Node = null) -> void:
	_sim = sim


## True when the facade has the read (its lane is merged and a SimBridge is there).
func has_read(method: String) -> bool:
	return _sim != null and is_instance_valid(_sim) and _sim.has_method(method)


## The plan's reads the facade lacks (empty on geo/int with a SimBridge).
func missing() -> PackedStringArray:
	var out := PackedStringArray()
	for m in READS:
		if not has_read(m):
			out.append(m)
	return out


## The 13 areas: {id, code, name, official_traffic_light, tint, area_km2,
## sea_area_km2, label_x, label_y}, by id.
func production_areas() -> Array:
	return _sim.get_geo_production_areas() if has_read("get_geo_production_areas") else []


## The 33 offshore candidates, each with bound_plot_id ("hav_1" on the one
## the game's open-sea plot is bound to, else "").
func offshore_candidates() -> Array:
	return _sim.get_geo_offshore_candidates() if has_read("get_geo_offshore_candidates") else []


## The production area (1-13) at an EPSG:25833 point, 0 for none.
func area_at(x: float, y: float) -> int:
	return int(_sim.get_geo_area_at(x, y)) if has_read("get_geo_area_at") else 0


## Changes when the rival layout changes (get_geo_sites would differ).
func sites_signature() -> String:
	return str(_sim.get_geo_sites_signature()) if has_read("get_geo_sites_signature") else ""


## Every locality as packed arrays: {signature, ids, x, y, kind, po, mtb_t,
## owner, owners[{id, name, is_major}]} (plan §3), each array in the plan's
## packed type whatever the bridge marshals (a C# long[] arrives as a
## PackedInt64Array, a List<int> as an Array).
func sites() -> Dictionary:
	return normalize_sites(_sim.get_geo_sites() if has_read("get_geo_sites") else {})


## A get_geo_sites dictionary with every array in the plan's packed type.
static func normalize_sites(raw: Dictionary) -> Dictionary:
	var out := raw.duplicate()
	for key in ["ids", "x", "y", "owner"]:
		out[key] = _ints(raw.get(key, []))
	for key in ["kind", "po"]:
		out[key] = _bytes(raw.get(key, []))
	out["mtb_t"] = _floats(raw.get("mtb_t", []))
	out["owners"] = raw.get("owners", []) if raw.get("owners", []) is Array else []
	return out


static func _ints(v: Variant) -> PackedInt32Array:
	if v is PackedInt32Array:
		return v
	var out := PackedInt32Array()
	if v is Array or v is PackedInt64Array or v is PackedFloat32Array or v is PackedFloat64Array or v is PackedByteArray:
		for x: Variant in v:
			out.append(int(x))
	return out


static func _bytes(v: Variant) -> PackedByteArray:
	if v is PackedByteArray:
		return v
	var out := PackedByteArray()
	if v is Array or v is PackedInt32Array or v is PackedInt64Array:
		for x: Variant in v:
			out.append(int(x))
	return out


static func _floats(v: Variant) -> PackedFloat32Array:
	if v is PackedFloat32Array:
		return v
	var out := PackedFloat32Array()
	if v is Array or v is PackedFloat64Array or v is PackedInt32Array or v is PackedInt64Array:
		for x: Variant in v:
			out.append(float(x))
	return out


## One area's rival picture: {sites_by_kind, sea_growout_mtb_t, top_owners}.
func rival_summary(po: int) -> Dictionary:
	return _sim.get_rival_summary(po) if has_read("get_rival_summary") else {}


## The company's sea sites on the coast: {site_id, site_label, label, po,
## candidate_id, x, y, source ("candidate" exact, "anchor" approximate)}.
func player_site_locations() -> Array:
	return _sim.get_player_site_locations() if has_read("get_player_site_locations") else []


## Every area's 12 open-pen candidates in rank order (track B's
## get_sea_candidates per area): {id, po, rank, label (the place as track A
## cleans it), near (the label as shipped), score, x, y, taken_by_site_id
## (the company's site there, "" when free), ...}.
func open_pen_candidates() -> Array:
	var out: Array = []
	if has_read("get_sea_candidates"):
		for po in range(1, AREA_COUNT + 1):
			out.append_array(_sim.get_sea_candidates(po))
	return out


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
