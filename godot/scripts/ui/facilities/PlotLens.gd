class_name PlotLens
extends VBoxContainer

## The build dialog's site lens and ghost placement (roadmap F2-31,
## docs/FARM_TYPES_SPEC.md §6.2 steps 2-3): the family's plots as buttons
## (free or held), and for the chosen one the pad or slot seen from above
## with the build ghosted onto it (the lot's tanks and the pipe route to the
## intake on land, the unit's ring and its mooring footprint at sea) beside
## what the lens shows of it: grid, zoning, nature sensitivity, blasting and
## pumps on a pad; exposure, the sea and the lice band on a slot. The plot
## figures are the engine's (SimBridge.get_build_plots); the ghost's shapes
## are the campus layout the 3D presenter builds (LandCampusLayout). Choosing
## a plot is presentation only: it changes nothing in the game.

signal plot_chosen(plot_id: String)

var _buttons: HBoxContainer = null
var _ghost: GhostView = null
var _facts: GridContainer = null
## Geo B: the note under a plot bound to a real open-sea area (hav_1).
var _geo_note: Label = null
## The plots shown (the bridge's dictionaries).
var plots: Array = []
var _chosen := ""


func _ready() -> void:
	if _buttons == null:
		_build()


func _build() -> void:
	add_theme_constant_override("separation", 8)
	_buttons = HBoxContainer.new()
	_buttons.name = "PlotButtons"
	_buttons.add_theme_constant_override("separation", 8)
	add_child(_buttons)
	var lens := HBoxContainer.new()
	lens.name = "Lens"
	lens.add_theme_constant_override("separation", 16)
	add_child(lens)
	var frame := PanelContainer.new()
	frame.theme_type_variation = &"Inset"
	frame.custom_minimum_size = Vector2(260, 170)
	lens.add_child(frame)
	_ghost = GhostView.new()
	_ghost.name = "Ghost"
	frame.add_child(_ghost)
	_facts = GridContainer.new()
	_facts.name = "Facts"
	_facts.columns = 2
	_facts.add_theme_constant_override("h_separation", 14)
	_facts.add_theme_constant_override("v_separation", 4)
	_facts.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	lens.add_child(_facts)
	_geo_note = Label.new()
	_geo_note.name = "GeoNote"
	_geo_note.theme_type_variation = &"CaptionMuted"
	_geo_note.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_geo_note.custom_minimum_size = Vector2(300, 0)
	_geo_note.visible = false
	add_child(_geo_note)


## Show the plots of a family; `chosen` is the plot the ghost stands on ("" none).
func show_plots(list: Array, chosen: String, variant_id: String, option: String) -> void:
	if _buttons == null:
		_build()
	plots = list
	_chosen = chosen
	for child in _buttons.get_children():
		_buttons.remove_child(child)
		child.queue_free()
	for p in list:
		var id := str(p.get("id", ""))
		var b := Button.new()
		b.name = "Plot_" + id
		b.toggle_mode = true
		b.set_pressed_no_signal(id == chosen)
		b.text = "%s · %s" % [str(p.get("name", id)), _state_text(p)]
		b.theme_type_variation = &"SmallButton" if id == chosen else &"SmallGhostButton"
		b.custom_minimum_size = Vector2(0, 36)
		UIPolish.set_available(b, "" if bool(p.get("free", false)) else tr("BUILD_PLOT_HELD"))
		b.pressed.connect(func() -> void: plot_chosen.emit(id))
		_buttons.add_child(b)
	UIPolish.link_horizontal_focus(_buttons)
	var plot := plot_by_id(chosen)
	_ghost.configure(plot, variant_id, option)
	_fill_facts(plot)


## The plot dictionary of `id` ({} when not shown).
func plot_by_id(id: String) -> Dictionary:
	for p in plots:
		if str(p.get("id", "")) == id:
			return p
	return {}


## The button of plot `id`, or null.
func plot_button(id: String) -> Button:
	return _buttons.get_node_or_null("Plot_" + id) as Button if _buttons != null else null


## The ghost drawing (tests read what it ghosts).
func ghost() -> GhostView:
	return _ghost


func _state_text(p: Dictionary) -> String:
	if not bool(p.get("free", false)):
		return tr("BUILD_PLOT_STATE_HELD")
	if str(p.get("family", "")) == "land":
		return tr("BUILD_PLOT_STATE_ZONED") if bool(p.get("zoned", false)) else tr("BUILD_PLOT_STATE_UNZONED")
	return tr("BUILD_PLOT_EXPOSURE_" + str(p.get("exposure", "")).to_upper())


func _fact(key: String, value: String) -> void:
	var k := Label.new()
	k.text = tr(key)
	k.theme_type_variation = &"SmallMuted"
	_facts.add_child(k)
	var v := Label.new()
	v.text = value
	v.theme_type_variation = &"FigureSmall"
	v.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	v.custom_minimum_size = Vector2(140, 0)
	v.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_facts.add_child(v)


func _fill_facts(p: Dictionary) -> void:
	for child in _facts.get_children():
		_facts.remove_child(child)
		child.queue_free()
	var geo := geo_binding(str(p.get("id", ""))) if str(p.get("family", "")) == "hav" else {}
	_geo_note.visible = bool(geo.get("bound", false))
	_geo_note.text = tr("GEO_PLOT_CLIMATE_NOTE")
	if p.is_empty():
		var hint := Label.new()
		hint.text = tr("BUILD_PLOT_PICK")
		hint.theme_type_variation = &"BodySmallMuted"
		hint.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		hint.custom_minimum_size = Vector2(200, 0)
		_facts.add_child(hint)
		return
	if _geo_note.visible:
		# Geo B: hav_1 lies in a real Havbruk til havs area (display only).
		_fact("GEO_PLOT_AREA", tr("GEO_PLOT_AREA_VALUE") % [str(geo.get("area", "")), int(geo.get("po", 0))])
		_fact("GEO_PLOT_STAGE", tr("GEO_PLOT_STAGE_" + str(geo.get("stage", "")).to_upper()))
		_fact("BUILD_PLOT_DISTANCE", tr("GEO_PLOT_DISTANCE_VALUE") % SimFormatters.num(float(geo.get("distance_km", 0.0)), 0))
	else:
		_fact("BUILD_PLOT_DISTANCE", SimFormatters.num(float(p.get("real_distance_km", 0.0)), 1) + " km")
	if str(p.get("family", "")) == "land":
		_fact("BUILD_PLOT_SIZE", "%s × %s m" % [SimFormatters.num(float(p.get("width_m", 0.0)), 0), SimFormatters.num(float(p.get("depth_m", 0.0)), 0)])
		_fact("BUILD_PLOT_ROOM", tr("BUILD_PLOT_ROOM_VALUE") % [int(p.get("grow_out_lots", 0)), int(p.get("post_smolt_modules", 0))])
		_fact("BUILD_PLOT_ZONING", tr("BUILD_PLOT_PRE_ZONED") if bool(p.get("pre_zoned", false)) else (
			tr("BUILD_PLOT_STATE_ZONED") if bool(p.get("zoned", false)) else tr("BUILD_PLOT_ZONING_NEEDED")))
		_fact("BUILD_PLOT_SENSITIVITY", SimFormatters.num(float(p.get("nature_sensitivity", 0.0)), 1))
		_fact("BUILD_PLOT_GRID", tr("BUILD_PLOT_GRID_VALUE") % [SimFormatters.num(float(p.get("grid_capacity_mw", 0.0)), 0), int(p.get("grid_feeds", 0))])
		_fact("BUILD_PLOT_PUMPS", SimFormatters.num(float(p.get("pump_m3s_per_lot", 0.0)), 1) + " m³/s")
		if float(p.get("deep_intake_reach_m", 0.0)) > 0.0:
			_fact("BUILD_PLOT_INTAKE", SimFormatters.num(float(p.get("deep_intake_reach_m", 0.0)), 0) + " m")
		_fact("BUILD_PLOT_BLASTING", SimFormatters.format_nok(float(p.get("blast_cost_nok", 0.0))))
	else:
		_fact("BUILD_PLOT_EXPOSURE", tr("BUILD_PLOT_EXPOSURE_" + str(p.get("exposure", "")).to_upper()))
		_fact("BUILD_PLOT_DEPTH", SimFormatters.num(float(geo.get("depth_m", p.get("water_depth_m", 0.0))), 0) + " m")
		_fact("BUILD_PLOT_SEA", tr("BUILD_PLOT_SEA_VALUE") % [SimFormatters.num(float(p.get("winter_mean_hs_m", 0.0)), 1),
			SimFormatters.num(float(p.get("summer_mean_hs_m", 0.0)), 1)])
		_fact("BUILD_PLOT_HS100", SimFormatters.num(float(p.get("hs100_m", 0.0)), 1) + " m")
		var lo := float(p.get("lice_external_min", 0.0))
		var hi := float(p.get("lice_external_max", 0.0))
		_fact("BUILD_PLOT_LICE", ("×" + SimFormatters.num(lo, 2)) if is_equal_approx(lo, hi) else "×%s – ×%s" % [SimFormatters.num(lo, 2), SimFormatters.num(hi, 2)])


## Geo B: a plot's real open-sea binding (SimBridge.get_geo_plot_binding:
## bound, area, stage, po, depth_m, distance_km …; {} without a bridge).
func geo_binding(plot_id: String) -> Dictionary:
	var sim := get_node_or_null("/root/SimBridge")
	return sim.get_geo_plot_binding(plot_id) if sim != null and plot_id != "" and sim.has_method("get_geo_plot_binding") else {}


## The note under a bound plot's facts.
func geo_note() -> Label:
	return _geo_note


## The plot from above with the build ghosted on it, in the surface's colours.
class GhostView extends Control:
	## Ghosted shapes in plot metres: [{"kind": "tank"|"box"|"ring", "pos": Vector2, "size": Vector2}], plus the route.
	var shapes: Array = []
	var route: PackedVector2Array = []
	var plot_size := Vector2.ZERO
	var plot_id := ""
	var offshore := false

	func _ready() -> void:
		mouse_filter = Control.MOUSE_FILTER_IGNORE
		resized.connect(queue_redraw)

	func configure(plot: Dictionary, variant_id: String, option: String) -> void:
		shapes = []
		route = PackedVector2Array()
		plot_id = str(plot.get("id", ""))
		offshore = str(plot.get("family", "")) == "hav"
		if plot.is_empty():
			plot_size = Vector2.ZERO
			queue_redraw()
			return
		var ghost := FarmGhostLayout.shapes_for(plot_id, variant_id, option, Vector2(float(plot.get("x", 0.0)), float(plot.get("z", 0.0))),
			float(plot.get("water_depth_m", 0.0)))
		shapes = ghost.get("shapes", [])
		route = ghost.get("route", PackedVector2Array())
		plot_size = Vector2(float(plot.get("width_m", 0.0)), float(plot.get("depth_m", 0.0)))
		if offshore or plot_size == Vector2.ZERO:
			plot_size = ghost.get("extent", Vector2(400, 400))
		queue_redraw()

	func _draw() -> void:
		if plot_size == Vector2.ZERO or size.x < 20.0:
			return
		var ink := LakskongenTheme.palette(self, &"text")
		var water := LakskongenTheme.palette(self, &"info")
		var accent := LakskongenTheme.palette(self, &"accent")
		var line := LakskongenTheme.palette(self, &"hairline")
		var s := minf((size.x - 16.0) / plot_size.x, (size.y - 16.0) / plot_size.y)
		var centre := size * 0.5
		var to_px := func(p: Vector2) -> Vector2: return centre + p * s
		if offshore:
			draw_rect(Rect2(Vector2.ZERO, size), Color(water, 0.12), true)
		else:
			draw_rect(Rect2(centre - plot_size * s * 0.5, plot_size * s), Color(line, 0.35), true)
			draw_rect(Rect2(centre - plot_size * s * 0.5, plot_size * s), Color(ink, 0.6), false, 1.5)
		for sh in shapes:
			var pos: Vector2 = to_px.call(sh["pos"])
			var sz: Vector2 = (sh["size"] as Vector2) * s
			match str(sh["kind"]):
				"tank":
					draw_circle(pos, maxf(1.5, sz.x * 0.5), Color(water, 0.35))
					draw_arc(pos, maxf(1.5, sz.x * 0.5), 0.0, TAU, 24, Color(ink, 0.7), 1.0, true)
				"box":
					draw_rect(Rect2(pos - sz * 0.5, sz), Color(ink, 0.25), true)
				"ring":
					draw_arc(pos, maxf(2.0, sz.x * 0.5), 0.0, TAU, 48, accent, 3.0, true)
				"mooring":
					var r := maxf(4.0, sz.x * 0.5)
					var n := 24
					for i in n:
						if i % 2 == 0:
							draw_arc(pos, r, TAU * i / n, TAU * (i + 1) / n, 4, Color(ink, 0.6), 1.0, true)
				"hull":
					draw_rect(Rect2(pos - sz * 0.5, sz), Color(accent, 0.6), true)
		if route.size() >= 2:
			var pts := PackedVector2Array()
			for p in route:
				pts.append(to_px.call(p))
			draw_polyline(pts, accent, 2.0, true)
