extends Control

## NotificationCenter — slide-in panel (anchored right) for event history.
## Displays up to MAX_ENTRIES notifications, newest on top.
## Auto-shows on critical alerts; player can toggle with N key or a button.
##
## Each entry dict shape:
##   { "icon": String, "title": String, "description": String,
##     "day": int, "severity": String }   # severity: "info"|"warning"|"critical"
##
## Expected node layout (set up in .tscn):
##   NotificationCenter (Control)
##   └─ PanelContainer
##      ├─ VBoxContainer
##      │  ├─ HeaderRow (HBoxContainer)
##      │  │  ├─ TitleLabel (Label)  "Varsler"
##      │  │  └─ CloseButton (Button)  "✕"
##      │  └─ ScrollContainer
##      │     └─ EntryList (VBoxContainer)  ← entries added here
##      └─ (AnimationPlayer — "slide_in" / "slide_out" animations)

signal panel_toggled(is_open: bool)

# ── Constants ──────────────────────────────────────────────────────────────────

const MAX_ENTRIES := 20

const SEVERITY_COLORS := {
	"info":     Color("#A3D1DE"),  # light blue
	"warning":  Color(1.00, 0.80, 0.10),  # yellow
	"critical": Color("#BF616A"),  # red
}

const SEVERITY_ICONS := {
	"info":     "ℹ",
	"warning":  "⚠",
	"critical": "🚨",
}

# ── Node references ────────────────────────────────────────────────────────────

@onready var panel:          Control         = $PanelContainer
@onready var entry_list:     VBoxContainer   = $PanelContainer/VBoxContainer/ScrollContainer/EntryList
@onready var close_button:   Button          = $PanelContainer/VBoxContainer/HeaderRow/CloseButton
@onready var anim_player:    AnimationPlayer = $PanelContainer/AnimationPlayer
@onready var scroll_container: ScrollContainer = $PanelContainer/VBoxContainer/ScrollContainer

# ── State ──────────────────────────────────────────────────────────────────────

var _sim: Node = null
var _entries: Array[Dictionary] = []
var _is_open: bool = false
var _expanded_index: int = -1   # which entry is currently expanded (-1 = none)

# ── Lifecycle ──────────────────────────────────────────────────────────────────

func _ready() -> void:
	_sim = get_node_or_null("/root/SimBridge")
	if _sim:
		_sim.alert_raised.connect(_on_alert_raised)
		_sim.state_updated.connect(_on_state_updated)

	close_button.pressed.connect(hide_panel)
	panel.hide()
	print("NotificationCenter: ready")

func _input(event: InputEvent) -> void:
	if event.is_action_pressed("toggle_notifications"):
		toggle()
		get_viewport().set_input_as_handled()
		return
	
	if visible and event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()

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
	panel_toggled.emit(true)

func hide_panel() -> void:
	if not _is_open:
		return
	_is_open = false
	if anim_player and anim_player.has_animation("slide_out"):
		anim_player.play("slide_out")
		await anim_player.animation_finished
	panel.hide()
	panel_toggled.emit(false)

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
	# Clear existing children
	for child in entry_list.get_children():
		child.queue_free()

	for i in _entries.size():
		var entry := _entries[i]
		entry_list.add_child(_build_entry_node(entry, i))

	# Scroll to top (newest entry)
	await get_tree().process_frame
	scroll_container.scroll_vertical = 0

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
	icon_lbl.add_theme_color_override("font_color", color)
	header.add_child(icon_lbl)

	var title_lbl := Label.new()
	title_lbl.text = entry.get("title", "")
	title_lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	title_lbl.add_theme_color_override("font_color", Color.WHITE)
	title_lbl.add_theme_font_size_override("font_size", 12)
	header.add_child(title_lbl)

	var day_lbl := Label.new()
	day_lbl.text = tr("ui.notification.day_format") % entry.get("day", 0)
	day_lbl.add_theme_color_override("font_color", Color("#7B8794"))
	day_lbl.add_theme_font_size_override("font_size", 10)
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
