extends Control
class_name AcquisitionPanel

# UI Components
@onready var target_list: ItemList
@onready var target_details: VBoxContainer
@onready var company_name_label: Label
@onready var asking_price_label: Label
@onready var health_score_label: Label
@onready var market_share_label: Label
@onready var distress_indicator: Label
@onready var asset_type_label: Label
@onready var tonnage_label: Label
@onready var offer_amount_spinbox: SpinBox
@onready var make_offer_button: Button
@onready var antitrust_warning: Label
@onready var player_cash_label: Label

# Signals
signal acquisition_offer_made(target_id: String, offer_amount: float)
signal acquisition_completed(target_id: String, final_price: float, synergies: float)

# Data
var available_targets: Array = []
var selected_target: Dictionary = {}
var player_cash_nok: float = 1000.0 # Million NOK
var player_market_share: float = 0.15

func _ready():
	setup_ui()
	load_acquisition_targets()
	connect_signals()
	update_player_info()

func setup_ui():
	# Create main container if not already in scene
	if not target_list:
		target_list = ItemList.new()
		add_child(target_list)
	
	# Setup target list
	target_list.item_selected.connect(_on_target_selected)
	
	# Setup offer controls
	if offer_amount_spinbox:
		offer_amount_spinbox.min_value = 1.0
		offer_amount_spinbox.max_value = 2000.0
		offer_amount_spinbox.step = 1.0
		offer_amount_spinbox.value = 100.0
	
	# Setup make offer button
	if make_offer_button:
		make_offer_button.pressed.connect(_on_make_offer_pressed)

func connect_signals():
	# Connect to game signals for acquisition updates
	pass

func load_acquisition_targets():
	# Load targets from JSON file
	var file = FileAccess.open("res://game/data/acquisition_targets.json", FileAccess.READ)
	if file:
		var json_text = file.get_as_text()
		var json = JSON.new()
		var parse_result = json.parse(json_text)
		
		if parse_result == OK:
			var data = json.data
			if data.has("targets"):
				available_targets = data["targets"]
				populate_target_list()
		
		file.close()
	else:
		print("Could not load acquisition targets data")
		create_sample_targets() # Fallback

func create_sample_targets():
	# Fallback sample data
	available_targets = [
		{
			"id": "nordfjord_salmon",
			"name": "Nordfjord Salmon AS",
			"asset_value": 85.0,
			"license_value": 132.8,
			"health_score": 0.85,
			"is_distressed": false,
			"asking_price": 250.0,
			"regional_market_share": 0.08,
			"region": "Nordfjord",
			"asset_type": "Fish Farm",
			"tonnage_capacity": 780.0
		}
	]
	populate_target_list()

func populate_target_list():
	target_list.clear()
	
	for target in available_targets:
		var display_text = "%s - %0.1fM NOK" % [target.name, target.asking_price]
		
		# Add distress indicator
		if target.is_distressed:
			display_text += " [DISTRESSED]"
		
		# Add health indicator
		var health_icon = "🟢" if target.health_score > 0.8 else "🟡" if target.health_score > 0.5 else "🔴"
		display_text += " " + health_icon
		
		target_list.add_item(display_text)

func _on_target_selected(index: int):
	if index >= 0 and index < available_targets.size():
		selected_target = available_targets[index]
		update_target_details()
		update_offer_controls()

func update_target_details():
	if selected_target.is_empty():
		return
	
	# Update labels with target information
	if company_name_label:
		company_name_label.text = selected_target.name
	
	if asking_price_label:
		asking_price_label.text = "Asking Price: %0.1fM NOK" % selected_target.asking_price
	
	if health_score_label:
		var health_text = "Health Score: %0.1f%%" % (selected_target.health_score * 100)
		var health_color = Color.GREEN if selected_target.health_score > 0.8 else Color.YELLOW if selected_target.health_score > 0.5 else Color.RED
		health_score_label.text = health_text
		health_score_label.modulate = health_color
	
	if market_share_label:
		market_share_label.text = "Market Share: %0.1f%%" % (selected_target.regional_market_share * 100)
	
	if distress_indicator:
		if selected_target.is_distressed:
			distress_indicator.text = "⚠️ DISTRESSED ASSET - Potential 40-70% discount"
			distress_indicator.modulate = Color.ORANGE
			distress_indicator.visible = true
		else:
			distress_indicator.visible = false
	
	if asset_type_label:
		asset_type_label.text = "Type: " + selected_target.asset_type
	
	if tonnage_label and selected_target.has("tonnage_capacity") and selected_target.tonnage_capacity > 0:
		tonnage_label.text = "MAB Capacity: %0.0f tonnes" % selected_target.tonnage_capacity
		tonnage_label.visible = true
	else:
		if tonnage_label:
			tonnage_label.visible = false

func update_offer_controls():
	if selected_target.is_empty():
		return
	
	# Set reasonable offer range
	var min_offer = selected_target.asking_price * 0.6 # 60% of asking
	var max_offer = selected_target.asking_price * 1.5 # 150% of asking
	
	if offer_amount_spinbox:
		offer_amount_spinbox.min_value = min_offer
		offer_amount_spinbox.max_value = max_offer
		offer_amount_spinbox.value = selected_target.asking_price * 0.9 # Start at 90% of asking
	
	# Check antitrust implications
	var combined_market_share = player_market_share + selected_target.regional_market_share
	if antitrust_warning:
		if combined_market_share > 0.30:
			antitrust_warning.text = "⚠️ ANTITRUST WARNING: Combined market share %.1f%% may trigger regulatory review" % (combined_market_share * 100)
			antitrust_warning.modulate = Color.RED
			antitrust_warning.visible = true
		else:
			antitrust_warning.visible = false

func _on_make_offer_pressed():
	if selected_target.is_empty():
		return
	
	var offer_amount = offer_amount_spinbox.value
	
	# Validate offer against available cash
	if offer_amount > player_cash_nok:
		show_error_message("Insufficient funds! You have %.1fM NOK available." % player_cash_nok)
		return
	
	# Emit signal to game logic
	acquisition_offer_made.emit(selected_target.id, offer_amount)
	
	# Disable button to prevent multiple offers
	make_offer_button.disabled = true
	make_offer_button.text = "Offer Submitted..."

func show_error_message(message: String):
	# Create a simple popup or print to console
	print("Acquisition Error: " + message)
	# In a real implementation, you'd show a proper dialog

func update_player_info():
	if player_cash_label:
		player_cash_label.text = "Available Cash: %.1fM NOK" % player_cash_nok

func set_player_cash(amount: float):
	player_cash_nok = amount
	update_player_info()

func set_player_market_share(share: float):
	player_market_share = share
	if not selected_target.is_empty():
		update_offer_controls()

func on_acquisition_result(success: bool, target_id: String, actual_price: float, synergies: float, failure_reason: String = ""):
	# Reset offer button
	make_offer_button.disabled = false
	make_offer_button.text = "Make Offer"
	
	if success:
		print("Acquisition successful! %s acquired for %.1fM NOK" % [target_id, actual_price])
		
		# Remove acquired target from list
		for i in range(available_targets.size()):
			if available_targets[i].id == target_id:
				available_targets.remove_at(i)
				break
		
		# Update UI
		populate_target_list()
		selected_target = {}
		
		# Emit completion signal
		acquisition_completed.emit(target_id, actual_price, synergies)
	else:
		show_error_message("Acquisition failed: " + failure_reason)

func refresh_targets():
	# Reload targets from data source
	load_acquisition_targets()