extends Control

## Dashboard v2.1 — Nordic Minimalist / Clean-Tech (scene-based).
## Layout lives in scenes/ui/Dashboard.tscn; this script is wiring only:
## @onready % refs, signal hookups, localization, theme fonts, data refresh.
## Dynamic rows (alerts) are still populated in code; static layout is in the scene.

# ── Autoload refs ─────────────────────────────────────────────────────────────
var _sim: Node = null
var _theme: Node = null
var _juice: Node = null

# ── Scene refs ────────────────────────────────────────────────────────────────
@onready var _title_label: Label = %TitleLabel
@onready var _kpi_biomass_value: Label = %KpiBiomassValue
@onready var _kpi_biomass_change: Label = %KpiBiomassChange
@onready var _kpi_revenue_value: Label = %KpiRevenueValue
@onready var _kpi_revenue_change: Label = %KpiRevenueChange
@onready var _kpi_health_value: Label = %KpiHealthValue
@onready var _kpi_health_change: Label = %KpiHealthChange
@onready var _kpi_fcr_value: Label = %KpiFcrValue
@onready var _kpi_fcr_change: Label = %KpiFcrChange

@onready var _pens_title: Label = %PensTitle
@onready var _water_title: Label = %WaterTitle
@onready var _weather_title: Label = %WeatherTitle
@onready var _fin_title: Label = %FinTitle
@onready var _alerts_title: Label = %AlertsTitle
@onready var _actions_title: Label = %ActionsTitle

@onready var _water_temp: Label = %WaterTemp
@onready var _water_oxygen: Label = %WaterOxygen
@onready var _water_salinity: Label = %WaterSalinity
@onready var _water_ph: Label = %WaterPh

@onready var _weather_icon: Label = %WeatherIcon
@onready var _weather_temp: Label = %WeatherTemp
@onready var _weather_desc: Label = %WeatherDesc
@onready var _weather_wind: Label = %WeatherWind
@onready var _weather_wave: Label = %WeatherWave
@onready var _weather_visibility: Label = %WeatherVisibility
@onready var _weather_humidity: Label = %WeatherHumidity

@onready var _alerts_container: VBoxContainer = %AlertsContainer

@onready var _chart_biomass: Control = %ChartBiomass
@onready var _chart_price: Control = %ChartPrice
@onready var _chart_lice: Control = %ChartLice
@onready var _chart_fcr: Control = %ChartFcr
@onready var _trends_title: Label = %TrendsTitle
@onready var _ops_title: Label = %OpsTitle
@onready var _ops_budget: Label = %OpsBudget
@onready var _ops_list: VBoxContainer = %OpsList
@onready var _policy_option: OptionButton = %PolicyOption

## NightShiftPolicy enum names (C# LakskongenSim.Logistics); SimLabels names them.
const NIGHT_SHIFT_POLICIES: Array[String] = ["Standard", "ScanOnly", "AutomatedROC"]
## WeatherState (C#) → icon.
const WEATHER_ICONS := {"Calm": "☀️", "Moderate": "⛅", "Storm": "⛈️", "Severe": "🌪️"}

@onready var _btn_feeding: Button = %BtnFeeding
@onready var _btn_harvest: Button = %BtnHarvest
@onready var _btn_delouse: Button = %BtnDelouse
@onready var _btn_reports: Button = %BtnReports
@onready var _btn_research: Button = %BtnResearch
@onready var _btn_map: Button = %BtnMap
@onready var _btn_hire: Button = %BtnHire
@onready var _btn_contracts: Button = %BtnContracts

## One card per sea site the player owns (U1-05: no placeholder "Merd 3-6"
## cards), built in code and kept by site id between refreshes.
@onready var _pen_grid: GridContainer = %PenGrid
var _site_cards: Dictionary = {}
var _sites_empty: Label = null

## Rows of the alert card (one per category and site), newest first.
const ALERT_ROWS_MAX := 6
## A site's lice at or over the spring limit (adult females per fish) warns;
## the standing limit (SimState.LICE_LEGAL_LIMIT) is critical.
const SITE_LICE_WARN := 0.2
var _alert_rows: Array[Dictionary] = []

## Income and costs booked over the last FIN_DAYS days, in FIN_BLOCK_DAYS blocks.
@onready var _fin_chart: PeriodBarChart = %FinChart
const FIN_DAYS := 30
const FIN_BLOCK_DAYS := 5

# ── Previous values for animations ────────────────────────────────────────────
var _prev_balance: float = 0.0
var _prev_price: float = 0.0
var _prev_biomass: float = 0.0
var _prev_revenue: float = 0.0
var _prev_health: float = 0.0
var _prev_fcr: float = 0.0

# ── State-of-the-farm banner + threshold-crossing feel ────────────────────────
# The banner lives in code (scene untouched); factor severities are remembered
# so a warning flash / double-pulse only fires on a real threshold crossing,
# never on every state_updated.
var _state_banner: FarmStateBanner = null
var _factor_sev: Dictionary = {}
var _thresholds_primed: bool = false

# ── KPI sparklines (created in code; script loaded lazily) ────────────────────
var _sparkline_script: GDScript = null
var _kpi_sparks: Dictionary = {}

# ── Lifecycle ─────────────────────────────────────────────────────────────────

func _ready() -> void:
	_sim = get_node_or_null("/root/SimBridge")
	_theme = get_node_or_null("/root/LakskongenTheme")
	_juice = get_node_or_null("/root/JuiceEffects")

	if _sim:
		if _sim.has_signal("day_advanced"):
			_sim.day_advanced.connect(_on_day_advanced)
		if _sim.has_signal("state_updated"):
			_sim.state_updated.connect(_on_state_updated)

	_collect_scene_refs()
	_setup_policy_option()
	_build_state_banner()
	_build_kpi_sparklines()
	_apply_localization()
	_apply_theme_fonts()
	_connect_quick_actions()
	_refresh_all()

# No Esc handler: the dashboard is home. Esc here reaches the game scene,
# which opens the pause menu (U1-06); hiding the dashboard only made GameUI
# show it again and swallowed the key.

# ── Signal handlers ───────────────────────────────────────────────────────────

# Throttle (roadmap U2-07): a sim day sends four signals (the tick and the
# state update each refresh the snapshot, and each refresh announces both a
# state update and a day), and at 30× a day passes every other frame. The
# dashboard refreshes at most once per frame (coalesced, deferred), at most
# once per sim day while the clock runs, never while it is hidden (it catches
# up when shown), and every update while paused (the player's own actions).
var _refresh_pending: bool = false
var _refreshed_day: int = -1
## Refreshes done (tests count them against the days that passed) and the
## time the last one took (ms).
var refresh_count: int = 0
var last_refresh_ms: float = 0.0

func _on_day_advanced(_day: int, _season: String) -> void:
	_queue_refresh()

func _on_state_updated() -> void:
	_queue_refresh()

func _queue_refresh() -> void:
	if _refresh_pending:
		return
	_refresh_pending = true
	_flush_refresh.call_deferred()

func _flush_refresh() -> void:
	if not is_inside_tree() or not is_visible_in_tree():
		return   # still pending: _notification refreshes when shown
	var day: int = int(_sim.current_day) if _sim else -1
	if day == _refreshed_day and _sim != null and not _sim.is_paused():
		_refresh_pending = false
		return
	_refresh_pending = false
	_refreshed_day = day
	refresh_count += 1
	var t0 := Time.get_ticks_usec()
	_refresh_all()
	last_refresh_ms = (Time.get_ticks_usec() - t0) / 1000.0

func _notification(what: int) -> void:
	if what == NOTIFICATION_VISIBILITY_CHANGED and _refresh_pending and is_visible_in_tree():
		_flush_refresh.call_deferred()

## True while the clock runs fast (10× or 30×): numbers snap instead of
## counting up, and KPI flashes rest (a flash every day at 30× strobes).
func _fast_clock() -> bool:
	return _sim != null and not _sim.is_paused() and float(_sim.get_time_speed()) >= FAST_SPEED

const FAST_SPEED := 10.0

func _on_quick_action(action: String) -> void:
	# Juice-only feedback: toast + button pop + route to the relevant tab.
	# No simulation calls are made here; panels trigger sim actions themselves.
	_pop_button(action)
	match action:
		"feeding":
			# Feeding is automatic; recipe and AI feeding live in their panel.
			# (The old "Fôr alle" only toasted "feeding all pens" — a dead verb.)
			_route_to_tab("ai_feeding")
		"harvest":
			_toast(tr("UI_TOAST_HARVEST_START"), "INFO")
			_route_to_tab("pens")
		"delouse":
			_toast(tr("UI_TOAST_DELOUSE_START"), "INFO")
			_route_to_tab("pens")
		"reports":
			_route_to_tab("finance")
		"research":
			_toast(tr("UI_TOAST_RESEARCH_OPEN"), "INFO")
			_route_to_tab("research")
		"map":
			_route_to_tab("map")
		"hire":
			_route_to_tab("crew")
		"contracts":
			_route_to_tab("contracts")
		_:
			pass

## Route to a GameUI tab by climbing ancestors (UI-only, no sim change).
func _route_to_tab(tab_id: String) -> void:
	var node: Node = self
	while node != null:
		if node.has_method("show_tab") and node.has_method("has_tab"):
			if node.has_method("get_active_tab") and node.get_active_tab() == tab_id:
				return
			if bool(node.call("has_tab", tab_id)):
				node.call("show_tab", tab_id)
			return
		node = node.get_parent()

func _pop_button(action: String) -> void:
	var btn: Button = null
	match action:
		"feeding": btn = _btn_feeding
		"harvest": btn = _btn_harvest
		"delouse": btn = _btn_delouse
		"reports": btn = _btn_reports
		"research": btn = _btn_research
		"map": btn = _btn_map
		"hire": btn = _btn_hire
		"contracts": btn = _btn_contracts
	if btn:
		JuiceEffects.button_pop(btn)

func _toast(message: String, level: String = "INFO") -> void:
	var mgr := get_node_or_null("/root/ToastNotification")
	if mgr and mgr.has_method("show_toast"):
		mgr.show_toast(message, level, 3.5)

# ── Scene wiring ──────────────────────────────────────────────────────────────

func _collect_scene_refs() -> void:
	_site_cards.clear()

## Create the state-of-the-farm banner in code and slot it under the title bar.
## Keeps Dashboard.tscn untouched; silently no-ops if the scene shape changes.
func _build_state_banner() -> void:
	if _state_banner and is_instance_valid(_state_banner):
		return
	var root_vbox := get_node_or_null("%RootVBox")
	if root_vbox == null:
		return
	_state_banner = FarmStateBanner.new()
	_state_banner.name = "FarmStateBanner"
	_state_banner.action_requested.connect(_route_to_tab)
	root_vbox.add_child(_state_banner)
	root_vbox.move_child(_state_banner, mini(1, root_vbox.get_child_count() - 1))

func _connect_quick_actions() -> void:
	_btn_feeding.pressed.connect(_on_quick_action.bind("feeding"))
	_btn_harvest.pressed.connect(_on_quick_action.bind("harvest"))
	_btn_delouse.pressed.connect(_on_quick_action.bind("delouse"))
	_btn_reports.pressed.connect(_on_quick_action.bind("reports"))
	_btn_research.pressed.connect(_on_quick_action.bind("research"))
	_btn_map.pressed.connect(_on_quick_action.bind("map"))
	_btn_hire.pressed.connect(_on_quick_action.bind("hire"))
	_btn_contracts.pressed.connect(_on_quick_action.bind("contracts"))

	# UIPolish: keyboard navigation for quick actions grid (GridContainer)
	var actions_grid := get_node_or_null("%ActionsGrid")
	if actions_grid:
		UIPolish.link_horizontal_focus(actions_grid)
		UIPolish.link_vertical_focus(actions_grid)

func _apply_localization() -> void:
	_apply_title()
	_pens_title.text = "🏗️ " + tr("UI_DASHBOARD_PENS_TITLE")
	_water_title.text = "💧 " + tr("UI_DASHBOARD_WATER_TITLE")
	_weather_title.text = "🌤️ " + tr("UI_DASHBOARD_WEATHER_TITLE")
	_fin_title.text = "💰 " + tr("UI_DASHBOARD_FINANCE_TITLE")
	_alerts_title.text = "⚠️ " + tr("UI_DASHBOARD_ALERTS_TITLE")
	_actions_title.text = "⚡ " + tr("UI_DASHBOARD_ACTIONS_TITLE")
	_btn_feeding.text = "🤖 " + tr("UI_DASHBOARD_ACTION_FEEDING")
	_btn_feeding.tooltip_text = tr("UI_DASHBOARD_ACTION_FEEDING_TIP")
	_btn_harvest.text = "📦 " + tr("UI_DASHBOARD_ACTION_HARVEST")
	_btn_delouse.text = "🧪 " + tr("UI_DASHBOARD_ACTION_DELOUSE")
	_btn_reports.text = "📊 " + tr("UI_DASHBOARD_ACTION_REPORTS")
	_btn_research.text = "🔬 " + tr("UI_DASHBOARD_ACTION_RESEARCH")
	_btn_map.text = "🗺️ " + tr("UI_DASHBOARD_ACTION_MAP")
	_btn_hire.text = "👷 " + tr("UI_DASHBOARD_ACTION_HIRE")
	_btn_contracts.text = "📋 " + tr("UI_DASHBOARD_ACTION_CONTRACTS")

## "<company> — Oversikt" (the title used to read the raw "%s — Oversikt").
func _apply_title() -> void:
	var company: String = str(_sim.company_name) if _sim else ""
	_title_label.text = tr("UI_DASHBOARD_TITLE") % (company if company != "" else tr("UI_COMPANY_FALLBACK"))

func _apply_theme_fonts() -> void:
	if not _theme:
		return
	LakskongenTheme.set_strong(_title_label)
	LakskongenTheme.set_strong(_kpi_biomass_value)
	LakskongenTheme.set_strong(_kpi_revenue_value)
	LakskongenTheme.set_strong(_kpi_health_value)
	LakskongenTheme.set_strong(_kpi_fcr_value)
	LakskongenTheme.set_strong(_weather_temp)
	LakskongenTheme.set_strong(_pens_title)
	LakskongenTheme.set_strong(_water_title)
	LakskongenTheme.set_strong(_weather_title)
	LakskongenTheme.set_strong(_fin_title)
	LakskongenTheme.set_strong(_alerts_title)
	LakskongenTheme.set_strong(_actions_title)

# ══════════════════════════════════════════════════════════════════════════════
# KPI SPARKLINES (created in code; additive juice, no sim mutation)
# ══════════════════════════════════════════════════════════════════════════════

## One Sparkline beneath each KPI card. The KPI card's value + change labels live
## in the same VBoxContainer, so we parent into that VBox (resolved via the value
## label's parent) — no hard scene path, no crash if a node is missing.
func _build_kpi_sparklines() -> void:
	_sparkline_script = load("res://scripts/ui/Sparkline.gd")
	if _sparkline_script == null:
		return
	var specs: Array = [
		["biomass", _kpi_biomass_value, "total_biomass_kg", Color("#5B8FA3"), "%.0f"],
		# U1-05: the revenue card draws the year's revenue, not the cash balance.
		["revenue", _kpi_revenue_value, "revenue_nok", Color("#2E6B45"), "%.1f"],
		["health", _kpi_health_value, "sustainability_score", Color("#1F5E73"), "%.0f"],
		["fcr", _kpi_fcr_value, "fcr", Color("#8A5A00"), "%.2f"],
	]
	for spec in specs:
		var key: String = spec[0]
		var value_lbl: Label = spec[1]
		if value_lbl == null:
			continue
		var parent: Node = value_lbl.get_parent()
		if parent == null:
			continue
		var spark = _sparkline_script.new()
		spark.name = "KpiSpark" + key.capitalize()
		spark.line_color = spec[3]
		spark.unit_fmt = spec[4]
		# Biomass is kept in kg and revenue in kr: label them as the cards do.
		if key == "biomass":
			spark.value_format = func(v: float) -> String: return SimFormatters.format_biomass(v)
		elif key == "revenue":
			spark.value_format = func(v: float) -> String: return SimFormatters.format_nok(v)
		spark.custom_minimum_size = Vector2(80, 28)
		parent.add_child(spark)
		_kpi_sparks[key] = {"node": spark, "metric": spec[2]}

func _refresh_kpi_sparklines() -> void:
	if not _sim or not _sim.has_method("get_analytics_data"):
		return
	for key in _kpi_sparks:
		var info: Dictionary = _kpi_sparks[key]
		var spark = info.get("node")
		if spark == null or not is_instance_valid(spark):
			continue
		var data: Array[float] = _sim.get_analytics_data(str(info.get("metric", "")), 30)
		if spark.has_method("set_values"):
			spark.call("set_values", data)
		else:
			spark.set("values", data)

## Flash a KPI value label green when the change is good, red when bad.
## Skips the first refresh (prev == 0) and changes below the material threshold.
func _flash_kpi(lbl: Label, new_val: float, prev_val: float, good_when_up: bool, material: float = 0.005) -> void:
	if lbl == null or is_zero_approx(prev_val) or _fast_clock():
		return
	var delta := new_val - prev_val
	if absf(delta) / maxf(absf(prev_val), 1e-6) < material:
		return
	_flash_label(lbl, (delta > 0.0) == good_when_up)

func _flash_label(lbl: Label, good: bool) -> void:
	if lbl == null:
		return
	if good:
		JuiceEffects.flash_income(lbl)
	else:
		JuiceEffects.flash_damage(lbl)

# ══════════════════════════════════════════════════════════════════════════════
# DATA REFRESH
# ══════════════════════════════════════════════════════════════════════════════

func _refresh_all() -> void:
	_apply_title()
	_refresh_kpis()
	_refresh_pens()
	_refresh_water()
	_refresh_weather()
	_refresh_alerts()
	_refresh_charts()
	_refresh_finance()
	_refresh_ops()
	_refresh_farm_state()

func _refresh_farm_state() -> void:
	if _sim == null:
		return
	var ev: Dictionary = FarmStateBanner.evaluate(_sim)
	var state := str(ev.get("state", FarmStateBanner.STATE_HEALTHY))
	var summary := str(ev.get("summary", ""))
	var detail := str(ev.get("detail", ""))
	if _state_banner and is_instance_valid(_state_banner):
		var title := "%s %s" % [FarmStateBanner.icon_for(state), summary]
		_state_banner.set_state(state, title, detail)
		_state_banner.set_action(str(ev.get("worst_key", "")))

	# One-shot threshold-crossing feel. Only fires on a real transition, so
	# being called on every state_updated cannot spam.
	var factors: Dictionary = ev.get("factors", {})
	_check_farm_factor("lice", int(factors.get("lice", 0)), _kpi_health_value)
	_check_farm_factor("oxygen", int(factors.get("oxygen", 0)), _water_oxygen)
	_check_farm_factor("fcr", int(factors.get("fcr", 0)), _kpi_fcr_value)
	_check_farm_factor("balance", int(factors.get("balance", 0)), _kpi_revenue_value)
	_thresholds_primed = true

## Warning flash + double-pulse when a metric crosses into critical, a green
## recovery flash when it climbs back out. Records severity to dedupe.
func _check_farm_factor(key: String, sev: int, value_label: Control) -> void:
	var prev := int(_factor_sev.get(key, 0))
	_factor_sev[key] = sev
	if not _thresholds_primed:
		return
	if value_label == null or not is_instance_valid(value_label):
		return
	if sev >= 2 and prev < 2:
		JuiceEffects.flash_warning(value_label)
		var card := _kpi_card(value_label)
		if card:
			card.pivot_offset = card.size * 0.5
			JuiceEffects.double_pulse(card)
	elif sev < 2 and prev >= 2:
		JuiceEffects.flash_income(value_label)

## Walk up from a KPI value label to the enclosing card (first PanelContainer).
func _kpi_card(lbl: Control) -> Control:
	var n: Node = lbl
	while n != null:
		if n is PanelContainer:
			return n as Control
		n = n.get_parent()
	return lbl

func _trend_text(trend: String) -> String:
	match trend:
		"up": return "▲ " + tr("UI_TREND_UP")
		"down": return "▼ " + tr("UI_TREND_DOWN")
		_: return "─ " + tr("UI_TREND_STABLE")

func _trend_color(trend: String, good_when_up: bool) -> Color:
	if trend == "stable":
		return Color("#5A6168")
	var good := (trend == "up") == good_when_up
	return Color("#2E6B45") if good else Color("#B3172F")

func _refresh_kpis() -> void:
	if not _sim:
		return

	# Biomass (juice: count-up tween, display only)
	var biomass_tonnes: float = float(_sim.fish_biomass_kg) / 1000.0
	_flash_kpi(_kpi_biomass_value, biomass_tonnes, _prev_biomass, true)
	_count_up(_kpi_biomass_value, _prev_biomass, biomass_tonnes,
		func(v: float) -> String: return tr("UI_DASHBOARD_BIOMASS_FORMAT") % SimFormatters.integer(int(v)))
	_prev_biomass = biomass_tonnes
	var biomass_trend: String = _sim.get_analytics_trend("total_biomass_kg") if _sim.has_method("get_analytics_trend") else "stable"
	_kpi_biomass_change.text = _trend_text(biomass_trend)
	LakskongenTheme.set_text_color(_kpi_biomass_change, _trend_color(biomass_trend, true))

	# Revenue (juice: count-up tween, display only)
	var revenue: float = float(_sim.get_revenue())
	var revenue_m := revenue / 1_000_000.0
	_flash_kpi(_kpi_revenue_value, revenue_m, _prev_revenue, true)
	_count_up(_kpi_revenue_value, _prev_revenue, revenue_m,
		func(v: float) -> String: return tr("UI_DASHBOARD_REVENUE_FORMAT") % SimFormatters.num(v, 1))
	_prev_revenue = revenue_m
	var revenue_trend: String = _sim.get_analytics_trend("revenue_nok") if _sim.has_method("get_analytics_trend") else "stable"
	_kpi_revenue_change.text = _trend_text(revenue_trend)
	LakskongenTheme.set_text_color(_kpi_revenue_change, _trend_color(revenue_trend, true))

	# Health (use sustainability score as proxy)
	var health: int = int(_sim.sustainability_score)
	_kpi_health_value.text = SimFormatters.percent(health / 100.0)
	_flash_kpi(_kpi_health_value, float(health), _prev_health, true, 0.01)
	_prev_health = float(health)
	if health >= 90:
		_kpi_health_change.text = "▲ " + tr("UI_DASHBOARD_HEALTH_GOOD")
		LakskongenTheme.set_tone(_kpi_health_change, &"Ok")
	elif health >= 70:
		_kpi_health_change.text = "─ " + tr("UI_DASHBOARD_HEALTH_STABLE")
		LakskongenTheme.set_tone(_kpi_health_change, &"Warn")
	else:
		_kpi_health_change.text = "▼ " + tr("UI_DASHBOARD_HEALTH_LOW")
		LakskongenTheme.set_tone(_kpi_health_change, &"Crit")

	# FCR — live from FeedSystem; target and ceiling from CalibrationRegistry
	# through the bridge (one target for every panel). Fresh smolts convert
	# feed poorly by nature: no verdict until they have settled (U1-08).
	var fcr: float = float(_sim.fcr)
	var fcr_target: float = float(_sim.fcr_target)
	var fcr_poor: float = float(_sim.fcr_poor)
	_kpi_fcr_value.text = SimFormatters.num(fcr, 2) if fcr > 0.0 else "–"
	_flash_kpi(_kpi_fcr_value, fcr, _prev_fcr, false)
	_prev_fcr = fcr
	var fcr_trend: String = _sim.get_analytics_trend("fcr") if _sim.has_method("get_analytics_trend") else "stable"
	if not _sim.fcr_is_settled():
		_kpi_fcr_change.text = tr("UI_DASHBOARD_FCR_SETTLING")
		LakskongenTheme.set_tone(_kpi_fcr_change, &"Muted")
	elif fcr <= fcr_target:
		_kpi_fcr_change.text = "▲ " + tr("UI_DASHBOARD_FCR_BETTER") % SimFormatters.num(fcr_target, 2)
		LakskongenTheme.set_tone(_kpi_fcr_change, &"Ok")
	elif fcr <= fcr_poor:
		_kpi_fcr_change.text = "%s · %s" % [_trend_text(fcr_trend), tr("UI_DASHBOARD_FCR_TARGET") % SimFormatters.num(fcr_target, 2)]
		LakskongenTheme.set_tone(_kpi_fcr_change, &"Warn")
	else:
		_kpi_fcr_change.text = "▼ " + tr("UI_DASHBOARD_FCR_BAD")
		LakskongenTheme.set_tone(_kpi_fcr_change, &"Crit")

	_refresh_kpi_sparklines()
	_refresh_kpi_tooltips(biomass_tonnes, revenue_m, health, fcr)

## Static-but-honest "explain this number" tooltips for the four KPI cards.
## Each explains the contributing factors and quotes the live value where it is
## trivially available. No fabricated live breakdown; null-guarded throughout.
func _refresh_kpi_tooltips(biomass_tonnes: float, revenue_m: float, health: int, fcr: float) -> void:
	var fish := 0
	var avg_w := 0.0
	if _sim:
		var f = _sim.get("fish_count")
		if f != null:
			fish = int(f)
		var aw = _sim.get("avg_weight_kg")
		if aw != null:
			avg_w = float(aw)

	if is_instance_valid(_kpi_biomass_value):
		_kpi_biomass_value.tooltip_text = tr("UI_DASHBOARD_BIOMASS_TIP") % [
			SimFormatters.integer(fish), SimFormatters.num(avg_w, 2), SimFormatters.integer(int(round(biomass_tonnes)))]

	if is_instance_valid(_kpi_revenue_value):
		_kpi_revenue_value.tooltip_text = tr("UI_DASHBOARD_REVENUE_TIP") % SimFormatters.num(revenue_m, 1)

	if is_instance_valid(_kpi_health_value):
		_kpi_health_value.tooltip_text = tr("UI_DASHBOARD_HEALTH_TIP") % SimFormatters.percent(health / 100.0)

	if is_instance_valid(_kpi_fcr_value):
		_kpi_fcr_value.tooltip_text = tr("UI_DASHBOARD_FCR_TIP") % [
			SimFormatters.num(float(_sim.fcr_target), 2), SimFormatters.num(fcr, 2)]

func _refresh_pens() -> void:
	if not _sim:
		return

	# The refresh's own snapshot of the sites (no second bridge call a day).
	show_sites(_sim.sea_sites if "sea_sites" in _sim else [])


## One card per site in `sites` (GameBridge site dictionaries), in order;
## cards of sites no longer listed go. _refresh_pens feeds it the sim's sites.
func show_sites(sites: Array) -> void:
	if _pen_grid == null:
		return
	var wanted: Dictionary = {}
	for i in sites.size():
		var site: Dictionary = sites[i]
		var id := str(site.get("id", "site_%d" % i))
		wanted[id] = true
		if not _site_cards.has(id):
			_site_cards[id] = _make_site_card(id)
		var card: Dictionary = _site_cards[id]
		_pen_grid.move_child(card["panel"], i)
		_fill_site_card(card, site)
	for id in _site_cards.keys():
		if not wanted.has(id):
			(_site_cards[id]["panel"] as Node).queue_free()
			_site_cards.erase(id)
	if _sites_empty == null:
		_sites_empty = Label.new()
		_sites_empty.name = "SitesEmpty"
		_sites_empty.theme_type_variation = &"SmallMuted"
		_sites_empty.text = tr("UI_DASHBOARD_SITES_EMPTY")
		_pen_grid.get_parent().add_child(_sites_empty)
	_sites_empty.visible = sites.is_empty()


## The site cards shown now, in order (tests read them).
func get_site_cards() -> Array[Control]:
	var out: Array[Control] = []
	if _pen_grid == null:
		return out
	for child in _pen_grid.get_children():
		if child is Control and child.has_meta(&"site_id") and not child.is_queued_for_deletion():
			out.append(child as Control)
	return out


## A site card: name and status dot, then fish, mean weight and the site's
## own lice count (each site's, not the company average).
func _make_site_card(site_id: String) -> Dictionary:
	var panel := PanelContainer.new()
	panel.name = "SiteCard_" + site_id.validate_node_name()
	panel.theme_type_variation = &"Tile"
	panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	panel.set_meta(&"site_id", site_id)
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 6)
	panel.add_child(box)
	var header := HBoxContainer.new()
	box.add_child(header)
	var name_label := Label.new()
	name_label.theme_type_variation = &"BodySmallStrong"
	name_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	name_label.clip_text = true
	header.add_child(name_label)
	var dot := ColorRect.new()
	dot.custom_minimum_size = Vector2(10, 10)
	dot.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	dot.mouse_filter = Control.MOUSE_FILTER_IGNORE
	header.add_child(dot)
	var card := {"panel": panel, "name": name_label, "dot": dot}
	for key in ["count", "weight", "lice"]:
		var row := HBoxContainer.new()
		box.add_child(row)
		var cap := Label.new()
		cap.theme_type_variation = &"SmallMuted"
		cap.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		cap.text = tr({"count": "UI_PEN_COUNT_LABEL", "weight": "UI_DASHBOARD_PEN_WEIGHT", "lice": "UI_DASHBOARD_PEN_LICE"}[key])
		row.add_child(cap)
		var value := Label.new()
		value.theme_type_variation = &"Small"
		row.add_child(value)
		card[key] = value
	_pen_grid.add_child(panel)
	return card


func _fill_site_card(card: Dictionary, site: Dictionary) -> void:
	var count: int = int(site.get("fish_count", 0))
	var biomass: float = float(site.get("biomass_kg", 0.0))
	var max_bio: float = float(site.get("max_biomass", 0.0))
	var escapes: int = int(site.get("escapes_this_year", 0))
	var lice: float = float(site.get("lice_per_fish", 0.0))
	(card["name"] as Label).text = str(site.get("name", site.get("id", "")))
	(card["count"] as Label).text = SimFormatters.integer(count) if count > 0 else tr("UI_DASHBOARD_PEN_EMPTY")
	(card["weight"] as Label).text = SimFormatters.format_fish_weight(biomass / count) if count > 0 else "–"
	(card["lice"] as Label).text = SimFormatters.num(lice, 2) if count > 0 else "–"
	var tone := &""
	var dot_color := Color("#2E6B45")
	if count <= 0:
		dot_color = Color("#D6CFBF")
	elif escapes > 0 or lice >= SimState.LICE_LEGAL_LIMIT or (max_bio > 0.0 and biomass > max_bio):
		dot_color = Color("#B3172F")
		tone = &"Crit"
	elif lice >= SITE_LICE_WARN or (max_bio > 0.0 and biomass > max_bio * 0.8):
		dot_color = Color("#8A5A00")
		tone = &"Warn"
	(card["dot"] as ColorRect).color = dot_color
	LakskongenTheme.set_tone(card["lice"] as Label, tone)
	(card["panel"] as Control).tooltip_text = tr("UI_DASHBOARD_SITE_TIP") % [
		SimFormatters.format_biomass(biomass), SimFormatters.num(lice, 2), SimFormatters.num(SimState.LICE_LEGAL_LIMIT, 1)]


func _refresh_water() -> void:
	if not _sim:
		return

	var temp: float = float(_sim.temperature_c)
	_water_temp.text = SimFormatters.num(temp, 1) + " °C"
	var o2: float = float(_sim.oxygen_mgl)
	_water_oxygen.text = SimFormatters.num(o2, 1) + " mg/L" if o2 == o2 and o2 > 0.0 else "–"
	if o2 == o2 and o2 > 0.0:
		if o2 < 5.0:
			LakskongenTheme.set_tone(_water_oxygen, &"Crit")
		elif o2 < 6.5:
			LakskongenTheme.set_tone(_water_oxygen, &"Warn")
		else:
			LakskongenTheme.set_tone(_water_oxygen, &"")
	_water_salinity.text = SimFormatters.num(float(_sim.wave_m), 1) + " m"
	var ph: float = float(_sim.water_ph)
	_water_ph.text = SimFormatters.num(ph, 1) if ph == ph and ph > 0.0 else "–"

func _refresh_weather() -> void:
	if not _sim:
		return

	var temp: float = float(_sim.temperature_c)
	_weather_temp.text = "%d °C" % int(temp)

	var weather_state: String = str(_sim.get_current_weather_state())
	_weather_icon.text = str(WEATHER_ICONS.get(weather_state, "⛅"))
	_weather_desc.text = SimLabels.weather(weather_state)

	# Live wind/wave + honest Markov-chain storm forecast (Phase 1.2).
	_weather_wind.text = SimFormatters.num(float(_sim.wind_ms), 0) + " m/s"
	_weather_wave.text = SimFormatters.num(float(_sim.wave_m), 1) + " m"
	var risk: float = float(_sim.storm_risk_3d)
	if risk > 0.15:
		_weather_desc.text += " · ☔ " + tr("UI_DASHBOARD_STORM_RISK") % SimFormatters.percent(risk)
		LakskongenTheme.set_text_color(_weather_desc, Color("#B3172F") if risk > 0.5 else Color("#8A5A00"))
	else:
		LakskongenTheme.set_tone(_weather_desc, &"")
	_weather_visibility.text = tr("UI_DASHBOARD_NETS_VALUE") % SimFormatters.percent(float(_sim.min_net_integrity) / 100.0)
	_weather_humidity.text = tr("UI_DASHBOARD_LICE_VALUE") % SimFormatters.num(float(_sim.sea_lice_per_fish), 2)

## Times the alert list changed and its rows were updated (tests: an
## unchanged list is not touched, U2-07).
var alert_rebuilds: int = 0

func _refresh_alerts() -> void:
	if not _alerts_container:
		return

	var alerts: Array[Dictionary] = _collect_alert_rows()
	# Diff, not rebuild: an unchanged list is left alone, a changed one
	# keeps its row nodes (at 30× the list was rebuilt every other frame).
	if alert_rebuilds > 0 and alerts == _alert_rows:
		return
	alert_rebuilds += 1
	_alert_rows = alerts
	# The rows keep their nodes; only changed labels are set (AlertRowList).
	AlertRowList.sync(_alerts_container, alerts, tr("UI_DASHBOARD_ALERTS_EMPTY"), _theme)

## The alert rows: live alerts first (newest event alerts, then conditions),
## then recent sim events, newest first. One row per (category, site)
## (roadmap U1-04) that counts its occurrences. U1-05 keeps the count and the
## time honest: an event alert is dated by the day it happened ("Nå" only
## today), and the event-log record of the same occurrence (the storm alert
## and the "Stormskade" entry of that day) is not counted a second time.
## At most ALERT_ROWS_MAX rows.
func _collect_alert_rows() -> Array[Dictionary]:
	var rows: Array[Dictionary] = []
	if not _sim:
		return rows
	var today: int = int(_sim.current_day)
	var by_identity: Dictionary = {}
	var alerted: Dictionary = {}
	for a in _sim.active_alerts:
		var identity := alert_identity(a)
		var day: int = int(a.get("day", today))
		alerted["%s|%s|%d" % [identity[0], identity[1], day]] = true
		_add_alert_row(rows, by_identity, identity, {"text": str(a.get("message", "")),
			"level": str(a.get("level", "info")), "time": _alert_time(day, today)})
	var events: Array = _sim.recent_events
	for i in range(events.size() - 1, -1, -1):
		var e: Dictionary = events[i]
		var desc: String = str(e.get("description", ""))
		if desc.is_empty():
			continue
		var site: Variant = e.get("site_id", "")
		var identity := [str(e.get("category", "event")).to_lower(), "" if site == null else str(site)]
		var day: int = int(e.get("day", 0))
		if alerted.has("%s|%s|%d" % [identity[0], identity[1], day]):
			continue
		_add_alert_row(rows, by_identity, identity, {
			"text": "%s — %s" % [str(e.get("name", tr("UI_EVENT_FALLBACK"))), desc], "level": "info",
			"time": _alert_time(day, today)})
	return rows.slice(0, ALERT_ROWS_MAX)

## "Nå" for today's alerts, "Dag N" for older ones.
func _alert_time(day: int, today: int) -> String:
	return tr("UI_TIME_NOW") if day >= today else tr("UI_DAY_N") % day

func _add_alert_row(rows: Array[Dictionary], by_identity: Dictionary, identity: Array, row: Dictionary) -> void:
	var key := "%s|%s" % identity
	if by_identity.has(key):
		var kept: Dictionary = rows[int(by_identity[key])]
		kept["count"] = int(kept["count"]) + 1
		return
	row["category"] = identity[0]
	row["site"] = identity[1]
	row["count"] = 1
	by_identity[key] = rows.size()
	rows.append(row)

## What an alert is about, as [category, site]. Event alerts carry both;
## condition alerts are keyed "<category>" or "<category>:<site>:<kind>".
static func alert_identity(alert: Dictionary) -> Array:
	if alert.has("category"):
		return [str(alert["category"]).to_lower(), str(alert.get("site", ""))]
	var parts := str(alert.get("key", "")).split(":")
	return [parts[0], parts[1] if parts.size() > 2 else ""]

## The rows the alert card shows now (category, site, level, text, count).
func get_alert_rows() -> Array[Dictionary]:
	return _alert_rows.duplicate(true)


func _refresh_charts() -> void:
	if not _sim or not _sim.has_method("get_analytics_data"):
		return
	var fc: Dictionary = _sim.get_forecast() if _sim.has_method("get_forecast") else {}
	var conf := str(fc.get("confidence", "low"))
	_trends_title.text = "📈 %s · 🔮 %s" % [tr("UI_DASHBOARD_TRENDS_TITLE"),
		tr("UI_DASHBOARD_FORECAST") % tr("UI_CONFIDENCE_" + ("HIGH" if conf == "high" else "MEDIUM" if conf == "medium" else "LOW"))]
	var biomass: Array[float] = []
	for v in _sim.get_analytics_data("total_biomass_kg", 30):
		biomass.append(v / 1000.0)
	var fbio: Array[float] = []
	for v in Array(fc.get("biomass_kg", [])):
		fbio.append(float(v) / 1000.0)
	_set_chart(_chart_biomass, tr("UI_CHART_BIOMASS_T"), biomass, "total_biomass_kg", -1.0, "%.0f", Color("#5B8FA3"), fbio)
	var price: Array[float] = _sim.get_analytics_data("market_price_nok", 30)
	_set_chart(_chart_price, tr("UI_CHART_PRICE_NOK_KG"), price, "market_price_nok", -1.0, "%.0f", Color("#2E6B45"))
	var lice: Array[float] = _sim.get_analytics_data("lice_per_fish", 30)
	var flice: Array[float] = []
	for v in Array(fc.get("lice", [])):
		flice.append(float(v))
	_set_chart(_chart_lice, tr("UI_CHART_LICE_LIMIT") % SimFormatters.num(SimState.LICE_LEGAL_LIMIT, 1), lice,
		"lice_per_fish", SimState.LICE_LEGAL_LIMIT, "%.2f", Color("#B3172F"), flice)
	var fcr: Array[float] = _sim.get_analytics_data("fcr", 30)
	var fcr_target: float = float(_sim.fcr_target)
	_set_chart(_chart_fcr, tr("UI_CHART_FCR_TARGET") % SimFormatters.num(fcr_target, 2), fcr, "fcr", fcr_target,
		"%.2f", Color("#8A5A00"))


func _set_chart(chart: Control, title: String, values: Array[float], metric: String, threshold: float, fmt: String, color: Color, future: Array[float] = []) -> void:
	if chart == null or not chart.has_method("set_series"):
		return
	chart.set("title", title)
	chart.set("threshold", threshold)
	chart.set("unit_fmt", fmt)
	chart.set("line_color", color)
	var ma: Array[float] = _sim.get_analytics_moving_average(metric, 30, 7) if _sim.has_method("get_analytics_moving_average") else []
	chart.call("set_series", values, ma)
	if chart.has_method("set_future"):
		chart.call("set_future", future)


func _refresh_finance() -> void:
	if not _sim or not _sim.has_method("get_analytics_data") or _fin_chart == null:
		return
	# U1-05: the revenue and costs the sim booked, block by block over the
	# last FIN_DAYS days (the card used to bucket the cash balance under a
	# "6 months" title with income/cost colours).
	var revenue: Array[float] = _sim.get_analytics_data("revenue_nok", FIN_DAYS + 1)
	var costs: Array[float] = _sim.get_analytics_data("expenses_nok", FIN_DAYS + 1)
	var data := PeriodBarChart.periods(revenue, costs, int(_sim.current_day), FIN_BLOCK_DAYS, FIN_DAYS / FIN_BLOCK_DAYS,
		revenue.size() < FIN_DAYS + 1)
	_fin_chart.set_periods(data["labels"], data["income"], data["costs"])


## ROC ops card (Phase 4.2): budget + per-site watch/scan/auto cycle buttons.
const WATCH_ORDER := ["watch", "scan", "auto"]
const WATCH_ICON := {"watch": "👁️", "scan": "🔍", "auto": "🤖"}
const WATCH_COST := {"watch": 25, "scan": 10, "auto": 0}
## Extra feed spilled under "auto" watch (engine: RemoteOpsCentre).
const AUTO_FEED_SPILL := 0.08

const NIGHT_WATCH_ORDER := ["Standard", "ScanOnly", "AutomatedROC"]
const NIGHT_WATCH_ICON := {"Standard": "👁️", "ScanOnly": "🔍", "AutomatedROC": "🤖"}
const NIGHT_WATCH_COST := {"Standard": 0, "ScanOnly": 0, "AutomatedROC": 85_000}

var _ops_signature: Array = []

func _refresh_ops() -> void:
	if _ops_list == null or not _sim:
		return
	var budget: Dictionary = _sim.get_attention_budget() if _sim.has_method("get_attention_budget") else {}
	var b: int = int(budget.get("budget", 100))
	var used: int = int(budget.get("used", 0))
	var pens: Array = _sim.get_all_pens() if _sim.has_method("get_all_pens") else []
	# Diff, not rebuild (U2-07): the rows change when a site or its watch does.
	var signature: Array = [b, used]
	for site in pens:
		signature.append([str(site.get("id", "")), str(site.get("name", "")), str(site.get("watch_mode", "watch"))])
	if signature != _ops_signature:
		_ops_signature = signature
		_rebuild_ops(pens, b, used)
	_sync_policy_option()


func _rebuild_ops(pens: Array, b: int, used: int) -> void:
	for child in _ops_list.get_children():
		child.queue_free()
	_ops_title.text = tr("UI_DASHBOARD_OPS_TITLE")
	_ops_budget.text = tr("UI_DASHBOARD_OPS_BUDGET") % [used, b, int(WATCH_COST["watch"]), int(WATCH_COST["scan"]),
		int(WATCH_COST["auto"]), SimFormatters.percent(AUTO_FEED_SPILL)]
	_ops_budget.add_theme_color_override("font_color",
		Color("#B3172F") if used > b else Color("#5A6168"))
	for site in pens:
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 8)
		_ops_list.add_child(row)
		var name_lbl := Label.new()
		name_lbl.text = str(site.get("name", "?"))
		name_lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		LakskongenTheme.set_size(name_lbl, 13)
		row.add_child(name_lbl)
		var mode := str(site.get("watch_mode", "watch"))
		var btn := Button.new()
		btn.text = "%s %s (%d)" % [str(WATCH_ICON.get(mode, "?")), SimLabels.watch_mode(mode), int(WATCH_COST.get(mode, 0))]
		var sid := str(site.get("id", ""))
		var nxt: String = WATCH_ORDER[(WATCH_ORDER.find(mode) + 1) % WATCH_ORDER.size()]
		btn.tooltip_text = tr("UI_DASHBOARD_WATCH_SWITCH") % SimLabels.watch_mode(nxt)
		btn.pressed.connect(_on_watch_cycle.bind(sid, nxt))
		row.add_child(btn)


## Night-shift policy selector (Phase 5.2). Items + signal are set up once in
## _setup_policy_option(); a refresh only mirrors the engine's policy.
func _sync_policy_option() -> void:
	if _policy_option:
		var status: Dictionary = _sim.get_night_shift_status() if _sim.has_method("get_night_shift_status") else {}
		var idx := NIGHT_SHIFT_POLICIES.find(str(status.get("policy", "Standard")))
		if idx >= 0 and idx < _policy_option.item_count:
			_policy_option.select(idx)


func _on_watch_cycle(site_id: String, mode: String) -> void:
	if _sim and _sim.has_method("set_watch_mode"):
		_sim.set_watch_mode(site_id, mode)

## Populate the night-shift dropdown once. The scene ships it empty; selecting
## on an empty OptionButton errors, and `pressed` never carried the index.
func _setup_policy_option() -> void:
	if _policy_option == null or _policy_option.item_count > 0:
		return
	for i in NIGHT_SHIFT_POLICIES.size():
		var policy: String = NIGHT_SHIFT_POLICIES[i]
		_policy_option.add_item(SimLabels.night_shift(policy), i)
	_policy_option.item_selected.connect(_on_policy_changed)

func _on_policy_changed(idx: int) -> void:
	if _sim and idx >= 0 and idx < NIGHT_SHIFT_POLICIES.size():
		_sim.set_night_shift_policy(NIGHT_SHIFT_POLICIES[idx])


# ══════════════════════════════════════════════════════════════════════════════
# COUNT-UP JUICE (display only)
# ══════════════════════════════════════════════════════════════════════════════

## Tween a KPI label from one value to another, formatting every frame with
## `fmt` (a Callable float -> String, locale-aware through SimFormatters).
## One tween per label (a day's new count-up replaces the last one); at 10×
## and 30× the number snaps to its value.
var _count_tweens: Dictionary = {}

func _count_up(lbl: Label, from_val: float, to_val: float, fmt: Callable) -> void:
	if lbl == null:
		return
	var running: Tween = _count_tweens.get(lbl)
	if running != null and running.is_valid():
		running.kill()
	if is_equal_approx(from_val, to_val) or _fast_clock():
		lbl.text = fmt.call(to_val)
		return
	var tween := lbl.create_tween()
	tween.set_ease(Tween.EASE_OUT).set_trans(Tween.TRANS_CUBIC)
	tween.tween_method(func(v: float): lbl.text = fmt.call(v), from_val, to_val, 0.6)
	_count_tweens[lbl] = tween
