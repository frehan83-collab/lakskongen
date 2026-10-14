extends Control

## SaveLoadMenu v2.2 — Nordic Minimalist (scene-based).
## Layout lives in scenes/ui/SaveLoadMenu.tscn; script is wiring only:
## @onready % refs, signal hookups, slot refresh, localization.
## Save slots are built in code from SimBridge.get_save_list() — every save/load
## goes through the SimBridge facade (C# SaveLoadManager, PascalCase API) and
## the UI only reports success when the C# call returned true (F928).
##
## Two contexts:
##   modal — opened by MainMenu / PauseMenu via show_load() / show_save();
##           closes itself (close_requested) after an action or ESC.
##   panel — on_panel_shown() refreshes and offers save + load together (the
##           old "Lagre/last inn" tab; save and load now live in the pause
##           menu, roadmap D15).

signal close_requested
signal game_loaded(file_name: String)
signal game_saved(slot_name: String)

var _mode: String = "load"  # "save" or "load" (title + modal semantics)
var _modal: bool = false
var _theme: Node = null
## Two-step delete: file name armed by the first "Slett" press.
var _delete_armed: String = ""

@onready var _title_label: Label = %TitleLabel
@onready var _close_button: Button = %CloseButton
@onready var _slots_container: VBoxContainer = %SlotsContainer
@onready var _new_save_button: Button = %NewSaveButton

func _ready() -> void:
	_theme = get_node_or_null("/root/LakskongenTheme")
	_apply_localization()
	_close_button.pressed.connect(_close)
	_new_save_button.pressed.connect(_on_new_save)
	hide()

func _apply_localization() -> void:
	_update_title_text()
	_new_save_button.text = _tr_or("UI_SAVELOAD_NEW_SAVE", "💾 Nytt Lagringspunkt")

func _tr_or(key: String, fallback: String) -> String:
	var t := tr(key)
	return t if t != key else fallback

func _sim() -> Node:
	return get_node_or_null("/root/SimBridge")

func _can_save() -> bool:
	var sim := _sim()
	return sim != null and sim.is_game_running()

# ── Entry points ───────────────────────────────────────────────────────────────

func show_load() -> void:
	_open_modal("load")

func show_save() -> void:
	_open_modal("save")

## GameUI tab hook: refresh every time the tab becomes visible.
func on_panel_shown() -> void:
	_modal = false
	_mode = "save" if _can_save() else "load"
	_refresh()

func _open_modal(mode: String) -> void:
	_modal = true
	_mode = mode
	_refresh()
	show()
	_close_button.grab_focus.call_deferred()

func _close() -> void:
	_delete_armed = ""
	hide()
	close_requested.emit()

func _unhandled_input(event: InputEvent) -> void:
	if _modal and visible and event.is_action_pressed("ui_cancel"):
		_close()
		get_viewport().set_input_as_handled()

# ── Rendering ──────────────────────────────────────────────────────────────────

func _refresh() -> void:
	_update_title_text()
	_close_button.visible = _modal
	_new_save_button.visible = _can_save() and not (_modal and _mode == "load")
	_refresh_slots()

func _update_title_text() -> void:
	if not _modal:
		_title_label.text = _tr_or("UI_SAVELOAD_TITLE_BOTH", "Lagre / Laste")
	elif _mode == "save":
		_title_label.text = _tr_or("UI_SAVELOAD_TITLE_SAVE", "Lagre Spill")
	else:
		_title_label.text = _tr_or("UI_SAVELOAD_TITLE_LOAD", "Last Spill")

func _refresh_slots() -> void:
	if not _slots_container: return
	for child in _slots_container.get_children():
		_slots_container.remove_child(child)
		child.queue_free()

	var sim := _sim()
	if sim == null:
		var lbl := Label.new()
		lbl.text = _tr_or("UI_SAVELOAD_MANAGER_UNAVAILABLE", "SaveLoadManager ikke tilgjengelig")
		LakskongenTheme.set_tone(lbl, &"Muted")
		_slots_container.add_child(lbl)
		return

	var saves: Array = sim.get_save_list()
	if saves.is_empty():
		UIPolish.show_empty_state_localized(_slots_container, _tr_or("UI_SAVELOAD_EMPTY_LOAD", "💾 Ingen lagrede spill funnet"), 14, "💾 ", _theme)
		return

	for save_info in saves:
		_slots_container.add_child(_create_slot(save_info))

## Saves the game writes itself are named by file ("autosave_latest",
## "autosave_3", "quicksave"); the player reads them as Norwegian labels.
func slot_display_name(slot_name: String) -> String:
	return _slot_display_name(slot_name)

func _slot_display_name(slot_name: String) -> String:
	if slot_name == "autosave_latest":
		return tr("UI_SAVELOAD_AUTOSAVE_LATEST")
	if slot_name.begins_with("autosave"):
		var suffix := slot_name.trim_prefix("autosave").lstrip("_")
		return tr("UI_SAVELOAD_AUTOSAVE_N") % suffix if suffix.is_valid_int() else tr("UI_SAVELOAD_AUTOSAVE")
	if slot_name.begins_with("quicksave"):
		return tr("UI_SAVELOAD_QUICKSAVE")
	return slot_name

func _create_slot(save_info: Dictionary) -> PanelContainer:
	var file_name := String(save_info.get("file", ""))
	var slot_name := String(save_info.get("name", file_name.get_basename()))

	var slot := PanelContainer.new()
	slot.name = "Slot_" + file_name.get_basename().validate_node_name()
	# A theme tile, so the slot sits right on the surface it is shown on: Ink
	# in the main and pause menus, Chart paper in a document panel. (A fixed
	# Chart colour put light Ink text on a light card: 1.03:1.)
	slot.theme_type_variation = &"Tile"

	var hbox := HBoxContainer.new()
	hbox.add_theme_constant_override("separation", 12)
	slot.add_child(hbox)

	var info_vbox := VBoxContainer.new()
	info_vbox.size_flags_horizontal = SIZE_EXPAND_FILL
	hbox.add_child(info_vbox)

	var name_lbl := Label.new()
	name_lbl.text = _slot_display_name(slot_name)
	if name_lbl.text == slot_name:
		name_lbl.set_meta(&"player_text", true)  # a name the player chose (text lint: data)
	LakskongenTheme.set_size(name_lbl, 16)
	LakskongenTheme.set_tone(name_lbl, &"")
	info_vbox.add_child(name_lbl)

	var details := Label.new()
	var day_fmt := _tr_or("UI_SAVELOAD_DAY_FORMAT", "Dag %s")
	details.text = day_fmt % str(save_info.get("day", "?"))
	var sim := _sim()
	if sim != null and save_info.has("cash"):
		details.text += "  ·  " + sim.format_nok(float(save_info["cash"]))
	LakskongenTheme.set_size(details, 12)
	LakskongenTheme.set_tone(details, &"Muted")
	info_vbox.add_child(details)

	var load_btn := Button.new()
	load_btn.name = "LoadButton"
	load_btn.text = _tr_or("UI_SAVELOAD_LOAD", "Last inn")
	load_btn.pressed.connect(_on_load.bind(file_name, slot_name))
	hbox.add_child(load_btn)

	if _can_save() and not (_modal and _mode == "load"):
		var save_btn := Button.new()
		save_btn.name = "OverwriteButton"
		save_btn.text = _tr_or("UI_SAVELOAD_OVERWRITE", "Overskriv")
		# Overwrite under the file's own base name so the same file is replaced.
		save_btn.pressed.connect(_on_save.bind(file_name.get_basename()))
		hbox.add_child(save_btn)

	var delete_btn := Button.new()
	delete_btn.name = "DeleteButton"
	delete_btn.text = _tr_or("UI_SAVELOAD_DELETE", "Slett")
	delete_btn.pressed.connect(_on_delete.bind(file_name, slot_name, delete_btn))
	hbox.add_child(delete_btn)

	return slot

# ── Actions ────────────────────────────────────────────────────────────────────

func _on_load(file_name: String, slot_name: String) -> void:
	var sim := _sim()
	if sim == null: return
	if sim.load_game(file_name):
		_toast(_tr_or("UI_TOAST_LOADED", "💾 Lagring lastet: %s") % slot_name, "SUCCESS")
		if _modal:
			_close()
		else:
			_refresh()
		# Last, so a listener that leaves the menu (MainMenu -> GameScene)
		# sees the final state.
		game_loaded.emit(file_name)
	else:
		_toast(_tr_or("UI_TOAST_LOAD_FAILED", "⚠️ Lasting feilet: %s") % sim.get_last_save_error(), "ERROR")

func _on_save(slot_name: String) -> void:
	var sim := _sim()
	if sim == null: return
	if sim.save_game(slot_name):
		game_saved.emit(slot_name)
		_toast(_tr_or("UI_TOAST_SAVED", "💾 Spill lagret: %s") % slot_name, "SUCCESS")
	else:
		_toast(_tr_or("UI_TOAST_SAVE_FAILED", "⚠️ Lagring feilet: %s") % sim.get_last_save_error(), "ERROR")
	_refresh()

func _on_new_save() -> void:
	var sim := _sim()
	if sim == null or not sim.is_game_running(): return
	var stamp := Time.get_datetime_string_from_system(false, true).replace(":", "-")
	var day_fmt := _tr_or("UI_SAVELOAD_DAY_FORMAT", "Dag %s")
	_on_save("%s — %s" % [day_fmt % str(sim.current_day), stamp])

func _on_delete(file_name: String, slot_name: String, button: Button) -> void:
	if _delete_armed != file_name:
		_delete_armed = file_name
		button.text = _tr_or("UI_SAVELOAD_DELETE_CONFIRM", "Bekreft sletting")
		return
	_delete_armed = ""
	var sim := _sim()
	if sim != null and sim.delete_save(file_name):
		_toast(_tr_or("UI_TOAST_DELETED", "🗑 Lagring slettet: %s") % slot_name, "INFO")
	_refresh()

func _toast(message: String, level: String = "INFO") -> void:
	var mgr := get_node_or_null("/root/ToastNotification")
	if mgr and mgr.has_method("show_toast"):
		mgr.show_toast(message, level, 4.0)
