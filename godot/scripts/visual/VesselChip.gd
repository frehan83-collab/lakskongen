class_name VesselChip
extends Node3D

## A status chip floating above a vessel: a D13 "Kyst & Stål" Ink glass
## pill with a coloured dot and a short Norwegian label ("Avlusing · om 2 d"),
## billboarded and at a constant size on screen (the same scale law as the
## site labels, PenCage.style_label). Roadmap A3-01: a boat's job state
## shows here, never in its hull colour. The dot carries the colour; the
## text names the state, so colour is never the only cue.

## Ink glass (#0B2A3A at 90%) and Ink text (#E8EEF0).
const FILL := Color(0.043, 0.165, 0.227, 0.9)
const TEXT_COLOR := Color(0.910, 0.933, 0.941)
## World units per screen pixel at unit view depth (PenCage labels use the same).
const PIXEL_SIZE := 0.0005
const FONT_SIZE := 30
const HEIGHT_PX := 44.0
const PAD_PX := 14.0
const DOT_PX := 12.0
const DOT_GAP_PX := 8.0
## The chip sits this many pixels above its anchor, clear of the site
## labels (PenCage.LABEL_HEIGHT) that share the screen at FJORD zoom.
const LIFT_PX := 38.0
const CHIP_SHADER := preload("res://shaders/world_chip.gdshader")

var _text := ""
var _accent := Color.WHITE
var _quad: MeshInstance3D
var _label: Label3D
var _material: ShaderMaterial

## max_distance: past this camera distance (m) the chip fades out, so far
## boats do not pile chips up on the horizon.
func _init(max_distance: float = 1400.0) -> void:
	name = "VesselChip"
	_material = ShaderMaterial.new()
	_material.shader = CHIP_SHADER
	_material.render_priority = 1
	_material.set_shader_parameter("fill", FILL)
	_material.set_shader_parameter("pixel_size", PIXEL_SIZE)
	_material.set_shader_parameter("dot_px", DOT_PX)
	_material.set_shader_parameter("pad_px", PAD_PX)
	_material.set_shader_parameter("offset_px", Vector2(0.0, LIFT_PX))
	var quad_mesh := QuadMesh.new()
	quad_mesh.size = Vector2.ONE
	_quad = MeshInstance3D.new()
	_quad.name = "Pill"
	_quad.mesh = quad_mesh
	_quad.material_override = _material
	_quad.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	# The shader grows the 1 m quad with distance; keep it from being culled.
	_quad.extra_cull_margin = 200.0
	add_child(_quad)
	_label = Label3D.new()
	_label.name = "Text"
	_label.billboard = BaseMaterial3D.BILLBOARD_ENABLED
	_label.fixed_size = true
	_label.pixel_size = PIXEL_SIZE
	_label.font_size = FONT_SIZE
	_label.outline_size = 0
	_label.modulate = TEXT_COLOR
	_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	_label.render_priority = 3
	_label.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(_label)
	for node: GeometryInstance3D in [_quad, _label]:
		node.visibility_range_end = max_distance
		node.visibility_range_end_margin = max_distance * 0.1
		node.visibility_range_fade_mode = GeometryInstance3D.VISIBILITY_RANGE_FADE_SELF

## Show `text` with an `accent` dot and rim.
func set_chip(text: String, accent: Color) -> void:
	_text = text
	_accent = accent
	_label.text = text
	var font: Font = _label.font if _label.font != null else ThemeDB.fallback_font
	var text_w := font.get_string_size(text, HORIZONTAL_ALIGNMENT_LEFT, -1, FONT_SIZE).x if font else 8.0 * text.length()
	var dot_w := DOT_PX + DOT_GAP_PX
	var width := PAD_PX * 2.0 + dot_w + text_w
	_quad.set_instance_shader_parameter("size_px", Vector2(width, HEIGHT_PX))
	_quad.set_instance_shader_parameter("accent", accent)
	# Centre the text in the space right of the dot (Label3D offset is in px).
	_label.offset = Vector2(dot_w * 0.5, LIFT_PX)

func get_text() -> String:
	return _text

func get_accent() -> Color:
	return _accent

## The pill's size on screen, in pixels.
func get_size_px() -> Vector2:
	return _quad.get_instance_shader_parameter("size_px")
