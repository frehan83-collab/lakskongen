## WeatherDiseasePanel.gd
## Real-time weather and disease outbreak notification system.
## Displays current weather conditions, alerts, and disease treatment options.

extends PanelContainer
class_name WeatherDiseasePanel

# UI References
@onready var weather_status_label: Label
@onready var temperature_label: Label
@onready var weather_alert_container: VBoxContainer
@onready var disease_alert_container: VBoxContainer
@onready var treatment_options_container: VBoxContainer

# Alert history
var active_weather_alerts: Array[Dictionary] = []
var active_disease_alerts: Array[Dictionary] = []

# Color scheme
const COLOR_CRITICAL = Color(0.9, 0.2, 0.2)
const COLOR_WARNING = Color(0.9, 0.7, 0.2)
const COLOR_INFO = Color(0.3, 0.7, 0.9)
const COLOR_TEXT = "#E6EEF3"
const COLOR_MUTED = "#9AA7B2"

func _ready():
	if not weather_status_label:
		_create_ui()
	
	# Connect to GameBridge signals
	if has_node("/root/GameBridge"):
		var game_bridge = get_node("/root/GameBridge")
		game_bridge.WeatherEvent.connect(_on_weather_event)
		game_bridge.DiseaseOutbreak.connect(_on_disease_outbreak)
		game_bridge.GameStateUpdated.connect(_on_state_updated)
		print("WeatherDiseasePanel: connected to GameBridge")
	else:
		push_warning("WeatherDiseasePanel: GameBridge not found")
	
	_refresh_from_bridge()

func _input(event: InputEvent) -> void:
	if not visible:
		return
	
	if event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()

func _create_ui():
	var margin = MarginContainer.new()
	margin.add_theme_constant_override("margin_top", 16)
	margin.add_theme_constant_override("margin_bottom", 16)
	margin.add_theme_constant_override("margin_left", 16)
	margin.add_theme_constant_override("margin_right", 16)
	add_child(margin)
	
	var scroll = ScrollContainer.new()
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	margin.add_child(scroll)
	
	var vbox = VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 16)
	vbox.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.add_child(vbox)
	
	# Header
	var header = Label.new()
	header.text = "🌊 Weather & Disease Monitoring"
	header.add_theme_font_size_override("font_size", 20)
	header.add_theme_color_override("font_color", Color(COLOR_TEXT))
	vbox.add_child(header)
	
	# Current Weather Section
	var weather_header = Label.new()
	weather_header.text = "Current Weather Conditions"
	weather_header.add_theme_font_size_override("font_size", 16)
	weather_header.add_theme_color_override("font_color", Color(COLOR_TEXT))
	vbox.add_child(weather_header)
	
	weather_status_label = Label.new()
	weather_status_label.add_theme_font_size_override("font_size", 14)
	vbox.add_child(weather_status_label)
	
	temperature_label = Label.new()
	temperature_label.add_theme_color_override("font_color", Color(COLOR_MUTED))
	vbox.add_child(temperature_label)
	
	# Weather Alerts Section
	var alert_header = Label.new()
	alert_header.text = "⚠️ Weather Alerts"
	alert_header.add_theme_font_size_override("font_size", 16)
	alert_header.add_theme_color_override("font_color", Color(COLOR_WARNING))
	vbox.add_child(alert_header)
	
	weather_alert_container = VBoxContainer.new()
	weather_alert_container.add_theme_constant_override("separation", 8)
	vbox.add_child(weather_alert_container)
	
	# Disease Alerts Section
	var disease_header = Label.new()
	disease_header.text = "🦠 Disease Outbreaks"
	disease_header.add_theme_font_size_override("font_size", 16)
	disease_header.add_theme_color_override("font_color", Color(COLOR_CRITICAL))
	vbox.add_child(disease_header)
	
	disease_alert_container = VBoxContainer.new()
	disease_alert_container.add_theme_constant_override("separation", 8)
	vbox.add_child(disease_alert_container)
	
	# Treatment Options Section
	var treatment_header = Label.new()
	treatment_header.text = "💊 Treatment Options"
	treatment_header.add_theme_font_size_override("font_size", 16)
	treatment_header.add_theme_color_override("font_color", Color(COLOR_TEXT))
	vbox.add_child(treatment_header)
	
	treatment_options_container = VBoxContainer.new()
	treatment_options_container.add_theme_constant_override("separation", 8)
	vbox.add_child(treatment_options_container)

func _on_state_updated():
	_refresh_from_bridge()

func _on_weather_event(event_type: String, severity: float, description: String):
	# Add to alert history
	active_weather_alerts.append({
		"type": event_type,
		"severity": severity,
		"description": description,
		"timestamp": Time.get_ticks_msec()
	})
	
	# Keep only last 5 alerts
	if active_weather_alerts.size() > 5:
		active_weather_alerts.pop_front()
	
	_display_weather_alerts()
	
	# Auto-dismiss after 30 seconds
	await get_tree().create_timer(30.0).timeout
	_refresh_weather_alerts()

func _on_disease_outbreak(disease_type: String, site_id: String, severity: float, treatment_cost: float):
	# Add to alert history
	active_disease_alerts.append({
		"disease_type": disease_type,
		"site_id": site_id,
		"severity": severity,
		"treatment_cost": treatment_cost,
		"timestamp": Time.get_ticks_msec()
	})
	
	# Keep only last 5 alerts
	if active_disease_alerts.size() > 5:
		active_disease_alerts.pop_front()
	
	_display_disease_alerts()
	_display_treatment_options(disease_type, site_id, treatment_cost)

func _refresh_from_bridge():
	if not has_node("/root/GameBridge"):
		return
	
	var game_bridge = get_node("/root/GameBridge")
	
	# Update current weather
	var weather_state = game_bridge.GetCurrentWeatherState()
	var temp = game_bridge.GetCurrentTemperature()
	
	var weather_icon = _get_weather_icon(weather_state)
	var weather_color = _get_weather_color(weather_state)
	
	weather_status_label.text = "%s %s" % [weather_icon, weather_state]
	weather_status_label.add_theme_color_override("font_color", weather_color)
	
	var temp_color = COLOR_INFO
	if temp < 4.0:
		temp_color = COLOR_WARNING
	elif temp > 16.0:
		temp_color = COLOR_CRITICAL
	
	temperature_label.text = "Sea Temperature: %.1f°C" % temp
	temperature_label.add_theme_color_override("font_color", temp_color)

func _display_weather_alerts():
	_clear_container(weather_alert_container)
	
	if active_weather_alerts.is_empty():
		var label = Label.new()
		label.text = "No active weather alerts"
		label.add_theme_color_override("font_color", Color(COLOR_MUTED))
		weather_alert_container.add_child(label)
		return
	
	for alert in active_weather_alerts:
		var panel = PanelContainer.new()
		panel.add_theme_stylebox_override("panel", _create_alert_style(alert.get("severity", 0.0)))
		
		var margin = MarginContainer.new()
		margin.add_theme_constant_override("margin_top", 8)
		margin.add_theme_constant_override("margin_bottom", 8)
		margin.add_theme_constant_override("margin_left", 12)
		margin.add_theme_constant_override("margin_right", 12)
		panel.add_child(margin)
		
		var vbox = VBoxContainer.new()
		margin.add_child(vbox)
		
		var type_label = Label.new()
		type_label.text = "⚠️ %s" % alert.get("type", "Unknown")
		type_label.add_theme_font_size_override("font_size", 14)
		type_label.add_theme_color_override("font_color", Color(COLOR_TEXT))
		vbox.add_child(type_label)
		
		var desc_label = Label.new()
		desc_label.text = alert.get("description", "")
		desc_label.add_theme_color_override("font_color", Color(COLOR_MUTED))
		desc_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		vbox.add_child(desc_label)
		
		weather_alert_container.add_child(panel)

func _display_disease_alerts():
	_clear_container(disease_alert_container)
	
	if active_disease_alerts.is_empty():
		var label = Label.new()
		label.text = "No active disease outbreaks"
		label.add_theme_color_override("font_color", Color(COLOR_MUTED))
		disease_alert_container.add_child(label)
		return
	
	for alert in active_disease_alerts:
		var panel = PanelContainer.new()
		panel.add_theme_stylebox_override("panel", _create_alert_style(alert.get("severity", 0.0)))
		
		var margin = MarginContainer.new()
		margin.add_theme_constant_override("margin_top", 8)
		margin.add_theme_constant_override("margin_bottom", 8)
		margin.add_theme_constant_override("margin_left", 12)
		margin.add_theme_constant_override("margin_right", 12)
		panel.add_child(margin)
		
		var vbox = VBoxContainer.new()
		margin.add_child(vbox)
		
		var type_label = Label.new()
		var disease_icon = "🦠" if alert.get("disease_type", "") != "SeaLice" else "🐛"
		type_label.text = "%s %s Outbreak" % [disease_icon, alert.get("disease_type", "Unknown")]
		type_label.add_theme_font_size_override("font_size", 14)
		type_label.add_theme_color_override("font_color", Color(COLOR_CRITICAL))
		vbox.add_child(type_label)
		
		var site_label = Label.new()
		var site_id = alert.get("site_id", "")
		site_label.text = "Location: %s" % ("All sites" if site_id == "all_sites" else site_id)
		site_label.add_theme_color_override("font_color", Color(COLOR_MUTED))
		vbox.add_child(site_label)
		
		var severity_label = Label.new()
		var severity = alert.get("severity", 0.0)
		severity_label.text = "Severity: %s" % _get_severity_text(severity)
		severity_label.add_theme_color_override("font_color", _get_severity_color(severity))
		vbox.add_child(severity_label)
		
		disease_alert_container.add_child(panel)

func _display_treatment_options(disease_type: String, site_id: String, estimated_cost: float):
	_clear_container(treatment_options_container)
	
	# Treatment methods for sea lice (most common)
	var treatments = [
		{"method": "mechanical", "name": "Mechanical Delousing", "effectiveness": 85, "cost_multiplier": 1.0, "icon": "🔧"},
		{"method": "thermal", "name": "Thermal Treatment", "effectiveness": 75, "cost_multiplier": 0.8, "icon": "🔥"},
		{"method": "chemical", "name": "Chemical Bath", "effectiveness": 70, "cost_multiplier": 0.6, "icon": "💊"},
		{"method": "biological", "name": "Cleaner Fish", "effectiveness": 50, "cost_multiplier": 0.5, "icon": "🐟"}
	]
	
	for treatment in treatments:
		var row = HBoxContainer.new()
		row.add_theme_constant_override("separation", 12)
		
		var name_label = Label.new()
		name_label.text = "%s %s" % [treatment.get("icon", ""), treatment.get("name", "")]
		name_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		name_label.add_theme_color_override("font_color", Color(COLOR_TEXT))
		row.add_child(name_label)
		
		var effectiveness_label = Label.new()
		effectiveness_label.text = "%d%% effective" % treatment.get("effectiveness", 0)
		effectiveness_label.add_theme_color_override("font_color", Color(COLOR_MUTED))
		row.add_child(effectiveness_label)
		
		var cost_label = Label.new()
		var cost = estimated_cost * treatment.get("cost_multiplier", 1.0)
		cost_label.text = "%s NOK" % _format_currency(cost)
		cost_label.add_theme_color_override("font_color", Color(COLOR_MUTED))
		row.add_child(cost_label)
		
		var apply_button = Button.new()
		apply_button.text = "Apply"
		apply_button.tooltip_text = "Apply %s treatment to %s" % [treatment.get("name", ""), site_id]
		apply_button.pressed.connect(_on_treatment_applied.bind(treatment.get("method", ""), site_id))
		row.add_child(apply_button)
		
		treatment_options_container.add_child(row)

func _on_treatment_applied(method: String, site_id: String):
	if not has_node("/root/GameBridge"):
		return
	
	var game_bridge = get_node("/root/GameBridge")
	var success = game_bridge.OrderDelousing(site_id, method)
	
	if success:
		print("Treatment applied: %s to %s" % [method, site_id])
		# Clear disease alerts for this site
		active_disease_alerts = active_disease_alerts.filter(func(alert): return alert.get("site_id", "") != site_id)
		_display_disease_alerts()
	else:
		print("Treatment failed: insufficient funds or invalid parameters")

func _refresh_weather_alerts():
	# Remove alerts older than 30 seconds
	var current_time = Time.get_ticks_msec()
	active_weather_alerts = active_weather_alerts.filter(func(alert): 
		return (current_time - alert.get("timestamp", 0)) < 30000
	)
	_display_weather_alerts()

func _get_weather_icon(state: String) -> String:
	match state:
		"Calm": return "☀️"
		"Moderate": return "⛅"
		"Storm": return "⛈️"
		"Severe": return "🌊"
		_: return "🌤️"

func _get_weather_color(state: String) -> Color:
	match state:
		"Calm": return COLOR_INFO
		"Moderate": return Color(COLOR_MUTED)
		"Storm": return COLOR_WARNING
		"Severe": return COLOR_CRITICAL
		_: return Color(COLOR_TEXT)

func _get_severity_text(severity: float) -> String:
	if severity >= 0.75:
		return "Critical"
	elif severity >= 0.5:
		return "High"
	elif severity >= 0.25:
		return "Moderate"
	else:
		return "Low"

func _get_severity_color(severity: float) -> Color:
	if severity >= 0.75:
		return COLOR_CRITICAL
	elif severity >= 0.5:
		return COLOR_WARNING
	else:
		return COLOR_INFO

func _create_alert_style(severity: float) -> StyleBoxFlat:
	var style = StyleBoxFlat.new()
	style.bg_color = Color(0.15, 0.15, 0.2, 0.9)
	
	var border_color = _get_severity_color(severity)
	style.border_color = border_color
	style.set_border_width_all(2)
	style.corner_radius_top_left = 4
	style.corner_radius_top_right = 4
	style.corner_radius_bottom_left = 4
	style.corner_radius_bottom_right = 4
	
	return style

func _clear_container(container: VBoxContainer):
	for child in container.get_children():
		child.queue_free()

func _format_currency(value: float) -> String:
	if value >= 1_000_000_000:
		return "%.2fB" % (value / 1_000_000_000.0)
	elif value >= 1_000_000:
		return "%.1fM" % (value / 1_000_000.0)
	elif value >= 1_000:
		return "%.0fK" % (value / 1_000.0)
	else:
		return "%.0f" % value
