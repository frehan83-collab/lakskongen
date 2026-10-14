extends Control

## MainMenu v2.1 — Nordic Minimalist (scene-based).
## Layout lives in scenes/MainMenu.tscn; this script is wiring only:
## signal hookups, localization, fade tween, wave animation, scenario panel spawn.
##
## "Fortsett" (roadmap U1-06) loads the newest save and enters the game; it is
## shown, as the primary action, only when a save exists, with the save's day
## and slot under it. "Medvirkende" opens the credits over the menu.

const SCENARIO_PANEL_SCRIPT = preload("res://scripts/ui/ScenarioSelectPanel.gd")
const MOD_MANAGER_SCENE = "res://scenes/ui/ModManagerPanel.tscn"
const GAME_SCENE = "res://scenes/GameScene.tscn"
const CREDITS_SCENE = "res://scenes/ui/CreditsScreen.tscn"

@onready var wave_effect: ColorRect = %WaveEffect
@onready var center: CenterContainer = %Center
@onready var menu_vbox: VBoxContainer = %MenuVBox
@onready var fish_icon: Label = %FishIcon
@onready var title_label: Label = %TitleLabel
@onready var subtitle_label: Label = %SubtitleLabel
@onready var version_label: Label = %VersionLabel
@onready var btn_continue: Button = %BtnContinue
@onready var continue_caption: Label = %ContinueCaption
@onready var btn_new_game: Button = %BtnNewGame
@onready var btn_load: Button = %BtnLoad
@onready var btn_settings: Button = %BtnSettings
@onready var btn_credits: Button = %BtnCredits
@onready var btn_quit: Button = %BtnQuit
@onready var settings_menu: Control = %SettingsMenu
@onready var save_load_menu: Control = %SaveLoadMenu

var wave_tween: Tween
var scenario_panel: Control = null
var mod_manager_panel: Control = null
var credits_screen: Control = null
## The newest save "Fortsett" loads ({} when there is none).
var _latest_save: Dictionary = {}
## Test seam: when valid, called instead of changing to GAME_SCENE (headless UI
## tests drive the menu without tearing down their own scene).
var enter_game_override: Callable = Callable()

signal new_game_requested
signal load_game_requested
signal settings_requested

func _ready() -> void:
	_apply_localization()
	_apply_heading_font()
	_connect_buttons()
	_connect_submenus()
	_ensure_mods_button()
	refresh_continue()

	# Fade in
	modulate.a = 0.0
	var fade := create_tween()
	fade.tween_property(self, "modulate:a", 1.0, 0.8).set_ease(Tween.EASE_OUT)

	_start_wave_animation()

func _apply_localization() -> void:
	title_label.text = _tr_or("UI_MAINMENU_TITLE", "LAKSKONGEN")
	subtitle_label.text = _tr_or("UI_MAINMENU_SUBTITLE", "Norsk Lakseoppdrett Simulator")
	btn_continue.text = tr("UI_BUTTON_CONTINUE")
	btn_continue.tooltip_text = tr("UI_BUTTON_CONTINUE_TIP")
	btn_new_game.text = _tr_or("UI_BUTTON_NEW_GAME", "Nytt spill")
	btn_load.text = _tr_or("UI_BUTTON_LOAD_GAME", "Last inn spill")
	btn_settings.text = _tr_or("UI_BUTTON_SETTINGS", "Innstillinger")
	btn_credits.text = tr("UI_BUTTON_CREDITS")
	btn_quit.text = _tr_or("UI_BUTTON_QUIT", "Avslutt")
	version_label.text = _tr_or("UI_VERSION_TEXT", "v0.2.0 Alpha")

func _tr_or(key: String, fallback: String) -> String:
	var t := tr(key)
	return t if t != key else fallback

func _apply_heading_font() -> void:
	# The title's Display role (theme variation) already sets the display face.
	LakskongenTheme.set_strong(title_label)

func _connect_buttons() -> void:
	btn_continue.pressed.connect(_on_continue_pressed)
	btn_new_game.pressed.connect(_on_new_game_pressed)
	btn_load.pressed.connect(_on_load_pressed)
	btn_settings.pressed.connect(_on_settings_pressed)
	btn_credits.pressed.connect(_on_credits_pressed)
	btn_quit.pressed.connect(_on_quit_pressed)
	for btn in [btn_continue, btn_new_game, btn_load, btn_settings, btn_credits, btn_quit]:
		btn.mouse_entered.connect(_on_btn_hover.bind(btn, true))
		btn.mouse_exited.connect(_on_btn_hover.bind(btn, false))
	# Polish: press-pop + keyboard navigation between the menu buttons.
		UIPolish.attach_press_feedback(btn)
	UIPolish.link_vertical_focus(btn_new_game.get_parent())

## The embedded SettingsMenu / SaveLoadMenu are shown in place of the button
## column; closing either restores it (F616).
func _connect_submenus() -> void:
	settings_menu.close_requested.connect(_on_submenu_closed.bind(btn_settings))
	save_load_menu.close_requested.connect(_on_submenu_closed.bind(btn_load))
	save_load_menu.game_loaded.connect(_on_game_loaded)

func _on_submenu_closed(focus_button: Button) -> void:
	center.visible = true
	_enable_all()
	refresh_continue()
	focus_button.grab_focus.call_deferred()

# ── Fortsett (continue the newest save) ───────────────────────────────────────

## Show "Fortsett" when a save exists: the primary action, with the save's day
## and slot under it; otherwise "Nytt spill" is primary. Focus goes to the
## primary action.
func refresh_continue() -> void:
	_latest_save = {}
	var sim := get_node_or_null("/root/SimBridge")
	if sim != null:
		var saves: Array = sim.get_save_list()
		if not saves.is_empty():
			_latest_save = saves[0]  # newest first (SaveLoadManager sorts by save time)
	var has_save := not _latest_save.is_empty()
	btn_continue.visible = has_save
	continue_caption.visible = has_save
	btn_continue.theme_type_variation = &"LargePrimaryButton"
	btn_new_game.theme_type_variation = &"LargeButton" if has_save else &"LargePrimaryButton"
	if has_save:
		var raw_name := str(_latest_save.get("name", ""))
		var slot: String = save_load_menu.slot_display_name(raw_name)
		continue_caption.text = tr("UI_CONTINUE_CAPTION") % [int(_latest_save.get("day", 0)), slot]
		# A name the player chose is shown as typed (text lint: data).
		if slot == raw_name:
			continue_caption.set_meta(&"player_text", true)
		elif continue_caption.has_meta(&"player_text"):
			continue_caption.remove_meta(&"player_text")
	UIPolish.link_vertical_focus(btn_new_game.get_parent())
	(btn_continue if has_save else btn_new_game).grab_focus.call_deferred()

## The save "Fortsett" loads, or {} when there is none.
func get_latest_save() -> Dictionary:
	return _latest_save.duplicate()

func _on_continue_pressed() -> void:
	var sim := get_node_or_null("/root/SimBridge")
	if sim == null or _latest_save.is_empty():
		return
	if sim.load_game(str(_latest_save.get("file", ""))):
		_disable_all()
		_enter_game()
		return
	var toast := get_node_or_null("/root/ToastNotification")
	if toast:
		toast.show_toast(tr("UI_TOAST_LOAD_FAILED") % sim.get_last_save_error(), "WARNING", 5.0)
	refresh_continue()

# ── Medvirkende ────────────────────────────────────────────────────────────────

func _on_credits_pressed() -> void:
	if credits_screen == null or not is_instance_valid(credits_screen):
		credits_screen = (load(CREDITS_SCENE) as PackedScene).instantiate()
		credits_screen.close_requested.connect(_on_submenu_closed.bind(btn_credits))
		add_child(credits_screen)
	center.visible = false
	credits_screen.show()

func _on_btn_hover(button: Button, hovering: bool) -> void:
	var target := Vector2(1.03, 1.03) if hovering else Vector2.ONE
	var tw := create_tween()
	tw.tween_property(button, "scale", target, 0.12).set_trans(Tween.TRANS_BACK).set_ease(Tween.EASE_OUT)

func _start_wave_animation() -> void:
	if wave_tween:
		wave_tween.kill()
	wave_tween = create_tween().set_loops()
	wave_tween.tween_property(wave_effect, "color:a", 0.12, 3.0).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
	wave_tween.tween_property(wave_effect, "color:a", 0.03, 3.0).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)

# ── Handlers ───────────────────────────────────────────────────────────────────

func _on_new_game_pressed() -> void:
	# Buttons stay enabled: the picker hides the column, and cancelling it must
	# hand back a working menu (F617). They are disabled once a game is chosen.
	emit_signal("new_game_requested")
	_show_scenario_panel()

func _on_load_pressed() -> void:
	emit_signal("load_game_requested")
	center.visible = false
	save_load_menu.show_load()

func _on_settings_pressed() -> void:
	emit_signal("settings_requested")
	center.visible = false
	settings_menu.show()

## A save was loaded from the menu: the engine already holds it, so enter the
## game scene (Main.gd keeps a running game instead of starting a new one).
func _on_game_loaded(_file_name: String) -> void:
	_disable_all()
	_enter_game()

func _enter_game() -> void:
	if enter_game_override.is_valid():
		enter_game_override.call()
		return
	get_tree().change_scene_to_file(GAME_SCENE)

func _on_quit_pressed() -> void:
	_disable_all()
	var tw := create_tween()
	tw.tween_property(self, "modulate:a", 0.0, 0.4)
	tw.tween_callback(get_tree().quit)

# ── Mod manager ───────────────────────────────────────────────────────────────

## Adds a "Mods" item to the menu between Settings and Quit (programmatic, so
## MainMenu.tscn stays untouched).
func _ensure_mods_button() -> void:
	if not is_instance_valid(menu_vbox) or menu_vbox.has_node("BtnMods"):
		return
	var btn := Button.new()
	btn.name = "BtnMods"
	btn.text = _tr_or("UI_BUTTON_MODS", "Mods")
	btn.custom_minimum_size = Vector2(320, 52)
	btn.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
	btn.theme_type_variation = &"LargeButton"
	btn.pressed.connect(_on_mods_pressed)
	UIPolish.attach_press_feedback(btn)
	menu_vbox.add_child(btn)
	# Place it just before the credits for a predictable order.
	if is_instance_valid(btn_credits):
		menu_vbox.move_child(btn, btn_credits.get_index())
	UIPolish.link_vertical_focus(menu_vbox)

func _on_mods_pressed() -> void:
	if mod_manager_panel == null or not is_instance_valid(mod_manager_panel):
		var scene = load(MOD_MANAGER_SCENE)
		if scene == null:
			push_warning("MainMenu: failed to load %s" % MOD_MANAGER_SCENE)
			return
		mod_manager_panel = scene.instantiate()
		mod_manager_panel.name = "ModManagerPanel"
		add_child(mod_manager_panel)
		if mod_manager_panel.has_signal("mods_changed"):
			mod_manager_panel.mods_changed.connect(_on_mods_changed)
		if mod_manager_panel.has_signal("panel_close_requested"):
			mod_manager_panel.panel_close_requested.connect(_on_mods_closed)
	mod_manager_panel.show()

func _on_mods_changed() -> void:
	# Hook for refreshing any cached mod state elsewhere in the menu.
	pass

func _on_mods_closed() -> void:
	if is_instance_valid(mod_manager_panel):
		mod_manager_panel.hide()

func _show_scenario_panel() -> void:
	if scenario_panel != null:
		scenario_panel.queue_free()
	scenario_panel = SCENARIO_PANEL_SCRIPT.new()
	add_child(scenario_panel)
	if scenario_panel.has_signal("scenario_selected"):
		scenario_panel.scenario_selected.connect(_on_scenario_selected)
	if scenario_panel.has_signal("panel_close_requested"):
		scenario_panel.panel_close_requested.connect(_on_scenario_close)
	center.visible = false

func _on_scenario_selected(scenario_id: String) -> void:
	var sim = get_node_or_null("/root/SimBridge")
	if sim:
		_disable_all()
		# start_new_game(player_name, starting_capital, scenario_id) —
		# pass the id in the scenario slot, not the player-name slot.
		sim.start_new_game("Player", 10000000, scenario_id)
		_enter_game()

func _on_scenario_close() -> void:
	if scenario_panel:
		scenario_panel.queue_free()
		scenario_panel = null
	_enable_all()
	center.visible = true
	btn_new_game.grab_focus.call_deferred()

func _disable_all() -> void:
	_set_buttons_disabled(true)

func _enable_all() -> void:
	_set_buttons_disabled(false)

func _set_buttons_disabled(disabled: bool) -> void:
	for btn: Button in [btn_continue, btn_new_game, btn_load, btn_settings, btn_credits, btn_quit]:
		btn.disabled = disabled

func _input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		if scenario_panel and scenario_panel.visible:
			_on_scenario_close()
			get_viewport().set_input_as_handled()
