class_name CameraShake
extends Node

## A short, trauma-based shake of the 3D camera (roadmap U2-06), for the rare
## events that deserve it (a critical event, the end of a game).
##
## Model (Squirrel Eiserloh, "Math for Game Programmers: Juicing Your Cameras
## With Math", GDC 2016): events add trauma (0..1), trauma decays linearly,
## the shake is trauma² (small hits barely move, big ones clearly do) and the
## offset follows smooth noise rather than random jumps.
##
## The shake moves only Camera3D.h_offset / v_offset, a sideways and upward
## shift of the view that leaves the camera's transform alone, so the camera
## rig (FjordCamera, owned by the graphics track) never sees it. The node
## adds its own offset on top of whatever the offsets hold and takes exactly
## that back, so it composes with anyone else using them. The size is a
## fraction of the view height at the camera's distance: the same shake on
## screen at the FARM and at the COAST zoom.
##
## Reduced motion (MotionPrefs) turns it off: shake() does nothing, and a
## shake already running stops at once.

const NODE_NAME := "CameraShake"
## Peak offset at full trauma, as a fraction of the view height (≈17 px at 1080p).
const MAX_SCREEN_FRACTION := 0.016
## Trauma lost per second (full trauma settles in about 0.7 s).
const DECAY_PER_SECOND := 1.5
## How fast the noise is walked (noise units per second; with the 0.5
## frequency below about eleven direction changes a second).
const NOISE_SPEED := 22.0
## Distance used when the camera cannot say how far it looks.
const FALLBACK_DISTANCE := 400.0

var trauma: float = 0.0
## Largest offset (metres) applied since the last shake began (QA and tests).
var peak_offset: float = 0.0

var _applied := Vector2.ZERO
var _time: float = 0.0
var _noise := FastNoiseLite.new()


## Shake the viewport's 3D camera by `amount` trauma (0..1). Returns false
## (and does nothing) under reduced motion or without a 3D camera.
static func shake(viewport: Viewport, amount: float) -> bool:
	if viewport == null or amount <= 0.0 or MotionPrefs.reduced_motion():
		return false
	var cam := viewport.get_camera_3d()
	if cam == null:
		return false
	var node := cam.get_node_or_null(NODE_NAME) as CameraShake
	if node == null:
		node = CameraShake.new()
		node.name = NODE_NAME
		cam.add_child(node)
	node.add_trauma(amount)
	return true


func _ready() -> void:
	# Keep settling while the tree is paused (the pause menu), so the view
	# never stays shifted.
	process_mode = Node.PROCESS_MODE_ALWAYS
	_noise.noise_type = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
	_noise.seed = 1729
	_noise.frequency = 0.5
	set_process(trauma > 0.0)


func add_trauma(amount: float) -> void:
	if trauma <= 0.0:
		peak_offset = 0.0
	trauma = clampf(trauma + amount, 0.0, 1.0)
	set_process(true)


func _process(delta: float) -> void:
	var cam := get_parent() as Camera3D
	if cam == null:
		return
	if MotionPrefs.reduced_motion():
		trauma = 0.0
	else:
		trauma = maxf(trauma - DECAY_PER_SECOND * delta, 0.0)
	_time += delta
	var amplitude := _view_height(cam) * MAX_SCREEN_FRACTION * trauma * trauma
	var offset := Vector2(
		_noise.get_noise_2d(_time * NOISE_SPEED, 0.0),
		_noise.get_noise_2d(0.0, 100.0 + _time * NOISE_SPEED)) * amplitude
	cam.h_offset += offset.x - _applied.x
	cam.v_offset += offset.y - _applied.y
	_applied = offset
	peak_offset = maxf(peak_offset, offset.length())
	if trauma <= 0.0:
		set_process(false)


## Height of the view (metres) at the distance the camera looks at.
func _view_height(cam: Camera3D) -> float:
	var distance := FALLBACK_DISTANCE
	if cam.has_method("get_current_distance"):
		distance = maxf(float(cam.call("get_current_distance")), 1.0)
	return 2.0 * distance * tan(deg_to_rad(cam.fov * 0.5))


func _exit_tree() -> void:
	var cam := get_parent() as Camera3D
	if cam != null:
		cam.h_offset -= _applied.x
		cam.v_offset -= _applied.y
	_applied = Vector2.ZERO
