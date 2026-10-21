extends Control

## Main menu (roadmap U1-09): the live fjord behind an Ink-glass column.
## Layout lives in scenes/MainMenu.tscn; this script is wiring only:
## signal hookups, localization, the fade-in, the hover marker, the backdrop
## and the scenario panel spawn.
##
## - The world is the game's own FjordEnvironment, staged by MenuBackdrop.gd
##   (late winter in Troms, calm water, a slow camera drift). An Ink veil
##   covers the first frames and lifts once the terrain is in.
## - The column is Ink glass at 90 % (D13), so its text reads over any sky;
##   ThemeLint judges it over a white and a black world.
## - Reduced motion (MotionPrefs) holds the camera, drops the slide-in and
##   shortens the veil's fade.
## - "Fortsett" (roadmap U1-06) loads the newest save and enters the game; it
##   is shown, as the primary action, only when a save exists, with the save's
##   day and slot under it. "Medvirkende" opens the credits over the menu.

const SCENARIO_PANEL_SCRIPT = preload("res://scripts/ui/ScenarioSelectPanel.gd")
const MOD_MANAGER_SCENE = "res://scenes/ui/ModManagerPanel.tscn"
const GAME_SCENE = "res://scenes/GameScene.tscn"
const CREDITS_SCENE = "res://scenes/ui/CreditsScreen.tscn"

## The veil lifts this long after the world is in (seconds), or at most this
## long after the menu opens if the terrain is slow (a busy machine).
const VEIL_FADE_S := 0.9
const VEIL_FADE_REDUCED_S := 0.25
const VEIL_MAX_WAIT_S := 2.5
## The column's content slides in from the left this far (px) over this long.
const COLUMN_SLIDE_PX := 24.0
const COLUMN_SLIDE_S := 0.45
## The hover marker: a salmon bar at the left edge of the item under the
## pointer or keyboard focus.
const MARKER_INSET := Vector2(-10.0, 14.0)
const MARKER_MOVE_S := 0.14

@onready var backdrop: MenuBackdrop = %Backdrop
@onready var vignette: ColorRect = %Vignette
@onready var center: Control = %Center
@onready var glass: ColorRect = %Glass
@onready var menu_vbox: VBoxContainer = %MenuVBox
@onready var lockup: Control = %Lockup
@onready var brand_mark: TextureRect = %BrandMark
@onready var wordmark: TextureRect = %Wordmark
@onready var subtitle_label: Label = %SubtitleLabel
@onready var version_label: Label = %VersionLabel
@onready var btn_continue: Button = %BtnContinue
@onready var continue_caption: Label = %ContinueCaption
@onready var btn_new_game: Button = %BtnNewGame
@onready var btn_load: Button = %BtnLoad
@onready var btn_settings: Button = %BtnSettings
@onready var btn_credits: Button = %BtnCredits
@onready var btn_quit: Button = %BtnQuit
@onready var marker: ColorRect = %Marker
@onready var veil: ColorRect = %Veil
@onready var settings_menu: Control = %SettingsMenu
@onready var save_load_menu: Control = %SaveLoadMenu

var scenario_panel: Control = null
var mod_manager_panel: Control = null
var credits_screen: Control = null
## The newest save "Fortsett" loads ({} when there is none).
var _latest_save: Dictionary = {}
## Test seam: when valid, called instead of changing to GAME_SCENE (headless UI
## tests drive the menu without tearing down their own scene).
var enter_game_override: Callable = Callable()
## True once the veil has lifted and the world shows.
var revealed := false
## Redusert bevegelse, read when the menu opens and when settings close.
var reduced_motion := false
var _veil_tween: Tween = null
var _marker_tween: Tween = null
var _marker_target: Control = null

signal new_game_requested
signal load_game_requested
signal settings_requested
## The veil has lifted: the world and the column are on screen.
signal revealed_world


func _ready() -> void:
	reduced_motion = MotionPrefs.reduced_motion()
	_apply_localization()
	_connect_buttons()
	_connect_submenus()
	_ensure_mods_button()
	refresh_continue()
	resized.connect(_layout_vignette)
	_layout_vignette()
	_reveal_column()
	if backdrop.is_world_ready():
		_lift_veil()
	else:
		backdrop.world_ready.connect(_lift_veil, CONNECT_ONE_SHOT)
		get_tree().create_timer(VEIL_MAX_WAIT_S).timeout.connect(_lift_veil)
	var audio := get_node_or_null("/root/AudioController")
	if audio != null and audio.has_method("play_menu_music"):
		audio.play_menu_music()


## The language changed (Innstillinger > Spill > Språk, U2-08): redraw at once.
func _notification(what: int) -> void:
	if what == NOTIFICATION_TRANSLATION_CHANGED and is_node_ready():
		_apply_localization()

func _apply_localization() -> void:
	subtitle_label.text = _tr_or("UI_MAINMENU_SUBTITLE", "Norsk lakseoppdrettssimulator")
	# The wordmark is a picture of the name: screen readers get the name.
	if "accessibility_name" in wordmark:
		wordmark.set("accessibility_name", _tr_or("UI_MAINMENU_TITLE", "Lakskongen"))
	btn_continue.text = tr("UI_BUTTON_CONTINUE")
	btn_continue.tooltip_text = tr("UI_BUTTON_CONTINUE_TIP")
	btn_new_game.text = _tr_or("UI_BUTTON_NEW_GAME", "Nytt spill")
	btn_load.text = _tr_or("UI_BUTTON_LOAD_GAME", "Last inn spill")
	btn_settings.text = _tr_or("UI_BUTTON_SETTINGS", "Innstillinger")
	btn_credits.text = tr("UI_BUTTON_CREDITS")
	btn_quit.text = _tr_or("UI_BUTTON_QUIT", "Avslutt")
	version_label.text = version_text()


## "v0.5.0 (alfa)": the number from application/config/version, the stage
## through the translation.
static func version_text() -> String:
	var version := str(ProjectSettings.get_setting("application/config/version", "0.0.0"))
	return TranslationServer.translate("UI_VERSION_TEXT") % version.get_slice("-", 0)


func _tr_or(key: String, fallback: String) -> String:
	var t := tr(key)
	return t if t != key else fallback


func _menu_buttons() -> Array[Button]:
	var out: Array[Button] = []
	for child in menu_vbox.get_children():
		if child is Button:
			out.append(child as Button)
	return out


func _connect_buttons() -> void:
	btn_continue.pressed.connect(_on_continue_pressed)
	btn_new_game.pressed.connect(_on_new_game_pressed)
	btn_load.pressed.connect(_on_load_pressed)
	btn_settings.pressed.connect(_on_settings_pressed)
	btn_credits.pressed.connect(_on_credits_pressed)
	btn_quit.pressed.connect(_on_quit_pressed)
	for btn in [btn_continue, btn_new_game, btn_load, btn_settings, btn_credits, btn_quit]:
		_wire_feedback(btn)
	UIPolish.link_vertical_focus(menu_vbox)


## Press pop, and the salmon marker on hover and keyboard focus.
func _wire_feedback(btn: Button) -> void:
	btn.mouse_entered.connect(_point_marker.bind(btn))
	btn.focus_entered.connect(_point_marker.bind(btn))
	btn.mouse_exited.connect(_on_marker_left.bind(btn))
	btn.focus_exited.connect(_on_marker_left.bind(btn))
	UIPolish.attach_press_feedback(btn)


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
	# The settings may have changed Redusert bevegelse.
	reduced_motion = MotionPrefs.reduced_motion()
	backdrop.set_reduced_motion(reduced_motion)
	focus_button.grab_focus.call_deferred()


# ── The world and the reveal ──────────────────────────────────────────────────

## Lift the Ink veil over the world (once): a quick fade, shorter still with
## reduced motion.
func _lift_veil() -> void:
	if revealed or not is_inside_tree():
		return
	revealed = true
	var duration := VEIL_FADE_REDUCED_S if reduced_motion else VEIL_FADE_S
	_veil_tween = create_tween()
	_veil_tween.tween_property(veil, "color:a", 0.0, duration).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_OUT)
	_veil_tween.tween_callback(func() -> void: veil.visible = false)
	revealed_world.emit()


## The column's content eases in from the left (it holds still with reduced
## motion). Only the offset moves: the text is at full opacity from the
## first frame, so the contrast checks judge it as the player reads it.
func _reveal_column() -> void:
	if reduced_motion:
		return
	var margin := menu_vbox.get_parent() as Control
	var rest := margin.position.x
	margin.position.x = rest - COLUMN_SLIDE_PX
	var tw := create_tween()
	tw.tween_property(margin, "position:x", rest, COLUMN_SLIDE_S).set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)


## The vignette's column shade ends where the glass does, at any size.
func _layout_vignette() -> void:
	var mat := vignette.material as ShaderMaterial
	if mat == null or size.x <= 0.0:
		return
	mat.set_shader_parameter("column_end", center.size.x / size.x)


func _point_marker(btn: Button) -> void:
	if btn.disabled or not btn.is_visible_in_tree():
		return
	_marker_target = btn
	var rect := btn.get_global_rect()
	# x from the column's margin, not the button: the slide-in moves the
	# buttons but the marker lands where they come to rest.
	var margin := menu_vbox.get_parent() as Control
	var x := float(margin.get_theme_constant("margin_left")) + MARKER_INSET.x - marker.size.x
	var target := Vector2(x, rect.position.y - center.get_global_rect().position.y + MARKER_INSET.y)
	marker.size = Vector2(marker.size.x, rect.size.y - MARKER_INSET.y * 2.0)
	if _marker_tween != null and _marker_tween.is_valid():
		_marker_tween.kill()
	if not marker.visible or reduced_motion:
		marker.position = target
		marker.visible = true
		return
	_marker_tween = create_tween()
	_marker_tween.tween_property(marker, "position", target, MARKER_MOVE_S).set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)


func _on_marker_left(btn: Button) -> void:
	if _marker_target != btn:
		return
	# Keyboard focus keeps the marker on the focused item.
	var focused := get_viewport().gui_get_focus_owner()
	if focused is Button and (focused as Button).get_parent() == menu_vbox and focused != btn:
		_point_marker(focused as Button)
		return
	if btn.has_focus():
		return
	marker.visible = false
	_marker_target = null


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
	btn_new_game.theme_type_variation = &"LargeGhostButton" if has_save else &"LargePrimaryButton"
	if has_save:
		var raw_name := str(_latest_save.get("name", ""))
		var slot: String = save_load_menu.slot_display_name(raw_name)
		continue_caption.text = tr("UI_CONTINUE_CAPTION") % [int(_latest_save.get("day", 0)), slot]
		# A name the player chose is shown as typed (text lint: data).
		if slot == raw_name:
			continue_caption.set_meta(&"player_text", true)
		elif continue_caption.has_meta(&"player_text"):
			continue_caption.remove_meta(&"player_text")
	UIPolish.link_vertical_focus(menu_vbox)
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

## Adds a "Mods" item to the menu between Settings and Credits (programmatic,
## so the item list in MainMenu.tscn stays the core set).
func _ensure_mods_button() -> void:
	if not is_instance_valid(menu_vbox) or menu_vbox.has_node("BtnMods"):
		return
	var btn := Button.new()
	btn.name = "BtnMods"
	btn.text = _tr_or("UI_BUTTON_MODS", "Mods")
	btn.custom_minimum_size = Vector2(0, 52)
	btn.alignment = HORIZONTAL_ALIGNMENT_LEFT
	btn.theme_type_variation = &"LargeGhostButton"
	btn.pressed.connect(_on_mods_pressed)
	menu_vbox.add_child(btn)
	# Place it just before the credits for a predictable order.
	if is_instance_valid(btn_credits):
		menu_vbox.move_child(btn, btn_credits.get_index())
	_wire_feedback(btn)
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
	if is_instance_valid(scenario_panel):
		scenario_panel.queue_free()
	scenario_panel = SCENARIO_PANEL_SCRIPT.new()
	add_child(scenario_panel)
	if scenario_panel.has_signal("scenario_selected"):
		scenario_panel.scenario_selected.connect(_on_scenario_selected)
	if scenario_panel.has_signal("panel_close_requested"):
		scenario_panel.panel_close_requested.connect(_on_scenario_close)
	center.visible = false


## The picker started a scenario: enter the game when it started, or keep the
## picker open and say why (an unknown or locked id no longer falls back to
## the default game, F692).
func _on_scenario_selected(scenario_id: String) -> void:
	var sim = get_node_or_null("/root/SimBridge")
	if sim == null:
		return
	_disable_all()
	# start_new_game(player_name, starting_capital, scenario_id) —
	# pass the id in the scenario slot, not the player-name slot.
	if sim.start_new_game("Player", 10000000, scenario_id):
		_enter_game()
		return
	_enable_all()
	if is_instance_valid(scenario_panel):
		scenario_panel.start_failed(tr("UI_SCENARIO_UNAVAILABLE"))


## Esc or Tilbake in the picker: back to a usable menu.
func _on_scenario_close() -> void:
	if is_instance_valid(scenario_panel):
		scenario_panel.queue_free()
	scenario_panel = null
	_enable_all()
	center.visible = true
	refresh_continue()


func _disable_all() -> void:
	_set_buttons_disabled(true)


func _enable_all() -> void:
	_set_buttons_disabled(false)


func _set_buttons_disabled(disabled: bool) -> void:
	for btn: Button in [btn_continue, btn_new_game, btn_load, btn_settings, btn_credits, btn_quit]:
		btn.disabled = disabled


## Esc in the picker is the picker's own (it emits panel_close_requested,
## U1-07); it used to hide itself first, so this never ran and the menu stayed
## empty behind it.
func _input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		if is_instance_valid(scenario_panel) and scenario_panel.visible:
			_on_scenario_close()
			get_viewport().set_input_as_handled()
