## CarbonLedgerPanel v1.0 — Nordic Minimalist (scene-based).
## Carbon ledger dashboard: Scope 1/2/3, credits, intensity, carbon-neutral status.
## Layout lives in scenes/ui/CarbonLedgerPanel.tscn; this script is wiring only.

extends PanelContainer
class_name CarbonLedgerPanel

# ── Scene refs ────────────────────────────────────────────────────────────────
@onready var header_label: Label = %CarbonTitle
@onready var status_label: Label = %CarbonStatus
@onready var intensity_label: Label = %CarbonIntensity
@onready var scope1_label: Label = %Scope1Value
@onready var scope2_label: Label = %Scope2Value
@onready var scope3_label: Label = %Scope3Value
@onready var credits_label: Label = %CreditsValue
@onready var net_label: Label = %NetValue
@onready var sites_container: VBoxContainer = %SitesList
@onready var offshore_container: VBoxContainer = %OffshoreList
@onready var ras_container: VBoxContainer = %RasList
@onready var processing_container: VBoxContainer = %ProcessingInfo
@onready var refresh_btn: Button = %BtnRefresh

# ── Autoloads ─────────────────────────────────────────────────────────────────
var _theme: Node = null
var _sim: Node = null

func _ready():
	_theme = get_node_or_null("/root/LakskongenTheme")
	_sim = get_node_or_null("/root/SimBridge")
	if _sim != null:
		if _sim.has_signal("state_updated"):
			_sim.state_updated.connect(_on_state_updated)
	else:
		push_warning("CarbonLedgerPanel: SimBridge not found — showing fallback data")

	_apply_localization()
	_apply_theme_fonts()
	refresh_btn.pressed.connect(_on_refresh_pressed)

	refresh()

	var audio := get_node_or_null("/root/AudioController")
	if audio and audio.has_method("attach_ui_sounds"):
		audio.attach_ui_sounds(self)

func _apply_localization() -> void:
	header_label.text = tr("UI_CARBON_LEDGER")
	status_label.text = ""
	intensity_label.text = tr("UI_CARBON_INTENSITY") + ": --"

func _apply_theme_fonts() -> void:
	if not _theme:
		return
	if _theme.font_heading_bold:
		header_label.add_theme_font_override("font", _theme.font_heading_bold)
	if _theme.font_heading_semibold:
		status_label.add_theme_font_override("font", _theme.font_heading_semibold)

func refresh():
	if _sim == null or not _sim.has_method("get_carbon_ledger"):
		_show_unavailable()
		return

	var ledger: Dictionary = _sim.get_carbon_ledger()
	if ledger.is_empty():
		_show_unavailable()
		return

	_update_summary(ledger)
	_update_sites(ledger)
	_update_offshore(ledger)
	_update_ras(ledger)
	_update_processing(ledger)

	# Carbon intensity
	var intensity: float = _sim.get_carbon_intensity() if _sim.has_method("get_carbon_intensity") else 0.0
	intensity_label.text = "%s: %.2f" % [tr("UI_CARBON_INTENSITY"), intensity]  # the label carries the unit

	# Carbon neutral status
	var neutral: bool = _sim.is_carbon_neutral_year() if _sim.has_method("is_carbon_neutral_year") else false
	if neutral:
		status_label.text = tr("UI_CARBON_NEUTRAL")
		status_label.add_theme_color_override("font_color", Color(0.3, 0.8, 0.3))
	else:
		status_label.text = "Ikke kulstoffneutral ennå"
		status_label.add_theme_color_override("font_color", Color(1, 0.6, 0.2))

func _show_unavailable():
	sites_container.add_child(_row_label(tr("UI_CARBON_LEDGER") + ": Data ikke tilgjengelig"))
	status_label.text = ""

func _update_summary(ledger: Dictionary):
	# Captioned and coloured: the bare values took the default light label
	# colour on the light cards and could not be told apart.
	for pair in [[scope1_label, "Scope 1", "scope1_kg"], [scope2_label, "Scope 2", "scope2_kg"],
			[scope3_label, "Scope 3", "scope3_kg"]]:
		(pair[0] as Label).text = "%s: %.1f t" % [pair[1], float(ledger.get(pair[2], 0.0)) / 1000.0]
		(pair[0] as Label).add_theme_color_override("font_color", _text_color())
	credits_label.text = "Kreditter: %.1f t" % (float(ledger.get("total_credits_kg", 0.0)) / 1000.0)
	
	var net := float(ledger.get("net_emissions_kg", 0.0)) / 1000.0
	net_label.text = "Netto: %.1f t" % net
	if net <= 0:
		net_label.add_theme_color_override("font_color", Color(0.3, 0.8, 0.3))
	else:
		net_label.add_theme_color_override("font_color", Color(1, 0.3, 0.3))

func _update_sites(ledger: Dictionary):
	if sites_container == null:
		return
	for child in sites_container.get_children():
		child.queue_free()

	var sites: Array = ledger.get("sites", [])
	if sites.is_empty():
		UIPolish.show_empty_state(sites_container, "Ingen kystlokaliteter", 12, "", _theme)
		return

	for site in sites:
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 8)
		sites_container.add_child(row)

		var net := float(site.get("net_kg", 0.0)) / 1000.0
		row.add_child(_row_label("%s: %.1f t netto" % [str(site.get("id", "?")), net]))

		var scope_lbl := Label.new()
		var s1 := float(site.get("scope1_kg", 0.0)) / 1000.0
		var s2 := float(site.get("scope2_kg", 0.0)) / 1000.0
		var s3 := float(site.get("scope3_kg", 0.0)) / 1000.0
		scope_lbl.text = "S1: %.1f S2: %.1f S3: %.1f" % [s1, s2, s3]
		scope_lbl.add_theme_font_size_override("font_size", 11)
		scope_lbl.add_theme_color_override("font_color", _theme.TEXT_SECONDARY if _theme else Color(0.6, 0.6, 0.6))
		row.add_child(scope_lbl)

	# UIPolish: vertical keyboard navigation for sites list
	UIPolish.link_vertical_focus(sites_container)

func _update_offshore(ledger: Dictionary):
	if offshore_container == null:
		return
	for child in offshore_container.get_children():
		child.queue_free()

	var farms: Array = ledger.get("offshore", [])
	if farms.is_empty():
		return

	for farm in farms:
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 8)
		offshore_container.add_child(row)

		var net := float(farm.get("net_kg", 0.0)) / 1000.0
		row.add_child(_row_label("🌊 %s: %.1f t netto" % [str(farm.get("name", "?")), net]))

	# UIPolish: vertical keyboard navigation for offshore list
	UIPolish.link_vertical_focus(offshore_container)

func _update_ras(ledger: Dictionary):
	if ras_container == null:
		return
	for child in ras_container.get_children():
		child.queue_free()

	var ras_list: Array = ledger.get("ras", [])
	if ras_list.is_empty():
		return

	for ras in ras_list:
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 8)
		ras_container.add_child(row)

		var net := float(ras.get("net_kg", 0.0)) / 1000.0
		row.add_child(_row_label("🏭 %s: %.1f t netto" % [str(ras.get("name", "?")), net]))

	# UIPolish: vertical keyboard navigation for RAS list
	UIPolish.link_vertical_focus(ras_container)

func _update_processing(ledger: Dictionary):
	if processing_container == null:
		return
	for child in processing_container.get_children():
		child.queue_free()

	var proc: Dictionary = ledger.get("processing", {})
	if proc.is_empty():
		return

	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 8)
	processing_container.add_child(row)

	var net := float(proc.get("net_kg", 0.0)) / 1000.0
	row.add_child(_row_label("🏭 Foredlingsanlegg (%s): %.1f t netto" % [str(proc.get("tier", "?")), net]))

	# UIPolish: vertical keyboard navigation for processing container
	UIPolish.link_vertical_focus(processing_container)

## Row and value text colour. Labels default to the light theme text colour,
## which vanished on this panel's light cards.
func _text_color() -> Color:
	return _theme.TEXT_PRIMARY if _theme else Color("#2E3440")

func _row_label(text: String) -> Label:
	var lbl := Label.new()
	lbl.text = text
	lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	lbl.add_theme_font_size_override("font_size", 13)
	lbl.add_theme_color_override("font_color", _text_color())
	return lbl

func _on_refresh_pressed():
	refresh()

func _on_state_updated():
	refresh()

func _input(event: InputEvent) -> void:
	# Only while shown: as a cached, hidden tab this would swallow every ESC.
	if is_visible_in_tree() and event.is_action_pressed("ui_cancel"):
		get_viewport().set_input_as_handled()
		hide()