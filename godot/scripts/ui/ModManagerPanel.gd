## ModManagerPanel v2.1 — Nordic Minimalist (scene-based).
## Layout lives in scenes/ui/ModManagerPanel.tscn; script is wiring only:
## @onready % refs, signal hookups, mod list logic, localization.
## Dynamic content (mod rows) is still built in code from engine data.
##
## Signals:
##   mods_changed()   — emitted whenever enable state or order changes
##   panel_close_requested

extends Control

# ── Signals ──────────────────────────────────────────────────────────────────

signal mods_changed()
signal panel_close_requested

# ── Colors ────────────────────────────────────────────────────────────────────

const COLOR_BG := Color("#F4F1EA")
const COLOR_HEADER := Color("#FFFFFF")
const COLOR_ROW_EVEN := Color("#F4F1EA")
const COLOR_ROW_ODD := Color("#FFFFFF")
const COLOR_ROW_HOVER := Color("#EDE9E0")
const COLOR_ROW_SEL := Color("#1F5E73")
const COLOR_ENABLED := Color("#2E6B45")
const COLOR_DISABLED := Color("#B3172F")
const COLOR_TEXT_BRIGHT := Color("#1C1E20")
const COLOR_TEXT_DIM := Color("#5A6168")
const COLOR_ACCENT := Color("#1F5E73")
const COLOR_DETAIL_BG := Color("#F4F1EA")

# ── Constants ─────────────────────────────────────────────────────────────────

const ROW_H := 44

# ── Runtime state ─────────────────────────────────────────────────────────────

## Array of mod data dicts:
## { id, name, author, version, description, enabled, event_count,
##   research_count, species_count, scenario_count, errors }
var _mods: Array = []

var _selected_index: int = -1
var _drag_index: int = -1
var _drag_offset_y: float = 0.0

# Scene-declared nodes
@onready var _title_label: Label = %TitleLabel
@onready var _status_label: Label = %StatusLabel
@onready var _close_button: Button = %CloseButton
@onready var _list_container: VBoxContainer = %ListContainer
@onready var _detail_name: Label = %DetailName
@onready var _detail_author: Label = %DetailAuthor
@onready var _detail_version: Label = %DetailVersion
@onready var _detail_desc: RichTextLabel = %DetailDesc
@onready var _detail_stats: Label = %DetailStats
@onready var _detail_errors: Label = %DetailErrors
@onready var _open_folder_button: Button = %OpenFolderButton
@onready var _reload_button: Button = %ReloadButton
@onready var _apply_button: Button = %ApplyButton

# ── Lifecycle ─────────────────────────────────────────────────────────────────

func _ready() -> void:
	_apply_localization()
	_connect_signals()
	_refresh_from_engine()

func _apply_localization() -> void:
	_title_label.text = _tr_or("UI_MODMANAGER_TITLE", "Modbehandling")

func _tr_or(key: String, fallback: String) -> String:
	var t := tr(key)
	return t if t != key else fallback

func _connect_signals() -> void:
	_close_button.pressed.connect(_on_close_pressed)
	_open_folder_button.pressed.connect(_on_open_folder_pressed)
	_reload_button.pressed.connect(_on_reload_pressed)
	_apply_button.pressed.connect(_on_apply_pressed)

# ── Public API ────────────────────────────────────────────────────────────────

## Reload mod list from the simulation engine / GameBridge.
func refresh() -> void:
	_refresh_from_engine()

# ── Keyboard ──────────────────────────────────────────────────────────────────

func _input(event: InputEvent) -> void:
	"""Handle keyboard navigation"""
	if not visible:
		return

	if event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()

# ── Data loading ──────────────────────────────────────────────────────────────

func _refresh_from_engine() -> void:
	_mods.clear()
	_selected_index = -1

	# Try to fetch mod data from the C# engine via GameBridge
	var bridge = _get_bridge()
	if bridge and bridge.has_method("GetModList"):
		var raw = bridge.GetModList()
		if raw is Array:
			_mods = raw

	# If no engine data, show placeholder mods for UI testing
	if _mods.is_empty():
		_mods = _get_placeholder_mods()

	_rebuild_list()
	_update_status_label()

## GameBridge is an autoload NODE (not a singleton), so resolve it by path.
func _get_bridge() -> Node:
	return get_node_or_null("/root/GameBridge")

func _get_placeholder_mods() -> Array:
	return [
		{
			"id": "no_mods_found",
			"name": "Ingen mods funnet",
			"author": "—",
			"version": "—",
			"description": "Legg mods i mappen mods/ i spillkatalogen.\nHver mod skal ha en manifest.json-fil.",
			"enabled": false,
			"event_count": 0,
			"research_count": 0,
			"species_count": 0,
			"scenario_count": 0,
			"errors": []
		}
	]

# ── List rendering ────────────────────────────────────────────────────────────

func _rebuild_list() -> void:
	for child in _list_container.get_children():
		child.queue_free()

	if _mods.is_empty():
		var empty_lbl := Label.new()
		empty_lbl.text = tr("UI_MODMANAGER_EMPTY")
		LakskongenTheme.set_text_color(empty_lbl, COLOR_TEXT_DIM)
		LakskongenTheme.set_size(empty_lbl, 13)
		empty_lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		_list_container.add_child(empty_lbl)
		return

	# Mod rows (column header is static in the scene)
	for i in range(_mods.size()):
		var row := _build_mod_row(i)
		_list_container.add_child(row)

func _build_mod_row(index: int) -> Control:
	var mod: Dictionary = _mods[index]
	var is_enabled: bool = mod.get("enabled", false)
	var is_selected: bool = (index == _selected_index)

	var row_color := COLOR_ROW_EVEN if (index % 2 == 0) else COLOR_ROW_ODD
	if is_selected:
		row_color = COLOR_ROW_SEL

	var row := PanelContainer.new()
	var row_sb := StyleBoxFlat.new()
	row_sb.bg_color = row_color
	row.add_theme_stylebox_override("panel", row_sb)
	row.custom_minimum_size.y = ROW_H
	row.set_meta("mod_index", index)

	var margin := MarginContainer.new()
	margin.add_theme_constant_override("margin_left", 8)
	margin.add_theme_constant_override("margin_right", 8)
	margin.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	row.add_child(margin)

	var hbox := HBoxContainer.new()
	hbox.add_theme_constant_override("separation", 8)
	margin.add_child(hbox)

	# Drag handle
	var drag_lbl := Label.new()
	drag_lbl.text = "⠿"
	LakskongenTheme.set_text_color(drag_lbl, COLOR_TEXT_DIM)
	LakskongenTheme.set_size(drag_lbl, 16)
	drag_lbl.custom_minimum_size.x = 20
	drag_lbl.mouse_default_cursor_shape = Control.CURSOR_VSIZE
	drag_lbl.set_meta("drag_row", index)
	hbox.add_child(drag_lbl)

	# Enable/disable toggle
	var toggle := CheckBox.new()
	toggle.button_pressed = is_enabled
	toggle.text = ""
	toggle.custom_minimum_size.x = 24
	toggle.toggled.connect(_on_mod_toggle.bind(index))
	hbox.add_child(toggle)

	# Mod name
	var name_lbl := Label.new()
	name_lbl.text = mod.get("name", "?")
	var has_errors := not (mod.get("errors", []) as Array).is_empty()
	var name_color := COLOR_TEXT_BRIGHT
	if not is_enabled:
		name_color = COLOR_TEXT_DIM
	elif has_errors:
		name_color = Color(1.0, 0.65, 0.35)
	LakskongenTheme.set_text_color(name_lbl, name_color)
	LakskongenTheme.set_size(name_lbl, 14)
	name_lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	hbox.add_child(name_lbl)

	# Version
	var ver_lbl := Label.new()
	ver_lbl.text = mod.get("version", "?")
	LakskongenTheme.set_text_color(ver_lbl, COLOR_TEXT_DIM)
	LakskongenTheme.set_size(ver_lbl, 12)
	ver_lbl.custom_minimum_size.x = 60
	hbox.add_child(ver_lbl)

	# Author
	var auth_lbl := Label.new()
	auth_lbl.text = mod.get("author", "?")
	LakskongenTheme.set_text_color(auth_lbl, COLOR_TEXT_DIM)
	LakskongenTheme.set_size(auth_lbl, 12)
	auth_lbl.custom_minimum_size.x = 90
	hbox.add_child(auth_lbl)

	# Status badge
	var status_lbl := Label.new()
	if has_errors:
		status_lbl.text = "⚠ " + tr("UI_MODS_ERROR")
		LakskongenTheme.set_text_color(status_lbl, Color(1.0, 0.55, 0.25))
	elif is_enabled:
		status_lbl.text = "● " + tr("UI_MODS_ACTIVE")
		LakskongenTheme.set_text_color(status_lbl, COLOR_ENABLED)
	else:
		status_lbl.text = "○ " + tr("UI_MODS_DISABLED")
		LakskongenTheme.set_text_color(status_lbl, COLOR_DISABLED)
	LakskongenTheme.set_size(status_lbl, 12)
	status_lbl.custom_minimum_size.x = 90
	hbox.add_child(status_lbl)

	# The row selects on clicks that reach it; the toggle keeps its own clicks
	# (a full-row Button overlay used to sit on top and swallow them).
	row.mouse_filter = Control.MOUSE_FILTER_STOP
	row.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	row.gui_input.connect(_on_row_gui_input.bind(index))

	return row

# ── Detail pane ───────────────────────────────────────────────────────────────

func _show_detail(index: int) -> void:
	if index < 0 or index >= _mods.size():
		_detail_name.text = tr("UI_MODS_NONE_SELECTED")
		_detail_author.text = ""
		_detail_version.text = ""
		_detail_desc.text = ""
		_detail_stats.text = ""
		_detail_errors.text = ""
		return

	var mod: Dictionary = _mods[index]
	_detail_name.text = mod.get("name", "Ukjent mod")
	_detail_author.text = tr("UI_MODMANAGER_AUTHOR_FORMAT") % mod.get("author", tr("UI_MODMANAGER_AUTHOR_UNKNOWN"))
	_detail_version.text = "Versjon: " + mod.get("version", "?")
	_detail_desc.text = "[color=#5A6168]" + mod.get("description", "") + "[/color]"

	var stats_parts: Array = []
	var ec: int = mod.get("event_count", 0)
	var rc: int = mod.get("research_count", 0)
	var sc: int = mod.get("species_count", 0)
	var nc: int = mod.get("scenario_count", 0)
	if ec > 0: stats_parts.append("%d hendelse%s" % [ec, "r" if ec != 1 else ""])
	if rc > 0: stats_parts.append("%d forskningsnoder" % rc)
	if sc > 0: stats_parts.append("%d fiskearter" % sc)
	if nc > 0: stats_parts.append("%d scenario%s" % [nc, "er" if nc != 1 else ""])
	_detail_stats.text = "Innhold: " + (", ".join(stats_parts) if not stats_parts.is_empty() else "ingen data")

	var errors: Array = mod.get("errors", [])
	if errors.is_empty():
		_detail_errors.text = ""
	else:
		_detail_errors.text = "⚠ Feil:\n" + "\n".join(errors)

# ── Status label ──────────────────────────────────────────────────────────────

func _update_status_label() -> void:
	var total := _mods.size()
	var enabled := _mods.filter(func(m): return m.get("enabled", false)).size()
	_status_label.text = "%d av %d aktive" % [enabled, total]

# ── Event handlers ────────────────────────────────────────────────────────────

func _on_row_gui_input(event: InputEvent, index: int) -> void:
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		_on_row_clicked(index)

func _on_row_clicked(index: int) -> void:
	_selected_index = index
	_rebuild_list()
	_show_detail(index)

func _on_mod_toggle(is_on: bool, index: int) -> void:
	if index < 0 or index >= _mods.size(): return
	_mods[index]["enabled"] = is_on
	_rebuild_list()
	_update_status_label()

func _on_close_pressed() -> void:
	emit_signal("panel_close_requested")

func _on_open_folder_pressed() -> void:
	# Prefer the directory the engine actually scans (user://mods), else exe/mods.
	var bridge = _get_bridge()
	var mods_path: String = ""
	if bridge != null:
		var reported = bridge.get("ModsDirectory")
		if reported is String:
			mods_path = reported
	if mods_path.is_empty():
		mods_path = OS.get_executable_path().get_base_dir().path_join("mods")
	if DirAccess.dir_exists_absolute(mods_path):
		OS.shell_open(mods_path)
	else:
		OS.shell_open(OS.get_executable_path().get_base_dir())

func _on_reload_pressed() -> void:
	var bridge = _get_bridge()
	if bridge and bridge.has_method("ReloadMods"):
		bridge.ReloadMods()
	_refresh_from_engine()

func _on_apply_pressed() -> void:
	# Push enabled/order state back to the engine
	var bridge = _get_bridge()
	if bridge and bridge.has_method("ApplyModSettings"):
		var settings := []
		for mod in _mods:
			settings.append({
				"id": mod.get("id", ""),
				"enabled": mod.get("enabled", false)
			})
		bridge.ApplyModSettings(settings)
	emit_signal("mods_changed")

# ── Drag-to-reorder ───────────────────────────────────────────────────────────
# Lightweight drag: press on drag handle, move mouse, release to drop.

func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton:
		if event.button_index == MOUSE_BUTTON_LEFT and not event.pressed:
			_drag_index = -1

	if event is InputEventMouseMotion and _drag_index >= 0:
		_handle_drag_move(event.global_position.y)

func _handle_drag_move(global_y: float) -> void:
	if _list_container == null: return
	var local_y := _list_container.get_local_mouse_position().y
	var target := int(local_y / ROW_H)
	target = clamp(target, 0, _mods.size() - 1)
	if target != _drag_index:
		var item = _mods[_drag_index]
		_mods.remove_at(_drag_index)
		_mods.insert(target, item)
		if _selected_index == _drag_index:
			_selected_index = target
		_drag_index = target
		_rebuild_list()
