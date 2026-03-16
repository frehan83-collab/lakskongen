extends Control

## SaveLoadMenu — Save/Load game interface.
## Lists save slots, handles save/load/delete operations.

signal save_completed(slot_name: String)
signal load_completed(slot_name: String)
signal menu_closed()

enum MenuMode { SAVE, LOAD }

# ── UI References ─────────────────────────────────────────────────────────────
@onready var title_label: Label = $Panel/TitleLabel
@onready var slots_container: VBoxContainer = $Panel/ScrollContainer/SlotsContainer
@onready var close_button: Button = $Panel/CloseButton
@onready var new_save_button: Button = $Panel/NewSaveButton

# ── State ─────────────────────────────────────────────────────────────────────
var _sim: Node = null
var _save_manager: Node = null
var _current_mode: MenuMode = MenuMode.LOAD

# ── Lifecycle ─────────────────────────────────────────────────────────────────

func _ready() -> void:
	if has_node("/root/SimBridge"):
		_sim = get_node("/root/SimBridge")
	
	if has_node("/root/SaveLoadManager"):
		_save_manager = get_node("/root/SaveLoadManager")
		_save_manager.game_saved.connect(_on_game_saved)
		_save_manager.game_loaded.connect(_on_game_loaded)
		_save_manager.save_failed.connect(_on_save_failed)
		_save_manager.load_failed.connect(_on_load_failed)
	else:
		push_warning("SaveLoadMenu: SaveLoadManager not found")
	
	if close_button:
		close_button.pressed.connect(_on_close_pressed)
	
	if new_save_button:
		new_save_button.pressed.connect(_on_new_save_pressed)
	
	visible = false

func _input(event: InputEvent) -> void:
	if visible and event.is_action_pressed("ui_cancel"):
		_on_close_pressed()
		get_viewport().set_input_as_handled()

# ── Public API ────────────────────────────────────────────────────────────────

## Open menu in SAVE or LOAD mode
func open(mode: MenuMode = MenuMode.LOAD) -> void:
	_current_mode = mode
	
	if title_label:
		title_label.text = "Save Game" if mode == MenuMode.SAVE else "Load Game"
	
	if new_save_button:
		new_save_button.visible = (mode == MenuMode.SAVE)
	
	_refresh_slots()
	visible = true

## Refresh the list of save slots
func _refresh_slots() -> void:
	# Clear existing slots
	for child in slots_container.get_children():
		child.queue_free()
	
	if not _save_manager:
		var label = Label.new()
		label.text = "SaveLoadManager not available"
		slots_container.add_child(label)
		return
	
	# Get save files
	var saves = _save_manager.GetSaveFiles()
	
	if saves.is_empty():
		var label = Label.new()
		label.text = "No save files found"
		slots_container.add_child(label)
		return
	
	# Create slot UI for each save
	for save in saves:
		var slot_ui = _create_slot_ui(save)
		slots_container.add_child(slot_ui)

func _create_slot_ui(save_info: SaveFileInfo) -> Control:
	var container = HBoxContainer.new()
	container.custom_minimum_size = Vector2(0, 60)
	
	# Info section
	var info_box = VBoxContainer.new()
	info_box.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	
	var name_label = Label.new()
	name_label.text = save_info.SaveName
	name_label.add_theme_font_size_override("font_size", 16)
	info_box.add_child(name_label)
	
	var details_label = Label.new()
	details_label.text = "Day %d | %s NOK | %s" % [
		save_info.GameDay,
		_format_nok(save_info.Cash),
		_format_datetime(save_info.SaveTime)
	]
	details_label.add_theme_color_override("font_color", Color.GRAY)
	info_box.add_child(details_label)
	
	container.add_child(info_box)
	
	# Action buttons
	if _current_mode == MenuMode.SAVE:
		var overwrite_btn = Button.new()
		overwrite_btn.text = "Overwrite"
		overwrite_btn.custom_minimum_size = Vector2(100, 0)
		overwrite_btn.pressed.connect(func(): _on_save_slot_pressed(save_info.FileName))
		container.add_child(overwrite_btn)
	else: # LOAD mode
		var load_btn = Button.new()
		load_btn.text = "Load"
		load_btn.custom_minimum_size = Vector2(100, 0)
		load_btn.pressed.connect(func(): _on_load_slot_pressed(save_info.FileName))
		container.add_child(load_btn)
	
	var delete_btn = Button.new()
	delete_btn.text = "Delete"
	delete_btn.custom_minimum_size = Vector2(80, 0)
	delete_btn.pressed.connect(func(): _on_delete_slot_pressed(save_info.FileName))
	container.add_child(delete_btn)
	
	return container

# ── Actions ───────────────────────────────────────────────────────────────────

func _on_new_save_pressed() -> void:
	# Create new save with timestamp
	var timestamp = Time.get_datetime_string_from_system().replace(":", "-")
	var save_name = "save_%s" % timestamp
	_save_game(save_name)

func _on_save_slot_pressed(file_name: String) -> void:
	# Remove .json extension if present
	var save_name = file_name.replace(".json", "")
	_save_game(save_name)

func _on_load_slot_pressed(file_name: String) -> void:
	_load_game(file_name)

func _on_delete_slot_pressed(file_name: String) -> void:
	# TODO: Show confirmation dialog
	if _save_manager:
		var success = _save_manager.DeleteSave(file_name)
		if success:
			print("SaveLoadMenu: Deleted save file: %s" % file_name)
			_refresh_slots()
		else:
			push_error("SaveLoadMenu: Failed to delete save file: %s" % file_name)

func _save_game(save_name: String) -> void:
	if not _save_manager:
		push_error("SaveLoadMenu: SaveLoadManager not available")
		return
	
	var success = _save_manager.SaveGame(save_name)
	if success:
		print("SaveLoadMenu: Game saved as '%s'" % save_name)
		save_completed.emit(save_name)
	else:
		push_error("SaveLoadMenu: Save failed for '%s'" % save_name)

func _load_game(file_name: String) -> void:
	if not _save_manager:
		push_error("SaveLoadMenu: SaveLoadManager not available")
		return
	
	var success = _save_manager.LoadGame(file_name)
	if success:
		print("SaveLoadMenu: Game loaded from '%s'" % file_name)
		load_completed.emit(file_name)
		hide()
	else:
		push_error("SaveLoadMenu: Load failed for '%s'" % file_name)

func _on_close_pressed() -> void:
	menu_closed.emit()
	hide()

# ── Signal Handlers ───────────────────────────────────────────────────────────

func _on_game_saved(save_name: String, file_path: String) -> void:
	print("SaveLoadMenu: Game saved successfully: %s" % save_name)
	_refresh_slots()

func _on_game_loaded(save_name: String) -> void:
	print("SaveLoadMenu: Game loaded successfully: %s" % save_name)
	hide()

func _on_save_failed(error: String) -> void:
	push_error("SaveLoadMenu: Save failed: %s" % error)
	# TODO: Show error dialog

func _on_load_failed(error: String) -> void:
	push_error("SaveLoadMenu: Load failed: %s" % error)
	# TODO: Show error dialog

# ── Helpers ───────────────────────────────────────────────────────────────────

func _format_nok(amount: int) -> String:
	if amount >= 1_000_000:
		return "%.2f M" % (amount / 1_000_000.0)
	elif amount >= 1_000:
		return "%.1f k" % (amount / 1_000.0)
	return str(amount)

func _format_datetime(dt: Dictionary) -> String:
	# DateTime from C# comes as Dictionary
	# For now, just return a simple string
	return "%04d-%02d-%02d %02d:%02d" % [
		dt.get("year", 2024),
		dt.get("month", 1),
		dt.get("day", 1),
		dt.get("hour", 0),
		dt.get("minute", 0)
	]

# Type alias for C# SaveFileInfo (duck typing in GDScript)
class SaveFileInfo:
	var FileName: String
	var SaveName: String
	var SaveTime: Dictionary
	var GameDay: int
	var Cash: int
	var FileSize: int
