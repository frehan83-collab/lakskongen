extends CanvasLayer

## ToastNotification — transient notices in the bottom-right corner (autoload).
##
## Screen region (roadmap U1-04): one container-owned stack anchored to the
## bottom-right corner, newest toast nearest the corner. The VBoxContainer owns
## the layout of one wrapper slot per toast; motion (slide in/out, the shift
## when a neighbour leaves) tweens the toast INSIDE its slot, so a tween never
## fights the container for the same position.
##
## At most MAX_VISIBLE_TOASTS are on screen. A further toast waits in a queue
## that drains critical first; the oldest visible one makes room. A message
## that is already on screen is not stacked again: its timer restarts and a
## "×N" counter grows. A message dismissed moments ago is not shown again
## inside DEDUP_WINDOW_SEC.
##
## Usage:
##   ToastNotification.show_toast("Høsting fullført", "SUCCESS", 4.0)
##   ToastNotification.show_toast("Sykdomsutbrudd i Sone 3", "CRITICAL")

# ── Constants ──────────────────────────────────────────────────────────────────

const SLIDE_IN_OUT_DURATION := 0.3 # seconds for in/out animation (ease-out)
const SHIFT_DURATION        := 0.18 # neighbours settling after a toast leaves
const TOAST_WIDTH           := 380.0
## Gap to the right and bottom screen edges (the stack's corner).
const STACK_MARGIN_RIGHT    := 16.0
const STACK_MARGIN_BOTTOM   := 16.0
const STACK_SEPARATION      := 10
## The message column's narrowest width (card minus icon, counter and close).
const MESSAGE_MIN_WIDTH     := 200.0
const MAX_VISIBLE_TOASTS    := 3    # Maximum toasts visible at once
const DEDUP_WINDOW_SEC      := 2.5  # Suppress a just-dismissed message within this window

# Toast level -> Kyst & Stål tone. Toasts are Ink cards ("Toast<Tone>" theme
# variations: raised Ink glass with a tone accent edge, roadmap D13).
const LEVEL_TONES := {"INFO": &"Info", "WARNING": &"Warn", "CRITICAL": &"Crit", "SUCCESS": &"Ok"}

## Severity glyphs: text symbols drawn in the tone colour (no emoji, D14).
const SEVERITY_ICONS := {
	"INFO":     "i",
	"WARNING":  "!",
	"CRITICAL": "!!",
	"SUCCESS":  "✓",
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
## Visible toast panels, newest first (each sits in its own slot in _toast_list).
var _active_nodes: Array[Control]         = []
## The stack: a bottom-aligned VBoxContainer anchored to the bottom-right corner.
var _toast_list:   VBoxContainer          = null
var _recent_messages: Dictionary          = {} # message -> ticks_msec of its dismissal
## Extra right inset while a right-hand drawer (the notification history) is
## open, so the stack sits beside it instead of over it.
var _right_inset: float = 0.0
var _lift: float = 0.0

# ── Lifecycle ──────────────────────────────────────────────────────────────────

func _ready() -> void:
	layer = 10 # Ensure toasts are on top of other UI
	# Toasts keep sliding and timing out while a pause menu holds the tree.
	process_mode = Node.PROCESS_MODE_ALWAYS
	_build_layout()

# ── Public API ─────────────────────────────────────────────────────────────────

## Displays a toast notification.
## message: The main text content of the toast.
## level: "INFO", "WARNING", "CRITICAL", or "SUCCESS". Defaults to "INFO".
## duration: How long the toast stays visible in seconds. Defaults to 5.0.
## action: Optional Callable invoked when the toast body is clicked.
func show_toast(message: String, level: String = "INFO", duration: float = 5.0, action: Callable = Callable()) -> void:
	var now := Time.get_ticks_msec()
	_purge_recent(now)
	if _recent_messages.has(message):
		return
	var showing := _visible_toast_for(message)
	if showing != null:
		_repeat(showing, duration)
		return
	for queued in _queue:
		if queued.message == message:
			return
	var data := QueuedToastData.new(message, level.to_upper(), duration, action)
	_queue.push_back(data)
	_process_queue()

## Dismiss all active toasts immediately.
func clear_all() -> void:
	_queue.clear()
	for node in _active_nodes.duplicate():
		_dismiss_toast(node, true)
	_recent_messages.clear()

## Keep the stack clear of a drawer on the right edge (0 = back to the
## corner). lift raises the stack's bottom too: moved off the corner it would
## otherwise sit in the bottom-centre band (the fjord camera hint).
func set_right_inset(pixels: float, lift: float = 0.0) -> void:
	_right_inset = maxf(pixels, 0.0)
	_lift = maxf(lift, 0.0)
	if _toast_list != null:
		_toast_list.offset_right = -STACK_MARGIN_RIGHT - _right_inset
		_toast_list.offset_left = _toast_list.offset_right - TOAST_WIDTH
		_toast_list.offset_bottom = -STACK_MARGIN_BOTTOM - _lift
		_toast_list.offset_top = _toast_list.offset_bottom

## Screen rects of the toasts on screen (newest first), for the overlap lint.
func get_toast_rects() -> Array[Rect2]:
	var rects: Array[Rect2] = []
	for node in _active_nodes:
		if is_instance_valid(node) and node.is_visible_in_tree():
			rects.append(node.get_global_rect())
	return rects

## The stack's own screen rect (its slots, whether or not a toast is showing).
func get_stack_rect() -> Rect2:
	return _toast_list.get_global_rect() if _toast_list != null else Rect2()

# ── Internal queue management ──────────────────────────────────────────────────

func _process_queue() -> void:
	while not _queue.is_empty():
		if _slot_count() >= MAX_VISIBLE_TOASTS:
			# A full stack makes room: the oldest toast leaves (once — not again
			# while one is already sliding out). Its slot is freed when it has
			# slid out, which drains the queue again.
			if _active_nodes.size() >= MAX_VISIBLE_TOASTS and _slot_count() == _active_nodes.size():
				_dismiss_toast(_active_nodes.back(), false)
			return
		_display_toast(_pop_next_queued())

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

func _visible_toast_for(message: String) -> Control:
	for node in _active_nodes:
		if is_instance_valid(node) and String(node.get_meta("toast_message", "")) == message:
			return node
	return null

## Slots still in the stack: visible toasts plus ones sliding out.
func _slot_count() -> int:
	return _toast_list.get_child_count() if _toast_list != null else 0

## The same message again while it is showing: count it and restart its timer.
func _repeat(toast_node: Control, duration: float) -> void:
	var count: int = int(toast_node.get_meta("toast_count", 1)) + 1
	toast_node.set_meta("toast_count", count)
	var counter := toast_node.find_child("RepeatCount", true, false) as Label
	if counter != null:
		counter.text = "×%d" % count
		counter.visible = true
	if bool(toast_node.get_meta("toast_settled", false)):
		_start_dismiss_timer(toast_node, duration)

func _display_toast(data: QueuedToastData) -> void:
	var toast_node := _build_toast_node(data)
	# The slot is the container's child: the VBoxContainer lays slots out and
	# the toast moves only inside its slot.
	var slot := Control.new()
	slot.name = "ToastSlot"
	slot.mouse_filter = Control.MOUSE_FILTER_IGNORE
	slot.add_child(toast_node)
	_toast_list.add_child(slot)
	# Newest at the bottom, nearest the corner.
	_toast_list.move_child(slot, _toast_list.get_child_count() - 1)
	_fit_slot(toast_node)
	toast_node.minimum_size_changed.connect(_fit_slot.bind(toast_node), CONNECT_DEFERRED)
	_active_nodes.push_front(toast_node)

	toast_node.position = Vector2(TOAST_WIDTH + STACK_MARGIN_RIGHT * 2.0, 0.0)
	var tween := toast_node.create_tween()
	tween.set_parallel(true)
	tween.tween_property(toast_node, "position:x", 0.0, SLIDE_IN_OUT_DURATION) \
		.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
	tween.tween_property(toast_node, "modulate:a", 1.0, SLIDE_IN_OUT_DURATION)
	tween.set_parallel(false)
	tween.tween_callback(func():
		toast_node.set_meta("toast_settled", true)
		_start_dismiss_timer(toast_node, data.duration)
		if data.level == "CRITICAL":
			_start_critical_pulse(toast_node)
	)

	toast_shown.emit(data.message, data.level)

## The slot is exactly as tall as its toast (a toast sits outside any
## container, so it is sized here: the message wraps at the card width).
func _fit_slot(toast_node: Control) -> void:
	if not is_instance_valid(toast_node):
		return
	var slot := toast_node.get_parent() as Control
	if slot == null:
		return
	toast_node.size = Vector2(TOAST_WIDTH, 0.0)
	var height := toast_node.get_combined_minimum_size().y
	toast_node.size = Vector2(TOAST_WIDTH, height)
	if not is_equal_approx(slot.custom_minimum_size.y, height):
		slot.custom_minimum_size = Vector2(TOAST_WIDTH, height)

## Gentle looping pulse for critical toasts — started only AFTER the entry
## animation settles so it never conflicts with the slide-in modulate tween.
func _start_critical_pulse(toast_node: Control) -> void:
	if not is_instance_valid(toast_node):
		return
	var pulse := toast_node.create_tween().set_loops()
	pulse.tween_property(toast_node, "self_modulate", Color(1.08, 0.94, 0.94, 1), 0.6).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
	pulse.tween_property(toast_node, "self_modulate", Color(1, 1, 1, 1), 0.6).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
	toast_node.set_meta("toast_pulse", pulse)

func _start_dismiss_timer(toast_node: Control, duration: float) -> void:
	# The timer tween lives ON the toast node so it dies with it: a closed
	# toast must not leave a timer behind holding a freed capture.
	if toast_node.has_meta("toast_life"):
		var previous := toast_node.get_meta("toast_life") as Tween
		if previous != null and previous.is_valid():
			previous.kill()
	var life := toast_node.create_tween()
	toast_node.set_meta("toast_life", life)
	var progress_bar = toast_node.find_child("ProgressBar", true, false) as ProgressBar
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
	_recent_messages[message] = Time.get_ticks_msec()
	toast_dismissed.emit(message)
	var slot := node.get_parent() as Control

	if immediate:
		_free_slot(slot)
		return

	var tween := node.create_tween()
	tween.set_parallel(true)
	tween.tween_property(node, "position:x", TOAST_WIDTH + STACK_MARGIN_RIGHT * 2.0, SLIDE_IN_OUT_DURATION) \
		.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_IN)
	tween.tween_property(node, "modulate:a", 0.0, SLIDE_IN_OUT_DURATION)
	tween.set_parallel(false)
	tween.tween_callback(_free_slot.bind(slot))

## Remove a slot; the toasts above it settle down smoothly (they keep their
## screen position for one frame and tween to their new slot, FLIP-style).
func _free_slot(slot: Control) -> void:
	if not is_instance_valid(slot):
		_process_queue()
		return
	var before: Dictionary = {}
	for other in _toast_list.get_children():
		if other != slot and other.get_child_count() > 0:
			before[other] = (other as Control).global_position.y
	_toast_list.remove_child(slot)
	slot.queue_free()
	_toast_list.queue_sort()
	_settle_after_sort.call_deferred(before)
	_process_queue()

func _settle_after_sort(before: Dictionary) -> void:
	if _toast_list == null:
		return
	# The container sorts on the frame's notification pass; read the new
	# positions after it.
	await get_tree().process_frame
	for slot in before.keys():
		if not is_instance_valid(slot):
			continue
		var toast := (slot as Control).get_child(0) as Control
		if toast == null or not toast in _active_nodes:
			continue
		var shift: float = float(before[slot]) - (slot as Control).global_position.y
		if absf(shift) < 0.5:
			continue
		toast.position.y = shift
		toast.create_tween().tween_property(toast, "position:y", 0.0, SHIFT_DURATION) \
			.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)

# ── Toast node builder ─────────────────────────────────────────────────────────

func _build_layout() -> void:
	_toast_list = VBoxContainer.new()
	_toast_list.name = "ToastStack"
	_toast_list.set_anchors_preset(Control.PRESET_BOTTOM_RIGHT)
	_toast_list.grow_horizontal = Control.GROW_DIRECTION_BEGIN
	_toast_list.grow_vertical = Control.GROW_DIRECTION_BEGIN
	_toast_list.alignment = BoxContainer.ALIGNMENT_END
	_toast_list.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_toast_list.add_theme_constant_override("separation", STACK_SEPARATION)
	add_child(_toast_list)
	set_right_inset(_right_inset, _lift)

func _build_toast_node(data: QueuedToastData) -> Control:
	var container := PanelContainer.new()
	container.name = "Toast"
	container.custom_minimum_size = Vector2(TOAST_WIDTH, 0)
	container.size = Vector2(TOAST_WIDTH, 0)
	container.set_meta("toast_message", data.message)
	container.set_meta("toast_action", data.action)
	container.set_meta("toast_level", data.level)
	container.modulate = Color(1, 1, 1, 0) # Start transparent for fade-in
	container.mouse_filter = Control.MOUSE_FILTER_STOP
	if data.action.is_valid():
		container.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	container.gui_input.connect(_on_toast_gui_input.bind(container))

	if not LEVEL_TONES.has(data.level):
		push_warning("ToastNotification: Unknown toast level '%s', defaulting to 'INFO'." % data.level)
	var tone: StringName = LEVEL_TONES.get(data.level, &"Info")
	container.theme_type_variation = StringName("Toast" + String(tone))

	var vbox_main := VBoxContainer.new()
	vbox_main.mouse_filter = Control.MOUSE_FILTER_IGNORE
	vbox_main.add_theme_constant_override("separation", 8)
	container.add_child(vbox_main)

	var hbox_content := HBoxContainer.new()
	hbox_content.add_theme_constant_override("separation", 12)
	hbox_content.mouse_filter = Control.MOUSE_FILTER_IGNORE
	vbox_main.add_child(hbox_content)

	# Severity mark: the tone's glyph on a tinted round badge.
	var badge := PanelContainer.new()
	badge.name = "SeverityBadge"
	badge.theme_type_variation = StringName("Badge" + String(tone))
	badge.custom_minimum_size = Vector2(26, 26)
	badge.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	badge.mouse_filter = Control.MOUSE_FILTER_IGNORE
	hbox_content.add_child(badge)
	var icon_lbl := Label.new()
	icon_lbl.name = "SeverityIcon"
	icon_lbl.text = SEVERITY_ICONS.get(data.level, SEVERITY_ICONS.INFO)
	icon_lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	icon_lbl.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	icon_lbl.mouse_filter = Control.MOUSE_FILTER_IGNORE
	icon_lbl.theme_type_variation = StringName("BodySmallStrong" + String(tone))
	badge.add_child(icon_lbl)

	var message_lbl := Label.new()
	message_lbl.name = "Message"
	message_lbl.text = data.message
	message_lbl.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	# A known wrap width from the start, so the first height is the real one.
	message_lbl.custom_minimum_size = Vector2(MESSAGE_MIN_WIDTH, 0)
	message_lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	message_lbl.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	message_lbl.mouse_filter = Control.MOUSE_FILTER_IGNORE
	message_lbl.theme_type_variation = &"BodySmall"
	hbox_content.add_child(message_lbl)

	var count_lbl := Label.new()
	count_lbl.name = "RepeatCount"
	count_lbl.visible = false
	count_lbl.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	count_lbl.mouse_filter = Control.MOUSE_FILTER_IGNORE
	count_lbl.theme_type_variation = StringName("Caption" + String(tone))
	hbox_content.add_child(count_lbl)

	var close_button := Button.new()
	close_button.name = "CloseButton"
	close_button.text = "×"
	close_button.tooltip_text = tr("UI_CLOSE")
	close_button.custom_minimum_size = Vector2(30, 30)
	close_button.size_flags_horizontal = Control.SIZE_SHRINK_END
	close_button.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	close_button.theme_type_variation = &"LargeGhostButton"
	close_button.pressed.connect(func(): _on_close_button_pressed(container))
	hbox_content.add_child(close_button)

	# Time-left bar: a hairline along the bottom of the card.
	var progress_bar := ProgressBar.new()
	progress_bar.name = "ProgressBar"
	progress_bar.mouse_filter = Control.MOUSE_FILTER_IGNORE
	progress_bar.custom_minimum_size = Vector2(0, 4)
	progress_bar.show_percentage = false
	progress_bar.theme_type_variation = StringName("ProgressBar" + String(tone))
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
