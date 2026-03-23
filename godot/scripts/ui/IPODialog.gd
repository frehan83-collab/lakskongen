extends AcceptDialog
## IPODialog - IPO decision interface
## Allows player to go public on Oslo Stock Exchange

@onready var eligibility_label = $MarginContainer/VBoxContainer/EligibilityLabel
@onready var valuation_label = $MarginContainer/VBoxContainer/ValuationLabel
@onready var dilution_slider = $MarginContainer/VBoxContainer/DilutionControl/Slider
@onready var dilution_value_label = $MarginContainer/VBoxContainer/DilutionControl/ValueLabel
@onready var price_min_input = $MarginContainer/VBoxContainer/PriceRange/MinInput
@onready var price_max_input = $MarginContainer/VBoxContainer/PriceRange/MaxInput
@onready var capital_raised_label = $MarginContainer/VBoxContainer/CapitalRaisedLabel
@onready var go_public_button = $MarginContainer/VBoxContainer/GoPublicButton
@onready var warning_label = $MarginContainer/VBoxContainer/WarningLabel

var ipo_eligibility: Dictionary = {}
var current_valuation: float = 0.0

func _ready():
	title = "Go Public - IPO Decision"
	dialog_hide_on_ok = false
	
	dilution_slider.min_value = 10
	dilution_slider.max_value = 30
	dilution_slider.step = 1
	dilution_slider.value = 20
	dilution_slider.value_changed.connect(_on_dilution_changed)
	
	price_min_input.text_changed.connect(_on_price_changed)
	price_max_input.text_changed.connect(_on_price_changed)
	go_public_button.pressed.connect(_on_go_public_pressed)
	
	_check_eligibility()

func _check_eligibility():
	if not SimBridge:
		push_error("SimBridge not found")
		return
	
	SimBridge.call_deferred("GetIPOEligibility")

func _on_ipo_eligibility_updated(eligibility_json: String):
	var json = JSON.new()
	var parse_result = json.parse(eligibility_json)
	
	if parse_result != OK:
		push_error("Failed to parse IPO eligibility JSON")
		return
	
	ipo_eligibility = json.data
	_update_eligibility_display()

func _update_eligibility_display():
	var is_eligible = ipo_eligibility.get("eligible", false)
	var reasons = ipo_eligibility.get("reasons", [])
	current_valuation = ipo_eligibility.get("valuation", 0.0)
	
	# Eligibility status
	if is_eligible:
		eligibility_label.text = "✓ ELIGIBLE for IPO"
		eligibility_label.add_theme_color_override("font_color", Color("#2AB7B5"))
		go_public_button.disabled = false
	else:
		eligibility_label.text = "✗ NOT ELIGIBLE for IPO"
		eligibility_label.add_theme_color_override("font_color", Color("#E06666"))
		go_public_button.disabled = true
		
		# Show reasons
		var reason_text = "\nRequirements:\n"
		for reason in reasons:
			reason_text += "  • " + reason + "\n"
		warning_label.text = reason_text
		warning_label.visible = true
	
	# Valuation
	var valuation_b = current_valuation / 1_000_000_000.0
	valuation_label.text = "Company Valuation: NOK %.2fB" % valuation_b
	
	_update_capital_raised()

func _on_dilution_changed(value: float):
	dilution_value_label.text = "%.0f%%" % value
	_update_capital_raised()

func _on_price_changed(_new_text: String):
	_update_capital_raised()

func _update_capital_raised():
	var dilution = dilution_slider.value / 100.0
	var shares_sold = dilution * current_valuation
	
	# Estimate capital raised (simplified - actual depends on price range)
	var capital = shares_sold
	var capital_b = capital / 1_000_000_000.0
	
	capital_raised_label.text = "Capital Raised: ~NOK %.2fB (%.0f%% dilution)" % [capital_b, dilution * 100]
	
	# Warning for high dilution
	if dilution > 0.25:
		warning_label.text = "WARNING: High dilution (>25%) may signal desperation to investors!"
		warning_label.add_theme_color_override("font_color", Color("#E06666"))
		warning_label.visible = true
	else:
		warning_label.visible = false

func _on_go_public_pressed():
	var dilution = dilution_slider.value / 100.0
	var price_min = float(price_min_input.text) if price_min_input.text != "" else 50.0
	var price_max = float(price_max_input.text) if price_max_input.text != "" else 70.0
	
	# Validation
	if price_min >= price_max:
		_show_error("Price range invalid: Min must be less than Max")
		return
	
	if price_min < 10.0:
		_show_error("Price too low: Minimum share price should be at least NOK 10")
		return
	
	# Confirmation dialog
	var confirm = ConfirmationDialog.new()
	var capital_b = (dilution * current_valuation) / 1_000_000_000.0
	confirm.dialog_text = """Going Public:
	
• Offering: %.0f%% of company
• Price Range: NOK %.2f - %.2f per share
• Capital Raised: ~NOK %.2fB

Consequences:
• Quarterly earnings pressure
• Analyst scrutiny
• Stock price volatility
• New strategic options (acquisitions, buybacks)

This is irreversible. Proceed?""" % [dilution * 100, price_min, price_max, capital_b]
	
	confirm.title = "Confirm IPO"
	confirm.confirmed.connect(_execute_ipo.bind(dilution, price_min, price_max))
	add_child(confirm)
	confirm.popup_centered()

func _execute_ipo(dilution: float, price_min: float, price_max: float):
	if not SimBridge:
		return
	
	# Prepare IPO parameters
	var ipo_params = {
		"offeringSize": dilution,
		"priceMin": price_min,
		"priceMax": price_max
	}
	
	var params_json = JSON.stringify(ipo_params)
	SimBridge.call_deferred("LaunchIPO", params_json)
	
	# Close dialog
	hide()
	
	# Show result notification (actual result via GameBridge signal)
	var result = AcceptDialog.new()
	result.dialog_text = "IPO launched! Investment banks are pricing your offering. Check the stock market panel for results."
	result.title = "IPO in Progress"
	get_parent().add_child(result)
	result.popup_centered()

func _on_ipo_result(result_json: String):
	var json = JSON.new()
	var parse_result = json.parse(result_json)
	
	if parse_result != OK:
		return
	
	var result = json.data
	var success = result.get("success", false)
	
	var notification = AcceptDialog.new()
	if success:
		var price = result.get("sharePrice", 0.0)
		var market_cap = result.get("marketCap", 0.0) / 1_000_000_000.0
		var capital = result.get("capitalRaised", 0.0) / 1_000_000_000.0
		var ticker = result.get("ticker", "LAKS")
		
		notification.dialog_text = """IPO SUCCESS!

Ticker: %s
Share Price: NOK %.2f
Market Cap: NOK %.2fB
Capital Raised: NOK %.2fB

You are now a public company. Good luck!""" % [ticker, price, market_cap, capital]
		notification.title = "🎉 IPO Complete"
	else:
		var message = result.get("message", "IPO failed")
		notification.dialog_text = "IPO failed: " + message
		notification.title = "IPO Failed"
	
	get_parent().add_child(notification)
	notification.popup_centered()

func _show_error(message: String):
	var error_dialog = AcceptDialog.new()
	error_dialog.dialog_text = message
	error_dialog.title = "Error"
	add_child(error_dialog)
	error_dialog.popup_centered()

func _on_confirmed():
	# Override default OK behavior
	pass
