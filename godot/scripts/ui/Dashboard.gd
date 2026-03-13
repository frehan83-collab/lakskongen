extends Control

## Dashboard — real-time KPI overview panel.
## Reads all data from SimBridge (GDScript autoload) and refreshes
## on every state_updated signal.  Sections: Overview, Production,
## Health, Market, Sustainability, Alerts.

# ── Overview section ──────────────────────────────────────────────────────────
@onready var day_label: Label        = _get("Overview/DayRow/DayValue")
@onready var balance_label: Label    = _get("Overview/BalanceRow/BalanceValue")
@onready var season_label: Label     = _get("Overview/SeasonRow/SeasonValue")
@onready var sites_label: Label      = _get("Overview/SitesRow/SitesValue")
@onready var zones_label: Label      = _get("Overview/ZonesRow/ZonesValue")

# ── Production section ────────────────────────────────────────────────────────
@onready var fish_count_label: Label  = _get("Production/FishRow/FishValue")
@onready var biomass_label: Label     = _get("Production/BiomassRow/BiomassValue")
@onready var temp_label: Label        = _get("Production/TempRow/TempValue")

# ── Health section ────────────────────────────────────────────────────────────
@onready var lice_label: Label        = _get("Health/LiceRow/LiceValue")
@onready var health_status_label: Label = _get("Health/StatusRow/StatusValue")

# ── Market section ────────────────────────────────────────────────────────────
@onready var price_label: Label       = _get("Market/PriceRow/PriceValue")
@onready var avg_price_label: Label   = _get("Market/AvgPriceRow/AvgPriceValue")

# ── Sustainability section ────────────────────────────────────────────────────
@onready var sustain_score_label: Label = _get("Sustainability/ScoreRow/ScoreValue")
@onready var sustain_level_label: Label = _get("Sustainability/LevelRow/LevelValue")

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

	_refresh()
	print("Dashboard: ready")

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
	_refresh_sustainability()
	_refresh_alerts()
	_refresh_events()

func _show_no_data() -> void:
	_set_text(day_label, "--")
	_set_text(balance_label, "--")
	_set_text(fish_count_label, "--")
	_set_text(biomass_label, "--")
	_set_text(price_label, "-- NOK/kg")
	_set_text(sustain_score_label, "--")

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

func _refresh_sustainability() -> void:
	var score: int = _sim.sustainability_score
	var level: String = _sim.sustainability_level
	_set_text(sustain_score_label, "%d / 100" % score)
	_set_text(sustain_level_label, _format_level(level))
	_set_color(sustain_score_label, _sim.sustainability_color(level))
	_set_color(sustain_level_label, _sim.sustainability_color(level))

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
