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
## F2-31: opens the family-first build flow on Land (the legacy build button is gone).
@onready var plan_land_button: Button = %BtnPlanLand
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
	plan_land_button.pressed.connect(_on_plan_land_pressed)
	maintenance_button.pressed.connect(_on_maintenance_pressed)

	refresh()

# ── Scene wiring ──────────────────────────────────────────────────────────────

func _apply_localization() -> void:
	ras_header_label.text = tr("UI_RAS_TITLE")
	_facility_section_title.text = tr("UI_RAS_FACILITIES")
	plan_land_button.text = tr("BUILD_PLAN_LAND")
	plan_land_button.tooltip_text = tr("BUILD_PLAN_LAND_TIP")
	maintenance_button.text = tr("UI_RAS_MAINTENANCE")

func _apply_theme_fonts() -> void:
	if not _theme:
		return
	LakskongenTheme.set_strong(ras_header_label)
	LakskongenTheme.set_strong(_facility_section_title)

func refresh():
	# Refresh UI with live data from SimBridge (fallback to placeholders offline)
	_update_facility_list()
	_update_water_chemistry()
	_update_maintenance_button()

## U1-08: maintenance only does something when a facility needs it.
func _update_maintenance_button() -> void:
	if maintenance_button == null:
		return
	var facilities := _get_facilities()
	var reason := ""
	if facilities.is_empty():
		reason = tr("UI_WHY_NO_RAS")
	elif not facilities.any(func(f: Dictionary) -> bool: return bool(f.get("needs_maintenance", false))):
		reason = tr("UI_WHY_RAS_NO_MAINTENANCE")
	UIPolish.set_available(maintenance_button, reason)

func on_panel_shown() -> void:
	refresh()

func _on_state_updated() -> void:
	if visible:
		refresh()

func _get_facilities() -> Array:
	if _sim != null and _sim.has_method("get_ras_facilities"):
		return _sim.get_ras_facilities()
	return [
		{"id": "ras_001", "name": "RAS-anlegg 1", "operational": true, "water_temp": 12.0, "oxygen": 8.0, "ph": 7.2, "needs_maintenance": false},
		{"id": "ras_002", "name": "RAS-anlegg 2", "operational": true, "water_temp": 12.0, "oxygen": 8.0, "ph": 7.2, "needs_maintenance": false},
	]

func _update_facility_list():
	if facility_list_container == null:
		return
	# Clear existing list items
	# Detach before freeing: a queued node keeps its name until the frame
	# ends, so the rebuilt rows would be renamed.
	for child in facility_list_container.get_children():
		facility_list_container.remove_child(child)
		child.queue_free()

	var facilities: Array = _get_facilities()
	if facilities.is_empty():
		var empty_label = Label.new()
		empty_label.text = tr("UI_RAS_EMPTY")
		if _theme:
			LakskongenTheme.set_size(empty_label, _theme.FONT_SMALL)
			LakskongenTheme.set_text_color(empty_label, _theme.TEXT_SECONDARY)
		facility_list_container.add_child(empty_label)
		return

	for f in facilities:
		var row := HBoxContainer.new()
		row.name = "Facility_%s" % str(f.get("id", ""))
		var facility_label = Label.new()
		var operational := bool(f.get("operational", false))
		var status := "Aktiv" if operational else "Under bygging %d%%" % int(round(float(f.get("construction_progress", 0.0)) * 100.0))
		var maint := " (trenger vedlikehold)" if f.get("needs_maintenance", false) else ""
		var fish := int(f.get("fish_count", 0))
		var stock := " — %s fisk, %.2f kg" % [_fmt_count(fish), float(f.get("mean_weight_kg", 0.0))] if fish > 0 else ""
		facility_label.text = "%s (%s)%s%s" % [f.get("name", "RAS-anlegg"), status, maint, stock]
		facility_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		if _theme:
			LakskongenTheme.set_size(facility_label, _theme.FONT_SMALL)
			LakskongenTheme.set_text_color(facility_label, _theme.TEXT_SECONDARY)
		row.add_child(facility_label)
		# F383: a facility now holds fish — stock smolts, harvest when grown.
		var stock_btn := Button.new()
		stock_btn.name = "StockButton"
		stock_btn.text = tr("UI_RAS_STOCK") % _fmt_count(STOCK_BATCH)
		UIPolish.set_available(stock_btn, "" if operational else tr("UI_WHY_UNDER_CONSTRUCTION"))
		stock_btn.pressed.connect(_on_stock_pressed.bind(str(f.get("id", ""))))
		row.add_child(stock_btn)
		var harvest_btn := Button.new()
		harvest_btn.name = "HarvestButton"
		harvest_btn.text = tr("UI_PEN_HARVEST")
		UIPolish.set_available(harvest_btn, "" if fish > 0 else tr("UI_WHY_NOTHING_TO_HARVEST"))
		harvest_btn.pressed.connect(_on_harvest_pressed.bind(str(f.get("id", ""))))
		row.add_child(harvest_btn)
		facility_list_container.add_child(row)

func _update_water_chemistry():
	if water_chemistry_label == null:
		return
	var facilities: Array = _get_facilities()
	if facilities.is_empty():
		water_chemistry_label.text = tr("UI_RAS_WATER_NONE")
		return
	var f: Dictionary = facilities[0]
	# C# backend keys (current_water_temp/oxygen_level/water_ph) with
	# fallback to the legacy offline-placeholder keys.
	var ph := float(f.get("water_ph", f.get("ph", 7.2)))
	var temp := float(f.get("current_water_temp", f.get("water_temp", 12.0)))
	var o2 := float(f.get("oxygen_level", f.get("oxygen", 8.0)))
	water_chemistry_label.text = tr("UI_RAS_WATER") % [SimFormatters.num(ph, 1), SimFormatters.num(temp, 1), SimFormatters.num(o2, 1)]

## Smolts per "Sett ut" press (well under a small facility's tank capacity).
const STOCK_BATCH := 100_000

func _on_stock_pressed(facility_id: String) -> void:
	if _sim != null and _sim.has_method("stock_ras_facility"):
		_report(_sim.stock_ras_facility(facility_id, STOCK_BATCH))
	refresh()

func _on_harvest_pressed(facility_id: String) -> void:
	if _sim != null and _sim.has_method("harvest_ras_facility"):
		_report(_sim.harvest_ras_facility(facility_id))
	refresh()

func _report(result: Dictionary) -> void:
	var mgr := get_node_or_null("/root/ToastNotification")
	if mgr and mgr.has_method("show_toast"):
		var ok := bool(result.get("success", false))
		mgr.show_toast(tr("UI_OFFSHORE_DONE") if ok else SimLabels.command_error(result), "SUCCESS" if ok else "WARNING", 4.0)

func _fmt_count(n: int) -> String:
	return SimFormatters.integer(n)

## F2-31: land farms are applied for and built through the build dialog's Land family.
func _on_plan_land_pressed():
	BuildFlowLink.open(self, "land")

func _on_maintenance_pressed():
	if _sim != null and _sim.has_method("ras_maintenance"):
		for f in _get_facilities():
			if f.get("needs_maintenance", false):
				_sim.ras_maintenance(f.get("id", ""))
				break
	refresh()

func _input(event: InputEvent) -> void:
	# Only while shown: as a cached, hidden tab this used to swallow every ESC.
	if is_visible_in_tree() and event.is_action_pressed("ui_cancel"):
		get_viewport().set_input_as_handled()
		_on_close_requested()

func _on_close_requested() -> void:
	hide()
