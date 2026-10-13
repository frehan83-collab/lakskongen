## NotificationCenter v2.1 — Nordic Minimalist (scene-based).
## Slide-in panel (anchored right) for event history.
## Layout lives in scenes/ui/NotificationCenter.tscn; this script is wiring only:
## @onready % refs, signal hookups, localization, theme fonts, entry list.
## Displays up to MAX_ENTRIES notifications, newest on top.
## Auto-shows on critical alerts; player can toggle with N key or a button.
##
## Each entry dict shape:
##   { "icon": String, "title": String, "description": String,
##     "day": int, "severity": String }   # severity: "info"|"warning"|"critical"

extends Control

signal panel_toggled(is_open: bool)

# ── Constants ──────────────────────────────────────────────────────────────────

const MAX_ENTRIES := 20

const SEVERITY_COLORS := {
	"info":     Color("#A3D1DE"),  # light blue
	"warning":  Color(1.00, 0.80, 0.10),  # yellow
	"critical": Color("#BF616A"),  # red
}

# Readable TEXT/glyph variants of the severity palette. The bright fills above
# are decorative (rails/borders); as text on the white cards they fail contrast
# (yellow ≈1.5:1, light blue ≈1.4:1). "⚠"/"ℹ" are text glyphs, not emoji, so
# they need a readable colour too (S3 a11y audit).
const SEVERITY_TEXT_COLORS := {
	"info":     Color("#0A4D68"),  # primary — 9.2:1 on white
	"warning":  Color("#9A7B1F"),  # matches AttentionInbox high — 4.9:1
	"critical": Color("#A54E56"),  # danger-dark — 5.1:1
}

const SEVERITY_ICONS := {
	"info":     "ℹ",
	"warning":  "⚠",
	"critical": "🚨",
}

# Lower rank = higher priority. Entries are grouped/sorted critical → warning → info.
const SEVERITY_RANK := {
	"critical": 0,
	"warning":  1,
	"info":     2,
}

const GROUP_TITLES := {
	"critical": "Kritisk",
	"warning":  "Advarsel",
	"info":     "Info",
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
var _expanded_index: int = -1   # which entry is currently expanded (-1 = none)
var _slide_tween: Tween = null
var _clear_button: Button = null

# ── Lifecycle ──────────────────────────────────────────────────────────────────

func _ready() -> void:
	_sim = get_node_or_null("/root/SimBridge")
	_theme = get_node_or_null("/root/LakskongenTheme")
	if _sim:
		if _sim.has_signal("alert_raised"):
			_sim.alert_raised.connect(_on_alert_raised)
		if _sim.has_signal("state_updated"):
			_sim.state_updated.connect(_on_state_updated)

	if close_button:
		close_button.pressed.connect(hide_panel)
	_ensure_clear_button()
	_apply_localization()
	_apply_theme_fonts()
	if panel:
		panel.hide()

func _input(event: InputEvent) -> void:
	if event.is_action_pressed("toggle_notifications"):
		toggle()
		get_viewport().set_input_as_handled()
		return

	# ESC closes an open panel the way the X button does. The root stays
	# visible (and mouse-transparent): hiding it disabled the N key and
	# critical auto-open for the rest of the session.
	if _is_open and event.is_action_pressed("ui_cancel"):
		hide_panel()
		get_viewport().set_input_as_handled()

# ── Scene wiring ──────────────────────────────────────────────────────────────

func _apply_localization() -> void:
	if _title_label:
		_title_label.text = tr("UI_NOTIFY_TITLE")
	if close_button:
		close_button.tooltip_text = tr("UI_NOTIFY_TITLE")

func _apply_theme_fonts() -> void:
	if not _theme or not _theme.font_heading_semibold:
		return
	if _title_label:
		_title_label.add_theme_font_override("font", _theme.font_heading_semibold)

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
		header.add_child(_clear_button)
	if _clear_button:
		_clear_button.text = _tr_or("UI_NOTIFY_CLEAR_ALL", "Tøm")
		_clear_button.tooltip_text = _tr_or("UI_NOTIFY_CLEAR_ALL", "Tøm alle varsler")
		if not _clear_button.pressed.is_connected(clear_all):
			_clear_button.pressed.connect(clear_all)

# ── Public API ─────────────────────────────────────────────────────────────────

## Add an entry programmatically (e.g. from ToastNotification or game systems).
func add_entry(icon: String, title: String, description: String,
			   day: int, severity: String = "info") -> void:
	var entry := {
		"icon": icon,
		"title": title,
		"description": description,
		"day": day,
		"severity": severity,
	}
	_entries.push_front(entry)
	if _entries.size() > MAX_ENTRIES:
		_entries.resize(MAX_ENTRIES)
	_rebuild_list()

	if severity == "critical" and not _is_open:
		show_panel()

## Remove every stored notification and refresh the (empty) list.
func clear_all() -> void:
	if _entries.is_empty():
		return
	_entries.clear()
	_expanded_index = -1
	_rebuild_list()

func toggle() -> void:
	if _is_open:
		hide_panel()
	else:
		show_panel()

func show_panel() -> void:
	if _is_open:
		return
	_is_open = true
	panel.show()
	if anim_player and anim_player.has_animation("slide_in"):
		anim_player.play("slide_in")
	else:
		_slide_in()
	panel_toggled.emit(true)

func hide_panel() -> void:
	if not _is_open:
		return
	_is_open = false
	if anim_player and anim_player.has_animation("slide_out"):
		anim_player.play("slide_out")
		await anim_player.animation_finished
	else:
		await _slide_out()
	panel.hide()
	panel_toggled.emit(false)

## Slide-in juice (display only): fade + rise from the right edge.
func _slide_in() -> void:
	if _slide_tween and _slide_tween.is_valid():
		_slide_tween.kill()
	panel.modulate.a = 0.0
	panel.position.x = 24.0
	_slide_tween = panel.create_tween()
	_slide_tween.set_ease(Tween.EASE_OUT).set_trans(Tween.TRANS_CUBIC)
	_slide_tween.set_parallel(true)
	_slide_tween.tween_property(panel, "modulate:a", 1.0, 0.25)
	_slide_tween.tween_property(panel, "position:x", 0.0, 0.25)

func _slide_out() -> void:
	if _slide_tween and _slide_tween.is_valid():
		_slide_tween.kill()
	_slide_tween = panel.create_tween()
	_slide_tween.set_ease(Tween.EASE_IN).set_trans(Tween.TRANS_CUBIC)
	_slide_tween.set_parallel(true)
	_slide_tween.tween_property(panel, "modulate:a", 0.0, 0.2)
	_slide_tween.tween_property(panel, "position:x", 24.0, 0.2)
	await _slide_tween.finished
	panel.position.x = 0.0
	panel.modulate.a = 1.0

# ── SimBridge signal handlers ──────────────────────────────────────────────────

func _on_alert_raised(level: String, message: String) -> void:
	var icon: String = SEVERITY_ICONS.get(level, "ℹ")
	add_entry(icon, _level_to_title(level), message,
			  _sim.current_day if _sim else 0, level)

func _on_state_updated() -> void:
	if not _sim:
		return
	# Ingest new events from SimBridge.recent_events that aren't already logged
	for ev in _sim.recent_events:
		var already_logged := false
		for logged in _entries:
			if logged.get("day") == ev.get("day", 0) \
					and logged.get("title") == ev.get("name", ""):
				already_logged = true
				break
		if not already_logged:
			var sev: String = _category_to_severity(ev.get("category", ""))
			add_entry(
				SEVERITY_ICONS.get(sev, "ℹ"),
				ev.get("name", "Hendelse"),
				ev.get("description", ""),
				ev.get("day", 0),
				sev
			)

# ── List rendering ─────────────────────────────────────────────────────────────

func _rebuild_list() -> void:
	if not entry_list:
		return
	# Clear existing children
	for child in entry_list.get_children():
		child.queue_free()

	if _entries.is_empty():
		UIPolish.show_empty_state_localized(entry_list, _tr_or("UI_NOTIFY_EMPTY", "🔔 Ingen varsler ennå — vi gir beskjed når noe skjer."), 12, "", _theme)
		return

	var ordered: Array = []
	for i in _entries.size():
		ordered.append({"entry": _entries[i], "index": i})
	# Group by severity (critical → warning → info); newest first within a group.
	ordered.sort_custom(func(a: Dictionary, b: Dictionary) -> bool:
		var ra := _severity_rank(str(a["entry"].get("severity", "info")))
		var rb := _severity_rank(str(b["entry"].get("severity", "info")))
		if ra != rb:
			return ra < rb
		return int(a["index"]) < int(b["index"])
	)

	var last_rank := -1
	for i in ordered.size():
		var entry: Dictionary = ordered[i]["entry"]
		var rank := _severity_rank(str(entry.get("severity", "info")))
		if rank != last_rank:
			entry_list.add_child(_build_group_header(rank))
			last_rank = rank
		entry_list.add_child(_build_entry_node(entry, i))

	# Scroll to top (newest entry)
	await get_tree().process_frame
	if scroll_container:
		scroll_container.scroll_vertical = 0

	# UIPolish: vertical keyboard navigation for notification entries
	UIPolish.link_vertical_focus(entry_list)

func _build_entry_node(entry: Dictionary, index: int) -> Control:
	var sev: String   = entry.get("severity", "info")
	var color: Color  = SEVERITY_COLORS.get(sev, SEVERITY_COLORS["info"])
	var is_expanded   := (_expanded_index == index)

	# Outer panel
	var panel_node := PanelContainer.new()
	panel_node.name = "Entry_%d" % index

	# Style the panel border to match severity color
	var style := StyleBoxFlat.new()
	style.border_color = color
	style.set_border_width_all(0)
	style.border_width_left = 3
	style.bg_color = Color("#FFFFFF")
	style.set_corner_radius_all(4)
	panel_node.add_theme_stylebox_override("panel", style)

	var vbox := VBoxContainer.new()
	vbox.add_theme_constant_override("separation", 2)
	panel_node.add_child(vbox)

	# Header row: [icon] [title]  [day]
	var header := HBoxContainer.new()
	vbox.add_child(header)

	var icon_lbl := Label.new()
	icon_lbl.text = entry.get("icon", "ℹ")
	icon_lbl.custom_minimum_size = Vector2(24, 0)
	icon_lbl.add_theme_color_override("font_color", SEVERITY_TEXT_COLORS.get(sev, SEVERITY_TEXT_COLORS["info"]))
	header.add_child(icon_lbl)

	var title_lbl := Label.new()
	title_lbl.text = entry.get("title", "")
	title_lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	title_lbl.add_theme_color_override("font_color", Color("#2E3440"))
	title_lbl.add_theme_font_size_override("font_size", 12)
	header.add_child(title_lbl)

	var day_lbl := Label.new()
	day_lbl.text = _tr_day_format(entry.get("day", 0))
	day_lbl.add_theme_color_override("font_color", Color("#7B8794"))
	day_lbl.add_theme_font_size_override("font_size", 11)
	header.add_child(day_lbl)

	# Description (shown when expanded)
	var desc_lbl := Label.new()
	desc_lbl.text = entry.get("description", "")
	desc_lbl.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	desc_lbl.add_theme_color_override("font_color", Color("#4C566A"))
	desc_lbl.add_theme_font_size_override("font_size", 11)
	desc_lbl.visible = is_expanded
	desc_lbl.name = "Desc"
	vbox.add_child(desc_lbl)

	# Click anywhere on header to toggle expand
	var btn := Button.new()
	btn.flat = true
	btn.text = ""
	btn.custom_minimum_size = Vector2(0, 36)
	# Stretch button over entire panel via script after adding
	# Use a simple approach: capture the index in a lambda
	var captured_index := index
	btn.pressed.connect(func(): _toggle_expand(captured_index))
	# Place button as a child that overlays (set it to fill)
	panel_node.add_child(btn)
	btn.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	btn.size_flags_vertical   = Control.SIZE_EXPAND_FILL
	btn.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND

	# Make button transparent but interactive
	var empty_style := StyleBoxEmpty.new()
	btn.add_theme_stylebox_override("normal",   empty_style)
	btn.add_theme_stylebox_override("hover",    empty_style)
	btn.add_theme_stylebox_override("pressed",  empty_style)
	btn.add_theme_stylebox_override("focus",    empty_style)

	return panel_node

func _toggle_expand(index: int) -> void:
	if _expanded_index == index:
		_expanded_index = -1
	else:
		_expanded_index = index
	_rebuild_list()

# ── Utility ────────────────────────────────────────────────────────────────────

func _severity_rank(severity: String) -> int:
	return SEVERITY_RANK.get(severity.to_lower(), 99)

func _rank_to_severity(rank: int) -> String:
	match rank:
		0: return "critical"
		1: return "warning"
		_: return "info"

func _build_group_header(rank: int) -> Control:
	var sev := _rank_to_severity(rank)
	var lbl := Label.new()
	lbl.text = _tr_or("UI_NOTIFY_GROUP_%s" % sev.to_upper(), GROUP_TITLES.get(sev, "Info"))
	lbl.add_theme_font_size_override("font_size", 11)
	lbl.add_theme_color_override("font_color", SEVERITY_TEXT_COLORS.get(sev, SEVERITY_TEXT_COLORS["info"]))
	lbl.mouse_filter = Control.MOUSE_FILTER_IGNORE
	return lbl

## Translated "Day %d" with fallback: tr() returns the key itself when the
## translation catalog is missing — formatting the raw key (no placeholder)
## aborts _build_entry_node and yields a null child for add_child().
func _tr_day_format(day: int) -> String:
	var fmt := tr("UI_NOTIFICATION_DAY_FORMAT")
	if fmt == "UI_NOTIFICATION_DAY_FORMAT" or not ("%" in fmt):
		return "Dag %d" % day
	return fmt % day

func _tr_or(key: String, fallback: String) -> String:
	var t := tr(key)
	return t if t != key else fallback

func _level_to_title(level: String) -> String:
	match level:
		"critical": return "Kritisk varsel"
		"warning":  return "Advarsel"
		_:          return "Informasjon"

func _category_to_severity(category: String) -> String:
	match category.to_lower():
		"disease", "outbreak", "crisis": return "critical"
		"warning", "compliance":          return "warning"
		_:                                return "info"
