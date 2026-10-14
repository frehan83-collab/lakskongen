extends SceneTree

## Regenerates the Kyst & Stål theme resources from res://theme/tokens.json.
##
## Run through tools/sync_tokens.py --write (it copies design/tokens.json first),
## or directly:
##   godot --headless --path godot --script res://scripts/tools/build_theme.gd
##
## Writes:
##   res://theme/fonts/<key>.tres          FontVariations (Inter tnum, display)
##   res://theme/lakskongen_theme.tres     Ink — the project theme (gui/theme/custom)
##   res://theme/lakskongen_chart_theme.tres  Chart — document panels
## Each theme is stamped with the SHA-256 of tokens.json and of the builder
## sources, so tools/sync_tokens.py --check can detect a stale theme without Godot.

const Builder := preload("res://theme/theme_builder.gd")

## Builder sources hashed into the stamp, in this order (sync_tokens.py mirrors it).
const BUILDER_SOURCES: Array[String] = [
	"res://theme/theme_tokens.gd",
	"res://theme/theme_style.gd",
	"res://theme/theme_controls.gd",
	"res://theme/theme_builder.gd",
	"res://scripts/tools/build_theme.gd",
]

const FONT_ORDER: Array[String] = ["ui_regular", "ui_medium", "ui_semibold", "ui_bold", "display", "display_medium", "ui_italic"]


func _init() -> void:
	var code := _run()
	quit(code)


func _run() -> int:
	var tokens := Builder.read_tokens(Builder.TOKENS_PATH)
	if tokens.is_empty():
		push_error("build_theme: %s is missing or invalid" % Builder.TOKENS_PATH)
		return 1
	var tokens_sha := FileAccess.get_sha256(Builder.TOKENS_PATH)
	var builder_sha := _builder_sha()
	if builder_sha.is_empty():
		return 1

	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(Builder.FONT_DIR))
	var fonts := Builder.make_fonts(tokens)
	for key in FONT_ORDER:
		var font: Resource = fonts[key]
		var path := "%s/%s.tres" % [Builder.FONT_DIR, key]
		_assign_ids(font, key)
		if ResourceSaver.save(font, path) != OK:
			push_error("build_theme: could not save %s" % path)
			return 1
		font.take_over_path(path)

	var targets := {
		Builder.SURFACE_INK: Builder.PROJECT_THEME_PATH,
		Builder.SURFACE_CHART: Builder.CHART_THEME_PATH,
	}
	for surface in Builder.SURFACES:
		var theme := Builder.build(tokens, surface, fonts)
		theme.set_meta(Builder.META_TOKENS_SHA, tokens_sha)
		theme.set_meta(Builder.META_BUILDER_SHA, builder_sha)
		_assign_theme_ids(theme, surface)
		var path: String = targets[surface]
		if ResourceSaver.save(theme, path) != OK:
			push_error("build_theme: could not save %s" % path)
			return 1
		print("build_theme: wrote %s (%s, %d types, display font: %s)" % [
			path, surface, theme.get_type_list().size(),
			"Barlow Condensed" if Builder.has_display_font(tokens) else "Inter fallback"])
	print("build_theme: tokens sha256 %s, builder sha256 %s" % [tokens_sha, builder_sha])
	return 0


## SHA-256 over the concatenated builder sources (bytes, in BUILDER_SOURCES order).
func _builder_sha() -> String:
	var ctx := HashingContext.new()
	ctx.start(HashingContext.HASH_SHA256)
	for path in BUILDER_SOURCES:
		if not FileAccess.file_exists(path):
			push_error("build_theme: builder source missing: %s" % path)
			return ""
		ctx.update(FileAccess.get_file_as_bytes(path))
	return ctx.finish().hex_encode()


## Deterministic sub-resource ids, so a rebuild with unchanged tokens rewrites
## identical files (no churn in review diffs).
func _assign_theme_ids(theme: Theme, prefix: String) -> void:
	var seen := {}
	var n := 0
	var types := Array(theme.get_type_list())
	types.sort()
	for type_name in types:
		var boxes := Array(theme.get_stylebox_list(type_name))
		boxes.sort()
		for item in boxes:
			n = _id(theme.get_stylebox(item, type_name), "%s_sb" % prefix, n, seen)
		var icons := Array(theme.get_icon_list(type_name))
		icons.sort()
		for item in icons:
			n = _id(theme.get_icon(item, type_name), "%s_icon" % prefix, n, seen)


func _id(res: Resource, prefix: String, n: int, seen: Dictionary) -> int:
	if res == null or seen.has(res.get_instance_id()) or not res.resource_path.is_empty():
		return n
	seen[res.get_instance_id()] = true
	res.resource_scene_unique_id = "%s_%d" % [prefix, n]
	return n + 1


func _assign_ids(font: Resource, key: String) -> void:
	if font is FontVariation:
		for i in (font as FontVariation).fallbacks.size():
			var fb: Font = (font as FontVariation).fallbacks[i]
			if fb != null and fb.resource_path.is_empty():
				fb.resource_scene_unique_id = "%s_fallback_%d" % [key, i]
