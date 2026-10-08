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

@onready var _btn_feed_all: Button = %BtnFeedAll
@onready var _btn_harvest: Button = %BtnHarvest
@onready var _btn_delouse: Button = %BtnDelouse
@onready var _btn_reports: Button = %BtnReports
@onready var _btn_research: Button = %BtnResearch
@onready var _btn_map: Button = %BtnMap
@onready var _btn_hire: Button = %BtnHire
@onready var _btn_contracts: Button = %BtnContracts

# Pen mini-cards are static scene nodes; collected into an array in _ready.
var _pen_cards: Array[Dictionary] = []

var _fin_bars_revenue: Array[ColorRect] = []
var _fin_bars_cost: Array[ColorRect] = []
var _fin_month_labels: Array[Label] = []

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
	_build_state_banner()
	_build_kpi_sparklines()
	_apply_localization()
	_apply_theme_fonts()
	_connect_quick_actions()
	_refresh_all()

func _input(event: InputEvent) -> void:
	if visible and event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()

# ── Signal handlers ───────────────────────────────────────────────────────────

func _on_day_advanced(_day: int, _season: String) -> void:
	_refresh_all()

func _on_state_updated() -> void:
	_refresh_all()

func _on_quick_action(action: String) -> void:
	# Juice-only feedback: toast + button pop + route to the relevant tab.
	# No simulation calls are made here; panels trigger sim actions themselves.
	_pop_button(action)
	match action:
		"feed_all":
			_toast(_tr_or("UI_TOAST_FEED_ALL", "🐟 Fôrer alle merder …"), "INFO")
			_route_to_tab("pens")
		"harvest":
			_toast(_tr_or("UI_TOAST_HARVEST_START", "📦 Klar for høsting — velg en merd"), "INFO")
			_route_to_tab("pens")
		"delouse":
			_toast(_tr_or("UI_TOAST_DELOUSE_START", "🧪 Velg en merd for avlusing"), "INFO")
			_route_to_tab("pens")
		"reports":
			_route_to_tab("finance")
		"research":
			_toast(_tr_or("UI_TOAST_RESEARCH_OPEN", "🔬 Forskningsstreet åpnes"), "INFO")
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
		"feed_all": btn = _btn_feed_all
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
	_pen_cards.clear()
	for i in range(1, 7):
		var suffix := str(i)
		_pen_cards.append({
			"panel": get_node_or_null("%PenCard" + suffix),
			"status_dot": get_node_or_null("%Pen" + suffix + "Status"),
			"name_label": get_node_or_null("%Pen" + suffix + "Name"),
			"count_value": get_node_or_null("%Pen" + suffix + "Count"),
			"weight_value": get_node_or_null("%Pen" + suffix + "Weight"),
			"temp_value": get_node_or_null("%Pen" + suffix + "Temp"),
		})
	_fin_bars_revenue.clear()
	_fin_bars_cost.clear()
	_fin_month_labels.clear()
	for i in range(1, 7):
		var suffix := str(i)
		_fin_bars_revenue.append(get_node_or_null("%FinRev" + suffix) as ColorRect)
		_fin_bars_cost.append(get_node_or_null("%FinCost" + suffix) as ColorRect)
		# MonthLbl nodes share one % name; resolve via parent path instead.
		_fin_month_labels.append(get_node_or_null("Margin/MainScroll/RootVBox/Row2/FinCard/FinOuter/FinBody/FinChart/Month" + suffix + "/MonthLbl") as Label)

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
	root_vbox.add_child(_state_banner)
	root_vbox.move_child(_state_banner, mini(1, root_vbox.get_child_count() - 1))

func _connect_quick_actions() -> void:
	_btn_feed_all.pressed.connect(_on_quick_action.bind("feed_all"))
	_btn_harvest.pressed.connect(_on_quick_action.bind("harvest"))
	_btn_delouse.pressed.connect(_on_quick_action.bind("delouse"))
	_btn_reports.pressed.connect(_on_quick_action.bind("reports"))
	_btn_research.pressed.connect(_on_quick_action.bind("research"))
	_btn_map.pressed.connect(_on_quick_action.bind("map"))
	_btn_hire.pressed.connect(_on_quick_action.bind("hire"))
	_btn_contracts.pressed.connect(_on_quick_action.bind("contracts"))

func _apply_localization() -> void:
	_title_label.text = _tr_or("UI_DASHBOARD_TITLE", "Fjordlaks AS — Oversikt")
	_pens_title.text = tr("🏗️ Merdoversikt")
	_water_title.text = tr("💧 Vannkvalitet")
	_weather_title.text = tr("🌤️ Vær")
	_fin_title.text = tr("💰 Økonomi (6 mnd)")
	_alerts_title.text = tr("⚠️ Varsler")
	_actions_title.text = tr("⚡ Hurtigvalg")
	_btn_feed_all.text = tr("🐟 Fôr alle")
	_btn_harvest.text = tr("📦 Høst")
	_btn_delouse.text = tr("🧪 Avlus")
	_btn_reports.text = tr("📊 Rapporter")
	_btn_research.text = tr("🔬 Forskning")
	_btn_map.text = tr("🗺️ Kart")
	_btn_hire.text = tr("👷 Ansett")
	_btn_contracts.text = tr("📋 Kontrakter")

func _tr_or(key: String, fallback: String) -> String:
	var t := tr(key)
	return t if t != key else fallback

func _apply_theme_fonts() -> void:
	if not _theme:
		return
	if _theme.font_heading_bold:
		_title_label.add_theme_font_override("font", _theme.font_heading_bold)
		_kpi_biomass_value.add_theme_font_override("font", _theme.font_heading_bold)
		_kpi_revenue_value.add_theme_font_override("font", _theme.font_heading_bold)
		_kpi_health_value.add_theme_font_override("font", _theme.font_heading_bold)
		_kpi_fcr_value.add_theme_font_override("font", _theme.font_heading_bold)
		_weather_temp.add_theme_font_override("font", _theme.font_heading_bold)
	if _theme.font_heading_semibold:
		_pens_title.add_theme_font_override("font", _theme.font_heading_semibold)
		_water_title.add_theme_font_override("font", _theme.font_heading_semibold)
		_weather_title.add_theme_font_override("font", _theme.font_heading_semibold)
		_fin_title.add_theme_font_override("font", _theme.font_heading_semibold)
		_alerts_title.add_theme_font_override("font", _theme.font_heading_semibold)
		_actions_title.add_theme_font_override("font", _theme.font_heading_semibold)
		for pen in _pen_cards:
			if pen.name_label:
				(pen.name_label as Label).add_theme_font_override("font", _theme.font_heading_semibold)
	if _theme.font_mono:
		_water_temp.add_theme_font_override("font", _theme.font_mono)
		_water_oxygen.add_theme_font_override("font", _theme.font_mono)
		_water_salinity.add_theme_font_override("font", _theme.font_mono)
		_water_ph.add_theme_font_override("font", _theme.font_mono)
		for pen in _pen_cards:
			for key in ["count_value", "weight_value", "temp_value"]:
				if pen[key]:
					(pen[key] as Label).add_theme_font_override("font", _theme.font_mono)

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
		["biomass", _kpi_biomass_value, "total_biomass_kg", Color("#2E86AB"), "%.0f"],
		["revenue", _kpi_revenue_value, "balance_nok", Color("#8FAD78"), "%.1f"],
		["health", _kpi_health_value, "sustainability_score", Color("#88C0D0"), "%.0f"],
		["fcr", _kpi_fcr_value, "fcr", Color("#D4B06A"), "%.2f"],
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
	if lbl == null or is_zero_approx(prev_val):
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
	var summary := _tr_or("UI_FARM_STATE_%s" % state.to_upper(), str(ev.get("summary_no", "")))
	var detail := str(ev.get("detail_no", ""))
	if _state_banner and is_instance_valid(_state_banner):
		var title := "%s %s" % [FarmStateBanner.icon_for(state), summary]
		_state_banner.set_state(state, title, detail)

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
		"up": return "▲ Stigende"
		"down": return "▼ Fallende"
		_: return "─ Stabil"

func _trend_color(trend: String, good_when_up: bool) -> Color:
	if trend == "stable":
		return Color("#7B8794")
	var good := (trend == "up") == good_when_up
	return Color("#8FAD78") if good else Color("#A54E56")

func _refresh_kpis() -> void:
	if not _sim:
		return

	# Biomass (juice: count-up tween, display only)
	var biomass_tonnes: float = float(_sim.fish_biomass_kg) / 1000.0
	var biomass_fmt := _tr_or("UI_DASHBOARD_BIOMASS_FORMAT", "%s tonn")
	_flash_kpi(_kpi_biomass_value, biomass_tonnes, _prev_biomass, true)
	_count_up(_kpi_biomass_value, _prev_biomass, biomass_tonnes, biomass_fmt)
	_prev_biomass = biomass_tonnes
	var biomass_trend: String = _sim.get_analytics_trend("total_biomass_kg") if _sim.has_method("get_analytics_trend") else "stable"
	_kpi_biomass_change.text = _trend_text(biomass_trend)
	_kpi_biomass_change.add_theme_color_override("font_color", _trend_color(biomass_trend, true))

	# Revenue (juice: count-up tween, display only)
	var revenue: float = float(_sim.get_revenue())
	var revenue_m := revenue / 1_000_000.0
	var revenue_fmt := _tr_or("UI_DASHBOARD_REVENUE_FORMAT", "%.1f MNOK")
	_flash_kpi(_kpi_revenue_value, revenue_m, _prev_revenue, true)
	_count_up(_kpi_revenue_value, _prev_revenue, revenue_m, revenue_fmt)
	_prev_revenue = revenue_m
	var balance_trend: String = _sim.get_analytics_trend("balance_nok") if _sim.has_method("get_analytics_trend") else "stable"
	_kpi_revenue_change.text = _trend_text(balance_trend)
	_kpi_revenue_change.add_theme_color_override("font_color", _trend_color(balance_trend, true))

	# Health (use sustainability score as proxy)
	var health: int = int(_sim.sustainability_score)
	_kpi_health_value.text = "%d%%" % health
	_flash_kpi(_kpi_health_value, float(health), _prev_health, true, 0.01)
	_prev_health = float(health)
	if health >= 90:
		_kpi_health_change.text = _tr_or("UI_DASHBOARD_HEALTH_GOOD", "▲ God status")
		_kpi_health_change.add_theme_color_override("font_color", Color("#8FAD78"))
	elif health >= 70:
		_kpi_health_change.text = _tr_or("UI_DASHBOARD_HEALTH_STABLE", "─ Stabil")
		_kpi_health_change.add_theme_color_override("font_color", Color("#D4B06A"))
	else:
		_kpi_health_change.text = _tr_or("UI_DASHBOARD_HEALTH_LOW", "▼ Lav")
		_kpi_health_change.add_theme_color_override("font_color", Color("#A54E56"))

	# FCR — live from FeedSystem via SimState (target <1.30).
	var fcr: float = float(_sim.fcr)
	_kpi_fcr_value.text = "%.2f" % fcr
	_flash_kpi(_kpi_fcr_value, fcr, _prev_fcr, false)
	_prev_fcr = fcr
	var fcr_trend: String = _sim.get_analytics_trend("fcr") if _sim.has_method("get_analytics_trend") else "stable"
	if fcr <= 1.15:
		_kpi_fcr_change.text = _tr_or("UI_DASHBOARD_FCR_BETTER", "▲ Bedre enn mål (1.15)")
		_kpi_fcr_change.add_theme_color_override("font_color", Color("#8FAD78"))
	elif fcr <= 1.30:
		_kpi_fcr_change.text = "%s · %.2f" % [_trend_text(fcr_trend), fcr]
		_kpi_fcr_change.add_theme_color_override("font_color", Color("#D4B06A"))
	else:
		_kpi_fcr_change.text = _tr_or("UI_DASHBOARD_FCR_BAD", "▼ Over mål — sjekk fôring")
		_kpi_fcr_change.add_theme_color_override("font_color", Color("#A54E56"))

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
		_kpi_biomass_value.tooltip_text = (
			"Stående biomasse = antall fisk × gjennomsnittsvekt.\n"
			+ "Nå: %s fisk × %.2f kg ≈ %s tonn.\n"
			+ "Telles per merd; påvirkes av tilvekst, dødelighet og rømming."
		) % [_format_number(fish), avg_w, _format_number(int(round(biomass_tonnes)))]

	if is_instance_valid(_kpi_revenue_value):
		_kpi_revenue_value.tooltip_text = (
			"Akkumulert saldo (driftsresultat) = salgsinntekter − kostnader.\n"
			+ "Nå: %.1f MNOK.\n"
			+ "Påvirkes av pris (NOK/kg), høstet volum, fôr- og behandlingskostnader."
		) % revenue_m

	if is_instance_valid(_kpi_health_value):
		_kpi_health_value.tooltip_text = (
			"Bærekraftsscore (0–100 %%) = vektet sum av lusehåndtering, utslipp,\n"
			+ "rømming og dyrevelferd.\n"
			+ "Nå: %d %%. ≥90 grønn · 70–89 gul · <70 rød."
		) % health

	if is_instance_valid(_kpi_fcr_value):
		_kpi_fcr_value.tooltip_text = (
			"Fôrfaktor (FCR) = fôr brukt (kg) / biomassevekst (kg).\n"
			+ "Lavere er bedre; mål < 1,30. Nå: %.2f.\n"
			+ "Faktorer: vanntemperatur, fôrkvalitet (protein/astaxantin), tetthet og oksygen."
		) % fcr

func _refresh_pens() -> void:
	if not _sim:
		return

	var pens: Array = _sim.get_all_pens() if _sim.has_method("get_all_pens") else []
	var temp: float = float(_sim.temperature_c)
	var lice: float = float(_sim.sea_lice_per_fish)

	for i in range(min(_pen_cards.size(), 6)):
		var pen: Dictionary = _pen_cards[i]
		if i < pens.size():
			var site: Dictionary = pens[i]
			var count: int = int(site.get("fish_count", 0))
			var biomass: float = float(site.get("biomass_kg", 0.0))
			var max_bio: float = float(site.get("max_biomass", 0.0))
			var escapes: int = int(site.get("escapes_this_year", 0))
			if pen.name_label:
				(pen.name_label as Label).text = str(site.get("name", "Merd %d" % (i + 1)))
			var avg_w: float = biomass / max(count, 1)
			(pen.count_value as Label).text = _format_number(count)
			(pen.weight_value as Label).text = "%.1f kg" % avg_w if count > 0 else "--"
			(pen.temp_value as Label).text = "%.1f°C" % temp
			var dot_color := Color("#A3BE8C")
			if escapes > 0 or (max_bio > 0.0 and biomass > max_bio):
				dot_color = Color("#BF616A")
			elif lice > 0.2 or (max_bio > 0.0 and biomass > max_bio * 0.8):
				dot_color = Color("#EBCB8B")
			(pen.status_dot as ColorRect).color = dot_color
		else:
			if pen.name_label:
				var pen_fmt := _tr_or("UI_DASHBOARD_PEN_NAME", "Merd %d")
				(pen.name_label as Label).text = pen_fmt % (i + 1)
			(pen.count_value as Label).text = tr("Tom")
			(pen.weight_value as Label).text = "--"
			(pen.temp_value as Label).text = "--"
			(pen.status_dot as ColorRect).color = Color("#D8DEE9")

func _refresh_water() -> void:
	if not _sim:
		return

	var temp: float = float(_sim.temperature_c)
	_water_temp.text = "%.1f°C" % temp
	var o2: float = float(_sim.oxygen_mgl)
	_water_oxygen.text = "%.1f mg/L" % o2 if o2 == o2 and o2 > 0.0 else "–"
	if o2 == o2 and o2 > 0.0:
		if o2 < 5.0:
			_water_oxygen.add_theme_color_override("font_color", Color("#A54E56"))
		elif o2 < 6.5:
			_water_oxygen.add_theme_color_override("font_color", Color("#D4B06A"))
		else:
			_water_oxygen.add_theme_color_override("font_color", Color("#2E3440"))
	_water_salinity.text = "%.1f m" % float(_sim.wave_m)
	var ph: float = float(_sim.water_ph)
	_water_ph.text = "%.1f" % ph if ph == ph and ph > 0.0 else "–"

func _refresh_weather() -> void:
	if not _sim:
		return

	var temp: float = float(_sim.temperature_c)
	_weather_temp.text = "%d°C" % int(temp)

	var weather_state = _sim.get_current_weather_state()
	match weather_state:
		"Calm":
			_weather_icon.text = "☀️"
			_weather_desc.text = tr("Klart")
		"Moderate":
			_weather_icon.text = "⛅"
			_weather_desc.text = tr("Delvis skyet")
		"Windy":
			_weather_icon.text = "💨"
			_weather_desc.text = tr("Vind")
		"Storm":
			_weather_icon.text = "⛈️"
			_weather_desc.text = tr("Storm")
		"Severe":
			_weather_icon.text = "🌪️"
			_weather_desc.text = tr("Ekstremvær")
		_:
			_weather_icon.text = "⛅"
			_weather_desc.text = tr("Delvis skyet")

	# Live wind/wave + honest Markov-chain storm forecast (Phase 1.2).
	_weather_wind.text = "%.0f m/s" % float(_sim.wind_ms)
	_weather_wave.text = "%.1f m" % float(_sim.wave_m)
	var risk: float = float(_sim.storm_risk_3d)
	if risk > 0.15:
		_weather_desc.text += " · ☔ %d%% (3d)" % int(risk * 100.0)
		_weather_desc.add_theme_color_override("font_color", Color("#A54E56") if risk > 0.5 else Color("#D4B06A"))
	else:
		_weather_desc.add_theme_color_override("font_color", Color("#2E3440"))
	_weather_visibility.text = "%.0f%% nøter" % float(_sim.min_net_integrity)
	_weather_humidity.text = "Lus %.2f" % float(_sim.sea_lice_per_fish)

func _refresh_alerts() -> void:
	if not _alerts_container:
		return

	# Clear existing alerts
	for child in _alerts_container.get_children():
		child.queue_free()

	# Real alerts: live SimState thresholds first, then recent sim events.
	var alerts: Array[Dictionary] = []
	if _sim:
		for a in _sim.active_alerts:
			var lvl: String = str(a.get("level", "info"))
			alerts.append({"text": str(a.get("message", "")), "level": lvl, "time": "Nå"})
		for e in _sim.recent_events:
			var desc: String = str(e.get("description", ""))
			if desc.is_empty():
				continue
			alerts.append({"text": "%s — %s" % [str(e.get("name", "Hendelse")), desc], "level": "info", "time": "Dag %d" % int(e.get("day", 0))})
		alerts = alerts.slice(0, 6)

	if alerts.is_empty():
		var empty := Label.new()
		empty.text = _tr_or("UI_DASHBOARD_ALERTS_EMPTY", "🎉 Ingen aktive varsler — alt ser bra ut")
		empty.add_theme_font_size_override("font_size", 13)
		empty.add_theme_color_override("font_color", Color("#7B8794"))
		empty.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		_alerts_container.add_child(empty)
		return

	for alert in alerts:
		var hbox := HBoxContainer.new()
		hbox.add_theme_constant_override("separation", 12)
		_alerts_container.add_child(hbox)

		var dot := ColorRect.new()
		dot.custom_minimum_size = Vector2(8, 8)
		match alert.level:
			"warning": dot.color = Color("#EBCB8B")
			"danger", "critical": dot.color = Color("#BF616A")
			"info": dot.color = Color("#88C0D0")
			_: dot.color = Color("#7B8794")
		hbox.add_child(dot)

		var text_vbox := VBoxContainer.new()
		text_vbox.add_theme_constant_override("separation", 2)
		text_vbox.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		hbox.add_child(text_vbox)

		var text_lbl := Label.new()
		text_lbl.text = alert.text
		text_lbl.add_theme_font_size_override("font_size", 13)
		text_lbl.add_theme_color_override("font_color", Color("#2E3440"))
		text_lbl.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		text_vbox.add_child(text_lbl)

		var time_lbl := Label.new()
		time_lbl.text = alert.time
		time_lbl.add_theme_font_size_override("font_size", 11)
		time_lbl.add_theme_color_override("font_color", Color("#7B8794"))
		text_vbox.add_child(time_lbl)


func _refresh_charts() -> void:
	if not _sim or not _sim.has_method("get_analytics_data"):
		return
	_trends_title.text = _tr_or("UI_DASHBOARD_TRENDS_TITLE", "📈 Trender (30 dager)")
	var fc: Dictionary = _sim.get_forecast() if _sim.has_method("get_forecast") else {}
	var conf := str(fc.get("confidence", "low"))
	_trends_title.text += " · 🔮 7d (%s konfidens)" % {"high": "høy", "medium": "middels"}.get(conf, "lav")
	var biomass: Array[float] = []
	for v in _sim.get_analytics_data("total_biomass_kg", 30):
		biomass.append(v / 1000.0)
	var fbio: Array[float] = []
	for v in Array(fc.get("biomass_kg", [])):
		fbio.append(float(v) / 1000.0)
	_set_chart(_chart_biomass, "Biomasse (tonn)", biomass, "total_biomass_kg", -1.0, "%.0f", Color("#2E86AB"), fbio)
	var price: Array[float] = _sim.get_analytics_data("market_price_nok", 30)
	_set_chart(_chart_price, "Pris (NOK/kg)", price, "market_price_nok", -1.0, "%.0f", Color("#8FAD78"))
	var lice: Array[float] = _sim.get_analytics_data("lice_per_fish", 30)
	var flice: Array[float] = []
	for v in Array(fc.get("lice", [])):
		flice.append(float(v))
	_set_chart(_chart_lice, "Lus/fisk (grense 0,5)", lice, "lice_per_fish", 0.5, "%.2f", Color("#A54E56"), flice)
	var fcr: Array[float] = _sim.get_analytics_data("fcr", 30)
	_set_chart(_chart_fcr, "FCR (mål 1,30)", fcr, "fcr", 1.3, "%.2f", Color("#D4B06A"))


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
	if not _sim or not _sim.has_method("get_analytics_data"):
		return
	if _fin_bars_revenue.is_empty():
		return
	# Six buckets over the last 30 days of balance history: green = net gain,
	# red = net loss per bucket. Honest label: saldoendring, not revenue/cost.
	var series: Array[float] = _sim.get_analytics_data("balance_nok", 30)
	if series.size() < 2:
		return
	var bucket := maxi(1, series.size() / 6)
	var max_abs := 1.0
	var deltas: Array[float] = []
	for b in range(6):
		var start_i: int = mini(b * bucket, series.size() - 1)
		var end_i: int = mini((b + 1) * bucket - 1, series.size() - 1)
		var d: float = series[end_i] - series[start_i]
		deltas.append(d)
		max_abs = max(max_abs, absf(d))
	for b in range(6):
		var frac: float = deltas[b] / max_abs
		var h: float = clampf(absf(frac) * 64.0, 2.0 if not is_zero_approx(deltas[b]) else 0.0, 64.0)
		var rev: ColorRect = _fin_bars_revenue[b] if b < _fin_bars_revenue.size() else null
		var cost: ColorRect = _fin_bars_cost[b] if b < _fin_bars_cost.size() else null
		if rev:
			rev.custom_minimum_size = Vector2(rev.custom_minimum_size.x, h if deltas[b] >= 0.0 else 0.0)
			rev.visible = deltas[b] >= 0.0
		if cost:
			cost.custom_minimum_size = Vector2(cost.custom_minimum_size.x, h if deltas[b] < 0.0 else 0.0)
			cost.visible = deltas[b] < 0.0
		if b < _fin_month_labels.size() and _fin_month_labels[b]:
			_fin_month_labels[b].text = "D-%d" % (series.size() - mini((b + 1) * bucket, series.size()))


## ROC ops card (Phase 4.2): budget + per-site watch/scan/auto cycle buttons.
const WATCH_ORDER := ["watch", "scan", "auto"]
const WATCH_ICON := {"watch": "👁️", "scan": "🔍", "auto": "🤖"}
const WATCH_COST := {"watch": 25, "scan": 10, "auto": 0}

const NIGHT_WATCH_ORDER := ["Standard", "ScanOnly", "AutomatedROC"]
const NIGHT_WATCH_ICON := {"Standard": "👁️", "ScanOnly": "🔍", "AutomatedROC": "🤖"}
const NIGHT_WATCH_COST := {"Standard": 0, "ScanOnly": 0, "AutomatedROC": 85_000}

func _refresh_ops() -> void:
	if _ops_list == null or not _sim:
		return
	for child in _ops_list.get_children():
		child.queue_free()
	var budget: Dictionary = _sim.get_attention_budget() if _sim.has_method("get_attention_budget") else {}
	var b: int = int(budget.get("budget", 100))
	var used: int = int(budget.get("used", 0))
	_ops_title.text = _tr_or("UI_DASHBOARD_OPS_TITLE", "🎛️ Driftssentral")
	_ops_budget.text = "Brukt %d/%d oppmerksomhet (👁️ 25 · 🔍 10 · 🤖 0 +8%% spill)" % [used, b]
	_ops_budget.add_theme_color_override("font_color",
		Color("#A54E56") if used > b else Color("#7B8794"))
	var pens: Array = _sim.get_all_pens() if _sim.has_method("get_all_pens") else []
	for site in pens:
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 8)
		_ops_list.add_child(row)
		var name_lbl := Label.new()
		name_lbl.text = str(site.get("name", "?"))
		name_lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		name_lbl.add_theme_font_size_override("font_size", 13)
		_ops_list.add_child(row)
		row.add_child(name_lbl)
		var mode := str(site.get("watch_mode", "watch"))
		var btn := Button.new()
		btn.text = "%s %s (%d)" % [str(WATCH_ICON.get(mode, "?")), mode.capitalize(), int(WATCH_COST.get(mode, 0))]
		var sid := str(site.get("id", ""))
		var nxt: String = WATCH_ORDER[(WATCH_ORDER.find(mode) + 1) % WATCH_ORDER.size()]
		btn.tooltip_text = "Bytt til %s" % nxt
		btn.pressed.connect(_on_watch_cycle.bind(sid, nxt))
		row.add_child(btn)

# Night-shift policy selector (Phase 5.2)
	if _policy_option:
		var status: Dictionary = _sim.get_night_shift_status() if _sim.has_method("get_night_shift_status") else {}
		var current: String = str(status.get("policy", "Standard"))
		var idx := ["Standard", "ScanOnly", "AutomatedROC"].find(current)
		if idx >= 0:
			_policy_option.select(idx)
		_policy_option.pressed.connect(_on_policy_changed)


func _on_watch_cycle(site_id: String, mode: String) -> void:
	if _sim and _sim.has_method("set_watch_mode"):
		_sim.set_watch_mode(site_id, mode)

func _on_policy_changed(_idx: int) -> void:
	if _sim and _sim.has_method("set_night_shift_policy"):
		var policies := ["Standard", "ScanOnly", "AutomatedROC"]
		var policy := String(policies[_idx])
		_sim.set_night_shift_policy(policy)


# ══════════════════════════════════════════════════════════════════════════════
# COUNT-UP JUICE (display only)
# ══════════════════════════════════════════════════════════════════════════════

## Tween a KPI label from one value to another. Supports "%s tonn"-style
## formats (integer with thousand separators) and "%.1f MNOK"-style floats.
func _count_up(lbl: Label, from_val: float, to_val: float, fmt: String) -> void:
	if lbl == null:
		return
	if is_equal_approx(from_val, to_val):
		lbl.text = _apply_kpi_fmt(fmt, to_val)
		return
	var tween := lbl.create_tween()
	tween.set_ease(Tween.EASE_OUT).set_trans(Tween.TRANS_CUBIC)
	tween.tween_method(func(v: float): lbl.text = _apply_kpi_fmt(fmt, v), from_val, to_val, 0.6)

func _apply_kpi_fmt(fmt: String, v: float) -> String:
	if "%s" in fmt:
		return fmt % _format_number(int(v))
	return fmt % v

# ══════════════════════════════════════════════════════════════════════════════
# UTILITIES
# ══════════════════════════════════════════════════════════════════════════════

func _format_number(n: int) -> String:
	var s := str(abs(n))
	var result := ""
	var count := 0
	for i in range(s.length() - 1, -1, -1):
		if count > 0 and count % 3 == 0:
			result = " " + result
		result = s[i] + result
		count += 1
	if n < 0:
		result = "-" + result
	return result
