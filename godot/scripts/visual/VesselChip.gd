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

## Screen edges (gfx-polish-1): a chip whose boat shows on screen slides
## inward so its whole pill stays at least EDGE_MARGIN_PX inside the view (08b
## cut a barge's chip in half at the left edge); once the whole boat is out
## of the view (its anchor more than half the boat's length, plus
## OFFSCREEN_PX, beyond an edge) the chip hides, so no chip points at nothing.
## Shifts under SHIFT_STEP_PX are not re-applied.
const EDGE_MARGIN_PX := 8.0
const OFFSCREEN_PX := 4.0
const SHIFT_STEP_PX := 0.5

## The eye's exposure the chip is compensated for now (G3-05): the night's
## exposure (up to 16x, EnvironmentDirector) would otherwise bloom the text
## and wash the Ink glass out; the chip keeps its day brightness on screen.
var exposure_comp := 1.0
## Re-tint when the compensation moves by more than this share.
const EXPOSURE_STEP := 0.02

var _text := ""
var _accent := Color.WHITE
## The chip's slide off the screen edge (chip pixels, view up = +y) and
## whether its boat is on screen (tests read them).
var edge_shift := Vector2.ZERO
var on_screen := true
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
		# A chip is not part of the world: no reflection in the water (G2-03).
		node.layers = PlanarReflection.NO_MIRROR_LAYER
		node.visibility_range_end = max_distance
		node.visibility_range_end_margin = max_distance * 0.1
		node.visibility_range_fade_mode = GeometryInstance3D.VISIBILITY_RANGE_FADE_SELF

## Show `text` with an `accent` dot and rim.
func set_chip(text: String, accent: Color) -> void:
	# Called on every sim refresh: measuring the text shapes it, so an
	# unchanged chip is left alone.
	if text == _text and accent == _accent and _label.text == text:
		return
	_text = text
	_accent = accent
	_label.text = text
	var font: Font = _label.font if _label.font != null else ThemeDB.fallback_font
	var text_w := font.get_string_size(text, HORIZONTAL_ALIGNMENT_LEFT, -1, FONT_SIZE).x if font else 8.0 * text.length()
	var dot_w := DOT_PX + DOT_GAP_PX
	var width := PAD_PX * 2.0 + dot_w + text_w
	_quad.set_instance_shader_parameter("size_px", Vector2(width, HEIGHT_PX))
	_quad.set_instance_shader_parameter("accent", accent)
	_place_text()


## Centre the text in the space right of the dot, with the pill's slide off
## the screen edge (Label3D offset is in px, up positive like the pill's).
func _place_text() -> void:
	_label.offset = Vector2((DOT_PX + DOT_GAP_PX) * 0.5, LIFT_PX) + edge_shift

func get_text() -> String:
	return _text

func get_accent() -> Color:
	return _accent

## The pill's size on screen, in pixels.
func get_size_px() -> Vector2:
	return _quad.get_instance_shader_parameter("size_px")

func _process(_delta: float) -> void:
	var director := get_tree().get_first_node_in_group(RenderQuality.DIRECTOR_GROUP) as EnvironmentDirector
	var exposure := float(director.get_composed().get("exposure", 1.0)) if director != null else 1.0
	set_exposure(exposure)
	var cam := get_viewport().get_camera_3d()
	if cam != null and cam.projection == Camera3D.PROJECTION_PERSPECTIVE:
		keep_on_screen(cam, get_viewport().get_visible_rect().size)


## Slide the pill and its text inward to keep the whole chip EDGE_MARGIN_PX
## inside a view of `view_size` (viewport px) seen through `cam`; hide it
## while its boat is out of the view or behind the camera.
func keep_on_screen(cam: Camera3D, view_size: Vector2) -> void:
	var anchor := global_position
	var visible_now := not cam.is_position_behind(anchor)
	var shift := Vector2.ZERO
	if visible_now:
		var at := cam.unproject_position(anchor)
		var reach := OFFSCREEN_PX + boat_reach_px(cam, view_size, anchor, vessel_half_length_m())
		visible_now = at.x >= -reach and at.x <= view_size.x + reach \
			and at.y >= -reach and at.y <= view_size.y + reach
		if visible_now:
			shift = edge_shift_for(at, get_size_px(), cam.fov, view_size)
	if visible_now != on_screen:
		on_screen = visible_now
		_quad.visible = visible_now
		_label.visible = visible_now
	if shift.distance_to(edge_shift) > SHIFT_STEP_PX:
		edge_shift = shift
		_material.set_shader_parameter("offset_px", Vector2(0.0, LIFT_PX) + edge_shift)
		_place_text()


## Half the length (m) of the boat this chip rides (its VesselModel's
## length_m), 0 when the parent has none.
func vessel_half_length_m() -> float:
	var boat := get_parent()
	if boat == null:
		return 0.0
	var length: Variant = boat.get("length_m")
	return float(length) * 0.5 if length != null else 0.0


## How far (viewport px) `half_length_m` of boat round `anchor` reaches on a
## view of `view_size` through `cam` (perspective: f / depth px a metre).
static func boat_reach_px(cam: Camera3D, view_size: Vector2, anchor: Vector3, half_length_m: float) -> float:
	var depth := -(cam.global_transform.affine_inverse() * anchor).z
	if depth <= 0.0 or half_length_m <= 0.0:
		return 0.0
	var focal_px := view_size.y / (2.0 * tan(deg_to_rad(cam.fov) * 0.5))
	return half_length_m * focal_px / depth


## The slide (chip px, up = +y) that keeps a pill of `size_px` lifted
## LIFT_PX above its anchor at screen point `at` EDGE_MARGIN_PX inside a view
## of `view_size` with a vertical field of view `fov_deg`. The chip scales
## with depth like a fixed-size Label3D: a chip pixel spans
## PIXEL_SIZE * view height / (2 tan(fov / 2)) screen pixels.
static func edge_shift_for(at: Vector2, size_px: Vector2, fov_deg: float, view_size: Vector2) -> Vector2:
	var k := PIXEL_SIZE * view_size.y / (2.0 * tan(deg_to_rad(fov_deg) * 0.5))
	if k <= 0.0:
		return Vector2.ZERO
	var half := size_px * k * 0.5
	var centre := at - Vector2(0.0, LIFT_PX * k)
	var dx := maxf(EDGE_MARGIN_PX + half.x - centre.x, 0.0) - maxf(centre.x + half.x - (view_size.x - EDGE_MARGIN_PX), 0.0)
	var dy := maxf(EDGE_MARGIN_PX + half.y - centre.y, 0.0) - maxf(centre.y + half.y - (view_size.y - EDGE_MARGIN_PX), 0.0)
	# Screen y runs down, the chip's up.
	return Vector2(dx, -dy) / k

## Keep the chip's screen brightness at the eye's `exposure` (the text's and
## the pill's light divided by it, in linear light).
func set_exposure(exposure: float) -> void:
	var comp := 1.0 / maxf(exposure, 1e-3)
	if absf(comp - exposure_comp) <= EXPOSURE_STEP * exposure_comp:
		return
	exposure_comp = comp
	var lin := TEXT_COLOR.srgb_to_linear()
	var text := Color(lin.r * comp, lin.g * comp, lin.b * comp).linear_to_srgb()
	text.a = TEXT_COLOR.a
	_label.modulate = text
	_material.set_shader_parameter("exposure_comp", comp)
