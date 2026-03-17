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

# Color definitions for toast levels
const COLOR_INFO     := Color("#2d8a7e") # Blue
const COLOR_WARNING  := Color("#e8a838") # Amber
const COLOR_CRITICAL := Color("#e87461") # Red
const COLOR_SUCCESS  := Color("#4caf50") # Green
const COLOR_TEXT     := Color.WHITE

const SEVERITY_ICONS := {
	"INFO":     "ℹ️",
	"WARNING":  "⚠️",
	"CRITICAL": "🔴",
	"SUCCESS":  "✅",
}

# ── Inner type for queued toasts ────────────────────────────────────────────────

class QueuedToastData:
	var message:  String
	var level:    String
	var duration: float

	func _init(p_message: String, p_level: String, p_duration: float) -> void:
		message  = p_message
		level    = p_level
		duration = p_duration

# ── Signals ────────────────────────────────────────────────────────────────────

signal toast_shown(message: String, level: String)
signal toast_dismissed(message: String)

# ── State ──────────────────────────────────────────────────────────────────────

var _queue:        Array[QueuedToastData] = []
var _active_nodes: Array[Control]         = [] # Stores active toast nodes
var _toast_list:   VBoxContainer          = null # Container for active toasts

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
func show_toast(message: String, level: String = "INFO", duration: float = 5.0) -> void:
	var data := QueuedToastData.new(message, level.to_upper(), duration)
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

	# Show new toasts if there's space and the queue is not empty.
	while _active_nodes.size() < MAX_VISIBLE_TOASTS and not _queue.is_empty():
		var data: QueuedToastData = _queue.pop_front()
		_display_toast(data)

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
	tween.tween_callback(func(): _start_dismiss_timer(toast_node, data.duration))

	toast_shown.emit(data.message, data.level)
	_reposition_active_toasts()

func _start_dismiss_timer(toast_node: Control, duration: float) -> void:
	var progress_bar = toast_node.find_child("ProgressBar") as ProgressBar
	if progress_bar:
		progress_bar.max_value = duration
		progress_bar.value = duration
		var progress_tween := create_tween()
		progress_tween.tween_property(progress_bar, "value", 0, duration)
		progress_tween.tween_callback(func(): _dismiss_toast(toast_node, false))
	else:
		var timer := get_tree().create_timer(duration)
		timer.timeout.connect(func(): _dismiss_toast(toast_node, false))

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
	container.modulate = Color(1, 1, 1, 0) # Start transparent for fade-in

	var bg_color: Color
	var border_color: Color

	match data.level:
		"INFO":
			bg_color = COLOR_INFO.darkened(0.5)
			border_color = COLOR_INFO
		"WARNING":
			bg_color = COLOR_WARNING.darkened(0.5)
			border_color = COLOR_WARNING
		"CRITICAL":
			bg_color = COLOR_CRITICAL.darkened(0.5)
			border_color = COLOR_CRITICAL
			# Critical toasts get a pulsing effect
			var tween := create_tween().set_loops()
			tween.tween_property(container, "modulate", Color(1.2, 0.8, 0.8, 1), 0.5).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
			tween.tween_property(container, "modulate", Color(1, 1, 1, 1), 0.5).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
		"SUCCESS":
			bg_color = COLOR_SUCCESS.darkened(0.5)
			border_color = COLOR_SUCCESS
		_: # Fallback for unknown levels
			bg_color = COLOR_INFO.darkened(0.5)
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
	container.add_child(vbox_main)

	var hbox_content := HBoxContainer.new()
	hbox_content.add_theme_constant_override("separation", 12)
	vbox_main.add_child(hbox_content)

	# Icon
	var icon_lbl := Label.new()
	icon_lbl.text = SEVERITY_ICONS.get(data.level, SEVERITY_ICONS.INFO)
	icon_lbl.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	icon_lbl.custom_minimum_size = Vector2(30, 0)
	icon_lbl.add_theme_color_override("font_color", border_color)
	icon_lbl.add_theme_font_size_override("font_size", 24)
	hbox_content.add_child(icon_lbl)

	# Message Text
	var message_lbl := Label.new()
	message_lbl.text = data.message
	message_lbl.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	message_lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	message_lbl.add_theme_color_override("font_color", COLOR_TEXT)
	message_lbl.add_theme_font_size_override("font_size", 16)
	hbox_content.add_child(message_lbl)

	# Close Button
	var close_button := Button.new()
	close_button.text = "X"
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
	progress_bar.custom_minimum_size = Vector2(0, 8)
	progress_bar.add_theme_color_override("font_color", Color(0,0,0,0)) # Hide percentage text
	vbox_main.add_child(progress_bar)

	return container

func _on_close_button_pressed(toast_node: Control) -> void:
	_dismiss_toast(toast_node, false) # Dismiss with animation
