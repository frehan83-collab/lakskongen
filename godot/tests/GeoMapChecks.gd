extends RefCounted
## Geo stages 2-3, Track C (docs/geo/STAGE23_PLAN.md): the national map's
## foundation. UiFlowTests runs these through run(self) and counts them with
## its own _check. Headless: the data images are sampled raw on the CPU,
## never rendered.
##
## C1, GeoData and GeoProjection (godot/scripts/ui/map):
## 1. The loader: the 13 production areas at both levels of detail with int
##    ids, the 19 offshore areas, the 30 salmon fjords, the 189 candidates and
##    the rules; the 14 base maps at basemap.json's sizes; each file parsed
##    once and shared frozen; a bad request returns an empty value and says why.
## 2. The projection reproduces manifest.json's six pyproj reference points
##    within 1 cm and every candidate's x/y within 1 m; to_px subtracts the
##    origin in 64-bit; to_cell floors like test_geodata's //.
## 3. The raw po_index: L8 at basemap.json's size, every value within 0..13,
##    with test_geodata's histogram; the area labels sample their own area,
##    except PO1's and PO3's, which lie off the 800 m open-sea mask (plan §8).
## 4. The raw suitability grids: all 13 decode at suitability.json's sizes,
##    every open-pen candidate's cell is scored, and PO3's 12 candidate
##    lookups equal test_geodata's.
## tests/test_geo_map_contract.py pins PO_INDEX_COUNTS and PO3_CELLS against
## test_geodata.py's own PNG decoder, so both sides read the same bytes.

## Pixels per raw po_index value 0..13 (0 = land or outside every area).
const PO_INDEX_COUNTS := [3804775, 2038, 5830, 10259, 22494, 20811, 39611, 21844, 40542, 36022, 15798, 20273, 31120, 7659]
## The areas whose label point samples 0 in po_index.national.png.
const LABELS_OFF_MASK := [1, 3]
## PO3's open-pen candidates in suitability/po03.png: [column, row, R, G, B].
const PO3_CELLS := {
	"PO03-01": [131, 186, 241, 21, 26],
	"PO03-02": [66, 275, 241, 22, 29],
	"PO03-03": [111, 196, 241, 21, 20],
	"PO03-04": [76, 251, 240, 22, 11],
	"PO03-05": [135, 115, 238, 21, 57],
	"PO03-06": [150, 157, 238, 21, 58],
	"PO03-07": [73, 229, 235, 22, 13],
	"PO03-08": [271, 59, 232, 21, 59],
	"PO03-09": [46, 269, 230, 22, 28],
	"PO03-10": [63, 298, 230, 22, 6],
	"PO03-11": [69, 78, 230, 22, 34],
	"PO03-12": [230, 68, 230, 21, 37],
}
const AREAS := 13
const OPEN_PENS_PER_AREA := 12
const OFFSHORE_SITES := 33
const OFFSHORE_FEATURES := 19
const SALMON_FJORDS := 30
const OFFSHORE_STAGES := ["tilradning_2019", "anbefaling_2022", "konsekvensvurdering_2024", "kan_utlyses_2026"]
const TRAFFIC_LIGHTS := ["green", "yellow", "red"]
const SCORE_COMPONENTS := ["current", "depth", "exchange", "farm_distance", "oxygen", "temperature", "waves"]
const MANIFEST_TOLERANCE_M := 0.01
## Candidates carry lon/lat to 5 decimals (about 0.5 m): measured worst 0.58 m.
const CANDIDATE_TOLERANCE_M := 1.0

var t: Node = null


func run(runner: Node) -> void:
	t = runner
	var started := Time.get_ticks_msec()
	GeoData.reset_cache()
	_check(GeoData.parse_count == 0, "geo data: nothing is parsed before a layer is asked for")
	_loader_layers()
	_loader_cache_and_failures()
	_base_maps()
	_projection()
	_po_index()
	_suitability()
	# Leave no warm cache behind: the lazy-load checks that follow (Track D's
	# 'map closed, caches empty') must see a cold GeoData.
	GeoData.reset_cache()
	_check(GeoData.parse_count == 0 and GeoData.parses_of("candidates.json") == 0,
		"geo data: reset_cache leaves a cold cache for the checks that follow")
	print("  geo C1 checks took %d ms" % (Time.get_ticks_msec() - started))
	await t.get_tree().process_frame


func _check(ok: bool, label: String) -> void:
	t._check(ok, label)


# ── 1. The loader ─────────────────────────────────────────────────────────────

func _loader_layers() -> void:
	for lod in ["national", "regional"]:
		var areas := GeoData.production_areas(lod)
		var ids := areas.map(func(f: Dictionary) -> Variant: return f["properties"]["id"])
		_check(ids == range(1, AREAS + 1) and ids.all(func(i: Variant) -> bool: return typeof(i) == TYPE_INT),
			"geo data: the %s production areas are PO1..PO13 in id order, ids cast to int" % lod)
	var complete := true
	for f: Dictionary in GeoData.production_areas("national"):
		var p: Dictionary = f["properties"]
		var label: Array = p.get("label_25833", [])
		complete = complete and label.size() == 2 and String(p.get("name", "")) != "" \
			and String(p.get("tint", "")).begins_with("#") and p.get("traffic_light") in TRAFFIC_LIGHTS \
			and f["geometry"]["type"] == "Polygon"
	_check(complete, "geo data: each area carries a name, a tint, a source traffic light and a label anchor")

	for lod in ["national", "regional"]:
		var offshore := GeoData.offshore_areas(lod)
		var staged := true
		for f: Dictionary in offshore:
			var p: Dictionary = f["properties"]
			staged = staged and p["stage"] in OFFSHORE_STAGES and typeof(p["area_id"]) == TYPE_INT
		_check(offshore.size() == OFFSHORE_FEATURES and staged,
			"geo data: %d %s offshore areas, each with its stage and an int area_id (%d)" % [OFFSHORE_FEATURES, lod, offshore.size()])

	var fjords := GeoData.salmon_fjords()
	var named := true
	for f: Dictionary in fjords:
		named = named and f["properties"]["kind"] == "salmon_fjord" and String(f["properties"]["name"]) != ""
	_check(fjords.size() == SALMON_FJORDS and named,
		"geo data: the 30 national salmon fjords are exclusions.national's salmon_fjord subset (%d)" % fjords.size())

	var per_area := {}
	var offshore_sites := 0
	var typed := true
	for s: Dictionary in GeoData.candidates():
		typed = typed and typeof(s["po"]) == TYPE_INT and typeof(s["rank"]) == TYPE_INT
		if s["type"] == "offshore":
			offshore_sites += 1
		elif s["type"] == "open_pen":
			per_area[s["po"]] = int(per_area.get(s["po"], 0)) + 1
	var twelve_each := per_area.size() == AREAS
	for n: int in per_area.values():
		twelve_each = twelve_each and n == OPEN_PENS_PER_AREA
	_check(GeoData.candidates().size() == AREAS * OPEN_PENS_PER_AREA + OFFSHORE_SITES and twelve_each
			and offshore_sites == OFFSHORE_SITES and typed,
		"geo data: 189 candidates, 12 open pens in each of the 13 areas and 33 offshore, po and rank cast to int")

	var weights: Dictionary = GeoData.rules().get("effective_weights", {})
	var keys := weights.keys()
	keys.sort()
	_check(keys == SCORE_COMPONENTS and weights.values().all(func(w: Variant) -> bool: return float(w) > 0.0),
		"geo data: the rules carry the seven effective score weights (%s)" % ", ".join(keys))


func _loader_cache_and_failures() -> void:
	var areas := GeoData.production_areas("national")
	var count := GeoData.parse_count
	var again := GeoData.production_areas("national")
	var files := ["production_areas.national.geojson", "production_areas.regional.geojson",
		"offshore_areas.national.geojson", "offshore_areas.regional.geojson",
		"exclusions.national.geojson", "candidates.json", "suitability_rules.json"]
	_check(is_same(again, areas) and GeoData.parse_count == count and count == files.size()
			and files.all(func(rel: String) -> bool: return GeoData.parses_of(rel) == 1),
		"geo data: each of the 7 files read so far was parsed once; a second read is the same cached array (parse_count %d)" % count)

	var p: Dictionary = areas[0]["properties"]
	_check(areas.is_read_only() and (areas[0] as Dictionary).is_read_only() and p.is_read_only()
			and (p["label_25833"] as Array).is_read_only() and GeoData.candidates().is_read_only()
			and (GeoData.candidates()[0]["components"] as Dictionary).is_read_only(),
		"geo data: the cache is shared frozen, down to a label anchor and a candidate's components")

	GeoData.last_error = ""
	var missing: Variant = GeoData.read_json("no_such_layer.geojson")
	var missing_error := GeoData.last_error
	GeoData.last_error = ""
	var escaping: Variant = GeoData.read_json("../project.godot")
	_check(missing is Dictionary and (missing as Dictionary).is_empty() and missing_error.contains("missing")
			and escaping is Dictionary and (escaping as Dictionary).is_empty() and GeoData.last_error.contains("not a file under"),
		"geo data: read_json returns {} and says why for a missing file and for a path out of data/geo")

	var errors := PackedStringArray()
	GeoData.last_error = ""
	var bad_lod := GeoData.production_areas("local").is_empty() and GeoData.offshore_areas("").is_empty()
	errors.append(GeoData.last_error)
	GeoData.last_error = ""
	var bad_po := GeoData.suitability_grid(0).is_empty() and GeoData.suitability_grid(AREAS + 1).is_empty()
	errors.append(GeoData.last_error)
	GeoData.last_error = ""
	var bad_key := GeoData.basemap("po14").is_empty() and GeoData.basemap_texture("po14") == null
	errors.append(GeoData.last_error)
	_check(bad_lod and bad_po and bad_key and "" not in errors,
		"geo data: an unknown level of detail, area or base map returns an empty value and records why")

	GeoData.last_error = ""
	var not_png := GeoData.load_data_png("basemap/national.jpg") == null
	var not_png_error := GeoData.last_error
	GeoData.last_error = ""
	var absent := GeoData.load_data_png("suitability/po14.png") == null
	_check(not_png and not_png_error != "" and absent and GeoData.last_error.contains("missing"),
		"geo data: load_data_png refuses a picture and a missing grid, and says why")


func _base_maps() -> void:
	var national := GeoData.basemap("national")
	var b: Array = national["bounds_25833"]
	_check(national["width"] == 1866 and national["height"] == 2186 and national["res_m"] == 800.0
			and float(b[2]) - float(b[0]) == national["width"] * national["res_m"]
			and float(b[3]) - float(b[1]) == national["height"] * national["res_m"] and not national["is_data"],
		"geo data: basemap('national') is 1866x2186 at 800 m over its bounds")
	var count := GeoData.parse_count
	var texture := GeoData.basemap_texture("national")
	_check(texture != null and not texture is ImageTexture and Vector2i(texture.get_size()) == Vector2i(1866, 2186)
			and GeoData.parse_count == count + 1 and GeoData.basemap_texture("national") == texture,
		"geo data: the national base map loads once as its imported texture, at basemap.json's size")
	var wrong := PackedStringArray()
	for po in range(1, AREAS + 1):
		var key := "po%02d" % po
		var info := GeoData.basemap(key)
		var tex := GeoData.basemap_texture(key)
		if info["po"] != po or tex == null or Vector2i(tex.get_size()) != Vector2i(info["width"], info["height"]):
			wrong.append(key)
	_check(wrong.is_empty(), "geo data: the 13 area base maps load at basemap.json's sizes (wrong: %s)" % ", ".join(wrong))
	var index := GeoData.basemap("po_index_national")
	var index_texture := GeoData.basemap_texture("po_index_national")
	_check(index["is_data"] and index_texture is ImageTexture
			and Vector2i(index_texture.get_size()) == Vector2i(index["width"], index["height"])
			and GeoData.parses_of(index["file"]) == 1,
		"geo data: the po_index data PNG becomes an ImageTexture of its raw bytes, decoded once")


# ── 2. The projection ─────────────────────────────────────────────────────────

func _projection() -> void:
	var points: Array = (GeoData.read_json("manifest.json") as Dictionary).get("projection_check_25833", [])
	_check(points.size() == 6, "geo projection: manifest.json lists the 6 pyproj reference points")
	for p: Dictionary in points:
		var xy := GeoProjection.utm33(p["lon"], p["lat"])
		var dx := absf(xy[0] - float(p["x"]))
		var dy := absf(xy[1] - float(p["y"]))
		_check(dx < MANIFEST_TOLERANCE_M and dy < MANIFEST_TOLERANCE_M,
			"geo projection: utm33(%.4f, %.4f) lands within 1 cm of pyproj (off %.5f m, %.5f m)" % [p["lon"], p["lat"], dx, dy])

	var worst := 0.0
	for s: Dictionary in GeoData.candidates():
		var xy := GeoProjection.utm33(s["lon"], s["lat"])
		worst = maxf(worst, maxf(absf(xy[0] - float(s["x"])), absf(xy[1] - float(s["y"]))))
	_check(worst < CANDIDATE_TOLERANCE_M,
		"geo projection: all 189 candidates' lon/lat project within 1 m of their x/y (worst %.3f m)" % worst)

	var b: Array = GeoData.basemap("national")["bounds_25833"]
	# 0.1 m pixels 1000.3 m inside the north-west corner, at a northing near
	# 8e6 m where a 32-bit float steps 0.5 m: only a 64-bit subtraction gets
	# the exact pixel.
	var fine := GeoProjection.to_px(float(b[0]) + 1000.3, float(b[3]) - 1000.3, b, 0.1)
	_check(fine.distance_to(Vector2(10003.0, 10003.0)) < 0.01,
		"geo projection: to_px subtracts the origin in 64-bit before narrowing (%s)" % fine)

	var label: Array = GeoData.production_areas("national")[3]["properties"]["label_25833"]
	var px := GeoProjection.to_px(label[0], label[1], b, 800.0)
	var back := GeoProjection.to_utm(px, b, 800.0)
	_check(Vector2i(px) == Vector2i(209, 1516) and absf(back[0] - float(label[0])) < 0.1 and absf(back[1] - float(label[1])) < 0.1,
		"geo projection: PO4's label anchor sits at national pixel (209, 1516) and to_utm inverts to_px")

	# PO3's 500 m grid bounds: origin (-92250, 6757500), 422 x 320 cells.
	var gb := [-92250.0, 6597500.0, 118750.0, 6757500.0]
	var edge_x := float(gb[0]) + 131 * 500.0
	var edge_y := float(gb[3]) - 186 * 500.0
	_check(GeoProjection.to_cell(edge_x, edge_y, gb, 500.0) == Vector2i(131, 186)
			and GeoProjection.to_cell(edge_x - 0.001, edge_y + 0.001, gb, 500.0) == Vector2i(130, 185)
			and GeoProjection.to_cell(float(gb[0]) - 1.0, float(gb[3]) + 1.0, gb, 500.0) == Vector2i(-1, -1),
		"geo projection: to_cell floors on and beside a cell's edges, and west of or above the grid, like test_geodata's //")


# ── 3. The raw po_index ───────────────────────────────────────────────────────

func _po_index() -> void:
	var info := GeoData.basemap("po_index_national")
	var image := GeoData.po_index_image()
	_check(image != null and image.get_format() == Image.FORMAT_L8
			and image.get_size() == Vector2i(info["width"], info["height"]),
		"geo po_index: the raw image is 8-bit grey (L8) at basemap.json's 1866x2186")
	if image == null:
		return
	var data := image.get_data()
	var counts := []
	var total := 0
	for value in AREAS + 1:
		counts.append(data.count(value))
		total += counts[value]
	_check(total == data.size(), "geo po_index: every raw value lies in 0..13 (%d of %d pixels)" % [total, data.size()])
	_check(counts == PO_INDEX_COUNTS, "geo po_index: the raw histogram equals test_geodata's decode, all 13 areas present")

	var b: Array = info["bounds_25833"]
	var res: float = info["res_m"]
	var sampled := []
	var expected := []
	for f: Dictionary in GeoData.production_areas("national"):
		var area: int = f["properties"]["id"]
		var label: Array = f["properties"]["label_25833"]
		var cell := GeoProjection.to_cell(label[0], label[1], b, res)
		sampled.append(image.get_pixel(cell.x, cell.y).r8)
		expected.append(0 if area in LABELS_OFF_MASK else area)
	_check(sampled == expected,
		"geo po_index: the label anchors sample their own area, PO1's and PO3's sample 0 off the open-sea mask (%s)" % str(sampled))


# ── 4. The raw suitability grids ──────────────────────────────────────────────

func _suitability() -> void:
	var records: Dictionary = (GeoData.read_json("suitability.json") as Dictionary).get("grids", {})
	var wrong := PackedStringArray()
	for po in range(1, AREAS + 1):
		var grid := GeoData.suitability_grid(po)
		var record: Dictionary = records.get(str(po), {})
		if grid.is_empty() or grid["image"].get_size() != Vector2i(int(record["width"]), int(record["height"])) \
				or grid["image"].get_format() != Image.FORMAT_RGB8:
			wrong.append(str(po))
	_check(wrong.is_empty(), "geo suitability: all 13 grids decode raw as RGB8 at suitability.json's sizes (wrong: %s)" % ", ".join(wrong))

	var scored := 0
	var open_pens := 0
	var po3 := []
	for s: Dictionary in GeoData.candidates():
		if s["type"] != "open_pen":
			continue
		open_pens += 1
		var pixel := _suitability_cell(s)
		if pixel.size() == 5 and pixel[2] > 0:
			scored += 1
		if s["po"] == 3:
			po3.append(s)
	_check(scored == open_pens and open_pens == AREAS * OPEN_PENS_PER_AREA,
		"geo suitability: every open-pen candidate's raw cell is scored (R > 0: %d of %d)" % [scored, open_pens])

	var ids := po3.map(func(s: Dictionary) -> String: return s["id"])
	ids.sort()
	var pinned := PO3_CELLS.keys()
	pinned.sort()
	_check(ids == pinned, "geo suitability: PO3 has exactly the 12 pinned open-pen candidates")
	for s: Dictionary in po3:
		var got := _suitability_cell(s)
		_check(got == PO3_CELLS.get(s["id"], []),
			"geo suitability: %s's raw po03.png cell equals test_geodata's (col, row, R, G, B = %s)" % [s["id"], str(got)])


## [column, row, R, G, B] of a candidate's cell in its area's raw grid, [] off it.
func _suitability_cell(site: Dictionary) -> Array:
	var grid := GeoData.suitability_grid(site["po"])
	if grid.is_empty():
		return []
	var image: Image = grid["image"]
	var cell := GeoProjection.to_cell(site["x"], site["y"], grid["bounds_25833"], grid["res_m"])
	if cell.x < 0 or cell.y < 0 or cell.x >= image.get_width() or cell.y >= image.get_height():
		return []
	var c := image.get_pixel(cell.x, cell.y)
	return [cell.x, cell.y, c.r8, c.g8, c.b8]
