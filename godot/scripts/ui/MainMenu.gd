extends Control

## MainMenu — title screen for Lakskongen.
## Buttons: Nytt Spill, Last inn, Innstillinger, Kreditter, Avslutt.
##
## Salmon-themed background is produced procedurally:
##   • Deep ocean gradient via two stacked ColorRects
##   • A slow "light shimmer" tween cycling on a semi-transparent ColorRect
##   • Drifting salmon silhouette labels (emoji) animated in _process
##
## Submenus (SettingsMenu, CreditsScreen, SaveLoadMenu) are expected as
## sibling nodes; they are shown/hidden via signals.
##
## Expected node layout:
##   MainMenu (Control, full-screen)
##   ├─ BgDeep      (ColorRect, full-screen, dark navy)
##   ├─ BgMid       (ColorRect, full-screen, ocean blue, low alpha)
##   ├─ Shimmer     (ColorRect, full-screen, white, alpha 0)  ← animated
##   ├─ SalmonLayer (Control, full-screen)  ← fish sprites appended here
##   ├─ CenterContainer
##   │  └─ VBoxContainer
##   │     ├─ TitleLabel   (Label) "🐟 LAKSKONGEN"
##   │     ├─ SubtitleLabel (Label) "Norsk Lakseoppdrett Simulator"
##   │     ├─ Spacer       (Control, min size 32)
##   │     ├─ BtnNewGame   (Button) "Nytt Spill"
##   │     ├─ BtnLoadGame  (Button) "Last inn spill"
##   │     ├─ BtnSettings  (Button) "Innstillinger"
##   │     ├─ BtnCredits   (Button) "Kreditter"
##   │     └─ BtnQuit      (Button) "Avslutt"
##   ├─ VersionLabel (Label, anchored bottom-right) "Lakskongen v0.1.0-alpha"
##   ├─ SettingsMenu (Control) — shown on demand
##   ├─ CreditsScreen (Control) — shown on demand
##   └─ SaveLoadMenu (Control) — shown on demand

# ── Color palette ─────────────────────────────────────────────────────────────

const COLOR_OCEAN_DEEP := Color(0.04, 0.10, 0.22)
const COLOR_OCEAN_MID  := Color(0.06, 0.20, 0.40, 0.60)
const COLOR_TITLE      := Color(0.85, 0.95, 1.00)
const COLOR_SUBTITLE   := Color(0.60, 0.80, 0.90)
const COLOR_BTN_NORMAL := Color(0.10, 0.30, 0.55)
const COLOR_BTN_HOVER  := Color(0.15, 0.45, 0.75)

const SALMON_EMOJIS := ["🐟", "🐠", "🐡", "🦈"]
const FISH_COUNT    := 8
const VERSION       := "Lakskongen v0.1.0-alpha"

# ── Node references ────────────────────────────────────────────────────────────

@onready var bg_deep:      ColorRect  = $BgDeep
@onready var bg_mid:       ColorRect  = $BgMid
@onready var shimmer:      ColorRect  = $Shimmer
@onready var salmon_layer: Control    = $SalmonLayer

@onready var title_label:    Label  = $CenterContainer/VBoxContainer/TitleLabel
@onready var subtitle_label: Label  = $CenterContainer/VBoxContainer/SubtitleLabel

@onready var btn_new_game: Button = $CenterContainer/VBoxContainer/BtnNewGame
@onready var btn_load:     Button = $CenterContainer/VBoxContainer/BtnLoadGame
@onready var btn_settings: Button = $CenterContainer/VBoxContainer/BtnSettings
@onready var btn_credits:  Button = $CenterContainer/VBoxContainer/BtnCredits
@onready var btn_quit:     Button = $CenterContainer/VBoxContainer/BtnQuit

@onready var version_label: Label = $VersionLabel

@onready var settings_menu:  Control = $SettingsMenu
@onready var credits_screen: Control = $CreditsScreen
@onready var save_load_menu: Control = $SaveLoadMenu

# ── State ──────────────────────────────────────────────────────────────────────

var _fish_nodes: Array[Label] = []
var _fish_velocity: Array[Vector2] = []
var _save_manager: Node = null
var _game_bridge: Node = null
var _shimmer_tween: Tween = null

# ── Lifecycle ──────────────────────────────────────────────────────────────────

func _ready() -> void:
	_save_manager = get_node_or_null("/root/SaveLoadManager")
	_game_bridge  = get_node_or_null("/root/GameBridge")

	_setup_background()
	_setup_buttons()
	_setup_submenus()
	_start_shimmer_loop()
	_spawn_fish()

	version_label.text = VERSION

	# Apply saved settings on startup
	if settings_menu and settings_menu.has_method("load_and_apply"):
		settings_menu.load_and_apply()

	# Entrance animation: fade in the whole menu
	modulate = Color(1, 1, 1, 0)
	var tween := create_tween()
	tween.tween_property(self, "modulate:a", 1.0, 0.8)


func _process(delta: float) -> void:
	_animate_fish(delta)

# ── Background setup ──────────────────────────────────────────────────────────

func _setup_background() -> void:
	bg_deep.color = COLOR_OCEAN_DEEP
	bg_mid.color  = COLOR_OCEAN_MID
	shimmer.color = Color(1, 1, 1, 0)


func _start_shimmer_loop() -> void:
	_shimmer_tween = create_tween().set_loops()
	_shimmer_tween.tween_property(shimmer, "color:a", 0.06, 3.0).set_trans(Tween.TRANS_SINE)
	_shimmer_tween.tween_property(shimmer, "color:a", 0.0,  4.0).set_trans(Tween.TRANS_SINE)
	_shimmer_tween.tween_interval(1.5)

# ── Fish animation ────────────────────────────────────────────────────────────

func _spawn_fish() -> void:
	var viewport_size := get_viewport_rect().size
	var rng := RandomNumberGenerator.new()
	rng.randomize()

	for i in FISH_COUNT:
		var fish := Label.new()
		fish.text = SALMON_EMOJIS[rng.randi() % SALMON_EMOJIS.size()]
		fish.add_theme_font_size_override("font_size", rng.randi_range(24, 52))
		fish.modulate = Color(1, 1, 1, rng.randf_range(0.25, 0.55))
		fish.position = Vector2(
			rng.randf_range(0, viewport_size.x),
			rng.randf_range(0, viewport_size.y)
		)
		salmon_layer.add_child(fish)
		_fish_nodes.append(fish)
		# All fish drift slowly leftward with slight vertical wander
		_fish_velocity.append(Vector2(
			rng.randf_range(-18.0, -8.0),
			rng.randf_range(-4.0,  4.0)
		))


func _animate_fish(delta: float) -> void:
	var viewport_size := get_viewport_rect().size
	for i in _fish_nodes.size():
		var fish := _fish_nodes[i]
		fish.position += _fish_velocity[i] * delta
		# Wrap around edges
		if fish.position.x < -64:
			fish.position.x = viewport_size.x + 32
		if fish.position.y < -64:
			fish.position.y = viewport_size.y + 32
		if fish.position.y > viewport_size.y + 64:
			fish.position.y = -32

# ── Button setup ──────────────────────────────────────────────────────────────

func _setup_buttons() -> void:
	btn_new_game.pressed.connect(_on_new_game_pressed)
	btn_load.pressed.connect(_on_load_pressed)
	btn_settings.pressed.connect(_on_settings_pressed)
	btn_credits.pressed.connect(_on_credits_pressed)
	btn_quit.pressed.connect(_on_quit_pressed)

	# Style each button
	for btn: Button in [btn_new_game, btn_load, btn_settings, btn_credits, btn_quit]:
		var style := StyleBoxFlat.new()
		style.bg_color = COLOR_BTN_NORMAL
		style.corner_radius_top_left     = 8
		style.corner_radius_top_right    = 8
		style.corner_radius_bottom_left  = 8
		style.corner_radius_bottom_right = 8
		style.content_margin_left   = 40
		style.content_margin_right  = 40
		style.content_margin_top    = 12
		style.content_margin_bottom = 12
		btn.add_theme_stylebox_override("normal", style)
		var hover_style := style.duplicate() as StyleBoxFlat
		hover_style.bg_color = COLOR_BTN_HOVER
		btn.add_theme_stylebox_override("hover", hover_style)
		btn.add_theme_color_override("font_color", Color(0.95, 0.98, 1.0))
		btn.add_theme_font_size_override("font_size", 20)

# ── Submenu setup ─────────────────────────────────────────────────────────────

func _setup_submenus() -> void:
	if settings_menu:
		settings_menu.hide()
		if settings_menu.has_signal("close_requested"):
			settings_menu.close_requested.connect(_on_submenu_closed)

	if credits_screen:
		credits_screen.hide()
		if credits_screen.has_signal("close_requested"):
			credits_screen.close_requested.connect(_on_submenu_closed)

	if save_load_menu:
		save_load_menu.hide()
		if save_load_menu.has_signal("close_requested"):
			save_load_menu.close_requested.connect(_on_submenu_closed)


func _set_main_buttons_disabled(disabled: bool) -> void:
	btn_new_game.disabled = disabled
	btn_load.disabled     = disabled
	btn_settings.disabled = disabled
	btn_credits.disabled  = disabled
	btn_quit.disabled     = disabled

# ── Button handlers ───────────────────────────────────────────────────────────

func _on_new_game_pressed() -> void:
	if _game_bridge and _game_bridge.has_method("start_new_game"):
		_game_bridge.start_new_game()
	_transition_to_game()


func _on_load_pressed() -> void:
	if save_load_menu:
		save_load_menu.show()
		_set_main_buttons_disabled(true)
	else:
		push_warning("MainMenu: SaveLoadMenu node not found")


func _on_settings_pressed() -> void:
	if settings_menu:
		settings_menu.show()
		_set_main_buttons_disabled(true)
	else:
		push_warning("MainMenu: SettingsMenu node not found")


func _on_credits_pressed() -> void:
	if credits_screen:
		credits_screen.show()
		_set_main_buttons_disabled(true)
	else:
		push_warning("MainMenu: CreditsScreen node not found")


func _on_quit_pressed() -> void:
	var tween := create_tween()
	tween.tween_property(self, "modulate:a", 0.0, 0.4)
	tween.tween_callback(get_tree().quit)


func _on_submenu_closed() -> void:
	_set_main_buttons_disabled(false)

# ── Scene transition ──────────────────────────────────────────────────────────

func _transition_to_game() -> void:
	_set_main_buttons_disabled(true)
	var tween := create_tween()
	tween.tween_property(self, "modulate:a", 0.0, 0.6)
	tween.tween_callback(func():
		get_tree().change_scene_to_file("res://scenes/Main.tscn")
	)

# ── Accessibility ──────────────────────────────────────────────────────────────

func _setup_tooltips() -> void:
	btn_new_game.tooltip_text = "Start et nytt oppdrettseventyr"
	btn_load.tooltip_text = "Last inn et lagret spill"
	btn_settings.tooltip_text = "Endre innstillinger for lyd, grafikk og spill"
	btn_credits.tooltip_text = "Se hvem som laget spillet"
	btn_quit.tooltip_text = "Avslutt til skrivebordet"


func _setup_focus_navigation() -> void:
	# Enable keyboard focus for all menu buttons
	btn_new_game.focus_mode = Control.FOCUS_ALL
	btn_load.focus_mode = Control.FOCUS_ALL
	btn_settings.focus_mode = Control.FOCUS_ALL
	btn_credits.focus_mode = Control.FOCUS_ALL
	btn_quit.focus_mode = Control.FOCUS_ALL
	
	# Set up vertical navigation flow
	btn_new_game.focus_neighbor_bottom = NodePath("../BtnLoadGame")
	btn_load.focus_neighbor_top = NodePath("../BtnNewGame")
	btn_load.focus_neighbor_bottom = NodePath("../BtnSettings")
	btn_settings.focus_neighbor_top = NodePath("../BtnLoadGame")
	btn_settings.focus_neighbor_bottom = NodePath("../BtnCredits")
	btn_credits.focus_neighbor_top = NodePath("../BtnSettings")
	btn_credits.focus_neighbor_bottom = NodePath("../BtnQuit")
	btn_quit.focus_neighbor_top = NodePath("../BtnCredits")
	btn_quit.focus_neighbor_bottom = NodePath("../BtnNewGame")  # Loop back
	
	# Set initial focus
	btn_new_game.grab_focus()
