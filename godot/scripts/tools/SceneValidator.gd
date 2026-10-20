extends SceneTree

## Headless scene-load validator for Lakskongen.
##
## Walks every `.tscn` under one or more project roots, then for each scene:
##   1. verifies every `[ext_resource] path="res://..."` points at an existing file
##   2. loads the scene as a `PackedScene`
##   3. instantiates it once to surface missing-script / bad-resource errors
##
## Validation runs on the first frame (not in `_init`) so project autoload
## singletons are registered; otherwise scripts that reference autoloads such
## as `LakskongenTheme` or `SimBridge` report spurious compile errors.
##
## Reports every problem and exits non-zero when a real failure is found.
##
## Usage (normally wrapped by tools/validate_scenes.sh):
##   godot --headless --path godot \
##       --script res://scripts/tools/SceneValidator.gd -- [roots...]
##
## Defaults to the whole project (`res://`), skipping `res://addons`.

const SKIP_DIRS: Array[String] = ["res://addons"]

var _scenes: Array[String] = []
var _failures: Array[String] = []
var _roots: Array[String] = []
var _empty_roots: Array[String] = []
var _started := false

func _init() -> void:
	_roots = _resolve_roots()
	for root in _roots:
		var before := _scenes.size()
		_collect(root)
		if _scenes.size() == before:
			_empty_roots.append(root)
	_scenes.sort()

func _process(_delta: float) -> bool:
	if _started:
		return true
	_started = true
	print("[SceneValidator] scanning %d scene(s) under %s" % [
		_scenes.size(), ", ".join(_roots)
	])
	for root in _empty_roots:
		# A root with nothing to validate is a mistake, not a pass (a mistyped
		# or missing root used to be skipped silently).
		_failures.append("%s: no scenes found" % root)
	for scene_path in _scenes:
		_validate(scene_path)
	_report()
	quit(1 if not _failures.is_empty() else 0)
	return true

func _resolve_roots() -> Array[String]:
	var args := OS.get_cmdline_user_args()
	if args.is_empty():
		return ["res://"]
	var roots: Array[String] = []
	for a in args:
		roots.append(a if a.begins_with("res://") else "res://" + a)
	return roots

func _collect(dir_path: String) -> void:
	for skip in SKIP_DIRS:
		if dir_path == skip or dir_path.begins_with(skip + "/"):
			return
	if not DirAccess.dir_exists_absolute(dir_path):
		return
	for file_name in DirAccess.get_files_at(dir_path):
		if file_name.get_extension().to_lower() == "tscn":
			_scenes.append(dir_path.path_join(file_name))
	for sub in DirAccess.get_directories_at(dir_path):
		_collect(dir_path.path_join(sub))

func _validate(scene_path: String) -> void:
	var text := FileAccess.get_file_as_string(scene_path)
	if text.is_empty():
		_failures.append("%s: cannot read scene file" % scene_path)
		return

	for res in _ext_resources(text):
		var dep: String = res["path"]
		if dep.is_empty():
			continue
		if not FileAccess.file_exists(dep):
			_failures.append("%s: missing ext_resource path -> %s" % [scene_path, dep])
			continue
		if res["type"] == "Mesh":
			_check_mesh(scene_path, dep)

	var packed := ResourceLoader.load(
		scene_path, "PackedScene", ResourceLoader.CACHE_MODE_IGNORE
	) as PackedScene
	if packed == null:
		_failures.append("%s: failed to load as PackedScene" % scene_path)
		return

	var instance := packed.instantiate()
	if instance == null:
		_failures.append("%s: PackedScene.instantiate() returned null" % scene_path)
	else:
		instance.free()

func _check_mesh(scene_path: String, mesh_path: String) -> void:
	var mesh := ResourceLoader.load(mesh_path, "Mesh", ResourceLoader.CACHE_MODE_IGNORE) as Mesh
	if mesh == null:
		_failures.append("%s: failed to load Mesh -> %s" % [scene_path, mesh_path])
		return
	if mesh.get_surface_count() == 0:
		_failures.append(
			"%s: Mesh has no surfaces (mis-declared PrimitiveMesh?) -> %s"
			% [scene_path, mesh_path]
		)

func _ext_resources(text: String) -> Array[Dictionary]:
	var out: Array[Dictionary] = []
	for raw_line in text.split("\n"):
		var line := raw_line.strip_edges()
		if not line.begins_with("[ext_resource"):
			continue
		out.append({
			"type": _extract_attr(line, "type"),
			"path": _extract_attr(line, "path"),
		})
	return out

func _extract_attr(line: String, attr: String) -> String:
	var needle := attr + "=\""
	var at := line.find(needle)
	if at < 0:
		return ""
	var start := at + needle.length()
	var end := line.find("\"", start)
	if end < 0:
		return ""
	return line.substr(start, end - start)

func _report() -> void:
	print("")
	if _failures.is_empty():
		print("[SceneValidator] PASS — all %d scene(s) loaded cleanly." % _scenes.size())
		print("SCENE_VALIDATION_RESULT failures=0")
		return
	var scenes_with_issues := {}
	for failure in _failures:
		scenes_with_issues[failure.get_slice(": ", 0)] = true
	print("[SceneValidator] FAIL — %d issue(s) across %d scene(s):" % [
		_failures.size(), scenes_with_issues.size()
	])
	for failure in _failures:
		print("  x " + failure)
	print("SCENE_VALIDATION_RESULT failures=%d" % _failures.size())
