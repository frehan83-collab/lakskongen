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
const CITY_FONT := 13
## The key starts open on a map at least this tall (1080p at 100 %).
const LEGEND_OPEN_MIN_HEIGHT := 700.0

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
var _carto: _Carto = null
var _graticule: _Graticule = null
var _chips: VBoxContainer = null
var _legend: MapLegend = null
var _hovered_area := 0
var _selected_area := 0
var _sites_signature := ""
var _player_rows: Array = []
var _laid_out_for := Vector2(-1.0, -1.0)
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
	_graticule = _Graticule.new()
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
	_candidates.set_offshore_candidates(bridge.offshore_candidates())
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
	_carto = _Carto.new()
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
## layout only when its signature moved, and the player's farms only when
## the expansion overview moved (overview_changed). Returns what changed.
func live_update(overview_changed: bool) -> PackedStringArray:
	var changed := PackedStringArray()
	var sig := bridge.sites_signature()
	if sig != _sites_signature:
		_sites_signature = sig
		_sites.set_sites(bridge.sites())
		changed.append("sites")
	if overview_changed and _update_players():
		changed.append("players")
	return changed


# ── Layout ────────────────────────────────────────────────────────────────────

func _queue_relayout() -> void:
	if _relayout_queued:
		return
	_relayout_queued = true
	_relayout.call_deferred()


## Lay the chart out for the current size (once per size).
func relayout(force: bool = false) -> void:
	_relayout_queued = false
	if size.x < 32.0 or size.y < 32.0 or (size == _laid_out_for and not force):
		return
	_laid_out_for = size
	var d := drawn_rect()
	var s := d.size.x / _tex_size.x
	_overlay.set_view(d.position, s, _info["bounds_25833"], _info["res_m"])
	_sites.set_view(d.position, s, _info["bounds_25833"], _info["res_m"])
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
			var r := GeoSiteLayer.badge_radius(int(c["count"])) + 1.0
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
		var r := GeoSiteLayer.badge_radius(n) + 3.0 if n > 1 else 5.0
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


## Re-read the player's farms; true when they changed.
func _update_players(force: bool = false) -> bool:
	var rows := bridge.player_site_locations()
	if rows == _player_rows and not force:
		return false
	_player_rows = rows
	var pts := PackedVector2Array()
	for r: Dictionary in rows:
		pts.append(utm_to_local(float(r.get("x", 0.0)), float(r.get("y", 0.0))))
	_players.set_marks(rows, pts)
	return true


# ── Input and tooltips ────────────────────────────────────────────────────────

func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseMotion:
		set_hovered_area(area_at_local(event.position))
		_hover.show_item(feature_at(event.position))
	elif event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		var area := area_at_local(event.position)
		var item := feature_at(event.position)
		if not item.is_empty():
			feature_clicked.emit(str(item["kind"]), feature_id(item))
		if area > 0:
			if event.double_click:
				area_drill_requested.emit(area)
			else:
				area_clicked.emit(area)
		accept_event()


func _notification(what: int) -> void:
	if what == NOTIFICATION_MOUSE_EXIT and _hover != null:
		_hover.show_item({})
		set_hovered_area(0)


## The feature under a point, top first: the player's farm, a candidate or
## hexagon, a site or cluster, an offshore area, a salmon fjord. {} for none.
func feature_at(p: Vector2) -> Dictionary:
	if _players != null:
		var row := _players.row_at(p)
		if not row.is_empty():
			return {"kind": "player", "row": row, "pos": p}
	var candidate := _candidates.item_at(p) if _candidates != null else {}
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


## The chart's graticule: parallels every 2° and meridians every 5° through
## the same projection as the map, faint over the image and across the deep
## water around it, with the parallels' latitudes along the right edge.
class _Graticule extends Control:
	const PARALLELS: Array[int] = [58, 60, 62, 64, 66, 68, 70]
	const MERIDIANS: Array[int] = [5, 10, 15, 20, 25, 30]
	const LABEL_FONT := 12
	var _lines: Array[PackedVector2Array] = []
	var _labels: Array = []  # [text, baseline position]

	func _ready() -> void:
		mouse_filter = Control.MOUSE_FILTER_IGNORE

	func layout_lines(view: GeoMapView, keep_clear: Array[Rect2]) -> void:
		_lines.clear()
		_labels.clear()
		var font := get_theme_font(&"font", &"BodySmall")
		for lat in PARALLELS:
			var line := PackedVector2Array()
			for k in 81:
				var lon := -20.0 + 1.0 * k
				var xy := GeoProjection.utm33(lon, float(lat))
				line.append(view.utm_to_local(xy[0], xy[1]))
			_lines.append(line)
			var text := tr("MAP_GEO_LAT") % lat
			var w := font.get_string_size(text, HORIZONTAL_ALIGNMENT_LEFT, -1, LABEL_FONT).x if font != null else 40.0
			var x := size.x - w - 10.0
			var y := _y_at(line, size.x - 6.0)
			var r := Rect2(Vector2(x - 2.0, y - LABEL_FONT - 2.0), Vector2(w + 4.0, LABEL_FONT + 6.0))
			if y > 20.0 and y < size.y - 6.0 and not keep_clear.any(func(o: Rect2) -> bool: return o.intersects(r)):
				_labels.append([text, Vector2(x, y - 3.0)])
		for lon in MERIDIANS:
			var line := PackedVector2Array()
			for k in 41:
				var xy := GeoProjection.utm33(float(lon), 55.0 + 0.45 * k)
				line.append(view.utm_to_local(xy[0], xy[1]))
			_lines.append(line)
		queue_redraw()

	## The y of a polyline where it crosses x (its last sample before x).
	static func _y_at(line: PackedVector2Array, x: float) -> float:
		for i in range(line.size() - 1):
			var a := line[i]
			var b := line[i + 1]
			if (a.x - x) * (b.x - x) <= 0.0 and absf(b.x - a.x) > 0.001:
				return lerpf(a.y, b.y, (x - a.x) / (b.x - a.x))
		return -1.0

	func _draw() -> void:
		var snow := MapPalette.snow()
		for line in _lines:
			draw_polyline(line, Color(snow, 0.11), 1.0, true)
		var font := get_theme_font(&"font", &"BodySmall")
		if font == null:
			return
		for l: Array in _labels:
			draw_string_outline(font, l[1], str(l[0]), HORIZONTAL_ALIGNMENT_LEFT, -1, LABEL_FONT, 3,
				Color(MapPalette.deep_water(), 0.9))
			draw_string(font, l[1], str(l[0]), HORIZONTAL_ALIGNMENT_LEFT, -1, LABEL_FONT, Color(snow, 0.6))


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


## The chart's furniture: city names that keep clear of the plates, a scale
## bar and a north arrow. One small canvas item, recorded on layout only.
class _Carto extends Control:
	## The city name rects drawn (GeoMapView.city_label_rects).
	var placed: Array[Rect2] = []
	var _cities: Array = []  # [name, anchor, label rect]
	var _scale_rect := Rect2()
	var _north_rect := Rect2()
	var _metres_per_px := 1.0
	var _km := 100

	func _ready() -> void:
		mouse_filter = Control.MOUSE_FILTER_IGNORE

	## Scale bar (bottom left) and north arrow (top right), in the deep-water
	## frame beside a portrait map, so neither sits on the coast.
	func layout_frame(drawn: Rect2, metres_per_px: float) -> void:
		_metres_per_px = metres_per_px
		_km = 200 if 200000.0 / metres_per_px <= drawn.size.x * 0.28 else 100
		var bar_px := float(_km) * 1000.0 / metres_per_px
		_scale_rect = Rect2(Vector2(14.0, size.y - 34.0), Vector2(bar_px + 46.0, 22.0))
		_north_rect = Rect2(Vector2(size.x - 40.0, 10.0), Vector2(26.0, 38.0))
		queue_redraw()

	func frame_rects() -> Array[Rect2]:
		return [_scale_rect.grow(3.0), _north_rect.grow(3.0)]

	## Place the city names: east, west, above or below the city, else left out.
	func layout_cities(cities: Array, bounds: Rect2, taken: Array[Rect2], label_only: Array[Rect2] = []) -> void:
		var font := get_theme_font(&"font", &"BodySmallStrong")
		var anchors := PackedVector2Array()
		var sizes := PackedVector2Array()
		var usable: Array = []
		var busy: Array[Rect2] = taken.duplicate()
		for c: Array in cities:
			var dot := Rect2(c[1] - Vector2(3.5, 3.5), Vector2(7.0, 7.0))
			if not bounds.encloses(dot) or _hits(dot, busy):
				continue
			usable.append(c)
			anchors.append(c[1])
			var sz := font.get_string_size(str(c[0]), HORIZONTAL_ALIGNMENT_LEFT, -1, CITY_FONT) if font != null else Vector2(60, 14)
			sizes.append(Vector2(ceilf(sz.x) + 2.0, float(CITY_FONT) + 5.0))
		for c: Array in usable:
			busy.append(Rect2(c[1] - Vector2(3.5, 3.5), Vector2(7.0, 7.0)))
		var rects := MapLabelPlacer.place_optional(anchors, sizes, 6.0, bounds, busy, label_only)
		placed.clear()
		_cities.clear()
		for i in usable.size():
			if rects[i].size == Vector2.ZERO:
				continue
			placed.append(rects[i])
			_cities.append([usable[i][0], usable[i][1], rects[i]])
		queue_redraw()

	static func _hits(r: Rect2, rects: Array[Rect2]) -> bool:
		for o in rects:
			if o.intersects(r):
				return true
		return false

	func _draw() -> void:
		var font := get_theme_font(&"font", &"BodySmallStrong")
		var deep := MapPalette.deep_water()
		var snow := MapPalette.snow()
		for c: Array in _cities:
			var p: Vector2 = c[1]
			draw_circle(p, 3.5, deep)
			draw_circle(p, 2.2, snow)
			if font != null:
				var r: Rect2 = c[2]
				var at := Vector2(r.position.x + 1.0, r.position.y + font.get_ascent(CITY_FONT) + 2.0)
				draw_string_outline(font, at, str(c[0]), HORIZONTAL_ALIGNMENT_LEFT, -1, CITY_FONT, 4, Color(deep, 0.92))
				draw_string(font, at, str(c[0]), HORIZONTAL_ALIGNMENT_LEFT, -1, CITY_FONT, snow)
		_draw_scale(font, deep, snow)
		_draw_north(font, deep, snow)

	func _draw_scale(font: Font, deep: Color, snow: Color) -> void:
		if _scale_rect.size == Vector2.ZERO:
			return
		var bar_px := float(_km) * 1000.0 / _metres_per_px
		var y := _scale_rect.end.y - 6.0
		var x0 := _scale_rect.position.x
		draw_rect(Rect2(x0 - 1.0, y - 4.0, bar_px + 2.0, 6.0), Color(deep, 0.85))
		draw_rect(Rect2(x0, y - 3.0, bar_px * 0.5, 4.0), snow)
		draw_rect(Rect2(x0 + bar_px * 0.5, y - 3.0, bar_px * 0.5, 4.0), Color(snow, 0.45))
		if font != null:
			var text := tr("MAP_GEO_SCALE_KM") % _km
			var at := Vector2(x0 + bar_px + 7.0, y + 2.0)
			draw_string_outline(font, at, text, HORIZONTAL_ALIGNMENT_LEFT, -1, CITY_FONT, 4, Color(deep, 0.92))
			draw_string(font, at, text, HORIZONTAL_ALIGNMENT_LEFT, -1, CITY_FONT, snow)

	func _draw_north(font: Font, deep: Color, snow: Color) -> void:
		if _north_rect.size == Vector2.ZERO:
			return
		var c := _north_rect.position + Vector2(_north_rect.size.x * 0.5, 24.0)
		var tip := c + Vector2(0.0, -14.0)
		var arrow := PackedVector2Array([tip, c + Vector2(6.0, 6.0), c + Vector2(0.0, 2.0), c + Vector2(-6.0, 6.0)])
		draw_colored_polygon(arrow, Color(deep, 0.85))
		draw_colored_polygon(PackedVector2Array([tip, c + Vector2(0.0, 2.0), c + Vector2(-6.0, 6.0)]), snow)
		draw_polyline(PackedVector2Array([tip, c + Vector2(6.0, 6.0), c + Vector2(0.0, 2.0), c + Vector2(-6.0, 6.0), tip]),
			snow, 1.0, true)
		if font != null:
			var text := tr("MAP_GEO_NORTH")
			var w := font.get_string_size(text, HORIZONTAL_ALIGNMENT_LEFT, -1, CITY_FONT).x
			var at := Vector2(c.x - w * 0.5, _north_rect.position.y + 7.0)
			draw_string_outline(font, at, text, HORIZONTAL_ALIGNMENT_LEFT, -1, CITY_FONT, 4, Color(deep, 0.92))
			draw_string(font, at, text, HORIZONTAL_ALIGNMENT_LEFT, -1, CITY_FONT, snow)
