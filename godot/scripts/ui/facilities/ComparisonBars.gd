class_name ComparisonBars
extends GridContainer

## The farm-type comparison (roadmap F2-31, docs/FARM_TYPES_SPEC.md §6.2): the
## catalogue's axes as rows and a family's variants as columns, each cell an
## aligned bar of pips with the value under it. Every value is the engine's
## (SimBridge.get_farm_type_catalog): the bar fills to the median over the
## largest median of that axis across all seven variants, so families compare
## on one scale; where the catalogue has a bad case (survival, capacity cost,
## build time) a hollow pip marks it. Sources travel in each cell's tooltip.
## The grid holds no farm-type number of its own.

## Pips in a bar.
const PIPS := 10

## The variants shown, in column order.
var _variants: Array = []
## Axis id -> the largest median over every catalogue variant.
var _scale: Dictionary = {}


## Build the table for `variants` (catalogue variant dictionaries) on the
## catalogue's `axes`, scaled over `all_variants`. `reference_id` names a
## column drawn as a reference (dimmed header).
## `head_factory` (variant dictionary -> Control) builds each column's head
## (the dialog's select buttons); `foot_factory` adds a last row (the gates).
func setup(axes: Array, variants: Array, all_variants: Array, reference_id: String = "",
		head_factory: Callable = Callable(), foot_factory: Callable = Callable()) -> void:
	for child in get_children():
		remove_child(child)
		child.queue_free()
	_variants = variants
	columns = variants.size() + 1
	add_theme_constant_override("h_separation", 14)
	add_theme_constant_override("v_separation", 6)
	_scale = {}
	for axis in axes:
		var top := 0.0
		for v in all_variants:
			var m: Variant = (v.get("axes", {}) as Dictionary).get(axis, {}).get("median")
			if m != null:
				top = maxf(top, absf(float(m)))
		_scale[axis] = top
	add_child(_corner())
	for v in variants:
		var reference := str(v.get("id", "")) == reference_id
		add_child(head_factory.call(v) if head_factory.is_valid() and not reference else _column_head(v, reference))
	for axis in axes:
		add_child(_axis_label(str(axis)))
		for v in variants:
			add_child(_cell(str(axis), v))
	if foot_factory.is_valid():
		var spacer := Control.new()
		spacer.name = "FootCorner"
		add_child(spacer)
		for v in variants:
			add_child(foot_factory.call(v))


## The bar of `variant_id` on `axis` (tests read its numbers), or null.
func bar(variant_id: String, axis: String) -> PipBar:
	return find_child("Bar_%s_%s" % [variant_id, axis], true, false) as PipBar


## The value label of `variant_id` on `axis`, or null.
func value_label(variant_id: String, axis: String) -> Label:
	return find_child("Value_%s_%s" % [variant_id, axis], true, false) as Label


func _corner() -> Control:
	var l := Label.new()
	l.name = "Corner"
	l.text = tr("BUILD_COMPARE_HEADER")
	l.theme_type_variation = &"OverlineMuted"
	l.uppercase = true
	l.size_flags_vertical = Control.SIZE_SHRINK_END
	return l


func _column_head(v: Dictionary, reference: bool) -> Control:
	var l := Label.new()
	l.name = "Head_" + str(v.get("id", ""))
	l.text = tr(str(v.get("name_key", ""))) + ("\n" + tr("BUILD_COMPARE_REFERENCE") if reference else "")
	l.theme_type_variation = &"BodySmallMuted" if reference else &"BodySmallStrong"
	l.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	l.custom_minimum_size = Vector2(124, 0)
	l.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	l.size_flags_vertical = Control.SIZE_SHRINK_END
	return l


func _axis_label(axis: String) -> Control:
	var l := Label.new()
	l.name = "Axis_" + axis
	l.text = tr("BUILD_AXIS_" + axis.to_upper())
	l.theme_type_variation = &"BodySmall"
	l.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	l.custom_minimum_size = Vector2(124, 0)
	l.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	return l


func _cell(axis: String, v: Dictionary) -> Control:
	var id := str(v.get("id", ""))
	var entry: Dictionary = (v.get("axes", {}) as Dictionary).get(axis, {})
	var col := VBoxContainer.new()
	col.name = "Cell_%s_%s" % [id, axis]
	col.add_theme_constant_override("separation", 2)
	col.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	col.tooltip_text = ComparisonBars.sources_text(entry)
	col.mouse_filter = Control.MOUSE_FILTER_PASS
	var b := PipBar.new()
	b.name = "Bar_%s_%s" % [id, axis]
	var m: Variant = entry.get("median")
	var bad: Variant = entry.get("bad")
	b.configure(-1.0 if m == null else float(m), -1.0 if bad == null else float(bad), float(_scale.get(axis, 0.0)))
	col.add_child(b)
	var value := Label.new()
	value.name = "Value_%s_%s" % [id, axis]
	value.text = ComparisonBars.format_axis(entry)
	value.theme_type_variation = &"FigureSmall"
	value.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	col.add_child(value)
	return col


## A catalogue value as the player reads it: the median, and the bad case in
## brackets where the catalogue has one. "–" with no published figure.
static func format_axis(entry: Dictionary) -> String:
	var m: Variant = entry.get("median")
	if m == null:
		return "–"
	var unit := str(entry.get("unit", ""))
	var text := format_value(float(m), unit)
	var bad: Variant = entry.get("bad")
	if bad != null and absf(float(bad) - float(m)) > 1e-9:
		text += " (" + format_value(float(bad), unit) + ")"
	return text


## One number in its unit.
static func format_value(value: float, unit: String) -> String:
	match unit:
		"NOK/t/yr":
			return SimFormatters.format_nok(value) + "/t/år"
		"NOK/kg":
			return SimFormatters.num(value, 1) + " kr/kg"
		"share":
			return SimFormatters.percent(value)
		"factor":
			return "×" + SimFormatters.num(value, 2)
		"kWh/kg":
			return SimFormatters.num(value, 1) + " kWh/kg"
		"days":
			return SimFormatters.num(value * SimFormatters.MONTHS_PER_YEAR / SimFormatters.DAYS_PER_YEAR, 0) + " mnd"
		"t/yr":
			return SimFormatters.num(value, 0) + " t/år"
	return SimFormatters.num(value, 1)


## "Kilde: L:R1, GB" with the note of a value that has no figure.
static func sources_text(entry: Dictionary) -> String:
	var keys: Array = entry.get("sources", [])
	var names: Array = keys.map(func(k: Variant) -> String:
		return TranslationServer.translate("BUILD_SOURCE_GB") if str(k) == "GB" else (TranslationServer.translate("BUILD_SOURCE_CODE") if str(k) == "code" else str(k)))
	var text := String(TranslationServer.translate("BUILD_SOURCES")) % ", ".join(names)
	if str(entry.get("note", "")) != "":
		text += "\n" + String(TranslationServer.translate("BUILD_NOTE_" + str(entry.get("note", "")).to_upper()))
	return text


## A bar of pips: filled to the median's share of the axis's scale, a hollow
## pip at the bad case. Draws in the surface's palette; holds its numbers for
## tests (median, bad, scale_max, filled, bad_pip).
class PipBar extends Control:
	var median := -1.0
	var bad := -1.0
	var scale_max := 0.0
	var filled := 0
	var bad_pip := -1

	func configure(m: float, b: float, top: float) -> void:
		median = m
		bad = b
		scale_max = top
		filled = ComparisonBars.pips_for(m, top)
		bad_pip = ComparisonBars.pips_for(b, top) if b >= 0.0 else -1
		custom_minimum_size = Vector2(ComparisonBars.PIPS * 12, 12)
		mouse_filter = Control.MOUSE_FILTER_IGNORE
		queue_redraw()

	func _draw() -> void:
		var on := LakskongenTheme.palette(self, &"info")
		var off := LakskongenTheme.palette(self, &"hairline")
		var mark := LakskongenTheme.palette(self, &"warn")
		var w := size.x / ComparisonBars.PIPS
		for i in ComparisonBars.PIPS:
			var r := Rect2(Vector2(i * w + 1.0, 2.0), Vector2(maxf(2.0, w - 3.0), size.y - 4.0))
			draw_rect(r, on if i < filled else Color(off, 0.9), true)
		if bad_pip >= 1:
			var r2 := Rect2(Vector2((bad_pip - 1) * w + 1.0, 0.5), Vector2(maxf(2.0, w - 3.0), size.y - 1.0))
			draw_rect(r2, mark, false, 2.0)


## Pips a value fills on a scale (at least one for any positive value).
static func pips_for(value: float, top: float) -> int:
	if value < 0.0 or top <= 0.0:
		return 0
	if value <= 0.0:
		return 0
	return clampi(int(round(value / top * PIPS)), 1, PIPS)
