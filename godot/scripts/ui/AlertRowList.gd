class_name AlertRowList
extends RefCounted

## The dashboard's alert rows (wave 3 ui-a). A row keeps its nodes from one
## refresh to the next and only a label whose text changed is set again: an
## alert's age ("I går", "3 dager siden") changes every sim day, and at 30x
## rebuilding the whole list for it cost 0.9 ms at p50 and 2.3 ms at p95 of
## a day (headless, po3_red_sone seed 91). A row: a level dot, the message
## and its time (with the repeat count), meta alert_category / alert_site.

const LEVEL_COLORS := {
	"warning": Color("#8A5A00"),
	"danger": Color("#B3172F"),
	"critical": Color("#B3172F"),
	"info": Color("#1F5E73"),
}
const NEUTRAL := Color("#5A6168")
const TEXT_SIZE := 13
const TIME_SIZE := 11
const EMPTY_META := &"alert_empty_state"


## Show `alerts` ({text, level, time, count, category, site}) in `container`;
## `empty_text` when there are none. Rows are reused in order.
static func sync(container: VBoxContainer, alerts: Array[Dictionary], empty_text: String, theme: Node) -> void:
	var rows: Array[HBoxContainer] = []
	var others: Array[Node] = []
	for child in container.get_children():
		if child.is_queued_for_deletion():
			continue
		if child is HBoxContainer and child.has_meta("alert_category"):
			rows.append(child)
		else:
			others.append(child)
	if alerts.is_empty():
		if rows.is_empty() and others.size() == 1 and others[0].get_meta(EMPTY_META, "") == empty_text:
			return  # the empty state already shows
		UIPolish.show_empty_state_localized(container, empty_text, TEXT_SIZE, "", theme)
		container.get_child(container.get_child_count() - 1).set_meta(EMPTY_META, empty_text)
		return
	for node in others:
		node.queue_free()  # the empty state
	for i in alerts.size():
		fill(rows[i] if i < rows.size() else _new_row(container), alerts[i])
	for i in range(alerts.size(), rows.size()):
		rows[i].queue_free()
	UIPolish.link_vertical_focus(container)


## The row shows `alert` (only what differs is set).
static func fill(row: HBoxContainer, alert: Dictionary) -> void:
	var dot := row.get_child(0) as ColorRect
	var color: Color = LEVEL_COLORS.get(str(alert.get("level", "")), NEUTRAL)
	if dot.color != color:
		dot.color = color
	var lines := row.get_child(1)
	_set_text(lines.get_child(0) as Label, str(alert.get("text", "")))
	var time := str(alert.get("time", ""))
	if int(alert.get("count", 1)) > 1:
		time += " · " + TranslationServer.translate("UI_DASHBOARD_ALERT_REPEATS") % int(alert.get("count", 1))
	_set_text(lines.get_child(1) as Label, time)
	row.set_meta("alert_category", alert.get("category", ""))
	row.set_meta("alert_site", alert.get("site", ""))


static func _set_text(label: Label, text: String) -> void:
	if label.text != text:
		label.text = text


static func _new_row(container: VBoxContainer) -> HBoxContainer:
	var hbox := HBoxContainer.new()
	hbox.add_theme_constant_override("separation", 12)
	hbox.set_meta("alert_category", "")
	container.add_child(hbox)
	var dot := ColorRect.new()
	dot.custom_minimum_size = Vector2(8, 8)
	dot.color = NEUTRAL
	hbox.add_child(dot)
	var lines := VBoxContainer.new()
	lines.add_theme_constant_override("separation", 2)
	lines.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	hbox.add_child(lines)
	var text_lbl := Label.new()
	LakskongenTheme.set_size(text_lbl, TEXT_SIZE)
	LakskongenTheme.set_tone(text_lbl, &"")
	text_lbl.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	lines.add_child(text_lbl)
	var time_lbl := Label.new()
	LakskongenTheme.set_size(time_lbl, TIME_SIZE)
	LakskongenTheme.set_tone(time_lbl, &"Muted")
	lines.add_child(time_lbl)
	return hbox
