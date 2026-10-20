class_name MapBreadcrumbs
extends PanelContainer
## Where the map is (geo stages 2-3 plan Track D): 'Norge › PO3 Karmøy til
## Sotra › Hille' with a back button. Each crumb above the current level is a
## button that goes back to it; the current level is plain text.

## The player asked for a level: "national" or "region".
signal level_requested(level: String)
signal back_requested

const SEPARATOR := "›"

var _row: HBoxContainer
var _back: Button
var _crumbs: Array[Control] = []


func _ready() -> void:
	theme_type_variation = &"CardCompact"
	mouse_filter = Control.MOUSE_FILTER_STOP
	_row = HBoxContainer.new()
	_row.name = "Row"
	_row.add_theme_constant_override("separation", LakskongenTheme.SPACE_XS)
	add_child(_row)
	_back = Button.new()
	_back.name = "BackBtn"
	_back.text = tr("MAP_REGION_BACK")
	_back.tooltip_text = tr("MAP_REGION_BACK_TIP")
	_back.theme_type_variation = &"SmallSecondaryButton"
	_back.pressed.connect(func() -> void: back_requested.emit())
	_row.add_child(_back)


## Show the path: area_label for the region level, place for the site level
## ("" when at region level).
func show_path(area_label: String, place: String) -> void:
	for c in _crumbs:
		_row.remove_child(c)
		c.queue_free()
	_crumbs = []
	_add_crumb("national", tr("MAP_REGION_CRUMB_NORWAY"), true)
	_add_separator()
	_add_crumb("region", area_label, place != "")
	if place != "":
		_add_separator()
		_add_crumb("site", place, false)


## The crumb for a level, or null ("national", "region", "site").
func crumb(level: String) -> Control:
	return _row.get_node_or_null("Crumb_" + level) as Control


func back_button() -> Button:
	return _back


## The crumbs' texts in order.
func path_text() -> PackedStringArray:
	var out := PackedStringArray()
	for c in _crumbs:
		if c.name.begins_with("Crumb_"):
			out.append(c.get("text"))
	return out


func _add_crumb(level: String, text: String, link: bool) -> void:
	var c: Control
	if link:
		var b := Button.new()
		b.text = text
		b.flat = false
		b.theme_type_variation = &"SmallGhostButton"
		b.tooltip_text = tr("MAP_REGION_CRUMB_TIP") % text
		b.pressed.connect(func() -> void: level_requested.emit(level))
		c = b
	else:
		var l := Label.new()
		l.text = text
		l.theme_type_variation = &"BodySmallStrong"
		c = l
	c.name = "Crumb_" + level
	c.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	_row.add_child(c)
	_crumbs.append(c)


func _add_separator() -> void:
	var l := Label.new()
	l.name = "Sep%d" % _crumbs.size()
	l.text = SEPARATOR
	l.theme_type_variation = &"BodySmallMuted"
	l.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	_row.add_child(l)
	_crumbs.append(l)
