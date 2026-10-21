class_name GeoData
extends RefCounted
## The national map data (godot/data/geo, docs/geo/GEODATA.md) for the map UI:
## a static, lazy, parse-once cache (geo stages 2-3 plan Track C1).
##
## - Each file is read and decoded at most once per process, on first use,
##   and never on a sim tick. parse_count counts every read and decode (a
##   JSON parse, a raw data-PNG decode, a base-map texture load);
##   parses_of(rel) gives one file's count, which stays at most 1.
## - Parsed JSON is shared and frozen: every Array and Dictionary in it is
##   read-only, so one caller cannot change what another sees. duplicate()
##   it to edit. The numeric ids (id, po, rank, area_id) of GeoJSON
##   properties and candidate sites are cast to int; JSON numbers parse as
##   float in Godot.
## - The data PNGs (suitability/poNN.png, basemap/po_index.national.png) are
##   data, not pictures: they are imported with importer="keep" and read raw
##   through FileAccess.get_file_as_bytes + Image.load_png_from_buffer, never
##   load(), so every byte keeps its value. Sample them nearest, no mipmaps.
## - A failure records last_error and returns an empty value ({}, [] or
##   null) without push_error, so a missing layer never spams the log.
## - Main thread only: the caches are plain static dictionaries, unlocked. A
##   loader on a worker thread must parse on its own and hand the result over.
##
## The 618 KB localities.geojson is not parsed here: the national icons come
## from the sim's packed arrays (SimBridge.get_geo_sites, Track A).

const ROOT := "res://data/geo/"
const LODS: Array[String] = ["national", "regional"]
## Numeric fields that are ids or counts, cast from JSON's float to int.
const INT_KEYS: Array[String] = ["id", "po", "rank", "area_id"]
const AREA_COUNT := 13
const PO_INDEX_KEY := "po_index_national"
const SALMON_FJORD := "salmon_fjord"

## Every read and decode of a geo file since the process started.
static var parse_count: int = 0
## Why the last failed call returned an empty value ("" before any failure).
static var last_error: String = ""

static var _json: Dictionary = {}      # rel -> frozen parse result
static var _png: Dictionary = {}       # rel -> Image (raw bytes)
static var _textures: Dictionary = {}  # basemap key -> Texture2D
static var _derived: Dictionary = {}   # name -> frozen derived value
static var _parses: Dictionary = {}    # rel -> reads of that file


## The parsed JSON or GeoJSON file at godot/data/geo/<rel>, frozen and
## cached. Returns {} and records last_error when the file is missing,
## unreadable or not valid JSON.
static func read_json(rel: String) -> Variant:
	if _json.has(rel):
		return _json[rel]
	if not _is_safe_rel(rel):
		return _fail("read_json: '%s' is not a file under %s" % [rel, ROOT], {})
	var path := ROOT + rel
	if not FileAccess.file_exists(path):
		return _fail("read_json: missing %s" % path, {})
	var text := FileAccess.get_file_as_string(path)
	if text.is_empty():
		return _fail("read_json: cannot read %s (%s)" % [path, error_string(FileAccess.get_open_error())], {})
	# A JSON instance, not JSON.parse_string: the static helper push_errors on
	# a parse failure, and a broken file must not spam the log.
	var parser := JSON.new()
	if parser.parse(text) != OK:
		return _fail("read_json: %s line %d: %s" % [
			path, parser.get_error_line(), parser.get_error_message()], {})
	var data: Variant = parser.data
	_cast_ids(data)
	_freeze(data)
	_json[rel] = data
	_count(rel)
	return data


## basemap.json's entry for one image ("national", "po01".."po13",
## "po_index_national"): {key, file, width, height, res_m, bounds_25833,
## is_data, po (0 for the national images)}. {} for an unknown key.
static func basemap(key: String) -> Dictionary:
	var name := "basemap:" + key
	if _derived.has(name):
		return _derived[name]
	var images: Variant = _dict(read_json("basemap.json")).get("images", {})
	var raw: Variant = images.get(key) if images is Dictionary else null
	if not raw is Dictionary:
		return _fail("basemap: no image '%s' in basemap.json" % key, {})
	var file := String(raw.get("file", ""))
	var info := {
		"key": key,
		"file": file,
		"width": int(raw.get("width", 0)),
		"height": int(raw.get("height", 0)),
		"res_m": float(raw.get("res_m", 0.0)),
		"bounds_25833": raw.get("bounds_25833", []),
		"is_data": file.get_extension() == "png",
		"po": int(raw.get("po", 0)),
	}
	info.make_read_only()
	_derived[name] = info
	return info


## The base-map image for a basemap.json key as a Texture2D, cached. The
## JPEGs load as their imported textures; the po_index data PNG becomes an
## ImageTexture of its raw bytes (load() would not find it: importer="keep").
## null, with last_error, when the image is missing or its size disagrees
## with basemap.json.
static func basemap_texture(key: String) -> Texture2D:
	if _textures.has(key):
		return _textures[key]
	var info := basemap(key)
	if info.is_empty():
		return null
	var file: String = info["file"]
	var texture: Texture2D = null
	if info["is_data"]:
		var image := load_data_png(file)
		if image == null:
			return null
		texture = ImageTexture.create_from_image(image)
	else:
		var path := ROOT + file
		if not ResourceLoader.exists(path):
			return _fail("basemap_texture: %s is not imported" % path, null)
		texture = load(path) as Texture2D
		if texture == null:
			return _fail("basemap_texture: %s did not load as a texture" % path, null)
		_count(file)
	var want := Vector2i(info["width"], info["height"])
	if Vector2i(texture.get_size()) != want:
		return _fail("basemap_texture: %s is %s, basemap.json says %s" % [file, texture.get_size(), want], null)
	_textures[key] = texture
	return texture


## A data PNG under godot/data/geo, decoded from its raw bytes, cached. The
## result is shared: read it, never change it. null, with last_error, when
## the file is missing or does not decode.
static func load_data_png(rel: String) -> Image:
	if _png.has(rel):
		return _png[rel]
	if not _is_safe_rel(rel) or rel.get_extension() != "png":
		return _fail("load_data_png: '%s' is not a PNG under %s" % [rel, ROOT], null)
	var path := ROOT + rel
	if not FileAccess.file_exists(path):
		return _fail("load_data_png: missing %s" % path, null)
	var bytes := FileAccess.get_file_as_bytes(path)
	if bytes.is_empty():
		return _fail("load_data_png: cannot read %s (%s)" % [path, error_string(FileAccess.get_open_error())], null)
	var image := Image.new()
	var err := image.load_png_from_buffer(bytes)
	if err != OK or image.is_empty():
		return _fail("load_data_png: %s does not decode (%s)" % [path, error_string(err)], null)
	_png[rel] = image
	_count(rel)
	return image


## The production-area index of the national map (8-bit grey, 1-13 on each
## area's sea, 0 elsewhere): an 800 m open-sea mask, so a coastal point can
## sample 0 (STAGE23_PLAN §5). null on failure.
static func po_index_image() -> Image:
	var info := basemap(PO_INDEX_KEY)
	if info.is_empty():
		return null
	return load_data_png(info["file"])


## The 13 production areas (GeoJSON features, id order) at "national" or
## "regional" detail. [] for another lod.
static func production_areas(lod: String) -> Array:
	return _features_of("production_areas", lod)


## The 19 Havbruk til havs offshore areas (one feature per stage reached)
## at "national" or "regional" detail. [] for another lod.
static func offshore_areas(lod: String) -> Array:
	return _features_of("offshore_areas", lod)


## The 30 national salmon fjords: the salmon_fjord features of
## exclusions.national.geojson.
static func salmon_fjords() -> Array:
	var name := "salmon_fjords"
	if _derived.has(name):
		return _derived[name]
	var features: Variant = _dict(read_json("exclusions.national.geojson")).get("features")
	if not features is Array:
		return _fail("salmon_fjords: exclusions.national.geojson has no features", [])
	var fjords: Array = []
	for feature: Variant in features:
		if _props(feature).get("kind", "") == SALMON_FJORD:
			fjords.append(feature)
	fjords.make_read_only()
	_derived[name] = fjords
	return fjords


## candidates.json's 189 sites: 156 open pens (12 per production area,
## by rank) and 33 offshore sites in the Havbruk til havs areas.
static func candidates() -> Array:
	var sites: Variant = _dict(read_json("candidates.json")).get("sites")
	if not sites is Array:
		return _fail("candidates: candidates.json has no sites", [])
	return sites


## One production area's 500 m suitability grid: suitability.json's entry
## plus the raw image. {po, file, width, height, res_m, origin_25833 (the
## north-west corner), bounds_25833 ([x0, y0, x1, y1], for GeoProjection),
## stats, image (RGB8: R score byte, G reason code, B depth / 4 m)}. {} for
## an area outside 1..13 or a grid that does not match its record.
static func suitability_grid(po: int) -> Dictionary:
	var name := "suitability:%d" % po
	if _derived.has(name):
		return _derived[name]
	if po < 1 or po > AREA_COUNT:
		return _fail("suitability_grid: no production area %d" % po, {})
	var grids: Variant = _dict(read_json("suitability.json")).get("grids", {})
	var raw: Variant = grids.get(str(po)) if grids is Dictionary else null
	if not raw is Dictionary:
		return _fail("suitability_grid: suitability.json has no grid %d" % po, {})
	var file := String(raw.get("file", ""))
	var image := load_data_png(file)
	if image == null:
		return {}
	var width := int(raw.get("width", 0))
	var height := int(raw.get("height", 0))
	var res := float(raw.get("res_m", 0.0))
	if image.get_width() != width or image.get_height() != height or image.get_format() != Image.FORMAT_RGB8:
		return _fail("suitability_grid: %s is %dx%d format %d, suitability.json says %dx%d RGB8" % [
			file, image.get_width(), image.get_height(), image.get_format(), width, height], {})
	var origin: Array = raw.get("origin_25833", [0.0, 0.0])
	var x0 := float(origin[0])
	var y1 := float(origin[1])
	var grid := {
		"po": po,
		"file": file,
		"width": width,
		"height": height,
		"res_m": res,
		"origin_25833": origin,
		"bounds_25833": _frozen([x0, y1 - height * res, x0 + width * res, y1]),
		"stats": raw.get("stats", {}),
		"image": image,
	}
	grid.make_read_only()
	_derived[name] = grid
	return grid


## suitability_rules.json: every weight and threshold with its source
## (effective_weights, components, hard_constraints, ...).
static func rules() -> Dictionary:
	return _dict(read_json("suitability_rules.json"))


## How many times godot/data/geo/<rel> has been read and decoded.
static func parses_of(rel: String) -> int:
	return int(_parses.get(rel, 0))


## Tests only: forget every cached file and counter, as at process start,
## so a check file leaves no warm cache behind for the lazy-load checks that
## run after it. Values handed out earlier stay valid for their holders.
static func reset_cache() -> void:
	_json = {}
	_png = {}
	_textures = {}
	_derived = {}
	_parses = {}
	parse_count = 0
	last_error = ""


# ── Internals ─────────────────────────────────────────────────────────────────

static func _features_of(layer: String, lod: String) -> Array:
	if lod not in LODS:
		return _fail("%s: unknown level of detail '%s' (national or regional)" % [layer, lod], [])
	var features: Variant = _dict(read_json("%s.%s.geojson" % [layer, lod])).get("features")
	if not features is Array:
		return _fail("%s: %s.%s.geojson has no features" % [layer, layer, lod], [])
	return features


## A relative path under ROOT: no scheme, no absolute path, no '..'.
static func _is_safe_rel(rel: String) -> bool:
	return not rel.is_empty() and not rel.begins_with("/") and not rel.contains("://") \
		and not rel.contains("\\") and ".." not in rel.split("/")


static func _cast_ids(data: Variant) -> void:
	if not data is Dictionary:
		return
	for list_key in ["features", "sites"]:
		var rows: Variant = data.get(list_key)
		if not rows is Array:
			continue
		for row: Variant in rows:
			if not row is Dictionary:
				continue
			_cast_record(row.get("properties") if list_key == "features" else row)


## Casts each INT_KEYS value that is a whole number to int. The test is exact:
## is_equal_approx's tolerance grows with the value (1e-5 relative), so it
## would round a fractional 100000.4 to 100000 and hide a broken id.
static func _cast_record(record: Variant) -> void:
	if not record is Dictionary:
		return
	for key in INT_KEYS:
		var v: Variant = record.get(key)
		if typeof(v) == TYPE_FLOAT and is_finite(v) and v == roundf(v):
			record[key] = int(v)


## Make every Array and Dictionary inside a parse result read-only.
static func _freeze(v: Variant) -> void:
	if v is Dictionary:
		for item: Variant in v.values():
			_freeze(item)
		(v as Dictionary).make_read_only()
	elif v is Array:
		for item: Variant in v:
			_freeze(item)
		(v as Array).make_read_only()


static func _frozen(a: Array) -> Array:
	a.make_read_only()
	return a


static func _dict(v: Variant) -> Dictionary:
	return v if v is Dictionary else {}


static func _props(feature: Variant) -> Dictionary:
	return _dict(_dict(feature).get("properties"))


static func _count(rel: String) -> void:
	parse_count += 1
	_parses[rel] = parses_of(rel) + 1


static func _fail(message: String, empty: Variant) -> Variant:
	last_error = message
	return empty
