class_name FarmFamilyCard
extends PanelContainer

## One of the build dialog's three family cards (roadmap F2-31,
## docs/FARM_TYPES_SPEC.md §1 and §6.2): Sjø, Land or Hav, with what the
## family pays for capacity with and what limits it (the vision table), its
## variants in short names, and how many of them the player may start now.
## The card is chosen by a click anywhere on it or by its button (the
## keyboard path: focus the button, Enter). Everything shown comes from the
## engine's farm-type catalogue; the card holds no number of its own.

signal chosen(family_id: String)

@onready var _title: Label = %Title
@onready var _chip: Label = %Chip
@onready var _pays: Label = %Pays
@onready var _limits: Label = %Limits
@onready var _variants: Label = %Variants
@onready var _status: Label = %Status
@onready var _choose: Button = %ChooseBtn

var family_id := ""
var _selected := false


func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_STOP
	gui_input.connect(_on_gui_input)
	_choose.pressed.connect(func() -> void: chosen.emit(family_id))


## Fill the card from a catalogue family (`family`: id, name_key, variants)
## and the start rules (`starts`: variant id -> {startable}).
func configure(family: Dictionary, starts: Dictionary, selected: bool, compact: bool) -> void:
	family_id = str(family.get("id", ""))
	_selected = selected
	var variants: Array = family.get("variants", [])
	var startable := 0
	var names: Array = []
	for v in variants:
		names.append(tr(str(v.get("name_key", ""))))
		if bool((starts.get(str(v.get("id", "")), {}) as Dictionary).get("startable", false)):
			startable += 1
	_title.text = tr(str(family.get("name_key", "")))
	_title.theme_type_variation = &"Subheading" if selected else &"SubheadingMuted"
	_chip.visible = selected
	_chip.text = tr("EXPANSION_FAMILY_SELECTED")
	_pays.text = tr("BUILD_FAMILY_PAYS_" + family_id.to_upper())
	_limits.text = tr("BUILD_FAMILY_LIMITS_" + family_id.to_upper())
	_variants.text = " · ".join(names)
	_status.text = tr("BUILD_FAMILY_OPEN_ALL") if startable == variants.size() else (
		tr("BUILD_FAMILY_LOCKED_ALL") if startable == 0 else tr("BUILD_FAMILY_OPEN_SOME") % [startable, variants.size()])
	_status.theme_type_variation = &"SmallOk" if startable > 0 else &"SmallMuted"
	for l in [_pays, _limits, _variants]:
		(l as Label).visible = not compact
	_choose.text = tr("BUILD_FAMILY_CHOSEN") if selected else tr("BUILD_FAMILY_CHOOSE") % _title.text
	_choose.theme_type_variation = &"SmallButton" if selected else &"SmallGhostButton"
	_choose.toggle_mode = true
	_choose.set_pressed_no_signal(selected)
	theme_type_variation = &"KpiCardAccent" if selected else &"Tile"


## The button the keyboard path focuses and activates.
func choose_button() -> Button:
	return _choose


func is_selected() -> bool:
	return _selected


func _on_gui_input(event: InputEvent) -> void:
	var click := event as InputEventMouseButton
	if click != null and click.pressed and click.button_index == MOUSE_BUTTON_LEFT:
		accept_event()
		chosen.emit(family_id)
