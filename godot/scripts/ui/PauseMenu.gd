extends Control

## PauseMenu — the in-game pause menu (Esc), roadmap U1-06 / D15.
##
## Esc on the home screen (the dashboard) opens it; Esc again, or Fortsett,
## closes it. While it is open the game clock stands still and the world
## behind it is frosted; closing restores the clock to how it was (a game the
## player had paused stays paused).
##
##   Fortsett · Lagre spill · Last inn spill · Innstillinger · Medvirkende ·
##   Til hovedmenyen · Avslutt spillet
##
## Save and load open the save list over the menu (it replaced the
## "Lagre/last inn" tab, D15); settings and credits open in place. Leaving —
## the main menu, the quit button or the window's close button (Main.gd turns
## the close request into request_quit()) — asks first: "Lagre før du
## avslutter?" with Lagre og avslutt / Avslutt uten å lagre / Avbryt. Saving
## there writes the quick-save slot, which "Fortsett" on the main menu loads.
##
## The menu owns the keyboard while open: Esc steps back (guard, sub-menu,
## then the menu itself) and the game's own keys (Space, N, I, Cmd/Ctrl+digit)
## do nothing until it closes.

signal opened
signal closed
signal main_menu_requested
signal quit_requested

const MAIN_MENU_SCENE := "res://scenes/MainMenu.tscn"
const CREDITS_SCENE := "res://scenes/ui/CreditsScreen.tscn"
const FADE_IN_S := 0.18
const FADE_OUT_S := 0.14
## Actions of the game screen that wait while the menu is open.
const BLOCKED_ACTIONS := [&"toggle_pause", &"toggle_notifications", &"toggle_attention_inbox"]

enum Leave { NONE, QUIT, MAIN_MENU }

@onready var _backdrop: ColorRect = %Backdrop
@onready var _center: Control = %Center
@onready var _panel: PanelContainer = %Panel
@onready var _overline: Label = %Overline
@onready var _title: Label = %TitleLabel
@onready var _context: Label = %ContextLabel
@onready var btn_resume: Button = %ResumeButton
@onready var btn_save: Button = %SaveButton
@onready var btn_load: Button = %LoadButton
@onready var btn_settings: Button = %SettingsButton
@onready var btn_credits: Button = %CreditsButton
@onready var btn_main_menu: Button = %MainMenuButton
@onready var btn_quit: Button = %QuitButton
@onready var guard: Control = %SaveGuard
@onready var guard_title: Label = %GuardTitle
@onready var _guard_body: Label = %GuardBody
@onready var _guard_error: Label = %GuardError
@onready var guard_cancel: Button = %GuardCancel
@onready var guard_discard: Button = %GuardDiscard
@onready var guard_save: Button = %GuardSave
@onready var settings_menu: Control = %SettingsMenu
@onready var save_load_menu: Control = %SaveLoadMenu

var credits_screen: Control = null
## Test seams: when valid, called instead of changing scene (with the scene
## path) or quitting the application.
var scene_change_override: Callable = Callable()
var quit_override: Callable = Callable()

var _sim: Node = null
var _open: bool = false
var _was_paused: bool = false
var _leave: Leave = Leave.NONE
var _return_focus: Button = null
var _fade: Tween = null

func _ready() -> void:
	_sim = get_node_or_null("/root/SimBridge")
	_apply_texts()
	btn_resume.pressed.connect(hide_menu)
	btn_save.pressed.connect(_open_save_list.bind("save", btn_save))
	btn_load.pressed.connect(_open_save_list.bind("load", btn_load))
	btn_settings.pressed.connect(_open_settings)
	btn_credits.pressed.connect(open_credits)
	btn_main_menu.pressed.connect(_ask.bind(Leave.MAIN_MENU))
	btn_quit.pressed.connect(_ask.bind(Leave.QUIT))
	guard_cancel.pressed.connect(_cancel_guard)
	guard_discard.pressed.connect(_leave_now)
	guard_save.pressed.connect(_save_and_leave)
	settings_menu.close_requested.connect(_on_submenu_closed)
	save_load_menu.close_requested.connect(_on_submenu_closed)
	save_load_menu.game_loaded.connect(_on_game_loaded)
	UIPolish.link_vertical_focus(btn_resume.get_parent())
	UIPolish.attach_press_feedback(_panel)
	hide()

func _apply_texts() -> void:
	_overline.text = tr("UI_PAUSE_OVERLINE")
	_title.text = tr("UI_PAUSE_TITLE")
	btn_resume.text = tr("UI_PAUSE_RESUME")
	btn_resume.tooltip_text = tr("UI_PAUSE_RESUME_TIP")
	btn_save.text = tr("UI_PAUSE_SAVE")
	btn_save.tooltip_text = tr("UI_PAUSE_SAVE_TIP")
	btn_load.text = tr("UI_PAUSE_LOAD")
	btn_load.tooltip_text = tr("UI_PAUSE_LOAD_TIP")
	btn_settings.text = tr("UI_BUTTON_SETTINGS")
	btn_credits.text = tr("UI_BUTTON_CREDITS")
	btn_main_menu.text = tr("UI_PAUSE_MAIN_MENU")
	btn_main_menu.tooltip_text = tr("UI_PAUSE_MAIN_MENU_TIP")
	btn_quit.text = tr("UI_PAUSE_QUIT")
	btn_quit.tooltip_text = tr("UI_PAUSE_QUIT_TIP")
	guard_cancel.text = tr("UI_GUARD_CANCEL")
	_guard_body.text = tr("UI_GUARD_BODY")

# ── Public API ─────────────────────────────────────────────────────────────────

func is_open() -> bool:
	return _open

## Open the menu: the clock stops (its state is restored on close).
func show_menu() -> void:
	if _open:
		return
	_open = true
	_was_paused = _sim.is_paused() if _sim != null else true
	if _sim != null:
		_sim.set_paused(true)
	_refresh_context()
	guard.hide()
	_center.show()
	mouse_filter = Control.MOUSE_FILTER_STOP
	show()
	_animate(true)
	btn_resume.grab_focus.call_deferred()
	opened.emit()

## Close the menu and give the clock back its state from before.
func hide_menu() -> void:
	if not _open:
		return
	_open = false
	_close_submenus()
	guard.hide()
	if _sim != null:
		_sim.set_paused(_was_paused)
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	_animate(false)
	closed.emit()

## The window's close button (or the quit button): open the menu if needed and
## ask "Lagre før du avslutter?".
func request_quit() -> void:
	show_menu()
	_ask(Leave.QUIT)

## Medvirkende over the menu (also reachable from the main menu).
func open_credits() -> void:
	if credits_screen == null or not is_instance_valid(credits_screen):
		credits_screen = (load(CREDITS_SCENE) as PackedScene).instantiate()
		credits_screen.close_requested.connect(_on_credits_closed)
		add_child(credits_screen)
	_return_focus = btn_credits
	_center.hide()
	credits_screen.show()

# ── Input ──────────────────────────────────────────────────────────────────────

## Ahead of the game screen (this menu sits on a higher layer, later in the
## tree): Esc steps back one level; the game's own keys wait.
func _input(event: InputEvent) -> void:
	if not _open:
		return
	if event.is_action_pressed("ui_cancel"):
		_step_back()
		get_viewport().set_input_as_handled()
		return
	for action in BLOCKED_ACTIONS:
		if event.is_action(action):
			get_viewport().set_input_as_handled()
			return
	var key := event as InputEventKey
	if key != null and key.is_command_or_control_pressed() and key.keycode >= KEY_0 and key.keycode <= KEY_9:
		get_viewport().set_input_as_handled()

func _step_back() -> void:
	if guard.visible:
		_cancel_guard()
	elif credits_screen != null and is_instance_valid(credits_screen) and credits_screen.visible:
		credits_screen.close()
	elif save_load_menu.visible:
		save_load_menu._close()
	elif settings_menu.visible:
		settings_menu._on_close()
	else:
		hide_menu()

# ── Sub-menus ──────────────────────────────────────────────────────────────────

func _open_save_list(mode: String, from: Button) -> void:
	_return_focus = from
	_center.hide()
	if mode == "save":
		save_load_menu.show_save()
	else:
		save_load_menu.show_load()

func _open_settings() -> void:
	_return_focus = btn_settings
	_center.hide()
	settings_menu.show()

func _on_submenu_closed() -> void:
	if not _open or guard.visible:
		return
	_center.show()
	if _return_focus != null:
		_return_focus.grab_focus.call_deferred()

func _on_credits_closed() -> void:
	_on_submenu_closed()

## A save loaded from the menu replaces the game: close the menu and leave the
## loaded game paused, so the player sees it before the clock runs.
func _on_game_loaded(_file_name: String) -> void:
	_was_paused = true
	hide_menu()

func _close_submenus() -> void:
	if settings_menu.visible:
		settings_menu.hide()
	if save_load_menu.visible:
		save_load_menu.hide()
	if credits_screen != null and is_instance_valid(credits_screen):
		credits_screen.hide()

# ── Leaving: the save guard ────────────────────────────────────────────────────

func _ask(leave: Leave) -> void:
	_close_submenus()
	_leave = leave
	_return_focus = btn_quit if leave == Leave.QUIT else btn_main_menu
	var quitting := leave == Leave.QUIT
	guard_title.text = tr("UI_GUARD_QUIT_TITLE" if quitting else "UI_GUARD_MENU_TITLE")
	guard_discard.text = tr("UI_GUARD_QUIT_DISCARD" if quitting else "UI_GUARD_MENU_DISCARD")
	guard_save.text = tr("UI_GUARD_QUIT_SAVE" if quitting else "UI_GUARD_MENU_SAVE")
	_guard_error.hide()
	_center.hide()
	guard.show()
	guard_save.grab_focus.call_deferred()

func _cancel_guard() -> void:
	guard.hide()
	_leave = Leave.NONE
	_center.show()
	if _return_focus != null:
		_return_focus.grab_focus.call_deferred()

## Save to the quick-save slot, then leave; a failed save stays here and says
## why (leaving would lose the game).
func _save_and_leave() -> void:
	var ok: bool = _sim != null and _sim.quick_save()
	if not ok:
		var reason: String = _sim.get_last_save_error() if _sim != null else ""
		_guard_error.text = tr("UI_GUARD_SAVE_FAILED") % reason
		_guard_error.show()
		return
	_leave_now()

func _leave_now() -> void:
	var leave := _leave
	_leave = Leave.NONE
	guard.hide()
	match leave:
		Leave.QUIT:
			quit_requested.emit()
			if quit_override.is_valid():
				quit_override.call()
			else:
				get_tree().quit()
		Leave.MAIN_MENU:
			# The abandoned game must not tick (or autosave) behind the menu.
			if _sim != null:
				_sim.set_paused(true)
			_open = false
			main_menu_requested.emit()
			if scene_change_override.is_valid():
				scene_change_override.call(MAIN_MENU_SCENE)
			else:
				get_tree().change_scene_to_file(MAIN_MENU_SCENE)

# ── Look ───────────────────────────────────────────────────────────────────────

## "Dag 13 · 13. jan. 2027 · PO3 i rødt" — where the player paused.
func _refresh_context() -> void:
	if _sim == null:
		_context.text = ""
		return
	var day: int = int(_sim.current_day)
	var parts: PackedStringArray = [tr("UI_DAY_N") % day, SimFormatters.format_day(day)]
	var scenario := _scenario_name()
	if not scenario.is_empty():
		parts.append(scenario)
	_context.text = " · ".join(parts)

func _scenario_name() -> String:
	if not _sim.has_method("get_active_scenario_id"):
		return ""
	var id := str(_sim.get_active_scenario_id())
	if id.is_empty():
		return ""
	for entry in _sim.get_available_scenarios():
		if entry is Dictionary and str(entry.get("id", "")) == id:
			return str(entry.get("name", ""))
	return ""

func _animate(appear: bool) -> void:
	if _fade != null and _fade.is_valid():
		_fade.kill()
	_panel.pivot_offset = _panel.size * 0.5
	_fade = create_tween().set_parallel(true)
	if appear:
		modulate.a = 0.0
		_panel.scale = Vector2(0.97, 0.97)
		_fade.tween_property(self, "modulate:a", 1.0, FADE_IN_S)
		_fade.tween_property(_panel, "scale", Vector2.ONE, FADE_IN_S + 0.04) \
			.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
	else:
		_fade.tween_property(self, "modulate:a", 0.0, FADE_OUT_S)
		_fade.chain().tween_callback(hide)
