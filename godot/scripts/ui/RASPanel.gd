## RASPanel v2.1 — Nordic Minimalist (scene-based).
## RAS facility management UI (Recirculating Aquaculture System)
## Layout lives in scenes/ui/RASPanel.tscn; this script is wiring only:
## @onready % refs, signal hookups, localization, theme fonts, data refresh.
## Facility rows are data-driven and still populated in code.

extends PanelContainer
class_name RASPanel

# ── Scene refs ────────────────────────────────────────────────────────────────
@onready var ras_header_label: Label = %RasTitle
@onready var _facility_section_title: Label = %FacilityHeader
@onready var facility_list_container: VBoxContainer = %FacilityList
@onready var water_chemistry_label: Label = %WaterChemistryLabel
@onready var build_new_button: Button = %BtnBuildRas
@onready var maintenance_button: Button = %BtnMaintenance

# ── Autoloads ─────────────────────────────────────────────────────────────────
var _theme: Node = null
var _sim: Node = null

func _ready():
	_theme = get_node_or_null("/root/LakskongenTheme")
	_sim = get_node_or_null("/root/SimBridge")
	if _sim != null:
		if _sim.has_signal("state_updated"):
			_sim.state_updated.connect(_on_state_updated)
	else:
		push_warning("RASPanel: SimBridge not found — showing fallback data")

	_apply_localization()
	_apply_theme_fonts()
	build_new_button.pressed.connect(_on_build_new_pressed)
	maintenance_button.pressed.connect(_on_maintenance_pressed)

	refresh()

# ── Scene wiring ──────────────────────────────────────────────────────────────

func _apply_localization() -> void:
	ras_header_label.text = tr("UI_RAS_TITLE")
	_facility_section_title.text = tr("UI_RAS_FACILITIES")
	build_new_button.text = tr("UI_RAS_BUILD")
	maintenance_button.text = tr("UI_RAS_MAINTENANCE")

func _apply_theme_fonts() -> void:
	if not _theme:
		return
	if _theme.font_heading_bold:
		ras_header_label.add_theme_font_override("font", _theme.font_heading_bold)
	if _theme.font_heading_semibold:
		_facility_section_title.add_theme_font_override("font", _theme.font_heading_semibold)

func refresh():
	# Refresh UI with live data from SimBridge (fallback to placeholders offline)
	_update_facility_list()
	_update_water_chemistry()

func on_panel_shown() -> void:
	refresh()

func _on_state_updated() -> void:
	if visible:
		refresh()

func _get_facilities() -> Array:
	if _sim != null and _sim.has_method("get_ras_facilities"):
		return _sim.get_ras_facilities()
	return [
		{"id": "ras_001", "name": "RAS Anlegg 1", "operational": true, "water_temp": 12.0, "oxygen": 8.0, "ph": 7.2, "needs_maintenance": false},
		{"id": "ras_002", "name": "RAS Anlegg 2", "operational": true, "water_temp": 12.0, "oxygen": 8.0, "ph": 7.2, "needs_maintenance": false},
	]

func _update_facility_list():
	if facility_list_container == null:
		return
	# Clear existing list items
	for child in facility_list_container.get_children():
		child.queue_free()

	var facilities: Array = _get_facilities()
	if facilities.is_empty():
		var empty_label = Label.new()
		empty_label.text = tr("UI_RAS_EMPTY")
		if _theme:
			empty_label.add_theme_font_size_override("font_size", _theme.FONT_SMALL)
			empty_label.add_theme_color_override("font_color", _theme.TEXT_SECONDARY)
		facility_list_container.add_child(empty_label)
		return

	for f in facilities:
		var facility_label = Label.new()
		var status := "Aktiv" if f.get("operational", true) else "Inaktiv"
		var maint := " (trenger vedlikehold)" if f.get("needs_maintenance", false) else ""
		facility_label.text = "%s (%s)%s" % [f.get("name", "RAS-anlegg"), status, maint]
		if _theme:
			facility_label.add_theme_font_size_override("font_size", _theme.FONT_SMALL)
			facility_label.add_theme_color_override("font_color", _theme.TEXT_SECONDARY)
		facility_list_container.add_child(facility_label)

func _update_water_chemistry():
	if water_chemistry_label == null:
		return
	var facilities: Array = _get_facilities()
	if facilities.is_empty():
		water_chemistry_label.text = "Vannkjemi: Ingen anlegg"
		return
	var f: Dictionary = facilities[0]
	# C# backend keys (current_water_temp/oxygen_level/water_ph) with
	# fallback to the legacy offline-placeholder keys.
	var ph := float(f.get("water_ph", f.get("ph", 7.2)))
	var temp := float(f.get("current_water_temp", f.get("water_temp", 12.0)))
	var o2 := float(f.get("oxygen_level", f.get("oxygen", 8.0)))
	water_chemistry_label.text = "Vannkjemi: pH %.1f, Temp %.1f°C, O2 %.1f mg/L" % [ph, temp, o2]

func _on_build_new_pressed():
	if _sim != null and _sim.has_method("build_ras_facility"):
		_sim.build_ras_facility("Nytt RAS-anlegg", "medium")
	refresh()

func _on_maintenance_pressed():
	if _sim != null and _sim.has_method("ras_maintenance"):
		for f in _get_facilities():
			if f.get("needs_maintenance", false):
				_sim.ras_maintenance(f.get("id", ""))
				break
	refresh()

func _input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		get_viewport().set_input_as_handled()
		_on_close_requested()

func _on_close_requested() -> void:
	hide()
