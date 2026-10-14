extends Control

## Norwegian coastline region selector for new sea site placement.
## 13 production zones (PO 1-13) from south to north covering the entire
## Norwegian coast with licence, temperature, disease risk, and logistics data.

signal close_requested
signal region_selected(region_id: String)

# Data-overlay legend embedded into the detail panel (see SiteOverlayLegend.gd).
const SiteOverlayLegend = preload("res://scripts/ui/SiteOverlayLegend.gd")
const NorwayCoastline = preload("res://scripts/ui/NorwayCoastline.gd")

# ── Colors ────────────────────────────────────────────────────────────────────
const BG_DEEP        := Color("#F4F1EA")
const BG_PANEL       := Color("#FFFFFF")
const BG_DETAIL      := Color("#EDE9E0")
const BG_MAP         := Color("#F4F1EA")
const BORDER_COLOR   := Color("#D6CFBF")
const TEXT_TITLE     := Color("#1C1E20")
const TEXT_BODY      := Color("#5A6168")
const TEXT_MUTED     := Color("#5A6168")
const TEXT_ACCENT    := Color("#1F5E73")
const LAND_COLOR     := Color("#D6CFBF")
const LAND_HOVER     := Color("#C8D3E5")
const LAND_SELECTED  := Color("#B0BED4")
const SEA_COLOR      := Color("#1F5E73")
const RISK_LOW       := Color("#2E6B45")
const RISK_MEDIUM    := Color("#8A5A00")
const RISK_HIGH      := Color("#B3172F")
const FJORD_COLOR    := Color("#1F5E73")

# ── Region data ───────────────────────────────────────────────────────────────
# Texts (description, disease details) live in translations.csv as
# REGION_<ID>_DESC / REGION_<ID>_DISEASE. Operators are the game's parody
# rivals (roadmap D17: no real company names).
# Positions are normalised [0..1] in a vertical layout (0 = top = north, 1 = south).
# x is the approximate coastline position left/right.
const REGIONS := [
	{
		"id": "po01",
		"name": "Svenskegrensen til Jæren",
		"norwegian_name": "PO 1: Svenskegrensen til Jæren",
		"production_zone": "PO01",
		"pos": Vector2(0.22, 0.96),
		"lonlat": Vector2(8.00, 58.15),
		"available_licenses": 28,
		"temp_min": 7.5,
		"temp_max": 13.5,
		"disease_risk": "Low",
		"distance_to_processing": "180 km (Kristiansand)",
		"processing_hub": "Kristiansand, Stavanger",
		"annual_harvest_kt": 15,
		"key_operators": ["Herøy Seafood AS", "Mowier ASA"],
		"icon": "🌊",
		"color": Color("#1F5E73"),
	},
	{
		"id": "po02",
		"name": "Ryfylke",
		"norwegian_name": "PO 2: Ryfylke",
		"production_zone": "PO02",
		"pos": Vector2(0.24, 0.88),
		"lonlat": Vector2(5.70, 59.00),
		"available_licenses": 32,
		"temp_min": 7.0,
		"temp_max": 13.0,
		"disease_risk": "Medium",
		"distance_to_processing": "60 km (Stavanger)",
		"processing_hub": "Stavanger, Hjelmeland",
		"annual_harvest_kt": 50,
		"key_operators": ["Greed Gregarious ASA", "Mowier ASA"],
		"icon": "⚓",
		"color": Color("#1F5E73"),
	},
	{
		"id": "po03",
		"name": "Karmøy til Sotra",
		"norwegian_name": "PO 3: Karmøy til Sotra",
		"production_zone": "PO03",
		"pos": Vector2(0.26, 0.81),
		"lonlat": Vector2(5.27, 59.41),
		"available_licenses": 24,
		"temp_min": 7.0,
		"temp_max": 12.5,
		"disease_risk": "Medium-High",
		"distance_to_processing": "80 km (Bergen/Bømlo)",
		"processing_hub": "Bømlo, Bergen",
		"annual_harvest_kt": 80,
		"key_operators": ["Herøy Seafood AS", "Mowier ASA"],
		"icon": "🐟",
		"color": Color("#1F5E73"),
	},
	{
		"id": "po04",
		"name": "Nordhordland til Stadt",
		"norwegian_name": "PO 4: Nordhordland til Stadt",
		"production_zone": "PO04",
		"pos": Vector2(0.28, 0.73),
		"lonlat": Vector2(5.32, 60.39),
		"available_licenses": 18,
		"temp_min": 6.5,
		"temp_max": 12.0,
		"disease_risk": "High",
		"distance_to_processing": "120 km (Florø)",
		"processing_hub": "Florø, Måløy",
		"annual_harvest_kt": 110,
		"key_operators": ["Mowier ASA", "SalMax Group ASA"],
		"icon": "🏭",
		"color": Color("#7B9BC0"),
	},
	{
		"id": "po05",
		"name": "Stadt til Hustadvika",
		"norwegian_name": "PO 5: Stadt til Hustadvika",
		"production_zone": "PO05",
		"pos": Vector2(0.31, 0.65),
		"lonlat": Vector2(6.15, 62.47),
		"available_licenses": 22,
		"temp_min": 6.5,
		"temp_max": 11.5,
		"disease_risk": "Medium-High",
		"distance_to_processing": "90 km (Ålesund)",
		"processing_hub": "Ålesund, Molde",
		"annual_harvest_kt": 95,
		"key_operators": ["SalMax Group ASA", "Mowier ASA"],
		"icon": "🚢",
		"color": Color("#7B9BC0"),
	},
	{
		"id": "po06",
		"name": "Nordmøre og Sør-Trøndelag",
		"norwegian_name": "PO 6: Nordmøre og Sør-Trøndelag",
		"production_zone": "PO06",
		"pos": Vector2(0.34, 0.58),
		"lonlat": Vector2(7.73, 63.11),
		"available_licenses": 20,
		"temp_min": 6.0,
		"temp_max": 11.0,
		"disease_risk": "Low",
		"distance_to_processing": "150 km (Hitra/Frøya)",
		"processing_hub": "Kristiansund, Hitra",
		"annual_harvest_kt": 120,
		"key_operators": ["SalMax Group ASA", "Herøy Seafood AS"],
		"icon": "🐟",
		"color": Color("#7B9BC0"),
	},
	{
		"id": "po07",
		"name": "Nord-Trøndelag med Bindal",
		"norwegian_name": "PO 7: Nord-Trøndelag med Bindal",
		"production_zone": "PO07",
		"pos": Vector2(0.38, 0.50),
		"lonlat": Vector2(11.24, 64.86),
		"available_licenses": 16,
		"temp_min": 5.5,
		"temp_max": 10.5,
		"disease_risk": "Low",
		"distance_to_processing": "120 km (Rørvik)",
		"processing_hub": "Rørvik, Nærøysund",
		"annual_harvest_kt": 100,
		"key_operators": ["SalMax Group ASA", "Mowier ASA"],
		"icon": "⚓",
		"color": Color("#7B9BC0"),
	},
	{
		"id": "po08",
		"name": "Helgeland til Bodø",
		"norwegian_name": "PO 8: Helgeland til Bodø",
		"production_zone": "PO08",
		"pos": Vector2(0.42, 0.42),
		"lonlat": Vector2(14.41, 67.28),
		"available_licenses": 19,
		"temp_min": 5.0,
		"temp_max": 10.0,
		"disease_risk": "Medium",
		"distance_to_processing": "180 km (Bodø/Sandnessjøen)",
		"processing_hub": "Bodø, Sandnessjøen",
		"annual_harvest_kt": 130,
		"key_operators": ["NordLaks Nord AS", "Offshore Optimists AS"],
		"icon": "⛰️",
		"color": Color("#1F5E73"),
	},
	{
		"id": "po09",
		"name": "Vestfjorden og Vesterålen",
		"norwegian_name": "PO 9: Vestfjorden og Vesterålen",
		"production_zone": "PO09",
		"pos": Vector2(0.46, 0.35),
		"lonlat": Vector2(14.57, 68.23),
		"available_licenses": 21,
		"temp_min": 4.5,
		"temp_max": 9.5,
		"disease_risk": "Low",
		"distance_to_processing": "160 km (Sortland/Bodø)",
		"processing_hub": "Sortland, Svolvær",
		"annual_harvest_kt": 110,
		"key_operators": ["NordLaks Nord AS", "Offshore Optimists AS"],
		"icon": "❄️",
		"color": Color("#1F5E73"),
	},
	{
		"id": "po10",
		"name": "Andøya til Senja",
		"norwegian_name": "PO 10: Andøya til Senja",
		"production_zone": "PO10",
		"pos": Vector2(0.50, 0.27),
		"lonlat": Vector2(16.12, 69.31),
		"available_licenses": 14,
		"temp_min": 4.0,
		"temp_max": 9.0,
		"disease_risk": "Low",
		"distance_to_processing": "140 km (Finnsnes)",
		"processing_hub": "Finnsnes, Senja",
		"annual_harvest_kt": 90,
		"key_operators": ["SalMax Group ASA", "Herøy Seafood AS"],
		"icon": "🌊",
		"color": Color("#8CC7DA"),
	},
	{
		"id": "po11",
		"name": "Kvaløya til Loppa",
		"norwegian_name": "PO 11: Kvaløya til Loppa",
		"production_zone": "PO11",
		"pos": Vector2(0.54, 0.19),
		"lonlat": Vector2(18.96, 69.65),
		"available_licenses": 12,
		"temp_min": 3.5,
		"temp_max": 8.5,
		"disease_risk": "Low",
		"distance_to_processing": "100 km (Tromsø)",
		"processing_hub": "Tromsø, Skjervøy",
		"annual_harvest_kt": 75,
		"key_operators": ["Herøy Seafood AS", "Greed Gregarious ASA"],
		"icon": "🚢",
		"color": Color("#8CC7DA"),
	},
	{
		"id": "po12",
		"name": "Vest-Finnmark",
		"norwegian_name": "PO 12: Vest-Finnmark",
		"production_zone": "PO12",
		"pos": Vector2(0.58, 0.12),
		"lonlat": Vector2(23.68, 70.66),
		"available_licenses": 10,
		"temp_min": 3.0,
		"temp_max": 8.0,
		"disease_risk": "Low",
		"distance_to_processing": "150 km (Alta)",
		"processing_hub": "Alta, Hammerfest",
		"annual_harvest_kt": 60,
		"key_operators": ["Greed Gregarious ASA", "SalMax Group ASA"],
		"icon": "❄️",
		"color": Color("#8CC7DA"),
	},
	{
		"id": "po13",
		"name": "Øst-Finnmark",
		"norwegian_name": "PO 13: Øst-Finnmark",
		"production_zone": "PO13",
		"pos": Vector2(0.64, 0.04),
		"lonlat": Vector2(31.11, 70.37),
		"available_licenses": 8,
		"temp_min": 2.5,
		"temp_max": 7.5,
		"disease_risk": "Low",
		"distance_to_processing": "250 km (Alta / Kirkenes)",
		"processing_hub": "Kirkenes, Vadsø",
		"annual_harvest_kt": 40,
		"key_operators": ["SalMax Group ASA", "Greed Gregarious ASA"],
		"icon": "🧊",
		"color": Color("#8CC7DA"),
	}
]

# ── State ─────────────────────────────────────────────────────────────────────
var _selected_region_id: String = ""
var _hovered_region_id: String = ""
var _region_nodes: Dictionary = {}  # id -> Control (map dot)
var _overlay = null  # SiteOverlayLegend instance (additive; optional)

# ── Node refs (declared in scenes/ui/MapPanel.tscn) ───────────────────────────
@onready var _map_container: Control = %MapContainer
@onready var _map_icon: Label = %MapIcon
@onready var _title_label: Label = %TitleLabel
@onready var _subtitle_label: Label = %SubtitleLabel
@onready var _close_btn: Button = %CloseBtn
@onready var _map_title: Label = %MapTitle
@onready var _compass_label: Label = %CompassLabel
@onready var _detail_icon: Label = %DetailIcon
@onready var _detail_name: Label = %DetailName
@onready var _detail_zone: Label = %DetailZone
@onready var _detail_desc: Label = %DetailDesc
@onready var _detail_licenses: Label = %DetailLicenses
@onready var _detail_temp: Label = %DetailTemp
@onready var _detail_risk_label: Label = %DetailRiskLabel
@onready var _detail_risk_indicator: ColorRect = %DetailRiskIndicator
@onready var _detail_distance: Label = %DetailDistance
@onready var _detail_processing: Label = %DetailProcessing
@onready var _detail_harvest: Label = %DetailHarvest
@onready var _detail_operators: Label = %DetailOperators
@onready var _disease_details: Label = %DiseaseDetails
@onready var _temp_bar_slot: Control = %TempBarSlot
@onready var _confirm_btn: Button = %ConfirmBtn
var _temp_bar: Control
## The chart under the markers; its city names keep clear of the markers.
var _coastline: _CoastlineDrawer = null

# ─────────────────────────────────────────────────────────────────────────────
func _ready() -> void:
	_apply_localization()
	_close_btn.pressed.connect(_on_close_pressed)
	_confirm_btn.pressed.connect(_on_confirm_pressed)
	# Decorative coastline backdrop (custom drawing, added behind labels).
	_coastline = _CoastlineDrawer.new()
	_coastline.set_anchors_preset(Control.PRESET_FULL_RECT)
	_map_container.add_child(_coastline)
	_map_container.move_child(_coastline, 0)
	# Temperature bar visualisation (custom drawing, added to its slot).
	_temp_bar = _TempBar.new()
	_temp_bar.custom_minimum_size = Vector2(0, 28)
	_temp_bar.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_temp_bar_slot.add_child(_temp_bar)
	# Live data-overlay legend (lice/oxygen/biomass/gill/containment).
	_add_overlay_legend()
	# Region markers are built deferred so the map container has layout size.
	call_deferred("_build_region_markers")
	hide()

	var audio := get_node_or_null("/root/AudioController")
	if audio and audio.has_method("attach_ui_sounds"):
		audio.attach_ui_sounds(self)

## Embed the SiteOverlayLegend at the top of the detail column and wire it so
## region markers re-tint whenever the overlay metric or live site data changes.
func _add_overlay_legend() -> void:
	_overlay = SiteOverlayLegend.new()
	_overlay.name = "SiteOverlayLegend"
	var host := find_child("DetailInner", true, false)
	if host == null:
		_overlay = null
		return
	host.add_child(_overlay)
	host.move_child(_overlay, 0)
	if _overlay.has_signal("sites_updated"):
		_overlay.sites_updated.connect(_apply_overlay_to_markers)
	if _overlay.has_signal("metric_changed"):
		_overlay.metric_changed.connect(_on_overlay_metric_changed)

func _on_overlay_metric_changed(_metric_id: String) -> void:
	_apply_overlay_to_markers()

## Tint each region marker by the average overlay intensity of the sea sites
## sharing its production zone. Regions with no live sites keep their static
## disease-risk dot (overlay_active = false), so existing behavior is preserved.
func _apply_overlay_to_markers() -> void:
	if _overlay == null or not is_instance_valid(_overlay):
		return
	for rid in _region_nodes:
		var m = _region_nodes[rid]
		if m == null or not is_instance_valid(m):
			continue
		var region := _get_region(rid)
		var zone := str(region.get("production_zone", ""))
		var t := float(_overlay.get_zone_intensity(zone))
		if t < 0.0:
			m.overlay_active = false
		else:
			m.overlay_active = true
			m.overlay_color = _overlay.intensity_to_color(t)
		m.queue_redraw()

func _apply_localization() -> void:
	_map_icon.text = "🗺️"
	_title_label.text = tr("UI_MAP_TITLE")
	_subtitle_label.text = tr("UI_MAP_SUBTITLE")
	_map_title.text = tr("UI_MAP_NORWAY")
	_compass_label.text = tr("UI_MAP_COMPASS_N")
	for pair in [["KeyLicences", "UI_MAP_KEY_LICENCES"], ["KeyDistance", "UI_MAP_KEY_DISTANCE"],
			["KeyHub", "UI_MAP_KEY_HUB"], ["KeyHarvest", "UI_MAP_KEY_HARVEST"],
			["KeyOperators", "UI_MAP_KEY_OPERATORS"], ["TempHeader", "UI_MAP_KEY_TEMPERATURE"],
			["OptimalLabel", "UI_MAP_TEMP_OPTIMAL"], ["RiskHeader", "UI_MAP_KEY_DISEASE"]]:
		var lbl := find_child(pair[0], true, false) as Label
		if lbl:
			lbl.text = tr(pair[1])
	# Picking a region changes nothing yet (expansion arrives with roadmap
	# U2-01): no "select region" verb until it does something.
	_confirm_btn.visible = false

func _input(event: InputEvent) -> void:
	"""Handle keyboard navigation"""
	if not visible:
		return
	
	if event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()

func show_panel() -> void:
	show()
	modulate.a = 0.0
	var tw := create_tween()
	tw.tween_property(self, "modulate:a", 1.0, 0.35)
	_clear_selection()

func hide_panel() -> void:
	var tw := create_tween()
	tw.tween_property(self, "modulate:a", 0.0, 0.25)
	tw.tween_callback(hide)

# ─────────────────────────────────────────────────────────────────────────────
# (Shell layout now lives in scenes/ui/MapPanel.tscn — see _apply_localization.)

# ─────────────────────────────────────────────────────────────────────────────
func _build_region_markers() -> void:
	if not is_instance_valid(_map_container):
		return
	var map_size := _map_container.size
	if map_size == Vector2.ZERO:
		map_size = Vector2(420, 600)

	for region in REGIONS:
		var marker := _RegionMarker.new()
		marker.region_data = region
		# Real coastal anchor: project lon/lat into map space so the marker
		# dot sits on the actual coastline (legacy "pos" kept as fallback).
		var anchor := Vector2(region["pos"].x, region["pos"].y)
		if region.has("lonlat"):
			var ll: Vector2 = region["lonlat"]
			anchor = NorwayCoastline.project(ll.x, ll.y)
		var px: float = anchor.x * map_size.x
		var py: float = anchor.y * (map_size.y - 80) + 30
		# Dot lives at marker-local (9, 22): land the dot on the anchor so
		# the label extends east over land.
		marker.position = Vector2(px - 9, py - 22)
		marker.custom_minimum_size = Vector2(120, 44)
		marker.mouse_entered.connect(_on_region_hover.bind(region["id"]))
		marker.mouse_exited.connect(_on_region_unhover.bind(region["id"]))
		marker.gui_input.connect(_on_region_input.bind(region["id"]))
		_map_container.add_child(marker)
		_region_nodes[region["id"]] = marker
	# Apply the current overlay (if any) once markers exist.
	_apply_overlay_to_markers()
	# U1-05: city names give way to the region markers (Bodø sat on top of
	# "Helgeland til Bodø", Trondheim on "Nordmøre og Sør-Trøndelag").
	if _coastline != null:
		_coastline.avoid = region_label_rects()
		_coastline.queue_redraw()


## Where each region marker draws its dot and its name (map-container space).
func region_label_rects() -> Array[Rect2]:
	var out: Array[Rect2] = []
	for id in _region_nodes:
		var marker: Control = _region_nodes[id]
		if not is_instance_valid(marker):
			continue
		out.append(Rect2(marker.position + Vector2(0.0, 11.0), Vector2(22.0, 22.0)))
		var lbl := marker.get_node_or_null("NameLabel") as Label
		if lbl != null:
			var text_size := lbl.get_minimum_size()
			out.append(Rect2(marker.position + lbl.position + Vector2(0.0, (lbl.size.y - text_size.y) * 0.5), text_size))
	return out


## The city labels the chart placed (map-container space; tests read them).
func city_label_rects() -> Array[Rect2]:
	var out: Array[Rect2] = []
	if _coastline != null:
		out.assign(_coastline.placed)
	return out

# ── Detail panel ──────────────────────────────────────────────────────────────
# (Detail panel layout now lives in scenes/ui/MapPanel.tscn.)

# ─────────────────────────────────────────────────────────────────────────────
func _show_region(region_id: String) -> void:
	var r := _get_region(region_id)
	if r.is_empty():
		return

	var key := str(r["id"]).to_upper()
	_detail_name.text = r["name"]
	_detail_zone.text = tr("UI_MAP_ZONE") % [int(str(r["production_zone"]).trim_prefix("PO")), r["name"]]
	_detail_icon.text = r.get("icon", "📍")
	_detail_desc.text = tr("REGION_%s_DESC" % key)
	_detail_licenses.text = tr("UI_MAP_LICENCES") % int(r["available_licenses"])
	_detail_temp.text = "%s – %s °C" % [SimFormatters.num(float(r["temp_min"]), 1), SimFormatters.num(float(r["temp_max"]), 1)]
	_detail_distance.text = r["distance_to_processing"]
	_detail_processing.text = r["processing_hub"]
	_detail_harvest.text = tr("UI_MAP_HARVEST") % SimFormatters.integer(int(r["annual_harvest_kt"]) * 1000)
	_detail_operators.text = "\n".join(PackedStringArray(r["key_operators"]))

	var risk: String = r["disease_risk"]
	_detail_risk_label.text = SimLabels.risk(risk)
	var risk_color := _risk_color(risk)
	LakskongenTheme.set_text_color(_detail_risk_label, risk_color)
	_detail_risk_indicator.color = risk_color

	# Update disease details label
	_disease_details.text = tr("REGION_%s_DISEASE" % key)

	# Temperature bar
	if _temp_bar is _TempBar:
		_temp_bar.set_range(r["temp_min"], r["temp_max"])

	_confirm_btn.disabled = false

func _clear_selection() -> void:
	_selected_region_id = ""
	_detail_name.text = tr("UI_MAP_SELECT_REGION")
	_detail_zone.text = tr("UI_MAP_CLICK_PROMPT")
	_detail_icon.text = "🗺️"
	_detail_desc.text = tr("UI_MAP_REGION_DESC")
	for lbl in [_detail_licenses, _detail_temp, _detail_distance, _detail_processing, _detail_harvest,
			_detail_operators, _detail_risk_label]:
		lbl.text = "—"
	LakskongenTheme.set_text_color(_detail_risk_label, TEXT_BODY)
	_detail_risk_indicator.color = TEXT_MUTED
	_confirm_btn.disabled = true
	_disease_details.text = ""
	_refresh_markers()

# ── Marker state refresh ───────────────────────────────────────────────────────
func _refresh_markers() -> void:
	for rid in _region_nodes:
		var m: _RegionMarker = _region_nodes[rid]
		if not is_instance_valid(m):
			continue
		m.state = (
			_RegionMarker.STATE_SELECTED if rid == _selected_region_id
			else _RegionMarker.STATE_HOVERED if rid == _hovered_region_id
			else _RegionMarker.STATE_NORMAL
		)
		m.queue_redraw()

# ── Signal handlers ────────────────────────────────────────────────────────────
func _on_region_hover(region_id: String) -> void:
	_hovered_region_id = region_id
	_refresh_markers()

func _on_region_unhover(_region_id: String) -> void:
	_hovered_region_id = ""
	_refresh_markers()

func _on_region_input(event: InputEvent, region_id: String) -> void:
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		_selected_region_id = region_id
		_show_region(region_id)
		_refresh_markers()

func _on_confirm_pressed() -> void:
	if _selected_region_id.is_empty():
		return
	emit_signal("region_selected", _selected_region_id)
	hide_panel()
	emit_signal("close_requested")

func _on_close_pressed() -> void:
	hide_panel()
	emit_signal("close_requested")

func _unhandled_key_input(event: InputEvent) -> void:
	if not visible:
		return
	if event is InputEventKey and event.pressed and event.keycode == KEY_ESCAPE:
		_on_close_pressed()
		get_viewport().set_input_as_handled()

# ── Helpers ────────────────────────────────────────────────────────────────────
func _get_region(region_id: String) -> Dictionary:
	for r in REGIONS:
		if r["id"] == region_id:
			return r
	return {}

func _risk_color(risk: String) -> Color:
	if risk.begins_with("Low"):
		return RISK_LOW
	elif risk.begins_with("High") or risk.ends_with("High"):
		return RISK_HIGH
	else:
		return RISK_MEDIUM

# (Widget factories removed — static styling lives in scenes/ui/MapPanel.tscn.)

# ── Inner classes ─────────────────────────────────────────────────────────────

## Real Norwegian coastline (Natural Earth 50m) rendered as a light
## nautical chart: sea gradient, landmass fill, coastline glow + stroke,
## graticule with labels, and major coastal cities.
class _CoastlineDrawer extends Control:
	const SEA_TOP := Color("#DCE9F2")
	const SEA_BOTTOM := Color("#C3D8E8")
	const LAND_FILL := Color("#E9EDE0")
	const COAST_GLOW := Color(0.04, 0.30, 0.41, 0.22)
	const COAST_LINE := Color("#1F5E73")
	const GRID_LINE := Color(1, 1, 1, 0.55)
	const GRID_TEXT := Color("#5A6168")

	var _rings: Array = []
	var _font: Font
	## Rects the city names must keep clear of (the region markers).
	var avoid: Array[Rect2] = []
	## The city name rects drawn last time.
	var placed: Array[Rect2] = []
	const CITY_FONT := 13
	const LABEL_GAP := 8.0

	func _ready() -> void:
		mouse_filter = Control.MOUSE_FILTER_IGNORE
		_rings = NorwayCoastline.rings()
		_font = get_theme_default_font()
		resized.connect(queue_redraw)
		queue_redraw()

	func _draw() -> void:
		var w := size.x
		var h := size.y
		if w <= 1.0 or h <= 1.0:
			return
		placed.clear()
		_draw_sea(w, h)
		_draw_graticule(w, h)
		_draw_land(w, h)
		_draw_cities(w, h)

	func _draw_sea(w: float, h: float) -> void:
		var bands := 28
		for i in range(bands):
			var t0 := float(i) / bands
			var col := SEA_TOP.lerp(SEA_BOTTOM, t0)
			draw_rect(Rect2(0, t0 * h, w, h / bands + 1.0), col)

	func _draw_graticule(w: float, h: float) -> void:
		# Latitude lines every 2 degrees with labels.
		var lat := 58.0
		while lat <= 72.0:
			var y := NorwayCoastline.project(10.0, lat).y * h
			if y >= 0.0 and y <= h:
				draw_line(Vector2(0, y), Vector2(w, y), GRID_LINE, 1.0)
				if _font:
					_grid_label("%d°N" % int(lat), Vector2(6, y - 4))
			lat += 2.0
		# Longitude lines every 5 degrees with bottom labels.
		var lon := 5.0
		while lon <= 30.0:
			var x := NorwayCoastline.project(lon, 65.0).x * w
			if x >= 0.0 and x <= w:
				draw_line(Vector2(x, 0), Vector2(x, h), GRID_LINE, 1.0)
				if _font:
					_grid_label("%d°E" % int(lon), Vector2(x + 4, h - 6))
			lon += 5.0

	## A graticule label at its baseline, unless a region marker is there.
	func _grid_label(text: String, baseline: Vector2) -> void:
		var sz := _font.get_string_size(text, HORIZONTAL_ALIGNMENT_LEFT, -1, CITY_FONT)
		var rect := Rect2(Vector2(baseline.x, baseline.y - sz.y + 4.0), sz)
		if _hits(rect):
			return
		placed.append(rect)
		draw_string(_font, baseline, text, HORIZONTAL_ALIGNMENT_LEFT, -1, CITY_FONT, GRID_TEXT)

	func _scaled_ring(ring: Array, w: float, h: float, close_east: bool) -> PackedVector2Array:
		var out := PackedVector2Array()
		for p in ring:
			var a: Array = p
			out.append(Vector2(float(a[0]) * w, float(a[1]) * h))
		if close_east and out.size() >= 2:
			# Close the open mainland polyline via the eastern map edge
			# (land = east). Off-canvas x clips cleanly.
			out.append(Vector2(w * 1.08, out[out.size() - 1].y))
			out.append(Vector2(w * 1.08, out[0].y))
		return out

	func _draw_land(w: float, h: float) -> void:
		NorwayCoastline.draw_landmass(self, Vector2(w, h), LAND_FILL)
		# Coastline glow + crisp edge on top of all fills.
		for ring in _rings:
			var open := NorwayCoastline.is_open_ring(ring)
			var poly := _scaled_ring(ring, w, h, false)
			if poly.size() < 2:
				continue
			if open:
				draw_polyline(poly, COAST_GLOW, 8.0)
			else:
				var closed := poly + PackedVector2Array([poly[0]])
				draw_polyline(closed, COAST_GLOW, 8.0)
		for ring in _rings:
			var poly := _scaled_ring(ring, w, h, false)
			if poly.size() < 2:
				continue
			if NorwayCoastline.is_open_ring(ring):
				draw_polyline(poly, COAST_LINE, 2.0)
			else:
				var closed := poly + PackedVector2Array([poly[0]])
				draw_polyline(closed, COAST_LINE, 2.0)

	## Cities with their names where the names fit: west over the sea first
	## (land lies east), then east, above and below. A city whose dot sits on
	## a region marker, or whose name fits nowhere clear of the markers and
	## of the names already placed, is left out: the region names it anyway.
	func _draw_cities(w: float, h: float) -> void:
		if _font == null:
			return
		var bounds := Rect2(Vector2.ZERO, Vector2(w, h))
		for c in NorwayCoastline.cities():
			var name := str(c[2])
			var pos := NorwayCoastline.project(float(c[0]), float(c[1])) * Vector2(w, h)
			var dot := Rect2(pos - Vector2(5.0, 5.0), Vector2(10.0, 10.0))
			if not bounds.encloses(dot) or _hits(dot):
				continue
			var label := _place_city_label(name, pos, bounds)
			if label.size == Vector2.ZERO:
				continue
			placed.append(label)
			draw_circle(pos, 4.5, COAST_LINE)
			draw_circle(pos, 2.5, Color.WHITE)
			# A halo in the land colour keeps the name readable over the coastline.
			var at := Vector2(label.position.x, label.end.y - 4.0)
			draw_string_outline(_font, at, name, HORIZONTAL_ALIGNMENT_LEFT, -1, CITY_FONT, 4, Color(LAND_FILL, 0.9))
			draw_string(_font, at, name, HORIZONTAL_ALIGNMENT_LEFT, -1, CITY_FONT, Color("#1C1E20"))

	func _place_city_label(name: String, pos: Vector2, bounds: Rect2) -> Rect2:
		var sz := _font.get_string_size(name, HORIZONTAL_ALIGNMENT_LEFT, -1, CITY_FONT)
		sz.y = float(CITY_FONT) + 4.0
		var candidates := [
			Vector2(pos.x - LABEL_GAP - sz.x, pos.y - sz.y * 0.5),
			Vector2(pos.x + LABEL_GAP, pos.y - sz.y * 0.5),
			Vector2(pos.x - sz.x * 0.5, pos.y - LABEL_GAP - sz.y),
			Vector2(pos.x - sz.x * 0.5, pos.y + LABEL_GAP),
		]
		for at in candidates:
			var rect := Rect2(at, sz)
			if bounds.encloses(rect) and not _hits(rect):
				return rect
		return Rect2()

	func _hits(rect: Rect2) -> bool:
		for r in avoid:
			if r.intersects(rect):
				return true
		for r in placed:
			if r.intersects(rect):
				return true
		return false


## Interactive region marker button drawn directly on the map.
class _RegionMarker extends Control:
	enum { STATE_NORMAL, STATE_HOVERED, STATE_SELECTED }

	var region_data: Dictionary = {}
	var state: int = STATE_NORMAL
	var overlay_active: bool = false
	var overlay_color: Color = Color("#5A6168")

	func _ready() -> void:
		mouse_filter = Control.MOUSE_FILTER_STOP
		var lbl := Label.new()
		lbl.name = "NameLabel"
		lbl.text = region_data.get("name", "")
		LakskongenTheme.set_size(lbl, 13)
		LakskongenTheme.set_tone(lbl, &"")
		lbl.position = Vector2(22, 0)
		lbl.size = Vector2(max(size.x - 22, 80), size.y)
		lbl.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
		# A halo in the surface colour (the theme's outline colour) keeps the
		# name readable where it crosses the coastline (U1-05).
		lbl.add_theme_constant_override("outline_size", 4)
		add_child(lbl)

	func _draw() -> void:
		if region_data.is_empty():
			return
		var w := size.x
		var h := size.y
		var risk: String = region_data.get("disease_risk", "Low")
		var region_color: Color = region_data.get("color", Color("#1F5E73"))

		# Background highlight on hover/select
		var bg_color := Color.TRANSPARENT
		match state:
			STATE_HOVERED:
				bg_color = Color(region_color, 0.25)
			STATE_SELECTED:
				bg_color = Color(region_color, 0.40)
		if bg_color.a > 0:
			draw_rect(Rect2(0, 0, w, h), bg_color, true, 0.0)

		# Dot
		var dot_col := _risk_dot_color(risk)
		if overlay_active:
			dot_col = overlay_color
		if state == STATE_SELECTED:
			dot_col = region_color.lightened(0.3)
		var dot_r := 7.0 if state == STATE_NORMAL else 9.0
		draw_circle(Vector2(dot_r + 2, h / 2.0), dot_r, dot_col)

	func _risk_dot_color(risk: String) -> Color:
		if risk.begins_with("Low"):
			return Color("#2E6B45")
		elif risk.begins_with("High") or risk.ends_with("High"):
			return Color("#B3172F")
		return Color("#8A5A00")


## Temperature range bar visualisation.
class _TempBar extends Control:
	var _temp_min: float = 5.0
	var _temp_max: float = 15.0

	func set_range(tmin: float, tmax: float) -> void:
		_temp_min = tmin
		_temp_max = tmax
		queue_redraw()

	func _draw() -> void:
		var w := size.x
		var h := size.y
		var total_range := 22.0  # 0°C to 22°C shown on bar

		# Background track
		draw_rect(Rect2(0, h * 0.3, w, h * 0.4), Color("#1F5E73"), true)

		# Optimal zone (12–16°C)
		var opt_start := 12.0 / total_range * w
		var opt_end := 16.0 / total_range * w
		draw_rect(Rect2(opt_start, h * 0.1, opt_end - opt_start, h * 0.8),
				Color("#2E6B45"), true)
		draw_rect(Rect2(opt_start, h * 0.1, opt_end - opt_start, h * 0.8),
				Color("#2E6B45"), false, 1.0)

		# Region range
		var range_start := _temp_min / total_range * w
		var range_end := _temp_max / total_range * w
		var range_color := Color("#1F5E73") if _temp_max < 17.0 else Color("#8A5A00")
		if _temp_max >= 19.0:
			range_color = Color("#B3172F")
		draw_rect(Rect2(range_start, h * 0.25, range_end - range_start, h * 0.5),
				Color(range_color, 0.7), true)

		# Tick marks
		for t in [5, 10, 12, 15, 16, 20]:
			var tx := float(t) / total_range * w
			draw_line(Vector2(tx, h * 0.8), Vector2(tx, h), Color(1, 1, 1, 0.3), 1.0)
