class_name DriftsplanCard
extends PanelContainer

## The Driftsplan card (roadmap F2-05; docs/FARM_TYPES_SPEC.md §1 pillar 5,
## §6.5): a unit's standing orders. It leads with "Neste planlagt" and lists
## every policy the game has for the unit's kind, each with a switch, its
## settings and what it last did. Everything it shows comes from the sim
## (SimBridge.get_driftsplan_catalog / get_driftsplan / get_next_planned):
## the ranges, the defaults and the reasons are the engine's, never this
## script's. Turning a switch on sets the order with the values shown; a
## change while it is on sets it again; turning it off ends it.
##
## Built in code so any board can hold it: the pen view mounts it for a fjord
## site, the facility boards (F2-32, F2-33) for their units.

signal plan_changed(unit_id: String)

## Shown for a number setting whose unit is "share" (0-1): as a percentage.
const SHARE_STEP_PCT := 5.0

var _sim: Node = null
var _unit_id := ""
var _unit_kind := ""
var _catalog: Array = []
## policy kind -> {"check": CheckButton, "status": Label, "controls": {key: Control}}
var _rows: Dictionary = {}
var _syncing := false

var _title: Label
var _next: Label
var _body: VBoxContainer
var _empty: Label


func _init() -> void:
	name = "DriftsplanCard"
	theme_type_variation = &"Card"
	size_flags_horizontal = Control.SIZE_EXPAND_FILL
	var outer := VBoxContainer.new()
	outer.add_theme_constant_override("separation", 0)
	add_child(outer)

	var header := PanelContainer.new()
	header.theme_type_variation = &"CardHeaderCompact"
	outer.add_child(header)
	var header_row := HBoxContainer.new()
	header_row.add_theme_constant_override("separation", 12)
	header.add_child(header_row)
	_title = Label.new()
	_title.name = "Title"
	_title.theme_type_variation = &"BodySmall"
	header_row.add_child(_title)
	_next = Label.new()
	_next.name = "NextPlanned"
	_next.theme_type_variation = &"SmallMuted"
	_next.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_next.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	_next.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
	_next.clip_text = true
	header_row.add_child(_next)

	var pad := MarginContainer.new()
	pad.add_theme_constant_override("margin_left", 16)
	pad.add_theme_constant_override("margin_top", 12)
	pad.add_theme_constant_override("margin_right", 16)
	pad.add_theme_constant_override("margin_bottom", 16)
	outer.add_child(pad)
	_body = VBoxContainer.new()
	_body.add_theme_constant_override("separation", 12)
	pad.add_child(_body)
	_empty = Label.new()
	_empty.theme_type_variation = &"SmallMuted"
	_empty.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_body.add_child(_empty)


func _ready() -> void:
	_sim = get_node_or_null("/root/SimBridge")
	_title.text = tr("DRIFTSPLAN_TITLE")
	if _sim != null and _sim.has_signal("state_updated"):
		_sim.state_updated.connect(refresh)


## Show the orders of <unit_id>, a unit of <unit_kind> ("fjord_site",
## "offshore_unit", "land_site", "post_smolt_site").
func set_unit(unit_id: String, unit_kind: String) -> void:
	var rebuild := unit_kind != _unit_kind or _rows.is_empty()
	_unit_id = unit_id
	_unit_kind = unit_kind
	if rebuild:
		_build_rows()
	refresh()


## The policy's switch (UI flow tests click it).
func policy_check(kind: String) -> CheckButton:
	return _rows[kind]["check"] if _rows.has(kind) else null


## A policy's setting control: a SpinBox for a number, an OptionButton for a choice.
func setting_control(kind: String, key: String) -> Control:
	return _rows[kind]["controls"].get(key) if _rows.has(kind) else null


## The policy's status line.
func policy_status(kind: String) -> Label:
	return _rows[kind]["status"] if _rows.has(kind) else null


## The "Neste planlagt" line.
func next_planned_label() -> Label:
	return _next


func _build_rows() -> void:
	for kind in _rows:
		(_rows[kind]["box"] as Control).queue_free()
	_rows.clear()
	_catalog = _sim.get_driftsplan_catalog() if _sim != null else []
	for policy in _catalog:
		var kinds: Array = policy.get("applies_to", [])
		if kinds.has(_unit_kind):
			_add_row(policy)
	_empty.text = tr("DRIFTSPLAN_INTRO") if not _rows.is_empty() else tr("DRIFTSPLAN_NONE_FOR_UNIT")


func _add_row(policy: Dictionary) -> void:
	var kind := str(policy.get("kind", ""))
	var box := VBoxContainer.new()
	box.name = "Policy_" + kind
	box.add_theme_constant_override("separation", 4)
	_body.add_child(box)

	var top := HBoxContainer.new()
	top.add_theme_constant_override("separation", 12)
	box.add_child(top)
	var check := CheckButton.new()
	check.name = "Switch"
	check.text = tr(str(policy.get("name_key", "")))
	check.tooltip_text = tr("DRIFTSPLAN_TIP_" + kind.to_upper())
	check.toggled.connect(_on_toggled.bind(kind))
	top.add_child(check)
	var status := Label.new()
	status.name = "Status"
	status.theme_type_variation = &"SmallMuted"
	status.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	status.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	top.add_child(status)

	var settings := HFlowContainer.new()
	settings.add_theme_constant_override("h_separation", 12)
	settings.add_theme_constant_override("v_separation", 4)
	box.add_child(settings)
	var controls := {}
	for setting in policy.get("settings", []):
		var key := str(setting.get("key", ""))
		var field := HBoxContainer.new()
		field.add_theme_constant_override("separation", 6)
		settings.add_child(field)
		var caption := Label.new()
		caption.theme_type_variation = &"SmallMuted"
		caption.text = tr(str(setting.get("name_key", "")))
		field.add_child(caption)
		var control := _setting_control(setting)
		control.name = "Setting_" + key
		control.tooltip_text = _setting_tip(kind, setting)
		field.add_child(control)
		controls[key] = control
	_rows[kind] = {"box": box, "check": check, "status": status, "controls": controls, "policy": policy}


func _setting_control(setting: Dictionary) -> Control:
	if str(setting.get("kind", "number")) == "choice":
		var option := OptionButton.new()
		option.theme_type_variation = &"SmallOptionButton"
		for choice in setting.get("choices", []):
			option.add_item(tr("DRIFTSPLAN_CHOICE_" + str(choice).to_upper()))
			option.set_item_metadata(option.item_count - 1, str(choice))
		option.item_selected.connect(func(_i: int) -> void: _on_setting_changed(option))
		return option
	var spin := SpinBox.new()
	spin.custom_minimum_size = Vector2(110, 0)
	var share := str(setting.get("unit", "")) == "share"
	var scale := 100.0 if share else 1.0
	spin.min_value = float(setting.get("min", 0.0)) * scale
	spin.max_value = float(setting.get("max", 1.0)) * scale
	spin.step = SHARE_STEP_PCT if share else (1.0 if bool(setting.get("whole", false)) else 0.1)
	# SpinBox puts its own space before the suffix.
	spin.suffix = "%" if share else (tr("DRIFTSPLAN_UNIT_DAYS") if str(setting.get("unit", "")) == "days" else "")
	spin.set_meta("scale", scale)
	spin.value = float(setting.get("default", 0.0)) * scale
	spin.value_changed.connect(func(_v: float) -> void: _on_setting_changed(spin))
	return spin


## Why the default is what it is: the setting's own Norwegian note, else its source.
func _setting_tip(kind: String, setting: Dictionary) -> String:
	var key := "DRIFTSPLAN_SETTING_TIP_%s_%s" % [kind.to_upper(), str(setting.get("key", "")).to_upper()]
	var tip := tr(key)
	return tip if tip != key else tr("DRIFTSPLAN_SOURCE") % str(setting.get("source", ""))


## Reread the unit's orders and its next job.
func refresh() -> void:
	if _sim == null or _unit_id.is_empty() or not is_inside_tree():
		return
	_syncing = true
	var orders := {}
	for entry in _sim.get_driftsplan(_unit_id):
		orders[str(entry.get("policy", ""))] = entry
	for kind in _rows:
		var row: Dictionary = _rows[kind]
		var entry: Dictionary = orders.get(kind, {})
		var on := not entry.is_empty()
		(row["check"] as CheckButton).set_pressed_no_signal(on)
		for setting in row["policy"].get("settings", []):
			var key := str(setting.get("key", ""))
			var control: Control = row["controls"][key]
			if control is SpinBox:
				var value := float(entry.get("numbers", {}).get(key, setting.get("default", 0.0)))
				(control as SpinBox).set_value_no_signal(value * float(control.get_meta("scale", 1.0)))
			elif control is OptionButton:
				var choice := str(entry.get("choices", {}).get(key, setting.get("default_choice", "")))
				var option := control as OptionButton
				for i in option.item_count:
					if str(option.get_item_metadata(i)) == choice:
						option.select(i)
		(row["status"] as Label).text = _status_text(entry)
	_next.text = _next_text(_sim.get_next_planned(_unit_id))
	_syncing = false


func _on_toggled(on: bool, kind: String) -> void:
	if _syncing or _sim == null:
		return
	var result: Dictionary = _sim.set_driftsplan(_unit_id, kind, _settings_of(kind)) if on else _sim.clear_driftsplan(_unit_id, kind)
	if not bool(result.get("success", false)):
		push_warning("DriftsplanCard: %s %s refused: %s" % [kind, _unit_id, str(result.get("error", ""))])
	plan_changed.emit(_unit_id)
	refresh()


func _on_setting_changed(control: Control) -> void:
	if _syncing or _sim == null:
		return
	for kind in _rows:
		if _rows[kind]["controls"].values().has(control) and (_rows[kind]["check"] as CheckButton).button_pressed:
			_sim.set_driftsplan(_unit_id, kind, _settings_of(kind))
			plan_changed.emit(_unit_id)
			refresh()
			return


## The row's values as the engine takes them (a share back to 0-1).
func _settings_of(kind: String) -> Dictionary:
	var values := {}
	var controls: Dictionary = _rows[kind]["controls"]
	for key in controls:
		var control: Control = controls[key]
		if control is SpinBox:
			var spin := control as SpinBox
			var scale := float(spin.get_meta("scale", 1.0))
			values[key] = int(spin.value) if is_equal_approx(spin.step, 1.0) and is_equal_approx(scale, 1.0) else spin.value / scale
		elif control is OptionButton:
			var option := control as OptionButton
			values[key] = str(option.get_item_metadata(option.selected))
	return values


func _status_text(entry: Dictionary) -> String:
	if entry.is_empty():
		return tr("DRIFTSPLAN_STATUS_OFF")
	var parts: Array[String] = []
	var next: Dictionary = entry.get("next", {})
	if not next.is_empty():
		parts.append(_when_text(next))
	var last := int(entry.get("last_attempt_day", -1))
	if last >= 0:
		var outcome := str(entry.get("last_outcome", ""))
		var said := tr("DRIFTSPLAN_OUTCOME_OK") if outcome == "ok" else _failure_text(outcome)
		parts.append(tr("DRIFTSPLAN_LAST") % [GameCalendar.short_date(last), said])
	return " · ".join(parts)


func _next_text(job: Dictionary) -> String:
	if job.is_empty():
		return tr("DRIFTSPLAN_NEXT_NONE")
	var policy_name := str(job.get("policy", ""))
	for policy in _catalog:
		if str(policy.get("kind", "")) == policy_name:
			policy_name = tr(str(policy.get("name_key", "")))
	return tr("DRIFTSPLAN_NEXT") % [policy_name, _when_text(job)]


## "14. jan (om 6 dager)", "i dag", or what a waiting job waits for.
func _when_text(job: Dictionary) -> String:
	var day := int(job.get("day", -1))
	if day < 0:
		var key := "DRIFTSPLAN_WAIT_" + str(job.get("trigger", "")).to_upper()
		var pattern := tr(key)
		if pattern == key:
			return tr("DRIFTSPLAN_WAIT")
		return pattern % SimFormatters.num(float(job.get("trigger_value", 0.0)), 2) if pattern.count("%s") == 1 else pattern
	var left := int(job.get("days_left", 0))
	if left <= 0:
		return tr("DRIFTSPLAN_WHEN_TODAY")
	return tr("DRIFTSPLAN_WHEN") % [GameCalendar.short_date(day), left]


func _failure_text(error_key: String) -> String:
	var key := "DRIFTSPLAN_FAIL_" + error_key.to_upper()
	var text := tr(key)
	return text if text != key else tr("DRIFTSPLAN_FAIL_OTHER")
