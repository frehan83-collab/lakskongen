extends Control

## PauseMenu — in-game pause overlay for Lakskongen.
## Shown when the player pauses; dims the game world behind it.
## Pauses the simulation via get_tree().paused and optionally GameBridge.
##
## Buttons: Fortsett (Resume), Lagre, Last inn, Innstillinger, Hovedmeny, Avslutt.
##
## Expected node layout:
##   PauseMenu (Control, full-screen, process_mode = ALWAYS so it responds while paused)
##   ├─ Overlay      (ColorRect, full-screen, semi-transparent dark)
##   └─ Panel        (PanelContainer, centred)
##      └─ VBoxContainer
##         ├─ TitleLabel   (Label) "⏸ Pause"
##         ├─ BtnResume    (Button) "Fortsett"
##         ├─ BtnSave      (Button) "Lagre spill"
##         ├─ BtnLoad      (Button) "Last inn spill"
##         ├─ BtnSettings  (Button) "Innstillinger"
##         ├─ Separator    (HSeparator)
##         ├─ BtnMainMenu  (Button) "Tilbake til hovedmeny"
##         └─ BtnQuit      (Button) "Avslutt"
##   (SettingsMenu and SaveLoadMenu as siblings, shown on demand)

signal resume_requested
signal settings_requested
signal save_requested
signal quit_to_menu_requested

# ── Color palette ─────────────────────────────────────────────────────────────

const COLOR_OVERLAY        := Color(0.0, 0.0, 0.05, 0.72)
const COLOR_PANEL_BG       := Color(0.08, 0.14, 0.25, 0.97)
const COLOR_BTN_NORMAL     := Color(0.12, 0.30, 0.52)
const COLOR_BTN_HOVER      := Color(0.18, 0.44, 0.72)
const COLOR_BTN_DANGER     := Color(0.45, 0.08, 0.08)
const COLOR_BTN_DANGER_HOVER := Color(0.65, 0.12, 0.12)

# ── Node references ────────────────────────────────────────────────────────────

@onready var overlay:       ColorRect      = $Overlay
@onready var panel:         PanelContainer = $Panel
@onready var title_label:   Label          = $Panel/VBoxContainer/TitleLabel
@onready var btn_resume:    Button         = $Panel/VBoxContainer/BtnResume
@onready var btn_save:      Button         = $Panel/VBoxContainer/BtnSave
@onready var btn_load:      Button         = $Panel/VBoxContainer/BtnLoad
@onready var btn_settings:  Button         = $Panel/VBoxContainer/BtnSettings
@onready var btn_main_menu: Button         = $Panel/VBoxContainer/BtnMainMenu
@onready var btn_quit:      Button         = $Panel/VBoxContainer/BtnQuit

@onready var settings_menu:  Control = $SettingsMenu
@onready var save_load_menu: Control = $SaveLoadMenu

# ── State ──────────────────────────────────────────────────────────────────────

var _sim: Node          = null
var _save_manager: Node = null
var _game_bridge: Node  = null

# ── Lifecycle ──────────────────────────────────────────────────────────────────

func _ready() -> void:
	_sim          = get_node_or_null("/root/SimBridge")
	_save_manager = get_node_or_null("/root/SaveLoadManager")
	_game_bridge  = get_node_or_null("/root/GameBridge")

	_setup_visuals()
	_connect_buttons()
	_setup_submenus()
	_setup_tooltips()
	_setup_focus_navigation()

	# PauseMenu must process while the tree is paused
	process_mode = Node.PROCESS_MODE_ALWAYS
	hide()


func _unhandled_input(event: InputEvent) -> void:
	if visible and event.is_action_pressed("ui_cancel"):
		_on_resume_pressed()
		get_viewport().set_input_as_handled()

# ── Public API ─────────────────────────────────────────────────────────────────

func show_menu() -> void:
	_pause_simulation(true)
	show()
	modulate = Color(1, 1, 1, 0)
	panel.scale = Vector2(0.88, 0.88)
	var tween := create_tween().set_parallel(true)
	tween.tween_property(self, "modulate:a", 1.0, 0.18)
	tween.tween_property(panel, "scale", Vector2(1, 1), 0.20) \
		.set_trans(Tween.TRANS_BACK).set_ease(Tween.EASE_OUT)


func hide_menu() -> void:
	var tween := create_tween().set_parallel(true)
	tween.tween_property(self, "modulate:a", 0.0, 0.15)
	tween.tween_property(panel, "scale", Vector2(0.88, 0.88), 0.15)
	tween.chain().tween_callback(func():
		hide()
		_pause_simulation(false)
	)

# ── Simulation pause ──────────────────────────────────────────────────────────

func _pause_simulation(paused: bool) -> void:
	get_tree().paused = paused
	if _game_bridge and _game_bridge.has_method("set_paused"):
		_game_bridge.set_paused(paused)

# ── Visual setup ──────────────────────────────────────────────────────────────

func _setup_visuals() -> void:
	overlay.color = COLOR_OVERLAY

	var panel_style := StyleBoxFlat.new()
	panel_style.bg_color                  = COLOR_PANEL_BG
	panel_style.corner_radius_top_left    = 14
	panel_style.corner_radius_top_right   = 14
	panel_style.corner_radius_bottom_left = 14
	panel_style.corner_radius_bottom_right = 14
	panel_style.content_margin_left   = 48
	panel_style.content_margin_right  = 48
	panel_style.content_margin_top    = 36
	panel_style.content_margin_bottom = 36
	panel.add_theme_stylebox_override("panel", panel_style)

	title_label.add_theme_font_size_override("font_size", 32)
	title_label.add_theme_color_override("font_color", Color(0.90, 0.96, 1.00))

	_style_button(btn_resume,    COLOR_BTN_NORMAL,     COLOR_BTN_HOVER,         22)
	_style_button(btn_save,      COLOR_BTN_NORMAL,     COLOR_BTN_HOVER,         19)
	_style_button(btn_load,      COLOR_BTN_NORMAL,     COLOR_BTN_HOVER,         19)
	_style_button(btn_settings,  COLOR_BTN_NORMAL,     COLOR_BTN_HOVER,         19)
	_style_button(btn_main_menu, COLOR_BTN_DANGER,     COLOR_BTN_DANGER_HOVER,  19)
	_style_button(btn_quit,      COLOR_BTN_DANGER,     COLOR_BTN_DANGER_HOVER,  19)


func _style_button(btn: Button, normal: Color, hover: Color, font_size: int) -> void:
	var style := StyleBoxFlat.new()
	style.bg_color                     = normal
	style.corner_radius_top_left       = 8
	style.corner_radius_top_right      = 8
	style.corner_radius_bottom_left    = 8
	style.corner_radius_bottom_right   = 8
	style.content_margin_left          = 36
	style.content_margin_right         = 36
	style.content_margin_top           = 10
	style.content_margin_bottom        = 10
	btn.add_theme_stylebox_override("normal", style)
	var hover_style := style.duplicate() as StyleBoxFlat
	hover_style.bg_color = hover
	btn.add_theme_stylebox_override("hover", hover_style)
	btn.add_theme_color_override("font_color", Color(0.95, 0.98, 1.0))
	btn.add_theme_font_size_override("font_size", font_size)

# ── Signal connections ─────────────────────────────────────────────────────────

func _connect_buttons() -> void:
	btn_resume.pressed.connect(_on_resume_pressed)
	btn_save.pressed.connect(_on_save_pressed)
	btn_load.pressed.connect(_on_load_pressed)
	btn_settings.pressed.connect(_on_settings_pressed)
	btn_main_menu.pressed.connect(_on_main_menu_pressed)
	btn_quit.pressed.connect(_on_quit_pressed)

# ── Submenu setup ─────────────────────────────────────────────────────────────

func _setup_submenus() -> void:
	if settings_menu:
		settings_menu.hide()
		if settings_menu.has_signal("close_requested"):
			settings_menu.close_requested.connect(_on_submenu_closed)
	if save_load_menu:
		save_load_menu.hide()
		if save_load_menu.has_signal("close_requested"):
			save_load_menu.close_requested.connect(_on_submenu_closed)


func _set_buttons_disabled(disabled: bool) -> void:
	for btn: Button in [btn_resume, btn_save, btn_load, btn_settings, btn_main_menu, btn_quit]:
		btn.disabled = disabled

# ── Button handlers ───────────────────────────────────────────────────────────

func _on_resume_pressed() -> void:
	resume_requested.emit()
	hide_menu()


func _on_save_pressed() -> void:
	save_requested.emit()
	btn_save.text     = tr("ui.pause.saving")
	btn_save.disabled = true
	if _save_manager and _save_manager.has_method("save_game"):
		_save_manager.save_game("quicksave")
	await get_tree().create_timer(0.5).timeout
	btn_save.text = tr("ui.pause.saved")
	await get_tree().create_timer(1.2).timeout
	btn_save.text     = tr("ui.pause.save")
	btn_save.disabled = false


func _on_load_pressed() -> void:
	if save_load_menu:
		save_load_menu.show()
		_set_buttons_disabled(true)
	else:
		push_warning("PauseMenu: SaveLoadMenu node not found")


func _on_settings_pressed() -> void:
	settings_requested.emit()
	if settings_menu:
		settings_menu.show()
		_set_buttons_disabled(true)
	else:
		push_warning("PauseMenu: SettingsMenu node not found")


func _on_main_menu_pressed() -> void:
	quit_to_menu_requested.emit()
	_pause_simulation(false)
	var tween := create_tween()
	tween.tween_property(self, "modulate:a", 0.0, 0.4)
	tween.tween_callback(func():
		hide()
		get_tree().change_scene_to_file("res://scenes/MainMenu.tscn")
	)


func _on_quit_pressed() -> void:
	_pause_simulation(false)
	var tween := create_tween()
	tween.tween_property(self, "modulate:a", 0.0, 0.4)
	tween.tween_callback(get_tree().quit)


func _on_submenu_closed() -> void:
	_set_buttons_disabled(false)

# ── Accessibility ──────────────────────────────────────────────────────────────

func _setup_tooltips() -> void:
	btn_resume.tooltip_text = "Fortsett spillet (ESC)"
	btn_save.tooltip_text = "Lagre spillprogresjonen din"
	btn_load.tooltip_text = "Last inn et tidligere lagret spill"
	btn_settings.tooltip_text = "Endre innstillinger for lyd, grafikk og tilgjengelighet"
	btn_main_menu.tooltip_text = "Gå tilbake til hovedmenyen (usaved progress will be lost)"
	btn_quit.tooltip_text = "Avslutt spillet (usaved progress will be lost)"


func _setup_focus_navigation() -> void:
	# Enable keyboard focus for all buttons
	btn_resume.focus_mode = Control.FOCUS_ALL
	btn_save.focus_mode = Control.FOCUS_ALL
	btn_load.focus_mode = Control.FOCUS_ALL
	btn_settings.focus_mode = Control.FOCUS_ALL
	btn_main_menu.focus_mode = Control.FOCUS_ALL
	btn_quit.focus_mode = Control.FOCUS_ALL
	
	# Set up vertical navigation flow
	btn_resume.focus_neighbor_bottom = NodePath("../BtnSave")
	btn_save.focus_neighbor_top = NodePath("../BtnResume")
	btn_save.focus_neighbor_bottom = NodePath("../BtnLoad")
	btn_load.focus_neighbor_top = NodePath("../BtnSave")
	btn_load.focus_neighbor_bottom = NodePath("../BtnSettings")
	btn_settings.focus_neighbor_top = NodePath("../BtnLoad")
	btn_settings.focus_neighbor_bottom = NodePath("../BtnMainMenu")
	btn_main_menu.focus_neighbor_top = NodePath("../BtnSettings")
	btn_main_menu.focus_neighbor_bottom = NodePath("../BtnQuit")
	btn_quit.focus_neighbor_top = NodePath("../BtnMainMenu")
	btn_quit.focus_neighbor_bottom = NodePath("../BtnResume")  # Loop back
