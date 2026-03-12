# InternationalMarketsPanel.gd
# UI for international market expansion in Lakskongen

extends PanelContainer
class_name InternationalMarketsPanel

@onready var market_list: ItemList = $VBox/MarketList
@onready var details_panel: PanelContainer = $VBox/Details
@onready var details_label: RichTextLabel = $VBox/Details/MarginContainer/DetailsText
@onready var enter_button: Button = $VBox/Details/MarginContainer/VBox/EnterMarketButton
@onready var sell_button: Button = $VBox/Details/MarginContainer/VBox/SellToMarketButton

# Mock data - will be replaced with C# integration
var markets_data: Array = []
var selected_market: Dictionary = {}
var player_reputation: float = 0.75
var player_certifications: Array = ["ASC", "EU_Compliance"]
var player_cash_nok: float = 50_000_000.0

func _ready() -> void:
	load_markets_data()
	refresh_market_list()
	
	market_list.item_selected.connect(_on_market_selected)
	enter_button.pressed.connect(_on_enter_market_pressed)
	sell_button.pressed.connect(_on_sell_to_market_pressed)

func load_markets_data() -> void:
	var markets_path = "res://game/data/international_markets.json"
	if not FileAccess.file_exists(markets_path):
		push_warning("International markets data not found: " + markets_path)
		return
	
	var file = FileAccess.open(markets_path, FileAccess.READ)
	if file:
		var json_string = file.get_as_text()
		file.close()
		
		var json = JSON.new()
		var parse_result = json.parse(json_string)
		if parse_result == OK:
			var data = json.data
			if data.has("markets"):
				markets_data = data["markets"]
		else:
			push_error("Failed to parse international_markets.json")

func refresh_market_list() -> void:
	market_list.clear()
	
	for market in markets_data:
		var accessible = check_market_accessible(market)
		var status_icon = "✅" if market.get("player_has_access", false) else ("🔓" if accessible else "🔒")
		
		var label = "%s %s - %s\n  Premium: +%.0f%% | Entry: %.1fM NOK" % [
			status_icon,
			market["region"],
			market["name"],
			(market["premium_multiplier"] - 1.0) * 100,
			market["entry_investment_nok"] / 1_000_000.0
		]
		market_list.add_item(label)

func check_market_accessible(market: Dictionary) -> bool:
	# Check reputation
	if player_reputation < market["min_reputation_score"]:
		return false
	
	# Check certifications
	var required_certs = market.get("required_certifications", [])
	for cert in required_certs:
		if not player_certifications.has(cert):
			return false
	
	return true

func _on_market_selected(index: int) -> void:
	if index < 0 or index >= markets_data.size():
		return
	
	selected_market = markets_data[index]
	display_market_details(selected_market)

func display_market_details(market: Dictionary) -> void:
	var accessible = check_market_accessible(market)
	var has_access = market.get("player_has_access", false)
	
	var details_text = "[b]%s[/b]\n" % market["name"]
	details_text += "Region: %s\n\n" % market["region"]
	
	details_text += "[b]Economics:[/b]\n"
	details_text += "Base Price: %.2f NOK/kg\n" % market["base_price_nok"]
	details_text += "Premium: +%.0f%%\n" % ((market["premium_multiplier"] - 1.0) * 100)
	details_text += "Logistics Cost: %.0f%%\n" % (market["logistics_cost_pct"] * 100)
	details_text += "Tariff: %.0f%%\n\n" % (market["tariff_rate"] * 100)
	
	var base_price = market["base_price_nok"]
	var premium_price = base_price * market["premium_multiplier"]
	var logistics = premium_price * market["logistics_cost_pct"]
	var tariff = premium_price * market["tariff_rate"]
	var net_price = premium_price - logistics - tariff
	
	details_text += "Net Price: [b]%.2f NOK/kg[/b]\n" % net_price
	details_text += "  (vs. %.2f NOK domestic)\n\n" % base_price
	
	details_text += "[b]Entry Requirements:[/b]\n"
	details_text += "Investment: %s NOK\n" % _format_number(market["entry_investment_nok"])
	details_text += "Regulatory Approval: %d months\n" % market["regulatory_approval_months"]
	details_text += "Min. Reputation: %.0f%%\n" % (market["min_reputation_score"] * 100)
	
	var required_certs = market.get("required_certifications", [])
	if required_certs.size() > 0:
		details_text += "Certifications: %s\n" % ", ".join(required_certs)
		
		# Check missing certs
		var missing_certs = []
		for cert in required_certs:
			if not player_certifications.has(cert):
				missing_certs.append(cert)
		
		if missing_certs.size() > 0:
			details_text += "  [color=red]Missing: %s[/color]\n" % ", ".join(missing_certs)
	
	if has_access:
		details_text += "\n[color=green]✅ Market Entered[/color]\n"
		details_text += "Player Market Share: %.1f%%\n" % (market.get("player_market_share", 0.01) * 100)
	elif accessible:
		details_text += "\n[color=yellow]🔓 Ready to Enter[/color]"
	else:
		details_text += "\n[color=red]🔒 Requirements Not Met[/color]"
	
	details_label.text = details_text
	details_panel.visible = true
	
	enter_button.disabled = has_access or not accessible or player_cash_nok < market["entry_investment_nok"]
	enter_button.text = "Enter Market" if not has_access else "Already Entered"
	
	sell_button.disabled = not has_access
	sell_button.visible = has_access

func _on_enter_market_pressed() -> void:
	if selected_market.is_empty():
		return
	
	var investment = selected_market["entry_investment_nok"]
	
	# Confirmation dialog would go here in real implementation
	print("Entering market: %s with investment %.0f NOK" % [selected_market["name"], investment])
	
	# Mark as entered (in real implementation, this would call C# system)
	selected_market["player_has_access"] = true
	selected_market["player_market_share"] = 0.01
	player_cash_nok -= investment
	
	refresh_market_list()
	display_market_details(selected_market)

func _on_sell_to_market_pressed() -> void:
	if selected_market.is_empty() or not selected_market.get("player_has_access", false):
		return
	
	# Would open sell dialog in real implementation
	print("Selling to market: %s" % selected_market["name"])

func _format_number(value: float) -> String:
	if value >= 1_000_000:
		return "%.2fM" % (value / 1_000_000.0)
	elif value >= 1_000:
		return "%.1fK" % (value / 1_000.0)
	else:
		return "%.0f" % value
