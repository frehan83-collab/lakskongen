extends SceneTree
## Compiles shaders under a real renderer (review F958).
##
## The --headless dummy renderer parses no shader code, so this runs with a
## window: each shader goes on a node that is drawn for a few frames, and a
## broken one makes Godot print "SHADER ERROR" to stderr. The markers around
## each shader go to stderr as well, so they stay in order with the errors and
## shader_check.py can attribute every error to its file.
##
## Usage: godot --path <project> --script shader_compile_probe.gd -- res://a.gdshader ...

const FRAMES_PER_SHADER := 3

var _paths := PackedStringArray()
var _index := -1
var _frames_left := 0
var _holder: Node = null


func _initialize() -> void:
	_paths = OS.get_cmdline_user_args()
	var camera := Camera3D.new()
	camera.position = Vector3(0, 0, 3)
	camera.current = true
	root.add_child(camera)


func _process(_delta: float) -> bool:
	if _frames_left > 0:
		_frames_left -= 1
		return false
	_finish_current()
	_index += 1
	if _index >= _paths.size():
		printerr("SHADER_CHECK_DONE count=%d" % _paths.size())
		return true
	_start(_paths[_index])
	return false


func _start(path: String) -> void:
	printerr("SHADER_CHECK_BEGIN " + path)
	# Bypass the cache so every shader is parsed here, inside its markers.
	var shader := ResourceLoader.load(path, "Shader", ResourceLoader.CACHE_MODE_IGNORE) as Shader
	if shader == null:
		printerr("SHADER_CHECK_LOAD_FAILED " + path)
		return
	var material := ShaderMaterial.new()
	material.shader = shader
	_holder = _node_for(shader.get_mode(), material)
	root.add_child(_holder)
	_frames_left = FRAMES_PER_SHADER


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
