class_name GeoAreaFacts
extends VBoxContainer
## The detail column's facts about one production area (geo stages 2-3
## plan, Track C), every value from the sim's geo reads, nothing invented:
## - the official traffic light (the source's, get_geo_production_areas),
##   a chip of its own beside the game's light, which the expansion section
##   shows (two lights, two labels, ruling OD5);
## - the sea area (km²);
## - the registered localities, by kind, and the summed MTB of the area's
##   producing sea grow-out sites (get_rival_summary);
## - the three owners with the most sites there (the game's parody rivals).

const LIGHT_TAGS := {"green": &"TagOk", "yellow": &"TagWarn", "red": &"TagCrit"}
const TOP_OWNERS := 3

var _area := 0


func _ready() -> void:
	name = "GeoAreaFacts"
	add_theme_constant_override("separation", LakskongenTheme.SPACE_SM)
	size_flags_horizontal = Control.SIZE_EXPAND_FILL


## The area shown (0: none).
func area() -> int:
	return _area


## Show an area: its row of get_geo_production_areas and its rival summary.
func show_area(area_id: int, geo_area: Dictionary, summary: Dictionary) -> void:
	_area = area_id
	for c in get_children():
		remove_child(c)
		c.queue_free()
	if area_id <= 0 or geo_area.is_empty():
		return
	add_child(_label("MAP_GEO_FACTS_HEADER", &"OverlineMuted", "FactsHeader"))
	var light := str(geo_area.get("official_traffic_light", ""))
	if LIGHT_TAGS.has(light):
		var chip := Label.new()
		chip.name = "OfficialLight"
		chip.theme_type_variation = LIGHT_TAGS[light]
		chip.text = tr("MAP_GEO_OFFICIAL_LIGHT") % tr("EXPANSION_LIGHT_" + light.to_upper())
		chip.tooltip_text = tr("MAP_GEO_OFFICIAL_LIGHT_TIP")
		chip.mouse_filter = Control.MOUSE_FILTER_STOP
		chip.size_flags_horizontal = Control.SIZE_SHRINK_BEGIN
		add_child(chip)
	var grid := GridContainer.new()
	grid.name = "FactsGrid"
	grid.columns = 2
	grid.add_theme_constant_override("h_separation", LakskongenTheme.SPACE_MD)
	grid.add_theme_constant_override("v_separation", LakskongenTheme.SPACE_XS + 2)
	add_child(grid)
	_fact(grid, "MAP_GEO_SEA_AREA", "SeaArea", tr("MAP_GEO_KM2") % SimFormatters.num(float(geo_area.get("sea_area_km2", 0.0))))
	var by_kind := GeoBridgeAdapter.sites_by_kind(summary)
	var by_class := PackedInt32Array([0, 0, 0, 0])
	var total := 0
	for kind: String in by_kind:
		var n := int(by_kind[kind])
		total += n
		by_class[GeoBridgeAdapter.display_class(kind)] += n
	_fact(grid, "MAP_GEO_LOCALITIES", "Localities", SimFormatters.integer(total))
	_fact(grid, "MAP_GEO_SEA_MTB", "SeaMtb", tr("MAP_GEO_TONNES") % SimFormatters.num(float(summary.get("sea_growout_mtb_t", 0.0))))
	var split := Label.new()
	split.name = "LocalitiesSplit"
	split.theme_type_variation = &"SmallMuted"
	split.text = GeoMapTips.split_text(Array(by_class))
	split.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	add_child(split)
	add_child(_label("MAP_GEO_TOP_OWNERS", &"OverlineMuted", "OwnersHeader"))
	var owners: Array = summary.get("top_owners", [])
	if owners.is_empty():
		add_child(_label("MAP_GEO_NO_OWNERS", &"SmallMuted", "NoOwners"))
	for i in mini(owners.size(), TOP_OWNERS):
		var o: Dictionary = owners[i]
		var row := HBoxContainer.new()
		row.name = "Owner_%d" % i
		row.add_theme_constant_override("separation", LakskongenTheme.SPACE_SM)
		var who := Label.new()
		who.name = "Name"
		who.theme_type_variation = &"BodySmallStrong" if bool(o.get("is_major", false)) else &"BodySmall"
		who.text = str(o.get("name", ""))
		who.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		who.clip_text = true
		row.add_child(who)
		var what := Label.new()
		what.name = "Sites"
		what.theme_type_variation = &"SmallMuted"
		what.text = GeoMapTips.owner_row(int(o.get("sites", 0)), float(o.get("mtb_t", 0.0)))
		row.add_child(what)
		add_child(row)
	var note := _label("MAP_GEO_SOURCE_NOTE", &"SmallMuted", "SourceNote")
	note.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	add_child(note)


func _fact(grid: GridContainer, key: String, node_name: String, value: String) -> void:
	var k := _label(key, &"SmallMuted", node_name + "Key")
	grid.add_child(k)
	var v := Label.new()
	v.name = node_name
	v.theme_type_variation = &"Figure"
	v.text = value
	grid.add_child(v)


func _label(key: String, variation: StringName, node_name: String) -> Label:
	var l := Label.new()
	l.name = node_name
	l.theme_type_variation = variation
	l.text = tr(key)
	return l
