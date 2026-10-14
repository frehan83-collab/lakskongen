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

const COLOR_OVERLAY        := Color(0.043137, 0.164706, 0.227451, 0.6)  # Ink scrim

# ── Node references ────────────────────────────────────────────────────────────

@onready var overlay:       ColorRect      = $Overlay
@onready var panel:         PanelContainer = $PanelContainer
@onready var title_label:   Label          = $PanelContainer/VBoxContainer/PausedLabel
@onready var btn_resume:    Button         = $PanelContainer/VBoxContainer/ResumeButton
@onready var btn_save:      Button         = $PanelContainer/VBoxContainer/SaveButton
@onready var btn_load:      Button         = get_node_or_null("PanelContainer/VBoxContainer/LoadButton")
@onready var btn_settings:  Button         = $PanelContainer/VBoxContainer/SettingsButton
@onready var btn_main_menu: Button         = $PanelContainer/VBoxContainer/QuitButton
@onready var btn_quit:      Button         = get_node_or_null("PanelContainer/VBoxContainer/ExitButton")

@onready var settings_menu:  Control = get_node_or_null("SettingsMenu")
@onready var save_load_menu: Control = get_node_or_null("/root/Main/SaveLoadMenu")

# ── State ──────────────────────────────────────────────────────────────────────

var _sim: Node          = null
var _game_bridge: Node  = null

# ── Lifecycle ──────────────────────────────────────────────────────────────────

func _ready() -> void:
	_sim          = get_node_or_null("/root/SimBridge")
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
	if _game_bridge and _game_bridge.has_method("SetPaused"):
		_game_bridge.SetPaused(paused)

# ── Visual setup ──────────────────────────────────────────────────────────────

func _setup_visuals() -> void:
	if overlay:
		overlay.color = COLOR_OVERLAY

	# An Ink dialog (theme variations, roadmap D13): the primary action in
	# salmon, the rest neutral, the exits as danger.
	if panel:
		panel.theme_type_variation = &"Dialog"

	if title_label:
		title_label.theme_type_variation = &"Title"

	for btn in [btn_resume]:
		if btn != null:
			btn.theme_type_variation = &"LargePrimaryButton"
	for btn in [btn_save, btn_load, btn_settings]:
		if btn != null:
			btn.theme_type_variation = &"LargeButton"
	for btn in [btn_main_menu, btn_quit]:
		if btn != null:
			btn.theme_type_variation = &"LargeDangerButton"

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
		if save_load_menu.has_signal("menu_closed"):
			save_load_menu.menu_closed.connect(_on_submenu_closed)
		elif save_load_menu.has_signal("close_requested"):
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
	btn_save.text     = tr("UI_PAUSE_SAVING")
	btn_save.disabled = true
	# Through the SimBridge facade (C# SaveLoadManager is PascalCase-only, F928);
	# report what actually happened instead of an unconditional "saved".
	var ok: bool = _sim != null and _sim.save_game("quicksave")
	await get_tree().create_timer(0.5).timeout
	if ok:
		btn_save.text = tr("UI_PAUSE_SAVED")
	else:
		var failed := tr("UI_PAUSE_SAVE_FAILED")
		btn_save.text = failed if failed != "UI_PAUSE_SAVE_FAILED" else "⚠️ Lagring feilet"
	await get_tree().create_timer(1.2).timeout
	btn_save.text     = tr("UI_PAUSE_SAVE")
	btn_save.disabled = false


func _on_load_pressed() -> void:
	if save_load_menu:
		if save_load_menu.has_method("show_load"):
			save_load_menu.show_load()
		else:
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
	if btn_resume:
		btn_resume.tooltip_text = "Fortsett spillet (ESC)"
	if btn_save:
		btn_save.tooltip_text = "Lagre spillprogresjonen din"
	if btn_load:
		btn_load.tooltip_text = "Last inn et tidligere lagret spill"
	if btn_settings:
		btn_settings.tooltip_text = "Endre innstillinger for lyd, grafikk og tilgjengelighet"
	if btn_main_menu:
		btn_main_menu.tooltip_text = "Gå tilbake til hovedmenyen (usaved progress will be lost)"
	if btn_quit:
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
