class_name GeoDrillStack
extends Control
## The map's levels below national (geo stages 2-3 plan Track D): the level
## stack national -> region -> site, laid over the national chart, which stays
## alive underneath. MapPanel reaches it only through its geo-drill hooks
## (drill_into, map_level, open_build_at, the Esc branch).
##
## - drill_into(area) shows the area's region view, its breadcrumbs and the
##   detail column's region section; a repeat drill reads from cache.
## - A star, a candidate button, a hexagon, a company site or a rival icon
##   opens the site card; its actions go back to MapPanel (open_build_at,
##   open_auction, the 3D jump, the Hav family with hav_1).
## - step_up() is Esc: site -> region -> national; at national it returns
##   false and Esc closes the tab as before.
## - The level is set synchronously; the 160 ms crossfade is cosmetic only
##   (and skipped under reduced motion).
## - On day signals, only while visible: the candidates' states and the
##   company's sites are read again, and only when the expansion overview or
##   the rival layout's signature changed (on_sim_changed).

signal level_changed(level: String)

const FADE_S := 0.16

var _panel: Control = null
var _sim: Object = null
var _level := "national"
var _po := 0
var _region: GeoRegionView
var _site: GeoSiteCard
var _crumbs: MapBreadcrumbs
var _detail: GeoRegionDetail = null
var _fade: Tween = null
var _last_overview_sig := ""
var _last_signature := ""
## Returns the expansion overview MapPanel last read ({} when none).
var _overview_of := Callable()
## Drills since the process started (tests: a repeat drill is from cache).
var drill_count := 0


func _ready() -> void:
	name = "GeoDrillStack"
	mouse_filter = Control.MOUSE_FILTER_STOP
	visible = false
	_region = GeoRegionView.new()
	_region.name = "GeoRegionView"
	add_child(_region)
	_region.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_site = GeoSiteCard.new()
	_site.name = "GeoSiteCard"
	_site.visible = false
	add_child(_site)
	_site.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_crumbs = MapBreadcrumbs.new()
	_crumbs.name = "MapBreadcrumbs"
	add_child(_crumbs)
	_crumbs.position = Vector2(12, 12)
	_region.candidate_chosen.connect(open_candidate)
	_region.offshore_chosen.connect(open_offshore)
	_region.player_site_chosen.connect(open_player_site)
	_region.rival_chosen.connect(open_rival)
	_site.back_requested.connect(func() -> void: step_up())
	_site.build_requested.connect(func(zone: String, cand: String) -> void: _call_panel("open_build_at", [zone, cand]))
	_site.licence_requested.connect(func(zone: String) -> void: _call_panel("open_auction", [zone]))
	_site.view_3d_requested.connect(func(site_id: String) -> void: _call_panel("_view_site", [site_id]))
	_site.hav_requested.connect(_open_hav)
	_crumbs.back_requested.connect(func() -> void: step_up())
	_crumbs.level_requested.connect(_go_to)


## Wire the stack to its MapPanel (overview_of returns the expansion overview
## the panel last read) and the detail column it adds a section to.
func setup(panel: Control, sim: Object, overview_of: Callable, detail_host: Control, after: Control) -> void:
	_panel = panel
	_sim = sim
	_overview_of = overview_of
	if detail_host != null:
		_detail = GeoRegionDetail.new()
		_detail.visible = false
		detail_host.add_child(_detail)
		if after != null and after.get_parent() == detail_host:
			detail_host.move_child(_detail, after.get_index() + 1)
		_detail.candidate_chosen.connect(open_candidate)


## "national", "region" or "site".
func level() -> String:
	return _level


## The area drilled into (0 at national).
func area() -> int:
	return _po if _level != "national" else 0


func region_view() -> GeoRegionView:
	return _region


func site_card() -> GeoSiteCard:
	return _site


func breadcrumbs() -> MapBreadcrumbs:
	return _crumbs


func detail() -> GeoRegionDetail:
	return _detail


## Drill into a production area (1-13): the region level. False for an area
## that does not exist (the level does not change).
func drill_into(po: int) -> bool:
	if GeoRegionMath.transform(po).is_empty():
		return false
	drill_count += 1
	_po = po
	var home: Variant = _panel.call("home_area") if _panel != null and _panel.has_method("home_area") else 0
	_region.show_area(po, _sim, int(home))
	_site.visible = false
	_region.visible = true
	_set_level("region")
	_crumbs.show_path(_area_label(po), "")
	if _detail != null:
		_detail.show_area(GeoRegionBridge.rival_summary(_sim, po), _region.candidates(), _region)
		_detail.visible = true
	_remember_overview()
	visible = true
	_crossfade(self)
	_focus_candidate.call_deferred(_first_candidate_id())
	return true


## The suggested candidate's id, else the first one's ("" when none).
func _first_candidate_id() -> String:
	var rows := _region.candidates()
	for row: Dictionary in rows:
		if bool(row.get("suggested", false)):
			return String(row.get("id", ""))
	return String(rows[0].get("id", "")) if not rows.is_empty() else ""


## Open a candidate's card (a star or its button).
func open_candidate(row: Dictionary) -> void:
	_site.show_candidate(row, _region)
	_enter_site(String(row.get("label", "")))


func open_offshore(row: Dictionary) -> void:
	_site.show_offshore(row, _region)
	_enter_site(String(row.get("area", "")))


func open_player_site(row: Dictionary) -> void:
	_site.show_player_site(row, _region)
	_enter_site(String(row.get("site_label", "")))


func open_rival(icon: Dictionary) -> void:
	_site.show_rival(icon, _region)
	_enter_site(String(icon.get("owner_name", "")))


## Esc: one level up. False at national (the tab then closes as before).
func step_up() -> bool:
	match _level:
		"site":
			_site.visible = false
			_set_level("region")
			_crumbs.show_path(_area_label(_po), "")
			_crossfade(_region)
			_focus_candidate.call_deferred(String(_site.row().get("id", "")))
			return true
		"region":
			go_national()
			return true
	return false


## Back to the national chart (the tab closing or a crumb).
func go_national() -> void:
	if _level == "national":
		return
	_site.visible = false
	visible = false
	if _detail != null:
		_detail.visible = false
	_set_level("national")


## The sim moved a day or changed state: while visible, re-read only what a
## build, a licence or a rival change can move, and only when it moved.
func on_sim_changed(force: bool = false) -> void:
	if _level == "national" or not is_visible_in_tree():
		return
	var sig := _overview_signature()
	if not force and sig == _last_overview_sig and not _rivals_moved():
		return
	_last_overview_sig = sig
	_region.refresh_live(force)
	if _detail != null:
		_detail.show_area(GeoRegionBridge.rival_summary(_sim, _po), _region.candidates(), _region)
	if _level == "site" and _site.kind() == "candidate":
		var id := String(_site.row().get("id", ""))
		for row: Dictionary in _region.candidates():
			if String(row.get("id", "")) == id:
				_site.show_candidate(row, _region)


# ── Internals ─────────────────────────────────────────────────────────────────

func _enter_site(place: String) -> void:
	_site.visible = true
	_set_level("site")
	_crumbs.show_path(_area_label(_po), place)
	_crossfade(_site)
	var action := _site.action_button()
	if action.visible and not action.disabled:
		action.grab_focus.call_deferred()
	else:
		_site.back_button().grab_focus.call_deferred()


func _go_to(level: String) -> void:
	if level == "national":
		go_national()
	elif level == "region" and _level == "site":
		step_up()


func _set_level(level: String) -> void:
	if level == _level:
		return
	_level = level
	level_changed.emit(level)


func _focus_candidate(candidate_id: String) -> void:
	if _detail == null or candidate_id == "":
		return
	var b := _detail.candidate_button(candidate_id)
	if b != null and b.is_visible_in_tree():
		b.grab_focus()


func _crossfade(node: CanvasItem) -> void:
	if _fade != null and _fade.is_valid():
		_fade.kill()
	if MotionPrefs.reduced_motion() or not is_inside_tree():
		node.modulate.a = 1.0
		return
	node.modulate.a = 0.0
	_fade = create_tween()
	_fade.tween_property(node, "modulate:a", 1.0, FADE_S)


func _open_hav() -> void:
	if _panel == null:
		return
	_panel.call("open_build_family", "hav")
	var dialog: Variant = _panel.call("build_dialog")
	if dialog != null:
		dialog.call("choose_plot", "hav_1")


func _call_panel(method: String, args: Array) -> void:
	if _panel != null and _panel.has_method(method):
		_panel.callv(method, args)


func _area_label(po: int) -> String:
	for f: Dictionary in GeoData.production_areas("national"):
		if int(f["properties"]["id"]) == po:
			return tr("MAP_REGION_AREA_NAME") % [po, String(f["properties"]["name"])]
	return ""


## What in the expansion overview can move a candidate's state or a company
## site: per zone its sites, licence, auction and both verdicts, not the
## figures that move every day (biomass, cash).
func _overview_signature() -> String:
	var overview: Variant = _overview_of.call() if _overview_of.is_valid() else {}
	if not overview is Dictionary:
		return ""
	var parts := PackedStringArray()
	for a: Dictionary in (overview as Dictionary).get("areas", []):
		for z: Dictionary in a.get("zones", []):
			parts.append("%s:%d:%s:%s:%s:%s:%s:%s" % [z.get("zone_id", ""), int(z.get("player_sites", 0)),
				z.get("can_build", false), z.get("build_reason_key", ""), z.get("can_apply", false),
				z.get("apply_reason_key", ""), z.get("holds_licence", false), z.get("open_auction_id", "")])
	return "|".join(parts)


func _remember_overview() -> void:
	_last_overview_sig = _overview_signature()
	_last_signature = GeoRegionBridge.geo_sites_signature(_sim)



func _rivals_moved() -> bool:
	var signature := GeoRegionBridge.geo_sites_signature(_sim)
	if signature == _last_signature:
		return false
	_last_signature = signature
	return true
