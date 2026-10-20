class_name RivalPresenceList
extends VBoxContainer
## Who farms the area (geo stages 2-3 plan Track D): the five owners with the
## most salmonid localities in a production area, in the engine's order
## (get_rival_summary: sites, then MTB, then id), each with its colour band
## (a major's 3D colour, CompetitorFarmVisualManager.band_color_for, or slate
## for an independent), its sites and their MTB. Names are the game's parody
## owners (D17).

var _header: Label
var _rows: VBoxContainer
var _empty: Label
var _owners: Array = []


func _ready() -> void:
	add_theme_constant_override("separation", LakskongenTheme.SPACE_XS)
	_header = Label.new()
	_header.name = "Header"
	_header.theme_type_variation = &"Overline"
	_header.text = tr("MAP_REGION_RIVALS_HEADER")
	add_child(_header)
	_rows = VBoxContainer.new()
	_rows.name = "Rows"
	_rows.add_theme_constant_override("separation", 2)
	add_child(_rows)
	_empty = Label.new()
	_empty.name = "Empty"
	_empty.theme_type_variation = &"BodySmallMuted"
	_empty.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_empty.text = tr("MAP_REGION_RIVALS_NONE")
	add_child(_empty)


## Show an area's summary (GeoRegionBridge.rival_summary).
func show_summary(summary: Dictionary) -> void:
	_owners = summary.get("top_owners", [])
	for c in _rows.get_children():
		_rows.remove_child(c)
		c.queue_free()
	for o: Dictionary in _owners:
		_rows.add_child(_row(o))
	_empty.visible = _owners.is_empty()


## The owner ids in the order shown.
func owner_ids() -> PackedStringArray:
	var out := PackedStringArray()
	for o: Dictionary in _owners:
		out.append(String(o.get("id", "")))
	return out


## The row of an owner id, or null.
func owner_row(owner_id: String) -> Control:
	return _rows.get_node_or_null("Owner_" + owner_id.validate_node_name()) as Control


func _row(o: Dictionary) -> Control:
	var id := String(o.get("id", ""))
	var major := bool(o.get("is_major", not id.begins_with("ind_")))
	var row := HBoxContainer.new()
	row.name = "Owner_" + id.validate_node_name()
	row.add_theme_constant_override("separation", LakskongenTheme.SPACE_SM)
	row.tooltip_text = tr("MAP_REGION_RIVAL_ROW_TIP") % [String(o.get("name", "")),
		tr("MAP_SITE_BAND_MAJOR") if major else tr("MAP_SITE_BAND_INDEPENDENT")]
	var swatch := ColorRect.new()
	swatch.name = "Band"
	swatch.color = CompetitorFarmVisualManager.band_color_for(id) if major else GeoRegionView.INDEPENDENT
	swatch.custom_minimum_size = Vector2(10, 10)
	swatch.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	swatch.mouse_filter = Control.MOUSE_FILTER_IGNORE
	row.add_child(swatch)
	var name_l := Label.new()
	name_l.name = "Name"
	name_l.text = String(o.get("name", ""))
	name_l.theme_type_variation = &"BodySmall"
	name_l.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	name_l.clip_text = true
	name_l.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	row.add_child(name_l)
	var figures := Label.new()
	figures.name = "Figures"
	figures.text = tr("MAP_REGION_RIVAL_FIGURES") % [int(o.get("sites", 0)),
		SimFormatters.integer(int(round(float(o.get("mtb_t", 0.0)))))]
	figures.theme_type_variation = &"BodySmallMuted"
	row.add_child(figures)
	return row
