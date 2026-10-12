extends Control

## GameUI — Master UI controller for Lakskongen.
## Manages tab-based navigation across all game panels with keyboard shortcuts.
## Panels are instantiated dynamically on tab switch for performance.

# ── Tab Definition ────────────────────────────────────────────────────────────

class TabDefinition:
	var id: String
	var label: String
	var icon: String
	var script_path: String
	var shortcut_key: Key
	
	func _init(p_id: String, p_label: String, p_icon: String, p_script_path: String, p_key: Key = KEY_NONE) -> void:
		id = p_id
		label = p_label
		icon = p_icon
		script_path = p_script_path
		shortcut_key = p_key

# ── Tab Registry ──────────────────────────────────────────────────────────────

var _tabs: Array[TabDefinition] = []
var _tab_buttons: Dictionary = {} # id -> Button
var _current_panel: Control = null
var _active_tab_id: String = ""
var _panel_cache: Dictionary = {} # id -> Control (cached panel instances)

# ── Autoload References ───────────────────────────────────────────────────────

var _sim: Node = null
var _theme: Node = null
var _tutorial_state: Node = null

## Advanced panels gated by tutorial progress. Kept in sync with
## scripts/autoload/TutorialState.gd (which owns the authoritative mapping);
## this is only a fallback so the UI behaves if the autoload is absent.
const ADVANCED_TABS: Array[String] = ["ras", "offshore", "analytics", "research", "sustainability", "telemetry"]

# ── UI Nodes ──────────────────────────────────────────────────────────────────

@onready var tab_bar: HBoxContainer = %TabBar
@onready var panel_container: Control = %PanelContainer

# ── Lifecycle ─────────────────────────────────────────────────────────────────

func _ready() -> void:
	# Get autoload references
	if has_node("/root/SimBridge"):
		_sim = get_node("/root/SimBridge")
		print("GameUI: SimBridge loaded: " + str(_sim != null))
	else:
		push_error("GameUI: SimBridge not found!")
	
	if has_node("/root/LakskongenTheme"):
		_theme = get_node("/root/LakskongenTheme")
	else:
		push_warning("GameUI: LakskongenTheme not found")
	
	# Progressive disclosure: TutorialState autoload gates advanced panels.
	# Missing autoload → fail-open (all panels unlocked), never crash.
	_tutorial_state = get_node_or_null("/root/TutorialState")
	if _tutorial_state:
		if _tutorial_state.has_signal("phase_changed"):
			_tutorial_state.phase_changed.connect(_on_tutorial_phase_changed)
		if _tutorial_state.has_signal("panel_unlocked"):
			_tutorial_state.panel_unlocked.connect(_on_panel_unlocked)
	else:
		push_warning("GameUI: TutorialState not found — advanced panels unlocked")
	
	# Define all tabs
	_register_tabs()
	
	# Build tab bar UI
	_build_tab_bar()
	
	# Apply tutorial-based locking before the first tab switch.
	_apply_progressive_disclosure()
	
	# Show default tab (Dashboard)
	_switch_to_tab("dashboard")
	
	print("GameUI: Ready with %d tabs" % _tabs.size())

func _input(event: InputEvent) -> void:
	if not visible:
		return
	
	# ESC closes current panel, returns to Dashboard
	if event.is_action_pressed("ui_cancel"):
		if _active_tab_id != "dashboard":
			_switch_to_tab("dashboard")
			get_viewport().set_input_as_handled()
		return
	
	# Check tab keyboard shortcuts
	if event is InputEventKey and event.pressed and not event.echo:
		for tab in _tabs:
			if tab.shortcut_key != KEY_NONE and event.keycode == tab.shortcut_key:
				if not _is_tab_unlocked(tab.id):
					# Locked by tutorial — swallow the shortcut.
					get_viewport().set_input_as_handled()
					return
				_switch_to_tab(tab.id)
				# Keyboard navigation: move focus into the freshly shown panel.
				_focus_panel_for_keyboard(tab.id)
				get_viewport().set_input_as_handled()
				return

# ── Tab Registration ──────────────────────────────────────────────────────────

func _register_tabs() -> void:
	_tabs.clear()
	
	# Core tabs with F-key shortcuts
	_tabs.append(TabDefinition.new("dashboard", "📊 Oversikt", "📊", "res://scripts/ui/Dashboard.gd", KEY_F1))
	_tabs.append(TabDefinition.new("map", "🗺️ Kart", "🗺️", "res://scripts/ui/MapPanel.gd", KEY_F2))
	_tabs.append(TabDefinition.new("finance", "💰 Økonomi", "💰", "res://scripts/ui/FinancePanel.gd", KEY_F3))
	_tabs.append(TabDefinition.new("pens", "🐟 Merder", "🐟", "res://scripts/ui/PenManagement.gd", KEY_F4))
	_tabs.append(TabDefinition.new("production", "🏭 Prosessering", "🏭", "res://scripts/ui/ProductionChainPanel.gd", KEY_F5))
	_tabs.append(TabDefinition.new("research", "🔬 Forskning", "🔬", "res://scripts/ui/TechTreePanel.gd", KEY_F6))
	_tabs.append(TabDefinition.new("sustainability", "🚦 Regulering", "🚦", "res://scripts/ui/SustainabilityPanel.gd", KEY_F7))
	_tabs.append(TabDefinition.new("ras", "🏗️ RAS", "🏗️", "res://scripts/ui/RASPanel.gd", KEY_F8))
	_tabs.append(TabDefinition.new("offshore", "🌊 Offshore", "🌊", "res://scripts/ui/OffshorePanel.gd", KEY_F9))
	_tabs.append(TabDefinition.new("analytics", "📈 Analyse", "📈", "res://scripts/ui/AnalyticsPanel.gd", KEY_F10))
	
	# Additional tabs (no F-key shortcuts)
	_tabs.append(TabDefinition.new("telemetry", "📡 Telemetri", "📡", "res://scripts/ui/TelemetryPanel.gd"))
	_tabs.append(TabDefinition.new("ai_feeding", "🤖 AI-Fôring", "🤖", "res://scripts/ui/AIFeedingPanel.gd"))
	_tabs.append(TabDefinition.new("crew", "👥 Mannskap", "👥", "res://scripts/ui/CrewPanel.gd"))
	_tabs.append(TabDefinition.new("contracts", "📈 Kontrakter", "📈", "res://scripts/ui/ContractsPanel.gd"))
	_tabs.append(TabDefinition.new("encyclopedia", "📚 Leksikon", "📚", "res://scripts/ui/EncyclopediaPanel.gd"))
	_tabs.append(TabDefinition.new("saveload", "💾 Lagre/Laste", "💾", "res://scripts/ui/SaveLoadMenu.gd"))

# ── Tab Bar UI ────────────────────────────────────────────────────────────────

func _build_tab_bar() -> void:
	if not tab_bar:
		push_error("GameUI: TabBar node not found!")
		return
	
	# Clear existing buttons
	for child in tab_bar.get_children():
		child.queue_free()
	_tab_buttons.clear()
	
	# Create button for each tab
	for tab in _tabs:
		var btn = Button.new()
		btn.text = tab.label
		btn.name = "Tab_" + tab.id
		btn.toggle_mode = true
		btn.button_group = null # We'll manage state manually
		# Size to the full label (emoji + text); the bar scrolls when it overflows.
		btn.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
		btn.clip_text = false
		btn.add_theme_font_size_override("font_size", 12)
		
		# Style button
		_style_tab_button(btn, false)
		
		# Connect signal
		btn.pressed.connect(_on_tab_button_pressed.bind(tab.id))
		
		tab_bar.add_child(btn)
		_tab_buttons[tab.id] = btn

	# Keyboard/gamepad: arrow keys walk the tab bar left/right.
	UIPolish.link_horizontal_focus(tab_bar)

## Move keyboard focus into the active panel after a shortcut-driven switch.
## Deferred so the panel is laid out before we grab focus.
func _focus_panel_for_keyboard(_tab_id: String) -> void:
	await get_tree().process_frame
	if _current_panel != null and is_instance_valid(_current_panel):
		UIPolish.focus_first(_current_panel)

# ── Progressive Disclosure ────────────────────────────────────────────────────

## True when a tab may be opened. Non-advanced tabs are always unlocked.
## If the TutorialState autoload is missing we fail-open (never soft-lock).
func _is_tab_unlocked(tab_id: String) -> bool:
	if tab_id not in ADVANCED_TABS:
		return true
	if _tutorial_state == null:
		return true
	if not _tutorial_state.has_method("is_panel_unlocked"):
		return true
	return bool(_tutorial_state.is_panel_unlocked(tab_id))

## Apply enabled/disabled state to every tab button from tutorial progress.
func _apply_progressive_disclosure() -> void:
	for tab_id in _tab_buttons.keys():
		var btn: Button = _tab_buttons[tab_id]
		if btn == null:
			continue
		var unlocked: bool = _is_tab_unlocked(tab_id)
		btn.disabled = not unlocked
		btn.modulate = Color.WHITE if unlocked else Color(1, 1, 1, 0.45)
		if unlocked:
			btn.tooltip_text = ""
		else:
			btn.tooltip_text = "Låses opp senere i tutorialen"

func _on_tutorial_phase_changed(_phase: int) -> void:
	_apply_progressive_disclosure()

func _on_panel_unlocked(_panel_id: String) -> void:
	_apply_progressive_disclosure()

# ── Tab Button Styling ────────────────────────────────────────────────────────

func _style_tab_button(btn: Button, is_active: bool) -> void:
	# Nordic Minimalist v2.0 tab styling
	btn.modulate = Color.WHITE
	
	if is_active:
		var sb := StyleBoxFlat.new()
		sb.bg_color = Color("#0A4D68")  # PRIMARY
		sb.set_corner_radius_all(4)
		sb.set_content_margin_all(8)
		btn.add_theme_stylebox_override("normal", sb)
		btn.add_theme_stylebox_override("hover", sb)
		btn.add_theme_color_override("font_color", Color("#ECEFF4"))
		btn.add_theme_color_override("font_hover_color", Color("#ECEFF4"))
	else:
		var sb := StyleBoxFlat.new()
		sb.bg_color = Color("#FFFFFF")
		sb.border_color = Color("#D8DEE9")
		sb.set_border_width_all(1)
		sb.set_corner_radius_all(4)
		sb.set_content_margin_all(8)
		btn.add_theme_stylebox_override("normal", sb)
		var sb_hover := StyleBoxFlat.new()
		sb_hover.bg_color = Color("#F8F9FB")
		sb_hover.border_color = Color("#88C0D0")
		sb_hover.set_border_width_all(1)
		sb_hover.set_corner_radius_all(4)
		sb_hover.set_content_margin_all(8)
		btn.add_theme_stylebox_override("hover", sb_hover)
		btn.add_theme_color_override("font_color", Color("#4C566A"))
		btn.add_theme_color_override("font_hover_color", Color("#0A4D68"))

# ── Tab Navigation ────────────────────────────────────────────────────────────

func _switch_to_tab(tab_id: String) -> void:
	if tab_id == _active_tab_id:
		return # Already on this tab
	
	# Progressive disclosure gate — locked tabs cannot be switched to.
	if not _is_tab_unlocked(tab_id):
		push_warning("GameUI: Tab '%s' is locked until later in the tutorial" % tab_id)
		return
	
	# Find tab definition
	var tab_def: TabDefinition = null
	for tab in _tabs:
		if tab.id == tab_id:
			tab_def = tab
			break
	
	if not tab_def:
		push_warning("GameUI: Unknown tab id '%s'" % tab_id)
		return
	
	# Hide current panel
	if _current_panel:
		_current_panel.hide()
	
	# Update button states
	for id in _tab_buttons:
		var btn = _tab_buttons[id]
		var is_active = (id == tab_id)
		btn.button_pressed = is_active
		_style_tab_button(btn, is_active)
	
	# Re-apply lock dimming (styling above resets modulate on every button).
	_apply_progressive_disclosure()
	
	# Get or create panel
	var panel: Control = _get_or_create_panel(tab_def)
	
	if panel:
		# Add to container if not already present
		if panel.get_parent() != panel_container:
			panel_container.add_child(panel)
		
		# Set layout to fill container
		panel.set_anchors_preset(Control.PRESET_FULL_RECT)
		panel.show()

		# Juice: soft panel transition (fade + rise), Compatibility-safe tween.
		_panel_enter(panel)

		# Polish: every button in the panel gets a satisfying press-pop.
		# Idempotent (JuiceEffects guards via meta), so re-switching is cheap.
		UIPolish.attach_press_feedback(panel)

		_current_panel = panel
		_active_tab_id = tab_id

		# Keep the active tab visible when the bar scrolls (overflow fix).
		_scroll_tab_into_view(tab_id)
		
		# Notify panel it's now visible (if it has on_panel_shown method)
		if panel.has_method("on_panel_shown"):
			panel.call("on_panel_shown")
		
		print("GameUI: Switched to tab '%s'" % tab_id)
	else:
		push_error("GameUI: Failed to load panel for tab '%s'" % tab_id)

# ── Panel Management ──────────────────────────────────────────────────────────

func _get_or_create_panel(tab_def: TabDefinition) -> Control:
	# Check cache first
	if _panel_cache.has(tab_def.id):
		return _panel_cache[tab_def.id]
	
	var panel: Control = null
	
	# Try loading a matching .tscn scene first (for scene-dependent scripts)
	var scene_path := tab_def.script_path.replace("scripts/ui/", "scenes/ui/").replace(".gd", ".tscn")
	if ResourceLoader.exists(scene_path):
		var scene = load(scene_path) as PackedScene
		if scene:
			panel = scene.instantiate() as Control
			panel.name = "Panel_" + tab_def.id
			print("GameUI: Loaded scene for '%s' from %s" % [tab_def.id, scene_path])
	
	# Fallback: instantiate directly from the script (for programmatic panels).
	# NOTE: Must use script.new() so the node gets the script's native base
	# type (e.g. PanelContainer). Control.new() + set_script() fails for any
	# script whose base is not exactly Control, leaving an empty panel.
	if panel == null:
		if not ResourceLoader.exists(tab_def.script_path):
			push_error("GameUI: Script not found: %s" % tab_def.script_path)
			return null

		var script = load(tab_def.script_path) as GDScript
		if script == null:
			push_error("GameUI: Failed to load script: %s" % tab_def.script_path)
			return null

		panel = script.new() as Control
		if panel == null:
			push_error("GameUI: Script root is not a Control: %s" % tab_def.script_path)
			return null
		panel.name = "Panel_" + tab_def.id
		print("GameUI: Created panel for '%s' (programmatic, base %s)" % [tab_def.id, panel.get_class()])
	
	panel.hide()
	
	# Cache it
	_panel_cache[tab_def.id] = panel
	
	return panel

# ── Signal Handlers ───────────────────────────────────────────────────────────

func _on_tab_button_pressed(tab_id: String) -> void:
	# Juice: press feedback without touching navigation logic.
	var btn: Button = _tab_buttons.get(tab_id)
	if btn:
		JuiceEffects.button_pop(btn)
	_switch_to_tab(tab_id)

# ── Juice helpers (additive; navigation logic above is untouched) ────────────

## Soft enter for tab panels: fade + rise. Falls back silently if the
## panel is not ready for tweens yet.
func _panel_enter(panel: Control) -> void:
	if panel == null or not is_instance_valid(panel):
		return
	JuiceEffects.panel_enter(panel)

## Ensure the active tab button is scrolled into view inside the
## TabBarScroll ScrollContainer (overflow fix for narrow windows).
func _scroll_tab_into_view(tab_id: String) -> void:
	var btn: Control = _tab_buttons.get(tab_id)
	if btn == null:
		return
	var scroller := tab_bar.get_parent() as ScrollContainer
	if scroller == null:
		return
	await get_tree().process_frame
	if not is_instance_valid(btn) or not is_instance_valid(scroller):
		return
	var btn_rect := Rect2(btn.position, btn.size)
	var view_w: float = scroller.size.x
	var scroll: float = scroller.scroll_horizontal
	if btn_rect.position.x < scroll:
		scroller.scroll_horizontal = int(max(0.0, btn_rect.position.x - 8.0))
	elif btn_rect.end.x > scroll + view_w:
		scroller.scroll_horizontal = int(btn_rect.end.x - view_w + 8.0)

# ── Public API ────────────────────────────────────────────────────────────────

## Show a specific tab by id.
func show_tab(tab_id: String) -> void:
	_switch_to_tab(tab_id)

## Get the currently active tab id.
func get_active_tab() -> String:
	return _active_tab_id

## Check if a tab is available.
func has_tab(tab_id: String) -> bool:
	for tab in _tabs:
		if tab.id == tab_id:
			return true
	return false

## Clear the panel cache (useful for reloading after changes).
func clear_panel_cache() -> void:
	for panel in _panel_cache.values():
		if is_instance_valid(panel):
			panel.queue_free()
	_panel_cache.clear()
	print("GameUI: Panel cache cleared")
