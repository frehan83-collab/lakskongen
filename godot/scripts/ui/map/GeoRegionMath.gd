class_name GeoRegionMath
extends RefCounted
## The region level's arithmetic (geo stages 2-3 plan Track D, §3
## "GeoRegionMath"): where a production area's base-map pixel falls in that
## area's 500 m suitability grid, and the colours the heat draws a cell in.
##
## The base map (basemap/poNN.jpg, 250 or 375 m a pixel) and the suitability
## grid (suitability/poNN.png, 500 m a cell) are both north-up UTM 33 rasters,
## but with different origins and resolutions, so the heat shader needs one
## affine map per area:
##   map px (x, y) -> metres (bx0 + x * bres, by1 - y * bres)
##                 -> cell ((X - ox) / 500, (oy - Y) / 500)
##                 -> uv (cell.x / W, cell.y / H)
## which is uv = px * suit_scale + suit_offset. map_suitability.gdshader takes
## exactly these numbers as its uniforms (shader_uniforms), and the headless
## checks sample the raw grid on the CPU at suit_uv() to pin the transform
## without a GPU (shaders are only compiled by GitHub's windowed step).
##
## Display only: nothing here feeds the simulation. Arithmetic is 64-bit until
## the result narrows to a Vector2.

const SUIT_RES_M := 500.0
const AREA_COUNT := 13

## The heat's colour ramp over the score (R - 1) / 254: cool deep teal for a
## poor cell, through sea green, to buoy-yellow gold for the best. The stops
## sit on the scored cells' spread (all 13 grids: 5th percentile 0.63, median
## 0.84, 90th 0.93), so the cells a player compares read apart; below 0.5 is
## deep teal. The shader carries the same stops (tests/test_geo_map_contract.py
## pins them equal).
const RAMP_STOPS: Array[float] = [0.5, 0.7, 0.85, 1.0]
const RAMP_COLORS: Array[Color] = [
	Color(0.122, 0.369, 0.451),  # #1F5E73 deep teal
	Color(0.369, 0.635, 0.604),  # #5EA29A sea green
	Color(0.776, 0.737, 0.373),  # #C6BC5F pale gold
	Color(0.910, 0.725, 0.165),  # #E8B92A buoy yellow
]
## The heat's opacity over the base map, rising with the score: poor water
## stays see-through, the best reads strongest, the map always shows.
const HEAT_ALPHA_LOW := 0.30
const HEAT_ALPHA_HIGH := 0.66
## Reason codes drawn as a muted hatch: the hard exclusions on the sea
## (4 salmon fjord .. 12 too deep). 1 land, 2 outside the area and 3 no depth
## stay clear: the base map already says land, and a hatch there is noise.
const HATCH_FIRST := 4
const HATCH_LAST := 12
## Code 13 (outside the planning area, more than 1 nm off the baseline) is
## the open-sea regime, not a ban on a place: quieter still.
const CODE_OUTSIDE_PLANNING := 13
const HATCH_COLOR := Color(0.70, 0.58, 0.56, 0.42)
const HATCH_QUIET_COLOR := Color(0.62, 0.66, 0.70, 0.18)
## A scored cell carries 16 + its binding term (GEODATA §2.1).
const SCORED_BASE := 16

static var _transforms: Dictionary = {}  # po -> frozen transform


## The basemap.json key of an area's base map ("po03").
static func basemap_key(po: int) -> String:
	return "po%02d" % po


## The area's raster georeference: {po, map_size (Vector2, base-map pixels),
## map_res, map_bounds, grid_size (Vector2i), grid_origin ([ox, oy], the
## north-west corner), suit_scale, suit_offset}. {} for an area outside 1..13
## or missing data. Read from basemap.json and suitability.json only: the grid
## image itself is not decoded here.
static func transform(po: int) -> Dictionary:
	if _transforms.has(po):
		return _transforms[po]
	if po < 1 or po > AREA_COUNT:
		return {}
	var map := GeoData.basemap(basemap_key(po))
	var grids: Variant = (GeoData.read_json("suitability.json") as Dictionary).get("grids", {})
	var grid: Variant = grids.get(str(po)) if grids is Dictionary else null
	if map.is_empty() or not grid is Dictionary:
		return {}
	var bounds: Array = map["bounds_25833"]
	var bres: float = map["res_m"]
	var origin: Array = grid.get("origin_25833", [0.0, 0.0])
	var w := int(grid.get("width", 0))
	var h := int(grid.get("height", 0))
	if w <= 0 or h <= 0 or bres <= 0.0:
		return {}
	var res := float(grid.get("res_m", SUIT_RES_M))
	var ox := float(origin[0])
	var oy := float(origin[1])
	var t := {
		"po": po,
		"map_size": Vector2(map["width"], map["height"]),
		"map_res": bres,
		"map_bounds": bounds,
		"grid_size": Vector2i(w, h),
		"grid_res": res,
		"grid_origin": [ox, oy],
		# u = (bx0 + px * bres - ox) / (res * W); v = (oy - by1 + py * bres) / (res * H)
		"suit_scale": Vector2(bres / (res * w), bres / (res * h)),
		"suit_offset": Vector2((float(bounds[0]) - ox) / (res * w), (oy - float(bounds[3])) / (res * h)),
	}
	t.make_read_only()
	_transforms[po] = t
	return t


## An area base-map pixel -> the UV of the same point in the area's
## suitability grid (0..1 inside the grid). Vector2.INF for an unknown area.
static func suit_uv(po: int, map_px: Vector2) -> Vector2:
	var t := transform(po)
	if t.is_empty():
		return Vector2.INF
	var s: Vector2 = t["suit_scale"]
	var o: Vector2 = t["suit_offset"]
	# 64-bit products; only the final uv narrows.
	var u: float = float(map_px.x) * float(s.x) + float(o.x)
	var v: float = float(map_px.y) * float(s.y) + float(o.y)
	return Vector2(u, v)


## The grid cell (column, row) under a uv, floored; may lie off the grid.
static func cell_of_uv(po: int, uv: Vector2) -> Vector2i:
	var t := transform(po)
	if t.is_empty() or not uv.is_finite():
		return Vector2i(-1, -1)
	var g: Vector2i = t["grid_size"]
	return Vector2i(int(floor(float(uv.x) * g.x)), int(floor(float(uv.y) * g.y)))


## The grid cell under a point in EPSG:25833 metres: floor((x - ox) / 500),
## floor((oy - y) / 500), the lookup of tests/test_geodata.py. May lie off the
## grid; Vector2i(-1, -1) for an unknown area.
static func suit_cell(po: int, x: float, y: float) -> Vector2i:
	var t := transform(po)
	if t.is_empty():
		return Vector2i(-1, -1)
	var origin: Array = t["grid_origin"]
	var res: float = t["grid_res"]
	return Vector2i(int(floor((x - float(origin[0])) / res)), int(floor((float(origin[1]) - y) / res)))


## A point in EPSG:25833 metres -> the area base map's pixel position.
static func map_px(po: int, x: float, y: float) -> Vector2:
	var t := transform(po)
	if t.is_empty():
		return Vector2.INF
	return GeoProjection.to_px(x, y, t["map_bounds"], t["map_res"])


## The uniforms map_suitability.gdshader takes for an area: map_size,
## suit_scale, suit_offset (the same numbers suit_uv uses). {} when unknown.
static func shader_uniforms(po: int) -> Dictionary:
	var t := transform(po)
	if t.is_empty():
		return {}
	return {"map_size": t["map_size"], "suit_scale": t["suit_scale"], "suit_offset": t["suit_offset"]}


## The raw cell {r, g, b, score, reason, depth_m, legal} of an area's grid
## at a cell, or {} off the grid (the image decodes once, in GeoData).
static func sample(po: int, cell: Vector2i) -> Dictionary:
	var grid := GeoData.suitability_grid(po)
	if grid.is_empty():
		return {}
	var image: Image = grid["image"]
	if cell.x < 0 or cell.y < 0 or cell.x >= image.get_width() or cell.y >= image.get_height():
		return {}
	var c := image.get_pixel(cell.x, cell.y)
	var r := c.r8
	return {
		"r": r, "g": c.g8, "b": c.b8,
		"score": (r - 1) / 254.0 if r > 0 else 0.0,
		"reason": c.g8,
		"depth_m": c.b8 * 4,
		"legal": r > 0,
	}


## The heat's colour for a raw cell (R score byte, G reason code), the CPU
## twin of map_suitability.gdshader's fragment: the ramp for a scored cell,
## the hatch colour for a hard exclusion (alpha 0 off the hatch lines is the
## shader's business), transparent otherwise.
static func heat_color(r: int, g: int) -> Color:
	if r > 0:
		var t := (r - 1) / 254.0
		var c := ramp(t)
		c.a = heat_alpha(t)
		return c
	if g >= HATCH_FIRST and g <= HATCH_LAST:
		return HATCH_COLOR
	if g == CODE_OUTSIDE_PLANNING:
		return HATCH_QUIET_COLOR
	return Color(0, 0, 0, 0)


## The heat's opacity at a score 0..1: HEAT_ALPHA_LOW up to the first stop,
## rising smoothly to HEAT_ALPHA_HIGH at 1.
static func heat_alpha(t: float) -> float:
	return lerpf(HEAT_ALPHA_LOW, HEAT_ALPHA_HIGH, smoothstep(RAMP_STOPS[0], 1.0, t))


## The ramp colour at a score 0..1 (opaque).
static func ramp(t: float) -> Color:
	t = clampf(t, 0.0, 1.0)
	if t <= RAMP_STOPS[0]:
		return RAMP_COLORS[0]
	for i in range(1, RAMP_STOPS.size()):
		if t <= RAMP_STOPS[i]:
			var span := RAMP_STOPS[i] - RAMP_STOPS[i - 1]
			return RAMP_COLORS[i - 1].lerp(RAMP_COLORS[i], (t - RAMP_STOPS[i - 1]) / span if span > 0.0 else 1.0)
	return RAMP_COLORS[RAMP_COLORS.size() - 1]


## The reason code's translation key, MAP_REGION_REASON_<code>: a hard code
## (1-13) says why the cell is closed, a scored code (16-24) names the term
## that costs the cell the most score (suitability.json "codes").
static func reason_key(code: int) -> String:
	return "MAP_REGION_REASON_%d" % code


## Tests only: forget the cached transforms.
static func reset_cache() -> void:
	_transforms = {}
