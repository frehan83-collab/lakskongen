extends Control

## MainMenu v2.0 — Nordic Minimalist
## Light background with teal branding, elegant centered layout.
## Wave animation at bottom, smooth transitions.

const BG_COLOR      := Color("#ECEFF4")
const PRIMARY       := Color("#0A4D68")
const PRIMARY_LIGHT := Color("#0D5F80")
const SECONDARY     := Color("#88C0D0")
const TEXT_MAIN     := Color("#2E3440")
const TEXT_MUTED    := Color("#7B8794")
const SURFACE       := Color("#FFFFFF")
const BORDER        := Color("#D8DEE9")

const TITLE_SIZE    := 56
const SUBTITLE_SIZE := 20
const BUTTON_SIZE   := 16
const BTN_W         := 320
const BTN_H         := 52
const BTN_GAP       := 12
const VERSION_TEXT  := "v0.2.0 Alpha — Nordic Minimalist"

const SCENARIO_PANEL_SCRIPT = preload("res://scripts/ui/ScenarioSelectPanel.gd")

var background: ColorRect
var wave_effect: ColorRect
var title_label: Label
var subtitle_label: Label
var version_label: Label
var button_container: VBoxContainer
var fish_icon: Label

var btn_new_game: Button
var btn_load: Button
var btn_settings: Button
var btn_quit: Button

var wave_tween: Tween
var scenario_panel: Control = null

signal new_game_requested
signal load_game_requested
signal settings_requested

func _ready() -> void:
	set_anchors_preset(PRESET_FULL_RECT)
	_build_ui()
	
	# Fade in
	modulate.a = 0.0
	var fade := create_tween()
	fade.tween_property(self, "modulate:a", 1.0, 0.8).set_ease(Tween.EASE_OUT)

func _build_ui() -> void:
	# Background
	background = ColorRect.new()
	background.color = BG_COLOR
	background.set_anchors_preset(PRESET_FULL_RECT)
	background.mouse_filter = MOUSE_FILTER_IGNORE
	add_child(background)
	
	# Wave effect at bottom (primary color, subtle)
	wave_effect = ColorRect.new()
	wave_effect.color = Color(SECONDARY.r, SECONDARY.g, SECONDARY.b, 0.0)
	wave_effect.mouse_filter = MOUSE_FILTER_IGNORE
	wave_effect.anchor_left = 0.0
	wave_effect.anchor_right = 1.0
	wave_effect.anchor_top = 1.0
	wave_effect.anchor_bottom = 1.0
	wave_effect.offset_top = -200
	add_child(wave_effect)
	_start_wave_animation()
	
	# Fish icon (large, centered above title)
	fish_icon = Label.new()
	fish_icon.text = "🐟"
	fish_icon.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	fish_icon.add_theme_font_size_override("font_size", 48)
	fish_icon.anchor_left = 0.5
	fish_icon.anchor_right = 0.5
	fish_icon.offset_left = -50
	fish_icon.offset_right = 50
	fish_icon.offset_top = 80
	fish_icon.offset_bottom = 140
	add_child(fish_icon)
	
	# Title
	title_label = Label.new()
	title_label.text = tr("UI_MAINMENU_TITLE") if tr("UI_MAINMENU_TITLE") != "ui.mainmenu.title" else "LAKSKONGEN"
	title_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	title_label.add_theme_font_size_override("font_size", TITLE_SIZE)
	title_label.add_theme_color_override("font_color", PRIMARY)
	var theme_node = get_node_or_null("/root/LakskongenTheme")
	if theme_node and theme_node.font_heading_bold:
		title_label.add_theme_font_override("font", theme_node.font_heading_bold)
	title_label.anchor_left = 0.5
	title_label.anchor_right = 0.5
	title_label.offset_left = -400
	title_label.offset_right = 400
	title_label.offset_top = 150
	title_label.offset_bottom = 150 + TITLE_SIZE + 20
	add_child(title_label)
	
	# Subtitle
	subtitle_label = Label.new()
	subtitle_label.text = tr("UI_MAINMENU_SUBTITLE") if tr("UI_MAINMENU_SUBTITLE") != "ui.mainmenu.subtitle" else "Norsk Lakseoppdrett Simulator"
	subtitle_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	subtitle_label.add_theme_font_size_override("font_size", SUBTITLE_SIZE)
	subtitle_label.add_theme_color_override("font_color", TEXT_MUTED)
	subtitle_label.anchor_left = 0.5
	subtitle_label.anchor_right = 0.5
	subtitle_label.offset_left = -300
	subtitle_label.offset_right = 300
	subtitle_label.offset_top = 150 + TITLE_SIZE + 25
	subtitle_label.offset_bottom = 150 + TITLE_SIZE + 25 + SUBTITLE_SIZE + 10
	add_child(subtitle_label)
	
	# Buttons
	button_container = VBoxContainer.new()
	button_container.alignment = BoxContainer.ALIGNMENT_CENTER
	button_container.add_theme_constant_override("separation", BTN_GAP)
	button_container.anchor_left = 0.5
	button_container.anchor_right = 0.5
	button_container.anchor_top = 0.5
	button_container.anchor_bottom = 0.5
	button_container.offset_left = -BTN_W / 2
	button_container.offset_right = BTN_W / 2
	var total_h = BTN_H * 4 + BTN_GAP * 3
	button_container.offset_top = -(total_h / 2) + 40
	button_container.offset_bottom = (total_h / 2) + 40
	add_child(button_container)
	
	btn_new_game = _make_btn("🐟  Nytt Spill", true)
	btn_load = _make_btn("💾  Last Spill", false)
	btn_settings = _make_btn("⚙️  Innstillinger", false)
	btn_quit = _make_btn("🚪  Avslutt", false)
	
	button_container.add_child(btn_new_game)
	button_container.add_child(btn_load)
	button_container.add_child(btn_settings)
	button_container.add_child(btn_quit)
	
	btn_new_game.pressed.connect(_on_new_game_pressed)
	btn_load.pressed.connect(_on_load_pressed)
	btn_settings.pressed.connect(_on_settings_pressed)
	btn_quit.pressed.connect(_on_quit_pressed)
	
	# Version
	version_label = Label.new()
	version_label.text = VERSION_TEXT
	version_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	version_label.add_theme_font_size_override("font_size", 12)
	version_label.add_theme_color_override("font_color", TEXT_MUTED)
	version_label.anchor_left = 1.0
	version_label.anchor_right = 1.0
	version_label.anchor_top = 1.0
	version_label.anchor_bottom = 1.0
	version_label.offset_left = -250
	version_label.offset_right = -20
	version_label.offset_top = -35
	version_label.offset_bottom = -15
	add_child(version_label)

func _make_btn(text: String, is_primary: bool) -> Button:
	var btn := Button.new()
	btn.text = text
	btn.custom_minimum_size = Vector2(BTN_W, BTN_H)
	btn.add_theme_font_size_override("font_size", BUTTON_SIZE)
	btn.focus_mode = Control.FOCUS_ALL
	
	if is_primary:
		var sb_n := StyleBoxFlat.new()
		sb_n.bg_color = PRIMARY
		sb_n.set_corner_radius_all(8)
		sb_n.set_content_margin_all(12)
		btn.add_theme_stylebox_override("normal", sb_n)
		var sb_h := StyleBoxFlat.new()
		sb_h.bg_color = PRIMARY_LIGHT
		sb_h.set_corner_radius_all(8)
		sb_h.set_content_margin_all(12)
		btn.add_theme_stylebox_override("hover", sb_h)
		btn.add_theme_color_override("font_color", Color.WHITE)
		btn.add_theme_color_override("font_hover_color", Color.WHITE)
	else:
		var sb_n := StyleBoxFlat.new()
		sb_n.bg_color = SURFACE
		sb_n.border_color = BORDER
		sb_n.set_border_width_all(1)
		sb_n.set_corner_radius_all(8)
		sb_n.set_content_margin_all(12)
		btn.add_theme_stylebox_override("normal", sb_n)
		var sb_h := StyleBoxFlat.new()
		sb_h.bg_color = Color("#F8F9FB")
		sb_h.border_color = PRIMARY
		sb_h.set_border_width_all(1)
		sb_h.set_corner_radius_all(8)
		sb_h.set_content_margin_all(12)
		btn.add_theme_stylebox_override("hover", sb_h)
		btn.add_theme_color_override("font_color", TEXT_MAIN)
		btn.add_theme_color_override("font_hover_color", PRIMARY)
	
	btn.mouse_entered.connect(_on_btn_hover.bind(btn, true))
	btn.mouse_exited.connect(_on_btn_hover.bind(btn, false))
	return btn

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

func _show_scenario_panel() -> void:
	if scenario_panel != null:
		scenario_panel.queue_free()
	scenario_panel = SCENARIO_PANEL_SCRIPT.new()
	add_child(scenario_panel)
	if scenario_panel.has_signal("scenario_selected"):
		scenario_panel.scenario_selected.connect(_on_scenario_selected)
	if scenario_panel.has_signal("panel_close_requested"):
		scenario_panel.panel_close_requested.connect(_on_scenario_close)
	button_container.visible = false
	title_label.visible = false
	subtitle_label.visible = false
	fish_icon.visible = false

func _on_scenario_selected(scenario_id: String) -> void:
	var sim = get_node_or_null("/root/SimBridge")
	if sim:
		sim.start_new_game(scenario_id)
		get_tree().change_scene_to_file("res://scenes/GameScene.tscn")

func _on_scenario_close() -> void:
	if scenario_panel:
		scenario_panel.queue_free()
		scenario_panel = null
	button_container.visible = true
	title_label.visible = true
	subtitle_label.visible = true
	fish_icon.visible = true

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
