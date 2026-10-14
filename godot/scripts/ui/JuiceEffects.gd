extends Node
class_name JuiceEffects

## JuiceEffects — Game Feel / "Juice" Helper
## Autoload singleton providing premium animation effects.
## Norwegian fjord aesthetic: smooth, deliberate, satisfying.

# ── Screen Shake ──────────────────────────────────────────────────────────────

static var _shake_intensity: float = 0.0
static var _shake_duration: float = 0.0
static var _shake_timer: float = 0.0
static var _original_camera_offset: Vector2 = Vector2.ZERO

## Shake the screen. Use for critical events (storms, escapes, bankruptcy).
## intensity: pixels of displacement (5-20 typical)
## duration: seconds (0.2-0.5 typical)
static func screen_shake(viewport: Viewport, intensity: float = 10.0, duration: float = 0.3) -> void:
	var canvas = viewport.get_camera_2d()
	if canvas:
		_original_camera_offset = canvas.offset
		var tween = viewport.create_tween()
		var steps = int(duration / 0.03)
		for i in range(steps):
			var decay = 1.0 - (float(i) / steps)
			var offset_x = randf_range(-intensity, intensity) * decay
			var offset_y = randf_range(-intensity, intensity) * decay
			tween.tween_property(canvas, "offset", _original_camera_offset + Vector2(offset_x, offset_y), 0.03)
		tween.tween_property(canvas, "offset", _original_camera_offset, 0.05)

# ── Number Lerp (Smooth Counter) ─────────────────────────────────────────────

## Smoothly animate a Label's text from one number to another.
## Perfect for cash balance, fish count, scores.
## format: "NOK %s" or "%s fisk" etc.
static func number_lerp(label: Label, from_val: float, to_val: float, duration: float = 0.8, format_str: String = "%s", decimals: int = 0) -> Tween:
	var tween = label.create_tween()
	tween.set_ease(Tween.EASE_OUT)
	tween.set_trans(Tween.TRANS_EXPO)
	
	var counter = {"value": from_val}
	tween.tween_method(
		func(val: float):
			counter["value"] = val
			if decimals == 0:
				label.text = format_str % _format_number(int(val))
			else:
				label.text = format_str % ("%.{0}f".format([decimals]) % val),
		from_val, to_val, duration
	)
	return tween

## Format large numbers with thousand separators (Norwegian style: 1 234 567)
static func _format_number(n: int) -> String:
	var s = str(abs(n))
	var result = ""
	var count = 0
	for i in range(s.length() - 1, -1, -1):
		if count > 0 and count % 3 == 0:
			result = " " + result
		result = s[i] + result
		count += 1
	if n < 0:
		result = "-" + result
	return result

# ── Pulse Effect ──────────────────────────────────────────────────────────────

## Heartbeat pulse on any Control node. Great for achievements, alerts.
static func pulse(node: Control, scale_factor: float = 1.15, duration: float = 0.3) -> Tween:
	var original_scale = node.scale
	var tween = node.create_tween()
	tween.set_ease(Tween.EASE_OUT)
	tween.set_trans(Tween.TRANS_ELASTIC)
	tween.tween_property(node, "scale", original_scale * scale_factor, duration * 0.4)
	tween.tween_property(node, "scale", original_scale, duration * 0.6)
	return tween

## Double-pulse for extra emphasis (critical alerts, big achievements).
static func double_pulse(node: Control, scale_factor: float = 1.2, duration: float = 0.5) -> Tween:
	var original_scale = node.scale
	var tween = node.create_tween()
	tween.set_ease(Tween.EASE_OUT)
	tween.set_trans(Tween.TRANS_ELASTIC)
	tween.tween_property(node, "scale", original_scale * scale_factor, duration * 0.2)
	tween.tween_property(node, "scale", original_scale, duration * 0.2)
	tween.tween_property(node, "scale", original_scale * (scale_factor * 0.8), duration * 0.2)
	tween.tween_property(node, "scale", original_scale, duration * 0.4)
	return tween

# ── Flash Color ───────────────────────────────────────────────────────────────

## Brief color flash on a Control. Use for damage (red), income (green), warning (amber).
static func flash_color(node: Control, flash: Color, duration: float = 0.4) -> Tween:
	var original = node.modulate
	var tween = node.create_tween()
	tween.tween_property(node, "modulate", flash, duration * 0.2)
	tween.tween_property(node, "modulate", original, duration * 0.8)
	return tween

## Red damage flash
static func flash_damage(node: Control) -> Tween:
	return flash_color(node, Color(1.0, 0.3, 0.2, 1.0), 0.5)

## Green income flash
static func flash_income(node: Control) -> Tween:
	return flash_color(node, Color("#2E6B45"), 0.4)

## Amber warning flash
static func flash_warning(node: Control) -> Tween:
	return flash_color(node, Color(1.0, 0.8, 0.2, 1.0), 0.4)

# ── Slide Animations ──────────────────────────────────────────────────────────

## Slide a panel in from the right edge
static func slide_in_right(node: Control, duration: float = 0.35) -> Tween:
	var target_pos = node.position
	node.position.x = node.get_viewport_rect().size.x + 50
	node.modulate.a = 0.0
	var tween = node.create_tween()
	tween.set_ease(Tween.EASE_OUT)
	tween.set_trans(Tween.TRANS_BACK)
	tween.set_parallel(true)
	tween.tween_property(node, "position", target_pos, duration)
	tween.tween_property(node, "modulate:a", 1.0, duration * 0.6)
	return tween

## Slide a panel out to the right
static func slide_out_right(node: Control, duration: float = 0.25) -> Tween:
	var tween = node.create_tween()
	tween.set_ease(Tween.EASE_IN)
	tween.set_trans(Tween.TRANS_BACK)
	tween.set_parallel(true)
	var off_x = node.get_viewport_rect().size.x + 50
	tween.tween_property(node, "position:x", off_x, duration)
	tween.tween_property(node, "modulate:a", 0.0, duration * 0.8)
	return tween

## Slide in from bottom (for toast notifications)
static func slide_in_bottom(node: Control, duration: float = 0.3) -> Tween:
	var target_pos = node.position
	node.position.y = node.get_viewport_rect().size.y + 50
	node.modulate.a = 0.0
	var tween = node.create_tween()
	tween.set_ease(Tween.EASE_OUT)
	tween.set_trans(Tween.TRANS_CUBIC)
	tween.set_parallel(true)
	tween.tween_property(node, "position", target_pos, duration)
	tween.tween_property(node, "modulate:a", 1.0, duration * 0.5)
	return tween

# ── Fade ──────────────────────────────────────────────────────────────────────

## Fade in a control
static func fade_in(node: Control, duration: float = 0.3) -> Tween:
	node.modulate.a = 0.0
	var tween = node.create_tween()
	tween.tween_property(node, "modulate:a", 1.0, duration)
	return tween

## Fade out a control
static func fade_out(node: Control, duration: float = 0.3) -> Tween:
	var tween = node.create_tween()
	tween.tween_property(node, "modulate:a", 0.0, duration)
	return tween

# ── Button Press Feedback ───────────────────────────────────────────────────

## Quick scale punch for button presses. Pure juice: no logic changes.
static func button_pop(button: Control, scale_factor: float = 0.94, duration: float = 0.12) -> Tween:
	var tween := button.create_tween()
	tween.set_ease(Tween.EASE_OUT)
	tween.set_trans(Tween.TRANS_BACK)
	tween.tween_property(button, "scale", Vector2.ONE * scale_factor, duration * 0.5)
	tween.tween_property(button, "scale", Vector2.ONE, duration * 0.5)
	return tween

## Wire press feedback onto a button without touching its existing connections.
static func attach_press_feedback(button: BaseButton) -> void:
	if button == null:
		return
	if button.has_meta("_juice_feedback_attached"):
		return
	button.set_meta("_juice_feedback_attached", true)
	button.pressed.connect(func(): button_pop(button))

# ── Panel Transitions ───────────────────────────────────────────────────────

## Soft enter for tab panels: fade + rise. Call after panel.show().
static func panel_enter(panel: Control, duration: float = 0.28) -> Tween:
	panel.modulate.a = 0.0
	panel.position.y += 14.0
	var tween := panel.create_tween()
	tween.set_ease(Tween.EASE_OUT)
	tween.set_trans(Tween.TRANS_CUBIC)
	tween.set_parallel(true)
	tween.tween_property(panel, "modulate:a", 1.0, duration)
	tween.tween_property(panel, "position:y", panel.position.y - 14.0, duration)
	return tween

# ── Floating Text (Damage Numbers / Income Pop) ──────────────────────────────

## Create a floating text that rises and fades (like damage numbers in RPGs).
## parent: node to add the label to
## text: "+5 000 NOK" or "-200 fisk"
## start_pos: position to spawn at
## color: text color
static func floating_text(parent: Node, text: String, start_pos: Vector2, color: Color = Color.WHITE, duration: float = 1.5) -> void:
	var label = Label.new()
	label.text = text
	LakskongenTheme.set_text_color(label, color)
	LakskongenTheme.set_size(label, 18)
	label.position = start_pos
	label.z_index = 100
	parent.add_child(label)
	
	var tween = label.create_tween()
	tween.set_parallel(true)
	tween.tween_property(label, "position:y", start_pos.y - 60, duration)
	tween.tween_property(label, "modulate:a", 0.0, duration)
	tween.chain().tween_callback(label.queue_free)
