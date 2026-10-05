extends CanvasLayer

## TutorialHighlight - Visual highlight system for tutorial UI elements
## Draws circles/rectangles around focused elements with pulsing animation

# Visual settings
const HIGHLIGHT_COLOR = Color("#2AB7B5")  # Accent color
const BACKDROP_COLOR = Color(0.0, 0.0, 0.0, 0.7)  # Semi-transparent black
const PULSE_DURATION = 1.5  # seconds
const PULSE_SCALE_MIN = 0.95
const PULSE_SCALE_MAX = 1.05
const ARROW_SIZE = 40.0
const ARROW_OFFSET = 20.0

# State
var active: bool = false
var target_element: Control = null
var highlight_type: String = "rectangle"  # rectangle, circle, arrow, none
var pulse_time: float = 0.0

# Nodes
var backdrop: ColorRect
var highlight_container: Control
var arrow: Polygon2D

func _ready():
	layer = 100  # Always on top
	_setup_backdrop()
	_setup_highlight_container()
	_setup_arrow()
	hide_highlight()

func _setup_backdrop():
	backdrop = ColorRect.new()
	backdrop.color = BACKDROP_COLOR
	backdrop.mouse_filter = Control.MOUSE_FILTER_IGNORE  # Click-through
	add_child(backdrop)


func _setup_arrow():
	arrow = Polygon2D.new()
	arrow.color = HIGHLIGHT_COLOR
	arrow.visible = false
	# Arrow pointing down: triangle
	var arrow_points = PackedVector2Array([
		Vector2(0, -ARROW_SIZE),
		Vector2(ARROW_SIZE * 0.5, 0),
		Vector2(-ARROW_SIZE * 0.5, 0)
	])
	arrow.polygon = arrow_points
	highlight_container.add_child(arrow)

func _process(delta: float):
	if not active or target_element == null:
		return
	
	# Update pulse animation
	pulse_time += delta
	var pulse_scale = lerp(PULSE_SCALE_MIN, PULSE_SCALE_MAX, 
		(sin(pulse_time * TAU / PULSE_DURATION) + 1.0) / 2.0)
	
	# Update highlight position and size
	_update_highlight(pulse_scale)

## PUBLIC API

## Highlight a UI element
func highlight_element(element: Control, type: String = "rectangle"):
	if element == null:
		push_error("TutorialHighlight: Cannot highlight null element")
		return
	
	target_element = element
	highlight_type = type
	active = true
	pulse_time = 0.0
	
	# Resize backdrop to full screen
	var viewport_size = get_viewport().get_visible_rect().size
	backdrop.size = viewport_size
	backdrop.visible = true
	
	# Show highlight
	highlight_container.visible = true
	arrow.visible = (type == "arrow")
	
	highlight_container.queue_redraw()
	
	print("TutorialHighlight: Highlighting %s with type %s" % [element.name, type])

## Clear highlight
func hide_highlight():
	active = false
	target_element = null
	backdrop.visible = false
	highlight_container.visible = false
	arrow.visible = false
	
	# Clear any drawn shapes
	highlight_container.queue_redraw()

## Check if highlighting is active
func is_active() -> bool:
	return active

## INTERNAL METHODS

## Update highlight position and appearance
func _update_highlight(pulse_scale: float):
	if target_element == null or not is_instance_valid(target_element):
		hide_highlight()
		return
	
	# Get element's global rect
	var rect = target_element.get_global_rect()
	
	# Apply pulse scale from center
	var center = rect.get_center()
	var scaled_size = rect.size * pulse_scale
	var scaled_pos = center - scaled_size / 2.0
	var scaled_rect = Rect2(scaled_pos, scaled_size)
	
	# Draw based on type
	highlight_container.queue_redraw()
	
	# Position arrow if needed
	if highlight_type == "arrow":
		arrow.position = Vector2(center.x, scaled_rect.position.y - ARROW_OFFSET - ARROW_SIZE)
		arrow.rotation = pulse_time * 0.5  # Gentle rotation

## Draw highlight shapes
func _draw_highlight_shapes():
	if not active or target_element == null:
		return
	
	var rect = target_element.get_global_rect()
	var center = rect.get_center()
	
	# Apply current pulse
	var pulse_scale = lerp(PULSE_SCALE_MIN, PULSE_SCALE_MAX, 
		(sin(pulse_time * TAU / PULSE_DURATION) + 1.0) / 2.0)
	var scaled_size = rect.size * pulse_scale
	var scaled_pos = center - scaled_size / 2.0
	var scaled_rect = Rect2(scaled_pos, scaled_size)
	
	# Draw based on type
	match highlight_type:
		"rectangle":
			_draw_rectangle_highlight(scaled_rect)
		"circle":
			_draw_circle_highlight(scaled_rect)
		"arrow":
			_draw_rectangle_highlight(scaled_rect)  # Still show rectangle + arrow

## Draw rectangle highlight
func _draw_rectangle_highlight(rect: Rect2):
	var border_width = 3.0
	
	# Draw outer border
	highlight_container.draw_rect(rect, HIGHLIGHT_COLOR, false, border_width)
	
	# Draw corner accents (makes it fancier)
	var corner_length = 20.0
	var corners = [
		[rect.position, rect.position + Vector2(corner_length, 0)],
		[rect.position, rect.position + Vector2(0, corner_length)],
		[rect.position + Vector2(rect.size.x, 0), rect.position + Vector2(rect.size.x - corner_length, 0)],
		[rect.position + Vector2(rect.size.x, 0), rect.position + Vector2(rect.size.x, corner_length)],
		[rect.position + Vector2(0, rect.size.y), rect.position + Vector2(corner_length, rect.size.y)],
		[rect.position + Vector2(0, rect.size.y), rect.position + Vector2(0, rect.size.y - corner_length)],
		[rect.position + rect.size, rect.position + rect.size - Vector2(corner_length, 0)],
		[rect.position + rect.size, rect.position + rect.size - Vector2(0, corner_length)]
	]
	
	for corner in corners:
		highlight_container.draw_line(corner[0], corner[1], HIGHLIGHT_COLOR, border_width + 1.0)

## Draw circle highlight
func _draw_circle_highlight(rect: Rect2):
	var center = rect.get_center()
	var radius = max(rect.size.x, rect.size.y) / 2.0 + 10.0
	var border_width = 3.0
	
	# Draw circle outline
	highlight_container.draw_arc(center, radius, 0, TAU, 64, HIGHLIGHT_COLOR, border_width)
	
	# Draw pulsing circle segments (visual flair)
	var segment_count = 8
	var segment_angle = TAU / segment_count
	for i in range(segment_count):
		if i % 2 == 0:  # Only draw every other segment
			var start_angle = i * segment_angle + pulse_time
			var end_angle = start_angle + segment_angle * 0.4
			highlight_container.draw_arc(center, radius + 5.0, start_angle, end_angle, 16, 
				Color(HIGHLIGHT_COLOR, 0.5), border_width * 1.5)

## Override _draw to render highlights
func _draw():
	pass  # Drawing is handled by highlight_container

# Custom draw for highlight_container
class HighlightDrawer extends Control:
	var parent: Node
	
	func _init(p: Node):
		parent = p
	
	func _draw():
		if parent.has_method("_draw_highlight_shapes"):
			parent._draw_highlight_shapes()

func _setup_highlight_container():
	highlight_container = HighlightDrawer.new(self)
	highlight_container.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(highlight_container)
