extends PanelContainer
## MarketChannelPanel - Display and interact with sales channels
## Shows 6 market channels with pricing, premiums, and capacity

@onready var channel_container = $MarginContainer/VBoxContainer/ChannelList
@onready var total_label = $MarginContainer/VBoxContainer/Header/TotalLabel
@onready var quantity_input = $MarginContainer/VBoxContainer/SellControls/QuantityInput
@onready var refresh_button = $MarginContainer/VBoxContainer/Header/RefreshButton

var channels_data: Array = []
var selected_channel: Dictionary = {}

func _ready():
	refresh_button.pressed.connect(_on_refresh_pressed)
	_load_market_channels()
	
	# Connect to GameBridge signals for live updates
	if GameBridge:
		if GameBridge.has_signal("MarketDataUpdated"):
			GameBridge.MarketDataUpdated.connect(_on_market_data_updated)

func _load_market_channels():
	if not SimBridge:
		push_error("SimBridge not found - cannot load market channels")
		return
	
	# Call C# method to get channel data
	SimBridge.call_deferred("GetMarketChannels")

func _on_market_data_updated(channels_json: String):
	var json = JSON.new()
	var parse_result = json.parse(channels_json)
	
	if parse_result != OK:
		push_error("Failed to parse market channels JSON")
		return
	
	channels_data = json.data
	_populate_channel_list()

func _populate_channel_list():
	# Clear existing children (except template)
	for child in channel_container.get_children():
		if child.name != "ChannelTemplate":
			child.queue_free()
	
	for channel in channels_data:
		var channel_item = _create_channel_item(channel)
		channel_container.add_child(channel_item)

func _create_channel_item(channel: Dictionary) -> Control:
	var item = PanelContainer.new()
	item.add_theme_stylebox_override("panel", _get_channel_style(channel))
	
	var margin = MarginContainer.new()
	margin.add_theme_constant_override("margin_left", 12)
	margin.add_theme_constant_override("margin_right", 12)
	margin.add_theme_constant_override("margin_top", 8)
	margin.add_theme_constant_override("margin_bottom", 8)
	item.add_child(margin)
	
	var hbox = HBoxContainer.new()
	margin.add_child(hbox)
	
	# Left side - Channel info
	var info_vbox = VBoxContainer.new()
	info_vbox.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	hbox.add_child(info_vbox)
	
	# Channel name and status
	var name_label = Label.new()
	var channel_name = channel.get("name", "Unknown")
	var is_locked = not channel.get("isAccessible", true)
	name_label.text = channel_name + (" [LOCKED]" if is_locked else "")
	LakskongenTheme.set_tone(name_label, &"" if not is_locked else &"Muted")
	LakskongenTheme.set_size(name_label, 16)
	info_vbox.add_child(name_label)
	
	# Premium and volume info
	var details_label = Label.new()
	var premium = (channel.get("basePriceMultiplier", 1.0) - 1.0) * 100
	var volume_cap = channel.get("maxWeeklyVolumeKg", 0) / 1000.0  # Convert to tonnes
	details_label.text = "Premium: %+.1f%% | Cap: %.0f tonnes/week" % [premium, volume_cap]
	LakskongenTheme.set_tone(details_label, &"Muted")
	LakskongenTheme.set_size(details_label, 12)
	info_vbox.add_child(details_label)
	
	# Price estimate
	var price_label = Label.new()
	var est_price = channel.get("currentPrice", 0.0)
	price_label.text = "Est. Price: NOK %.2f/kg" % est_price
	LakskongenTheme.set_tone(price_label, &"Info")
	info_vbox.add_child(price_label)
	
	# Requirements (if any)
	var req_certs = channel.get("requiredCertifications", [])
	if req_certs.size() > 0:
		var req_label = Label.new()
		req_label.text = "Requires: " + ", ".join(req_certs)
		LakskongenTheme.set_tone(req_label, &"Crit")
		LakskongenTheme.set_size(req_label, 11)
		info_vbox.add_child(req_label)
	
	# Right side - Sell button
	var sell_button = Button.new()
	sell_button.text = tr("Sell Here")
	sell_button.disabled = is_locked
	sell_button.custom_minimum_size = Vector2(100, 40)
	sell_button.pressed.connect(_on_sell_button_pressed.bind(channel))
	hbox.add_child(sell_button)
	
	return item

func _get_channel_style(channel: Dictionary) -> StyleBoxFlat:
	var style = StyleBoxFlat.new()
	style.bg_color = Color("#1C1E20")
	style.border_color = Color("#1F5E73") if channel.get("isAccessible", true) else Color("#5A6168")
	style.border_width_left = 2
	style.border_width_top = 2
	style.border_width_right = 2
	style.border_width_bottom = 2
	style.corner_radius_top_left = 6
	style.corner_radius_top_right = 6
	style.corner_radius_bottom_left = 6
	style.corner_radius_bottom_right = 6
	return style

func _on_sell_button_pressed(channel: Dictionary):
	var quantity = int(quantity_input.text) if quantity_input else 1000
	var channel_id = channel.get("type", "")
	
	if quantity <= 0:
		push_warning("Invalid quantity: %d" % quantity)
		return
	
	# Check for slippage warning
	var volume_cap = channel.get("maxWeeklyVolumeKg", 0)
	if quantity > volume_cap * 0.5:
		_show_slippage_warning(channel, quantity)
	
	# Call C# to execute sale
	if SimBridge:
		SimBridge.call_deferred("SellToChannel", channel_id, quantity)

func _show_slippage_warning(channel: Dictionary, quantity: int):
	var warning_dialog = AcceptDialog.new()
	warning_dialog.dialog_text = "Warning: Large volume sale (%.1f tonnes) may cause price slippage in %s channel!" % [quantity / 1000.0, channel.get("name", "")]
	warning_dialog.title = "Slippage Risk"
	add_child(warning_dialog)
	warning_dialog.popup_centered()

func _on_refresh_pressed():
	_load_market_channels()

func _input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		get_viewport().set_input_as_handled()
		_on_close_requested()

func _on_close_requested() -> void:
	hide()

