extends PanelContainer
## CompetitorDashboard - Display AI competitor companies
## Shows 6 rivals (Mowier, SalMax, Herøy, etc.) with stats and actions

@onready var competitor_list = $MarginContainer/VBoxContainer/ScrollContainer/CompetitorList
@onready var market_share_label = $MarginContainer/VBoxContainer/Header/MarketShareLabel
@onready var refresh_button = $MarginContainer/VBoxContainer/Header/RefreshButton

var competitors_data: Array = []

func _ready():
	refresh_button.pressed.connect(_on_refresh_pressed)
	_load_competitors()
	
	# Connect to GameBridge for updates
	if GameBridge:
		if GameBridge.has_signal("CompetitorDataUpdated"):
			GameBridge.CompetitorDataUpdated.connect(_on_competitor_data_updated)

func _load_competitors():
	if not SimBridge:
		push_error("SimBridge not found - cannot load competitors")
		return
	
	SimBridge.call_deferred("GetCompetitors")

func _on_competitor_data_updated(competitors_json: String):
	var json = JSON.new()
	var parse_result = json.parse(competitors_json)
	
	if parse_result != OK:
		push_error("Failed to parse competitors JSON")
		return
	
	competitors_data = json.data
	_populate_competitor_list()
	_update_market_share()

func _populate_competitor_list():
	# Clear existing
	for child in competitor_list.get_children():
		if child.name != "CompetitorTemplate":
			child.queue_free()
	
	for competitor in competitors_data:
		var item = _create_competitor_item(competitor)
		competitor_list.add_child(item)

func _create_competitor_item(competitor: Dictionary) -> Control:
	var item = PanelContainer.new()
	var style = StyleBoxFlat.new()
	style.bg_color = Color("#1C1E20")
	style.border_color = Color("#1F5E73")
	style.border_width_left = 1
	style.border_width_top = 1
	style.border_width_right = 1
	style.border_width_bottom = 1
	style.corner_radius_top_left = 8
	style.corner_radius_top_right = 8
	style.corner_radius_bottom_left = 8
	style.corner_radius_bottom_right = 8
	item.add_theme_stylebox_override("panel", style)
	item.custom_minimum_size = Vector2(0, 120)
	
	var margin = MarginContainer.new()
	margin.add_theme_constant_override("margin_left", 12)
	margin.add_theme_constant_override("margin_right", 12)
	margin.add_theme_constant_override("margin_top", 10)
	margin.add_theme_constant_override("margin_bottom", 10)
	item.add_child(margin)
	
	var vbox = VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 4)
	margin.add_child(vbox)
	
	# Header row - Name and strategy
	var header_hbox = HBoxContainer.new()
	vbox.add_child(header_hbox)
	
	var name_label = Label.new()
	var name = competitor.get("name", "Unknown")
	var tagline = competitor.get("tagline", "")
	name_label.text = name + " - " + tagline
	LakskongenTheme.set_tone(name_label, &"")
	LakskongenTheme.set_size(name_label, 16)
	name_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	header_hbox.add_child(name_label)
	
	# Strategy badge
	var strategy_label = Label.new()
	strategy_label.text = competitor.get("strategy", "Unknown")
	LakskongenTheme.set_tone(strategy_label, &"Info")
	LakskongenTheme.set_size(strategy_label, 11)
	header_hbox.add_child(strategy_label)
	
	# Parody note
	var parody_label = Label.new()
	parody_label.text = "Parody of: " + competitor.get("parodyOf", "N/A")
	LakskongenTheme.set_tone(parody_label, &"Muted")
	LakskongenTheme.set_size(parody_label, 11)
	vbox.add_child(parody_label)
	
	# Financial metrics row
	var metrics_hbox = HBoxContainer.new()
	metrics_hbox.add_theme_constant_override("separation", 16)
	vbox.add_child(metrics_hbox)
	
	# Revenue
	var revenue_label = Label.new()
	var revenue = competitor.get("revenue", 0.0) / 1_000_000_000.0  # Convert to billions
	revenue_label.text = "Revenue: NOK %.2fB" % revenue
	LakskongenTheme.set_tone(revenue_label, &"Muted")
	metrics_hbox.add_child(revenue_label)
	
	# Market share
	var share_label = Label.new()
	var market_share = competitor.get("marketShareNorway", 0.0) * 100
	share_label.text = "Share: %.1f%%" % market_share
	LakskongenTheme.set_tone(share_label, &"Muted")
	metrics_hbox.add_child(share_label)
	
	# Harvest volume
	var volume_label = Label.new()
	var harvest = competitor.get("weeklyHarvestTonnes", 0.0)
	volume_label.text = "Harvest: %.0f t/week" % harvest
	LakskongenTheme.set_tone(volume_label, &"Muted")
	metrics_hbox.add_child(volume_label)
	
	# Recent actions
	var actions_label = Label.new()
	var recent_actions = competitor.get("recentActions", [])
	if recent_actions.size() > 0:
		var latest = recent_actions[0] if recent_actions.size() > 0 else "No recent activity"
		actions_label.text = "Recent: " + latest
		LakskongenTheme.set_tone(actions_label, &"")
	else:
		actions_label.text = tr("No recent activity")
		LakskongenTheme.set_tone(actions_label, &"Muted")
	LakskongenTheme.set_size(actions_label, 11)
	vbox.add_child(actions_label)
	
	# Bottom row - Status and acquire button
	var bottom_hbox = HBoxContainer.new()
	vbox.add_child(bottom_hbox)
	
	# Status indicators
	var status_label = Label.new()
	var is_distressed = competitor.get("isDistressed", false)
	var is_public = competitor.get("isPubliclyListed", false)
	var status_text = ""
	if is_distressed:
		status_text = "[DISTRESSED]"
		LakskongenTheme.set_tone(status_label, &"Crit")
	elif is_public:
		status_text = "[PUBLIC]"
		LakskongenTheme.set_tone(status_label, &"Info")
	else:
		status_text = "[PRIVATE]"
		LakskongenTheme.set_tone(status_label, &"Muted")
	status_label.text = status_text
	status_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	bottom_hbox.add_child(status_label)
	
	# Acquire button
	var acquire_button = Button.new()
	var is_acquirable = competitor.get("isAcquisitionTarget", false)
	var acq_price = competitor.get("acquisitionPrice", 0.0) / 1_000_000_000.0
	
	if is_acquirable:
		acquire_button.text = "Acquire (NOK %.2fB)" % acq_price
		acquire_button.disabled = false
	else:
		acquire_button.text = tr("Not for Sale")
		acquire_button.disabled = true
	
	acquire_button.custom_minimum_size = Vector2(140, 32)
	acquire_button.pressed.connect(_on_acquire_button_pressed.bind(competitor))
	bottom_hbox.add_child(acquire_button)
	
	return item

func _update_market_share():
	var player_share = 0.0  # TODO: Get from GameState
	var total_competitor_share = 0.0
	
	for competitor in competitors_data:
		total_competitor_share += competitor.get("marketShareNorway", 0.0)
	
	# Assuming player share is remainder
	player_share = 1.0 - total_competitor_share
	
	market_share_label.text = "Your Market Share: %.1f%% | Competitors: %.1f%%" % [
		player_share * 100, total_competitor_share * 100
	]

func _on_acquire_button_pressed(competitor: Dictionary):
	var company_id = competitor.get("id", "")
	var name = competitor.get("name", "Unknown")
	var price = competitor.get("acquisitionPrice", 0.0) / 1_000_000_000.0
	
	# Confirmation dialog
	var confirm = ConfirmationDialog.new()
	confirm.dialog_text = "Acquire %s for NOK %.2fB?\n\nThis is a major strategic decision that will affect your debt, market share, and competitive position." % [name, price]
	confirm.title = "Confirm Acquisition"
	confirm.confirmed.connect(_execute_acquisition.bind(company_id))
	add_child(confirm)
	confirm.popup_centered()

func _execute_acquisition(company_id: String):
	if not SimBridge:
		return
	
	SimBridge.call_deferred("AcquireCompetitor", company_id)
	
	# Show result (actual success/failure comes via GameBridge signal)
	var result = AcceptDialog.new()
	result.dialog_text = "Acquisition initiated. Check your finances for the result."
	result.title = "M&A in Progress"
	add_child(result)
	result.popup_centered()

func _on_refresh_pressed():
	_load_competitors()

func _input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		get_viewport().set_input_as_handled()
		_on_close_requested()

func _on_close_requested() -> void:
	hide()

