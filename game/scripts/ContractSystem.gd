# ContractSystem.gd
# Contract warfare mechanics for Lakskongen salmon farming simulator
# Exclusive deals, buyer lockout, supplier dominance

extends Node
class_name ContractSystem

signal contract_signed(contract_id: String, type: String, counterparty: String)
signal contract_expired(contract_id: String)
signal contract_breached(contract_id: String, reason: String)

enum ContractType {
	EXCLUSIVE_SUPPLIER,  # Lock down feed/smolt supplier
	LONG_TERM_BUYER,     # Guaranteed buyer at fixed price
	JOINT_VENTURE,       # Partnership for shared operations
	LICENSE_DEAL,        # MAB license lease/partnership
	EXPORT_AGREEMENT     # International market access
}

class ContractTerms:
	var contract_id: String
	var type: ContractType
	var duration_months: int
	var volume_kg_per_month: float
	var price_nok_per_kg: float
	var exclusivity_radius_km: float
	var counterparty_name: String
	var signed_date: int  # Game month
	var active: bool = true
	
	# Economic effects
	var revenue_boost_pct: float = 0.0
	var cost_reduction_pct: float = 0.0
	var quality_premium_pct: float = 0.0
	var competitor_lockout: bool = false
	
	func _init(id: String, t: ContractType, duration: int, volume: float, price: float, radius: float, counterparty: String):
		contract_id = id
		type = t
		duration_months = duration
		volume_kg_per_month = volume
		price_nok_per_kg = price
		exclusivity_radius_km = radius
		counterparty_name = counterparty

# Active contracts
var active_contracts: Dictionary = {}  # contract_id -> ContractTerms

# Available contract templates (loaded from JSON)
var contract_templates: Array = []

# Game state
var current_game_month: int = 0
var player_reputation: float = 0.75
var player_production_capacity_kg: float = 0.0

func _ready() -> void:
	load_contract_templates()

func load_contract_templates() -> void:
	var template_path = "res://game/data/contract_templates.json"
	if not FileAccess.file_exists(template_path):
		push_warning("Contract templates not found: " + template_path)
		return
	
	var file = FileAccess.open(template_path, FileAccess.READ)
	if file:
		var json_string = file.get_as_text()
		file.close()
		
		var json = JSON.new()
		var parse_result = json.parse(json_string)
		if parse_result == OK:
			var data = json.data
			if data.has("templates"):
				contract_templates = data["templates"]
		else:
			push_error("Failed to parse contract_templates.json")

func generate_available_contracts(reputation: float, production_capacity: float) -> Array:
	player_reputation = reputation
	player_production_capacity_kg = production_capacity
	
	var available: Array = []
	
	for template in contract_templates:
		# Check unlock conditions
		if template.has("min_reputation") and reputation < template["min_reputation"]:
			continue
		if template.has("min_production_kg") and production_capacity < template["min_production_kg"]:
			continue
		
		# Check if already have exclusive contract of this type
		if template.get("exclusive", false):
			var has_active = false
			for contract in active_contracts.values():
				if contract.type == template["type"] and contract.active:
					has_active = true
					break
			if has_active:
				continue
		
		available.append(template)
	
	return available

func evaluate_contract(contract_template: Dictionary) -> Dictionary:
	var type = contract_template["type"]
	var duration = contract_template["duration_months"]
	var volume = contract_template["volume_kg_per_month"]
	var price = contract_template["price_nok_per_kg"]
	
	# Calculate ROI
	var monthly_revenue = volume * price
	var annual_revenue = monthly_revenue * 12
	
	var cost_savings = 0.0
	if type == "EXCLUSIVE_SUPPLIER":
		cost_savings = volume * contract_template.get("cost_reduction_nok_per_kg", 2.0) * 12
	
	var total_benefit = (annual_revenue + cost_savings) * (duration / 12.0)
	
	# Calculate risk
	var risk_score = 0.0
	if contract_template.get("exclusivity_radius_km", 0) > 0:
		risk_score += 0.3  # Lock-in risk
	if duration > 24:
		risk_score += 0.2  # Long commitment risk
	if price < 71.44:  # Below baseline salmon price
		risk_score += 0.4  # Below-market risk
	
	# Exclusivity value (locking out competitors)
	var exclusivity_value = 0.0
	if contract_template.get("exclusive", false):
		exclusivity_value = monthly_revenue * 0.25  # 25% bonus for market control
	
	return {
		"roi_annual_nok": total_benefit / (duration / 12.0),
		"total_benefit_nok": total_benefit,
		"risk_score": clamp(risk_score, 0.0, 1.0),
		"exclusivity_value_nok": exclusivity_value,
		"payback_months": duration / 2.0  # Simplified
	}

func sign_contract(contract_template: Dictionary) -> bool:
	# Validate production capacity
	var volume_required = contract_template["volume_kg_per_month"]
	if volume_required > player_production_capacity_kg * 0.8:
		push_warning("Insufficient production capacity for contract")
		return false
	
	# Create contract instance
	var contract_id = contract_template["id"] + "_" + str(Time.get_ticks_msec())
	var type = _string_to_contract_type(contract_template["type"])
	
	var contract = ContractTerms.new(
		contract_id,
		type,
		contract_template["duration_months"],
		contract_template["volume_kg_per_month"],
		contract_template["price_nok_per_kg"],
		contract_template.get("exclusivity_radius_km", 0.0),
		contract_template["counterparty"]
	)
	
	contract.signed_date = current_game_month
	contract.revenue_boost_pct = contract_template.get("revenue_boost_pct", 0.0)
	contract.cost_reduction_pct = contract_template.get("cost_reduction_pct", 0.0)
	contract.quality_premium_pct = contract_template.get("quality_premium_pct", 0.0)
	contract.competitor_lockout = contract_template.get("exclusive", false)
	
	active_contracts[contract_id] = contract
	
	emit_signal("contract_signed", contract_id, contract_template["type"], contract_template["counterparty"])
	
	return true

func tick(months_elapsed: int) -> void:
	current_game_month += months_elapsed
	
	var expired_contracts: Array = []
	
	for contract_id in active_contracts.keys():
		var contract: ContractTerms = active_contracts[contract_id]
		
		if not contract.active:
			continue
		
		var months_active = current_game_month - contract.signed_date
		
		if months_active >= contract.duration_months:
			contract.active = false
			expired_contracts.append(contract_id)
			emit_signal("contract_expired", contract_id)
	
	# Clean up expired contracts
	for contract_id in expired_contracts:
		active_contracts.erase(contract_id)

func get_active_contracts() -> Array:
	var active: Array = []
	for contract in active_contracts.values():
		if contract.active:
			active.append(contract)
	return active

func get_total_monthly_revenue() -> float:
	var total = 0.0
	for contract in active_contracts.values():
		if contract.active and contract.type == ContractType.LONG_TERM_BUYER:
			total += contract.volume_kg_per_month * contract.price_nok_per_kg
	return total

func get_total_cost_reduction_pct() -> float:
	var total = 0.0
	for contract in active_contracts.values():
		if contract.active:
			total += contract.cost_reduction_pct
	return min(total, 0.5)  # Cap at 50% reduction

func has_exclusive_supplier_contract() -> bool:
	for contract in active_contracts.values():
		if contract.active and contract.type == ContractType.EXCLUSIVE_SUPPLIER:
			return true
	return false

func breach_contract(contract_id: String, reason: String) -> void:
	if active_contracts.has(contract_id):
		var contract: ContractTerms = active_contracts[contract_id]
		contract.active = false
		emit_signal("contract_breached", contract_id, reason)
		
		# Reputation penalty
		player_reputation = max(0.0, player_reputation - 0.15)

func _string_to_contract_type(type_string: String) -> ContractType:
	match type_string:
		"EXCLUSIVE_SUPPLIER": return ContractType.EXCLUSIVE_SUPPLIER
		"LONG_TERM_BUYER": return ContractType.LONG_TERM_BUYER
		"JOINT_VENTURE": return ContractType.JOINT_VENTURE
		"LICENSE_DEAL": return ContractType.LICENSE_DEAL
		"EXPORT_AGREEMENT": return ContractType.EXPORT_AGREEMENT
		_: return ContractType.EXCLUSIVE_SUPPLIER

# Debug helper
func get_contract_summary() -> String:
	var summary = "Active Contracts: %d\n" % active_contracts.size()
	for contract in active_contracts.values():
		if contract.active:
			summary += "- %s with %s (%d months remaining)\n" % [
				_contract_type_to_string(contract.type),
				contract.counterparty_name,
				contract.duration_months - (current_game_month - contract.signed_date)
			]
	return summary

func _contract_type_to_string(type: ContractType) -> String:
	match type:
		ContractType.EXCLUSIVE_SUPPLIER: return "Exclusive Supplier"
		ContractType.LONG_TERM_BUYER: return "Long-term Buyer"
		ContractType.JOINT_VENTURE: return "Joint Venture"
		ContractType.LICENSE_DEAL: return "License Deal"
		ContractType.EXPORT_AGREEMENT: return "Export Agreement"
		_: return "Unknown"
