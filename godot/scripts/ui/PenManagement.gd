extends Control

## PenManagement — Pen list and detail panel.
## Shows all pens with stats, allows feed/delouse/harvest actions.

signal pen_selected(pen_id: String)
signal action_requested(action: String, pen_id: String)

# ── UI References ─────────────────────────────────────────────────────────────
@onready var pen_list: VBoxContainer = $Panel/HSplit/LeftPanel/ScrollContainer/PenList
@onready var detail_panel: VBoxContainer = $Panel/HSplit/RightPanel/DetailPanel
@onready var no_pen_label: Label = $Panel/HSplit/RightPanel/NoPenLabel

# Detail panel elements
@onready var pen_name_label: Label = $Panel/HSplit/RightPanel/DetailPanel/NameLabel
@onready var fish_count_label: Label = $Panel/HSplit/RightPanel/DetailPanel/Stats/FishCountRow/Value
@onready var biomass_label: Label = $Panel/HSplit/RightPanel/DetailPanel/Stats/BiomassRow/Value
@onready var avg_weight_label: Label = $Panel/HSplit/RightPanel/DetailPanel/Stats/AvgWeightRow/Value
@onready var zone_label: Label = $Panel/HSplit/RightPanel/DetailPanel/Stats/ZoneRow/Value
@onready var capacity_label: Label = $Panel/HSplit/RightPanel/DetailPanel/Stats/CapacityRow/Value

# Water quality
@onready var temp_label: Label = $Panel/HSplit/RightPanel/DetailPanel/WaterQuality/TempRow/Value
@onready var oxygen_label: Label = $Panel/HSplit/RightPanel/DetailPanel/WaterQuality/OxygenRow/Value
@onready var salinity_label: Label = $Panel/HSplit/RightPanel/DetailPanel/WaterQuality/SalinityRow/Value

# Health
@onready var lice_label: Label = $Panel/HSplit/RightPanel/DetailPanel/Health/LiceRow/Value
@onready var disease_label: Label = $Panel/HSplit/RightPanel/DetailPanel/Health/DiseaseRow/Value
@onready var mortality_label: Label = $Panel/HSplit/RightPanel/DetailPanel/Health/MortalityRow/Value

# Action buttons
@onready var feed_button: Button = $Panel/HSplit/RightPanel/DetailPanel/Actions/FeedButton
@onready var delouse_button: Button = $Panel/HSplit/RightPanel/DetailPanel/Actions/DelousingButton
@onready var harvest_button: Button = $Panel/HSplit/RightPanel/DetailPanel/Actions/HarvestButton

# ── State ─────────────────────────────────────────────────────────────────────
var _sim: Node = null
var _selected_pen_id: String = ""
var _pen_data: Dictionary = {}

# ── Lifecycle ─────────────────────────────────────────────────────────────────

func _ready() -> void:
	if has_node("/root/SimBridge"):
		_sim = get_node("/root/SimBridge")
		_sim.state_updated.connect(_on_state_updated)
		_sim.pen_action_completed.connect(_on_pen_action_completed)
	else:
		push_warning("PenManagement: SimBridge not found")
	
	# Connect action buttons
	if feed_button:
		feed_button.pressed.connect(_on_feed_pressed)
	if delouse_button:
		delouse_button.pressed.connect(_on_delouse_pressed)
	if harvest_button:
		harvest_button.pressed.connect(_on_harvest_pressed)
	
	_refresh()

func _input(event: InputEvent) -> void:
	if visible and event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()

# ── Signal Handlers ───────────────────────────────────────────────────────────

func _on_state_updated() -> void:
	_refresh()

func _on_pen_action_completed(action: String, pen_id: String, success: bool) -> void:
	if success:
		_show_feedback("success", "%s completed for pen %s" % [action.capitalize(), pen_id])
	else:
		_show_feedback("error", "%s failed for pen %s" % [action.capitalize(), pen_id])
	_refresh()

# ── Refresh ───────────────────────────────────────────────────────────────────

func _refresh() -> void:
	if not _sim:
		return
	
	_refresh_pen_list()
	if _selected_pen_id:
		_refresh_detail_panel()

func _refresh_pen_list() -> void:
	# Clear existing list
	for child in pen_list.get_children():
		child.queue_free()
	
	# Get all pens from SimBridge
	var pens = _sim.get_all_pens()
	
	if pens.is_empty():
		var label = Label.new()
		label.text = "No pens available. Place a pen to start!"
		pen_list.add_child(label)
		_hide_detail_panel()
		return
	
	# Create pen list items
	for pen in pens:
		var item = _create_pen_list_item(pen)
		pen_list.add_child(item)
	
	# Auto-select first pen if none selected
	if _selected_pen_id.is_empty() and not pens.is_empty():
		_select_pen(pens[0]["id"])

func _create_pen_list_item(pen: Dictionary) -> Control:
	var button = Button.new()
	var pen_id: String = pen.get("id", "unknown")
	var fish_count: int = pen.get("fish_count", 0)
	var biomass: float = pen.get("biomass_kg", 0.0)
	
	button.text = "%s\n%s fish | %.1f t" % [
		pen_id,
		_sim.format_fish(fish_count),
		biomass / 1000.0
	]
	button.custom_minimum_size = Vector2(150, 60)
	button.pressed.connect(func(): _select_pen(pen_id))
	
	# Highlight selected pen
	if pen_id == _selected_pen_id:
		button.add_theme_color_override("font_color", Color.CYAN)
	
	return button

func _refresh_detail_panel() -> void:
	if _selected_pen_id.is_empty():
		_hide_detail_panel()
		return
	
	# Get pen data
	var pens = _sim.get_all_pens()
	var pen: Dictionary = {}
	for p in pens:
		if p.get("id") == _selected_pen_id:
			pen = p
			break
	
	if pen.is_empty():
		_hide_detail_panel()
		return
	
	_pen_data = pen
	_show_detail_panel()
	
	# Basic stats
	if pen_name_label:
		pen_name_label.text = pen.get("name", pen.get("id", "Unknown Pen"))
	
	var fish_count: int = pen.get("fish_count", 0)
	var biomass: float = pen.get("biomass_kg", 0.0)
	var avg_weight: float = biomass / float(fish_count) if fish_count > 0 else 0.0
	var zone: String = pen.get("zone", "Unknown")
	var max_biomass: float = pen.get("max_biomass", 780000.0)
	
	_set_label(fish_count_label, _sim.format_fish(fish_count))
	_set_label(biomass_label, "%.1f t / %.1f t" % [biomass / 1000.0, max_biomass / 1000.0])
	_set_label(avg_weight_label, "%.2f kg" % avg_weight)
	_set_label(zone_label, zone)
	
	var capacity_pct = (biomass / max_biomass * 100.0) if max_biomass > 0 else 0.0
	_set_label(capacity_label, "%.1f%%" % capacity_pct)
	if capacity_label:
		if capacity_pct > 90:
			capacity_label.add_theme_color_override("font_color", Color.RED)
		elif capacity_pct > 75:
			capacity_label.add_theme_color_override("font_color", Color.ORANGE)
		else:
			capacity_label.add_theme_color_override("font_color", Color.GREEN)
	
	# Water quality (from SimBridge global state for now)
	_set_label(temp_label, "%.1f°C" % _sim.temperature_c)
	_set_label(oxygen_label, "8.5 mg/L") # Placeholder
	_set_label(salinity_label, "35 PSU") # Placeholder
	
	# Health
	_set_label(lice_label, "%.2f / fish" % _sim.sea_lice_per_fish)
	_set_label(disease_label, "None") # Placeholder
	_set_label(mortality_label, "0.03%") # Placeholder
	
	# Enable/disable action buttons based on state
	if feed_button:
		feed_button.disabled = (fish_count == 0)
	if delouse_button:
		delouse_button.disabled = (fish_count == 0 or _sim.sea_lice_per_fish < 0.5)
	if harvest_button:
		harvest_button.disabled = (fish_count == 0 or avg_weight < 3.5)

func _show_detail_panel() -> void:
	if detail_panel:
		detail_panel.visible = true
	if no_pen_label:
		no_pen_label.visible = false

func _hide_detail_panel() -> void:
	if detail_panel:
		detail_panel.visible = false
	if no_pen_label:
		no_pen_label.visible = true

# ── Actions ───────────────────────────────────────────────────────────────────

func _select_pen(pen_id: String) -> void:
	_selected_pen_id = pen_id
	pen_selected.emit(pen_id)
	_refresh()

func _on_feed_pressed() -> void:
	if _selected_pen_id.is_empty():
		return
	
	# Feed 100 kg by default
	var amount = 100.0
	_sim.feed_pen(_selected_pen_id, amount)
	action_requested.emit("feed", _selected_pen_id)

func _on_delouse_pressed() -> void:
	if _selected_pen_id.is_empty():
		return
	
	# Use mechanical delousing by default
	_sim.delouse_pen(_selected_pen_id, "mechanical")
	action_requested.emit("delouse", _selected_pen_id)

func _on_harvest_pressed() -> void:
	if _selected_pen_id.is_empty():
		return
	
	# Confirm harvest (for now, just do it)
	var biomass = _sim.harvest_pen(_selected_pen_id)
	if biomass > 0:
		_show_feedback("success", "Harvested %.1f t from pen %s" % [biomass / 1000.0, _selected_pen_id])
	action_requested.emit("harvest", _selected_pen_id)

# ── Helpers ───────────────────────────────────────────────────────────────────

func _set_label(label: Label, text: String) -> void:
	if label:
		label.text = text

func _show_feedback(level: String, message: String) -> void:
	print("PenManagement: [%s] %s" % [level.to_upper(), message])
	# TODO: Show toast notification
