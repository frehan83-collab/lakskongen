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
var _active_tab_id: String = "dashboard"
var _panel_cache: Dictionary = {} # id -> Control (cached panel instances)

# ── Autoload References ───────────────────────────────────────────────────────

var _sim: Node = null
var _theme: Node = null

# ── UI Nodes ──────────────────────────────────────────────────────────────────

@onready var tab_bar: HBoxContainer = %TabBar
@onready var panel_container: Control = %PanelContainer

# ── Lifecycle ─────────────────────────────────────────────────────────────────

func _ready() -> void:
	# Get autoload references
	if has_node("/root/SimBridge"):
		_sim = get_node("/root/SimBridge")
	else:
		push_error("GameUI: SimBridge not found!")
	
	if has_node("/root/LakskongenTheme"):
		_theme = get_node("/root/LakskongenTheme")
	else:
		push_warning("GameUI: LakskongenTheme not found")
	
	# Define all tabs
	_register_tabs()
	
	# Build tab bar UI
	_build_tab_bar()
	
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
				_switch_to_tab(tab.id)
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
	
	# Additional tabs (no F-key shortcuts)
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
		
		# Style button
		_style_tab_button(btn, false)
		
		# Connect signal
		btn.pressed.connect(_on_tab_button_pressed.bind(tab.id))
		
		tab_bar.add_child(btn)
		_tab_buttons[tab.id] = btn

# ── Tab Button Styling ────────────────────────────────────────────────────────

func _style_tab_button(btn: Button, is_active: bool) -> void:
	if _theme:
		# Use LakskongenTheme colors
		if is_active:
			btn.modulate = Color(1.0, 1.0, 1.0)
			btn.add_theme_color_override("font_color", Color(0.40, 0.85, 1.00)) # TEXT_ACCENT
		else:
			btn.modulate = Color(0.85, 0.85, 0.85)
			btn.add_theme_color_override("font_color", Color(0.72, 0.88, 0.98)) # TEXT_BODY
	else:
		# Fallback styling
		if is_active:
			btn.modulate = Color(1.2, 1.2, 1.0)
		else:
			btn.modulate = Color(0.8, 0.8, 0.8)

# ── Tab Navigation ────────────────────────────────────────────────────────────

func _switch_to_tab(tab_id: String) -> void:
	if tab_id == _active_tab_id:
		return # Already on this tab
	
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
	
	# Get or create panel
	var panel: Control = _get_or_create_panel(tab_def)
	
	if panel:
		# Add to container if not already present
		if panel.get_parent() != panel_container:
			panel_container.add_child(panel)
		
		# Set layout to fill container
		panel.set_anchors_preset(Control.PRESET_FULL_RECT)
		panel.show()
		
		_current_panel = panel
		_active_tab_id = tab_id
		
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
	
	# Load and instantiate panel script
	if not ResourceLoader.exists(tab_def.script_path):
		push_error("GameUI: Script not found: %s" % tab_def.script_path)
		return null
	
	var script = load(tab_def.script_path)
	if not script:
		push_error("GameUI: Failed to load script: %s" % tab_def.script_path)
		return null
	
	# Create instance
	var panel = Control.new()
	panel.name = "Panel_" + tab_def.id
	panel.set_script(script)
	panel.hide()
	
	# Cache it
	_panel_cache[tab_def.id] = panel
	
	print("GameUI: Created panel for '%s'" % tab_def.id)
	
	return panel

# ── Signal Handlers ───────────────────────────────────────────────────────────

func _on_tab_button_pressed(tab_id: String) -> void:
	_switch_to_tab(tab_id)

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
