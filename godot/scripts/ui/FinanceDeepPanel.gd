## FinanceDeepPanel.gd
## Deep Finance UI Panel — displays detailed financial information including loans, tax burden, and investor relations.
## Extends Control for dynamic UI construction and state refresh from SimBridge.

extends Control
class_name FinanceDeepPanel

# ══════════════════════════════════════════════════════════════════════════════
# SIGNALS
# ══════════════════════════════════════════════════════════════════════════════

signal loan_requested(amount: float, duration_months: int)
signal tax_optimization_triggered()

# ══════════════════════════════════════════════════════════════════════════════
# UI REFERENCES
# ══════════════════════════════════════════════════════════════════════════════

@onready var scroll_container: ScrollContainer
@onready var content_vbox: VBoxContainer

# Section containers
@onready var loan_management_container: VBoxContainer
@onready var tax_overview_container: VBoxContainer
@onready var investor_relations_container: VBoxContainer
@onready var financial_health_container: VBoxContainer

# ══════════════════════════════════════════════════════════════════════════════
# COLOR SCHEME
# ══════════════════════════════════════════════════════════════════════════════

const COLOR_GREEN = Color("#A3BE8C")
const COLOR_YELLOW = Color("#EBCB8B")
const COLOR_RED = Color("#BF616A")
const COLOR_BLUE = Color("#5E81AC")
const COLOR_TEXT = "#2E3440"
const COLOR_MUTED = "#7B8794"
const COLOR_HIGHLIGHT = "#0A4D68"

# ══════════════════════════════════════════════════════════════════════════════
# LIFECYCLE
# ══════════════════════════════════════════════════════════════════════════════

func _ready() -> void:
	_create_ui()
	
	# Connect to SimBridge for live data updates
	if has_node("/root/SimBridge"):
		var sim_bridge = get_node("/root/SimBridge")
		if sim_bridge.has_signal("state_updated"):
			sim_bridge.state_updated.connect(_on_state_updated)
		print("FinanceDeepPanel: connected to SimBridge")
	else:
		push_warning("FinanceDeepPanel: SimBridge not found")
	
	# Initial refresh
	_refresh_state()

func _input(event: InputEvent) -> void:
	"""Handle ESC to close"""
	if not visible:
		return
	
	if event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()

# ══════════════════════════════════════════════════════════════════════════════
# UI CONSTRUCTION
# ══════════════════════════════════════════════════════════════════════════════

func _create_ui() -> void:
	"""Dynamically create all UI elements"""
	
	# Root margin container
	var margin = MarginContainer.new()
	margin.add_theme_constant_override("margin_top", 16)
	margin.add_theme_constant_override("margin_bottom", 16)
	margin.add_theme_constant_override("margin_left", 16)
	margin.add_theme_constant_override("margin_right", 16)
	add_child(margin)
	
	# Scroll container for content
	scroll_container = ScrollContainer.new()
	scroll_container.size_flags_vertical = Control.SIZE_EXPAND_FILL
	margin.add_child(scroll_container)
	
	# Main vertical layout
	content_vbox = VBoxContainer.new()
	content_vbox.add_theme_constant_override("separation", 20)
	content_vbox.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll_container.add_child(content_vbox)
	
	# ══════════════════════════════════════════════════════════════════════════
	# HEADER
	# ══════════════════════════════════════════════════════════════════════════
	var header = Label.new()
	header.text = "💰 Finansiell Oversikt"
	header.add_theme_font_size_override("font_size", 22)
	header.add_theme_color_override("font_color", Color(COLOR_HIGHLIGHT))
	header.tooltip_text = "Dyptgående finansiell analyse — lån, skatt, investorer, og økonomisk helse."
	content_vbox.add_child(header)
	
	var separator0 = HSeparator.new()
	content_vbox.add_child(separator0)
	
	# ══════════════════════════════════════════════════════════════════════════
	# 1. LOAN MANAGEMENT
	# ══════════════════════════════════════════════════════════════════════════
	var loan_header = Label.new()
	loan_header.text = "🏦 Lånstyring"
	loan_header.add_theme_font_size_override("font_size", 18)
	loan_header.add_theme_color_override("font_color", Color(COLOR_TEXT))
	content_vbox.add_child(loan_header)
	
	loan_management_container = VBoxContainer.new()
	loan_management_container.add_theme_constant_override("separation", 8)
	content_vbox.add_child(loan_management_container)
	
	var separator1 = HSeparator.new()
	content_vbox.add_child(separator1)
	
	# ══════════════════════════════════════════════════════════════════════════
	# 2. TAX OVERVIEW
	# ══════════════════════════════════════════════════════════════════════════
	var tax_header = Label.new()
	tax_header.text = "📊 Skatteoversikt"
	tax_header.add_theme_font_size_override("font_size", 18)
	tax_header.add_theme_color_override("font_color", Color(COLOR_TEXT))
	content_vbox.add_child(tax_header)
	
	tax_overview_container = VBoxContainer.new()
	tax_overview_container.add_theme_constant_override("separation", 8)
	content_vbox.add_child(tax_overview_container)
	
	var separator2 = HSeparator.new()
	content_vbox.add_child(separator2)
	
	# ══════════════════════════════════════════════════════════════════════════
	# 3. INVESTOR RELATIONS
	# ══════════════════════════════════════════════════════════════════════════
	var investor_header = Label.new()
	investor_header.text = "👔 Investorrelasjoner"
	investor_header.add_theme_font_size_override("font_size", 18)
	investor_header.add_theme_color_override("font_color", Color(COLOR_TEXT))
	content_vbox.add_child(investor_header)
	
	investor_relations_container = VBoxContainer.new()
	investor_relations_container.add_theme_constant_override("separation", 8)
	content_vbox.add_child(investor_relations_container)
	
	var separator3 = HSeparator.new()
	content_vbox.add_child(separator3)
	
	# ══════════════════════════════════════════════════════════════════════════
	# 4. FINANCIAL HEALTH SUMMARY
	# ══════════════════════════════════════════════════════════════════════════
	var health_header = Label.new()
	health_header.text = "❤️ Finansiell Helse"
	health_header.add_theme_font_size_override("font_size", 18)
	health_header.add_theme_color_override("font_color", Color(COLOR_TEXT))
	content_vbox.add_child(health_header)
	
	financial_health_container = VBoxContainer.new()
	financial_health_container.add_theme_constant_override("separation", 8)
	content_vbox.add_child(financial_health_container)

# ══════════════════════════════════════════════════════════════════════════════
# STATE REFRESH
# ══════════════════════════════════════════════════════════════════════════════

func _on_state_updated() -> void:
	_refresh_state()

func _refresh_state() -> void:
	"""Pull live data from SimBridge and update display"""
	if not has_node("/root/SimBridge"):
		return
	
	var sim_bridge = get_node("/root/SimBridge")
	
	# Get financial data (with safe fallbacks)
	var loans = _get_loans_safe(sim_bridge)
	var tax_data = _get_tax_data_safe(sim_bridge)
	var investors = _get_investors_safe(sim_bridge)
	var health_data = _get_financial_health_safe(sim_bridge)
	
	# Update UI sections
	_update_loan_management(loans)
	_update_tax_overview(tax_data)
	_update_investor_relations(investors)
	_update_financial_health(health_data)

# ══════════════════════════════════════════════════════════════════════════════
# 1. LOAN MANAGEMENT
# ══════════════════════════════════════════════════════════════════════════════

func _update_loan_management(loans_data: Dictionary) -> void:
	_clear_container(loan_management_container)
	
	var active_loans = loans_data.get("active_loans", [])
	var total_debt = loans_data.get("total_debt", 0.0)
	var monthly_payment = loans_data.get("monthly_payment", 0.0)
	var credit_rating = loans_data.get("credit_rating", "A")
	var available_credit = loans_data.get("available_credit", 50_000_000.0)
	
	# Total debt summary
	var debt_row = HBoxContainer.new()
	debt_row.add_theme_constant_override("separation", 8)
	
	var debt_label = Label.new()
	debt_label.text = "Total gjeld:"
	debt_label.custom_minimum_size = Vector2(150, 0)
	debt_label.add_theme_color_override("font_color", Color(COLOR_MUTED))
	debt_row.add_child(debt_label)
	
	var debt_value = Label.new()
	debt_value.text = _format_currency(total_debt)
	debt_value.add_theme_font_size_override("font_size", 16)
	debt_value.add_theme_color_override("font_color", _debt_color(total_debt))
	debt_row.add_child(debt_value)
	
	loan_management_container.add_child(debt_row)
	
	# Monthly payment
	var payment_row = HBoxContainer.new()
	payment_row.add_theme_constant_override("separation", 8)
	
	var payment_label = Label.new()
	payment_label.text = "Månedlig betaling:"
	payment_label.custom_minimum_size = Vector2(150, 0)
	payment_label.add_theme_color_override("font_color", Color(COLOR_MUTED))
	payment_row.add_child(payment_label)
	
	var payment_value = Label.new()
	payment_value.text = _format_currency(monthly_payment)
	payment_value.add_theme_font_size_override("font_size", 14)
	payment_value.add_theme_color_override("font_color", Color(COLOR_TEXT))
	payment_row.add_child(payment_value)
	
	loan_management_container.add_child(payment_row)
	
	# Credit rating
	var rating_row = HBoxContainer.new()
	rating_row.add_theme_constant_override("separation", 8)
	
	var rating_label = Label.new()
	rating_label.text = "Kredittvurdering:"
	rating_label.custom_minimum_size = Vector2(150, 0)
	rating_label.add_theme_color_override("font_color", Color(COLOR_MUTED))
	rating_row.add_child(rating_label)
	
	var rating_value = Label.new()
	rating_value.text = credit_rating
	rating_value.add_theme_font_size_override("font_size", 14)
	rating_value.add_theme_color_override("font_color", _credit_rating_color(credit_rating))
	rating_value.tooltip_text = "Din kredittvurdering påvirker renten du får på nye lån."
	rating_row.add_child(rating_value)
	
	loan_management_container.add_child(rating_row)
	
	# Active loans list
	if active_loans.size() > 0:
		var loans_header = Label.new()
		loans_header.text = "Aktive lån:"
		loans_header.add_theme_font_size_override("font_size", 14)
		loans_header.add_theme_color_override("font_color", Color(COLOR_TEXT))
		loan_management_container.add_child(loans_header)
		
		for loan in active_loans:
			var loan_row = HBoxContainer.new()
			loan_row.add_theme_constant_override("separation", 12)
			
			var loan_name = Label.new()
			loan_name.text = "  🏦 %s" % loan.get("name", "Ukjent lån")
			loan_name.size_flags_horizontal = Control.SIZE_EXPAND_FILL
			loan_name.add_theme_font_size_override("font_size", 12)
			loan_name.add_theme_color_override("font_color", Color(COLOR_TEXT))
			loan_row.add_child(loan_name)
			
			var loan_amount = Label.new()
			loan_amount.text = _format_currency(loan.get("remaining", 0.0))
			loan_amount.add_theme_font_size_override("font_size", 12)
			loan_amount.add_theme_color_override("font_color", Color(COLOR_MUTED))
			loan_row.add_child(loan_amount)
			
			var loan_rate = Label.new()
			loan_rate.text = "%.2f%% rente" % loan.get("interest_rate", 4.5)
			loan_rate.add_theme_font_size_override("font_size", 11)
			loan_rate.add_theme_color_override("font_color", Color(COLOR_MUTED))
			loan_row.add_child(loan_rate)
			
			loan_management_container.add_child(loan_row)
	else:
		var no_loans = Label.new()
		no_loans.text = "✅ Ingen aktive lån"
		no_loans.add_theme_font_size_override("font_size", 12)
		no_loans.add_theme_color_override("font_color", COLOR_GREEN)
		loan_management_container.add_child(no_loans)
	
	# "Take Loan" button
	var button_row = HBoxContainer.new()
	button_row.add_theme_constant_override("separation", 8)
	
	var take_loan_button = Button.new()
	take_loan_button.text = "💳 Ta nytt lån"
	take_loan_button.tooltip_text = "Søk om nytt lån basert på din kredittvurdering og tilgjengelig kreditt."
	take_loan_button.pressed.connect(_on_take_loan_pressed)
	button_row.add_child(take_loan_button)
	
	var available_label = Label.new()
	available_label.text = "(Tilgjengelig kreditt: %s)" % _format_currency(available_credit)
	available_label.add_theme_font_size_override("font_size", 11)
	available_label.add_theme_color_override("font_color", Color(COLOR_MUTED))
	button_row.add_child(available_label)
	
	loan_management_container.add_child(button_row)

# ══════════════════════════════════════════════════════════════════════════════
# 2. TAX OVERVIEW
# ══════════════════════════════════════════════════════════════════════════════

func _update_tax_overview(tax_data: Dictionary) -> void:
	_clear_container(tax_overview_container)
	
	var current_rate = tax_data.get("current_rate", 25.0)
	var last_paid = tax_data.get("last_paid", 0.0)
	var annual_burden = tax_data.get("annual_burden", 0.0)
	var estimated_next = tax_data.get("estimated_next", 0.0)
	var optimization_tips = tax_data.get("optimization_tips", [])
	
	# Current tax rate
	var rate_row = HBoxContainer.new()
	rate_row.add_theme_constant_override("separation", 8)
	
	var rate_label = Label.new()
	rate_label.text = "Gjeldende skattesats:"
	rate_label.custom_minimum_size = Vector2(180, 0)
	rate_label.add_theme_color_override("font_color", Color(COLOR_MUTED))
	rate_row.add_child(rate_label)
	
	var rate_value = Label.new()
	rate_value.text = "%.1f%%" % current_rate
	rate_value.add_theme_font_size_override("font_size", 16)
	rate_value.add_theme_color_override("font_color", Color(COLOR_TEXT))
	rate_value.tooltip_text = "Standard norsk selskapsskatt for oppdrettsnæringen."
	rate_row.add_child(rate_value)
	
	tax_overview_container.add_child(rate_row)
	
	# Last tax paid
	var last_paid_row = HBoxContainer.new()
	last_paid_row.add_theme_constant_override("separation", 8)
	
	var last_paid_label = Label.new()
	last_paid_label.text = "Sist betalt skatt:"
	last_paid_label.custom_minimum_size = Vector2(180, 0)
	last_paid_label.add_theme_color_override("font_color", Color(COLOR_MUTED))
	last_paid_row.add_child(last_paid_label)
	
	var last_paid_value = Label.new()
	last_paid_value.text = _format_currency(last_paid)
	last_paid_value.add_theme_font_size_override("font_size", 14)
	last_paid_value.add_theme_color_override("font_color", Color(COLOR_TEXT))
	last_paid_row.add_child(last_paid_value)
	
	tax_overview_container.add_child(last_paid_row)
	
	# Annual tax burden
	var burden_row = HBoxContainer.new()
	burden_row.add_theme_constant_override("separation", 8)
	
	var burden_label = Label.new()
	burden_label.text = "Årlig skattebelastning:"
	burden_label.custom_minimum_size = Vector2(180, 0)
	burden_label.add_theme_color_override("font_color", Color(COLOR_MUTED))
	burden_row.add_child(burden_label)
	
	var burden_value = Label.new()
	burden_value.text = _format_currency(annual_burden)
	burden_value.add_theme_font_size_override("font_size", 14)
	burden_value.add_theme_color_override("font_color", COLOR_RED)
	burden_row.add_child(burden_value)
	
	tax_overview_container.add_child(burden_row)
	
	# Estimated next payment
	var next_row = HBoxContainer.new()
	next_row.add_theme_constant_override("separation", 8)
	
	var next_label = Label.new()
	next_label.text = "Estimert neste betaling:"
	next_label.custom_minimum_size = Vector2(180, 0)
	next_label.add_theme_color_override("font_color", Color(COLOR_MUTED))
	next_row.add_child(next_label)
	
	var next_value = Label.new()
	next_value.text = _format_currency(estimated_next)
	next_value.add_theme_font_size_override("font_size", 14)
	next_value.add_theme_color_override("font_color", COLOR_YELLOW)
	next_row.add_child(next_value)
	
	tax_overview_container.add_child(next_row)
	
	# Tax optimization tips
	if optimization_tips.size() > 0:
		var tips_header = Label.new()
		tips_header.text = "💡 Skatteoptimaliseringstips:"
		tips_header.add_theme_font_size_override("font_size", 14)
		tips_header.add_theme_color_override("font_color", COLOR_BLUE)
		tax_overview_container.add_child(tips_header)
		
		for tip in optimization_tips:
			var tip_label = Label.new()
			tip_label.text = "  • %s" % tip
			tip_label.add_theme_font_size_override("font_size", 12)
			tip_label.add_theme_color_override("font_color", Color(COLOR_TEXT))
			tip_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
			tip_label.custom_minimum_size = Vector2(400, 0)
			tax_overview_container.add_child(tip_label)

# ══════════════════════════════════════════════════════════════════════════════
# 3. INVESTOR RELATIONS
# ══════════════════════════════════════════════════════════════════════════════

func _update_investor_relations(investors_data: Dictionary) -> void:
	_clear_container(investor_relations_container)
	
	var investors = investors_data.get("investors", [])
	var total_investment = investors_data.get("total_investment", 0.0)
	var equity_dilution = investors_data.get("equity_dilution", 0.0)
	var dividend_paid = investors_data.get("dividend_paid", 0.0)
	var next_dividend = investors_data.get("next_dividend", 0.0)
	
	# Total investment summary
	var investment_row = HBoxContainer.new()
	investment_row.add_theme_constant_override("separation", 8)
	
	var investment_label = Label.new()
	investment_label.text = "Total investering:"
	investment_label.custom_minimum_size = Vector2(180, 0)
	investment_label.add_theme_color_override("font_color", Color(COLOR_MUTED))
	investment_row.add_child(investment_label)
	
	var investment_value = Label.new()
	investment_value.text = _format_currency(total_investment)
	investment_value.add_theme_font_size_override("font_size", 16)
	investment_value.add_theme_color_override("font_color", COLOR_GREEN)
	investment_row.add_child(investment_value)
	
	investor_relations_container.add_child(investment_row)
	
	# Equity dilution
	var dilution_row = HBoxContainer.new()
	dilution_row.add_theme_constant_override("separation", 8)
	
	var dilution_label = Label.new()
	dilution_label.text = "Egenkapitalutvanning:"
	dilution_label.custom_minimum_size = Vector2(180, 0)
	dilution_label.add_theme_color_override("font_color", Color(COLOR_MUTED))
	dilution_row.add_child(dilution_label)
	
	var dilution_value = Label.new()
	dilution_value.text = "%.1f%%" % equity_dilution
	dilution_value.add_theme_font_size_override("font_size", 14)
	dilution_value.add_theme_color_override("font_color", _dilution_color(equity_dilution))
	dilution_value.tooltip_text = "Andel av selskapet som eies av eksterne investorer."
	dilution_row.add_child(dilution_value)
	
	investor_relations_container.add_child(dilution_row)
	
	# Dividend info
	var dividend_row = HBoxContainer.new()
	dividend_row.add_theme_constant_override("separation", 8)
	
	var dividend_label = Label.new()
	dividend_label.text = "Utbetalt utbytte:"
	dividend_label.custom_minimum_size = Vector2(180, 0)
	dividend_label.add_theme_color_override("font_color", Color(COLOR_MUTED))
	dividend_row.add_child(dividend_label)
	
	var dividend_value = Label.new()
	dividend_value.text = _format_currency(dividend_paid)
	dividend_value.add_theme_font_size_override("font_size", 14)
	dividend_value.add_theme_color_override("font_color", Color(COLOR_TEXT))
	dividend_row.add_child(dividend_value)
	
	investor_relations_container.add_child(dividend_row)
	
	# Next dividend
	var next_div_row = HBoxContainer.new()
	next_div_row.add_theme_constant_override("separation", 8)
	
	var next_div_label = Label.new()
	next_div_label.text = "Neste utbytte (estimert):"
	next_div_label.custom_minimum_size = Vector2(180, 0)
	next_div_label.add_theme_color_override("font_color", Color(COLOR_MUTED))
	next_div_row.add_child(next_div_label)
	
	var next_div_value = Label.new()
	next_div_value.text = _format_currency(next_dividend)
	next_div_value.add_theme_font_size_override("font_size", 14)
	next_div_value.add_theme_color_override("font_color", COLOR_BLUE)
	next_div_row.add_child(next_div_value)
	
	investor_relations_container.add_child(next_div_row)
	
	# Investor list
	if investors.size() > 0:
		var investors_header = Label.new()
		investors_header.text = "Investorer:"
		investors_header.add_theme_font_size_override("font_size", 14)
		investors_header.add_theme_color_override("font_color", Color(COLOR_TEXT))
		investor_relations_container.add_child(investors_header)
		
		for investor in investors:
			var investor_row = HBoxContainer.new()
			investor_row.add_theme_constant_override("separation", 12)
			
			var investor_name = Label.new()
			investor_name.text = "  👔 %s" % investor.get("name", "Ukjent investor")
			investor_name.size_flags_horizontal = Control.SIZE_EXPAND_FILL
			investor_name.add_theme_font_size_override("font_size", 12)
			investor_name.add_theme_color_override("font_color", Color(COLOR_TEXT))
			investor_row.add_child(investor_name)
			
			var investor_stake = Label.new()
			investor_stake.text = "%.1f%% eierandel" % investor.get("stake", 0.0)
			investor_stake.add_theme_font_size_override("font_size", 12)
			investor_stake.add_theme_color_override("font_color", Color(COLOR_MUTED))
			investor_row.add_child(investor_stake)
			
			var investor_amount = Label.new()
			investor_amount.text = _format_currency(investor.get("amount", 0.0))
			investor_amount.add_theme_font_size_override("font_size", 11)
			investor_amount.add_theme_color_override("font_color", Color(COLOR_MUTED))
			investor_row.add_child(investor_amount)
			
			investor_relations_container.add_child(investor_row)
	else:
		var no_investors = Label.new()
		no_investors.text = "✅ Ingen eksterne investorer"
		no_investors.add_theme_font_size_override("font_size", 12)
		no_investors.add_theme_color_override("font_color", COLOR_GREEN)
		investor_relations_container.add_child(no_investors)

# ══════════════════════════════════════════════════════════════════════════════
# 4. FINANCIAL HEALTH SUMMARY
# ══════════════════════════════════════════════════════════════════════════════

func _update_financial_health(health_data: Dictionary) -> void:
	_clear_container(financial_health_container)
	
	var debt_to_equity = health_data.get("debt_to_equity_ratio", 0.0)
	var cash_runway_days = health_data.get("cash_runway_days", 365)
	var credit_rating = health_data.get("credit_rating", "A")
	var profitability_trend = health_data.get("profitability_trend", "stable")
	var liquidity_ratio = health_data.get("liquidity_ratio", 1.5)
	
	# Debt-to-equity ratio
	var de_row = HBoxContainer.new()
	de_row.add_theme_constant_override("separation", 8)
	
	var de_label = Label.new()
	de_label.text = "Gjeld-til-egenkapital:"
	de_label.custom_minimum_size = Vector2(180, 0)
	de_label.add_theme_color_override("font_color", Color(COLOR_MUTED))
	de_row.add_child(de_label)
	
	var de_value = Label.new()
	de_value.text = "%.2f" % debt_to_equity
	de_value.add_theme_font_size_override("font_size", 16)
	de_value.add_theme_color_override("font_color", _debt_to_equity_color(debt_to_equity))
	de_value.tooltip_text = "Lav verdi (<0.5) = sunn balanse. Høy verdi (>2.0) = høy gjeldsrisiko."
	de_row.add_child(de_value)
	
	var de_status = Label.new()
	de_status.text = _debt_to_equity_status(debt_to_equity)
	de_status.add_theme_font_size_override("font_size", 12)
	de_status.add_theme_color_override("font_color", Color(COLOR_MUTED))
	de_row.add_child(de_status)
	
	financial_health_container.add_child(de_row)
	
	# Cash runway
	var runway_row = HBoxContainer.new()
	runway_row.add_theme_constant_override("separation", 8)
	
	var runway_label = Label.new()
	runway_label.text = "Kontantreserve (dager):"
	runway_label.custom_minimum_size = Vector2(180, 0)
	runway_label.add_theme_color_override("font_color", Color(COLOR_MUTED))
	runway_row.add_child(runway_label)
	
	var runway_value = Label.new()
	runway_value.text = "%d dager" % cash_runway_days
	runway_value.add_theme_font_size_override("font_size", 14)
	runway_value.add_theme_color_override("font_color", _cash_runway_color(cash_runway_days))
	runway_value.tooltip_text = "Hvor lenge du kan dekke utgifter med nåværende kontanter."
	runway_row.add_child(runway_value)
	
	financial_health_container.add_child(runway_row)
	
	# Credit rating indicator
	var rating_row = HBoxContainer.new()
	rating_row.add_theme_constant_override("separation", 8)
	
	var rating_label = Label.new()
	rating_label.text = "Kredittvurdering:"
	rating_label.custom_minimum_size = Vector2(180, 0)
	rating_label.add_theme_color_override("font_color", Color(COLOR_MUTED))
	rating_row.add_child(rating_label)
	
	var rating_value = Label.new()
	rating_value.text = credit_rating
	rating_value.add_theme_font_size_override("font_size", 16)
	rating_value.add_theme_color_override("font_color", _credit_rating_color(credit_rating))
	rating_row.add_child(rating_value)
	
	var rating_desc = Label.new()
	rating_desc.text = _credit_rating_description(credit_rating)
	rating_desc.add_theme_font_size_override("font_size", 12)
	rating_desc.add_theme_color_override("font_color", Color(COLOR_MUTED))
	rating_row.add_child(rating_desc)
	
	financial_health_container.add_child(rating_row)
	
	# Liquidity ratio
	var liquidity_row = HBoxContainer.new()
	liquidity_row.add_theme_constant_override("separation", 8)
	
	var liquidity_label = Label.new()
	liquidity_label.text = "Likviditetsgrad:"
	liquidity_label.custom_minimum_size = Vector2(180, 0)
	liquidity_label.add_theme_color_override("font_color", Color(COLOR_MUTED))
	liquidity_row.add_child(liquidity_label)
	
	var liquidity_value = Label.new()
	liquidity_value.text = "%.2f" % liquidity_ratio
	liquidity_value.add_theme_font_size_override("font_size", 14)
	liquidity_value.add_theme_color_override("font_color", _liquidity_color(liquidity_ratio))
	liquidity_value.tooltip_text = "Omløpsmidler / kortsiktig gjeld. >1.0 = god likviditet."
	liquidity_row.add_child(liquidity_value)
	
	financial_health_container.add_child(liquidity_row)
	
	# Profitability trend
	var trend_row = HBoxContainer.new()
	trend_row.add_theme_constant_override("separation", 8)
	
	var trend_label = Label.new()
	trend_label.text = "Lønnsomhetstrend:"
	trend_label.custom_minimum_size = Vector2(180, 0)
	trend_label.add_theme_color_override("font_color", Color(COLOR_MUTED))
	trend_row.add_child(trend_label)
	
	var trend_value = Label.new()
	trend_value.text = _trend_icon(profitability_trend) + " " + _trend_label(profitability_trend)
	trend_value.add_theme_font_size_override("font_size", 14)
	trend_value.add_theme_color_override("font_color", _trend_color(profitability_trend))
	trend_row.add_child(trend_value)
	
	financial_health_container.add_child(trend_row)

# ══════════════════════════════════════════════════════════════════════════════
# SAFE DATA GETTERS (with fallback placeholders)
# ══════════════════════════════════════════════════════════════════════════════

func _get_loans_safe(sim_bridge: Node) -> Dictionary:
	if sim_bridge.has_method("get_loans_data"):
		return sim_bridge.get_loans_data()
	else:
		# Placeholder
		return {
			"active_loans": [
				{"name": "Investeringslån 2024", "remaining": 25_000_000.0, "interest_rate": 4.2},
				{"name": "Driftskreditt", "remaining": 8_500_000.0, "interest_rate": 5.8}
			],
			"total_debt": 33_500_000.0,
			"monthly_payment": 425_000.0,
			"credit_rating": "A-",
			"available_credit": 45_000_000.0
		}

func _get_tax_data_safe(sim_bridge: Node) -> Dictionary:
	if sim_bridge.has_method("get_tax_data"):
		return sim_bridge.get_tax_data()
	else:
		# Placeholder
		return {
			"current_rate": 25.0,
			"last_paid": 3_200_000.0,
			"annual_burden": 12_800_000.0,
			"estimated_next": 3_500_000.0,
			"optimization_tips": [
				"Invester i forsknings- og utviklingsfradrag",
				"Vurder regionale skattefordeler i Nord-Norge",
				"Optimaliser avskrivninger på nytt utstyr"
			]
		}

func _get_investors_safe(sim_bridge: Node) -> Dictionary:
	if sim_bridge.has_method("get_investors_data"):
		return sim_bridge.get_investors_data()
	else:
		# Placeholder
		return {
			"investors": [
				{"name": "Nordic Aqua Ventures", "stake": 15.0, "amount": 45_000_000.0},
				{"name": "Innovasjon Norge", "stake": 5.0, "amount": 15_000_000.0}
			],
			"total_investment": 60_000_000.0,
			"equity_dilution": 20.0,
			"dividend_paid": 2_400_000.0,
			"next_dividend": 3_000_000.0
		}

func _get_financial_health_safe(sim_bridge: Node) -> Dictionary:
	if sim_bridge.has_method("get_financial_health"):
		return sim_bridge.get_financial_health()
	else:
		# Placeholder
		return {
			"debt_to_equity_ratio": 0.65,
			"cash_runway_days": 185,
			"credit_rating": "A-",
			"profitability_trend": "improving",
			"liquidity_ratio": 1.4
		}

# ══════════════════════════════════════════════════════════════════════════════
# BUTTON HANDLERS
# ══════════════════════════════════════════════════════════════════════════════

func _on_take_loan_pressed() -> void:
	print("FinanceDeepPanel: Take loan button pressed")
	# TODO: Open loan dialog when implemented
	# For now, emit signal with placeholder values
	emit_signal("loan_requested", 10_000_000.0, 60)

# ══════════════════════════════════════════════════════════════════════════════
# HELPER FUNCTIONS
# ══════════════════════════════════════════════════════════════════════════════

func _clear_container(container: VBoxContainer) -> void:
	for child in container.get_children():
		child.queue_free()

func _format_currency(value: float) -> String:
	if value >= 1_000_000_000:
		return "%.2f mrd NOK" % (value / 1_000_000_000.0)
	elif value >= 1_000_000:
		return "%.1f mill NOK" % (value / 1_000_000.0)
	elif value >= 1_000:
		return "%.0f k NOK" % (value / 1_000.0)
	else:
		return "%.0f NOK" % value

# ── Color helpers ─────────────────────────────────────────────────────────────

func _debt_color(debt: float) -> Color:
	if debt > 100_000_000:
		return COLOR_RED
	elif debt > 50_000_000:
		return COLOR_YELLOW
	else:
		return COLOR_GREEN

func _credit_rating_color(rating: String) -> Color:
	match rating:
		"AAA", "AA+", "AA", "AA-":
			return COLOR_GREEN
		"A+", "A", "A-":
			return COLOR_YELLOW
		_:
			return COLOR_RED

func _credit_rating_description(rating: String) -> String:
	match rating:
		"AAA", "AA+", "AA", "AA-":
			return "(Utmerket)"
		"A+", "A", "A-":
			return "(God)"
		"BBB+", "BBB", "BBB-":
			return "(Akseptabel)"
		_:
			return "(Risikofylt)"

func _dilution_color(pct: float) -> Color:
	if pct < 25.0:
		return COLOR_GREEN
	elif pct < 50.0:
		return COLOR_YELLOW
	else:
		return COLOR_RED

func _debt_to_equity_color(ratio: float) -> Color:
	if ratio < 0.5:
		return COLOR_GREEN
	elif ratio < 1.5:
		return COLOR_YELLOW
	else:
		return COLOR_RED

func _debt_to_equity_status(ratio: float) -> String:
	if ratio < 0.5:
		return "(Sunn)"
	elif ratio < 1.0:
		return "(Moderat)"
	elif ratio < 2.0:
		return "(Forhøyet)"
	else:
		return "(Høy risiko)"

func _cash_runway_color(days: int) -> Color:
	if days > 180:
		return COLOR_GREEN
	elif days > 90:
		return COLOR_YELLOW
	else:
		return COLOR_RED

func _liquidity_color(ratio: float) -> Color:
	if ratio >= 1.5:
		return COLOR_GREEN
	elif ratio >= 1.0:
		return COLOR_YELLOW
	else:
		return COLOR_RED

func _trend_color(trend: String) -> Color:
	match trend:
		"improving":
			return COLOR_GREEN
		"stable":
			return COLOR_YELLOW
		"declining":
			return COLOR_RED
		_:
			return Color(COLOR_MUTED)

func _trend_icon(trend: String) -> String:
	match trend:
		"improving":
			return "📈"
		"stable":
			return "➡️"
		"declining":
			return "📉"
		_:
			return "❓"

func _trend_label(trend: String) -> String:
	match trend:
		"improving":
			return "Bedring"
		"stable":
			return "Stabil"
		"declining":
			return "Nedgang"
		_:
			return "Ukjent"
