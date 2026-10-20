extends Control

## The region view (roadmap U2-01): the 13 production areas on the coast,
## each marked with its traffic light as the sim knows it and a ring where the
## company farms. Picking an area shows the company's capacity there and its
## zones with the engine's build and licence verdicts (RegionExpansionSection);
## "Bygg lokalitet" opens the BuildSiteDialog and "Søk vekstlisens" the
## LicenceAuctionCard. Every refusal is rendered as text.

signal close_requested
signal region_selected(region_id: String)

# Data-overlay legend embedded into the detail panel (see SiteOverlayLegend.gd).
const SiteOverlayLegend = preload("res://scripts/ui/SiteOverlayLegend.gd")
const NorwayCoastline = preload("res://scripts/ui/NorwayCoastline.gd")
const AuctionCardScene := preload("res://scenes/ui/LicenceAuctionCard.tscn")
const BuildDialogScene := preload("res://scenes/ui/BuildSiteDialog.tscn")

## The sim's traffic light -> the marker dot's tone on the chart.
const LIGHT_PALETTE_KEYS := {"green": &"ok", "yellow": &"warn", "red": &"crit"}

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
var _temp_bar: Control
## The chart under the markers; its city names keep clear of the markers.
var _coastline: _CoastlineDrawer = null

# ── Expansion (U2-01) ─────────────────────────────────────────────────────────
var _sim: Node = null
## The engine's expansion map (SimBridge.get_expansion_overview).
var _overview: Dictionary = {}
var _area_picker: OptionButton = null
var _expansion: RegionExpansionSection = null
var _auction_card: LicenceAuctionCard = null
var _build_dialog: BuildSiteDialog = null
var _map_legend: HBoxContainer = null
## F2-31: "Nytt anlegg" opens the build dialog's family cards.
var _new_farm_btn: Button = null

# ─────────────────────────────────────────────────────────────────────────────
func _ready() -> void:
	_sim = get_node_or_null("/root/SimBridge")
	_apply_localization()
	_close_btn.pressed.connect(_on_close_pressed)
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
	# U2-01: the area picker (a keyboard path to every area) and the expansion
	# section sit right under the area's name; the dialogs cover the panel.
	_add_expansion_section()
	# Live data-overlay legend (lice/oxygen/biomass/gill/containment).
	_add_overlay_legend()
	_add_map_legend()
	_add_dialogs()
	# Region markers are built deferred so the map container has layout size.
	call_deferred("_build_region_markers")
	_geo_hooks_ready.call_deferred()  # geo-drill hook (Track D): after the markers exist
	hide()
	if _sim != null:
		_sim.state_updated.connect(_on_sim_changed)
		_sim.day_advanced.connect(func(_d: int, _s: String) -> void: _on_sim_changed())
	# Leaving the tab closes a dialog; coming back shows the map, not a stale card.
	visibility_changed.connect(func() -> void:
		if not visible:
			if _auction_card != null:
				_auction_card.close()
			if _build_dialog != null:
				_build_dialog.close())

	var audio := get_node_or_null("/root/AudioController")
	if audio and audio.has_method("attach_ui_sounds"):
		audio.attach_ui_sounds(self)

## Embed the SiteOverlayLegend under the expansion section and wire it so
## region markers re-tint whenever the overlay metric or live site data changes.
func _add_overlay_legend() -> void:
	_overlay = SiteOverlayLegend.new()
	_overlay.name = "SiteOverlayLegend"
	var host := find_child("DetailInner", true, false)
	if host == null:
		_overlay = null
		return
	host.add_child(_overlay)
	host.move_child(_overlay, _expansion.get_index() + 1 if _expansion != null else 0)
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
	# U2-01: the invented "available licences" count gives way to the
	# company's real licences in the expansion section.
	for node_name in ["KeyLicences", "DetailLicenses"]:
		var cell := find_child(node_name, true, false) as Control
		if cell:
			cell.visible = false


# ── Expansion (U2-01) ─────────────────────────────────────────────────────────

func _add_expansion_section() -> void:
	var host := find_child("DetailInner", true, false) as VBoxContainer
	var name_row := find_child("NameRow", true, false) as Control
	if host == null:
		return
	_area_picker = OptionButton.new()
	_area_picker.name = "AreaPicker"
	_area_picker.tooltip_text = tr("EXPANSION_AREA_PICKER_TIP")
	_area_picker.custom_minimum_size = Vector2(0, 40)
	_area_picker.add_item(tr("EXPANSION_AREA_PICKER_PROMPT"), 0)
	for i in REGIONS.size():
		_area_picker.add_item(tr("UI_MAP_ZONE") % [i + 1, REGIONS[i]["name"]], i + 1)
	_area_picker.item_selected.connect(_on_area_picked)
	host.add_child(_area_picker)
	host.move_child(_area_picker, name_row.get_index() + 1 if name_row else 0)
	# F2-31: land and offshore farms start from the family cards; the button
	# sits in the header, in view whatever the detail column shows.
	_new_farm_btn = Button.new()
	_new_farm_btn.name = "NewFarmBtn"
	_new_farm_btn.text = tr("BUILD_NEW_FARM")
	_new_farm_btn.tooltip_text = tr("BUILD_NEW_FARM_TIP")
	_new_farm_btn.theme_type_variation = &"SecondaryButton"
	_new_farm_btn.custom_minimum_size = Vector2(0, 40)
	_new_farm_btn.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	_new_farm_btn.pressed.connect(func() -> void: open_build_family(""))
	var header := find_child("HeaderInner", true, false) as HBoxContainer
	var close := find_child("CloseBtn", true, false) as Control
	if header != null:
		header.add_child(_new_farm_btn)
		if close != null and close.get_parent() == header:
			header.move_child(_new_farm_btn, close.get_index())
	else:
		host.add_child(_new_farm_btn)
		host.move_child(_new_farm_btn, _area_picker.get_index() + 1)
	_expansion = RegionExpansionSection.new()
	_expansion.name = "ExpansionSection"
	_expansion.build_requested.connect(open_build)
	_expansion.licence_requested.connect(open_auction)
	host.add_child(_expansion)
	host.move_child(_expansion, _area_picker.get_index() + 1)


func _add_dialogs() -> void:
	_auction_card = AuctionCardScene.instantiate() as LicenceAuctionCard
	_auction_card.name = "LicenceAuctionCard"
	add_child(_auction_card)
	_auction_card.build_requested.connect(open_build)
	_auction_card.closed.connect(_refresh_expansion)
	_build_dialog = BuildDialogScene.instantiate() as BuildSiteDialog
	_build_dialog.name = "BuildSiteDialog"
	add_child(_build_dialog)
	_build_dialog.set_tab_probe(_tab_open)
	_build_dialog.built.connect(func(_site: String, _zone: String) -> void: _refresh_expansion())
	_build_dialog.closed.connect(_refresh_expansion)
	_build_dialog.view_site_requested.connect(_view_site)
	_build_dialog.tab_requested.connect(_show_tab)


## The chart's key: what the dots and the ring mean.
func _add_map_legend() -> void:
	_map_legend = HBoxContainer.new()
	_map_legend.name = "MapLegend"
	_map_legend.add_theme_constant_override("separation", LakskongenTheme.SPACE_SM)
	_map_legend.set_anchors_and_offsets_preset(Control.PRESET_BOTTOM_LEFT)
	_map_legend.position = Vector2(12, -40)
	_map_legend.grow_vertical = Control.GROW_DIRECTION_BEGIN
	_map_legend.mouse_filter = Control.MOUSE_FILTER_IGNORE
	var card := PanelContainer.new()
	card.theme_type_variation = &"CardCompact"
	card.mouse_filter = Control.MOUSE_FILTER_IGNORE
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", LakskongenTheme.SPACE_SM + 2)
	for entry in [["Ok", "EXPANSION_LIGHT_GREEN"], ["Warn", "EXPANSION_LIGHT_YELLOW"], ["Crit", "EXPANSION_LIGHT_RED"]]:
		var swatch := PanelContainer.new()
		swatch.theme_type_variation = StringName("Swatch" + str(entry[0]))
		swatch.custom_minimum_size = Vector2(12, 12)
		swatch.size_flags_vertical = Control.SIZE_SHRINK_CENTER
		row.add_child(swatch)
		var l := Label.new()
		l.text = tr(str(entry[1]))
		l.theme_type_variation = &"Small"
		row.add_child(l)
	var ring := Label.new()
	ring.text = tr("EXPANSION_LEGEND_YOUR_SITES")
	ring.theme_type_variation = &"SmallAccent"
	row.add_child(ring)
	card.add_child(row)
	_map_legend.add_child(card)
	_map_container.add_child(_map_legend)


## The tab a family card links to may be opened (the tutorial's gate).
func _tab_open(tab_id: String) -> bool:
	var ui := _game_ui()
	if ui == null or not ui.has_method("_is_tab_unlocked"):
		return true
	return bool(ui.call("_is_tab_unlocked", tab_id))


func _game_ui() -> Node:
	var node: Node = self
	while node != null:
		if node.has_method("show_tab") and node.has_method("get_active_tab"):
			return node
		node = node.get_parent()
	return null


func _show_tab(tab_id: String) -> void:
	var ui := _game_ui()
	if ui != null:
		ui.show_tab(tab_id)


## After a build: the fjord tab, with the camera on the new site.
func _view_site(site_id: String) -> void:
	var ui := _game_ui()
	if ui == null:
		return
	ui.show_tab("fjord")
	var scene: Node = ui.owner
	var vista: Node = scene.get_node_or_null("%FjordVistaBridge") if scene != null else null
	if vista != null and vista.has_method("select_pen_from_ui"):
		vista.select_pen_from_ui(site_id)
		vista.focus_selected_pen()


## GameUI calls this whenever the tab is shown: re-read the engine's map and
## pick the company's home area when nothing is picked yet.
func on_panel_shown() -> void:
	# Rebuild only what changed: a reopened map keeps its cards (and focus).
	_refresh_expansion(false)
	if _selected_region_id == "":
		var home := _home_area()
		if home > 0:
			select_area(home)


func _on_sim_changed() -> void:
	if is_visible_in_tree():
		_refresh_expansion(false)


func _refresh_expansion(force: bool = true) -> void:
	if _sim == null:
		return
	var fresh: Dictionary = _sim.get_expansion_overview()
	var changed: bool = force or fresh.get("areas", []) != _overview.get("areas", [])
	_overview = fresh
	if not changed:
		return
	_apply_expansion_to_markers()
	if _selected_region_id != "":
		var a := _area_view(_region_area(_selected_region_id))
		if not a.is_empty() and _expansion != null:
			_expansion.show_area(a, _overview)
			_show_area_climate(a)


## The overview's row of a production area (1-13), or {}.
func _area_view(area: int) -> Dictionary:
	for a in _overview.get("areas", []):
		if int(a.get("production_area", 0)) == area:
			return a
	return {}


## The area where the company has the most sites (0 when it has none).
func _home_area() -> int:
	var best := 0
	var best_sites := 0
	for a in _overview.get("areas", []):
		var sites := 0
		for z in a.get("zones", []):
			sites += int(z.get("player_sites", 0))
		if sites > best_sites:
			best_sites = sites
			best = int(a.get("production_area", 0))
	return best


func _region_area(region_id: String) -> int:
	return int(str(_get_region(region_id).get("production_zone", "PO0")).trim_prefix("PO"))


func _region_of_area(area: int) -> String:
	return REGIONS[area - 1]["id"] if area >= 1 and area <= REGIONS.size() else ""


## Pick a production area (1-13) as a click on its marker does.
func select_area(area: int) -> void:
	var rid := _region_of_area(area)
	if rid == "":
		return
	_selected_region_id = rid
	_show_region(rid)
	_refresh_markers()


func _on_area_picked(index: int) -> void:
	var area := _area_picker.get_item_id(index)
	if area > 0:
		select_area(area)
		_mark_area_pick(area)


## Open the licence auction card for a zone (map, inbox, an "overbudt" event).
func open_auction(zone_id: String) -> void:
	if _auction_card == null:
		return
	_select_area_of_zone(zone_id)
	_build_dialog.close()
	_auction_card.open_for_zone(zone_id)


## Open the build dialog for a zone.
func open_build(zone_id: String) -> void:
	if _build_dialog == null:
		return
	_select_area_of_zone(zone_id)
	_auction_card.close()
	_build_dialog.open_for_zone(zone_id)


## F2-31: open the build dialog on a farm family ("land", "hav", "sjo"), or
## on the three family cards ("").
func open_build_family(family_id: String) -> void:
	if _build_dialog == null:
		return
	_auction_card.close()
	_build_dialog.open_for_family(family_id)


## F2-31: the map's way into the family-first build flow.
func new_farm_button() -> Button:
	return _new_farm_btn


func auction_card() -> LicenceAuctionCard:
	return _auction_card


func build_dialog() -> BuildSiteDialog:
	return _build_dialog


## U2-02: the marker of a production area (1-13), or null.
func region_marker(area: int) -> Control:
	return _region_nodes.get(_region_of_area(area)) as Control


## U2-02: the area the company farms in (0 when none).
func home_area() -> int:
	if _overview.is_empty():
		_refresh_expansion()
	return _home_area()


## The player picked an area (its marker or the picker): the first season's
## map step waits for the company's own one.
func _mark_area_pick(area: int) -> void:
	if _sim != null and area > 0 and area == home_area():
		_sim.tutorial_mark("map_area_home")


func expansion_section() -> RegionExpansionSection:
	return _expansion


func _select_area_of_zone(zone_id: String) -> void:
	if _overview.is_empty():
		_refresh_expansion()
	for a in _overview.get("areas", []):
		for z in a.get("zones", []):
			if str(z.get("zone_id", "")) == zone_id:
				var area := int(a.get("production_area", 0))
				if _region_area(_selected_region_id) != area:
					select_area(area)
				return


func _apply_expansion_to_markers() -> void:
	for rid in _region_nodes:
		var m = _region_nodes[rid]
		if m == null or not is_instance_valid(m):
			continue
		var a := _area_view(_region_area(rid))
		var sites := 0
		for z in a.get("zones", []):
			sites += int(z.get("player_sites", 0))
		var light := str(a.get("traffic_light", ""))
		m.has_light = LIGHT_PALETTE_KEYS.has(light)
		if m.has_light:
			m.light_color = LakskongenTheme.palette(self, LIGHT_PALETTE_KEYS[light])
		m.ring_color = LakskongenTheme.palette(self, &"accent")
		m.player_sites = sites
		m.tooltip_text = tr("EXPANSION_MARKER_TIP") % [tr("UI_MAP_ZONE") % [_region_area(rid), str(_get_region(rid).get("name", ""))],
			tr("EXPANSION_LIGHT_" + light.to_upper()) if light != "" else "", sites]
		m.queue_redraw()


## The area's sea temperature range from the sim's climatology (monthly means at 3 m).
func _show_area_climate(a: Dictionary) -> void:
	var tmin := float(a.get("sea_temp_min_c", 0.0))
	var tmax := float(a.get("sea_temp_max_c", 0.0))
	_detail_temp.text = "%s – %s °C" % [SimFormatters.num(tmin, 1), SimFormatters.num(tmax, 1)]
	if _temp_bar is _TempBar:
		_temp_bar.set_range(tmin, tmax)

func _input(event: InputEvent) -> void:
	"""Handle keyboard navigation"""
	if not visible or _dialog_open():
		return

	if event.is_action_pressed("ui_cancel"):
		# geo-drill hook (Track D): below national, Esc steps up one level and is consumed.
		if _geo_step_up():
			get_viewport().set_input_as_handled()
			return
		hide()
		get_viewport().set_input_as_handled()


## True while the auction card or the build dialog is up (Esc closes it first).
func _dialog_open() -> bool:
	return (_auction_card != null and _auction_card.visible) or (_build_dialog != null and _build_dialog.visible)

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
	# Apply the current overlay (if any) and the sim's traffic lights once markers exist.
	_apply_overlay_to_markers()
	_apply_expansion_to_markers()
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

	# U2-01: the engine's view of the area (capacity, zones, verdicts) and its
	# sea temperature from the sim's climatology.
	var area := int(str(r["production_zone"]).trim_prefix("PO"))
	if _overview.is_empty():
		_refresh_expansion()
	var view := _area_view(area)
	if _expansion != null and not view.is_empty():
		_expansion.show_area(view, _overview)
		_show_area_climate(view)
	if _area_picker != null:
		_area_picker.select(area)

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
	_disease_details.text = ""
	if _expansion != null:
		_expansion.show_area({}, _overview)
	if _area_picker != null:
		_area_picker.select(0)
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
		region_selected.emit(region_id)
		_mark_area_pick(_region_area(region_id))

func _on_close_pressed() -> void:
	hide_panel()
	emit_signal("close_requested")

func _unhandled_key_input(event: InputEvent) -> void:
	if not visible or _dialog_open():
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
	## U2-01: the area's traffic light as the sim knows it (the dot) and the
	## company's sites there (a ring around the dot).
	var has_light: bool = false
	var light_color: Color = Color("#8A5A00")
	var ring_color: Color = Color("#B5452B")
	var player_sites: int = 0

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
			draw_rect(Rect2(0, 0, w, h), bg_color, true)

		# Dot: the traffic light (or the active data overlay); the selection
		# grows it and the company's sites ring it.
		var dot_col := light_color if has_light else _risk_dot_color(risk)
		if overlay_active:
			dot_col = overlay_color
		var dot_r := 7.0 if state == STATE_NORMAL else 9.0
		var center := Vector2(11.0, h / 2.0)
		if state == STATE_SELECTED:
			draw_circle(center, dot_r + 5.0, Color(region_color, 0.35))
		draw_circle(center, dot_r, dot_col)
		if player_sites > 0:
			draw_arc(center, dot_r + 3.0, 0.0, TAU, 32, ring_color, 2.5, true)

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


# ═══ BEGIN geo-drill hook (Track D: the region and site levels, docs/geo/STAGE23_PLAN.md §2 D) ═══
# MapPanel reaches the levels below national only through this block, the
# one deferred call in _ready and the Esc branch in _input (each marked
# "geo-drill hook"). The levels live in godot/scripts/ui/map/GeoDrillStack.gd.
# Merge note: C2's MapPanel v2 replaces the national chart, its markers and
# REGIONS; at that merge wire GeoMapView.area_drill_requested to drill_into
# and keep this block (it reads _region_nodes, _region_area, _selected_region_id,
# _area_picker, _map_container, _overview, _build_dialog and _auction_card).

var _geo_drill: GeoDrillStack = null
var _geo_explore_btn: Button = null


## Drill into a production area (1-13): the region level, the area also
## selected in the detail column. False for an area that does not exist.
func drill_into(area: int) -> bool:
	if area < 1 or area > GeoRegionMath.AREA_COUNT:
		return false
	if _region_area(_selected_region_id) != area:
		select_area(area)
	return _geo_stack().drill_into(area)


## The level the map shows: "national", "region" or "site".
func map_level() -> String:
	return _geo_drill.level() if _geo_drill != null else "national"


## Open the build dialog at an open-pen candidate (track B's
## BuildSiteDialog.open_for_candidate, through GeoRegionBridge).
func open_build_at(zone_id: String, candidate_id: String) -> void:
	if _build_dialog == null:
		return
	_auction_card.close()
	GeoRegionBridge.open_build_at(_build_dialog, zone_id, candidate_id)


## The region and site levels (null until the first drill).
func geo_drill() -> GeoDrillStack:
	return _geo_drill


## 'Utforsk området': the keyboard's way into the selected area.
func explore_button() -> Button:
	return _geo_explore_btn


func _geo_stack() -> GeoDrillStack:
	if _geo_drill != null:
		return _geo_drill
	_geo_drill = GeoDrillStack.new()
	_map_container.add_child(_geo_drill)
	_geo_drill.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	var host := find_child("DetailInner", true, false) as Control
	_geo_drill.setup(self, _sim, func() -> Dictionary: return _overview, host, _geo_explore_btn)
	_geo_drill.level_changed.connect(func(_level: String) -> void: _geo_update_explore())
	return _geo_drill


## Esc below national: one level up (true when it was consumed).
func _geo_step_up() -> bool:
	return _geo_drill != null and _geo_drill.step_up()


func _geo_hooks_ready() -> void:
	# A double-click on an area's marker drills into it.
	for rid: String in _region_nodes:
		var marker: Control = _region_nodes[rid]
		marker.gui_input.connect(func(event: InputEvent) -> void:
			var mb := event as InputEventMouseButton
			if mb != null and mb.pressed and mb.double_click and mb.button_index == MOUSE_BUTTON_LEFT:
				drill_into(_region_area(rid)))
	var host := find_child("DetailInner", true, false) as VBoxContainer
	if host != null and _area_picker != null:
		_geo_explore_btn = Button.new()
		_geo_explore_btn.name = "ExploreAreaBtn"
		_geo_explore_btn.text = tr("MAP_REGION_EXPLORE")
		_geo_explore_btn.theme_type_variation = &"SecondaryButton"
		_geo_explore_btn.custom_minimum_size = Vector2(0, 40)
		_geo_explore_btn.pressed.connect(func() -> void:
			var area := _region_area(_selected_region_id)
			drill_into(area if area > 0 else home_area()))
		host.add_child(_geo_explore_btn)
		host.move_child(_geo_explore_btn, _area_picker.get_index() + 1)
		# The picker follows into the region level once drilled in.
		_area_picker.item_selected.connect(func(index: int) -> void:
			var area := _area_picker.get_item_id(index)
			if area > 0 and map_level() != "national":
				drill_into(area)
			_geo_update_explore())
	region_selected.connect(func(_rid: String) -> void: _geo_update_explore())
	# Leaving the tab returns to national: the map always opens there (OD11).
	visibility_changed.connect(func() -> void:
		if not visible and _geo_drill != null:
			_geo_drill.go_national()
		_geo_update_explore())
	if _sim != null:
		_sim.state_updated.connect(_geo_on_sim_changed)
		_sim.day_advanced.connect(func(_d: int, _s: String) -> void: _geo_on_sim_changed())
	if _build_dialog != null:
		_build_dialog.built.connect(func(_site: String, _zone: String) -> void: _geo_on_sim_changed(true))
		_build_dialog.closed.connect(func() -> void: _geo_on_sim_changed(true))
	if _auction_card != null:
		_auction_card.closed.connect(func() -> void: _geo_on_sim_changed(true))
	_geo_update_explore()


## Day signals and closed dialogs: the region level re-reads what moved,
## only while it is shown (no geo read while the map is closed).
func _geo_on_sim_changed(force: bool = false) -> void:
	if _geo_drill != null and is_visible_in_tree() and map_level() != "national":
		_geo_drill.on_sim_changed(force)


func _geo_update_explore() -> void:
	if _geo_explore_btn == null:
		return
	var area := _region_area(_selected_region_id)
	if area <= 0:
		area = _home_area()
	_geo_explore_btn.disabled = area <= 0
	_geo_explore_btn.tooltip_text = tr("MAP_REGION_EXPLORE_TIP") if area > 0 else tr("MAP_REGION_EXPLORE_NONE_TIP")
	_geo_explore_btn.visible = map_level() == "national"
# ═══ END geo-drill hook ═══
