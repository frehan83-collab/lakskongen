class_name GeoDrillStack
extends Control
## The map's levels below national (geo stages 2-3 plan Track D): the level
## stack national -> region -> site, laid over the national chart, which stays
## alive underneath. MapPanel reaches it only through its geo-drill hooks
## (drill_into, map_level, open_build_at, the Esc branch, the chart's
## feature signals).
##
## - drill_into(area) shows the area's region view, its breadcrumbs and the
##   detail column's region section; a repeat drill reads from cache.
## - A star, a candidate button, a hexagon, a company site (on the map or its
##   row) or a rival icon opens the site card; its actions go back to
##   MapPanel (open_build_at, open_auction, the 3D jump, the Hav family with
##   hav_1).
## - step_up() is Esc: site -> region -> national, the focus back on the row
##   or button the card came from; at national it returns false and Esc
##   closes the tab as before.
## - The level is set synchronously; the 160 ms crossfade is cosmetic only:
##   each faded node has its own tween, so a fade that starts on one node
##   never stops another's half way (skipped under reduced motion).
## - On day signals, only while visible: the candidates' states and the
##   company's sites are read again, and only when the expansion overview or
##   the rival layout's signature changed (on_sim_changed).
## Every sim read goes through the map's one adapter (GeoBridgeAdapter).

signal level_changed(level: String)

const FADE_S := 0.16
## The national chart's feature kinds open_feature answers: C2's
## feature_clicked kinds, the same list as GeoMapView.CARD_KINDS.
const FEATURE_KINDS: Array[String] = ["candidates", "hexagon", "player", "site"]

var _panel: Control = null
var _bridge: GeoBridgeAdapter = null
var _level := "national"
var _po := 0
var _region: GeoRegionView
var _site: GeoSiteCard
var _crumbs: MapBreadcrumbs
var _detail: GeoRegionDetail = null
## The running crossfade of each node (its own tween).
var _fades: Dictionary = {}
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


## Wire the stack to its MapPanel, the map's adapter (the chart's), the
## expansion overview the panel last read, and the detail column it adds a
## section to (after the given control).
func setup(panel: Control, bridge: GeoBridgeAdapter, overview_of: Callable, detail_host: Control, after: Control) -> void:
	_panel = panel
	_bridge = bridge
	_overview_of = overview_of
	if detail_host != null:
		_detail = GeoRegionDetail.new()
		_detail.visible = false
		detail_host.add_child(_detail)
		if after != null and after.get_parent() == detail_host:
			detail_host.move_child(_detail, after.get_index() + 1)
		_detail.candidate_chosen.connect(open_candidate)
		_detail.player_site_chosen.connect(open_player_site)
		_detail.offshore_chosen.connect(open_offshore)


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
	_region.show_area(po, _bridge)
	_site.visible = false
	_region.visible = true
	_set_level("region")
	_crumbs.show_path(_area_label(po), "")
	_show_detail()
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


## Open the card of a national chart feature (Track C2's GeoMapView: a
## double-click on it, or the detail column's button naming it): drill into
## its area, then open its card. "candidates" (a star's best candidate id),
## "hexagon" (a Havbruk til havs id), "player" (a company site id), "site" (a
## locality id; its card when the locality has a rival owner, else the
## region level). False when the feature is not found (the level does not
## change).
func open_feature(kind: String, id: String) -> bool:
	match kind:
		"candidates":
			var po := candidate_area(id)
			if not _drill_from_panel(po):
				return false
			var row := _row_with(_region.candidates(), "id", id)
			if not row.is_empty():
				open_candidate(row)
			return true
		"hexagon":
			var row := _row_with(_bridge.offshore_candidates() if _bridge != null else [], "id", id)
			if row.is_empty() or not _drill_from_panel(int(row.get("po", 0))):
				return false
			var shown := _row_with(_region.offshore(), "id", id)
			if not shown.is_empty():
				open_offshore(shown)
			return true
		"player":
			var row := _row_with(_bridge.player_site_locations() if _bridge != null else [], "site_id", id)
			if row.is_empty() or not _drill_from_panel(int(row.get("po", 0))):
				return false
			var shown := _row_with(_region.player_sites(), "site_id", id)
			if not shown.is_empty():
				open_player_site(shown)
			return true
		"site":
			var at := _site_index(_bridge.sites() if _bridge != null else {}, id)
			if at.is_empty() or not _drill_from_panel(int(at["po"])):
				return false
			for icon: Dictionary in (_region.rival_layer() as Object).get("icons"):
				if int(icon.get("id", -1)) == int(id):
					open_rival(icon)
			return true
	return false


## What a national feature's card is about, for the detail column's button
## ("" when the feature has no card): the candidate's rank and place, the
## offshore area, the company site, or the rival locality and its owner. A
## locality in no production area (inland, or the offshore unit) has none:
## the region level shows an area's own localities only.
static func feature_label(bridge: GeoBridgeAdapter, kind: String, id: String) -> String:
	if bridge == null:
		return ""
	match kind:
		"candidates":
			var row := _row_with(bridge.sea_candidates(candidate_area(id)), "id", id)
			return "" if row.is_empty() else TranslationServer.translate("MAP_REGION_PICK_CANDIDATE") % [
				int(row.get("rank", 0)), String(row.get("label", ""))]
		"hexagon":
			var row := _row_with(bridge.offshore_candidates(), "id", id)
			return "" if row.is_empty() else TranslationServer.translate("MAP_REGION_PICK_OFFSHORE") % String(row.get("area", ""))
		"player":
			var row := _row_with(bridge.player_site_locations(), "site_id", id)
			return "" if row.is_empty() else TranslationServer.translate("MAP_REGION_PICK_SITE") % String(row.get("site_label", ""))
		"site":
			var sites := bridge.sites()
			var at := _site_index(sites, id)
			if at.is_empty() or int(at["owner"]) < 0 or int(at["po"]) < 1:
				return ""
			var owners: Array = sites.get("owners", [])
			var owner := int(at["owner"])
			var who := String((owners[owner] as Dictionary).get("name", "")) if owner < owners.size() else ""
			return TranslationServer.translate("MAP_REGION_PICK_RIVAL") % [int(id), who]
	return ""


## An open-pen candidate's production area (candidates.json), 0 when unknown.
static func candidate_area(id: String) -> int:
	for s: Dictionary in GeoData.candidates():
		if String(s.get("id", "")) == id and s.get("type", "") == "open_pen":
			return int(s.get("po", 0))
	return 0


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
			var kind := _site.kind()
			var row := _site.row()
			_site.visible = false
			_set_level("region")
			_crumbs.show_path(_area_label(_po), "")
			_crossfade(_region)
			_focus_origin.call_deferred(kind, row)
			return true
		"region":
			go_national()
			_focus_explore.call_deferred()
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
	var rivals_moved := _rivals_moved()
	if not force and sig == _last_overview_sig and not rivals_moved:
		return
	_last_overview_sig = sig
	_region.refresh_live(force)
	_show_detail()
	if _level == "site" and _site.kind() == "candidate":
		var id := String(_site.row().get("id", ""))
		for row: Dictionary in _region.candidates():
			if String(row.get("id", "")) == id:
				_site.show_candidate(row, _region)


# ── Internals ─────────────────────────────────────────────────────────────────

func _show_detail() -> void:
	if _detail == null:
		return
	_detail.show_area(_bridge.rival_summary(_po) if _bridge != null else {}, _region.candidates(), _region)
	_detail.visible = true


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


## Drill through the panel (it also selects the area in the detail column).
func _drill_from_panel(po: int) -> bool:
	if po < 1:
		return false
	if _panel != null and _panel.has_method("drill_into"):
		return bool(_panel.call("drill_into", po))
	return drill_into(po)


static func _row_with(rows: Array, key: String, value: String) -> Dictionary:
	for row: Dictionary in rows:
		if String(row.get(key, "")) == value:
			return row
	return {}


## A locality's {index, po, owner} in get_geo_sites' arrays, {} when unknown.
static func _site_index(sites: Dictionary, id: String) -> Dictionary:
	if not id.is_valid_int():
		return {}
	var ids: PackedInt32Array = sites.get("ids", PackedInt32Array())
	var at := ids.find(int(id))
	var pos: PackedByteArray = sites.get("po", PackedByteArray())
	var owner: PackedInt32Array = sites.get("owner", PackedInt32Array())
	if at < 0 or at >= pos.size():
		return {}
	return {"index": at, "po": int(pos[at]), "owner": owner[at] if at < owner.size() else -1}


func _go_to(level: String) -> void:
	if level == "national":
		go_national()
		_focus_explore.call_deferred()
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


## Back from a card: the row or button it came from (a candidate's button, a
## company site's row, hav_1's row), else the suggested candidate's button,
## else the crumbs' back button. Nothing when the level moved on meanwhile.
func _focus_origin(kind: String, row: Dictionary) -> void:
	if _level != "region" or _detail == null:
		return
	var target: Control = null
	match kind:
		"candidate":
			target = _detail.candidate_button(String(row.get("id", "")))
		"site":
			target = _detail.site_button(String(row.get("site_id", "")))
		"offshore":
			target = _detail.hav_button() if String(row.get("bound_plot_id", "")) != "" else null
	if target == null or not target.is_visible_in_tree():
		target = _detail.candidate_button(_first_candidate_id())
	if target == null or not target.is_visible_in_tree():
		target = _crumbs.back_button()
	target.grab_focus()


## Back at national: 'Utforsk området', when the panel is still shown.
func _focus_explore() -> void:
	if _level != "national" or _panel == null or not _panel.is_visible_in_tree() or not _panel.has_method("explore_button"):
		return
	var b: Button = _panel.call("explore_button")
	if b != null and b.is_visible_in_tree() and not b.disabled:
		b.grab_focus()


## A cosmetic fade-in of one node. Each node keeps its own tween: a fade that
## starts on another node (a card opened while the region still fades in)
## leaves this one running to the end, so no level is left see-through.
func _crossfade(node: CanvasItem) -> void:
	var running: Tween = _fades.get(node)
	if running != null and running.is_valid():
		running.kill()
	_fades.erase(node)
	if MotionPrefs.reduced_motion() or not node.is_inside_tree():
		node.modulate.a = 1.0
		return
	node.modulate.a = 0.0
	var fade := node.create_tween()
	fade.tween_property(node, "modulate:a", 1.0, FADE_S)
	_fades[node] = fade


## hav_1's 'Bygg her (hav)': the Hav family with the first offshore unit the
## company may start chosen (as the Hav card chooses it) and hav_1 under it,
## its lens in view. Opened on the family alone, the dialog chose no unit, so
## the lens stayed hidden and choosing a unit dropped hav_1 (GEO-VIS).
func _open_hav() -> void:
	if _panel == null:
		return
	_panel.call("open_build_family", "hav")
	var dialog: Variant = _panel.call("build_dialog")
	if dialog != null:
		dialog.call("choose_family", "hav")
		dialog.call("choose_plot", GeoOverlayLayer.HAV_PLOT)
		dialog.call("show_plot_step", "hav")


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
	_last_signature = _bridge.sites_signature() if _bridge != null else ""


## True once when the rival layout's signature moved since the last look.
func _rivals_moved() -> bool:
	var signature := _bridge.sites_signature() if _bridge != null else ""
	if signature == _last_signature:
		return false
	_last_signature = signature
	return true
