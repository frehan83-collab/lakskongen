class_name PlanarReflection
extends Node
## The fjord's mirror (G2-03): calm water reflects the mountains, the boats,
## the sky and the aurora, not just the sky's radiance.
##
## A SubViewport renders the scene from the main camera mirrored in the
## water plane (half the window's resolution on High), into a linear HDR
## texture. The water shader looks each pixel up in it by projecting the
## surface point with the very view-projection that rendered the texture
## (planar_view_proj), so a texture rendered a frame earlier (30 Hz at
## COAST) still lines up with what it shows; the surface normal tilts the
## lookup, which breaks the mirror up under ripples and chop. The water
## blends it in by roughness and fresnel, and falls back to the sky's
## radiance where the sea is too rough, at the texture's edges, and on the
## Low tier (no mirror at all: the reflection probe and the sky light the
## water as before).
##
## Godot cannot clip a camera to a plane, so the mirrored camera, which sits
## under the water, would see what hangs below the surface (nets, the
## sinker tube, a lice skirt, the fish) in front of the reflection of what
## stands above it. Those parts sit on NO_MIRROR_LAYER, which the mirrored
## camera leaves out, as do world labels and the rain and snow round the
## camera. The water itself (WaterGrid.RENDER_LAYER) never mirrors itself.
##
## EnvironmentDirector owns the mirror's look like the main one's: it writes
## `environment` (fog, ambient, background; linear tone mapping, so the
## texture holds scene radiance the main pass exposes and tone-maps once),
## tells this node the quality tier's settings (configure()) and whether the
## sea is calm enough anywhere to show a mirror (set_wanted()). This node
## owns the viewport, the mirrored camera, the update cadence and the
## water's mirror uniforms.

## Render layer of what must not appear in the mirror: everything wholly
## under the water (it would cover the reflection), world labels, and the
## weather particles round the camera. Render layer 19.
const NO_MIRROR_LAYER := 1 << 18
## Camera distance (m) from which the mirror updates at the tier's far rate
## (FjordCamera.COAST_BAND: the COAST zoom band).
const FAR_DISTANCE_M := 1600.0
## The water's mirror uniforms.
const TEXTURE_UNIFORM := "planar_reflection"
const VIEW_PROJ_UNIFORM := "planar_view_proj"
const MIX_UNIFORM := "planar_mix"
## Smallest mirror (px) on the short side; below it the lookup turns to mush.
const MIN_HEIGHT_PX := 180
## Fade time (s) when the mirror switches on or off, so nothing pops.
const FADE_S := 0.6

## The camera the mirror follows.
@export var camera: Camera3D
## The water surface whose material shows the mirror.
@export var water_grid: WaterGrid
## Height of the mirror plane (the still water level, m).
@export var water_level: float = 0.0

## The mirror camera's environment (EnvironmentDirector writes it).
var environment := Environment.new()
## Tier settings (configure()): on, resolution scale, far update rate (Hz).
var tier_enabled := true
var resolution_scale := 0.5
var far_hz := 30.0
var near_hz := 0.0
## Mirror passes rendered so far (tests read it).
var renders := 0
## The view-projection of the last rendered pass.
var view_proj := Projection.IDENTITY

var _viewport: SubViewport
var _camera: Camera3D
var _wanted := true
var _world_visible := true
var _since := INF
var _mix := 0.0
var _texture_bound := false


func _init() -> void:
	# After the camera rig (FjordCamera) has placed the camera for this frame.
	process_priority = 100


func _ready() -> void:
	_viewport = SubViewport.new()
	_viewport.name = "MirrorViewport"
	_viewport.use_hdr_2d = true
	RenderQuality.apply_mirror_viewport(_viewport)
	_viewport.use_debanding = false
	_viewport.positional_shadow_atlas_size = 0
	# Coarser automatic LODs: the mirror is half resolution and rippled.
	_viewport.mesh_lod_threshold = 2.0
	_viewport.render_target_clear_mode = SubViewport.CLEAR_MODE_ALWAYS
	_viewport.render_target_update_mode = SubViewport.UPDATE_DISABLED
	_viewport.audio_listener_enable_3d = false
	_viewport.gui_disable_input = true
	_viewport.size = Vector2i(64, 64)
	add_child(_viewport)
	_camera = Camera3D.new()
	_camera.name = "MirrorCamera"
	_camera.cull_mask = mirror_cull_mask()
	_camera.environment = environment
	var attrs := CameraAttributesPractical.new()
	attrs.dof_blur_far_enabled = false
	attrs.dof_blur_near_enabled = false
	attrs.auto_exposure_enabled = false
	_camera.attributes = attrs
	_viewport.add_child(_camera)
	_camera.current = true


## Layers the mirrored camera draws: all but the water and NO_MIRROR_LAYER.
static func mirror_cull_mask() -> int:
	return 0xFFFFF & ~(WaterGrid.RENDER_LAYER | NO_MIRROR_LAYER)


## The tier's settings (EnvironmentDirector, from RenderQuality): whether the
## mirror exists, its resolution as a share of the window, and its update
## rates (Hz) near and far (0: every frame).
func configure(enabled: bool, scale: float, near_rate_hz: float, far_rate_hz: float) -> void:
	tier_enabled = enabled
	resolution_scale = clampf(scale, 0.1, 1.0)
	near_hz = maxf(near_rate_hz, 0.0)
	far_hz = maxf(far_rate_hz, 0.0)
	_since = INF


## Whether the scene wants a mirror now (EnvironmentDirector): a calm enough
## sea somewhere, the camera above the water.
func set_wanted(wanted: bool) -> void:
	_wanted = wanted


## The world is on screen (false behind an opaque full-screen panel).
func set_world_visible(world_visible: bool) -> void:
	_world_visible = world_visible


## The mirror renders and the water shows it.
func is_active() -> bool:
	return tier_enabled and _wanted and _world_visible and camera != null and is_inside_tree()


func get_texture() -> Texture2D:
	return _viewport.get_texture() if _viewport != null else null


func get_mirror_camera() -> Camera3D:
	return _camera


func get_viewport_size() -> Vector2i:
	return _viewport.size if _viewport != null else Vector2i.ZERO


## The share of the water's reflection the mirror holds now (0-1, faded).
func get_mix() -> float:
	return _mix


## Update interval (s) at a camera distance: the tier's far rate from the
## COAST band, its near rate (0 = every frame) inside it.
func interval_for(distance: float) -> float:
	var hz := far_hz if distance >= FAR_DISTANCE_M else near_hz
	return 1.0 / hz if hz > 0.0 else 0.0


## The camera transform mirrored in the plane y = level: a proper rotation
## (M B M with M = diag(1, -1, 1)) that looks along the mirrored view, its
## image upside down; the water projects with its matrix, so the flip does
## not matter.
static func mirror_transform(t: Transform3D, level: float) -> Transform3D:
	var m := Basis(Vector3(1.0, 0.0, 0.0), Vector3(0.0, -1.0, 0.0), Vector3(0.0, 0.0, 1.0))
	return Transform3D(m * t.basis * m, Vector3(t.origin.x, 2.0 * level - t.origin.y, t.origin.z))


## Where a world point lands in the mirror texture (UV, 0-1) for a
## view-projection; the shader's planar_uv() does the same.
static func uv_of(vp: Projection, p: Vector3) -> Vector2:
	var c := vp * Vector4(p.x, p.y, p.z, 1.0)
	var w := maxf(c.w, 1e-4)
	return Vector2(c.x / w * 0.5 + 0.5, 0.5 - c.y / w * 0.5)


func _process(delta: float) -> void:
	var mat := water_grid.material if water_grid != null else null
	var active := is_active() and camera.global_position.y > water_level + 0.5
	_mix = move_toward(_mix, 1.0 if active else 0.0, delta / FADE_S if delta > 0.0 else 1.0)
	if mat != null:
		mat.set_shader_parameter(MIX_UNIFORM, _mix)
	if _mix <= 0.0 and not active:
		_since = INF
		return
	_resize()
	_since += delta
	var interval := interval_for(_distance())
	# Render on the frame that crosses the interval (half a frame early, so
	# 30 Hz at 60 fps is every second frame, not every third).
	if _since + 0.5 * delta < interval:
		return
	_since = 0.0
	_render(mat)


func _distance() -> float:
	if camera != null and camera.has_method("get_current_distance"):
		return float(camera.call("get_current_distance"))
	return 0.0


func _resize() -> void:
	var win := get_window()
	var full: Vector2 = Vector2(win.size) if win != null else Vector2(1280.0, 720.0)
	var target := Vector2i(full * resolution_scale)
	if target.y < MIN_HEIGHT_PX and full.y > 0.0:
		target = Vector2i(int(full.x * MIN_HEIGHT_PX / full.y), MIN_HEIGHT_PX)
	target = target.max(Vector2i(16, 16))
	if _viewport.size != target:
		_viewport.size = target


func _render(mat: ShaderMaterial) -> void:
	_camera.fov = camera.fov
	_camera.near = camera.near
	_camera.far = camera.far
	_camera.keep_aspect = camera.keep_aspect
	_camera.h_offset = camera.h_offset
	_camera.v_offset = -camera.v_offset
	_camera.global_transform = mirror_transform(camera.global_transform, water_level)
	view_proj = _camera.get_camera_projection() * Projection(_camera.get_camera_transform().affine_inverse())
	_viewport.render_target_update_mode = SubViewport.UPDATE_ONCE
	renders += 1
	if mat != null:
		if not _texture_bound:
			mat.set_shader_parameter(TEXTURE_UNIFORM, _viewport.get_texture())
			_texture_bound = true
		mat.set_shader_parameter(VIEW_PROJ_UNIFORM, view_proj)
