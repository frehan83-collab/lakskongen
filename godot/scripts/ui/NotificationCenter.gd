## NotificationCenter — the event inbox (roadmap U2-06; layout U1-04).
## Slide-in drawer on the right edge, under the tab rail; the HUD bell and
## the V key open it (N is "Til neste hendelse", D15/U2-07). Layout lives in
## scenes/ui/NotificationCenter.tscn; this
## script is wiring and the entry list.
##
## Every event the typed bus delivers lands here (SimBridge.game_event_raised):
## criticals and warnings as unread entries (the bell's badge counts them),
## info as the log. An escalation updates its entry instead of adding one.
## Entries are grouped Kritisk → Advarsel → Info, newest first, and each one
## shows what happened, where and when — the message is visible, not hidden
## behind a click (the old drawer listed rows that only said "Advarsel") —
## and, for a warning or critical, the verb that answers it.
## Opening the drawer marks everything read.
##
## Entry dict: {id, key, type, icon, title, description, day, severity
## ("info" | "warning" | "critical"), subject, counter_verb, event, unread}.

extends Control

signal panel_toggled(is_open: bool)
## The unread warnings and criticals changed (the HUD badge listens).
signal unread_changed(count: int, has_critical: bool)

# ── Constants ──────────────────────────────────────────────────────────────────

const INBOX_GROUP := &"event_inbox"
const MAX_ENTRIES := 30
## Lines of a collapsed entry's message (a click shows all of it).
const COLLAPSED_LINES := 2
## Space between the open drawer and the toast stack beside it.
const TOAST_GAP := 12.0
## Moved beside the drawer, the stack also rises above the bottom-centre band
## (the fjord camera hint and its "Fly til merd" button, 104 px tall).
const TOAST_LIFT := 112.0

# Lower rank = higher priority. Entries are grouped/sorted critical → warning → info.
## A warning or critical counts as "now" while its event key was reported
## within this many days (a bloom or lice over the limit is reported every
## day it lasts); older entries are history ("Tidligere"), muted and outside
## the badge. Before, every entry stayed "Kritisk"/"Advarsel" until cleared:
## the first season ended with 2 critical and 18-23 warnings, mostly long
## over (SHIP_PLAN cycle 2: alarm fatigue).
const LIVE_DAYS := 2
## The history group's rank: after Kritisk, Advarsel and Info.
const HISTORY_RANK := 3
const SEVERITY_RANK := {
	"critical": 0,
	"warning":  1,
	"info":     2,
}

# ── Scene refs ────────────────────────────────────────────────────────────────

@onready var panel: PanelContainer = %Panel
@onready var entry_list: VBoxContainer = %EntryList
@onready var close_button: Button = %CloseButton
@onready var anim_player: AnimationPlayer = %AnimPlayer
@onready var scroll_container: ScrollContainer = %ScrollContainer
@onready var _title_label: Label = %TitleLabel

# ── State ──────────────────────────────────────────────────────────────────────

var _sim: Node = null
var _theme: Node = null
var _entries: Array[Dictionary] = []
var _is_open: bool = false
var _expanded_id: String = ""
var _slide_tween: Tween = null
var _clear_button: Button = null
## The toast-stack room last asked for (open drawer and its left edge).
var _toast_room_open: bool = false
var _toast_room_left: float = -1.0
## The event bus's generation the entries belong to (a new game changes it).
var _generation: int = -1

# ── Lifecycle ──────────────────────────────────────────────────────────────────

func _ready() -> void:
	add_to_group(INBOX_GROUP)
	_sim = get_node_or_null("/root/SimBridge")
	_theme = get_node_or_null("/root/LakskongenTheme")
	if _sim and _sim.has_signal("game_event_raised"):
		_sim.game_event_raised.connect(_on_game_event)
	# Entries age into history as days pass, with no new event to redraw them.
	if _sim and _sim.has_signal("day_advanced"):
		_sim.day_advanced.connect(_on_day_advanced)
	# A new or loaded game starts with an empty inbox (the bus starts empty too).
	var bridge := get_node_or_null("/root/GameBridge")
	if bridge != null:
		bridge.GameStarted.connect(_on_game_started)
		_generation = int(bridge.GameEventGeneration)

	if close_button:
		close_button.pressed.connect(hide_panel)
	_ensure_clear_button()
	_apply_localization()
	_apply_theme_fonts()
	if panel:
		panel.hide()
		# The drawer's header is never covered by a toast (U2-06).
		var header := _title_label.get_parent() as Control if _title_label else null
		if header != null:
			header.add_to_group(&"toast_keep_clear")
	_rebuild_list()

func _input(event: InputEvent) -> void:
	# V types into a focused text field (the panel's key used to eat the letter).
	if event.is_action_pressed("toggle_notifications") and not UIPolish.is_typing(self):
		toggle()
		get_viewport().set_input_as_handled()
		return

	# ESC closes an open panel the way the X button does. The root stays
	# visible (and mouse-transparent): hiding it disabled the V key and the
	# bell for the rest of the session.
	if _is_open and event.is_action_pressed("ui_cancel"):
		hide_panel()
		get_viewport().set_input_as_handled()

# ── Scene wiring ──────────────────────────────────────────────────────────────

func _apply_localization() -> void:
	if _title_label:
		_title_label.text = tr("UI_NOTIFY_TITLE")
	if close_button:
		close_button.tooltip_text = tr("UI_CLOSE")

func _apply_theme_fonts() -> void:
	if _title_label:
		LakskongenTheme.set_strong(_title_label)

## The scene ships without a clear-all button; add one to the header if absent.
func _ensure_clear_button() -> void:
	if not _title_label:
		return
	var existing := get_node_or_null("%ClearAllButton")
	if existing is Button:
		_clear_button = existing
	else:
		var header := _title_label.get_parent()
		if header == null:
			return
		_clear_button = Button.new()
		_clear_button.name = "ClearAllButton"
		_clear_button.theme_type_variation = &"SmallGhostButton"
		header.add_child(_clear_button)
		header.move_child(_clear_button, _title_label.get_index() + 1)
	if _clear_button:
		_clear_button.text = tr("UI_NOTIFY_CLEAR_ALL")
		_clear_button.tooltip_text = tr("UI_NOTIFY_CLEAR_ALL_TIP")
		if not _clear_button.pressed.is_connected(clear_all):
			_clear_button.pressed.connect(clear_all)

# ── Public API ─────────────────────────────────────────────────────────────────

## An event from the typed bus: a new entry, or (an escalation of an event
## already listed) the same entry raised to the new severity.
func add_event(event: Dictionary) -> void:
	_follow_generation(int(event.get("generation", _generation)))
	var severity := str(event.get("severity", "info"))
	# Unread = news the player was told about (a toast or a card); history
	# from days skipped at once is logged as read.
	var today := int(_sim.get("current_day")) if _sim != null else int(event.get("day", 0))
	var bridge := get_node_or_null("/root/GameBridge")
	if bridge != null:
		today = int(bridge.CurrentDay)
	var told := ConsequenceDirector.route_for(event, today) != ConsequenceDirector.CHANNEL_LOG
	var entry := {
		"id": str(event.get("id", "")),
		"key": str(event.get("key", "")),
		"type": str(event.get("type", "")),
		"icon": GameEventText.glyph(event),
		"title": GameEventText.title(event),
		"description": str(event.get("message", "")),
		"day": int(event.get("day", 0)),
		"severity": severity,
		"subject": GameEventText.subject(event, _sim),
		"counter_verb": str(event.get("counter_verb", "none")),
		"event": event,
		"unread": told and not _is_open,
	}
	if bool(event.get("escalation", false)):
		for i in _entries.size():
			if str(_entries[i].get("key", "")) == entry["key"]:
				_entries.remove_at(i)
				break
	_entries.push_front(entry)
	if _entries.size() > MAX_ENTRIES:
		_entries.resize(MAX_ENTRIES)
	_rebuild_list()
	_emit_unread()

## Add an entry by hand (not from the bus): kept for callers outside the bus.
func add_entry(icon: String, title: String, description: String,
			   day: int, severity: String = "info") -> void:
	_entries.push_front({
		"id": "", "key": "", "type": "", "icon": icon, "title": title, "description": description,
		"day": day, "severity": severity, "subject": "", "counter_verb": "none", "event": {},
		"unread": severity != "info" and not _is_open,
	})
	if _entries.size() > MAX_ENTRIES:
		_entries.resize(MAX_ENTRIES)
	_rebuild_list()
	_emit_unread()

## Unread warnings and criticals (the HUD bell's badge).
func unread_count() -> int:
	var seen := _last_seen()
	var n := 0
	for e in _entries:
		if bool(e.get("unread", false)) and _is_live(e, seen):
			n += 1
	return n

func has_unread_critical() -> bool:
	var seen := _last_seen()
	for e in _entries:
		if bool(e.get("unread", false)) and str(e.get("severity", "")) == "critical" and _is_live(e, seen):
			return true
	return false


## {event key: last day reported} from the engine's bus ({} without one).
func _last_seen() -> Dictionary:
	var bridge := get_node_or_null("/root/GameBridge")
	if bridge == null or not bridge.has_method("GetEventKeysLastSeen"):
		return {}
	return bridge.GetEventKeysLastSeen()


func _today() -> int:
	var bridge := get_node_or_null("/root/GameBridge")
	if bridge != null:
		return int(bridge.CurrentDay)
	return int(_sim.get("current_day")) if _sim != null else 0


## Whether `entry` still needs the player: info is the log (never "now");
## a warning or critical is live while its key was reported within LIVE_DAYS
## (its own day when the bus has forgotten the key, or for hand-made entries).
func _is_live(entry: Dictionary, seen: Dictionary) -> bool:
	var sev := str(entry.get("severity", "info"))
	if sev != "warning" and sev != "critical":
		return false
	var key := str(entry.get("key", ""))
	var last := int(seen.get(key, entry.get("day", 0))) if key != "" else int(entry.get("day", 0))
	return _today() - last <= LIVE_DAYS


## The group an entry is listed under: its severity's while live, history after.
func _group_rank(entry: Dictionary, seen: Dictionary) -> int:
	var rank := _severity_rank(str(entry.get("severity", "info")))
	if rank <= 1 and not _is_live(entry, seen):
		return HISTORY_RANK
	return rank


func _on_day_advanced(_day: int = 0, _season: String = "") -> void:
	_rebuild_list()
	_emit_unread()

## An event the player has seen elsewhere (its critical card).
func mark_read(event_id: String) -> void:
	for e in _entries:
		if str(e.get("id", "")) == event_id and bool(e.get("unread", false)):
			e["unread"] = false
			_rebuild_list()
			_emit_unread()
			return

func mark_all_read() -> void:
	var changed := false
	for e in _entries:
		if bool(e.get("unread", false)):
			e["unread"] = false
			changed = true
	if changed:
		_emit_unread()

## Remove every stored notification and refresh the (empty) list.
func clear_all() -> void:
	if _entries.is_empty():
		return
	_entries.clear()
	_expanded_id = ""
	_rebuild_list()
	_emit_unread()

func is_open() -> bool:
	return _is_open

func toggle() -> void:
	if _is_open:
		hide_panel()
	else:
		show_panel()
		# U2-02: the player opened the inbox (the bell or V).
		var sim := get_node_or_null("/root/SimBridge")
		if sim != null:
			sim.tutorial_mark("inbox")

func show_panel() -> void:
	if _is_open:
		return
	_is_open = true
	panel.show()
	_sync_toast_room()
	if anim_player and anim_player.has_animation("slide_in"):
		anim_player.play("slide_in")
	else:
		_slide_in()
	panel_toggled.emit(true)
	# Seen: the badge goes at once; the entries that were new keep their dot
	# while the drawer stays open, so the player sees which ones they are.
	for e in _entries:
		if bool(e.get("unread", false)):
			e["seen_open"] = true
	mark_all_read()
	_rebuild_now()

func hide_panel() -> void:
	if not _is_open:
		return
	_is_open = false
	_sync_toast_room()
	if anim_player and anim_player.has_animation("slide_out"):
		anim_player.play("slide_out")
		await anim_player.animation_finished
	else:
		await _slide_out()
	panel.hide()
	panel_toggled.emit(false)
	for e in _entries:
		e.erase("seen_open")
	_rebuild_list()

func _emit_unread() -> void:
	unread_changed.emit(unread_count(), has_unread_critical())

## Screen regions (roadmap U1-04): the open history is a drawer on the right
## edge, under the tab rail; the toast stack moves left of it while it is open
## and on screen, and back to the corner when it closes, is hidden with the
## game UI (fjord views, photo shots) or the game scene goes away. Checked
## every frame, so a UI-scale change moves the stack with the drawer.
func _process(_delta: float) -> void:
	_sync_toast_room()

func _sync_toast_room() -> void:
	var open := _is_open and panel != null and panel.is_visible_in_tree()
	var drawer_left := get_global_rect().position.x if open else -1.0
	if open == _toast_room_open and is_equal_approx(drawer_left, _toast_room_left):
		return
	_toast_room_open = open
	_toast_room_left = drawer_left
	var toasts := get_node_or_null("/root/ToastNotification")
	if toasts == null or not toasts.has_method("set_right_inset"):
		return
	var inset := 0.0
	var lift := 0.0
	if open:
		inset = get_viewport_rect().size.x - drawer_left - float(toasts.STACK_MARGIN_RIGHT) + TOAST_GAP
		lift = TOAST_LIFT
	toasts.set_right_inset(inset, lift)

func _exit_tree() -> void:
	if _toast_room_open:
		_is_open = false
		_sync_toast_room()

## Slide-in juice (display only): fade + rise from the right edge. Reduced
## motion keeps the fade and drops the slide.
func _slide_in() -> void:
	if _slide_tween and _slide_tween.is_valid():
		_slide_tween.kill()
	var still := MotionPrefs.reduced_motion()
	panel.modulate.a = 0.0
	panel.position.x = 0.0 if still else 24.0
	_slide_tween = panel.create_tween()
	_slide_tween.set_ease(Tween.EASE_OUT).set_trans(Tween.TRANS_CUBIC)
	_slide_tween.set_parallel(true)
	_slide_tween.tween_property(panel, "modulate:a", 1.0, 0.25)
	if not still:
		_slide_tween.tween_property(panel, "position:x", 0.0, 0.25)

func _slide_out() -> void:
	if _slide_tween and _slide_tween.is_valid():
		_slide_tween.kill()
	_slide_tween = panel.create_tween()
	_slide_tween.set_ease(Tween.EASE_IN).set_trans(Tween.TRANS_CUBIC)
	_slide_tween.set_parallel(true)
	_slide_tween.tween_property(panel, "modulate:a", 0.0, 0.2)
	if not MotionPrefs.reduced_motion():
		_slide_tween.tween_property(panel, "position:x", 24.0, 0.2)
	await _slide_tween.finished
	panel.position.x = 0.0
	panel.modulate.a = 1.0

# ── SimBridge signal handlers ──────────────────────────────────────────────────

func _on_game_event(event: Dictionary) -> void:
	add_event(event)

## A new or loaded game: the entries of the old one go. By generation, not
## by the signal alone: the new game's first events may arrive before
## GameStarted reaches this node, and they must stay.
func _on_game_started() -> void:
	var bridge := get_node_or_null("/root/GameBridge")
	if bridge != null:
		_follow_generation(int(bridge.GameEventGeneration))

func _follow_generation(generation: int) -> void:
	if generation == _generation:
		return
	_generation = generation
	if _entries.is_empty():
		return
	_entries.clear()
	_expanded_id = ""
	_rebuild_list()
	_emit_unread()

# ── List rendering ─────────────────────────────────────────────────────────────

## The list is drawn while the drawer is open, at most once a frame; while it
## is shut an event only marks it stale, and opening draws it (U2-07: every
## event rebuilt every entry, 1 ms per entry listed, so with a season's log a
## warning cost 20-40 ms of a 30× frame).
var _rebuild_queued: bool = false
## Times the list was drawn (tests).
var list_builds: int = 0

func _rebuild_list() -> void:
	if not _is_open:
		return
	if _rebuild_queued:
		return
	_rebuild_queued = true
	_rebuild_now.call_deferred()

func _rebuild_now() -> void:
	_rebuild_queued = false
	if not entry_list:
		return
	list_builds += 1
	for child in entry_list.get_children():
		entry_list.remove_child(child)
		child.queue_free()

	if _entries.is_empty():
		UIPolish.show_empty_state_localized(entry_list, tr("UI_NOTIFY_EMPTY"), 12, "", _theme)
		return

	var seen := _last_seen()
	var ordered: Array = []
	for i in _entries.size():
		ordered.append({"entry": _entries[i], "index": i, "rank": _group_rank(_entries[i], seen)})
	# Group: what needs the player now (critical → warning), the log (info),
	# then history (warnings and criticals that are over); newest first within.
	ordered.sort_custom(func(a: Dictionary, b: Dictionary) -> bool:
		if int(a["rank"]) != int(b["rank"]):
			return int(a["rank"]) < int(b["rank"])
		return int(a["index"]) < int(b["index"])
	)
	var counts := {}
	for o in ordered:
		counts[int(o["rank"])] = int(counts.get(int(o["rank"]), 0)) + 1

	var last_rank := -1
	for i in ordered.size():
		var entry: Dictionary = ordered[i]["entry"]
		var rank := int(ordered[i]["rank"])
		if rank != last_rank:
			entry_list.add_child(_build_group_header(rank, int(counts.get(rank, 0))))
			last_rank = rank
		var node := _build_entry_node(entry, int(ordered[i]["index"]))
		if rank == HISTORY_RANK:
			node.modulate.a = 0.62   # over: still readable, no longer a call to act
		entry_list.add_child(node)

	if scroll_container:
		scroll_container.scroll_vertical = 0
	UIPolish.link_vertical_focus(entry_list)

func _build_entry_node(entry: Dictionary, index: int) -> Control:
	var sev: String = str(entry.get("severity", "info"))
	var tone: StringName = GameEventText.TONES.get(sev, &"Info")
	var expanded := str(entry.get("id", "")) != "" and _expanded_id == str(entry.get("id", ""))

	var card := PanelContainer.new()
	card.name = "Entry_%d" % index
	card.theme_type_variation = StringName("Callout" + String(tone))

	# The click target sits behind the content, so the verb button on top
	# still takes its own clicks.
	var hit := Button.new()
	hit.name = "Expand"
	hit.flat = true
	hit.focus_mode = Control.FOCUS_ALL
	hit.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	hit.tooltip_text = tr("UI_NOTIFY_EXPAND_TIP")
	var empty_style := StyleBoxEmpty.new()
	for state in ["normal", "hover", "pressed", "focus", "hover_pressed"]:
		hit.add_theme_stylebox_override(state, empty_style)
	var entry_id := str(entry.get("id", ""))
	hit.pressed.connect(func() -> void: _toggle_expand(entry_id))
	card.add_child(hit)

	var body := VBoxContainer.new()
	body.mouse_filter = Control.MOUSE_FILTER_IGNORE
	body.add_theme_constant_override("separation", 3)
	card.add_child(body)

	# Header: [glyph] title · unread dot · day
	var header := HBoxContainer.new()
	header.mouse_filter = Control.MOUSE_FILTER_IGNORE
	header.add_theme_constant_override("separation", 8)
	body.add_child(header)

	# Severity chip: the glyph in Ink on a solid tone fill (D13's "Ink on a
	# tone"; a tinted pill read 4.1:1 for the info "i").
	var badge := PanelContainer.new()
	badge.theme_type_variation = StringName("Swatch" + String(tone))
	badge.custom_minimum_size = Vector2(22, 20)
	badge.size_flags_vertical = Control.SIZE_SHRINK_BEGIN
	badge.mouse_filter = Control.MOUSE_FILTER_IGNORE
	header.add_child(badge)
	var icon_lbl := Label.new()
	icon_lbl.name = "Glyph"
	icon_lbl.text = str(entry.get("icon", "i"))
	icon_lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	icon_lbl.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	icon_lbl.theme_type_variation = &"Caption"
	icon_lbl.add_theme_color_override("font_color", get_theme_color("on_salmon", "Palette"))
	badge.add_child(icon_lbl)

	var title_lbl := Label.new()
	title_lbl.name = "Title"
	title_lbl.text = str(entry.get("title", ""))
	title_lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	title_lbl.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	title_lbl.theme_type_variation = &"BodySmallStrong"
	header.add_child(title_lbl)

	if bool(entry.get("unread", false)) or bool(entry.get("seen_open", false)):
		var dot := Label.new()
		dot.name = "UnreadDot"
		dot.text = "●"
		dot.tooltip_text = tr("UI_NOTIFY_UNREAD")
		dot.theme_type_variation = &"CaptionAccent"
		dot.size_flags_vertical = Control.SIZE_SHRINK_BEGIN
		header.add_child(dot)

	var day_lbl := Label.new()
	day_lbl.name = "Day"
	day_lbl.text = tr("UI_EVENT_DAY") % int(entry.get("day", 0))
	day_lbl.theme_type_variation = &"CaptionMuted"
	day_lbl.size_flags_vertical = Control.SIZE_SHRINK_BEGIN
	header.add_child(day_lbl)

	var subject := str(entry.get("subject", ""))
	if subject != "":
		var subject_lbl := Label.new()
		subject_lbl.name = "Subject"
		subject_lbl.text = subject
		subject_lbl.theme_type_variation = &"CaptionMuted"
		body.add_child(subject_lbl)

	var description := str(entry.get("description", ""))
	if description != "":
		var desc_lbl := Label.new()
		desc_lbl.name = "Desc"
		desc_lbl.text = description
		desc_lbl.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		desc_lbl.theme_type_variation = &"BodySmallMuted"
		if not expanded:
			desc_lbl.max_lines_visible = COLLAPSED_LINES
			desc_lbl.text_overrun_behavior = TextServer.OVERRUN_TRIM_ELLIPSIS
		body.add_child(desc_lbl)

	var event: Dictionary = entry.get("event", {})
	if sev != "info" and not event.is_empty() and GameEventText.has_verb(event):
		var row := HBoxContainer.new()
		row.mouse_filter = Control.MOUSE_FILTER_IGNORE
		body.add_child(row)
		var verb := Button.new()
		verb.name = "VerbButton"
		verb.text = GameEventText.verb_label(str(event.get("counter_verb", "")))
		verb.theme_type_variation = &"SmallSecondaryButton"
		verb.pressed.connect(func() -> void: _perform(event))
		row.add_child(verb)
	return card

func _perform(event: Dictionary) -> void:
	if GameEventActions.perform(event, owner):
		hide_panel()

func _toggle_expand(entry_id: String) -> void:
	_expanded_id = "" if _expanded_id == entry_id else entry_id
	_rebuild_list()

# ── Utility ────────────────────────────────────────────────────────────────────

func _severity_rank(severity: String) -> int:
	return SEVERITY_RANK.get(severity.to_lower(), 99)

func _rank_to_severity(rank: int) -> String:
	match rank:
		0: return "critical"
		1: return "warning"
		_: return "info"

## "KRITISK · 2"
func _build_group_header(rank: int, count: int) -> Control:
	if rank == HISTORY_RANK:
		var past := Label.new()
		past.name = "Group_history"
		past.text = "%s · %d" % [tr("UI_NOTIFY_GROUP_HISTORY"), count]
		past.theme_type_variation = &"OverlineMuted"
		past.uppercase = true
		past.mouse_filter = Control.MOUSE_FILTER_IGNORE
		return past
	var sev := _rank_to_severity(rank)
	var lbl := Label.new()
	lbl.name = "Group_" + sev
	lbl.text = "%s · %d" % [tr("UI_NOTIFY_GROUP_%s" % sev.to_upper()), count]
	lbl.theme_type_variation = StringName("Overline" + String(GameEventText.TONES.get(sev, &"Info")))
	lbl.uppercase = true
	lbl.mouse_filter = Control.MOUSE_FILTER_IGNORE
	return lbl
