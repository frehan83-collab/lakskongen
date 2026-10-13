extends CanvasLayer

## ToastNotification — a premium notification system designed for Godot 4.x.
## It displays transient popups that slide in from the right, show a progress bar,
## and auto-dismiss. A queue system ensures a maximum of 3 visible toasts,
## stacking vertically from the bottom-right corner. When a 4th toast arrives,
## the oldest visible toast slides out first to make room.
##
## This script is intended to be an Autoload (singleton).
##
## Usage (from any script, assuming ToastNotification is an autoload named "ToastManager"):
##   ToastManager.show_toast("🐟 Høsting fullført", "SUCCESS", 4.0)
##   ToastManager.show_toast("🦠 Sykdomsutbrudd! ILA påvist i Sone 3", "CRITICAL")
##   ToastManager.show_toast("Ny oppdatering tilgjengelig", "INFO")

# ── Constants ──────────────────────────────────────────────────────────────────

const SLIDE_IN_OUT_DURATION := 0.3 # seconds for in/out animation (ease-out)
const TOAST_WIDTH           := 380.0
const TOAST_MARGIN          := 15.0 # right/top margin from screen edge
const MAX_VISIBLE_TOASTS    := 3    # Maximum toasts visible at once
const DEDUP_WINDOW_SEC      := 2.5  # Suppress an identical message within this window

# Color definitions for toast levels (Nordic Minimalist palette)
const COLOR_INFO     := Color("#5E81AC") # Steel blue accent
const COLOR_WARNING  := Color("#EBCB8B") # Muted amber
const COLOR_CRITICAL := Color("#BF616A") # Nordic red
const COLOR_SUCCESS  := Color("#A3BE8C") # Aurora green
const COLOR_TEXT     := Color("#ECEFF4") # Snow white

# Toast surface tint (darkened accent keeps text legible)
const TOAST_SURFACE_DARKEN := 0.62

const SEVERITY_ICONS := {
	"INFO":     "ℹ️",
	"WARNING":  "⚠️",
	"CRITICAL": "🔴",
	"SUCCESS":  "✅",
}

# Lower rank = higher priority. The queue drains CRITICAL first.
const SEVERITY_RANK := {
	"CRITICAL": 0,
	"WARNING":  1,
	"SUCCESS":  2,
	"INFO":     3,
}

# ── Inner type for queued toasts ────────────────────────────────────────────────

class QueuedToastData:
	var message:  String
	var level:    String
	var duration: float
	var action:   Callable

	func _init(p_message: String, p_level: String, p_duration: float, p_action: Callable = Callable()) -> void:
		message  = p_message
		level    = p_level
		duration = p_duration
		action   = p_action

# ── Signals ────────────────────────────────────────────────────────────────────

signal toast_shown(message: String, level: String)
signal toast_dismissed(message: String)

# ── State ──────────────────────────────────────────────────────────────────────

var _queue:        Array[QueuedToastData] = []
var _active_nodes: Array[Control]         = [] # Stores active toast nodes
var _toast_list:   VBoxContainer          = null # Container for active toasts
var _recent_messages: Dictionary          = {} # message -> ticks_msec for dedup

# ── Lifecycle ──────────────────────────────────────────────────────────────────

func _ready() -> void:
	layer = 10 # Ensure toasts are on top of other UI
	_build_layout()
	print("ToastNotification: ready")

# ── Public API ─────────────────────────────────────────────────────────────────

## Displays a toast notification.
## message: The main text content of the toast.
## level: "INFO", "WARNING", "CRITICAL", or "SUCCESS". Defaults to "INFO".
## duration: How long the toast stays visible in seconds. Defaults to 5.0.
## action: Optional Callable invoked when the toast body is clicked. Defaults to empty.
## Identical messages inside DEDUP_WINDOW_SEC are suppressed.
func show_toast(message: String, level: String = "INFO", duration: float = 5.0, action: Callable = Callable()) -> void:
	var now := Time.get_ticks_msec()
	_purge_recent(now)
	if _recent_messages.has(message):
		return
	_recent_messages[message] = now
	var data := QueuedToastData.new(message, level.to_upper(), duration, action)
	_queue.push_back(data)
	_process_queue()

## Dismiss all active toasts immediately.
func clear_all() -> void:
	for node in _active_nodes.duplicate():
		_dismiss_toast(node, true)
	_queue.clear()

# ── Internal queue management ──────────────────────────────────────────────────

func _process_queue() -> void:
	# If we have too many active toasts and new ones are queued, dismiss the oldest.
	if _active_nodes.size() >= MAX_VISIBLE_TOASTS and not _queue.is_empty():
		_dismiss_toast(_active_nodes.back(), false) # Dismiss oldest toast

	# Show new toasts if there's space, highest severity first.
	while _active_nodes.size() < MAX_VISIBLE_TOASTS and not _queue.is_empty():
		var data := _pop_next_queued()
		if data == null:
			break
		_display_toast(data)

## Removes and returns the highest-severity queued toast (FIFO within a tier).
func _pop_next_queued() -> QueuedToastData:
	if _queue.is_empty():
		return null
	var best := 0
	for i in range(1, _queue.size()):
		if SEVERITY_RANK.get(_queue[i].level, 99) < SEVERITY_RANK.get(_queue[best].level, 99):
			best = i
	var data := _queue[best]
	_queue.remove_at(best)
	return data

func _purge_recent(now: int) -> void:
	var window_ms := int(DEDUP_WINDOW_SEC * 1000.0)
	for key in _recent_messages.keys():
		if now - int(_recent_messages[key]) > window_ms:
			_recent_messages.erase(key)

func _display_toast(data: QueuedToastData) -> void:
	var toast_node := _build_toast_node(data)
	_toast_list.add_child(toast_node)
	_active_nodes.push_front(toast_node) # Add to front to stack downwards

	# Initial position off-screen to the right
	var start_pos := Vector2(TOAST_WIDTH + TOAST_MARGIN * 2, toast_node.position.y)
	var end_pos   := Vector2(0, toast_node.position.y)
	toast_node.position = start_pos

	var tween := create_tween()
	tween.set_parallel(true)
	# Slide in animation (0.3s ease-out)
	tween.tween_property(toast_node, "position", end_pos, SLIDE_IN_OUT_DURATION).set_ease(Tween.EASE_OUT)
	tween.tween_property(toast_node, "modulate", Color(1, 1, 1, 1), SLIDE_IN_OUT_DURATION)
	tween.set_parallel(false)
	tween.tween_callback(func():
		_start_dismiss_timer(toast_node, data.duration)
		if data.level == "CRITICAL":
			_start_critical_pulse(toast_node)
	)

	toast_shown.emit(data.message, data.level)
	_reposition_active_toasts()

## Gentle looping pulse for critical toasts — started only AFTER the entry
## animation settles so it never conflicts with the slide-in modulate tween.
func _start_critical_pulse(toast_node: Control) -> void:
	if not is_instance_valid(toast_node):
		return
	var pulse := toast_node.create_tween().set_loops()
	pulse.tween_property(toast_node, "modulate", Color(1.05, 0.9, 0.9, 1), 0.6).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
	pulse.tween_property(toast_node, "modulate", Color(1, 1, 1, 1), 0.6).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)

func _start_dismiss_timer(toast_node: Control, duration: float) -> void:
	# The tween is created ON the toast node so it dies with it: a manually
	# closed toast must not leave a timer behind holding a freed capture
	# ("Lambda capture was freed" — the engine logs when the callable is
	# invoked, so a fire-time is_instance_valid guard does NOT help).
	var life := toast_node.create_tween()
	var progress_bar = toast_node.find_child("ProgressBar") as ProgressBar
	if progress_bar:
		progress_bar.max_value = duration
		progress_bar.value = duration
		life.tween_property(progress_bar, "value", 0, duration)
	else:
		life.tween_interval(duration)
	life.tween_callback(_dismiss_toast.bind(toast_node, false))

func _dismiss_toast(node: Control, immediate: bool) -> void:
	if not is_instance_valid(node) or not node in _active_nodes:
		return

	var message: String = node.get_meta("toast_message", "")
	_active_nodes.erase(node)
	toast_dismissed.emit(message)

	if immediate:
		node.queue_free()
		_reposition_active_toasts()
		_process_queue()
		return

	var tween := create_tween()
	tween.set_parallel(true)
	# Slide out animation
	tween.tween_property(node, "position", Vector2(TOAST_WIDTH + TOAST_MARGIN * 2, node.position.y), SLIDE_IN_OUT_DURATION).set_ease(Tween.EASE_IN)
	tween.tween_property(node, "modulate:a", 0.0, SLIDE_IN_OUT_DURATION)
	tween.set_parallel(false)
	tween.tween_callback(func():
		node.queue_free()
		_reposition_active_toasts()
		_process_queue() # Try to show next queued toast
	)

func _reposition_active_toasts() -> void:
	var current_y_offset := 0.0
	# Iterate in reverse to ensure correct stacking from bottom-right
	for i in range(_active_nodes.size() -1, -1, -1):
		var toast = _active_nodes[i]
		var target_pos = Vector2(toast.position.x, current_y_offset)

		if not toast.position.is_equal_approx(target_pos):
			var tween := create_tween()
			tween.set_parallel(false)
			tween.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
			tween.tween_property(toast, "position", target_pos, SLIDE_IN_OUT_DURATION * 0.5) # Faster reposition

		current_y_offset += toast.size.y + _toast_list.get_theme_constant("separation")

# ── Toast node builder ─────────────────────────────────────────────────────────

func _build_layout() -> void:
	var anchor := Control.new()
	anchor.name = "ToastAnchor"
	anchor.set_anchors_preset(Control.PRESET_TOP_RIGHT)
	anchor.grow_horizontal = Control.GROW_DIRECTION_BEGIN
	anchor.grow_vertical   = Control.GROW_DIRECTION_END
	anchor.custom_minimum_size = Vector2(TOAST_WIDTH, 0)
	anchor.offset_left  = -TOAST_WIDTH - TOAST_MARGIN
	anchor.offset_top   = TOAST_MARGIN
	anchor.offset_right = -TOAST_MARGIN
	anchor.offset_bottom = 0
	add_child(anchor)

	_toast_list = VBoxContainer.new()
	_toast_list.name = "ToastList"
	_toast_list.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_toast_list.add_theme_constant_override("separation", 10) # More vertical spacing
	anchor.add_child(_toast_list)

func _build_toast_node(data: QueuedToastData) -> Control:
	var container := PanelContainer.new()
	container.name = "Toast_%s" % data.message.hash() # Unique name
	container.custom_minimum_size = Vector2(TOAST_WIDTH, 0)
	container.set_meta("toast_message", data.message)
	container.set_meta("toast_action", data.action)
	container.modulate = Color(1, 1, 1, 0) # Start transparent for fade-in
	container.mouse_filter = Control.MOUSE_FILTER_STOP
	if data.action.is_valid():
		container.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	container.gui_input.connect(_on_toast_gui_input.bind(container))

	var bg_color: Color
	var border_color: Color

	match data.level:
		"INFO":
			bg_color = COLOR_INFO.darkened(TOAST_SURFACE_DARKEN)
			border_color = COLOR_INFO
		"WARNING":
			bg_color = COLOR_WARNING.darkened(TOAST_SURFACE_DARKEN)
			border_color = COLOR_WARNING
		"CRITICAL":
			bg_color = COLOR_CRITICAL.darkened(TOAST_SURFACE_DARKEN)
			border_color = COLOR_CRITICAL
			# Pulsing effect is started after slide-in (see _display_toast)
			# so it never fights the entry animation's modulate tween.
		"SUCCESS":
			bg_color = COLOR_SUCCESS.darkened(TOAST_SURFACE_DARKEN)
			border_color = COLOR_SUCCESS
		_: # Fallback for unknown levels
			bg_color = COLOR_INFO.darkened(TOAST_SURFACE_DARKEN)
			border_color = COLOR_INFO
			push_warning("ToastNotification: Unknown toast level '%s', defaulting to 'INFO'." % data.level)

	var style := StyleBoxFlat.new()
	style.bg_color = bg_color
	style.border_color = border_color
	style.set_border_width_all(1)
	style.border_width_left = 6 # Thicker left border for accent
	style.set_corner_radius_all(8) # 8px rounded corners
	style.content_margin_left   = 15.0
	style.content_margin_right  = 15.0
	style.content_margin_top    = 10.0
	style.content_margin_bottom = 10.0
	container.add_theme_stylebox_override("panel", style)

	var vbox_main := VBoxContainer.new()
	vbox_main.mouse_filter = Control.MOUSE_FILTER_IGNORE
	container.add_child(vbox_main)

	var hbox_content := HBoxContainer.new()
	hbox_content.add_theme_constant_override("separation", 12)
	hbox_content.mouse_filter = Control.MOUSE_FILTER_IGNORE
	vbox_main.add_child(hbox_content)

	# Icon
	var icon_lbl := Label.new()
	icon_lbl.text = SEVERITY_ICONS.get(data.level, SEVERITY_ICONS.INFO)
	icon_lbl.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	icon_lbl.custom_minimum_size = Vector2(30, 0)
	icon_lbl.mouse_filter = Control.MOUSE_FILTER_IGNORE
	icon_lbl.add_theme_color_override("font_color", border_color)
	icon_lbl.add_theme_font_size_override("font_size", 24)
	hbox_content.add_child(icon_lbl)

	# Message Text
	var message_lbl := Label.new()
	message_lbl.text = data.message
	message_lbl.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	message_lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	message_lbl.mouse_filter = Control.MOUSE_FILTER_IGNORE
	message_lbl.add_theme_color_override("font_color", COLOR_TEXT)
	message_lbl.add_theme_font_size_override("font_size", 16)
	hbox_content.add_child(message_lbl)

	# Close Button
	var close_button := Button.new()
	close_button.text = tr("X")
	close_button.custom_minimum_size = Vector2(30, 30)
	close_button.size_flags_horizontal = Control.SIZE_SHRINK_END
	close_button.add_theme_color_override("font_color", COLOR_TEXT.darkened(0.2))
	close_button.add_theme_font_size_override("font_size", 18)
	close_button.flat = true # Make it flat
	# Connect the pressed signal
	close_button.pressed.connect(func(): _on_close_button_pressed(container))
	hbox_content.add_child(close_button)

	# Progress Bar
	var progress_bar := ProgressBar.new()
	progress_bar.name = "ProgressBar"
	progress_bar.visible = true
	progress_bar.mouse_filter = Control.MOUSE_FILTER_IGNORE
	progress_bar.custom_minimum_size = Vector2(0, 8)
	# Hide the percentage label entirely (transparent text still reserves
	# label height, which inflated the bar to ~35px).
	progress_bar.show_percentage = false
	vbox_main.add_child(progress_bar)

	return container

func _on_close_button_pressed(toast_node: Control) -> void:
	_dismiss_toast(toast_node, false) # Dismiss with animation

## Body click: invoke the optional action Callable, then dismiss.
func _on_toast_gui_input(event: InputEvent, toast_node: Control) -> void:
	if not (event is InputEventMouseButton):
		return
	var mb := event as InputEventMouseButton
	if not mb.pressed or mb.button_index != MOUSE_BUTTON_LEFT:
		return
	var action: Callable = toast_node.get_meta("toast_action", Callable())
	if action.is_valid():
		action.call()
	_dismiss_toast(toast_node, false)
	get_viewport().set_input_as_handled()

# No ESC handler: toasts close by click or timeout. One consumed every ESC
# that reached this autoload and hid the whole layer for good (nothing showed
# it again), so after one ESC on the main menu no toast appeared for the rest
# of the session, and the save list's own ESC close never ran (review F665).

