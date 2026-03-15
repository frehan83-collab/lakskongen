extends CanvasLayer

## ToastNotification — transient popup that slides in from the top-right,
## displays for 3–5 seconds, then auto-dismisses.
## Multiple toasts are queued and shown one at a time.
##
## Usage (from any script):
##   var toast := get_node("/root/ToastNotification")
##   toast.push("🐟 Høsting fullført", "2 400 laks høstet — 18 500 kg", "info", 4.0)
##   toast.push("🦠 Sykdomsutbrudd!", "ILA påvist i Sone 3", "critical")
##
## Expected node layout (set up in .tscn or created fully in code below):
##   ToastNotification (CanvasLayer, layer=10)
##   └─ ToastAnchor (Control, anchors: top-right, size (340, 0))
##      └─ ToastList (VBoxContainer)  ← individual toasts appended here
##
## Each toast animates via a Tween (no AnimationPlayer required).

# ── Constants ──────────────────────────────────────────────────────────────────

const DEFAULT_DURATION := 4.0       # seconds visible
const SLIDE_DURATION   := 0.25      # seconds for in/out animation
const TOAST_WIDTH      := 340.0
const TOAST_MARGIN     := 12.0      # right/top margin from screen edge

const SEVERITY_BG := {
	"info":     Color(0.10, 0.18, 0.30, 0.95),
	"warning":  Color(0.28, 0.22, 0.04, 0.95),
	"critical": Color(0.30, 0.06, 0.06, 0.95),
}
const SEVERITY_ACCENT := {
	"info":     Color(0.30, 0.75, 1.00),
	"warning":  Color(1.00, 0.80, 0.10),
	"critical": Color(0.95, 0.25, 0.15),
}

# ── Inner type ─────────────────────────────────────────────────────────────────

class ToastData:
	var icon:        String
	var title:       String
	var description: String
	var severity:    String
	var duration:    float

	func _init(p_icon: String, p_title: String, p_desc: String,
			   p_severity: String, p_duration: float) -> void:
		icon        = p_icon
		title       = p_title
		description = p_desc
		severity    = p_severity
		duration    = p_duration

# ── Signals ────────────────────────────────────────────────────────────────────

signal toast_shown(title: String, severity: String)
signal toast_dismissed(title: String)

# ── State ──────────────────────────────────────────────────────────────────────

var _queue:        Array[ToastData] = []
var _active_nodes: Array[Control]   = []
var _sim:          Node             = null
var _toast_list:   VBoxContainer    = null

# ── Lifecycle ──────────────────────────────────────────────────────────────────

func _ready() -> void:
	layer = 10   # above all game UI

	_sim = get_node_or_null("/root/SimBridge")
	if _sim:
		_sim.alert_raised.connect(_on_alert_raised)

	_build_layout()
	print("ToastNotification: ready")

func _input(event: InputEvent) -> void:
	"""Handle keyboard navigation"""
	if not visible:
		return
	
	if event.is_action_pressed("ui_cancel"):
		hide()
		get_viewport().set_input_as_handled()

func _build_layout() -> void:
	var anchor := Control.new()
	anchor.name = "ToastAnchor"
	anchor.set_anchors_preset(Control.PRESET_TOP_RIGHT)
	anchor.grow_horizontal = Control.GROW_DIRECTION_BEGIN
	anchor.grow_vertical   = Control.GROW_DIRECTION_END
	anchor.custom_minimum_size = Vector2(TOAST_WIDTH, 0)
	# Offset from top-right corner
	anchor.offset_left  = -TOAST_WIDTH - TOAST_MARGIN
	anchor.offset_top   = TOAST_MARGIN
	anchor.offset_right = -TOAST_MARGIN
	anchor.offset_bottom = 0
	add_child(anchor)

	_toast_list = VBoxContainer.new()
	_toast_list.name = "ToastList"
	_toast_list.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_toast_list.add_theme_constant_override("separation", 6)
	anchor.add_child(_toast_list)

# ── Public API ─────────────────────────────────────────────────────────────────

## Push a new toast to the display queue.
## severity: "info" | "warning" | "critical"
func push(title: String, description: String = "",
		  severity: String = "info",
		  duration: float = DEFAULT_DURATION,
		  icon: String = "") -> void:
	if icon.is_empty():
		icon = _default_icon(severity)
	var data := ToastData.new(icon, title, description, severity, duration)
	_queue.push_back(data)
	_flush_queue()

## Dismiss all active toasts immediately.
func clear_all() -> void:
	for node in _active_nodes.duplicate():
		_dismiss_toast(node, true)
	_queue.clear()

# ── Internal queue management ──────────────────────────────────────────────────

func _flush_queue() -> void:
	while not _queue.is_empty():
		var data: ToastData = _queue.pop_front()
		_show_toast(data)

func _show_toast(data: ToastData) -> void:
	var toast_node := _build_toast(data)
	_toast_list.add_child(toast_node)
	_active_nodes.append(toast_node)

	# Start off-screen to the right
	toast_node.position.x = TOAST_WIDTH + TOAST_MARGIN * 2

	# Slide in
	var tween := create_tween()
	tween.tween_property(toast_node, "position:x", 0.0, SLIDE_DURATION) \
		 .set_ease(Tween.EASE_OUT).set_trans(Tween.TRANS_CUBIC)

	toast_shown.emit(data.title, data.severity)

	# Auto-dismiss after duration
	var timer := get_tree().create_timer(data.duration)
	timer.timeout.connect(func(): _dismiss_toast(toast_node, false))

func _dismiss_toast(node: Control, immediate: bool) -> void:
	if not is_instance_valid(node):
		return
	if not node in _active_nodes:
		return

	var title: String = node.get_meta("toast_title", "")
	_active_nodes.erase(node)
	toast_dismissed.emit(title)

	if immediate:
		node.queue_free()
		return

	# Slide out then free
	var tween := create_tween()
	tween.tween_property(node, "position:x", TOAST_WIDTH + TOAST_MARGIN * 2, SLIDE_DURATION) \
		 .set_ease(Tween.EASE_IN).set_trans(Tween.TRANS_CUBIC)
	tween.finished.connect(func(): node.queue_free())

# ── Toast node builder ─────────────────────────────────────────────────────────

func _build_toast(data: ToastData) -> Control:
	var container := PanelContainer.new()
	container.name       = "Toast"
	container.custom_minimum_size = Vector2(TOAST_WIDTH, 0)
	container.set_meta("toast_title", data.title)

	var sev    := data.severity
	var bg     := SEVERITY_BG.get(sev,     SEVERITY_BG["info"])
	var accent := SEVERITY_ACCENT.get(sev, SEVERITY_ACCENT["info"])

	var style := StyleBoxFlat.new()
	style.bg_color = bg
	style.border_color = accent
	style.set_border_width_all(0)
	style.border_width_left = 4
	style.set_corner_radius_all(6)
	style.content_margin_left   = 10.0
	style.content_margin_right  = 10.0
	style.content_margin_top    = 8.0
	style.content_margin_bottom = 8.0
	container.add_theme_stylebox_override("panel", style)

	var hbox := HBoxContainer.new()
	hbox.add_theme_constant_override("separation", 8)
	container.add_child(hbox)

	# Icon
	var icon_lbl := Label.new()
	icon_lbl.text = data.icon
	icon_lbl.vertical_alignment = VERTICAL_ALIGNMENT_TOP
	icon_lbl.custom_minimum_size = Vector2(24, 0)
	icon_lbl.add_theme_color_override("font_color", accent)
	icon_lbl.add_theme_font_size_override("font_size", 18)
	hbox.add_child(icon_lbl)

	# Text block
	var vbox := VBoxContainer.new()
	vbox.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	vbox.add_theme_constant_override("separation", 2)
	hbox.add_child(vbox)

	var title_lbl := Label.new()
	title_lbl.text = data.title
	title_lbl.add_theme_color_override("font_color", Color.WHITE)
	title_lbl.add_theme_font_size_override("font_size", 13)
	vbox.add_child(title_lbl)

	if not data.description.is_empty():
		var desc_lbl := Label.new()
		desc_lbl.text = data.description
		desc_lbl.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		desc_lbl.add_theme_color_override("font_color", Color(0.80, 0.80, 0.80))
		desc_lbl.add_theme_font_size_override("font_size", 11)
		vbox.add_child(desc_lbl)

	# Close button (×)
	var close_btn := Button.new()
	close_btn.text = "✕"
	close_btn.flat = true
	close_btn.custom_minimum_size = Vector2(24, 24)
	close_btn.vertical_alignment = VERTICAL_ALIGNMENT_TOP
	close_btn.add_theme_color_override("font_color", Color(0.6, 0.6, 0.6))
	close_btn.pressed.connect(func(): _dismiss_toast(container, false))
	hbox.add_child(close_btn)

	return container

# ── SimBridge signal handlers ──────────────────────────────────────────────────

func _on_alert_raised(level: String, message: String) -> void:
	var icon: String = _default_icon(level)
	var title: String
	match level:
		"critical": title = "Kritisk varsel"
		"warning":  title = "Advarsel"
		_:          title = "Informasjon"
	push(title, message, level)

# ── Utility ────────────────────────────────────────────────────────────────────

func _default_icon(severity: String) -> String:
	match severity:
		"critical": return "🚨"
		"warning":  return "⚠"
		_:          return "ℹ"
