class_name GeoRegionRivals
extends Control
## The region level's rival localities (geo stages 2-3 plan Track D): the
## area's owned salmonid localities from Track A's get_geo_sites, in their
## owner's band (a major's 3D colour, CompetitorFarmVisualManager.band_color_for;
## independents slate), by kind (sea ring, land square, offshore 12-gon).
## One recording (redraw_count), re-recorded only on a resize, a new area or
## new rival data, never on a tick. Lives inside the view's map clip.

var view: GeoRegionView = null
var redraw_count := 0
## [{id, px (map), kind, owner_id, owner_name, is_major, mtb_t, color}]
var icons: Array = []


func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_PASS
	resized.connect(queue_redraw)


func set_sites(sites: Dictionary, po: int) -> void:
	icons = []
	var ids: PackedInt32Array = sites.get("ids", PackedInt32Array())
	var xs: PackedInt32Array = sites.get("x", PackedInt32Array())
	var ys: PackedInt32Array = sites.get("y", PackedInt32Array())
	var kinds: PackedByteArray = sites.get("kind", PackedByteArray())
	var pos: PackedByteArray = sites.get("po", PackedByteArray())
	var mtb: PackedFloat32Array = sites.get("mtb_t", PackedFloat32Array())
	var owner: PackedInt32Array = sites.get("owner", PackedInt32Array())
	var owners: Array = sites.get("owners", [])
	# kinds/stages/names arrive as PackedStringArray from the lane.
	var kind_names: Variant = sites.get("kinds", GeoRegionBridge.DEFAULT_SITE_KINDS)
	var stage_codes: PackedByteArray = sites.get("stage", PackedByteArray())
	var stage_names: Variant = sites.get("stages", PackedStringArray())
	var names: Variant = sites.get("names", PackedStringArray())
	for i in ids.size():
		if i >= pos.size() or int(pos[i]) != po or i >= owner.size() or owner[i] < 0 or owner[i] >= owners.size():
			continue
		var o: Dictionary = owners[owner[i]]
		var major := bool(o.get("is_major", false))
		var kind_code := int(kinds[i]) if i < kinds.size() else 0
		var icon := {
			"id": ids[i],
			"px": GeoRegionMath.map_px(po, float(xs[i]), float(ys[i])),
			"kind": str(kind_names[kind_code]) if kind_code < kind_names.size() else "other",
			"owner_id": str(o.get("id", "")),
			"owner_name": str(o.get("name", "")),
			"is_major": major,
			"mtb_t": float(mtb[i]) if i < mtb.size() else 0.0,
			"color": CompetitorFarmVisualManager.band_color_for(str(o.get("id", ""))) if major else GeoRegionIcons.INDEPENDENT,
		}
		if i < stage_codes.size() and int(stage_codes[i]) < stage_names.size():
			icon["stage"] = str(stage_names[stage_codes[i]])
		if i < names.size():
			icon["name"] = str(names[i])
		icons.append(icon)
	queue_redraw()


func _draw() -> void:
	redraw_count += 1
	if view == null:
		return
	for icon: Dictionary in icons:
		var at := view.to_view(icon["px"]) - (get_parent() as Control).position
		var col: Color = icon["color"]
		match String(icon["kind"]):
			"land_salmonid":
				draw_rect(Rect2(at - Vector2(4, 4), Vector2(8, 8)), GeoRegionIcons.HALO)
				draw_rect(Rect2(at - Vector2(3, 3), Vector2(6, 6)), col)
			"offshore_salmonid":
				var pts := PackedVector2Array()
				for k in 12:
					pts.append(at + Vector2.from_angle(TAU * k / 12.0) * 5.5)
				draw_colored_polygon(pts, GeoRegionIcons.HALO)
				pts.append(pts[0])
				draw_polyline(pts, col, 1.5, true)
			_:
				draw_circle(at, 5.0, GeoRegionIcons.HALO)
				draw_arc(at, 3.5, 0.0, TAU, 16, col, 2.0, true)


## The icon nearest a local position within 7 px, or {}.
func icon_at(local: Vector2) -> Dictionary:
	var best := {}
	var best_d := 7.0
	for icon: Dictionary in icons:
		var d := (view.to_view(icon["px"]) - (get_parent() as Control).position).distance_to(local)
		if d < best_d:
			best_d = d
			best = icon
	return best


func _get_tooltip(at_position: Vector2) -> String:
	var icon := icon_at(at_position)
	if icon.is_empty():
		return ""
	return tr("MAP_REGION_RIVAL_TIP") % [String(icon["owner_name"]), tr(GeoRegionView.kind_key(String(icon["kind"]))),
		SimFormatters.integer(int(round(float(icon["mtb_t"]))))]


func _gui_input(event: InputEvent) -> void:
	var mb := event as InputEventMouseButton
	if mb != null and mb.pressed and mb.button_index == MOUSE_BUTTON_LEFT:
		var icon := icon_at(mb.position)
		if not icon.is_empty():
			accept_event()
			view.rival_chosen.emit(icon)
