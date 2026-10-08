@tool
class_name FjordCamera
extends Camera3D
## 3D Fjord Camera with zoom levels, tilt-shift, and smooth transitions.
## Controls: WASD/Arrows to pan, scroll to zoom, middle-click to orbit.

# === Zoom Levels ===
enum ZoomLevel { COAST, FJORD, FARM, UNDERWATER }

@export var current_zoom: ZoomLevel = ZoomLevel.FJORD

# Zoom presets: [altitude, fov, pitch_deg, tilt_shift_focus]
const ZOOM_PRESETS := {
	ZoomLevel.COAST:      { "altitude": 1000.0, "fov": 45.0, "pitch": -60.0, "tilt_focus": 0.45 },
	ZoomLevel.FJORD:      { "altitude": 200.0,  "fov": 50.0, "pitch": -50.0, "tilt_focus": 0.5 },
	ZoomLevel.FARM:       { "altitude": 50.0,   "fov": 55.0, "pitch": -40.0, "tilt_focus": 0.55 },
	ZoomLevel.UNDERWATER: { "altitude": -10.0,  "fov": 70.0, "pitch": -10.0, "tilt_focus": 0.5 },
}

# === Movement Settings ===
@export_group("Movement")
@export var pan_speed: float = 200.0
@export var pan_acceleration: float = 8.0
@export var zoom_speed: float = 5.0
@export var orbit_speed: float = 0.003
@export var transition_speed: float = 2.0

# === Auto Orbit (vista background drift) ===
@export_group("AutoOrbit")
@export var auto_orbit_enabled: bool = false
@export var auto_orbit_speed: float = 0.03

# === Boundaries ===
@export_group("Boundaries")
@export var min_x: float = -2000.0
@export var max_x: float = 2000.0
@export var min_z: float = -2000.0
@export var max_z: float = 2000.0

# === Tilt-Shift ===
@export_group("Tilt-Shift")
## Disabled by default: the tilt-shift focus band rendered solid black on the
## Metal backend (a full-width black bar across the centre of the viewport).
## Re-enable once the screen-texture shader is verified on Metal.
@export var tilt_shift_enabled: bool = false
@export var tilt_shift_material: ShaderMaterial

# Internal state
var _target_position: Vector3 = Vector3.ZERO
var _target_altitude: float = 200.0
var _target_fov: float = 50.0
var _target_pitch: float = -50.0
var _yaw: float = 0.0  # Rotation around Y axis
var _velocity: Vector3 = Vector3.ZERO
var _is_orbiting: bool = false
var _orbit_start: Vector2 = Vector2.ZERO
var _zoom_interpolation: float = 0.0
var _is_underwater: bool = false
var _fly_tween: Tween = null

signal zoom_level_changed(level: ZoomLevel)
signal entered_underwater()
signal exited_underwater()


func _ready() -> void:
	# Auto-find tilt-shift material from TiltShiftCanvas sibling
	if not tilt_shift_material:
		var canvas := get_parent().get_node_or_null("TiltShiftCanvas")
		if canvas:
			var rect := canvas.get_node_or_null("TiltShiftRect") as ColorRect
			if rect and rect.material is ShaderMaterial:
				tilt_shift_material = rect.material as ShaderMaterial

	_apply_zoom_preset(current_zoom)
	_update_camera_transform()


func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventMouseButton:
		_handle_mouse_button(event)
	elif event is InputEventMouseMotion and _is_orbiting:
		_handle_orbit(event)


func _handle_mouse_button(event: InputEventMouseButton) -> void:
	match event.button_index:
		MOUSE_BUTTON_WHEEL_UP:
			if event.pressed:
				_zoom_in()
		MOUSE_BUTTON_WHEEL_DOWN:
			if event.pressed:
				_zoom_out()
		MOUSE_BUTTON_MIDDLE:
			_set_orbiting(event.pressed, event.position)
		MOUSE_BUTTON_RIGHT:
			_set_orbiting(event.pressed, event.position)


func _set_orbiting(pressed: bool, mouse_pos: Vector2) -> void:
	_is_orbiting = pressed
	if pressed:
		_orbit_start = mouse_pos
		Input.mouse_mode = Input.MOUSE_MODE_CAPTURED
	else:
		Input.mouse_mode = Input.MOUSE_MODE_VISIBLE


func _handle_orbit(event: InputEventMouseMotion) -> void:
	_yaw -= event.relative.x * orbit_speed
	var pitch_delta := event.relative.y * orbit_speed * 40.0
	_target_pitch = clampf(_target_pitch + pitch_delta, -80.0, -5.0)


func _zoom_in() -> void:
	var idx := int(current_zoom)
	if idx < ZoomLevel.UNDERWATER:
		set_zoom_level(idx + 1 as ZoomLevel)


func _zoom_out() -> void:
	var idx := int(current_zoom)
	if idx > ZoomLevel.COAST:
		set_zoom_level(idx - 1 as ZoomLevel)


func set_zoom_level(level: ZoomLevel) -> void:
	if level == current_zoom:
		return
	current_zoom = level
	_apply_zoom_preset(level)
	zoom_level_changed.emit(level)

	# Underwater transition
	var was_underwater := _is_underwater
	_is_underwater = (level == ZoomLevel.UNDERWATER)
	if _is_underwater and not was_underwater:
		entered_underwater.emit()
	elif not _is_underwater and was_underwater:
		exited_underwater.emit()


func _apply_zoom_preset(level: ZoomLevel) -> void:
	var preset: Dictionary = ZOOM_PRESETS[level]
	_target_altitude = preset["altitude"]
	_target_fov = preset["fov"]
	_target_pitch = preset["pitch"]

	if tilt_shift_material and tilt_shift_enabled:
		tilt_shift_material.set_shader_parameter("focus_center", preset["tilt_focus"])


func _process(delta: float) -> void:
	_process_pan_input(delta)
	_process_auto_orbit(delta)
	_interpolate_camera(delta)
	_update_camera_transform()
	_update_tilt_shift()


func _process_auto_orbit(delta: float) -> void:
	if auto_orbit_enabled and not _is_orbiting:
		_yaw += auto_orbit_speed * delta


func _process_pan_input(delta: float) -> void:
	var input_dir := Vector2.ZERO

	if Input.is_action_pressed("ui_left") or Input.is_key_pressed(KEY_A):
		input_dir.x -= 1.0
	if Input.is_action_pressed("ui_right") or Input.is_key_pressed(KEY_D):
		input_dir.x += 1.0
	if Input.is_action_pressed("ui_up") or Input.is_key_pressed(KEY_W):
		input_dir.y -= 1.0
	if Input.is_action_pressed("ui_down") or Input.is_key_pressed(KEY_S):
		input_dir.y += 1.0

	if input_dir.length_squared() > 0.0:
		input_dir = input_dir.normalized()

	# Scale pan speed by altitude (pan faster when zoomed out)
	var altitude_factor := clampf(_target_altitude / 200.0, 0.25, 5.0)

	# Rotate input by camera yaw
	var rotated := input_dir.rotated(-_yaw)
	var target_vel := Vector3(rotated.x, 0.0, rotated.y) * pan_speed * altitude_factor

	_velocity = _velocity.lerp(target_vel, pan_acceleration * delta)
	_target_position += _velocity * delta

	# Clamp to boundaries
	_target_position.x = clampf(_target_position.x, min_x, max_x)
	_target_position.z = clampf(_target_position.z, min_z, max_z)


func _interpolate_camera(delta: float) -> void:
	var speed := transition_speed * delta

	# Interpolate altitude
	var current_alt := global_position.y
	var new_alt := lerpf(current_alt, _target_altitude, speed)

	# Interpolate FOV
	fov = lerpf(fov, _target_fov, speed)

	# Interpolate position
	var target := Vector3(_target_position.x, new_alt, _target_position.z)
	global_position = global_position.lerp(target, speed)


func _update_camera_transform() -> void:
	# Apply rotation (yaw + pitch)
	var pitch_rad := deg_to_rad(_target_pitch)
	rotation = Vector3(pitch_rad, _yaw, 0.0)


func _update_tilt_shift() -> void:
	if not tilt_shift_material or not tilt_shift_enabled:
		return

	# Adjust blur based on zoom level (more blur at high altitude = more miniature)
	var blur := remap(global_position.y, 50.0, 1000.0, 1.0, 4.0)
	blur = clampf(blur, 0.5, 5.0)
	tilt_shift_material.set_shader_parameter("blur_amount", blur)

	# Disable tilt-shift underwater
	if _is_underwater:
		tilt_shift_material.set_shader_parameter("blur_amount", 0.0)


## Smoothly move camera to look at a world position
func look_at_position(world_pos: Vector3, zoom: ZoomLevel = current_zoom) -> void:
	_target_position = Vector3(world_pos.x, 0.0, world_pos.z)
	set_zoom_level(zoom)


## Eased fly-to variant (no instant snaps): tweens the focus target with an
## ease-out curve. The per-frame interpolation then carries the camera along
## smoothly. Zoom-level signals behave exactly like look_at_position.
func fly_to_position(world_pos: Vector3, zoom: ZoomLevel = current_zoom, duration: float = 1.2) -> void:
	set_zoom_level(zoom)
	var dest := Vector3(world_pos.x, 0.0, world_pos.z)
	if _fly_tween and _fly_tween.is_valid():
		_fly_tween.kill()
	_fly_tween = create_tween()
	_fly_tween.set_ease(Tween.EASE_OUT).set_trans(Tween.TRANS_CUBIC)
	_fly_tween.tween_property(self, "_target_position", dest, duration)


## Get the current focus point on the ground
func get_focus_point() -> Vector3:
	return _target_position
