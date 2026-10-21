extends Node

## Save-slot pictures (roadmap U2-08). After every save (the player's, a
## quicksave, an autosave) it renders the world the save was taken in, from
## the game camera's own view but without any UI (a small SubViewport that
## shares the world), and writes it beside the save as
## "<save>.thumb.png" (SaveLoadManager.ThumbnailPathFor). The save and load
## lists show it (SaveLoadMenu).
##
## Headless there are no frames: nothing is written, and the lists show the
## empty picture tile. Tests hand in an image through image_source.
##
## The picture is of the world the save was made in, or none: the camera is
## taken when the save is reported, and if it leaves the tree before the
## picture is read (the scene changed), nothing is written. Code that leaves
## the game right after a save (the pause menu's "Lagre og avslutt") waits for
## the picture first: wait_until_written() (U2-08 review).

## The picture's size (16:9); rendered at RENDER_SCALE times this, then
## filtered down so the water and the pens stay crisp.
const SIZE := Vector2i(384, 216)
const RENDER_SCALE := 2
## Frames the picture's viewport renders before it is read.
const SETTLE_FRAMES := 8
## The longest a leave waits for a picture in flight (ms); it takes
## SETTLE_FRAMES frames, about 150 ms at 60 fps.
const WAIT_TIMEOUT_MS := 2000

signal thumbnail_saved(path: String)

## Test seam: a Callable returning an Image stands in for the render.
var image_source: Callable = Callable()
## Pictures written since launch (tests read it).
var written: int = 0
## Pictures given up because their world left the tree (tests read it).
var abandoned: int = 0

## Pictures promised (a save reported) but not yet written or given up.
var _pending: int = 0

var _mgr: Node = null


func _ready() -> void:
	# The pause menu saves while the game stands still: keep working.
	process_mode = Node.PROCESS_MODE_ALWAYS
	_mgr = get_node_or_null("/root/SaveLoadManager")
	if _mgr != null and _mgr.has_signal("GameSaved"):
		_mgr.GameSaved.connect(_on_game_saved)


func _on_game_saved(_save_name: String, file_path: String) -> void:
	if _mgr == null:
		return
	# The camera now, while the saved game is still the one on screen (held by
	# id: it may be freed before the picture is read).
	var cam := get_viewport().get_camera_3d()
	if not image_source.is_valid() and (DisplayServer.get_name() == "headless" or cam == null):
		return
	_pending += 1
	_write_pending.call_deferred(str(_mgr.ThumbnailPathFor(file_path.get_file())), cam.get_instance_id() if cam != null else 0)


func _write_pending(path: String, cam_id: int) -> void:
	await write_for(path, cam_id)
	_pending = maxi(_pending - 1, 0)


## True while a save's picture is still being rendered.
func is_busy() -> bool:
	return _pending > 0


## Wait until the pictures in flight are written (or given up), at most
## `timeout_ms`. True when none is left.
func wait_until_written(timeout_ms: int = WAIT_TIMEOUT_MS) -> bool:
	var deadline := Time.get_ticks_msec() + timeout_ms
	while _pending > 0 and Time.get_ticks_msec() < deadline:
		await get_tree().process_frame
	return _pending == 0


## Render the world the camera `cam_id` looks at (0: the game camera now) and
## write it to `path` (a few frames later). Nothing is written when that
## camera has left the tree by then: no picture beats one of another world.
func write_for(path: String, cam_id: int = 0) -> bool:
	var img: Image = await capture_world(cam_id)
	if cam_id != 0 and _live_camera(cam_id) == null:
		abandoned += 1
		return false
	if img == null or img.is_empty():
		return false
	if img.get_size() != SIZE:
		img.resize(SIZE.x, SIZE.y, Image.INTERPOLATE_LANCZOS)
	if img.save_png(path) != OK:
		push_warning("SaveThumbnails: could not write %s" % path)
		return false
	written += 1
	thumbnail_saved.emit(path)
	return true


## The camera with this instance id while it is alive and in the tree, else null.
static func _live_camera(cam_id: int) -> Camera3D:
	var obj: Object = instance_from_id(cam_id) if cam_id != 0 else null
	var cam := obj as Camera3D
	return cam if cam != null and cam.is_inside_tree() else null


## The world the camera `cam_id` (0: the game camera) sees, without the UI
## (null headless, without a 3D camera, or when the camera leaves the tree
## before the picture is read).
func capture_world(cam_id: int = 0) -> Image:
	if image_source.is_valid():
		return image_source.call() as Image
	if DisplayServer.get_name() == "headless":
		return null
	var cam: Camera3D = _live_camera(cam_id) if cam_id != 0 else get_viewport().get_camera_3d()
	if cam == null or not cam.is_inside_tree():
		return null
	cam_id = cam.get_instance_id()
	var vp := SubViewport.new()
	vp.size = SIZE * RENDER_SCALE
	vp.world_3d = cam.get_world_3d()
	# No MSAA of its own (RenderQuality owns render settings): drawn at twice
	# the size and filtered down, the picture is supersampled anyway. It runs
	# for SETTLE_FRAMES frames: the sky's reduced-resolution cloud passes and
	# other per-viewport buffers start empty in a new viewport (the first frame
	# reads grey and washed out).
	vp.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	var eye := Camera3D.new()
	eye.fov = cam.fov
	eye.near = cam.near
	eye.far = cam.far
	eye.cull_mask = cam.cull_mask
	eye.keep_aspect = cam.keep_aspect
	eye.environment = cam.environment
	# No depth of field: a picture this small wants every pen sharp.
	eye.attributes = null
	vp.add_child(eye)
	add_child(vp)
	eye.global_transform = cam.global_transform
	eye.current = true
	for i in SETTLE_FRAMES:
		await RenderingServer.frame_post_draw
		cam = _live_camera(cam_id)
		if cam == null:
			vp.queue_free()
			return null
		eye.global_transform = cam.global_transform
		eye.fov = cam.fov
	var img := vp.get_texture().get_image()
	vp.queue_free()
	return img
