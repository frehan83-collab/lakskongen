## WeatherDiseasePanel v2.1 — Nordic Minimalist (scene-based).
## Real-time weather and disease outbreak notification system.
## Displays current weather conditions, alerts, and disease treatment options.
## Layout lives in scenes/ui/WeatherDiseasePanel.tscn; this script is wiring only:
## @onready % refs, signal hookups, localization, theme fonts, data refresh.
## Alert cards and treatment rows are data-driven and still populated in code.

extends PanelContainer
class_name WeatherDiseasePanel

# ── Scene refs ────────────────────────────────────────────────────────────────
@onready var _title_label: Label = %TitleLabel
@onready var _weather_section_title: Label = %WeatherHeader
@onready var _alert_section_title: Label = %AlertHeader
@onready var _disease_section_title: Label = %DiseaseHeader
@onready var _treatment_section_title: Label = %TreatmentHeader

@onready var weather_status_label: Label = %WeatherStatusLabel
@onready var temperature_label: Label = %TemperatureLabel
@onready var weather_alert_container: VBoxContainer = %WeatherAlertList
@onready var disease_alert_container: VBoxContainer = %DiseaseAlertList
@onready var treatment_options_container: VBoxContainer = %TreatmentList
@onready var bloom_alert_container: VBoxContainer = %BloomAlertList
@onready var bloom_site_option: OptionButton = %BloomSiteOption
@onready var btn_bubble: Button = %BtnBubble
@onready var btn_diffuser: Button = %BtnDiffuser
@onready var btn_vaccinate: Button = %BtnVaccinate

# Alert history
var active_weather_alerts: Array[Dictionary] = []
var active_disease_alerts: Array[Dictionary] = []

# Color scheme
const COLOR_CRITICAL = Color("#BF616A")
const COLOR_WARNING = Color("#EBCB8B")
const COLOR_INFO = Color("#88C0D0")
const COLOR_TEXT = "#2E3440"
const COLOR_MUTED = "#7B8794"

var _theme: Node = null

# P10 gill-health early-warning card (built in code).
var _gill_container: VBoxContainer = null

# P11 bloom-mitigation catalog (built in code, additive to the existing buttons).
var _mitigation_list: VBoxContainer = null

func _ready():
	_apply_localization()
	_build_gill_card()
	_build_bloom_mitigation_list()
	_apply_theme_fonts()

	# Connect to GameBridge signals
	if has_node("/root/GameBridge"):
		var game_bridge = get_node("/root/GameBridge")
		game_bridge.WeatherEvent.connect(_on_weather_event)
		game_bridge.DiseaseOutbreak.connect(_on_disease_outbreak)
		game_bridge.GameStateUpdated.connect(_on_state_updated)
	else:
		push_warning("WeatherDiseasePanel: GameBridge not found")

	btn_bubble.pressed.connect(_on_deploy_countermeasure.bind("bubble"))
	btn_diffuser.pressed.connect(_on_deploy_countermeasure.bind("diffuser"))
	btn_vaccinate.pressed.connect(_on_vaccinate_pressed)

	_refresh_from_bridge()

func _input(event: InputEvent) -> void:
	if not visible:
		return

	if event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()

# ── Scene wiring ──────────────────────────────────────────────────────────────

func _apply_localization() -> void:
	_title_label.text = tr("🌊 Weather & Disease Monitoring")
	_weather_section_title.text = tr("Current Weather Conditions")
	_alert_section_title.text = tr("⚠️ Weather Alerts")
	_disease_section_title.text = tr("🦠 Disease Outbreaks")
	_treatment_section_title.text = tr("💊 Treatment Options")

func _apply_theme_fonts() -> void:
	_theme = get_node_or_null("/root/LakskongenTheme")
	if not _theme:
		return
	if _theme.font_heading_bold:
		_title_label.add_theme_font_override("font", _theme.font_heading_bold)
	if _theme.font_heading_semibold:
		_weather_section_title.add_theme_font_override("font", _theme.font_heading_semibold)
		_alert_section_title.add_theme_font_override("font", _theme.font_heading_semibold)
		_disease_section_title.add_theme_font_override("font", _theme.font_heading_semibold)
		_treatment_section_title.add_theme_font_override("font", _theme.font_heading_semibold)

## P10 gill-health card: per-site risk chip (green/amber/red). Built in code and
## inserted under the weather card; guards empty site lists.
func _build_gill_card() -> void:
	if _title_label == null:
		return
	var root: Node = _title_label.get_parent()
	if root == null:
		return
	var card := PanelContainer.new()
	card.name = "GillHealthCard"
	var sb := StyleBoxFlat.new()
	sb.bg_color = Color(1, 1, 1, 1)
	sb.border_color = Color(0.898039, 0.913725, 0.941176, 1)
	sb.set_border_width_all(1)
	sb.set_corner_radius_all(8)
	card.add_theme_stylebox_override("panel", sb)

	var outer := VBoxContainer.new()
	outer.add_theme_constant_override("separation", 0)
	card.add_child(outer)

	var header := Label.new()
	header.text = tr("🫁 Gjellehelse (tidligvarsling)")
	header.add_theme_font_size_override("font_size", 16)
	header.add_theme_color_override("font_color", Color(COLOR_TEXT))
	header.custom_minimum_size.y = 30
	outer.add_child(header)

	_gill_container = VBoxContainer.new()
	_gill_container.add_theme_constant_override("separation", 6)
	outer.add_child(_gill_container)

	root.add_child(card)
	root.move_child(card, 1)

func _refresh_gill_health() -> void:
	if _gill_container == null:
		return
	_clear_container(_gill_container)
	var sim := get_node_or_null("/root/SimBridge")
	if sim == null or not sim.has_method("get_gill_risk"):
		return
	var pens: Array = sim.get_all_pens() if sim.has_method("get_all_pens") else []
	if pens.is_empty():
		var none := Label.new()
		none.text = tr("Ingen lokaliteter — gjelleovervåking inaktiv")
		none.add_theme_color_override("font_color", Color(COLOR_MUTED))
		_gill_container.add_child(none)
		return
	for p in pens:
		var site_id := str(p.get("id", ""))
		if site_id.is_empty():
			continue
		var risk: Dictionary = sim.get_gill_risk(site_id)
		var r := float(risk.get("risk", 0.0))
		var critical := bool(risk.get("critical", false))
		var warning := bool(risk.get("warning", false))
		var color := COLOR_INFO
		var tag := "🟢"
		if critical:
			color = COLOR_CRITICAL
			tag = "🔴"
		elif warning:
			color = COLOR_WARNING
			tag = "🟡"
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 8)
		_gill_container.add_child(row)
		var name_lbl := Label.new()
		name_lbl.text = "%s %s" % [tag, str(p.get("name", site_id))]
		name_lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		name_lbl.add_theme_color_override("font_color", color)
		row.add_child(name_lbl)
		var chip := Label.new()
		var reason := str(risk.get("reason", ""))
		chip.text = "%d%% · %s" % [int(r * 100.0), tr(reason)]
		chip.add_theme_color_override("font_color", color)
		row.add_child(chip)

## Build the additive mitigation list container right below the existing bloom
## buttons. The buttons themselves keep their original behavior; this adds the
## catalog-driven view with cost + effectiveness for every mitigation kind.
func _build_bloom_mitigation_list() -> void:
	if btn_bubble == null:
		return
	var site_row: Node = btn_bubble.get_parent()
	var body: Node = site_row.get_parent() if site_row != null else null
	if body == null:
		return
	_mitigation_list = VBoxContainer.new()
	_mitigation_list.name = "BloomMitigationList"
	_mitigation_list.add_theme_constant_override("separation", 6)
	body.add_child(_mitigation_list)
	body.move_child(_mitigation_list, site_row.get_index() + 1)

## Rebuild the mitigation catalog rows for the currently selected site.
func _refresh_bloom_mitigations() -> void:
	if _mitigation_list == null:
		return
	_clear_container(_mitigation_list)
	var sim := get_node_or_null("/root/SimBridge")
	if sim == null or not sim.has_method("get_bloom_mitigations"):
		return
	var catalog: Array = sim.get_bloom_mitigations()
	if catalog.is_empty():
		return

	var sel_id := ""
	if bloom_site_option.selected >= 0 and bloom_site_option.selected < _bloom_site_ids.size():
		sel_id = _bloom_site_ids[bloom_site_option.selected]

	var title := Label.new()
	title.text = tr("Mottiltak: kostnad · effekt")
	title.add_theme_font_size_override("font_size", 13)
	title.add_theme_color_override("font_color", Color(COLOR_TEXT))
	_mitigation_list.add_child(title)

	for entry in catalog:
		var kind := str(entry.get("kind", ""))
		var installed_sites: Array = entry.get("installed_sites", [])
		var installed := not sel_id.is_empty() and sel_id in installed_sites
		var max_red := float(entry.get("max_reduction", 0.0))
		var cost := float(entry.get("fixed_cost", 0.0))
		var opex := float(entry.get("opex_at_full", 0.0))
		var best := str(entry.get("best_for", ""))

		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 8)
		_mitigation_list.add_child(row)

		var name_lbl := Label.new()
		name_lbl.text = ("🔛 " if installed else "⚪ ") + tr(str(entry.get("label", kind)))
		name_lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		name_lbl.add_theme_color_override("font_color", Color(COLOR_TEXT))
		row.add_child(name_lbl)

		var info := Label.new()
		info.text = "%s · −%.0f%% død." % [_format_currency(cost) + " NOK", max_red * 100.0]
		info.add_theme_font_size_override("font_size", 11)
		info.add_theme_color_override("font_color", Color(COLOR_MUTED))
		row.add_child(info)

		var btn := Button.new()
		btn.text = tr("Fjern") if installed else tr("Installer")
		btn.disabled = sel_id.is_empty()
		btn.tooltip_text = tr("Fjerner inntil %.0f%% av dødeligheten ved oppblomstring.\nInstallasjon: %s NOK. Drift ved full alvor: %s NOK.\nBest mot: %s.") % [
			max_red * 100.0, _format_currency(cost), _format_currency(opex), tr(best)]
		btn.pressed.connect(_on_install_mitigation.bind(kind))
		row.add_child(btn)

func _on_install_mitigation(kind: String) -> void:
	var sim := get_node_or_null("/root/SimBridge")
	if sim == null or not sim.has_method("install_bloom_mitigation"):
		return
	if bloom_site_option.selected < 0 or bloom_site_option.selected >= _bloom_site_ids.size():
		return
	sim.install_bloom_mitigation(_bloom_site_ids[bloom_site_option.selected], kind)
	_refresh_blooms()
	_refresh_bloom_mitigations()

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

	_refresh_blooms()
	_refresh_bloom_mitigations()
	_refresh_gill_health()


## Blooms section (Phase 4.3): site picker + countermeasure toggles + live list.
var _bloom_site_ids: Array[String] = []

func _refresh_blooms() -> void:
	_clear_container(bloom_alert_container)
	var sim := get_node_or_null("/root/SimBridge")
	if sim == null:
		return
	# Site picker (preserve selection).
	var prev := ""
	if bloom_site_option.selected >= 0 and bloom_site_option.selected < _bloom_site_ids.size():
		prev = _bloom_site_ids[bloom_site_option.selected]
	_bloom_site_ids.clear()
	bloom_site_option.clear()
	var pens: Array = sim.get_all_pens() if sim.has_method("get_all_pens") else []
	for p in pens:
		_bloom_site_ids.append(str(p.get("id", "")))
		bloom_site_option.add_item("%s (noter %.0f%%)" % [
			str(p.get("name", "?")), float(p.get("net_integrity", 100.0))])
	if not pens.is_empty():
		var sel := maxi(0, _bloom_site_ids.find(prev))
		bloom_site_option.select(sel)
	# Toggle states for selected site.
	var sel_id := _bloom_site_ids[bloom_site_option.selected] if not _bloom_site_ids.is_empty() else ""
	for p in pens:
		if str(p.get("id", "")) == sel_id:
			btn_bubble.text = ("🔛 " if bool(p.get("has_bubble", false)) else "⚪ ") + "🫧 Boblegardin"
			btn_diffuser.text = ("🔛 " if bool(p.get("has_diffuser", false)) else "⚪ ") + "💨 O2-diffuser"
	# Live bloom list.
	var blooms: Array = sim.get_active_blooms() if sim.has_method("get_active_blooms") else []
	if blooms.is_empty():
		var none := Label.new()
		none.text = "Ingen aktive oppblomstringer — sesongrisiko: maneter sep–jan, alger mai–jul"
		none.add_theme_color_override("font_color", Color(COLOR_MUTED))
		bloom_alert_container.add_child(none)
		return
	var icons := {"jellyfish": "🪼", "algae": "🟤", "hypoxia": "💨"}
	for b in blooms:
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 8)
		bloom_alert_container.add_child(row)
		var lbl := Label.new()
		lbl.text = "%s %s @ %s — alvor %.0f%%, %dd igjen" % [
			str(icons.get(str(b.get("kind", "")), "⚠️")),
			str(b.get("kind", "?")).capitalize(),
			str(b.get("site_id", "?")),
			float(b.get("severity", 0.0)) * 100.0,
			int(b.get("days_left", 0))]
		lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		lbl.add_theme_color_override("font_color", Color(COLOR_TEXT))
		row.add_child(lbl)


func _on_deploy_countermeasure(kind: String) -> void:
	var sim := get_node_or_null("/root/SimBridge")
	if sim == null or not sim.has_method("deploy_countermeasure"):
		return
	if bloom_site_option.selected < 0 or bloom_site_option.selected >= _bloom_site_ids.size():
		return
	sim.deploy_countermeasure(_bloom_site_ids[bloom_site_option.selected], kind)


## Vaccination (Phase 5.1): October window only; button shows window state.
func _on_vaccinate_pressed() -> void:
	var sim := get_node_or_null("/root/SimBridge")
	if sim == null or not sim.has_method("vaccinate_site"):
		return
	if bloom_site_option.selected < 0 or bloom_site_option.selected >= _bloom_site_ids.size():
		return
	sim.vaccinate_site(_bloom_site_ids[bloom_site_option.selected])

func _display_weather_alerts():
	_clear_container(weather_alert_container)

	if active_weather_alerts.is_empty():
		var label = Label.new()
		label.text = tr("No active weather alerts")
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
		label.text = tr("No active disease outbreaks")
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
		site_label.text = tr("Location: %s") % ("All sites" if site_id == "all_sites" else site_id)
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
		apply_button.text = tr("Apply")
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
		# Clear disease alerts for this site
		active_disease_alerts = active_disease_alerts.filter(func(alert): return alert.get("site_id", "") != site_id)
		_display_disease_alerts()

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
	style.bg_color = Color(0.18, 0.20, 0.25, 0.7)

	var border_color = _get_severity_color(severity)
	style.border_color = border_color
	style.set_border_width_all(2)
	style.corner_radius_top_left = 4
	style.corner_radius_top_right = 4
	style.corner_radius_bottom_left = 4
	style.corner_radius_bottom_right = 4

	return style

func _clear_container(container: VBoxContainer):
	if container == null:
		return
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
