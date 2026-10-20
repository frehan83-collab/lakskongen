class_name PermitTimeline
extends VBoxContainer

## The land build preview's permit timeline (roadmap F2-31,
## docs/FARM_TYPES_SPEC.md §6.2 step 4): every permit the pad needs, its
## status, its decision window drawn on one time axis (zoning 1-2 years;
## land, discharge, sea works and biosecurity in parallel), its refusal
## probability and the drivers that make it up, and the grid. Everything is
## the engine's land preview (SimBridge.preview_farm_build → "land":
## "permits", "package_refusal_probability", "design_power_mw",
## "grid_capacity_mw"); the timeline only lays it out.

## Status -> the tag's tone.
const STATUS_TAGS := {"approved": &"TagOk", "pre_zoned": &"TagOk", "pending": &"TagInfo", "refused": &"TagCrit", "needed": &"TagWarn"}


## Lay out `land` (the preview's "land" dictionary).
func show_permits(land: Dictionary) -> void:
	for child in get_children():
		remove_child(child)
		child.queue_free()
	add_theme_constant_override("separation", 6)
	var head := HBoxContainer.new()
	head.add_theme_constant_override("separation", 8)
	var title := Label.new()
	title.text = tr("BUILD_PERMITS_TITLE")
	title.theme_type_variation = &"OverlineMuted"
	title.uppercase = true
	title.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	head.add_child(title)
	var package := Label.new()
	package.name = "Package"
	package.text = tr("BUILD_PERMITS_PACKAGE") % SimFormatters.percent(float(land.get("package_refusal_probability", 0.0)), 1)
	package.theme_type_variation = &"BodySmallStrong"
	head.add_child(package)
	add_child(head)
	var permits: Array = land.get("permits", [])
	var longest := 1
	for p in permits:
		longest = maxi(longest, int(p.get("max_days", 0)))
	for p in permits:
		add_child(_row(p as Dictionary, longest))
	var design := float(land.get("design_power_mw", 0.0))
	var grid := float(land.get("grid_capacity_mw", 0.0))
	var power := Label.new()
	power.name = "Grid"
	power.text = tr("BUILD_PERMITS_GRID_OK" if design <= grid else "BUILD_PERMITS_GRID_SHORT") % [SimFormatters.num(design, 1), SimFormatters.num(grid, 0)]
	power.theme_type_variation = &"BodySmall" if design <= grid else &"BodySmallWarn"
	power.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	power.custom_minimum_size = Vector2(300, 0)
	add_child(power)
	var season := Label.new()
	season.name = "SeaWorksSeason"
	season.text = tr("BUILD_PERMITS_SEA_WORKS_SEASON")
	season.theme_type_variation = &"SmallMuted"
	season.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	season.custom_minimum_size = Vector2(300, 0)
	add_child(season)


## The row of permit `kind` ("Zoning", "LandAquaculture", …), or null.
func row(kind: String) -> Control:
	return get_node_or_null("Permit_" + kind) as Control


func _row(p: Dictionary, longest: int) -> Control:
	var kind := str(p.get("kind", ""))
	var status := str(p.get("status", ""))
	var col := VBoxContainer.new()
	col.name = "Permit_" + kind
	col.add_theme_constant_override("separation", 2)
	col.set_meta(&"refusal_probability", float(p.get("refusal_probability", 0.0)))
	var line := HBoxContainer.new()
	line.add_theme_constant_override("separation", 8)
	var name_label := Label.new()
	name_label.name = "Name"
	name_label.text = tr(str(p.get("label_key", "")))
	name_label.theme_type_variation = &"BodySmallStrong"
	name_label.custom_minimum_size = Vector2(190, 0)
	name_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	line.add_child(name_label)
	var tag := Label.new()
	tag.name = "Status"
	tag.text = tr(str(p.get("status_label_key", "")))
	tag.theme_type_variation = STATUS_TAGS.get(status, &"TagMuted")
	tag.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	line.add_child(tag)
	var window := WindowBar.new()
	window.name = "Window"
	window.configure(int(p.get("min_days", 0)), int(p.get("max_days", 0)), longest, status in ["approved", "pre_zoned"])
	window.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	window.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	line.add_child(window)
	var odds := Label.new()
	odds.name = "Odds"
	odds.text = _odds_text(p, status)
	odds.theme_type_variation = &"FigureSmall"
	odds.custom_minimum_size = Vector2(150, 0)
	odds.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	line.add_child(odds)
	col.add_child(line)
	if status in ["needed", "refused"]:
		var drivers := Label.new()
		drivers.name = "Drivers"
		var parts: Array = []
		for d in p.get("drivers", []):
			parts.append("%s %s" % [tr(str(d.get("label_key", ""))), SimFormatters.percent(float(d.get("effect", 0.0)), 1)])
		drivers.text = tr("BUILD_PERMITS_DRIVERS") % ", ".join(parts)
		drivers.theme_type_variation = &"SmallMuted"
		drivers.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		drivers.custom_minimum_size = Vector2(300, 0)
		col.add_child(drivers)
	return col


func _odds_text(p: Dictionary, status: String) -> String:
	match status:
		"approved", "pre_zoned":
			return tr("BUILD_PERMITS_DONE")
		"pending":
			return tr("BUILD_PERMITS_DECIDED_ON") % SimFormatters.format_day(int(p.get("decision_day", 0)))
	var months := func(d: int) -> String: return SimFormatters.num(float(d) * SimFormatters.MONTHS_PER_YEAR / SimFormatters.DAYS_PER_YEAR, 0)
	return tr("BUILD_PERMITS_WINDOW") % [months.call(int(p.get("min_days", 0))), months.call(int(p.get("max_days", 0))),
		SimFormatters.percent(float(p.get("refusal_probability", 0.0)), 1)]


## A decision window on the timeline's axis (0 to the slowest case's longest day).
class WindowBar extends Control:
	var min_days := 0
	var max_days := 0
	var longest := 1
	var done := false

	func configure(lo: int, hi: int, axis_days: int, finished: bool) -> void:
		min_days = lo
		max_days = hi
		longest = maxi(1, axis_days)
		done = finished
		custom_minimum_size = Vector2(120, 10)
		mouse_filter = Control.MOUSE_FILTER_IGNORE
		queue_redraw()

	func _draw() -> void:
		var track := LakskongenTheme.palette(self, &"hairline")
		var info := LakskongenTheme.palette(self, &"info")
		var ok := LakskongenTheme.palette(self, &"ok")
		draw_rect(Rect2(Vector2(0, size.y * 0.5 - 1.0), Vector2(size.x, 2.0)), Color(track, 0.8), true)
		if done:
			draw_circle(Vector2(4.0, size.y * 0.5), 4.0, ok)
			return
		var x0 := size.x * float(min_days) / longest
		var x1 := size.x * float(max_days) / longest
		draw_rect(Rect2(Vector2(x0, 1.0), Vector2(maxf(3.0, x1 - x0), size.y - 2.0)), info, true)
