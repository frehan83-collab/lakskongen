extends Control

class_name FinancialDashboard

const LakskongenTheme = preload("res://theme/default_theme.gd") # Anta at temaet er her

var simulation_core = null

func _ready():
    anchor_right = 1.0
    anchor_bottom = 1.0
    set_process_priority(1)

    _setup_ui()
    _connect_signals()
    _refresh_financials()

func _setup_ui():
    var scroll_container = ScrollContainer.new()
    scroll_container.set_v_size_flags(SIZE_EXPAND_FILL)
    add_child(scroll_container)

    var main_vbox = VBoxContainer.new()
    main_vbox.add_theme_constant_override("separation", LakskongenTheme.SPACE_LG)
    scroll_container.add_child(main_vbox)

    # Row 1: KPI Tiles
    var kpi_hbox = HBoxContainer.new()
    kpi_hbox.add_theme_constant_override("separation", LakskongenTheme.SPACE_MD)
    kpi_hbox.set_h_size_flags(SIZE_EXPAND_FILL)
    main_vbox.add_child(kpi_hbox)

    kpi_hbox.add_child(_create_kpi_tile("💰 Revenue", "12,500,000 NOK", LakskongenTheme.SUCCESS))
    kpi_hbox.add_child(_create_kpi_tile("📉 Expenses", "8,200,000 NOK", LakskongenTheme.DANGER))
    kpi_hbox.add_child(_create_kpi_tile("📊 Net Profit", "4,300,000 NOK", LakskongenTheme.ACCENT))
    kpi_hbox.add_child(_create_kpi_tile("🏦 Cash", "25,000,000 NOK", LakskongenTheme.GOLD))

    # Row 2: INCOME STATEMENT
    main_vbox.add_child(LakskongenTheme.create_section_header("INCOME STATEMENT"))
    var income_statement_vbox = VBoxContainer.new()
    income_statement_vbox.add_theme_constant_override("separation", LakskongenTheme.SPACE_SM)
    main_vbox.add_child(income_statement_vbox)

    income_statement_vbox.add_child(LakskongenTheme.create_stat("Salmon Sales (Spot):", "8,000,000 NOK", LakskongenTheme.SUCCESS))
    income_statement_vbox.add_child(LakskongenTheme.create_stat("Salmon Sales (Contract):", "3,500,000 NOK", LakskongenTheme.SUCCESS))
    income_statement_vbox.add_child(LakskongenTheme.create_stat("VAP Product Sales:", "1,000,000 NOK", LakskongenTheme.SUCCESS))
    income_statement_vbox.add_child(LakskongenTheme.create_divider(LakskongenTheme.MUTED))
    income_statement_vbox.add_child(LakskongenTheme.create_stat("Feed Costs:", "-3,200,000 NOK", LakskongenTheme.DANGER))
    income_statement_vbox.add_child(LakskongenTheme.create_stat("Staff Salaries:", "-1,800,000 NOK", LakskongenTheme.DANGER))
    income_statement_vbox.add_child(LakskongenTheme.create_stat("Veterinary & Delousing:", "-900,000 NOK", LakskongenTheme.DANGER))
    income_statement_vbox.add_child(LakskongenTheme.create_stat("Insurance:", "-500,000 NOK", LakskongenTheme.DANGER))
    income_statement_vbox.add_child(LakskongenTheme.create_stat("Cartel Operations:", "-800,000 NOK", LakskongenTheme.WARNING))
    income_statement_vbox.add_child(LakskongenTheme.create_stat("Fines & Penalties:", "-1,000,000 NOK", LakskongenTheme.DANGER))
    income_statement_vbox.add_child(LakskongenTheme.create_divider(LakskongenTheme.MUTED))
    var net_profit_stat = LakskongenTheme.create_stat("NET PROFIT:", "4,300,000 NOK", LakskongenTheme.ACCENT)
    _set_label_font_size(net_profit_stat.get_child(0), LakskongenTheme.FONT_LG) # Label
    _set_label_font_size(net_profit_stat.get_child(1), LakskongenTheme.FONT_LG) # Value
    _set_label_bold(net_profit_stat.get_child(0))
    _set_label_bold(net_profit_stat.get_child(1))
    income_statement_vbox.add_child(net_profit_stat)

    # Row 3: KEY METRICS
    main_vbox.add_child(LakskongenTheme.create_section_header("KEY METRICS"))
    var key_metrics_vbox = VBoxContainer.new()
    key_metrics_vbox.add_theme_constant_override("separation", LakskongenTheme.SPACE_SM)
    main_vbox.add_child(key_metrics_vbox)

    key_metrics_vbox.add_child(LakskongenTheme.create_stat("FCR (Feed Conversion Ratio):", "1.15", LakskongenTheme.ACCENT))
    key_metrics_vbox.add_child(LakskongenTheme.create_stat("Survival Rate:", "94.2%", LakskongenTheme.SUCCESS))
    key_metrics_vbox.add_child(LakskongenTheme.create_stat("Production Cost/kg:", "42.50 NOK", LakskongenTheme.TEXT))
    key_metrics_vbox.add_child(LakskongenTheme.create_stat("Avg Harvest Weight:", "5.2 kg", LakskongenTheme.TEXT))
    key_metrics_vbox.add_child(LakskongenTheme.create_stat("Market Share:", "8.3%", LakskongenTheme.ACCENT))
    key_metrics_vbox.add_child(LakskongenTheme.create_stat("Reputation Score:", "72/100", LakskongenTheme.WARNING))
    
    # Row 4: COMING SOON PANEL
    var coming_soon_panel = load("res://scripts/ui/ComingSoonPanel.gd").new()
    main_vbox.add_child(coming_soon_panel)


func _create_kpi_tile(title_text, value_text, value_color):
    var panel = LakskongenTheme.create_styled_panel()
    panel.set_h_size_flags(SIZE_EXPAND_FILL)

    var vbox = VBoxContainer.new()
    vbox.alignment = BoxContainer.ALIGN_CENTER
    vbox.add_theme_constant_override("separation", LakskongenTheme.SPACE_SM)
    panel.add_child(vbox)

    var title_label = LakskongenTheme.create_label(title_text, LakskongenTheme.FONT_SM, LakskongenTheme.MUTED)
    title_label.set_h_size_flags(SIZE_EXPAND_FILL)
    title_label.set_horizontal_alignment(HORIZONTAL_ALIGNMENT_CENTER)
    vbox.add_child(title_label)

    var value_label = LakskongenTheme.create_label(value_text, LakskongenTheme.FONT_XXL, value_color)
    value_label.set_h_size_flags(SIZE_EXPAND_FILL)
    value_label.set_horizontal_alignment(HORIZONTAL_ALIGNMENT_CENTER)
    vbox.add_child(value_label)

    return panel

func _connect_signals():
    simulation_core = get_node_or_null("/root/SimulationCore")
    if simulation_core:
        if not simulation_core.is_connected("DayTicked", Callable(self, "_refresh_financials")):
            simulation_core.connect("DayTicked", Callable(self, "_refresh_financials"))
    else:
        print("FinancialDashboard: Could not find SimulationCore at /root/SimulationCore")

func _refresh_financials():
    # Placeholder for fetching real financial data
    print("FinancialDashboard: Refreshing financials (using dummy data for now).")
    # Here you would update the labels with actual data from SimulationCore or other sources.
    # For example:
    # kpi_hbox.get_child(0).get_node("VBoxContainer/ValueLabel").text = str(simulation_core.get_current_revenue()) + " NOK"
    pass

func _set_label_font_size(label_node, font_size):
    if label_node and label_node is Label:
        var font = label_node.get_theme_font("font").duplicate()
        font.set_size(font_size)
        label_node.add_theme_font_override("font", font)

func _set_label_bold(label_node):
    if label_node and label_node is Label:
        var font = label_node.get_theme_font("font").duplicate()
        font.set_size(label_node.get_theme_font("font").get_size())
        font.set_font_weight(TextServer.FONT_WEIGHT_BOLD)
        label_node.add_theme_font_override("font", font)
