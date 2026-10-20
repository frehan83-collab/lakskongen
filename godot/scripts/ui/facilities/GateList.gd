class_name GateList
extends VBoxContainer

## Every unlock gate of a farm-type variant, money included, each with a
## progress bar (roadmap F2-31 acceptance 3, docs/FARM_TYPES_SPEC.md §6.1:
## "Fullfør 1 produksjonssyklus i sjø ● · 12 mnd uten rømming 8/12 · Forsk:
## Havbruk til havs · Finansiering 41/96 MNOK"). The gates and their progress
## are the engine's (the catalogue's "gates": kind, subject, required,
## current, met); a gate the build path waives (the zoned pad on land, the
## development licence at sea) says so. Status is a shape plus a colour:
## ● met, ○ still to do.

## Research node id -> its name (the tree's own), set by the dialog.
var research_names: Dictionary = {}
## Variant id -> its name key, for "a cycle with <variant>".
var variant_names: Dictionary = {}


## Show the gates of `variant` (a catalogue variant dictionary); `waived`
## lists the gate kinds its build path waives; `family_id` words the money gate.
func show_gates(variant: Dictionary, waived: Array, family_id: String) -> void:
	for child in get_children():
		remove_child(child)
		child.queue_free()
	add_theme_constant_override("separation", 4)
	var head := Label.new()
	head.name = "GatesHeader"
	head.text = tr("BUILD_GATES_HEADER")
	head.theme_type_variation = &"OverlineMuted"
	head.uppercase = true
	add_child(head)
	var i := 0
	for gate in variant.get("gates", []):
		add_child(_row(gate as Dictionary, waived, family_id, i))
		i += 1


## The rows (one per gate), in the catalogue's order.
func rows() -> Array:
	return get_children().filter(func(c: Node) -> bool: return c.name.begins_with("Gate_"))


func _row(gate: Dictionary, waived: Array, family_id: String, index: int) -> Control:
	var kind := str(gate.get("kind", ""))
	var met := bool(gate.get("met", false))
	var required := float(gate.get("required", 0.0))
	var current := float(gate.get("current", 0.0))
	var row := VBoxContainer.new()
	row.name = "Gate_%d_%s" % [index, kind]
	row.add_theme_constant_override("separation", 2)
	row.set_meta(&"kind", kind)
	row.set_meta(&"met", met)
	row.set_meta(&"progress", GateList.progress_of(gate))
	var line := HBoxContainer.new()
	line.add_theme_constant_override("separation", 6)
	var shape := Label.new()
	shape.name = "Shape"
	shape.text = "●" if met else "○"
	shape.theme_type_variation = &"BodySmallOk" if met else &"BodySmallMuted"
	line.add_child(shape)
	var text := Label.new()
	text.name = "Text"
	text.text = _gate_text(kind, str(gate.get("subject", "")), required, family_id)
	text.theme_type_variation = &"BodySmall" if not met else &"BodySmallMuted"
	text.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	text.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	text.custom_minimum_size = Vector2(110, 0)
	line.add_child(text)
	row.add_child(line)
	var amount_text := _amount_text(kind, current, required)
	if amount_text != "":
		var amount := Label.new()
		amount.name = "Amount"
		amount.text = amount_text
		amount.theme_type_variation = &"FigureSmall"
		row.add_child(amount)
	var bar := ProgressBar.new()
	bar.name = "Progress"
	bar.min_value = 0.0
	bar.max_value = 1.0
	bar.step = 0.0
	bar.value = GateList.progress_of(gate)
	bar.show_percentage = false
	bar.custom_minimum_size = Vector2(0, 6)
	row.add_child(bar)
	if not met and waived.has(kind) and kind != "Funds":
		var note := Label.new()
		note.name = "Waived"
		note.text = tr("BUILD_GATE_WAIVED_" + kind.to_upper())
		note.theme_type_variation = &"SmallMuted"
		note.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		note.custom_minimum_size = Vector2(120, 0)
		row.add_child(note)
	return row


## "41 / 96 MNOK", "0,04 / 0,90 GNOK": two amounts in the larger one's unit, so a gate fits its column.
static func nok_pair(current: float, required: float) -> String:
	var top := maxf(absf(current), absf(required))
	if top >= 1_000_000_000.0:
		return "%s / %s GNOK" % [SimFormatters.num(current / 1_000_000_000.0, 2), SimFormatters.num(required / 1_000_000_000.0, 2)]
	if top >= 1_000_000.0:
		return "%s / %s MNOK" % [SimFormatters.num(current / 1_000_000.0, 0), SimFormatters.num(required / 1_000_000.0, 0)]
	return "%s / %s kNOK" % [SimFormatters.num(current / 1_000.0, 0), SimFormatters.num(required / 1_000.0, 0)]


## How far a gate is (0-1): its current value over what it asks for.
static func progress_of(gate: Dictionary) -> float:
	var required := float(gate.get("required", 0.0))
	if bool(gate.get("met", false)):
		return 1.0
	if required <= 0.0:
		return 0.0
	return clampf(float(gate.get("current", 0.0)) / required, 0.0, 1.0)


func _gate_text(kind: String, subject: String, required: float, family_id: String) -> String:
	match kind:
		"Research":
			return tr("BUILD_GATE_RESEARCH") % str(research_names.get(subject, tr("BUILD_GATE_RESEARCH_UNKNOWN")))
		"EscapeFreeDays":
			return tr("BUILD_GATE_ESCAPEFREEDAYS") % SimFormatters.num(required * SimFormatters.MONTHS_PER_YEAR / SimFormatters.DAYS_PER_YEAR, 0)
		"OwnUnitCompletedCycle":
			return tr("BUILD_GATE_OWNUNITCOMPLETEDCYCLE") % tr(str(variant_names.get(subject, "")))
		"Funds":
			return tr("BUILD_GATE_FUNDS_HAV") if family_id == "hav" else tr("BUILD_GATE_FUNDS")
	return tr("BUILD_GATE_" + kind.to_upper())


func _amount_text(kind: String, current: float, required: float) -> String:
	match kind:
		"Funds":
			return GateList.nok_pair(maxf(0.0, current), required)
		"EscapeFreeDays":
			var per_month := float(SimFormatters.DAYS_PER_YEAR) / SimFormatters.MONTHS_PER_YEAR
			return "%s / %s" % [SimFormatters.num(minf(current, required) / per_month, 0), SimFormatters.num(required / per_month, 0)]
		"Start":
			return ""
	return "%s / %s" % [SimFormatters.num(minf(current, required), 0), SimFormatters.num(required, 0)]
