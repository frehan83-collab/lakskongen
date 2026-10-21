extends Control

## The map tab: the real Norwegian coast with its 13 production areas (geo
## stages 2-3 plan, Track C, over roadmap U2-01). The chart is a GeoMapView
## (godot/scripts/ui/map): the national base map tinted by area, a plate per
## area carrying the engine's traffic light and a ring where the company
## farms, the aquaculture sites, the planning overlays and the candidates.
## Picking an area (its plate, a click on its water or land, the picker)
## shows its facts from the sim's geo reads, the company's capacity there and
## its zones with the engine's build and licence verdicts
## (RegionExpansionSection); "Bygg lokalitet" opens the BuildSiteDialog and
## "Søk vekstlisens" the LicenceAuctionCard. Every refusal is rendered as text.
##
## Track D adds the region and site levels through the marked hooks:
## drill_into(), _on_feature_clicked(), _on_feature_drill_requested() and the
## marked branch of Esc (the geo-drill hook block at the end of this file).

signal close_requested
signal region_selected(region_id: String)

# Data-overlay legend embedded into the detail panel (see SiteOverlayLegend.gd).
const SiteOverlayLegend = preload("res://scripts/ui/SiteOverlayLegend.gd")
const AuctionCardScene := preload("res://scenes/ui/LicenceAuctionCard.tscn")
const BuildDialogScene := preload("res://scenes/ui/BuildSiteDialog.tscn")

## The sim's traffic light -> the plate badge's tone.
const LIGHT_PALETTE_KEYS := {"green": &"ok", "yellow": &"warn", "red": &"crit"}
const AREA_COUNT := 13

# ── State ─────────────────────────────────────────────────────────────────────
var _selected_region_id: String = ""
## "po01".."po13" -> the area's plate (MapMarker). Tests and the tutorial
## click these.
var _region_nodes: Dictionary = {}
var _overlay = null  # SiteOverlayLegend instance (additive; optional)

# ── Node refs (declared in scenes/ui/MapPanel.tscn) ───────────────────────────
@onready var _map_container: Control = %MapContainer
@onready var _title_label: Label = %TitleLabel
@onready var _subtitle_label: Label = %SubtitleLabel
@onready var _close_btn: Button = %CloseBtn
@onready var _detail_name: Label = %DetailName
@onready var _detail_zone: Label = %DetailZone
@onready var _detail_temp: Label = %DetailTemp
@onready var _temp_bar_slot: Control = %TempBarSlot
var _temp_bar: MapTempBar = null
## The national chart (GeoMapView).
var _chart: GeoMapView = null
## The area's facts from the sim's geo reads.
var _facts: GeoAreaFacts = null

# ── Expansion (U2-01) ─────────────────────────────────────────────────────────
var _sim: Node = null
## The engine's expansion map (SimBridge.get_expansion_overview).
var _overview: Dictionary = {}
var _area_picker: OptionButton = null
var _expansion: RegionExpansionSection = null
var _auction_card: LicenceAuctionCard = null
var _build_dialog: BuildSiteDialog = null
## F2-31: "Nytt anlegg" opens the build dialog's family cards.
var _new_farm_btn: Button = null

# ─────────────────────────────────────────────────────────────────────────────
func _ready() -> void:
	_sim = get_node_or_null("/root/SimBridge")
	_apply_localization()
	_close_btn.pressed.connect(_on_close_pressed)
	# The chart: built once, when the tab first opens; GeoData parses the
	# national files once per process.
	_chart = GeoMapView.new()
	_chart.name = "GeoMapView"
	_map_container.add_child(_chart)
	_map_container.move_child(_chart, 0)
	_chart.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_chart.setup(_sim)
	_chart.area_clicked.connect(_on_chart_area_clicked)
	_chart.area_drill_requested.connect(func(area: int) -> void: drill_into(area))
	_chart.feature_clicked.connect(_on_feature_clicked)
	_chart.feature_drill_requested.connect(_on_feature_drill_requested)
	for area: int in _chart.plates():
		_region_nodes[_region_of_area(area)] = _chart.plate(area)
	# Temperature bar visualisation (custom drawing, added to its slot).
	_temp_bar = MapTempBar.new()
	_temp_bar.custom_minimum_size = Vector2(0, 28)
	_temp_bar.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_temp_bar_slot.add_child(_temp_bar)
	# U2-01: the area picker (a keyboard path to every area) and the expansion
	# section sit right under the area's name; the dialogs cover the panel.
	_add_expansion_section()
	# Live data-overlay legend (lice/oxygen/biomass/gill/containment).
	_add_overlay_legend()
	_add_area_facts()
	_add_dialogs()
	_clear_selection()
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
	# Track D: the region and site levels' buttons and signals.
	_geo_hooks_ready()

	var audio := get_node_or_null("/root/AudioController")
	if audio and audio.has_method("attach_ui_sounds"):
		audio.attach_ui_sounds(self)

## Embed the SiteOverlayLegend under the expansion section and wire it so
## the area plates re-tint whenever the overlay metric or live site data changes.
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

## Tint each area's plate by the average overlay intensity of the company's
## sea sites in that production area. A site carries its zone id, so the
## overview's zones[].production_area says which area it farms in (the old
## lookup compared "PO03" with zone ids and never matched). Areas with no
## live site keep their plain plate (overlay_active = false).
func _apply_overlay_to_markers() -> void:
	if _overlay == null or not is_instance_valid(_overlay) or not is_visible_in_tree():
		return
	var zone_area := _zone_areas()
	for rid in _region_nodes:
		var m: MapMarker = _region_nodes[rid]
		if m == null or not is_instance_valid(m):
			continue
		var t := float(_overlay.get_area_intensity(_region_area(rid), zone_area))
		m.set_overlay(t >= 0.0, _overlay.intensity_to_color(maxf(t, 0.0)))


## zone id -> production area, from the expansion overview.
func _zone_areas() -> Dictionary:
	if _overview.is_empty():
		_refresh_expansion()
	var out := {}
	for a in _overview.get("areas", []):
		for z in a.get("zones", []):
			out[str(z.get("zone_id", ""))] = int(z.get("production_area", a.get("production_area", 0)))
	return out

func _apply_localization() -> void:
	_title_label.text = tr("UI_MAP_TITLE")
	_subtitle_label.text = tr("UI_MAP_SUBTITLE")
	for pair in [["TempHeader", "UI_MAP_KEY_TEMPERATURE"], ["OptimalLabel", "UI_MAP_TEMP_OPTIMAL"]]:
		var lbl := find_child(pair[0], true, false) as Label
		if lbl:
			lbl.text = tr(pair[1])


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
	for a: Dictionary in _chart.areas():
		_area_picker.add_item(tr("UI_MAP_ZONE") % [int(a["id"]), str(a["name"])], int(a["id"]))
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


## The area's facts (GeoAreaFacts) after the overlay legend, before the
## sea temperature.
func _add_area_facts() -> void:
	var host := find_child("DetailInner", true, false) as VBoxContainer
	if host == null:
		return
	_facts = GeoAreaFacts.new()
	host.add_child(_facts)
	var temp := find_child("TempSep", true, false) as Control
	if temp != null and temp.get_parent() == host:
		host.move_child(_facts, temp.get_index())


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
## pick the company's home area when nothing is picked yet. The map always
## opens at the national level (ruling OD11).
func on_panel_shown() -> void:
	# Rebuild only what changed: a reopened map keeps its cards (and focus).
	var changed := _refresh_expansion(false)
	if _chart != null:
		_chart.live_update(true)
	_apply_overlay_to_markers()
	if _selected_region_id == "":
		var home := _home_area()
		if home > 0:
			select_area(home)
	elif changed:
		_show_area(_region_area(_selected_region_id))


## A sim day or state change: only while the tab is shown, and only what
## changed (the overview's areas, the rival layout's signature).
func _on_sim_changed() -> void:
	if not is_visible_in_tree():
		return
	var changed := _refresh_expansion(false)
	if _chart != null and "sites" in _chart.live_update(changed) and _facts != null and _facts.area() > 0:
		_facts.show_area(_facts.area(), _geo_area(_facts.area()), _chart.bridge.rival_summary(_facts.area()))


## Re-read the engine's expansion map; true when its areas changed.
func _refresh_expansion(force: bool = true) -> bool:
	if _sim == null:
		return false
	var fresh: Dictionary = _sim.get_expansion_overview()
	var changed: bool = force or fresh.get("areas", []) != _overview.get("areas", [])
	_overview = fresh
	if not changed:
		return false
	_apply_expansion_to_markers()
	if _selected_region_id != "":
		var a := _area_view(_region_area(_selected_region_id))
		if not a.is_empty() and _expansion != null:
			_expansion.show_area(a, _overview)
			_show_area_climate(a)
	return true


## The overview's row of a production area (1-13), or {}.
func _area_view(area: int) -> Dictionary:
	for a in _overview.get("areas", []):
		if int(a.get("production_area", 0)) == area:
			return a
	return {}


## The area's row of the sim's geo areas (get_geo_production_areas), or {}.
func _geo_area(area: int) -> Dictionary:
	var areas: Array = _chart.areas() if _chart != null else []
	return areas[area - 1] if area >= 1 and area <= areas.size() else {}


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
	return int(region_id.trim_prefix("po")) if region_id.begins_with("po") else 0


func _region_of_area(area: int) -> String:
	return "po%02d" % area if area >= 1 and area <= AREA_COUNT else ""


## Pick a production area (1-13) as a click on its plate does.
func select_area(area: int) -> void:
	var rid := _region_of_area(area)
	if rid == "":
		return
	_selected_region_id = rid
	_show_area(area)
	if _chart != null:
		_chart.set_selected_area(area)
	region_selected.emit(rid)


## A click on the chart (a plate, the area's water or land).
func _on_chart_area_clicked(area: int) -> void:
	select_area(area)
	_mark_area_pick(area)


func _on_area_picked(index: int) -> void:
	var area := _area_picker.get_item_id(index)
	if area > 0:
		select_area(area)
		_mark_area_pick(area)


## The national chart.
func chart() -> GeoMapView:
	return _chart


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


## U2-02: the plate of a production area (1-13), or null.
func region_marker(area: int) -> Control:
	return _region_nodes.get(_region_of_area(area)) as Control


## U2-02: the area the company farms in (0 when none).
func home_area() -> int:
	if _overview.is_empty():
		_refresh_expansion()
	return _home_area()


## The player picked an area (its plate, the chart or the picker): the first
## season's map step waits for the company's own one.
func _mark_area_pick(area: int) -> void:
	if _sim != null and area > 0 and area == home_area():
		_sim.tutorial_mark("map_area_home")


func expansion_section() -> RegionExpansionSection:
	return _expansion


## The area facts section (GeoAreaFacts).
func area_facts() -> GeoAreaFacts:
	return _facts


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


## Each plate's badge (the engine's traffic light), the ring of the
## company's farms and the tooltip, from the overview.
func _apply_expansion_to_markers() -> void:
	for rid in _region_nodes:
		var m: MapMarker = _region_nodes[rid]
		if m == null or not is_instance_valid(m):
			continue
		var area := _region_area(rid)
		var a := _area_view(area)
		var sites := 0
		for z in a.get("zones", []):
			sites += int(z.get("player_sites", 0))
		var light := str(a.get("traffic_light", ""))
		var lit := LIGHT_PALETTE_KEYS.has(light)
		m.set_status(lit, LakskongenTheme.palette(self, LIGHT_PALETTE_KEYS[light]) if lit else Color.GRAY, sites,
			LakskongenTheme.palette(self, &"salmon"))
		m.tooltip_text = tr("EXPANSION_MARKER_TIP") % [tr("UI_MAP_ZONE") % [area, str(_geo_area(area).get("name", ""))],
			tr("EXPANSION_LIGHT_" + light.to_upper()) if light != "" else "", sites]


## The area's sea temperature range from the sim's climatology (monthly means at 3 m).
func _show_area_climate(a: Dictionary) -> void:
	var tmin := float(a.get("sea_temp_min_c", 0.0))
	var tmax := float(a.get("sea_temp_max_c", 0.0))
	_detail_temp.text = "%s – %s °C" % [SimFormatters.num(tmin, 1), SimFormatters.num(tmax, 1)]
	if _temp_bar != null:
		_temp_bar.set_range(tmin, tmax)

func _input(event: InputEvent) -> void:
	"""Handle keyboard navigation"""
	if not visible or _dialog_open():
		return

	if event.is_action_pressed("ui_cancel"):
		# geo-drill hook: Track D steps up one level here (site -> region ->
		# national) and consumes Esc; at the national level Esc closes the tab.
		if _step_up_level():
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


## Where each plate draws its anchor dot and its plate (map-container space):
## two rects per area, 26 in all.
func region_label_rects() -> Array[Rect2]:
	var out: Array[Rect2] = []
	for id in _region_nodes:
		var marker: MapMarker = _region_nodes[id]
		if not is_instance_valid(marker):
			continue
		out.append(marker.dot_rect())
		out.append(marker.get_rect())
	return out


## The city names the chart placed (map-container space; tests read them).
func city_label_rects() -> Array[Rect2]:
	return _chart.city_label_rects() if _chart != null else ([] as Array[Rect2])


# ── Detail column ─────────────────────────────────────────────────────────────

## Fill the detail column for an area: its name, the engine's view of it
## (capacity, zones, verdicts), its sea temperature from the sim's
## climatology and its facts from the sim's geo reads.
func _show_area(area: int) -> void:
	var geo := _geo_area(area)
	if geo.is_empty():
		return
	_detail_name.text = str(geo.get("name", ""))
	_detail_zone.text = tr("MAP_GEO_AREA_NUMBER") % area
	if _overview.is_empty():
		_refresh_expansion()
	var view := _area_view(area)
	if _expansion != null and not view.is_empty():
		_expansion.show_area(view, _overview)
		_show_area_climate(view)
	if _facts != null:
		_facts.show_area(area, geo, _chart.bridge.rival_summary(area))
	if _area_picker != null:
		_area_picker.select(area)

func _clear_selection() -> void:
	_selected_region_id = ""
	_detail_name.text = tr("UI_MAP_SELECT_REGION")
	_detail_zone.text = tr("UI_MAP_CLICK_PROMPT")
	_detail_temp.text = "—"
	if _expansion != null:
		_expansion.show_area({}, _overview)
	if _facts != null:
		_facts.show_area(0, {}, {})
	if _area_picker != null:
		_area_picker.select(0)
	if _chart != null:
		_chart.set_selected_area(0)

func _on_close_pressed() -> void:
	hide_panel()
	emit_signal("close_requested")

func _unhandled_key_input(event: InputEvent) -> void:
	if not visible or _dialog_open():
		return
	if event is InputEventKey and event.pressed and event.keycode == KEY_ESCAPE:
		_on_close_pressed()
		get_viewport().set_input_as_handled()


# ═══ BEGIN geo-drill hook (Track D: the region and site levels, docs/geo/STAGE23_PLAN.md §2 D) ═══
# MapPanel reaches the levels below national through this block, the marked
# Esc branch in _input and the chart signals _ready wires to it; the levels
# live in godot/scripts/ui/map/GeoDrillStack.gd. National clicks (the
# captain's ruling at D's merge, RUN_LOG §7): a click selects the area and
# names the feature it hit in the detail column, a button away from its
# card; a double-click on a feature opens its card; a double-click elsewhere
# on an area or on its plate, Enter on a plate or 'Utforsk området' drill
# into the area.

var _geo_drill: GeoDrillStack = null
var _geo_explore_btn: Button = null
var _geo_feature_btn: Button = null
## The feature the last chart click hit: {kind, id, frame}; {} for none.
var _geo_pick: Dictionary = {}


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
## BuildSiteDialog.open_for_candidate).
func open_build_at(zone_id: String, candidate_id: String) -> void:
	if _build_dialog == null:
		return
	_auction_card.close()
	_build_dialog.open_for_candidate(zone_id, candidate_id)


## The region and site levels (null until the first drill).
func geo_drill() -> GeoDrillStack:
	return _geo_drill


## 'Utforsk området': the keyboard's way into the selected area.
func explore_button() -> Button:
	return _geo_explore_btn


## The detail column's way to the card of the feature a click named.
func feature_button() -> Button:
	return _geo_feature_btn


func _geo_stack() -> GeoDrillStack:
	if _geo_drill == null:
		_geo_drill = GeoDrillStack.new()
		_map_container.add_child(_geo_drill)
		_geo_drill.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
		_geo_drill.setup(self, _chart.bridge, func() -> Dictionary: return _overview,
			find_child("DetailInner", true, false) as Control, _geo_feature_btn)
		_geo_drill.level_changed.connect(func(_level: String) -> void: _geo_update_explore())
	return _geo_drill


## Track D's level hook (Esc, the marked branch of _input): leave the site
## or region level for the one above; false at the national level.
func _step_up_level() -> bool:
	return _geo_drill != null and _geo_drill.step_up()


## Track D's site hook: a click on a feature of the national chart (C2's
## feature_clicked) names it in the detail column, a button away from its
## card; the same click selects the area (area_clicked) as before.
func _on_feature_clicked(kind: String, id: String) -> void:
	_geo_pick = {"kind": kind, "id": id, "frame": Engine.get_process_frames()}  # geo-site hook
	_geo_update_explore()


## A double-click on a feature of the national chart: its card.
func _on_feature_drill_requested(kind: String, id: String) -> void:
	_geo_stack().open_feature(kind, id)


## Track D's buttons and signals, wired at the end of _ready.
func _geo_hooks_ready() -> void:
	var host := find_child("DetailInner", true, false) as VBoxContainer
	if host != null and _area_picker != null:
		_geo_explore_btn = _geo_button("ExploreAreaBtn", func() -> void:
			var area := _region_area(_selected_region_id)
			drill_into(area if area > 0 else home_area()))
		_geo_feature_btn = _geo_button("OpenFeatureBtn", func() -> void:
			if not _geo_pick.is_empty():
				_geo_stack().open_feature(str(_geo_pick["kind"]), str(_geo_pick["id"])))
		_geo_feature_btn.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		for b: Button in [_geo_feature_btn, _geo_explore_btn]:
			host.add_child(b)
			host.move_child(b, _area_picker.get_index() + 1)
		# The picker follows into the region level once drilled in.
		_area_picker.item_selected.connect(func(index: int) -> void:
			var area := _area_picker.get_item_id(index)
			if area > 0 and map_level() != "national":
				drill_into(area)
			_geo_update_explore())
	region_selected.connect(func(_rid: String) -> void:
		# Another pick of an area forgets the feature, unless this click named it.
		if int(_geo_pick.get("frame", -1)) != Engine.get_process_frames():
			_geo_pick = {}
		_geo_update_explore())
	# Leaving the tab returns to national: the map always opens there (OD11).
	visibility_changed.connect(func() -> void:
		if not visible:
			_geo_pick = {}
			if _geo_drill != null:
				_geo_drill.go_national()
		_geo_update_explore())
	if _sim != null:
		_sim.state_updated.connect(_geo_on_sim_changed)
		_sim.day_advanced.connect(func(_d: int, _s: String) -> void: _geo_on_sim_changed())
	_build_dialog.built.connect(func(_site: String, _zone: String) -> void: _geo_on_sim_changed(true))
	_build_dialog.closed.connect(func() -> void: _geo_on_sim_changed(true))
	_auction_card.closed.connect(func() -> void: _geo_on_sim_changed(true))
	_geo_update_explore()


func _geo_button(node_name: String, on_press: Callable) -> Button:
	var b := Button.new()
	b.name = node_name
	b.theme_type_variation = &"SecondaryButton"
	b.custom_minimum_size = Vector2(0, 40)
	b.pressed.connect(on_press)
	return b


## Day signals and closed dialogs: the region level re-reads what moved,
## only while it is shown (no geo read while the map is closed).
func _geo_on_sim_changed(force: bool = false) -> void:
	if _geo_drill != null and is_visible_in_tree() and map_level() != "national":
		_geo_drill.on_sim_changed(force)


## The national level's two buttons: 'Utforsk området' for the picked (or
## home) area, and the card of the feature a click named, when it has one.
func _geo_update_explore() -> void:
	if _geo_explore_btn == null:
		return
	var national := map_level() == "national"
	var area := _region_area(_selected_region_id)
	if area <= 0:
		area = _home_area()
	_geo_explore_btn.text = tr("MAP_REGION_EXPLORE")
	_geo_explore_btn.disabled = area <= 0
	_geo_explore_btn.tooltip_text = tr("MAP_REGION_EXPLORE_TIP") if area > 0 else tr("MAP_REGION_EXPLORE_NONE_TIP")
	_geo_explore_btn.visible = national
	var label := GeoDrillStack.feature_label(_chart.bridge, str(_geo_pick["kind"]), str(_geo_pick["id"])) \
		if national and not _geo_pick.is_empty() else ""
	_geo_feature_btn.text = tr("MAP_REGION_PICK_OPEN") % label if label != "" else ""
	_geo_feature_btn.tooltip_text = tr("MAP_REGION_PICK_OPEN_TIP")
	_geo_feature_btn.visible = label != ""
# ═══ END geo-drill hook ═══
