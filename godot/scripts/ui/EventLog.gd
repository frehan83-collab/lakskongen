extends Control

## Event log component for displaying scrollable game events
## Shows color-coded log entries with timestamps and event details

@onready var log_entries: VBoxContainer = $VBoxContainer/ScrollContainer/LogEntries
@onready var scroll_container: ScrollContainer = $VBoxContainer/ScrollContainer
@onready var clear_button: Button = $VBoxContainer/Header/ClearButton

# Log entry limits and management
const MAX_LOG_ENTRIES = 200
const TRIM_TO_ENTRIES = 150

# Color scheme for different event types
const EVENT_COLORS = {
	"info": Color.WHITE,
	"success": Color.GREEN,
	"warning": Color.ORANGE,
	"danger": Color.RED,
	"error": Color.RED,
	"financial": Color.YELLOW,
	"production": Color.CYAN,
	"health": Color.PINK,
	"market": Color.LIGHT_BLUE,
	"system": Color.GRAY
}

# References to autoloads
var event_bus: Node
var game_bridge: Node

# Log state
var entry_count: int = 0
var auto_scroll: bool = true

func _ready():
	# Get autoload references
	event_bus = get_node("/root/EventBus")
	game_bridge = get_node("/root/GameBridge")
	
	# Connect clear button
	clear_button.pressed.connect(_on_clear_button_pressed)
	
	# Connect to all EventBus signals for comprehensive logging
	if event_bus:
		_connect_to_event_signals()
	
	# Connect to GameBridge signals
	if game_bridge:
		game_bridge.GameStarted.connect(_on_game_started)
		game_bridge.GameEnded.connect(_on_game_ended)
		game_bridge.SimulationTicked.connect(_on_simulation_ticked)
	
	print("EventLog initialized")

func _input(event: InputEvent) -> void:
	"""Handle keyboard navigation"""
	if not visible:
		return
	
	if event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()

func _connect_to_event_signals():
	"""Connect to all relevant EventBus signals"""
	# Time & Progress Events
	event_bus.day_advanced.connect(_on_day_advanced)
	event_bus.time_speed_changed.connect(_on_time_speed_changed)
	event_bus.game_paused.connect(_on_game_paused)
	
	# Financial Events
	event_bus.finance_updated.connect(_on_finance_updated)
	event_bus.market_price_changed.connect(_on_market_price_changed)
	event_bus.revenue_earned.connect(_on_revenue_earned)
	event_bus.expense_incurred.connect(_on_expense_incurred)
	
	# Production Events
	event_bus.harvest_completed.connect(_on_harvest_completed)
	event_bus.fish_stocked.connect(_on_fish_stocked)
	event_bus.growth_report.connect(_on_growth_report)
	
	# Health & Disease Events
	event_bus.disease_outbreak.connect(_on_disease_outbreak)
	event_bus.lice_warning.connect(_on_lice_warning)
	event_bus.mortality_report.connect(_on_mortality_report)
	event_bus.treatment_applied.connect(_on_treatment_applied)
	
	# Environmental Events
	event_bus.weather_changed.connect(_on_weather_changed)
	event_bus.water_quality_changed.connect(_on_water_quality_changed)
	event_bus.season_changed.connect(_on_season_changed)
	
	# Farm Management Events
	event_bus.farm_built.connect(_on_farm_built)
	event_bus.farm_upgraded.connect(_on_farm_upgraded)
	event_bus.farm_sold.connect(_on_farm_sold)
	
	# Research & Technology Events
	event_bus.research_completed.connect(_on_research_completed)
	event_bus.technology_unlocked.connect(_on_technology_unlocked)
	
	# Market & Business Events
	event_bus.contract_offered.connect(_on_contract_offered)
	event_bus.contract_completed.connect(_on_contract_completed)
	
	# Regulatory & Compliance Events
	event_bus.inspection_scheduled.connect(_on_inspection_scheduled)
	event_bus.compliance_issue.connect(_on_compliance_issue)

func add_log_entry(text: String, event_type: String = "info", show_timestamp: bool = true):
	"""Add a new log entry with color coding and timestamp"""
	
	# Create new log entry
	var entry = RichTextLabel.new()
	entry.bbcode_enabled = true
	entry.fit_content = true
	entry.scroll_active = false
	entry.selection_enabled = true
	entry.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	
	# Format timestamp
	var timestamp = ""
	if show_timestamp and game_bridge and game_bridge.GameState:
		var day = game_bridge.GameState.CurrentDay
		timestamp = "[color=gray][Day %d][/color] " % day
	
	# Get color for event type
	var color = EVENT_COLORS.get(event_type, Color.WHITE)
	var color_hex = color.to_html()
	
	# Format the complete entry
	var formatted_text = timestamp + "[color=" + color_hex + "]" + text + "[/color]"
	entry.text = formatted_text
	
	# Add to log
	log_entries.add_child(entry)
	entry_count += 1
	
	# Trim old entries if needed
	_trim_log_entries()
	
	# Auto-scroll to bottom
	if auto_scroll:
		call_deferred("_scroll_to_bottom")

func _trim_log_entries():
	"""Remove old log entries to prevent memory issues"""
	if entry_count > MAX_LOG_ENTRIES:
		var entries_to_remove = entry_count - TRIM_TO_ENTRIES
		var children = log_entries.get_children()
		
		for i in range(entries_to_remove):
			if i < children.size():
				children[i].queue_free()
		
		entry_count = TRIM_TO_ENTRIES

func _scroll_to_bottom():
	"""Scroll to the bottom of the log"""
	await get_tree().process_frame
	scroll_container.scroll_vertical = scroll_container.get_v_scroll_bar().max_value

func clear_log():
	"""Clear all log entries"""
	for child in log_entries.get_children():
		child.queue_free()
	
	entry_count = 0
	
	# Add welcome message back
	var welcome = RichTextLabel.new()
	welcome.bbcode_enabled = true
	welcome.fit_content = true
	welcome.text = tr("UI_EVENTLOG_CLEARED")
	log_entries.add_child(welcome)

func _format_currency(amount: int) -> String:
	"""Format currency for log display"""
	var abs_amount = abs(amount)
	var sign = "-" if amount < 0 else ""
	
	if abs_amount >= 1000000:
		return sign + "$%.1fM" % (abs_amount / 1000000.0)
	elif abs_amount >= 1000:
		return sign + "$%.0fK" % (abs_amount / 1000.0)
	else:
		return sign + "$%d" % abs_amount

# === Button Signal Handlers ===

func _on_clear_button_pressed():
	clear_log()

# === GameBridge Signal Handlers ===

func _on_game_started():
	add_log_entry("New game started! Welcome to Lakskongen.", "system")

func _on_game_ended(reason: String):
	add_log_entry("Game ended: " + reason, "system")

func _on_simulation_ticked(day: int):
	# Don't log every tick to avoid spam
	pass

# === EventBus Signal Handlers ===

func _on_day_advanced(day: int, season: String):
	if day % 30 == 1:  # Log month transitions
		add_log_entry("Month %d begins (%s season)" % [(day - 1) / 30 + 1, season], "info")

func _on_time_speed_changed(speed: float):
	var speed_text = "%.0fx" % speed
	add_log_entry("Time speed changed to " + speed_text, "system")

func _on_game_paused(is_paused: bool):
	add_log_entry("Game " + ("paused" if is_paused else "resumed"), "system")

func _on_finance_updated(cash: int, net_worth: int):
	# Only log significant financial changes
	pass

func _on_market_price_changed(price_per_kg: float, trend: String):
	add_log_entry("Market price: $%.2f/kg (%s)" % [price_per_kg, trend], "market")

func _on_revenue_earned(amount: int, source: String):
	add_log_entry("Revenue: %s from %s" % [_format_currency(amount), source], "financial")

func _on_expense_incurred(amount: int, source: String):
	add_log_entry("Expense: %s for %s" % [_format_currency(amount), source], "financial")

func _on_harvest_completed(farm_id: int, fish_count: int, avg_weight: float, revenue: int):
	add_log_entry("Harvest complete at Farm %d: %d fish (%.1fkg avg) - %s revenue" % 
		[farm_id, fish_count, avg_weight, _format_currency(revenue)], "success")

func _on_fish_stocked(farm_id: int, fish_count: int, avg_weight: float):
	add_log_entry("Stocked %d fish (%.1fkg avg) at Farm %d" % 
		[fish_count, avg_weight, farm_id], "production")

func _on_growth_report(farm_id: int, avg_growth_rate: float, total_fish: int):
	add_log_entry("Farm %d growth: %.1f%% rate, %d fish total" % 
		[farm_id, avg_growth_rate, total_fish], "production")

func _on_disease_outbreak(farm_id: int, disease_name: String, affected_fish: int):
	add_log_entry("DISEASE OUTBREAK at Farm %d: %s affecting %d fish!" % 
		[farm_id, disease_name, affected_fish], "danger")

func _on_lice_warning(farm_id: int, lice_count: float, severity: String):
	add_log_entry("Lice alert at Farm %d: %.1f per fish (%s level)" % 
		[farm_id, lice_count, severity], "warning")

func _on_mortality_report(farm_id: int, deaths: int, cause: String):
	add_log_entry("Mortality at Farm %d: %d fish died (%s)" % 
		[farm_id, deaths, cause], "danger")

func _on_treatment_applied(farm_id: int, treatment_type: String, cost: int):
	add_log_entry("Treatment applied at Farm %d: %s (cost: %s)" % 
		[farm_id, treatment_type, _format_currency(cost)], "health")

func _on_weather_changed(weather_type: String, temperature: float, impact: String):
	add_log_entry("Weather: %s, %.1f°C - %s" % 
		[weather_type, temperature, impact], "info")

func _on_water_quality_changed(farm_id: int, oxygen_level: float, temperature: float):
	add_log_entry("Farm %d water quality: %.1f%% oxygen, %.1f°C" % 
		[farm_id, oxygen_level, temperature], "info")

func _on_season_changed(new_season: String, effects: String):
	add_log_entry("Season changed to %s - %s" % [new_season, effects], "info")

func _on_farm_built(farm_id: int, location: String, cost: int):
	add_log_entry("New farm built at %s (Farm %d) - Cost: %s" % 
		[location, farm_id, _format_currency(cost)], "success")

func _on_farm_upgraded(farm_id: int, upgrade_type: String, cost: int):
	add_log_entry("Farm %d upgraded: %s - Cost: %s" % 
		[farm_id, upgrade_type, _format_currency(cost)], "production")

func _on_farm_sold(farm_id: int, sale_price: int):
	add_log_entry("Farm %d sold for %s" % 
		[farm_id, _format_currency(sale_price)], "financial")

func _on_research_completed(research_name: String, benefits: String):
	add_log_entry("Research completed: %s - %s" % 
		[research_name, benefits], "success")

func _on_technology_unlocked(tech_name: String, description: String):
	add_log_entry("Technology unlocked: %s - %s" % 
		[tech_name, description], "success")

func _on_contract_offered(contract_type: String, value: int, terms: String):
	add_log_entry("Contract offered: %s (%s) - %s" % 
		[contract_type, _format_currency(value), terms], "market")

func _on_contract_completed(contract_type: String, payout: int, successful: bool):
	var status = "completed successfully" if successful else "failed"
	add_log_entry("Contract %s: %s - %s" % 
		[status, contract_type, _format_currency(payout)], 
		"success" if successful else "warning")

func _on_inspection_scheduled(farm_id: int, inspection_type: String, days_until: int):
	add_log_entry("Inspection scheduled: %s at Farm %d in %d days" % 
		[inspection_type, farm_id, days_until], "warning")

func _on_compliance_issue(farm_id: int, issue: String, fine: int):
	add_log_entry("Compliance issue at Farm %d: %s - Fine: %s" % 
		[farm_id, issue, _format_currency(fine)], "danger")
