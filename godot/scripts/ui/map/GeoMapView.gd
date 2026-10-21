class_name GeoMapView
extends Control
## The national map (geo stages 2-3 plan, Track C; GEODATA §7.3): the real
## Norwegian coast as one stylised chart, built from godot/data/geo and the
## sim's geo reads (GeoBridgeAdapter).
##
## Bottom to top: a deep-water frame; basemap/national.jpg in a TextureRect
## (keep aspect, centred) with map_po_tint.gdshader tinting each production
## area's sea from the raw po_index; a faint graticule; the salmon fjords and
## offshore areas (GeoOverlayLayer); the aquaculture sites (GeoSiteLayer);
## the candidates (a second GeoOverlayLayer); the company's own farms; a
## hover ring; the 13 area plates (MapMarker, placed by MapLabelPlacer); the
## city names, a scale bar and a north arrow; the layer chips and the key.
##
## Hit-testing reads the CPU copy of the same raw po_index the shader tints
## with, and falls back to the sim's polygons (get_geo_area_at) where the
## index is 0: on land, in inner waters and on the coastal points the 800 m
## open-sea mask leaves out (PO1's and PO3's labels, 27 candidates).
##
## Performance: the national data is read once per process (GeoData); the
## layers record on a resize, a layer toggle or a new rival layout only;
## live_update() on a sim day touches the shader's uniforms, the plates and
## the player's few marks, and only when what they show has changed.

signal area_clicked(area: int)
signal area_drill_requested(area: int)
signal feature_clicked(kind: String, id: String)
## A double-click on a feature that has a card at the site level (Track D's
## GeoSiteCard): one of CARD_KINDS. It stands in for area_drill_requested.
signal feature_drill_requested(kind: String, id: String)
signal area_hovered(area: int)

const TINT_SHADER := preload("res://shaders/map_po_tint.gdshader")
const TINT_STRENGTH := 0.35
const AREA_COUNT := 13
## [layer id, chip translation key, tooltip key]; layer_color() gives the edge.
const LAYERS: Array = [
	["sites", "MAP_GEO_LAYER_SITES", "MAP_GEO_LAYER_SITES_TIP"],
	["candidates", "MAP_GEO_LAYER_CANDIDATES", "MAP_GEO_LAYER_CANDIDATES_TIP"],
	["offshore", "MAP_GEO_LAYER_OFFSHORE", "MAP_GEO_LAYER_OFFSHORE_TIP"],
	["fjords", "MAP_GEO_LAYER_FJORDS", "MAP_GEO_LAYER_FJORDS_TIP"],
]
## Eight coastal cities [name, lon, lat], named on the chart where they fit.
const CITIES: Array = [
	["Oslo", 10.7522, 59.9139], ["Stavanger", 5.7331, 58.97], ["Bergen", 5.3221, 60.3913],
	["Ålesund", 6.1549, 62.4722], ["Trondheim", 10.3951, 63.4305], ["Bodø", 14.4049, 67.2804],
	["Tromsø", 18.9553, 69.6492], ["Hammerfest", 23.6821, 70.6634],
]
## Plates lean away from this inland point (Jämtland), out over the sea.
const LAND_CENTRE_LONLAT := Vector2(15.5, 64.0)
const EDGE_PAD := 8.0
## The key starts open on a map at least this tall (1080p at 100 %).
const LEGEND_OPEN_MIN_HEIGHT := 700.0
## Cluster numerals never draw smaller than this on the screen (physical
## pixels): on a small window (1280x720 with the UI at 100 %) the site
## layer's badges, and the grid that spaces them, grow by symbol_scale().
const MIN_BADGE_SCREEN_PX := 11.0
## A window smaller than this is no screen to read (the headless display
## server's is 64x64): the chart is then taken as shown 1:1.
const MIN_SCREEN_PX := Vector2i(320, 240)
## The company's farms closer than this on screen are spread apart around
## their common place, so each keeps a disc of its own (two scenario farms
## anchored at one locality would otherwise draw, and answer, as one).
const PLAYER_SPREAD_PX := 13.0
## The features a double-click opens a card for (Track D's site level): the
## candidate stars, the Havbruk til havs hexagons, the company's farms and a
## single locality. Any other double-click (a cluster, a fjord, an offshore
## area, open water) drills into the area.
const CARD_KINDS: Array[String] = ["candidates", "hexagon", "player", "site"]

## Times the layout ran (one per size).
var layout_count: int = 0
## Code-only plates: the full names did not fit without a collision.
var compact := false

var bridge: GeoBridgeAdapter = null
var _info: Dictionary = {}
var _tex_size := Vector2.ONE
var _po_index: Image = null
var _areas: Array = []
var _material: ShaderMaterial = null
var _base: TextureRect = null
var _overlay: GeoOverlayLayer = null
var _candidates: GeoOverlayLayer = null
var _sites: GeoSiteLayer = null
var _players: GeoSiteLayer.PlayerMarks = null
var _hover: _Hover = null
var _plates_root: Control = null
var _plates: Dictionary = {}  # area -> MapMarker
var _carto: MapCartography.Furniture = null
var _graticule: MapCartography.Graticule = null
var _chips: VBoxContainer = null
var _legend: MapLegend = null
var _hovered_area := 0
var _selected_area := 0
var _sites_signature := ""
var _player_rows: Array = []
var _offshore_rows: Array = []
## Which open-pen candidates the company holds (the stars move off them),
## and the company's farms the stars were last read for.
var _open_pens_key := ""
var _pens_for_farms: Array = []
var _laid_out_for := Vector2(-1.0, -1.0)
var _laid_out_symbol := -1.0
var _relayout_queued := false
var _legend_auto := true


## Build the chart for a SimBridge (null: data files and stubs only).
func setup(sim: Node) -> void:
	bridge = GeoBridgeAdapter.new(sim)
	clip_contents = true
	mouse_filter = Control.MOUSE_FILTER_STOP
	_info = GeoData.basemap("national")
	var texture := GeoData.basemap_texture("national")
	_po_index = GeoData.po_index_image()
	if texture != null:
		_tex_size = Vector2(texture.get_size())
	_areas = bridge.production_areas().duplicate()
	_areas.sort_custom(func(a: Dictionary, b: Dictionary) -> bool: return int(a["id"]) < int(b["id"]))
	var backdrop := ColorRect.new()
	backdrop.name = "Backdrop"
	backdrop.color = MapPalette.deep_water()
	backdrop.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_full_rect(backdrop)
	_base = TextureRect.new()
	_base.name = "BaseMap"
	_base.texture = texture
	_base.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_base.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	_base.texture_filter = CanvasItem.TEXTURE_FILTER_LINEAR_WITH_MIPMAPS
	_base.mouse_filter = Control.MOUSE_FILTER_IGNORE
	# ThemeLint cannot see a texture: text over the map is judged against the
	# chart's deep water (ThemeLint models the meta, plan Track C).
	_base.set_meta(&"lint_background", MapPalette.deep_water())
	_base.material = _tint_material()
	_full_rect(_base)
	_graticule = MapCartography.Graticule.new()
	_graticule.name = "Graticule"
	_full_rect(_graticule)
	_overlay = GeoOverlayLayer.new()
	_overlay.name = "Overlays"
	_full_rect(_overlay)
	_sites = GeoSiteLayer.new()
	_sites.name = "Sites"
	_full_rect(_sites)
	_sites_signature = bridge.sites_signature()
	_sites.set_sites(bridge.sites())
	_candidates = GeoOverlayLayer.new()
	_candidates.name = "Candidates"
	_candidates.part = "candidates"
	_full_rect(_candidates)
	_offshore_rows = bridge.offshore_candidates()
	_candidates.set_offshore_candidates(_offshore_rows)
	var pens := bridge.open_pen_candidates()
	_open_pens_key = _held_key(pens)
	_pens_for_farms = bridge.player_site_locations()
	_candidates.set_open_pens(pens)
	_players = GeoSiteLayer.PlayerMarks.new()
	_players.name = "PlayerSites"
	_full_rect(_players)
	_hover = _Hover.new()
	_hover.name = "Hover"
	_full_rect(_hover)
	_plates_root = Control.new()
	_plates_root.name = "Plates"
	_plates_root.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_full_rect(_plates_root)
	for a: Dictionary in _areas:
		var plate := MapMarker.new()
		_plates_root.add_child(plate)
		plate.setup(int(a["id"]), str(a["name"]))
		plate.pressed.connect(func(area: int) -> void: area_clicked.emit(area))
		plate.drill_requested.connect(func(area: int) -> void: area_drill_requested.emit(area))
		plate.mouse_entered.connect(set_hovered_area.bind(int(a["id"])))
		plate.mouse_exited.connect(set_hovered_area.bind(0))
		_plates[int(a["id"])] = plate
	_carto = MapCartography.Furniture.new()
	_carto.name = "Cartography"
	_full_rect(_carto)
	_chips = _build_chips()
	add_child(_chips)
	_legend = MapLegend.new()
	add_child(_legend)
	# The key opens by itself on a tall map; once the player folds or opens
	# it, the choice holds and the plates make room for it.
	_legend.toggle_button().toggled.connect(func(_on: bool) -> void:
		_legend_auto = false
		relayout(true))
	resized.connect(_queue_relayout)
	# A window resize keeps the layout size (canvas_items stretch) but
	# changes the screen scale, and so the badges' symbol scale.
	get_viewport().size_changed.connect(_queue_relayout)
	_queue_relayout()


# ── Geometry ──────────────────────────────────────────────────────────────────

## Where the base map is drawn in this control (TextureRect's keep-aspect-
## centred rule, to the pixel).
func drawn_rect() -> Rect2:
	var w := _tex_size.x * size.y / _tex_size.y
	var h := size.y
	if w > size.x:
		w = size.x
		h = _tex_size.y * w / _tex_size.x
	return Rect2(Vector2((size.x - w) * 0.5, (size.y - h) * 0.5), Vector2(w, h))


## Screen pixels per national-image pixel.
func px_scale() -> float:
	return drawn_rect().size.x / _tex_size.x


## Physical screen pixels per layout pixel: the root window's canvas_items
## stretch (0.67 on a 1280x720 window, 1.5 with the UI at 150 % on 1080p);
## 1 when the window is no screen to read (smaller than MIN_SCREEN_PX).
func screen_scale() -> float:
	var win := get_window()
	if win == null or win.size.x < MIN_SCREEN_PX.x or win.size.y < MIN_SCREEN_PX.y:
		return 1.0
	return get_viewport().get_final_transform().get_scale().x * get_global_transform_with_canvas().get_scale().x


## How much the cluster badges and their grid grow so the numerals keep
## MIN_BADGE_SCREEN_PX on screen: 1 at 1080p and above, 1.5 on a 1280x720
## window with the UI at 100 %.
func symbol_scale() -> float:
	return maxf(1.0, MIN_BADGE_SCREEN_PX / (float(GeoSiteLayer.BADGE_FONT) * maxf(screen_scale(), 0.01)))


## An EPSG:25833 point in this control's coordinates.
func utm_to_local(x: float, y: float) -> Vector2:
	var d := drawn_rect()
	return d.position + GeoProjection.to_px(x, y, _info["bounds_25833"], _info["res_m"]) * (d.size.x / _tex_size.x)


## A point of this control in EPSG:25833 metres.
func local_to_utm(p: Vector2) -> PackedFloat64Array:
	var d := drawn_rect()
	return GeoProjection.to_utm((p - d.position) / (d.size.x / _tex_size.x), _info["bounds_25833"], _info["res_m"])


## The production area under a point: the raw po_index first, then the sim's
## polygons where the index is 0. 0 off the map or outside every area.
func area_at_local(p: Vector2) -> int:
	var d := drawn_rect()
	if not d.has_point(p) or _po_index == null:
		return 0
	var px := (p - d.position) / (d.size.x / _tex_size.x)
	var cell := Vector2i(clampi(int(px.x), 0, _po_index.get_width() - 1), clampi(int(px.y), 0, _po_index.get_height() - 1))
	var id := _po_index.get_pixel(cell.x, cell.y).r8
	if id > 0:
		return id
	var xy := local_to_utm(p)
	return bridge.area_at(xy[0], xy[1])


## The raw po_index value under a point (0 off the mask), for the checks.
func po_index_at_local(p: Vector2) -> int:
	var d := drawn_rect()
	if not d.has_point(p) or _po_index == null:
		return 0
	var px := (p - d.position) / (d.size.x / _tex_size.x)
	return _po_index.get_pixel(clampi(int(px.x), 0, _po_index.get_width() - 1),
		clampi(int(px.y), 0, _po_index.get_height() - 1)).r8


# ── Parts (tests, MapPanel, Track D) ──────────────────────────────────────────

func plate(area: int) -> MapMarker:
	return _plates.get(area) as MapMarker


func plates() -> Dictionary:
	return _plates


func site_layer() -> GeoSiteLayer:
	return _sites


## The salmon fjords and the offshore areas (under the sites).
func overlay_layer() -> GeoOverlayLayer:
	return _overlay


## The candidate stars and hexagons (above the sites).
func candidate_layer() -> GeoOverlayLayer:
	return _candidates


func player_marks() -> GeoSiteLayer.PlayerMarks:
	return _players


func legend() -> MapLegend:
	return _legend


func base_map() -> TextureRect:
	return _base


func tint_material() -> ShaderMaterial:
	return _material


## The layer chips (Buttons named Chip_<layer>).
func chips() -> Array[Button]:
	var out: Array[Button] = []
	if _chips != null:
		for c in _chips.get_children():
			if c is Button:
				out.append(c)
	return out


## The areas as the bridge gives them, by id.
func areas() -> Array:
	return _areas


## The city name rects drawn last layout (this control's coordinates).
func city_label_rects() -> Array[Rect2]:
	return _carto.placed if _carto != null else ([] as Array[Rect2])


# ── State ─────────────────────────────────────────────────────────────────────

func set_selected_area(area: int) -> void:
	_selected_area = area
	if _material != null:
		_material.set_shader_parameter("selected_id", area)
	for a: int in _plates:
		var p: MapMarker = _plates[a]
		p.state = MapMarker.STATE_SELECTED if a == area else MapMarker.STATE_NORMAL
		p.refresh()


func set_hovered_area(area: int) -> void:
	if area == _hovered_area:
		return
	_hovered_area = area
	if _material != null:
		_material.set_shader_parameter("hovered_id", area)
	area_hovered.emit(area)


## Show or hide a layer ("sites", "candidates", "offshore", "fjords").
func set_layer(layer: String, on: bool) -> void:
	if layer == "sites":
		_sites.visible = on
	elif layer == "candidates":
		_candidates.set_layer(layer, on)
	else:
		_overlay.set_layer(layer, on)
	var chip := _chips.get_node_or_null("Chip_" + layer) as Button
	if chip != null and chip.button_pressed != on:
		chip.set_pressed_no_signal(on)
		_style_chip(chip)


## A sim day or state change while the map is shown: re-read the rival
## layout only when its signature moved, and the player's farms and hav_1's
## binding only when the expansion overview moved (overview_changed); a
## layer records again only when what it shows changed. Returns what changed.
func live_update(overview_changed: bool) -> PackedStringArray:
	var changed := PackedStringArray()
	var sig := bridge.sites_signature()
	if sig != _sites_signature:
		_sites_signature = sig
		_sites.set_sites(bridge.sites())
		changed.append("sites")
	if overview_changed and _update_players():
		changed.append("players")
	# A farm built on (or gone from) a candidate moves its area's star: read
	# the stars again whenever the farms differ from those they were read for
	# (a relayout may have taken the new farms in first, while hidden).
	if overview_changed and _player_rows != _pens_for_farms:
		_pens_for_farms = _player_rows
		var pens := bridge.open_pen_candidates()
		var key := _held_key(pens)
		if key != _open_pens_key:
			_open_pens_key = key
			_candidates.set_open_pens(pens)
			changed.append("candidates")
	# A loaded game may bind hav_1 elsewhere (its home area): re-record the
	# candidates only when the bound candidate moved.
	if overview_changed:
		var rows := bridge.offshore_candidates()
		if _bound_id(rows) != _bound_id(_offshore_rows):
			_offshore_rows = rows
			_candidates.set_offshore_candidates(rows)
			if not "candidates" in changed:
				changed.append("candidates")
	return changed


## The open-pen candidates the company holds, and how many rows there are.
static func _held_key(rows: Array) -> String:
	var held := PackedStringArray()
	for r: Variant in rows:
		if r is Dictionary and str(r.get("taken_by_site_id", "")) != "":
			held.append(str(r.get("id", "")))
	return "%d|%s" % [rows.size(), ",".join(held)]


static func _bound_id(rows: Array) -> String:
	for r: Variant in rows:
		if r is Dictionary and str(r.get("bound_plot_id", "")) == GeoOverlayLayer.HAV_PLOT:
			return str(r.get("id", ""))
	return ""


# ── Layout ────────────────────────────────────────────────────────────────────

func _queue_relayout() -> void:
	if _relayout_queued:
		return
	_relayout_queued = true
	_relayout.call_deferred()


## Lay the chart out for the current size (once per size).
func relayout(force: bool = false) -> void:
	_relayout_queued = false
	var symbol := symbol_scale()
	if size.x < 32.0 or size.y < 32.0 or (size == _laid_out_for and is_equal_approx(symbol, _laid_out_symbol) and not force):
		return
	_laid_out_for = size
	_laid_out_symbol = symbol
	var d := drawn_rect()
	var s := d.size.x / _tex_size.x
	_overlay.set_view(d.position, s, _info["bounds_25833"], _info["res_m"])
	_sites.set_view(d.position, s, _info["bounds_25833"], _info["res_m"], symbol)
	_candidates.set_view(d.position, s, _info["bounds_25833"], _info["res_m"])
	_update_players(true)
	_chips.position = Vector2(EDGE_PAD, EDGE_PAD)
	_chips.reset_size()
	if _legend_auto:
		_legend.set_open(size.y >= LEGEND_OPEN_MIN_HEIGHT)
	_legend.reset_size()
	_legend.position = (size - _legend.get_combined_minimum_size() - Vector2(EDGE_PAD, EDGE_PAD)).floor()
	_carto.layout_frame(d, float(_info["res_m"]) / s)
	var obstacles: Array[Rect2] = [_chips.get_rect().grow(4.0), _legend.get_rect().grow(4.0)]
	obstacles.append_array(_carto.frame_rects())
	_graticule.layout_lines(self, obstacles)
	_place_plates(obstacles, _soft_obstacles())
	# City names keep clear of the plates and the anchors, and of the cluster
	# badges where they can (soft: a name may still cross a badge).
	var taken: Array[Rect2] = obstacles.duplicate()
	for a: int in _plates:
		taken.append((_plates[a] as MapMarker).get_rect())
		taken.append((_plates[a] as MapMarker).dot_rect())
	var badges: Array[Rect2] = []
	for c: Dictionary in _sites.clusters():
		if int(c["count"]) > 1:
			var r := _sites.badge_radius_now(int(c["count"])) + 1.0
			badges.append(Rect2(c["pos"] - Vector2(r, r), Vector2(r, r) * 2.0))
	_carto.layout_cities(_city_anchors(), Rect2(Vector2(EDGE_PAD, EDGE_PAD), size - Vector2(EDGE_PAD, EDGE_PAD) * 2.0),
		taken, badges)
	layout_count += 1


func _relayout() -> void:
	relayout()


## What a plate should rather not hide: the site clusters, the candidates
## and the player's own farms (MapLabelPlacer's soft obstacles).
func _soft_obstacles() -> Array[Rect2]:
	var soft: Array[Rect2] = []
	for c: Dictionary in _sites.clusters():
		var n: int = c["count"]
		var r := _sites.badge_radius_now(n) + 3.0 if n > 1 else 5.0
		soft.append(Rect2(c["pos"] - Vector2(r, r), Vector2(r, r) * 2.0))
	for s: Dictionary in _candidates.star_groups():
		soft.append(Rect2(s["pos"] - Vector2(9, 9), Vector2(18, 18)))
	for h: Dictionary in _candidates.hexagons():
		soft.append(Rect2(h["pos"] - Vector2(7, 7), Vector2(14, 14)))
	for p in _players.points():
		# The company's farms weigh more: four rects on the same spot.
		for k in 4:
			soft.append(Rect2(p - Vector2(10, 10), Vector2(20, 20)))
	return soft


func _place_plates(obstacles: Array[Rect2], soft: Array[Rect2] = []) -> void:
	var anchors := PackedVector2Array()
	var prefer := PackedVector2Array()
	var land := GeoProjection.utm33(LAND_CENTRE_LONLAT.x, LAND_CENTRE_LONLAT.y)
	var centre := utm_to_local(land[0], land[1])
	var order: Array[MapMarker] = []
	for a: Dictionary in _areas:
		var p: MapMarker = _plates[int(a["id"])]
		p.anchor = utm_to_local(float(a["label_x"]), float(a["label_y"]))
		anchors.append(p.anchor)
		prefer.append((p.anchor - centre).normalized())
		order.append(p)
	var bounds := Rect2(Vector2(EDGE_PAD, EDGE_PAD), size - Vector2(EDGE_PAD, EDGE_PAD) * 2.0)
	var result := {}
	for mode: bool in [false, true]:
		compact = mode
		var sizes := PackedVector2Array()
		for p in order:
			p.set_compact(mode)
			sizes.append(p.plate_size())
		result = MapLabelPlacer.place(anchors, sizes, prefer, bounds, obstacles, soft)
		if result["ok"]:
			break
	var rects: Array = result["rects"]
	for i in order.size():
		var p := order[i]
		p.position = (rects[i] as Rect2).position
		p.size = (rects[i] as Rect2).size
		p.leader = result["leaders"][i]
		p.queue_redraw()


func _city_anchors() -> Array:
	var out: Array = []
	for c: Array in CITIES:
		var xy := GeoProjection.utm33(float(c[1]), float(c[2]))
		out.append([str(c[0]), utm_to_local(xy[0], xy[1])])
	return out


## Marks closer than gap_px (PLAYER_SPREAD_PX by default; the region level
## spreads its larger site marks by their own size) pushed apart along the
## line between them (a fixed, index-based direction for marks on one
## point), a few rounds, pair by pair in index order: deterministic, and a
## group keeps its centre.
static func spread_marks(points: PackedVector2Array, gap_px: float = PLAYER_SPREAD_PX) -> PackedVector2Array:
	var out := points.duplicate()
	for _round in 12:
		var moved := false
		for i in out.size():
			for j in range(i + 1, out.size()):
				var d := out[j] - out[i]
				var gap := d.length()
				if gap >= gap_px - 0.01:
					continue
				var dir := d / gap if gap > 0.01 else Vector2.RIGHT.rotated(TAU * float(i + 2 * j) / 7.0)
				var push := dir * (gap_px - gap) * 0.5
				out[i] -= push
				out[j] += push
				moved = true
		if not moved:
			break
	return out


## Re-read the player's farms; true when they changed.
func _update_players(force: bool = false) -> bool:
	var rows := bridge.player_site_locations()
	if rows == _player_rows and not force:
		return false
	_player_rows = rows
	var pts := PackedVector2Array()
	for r: Dictionary in rows:
		pts.append(utm_to_local(float(r.get("x", 0.0)), float(r.get("y", 0.0))))
	_players.set_marks(rows, spread_marks(pts))
	return true


# ── Input and tooltips ────────────────────────────────────────────────────────

func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseMotion:
		set_hovered_area(area_at_local(event.position))
		_hover.show_item(feature_at(event.position))
	elif event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		var area := area_at_local(event.position)
		var item := feature_at(event.position)
		var kind := str(item.get("kind", ""))
		if not item.is_empty():
			feature_clicked.emit(kind, feature_id(item))
		if event.double_click and kind in CARD_KINDS:
			feature_drill_requested.emit(kind, feature_id(item))
		elif area > 0:
			if event.double_click:
				area_drill_requested.emit(area)
			else:
				area_clicked.emit(area)
		accept_event()


func _notification(what: int) -> void:
	if what == NOTIFICATION_MOUSE_EXIT and _hover != null:
		_hover.show_item({})
		set_hovered_area(0)


## The feature under a point: the nearer of the company's farm and a
## candidate star or hexagon (the farm, drawn above, on equal distance), else
## a site or cluster, an offshore area, a salmon fjord. {} for none. "pos" is
## the feature's own point (the hover ring's centre), never the pointer.
func feature_at(p: Vector2) -> Dictionary:
	var candidate := _candidates.item_at(p) if _candidates != null else {}
	var mark := _players.index_at(p) if _players != null else -1
	if mark >= 0:
		var at: Vector2 = _players.points()[mark]
		if candidate.is_empty() or at.distance_to(p) <= float(candidate.get("distance", INF)):
			return {"kind": "player", "row": _players.rows()[mark], "pos": at,
				"radius": GeoSiteLayer.PlayerMarks.RADIUS}
	if not candidate.is_empty():
		return candidate
	var site := _sites.item_at(p) if _sites != null else {}
	if not site.is_empty():
		return site
	return _overlay.item_at(p) if _overlay != null else {}


## A feature's id for feature_clicked: the candidate, the site, the area.
func feature_id(item: Dictionary) -> String:
	match str(item.get("kind", "")):
		"player":
			return str(item["row"].get("site_id", ""))
		"hexagon":
			return str(item["row"].get("id", ""))
		"candidates":
			return str(item["best"].get("id", ""))
		"site":
			var ids: PackedInt32Array = _sites.sites().get("ids", PackedInt32Array())
			return str(ids[item["index"]]) if item["index"] < ids.size() else ""
		"offshore_area", "fjord":
			return str(item.get("name", ""))
	return ""


func _get_tooltip(at: Vector2) -> String:
	var item := feature_at(at)
	if not item.is_empty():
		return GeoMapTips.feature(item, self)
	var area := area_at_local(at)
	if area > 0 and area <= _areas.size():
		return tr("UI_MAP_ZONE") % [area, str(_areas[area - 1]["name"])]
	return ""


# ── Building blocks ───────────────────────────────────────────────────────────

func _full_rect(c: Control) -> void:
	add_child(c)
	c.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	c.mouse_filter = Control.MOUSE_FILTER_IGNORE


func _tint_material() -> ShaderMaterial:
	_material = ShaderMaterial.new()
	_material.shader = TINT_SHADER
	_material.set_shader_parameter("po_index", GeoData.basemap_texture("po_index_national"))
	var tints := PackedColorArray()
	tints.resize(AREA_COUNT + 1)
	tints[0] = Color(0, 0, 0, 0)
	for a: Dictionary in _areas:
		var hex := str(a.get("tint", ""))
		tints[int(a["id"])] = Color(hex) if hex.begins_with("#") else Color(0, 0, 0, 0)
	_material.set_shader_parameter("tints", tints)
	_material.set_shader_parameter("tint_strength", TINT_STRENGTH)
	_material.set_shader_parameter("frame_color", MapPalette.deep_water())
	return _material


func _build_chips() -> VBoxContainer:
	var box := VBoxContainer.new()
	box.name = "LayerChips"
	box.add_theme_constant_override("separation", 4)
	for spec: Array in LAYERS:
		var chip := Button.new()
		chip.name = "Chip_" + str(spec[0])
		chip.toggle_mode = true
		chip.button_pressed = true
		chip.focus_mode = Control.FOCUS_ALL
		chip.alignment = HORIZONTAL_ALIGNMENT_LEFT
		chip.theme_type_variation = &"SmallButton"
		chip.text = tr(str(spec[1]))
		chip.tooltip_text = tr(str(spec[2]))
		chip.set_meta(&"layer", str(spec[0]))
		chip.toggled.connect(func(on: bool) -> void:
			set_layer(str(spec[0]), on)
			_style_chip(chip))
		_style_chip(chip)
		box.add_child(chip)
	return box


static func layer_color(layer: String) -> Color:
	match layer:
		"candidates":
			return MapPalette.salmon()
		"offshore":
			return MapPalette.buoy()
		"fjords":
			return MapPalette.fjord_red()
	return MapPalette.chart("info")


func _style_chip(chip: Button) -> void:
	var on := chip.button_pressed
	var edge := layer_color(str(chip.get_meta(&"layer", ""))) if on else Color(MapPalette.chart("hairline"), 1.0)
	for state in ["normal", "hover", "pressed", "hover_pressed", "focus"]:
		var sb := StyleBoxFlat.new()
		sb.bg_color = Color(MapPalette.chart("raised"), 0.97 if on else 0.82)
		sb.set_corner_radius_all(4)
		sb.content_margin_left = 11.0
		sb.content_margin_right = 10.0
		sb.content_margin_top = 4.0
		sb.content_margin_bottom = 4.0
		sb.border_color = MapPalette.chart("info") if state.begins_with("hover") else MapPalette.chart("hairline")
		sb.set_border_width_all(1)
		sb.border_width_left = 5
		sb.shadow_color = Color(0, 0, 0, 0.3)
		sb.shadow_size = 3
		if state == "focus":
			sb.draw_center = false
			sb.border_color = MapPalette.chart("focus")
			sb.set_border_width_all(2)
		else:
			sb.border_color = edge if state in ["normal", "pressed"] else sb.border_color
		chip.add_theme_stylebox_override(state, sb)
	var text := MapPalette.chart("text") if on else MapPalette.chart("muted")
	for c in ["font_color", "font_pressed_color", "font_hover_color", "font_hover_pressed_color", "font_focus_color"]:
		chip.add_theme_color_override(c, text)


## The hovered feature's ring: a tiny canvas item, so hovering re-records
## nothing else.
class _Hover extends Control:
	var _item: Dictionary = {}

	func _ready() -> void:
		mouse_filter = Control.MOUSE_FILTER_IGNORE

	func show_item(item: Dictionary) -> void:
		var same: bool = item.get("pos") == _item.get("pos") and item.get("kind") == _item.get("kind")
		_item = item
		if not same:
			queue_redraw()

	func _draw() -> void:
		if _item.is_empty() or not _item.has("pos"):
			return
		var r := float(_item.get("radius", 9.0)) + 3.0
		draw_arc(_item["pos"], r, 0.0, TAU, 32, Color(MapPalette.deep_water(), 0.8), 4.0, true)
		draw_arc(_item["pos"], r, 0.0, TAU, 32, MapPalette.snow(), 2.0, true)
