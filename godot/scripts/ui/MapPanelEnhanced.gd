extends Control

## Enhanced Map Panel with Environmental Data Layers
## Inspired by Mareano Portal (mareano.no) - The Sea in Maps and Pictures

signal close_requested
signal region_selected(region_id: String, environmental_data: Dictionary)
signal environmental_layer_changed(layer_id: int)

enum DataLayer {
	REGIONS, TEMPERATURE, OXYGEN, CURRENTS, QUALITY, DISEASE_RISK, SEASONAL
}

const BG_PANEL := Color("#FFFFFF")
const TEXT_TITLE := Color("#2E3440")
const TEXT_MUTED := Color("#7B8794")
const TEXT_ACCENT := Color("#0A4D68")

# Temperature gradient colors
const TEMP_COLDEST := Color("#1a5276")
const TEMP_COLD := Color("#2874a6") 
const TEMP_COOL := Color("#5dade2")
const TEMP_OPTIMAL := Color("#58d68d")
const TEMP_WARM := Color("#f4d03f")
const TEMP_HOT := Color("#e74c3c")

const REGIONS = [
	{"id": "po01", "name": "Svenskegrensen til Jæren", "pos": Vector2(0.22, 0.96), "temp_current": 10.5, "oxygen_current": 7.8, "current_speed": 0.25, "quality_score": 0.72, "disease_risk": "Low"},
	{"id": "po02", "name": "Ryfylke", "pos": Vector2(0.24, 0.88), "temp_current": 10.0, "oxygen_current": 8.0, "current_speed": 0.20, "quality_score": 0.78, "disease_risk": "Medium"},
	{"id": "po03", "name": "Karmøy til Sotra", "pos": Vector2(0.26, 0.81), "temp_current": 9.5, "oxygen_current": 8.2, "current_speed": 0.22, "quality_score": 0.82, "disease_risk": "Medium-High"},
	{"id": "po04", "name": "Nordhordland til Stadt", "pos": Vector2(0.28, 0.73), "temp_current": 9.0, "oxygen_current": 7.9, "current_speed": 0.30, "quality_score": 0.75, "disease_risk": "High"},
	{"id": "po05", "name": "Stadt til Hustadvika", "pos": Vector2(0.31, 0.65), "temp_current": 8.5, "oxygen_current": 8.5, "current_speed": 0.35, "quality_score": 0.88, "disease_risk": "Medium-High"},
	{"id": "po06", "name": "Nordmøre og Sør-Trøndelag", "pos": Vector2(0.34, 0.58), "temp_current": 8.0, "oxygen_current": 8.7, "current_speed": 0.32, "quality_score": 0.90, "disease_risk": "Low"},
	{"id": "po07", "name": "Nord-Trøndelag med Bindal", "pos": Vector2(0.38, 0.50), "temp_current": 7.5, "oxygen_current": 8.8, "current_speed": 0.25, "quality_score": 0.85, "disease_risk": "Low"},
	{"id": "po08", "name": "Helgeland til Bodø", "pos": Vector2(0.42, 0.42), "temp_current": 7.0, "oxygen_current": 9.0, "current_speed": 0.28, "quality_score": 0.87, "disease_risk": "Medium"},
	{"id": "po09", "name": "Vestfjorden og Vesterålen", "pos": Vector2(0.46, 0.35), "temp_current": 6.5, "oxygen_current": 9.2, "current_speed": 0.26, "quality_score": 0.86, "disease_risk": "Low"},
	{"id": "po10", "name": "Andøya til Senja", "pos": Vector2(0.50, 0.27), "temp_current": 6.0, "oxygen_current": 9.3, "current_speed": 0.26, "quality_score": 0.88, "disease_risk": "Low"},
	{"id": "po11", "name": "Kvaløya til Loppa", "pos": Vector2(0.54, 0.19), "temp_current": 5.5, "oxygen_current": 9.4, "current_speed": 0.30, "quality_score": 0.82, "disease_risk": "Low"},
	{"id": "po12", "name": "Vest-Finnmark", "pos": Vector2(0.58, 0.12), "temp_current": 5.0, "oxygen_current": 9.5, "current_speed": 0.28, "quality_score": 0.80, "disease_risk": "Low"},
	{"id": "po13", "name": "Øst-Finnmark", "pos": Vector2(0.64, 0.04), "temp_current": 4.5, "oxygen_current": 9.6, "current_speed": 0.25, "quality_score": 0.78, "disease_risk": "Low"}
]

var _selected_region_id: String = ""
var _current_layer: int = DataLayer.REGIONS
var _layer_buttons: Dictionary = {}
var _map_container: Control
var _legend_container: Control

func _ready() -> void:
	_build_ui()
	hide()

func _input(event: InputEvent) -> void:
	if not visible:
		return
	if event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()

func show_panel() -> void:
	show()
	modulate.a = 0.0
	var tw := create_tween()
	tw.tween_property(self, "modulate:a", 1.0, 0.35)

func hide_panel() -> void:
	var tw := create_tween()
	tw.tween_property(self, "modulate:a", 0.0, 0.25)
	tw.tween_callback(hide)

func _build_ui() -> void:
	anchor_right = 1.0
	anchor_bottom = 1.0

	var overlay := ColorRect.new()
	overlay.set_anchors_preset(Control.PRESET_FULL_RECT)
	overlay.color = Color(0.18, 0.20, 0.25, 0.60)
	add_child(overlay)

	var outer := MarginContainer.new()
	outer.set_anchors_preset(Control.PRESET_FULL_RECT)
	for side in ["left", "right", "top", "bottom"]:
		outer.add_theme_constant_override("margin_" + side, 40)
	add_child(outer)

	var panel_bg := _make_panel(BG_PANEL, 14)
	panel_bg.set_anchors_preset(Control.PRESET_FULL_RECT)
	outer.add_child(panel_bg)

	var root_vbox := VBoxContainer.new()
	root_vbox.set_anchors_preset(Control.PRESET_FULL_RECT)
	panel_bg.add_child(root_vbox)

	root_vbox.add_child(_build_header())
	root_vbox.add_child(_build_layer_toolbar())
	
	var body := HBoxContainer.new()
	body.size_flags_vertical = Control.SIZE_EXPAND_FILL
	root_vbox.add_child(body)

	body.add_child(_build_map_area())
	body.add_child(_build_detail_panel())

func _build_header() -> Control:
	var hdr := HBoxContainer.new()
	hdr.custom_minimum_size.y = 62
	
	var m := MarginContainer.new()
	m.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	for side in ["left", "right"]:
		m.add_theme_constant_override("margin_" + side, 24)
	hdr.add_child(m)
	
	var inner := HBoxContainer.new()
	m.add_child(inner)
	
	var title := Label.new()
	title.text = "SELECT REGION — NORWEGIAN COASTLINE"
	title.add_theme_font_size_override("font_size", 20)
	title.add_theme_color_override("font_color", TEXT_TITLE)
	title.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	inner.add_child(title)
	
	var close_btn := Button.new()
	close_btn.text = "✕"
	close_btn.pressed.connect(func(): hide())
	inner.add_child(close_btn)
	
	return hdr

func _build_layer_toolbar() -> HBoxContainer:
	var toolbar := HBoxContainer.new()
	toolbar.custom_minimum_size.y = 48
	
	var label := Label.new()
	label.text = "Data Layers:"
	toolbar.add_child(label)
	
	_layer_buttons[DataLayer.REGIONS] = _make_layer_btn("Regions", DataLayer.REGIONS, true)
	_layer_buttons[DataLayer.TEMPERATURE] = _make_layer_btn("🌡️ Temperature", DataLayer.TEMPERATURE)
	_layer_buttons[DataLayer.OXYGEN] = _make_layer_btn("💨 Oxygen", DataLayer.OXYGEN)
	_layer_buttons[DataLayer.CURRENTS] = _make_layer_btn("🌊 Currents", DataLayer.CURRENTS)
	_layer_buttons[DataLayer.QUALITY] = _make_layer_btn("⭐ Quality", DataLayer.QUALITY)
	
	for btn in _layer_buttons.values():
		toolbar.add_child(btn)
	
	return toolbar

func _make_layer_btn(text: String, layer: int, active: bool = false) -> Button:
	var btn := Button.new()
	btn.text = text
	btn.toggle_mode = true
	btn.button_pressed = active
	btn.pressed.connect(func(): _set_layer(layer))
	return btn

func _set_layer(layer: int) -> void:
	_current_layer = layer
	for l in _layer_buttons.keys():
		_layer_buttons[l].button_pressed = (l == layer)
	emit_signal("environmental_layer_changed", layer)
	_update_legend()
	queue_redraw()

func _build_map_area() -> Control:
	var map_bg := PanelContainer.new()
	map_bg.custom_minimum_size.x = 500
	map_bg.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	
	var margin := MarginContainer.new()
	for side in ["left", "right", "top", "bottom"]:
		margin.add_theme_constant_override("margin_" + side, 20)
	map_bg.add_child(margin)
	
	_map_container = Control.new()
	_map_container.set_anchors_preset(Control.PRESET_FULL_RECT)
	margin.add_child(_map_container)
	
	var coastline := _CoastlineDrawer.new()
	coastline.set_anchors_preset(Control.PRESET_FULL_RECT)
	_map_container.add_child(coastline)
	
	# Legend
	_legend_container = _build_legend()
	_legend_container.set_anchors_preset(Control.PRESET_BOTTOM_LEFT)
	_legend_container.position = Vector2(10, -10)
	_map_container.add_child(_legend_container)
	
	call_deferred("_build_region_markers")
	
	return map_bg

func _build_region_markers() -> void:
	if not is_instance_valid(_map_container):
		return
	var map_size := _map_container.size
	if map_size == Vector2.ZERO:
		map_size = Vector2(420, 600)

	for region in REGIONS:
		var marker := _RegionMarker.new()
		marker.region_data = region
		marker.current_layer = _current_layer
		var px: float = region["pos"].x * map_size.x
		var py: float = region["pos"].y * (map_size.y - 80) + 30
		marker.position = Vector2(px - 40, py - 15)
		marker.custom_minimum_size = Vector2(80, 30)
		marker.mouse_entered.connect(func(): _on_region_hover(region["id"]))
		marker.gui_input.connect(func(e): _on_region_input(e, region["id"]))
		_map_container.add_child(marker)

func _build_legend() -> PanelContainer:
	var panel := PanelContainer.new()
	
	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 4)
	panel.add_child(vbox)
	
	var title := Label.new()
	title.text = "Legend"
	title.add_theme_font_size_override("font_size", 12)
	vbox.add_child(title)
	
	return panel

func _update_legend() -> void:
	# Update legend based on current layer
	pass

func _build_detail_panel() -> PanelContainer:
	var panel := PanelContainer.new()
	panel.custom_minimum_size.x = 340
	
	var scroll := ScrollContainer.new()
	scroll.set_anchors_preset(Control.PRESET_FULL_RECT)
	panel.add_child(scroll)
	
	var vbox := VBoxContainer.new()
	scroll.add_child(vbox)
	
	var label := Label.new()
	label.text = "Select a region to view environmental data"
	vbox.add_child(label)
	
	return panel

func _on_region_hover(region_id: String) -> void:
	pass

func _on_region_input(event: InputEvent, region_id: String) -> void:
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		_selected_region_id = region_id
		emit_signal("region_selected", region_id, _get_region_data(region_id))

func _get_region_data(region_id: String) -> Dictionary:
	for r in REGIONS:
		if r["id"] == region_id:
			return r
	return {}

func _make_panel(bg: Color, radius: int) -> PanelContainer:
	var pc := PanelContainer.new()
	var style := StyleBoxFlat.new()
	style.bg_color = bg
	style.corner_radius_top_left = radius
	style.corner_radius_top_right = radius
	style.corner_radius_bottom_left = radius
	style.corner_radius_bottom_right = radius
	pc.add_theme_stylebox_override("panel", style)
	return pc

# Coastline drawer
class _CoastlineDrawer extends Control:
	func _draw() -> void:
		var w := size.x
		var h := size.y
		draw_rect(Rect2(0, 0, w, h), Color("#ECEFF4"))
		
		# Simplified Norway coastline
		var pts := PackedVector2Array([
			Vector2(0.72, 0.0), Vector2(0.68, 0.05), Vector2(0.65, 0.08),
			Vector2(0.62, 0.12), Vector2(0.58, 0.18), Vector2(0.56, 0.24),
			Vector2(0.53, 0.28), Vector2(0.50, 0.34), Vector2(0.47, 0.40),
			Vector2(0.44, 0.46), Vector2(0.40, 0.52), Vector2(0.37, 0.58),
			Vector2(0.34, 0.64), Vector2(0.31, 0.70), Vector2(0.28, 0.76),
			Vector2(0.25, 0.82), Vector2(0.23, 0.88), Vector2(0.22, 0.94),
			Vector2(0.22, 1.0), Vector2(1.0, 1.0), Vector2(1.0, 0.0)
		])
		var scaled: PackedVector2Array
		for p in pts:
			scaled.append(Vector2(p.x * w, p.y * h))
		draw_colored_polygon(scaled, Color(0.92, 0.94, 0.96, 0.95))

# Region marker with layer-aware coloring
class _RegionMarker extends Control:
	var region_data: Dictionary = {}
	var current_layer: int = 0
	
	func _draw() -> void:
		var color := _get_color_for_layer()
		draw_circle(Vector2(8, 15), 6, color)
		draw_string(get_theme_font("font"), Vector2(20, 20), region_data.get("name", ""), HORIZONTAL_ALIGNMENT_LEFT, -1, 12)
	
	func _get_color_for_layer() -> Color:
		match current_layer:
			0: # REGIONS - use disease risk
				var risk = region_data.get("disease_risk", "Low")
				if risk == "Low": return Color("#A3BE8C")
				if risk == "High": return Color("#BF616A")
				return Color("#EBCB8B")
			1: # TEMPERATURE
				var temp = region_data.get("temp_current", 10.0)
				if temp < 5: return Color("#1a5276")
				if temp < 8: return Color("#2874a6")
				if temp < 12: return Color("#5dade2")
				if temp < 16: return Color("#58d68d")
				if temp < 18: return Color("#f4d03f")
				return Color("#e74c3c")
			2: # OXYGEN
				var o2 = region_data.get("oxygen_current", 8.0)
				if o2 < 5: return Color("#922b21")
				if o2 < 7: return Color("#f39c12")
				return Color("#27ae60")
			4: # QUALITY
				var q = region_data.get("quality_score", 0.5)
				if q > 0.8: return Color("#27ae60")
				if q > 0.6: return Color("#58d68d")
				if q > 0.4: return Color("#f4d03f")
				if q > 0.2: return Color("#e67e22")
				return Color("#e74c3c")
			_:
				return Color("#5E81AC")
