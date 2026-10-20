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

## The picture's size (16:9); rendered at RENDER_SCALE times this, then
## filtered down so the water and the pens stay crisp.
const SIZE := Vector2i(384, 216)
const RENDER_SCALE := 2
## Frames the picture's viewport renders before it is read.
const SETTLE_FRAMES := 8

signal thumbnail_saved(path: String)

## Test seam: a Callable returning an Image stands in for the render.
var image_source: Callable = Callable()
## Pictures written since launch (tests read it).
var written: int = 0

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
	write_for.call_deferred(str(_mgr.ThumbnailPathFor(file_path.get_file())))


## Render the world and write it to `path` (a frame or two later).
func write_for(path: String) -> bool:
	var img: Image = await capture_world()
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


## The world as the game camera sees it, without the UI (null headless or
## without a 3D camera).
func capture_world() -> Image:
	if image_source.is_valid():
		return image_source.call() as Image
	if DisplayServer.get_name() == "headless":
		return null
	var cam := get_viewport().get_camera_3d()
	if cam == null:
		return null
	var vp := SubViewport.new()
	vp.size = SIZE * RENDER_SCALE
	vp.world_3d = get_viewport().find_world_3d()
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
		if is_instance_valid(cam):
			eye.global_transform = cam.global_transform
			eye.fov = cam.fov
	var img := vp.get_texture().get_image()
	vp.queue_free()
	return img
