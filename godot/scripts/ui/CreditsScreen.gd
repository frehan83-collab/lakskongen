extends Control

## CreditsScreen — Medvirkende (roadmap U1-06, D6/D24).
##
## Reachable from the main menu and the pause menu. The page is built from
## res://data/credits.json: who made the game, then every third-party work the
## game ships with its source and licence — the list THIRD_PARTY.md keeps
## (tests/test_credits.py holds the two together; CC BY works carry their
## attribution notice). The page drifts upward after a moment; the wheel, a
## drag or a key takes over and it drifts on again after a pause. × or Esc
## closes it (close_requested).

signal close_requested

const CREDITS_PATH := "res://data/credits.json"
const DRIFT_PX_PER_S := 36.0
const DRIFT_DELAY_S := 1.4
## After the player scrolls, the page waits this long before drifting on.
const DRIFT_RESUME_S := 4.0
const SECTION_GAP := 48.0

@onready var scroll_container: ScrollContainer = %ScrollContainer
@onready var credits_container: VBoxContainer = %CreditsContainer
@onready var close_button: Button = %CloseButton

var _wait: float = DRIFT_DELAY_S
var _drift: float = 0.0
var _entries: Array[Dictionary] = []

func _ready() -> void:
	close_button.tooltip_text = tr("UI_CREDITS_CLOSE_TIP")
	close_button.pressed.connect(close)
	scroll_container.gui_input.connect(_on_scroll_input)
	_build()
	visibility_changed.connect(_on_visibility_changed)
	_on_visibility_changed()

func _on_visibility_changed() -> void:
	if not visible:
		return
	_wait = DRIFT_DELAY_S
	_drift = 0.0
	scroll_container.scroll_vertical = 0
	modulate.a = 0.0
	create_tween().tween_property(self, "modulate:a", 1.0, 0.3)
	close_button.grab_focus.call_deferred()

func _process(delta: float) -> void:
	if not visible:
		return
	if _wait > 0.0:
		_wait -= delta
		return
	_drift += DRIFT_PX_PER_S * delta
	var step := int(_drift)
	if step > 0:
		_drift -= step
		scroll_container.scroll_vertical += step

func _input(event: InputEvent) -> void:
	if not visible:
		return
	if event.is_action_pressed("ui_cancel"):
		close()
		get_viewport().set_input_as_handled()
	elif event is InputEventKey and event.pressed:
		_wait = DRIFT_RESUME_S

func _on_scroll_input(event: InputEvent) -> void:
	if event is InputEventMouseButton or event is InputEventScreenDrag or event is InputEventPanGesture:
		_wait = DRIFT_RESUME_S

## Close (× or Esc): fade out, hide, and tell the menu underneath.
func close() -> void:
	if not visible:
		return
	var tween := create_tween()
	tween.tween_property(self, "modulate:a", 0.0, 0.2)
	tween.tween_callback(func():
		hide()
		close_requested.emit()
	)

## The third-party works listed on the page (credits.json entries, in order).
func get_listed_entries() -> Array[Dictionary]:
	return _entries.duplicate(true)

## The name a work is shown under.
func display_name(entry: Dictionary) -> String:
	var key := str(entry.get("display_key", ""))
	return tr(key) if not key.is_empty() else str(entry.get("name", ""))

# ── Page ───────────────────────────────────────────────────────────────────────

func _build() -> void:
	var data := _load_data()
	_add_space(96.0)
	var title := _label(tr("UI_MAINMENU_TITLE"), &"DisplayAccent")
	credits_container.add_child(title)
	credits_container.add_child(_label(tr("UI_MAINMENU_SUBTITLE"), &"HeadingMuted"))

	_section("UI_CREDITS_MADE_BY")
	for member: Dictionary in data.get("team", []):
		credits_container.add_child(_label(tr(str(member.get("role_key", ""))), &"BodySmallMuted"))
		credits_container.add_child(_label(str(member.get("name", "")), &"Heading"))

	_section("UI_CREDITS_THIRD_PARTY")
	var intro := _label(tr("UI_CREDITS_THIRD_PARTY_INTRO"), &"BodySmallMuted")
	intro.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	credits_container.add_child(intro)
	_entries.clear()
	for entry: Dictionary in data.get("third_party", []):
		_entries.append(entry)
		credits_container.add_child(_work_row(entry))

	_section("UI_CREDITS_THANKS")
	var thanks := _label(tr("UI_CREDITS_THANKS_BODY"), &"Body")
	thanks.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	credits_container.add_child(thanks)
	_add_space(SECTION_GAP)
	credits_container.add_child(_label(tr("UI_CREDITS_COPYRIGHT"), &"SmallMuted"))
	# Room to drift the last line up to the middle of the screen.
	_add_space(get_viewport_rect().size.y * 0.5)

## One third-party work: what it is for, its name, author and source, the
## licence as a tag, and an attribution notice where the licence asks for one.
func _work_row(entry: Dictionary) -> Control:
	var row := PanelContainer.new()
	row.name = "Work"
	row.theme_type_variation = &"Tile"
	row.set_meta("credit_name", str(entry.get("name", "")))
	row.set_meta("credit_licence", str(entry.get("licence", "")))
	var line := HBoxContainer.new()
	line.add_theme_constant_override("separation", 16)
	row.add_child(line)
	var text := VBoxContainer.new()
	text.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	text.add_theme_constant_override("separation", 2)
	line.add_child(text)
	var use := _label(tr(str(entry.get("use_key", ""))), &"OverlineMuted", HORIZONTAL_ALIGNMENT_LEFT)
	text.add_child(use)
	var name_label := _label(display_name(entry), &"Subheading", HORIZONTAL_ALIGNMENT_LEFT)
	name_label.name = "WorkName"
	text.add_child(name_label)
	var by := "%s · %s" % [str(entry.get("author", "")), str(entry.get("source", ""))]
	text.add_child(_label(by, &"BodySmallMuted", HORIZONTAL_ALIGNMENT_LEFT))
	var notice_key := str(entry.get("notice_key", ""))
	if not notice_key.is_empty():
		var notice := _label(tr(notice_key), &"BodySmall", HORIZONTAL_ALIGNMENT_LEFT)
		notice.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		text.add_child(notice)
	var licence_key := str(entry.get("licence_key", ""))
	var licence := _label(tr(licence_key) if not licence_key.is_empty() else str(entry.get("licence", "")),
		&"TagInfo", HORIZONTAL_ALIGNMENT_CENTER)
	licence.name = "Licence"
	licence.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	line.add_child(licence)
	return row

func _section(title_key: String) -> void:
	_add_space(SECTION_GAP)
	credits_container.add_child(_label(tr(title_key), &"OverlineAccent"))
	var rule := HSeparator.new()
	credits_container.add_child(rule)

func _label(text: String, variation: StringName, align: HorizontalAlignment = HORIZONTAL_ALIGNMENT_CENTER) -> Label:
	var lbl := Label.new()
	lbl.text = text
	lbl.theme_type_variation = variation
	lbl.horizontal_alignment = align
	return lbl

func _add_space(height: float) -> void:
	var spacer := Control.new()
	spacer.custom_minimum_size = Vector2(0, height)
	spacer.mouse_filter = Control.MOUSE_FILTER_IGNORE
	credits_container.add_child(spacer)

func _load_data() -> Dictionary:
	var file := FileAccess.open(CREDITS_PATH, FileAccess.READ)
	if file == null:
		push_error("CreditsScreen: cannot read %s" % CREDITS_PATH)
		return {}
	var parsed: Variant = JSON.parse_string(file.get_as_text())
	if not (parsed is Dictionary):
		push_error("CreditsScreen: %s is not a JSON object" % CREDITS_PATH)
		return {}
	return parsed
