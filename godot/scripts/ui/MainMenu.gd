extends Control

## MainMenu v2.1 — Nordic Minimalist (scene-based).
## Layout lives in scenes/MainMenu.tscn; this script is wiring only:
## signal hookups, localization, fade tween, wave animation, scenario panel spawn.

const SCENARIO_PANEL_SCRIPT = preload("res://scripts/ui/ScenarioSelectPanel.gd")
const MOD_MANAGER_SCENE = "res://scenes/ui/ModManagerPanel.tscn"

@onready var wave_effect: ColorRect = %WaveEffect
@onready var center: CenterContainer = %Center
@onready var menu_vbox: VBoxContainer = %MenuVBox
@onready var fish_icon: Label = %FishIcon
@onready var title_label: Label = %TitleLabel
@onready var subtitle_label: Label = %SubtitleLabel
@onready var version_label: Label = %VersionLabel
@onready var btn_new_game: Button = %BtnNewGame
@onready var btn_load: Button = %BtnLoad
@onready var btn_settings: Button = %BtnSettings
@onready var btn_quit: Button = %BtnQuit

var wave_tween: Tween
var scenario_panel: Control = null
var mod_manager_panel: Control = null

signal new_game_requested
signal load_game_requested
signal settings_requested

func _ready() -> void:
	_apply_localization()
	_apply_heading_font()
	_connect_buttons()
	_ensure_mods_button()

	# Fade in
	modulate.a = 0.0
	var fade := create_tween()
	fade.tween_property(self, "modulate:a", 1.0, 0.8).set_ease(Tween.EASE_OUT)

	_start_wave_animation()

func _apply_localization() -> void:
	title_label.text = _tr_or("UI_MAINMENU_TITLE", "LAKSKONGEN")
	subtitle_label.text = _tr_or("UI_MAINMENU_SUBTITLE", "Norsk Lakseoppdrett Simulator")
	btn_new_game.text = "🐟  " + _tr_or("UI_BUTTON_NEW_GAME", "Nytt Spill")
	btn_load.text = "💾  " + _tr_or("UI_BUTTON_LOAD_GAME", "Last Spill")
	btn_settings.text = "⚙️  " + _tr_or("UI_BUTTON_SETTINGS", "Innstillinger")
	btn_quit.text = "🚪  " + _tr_or("UI_BUTTON_QUIT", "Avslutt")
	version_label.text = _tr_or("UI_VERSION_TEXT", "v0.2.0 Alpha — Nordic Minimalist")

func _tr_or(key: String, fallback: String) -> String:
	var t := tr(key)
	return t if t != key else fallback

func _apply_heading_font() -> void:
	var theme_node = get_node_or_null("/root/LakskongenTheme")
	if theme_node and theme_node.font_heading_bold:
		title_label.add_theme_font_override("font", theme_node.font_heading_bold)

func _connect_buttons() -> void:
	btn_new_game.pressed.connect(_on_new_game_pressed)
	btn_load.pressed.connect(_on_load_pressed)
	btn_settings.pressed.connect(_on_settings_pressed)
	btn_quit.pressed.connect(_on_quit_pressed)
	for btn in [btn_new_game, btn_load, btn_settings, btn_quit]:
		btn.mouse_entered.connect(_on_btn_hover.bind(btn, true))
		btn.mouse_exited.connect(_on_btn_hover.bind(btn, false))
	# Polish: press-pop + keyboard navigation between the menu buttons.
		UIPolish.attach_press_feedback(btn)
	UIPolish.link_vertical_focus(btn_new_game.get_parent())
	# Keyboard accessibility: focus the primary action on entry.
	btn_new_game.grab_focus.call_deferred()

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
	_disable_all()
	emit_signal("new_game_requested")
	_show_scenario_panel()

func _on_load_pressed() -> void:
	emit_signal("load_game_requested")

func _on_settings_pressed() -> void:
	emit_signal("settings_requested")

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
	btn.text = "🧩  " + _tr_or("UI_BUTTON_MODS", "Mods")
	btn.custom_minimum_size = Vector2(320, 52)
	btn.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
	btn.add_theme_font_size_override("font_size", 16)
	btn.pressed.connect(_on_mods_pressed)
	UIPolish.attach_press_feedback(btn)
	menu_vbox.add_child(btn)
	# Place it just before Quit for a predictable order.
	if is_instance_valid(btn_quit):
		menu_vbox.move_child(btn, btn_quit.get_index())
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
		# start_new_game(player_name, starting_capital, scenario_id) —
		# pass the id in the scenario slot, not the player-name slot.
		sim.start_new_game("Player", 10000000, scenario_id)
		get_tree().change_scene_to_file("res://scenes/GameScene.tscn")

func _on_scenario_close() -> void:
	if scenario_panel:
		scenario_panel.queue_free()
		scenario_panel = null
	center.visible = true

func _disable_all() -> void:
	btn_new_game.disabled = true
	btn_load.disabled = true
	btn_settings.disabled = true
	btn_quit.disabled = true

func _input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		if scenario_panel and scenario_panel.visible:
			_on_scenario_close()
			get_viewport().set_input_as_handled()
