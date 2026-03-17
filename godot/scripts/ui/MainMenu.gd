extends Control

## MainMenu — Premium main menu for Lakskongen
## 
## Features:
## - Large "LAKSKONGEN" title in fjord teal (#2d8a7e)
## - Subtitle "Norsk Lakseoppdrett Simulator" in muted blue-gray (#8899aa)
## - Version text "v0.1.0 Alpha" in bottom-right corner
## - 4 centered buttons (300px wide, 15px gap)
## - Dark ocean background (#0d1b2a)
## - Animated wave effect at bottom (sine wave opacity tween)
## - Button hover with scale animation (1.05x)
##
## Expected node structure (all created programmatically):
##   MainMenu (Control, full-screen)

# ── Constants ──────────────────────────────────────────────────────────────────

const BG_COLOR := Color("#0d1b2a")           # Dark ocean
const TITLE_COLOR := Color("#2d8a7e")        # Fjord teal
const SUBTITLE_COLOR := Color("#8899aa")     # Muted blue-gray
const VERSION_COLOR := Color("#8899aa")      # Muted blue-gray
const TEXT_COLOR := Color("#e8e8e8")         # Clean white
const ACCENT_PRIMARY := Color("#2d8a7e")     # Teal accent
const ACCENT_HOVER := Color("#45b5aa")       # Light teal
const BG_MEDIUM := Color("#1a2744")          # Button background

const TITLE_FONT_SIZE := 64
const SUBTITLE_FONT_SIZE := 24
const VERSION_FONT_SIZE := 14
const BUTTON_FONT_SIZE := 18

const BUTTON_WIDTH := 300
const BUTTON_HEIGHT := 50
const BUTTON_GAP := 15

const VERSION_TEXT := "v0.1.0 Alpha"

# ── Node references ────────────────────────────────────────────────────────────

var background: ColorRect
var wave_effect: ColorRect
var title_label: Label
var subtitle_label: Label
var version_label: Label
var button_container: VBoxContainer

var btn_new_game: Button
var btn_load: Button
var btn_settings: Button
var btn_quit: Button

var wave_tween: Tween

# ── Signals ────────────────────────────────────────────────────────────────────

signal new_game_requested
signal load_game_requested
signal settings_requested

# ── Lifecycle ──────────────────────────────────────────────────────────────────

func _ready() -> void:
	# Set full-screen anchors
	set_anchors_preset(Control.PRESET_FULL_RECT)
	
	_create_background()
	_create_wave_effect()
	_create_title()
	_create_subtitle()
	_create_version_label()
	_create_buttons()
	_start_wave_animation()
	
	# Fade in animation
	modulate.a = 0.0
	var fade_tween := create_tween()
	fade_tween.tween_property(self, "modulate:a", 1.0, 0.8).set_ease(Tween.EASE_OUT)

# ── Background setup ───────────────────────────────────────────────────────────

func _create_background() -> void:
	background = ColorRect.new()
	background.color = BG_COLOR
	background.set_anchors_preset(Control.PRESET_FULL_RECT)
	background.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(background)

func _create_wave_effect() -> void:
	wave_effect = ColorRect.new()
	wave_effect.color = Color(ACCENT_PRIMARY.r, ACCENT_PRIMARY.g, ACCENT_PRIMARY.b, 0.0)
	wave_effect.mouse_filter = Control.MOUSE_FILTER_IGNORE
	
	# Position at bottom of screen, 150px tall
	wave_effect.anchor_left = 0.0
	wave_effect.anchor_right = 1.0
	wave_effect.anchor_top = 1.0
	wave_effect.anchor_bottom = 1.0
	wave_effect.offset_top = -150
	wave_effect.offset_bottom = 0
	
	add_child(wave_effect)

func _start_wave_animation() -> void:
	if wave_tween:
		wave_tween.kill()
	
	wave_tween = create_tween().set_loops()
	wave_tween.tween_property(wave_effect, "color:a", 0.15, 2.5).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
	wave_tween.tween_property(wave_effect, "color:a", 0.05, 2.5).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)

# ── Title and text setup ───────────────────────────────────────────────────────

func _create_title() -> void:
	title_label = Label.new()
	title_label.text = "LAKSKONGEN"
	title_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	title_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	title_label.add_theme_font_size_override("font_size", TITLE_FONT_SIZE)
	title_label.add_theme_color_override("font_color", TITLE_COLOR)
	
	# Position at top center
	title_label.anchor_left = 0.5
	title_label.anchor_right = 0.5
	title_label.anchor_top = 0.0
	title_label.anchor_bottom = 0.0
	title_label.offset_left = -300
	title_label.offset_right = 300
	title_label.offset_top = 80
	title_label.offset_bottom = 80 + TITLE_FONT_SIZE + 20
	
	add_child(title_label)

func _create_subtitle() -> void:
	subtitle_label = Label.new()
	subtitle_label.text = "Norsk Lakseoppdrett Simulator"
	subtitle_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	subtitle_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	subtitle_label.add_theme_font_size_override("font_size", SUBTITLE_FONT_SIZE)
	subtitle_label.add_theme_color_override("font_color", SUBTITLE_COLOR)
	
	# Position below title
	subtitle_label.anchor_left = 0.5
	subtitle_label.anchor_right = 0.5
	subtitle_label.anchor_top = 0.0
	subtitle_label.anchor_bottom = 0.0
	subtitle_label.offset_left = -300
	subtitle_label.offset_right = 300
	subtitle_label.offset_top = 80 + TITLE_FONT_SIZE + 30
	subtitle_label.offset_bottom = 80 + TITLE_FONT_SIZE + 30 + SUBTITLE_FONT_SIZE + 10
	
	add_child(subtitle_label)

func _create_version_label() -> void:
	version_label = Label.new()
	version_label.text = VERSION_TEXT
	version_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	version_label.add_theme_font_size_override("font_size", VERSION_FONT_SIZE)
	version_label.add_theme_color_override("font_color", VERSION_COLOR)
	
	# Position at bottom-right corner
	version_label.anchor_left = 1.0
	version_label.anchor_right = 1.0
	version_label.anchor_top = 1.0
	version_label.anchor_bottom = 1.0
	version_label.offset_left = -150
	version_label.offset_right = -20
	version_label.offset_top = -40
	version_label.offset_bottom = -20
	
	add_child(version_label)

# ── Button setup ───────────────────────────────────────────────────────────────

func _create_buttons() -> void:
	# Create container for buttons (centered vertically)
	button_container = VBoxContainer.new()
	button_container.alignment = BoxContainer.ALIGNMENT_CENTER
	button_container.add_theme_constant_override("separation", BUTTON_GAP)
	
	# Center the container
	button_container.anchor_left = 0.5
	button_container.anchor_right = 0.5
	button_container.anchor_top = 0.5
	button_container.anchor_bottom = 0.5
	button_container.offset_left = -BUTTON_WIDTH / 2
	button_container.offset_right = BUTTON_WIDTH / 2
	button_container.offset_top = -((BUTTON_HEIGHT * 4 + BUTTON_GAP * 3) / 2)
	button_container.offset_bottom = (BUTTON_HEIGHT * 4 + BUTTON_GAP * 3) / 2
	
	add_child(button_container)
	
	# Create buttons
	btn_new_game = _create_button("Nytt Spill", true)
	btn_load = _create_button("Last Spill", false)
	btn_settings = _create_button("Innstillinger", false)
	btn_quit = _create_button("Avslutt", false)
	
	button_container.add_child(btn_new_game)
	button_container.add_child(btn_load)
	button_container.add_child(btn_settings)
	button_container.add_child(btn_quit)
	
	# Connect signals
	btn_new_game.pressed.connect(_on_new_game_pressed)
	btn_load.pressed.connect(_on_load_pressed)
	btn_settings.pressed.connect(_on_settings_pressed)
	btn_quit.pressed.connect(_on_quit_pressed)

func _create_button(button_text: String, is_primary: bool = false) -> Button:
	var btn := Button.new()
	btn.text = button_text
	btn.custom_minimum_size = Vector2(BUTTON_WIDTH, BUTTON_HEIGHT)
	btn.add_theme_font_size_override("font_size", BUTTON_FONT_SIZE)
	btn.add_theme_color_override("font_color", TEXT_COLOR)
	
	# Create custom style
	var style_normal := StyleBoxFlat.new()
	style_normal.bg_color = ACCENT_PRIMARY if is_primary else BG_MEDIUM
	style_normal.set_corner_radius_all(8)
	style_normal.set_border_width_all(2)
	style_normal.border_color = ACCENT_PRIMARY
	style_normal.set_content_margin_all(12)
	
	var style_hover := StyleBoxFlat.new()
	style_hover.bg_color = ACCENT_HOVER
	style_hover.set_corner_radius_all(8)
	style_hover.set_border_width_all(2)
	style_hover.border_color = ACCENT_HOVER
	style_hover.set_content_margin_all(12)
	
	var style_pressed := StyleBoxFlat.new()
	style_pressed.bg_color = ACCENT_PRIMARY.darkened(0.2)
	style_pressed.set_corner_radius_all(8)
	style_pressed.set_border_width_all(2)
	style_pressed.border_color = ACCENT_PRIMARY
	style_pressed.set_content_margin_all(12)
	
	btn.add_theme_stylebox_override("normal", style_normal)
	btn.add_theme_stylebox_override("hover", style_hover)
	btn.add_theme_stylebox_override("pressed", style_pressed)
	
	# Add hover animation
	btn.mouse_entered.connect(_on_button_hover.bind(btn, true))
	btn.mouse_exited.connect(_on_button_hover.bind(btn, false))
	
	btn.focus_mode = Control.FOCUS_ALL
	
	return btn

func _on_button_hover(button: Button, is_hovering: bool) -> void:
	var target_scale := Vector2(1.05, 1.05) if is_hovering else Vector2(1.0, 1.0)
	
	var tween := create_tween()
	tween.tween_property(button, "scale", target_scale, 0.15).set_trans(Tween.TRANS_BACK).set_ease(Tween.EASE_OUT)

# ── Button handlers ────────────────────────────────────────────────────────────

func _on_new_game_pressed() -> void:
	_disable_all_buttons()
	emit_signal("new_game_requested")
	
	# Fade out and change scene
	var tween := create_tween()
	tween.tween_property(self, "modulate:a", 0.0, 0.5)
	tween.tween_callback(_change_to_game_scene)

func _on_load_pressed() -> void:
	emit_signal("load_game_requested")
	# You could show a save/load panel here

func _on_settings_pressed() -> void:
	emit_signal("settings_requested")
	# You could show a settings panel here

func _on_quit_pressed() -> void:
	_disable_all_buttons()
	
	var tween := create_tween()
	tween.tween_property(self, "modulate:a", 0.0, 0.4)
	tween.tween_callback(get_tree().quit)

func _disable_all_buttons() -> void:
	btn_new_game.disabled = true
	btn_load.disabled = true
	btn_settings.disabled = true
	btn_quit.disabled = true

func _change_to_game_scene() -> void:
	# Change to main game scene
	get_tree().change_scene_to_file("res://scenes/Main.tscn")
