extends Panel
## ROI Calculator Dashboard Panel
## Connects to CostModel and RevenueModel for return on investment analysis.

# UI references
@onready var investment_label: Label = $VBoxContainer/InvestmentLabel
@onready var annual_return_label: Label = $VBoxContainer/AnnualReturnLabel
@onready var roi_label: Label = $VBoxContainer/RoiLabel
@onready var payback_label: Label = $VBoxContainer/PaybackLabel
@onready var npv_label: Label = $VBoxContainer/NpvLabel
@onready var irr_label: Label = $VBoxContainer/IrrLabel
@onready var break_even_label: Label = $VBoxContainer/BreakEvenLabel
@onready var verdict_label: Label = $VBoxContainer/VerdictLabel
@onready var refresh_button: Button = $VBoxContainer/RefreshButton

var game_bridge: Node

func _ready() -> void:
	game_bridge = get_node("/root/GameBridge")
	
	if refresh_button:
		refresh_button.pressed.connect(_on_refresh_pressed)
	
	if game_bridge:
		game_bridge.game_state_updated.connect(_on_game_state_updated)
	
	_refresh_data()

func _on_game_state_updated() -> void:
	_refresh_data()

func _on_refresh_pressed() -> void:
	_refresh_data()

func _refresh_data() -> void:
	if not game_bridge:
		push_error("ROICalculatorPanel: GameBridge not found")
		return
	
	var roi_data: Dictionary = game_bridge.GetROIAnalysis()
	
	if roi_data.is_empty():
		push_warning("ROICalculatorPanel: No ROI data available")
		return
	
	_update_display(roi_data)

func _update_display(roi_data: Dictionary) -> void:
	var investment: float = roi_data.get("total_investment", 0.0)
	var annual_return: float = roi_data.get("annual_return", 0.0)
	var roi_pct: float = roi_data.get("roi_pct", 0.0)
	var payback_months: float = roi_data.get("payback_months", 0.0)
	var npv: float = roi_data.get("npv", 0.0)
	var irr_pct: float = roi_data.get("irr_pct", 0.0)
	var break_even_month: int = roi_data.get("break_even_month", 0)
	
	if investment_label:
		investment_label.text = "Total Investment: %s NOK" % _format_currency(investment)
	
	if annual_return_label:
		annual_return_label.text = "Annual Return: %s NOK" % _format_currency(annual_return)
		annual_return_label.add_theme_color_override("font_color", _get_profit_color(annual_return))
	
	if roi_label:
		roi_label.text = "ROI: %.1f%%" % roi_pct
		roi_label.add_theme_color_override("font_color", _get_roi_color(roi_pct))
	
	if payback_label:
		var payback_years: float = payback_months / 12.0
		payback_label.text = "Payback Period: %.1f years" % payback_years
		payback_label.add_theme_color_override("font_color", _get_payback_color(payback_years))
	
	if npv_label:
		npv_label.text = "NPV (5-year): %s NOK" % _format_currency(npv)
		npv_label.add_theme_color_override("font_color", _get_profit_color(npv))
	
	if irr_label:
		irr_label.text = "IRR: %.1f%%" % irr_pct
		irr_label.add_theme_color_override("font_color", _get_roi_color(irr_pct))
	
	if break_even_label:
		break_even_label.text = "Break-Even: Month %d" % break_even_month
	
	if verdict_label:
		verdict_label.text = _get_investment_verdict(roi_pct, payback_months / 12.0, npv)

func _get_investment_verdict(roi_pct: float, payback_years: float, npv: float) -> String:
	if roi_pct > 20.0 and payback_years < 3.0 and npv > 0:
		return "✅ Excellent Investment - Strong returns and fast payback"
	elif roi_pct > 15.0 and payback_years < 5.0:
		return "✅ Good Investment - Solid fundamentals"
	elif roi_pct > 10.0 and payback_years < 7.0:
		return "⚠️ Moderate Investment - Consider alternatives"
	elif roi_pct > 5.0:
		return "⚠️ Weak Investment - High risk, low return"
	else:
		return "🚨 Poor Investment - Negative returns expected"

func _format_currency(value: float) -> String:
	if value >= 1_000_000_000:
		return "%.2fB" % (value / 1_000_000_000.0)
	elif value >= 1_000_000:
		return "%.1fM" % (value / 1_000_000.0)
	elif value >= 1_000:
		return "%.0fK" % (value / 1_000.0)
	else:
		return "%.0f" % value

func _get_profit_color(value: float) -> Color:
	return Color(0.2, 0.8, 0.2) if value > 0 else Color(0.8, 0.2, 0.2)

func _get_roi_color(roi_pct: float) -> Color:
	if roi_pct > 20.0:
		return Color(0.2, 0.8, 0.2) # Excellent
	elif roi_pct > 15.0:
		return Color(0.4, 0.8, 0.4) # Good
	elif roi_pct > 10.0:
		return Color(0.8, 0.8, 0.2) # Moderate
	elif roi_pct > 5.0:
		return Color(0.8, 0.6, 0.2) # Weak
	else:
		return Color(0.8, 0.2, 0.2) # Poor

func _get_payback_color(payback_years: float) -> Color:
	if payback_years < 3.0:
		return Color(0.2, 0.8, 0.2) # Fast
	elif payback_years < 5.0:
		return Color(0.6, 0.8, 0.4) # Good
	elif payback_years < 7.0:
		return Color(0.8, 0.8, 0.2) # Moderate
	else:
		return Color(0.8, 0.2, 0.2) # Slow
