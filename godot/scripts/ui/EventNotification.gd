extends CanvasLayer

## EventNotification — Dramatic popup alerts for major game events
## Slides in from top, auto-dismisses, supports stacking and pulse effects

enum EventSeverity { INFO, WARNING, CRITICAL, ACHIEVEMENT }

var notification_stack: Array = []
var stack_offset: float = 0.0
const NOTIFICATION_HEIGHT := 120.0
const NOTIFICATION_GAP := 8.0
const SLIDE_DURATION := 0.4
const DISPLAY_DURATION := 5.0

func _ready():
	layer = 100  # Always on top
	
	# Connect to SimulationCore's GameEvent signal
	var sim = get_node_or_null("/root/SimulationCore")
	if sim:
		sim.GameEvent.connect(_on_game_event)

func _on_game_event(title: String, description: String, severity: int):
	show_event(title, description, severity)

func show_event(title: String, description: String, severity: int):
	var panel := PanelContainer.new()
	var style := StyleBoxFlat.new()
	
	# Color based on severity
	match severity:
		EventSeverity.INFO:
			style.bg_color = LakskongenTheme.ACCENT
		EventSeverity.WARNING:
			style.bg_color = LakskongenTheme.WARNING
		EventSeverity.CRITICAL:
			style.bg_color = LakskongenTheme.DANGER
		EventSeverity.ACHIEVEMENT:
			style.bg_color = LakskongenTheme.GOLD
	
	style.corner_radius_bottom_left = LakskongenTheme.RADIUS_MD
	style.corner_radius_bottom_right = LakskongenTheme.RADIUS_MD
	style.content_margin_left = LakskongenTheme.SPACE_LG
	style.content_margin_right = LakskongenTheme.SPACE_LG
	style.content_margin_top = LakskongenTheme.SPACE_MD
	style.content_margin_bottom = LakskongenTheme.SPACE_MD
	panel.add_theme_stylebox_override("panel", style)
	
	# Layout
	var hbox := HBoxContainer.new()
	hbox.add_theme_constant_override("separation", LakskongenTheme.SPACE_MD)
	panel.add_child(hbox)
	
	# Text content
	var vbox := VBoxContainer.new()
	vbox.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	vbox.add_theme_constant_override("separation", LakskongenTheme.SPACE_XS)
	hbox.add_child(vbox)
	
	# Title
	var text_color := LakskongenTheme.BG if severity == EventSeverity.WARNING or severity == EventSeverity.ACHIEVEMENT else LakskongenTheme.TEXT
	var title_label := Label.new()
	title_label.text = title
	LakskongenTheme.set_size(title_label, LakskongenTheme.FONT_XXL)
	LakskongenTheme.set_text_color(title_label, text_color)
	vbox.add_child(title_label)
	
	# Description
	var desc_label := Label.new()
	desc_label.text = description
	LakskongenTheme.set_size(desc_label, LakskongenTheme.FONT_MD)
	LakskongenTheme.set_text_color(desc_label, text_color.darkened(0.15))
	desc_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	vbox.add_child(desc_label)
	
	# Close button
	var close_btn := Button.new()
	close_btn.text = "✕"
	LakskongenTheme.set_size(close_btn, LakskongenTheme.FONT_XL)
	LakskongenTheme.set_text_color(close_btn, text_color)
	close_btn.flat = true
	close_btn.pressed.connect(_dismiss_notification.bind(panel))
	hbox.add_child(close_btn)
	
	# Position: start above screen, slide in
	var viewport_w := get_viewport().get_visible_rect().size.x
	panel.position = Vector2(viewport_w * 0.15, -NOTIFICATION_HEIGHT)
	panel.custom_minimum_size = Vector2(viewport_w * 0.7, 0)
	
	add_child(panel)
	notification_stack.append(panel)
	
	# Calculate target Y based on stack
	var target_y := _get_stack_y(notification_stack.size() - 1)
	
	# Slide in
	var tween := create_tween()
	tween.tween_property(panel, "position:y", target_y, SLIDE_DURATION).set_trans(Tween.TRANS_BACK).set_ease(Tween.EASE_OUT)
	
	# Auto dismiss after delay
	get_tree().create_timer(DISPLAY_DURATION).timeout.connect(_dismiss_notification.bind(panel))

func show_critical(title: String, description: String):
	show_event(title, description, EventSeverity.CRITICAL)
	
	# Pulse effect — find the panel we just added (last in stack)
	if notification_stack.size() > 0:
		var panel: PanelContainer = notification_stack.back()
		var pulse_tween := create_tween()
		pulse_tween.set_loops(3)
		pulse_tween.tween_property(panel, "modulate", Color(1.5, 0.7, 0.7, 1.0), 0.15)
		pulse_tween.tween_property(panel, "modulate", Color.WHITE, 0.15)

func show_achievement(title: String):
	show_event("🏆 " + title, "Achievement Unlocked!", EventSeverity.ACHIEVEMENT)

func show_info(title: String, description: String):
	show_event(title, description, EventSeverity.INFO)

func show_warning(title: String, description: String):
	show_event(title, description, EventSeverity.WARNING)

func _dismiss_notification(panel: PanelContainer):
	if not is_instance_valid(panel) or not panel.is_inside_tree():
		return
	
	# Remove from stack
	var idx := notification_stack.find(panel)
	if idx >= 0:
		notification_stack.remove_at(idx)
	
	# Slide out
	var tween := create_tween()
	tween.tween_property(panel, "position:y", -NOTIFICATION_HEIGHT - 20, SLIDE_DURATION * 0.7).set_trans(Tween.TRANS_BACK).set_ease(Tween.EASE_IN)
	tween.tween_callback(panel.queue_free)
	
	# Reposition remaining notifications
	_reposition_stack()

func _reposition_stack():
	for i in range(notification_stack.size()):
		if is_instance_valid(notification_stack[i]):
			var target_y := _get_stack_y(i)
			var tween := create_tween()
			tween.tween_property(notification_stack[i], "position:y", target_y, 0.2)

func _get_stack_y(index: int) -> float:
	return LakskongenTheme.SPACE_SM + index * (NOTIFICATION_HEIGHT + NOTIFICATION_GAP)
