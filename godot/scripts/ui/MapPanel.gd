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
		"temp_range": "7.5 – 13.5 °C",
		"temp_min": 7.5,
		"temp_max": 13.5,
		"disease_risk": "Low",
		"disease_details": "Low disease pressure. Warmer summer temperatures may occasionally stress fish.",
		"distance_to_processing": "180 km (Kristiansand)",
		"processing_hub": "Kristiansand, Stavanger",
		"annual_harvest_kt": 15,
		"key_operators": ["Skagerak Salmon", "Mowi"],
		"description": "Norway's southernmost production zone, stretching from the Swedish border to Jæren. Summer peaks can approach 20°C in shallow areas, making thermal stress a consideration. Relatively few sheltered deep-water fjord locations compared to the west coast, making it a smaller but strategic production area.",
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
		"temp_range": "7.0 – 13.0 °C",
		"temp_min": 7.0,
		"temp_max": 13.0,
		"disease_risk": "Medium",
		"disease_details": "Moderate disease risk. Stable conditions in the deep Ryfylke fjords.",
		"distance_to_processing": "60 km (Stavanger)",
		"processing_hub": "Stavanger, Hjelmeland",
		"annual_harvest_kt": 50,
		"key_operators": ["Grieg Seafood", "Bremnes Seashore", "Mowi"],
		"description": "Rogaland's main salmon farming region, characterized by deep, sheltered fjords like the Boknafjord system. Strong logistics infrastructure tied to the Stavanger region. Summer temperatures are generally favorable, with excellent deep-water exchange providing good oxygenation.",
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
		"temp_range": "7.0 – 12.5 °C",
		"temp_min": 7.0,
		"temp_max": 12.5,
		"disease_risk": "Medium-High",
		"disease_details": "Elevated disease and sea lice risk, requiring strict biosecurity and active treatment.",
		"distance_to_processing": "80 km (Bergen/Bømlo)",
		"processing_hub": "Bømlo, Bergen",
		"annual_harvest_kt": 80,
		"key_operators": ["Lerøy", "Mowi", "Bremnes Seashore"],
		"description": "Spanning the outer coastal archipelago and inner fjords between Rogaland and Vestland. High density of farming activity historically led to elevated biological pressures, making this a complex but highly productive zone. Key hub for maritime technology and aquaculture innovation.",
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
		"temp_range": "6.5 – 12.0 °C",
		"temp_min": 6.5,
		"temp_max": 12.0,
		"disease_risk": "High",
		"disease_details": "High disease and lice pressure. Strict area management needed to navigate challenges.",
		"distance_to_processing": "120 km (Florø)",
		"processing_hub": "Florø, Måløy",
		"annual_harvest_kt": 110,
		"key_operators": ["Mowi", "SalMar", "Cermaq"],
		"description": "Covering the rugged coastline north of Bergen up to the exposed Stadt peninsula. This region sees significant biological pressure, but benefits from strong tidal currents and deep fjord basins like the Sognefjord. Production requires careful risk management and robust equipment.",
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
		"temp_range": "6.5 – 11.5 °C",
		"temp_min": 6.5,
		"temp_max": 11.5,
		"disease_risk": "Medium-High",
		"disease_details": "Moderate to high disease risk. Exposed areas face tough weather conditions.",
		"distance_to_processing": "90 km (Ålesund)",
		"processing_hub": "Ålesund, Molde",
		"annual_harvest_kt": 95,
		"key_operators": ["Hofseth", "SalMar", "Mowi"],
		"description": "Møre og Romsdal's primary zone, offering deep fjords like the Storfjord and exposed island sites. The region is a major export hub through Ålesund. Sites here must contend with winter storms but benefit from excellent water exchange from the Norwegian Sea.",
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
		"temp_range": "6.0 – 11.0 °C",
		"temp_min": 6.0,
		"temp_max": 11.0,
		"disease_risk": "Low",
		"disease_details": "Low disease pressure with excellent biological performance in recent years.",
		"distance_to_processing": "150 km (Hitra/Frøya)",
		"processing_hub": "Kristiansund, Hitra",
		"annual_harvest_kt": 120,
		"key_operators": ["Lerøy Midt", "SalMar", "Måsøval"],
		"description": "A highly productive region bridging Møre og Romsdal and Trøndelag. The islands of Hitra and Frøya to the north provide world-class farming conditions. The zone currently enjoys a green traffic light status, indicating sustainable lice levels and good capacity for growth.",
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
		"temp_range": "5.5 – 10.5 °C",
		"temp_min": 5.5,
		"temp_max": 10.5,
		"disease_risk": "Low",
		"disease_details": "Low biological risk. Excellent water quality and low lice levels.",
		"distance_to_processing": "120 km (Rørvik)",
		"processing_hub": "Rørvik, Nærøysund",
		"annual_harvest_kt": 100,
		"key_operators": ["SinkabergHansen", "SalMar", "Mowi"],
		"description": "Covering the northern part of Trøndelag up to the southern border of Nordland. Characterized by excellent biological conditions and strong local ownership in the aquaculture sector. Rørvik/Nærøysund is one of Norway's most vital maritime and aquaculture clusters.",
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
		"temp_range": "5.0 – 10.0 °C",
		"temp_min": 5.0,
		"temp_max": 10.0,
		"disease_risk": "Medium",
		"disease_details": "Moderate disease risk. PD is occasionally present, requiring careful surveillance.",
		"distance_to_processing": "180 km (Bodø/Sandnessjøen)",
		"processing_hub": "Bodø, Sandnessjøen",
		"annual_harvest_kt": 130,
		"key_operators": ["Nova Sea", "Cermaq", "Kvarøy"],
		"description": "The stunning Helgeland coast features thousands of islands providing sheltered farming locations with strong ocean currents. A major engine for Northern Norway's aquaculture economy, with strong community integration and innovative local producers.",
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
		"temp_range": "4.5 – 9.5 °C",
		"temp_min": 4.5,
		"temp_max": 9.5,
		"disease_risk": "Low",
		"disease_details": "Low disease pressure. Cold water reduces lice growth significantly.",
		"distance_to_processing": "160 km (Sortland/Bodø)",
		"processing_hub": "Sortland, Svolvær",
		"annual_harvest_kt": 110,
		"key_operators": ["Nordlaks", "Cermaq", "Eidsfjord Sjøfarm"],
		"description": "Encompassing the massive Vestfjorden basin and the Vesterålen archipelago. Known for pioneering offshore and semi-closed containment systems to utilize exposed areas. The cold, oxygen-rich waters produce excellent fish welfare and quality, though winter darkness slows growth.",
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
		"temp_range": "4.0 – 9.0 °C",
		"temp_min": 4.0,
		"temp_max": 9.0,
		"disease_risk": "Low",
		"disease_details": "Very low biological pressure. Strong tidal currents keep sites clean.",
		"distance_to_processing": "140 km (Finnsnes)",
		"processing_hub": "Finnsnes, Senja",
		"annual_harvest_kt": 90,
		"key_operators": ["SalMar", "Wilsgård", "Lerøy"],
		"description": "Bridging northern Nordland and southern Troms. The region is dominated by the large islands of Andøya and Senja. The harsh, exposed environment requires heavy-duty equipment but rewards operators with minimal lice issues and premium quality output.",
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
		"temp_range": "3.5 – 8.5 °C",
		"temp_min": 3.5,
		"temp_max": 8.5,
		"disease_risk": "Low",
		"disease_details": "Excellent biosecurity. Low risk of systemic diseases like PD or ISA.",
		"distance_to_processing": "100 km (Tromsø)",
		"processing_hub": "Tromsø, Skjervøy",
		"annual_harvest_kt": 75,
		"key_operators": ["Lerøy Aurora", "Cermaq", "Arnøy Laks"],
		"description": "Centered around the Tromsø region, offering pristine arctic fjord conditions. The cold water requires specialized feed and light regimes to optimize growth and prevent winter maturation. Excellent biological performance makes it a highly profitable zone for established players.",
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
		"temp_range": "3.0 – 8.0 °C",
		"temp_min": 3.0,
		"temp_max": 8.0,
		"disease_risk": "Low",
		"disease_details": "Minimal disease risk. Strict transport regulations protect the zone's status.",
		"distance_to_processing": "150 km (Alta)",
		"processing_hub": "Alta, Hammerfest",
		"annual_harvest_kt": 60,
		"key_operators": ["Grieg Seafood", "Cermaq", "SalMar"],
		"description": "Norway's western Arctic frontier. The Altafjord is a historically important area for salmon farming development in the north. The extreme photoperiod (midnight sun and polar night) profoundly affects fish biology. Exceptional water quality and low lice counts.",
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
		"temp_range": "2.5 – 7.5 °C",
		"temp_min": 2.5,
		"temp_max": 7.5,
		"disease_risk": "Low",
		"disease_details": "Extremely low lice and disease risk. Risk of winter sores due to very cold temperatures.",
		"distance_to_processing": "250 km (Alta / Kirkenes)",
		"processing_hub": "Kirkenes, Vadsø",
		"annual_harvest_kt": 40,
		"key_operators": ["SalMar", "Grieg Seafood"],
		"description": "The northeasternmost point of Norway's salmon production, bordering the Barents Sea. Extremely low water temperatures can slow growth and cause winter sores, requiring careful handling and specific diet formulations. However, the region represents the ultimate pristine environment for arctic salmon.",
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

# ─────────────────────────────────────────────────────────────────────────────
func _ready() -> void:
	_apply_localization()
	_close_btn.pressed.connect(_on_close_pressed)
	_confirm_btn.pressed.connect(_on_confirm_pressed)
	# Decorative coastline backdrop (custom drawing, added behind labels).
	var coastline := _CoastlineDrawer.new()
	coastline.set_anchors_preset(Control.PRESET_FULL_RECT)
	_map_container.add_child(coastline)
	_map_container.move_child(coastline, 0)
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
	_map_icon.text = tr("🗺️  ")
	_title_label.text = tr("SELECT REGION — NORWEGIAN COASTLINE")
	_subtitle_label.text = tr("UI_MAP_SUBTITLE")
	_map_title.text = tr("UI_MAP_NORWAY")
	_compass_label.text = tr("UI_MAP_COMPASS_N")

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

# ── Detail panel ──────────────────────────────────────────────────────────────
# (Detail panel layout now lives in scenes/ui/MapPanel.tscn.)

# ─────────────────────────────────────────────────────────────────────────────
func _show_region(region_id: String) -> void:
	var r := _get_region(region_id)
	if r.is_empty():
		return

	_detail_name.text = r["name"]
	_detail_zone.text = "Production Zone " + r["production_zone"] + "  •  " + r["norwegian_name"]
	_detail_icon.text = r.get("icon", "📍")
	_detail_desc.text = r["description"]
	_detail_licenses.text = str(r["available_licenses"]) + " licences"
	_detail_temp.text = r["temp_range"]
	_detail_distance.text = r["distance_to_processing"]
	_detail_processing.text = r["processing_hub"]
	_detail_harvest.text = str(r["annual_harvest_kt"]) + " 000 tonnes/year"
	_detail_operators.text = tr("\n")

	var risk: String = r["disease_risk"]
	_detail_risk_label.text = risk
	var risk_color := _risk_color(risk)
	LakskongenTheme.set_text_color(_detail_risk_label, risk_color)
	_detail_risk_indicator.color = risk_color

	# Update disease details label
	_disease_details.text = r.get("disease_details", "")

	# Temperature bar
	if _temp_bar is _TempBar:
		_temp_bar.set_range(r["temp_min"], r["temp_max"])

	_confirm_btn.disabled = false
	_confirm_btn.text = "  SELECT " + r["name"].to_upper() + "  "

func _clear_selection() -> void:
	_selected_region_id = ""
	_detail_name.text = tr("UI_MAP_SELECT_REGION")
	_detail_zone.text = tr("UI_MAP_CLICK_PROMPT")
	_detail_icon.text = tr("🗺️")
	_detail_desc.text = tr("UI_MAP_REGION_DESC")
	_detail_licenses.text = tr("—")
	_detail_temp.text = tr("—")
	_detail_distance.text = tr("—")
	_detail_processing.text = tr("—")
	_detail_harvest.text = tr("—")
	_detail_operators.text = tr("—")
	_detail_risk_label.text = tr("—")
	LakskongenTheme.set_text_color(_detail_risk_label, TEXT_BODY)
	_detail_risk_indicator.color = TEXT_MUTED
	_confirm_btn.disabled = true
	_confirm_btn.text = tr("  SELECT THIS REGION  ")
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
					draw_string(_font, Vector2(6, y - 4), "%d°N" % int(lat),
						HORIZONTAL_ALIGNMENT_LEFT, -1, 13, GRID_TEXT)
			lat += 2.0
		# Longitude lines every 5 degrees with bottom labels.
		var lon := 5.0
		while lon <= 30.0:
			var x := NorwayCoastline.project(lon, 65.0).x * w
			if x >= 0.0 and x <= w:
				draw_line(Vector2(x, 0), Vector2(x, h), GRID_LINE, 1.0)
				if _font:
					draw_string(_font, Vector2(x + 4, h - 6), "%d°E" % int(lon),
						HORIZONTAL_ALIGNMENT_LEFT, -1, 13, GRID_TEXT)
			lon += 5.0

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

	func _draw_cities(w: float, h: float) -> void:
		if _font == null:
			return
		for c in NorwayCoastline.cities():
			var lon := float(c[0])
			var pos := NorwayCoastline.project(lon, float(c[1])) * Vector2(w, h)
			if pos.x < -20.0 or pos.x > w + 60.0 or pos.y < -10.0 or pos.y > h + 10.0:
				continue
			draw_circle(pos, 4.5, COAST_LINE)
			draw_circle(pos, 2.5, Color.WHITE)
			# Crowded south-west coast: label over open sea (west) instead
			# of colliding with region names stacked on land (east).
			if lon < 9.0:
				var tw := _font.get_string_size(str(c[2]), HORIZONTAL_ALIGNMENT_LEFT, -1, 13).x
				draw_string(_font, pos + Vector2(-8.0 - tw, 4), str(c[2]),
					HORIZONTAL_ALIGNMENT_LEFT, -1, 13, Color("#1C1E20"))
			else:
				draw_string(_font, pos + Vector2(8, 4), str(c[2]),
					HORIZONTAL_ALIGNMENT_LEFT, -1, 13, Color("#1C1E20"))


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
