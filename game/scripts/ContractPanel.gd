# ContractPanel.gd
# UI for contract warfare system in Lakskongen

extends PanelContainer
class_name ContractPanel

@onready var contract_system = get_node("/root/ContractSystem") if has_node("/root/ContractSystem") else null
@onready var available_list: ItemList = $VBox/AvailableContracts/List
@onready var active_list: ItemList = $VBox/ActiveContracts/List
@onready var details_panel: PanelContainer = $VBox/Details
@onready var details_label: RichTextLabel = $VBox/Details/MarginContainer/DetailsText
@onready var sign_button: Button = $VBox/Details/MarginContainer/VBox/SignButton
@onready var filter_option: OptionButton = $VBox/AvailableContracts/FilterBar/TypeFilter
@onready var sort_option: OptionButton = $VBox/AvailableContracts/FilterBar/SortBy

var selected_contract_template: Dictionary = {}
var available_contracts: Array = []
var player_reputation: float = 0.75
var player_production_kg: float = 100000.0

func _ready() -> void:
	if contract_system == null:
		push_warning("ContractSystem not found in autoload")
		return
	
	setup_filters()
	refresh_available_contracts()
	refresh_active_contracts()
	
	available_list.item_selected.connect(_on_available_selected)
	sign_button.pressed.connect(_on_sign_pressed)
	filter_option.item_selected.connect(func(_idx): refresh_available_contracts())
	sort_option.item_selected.connect(func(_idx): refresh_available_contracts())
	
	if contract_system:
		contract_system.contract_signed.connect(_on_contract_signed)
		contract_system.contract_expired.connect(_on_contract_expired)

func setup_filters() -> void:
	filter_option.clear()
	filter_option.add_item("All Types", -1)
	filter_option.add_item("Exclusive Supplier", 0)
	filter_option.add_item("Long-term Buyer", 1)
	filter_option.add_item("Joint Venture", 2)
	filter_option.add_item("License Deal", 3)
	filter_option.add_item("Export Agreement", 4)
	
	sort_option.clear()
	sort_option.add_item("Value (High to Low)")
	sort_option.add_item("Duration (Short to Long)")
	sort_option.add_item("Risk (Low to High)")

func refresh_available_contracts() -> void:
	if not contract_system:
		return
	
	available_contracts = contract_system.generate_available_contracts(player_reputation, player_production_kg)
	
	# Apply filter
	var filter_type = filter_option.get_selected_id()
	if filter_type >= 0:
		available_contracts = available_contracts.filter(func(c): return c["type"] == _type_index_to_string(filter_type))
	
	# Apply sort
	var sort_mode = sort_option.selected
	if sort_mode == 0:  # Value
		available_contracts.sort_custom(func(a, b): 
			var eval_a = contract_system.evaluate_contract(a)
			var eval_b = contract_system.evaluate_contract(b)
			return eval_a["total_benefit_nok"] > eval_b["total_benefit_nok"]
		)
	elif sort_mode == 1:  # Duration
		available_contracts.sort_custom(func(a, b): return a["duration_months"] < b["duration_months"])
	elif sort_mode == 2:  # Risk
		available_contracts.sort_custom(func(a, b):
			var eval_a = contract_system.evaluate_contract(a)
			var eval_b = contract_system.evaluate_contract(b)
			return eval_a["risk_score"] < eval_b["risk_score"]
		)
	
	# Populate list
	available_list.clear()
	for contract in available_contracts:
		var evaluation = contract_system.evaluate_contract(contract)
		var label = "%s - %s\n  Value: %.1fM NOK | Risk: %.0f%%" % [
			contract["counterparty"],
			contract["type"],
			evaluation["total_benefit_nok"] / 1_000_000.0,
			evaluation["risk_score"] * 100
		]
		available_list.add_item(label)

func refresh_active_contracts() -> void:
	if not contract_system:
		return
	
	active_list.clear()
	var active = contract_system.get_active_contracts()
	
	for contract in active:
		var months_left = contract.duration_months - (contract_system.current_game_month - contract.signed_date)
		var label = "%s - %s\n  %.0f months left | %.0f kg/month" % [
			contract.counterparty_name,
			_contract_type_to_string(contract.type),
			months_left,
			contract.volume_kg_per_month
		]
		active_list.add_item(label)
	
	# Update summary
	var total_revenue = contract_system.get_total_monthly_revenue()
	var cost_reduction = contract_system.get_total_cost_reduction_pct()
	
	var summary = "Active: %d contracts\nMonthly Revenue: %.1fM NOK\nCost Reduction: %.1f%%" % [
		active.size(),
		total_revenue / 1_000_000.0,
		cost_reduction * 100
	]
	# Could display summary somewhere in UI

func _on_available_selected(index: int) -> void:
	if index < 0 or index >= available_contracts.size():
		return
	
	selected_contract_template = available_contracts[index]
	display_contract_details(selected_contract_template)

func display_contract_details(contract: Dictionary) -> void:
	var evaluation = contract_system.evaluate_contract(contract)
	
	var details_text = "[b]%s[/b]\n" % contract["counterparty"]
	details_text += "Type: %s\n" % contract["type"]
	details_text += "Duration: %d months\n" % contract["duration_months"]
	details_text += "Volume: %s kg/month\n" % _format_number(contract["volume_kg_per_month"])
	details_text += "Price: %.2f NOK/kg\n\n" % contract["price_nok_per_kg"]
	
	details_text += "[b]Economic Impact:[/b]\n"
	details_text += "Annual ROI: %s NOK\n" % _format_number(evaluation["roi_annual_nok"])
	details_text += "Total Benefit: %s NOK\n" % _format_number(evaluation["total_benefit_nok"])
	details_text += "Risk Score: %.0f%%\n" % (evaluation["risk_score"] * 100)
	
	if evaluation["exclusivity_value_nok"] > 0:
		details_text += "\n[color=green]Competitor Lockout Value: %s NOK[/color]\n" % _format_number(evaluation["exclusivity_value_nok"])
	
	if contract.has("exclusivity_radius_km") and contract["exclusivity_radius_km"] > 0:
		details_text += "\n[color=yellow]⚠ Exclusivity: %d km radius[/color]\n" % contract["exclusivity_radius_km"]
	
	details_text += "\n%s" % contract.get("description", "")
	
	details_label.text = details_text
	details_panel.visible = true
	sign_button.disabled = false

func _on_sign_pressed() -> void:
	if selected_contract_template.is_empty():
		return
	
	var success = contract_system.sign_contract(selected_contract_template)
	
	if success:
		refresh_available_contracts()
		refresh_active_contracts()
		details_panel.visible = false
		selected_contract_template = {}
	else:
		# Show error (insufficient capacity, etc.)
		push_warning("Failed to sign contract")

func _on_contract_signed(contract_id: String, type: String, counterparty: String) -> void:
	# Could show notification toast
	print("Contract signed with %s: %s" % [counterparty, type])

func _on_contract_expired(contract_id: String) -> void:
	refresh_active_contracts()
	# Could show notification

func _format_number(value: float) -> String:
	if value >= 1_000_000:
		return "%.2fM" % (value / 1_000_000.0)
	elif value >= 1_000:
		return "%.1fK" % (value / 1_000.0)
	else:
		return "%.0f" % value

func _type_index_to_string(index: int) -> String:
	match index:
		0: return "EXCLUSIVE_SUPPLIER"
		1: return "LONG_TERM_BUYER"
		2: return "JOINT_VENTURE"
		3: return "LICENSE_DEAL"
		4: return "EXPORT_AGREEMENT"
		_: return ""

func _contract_type_to_string(type: int) -> String:
	match type:
		0: return "Exclusive Supplier"
		1: return "Long-term Buyer"
		2: return "Joint Venture"
		3: return "License Deal"
		4: return "Export Agreement"
		_: return "Unknown"
