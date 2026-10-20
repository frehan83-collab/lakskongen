class_name MultiMeshPack
extends RefCounted
## MultiMesh instance data as one float buffer (MultiMesh.buffer): per
## instance a 3D transform (12 floats, the rows of the 3 x 4 matrix), then
## its colour (4) when the MultiMesh uses colours, then its custom data (4)
## when it uses custom data. The site kit uploads whole buffers in one call
## (cheaper than an instance setter per instance and frame) and the UI-flow
## tests read them back the same way, which also works on the headless
## renderer, where the per-instance getters return defaults.


## Floats per instance of a 3D MultiMesh.
static func stride(mm: MultiMesh) -> int:
	return 12 + (4 if mm.use_colors else 0) + (4 if mm.use_custom_data else 0)


## The buffer for `transforms`, with `colors` and `customs` (one per
## instance, used only when the MultiMesh takes them).
static func pack(mm: MultiMesh, transforms: Array[Transform3D], colors: Array = [], customs: Array = []) -> PackedFloat32Array:
	var s := stride(mm)
	var buf := PackedFloat32Array()
	buf.resize(transforms.size() * s)
	for k in transforms.size():
		var i := k * s
		write_transform(buf, i, transforms[k])
		var j := i + 12
		if mm.use_colors:
			var c: Color = colors[k] if k < colors.size() else Color.WHITE
			buf[j] = c.r
			buf[j + 1] = c.g
			buf[j + 2] = c.b
			buf[j + 3] = c.a
			j += 4
		if mm.use_custom_data:
			var d: Color = customs[k] if k < customs.size() else Color(0, 0, 0, 0)
			buf[j] = d.r
			buf[j + 1] = d.g
			buf[j + 2] = d.b
			buf[j + 3] = d.a
	return buf


## Set every instance of `mm` at once (instance_count follows the arrays).
static func upload(mm: MultiMesh, transforms: Array[Transform3D], colors: Array = [], customs: Array = []) -> void:
	if mm.instance_count != transforms.size():
		mm.instance_count = transforms.size()
	if transforms.is_empty():
		return
	mm.buffer = pack(mm, transforms, colors, customs)


static func write_transform(buf: PackedFloat32Array, i: int, t: Transform3D) -> void:
	var b := t.basis
	buf[i] = b.x.x
	buf[i + 1] = b.y.x
	buf[i + 2] = b.z.x
	buf[i + 3] = t.origin.x
	buf[i + 4] = b.x.y
	buf[i + 5] = b.y.y
	buf[i + 6] = b.z.y
	buf[i + 7] = t.origin.y
	buf[i + 8] = b.x.z
	buf[i + 9] = b.y.z
	buf[i + 10] = b.z.z
	buf[i + 11] = t.origin.z


## Instance k's transform as uploaded.
static func transform_of(mm: MultiMesh, k: int) -> Transform3D:
	var buf := mm.buffer
	var i := k * stride(mm)
	if i + 12 > buf.size():
		return Transform3D()
	return Transform3D(Vector3(buf[i], buf[i + 4], buf[i + 8]), Vector3(buf[i + 1], buf[i + 5], buf[i + 9]),
		Vector3(buf[i + 2], buf[i + 6], buf[i + 10]), Vector3(buf[i + 3], buf[i + 7], buf[i + 11]))


## Instance k's colour as uploaded (white without colours).
static func color_of(mm: MultiMesh, k: int) -> Color:
	if not mm.use_colors:
		return Color.WHITE
	var buf := mm.buffer
	var i := k * stride(mm) + 12
	return Color(buf[i], buf[i + 1], buf[i + 2], buf[i + 3]) if i + 4 <= buf.size() else Color.WHITE


## Instance k's custom data as uploaded (zero without custom data).
static func custom_of(mm: MultiMesh, k: int) -> Color:
	if not mm.use_custom_data:
		return Color(0, 0, 0, 0)
	var buf := mm.buffer
	var i := k * stride(mm) + 12 + (4 if mm.use_colors else 0)
	return Color(buf[i], buf[i + 1], buf[i + 2], buf[i + 3]) if i + 4 <= buf.size() else Color(0, 0, 0, 0)
