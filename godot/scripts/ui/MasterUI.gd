extends CanvasLayer

## MasterUI — Orchestrates all UI panels with tab navigation
## Uses LakskongenTheme design system for consistent styling

var current_tab := 0
var tab_panels := []
var tab_buttons := []

func _ready():
	# Apply global dark background
	var bg := ColorRect.new()
	bg.color = LakskongenTheme.BG
	bg.set_anchors_preset(Control.PRESET_FULL_RECT)
	bg.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(bg)
	
	# ── Root Layout ──
	var root := VBoxContainer.new()
	root.set_anchors_preset(Control.PRESET_FULL_RECT)
	root.add_theme_constant_override("separation", 0)
	add_child(root)
	
	# ── Top Bar (Tab Navigation + Market HUD) ──
	var top_bar := HBoxContainer.new()
	top_bar.custom_minimum_size.y = 56
	top_bar.add_theme_constant_override("separation", LakskongenTheme.SPACE_SM)
	
	var top_bg := StyleBoxFlat.new()
	top_bg.bg_color = LakskongenTheme.PANEL
	top_bg.content_margin_left = LakskongenTheme.SPACE_MD
	top_bg.content_margin_right = LakskongenTheme.SPACE_MD
	top_bg.content_margin_top = LakskongenTheme.SPACE_SM
	top_bg.content_margin_bottom = LakskongenTheme.SPACE_SM
	
	var top_panel := PanelContainer.new()
	top_panel.add_theme_stylebox_override("panel", top_bg)
	top_panel.add_child(top_bar)
	root.add_child(top_panel)
	
	# Title
	var title := LakskongenTheme.create_label("LAKSKONGEN", LakskongenTheme.FONT_XL, LakskongenTheme.ACCENT)
	top_bar.add_child(title)
	
	# Spacer
	var spacer := Control.new()
	spacer.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	top_bar.add_child(spacer)
	
	# Tab buttons
	var tab_names := ["🕹️ Play", "🗺️ Map", "💰 Finance", "🚦 Regulation", "🔬 Research", "💼 M&A", "🏭 Factory", "👥 Staff", "🏗️ Pens", "💾 Save", "📊 Log"]
	for i in range(tab_names.size()):
		var btn := LakskongenTheme.create_button(tab_names[i], i == 0)
		btn.pressed.connect(_on_tab_pressed.bind(i))
		top_bar.add_child(btn)
		tab_buttons.append(btn)
	
	# ── Main Content Area ──
	var content_area := Control.new()
	content_area.size_flags_vertical = Control.SIZE_EXPAND_FILL
	content_area.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	root.add_child(content_area)
	
	# Instantiate all panels
	var gameplay_panel = load("res://scripts/ui/GameplayPanel.gd").new()
	var map_panel = load("res://scripts/ui/MapPanel.gd").new()
	var financial_dashboard = load("res://scripts/ui/FinancialDashboard.gd").new()
	var regulation_panel = load("res://scripts/ui/RegulationPanel.gd").new()
	var tech_tree_panel = load("res://scripts/ui/TechTreePanel.gd").new()
	var mna_dashboard = load("res://scripts/ui/MnADashboard.gd").new()
	var processing_panel = load("res://scripts/ui/ProcessingFacilityPanel.gd").new()
	var save_load_panel = load("res://scripts/ui/SaveLoadPanel.gd").new()
	var staff_panel = load("res://scripts/ui/StaffPanel.gd").new()
	var pen_management_panel = load("res://scripts/ui/PenManagementPanel.gd").new()
	var event_log = load("res://scripts/ui/EventLog.gd").new()
	
	tab_panels = [gameplay_panel, map_panel, financial_dashboard, regulation_panel, tech_tree_panel, mna_dashboard, processing_panel, staff_panel, pen_management_panel, save_load_panel, event_log]
	
	for panel in tab_panels:
		panel.set_anchors_preset(Control.PRESET_FULL_RECT)
		content_area.add_child(panel)
	
	# Market HUD — always visible overlay (top-right)
	var market_hud = load("res://scripts/ui/MarketHUD.gd").new()
	add_child(market_hud)
	
	# Next Action Widget — HUD overlay (below MarketHUD)
	var next_action_widget = load("res://scripts/ui/NextActionWidget.gd").new()
	add_child(next_action_widget)
	
	# Show first tab
	_switch_to_tab(0)

func _on_tab_pressed(index: int):
	_switch_to_tab(index)

func _switch_to_tab(index: int):
	current_tab = index
	for i in range(tab_panels.size()):
		tab_panels[i].visible = (i == index)
	
	# Update button styles
	for i in range(tab_buttons.size()):
		var style := StyleBoxFlat.new()
		if i == index:
			style.bg_color = LakskongenTheme.ACCENT
		else:
			style.bg_color = LakskongenTheme.ACCENT2
		style.corner_radius_top_left = LakskongenTheme.RADIUS_SM
		style.corner_radius_top_right = LakskongenTheme.RADIUS_SM
		style.corner_radius_bottom_left = LakskongenTheme.RADIUS_SM
		style.corner_radius_bottom_right = LakskongenTheme.RADIUS_SM
		style.content_margin_left = LakskongenTheme.SPACE_MD
		style.content_margin_right = LakskongenTheme.SPACE_MD
		style.content_margin_top = LakskongenTheme.SPACE_SM
		style.content_margin_bottom = LakskongenTheme.SPACE_SM
		tab_buttons[i].add_theme_stylebox_override("normal", style)
tooltip_text = tr("TOOLTIP_%s")  # TODO: Add translation key
