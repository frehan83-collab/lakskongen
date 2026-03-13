extends Control

## Dashboard — real-time KPI overview panel.
## Reads all data from SimBridge (GDScript autoload) and refreshes
## on every state_updated signal.  Sections: Overview, Production,
## Health, Market, Sustainability, Finance, Research, Environment, Alerts.

# ── Overview section ──────────────────────────────────────────────────────────
@onready var day_label: Label        = _get("Overview/DayRow/DayValue")
@onready var balance_label: Label    = _get("Overview/BalanceRow/BalanceValue")
@onready var season_label: Label     = _get("Overview/SeasonRow/SeasonValue")
@onready var sites_label: Label      = _get("Overview/SitesRow/SitesValue")
@onready var zones_label: Label      = _get("Overview/ZonesRow/ZonesValue")

# ── Production section ────────────────────────────────────────────────────────
@onready var fish_count_label: Label  = _get("Production/FishRow/FishValue")
@onready var biomass_label: Label     = _get("Production/BiomassRow/BiomassValue")
@onready var avg_weight_label: Label  = _get("Production/AvgWeightRow/AvgWeightValue")
@onready var temp_label: Label        = _get("Production/TempRow/TempValue")

# ── Health section ────────────────────────────────────────────────────────────
@onready var lice_label: Label        = _get("Health/LiceRow/LiceValue")
@onready var health_status_label: Label = _get("Health/StatusRow/StatusValue")

# ── Market section ────────────────────────────────────────────────────────────
@onready var price_label: Label       = _get("Market/PriceRow/PriceValue")
@onready var avg_price_label: Label   = _get("Market/AvgPriceRow/AvgPriceValue")

# ── Financial section ─────────────────────────────────────────────────────────
@onready var revenue_label: Label     = _get("Finance/RevenueRow/RevenueValue")
@onready var expenses_label: Label    = _get("Finance/ExpensesRow/ExpensesValue")
@onready var profit_label: Label      = _get("Finance/ProfitRow/ProfitValue")

# ── Sustainability section ────────────────────────────────────────────────────
@onready var sustain_score_label: Label = _get("Sustainability/ScoreRow/ScoreValue")
@onready var sustain_level_label: Label = _get("Sustainability/LevelRow/LevelValue")
@onready var certification_label: Label = _get("Sustainability/CertificationRow/CertificationValue")
@onready var price_mult_label: Label    = _get("Sustainability/PriceMultRow/PriceMultValue")

# ── Research section ──────────────────────────────────────────────────────────
@onready var research_active_label: Label   = _get("Research/ActiveRow/ActiveValue")
@onready var research_progress_label: Label = _get("Research/ProgressRow/ProgressValue")

# ── Environment section ───────────────────────────────────────────────────────
@onready var weather_label: Label     = _get("Environment/WeatherRow/WeatherValue")
@onready var oxygen_label: Label      = _get("Environment/OxygenRow/OxygenValue")

# ── Alerts section ────────────────────────────────────────────────────────────
@onready var alerts_container: VBoxContainer = _get("Alerts/AlertsList")
@onready var no_alerts_label: Label          = _get("Alerts/NoAlertsLabel")

# ── Recent events section ─────────────────────────────────────────────────────
@onready var events_container: VBoxContainer = _get("RecentEvents/EventsList")

# ── Autoload refs ─────────────────────────────────────────────────────────────
var _sim: Node = null

# ── Lifecycle ─────────────────────────────────────────────────────────────────

func _ready() -> void:
	if has_node("/root/SimBridge"):
		_sim = get_node("/root/SimBridge")
		_sim.state_updated.connect(_on_state_updated)
		_sim.alert_raised.connect(_on_alert_raised)
	else:
		push_warning("Dashboard: SimBridge not found — display will be empty")
	_setup_external_data_ui()
	_refresh()
	print("Dashboard: ready")

var _ext_box: VBoxContainer = null
var ext_warnings_label: Label = null
var ext_risk_label: Label = null
var ext_crisis_label: Label = null
var ext_source_label: Label = null

func _setup_external_data_ui() -> void:
	var parent_container = null
	if has_node("ScrollContainer/VBoxContainer"):
		parent_container = get_node("ScrollContainer/VBoxContainer")
	elif has_node("Health"):
		parent_container = get_node("Health").get_parent()
	else:
		parent_container = self
		
	if not parent_container:
		return

	_ext_box = VBoxContainer.new()
	_ext_box.name = "ExternalData"
	parent_container.add_child(_ext_box)

	var hsep = HSeparator.new()
	_ext_box.add_child(hsep)

	var title = Label.new()
	title.text = "External Analytics"
	_ext_box.add_child(title)

	ext_warnings_label = _create_row(_ext_box, "Warnings:")
	ext_risk_label     = _create_row(_ext_box, "Composite Risk:")
	ext_crisis_label   = _create_row(_ext_box, "Crisis Status:")
	ext_source_label   = _create_row(_ext_box, "Snapshot:")

func _create_row(parent: Node, label_text: String) -> Label:
	var row = HBoxContainer.new()
	parent.add_child(row)
	var lbl = Label.new()
	lbl.text = label_text
	lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(lbl)
	var val = Label.new()
	val.text = "--"
	row.add_child(val)
	return val

# ── Signal handlers ───────────────────────────────────────────────────────────

func _on_state_updated() -> void:
	_refresh()

func _on_alert_raised(_level: String, _message: String) -> void:
	_refresh_alerts()

# ── Full refresh ──────────────────────────────────────────────────────────────

func _refresh() -> void:
	if not _sim:
		_show_no_data()
		return

	_refresh_overview()
	_refresh_production()
	_refresh_health()
	_refresh_market()
	_refresh_finance()
	_refresh_sustainability()
	_refresh_research()
	_refresh_environment()
	_refresh_alerts()
	_refresh_events()
	_refresh_external_data()

func _show_no_data() -> void:
	_set_text(day_label, "--")
	_set_text(balance_label, "--")
	_set_text(fish_count_label, "--")
	_set_text(biomass_label, "--")
	_set_text(avg_weight_label, "--")
	_set_text(price_label, "-- NOK/kg")
	_set_text(revenue_label, "--")
	_set_text(expenses_label, "--")
	_set_text(profit_label, "--")
	_set_text(sustain_score_label, "--")
	_set_text(research_active_label, "Ingen aktiv")
	_set_text(weather_label, "--")
	_set_text(oxygen_label, "--")

# ── Section refreshes ─────────────────────────────────────────────────────────

func _refresh_overview() -> void:
	_set_text(day_label, "Dag %d" % _sim.current_day)
	_set_text(balance_label, _sim.format_nok(_sim.balance))
	_set_color(balance_label, _sim.balance_color(_sim.balance))

	var season: String = _sim._season_name(_sim.current_day)
	_set_text(season_label, season)

	_set_text(sites_label, str(_sim.site_count))
	_set_text(zones_label, str(_sim.zone_count))

func _refresh_production() -> void:
	_set_text(fish_count_label, _sim.format_fish(_sim.fish_count))
	_set_text(biomass_label, _sim.format_biomass(_sim.fish_biomass_kg))
	
	# Calculate and display average weight
	var avg_weight_kg: float = 0.0
	if _sim.fish_count > 0:
		avg_weight_kg = _sim.fish_biomass_kg / float(_sim.fish_count)
	var avg_weight_g: float = avg_weight_kg * 1000.0
	if avg_weight_g >= 1000.0:
		_set_text(avg_weight_label, "%.2f kg" % avg_weight_kg)
	else:
		_set_text(avg_weight_label, "%.0f g" % avg_weight_g)

	var temp_text: String = "%.1f°C" % _sim.temperature_c
	_set_text(temp_label, temp_text)
	# Color-code temperature: optimal 12-14°C for Atlantic salmon
	var t: float = _sim.temperature_c
	if t < 4.0 or t > 18.0:
		_set_color(temp_label, Color.RED)
	elif t < 8.0 or t > 16.0:
		_set_color(temp_label, Color.ORANGE)
	else:
		_set_color(temp_label, Color.GREEN)

func _refresh_health() -> void:
	var lice: float = _sim.sea_lice_per_fish
	_set_text(lice_label, "%.2f / fisk" % lice)
	_set_color(lice_label, _sim.lice_color(lice))

	# Overall health status based on lice + sustainability
	var health_str: String
	var health_col: Color
	if lice > 5.0 or _sim.sustainability_score < 30:
		health_str = "Kritisk"
		health_col = Color.RED
	elif lice > 2.0 or _sim.sustainability_score < 50:
		health_str = "Dårlig"
		health_col = Color.ORANGE
	elif lice > 0.5 or _sim.sustainability_score < 70:
		health_str = "Middels"
		health_col = Color.YELLOW
	else:
		health_str = "God"
		health_col = Color.GREEN

	_set_text(health_status_label, health_str)
	_set_color(health_status_label, health_col)

func _refresh_market() -> void:
	_set_text(price_label, "%.2f NOK/kg" % _sim.market_price_nok)
	_set_text(avg_price_label, "%.2f NOK/kg" % _sim.weekly_avg_price_nok)

	# Color based on price vs weekly average
	var delta: float = _sim.market_price_nok - _sim.weekly_avg_price_nok
	if delta > 2.0:
		_set_color(price_label, Color.GREEN)
	elif delta < -2.0:
		_set_color(price_label, Color.RED)
	else:
		_set_color(price_label, Color.WHITE)

func _refresh_finance() -> void:
	var revenue: float = _sim.get_revenue()
	var expenses: float = _sim.get_expenses()
	var profit: float = _sim.get_profit()
	
	_set_text(revenue_label, _sim.format_nok(revenue))
	_set_color(revenue_label, Color.GREEN if revenue > 0.0 else Color.WHITE)
	
	_set_text(expenses_label, _sim.format_nok(expenses))
	_set_color(expenses_label, Color.SALMON if expenses > 0.0 else Color.WHITE)
	
	_set_text(profit_label, _sim.format_nok(profit))
	if profit > 0.0:
		_set_color(profit_label, Color.GREEN)
	elif profit < 0.0:
		_set_color(profit_label, Color.RED)
	else:
		_set_color(profit_label, Color.WHITE)

func _refresh_sustainability() -> void:
	var score: int = _sim.sustainability_score
	var level: String = _sim.sustainability_level
	_set_text(sustain_score_label, "%d / 100" % score)
	_set_text(sustain_level_label, _format_level(level))
	_set_color(sustain_score_label, _sim.sustainability_color(level))
	_set_color(sustain_level_label, _sim.sustainability_color(level))
	
	# Display certification level
	var cert_level: String = _sim.get_certification_level()
	_set_text(certification_label, _format_level(cert_level))
	_set_color(certification_label, _sim.sustainability_color(cert_level))
	
	# Display price multiplier from certification
	var price_mult: float = _sim.get_price_multiplier()
	var mult_percent: int = int((price_mult - 1.0) * 100.0)
	if mult_percent > 0:
		_set_text(price_mult_label, "+%d%%" % mult_percent)
		_set_color(price_mult_label, Color.GREEN)
	elif mult_percent < 0:
		_set_text(price_mult_label, "%d%%" % mult_percent)
		_set_color(price_mult_label, Color.RED)
	else:
		_set_text(price_mult_label, "0%")
		_set_color(price_mult_label, Color.WHITE)

func _refresh_research() -> void:
	var active_id: String = _sim.get_active_research_id()
	
	if active_id.is_empty():
		_set_text(research_active_label, "Ingen aktiv")
		_set_text(research_progress_label, "--")
		_set_color(research_active_label, Color.GRAY)
	else:
		# Get research details
		var research_nodes: Array[Dictionary] = _sim.get_research_nodes()
		var active_name: String = active_id
		var total_days: int = 0
		
		for node in research_nodes:
			if node.get("id", "") == active_id:
				active_name = node.get("name", active_id)
				total_days = node.get("research_days", 0)
				break
		
		_set_text(research_active_label, active_name)
		_set_color(research_active_label, Color.CYAN)
		
		var progress: int = _sim.get_research_progress()
		var remaining: int = _sim.get_research_days_remaining()
		
		if total_days > 0:
			var progress_pct: int = int((float(progress) / float(total_days)) * 100.0)
			_set_text(research_progress_label, "%d%% (%d/%d dager)" % [progress_pct, progress, total_days])
		else:
			_set_text(research_progress_label, "%d dager gjenstår" % remaining)
		_set_color(research_progress_label, Color.YELLOW)

func _refresh_environment() -> void:
	# Weather state
	var weather: String = _sim.audio_weather_state
	var weather_text: String
	match weather:
		"calm":  weather_text = "Rolig ☀"
		"wind":  weather_text = "Vind 💨"
		"storm": weather_text = "Storm ⛈"
		_:       weather_text = "Ukjent"
	_set_text(weather_label, weather_text)
	
	# Color code weather
	match weather:
		"calm":  _set_color(weather_label, Color.GREEN)
		"wind":  _set_color(weather_label, Color.YELLOW)
		"storm": _set_color(weather_label, Color.RED)
		_:       _set_color(weather_label, Color.WHITE)
	
	# Oxygen level (placeholder - not directly exposed by SimBridge yet)
	# In a real implementation, this would come from water chemistry data
	# For now, we'll show a derived value or placeholder
	_set_text(oxygen_label, "-- mg/L")
	_set_color(oxygen_label, Color.WHITE)

func _refresh_alerts() -> void:
	if not alerts_container:
		return

	# Clear existing alert children
	for child in alerts_container.get_children():
		child.queue_free()

	var alerts: Array[Dictionary] = _sim.active_alerts
	if alerts.is_empty():
		_set_visible(no_alerts_label, true)
		return

	_set_visible(no_alerts_label, false)
	for alert in alerts:
		var lbl := Label.new()
		var lvl: String = alert.get("level", "info")
		var msg: String = alert.get("message", "")
		lbl.text = ("⚠ " if lvl == "warning" else "🔴 ") + msg
		lbl.add_theme_color_override("font_color",
			Color.RED if lvl == "critical" else Color.YELLOW)
		alerts_container.add_child(lbl)

func _refresh_events() -> void:
	if not events_container:
		return

	for child in events_container.get_children():
		child.queue_free()

	var events: Array[Dictionary] = _sim.recent_events
	if events.is_empty():
		return

	# Show newest first
	for i in range(events.size() - 1, -1, -1):
		var evt: Dictionary = events[i]
		var day: int    = evt.get("day", 0)
		var name: String = evt.get("name", "Ukjent hendelse")
		var bal_chg: float = evt.get("balance_change", 0.0)

		var lbl := Label.new()
		var sign: String = "+" if bal_chg >= 0 else ""
		lbl.text = "[Dag %d] %s  (%s%s)" % [
			day, name, sign, _sim.format_nok(bal_chg)
		]
		lbl.add_theme_color_override("font_color",
			Color.GREEN if bal_chg >= 0 else Color.SALMON)
		events_container.add_child(lbl)

# ── Helpers ───────────────────────────────────────────────────────────────────

func _get(path: String) -> Node:
	if has_node(path):
		return get_node(path)
	push_warning("Dashboard: node not found: %s" % path)
	return null

func _set_text(lbl: Label, txt: String) -> void:
	if lbl:
		lbl.text = txt

func _set_color(lbl: Label, col: Color) -> void:
	if lbl:
		lbl.add_theme_color_override("font_color", col)

func _set_visible(node: Node, vis: bool) -> void:
	if node:
		node.visible = vis

func _format_level(level: String) -> String:
	match level:
		"SustainabilityLeader": return "Bærekraftsleder ★"
		"OrganicCertified":     return "Økologisk sertifisert"
		"ASCCertified":         return "ASC-sertifisert"
		"BasicCompliance":      return "Grunnleggende overholdelse"
		_:                      return "Ikke i samsvar ⚠"


func _refresh_external_data() -> void:
	if not ext_warnings_label: return
	
	_set_text(ext_warnings_label, "%d active" % _sim.external_warning_count)
	if _sim.external_warning_count > 0:
		_set_color(ext_warnings_label, Color.ORANGE)
	else:
		_set_color(ext_warnings_label, Color.GREEN)

	if ext_risk_label:
		_set_text(ext_risk_label, "%.1f" % _sim.external_composite_risk)
		if _sim.external_composite_risk > 0.8:
			_set_color(ext_risk_label, Color.RED)
		elif _sim.external_composite_risk > 0.4:
			_set_color(ext_risk_label, Color.YELLOW)
		else:
			_set_color(ext_risk_label, Color.GREEN)

	if ext_crisis_label:
		if _sim.external_crisis_active:
			_set_text(ext_crisis_label, _sim.external_crisis_label)
			_set_color(ext_crisis_label, Color.RED)
		else:
			_set_text(ext_crisis_label, "None")
			_set_color(ext_crisis_label, Color.GREEN)

	if ext_source_label:
		var txt = _sim.external_snapshot_source
		if _sim.external_snapshot_freshness_days >= 0:
			txt += " (%d days old)" % _sim.external_snapshot_freshness_days
		_set_text(ext_source_label, txt)


