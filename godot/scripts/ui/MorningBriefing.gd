extends CanvasLayer

## MorningBriefing — Start-of-day summary
## Shows: weather forecast, overnight events, market changes, pending decisions
## Appears automatically at the start of each day (day advanced signal)

var _game_state: Node = null
var _sim_core: Node = null
var _decision_manager: Node = null
var _last_day_shown: int = 0

func _ready():
	visible = false
	
	_game_state = get_node("/root/GameState")
	_sim_core = get_node("/root/SimulationCore")
	_decision_manager = get_node("/root/DecisionManager")
	
	if _game_state:
		_game_state.DayAdvanced.connect(_on_day_advanced)
	
	_build_ui()

func _build_ui():
	# Semi-transparent overlay
	var overlay := ColorRect.new()
	overlay.color = Color(0, 0, 0, 0.6)
	overlay.set_anchors_preset(Control.PRESET_FULL_RECT)
	overlay.mouse_filter = Control.MOUSE_FILTER_STOP
	add_child(overlay)
	
	# Center container
	var center := CenterContainer.new()
	center.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(center)
	
	# Main panel
	var panel := LakskongenTheme.create_styled_panel()
	panel.custom_minimum_size = Vector2(800, 600)
	center.add_child(panel)
	
	# Content VBox
	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", LakskongenTheme.SPACE_MD)
	panel.add_child(vbox)
	
	# Header
	var header_hbox := HBoxContainer.new()
	var header := LakskongenTheme.create_label("☀️ MORNING BRIEFING", LakskongenTheme.FONT_XXL, LakskongenTheme.GOLD)
	header.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	header_hbox.add_child(header)
	
	var day_label := LakskongenTheme.create_label("Day 1", LakskongenTheme.FONT_LG, LakskongenTheme.MUTED)
	day_label.name = "DayLabel"
	header_hbox.add_child(day_label)
	vbox.add_child(header_hbox)
	
	vbox.add_child(LakskongenTheme.create_divider(LakskongenTheme.GOLD))
	
	# Scroll container for content
	var scroll := ScrollContainer.new()
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	vbox.add_child(scroll)
	
	var content_vbox := VBoxContainer.new()
	content_vbox.add_theme_constant_override("separation", LakskongenTheme.SPACE_MD)
	content_vbox.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	content_vbox.name = "ContentVBox"
	scroll.add_child(content_vbox)
	
	# Close button
	var close_btn := LakskongenTheme.create_button("Start the Day →", true)
	close_btn.custom_minimum_size.y = 50
	close_btn.pressed.connect(_on_close_pressed)
	vbox.add_child(close_btn)

func _on_day_advanced(new_day: int):
	# Only show once per day
	if new_day == _last_day_shown:
		return
	
	_last_day_shown = new_day
	
	# Don't show on day 1 (tutorial shows first)
	if new_day <= 1:
		return
	
	_update_briefing_content(new_day)
	visible = true
	
	# Play morning chime
	if has_node("/root/GameAudio"):
		get_node("/root/GameAudio").play_sfx("morning_chime")

func _update_briefing_content(current_day: int):
	var day_label = $CenterContainer/PanelContainer/VBoxContainer/HBoxContainer/DayLabel as Label
	day_label.text = "Day %d, Year %d" % [_game_state.CurrentDay, _game_state.CurrentYear]
	
	var content_vbox = $CenterContainer/PanelContainer/VBoxContainer/ScrollContainer/ContentVBox as VBoxContainer
	
	# Clear old content
	for child in content_vbox.get_children():
		child.queue_free()
	
	# 1. Weather Forecast
	content_vbox.add_child(_create_section("🌤️ Weather Forecast", _get_weather_forecast()))
	
	# 2. Market Update
	content_vbox.add_child(_create_section("💰 Market Update", _get_market_update()))
	
	# 3. Overnight Events
	var events = _get_overnight_events()
	if events != "":
		content_vbox.add_child(_create_section("📰 Overnight Events", events))
	
	# 4. Pending Decisions
	var decisions = _get_pending_decisions()
	if decisions != "":
		content_vbox.add_child(_create_section("⚠️ Pending Decisions", decisions))
	
	# 5. Company Status
	content_vbox.add_child(_create_section("🏢 Company Status", _get_company_status()))

func _create_section(title: String, content: String) -> VBoxContainer:
	var section := VBoxContainer.new()
	section.add_theme_constant_override("separation", LakskongenTheme.SPACE_SM)
	
	# Title
	var title_label := LakskongenTheme.create_label(title, LakskongenTheme.FONT_LG, LakskongenTheme.ACCENT)
	section.add_child(title_label)
	
	# Content
	var content_label := LakskongenTheme.create_label(content, LakskongenTheme.FONT_MD, LakskongenTheme.TEXT)
	content_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	section.add_child(content_label)
	
	# Divider
	section.add_child(LakskongenTheme.create_divider())
	
	return section

func _get_weather_forecast() -> String:
	if not _sim_core or not _sim_core.Environment:
		return "No data available."
	
	var env = _sim_core.Environment
	var temp = env.CurrentWaterTemp
	var weather = _weather_event_to_string(env.CurrentWeather)
	
	return "Water temp: %.1f°C\nCurrent: %s\nOxygen: %d%%" % [temp, weather, env.OxygenLevel]

func _get_market_update() -> String:
	if not _sim_core or not _sim_core.Market:
		return "No data available."
	
	var market = _sim_core.Market
	var price = market.CurrentSpotPriceNoK
	var change = market.PriceChangePercent * 100.0
	
	var trend = "→"
	var color_text = ""
	if change > 0.5:
		trend = "↗"
		color_text = " (rising)"
	elif change < -0.5:
		trend = "↘"
		color_text = " (falling)"
	
	return "Spot price: %.2f NOK/kg %s\nChange: %+.1f%%%s" % [price, trend, change, color_text]

func _get_overnight_events() -> String:
	# Check for any critical events from last tick
	# For now, placeholder — could store last N events in EventLog
	return ""

func _get_pending_decisions() -> String:
	if not _decision_manager:
		return ""
	
	var decisions = _decision_manager.GetActiveDecisions()
	if decisions.size() == 0:
		return ""
	
	var text = ""
	for decision in decisions:
		text += "• %s (%d days left)\n" % [decision.Title, decision.DaysRemaining]
	
	return text.strip_edges()

func _get_company_status() -> String:
	if not _game_state:
		return ""
	
	var balance = _game_state.CashBalance
	var balance_text = "%.0f NOK" % balance
	var status = "Healthy"
	
	if balance < 0:
		status = "⚠️ IN DEBT"
	elif balance < 1_000_000:
		status = "⚠️ Low Cash"
	
	return "Cash: %s\nStatus: %s\nPens: %d\nReputation: %d/100" % [
		balance_text, status, _game_state.OwnedPens, _game_state.ReputationScore
	]

func _weather_event_to_string(event) -> String:
	match event:
		0: return "Calm"
		1: return "⛈️ Storm"
		2: return "🌊 HURRICANE"
		3: return "☠️ Algal Bloom"
		4: return "🪼 Jellyfish Swarm"
		_: return "Unknown"

func _on_close_pressed():
	visible = false

func _input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		get_viewport().set_input_as_handled()
		_on_close_requested()

func _on_close_requested() -> void:
	hide()

