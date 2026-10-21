class_name GeoSiteCard
extends Control
## The map's site level (geo stages 2-3 plan Track D): a 2D card for one
## place, over a 4x crop of the area's base map around it (the heat's 500 m
## cells, the registered site and mooring polygons, a 2.5 km ring for the
## distance to other farms and the 5 km rings of the river mouths nearby).
##
## - A candidate: its 'why here' as the engine reads it (score, the binding
##   term, the components and what each costs, depth, distances, ocean
##   values) and an honest note that the score is a siting model. Its action
##   is 'Bygg her' when the build is allowed, 'Søk vekstlisens' when the zone
##   needs a growth licence (the subtitle names the zone), 'Vis i 3D' once it
##   is yours; any other refusal is a disabled button saying why.
## - A company site: where it lies, and 'Vis i 3D'.
## - A rival locality: owner, kind, stage and MTB, no action. Its subtitle is
##   the locality's number, never the registry's name for it (D17: some
##   localities are named after the company that holds them).
## - A Havbruk til havs hexagon: the hav_1 one offers 'Bygg her (Hav)'; any
##   other says it is not offered from your area.
## Every number comes from the row the region level read through the map's
## adapter (GeoBridgeAdapter); the rings' radii from suitability_rules.json,
## the rules the scores used.

signal build_requested(zone_id: String, candidate_id: String)
signal licence_requested(zone_id: String)
signal view_3d_requested(site_id: String)
signal hav_requested
signal back_requested

const ZOOM := 4.0
const CARD_WIDTH := 380.0
const CARD_MARGIN := 12.0
const LICENCE_REASON := "growth_licence_required"
const TERMS: Array[String] = ["depth", "exchange", "farm_distance", "temperature", "current", "waves", "oxygen", "river"]

var _kind := ""
var _row: Dictionary = {}
var _view: GeoRegionView = null
var _action := ""

var _crop: _CropMap
var _card: PanelContainer
var _overline: Label
var _title: Label
var _subtitle: Label
var _scroll: ScrollContainer
var _facts: VBoxContainer
var _note: Label
var _rings: Label
var _back_btn: Button
var _action_btn: Button


func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_STOP
	clip_contents = true
	_crop = _CropMap.new()
	_crop.name = "Crop"
	_crop.card = self
	_crop.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(_crop)
	_crop.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_build_card()
	resized.connect(_fit)
	_fit()


## The kind shown: "candidate", "site", "rival", "offshore" or "".
func kind() -> String:
	return _kind


func row() -> Dictionary:
	return _row


## What the action button does: "build", "licence", "view_3d", "hav" or "".
func action() -> String:
	return _action


func action_button() -> Button:
	return _action_btn


func back_button() -> Button:
	return _back_btn


func title_text() -> String:
	return _title.text


func subtitle_text() -> String:
	return _subtitle.text


## The fact rows shown: {name: value text}.
func facts() -> Dictionary:
	var out := {}
	for row_node in _facts.get_children():
		var v := row_node.get_node_or_null("Value") as Label
		if v != null:
			out[String(row_node.name).trim_prefix("Fact_")] = v.text
	return out


## The crop's centre in base-map pixels.
func centre_px() -> Vector2:
	return _crop.centre


func crop() -> Control:
	return _crop


## Show a candidate row (GeoBridgeAdapter.sea_candidates) of the view's area.
func show_candidate(row: Dictionary, view: GeoRegionView) -> void:
	_begin("candidate", row, view)
	var state := view.state_of(row)
	_overline.text = tr("MAP_SITE_OVERLINE_CANDIDATE") % [int(row.get("rank", 0)), view.candidates().size()]
	_title.text = String(row.get("label", ""))
	_subtitle.text = "%s · %s" % [_area_name(view.area()), tr(GeoRegionView.STATE_KEYS[state])]
	_section("why", "MAP_SITE_SECTION_WHY")
	_fact("score", "MAP_SITE_FACT_SCORE", SimFormatters.num(float(row.get("score", 0.0)), 3),
		float(row.get("score", 0.0)))
	_fact("binding", "MAP_SITE_FACT_BINDING", tr("MAP_SITE_TERM_" + String(row.get("binding", "none")).to_upper()))
	var components: Dictionary = row.get("components", {})
	var costs: Dictionary = row.get("component_costs", {})
	for term: String in TERMS:
		if not components.has(term) and not costs.has(term):
			continue
		var value := float(components.get(term, 1.0))
		_fact("term_" + term, "MAP_SITE_TERM_" + term.to_upper(), term_text(value, float(costs.get(term, 0.0))), value)
	_section("place", "MAP_SITE_SECTION_PLACE")
	_fact("depth", "MAP_SITE_FACT_DEPTH", tr("MAP_SITE_UNIT_M") % SimFormatters.integer(int(row.get("depth_m", 0))))
	_fact("farm", "MAP_SITE_FACT_NEAREST_FARM", tr("MAP_SITE_UNIT_KM") % SimFormatters.num(float(row.get("nearest_farm_km", 0.0)), 1))
	_fact("river", "MAP_SITE_FACT_NEAREST_RIVER", tr("MAP_SITE_UNIT_KM") % SimFormatters.num(float(row.get("nearest_river_km", 0.0)), 1))
	_fact("openness", "MAP_SITE_FACT_OPENNESS", SimFormatters.percent(float(row.get("openness", 0.0))))
	var ocean: Dictionary = row.get("ocean", {})
	if not ocean.is_empty():
		_fact("current", "MAP_SITE_FACT_CURRENT", tr("MAP_SITE_UNIT_MS") % SimFormatters.num(float(ocean.get("current_speed_ms", 0.0)), 2))
		_fact("waves", "MAP_SITE_FACT_WAVES", tr("MAP_SITE_UNIT_M_DEC") % SimFormatters.num(float(ocean.get("hs_m", 0.0)), 1))
		_fact("oxygen", "MAP_SITE_FACT_OXYGEN", tr("MAP_SITE_UNIT_O2") % SimFormatters.integer(int(round(float(ocean.get("o2_mmol_m3", 0.0))))))
	_fact("zone", "MAP_SITE_FACT_ZONE", SimLabels.zone(String(row.get("build_zone_id", ""))))
	_note.text = tr("MAP_SITE_HONEST_NOTE")
	_note.get_parent().visible = true
	var zone := String(row.get("build_zone_id", ""))
	if state == "yours":
		_set_action("view_3d", "MAP_SITE_VIEW_3D", true, "MAP_SITE_VIEW_3D_TIP")
	elif bool(row.get("can_build", false)):
		_set_action("build", "MAP_SITE_BUILD_HERE", true, "MAP_SITE_BUILD_HERE_TIP")
	elif String(row.get("build_reason_key", "")) == LICENCE_REASON:
		_subtitle.text = tr("MAP_SITE_LICENCE_ZONE") % SimLabels.zone(zone)
		_set_action("licence", "MAP_SITE_APPLY_LICENCE", true, "MAP_SITE_APPLY_LICENCE_TIP")
	else:
		var why := String(row.get("build_label_key", ""))
		_set_action("build", "MAP_SITE_BUILD_HERE", false, why if why != "" else "MAP_SITE_BUILD_REFUSED_TIP")
	_end()


## Show one of the company's sites (GeoBridgeAdapter.player_site_locations).
func show_player_site(row: Dictionary, view: GeoRegionView) -> void:
	_begin("site", row, view)
	_overline.text = tr("MAP_SITE_OVERLINE_YOURS")
	_title.text = String(row.get("site_label", ""))
	_subtitle.text = "%s · %s" % [String(row.get("label", "")), _area_name(view.area())]
	var exact := String(row.get("source", "")) == "candidate"
	_fact("where", "MAP_SITE_FACT_WHERE", tr("MAP_SITE_WHERE_CANDIDATE") if exact else tr("MAP_REGION_SITE_APPROX"))
	if exact:
		_fact("candidate", "MAP_SITE_FACT_CANDIDATE", String(row.get("candidate_id", "")))
	_note.get_parent().visible = false
	_set_action("view_3d", "MAP_SITE_VIEW_3D", true, "MAP_SITE_VIEW_3D_TIP")
	_end()


## Show a rival locality (an icon of the region's rival layer).
func show_rival(icon: Dictionary, view: GeoRegionView) -> void:
	_begin("rival", icon, view)
	_overline.text = tr("MAP_SITE_OVERLINE_RIVAL")
	_title.text = String(icon.get("owner_name", ""))
	_subtitle.text = tr("MAP_SITE_LOCALITY_NUMBER") % int(icon.get("id", 0))
	_fact("owner", "MAP_SITE_FACT_OWNER", String(icon.get("owner_name", "")))
	_fact("kind", "MAP_SITE_FACT_KIND", tr(GeoRegionView.kind_key(String(icon.get("kind", "")))))
	if icon.has("stage"):
		_fact("stage", "MAP_SITE_FACT_STAGE", tr("MAP_SITE_LOCALITY_STAGE_" + String(icon["stage"]).to_upper()))
	_fact("mtb", "MAP_SITE_FACT_MTB", tr("MAP_SITE_UNIT_T") % SimFormatters.integer(int(round(float(icon.get("mtb_t", 0.0))))))
	_fact("band", "MAP_SITE_FACT_BAND", tr("MAP_SITE_BAND_MAJOR") if bool(icon.get("is_major", false)) else tr("MAP_SITE_BAND_INDEPENDENT"))
	_note.get_parent().visible = false
	_set_action("", "", false, "")
	_end()


## Show a Havbruk til havs candidate (a hexagon).
func show_offshore(row: Dictionary, view: GeoRegionView) -> void:
	_begin("offshore", row, view)
	var bound := String(row.get("bound_plot_id", "")) != ""
	_overline.text = tr("MAP_SITE_OVERLINE_OFFSHORE")
	_title.text = String(row.get("area", ""))
	_subtitle.text = tr("MAP_SITE_STAGE_" + String(row.get("stage", "")).to_upper())
	_fact("score", "MAP_SITE_FACT_SCORE", SimFormatters.num(float(row.get("score", 0.0)), 3), float(row.get("score", 0.0)))
	_fact("depth", "MAP_SITE_FACT_DEPTH", tr("MAP_SITE_UNIT_M") % SimFormatters.integer(int(row.get("depth_m", 0))))
	_fact("stage_factor", "MAP_SITE_FACT_STAGE_FACTOR", SimFormatters.num(float(row.get("stage_factor", 1.0)), 2))
	_fact("near", "MAP_SITE_FACT_NEAR", String(row.get("label", row.get("near", ""))))
	_note.text = tr("MAP_REGION_HEX_BOUND") if bound else tr("MAP_REGION_HEX_NOT_OFFERED")
	_note.get_parent().visible = true
	if bound:
		_set_action("hav", "MAP_SITE_BUILD_HAV", true, "MAP_SITE_BUILD_HAV_TIP")
	else:
		_set_action("", "", false, "")
	_end()


func _begin(kind: String, row: Dictionary, view: GeoRegionView) -> void:
	_kind = kind
	_row = row
	_view = view
	# A rival's crop has no farm ring (the distance is to its own site): the
	# key names only the river rings it draws.
	_rings.text = tr("MAP_SITE_RINGS_LEGEND_RIVERS") if kind == "rival" else tr("MAP_SITE_RINGS_LEGEND")
	for c in _facts.get_children():
		_facts.remove_child(c)
		c.queue_free()
	var at := GeoRegionMath.map_px(view.area(), float(row.get("x", 0.0)), float(row.get("y", 0.0)))
	if kind == "rival":
		at = row.get("px", at)
	_crop.show_at(view, at)


func _end() -> void:
	_scroll.scroll_vertical = 0
	_fit()


func _set_action(action: String, key: String, enabled: bool, tip_key: String) -> void:
	_action = action
	_action_btn.visible = action != ""
	_action_btn.text = tr(key) if key != "" else ""
	_action_btn.disabled = not enabled
	_action_btn.tooltip_text = tr(tip_key) if tip_key != "" else ""


func _on_action() -> void:
	match _action:
		"build":
			build_requested.emit(String(_row.get("build_zone_id", "")), String(_row.get("id", "")))
		"licence":
			licence_requested.emit(String(_row.get("build_zone_id", "")))
		"view_3d":
			var site := String(_row.get("site_id", _row.get("taken_by_site_id", "")))
			view_3d_requested.emit(site)
		"hav":
			hav_requested.emit()


func _area_name(po: int) -> String:
	for f: Dictionary in GeoData.production_areas("national"):
		if int(f["properties"]["id"]) == po:
			return tr("MAP_REGION_AREA_NAME") % [po, String(f["properties"]["name"])]
	return ""


# ── Layout ────────────────────────────────────────────────────────────────────

func _build_card() -> void:
	_card = PanelContainer.new()
	_card.name = "Dialog"
	_card.theme_type_variation = &"CardPadded"
	add_child(_card)
	_card.owner = self
	_card.unique_name_in_owner = true
	var box := VBoxContainer.new()
	box.name = "CardBox"
	box.add_theme_constant_override("separation", LakskongenTheme.SPACE_SM)
	_card.add_child(box)
	_overline = _label("Overline", &"Overline")
	box.add_child(_overline)
	_title = _label("Title", &"Heading")
	_title.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	box.add_child(_title)
	_subtitle = _label("Subtitle", &"BodySmallMuted")
	_subtitle.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	box.add_child(_subtitle)
	_scroll = ScrollContainer.new()
	_scroll.name = "StepScroll"
	_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	box.add_child(_scroll)
	_scroll.owner = self
	_scroll.unique_name_in_owner = true
	var inner := VBoxContainer.new()
	inner.name = "ScrollBox"
	inner.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	inner.add_theme_constant_override("separation", LakskongenTheme.SPACE_SM)
	_scroll.add_child(inner)
	_facts = VBoxContainer.new()
	_facts.name = "Facts"
	_facts.add_theme_constant_override("separation", LakskongenTheme.SPACE_XS)
	inner.add_child(_facts)
	var callout := PanelContainer.new()
	callout.name = "NoteCallout"
	callout.theme_type_variation = &"CalloutMuted"
	_note = _label("Note", &"BodySmall")
	_note.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	callout.add_child(_note)
	inner.add_child(callout)
	_rings = _label("RingsLegend", &"SmallMuted")
	_rings.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_rings.text = tr("MAP_SITE_RINGS_LEGEND")
	inner.add_child(_rings)
	var buttons := HBoxContainer.new()
	buttons.name = "Buttons"
	buttons.add_theme_constant_override("separation", LakskongenTheme.SPACE_SM)
	box.add_child(buttons)
	_back_btn = Button.new()
	_back_btn.name = "BackBtn"
	_back_btn.text = tr("MAP_SITE_BACK")
	_back_btn.tooltip_text = tr("MAP_SITE_BACK_TIP")
	_back_btn.theme_type_variation = &"SecondaryButton"
	_back_btn.pressed.connect(func() -> void: back_requested.emit())
	buttons.add_child(_back_btn)
	_action_btn = Button.new()
	_action_btn.name = "ActionBtn"
	_action_btn.theme_type_variation = &"PrimaryButton"
	_action_btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_action_btn.pressed.connect(_on_action)
	buttons.add_child(_action_btn)


## The farm-distance ring, m: the rules' hard minimum between farms
## (hard_constraints.farm_too_close.min_km); 0 when the rules lack it.
static func farm_ring_m() -> float:
	var hard: Variant = GeoData.rules().get("hard_constraints", {})
	var farm: Variant = hard.get("farm_too_close", {}) if hard is Dictionary else {}
	return float(farm.get("min_km", 0.0)) * 1000.0 if farm is Dictionary else 0.0


## The river rings, m: the radius over which a river mouth lowers the score
## (river_factor.radius_m); 0 when the rules lack it.
static func river_ring_m() -> float:
	var river: Variant = GeoData.rules().get("river_factor", {})
	return float(river.get("radius_m", 0.0)) if river is Dictionary else 0.0


## A score term's value as the card shows it: the component (0-1) and, when
## it costs the score anything, what it costs ("0,90 (−0,014)").
static func term_text(value: float, cost: float) -> String:
	if cost < 0.0005:
		return SimFormatters.num(value, 2)
	return TranslationServer.translate("MAP_SITE_TERM_VALUE") % [SimFormatters.num(value, 2), SimFormatters.num(cost, 3)]


## A small heading between the card's fact groups.
func _section(id: String, key: String) -> void:
	var l := _label("Section_" + id, &"Overline")
	l.text = tr(key)
	_facts.add_child(l)


## A fact row: the term, its value, and a bar when a 0..1 measure is given.
func _fact(id: String, key: String, value: String, bar: float = -1.0) -> void:
	var row := HBoxContainer.new()
	row.name = "Fact_" + id
	row.add_theme_constant_override("separation", LakskongenTheme.SPACE_SM)
	var name_l := _label("Name", &"BodySmallMuted")
	name_l.text = tr(key)
	name_l.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	name_l.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	row.add_child(name_l)
	if bar >= 0.0:
		var b := _Bar.new()
		b.name = "Bar"
		b.value = bar
		b.custom_minimum_size = Vector2(56, 10)
		b.size_flags_vertical = Control.SIZE_SHRINK_CENTER
		row.add_child(b)
	var v := _label("Value", &"BodySmallStrong")
	v.text = value
	v.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	row.add_child(v)
	_facts.add_child(row)


func _label(node_name: String, variation: StringName) -> Label:
	var l := Label.new()
	l.name = node_name
	l.theme_type_variation = variation
	return l


## The card docks to the right edge at full height (anchors keep its height
## in step with the map area); its width follows the area's.
func _fit() -> void:
	if _card == null:
		return
	var w := minf(CARD_WIDTH, maxf(size.x * 0.5, 260.0))
	_card.anchor_left = 1.0
	_card.anchor_right = 1.0
	_card.anchor_top = 0.0
	_card.anchor_bottom = 1.0
	_card.offset_left = -w - CARD_MARGIN
	_card.offset_right = -CARD_MARGIN
	_card.offset_top = CARD_MARGIN
	_card.offset_bottom = -CARD_MARGIN
	_crop.free_width = maxf(size.x - w - CARD_MARGIN * 2.0, 0.0)
	_crop.sync()


## A 0..1 bar in the ramp's colour.
class _Bar extends Control:
	var value := 0.0

	func _draw() -> void:
		var r := Rect2(Vector2.ZERO, size)
		draw_rect(r, LakskongenTheme.palette(self, &"sunken"))
		draw_rect(Rect2(Vector2.ZERO, Vector2(size.x * clampf(value, 0.0, 1.0), size.y)), GeoRegionMath.ramp(value))
		draw_rect(r, LakskongenTheme.palette(self, &"hairline"), false, 1.0)


## The 4x crop of the area's base map around the place, in three layers:
## this node draws the open sea and the base map; CropHeat is the heat, one
## rect for the whole base map through map_suitability.gdshader (the region
## view's shader and raw grid, nearest), clipped to the crop, so a redraw
## records no cell; CropMarks draws the site and mooring polygons, the
## rings, the rivals and the place.
class _CropMap extends Control:
	## The heat's opacity in the crop, over the region view's: scored cells
	## and the hatch read a little quieter under the card's marks.
	const HEAT_OPACITY := 0.75
	const HATCH_OPACITY := 0.5

	var card: GeoSiteCard = null
	var view: GeoRegionView = null
	var centre := Vector2.ZERO
	var free_width := 0.0
	var redraw_count := 0
	var heat: TextureRect = null
	var marks: _CropMarks = null
	var _heat_po := 0

	func _ready() -> void:
		clip_contents = true
		heat = TextureRect.new()
		heat.name = "CropHeat"
		heat.mouse_filter = Control.MOUSE_FILTER_IGNORE
		heat.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		heat.stretch_mode = TextureRect.STRETCH_SCALE
		var mat := ShaderMaterial.new()
		mat.shader = GeoRegionView.SuitabilityShader
		mat.set_shader_parameter("show_heat", HEAT_OPACITY)
		mat.set_shader_parameter("show_hatch", HATCH_OPACITY)
		heat.material = mat
		add_child(heat)
		marks = _CropMarks.new()
		marks.name = "CropMarks"
		marks.crop = self
		marks.mouse_filter = Control.MOUSE_FILTER_IGNORE
		add_child(marks)
		marks.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
		resized.connect(sync)

	func show_at(region: GeoRegionView, at: Vector2) -> void:
		view = region
		centre = at
		sync()

	## Lay the heat rect over the base map's place in the crop, and redraw.
	func sync() -> void:
		if heat == null:
			return
		var po := view.area() if view != null else 0
		heat.visible = po > 0
		if po > 0:
			if po != _heat_po:
				_heat_po = po
				heat.texture = GeoData.basemap_texture(GeoRegionMath.basemap_key(po))
				var mat := heat.material as ShaderMaterial
				var uniforms := GeoRegionMath.shader_uniforms(po)
				for k: String in uniforms:
					mat.set_shader_parameter(k, uniforms[k])
				mat.set_shader_parameter("suit_tex", GeoRegionView.heat_texture(po))
			var map_size: Vector2 = GeoRegionMath.transform(po)["map_size"]
			heat.position = to_crop(Vector2.ZERO)
			heat.size = map_size * scale_px()
		queue_redraw()
		marks.queue_redraw()

	## Screen pixels per base-map pixel: 4x the region view's.
	func scale_px() -> float:
		if view == null:
			return 1.0
		var t := GeoRegionMath.transform(view.area())
		var region_scale := view.map_rect().size.x / float((t.get("map_size", Vector2.ONE) as Vector2).x)
		return GeoSiteCard.ZOOM * maxf(region_scale, 0.05)

	## Base-map pixel -> crop position.
	func to_crop(px: Vector2) -> Vector2:
		var w := free_width if free_width > 0.0 else size.x
		return Vector2(w * 0.5, size.y * 0.5) + (px - centre) * scale_px()

	## The part of the base map the crop shows, in base-map pixels.
	func source_rect() -> Rect2:
		var t := GeoRegionMath.transform(view.area())
		var s := scale_px()
		var tl := centre - to_crop(centre) / s
		return Rect2(tl, size / s).intersection(Rect2(Vector2.ZERO, t["map_size"]))

	func _draw() -> void:
		redraw_count += 1
		# Beyond the area's base map lies open sea (an offshore site often does).
		draw_rect(Rect2(Vector2.ZERO, size), GeoRegionIcons.OPEN_SEA)
		if view == null or view.area() == 0:
			return
		var src := source_rect()
		var tex := GeoData.basemap_texture(GeoRegionMath.basemap_key(view.area()))
		if tex != null and src.has_area():
			draw_texture_rect_region(tex, Rect2(to_crop(src.position), src.size * scale_px()), src)


## The crop's marks over its heat: the registered site and mooring polygons,
## the farm ring, the river mouths' rings, the rivals and the place itself.
class _CropMarks extends Control:
	var crop: _CropMap = null

	func _draw() -> void:
		var view := crop.view
		var card := crop.card
		if view == null or view.area() == 0:
			return
		var po := view.area()
		var t := GeoRegionMath.transform(po)
		var s := crop.scale_px()
		var src := crop.source_rect()
		_draw_polygons(po, src)
		_draw_rings(po, t, src, s)
		for icon: Dictionary in (view.rival_layer() as Object).get("icons"):
			var at := crop.to_crop(icon["px"])
			if Rect2(Vector2.ZERO, size).has_point(at):
				draw_circle(at, 7.0, GeoRegionIcons.HALO)
				draw_arc(at, 5.0, 0.0, TAU, 20, icon["color"], 2.5, true)
		var c := crop.to_crop(crop.centre)
		match card.kind():
			"candidate":
				GeoRegionIcons.draw_star(self, c, 15.0, view.state_of(card.row()))
			"offshore":
				GeoRegionIcons.draw_hexagon(self, c, 14.0, String(card.row().get("bound_plot_id", "")) != "")
			"site":
				draw_circle(c, 11.0, GeoRegionIcons.HALO)
				draw_circle(c, 7.0, GeoRegionIcons.SALMON)
				draw_arc(c, 9.0, 0.0, TAU, 32, GeoRegionIcons.SNOW, 2.0, true)
			"rival":
				draw_circle(c, 11.0, GeoRegionIcons.HALO)
				draw_arc(c, 8.0, 0.0, TAU, 32, card.row().get("color", GeoRegionIcons.INDEPENDENT), 3.0, true)

	func _draw_polygons(po: int, src: Rect2) -> void:
		var data := GeoRegionData.area(po)
		if data.is_empty():
			return
		for layer: String in ["locality_areas", "mooring_areas"]:
			var st: Dictionary = GeoRegionOverlay.STYLE[layer]
			for feature: Dictionary in data["layers"][layer]:
				for part: Dictionary in feature["parts"]:
					var ring: PackedVector2Array = part["ring"]
					if not src.grow(4.0).has_point(ring[0]):
						continue
					var tris: PackedInt32Array = part["tris"]
					if not tris.is_empty() and (st["fill"] as Color).a > 0.0:
						var fill := PackedVector2Array()
						for p: Vector2 in part.get("fill_points", ring):
							fill.append(crop.to_crop(p))
						var cols := PackedColorArray()
						cols.resize(fill.size())
						cols.fill(st["fill"])
						RenderingServer.canvas_item_add_triangle_array(get_canvas_item(), tris, fill, cols)
					var pts := PackedVector2Array()
					for p in ring:
						pts.append(crop.to_crop(p))
					pts.append(pts[0])
					draw_polyline(pts, st["line"], 1.5, true)

	func _draw_rings(po: int, t: Dictionary, src: Rect2, s: float) -> void:
		var res: float = t["map_res"]
		if crop.card.kind() != "rival":
			var farm_m := GeoSiteCard.farm_ring_m()
			if farm_m > 0.0:
				draw_arc(crop.to_crop(crop.centre), farm_m / res * s, 0.0, TAU, 96, Color(GeoRegionIcons.SNOW, 0.85), 1.5, true)
		var data := GeoRegionData.area(po)
		if data.is_empty():
			return
		var river_m := GeoSiteCard.river_ring_m()
		for p: Dictionary in data["layers"]["constraint_points"]:
			if String(p["kind"]) == "coral_reef" or river_m <= 0.0 or not src.grow(river_m / res).has_point(p["px"]):
				continue
			var c := crop.to_crop(p["px"])
			var r := river_m / res * s
			var col: Color = GeoRegionOverlay.STYLE.get(String(p["kind"]), GeoRegionOverlay.STYLE["anadromous_river"])["line"]
			for i in 48:
				var a := TAU * i / 48.0
				draw_arc(c, r, a, a + TAU / 48.0 * 0.55, 3, col, 1.5, true)
			draw_circle(c, 3.0, col)
