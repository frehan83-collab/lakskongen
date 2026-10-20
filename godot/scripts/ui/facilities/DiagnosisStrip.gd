class_name DiagnosisStrip
extends PanelContainer

## The build dialog's diagnosis strip (roadmap F2-31 acceptance 5,
## docs/FARM_TYPES_SPEC.md §6.2): "Din flaskehals: lus i PO3 (rødt) → Land
## eller Hav fjerner den". The binding constraint is the engine's
## (SimBridge.get_build_diagnosis: lice light, licence MTB in use, the
## warmest month, deaths from the water, cash against the cheapest rung), as
## are the families that remove it; the strip only words it. Severity is a
## shape plus a colour: ■ binds hard, ▲ binds, ● loose.

## Score at or above which a constraint binds hard (■, crit).
const HARD := 0.75
## Score at or above which it binds (▲, warn).
const SOFT := 0.4

var _shape: Label = null
var _text: Label = null
var _next: Label = null

## The diagnosis shown (the bridge's dictionary).
var diagnosis: Dictionary = {}


func _ready() -> void:
	if _shape == null:
		_build()


## Build the strip's nodes when it is made in code (no scene).
func _build() -> void:
	theme_type_variation = &"CalloutInfo"
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 8)
	add_child(row)
	_shape = Label.new()
	_shape.name = "Shape"
	row.add_child(_shape)
	var col := VBoxContainer.new()
	col.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	col.add_theme_constant_override("separation", 2)
	row.add_child(col)
	_text = Label.new()
	_text.name = "Text"
	_text.theme_type_variation = &"BodySmallStrong"
	_text.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_text.custom_minimum_size = Vector2(300, 0)
	col.add_child(_text)
	_next = Label.new()
	_next.name = "Next"
	_next.theme_type_variation = &"SmallMuted"
	_next.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_next.custom_minimum_size = Vector2(300, 0)
	col.add_child(_next)


## Show `d` (SimBridge.get_build_diagnosis()).
func show_diagnosis(d: Dictionary) -> void:
	if _shape == null:
		_build()
	diagnosis = d
	var binding: Dictionary = d.get("binding", {})
	visible = not binding.is_empty()
	if binding.is_empty():
		return
	var score := float(binding.get("score", 0.0))
	_shape.text = "■" if score >= HARD else ("▲" if score >= SOFT else "●")
	_shape.theme_type_variation = &"BodyStrongCrit" if score >= HARD else (&"BodyStrongWarn" if score >= SOFT else &"BodyStrongOk")
	theme_type_variation = &"CalloutCrit" if score >= HARD else (&"CalloutWarn" if score >= SOFT else &"CalloutOk")
	_text.text = tr("BUILD_DIAG_LINE") % [DiagnosisStrip.constraint_text(binding), DiagnosisStrip.relief_text(binding)]
	var others: Array = []
	for c in (d.get("constraints", []) as Array).slice(1, 3):
		others.append(DiagnosisStrip.constraint_text(c as Dictionary))
	_next.visible = not others.is_empty()
	_next.text = tr("BUILD_DIAG_THEN") % ", ".join(others)


## The strip's main line (tests read it).
func text_label() -> Label:
	return _text


## "lus i PO 3 (rødt trafikklys)", "MTB i PO 3: 92 % i bruk", …
static func constraint_text(c: Dictionary) -> String:
	var key := str(c.get("key", ""))
	var area := int(c.get("area", 0))
	var value := float(c.get("value", 0.0))
	var reference := float(c.get("reference", 0.0))
	var t := func(k: String) -> String: return String(TranslationServer.translate(k))
	match key:
		"lice":
			return t.call("BUILD_DIAG_LICE") % [area, t.call("BUILD_DIAG_LIGHT_%d" % int(value))]
		"capacity":
			return t.call("BUILD_DIAG_CAPACITY") % [area, SimFormatters.percent(value)]
		"temperature":
			return t.call("BUILD_DIAG_TEMPERATURE") % [area, SimFormatters.num(value, 1), SimFormatters.num(reference, 0)]
		"oxygen":
			return t.call("BUILD_DIAG_OXYGEN") % SimFormatters.percent(value)
		"capital":
			return t.call("BUILD_DIAG_CAPITAL") % [SimFormatters.format_nok(maxf(0.0, value)), SimFormatters.format_nok(reference)]
	return t.call("BUILD_DIAG_UNKNOWN")


## "Land eller Hav fjerner den", "Land fjerner den", or what eases it.
static func relief_text(c: Dictionary) -> String:
	var families: Array = c.get("relieved_by", [])
	var t := func(k: String) -> String: return String(TranslationServer.translate(k))
	if families.has("land") and families.has("hav"):
		return t.call("BUILD_DIAG_RELIEF_BOTH")
	if families.has("land"):
		return t.call("BUILD_DIAG_RELIEF_LAND")
	if families.has("hav"):
		return t.call("BUILD_DIAG_RELIEF_HAV")
	return t.call("BUILD_DIAG_RELIEF_" + str(c.get("key", "")).to_upper())
