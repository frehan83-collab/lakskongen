extends SceneTree
## Compiles shaders under a real renderer (review F958).
##
## The --headless dummy renderer parses no shader code, so this runs with a
## window: each shader goes on a node that stays on screen for a number of
## DRAWN frames, and a broken one makes Godot print "SHADER ERROR". The
## markers around each shader go to stderr, like Godot's errors, so they stay
## in order with them and shader_check.py can attribute every error to its
## file. A shader that cannot be loaded or is never drawn is reported and the
## probe goes on to the next one.
##
## Usage: godot --path <project> --script shader_compile_probe.gd -- res://a.gdshader ...

## Frames drawn with each shader on screen. Counted on frame_post_draw, not
## per process tick, so a slow software rasterizer (Mesa's llvmpipe/lavapipe
## under Xvfb on CI) has drawn the shader before the probe moves on.
const FRAMES_PER_SHADER := 8
## Process ticks a shader may wait for those frames. A window that never
## draws must fail the shader, not pass it.
const MAX_TICKS_PER_SHADER := 2000

var _paths := PackedStringArray()
var _index := -1
var _holder: Node = null
var _frames_drawn := 0
var _drawn_at_start := 0
var _ticks := 0


func _initialize() -> void:
	_paths = OS.get_cmdline_user_args()
	RenderingServer.frame_post_draw.connect(_on_frame_post_draw, Object.CONNECT_DEFERRED)
	var camera := Camera3D.new()
	camera.position = Vector3(0, 0, 3)
	camera.current = true
	root.add_child(camera)


func _on_frame_post_draw() -> void:
	_frames_drawn += 1


func _process(_delta: float) -> bool:
	if _holder != null and _frames_drawn - _drawn_at_start < FRAMES_PER_SHADER:
		_ticks += 1
		if _ticks < MAX_TICKS_PER_SHADER:
			return false
		printerr("SHADER_CHECK_NOT_DRAWN %s only %d of %d frames were drawn in %d process ticks" % [
			_paths[_index], _frames_drawn - _drawn_at_start, FRAMES_PER_SHADER, _ticks])
	_finish_current()
	_index += 1
	if _index >= _paths.size():
		printerr("SHADER_CHECK_DONE count=%d" % _paths.size())
		return true
	_start(_paths[_index])
	return false


func _start(path: String) -> void:
	printerr("SHADER_CHECK_BEGIN " + path)
	_ticks = 0
	_drawn_at_start = _frames_drawn
	# Bypass the cache so every shader is parsed here, inside its markers.
	var shader := ResourceLoader.load(path, "Shader", ResourceLoader.CACHE_MODE_IGNORE) as Shader
	if shader == null:
		printerr("SHADER_CHECK_LOAD_FAILED " + path)
		return
	var material := ShaderMaterial.new()
	material.shader = shader
	_holder = _node_for(shader.get_mode(), material)
	root.add_child(_holder)


func _finish_current() -> void:
	if _index < 0:
		return
	if _holder != null:
		_holder.free()
		_holder = null
	printerr("SHADER_CHECK_END " + _paths[_index])


func _node_for(mode: Shader.Mode, material: ShaderMaterial) -> Node:
	if mode == Shader.MODE_CANVAS_ITEM:
		var rect := ColorRect.new()
		rect.size = Vector2(64, 64)
		rect.material = material
		return rect
	if mode == Shader.MODE_SKY:
		var sky := Sky.new()
		sky.sky_material = material
		var environment := Environment.new()
		environment.background_mode = Environment.BG_SKY
		environment.sky = sky
		var world := WorldEnvironment.new()
		world.environment = environment
		return world
	if mode == Shader.MODE_PARTICLES:
		var particles := GPUParticles3D.new()
		particles.process_material = material
		particles.draw_pass_1 = QuadMesh.new()
		return particles
	if mode == Shader.MODE_FOG:
		var fog := FogVolume.new()
		fog.material = material
		return fog
	var mesh := MeshInstance3D.new()
	mesh.mesh = QuadMesh.new()
	mesh.material_override = material
	return mesh
