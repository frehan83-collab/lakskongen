class_name GeoRegionView
extends Control
## The map's region level (geo stages 2-3 plan Track D): one production
## area's base map (basemap/poNN.jpg) with
## - the raw suitability heat (map_suitability.gdshader over the area's raw
##   suitability/poNN.png: cool-to-gold score, hatched hard exclusions);
## - the constraint layers (GeoRegionOverlay, from GeoRegionData);
## - the area's rival localities in owner bands (majors in their 3D colour,
##   CompetitorFarmVisualManager.band_color_for; independents slate);
## - the company's sites, the area's 12 open-pen candidate stars (free,
##   suggested, taken or yours) and the Havbruk til havs hexagons;
## - layer chips, a legend and a hover readout of the cell under the pointer
##   (score, why, depth) read from the CPU copy of the grid.
## Every sim read goes through GeoRegionBridge (the Track A/B adapter).
## Stars, hexagons and sites are small Controls (click, tooltip); the rival
## icons are one _draw in _SiteLayer, re-recorded only on a resize, a new
## area, a toggle or new rival data (redraw_count), never on a tick.

signal candidate_chosen(row: Dictionary)
signal offshore_chosen(row: Dictionary)
signal player_site_chosen(row: Dictionary)
signal rival_chosen(info: Dictionary)

const SuitabilityShader := preload("res://shaders/map_suitability.gdshader")

const STAR_PX := 26.0
const HEX_PX := 24.0
const SITE_PX := 22.0
## Layer chip groups, in order: the heat, the restrictions, the farms
## (site polygons and rival icons), the candidates (stars and hexagons).
const CHIPS: Array[String] = ["heat", "restrictions", "sites", "candidates"]
const STATE_KEYS := {"free": "MAP_REGION_STATE_FREE", "suggested": "MAP_REGION_STATE_SUGGESTED",
	"taken": "MAP_REGION_STATE_TAKEN", "yours": "MAP_REGION_STATE_YOURS"}

static var _heat_textures: Dictionary = {}  # po -> ImageTexture of the raw grid

var _po: int = 0
var _home: int = 0
var _sim: Object = null
var _candidates: Array = []
var _offshore: Array = []
var _player_sites: Array = []
var _player_ids: Dictionary = {}
var _sites_signature: String = "-"
var _uniforms: Dictionary = {}
var _hidden: Dictionary = {}

var _backdrop: ColorRect
var _base: TextureRect
var _heat: TextureRect
var _clip: Control
var _overlay: GeoRegionOverlay
var _site_layer: GeoRegionRivals
var _markers: Control
var _chips_box: VBoxContainer
var _chips: Dictionary = {}
var _legend: VBoxContainer
var _readout_card: PanelContainer
var _readout: Label


func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_STOP
	clip_contents = true
	_backdrop = ColorRect.new()
	_backdrop.name = "Backdrop"
	_backdrop.color = GeoRegionIcons.BACKDROP
	_backdrop.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_full(_backdrop)
	_base = _texture_rect("BaseMap")
	_heat = _texture_rect("Heat")
	var mat := ShaderMaterial.new()
	mat.shader = SuitabilityShader
	_heat.material = mat
	# The vector layers draw inside the base map's rect only (a fairway or a
	# protected area running off the map stops at its edge).
	_clip = Control.new()
	_clip.name = "MapClip"
	_clip.clip_contents = true
	_clip.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(_clip)
	_overlay = GeoRegionOverlay.new()
	_overlay.name = "Overlay"
	_clip.add_child(_overlay)
	_overlay.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_site_layer = GeoRegionRivals.new()
	_site_layer.name = "Rivals"
	_site_layer.view = self
	_clip.add_child(_site_layer)
	_site_layer.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_markers = Control.new()
	_markers.name = "Markers"
	_markers.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_full(_markers)
	_build_chips()
	_build_legend()
	_build_readout()
	resized.connect(_layout_markers)


## Show a production area (1-13); home_po is the company's home area (the
## open-sea plot hav_1 binds from it). Returns false for an unknown area.
func show_area(po: int, sim: Object, home_po: int = 0) -> bool:
	var t := GeoRegionMath.transform(po)
	if t.is_empty():
		return false
	_sim = sim
	_home = home_po
	var changed := po != _po
	_po = po
	if changed:
		_base.texture = GeoData.basemap_texture(GeoRegionMath.basemap_key(po))
		_heat.texture = _base.texture
		_uniforms = GeoRegionMath.shader_uniforms(po)
		var mat := _heat.material as ShaderMaterial
		for k: String in _uniforms:
			mat.set_shader_parameter(k, _uniforms[k])
		mat.set_shader_parameter("suit_tex", _heat_texture(po))
		_overlay.set_area(GeoRegionData.area(po))
		_sites_signature = "-"
	refresh_live(true)
	_readout.text = tr("MAP_REGION_READOUT_HINT")
	return true


## Re-read what the sim changes: the candidates' states, the company's sites
## and (when the layout's signature moved) the rivals and hexagons.
func refresh_live(force: bool = false) -> void:
	if _po == 0:
		return
	_candidates = GeoRegionBridge.sea_candidates(_sim, _po)
	var every_site := GeoRegionBridge.player_site_locations(_sim)
	_player_ids = {}
	for r: Dictionary in every_site:
		_player_ids[String(r.get("site_id", ""))] = true
	_player_sites = every_site.filter(func(r: Dictionary) -> bool: return int(r.get("po", 0)) == _po)
	var signature := GeoRegionBridge.geo_sites_signature(_sim)
	if force or signature != _sites_signature:
		_sites_signature = signature
		_site_layer.set_sites(GeoRegionBridge.geo_sites(_sim), _po)
		_offshore = GeoRegionBridge.offshore_candidates(_sim, _home).filter(_hexagon_in_view)
	_rebuild_markers()


func area() -> int:
	return _po


## The area's candidate rows as last read (rank order).
func candidates() -> Array:
	return _candidates


## The Havbruk til havs rows drawn as hexagons in this area.
func offshore() -> Array:
	return _offshore


## The company's site rows drawn in this area.
func player_sites() -> Array:
	return _player_sites


## A candidate's state: "yours", "taken", "suggested" or "free".
func state_of(row: Dictionary) -> String:
	var holder := String(row.get("taken_by_site_id", ""))
	if holder != "":
		return "yours" if _player_ids.has(holder) else "taken"
	return "suggested" if bool(row.get("suggested", false)) else "free"


func star(candidate_id: String) -> Control:
	return _markers.get_node_or_null("MapStar_" + candidate_id) as Control


func hexagon(candidate_id: String) -> Control:
	return _markers.get_node_or_null("MapHex_" + candidate_id) as Control


func site_marker(site_id: String) -> Control:
	return _markers.get_node_or_null("MapSite_" + site_id) as Control


func chip(group: String) -> Button:
	return _chips.get(group) as Button


func legend() -> Control:
	return _legend


func readout() -> Label:
	return _readout


func overlay() -> GeoRegionOverlay:
	return _overlay


func rival_layer() -> Control:
	return _site_layer


## The heat shader's uniforms as set (GeoRegionMath.shader_uniforms(po)).
func heat_uniforms() -> Dictionary:
	return _uniforms


func heat_material() -> ShaderMaterial:
	return _heat.material as ShaderMaterial


## The base map's drawn rect in this view.
func map_rect() -> Rect2:
	var t := GeoRegionMath.transform(_po)
	return fit_rect(t.get("map_size", Vector2.ONE), size)


## Base-map pixel -> this view's local position.
func to_view(map_px: Vector2) -> Vector2:
	var r := map_rect()
	var t := GeoRegionMath.transform(_po)
	var s := r.size.x / float((t.get("map_size", Vector2.ONE) as Vector2).x)
	return r.position + map_px * s


## This view's local position -> base-map pixel.
func to_map(local: Vector2) -> Vector2:
	var r := map_rect()
	var t := GeoRegionMath.transform(_po)
	var s := r.size.x / float((t.get("map_size", Vector2.ONE) as Vector2).x)
	return (local - r.position) / s if s > 0.0 else Vector2.INF


## The translation key of a locality kind (any kind outside the salmonid
## three reads as another species).
static func kind_key(kind: String) -> String:
	if kind in ["sea_salmonid", "land_salmonid", "offshore_salmonid"]:
		return "MAP_SITE_KIND_" + kind.to_upper()
	return "MAP_SITE_KIND_OTHER"


## The fit of a map of map_size into a box (KEEP_ASPECT_CENTERED).
static func fit_rect(map_size: Vector2, box: Vector2) -> Rect2:
	if map_size.x <= 0.0 or map_size.y <= 0.0 or box.x <= 0.0 or box.y <= 0.0:
		return Rect2()
	var s := minf(box.x / map_size.x, box.y / map_size.y)
	var drawn := map_size * s
	return Rect2((box - drawn) * 0.5, drawn)


## The readout for the cell under a view position (tests call it directly
## too): score, why and depth from the CPU grid, or the hint off the grid.
func readout_at(local: Vector2) -> String:
	var px := to_map(local)
	var cell := GeoRegionMath.cell_of_uv(_po, GeoRegionMath.suit_uv(_po, px))
	var s := GeoRegionMath.sample(_po, cell)
	if s.is_empty() or (not bool(s["legal"]) and int(s["reason"]) in [0, 1, 2]):
		return tr("MAP_REGION_READOUT_HINT")
	var why := tr(GeoRegionMath.reason_key(int(s["reason"])))
	var depth := SimFormatters.integer(int(s["depth_m"]))
	if bool(s["legal"]):
		return tr("MAP_REGION_READOUT_SCORED") % [SimFormatters.num(float(s["score"]), 2), why, depth]
	if int(s["depth_m"]) <= 0:
		return tr("MAP_REGION_READOUT_CLOSED_NO_DEPTH") % why
	return tr("MAP_REGION_READOUT_CLOSED") % [why, depth]


func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseMotion and _po > 0:
		_readout.text = readout_at((event as InputEventMouseMotion).position)


# ── Markers ───────────────────────────────────────────────────────────────────

func _rebuild_markers() -> void:
	for child in _markers.get_children():
		_markers.remove_child(child)
		child.queue_free()
	for row: Dictionary in _offshore:
		var m := _Marker.new()
		m.name = "MapHex_" + String(row.get("id", ""))
		m.kind = "hex"
		m.bound = String(row.get("bound_plot_id", "")) != ""
		m.custom_minimum_size = Vector2(HEX_PX, HEX_PX)
		m.tooltip_text = _hex_tip(row)
		m.pressed.connect(func() -> void: offshore_chosen.emit(row))
		m.set_meta(&"row", row)
		_markers.add_child(m)
	for row: Dictionary in _candidates:
		var m := _Marker.new()
		m.name = "MapStar_" + String(row.get("id", ""))
		m.kind = "star"
		m.state = state_of(row)
		m.custom_minimum_size = Vector2(STAR_PX, STAR_PX)
		m.tooltip_text = _star_tip(row)
		m.pressed.connect(func() -> void: candidate_chosen.emit(row))
		m.set_meta(&"row", row)
		_markers.add_child(m)
	for row: Dictionary in _player_sites:
		var m := _Marker.new()
		m.name = "MapSite_" + String(row.get("site_id", ""))
		m.kind = "site"
		m.custom_minimum_size = Vector2(SITE_PX, SITE_PX)
		m.tooltip_text = _site_tip(row)
		m.pressed.connect(func() -> void: player_site_chosen.emit(row))
		m.set_meta(&"row", row)
		_markers.add_child(m)
	_apply_marker_visibility()
	_layout_markers()


func _layout_markers() -> void:
	if _po == 0:
		return
	var r := map_rect()
	_clip.position = r.position
	_clip.size = r.size
	for m: Control in _markers.get_children():
		var row: Dictionary = m.get_meta(&"row", {})
		var at := to_view(GeoRegionMath.map_px(_po, float(row.get("x", 0.0)), float(row.get("y", 0.0))))
		if m.name.begins_with("MapHex_"):
			var pinned := _pin_inside(at, r, HEX_PX * 0.5 + 4.0)
			# Off the map: a chevron on the hexagon points the way to it.
			(m as _Marker).off_map = (at - pinned).normalized() if at.distance_to(pinned) > 0.5 else Vector2.ZERO
			(m as _Marker).queue_redraw()
			at = pinned
		m.size = m.custom_minimum_size
		m.position = at - m.size * 0.5
		m.set_meta(&"anchor", at)


## A hexagon off the base map is pinned to its edge (it points the way).
static func _pin_inside(at: Vector2, r: Rect2, inset: float) -> Vector2:
	return Vector2(clampf(at.x, r.position.x + inset, r.end.x - inset), clampf(at.y, r.position.y + inset, r.end.y - inset))


## An offshore candidate belongs to the view when it lies on the area's map
## or is the area's own (po), wherever it lies.
func _hexagon_in_view(row: Dictionary) -> bool:
	if int(row.get("po", 0)) == _po:
		return true
	var px := GeoRegionMath.map_px(_po, float(row.get("x", 0.0)), float(row.get("y", 0.0)))
	return Rect2(Vector2.ZERO, GeoRegionMath.transform(_po)["map_size"]).has_point(px)


func _apply_marker_visibility() -> void:
	for m: Control in _markers.get_children():
		if m.name.begins_with("MapSite_"):
			m.visible = true
		else:
			m.visible = not _hidden.has("candidates")


func _star_tip(row: Dictionary) -> String:
	var lines := PackedStringArray()
	lines.append(tr("MAP_REGION_STAR_TIP_HEAD") % [int(row.get("rank", 0)), String(row.get("label", ""))])
	lines.append(tr("MAP_REGION_STAR_TIP_SCORE") % [SimFormatters.num(float(row.get("score", 0.0)), 3),
		tr("MAP_SITE_TERM_" + String(row.get("binding", "none")).to_upper())])
	lines.append(tr("MAP_REGION_STAR_TIP_PLACE") % [SimFormatters.integer(int(row.get("depth_m", 0))),
		SimFormatters.num(float(row.get("nearest_farm_km", 0.0)), 1)])
	lines.append(tr(STATE_KEYS[state_of(row)]))
	return "\n".join(lines)


func _hex_tip(row: Dictionary) -> String:
	var head := tr("MAP_REGION_HEX_TIP") % [String(row.get("area", "")), SimFormatters.integer(int(row.get("depth_m", 0)))]
	var how := tr("MAP_REGION_HEX_BOUND") if String(row.get("bound_plot_id", "")) != "" else tr("MAP_REGION_HEX_NOT_OFFERED")
	return head + "\n" + how


func _site_tip(row: Dictionary) -> String:
	var tip := tr("MAP_REGION_SITE_TIP") % [String(row.get("site_label", "")), String(row.get("label", ""))]
	if String(row.get("source", "")) != "candidate":
		tip += "\n" + tr("MAP_REGION_SITE_APPROX")
	return tip


# ── Chips, legend and readout ─────────────────────────────────────────────────

func _build_chips() -> void:
	var card := PanelContainer.new()
	card.name = "ChipsCard"
	card.theme_type_variation = &"CardCompact"
	card.set_anchors_and_offsets_preset(Control.PRESET_TOP_RIGHT)
	card.grow_horizontal = Control.GROW_DIRECTION_BEGIN
	card.position = Vector2(-12, 12)
	_chips_box = VBoxContainer.new()
	_chips_box.name = "Chips"
	_chips_box.add_theme_constant_override("separation", LakskongenTheme.SPACE_XS)
	card.add_child(_chips_box)
	for group: String in CHIPS:
		var b := GeoRegionIcons.ChipButton.new()
		b.name = "Chip_" + group
		b.kind = group
		b.icon = GeoRegionIcons.icon_space()
		b.alignment = HORIZONTAL_ALIGNMENT_LEFT
		b.toggle_mode = true
		b.button_pressed = true
		b.focus_mode = Control.FOCUS_ALL
		b.theme_type_variation = &"SmallSecondaryButton"
		b.text = tr("MAP_REGION_CHIP_" + group.to_upper())
		b.tooltip_text = tr("MAP_REGION_CHIP_" + group.to_upper() + "_TIP")
		b.toggled.connect(func(on: bool) -> void: set_group_visible(group, on))
		_chips_box.add_child(b)
		_chips[group] = b
	_legend = VBoxContainer.new()
	_legend.name = "Legend"
	_legend.add_theme_constant_override("separation", 2)
	_legend.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_chips_box.add_child(_legend)
	add_child(card)
	_anchor_corner.call_deferred(card, Control.PRESET_TOP_RIGHT)


## Show or hide a chip's group; the chip follows.
func set_group_visible(group: String, on: bool) -> void:
	if on:
		_hidden.erase(group)
	else:
		_hidden[group] = true
	var b: Button = _chips.get(group)
	if b != null and b.button_pressed != on:
		b.set_pressed_no_signal(on)
	match group:
		"heat":
			_heat.visible = on
		"restrictions":
			_overlay.set_group_visible("restrictions", on)
		"sites":
			_overlay.set_group_visible("sites", on)
			_site_layer.visible = on
		"candidates":
			_apply_marker_visibility()


func group_visible(group: String) -> bool:
	return not _hidden.has(group)


## The key under the chips: what the chips' icons do not say (each chip
## carries its own layer's icon; the tooltips carry the detail).
func _build_legend() -> void:
	var box := _legend
	for entry in [["hatch", "MAP_REGION_LEGEND_CLOSED"], ["site", "MAP_REGION_LEGEND_YOURS"],
			["hex", "MAP_REGION_LEGEND_OFFSHORE"]]:
		var row := HBoxContainer.new()
		row.name = "Legend_" + String(entry[0])
		row.add_theme_constant_override("separation", LakskongenTheme.SPACE_SM)
		row.mouse_filter = Control.MOUSE_FILTER_PASS
		var icon := GeoRegionIcons.LegendIcon.new()
		icon.kind = entry[0]
		icon.custom_minimum_size = Vector2(GeoRegionIcons.ICON_W, GeoRegionIcons.ICON_H)
		icon.size_flags_vertical = Control.SIZE_SHRINK_CENTER
		icon.mouse_filter = Control.MOUSE_FILTER_IGNORE
		row.add_child(icon)
		var l := Label.new()
		l.text = tr(String(entry[1]))
		l.theme_type_variation = &"Small"
		row.add_child(l)
		row.tooltip_text = tr(String(entry[1]) + "_TIP")
		box.add_child(row)


func _build_readout() -> void:
	_readout_card = PanelContainer.new()
	_readout_card.name = "ReadoutCard"
	_readout_card.theme_type_variation = &"CardCompact"
	_readout_card.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_readout = Label.new()
	_readout.name = "ReadoutLabel"
	_readout.theme_type_variation = &"BodySmall"
	_readout.text = tr("MAP_REGION_READOUT_HINT")
	_readout_card.add_child(_readout)
	add_child(_readout_card)
	_anchor_corner.call_deferred(_readout_card, Control.PRESET_BOTTOM_RIGHT)


func _anchor_corner(c: Control, preset: Control.LayoutPreset) -> void:
	c.set_anchors_and_offsets_preset(preset, Control.PRESET_MODE_MINSIZE, 12)
	match preset:
		Control.PRESET_TOP_RIGHT:
			c.grow_horizontal = Control.GROW_DIRECTION_BEGIN
		Control.PRESET_BOTTOM_LEFT:
			c.grow_vertical = Control.GROW_DIRECTION_BEGIN
		Control.PRESET_BOTTOM_RIGHT:
			c.grow_horizontal = Control.GROW_DIRECTION_BEGIN
			c.grow_vertical = Control.GROW_DIRECTION_BEGIN


# ── Helpers ───────────────────────────────────────────────────────────────────

func _texture_rect(node_name: String) -> TextureRect:
	var r := TextureRect.new()
	r.name = node_name
	r.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	r.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	r.mouse_filter = Control.MOUSE_FILTER_IGNORE
	r.texture_filter = CanvasItem.TEXTURE_FILTER_LINEAR_WITH_MIPMAPS
	_full(r)
	return r


func _full(c: Control) -> void:
	add_child(c)
	c.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)


static func _heat_texture(po: int) -> ImageTexture:
	if _heat_textures.has(po):
		return _heat_textures[po]
	var grid := GeoData.suitability_grid(po)
	if grid.is_empty():
		return null
	var tex := ImageTexture.create_from_image(grid["image"])
	_heat_textures[po] = tex
	return tex


## A star, a hexagon or a company site on the map: a small clickable Control.
class _Marker extends Control:
	signal pressed
	var kind := "star"
	var state := "free"
	var bound := false
	## For a hexagon pinned to the map edge: the way to its real position.
	var off_map := Vector2.ZERO
	var hovered := false

	func _ready() -> void:
		mouse_filter = Control.MOUSE_FILTER_STOP
		mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
		mouse_entered.connect(func() -> void:
			hovered = true
			queue_redraw())
		mouse_exited.connect(func() -> void:
			hovered = false
			queue_redraw())

	func _gui_input(event: InputEvent) -> void:
		var mb := event as InputEventMouseButton
		if mb != null and mb.pressed and mb.button_index == MOUSE_BUTTON_LEFT:
			accept_event()
			pressed.emit()

	func _draw() -> void:
		var c := size * 0.5
		var r := minf(size.x, size.y) * 0.5 - 1.0
		match kind:
			"star":
				GeoRegionIcons.draw_star(self, c, r * (1.12 if hovered else 1.0), state)
			"hex":
				GeoRegionIcons.draw_hexagon(self, c, r * (1.1 if hovered else 1.0), bound)
				if off_map != Vector2.ZERO:
					var side := off_map.orthogonal() * 4.0
					draw_colored_polygon(PackedVector2Array([c + off_map * (r + 7.0), c + off_map * (r + 2.0) + side,
						c + off_map * (r + 2.0) - side]), GeoRegionIcons.BUOY)
			"site":
				draw_circle(c, r, GeoRegionIcons.HALO)
				draw_circle(c, r - 3.0, GeoRegionIcons.SALMON)
				draw_arc(c, r - 1.5, 0.0, TAU, 32, GeoRegionIcons.SNOW, 2.0, true)
				if hovered:
					draw_arc(c, r + 0.5, 0.0, TAU, 32, GeoRegionIcons.SALMON, 1.5, true)
