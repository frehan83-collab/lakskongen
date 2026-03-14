## FinancePanel.gd
## Financial dashboard showing revenue, expenses, profit, loans, and financial history.
## Wired to GameBridge finance APIs for live data display.

extends PanelContainer
class_name FinancePanel

# UI References
@onready var revenue_label: Label
@onready var expenses_label: Label
@onready var profit_label: Label
@onready var balance_label: Label

# Loan section
@onready var loan_status_label: Label
@onready var loan_amount_label: Label
@onready var loan_interest_label: Label
@onready var loan_remaining_label: Label
@onready var take_loan_button: Button
@onready var repay_loan_button: Button

# Financial history chart container
@onready var history_container: VBoxContainer

# Color scheme
const COLOR_POSITIVE = Color(0.3, 0.9, 0.3)
const COLOR_NEGATIVE = Color(0.9, 0.3, 0.3)
const COLOR_NEUTRAL = Color(0.85, 0.85, 0.85)
const COLOR_TEXT = "#E6EEF3"
const COLOR_MUTED = "#9AA7B2"

signal loan_taken(amount: float)
signal loan_repaid(amount: float)

func _ready():
	# Create UI elements if not assigned
	if not revenue_label:
		_create_ui()
	
	# Connect to SimBridge for live data updates
	if has_node("/root/SimBridge"):
		var sim_bridge = get_node("/root/SimBridge")
		sim_bridge.state_updated.connect(_on_state_updated)
		print("FinancePanel: connected to SimBridge")
	else:
		push_warning("FinancePanel: SimBridge not found")
	
	# Connect button signals
	if take_loan_button:
		take_loan_button.pressed.connect(_on_take_loan_pressed)
	if repay_loan_button:
		repay_loan_button.pressed.connect(_on_repay_loan_pressed)
	
	# Initial refresh
	_refresh_from_bridge()

func _create_ui():
	"""Dynamically create UI elements if not set in editor"""
	
	var margin = MarginContainer.new()
	margin.add_theme_constant_override("margin_top", 16)
	margin.add_theme_constant_override("margin_bottom", 16)
	margin.add_theme_constant_override("margin_left", 16)
	margin.add_theme_constant_override("margin_right", 16)
	add_child(margin)
	
	var vbox = VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 16)
	margin.add_child(vbox)
	
	# Header
	var header = Label.new()
	header.text = "💰 Finance Dashboard"
	header.add_theme_font_size_override("font_size", 20)
	header.add_theme_color_override("font_color", Color(COLOR_TEXT))
	header.tooltip_text = "Økonomisk oversikt: inntekter, kostnader, fortjeneste, lån og finanshistorikk."
	vbox.add_child(header)
	
	# Financial summary section
	var summary_container = VBoxContainer.new()
	summary_container.add_theme_constant_override("separation", 8)
	vbox.add_child(summary_container)
	
	# Balance
	balance_label = _create_stat_row(summary_container, "Current Balance:")
	
	# Revenue
	revenue_label = _create_stat_row(summary_container, "Revenue (Period):")
	
	# Expenses
	expenses_label = _create_stat_row(summary_container, "Expenses (Period):")
	
	# Profit
	profit_label = _create_stat_row(summary_container, "Profit (Period):")
	
	# Separator
	var separator1 = HSeparator.new()
	vbox.add_child(separator1)
	
	# Loan section header
	var loan_header = Label.new()
	loan_header.text = "Loan Status"
	loan_header.add_theme_font_size_override("font_size", 16)
	loan_header.add_theme_color_override("font_color", Color(COLOR_TEXT))
	vbox.add_child(loan_header)
	
	var loan_container = VBoxContainer.new()
	loan_container.add_theme_constant_override("separation", 8)
	vbox.add_child(loan_container)
	
	loan_status_label = _create_stat_row(loan_container, "Status:")
	loan_amount_label = _create_stat_row(loan_container, "Loan Amount:")
	loan_interest_label = _create_stat_row(loan_container, "Interest Rate:")
	loan_remaining_label = _create_stat_row(loan_container, "Remaining:")
	
	# Loan buttons
	var loan_btn_container = HBoxContainer.new()
	loan_btn_container.add_theme_constant_override("separation", 12)
	vbox.add_child(loan_btn_container)
	
	take_loan_button = Button.new()
	take_loan_button.text = "Take Loan (5M NOK)"
	take_loan_button.tooltip_text = "Ta opp et nytt lån på 5 millioner NOK."
	loan_btn_container.add_child(take_loan_button)
	
	repay_loan_button = Button.new()
	repay_loan_button.text = "Repay Loan (1M NOK)"
	repay_loan_button.tooltip_text = "Nedbetal 1 million NOK på eksisterende lån."
	loan_btn_container.add_child(repay_loan_button)
	
	# Separator
	var separator2 = HSeparator.new()
	vbox.add_child(separator2)
	
	# Financial history header
	var history_header = Label.new()
	history_header.text = "Recent Financial History (Last 30 Days)"
	history_header.add_theme_font_size_override("font_size", 16)
	history_header.add_theme_color_override("font_color", Color(COLOR_TEXT))
	vbox.add_child(history_header)
	
	# History container (will be populated with data)
	history_container = VBoxContainer.new()
	history_container.add_theme_constant_override("separation", 4)
	vbox.add_child(history_container)

func _create_stat_row(parent: Node, label_text: String) -> Label:
	"""Create a stat row with label and value"""
	var hbox = HBoxContainer.new()
	hbox.add_theme_constant_override("separation", 12)
	parent.add_child(hbox)
	
	var label = Label.new()
	label.text = label_text
	label.custom_minimum_size = Vector2(200, 0)
	label.add_theme_font_size_override("font_size", 14)
	label.add_theme_color_override("font_color", Color(COLOR_MUTED))
	hbox.add_child(label)
	
	var value = Label.new()
	value.text = "--"
	value.add_theme_font_size_override("font_size", 14)
	value.add_theme_color_override("font_color", Color(COLOR_TEXT))
	hbox.add_child(value)
	
	return value

func _on_state_updated():
	"""Called when SimBridge state updates"""
	_refresh_from_bridge()

func _refresh_from_bridge():
	"""Pull live data from SimBridge and update display"""
	if not has_node("/root/SimBridge"):
		return
	
	var sim_bridge = get_node("/root/SimBridge")
	
	# Get financial data
	var revenue = sim_bridge.get_revenue()
	var expenses = sim_bridge.get_expenses()
	var profit = sim_bridge.get_profit()
	var balance = sim_bridge.balance
	
	# Update summary
	if balance_label:
		balance_label.text = _format_nok(balance)
		balance_label.add_theme_color_override("font_color", 
			COLOR_POSITIVE if balance >= 0 else COLOR_NEGATIVE)
	
	if revenue_label:
		revenue_label.text = _format_nok(revenue)
		revenue_label.add_theme_color_override("font_color", COLOR_POSITIVE)
	
	if expenses_label:
		expenses_label.text = _format_nok(expenses)
		expenses_label.add_theme_color_override("font_color", COLOR_NEGATIVE)
	
	if profit_label:
		profit_label.text = _format_nok(profit)
		profit_label.add_theme_color_override("font_color", 
			COLOR_POSITIVE if profit >= 0 else COLOR_NEGATIVE)
	
	# Get and update loan info
	var loan_info = sim_bridge.get_loan_info()
	_update_loan_display(loan_info)
	
	# Get and update financial history
	var history = sim_bridge.get_financial_history(30)
	_update_history_display(history)

func _update_loan_display(loan_info: Dictionary):
	"""Update loan status display"""
	var has_loan = loan_info.get("has_loan", false)
	
	if loan_status_label:
		loan_status_label.text = "Active" if has_loan else "No Active Loan"
		loan_status_label.add_theme_color_override("font_color", 
			Color(1.0, 0.8, 0.2) if has_loan else COLOR_NEUTRAL)
	
	if has_loan:
		if loan_amount_label:
			loan_amount_label.text = _format_nok(loan_info.get("amount", 0.0))
		
		if loan_interest_label:
			loan_interest_label.text = "%.2f%%" % (loan_info.get("interest_rate", 0.0) * 100)
		
		if loan_remaining_label:
			loan_remaining_label.text = _format_nok(loan_info.get("remaining", 0.0))
			loan_remaining_label.add_theme_color_override("font_color", COLOR_NEGATIVE)
		
		if take_loan_button:
			take_loan_button.disabled = true
		if repay_loan_button:
			repay_loan_button.disabled = false
	else:
		if loan_amount_label:
			loan_amount_label.text = "--"
		if loan_interest_label:
			loan_interest_label.text = "--"
		if loan_remaining_label:
			loan_remaining_label.text = "--"
		
		if take_loan_button:
			take_loan_button.disabled = false
		if repay_loan_button:
			repay_loan_button.disabled = true

func _update_history_display(history: Array):
	"""Update financial history display"""
	if not history_container:
		return
	
	# Clear existing entries
	for child in history_container.get_children():
		child.queue_free()
	
	# Show last 10 entries (most recent first)
	var entries_to_show = min(10, history.size())
	for i in range(history.size() - entries_to_show, history.size()):
		if i < 0 or i >= history.size():
			continue
		
		var entry = history[i]
		var day = entry.get("day", 0)
		var entry_revenue = entry.get("revenue", 0.0)
		var entry_expenses = entry.get("expenses", 0.0)
		var entry_balance = entry.get("balance", 0.0)
		
		var hbox = HBoxContainer.new()
		hbox.add_theme_constant_override("separation", 16)
		history_container.add_child(hbox)
		
		# Day
		var day_lbl = Label.new()
		day_lbl.text = "Day %d" % day
		day_lbl.custom_minimum_size = Vector2(80, 0)
		day_lbl.add_theme_font_size_override("font_size", 12)
		day_lbl.add_theme_color_override("font_color", Color(COLOR_MUTED))
		hbox.add_child(day_lbl)
		
		# Revenue
		var rev_lbl = Label.new()
		rev_lbl.text = "Rev: %s" % _format_nok_short(entry_revenue)
		rev_lbl.custom_minimum_size = Vector2(120, 0)
		rev_lbl.add_theme_font_size_override("font_size", 11)
		rev_lbl.add_theme_color_override("font_color", COLOR_POSITIVE)
		hbox.add_child(rev_lbl)
		
		# Expenses
		var exp_lbl = Label.new()
		exp_lbl.text = "Exp: %s" % _format_nok_short(entry_expenses)
		exp_lbl.custom_minimum_size = Vector2(120, 0)
		exp_lbl.add_theme_font_size_override("font_size", 11)
		exp_lbl.add_theme_color_override("font_color", COLOR_NEGATIVE)
		hbox.add_child(exp_lbl)
		
		# Balance
		var bal_lbl = Label.new()
		bal_lbl.text = "Bal: %s" % _format_nok_short(entry_balance)
		bal_lbl.add_theme_font_size_override("font_size", 11)
		bal_lbl.add_theme_color_override("font_color", 
			COLOR_POSITIVE if entry_balance >= 0 else COLOR_NEGATIVE)
		hbox.add_child(bal_lbl)

func _on_take_loan_pressed():
	"""Handle take loan button press"""
	if not has_node("/root/SimBridge"):
		return
	
	var sim_bridge = get_node("/root/SimBridge")
	var loan_amount = 5_000_000.0  # 5M NOK
	
	var success = sim_bridge.take_loan(loan_amount)
	if success:
		print("FinancePanel: Loan taken - %.0f NOK" % loan_amount)
		emit_signal("loan_taken", loan_amount)
	else:
		print("FinancePanel: Failed to take loan")

func _on_repay_loan_pressed():
	"""Handle repay loan button press"""
	if not has_node("/root/SimBridge"):
		return
	
	var sim_bridge = get_node("/root/SimBridge")
	var repay_amount = 1_000_000.0  # 1M NOK
	
	var success = sim_bridge.repay_loan(repay_amount)
	if success:
		print("FinancePanel: Loan repaid - %.0f NOK" % repay_amount)
		emit_signal("loan_repaid", repay_amount)
	else:
		print("FinancePanel: Failed to repay loan (insufficient funds or no loan)")

func _format_nok(amount: float) -> String:
	"""Format NOK amount with M/k suffix"""
	var abs_amt = abs(amount)
	var sign = "-" if amount < 0.0 else ""
	
	if abs_amt >= 1_000_000.0:
		return "%s%.2f MNOK" % [sign, abs_amt / 1_000_000.0]
	elif abs_amt >= 1_000.0:
		return "%s%.0f kNOK" % [sign, abs_amt / 1_000.0]
	return "%s%.0f NOK" % [sign, abs_amt]

func _format_nok_short(amount: float) -> String:
	"""Format NOK amount with M/k suffix (shorter version)"""
	var abs_amt = abs(amount)
	var sign = "-" if amount < 0.0 else ""
	
	if abs_amt >= 1_000_000.0:
		return "%s%.1fM" % [sign, abs_amt / 1_000_000.0]
	elif abs_amt >= 1_000.0:
		return "%s%.0fk" % [sign, abs_amt / 1_000.0]
	return "%s%.0f" % [sign, abs_amt]
