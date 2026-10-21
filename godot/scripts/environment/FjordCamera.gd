@tool
class_name FjordCamera
extends Camera3D
## Fjord camera rig: the camera orbits a focus point on the water.
##
## - Continuous zoom from 25 to 3000 m (x1.15 per wheel notch, toward the
##   cursor). Pitch and field of view follow the distance (VIEW_TABLE), so
##   the horizon sits 10-25 % from the top of the frame at the FJORD and
##   COAST defaults and the farm is seen from above at FARM.
## - Frame-rate-independent damping on every axis:
##   x += (target - x) * (1 - exp(-k * dt)).
## - Terrain avoidance lifts the camera until it clears the ground below it
##   and the sight line to the focus; it never jumps over the focus.
## - Input acts only while the world is on screen (input_enabled, set by the
##   presenter) and never steals keys from a focused Control: arrows belong
##   to any focused Control, letters to text fields.
## - Controls: WASD/arrows pan, middle-drag pans, right-drag orbits, wheel
##   zooms, Q/E rotate, Home recentres, T toggles top-down, double-click
##   focuses, G (the camera_bookmark action, rebindable) cycles the bookmarks
##   Fjord -> Land -> Hav (F2-21). Idle auto-orbit starts after 30 s without
##   input.
## - Focus targets (F2-20/F2-21, farm-types spec §4): places framed at their
##   own distance, pitch and (optionally) yaw; both clamps take the union of
##   the world's box and the targets' discs, so a unit or a plot beyond the
##   world's edge can be framed and orbited.

enum ZoomLevel { COAST, FJORD, FARM, UNDERWATER }

@export var current_zoom: ZoomLevel = ZoomLevel.FJORD

## Distance from the focus for each named zoom level (metres).
const ZOOM_DISTANCE := {
	ZoomLevel.COAST: 2400.0,
	ZoomLevel.FJORD: 900.0,
	ZoomLevel.FARM: 140.0,
	ZoomLevel.UNDERWATER: 40.0,
}
## [distance m, pitch deg, vertical fov deg], interpolated in log distance.
## FJORD: -17 deg at 45 deg fov puts the horizon 13 % from the top; COAST:
## -20 deg at 52 deg fov, 13 %. FARM looks down on the pen.
const VIEW_TABLE := [
	[25.0, -36.0, 55.0],
	[140.0, -32.0, 50.0],
	[400.0, -24.0, 47.0],
	[900.0, -17.0, 45.0],
	[2400.0, -20.0, 52.0],
	[3000.0, -21.0, 54.0],
]
const MIN_DISTANCE := 25.0
const MAX_DISTANCE := 3000.0
## Zoom bands for current_zoom: COAST at or beyond, FARM below.
const COAST_BAND := 1600.0
const FARM_BAND := 400.0
const ZOOM_STEP := 1.15
## Damping rates (1/s): yaw, pitch and the rig position; focus and distance
## (slower, so one wheel notch moves the camera < 2 % of the distance per
## frame at 60 fps).
const DAMPING := 12.0
const ZOOM_DAMPING := 5.0
const SNAP_EPSILON := 0.0005
## Terrain avoidance: clearance over the ground under the camera, and over
## the terrain along the sight line, sampled SIGHT_SAMPLES times. Six samples
## (the roadmap's first sketch) let ridges between them hide the focus in 9 %
## of COAST orbits; 32 samples with 15 m clearance hid it in 0.5 % while the
## camera stayed in the world. With the far field (G2-01) a COAST camera
## stands out over the plateau beyond it and its longer, flatter sight line
## needs 48 (32 hid the focus in 1.5 %).
const GROUND_CLEARANCE := 32.0
const SIGHT_CLEARANCE := 15.0
const SIGHT_SAMPLES := 48
## F2-21: the sight line's clearance tapers to nothing over this last stretch
## (m) to the focus, so a focus on land can be framed (_required_height).
const FOCUS_GROUND_M := 120.0
## Steepest view the avoidance may force before it pulls the camera closer.
const MAX_ELEVATION_DEG := 78.0
const MIN_PITCH_DEG := -85.0
const MAX_PITCH_DEG := -5.0
const TOP_DOWN_PITCH_DEG := -89.0
## Soft limit for the focus: stay this far inside the 4 km world's edge.
const EDGE_MARGIN := 300.0
## G2-01: with a far field round the world the camera itself may leave the
## world square as far as its orbit takes it (focus limit + MAX_DISTANCE),
## and never closer than this to the far field's outer edge.
const FAR_FIELD_MARGIN := 2000.0
const IDLE_ORBIT_DELAY := 30.0
## G3-04 UNDERWATER: the camera hangs this deep (m; the roadmap's 3-8 m),
## clear of the seabed by UNDERWATER_SEABED_CLEARANCE.
const UNDERWATER_DEPTH := 5.0
const UNDERWATER_MIN_DEPTH := 3.0
const UNDERWATER_MAX_DEPTH := 8.0
const UNDERWATER_SEABED_CLEARANCE := 4.0
## Inside a pen (underwater_anchor): the camera stands this share of the
## net's radius out from the pen's centre (18 m of 24.95: in the school's
## band, which circles out to FishSchool.SCHOOL_OUTER_M, 22.5 m) and looks
## across the pen, turned UNDERWATER_TURN_DEG off its centre (the school
## passes through one side of the view, the net wall and its ropes show at
## the other) and tilted UNDERWATER_PITCH_DEG up (the surface's bright window
## at the top). Q/E, right-drag and the pan keys turn it round the pen and
## tilt it.
const UNDERWATER_RING := 0.72
const UNDERWATER_TURN_DEG := 45.0
const UNDERWATER_PITCH_DEG := 16.0
const UNDERWATER_PITCH_LIMIT_DEG := 60.0
const UNDERWATER_FOV_DEG := 64.0
## The dive: the camera crosses the surface inside the collar. It first flies
## to DIVE_HOVER_M over its place in the pen (within DIVE_ENTRY_M of it),
## then goes straight down; leaving, it rises straight up through the
## surface before it flies out. Looking down on the way in.
const DIVE_HOVER_M := 7.0
const DIVE_ENTRY_M := 1.5
const DIVE_LOOK_DOWN_DEG := -55.0
## How fast the view turns while it dives or surfaces (1/s): the orbit's
## look-at and the pen's view differ there, and the turn is eased.
const DIVE_TURN_RATE := 6.0
## Underwater, the up and down pan keys tilt the view this fast (deg/s).
const UNDERWATER_TILT_SPEED := 30.0

@export_group("Movement")
## Pan speed as a fraction of the zoom distance per second.
@export var pan_speed: float = 0.8
## Q/E rotation speed (rad/s) and right-drag orbit sensitivity (rad/px).
@export var rotate_speed: float = 0.6
@export var orbit_speed: float = 0.005

@export_group("AutoOrbit")
## Slow orbit once the player has left the camera alone for IDLE_ORBIT_DELAY.
@export var auto_orbit_enabled: bool = false
@export var auto_orbit_speed: float = 0.03

## Terrain for avoidance, the fjord axis and cursor picking.
@export var terrain: FjordTerrain

## Camera input acts only while this is true (the presenter clears it while
## an opaque panel covers the world). Turning it off ends a drag under way:
## the button's release goes to whatever takes the input then (wave4b int
## review: a right-drag held into a first sight kept orbiting on every bare
## mouse move after it).
var input_enabled: bool = true:
	set(value):
		input_enabled = value
		if not value:
			_orbiting = false
			_panning = false
## Where Home recentres (the presenter sets it to the first pen).
var home_focus: Vector3 = Vector3.ZERO
## G3-04: the pen the UNDERWATER view dives into. The presenter sets it to a
## Callable(point: Vector3) -> Dictionary: {"center": the pen's centre on the
## water, "radius": its net's radius (m)} for the pen nearest a world point,
## or {} when no pen is near (the view then stays in open water).
var underwater_anchor: Callable = Callable()

var _focus_target := Vector3.ZERO
var _focus := Vector3.ZERO
var _distance_target: float = ZOOM_DISTANCE[ZoomLevel.FJORD]
var _distance: float = ZOOM_DISTANCE[ZoomLevel.FJORD]
var _yaw_target: float = 0.0
var _yaw: float = 0.0
var _pitch_offset_target: float = 0.0
var _pitch_offset: float = 0.0
var _top_down := false
var _top_down_blend: float = 0.0
var _underwater := false
## The pen the underwater view stands in ({} in open water), looked up when
## the focus moves (not every frame).
var _anchor := {}
var _anchor_key := Vector3.INF
## True while the view eases between the orbit and the underwater view (it
## turns smoothly there instead of snapping to a look-at).
var _turning := false
var _rig_position := Vector3.ZERO
var _user_rotated := false
var _idle_time: float = 0.0
var _orbiting := false
var _panning := false
var _fly_tween: Tween = null
var _settled := false
## F2-20 (farm-types spec §4; F2-21 adds the bookmarks): places the focus may
## stand beyond the world's focus clamp, id -> {pos, distance, pitch, radius, yaw}.
var _focus_targets: Dictionary = {}
## F2-21 bookmarks: slot ("land", "hav") -> the focus target it flies to (the
## presenter names them); "fjord" is the home view. G steps through
## BOOKMARK_SLOTS from the one shown last.
var _bookmarks: Dictionary = {}
var _bookmark_at := 0

## F2-21 (spec §4 "Bookmarks"): the input action that cycles the bookmarks
## (project.godot, G by default; rebindable, D15), and the cycle's order.
const BOOKMARK_ACTION := "camera_bookmark"
const BOOKMARK_SLOTS := ["fjord", "land", "hav"]

signal zoom_level_changed(level: ZoomLevel)
## F2-21: G took the camera to a bookmark ("fjord", "land" or "hav"; the
## target id, "" for the home view).
signal bookmark_changed(slot: String, target_id: String)
signal entered_underwater()
signal exited_underwater()


func _ready() -> void:
	_distance_target = ZOOM_DISTANCE[current_zoom] if current_zoom != ZoomLevel.UNDERWATER else ZOOM_DISTANCE[ZoomLevel.FARM]
	_underwater = current_zoom == ZoomLevel.UNDERWATER
	_yaw_target = home_yaw(_focus_target)
	_snap()


# ── Public API ────────────────────────────────────────────────────────────────

func set_zoom_level(level: ZoomLevel) -> void:
	var was_underwater := _underwater
	_underwater = level == ZoomLevel.UNDERWATER
	_distance_target = ZOOM_DISTANCE[level]
	_update_zoom_band(level)
	if _underwater != was_underwater:
		# The view turns between the orbit's look-at and the pen's view.
		_turning = true
		_pitch_offset_target = 0.0
	if _underwater and not was_underwater:
		entered_underwater.emit()
	elif was_underwater and not _underwater:
		exited_underwater.emit()


## True in the UNDERWATER view (the camera may still be on its way down).
func is_underwater_view() -> bool:
	return _underwater


## The pen the underwater view stands in for a focus point ({} in open
## water): {"center", "radius"} from underwater_anchor.
func underwater_anchor_for(focus: Vector3) -> Dictionary:
	return _anchor_for(focus)


## The underwater view's direction for a focus, a yaw (radians) and a tilt
## (degrees, as the pitch offset): across the pen from the camera's place in
## it, turned UNDERWATER_TURN_DEG off the pen's centre, or at the focus in
## open water; UNDERWATER_PITCH_DEG up plus the tilt.
func underwater_forward(focus: Vector3, yaw: float, tilt_deg: float) -> Vector3:
	var across := Vector3(-sin(yaw), 0.0, -cos(yaw))
	if not _anchor_for(focus).is_empty():
		across = across.rotated(Vector3.UP, deg_to_rad(UNDERWATER_TURN_DEG))
	var pitch := deg_to_rad(clampf(UNDERWATER_PITCH_DEG + tilt_deg, -UNDERWATER_PITCH_LIMIT_DEG, UNDERWATER_PITCH_LIMIT_DEG))
	return (across * cos(pitch) + Vector3.UP * sin(pitch)).normalized()


## Focus a world position at a zoom level (eased by the rig's damping).
func look_at_position(world_pos: Vector3, zoom: ZoomLevel = current_zoom) -> void:
	_cancel_fly()
	_set_focus_target(world_pos)
	if not _user_rotated:
		_yaw_target = _nearest_angle(_yaw, home_yaw(_focus_target))
	set_zoom_level(zoom)


## Eased fly-to: tweens the focus over `duration` seconds; the damping keeps
## every other axis smooth.
func fly_to_position(world_pos: Vector3, zoom: ZoomLevel = current_zoom, duration: float = 1.2) -> void:
	set_zoom_level(zoom)
	_cancel_fly()
	var dest := _clamp_to_world(Vector3(world_pos.x, 0.0, world_pos.z))
	if not _user_rotated:
		_yaw_target = _nearest_angle(_yaw, home_yaw(dest))
	if not is_inside_tree() or duration <= 0.0:
		_focus_target = dest
		return
	_fly_tween = create_tween()
	_fly_tween.set_ease(Tween.EASE_OUT).set_trans(Tween.TRANS_CUBIC)
	_fly_tween.tween_property(self, "_focus_target", dest, duration)


## F2-20 (spec §4 "Camera"): register a place the camera frames, such as a
## land campus or an offshore unit, at its own distance and pitch (degrees,
## negative looks down) and, F2-21, its own yaw (radians, the rig's heading
## as home_yaw() gives it; NAN: the fjord-axis heading). The focus clamp takes
## the union of the world's box and every target's disc of `radius` metres,
## so a unit beyond the world's edge can be focused and panned round.
## Re-registering an id moves it.
func register_focus_target(id: String, pos: Vector3, framing_distance: float, pitch_deg: float, radius: float,
		yaw: float = NAN) -> void:
	_focus_targets[id] = {"pos": pos, "distance": framing_distance, "pitch": pitch_deg, "radius": maxf(radius, 0.0),
		"yaw": yaw}


func unregister_focus_target(id: String) -> void:
	_focus_targets.erase(id)


## F2-21: the bookmark `slot` ("land" or "hav") flies to focus target
## `target_id` ("" clears it; a cleared or unregistered slot is skipped).
func set_bookmark(slot: String, target_id: String) -> void:
	if target_id == "":
		_bookmarks.erase(slot)
	else:
		_bookmarks[slot] = target_id


## The focus target a bookmark slot flies to ("" when none).
func get_bookmark(slot: String) -> String:
	return str(_bookmarks.get(slot, ""))


## The bookmark shown last ("fjord" after Home or a new game).
func current_bookmark() -> String:
	return BOOKMARK_SLOTS[_bookmark_at]


## F2-21 (spec §4): the next bookmark after the one shown last, Fjord ->
## Land -> Hav -> Fjord, skipping a slot with no registered target; flies
## there and returns the slot.
func cycle_bookmark() -> String:
	for step in range(1, BOOKMARK_SLOTS.size() + 1):
		var at := (_bookmark_at + step) % BOOKMARK_SLOTS.size()
		var slot: String = BOOKMARK_SLOTS[at]
		if slot == "fjord":
			# The home view: down the fjord to the mouth at the FJORD default
			# (from a unit's close framing, not Home's kept zoom band).
			set_zoom_level(ZoomLevel.FJORD)
			recentre()
			_bookmark_at = at
			bookmark_changed.emit(slot, "")
			return slot
		var id := get_bookmark(slot)
		if id != "" and focus_target(id):
			_bookmark_at = at
			bookmark_changed.emit(slot, id)
			return slot
	return current_bookmark()


func has_focus_target(id: String) -> bool:
	return _focus_targets.has(id)


## The registered target {pos, distance, pitch, radius}, or {}.
func get_focus_target(id: String) -> Dictionary:
	return _focus_targets.get(id, {})


## Fly to a registered target at its framing (its own yaw when it has one).
## False for an unknown id.
func focus_target(id: String, duration: float = 1.2) -> bool:
	if not _focus_targets.has(id):
		return false
	var t: Dictionary = _focus_targets[id]
	frame_position(t["pos"], float(t["distance"]), float(t["pitch"]), duration, float(t.get("yaw", NAN)))
	return true


## Fly the focus to `world_pos` at `distance` metres and `pitch_deg` (below the
## horizon, e.g. -40): the pitch becomes an offset from the distance table's,
## as a right-drag tilt would, so Home restores the table. A finite `yaw`
## (radians) heads the rig there, as a turn with Q/E would; NAN keeps the
## fjord-axis heading unless the player has turned the view.
func frame_position(world_pos: Vector3, distance: float, pitch_deg: float, duration: float = 1.2, yaw: float = NAN) -> void:
	_cancel_fly()
	_top_down = false
	_distance_target = clampf(distance, MIN_DISTANCE, MAX_DISTANCE)
	_pitch_offset_target = clampf(pitch_deg - view_for_distance(_distance_target).x, -40.0, 15.0)
	_update_zoom_band(_band_for(_distance_target))
	var dest := _clamp_to_world(Vector3(world_pos.x, 0.0, world_pos.z))
	if is_finite(yaw):
		_user_rotated = true
		_yaw_target = _nearest_angle(_yaw, yaw)
	elif not _user_rotated:
		_yaw_target = _nearest_angle(_yaw, home_yaw(dest))
	if not is_inside_tree() or duration <= 0.0:
		_focus_target = dest
		return
	_fly_tween = create_tween()
	_fly_tween.set_ease(Tween.EASE_OUT).set_trans(Tween.TRANS_CUBIC)
	_fly_tween.tween_property(self, "_focus_target", dest, duration)


## Back to the home view: home focus, fjord-axis yaw, table pitch, no top-down.
func recentre() -> void:
	_user_rotated = false
	_top_down = false
	_pitch_offset_target = 0.0
	_bookmark_at = 0
	if _underwater:
		set_zoom_level(ZoomLevel.FJORD)
	fly_to_position(home_focus, current_zoom, 0.8)


## Cinematic hold (the main menu's backdrop, photo mode): the rig heads for
## this focus, yaw (radians), distance (metres, unclamped by the zoom range)
## and pitch offset (degrees) and keeps them until another call or player
## input moves it. Damping and terrain avoidance still apply, so a moving
## target glides and never cuts through a mountainside; `snap` jumps there.
func hold_view(focus: Vector3, yaw: float, distance: float, pitch_offset_deg: float = 0.0, snap: bool = false) -> void:
	_cancel_fly()
	_set_focus_target(focus)
	_user_rotated = true
	_yaw_target = _nearest_angle(_yaw, yaw)
	_distance_target = maxf(distance, 1.0)
	_pitch_offset_target = clampf(pitch_offset_deg, -40.0, 15.0)
	_update_zoom_band(_band_for(_distance_target))
	if snap:
		_snap()


## F2-23: a cinematic (the first sight) places the camera itself while the
## rig stands still (its process off): the pose and the field of view, and
## the distance the environment reads for its depth of field and shadows.
func cinematic_pose(eye: Vector3, look: Vector3, fov_deg: float) -> void:
	_rig_position = eye
	_distance = maxf(eye.distance_to(look), MIN_DISTANCE)
	fov = fov_deg
	global_position = eye
	if Vector2(look.x - eye.x, look.z - eye.z).length() > 0.01:
		look_at(look, Vector3.UP)


## Tilt the view relative to the distance table (degrees, positive looks
## up), as a right-drag does. Photo mode and the aurora shot use it.
## Underwater it tilts the view from UNDERWATER_PITCH_DEG, within
## +-UNDERWATER_PITCH_LIMIT_DEG of the horizontal.
func set_pitch_offset(degrees: float) -> void:
	if _underwater:
		_pitch_offset_target = clampf(degrees, -UNDERWATER_PITCH_LIMIT_DEG - UNDERWATER_PITCH_DEG,
			UNDERWATER_PITCH_LIMIT_DEG - UNDERWATER_PITCH_DEG)
		return
	_pitch_offset_target = clampf(degrees, -40.0, 15.0)


## Turn the view by `radians` from where it is heading, as Q/E do.
func rotate_view(radians: float) -> void:
	_user_rotated = true
	_yaw_target += radians


func set_top_down(enabled: bool) -> void:
	_top_down = enabled


func is_top_down() -> bool:
	return _top_down


## The focus point on the water.
func get_focus_point() -> Vector3:
	return _focus_target


## Distance from the focus the rig is heading for (metres).
func get_zoom_distance() -> float:
	return _distance_target


## Current (damped) distance from the focus.
func get_current_distance() -> float:
	return _distance


## Yaw that looks along the fjord toward its mouth (-Z) from behind `focus`.
func home_yaw(focus: Vector3) -> float:
	var ahead := Vector3(0.0, 0.0, -1.0)
	if terrain != null and terrain.has_method("channel_center_x"):
		var x_ahead := terrain.channel_center_x(focus.z - 150.0)
		var x_behind := terrain.channel_center_x(focus.z + 150.0)
		ahead = Vector3(x_ahead - x_behind, 0.0, -300.0).normalized()
	# The camera sits behind the focus, opposite the view direction.
	return atan2(-ahead.x, -ahead.z)


## Pitch (deg) and fov (deg) the table gives at a distance.
static func view_for_distance(distance: float) -> Vector2:
	var d := clampf(distance, VIEW_TABLE[0][0], VIEW_TABLE[VIEW_TABLE.size() - 1][0])
	for i in range(VIEW_TABLE.size() - 1):
		var a: Array = VIEW_TABLE[i]
		var b: Array = VIEW_TABLE[i + 1]
		if d <= b[0]:
			var t := (log(d) - log(a[0])) / (log(b[0]) - log(a[0]))
			return Vector2(lerpf(a[1], b[1], t), lerpf(a[2], b[2], t))
	var last: Array = VIEW_TABLE[VIEW_TABLE.size() - 1]
	return Vector2(last[1], last[2])


## Steady-state camera position for a focus, yaw, distance and pitch (deg):
## the orbit position, lifted clear of the terrain and of the sight line,
## and pulled in if the lift alone would look straight down. Pure: the
## avoidance tests drive it directly.
func solve_rig(focus: Vector3, yaw: float, distance: float, pitch_deg: float) -> Vector3:
	var water_y := _water_level()
	if _underwater:
		return _solve_underwater(focus, yaw, distance)
	var pitch := deg_to_rad(pitch_deg)
	var horiz := distance * cos(pitch)
	var rise := distance * sin(-pitch)
	var dir := Vector3(sin(yaw), 0.0, cos(yaw))
	var max_tan := tan(deg_to_rad(MAX_ELEVATION_DEG))
	var pos := _clamp_camera(focus + dir * horiz + Vector3(0.0, rise, 0.0))
	horiz = Vector2(pos.x - focus.x, pos.z - focus.z).length()
	var need := _required_height(focus, pos, water_y)
	if need <= pos.y:
		return pos
	if horiz < 1.0 or (need - focus.y) / horiz <= max_tan:
		pos.y = need
		return pos
	# Lifting alone would look straight down: come in along the same bearing
	# to the farthest distance whose lift stays under MAX_ELEVATION_DEG
	# (bisection, so the answer moves smoothly with yaw and zoom).
	var lo := 0.0
	var hi := horiz
	for i in range(14):
		var mid := (lo + hi) * 0.5
		var probe := _clamp_camera(focus + dir * mid + Vector3(0.0, rise * mid / horiz, 0.0))
		var probe_need := maxf(_required_height(focus, probe, water_y), probe.y)
		if (probe_need - focus.y) / maxf(mid, 1.0) <= max_tan:
			lo = mid
		else:
			hi = mid
	pos = _clamp_camera(focus + dir * lo + Vector3(0.0, rise * lo / horiz, 0.0))
	pos.y = maxf(_required_height(focus, pos, water_y), pos.y)
	return pos


# ── Frame update ──────────────────────────────────────────────────────────────

func _process(delta: float) -> void:
	if not Engine.is_editor_hint():
		_process_keys(delta)
		_process_idle_orbit(delta)
	_damp(delta)
	_apply_transform(delta)


func _damp(delta: float) -> void:
	var k := 1.0 - exp(-DAMPING * delta)
	var kz := 1.0 - exp(-ZOOM_DAMPING * delta)
	_focus = _approach_v(_focus, _focus_target, kz)
	_distance = _approach(_distance, _distance_target, kz)
	_yaw = _approach(_yaw, _yaw_target, k)
	_pitch_offset = _approach(_pitch_offset, _pitch_offset_target, k)
	_top_down_blend = _approach(_top_down_blend, 1.0 if _top_down else 0.0, k)
	var view := view_for_distance(_distance)
	var pitch := clampf(view.x + _pitch_offset, MIN_PITCH_DEG, MAX_PITCH_DEG)
	pitch = lerpf(pitch, TOP_DOWN_PITCH_DEG, _top_down_blend)
	var target := _dive_path(solve_rig(_focus, _yaw, _distance, pitch))
	_rig_position = _approach_v(_rig_position, target, k)
	var fov_target := UNDERWATER_FOV_DEG if _underwater else view.y
	fov = _approach(fov, fov_target, k)


## The way into and out of the water (G3-04): the camera crosses the surface
## inside the pen's collar, never through its side. Diving, it flies to
## DIVE_HOVER_M over its place in the pen first, then goes straight down;
## leaving, it rises straight up out of the water before it flies off.
func _dive_path(target: Vector3) -> Vector3:
	var water_y := _water_level()
	if _underwater:
		if _rig_position.y > water_y - 0.2 and not _anchor.is_empty() \
				and Vector2(target.x - _rig_position.x, target.z - _rig_position.z).length() > DIVE_ENTRY_M:
			return Vector3(target.x, water_y + DIVE_HOVER_M, target.z)
		return target
	if _rig_position.y < water_y + 0.5:
		return Vector3(_rig_position.x, water_y + 2.0, _rig_position.z)
	return target


func _apply_transform(delta: float = 0.0) -> void:
	var moved := (_rig_position - global_position).length_squared() > 0.0000001
	# Underwater the tilt turns the view without moving the camera.
	if not moved and _settled and not _turning and not _underwater:
		return
	global_position = _rig_position
	var water_y := _water_level()
	var wanted := Basis.IDENTITY
	var has_wanted := false
	if _underwater or _rig_position.y < water_y + 0.5:
		# The underwater view (and on the way out, until the camera is in the
		# air): across the pen; looking down at the water while it dives.
		var tilt := _pitch_offset
		var above := clampf((_rig_position.y - water_y) / DIVE_HOVER_M, 0.0, 1.0)
		if above > 0.0:
			tilt = lerpf(tilt, DIVE_LOOK_DOWN_DEG - UNDERWATER_PITCH_DEG, smoothstep(0.0, 1.0, above))
		wanted = Basis.looking_at(underwater_forward(_focus, _yaw, tilt), Vector3.UP)
		has_wanted = true
	elif Vector2(_focus.x - _rig_position.x, _focus.z - _rig_position.z).length() > 0.01:
		wanted = Basis.looking_at(_focus - _rig_position, Vector3.UP)
		has_wanted = true
	if has_wanted:
		if _turning and _settled and delta > 0.0:
			var current := global_transform.basis.orthonormalized()
			var eased := current.slerp(wanted, 1.0 - exp(-DIVE_TURN_RATE * delta))
			global_transform = Transform3D(eased, _rig_position)
			# Done once the view has turned and the camera is out of the dive.
			if rad_to_deg(eased.z.angle_to(wanted.z)) < 0.2 and (_underwater == (_rig_position.y < water_y)):
				_turning = false
		else:
			global_transform = Transform3D(wanted, _rig_position)
			_turning = _turning and not _settled
	_settled = true


func _snap() -> void:
	_focus = _focus_target
	_distance = _distance_target
	_yaw = _yaw_target
	_pitch_offset = _pitch_offset_target
	_top_down_blend = 1.0 if _top_down else 0.0
	var view := view_for_distance(_distance)
	fov = UNDERWATER_FOV_DEG if _underwater else view.y
	# The pitch _damp settles on (the table's, the offset, top-down), so a
	# snapped rig does not move on its next frame (F2-23: a first-sight
	# sequence hands the camera back on a framing's pitch).
	var pitch := clampf(view.x + _pitch_offset, MIN_PITCH_DEG, MAX_PITCH_DEG)
	_rig_position = solve_rig(_focus, _yaw, _distance, lerpf(pitch, TOP_DOWN_PITCH_DEG, _top_down_blend))
	_settled = false
	_turning = false
	_apply_transform()


func _approach(current: float, target: float, k: float) -> float:
	if absf(target - current) < SNAP_EPSILON:
		return target
	return current + (target - current) * k


func _approach_v(current: Vector3, target: Vector3, k: float) -> Vector3:
	if current.distance_to(target) < SNAP_EPSILON:
		return target
	return current + (target - current) * k


# ── Terrain avoidance ─────────────────────────────────────────────────────────

## Lowest camera height that clears the ground under it and the terrain along
## the sight line to the focus. Over the last FOCUS_GROUND_M to the focus the
## clearance tapers to nothing: a focus on land (a campus on its pad, F2-21)
## stands on the ground, and asking the line to pass 15 m over the pad a few
## metres from it lifted a -40 deg land framing to -64 to -71 deg.
func _required_height(focus: Vector3, pos: Vector3, water_y: float) -> float:
	var need := maxf(_ground(pos.x, pos.z), water_y) + GROUND_CLEARANCE
	var reach := Vector2(focus.x - pos.x, focus.z - pos.z).length()
	for i in range(1, SIGHT_SAMPLES + 1):
		var t := float(i) / float(SIGHT_SAMPLES + 1)
		var p := pos.lerp(focus, t)
		var ground := _ground(p.x, p.z)
		if ground <= water_y:
			continue  # water never hides the focus
		var clearance := SIGHT_CLEARANCE * clampf((1.0 - t) * reach / FOCUS_GROUND_M, 0.0, 1.0)
		# The sight line at t is pos.y*(1-t) + focus.y*t: solve for pos.y.
		need = maxf(need, (ground + clearance - focus.y * t) / (1.0 - t))
	return need


## Underwater (G3-04): inside the pen nearest the focus (underwater_anchor),
## UNDERWATER_RING of the net's radius out from its centre on the yaw's
## side, UNDERWATER_DEPTH down; in open water `distance` from the focus.
## Clear of the seabed in shallow water.
func _solve_underwater(focus: Vector3, yaw: float, distance: float) -> Vector3:
	var water_y := _water_level()
	var dir := Vector3(sin(yaw), 0.0, cos(yaw))
	var anchor := _anchor_for(focus)
	var pos: Vector3
	if anchor.is_empty():
		pos = focus + dir * distance
	else:
		var centre: Vector3 = anchor["center"]
		pos = Vector3(centre.x, 0.0, centre.z) + dir * float(anchor["radius"]) * UNDERWATER_RING
	pos.y = water_y - UNDERWATER_DEPTH
	var seabed := _ground(pos.x, pos.z)
	pos.y = maxf(pos.y, minf(seabed + UNDERWATER_SEABED_CLEARANCE, water_y - 1.0))
	return pos


## The pen underwater_anchor gives for a focus point, cached per point.
func _anchor_for(focus: Vector3) -> Dictionary:
	if not underwater_anchor.is_valid():
		return {}
	if focus != _anchor_key:
		_anchor_key = focus
		var found: Variant = underwater_anchor.call(focus)
		_anchor = found if found is Dictionary else {}
	return _anchor


func _ground(x: float, z: float) -> float:
	if terrain != null and terrain.has_method("get_height_at"):
		return terrain.get_height_at(x, z)
	return -1000.0


func _water_level() -> float:
	if terrain != null:
		return terrain.water_level
	return 0.0


# ── World bounds ──────────────────────────────────────────────────────────────

func _world_half() -> Vector2:
	if terrain != null:
		return terrain.terrain_size * 0.5 - Vector2.ONE * EDGE_MARGIN
	return Vector2(2000.0, 2000.0) - Vector2.ONE * EDGE_MARGIN


## Clamp per axis: as the camera orbits, the clamped point slides along the
## edge no faster than the orbit itself. With farm-type focus targets
## registered (F2-20, spec §4) the focus may also stand in any target's disc:
## the clamp takes the union, so a point outside every shape goes to the
## nearest of the box and the discs.
func _clamp_to_world(p: Vector3) -> Vector3:
	var half := _world_half()
	var boxed := Vector3(clampf(p.x, -half.x, half.x), p.y, clampf(p.z, -half.y, half.y))
	if _focus_targets.is_empty() or boxed.is_equal_approx(p):
		return boxed
	var best := boxed
	var best_d := Vector2(boxed.x - p.x, boxed.z - p.z).length_squared()
	for target: Dictionary in _focus_targets.values():
		var c: Vector3 = target["pos"]
		var r: float = target["radius"]
		var off := Vector2(p.x - c.x, p.z - c.z)
		if off.length() <= r:
			return p
		var edge := Vector2(c.x, c.z) + off.normalized() * r
		var d := Vector2(edge.x - p.x, edge.y - p.z).length_squared()
		if d < best_d:
			best_d = d
			best = Vector3(edge.x, p.y, edge.y)
	return best


## Where the camera itself may go (G2-01): the focus limit plus the longest
## orbit once a far field surrounds the world (inside the far field's edge
## by FAR_FIELD_MARGIN), else the focus limit.
func _camera_half() -> Vector2:
	var half := _world_half()
	var far := terrain.far_field_half_extent() if terrain != null else 0.0
	if far <= 0.0:
		return half
	var reach := minf(MAX_DISTANCE, far - FAR_FIELD_MARGIN - maxf(half.x, half.y))
	return half + Vector2.ONE * maxf(reach, 0.0)


func _clamp_camera(p: Vector3) -> Vector3:
	var half := _camera_half()
	var boxed := Vector3(clampf(p.x, -half.x, half.x), p.y, clampf(p.z, -half.y, half.y))
	if boxed.is_equal_approx(p):
		return boxed
	# F2-20 (spec §4): the camera may also stand anywhere a registered focus
	# target is framed from: within its disc grown by its framing distance.
	for target: Dictionary in _focus_targets.values():
		var c: Vector3 = target["pos"]
		var reach: float = float(target["radius"]) + float(target["distance"])
		if Vector2(p.x - c.x, p.z - c.z).length() <= reach:
			return p
	return boxed


# ── Input ─────────────────────────────────────────────────────────────────────

func _input(_event: InputEvent) -> void:
	_idle_time = 0.0


func _unhandled_input(event: InputEvent) -> void:
	if Engine.is_editor_hint() or not input_enabled:
		return
	if event is InputEventMouseButton:
		_handle_mouse_button(event)
	elif event is InputEventMouseMotion:
		if _orbiting:
			_handle_orbit(event)
		elif _panning:
			_handle_drag_pan(event)
	elif event is InputEventKey and event.pressed and not event.echo:
		_handle_key(event)


func _handle_mouse_button(event: InputEventMouseButton) -> void:
	match event.button_index:
		MOUSE_BUTTON_WHEEL_UP:
			if event.pressed:
				_zoom_by(1.0 / ZOOM_STEP, event.position)
		MOUSE_BUTTON_WHEEL_DOWN:
			if event.pressed:
				_zoom_by(ZOOM_STEP, event.position)
		MOUSE_BUTTON_RIGHT:
			_orbiting = event.pressed
		MOUSE_BUTTON_MIDDLE:
			_panning = event.pressed
		MOUSE_BUTTON_LEFT:
			if event.pressed and event.double_click and not _underwater:
				var hit := pick_ground(event.position)
				if hit.is_finite():
					fly_to_position(hit, current_zoom, 0.6)


func _handle_orbit(event: InputEventMouseMotion) -> void:
	_user_rotated = true
	_yaw_target -= event.relative.x * orbit_speed
	if _underwater:
		_pitch_offset_target = clampf(_pitch_offset_target - event.relative.y * orbit_speed * 30.0,
			-UNDERWATER_PITCH_LIMIT_DEG - UNDERWATER_PITCH_DEG, UNDERWATER_PITCH_LIMIT_DEG - UNDERWATER_PITCH_DEG)
		return
	_pitch_offset_target = clampf(_pitch_offset_target - event.relative.y * orbit_speed * 30.0, -40.0, 15.0)


func _handle_drag_pan(event: InputEventMouseMotion) -> void:
	if _underwater:
		# Inside the pen there is nowhere to pan to: a drag looks round.
		_handle_orbit(event)
		return
	var viewport_h := maxf(get_viewport().get_visible_rect().size.y, 1.0)
	var metres_per_px := 2.0 * _distance * tan(deg_to_rad(fov) * 0.5) / viewport_h
	var move := Vector2(-event.relative.x, -event.relative.y).rotated(-_yaw) * metres_per_px
	_set_focus_target(_focus_target + Vector3(move.x, 0.0, move.y))


func _handle_key(event: InputEventKey) -> void:
	if not _letter_keys_allowed():
		return
	if InputMap.has_action(BOOKMARK_ACTION) and event.is_action_pressed(BOOKMARK_ACTION, false, true):
		cycle_bookmark()
		get_viewport().set_input_as_handled()
		return
	match event.keycode:
		KEY_HOME:
			recentre()
			get_viewport().set_input_as_handled()
		KEY_T:
			set_top_down(not _top_down)
			get_viewport().set_input_as_handled()


func _process_keys(delta: float) -> void:
	if not input_enabled:
		return
	var pan := Vector2.ZERO
	var rotate := 0.0
	if _letter_keys_allowed():
		pan.x += float(Input.is_physical_key_pressed(KEY_D)) - float(Input.is_physical_key_pressed(KEY_A))
		pan.y += float(Input.is_physical_key_pressed(KEY_S)) - float(Input.is_physical_key_pressed(KEY_W))
		rotate += float(Input.is_physical_key_pressed(KEY_E)) - float(Input.is_physical_key_pressed(KEY_Q))
	if _arrow_keys_allowed():
		pan.x += float(Input.is_physical_key_pressed(KEY_RIGHT)) - float(Input.is_physical_key_pressed(KEY_LEFT))
		pan.y += float(Input.is_physical_key_pressed(KEY_DOWN)) - float(Input.is_physical_key_pressed(KEY_UP))
	if pan != Vector2.ZERO and _underwater:
		# Inside the pen the pan keys look round: left and right turn the
		# view round the pen, up and down tilt it.
		rotate += pan.x
		_pitch_offset_target = clampf(_pitch_offset_target - pan.y * UNDERWATER_TILT_SPEED * delta,
			-UNDERWATER_PITCH_LIMIT_DEG - UNDERWATER_PITCH_DEG, UNDERWATER_PITCH_LIMIT_DEG - UNDERWATER_PITCH_DEG)
	elif pan != Vector2.ZERO:
		var move := pan.normalized().rotated(-_yaw) * pan_speed * _distance_target * delta
		_set_focus_target(_focus_target + Vector3(move.x, 0.0, move.y))
		_cancel_fly()
	if rotate != 0.0:
		_user_rotated = true
		_yaw_target += rotate * rotate_speed * delta


## Letters (WASD, Q/E, T, Home) pass unless a text field has focus or a
## modifier is held (Cmd/Ctrl shortcuts belong to the UI).
func _letter_keys_allowed() -> bool:
	if not input_enabled or not is_inside_tree():
		return false
	if Input.is_key_pressed(KEY_CTRL) or Input.is_key_pressed(KEY_META) or Input.is_key_pressed(KEY_ALT):
		return false
	var owner_control := get_viewport().gui_get_focus_owner()
	return not (owner_control is LineEdit or owner_control is TextEdit)


## Arrows pass only when no Control has focus (they walk tab bars and lists).
func _arrow_keys_allowed() -> bool:
	return _letter_keys_allowed() and get_viewport().gui_get_focus_owner() == null


func _process_idle_orbit(delta: float) -> void:
	_idle_time += delta
	if auto_orbit_enabled and _idle_time >= IDLE_ORBIT_DELAY and not _orbiting:
		_yaw_target += auto_orbit_speed * delta


## Zoom by a factor toward the ground point under the cursor.
func _zoom_by(factor: float, screen_pos: Vector2) -> void:
	if _underwater:
		if factor > 1.0:
			set_zoom_level(ZoomLevel.FARM)
		return
	var old := _distance_target
	var new_distance := clampf(old * factor, MIN_DISTANCE, MAX_DISTANCE)
	if factor < 1.0 and old <= MIN_DISTANCE + 0.01:
		set_zoom_level(ZoomLevel.UNDERWATER)
		return
	var hit := pick_ground(screen_pos)
	if hit.is_finite():
		_set_focus_target(_focus_target + (Vector3(hit.x, 0.0, hit.z) - _focus_target) * (1.0 - new_distance / old))
	_distance_target = new_distance
	_update_zoom_band(_band_for(new_distance))


## World point under a screen position: the first terrain or water hit along
## the camera ray (ray-marched over the heightfield), or INF on a miss.
func pick_ground(screen_pos: Vector2) -> Vector3:
	var origin := project_ray_origin(screen_pos)
	var dir := project_ray_normal(screen_pos)
	var water_y := _water_level()
	var travelled := 0.0
	var p := origin
	while travelled < 8000.0:
		var ground := maxf(_ground(p.x, p.z), water_y)
		var gap := p.y - ground
		if gap <= 0.5:
			return Vector3(p.x, ground, p.z)
		var step := clampf(gap * 0.5, 2.0, 200.0)
		p += dir * step
		travelled += step
	return Vector3.INF


# ── Helpers ───────────────────────────────────────────────────────────────────

func _set_focus_target(p: Vector3) -> void:
	_focus_target = _clamp_to_world(Vector3(p.x, 0.0, p.z))


func _cancel_fly() -> void:
	if _fly_tween and _fly_tween.is_valid():
		_fly_tween.kill()
	_fly_tween = null


func _band_for(distance: float) -> ZoomLevel:
	if distance >= COAST_BAND:
		return ZoomLevel.COAST
	if distance < FARM_BAND:
		return ZoomLevel.FARM
	return ZoomLevel.FJORD


func _update_zoom_band(level: ZoomLevel) -> void:
	if level != current_zoom:
		current_zoom = level
		zoom_level_changed.emit(level)


static func _nearest_angle(from: float, to: float) -> float:
	return from + wrapf(to - from, -PI, PI)

