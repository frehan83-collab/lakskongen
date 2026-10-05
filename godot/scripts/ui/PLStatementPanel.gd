extends Panel
## P&L Statement Dashboard Panel
## Displays profit & loss statement with real-time data from EconomicEngine via GameBridge.

# UI references
@onready var revenue_label: Label = $VBoxContainer/RevenueLabel
@onready var cogs_label: Label = $VBoxContainer/CogsLabel
@onready var gross_profit_label: Label = $VBoxContainer/GrossProfitLabel
@onready var opex_label: Label = $VBoxContainer/OpexLabel
@onready var ebitda_label: Label = $VBoxContainer/EbitdaLabel
@onready var ebit_label: Label = $VBoxContainer/EbitLabel
@onready var net_profit_label: Label = $VBoxContainer/NetProfitLabel
@onready var gross_margin_label: Label = $VBoxContainer/GrossMarginLabel
@onready var operating_margin_label: Label = $VBoxContainer/OperatingMarginLabel
@onready var net_margin_label: Label = $VBoxContainer/NetMarginLabel
@onready var refresh_button: Button = $VBoxContainer/RefreshButton

# GameBridge reference
var game_bridge: Node

func _ready() -> void:
	# Get GameBridge autoload
	game_bridge = get_node("/root/GameBridge")
	
	# Connect signals
	if refresh_button:
		refresh_button.pressed.connect(_on_refresh_pressed)
	
	if game_bridge:
		game_bridge.GameStateUpdated.connect(_on_game_state_updated)
	
	# Initial load
	_refresh_data()

func _on_game_state_updated() -> void:
	_refresh_data()

func _on_refresh_pressed() -> void:
	_refresh_data()

func _refresh_data() -> void:
	if not game_bridge:
		push_error("PLStatementPanel: GameBridge not found")
		return
	
	# Get P&L data from GameBridge
	var pl_data: Dictionary = game_bridge.GetPLData()
	
	if pl_data.is_empty():
		push_warning("PLStatementPanel: No P&L data available")
		return
	
	# Update labels
	if revenue_label:
		revenue_label.text = "Revenue: %s NOK" % _format_currency(pl_data.get("revenue", 0.0))
	
	if cogs_label:
		cogs_label.text = "Cost of Sales: %s NOK" % _format_currency(pl_data.get("cost_of_sales", 0.0))
	
	if gross_profit_label:
		var gross_profit: float = pl_data.get("gross_profit", 0.0)
		gross_profit_label.text = "Gross Profit: %s NOK" % _format_currency(gross_profit)
		gross_profit_label.add_theme_color_override("font_color", _get_profit_color(gross_profit))
	
	if opex_label:
		opex_label.text = "Operating Expenses: %s NOK" % _format_currency(pl_data.get("operating_expenses", 0.0))
	
	if ebitda_label:
		var ebitda: float = pl_data.get("ebitda", 0.0)
		ebitda_label.text = "EBITDA: %s NOK" % _format_currency(ebitda)
		ebitda_label.add_theme_color_override("font_color", _get_profit_color(ebitda))
	
	if ebit_label:
		var ebit: float = pl_data.get("ebit", 0.0)
		ebit_label.text = "EBIT: %s NOK" % _format_currency(ebit)
		ebit_label.add_theme_color_override("font_color", _get_profit_color(ebit))
	
	if net_profit_label:
		var net_profit: float = pl_data.get("net_profit", 0.0)
		net_profit_label.text = "Net Profit: %s NOK" % _format_currency(net_profit)
		net_profit_label.add_theme_color_override("font_color", _get_profit_color(net_profit))
	
	if gross_margin_label:
		gross_margin_label.text = "Gross Margin: %.1f%%" % pl_data.get("gross_margin_pct", 0.0)
	
	if operating_margin_label:
		operating_margin_label.text = "Operating Margin: %.1f%%" % pl_data.get("operating_margin_pct", 0.0)
	
	if net_margin_label:
		net_margin_label.text = "Net Margin: %.1f%%" % pl_data.get("net_margin_pct", 0.0)

func _format_currency(value: float) -> String:
	if value >= 1_000_000_000:
		return "%.2fB" % (value / 1_000_000_000.0)
	elif value >= 1_000_000:
		return "%.2fM" % (value / 1_000_000.0)
	elif value >= 1_000:
		return "%.1fK" % (value / 1_000.0)
	else:
		return "%.0f" % value

func _get_profit_color(value: float) -> Color:
	if value > 0:
		return Color(0.2, 0.8, 0.2) # Green for profit
	elif value < 0:
		return Color(0.8, 0.2, 0.2) # Red for loss
	else:
		return Color(0.8, 0.8, 0.8) # Gray for break-even
