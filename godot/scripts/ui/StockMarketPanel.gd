## StockMarketPanel v2.1 — Nordic Minimalist (scene-based).
## IPO eligibility, stock price tracking, quarterly earnings, and dividend policy UI.
## Displays company valuation, IPO readiness, stock chart, and financial performance.
## Layout lives in scenes/ui/StockMarketPanel.tscn; this script is wiring only:
## @onready % refs, signal hookups, localization, theme fonts, data refresh.
## Earnings rows and dividend rows are data-driven and still populated in code.

extends PanelContainer
class_name StockMarketPanel

# ── Scene refs ────────────────────────────────────────────────────────────────
@onready var _title_label: Label = %TitleLabel
@onready var _ipo_section_title: Label = %IpoHeader
@onready var _stock_section_title: Label = %StockHeader
@onready var _earnings_section_title: Label = %EarningsHeader
@onready var _dividend_section_title: Label = %DividendHeader

@onready var ipo_status_label: Label = %IpoStatusLabel
@onready var company_valuation_label: Label = %CompanyValuationLabel
@onready var stock_price_label: Label = %StockPriceLabel
@onready var stock_chart_container: VBoxContainer = %StockChartBox
@onready var quarterly_earnings_container: VBoxContainer = %EarningsList
@onready var dividend_policy_container: VBoxContainer = %DividendBox
@onready var ipo_button: Button = %IpoButton

# Stock market state
var is_public: bool = false
var stock_price_history: Array[float] = []
var quarterly_earnings: Array[Dictionary] = []

# Color scheme
const COLOR_GREEN = Color("#2E6B45")
const COLOR_YELLOW = Color("#8A5A00")
const COLOR_RED = Color("#B3172F")
const COLOR_TEXT = "#1C1E20"
const COLOR_MUTED = "#5A6168"

# IPO eligibility criteria (Norwegian stock market requirements)
const IPO_MIN_REVENUE = 50_000_000.0  # 50M NOK annual revenue
const IPO_MIN_PROFIT = 5_000_000.0    # 5M NOK annual profit
const IPO_MIN_SITES = 3               # At least 3 operational sites
const IPO_MIN_DAYS = 365              # 1 year of operations

var _theme: Node = null

func _ready():
	_apply_localization()
	_apply_theme_fonts()

	ipo_button.pressed.connect(_on_ipo_button_pressed)

	# Connect to GameBridge for live data
	if has_node("/root/GameBridge"):
		var game_bridge = get_node("/root/GameBridge")
		game_bridge.GameStateUpdated.connect(_on_state_updated)
		game_bridge.SimulationTicked.connect(_on_day_advanced)
	else:
		push_warning("StockMarketPanel: GameBridge not found")

	_refresh_from_bridge()

func _input(event: InputEvent) -> void:
	if not visible:
		return

	if event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()

# ── Scene wiring ──────────────────────────────────────────────────────────────

func _apply_localization() -> void:
	_title_label.text = "📈 " + tr("UI_STOCK_TITLE")
	_title_label.tooltip_text = "Company valuation, IPO eligibility, and stock market performance"
	_ipo_section_title.text = tr("UI_STOCK_IPO_ELIGIBILITY")
	_stock_section_title.text = tr("UI_STOCK_PERFORMANCE")
	_earnings_section_title.text = tr("UI_STOCK_QUARTERLY")
	_dividend_section_title.text = tr("UI_STOCK_DIVIDEND")
	ipo_button.text = tr("UI_STOCK_LAUNCH_IPO")
	ipo_button.tooltip_text = tr("UI_STOCK_LAUNCH_IPO_TOOLTIP")

func _apply_theme_fonts() -> void:
	_theme = get_node_or_null("/root/LakskongenTheme")
	if not _theme:
		return
	LakskongenTheme.set_strong(_title_label)
	LakskongenTheme.set_strong(_ipo_section_title)
	LakskongenTheme.set_strong(_stock_section_title)
	LakskongenTheme.set_strong(_earnings_section_title)
	LakskongenTheme.set_strong(_dividend_section_title)

func _on_state_updated():
	_refresh_from_bridge()

func _on_day_advanced(day: int):
	# Update stock price daily if public
	if is_public:
		_update_stock_price()

	# Quarterly earnings (every 90 days)
	if day % 90 == 0:
		_record_quarterly_earnings(day)

func _refresh_from_bridge():
	if not has_node("/root/GameBridge"):
		return

	var game_bridge = get_node("/root/GameBridge")

	var balance = game_bridge.Balance
	var revenue = game_bridge.Revenue  # Total accumulated revenue
	var profit = game_bridge.Profit
	var site_count = game_bridge.SiteCount
	var current_day = game_bridge.CurrentDay

	# Calculate company valuation (Price-to-Earnings ratio ~ 15x for aquaculture)
	var valuation = max(balance, profit * 15.0)
	company_valuation_label.text = "Company Valuation: %s NOK" % _format_currency(valuation)

	# Check IPO eligibility
	var eligible = _check_ipo_eligibility(revenue, profit, site_count, current_day)
	_update_ipo_status(eligible, revenue, profit, site_count, current_day)

	# Update stock price if public
	if is_public:
		_display_stock_chart()
		_display_quarterly_earnings()
		_display_dividend_policy(profit)

func _check_ipo_eligibility(revenue: float, profit: float, site_count: int, days: int) -> bool:
	return revenue >= IPO_MIN_REVENUE and \
		   profit >= IPO_MIN_PROFIT and \
		   site_count >= IPO_MIN_SITES and \
		   days >= IPO_MIN_DAYS

func _update_ipo_status(eligible: bool, revenue: float, profit: float, site_count: int, days: int):
	if is_public:
		ipo_status_label.text = "✅ " + tr("UI_STOCK_IS_PUBLIC")
		LakskongenTheme.set_text_color(ipo_status_label, COLOR_GREEN)
		ipo_button.disabled = true
		ipo_button.text = tr("UI_STOCK_ALREADY_PUBLIC")
		return

	if eligible:
		ipo_status_label.text = "✅ " + tr("UI_STOCK_ELIGIBLE")
		LakskongenTheme.set_text_color(ipo_status_label, COLOR_GREEN)
		ipo_button.disabled = false
	else:
		var requirements = []
		if revenue < IPO_MIN_REVENUE:
			requirements.append("Revenue: %s / %s NOK" % [_format_currency(revenue), _format_currency(IPO_MIN_REVENUE)])
		if profit < IPO_MIN_PROFIT:
			requirements.append("Profit: %s / %s NOK" % [_format_currency(profit), _format_currency(IPO_MIN_PROFIT)])
		if site_count < IPO_MIN_SITES:
			requirements.append("Sites: %d / %d" % [site_count, IPO_MIN_SITES])
		if days < IPO_MIN_DAYS:
			requirements.append("Days: %d / %d" % [days, IPO_MIN_DAYS])

		ipo_status_label.text = "❌ IPO Requirements:\n" + "\n".join(requirements)
		LakskongenTheme.set_text_color(ipo_status_label, COLOR_RED)
		ipo_button.disabled = true

func _on_ipo_button_pressed():
	if not has_node("/root/GameBridge"):
		return

	var game_bridge = get_node("/root/GameBridge")
	var valuation = max(game_bridge.Balance, game_bridge.Profit * 15.0)
	var initial_stock_price = valuation / 1_000_000.0  # Assume 1M shares

	is_public = true
	stock_price_history.clear()
	stock_price_history.append(initial_stock_price)

	_refresh_from_bridge()

func _update_stock_price():
	if not has_node("/root/GameBridge"):
		return

	var game_bridge = get_node("/root/GameBridge")
	var profit = game_bridge.Profit
	var balance = game_bridge.Balance

	# Stock price fluctuates based on profitability and balance
	var last_price = stock_price_history[-1] if stock_price_history.size() > 0 else 50.0
	var profit_factor = 1.0 + (profit / 10_000_000.0) * 0.01  # +1% per 10M profit
	var volatility = randf_range(-0.02, 0.02)  # ±2% daily volatility

	var new_price = last_price * profit_factor * (1.0 + volatility)
	new_price = clamp(new_price, 1.0, 10000.0)

	stock_price_history.append(new_price)

	# Keep only last 365 days
	if stock_price_history.size() > 365:
		stock_price_history.pop_front()

func _display_stock_chart():
	if stock_price_history.is_empty():
		return

	var current_price = stock_price_history[-1]
	var day_change = 0.0
	if stock_price_history.size() > 1:
		var prev_price = stock_price_history[-2]
		day_change = (current_price - prev_price) / prev_price * 100.0

	var color = COLOR_GREEN if day_change >= 0 else COLOR_RED
	stock_price_label.text = "Current Price: %.2f NOK (%+.2f%%)" % [current_price, day_change]
	LakskongenTheme.set_text_color(stock_price_label, color)

	# Simple ASCII chart (TODO: replace with GraphEdit or custom drawing)
	_clear_container(stock_chart_container)
	var chart_label = Label.new()
	chart_label.text = _generate_ascii_chart(stock_price_history)
	LakskongenTheme.set_text_color(chart_label, Color(COLOR_MUTED))
	stock_chart_container.add_child(chart_label)

func _display_quarterly_earnings():
	_clear_container(quarterly_earnings_container)

	if quarterly_earnings.is_empty():
		var label = Label.new()
		label.text = tr("UI_STOCK_NO_QUARTERS")
		LakskongenTheme.set_text_color(label, Color(COLOR_MUTED))
		quarterly_earnings_container.add_child(label)
		return

	# Show last 4 quarters
	var recent = quarterly_earnings.slice(max(0, quarterly_earnings.size() - 4), quarterly_earnings.size())
	for quarter in recent:
		var row = HBoxContainer.new()
		row.add_theme_constant_override("separation", 8)

		var quarter_label = Label.new()
		quarter_label.text = "Q%d (Day %d)" % [quarter.get("quarter", 0), quarter.get("day", 0)]
		quarter_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		LakskongenTheme.set_text_color(quarter_label, Color(COLOR_TEXT))
		row.add_child(quarter_label)

		var revenue_label = Label.new()
		revenue_label.text = "Revenue: %s" % _format_currency(quarter.get("revenue", 0.0))
		LakskongenTheme.set_text_color(revenue_label, Color(COLOR_MUTED))
		row.add_child(revenue_label)

		var profit_label = Label.new()
		var profit = quarter.get("profit", 0.0)
		profit_label.text = "Profit: %s" % _format_currency(profit)
		LakskongenTheme.set_text_color(profit_label, COLOR_GREEN if profit > 0 else COLOR_RED)
		row.add_child(profit_label)

		quarterly_earnings_container.add_child(row)

func _display_dividend_policy(annual_profit: float):
	_clear_container(dividend_policy_container)

	# Standard dividend policy: 30% payout ratio
	var dividend_payout_ratio = 0.30
	var annual_dividend = annual_profit * dividend_payout_ratio
	var dividend_per_share = annual_dividend / 1_000_000.0  # 1M shares

	var policy_label = Label.new()
	policy_label.text = "Payout Ratio: %.0f%%" % (dividend_payout_ratio * 100)
	LakskongenTheme.set_text_color(policy_label, Color(COLOR_MUTED))
	dividend_policy_container.add_child(policy_label)

	var dividend_label = Label.new()
	dividend_label.text = "Annual Dividend: %s NOK (%.2f NOK/share)" % [_format_currency(annual_dividend), dividend_per_share]
	LakskongenTheme.set_text_color(dividend_label, Color(COLOR_TEXT))
	dividend_policy_container.add_child(dividend_label)

func _record_quarterly_earnings(day: int):
	if not has_node("/root/GameBridge"):
		return

	var game_bridge = get_node("/root/GameBridge")
	var quarter_num = (day / 90) % 4 + 1

	quarterly_earnings.append({
		"quarter": quarter_num,
		"day": day,
		"revenue": game_bridge.Revenue,
		"profit": game_bridge.Profit
	})

func _generate_ascii_chart(data: Array[float]) -> String:
	if data.is_empty():
		return ""

	var min_val = data.min()
	var max_val = data.max()
	var range_val = max_val - min_val
	if range_val < 0.01:
		return "Flat"

	var chart = ""
	var samples = min(30, data.size())
	var step = max(1, data.size() / samples)

	for i in range(samples):
		var idx = i * step
		if idx >= data.size():
			break
		var normalized = (data[idx] - min_val) / range_val
		var bar_len = int(normalized * 20)
		chart += "█" * bar_len + "\n"

	return chart

func _clear_container(container: VBoxContainer):
	if container == null:
		return
	for child in container.get_children():
		child.queue_free()

func _format_currency(value: float) -> String:
	if value >= 1_000_000_000:
		return "%.2fB" % (value / 1_000_000_000.0)
	elif value >= 1_000_000:
		return "%.1fM" % (value / 1_000_000.0)
	elif value >= 1_000:
		return "%.0fK" % (value / 1_000.0)
	else:
		return "%.0f" % value
